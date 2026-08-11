# -*- coding: utf-8 -*-
"""2つの JSON を構造的に突き合わせる (Python 版 / C# 版のパース結果比較用)。

    python tools/jsondiff.py a.json b.json [--tol 0] [--max 40]

数値は --tol の絶対許容差で比較する (既定 0 = 完全一致)。
PMX/VMD のパースは同じバイト列を同じ型で読むだけなので、本来は 0 で一致するはず。
差分はパスつきで先頭 --max 件だけ表示する。
"""
import argparse
import json
import math
import sys

diffs = []


def cmp(path, a, b, tol):
    if len(diffs) > 100000:
        return
    if isinstance(a, bool) or isinstance(b, bool):
        if a != b:
            diffs.append((path, a, b))
        return
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        if a != b:
            if tol <= 0 or not (math.isfinite(a) and math.isfinite(b)) or abs(a - b) > tol:
                diffs.append((path, a, b))
        return
    if type(a) is not type(b):
        diffs.append((path, "type %s" % type(a).__name__, "type %s" % type(b).__name__))
        return
    if isinstance(a, dict):
        # 片方にしか無いキーは、値が null 相当なら無視する
        # (C# 側は null のキーを書かない設定にしてある)。
        for k in sorted(set(a) | set(b)):
            if k not in a:
                if b[k] is not None:
                    diffs.append(("%s.%s" % (path, k), "<なし>", b[k]))
            elif k not in b:
                if a[k] is not None:
                    diffs.append(("%s.%s" % (path, k), a[k], "<なし>"))
            else:
                cmp("%s.%s" % (path, k), a[k], b[k], tol)
        return
    if isinstance(a, list):
        if len(a) != len(b):
            diffs.append(("%s (長さ)" % path, len(a), len(b)))
            return
        for i, (x, y) in enumerate(zip(a, b)):
            cmp("%s[%d]" % (path, i), x, y, tol)
        return
    if a != b:
        diffs.append((path, a, b))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--tol", type=float, default=0.0)
    ap.add_argument("--max", type=int, default=40)
    args = ap.parse_args()

    with open(args.a, encoding="utf-8") as f:
        a = json.load(f)
    with open(args.b, encoding="utf-8") as f:
        b = json.load(f)

    cmp("", a, b, args.tol)
    for path, x, y in diffs[:args.max]:
        sx, sy = repr(x), repr(y)
        if len(sx) > 60:
            sx = sx[:60] + "..."
        if len(sy) > 60:
            sy = sy[:60] + "..."
        print("%s\n    python: %s\n    C#    : %s" % (path or "<ルート>", sx, sy))
    print("---")
    print("差分 %d 件%s" % (len(diffs), " (先頭 %d 件のみ表示)" % args.max
                          if len(diffs) > args.max else ""))
    return 1 if diffs else 0


if __name__ == "__main__":
    sys.exit(main())
