// ===========================================================================
// テクスチャの読み込みと glTF 向けの前処理。
// Python 版 convert.py の load_texture / _alpha_class / _average_color /
// _prebake_mask_alpha を移植したもの (PIL の置き換え)。
//
// ★ここの分類としきい値は実モデルを見ながら調整された経験則で、
//   コメントに書かれた測定値がそのまま根拠になっている。数値を動かすときは
//   必ず実モデルで見た目を確認すること (Python 版のコメントも参照)。
//
// 対応形式: PNG / JPEG / BMP / TGA / GIF / TIFF は ImageSharp、DDS は Pfim。
// MMD の .sph / .spa は中身が BMP や PNG なので、拡張子ではなく内容で判定する。
//
// ★Python 版との既知かつ意図的な差 (最大 1/255):
//   16bit PNG を 8bit へ落とすとき、PIL は上位バイトを取る切り捨て、
//   ImageSharp は round(v*255/65535) のスケーリングを行う。後者が正しい変換。
//   (モデルD 16bit RGBA で該当)
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mmd2Gltf
{
    public enum AlphaClass { Opaque, Mask, Blend }

    /// <summary>アルファ分布の統計 (分類の根拠。extras.mmd とログに出す)。</summary>
    public sealed class AlphaStats
    {
        public double BelowFrac;        // alpha &lt; 250 の割合
        public double SemiLowFrac;      // alpha 5..127 の割合 (本当に透けている)
        public double SemiHighFrac;     // alpha 128..249 の割合 (薄い半透明)
        public double? MeanAlphaOfBelow; // alpha &lt; 250 の平均 (0..255)
    }

    public sealed class LoadedTexture
    {
        public byte[] Data;             // glTF に埋め込むバイト列 (PNG か JPEG)
        public string Mime;
        public AlphaClass StandardClass; // alphaMode / prebake に使う分類
        public AlphaClass RawClass;      // 元テクスチャそのものの分類 (extras.mmd 用)
        public (float R, float G, float B)? AverageColor;
        public AlphaStats Stats;
        public byte[] OriginalData;      // prebake 前の PNG (mask のときだけ)
    }

    public static class TextureLoader
    {
        /// <summary>PIL が無いと読めない形式 (Python 版の警告と同じ一覧)。参考用。</summary>
        public static readonly string[] DecodeRequiredExts =
            { ".bmp", ".tga", ".sph", ".spa", ".dds", ".tif", ".tiff", ".gif" };

        // ------------------------------------------------------------------
        // パス解決 (Windows で作られたモデルは大文字小文字が合わないことがある)
        // ------------------------------------------------------------------
        public static string ResolvePath(string baseDir, string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            rel = rel.Replace('\\', '/').Trim();
            if (rel.Length == 0) return null;

            string direct = Path.GetFullPath(Path.Combine(baseDir, rel));
            if (File.Exists(direct)) return direct;

            // 1段ずつ大文字小文字を無視して探す
            string cur = baseDir;
            foreach (string part in rel.Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { cur = Path.GetDirectoryName(cur); continue; }
                string match = null;
                try
                {
                    foreach (string e in Directory.EnumerateFileSystemEntries(cur))
                        if (string.Equals(Nfc(Path.GetFileName(e)), Nfc(part), StringComparison.OrdinalIgnoreCase))
                        { match = Path.GetFileName(e); break; }
                }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
                if (match == null) return null;
                cur = Path.Combine(cur, match);
            }
            return File.Exists(cur) ? cur : null;
        }

        // macOS では濁点・半濁点を「が」= 「か」+「゛」のように分解した形 (NFD) で
        // ファイル名を持つことがある。PMX 側は合成済みの形 (NFC) なので、
        // 見た目が同じでも文字列比較では一致しない。比較の前に NFC へ揃える。
        // (Windows では元々 NFC なので結果は変わらない)
        // ★string.Normalize は使わない: CLI / physbake は InvariantGlobalization=true
        //   なので Normalize が何もしない。かな の濁点・半濁点だけ自前で合成する。
        internal static string Nfc(string s)
        {
            if (s == null || (s.IndexOf('゙') < 0 && s.IndexOf('゚') < 0)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (sb.Length > 0 && (c == '゙' || c == '゚'))
                {
                    char prev = sb[sb.Length - 1];
                    char composed = ComposeKana(prev, c == '゙');
                    if (composed != '\0') { sb[sb.Length - 1] = composed; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // 清音 + 濁点(゛) / 半濁点(゜) → 合成済みの1文字。合成できなければ '\0'。
        private static char ComposeKana(char c, bool dakuten)
        {
            const string kaToTo = "かきくけこさしすせそたちつてと" + "カキクケコサシスセソタチツテト";
            const string haToHo = "はひふへほ" + "ハヒフヘホ";
            if (dakuten)
            {
                // Unicode では「か」の直後が「が」のように、濁音は清音の +1 に並ぶ。
                if (kaToTo.IndexOf(c) >= 0 || haToHo.IndexOf(c) >= 0) return (char)(c + 1);
                switch (c)
                {
                    case 'う': return 'ゔ';
                    case 'ウ': return 'ヴ';
                    case 'ワ': return 'ヷ';
                    case 'ヰ': return 'ヸ';
                    case 'ヱ': return 'ヹ';
                    case 'ヲ': return 'ヺ';
                    case 'ゝ': return 'ゞ';
                    case 'ヽ': return 'ヾ';
                }
                return '\0';
            }
            // 半濁音は清音の +2 (は→ぱ)。
            return haToHo.IndexOf(c) >= 0 ? (char)(c + 2) : '\0';
        }

        // ------------------------------------------------------------------
        // 読み込み本体
        // ------------------------------------------------------------------
        public static LoadedTexture Load(string path, Action<string> warn = null)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // JPEG は再エンコードせず素通し (Python 版と同じ)。JPEG にアルファは無い。
            if (ext == ".jpg" || ext == ".jpeg")
                return new LoadedTexture
                {
                    Data = File.ReadAllBytes(path),
                    Mime = "image/jpeg",
                    StandardClass = AlphaClass.Opaque,
                    RawClass = AlphaClass.Opaque,
                };

            Image<Rgba32> img;
            try { img = Decode(path); }
            catch (Exception e)
            {
                warn?.Invoke($"テクスチャを読めませんでした: {path} ({e.Message})");
                return null;
            }

            using (img)
            {
                var rawClass = ClassifyAlpha(img, out AlphaStats stats);
                var avg = AverageColor(img);

                // standardClass: *標準の* glTF パイプラインが使う分類。
                // 素朴なビューアは深度書き込み無しでアルファブレンドするので、
                // 大きく重なるメッシュ (髪) に BLEND を使うと裏返って見える。
                // 「薄い半透明 (semi_high) だけを理由に blend」になったものは
                // ここで mask に落とす。元テクスチャは OriginalData に残し、
                // extras.mmd 経由で対応済みのインポータが本来のブレンドへ戻せる。
                var standardClass = rawClass;
                if (rawClass == AlphaClass.Blend && stats != null && stats.SemiLowFrac <= 0.25)
                    standardClass = AlphaClass.Mask;

                byte[] originalData = null;
                if (standardClass == AlphaClass.Mask)
                {
                    originalData = EncodePng(img, withAlpha: true);
                    PrebakeMaskAlpha(img);
                }

                return new LoadedTexture
                {
                    Data = EncodePng(img, withAlpha: standardClass != AlphaClass.Opaque),
                    Mime = "image/png",
                    StandardClass = standardClass,
                    RawClass = rawClass,
                    AverageColor = avg,
                    Stats = stats,
                    OriginalData = originalData,
                };
            }
        }

        // ------------------------------------------------------------------
        // デコード
        // ------------------------------------------------------------------
        private static Image<Rgba32> Decode(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (IsDds(data)) return DecodeDds(data);
            // 拡張子ではなく内容で判定する (MMD の .sph / .spa は中身が BMP/PNG)。
            return Image.Load<Rgba32>(data);
        }

        private static bool IsDds(byte[] d) =>
            d.Length >= 4 && d[0] == 0x44 && d[1] == 0x44 && d[2] == 0x53 && d[3] == 0x20; // "DDS "

        /// <summary>DDS を Pfim でデコードする。
        /// ★手元に DDS のテストデータが無く未検証 (所持12モデルに1つも無い)。
        /// 実際の DDS で問題が出たらここを疑うこと。</summary>
        private static Image<Rgba32> DecodeDds(byte[] data)
        {
            using var pf = Pfim.Pfimage.FromStream(new MemoryStream(data));
            int w = pf.Width, h = pf.Height;
            var img = new Image<Rgba32>(w, h);
            byte[] src = pf.Data;
            int stride = pf.Stride;

            // Pfim はチャンネルを BGR(A) 順で返す。
            int bpp = pf.Format switch
            {
                Pfim.ImageFormat.Rgba32 => 4,
                Pfim.ImageFormat.Rgb24 => 3,
                _ => throw new NotSupportedException($"未対応の DDS フォーマット: {pf.Format}"),
            };
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int o = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = o + x * bpp;
                        row[x] = new Rgba32(src[i + 2], src[i + 1], src[i],
                                            bpp == 4 ? src[i + 3] : (byte)255);
                    }
                }
            });
            return img;
        }

        private static byte[] EncodePng(Image<Rgba32> img, bool withAlpha)
        {
            var encoder = new PngEncoder
            {
                ColorType = withAlpha ? PngColorType.RgbWithAlpha : PngColorType.Rgb,
                // ICC / gamma チャンクは glTF バリデータが警告するので付けない。
                ChunkFilter = PngChunkFilter.ExcludeAll,
            };
            using var ms = new MemoryStream();
            img.Save(ms, encoder);
            return ms.ToArray();
        }

        // ------------------------------------------------------------------
        // アルファ分類
        // ------------------------------------------------------------------
        /// <summary>
        /// アルファの使われ方を opaque / mask / blend に分類する。
        ///   mask  = ほぼ二値 → alphaMode MASK (切り抜き)。肌や顔のテクスチャで
        ///           使っていない UV 領域が透明なもの。BLEND にすると深度ソートが
        ///           壊れて顔が裏返って見える。
        ///   blend = 半透明の texel がそれなりにある (毛先など) → 本当のブレンド。
        /// しきい値の根拠 (モデルA モデルでの実測): 前髪 semi_high=0.162 → blend /
        /// 顔の肌 0.036 → mask (眉の prebake を効かせるため) / 口 0.014 → mask。
        /// </summary>
        public static AlphaClass ClassifyAlpha(Image<Rgba32> img, out AlphaStats stats)
        {
            var hist = new long[256];
            long total = 0;
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++) hist[row[x].A]++;
                }
            });
            foreach (long c in hist) total += c;
            if (total == 0) total = 1;

            long belowCount = 0;
            for (int v = 0; v < 250; v++) belowCount += hist[v];
            if (belowCount == 0) { stats = null; return AlphaClass.Opaque; }

            long semiLow = 0;
            for (int v = 5; v < 128; v++) semiLow += hist[v];
            long semiHigh = 0;
            for (int v = 128; v < 250; v++) semiHigh += hist[v];
            double meanBelow = 0;
            for (int v = 0; v < 250; v++) meanBelow += (double)v * hist[v];
            meanBelow /= belowCount;

            stats = new AlphaStats
            {
                BelowFrac = (double)belowCount / total,
                SemiLowFrac = (double)semiLow / total,
                SemiHighFrac = (double)semiHigh / total,
                MeanAlphaOfBelow = meanBelow,
            };
            return (stats.SemiLowFrac > 0.25 || stats.SemiHighFrac > 0.10)
                ? AlphaClass.Blend : AlphaClass.Mask;
        }

        // ------------------------------------------------------------------
        // 平均色
        // ------------------------------------------------------------------
        /// <summary>
        /// 平均色 (0..1)。拡散色も環境色も黒で、色がスフィアマップからしか来ない
        /// MMD の金属材質向けに、静的な baseColorFactor の代用として使う。
        /// glTF はスフィア反射を表現できないので、真っ黒にするよりはるかにマシ。
        /// ※Python 版に合わせて 32x32 へ縮小してから平均する (全画素平均の方が
        ///   正確だが、移植の突合ができなくなるので同じ手順にしてある)。
        /// </summary>
        public static (float R, float G, float B)? AverageColor(Image<Rgba32> img)
        {
            try
            {
                // ★縮小の *前に* アルファを捨てる。PIL の convert("RGB") は
                //   アルファを落として RGB をそのまま残すが、ImageSharp の Resize は
                //   既定でアルファを乗算するため、alpha=0 の領域 (肌テクスチャの
                //   未使用 UV など) の RGB が消えて平均色が暗くなる。
                //   実測: モデルA の 肌4.png で 0.769 → 0.757 とズレていた。
                using var opaque = img.Clone();
                opaque.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                            row[x] = new Rgba32(row[x].R, row[x].G, row[x].B, 255);
                    }
                });
                using var small = opaque.Clone(x => x.Resize(32, 32));
                double r = 0, g = 0, b = 0;
                long n = 0;
                small.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        { r += row[x].R; g += row[x].G; b += row[x].B; n++; }
                    }
                });
                if (n == 0) return null;
                return ((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(b / n / 255.0));
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // mask 用のアルファ焼き込み
        // ------------------------------------------------------------------
        /// <summary>
        /// 柔らかく描かれたアルファ (眉など) を RGB へ先に混ぜ込んでから、
        /// アルファを 0/255 の硬い切り抜きに潰す。
        ///
        /// MMD はこの手のテクスチャを本物のアルファブレンドで描く: 肌の上に
        /// 25% 程度の濃いインクを重ねて柔らかい茶色になる。こちらは深度ソートの
        /// 破綻を避けるため alphaMode=MASK を使うが、MASK はしきい値を超えた
        /// 画素を *素の色のまま* 出すので、25% の濃いストロークが真っ黒な線になる。
        /// そこで先にブレンド結果を焼き込む。背景色にはそのテクスチャ自身の
        /// 「完全不透明な画素の平均色」を使う (肌の色の代用)。
        /// </summary>
        public static void PrebakeMaskAlpha(Image<Rgba32> img, byte cutoff = 25)
        {
            double br = 0, bg = 0, bb = 0;
            long opaqueCount = 0;
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                        if (row[x].A >= 250)
                        { br += row[x].R; bg += row[x].G; bb += row[x].B; opaqueCount++; }
                }
            });
            if (opaqueCount == 0) return;   // 背景として使える画素が無い = 触らない
            br /= opaqueCount; bg /= opaqueCount; bb /= opaqueCount;

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        byte a = row[x].A;
                        if (a >= cutoff && a < 250)
                        {
                            double af = a / 255.0;
                            row[x] = new Rgba32(
                                Blend(row[x].R, br, af), Blend(row[x].G, bg, af),
                                Blend(row[x].B, bb, af), 255);
                        }
                        else
                        {
                            row[x] = new Rgba32(row[x].R, row[x].G, row[x].B,
                                                a >= cutoff ? (byte)255 : (byte)0);
                        }
                    }
                }
            });
        }

        private static byte Blend(byte channel, double bg, double alpha)
        {
            // numpy の round は偶数丸め (round-half-to-even)。Python 版と
            // 突合できるよう同じ丸めにしてある (視覚差は 1/255 で無いに等しいが、
            // 移植バグと意図的な差を区別できなくなるのを避けるため)。
            double v = Math.Round(channel * alpha + bg * (1.0 - alpha), MidpointRounding.ToEven);
            return (byte)Math.Clamp(v, 0, 255);
        }
    }
}
