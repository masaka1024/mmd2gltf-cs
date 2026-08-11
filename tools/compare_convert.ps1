# PMX -> GLB 変換の出力を Python 版と C# 版で突き合わせる。
#
#   pwsh tools/compare_convert.ps1 -Work <作業用フォルダ> [-Mmd2Gltf <mmd2gltf-gui のパス>]
#
# Python 版は物理ベイクを付けずに動かす (C# 版は PBD 物理を持たず、
# 揺れ物は physbake が後段で焼くため)。
param(
    [Parameter(Mandatory = $true)][string]$Work,
    [string]$Mmd2Gltf,
    [switch]$KeepGlb
)

$ErrorActionPreference = "Continue"
# ★$PSScriptRoot は param() の既定値の中では使えない (param 評価時にはまだ未設定)。
#   Python 版リポジトリは既定でこのリポジトリの隣にある想定。
if (-not $Mmd2Gltf) {
    $Mmd2Gltf = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "mmd2gltf-gui"
}
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $Work | Out-Null

. (Join-Path $PSScriptRoot "_testdata.ps1")

$cases = $TdConvertCases

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
