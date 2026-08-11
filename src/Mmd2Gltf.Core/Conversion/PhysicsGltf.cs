// ===========================================================================
// PMX 剛体 / Joint の raw 値 (extras.mmd) を glTF 系のボーンローカルへ変換し、
// extras.mmd.physicsGltf として併載する。Python 版 physics.py の移植。
//
// 座標規約そのものは MmdConvert / MmdMath.FromEulerYxz に集約してある。
// ここが持っているのは「どの空間へ移すか」の手順 (行列合成) だけ。
//
// 角度制限は Z 反転の鏡映なので X/Y 軸は符号反転 + min/max 入れ替え、Z 軸は不変。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Mmd2Gltf.Animation;
using Mmd2Gltf.Pmx;

namespace Mmd2Gltf.Conversion
{
    public static class PhysicsGltf
    {
        public const string MmdEulerOrder = "YXZ";

        // ---- 4x4 行列 (行優先, double) ---------------------------------------
        public static double[,] MatIdentity()
        {
            var m = new double[4, 4];
            for (int i = 0; i < 4; i++) m[i, i] = 1.0;
            return m;
        }

        public static double[,] MatMul(double[,] a, double[,] b)
        {
            var r = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    double s = 0;
                    for (int k = 0; k < 4; k++) s += a[i, k] * b[k, j];
                    r[i, j] = s;
                }
            return r;
        }

        /// <summary>一般 4x4 逆行列 (ガウス・ジョルダン)。Python 版と同じ手順。</summary>
        public static double[,] MatInv(double[,] m)
        {
            const int n = 4;
            var a = new double[n, 2 * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) a[i, j] = m[i, j];
                a[i, n + i] = 1.0;
            }
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r2 = col; r2 < n; r2++)
                    if (Math.Abs(a[r2, col]) > Math.Abs(a[piv, col])) piv = r2;
                if (Math.Abs(a[piv, col]) < 1e-15)
                    throw new InvalidOperationException("特異行列のため逆行列が求まりません。");
                if (piv != col)
                    for (int j = 0; j < 2 * n; j++)
                        (a[col, j], a[piv, j]) = (a[piv, j], a[col, j]);
                double pv = a[col, col];
                for (int j = 0; j < 2 * n; j++) a[col, j] /= pv;
                for (int r2 = 0; r2 < n; r2++)
                {
                    if (r2 == col) continue;
                    double f = a[r2, col];
                    if (f == 0) continue;
                    for (int j = 0; j < 2 * n; j++) a[r2, j] -= f * a[col, j];
                }
            }
            var inv = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) inv[i, j] = a[i, n + j];
            return inv;
        }

        public static double[,] TrsToMat(Vec3d t, Quatd q, Vec3d s)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            var rot = new[,]
            {
                { 1 - 2 * (y * y + z * z),     2 * (x * y - z * w),     2 * (x * z + y * w) },
                {     2 * (x * y + z * w), 1 - 2 * (x * x + z * z),     2 * (y * z - x * w) },
                {     2 * (x * z - y * w),     2 * (y * z + x * w), 1 - 2 * (x * x + y * y) },
            };
            var m = MatIdentity();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) m[i, j] = rot[i, j] * s[j];
                m[i, 3] = t[i];
            }
            return m;
        }

        /// <summary>4x4 → (並進, クォータニオン)。回転成分にスケールが無い前提。</summary>
        public static void MatToTrs(double[,] m, out Vec3d t, out Quatd q)
        {
            t = new Vec3d(m[0, 3], m[1, 3], m[2, 3]);
            double m00 = m[0, 0], m11 = m[1, 1], m22 = m[2, 2];
            double tr = m00 + m11 + m22;
            double x, y, z, w, s;
            if (tr > 0)
            {
                s = Math.Sqrt(tr + 1.0) * 2;
                w = 0.25 * s;
                x = (m[2, 1] - m[1, 2]) / s;
                y = (m[0, 2] - m[2, 0]) / s;
                z = (m[1, 0] - m[0, 1]) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2;
                w = (m[2, 1] - m[1, 2]) / s;
                x = 0.25 * s;
                y = (m[0, 1] + m[1, 0]) / s;
                z = (m[0, 2] + m[2, 0]) / s;
            }
            else if (m11 > m22)
            {
                s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2;
                w = (m[0, 2] - m[2, 0]) / s;
                x = (m[0, 1] + m[1, 0]) / s;
                y = 0.25 * s;
                z = (m[1, 2] + m[2, 1]) / s;
            }
            else
            {
                s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2;
                w = (m[1, 0] - m[0, 1]) / s;
                x = (m[0, 2] + m[2, 0]) / s;
                y = (m[1, 2] + m[2, 1]) / s;
                z = 0.25 * s;
            }
            q = new Quatd(x, y, z, w).Normalized;
        }

        // ---- 組み上がった glTF JSON から node 別の rest 世界行列 -----------------
        public static double[][,] ComputeBoneWorldMatrices(JsonObject gltf)
        {
            var nodes = (JsonArray)gltf["nodes"];
            int n = nodes.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = -1;
            for (int i = 0; i < n; i++)
                if (nodes[i]["children"] is JsonArray ch)
                    foreach (var c in ch) parent[c.GetValue<int>()] = i;

            double[,] Local(JsonNode nd)
            {
                if (nd["matrix"] is JsonArray mm)   // 列優先で来るので転置する
                {
                    var m = new double[4, 4];
                    for (int i = 0; i < 4; i++)
                        for (int j = 0; j < 4; j++) m[i, j] = mm[j * 4 + i].GetValue<double>();
                    return m;
                }
                var t = ReadVec(nd["translation"], 0, 0, 0);
                var q = ReadQuat(nd["rotation"]);
                var s = ReadVec(nd["scale"], 1, 1, 1);
                return TrsToMat(t, q, s);
            }

            var world = new double[n][,];
            double[,] Wm(int i)
            {
                if (world[i] != null) return world[i];
                var m = Local(nodes[i]);
                world[i] = parent[i] == -1 ? m : MatMul(Wm(parent[i]), m);
                return world[i];
            }
            for (int i = 0; i < n; i++) Wm(i);
            return world;
        }

        private static Vec3d ReadVec(JsonNode n, double dx, double dy, double dz) =>
            n is JsonArray a && a.Count >= 3
                ? new Vec3d(a[0].GetValue<double>(), a[1].GetValue<double>(), a[2].GetValue<double>())
                : new Vec3d(dx, dy, dz);

        private static Quatd ReadQuat(JsonNode n) =>
            n is JsonArray a && a.Count >= 4
                ? new Quatd(a[0].GetValue<double>(), a[1].GetValue<double>(),
                            a[2].GetValue<double>(), a[3].GetValue<double>())
                : Quatd.Identity;

        // ---- physicsGltf ブロックの生成 ---------------------------------------
        /// <summary>extras.mmd (raw) と node 別世界行列から physicsGltf を作る。</summary>
        public static JsonObject Build(double unitScale,
                                       List<PmxRigidBody> rbs, List<PmxJoint> jts,
                                       double[][,] boneWorldMatrices)
        {
            double s = unitScale;

            double[,] BoneMat(int bi) =>
                bi >= 0 && bi < boneWorldMatrices.Length ? boneWorldMatrices[bi] : null;

            Vec3d PosToGltf(float[] p) =>
                MmdConvert.MmdToGltfPos(new Vec3d(p[0], p[1], p[2]), s);

            Quatd RotToGltf(float[] e) =>
                MmdConvert.MmdToGltfRot(MmdMath.FromEulerYxz(e[0], e[1], e[2]));

            var outRbs = new JsonArray();
            foreach (var rb in rbs)
            {
                var rigidWorld = TrsToMat(PosToGltf(rb.Pos), RotToGltf(rb.Rot), new Vec3d(1, 1, 1));
                var bm = BoneMat(rb.Bone);
                double[,] local;
                string space;
                if (bm != null) { local = MatMul(MatInv(bm), rigidWorld); space = "boneLocal"; }
                else { local = rigidWorld; space = "world"; }
                MatToTrs(local, out var t, out var q);

                outRbs.Add(new JsonObject
                {
                    ["name"] = rb.Name,
                    ["bone"] = rb.Bone,
                    ["space"] = space,
                    ["shape"] = rb.Shape,
                    ["mode"] = rb.Mode,
                    ["group"] = rb.Group,
                    ["noCollisionMask"] = rb.NoCollisionMask,
                    ["size"] = new JsonArray(rb.Size[0] * s, rb.Size[1] * s, rb.Size[2] * s),
                    ["position"] = new JsonArray(t.X, t.Y, t.Z),
                    ["rotation"] = new JsonArray(q.X, q.Y, q.Z, q.W),
                    ["mass"] = (double)rb.Mass,
                    ["linearDamping"] = (double)rb.LinearDamping,
                    ["angularDamping"] = (double)rb.AngularDamping,
                    ["restitution"] = (double)rb.Restitution,
                    ["friction"] = (double)rb.Friction,
                });
            }

            var outJts = new JsonArray();
            foreach (var j in jts)
            {
                var jw = TrsToMat(PosToGltf(j.Pos), RotToGltf(j.Rot), new Vec3d(1, 1, 1));
                var rbB = j.RigidB >= 0 && j.RigidB < rbs.Count ? rbs[j.RigidB] : null;
                var bm = rbB != null ? BoneMat(rbB.Bone) : null;
                double[,] local;
                string space;
                int refBone;
                if (bm != null) { local = MatMul(MatInv(bm), jw); space = "boneLocal"; refBone = rbB.Bone; }
                else { local = jw; space = "world"; refBone = -1; }
                MatToTrs(local, out var t, out var q);

                float[] rmin = j.RotMin, rmax = j.RotMax, pmin = j.PosMin, pmax = j.PosMax;
                outJts.Add(new JsonObject
                {
                    ["name"] = j.Name,
                    ["type"] = j.Type,
                    ["rigidA"] = j.RigidA,
                    ["rigidB"] = j.RigidB,
                    ["space"] = space,
                    ["refBone"] = refBone,
                    ["position"] = new JsonArray(t.X, t.Y, t.Z),
                    ["rotation"] = new JsonArray(q.X, q.Y, q.Z, q.W),
                    // Z 反転の鏡映: X/Y は符号反転 + min/max 入れ替え、Z は不変
                    ["linearLimitMin"] = new JsonArray(pmin[0] * s, pmin[1] * s, -pmax[2] * s),
                    ["linearLimitMax"] = new JsonArray(pmax[0] * s, pmax[1] * s, -pmin[2] * s),
                    ["angularLimitMin"] = new JsonArray(-(double)rmax[0], -(double)rmax[1], (double)rmin[2]),
                    ["angularLimitMax"] = new JsonArray(-(double)rmin[0], -(double)rmin[1], (double)rmax[2]),
                    ["springPosition"] = new JsonArray(j.SpringPos[0] * s, j.SpringPos[1] * s, j.SpringPos[2] * s),
                    ["springRotation"] = new JsonArray((double)j.SpringRot[0], (double)j.SpringRot[1], (double)j.SpringRot[2]),
                });
            }

            return new JsonObject
            {
                ["space"] = "boneLocal",
                ["unitScale"] = s,
                ["eulerOrderSource"] = MmdEulerOrder,
                ["note"] = "rigidBodies/joints are expressed in the local space of their " +
                           "own bone (rigid) / rigidB's bone (joint). bone==-1 entries " +
                           "fall back to glTF world space (see per-entry 'space'). " +
                           "Angles in radians.",
                ["rigidBodies"] = outRbs,
                ["joints"] = outJts,
            };
        }
    }
}
