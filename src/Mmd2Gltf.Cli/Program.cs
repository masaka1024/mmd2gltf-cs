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
                    case "texdump": return TexDump.Run(args[1..]);
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

  mmd2gltf texdump <dir|file>...   テクスチャの分類結果を JSON で出す (Python 版との突合用)

PMX→glTF 変換そのものはまだ移植中です。現時点では Python 版
(mmd2gltf-gui) で GLB を作り、physbake で物理をベイクしてください。");
    }
}
