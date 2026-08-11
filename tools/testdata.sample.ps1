# 突合スクリプトが使うテストデータの場所。
#
# 使い方: このファイルを testdata.ps1 という名前でコピーし、自分の環境の
#         PMX / VMD のパスに書き換えてください。testdata.ps1 は .gitignore
#         されているのでコミットされません (ローカルのパスや所持モデルの
#         一覧をリポジトリへ載せないため)。
#
# ★1モデルだけで検証しないこと。過去に モデルA 一本で詰めた結果、モデルA がたまたま
#   使っていない機能 (ばね定数・PMX mode2・複合回転・揺れ物カーブ) の欠陥を
#   4件見逃しています。ばね/mode2/複合回転を持つモデルを必ず含めてください。

# 短いモーション (61フレーム程度)。スモークテスト用。
$TdWalk = "D:\mmd\motions\walk.vmd"

# --- PMX / VMD 単体のパース比較 (compare_all.ps1) ---
$TdPmx = @(
    "D:\mmd\models\ModelA\ModelA.pmx",
    "D:\mmd\models\ModelB\ModelB.pmx"
)
$TdVmd = @(
    $TdWalk,
    "D:\mmd\motions\dance.vmd"
)

# --- FK/IK ベイクの比較 (compare_anim.ps1): モデル, モーション, 間引き ---
$TdAnimCases = @(
    @($TdPmx[0], $TdWalk, 1),
    @($TdPmx[1], $TdWalk, 1)
)

# --- GLB 全体の比較 (compare_convert.ps1): モデル, モーション(""=無し), 追加オプション ---
$TdConvertCases = @(
    @($TdPmx[0], $TdWalk, @()),
    @($TdPmx[0], "", @("--morph-mode", "dense")),
    @($TdPmx[1], "", @("--unlit"))
)

# --- 物理ベイクの健全性確認 (check_bake.ps1): モデル, モーション ---
$TdBakeCases = @(
    @($TdPmx[0], $TdWalk),
    @($TdPmx[1], $TdWalk)
)
