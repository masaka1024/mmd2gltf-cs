// ===========================================================================
// physbake — mmd2gltf が出力した GLB のアニメーションに、自作 Bullet 互換
// エンジンで計算した揺れ物 (髪/スカート/リボン等) の動きをベイクする。
//
//   physbake <input.glb> [-o output.glb] [options]
//
// 入力 GLB に必要なもの: extras.mmd (剛体/Joint/unitScale)、skins[0]、
// VMD から焼かれた animation。つまり mmd2gltf --vmd ... の出力そのもの。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using BulletPhysics;
using BulletPhysics.Pmx;

namespace Mmd2Gltf
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception e)
            {
                Console.Error.WriteLine("エラー: " + e.Message);
                return 1;
            }
        }

        private static void Usage()
        {
            Console.WriteLine(@"physbake — 自作物理エンジンの結果を glTF アニメーションへベイクする

  physbake <input.glb> [-o <output.glb>] [オプション]

オプション:
  -o, --output <path>   出力 GLB (既定: <入力>_phys.glb)
      --animation <N>   対象アニメーション index (既定: 0)
      --warmup <秒>     フレーム0姿勢での空回し秒数 (既定: 2.0)
      --sim-hz <Hz>     内部固定刻み (既定: エンジン既定 30)
      --substeps <N>    1ステップあたりのサブステップ数 (既定: エンジン既定 2)
      --iterations <N>  ソルバ反復数 (既定: エンジン既定 10)
      --gravity <G>     重力の大きさ・MMDスケール (既定: エンジン既定 98)
      --no-bone-merge   PMX mode2 (物理演算+ボーン位置合わせ) を無効にする
      --align-bones     全物理ボーンに位置合わせ補正を適用する (既定 OFF)
      --dry-run         GLB を書かずに計算と統計だけ出す
  -q, --quiet           進捗ログを出さない
  -h, --help            このヘルプ");
        }

        private static int Run(string[] args)
        {
            string input = null, output = null;
            int animIndex = 0;
            bool quiet = false, dryRun = false;
            var opt = new BakeOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next(string name) => ++i < args.Length ? args[i]
                    : throw new ArgumentException($"{name} には値が必要です。");
                float NextF(string name) => float.Parse(Next(name), CultureInfo.InvariantCulture);
                int NextI(string name) => int.Parse(Next(name), CultureInfo.InvariantCulture);

                switch (a)
                {
                    case "-h": case "--help": Usage(); return 0;
                    case "-o": case "--output": output = Next(a); break;
                    case "--animation": animIndex = NextI(a); break;
                    case "--warmup": opt.WarmupSeconds = NextF(a); break;
                    case "--sim-hz": opt.SimHz = NextF(a); break;
                    case "--substeps": opt.SubSteps = NextI(a); break;
                    case "--iterations": opt.SolverIterations = NextI(a); break;
                    case "--gravity": opt.Gravity = NextF(a); break;
                    case "--no-bone-merge": opt.BoneMergeMode = false; break;
                    case "--align-bones": opt.AlignBonePositions = true; break;
                    case "--dry-run": dryRun = true; break;
                    case "-q": case "--quiet": quiet = true; break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException($"不明なオプション: {a}");
                        if (input != null) throw new ArgumentException("入力ファイルが複数指定されています。");
                        input = a;
                        break;
                }
            }

            if (input == null) { Usage(); return 2; }
            if (!File.Exists(input)) throw new FileNotFoundException($"入力が見つかりません: {input}");
            output ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)),
                                    Path.GetFileNameWithoutExtension(input) + "_phys.glb");

            Action<string> log = quiet ? null : Console.WriteLine;

            // ---------------- 読み込み ----------------
            log?.Invoke($"入力 {input}");
            var glb = Glb.Load(input);
            var skel = GltfSkeleton.Load(glb);
            var model = GlbPhysicsReader.LoadBytes(File.ReadAllBytes(input), out float unitScale,
                                                  out List<string> warnings);
            foreach (var w in warnings) Console.Error.WriteLine("  警告(GLB): " + w);

            if (model.RigidBodies.Count == 0)
                throw new InvalidOperationException(
                    "extras.mmd に剛体がありません。mmd2gltf を --no-extras 無しで実行した GLB が必要です。");
            if (model.BoneNames.Count != skel.BoneCount)
                throw new InvalidOperationException(
                    $"ボーン数が一致しません (GlbPhysicsReader {model.BoneNames.Count} / skins {skel.BoneCount})。");

            var anim = GltfAnimation.Load(glb, animIndex);
            int frames = anim.Times.Length;
            log?.Invoke($"  unitScale {unitScale}  ボーン {skel.BoneCount}  " +
                        $"アニメ '{anim.Name}' {frames}フレーム ({anim.Times[0]:F3}..{anim.Times[frames - 1]:F3}s)");

            // ---------------- 検算: 座標規約とボーン階層の対応 ----------------
            // バインド姿勢 (rest ローカル) を FK 合成した world は、GlbPhysicsReader が
            // 別経路で読んだ BonePositions と一致するはず。ここがズレていたら
            // 変換規約かボーン対応が間違っており、以降の数字は全部無意味になる。
            int nb = skel.BoneCount;
            var order = skel.TopologicalOrder();
            {
                var restWorld = new RigidTransform[nb];
                float worst = 0f; int worstBone = 0;
                foreach (int b in order)
                {
                    var local = MmdConvert.GltfToMmd(skel.RestPos[b], skel.RestRot[b], unitScale);
                    int p = skel.ParentBone[b];
                    restWorld[b] = p >= 0 ? restWorld[p] * local : local;
                    float e = (restWorld[b].Origin - model.BonePositions[b]).Length;
                    if (e > worst) { worst = e; worstBone = b; }
                }
                // PMX 座標は概ね 0..20 の範囲。float 精度の累積で 1e-3 程度までは正常。
                if (worst > 1e-2f)
                    throw new InvalidOperationException(
                        $"バインド姿勢の検算に失敗しました: ボーン '{skel.Names[worstBone]}' で {worst:F4} のズレ。" +
                        "座標規約かボーン階層の対応が壊れています。");
                log?.Invoke($"  検算 OK: バインドFK vs エンジンのボーン位置 最大誤差 {worst:E2}");
            }

            // ---------------- FK: glTF アニメ -> raw PMX ボーンworld ----------------
            var fkWorld = new RigidTransform?[frames][];
            var fkWorldRaw = new RigidTransform[frames][];
            for (int f = 0; f < frames; f++)
            {
                var row = new RigidTransform[nb];
                var rowOpt = new RigidTransform?[nb];
                float t = anim.Times[f];
                foreach (int b in order)
                {
                    anim.SampleLocal(skel.NodeOfBone[b], t, skel.RestPos[b], skel.RestRot[b],
                                     out var lpos, out var lrot);
                    var local = MmdConvert.GltfToMmd(lpos, lrot, unitScale);
                    int p = skel.ParentBone[b];
                    row[b] = p >= 0 ? row[p] * local : local;
                    rowOpt[b] = row[b];
                }
                fkWorld[f] = rowOpt;
                fkWorldRaw[f] = row;
            }

            // ---------------- 物理 ----------------
            log?.Invoke("物理ベイク:");
            var bake = PhysicsBaker.Run(model, fkWorld, anim.Times, opt, log);
            if (bake.PhysicsBones.Length == 0)
            {
                Console.Error.WriteLine("動かせる物理ボーンがありません (全剛体が追従モード)。何も書き換えません。");
                return 3;
            }
            log?.Invoke($"  {bake.StepCount} ステップ / {bake.Seconds:F2}s " +
                        $"({bake.Seconds * 1000.0 / Math.Max(1, bake.StepCount):F2} ms/step)");
            if (bake.NanBone != null)
                Console.Error.WriteLine($"  警告: 物理が発散しました (最初の検出: ボーン '{bake.NanBone}')。" +
                    "ばね定数が大きすぎる/質量が小さすぎるモデルの可能性があります。--sim-hz を上げてください。");

            // ---------------- 物理結果 -> glTF ローカルキー ----------------
            var isPhysics = new bool[nb];
            foreach (int b in bake.PhysicsBones) isPhysics[b] = true;

            // 上書き前の FK 位置を診断用に控える (物理がどれだけ動かしたかの比較基準)。
            var fkOnlyPos = new Dictionary<int, Vec3[]>();
            foreach (int b in bake.PhysicsBones)
            {
                var arr = new Vec3[frames];
                for (int f = 0; f < frames; f++) arr[f] = fkWorldRaw[f][b].Origin;
                fkOnlyPos[b] = arr;
            }

            // 最終 world = FK を物理結果で上書きしたもの (親が物理ボーンでも正しく相対を取れる)。
            for (int f = 0; f < frames; f++)
                foreach (int b in bake.PhysicsBones)
                    fkWorldRaw[f][b] = bake.BoneWorld[f][b];

            var rotKeys = new Dictionary<int, float[]>();
            var posKeys = new Dictionary<int, float[]>();
            // 診断値。
            //   stretch = |ローカル並進| - |restローカル並進| (剛体リンクは伸びないはず → 0 に近いこと)
            //   move    = 物理結果と FK の world 位置の差 (物理がどれだけ動かしたか)
            float maxStretch = 0f, maxMove = 0f;
            string maxStretchBone = null, maxMoveBone = null;
            var allStretch = new List<float>(bake.PhysicsBones.Length * frames);
            var allMove = new List<float>(bake.PhysicsBones.Length * frames);

            foreach (int b in bake.PhysicsBones)
            {
                var rot = new float[frames * 4];
                var pos = new float[frames * 3];
                int p = skel.ParentBone[b];
                Quat prev = default;
                bool hasPrev = false;
                float stretchB = 0f, moveB = 0f;
                float restLen = skel.RestPos[b].Length;

                for (int f = 0; f < frames; f++)
                {
                    var wparent = p >= 0 ? fkWorldRaw[f][p] : RigidTransform.Identity;
                    var local = wparent.Inverse() * fkWorldRaw[f][b];
                    MmdConvert.MmdToGltf(local, unitScale, out var gpos, out var grot);

                    grot = grot.Normalized;
                    if (hasPrev &&
                        grot.x * prev.x + grot.y * prev.y + grot.z * prev.z + grot.w * prev.w < 0f)
                        grot = new Quat(-grot.x, -grot.y, -grot.z, -grot.w);   // 連続性 (LINEAR 補間の最短路)
                    prev = grot; hasPrev = true;

                    rot[f * 4] = grot.x; rot[f * 4 + 1] = grot.y;
                    rot[f * 4 + 2] = grot.z; rot[f * 4 + 3] = grot.w;
                    pos[f * 3] = gpos.x; pos[f * 3 + 1] = gpos.y; pos[f * 3 + 2] = gpos.z;

                    float s = Math.Abs(gpos.Length - restLen);
                    if (s > stretchB) stretchB = s;
                    allStretch.Add(s);
                    float m = (bake.BoneWorld[f][b].Origin - fkOnlyPos[b][f]).Length * unitScale;
                    if (m > moveB) moveB = m;
                    allMove.Add(m);
                }
                rotKeys[b] = rot;
                posKeys[b] = pos;
                if (stretchB > maxStretch) { maxStretch = stretchB; maxStretchBone = skel.Names[b]; }
                if (moveB > maxMove) { maxMove = moveB; maxMoveBone = skel.Names[b]; }
            }

            // 最大値は一瞬のスパイクを拾うので中央値/p90 も併記する
            // (この種の指標は最大値だけ見ると判断を誤る、というのが過去の教訓)。
            static (float med, float p90) Pct(List<float> v)
            {
                if (v.Count == 0) return (0f, 0f);
                v.Sort();
                return (v[v.Count / 2], v[Math.Min(v.Count - 1, (int)(v.Count * 0.9))]);
            }

            void LogStats()
            {
                var (mMed, mP90) = Pct(allMove);
                var (sMed, sP90) = Pct(allStretch);
                float u = 1f / unitScale;   // 過去の計測記録は全て MMD 単位なので合わせる
                log?.Invoke($"  物理による移動 中央 {mMed * u:F3} / p90 {mP90 * u:F3} / 最大 {maxMove * u:F3} MMD単位 (ボーン '{maxMoveBone}')");
                log?.Invoke($"  リンクの伸び   中央 {sMed * u:F3} / p90 {sP90 * u:F3} / 最大 {maxStretch * u:F3} MMD単位 (ボーン '{maxStretchBone}')");
                // 「伸び」はジョイントの移動制限が広いモデル(例: Tda式スカートは±0.5)では
                // 正当に大きくなる。制限を超える値だけが異常なので、しきい値は緩く取る。
                if (sP90 * u > 1.0f)
                    Console.Error.WriteLine("  警告: リンクの伸びが大きすぎます。ジョイントの移動制限を超えている可能性があります。");
            }

            if (dryRun)
            {
                log?.Invoke($"  (dry-run) 物理ボーン {bake.PhysicsBones.Length} 本");
                LogStats();
                return 0;
            }

            // ---------------- glTF アニメーションの書き換え ----------------
            int timeAcc = glb.AddFloatAccessor(anim.Times, "SCALAR", withMinMax: true);
            var animObj = (JsonObject)glb.RequireArr("animations")[animIndex];
            var samplers = animObj["samplers"] as JsonArray ?? new JsonArray();
            var channels = animObj["channels"] as JsonArray ?? new JsonArray();
            animObj["samplers"] = samplers;
            animObj["channels"] = channels;

            // 物理ボーンの既存 rotation/translation チャンネルを外す (FK の値を捨てる)。
            var physNodes = new HashSet<int>();
            foreach (int b in bake.PhysicsBones) physNodes.Add(skel.NodeOfBone[b]);
            int removed = 0;
            for (int i = channels.Count - 1; i >= 0; i--)
            {
                var target = channels[i]["target"] as JsonObject;
                var nodeNode = target?["node"];
                if (nodeNode == null) continue;
                string path = target["path"]?.GetValue<string>();
                if (physNodes.Contains(nodeNode.GetValue<int>()) &&
                    (path == "rotation" || path == "translation"))
                {
                    channels.RemoveAt(i);
                    removed++;
                }
            }

            void AddChannel(int node, string path, int outputAcc)
            {
                samplers.Add(new JsonObject
                {
                    ["input"] = timeAcc,
                    ["output"] = outputAcc,
                    ["interpolation"] = "LINEAR",
                });
                channels.Add(new JsonObject
                {
                    ["sampler"] = samplers.Count - 1,
                    ["target"] = new JsonObject { ["node"] = node, ["path"] = path },
                });
            }

            foreach (int b in bake.PhysicsBones)
            {
                int node = skel.NodeOfBone[b];
                AddChannel(node, "rotation", glb.AddFloatAccessor(rotKeys[b], "VEC4"));
                AddChannel(node, "translation", glb.AddFloatAccessor(posKeys[b], "VEC3"));
            }

            // 由来を残す (再ベイク時や不具合報告のため)。
            var extras = animObj["extras"] as JsonObject ?? new JsonObject();
            extras["physicsBake"] = new JsonObject
            {
                ["engine"] = "mmd2gltf-cs-physics",
                ["bones"] = bake.PhysicsBones.Length,
                ["simHz"] = opt.SimHz > 0 ? opt.SimHz : 30f,
                ["subSteps"] = opt.SubSteps > 0 ? opt.SubSteps : 2,
                ["warmupSeconds"] = opt.WarmupSeconds,
                ["boneMergeMode"] = opt.BoneMergeMode,
                ["alignBonePositions"] = opt.AlignBonePositions,
            };
            animObj["extras"] = extras;

            glb.Save(output);
            log?.Invoke($"  FKチャンネル {removed} 本を差し替え、物理チャンネル {bake.PhysicsBones.Length * 2} 本を書き込みました");
            LogStats();
            log?.Invoke($"出力 {output}");
            return 0;
        }
    }
}
