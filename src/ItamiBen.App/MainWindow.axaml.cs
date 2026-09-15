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

    private readonly Sampler _sampler = new();
    private readonly List<CheckBox> _goalBoxes = [];
    private readonly List<RadioButton> _tierButtons = [];

    private GoalRules _rules = GoalRules.Empty;
    private string? _rulesError;
    private GoalTotals _totals = new();
    private Round? _round;

    /// <summary>本轮是否已落盘。**落盘是一个动作，不是一条政策**（DECISIONS C5）。</summary>
    private bool _written;

    private int _focusMinutes = 25;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        Log.Start();
        LoadRules();
        _totals = Totals.Load();

        BuildGoals();
        BuildTiers();

        this.FindControl<Button>("ActionBtn")!.Click += (_, _) => OnAction();
        this.FindControl<Button>("GrantBtn")!.Click += (_, _) =>
        {
            // ⚠️ 平台层整层包在 try 里：2026-09-15 这个按钮把整个 app 搞崩过一次
            // （SIGSEGV in CFGetTypeID）。根因已修，但**读不到权限不该把程序带走**。
            try { ForegroundWindow.RequestTitlePermission(); }
            catch (Exception e) { Log.Error("RequestTitlePermission failed", e); }
        };

        ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();

        _sampler.Ticked += OnTick;
        _sampler.Start();

        // ⚠️ 关窗和 Cmd+Q 是两条不同的路，但**执行的是同一个写入动作**（C5）
        Closing += (_, _) => Settle(EndReason.Closed);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownRequested += (_, _) => Settle(EndReason.Closed);

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
        // ⚠️ 休息阶段也要喂——阶段靠 Observe 推进（Round 的文档里写着）
        if (_round is { Ending: null })
        {
            var j = _round.Observe(s.At, s.App, s.Title);

            // 只在真正记了一秒的时候写日志：一秒十拍，全写等于每秒十行
            if (j is { } judged)
                Log.Line($"{judged.Outcome,-10} focused={_round.FocusedSeconds,-5} slack={_round.SlackSeconds,-5} "
                       + $"app={s.App,-18} title={s.Title}");

            if (_round.Ending is not null) Settle(_round.Ending.Value);
        }

        UpdateUi(s);
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

        dial.Cells = _round?.Cells ?? [];
        dial.StartedAt = _round?.StartedAt;
        dial.Projection = _round?.Project();
        dial.InvalidateVisual();

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

    /// <summary>向上取整到分钟：读数因此每分钟才跳一次，跟格子封盘同步。</summary>
    private static string Mins(int seconds) => $"{(int)Math.Ceiling(Math.Max(seconds, 0) / 60.0)} min";

    private static string Hm(long seconds) => $"{seconds / 3600}h {seconds % 3600 / 60}m";
}
