// ===========================================================================
// mmd2gltf CLI。ステージ3で PMX→glTF 変換の入口になる。
// 現在は移植中のため、Python 版と突合するための検証サブコマンドだけがある。
// ===========================================================================
using System;

namespace Mmd2Gltf.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) { Usage(); return 2; }
                switch (args[0])
                {
                    case "convert": return ConvertCommand.Run(args[1..]);
                    case "texdump": return TexDump.Run(args[1..]);
                    case "pmxdump": return ModelDump.RunPmx(args[1..]);
                    case "vmddump": return ModelDump.RunVmd(args[1..]);
                    case "animdump": return ModelDump.RunAnim(args[1..]);
                    case "enginedump": return EngineDump.Run(args[1..]);
                    case "-h": case "--help": Usage(); return 0;
                    default:
                        Console.Error.WriteLine($"不明なサブコマンド: {args[0]}");
                        Usage();
                        return 2;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("エラー: " + e.Message);
                return 1;
            }
        }

        private static void Usage() => Console.WriteLine(@"mmd2gltf (C#版・移植中)

  mmd2gltf convert <model.pmx> [--vmd <motion.vmd>] [-o out.glb] [オプション]
                                   PMX (+VMD) を glTF 2.0 (.glb) へ変換する
                                   (揺れ物の物理は physbake が後段で焼く)

  -- 以下は Python 版との突合用の検証コマンド --
  mmd2gltf texdump <dir|file>...   テクスチャの分類結果を JSON で出す
  mmd2gltf pmxdump <model.pmx> [-o out.json]   PMX のパース結果を JSON で出す
  mmd2gltf vmddump <motion.vmd> [-o out.json]  VMD のパース結果を JSON で出す
  mmd2gltf animdump <model.pmx> --vmd <motion.vmd> [-o out.json]
                                   FK/IK ベイク結果を JSON で出す

各コマンドの詳細は `mmd2gltf <コマンド> --help`。");
    }
}
