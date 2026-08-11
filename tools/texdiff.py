# -*- coding: utf-8 -*-
"""texdump の Python 版 / C# 版の出力を突き合わせる。

    python tools/texdiff.py tex_py.json tex_cs.json

分類 (standardClass / rawClass / mime / hasOriginal) は完全一致を要求する。
統計値は 1e-4、平均色は 0.02 まで許容する
(平均色は 32x32 縮小の補間実装が PIL と ImageSharp で違うため)。
PNG バイト列の差は情報として出すだけ (エンコーダが違うので一致しない)。
"""
import json
import sys

STAT_TOL = 1e-4
AVG_TOL = 0.02


def main():
    py = {e["file"]: e for e in json.load(open(sys.argv[1], encoding="utf-8"))}
    cs = {e["file"]: e for e in json.load(open(sys.argv[2], encoding="utf-8"))}
    print("python %d 件 / C# %d 件" % (len(py), len(cs)))

    diffs = 0
    png_differs = 0
    for k in sorted(set(py) | set(cs)):
        a, b = py.get(k), cs.get(k)
        name = k.rsplit("/", 1)[-1]
        if a is None or b is None:
            print("片方にしか無い (%s): %s" % ("C#" if a is None else "python", k))
            diffs += 1
            continue

        for f in ("standardClass", "rawClass", "mime", "hasOriginal", "error"):
            if a.get(f) != b.get(f):
                print("差分 %-14s %-30s python=%s  C#=%s" % (f, name, a.get(f), b.get(f)))
                diffs += 1

        sa, sb = a.get("stats"), b.get("stats")
        if (sa is None) != (sb is None):
            print("差分 stats有無     %s" % name)
            diffs += 1
        elif sa:
            for f in sa:
                va, vb = sa[f], sb.get(f)
                if va is None or vb is None:
                    if va != vb:
                        print("差分 %-14s %-30s %s vs %s" % (f, name, va, vb))
                        diffs += 1
                elif abs(va - vb) > STAT_TOL:
                    print("差分 %-14s %-30s %.6f vs %.6f" % (f, name, va, vb))
                    diffs += 1

        ca, cb = a.get("averageColor"), b.get("averageColor")
        if (ca is None) != (cb is None):
            print("差分 averageColor有無  %s" % name)
            diffs += 1
        elif ca:
            d = max(abs(x - y) for x, y in zip(ca, cb))
            if d > AVG_TOL:
                print("差分 averageColor  %-30s 最大 %.4f  %s vs %s"
                      % (name, d, [round(x, 3) for x in ca], [round(x, 3) for x in cb]))
                diffs += 1

        if a.get("dataSha256") != b.get("dataSha256"):
            png_differs += 1

    print("---")
    print("PNG バイト列が違うもの: %d 件 (エンコーダが違うので想定内)" % png_differs)
    print("判定に効く差分: %d 件" % diffs)
    return 1 if diffs else 0


if __name__ == "__main__":
    sys.exit(main())
