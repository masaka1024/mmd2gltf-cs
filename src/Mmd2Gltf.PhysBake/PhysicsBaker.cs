// ===========================================================================
// 自作 Bullet 互換エンジン (external/mmd-physics) を使って、
// FK で求めたボーンworld姿勢列から物理ボーンの姿勢列を作る。
//
// 駆動と書き戻しの式は Unity 実装 (MmdPhysicsBehaviour) と同一のものを使う:
//   駆動   : PmxPhysicsBuilder.ApplyKinematicTargets  (= boneWorld * BodyOffsetFromBone)
//   書き戻し: boneWorld = body.WorldTransform * BodyOffsetFromBone.Inverse()
//   mode2  : ComputeAlignedBonePoses (位置=親チェーン再構成 / 回転=物理)
// ★手書きしないこと。過去に駆動式を手書きしたハーネスで全計測が汚染された事故がある。
// ===========================================================================
using System;
using System.Collections.Generic;
using BulletPhysics;
using BulletPhysics.Pmx;

namespace Mmd2Gltf.PhysBake
{
    public sealed class BakeOptions
    {
        /// <summary>内部固定刻み [Hz]。0 = エンジン既定 (30Hz)。</summary>
        public float SimHz = 0f;
        /// <summary>1 固定ステップあたりのサブステップ数。0 = エンジン既定 (2)。</summary>
        public int SubSteps = 0;
        /// <summary>ソルバ反復数。0 = エンジン既定 (10)。</summary>
        public int SolverIterations = 0;
        /// <summary>重力の大きさ (MMD スケール, 正の値)。0 = エンジン既定 (98)。</summary>
        public float Gravity = 0f;
        /// <summary>フレーム0の姿勢で空回しする秒数 (初期過渡を捨てる)。</summary>
        public float WarmupSeconds = 2.0f;
        /// <summary>PMX mode2 (物理演算+ボーン位置合わせ) を有効にする。</summary>
        public bool BoneMergeMode = true;
        /// <summary>全物理ボーンに位置合わせ (PMXエディタの補正層相当) を適用する。
        /// 既定 OFF: 正しい配置では貫入がむしろ増えることが計測で分かっている。</summary>
        public bool AlignBonePositions = false;
    }

    public sealed class BakeResult
    {
        /// <summary>物理で動かしたボーンの index (昇順)。</summary>
        public int[] PhysicsBones;
        /// <summary>[frame][bone] の raw PMX world 姿勢。物理ボーン以外は未設定 (null)。</summary>
        public RigidTransform[][] BoneWorld;
        public int StepCount;
        public double Seconds;
        public string NanBone;      // 発散を検出したボーン名 (無ければ null)
    }

    public static class PhysicsBaker
    {
        public static BakeResult Run(PmxPhysicsModel model,
                                     RigidTransform?[][] fkBoneWorld,
                                     float[] times,
                                     BakeOptions opt,
                                     Action<string> log)
        {
            int frames = times.Length;
            int nb = model.BoneNames.Count;

            var builder = PmxPhysicsBuilder.Build(model);
            var world = builder.World;
            if (opt.SimHz > 0f) world.FixedTimeStep = 1f / opt.SimHz;
            if (opt.SubSteps > 0) world.SubSteps = opt.SubSteps;
            if (opt.SolverIterations > 0) world.SolverIterations = opt.SolverIterations;
            if (opt.Gravity > 0f) world.Gravity = new Vec3(0f, -opt.Gravity, 0f);

            // 物理ボーン = BoneFollow でない剛体が紐づくボーン。
            var physicsBoneSet = new SortedSet<int>();
            foreach (var link in builder.BoneLinks)
                if (link.Mode != PhysicsMode.BoneFollow && link.BoneIndex >= 0 && link.BoneIndex < nb)
                    physicsBoneSet.Add(link.BoneIndex);

            var result = new BakeResult
            {
                PhysicsBones = new int[physicsBoneSet.Count],
                BoneWorld = new RigidTransform[frames][],
            };
            physicsBoneSet.CopyTo(result.PhysicsBones);

            log?.Invoke($"  剛体 {model.RigidBodies.Count} / Joint {model.Joints.Count} / 物理ボーン {result.PhysicsBones.Length}");
            log?.Invoke($"  刻み 1/{1f / world.FixedTimeStep:F0}s x {world.SubSteps}サブ, ソルバ反復 {world.SolverIterations}, 重力 {-world.Gravity.y:F1}");

            bool needAligned = opt.AlignBonePositions ||
                               (opt.BoneMergeMode && builder.HasBoneMergeBodies);
            if (opt.BoneMergeMode && builder.HasBoneMergeBodies)
                log?.Invoke("  PMX mode2 (物理演算+ボーン位置合わせ) の剛体があります: 位置合わせを適用します");

            Func<int, RigidTransform?> DrivenAt(int frame) =>
                bi => (bi >= 0 && bi < nb) ? fkBoneWorld[frame][bi] : null;

            // 物理開始前に FK-rest で全剛体をボーン姿勢へ整合させる (MMD の物理リセット相当)。
            builder.ApplyKinematicTargets(DrivenAt(0));
            builder.ResetBodiesToBonePoseFk(DrivenAt(0));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int steps = 0;

            // ウォームアップ: フレーム0姿勢で空回しして初期過渡を捨てる。
            int warmupSteps = (int)Math.Round(opt.WarmupSeconds / world.FixedTimeStep);
            for (int i = 0; i < warmupSteps; i++) { world.StepSimulation(world.FixedTimeStep); steps++; }
            if (warmupSteps > 0)
                log?.Invoke($"  ウォームアップ {opt.WarmupSeconds:F2}s ({warmupSteps} ステップ)");

            for (int f = 0; f < frames; f++)
            {
                if (f > 0)
                {
                    builder.ApplyKinematicTargets(DrivenAt(f));
                    float dt = times[f] - times[f - 1];
                    if (dt <= 0f) dt = world.FixedTimeStep;
                    world.StepSimulation(dt);
                    steps += world.LastStepsRun;
                }

                RigidTransform?[] aligned = needAligned
                    ? builder.ComputeAlignedBonePoses(DrivenAt(f))
                    : null;

                var row = new RigidTransform[nb];
                result.BoneWorld[f] = row;
                foreach (var link in builder.BoneLinks)
                {
                    if (link.Mode == PhysicsMode.BoneFollow) continue;
                    int bi = link.BoneIndex;
                    if (bi < 0 || bi >= nb) continue;

                    bool useAligned = aligned != null && aligned[bi].HasValue &&
                                      (opt.AlignBonePositions ||
                                       (opt.BoneMergeMode && link.Mode == PhysicsMode.DynamicBoneMerge));
                    var boneWorld = useAligned
                        ? aligned[bi].Value
                        : link.Body.WorldTransform * link.BodyOffsetFromBone.Inverse();

                    // NaN ガード: 発散したらそのフレーム以降は直前の姿勢を保持する。
                    if (!IsFinite(boneWorld))
                    {
                        result.NanBone ??= (bi < model.BoneNames.Count ? model.BoneNames[bi] : "#" + bi);
                        boneWorld = f > 0 ? result.BoneWorld[f - 1][bi] : RigidTransform.Identity;
                    }
                    row[bi] = boneWorld;
                }

                if (log != null && frames >= 400 && f % 200 == 0)
                    log($"  物理ベイク {f}/{frames}");
            }

            sw.Stop();
            result.StepCount = steps;
            result.Seconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        private static bool IsFinite(RigidTransform t)
        {
            var v = t.Origin; var q = t.Rotation;
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z) ||
                     float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
                     float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
        }
    }
}
