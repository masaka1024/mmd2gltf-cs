# -*- coding: utf-8 -*-
"""Python 版 mmd2gltf の PMX / VMD パース結果を JSON で出す (C# 版との突合用)。

    python tools/modeldump.py --mmd2gltf <mmd2gltf-gui のパス> pmx <model.pmx> -o out.json
    python tools/modeldump.py --mmd2gltf <mmd2gltf-gui のパス> vmd <motion.vmd> -o out.json

C# 側の `mmd2gltf pmxdump` / `vmddump` と同じキー名の JSON を出す。
"""
import argparse
import importlib
import json
import os
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mmd2gltf", required=True)
    ap.add_argument("kind", choices=["pmx", "vmd", "anim"])
    ap.add_argument("input")
    ap.add_argument("-o", "--output")
    ap.add_argument("--vmd", help="anim のときのモーション")
    ap.add_argument("--step", type=int, default=1)
    ap.add_argument("--no-ik", action="store_true")
    ap.add_argument("--ignore-vmd-ik", action="store_true")
    ap.add_argument("--disable-ik", action="append")
    a = ap.parse_args()

    sys.path.insert(0, os.path.abspath(a.mmd2gltf))

    if a.kind == "pmx":
        data = importlib.import_module("mmd2gltf.pmx").parse_pmx(a.input)
        print("PMX %s '%s'  頂点%d 面%d 材質%d ボーン%d モーフ%d 剛体%d Joint%d 余りバイト%d"
              % (data["version"], data["name"], len(data["vertices"]),
                 len(data["indices"]) // 3, len(data["materials"]),
                 len(data["bones"]), len(data["morphs"]),
                 len(data["rigid_bodies"]), len(data["joints"]),
                 data["trailing_bytes"]), file=sys.stderr)
    elif a.kind == "vmd":
        data = importlib.import_module("mmd2gltf.vmd").parse_vmd(a.input)
        print("VMD '%s'  ボーン%dトラック モーフ%dトラック IKキー%d 最終フレーム%d"
              % (data["model_name"], len(data["bones"]), len(data["morphs"]),
                 len(data["ik_frames"]), data["max_frame"]), file=sys.stderr)
    else:
        if not a.vmd:
            sys.exit("anim には --vmd が要ります")
        model = importlib.import_module("mmd2gltf.pmx").parse_pmx(a.input)
        motion = importlib.import_module("mmd2gltf.vmd").parse_vmd(a.vmd)
        bake = importlib.import_module("mmd2gltf.animation").bake
        times, out, unmatched = bake(
            model, motion, solve_ik=not a.no_ik, step=a.step,
            disable_ik=a.disable_ik, use_vmd_ik_frames=not a.ignore_vmd_ik)
        data = {
            "times": times,
            "bones": {str(i): {"r": ([list(q) for q in v["r"]] if v["r"] else None),
                               "t": ([list(t) for t in v["t"]] if v["t"] else None)}
                      for i, v in sorted(out.items())},
            "unmatched": unmatched,
        }
        print("ベイク %dフレーム / 動いたボーン %d / PMX に無かった VMD トラック %d"
              % (len(times), len(out), len(unmatched)), file=sys.stderr)

    js = json.dumps(data, ensure_ascii=False, separators=(",", ":"))
    if a.output:
        with open(a.output, "w", encoding="utf-8") as fh:
            fh.write(js)
    else:
        sys.stdout.write(js)


if __name__ == "__main__":
    main()
