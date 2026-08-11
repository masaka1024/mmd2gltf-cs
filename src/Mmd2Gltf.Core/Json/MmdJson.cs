// ===========================================================================
// JSON の共通設定。extras.mmd へ PMX の生データをそのまま載せるときと、
// 検証用のダンプの両方で使う。
//
// float は double へ広げて書く: Python 側は struct で読んだ float32 を float64 に
// 入れて JSON へ出すので、こうすると数値がテキストレベルで一致する。
// ===========================================================================
using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mmd2Gltf
{
    public sealed class FloatAsDoubleConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            => (float)r.GetDouble();
        public override void Write(Utf8JsonWriter w, float v, JsonSerializerOptions o)
            => w.WriteNumberValue((double)v);
    }

    /// <summary>byte[] を数値配列として書く。既定は base64 文字列になってしまうため
    /// (VMD のベジェ制御点が該当)。</summary>
    public sealed class ByteArrayAsNumbersConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter w, byte[] v, JsonSerializerOptions o)
        {
            w.WriteStartArray();
            foreach (byte b in v) w.WriteNumberValue(b);
            w.WriteEndArray();
        }
    }

    public static class MmdJson
    {
        public static readonly JsonSerializerOptions Options = Make();

        private static JsonSerializerOptions Make()
        {
            var o = new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                IncludeFields = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            o.Converters.Add(new FloatAsDoubleConverter());
            o.Converters.Add(new ByteArrayAsNumbersConverter());
            return o;
        }
    }
}
