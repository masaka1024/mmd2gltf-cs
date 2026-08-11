// ===========================================================================
// convert — PMX (+VMD) を glTF 2.0 (.glb) へ変換する。
// Python 版 CLI から、削除した PBD 物理まわりのオプション (約35個) を除いたもの。
// 揺れ物の物理は physbake が後段で焼く。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mmd2Gltf.Conversion;

namespace Mmd2Gltf.Cli
{
    public static class ConvertCommand
    {
        public static void Usage() => Console.WriteLine(@"mmd2gltf convert <model.pmx> [オプション]

  -o, --output <path>       出力 .glb (既定: <PMX名>.glb)
      --vmd <motion.vmd>    焼き付けるモーション
      --no-ik               モーション焼き付け時に IK を解かない
      --disable-ik <NAME>   名前に NAME を含む IK ボーンを無効化 (複数可)
      --ignore-vmd-ik       VMD の IK on/off キーを無視する
      --step <N>            N フレームおきに焼く (既定 1 = 30fps)
      --unlit               KHR_materials_unlit を付ける
      --no-extras           extras.mmd (IK/物理/モーフ情報) を省く
      --anim-name <NAME>    アニメーション名
      --alpha-mode <MODE>   auto|opaque|mask|blend (既定 auto)
      --no-custom-attrs     MMD 固有の頂点属性 (_SDEF_* 等) を省く
      --force-double-sided  全材質を両面描画にする
      --morph-mode <MODE>   sparse|dense|none (既定 sparse)
      --scale <F>           MMD 単位 → glTF 単位 (既定 0.08)
  -q, --quiet               ログを出さない");

        public static int Run(string[] args)
        {
            string pmx = null, output = null;
            bool quiet = false;
            var opt = new ConvertOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => ++i < args.Length ? args[i]
                    : throw new ArgumentException($"{a} には値が必要です。");
                switch (a)
                {
                    case "-h": case "--help": Usage(); return 0;
                    case "-o": case "--output": output = Next(); break;
                    case "--vmd": opt.VmdPath = Next(); break;
                    case "--no-ik": opt.SolveIk = false; break;
                    case "--disable-ik": (opt.DisableIk ??= new List<string>()).Add(Next()); break;
                    case "--ignore-vmd-ik": opt.UseVmdIkFrames = false; break;
                    case "--step": opt.Step = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--unlit": opt.Unlit = true; break;
                    case "--no-extras": opt.Extras = false; break;
                    case "--anim-name": opt.AnimName = Next(); break;
                    case "--alpha-mode": opt.AlphaMode = Next(); break;
                    case "--no-custom-attrs": opt.CustomAttrs = false; break;
                    case "--force-double-sided": opt.ForceDoubleSided = true; break;
                    case "--morph-mode": opt.MorphMode = Next(); break;
                    case "--scale": opt.Scale = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "-q": case "--quiet": quiet = true; break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException($"不明なオプション: {a}");
                        if (pmx != null) throw new ArgumentException("入力 PMX が複数指定されています。");
                        pmx = a;
                        break;
                }
            }
            if (pmx == null) { Usage(); return 2; }
            if (!File.Exists(pmx)) throw new FileNotFoundException($"入力が見つかりません: {pmx}");
            output ??= Path.ChangeExtension(Path.GetFullPath(pmx), ".glb");

            Action<string> log = quiet ? _ => { } : s => Console.Error.WriteLine("[mmd2gltf] " + s);
            MmdConverter.Convert(pmx, output, opt, log);
            return 0;
        }
    }
}
