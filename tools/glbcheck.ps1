# 出力した GLB に本当にモーションが入っているかを、出力側から確認する。
#
#   pwsh tools/glbcheck.ps1 -Glb out.glb
#
# 終了コード: 0 = モーション有り / 2 = 完全に静止 (Tポーズ) / 3 = ほぼ動いていない
# なので CI やリリース前チェックにそのまま噛ませられる。
#
# ★変換ログの自己申告 ("bone animation: N channels") を信用しないこと。
#   チャンネルとキーフレームは書けていても中身が全部同じ値なら Tポーズのまま出る。
#   実際に、入力 VMD の中身が空だったのにログは正常に見えた事例がある。
#   ここでは accessor を読んで「値が変化しているか」まで見る。
#
# 見るもの:
#   rotation    各チャンネルの回転角の最大-最小。動いていれば > 0。
#   translation 各チャンネルの位置の変動幅。物理ボーンはリンクが伸びない限り
#               静止が正常なので、静止の本数が急に変わったら疑う。
#   weights     モーフが動いているか。
param(
    [Parameter(Mandatory = $true)][string]$Glb,
    # これ未満の変化は「静止」とみなす。float32 の丸めを拾わない程度に緩くしてある。
    [double]$RotEpsDeg = 0.5,
    [double]$TransEps = 1e-4,
    # 最大回転がこれ未満なら「ほぼ動いていない」として警告する。
    [double]$WarnDeg = 5.0
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Glb)) { Write-Host "見つからない: $Glb"; exit 1 }

# --- GLB のチャンク分解 (JSON と BIN) ---
$fs = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $Glb))
$br = New-Object System.IO.BinaryReader $fs
if ($br.ReadUInt32() -ne 0x46546C67) { $br.Dispose(); $fs.Dispose(); Write-Host "GLB ではない: $Glb"; exit 1 }
[void]$br.ReadUInt32(); [void]$br.ReadUInt32()
$json = $null; $bin = $null
while ($fs.Position -lt $fs.Length) {
    $len = $br.ReadUInt32(); $type = $br.ReadUInt32(); $data = $br.ReadBytes($len)
    if     ($type -eq 0x4E4F534A) { $json = [System.Text.Encoding]::UTF8.GetString($data) }  # JSON
    elseif ($type -eq 0x004E4942) { $bin  = $data }                                          # BIN
}
$br.Dispose(); $fs.Dispose()
$g = $json | ConvertFrom-Json

function Read-Accessor($idx) {
    $a = $g.accessors[$idx]
    $ncomp = @{ "SCALAR" = 1; "VEC2" = 2; "VEC3" = 3; "VEC4" = 4 }[$a.type]
    # アニメーションの output は float32 しか書かないので、それ以外は想定外。
    if ($a.componentType -ne 5126) { throw "componentType $($a.componentType) は未対応" }
    $bv = $g.bufferViews[$a.bufferView]
    $base = [int]$bv.byteOffset + [int]$a.byteOffset
    $stride = if ($bv.PSObject.Properties["byteStride"] -and $bv.byteStride) { [int]$bv.byteStride } else { 4 * $ncomp }
    $out = New-Object "double[][]" $a.count
    for ($i = 0; $i -lt $a.count; $i++) {
        $row = New-Object "double[]" $ncomp
        for ($c = 0; $c -lt $ncomp; $c++) { $row[$c] = [BitConverter]::ToSingle($bin, $base + $i * $stride + $c * 4) }
        $out[$i] = $row
    }
    ,$out
}

$nodeName = @{}
for ($i = 0; $i -lt $g.nodes.Count; $i++) {
    $nodeName[$i] = if ($g.nodes[$i].PSObject.Properties["name"]) { $g.nodes[$i].name } else { "node$i" }
}

"GLB   : {0} ({1:N1} MB)" -f (Split-Path -Leaf $Glb), ((Get-Item -LiteralPath $Glb).Length / 1MB)
$nAnim = if ($g.PSObject.Properties["animations"]) { $g.animations.Count } else { 0 }
"nodes = $($g.nodes.Count) / meshes = $($g.meshes.Count) / animations = $nAnim"

if ($nAnim -eq 0) {
    ""
    "NG: アニメーションが1つも無い = 完全な静止 (Tポーズ)"
    exit 2
}

$worst = 0
foreach ($anim in $g.animations) {
    $anm = if ($anim.PSObject.Properties["name"]) { $anim.name } else { "(名前なし)" }
    ""
    "--- animation '$anm' : channels = $($anim.channels.Count) ---"

    $movedRot = 0; $staticRot = 0; $movedTrans = 0; $staticTrans = 0; $movedW = 0; $staticW = 0
    $maxDeg = 0.0; $maxDegBone = ""; $maxTr = 0.0; $maxTrBone = ""; $keyTotal = 0

    foreach ($ch in $anim.channels) {
        $vals = Read-Accessor $anim.samplers[$ch.sampler].output
        $keyTotal += $vals.Count
        $bone = $nodeName[[int]$ch.target.node]

        switch ($ch.target.path) {
            "rotation" {
                # 四元数を回転角へ直し、その最大-最小を「動いた量」とする。
                # 軸が変わるだけの動きは拾えないが、Tポーズ判定には十分。
                $mn = [double]::MaxValue; $mx = [double]::MinValue
                foreach ($v in $vals) {
                    $deg = 2.0 * [Math]::Acos([Math]::Min(1.0, [Math]::Abs($v[3]))) * 180.0 / [Math]::PI
                    if ($deg -lt $mn) { $mn = $deg }
                    if ($deg -gt $mx) { $mx = $deg }
                }
                $range = $mx - $mn
                if ($range -gt $RotEpsDeg) { $movedRot++ } else { $staticRot++ }
                if ($range -gt $maxDeg) { $maxDeg = $range; $maxDegBone = $bone }
            }
            "translation" {
                $mn = @(1e30, 1e30, 1e30); $mx = @(-1e30, -1e30, -1e30)
                foreach ($v in $vals) {
                    for ($c = 0; $c -lt 3; $c++) {
                        if ($v[$c] -lt $mn[$c]) { $mn[$c] = $v[$c] }
                        if ($v[$c] -gt $mx[$c]) { $mx[$c] = $v[$c] }
                    }
                }
                # ★カンマは減算より強く結合するので @($a - $b, ...) と書くと
                #   配列同士の引き算になって落ちる。個別の変数に落とすこと。
                $dx = $mx[0] - $mn[0]; $dy = $mx[1] - $mn[1]; $dz = $mx[2] - $mn[2]
                $range = [Math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
                if ($range -gt $TransEps) { $movedTrans++ } else { $staticTrans++ }
                if ($range -gt $maxTr) { $maxTr = $range; $maxTrBone = $bone }
            }
            "weights" {
                $mn = [double]::MaxValue; $mx = [double]::MinValue
                foreach ($v in $vals) { foreach ($x in $v) {
                    if ($x -lt $mn) { $mn = $x }
                    if ($x -gt $mx) { $mx = $x }
                } }
                if (($mx - $mn) -gt 1e-4) { $movedW++ } else { $staticW++ }
            }
        }
    }

    "keyframes   : {0:N0} (全チャンネル合計)" -f $keyTotal
    "rotation    : 可動 {0} / 静止 {1}" -f $movedRot, $staticRot
    "translation : 可動 {0} / 静止 {1}" -f $movedTrans, $staticTrans
    "weights     : 可動 {0} / 静止 {1}" -f $movedW, $staticW
    "最大回転変化: {0,8:F2} 度   ボーン '{1}'" -f $maxDeg, $maxDegBone
    "最大移動量  : {0,8:F4}      ボーン '{1}'" -f $maxTr, $maxTrBone
    ""

    if ($movedRot -eq 0 -and $movedTrans -eq 0) {
        "NG: 全チャンネルが静止 = Tポーズのまま (モーションが入っていない)"
        if ($worst -lt 2) { $worst = 2 }
    }
    elseif ($maxDeg -lt $WarnDeg) {
        "警告: ほとんど動いていない (最大 {0:F2} 度)。入力かベイクを確認すること" -f $maxDeg
        if ($worst -lt 3) { $worst = 3 }
    }
    else {
        "OK: モーションが入っている"
    }
}

exit $worst
