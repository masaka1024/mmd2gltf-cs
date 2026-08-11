# -*- coding: utf-8 -*-
"""2つの GLB を突き合わせる (Python 版 / C# 版の出力比較用)。

    python tools/glbdiff.py a.glb b.glb [--ftol 1e-6] [--ctol 1e-3] [--max 20]

比較するもの:
  1. JSON チャンクの構造
  2. 埋め込み画像の画素 (PNG のバイト列はエンコーダが違うので一致しない)
  3. accessor の中身 (bufferView の並びが違っても意味的な差だけを見るため)

許容差の考え方 (既知かつ意図的な差だけを通し、それ以外は落とす):
  * 整数の accessor (index / JOINTS_0 など) は完全一致を要求する。
  * 浮動小数の accessor は相対 --ftol (既定 1e-6) で比較する。
    glTF は float32 なので分解能は約 6e-8 相対。FK/IK ベイクに残る float64 の
    数 ULP の差 (実測 1.3e-15 相対) が float32 の丸め境界をまたぐと、
    ちょうど 1 ULP ずれた値が出ることがある (ぬこ式レーシングミクで 5 値)。
  * JSON の数値は完全一致を要求する。ただし baseColorFactor だけは --ctol
    (既定 1e-3) を使う。スフィアマップ専用材質のフォールバック色は
    テクスチャを 32x32 へ縮小して平均した値で、縮小の補間実装が
    PIL と ImageSharp で違うため ~1e-4 ずれる (IA の材質 12/13 が該当)。
  * 画像は最大差 1 まで許容する。16bit PNG の 8bit 化が PIL は切り捨て、
    ImageSharp はスケーリングのため (後者が正しい変換)。
"""
import argparse
import array
import json
import struct
import sys

COMP = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2),
        5125: ("I", 4), 5126: ("f", 4)}
NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}


def load_glb(path):
    with open(path, "rb") as f:
        d = f.read()
    if d[:4] != b"glTF":
        raise ValueError("%s は GLB ではありません" % path)
    off, js, bin_ = 12, None, b""
    while off + 8 <= len(d):
        clen, ctype = struct.unpack_from("<I4s", d, off)
        data = d[off + 8:off + 8 + clen]
        if ctype == b"JSON":
            js = json.loads(data.decode("utf-8"))
        elif ctype == b"BIN\x00":
            bin_ = data
        off += 8 + clen
        if clen % 4:
            off += 4 - clen % 4
    return js, bin_


def read_accessor(js, bin_, i):
    """accessor を平坦な array に解決する (要素数 = count * 成分数)。sparse も展開する。
    大きいモデルでも実用的な速度になるよう array.frombytes でまとめて読む。"""
    a = js["accessors"][i]
    n = a["count"]
    nc = NCOMP[a["type"]]
    fmt, size = COMP[a["componentType"]]
    out = array.array(fmt, bytes(n * nc * size))

    if "bufferView" in a:
        bv = js["bufferViews"][a["bufferView"]]
        base = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride") or nc * size
        if stride == nc * size:
            out = array.array(fmt)
            out.frombytes(bin_[base:base + n * nc * size])
        else:
            for k in range(n):
                out[k * nc:(k + 1) * nc] = array.array(
                    fmt, bin_[base + k * stride: base + k * stride + nc * size])

    sp = a.get("sparse")
    if sp:
        ibv = js["bufferViews"][sp["indices"]["bufferView"]]
        ifmt, isize = COMP[sp["indices"]["componentType"]]
        ibase = ibv.get("byteOffset", 0) + sp["indices"].get("byteOffset", 0)
        idxs = array.array(ifmt)
        idxs.frombytes(bin_[ibase:ibase + sp["count"] * isize])
        vbv = js["bufferViews"][sp["values"]["bufferView"]]
        vbase = vbv.get("byteOffset", 0) + sp["values"].get("byteOffset", 0)
        vals = array.array(fmt)
        vals.frombytes(bin_[vbase:vbase + sp["count"] * nc * size])
        for k, vi in enumerate(idxs):
            out[vi * nc:(vi + 1) * nc] = vals[k * nc:(k + 1) * nc]
    return out, nc


def image_bufferviews(js):
    return {img["bufferView"] for img in js.get("images", []) if "bufferView" in img}


def strip_offsets(js):
    """バイナリの配置に依存する値を落とした JSON を返す
    (中身は accessor / 画像単位で別途比較するため)。
    画像は PNG エンコーダが違うのでバイト長も一致しない。画素は別途比較する。"""
    j = json.loads(json.dumps(js))
    imgs = image_bufferviews(js)
    for i, bv in enumerate(j.get("bufferViews", [])):
        bv.pop("byteOffset", None)
        if i in imgs:
            bv.pop("byteLength", None)
    for b in j.get("buffers", []):
        b.pop("byteLength", None)
    return j


def compare_images(ja, ba, jb, bb, verbose):
    """埋め込み画像を画素単位で比較する。PNG のバイト列はエンコーダが違うので一致しない。"""
    ia, ib = ja.get("images", []), jb.get("images", [])
    if len(ia) != len(ib):
        print("画像: 枚数が違います %d vs %d" % (len(ia), len(ib)))
        return 1
    if not ia:
        return 0
    try:
        from PIL import Image
    except ImportError:
        print("画像: Pillow が無いので画素比較をスキップしました (%d 枚)" % len(ia))
        return 0
    import io

    def blob(js, bin_, img):
        bv = js["bufferViews"][img["bufferView"]]
        o = bv.get("byteOffset", 0)
        return bin_[o:o + bv["byteLength"]]

    bad = 0
    tolerated = 0
    for k, (x, y) in enumerate(zip(ia, ib)):
        da, db = blob(ja, ba, x), blob(jb, bb, y)
        if da == db:
            continue
        try:
            pa = Image.open(io.BytesIO(da)).convert("RGBA")
            pb = Image.open(io.BytesIO(db)).convert("RGBA")
        except Exception as e:
            print("画像[%d] %s: デコードできません (%s)" % (k, x.get("name"), e))
            bad += 1
            continue
        if pa.size != pb.size:
            print("画像[%d] %s: サイズが違います %s vs %s"
                  % (k, x.get("name"), pa.size, pb.size))
            bad += 1
            continue
        worst = 0
        for u, v in zip(pa.getdata(), pb.getdata()):
            d = max(abs(u[i] - v[i]) for i in range(4))
            if d > worst:
                worst = d
        if worst == 0:
            continue
        if worst <= 1:
            # 16bit PNG の 8bit 化の差 (PIL=切り捨て / ImageSharp=スケーリング)。
            tolerated += 1
            continue
        print("画像[%d] %s: 画素の最大差 %d" % (k, x.get("name"), worst))
        bad += 1
    print("画像: %d 枚中 %d 枚が最大差1で許容、%d 枚が不一致" % (len(ia), tolerated, bad))
    return bad


diffs = []
color_diffs = []


def cmp(path, a, b, ctol):
    if len(diffs) > 20000:
        return
    if isinstance(a, bool) or isinstance(b, bool):
        if a != b:
            diffs.append((path, a, b))
        return
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        if a == b:
            return
        # スフィアマップ平均色のフォールバックだけは縮小補間の実装差で ~1e-4 ずれる
        if "baseColorFactor" in path:
            if abs(a - b) > ctol:
                diffs.append((path, a, b))
            else:
                color_diffs.append((path, a, b))
            return
        diffs.append((path, a, b))
        return
    if type(a) is not type(b):
        diffs.append((path, "type %s" % type(a).__name__, "type %s" % type(b).__name__))
        return
    if isinstance(a, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a:
                diffs.append(("%s.%s" % (path, k), "<なし>", b[k]))
            elif k not in b:
                diffs.append(("%s.%s" % (path, k), a[k], "<なし>"))
            else:
                cmp("%s.%s" % (path, k), a[k], b[k], ctol)
        return
    if isinstance(a, list):
        if len(a) != len(b):
            diffs.append(("%s (長さ)" % path, len(a), len(b)))
            return
        for i, (x, y) in enumerate(zip(a, b)):
            cmp("%s[%d]" % (path, i), x, y, ctol)
        return
    if a != b:
        diffs.append((path, a, b))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--ftol", type=float, default=1e-6,
                    help="浮動小数 accessor の相対許容差 (既定 1e-6)")
    ap.add_argument("--ctol", type=float, default=1e-3,
                    help="baseColorFactor の絶対許容差 (既定 1e-3)")
    ap.add_argument("--max", type=int, default=20)
    args = ap.parse_args()

    ja, ba = load_glb(args.a)
    jb, bb = load_glb(args.b)
    print("python: JSON %d bytes / BIN %d bytes" % (len(json.dumps(ja)), len(ba)))
    print("C#    : JSON %d bytes / BIN %d bytes" % (len(json.dumps(jb)), len(bb)))

    cmp("", strip_offsets(ja), strip_offsets(jb), args.ctol)
    n_json = len(diffs)
    for path, x, y in diffs[:args.max]:
        sx, sy = repr(x), repr(y)
        print("JSON %s\n    python: %s\n    C#    : %s"
              % (path or "<ルート>", sx[:70], sy[:70]))
    print("JSON の差分: %d 件%s" % (n_json,
                                 " (先頭 %d 件のみ表示)" % args.max
                                 if n_json > args.max else ""))
    if color_diffs:
        worst_c = max(abs(x - y) for _, x, y in color_diffs)
        print("  うち baseColorFactor で許容: %d 値 (最大 %.2e / スフィア平均色の縮小補間差)"
              % (len(color_diffs), worst_c))

    n_img = compare_images(ja, ba, jb, bb, args.max)

    if ba == bb:
        print("BIN: 完全一致")
        return 1 if (n_json or n_img) else 0

    print("BIN: バイト列は不一致 (画像のエンコーダ差を含む)。accessor 単位で中身を比較します。")
    bad = 0
    na = len(ja.get("accessors", []))
    nb2 = len(jb.get("accessors", []))
    if na != nb2:
        print("  accessor の数が違います: %d vs %d" % (na, nb2))
        return 1
    worst = 0.0
    worst_at = None
    n_exact = 0
    n_tolerated = 0
    for i in range(na):
        is_float = ja["accessors"][i]["componentType"] == 5126
        va, nca = read_accessor(ja, ba, i)
        vb, _ = read_accessor(jb, bb, i)
        if len(va) != len(vb):
            print("  accessor[%d]: 要素数が違います %d vs %d" % (i, len(va), len(vb)))
            bad += 1
            continue
        if va == vb:            # 大半はここで終わる (バイト単位で同一)
            n_exact += 1
            continue
        for k in range(len(va)):
            x, y = va[k], vb[k]
            if x == y:
                continue
            d = abs(x - y)
            rel = d / max(abs(x), abs(y), 1e-30)
            if rel > worst:
                worst, worst_at = rel, (i, k // nca, k % nca, x, y)
            # 整数 accessor (index / JOINTS_0 等) は完全一致でなければ不合格
            if is_float and rel <= args.ftol:
                n_tolerated += 1
            else:
                bad += 1
    print("  accessor %d 本中 %d 本が完全一致" % (na, n_exact))
    if n_tolerated:
        print("  float32 の分解能内で許容した値: %d 個 (最大相対差 %.2e)" % (n_tolerated, worst))
    if bad and worst_at:
        i, k, c, x, y = worst_at
        print("  最大相対差 %.3e  accessor[%d] 要素%d 成分%d: %r vs %r" % (worst, i, k, c, x, y))
    print("  許容外の値: %d 個" % bad)
    return 1 if (n_json or bad or n_img) else 0


if __name__ == "__main__":
    sys.exit(main())
