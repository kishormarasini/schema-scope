using Spectre.Console;
using Spectre.Console.Rendering;

namespace SchemaScope.Ui;

internal static class Theme
{
    public const string Title    = "bold #89b4fa";
    public const string Brand    = "#89b4fa";
    public const string Prompt   = "bold #89b4fa";

    public const string Subtitle = "#7f849c";
    public const string Muted    = "#6c7086";
    public const string Subtle   = "#585b70";

    public const string Success  = "#a6e3a1";
    public const string Warning  = "#fab387";
    public const string Danger   = "#f38ba8";
    public const string Info     = "#89dceb";
    public const string Accent   = "#cba6f7";

    public const string PillOk      = "#1e1e2e on #a6e3a1";
    public const string PillDiffers = "#1e1e2e on #fab387";
    public const string PillMissing = "#1e1e2e on #f38ba8";
    public const string PillNeutral = "#1e1e2e on #7f849c";

    public static Style BorderStyle    => new(Hex("#585b70"));
    public static Style HighlightStyle => new(Hex("#89b4fa"), decoration: Decoration.Bold);
    public static Style PromptStyle    => new(Hex("#cdd6f4"));
    public static Color Subtitles      => Hex("#7f849c");

    public const string PillAccent = "#1e1e2e on #cba6f7";
    public const string PillBrand  = "#1e1e2e on #89b4fa";

    public static string Escape(string text) => Markup.Escape(text ?? string.Empty);

    public static string Gradient(string text, string fromHex, string toHex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var from = Hex(fromHex);
        var to   = Hex(toHex);
        var sb   = new System.Text.StringBuilder(text.Length * 24);
        var last = Math.Max(1, text.Length - 1);

        for (var i = 0; i < text.Length; i++)
        {
            var t = (double)i / last;
            var r = (byte)(from.R + (to.R - from.R) * t);
            var g = (byte)(from.G + (to.G - from.G) * t);
            var b = (byte)(from.B + (to.B - from.B) * t);
            sb.Append($"[#{r:x2}{g:x2}{b:x2}]{Markup.Escape(text[i].ToString())}[/]");
        }
        return sb.ToString();
    }

    public static string GradientRule(int width, string fromHex, string toHex) =>
        Gradient(new string('─', Math.Max(1, width)), fromHex, toHex);

    public static Color Hex(string hex)
    {
        var s = hex.TrimStart('#');
        return new Color(
            Convert.ToByte(s.Substring(0, 2), 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }

    private static int CellWidth(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return 0;
        }
        return new Segment(plainText).CellCount();
    }

    public static string MenuRow(string glyph, string label, string description)
    {
        const int LabelStartCol       = 4;
        const int DefaultDescStartCol = 24;

        var glyphCells = CellWidth(glyph);
        var labelGap   = LabelStartCol - glyphCells;
        if (labelGap < 1)
        {
            labelGap = 1;
        }

        var labelCells = CellWidth(label);
        var descStart  = Math.Max(DefaultDescStartCol, LabelStartCol + labelCells + 2);
        var descGap    = descStart - LabelStartCol - labelCells;

        return $"[{Brand}]{Markup.Escape(glyph)}[/]" +
               $"{new string(' ', labelGap)}" +
               $"{Markup.Escape(label)}" +
               $"{new string(' ', descGap)}" +
               $"[{Subtitle}]{Markup.Escape(description)}[/]";
    }
}
