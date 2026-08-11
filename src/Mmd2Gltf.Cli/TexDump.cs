// ===========================================================================
// texdump — テクスチャの読み込み結果を JSON で出す。
// Python 版 (tools/texdump.py) が同じ形式を出すので、diff で移植の正しさを確認する。
// 出力する PNG の中身も比較できるよう、バイト列の SHA-256 と画素の一致率を出す。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mmd2Gltf.Cli
{
    public static class TexDump
    {
        private static readonly string[] Exts =
            { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".sph", ".spa", ".dds", ".tif", ".tiff", ".gif" };

        public static int Run(string[] args)
        {
            string outPath = null;
            var inputs = new List<string>();
            string pngDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o": outPath = args[++i]; break;
                    case "--png-dir": pngDir = args[++i]; break;   // 変換後 PNG も書き出す
                    default: inputs.Add(args[i]); break;
                }
            }
            if (inputs.Count == 0) throw new ArgumentException("入力 (ディレクトリかファイル) を指定してください。");
            if (pngDir != null) Directory.CreateDirectory(pngDir);

            var files = new List<string>();
            foreach (string inp in inputs)
            {
                if (Directory.Exists(inp))
                    files.AddRange(Directory.EnumerateFiles(inp, "*", SearchOption.AllDirectories)
                        .Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant())));
                else if (File.Exists(inp)) files.Add(inp);
                else Console.Error.WriteLine($"見つかりません: {inp}");
            }
            files.Sort(StringComparer.Ordinal);

            var result = new JsonArray();
            foreach (string f in files)
            {
                var tex = TextureLoader.Load(f, w => Console.Error.WriteLine("  警告: " + w));
                var entry = new JsonObject { ["file"] = f.Replace('\\', '/') };
                if (tex == null) { entry["error"] = "load failed"; result.Add(entry); continue; }

                entry["mime"] = tex.Mime;
                entry["standardClass"] = tex.StandardClass.ToString().ToLowerInvariant();
                entry["rawClass"] = tex.RawClass.ToString().ToLowerInvariant();
                entry["hasOriginal"] = tex.OriginalData != null;
                entry["dataSha256"] = Sha(tex.Data);
                if (tex.AverageColor is { } c)
                    entry["averageColor"] = new JsonArray(R6(c.R), R6(c.G), R6(c.B));
                if (tex.Stats != null)
                    entry["stats"] = new JsonObject
                    {
                        ["belowFrac"] = R6(tex.Stats.BelowFrac),
                        ["semiLowFrac"] = R6(tex.Stats.SemiLowFrac),
                        ["semiHighFrac"] = R6(tex.Stats.SemiHighFrac),
                        ["meanAlphaOfBelow"] = tex.Stats.MeanAlphaOfBelow.HasValue
                            ? R6(tex.Stats.MeanAlphaOfBelow.Value) : null,
                    };
                result.Add(entry);

                if (pngDir != null && tex.Mime == "image/png")
                    File.WriteAllBytes(Path.Combine(pngDir, SafeName(f) + ".png"), tex.Data);
            }

            string json = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            if (outPath != null) File.WriteAllText(outPath, json);
            else Console.WriteLine(json);
            Console.Error.WriteLine($"{files.Count} 個のテクスチャを処理しました。");
            return 0;
        }

        // JSON の桁ゆれで差分が出ないよう、両実装とも 6 桁に丸めて比較する。
        private static double R6(double v) => Math.Round(v, 6);

        private static string Sha(byte[] data) =>
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()[..16];

        // モデルをまたぐと同じファイル名 (eye.png 等) が衝突して比較から漏れるので、
        // フルパスのハッシュを前置する。Python 版 (tools/texdump.py) と同じ規則。
        private static string SafeName(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();
            string h = Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(full))).ToLowerInvariant()[..8];
            string s = Path.GetFileName(path);
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return h + "_" + s;
        }
    }
}
