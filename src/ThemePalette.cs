using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shiyi
{
    internal sealed class ThemeColors
    {
        public readonly string Canvas, Surface, Text, Muted, Border, Accent;
        public ThemeColors(string canvas, string surface, string text, string muted, string border, string accent)
        { Canvas = canvas; Surface = surface; Text = text; Muted = muted; Border = border; Accent = accent; }
    }

    internal sealed class ThemePreset
    {
        public readonly string Id, Name;
        public readonly ThemeColors Light, Dark;
        private ThemePreset(string id, string name, ThemeColors light, ThemeColors dark)
        { Id = id; Name = name; Light = light; Dark = dark; }

        // The six T3 variants use the sRGB equivalents of the corresponding T3 Code palette roles.
        public static readonly ThemePreset[] All = {
            new ThemePreset("shiyi", "拾译", new ThemeColors("#F4F7F3","#FFFFFF","#20352F","#566C64","#CCD9D1","#166650"),
                new ThemeColors("#13201D","#1C2C28","#EEF6F2","#A3B8B0","#3E544C","#68BEA1")),
            new ThemePreset("t3-code", "T3 蓝", new ThemeColors("#FCFCFC","#FFFFFF","#27272A","#71717B","#E4E4E7","#1B4ED8"),
                new ThemeColors("#0A0A0A","#111111","#F5F5F5","#818181","#191919","#346BF1")),
            new ThemePreset("t3-chat", "玫瑰", new ThemeColors("#FDF7FD","#FAF3FB","#501854","#AC1668","#EEE1ED","#DB2777"),
                new ThemeColors("#1F1A24","#29232D","#F9F8FB","#E7D0DD","#27242C","#A3004C")),
            new ThemePreset("grove", "森林", new ThemeColors("#F3F7F4","#F3F7F4","#241523","#746C73","#CBD5D1","#1B7D50"),
                new ThemeColors("#1B2821","#1B2821","#FFFAFF","#919595","#415F4F","#69D69A")),
            new ThemePreset("ocean", "海洋", new ThemeColors("#F5F7F8","#F5F7F8","#241523","#746C75","#CDD4DC","#2672AF"),
                new ThemeColors("#17212B","#17212B","#FFFAFF","#8D8F97","#405567","#70B9EE")),
            new ThemePreset("ember", "余烬", new ThemeColors("#F9F7F5","#F9F7F5","#241523","#766C74","#DDD2CE","#AE552A"),
                new ThemeColors("#291E1A","#291E1A","#FFFAFF","#968E8F","#664C3F","#F09A64")),
            new ThemePreset("iris", "鸢尾", new ThemeColors("#F8F7F9","#F8F7F9","#241523","#766C76","#D6D1DE","#7253B9"),
                new ThemeColors("#1D1929","#1D1929","#FFFAFF","#8E8A95","#4D4366","#9D7DF2"))
        };

        public static ThemePreset Find(string id) { return All.FirstOrDefault(p => p.Id == id) ?? All[0]; }
        public static string Mode(string value) { return value == "dark" || value == "system" ? value : "light"; }
        public static string Hex(string input)
        {
            var s = (input ?? "").Trim();
            if (!Regex.IsMatch(s, @"^#?(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$")) return "";
            s = s.TrimStart('#').ToUpperInvariant();
            if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
            return "#" + s;
        }
    }
}
