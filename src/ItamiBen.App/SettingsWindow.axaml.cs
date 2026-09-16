using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ItamiBen.App.Platform;

namespace ItamiBen.App;

/// <summary>
/// 设置窗口，**照 v3 的样子做**：一张卡一个设置，**一个字说明都没有**
/// （标题 + 控件，剩下的留给猜——跟主窗口滑块不显示数字是同一条）。
///
/// 改动**当场生效当场落盘**，没有「确定 / 取消」：这几个都是外观类的值，
/// 为它维护一份「还没提交的草稿」不值当。
///
/// ⚠️ 开关走 <see cref="MainWindow"/> 上那几个 <c>Set*</c> 入口，**不直接写
/// <see cref="Settings"/>**——右键菜单和右上角的图标读的是同一个状态，
/// 绕过入口就会分家（DECISIONS G8 记的那个病）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly MainWindow? _owner;

    /// <summary>装填时置起来，免得初始化那几下事件直接开始试听。</summary>
    private bool _loading;

    // Avalonia 的设计器要一个无参构造
    public SettingsWindow() : this(new Settings(), null) { }

    public SettingsWindow(Settings settings, MainWindow? owner)
    {
        _settings = settings;
        _owner = owner;
        AvaloniaXamlLoader.Load(this);

        PaintCards();

        var available = Sound.Available();
        _loading = true;
        Fill("FocusSound", available, _settings.FocusDoneSound);
        Fill("RestSound", available, _settings.RestDoneSound);
        Fill("IdleSound", available, _settings.IdleSound);
        Fill("AlarmsSound", available, _settings.AlarmsSound);
        Fill("AlarmSound", available, _settings.AlarmSound);
        this.FindControl<Slider>("TickVol")!.Value = _settings.TickVolume;
        this.FindControl<ToggleSwitch>("ForceOn")!.IsChecked = _settings.ForceTicking;
        this.FindControl<ToggleSwitch>("FocusOn")!.IsChecked = _settings.FocusDoneEnabled;
        this.FindControl<ToggleSwitch>("RestOn")!.IsChecked = _settings.RestDoneEnabled;
        this.FindControl<ToggleSwitch>("IdleOn")!.IsChecked = _settings.IdleEnabled;
        this.FindControl<ToggleSwitch>("AlarmsOn")!.IsChecked = _settings.AlarmsEnabled;
        this.FindControl<ToggleSwitch>("ExecuteOn")!.IsChecked = _owner?.CommandArmed ?? false;
        _loading = false;

        Wire("FocusSound", name => _settings.FocusDoneSound = name);
        Wire("RestSound", name => _settings.RestDoneSound = name);
        Wire("IdleSound", name => _settings.IdleSound = name);
        Wire("AlarmsSound", name => _settings.AlarmsSound = name);
        Wire("AlarmSound", name => _settings.AlarmSound = name);

        Toggle("ForceOn", on => _owner?.SetForceTicking(on));
        Toggle("FocusOn", on => _settings.FocusDoneEnabled = on);
        Toggle("RestOn", on => _settings.RestDoneEnabled = on);
        Toggle("IdleOn", on => _settings.IdleEnabled = on);
        Toggle("AlarmsOn", on => _settings.AlarmsEnabled = on);

        // ⚠️ 到点跑命令**仍然不持久化**（DECISIONS E8）：这里改的是 MainWindow 上那个
        //    内存字段，重启之后一律是关的。设置窗口改不了这一点，也不该能改
        this.FindControl<Button>("ConfigureOnline")!.Click += async (_, _) =>
        {
            if (_owner is not { } owner) return;
            await new SqlWindow(owner, owner.Store).ShowDialog(this);
            ShowCommandPreview();
        };

        Toggle("ExecuteOn", on => { _owner?.SetCommandArmed(on); ShowCommandPreview(); });
        ShowCommandPreview();

        // ⚠️ 音量改了要**当场试听**：合成是按音量烘焙的，不听一下不知道调到哪儿了。
        //    Tick.Play 内部音量一变会重新合成并 MacAudio.Forget——不 Forget 的话拖滑块
        //    什么都不会发生（SystemSoundID 在创建那一刻就把音频吃进去了）
        var volume = this.FindControl<Slider>("TickVol")!;
        volume.PropertyChanged += (_, e) =>
        {
            if (_loading || e.Property != RangeBase.ValueProperty) return;
            _settings.TickVolume = (int)Math.Round(volume.Value);
            Tick.Play(DateTime.Now.Second, _settings.TickVolume);
            _settings.Save();
        };

        this.FindControl<Button>("OpenFolder")!.Click += (_, _) => AppData.OpenInFileManager();

        Closed += (_, _) => _settings.Save();
    }

    /// <summary>
    /// 开关开着时把**到点会跑的那一条命令原文**显示出来。
    ///
    /// ⚠️ 这不是说明文字，是**值**本身——而且这个值多半是关机命令，
    /// **按下开关之前有权知道按的是什么**（v3 的 E14）。没配好就直说没配好。
    /// </summary>
    private void ShowCommandPreview()
    {
        var preview = this.FindControl<TextBlock>("CommandPreview")!;
        var on = _owner?.CommandArmed == true;
        preview.IsVisible = on;
        if (!on) return;

        preview.Text = _owner?.CommandForThisOs is { Length: > 0 } cmd
            ? cmd
            : "(no command for this OS — check `alarmCommand` and the `command` table)";
    }

    /// <summary>卡片底色跟着主题走。取钟面调色板里的值，不新增色号。</summary>
    private void PaintCards()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var palette = dark ? DialPalette.Dark : DialPalette.Light;
        var back = new SolidColorBrush(palette.Card);
        var border = new SolidColorBrush(palette.Tick, 0.25);

        foreach (var name in new[] { "CardTick", "CardFocus", "CardRest", "CardIdle", "CardAlarms", "CardCommand" })
        {
            var card = this.FindControl<Border>(name)!;
            card.Background = back;
            card.BorderBrush = border;
        }
    }

    private void Fill(string name, IReadOnlyList<string> available, string? current)
    {
        var box = this.FindControl<ComboBox>(name)!;
        box.ItemsSource = available;
        box.SelectedItem = current is not null && available.Contains(current) ? current : null;
    }

    /// <summary>挑一个就**当场放一遍**——挑音色不听一下没有意义。</summary>
    private void Wire(string name, Action<string> assign)
    {
        var box = this.FindControl<ComboBox>(name)!;
        box.SelectionChanged += (_, _) =>
        {
            if (_loading || box.SelectedItem is not string picked) return;
            assign(picked);

            // ⚠️ 试听是 `Play` 一遍，不是 `Repeat` 四遍：这是在挑音色，不是在排练打扰
            Sound.Play(picked);
            _settings.Save();
        };
    }

    private void Toggle(string name, Action<bool> assign)
    {
        var sw = this.FindControl<ToggleSwitch>(name)!;
        sw.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            assign(sw.IsChecked == true);
            _settings.Save();
        };
    }
}
