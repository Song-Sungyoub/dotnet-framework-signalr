# dotnet-framework-signalr

.NET Framework 4.8 애플리케이션에서 사용하는 **ASP.NET SignalR 2.4.3** 클라이언트와 OWIN 자체 호스팅 서버의 연결·수명을 관리하는 클래스 라이브러리입니다.

장비 제어, DB, 계정, UI, 특정 Hub 이름과 데이터 모델에 의존하지 않습니다. Hub 메서드와 DTO는 사용하는 애플리케이션에서 정의합니다. 원래 업무 모듈을 대체하는 호환 DLL은 아니며 새로운 범용 API를 제공합니다.

**ASP.NET Core SignalR과는 통신 호환되지 않습니다.** [.NET 8 이상으로의 전환은 서버·클라이언트 양쪽의 별도 마이그레이션입니다.](https://learn.microsoft.com/en-us/aspnet/core/signalr/version-differences)

## 환경과 빌드

- Windows 및 .NET Framework 4.8 런타임
- .NET SDK 8 이상 및 .NET Framework 4.8 Developer Pack (참조 어셈블리)
- Visual Studio를 사용하는 경우 SDK 형식 프로젝트를 지원하는 Visual Studio 2022 권장
- NuGet 패키지 복원을 위한 인터넷 연결

```powershell
dotnet restore SignalR.Library.sln
dotnet build SignalR.Library.sln -c Release --no-restore
& ./tests/SignalR.Library.Checks/bin/Release/net48/SignalR.Library.Checks.exe
dotnet pack src/SignalR.Library/SignalR.Library.csproj -c Release --no-build -o artifacts
```

`dotnet test` 대신 위 검사 실행 파일을 실행합니다. 추가 테스트 프레임워크 없이 실제 HTTP 연결로 예제의 Hub와 라이브러리를 함께 검사하며 실패 시 종료 코드 1을 반환합니다. GitHub Actions도 동일한 명령을 사용합니다.

2026-10-21 Windows 로컬 검증에서 Release 빌드 경고·오류 0개, 실제 통신 검사 51개와 패키징을 통과했습니다. 명시적 연결 종료 중 SignalR 2 내부의 `Error while closing the websocket` 진단 로그가 관측됐습니다. 검사 실패로 이어지지는 않았으나 종료 진단 로그를 무조건 정상으로 간주해서는 안 됩니다. 장시간 운용과 원격 네트워크·HTTPS·인증 연동은 별도 검증 대상입니다.

## 예제 실행

두 PowerShell 터미널에서 저장소 루트를 기준으로 실행합니다.

```powershell
# 터미널 1
& ./samples/SignalR.Sample/bin/Release/net48/SignalR.Sample.exe server

# 터미널 2
& ./samples/SignalR.Sample/bin/Release/net48/SignalR.Sample.exe client
```

기본 주소는 `http://localhost:8080/`입니다. 마지막 인수로 서버와 클라이언트에 동일한 주소를 지정할 수 있습니다. 클라이언트는 `ChatHub`에서 메시지를 주고받고, 같은 연결의 `StatusHub`에서 서버 시간을 조회합니다.

HttpListener가 접근 거부를 반환하면 해당 실행 계정에 **사용할 URL prefix만** 예약하거나 권한이 준비된 환경에서 실행합니다. 라이브러리와 검사 코드는 URL ACL, 방화벽, 인증서 설정을 변경하지 않습니다. 다른 프로세스가 사용 중인 포트는 피해야 합니다.

## 클라이언트 사용

```csharp
using Microsoft.AspNet.SignalR.Client;
using SignalR.Library;

using (var client = new SignalRClient("http://localhost:8080/"))
{
    var hub = client.CreateHubProxy("EchoHub");
    using (hub.On<string>("Notice", text => Console.WriteLine(text)))
    {
        await client.ConnectAsync();
        var result = await hub.Invoke<string>("Echo", "hello");
        Console.WriteLine(result);
        client.Disconnect();
    }
}
```

`On<T>`와 `Invoke<T>`는 SignalR의 기본 API를 사용합니다. 임의 개수의 인수, 여러 Hub, DTO와 반환값을 지원하므로 기능별 래퍼를 추가할 필요가 없습니다. 모든 Hub 프록시와 수신 핸들러는 **연결 전에** 생성합니다. `On`이 반환한 `IDisposable`은 구독자가 해제합니다.

`Disconnect()`는 연결만 중단하므로 같은 인스턴스에서 `ConnectAsync()`를 다시 호출하면 기존 프록시와 구독을 계속 사용합니다. `Dispose()` 이후에는 새 클라이언트를 만들어야 합니다. 일시적인 연결 손실의 재연결은 SignalR 2의 기본 동작을 따르며 최종 `Disconnected` 이후의 재시도 정책은 애플리케이션이 결정합니다.

`Connection`은 헤더·자격 증명·오류·재연결 이벤트·추적 등 SignalR 기본 설정을 노출합니다. 연결 전에 설정하며 연결의 생성·해제는 `SignalRClient`에 맡깁니다. `IsConnectedChanged`와 수신 콜백에서 WPF/Unity UI를 갱신할 때는 호출자가 UI 스레드로 전달해야 합니다. 이벤트 핸들러의 예외 처리는 구독자의 책임입니다.

## 서버 사용

Hub는 애플리케이션 어셈블리에 두고, 서버 시작 전에 해당 어셈블리를 로드합니다.

```csharp
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using SignalR.Library;

[HubName("EchoHub")]
public class EchoHub : Hub
{
    public string Echo(string text) => text;
}

// 서버를 사용하는 메서드 안에서:
using (var server = new SignalRService("http://localhost:8080/"))
{
    server.StartServer();
    var hub = server.GetHubContext<EchoHub>();
    await hub.Clients.All.Notice("Server is ready.");
    // hub.Clients.Client(connectionId).Notice("For one connection");
    Console.ReadLine();
}
```

각 서버는 독립적인 SignalR resolver를 소유합니다. 서버 외부에서 메시지를 보낼 때는 **해당 서비스의 `GetHubContext<T>()`**를 사용합니다. `GlobalHost`로 얻은 컨텍스트는 이 서버의 연결에 연결되지 않습니다. 중지 전에 얻은 컨텍스트는 재시작 후 다시 가져와야 합니다.

`StartServer()`와 `StopServer()`는 반복 호출할 수 있고 중지 후 재시작할 수 있습니다. `Dispose()` 이후 재시작은 허용하지 않습니다. 시작 실패 시 예외를 호출자에게 전달하고 resolver를 정리합니다. 시작·중지·해제 호출은 소유자가 순서대로 수행하며, 연결과 해제를 동시에 실행하지 않습니다.

인증 또는 필요한 CORS 정책은 생성자의 `configure` 콜백에서 OWIN 미들웨어로 추가합니다. 이 콜백은 `MapSignalR`보다 먼저 실행합니다. 별도 인증·CORS 패키지가 필요하면 호스트 프로젝트에 추가합니다. 기본 설정은 CORS 전체 허용이나 자체 로그인·권한 관리를 추가하지 않습니다.

예제 Hub는 인증 없는 로컬 통신 데모입니다. 외부 서비스를 운영할 때는 HTTPS, Hub의 `[Authorize]`와 메서드별 권한·입력 검증 등 애플리케이션 정책을 적용합니다. 그룹 구독과 대상 연결 ID만으로 접근 권한이 부여되는 것은 아닙니다.

## 구성

```text
src/SignalR.Library/          연결 및 서버 수명 관리
samples/SignalR.Sample/       서버/클라이언트 콘솔 예제와 두 개의 Hub
tests/SignalR.Library.Checks/ 예제를 사용하는 실제 통신 검사
.github/workflows/build.yml  Windows 빌드·통신 검사·패키징
```

패키지는 `PackageReference`로 복원합니다. 업무 프로젝트, 외부 소스 상대 경로, 사내 DLL, `packages.config`가 필요하지 않습니다. Newtonsoft.Json은 원본의 6.0.4 대신 13.0.4를 사용합니다. 실행 애플리케이션은 필요에 따라 binding redirect를 생성해야 하며 예제와 검사 프로젝트에는 자동 생성을 설정했습니다.

## 배포 상태

[GitHub 저장소](https://github.com/Song-Sungyoub/dotnet-framework-signalr)에 소스를 공개합니다. 라이브러리와 로컬 NuGet 패키지 이름은 `SignalR.Library`, 초기 버전은 `0.1.0`입니다. NuGet.org 게시와 게시 자동화는 포함하지 않았습니다.

배포 라이선스는 아직 지정하지 않았습니다. 공개 저장소에 포함된 소스의 재사용 허용 범위는 라이선스 지정 후 명시합니다. 의존 패키지의 라이선스는 각 패키지의 조건을 따릅니다.

## 참고

- [Microsoft: SignalR 2 자체 호스팅](https://learn.microsoft.com/en-us/aspnet/signalr/overview/deployment/tutorial-signalr-self-host)
- [Microsoft: SignalR 2 .NET 클라이언트 API](https://learn.microsoft.com/en-us/aspnet/signalr/overview/guide-to-the-api/hubs-api-guide-net-client)
