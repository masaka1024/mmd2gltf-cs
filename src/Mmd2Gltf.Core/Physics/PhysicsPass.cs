// ===========================================================================
// 変換パイプラインの中で揺れ物の物理を焼くパス。
//
// physbake (GLB を読み直す単体ツール) と同じエンジン・同じ駆動式を使うが、
// glTF の accessor を経由しない分だけ入力が精密になる:
//   physbake  : glTF に書いた float32 のローカル姿勢を読み戻して FK
//   このパス  : FK ベイクの float64 の結果をそのまま使う
// どちらが正しいかではなく、丸めが1回少ないのはこちら。
//
// 出力は「物理ボーンの、親から見たローカル姿勢 (MMD raw 空間)」。
// glTF 空間への変換とチャンネル書き出しは呼び出し側 (MmdConverter) が行う。
// ===========================================================================
using System;
using System.Collections.Generic;
using BulletPhysics;
using Mmd2Gltf.Animation;
using Mmd2Gltf.Pmx;

namespace Mmd2Gltf.Physics
{
    public sealed class PhysicsPassResult
    {
        /// <summary>ボーン index -> フレーム毎のローカル回転 (MMD raw)。</summary>
        public Dictionary<int, Quatd[]> LocalRotations = new();
        /// <summary>ボーン index -> フレーム毎のローカル並進 (MMD raw, rest 込み)。</summary>
        public Dictionary<int, Vec3d[]> LocalTranslations = new();
        public int[] PhysicsBones = Array.Empty<int>();
        public int StepCount;
        public double Seconds;
        public string NanBone;
        /// <summary>診断: 物理が FK からどれだけ動かしたか (MMD 単位)。</summary>
        public double MoveMedian, MoveP90, MoveMax;
        public string MoveMaxBone;
        /// <summary>診断: リンクの伸び |ローカル並進| - |rest| (MMD 単位)。</summary>
        public double StretchMedian, StretchP90, StretchMax;
        public string StretchMaxBone;
    }

    public static class PhysicsPass
    {
        public static PhysicsPassResult Run(PmxModel model, BakedMotion baked,
                                            BakeOptions opt, double unitScale,
                                            Action<string> log)
        {
            int nb = model.Bones.Count;
            int frames = baked.Times.Length;

            // rest ローカル (親からのオフセット)。バインドは回転恒等。
            var rest = new Vec3d[nb];
            for (int i = 0; i < nb; i++)
            {
                var p = model.Bones[i].Parent;
                var self = new Vec3d(model.Bones[i].Pos[0], model.Bones[i].Pos[1], model.Bones[i].Pos[2]);
                rest[i] = (p >= 0 && p < nb)
                    ? self - new Vec3d(model.Bones[p].Pos[0], model.Bones[p].Pos[1], model.Bones[p].Pos[2])
                    : self;
            }

            // 親→子の順に並べる (FK 合成順)。
            var order = TopologicalOrder(model);

            // 診断用: FK 入力を「glTF に書いてから読み戻した精度」へ落とす。
            // physbake (GLB を読み直す経路) は float32 のローカル姿勢から FK するので、
            // これを ON にすると両経路の入力が揃う。差がどこから来るかの切り分け用。
            bool fkFloat32 = Environment.GetEnvironmentVariable("PHYS_FK_FLOAT32") == "1";

            // FK: ベイク結果 (double) からボーン world 姿勢を作る。
            var fkWorld = new RigidTransform?[frames][];
            var fkWorldD = new (Quatd rot, Vec3d pos)[frames][];
            for (int f = 0; f < frames; f++)
            {
                var rowD = new (Quatd, Vec3d)[nb];
                var row = new RigidTransform?[nb];
                foreach (int b in order)
                {
                    baked.Bones.TryGetValue(b, out var trk);
                    var lq = trk?.Rotations != null ? trk.Rotations[f] : Quatd.Identity;
                    var lt = trk?.Translations != null ? trk.Translations[f] : rest[b];
                    if (fkFloat32)
                    {
                        lq = new Quatd((float)lq.X, (float)lq.Y, (float)lq.Z, (float)lq.W);
                        // 並進は glTF 空間 (スケール込み) で float32 になる
                        lt = new Vec3d((float)(lt.X * unitScale) / unitScale,
                                       (float)(lt.Y * unitScale) / unitScale,
                                       -(double)(float)(-lt.Z * unitScale) / unitScale);
                    }

                    int p = model.Bones[b].Parent;
                    Quatd wq;
                    Vec3d wp;
                    if (p >= 0 && p < nb)
                    {
                        var (pq, pp) = rowD[p];
                        wq = pq * lq;
                        wp = pp + MmdMath.Rotate(pq, lt);
                    }
                    else { wq = lq; wp = lt; }
                    rowD[b] = (wq, wp);
                    row[b] = ToRigid(wq, wp);
                }
                fkWorldD[f] = rowD;
                fkWorld[f] = row;
            }

            var times = new float[frames];
            for (int f = 0; f < frames; f++) times[f] = (float)baked.Times[f];

            var engineModel = EnginePhysicsModel.From(model);
            var bake = PhysicsBaker.Run(engineModel, fkWorld, times, opt, log);

            var result = new PhysicsPassResult
            {
                PhysicsBones = bake.PhysicsBones,
                StepCount = bake.StepCount,
                Seconds = bake.Seconds,
                NanBone = bake.NanBone,
            };
            if (bake.PhysicsBones.Length == 0) return result;

            // 最終 world = FK を物理結果で上書き。親が物理ボーンでも正しく相対を取れる。
            var finalWorld = new RigidTransform[frames][];
            var isPhysics = new bool[nb];
            foreach (int b in bake.PhysicsBones) isPhysics[b] = true;
            for (int f = 0; f < frames; f++)
            {
                var row = new RigidTransform[nb];
                for (int b = 0; b < nb; b++)
                {
                    var (wq, wp) = fkWorldD[f][b];
                    row[b] = isPhysics[b] ? bake.BoneWorld[f][b] : ToRigid(wq, wp);
                }
                finalWorld[f] = row;
            }

            var moves = new List<double>(bake.PhysicsBones.Length * frames);
            var stretches = new List<double>(bake.PhysicsBones.Length * frames);
            foreach (int b in bake.PhysicsBones)
            {
                var rots = new Quatd[frames];
                var poss = new Vec3d[frames];
                int p = model.Bones[b].Parent;
                double restLen = rest[b].Length;
                double moveB = 0, stretchB = 0;

                for (int f = 0; f < frames; f++)
                {
                    var wparent = (p >= 0 && p < nb) ? finalWorld[f][p] : RigidTransform.Identity;
                    var local = wparent.Inverse() * finalWorld[f][b];
                    rots[f] = new Quatd(local.Rotation.x, local.Rotation.y, local.Rotation.z, local.Rotation.w);
                    poss[f] = new Vec3d(local.Origin.x, local.Origin.y, local.Origin.z);

                    double s = Math.Abs(poss[f].Length - restLen);
                    stretches.Add(s);
                    if (s > stretchB) stretchB = s;

                    var (_, fkPos) = fkWorldD[f][b];
                    var pw = bake.BoneWorld[f][b].Origin;
                    double mv = (new Vec3d(pw.x, pw.y, pw.z) - fkPos).Length;
                    moves.Add(mv);
                    if (mv > moveB) moveB = mv;
                }
                result.LocalRotations[b] = rots;
                result.LocalTranslations[b] = poss;
                if (moveB > result.MoveMax) { result.MoveMax = moveB; result.MoveMaxBone = model.Bones[b].Name; }
                if (stretchB > result.StretchMax) { result.StretchMax = stretchB; result.StretchMaxBone = model.Bones[b].Name; }
            }

            (result.MoveMedian, result.MoveP90) = Pct(moves);
            (result.StretchMedian, result.StretchP90) = Pct(stretches);
            return result;
        }

        // 最大値は一瞬のスパイクを拾うので中央値/p90 も出す
        // (この種の指標は最大値だけ見ると判断を誤る、というのが過去の教訓)。
        private static (double med, double p90) Pct(List<double> v)
        {
            if (v.Count == 0) return (0, 0);
            v.Sort();
            return (v[v.Count / 2], v[Math.Min(v.Count - 1, (int)(v.Count * 0.9))]);
        }

        private static RigidTransform ToRigid(Quatd q, Vec3d p) =>
            new RigidTransform(new Quat((float)q.X, (float)q.Y, (float)q.Z, (float)q.W),
                               new Vec3((float)p.X, (float)p.Y, (float)p.Z));

        private static int[] TopologicalOrder(PmxModel model)
        {
            int n = model.Bones.Count;
            var depth = new int[n];
            for (int b = 0; b < n; b++)
            {
                int d = 0, p = model.Bones[b].Parent;
                while (p >= 0 && p < n)
                {
                    d++; p = model.Bones[p].Parent;
                    if (d > n) throw new InvalidOperationException("ボーン階層が循環しています。");
                }
                depth[b] = d;
            }
            var idx = new int[n];
            for (int b = 0; b < n; b++) idx[b] = b;
            Array.Sort(idx, (x, y) => depth[x] != depth[y] ? depth[x] - depth[y] : x - y);
            return idx;
        }
    }
}
