using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RamCleaner;

/// <summary>
/// Tokens visuais baseados no design system do davimf.dev (src/styles/tokens.css).
/// Escuro = valores originais. Claro = derivado dos mesmos tokens (a base só tem escuro).
/// O tema é escolhido uma vez ao abrir, seguindo o Windows.
/// </summary>
internal static class Theme
{
    public static bool IsDark { get; private set; } = true;

    // superfícies e texto
    public static Color Bg, Surface1, Surface2, Surface3;
    public static Color Fg, FgSoft, FgMuted;
    // accent + estado
    public static Color Accent, AccentBright, Danger, Ok, Warn;
    // linhas (na base são rgba sobre o fundo; aqui já misturadas)
    public static Color Line, LineStrong;

    public const int RadiusSm = 8;   // chip, badge
    public const int RadiusMd = 12;  // campo
    public const int RadiusLg = 16;  // painel, card

    public static string SansFamily = "Segoe UI";
    public static string DisplayFamily = "Segoe UI";

    public static void Init()
    {
        IsDark = !WindowsUsesLightTheme();

        if (IsDark)
        {
            Bg = C(7, 7, 7);
            Surface1 = C(13, 13, 13);
            Surface2 = C(18, 18, 18);
            Surface3 = C(24, 24, 24);
            Fg = C(244, 242, 237);
            FgSoft = C(184, 182, 176);
            FgMuted = C(139, 139, 135);
            Accent = C(230, 181, 102);
            AccentBright = C(242, 189, 94);
            Danger = C(217, 105, 94);
            Ok = C(111, 191, 143);
            Warn = C(206, 124, 66);
            Line = C(30, 30, 30);
            LineStrong = C(44, 44, 43);
        }
        else
        {
            Bg = C(246, 244, 239);
            Surface1 = C(252, 251, 248);
            Surface2 = C(241, 239, 233);
            Surface3 = C(233, 230, 223);
            Fg = C(22, 21, 19);
            FgSoft = C(70, 68, 64);
            FgMuted = C(108, 106, 101);
            Accent = C(163, 114, 36);
            AccentBright = C(186, 132, 44);
            Danger = C(180, 64, 54);
            Ok = C(40, 122, 78);
            Warn = C(172, 88, 30);
            Line = C(228, 225, 218);
            LineStrong = C(210, 206, 198);
        }

        using var fonts = new InstalledFontCollection();
        var names = new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        SansFamily = First(names, "Satoshi", "Segoe UI Variable Text", "Segoe UI");
        DisplayFamily = First(names, "Cabinet Grotesk", "Segoe UI Variable Display", "Segoe UI");
    }

    private static string First(HashSet<string> installed, params string[] options) =>
        options.FirstOrDefault(installed.Contains) ?? "Segoe UI";

    private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    /// <summary>Mistura <paramref name="c"/> sobre <paramref name="over"/> com opacidade <paramref name="a"/> (0–1).</summary>
    public static Color Mix(Color c, Color over, double a) => Color.FromArgb(
        (int)(c.R * a + over.R * (1 - a)),
        (int)(c.G * a + over.G * (1 - a)),
        (int)(c.B * a + over.B * (1 - a)));

    // ------------------------------------------------------------ fontes
    private static readonly Dictionary<(string, float, FontStyle), Font> _fontCache = new();

    public static Font Sans(float size, FontStyle style = FontStyle.Regular) => Get(SansFamily, size, style);
    public static Font Display(float size, FontStyle style = FontStyle.Bold) => Get(DisplayFamily, size, style);
    public static Font Eyebrow => Sans(7.5f, FontStyle.Bold);

    private static Font Get(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        if (!_fontCache.TryGetValue(key, out var f))
            _fontCache[key] = f = new Font(family, size, style, GraphicsUnit.Point);
        return f;
    }

    // ------------------------------------------------------------ desenho
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color? border = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundRect(r, radius);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        if (border is Color bc)
        {
            using var pen = new Pen(bc, 1f);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Rótulo técnico: caixa alta, espaçado (assinatura "Eyebrow" da base).</summary>
    public static void DrawEyebrow(Graphics g, string text, Point at, Color color, float tracking = 1.1f)
    {
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var font = Eyebrow;
        float x = at.X;
        using var brush = new SolidBrush(color);
        using var fmt = StringFormat.GenericTypographic;
        foreach (char ch in text.ToUpperInvariant())
        {
            string s = ch.ToString();
            g.DrawString(s, font, brush, x, at.Y, fmt);
            float w = ch == ' ' ? font.Size * 0.45f : g.MeasureString(s, font, 100, fmt).Width;
            x += w + tracking;
        }
    }

    public static int MeasureEyebrow(Graphics g, string text, float tracking = 1.1f)
    {
        var font = Eyebrow;
        using var fmt = StringFormat.GenericTypographic;
        float x = 0;
        foreach (char ch in text.ToUpperInvariant())
            x += (ch == ' ' ? font.Size * 0.45f : g.MeasureString(ch.ToString(), font, 100, fmt).Width) + tracking;
        return (int)Math.Ceiling(x);
    }

    // ------------------------------------------------------------ janela
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    /// <summary>Barra de título escura/clara e na cor do fundo (Win11). Ignora falhas em Windows antigos.</summary>
    public static void ApplyWindow(Form f)
    {
        try
        {
            int dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            int caption = ColorRef(Bg), text = ColorRef(Fg), border = ColorRef(LineStrong);
            DwmSetWindowAttribute(f.Handle, DWMWA_CAPTION_COLOR, ref caption, 4);
            DwmSetWindowAttribute(f.Handle, DWMWA_TEXT_COLOR, ref text, 4);
            DwmSetWindowAttribute(f.Handle, DWMWA_BORDER_COLOR, ref border, 4);
        }
        catch { }
    }

    /// <summary>Barras de rolagem escuras em grids/listas.</summary>
    public static void ApplyScrollbars(Control c)
    {
        if (!IsDark) return;
        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------ menu da bandeja
    public static ToolStripRenderer MenuRenderer() => new MenuRendererImpl();

    private sealed class MenuRendererImpl : ToolStripProfessionalRenderer
    {
        public MenuRendererImpl() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Fg : FgMuted;
            e.TextFont = Sans(9f);
            base.OnRenderItemText(e);
        }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Surface2;
            public override Color ImageMarginGradientBegin => Surface2;
            public override Color ImageMarginGradientMiddle => Surface2;
            public override Color ImageMarginGradientEnd => Surface2;
            public override Color MenuBorder => LineStrong;
            public override Color MenuItemBorder => Surface3;
            public override Color MenuItemSelected => Surface3;
            public override Color MenuItemSelectedGradientBegin => Surface3;
            public override Color MenuItemSelectedGradientEnd => Surface3;
            public override Color SeparatorDark => Line;
            public override Color SeparatorLight => Line;
        }
    }
}
