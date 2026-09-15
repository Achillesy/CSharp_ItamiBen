using Avalonia.Media;

namespace ItamiBen.App;

/// <summary>
/// 钟面的两套配色。**换主题换的是表盘的照明，不是把这只钟换成另一只钟**——
/// 所以木边框两套逐字相同（v3 的 M10，用户 2026-08-29：钟的木制边框不要变）。
///
/// 光源统一假定在**左上**。
/// </summary>
public sealed record DialPalette(
    Color Face, Color FaceRim, Color Ink, Color Tick,
    Color BezelLit, Color BezelMid, Color BezelDark,
    Color Focus, Color OffTask, Color Commit, Color Break, Color Sweep, Color Alarm, Color AlarmsDot, Color AlarmsDotOuter)
{
    /// <summary>日面：白表盘 + 木边框（照着用户给的那张实物挂钟照片调的）。</summary>
    public static readonly DialPalette Light = new(
        Face: Color.FromRgb(0xFF, 0xFF, 0xFF),
        FaceRim: Color.FromRgb(0xF2, 0xF3, 0xF5),     // 表盘边缘，边框把光挡住的地方
        Ink: Color.FromRgb(0x1B, 0x22, 0x2A),
        Tick: Color.FromRgb(0x5A, 0x63, 0x6D),
        BezelLit: Color.FromRgb(0xB5, 0x7C, 0x4C),
        BezelMid: Color.FromRgb(0x8C, 0x58, 0x30),
        BezelDark: Color.FromRgb(0x5A, 0x35, 0x1C),
        Focus: Color.FromRgb(0x2F, 0xA3, 0x6B),
        OffTask: Color.FromRgb(0xD6, 0x45, 0x3F),
        Commit: Color.FromRgb(0x8A, 0x94, 0xA0),      // 灰色承诺弧。⚠️ 不能省（DECISIONS D3）
        Break: Color.FromRgb(0x7F, 0xB2, 0xDD),       // 淡蓝休息块
        Sweep: Color.FromRgb(0x33, 0x40, 0x4B),
        // 闹钟黄针：老式闹钟那种暖黄。比分针短、比时针粗
        Alarm: Color.FromRgb(0xF0, 0xC0, 0x40),
        // alarms.cron 的小红圈。⚠️ 独立色号，**别复用 OffTask 的红**——数值相近也不共用，
        // 以后想单独调其中一个不会牵动另一个（v3 的 J 组同款理由）
        AlarmsDot: Color.FromRgb(0xD6, 0x45, 0x3F),
        // 同一分钟不止一条时外圈换橙。⚠️ 取值要偏艳橙：这个标记的圆心压在木框上
        // （#B57C4C），淡橙会糊进木色里，而红色本来就是靠对比度选的
        AlarmsDotOuter: Color.FromRgb(0xE8, 0x6A, 0x16));

    /// <summary>
    /// 夜面。⚠️ <see cref="Break"/> **单独调亮一档**（DECISIONS D4）：
    /// 淡蓝在深底上容易发灰发脏，两个模式共用一个值必然有一边难看。
    /// 木边框（<c>Bezel*</c>）跟日面**逐字相同**，别「顺手」调暗。
    /// </summary>
    public static readonly DialPalette Dark = new(
        Face: Color.FromRgb(0x20, 0x27, 0x2F),
        FaceRim: Color.FromRgb(0x14, 0x19, 0x1F),
        Ink: Color.FromRgb(0xE4, 0xE9, 0xEF),
        Tick: Color.FromRgb(0x8A, 0x97, 0xA4),
        BezelLit: Color.FromRgb(0xB5, 0x7C, 0x4C),
        BezelMid: Color.FromRgb(0x8C, 0x58, 0x30),
        BezelDark: Color.FromRgb(0x5A, 0x35, 0x1C),
        Focus: Color.FromRgb(0x46, 0xBE, 0x84),
        OffTask: Color.FromRgb(0xE9, 0x63, 0x5C),
        Commit: Color.FromRgb(0x6E, 0x7A, 0x87),
        Break: Color.FromRgb(0x8F, 0xC4, 0xEE),       // 比日面亮一档，见 D4
        Sweep: Color.FromRgb(0xB8, 0xC4, 0xD0),
        Alarm: Color.FromRgb(0xF5, 0xD0, 0x50),
        AlarmsDot: Color.FromRgb(0xE9, 0x63, 0x5C),
        AlarmsDotOuter: Color.FromRgb(0xFA, 0x8A, 0x3C));
}
