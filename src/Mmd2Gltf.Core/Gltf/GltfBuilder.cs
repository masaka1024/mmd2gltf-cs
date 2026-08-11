// ===========================================================================
// 最小限の glTF 2.0 / GLB ビルダー。Python 版 gltf.py の移植。
//
// accessor に渡す値は double のまま持つ。バイナリへは float32 で書くが、
// accessor の min/max は丸める前の double を書く (Python 版と同じ挙動)。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mmd2Gltf
{
    public sealed class GltfBuilder
    {
        public const int Float = 5126;
        public const int UInt = 5125;
        public const int UShort = 5123;
        public const int UByte = 5121;
        public const int ArrayBuffer = 34962;
        public const int ElementArrayBuffer = 34963;

        private static readonly Dictionary<string, int> NComp = new()
        {
            ["SCALAR"] = 1, ["VEC2"] = 2, ["VEC3"] = 3, ["VEC4"] = 4, ["MAT4"] = 16,
        };

        public JsonObject J { get; }
        private readonly MemoryStream _bin = new();

        public GltfBuilder(string generator = "mmd2gltf")
        {
            J = new JsonObject
            {
                ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = generator },
                ["buffers"] = new JsonArray(),
                ["bufferViews"] = new JsonArray(),
                ["accessors"] = new JsonArray(),
            };
        }

        private JsonArray Arr(string key)
        {
            if (J[key] is not JsonArray a) { a = new JsonArray(); J[key] = a; }
            return a;
        }

        // ---- バイナリ -----------------------------------------------------
        public int AddBufferView(byte[] data, int? target = null, int? byteStride = null)
        {
            while (_bin.Length % 4 != 0) _bin.WriteByte(0);
            var bv = new JsonObject
            {
                ["buffer"] = 0,
                ["byteOffset"] = (int)_bin.Length,
                ["byteLength"] = data.Length,
            };
            if (target.HasValue) bv["target"] = target.Value;
            if (byteStride.HasValue) bv["byteStride"] = byteStride.Value;
            _bin.Write(data, 0, data.Length);
            Arr("bufferViews").Add(bv);
            return Arr("bufferViews").Count - 1;
        }

        // ---- accessor ------------------------------------------------------
        /// <summary>FLOAT の accessor。min/max は丸め前の double を書く。</summary>
        public int AddAccessor(IReadOnlyList<double> flat, string type,
                               int? target = null, bool minmax = false)
        {
            int nc = NComp[type];
            int count = flat.Count / nc;
            var data = new byte[flat.Count * 4];
            for (int i = 0; i < flat.Count; i++)
                BitConverter.TryWriteBytes(data.AsSpan(i * 4), (float)flat[i]);
            int bv = AddBufferView(data, target);

            var acc = new JsonObject
            {
                ["bufferView"] = bv,
                ["componentType"] = Float,
                ["count"] = count,
                ["type"] = type,
            };
            if (minmax && count > 0)
            {
                var mins = new double[nc];
                var maxs = new double[nc];
                for (int c = 0; c < nc; c++) { mins[c] = flat[c]; maxs[c] = flat[c]; }
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < nc; c++)
                    {
                        double v = flat[i * nc + c];
                        if (v < mins[c]) mins[c] = v;
                        if (v > maxs[c]) maxs[c] = v;
                    }
                acc["min"] = ToArray(mins);
                acc["max"] = ToArray(maxs);
            }
            Arr("accessors").Add(acc);
            return Arr("accessors").Count - 1;
        }

        /// <summary>整数の accessor (UINT / USHORT / UBYTE)。</summary>
        public int AddIntAccessor(IReadOnlyList<int> flat, int componentType, string type,
                                  int? target = null)
        {
            int nc = NComp[type];
            int size = componentType switch
            {
                UInt => 4, UShort => 2, UByte => 1,
                _ => throw new ArgumentException($"整数 accessor に componentType {componentType} は使えません。"),
            };
            var data = new byte[flat.Count * size];
            for (int i = 0; i < flat.Count; i++)
            {
                switch (size)
                {
                    case 4: BitConverter.TryWriteBytes(data.AsSpan(i * 4), (uint)flat[i]); break;
                    case 2: BitConverter.TryWriteBytes(data.AsSpan(i * 2), (ushort)flat[i]); break;
                    default: data[i] = (byte)flat[i]; break;
                }
            }
            int bv = AddBufferView(data, target);
            Arr("accessors").Add(new JsonObject
            {
                ["bufferView"] = bv,
                ["componentType"] = componentType,
                ["count"] = flat.Count / nc,
                ["type"] = type,
            });
            return Arr("accessors").Count - 1;
        }

        /// <summary>モーフターゲット用の sparse accessor。
        /// baseBv にゼロ埋め bufferView を渡すと、sparse を無視する / bufferView 無しの
        /// accessor を扱えないローダーとの互換性が上がる。</summary>
        public int AddSparse(int totalCount, IReadOnlyList<int> idxList,
                             IReadOnlyList<double> values, string type = "VEC3",
                             int? baseBv = null)
        {
            int nc = NComp[type];
            var idxBytes = new byte[idxList.Count * 4];
            for (int i = 0; i < idxList.Count; i++)
                BitConverter.TryWriteBytes(idxBytes.AsSpan(i * 4), (uint)idxList[i]);
            int iv = AddBufferView(idxBytes);

            var valBytes = new byte[values.Count * 4];
            for (int i = 0; i < values.Count; i++)
                BitConverter.TryWriteBytes(valBytes.AsSpan(i * 4), (float)values[i]);
            int vv = AddBufferView(valBytes);

            var mins = new double[nc];
            var maxs = new double[nc];
            for (int i = 0; i < values.Count; i += nc)
                for (int c = 0; c < nc; c++)
                {
                    double v = values[i + c];
                    if (v < mins[c]) mins[c] = v;
                    if (v > maxs[c]) maxs[c] = v;
                }

            var acc = new JsonObject
            {
                ["componentType"] = Float,
                ["count"] = totalCount,
                ["type"] = type,
                ["min"] = ToArray(mins),
                ["max"] = ToArray(maxs),
                ["sparse"] = new JsonObject
                {
                    ["count"] = idxList.Count,
                    ["indices"] = new JsonObject { ["bufferView"] = iv, ["componentType"] = UInt },
                    ["values"] = new JsonObject { ["bufferView"] = vv },
                },
            };
            if (baseBv.HasValue) acc["bufferView"] = baseBv.Value;
            Arr("accessors").Add(acc);
            return Arr("accessors").Count - 1;
        }

        public int AddImage(byte[] data, string mime, string name = null)
        {
            int bv = AddBufferView(data);
            var img = new JsonObject { ["bufferView"] = bv, ["mimeType"] = mime };
            if (name != null) img["name"] = name;
            Arr("images").Add(img);
            return Arr("images").Count - 1;
        }

        public static JsonArray ToArray(IReadOnlyList<double> v)
        {
            var a = new JsonArray();
            foreach (double x in v) a.Add(x);
            return a;
        }

        public static JsonArray ToArray(IReadOnlyList<float> v)
        {
            var a = new JsonArray();
            foreach (float x in v) a.Add((double)x);   // Python の float64 表現に合わせる
            return a;
        }

        // ---- 出力 -----------------------------------------------------------
        public void WriteGlb(string path)
        {
            while (_bin.Length % 4 != 0) _bin.WriteByte(0);
            J["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = (int)_bin.Length });

            // NaN/Infinity は JSON として不正。黙って壊れた GLB を作らないよう
            // ここで止める (Python 版の allow_nan=False と同じ意図)。
            var json = J.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            if (json.Contains("NaN") || json.Contains("Infinity"))
                throw new InvalidOperationException(
                    "glTF JSON に NaN / Infinity が入っています (JSON として不正で、" +
                    "ほとんどのビューアが読み込めない GLB になります)。");

            var js = Encoding.UTF8.GetBytes(json);
            int jsPad = (4 - (js.Length & 3)) & 3;
            int total = 12 + 8 + js.Length + jsPad + 8 + (int)_bin.Length;

            // 一時ファイルへ書いてから差し替える。書き込み途中で強制終了しても
            // 前回の正常な .glb を壊さないため。
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Encoding.ASCII.GetBytes("glTF"));
                w.Write((uint)2);
                w.Write((uint)total);
                w.Write((uint)(js.Length + jsPad));
                w.Write(Encoding.ASCII.GetBytes("JSON"));
                w.Write(js);
                for (int i = 0; i < jsPad; i++) w.Write((byte)0x20);
                w.Write((uint)_bin.Length);
                w.Write(Encoding.ASCII.GetBytes("BIN\0"));
                w.Write(_bin.GetBuffer(), 0, (int)_bin.Length);
            }
            File.Move(tmp, path, overwrite: true);
        }
    }
}
