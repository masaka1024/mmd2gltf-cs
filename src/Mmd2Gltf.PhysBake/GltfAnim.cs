// ===========================================================================
// glTF animation の読み出しとサンプリング。
// mmd2gltf の出力は全チャンネルが同じ input accessor (時刻列) を共有するが、
// ここでは前提を置かず「全 input の和集合」を時刻グリッドにして各サンプラを
// 補間サンプルする。回転は slerp、並進は線形。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using BulletPhysics;

namespace Mmd2Gltf.PhysBake
{
    public sealed class AnimSampler
    {
        public float[] Times;
        public float[] Values;      // Times.Length * Components
        public int Components;
        public string Interpolation = "LINEAR";

        private int Find(float t)
        {
            // Times[i] <= t < Times[i+1] となる i を返す (端はクランプ)。
            int lo = 0, hi = Times.Length - 1;
            if (t <= Times[0]) return 0;
            if (t >= Times[hi]) return hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Times[mid] <= t) lo = mid; else hi = mid;
            }
            return lo;
        }

        public Vec3 SampleVec3(float t)
        {
            GetSpan(t, out int i0, out int i1, out float u);
            var a = new Vec3(Values[i0 * 3], Values[i0 * 3 + 1], Values[i0 * 3 + 2]);
            if (i0 == i1 || u <= 0f) return a;
            var b = new Vec3(Values[i1 * 3], Values[i1 * 3 + 1], Values[i1 * 3 + 2]);
            return a + (b - a) * u;
        }

        public Quat SampleQuat(float t)
        {
            GetSpan(t, out int i0, out int i1, out float u);
            var a = new Quat(Values[i0 * 4], Values[i0 * 4 + 1], Values[i0 * 4 + 2], Values[i0 * 4 + 3]);
            if (i0 == i1 || u <= 0f) return a.Normalized;
            var b = new Quat(Values[i1 * 4], Values[i1 * 4 + 1], Values[i1 * 4 + 2], Values[i1 * 4 + 3]);
            return Quat.Slerp(a.Normalized, b.Normalized, u);
        }

        private void GetSpan(float t, out int i0, out int i1, out float u)
        {
            i0 = Find(t);
            i1 = Math.Min(i0 + 1, Times.Length - 1);
            if (Interpolation == "STEP" || i0 == i1) { u = 0f; return; }
            float span = Times[i1] - Times[i0];
            u = span > 0f ? Math.Min(1f, Math.Max(0f, (t - Times[i0]) / span)) : 0f;
        }
    }

    /// <summary>1つの animation を「ノード -> path -> サンプラ」に整理したもの。</summary>
    public sealed class GltfAnimation
    {
        public int Index;
        public string Name;
        public readonly Dictionary<int, AnimSampler> Rotation = new();
        public readonly Dictionary<int, AnimSampler> Translation = new();
        public readonly Dictionary<int, AnimSampler> Scale = new();
        public float[] Times;       // 全 input の和集合 (昇順)

        public static GltfAnimation Load(Glb glb, int animIndex)
        {
            var anims = glb.Arr("animations");
            if (anims == null || anims.Count == 0)
                throw new InvalidOperationException(
                    "この GLB にはアニメーションがありません。--vmd 付きで変換した GLB を渡してください。");
            if (animIndex < 0 || animIndex >= anims.Count)
                throw new ArgumentOutOfRangeException(nameof(animIndex),
                    $"animation {animIndex} は範囲外です (0..{anims.Count - 1})。");

            var a = anims[animIndex] as JsonObject;
            var result = new GltfAnimation
            {
                Index = animIndex,
                Name = a["name"]?.GetValue<string>(),
            };

            var samplersJson = a["samplers"] as JsonArray ?? new JsonArray();
            var channels = a["channels"] as JsonArray ?? new JsonArray();
            var cache = new Dictionary<int, AnimSampler>();
            var timeSet = new SortedSet<float>();

            AnimSampler GetSampler(int si)
            {
                if (cache.TryGetValue(si, out var s)) return s;
                var sj = samplersJson[si] as JsonObject;
                string interp = sj["interpolation"]?.GetValue<string>() ?? "LINEAR";
                if (interp == "CUBICSPLINE")
                    throw new NotSupportedException("CUBICSPLINE 補間は未対応です。");
                var times = glb.ReadFloatAccessor(sj["input"].GetValue<int>(), out int tc);
                if (tc != 1) throw new InvalidOperationException("サンプラの input が SCALAR ではありません。");
                var vals = glb.ReadFloatAccessor(sj["output"].GetValue<int>(), out int vc);
                s = new AnimSampler { Times = times, Values = vals, Components = vc, Interpolation = interp };
                cache[si] = s;
                foreach (var t in times) timeSet.Add(t);
                return s;
            }

            foreach (var ch in channels)
            {
                var target = ch["target"] as JsonObject;
                if (target == null) continue;
                var nodeNode = target["node"];
                if (nodeNode == null) continue;      // ノード無し = 無効チャンネル
                int node = nodeNode.GetValue<int>();
                string path = target["path"]?.GetValue<string>();
                var sampler = GetSampler(ch["sampler"].GetValue<int>());
                switch (path)
                {
                    case "rotation": result.Rotation[node] = sampler; break;
                    case "translation": result.Translation[node] = sampler; break;
                    case "scale": result.Scale[node] = sampler; break;
                }
            }

            result.Times = new float[timeSet.Count];
            timeSet.CopyTo(result.Times);
            if (result.Times.Length == 0)
                throw new InvalidOperationException("アニメーションにキーフレームがありません。");
            return result;
        }

        /// <summary>指定ノードの、指定時刻でのローカル TRS (glTF 空間)。
        /// アニメが無い成分はノードの rest 値を返す。</summary>
        public void SampleLocal(int node, float t, Vec3 restPos, Quat restRot,
                                out Vec3 pos, out Quat rot)
        {
            pos = Translation.TryGetValue(node, out var ts) ? ts.SampleVec3(t) : restPos;
            rot = Rotation.TryGetValue(node, out var rs) ? rs.SampleQuat(t) : restRot;
        }
    }
}
