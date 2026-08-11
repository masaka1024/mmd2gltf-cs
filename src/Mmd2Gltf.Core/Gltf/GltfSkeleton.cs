// ===========================================================================
// glTF の skins[0] からボーン階層を取り出す。
// ボーン index (PMX と同じ並び) -> ノード index / 親ボーン index / rest ローカル TRS。
// GlbPhysicsReader が extras.mmd を読むときと同じ前提に揃えてある
// (バインド姿勢は回転恒等・並進のみ)。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using BulletPhysics;

namespace Mmd2Gltf
{
    public sealed class GltfSkeleton
    {
        public int[] NodeOfBone;        // ボーン index -> ノード index
        public int[] BoneOfNode;        // ノード index -> ボーン index (-1 = ボーンでない)
        public int[] ParentBone;        // ボーン index -> 親ボーン index (-1 = ルート)
        public Vec3[] RestPos;          // glTF ローカル並進 (rest)
        public Quat[] RestRot;          // glTF ローカル回転 (rest)
        public string[] Names;

        public int BoneCount => NodeOfBone.Length;

        public static GltfSkeleton Load(Glb glb)
        {
            var nodes = glb.RequireArr("nodes");
            var skins = glb.Arr("skins");
            if (skins == null || skins.Count == 0)
                throw new InvalidDataException("glTF に skins がありません (スキン付きモデルではない?)。");
            var joints = skins[0]["joints"] as JsonArray
                         ?? throw new InvalidDataException("skins[0].joints がありません。");

            int nb = joints.Count;
            var sk = new GltfSkeleton
            {
                NodeOfBone = new int[nb],
                BoneOfNode = new int[nodes.Count],
                ParentBone = new int[nb],
                RestPos = new Vec3[nb],
                RestRot = new Quat[nb],
                Names = new string[nb],
            };
            for (int i = 0; i < nodes.Count; i++) sk.BoneOfNode[i] = -1;
            for (int b = 0; b < nb; b++)
            {
                int ni = joints[b].GetValue<int>();
                if (ni < 0 || ni >= nodes.Count)
                    throw new InvalidDataException($"ボーン {b} のノード index {ni} が範囲外です。");
                sk.NodeOfBone[b] = ni;
                sk.BoneOfNode[ni] = b;
                sk.ParentBone[b] = -1;
            }

            for (int b = 0; b < nb; b++)
            {
                var node = nodes[sk.NodeOfBone[b]] as JsonObject;
                sk.Names[b] = node["name"]?.GetValue<string>() ?? ("bone_" + b);
                sk.RestPos[b] = ReadVec3(node["translation"]);
                sk.RestRot[b] = ReadQuat(node["rotation"]);
                if (node["scale"] is JsonArray s &&
                    (Math.Abs(s[0].GetValue<float>() - 1f) > 1e-6f ||
                     Math.Abs(s[1].GetValue<float>() - 1f) > 1e-6f ||
                     Math.Abs(s[2].GetValue<float>() - 1f) > 1e-6f))
                    throw new NotSupportedException(
                        $"ボーン '{sk.Names[b]}' に非単位スケールがあります。物理ベイクはスケール無しを前提としています。");

                if (node["children"] is JsonArray children)
                    foreach (var c in children)
                    {
                        int cn = c.GetValue<int>();
                        int cb = (cn >= 0 && cn < sk.BoneOfNode.Length) ? sk.BoneOfNode[cn] : -1;
                        if (cb >= 0) sk.ParentBone[cb] = b;
                    }
            }
            return sk;
        }

        private static Vec3 ReadVec3(JsonNode n) =>
            n is JsonArray a && a.Count >= 3
                ? new Vec3(a[0].GetValue<float>(), a[1].GetValue<float>(), a[2].GetValue<float>())
                : Vec3.Zero;

        private static Quat ReadQuat(JsonNode n) =>
            n is JsonArray a && a.Count >= 4
                ? new Quat(a[0].GetValue<float>(), a[1].GetValue<float>(),
                           a[2].GetValue<float>(), a[3].GetValue<float>())
                : Quat.Identity;

        /// <summary>親→子の順に並べたボーン index (FK 合成順)。</summary>
        public int[] TopologicalOrder()
        {
            int nb = BoneCount;
            var order = new int[nb];
            var depth = new int[nb];
            for (int b = 0; b < nb; b++)
            {
                int d = 0, p = ParentBone[b];
                while (p >= 0) { d++; p = ParentBone[p]; if (d > nb) throw new InvalidDataException("ボーン階層が循環しています。"); }
                depth[b] = d;
            }
            var idx = new List<int>(nb);
            for (int b = 0; b < nb; b++) idx.Add(b);
            idx.Sort((x, y) => depth[x] != depth[y] ? depth[x] - depth[y] : x - y);
            idx.CopyTo(order);
            return order;
        }
    }
}
