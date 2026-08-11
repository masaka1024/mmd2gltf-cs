// ===========================================================================
// 画面文言 (日本語 / English)。
// 変換オプションの文言は tkinter 版 gui.py から引き継いでいる
// (削除した PBD 物理専用の約35オプションぶんは無い)。
// 物理ベイクの項目は自作エンジン用に書き下ろした。
// ===========================================================================
using System.Collections.Generic;

namespace Mmd2Gltf.Gui
{
    public static class Strings
    {
        public static string Lang = "ja";

        public static string T(string key) =>
            (Lang == "ja" ? Ja : En).TryGetValue(key, out var s) ? s
                : (En.TryGetValue(key, out var e) ? e : key);

        public static readonly Dictionary<string, string> Ja = new()
        {
            ["window_title"] = "mmd2gltf - PMX/VMD → glTF (.glb) 変換",
            ["frame_files"] = "ファイル",
            ["label_pmx"] = "PMXモデル *",
            ["label_vmd"] = "VMDモーション",
            ["label_out"] = "出力 .glb",
            ["browse"] = "参照...",
            ["drop_hint"] = "(ドラッグ&ドロップ可)",

            ["frame_options"] = "オプション",
            ["opt_unlit"] = "unlit(MMDのトゥーン見た目に近づける)",
            ["opt_doubleside"] = "全材質を両面描画(髪・スカートの裏面が消える場合に)",
            ["opt_morph_mode"] = "モーフ格納方式",
            ["opt_morph_hint"] =
                "✱ 他ソフト(Unity/UniGLTF等)へ持ち込む・忠実な変換が目的なら「dense」を" +
                "選択してください(sparseは非対応のインポータで表情が壊れることがあります)。" +
                "容量を抑えたい場合のみ「sparse」(既定)でOKです。",
            ["opt_alpha_mode"] = "アルファモード",
            ["opt_scale"] = "スケール(MMD単位→m。既定0.08。等倍にしたい場合は1.0)",

            ["adv_toggle"] = "詳細設定",
            ["tab_general"] = "一般",
            ["tab_physics"] = "物理ベイク",
            ["adv_noik"] = "IKを解かない",
            ["adv_ignore_vmd_ik"] = "VMD内のIK ON/OFFキーを無視",
            ["adv_no_extras"] = "extras.mmd を出力しない",
            ["adv_no_custom_attrs"] =
                "MMD固有の頂点属性を省略\n" +
                "  ※Blenderで「メッシュを読み込めない」エラーが出る場合はこれをON",
            ["adv_disable_ik_label"] = "無効化するIK名(カンマ区切り)",
            ["adv_step_label"] = "サンプリング間隔",
            ["adv_anim_name_label"] = "アニメーション名",

            ["phys_enable"] = "揺れ物の物理をベイクする(髪・スカート・リボン等)",
            ["phys_hint"] =
                "PMX の剛体とジョイントを自作の Bullet 互換エンジンで解いて、" +
                "その結果をボーンのキーフレームに焼き込みます。モーション(VMD)が必要です。\n" +
                "モデル作者が設定したパラメータがそのまま効くので、通常は下の項目を" +
                "触る必要はありません。",
            ["phys_warmup"] = "ウォームアップ(秒)",
            ["phys_warmup_hint"] =
                "最初のフレームの姿勢で空回しして、髪やスカートを落ち着かせてから" +
                "本番を始めます。0にすると1フレーム目にバインド姿勢から落ちる動きが残ります。",
            ["phys_simhz"] = "内部の刻み(Hz)",
            ["phys_simhz_hint"] =
                "0でエンジン既定(30Hz)。ばね定数が大きいモデルで揺れ物が暴れる場合は" +
                "60や120に上げると安定します。上げるほど遅くなります。",
            ["phys_substeps"] = "サブステップ数",
            ["phys_iterations"] = "ソルバ反復数",
            ["phys_gravity"] = "重力の強さ(MMD単位)",
            ["phys_gravity_hint"] = "0でエンジン既定(98)。大きくするほど重く垂れ下がります。",
            ["phys_no_bone_merge"] = "PMX の「物理演算+ボーン位置合わせ」を無効にする",
            ["phys_align"] = "全ての物理ボーンに位置合わせ補正をかける",
            ["phys_align_hint"] =
                "既定はオフ。実測では、正しく配置されたモデルではオンにすると" +
                "むしろ体への食い込みが増えます。",

            ["frame_log"] = "ログ",
            ["btn_run"] = "変換する",
            ["btn_running"] = "変換中...",
            ["err_no_pmx"] = "PMXモデルを指定してください。",
            ["err_no_vmd_for_physics"] =
                "物理ベイクにはモーション(VMD)が必要です。VMDを指定するか、物理ベイクを外してください。",
            ["done"] = "完了しました:",
            ["failed"] = "失敗しました:",
        };

        public static readonly Dictionary<string, string> En = new()
        {
            ["window_title"] = "mmd2gltf - PMX/VMD to glTF (.glb)",
            ["frame_files"] = "Files",
            ["label_pmx"] = "PMX model *",
            ["label_vmd"] = "VMD motion",
            ["label_out"] = "Output .glb",
            ["browse"] = "Browse...",
            ["drop_hint"] = "(drag & drop supported)",

            ["frame_options"] = "Options",
            ["opt_unlit"] = "Unlit (closer to MMD's toon look)",
            ["opt_doubleside"] = "Force all materials double-sided (fixes disappearing hair/skirt backfaces)",
            ["opt_morph_mode"] = "Morph storage mode",
            ["opt_morph_hint"] =
                "✱ If you're importing into another tool (Unity/UniGLTF, etc.) or want the most " +
                "faithful conversion, choose \"dense\" — some importers do not support sparse " +
                "accessors and the face morphs break. \"sparse\" (default) only to keep files small.",
            ["opt_alpha_mode"] = "Alpha mode",
            ["opt_scale"] = "Scale (MMD units -> m. Default 0.08. Use 1.0 for no scaling)",

            ["adv_toggle"] = "Advanced settings",
            ["tab_general"] = "General",
            ["tab_physics"] = "Physics baking",
            ["adv_noik"] = "Don't solve IK",
            ["adv_ignore_vmd_ik"] = "Ignore IK on/off keys in VMD",
            ["adv_no_extras"] = "Don't output extras.mmd",
            ["adv_no_custom_attrs"] =
                "Omit MMD-specific vertex attributes\n" +
                "  Turn this on if Blender fails to load the mesh",
            ["adv_disable_ik_label"] = "IK names to disable (comma-separated)",
            ["adv_step_label"] = "Sampling step",
            ["adv_anim_name_label"] = "Animation clip name",

            ["phys_enable"] = "Bake secondary-motion physics (hair, skirt, ribbons, ...)",
            ["phys_hint"] =
                "Solves the PMX rigid bodies and joints with the bundled Bullet-compatible " +
                "engine and bakes the result into bone key frames. Requires a motion (VMD).\n" +
                "The model author's own parameters are used as-is, so you normally do not " +
                "need to touch the settings below.",
            ["phys_warmup"] = "Warm-up (seconds)",
            ["phys_warmup_hint"] =
                "Runs the simulation at the first frame's pose so hair and skirts settle " +
                "before recording. With 0 the first frames still show them falling from the bind pose.",
            ["phys_simhz"] = "Internal time step (Hz)",
            ["phys_simhz_hint"] =
                "0 = engine default (30 Hz). Raise to 60 or 120 if a model with strong springs " +
                "goes unstable. Higher is slower.",
            ["phys_substeps"] = "Sub-steps",
            ["phys_iterations"] = "Solver iterations",
            ["phys_gravity"] = "Gravity (MMD units)",
            ["phys_gravity_hint"] = "0 = engine default (98). Higher makes things hang heavier.",
            ["phys_no_bone_merge"] = "Disable PMX \"physics + bone alignment\" mode",
            ["phys_align"] = "Apply bone-position alignment to every physics bone",
            ["phys_align_hint"] =
                "Off by default. Measurements show that on correctly placed models turning this " +
                "on actually increases penetration into the body.",

            ["frame_log"] = "Log",
            ["btn_run"] = "Convert",
            ["btn_running"] = "Converting...",
            ["err_no_pmx"] = "Please choose a PMX model.",
            ["err_no_vmd_for_physics"] =
                "Physics baking needs a motion (VMD). Choose a VMD or turn physics baking off.",
            ["done"] = "Done:",
            ["failed"] = "Failed:",
        };
    }
}
