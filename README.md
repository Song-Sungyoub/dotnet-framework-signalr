<div align="center">

# dotnet-framework-signalr

**Real-time messaging for .NET Framework. Less setup, explicit lifetimes.**

Small client and OWIN self-host helpers built on ASP.NET SignalR 2.

[![Build](https://github.com/Song-Sungyoub/dotnet-framework-signalr/actions/workflows/build.yml/badge.svg)](https://github.com/Song-Sungyoub/dotnet-framework-signalr/actions/workflows/build.yml)
![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4?style=flat-square)
![SignalR](https://img.shields.io/badge/SignalR-2.4.3-0078D4?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square)

[Quick start](#quick-start) · [Usage](#usage) · [Configuration](#configuration) · [Validation](#validation) · [License](#license)

</div>

---

## At a glance

- **One connection, multiple hubs.** Create native hub proxies and use SignalR's typed `On<T>` and `Invoke<T>` APIs.
- **Self-host in your application.** Run an OWIN server from a console app, WPF app, or Windows service.
- **Explicit resource ownership.** Disconnect, reconnect, stop, restart, and dispose without global singleton state.
- **Independent server instances.** Each host owns its own SignalR dependency resolver.
- **Application-defined contracts.** Keep hub methods, DTOs, business logic, and UI code in your application.

> [!IMPORTANT]
> This library targets **Windows / .NET Framework 4.8 / ASP.NET SignalR 2**.
> It is **not compatible with ASP.NET Core SignalR**. Moving to modern .NET requires a separate server and client migration. [See Microsoft's compatibility guide.](https://learn.microsoft.com/en-us/aspnet/core/signalr/version-differences)

## Quick start

### 1. Build

Requirements: Windows, the .NET Framework 4.8 runtime and Developer Pack, .NET SDK 8 or later, and internet access for NuGet restore. Visual Studio 2022 is recommended if you use an IDE.

```powershell
git clone https://github.com/Song-Sungyoub/dotnet-framework-signalr.git
cd dotnet-framework-signalr

dotnet restore SignalR.Library.sln
dotnet build SignalR.Library.sln -c Release --no-restore
```

### 2. Run the demo

Open two PowerShell terminals in the repository root.

```powershell
# Terminal 1 — server
& ./samples/SignalR.Sample/bin/Release/net48/SignalR.Sample.exe server
```

```powershell
# Terminal 2 — client
& ./samples/SignalR.Sample/bin/Release/net48/SignalR.Sample.exe client
```

Type a message in the client to broadcast it through `ChatHub`. The same connection also calls `StatusHub` to read the server's UTC time.

The default address is `http://localhost:8080/`. Pass another URL as the final argument to both commands to change it.

### 3. Add it to your application

Reference `src/SignalR.Library/SignalR.Library.csproj` from your .NET Framework 4.8 project.

The package is **not published to NuGet.org**. To create a local package:

```powershell
dotnet pack src/SignalR.Library/SignalR.Library.csproj -c Release --no-build -o artifacts
```

Library and package name: `SignalR.Library` · Initial version: `0.1.0`.

## Usage

### Define a hub

Place your hubs in the host application's assembly and load that assembly before starting the server.

```csharp
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

[HubName("EchoHub")]
public class EchoHub : Hub
{
    public string Echo(string text) => text;
}
```

### Start a server

Inside your application entry point or a method that owns the server:

```csharp
using SignalR.Library;

using (var server = new SignalRService("http://localhost:8080/"))
{
    server.StartServer();

    var hub = server.GetHubContext<EchoHub>();
    await hub.Clients.All.Notice("Server is ready.");
    // hub.Clients.Client(connectionId).Notice("For one connection");

    Console.ReadLine();
}
```

Use the running service's `GetHubContext<T>()` for broadcasts and connection- or group-specific messages. A context obtained from `GlobalHost` does not address this service's connections.

### Connect a client

Inside an asynchronous method:

```csharp
using Microsoft.AspNet.SignalR.Client;
using SignalR.Library;

using (var client = new SignalRClient("http://localhost:8080/"))
{
    var hub = client.CreateHubProxy("EchoHub");

    using (hub.On<string>("Notice", text => Console.WriteLine(text)))
    {
        await client.ConnectAsync();

        var reply = await hub.Invoke<string>("Echo", "hello");
        Console.WriteLine(reply);

        client.Disconnect();
    }
}
```

Create **all hub proxies and receive handlers before connecting**. Dispose the subscription returned by `On` when its owner no longer needs it.

Native SignalR APIs support multiple arguments, typed DTOs, multiple hubs, and typed return values. Keep application-specific wrappers in your application.

## Configuration

### Lifecycle

| Operation | Behavior |
| --- | --- |
| `client.Disconnect()` | Stops the connection; retains proxies and subscriptions. |
| `client.ConnectAsync()` | Starts the connection, including after an explicit disconnect. |
| `client.Dispose()` | Releases the connection; create a new client to connect again. |
| `server.StartServer()` | Starts the host; repeated calls while running have no effect. |
| `server.StopServer()` | Stops the host; repeated calls are allowed. |
| `server.Dispose()` | Releases the host and resolver; restarting is no longer allowed. |

Reacquire hub contexts after restarting a server. Startup failures propagate to the caller and release the resolver. The owner must serialize lifecycle calls: do not start, stop, or dispose the same instance concurrently.

Temporary reconnects follow SignalR 2's native behavior. After the connection reaches its final `Disconnected` state, the application decides whether and when to retry.

### Client settings and UI callbacks

Use `client.Connection` to configure headers, credentials, tracing, error handlers, and reconnect events **before connecting**. Let `SignalRClient` own the connection's lifecycle.

`IsConnectedChanged` and receive handlers run on SignalR callback threads. Marshal WPF or other UI updates to the UI thread. Subscribers are responsible for handling exceptions in their own callbacks.

### Authentication, CORS, and hosting

Pass OWIN middleware through the server constructor's optional `configure` callback. It runs **before `MapSignalR`**. Add any required authentication or CORS packages to the host project.

The default configuration does not enable unrestricted CORS or implement authentication. The demo hubs are unauthenticated and intended for local use. For external deployments, apply HTTPS, hub authorization such as `[Authorize]`, and method-level permission and input validation. Group membership or a connection ID alone is not authorization.

If HttpListener returns **Access denied**, reserve only the required URL prefix for the account running the host, or use an environment with the necessary permissions. The library and checks do not modify URL ACLs, firewall rules, or certificate settings. Choose a port not already used by another process.

Dependencies use `PackageReference`; no external source-tree paths or private DLLs are required. Newtonsoft.Json is pinned to `13.0.4`. Host applications may need binding redirects; automatic generation is enabled in the sample and checks projects.

## Validation

```powershell
& ./tests/SignalR.Library.Checks/bin/Release/net48/SignalR.Library.Checks.exe
```

Run this executable instead of `dotnet test`. It tests the library and sample hubs over real HTTP connections without a separate test framework, and exits with code `1` on failure. GitHub Actions runs the same checks.

| Check | Verified result |
| --- | --- |
| Windows Release build | Zero warnings and errors |
| Real HTTP integration checks | 51 assertions passed |
| Standalone local NuGet consumer | RPC round trip passed |
| Local and GitHub Actions packaging | Passed |

Verified on **2026-10-21**. Coverage includes typed calls and DTOs, multiple hubs, broadcasts, targeted messages, server isolation, subscription disposal, reconnection, server restart, and startup failure cleanup.

> [!NOTE]
> SignalR 2 emitted `Error while closing the websocket` diagnostics during explicit disconnects in both local and CI checks. The checks passed, but do not assume that every shutdown diagnostic is harmless.
>
> Long-running workloads, remote networks, HTTPS, and authentication integrations have not been validated.

## Project layout

```text
src/SignalR.Library/          Client and self-host lifecycle helpers
samples/SignalR.Sample/       Console server/client demo with two hubs
tests/SignalR.Library.Checks/ Real HTTP checks using the sample hubs
.github/workflows/build.yml  Windows build, integration checks, and packaging
```

## License

A project license has not been selected yet. Reuse terms will be specified when a license is added. Dependencies remain subject to their own licenses.

## References

- [SignalR 2 self-hosting](https://learn.microsoft.com/en-us/aspnet/signalr/overview/deployment/tutorial-signalr-self-host)
- [SignalR 2 .NET client API](https://learn.microsoft.com/en-us/aspnet/signalr/overview/guide-to-the-api/hubs-api-guide-net-client)
- [ASP.NET SignalR vs. ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/version-differences)
