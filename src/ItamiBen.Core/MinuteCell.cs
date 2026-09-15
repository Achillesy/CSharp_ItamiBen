namespace ItamiBen.Core;

/// <summary>
/// 环上的一格 = 一分钟。**判定层和渲染层之间唯一的契约。**
///
/// ⚠️ **两个计数，不是一个判定**（DECISIONS C7）。只存一个判定就分不开这两种情况：
/// <code>
/// focused=40, sampled=60  →  专注 40 秒、跑偏 20 秒
/// focused=40, sampled=40  →  专注 40 秒、另外 20 秒没采到（睡了 / 程序没跑 / 漏拍）
/// </code>
/// 两者待遇完全不同：前者是红格，后者留白——**没采到的秒既不计入也不冤枉人**。
///
/// **不带颜色，也不带档位。** DESIGN §4.4 写的是「按秒数高度的绿 / 红 / 空白」，
/// 是连续编码不是分档，所以这里给原始秒数，怎么画是渲染层的事。
///
/// **不带时间戳**：格子的时刻永远是 <c>Round.StartedAt + Index 分钟</c>，
/// 存第二份就是给自己留一个能对不上的机会。
/// </summary>
/// <param name="Index">
/// 从 Start 起的第几分钟，0 开始。⚠️ 它同时就是钟面上的位置：
/// <c>Index / 60</c> 是第几圈，<c>Index % 60</c> 是分针指向哪一格（DESIGN §4.3）。
/// **不环绕、不归档、不塌缩**，所以这个索引一辈子不会被改写。
/// </param>
/// <param name="FocusedSeconds">这一分钟里判成专注的秒数，0~60。</param>
/// <param name="SampledSeconds">这一分钟里**采到了**的秒数，0~60。恒 ≥ <paramref name="FocusedSeconds"/>。</param>
public readonly record struct MinuteCell(int Index, int FocusedSeconds, int SampledSeconds)
{
    /// <summary>采到了但跑偏的秒数。红格的高度。</summary>
    public int OffTaskSeconds => SampledSeconds - FocusedSeconds;

    /// <summary>
    /// 这一分钟里没采到的秒数。留白的高度。
    ///
    /// ⚠️ **只有走完的格子读它才有意义**：正在走的那一格、以及还没到的格子，
    /// 剩下的秒只是「还没发生」，不是「没采到」。谁是走完的格子看
    /// <see cref="Round.CurrentMinute"/>——索引比它小的才封了盘。
    /// </summary>
    public int UnsampledSeconds => 60 - SampledSeconds;
}
