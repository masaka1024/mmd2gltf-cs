// ===========================================================================
// VMD モーションを PMX スケルトンへ焼き付ける。Python 版 animation.py の移植。
//
// すべて MMD ネイティブの左手座標系で動く。1フレームあたりの手順 (30fps, VMD native):
//   1. VMD トラックを評価する (X/Y/Z/R 各チャンネルのベジェ補間)
//   2. MMD の変形順 (物理後フラグ, 変形階層, index) にボーンを処理する
//      - 回転付与 / 移動付与
//      - 角度制限つき CCD で IK チェーンを解く
//   3. 全ボーンのローカル移動 / 回転を記録する
//
// 物理はここでは扱わない。物理ボーンはアニメと親から来た姿勢のままになり、
// 揺れは後段の physbake (自作エンジン) が上書きする。
// ===========================================================================
using System;
using System.Collections.Generic;
using Mmd2Gltf.Pmx;
using Mmd2Gltf.Vmd;

namespace Mmd2Gltf.Animation
{
    /// <summary>1ボーン分の焼き付け結果。動きの無いチャンネルは null。</summary>
    public sealed class BakedBoneTrack
    {
        public Quatd[] Rotations;      // null = 回転していない
        public Vec3d[] Translations;   // null = rest から動いていない (rest 込みの値)
    }

    public sealed class BakedMotion
    {
        public double[] Times;                              // 秒
        public Dictionary<int, BakedBoneTrack> Bones = new(); // ボーン index -> トラック
        public List<string> UnmatchedVmdBones = new();      // PMX に無かった VMD トラック名
        /// <summary>移動可でないのに VMD が移動値を持っていたボーン名 (移動は無視した)。</summary>
        public List<string> IgnoredTranslationBones = new();
    }

    /// <summary>1ボーン分の VMD キー列。MMD のベジェ補間つき。</summary>
    internal sealed class Track
    {
        private readonly List<VmdBoneKey> _keys;
        private readonly uint[] _frames;

        public Track(List<VmdBoneKey> keys)
        {
            _keys = keys;
            _frames = new uint[keys.Count];
            for (int i = 0; i < keys.Count; i++) _frames[i] = keys[i].Frame;
        }

        public void Sample(int frame, out Vec3d pos, out Quatd rot)
        {
            if (frame <= _frames[0]) { Take(_keys[0], out pos, out rot); return; }
            if (frame >= _frames[^1]) { Take(_keys[^1], out pos, out rot); return; }

            int lo = 0, hi = _frames.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_frames[mid] <= frame) lo = mid; else hi = mid;
            }
            var k0 = _keys[lo];
            var k1 = _keys[hi];
            double span = (double)k1.Frame - k0.Frame;
            double x = span != 0 ? (frame - k0.Frame) / span : 0.0;

            var p = new double[3];
            for (int axis = 0; axis < 3; axis++)
            {
                var c = k1.Curves[axis];
                double t = VmdParser.CurveIsLinear(c)
                    ? x
                    : VmdParser.BezierY(x, c[0] / 127.0, c[1] / 127.0, c[2] / 127.0, c[3] / 127.0);
                // ★double へ広げてから引くこと。float 同士で引くと 1e-8 級の差が出て、
                //   それが IK 経由で回転にまで伝播する (Python 側は float64)。
                double a = k0.Pos[axis], b = k1.Pos[axis];
                p[axis] = a + (b - a) * t;
            }
            pos = new Vec3d(p[0], p[1], p[2]);

            var cr = k1.Curves[3];
            double tr = VmdParser.CurveIsLinear(cr)
                ? x
                : VmdParser.BezierY(x, cr[0] / 127.0, cr[1] / 127.0, cr[2] / 127.0, cr[3] / 127.0);
            rot = MmdMath.Slerp(Q(k0).Normalized, Q(k1).Normalized, tr);
        }

        private static void Take(VmdBoneKey k, out Vec3d pos, out Quatd rot)
        {
            pos = new Vec3d(k.Pos[0], k.Pos[1], k.Pos[2]);
            rot = Q(k);
        }

        private static Quatd Q(VmdBoneKey k) => new(k.Rot[0], k.Rot[1], k.Rot[2], k.Rot[3]);
    }

    internal sealed class Skeleton
    {
        private readonly List<PmxBone> _bones;
        public readonly Vec3d[] Rest;      // 親からのローカル rest オフセット
        public readonly int[] Order;       // 変形順 (物理後, 変形階層, index)
        public Quatd[] LocalQ;
        public Vec3d[] LocalT;             // アニメによるオフセット (rest に加算)
        /// <summary>移動可フラグ(0x0004)。MMD は移動可でないボーンの VMD 移動値を
        /// 適用しないため、ベイクでも同じ扱いにする (適用すると、物理焼き込み系ツールが
        /// 書き出したフルキー VMD に残る移動値が回転専用ボーンへ乗り、glTF 側で
        /// translation チャンネルとして出力され、受け手の物理・姿勢参照を壊す)。</summary>
        public readonly bool[] Movable;

        private readonly Quatd[] _worldQ;
        private readonly Vec3d[] _worldP;
        private readonly bool[] _worldValid;

        public Skeleton(List<PmxBone> bones)
        {
            _bones = bones;
            int n = bones.Count;
            Movable = new bool[n];
            for (int i = 0; i < n; i++) Movable[i] = (bones[i].Flags & 0x0004) != 0;
            Rest = new Vec3d[n];
            for (int i = 0; i < n; i++)
            {
                int p = bones[i].Parent;
                var pos = new Vec3d(bones[i].Pos[0], bones[i].Pos[1], bones[i].Pos[2]);
                Rest[i] = (p >= 0 && p < n)
                    ? pos - new Vec3d(bones[p].Pos[0], bones[p].Pos[1], bones[p].Pos[2])
                    : pos;
            }

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int pa = (bones[a].Flags & 0x1000) != 0 ? 1 : 0;   // 物理後変形
                int pb = (bones[b].Flags & 0x1000) != 0 ? 1 : 0;
                if (pa != pb) return pa - pb;
                if (bones[a].Layer != bones[b].Layer) return bones[a].Layer - bones[b].Layer;
                return a - b;
            });
            Order = order;

            LocalQ = new Quatd[n];
            LocalT = new Vec3d[n];
            _worldQ = new Quatd[n];
            _worldP = new Vec3d[n];
            _worldValid = new bool[n];
        }

        public void Reset()
        {
            for (int i = 0; i < LocalQ.Length; i++) { LocalQ[i] = Quatd.Identity; LocalT[i] = Vec3d.Zero; }
            Invalidate();
        }

        public void Invalidate() => Array.Clear(_worldValid, 0, _worldValid.Length);

        public void World(int i, out Quatd wq, out Vec3d wp)
        {
            if (_worldValid[i]) { wq = _worldQ[i]; wp = _worldP[i]; return; }
            int p = _bones[i].Parent;
            var lt = Rest[i] + LocalT[i];
            if (p >= 0 && p < _bones.Count)
            {
                World(p, out var pq, out var pp);
                wq = pq * LocalQ[i];
                wp = pp + MmdMath.Rotate(pq, lt);
            }
            else
            {
                wq = LocalQ[i];
                wp = lt;
            }
            _worldQ[i] = wq; _worldP[i] = wp; _worldValid[i] = true;
        }

        private Vec3d WorldPos(int i) { World(i, out _, out var p); return p; }

        /// <summary>1フレーム分の変形を流す。ikEnabled が null なら全 IK を解く。</summary>
        public void Pose(Dictionary<int, (Vec3d pos, Quatd rot)> sampled, HashSet<int> ikEnabled)
        {
            Reset();
            foreach (var kv in sampled)
            {
                if (Movable[kv.Key]) LocalT[kv.Key] = kv.Value.pos;
                LocalQ[kv.Key] = kv.Value.rot.Normalized;
            }

            foreach (int i in Order)
            {
                var b = _bones[i];
                int fl = b.Flags;
                if ((fl & (0x0100 | 0x0200)) != 0)
                {
                    int src = b.InheritParent ?? -1;
                    double ratio = b.InheritRatio ?? 1.0;
                    if (src >= 0 && src < _bones.Count)
                    {
                        if ((fl & 0x0100) != 0)   // 回転付与
                        {
                            var add = MmdMath.Slerp(Quatd.Identity, LocalQ[src], ratio);
                            LocalQ[i] = (add * LocalQ[i]).Normalized;
                        }
                        if ((fl & 0x0200) != 0)   // 移動付与
                            LocalT[i] = LocalT[i] + LocalT[src] * ratio;
                        Invalidate();
                    }
                }
                if ((fl & 0x0020) != 0 && b.Ik != null)
                    if (ikEnabled == null || ikEnabled.Contains(i))
                        SolveIk(i, b.Ik);
            }
        }

        private void SolveIk(int ikBone, PmxIk ik)
        {
            int eff = ik.Target;
            if (eff < 0 || eff >= _bones.Count) return;
            var goal = WorldPos(ikBone);
            double limit = ik.LimitAngle;

            for (int loop = 0; loop < Math.Max(1, ik.Loop); loop++)
            {
                bool done = false;
                for (int li = 0; li < ik.Links.Count; li++)
                {
                    var link = ik.Links[li];
                    int j = link.Bone;
                    World(j, out var lq, out var lp);
                    var effPos = WorldPos(eff);
                    if ((effPos - goal).Length < 1e-4) { done = true; break; }

                    var inv = lq.Inverse;
                    var v1 = MmdMath.Rotate(inv, effPos - lp).Normalized;
                    var v2 = MmdMath.Rotate(inv, goal - lp).Normalized;
                    double d = Math.Max(-1.0, Math.Min(1.0, Vec3d.Dot(v1, v2)));
                    double ang = Math.Acos(d);
                    if (ang < 1e-6) continue;
                    ang = Math.Min(ang, limit * (li + 1));
                    var axis = Vec3d.Cross(v1, v2).Normalized;
                    if (axis.Length < 1e-6) continue;

                    LocalQ[j] = (LocalQ[j] * MmdMath.FromAxisAngle(axis, ang)).Normalized;
                    if (link.Min != null)
                    {
                        var e = MmdMath.ToEulerXyz(LocalQ[j]);
                        var c = new Vec3d(
                            Math.Max(link.Min[0], Math.Min(link.Max[0], e.X)),
                            Math.Max(link.Min[1], Math.Min(link.Max[1], e.Y)),
                            Math.Max(link.Min[2], Math.Min(link.Max[2], e.Z)));
                        LocalQ[j] = MmdMath.FromEulerXyz(c);
                    }
                    Invalidate();
                }
                if (done) break;
            }
        }
    }

    public static class AnimationBaker
    {
        public const double Fps = 30.0;

        /// <param name="disableIk">名前にこれらの文字列を含む IK ボーンは解かない (例: "足")。</param>
        /// <param name="useVmdIkFrames">VMD に入っている IK on/off キーを尊重する。</param>
        public static BakedMotion Bake(PmxModel model, VmdMotion vmd,
                                       bool solveIk = true, int step = 1,
                                       IEnumerable<string> disableIk = null,
                                       bool useVmdIkFrames = true,
                                       Action<int, int> progress = null)
        {
            var bones = model.Bones;
            int n = bones.Count;

            // VMD の名前は Shift-JIS 15バイトまでなので、同じ切り詰めで対応表を作る。
            var nameMap = new Dictionary<byte[], int>(MmdEncoding.ByteArrayComparer.Instance);
            for (int i = 0; i < n; i++)
            {
                var key = MmdEncoding.TruncatedKey(bones[i].Name, 15);
                if (!nameMap.ContainsKey(key)) nameMap[key] = i;   // 先勝ち (Python の setdefault)
            }

            var result = new BakedMotion();
            var tracks = new Dictionary<int, Track>();
            foreach (var kv in vmd.Bones)
            {
                var key = MmdEncoding.TruncatedKey(kv.Key, 15);
                if (!nameMap.TryGetValue(key, out int bi))
                {
                    result.UnmatchedVmdBones.Add(kv.Key);
                    continue;
                }
                tracks[bi] = new Track(kv.Value);

                // 移動可(0x0004)でないボーンのトラックに移動値が入っている場合、その移動値は
                // MMD 同様に適用しない (Skeleton.Movable 参照)。ここでは報告用に数えるだけ。
                if ((bones[bi].Flags & 0x0004) == 0)
                {
                    foreach (var k in kv.Value)
                    {
                        if (Math.Abs(k.Pos[0]) + Math.Abs(k.Pos[1]) + Math.Abs(k.Pos[2]) > 1e-9)
                        {
                            result.IgnoredTranslationBones.Add(bones[bi].Name);
                            break;
                        }
                    }
                }
            }

            var sk = new Skeleton(bones);

            // ---- どの IK を解いてよいか ----------------------------------------
            var allIk = new List<int>();
            for (int i = 0; i < n; i++)
                if ((bones[i].Flags & 0x0020) != 0 && bones[i].Ik != null) allIk.Add(i);

            var baseEnabled = new HashSet<int>(solveIk ? allIk : new List<int>());
            if (disableIk != null)
                foreach (int i in allIk)
                    foreach (string pat in disableIk)
                        if (!string.IsNullOrEmpty(pat) && bones[i].Name.Contains(pat))
                        { baseEnabled.Remove(i); break; }

            // VMD の IK on/off キー (名前は 20 バイトで切り詰め)
            var ikKeyFrames = new List<(uint frame, Dictionary<int, bool> map)>();
            if (useVmdIkFrames && vmd.IkFrames.Count > 0)
            {
                var ikNameMap = new Dictionary<byte[], int>(MmdEncoding.ByteArrayComparer.Instance);
                foreach (int i in allIk)
                {
                    var k = MmdEncoding.TruncatedKey(bones[i].Name, 20);
                    if (!ikNameMap.ContainsKey(k)) ikNameMap[k] = i;
                }
                foreach (var fr in vmd.IkFrames)
                {
                    var map = new Dictionary<int, bool>();
                    foreach (var kv in fr.Ik)
                        if (ikNameMap.TryGetValue(MmdEncoding.TruncatedKey(kv.Key, 20), out int i))
                            map[i] = kv.Value;
                    ikKeyFrames.Add((fr.Frame, map));
                }
            }

            HashSet<int> EnabledAt(int frame)
            {
                if (ikKeyFrames.Count == 0) return baseEnabled;
                var state = new Dictionary<int, bool>();
                foreach (var (f0, map) in ikKeyFrames)
                {
                    if (f0 > frame) break;
                    foreach (var kv in map) state[kv.Key] = kv.Value;
                }
                var set = new HashSet<int>();
                foreach (int i in baseEnabled)
                    if (!state.TryGetValue(i, out bool on) || on) set.Add(i);
                return set;
            }

            // ---- フレーム列 ------------------------------------------------------
            int last = (int)vmd.MaxFrame;
            var frames = new List<int>();
            for (int f = 0; f <= last; f += Math.Max(1, step)) frames.Add(f);
            if (frames[^1] != last) frames.Add(last);

            result.Times = new double[frames.Count];
            for (int i = 0; i < frames.Count; i++) result.Times[i] = frames[i] / Fps;

            var rots = new Quatd[n][];
            var trans = new Vec3d[n][];
            for (int i = 0; i < n; i++) { rots[i] = new Quatd[frames.Count]; trans[i] = new Vec3d[frames.Count]; }

            var sampled = new Dictionary<int, (Vec3d, Quatd)>();
            for (int fi = 0; fi < frames.Count; fi++)
            {
                int f = frames[fi];
                sampled.Clear();
                foreach (var kv in tracks)
                {
                    kv.Value.Sample(f, out var p, out var q);
                    sampled[kv.Key] = (p, q);
                }
                sk.Pose(sampled, EnabledAt(f));
                for (int i = 0; i < n; i++)
                {
                    rots[i][fi] = sk.LocalQ[i];
                    trans[i][fi] = sk.Rest[i] + sk.LocalT[i];
                }
                if (progress != null && fi % 200 == 0) progress(fi, frames.Count);
            }

            // ---- 動きの無いチャンネルは落とす ------------------------------------
            for (int i = 0; i < n; i++)
            {
                bool animR = false;
                foreach (var q in rots[i])
                    if (Math.Abs(q.X) + Math.Abs(q.Y) + Math.Abs(q.Z) > 1e-7 || q.W < 0.999999)
                    { animR = true; break; }

                bool animT = false;
                foreach (var t in trans[i])
                    if ((t - sk.Rest[i]).Length > 1e-6) { animT = true; break; }

                if (animR || animT)
                    result.Bones[i] = new BakedBoneTrack
                    {
                        Rotations = animR ? rots[i] : null,
                        Translations = animT ? trans[i] : null,
                    };
            }
            return result;
        }
    }
}
