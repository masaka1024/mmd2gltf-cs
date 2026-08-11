// ===========================================================================
// GLB (glTF binary) コンテナの読み書き。
// JSON チャンクは System.Text.Json の JsonNode で保持し、書き換えて再直列化する。
// BIN チャンクは末尾に追記できる (新しい accessor 用)。
// 対応範囲は mmd2gltf が出力する GLB (buffer 1本・非圧縮・非 sparse) に限る。
// 想定外の入力は例外で止める (黙って壊れた GLB を吐かないため)。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mmd2Gltf
{
    public sealed class Glb
    {
        private const uint MagicGlb = 0x46546C67;   // "glTF"
        private const uint ChunkJson = 0x4E4F534A;  // "JSON"
        private const uint ChunkBin = 0x004E4942;   // "BIN\0"

        public const int ComponentFloat = 5126;
        public const int ComponentUShort = 5123;
        public const int ComponentUInt = 5125;

        public JsonObject Root { get; private set; }

        private byte[] _bin;          // 元の BIN チャンク
        private readonly MemoryStream _extra = new();  // 追記分

        public static Glb Load(string path)
        {
            var g = new Glb();
            g.Parse(File.ReadAllBytes(path));
            return g;
        }

        private void Parse(byte[] d)
        {
            if (d.Length < 12 || BitConverter.ToUInt32(d, 0) != MagicGlb)
                throw new InvalidDataException("GLB マジックが不正です (glTF binary ではありません)。");

            int off = 12;
            string json = null;
            _bin = Array.Empty<byte>();
            while (off + 8 <= d.Length)
            {
                int clen = (int)BitConverter.ToUInt32(d, off);
                uint ctype = BitConverter.ToUInt32(d, off + 4);
                int cdata = off + 8;
                if (cdata + clen > d.Length)
                    throw new InvalidDataException("GLB のチャンク長がファイル長を超えています。");
                if (ctype == ChunkJson)
                    json = Encoding.UTF8.GetString(d, cdata, clen);
                else if (ctype == ChunkBin)
                {
                    _bin = new byte[clen];
                    Array.Copy(d, cdata, _bin, 0, clen);
                }
                off = cdata + clen;
                if ((clen & 3) != 0) off += 4 - (clen & 3);
            }
            if (json == null) throw new InvalidDataException("GLB に JSON チャンクがありません。");
            Root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException("GLB の JSON チャンクがオブジェクトではありません。");
        }

        // ------------------------------------------------------------------
        // 参照ヘルパ
        // ------------------------------------------------------------------
        public JsonArray Arr(string key) => Root[key] as JsonArray;

        public JsonArray RequireArr(string key) =>
            Root[key] as JsonArray ?? throw new InvalidDataException($"glTF に '{key}' がありません。");

        private static int Int(JsonNode n, string key, int fallback = -1) =>
            n is JsonObject o && o.TryGetPropertyValue(key, out var v) && v != null
                ? v.GetValue<int>() : fallback;

        private static string Str(JsonNode n, string key) =>
            n is JsonObject o && o.TryGetPropertyValue(key, out var v) && v != null
                ? v.GetValue<string>() : null;

        // ------------------------------------------------------------------
        // accessor 読み出し (FLOAT のみ / 非 sparse)
        // ------------------------------------------------------------------
        private static readonly Dictionary<string, int> TypeComponents = new()
        {
            ["SCALAR"] = 1, ["VEC2"] = 2, ["VEC3"] = 3, ["VEC4"] = 4,
            ["MAT2"] = 4, ["MAT3"] = 9, ["MAT4"] = 16,
        };

        /// <summary>accessor を float 配列で読む。戻り値の長さ = count * 成分数。</summary>
        public float[] ReadFloatAccessor(int accessorIndex, out int components)
        {
            var accessors = RequireArr("accessors");
            if (accessorIndex < 0 || accessorIndex >= accessors.Count)
                throw new InvalidDataException($"accessor {accessorIndex} が範囲外です。");
            var acc = accessors[accessorIndex];

            int compType = Int(acc, "componentType");
            if (compType != ComponentFloat)
                throw new NotSupportedException(
                    $"accessor {accessorIndex}: componentType {compType} は未対応です (FLOAT のみ)。");
            if (acc is JsonObject ao && ao.ContainsKey("sparse"))
                throw new NotSupportedException($"accessor {accessorIndex}: sparse は未対応です。");

            string type = Str(acc, "type") ?? "SCALAR";
            if (!TypeComponents.TryGetValue(type, out components))
                throw new InvalidDataException($"accessor {accessorIndex}: 未知の type '{type}'。");

            int count = Int(acc, "count", 0);
            int accOffset = Int(acc, "byteOffset", 0);
            int bvIndex = Int(acc, "bufferView");
            var result = new float[count * components];
            if (bvIndex < 0)  // bufferView 省略 = 全ゼロ
                return result;

            var bv = RequireArr("bufferViews")[bvIndex];
            int bvOffset = Int(bv, "byteOffset", 0);
            int stride = Int(bv, "byteStride", 0);
            if (stride <= 0) stride = components * 4;
            int bufIndex = Int(bv, "buffer", 0);
            if (bufIndex != 0)
                throw new NotSupportedException("buffer 0 以外は未対応です (GLB の BIN チャンクのみ)。");

            int baseOff = bvOffset + accOffset;
            for (int i = 0; i < count; i++)
            {
                int o = baseOff + i * stride;
                for (int c = 0; c < components; c++)
                    result[i * components + c] = BitConverter.ToSingle(_bin, o + c * 4);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 追記 (BIN 末尾へ) + bufferView / accessor 生成
        // ------------------------------------------------------------------
        private int AppendBufferView(byte[] data)
        {
            long cur = _bin.Length + _extra.Length;
            int pad = (int)((4 - (cur & 3)) & 3);
            for (int i = 0; i < pad; i++) _extra.WriteByte(0);
            int offset = (int)(_bin.Length + _extra.Length);
            _extra.Write(data, 0, data.Length);

            var views = Root["bufferViews"] as JsonArray;
            if (views == null) { views = new JsonArray(); Root["bufferViews"] = views; }
            views.Add(new JsonObject
            {
                ["buffer"] = 0,
                ["byteOffset"] = offset,
                ["byteLength"] = data.Length,
            });
            return views.Count - 1;
        }

        /// <summary>float 配列を新しい accessor として追加し、その index を返す。</summary>
        public int AddFloatAccessor(float[] values, string type, bool withMinMax = false)
        {
            if (!TypeComponents.TryGetValue(type, out int comps))
                throw new ArgumentException($"未知の accessor type '{type}'。", nameof(type));
            if (values.Length % comps != 0)
                throw new ArgumentException($"値の個数 {values.Length} が type {type} の成分数 {comps} で割り切れません。");

            var bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            int bv = AppendBufferView(bytes);

            var acc = new JsonObject
            {
                ["bufferView"] = bv,
                ["componentType"] = ComponentFloat,
                ["count"] = values.Length / comps,
                ["type"] = type,
            };
            if (withMinMax && values.Length > 0)
            {
                var min = new float[comps];
                var max = new float[comps];
                for (int c = 0; c < comps; c++) { min[c] = float.MaxValue; max[c] = float.MinValue; }
                for (int i = 0; i < values.Length; i++)
                {
                    int c = i % comps;
                    if (values[i] < min[c]) min[c] = values[i];
                    if (values[i] > max[c]) max[c] = values[i];
                }
                var jmin = new JsonArray();
                var jmax = new JsonArray();
                for (int c = 0; c < comps; c++) { jmin.Add(min[c]); jmax.Add(max[c]); }
                acc["min"] = jmin;
                acc["max"] = jmax;
            }

            var accessors = Root["accessors"] as JsonArray;
            if (accessors == null) { accessors = new JsonArray(); Root["accessors"] = accessors; }
            accessors.Add(acc);
            return accessors.Count - 1;
        }

        // ------------------------------------------------------------------
        // 保存
        // ------------------------------------------------------------------
        public void Save(string path)
        {
            var buffers = Root["buffers"] as JsonArray;
            if (buffers == null || buffers.Count == 0)
                throw new InvalidDataException("glTF に buffers がありません。");
            int total = _bin.Length + (int)_extra.Length;
            buffers[0]!["byteLength"] = total;

            var jsonBytes = Encoding.UTF8.GetBytes(Root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            int jsonPad = (4 - (jsonBytes.Length & 3)) & 3;
            int binPad = (4 - (total & 3)) & 3;

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var w = new BinaryWriter(fs);
            int length = 12 + 8 + jsonBytes.Length + jsonPad + 8 + total + binPad;
            w.Write(MagicGlb);
            w.Write((uint)2);
            w.Write((uint)length);

            w.Write((uint)(jsonBytes.Length + jsonPad));
            w.Write(ChunkJson);
            w.Write(jsonBytes);
            for (int i = 0; i < jsonPad; i++) w.Write((byte)0x20);   // JSON は空白でパディング

            w.Write((uint)(total + binPad));
            w.Write(ChunkBin);
            w.Write(_bin);
            w.Write(_extra.GetBuffer(), 0, (int)_extra.Length);
            for (int i = 0; i < binPad; i++) w.Write((byte)0x00);
        }
    }
}
