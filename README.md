# Platform Tools

Platform Tools is a beginner-friendly, cross-platform desktop interface for Cloudflare Tunnel. It publishes local web, SSH, RDP, SMB, database, and custom TCP services without requiring users to learn `cloudflared` commands.

## What users can do

- Create a temporary `trycloudflare.com` URL without signing in (HTTP/HTTPS only).
- Sign in to Cloudflare and publish a service on their own hostname.
- Choose public access or protect a hostname with Cloudflare Access email rules.
- Export a connection as a `.ptlink` file or share code. Share files never contain tunnel credentials or API tokens.
- Import a share profile and automatically start the client-side proxy required by SSH, RDP, SMB, databases, and custom TCP services.
- Switch between Simplified Chinese and English, light and dark themes.
- Check and update the bundled official `cloudflared` binary.

## Quick start for users

1. Open **Platform Tools** and select **Publish a local service**.
2. Choose the service type and confirm the local address.
3. For a web service, choose **Temporary URL** and press **Publish**. Copy the generated address when it appears.
4. For an own-domain or non-HTTP service, choose **Use my own domain**, sign in in the browser, enter a tunnel name and full hostname, then publish.
5. Save the generated `.ptlink` file when another computer needs the Platform Tools connector.

Non-HTTP services are not ordinary publicly exposed ports. Cloudflare requires `cloudflared` on the connecting computer. Platform Tools includes and controls it automatically. RDP opens Microsoft Remote Desktop on Windows; SSH and database modes display the local command/address to use.

## Access protection

Protected mode asks for a Cloudflare Account ID, an API token with **Access: Apps and Policies Write**, and one or more allowed email addresses. The token exists only in the input control and operation memory and is cleared after use. It is not persisted, logged, or exported.

Cloudflare's browser login creates `cert.pem` and tunnel credential files in the current user's standard `.cloudflared` directory. Platform Tools stores non-secret settings and recent history in the current user's local application-data directory.

## Supported packages

| Platform | Architectures | Outputs |
|---|---|---|
| Windows | x64, ARM64 app | Setup EXE, portable ZIP |
| macOS | Intel, Apple Silicon | `.app`, DMG, portable tar.gz |
| Linux | x64, ARM64 | AppImage, DEB, portable tar.gz |

Cloudflare does not currently publish a native Windows ARM64 `cloudflared`; the Windows ARM64 package therefore uses Cloudflare's official x64 binary through Windows 11's x64 emulation.

Published executables are self-contained and do not require users to install .NET. Public distribution should sign/notarize the packages with the publisher's own Windows code-signing certificate and Apple Developer identity.

## Development

Requirements: .NET 10 SDK. The desktop UI uses Avalonia 12.

```powershell
dotnet restore PlatformTools.slnx
dotnet test PlatformTools.slnx -c Release
dotnet run --project src/PlatformTools.App/PlatformTools.App.csproj
```

Build Windows packages:

```powershell
./scripts/package-windows.ps1 -Version 0.1.0 -Runtime win-x64
```

On Linux or macOS:

```bash
./scripts/package-unix.sh 0.1.0 linux-x64
./scripts/package-unix.sh 0.1.0 osx-arm64
```

The GitHub Actions release workflow tests and packages every supported runtime on a native runner. Native runners are required for DMG, DEB, and AppImage creation.

## Known Cloudflare service constraints

- Quick Tunnels are intended for development/testing, have no uptime SLA, allow at most 200 in-flight requests, and do not support SSE.
- Own-domain and non-HTTP publishing requires a Cloudflare account and a domain managed by Cloudflare.
- SSH, TCP, RDP, and SMB clients require a client-side `cloudflared` process or a Cloudflare One/WARP private-network setup.
- Creating Access protection requires the account-level Access API permission named above.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for bundled component notices.
