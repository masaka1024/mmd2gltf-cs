# PMX -> GLB 変換の出力を Python 版と C# 版で突き合わせる。
#
#   pwsh tools/compare_convert.ps1 -Work <作業用フォルダ> [-Mmd2Gltf <mmd2gltf-gui のパス>]
#
# Python 版は物理ベイクを付けずに動かす (C# 版は PBD 物理を持たず、
# 揺れ物は physbake が後段で焼くため)。
param(
    [Parameter(Mandatory = $true)][string]$Work,
    [string]$Mmd2Gltf = "C:\mytask2\mmd2gltf-gui",
    [switch]$KeepGlb
)

$ErrorActionPreference = "Continue"
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $Work | Out-Null

$walk = "C:\Users\masa_\Downloads\MMD4Mecanim_Beta_20201105\MMD4Mecanim_Beta_20201105\歩き.vmd"

# モデル, モーション(空文字=アニメ無し), 追加オプション
$cases = @(
    @("C:\mytask2\mmd2gltf-gui\tests\test.pmx", "C:\mytask2\mmd2gltf-gui\tests\test.vmd", @()),
    @("C:\Users\masa_\Downloads\IA\IA\IA.pmx", $walk, @()),
    @("C:\Users\masa_\Downloads\IA\IA\IA.pmx", "", @("--morph-mode", "dense")),
    @("C:\Users\masa_\Downloads\Tda式初音ミクV4X_Ver1.00\Tda式初音ミクV4X_Ver1.00\Tda式初音ミクV4X_Ver1.00.pmx", $walk, @()),
    @("C:\Users\masa_\Downloads\ぬこ式レーシングミク2023_ver1.01\ぬこ式レーシングミク2023_ver1.00\Racing_Miku2023.pmx", $walk, @()),
    @("C:\Users\masa_\Downloads\コロン式++初音ミクV3_Re_rev.1.2\コロン式  初音ミクV3_Re_rev.1.2\コロン式  初音ミクV3_Re_rev.1.2(ノーマル).pmx", "", @()),
    @("C:\Users\masa_\Downloads\Alicia\Alicia\MMD\Alicia_solid.pmx", "", @("--unlit")),
    @("C:\Users\masa_\Downloads\ゆかりver7\ゆかりver7\ゆかりver7.pmx", "", @()),
    @("C:\Users\masa_\Downloads\つみ式ミクさんv4\つみ式ミクさんv4\つみ式ミクさんv4.pmx", "", @()),
    @("C:\Users\masa_\Downloads\ぽんぷ長式初音ミク_テスト版_4\ぽんぷ長式初音ミク_テスト版_4\ぽんぷ長式初音ミク.pmx", "", @()),
    @("C:\Users\masa_\Downloads\折岸みつ(0611)\折岸みつ(0611)\折岸みつ.pmx", "", @()),
    @("C:\Users\masa_\Downloads\おナス式初音ミク(WhiteRose)ver0.2\おナス式初音ミク(WhiteRose)ver0.2\おナス式初音ミク(ホワイトローズ)ver0.2.pmx", "", @()),
    @("C:\Users\masa_\Downloads\IAx改v1.5\IAx改v1.0s\IAx改v1.5[Fur_neko.fx].pmx", "", @()),
    @("C:\Users\masa_\Downloads\mikuV4XS\mikuV4XS\tex\mikuV4XS.pmx", "", @())
)

$fail = 0
$i = 0
foreach ($c in $cases) {
    $i++
    $pmx, $vmd, $extra = $c
    if (-not (Test-Path -LiteralPath $pmx)) { Write-Host "SKIP (見つかりません) $pmx"; continue }
    $name = [IO.Path]::GetFileNameWithoutExtension($pmx)
    $pyGlb = Join-Path $Work "c$i`_py.glb"
    $csGlb = Join-Path $Work "c$i`_cs.glb"
    $label = if ($vmd) { "$name + " + [IO.Path]::GetFileNameWithoutExtension($vmd) } else { $name }
    if ($extra.Count) { $label += " [" + ($extra -join " ") + "]" }
    Write-Host "=== $label"

    $pyArgs = @("-m", "mmd2gltf", $pmx, "-o", $pyGlb)
    $csArgs = @("convert", $pmx, "-o", $csGlb)
    if ($vmd) { $pyArgs += @("--vmd", $vmd); $csArgs += @("--vmd", $vmd) }
    $pyArgs += $extra
    $csArgs += $extra

    Push-Location $Mmd2Gltf
    & python @pyArgs 2>&1 | Select-Object -Last 1 | ForEach-Object { "    python $_" }
    Pop-Location
    & dotnet run -c Release --project (Join-Path $repo "src\Mmd2Gltf.Cli") --no-build -- @csArgs 2>&1 |
        Select-Object -Last 1 | ForEach-Object { "    C#     $_" }

    $out = & python (Join-Path $repo "tools\glbdiff.py") $pyGlb $csGlb --max 8
    $out | Where-Object { $_ -notmatch 'DeprecationWarning|getdata' } | ForEach-Object { "    $_" }
    if ($LASTEXITCODE -ne 0) { $fail++ }
    if (-not $KeepGlb) { Remove-Item $pyGlb, $csGlb -ErrorAction SilentlyContinue }
}
Write-Host ""
Write-Host "==== 差分のあったケース: $fail 件 ===="
exit $fail
