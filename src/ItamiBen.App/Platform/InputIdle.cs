using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ItamiBen.App.Platform;

/// <summary>
/// **全系统的键鼠空闲时长**（不只是本进程）。从 v3 原样搬过来，跟 AW 无关。
///
/// 两个平台各一条调用，都收口在这个文件里：
///
/// <code>
/// Windows   user32 的 GetLastInputInfo
/// macOS     ApplicationServices 的 CGEventSourceSecondsSinceLastEventType
/// </code>
///
/// ⚠️ **macOS 这条不需要辅助功能授权**：它只问「距上次输入多久」，不装任何事件钩子，
/// 读不到任何输入内容。v3 在 macOS 26.5.2 上实测过。
///
/// ⚠️ **读不出来一律当成「刚动过」**（返回 0），不是当成「离开很久」。方向是故意的：
/// 判成离开会让那一段秒既不计入也不算跑偏——冤枉不了人，但会白白吃掉环上的余量。
/// 宁可少判一次离开。
/// </summary>
public static class InputIdle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    // 用老式 DllImport 而不是 LibraryImport：后者要求整个项目打开 AllowUnsafeBlocks，
    // 为这一个调用不值得
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    /// <summary>
    /// <c>kCGEventSourceStateCombinedSessionState</c> = 0 是「整个登录会话范围内的输入」，
    /// 跟 <c>GetLastInputInfo</c> 的范围一致（不限本进程）；
    /// <c>kCGAnyInputEventType</c> = 0xFFFFFFFF 是「任何一种输入事件」。
    /// </summary>
    [DllImport(ApplicationServices)]
    private static extern double CGEventSourceSecondsSinceLastEventType(uint stateId, uint eventType);

    /// <summary>距上次键鼠输入多少秒。读不出来返回 0（见类注释：宁可少判一次离开）。</summary>
    public static int Seconds()
    {
        var elapsed = Elapsed();
        return elapsed < TimeSpan.Zero ? 0 : (int)elapsed.TotalSeconds;
    }

    public static TimeSpan Elapsed()
    {
        if (OperatingSystem.IsWindows()) return WindowsElapsed();
        if (OperatingSystem.IsMacOS()) return MacElapsed();
        return TimeSpan.Zero;
    }

    [SupportedOSPlatform("macos")]
    private static TimeSpan MacElapsed()
    {
        const uint CombinedSessionState = 0;
        const uint AnyInputEventType = 0xFFFFFFFF;
        try
        {
            var seconds = CGEventSourceSecondsSinceLastEventType(CombinedSessionState, AnyInputEventType);
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    [SupportedOSPlatform("windows")]
    private static TimeSpan WindowsElapsed()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // 两个都是 32 位毫秒计数器，49.7 天绕一圈。无符号相减跨越绕回点仍然是对的差值
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
