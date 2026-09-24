using System.Diagnostics;

namespace Lysma;

/// <summary>
/// Janela para criar ou editar um app monitorado.
/// - Marque os processos na lista (dá pra buscar). Clicar em qualquer ponto da linha marca/desmarca.
/// - "Nome": se preencher, todos os processos marcados viram UM grupo com esse nome.
///   Se deixar vazio (só ao adicionar), cada processo marcado vira uma linha separada.
/// </summary>
internal class AddProcessForm : Form
{
    private readonly ThemedGrid _grid = new();
    private readonly ThinScrollBar _scroll = new();
    private readonly FieldBox _search = new("Buscar processo aberto...");
    private readonly FieldBox _name;
    private readonly FieldBox _threshold = new("0", "MB", numeric: true);
    private readonly Label _hint = new();

    private readonly List<(string name, int count, long ws)> _all = new();
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _editMode;
    private bool _rebuilding;

    /// <summary>Resultado: apps a adicionar (modo adicionar) ou 1 app editado (modo editar).</summary>
    public List<TargetApp> Result { get; } = new();

    /// <param name="editing">null = adicionar novo; senão, edita esse app.</param>
    /// <param name="usedElsewhere">Processos que já estão em outros apps (são escondidos da lista).</param>
    public AddProcessForm(TargetApp? editing, IEnumerable<string> usedElsewhere)
    {
        _editMode = editing != null;
        Text = _editMode ? "Editar app" : "Adicionar apps";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 680);
        MinimumSize = new Size(520, 520);
        Font = Theme.Sans(9f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;
        ShowInTaskbar = false;
        Icon = AppIcon.Window;
        MinimizeBox = false;

        _name = new FieldBox(_editMode ? "ex.: Edge" : "Opcional: ex.: Edge (junta os marcados num grupo só)");

        var hidden = new HashSet<string>(usedElsewhere, StringComparer.OrdinalIgnoreCase);
        LoadProcesses(hidden, editing);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            Padding = new Padding(22, 14, 22, 16),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));  // título
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // busca
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // lista
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176)); // campos + botões

        root.Controls.Add(new HeaderBlock
        {
            Eyebrow = _editMode ? "Editar" : "Adicionar",
            Title = _editMode ? editing!.Label : "Escolher apps",
            Subtitle = "Marque os processos. Apps como Discord e navegadores abrem vários: dá pra juntar num nome só.",
            Dock = DockStyle.Fill
        }, 0, 0);

        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 0, 0, 12);
        _search.Input.TextChanged += (_, _) => RebuildList();
        root.Controls.Add(_search, 0, 1);

        root.Controls.Add(BuildList(), 0, 2);
        root.Controls.Add(BuildBottom(), 0, 3);
        Controls.Add(root);

        if (editing != null)
        {
            _name.Input.Text = editing.DisplayName;
            _threshold.Input.Text = editing.ThresholdMB.ToString();
            foreach (var n in editing.ProcessNames) _checked.Add(n);
        }
        _name.Input.TextChanged += (_, _) => UpdateHint();

        RebuildList();
        UpdateHint();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindow(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _search.Input.Focus();
    }

    // ------------------------------------------------------------ UI

    private Control BuildList()
    {
        var surface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0) };

        _grid.Dock = DockStyle.Fill;
        GridStyle.Apply(_grid, Theme.Surface1);
        _grid.RowTemplate.Height = 36;
        _grid.MultiSelect = false;
        _grid.ReadOnly = false;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "chk", HeaderText = "", FillWeight = 16, MinimumWidth = 54 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Processo", FillWeight = 100, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "count", HeaderText = "Qtd", FillWeight = 22, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ram", HeaderText = "RAM", FillWeight = 34, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns["count"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns["ram"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns["ram"]!.DefaultCellStyle.ForeColor = Theme.Fg;
        var nameStyle = _grid.Columns["name"]!.DefaultCellStyle;
        nameStyle.ForeColor = nameStyle.SelectionForeColor = Theme.Fg;
        _grid.Columns["chk"]!.ReadOnly = true; // o clique é tratado abaixo (linha inteira)

        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0) { GridStyle.PaintHeader(_grid, e, Theme.Surface1); return; }
            if (e.RowIndex >= 0 && e.ColumnIndex == 0) GridStyle.PaintSwitchCell(e);
        };
        _grid.CellClick += (_, e) => { if (e.RowIndex >= 0) ToggleRow(e.RowIndex); };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Space && _grid.CurrentRow != null)
            {
                ToggleRow(_grid.CurrentRow.Index);
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _grid.DataError += (_, e) => e.ThrowException = false;

        _scroll.Dock = DockStyle.Right;
        _scroll.Attach(_grid);
        surface.Controls.Add(_grid);
        surface.Controls.Add(_scroll);
        return surface;
    }

    private Control BuildBottom()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Theme.Bg, Margin = new Padding(0, 14, 0, 0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); // rótulos
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // campos
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); // dica
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // botões

        panel.Controls.Add(new EyebrowLabel("Nome") { Margin = new Padding(2, 4, 0, 0) }, 0, 0);
        panel.Controls.Add(new EyebrowLabel("Limpar acima de") { Margin = new Padding(12, 4, 0, 0) }, 1, 0);

        _name.Dock = DockStyle.Fill;
        _name.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(_name, 0, 1);

        _threshold.Dock = DockStyle.Fill;
        _threshold.Margin = new Padding(12, 0, 0, 6);
        new ToolTip().SetToolTip(_threshold.Input, "0 = limpa sempre, sem limite mínimo");
        panel.Controls.Add(_threshold, 1, 1);

        _hint.Dock = DockStyle.Fill;
        _hint.ForeColor = Theme.FgMuted;
        _hint.Font = Theme.Sans(8.5f);
        _hint.Margin = new Padding(2, 4, 0, 0);
        panel.Controls.Add(_hint, 0, 2);
        panel.SetColumnSpan(_hint, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0), WrapContents = false };
        var ok = new PillButton { Text = _editMode ? "Salvar" : "Adicionar", Variant = PillButton.Kind.Primary, Margin = new Padding(8, 0, 0, 0) };
        var cancel = new PillButton { Text = "Cancelar", Variant = PillButton.Kind.Ghost, DialogResult = DialogResult.Cancel, Margin = new Padding(0) };
        ok.Click += (_, _) => OnOk();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        panel.Controls.Add(buttons, 0, 3);
        panel.SetColumnSpan(buttons, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        return panel;
    }

    // ------------------------------------------------------------ dados

    private void LoadProcesses(HashSet<string> hidden, TargetApp? editing)
    {
        var groups = new Dictionary<string, (int count, long ws)>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0 || p.Id == 4 || p.Id == self) continue;
                var g = groups.GetValueOrDefault(p.ProcessName);
                groups[p.ProcessName] = (g.count + 1, g.ws + p.WorkingSet64);
            }
            catch { }
            finally { p.Dispose(); }
        }

        // No modo editar, mostra também os processos do grupo que estão fechados agora.
        if (editing != null)
            foreach (var n in editing.ProcessNames)
                if (!groups.ContainsKey(n)) groups[n] = (0, 0);

        foreach (var kv in groups.OrderByDescending(k => k.Value.ws))
        {
            if (hidden.Contains(kv.Key)) continue;
            _all.Add((kv.Key, kv.Value.count, kv.Value.ws));
        }
    }

    private void RebuildList()
    {
        _rebuilding = true;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        string f = _search.Input.Text.Trim();
        foreach (var (name, count, ws) in _all)
        {
            // Marcados sempre aparecem, mesmo fora do filtro.
            bool isChecked = _checked.Contains(name);
            if (f.Length > 0 && !isChecked && !name.Contains(f, StringComparison.OrdinalIgnoreCase)) continue;
            int i = _grid.Rows.Add(isChecked, name, count > 0 ? count.ToString() : "-", count > 0 ? Trimmer.FormatMB(ws) : "fechado");
            _grid.Rows[i].Tag = name;
        }
        _grid.ResumeLayout();
        _rebuilding = false;
    }

    private void ToggleRow(int index)
    {
        if (_rebuilding || index < 0 || index >= _grid.Rows.Count) return;
        var row = _grid.Rows[index];
        var n = (string)row.Tag!;
        bool now = !_checked.Contains(n);
        if (now) _checked.Add(n); else _checked.Remove(n);
        row.Cells["chk"].Value = now;
        _grid.InvalidateRow(index);
        UpdateHint();
    }

    private void UpdateHint()
    {
        int n = _checked.Count;
        string name = _name.Input.Text.Trim();
        if (n == 0)
            _hint.Text = "Nenhum processo marcado ainda.";
        else if (_editMode || name.Length > 0 || n == 1)
            _hint.Text = $"Vai virar 1 linha \"{(name.Length > 0 ? name : _checked.First())}\" com {n} processo(s): {string.Join(", ", _checked)}";
        else
            _hint.Text = $"Sem nome: vão ser {n} linhas separadas. Preencha o nome para juntar num grupo só.";
    }

    private void OnOk()
    {
        if (_checked.Count == 0)
        {
            MessageBox.Show(this, "Marque pelo menos um processo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string name = _name.Input.Text.Trim();
        int limit = int.TryParse(_threshold.Input.Text, out int mb) ? Math.Clamp(mb, 0, 1_048_576) : 0;
        // Mantém a ordem da lista (maior RAM primeiro).
        var procs = _all.Select(a => a.name).Where(_checked.Contains).ToList();

        if (_editMode || name.Length > 0 || procs.Count == 1)
        {
            Result.Add(new TargetApp
            {
                DisplayName = name.Length > 0 ? name : procs[0],
                ProcessNames = procs,
                ThresholdMB = limit
            });
        }
        else
        {
            foreach (var p in procs)
                Result.Add(new TargetApp { DisplayName = p, ProcessNames = new() { p }, ThresholdMB = limit });
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
