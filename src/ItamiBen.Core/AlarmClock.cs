namespace ItamiBen.Core;

/// <summary>
/// 闹钟模型。**纯逻辑、没有 UI、没有时钟**——<c>now</c> 一律是参数，整个类直接可单测。
///
/// 从 v3 原样搬过来（DESIGN §6），它跟 ActivityWatch 无关，护栏见
/// `../ItamiTimer/DECISIONS.md` 的 E 组。⚠️ v3 把它放在 App 里，v4 放 Core——
/// 它符合 Core 的全部条件，放 App 只会让它测不到。
///
/// **状态只有一个：<see cref="FireAt"/>**，黄针的位置是它对 12 小时取余的**推导值**
/// （v3 的 E7，2026-07-30 定稿，推翻了同日早些的「位置 + 时刻各存一份」）。
/// 两个值会漂，一个不会。
///
/// ⚠️ 用 <see cref="DateTime"/> 而不是 <see cref="DateTimeOffset"/>：这是**墙上时钟**
/// 的功能，「今天的 9:05」就是本地的 9:05，`now.Date` 这类运算在 DateTime 上才是直白的。
/// 环那边（<see cref="Round"/>）用 DateTimeOffset，两者不需要统一——它们量的不是同一种东西。
/// </summary>
public sealed class AlarmClock
{
    /// <summary>滚轮一格 = 1 分钟；表盘一圈 720 分钟（12 小时制），所以一共 720 个位置。</summary>
    public const double SlotMinutes = 1;

    public const double FaceMinutes = 720;

    /// <summary>
    /// 最后一次拨针算出来的响铃时刻。**响过也不清空**——它仍然是黄针位置的来源；
    /// 「响过没有」由 <see cref="_fired"/> 单独记。null = 从没拨过针。
    /// </summary>
    public DateTime? FireAt { get; private set; }

    /// <summary>这一轮的时刻是否已经响过（或者恢复时就已经过期 = 无效）。</summary>
    private bool _fired;

    /// <summary>
    /// 黄针在表盘上的位置（0~719 分钟）：**时刻对 12 小时取余**。
    /// 从没拨过针就停在 12 点（0）。
    /// </summary>
    public double Position => FireAt is { } at
        ? at.Hour % 12 * 60 + at.Minute + at.Second / 60.0
        : 0;

    /// <summary>
    /// 把黄针拨动 <paramref name="minutes"/> 分钟——正数顺时针、负数逆时针。
    /// 拨完**立刻**用严格算法重算「下次什么时候响」；界面上显示的就是
    /// <see cref="FireAt"/> 本身——**显示的和真会响的是同一个值**。
    ///
    /// <see cref="NextRing"/> 保持表盘位置不变（今天的 T / T+12 / 明天的 T 对 12 小时
    /// 取余都一样），所以推导出来的 <see cref="Position"/> 拨完正好落在新位置上。
    /// </summary>
    public void Bump(double minutes, DateTime now)
    {
        // ⚠️ C# 的 % 对负数给负结果；逆时针越过 12 点要的是真正的模运算
        var pos = ((Position + minutes) % FaceMinutes + FaceMinutes) % FaceMinutes;
        FireAt = NextRing(now, pos);
        _fired = false;
    }

    /// <summary>
    /// 到点了没有。单调比较，**不重新推导黄针位置**，也不留任何角度容差
    /// （一分钟才 0.5°，给 1.5° 的「容差」等于提前三分钟响，v3 的 E1）。
    /// </summary>
    public bool ShouldFire(DateTime now) => !_fired && FireAt is { } at && now >= at;

    /// <summary>
    /// 响一次就完，**不是每日重复闹钟**（v3 的 E5）。时刻留着，作为黄针位置的来源。
    /// </summary>
    public void MarkFired() => _fired = true;

    /// <summary>
    /// 从上次会话恢复：读回时刻**只为了显示，不激活闹钟**。
    /// 关着程序时错过的闹钟**不补响**，只剩黄针残影。
    /// </summary>
    public void Restore(DateTime? fireAt)
    {
        FireAt = fireAt;
        _fired = true;   // 启动时一律不激活，拨一下滚轮才算数
    }

    /// <summary>
    /// 真正的「响铃时刻」算法：黄针位置（12 小时制的表盘时刻 T）→ 下一次真的会响的那一刻。
    /// 三档全部用**严格小于**（v3 的 E2）：
    ///
    /// <code>
    /// now &lt; 今天的 T       → 今天的 T（上午那半）
    /// now &lt; 今天的 T + 12h → T + 12（下午那半）
    /// 否则                   → 明天的 T
    /// </code>
    ///
    /// ⚠️ **故意不是「小于等于」**：如果 now 正好落在黄针那一格上（拨针的这一刻恰好
    /// 跟时针重合），那意思是「12 小时之后」，不是「就是现在」——否则会在你正调它的
    /// 时候当场响起来。
    /// </summary>
    public static DateTime NextRing(DateTime now, double faceMinutes)
    {
        var t = now.Date.AddMinutes(faceMinutes);
        var tPlus12 = t.AddHours(12);
        if (now < t) return t;
        if (now < tPlus12) return tPlus12;
        return t.AddDays(1);
    }
}
