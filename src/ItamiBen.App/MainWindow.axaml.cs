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

    /// <summary>
    /// 三声通知各响 **2 遍**（v3 的 E11）。比闹钟的 4 遍少，因为**它们各自落在一个
    /// 留在屏幕上的状态上**（环满了、淡蓝块、格子不再长），漏听还能看回来；
    /// 闹钟响完什么都不留，所以给 4 遍。
    /// </summary>
    private const int NotifyRings = 2;

    /// <summary>
    /// 键鼠空闲到这个秒数就提醒一次——**还没到「离开」的门槛**（180 秒），
    /// 人只是飘了。这一声是把你捞回来，不是事后报账。
    /// </summary>
    private const int IdleNudgeSeconds = 60;

    /// <summary>上一拍这一轮处在哪个阶段，用来抓「刚刚达成」「刚刚休息完」两个瞬间。</summary>
    private RoundPhase? _lastPhase;

    private readonly Sampler _sampler = new();
    /// <summary>
    /// 目标列表。**单选**——一轮只盯一个目标（DECISIONS C12 因此变成天然成立）。
    /// v3 也是单选，2026-09-16 用户确认这一直就是他要的。
    /// </summary>
    private readonly List<RadioButton> _goalBoxes = [];

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

    /// <summary>上一次响滴答的那一秒（绝对秒序号）。</summary>
    private long _lastTickSecond = -1;

    /// <summary>
    /// 刚才那一秒是不是**跑偏**。跑偏的判据必须是 <c>== OffTask</c>，
    /// ⚠️ **不能写成「不是 Focused」**（v3 的 N4）：那样「人不在」和「读不到」
    /// 也会算跑偏——锁个屏钟面就永远闪下去，而账本那段时间一秒都没扣，
    /// **屏幕跟账本对着说反话**。
    /// </summary>
    private bool _drifting;

    /// <summary>钟面此刻是不是反着的。跑偏期间每秒翻一次。</summary>
    private bool _inverted;

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

    /// <summary>上一次装配时的配置版本。跟库里对不上就重装（<see cref="ReloadConfigIfChanged"/>）。</summary>
    private long _configVersion;

    /// <summary>提示条显示到哪一刻。null = 没在显示。⚠️ 用截止时刻不用布尔量，同 E6。</summary>
    private DateTime? _bannerUntil;

    /// <summary>
    /// 右键菜单那两项。**留着引用**：主题一换，图标的墨色要跟着重画
    /// （菜单只构造一次，不会自己更新）。图钉那项还要跟右上角的图标**联动**。
    /// </summary>
    private MenuItem? _pinItem;
    private MenuItem? _tickItem;
    private MenuItem? _commandItem;

    /// <summary>
    /// 闹钟到点跑命令，开着没有。
    ///
    /// ⚠️ **故意不进 `settings.json`，每次启动一律是关的**（v3 的 E8）。
    /// 持久化它是**事故**：那条命令多半是关机/重启，重启之后被上一个会话的设置拍死，
    /// 而你根本不知道它还开着。想到点关机就必须在**本次会话里**手动打开一次。
    ///
    /// 做成普通字段而不是 Settings 属性，是让「不持久化」这件事**结构上做不到反面**
    /// ——v3 是放在 Settings 里、靠 `Load` 强制复位，那要靠人记得。
    /// </summary>
    private bool _commandArmed;

    /// <summary>
    /// 窗口最后一次**真实**的位置。
    ///
    /// ⚠️ **不能在退出那一刻现读 `Position`**：2026-09-16 实测那样存下来的是 `(0,0)`，
    /// 下次启动窗口就被拖到屏幕左上角。改成一有移动就记下来——反正拖窗口本来就会
    /// 连续触发 `PositionChanged`，取最后一个值天然是对的。
    /// </summary>
    private PixelPoint? _lastPosition;

    /// <summary>
    /// 「窗口停下来了吗」的计时器。拖动中每动一下都把它往后推，250ms 没动过 = 松手了。
    ///
    /// ⚠️ **为什么要等停下来，不能一边拖一边夹**：见 <see cref="ClampIntoScreen"/>。
    /// </summary>
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>正在由 <see cref="ClampIntoScreen"/> 挪窗口。挪出来的 `PositionChanged` 不算用户拖的。</summary>
    private bool _clamping;

    /// <summary>SIGTERM / SIGINT 的登记，要留着引用否则会被 GC 掉。</summary>
    private readonly List<IDisposable> _signals = [];

    /// <summary>⚠️ 在 <c>OpenStore()</c> 之后才装得进来——设置现在住在库里。</summary>
    private Settings _settings = new();
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

    /// <summary>退出路径走过没有。见 <see cref="OnExit"/>——**三条路都会调它**。</summary>
    private bool _exited;

    /// <summary>关窗口已经问过并得到许可。</summary>
    private bool _closeApproved;

    /// <summary>正在弹 Give up 的确认框，防止连点弹出两个。</summary>
    private bool _asking;

    private int _focusMinutes = 25;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        OpenStore();

        // ⚠️ **必须排在 OpenStore 后面**：设置住在库里（I12）。库要是打不开，
        //    这里拿到的是一套默认值——程序照样跑，只是记不住上次的选择。
        _settings = Settings.Load(_store);
        _totals = Totals.Load(_store);
        WindowLayout.Bind(_settings);
        AppData.RefreshAgentDoc();
        if (_store is { } db) Config.EnsureSeeded(db);
        LoadConfig();

        BuildGoals();

        // 把上次选的那个选回来；它没了（rules.json 改过）就退回第一个——
        // **永远有一个是选中的**，不让用户面对一个「什么都没选」的起点
        var saved = _goalBoxes.FirstOrDefault(b => (string)b.Content! == _settings.SelectedGoal);
        if (saved is not null) saved.IsChecked = true;
        else if (_goalBoxes.Count > 0) _goalBoxes[0].IsChecked = true;

        ResumeRound();   // ⚠️ 排在后面：接回来的那一轮说了算，会覆盖上面这个选择

        this.FindControl<Button>("ActionBtn")!.Click += (_, _) => OnAction();
        this.FindControl<Button>("GrantBtn")!.Click += (_, _) =>
        {
            // ⚠️ 平台层整层包在 try 里：2026-09-15 这个按钮把整个 app 搞崩过一次
            // （SIGSEGV in CFGetTypeID）。根因已修，但**读不到权限不该把程序带走**。
            try { ForegroundWindow.RequestTitlePermission(); }
            catch (Exception e) { Events.Error("permission", "RequestTitlePermission failed", e); }
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
        Closing += OnClosing;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownRequested += (_, _) => OnExit("shutdown requested");

        PositionChanged += (_, e) =>
        {
            // 还没显示出来时是 (0,0)，那不是真实位置
            if (e.Point is { X: 0, Y: 0 }) return;
            _lastPosition = e.Point;

            // 自己挪的不算拖动，否则夹一次就再排一次 settle，来回震荡
            if (_clamping) return;
            _settle.Stop();
            _settle.Start();
        };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            ClampIntoScreen();
        };

        HookSignals();

        UpdateUi();
    }

    // ── 装配 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 从库里装配规则和计划表。**配置住在库里**（DECISIONS I15）。
    ///
    /// ⚠️ 库开不了就是空规则——**宁可什么都做不了，也不能放行一切**：
    /// 空规则匹配一切 = 约束当场归零，那正是这个程序唯一的卖点。
    /// </summary>
    private void LoadConfig()
    {
        _rules = Config.LoadRules(_store, _settings);
        _alarms = Config.LoadSchedule(_store);
        _configVersion = _store?.ConfigVersion ?? 0;

        _rulesError = _rules.SelectableGoals.Count == 0
            ? "no goals configured — ask an agent to read AGENT.md"
            : null;
    }

    /// <summary>
    /// 有人改过配置就重装。**每分钟看一眼版本号。**
    ///
    /// ⚠️ 没有这一步的话，智能体改完要等你重启才生效——而「改了没反应」正是这个项目
    /// 最恨的那类失败。版本号是**智能体自己负责加一**的，AGENT.md 里写死了。
    /// </summary>
    private void ReloadConfigIfChanged()
    {
        if (_store is not { } store) return;
        long version;
        try { version = store.ConfigVersion; }
        catch (Exception e) { Events.Error("config", "Cannot read the config version", e); return; }
        if (version == _configVersion) return;

        LoadConfig();
        BuildGoals();
        Events.Info("config", $"reloaded at version {version}");
    }

    /// <summary>
    /// 目标列表：一行一个，**左边勾选框、右边累计小时**（跟 v3 一致）。
    /// rules.json 有几个目标就有几行，窗口高度跟着走。
    /// </summary>
    /// <summary>
    /// 铺目标行。**必须可以重复调**——配置一改就会重铺（`ReloadConfigIfChanged`）。
    ///
    /// ⚠️ 2026-09-16 实机撞到：原来它只在启动时调一次，所以**只往里加、从不清空**。
    /// 加了「改完配置当场重装」之后，界面上的目标列表**翻了一倍**（用户截图）。
    /// 这一族错跟退出路径那个（I17）是同一个形状：**一个函数原本「只跑一次」，
    /// 后来被第二个调用方接上，而它从来没为第二次做过准备。**
    ///
    /// ⚠️ 重铺要把选中项带过去：不带的话改一次配置就把用户刚选的目标弄丢了。
    /// </summary>
    private void BuildGoals()
    {
        var panel = this.FindControl<StackPanel>("GoalsPanel")!;
        var wasPicked = Picked();

        panel.Children.Clear();
        _goalBoxes.Clear();
        _goalTotals.Clear();

        foreach (var goal in _rules.SelectableGoals)
        {
            var box = new RadioButton
            {
                Content = goal,
                GroupName = "goal",
                VerticalAlignment = VerticalAlignment.Center,
            };
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

        // 选中项带过去；它没了（配置里删掉了）就退回第一个——**永远有一个是选中的**
        var keep = _goalBoxes.FirstOrDefault(b => (string)b.Content! == wasPicked)
                ?? _goalBoxes.FirstOrDefault();
        if (keep is not null) keep.IsChecked = true;
    }

    private void ApplyTheme() => ApplyPalette();

    /// <summary>
    /// 把当前该用的调色板铺下去。**跑偏时钟面翻成半反色**——只翻盘面 / 刻度 / 指针，
    /// 色环、木框、淡蓝块、小红圈一概不翻（那是账本本身，翻了就把语义拆了）。
    /// </summary>
    private void ApplyPalette()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var normal = dark ? DialPalette.Dark : DialPalette.Light;
        var palette = _inverted ? normal.WithFaceFrom(dark ? DialPalette.Light : DialPalette.Dark) : normal;

        this.FindControl<DialControl>("Dial")!.Palette = palette;
        this.FindControl<Border>("CardBackdrop")!.Background = new SolidColorBrush(normal.Card);

        var ink = new SolidColorBrush(normal.Ink);
        this.FindControl<TextBlock>("AlarmBannerTime")!.Foreground = ink;
        this.FindControl<TextBlock>("AlarmBannerText")!.Foreground = ink;
        this.FindControl<TextBlock>("AlarmText")!.Foreground = ink;

        var dominoes = this.FindControl<DominoRow>("Dominoes")!;
        dominoes.Palette = normal;   // ⚠️ 骨牌不跟着翻：它压根不在钟面上
        dominoes.Fallen = DominoRow.FallenForToday(DateTime.Now);

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
        // ⚠️ 图标用**不翻**的那一档：它们坐在钟面外面，跟着闪会变成另一种干扰
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var palette = dark ? DialPalette.Dark : DialPalette.Light;

        var pin = this.FindControl<Button>("PinBtn")!;
        pin.Content = ChromeIcons.Pin(_settings.Pinned, palette);
        pin.Classes.Set("on", _settings.Pinned);

        var tick = this.FindControl<Button>("TickBtn")!;
        tick.Content = ChromeIcons.Speaker(_settings.TickEnabled, palette);
        tick.Classes.Set("on", _settings.TickEnabled);

        if (_tickItem is not null) _tickItem.IsChecked = _settings.ForceTicking;
        if (_commandItem is not null) _commandItem.IsChecked = _commandArmed;

        this.FindControl<Button>("ThemeBtn")!.Content = ChromeIcons.Theme(dark, palette);
        this.FindControl<Button>("SettingsBtn")!.Content = ChromeIcons.Gear(palette);

        if (_pinItem is not null) _pinItem.IsChecked = _settings.Pinned;
    }

    /// <summary>当前选中的目标；一个都没选（rules.json 是空的）就是 null。</summary>
    private string? Picked()
        => _goalBoxes.FirstOrDefault(b => b.IsChecked == true)?.Content as string;

    /// <summary>置顶的**唯一入口**——图标、菜单项、设置、窗口属性一次全对齐。</summary>
    private void SetPinned(bool pinned)
    {
        _settings.Pinned = pinned;
        Topmost = pinned;
        ApplyChrome();
    }

    /// <summary>
    /// 滴答的**唯一入口**——喇叭图标、菜单项、设置一次全对齐。
    ///
    /// ⚠️ **`Tick.Stop()` 只能挂在这里，绝不能放进 <see cref="ApplyChrome"/>**（v3 的 E13）：
    /// Windows 上它是 `PlaySound(null)`，停的是**本进程在 winmm 单通道上正在放的任何东西**
    /// ——包括正在响的闹钟。而 `ApplyChrome` 有好几个调用点（换主题、点图钉），
    /// 放进去就等于「点一下图钉把正在响的闹钟掐了」。
    /// </summary>
    internal void SetTicking(bool on)
    {
        _settings.TickEnabled = on;
        if (!on) Tick.Stop();
        ApplyChrome();
    }

    /// <summary>无条件滴答的唯一入口——菜单那一项和设置窗口那张卡走的是同一条路。</summary>
    internal void SetForceTicking(bool on)
    {
        _settings.ForceTicking = on;
        if (!on) Tick.Stop();
        ApplyChrome();
    }

    /// <summary>到点跑命令的唯一入口。⚠️ 仍然**不持久化**（E8），设置窗口也改不了这一点。</summary>
    internal void SetCommandArmed(bool on)
    {
        _commandArmed = on;
        ApplyChrome();
    }

    internal bool CommandArmed => _commandArmed;

    /// <summary>到点会跑的那一条；没配就是 null。设置窗口要显示它（v3 的 E14）。</summary>
    internal string? CommandForThisOs => _rules.CommandNamed(_settings.AlarmCommand);

    /// <summary>
    /// 把内存里的设置写回库。**给 <see cref="SqlWindow"/> 在跑外来 SQL 之前调。**
    ///
    /// ⚠️ 设置在内存里有一份、退出时整份写回，所以 SQL 改了 `setting` 的某一行，
    /// **退出时会被内存里那份盖掉**。先 flush 让库成为唯一真相，冲突就不存在了。
    /// </summary>
    internal void FlushSettings() => _settings.Save();

    /// <summary>
    /// 外来 SQL 跑完之后当场重装。**不关程序**——关窗口会把正在跑的那一轮作废掉
    /// （`OnClosing` → `Settle`），代价和「改个提醒文字」完全不成比例。
    /// </summary>
    internal void ReloadAfterSql()
    {
        _settings = Settings.Load(_store);
        LoadConfig();
        BuildGoals();
        ApplyChrome();
        RefreshGoalTotals();
    }

    /// <summary>给设置窗口的那个红按钮用。</summary>
    internal SampleStore? Store => _store;

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
        // ⚠️ 高度不设：SizeToContent="Height" 自己长，rules.json 有几个目标就有几行
        this.FindControl<DialControl>("Dial")!.Height = metrics.DialHeight;
        this.FindControl<DominoRow>("Dominoes")!.Height = metrics.DominoHeight;
        // ⚠️ 骨牌行的边距**分档**，不在 XAML 里写死（见 LayoutMetrics.DominoMargin）
        this.FindControl<Grid>("DominoRowCell")!.Margin = metrics.DominoMargin;

        var mask = new SolidColorBrush(Color.FromArgb((byte)Math.Round(WindowLayout.Opacity * 255), 255, 255, 255));
        this.FindControl<DialControl>("Dial")!.OpacityMask = mask;
        this.FindControl<DominoRow>("Dominoes")!.OpacityMask = mask;
        this.FindControl<Border>("CardBackdrop")!.OpacityMask = mask;

        this.FindControl<TextBlock>("VersionLabel")!.Text =
            typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";

        // ⚠️ 提示条的上限**是算出来的硬上限，不是保守取值**：它跟骨牌叠在同一个 Auto 高
        //    的格子里，撑高了会把下面的卡片顶下去，整扇窗为了一条提示条跳一分钟
        foreach (var name in new[] { "AlarmBannerText", "AlarmBannerTextBlue" })
        {
            var text = this.FindControl<TextBlock>(name)!;
            text.MaxLines = metrics.BannerMaxLines;
            text.MaxWidth = metrics.BannerMaxWidth;
        }

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

            // 先问小红圈：这一下精确落在它上面就当「瞄一眼下一条」处理，不再往下走拖窗口。
            // ⚠️ **纯读**：不出声、不推进水位线——跟到点真触发那条路完全隔离
            if (dial.HitTestAlarmsDot(e.GetPosition(dial)))
            {
                PeekNextAlarm();
                e.Handled = true;
                return;
            }

            BeginMoveDrag(e);   // ⚠️ 只能在**按下那一刻**调，等「松开算不算点击」判完就来不及了
        };

        // 没有标题栏就没有系统菜单，这是唯一能关窗口的地方。只有两项，不做成一整套窗口菜单。
        // 走 Close() 而不是直接退进程——跟点 × 完全同一条路径（会走 OnExit 落盘）。
        // ⚠️ **这一项不挂图标**（2026-09-16 用户指出）：图标和勾选标记共用菜单左边那一列，
        //    而图标比勾号宽，整列被它撑开——另外三项的勾号前面于是多出一截空白。
        //    四项都不带图标，列宽就只由勾号决定，对齐了。
        var close = new MenuItem { Header = "Close window" };
        close.Click += (_, _) => Close();

        _pinItem = new MenuItem { Header = "Keep on top", ToggleType = MenuItemToggleType.CheckBox };
        _pinItem.Click += (_, _) => SetPinned(!_settings.Pinned);

        // ⚠️ 菜单里这一项是 **Force**（无条件响），跟喇叭图标不是同一个开关：
        //    喇叭挂**跑偏才响**。两个开关合起来才是完整语义
        _tickItem = new MenuItem { Header = "Force ticking", ToggleType = MenuItemToggleType.CheckBox };
        _tickItem.Click += (_, _) => SetForceTicking(!_settings.ForceTicking);

        // ⚠️ 到点跑命令。**每次启动都是关的**，见 _commandArmed
        _commandItem = new MenuItem { Header = "Run command at alarm", ToggleType = MenuItemToggleType.CheckBox };
        // ⚠️ 界面上**不显示具体命令**（用户 2026-09-16 要求），改成打开那一刻写日志——
        //    那条命令多半是关机，事后总得查得出「这一下到底会跑什么」
        _commandItem.Click += (_, _) => SetCommandArmed(!_commandArmed);

        dial.ContextMenu = new ContextMenu
        {
            ItemsSource = new[] { _tickItem, _commandItem, _pinItem, close },
        };

        this.FindControl<Button>("TickBtn")!.Click += (_, _) => SetTicking(!_settings.TickEnabled);

        // ⚠️ 齿轮**不挂 Tooltip**（v3 的 E10）：点开的是模态窗，模态一起来按钮就再也收不到
        //    PointerExited——气泡会卡在对话框上面，关掉对话框还赖着不走。齿轮本来也不用解释
        this.FindControl<Button>("SettingsBtn")!.Click += async (_, _) =>
        {
            await new SettingsWindow(_settings, this).ShowDialog(this);
            ApplyChrome();
        };

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
                return;
            }

            Position = new PixelPoint(x, y);
            WindowStartupLocation = WindowStartupLocation.Manual;

            // ⚠️ 真正的夹回要等 `Opened`：原生窗口没建出来之前 `FrameSize` 和
            //    `Screens.ScreenFromWindow` 都不可靠，而这两样正是 ClampIntoScreen 要用的。
            //    **夹回只有一份实现**——上面那个 `Contains` 只负责「这块屏还在不在」。
            Opened += (_, _) => ClampIntoScreen();
        }
        catch (Exception e)
        {
            Events.Error("window", "Failed to restore the window position", e);
        }
    }

    /// <summary>
    /// 松手之后把窗口整个拉回屏幕可用区域内（v3 的用户 2026-08-08 提的：向上拖出屏幕
    /// 会被系统弹回来，希望左/右/下也一样）。macOS 和 Windows 都只替我们管了上边缘，
    /// 另外三边得自己来——**无边框窗口连标题栏都没有，推出去就再也够不着了**。
    ///
    /// 拉回的目标是「窗口目前主要待在哪块屏」的**工作区**（<c>ScreenFromWindow</c>，
    /// 工作区 = 扣掉菜单栏 / 任务栏 / Dock 之后的部分），**不是所有屏幕拼起来的大矩形**：
    /// 多屏排布可能不是一个完整矩形，拿外接矩形去判断会把两块屏之间的空洞也算成合法位置。
    ///
    /// ⚠️ **为什么必须等拖动停下来再拉，不能一边拖一边拉**：一边拖一边拉的话，窗口
    /// 永远被摁在当前这块屏的边界内，就永远到不了「一半以上落在另一块屏上」那个状态，
    /// 而 <c>ScreenFromWindow</c> 正是按这个判断该归哪块屏的——结果是**窗口再也拖不到
    /// 第二块显示器上去**。v3 的用户是双屏，这条不是理论风险。所以拖动过程中随便它跨屏、
    /// 出界，松手之后（<see cref="_settle"/>）再归位。
    ///
    /// ⚠️ 坐标单位要小心：<c>Position</c> 和 <c>Screen.WorkingArea</c> 是**物理像素**，
    /// 而 <c>FrameSize</c> / <c>ClientSize</c> 是**与 DPI 无关的逻辑单位**，两者差一个
    /// <c>Screen.Scaling</c>。不换算的话，在缩放不是 100% 的屏幕上会算错窗口多大。
    /// </summary>
    private void ClampIntoScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(FrameSize ?? ClientSize, screen.Scaling);

        // 窗口比工作区还大时，Math.Max 保证下界不会反超上界（Math.Clamp 那样会直接抛）——
        // 这种情况下贴着左上角，宁可右边 / 下边露出去，也不要把左上角推出屏幕。
        var x = Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - size.Width));
        var y = Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        if (x == Position.X && y == Position.Y) return;

        _clamping = true;
        try
        {
            Position = new PixelPoint(x, y);
            _lastPosition = Position;   // 存进 settings.json 的要是夹回之后的位置
        }
        finally { _clamping = false; }
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

                // ⚠️ 只在**专注阶段**闪：休息期间跑偏不算跑偏
                _drifting = round.Phase == RoundPhase.Focusing
                         && j?.Outcome == SecondOutcome.OffTask;

                // ⚠️ **这里以前每秒写一行日志，2026-09-16 删掉了。**
                //    那一行记的是 at / app / title / idle ——**四样全是 samples.db 的列**，
                //    外加 outcome / focused / slack ——这三样是它们的纯函数，而且重放是
                //    幂等的（`RebuildTests` 守着）。也就是说整行都是数据库的副本，
                //    50 分钟一轮 3000 行、几百 KB，还把真正要看的那几行冲得找不着。
                //    **观测归数据库，日志只留判断和失败。**
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

        // ⚠️ **不在专注阶段就一定不跑偏**，必须在这里显式清掉：`_drifting` 只在
        //    「专注阶段记下一秒」那条路上更新，休息阶段那条路不碰它——留着上一个值
        //    就会**整个休息期间一直闪**。没有正在跑的一轮同理。
        if (_round is not { Phase: RoundPhase.Focusing }) _drifting = false;

        // 阶段刚变过去的那一拍响一声。⚠️ 抓的是**变化**不是状态，所以要记上一拍
        if (_round is { } r0 && _lastPhase != r0.Phase)
        {
            if (_lastPhase == RoundPhase.Focusing && r0.Phase == RoundPhase.Resting
                && _settings.FocusDoneEnabled)
                Sound.Repeat(_settings.FocusDoneSound, NotifyRings);

            if (_lastPhase == RoundPhase.Resting && r0.Ending == EndReason.Completed
                && _settings.RestDoneEnabled)
                Sound.Repeat(_settings.RestDoneSound, NotifyRings);

            _lastPhase = r0.Phase;
        }

        // ⚠️ 整分钟那一串排在闹钟**之前**（v3 的 J10）：两边都要出声时，
        //    Windows 的 winmm 是单通道、后响的会掐断先响的，而闹钟响完什么都不留、
        //    清单响完还留着一分钟的提示条 —— 所以让闹钟赢。
        if (MinuteOf(s.At) != _lastMinute)
        {
            _lastMinute = MinuteOf(s.At);
            OnMinute(s.At.LocalDateTime);
        }

        var second = s.At.ToUnixTimeSeconds();
        if (second != _lastTickSecond)
        {
            _lastTickSecond = second;

            // ⚠️ **两个开关，不是一个**：喇叭图标挂**跑偏**，菜单/设置里那个 Force
            //    无条件响。合起来就是 v3 那一句。
            if (_settings.ForceTicking || (_settings.TickEnabled && _drifting))
                Tick.Play(s.At.Second, _settings.TickVolume);

            // 跑偏就让钟面**一秒一翻**。⚠️ 是「翻」不是「设成反色」：
            // 稳定反着的钟面看两分钟就变成壁纸了，抓不住眼睛；**闪**才抓得住
            // （v3 的用户 2026-08-29 撤掉「5 秒一次」那版的全部理由）。
            // 一秒一翻 = 完整周期 2 秒 = 0.5 Hz，远在光敏性癫痫的 3 Hz 风险区间之外。
            // 不跑偏就立刻回到用户设的那一档，不留半个相位。
            _inverted = _drifting && !_inverted;
            ApplyPalette();

            // ⚠️ 这两样都按**秒**收，不能挂到分钟节拍上——挂上去会一直糊到下一个整分钟。
            //    v3 的提示条就是按分钟收的，它自己的注释里记着「点红圈瞄一眼那 3 秒
            //    其实也有这个毛病，**还没修**」——这里一并修掉。
            var nowLocal = DateTime.Now;
            var scrub = this.FindControl<TextBlock>("AlarmText")!;
            if (scrub.IsVisible && nowLocal >= _alarmQuietUntil) scrub.IsVisible = false;
            if (_bannerUntil is { } until && nowLocal >= until) ShowBanner(null);
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
        // 飘了就捞一下。⚠️ 只在**专注阶段**、而且只在「还没到离开门槛」那一段——
        //    过了门槛就是真离开了，那一段既不计入也不算跑偏，催也没用
        if (_round is { Phase: RoundPhase.Focusing } && _settings.IdleEnabled
            && _last.Idle >= IdleNudgeSeconds && _last.Idle < AwayMap.ThresholdSeconds)
            Sound.Repeat(_settings.IdleSound, NotifyRings);

        ReloadConfigIfChanged();
        CheckAlarmsList(now);
        RefreshAlarmsDot(now);

        // 骨牌每分钟核对一次星期。挂在现成的分钟节拍上，不专门开定时器——
        // 判断本身是一次 DayOfWeek 比较，零成本
        this.FindControl<DominoRow>("Dominoes")!.Fallen = DominoRow.FallenForToday(now);
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
            Events.Info("cron", $"{e.At:HH:mm} [{e.Expression}] {e.Text}");

        // 条数超出的部分缀在**时间行**末尾（`23:55  +2`），不占新的一行——多一行会把
        // 下面的卡片顶下去
        var head = due[0];
        var extra = due.Count > 1 ? $"  +{due.Count - 1}" : "";
        ShowBanner($"{head.At:HH:mm}{extra}", head.Text,
                   new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1));

        // 系统通知：**无条件，而且一条事件一个、永远不合并**（v3 的用户 2026-09-03 点名）。
        // ⚠️ 它跟上面那条提示条**并存，不是二选一**：提示条保证屏幕上一定看得见，
        //    但它有硬高度上限、多出来的只剩一个 `+N`；**通知中心这一份才是不丢内容的**，
        //    而且关掉程序也还能翻回来。
        foreach (var e in due)
            if (e.Text.Length > 0)
                Platform.Notify.Show(e.Text);

        // ⚠️ **带命令的条目在这里跑**，而且 `AlarmsList.Due` 已经保证它们不会被补放
        //    （错过的那一分钟不会事后执行）。命令只按名字取，原文只住在 command 表里。
        foreach (var e in due)
            if (e.Run is { } name)
                Platform.Command.LaunchDetached(_rules, name);

        // ⚠️ 这个开关**只管响不响铃**（v3 的 J6）：上面那条「检查清单 → 挑出到点的 →
        //    提示条 + 系统通知 + 日志」的主链路**无条件每分钟都走**，不受它控制。
        //    关掉它只是消音，不是让提醒消失
        if (_settings.AlarmsEnabled) Sound.Repeat(_settings.AlarmsSound, AlarmsListRings);
    }

    /// <summary>
    /// 点一下小红圈：把 12 小时内下一条**瞄一眼**，只留 3 秒。
    ///
    /// ⚠️ 停留时长跟到点真触发（一分钟）**分开**：那是提醒，这只是查看。
    /// </summary>
    private void PeekNextAlarm()
    {
        var next = AlarmsList.NextDue(_alarms, DateTime.Now);
        if (next.Count == 0) return;

        var extra = next.Count > 1 ? $"  +{next.Count - 1}" : "";
        ShowBanner($"{next[0].At:HH:mm}{extra}", next[0].Text, DateTime.Now.AddSeconds(3));
    }

    private void RefreshAlarmsDot(DateTime now)
    {
        var nextDue = AlarmsList.NextDue(_alarms, now);
        var next = nextDue.Count > 0 ? nextDue[0] : (AlarmEntry?)null;

        var dial = this.FindControl<DialControl>("Dial")!;
        dial.AlarmsDotMinutes = AlarmsList.DotPosition(next, now);
        dial.AlarmsDotMultiple = nextDue.Count > 1;
    }

    /// <summary>
    /// 提示条。<paramref name="time"/> 为 null 就是收起。
    ///
    /// ⚠️ **两层实色文字一起写**（墨色一份 + 亮蓝一份偏移叠在上面），跟画指针阴影同一个
    /// 手法：窗口是透明的，提示条背后可能是任何壁纸，单色文字会糊进去。漏写任何一层
    /// 都会缺一半，而且**不报错**。
    /// </summary>
    private void ShowBanner(string? time, string? body = null, DateTime? until = null)
    {
        _bannerUntil = time is null ? null : until;
        this.FindControl<Grid>("AlarmBanner")!.IsVisible = time is not null;
        if (time is null) return;

        foreach (var name in new[] { "AlarmBannerTime", "AlarmBannerTimeBlue" })
            this.FindControl<TextBlock>(name)!.Text = time;
        foreach (var name in new[] { "AlarmBannerText", "AlarmBannerTextBlue" })
            this.FindControl<TextBlock>(name)!.Text = body ?? "";
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
            //
            // ⚠️ ① 一个字都不记：它**每一轮都会发生**，是设计本身，记了就是噪音。
            //    只有 ② 才值得留痕——那说明真有秒丢了。
            if (compare
                && rebuilt.FocusedSeconds != live.FocusedSeconds
                && away.Spans.Count == _lastAwaySpans)
                Events.Warn("db", $"rebuild mismatch: live={live.FocusedSeconds}s db={rebuilt.FocusedSeconds}s "
                                + "— 区间条数没变却对不上，说明真的有秒没写进库");
            _lastAwaySpans = away.Spans.Count;


            _round = rebuilt;
        }
        catch (Exception e)
        {
            // 重建失败就继续用实时那个环——读不了库不该把正在跑的一轮毁掉
            Events.Error("db", "Failed to rebuild the round from samples.db", e);
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
            Events.Error("db", $"Cannot resume the round started at {rec.StartedAt:HH:mm}", e);
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
            Settle(reason);
        }
        else
        {
        }
    }

    private void OpenStore()
    {
        try
        {
            _store = SampleStore.Open(AppData.DbPath());
            _recorder = new Recorder(_store, () => (_last.App, _last.Title), () => _last.Idle);

            // 库开起来了，从这一刻起要记的事都进 `event` 表，不再落文本（见 Events）
            Events.Bind(_store);

            // ⚠️ **每次启动记一行，这是唯一破例记「正常事件」的地方**：
            //    sample 只在专注阶段才写，所以采样流里到处是空档。少了这一行，
            //    「那段时间没在跑」和「在跑但没录」**长得一模一样**——而这恰恰是
            //    回头查问题时第一个要分清的。一天几行，不算噪音。
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
            Events.Info("start", $"v{version} pid={Environment.ProcessId}");
        }
        catch (Exception e)
        {
            // 录不上也不能崩：环会退化成「一秒都没采到」，但程序照跑
            // ⚠️ **这一条只能落文本**：库都没打开，事件表压根写不进去——
            //    这正是那份「最后的求救信」存在的全部理由
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

        // 拨到哪儿了，**只在拨的时候显示**。收起的时刻**复用 `_alarmQuietUntil`**——
        // 「停手两秒后收起」跟「调整期结束」天然是同一个时刻，不需要第二个计时量
        var scrub = this.FindControl<TextBlock>("AlarmText")!;
        scrub.Text = _alarm.FireAt is { } at ? at.ToString("HH:mm") : "";
        scrub.IsVisible = true;

        // ⚠️ 拨针要留痕：它的后果（响铃）可能几小时后才发作，到时候「这闹钟哪来的」
        //    完全无从查起。2026-09-16 实测就撞上一次——日志里只有 `alarm fired`，
        //    查不出是谁把它从 15:32 拨到 10:22 的
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

        _alarm.MarkFired();   // 先消费掉再动手：失败也不该让它反复触发

        // ⚠️ **二选一**：开着就跑命令、不响铃（跟 v3 一致）。
        //    响铃是「提醒你自己动手」，跑命令是「替你动手」，两件事不叠加。
        if (_commandArmed)
        {
            Events.Info("alarm", $"{_alarm.FireAt:HH:mm} fired → running the command");
            Command.LaunchDetached(_rules, _settings.AlarmCommand);
            return;
        }

        Events.Info("alarm", $"{_alarm.FireAt:HH:mm} fired, sound={_settings.AlarmSound ?? "(none)"} ×{AlarmRings}");
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
                    // ⚠️ **这里不再自己写 stop 事件**：那条记录归 OnExit 统一写，
                    //    否则「有没有留下痕迹」就取决于走的是哪条退出路（见 OnExit 的注释）
                    try { Dispatcher.UIThread.Invoke(() => OnExit($"{ctx.Signal} received")); }
                    catch (Exception e) { Events.Error("stop", "Failed to settle on signal", e); }
                    // Cancel = false：照常让进程退出，我们只是抢在它之前把账写完
                }));
            }
            catch (Exception e)
            {
                Events.Error("stop", $"Cannot hook {signal}", e);
            }
        }
    }

    /// <summary>
    /// 受控退出。⚠️ 两件事，**都必须做**：本轮落盘（C5）和闹钟时刻落盘（E7）。
    /// 后者跟有没有正在跑的一轮无关——所以它不能藏在 <see cref="Settle"/> 里。
    ///
    /// **必须幂等**：`Closing` / `ShutdownRequested` / 信号三条路都会调它。
    ///
    /// ⚠️ 2026-09-16 这里出过一次事故，形状值得记住：原来的理由是
    /// 「<see cref="Settle"/> 有 `_written` 挡着，**设置重写一遍也无害**」——
    /// 那句话在设置还是一个 JSON 文件时是对的。设置搬进数据库之后（I12），
    /// 第二遍会撞上**已经 Dispose 的连接**，`BeginTransaction` 当场抛。
    /// 而那时 `Events` 已经解绑，异常只落进那份本该是空的文本文件里。
    ///
    /// **改动作废了一条前提，而那条前提只写在注释里。** 所以现在靠一个显式的标志，
    /// 不再靠「里面每一步碰巧都能重跑」。
    /// </summary>
    private void OnExit(string why)
    {
        if (_exited) return;
        _exited = true;

        // ⚠️ **stop 事件必须写在这里，不能写在某一条退出路上。**
        //    2026-09-16 实测发现：它原来只写在 POSIX 信号处理器里，于是
        //    `pkill` / 关机能留下记录，而**点 × / Cmd+Q 什么都不留**。
        //    症状是 `event` 表里历史上每一条 stop 都是 `SIGTERM received`，
        //    一条手动关闭都没有——而那恰恰是用户最常用的关法。
        //    查「程序上次是怎么没的」时，缺记录和「被 kill -9 了」长得一模一样。
        //    对照 AW 的窗口时间线才看出来：18:43:58 焦点离开 ItamiBen 之后它
        //    再没出现过（= 手动关掉了），而库里那一刻是空白。
        Events.Info("stop", $"{why} — settling before exit");

        Settle(EndReason.Closed);
        _settings.FocusMinutes = _focusMinutes;
        _settings.SelectedGoal = Picked();
        _settings.AlarmAt = _alarm.FireAt;
        if (_lastPosition is { } at)
        {
            _settings.WindowX = at.X;
            _settings.WindowY = at.Y;
        }
        _settings.Save();

        // ⚠️ **先解绑再关库**：解绑之后再出的事会落回文本，而不是往一个已经关掉的
        //    连接上写——那会抛，而抛在退出路径上最难查
        Events.Unbind();
        _store?.Dispose();
    }

    private void OnAction()
    {
        if (_round is { Ending: null })
        {
            _ = GiveUpAsync();
            return;
        }

        if (Picked() is not { } goal) return;

        _round = new Round(DateTimeOffset.Now, _focusMinutes, [goal], _rules);
        _written = false;
        _lastRebuiltMinute = -1;
        _lastAwaySpans = 0;
        _lastPhase = RoundPhase.Focusing;
        _store?.BeginRound(_round.StartedAt, _round.FocusMinutes, _round.Goals);
        UpdateUi();
    }

    /// <summary>
    /// Give up 要问一句——它**作废整轮**。
    ///
    /// ⚠️ 问不等于拦（DECISIONS C8）：用户执意放弃是他的选择，这里只是确认他知道
    /// 按下去会发生什么。**别把它变成劝阻。**
    /// </summary>
    private async Task GiveUpAsync()
    {
        if (_asking) return;
        _asking = true;
        try
        {
            if (!await Confirm.AskAsync(this, "The task isn't finished. Give up?")) return;
            Settle(EndReason.GaveUp);
            UpdateUi();
        }
        finally { _asking = false; }
    }

    /// <summary>
    /// 专注途中关窗口 = 作废整轮，所以也要问一句。
    ///
    /// ⚠️ **必须先 `e.Cancel = true` 再去 await**：关闭事件不等异步，不拦下来窗口
    /// 当场就没了，问了也白问。得到许可之后置标志再调一次 <see cref="Window.Close"/>。
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || _round is not { Ending: null } || _asking)
        {
            OnExit("window closed");
            return;
        }

        e.Cancel = true;
        _asking = true;
        try
        {
            if (!await Confirm.AskAsync(this, "The task isn't finished. Quit anyway?")) return;
            _closeApproved = true;
            Close();
        }
        finally { _asking = false; }
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

        // ⚠️ **先落库再更新内存**：反过来的话，落库那一步炸了，界面上的数字已经涨了，
        //    而账本里没有——下次启动数字自己缩回去，看着像程序把时间吃了
        Totals.Add(_store, _round.FocusedSecondsByGoal);
        _totals.Add(_round.FocusedSecondsByGoal);
        _written = true;

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

        RefreshGoalTotals();

        var action = this.FindControl<Button>("ActionBtn")!;
        action.Content = running ? "Give up" : "Start";
        action.IsEnabled = running || Picked() is not null;

        // ⚠️ 只有 Give up 是红的——它作废整轮。休息中**仍然是 Give up**（C8）
        action.Classes.Set("danger", running);

        // 提交之后目标和时长锁死——**一轮开始就不能再改**，规则是你事先写的
        foreach (var b in _goalBoxes) b.IsEnabled = !running;
        this.FindControl<Slider>("Minutes")!.IsEnabled = !running;

        UpdateStatus(sample);
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
        catch (Exception e) { Events.Error("permission", "TitlePermissionGranted failed", e); return; }

        this.FindControl<Button>("GrantBtn")!.IsVisible = !granted;
        this.FindControl<Border>("StatusBar")!.Background = new SolidColorBrush(
            granted ? Color.FromArgb(0x18, 0x80, 0x80, 0x80) : Color.FromRgb(0xC4, 0x5A, 0x28));

        // ⚠️ 库开不起来 ⇒ 一秒都录不进去 ⇒ **每一轮都会静默触底**。
        //    这是最坏的一种失败：程序照跑、钟面照转，只是永远不可能达成。必须说出来
        if (_store is null)
        {
            ShowStatus("Cannot open samples.db",
                       "Nothing is being recorded, so every round will run out. See itamiben.log.");
            this.FindControl<Border>("StatusBar")!.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x5A, 0x28));
            this.FindControl<Button>("GrantBtn")!.IsVisible = false;
            return;
        }

        if (!granted)
        {
            // ⚠️ 症状是「app 名读得到、标题读不到」。第一次见很容易误判成目标 app 的问题
            ShowStatus("No Accessibility permission",
                       "Window titles are unreadable, so title rules never match. "
                     + "System Settings → Privacy & Security → Accessibility");
            return;
        }

        if (sample is { } s)
            ShowStatus(s.App.Length == 0 ? "—" : s.App,
                       s.Title.Length == 0 ? "(no title)" : s.Title);
    }

    /// <summary>
    /// 状态栏两行：**应用名居中，标题左对齐**。
    ///
    /// ⚠️ 两行都是单行 + 省略号截断，**绝不折行**——窗口是 `SizeToContent="Height"`，
    /// 一折行整扇窗就变高、边框跟着跳。
    /// </summary>
    private void ShowStatus(string app, string title)
    {
        this.FindControl<TextBlock>("StatusApp")!.Text = app;
        this.FindControl<TextBlock>("StatusTitle")!.Text = title;
    }

    /// <summary>向上取整到分钟：读数因此每分钟才跳一次，跟格子封盘同步。</summary>
    private static string Mins(int seconds) => $"{(int)Math.Ceiling(Math.Max(seconds, 0) / 60.0)} min";

    private static string Hm(long seconds) => $"{seconds / 3600}h {seconds % 3600 / 60}m";
}
