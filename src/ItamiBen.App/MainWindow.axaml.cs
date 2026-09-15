using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
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
    /// 界面上给的三个档位。
    ///
    /// ⚠️ **别往上加档**（DECISIONS C6）：难度曲线是从达成截止线自然长出来的，
    /// 50 分钟就已经要求效率 ≥45.5%；再往上会出现理论上不可能完成的设定。
    /// 结构上限是 <see cref="Round.MaxFocusMinutes"/>，那是边界不是推荐值。
    /// </summary>
    private static readonly int[] Tiers = [10, 25, 50];

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
    private readonly List<RadioButton> _tierButtons = [];

    private GoalRules _rules = GoalRules.Empty;
    private string? _rulesError;
    private GoalTotals _totals = new();
    private Round? _round;

    /// <summary>观测库和录制器。开不起来就一个都没有——录不上不该把程序搞崩。</summary>
    private SampleStore? _store;
    private Recorder? _recorder;

    /// <summary>最近一拍的采样，录制器从这里取前台窗口（Sampler 已经读过了，不重复读）。</summary>
    private Sample _last;

    /// <summary>上一次从库里整个重建是在哪一分钟。-1 = 还没重建过。</summary>
    private int _lastRebuiltMinute = -1;

    /// <summary>上一次跑整分钟那一串事情是在哪一分钟。-1 = 还没跑过（启动后第一拍就跑）。</summary>
    private int _lastMinute = -1;

    /// <summary>alarms.cron，每分钟重读一次——用户手写的文件，改完不该还要重启。</summary>
    private IReadOnlyList<CronEntry> _alarms = [];

    /// <summary>
    /// alarms.cron 的去重水位线。**纯内存、不持久化，初始化成启动那一刻**（v3 的 J7）：
    /// 程序关闭期间错过的条目重开后直接跳过，不倒回去补。
    /// </summary>
    private DateTime _alarmsProcessedThrough = DateTime.Now;

    /// <summary>提示条显示到哪一刻。null = 没在显示。⚠️ 用截止时刻不用布尔量，同 E6。</summary>
    private DateTime? _bannerUntil;

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
        BuildTiers();
        ResumeRound();

        this.FindControl<Button>("ActionBtn")!.Click += (_, _) => OnAction();
        this.FindControl<Button>("GrantBtn")!.Click += (_, _) =>
        {
            // ⚠️ 平台层整层包在 try 里：2026-09-15 这个按钮把整个 app 搞崩过一次
            // （SIGSEGV in CFGetTypeID）。根因已修，但**读不到权限不该把程序带走**。
            try { ForegroundWindow.RequestTitlePermission(); }
            catch (Exception e) { Log.Error("RequestTitlePermission failed", e); }
        };

        // 读回时刻只为了显示黄针残影，**不激活**——关着程序时错过的闹钟不补响（v3 的 E7）
        _alarm.Restore(_settings.AlarmAt);
        Log.Line($"alarm restored: at={_settings.AlarmAt:yyyy-MM-dd HH:mm} sound={_settings.AlarmSound ?? "(none)"}");

        // ⚠️ 拨针挂在**钟面本身**上，没有独立按钮（v3 的 E4：Button 内部会把
        //    PointerPressed 标 Handled，挂在钟面上的普通订阅收不到）。
        this.FindControl<DialControl>("Dial")!.PointerWheelChanged += OnAlarmWheel;

        ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();

        _sampler.Ticked += OnTick;
        _sampler.Start();

        // ⚠️ 关窗和 Cmd+Q 是两条不同的路，但**执行的是同一个写入动作**（C5）
        Closing += (_, _) => OnExit();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownRequested += (_, _) => OnExit();

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

    private void BuildGoals()
    {
        var panel = this.FindControl<WrapPanel>("GoalsPanel")!;
        foreach (var goal in _rules.SelectableGoals)
        {
            var box = new CheckBox { Content = goal, Margin = new Thickness(0, 0, 14, 0) };
            box.IsCheckedChanged += (_, _) => UpdateUi();
            _goalBoxes.Add(box);
            panel.Children.Add(box);
        }
    }

    private void BuildTiers()
    {
        var panel = this.FindControl<StackPanel>("TierPanel")!;
        foreach (var minutes in Tiers)
        {
            var btn = new RadioButton
            {
                Content = $"{minutes} min",
                GroupName = "focus",
                IsChecked = minutes == _focusMinutes,
                Tag = minutes,
            };
            btn.IsCheckedChanged += (_, _) =>
            {
                if (btn.IsChecked == true) _focusMinutes = (int)btn.Tag!;
                UpdateUi();
            };
            _tierButtons.Add(btn);
            panel.Children.Add(btn);
        }
    }

    private void ApplyTheme()
        => this.FindControl<DialControl>("Dial")!.Palette =
            ActualThemeVariant == ThemeVariant.Dark ? DialPalette.Dark : DialPalette.Light;

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

            if (round.Ending is null && s.At.Minute != _lastRebuiltMinute)
            {
                _lastRebuiltMinute = s.At.Minute;
                Rebuild(s.At);
            }

            if (_round?.Ending is { } reason) Settle(reason);
        }

        // ⚠️ 整分钟那一串排在闹钟**之前**（v3 的 J10）：两边都要出声时，
        //    Windows 的 winmm 是单通道、后响的会掐断先响的，而闹钟响完什么都不留、
        //    清单响完还留着一分钟的提示条 —— 所以让闹钟赢。
        if (s.At.Minute != _lastMinute)
        {
            _lastMinute = s.At.Minute;
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
    private void Rebuild(DateTimeOffset now)
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

            if (rebuilt.FocusedSeconds != live.FocusedSeconds)
                Log.Warn($"rebuild mismatch: live={live.FocusedSeconds}s db={rebuilt.FocusedSeconds}s "
                       + "— 说明有秒没写进库");

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
        Rebuild(DateTimeOffset.Now);          // 从 sample 重放 + 补最后一段

        foreach (var b in _goalBoxes) b.IsChecked = rec.Goals.Contains((string)b.Content!);
        _focusMinutes = rec.FocusMinutes;
        foreach (var b in _tierButtons) b.IsChecked = (int)b.Tag! == rec.FocusMinutes;

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
    /// 受控退出。⚠️ 两件事，**都必须做**：本轮落盘（C5）和闹钟时刻落盘（E7）。
    /// 后者跟有没有正在跑的一轮无关——所以它不能藏在 <see cref="Settle"/> 里。
    /// </summary>
    private void OnExit()
    {
        Settle(EndReason.Closed);
        _settings.AlarmAt = _alarm.FireAt;
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
        _store?.EndRound(_round.StartedAt, _round.EndedAt ?? DateTimeOffset.Now, reason.ToString());

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
        dial.Cells = _round?.Cells ?? [];
        dial.StartedAt = _round?.StartedAt;
        dial.Projection = _round?.Project();
        dial.InvalidateVisual();

        this.FindControl<TextBlock>("AlarmText")!.Text = FormatAlarm();
        this.FindControl<TextBlock>("Readout")!.Text = ReadoutText();
        this.FindControl<TextBlock>("TotalsText")!.Text = FormatTotals();

        var action = this.FindControl<Button>("ActionBtn")!;
        action.Content = running ? "Give up" : "Start";
        action.IsEnabled = running || _goalBoxes.Any(b => b.IsChecked == true);

        // 提交之后目标和档位锁死——**一轮开始就不能再改**，规则是你事先写的
        foreach (var b in _goalBoxes) b.IsEnabled = !running;
        foreach (var b in _tierButtons) b.IsEnabled = !running;

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
    /// **显示 = 文件里的总数 + 本轮实时累计**（DESIGN §4.5）。用小时+分钟，
    /// 于是它天然每分钟才动一次——格子每分钟才封盘，秒级跳动是噪声。
    /// </summary>
    private string FormatTotals()
    {
        var names = _totals.Goals.Union(_rules.SelectableGoals, StringComparer.Ordinal).ToList();
        if (names.Count == 0) return "";

        var live = _written ? null : _round?.FocusedSecondsByGoal;
        var parts = names
            .Select(g => (Goal: g, Seconds: _totals[g] + (live?.GetValueOrDefault(g) ?? 0)))
            .Where(x => x.Seconds > 0)
            .Select(x => $"{x.Goal} {Hm(x.Seconds)}");

        var text = string.Join("   ", parts);
        return text.Length == 0 ? "" : text;
    }

    private void UpdateStatus(Sample? sample)
    {
        bool granted;
        try { granted = ForegroundWindow.TitlePermissionGranted; }
        catch (Exception e) { Log.Error("TitlePermissionGranted failed", e); return; }

        this.FindControl<Button>("GrantBtn")!.IsVisible = !granted;
        this.FindControl<Border>("StatusBar")!.Background = new SolidColorBrush(
            granted ? Color.FromArgb(0x18, 0x80, 0x80, 0x80) : Color.FromRgb(0xC4, 0x5A, 0x28));

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
