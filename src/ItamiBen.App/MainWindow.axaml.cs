using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using ItamiBen.App.Platform;
using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 产品界面：钟面 + 勾选小目标 + Start / Give up。
///
/// 它自己**不判定任何东西**——每一拍把采样原样交给 <see cref="Round"/>，再把
/// <see cref="Round.Cells"/> 和 <see cref="Round.Project"/> 交给钟面。判定的规则
/// 一份在 Core，渲染的规则一份在 <see cref="DialControl"/>，这里只负责把线接上。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 闹钟响几遍。**4 遍**（v3 的 E11）：它是这个程序里**唯一没有第二次机会**的声音
    /// ——响完什么都不留（黄针不动、不弹窗），走个神就错过了。间隔 = 音频文件自己的长度。
    /// </summary>
    private const int AlarmRings = 4;

    /// <summary>
    /// alarms.cron 到点响几遍。**2 遍**（v3 的 J10）：它响完还留着一分钟的提示条，
    /// 漏听还能看回来；闹钟给 4 遍是因为它响完什么都不留。
    /// </summary>
    private const int AlarmsListRings = 2;

    private readonly Sampler _sampler = new();
    private readonly List<CheckBox> _goalBoxes = [];

    /// <summary>每个目标那一行右边的累计数字，跟 <see cref="_goalBoxes"/> 一一对应。</summary>
    private readonly List<TextBlock> _goalTotals = [];

    private GoalRules _rules = GoalRules.Empty;
    private string? _rulesError;
    private GoalTotals _totals = new();
    private Round? _round;

    /// <summary>观测库和录制器。开不起来就一个都没有——录不上不该把程序搞崩。</summary>
    private SampleStore? _store;
    private Recorder? _recorder;

    /// <summary>最近一拍的采样，录制器从这里取前台窗口（Sampler 已经读过了，不重复读）。</summary>
    private Sample _last;

    /// <summary>
    /// 上一次从库里整个重建 / 上一次跑整分钟那一串，用的是**绝对分钟序号**
    /// （Unix 秒 ÷ 60），不是 <c>DateTime.Minute</c>（0~59）。
    ///
    /// ⚠️ 用 0~59 的话，**睡眠恰好整小时之后两者会相等**，那一分钟的重建和提醒检查
    /// 直接被跳过——不报错，只是安静地少跑一次。这类错正是这个项目最怕的那种。
    /// </summary>
    private long _lastRebuiltMinute = -1;

    private long _lastMinute = -1;

    /// <summary>
    /// 上一次重建时算出来的离开区间条数。
    /// **用来分辨「对账对不上」的两种原因**——见 <see cref="Rebuild"/> 里那段。
    /// </summary>
    private int _lastAwaySpans;

    /// <summary>alarms.cron，每分钟重读一次——用户手写的文件，改完不该还要重启。</summary>
    private IReadOnlyList<CronEntry> _alarms = [];

    /// <summary>
    /// alarms.cron 的去重水位线。**纯内存、不持久化，初始化成启动那一刻**（v3 的 J7）：
    /// 程序关闭期间错过的条目重开后直接跳过，不倒回去补。
    /// </summary>
    private DateTime _alarmsProcessedThrough = DateTime.Now;

    /// <summary>提示条显示到哪一刻。null = 没在显示。⚠️ 用截止时刻不用布尔量，同 E6。</summary>
    private DateTime? _bannerUntil;

    /// <summary>
    /// 右键菜单那两项。**留着引用**：主题一换，图标的墨色要跟着重画
    /// （菜单只构造一次，不会自己更新）。图钉那项还要跟右上角的图标**联动**。
    /// </summary>
    private MenuItem? _pinItem;
    private MenuItem? _closeItem;

    /// <summary>SIGTERM / SIGINT 的登记，要留着引用否则会被 GC 掉。</summary>
    private readonly List<IDisposable> _signals = [];

    private readonly Settings _settings = Settings.Load();
    private readonly AlarmClock _alarm = new();

    /// <summary>连拨的计数和上一拍的时刻，见 <see cref="OnAlarmWheel"/>。</summary>
    private DateTime _lastWheelAt = DateTime.MinValue;
    private int _wheelStreak;

    /// <summary>两拍之间超过这么久就算断了，下一拍从 1 分钟/格重新起步。</summary>
    private const int WheelStreakGapMs = 300;

    /// <summary>
    /// 调整期静默的**截止时刻**。⚠️ 用时刻不用布尔量（v3 的 E6）：布尔 + `Task.Delay`
    /// 复位的话，滚轮连续来时**早到的复位会掐断晚到的那次调整期**。
    /// </summary>
    private DateTime _alarmQuietUntil = DateTime.MinValue;

    /// <summary>本轮是否已落盘。**落盘是一个动作，不是一条政策**（DECISIONS C5）。</summary>
    private bool _written;

    private int _focusMinutes = 25;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        Log.Start();
        LoadRules();
        _totals = Totals.Load();
        OpenStore();

        BuildGoals();
        foreach (var b in _goalBoxes)
            b.IsChecked = _settings.SelectedGoals.Contains((string)b.Content!);
        ResumeRound();   // ⚠️ 排在后面：接回来的那一轮说了算，会把上面这几个勾覆盖掉

        this.FindControl<Button>("ActionBtn")!.Click += (_, _) => OnAction();
        this.FindControl<Button>("GrantBtn")!.Click += (_, _) =>
        {
            // ⚠️ 平台层整层包在 try 里：2026-09-15 这个按钮把整个 app 搞崩过一次
            // （SIGSEGV in CFGetTypeID）。根因已修，但**读不到权限不该把程序带走**。
            try { ForegroundWindow.RequestTitlePermission(); }
            catch (Exception e) { Log.Error("RequestTitlePermission failed", e); }
        };

        // 读回时刻只为了显示黄针残影，**不激活**——关着程序时错过的闹钟不补响（v3 的 E7）
        var minutes = this.FindControl<Slider>("Minutes")!;
        minutes.Value = _settings.FocusMinutes ?? 25;
        _focusMinutes = (int)minutes.Value;
        minutes.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            _focusMinutes = (int)Math.Round(minutes.Value);
            UpdateUi();
        };

        _alarm.Restore(_settings.AlarmAt);
        Log.Line($"alarm restored: at={_settings.AlarmAt:yyyy-MM-dd HH:mm} sound={_settings.AlarmSound ?? "(none)"}");

        // ⚠️ 拨针挂在**钟面本身**上，没有独立按钮（v3 的 E4：Button 内部会把
        //    PointerPressed 标 Handled，挂在钟面上的普通订阅收不到）。
        this.FindControl<DialControl>("Dial")!.PointerWheelChanged += OnAlarmWheel;

        // null = 跟着系统走（第一次启动）；点过主题图标之后才钉死
        RequestedThemeVariant = _settings.DarkTheme switch
        {
            true => ThemeVariant.Dark,
            false => ThemeVariant.Light,
            null => ThemeVariant.Default,
        };

        ApplyLayout();
        ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();

        _sampler.Ticked += OnTick;
        _sampler.Start();

        // ⚠️ 关窗和 Cmd+Q 是两条不同的路，但**执行的是同一个写入动作**（C5）
        Closing += (_, _) => OnExit();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownRequested += (_, _) => OnExit();

        HookSignals();

        UpdateUi();
    }

    // ── 装配 ────────────────────────────────────────────────────────────────

    private void LoadRules()
    {
        var path = AppData.RulesPath();
        try
        {
            _rules = GoalRules.Parse(File.ReadAllText(path));
            Log.Line($"rules loaded from {path}: {string.Join(", ", _rules.SelectableGoals)}");
        }
        catch (Exception e)
        {
            // ⚠️ 规则读不了就一个目标都不给选——**宁可什么都做不了，也不能放行一切**。
            //    空规则匹配一切 = 约束当场归零，那正是这个程序唯一的卖点
            _rules = GoalRules.Empty;
            _rulesError = $"{Path.GetFileName(path)}: {e.Message}";
            Log.Error($"Failed to load {path}", e);
        }
    }

    /// <summary>
    /// 目标列表：一行一个，**左边勾选框、右边累计小时**（跟 v3 一致）。
    /// rules.json 有几个目标就有几行，窗口高度跟着走。
    /// </summary>
    private void BuildGoals()
    {
        var panel = this.FindControl<StackPanel>("GoalsPanel")!;
        foreach (var goal in _rules.SelectableGoals)
        {
            var box = new CheckBox { Content = goal, VerticalAlignment = VerticalAlignment.Center };
            box.IsCheckedChanged += (_, _) => UpdateUi();

            var total = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(box);
            Grid.SetColumn(total, 1);
            row.Children.Add(total);

            _goalBoxes.Add(box);
            _goalTotals.Add(total);
            panel.Children.Add(row);
        }
    }

    private void ApplyTheme()
    {
        var palette = ActualThemeVariant == ThemeVariant.Dark ? DialPalette.Dark : DialPalette.Light;
        this.FindControl<DialControl>("Dial")!.Palette = palette;
        this.FindControl<Border>("CardBackdrop")!.Background = new SolidColorBrush(palette.Card);
        ApplyChrome();
    }

    /// <summary>
    /// 把右上角两个图标和右键菜单**整个重画一遍**。
    ///
    /// 状态一变就整个重画，不去「改某一个属性」——跟钟面每拍整个重画是同一个路数，
    /// 不存在「上一次的状态没清干净」这回事。
    ///
    /// ⚠️ **图钉图标、右键菜单那一项、`settings.Pinned`、`Topmost` 是同一个状态的四种
    /// 表现，必须一起更新**。任何一处单独改都会让它们悄悄分家——而分家之后界面还是
    /// 好好的，只是说的不是同一件事。
    ///
    /// ⚠️ 主题一换图标要重画：墨色取自当前调色板，夜面下还用日面的深墨就只剩光晕
    /// 在撑形状了。
    /// </summary>
    private void ApplyChrome()
    {
        var palette = ActualThemeVariant == ThemeVariant.Dark ? DialPalette.Dark : DialPalette.Light;
        var dark = ActualThemeVariant == ThemeVariant.Dark;

        var pin = this.FindControl<Button>("PinBtn")!;
        pin.Content = ChromeIcons.Pin(_settings.Pinned, palette);
        pin.Classes.Set("on", _settings.Pinned);

        this.FindControl<Button>("ThemeBtn")!.Content = ChromeIcons.Theme(dark, palette);

        if (_pinItem is not null) _pinItem.IsChecked = _settings.Pinned;
        if (_closeItem is not null) _closeItem.Icon = ChromeIcons.Close(palette);
    }

    /// <summary>置顶的**唯一入口**——图标、菜单项、设置、窗口属性一次全对齐。</summary>
    private void SetPinned(bool pinned)
    {
        _settings.Pinned = pinned;
        Topmost = pinned;
        ApplyChrome();
    }

    /// <summary>
    /// 窗口尺寸、不透明度、置顶、拖动、右键菜单——**无边框那一套**。
    ///
    /// ⚠️ **不透明度要用 `OpacityMask`，不能用 `Opacity`**（v3 的 K29，实测出来的）：
    /// Avalonia 里 `Visual.Opacity` 是**逐个绘制操作**各自半透，不是「整个控件先合成
    /// 成一层再降透」。钟面那块白是画在木色**实心圆盘**之上的，逐笔半透会让木色透过
    /// 钟面混上来，出来是米黄——v3 的用户一眼看出「表盘泛黄」。headless 实测
    /// `Opacity=0.5` 时白面净区是 (207,189,176,A=201)，`OpacityMask=0.5` 是
    /// (255,255,255,A=127)。<br/>
    /// 顺带的好处：控件的 `Opacity` 保持 1，**命中测试完全不受影响**（拖钟面照常）。
    ///
    /// ⚠️ 只给**钟面**和**卡片底色**套：按钮和文字保持实心，低透明度下才读得了。
    /// </summary>
    private void ApplyLayout()
    {
        var metrics = WindowLayout.Current;
        Width = metrics.WindowWidth;
        Height = metrics.WindowHeight;

        var mask = new SolidColorBrush(Color.FromArgb((byte)Math.Round(WindowLayout.Opacity * 255), 255, 255, 255));
        this.FindControl<DialControl>("Dial")!.OpacityMask = mask;
        this.FindControl<Border>("CardBackdrop")!.OpacityMask = mask;

        Topmost = _settings.Pinned;

        var dial = this.FindControl<DialControl>("Dial")!;

        // ⚠️ 拖动挂在**钟面**上，不是整扇窗口——卡片里全是按钮，挂上去会跟点击打架。
        //    实际可拖范围是**圆的不是方的**：Avalonia 对自绘控件的命中测试是按真正画过
        //    的绘制操作逐个判的，所以四个角（画过一笔都没有的透明区）拖不动，而圆盘
        //    外沿再往外一点点能拖——那儿画着钟投在墙上的影子。这是 v3 实测的结论，
        //    用户看过之后认为圆形更好，没有去 override 成方的。
        dial.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);   // ⚠️ 只能在**按下那一刻**调，等「松开算不算点击」判完就来不及了
        };

        // 没有标题栏就没有系统菜单，这是唯一能关窗口的地方。只有两项，不做成一整套窗口菜单。
        // 走 Close() 而不是直接退进程——跟点 × 完全同一条路径（会走 OnExit 落盘）。
        var close = new MenuItem { Header = "Close window", Icon = ChromeIcons.Close() };
        close.Click += (_, _) => Close();

        _closeItem = close;
        _pinItem = new MenuItem { Header = "Keep on top", ToggleType = MenuItemToggleType.CheckBox };
        _pinItem.Click += (_, _) => SetPinned(!_settings.Pinned);

        dial.ContextMenu = new ContextMenu { ItemsSource = new[] { _pinItem, close } };

        this.FindControl<Button>("PinBtn")!.Click += (_, _) => SetPinned(!_settings.Pinned);
        this.FindControl<Button>("ThemeBtn")!.Click += (_, _) =>
        {
            // 图标画的是**当前状态**，所以点它就是切到另一头
            _settings.DarkTheme = ActualThemeVariant != ThemeVariant.Dark;
            RequestedThemeVariant = _settings.DarkTheme is true ? ThemeVariant.Dark : ThemeVariant.Light;
            // ActualThemeVariantChanged 会带出 ApplyTheme → ApplyChrome，这里不用手动调
        };

        RestoreWindowPosition();
    }

    /// <summary>
    /// 把上次的位置放回去。⚠️ **必须夹回屏幕内**：显示器拔掉、分辨率改了之后，上次
    /// 那个位置可能整个落在屏幕外，而无边框窗口连标题栏都没有，**再也找不着也够不着**。
    /// 夹不住就干脆居中。
    /// </summary>
    private void RestoreWindowPosition()
    {
        if (_settings.WindowX is not { } x || _settings.WindowY is not { } y) return;

        try
        {
            var area = Screens.All.Select(sc => sc.WorkingArea)
                               .FirstOrDefault(a => a.Contains(new PixelPoint(x, y)));
            if (area == default)
            {
                Log.Line($"saved window position ({x},{y}) is off-screen — centring instead");
                return;
            }

            Position = new PixelPoint(
                Math.Clamp(x, area.X, Math.Max(area.X, area.Right - 80)),
                Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - 80)));
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        catch (Exception e)
        {
            Log.Error("Failed to restore the window position", e);
        }
    }

    // ── 每一拍 ──────────────────────────────────────────────────────────────

    private void OnTick(Sample s)
    {
        _last = s;

        if (_round is { Ending: null } round)
        {
            // ⚠️ **只有专注阶段才录**（DECISIONS F4）：按 Start 开始，达成就停。
            //    休息期间不写行——环上那一段本来就画淡蓝块，有没有观测都一样。
            if (round.Phase == RoundPhase.Focusing && _recorder is { } rec && rec.Record(s.At))
            {
                // 写进库了，同一秒也喂给环：这样读数是实时的，不用等下一次重建。
                // 两条路算出来的是同一个结果（重放幂等），下面每分钟的重建会对账。
                // ⚠️ 实时这条路只能按**当前这一秒**的 idle 判，所以跨过门槛之前那 180 秒
                //    会先画成红格——每分钟从库重建时 AwayMap 会跨行回溯，把它们纠正成
                //    空白。v3 对这个「回溯改写」有同样的行为，是语义不是 bug
                var j = round.Observe(s.At, s.App, s.Title, s.Idle >= AwayMap.ThresholdSeconds);
                if (j is { } judged)
                    Log.Line($"{judged.Outcome,-10} focused={round.FocusedSeconds,-5} slack={round.SlackSeconds,-5} "
                           + $"idle={_last.Idle,-5} app={s.App,-18} title={s.Title}");
            }
            else
            {
                // 这一秒已经记过了（一秒十拍），或者在休息。时间照样要推进
                round.Advance(s.At);
            }

            if (round.Ending is null && MinuteOf(s.At) != _lastRebuiltMinute)
            {
                _lastRebuiltMinute = MinuteOf(s.At);
                Rebuild(s.At);
            }

            if (_round?.Ending is { } reason) Settle(reason);
        }

        // ⚠️ 整分钟那一串排在闹钟**之前**（v3 的 J10）：两边都要出声时，
        //    Windows 的 winmm 是单通道、后响的会掐断先响的，而闹钟响完什么都不留、
        //    清单响完还留着一分钟的提示条 —— 所以让闹钟赢。
        if (MinuteOf(s.At) != _lastMinute)
        {
            _lastMinute = MinuteOf(s.At);
            OnMinute(s.At.LocalDateTime);
        }

        CheckAlarm();
        UpdateUi(s);
    }

    /// <summary>
    /// 整分钟那一串。**顺序是定死的**：
    /// <list type="number">
    ///   <item>提示条到期收起——⚠️ 必须排在画新提示条**之前**，反过来会把第 ② 步
    ///         刚画上的那条当场擦掉；</item>
    ///   <item>alarms.cron 到点检查（响 2 遍 + 提示条 + 日志）；</item>
    ///   <item>小红圈位置重算——每拍整个重算，不存在「清除上一次画的圆」这回事。</item>
    /// </list>
    /// </summary>
    /// <summary>绝对分钟序号。见 <see cref="_lastRebuiltMinute"/> 为什么不能用 0~59。</summary>
    private static long MinuteOf(DateTimeOffset at) => at.ToUnixTimeSeconds() / 60;

    private void OnMinute(DateTime now)
    {
        if (_bannerUntil is { } until && now >= until) ShowBanner(null);

        _alarms = LoadAlarms();
        CheckAlarmsList(now);
        RefreshAlarmsDot(now);
    }

    /// <summary>每分钟重读一次：这是用户手写的文件，改完不该还要重启。读不了就当没有。</summary>
    private static IReadOnlyList<CronEntry> LoadAlarms()
    {
        try
        {
            var path = AppData.AlarmsPath();
            return File.Exists(path) ? AlarmsList.Parse(File.ReadAllText(path)) : [];
        }
        catch (Exception e)
        {
            Log.Error("Failed to read alarms.cron", e);
            return [];
        }
    }

    /// <summary>
    /// 到点的条目。
    ///
    /// ⚠️ **反馈只有日志这一条正向渠道**（v3 的 J16）：解析不了的行安静跳过，不记日志、
    /// 不提示、不统计加载了几条。知情代价——一个 typo = 这条提醒永远不响，屏幕上和日志里
    /// 都零反馈。诊断方式是「提醒没响 → 翻日志查不到记录 → 反推自己写错了」，
    /// 所以成功那一行**必须带上命中的表达式原文**，否则多条规则时只知道响过、
    /// 不知道是哪一行响的。
    /// </summary>
    private void CheckAlarmsList(DateTime now)
    {
        var due = AlarmsList.Due(_alarms, _alarmsProcessedThrough, now);
        _alarmsProcessedThrough = now;
        if (due.Count == 0) return;

        foreach (var e in due)
            Log.Line($"alarms.cron fired {e.At:HH:mm} [{e.Expression}] {e.Text}");

        ShowBanner(string.Join('\n', due.Select(e => $"{e.At:HH:mm}   {e.Text}")),
                   new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1));
        Sound.Repeat(_settings.AlarmsSound, AlarmsListRings);
    }

    private void RefreshAlarmsDot(DateTime now)
    {
        var nextDue = AlarmsList.NextDue(_alarms, now);
        var next = nextDue.Count > 0 ? nextDue[0] : (AlarmEntry?)null;

        var dial = this.FindControl<DialControl>("Dial")!;
        dial.AlarmsDotMinutes = AlarmsList.DotPosition(next, now);
        dial.AlarmsDotMultiple = nextDue.Count > 1;
    }

    /// <summary><paramref name="text"/> 为 null 就是收起。</summary>
    private void ShowBanner(string? text, DateTime? until = null)
    {
        _bannerUntil = text is null ? null : until;
        this.FindControl<Border>("AlarmBanner")!.IsVisible = text is not null;
        if (text is not null) this.FindControl<TextBlock>("AlarmBannerText")!.Text = text;
    }

    /// <summary>
    /// **每分钟从库里把整个环重建一次。**
    ///
    /// 这不是优化，是这个架构的地基：「任何时候根据库里的内容重建 120 分钟的环，
    /// 结果都一样」——靠的是 <see cref="Round.Observe"/> 那个只进不退的哨兵让重放幂等。
    /// 崩溃恢复走的也是这条路。
    ///
    /// ⚠️ 重建完还要 <see cref="Round.Advance"/> 到此刻：库里最后一行到现在之间可能
    /// 什么都没有（程序没跑、电脑睡了），那段时间**照样从环上过去了**。
    ///
    /// 顺带对一次账：实时那条路和重建这条路应该算出同一个数，不一样就说明库没写进去。
    /// </summary>
    /// <param name="compare">
    /// 跟当前这个环对一次账。⚠️ **恢复那条路要传 false**：那时手里的环是刚构造出来的
    /// 空壳，跟重建结果必然不同，对账只会吐出一条假警告。
    /// </param>
    private void Rebuild(DateTimeOffset now, bool compare = true)
    {
        if (_round is not { } live || _store is null) return;

        try
        {
            // live.StartedAt 已经是抹到整分的了，Round 的构造再抹一次是幂等的
            var rows = _store.Read(live.StartedAt, now.AddSeconds(1));

            // ⚠️ 离开区间必须**跨行**算：门槛是事后才跨过的，一行一判会把锁屏画成红格
            //    （macOS 锁屏读到的是 loginwindow，不是空字符串——2026-09-16 实测）
            var away = AwayMap.Of(rows);

            var rebuilt = new Round(live.StartedAt, live.FocusMinutes, live.Goals, _rules);
            foreach (var o in rows)
                rebuilt.Observe(o.At, o.App, o.Title, away.Covers(o.At));
            rebuilt.Advance(now);

            // ⚠️ **对不上有两种完全不同的原因，别混成一条警告**（2026-09-16 实机撞到）：
            //
            //   ① 离开区间**新增**了一条 ⇒ 跨过 180 秒门槛，前面那最多 179 秒被追认成
            //      「人不在」。实时那条路当时把它们记成了专注（idle 还没到门槛），
            //      重建把它们拿掉——**这是设计本身，不是错**；
            //   ② 区间条数没变却仍然对不上 ⇒ 真的有秒没写进库，那才是要查的。
            //
            // 头一版把两者都打成 "有秒没写进库"，实机第一次跨门槛就报了一条假警告
            // （live=532 → db=353，正好 179 秒）。**一个会说谎的自检比没有自检更糟。**
            if (compare && rebuilt.FocusedSeconds != live.FocusedSeconds)
            {
                if (away.Spans.Count > _lastAwaySpans)
                    Log.Line($"retroactive away: focused {live.FocusedSeconds}s → {rebuilt.FocusedSeconds}s "
                           + $"（跨过门槛，之前那段被追认成离开；away={away.Spans.Count}）");
                else
                    Log.Warn($"rebuild mismatch: live={live.FocusedSeconds}s db={rebuilt.FocusedSeconds}s "
                           + "— 区间条数没变却对不上，说明真的有秒没写进库");
            }
            _lastAwaySpans = away.Spans.Count;

            Log.Line($"rebuilt from db: minute={rebuilt.CurrentMinute,-4} focused={rebuilt.FocusedSeconds,-5} "
                   + $"slack={rebuilt.SlackSeconds,-5} rows={rows.Count,-5} away={away.Spans.Count} "
                   + $"phase={rebuilt.Phase}");
            _round = rebuilt;
        }
        catch (Exception e)
        {
            // 重建失败就继续用实时那个环——读不了库不该把正在跑的一轮毁掉
            Log.Error("Failed to rebuild the round from samples.db", e);
        }
    }

    /// <summary>
    /// **崩溃恢复。** 库里还有没终结的一轮就把它接回来。
    ///
    /// 这是新架构白捡的性质：本轮的状态（起点 / 档位 / 目标）在 `round` 表里，
    /// 观测在 `sample` 表里，**重放一遍就全回来了**。v4 原来的 C5 明写着
    /// 「崩在第 119 分钟，那 119 分钟全没了，没有后路」——那个代价现在没有了。
    ///
    /// 三种结局：
    /// <list type="bullet">
    ///   <item>还在两小时环里 ⇒ 接着跑，界面把目标和档位一并还原；</item>
    ///   <item>离现在超过两小时（或余量早就耗尽）⇒ <see cref="Round.Advance"/> 会让它
    ///         当场触底，走同一个终结动作；</item>
    ///   <item>rules.json 改过、那个目标没了 ⇒ 没法重放，就地终结，不猜。</item>
    /// </list>
    /// </summary>
    private void ResumeRound()
    {
        if (_store?.OpenRound() is not { } rec) return;

        try
        {
            _round = new Round(rec.StartedAt, rec.FocusMinutes, rec.Goals, _rules);
        }
        catch (ArgumentException e)
        {
            // 目标被禁用/删掉了。**不猜、不降级**——宁可这一轮作废
            Log.Error($"Cannot resume the round started at {rec.StartedAt:HH:mm}", e);
            _store.EndRound(rec.StartedAt, DateTimeOffset.Now, nameof(EndReason.Closed));
            _round = null;
            return;
        }

        _written = false;
        _lastRebuiltMinute = -1;
        _lastAwaySpans = 0;
        Rebuild(DateTimeOffset.Now, compare: false);   // 从 sample 重放 + 补最后一段

        foreach (var b in _goalBoxes) b.IsChecked = rec.Goals.Contains((string)b.Content!);
        _focusMinutes = rec.FocusMinutes;
        this.FindControl<Slider>("Minutes")!.Value = rec.FocusMinutes;

        if (_round?.Ending is { } reason)
        {
            Log.Line($"resumed round from {rec.StartedAt:HH:mm} had already ended: {reason}");
            Settle(reason);
        }
        else
        {
            Log.Line($"resumed round from {rec.StartedAt:HH:mm}: focus={rec.FocusMinutes}min "
                   + $"focused={_round?.FocusedSeconds}s slack={_round?.SlackSeconds}s "
                   + $"goals={string.Join("/", rec.Goals)}");
        }
    }

    private void OpenStore()
    {
        try
        {
            _store = SampleStore.Open(AppData.SamplesPath());
            _recorder = new Recorder(_store, () => (_last.App, _last.Title), () => _last.Idle);
            var (apps, titles, samples) = _store.Counts;
            Log.Line($"samples.db opened: {samples} samples, {apps} apps, {titles} titles, "
                   + $"oldest={_store.Oldest:yyyy-MM-dd HH:mm:ss} newest={_store.Newest:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception e)
        {
            // 录不上也不能崩：环会退化成「一秒都没采到」，但程序照跑
            Log.Error("Failed to open samples.db", e);
        }
    }

    /// <summary>
    /// 滚轮拨闹钟：**前滚逆时针、后滚顺时针**。慢拨一格 1 分钟，**连着快拨会加速**。
    ///
    /// ⚠️ **方向是用户点名的，别按自己的「直觉」翻转**（v3 的 E3 明写着这一条）。
    ///
    /// ⚠️ **加速看的是拨的节奏，不是单次事件的大小**（v3 2026-08-02 改的）。
    /// 原来那版读 <c>Math.Abs(e.Delta.Y) / 120</c>——120 是 **Win32 `WM_MOUSEWHEEL`**
    /// 的单位，而 Avalonia 的 <c>Delta.Y</c> **一格就是 1.0**。于是那个除法永远是 0.008，
    /// 被 <c>Math.Max(1, …)</c> 拉回 1，档位判断永远落在第一档：**那道加速梯子一次都
    /// 没跑起来过**，而且不报错。CLAUDE.md 的硬性约束里点名的就是这个坑。
    ///
    /// Avalonia 里快拨表现为**事件更密**而不是 Delta 更大，所以正确的做法是数
    /// 「连着拨了几格」——间隔超过 <see cref="WheelStreakGapMs"/> 毫秒就断档重来。
    /// 这样慢拨仍然是一分钟一分钟微调，快拨一口气能扫过几个小时。
    /// </summary>
    private void OnAlarmWheel(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;

        var now = DateTime.Now;
        _wheelStreak = (now - _lastWheelAt).TotalMilliseconds <= WheelStreakGapMs ? _wheelStreak + 1 : 1;
        _lastWheelAt = now;

        // 一串连拨里：1 → 3 → 8 → 15 → 30 分钟/格。一圈 12 小时是 720 格，
        // 30 分钟/格时二十来下就能扫完；一松手立刻回到 1 分钟/格，微调不受影响。
        var step = _wheelStreak switch
        {
            <= 2 => 1,
            <= 5 => 3,
            <= 10 => 8,
            <= 20 => 15,
            _ => 30,
        };

        // 高精度触控板一次可能报好几格，一并乘进去
        var notches = Math.Max(1, (int)Math.Round(Math.Abs(e.Delta.Y)));
        var direction = e.Delta.Y > 0 ? -1 : +1;

        _alarm.Bump(direction * notches * step * AlarmClock.SlotMinutes, now);
        _alarmQuietUntil = now.AddSeconds(2);

        // ⚠️ 拨针要留痕：它的后果（响铃）可能几小时后才发作，到时候「这闹钟哪来的」
        //    完全无从查起。2026-09-16 实测就撞上一次——日志里只有 `alarm fired`，
        //    查不出是谁把它从 15:32 拨到 10:22 的
        Log.Line($"alarm set to {_alarm.FireAt:yyyy-MM-dd HH:mm} (wheel {direction * notches * step:+#;-#;0} min)");
        e.Handled = true;
        UpdateUi();
    }

    /// <summary>
    /// 闹钟到点了没有。挂在采样节拍上（100ms），所以误差不到一拍。
    ///
    /// ⚠️ v3 把它挂在整分钟节拍上，是因为那边同一分钟里还有 AW 查询、清单、三声通知
    /// 要排先后（它的 L13）。v4 这一拍只有闹钟一件事，没有顺序可排，挂在采样节拍上
    /// 反而更准。<see cref="AlarmClock.ShouldFire"/> 自带一次性，重复调用无害。
    /// </summary>
    private void CheckAlarm()
    {
        var now = DateTime.Now;
        if (now < _alarmQuietUntil) return;      // 正在拨针，别当场响（E6）
        if (!_alarm.ShouldFire(now)) return;

        _alarm.MarkFired();                       // 先消费掉再出声：响铃失败也不该让它反复响
        Log.Line($"alarm fired: {_alarm.FireAt:HH:mm} sound={_settings.AlarmSound ?? "(none)"} ×{AlarmRings}");
        Sound.Repeat(_settings.AlarmSound, AlarmRings);
    }

    /// <summary>
    /// **`kill` 也走受控退出。**
    ///
    /// ⚠️ Avalonia 的 `Closing` / `ShutdownRequested` **对 SIGTERM 一律不触发**——
    /// 而 `run-macos.sh` 正是用 `pkill` 停进程的。症状很温和：一轮明明正常跑完，
    /// 下次启动闹钟却退回上上次的值（2026-09-16 实测撞到）。
    ///
    /// 挂上之后 `kill`、`pkill`、注销、关机都能走到 <see cref="OnExit"/>。
    /// ⚠️ **`kill -9` 仍然救不了**，那是内核直接抹掉进程——但那条路有观测库兜着
    /// （DECISIONS F4：本轮状态在库里，重开就接回来）。
    ///
    /// 信号处理器跑在线程池线程上，而 <see cref="OnExit"/> 会碰 SQLite 连接和
    /// <c>_round</c>——**必须回到 UI 线程**，否则跟每秒的采样撞在一起。
    /// </summary>
    private void HookSignals()
    {
        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT })
        {
            try
            {
                _signals.Add(PosixSignalRegistration.Create(signal, ctx =>
                {
                    Log.Line($"{ctx.Signal} received — settling before exit");
                    try { Dispatcher.UIThread.Invoke(OnExit); }
                    catch (Exception e) { Log.Error("Failed to settle on signal", e); }
                    // Cancel = false：照常让进程退出，我们只是抢在它之前把账写完
                }));
            }
            catch (Exception e)
            {
                Log.Error($"Cannot hook {signal}", e);
            }
        }
    }

    /// <summary>
    /// 受控退出。⚠️ 两件事，**都必须做**：本轮落盘（C5）和闹钟时刻落盘（E7）。
    /// 后者跟有没有正在跑的一轮无关——所以它不能藏在 <see cref="Settle"/> 里。
    ///
    /// **幂等**：`Closing` / `ShutdownRequested` / 信号三条路都会调它，
    /// 而 <see cref="Settle"/> 有 <c>_written</c> 挡着，设置重写一遍也无害。
    /// </summary>
    private void OnExit()
    {
        Settle(EndReason.Closed);
        _settings.FocusMinutes = _focusMinutes;
        _settings.SelectedGoals = [.. _goalBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!)];
        _settings.AlarmAt = _alarm.FireAt;
        try { _settings.WindowX = Position.X; _settings.WindowY = Position.Y; }
        catch (Exception e) { Log.Error("Failed to read the window position", e); }
        _settings.Save();
        _store?.Dispose();
    }

    private void OnAction()
    {
        if (_round is { Ending: null })
        {
            // Give up 是用户的选择，**不拦**（DECISIONS C8）
            Settle(EndReason.GaveUp);
            UpdateUi();
            return;
        }

        var goals = _goalBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList();
        if (goals.Count == 0) return;

        _round = new Round(DateTimeOffset.Now, _focusMinutes, goals, _rules);
        _written = false;
        _lastRebuiltMinute = -1;
        _lastAwaySpans = 0;
        _store?.BeginRound(_round.StartedAt, _round.FocusMinutes, _round.Goals);
        Log.Line($"round started: focus={_focusMinutes}min break={_round.BreakMinutes}min "
               + $"deadline=min{_round.DeadlineMinute} budget={_round.BudgetSeconds}s goals={string.Join("/", goals)}");
        UpdateUi();
    }

    /// <summary>
    /// **这一轮的唯一写入动作。** 达成 / Give up / 触底 / 关窗 / Cmd+Q 全都走这里，
    /// <paramref name="reason"/> 只进日志——⚠️ 别按出口分叉（DECISIONS C5）：
    /// 那些秒是用户真干了活，算过的不能再拿走。
    /// </summary>
    private void Settle(EndReason reason)
    {
        if (_round is null || _written) return;

        _round.End(DateTimeOffset.Now, reason);

        // ⚠️ **顺序是有讲究的：先在库里标记终结，再写账本。**
        //    崩在两者之间 ⇒ 本轮的秒丢了，那是 C5 已经知情接受的失败方向。
        //    反过来（先写账本、后标记）崩了 ⇒ 下次启动会把同一轮**再结算一遍**，
        //    账本虚高，而虚高是不可逆的：算过的秒不能拿走，多算的也没法证明是多算的。
        // ⚠️ 写进库的是 **`_round.Ending`** 不是传进来的 `reason`：环可能已经自己终结了
        //    （触底 / 休息走完），那时 `End` 是空操作、`Ending` 保留的是真正的原因。
        //    现在每个调用点传的都对，但这条不该靠调用点保证——它只写一次，写错就永远错。
        _store?.EndRound(_round.StartedAt, _round.EndedAt ?? DateTimeOffset.Now,
                         (_round.Ending ?? reason).ToString());

        _totals.Add(_round.FocusedSecondsByGoal);
        Totals.Save(_totals);
        _written = true;

        Log.Line($"round settled: {_round.Ending} focused={_round.FocusedSeconds}s "
               + $"by goal=[{string.Join(", ", _round.FocusedSecondsByGoal.Select(kv => $"{kv.Key}:{kv.Value}s"))}]");
    }

    // ── 显示 ────────────────────────────────────────────────────────────────

    private void UpdateUi(Sample? sample = null)
    {
        var dial = this.FindControl<DialControl>("Dial")!;
        var running = _round is { Ending: null };

        dial.AlarmMinutes = _alarm.Position;

        // ⚠️ **一轮终结，环就清空**（DESIGN §4.4：空闲那一行画的是「—」）。
        //    休息中 `Ending` 还是 null，所以淡蓝块照常留着——要清的只是**终结之后**。
        //
        //    别留着「让人再看一眼成绩」：真的分针还在走，而环是冻在起点那一圈上的，
        //    扫一眼极容易读成「现在正在走这圈」。成绩留在文字上就够了
        //    （`Done. 10 min of focus.` + 目标后面的累计）。空盘是下一轮的邀请。
        var live = _round is { Ending: null };
        dial.Cells = live ? _round!.Cells : [];
        dial.StartedAt = live ? _round!.StartedAt : null;
        dial.Projection = live ? _round!.Project() : null;
        dial.InvalidateVisual();

        this.FindControl<TextBlock>("AlarmText")!.Text = FormatAlarm();
        this.FindControl<TextBlock>("Readout")!.Text = ReadoutText();
        RefreshGoalTotals();

        var action = this.FindControl<Button>("ActionBtn")!;
        action.Content = running ? "Give up" : "Start";
        action.IsEnabled = running || _goalBoxes.Any(b => b.IsChecked == true);

        // ⚠️ 只有 Give up 是红的——它作废整轮。休息中**仍然是 Give up**（C8）
        action.Classes.Set("danger", running);

        // 提交之后目标和时长锁死——**一轮开始就不能再改**，规则是你事先写的
        foreach (var b in _goalBoxes) b.IsEnabled = !running;
        this.FindControl<Slider>("Minutes")!.IsEnabled = !running;

        UpdateStatus(sample);
    }

    private string ReadoutText()
    {
        if (_rulesError is not null) return $"No goals: {_rulesError}";
        if (_round is null)
            return _rules.SelectableGoals.Count == 0
                ? "No goals in rules.json yet."
                : "Pick what you are allowed to do.";

        var p = _round.Project();
        return _round.Phase switch
        {
            RoundPhase.Focusing => $"{Mins(p.CommitSeconds)} to go · {Mins(p.SlackSeconds)} of slack",
            RoundPhase.Resting => $"Break · {Mins(p.BreakEndSeconds - p.HandSeconds)} left",
            _ => _round.Ending switch
            {
                EndReason.Completed => $"Done. {Mins(_round.FocusedSeconds)} of focus.",
                EndReason.GaveUp => $"Gave up. {Mins(_round.FocusedSeconds)} counted anyway.",
                EndReason.RanOut => $"Ran out of slack. {Mins(_round.FocusedSeconds)} counted anyway.",
                _ => $"Stopped. {Mins(_round.FocusedSeconds)} counted anyway.",
            },
        };
    }

    /// <summary>
    /// 每个目标那一行右边的累计。**显示 = 文件里的总数 + 本轮实时累计**（DESIGN §4.5）。
    ///
    /// ⚠️ 格式跟 v3 一致：**小时、两位小数、不带单位**（`15.88`）。
    /// 账本里存的是整数秒（C7：1 Hz 采样，更细的单位是假精度），显示时才除 3600——
    /// 而且要写成 <c>3600.0</c>，整数除法会把小数位悄悄吃掉。
    ///
    /// ⚠️ **纯显示求和，一个字都不写**：`during.json` 只在受控退出时被写一次。
    /// </summary>
    private void RefreshGoalTotals()
    {
        var live = _written ? null : _round?.FocusedSecondsByGoal;

        for (var i = 0; i < _goalBoxes.Count && i < _goalTotals.Count; i++)
        {
            var goal = (string)_goalBoxes[i].Content!;
            var seconds = _totals[goal] + (live?.GetValueOrDefault(goal) ?? 0);
            _goalTotals[i].Text = (seconds / 3600.0).ToString("F2");
        }
    }

    private void UpdateStatus(Sample? sample)
    {
        bool granted;
        try { granted = ForegroundWindow.TitlePermissionGranted; }
        catch (Exception e) { Log.Error("TitlePermissionGranted failed", e); return; }

        this.FindControl<Button>("GrantBtn")!.IsVisible = !granted;
        this.FindControl<Border>("StatusBar")!.Background = new SolidColorBrush(
            granted ? Color.FromArgb(0x18, 0x80, 0x80, 0x80) : Color.FromRgb(0xC4, 0x5A, 0x28));

        // ⚠️ 库开不起来 ⇒ 一秒都录不进去 ⇒ **每一轮都会静默触底**。
        //    这是最坏的一种失败：程序照跑、钟面照转，只是永远不可能达成。必须说出来
        if (_store is null)
        {
            this.FindControl<TextBlock>("StatusText")!.Text =
                "Cannot open samples.db — nothing is being recorded, so every round will run out. "
                + "See itamiben.log.";
            this.FindControl<Border>("StatusBar")!.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x5A, 0x28));
            this.FindControl<Button>("GrantBtn")!.IsVisible = false;
            return;
        }

        if (!granted)
        {
            // ⚠️ 症状是「app 名读得到、标题读不到」。第一次见很容易误判成目标 app 的问题
            this.FindControl<TextBlock>("StatusText")!.Text =
                "No Accessibility permission → window titles are unreadable, so title rules never match. "
                + "Grant it in System Settings → Privacy & Security → Accessibility.";
            return;
        }

        if (sample is { } s)
            this.FindControl<TextBlock>("StatusText")!.Text =
                $"{(s.App.Length == 0 ? "—" : s.App)}   {(s.Title.Length == 0 ? "(no title)" : s.Title)}";
    }

    /// <summary>
    /// 闹钟时刻。
    ///
    /// ⚠️ **必须把「上弦了」和「黄针残影」分开**：两者在盘面上长得一模一样
    /// （v3 的 E7 明说过期闹钟只剩残影），不写出来用户读不出它还作不作数。
    /// </summary>
    private string FormatAlarm()
    {
        if (_alarm.FireAt is not { } at) return "";

        var now = DateTime.Now;
        var when = at.Date == now.Date ? at.ToString("HH:mm") : at.ToString("HH:mm") + " tomorrow";
        return _alarm.IsArmed(now) ? $"⏰ {when}" : $"⏰ {when} · off";
    }

    /// <summary>向上取整到分钟：读数因此每分钟才跳一次，跟格子封盘同步。</summary>
    private static string Mins(int seconds) => $"{(int)Math.Ceiling(Math.Max(seconds, 0) / 60.0)} min";

    private static string Hm(long seconds) => $"{seconds / 3600}h {seconds % 3600 / 60}m";
}
