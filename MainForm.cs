using System.Diagnostics;
using System.Text.Json;

namespace Divoom.HardwareMonitor;

public sealed class MainForm : Form
{
    private readonly MonitorService _monitor = new();
    private readonly DivoomTransport _transport = new();
    private readonly SendingService _sender;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly NotifyIcon _tray;
    private readonly ComboBox _gpu = Combo(330), _disk = Combo(330), _devices = Combo(280), _lcd = Combo(85);
    private readonly TextBox _ip = new() { Width = 150, PlaceholderText = "192.168.1.100" };
    private readonly NumericUpDown _hardware = new() { Width = 90, Minimum = 0, Maximum = int.MaxValue };
    private readonly NumericUpDown _deviceId = new() { Width = 130, Minimum = 0, Maximum = int.MaxValue };
    private readonly TextBox _localToken = new() { Width = 160, UseSystemPasswordChar = true, PlaceholderText = "可选：本地令牌" };
    private readonly Label _endpoint = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly CheckBox _timeGate = new() { Text = "TimeGate 五屏", AutoSize = true };
    private readonly CheckBox _independent = new() { Text = "LCD 独立模式", AutoSize = true };
    private readonly Button _start = Button("开始发送", 110), _discover = Button("刷新设备", 100);
    private readonly TextBox _status = new() { Name = "connection-status", Text = Localizer.Get("本机监控启动中 · 尚未发送"), Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Color.FromArgb(38, 95, 135) };
    private readonly ComboBox _smartctl = Combo(330);
    private readonly CheckBox _plugins = new() { Text = "启用 providers 目录中的 SDK 扩展（需要自行提供授权适配器）", AutoSize = true };
    private readonly TextBox _smartctlPath = new() { Width = 580, PlaceholderText = "smartctl.exe" };
    private readonly TextBox _diagnostics = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new("Microsoft YaHei UI", 10) };
    private readonly DataGridView _diskGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly MetricCard _cpuCard = new("CPU"), _gpuCard = new("GPU"), _memoryCard = new("内存"), _diskCard = new("硬盘");
    private string _gpuSignature = "", _diskSignature = "", _lastDiagnostic = "";
    private bool _exit, _filling, _cleanedUp, _closing;
    private record Choice(string Id, string Key, string Name) { public override string ToString() => Name; }

    public MainForm()
    {
        Text = "Divoom PC 硬件监控 v" + typeof(MainForm).Assembly.GetName().Version?.ToString(3);
        Font = new("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(244, 247, 251);
        ClientSize = new(1100, 780);
        MinimumSize = new(960, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        _sender = new(_transport, () => _monitor.Current);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.Absolute, 58)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 28));
        var title = new Label { Text = "Divoom  /  PC 硬件监控", Dock = DockStyle.Fill, Font = new("Microsoft YaHei UI", 23, FontStyle.Bold), ForeColor = Color.FromArgb(24, 39, 64), TextAlign = ContentAlignment.MiddleLeft };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(MonitorPage()); tabs.TabPages.Add(SettingsPage()); tabs.TabPages.Add(DiagnosticPage());
        root.Controls.Add(title, 0, 0); root.Controls.Add(tabs, 0, 1); root.Controls.Add(new Label { Text = "本机监控独立运行；关闭窗口后保持后台运行。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray }, 0, 2);
        Controls.Add(root);
        _status.TextChanged += (_, _) =>
        {
            string normalized = _status.Text.ReplaceLineEndings("\r\n");
            if (_status.Text != normalized) _status.Text = normalized;
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示监控面板", null, (_, _) => RestoreWindow());
        menu.Items.Add("开始 / 停止发送", null, async (_, _) => { RestoreWindow(); await ToggleSending(); });
        menu.Items.Add("退出", null, (_, _) => { _exit = true; Close(); });
        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Divoom PC 硬件监控", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _ip.Text = _settings.DeviceIp; _timeGate.Checked = _settings.DeviceHardware is 400 or 402; _independent.Checked = _settings.LcdIndependence == 1;
        _hardware.Value = Math.Max(0, _settings.DeviceHardware);
        _deviceId.Value = Math.Max(0, _settings.DeviceId);
        _ip.TextChanged += (_, _) => { _deviceId.Value = 0; _localToken.Clear(); };
        void UpdateEndpoint()
        {
            var uri = new DeviceTarget("127.0.0.1", 0, 0, 0, (int)_hardware.Value).PostUri;
            _endpoint.Text = $"端口 {uri.Port} {uri.AbsolutePath}";
        }
        _hardware.ValueChanged += (_, _) => { UpdateEndpoint(); _timeGate.Checked = (int)_hardware.Value is 400 or 402; };
        UpdateEndpoint();
        _lcd.Items.AddRange(new object[] { "LCD 1", "LCD 2", "LCD 3", "LCD 4", "LCD 5" }); _lcd.SelectedIndex = Math.Clamp(_settings.LcdIndex, 0, 4);
        _lcd.Enabled = _timeGate.Checked; _independent.Enabled = _timeGate.Checked;
        _smartctl.SelectedIndex = Math.Clamp((int)_settings.SmartctlMode, 0, 2); _smartctlPath.Text = _settings.SmartctlPath; _plugins.Checked = _settings.EnableExternalProviders;
        _timeGate.CheckedChanged += (_, _) => { _lcd.Enabled = _timeGate.Checked; _independent.Enabled = _timeGate.Checked; if (_timeGate.Checked && (int)_hardware.Value is not (400 or 402)) _hardware.Value = 400; else if (!_timeGate.Checked) _lcd.SelectedIndex = 0; };
        _gpu.SelectedIndexChanged += (_, _) => { if (!_filling) { SaveSelections(); if (_monitor.Current is { } s) Display(s); } };
        _disk.SelectedIndexChanged += (_, _) => { if (!_filling) { SaveSelections(); if (_monitor.Current is { } s) Display(s); } };
        _discover.Click += async (_, _) => await Discover();
        _devices.SelectedIndexChanged += async (_, _) => await SelectDevice();
        _start.Click += async (_, _) => await ToggleSending();
        _monitor.Updated += s => OnUi(() => Display(s));
        _sender.StatusChanged += message => OnUi(() => _status.Text = message);
        Shown += (_, _) => _monitor.Start();
        FormClosing += OnClosing;
        ApplyLanguage(this);
        foreach (ToolStripItem item in menu.Items) item.Text = Localizer.Ui(item.Text ?? "");
        _tray.Text = Localizer.Ui(_tray.Text);
    }
    private static void ApplyLanguage(Control control)
    {
        if (control is (Label or System.Windows.Forms.Button or TabPage or GroupBox or CheckBox or Form) && control.Tag as string is not ("HardwareName" or "RawDiagnostic")) control.Text = Localizer.Ui(control.Text);
        if (control is TextBox box && !box.ReadOnly) box.PlaceholderText = Localizer.Ui(box.PlaceholderText);
        if (control is TextBox { Name: "connection-status" }) control.Text = Localizer.Ui(control.Text);
        if (control is DataGridView grid) foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = Localizer.Ui(column.HeaderText);
        foreach (Control child in control.Controls) ApplyLanguage(child);
    }
    private static ComboBox Combo(int width) => new() { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
    private static Button Button(string text, int width = 150) => new() { Text = text, Width = width, Height = 34, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new(0, 34), Padding = new(8, 0, 8, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new(4) };
    private static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(0, 5, 0, 3), WrapContents = true };
        foreach (var child in children) { if (child is Label) child.Margin = new(4, 7, 4, 0); else child.Margin = new(4, 3, 4, 3); row.Controls.Add(child); }
        return row;
    }
    private TabPage MonitorPage()
    {
        var page = new TabPage("实时监控") { BackColor = BackColor, Padding = new(8) };
        // Bounded rows keep the disk table visible. Never combine an auto-sized
        // connection panel with a percentage-height sibling: their preferred sizes cycle.
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 3 };
        body.RowStyles.Add(new(SizeType.Absolute, 164)); body.RowStyles.Add(new(SizeType.Percent, 100)); body.RowStyles.Add(new(SizeType.Absolute, 96));
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4, RowCount = 1 };
        for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new(SizeType.Percent, 25));
        cards.Controls.Add(_cpuCard, 0, 0); cards.Controls.Add(_gpuCard, 1, 0); cards.Controls.Add(_memoryCard, 2, 0); cards.Controls.Add(_diskCard, 3, 0);
        body.Controls.Add(cards, 0, 0);

        Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new(3), ForeColor = Color.FromArgb(45, 58, 79) };
        TableLayoutPanel Line(params Control[] controls)
        {
            var line = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = controls.Length, RowCount = 1 };
            foreach (var control in controls)
            {
                line.ColumnStyles.Add(new(SizeType.Percent, 100f / controls.Length));
                control.Margin = new(3);
                if (control is TextBox or ComboBox or NumericUpDown) control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                else { control.Dock = DockStyle.Fill; control.AutoSize = false; }
                line.Controls.Add(control);
            }
            return line;
        }
        TableLayoutPanel Field(string label, Control input)
        {
            var line = Line(Caption(label), input);
            line.ColumnStyles.Clear(); line.ColumnStyles.Add(new(SizeType.Absolute, 105)); line.ColumnStyles.Add(new(SizeType.Percent, 100));
            return line;
        }
        var lower = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
        lower.ColumnStyles.Add(new(SizeType.Percent, 100)); lower.ColumnStyles.Add(new(SizeType.Absolute, 400));
        var storage = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new(12), Margin = new(5, 8, 5, 0), ColumnCount = 1, RowCount = 3 };
        storage.RowStyles.Add(new(SizeType.Absolute, 42)); storage.RowStyles.Add(new(SizeType.Absolute, 42)); storage.RowStyles.Add(new(SizeType.Percent, 100));
        storage.Controls.Add(Field("发送显卡", _gpu), 0, 0); storage.Controls.Add(Field("发送硬盘", _disk), 0, 1);
        foreach (string heading in new[] { "磁盘 / 卷", "型号 / 容量", "容量占用", "活动率", "温度" }) _diskGrid.Columns.Add(heading, heading);
        _diskGrid.Columns[0].FillWeight = 90; _diskGrid.Columns[1].FillWeight = 190;
        _diskGrid.Font = new(Font.FontFamily, 9);
        _diskGrid.EnableHeadersVisualStyles = false;
        _diskGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 243, 249);
        _diskGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(24, 39, 64);
        _diskGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _diskGrid.DefaultCellStyle.Padding = new(3);
        _diskGrid.RowTemplate.Height = 30;
        _diskGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
        _diskGrid.GridColor = Color.FromArgb(226, 232, 240);
        _diskGrid.Margin = new(3, 10, 3, 3);
        storage.Controls.Add(_diskGrid, 0, 2); lower.Controls.Add(storage, 0, 0);

        var connection = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new(12), Margin = new(5, 8, 5, 0), ColumnCount = 1, RowCount = 2 };
        connection.ColumnStyles.Add(new(SizeType.Percent, 100));
        connection.RowStyles.Add(new(SizeType.Percent, 100)); connection.RowStyles.Add(new(SizeType.Absolute, 46));
        var connectionScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var connectionRows = new TableLayoutPanel { Dock = DockStyle.Top, Height = 328, Margin = Padding.Empty, ColumnCount = 1, RowCount = 9 };
        connectionRows.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 40, 38, 38, 38, 24, 38, 44, 32, 36 }) connectionRows.RowStyles.Add(new(SizeType.Absolute, height));
        var connectionTitle = Caption("局域网设备"); connectionTitle.Font = new(Font, FontStyle.Bold);
        var header = Line(connectionTitle, _discover);
        header.ColumnStyles.Clear(); header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 132));
        connectionRows.Controls.Add(header, 0, 0);
        connectionRows.Controls.Add(Line(_devices), 0, 1);
        connectionRows.Controls.Add(Field("设备 IP", _ip), 0, 2);
        var identity = Line(Caption("Hardware"), _hardware, Caption("DeviceId"), _deviceId);
        identity.Font = new(Font.FontFamily, 9);
        identity.ColumnStyles.Clear();
        foreach (int width in new[] { 80, 60, 68 }) identity.ColumnStyles.Add(new(SizeType.Absolute, width));
        identity.ColumnStyles.Add(new(SizeType.Percent, 100));
        connectionRows.Controls.Add(identity, 0, 3);
        _endpoint.Dock = DockStyle.Fill; _endpoint.AutoSize = false; _endpoint.TextAlign = ContentAlignment.MiddleLeft; _endpoint.Margin = new(3); _endpoint.Font = new(Font.FontFamily, 9);
        connectionRows.Controls.Add(_endpoint, 0, 4);
        connectionRows.Controls.Add(Field("LocalToken", _localToken), 0, 5);
        var help = Caption("APP → Wi-Fi 设备 → 设置 → 本地令牌"); help.Font = new(Font.FontFamily, 9); help.ForeColor = Color.SlateGray;
        help.AutoEllipsis = false;
        connectionRows.Controls.Add(help, 0, 6);
        var timeGateMode = Line(_timeGate);
        connectionRows.Controls.Add(timeGateMode, 0, 7);
        var lcdMode = Line(_lcd, _independent);
        lcdMode.ColumnStyles.Clear(); lcdMode.ColumnStyles.Add(new(SizeType.Absolute, 100)); lcdMode.ColumnStyles.Add(new(SizeType.Percent, 100));
        connectionRows.Controls.Add(lcdMode, 0, 8);
        void UpdateTimeGateLayout()
        {
            bool visible = (int)_hardware.Value is 400 or 402;
            float scale = connectionRows.RowStyles[0].Height / 40f;
            timeGateMode.Visible = lcdMode.Visible = visible;
            connectionRows.RowStyles[7].Height = visible ? 32 * scale : 0;
            connectionRows.RowStyles[8].Height = visible ? 36 * scale : 0;
            connectionRows.Height = (int)Math.Ceiling(connectionRows.RowStyles.Cast<RowStyle>().Sum(row => row.Height));
        }
        _hardware.ValueChanged += (_, _) => UpdateTimeGateLayout();
        UpdateTimeGateLayout();
        _start.BackColor = Color.FromArgb(28, 112, 224); _start.ForeColor = Color.White; _start.FlatAppearance.BorderSize = 0;
        connectionScroll.Controls.Add(connectionRows); connection.Controls.Add(connectionScroll, 0, 0);
        connection.Controls.Add(Line(_start), 0, 1); lower.Controls.Add(connection, 1, 0); body.Controls.Add(lower, 0, 1);
        _status.Dock = DockStyle.Fill; _status.Margin = new(5, 8, 5, 0);
        body.Controls.Add(_status, 0, 2); page.Controls.Add(body);
        return page;
    }
    private TabPage SettingsPage()
    {
        var page = new TabPage("采集设置") { BackColor = BackColor, Padding = new(20) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var language = Combo(330);
        language.Items.Add(new LanguageChoice("auto", Localizer.Get("跟随系统")));
        foreach (var item in Localizer.Languages) language.Items.Add(item);
        language.SelectedIndex = Math.Max(0, Enumerable.Range(0, language.Items.Count).FirstOrDefault(i => language.Items[i] is LanguageChoice choice && choice.Code == _settings.Language));
        flow.Controls.Add(new FlowLayoutPanel { AutoSize = true, WrapContents = false, Controls = { new Label { Text = "语言", AutoSize = true, Margin = new(4, 9, 8, 0) }, language } });
        var permissions = new Label { Text = "温度采集权限", AutoSize = true, Font = new(Font, FontStyle.Bold), Margin = new(4, 10, 4, 10) };
        var downloadPawn = Button("PawnIO 官方下载", 180);
        downloadPawn.Click += (_, _) => OpenUrl("https://pawnio.eu/");
        var elevate = Button("以管理员身份重启", 190);
        elevate.Click += (_, _) => Elevate();
        var restart = Button("重新检测传感器", 180);
        restart.Click += (_, _) => _monitor.RestartSensors();
        var browse = Button("浏览…", 90);
        browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "smartctl 程序|smartctl.exe|可执行文件|*.exe" }; if (dialog.ShowDialog(this) == DialogResult.OK) _smartctlPath.Text = dialog.FileName; };
        _smartctl.Items.AddRange(new object[] { Localizer.Get("自动（推荐，使用内置 smartctl）"), Localizer.Get("禁用补充采集"), Localizer.Get("自定义 smartctl 路径") });
        _smartctl.SelectedIndexChanged += (_, _) => { _smartctlPath.Enabled = browse.Enabled = _smartctl.SelectedIndex == 2; };
        _smartctlPath.Enabled = browse.Enabled = false;
        var smartDownload = Button("smartmontools 官方项目", 240);
        smartDownload.Click += (_, _) => OpenUrl("https://github.com/smartmontools/smartmontools/releases");
        var save = Button("保存并重启采集", 190);
        save.Click += (_, _) =>
        {
            if (_smartctl.SelectedIndex == 2 && !File.Exists(_smartctlPath.Text.Trim())) { MessageBox.Show(this, Localizer.Get("请先选择存在的 smartctl 文件。"), Text); return; }
            _settings.SmartctlMode = (SmartctlMode)Math.Max(0, _smartctl.SelectedIndex); _settings.SmartctlPath = _smartctlPath.Text.Trim(); _settings.EnableExternalProviders = _plugins.Checked;
            bool languageChanged = _settings.Language != ((LanguageChoice)language.SelectedItem!).Code;
            _settings.Language = ((LanguageChoice)language.SelectedItem!).Code;
            SaveSelections(); _monitor.RestartSensors(); _status.Text = Localizer.Get(languageChanged ? "语言已保存，重启程序后生效。" : "采集设置已保存，正在重新检测传感器。");
        };
        flow.Controls.Add(permissions);
        flow.Controls.Add(new Label { Text = "CPU 温度可能需要 PawnIO 和管理员权限。", AutoSize = true, MaximumSize = new(850, 0), Margin = new(4, 8, 4, 12) });
        flow.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { downloadPawn, elevate, restart } });
        flow.Controls.Add(new Label { Text = "硬盘温度补充", AutoSize = true, Font = new(Font, FontStyle.Bold), Margin = new(4, 25, 4, 8) });
        flow.Controls.Add(_smartctl);
        flow.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { _smartctlPath, browse } });
        flow.Controls.Add(smartDownload);
        flow.Controls.Add(new Label { Text = "自动补充缺失的磁盘温度；部分磁盘需要管理员权限。", AutoSize = true, MaximumSize = new(850, 0), Margin = new(4, 8, 4, 12) });
        _plugins.Margin = new(4, 20, 4, 12); flow.Controls.Add(_plugins);
        _plugins.Text = "启用 SDK 扩展";
        flow.Controls.Add(new Label { Text = "扩展 SDK 需要单独授权。", AutoSize = true, Margin = new(4, 8, 4, 15) });
        flow.Controls.Add(save); page.Controls.Add(flow); return page;
    }
    private TabPage DiagnosticPage()
    {
        var page = new TabPage("诊断") { Padding = new(12) };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        body.RowStyles.Add(new(SizeType.Absolute, 45)); body.RowStyles.Add(new(SizeType.Percent, 100));
        var export = Button("导出诊断报告", 180);
        export.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = Localizer.Get("导出诊断报告") + "|*.json", FileName = "Divoom-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { Version = typeof(MainForm).Assembly.GetName().Version?.ToString(), OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(), Snapshot = _monitor.Current }, AppSettings.JsonOptions));
            _status.Text = "诊断报告已保存。";
        };
        body.Controls.Add(Row(export, new Label { Text = "报告包含硬件和错误信息；不会自动上传。", AutoSize = true }), 0, 0);
        body.Controls.Add(_diagnostics, 0, 1); page.Controls.Add(body); return page;
    }
    private void Display(HardwareSnapshot s)
    {
        if (_status.Text == Localizer.Get("本机监控启动中 · 尚未发送")) _status.Text = Localizer.Get("本机监控运行中 · 尚未发送");
        _filling = true;
        string gpuSignature = string.Join("|", s.Gpus.Select(x => x.Id));
        if (_gpuSignature != gpuSignature)
        {
            string selected = (_gpu.SelectedItem as Choice)?.Key ?? _settings.GpuId;
            _gpu.Items.Clear(); foreach (var item in s.Gpus) _gpu.Items.Add(new Choice(item.Id, item.MatchKey, item.Name));
            _gpu.SelectedIndex = s.Gpus.FindIndex(x => x.MatchKey == selected);
            if (_gpu.SelectedIndex < 0 && _gpu.Items.Count > 0) _gpu.SelectedIndex = Math.Max(0, s.Gpus.FindIndex(x => x.IsDiscrete));
            _gpuSignature = gpuSignature;
        }
        string diskSignature = string.Join("|", s.Disks.Select(x => x.Id));
        if (_diskSignature != diskSignature)
        {
            string selected = (_disk.SelectedItem as Choice)?.Id ?? _settings.DiskId;
            _disk.Items.Clear(); foreach (var item in s.Disks) _disk.Items.Add(new Choice(item.Id, item.Id, $"磁盘 {item.Index} · {item.Name}"));
            int systemDisk = s.Volumes.FirstOrDefault(x => x.Name.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))?.DiskIndex ?? 0;
            _disk.SelectedIndex = s.Disks.FindIndex(x => x.Id == selected);
            if (_disk.SelectedIndex < 0 && _disk.Items.Count > 0) _disk.SelectedIndex = Math.Max(0, s.Disks.FindIndex(x => x.Index == systemDisk));
            _diskSignature = diskSignature;
        }
        _filling = false;
        var gpu = s.Gpus.FirstOrDefault(x => x.Id == (_gpu.SelectedItem as Choice)?.Id);
        var disk = s.Disks.FirstOrDefault(x => x.Id == (_disk.SelectedItem as Choice)?.Id);
        _cpuCard.Set(s.CpuName, s.CpuLoad.Display(s.SampledAt), "温度  " + s.CpuTemperature.Display(s.SampledAt), s.CpuTemperature.Error ?? s.CpuTemperature.Source);
        _gpuCard.Set(gpu?.Name ?? "未检测到支持的显卡", gpu?.Load.Display(s.SampledAt) ?? "--", "温度  " + (gpu?.Temperature.Display(s.SampledAt) ?? "--"), gpu?.Temperature.Error ?? gpu?.Temperature.Source ?? "");
        _memoryCard.Set("物理内存占用", s.MemoryLoad.Display(s.SampledAt), $"{Gb(s.TotalMemoryBytes - s.AvailableMemoryBytes)} / {Gb(s.TotalMemoryBytes)}", "GlobalMemoryStatusEx");
        _diskCard.Set(disk?.Name ?? "未检测到磁盘", disk?.Temperature.Display(s.SampledAt) ?? "--", "活动率  " + (disk?.Activity.Display(s.SampledAt) ?? "--"), disk?.Temperature.Error ?? disk?.Temperature.Source ?? "");
        int scroll = _diskGrid.FirstDisplayedScrollingRowIndex;
        _diskGrid.Rows.Clear();
        foreach (var item in s.Disks) _diskGrid.Rows.Add(Localizer.Get("硬盘") + " " + item.Index, item.Name, Localizer.Get("按卷查看"), item.Activity.Display(s.SampledAt), item.Temperature.Display(s.SampledAt));
        foreach (var volume in s.Volumes) _diskGrid.Rows.Add(volume.Name + (volume.DiskIndex is { } i ? " → " + Localizer.Get("硬盘") + " " + i : ""), $"{Localizer.Get("已用")} {Gb(volume.TotalBytes - volume.FreeBytes)} / {Gb(volume.TotalBytes)}", volume.UsedPercent.ToString("F1") + "%", "", "");
        if (scroll >= 0 && scroll < _diskGrid.RowCount) _diskGrid.FirstDisplayedScrollingRowIndex = scroll;
        var notes = new List<string>(s.Diagnostics);
        void Describe(string label, Metric value) => notes.Add($"{label}: {value.Display(s.SampledAt)} | {value.Source} | {value.Error ?? (value.IsAvailable(s.SampledAt) ? "正常" : "数据过期")}");
        Describe("CPU 使用率", s.CpuLoad); Describe("CPU 温度", s.CpuTemperature); Describe("内存", s.MemoryLoad);
        foreach (var item in s.Gpus) { Describe(item.Name + " 使用率", item.Load); Describe(item.Name + " 温度", item.Temperature); }
        foreach (var item in s.Disks) { Describe(item.Name + " 活动率", item.Activity); Describe(item.Name + " 温度", item.Temperature); }
        _diagnostics.Text = $"更新时间: {s.SampledAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\r\nCPU: {s.CpuName}\r\n\r\n" + string.Join("\r\n", notes);
        string errors = string.Join(" | ", s.Diagnostics);
        if (errors != _lastDiagnostic) { if (errors.Length > 0) AppLog.Write(errors); _lastDiagnostic = errors; }
    }
    private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("F1") + " GB";
    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!IsDisposed && !Disposing) { action(); ApplyLanguage(this); } }); } catch (InvalidOperationException) { }
    }
    private void SaveSelections()
    {
        _settings.DeviceIp = _ip.Text.Trim(); _settings.TimeGate = _timeGate.Checked;
        _settings.DeviceHardware = (int)_hardware.Value;
        _settings.DeviceId = (int)_deviceId.Value;
        _settings.LcdIndex = Math.Max(0, _lcd.SelectedIndex); _settings.LcdIndependence = _independent.Checked ? 1 : 0;
        if (_gpu.SelectedItem is Choice gpu) _settings.GpuId = gpu.Key;
        if (_disk.SelectedItem is Choice disk) _settings.DiskId = disk.Id;
        _settings.Save();
    }
    private async Task Discover()
    {
        _discover.Enabled = false; _status.Text = "正在获取同局域网设备列表…";
        try
        {
            var devices = await _transport.DiscoverAsync(_lifetime.Token);
            _devices.Items.Clear(); foreach (var item in devices) _devices.Items.Add(item);
            _status.Text = devices.Count > 0 ? $"发现 {devices.Count} 台设备，选择设备或手填 IP。" : "未发现设备，可手动填写 IP。";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) { _status.Text = "设备列表不可用，可手填 IP: " + e.Message; }
        finally { if (!IsDisposed) _discover.Enabled = !_sender.IsRunning; }
    }
    private async Task SelectDevice()
    {
        if (_devices.SelectedItem is not DeviceInfo device) return;
        _ip.Text = device.DevicePrivateIP; _settings.DeviceId = device.DeviceId;
        _deviceId.Value = Math.Max(0, device.DeviceId);
        _hardware.Value = Math.Max(0, device.Hardware);
        _timeGate.Checked = device.Hardware is 400 or 402;
        if (!_timeGate.Checked) return;
        try
        {
            int mode = await _transport.ReadIndependenceAsync(device.DeviceId, _lifetime.Token);
            if (_devices.SelectedItem is DeviceInfo current && current.DeviceId == device.DeviceId && !_sender.IsRunning) _independent.Checked = mode == 1;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) { _status.Text = "LCD 模式未读到，可手动设置: " + e.Message; }
    }
    private async Task ToggleSending()
    {
        _start.Enabled = false;
        try
        {
            if (_sender.IsRunning) await _sender.StopAsync();
            else
            {
                SaveSelections();
                var target = new DeviceTarget(_settings.DeviceIp, _settings.DeviceId, _timeGate.Checked ? _settings.LcdIndex : 0, _timeGate.Checked ? _settings.LcdIndependence : 0, _settings.DeviceHardware, DeviceSecrets.ParseLocalToken(_localToken.Text));
                await _sender.StartAsync(target, (_gpu.SelectedItem as Choice)?.Id ?? "", (_disk.SelectedItem as Choice)?.Id ?? "");
            }
        }
        catch (Exception e) { _status.Text = e.Message; }
        finally
        {
            _start.Text = _sender.IsRunning ? "停止发送" : "开始发送"; _start.Enabled = true;
            foreach (Control item in new Control[] { _ip, _devices, _discover, _timeGate, _gpu, _disk, _hardware, _deviceId, _localToken }) item.Enabled = !_sender.IsRunning;
            _lcd.Enabled = _independent.Enabled = !_sender.IsRunning && _timeGate.Checked;
        }
    }
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void Elevate()
    {
        try
        {
            SaveSelections();
            var info = MonitorService.SelfStart(); info.UseShellExecute = true; info.Verb = "runas"; info.CreateNoWindow = false;
            if (Process.Start(info) is not null) { _exit = true; Close(); }
        }
        catch (System.ComponentModel.Win32Exception e) { _status.Text = "管理员重启未完成: " + e.Message; }
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        if (_cleanedUp) return;
        // Keep the message loop alive until cancellation and child-process cleanup complete.
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _exit = true;
        try { SaveSelections(); } catch (Exception error) { AppLog.Write("保存设置失败: " + error.Message); }
        _tray.Visible = false; _lifetime.Cancel();
        await _sender.StopAsync();
        _monitor.Dispose(); _sender.Dispose(); _tray.Dispose(); _lifetime.Dispose();
        _cleanedUp = true;
        Close();
    }
    private sealed class MetricCard : Panel
    {
        private readonly Label _name, _main, _secondary, _source;
        public MetricCard(string title)
        {
            Dock = DockStyle.Fill; BackColor = Color.White; Margin = new(5); Padding = new(12);
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            table.RowStyles.Add(new(SizeType.Absolute, 20)); table.RowStyles.Add(new(SizeType.Absolute, 30)); table.RowStyles.Add(new(SizeType.Absolute, 43)); table.RowStyles.Add(new(SizeType.Absolute, 23)); table.RowStyles.Add(new(SizeType.Percent, 100));
            Label Label(string text, float size, Color color) => new() { Text = text, Dock = DockStyle.Fill, Font = new("Microsoft YaHei UI", size), ForeColor = color, AutoEllipsis = true };
            table.Controls.Add(Label(title, 10, Color.SlateGray), 0, 0);
            _name = Label("正在检测…", 9, Color.FromArgb(45, 58, 79));
            _main = Label("--", 27, Color.FromArgb(25, 80, 133));
            _secondary = Label("", 11, Color.FromArgb(45, 58, 79));
            _source = Label("", 8, Color.SlateGray);
            table.Controls.Add(_name, 0, 1); table.Controls.Add(_main, 0, 2); table.Controls.Add(_secondary, 0, 3); table.Controls.Add(_source, 0, 4); Controls.Add(table);
        }
        public void Set(string name, string main, string secondary, string source) { _name.Tag = "HardwareName"; _name.Text = Localizer.Resources["keys"].Contains(name) ? Localizer.Get(name) : name; _main.Text = main; _secondary.Text = Localizer.Ui(secondary); _source.Tag = "RawDiagnostic"; _source.Text = source; }
    }
}
