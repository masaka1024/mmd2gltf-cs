#!/bin/bash
# macOS 用の配布物を作る (Mac 上で実行すること)。
#
#   bash tools/make_mac_app.sh              # 既定 (osx-arm64 = Apple Silicon)
#   bash tools/make_mac_app.sh osx-x64      # Intel Mac 向け
#
# 作るもの (artifacts/ 以下、.gitignore 済み):
#   mmd2gltf-cs-<ver>-<rid>/
#     mmd2gltf-gui.app   … ダブルクリックで起動する GUI
#     mmd2gltf           … CLI (ターミナル用)
#     physbake           … 物理ベイク単体 (ターミナル用)
#     README.md / LICENSE
#   mmd2gltf-cs-<ver>-<rid>.zip
#
# ★Mac 上で実行する理由:
#   .icns アイコン作成 (sips / iconutil) と署名 (codesign) が macOS の
#   標準コマンドだから。Apple Silicon は署名なしの実行ファイルを起動できない
#   ので、無料の「アドホック署名」(codesign -s -) を付ける。
#   (Apple 開発者登録の正式署名ではないので、初回は右クリック→「開く」が必要)
#
# ★submodule が必要。git submodule update --init を先に済ませておくこと。

set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIG=Release

if [[ "$(uname)" != "Darwin" ]]; then
    echo "このスクリプトは macOS 上で実行してください。" >&2
    exit 1
fi
if [[ ! -d "$ROOT/external/mmd-physics/Assets/MmdPhysics" ]]; then
    echo "submodule (external/mmd-physics) が空です。git submodule update --init を実行してください。" >&2
    exit 1
fi
command -v dotnet >/dev/null || { echo "dotnet が見つかりません。.NET 9 SDK をインストールしてください。" >&2; exit 1; }

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Mmd2Gltf.Cli/Mmd2Gltf.Cli.csproj" | head -1)"
NAME="mmd2gltf-cs-$VERSION-$RID"
STAGE="$ROOT/artifacts/$NAME"
PUB="$ROOT/artifacts/_publish_mac"

rm -rf "$STAGE" "$PUB"
mkdir -p "$STAGE" "$PUB"

# ---------------------------------------------------------------
# 1) CLI 2本: 単一ファイルで publish してアドホック署名
# ---------------------------------------------------------------
for pair in "Mmd2Gltf.Cli:mmd2gltf" "Mmd2Gltf.PhysBake:physbake"; do
    proj="${pair%%:*}"; exe="${pair##*:}"
    echo "== publish $proj -> $RID"
    dotnet publish "$ROOT/src/$proj" -c $CONFIG -r "$RID" --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=none \
        -o "$PUB/$exe" --nologo
    cp "$PUB/$exe/$exe" "$STAGE/$exe"
    chmod +x "$STAGE/$exe"
    codesign --force -s - "$STAGE/$exe"
done

# ---------------------------------------------------------------
# 2) GUI: .app バンドルに包む
#    .app の中は単一ファイルにしない (Avalonia のネイティブ部品 dylib を
#    素直に並べた方が起動が速く、署名も確実)。
# ---------------------------------------------------------------
echo "== publish Mmd2Gltf.Gui -> $RID"
dotnet publish "$ROOT/src/Mmd2Gltf.Gui" -c $CONFIG -r "$RID" --self-contained true \
    -p:UseAppHost=true \
    -p:DebugType=none \
    -o "$PUB/gui" --nologo

APP="$STAGE/mmd2gltf-gui.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB/gui/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/mmd2gltf-gui"

# アイコン: assets/mmd2gltf.png (1024px) から .icns を作る
ICONSET="$PUB/mmd2gltf.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
    sips -z $s $s           "$ROOT/assets/mmd2gltf.png" --out "$ICONSET/icon_${s}x${s}.png"    >/dev/null
    sips -z $((s*2)) $((s*2)) "$ROOT/assets/mmd2gltf.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/mmd2gltf.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>                <string>mmd2gltf</string>
    <key>CFBundleDisplayName</key>         <string>mmd2gltf</string>
    <key>CFBundleIdentifier</key>          <string>io.github.masaka1024.mmd2gltf-cs</string>
    <key>CFBundleExecutable</key>          <string>mmd2gltf-gui</string>
    <key>CFBundleIconFile</key>            <string>mmd2gltf.icns</string>
    <key>CFBundlePackageType</key>         <string>APPL</string>
    <key>CFBundleShortVersionString</key>  <string>$VERSION</string>
    <key>CFBundleVersion</key>             <string>$VERSION</string>
    <key>LSMinimumSystemVersion</key>      <string>12.0</string>
    <key>NSHighResolutionCapable</key>     <true/>
    <key>NSHumanReadableCopyright</key>    <string>Copyright (c) 2026 masaka1024</string>
</dict>
</plist>
PLIST

# .app 全体をアドホック署名 (中の dylib も含めて)
codesign --force --deep -s - "$APP"

cp "$ROOT/README.md" "$ROOT/LICENSE" "$STAGE/"

# ---------------------------------------------------------------
# 3) zip (ditto を使うと .app の中身・署名・実行属性が崩れない)
# ---------------------------------------------------------------
ZIP="$ROOT/artifacts/$NAME.zip"
rm -f "$ZIP"
( cd "$ROOT/artifacts" && ditto -c -k --keepParent "$NAME" "$ZIP" )
rm -rf "$PUB"

echo ""
echo "=== $NAME ==="
du -sh "$STAGE"/* | sed "s:$STAGE/::"
du -sh "$ZIP" | sed "s:$ROOT/artifacts/::"
echo ""
echo "動作確認: open \"$APP\""
