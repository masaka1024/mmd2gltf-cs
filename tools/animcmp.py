# -*- coding: utf-8 -*-
"""2つの GLB のアニメーションを (ノード, path) 単位で突き合わせる。

    python tools/animcmp.py a.glb b.glb [--tol 1e-6] [--max 20]

チャンネルの並び順に依存せず、同じボーンの同じ path 同士を比べる。
統合パス (mmd2gltf convert --bake-physics) と2段パス
(mmd2gltf convert → physbake) の結果を比べるために作った。

★重要: 揺れ物の物理はフレーム単位では一致しない。実測 (モデルA + 短いモーション, 61フレーム) で、
  同じコードパスに 1e-7 の入力差 (float32 丸め) を与えるだけで、
  クォータニオン成分が最大 0.83 ずれる。自由に揺れる髪はカオス的で、
  経路が違えば必ず位相がずれる。
  したがって物理チャンネルは「フレーム単位の一致」ではなく
  「振幅などの統計量が揃っているか」で判定すること (--stats)。
  FK チャンネル (物理でないボーン) は完全一致を要求してよい。
"""
import argparse
import sys

from glbdiff import load_glb, read_accessor


def channels(js, bin_, ai=0):
    """(ノードindex, path) -> (時刻列, 値列, 成分数) を返す。"""
    out = {}
    anims = js.get("animations", [])
    if not anims:
        return out
    a = anims[ai]
    for ch in a["channels"]:
        t = ch.get("target", {})
        if "node" not in t:
            continue
        s = a["samplers"][ch["sampler"]]
        times, _ = read_accessor(js, bin_, s["input"])
        vals, nc = read_accessor(js, bin_, s["output"])
        out[(t["node"], t["path"])] = (list(times), list(vals), nc)
    return out


def node_names(js):
    return [n.get("name", "?") for n in js.get("nodes", [])]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--tol", type=float, default=1e-6,
                    help="相対許容差 (既定 1e-6。glTF は float32 なので分解能は約 6e-8)")
    ap.add_argument("--max", type=int, default=20)
    ap.add_argument("--stats", action="store_true",
                    help="振幅 (動きの大きさ) の比較も出す。物理チャンネルはこちらで判定する")
    args = ap.parse_args()

    ja, ba = load_glb(args.a)
    jb, bb = load_glb(args.b)
    ca, cb = channels(ja, ba), channels(jb, bb)
    names = node_names(ja)

    only_a = sorted(set(ca) - set(cb))
    only_b = sorted(set(cb) - set(ca))
    for k in only_a[:args.max]:
        print("A にしか無いチャンネル: node %d '%s' %s" % (k[0], names[k[0]], k[1]))
    for k in only_b[:args.max]:
        print("B にしか無いチャンネル: node %d '%s' %s" % (k[0], names[k[0]], k[1]))
    print("チャンネル数: A %d / B %d / 共通 %d"
          % (len(ca), len(cb), len(set(ca) & set(cb))))

    rows = []
    shown = 0
    for k in sorted(set(ca) & set(cb)):
        ta, va, nc = ca[k]
        tb, vb, _ = cb[k]
        if ta != tb:
            print("時刻列が違います: node %d '%s' %s" % (k[0], names[k[0]], k[1]))
            continue
        if len(va) != len(vb):
            print("値の数が違います: node %d '%s' %s" % (k[0], names[k[0]], k[1]))
            continue
        # ★成分ごとの相対差は使わない。ほぼゼロの成分同士 (1e-9 と -1e-9) で
        #   相対差 2.0 になってしまい、実質同じ姿勢を「全然違う」と誤判定する。
        #   チャンネル全体の大きさ (成分の最大絶対値) で正規化する。
        scale = max((abs(x) for x in va), default=0.0)
        scale = max(scale, max((abs(y) for y in vb), default=0.0), 1e-12)
        maxdiff = 0.0
        for x, y in zip(va, vb):
            d = abs(x - y)
            if d > maxdiff:
                maxdiff = d
        rows.append((maxdiff / scale, maxdiff, scale, k))

    rows.sort(reverse=True)
    n_bad = sum(1 for r, _, _, _ in rows if r > args.tol)
    for rel, maxdiff, scale, k in rows:
        if rel <= args.tol or shown >= args.max:
            continue
        print("差 node %-4d '%s' %-11s 最大差 %.3e (振幅 %.3e の %.2e 倍)"
              % (k[0], names[k[0]], k[1], maxdiff, scale, rel))
        shown += 1
    exact = sum(1 for _, d, _, _ in rows if d == 0.0)
    print("共通チャンネル %d 本中 %d 本が完全一致、%d 本が許容外の差あり"
          % (len(rows), exact, n_bad))
    if rows:
        print("全体の最大: 振幅比 %.3e / 絶対差 %.3e"
              % (max(r for r, _, _, _ in rows), max(d for _, d, _, _ in rows)))

    if args.stats:
        stats_report(ca, cb, names, args.max)
    return 1 if (only_a or only_b or n_bad) else 0


def stats_report(ca, cb, names, top):
    """振幅 (rest からの変位の RMS) を比べる。
    カオスで位相はずれるが、揺れの大きさが揃っていれば物理としては同等。"""
    import math

    def amplitude(v, nc, path):
        n = len(v) // nc
        if path == "rotation":
            # ★クォータニオンは q と -q が同じ姿勢なので、成分の RMS で測ると
            #   連続性のための符号反転を「大きく動いた」と誤って拾う。
            #   符号に依存しない「回転角」で測る (angle = 2*acos(|w|))。
            angs = [2.0 * math.acos(min(1.0, abs(v[i * nc + 3]))) for i in range(n)]
            m = sum(angs) / n
            return math.sqrt(sum((a - m) ** 2 for a in angs) / n)
        tot = 0.0
        for c in range(nc):
            col = v[c::nc]
            m = sum(col) / n
            tot += sum((x - m) ** 2 for x in col) / n
        return math.sqrt(tot)

    rows = []
    for k in sorted(set(ca) & set(cb)):
        _, va, nc = ca[k]
        _, vb, _ = cb[k]
        if len(va) != len(vb) or not va:
            continue
        ra, rb = amplitude(va, nc, k[1]), amplitude(vb, nc, k[1])
        if max(ra, rb) < 1e-9:
            continue
        rows.append((abs(ra - rb) / max(ra, rb), ra, rb, k))
    if not rows:
        print("振幅を比較できるチャンネルがありません。")
        return
    rows.sort(reverse=True)
    print("--- 振幅 (動きの大きさ) の比較 ---")
    for rel, ra, rb, k in rows[:top]:
        print("  node %-4d '%s' %-11s A %.4f / B %.4f  (差 %.1f%%)"
              % (k[0], names[k[0]], k[1], ra, rb, rel * 100))
    med = sorted(r for r, _, _, _ in rows)[len(rows) // 2]
    print("  振幅の相対差: 中央 %.2f%% / 最大 %.2f%% (%d チャンネル)"
          % (med * 100, rows[0][0] * 100, len(rows)))


if __name__ == "__main__":
    sys.exit(main())
