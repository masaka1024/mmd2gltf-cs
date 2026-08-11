# mmd2gltf-cs

MMD (PMX/VMD) → glTF 2.0 変換ツールの **C# 版**。
揺れ物 (髪・スカート・リボン等) の物理は、自作の Bullet 互換エンジン
[mmd2gltf-cs-physics](https://github.com/masaka1024/mmd2gltf-cs-physics) で計算した結果をベイクする。

MMD/MikuMikuDance および PmxEditor とは無関係の非公式・独立実装です。

## なぜ作り直すのか

先行する Python 実装 [mmd2gltf-gui](https://github.com/masaka1024/mmd2gltf-gui) は
**そのまま残してあり、このリポジトリは一切触らない**。作り直す理由は2つ:

1. **物理を自作エンジンに置き換える。**
   Python 版の物理ベイクは PBD/XPBD の独自実装 (`bake_hair.py` 約3,900行) で、
   約35個のチューニングオプションを手で合わせる必要があった。
   自作エンジンは PMX 剛体/Joint をそのまま解く Bullet 互換ソルバなので、
   モデル作者が意図したパラメータがそのまま効く。

2. **座標規約の二重管理をやめる。**
   Python 版は `(x,y,-z)` / `quat(-x,-y,z,w)` / YXZ オイラーという MMD↔glTF 規約を
   エンジンとは別に持っていた。過去にこの二重管理が原因で
   **Z鏡像バグ**と**オイラー順序 ZYX/YXZ バグ**が発生している。
   エンジンをライブラリとして直接参照すれば、この層が構造的に消える。

## 現在の状態

**ステージ1 のみ完了。** まだ PMX→glTF 変換そのものは Python 版に依存している。

| ステージ | 内容 | 状態 |
|---|---|---|
| 1 | `physbake`: GLB のアニメに自作エンジンの物理をベイク | ✅ 動作 |
| 2a | テクスチャ処理 (PIL の置き換え) | ✅ Python 版と一致 |
| 2b | PMX / VMD パーサ | ✅ Python 版と一致 |
| 2c | FK/IK アニメーションベイク | ✅ Python 版と一致 |
| 2d | glTF ライタと変換本体 | ✅ Python 版と一致 |
| 3 | convert と physbake を1コマンドに統合 | ✅ 動作 |
| 4 | GUI (Avalonia / Windows・macOS・Linux) | ✅ 動作 (Windows・macOS で実機確認済み) |

```
src/
  Mmd2Gltf.Core/       ライブラリ
    Pmx/               PMX パーサ
    Vmd/               VMD パーサ
    Animation/         FK/IK ベイク、MMD の数学
    Textures/          テクスチャのデコードとアルファ処理
    Gltf/              glTF ビルダー / GLB 読み書き
    Conversion/        PMX -> glTF 変換本体
    Physics/           自作エンジンでの物理ベイク
    MmdConvert.cs      MMD <-> glTF の座標規約 (ここ1箇所だけ)
  Mmd2Gltf.Cli/        mmd2gltf CLI (convert + 検証用ダンプ)
  Mmd2Gltf.PhysBake/   physbake CLI
  Mmd2Gltf.Gui/        GUI (Avalonia)
external/mmd-physics/  物理エンジン (submodule)
tools/                 Python 版との突合スクリプト
```

この順序は「常に動くものがある」ことを優先している。
ステージ1が本プロジェクトで唯一の新規要素 (エンジン→glTFキーフレーム) で、
ステージ2は Python 版という参照実装があるため出力を突合しながら移植できる。

## ビルドと実行

```bash
git clone --recursive https://github.com/masaka1024/mmd2gltf-cs.git
cd mmd2gltf-cs
dotnet build -c Release src/Mmd2Gltf.PhysBake
```

`--recursive` を忘れた場合は `git submodule update --init`。

### GUI

```bash
dotnet run --project src/Mmd2Gltf.Gui
```

ファイルを選ぶ (ドラッグ&ドロップ可)、オプションを決める、変換する、だけ。
日本語 / English は起動時に OS のロケールから選ばれ、右上で切り替えられる。

Avalonia (MIT) なので Windows / macOS / Linux で同じコードが動く。
依存はすべてマネージド (ImageSharp / Pfim / エンジン本体) でネイティブ依存は無い。
**Windows と macOS で実機動作を確認済み** (2026-08-11)。Linux は未検証。

配布用にビルドする場合:

```bash
dotnet publish src/Mmd2Gltf.Gui -c Release -r win-x64   --self-contained -p:PublishSingleFile=true
dotnet publish src/Mmd2Gltf.Gui -c Release -r osx-arm64 --self-contained
dotnet publish src/Mmd2Gltf.Gui -c Release -r osx-x64   --self-contained
```

macOS で普通のアプリとして扱うには、出力を `.app` バンドル
(`Contents/MacOS/` に実行ファイル + `Contents/Info.plist`) に包む必要がある。
未署名なので初回は右クリック →「開く」で起動する。

### CLI

```bash
# 変換 + 揺れ物の物理ベイクを1コマンドで
mmd2gltf convert model.pmx --vmd motion.vmd --bake-physics -o model.glb
```

`--bake-physics` は変換の途中でエンジンを回すので、中間ファイルを作らない。
既にある GLB へ後から物理を足したいとき (Python 版の出力など) は `physbake` を使う:

```bash
mmd2gltf convert model.pmx --vmd motion.vmd -o model.glb
physbake model.glb -o model_phys.glb
```

`mmd2gltf convert --help` / `physbake --help` で全オプション。

`physbake --help` で全オプション。主なもの:

| オプション | 既定 | 意味 |
|---|---|---|
| `--warmup <秒>` | 2.0 | フレーム0姿勢での空回し。初期過渡を捨てる |
| `--sim-hz <Hz>` | 30 | 内部固定刻み |
| `--substeps <N>` | 2 | 1ステップあたりのサブステップ数 |
| `--no-bone-merge` | — | PMX mode2 (物理演算+ボーン位置合わせ) を無効化 |
| `--dry-run` | — | GLB を書かずに統計だけ出す |

既定の 30Hz × 2サブは実効刻み 1/60s で、Unity 側の既定 (1/60 × 1サブ) と同じ。

## エンジンの扱い (重要)

物理エンジンの実体は `external/mmd-physics` (submodule) **だけ**。
このリポジトリにソースを複製しないこと。
エンジンはすでに physics リポジトリ / importer リポジトリ / Unity プロジェクトの
3箇所に実体があり、同期義務が発生している。4箇所目を作らない。

エンジンを更新するときは submodule を進めてコミットする:

```bash
git -C external/mmd-physics pull origin main
git add external/mmd-physics && git commit -m "engine: 上流へ追従"
```

## 検算

`physbake` は毎回、**バインド姿勢の FK 合成結果**と
**エンジンが GLB から別経路で読んだボーン位置**を突き合わせ、
ズレが 1e-2 を超えたら実行を止める。
座標規約かボーン階層の対応が壊れていれば、以降の数字は全部無意味になるため。
実測では 4モデル (テストモデル / IA / Tda式初音ミクV4X / ぬこ式レーシングミク2023)
すべてで誤差 0。

ベイク後は次の2つを MMD 単位で報告する。最大値は一瞬のスパイクを拾うので
中央値と p90 を併記する:

- **物理による移動** — 物理結果と FK の world 位置の差。物理がどれだけ効いたか。
- **リンクの伸び** — `|ローカル並進| - |restローカル並進|`。
  拘束が効いていれば小さい。ただしジョイントの移動制限が広いモデル
  (例: Tda式のスカートは ±0.5) では正当に大きくなるので、しきい値だけで判断しないこと。

### Python 版との突合

`tools/` に、同じ入力に対する Python 版と C# 版の出力を突き合わせるスクリプトがある。
移植の各段でこれを回して差分ゼロを確認している。

```bash
pwsh tools/compare_all.ps1     -Work <作業用フォルダ>   # PMX / VMD のパース結果
pwsh tools/compare_anim.ps1    -Work <作業用フォルダ>   # FK/IK ベイク結果
pwsh tools/compare_convert.ps1 -Work <作業用フォルダ>   # 変換した GLB 全体
```

※`.ps1` は UTF-8 **BOM 付き**で保存すること。Windows PowerShell 5.1 は BOM 無しの
`.ps1` を ANSI として読むため、日本語パスでパースエラーになる。

結果 (2026-08-11):

| 対象 | 検証範囲 | 結果 |
|---|---|---|
| PMX パーサ | 13モデル | 差分0 |
| VMD パーサ | 5モーション | 差分0 |
| FK/IK ベイク | 7ケース | 差分0 |
| テクスチャ | 12モデル 248枚 | 差分0 |
| GLB 全体 | 14ケース | 差分0 |

GLB 比較 (`tools/glbdiff.py`) が通す差は3種類だけで、いずれも原因が特定済み:

- **accessor の float32 1ULP** — FK/IK ベイクに残る float64 の数 ULP の差
  (実測 最大 1.3e-15 相対) が float32 の丸め境界をまたぐと起きる。
  ぬこ式レーシングミクで 5 値 / 最大 8.6e-8 相対。float32 の分解能そのもの。
- **baseColorFactor ~1e-4** — スフィアマップ専用材質のフォールバック色。
  テクスチャを 32x32 へ縮小して平均する処理の補間実装が PIL と ImageSharp で違う
  (IA の材質 12/13)。
- **画像の 1/255** — 16bit PNG の 8bit 化。PIL は切り捨て、ImageSharp はスケーリング
  (後者が正しい変換)。

整数の accessor (index / JOINTS_0 など) は完全一致を要求している。

### テクスチャ処理の突合

`tools/` に Python 版と C# 版を突き合わせるスクリプトがある。

```bash
mmd2gltf texdump <モデルフォルダ>... -o cs.json --png-dir png_cs
python tools/texdump.py --mmd2gltf ../mmd2gltf-gui <モデルフォルダ>... -o py.json --png-dir png_py
python tools/texdiff.py py.json cs.json     # 分類・統計・平均色
python tools/pngdiff.py png_py png_cs       # 変換後 PNG の画素
```

12モデル・248テクスチャで、分類 (opaque/mask/blend)・統計・平均色は**完全一致**。
PNG の画素も 246枚中 235枚が完全一致、残り11枚は 16bit PNG の 8bit 化の差で最大 1/255
(PIL は切り捨て、ImageSharp はスケーリング。後者が正しい変換なので許容している)。

### ★物理はフレーム単位では再現しない (カオス的)

揺れ物の物理は **1e-7 の入力差で結果が大きく変わる**。実測 (IA + 歩き, 61フレーム):

| 比較 | 振幅の相対差 中央 | 最大 |
|---|---|---|
| 統合パス vs 2段パス | 4.7% | 37% |
| **同じコードパス同士 (入力差 1e-7 のみ)** | **4.0%** | **40%** |

2経路の差は、同じコードに 1e-7 の摂動を与えたときと同じ大きさしかない。
自由に揺れる髪はカオス的なので、経路が違えば必ず位相がずれる。
**物理チャンネルをフレーム単位で突き合わせても意味が無い**。
`tools/animcmp.py --stats` の振幅比較で判定すること
(回転は q と -q が同じ姿勢なので、成分ではなく回転角で測っている)。

FK チャンネル (物理でないボーン) は完全一致を要求してよい。

```bash
python tools/animcmp.py a.glb b.glb --stats   # 順序非依存のチャンネル比較
pwsh tools/check_bake.ps1 -Work <作業用フォルダ>  # 12モデルで発散しないか確認
```

`PHYS_FK_FLOAT32=1` を環境変数に置くと、統合パスの FK 入力を physbake と同じ
float32 精度へ落とす。差がどこから来るかの切り分け用 (上の表はこれで測った)。

**検証は必ず複数モデルで回すこと。** 過去に IA 一本で詰めた結果、
IA がたまたま使っていない機能 (ばね定数・PMX mode2・複合回転・揺れ物カーブ) の
欠陥を4件見逃している。ばね/mode2/複合回転を持つモデルを最低1つ含めること。

## ライセンス

未定 (Python 版に合わせる予定)。
