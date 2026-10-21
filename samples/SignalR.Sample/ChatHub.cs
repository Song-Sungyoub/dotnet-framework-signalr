using System;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace SignalR.Sample
{
    public sealed class ChatMessage
    {
        public string Text { get; set; }
        public DateTime SentAtUtc { get; set; }
    }

    [HubName("ChatHub")]
    public class ChatHub : Hub
    {
        public string Echo(string text) => text;

        public Task Send(ChatMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Text) || message.Text.Length > 1024)
                throw new HubException("A message of 1 to 1024 characters is required.");

            message.SentAtUtc = DateTime.UtcNow;
            return Clients.All.Received(message);
        }
    }

    [HubName("StatusHub")]
    public class StatusHub : Hub
    {
        public DateTime UtcNow() => DateTime.UtcNow;
    }
}
