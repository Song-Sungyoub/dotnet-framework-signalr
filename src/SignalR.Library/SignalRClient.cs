using System;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;

namespace SignalR.Library
{
    /// <summary>Owns one connection; create and subscribe to all hub proxies before connecting.</summary>
    public sealed class SignalRClient : IDisposable
    {
        private bool _disposed;

        /// <summary>Native connection for headers, credentials, tracing and transport events. This client owns it.</summary>
        public HubConnection Connection { get; }
        public string ConnectionId => Connection.ConnectionId;
        public bool IsConnected => !_disposed && Connection.State == ConnectionState.Connected;

        /// <summary>Runs on the SignalR callback thread. Marshal UI changes to the UI thread.</summary>
        public event Action<bool> IsConnectedChanged;

        public SignalRClient(string url)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("An absolute HTTP or HTTPS URL is required.", nameof(url));

            Connection = new HubConnection(url);
            Connection.StateChanged += OnStateChanged;
        }

        /// <summary>Returns a native proxy supporting On and Invoke, including typed payloads and results.</summary>
        public IHubProxy CreateHubProxy(string hubName)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(hubName))
                throw new ArgumentException("A hub name is required.", nameof(hubName));
            if (Connection.State != ConnectionState.Disconnected)
                throw new InvalidOperationException("Create hub proxies before connecting.");

            return Connection.CreateHubProxy(hubName);
        }

        public Task ConnectAsync()
        {
            ThrowIfDisposed();
            return Connection.Start();
        }

        /// <summary>Stops the connection while retaining proxies and subscriptions for a later ConnectAsync.</summary>
        public void Disconnect()
        {
            ThrowIfDisposed();
            Connection.Stop();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Connection.StateChanged -= OnStateChanged;
            IsConnectedChanged = null;
            Connection.Dispose();
        }

        private void OnStateChanged(StateChange change)
        {
            var wasConnected = change.OldState == ConnectionState.Connected;
            var isConnected = change.NewState == ConnectionState.Connected;
            if (wasConnected != isConnected) IsConnectedChanged?.Invoke(isConnected);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SignalRClient));
        }
    }
}
