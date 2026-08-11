// ===========================================================================
// メイン画面。tkinter 版 gui.py と同じ構成 (ファイル / オプション / 詳細設定 / ログ)。
// Avalonia なので Windows でも macOS でも同じコードで動く。
//
// 変換はバックグラウンドスレッドで走らせ、ログは Dispatcher 経由で UI へ送る。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mmd2Gltf.Conversion;

namespace Mmd2Gltf.Gui
{
    public partial class MainWindow : Window
    {
        private bool _outEdited;
        private bool _running;

        public MainWindow()
        {
            // ★AvaloniaXamlLoader.Load ではなく生成された InitializeComponent を呼ぶこと。
            //   前者だと x:Name のフィールドが埋まらず、参照した瞬間に NullReference になる。
            InitializeComponent();

            LangBox.SelectedIndex = Strings.Lang == "ja" ? 0 : 1;
            LangBox.SelectionChanged += (_, _) =>
            {
                Strings.Lang = LangBox.SelectedIndex == 0 ? "ja" : "en";
                ApplyLanguage();
            };

            PmxBrowse.Click += async (_, _) => await PickAsync(PmxBox, "PMX", "*.pmx");
            VmdBrowse.Click += async (_, _) => await PickAsync(VmdBox, "VMD", "*.vmd");
            OutBrowse.Click += async (_, _) => await PickSaveAsync();
            PmxBox.TextChanged += (_, _) => UpdateDefaultOutput();
            OutBox.TextChanged += (_, _) => { if (OutBox.IsFocused) _outEdited = true; };
            RunButton.Click += async (_, _) => await RunAsync();
            PhysEnableCb.IsCheckedChanged += (_, _) => UpdatePhysEnabled();

            SetupDragDrop();
            ApplyLanguage();
            UpdatePhysEnabled();
        }

        // ------------------------------------------------------------------
        private void ApplyLanguage()
        {
            Title = Strings.T("window_title");
            FilesGroup.Header = Strings.T("frame_files");
            PmxLabel.Text = Strings.T("label_pmx");
            VmdLabel.Text = Strings.T("label_vmd");
            OutLabel.Text = Strings.T("label_out");
            PmxBrowse.Content = VmdBrowse.Content = OutBrowse.Content = Strings.T("browse");
            DropHint.Text = Strings.T("drop_hint");

            OptionsHeader.Text = Strings.T("frame_options");
            UnlitCb.Content = Strings.T("opt_unlit");
            DoubleSidedCb.Content = Strings.T("opt_doubleside");
            MorphLabel.Text = Strings.T("opt_morph_mode");
            MorphHint.Text = Strings.T("opt_morph_hint");
            AlphaLabel.Text = Strings.T("opt_alpha_mode");
            ScaleLabel.Text = Strings.T("opt_scale");

            AdvExpander.Header = Strings.T("adv_toggle");
            TabGeneral.Header = Strings.T("tab_general");
            TabPhysics.Header = Strings.T("tab_physics");
            NoIkCb.Content = Strings.T("adv_noik");
            IgnoreVmdIkCb.Content = Strings.T("adv_ignore_vmd_ik");
            NoExtrasCb.Content = Strings.T("adv_no_extras");
            NoCustomAttrsCb.Content = Strings.T("adv_no_custom_attrs");
            DisableIkLabel.Text = Strings.T("adv_disable_ik_label");
            StepLabel.Text = Strings.T("adv_step_label");
            AnimNameLabel.Text = Strings.T("adv_anim_name_label");

            PhysEnableCb.Content = Strings.T("phys_enable");
            PhysHint.Text = Strings.T("phys_hint");
            WarmupLabel.Text = Strings.T("phys_warmup");
            WarmupHint.Text = Strings.T("phys_warmup_hint");
            SimHzLabel.Text = Strings.T("phys_simhz");
            SimHzHint.Text = Strings.T("phys_simhz_hint");
            SubStepsLabel.Text = Strings.T("phys_substeps");
            IterationsLabel.Text = Strings.T("phys_iterations");
            GravityLabel.Text = Strings.T("phys_gravity");
            GravityHint.Text = Strings.T("phys_gravity_hint");
            NoBoneMergeCb.Content = Strings.T("phys_no_bone_merge");
            AlignBonesCb.Content = Strings.T("phys_align");
            AlignHint.Text = Strings.T("phys_align_hint");

            LogHeader.Text = Strings.T("frame_log");
            RunButton.Content = Strings.T(_running ? "btn_running" : "btn_run");
        }

        private void UpdatePhysEnabled()
        {
            bool on = PhysEnableCb.IsChecked == true;
            PhysGrid.IsEnabled = on;
            NoBoneMergeCb.IsEnabled = on;
            AlignBonesCb.IsEnabled = on;
        }

        // ------------------------------------------------------------------
        private void SetupDragDrop()
        {
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, (_, e) =>
                e.DragEffects = e.Data.Contains(DataFormats.Files)
                    ? DragDropEffects.Copy : DragDropEffects.None);
            AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                var files = e.Data.GetFiles();
                if (files == null) return;
                // 拡張子で行き先を決める。どちらの欄に落としても正しく入る。
                foreach (var f in files)
                {
                    string path = f.TryGetLocalPath();
                    if (path == null) continue;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".pmx") PmxBox.Text = path;
                    else if (ext == ".vmd") VmdBox.Text = path;
                }
            });
        }

        private async Task PickAsync(TextBox target, string label, string pattern)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(label) { Patterns = new[] { pattern } },
                    new FilePickerFileType("All") { Patterns = new[] { "*" } },
                },
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) target.Text = p;
        }

        private async Task PickSaveAsync()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                DefaultExtension = "glb",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("glTF binary") { Patterns = new[] { "*.glb" } },
                },
            });
            if (file?.TryGetLocalPath() is { } p) { OutBox.Text = p; _outEdited = true; }
        }

        private void UpdateDefaultOutput()
        {
            if (_outEdited) return;
            string pmx = PmxBox.Text?.Trim();
            OutBox.Text = string.IsNullOrEmpty(pmx) ? "" : Path.ChangeExtension(pmx, ".glb");
        }

        // ------------------------------------------------------------------
        private void Log(string s) => Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += s + Environment.NewLine;
            LogBox.CaretIndex = LogBox.Text.Length;
        });

        private async Task RunAsync()
        {
            if (_running) return;
            string pmx = PmxBox.Text?.Trim();
            if (string.IsNullOrEmpty(pmx))
            {
                LogBox.Text = Strings.T("err_no_pmx") + Environment.NewLine;
                return;
            }
            string vmd = VmdBox.Text?.Trim();
            bool physics = PhysEnableCb.IsChecked == true;
            if (physics && string.IsNullOrEmpty(vmd))
            {
                LogBox.Text = Strings.T("err_no_vmd_for_physics") + Environment.NewLine;
                return;
            }
            string outPath = OutBox.Text?.Trim();
            if (string.IsNullOrEmpty(outPath)) outPath = Path.ChangeExtension(pmx, ".glb");

            var opt = new ConvertOptions
            {
                VmdPath = string.IsNullOrEmpty(vmd) ? null : vmd,
                Unlit = UnlitCb.IsChecked == true,
                ForceDoubleSided = DoubleSidedCb.IsChecked == true,
                MorphMode = ((ComboBoxItem)MorphBox.SelectedItem)?.Content?.ToString() ?? "sparse",
                AlphaMode = ((ComboBoxItem)AlphaBox.SelectedItem)?.Content?.ToString() ?? "auto",
                Scale = ParseD(ScaleBox.Text, 0.08),
                SolveIk = NoIkCb.IsChecked != true,
                UseVmdIkFrames = IgnoreVmdIkCb.IsChecked != true,
                Extras = NoExtrasCb.IsChecked != true,
                CustomAttrs = NoCustomAttrsCb.IsChecked != true,
                Step = (int)ParseD(StepBox.Text, 1),
                AnimName = string.IsNullOrWhiteSpace(AnimNameBox.Text) ? null : AnimNameBox.Text.Trim(),
                BakePhysics = physics,
            };
            var names = (DisableIkBox.Text ?? "").Split(',')
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (names.Count > 0) opt.DisableIk = names;

            opt.Physics.WarmupSeconds = (float)ParseD(WarmupBox.Text, 2.0);
            opt.Physics.SimHz = (float)ParseD(SimHzBox.Text, 0);
            opt.Physics.SubSteps = (int)ParseD(SubStepsBox.Text, 0);
            opt.Physics.SolverIterations = (int)ParseD(IterationsBox.Text, 0);
            opt.Physics.Gravity = (float)ParseD(GravityBox.Text, 0);
            opt.Physics.BoneMergeMode = NoBoneMergeCb.IsChecked != true;
            opt.Physics.AlignBonePositions = AlignBonesCb.IsChecked == true;

            _running = true;
            RunButton.Content = Strings.T("btn_running");
            RunButton.IsEnabled = false;
            Progress.IsVisible = true;
            LogBox.Text = "";

            try
            {
                await Task.Run(() => MmdConverter.Convert(pmx, outPath, opt, Log));
                Log(Strings.T("done") + " " + outPath);
            }
            catch (Exception ex)
            {
                Log(Strings.T("failed") + " " + ex.Message);
            }
            finally
            {
                _running = false;
                RunButton.Content = Strings.T("btn_run");
                RunButton.IsEnabled = true;
                Progress.IsVisible = false;
            }
        }

        private static double ParseD(string s, double fallback) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }
}
