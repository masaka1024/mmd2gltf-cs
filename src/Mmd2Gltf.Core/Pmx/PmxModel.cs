// ===========================================================================
// PMX 2.0 / 2.1 のデータ構造。
// Python 版 pmx.py が返す辞書と 1:1 に対応させてある (JSON プロパティ名も同じ)。
// こうしておくと pmxdump の出力をそのまま突き合わせられる。
//
// 値はすべて MMD のネイティブ左手座標系のまま。glTF への変換は後段で行う。
// ===========================================================================
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mmd2Gltf.Pmx
{
    public sealed class PmxSdef
    {
        [JsonPropertyName("c")] public float[] C;
        [JsonPropertyName("r0")] public float[] R0;
        [JsonPropertyName("r1")] public float[] R1;
    }

    public sealed class PmxVertex
    {
        [JsonPropertyName("pos")] public float[] Pos;
        [JsonPropertyName("normal")] public float[] Normal;
        [JsonPropertyName("uv")] public float[] Uv;
        [JsonPropertyName("add_uv")] public float[][] AddUv;
        [JsonPropertyName("weight_type")] public int WeightType;
        [JsonPropertyName("bones")] public int[] Bones;
        [JsonPropertyName("weights")] public float[] Weights;
        [JsonPropertyName("sdef")] public PmxSdef Sdef;
        [JsonPropertyName("edge")] public float Edge;
    }

    public sealed class PmxMaterial
    {
        [JsonPropertyName("name")] public string Name;
        [JsonPropertyName("name_en")] public string NameEn;
        [JsonPropertyName("diffuse")] public float[] Diffuse;
        [JsonPropertyName("specular")] public float[] Specular;
        [JsonPropertyName("specular_power")] public float SpecularPower;
        [JsonPropertyName("ambient")] public float[] Ambient;
        [JsonPropertyName("flags")] public int Flags;
        [JsonPropertyName("edge_color")] public float[] EdgeColor;
        [JsonPropertyName("edge_size")] public float EdgeSize;
        [JsonPropertyName("texture")] public int Texture;
        [JsonPropertyName("sphere_texture")] public int SphereTexture;
        [JsonPropertyName("sphere_mode")] public int SphereMode;
        /// <summary>共有トゥーン番号。共有でなければ -1。</summary>
        [JsonPropertyName("toon_shared")] public int ToonShared;
        /// <summary>個別トゥーンのテクスチャ index。共有なら -1。</summary>
        [JsonPropertyName("toon_texture")] public int ToonTexture;
        [JsonPropertyName("memo")] public string Memo;
        [JsonPropertyName("index_count")] public int IndexCount;
    }

    public sealed class PmxIkLink
    {
        [JsonPropertyName("bone")] public int Bone;
        [JsonPropertyName("min")] public float[] Min;   // 角度制限なしなら null
        [JsonPropertyName("max")] public float[] Max;
    }

    public sealed class PmxIk
    {
        [JsonPropertyName("target")] public int Target;
        [JsonPropertyName("loop")] public int Loop;
        [JsonPropertyName("limit_angle")] public float LimitAngle;
        [JsonPropertyName("links")] public List<PmxIkLink> Links = new();
    }

    public sealed class PmxBone
    {
        [JsonPropertyName("name")] public string Name;
        [JsonPropertyName("name_en")] public string NameEn;
        [JsonPropertyName("pos")] public float[] Pos;
        [JsonPropertyName("parent")] public int Parent;
        [JsonPropertyName("layer")] public int Layer;
        [JsonPropertyName("flags")] public int Flags;
        [JsonPropertyName("tail_bone")] public int? TailBone;      // flags &amp; 0x0001
        [JsonPropertyName("tail_offset")] public float[] TailOffset;
        [JsonPropertyName("inherit_parent")] public int? InheritParent;
        [JsonPropertyName("inherit_ratio")] public float? InheritRatio;
        [JsonPropertyName("fixed_axis")] public float[] FixedAxis;
        [JsonPropertyName("local_x")] public float[] LocalX;
        [JsonPropertyName("local_z")] public float[] LocalZ;
        [JsonPropertyName("external_key")] public int? ExternalKey;
        [JsonPropertyName("ik")] public PmxIk Ik;
    }

    /// <summary>モーフオフセット。型ごとに使うフィールドが違う (Python 版は型別の
    /// 辞書だったので、使わないフィールドは null にして JSON から落とす)。</summary>
    public sealed class PmxMorphOffset
    {
        // group / flip
        [JsonPropertyName("morph")] public int? Morph;
        [JsonPropertyName("weight")] public float? Weight;
        // vertex / UV
        [JsonPropertyName("vertex")] public int? Vertex;
        [JsonPropertyName("offset")] public float[] Offset;
        // bone
        [JsonPropertyName("bone")] public int? Bone;
        [JsonPropertyName("translation")] public float[] Translation;
        [JsonPropertyName("rotation")] public float[] Rotation;
        // material
        [JsonPropertyName("material")] public int? Material;
        [JsonPropertyName("op")] public int? Op;
        [JsonPropertyName("diffuse")] public float[] Diffuse;
        [JsonPropertyName("specular")] public float[] Specular;
        [JsonPropertyName("specular_power")] public float? SpecularPower;
        [JsonPropertyName("ambient")] public float[] Ambient;
        [JsonPropertyName("edge_color")] public float[] EdgeColor;
        [JsonPropertyName("edge_size")] public float? EdgeSize;
        [JsonPropertyName("texture_tint")] public float[] TextureTint;
        [JsonPropertyName("sphere_tint")] public float[] SphereTint;
        [JsonPropertyName("toon_tint")] public float[] ToonTint;
        // impulse (2.1)
        [JsonPropertyName("rigid")] public int? Rigid;
        [JsonPropertyName("local")] public int? Local;
        [JsonPropertyName("velocity")] public float[] Velocity;
        [JsonPropertyName("torque")] public float[] Torque;
    }

    public sealed class PmxMorph
    {
        [JsonPropertyName("name")] public string Name;
        [JsonPropertyName("name_en")] public string NameEn;
        [JsonPropertyName("panel")] public int Panel;
        /// <summary>0:グループ 1:頂点 2:ボーン 3-7:UV 8:材質 9:フリップ 10:インパルス</summary>
        [JsonPropertyName("type")] public int Type;
        [JsonPropertyName("offsets")] public List<PmxMorphOffset> Offsets = new();
    }

    public sealed class PmxDisplayItem
    {
        [JsonPropertyName("type")] public string Type;   // "bone" | "morph"
        [JsonPropertyName("index")] public int Index;
    }

    public sealed class PmxDisplayFrame
    {
        [JsonPropertyName("name")] public string Name;
        [JsonPropertyName("name_en")] public string NameEn;
        [JsonPropertyName("special")] public int Special;
        [JsonPropertyName("items")] public List<PmxDisplayItem> Items = new();
    }

    public sealed class PmxRigidBody
    {
        [JsonPropertyName("name")] public string Name;
        [JsonPropertyName("name_en")] public string NameEn;
        [JsonPropertyName("bone")] public int Bone;
        [JsonPropertyName("group")] public int Group;
        /// <summary>名前に反して実体は collide-with マスク (bit が 1 の相手と衝突する)。
        /// PMX 仕様の呼称に合わせてキー名は据え置き。</summary>
        [JsonPropertyName("no_collision_mask")] public int NoCollisionMask;
        [JsonPropertyName("shape")] public int Shape;      // 0:球 1:箱 2:カプセル
        [JsonPropertyName("size")] public float[] Size;
        [JsonPropertyName("pos")] public float[] Pos;
        [JsonPropertyName("rot")] public float[] Rot;      // ラジアン (YXZ 順)
        [JsonPropertyName("mass")] public float Mass;
        [JsonPropertyName("linear_damping")] public float LinearDamping;
        [JsonPropertyName("angular_damping")] public float AngularDamping;
        [JsonPropertyName("restitution")] public float Restitution;
        [JsonPropertyName("friction")] public float Friction;
        [JsonPropertyName("mode")] public int Mode;        // 0:追従 1:物理 2:物理+ボーン位置合わせ
    }

    public sealed class PmxJoint
    {
        [JsonPropertyName("name")] public string Name;
        [JsonPropertyName("name_en")] public string NameEn;
        [JsonPropertyName("type")] public int Type;
        [JsonPropertyName("rigid_a")] public int RigidA;
        [JsonPropertyName("rigid_b")] public int RigidB;
        [JsonPropertyName("pos")] public float[] Pos;
        [JsonPropertyName("rot")] public float[] Rot;
        [JsonPropertyName("pos_min")] public float[] PosMin;
        [JsonPropertyName("pos_max")] public float[] PosMax;
        [JsonPropertyName("rot_min")] public float[] RotMin;
        [JsonPropertyName("rot_max")] public float[] RotMax;
        [JsonPropertyName("spring_pos")] public float[] SpringPos;
        [JsonPropertyName("spring_rot")] public float[] SpringRot;
    }

    public sealed class PmxModel
    {
        [JsonPropertyName("version")] public float Version;
        [JsonPropertyName("encoding")] public string Encoding;
        [JsonPropertyName("add_uv_count")] public int AddUvCount;
        [JsonPropertyName("name")] public string Name;
        [JsonPropertyName("name_en")] public string NameEn;
        [JsonPropertyName("comment")] public string Comment;
        [JsonPropertyName("comment_en")] public string CommentEn;
        [JsonPropertyName("vertices")] public List<PmxVertex> Vertices = new();
        [JsonPropertyName("indices")] public int[] Indices;
        [JsonPropertyName("textures")] public List<string> Textures = new();
        [JsonPropertyName("materials")] public List<PmxMaterial> Materials = new();
        [JsonPropertyName("bones")] public List<PmxBone> Bones = new();
        [JsonPropertyName("morphs")] public List<PmxMorph> Morphs = new();
        [JsonPropertyName("display_frames")] public List<PmxDisplayFrame> DisplayFrames = new();
        [JsonPropertyName("rigid_bodies")] public List<PmxRigidBody> RigidBodies = new();
        [JsonPropertyName("joints")] public List<PmxJoint> Joints = new();
        /// <summary>読み残したバイト数 (PMX 2.1 の SoftBody は未対応)。</summary>
        [JsonPropertyName("trailing_bytes")] public int TrailingBytes;
    }
}
