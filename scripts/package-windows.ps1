param([string]$Version = "0.1.0", [ValidateSet("win-x64", "win-arm64")][string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Invalid version: $Version" }
$root = Split-Path -Parent $PSScriptRoot
$artifacts = [IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$publish = [IO.Path]::GetFullPath((Join-Path $artifacts "PlatformTools-$Version-$Runtime"))
if (-not $publish.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe output path." }
$zip = Join-Path $artifacts "PlatformTools-$Version-$Runtime-portable.zip"
$project = Join-Path $root "src\PlatformTools.App\PlatformTools.App.csproj"
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $publish "tools") -Force | Out-Null
dotnet publish $project -c Release -r $Runtime --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
$asset = "cloudflared-windows-amd64.exe" # Cloudflare does not publish a native Windows ARM64 binary; Windows 11 ARM64 runs x64 via emulation.
Invoke-WebRequest -Uri "https://github.com/cloudflare/cloudflared/releases/latest/download/$asset" -OutFile (Join-Path $publish "tools\cloudflared.exe") -UseBasicParsing
Copy-Item -LiteralPath (Join-Path $root "THIRD_PARTY_NOTICES.md") -Destination $publish
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal
Write-Output $zip
