using System;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.AspNet.SignalR.Infrastructure;
using Microsoft.Owin.Hosting;
using Owin;

namespace SignalR.Library
{
    /// <summary>Owns an OWIN self-host and a private SignalR resolver. Lifecycle calls must be serialized by the owner.</summary>
    public sealed class SignalRService : IDisposable
    {
        private readonly string _url;
        private readonly Action<IAppBuilder> _configure;
        private IDisposable _server;
        private DefaultDependencyResolver _resolver;
        private bool _disposed;

        public bool IsRunning => _server != null;

        /// <param name="url">HTTP or HTTPS listener prefix, for example http://localhost:8080/.</param>
        /// <param name="configure">Optional middleware, run before MapSignalR. Configure authentication/CORS here.</param>
        public SignalRService(string url, Action<IAppBuilder> configure = null)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("An absolute HTTP or HTTPS listener URL without credentials, query or fragment is required.", nameof(url));

            _url = url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";
            _configure = configure;
        }

        public void StartServer()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SignalRService));
            if (IsRunning) return;

            var resolver = new DefaultDependencyResolver();
            try
            {
                _server = WebApp.Start(_url, app =>
                {
                    _configure?.Invoke(app);
                    app.MapSignalR(new HubConfiguration { Resolver = resolver });
                });
                _resolver = resolver;
            }
            catch
            {
                resolver.Dispose();
                throw;
            }
        }

        /// <summary>Use the running server's context for broadcasts or connection/group-specific messages.</summary>
        public IHubContext GetHubContext<THub>() where THub : IHub
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SignalRService));
            if (!IsRunning) throw new InvalidOperationException("Start the server before requesting a hub context.");
            return _resolver.Resolve<IConnectionManager>().GetHubContext<THub>();
        }

        public void StopServer()
        {
            var server = _server;
            var resolver = _resolver;
            _server = null;
            _resolver = null;
            try
            {
                server?.Dispose();
            }
            finally
            {
                resolver?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopServer();
        }
    }
}
