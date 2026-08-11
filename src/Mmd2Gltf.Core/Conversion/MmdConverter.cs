// ===========================================================================
// PMX (+VMD) -> glTF 2.0 (.glb)。Python 版 convert.py の移植。
//
// 座標変換 (MMD 左手系 Y-up -> glTF 右手系 Y-up):
//     位置 / 法線 : (x, y, z) -> (x, y, -z)
//     クォータニオン : (x, y, z, w) -> (-x, -y, z, w)
//     三角形の巻き順 : (a, b, c) -> (a, c, b)
// glTF が素で表せないもの (IK, 物理, スフィア/トゥーン, 材質・ボーンモーフ,
// SDEF, 表示枠, ...) は MMD 座標系のまま extras.mmd に載せて保存する。
//
// 揺れ物の物理はここでは焼かない。PMX の剛体/Joint は extras.mmd に載るので、
// physbake (自作エンジン) が後段でアニメーションチャンネルを差し替える。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mmd2Gltf.Animation;
using Mmd2Gltf.Pmx;
using Mmd2Gltf.Vmd;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mmd2Gltf.Conversion
{
    public sealed class ConvertOptions
    {
        public string VmdPath;
        public bool Unlit;
        public bool SolveIk = true;
        public int Step = 1;
        public bool Extras = true;
        public string AnimName;
        public List<string> DisableIk;
        public bool UseVmdIkFrames = true;
        /// <summary>"sparse" | "dense" | "none"</summary>
        public string MorphMode = "sparse";
        /// <summary>"auto" | "opaque" | "mask" | "blend"</summary>
        public string AlphaMode = "auto";
        public bool ForceDoubleSided;
        public bool CustomAttrs = true;
        public double Scale = 0.08;
    }

    public static class MmdConverter
    {
        // 頂点モーフ / UV モーフだけが glTF のモーフターゲットになる。
        private static bool IsTargetMorphType(int t) => t == 1 || t == 3;

        public static string Convert(string pmxPath, string outPath, ConvertOptions opt,
                                     Action<string> log = null)
        {
            opt ??= new ConvertOptions();
            log ??= _ => { };
            double scale = opt.Scale;

            var model = PmxParser.Parse(pmxPath);
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(pmxPath));
            log($"PMX {model.Version:0.0}  '{model.Name}'  {model.Vertices.Count} verts / " +
                $"{model.Indices.Length / 3} tris / {model.Materials.Count} mats / " +
                $"{model.Bones.Count} bones / {model.Morphs.Count} morphs");

            var g = new GltfBuilder();
            var verts = model.Vertices;
            int nv = verts.Count;
            int nb = model.Bones.Count;
            if (nb > 65535)
                throw new InvalidOperationException("ボーンが 65535 本を超えるモデルは未対応です。");

            // ---------------- 頂点属性 ----------------------------------------
            var pos = new List<double>(nv * 3);
            var nrm = new List<double>(nv * 3);
            var uv = new List<double>(nv * 2);
            var joints = new List<int>(nv * 4);
            var weights = new List<double>(nv * 4);
            var edge = new List<double>(nv);
            int addUvN = model.AddUvCount;
            var addUvs = new List<double>[addUvN];
            for (int k = 0; k < addUvN; k++) addUvs[k] = new List<double>(nv * 4);
            bool hasSdef = verts.Any(v => v.Sdef != null);
            var sdefC = new List<double>();
            var sdefR0 = new List<double>();
            var sdefR1 = new List<double>();
            var wtype = new List<double>();

            var acc = new Dictionary<int, double>(4);
            foreach (var v in verts)
            {
                pos.Add(v.Pos[0] * scale); pos.Add(v.Pos[1] * scale); pos.Add(-v.Pos[2] * scale);

                double nx = v.Normal[0], ny = v.Normal[1], nz = -(double)v.Normal[2];
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl > 1e-6) { nrm.Add(nx / nl); nrm.Add(ny / nl); nrm.Add(nz / nl); }
                else
                {
                    // MMD の一部の頂点は法線が (0,0,0) (非表示・物理専用など)。
                    // glTF は単位法線を要求するので任意の単位ベクトルで埋める。
                    nrm.Add(0.0); nrm.Add(0.0); nrm.Add(1.0);
                }

                uv.Add(v.Uv[0]); uv.Add(v.Uv[1]);
                edge.Add(v.Edge);
                for (int k = 0; k < addUvN; k++)
                    foreach (float c in v.AddUv[k]) addUvs[k].Add(c);

                // スキニング (SDEF/QDEF は線形ブレンドで近似)。
                // 重複するボーン index はここでまとめる。MMD のモデルは SDEF
                // (たまに BDEF2) の頂点で bone0 == bone1 になっていることが多く
                // (足首や肘のあたり)、そのままだと JOINTS_0 が [9,9,0,0] になって
                // glTF バリデータに弾かれる (ACCESSOR_JOINTS_INDEX_DUPLICATE)。
                // 重複分のウェイトを足して1つにまとめ、合計1.0へ正規化する。
                acc.Clear();
                for (int i = 0; i < v.Bones.Length; i++)
                {
                    double w = v.Weights[i];
                    if (w <= 0.0) continue;
                    int b = v.Bones[i];
                    if (b < 0 || b >= nb) b = 0;
                    acc[b] = acc.TryGetValue(b, out double old) ? old + w : w;
                }
                List<int> bs;
                List<double> ws;
                if (acc.Count > 0)
                {
                    // Python の sorted(..., key=-weight) は安定ソート。挿入順を保った
                    // まま重み降順にするため、同じ性質の順序付きソートを使う。
                    var top = acc.ToList();
                    var ordered = top.Select((kv, i) => (kv, i))
                                     .OrderByDescending(x => x.kv.Value)
                                     .ThenBy(x => x.i)
                                     .Take(4).ToList();
                    bs = ordered.Select(x => x.kv.Key).ToList();
                    ws = ordered.Select(x => x.kv.Value).ToList();
                    double s = ws.Sum();
                    for (int i = 0; i < ws.Count; i++) ws[i] /= s;
                }
                else { bs = new List<int> { 0 }; ws = new List<double> { 1.0 }; }
                while (bs.Count < 4) { bs.Add(0); ws.Add(0.0); }
                joints.AddRange(bs);
                weights.AddRange(ws);

                if (hasSdef)
                {
                    wtype.Add(v.WeightType);
                    var sd = v.Sdef;
                    if (sd != null)
                    {
                        AddCPos(sdefC, sd.C, scale);
                        AddCPos(sdefR0, sd.R0, scale);
                        AddCPos(sdefR1, sd.R1, scale);
                    }
                    else
                    {
                        for (int i = 0; i < 3; i++) { sdefC.Add(0); sdefR0.Add(0); sdefR1.Add(0); }
                    }
                }
            }

            const int AB = GltfBuilder.ArrayBuffer;
            var attrs = new JsonObject
            {
                ["POSITION"] = g.AddAccessor(pos, "VEC3", AB, minmax: true),
                ["NORMAL"] = g.AddAccessor(nrm, "VEC3", AB),
                ["TEXCOORD_0"] = g.AddAccessor(uv, "VEC2", AB),
                ["JOINTS_0"] = g.AddIntAccessor(joints, GltfBuilder.UShort, "VEC4", AB),
                ["WEIGHTS_0"] = g.AddAccessor(weights, "VEC4", AB),
            };
            if (opt.CustomAttrs)
            {
                if (edge.Any(e => e != 0.0))
                    attrs["_EDGESCALE"] = g.AddAccessor(edge, "SCALAR", AB);
                for (int k = 0; k < addUvN; k++)
                    attrs[$"_ADDUV{k + 1}"] = g.AddAccessor(addUvs[k], "VEC4", AB);
                if (hasSdef)
                {
                    attrs["_SDEF_C"] = g.AddAccessor(sdefC, "VEC3", AB);
                    attrs["_SDEF_R0"] = g.AddAccessor(sdefR0, "VEC3", AB);
                    attrs["_SDEF_R1"] = g.AddAccessor(sdefR1, "VEC3", AB);
                    // ubyte ではなく float。頂点属性は4バイト境界に揃える必要がある。
                    attrs["_WEIGHTTYPE"] = g.AddAccessor(wtype, "SCALAR", AB);
                }
            }

            // ---------------- モーフターゲット ---------------------------------
            var targetMorphs = opt.MorphMode != "none"
                ? MorphTargets.Collect(model, scale)
                : new List<MorphTargets.Entry>();
            var morphToTarget = new Dictionary<int, int>();
            for (int ti = 0; ti < targetMorphs.Count; ti++)
                morphToTarget[targetMorphs[ti].MorphIndex] = ti;

            var targets = new List<JsonObject>();
            var targetNames = new List<string>();
            int? zeroBv3 = null, zeroBv2 = null;
            if (targetMorphs.Count > 0 && opt.MorphMode == "sparse")
            {
                // 共有のゼロ埋めベース。複数の頂点属性 accessor が1つの view を
                // 共有するとき、バリデータは byteStride と target を要求する。
                zeroBv3 = g.AddBufferView(new byte[nv * 12], AB, 12);
                if (targetMorphs.Any(e => e.Uv.Count > 0))
                    zeroBv2 = g.AddBufferView(new byte[nv * 8], AB, 8);
            }
            foreach (var e in targetMorphs)
            {
                var tgt = new JsonObject();
                if (opt.MorphMode == "dense")
                {
                    var flat = new double[nv * 3];
                    foreach (var kv in e.Pos)
                    {
                        flat[kv.Key * 3] = kv.Value[0];
                        flat[kv.Key * 3 + 1] = kv.Value[1];
                        flat[kv.Key * 3 + 2] = -kv.Value[2];
                    }
                    tgt["POSITION"] = g.AddAccessor(flat, "VEC3", minmax: true);
                    if (e.Uv.Count > 0)
                    {
                        var flat2 = new double[nv * 2];
                        foreach (var kv in e.Uv)
                        {
                            flat2[kv.Key * 2] = kv.Value[0];
                            flat2[kv.Key * 2 + 1] = kv.Value[1];
                        }
                        tgt["TEXCOORD_0"] = g.AddAccessor(flat2, "VEC2");
                    }
                }
                else
                {
                    if (e.Pos.Count > 0)
                    {
                        var idxs = e.Pos.Keys.ToList();
                        idxs.Sort();
                        var flat = new List<double>(idxs.Count * 3);
                        foreach (int vi in idxs)
                        {
                            var d = e.Pos[vi];
                            flat.Add(d[0]); flat.Add(d[1]); flat.Add(-d[2]);
                        }
                        tgt["POSITION"] = g.AddSparse(nv, idxs, flat, "VEC3", zeroBv3);
                    }
                    else
                    {
                        tgt["POSITION"] = g.AddSparse(nv, new[] { 0 },
                            new double[] { 0, 0, 0 }, "VEC3", zeroBv3);
                    }
                    if (e.Uv.Count > 0)
                    {
                        var idxs = e.Uv.Keys.ToList();
                        idxs.Sort();
                        var flat = new List<double>(idxs.Count * 2);
                        foreach (int vi in idxs) { flat.Add(e.Uv[vi][0]); flat.Add(e.Uv[vi][1]); }
                        tgt["TEXCOORD_0"] = g.AddSparse(nv, idxs, flat, "VEC2", zeroBv2);
                    }
                }
                targets.Add(tgt);
                targetNames.Add(model.Morphs[e.MorphIndex].Name);
            }

            // ---------------- テクスチャ ---------------------------------------
            var texMap = new Dictionary<int, int>();       // PMX テクスチャ index -> glTF texture index
            var origTexMap = new Dictionary<int, int>();   // prebake 前のオリジナル
            var texAlpha = new Dictionary<int, AlphaClass>();
            var texAlphaTrue = new Dictionary<int, AlphaClass>();
            var texAvgColor = new Dictionary<int, (float R, float G, float B)?>();
            var texAlphaStats = new Dictionary<int, AlphaStats>();
            var texOrigImg = new Dictionary<int, Image<Rgba32>>();

            if (model.Textures.Count > 0)
            {
                g.J["samplers"] = new JsonArray(new JsonObject
                {
                    ["magFilter"] = 9729, ["minFilter"] = 9987,
                    ["wrapS"] = 10497, ["wrapT"] = 10497,
                });
                g.J["textures"] = new JsonArray();
            }
            for (int ti = 0; ti < model.Textures.Count; ti++)
            {
                string rel = model.Textures[ti];
                string p = TextureLoader.ResolvePath(baseDir, rel);
                if (p == null) { log("WARNING: texture not found: " + rel); continue; }
                var res = TextureLoader.Load(p, w => log("WARNING: " + w));
                if (res == null) continue;

                string leaf = Path.GetFileName(rel.Replace('\\', '/'));
                int img = g.AddImage(res.Data, res.Mime, leaf);
                var texArr = (JsonArray)g.J["textures"];
                texArr.Add(new JsonObject { ["sampler"] = 0, ["source"] = img, ["name"] = leaf });
                texMap[ti] = texArr.Count - 1;
                texAlpha[ti] = res.StandardClass;
                texAlphaTrue[ti] = res.RawClass;
                texAlphaStats[ti] = res.Stats;
                texAvgColor[ti] = res.AverageColor;

                // prebake 前の無加工オリジナル (mask のときだけ)。標準の材質からは
                // 参照しない追加テクスチャとして埋め込む。忠実なインポータは
                // extras.mmd.origTexture から見つけて、MMD 風の実アルファブレンド
                // (柔らかい眉・目の線画) を復元できる。
                if (res.OriginalData != null)
                {
                    int oimg = g.AddImage(res.OriginalData, "image/png", "orig_" + leaf);
                    texArr.Add(new JsonObject
                    {
                        ["sampler"] = 0, ["source"] = oimg, ["name"] = "orig_" + leaf,
                    });
                    origTexMap[ti] = texArr.Count - 1;
                    try { texOrigImg[ti] = Image.Load<Rgba32>(res.OriginalData); }
                    catch { /* 判定に使うだけなので読めなくても続行 */ }
                }
            }

            // ---------------- 材質 ---------------------------------------------
            var materials = new JsonArray();
            g.J["materials"] = materials;
            for (int mIdx = 0; mIdx < model.Materials.Count; mIdx++)
            {
                var m = model.Materials[mIdx];
                string matName = !string.IsNullOrEmpty(m.Name) ? m.Name : m.NameEn;
                var pbr = new JsonObject
                {
                    ["baseColorFactor"] = new JsonArray(
                        m.Diffuse.Select(c => (JsonNode)Math.Max(0.0, Math.Min(1.0, (double)c))).ToArray()),
                    ["metallicFactor"] = 0.0,
                    ["roughnessFactor"] = 1.0,
                };
                var mat = new JsonObject
                {
                    ["name"] = matName,
                    ["pbrMetallicRoughness"] = pbr,
                    ["doubleSided"] = opt.ForceDoubleSided || (m.Flags & 0x01) != 0,
                };

                int ti2 = m.Texture;
                var aclass = AlphaClass.Opaque;
                if (texMap.TryGetValue(ti2, out int gtex))
                {
                    pbr["baseColorTexture"] = new JsonObject { ["index"] = gtex };
                    aclass = texAlpha.TryGetValue(ti2, out var ac) ? ac : AlphaClass.Opaque;
                    // 【診断ログ】眉が消える / 前髪に穴が開く等の切り分け用。
                    var st = texAlphaStats.TryGetValue(ti2, out var s2) ? s2 : null;
                    if (st != null)
                        log($"MATERIAL '{matName}': texture={(ti2 < model.Textures.Count ? model.Textures[ti2] : "?")} " +
                            $"alphaClass={aclass.ToString().ToLowerInvariant()}(std) " +
                            $"below0.5={st.BelowFrac:F3} semiLow(5-128)={st.SemiLowFrac:F3} " +
                            $"semiHigh(128-250)={st.SemiHighFrac:F3} " +
                            $"meanAlphaOfBelow(0-255)={(st.MeanAlphaOfBelow.HasValue ? st.MeanAlphaOfBelow.Value.ToString("F1") : "N/A")}");
                }
                else
                {
                    // 【スフィア専用フォールバック】拡散色も環境色もほぼ黒で、
                    // ベーステクスチャが無い場合、色の出どころは MMD のスフィアマップ
                    // (extras.mmd にしか残らない) だけになる。標準ビューアでは真っ黒に
                    // なるので、スフィア画像の平均色を静的な近似として使う。
                    bool diffuseDark = Math.Max(Math.Max(m.Diffuse[0], m.Diffuse[1]), m.Diffuse[2]) < 0.05;
                    bool ambientDark = Math.Max(Math.Max(m.Ambient[0], m.Ambient[1]), m.Ambient[2]) < 0.05;
                    if (diffuseDark && ambientDark && texMap.ContainsKey(m.SphereTexture) &&
                        texAvgColor.TryGetValue(m.SphereTexture, out var avg) && avg.HasValue)
                    {
                        pbr["baseColorFactor"] = new JsonArray(
                            (double)avg.Value.R, (double)avg.Value.G, (double)avg.Value.B,
                            (double)m.Diffuse[3]);
                        log($"INFO: '{matName}' has no base texture and black diffuse/ambient; " +
                            "using sphere map average color as a static baseColorFactor fallback");
                    }
                }

                bool overlay = false, translucent = false;
                if (aclass == AlphaClass.Mask && texOrigImg.ContainsKey(ti2) && origTexMap.ContainsKey(ti2))
                {
                    var uvstats = MaterialAlpha.Sample(texOrigImg[ti2], model, mIdx);
                    if (!MaterialAlpha.IsOverlay(uvstats) && MaterialAlpha.IsTranslucent(uvstats))
                    {
                        translucent = true;
                        log($"INFO: '{matName}' is dominated by semi-transparent texels " +
                            $"(semi={uvstats.FracSemi * 100:F0}%, mean a={uvstats.Mean:F2}) -> " +
                            "extras alphaClass=blend (GLB stays MASK+prebake for viewer safety)");
                    }
                    if (MaterialAlpha.IsOverlay(uvstats))
                    {
                        overlay = true;
                        pbr["baseColorTexture"] = new JsonObject { ["index"] = origTexMap[ti2] };
                        log($"INFO: '{matName}' uses only a semi-transparent region of a shared texture " +
                            $"(mean a={uvstats.Mean:F2} max={uvstats.Max:F2}, n={uvstats.N}) -> " +
                            "overlay: alphaMode=BLEND with untouched original texture");
                    }
                }

                string mode;
                if (opt.AlphaMode != "auto") mode = opt.AlphaMode.ToUpperInvariant();
                else if (overlay) mode = "BLEND";
                else if (m.Diffuse[3] < 1.0f || aclass == AlphaClass.Blend) mode = "BLEND";
                else if (aclass == AlphaClass.Mask) mode = "MASK";
                else mode = "OPAQUE";
                mat["alphaMode"] = mode;
                if (mode == "MASK")
                {
                    // カットオフは 0.5 ではなく 0.1。実測で「顔3」(眉を含む肌テクスチャ) は
                    // 透明でない部分の平均アルファが 64.7/255 (≒0.25) しかなく、
                    // 0.5 だと眉ごと切り落としていた (実機で消えるのを確認済み)。
                    // 未使用 UV 領域はアルファがほぼ0に振り切っているので、0.1 でも
                    // 「本当に透明な部分」だけを正しく落とせる。
                    mat["alphaCutoff"] = 0.1;
                }
                if (opt.Unlit)
                    mat["extensions"] = new JsonObject { ["KHR_materials_unlit"] = new JsonObject() };
                if (opt.Extras)
                {
                    mat["extras"] = new JsonObject
                    {
                        ["mmd"] = new JsonObject
                        {
                            ["nameEn"] = m.NameEn,
                            ["ambient"] = GltfBuilder.ToArray(m.Ambient),
                            ["specular"] = GltfBuilder.ToArray(m.Specular),
                            ["specularPower"] = (double)m.SpecularPower,
                            ["flags"] = m.Flags,
                            ["edgeColor"] = GltfBuilder.ToArray(m.EdgeColor),
                            ["edgeSize"] = (double)m.EdgeSize,
                            ["sphereMode"] = m.SphereMode,
                            ["sphereTexture"] = texMap.TryGetValue(m.SphereTexture, out int sx) ? sx : -1,
                            ["toonTexture"] = texMap.TryGetValue(m.ToonTexture, out int tx) ? tx : -1,
                            ["toonShared"] = m.ToonShared,
                            ["memo"] = m.Memo,
                            // alphaClass: ベーステクスチャのアルファ分類。
                            // origTexture: prebake 前のオリジナルの glTF テクスチャ index
                            // (標準テクスチャが無加工なら -1)。
                            ["alphaClass"] = (overlay || translucent)
                                ? "blend"
                                : (texAlphaTrue.TryGetValue(ti2, out var tac) ? tac : aclass)
                                    .ToString().ToLowerInvariant(),
                            ["origTexture"] = origTexMap.TryGetValue(ti2, out int ox) ? ox : -1,
                        },
                    };
                }
                materials.Add(mat);
            }
            if (opt.Unlit) g.J["extensionsUsed"] = new JsonArray("KHR_materials_unlit");

            // ---------------- メッシュ ------------------------------------------
            var idx = model.Indices;
            var prims = new JsonArray();
            int offset2 = 0;
            for (int mi = 0; mi < model.Materials.Count; mi++)
            {
                int cnt = model.Materials[mi].IndexCount;
                int start = offset2;
                offset2 += cnt;
                if (cnt <= 0) continue;
                var flipped = new List<int>(cnt);
                for (int t = 0; t + 2 < cnt; t += 3)
                {
                    flipped.Add(idx[start + t]);
                    flipped.Add(idx[start + t + 2]);   // 巻き順反転
                    flipped.Add(idx[start + t + 1]);
                }
                int iacc = g.AddIntAccessor(flipped, GltfBuilder.UInt, "SCALAR",
                                            GltfBuilder.ElementArrayBuffer);
                var prim = new JsonObject
                {
                    ["attributes"] = attrs.DeepClone(),   // JsonNode は親を1つしか持てないので複製する
                    ["indices"] = iacc,
                    ["material"] = mi,
                    ["mode"] = 4,
                };
                if (targets.Count > 0)
                {
                    var ta = new JsonArray();
                    foreach (var t2 in targets) ta.Add(t2.DeepClone());
                    prim["targets"] = ta;
                }
                prims.Add(prim);
            }

            var mesh = new JsonObject
            {
                ["name"] = !string.IsNullOrEmpty(model.Name) ? model.Name : "mesh",
                ["primitives"] = prims,
            };
            if (targets.Count > 0)
            {
                // ★Enumerable.Repeat で JsonNode を複製すると同一インスタンスが並び、
                //   JsonNode は親を1つしか持てないので例外になる。毎回作ること。
                var w0 = new JsonArray();
                for (int i = 0; i < targets.Count; i++) w0.Add(0.0);
                mesh["weights"] = w0;
                mesh["extras"] = new JsonObject
                {
                    ["targetNames"] = new JsonArray(targetNames.Select(n => (JsonNode)n).ToArray()),
                };
            }
            g.J["meshes"] = new JsonArray(mesh);

            // ---------------- ノード / スケルトン ---------------------------------
            var bones = model.Bones;
            var nodes = new JsonArray();
            var world = new List<double[]>(nb);
            for (int i = 0; i < nb; i++)
            {
                var b = bones[i];
                int p = b.Parent;
                var wp = new double[] { b.Pos[0], b.Pos[1], b.Pos[2] };
                world.Add(wp);
                double[] lt = (p >= 0 && p < nb)
                    ? new[] { wp[0] - bones[p].Pos[0], wp[1] - bones[p].Pos[1], wp[2] - bones[p].Pos[2] }
                    : wp;
                var node = new JsonObject
                {
                    ["name"] = !string.IsNullOrEmpty(b.Name) ? b.Name : $"bone_{i}",
                    ["translation"] = new JsonArray(lt[0] * scale, lt[1] * scale, -lt[2] * scale),
                };
                if (opt.Extras)
                {
                    var ex = new JsonObject
                    {
                        ["nameEn"] = b.NameEn,
                        ["flags"] = b.Flags,
                        ["layer"] = b.Layer,
                    };
                    // Python 版は「キーが存在するときだけ載せる」ので、無いものは書かない。
                    if (b.TailBone.HasValue) ex["tail_bone"] = b.TailBone.Value;
                    if (b.TailOffset != null) ex["tail_offset"] = GltfBuilder.ToArray(b.TailOffset);
                    if (b.InheritParent.HasValue) ex["inherit_parent"] = b.InheritParent.Value;
                    if (b.InheritRatio.HasValue) ex["inherit_ratio"] = (double)b.InheritRatio.Value;
                    if (b.FixedAxis != null) ex["fixed_axis"] = GltfBuilder.ToArray(b.FixedAxis);
                    if (b.LocalX != null) ex["local_x"] = GltfBuilder.ToArray(b.LocalX);
                    if (b.LocalZ != null) ex["local_z"] = GltfBuilder.ToArray(b.LocalZ);
                    if (b.ExternalKey.HasValue) ex["external_key"] = b.ExternalKey.Value;
                    if (b.Ik != null) ex["ik"] = JsonSerializer.SerializeToNode(b.Ik, MmdJson.Options);
                    node["extras"] = new JsonObject { ["mmd"] = ex };
                }
                nodes.Add(node);
            }
            var rootBones = new JsonArray();
            for (int i = 0; i < nb; i++)
            {
                int p = bones[i].Parent;
                if (p >= 0 && p < nb)
                {
                    if (nodes[p]["children"] is not JsonArray ch)
                    { ch = new JsonArray(); nodes[p]["children"] = ch; }
                    ch.Add(i);
                }
                else rootBones.Add(i);
            }

            var ibm = new List<double>(nb * 16);
            foreach (var wp in world)
            {
                double wx = wp[0] * scale, wy = wp[1] * scale, wz = -wp[2] * scale;
                ibm.AddRange(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -wx, -wy, -wz, 1 });
            }
            int ibmAcc = g.AddAccessor(ibm, "MAT4");

            // スキンメッシュのノードはシーンのルートに置く (変換は無視されるため)
            int meshNode = nodes.Count;
            nodes.Add(new JsonObject
            {
                ["name"] = !string.IsNullOrEmpty(model.Name) ? model.Name : "model",
                ["mesh"] = 0,
                ["skin"] = 0,
            });
            int root = nodes.Count;
            nodes.Add(new JsonObject
            {
                ["name"] = (!string.IsNullOrEmpty(model.Name) ? model.Name : "root") + "_skeleton",
                ["children"] = rootBones,
            });
            g.J["nodes"] = nodes;
            g.J["skins"] = new JsonArray(new JsonObject
            {
                ["joints"] = new JsonArray(Enumerable.Range(0, nb).Select(i => (JsonNode)i).ToArray()),
                ["inverseBindMatrices"] = ibmAcc,
                ["skeleton"] = root,
            });
            g.J["scenes"] = new JsonArray(new JsonObject
            {
                ["nodes"] = new JsonArray(root, meshNode),
            });
            g.J["scene"] = 0;

            // ---------------- extras.mmd ------------------------------------------
            if (opt.Extras)
            {
                var morphExtras = new JsonArray();
                for (int i = 0; i < model.Morphs.Count; i++)
                {
                    var mo = model.Morphs[i];
                    var me = new JsonObject
                    {
                        ["name"] = mo.Name,
                        ["nameEn"] = mo.NameEn,
                        ["panel"] = mo.Panel,
                        ["type"] = mo.Type,
                        ["target"] = morphToTarget.TryGetValue(i, out int mt) ? mt : null,
                    };
                    // 頂点 / UV モーフは glTF のターゲットになるので offsets は省く。
                    if (!IsTargetMorphType(mo.Type))
                        me["offsets"] = JsonSerializer.SerializeToNode(mo.Offsets, MmdJson.Options);
                    morphExtras.Add(me);
                }

                var mmd = new JsonObject
                {
                    ["coordinateNote"] = "values under extras.mmd are raw PMX values in " +
                                         "MMD left-handed space (x,y,z), unscaled " +
                                         "(see unitScale); glTF space is (x,y,-z), " +
                                         "quat (-x,-y,z,w)",
                    ["unitScale"] = scale,
                    ["format"] = "pmx",
                    ["version"] = (double)model.Version,
                    ["name"] = model.Name,
                    ["nameEn"] = model.NameEn,
                    ["comment"] = model.Comment,
                    ["commentEn"] = model.CommentEn,
                    ["morphs"] = morphExtras,
                    ["displayFrames"] = JsonSerializer.SerializeToNode(model.DisplayFrames, MmdJson.Options),
                    ["rigidBodies"] = JsonSerializer.SerializeToNode(model.RigidBodies, MmdJson.Options),
                    ["joints"] = JsonSerializer.SerializeToNode(model.Joints, MmdJson.Options),
                };
                g.J["extras"] = new JsonObject { ["mmd"] = mmd };

                // 変換済みの物理ビュー (後工程向け・ボーンローカル) を併載する。
                // raw の rigidBodies/joints はそのまま残す。
                if (model.RigidBodies.Count > 0)
                {
                    var bwm = PhysicsGltf.ComputeBoneWorldMatrices(g.J);
                    mmd["physicsGltf"] = PhysicsGltf.Build(scale, model.RigidBodies, model.Joints, bwm);
                }
            }

            // ---------------- アニメーション ----------------------------------------
            if (!string.IsNullOrEmpty(opt.VmdPath))
                BuildAnimation(g, model, opt, scale, targets.Count, targetMorphs, meshNode, log);

            g.WriteGlb(outPath);
            log($"wrote {outPath} ({new FileInfo(outPath).Length / 1e6:F1} MB)");
            return outPath;
        }

        private static void AddCPos(List<double> dst, float[] v, double scale)
        {
            dst.Add(v[0] * scale); dst.Add(v[1] * scale); dst.Add(-v[2] * scale);
        }

        // ---------------------------------------------------------------------
        private static void BuildAnimation(GltfBuilder g, PmxModel model, ConvertOptions opt,
                                           double scale, int targetCount,
                                           List<MorphTargets.Entry> targetMorphs,
                                           int meshNode, Action<string> log)
        {
            var vmd = VmdParser.Parse(opt.VmdPath);
            log($"VMD '{vmd.ModelName}'  {vmd.Bones.Count} bone tracks / " +
                $"{vmd.Morphs.Count} morph tracks / last frame {vmd.MaxFrame}");

            var anim = new JsonObject
            {
                ["name"] = opt.AnimName ?? Path.GetFileNameWithoutExtension(opt.VmdPath),
                ["samplers"] = new JsonArray(),
                ["channels"] = new JsonArray(),
            };
            var samplers = (JsonArray)anim["samplers"];
            var channels = (JsonArray)anim["channels"];

            if (vmd.Bones.Count > 0)
            {
                if (opt.DisableIk is { Count: > 0 })
                    log("  IK disabled for bones matching: " + string.Join(", ", opt.DisableIk));
                if (vmd.IkFrames.Count > 0)
                    log($"  VMD contains {vmd.IkFrames.Count} IK on/off key frame(s)" +
                        (opt.UseVmdIkFrames ? "" : " (ignored)"));

                var baked = AnimationBaker.Bake(model, vmd, opt.SolveIk, opt.Step,
                    opt.DisableIk, opt.UseVmdIkFrames,
                    (i, n) => log($"  baking frame {i}/{n}"));
                if (baked.UnmatchedVmdBones.Count > 0)
                    log($"  {baked.UnmatchedVmdBones.Count} VMD bone tracks had no matching PMX bone");

                int tAcc = g.AddAccessor(baked.Times, "SCALAR", minmax: true);

                foreach (int bi in baked.Bones.Keys.OrderBy(k => k))
                {
                    var data = baked.Bones[bi];
                    if (data.Rotations != null)
                    {
                        var flat = new List<double>(data.Rotations.Length * 4);
                        Quatd prev = default;
                        bool hasPrev = false;
                        foreach (var q0 in data.Rotations)
                        {
                            var q = MmdConvert.MmdToGltfRot(q0);
                            if (hasPrev && q.X * prev.X + q.Y * prev.Y + q.Z * prev.Z + q.W * prev.W < 0)
                                q = new Quatd(-q.X, -q.Y, -q.Z, -q.W);
                            flat.Add(q.X); flat.Add(q.Y); flat.Add(q.Z); flat.Add(q.W);
                            prev = q; hasPrev = true;
                        }
                        AddChannel(samplers, channels, tAcc,
                                   g.AddAccessor(flat, "VEC4"), bi, "rotation");
                    }
                    if (data.Translations != null)
                    {
                        var flat = new List<double>(data.Translations.Length * 3);
                        foreach (var t in data.Translations)
                        {
                            var p = MmdConvert.MmdToGltfPos(t, scale);
                            flat.Add(p.X); flat.Add(p.Y); flat.Add(p.Z);
                        }
                        AddChannel(samplers, channels, tAcc,
                                   g.AddAccessor(flat, "VEC3"), bi, "translation");
                    }
                }
                log($"  bone animation: {channels.Count} channels, {baked.Times.Length} key frames");
            }

            // モーフ (weights) のアニメーション。キーの間は線形。
            if (vmd.Morphs.Count > 0 && targetCount > 0)
            {
                var tmap = new Dictionary<int, List<float[]>>();
                var targetKeys = new List<byte[]>();
                for (int i = 0; i < targetMorphs.Count; i++)
                {
                    string name = model.Morphs[targetMorphs[i].MorphIndex].Name;
                    var key = MmdEncoding.TruncatedKey(name, 15);
                    targetKeys.Add(key);
                    foreach (var kv in vmd.Morphs)
                        if (MmdEncoding.ByteArrayComparer.Instance.Equals(
                                MmdEncoding.TruncatedKey(kv.Key, 15), key))
                        { tmap[i] = kv.Value; break; }
                }
                int skipped = vmd.Morphs.Keys.Count(vname =>
                    !targetKeys.Any(k => MmdEncoding.ByteArrayComparer.Instance.Equals(
                        k, MmdEncoding.TruncatedKey(vname, 15))));
                if (skipped > 0)
                    log($"  {skipped} VMD morph tracks target non-vertex/UV morphs (kept in extras only)");

                if (tmap.Count > 0)
                {
                    var frameSet = new SortedSet<float>();
                    foreach (var keys in tmap.Values) foreach (var k in keys) frameSet.Add(k[0]);
                    var frames = frameSet.ToList();

                    var wTimes = frames.Select(f => f / AnimationBaker.Fps).ToList();
                    var flat = new List<double>(frames.Count * targetCount);
                    foreach (float f in frames)
                    {
                        for (int ti = 0; ti < targetCount; ti++)
                        {
                            if (!tmap.TryGetValue(ti, out var keys)) { flat.Add(0.0); continue; }
                            if (f <= keys[0][0]) { flat.Add(keys[0][1]); }
                            else if (f >= keys[^1][0]) { flat.Add(keys[^1][1]); }
                            else
                            {
                                for (int k = 0; k < keys.Count - 1; k++)
                                {
                                    double f0 = keys[k][0], w0 = keys[k][1];
                                    double f1 = keys[k + 1][0], w1 = keys[k + 1][1];
                                    if (f0 <= f && f <= f1)
                                    {
                                        double r = f1 > f0 ? (f - f0) / (f1 - f0) : 0;
                                        flat.Add(w0 + (w1 - w0) * r);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    int inAcc = g.AddAccessor(wTimes, "SCALAR", minmax: true);
                    int outAcc = g.AddAccessor(flat, "SCALAR");
                    samplers.Add(new JsonObject
                    {
                        ["input"] = inAcc, ["output"] = outAcc, ["interpolation"] = "LINEAR",
                    });
                    channels.Add(new JsonObject
                    {
                        ["sampler"] = samplers.Count - 1,
                        ["target"] = new JsonObject { ["node"] = meshNode, ["path"] = "weights" },
                    });
                    log($"  morph animation: {tmap.Count} tracks, {frames.Count} key frames");
                }
            }

            if (channels.Count > 0) g.J["animations"] = new JsonArray(anim);
        }

        private static void AddChannel(JsonArray samplers, JsonArray channels,
                                       int input, int output, int node, string path)
        {
            samplers.Add(new JsonObject
            {
                ["input"] = input, ["output"] = output, ["interpolation"] = "LINEAR",
            });
            channels.Add(new JsonObject
            {
                ["sampler"] = samplers.Count - 1,
                ["target"] = new JsonObject { ["node"] = node, ["path"] = path },
            });
        }
    }
}
