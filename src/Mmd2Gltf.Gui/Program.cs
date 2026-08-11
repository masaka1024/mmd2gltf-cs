using System;
using Avalonia;

namespace Mmd2Gltf.Gui
{
    public static class Program
    {
        // Avalonia の初期化より前に UI を触らないこと (STAThread は Windows 向け)。
        [STAThread]
        public static void Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // フォントは埋め込まずプラットフォーム既定を使う。
        // Avalonia 同梱の Inter は日本語グリフを持たないため、埋め込むと
        // 日本語 UI が豆腐になる。Windows / macOS とも既定フォントで日本語が出る。
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
