# PMX / VMD のパース結果を Python 版と C# 版で総当たり比較する。
#
#   pwsh tools/compare_all.ps1 -Work <作業用フォルダ> [-Mmd2Gltf <mmd2gltf-gui のパス>]
#
# --tol 1e-7 を使う理由は PmxParser.cs の冒頭コメント参照
# (BDEF2 の 1.0-w を Python は float64 で計算するため。float32 では一致する)。
param(
    [Parameter(Mandatory = $true)][string]$Work,
    [string]$Mmd2Gltf = "C:\mytask2\mmd2gltf-gui",
    [string[]]$Pmx,
    [string[]]$Vmd
)

$ErrorActionPreference = "Continue"
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $Work | Out-Null

if (-not $Pmx) {
    $Pmx = @(
        "C:\Users\masa_\Downloads\IA\IA\IA.pmx",
        "C:\Users\masa_\Downloads\Tda式初音ミクV4X_Ver1.00\Tda式初音ミクV4X_Ver1.00\Tda式初音ミクV4X_Ver1.00.pmx",
        "C:\Users\masa_\Downloads\ぬこ式レーシングミク2023_ver1.01\ぬこ式レーシングミク2023_ver1.00\Racing_Miku2023.pmx",
        "C:\Users\masa_\Downloads\コロン式++初音ミクV3_Re_rev.1.2\コロン式  初音ミクV3_Re_rev.1.2\コロン式  初音ミクV3_Re_rev.1.2(ノーマル).pmx",
        "C:\Users\masa_\Downloads\Alicia\Alicia\MMD\Alicia_solid.pmx",
        "C:\Users\masa_\Downloads\ゆかりver7\ゆかりver7\ゆかりver7.pmx",
        "C:\Users\masa_\Downloads\つみ式ミクさんv4\つみ式ミクさんv4\つみ式ミクさんv4.pmx",
        "C:\Users\masa_\Downloads\ぽんぷ長式初音ミク_テスト版_4\ぽんぷ長式初音ミク_テスト版_4\ぽんぷ長式初音ミク.pmx",
        "C:\Users\masa_\Downloads\折岸みつ(0611)\折岸みつ(0611)\折岸みつ.pmx",
        "C:\Users\masa_\Downloads\おナス式初音ミク(WhiteRose)ver0.2\おナス式初音ミク(WhiteRose)ver0.2\おナス式初音ミク(ホワイトローズ)ver0.2.pmx",
        "C:\Users\masa_\Downloads\IAx改v1.5\IAx改v1.0s\IAx改v1.5[Fur_neko.fx].pmx",
        "C:\Users\masa_\Downloads\mikuV4XS\mikuV4XS\tex\mikuV4XS.pmx",
        "C:\mytask2\mmd2gltf-gui\tests\test.pmx"
    )
}
if (-not $Vmd) {
    $Vmd = @(
        "C:\Users\masa_\Downloads\IA_Conqueror_Motion\IA_Conqueror_Motion\IA_Conqueror_light_version.vmd",
        "C:\Users\masa_\Downloads\IA_Conqueror_Motion\IA_Conqueror_Motion\IA_Conqueror_full_key_version.vmd",
        "C:\Users\masa_\Downloads\MMD4Mecanim_Beta_20201105\MMD4Mecanim_Beta_20201105\歩き.vmd",
        "C:\Users\masa_\Downloads\Alicia\Alicia\MMD Motion\2分ループステップ1.vmd",
        "C:\mytask2\mmd2gltf-gui\tests\test.vmd"
    )
}

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
