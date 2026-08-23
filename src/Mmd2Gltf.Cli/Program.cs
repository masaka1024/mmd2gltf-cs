// ===========================================================================
// mmd2gltf CLI。PMX→glTF 変換の入口。
// convert が本体で、残りは Python 版と突合するための検証サブコマンド。
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

        private static readonly string Ver =
            typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

        private static void Usage() => Console.WriteLine($@"mmd2gltf {Ver} — MMD (PMX/VMD) を glTF 2.0 (.glb) へ変換する

  mmd2gltf convert <model.pmx> [--vmd <motion.vmd>] [-o out.glb] [オプション]
                                   PMX (+VMD) を glTF 2.0 (.glb) へ変換する
                                   (--bake-physics で揺れ物の物理も同時に焼ける)

  -- 以下は Python 版との突合用の検証コマンド --
  mmd2gltf texdump <dir|file>...   テクスチャの分類結果を JSON で出す
  mmd2gltf pmxdump <model.pmx> [-o out.json]   PMX のパース結果を JSON で出す
  mmd2gltf vmddump <motion.vmd> [-o out.json]  VMD のパース結果を JSON で出す
  mmd2gltf animdump <model.pmx> --vmd <motion.vmd> [-o out.json]
                                   FK/IK ベイク結果を JSON で出す

各コマンドの詳細は `mmd2gltf <コマンド> --help`。");
    }
}
