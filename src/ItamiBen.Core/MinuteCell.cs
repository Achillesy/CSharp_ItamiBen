namespace ItamiBen.Core;

/// <summary>
/// 环上的一格 = 一分钟。**判定层和渲染层之间唯一的契约。**
///
/// ⚠️ **三个计数，不是一个判定**（DECISIONS C7）。只存一个判定就分不开这几种情况：
/// <code>
/// focused=40, sampled=60, away=0   →  专注 40 秒、跑偏 20 秒
/// focused=40, sampled=40, away=0   →  专注 40 秒、另外 20 秒**没采到**（程序没跑 / 漏拍）
/// focused=40, sampled=40, away=20  →  专注 40 秒、另外 20 秒**人不在**
/// </code>
/// 三者待遇完全不同：跑偏画红、人不在画**空心虚线框**（时间确实过去了，但不怪你）、
/// 没采到**什么都不画**。**不怪你**和**没记录**是两件事，混一起就分不开了。
///
/// **不带颜色，只带档位**（<see cref="Tier"/>）。分界线是：
/// **「这一格该被读成什么」是判定，「某种读法该画成什么样」是渲染。**
/// 档位在这里定义，是为了让「怎么读」这条规则**只写一份**——v3 当年因为 CLI 和钟面
/// 各写一遍，同样的阈值、同样的 argmax、同样的平局规则抄了两份。
///
/// **不带时间戳**：格子的时刻永远是 <c>Round.StartedAt + Index 分钟</c>，
/// 存第二份就是给自己留一个能对不上的机会。
/// </summary>
/// <param name="Index">
/// 从 Start 起的第几分钟，0 开始。⚠️ 它同时就是钟面上的位置：
/// <c>Index / 60</c> 是第几圈，<c>Index % 60</c> 是分针指向哪一格（DESIGN §4.3）。
/// </param>
/// <param name="FocusedSeconds">判成专注的秒数，0~60。</param>
/// <param name="SampledSeconds">**采到了**的秒数（专注 + 跑偏），0~60。恒 ≥ <paramref name="FocusedSeconds"/>。</param>
/// <param name="AwaySeconds">人不在的秒数。⚠️ **不进 <paramref name="SampledSeconds"/>**——它既不计入也不算跑偏。</param>
public readonly record struct MinuteCell(int Index, int FocusedSeconds, int SampledSeconds, int AwaySeconds)
{
    /// <summary>采到了但跑偏的秒数。</summary>
    public int OffTaskSeconds => SampledSeconds - FocusedSeconds;

    /// <summary>这一分钟里**一秒都没记下来**的秒数：程序没跑、电脑睡了、漏拍。</summary>
    public int UnrecordedSeconds => 60 - SampledSeconds - AwaySeconds;

    /// <summary>
    /// 这一格该被读成什么。**这条规则只写在这一个地方。**
    ///
    /// 有专注就按 <c>&gt;40 / &gt;20 / &gt;0</c> 分三档；一秒专注都没有时，在其余三类里
    /// 取**最大的那个**，平局倒向**更靠后的档位**（OffTask &gt; Away &gt; NotDrawn，
    /// 也就是 fail-closed）。
    ///
    /// ⚠️ **是 argmax 不是「过半数」**：三类混在一起时可能谁都不过半，而按阈值判会
    /// 默认落进红色——那样「29 秒离开 + 28 秒跑偏」会被整格判成红的，而
    /// **把离开画成跑偏是冤枉人**。argmax 没有阈值，也就没有这个悬崖。
    /// </summary>
    public CellTier Tier
    {
        get
        {
            if (FocusedSeconds > 40) return CellTier.FocusFull;
            if (FocusedSeconds > 20) return CellTier.FocusMid;
            if (FocusedSeconds > 0) return CellTier.FocusLow;

            var best = UnrecordedSeconds;
            var pick = CellTier.NotDrawn;
            if (AwaySeconds >= best) { best = AwaySeconds; pick = CellTier.Away; }
            if (OffTaskSeconds >= best) pick = CellTier.OffTask;
            return pick;
        }
    }
}

/// <summary>
/// 一格怎么读。**顺序就是「谁盖谁」的顺序**：平局倒向更靠后的那个。
/// </summary>
public enum CellTier : byte
{
    /// <summary>一秒都没记下来（漏拍留下的洞）。**什么都不画**——它不配占任何视觉面积。</summary>
    NotDrawn,

    /// <summary>人不在。不计入，但**也不怪你**——空心虚线框：这段时间确实存在，只是不属于任何一边。</summary>
    Away,

    /// <summary>采到了但不匹配。红。</summary>
    OffTask,

    /// <summary>1~20 秒专注。</summary>
    FocusLow,

    /// <summary>21~40 秒专注。</summary>
    FocusMid,

    /// <summary>41~60 秒专注。</summary>
    FocusFull,
}
