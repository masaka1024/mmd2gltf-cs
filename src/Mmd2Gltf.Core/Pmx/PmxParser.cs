// ===========================================================================
// PMX 2.0 / 2.1 パーサ。Python 版 pmx.py の移植。
// エンジン側の BulletPhysics.Pmx.PmxReader とは別物 (あちらは物理データだけを
// 読む軽量リーダ)。こちらは頂点・材質・モーフまで含む変換用の完全パーサ。
//
// ★Python 版との既知の差 (出力には出ない):
//   BDEF2/SDEF の 2本目のウェイト `1.0 - w` を、Python は float64、こちらは
//   float32 で計算する。IA.pmx で 87 箇所が ~3e-8 ずれるが、Python 側の値を
//   float32 へ落とすと 87 件すべてこちらと完全一致する (実測・残差 0)。
//   glTF の WEIGHTS_0 は float32 なので、出力段では差が消える。
//   突合するときは --tol 1e-7 を付けること。
// ===========================================================================
using System;
using System.IO;
using System.Text;

namespace Mmd2Gltf.Pmx
{
    public static class PmxParser
    {
        private sealed class Reader
        {
            private readonly byte[] _d;
            private int _o;
            public Reader(byte[] d) { _d = d; _o = 0; }
            public int Offset => _o;
            public int Length => _d.Length;

            public byte U8() => _d[_o++];
            public sbyte I8() => (sbyte)_d[_o++];
            public ushort U16() { var v = BitConverter.ToUInt16(_d, _o); _o += 2; return v; }
            public short I16() { var v = BitConverter.ToInt16(_d, _o); _o += 2; return v; }
            public int I32() { var v = BitConverter.ToInt32(_d, _o); _o += 4; return v; }
            public float F32() { var v = BitConverter.ToSingle(_d, _o); _o += 4; return v; }

            public float[] Vec(int n)
            {
                var a = new float[n];
                for (int i = 0; i < n; i++) a[i] = F32();
                return a;
            }

            public byte[] Raw(int n)
            {
                var b = new byte[n];
                Array.Copy(_d, _o, b, 0, n);
                _o += n;
                return b;
            }

            public void Skip(int n) => _o += n;
            public byte[] Data => _d;
        }

        public static PmxModel Parse(string path) => Parse(File.ReadAllBytes(path));

        public static PmxModel Parse(byte[] data)
        {
            var r = new Reader(data);
            var magic = r.Raw(4);
            if (!(magic[0] == 'P' && magic[1] == 'M' && magic[2] == 'X' && magic[3] == ' '))
                throw new InvalidDataException(
                    "PMX ファイルではありません (PMD 形式は未対応です)。");

            float version = (float)Math.Round(r.F32(), 2);
            int nGlobals = r.U8();
            var g = r.Raw(nGlobals);
            var enc = g[0] == 0 ? Encoding.Unicode : Encoding.UTF8;  // 0 = UTF-16LE
            int addUv = g[1];
            int szVert = g[2], szTex = g[3], szMat = g[4], szBone = g[5], szMorph = g[6], szRigid = g[7];

            string Text()
            {
                int n = r.I32();
                return enc.GetString(r.Raw(n));
            }

            // 頂点 index は符号なし (4バイトのときだけ仕様上 signed int)。
            int VIdx() => szVert switch { 1 => r.U8(), 2 => r.U16(), 4 => r.I32(), _ => throw Bad(szVert) };
            // その他の index は符号付き (-1 = 無し)。
            Func<int> MakeIdx(int size) => size switch
            {
                1 => () => r.I8(),
                2 => () => r.I16(),
                4 => () => r.I32(),
                _ => throw Bad(size),
            };
            static InvalidDataException Bad(int size) =>
                new($"index サイズ {size} は不正です (1/2/4 のみ)。");

            var tIdx = MakeIdx(szTex);
            var mIdx = MakeIdx(szMat);
            var bIdx = MakeIdx(szBone);
            var moIdx = MakeIdx(szMorph);
            var rIdx = MakeIdx(szRigid);

            var model = new PmxModel
            {
                Version = version,
                Encoding = g[0] == 0 ? "utf-16-le" : "utf-8",
                AddUvCount = addUv,
                Name = Text(),
                NameEn = Text(),
                Comment = Text(),
                CommentEn = Text(),
            };

            // ---- 頂点 --------------------------------------------------------
            int n = r.I32();
            model.Vertices.Capacity = n;
            for (int i = 0; i < n; i++)
            {
                var v = new PmxVertex
                {
                    Pos = r.Vec(3),
                    Normal = r.Vec(3),
                    Uv = r.Vec(2),
                };
                v.AddUv = new float[addUv][];
                for (int k = 0; k < addUv; k++) v.AddUv[k] = r.Vec(4);

                int wt = r.U8();
                v.WeightType = wt;
                switch (wt)
                {
                    case 0:  // BDEF1
                        v.Bones = new[] { bIdx() };
                        v.Weights = new[] { 1.0f };
                        break;
                    case 1:  // BDEF2
                    {
                        v.Bones = new[] { bIdx(), bIdx() };
                        float w = r.F32();
                        v.Weights = new[] { w, 1.0f - w };
                        break;
                    }
                    case 2:  // BDEF4
                    case 4:  // QDEF (2.1)
                        v.Bones = new[] { bIdx(), bIdx(), bIdx(), bIdx() };
                        v.Weights = r.Vec(4);
                        break;
                    case 3:  // SDEF
                    {
                        v.Bones = new[] { bIdx(), bIdx() };
                        float w = r.F32();
                        v.Weights = new[] { w, 1.0f - w };
                        v.Sdef = new PmxSdef { C = r.Vec(3), R0 = r.Vec(3), R1 = r.Vec(3) };
                        break;
                    }
                    default:
                        throw new InvalidDataException($"未知のウェイト種別 {wt} (頂点 {i})。");
                }
                v.Edge = r.F32();
                model.Vertices.Add(v);
            }

            // ---- 面 ----------------------------------------------------------
            n = r.I32();
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = VIdx();
            model.Indices = indices;

            // ---- テクスチャ --------------------------------------------------
            n = r.I32();
            for (int i = 0; i < n; i++) model.Textures.Add(Text());

            // ---- 材質 --------------------------------------------------------
            n = r.I32();
            for (int i = 0; i < n; i++)
            {
                var m = new PmxMaterial
                {
                    Name = Text(),
                    NameEn = Text(),
                    Diffuse = r.Vec(4),
                    Specular = r.Vec(3),
                    SpecularPower = r.F32(),
                    Ambient = r.Vec(3),
                    Flags = r.U8(),
                    EdgeColor = r.Vec(4),
                    EdgeSize = r.F32(),
                    Texture = tIdx(),
                    SphereTexture = tIdx(),
                    SphereMode = r.U8(),
                };
                if (r.U8() != 0) { m.ToonShared = r.I8(); m.ToonTexture = -1; }
                else { m.ToonShared = -1; m.ToonTexture = tIdx(); }
                m.Memo = Text();
                m.IndexCount = r.I32();
                model.Materials.Add(m);
            }

            // ---- ボーン ------------------------------------------------------
            n = r.I32();
            for (int i = 0; i < n; i++)
            {
                var b = new PmxBone
                {
                    Name = Text(),
                    NameEn = Text(),
                    Pos = r.Vec(3),
                    Parent = bIdx(),
                    Layer = r.I32(),
                    Flags = r.U16(),
                };
                int fl = b.Flags;
                if ((fl & 0x0001) != 0) b.TailBone = bIdx();
                else b.TailOffset = r.Vec(3);
                if ((fl & (0x0100 | 0x0200)) != 0)   // 回転付与 / 移動付与
                {
                    b.InheritParent = bIdx();
                    b.InheritRatio = r.F32();
                }
                if ((fl & 0x0400) != 0) b.FixedAxis = r.Vec(3);
                if ((fl & 0x0800) != 0) { b.LocalX = r.Vec(3); b.LocalZ = r.Vec(3); }
                if ((fl & 0x2000) != 0) b.ExternalKey = r.I32();
                if ((fl & 0x0020) != 0)              // IK
                {
                    var ik = new PmxIk { Target = bIdx(), Loop = r.I32(), LimitAngle = r.F32() };
                    int nl = r.I32();
                    for (int k = 0; k < nl; k++)
                    {
                        var link = new PmxIkLink { Bone = bIdx() };
                        if (r.U8() != 0) { link.Min = r.Vec(3); link.Max = r.Vec(3); }
                        ik.Links.Add(link);
                    }
                    b.Ik = ik;
                }
                model.Bones.Add(b);
            }

            // ---- モーフ ------------------------------------------------------
            n = r.I32();
            for (int i = 0; i < n; i++)
            {
                var mo = new PmxMorph
                {
                    Name = Text(),
                    NameEn = Text(),
                    Panel = r.U8(),
                    Type = r.U8(),
                };
                int t = mo.Type;
                int cnt = r.I32();
                for (int k = 0; k < cnt; k++)
                {
                    var off = new PmxMorphOffset();
                    if (t == 0 || t == 9)            // グループ / フリップ(2.1)
                    {
                        off.Morph = moIdx();
                        off.Weight = r.F32();
                    }
                    else if (t == 1)                 // 頂点
                    {
                        off.Vertex = VIdx();
                        off.Offset = r.Vec(3);
                    }
                    else if (t == 2)                 // ボーン
                    {
                        off.Bone = bIdx();
                        off.Translation = r.Vec(3);
                        off.Rotation = r.Vec(4);
                    }
                    else if (t >= 3 && t <= 7)       // UV / 追加UV1-4
                    {
                        off.Vertex = VIdx();
                        off.Offset = r.Vec(4);
                    }
                    else if (t == 8)                 // 材質
                    {
                        off.Material = mIdx();
                        off.Op = r.U8();
                        off.Diffuse = r.Vec(4);
                        off.Specular = r.Vec(3);
                        off.SpecularPower = r.F32();
                        off.Ambient = r.Vec(3);
                        off.EdgeColor = r.Vec(4);
                        off.EdgeSize = r.F32();
                        off.TextureTint = r.Vec(4);
                        off.SphereTint = r.Vec(4);
                        off.ToonTint = r.Vec(4);
                    }
                    else if (t == 10)                // インパルス(2.1)
                    {
                        off.Rigid = rIdx();
                        off.Local = r.U8();
                        off.Velocity = r.Vec(3);
                        off.Torque = r.Vec(3);
                    }
                    else
                    {
                        throw new InvalidDataException($"未知のモーフ種別 {t} (モーフ '{mo.Name}')。");
                    }
                    mo.Offsets.Add(off);
                }
                model.Morphs.Add(mo);
            }

            // ---- 表示枠 ------------------------------------------------------
            n = r.I32();
            for (int i = 0; i < n; i++)
            {
                var fr = new PmxDisplayFrame { Name = Text(), NameEn = Text(), Special = r.U8() };
                int cnt = r.I32();
                for (int k = 0; k < cnt; k++)
                {
                    int kind = r.U8();
                    fr.Items.Add(new PmxDisplayItem
                    {
                        Type = kind == 0 ? "bone" : "morph",
                        Index = kind == 0 ? bIdx() : moIdx(),
                    });
                }
                model.DisplayFrames.Add(fr);
            }

            // ---- 剛体 --------------------------------------------------------
            n = r.I32();
            for (int i = 0; i < n; i++)
            {
                model.RigidBodies.Add(new PmxRigidBody
                {
                    Name = Text(),
                    NameEn = Text(),
                    Bone = bIdx(),
                    Group = r.U8(),
                    NoCollisionMask = r.U16(),
                    Shape = r.U8(),
                    Size = r.Vec(3),
                    Pos = r.Vec(3),
                    Rot = r.Vec(3),
                    Mass = r.F32(),
                    LinearDamping = r.F32(),
                    AngularDamping = r.F32(),
                    Restitution = r.F32(),
                    Friction = r.F32(),
                    Mode = r.U8(),
                });
            }

            // ---- Joint -------------------------------------------------------
            n = r.I32();
            for (int i = 0; i < n; i++)
            {
                model.Joints.Add(new PmxJoint
                {
                    Name = Text(),
                    NameEn = Text(),
                    Type = r.U8(),
                    RigidA = rIdx(),
                    RigidB = rIdx(),
                    Pos = r.Vec(3),
                    Rot = r.Vec(3),
                    PosMin = r.Vec(3),
                    PosMax = r.Vec(3),
                    RotMin = r.Vec(3),
                    RotMax = r.Vec(3),
                    SpringPos = r.Vec(3),
                    SpringRot = r.Vec(3),
                });
            }

            // PMX 2.1 の SoftBody は読まない (極めて稀)。残りバイト数だけ記録する。
            model.TrailingBytes = r.Length - r.Offset;
            return model;
        }
    }
}
