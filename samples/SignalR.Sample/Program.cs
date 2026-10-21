using System;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using SignalR.Library;

namespace SignalR.Sample
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length < 1 || args.Length > 2 || (args[0] != "server" && args[0] != "client"))
            {
                Console.WriteLine("Usage: SignalR.Sample.exe server|client [http://localhost:8080/]");
                return 1;
            }

            var url = args.Length == 2 ? args[1] : "http://localhost:8080/";
            try
            {
                if (args[0] == "server")
                {
                    using (var server = new SignalRService(url))
                    {
                        server.StartServer();
                        Console.WriteLine("Server started. Press Enter to stop.");
                        Console.ReadLine();
                    }
                }
                else
                {
                    using (var client = new SignalRClient(url))
                    {
                        var chat = client.CreateHubProxy("ChatHub");
                        var status = client.CreateHubProxy("StatusHub");
                        using (chat.On<ChatMessage>("Received", message =>
                            Console.WriteLine($"[{message.SentAtUtc:O}] {message.Text}")))
                        {
                            await client.ConnectAsync();
                            Console.WriteLine("Server UTC: " + await status.Invoke<DateTime>("UtcNow"));
                            Console.WriteLine("Type a message, or an empty line to quit.");
                            string text;
                            while (!string.IsNullOrEmpty(text = Console.ReadLine()))
                                await chat.Invoke("Send", new ChatMessage { Text = text });
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }
    }
}
