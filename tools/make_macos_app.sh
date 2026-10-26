#!/bin/bash
# macOS 用の配布物 (.app 入りの .dmg) を作る。
#
#   tools/make_macos_app.sh                # 既定 (osx-arm64)
#   tools/make_macos_app.sh osx-x64        # Intel Mac 用
#
# dmg の中身:
#   mmd2gltf.app   GUI (自己完結。利用者は .NET のインストール不要)
#   Applications   /Applications へのリンク (ドラッグでインストール)
#   cli/           mmd2gltf / physbake (単一ファイル) と README / LICENSE
#
# 署名は ad-hoc のみ (Developer ID 無し) なので、利用者は初回に右クリック →「開く」。
# 出力は artifacts/ (.gitignore 済み)。macOS 上でしか動かない (sips / iconutil / hdiutil)。
#
# ★submodule が必要。git submodule update --init を先に済ませておくこと。

set -euo pipefail

rid="${1:-osx-arm64}"
root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/Mmd2Gltf.Cli/Mmd2Gltf.Cli.csproj" | head -n1)"
name="mmd2gltf-cs-$version-$rid"
stage="$root/artifacts/$name"
publish="$root/artifacts/_publish/$rid"
app="$stage/mmd2gltf.app"

if [ ! -d "$root/external/mmd-physics/Assets/MmdPhysics" ]; then
    echo "submodule (external/mmd-physics) が空です。git submodule update --init を実行してください。" >&2
    exit 1
fi

rm -rf "$stage" "$publish"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$stage/cli"

# GUI: .app の中に置くので単一ファイルにはしない (Avalonia のネイティブ lib をそのまま並べる)。
echo "publish src/Mmd2Gltf.Gui -> $rid"
dotnet publish "$root/src/Mmd2Gltf.Gui" -c Release -r "$rid" --self-contained true \
    -p:DebugType=none -o "$publish/gui" --nologo >/dev/null
cp -R "$publish/gui/" "$app/Contents/MacOS/"

# CLI: Windows 版と同じく単一ファイル。
# ★EnableCompressionInSingleFile は付けない。osx-arm64 で起動時に 15〜20% の確率で
#   AccessViolationException (Thread.StartHelper.InitializeCulture) で落ちる。
#   無圧縮なら 0 回。dmg 自体が圧縮されるので配布サイズはほぼ変わらない。
for p in Mmd2Gltf.Cli:mmd2gltf Mmd2Gltf.PhysBake:physbake; do
    proj="${p%%:*}"; exe="${p##*:}"
    echo "publish src/$proj -> $rid"
    dotnet publish "$root/src/$proj" -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=none \
        -o "$publish/$exe" --nologo >/dev/null
    cp "$publish/$exe/$exe" "$stage/cli/$exe"
done
cp "$root/README.md" "$root/LICENSE" "$stage/cli/"

# アイコン: マスターの 1024px PNG から全サイズを作って icns にする。
iconset="$publish/mmd2gltf.iconset"
mkdir -p "$iconset"
for s in 16 32 128 256 512; do
    sips -z $s $s "$root/assets/mmd2gltf.png" --out "$iconset/icon_${s}x${s}.png" >/dev/null
    sips -z $((s*2)) $((s*2)) "$root/assets/mmd2gltf.png" --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/mmd2gltf.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>mmd2gltf</string>
  <key>CFBundleDisplayName</key><string>mmd2gltf</string>
  <key>CFBundleIdentifier</key><string>com.masaka1024.mmd2gltf-cs</string>
  <key>CFBundleExecutable</key><string>mmd2gltf-gui</string>
  <key>CFBundleIconFile</key><string>mmd2gltf.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

codesign --force --deep -s - "$app"
codesign --force -s - "$stage/cli/mmd2gltf" "$stage/cli/physbake"
ln -s /Applications "$stage/Applications"

dmg="$root/artifacts/$name.dmg"
rm -f "$dmg"
hdiutil create -volname "mmd2gltf $version" -srcfolder "$stage" -ov -format UDZO "$dmg" >/dev/null

echo ""
echo "=== $name ==="
du -sh "$app" "$stage/cli/mmd2gltf" "$stage/cli/physbake" "$dmg"
