# 統合パス (convert --bake-physics) を複数モデルで回し、健全性を確認する。
#
#   pwsh tools/check_bake.ps1 -Work <作業用フォルダ>
#
# 見るもの: NaN で発散しないか / リンクの伸びがジョイントの移動制限に収まるか /
#           物理が実際に効いているか (移動が0でない)。
#
# ★1モデルだけで詰めないこと。過去に IA 一本で検証した結果、IA がたまたま
#   使っていない機能 (ばね定数・PMX mode2・複合回転・揺れ物カーブ) の欠陥を
#   4件見逃している。ばね/mode2/揺れ物カーブを持つモデルを必ず含める。
param(
    [Parameter(Mandatory = $true)][string]$Work,
    [switch]$KeepGlb
)

$ErrorActionPreference = "Continue"
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $Work | Out-Null

$walk = "C:\Users\masa_\Downloads\MMD4Mecanim_Beta_20201105\MMD4Mecanim_Beta_20201105\歩き.vmd"

$cases = @(
    @("C:\Users\masa_\Downloads\IA\IA\IA.pmx", $walk),
    @("C:\Users\masa_\Downloads\Tda式初音ミクV4X_Ver1.00\Tda式初音ミクV4X_Ver1.00\Tda式初音ミクV4X_Ver1.00.pmx", $walk),
    @("C:\Users\masa_\Downloads\ぬこ式レーシングミク2023_ver1.01\ぬこ式レーシングミク2023_ver1.00\Racing_Miku2023.pmx", $walk),
    @("C:\Users\masa_\Downloads\コロン式++初音ミクV3_Re_rev.1.2\コロン式  初音ミクV3_Re_rev.1.2\コロン式  初音ミクV3_Re_rev.1.2(ノーマル).pmx", $walk),
    @("C:\Users\masa_\Downloads\ゆかりver7\ゆかりver7\ゆかりver7.pmx", $walk),
    @("C:\Users\masa_\Downloads\つみ式ミクさんv4\つみ式ミクさんv4\つみ式ミクさんv4.pmx", $walk),
    @("C:\Users\masa_\Downloads\ぽんぷ長式初音ミク_テスト版_4\ぽんぷ長式初音ミク_テスト版_4\ぽんぷ長式初音ミク.pmx", $walk),
    @("C:\Users\masa_\Downloads\折岸みつ(0611)\折岸みつ(0611)\折岸みつ.pmx", $walk),
    @("C:\Users\masa_\Downloads\おナス式初音ミク(WhiteRose)ver0.2\おナス式初音ミク(WhiteRose)ver0.2\おナス式初音ミク(ホワイトローズ)ver0.2.pmx", $walk),
    @("C:\Users\masa_\Downloads\IAx改v1.5\IAx改v1.0s\IAx改v1.5[Fur_neko.fx].pmx", $walk),
    @("C:\Users\masa_\Downloads\Alicia\Alicia\MMD\Alicia_solid.pmx", $walk),
    @("C:\Users\masa_\Downloads\mikuV4XS\mikuV4XS\tex\mikuV4XS.pmx", $walk)
)

$bad = 0
$i = 0
foreach ($c in $cases) {
    $i++
    $pmx, $vmd = $c
    if (-not (Test-Path -LiteralPath $pmx)) { Write-Host "SKIP $pmx"; continue }
    $name = [IO.Path]::GetFileNameWithoutExtension($pmx)
    $glb = Join-Path $Work "b$i.glb"
    $out = & dotnet run -c Release --project (Join-Path $repo "src\Mmd2Gltf.Cli") --no-build -- `
        convert $pmx --vmd $vmd --bake-physics -o $glb 2>&1
    $lines = $out | Where-Object { $_ -match '物理ボーン|物理による移動|リンクの伸び|警告|剛体 \d+' }
    Write-Host "=== $name"
    $lines | ForEach-Object { "    $_" }
    if ($out -match '発散') { $bad++; Write-Host "    ★NaN で発散しました" }
    if (-not ($out -match '物理ボーン')) { Write-Host "    (剛体なし / 物理ボーンなし)" }
    if (-not $KeepGlb) { Remove-Item $glb -ErrorAction SilentlyContinue }
}
Write-Host ""
Write-Host "==== 発散したモデル: $bad 件 ===="
exit $bad
