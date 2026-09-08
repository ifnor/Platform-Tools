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
2. Select **Add service**, enter a name, choose the service type, and confirm the local address.
3. For a web service, choose **Temporary URL** and press **Save and start**. Copy the generated address from its service card when it appears.
4. For an own-domain or non-HTTP service, open **Settings → Cloudflare account** and sign in in the browser. Check the displayed account status, then choose **Use my own domain**, enter a tunnel name and full hostname, and publish. The status in the top bar also opens Settings. Use **Refresh status** to verify saved credentials; a network verification failure does not remove them.
5. Save the generated `.ptlink` file when another computer needs the Platform Tools connector.

Non-HTTP services are not ordinary publicly exposed ports. Cloudflare requires `cloudflared` on the connecting computer. Platform Tools includes and controls it automatically. RDP opens Microsoft Remote Desktop on Windows; SSH and database modes display the local command/address to use.

## Multiple published services

Open **Publish → Add service** to save a service or save and start it. Each service has its own cloudflared process, connection state, public address, and bounded runtime log. Starting or stopping one service leaves the others running. A service is shown as running after cloudflared registers a connection; startup failure and unexpected process exit are shown per service.

Configurations are saved in `config/services.json` and restored stopped (or pending deletion) after restarting the app. API tokens and runtime URLs are not saved there. Fixed-domain services must use distinct hostnames and tunnel names, and DNS records are not silently overwritten. Stop a service before editing it. Deleting a service asks for confirmation, stops it, removes the matching DNS CNAME and Cloudflare tunnel, and then removes its local tunnel credentials and service configuration. Conflicting DNS records belonging to other targets are preserved. Failed cleanup retains the service for retry and blocks editing/restarting it; deletion progress is saved in `config/deletions`. Once cloud cleanup is confirmed, retries only finish local cleanup and do not require network access or login. Read-only attributes on the selected tunnel files are cleared for deletion; shared credentials and filesystem permissions are unchanged. Use the original Cloudflare account and authorized zone. If login credentials lack cleanup permissions, the app requests a one-operation API token with Cloudflare Tunnel Edit, DNS Edit, and Zone Read permissions. Shared login credentials and Access applications/policies are retained. Exiting with active services asks for confirmation and stops all of them before closing.

The home page shows service, running, and failure counts. Each running service can copy its address, copy a share code, or export a `.ptlink` file. Protected services request an Access API token at startup and reuse their existing Access application and policy when possible.

If a hostname fails with “An A, AAAA, or CNAME record with that host already exists”, edit the stopped service to use an unused hostname, or restore the original tunnel name if this hostname belongs to a previously published service. An existing route to the same tunnel is accepted. To migrate a hostname intentionally, first review its current DNS record and dependencies in Cloudflare, then configure a proxied CNAME pointing to the tunnel target shown in the error message. The application does not overwrite conflicting DNS records automatically.

## Access protection

Protected mode asks for a Cloudflare Account ID, an API token with **Access: Apps and Policies Write**, and one or more allowed email addresses. The token exists only in the input control and operation memory and is cleared after use. It is not persisted, logged, or exported.

All persistent app data is stored in `config` beside the executable, independent of the working directory:

- `config/.cloudflared/`: Cloudflare login certificate (`cert.pem`), tunnel credentials (`<tunnel-id>.json`), and client Access authorization cache.
- `config/settings.json`: settings and recent connections.
- `config/services.json`: saved service definitions, restored stopped on launch.
- `config/tunnels/<tunnel-id>/config.yml`: generated tunnel configuration (regenerated when publishing after moving the application).

On first use, missing settings and credentials are copied from the previous user-profile locations. Existing portable files take precedence, and the original files are retained. Keep the entire `config` directory when upgrading or moving the app; use a writable application directory. API tokens are still kept only in memory. The `config` directory contains secrets and must not be included in shared release packages.

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

## Automatic GitHub releases

Push a version tag to build and publish all platforms:

```bash
git push origin main
git tag -a v0.3.5 -m "Release v0.3.5"
git push origin v0.3.5
```

The tag must contain the release workflow and scripts. Tags use `vMAJOR.MINOR.PATCH`, optionally followed by a prerelease suffix such as `-beta.1`. The workflow tests the tagged source, builds all six runtimes on native runners, verifies all 13 packages, generates `SHA256SUMS.txt`, then publishes a GitHub Release with generated notes. Prerelease tags are marked as prereleases. No personal access token is needed: only the publish job receives `contents: write` through `GITHUB_TOKEN`.

Release assets: Windows x64 portable ZIP and Setup EXE; Windows ARM64 portable ZIP; Linux x64/ARM64 portable tar.gz, DEB and AppImage; macOS Intel/Apple Silicon portable tar.gz and DMG. Windows ARM64 uses the official x64 cloudflared binary under emulation.

If a build fails, no release is published. Rerun failed jobs, or use Actions → Release → Run workflow with the **existing tag**. Partial uploads stay in a draft until publishing succeeds. A published release is never overwritten by a rerun; create a new version tag instead. Actions artifacts are retained for 14 days; published release assets remain available.

Portable archives retain `./config` beside the executable. Installed Linux launchers use `${XDG_DATA_HOME:-$HOME/.local/share}/platform-tools/config`; the macOS app launcher uses `~/Library/Application Support/Platform Tools/config`. These launchers set `PLATFORMTOOLS_DATA_HOME` so read-only installation directories are not used for credentials. Signing and Apple notarization are not configured; these require the publisher's own certificates and credentials.
