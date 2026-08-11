# -*- coding: utf-8 -*-
"""Python 版 mmd2gltf のテクスチャ処理結果を JSON で出す (C# 版との突合用)。

    python tools/texdump.py --mmd2gltf <mmd2gltf-gui のパス> <dir|file>... [-o out.json]

C# 側の `mmd2gltf texdump` と同じ形式を出すので、そのまま diff できる。
比較しているのは convert.py の load_texture / _alpha_class / _average_color /
_prebake_mask_alpha。
"""
import argparse
import hashlib
import json
import os
import sys

EXTS = (".png", ".jpg", ".jpeg", ".bmp", ".tga", ".sph", ".spa",
        ".dds", ".tif", ".tiff", ".gif")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mmd2gltf", required=True,
                    help="mmd2gltf-gui リポジトリのパス")
    ap.add_argument("inputs", nargs="+")
    ap.add_argument("-o", "--output")
    ap.add_argument("--png-dir", help="変換後 PNG も書き出す")
    a = ap.parse_args()

    sys.path.insert(0, os.path.abspath(a.mmd2gltf))
    # `from mmd2gltf import convert` は __init__ が再エクスポートしている
    # convert *関数* を掴んでしまうので、モジュールを明示的に取る。
    import importlib
    C = importlib.import_module("mmd2gltf.convert")

    if not C.HAS_PIL:
        sys.exit("Pillow が要ります: pip install Pillow")

    if a.png_dir:
        os.makedirs(a.png_dir, exist_ok=True)

    files = []
    for inp in a.inputs:
        if os.path.isdir(inp):
            for root, _dirs, names in os.walk(inp):
                for n in names:
                    if os.path.splitext(n)[1].lower() in EXTS:
                        files.append(os.path.join(root, n))
        elif os.path.isfile(inp):
            files.append(inp)
        else:
            print("見つかりません: %s" % inp, file=sys.stderr)
    files.sort()

    out = []
    for f in files:
        entry = {"file": f.replace("\\", "/")}
        r = C.load_texture(f)
        if r is None:
            entry["error"] = "load failed"
            out.append(entry)
            continue
        data, mime, standard_cls, aclass, avg, stats, orig = r
        entry["mime"] = mime
        entry["standardClass"] = standard_cls
        entry["rawClass"] = aclass
        entry["hasOriginal"] = orig is not None
        entry["dataSha256"] = hashlib.sha256(data).hexdigest()[:16]
        if avg is not None:
            entry["averageColor"] = [round(v, 6) for v in avg]
        if stats is not None:
            entry["stats"] = {
                "belowFrac": round(stats["below_frac"], 6),
                "semiLowFrac": round(stats["semi_low_frac"], 6),
                "semiHighFrac": round(stats["semi_high_frac"], 6),
                "meanAlphaOfBelow": (round(stats["mean_alpha_of_below_0_255"], 6)
                                     if stats["mean_alpha_of_below_0_255"] is not None
                                     else None),
            }
        out.append(entry)

        if a.png_dir and mime == "image/png":
            # モデルをまたぐと同じファイル名が衝突して比較から漏れるので、
            # フルパスのハッシュを前置する (C# 版 TexDump.SafeName と同じ規則)。
            full = os.path.abspath(f).replace("\\", "/").lower()
            h = hashlib.sha256(full.encode("utf-8")).hexdigest()[:8]
            safe = h + "_" + os.path.basename(f)
            with open(os.path.join(a.png_dir, safe + ".png"), "wb") as fh:
                fh.write(data)

    js = json.dumps(out, indent=2, ensure_ascii=False)
    if a.output:
        with open(a.output, "w", encoding="utf-8") as fh:
            fh.write(js)
    else:
        print(js)
    print("%d 個のテクスチャを処理しました。" % len(files), file=sys.stderr)


if __name__ == "__main__":
    main()
