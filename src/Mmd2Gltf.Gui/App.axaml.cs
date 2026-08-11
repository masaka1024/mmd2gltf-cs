using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Mmd2Gltf.Gui
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            // 初期言語は OS のロケールから決める (日本語環境なら日本語)。
            var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            Strings.Lang = name == "ja" ? "ja" : "en";

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
