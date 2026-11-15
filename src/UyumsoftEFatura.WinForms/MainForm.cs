using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    private readonly CheckBox _chkRemember = new() { Text = "Şifreyi hatırla", AutoSize = true, Margin = new Padding(8, 6, 3, 3) };
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
    private readonly CheckBox _chkPdf = new() { Text = "PDF de indir", AutoSize = true, Margin = new Padding(6, 6, 3, 3) };
    private readonly CheckBox _chkSkip = new() { Text = "İndirilmişleri atla", AutoSize = true, Checked = true, Margin = new Padding(6, 6, 3, 3) };
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
    private readonly ToolStripStatusLabel _lblEnv = new() { Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.White, Padding = new Padding(6, 0, 6, 0) };
    private readonly ToolStripProgressBar _progress = new() { Width = 200, Visible = false };

    private List<InvoiceRow> _allRows = [];
    private List<InvoiceRow> _viewRows = [];
    private bool _lastListWasInbox = true;
    private AppSettings _settings = new();
    private int _currentEnv; // 0 = Test, 1 = Canlı
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
        LoadSettings();
        WireEvents();
        UpdateEnvironmentIndicator();
    }

    private bool IsProd => _currentEnv == 1;

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
                Lbl("Ortam:"), _cmbEnv, Lbl("Kullanıcı:"), _txtUser, Lbl("Şifre:"), _txtPass, _chkRemember, _btnTest),
            Group("Sorgu",
                Lbl("Başlangıç:"), _dtFrom, Lbl("Bitiş:"), _dtTo, boxPanel, _btnList,
                Lbl("   Filtre:"), _txtFilter),
            Group("İndirme",
                Lbl("Klasör:"), _txtFolder, _btnBrowse, _chkPdf, _chkSkip,
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
        status.Items.AddRange(new ToolStripItem[] { _lblEnv, _lblStatus, _lblTotal, _progress });

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
        _cmbEnv.SelectedIndexChanged += (_, _) => SwitchEnvironment();
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
        return new UyumsoftClient(_txtUser.Text.Trim(), _txtPass.Text, production: IsProd);
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
        bool skip = _chkSkip.Checked;
        // Test faturaları gerçek faturalarla karışmasın: test ortamı ayrı alt klasöre iner
        var dir = IsProd
            ? Path.Combine(_txtFolder.Text, inbox ? "Gelen" : "Giden")
            : Path.Combine(_txtFolder.Text, "TEST", inbox ? "Gelen" : "Giden");

        await RunBusyAsync($"{rows.Count} fatura indiriliyor...", async ct =>
        {
            await using var client = CreateClient();
            _progress.Maximum = rows.Count;
            _progress.Value = 0;
            int ok = 0, skipped = 0;
            var errors = new List<string>();
            string? lastPath = null;

            foreach (var row in rows)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var result = await client.DownloadInvoiceAsync(inbox, row.Ettn, row.InvoiceNo, dir, pdf, skip);
                    lastPath = result.XmlPath;
                    if (result.Skipped) skipped++; else ok++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{row.InvoiceNo}: {UyumsoftClient.DescribeError(ex)}");
                }
                _progress.Value++;
                SetStatus($"İndiriliyor {_progress.Value}/{rows.Count} — {row.InvoiceNo}");
            }

            var msg = $"{ok} fatura indirildi" + (skipped > 0 ? $", {skipped} tanesi zaten vardı (atlandı)" : "") + $" → {dir}";
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
            var text = UyumsoftClient.DescribeError(ex);
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

    // ---- Ortam değişimi ----

    private void SwitchEnvironment()
    {
        if (_cmbEnv.SelectedIndex == _currentEnv) return;
        StoreCredentials(_currentEnv);           // eski ortamın bilgilerini sakla
        _currentEnv = _cmbEnv.SelectedIndex;
        RestoreCredentials(_currentEnv);         // yeni ortamın bilgilerini getir

        // Liste diğer ortama ait olduğu için temizlenir
        _allRows = [];
        ApplyView();
        UpdateEnvironmentIndicator();
        SetStatus(IsProd ? "CANLI ortama geçildi. İşlemler gerçek faturalar üzerinde yapılır." : "Test ortamına geçildi.");
    }

    private void UpdateEnvironmentIndicator()
    {
        Text = IsProd ? "Uyumsoft e-Fatura İndirici — CANLI ORTAM" : "Uyumsoft e-Fatura İndirici — Test Ortamı";
        _lblEnv.Text = IsProd ? "CANLI" : "TEST";
        _lblEnv.BackColor = IsProd ? Color.Firebrick : Color.SteelBlue;
        _cmbEnv.BackColor = IsProd ? Color.MistyRose : SystemColors.Window;
    }

    // ---- Ayarlar ----

    private sealed class EnvCredentials
    {
        public string? User { get; set; }
        public string? ProtectedPassword { get; set; } // Windows DPAPI ile şifrelenmiş (yalnızca bu Windows kullanıcısı çözebilir)
    }

    private sealed class AppSettings
    {
        public int Env { get; set; }
        public EnvCredentials Test { get; set; } = new();
        public EnvCredentials Prod { get; set; } = new();
        public bool RememberPassword { get; set; }
        public string? Folder { get; set; }
        public bool Pdf { get; set; }
        public bool SkipExisting { get; set; } = true;
    }

    private EnvCredentials Creds(int env) => env == 1 ? _settings.Prod : _settings.Test;

    private void StoreCredentials(int env)
    {
        var c = Creds(env);
        c.User = _txtUser.Text.Trim();
        c.ProtectedPassword = _chkRemember.Checked && _txtPass.Text.Length > 0 ? Protect(_txtPass.Text) : null;
        _sessionPasswords[env] = _txtPass.Text; // program açıkken ortamlar arası geçişte şifre kaybolmasın
    }

    private void RestoreCredentials(int env)
    {
        var c = Creds(env);
        _txtUser.Text = c.User ?? "";
        _txtPass.Text = _sessionPasswords.TryGetValue(env, out var p) && p.Length > 0
            ? p
            : Unprotect(c.ProtectedPassword) ?? "";
    }

    private readonly Dictionary<int, string> _sessionPasswords = new();

    private static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    private void LoadSettings()
    {
        _txtFolder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "e-Faturalar");
        try
        {
            if (File.Exists(SettingsPath))
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { _settings = new(); /* bozuk ayar dosyası yok sayılır */ }

        _currentEnv = _settings.Env is 0 or 1 ? _settings.Env : 0;
        _cmbEnv.SelectedIndex = _currentEnv;
        _chkRemember.Checked = _settings.RememberPassword;
        if (!string.IsNullOrWhiteSpace(_settings.Folder)) _txtFolder.Text = _settings.Folder;
        _chkPdf.Checked = _settings.Pdf;
        _chkSkip.Checked = _settings.SkipExisting;
        RestoreCredentials(_currentEnv);
    }

    private void SaveSettings()
    {
        try
        {
            StoreCredentials(_currentEnv);
            if (!_chkRemember.Checked)
            {
                _settings.Test.ProtectedPassword = null;
                _settings.Prod.ProtectedPassword = null;
            }
            _settings.Env = _currentEnv;
            _settings.RememberPassword = _chkRemember.Checked;
            _settings.Folder = _txtFolder.Text;
            _settings.Pdf = _chkPdf.Checked;
            _settings.SkipExisting = _chkSkip.Checked;

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
