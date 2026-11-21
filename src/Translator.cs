using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;

namespace ModToolEN
{
    /// Chinese -> English text for every string the original tool can show.
    static class Translator
    {
        static readonly string[,] Pairs =
        {
            // Resource table (Properties.Resources)
            { "贴图会被程序转换，是否继续？", "The texture will be converted to the original format. Continue?" },
            { "部分模型可能需要选中此项才能正常解析", "Some models only parse correctly with this option checked." },
            { "3DM NBA2K12 MOD工具", "3DM NBA2K12 MOD Tool" },
            { "无法识别的图片", "Unrecognized image" },
            { "是否覆盖源文件？", "Overwrite the original file?" },
            { "设为模型贴图", "Set as model texture" },
            { "清除模型贴图", "Clear model texture" },
            { "使用透明通道", "Use alpha channel" },
            { "MIPMAP等级", "MIPMAP level" },
            { "模型解析2", "Model parser 2" },
            { "保存中···", "Saving..." },
            { "保存完毕", "Save complete" },
            { "保存提醒", "Save reminder" },
            { "原始大小", "Original size" },
            { "适应窗口", "Fit to window" },
            { "另存为", "Save as" },
            { "导出", "Export" },
            { "替换", "Replace" },
            { "文件", "File" },
            { "打开", "Open" },
            { "保存", "Save" },
            { "关于", "About" },

            // Hard-coded strings in the tool
            { "请使用“NBA2K12 MOD工具”打开此文件", "Please open this file with \"NBA2K12 MOD Tool\"" },
            { "NBA2K12 MOD工具", "NBA2K12 MOD Tool" },
            { "无法打开的IFF文件", "This IFF file cannot be opened" },
            { "不是N2KM文件", "Not an N2KM file" },
            { "N2KM版本错误", "Wrong N2KM version" },
            { "支持的图片", "Supported images" },
            { "导入成功", "Import successful" },
            { " 打开失败", " failed to open" },
            { "打开失败", "Failed to open" },
            { "格式未知", "Unknown format" },
            { "找不到", "Cannot find " },
            { "成功", "Success" },
            { "版本：", "Version: " },
            { "3DM游戏网：", "3DM Game: " },
            { "3DM论坛：", "3DM Forum: " },
            { "NBA2K12 讨论区", "NBA2K12 Board" },
            { "3DM游戏网", "3DM Game" },
            { "3DM论坛", "3DM Forum" },
            { "巴布网", "Babu Forum" },

            // 3D preview window (DXRender)
            { "旋转", "Rotate" },

            // Common Windows button captions on Chinese systems
            { "确定", "OK" },
            { "取消", "Cancel" },

            // Leftover full-width punctuation
            { "　", " " },
            { "：", ": " },
            { "，", ", " },
            { "？", "?" },
            { "！", "!" },
            { "（", " (" },
            { "）", ") " },
            { "···", "..." },
        };

        static readonly List<KeyValuePair<string, string>> Sorted = new List<KeyValuePair<string, string>>();
        static readonly Regex VertexError = new Regex("模型(.+?)顶点数错误");

        static Translator()
        {
            for (int i = 0; i < Pairs.GetLength(0); i++)
                Sorted.Add(new KeyValuePair<string, string>(Pairs[i, 0], Pairs[i, 1]));
            // Longest first so "导入成功" wins over "成功", etc.
            Sorted.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        }

        public static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xFF00 && c <= 0xFFEF) || c == '·') return true;
            return false;
        }

        public static string T(string s)
        {
            if (!HasCjk(s)) return s;
            s = VertexError.Replace(s, "Model $1: vertex count does not match the original");
            foreach (var kv in Sorted)
                if (s.IndexOf(kv.Key, StringComparison.Ordinal) >= 0)
                    s = s.Replace(kv.Key, kv.Value);
            return s;
        }
    }

    /// Replaces the tool's Properties.Resources manager so every resource string comes back in English.
    class EnglishResources : ResourceManager
    {
        public EnglishResources(Assembly a) : base("NBA2K12TEXTUREEDITOR.Properties.Resources", a) { }

        public override string GetString(string name) { return Translator.T(base.GetString(name)); }
        public override string GetString(string name, CultureInfo culture) { return Translator.T(base.GetString(name, culture)); }

        public static void Install(Assembly toolAsm)
        {
            var t = toolAsm.GetType("NBA2K12TEXTUREEDITOR.Properties.Resources");
            if (t == null) return;
            var f = t.GetField("resourceMan", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(null, new EnglishResources(toolAsm));
        }
    }
}
