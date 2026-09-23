using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace RamCleaner;

// Controles desenhados à mão (GDI+). Só repintam quando algo muda: nenhum timer, nenhuma animação.

/// <summary>Painel com borda fina e cantos arredondados (equivalente ao &lt;Surface&gt; da base).</summary>
internal class SurfacePanel : Panel
{
    [DefaultValue(1)] public int Level { get; set; } = 1;
    [DefaultValue(Theme.RadiusLg)] public int Radius { get; set; } = Theme.RadiusLg;

    public SurfacePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Padding = new Padding(20);
    }

    public Color Fill => Level switch { 2 => Theme.Surface2, 3 => Theme.Surface3, _ => Theme.Surface1 };

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        BackColor = Fill; // filhos herdam a cor da superfície
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Pinta os cantos com a cor do pai para o arredondado ficar limpo.
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(e.Graphics, r, Radius, Fill, Theme.Line);
    }
}

/// <summary>Botão em pílula com as variantes da base: primary, secondary, ghost, danger.</summary>
internal class PillButton : Control, IButtonControl
{
    public enum Kind { Primary, Secondary, Ghost, Danger }

    private bool _hover, _down;
    [DefaultValue(Kind.Secondary)] public Kind Variant { get; set; } = Kind.Secondary;
    public DialogResult DialogResult { get; set; } = DialogResult.None;

    public PillButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        Font = Theme.Sans(9f, FontStyle.Bold);
        Height = 34;
        Margin = new Padding(0, 0, 8, 0);
        TabStop = true;
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); Invalidate(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }

    private void FitWidth()
    {
        int w = TextRenderer.MeasureText(Text, Font).Width;
        Width = w + 32;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) PerformClick(); // Enter já é tratado pelo AcceptButton do form
        base.OnKeyUp(e);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is Form f) f.DialogResult = DialogResult;
    }

    public void NotifyDefault(bool value) { }
    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        var parentBg = Parent?.BackColor ?? Theme.Bg;

        Color fill, text, border;
        switch (Variant)
        {
            case Kind.Primary:
                fill = _hover ? Theme.Mix(Theme.Fg, Theme.Accent, 0.88) : Theme.Fg;
                text = Theme.Bg;
                border = fill;
                break;
            case Kind.Danger:
                fill = _hover ? Theme.Mix(Theme.Danger, parentBg, 0.22) : Theme.Mix(Theme.Danger, parentBg, 0.12);
                text = Theme.Danger;
                border = Theme.Mix(Theme.Danger, parentBg, 0.35);
                break;
            case Kind.Ghost:
                fill = _hover ? Theme.Surface3 : parentBg;
                text = _hover ? Theme.Fg : Theme.FgMuted;
                border = fill;
                break;
            default: // Secondary
                fill = _hover ? Theme.Surface3 : parentBg;
                text = Theme.Fg;
                border = _hover ? Theme.Mix(Theme.Fg, parentBg, 0.25) : Theme.LineStrong;
                break;
        }
        if (_down) fill = Theme.Mix(fill, parentBg, 0.85);
        if (!Enabled) { text = Theme.Mix(text, parentBg, 0.5); }

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, r, Height / 2f, fill, border);

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Theme.Mix(Theme.Accent, parentBg, 0.6), 2f);
            using var path = Theme.RoundRect(new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), Height / 2f);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>Interruptor (substitui CheckBox).</summary>
internal class ToggleSwitch : Control
{
    private bool _checked, _hover;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Font = Theme.Sans(9f);
        Height = 28;
        Margin = new Padding(0, 0, 18, 0);
        TabStop = true;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Width = 36 + 10 + TextRenderer.MeasureText(Text, Font).Width + 4;
        Invalidate();
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Checked = !Checked; base.OnKeyUp(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = Parent?.BackColor ?? Theme.Bg;
        g.Clear(bg);
        int y = (Height - 20) / 2;
        DrawSwitch(g, new Rectangle(0, y, 36, 20), _checked, _hover, bg);

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Theme.Mix(Theme.Accent, bg, 0.6), 1.5f);
            using var path = Theme.RoundRect(new RectangleF(-0.5f + 1, y - 2 + 0.5f, 37, 23), 11.5f);
            g.DrawPath(pen, path);
        }

        var tr = new Rectangle(46, 0, Width - 46, Height);
        TextRenderer.DrawText(g, Text, Font, tr, Theme.FgSoft,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
    }

    /// <summary>Usado também pelas células "Ativo" da tabela.</summary>
    public static void DrawSwitch(Graphics g, Rectangle r, bool on, bool hover, Color bg)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = on ? Theme.Accent : (hover ? Theme.Surface3 : Theme.Mix(Theme.Fg, bg, 0.10));
        var border = on ? Theme.Accent : Theme.LineStrong;
        Theme.FillRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f, track, border);

        int d = r.Height - 6;
        int kx = on ? r.Right - d - 3 : r.X + 3;
        using var knob = new SolidBrush(on ? Theme.Bg : Theme.FgMuted);
        g.FillEllipse(knob, kx, r.Y + 3, d, d);
    }
}

/// <summary>Card de métrica: rótulo técnico + número grande + legenda.</summary>
internal class StatCard : SurfacePanel
{
    private string _label = "", _value = "-", _caption = "";
    private Color? _valueColor;

    public string Label { get => _label; set { _label = value; Invalidate(); } }
    public string Value { get => _value; set { if (_value == value) return; _value = value; Invalidate(); } }
    public string Caption { get => _caption; set { if (_caption == value) return; _caption = value; Invalidate(); } }
    public Color? ValueColor { get => _valueColor; set { _valueColor = value; Invalidate(); } }

    public StatCard()
    {
        Height = 100;
        Margin = new Padding(0, 0, 12, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        Theme.DrawEyebrow(g, _label, new Point(18, 16), Theme.FgMuted);

        var vf = Theme.Display(18f);
        TextRenderer.DrawText(g, _value, vf, new Point(15, 34), _valueColor ?? Theme.Fg, TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, _caption, Theme.Sans(8.5f), new Rectangle(18, 70, Width - 30, 20), Theme.FgMuted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

/// <summary>Campo arredondado (surface-2, raio 12) que hospeda um TextBox sem borda + sufixo opcional ("ms", "MB").</summary>
internal class FieldBox : Control
{
    public TextBox Input { get; } = new();
    private readonly string _suffix;
    private bool _focused;

    public FieldBox(string placeholder = "", string suffix = "", bool numeric = false)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _suffix = suffix;
        Height = 34;
        Cursor = Cursors.IBeam;

        Input.BorderStyle = BorderStyle.None;
        Input.BackColor = Theme.Surface2;
        Input.ForeColor = Theme.Fg;
        Input.Font = Theme.Sans(9.5f);
        Input.PlaceholderText = placeholder;
        if (numeric)
        {
            Input.TextAlign = HorizontalAlignment.Right;
            Input.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        }
        Input.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        Input.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        Controls.Add(Input);
    }

    protected override void OnClick(EventArgs e) { Input.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int sfx = _suffix.Length > 0 ? TextRenderer.MeasureText(_suffix, Theme.Sans(9f)).Width + 8 : 0;
        int h = Input.PreferredHeight;
        Input.SetBounds(12, (Height - h) / 2, Math.Max(10, Width - 24 - sfx), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        var border = _focused ? Theme.Mix(Theme.Accent, Theme.Surface2, 0.6) : Theme.Line;
        Theme.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.RadiusMd, Theme.Surface2, border);
        if (_suffix.Length > 0)
            TextRenderer.DrawText(g, _suffix, Theme.Sans(9f), new Rectangle(0, 0, Width - 12, Height), Theme.FgMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>Rótulo técnico (eyebrow) como controle.</summary>
internal class EyebrowLabel : Control
{
    public EyebrowLabel(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Text = text;
        Height = 16;
        Margin = new Padding(0, 9, 8, 0);
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        Width = Theme.MeasureEyebrow(g, text) + 4;
    }

    public Color Color { get; set; } = Theme.FgMuted;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.DrawEyebrow(e.Graphics, Text, new Point(0, 1), Color);
    }
}

/// <summary>Estilo comum das tabelas + desenho de badges e interruptores nas células.</summary>
internal static class GridStyle
{
    public enum Tone { Neutral, Ok, Warn, Danger, Accent }

    public static void Apply(DataGridView grid, Color surface)
    {
        grid.BackgroundColor = surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Theme.Line;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = 34;
        var h = grid.ColumnHeadersDefaultCellStyle;
        h.BackColor = surface;
        h.ForeColor = Theme.FgMuted;
        h.SelectionBackColor = surface;
        h.SelectionForeColor = Theme.FgMuted;
        h.Font = Theme.Sans(7.5f, FontStyle.Bold);
        h.Padding = new Padding(8, 0, 8, 0);

        grid.RowTemplate.Height = 42;
        var d = grid.DefaultCellStyle;
        d.BackColor = surface;
        d.ForeColor = Theme.FgSoft;
        d.SelectionBackColor = Theme.Surface3;
        d.SelectionForeColor = Theme.Fg;
        d.Font = Theme.Sans(9f);
        d.Padding = new Padding(8, 0, 8, 0);

        grid.EditingControlShowing += (_, e) =>
        {
            e.Control.BackColor = Theme.Surface3;
            e.Control.ForeColor = Theme.Fg;
        };

        grid.HandleCreated += (_, _) => Theme.ApplyScrollbars(grid);
        typeof(DataGridView).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(grid, true);
    }

    /// <summary>Desenha uma célula de CheckBox como interruptor.</summary>
    public static void PaintSwitchCell(DataGridViewCellPaintingEventArgs e)
    {
        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var bg = selected ? e.CellStyle!.SelectionBackColor : e.CellStyle!.BackColor;
        e.PaintBackground(e.CellBounds, selected);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        var r = new Rectangle(e.CellBounds.X + 10, e.CellBounds.Y + (e.CellBounds.Height - 18) / 2, 32, 18);
        ToggleSwitch.DrawSwitch(e.Graphics!, r, e.FormattedValue is true || e.Value is true, false, bg);
        e.Handled = true;
    }

    /// <summary>Desenha o texto da célula como pílula de estado (Badge da base).</summary>
    public static void PaintBadgeCell(DataGridViewCellPaintingEventArgs e, Tone tone)
    {
        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var bg = selected ? e.CellStyle!.SelectionBackColor : e.CellStyle!.BackColor;
        e.PaintBackground(e.CellBounds, selected);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);

        string text = Convert.ToString(e.FormattedValue) ?? "";
        if (text.Length == 0) { e.Handled = true; return; }

        Color fg = tone switch
        {
            Tone.Ok => Theme.Ok, Tone.Warn => Theme.Warn, Tone.Danger => Theme.Danger,
            Tone.Accent => Theme.Accent, _ => Theme.FgMuted
        };
        var fill = tone == Tone.Neutral ? Theme.Surface2 : Theme.Mix(fg, bg, 0.12);
        var border = tone switch
        {
            Tone.Neutral => Theme.Line,
            Tone.Warn => fg, // aviso com borda cheia: não confundir com o accent (regra da base)
            _ => Theme.Mix(fg, bg, 0.35)
        };

        var font = Theme.Sans(7.5f, FontStyle.Bold);
        string up = text.ToUpperInvariant();
        var size = TextRenderer.MeasureText(up, font);
        int w = Math.Min(size.Width + 14, e.CellBounds.Width - 16);
        var r = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 20) / 2, w, 20);
        Theme.FillRound(e.Graphics!, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.RadiusSm, fill, border);
        TextRenderer.DrawText(e.Graphics!, up, font, r, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        e.Handled = true;
    }

    /// <summary>Cabeçalho em caixa alta espaçada.</summary>
    public static void PaintHeader(DataGridView grid, DataGridViewCellPaintingEventArgs e, Color surface)
    {
        var g = e.Graphics!;
        using (var b = new SolidBrush(surface)) g.FillRectangle(b, e.CellBounds);
        using (var p = new Pen(Theme.Line)) g.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        string text = Convert.ToString(e.Value) ?? "";
        if (text.Length > 0)
        {
            int tw = Theme.MeasureEyebrow(g, text);
            bool right = e.ColumnIndex >= 0 &&
                grid.Columns[e.ColumnIndex].DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleRight;
            int x = right ? e.CellBounds.Right - 8 - tw : e.CellBounds.X + 8;
            var old = g.Clip;
            g.SetClip(e.CellBounds);
            Theme.DrawEyebrow(g, text, new Point(x, e.CellBounds.Y + (e.CellBounds.Height - 12) / 2), Theme.FgMuted);
            g.Clip = old;
        }
        e.Handled = true;
    }
}
