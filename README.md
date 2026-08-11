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
| 1 | `physbake`: mmd2gltf 製 GLB のアニメに物理をベイク | ✅ 動作 |
| 2 | PMX/VMD パーサ・FK/IK ベイク・glTF ライタの移植 | 未着手 |
| 3 | 1と2を統合して単一パイプラインに | 未着手 |
| 4 | GUI | 未着手 |

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

```bash
# 1) Python 版で PMX+VMD → GLB (物理ベイクは使わない)
python -m mmd2gltf model.pmx --vmd motion.vmd -o model.glb

# 2) その GLB に自作エンジンの物理をベイク
physbake model.glb -o model_phys.glb
```

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

**検証は必ず複数モデルで回すこと。** 過去に IA 一本で詰めた結果、
IA がたまたま使っていない機能 (ばね定数・PMX mode2・複合回転・揺れ物カーブ) の
欠陥を4件見逃している。ばね/mode2/複合回転を持つモデルを最低1つ含めること。

## ライセンス

未定 (Python 版に合わせる予定)。
