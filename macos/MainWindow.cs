using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Diagnostics;
using System.Text.Json;

namespace Divoom.HardwareMonitor.Mac;

public sealed class MainWindow : Window
{
    private readonly MacMonitor _monitor = new();
    private readonly DivoomTransport _transport = new();
    private readonly SendingService _sender;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly TextBox _ip = new() { Width = 165, Watermark = "设备 IP" };
    private readonly NumericUpDown _hardware = new() { Width = 110, Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0" };
    private readonly NumericUpDown _deviceId = new() { Width = 170, Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0" };
    private readonly TextBox _localToken = new() { Width = 180, PasswordChar = '●', Watermark = "可选：本地令牌" };
    private readonly ComboBox _devices = new() { Width = 350 }, _gpu = new() { Width = 300 }, _disk = new() { Width = 300 };
    private readonly ComboBox _lcd = new() { Width = 100, ItemsSource = new[] { "LCD 1", "LCD 2", "LCD 3", "LCD 4", "LCD 5" } };
    private readonly CheckBox _independent = new() { Content = "LCD 独立模式" };
    private readonly TextBlock _status = Label("本机监控启动中 · 尚未发送"), _endpoint = Label("");
    private readonly Button _start = ActionButton("开始发送"), _discover = ActionButton("刷新设备");
    private readonly TextBox _diagnostic = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 13, MinHeight = 400 };
    private readonly TextBox _diskList = new() { IsReadOnly = true, AcceptsReturn = true, FontSize = 13, Height = 150 };
    private readonly ComboBox _smartMode = new() { Width = 400, ItemsSource = new[] { Localizer.Get("自动查找 macOS smartctl"), Localizer.Get("禁用补充采集"), Localizer.Get("自定义 smartctl 路径") } };
    private readonly TextBox _smartPath = new() { Width = 570, Watermark = "smartctl" };
    private readonly Card _cpuCard = new("CPU"), _gpuCard = new("GPU"), _memoryCard = new("内存"), _diskCard = new("硬盘");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TrayIcon? _tray;
    private bool _filling, _quitting;
    private string _gpuSignature = "", _diskSignature = "", _lastErrors = "";
    private sealed record Choice(string Id, string Name) { public override string ToString() => Name; }

    public MainWindow(IClassicDesktopStyleApplicationLifetime desktop, bool preview = false)
    {
        _desktop = desktop; _sender = new(_transport, () => _monitor.Current);
        Title = "Divoom / Mac 硬件监控 v" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3); Width = 1120; Height = 830; MinWidth = 980; MinHeight = 700;
        Background = Brush.Parse("#F4F7FB"); Padding = new Thickness(24);
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 16 };
        root.Children.Add(new TextBlock { Text = "Divoom / Mac 硬件监控", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#103354") });
        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = Localizer.Get("实时监控"), Content = MonitorPage() }, new TabItem { Header = Localizer.Get("采集设置"), Content = SettingsPage() }, new TabItem { Header = Localizer.Get("诊断"), Content = DiagnosticPage() } } };
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        var footer = Label("本机监控独立运行；关闭窗口后保持后台运行。");
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _filling = true;
        _ip.Text = _settings.DeviceIp; _hardware.Value = _settings.DeviceHardware; _deviceId.Value = _settings.DeviceId;
        _lcd.SelectedIndex = Math.Clamp(_settings.LcdIndex, 0, 4); _independent.IsChecked = _settings.LcdIndependence == 1;
        _smartMode.SelectedIndex = (int)_settings.SmartctlMode; _smartPath.Text = _settings.SmartctlPath; _filling = false;
        _ip.TextChanged += (_, _) => { if (!_filling) _deviceId.Value = 0; _localToken.Text = ""; UpdateEndpoint(); };
        _hardware.ValueChanged += (_, _) => UpdateEndpoint(); UpdateEndpoint();
        _devices.SelectionChanged += async (_, _) => await SelectDevice();
        _discover.Click += async (_, _) => await Discover(); _start.Click += async (_, _) => await ToggleSending();
        _sender.StatusChanged += message => Dispatcher.UIThread.Post(() => _status.Text = Localizer.Ui(message));
        _timer.Tick += (_, _) => { if (_monitor.Current is { } snapshot) Display(snapshot); else _diagnostic.Text = _monitor.Status; ApplyLanguage(this); };
        Closing += (_, args) => { if (!_quitting && _tray is not null) { args.Cancel = true; Hide(); } else if (!_quitting) { args.Cancel = true; _ = Quit(); } };
        Opened += (_, _) => { ApplyLanguage(this); if (preview) return; SetupTray(); if (OperatingSystem.IsMacOS()) _monitor.Start(); else _diagnostic.Text = "仅界面预览；Windows 不执行 macOS 采集。"; _timer.Start(); };
        ApplyLanguage(this);
    }
    private static void ApplyLanguage(Control control)
    {
        if (control is Window window) window.Title = Localizer.Ui(window.Title ?? "");
        if (control is TextBlock text && control.Tag as string is not ("HardwareName" or "RawDiagnostic")) text.Text = Localizer.Ui(text.Text ?? "");
        if (control is Button button && button.Content is string content) button.Content = Localizer.Ui(content);
        if (control is CheckBox check && check.Content is string label) check.Content = Localizer.Ui(label);
        if (control is TabItem tab && tab.Header is string header) tab.Header = Localizer.Ui(header);
        if (control is TextBox box && box.Watermark is { } watermark) box.Watermark = Localizer.Ui(watermark);
        foreach (var child in Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(control).OfType<Control>()) ApplyLanguage(child);
    }
    private Control MonitorPage()
    {
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(10) };
        var cards = new Grid { ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 10 };
        int column = 0; foreach (var card in new[] { _cpuCard, _gpuCard, _memoryCard, _diskCard }) { Grid.SetColumn(card.Panel, column++); cards.Children.Add(card.Panel); }
        body.Children.Add(cards); body.Children.Add(Row(Label("发送显卡"), _gpu, Label("发送硬盘"), _disk)); body.Children.Add(_diskList);
        var connection = new StackPanel { Spacing = 10 };
        connection.Children.Add(Label("局域网设备")); connection.Children.Add(Row(_discover, _devices, _ip, _start));
        connection.Children.Add(Row(Label("Hardware"), _hardware, Label("DeviceId"), _deviceId, _lcd, _independent)); connection.Children.Add(_endpoint);
        connection.Children.Add(Row(Label("LocalToken"), _localToken, Label("Divoom APP → Wi-Fi 设备 → 设置 → 本地令牌（仅本次运行使用）")));
        body.Children.Add(new Border { BorderBrush = Brush.Parse("#D7DEE8"), BorderThickness = new Thickness(1), Padding = new Thickness(14), Child = connection });
        body.Children.Add(_status); return new ScrollViewer { Content = body };
    }
    private Control SettingsPage()
    {
        var body = new StackPanel { Spacing = 18, Margin = new Thickness(24) };
        var language = new ComboBox { Width = 330, ItemsSource = new[] { new LanguageChoice("auto", Localizer.Get("跟随系统")) }.Concat(Localizer.Languages).ToArray() };
        language.SelectedItem = language.Items.Cast<LanguageChoice>().FirstOrDefault(l => l.Code == _settings.Language) ?? language.Items[0];
        body.Children.Add(Row(Label("语言"), language));
        body.Children.Add(Label("温度采集权限"));
        body.Children.Add(Label("温度取决于硬件与系统支持；不可用时显示 --。"));
        var restart = ActionButton("重新检测传感器"); restart.Click += (_, _) => _monitor.Restart(); body.Children.Add(restart);
        body.Children.Add(Label("硬盘温度补充")); body.Children.Add(_smartMode); body.Children.Add(_smartPath);
        body.Children.Add(Label("自动查找 Homebrew 路径；不自动提权。"));
        var save = ActionButton("保存并重启采集");
        save.Click += (_, _) =>
        {
            try
            {
                if (_smartMode.SelectedIndex == 2 && !File.Exists(_smartPath.Text)) throw new IOException("请选择存在的 macOS smartctl 文件。");
                _settings.SmartctlMode = (SmartctlMode)Math.Max(0, _smartMode.SelectedIndex); _settings.SmartctlPath = _smartPath.Text?.Trim() ?? "";
                bool languageChanged = _settings.Language != ((LanguageChoice)language.SelectedItem!).Code;
                _settings.Language = ((LanguageChoice)language.SelectedItem!).Code;
                SaveSelections(); _monitor.Restart(); _status.Text = Localizer.Get(languageChanged ? "语言已保存，重启程序后生效。" : "采集设置已保存，正在重新检测传感器。");
            }
            catch (Exception e) { _status.Text = e.Message; }
        };
        body.Children.Add(save); return new ScrollViewer { Content = body };
    }
    private Control DiagnosticPage()
    {
        var body = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 14, Margin = new Thickness(16) };
        var export = ActionButton("导出诊断报告"); var logs = ActionButton("打开日志目录");
        export.Click += async (_, _) =>
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出诊断报告", SuggestedFileName = "Divoom-Mac-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } } });
                if (file is null) return;
                string log = ""; var path = Path.Combine(AppSettings.DataDirectory, "monitor.log");
                if (File.Exists(path)) { log = await File.ReadAllTextAsync(path); if (log.Length > 64000) log = log[^64000..]; }
                var report = new { Version = typeof(MainWindow).Assembly.GetName().Version?.ToString(), OS = Environment.OSVersion.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Runtime = Environment.Version.ToString(), WorkerStatus = _monitor.Status, Snapshot = _monitor.Current, RecentLog = log };
                await using var output = await file.OpenWriteAsync(); output.SetLength(0); await JsonSerializer.SerializeAsync(output, report, AppSettings.JsonOptions);
                _status.Text = "诊断报告已保存，由你决定是否分享。";
            }
            catch (Exception e) { _status.Text = "导出失败: " + e.Message; }
        };
        logs.Click += (_, _) => { try { Directory.CreateDirectory(AppSettings.DataDirectory); var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false }; start.ArgumentList.Add(AppSettings.DataDirectory); Process.Start(start); } catch (Exception e) { _status.Text = e.Message; } };
        body.Children.Add(Row(export, logs, Label("报告包含硬件和错误信息；不会自动上传。")));
        Grid.SetRow(_diagnostic, 1); body.Children.Add(_diagnostic); return body;
    }
    private void Display(HardwareSnapshot s)
    {
        if (_status.Text == Localizer.Get("本机监控启动中 · 尚未发送")) _status.Text = Localizer.Get("本机监控运行中 · 尚未发送");
        var now = DateTimeOffset.UtcNow;
        string gpuSignature = string.Join('|', s.Gpus.Select(g => g.Id)), diskSignature = string.Join('|', s.Disks.Select(d => d.Id));
        if (_gpuSignature != gpuSignature)
        {
            string selected = (_gpu.SelectedItem as Choice)?.Id ?? _settings.GpuId;
            _gpu.ItemsSource = s.Gpus.Select(g => new Choice(g.Id, g.Name)).ToList();
            _gpu.SelectedIndex = Math.Max(0, s.Gpus.FindIndex(g => g.Id == selected)); _gpuSignature = gpuSignature;
        }
        if (_diskSignature != diskSignature)
        {
            string selected = (_disk.SelectedItem as Choice)?.Id ?? _settings.DiskId;
            _disk.ItemsSource = s.Disks.Select(d => new Choice(d.Id, d.Id + " · " + d.Name)).ToList();
            _disk.SelectedIndex = Math.Max(0, s.Disks.FindIndex(d => d.Id == selected)); _diskSignature = diskSignature;
        }
        var gpu = s.Gpus.FirstOrDefault(g => g.Id == (_gpu.SelectedItem as Choice)?.Id); var disk = s.Disks.FirstOrDefault(d => d.Id == (_disk.SelectedItem as Choice)?.Id);
        _cpuCard.Set(s.CpuName, s.CpuLoad.Display(now), "温度 " + s.CpuTemperature.Display(now), s.CpuTemperature.Error ?? s.CpuTemperature.Source);
        _gpuCard.Set(gpu?.Name ?? "未检测到支持的显卡", gpu?.Load.Display(now) ?? "--", "温度 " + (gpu?.Temperature.Display(now) ?? "--"), gpu?.Temperature.Error ?? gpu?.Temperature.Source ?? "");
        _memoryCard.Set("物理内存占用", s.MemoryLoad.Display(now), $"{Gb(s.TotalMemoryBytes - s.AvailableMemoryBytes)} / {Gb(s.TotalMemoryBytes)}", s.MemoryLoad.Source);
        _diskCard.Set(disk?.Name ?? "未检测到磁盘", disk?.Temperature.Display(now) ?? "--", "服务时间占比 " + (disk?.Activity.Display(now) ?? "--"), disk?.Temperature.Error ?? disk?.Temperature.Source ?? "");
        _diskList.Text = string.Join('\n', s.Disks.Select(d => $"{d.Id}  {d.Name}   {Localizer.Get("活动率")} {d.Activity.Display(now)}   {Localizer.Get("温度")} {d.Temperature.Display(now)}").Concat(s.Volumes.Select(v => $"{v.Name}   {Localizer.Get("已用")} {Gb(v.TotalBytes - v.FreeBytes)} / {Gb(v.TotalBytes)}   {v.UsedPercent:F1}%")));
        var notes = new List<string> { "更新时间: " + s.SampledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), "CPU: " + s.CpuName, _monitor.Status }; notes.AddRange(s.Diagnostics);
        void Describe(string name, Metric metric) => notes.Add($"{name}: {metric.Display(now)} | {metric.Source} | {metric.Error ?? (metric.IsAvailable(now) ? "正常" : "数据过期")}");
        Describe("CPU 使用率", s.CpuLoad); Describe("CPU 温度", s.CpuTemperature); Describe("内存", s.MemoryLoad);
        foreach (var g in s.Gpus) { Describe(g.Name + " 使用率", g.Load); Describe(g.Name + " 温度", g.Temperature); }
        foreach (var d in s.Disks) { Describe(d.Name + " 活动", d.Activity); Describe(d.Name + " 温度", d.Temperature); }
        _diagnostic.Text = string.Join('\n', notes);
        // Avoid logging continuously changing successful values.
        string missing = string.Join(" | ", s.Diagnostics.Concat(new[] { s.CpuTemperature.Error ?? "" }).Where(n => n.Length > 0));
        if (missing != _lastErrors) { AppLog.Write(missing); _lastErrors = missing; }
    }
    private async Task Discover()
    {
        _discover.IsEnabled = false;
        try { var devices = await _transport.DiscoverAsync(_lifetime.Token); _devices.ItemsSource = devices; _status.Text = $"发现 {devices.Count} 台设备，也可手动填写 IP。"; }
        catch (Exception e) { _status.Text = "设备列表不可用，可手填 IP: " + e.Message; }
        finally { _discover.IsEnabled = !_sender.IsRunning; }
    }
    private async Task SelectDevice()
    {
        if (_devices.SelectedItem is not DeviceInfo device) return;
        _filling = true; _ip.Text = device.DevicePrivateIP; _hardware.Value = device.Hardware; _deviceId.Value = device.DeviceId; _filling = false; UpdateEndpoint();
        if (device.Hardware is not (400 or 402)) return;
        try { int mode = await _transport.ReadIndependenceAsync(device.DeviceId, _lifetime.Token); if (_devices.SelectedItem is DeviceInfo current && current.DeviceId == device.DeviceId && !_sender.IsRunning) _independent.IsChecked = mode == 1; }
        catch (Exception e) { _status.Text = "LCD 模式未读到，可手动设置: " + e.Message; }
    }
    private async Task ToggleSending()
    {
        _start.IsEnabled = false;
        try
        {
            if (_sender.IsRunning) await _sender.StopAsync();
            else { SaveSelections(); await _sender.StartAsync(new(_settings.DeviceIp, _settings.DeviceId, _settings.LcdIndex, _settings.LcdIndependence, _settings.DeviceHardware, DeviceSecrets.ParseLocalToken(_localToken.Text)), (_gpu.SelectedItem as Choice)?.Id ?? "", (_disk.SelectedItem as Choice)?.Id ?? ""); }
        }
        catch (Exception e) { _status.Text = e.Message; }
        finally
        {
            _start.Content = _sender.IsRunning ? "停止发送" : "开始发送"; _start.IsEnabled = true;
            foreach (var control in new Control[] { _ip, _hardware, _deviceId, _devices, _discover, _lcd, _independent, _gpu, _disk, _localToken }) control.IsEnabled = !_sender.IsRunning;
        }
    }
    private void SaveSelections()
    {
        _settings.DeviceIp = _ip.Text?.Trim() ?? ""; _settings.DeviceHardware = (int)(_hardware.Value ?? 0); _settings.DeviceId = (int)(_deviceId.Value ?? 0);
        _settings.LcdIndex = Math.Max(0, _lcd.SelectedIndex); _settings.LcdIndependence = _independent.IsChecked == true ? 1 : 0;
        _settings.GpuId = (_gpu.SelectedItem as Choice)?.Id ?? ""; _settings.DiskId = (_disk.SelectedItem as Choice)?.Id ?? ""; _settings.Save();
    }
    private void UpdateEndpoint()
    {
        _lcd.IsVisible = _independent.IsVisible = (int)(_hardware.Value ?? 0) is 400 or 402;
        try { var target = new DeviceTarget(_ip.Text ?? "", (int)(_deviceId.Value ?? 0), Math.Max(0, _lcd.SelectedIndex), _independent.IsChecked == true ? 1 : 0, (int)(_hardware.Value ?? 0)); _endpoint.Text = target.PostUri.ToString(); }
        catch (ArgumentException e) { _endpoint.Text = e.Message; }
    }
    private void SetupTray()
    {
        try
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "icon.png");
            if (!File.Exists(icon)) return;
            var menu = new NativeMenu(); var show = new NativeMenuItem(Localizer.Get("显示监控面板")); var quit = new NativeMenuItem(Localizer.Get("退出"));
            show.Click += (_, _) => Restore(); quit.Click += async (_, _) => await Quit(); menu.Add(show); menu.Add(quit);
            _tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "Divoom Mac 硬件监控", Menu = menu, IsVisible = true };
            _tray.Clicked += (_, _) => Restore(); TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        }
        catch (Exception e) { AppLog.Write("菜单栏图标不可用: " + e.Message); }
    }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    private async Task Quit()
    {
        if (_quitting) return; _quitting = true; _timer.Stop(); _lifetime.Cancel();
        try { SaveSelections(); await _sender.StopAsync(); } catch (Exception e) { AppLog.Write(e.Message); }
        _sender.Dispose(); _monitor.Dispose(); _tray?.Dispose(); _lifetime.Dispose(); _desktop.Shutdown();
    }
    private static string Gb(long value) => (value / 1073741824.0).ToString("F1") + " GB";
    private static TextBlock Label(string text) => new() { Text = Localizer.Ui(text), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#305778") };
    private static Button ActionButton(string text) => new() { Content = Localizer.Ui(text), Padding = new Thickness(14, 7), VerticalAlignment = VerticalAlignment.Center };
    private static WrapPanel Row(params Control[] controls) { var row = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var control in controls) { control.Margin = new Thickness(0, 0, 10, 4); row.Children.Add(control); } return row; }
    private sealed class Card
    {
        public Border Panel { get; }
        private readonly TextBlock _name = Label("--"), _value = Label("--"), _detail = Label(""), _source = Label("");
        public Card(string title)
        {
            _value.FontSize = 34; _value.Foreground = Brush.Parse("#1660A0"); _name.Height = 38; _name.FontSize = 12; _source.FontSize = 11; _source.Height = 34;
            _name.Tag = "HardwareName"; _source.Tag = "RawDiagnostic";
            Panel = new Border { Background = Brushes.White, Padding = new Thickness(16), CornerRadius = new CornerRadius(6), Child = new StackPanel { Spacing = 5, Children = { Label(title), _name, _value, _detail, _source } } };
        }
        public void Set(string name, string value, string detail, string source) { _name.Text = Localizer.Resources["keys"].Contains(name) ? Localizer.Get(name) : name; _value.Text = value; _detail.Text = Localizer.Ui(detail); _source.Text = source; ToolTip.SetTip(Panel, source); }
    }
}
