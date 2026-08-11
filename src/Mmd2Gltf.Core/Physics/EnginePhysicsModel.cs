// ===========================================================================
// パース済みの PmxModel から、物理エンジンが使う PmxPhysicsModel を作る。
//
// エンジンには PMX / GLB を自前で読むリーダ (PmxReader / GlbPhysicsReader) が
// あるが、統合パイプラインではファイルを読み直さずここで詰め替える。
// 値はどちらも raw PMX (MMD 左手系・未スケール) なので変換は無い。
// ===========================================================================
using BulletPhysics;
using BulletPhysics.Pmx;
using Mmd2Gltf.Pmx;

namespace Mmd2Gltf.Physics
{
    public static class EnginePhysicsModel
    {
        public static PmxPhysicsModel From(PmxModel model)
        {
            var m = new PmxPhysicsModel
            {
                ModelName = model.Name,
                ModelNameEn = model.NameEn,
                Version = model.Version,
            };

            foreach (var b in model.Bones)
            {
                m.BoneNames.Add(b.Name);
                m.BonePositions.Add(new Vec3(b.Pos[0], b.Pos[1], b.Pos[2]));
                m.BoneParents.Add(b.Parent);
                m.BoneDeformLayers.Add(b.Layer);
            }

            foreach (var r in model.RigidBodies)
            {
                m.RigidBodies.Add(new BulletPhysics.Pmx.PmxRigidBody
                {
                    Name = r.Name,
                    NameEn = r.NameEn,
                    BoneIndex = r.Bone,
                    Group = (byte)r.Group,
                    // 名前に反して実体は collide-with マスク。エンジンも生値のまま持つ。
                    NonCollisionGroup = (ushort)r.NoCollisionMask,
                    ShapeType = (byte)r.Shape,
                    Size = new Vec3(r.Size[0], r.Size[1], r.Size[2]),
                    Position = new Vec3(r.Pos[0], r.Pos[1], r.Pos[2]),
                    Rotation = new Vec3(r.Rot[0], r.Rot[1], r.Rot[2]),
                    Mass = r.Mass,
                    LinearDamping = r.LinearDamping,
                    AngularDamping = r.AngularDamping,
                    Restitution = r.Restitution,
                    Friction = r.Friction,
                    PhysicsMode = (byte)r.Mode,
                });
            }

            foreach (var j in model.Joints)
            {
                m.Joints.Add(new BulletPhysics.Pmx.PmxJoint
                {
                    Name = j.Name,
                    NameEn = j.NameEn,
                    JointType = (byte)j.Type,
                    RigidBodyAIndex = j.RigidA,
                    RigidBodyBIndex = j.RigidB,
                    Position = new Vec3(j.Pos[0], j.Pos[1], j.Pos[2]),
                    Rotation = new Vec3(j.Rot[0], j.Rot[1], j.Rot[2]),
                    LinearLowerLimit = new Vec3(j.PosMin[0], j.PosMin[1], j.PosMin[2]),
                    LinearUpperLimit = new Vec3(j.PosMax[0], j.PosMax[1], j.PosMax[2]),
                    AngularLowerLimit = new Vec3(j.RotMin[0], j.RotMin[1], j.RotMin[2]),
                    AngularUpperLimit = new Vec3(j.RotMax[0], j.RotMax[1], j.RotMax[2]),
                    SpringLinear = new Vec3(j.SpringPos[0], j.SpringPos[1], j.SpringPos[2]),
                    SpringAngular = new Vec3(j.SpringRot[0], j.SpringRot[1], j.SpringRot[2]),
                });
            }
            return m;
        }
    }
}
