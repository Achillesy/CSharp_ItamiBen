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
            ShowCommandBranch();
        };

        Toggle("ExecuteOn", on => { _owner?.SetCommandArmed(on); ShowCommandBranch(); });
        ShowCommandBranch();

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
    /// 把这张卡**当前活着的那个分支**显示出来。闹钟到点只会二选一（<c>CheckAlarm</c>：
    /// armed 就跑命令、并且 return，不响铃），所以卡上那两样东西永远只有一样是真的：
    ///
    /// <list type="bullet">
    ///   <item><b>Ring</b>（开关关着）——上面那个音色是活的，命令原文不显示。</item>
    ///   <item><b>Run</b>（开关开着）——命令原文显示出来，而那个音色**禁用**：
    ///   到点根本不会响，它这会儿不是一个能生效的设置。</item>
    /// </list>
    ///
    /// ⚠️ 禁用不是装饰，是**把二选一做出来**：光靠 Run / Ring 两个字，那还只是写在标签上；
    /// 真的关掉那一半，卡片自己就说清楚了哪一半不算数（2026-09-18 用户定的：
    /// 既然是二选一，死掉那一侧就该真的不能改，而不是只变淡）。
    /// ⚠️ 用 <c>IsEnabled</c> 而不是 <c>IsVisible</c>：后者会让卡片高度跳一下，
    /// 而且「它还在，只是这会儿不算数」正是要传达的意思。**扳回 Ring 立刻就活过来**，
    /// 所以这不会把人锁在外面。
    ///
    /// ⚠️ 命令原文不是说明文字，是**值**本身——这个值多半是关机命令，
    /// **按下开关之前有权知道按的是什么**（v3 的 E14）。没配好就直说没配好。
    /// </summary>
    private void ShowCommandBranch()
    {
        var on = _owner?.CommandArmed == true;

        this.FindControl<ComboBox>("AlarmSound")!.IsEnabled = !on;

        var preview = this.FindControl<TextBlock>("CommandPreview")!;
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

        PaintArmedSwitch();
    }

    /// <summary>
    /// Command 那个开关**扳到 Run 那一侧时是红的**，其余五个照旧走系统强调色。
    ///
    /// ⚠️ 为什么只有它特殊：另外五个是「响 / 不响」，关掉最坏就是安静；这一个扳过去
    /// 是**到点替你执行一条命令**（多半是关机）。红在这扇窗里已经有含义——底下
    /// <c>ConfigureOnline</c> 那颗按钮就是红的，注释写着「这一整块是利器」。同一类，同一个红。
    ///
    /// ⚠️ **只在开着时喊**：关着的时候它本来就无害，静息态跟别的开关长一样是对的，
    /// 不该让一张卡永远看起来像报错。
    ///
    /// ⚠️ **写死 <c>#D6453F</c>，不跟主题走**——这是这个仓库里「利器红」的既定值，
    /// 另外三处一字不差：<c>App.axaml</c> 的 Give up、本窗口的 <c>ConfigureOnline</c>、
    /// <c>SqlWindow.axaml</c> 的 Apply。理由写在 App.axaml 上：**底色是语义色，不跟主题**。
    ///
    /// ⚠️ **别改成 <see cref="DialPalette.OffTask"/>**（2026-09-18 我就这么写过一版）：
    /// 调色板里那两档红是**给表盘用的**——盘面会从白换成深色，红得跟着换才看得清。
    /// 按钮不在盘面上，没这个问题；跟着换的结果是系统深色下这一个开关变成
    /// <c>#E9635C</c>，而同一扇窗里另外两颗红按钮还是 <c>#D6453F</c>，当场分家。
    /// 搜 <c>D6453F</c> 能一次找齐这四处，那是故意的。
    ///
    /// ⚠️ 覆盖的是 Fluent 主题的**资源键**，不是去改模板内部的那几个命名部件：
    /// 部件名是主题的实现细节，Avalonia 升级换了名字会**静默失效**（开关变回蓝的，
    /// 不报错）；资源键是给外面覆盖用的，稳得多。三个态都要写，否则鼠标一悬停就跳回蓝色。
    /// </summary>
    private void PaintArmedSwitch()
    {
        var armed = new SolidColorBrush(Color.Parse("#D6453F"));
        var sw = this.FindControl<ToggleSwitch>("ExecuteOn")!;
        foreach (var key in new[]
                 {
                     "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                     "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed",
                 })
            sw.Resources[key] = armed;
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
