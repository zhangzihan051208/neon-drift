using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 多语言。代码里的文字一律写英文（英文就是原文、也是查表的键），显示的时候再翻译。
    /// 译文放在 StreamingAssets/Lang/&lt;语言&gt;.tsv：每行「英文原文 TAB 译文」，# 开头是注释，\n 表示换行。
    /// 原文里带 {0} {1} 的是模板：比如 "E  Buy {0} (₡{1})"，运行时会匹配 "E  Buy Crowbar (₡50)"，并把里面的武器名也翻译掉。
    /// 拼起来的长句（多行、用 " · " 隔开的）会拆开分别翻译。加一门新语言 = 加一个 tsv 文件，再在 Langs 里登记。
    /// </summary>
    public static class Loc
    {
        public static readonly string[] Langs = { "en", "zh" };
        public static readonly string[] LangNames = { "English", "中文" };
        public static string Lang { get; private set; } = "en";

        static readonly Dictionary<string, string> exact = new Dictionary<string, string>();
        static readonly List<(Regex re, string to, int lit)> templates = new List<(Regex, string, int)>();
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        static readonly string[] Seps = { " · ", "   ", "  " };

        public static void SetLang(string lang)
        {
            Lang = System.Array.IndexOf(Langs, lang) >= 0 ? lang : "en";
            exact.Clear(); templates.Clear(); cache.Clear();
            if (Lang == "en") return;
            string path = Path.Combine(Application.streamingAssetsPath, "Lang", Lang + ".tsv");
            if (!File.Exists(path)) { Debug.LogWarning($"[Loc] missing {path}"); return; }
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                int tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                string k = raw.Substring(0, tab).Replace("\\n", "\n"), v = raw.Substring(tab + 1).Replace("\\n", "\n");
                if (v.Length == 0) continue;
                if (Regex.IsMatch(k, @"\{\d\}"))
                {
                    string pat = "^" + Regex.Replace(Regex.Escape(k).Replace(@"\{", "{"), @"\{(\d)\}", m => $"(?<a{m.Groups[1].Value}>.+?)") + "$";
                    templates.Add((new Regex(pat, RegexOptions.Singleline), v, Regex.Replace(k, @"\{\d\}", "").Length));
                }
                else exact[k] = v;
            }
            // 固定文字多的模板先试，免得 "{0}: {1}" 这种宽泛的模板抢先匹配
            templates.Sort((a, b) => b.lit.CompareTo(a.lit));
        }

        /// <summary>翻译一段要显示的文字（英文直接原样返回）。</summary>
        public static string T(string s)
        {
            if (Lang == "en" || string.IsNullOrEmpty(s)) return s;
            if (cache.TryGetValue(s, out var hit)) return hit;
            string r = Tr(s, 0);
            if (cache.Count > 6000) cache.Clear();
            cache[s] = r;
            return r;
        }

        /// <summary>带参数的：T("Room {0}", 3)。</summary>
        public static string T(string template, params object[] args) => string.Format(T(template), args);

        static string Tr(string s, int depth)
        {
            if (depth > 6 || s.Length == 0) return s;
            if (exact.TryGetValue(s, out var v)) return v;
            // 前后的空白保留
            string core = s.Trim();
            if (core.Length != s.Length)
            {
                if (core.Length == 0) return s;
                int lead = s.IndexOf(core[0]);
                return s.Substring(0, lead) + Tr(core, depth + 1) + s.Substring(lead + core.Length);
            }
            if (s.IndexOf('\n') >= 0)
            {
                var lines = s.Split('\n');
                for (int i = 0; i < lines.Length; i++) lines[i] = Tr(lines[i], depth + 1);
                return string.Join("\n", lines);
            }
            // 破折号连起来的两句先拆开（免得 "{0}: {1}" 这种模板把两句吞成一句）
            if (s.IndexOf(" — ", System.StringComparison.Ordinal) >= 0)
            {
                var parts = s.Split(new[] { " — " }, System.StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++) parts[i] = Tr(parts[i], depth + 1);
                return string.Join(" — ", parts);
            }
            foreach (var (re, to, _) in templates)
            {
                var m = re.Match(s);
                if (!m.Success) continue;
                string outS = to;
                for (int i = 0; i < 6; i++)
                {
                    var g = m.Groups["a" + i];
                    if (g.Success) outS = outS.Replace("{" + i + "}", Tr(g.Value, depth + 1));
                }
                return outS;
            }
            foreach (var sep in Seps)
            {
                if (s.IndexOf(sep, System.StringComparison.Ordinal) < 0) continue;
                var parts = s.Split(new[] { sep }, System.StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++) parts[i] = Tr(parts[i], depth + 1);
                return string.Join(sep, parts);
            }
            return s;
        }
    }
}
