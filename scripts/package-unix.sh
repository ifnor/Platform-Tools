#!/usr/bin/env bash
set -euo pipefail
version="${1:-0.1.0}"
rid="${2:-linux-x64}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]] || { echo "Invalid version" >&2; exit 2; }
case "$rid" in linux-x64|linux-arm64|osx-x64|osx-arm64) ;; *) echo "Unsupported runtime: $rid" >&2; exit 2 ;; esac
root="$(cd "$(dirname "$0")/.." && pwd)"
publish="$root/artifacts/PlatformTools-$version-$rid"
rm -rf "$publish"
mkdir -p "$publish/tools"
dotnet publish "$root/src/PlatformTools.App/PlatformTools.App.csproj" -c Release -r "$rid" --self-contained true -p:Version="$version" -o "$publish"
case "$rid" in
  linux-x64) asset="cloudflared-linux-amd64" ;;
  linux-arm64) asset="cloudflared-linux-arm64" ;;
  osx-x64) asset="cloudflared-darwin-amd64.tgz" ;;
  osx-arm64) asset="cloudflared-darwin-arm64.tgz" ;;
esac
if [[ "$rid" == osx-* ]]; then
  curl -fL "https://github.com/cloudflare/cloudflared/releases/latest/download/$asset" -o "$publish/tools/cloudflared.tgz"
  tar -xzf "$publish/tools/cloudflared.tgz" -C "$publish/tools"
  rm "$publish/tools/cloudflared.tgz"
else
  curl -fL "https://github.com/cloudflare/cloudflared/releases/latest/download/$asset" -o "$publish/tools/cloudflared"
fi
chmod +x "$publish/PlatformTools" "$publish/tools/cloudflared"
cp "$root/THIRD_PARTY_NOTICES.md" "$publish/"
tar -C "$publish" -czf "$root/artifacts/PlatformTools-$version-$rid-portable.tar.gz" .
if [[ "$rid" == linux-* ]]; then
  arch="$( [[ "$rid" == "linux-arm64" ]] && echo arm64 || echo amd64 )"
  deb="$root/artifacts/deb-$rid"
  rm -rf "$deb"; mkdir -p "$deb/DEBIAN" "$deb/opt/platform-tools" "$deb/usr/bin" "$deb/usr/share/applications" "$deb/usr/share/icons/hicolor/scalable/apps" "$deb/usr/share/mime/packages"
  cp -a "$publish/." "$deb/opt/platform-tools/"
  cat > "$deb/DEBIAN/control" <<EOF
Package: platform-tools
Version: $version
Section: net
Priority: optional
Architecture: $arch
Maintainer: Platform Tools
Depends: libx11-6, libice6, libsm6, libfontconfig1
Description: Beginner-friendly Cloudflare Tunnel desktop client
EOF
  ln -s /opt/platform-tools/PlatformTools "$deb/usr/bin/platform-tools"
  cp "$root/assets/platform-tools.svg" "$deb/usr/share/icons/hicolor/scalable/apps/platform-tools.svg"
  cat > "$deb/usr/share/applications/platform-tools.desktop" <<EOF
[Desktop Entry]
Name=Platform Tools
Exec=/opt/platform-tools/PlatformTools
Icon=platform-tools
Type=Application
Categories=Network;Utility;
MimeType=application/x-platformtools-share;
EOF
  cat > "$deb/usr/share/mime/packages/platform-tools.xml" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?><mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info"><mime-type type="application/x-platformtools-share"><comment>Platform Tools share file</comment><glob pattern="*.ptlink"/></mime-type></mime-info>
EOF
  dpkg-deb --build "$deb" "$root/artifacts/PlatformTools-$version-$rid.deb"

  appdir="$root/artifacts/PlatformTools-$version-$rid.AppDir"
  rm -rf "$appdir"; mkdir -p "$appdir/usr/bin"
  cp -a "$publish/." "$appdir/usr/bin/"
  cp "$root/assets/platform-tools.svg" "$appdir/platform-tools.svg"
  cp "$deb/usr/share/applications/platform-tools.desktop" "$appdir/platform-tools.desktop"
  cat > "$appdir/AppRun" <<'EOF'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/PlatformTools" "$@"
EOF
  chmod +x "$appdir/AppRun"
  toolarch="$( [[ "$rid" == "linux-arm64" ]] && echo aarch64 || echo x86_64 )"
  curl -fL "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$toolarch.AppImage" -o "$root/artifacts/appimagetool"
  chmod +x "$root/artifacts/appimagetool"
  APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$toolarch" "$root/artifacts/appimagetool" "$appdir" "$root/artifacts/PlatformTools-$version-$rid.AppImage"
elif [[ "$rid" == osx-* ]]; then
  bundle="$root/artifacts/Platform Tools.app"
  rm -rf "$bundle"; mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
  cp -a "$publish/." "$bundle/Contents/MacOS/"
  cat > "$bundle/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?><!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd"><plist version="1.0"><dict><key>CFBundleName</key><string>Platform Tools</string><key>CFBundleDisplayName</key><string>Platform Tools</string><key>CFBundleIdentifier</key><string>tools.platform.desktop</string><key>CFBundleVersion</key><string>$version</string><key>CFBundleShortVersionString</key><string>$version</string><key>CFBundleExecutable</key><string>PlatformTools</string><key>NSHighResolutionCapable</key><true/><key>CFBundleDocumentTypes</key><array><dict><key>CFBundleTypeName</key><string>Platform Tools Share File</string><key>CFBundleTypeRole</key><string>Viewer</string><key>CFBundleTypeExtensions</key><array><string>ptlink</string></array></dict></array></dict></plist>
EOF
  hdiutil create -volname "Platform Tools" -srcfolder "$bundle" -ov -format UDZO "$root/artifacts/PlatformTools-$version-$rid.dmg"
fi
echo "$publish"
