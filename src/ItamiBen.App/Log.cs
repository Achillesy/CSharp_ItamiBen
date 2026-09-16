namespace ItamiBen.App;

/// <summary>
/// 一行一拍的诊断日志，写在运行时目录的 <c>itamiben.log</c>。
///
/// ⚠️ **这不是可选的装饰**：2026-09-15 调试探针时因为没有日志，来回问了用户好几轮
/// 「窗口上到底写的啥」，比改代码还慢。**给程序装眼睛，比省那几行代码重要得多。**
///
/// 写失败一律吞掉——**记不上日志绝不能把程序搞崩**。
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static readonly string Path_ = System.IO.Path.Combine(AppData.Dir, "itamiben.log");

    /// <summary>
    /// <see cref="Start"/> 调过没有。**没调过就一个字都不写。**
    ///
    /// ⚠️ 这不是开关，是护栏：`ItamiBen.App.Tests` 引的是 App 工程本身，测到的代码
    /// （比如 <see cref="Platform.Sound.Duration"/> 读不到文件时）照样会调 <see cref="Warn"/>，
    /// 于是**单元测试往用户真实的 itamiben.log 里写东西**——2026-09-16 实测撞上了，
    /// 调试时看见日志里冒出 `itamiben-no-such-file.wav` 才发现。
    ///
    /// 只有 App 启动时会调 <see cref="Start"/>，测试不会，所以这一条就够了。
    /// 跟 <c>GoalTotals</c> 不碰磁盘、<c>SampleStore</c> 测试传 `:memory:` 是同一族纪律：
    /// **测试碰不到用户的运行时目录。**
    /// </summary>
    private static bool _started;

    /// <summary>每次启动重开一个文件：调试看的永远是这一次运行，不用在几万行里找分界。</summary>
    public static void Start()
    {
        // ⚠️ 先置位再写：这个标志的含义是「现在是 App 在跑」，不是「文件写成功了」
        _started = true;
        try
        {
            Directory.CreateDirectory(AppData.Dir);
            File.WriteAllText(Path_,
                $"# ItamiBen pid={Environment.ProcessId} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        }
        catch { }
    }

    public static void Line(string text)
    {
        if (!_started) return;
        lock (Gate)
        {
            try { File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss}  {text}\n"); }
            catch { }
        }
    }

    /// <summary>
    /// 往**已经在跑的那个实例**的日志里补一行，自己不接管这个文件。
    ///
    /// ⚠️ **专给被单实例锁挡回去的那个进程用**，只有一个调用方（<see cref="SingleInstance"/>）。
    /// 它不能走 <see cref="Start"/>：那是 `File.WriteAllText`，**会把正在跑的那个实例的
    /// 日志整份清空**——本来只想留一句话，结果把现场擦了。
    ///
    /// ⚠️ 也因此它绕过了 <c>_started</c> 那道闸（那道闸是拦单元测试的）。
    /// 别给它加第二个调用方；要在 App 里记日志就用 <see cref="Line"/>。
    /// </summary>
    public static void Aside(string text)
    {
        lock (Gate)
        {
            try { File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss}  {text}\n"); }
            catch { }
        }
    }

    public static void Warn(string text) => Line($"WARN  {text}");

    public static void Error(string what, Exception e) => Line($"ERROR {what}: {e.GetType().Name} {e.Message}");
}
