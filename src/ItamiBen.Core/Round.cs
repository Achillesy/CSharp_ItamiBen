namespace ItamiBen.Core;

/// <summary>一轮走到哪一步了。**永远是推导出来的，没有独立的状态机变量。**</summary>
public enum RoundPhase : byte
{
    /// <summary>专注中。按钮是 Give up。</summary>
    Focusing,

    /// <summary>休息中。⚠️ 按钮**仍然**是 Give up，不变成 Start（DECISIONS C8）。</summary>
    Resting,

    /// <summary>已终结。</summary>
    Ended,
}

/// <summary>
/// 一轮是怎么结束的。
///
/// ⚠️ **只用来显示，绝不用来分叉落盘逻辑**（DECISIONS C5）：屏幕上显示的那个数就是
/// 要写进文件的数，四个出口执行的是**同一个写入动作**。别给 Give up 打折、别问
/// 「强制结束算不算数」——那些秒是用户真干了活，算过的不能再拿走，而那种分支正是
/// bug 的温床。
/// </summary>
public enum EndReason : byte
{
    /// <summary>专注达成，休息也走完了。</summary>
    Completed,

    /// <summary>用户按了 Give up。</summary>
    GaveUp,

    /// <summary>余量耗尽——淡蓝块尾撞到了环终点（DESIGN §5）。</summary>
    RanOut,

    /// <summary>关窗 / Cmd+Q。</summary>
    Closed,
}

/// <summary>
/// **一轮任务：两小时硬环 + 判定 + 累计。** 这个项目的心脏，纯逻辑、零 UI、可单测。
///
/// 120 格 = 120 分钟 = 钟面两圈 × 60，索引就是「从 Start 起第 N 分钟」。
/// **不环绕、不归档、不塌缩**（DECISIONS C4）⇒ **转完两圈就是到点，显示即规则。**
///
/// <b>时间是参数，不是环境。</b> 外面每秒调一次 <see cref="Observe"/>，这个类不认识
/// <c>DateTime.Now</c>；<see cref="CurrentMinute"/> / <see cref="SlackSeconds"/> /
/// <see cref="Project"/> 全都读最后一次 Observe 带进来的那一刻。
///
/// ⚠️ **休息阶段也必须每秒调 Observe**（app / title 会被忽略）——阶段靠它推进。
///
/// <b>每一秒当场定死，永不改写</b>（DESIGN §4.2）：进程内同步读取，没有任何东西会事后
/// 改写，所以 v3 的 <c>AwMirror</c> / <c>MirrorFeed</c> / 预测三规则 / 环形缓冲 /
/// <c>carryForward</c> / 每分钟重读整层都不存在。
/// </summary>
public sealed class Round
{
    /// <summary>
    /// 环的长度。⚠️ **不可配置**（DECISIONS C4）：第一次撞墙的人就会去改它，严酷性
    /// 当场归零；而且它跟钟面几何是同一个数，改了钟面没法画。
    /// </summary>
    public const int RingMinutes = 120;

    public const int RingSeconds = RingMinutes * 60;

    /// <summary>
    /// 专注分钟数的结构上限：<c>focus + ⌈focus/5⌉ ≤ 120</c> 解出来就是 100。
    ///
    /// ⚠️ 这是**「理论上还有可能完成」的边界**，不是推荐值（DECISIONS C6：
    /// 别往上加档，否则会出现理论上不可能完成的设定）。界面给的档位是 10 / 25 / 50，
    /// 选 100 意味着从第 0 秒起效率必须 100%，实际上一开局就触底。
    /// </summary>
    public const int MaxFocusMinutes = 100;

    /// <summary>
    /// 休息分钟数 = <b>⌈focus ÷ 5⌉</b>：10 → 2，25 → 5，50 → 10。
    ///
    /// 上取整而不是 <c>⌊f/5⌋+1</c>——后者藏着「滑块只产出 5 的倍数」这个从没写下来的
    /// 前提，v3 为此栽过（步长一改成 1，8 个值里 6 个算错，而守着它的测试用的
    /// InlineData 全是 5 的倍数，一直是绿的）。上取整对 focus ≥ 1 恒 ≥ 1，不需要那个 +1。
    /// </summary>
    public static int BreakMinutesFor(int focusMinutes) => (focusMinutes + 4) / 5;

    private readonly GoalRules _rules;
    private readonly int[] _focused = new int[RingMinutes];
    private readonly int[] _sampled = new int[RingMinutes];
    private readonly Dictionary<string, int> _byGoal = new(StringComparer.Ordinal);

    /// <summary>已经记过的最后一个秒索引。**只进不退**，这就是「同一秒不会记两遍」的全部机制。</summary>
    private int _lastRecorded = -1;

    /// <summary>
    /// 从 Start 到最后一次 Observe / End 那一刻，**一共过去了多少秒**——
    /// 含正在走的这一秒（槽位 0 本身就是一秒）。只进不退。
    /// </summary>
    private int _consumed;

    /// <summary>达成那一刻的 <see cref="_consumed"/>——余量从此冻结在这里。</summary>
    private int _achievedConsumed;

    /// <param name="now">按 Start 的那一刻传。内部会 <see cref="TimeGrid.FloorToMinute"/>（代价见那里）。</param>
    /// <param name="focusMinutes">承诺的专注分钟数，提交后锁死。</param>
    /// <param name="goals">勾选的目标，**按勾选顺序**——一秒同时命中多个时算给排在前面的那个。</param>
    /// <param name="rules">规则，**开始时锁定**：中途改 rules.json 不影响正在跑的这一轮。</param>
    public Round(DateTimeOffset now, int focusMinutes, IReadOnlyList<string> goals, GoalRules rules)
    {
        if (focusMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(focusMinutes), focusMinutes, "Focus must be at least 1 minute.");
        if (focusMinutes > MaxFocusMinutes)
            throw new ArgumentOutOfRangeException(nameof(focusMinutes), focusMinutes,
                $"Focus of {focusMinutes} minutes plus its break does not fit in the {RingMinutes}-minute ring.");
        if (goals.Count == 0)
            throw new ArgumentException("A round needs at least one goal; with none, no second could ever count.", nameof(goals));
        foreach (var goal in goals)
            if (!rules.IsSelectable(goal))
                throw new ArgumentException($"Goal \"{goal}\" is not a selectable goal in rules.json.", nameof(goals));

        StartedAt = TimeGrid.FloorToMinute(now);
        FocusMinutes = focusMinutes;
        BreakMinutes = BreakMinutesFor(focusMinutes);
        Goals = [.. goals];
        _rules = rules;
    }

    /// <summary>本轮的起点，**已经抹到整分**——格子索引和钟面刻度因此逐格对齐。</summary>
    public DateTimeOffset StartedAt { get; }

    public int FocusMinutes { get; }

    public int BreakMinutes { get; }

    /// <summary>要凑够的专注秒数。</summary>
    public int TargetSeconds => FocusMinutes * 60;

    /// <summary>
    /// 达成截止线 = 第 <c>(120 − 休息分钟)</c> 分钟（DESIGN §4.3）。
    ///
    /// 休息也占环里的格子而且必须塞得下，所以它是**环尾预留的一段**，专注必须在这段
    /// 开始之前达成。⚠️ 这条推论的价值在于：「达成太晚、休息被截断」这个边界情况
    /// **不是被解决了，是被设计掉了**。
    /// </summary>
    public int DeadlineMinute => RingMinutes - BreakMinutes;

    /// <summary>
    /// 整轮能浪费掉的总秒数 = <c>截止线 − 专注目标</c>。
    /// 10 分钟档 6480 秒（效率 ≥ 8.5%），25 分钟档 5400 秒（≥ 21.7%），
    /// 50 分钟档 3600 秒（≥ 45.5%）。
    ///
    /// ⚠️ 难度曲线是从这条规则自然长出来的，不是调出来的。**用户自己选的档位，
    /// 程序不解释、不提醒、不放宽。**
    /// </summary>
    public int BudgetSeconds => DeadlineMinute * 60 - TargetSeconds;

    public IReadOnlyList<string> Goals { get; }

    /// <summary>本轮已攒下的专注秒数，封顶 <see cref="TargetSeconds"/>。</summary>
    public int FocusedSeconds { get; private set; }

    /// <summary>本轮专注秒数按目标拆开——落盘时按这个往 <see cref="GoalTotals"/> 里加。</summary>
    public IReadOnlyDictionary<string, int> FocusedSecondsByGoal => _byGoal;

    /// <summary>达成那一刻；没达成就是 null。休息从这一刻起算。</summary>
    public DateTimeOffset? AchievedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>怎么结束的；还没结束就是 null。**只用来显示**（见 <see cref="EndReason"/>）。</summary>
    public EndReason? Ending { get; private set; }

    public RoundPhase Phase
        => Ending is not null ? RoundPhase.Ended
         : AchievedAt is not null ? RoundPhase.Resting
         : RoundPhase.Focusing;

    /// <summary>
    /// 从 Start 起一共过去了多少秒（截至最后一次 Observe）。**含正在走的这一秒**
    /// ——第 0 秒也是一秒，少算它整条余量会系统性地多给一秒。封顶在环长上。
    /// </summary>
    public int ElapsedSeconds => _consumed;

    /// <summary>分针正指着哪一格。索引比它小的格子已经封盘，等于它的那格还在走。</summary>
    public int CurrentMinute => Math.Min(Math.Max(_consumed - 1, 0) / 60, RingMinutes - 1);

    /// <summary>
    /// 已经浪费掉的秒数 = 走过的秒 − 专注的秒。
    ///
    /// ⚠️ **跑偏的秒和没采到的秒一起算在这里**，这不矛盾：没采到的秒「既不计入也不算
    /// 跑偏」说的是**不冤枉人**（不画红格、不记在任何人头上），可它照样从环上过去了，
    /// 照样吃余量。睡一觉刷满番茄钟之所以做不到，就是因为这两件事是分开的。
    ///
    /// 达成之后冻结——休息不吃余量。
    /// </summary>
    public int WastedSeconds => (AchievedAt is null ? _consumed : _achievedConsumed) - FocusedSeconds;

    /// <summary>
    /// 还剩多少余量 = <see cref="BudgetSeconds"/> − <see cref="WastedSeconds"/>。
    /// 钟面上就是「淡蓝块尾 → 环终点」那一段。**小于 0 就是触底。**
    /// </summary>
    public int SlackSeconds => BudgetSeconds - WastedSeconds;

    public MinuteCell Cell(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, RingMinutes);
        return new MinuteCell(index, _focused[index], _sampled[index]);
    }

    /// <summary>整整 120 格，一格不少——环是一开始就整个存在的，不是长出来的。</summary>
    public IReadOnlyList<MinuteCell> Cells
    {
        get
        {
            var cells = new MinuteCell[RingMinutes];
            for (var i = 0; i < RingMinutes; i++) cells[i] = new MinuteCell(i, _focused[i], _sampled[i]);
            return cells;
        }
    }

    /// <summary>
    /// **把一秒钉进环里。** 每秒调一次，<paramref name="now"/> 就是这一拍的墙上时钟。
    ///
    /// ⚠️ 调用方**不要**自己攒「这一秒采过没有」——那个哨兵在这里
    /// （<see cref="_lastRecorded"/>）：同一秒调两次，第二次不算数；漏掉一拍，那一秒
    /// 就永远空着（= 没采到），**绝不补记**。DESIGN §2.1.1 量到 `DispatcherTimer(1s)`
    /// 13 分钟漏 5 拍且不报错，所以外面必须高频采样 + 靠这里去重。
    /// </summary>
    /// <param name="away">
    /// 人在不在。由 <see cref="AwayMap"/> 跨行算好传进来——⚠️ **别在这里拿单行的
    /// idle 比门槛**，门槛是事后才跨过的，一行一判会把锁屏画成红格。
    /// </param>
    /// <returns>这一秒的判定；这一秒没被记（重复 / 休息中 / 已终结）时返回 null。</returns>
    public SecondJudgment? Observe(DateTimeOffset now, string app, string title, bool away = false)
    {
        if (Ending is not null) return null;

        var slot = SlotAt(now);
        Consume(slot);

        // ── 休息中：只推进阶段，不判定。休息不吃余量，也不可能触底（C6 把这个边界设计掉了）
        if (AchievedAt is not null) { RestCheck(now); return null; }

        SecondJudgment? judged = null;
        if (slot > _lastRecorded && slot < RingSeconds)
        {
            _lastRecorded = slot;
            var j = Judgment.Judge(app, title, away, Goals, _rules);
            judged = j;

            if (j.Sampled)
            {
                var cell = slot / 60;
                _sampled[cell]++;
                if (j.Focused)
                {
                    _focused[cell]++;
                    FocusedSeconds++;
                    _byGoal[j.Goal!] = _byGoal.GetValueOrDefault(j.Goal!) + 1;

                    if (FocusedSeconds >= TargetSeconds)
                    {
                        // 达成就是这一拍本身，不是从账本反推出来的某个整分
                        AchievedAt = now;
                        _achievedConsumed = _consumed;
                        return judged;
                    }
                }
            }
        }

        if (SlackSeconds < 0) End(now, EndReason.RanOut);
        return judged;
    }

    /// <summary>
    /// **只把时钟推到 <paramref name="now"/>，不记任何一秒。**
    ///
    /// 两个用处：
    /// <list type="number">
    ///   <item>同一秒里的第 2~10 拍——那一秒已经记过了，但时间还在走；</item>
    ///   <item>从库里重建之后补最后一段——库里最后一行到此刻之间可能什么都没有
    ///         （程序没跑、电脑睡了），那段时间**照样从环上过去了**，必须推进，
    ///         否则余量不会减少、触底永远不会发生。</item>
    /// </list>
    ///
    /// ⚠️ 它**不会**把那些秒记成任何东西——没观测到就是没观测到（DECISIONS F3）。
    /// </summary>
    public void Advance(DateTimeOffset now)
    {
        if (Ending is not null) return;
        Consume(SlotAt(now));

        if (AchievedAt is not null) { RestCheck(now); return; }
        if (SlackSeconds < 0) End(now, EndReason.RanOut);
    }

    /// <summary>休息走完没有。⚠️ 休息期间不可能触底——C6 把那个边界情况设计掉了。</summary>
    private void RestCheck(DateTimeOffset now)
    {
        if (now >= AchievedAt!.Value.AddMinutes(BreakMinutes)) End(now, EndReason.Completed);
    }

    /// <summary>
    /// 终结这一轮。四个出口（达成 / Give up / 触底 / 关窗）走的是同一条路
    /// ——⚠️ <paramref name="reason"/> **只进显示，不许拿来分叉**（DECISIONS C5）。
    ///
    /// 重复调用被忽略：一轮只终结一次。
    /// </summary>
    public void End(DateTimeOffset now, EndReason reason)
    {
        if (Ending is not null) return;
        Consume(SlotAt(now));
        EndedAt = now;
        Ending = reason;
    }

    /// <summary>
    /// 钟面那三段色块。达成前淡蓝块跟着预计达成点往环尾退，达成后钉死。
    ///
    /// ⚠️ **这块蓝随着你摸鱼一格一格往环尾退，这就是惩罚本身**（DESIGN §5）。
    /// 不弹窗、不出声、不闪烁——加提示等于承认视觉没说清楚（DECISIONS D1）。
    /// </summary>
    public RingProjection Project()
    {
        var hand = _consumed;
        var breakStart = AchievedAt is null ? hand + (TargetSeconds - FocusedSeconds) : _achievedConsumed;
        var breakEnd = breakStart + BreakMinutes * 60;

        // 触底那一帧余量是 −1，夹到 0：那一轮已经结束了，让渲染层再处理一次负数没有意义
        var slack = Math.Max(SlackSeconds, 0);
        if (breakEnd > RingSeconds) { breakEnd = RingSeconds; breakStart = Math.Min(breakStart, breakEnd); }

        return new RingProjection(hand, breakStart, breakStart, breakEnd, slack);
    }

    /// <summary>这一刻落在哪个秒槽里（从 Start 起数，0 开始）。</summary>
    private int SlotAt(DateTimeOffset now)
    {
        var s = (long)Math.Floor((now - StartedAt).TotalSeconds);
        if (s < 0) return 0;                       // 时钟往回跳（对时 / 夏令时）：当没走
        return s > RingSeconds ? RingSeconds : (int)s;
    }

    /// <summary>推进「一共过去了多少秒」。槽位 <c>n</c> 意味着 <c>n+1</c> 秒已经过去了。</summary>
    private void Consume(int slot) => _consumed = Math.Max(_consumed, Math.Min(slot + 1, RingSeconds));
}
