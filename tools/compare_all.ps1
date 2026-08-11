# PMX / VMD のパース結果を Python 版と C# 版で総当たり比較する。
#
#   pwsh tools/compare_all.ps1 -Work <作業用フォルダ> [-Mmd2Gltf <mmd2gltf-gui のパス>]
#
# --tol 1e-7 を使う理由は PmxParser.cs の冒頭コメント参照
# (BDEF2 の 1.0-w を Python は float64 で計算するため。float32 では一致する)。
param(
    [Parameter(Mandatory = $true)][string]$Work,
    [string]$Mmd2Gltf,
    [string[]]$Pmx,
    [string[]]$Vmd
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

if (-not $Pmx) { $Pmx = $TdPmx }
if (-not $Vmd) { $Vmd = $TdVmd }

$fail = 0
$i = 0
foreach ($kind in @("pmx", "vmd")) {
    $files = if ($kind -eq "pmx") { $Pmx } else { $Vmd }
    foreach ($f in $files) {
        $i++
        if (-not (Test-Path -LiteralPath $f)) {
            Write-Host "SKIP (見つかりません) $f"
            continue
        }
        $name = [IO.Path]::GetFileNameWithoutExtension($f)
        $pyJson = Join-Path $Work "$i`_py.json"
        $csJson = Join-Path $Work "$i`_cs.json"
        Write-Host "=== [$kind] $name"
        & dotnet run -c Release --project (Join-Path $repo "src\Mmd2Gltf.Cli") --no-build -- "$kind`dump" $f -o $csJson 2>&1 |
            ForEach-Object { "    C#     $_" }
        & python (Join-Path $repo "tools\modeldump.py") --mmd2gltf $Mmd2Gltf $kind $f -o $pyJson 2>&1 |
            ForEach-Object { "    python $_" }
        $out = & python (Join-Path $repo "tools\jsondiff.py") $pyJson $csJson --tol 1e-7 --max 10
        $out | ForEach-Object { "    $_" }
        if ($LASTEXITCODE -ne 0) { $fail++ }
        Remove-Item $pyJson, $csJson -ErrorAction SilentlyContinue
    }
}
Write-Host ""
Write-Host "==== 差分のあったファイル: $fail 件 ===="
exit $fail
