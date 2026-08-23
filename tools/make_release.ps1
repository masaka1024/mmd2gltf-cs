# リリース用の実行形式を作る。
#
#   pwsh tools/make_release.ps1                 # 既定 (win-x64)
#   pwsh tools/make_release.ps1 -Rid osx-arm64  # 別プラットフォーム
#
# 自己完結・単一ファイルで publish する。利用者は .NET のインストール不要。
# 出力は artifacts/ (.gitignore 済み)。
#
# ★submodule が必要。git submodule update --init を先に済ませておくこと。

[CmdletBinding()]
param(
    [string]$Rid = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content "$root/src/Mmd2Gltf.Cli/Mmd2Gltf.Cli.csproj")).Project.PropertyGroup.Version | Select-Object -First 1
$name = "mmd2gltf-cs-$version-$Rid"
$stage = Join-Path $root "artifacts/$name"

if (-not (Test-Path "$root/external/mmd-physics/Assets/MmdPhysics")) {
    throw "submodule (external/mmd-physics) が空です。git submodule update --init を実行してください。"
}

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

$projects = @(
    @{ Path = 'src/Mmd2Gltf.Cli';      Exe = 'mmd2gltf'     },
    @{ Path = 'src/Mmd2Gltf.PhysBake'; Exe = 'physbake'     },
    @{ Path = 'src/Mmd2Gltf.Gui';      Exe = 'mmd2gltf-gui' }
)

foreach ($p in $projects) {
    Write-Host "publish $($p.Path) -> $Rid" -ForegroundColor Cyan
    $outDir = Join-Path $root "artifacts/_publish/$($p.Exe)"
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    dotnet publish (Join-Path $root $p.Path) `
        -c $Configuration -r $Rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $outDir --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish に失敗: $($p.Path)" }

    # 単一ファイルなので exe だけを拾う (pdb 等は捨てる)。
    $ext = if ($Rid -like 'win-*') { '.exe' } else { '' }
    $built = Join-Path $outDir "$($p.Exe)$ext"
    if (-not (Test-Path $built)) { throw "exe が見つからない: $built" }
    Copy-Item $built (Join-Path $stage "$($p.Exe)$ext")
}

Copy-Item "$root/README.md" $stage
Copy-Item "$root/LICENSE"   $stage

Write-Host ""
Write-Host "=== $name ===" -ForegroundColor Green
Get-ChildItem $stage | ForEach-Object {
    '{0,-24} {1,10:N1} MB' -f $_.Name, ($_.Length / 1MB)
}

if (-not $NoZip) {
    $zip = Join-Path $root "artifacts/$name.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path "$stage/*" -DestinationPath $zip
    Write-Host ""
    '{0,-24} {1,10:N1} MB' -f (Split-Path -Leaf $zip), ((Get-Item $zip).Length / 1MB)
}
