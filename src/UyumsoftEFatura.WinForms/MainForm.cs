using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace UyumsoftEFatura.WinForms;

/// <summary>
/// Uyumsoft e-Fatura: tarih aralığı seçip gelen/giden faturaları listeleme ve XML/PDF indirme.
/// </summary>
public sealed class MainForm : Form
{
    // --- Bağlantı ---
    private readonly TextBox _txtUser = new() { Width = 140 };
    private readonly TextBox _txtPass = new() { Width = 140, UseSystemPasswordChar = true };
    private readonly ComboBox _cmbEnv = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _btnTest = new() { Text = "Bağlantıyı Test Et", AutoSize = true };

    // --- Sorgu ---
    private readonly DateTimePicker _dtFrom = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _dtTo = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly RadioButton _rbInbox = new() { Text = "Gelen", Checked = true, AutoSize = true };
    private readonly RadioButton _rbOutbox = new() { Text = "Giden", AutoSize = true };
    private readonly Button _btnList = new() { Text = "Listele", AutoSize = true };
    private readonly TextBox _txtFilter = new() { Width = 200, PlaceholderText = "Fatura no / unvan / VKN ara..." };

    // --- İndirme ---
    private readonly TextBox _txtFolder = new() { Width = 280 };
    private readonly Button _btnBrowse = new() { Text = "...", Width = 30 };
    private readonly CheckBox _chkPdf = new() { Text = "PDF de indir", AutoSize = true };
    private readonly Button _btnDownloadSelected = new() { Text = "Seçilenleri İndir", AutoSize = true };
    private readonly Button _btnDownloadAll = new() { Text = "Tümünü İndir", AutoSize = true };
    private readonly Button _btnOpenFolder = new() { Text = "Klasörü Aç", AutoSize = true };
    private readonly Button _btnCancel = new() { Text = "Durdur", AutoSize = true, Enabled = false };

    // --- Liste ve durum ---
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        AutoGenerateColumns = true,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        BackgroundColor = SystemColors.Window,
    };
    private readonly ToolStripStatusLabel _lblStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Hazır" };
    private readonly ToolStripStatusLabel _lblTotal = new() { Text = "" };
    private readonly ToolStripProgressBar _progress = new() { Width = 200, Visible = false };

    private List<InvoiceRow> _allRows = [];
    private List<InvoiceRow> _viewRows = [];
    private bool _lastListWasInbox = true;
    private string? _sortProperty;
    private bool _sortAscending = true;
    private CancellationTokenSource? _cts;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UyumsoftEFatura", "ayarlar.json");

    public MainForm()
    {
        Text = "Uyumsoft e-Fatura İndirici";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 600);
        Size = new Size(1280, 760);
        Font = new Font("Segoe UI", 9F);

        _cmbEnv.Items.AddRange(new object[] { "Test", "Canlı" });
        _cmbEnv.SelectedIndex = 0;
        var today = DateTime.Today;
        _dtFrom.Value = new DateTime(today.Year, today.Month, 1);
        _dtTo.Value = today;

        BuildLayout();
        WireEvents();
        LoadSettings();
    }

    // =====================================================================
    // Arayüz
    // =====================================================================

    private void BuildLayout()
    {
        var boxPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(12, 3, 3, 3) };
        boxPanel.Controls.AddRange(new Control[] { _rbInbox, _rbOutbox });

        var groups = new[]
        {
            Group("Bağlantı",
                Lbl("Kullanıcı:"), _txtUser, Lbl("Şifre:"), _txtPass, Lbl("Ortam:"), _cmbEnv, _btnTest),
            Group("Sorgu",
                Lbl("Başlangıç:"), _dtFrom, Lbl("Bitiş:"), _dtTo, boxPanel, _btnList,
                Lbl("   Filtre:"), _txtFilter),
            Group("İndirme",
                Lbl("Klasör:"), _txtFolder, _btnBrowse, _chkPdf,
                _btnDownloadSelected, _btnDownloadAll, _btnCancel, _btnOpenFolder),
        };

        var top = new Panel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(8, 6, 8, 4),
            Height = groups.Sum(g => g.Height) + 10,
        };
        // Dock=Top kontrolleri ters sırada eklenir ki ekranda doğru sırada görünsün
        foreach (var g in groups.Reverse()) top.Controls.Add(g);

        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { _lblStatus, _lblTotal, _progress });

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 4) };
        gridHost.Controls.Add(_grid);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Seçilenleri indir", null, async (_, _) => await DownloadAsync(SelectedRows()));
        menu.Items.Add("ETTN kopyala", null, (_, _) => CopyEttn());
        _grid.ContextMenuStrip = menu;

        // Sıra önemli: Fill olan en son eklenir
        Controls.Add(gridHost);
        Controls.Add(top);
        Controls.Add(status);
        AcceptButton = _btnList;
    }

    private static GroupBox Group(string title, params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true, // pencere daraltılırsa yatay kaydırma çubuğu çıkar
            Padding = new Padding(2, 0, 2, 0),
        };
        flow.Controls.AddRange(controls);
        var gb = new GroupBox { Text = title, Dock = DockStyle.Top, Height = 62, Padding = new Padding(6, 3, 6, 3) };
        gb.Controls.Add(flow);
        return gb;
    }

    private static Label Lbl(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(6, 7, 0, 0),
    };

    private void WireEvents()
    {
        _btnTest.Click += async (_, _) => await TestConnectionAsync();
        _btnList.Click += async (_, _) => await ListAsync();
        _btnDownloadSelected.Click += async (_, _) => await DownloadAsync(SelectedRows());
        _btnDownloadAll.Click += async (_, _) => await DownloadAsync(_viewRows);
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        _btnBrowse.Click += (_, _) => BrowseFolder();
        _btnOpenFolder.Click += (_, _) => OpenFolder();
        _txtFilter.TextChanged += (_, _) => ApplyView();
        _grid.ColumnHeaderMouseClick += (_, e) => SortBy(_grid.Columns[e.ColumnIndex].DataPropertyName);
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _viewRows.Count)
                await DownloadAsync([_viewRows[e.RowIndex]], openAfter: true);
        };
        _grid.DataBindingComplete += (_, _) => FormatGrid();
        FormClosing += (_, _) => SaveSettings();
    }

    private void FormatGrid()
    {
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            col.SortMode = DataGridViewColumnSortMode.Programmatic;
            if (col.ValueType == typeof(decimal))
            {
                col.DefaultCellStyle.Format = "N2";
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            else if (col.ValueType == typeof(DateTime?) || col.ValueType == typeof(DateTime))
            {
                col.DefaultCellStyle.Format = "dd.MM.yyyy";
            }
            col.HeaderCell.SortGlyphDirection = col.DataPropertyName == _sortProperty
                ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;
        }
    }

    // =====================================================================
    // İşlemler
    // =====================================================================

    private UyumsoftClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_txtUser.Text) || string.IsNullOrEmpty(_txtPass.Text))
            throw new UyumsoftException("Kullanıcı adı ve şifre girin.");
        return new UyumsoftClient(_txtUser.Text.Trim(), _txtPass.Text, production: _cmbEnv.SelectedIndex == 1);
    }

    private async Task TestConnectionAsync()
    {
        await RunBusyAsync("Bağlanılıyor...", async _ =>
        {
            await using var client = CreateClient();
            var info = await client.WhoAmIAsync();
            SetStatus($"Bağlantı başarılı: {info.Customer?.Name} ({info.Customer?.VkTckNo})");
            MessageBox.Show(this, $"Bağlantı başarılı.\n\n{info.Customer?.Name}\nVKN/TCKN: {info.Customer?.VkTckNo}",
                "Uyumsoft", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private async Task ListAsync()
    {
        if (_dtFrom.Value.Date > _dtTo.Value.Date)
        {
            MessageBox.Show(this, "Başlangıç tarihi bitiş tarihinden sonra olamaz.", "Uyarı",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool inbox = _rbInbox.Checked;
        var from = _dtFrom.Value.Date;
        var to = _dtTo.Value.Date.AddDays(1).AddTicks(-1);

        await RunBusyAsync("Faturalar listeleniyor...", async ct =>
        {
            await using var client = CreateClient();
            var progress = new Progress<string>(SetStatus);
            var rows = await client.GetAllInvoiceRowsAsync(inbox, from, to, 100, progress, ct);
            _allRows = rows;
            _lastListWasInbox = inbox;
            ApplyView();
            SetStatus($"{(inbox ? "Gelen" : "Giden")} faturalar: {from:dd.MM.yyyy} - {to:dd.MM.yyyy}, {rows.Count} kayıt");
        });
    }

    private async Task DownloadAsync(IReadOnlyList<InvoiceRow> rows, bool openAfter = false)
    {
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "İndirilecek fatura yok. Önce listeleyip satır seçin.", "Bilgi",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(_txtFolder.Text))
        {
            BrowseFolder();
            if (string.IsNullOrWhiteSpace(_txtFolder.Text)) return;
        }

        bool inbox = _lastListWasInbox;
        bool pdf = _chkPdf.Checked;
        var dir = Path.Combine(_txtFolder.Text, inbox ? "Gelen" : "Giden");

        await RunBusyAsync($"{rows.Count} fatura indiriliyor...", async ct =>
        {
            await using var client = CreateClient();
            _progress.Maximum = rows.Count;
            _progress.Value = 0;
            int ok = 0;
            var errors = new List<string>();
            string? lastPath = null;

            foreach (var row in rows)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    lastPath = await client.DownloadInvoiceAsync(inbox, row.Ettn, row.InvoiceNo, dir, pdf);
                    ok++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{row.InvoiceNo}: {ex.Message}");
                }
                _progress.Value++;
                SetStatus($"İndiriliyor {_progress.Value}/{rows.Count} — {row.InvoiceNo}");
            }

            var msg = $"{ok} fatura indirildi → {dir}";
            if (ct.IsCancellationRequested) msg += " (kullanıcı tarafından durduruldu)";
            SetStatus(msg);

            if (errors.Count > 0)
                MessageBox.Show(this, msg + $"\n\n{errors.Count} hata:\n" + string.Join("\n", errors.Take(15)),
                    "İndirme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (openAfter && lastPath is not null)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{lastPath}\"") { UseShellExecute = true });
            else if (rows.Count > 1)
                MessageBox.Show(this, msg, "İndirme", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    /// <summary>Butonları kilitler, hataları kullanıcıya gösterir.</summary>
    private async Task RunBusyAsync(string message, Func<CancellationToken, Task> work)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        SetStatus(message);
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("İşlem durduruldu.");
        }
        catch (Exception ex)
        {
            var text = ex is UyumsoftException or System.ServiceModel.FaultException
                ? ex.Message
                : ex.GetBaseException().Message;
            SetStatus("Hata: " + text);
            MessageBox.Show(this, text, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        foreach (var b in new Control[] { _btnTest, _btnList, _btnDownloadSelected, _btnDownloadAll, _rbInbox, _rbOutbox, _dtFrom, _dtTo, _cmbEnv })
            b.Enabled = !busy;
        _btnCancel.Enabled = busy;
        _progress.Visible = busy;
        if (!busy) _progress.Value = 0;
    }

    private void SetStatus(string text) => _lblStatus.Text = text;

    // =====================================================================
    // Liste: filtre, sıralama, seçim
    // =====================================================================

    private void ApplyView()
    {
        IEnumerable<InvoiceRow> q = _allRows;
        var f = _txtFilter.Text.Trim();
        if (f.Length > 0)
        {
            q = q.Where(r =>
                r.InvoiceNo.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.Title.Contains(f, StringComparison.CurrentCultureIgnoreCase) ||
                r.TaxId.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.Ettn.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        if (_sortProperty is not null)
        {
            var prop = TypeDescriptor.GetProperties(typeof(InvoiceRow))[_sortProperty];
            if (prop is not null)
                q = _sortAscending ? q.OrderBy(r => prop.GetValue(r)) : q.OrderByDescending(r => prop.GetValue(r));
        }

        _viewRows = q.ToList();
        _grid.DataSource = new BindingList<InvoiceRow>(_viewRows);

        var totals = _viewRows.GroupBy(r => r.Currency)
            .Select(g => $"{g.Sum(r => r.PayableAmount):N2} {g.Key}");
        _lblTotal.Text = _viewRows.Count == 0 ? "" : $"{_viewRows.Count} fatura | Toplam: {string.Join(" + ", totals)}";
    }

    private void SortBy(string property)
    {
        if (_sortProperty == property) _sortAscending = !_sortAscending;
        else { _sortProperty = property; _sortAscending = true; }
        ApplyView();
    }

    private List<InvoiceRow> SelectedRows() =>
        _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as InvoiceRow)
            .Where(r => r is not null)
            .Cast<InvoiceRow>()
            .ToList();

    private void CopyEttn()
    {
        var ettns = SelectedRows().Select(r => r.Ettn).ToList();
        if (ettns.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, ettns));
    }

    // =====================================================================
    // Klasör ve ayarlar
    // =====================================================================

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Faturaların kaydedileceği klasörü seçin",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_txtFolder.Text) ? _txtFolder.Text : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtFolder.Text = dlg.SelectedPath;
    }

    private void OpenFolder()
    {
        if (Directory.Exists(_txtFolder.Text))
            Process.Start(new ProcessStartInfo(_txtFolder.Text) { UseShellExecute = true });
    }

    private sealed record Settings(string? User, int Env, string? Folder, bool Pdf);

    private void LoadSettings()
    {
        _txtFolder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "e-Faturalar");
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
            if (s is null) return;
            _txtUser.Text = s.User ?? "";
            _cmbEnv.SelectedIndex = s.Env is 0 or 1 ? s.Env : 0;
            if (!string.IsNullOrWhiteSpace(s.Folder)) _txtFolder.Text = s.Folder;
            _chkPdf.Checked = s.Pdf;
        }
        catch { /* bozuk ayar dosyası yok sayılır */ }
    }

    private void SaveSettings()
    {
        // Şifre güvenlik nedeniyle kaydedilmez.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new Settings(_txtUser.Text, _cmbEnv.SelectedIndex, _txtFolder.Text, _chkPdf.Checked)));
        }
        catch { }
    }
}
