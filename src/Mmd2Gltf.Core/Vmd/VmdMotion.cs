// ===========================================================================
// VMD (Vocaloid Motion Data 0002) のデータ構造とパーサ。Python 版 vmd.py の移植。
// ボーンキー (X/Y/Z/R の4本のベジェ曲線つき) とモーフキー、IK on/off キーを読む。
// カメラ / 照明 / セルフ影は読み飛ばす。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;

namespace Mmd2Gltf.Vmd
{
    public sealed class VmdBoneKey
    {
        [JsonPropertyName("frame")] public uint Frame;
        [JsonPropertyName("pos")] public float[] Pos;
        [JsonPropertyName("rot")] public float[] Rot;      // (x, y, z, w)
        /// <summary>[X, Y, Z, R] それぞれ (x1, y1, x2, y2)、0..127。</summary>
        [JsonPropertyName("curves")] public byte[][] Curves;
    }

    public sealed class VmdIkFrame
    {
        [JsonPropertyName("frame")] public uint Frame;
        [JsonPropertyName("show")] public bool Show;
        [JsonPropertyName("ik")] public Dictionary<string, bool> Ik = new();
    }

    public sealed class VmdMotion
    {
        [JsonPropertyName("model_name")] public string ModelName;
        [JsonPropertyName("bones")] public Dictionary<string, List<VmdBoneKey>> Bones = new();
        /// <summary>モーフ名 -> (フレーム, 重み) の列。</summary>
        [JsonPropertyName("morphs")] public Dictionary<string, List<float[]>> Morphs = new();
        [JsonPropertyName("ik_frames")] public List<VmdIkFrame> IkFrames = new();
        [JsonPropertyName("max_frame")] public uint MaxFrame;
    }

    public static class VmdParser
    {
        // VMD の文字列は Shift-JIS 固定長で、末尾は NUL 以降がゴミのことがある。
        private static string Sjis(byte[] d, int offset, int len)
        {
            int end = offset;
            int limit = Math.Min(offset + len, d.Length);
            while (end < limit && d[end] != 0) end++;
            return ShiftJis.GetString(d, offset, end - offset);
        }

        private static readonly Encoding ShiftJis = GetShiftJis();

        private static Encoding GetShiftJis()
        {
            // .NET Core では既定で Shift-JIS が使えないため、コードページを登録する。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }

        public static VmdMotion Parse(string path) => Parse(File.ReadAllBytes(path));

        public static VmdMotion Parse(byte[] d)
        {
            const string Magic = "Vocaloid Motion Data 0002";
            if (d.Length < 30 || Encoding.ASCII.GetString(d, 0, Magic.Length) != Magic)
                throw new InvalidDataException(
                    "VMD 0002 ファイルではありません (旧 0001 形式は未対応です)。");

            int o = 30;
            var m = new VmdMotion { ModelName = Sjis(d, o, 20) };
            o += 20;

            // ---- ボーンキー ---------------------------------------------------
            uint n = BitConverter.ToUInt32(d, o); o += 4;
            for (uint i = 0; i < n; i++)
            {
                string name = Sjis(d, o, 15);
                var key = new VmdBoneKey
                {
                    Frame = BitConverter.ToUInt32(d, o + 15),
                    Pos = new[]
                    {
                        BitConverter.ToSingle(d, o + 19),
                        BitConverter.ToSingle(d, o + 23),
                        BitConverter.ToSingle(d, o + 27),
                    },
                    Rot = new[]
                    {
                        BitConverter.ToSingle(d, o + 31),
                        BitConverter.ToSingle(d, o + 35),
                        BitConverter.ToSingle(d, o + 39),
                        BitConverter.ToSingle(d, o + 43),
                    },
                };
                int ip = o + 47;
                key.Curves = new byte[4][];
                for (int c = 0; c < 4; c++)
                    key.Curves[c] = new[] { d[ip + c], d[ip + c + 4], d[ip + c + 8], d[ip + c + 12] };
                o += 111;

                if (!m.Bones.TryGetValue(name, out var list))
                    m.Bones[name] = list = new List<VmdBoneKey>();
                list.Add(key);
            }
            foreach (var list in m.Bones.Values)
                list.Sort((a, b) => a.Frame.CompareTo(b.Frame));

            // ---- モーフキー ---------------------------------------------------
            if (o + 4 <= d.Length)
            {
                n = BitConverter.ToUInt32(d, o); o += 4;
                for (uint i = 0; i < n; i++)
                {
                    string name = Sjis(d, o, 15);
                    float frame = BitConverter.ToUInt32(d, o + 15);
                    float w = BitConverter.ToSingle(d, o + 19);
                    o += 23;
                    if (!m.Morphs.TryGetValue(name, out var list))
                        m.Morphs[name] = list = new List<float[]>();
                    list.Add(new[] { frame, w });
                }
                foreach (var list in m.Morphs.Values)
                    list.Sort((a, b) => a[0].CompareTo(b[0]));
            }

            // ---- カメラ / 照明 / セルフ影 (読み飛ばす) --------------------------
            void SkipSection(int recordSize)
            {
                if (o + 4 > d.Length) return;
                uint cnt = BitConverter.ToUInt32(d, o);
                o += 4 + (int)cnt * recordSize;
            }
            SkipSection(61);   // カメラ
            SkipSection(28);   // 照明
            SkipSection(9);    // セルフ影

            // ---- IK on/off キー ------------------------------------------------
            if (o + 4 <= d.Length)
            {
                n = BitConverter.ToUInt32(d, o); o += 4;
                for (uint i = 0; i < n; i++)
                {
                    if (o + 9 > d.Length) break;
                    var fr = new VmdIkFrame
                    {
                        Frame = BitConverter.ToUInt32(d, o),
                        Show = d[o + 4] != 0,
                    };
                    uint cnt = BitConverter.ToUInt32(d, o + 5);
                    o += 9;
                    for (uint k = 0; k < cnt; k++)
                    {
                        if (o + 21 > d.Length) break;
                        fr.Ik[Sjis(d, o, 20)] = d[o + 20] != 0;
                        o += 21;
                    }
                    m.IkFrames.Add(fr);
                }
                m.IkFrames.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }

            uint max = 0;
            foreach (var list in m.Bones.Values)
                if (list.Count > 0 && list[^1].Frame > max) max = list[^1].Frame;
            foreach (var list in m.Morphs.Values)
                if (list.Count > 0 && (uint)list[^1][0] > max) max = (uint)list[^1][0];
            m.MaxFrame = max;
            return m;
        }

        /// <summary>MMD のベジェ曲線を x (0..1) で評価する。
        /// Python 側は float64 なので、突合できるよう double で計算する。</summary>
        public static double BezierY(double x, double x1, double y1, double x2, double y2)
        {
            if (x <= 0.0) return 0.0;
            if (x >= 1.0) return 1.0;
            // X(t) = 3(1-t)^2 t x1 + 3(1-t) t^2 x2 + t^3 を二分法で解く。
            double lo = 0.0, hi = 1.0;
            for (int i = 0; i < 24; i++)
            {
                double t = (lo + hi) * 0.5;
                double u = 1.0 - t;
                double xt = 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t;
                if (xt < x) lo = t; else hi = t;
            }
            double tt = (lo + hi) * 0.5;
            double uu = 1.0 - tt;
            return 3 * uu * uu * tt * y1 + 3 * uu * tt * tt * y2 + tt * tt * tt;
        }

        /// <summary>制御点が対角線上にある = 線形補間。</summary>
        public static bool CurveIsLinear(byte[] c) => c[0] == c[1] && c[2] == c[3];
    }
}
