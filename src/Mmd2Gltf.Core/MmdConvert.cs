// ===========================================================================
// MMD(PMX raw) <-> glTF の座標規約。★この規約はここ1箇所にだけ書くこと。
//
// mmd2gltf (Python) の convert.py と同一:
//     cpos(v, s) = (x, y, -z) * s        位置
//     cquat(q)   = (-x, -y, z, w)        回転
// どちらも自己逆写像 (involution) なので逆変換は同じ式 (スケールだけ逆数)。
//
// この写像 Φ は「Z反転による鏡映での共役 + 一様スケール」なので相似変換群の
// 準同型になっている。つまり raw で W = P * L なら glTF でも Φ(W) = Φ(P) * Φ(L)。
// よって階層合成は raw / glTF どちらの空間で行っても等価。
//
// ★過去の事故: この規約を2箇所に書いたことで Z鏡像バグ (Unity 側ブリッジの
//   3回目 ReverseZ) と オイラー順序 ZYX/YXZ バグ が生まれた。増やさないこと。
// ===========================================================================
using BulletPhysics;

namespace Mmd2Gltf
{
    public static class MmdConvert
    {
        /// <summary>glTF ローカル/ワールド位置 → PMX raw 位置。</summary>
        public static Vec3 GltfToMmdPos(Vec3 g, float unitScale) =>
            new Vec3(g.x / unitScale, g.y / unitScale, -g.z / unitScale);

        /// <summary>PMX raw 位置 → glTF 位置。</summary>
        public static Vec3 MmdToGltfPos(Vec3 m, float unitScale) =>
            new Vec3(m.x * unitScale, m.y * unitScale, -m.z * unitScale);

        /// <summary>glTF 回転 → PMX raw 回転 (自己逆写像)。</summary>
        public static Quat GltfToMmdRot(Quat g) => new Quat(-g.x, -g.y, g.z, g.w);

        /// <summary>PMX raw 回転 → glTF 回転 (自己逆写像)。</summary>
        public static Quat MmdToGltfRot(Quat m) => new Quat(-m.x, -m.y, m.z, m.w);

        public static RigidTransform GltfToMmd(Vec3 pos, Quat rot, float unitScale) =>
            new RigidTransform(GltfToMmdRot(rot), GltfToMmdPos(pos, unitScale));

        public static void MmdToGltf(RigidTransform t, float unitScale, out Vec3 pos, out Quat rot)
        {
            pos = MmdToGltfPos(t.Origin, unitScale);
            rot = MmdToGltfRot(t.Rotation);
        }
    }
}
