using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using SignalR.Library;
using SignalR.Sample;

internal static class Program
{
    private static int _checks;

    private static async Task<int> Main()
    {
        try
        {
            ValidateArgumentsAndLifetime();
            await CheckTransport();
            Console.WriteLine($"PASS: {_checks} assertions (real HTTP SignalR connections).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void ValidateArgumentsAndLifetime()
    {
        Throws<ArgumentNullException>(() => new SignalRClient(null));
        Throws<ArgumentException>(() => new SignalRClient("relative/path"));
        Throws<ArgumentException>(() => new SignalRClient("ftp://localhost/"));
        Throws<ArgumentNullException>(() => new SignalRService(null));
        Throws<ArgumentException>(() => new SignalRService("http://localhost/?query=1"));
        Throws<ArgumentException>(() => new SignalRService("http://user:secret@localhost/"));
        using (var client = new SignalRClient("http://localhost:8080/"))
        {
            Throws<ArgumentException>(() => client.CreateHubProxy(" "));
            client.Dispose();
            client.Dispose();
            Throws<ObjectDisposedException>(() => client.ConnectAsync());
            Throws<ObjectDisposedException>(() => client.CreateHubProxy("ChatHub"));
            Assert(!client.IsConnected, "Disposed client must be disconnected.");
        }
        using (var server = new SignalRService("http://localhost:8080/"))
        {
            Throws<InvalidOperationException>(() => server.GetHubContext<ChatHub>());
            server.StopServer();
            server.Dispose();
            server.Dispose();
            Throws<ObjectDisposedException>(() => server.StartServer());
            Assert(!server.IsRunning, "Disposed server must be stopped.");
        }
    }

    private static async Task CheckTransport()
    {
        // The OS selects test ports; the brief release-to-bind race is reported as a test failure.
        var url = FreeUrl();
        var otherUrl = FreeUrl();
        while (otherUrl == url) otherUrl = FreeUrl();

        var failStartup = true;
        using (var server = new SignalRService(url, app =>
        {
            if (failStartup) throw new InvalidOperationException("Expected startup failure.");
        }))
        using (var otherServer = new SignalRService(otherUrl))
        {
            Throws<Exception>(() => server.StartServer());
            Assert(!server.IsRunning, "Failed startup must not leave a running server.");
            failStartup = false;
            server.StartServer();
            server.StartServer();
            Assert(server.IsRunning, "Server must start after a failed attempt.");
            otherServer.StartServer();

            using (var first = new SignalRClient(url))
            using (var second = new SignalRClient(url))
            using (var isolated = new SignalRClient(otherUrl))
            using (var firstInbox = new BlockingCollection<ChatMessage>())
            using (var secondInbox = new BlockingCollection<ChatMessage>())
            using (var isolatedInbox = new BlockingCollection<ChatMessage>())
            {
                var chat = first.CreateHubProxy("ChatHub");
                var status = first.CreateHubProxy("StatusHub");
                var secondChat = second.CreateHubProxy("ChatHub");
                var isolatedChat = isolated.CreateHubProxy("ChatHub");
                var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                first.IsConnectedChanged += value =>
                {
                    if (value) connected.TrySetResult(true);
                    else disconnected.TrySetResult(true);
                };

                using (chat.On<ChatMessage>("Received", firstInbox.Add))
                using (secondChat.On<ChatMessage>("Received", secondInbox.Add))
                using (isolatedChat.On<ChatMessage>("Received", isolatedInbox.Add))
                {
                    await Timeout(Task.WhenAll(first.ConnectAsync(), second.ConnectAsync(), isolated.ConnectAsync()));
                    await Timeout(connected.Task);
                    Assert(first.IsConnected && !string.IsNullOrEmpty(first.ConnectionId), "Connection state and ID must be available.");
                    await Timeout(first.ConnectAsync());
                    Throws<InvalidOperationException>(() => first.CreateHubProxy("LateHub"));
                    Assert(await Timeout(chat.Invoke<string>("Echo", "hello")) == "hello", "Typed RPC result must match.");
                    Assert(Math.Abs((await Timeout(status.Invoke<DateTime>("UtcNow")) - DateTime.UtcNow).TotalSeconds) < 30,
                        "Second hub must return the current UTC time.");

                    await Timeout(chat.Invoke("Send", new ChatMessage { Text = "broadcast" }));
                    var message = Take(firstInbox, "broadcast");
                    Take(secondInbox, "broadcast");
                    Assert(message.SentAtUtc != default(DateTime), "Typed DTO must include the server timestamp.");

                    var invalidRejected = false;
                    try { await Timeout(chat.Invoke("Send", new ChatMessage { Text = " " })); }
                    catch (HubException) { invalidRejected = true; }
                    Assert(invalidRejected, "Hub validation errors must reach the caller.");

                    var context = server.GetHubContext<ChatHub>();
                    await Timeout((Task)context.Clients.Client(first.ConnectionId).Received(new ChatMessage { Text = "targeted" }));
                    await Timeout((Task)context.Clients.All.Received(new ChatMessage { Text = "after-target" }));
                    Take(firstInbox, "targeted");
                    Take(firstInbox, "after-target");
                    Take(secondInbox, "after-target");

                    await Timeout((Task)otherServer.GetHubContext<ChatHub>().Clients.All.Received(new ChatMessage { Text = "isolated" }));
                    Take(isolatedInbox, "isolated");
                    await Timeout((Task)context.Clients.All.Received(new ChatMessage { Text = "primary" }));
                    Take(firstInbox, "primary");
                    Take(secondInbox, "primary");

                    var unexpected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var subscription = chat.On<ChatMessage>("Received", _ => unexpected.TrySetResult(true));
                    subscription.Dispose();
                    await Timeout(chat.Invoke("Send", new ChatMessage { Text = "unsubscribed" }));
                    Take(firstInbox, "unsubscribed");
                    Take(secondInbox, "unsubscribed");
                    Assert(!unexpected.Task.IsCompleted, "Disposed subscription must not receive messages.");

                    first.Disconnect();
                    await Timeout(disconnected.Task);
                    Assert(!first.IsConnected, "Explicit disconnect must update state.");
                    await Timeout(first.ConnectAsync());
                    await Timeout(chat.Invoke("Send", new ChatMessage { Text = "reconnected" }));
                    Take(firstInbox, "reconnected");
                    Take(secondInbox, "reconnected");
                    first.Disconnect();
                    second.Disconnect();
                    isolated.Disconnect();
                    Assert(isolatedInbox.Count == 0, "Independent server messages must not leak across resolvers.");
                }
            }

            server.StopServer();
            server.StopServer();
            Throws<InvalidOperationException>(() => server.GetHubContext<ChatHub>());
            server.StartServer();
            using (var client = new SignalRClient(url))
            {
                var proxy = client.CreateHubProxy("ChatHub");
                await Timeout(client.ConnectAsync());
                Assert(await Timeout(proxy.Invoke<string>("Echo", "restart")) == "restart", "Server restart must accept new connections.");
            }
        }
    }

    private static string FreeUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"http://localhost:{port}/";
    }

    private static ChatMessage Take(BlockingCollection<ChatMessage> inbox, string expected)
    {
        Assert(inbox.TryTake(out var message, TimeSpan.FromSeconds(15)), "Timed out receiving " + expected);
        Assert(message.Text == expected, $"Expected {expected}, received {message.Text}.");
        return message;
    }

    private static async Task Timeout(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20))) != task)
            throw new TimeoutException("SignalR operation exceeded 20 seconds.");
        await task;
    }

    private static async Task<T> Timeout<T>(Task<T> task)
    {
        await Timeout((Task)task);
        return await task;
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new Exception("Expected exception: " + typeof(T).Name);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
}
