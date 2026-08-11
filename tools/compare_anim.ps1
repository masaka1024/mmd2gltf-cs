# FK/IK ベイク結果を Python 版と C# 版で比較する。
#
#   pwsh tools/compare_anim.ps1 -Work <作業用フォルダ> [-Mmd2Gltf <mmd2gltf-gui のパス>]
#
# ベイク結果は 1フレームあたり全ボーン分出るので JSON が巨大になる。
# 長いモーションは -Step で間引く (IK の反復自体は間引いても同じように走る)。
param(
    [Parameter(Mandatory = $true)][string]$Work,
    [string]$Mmd2Gltf,
    [int]$Step = 1
)

$ErrorActionPreference = "Continue"
# ★$PSScriptRoot は param() の既定値の中では使えない (param 評価時にはまだ未設定)。
#   Python 版リポジトリは既定でこのリポジトリの隣にある想定。
if (-not $Mmd2Gltf) {
    $Mmd2Gltf = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "mmd2gltf-gui"
}
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $Work | Out-Null

# モデル, モーション, 間引き
. (Join-Path $PSScriptRoot "_testdata.ps1")

$cases = $TdAnimCases

$fail = 0
$i = 0
foreach ($c in $cases) {
    $i++
    $pmx, $vmd, $st = $c
    if ($Step -gt 1) { $st = $Step }
    if (-not (Test-Path -LiteralPath $pmx) -or -not (Test-Path -LiteralPath $vmd)) {
        Write-Host "SKIP (見つかりません) $pmx / $vmd"
        continue
    }
    $pyJson = Join-Path $Work "a$i`_py.json"
    $csJson = Join-Path $Work "a$i`_cs.json"
    Write-Host ("=== {0} + {1} (step {2})" -f
        [IO.Path]::GetFileNameWithoutExtension($pmx),
        [IO.Path]::GetFileNameWithoutExtension($vmd), $st)
    & dotnet run -c Release --project (Join-Path $repo "src\Mmd2Gltf.Cli") --no-build -- `
        animdump $pmx --vmd $vmd --step $st -o $csJson 2>&1 | ForEach-Object { "    C#     $_" }
    & python (Join-Path $repo "tools\modeldump.py") --mmd2gltf $Mmd2Gltf anim $pmx `
        --vmd $vmd --step $st -o $pyJson 2>&1 | ForEach-Object { "    python $_" }
    # 実測 (モデルA + 長尺モーション, 351フレーム, 許容差0で比較): 差があるのは
    # 全値のうち 57 個だけで、最大相対差 1.3e-15 = float64 の数 ULP。
    # float32 の分解能 (1.2e-7) より 8桁小さく、glTF の出力には現れない。
    # 式の評価順が違えば必ずこの程度は出るので、1e-9 を許容差とする。
    $out = & python (Join-Path $repo "tools\jsondiff.py") $pyJson $csJson --tol 1e-9 --max 8
    $out | ForEach-Object { "    $_" }
    if ($LASTEXITCODE -ne 0) { $fail++ }
    Remove-Item $pyJson, $csJson -ErrorAction SilentlyContinue
}
Write-Host ""
Write-Host "==== 差分のあったケース: $fail 件 ===="
exit $fail
