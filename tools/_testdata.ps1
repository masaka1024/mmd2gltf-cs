# テストデータの場所を読み込む共通処理。
# tools/testdata.ps1 (git 管理外) があればそれを、無ければ testdata.sample.ps1 を使う。
# 個人のローカルパスや所持モデルの一覧をリポジトリへ載せないための仕組み。
$tdLocal = Join-Path $PSScriptRoot "testdata.ps1"
$tdSample = Join-Path $PSScriptRoot "testdata.sample.ps1"
if (Test-Path $tdLocal) {
    . $tdLocal
} else {
    Write-Host "tools/testdata.ps1 がありません。testdata.sample.ps1 の雛形を使います。"
    Write-Host "自分の環境の PMX / VMD のパスに合わせて testdata.ps1 を作ってください。"
    . $tdSample
}
