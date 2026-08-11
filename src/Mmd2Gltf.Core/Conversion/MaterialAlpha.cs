// ===========================================================================
// 共有テクスチャを使う材質のアルファ判定。Python 版 convert.py の
// _material_uv_alpha_stats / _is_overlay_material / _is_translucent_material。
//
// なぜ材質単位で見るのか: アルファ分類はテクスチャ単位だが、MMD のモデルは
// 1枚のテクスチャを「不透明な領域 (顔の肌)」と「完全に半透明な領域
// (額の髪影・チーク)」で共有することが多い。テクスチャ全体のヒストグラムは
// 不透明側に引きずられて "mask" になり、prebake が影のインクを不透明化して
// しまう (Tda式ミクで実測: hairshadow の使用UV領域は平均α0.217 / 最大0.47 で
// 不透明画素が1つも無い)。実際に使われている UV 領域だけを標本化して切り分ける。
// ===========================================================================
using System;
using System.Collections.Generic;
using Mmd2Gltf.Pmx;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mmd2Gltf.Conversion
{
    public sealed class UvAlphaStats
    {
        public double Mean;
        public double Max;
        public double FracBelowHalf;
        public double FracSemi;
        public int N;
    }

    public static class MaterialAlpha
    {
        /// <summary>ある材質が実際に使っている UV 領域だけでアルファを標本化する。</summary>
        public static UvAlphaStats Sample(Image<Rgba32> img, PmxModel model, int matIndex)
        {
            if (img == null) return null;
            int w = img.Width, h = img.Height;

            var vids = new HashSet<int>();
            int off = 0;
            bool found = false;
            for (int mi = 0; mi < model.Materials.Count; mi++)
            {
                int cnt = model.Materials[mi].IndexCount;
                if (mi == matIndex)
                {
                    for (int k = off; k < off + cnt && k < model.Indices.Length; k++)
                        vids.Add(model.Indices[k]);
                    found = true;
                    break;
                }
                off += cnt;
            }
            if (!found || vids.Count == 0) return null;

            double aSum = 0, aMax = 0;
            int below = 0, semi = 0, n = 0;
            img.ProcessPixelRows(accessor =>
            {
                foreach (int vi in vids)
                {
                    if (vi < 0 || vi >= model.Vertices.Count) continue;
                    var uvv = model.Vertices[vi].Uv;
                    // Python の % は常に非負を返す。C# の % は符号を保つので floor を使う。
                    double u = uvv[0] - Math.Floor((double)uvv[0]);
                    double v = uvv[1] - Math.Floor((double)uvv[1]);
                    int x = (int)(u * (w - 1));
                    int y = (int)(v * (h - 1));
                    double a = accessor.GetRowSpan(y)[x].A / 255.0;
                    aSum += a;
                    if (a > aMax) aMax = a;
                    if (a < 0.5) below++;
                    if (a > 0.02 && a < 0.98) semi++;
                    n++;
                }
            });
            if (n == 0) return null;
            return new UvAlphaStats
            {
                Mean = aSum / n,
                Max = aMax,
                FracBelowHalf = (double)below / n,
                FracSemi = (double)semi / n,
                N = n,
            };
        }

        /// <summary>不透明画素も持つが、使用 UV 領域の過半が中間アルファの材質
        /// (メガネのレンズ等)。標準 GLB はビューア安全のため MASK+prebake のままにし、
        /// extras.mmd.alphaClass だけを "blend" にして対応インポータへ知らせる。</summary>
        public static bool IsTranslucent(UvAlphaStats s) =>
            s != null && s.N >= 3 && s.FracSemi > 0.5;

        /// <summary>使用 UV 領域に不透明画素がほぼ無い材質は、意図的な半透明の
        /// 重ね描き (額の髪影・チーク)。MASK/prebake だと不透明な板になってしまうので、
        /// 実ブレンド + 無加工のオリジナルテクスチャで描く。
        /// 重ね描きのメッシュは小さく顔のすぐ上に浮いているだけなので、
        /// MASK を選ぶ理由だった深度ソートの問題は当てはまらない。</summary>
        public static bool IsOverlay(UvAlphaStats s) =>
            s != null && s.N >= 3 && s.FracBelowHalf >= 0.95 && s.Max < 0.6;
    }
}
