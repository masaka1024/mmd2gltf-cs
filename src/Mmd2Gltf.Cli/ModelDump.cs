// ===========================================================================
// pmxdump / vmddump — パース結果を JSON で出す (Python 版との突合用)。
// Python 版 (tools/modeldump.py) が同じキー名の JSON を出すので diff できる。
//
// float は double へ広げて書く。Python は struct で読んだ float32 を float64 に
// 入れて JSON へ出すので、同じ表現になり数値がテキストレベルで一致する。
// ===========================================================================
using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mmd2Gltf.Pmx;
using Mmd2Gltf.Vmd;

namespace Mmd2Gltf.Cli
{
    /// <summary>float を double として書き出す。Python の float64 表現に揃えるため。</summary>
    public sealed class FloatAsDoubleConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            => (float)r.GetDouble();
        public override void Write(Utf8JsonWriter w, float v, JsonSerializerOptions o)
            => w.WriteNumberValue((double)v);
    }

    /// <summary>byte[] を数値配列として書き出す。既定では base64 文字列になってしまい、
    /// Python 側の list とは比較できないため (VMD のベジェ制御点が該当)。</summary>
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

    public static class ModelDump
    {
        private static readonly JsonSerializerOptions Options = Make();

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

        public static int RunPmx(string[] args)
        {
            Parse(args, out string input, out string output);
            var model = PmxParser.Parse(input);
            Write(model, output);
            Console.Error.WriteLine(
                $"PMX {model.Version} '{model.Name}'  頂点{model.Vertices.Count} 面{model.Indices.Length / 3} " +
                $"材質{model.Materials.Count} ボーン{model.Bones.Count} モーフ{model.Morphs.Count} " +
                $"剛体{model.RigidBodies.Count} Joint{model.Joints.Count} 余りバイト{model.TrailingBytes}");
            return 0;
        }

        public static int RunVmd(string[] args)
        {
            Parse(args, out string input, out string output);
            var vmd = VmdParser.Parse(input);
            Write(vmd, output);
            Console.Error.WriteLine(
                $"VMD '{vmd.ModelName}'  ボーン{vmd.Bones.Count}トラック モーフ{vmd.Morphs.Count}トラック " +
                $"IKキー{vmd.IkFrames.Count} 最終フレーム{vmd.MaxFrame}");
            return 0;
        }

        private static void Parse(string[] args, out string input, out string output)
        {
            input = null; output = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-o") output = args[++i];
                else if (input == null) input = args[i];
                else throw new ArgumentException("入力ファイルが複数指定されています。");
            }
            if (input == null) throw new ArgumentException("入力ファイルを指定してください。");
        }

        private static void Write(object value, string output)
        {
            if (output != null)
            {
                using var fs = File.Create(output);
                JsonSerializer.Serialize(fs, value, Options);
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(value, Options));
            }
        }
    }
}
