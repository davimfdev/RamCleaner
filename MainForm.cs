using System.Drawing.Text;

namespace RamCleaner;

internal class MainForm : Form
{
    private readonly AppConfig _cfg;
    /// <summary>Estatísticas por TargetApp.Id.</summary>
    private readonly Dictionary<string, AppStats> _stats = new();

    private readonly DataGridView _grid = new();
    private readonly FieldBox _interval = new(suffix: "ms", numeric: true);
    private readonly ToggleSwitch _startup = new() { Text = "Iniciar com o Windows" };
    private readonly ToggleSwitch _startMin = new() { Text = "Abrir minimizado na bandeja" };
    private readonly ToggleSwitch _paused = new() { Text = "Pausar" };
    private readonly StatCard _cardRam = new() { Label = "Monitorado" };
    private readonly StatCard _cardFreed = new() { Label = "Liberado na sessão" };
    private readonly StatCard _cardState = new() { Label = "Status", Margin = new Padding(0) };
    private readonly NotifyIcon _tray = new();
    private readonly ToolTip _tip = new();

    private readonly System.Windows.Forms.Timer _trimTimer = new();
    private const int RefreshMs = 2000;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = RefreshMs };

    private bool _busy;
    private bool _pendingTrim;
    private ToolStripMenuItem? _pauseItem;
    private bool _reallyExit;
    private bool _loadingGrid;
    private readonly bool _startHidden;

    // Colunas
    private const string ColEnabled = "enabled", ColName = "name", ColProcs = "procs", ColCount = "count",
        ColRam = "ram", ColPriv = "priv", ColLimit = "limit", ColFreed = "freed", ColTotal = "total", ColInfo = "info";

    public MainForm(AppConfig cfg, bool startHidden)
    {
        _cfg = cfg;
        _startHidden = startHidden;

        Text = "RamCleaner";
        Size = new Size(1140, 680);
        MinimumSize = new Size(920, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Sans(9f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { Icon = SystemIcons.Application; }

        BuildLayout();
        BuildTray();

        _trimTimer.Tick += async (_, _) => await RunCycle(trim: true);
        _refreshTimer.Tick += async (_, _) =>
        {
            // Se a limpeza já roda mais rápido que 2 s, ela mesma atualiza a tela.
            if (!_cfg.Paused && _cfg.IntervalMs <= RefreshMs) return;
            await RunCycle(trim: false);
        };
        ApplyInterval();
        _trimTimer.Start();
        _refreshTimer.Start();

        ReloadGrid();
        _ = RunCycle(trim: false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindow(this);
    }

    // ================================================================ Layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            Padding = new Padding(24, 16, 24, 14),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));   // cabeçalho
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));  // cards
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // tabela
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // rodapé

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildCards(), 0, 1);
        root.Controls.Add(BuildContent(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0), BackColor = Theme.Bg };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        header.Controls.Add(new HeaderBlock
        {
            Eyebrow = "RamCleaner",
            Title = "Limpeza de memória",
            Subtitle = "Esvazia o working set dos apps escolhidos a cada intervalo. A memória volta sob demanda.",
            Dock = DockStyle.Fill
        }, 0, 0);

        var clean = new PillButton { Text = "Limpar agora", Variant = PillButton.Kind.Primary, Height = 38, Margin = new Padding(0, 14, 0, 0) };
        clean.Click += async (_, _) => await RunCycle(trim: true, force: true);
        header.Controls.Add(clean, 1, 0);
        return header;
    }

    private Control BuildCards()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 0, 0, 14), BackColor = Theme.Bg };
        for (int i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        foreach (var c in new[] { _cardRam, _cardFreed, _cardState }) c.Dock = DockStyle.Fill;
        row.Controls.Add(_cardRam, 0, 0);
        row.Controls.Add(_cardFreed, 1, 0);
        row.Controls.Add(_cardState, 2, 0);
        return row;
    }

    private Control BuildContent()
    {
        var surface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 6), Margin = new Padding(0) };

        // ---- barra de ferramentas
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 2, BackColor = Theme.Surface1 };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface1, Margin = new Padding(0) };
        var add = new PillButton { Text = "+ Adicionar" };
        var edit = new PillButton { Text = "Editar" };
        var remove = new PillButton { Text = "Remover", Variant = PillButton.Kind.Danger };
        add.Click += (_, _) => AddApps();
        edit.Click += (_, _) => EditSelected();
        remove.Click += (_, _) => RemoveSelected();
        left.Controls.AddRange(new Control[] { add, edit, remove });

        var right = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
            BackColor = Theme.Surface1, Margin = new Padding(0), Anchor = AnchorStyles.Right
        };
        _interval.Width = 120;
        _interval.Margin = new Padding(0, 0, 18, 0);
        _interval.Input.Text = _cfg.IntervalMs.ToString();
        _interval.Input.Leave += (_, _) => CommitInterval();
        _interval.Input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            CommitInterval();
            e.SuppressKeyPress = true;
        };
        _tip.SetToolTip(_interval.Input, $"Mínimo {AppConfig.MinIntervalMs} ms. 500 = meio segundo, 30000 = 30 s.");

        _paused.Checked = _cfg.Paused;
        _paused.Margin = new Padding(0, 3, 0, 0);
        _paused.CheckedChanged += (_, _) =>
        {
            _cfg.Paused = _paused.Checked;
            _cfg.Save();
            UpdateTrayMenu();
            UpdateGridStats();
        };
        right.Controls.AddRange(new Control[] { new EyebrowLabel("Intervalo"), _interval, _paused });

        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(right, 1, 0);

        var spacer = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Surface1 };

        BuildGrid();

        surface.Controls.Add(_grid);   // Fill (adicionado primeiro = fica por último no dock)
        surface.Controls.Add(spacer);
        surface.Controls.Add(bar);
        return surface;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 10, 0, 0), BackColor = Theme.Bg };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var toggles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
        _startup.Checked = _cfg.StartWithWindows;
        _startup.CheckedChanged += (_, _) =>
        {
            _cfg.StartWithWindows = _startup.Checked;
            try { Startup.Apply(_cfg.StartWithWindows); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível alterar a inicialização:\n" + ex.Message); }
            _cfg.Save();
        };
        _startMin.Checked = _cfg.StartMinimized;
        _startMin.CheckedChanged += (_, _) => { _cfg.StartMinimized = _startMin.Checked; _cfg.Save(); };
        toggles.Controls.AddRange(new Control[] { _startup, _startMin });

        var hint = new Label
        {
            Text = "Fechar no X mantém na bandeja",
            AutoSize = true,
            ForeColor = Theme.FgMuted,
            Font = Theme.Sans(8.5f),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 6, 0, 0)
        };

        footer.Controls.Add(toggles, 0, 0);
        footer.Controls.Add(hint, 1, 0);
        return footer;
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        GridStyle.Apply(_grid, Theme.Surface1);
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.ShowCellToolTips = true;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = ColEnabled, HeaderText = "Ativo", FillWeight = 30, MinimumWidth = 60 });
        AddText(ColName, "Nome", 80, readOnly: false);
        AddText(ColProcs, "Processos", 120, readOnly: true);
        AddText(ColCount, "Proc.", 30, readOnly: true, right: true);
        AddText(ColRam, "RAM", 55, readOnly: true, right: true);
        AddText(ColPriv, "Privada", 55, readOnly: true, right: true);
        AddText(ColLimit, "Limite MB", 48, readOnly: false, right: true);
        AddText(ColFreed, "Liberou", 50, readOnly: true, right: true);
        AddText(ColTotal, "Total", 55, readOnly: true, right: true);
        AddText(ColInfo, "Status", 85, readOnly: true);

        var nameStyle = _grid.Columns[ColName]!.DefaultCellStyle;
        nameStyle.Font = Theme.Sans(9f, FontStyle.Bold);
        nameStyle.ForeColor = nameStyle.SelectionForeColor = Theme.Fg;
        _grid.Columns[ColProcs]!.DefaultCellStyle.ForeColor = Theme.FgMuted;
        _grid.Columns[ColRam]!.DefaultCellStyle.ForeColor = Theme.Fg;
        var totalStyle = _grid.Columns[ColTotal]!.DefaultCellStyle;
        totalStyle.ForeColor = totalStyle.SelectionForeColor = Theme.Accent;
        totalStyle.Font = Theme.Sans(9f, FontStyle.Bold);

        _grid.Columns[ColName]!.ToolTipText = "Clique e digite (ou F2) para renomear";
        _grid.Columns[ColProcs]!.ToolTipText = "Duplo clique para editar os processos deste app";
        _grid.Columns[ColRam]!.ToolTipText = "Working set: o que o Gerenciador de Tarefas mostra";
        _grid.Columns[ColPriv]!.ToolTipText = "Memória privada (commit): o que o app realmente reservou";

        // Commit imediato do interruptor.
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnCellValueChanged;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            string col = _grid.Columns[e.ColumnIndex].Name;
            if (col == ColName || col == ColLimit || col == ColEnabled) return; // essas editam na célula
            EditSelected();
        };
        _grid.CellPainting += OnCellPainting;
        _grid.Paint += OnGridPaint;
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    private void AddText(string name, string header, int weight, bool readOnly, bool right = false)
    {
        var col = new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = header, FillWeight = weight, ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        if (right) col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns.Add(col);
    }

    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex == -1 && e.ColumnIndex >= 0)
        {
            GridStyle.PaintHeader(_grid, e, Theme.Surface1);
            return;
        }
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        string col = _grid.Columns[e.ColumnIndex].Name;
        if (col == ColEnabled)
            GridStyle.PaintSwitchCell(e);
        else if (col == ColInfo)
            GridStyle.PaintBadgeCell(e, _grid.Rows[e.RowIndex].Cells[ColInfo].Tag as GridStyle.Tone? ?? GridStyle.Tone.Neutral);
    }

    /// <summary>Estado vazio: explica o próximo passo em vez de uma tabela em branco.</summary>
    private void OnGridPaint(object? sender, PaintEventArgs e)
    {
        if (_grid.Rows.Count > 0) return;
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var area = new Rectangle(0, _grid.ColumnHeadersHeight, _grid.Width, _grid.Height - _grid.ColumnHeadersHeight);
        var title = new Rectangle(area.X, area.Y + area.Height / 2 - 26, area.Width, 24);
        var sub = new Rectangle(area.X, title.Bottom + 2, area.Width, 20);
        TextRenderer.DrawText(g, "Nenhum app monitorado", Theme.Display(11f), title, Theme.Fg, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "Clique em + Adicionar e escolha o que limpar (Discord, Spotify, navegador...).",
            Theme.Sans(9f), sub, Theme.FgMuted, TextFormatFlags.HorizontalCenter);
    }

    // ================================================================ Bandeja

    private void BuildTray()
    {
        _tray.Icon = Icon;
        _tray.Text = "RamCleaner";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowWindow();
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        if (_tray.ContextMenuStrip == null)
        {
            var menu = new ContextMenuStrip { Renderer = Theme.MenuRenderer(), ShowImageMargin = false };
            menu.Items.Add("Abrir", null, (_, _) => ShowWindow());
            menu.Items.Add("Limpar agora", null, async (_, _) => await RunCycle(trim: true, force: true));
            _pauseItem = new ToolStripMenuItem("", null, (_, _) => _paused.Checked = !_paused.Checked);
            menu.Items.Add(_pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, (_, _) => { _reallyExit = true; Close(); });
            _tray.ContextMenuStrip = menu;
        }
        if (_pauseItem != null) _pauseItem.Text = _cfg.Paused ? "Retomar" : "Pausar";
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    // ================================================================ Grid <-> config

    private void ReloadGrid()
    {
        _loadingGrid = true;
        _grid.Rows.Clear();
        foreach (var t in _cfg.Targets)
        {
            int i = _grid.Rows.Add();
            var row = _grid.Rows[i];
            row.Tag = t;
            row.Cells[ColEnabled].Value = t.Enabled;
            row.Cells[ColName].Value = t.Label;
            row.Cells[ColProcs].Value = string.Join(", ", t.ProcessNames);
            row.Cells[ColLimit].Value = t.ThresholdMB == 0 ? "sempre" : t.ThresholdMB.ToString();
            if (!_stats.ContainsKey(t.Id)) _stats[t.Id] = new AppStats();
        }
        _loadingGrid = false;
        UpdateGridStats();
        _grid.Invalidate();
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loadingGrid || e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not TargetApp t) return;

        string col = _grid.Columns[e.ColumnIndex].Name;
        if (col == ColEnabled)
        {
            t.Enabled = row.Cells[ColEnabled].Value is true;
        }
        else if (col == ColName)
        {
            string n = Convert.ToString(row.Cells[ColName].Value)?.Trim() ?? "";
            if (n.Length > 0) t.DisplayName = n;
            _loadingGrid = true;
            row.Cells[ColName].Value = t.Label;
            _loadingGrid = false;
        }
        else if (col == ColLimit)
        {
            string raw = Convert.ToString(row.Cells[ColLimit].Value)?.Trim() ?? "";
            if (raw.Equals("sempre", StringComparison.OrdinalIgnoreCase)) raw = "0";
            if (int.TryParse(raw, out int mb) && mb >= 0) t.ThresholdMB = mb;
            _loadingGrid = true;
            row.Cells[ColLimit].Value = t.ThresholdMB == 0 ? "sempre" : t.ThresholdMB.ToString();
            _loadingGrid = false;
        }
        else
        {
            return; // colunas de estatística: nada a salvar
        }
        _cfg.Save();
        UpdateGridStats();
    }

    private IEnumerable<string> ProcessesUsedExcept(TargetApp? except) =>
        _cfg.Targets.Where(t => t != except).SelectMany(t => t.ProcessNames);

    private void AddApps()
    {
        using var dlg = new AddProcessForm(null, ProcessesUsedExcept(null));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _cfg.Targets.AddRange(dlg.Result);
        _cfg.Save();
        ReloadGrid();
        _ = RunCycle(trim: false);
    }

    private void EditSelected()
    {
        if (_grid.CurrentRow?.Tag is not TargetApp t) return;
        using var dlg = new AddProcessForm(t, ProcessesUsedExcept(t));
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result.Count == 0) return;

        var r = dlg.Result[0];
        t.DisplayName = r.DisplayName;
        t.ProcessNames = r.ProcessNames;
        t.ThresholdMB = r.ThresholdMB;
        _cfg.Save();
        ReloadGrid();
        _ = RunCycle(trim: false);
    }

    private void RemoveSelected()
    {
        var toRemove = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.Tag as TargetApp).OfType<TargetApp>().ToList();
        if (toRemove.Count == 0) return;
        foreach (var t in toRemove)
        {
            _cfg.Targets.Remove(t);
            _stats.Remove(t.Id);
        }
        _cfg.Save();
        ReloadGrid();
    }

    private void CommitInterval()
    {
        if (int.TryParse(_interval.Input.Text, out int ms))
            _cfg.IntervalMs = Math.Clamp(ms, AppConfig.MinIntervalMs, AppConfig.MaxIntervalMs);
        _interval.Input.Text = _cfg.IntervalMs.ToString();
        ApplyInterval();
        _cfg.Save();
        UpdateGridStats();
    }

    // ================================================================ Ciclo

    private void ApplyInterval() =>
        _trimTimer.Interval = Math.Clamp(_cfg.IntervalMs, AppConfig.MinIntervalMs, AppConfig.MaxIntervalMs);

    /// <summary>Lê RAM de todos os apps e (se trim) limpa os que estão ativos e acima do limite.</summary>
    private async Task RunCycle(bool trim, bool force = false)
    {
        if (trim && _cfg.Paused && !force) trim = false;
        if (_busy)
        {
            // Se cair no meio de outro ciclo, faz a limpeza logo em seguida.
            if (trim) _pendingTrim = true;
            return;
        }
        _busy = true;
        try
        {
            // Cópia para o thread de trabalho não brigar com edições da UI.
            var targets = _cfg.Targets.Select(t => new TargetApp
            {
                Id = t.Id,
                DisplayName = t.DisplayName,
                ProcessNames = t.ProcessNames.ToList(),
                Enabled = t.Enabled,
                ThresholdMB = t.ThresholdMB
            }).ToList();
            var stats = targets.ToDictionary(
                t => t.Id,
                t => _stats.TryGetValue(t.Id, out var s) ? s.Clone() : new AppStats());

            await Task.Run(() =>
            {
                using var snap = new ProcessSnapshot(); // 1 leitura de processos por ciclo
                foreach (var t in targets)
                    Trimmer.Update(t, stats[t.Id], snap, trim);
            });

            foreach (var kv in stats)
            {
                // Ignora apps removidos enquanto o ciclo rodava.
                if (_cfg.Targets.Any(t => t.Id == kv.Key))
                    _stats[kv.Key] = kv.Value;
            }
            UpdateGridStats();
        }
        catch
        {
            // Nunca deixa uma falha derrubar o app residente.
        }
        finally
        {
            _busy = false;
        }

        if (_pendingTrim)
        {
            _pendingTrim = false;
            await RunCycle(trim: true, force: true);
        }
    }

    /// <summary>Só escreve na célula se o valor mudou (evita repintar a tabela à toa a cada 500 ms).</summary>
    private static void Set(DataGridViewCell cell, object value)
    {
        if (!Equals(cell.Value, value)) cell.Value = value;
    }

    private void UpdateGridStats()
    {
        long totalWs = 0, totalFreed = 0;
        int open = 0, procs = 0;
        DateTime? lastTrim = null;

        _loadingGrid = true;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not TargetApp t) continue;
            var s = _stats.GetValueOrDefault(t.Id) ?? new AppStats();
            totalWs += s.WorkingSetBytes;
            totalFreed += s.TotalFreedBytes;
            procs += s.ProcessCount;
            if (s.ProcessCount > 0) open++;
            if (s.LastTrim != null && (lastTrim == null || s.LastTrim > lastTrim)) lastTrim = s.LastTrim;

            Set(row.Cells[ColCount], s.ProcessCount > 0 ? s.ProcessCount.ToString() : "-");
            Set(row.Cells[ColRam], s.ProcessCount > 0 ? Trimmer.FormatMB(s.WorkingSetBytes) : "-");
            Set(row.Cells[ColPriv], s.ProcessCount > 0 ? Trimmer.FormatMB(s.PrivateBytes) : "-");
            Set(row.Cells[ColFreed], s.LastTrim != null ? Trimmer.FormatMB(s.LastFreedBytes) : "-");
            Set(row.Cells[ColTotal], Trimmer.FormatMB(s.TotalFreedBytes));
            row.Cells[ColFreed].ToolTipText = s.LastTrim != null ? $"Última limpeza às {s.LastTrim:HH:mm:ss}" : "";

            (string info, GridStyle.Tone tone) =
                s.ProcessCount == 0 ? ("Fechado", GridStyle.Tone.Neutral) :
                !t.Enabled ? ("Desativado", GridStyle.Tone.Neutral) :
                s.AccessDenied > 0 ? ($"Sem permissão ({s.AccessDenied})", GridStyle.Tone.Danger) :
                t.ThresholdMB > 0 && s.WorkingSetBytes < (long)t.ThresholdMB * 1024 * 1024 ? ("Abaixo do limite", GridStyle.Tone.Accent) :
                _cfg.Paused ? ("Pausado", GridStyle.Tone.Warn) :
                ("Limpando", GridStyle.Tone.Ok);

            var infoCell = row.Cells[ColInfo];
            if (!Equals(infoCell.Tag, tone)) { infoCell.Tag = tone; _grid.InvalidateCell(infoCell); }
            Set(infoCell, info);
            infoCell.ToolTipText = s.AccessDenied > 0 ? "Esse app roda como administrador. Rode o RamCleaner como admin para limpá-lo." : "";
        }
        _loadingGrid = false;

        _cardRam.Value = Trimmer.FormatMB(totalWs);
        _cardRam.Caption = $"{open} de {_cfg.Targets.Count} apps abertos · {procs} processos";

        _cardFreed.Value = Trimmer.FormatMB(totalFreed);
        _cardFreed.ValueColor = Theme.Accent;
        _cardFreed.Caption = lastTrim != null ? $"última limpeza às {lastTrim:HH:mm:ss}" : "nenhuma limpeza ainda";

        string every = AppConfig.FormatInterval(_cfg.IntervalMs);
        _cardState.Value = _cfg.Paused ? "Pausado" : "Ativo";
        _cardState.ValueColor = _cfg.Paused ? Theme.Warn : Theme.Ok;
        _cardState.Caption = _cfg.Paused ? "limpeza automática desligada" : $"limpando a cada {every}";

        string tip = $"RamCleaner - {Trimmer.FormatMB(totalWs)} ({(_cfg.Paused ? "pausado" : "a cada " + every)})";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    // ================================================================ Janela/bandeja

    protected override void SetVisibleCore(bool value)
    {
        // Permite iniciar direto na bandeja sem "piscar" a janela.
        if (_startHidden && !IsHandleCreated)
        {
            CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Botão X só esconde na bandeja; "Sair" no menu da bandeja fecha de verdade.
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trimTimer.Stop();
        _refreshTimer.Stop();
        _cfg.Save();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }
}

/// <summary>Bloco de título: eyebrow dourado + título display + subtítulo (como os headers do FMM/davimf.dev).</summary>
internal class HeaderBlock : Control
{
    public string Eyebrow { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";

    public HeaderBlock()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Margin = new Padding(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        Theme.DrawEyebrow(g, Eyebrow, new Point(1, 6), Theme.Accent, 1.6f);
        TextRenderer.DrawText(g, Title, Theme.Display(17f), new Point(-2, 22), Theme.Fg, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Subtitle, Theme.Sans(9f), new Rectangle(0, 54, Width, 20), Theme.FgMuted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
