# -*- coding: utf-8 -*-
"""texdump --png-dir が出した PNG を画素単位で突き合わせる。

    python tools/pngdiff.py <py_png_dir> <cs_png_dir>

バイト列はエンコーダが違うので一致しないが、*画素* は一致するはず。
ここが合っていれば _prebake_mask_alpha とデコードの移植が正しい。

★既知かつ意図的な差 (最大 1/255): 16bit PNG の 8bit への落とし方。
  PIL は上位バイトを取る単純な切り捨て、ImageSharp は round(v*255/65535) の
  スケーリングで、後者が正しい変換。所持モデルでは モデルD の
  11枚が 16bit RGBA (IHDR bitDepth=16) で該当する。
  よって「最大差 1」は許容し、2 以上だけを不合格とする。
"""
import os
import sys

from PIL import Image


def main():
    pd, cd = sys.argv[1], sys.argv[2]
    names = sorted(set(os.listdir(pd)) & set(os.listdir(cd)))
    only = (set(os.listdir(pd)) ^ set(os.listdir(cd)))
    if only:
        print("片方にしか無いファイル: %d 件" % len(only))
        for n in sorted(only)[:10]:
            print("  ", n)

    bad = 0
    tolerated = 0
    for n in names:
        a = Image.open(os.path.join(pd, n)).convert("RGBA")
        b = Image.open(os.path.join(cd, n)).convert("RGBA")
        if a.size != b.size:
            print("サイズ違い %s %s vs %s" % (n, a.size, b.size))
            bad += 1
            continue
        pa, pb = a.getdata(), b.getdata()
        ndiff = 0
        worst = 0
        for x, y in zip(pa, pb):
            d = max(abs(x[i] - y[i]) for i in range(4))
            if d:
                ndiff += 1
                worst = max(worst, d)
        if ndiff:
            total = a.size[0] * a.size[1]
            if worst <= 1:
                tolerated += 1        # 16bit PNG の丸め差。上のコメント参照。
                continue
            print("画素差 %-34s %d/%d 画素 (%.4f%%) 最大差 %d"
                  % (n, ndiff, total, 100.0 * ndiff / total, worst))
            bad += 1
    print("---")
    print("比較 %d 件" % len(names))
    print("最大差 1 で許容したもの: %d 件 (16bit PNG の 8bit 化の差)" % tolerated)
    print("不合格 (最大差 2 以上): %d 件" % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
