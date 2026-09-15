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

    /// <summary>每次启动重开一个文件：调试看的永远是这一次运行，不用在几万行里找分界。</summary>
    public static void Start()
    {
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
        lock (Gate)
        {
            try { File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss}  {text}\n"); }
            catch { }
        }
    }

    public static void Warn(string text) => Line($"WARN  {text}");

    public static void Error(string what, Exception e) => Line($"ERROR {what}: {e.GetType().Name} {e.Message}");
}
