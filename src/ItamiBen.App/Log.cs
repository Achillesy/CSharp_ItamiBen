namespace ItamiBen.App;

/// <summary>
/// **最后的求救信，不是日志。**
///
/// 2026-09-16 之前这里是一份正经的运行日志；现在正经的记录去了 `samples.db` 的
/// `event` 表（见 <see cref="Events"/>）。这个文件只剩一个用处：
/// **数据库够不着的时候，把话留在某个地方。**
///
/// 够不着一共就那么几种：
/// <list type="bullet">
///   <item>观测库自己打不开（磁盘满、文件损坏、权限没了）；</item>
///   <item>被单实例锁挡回去了，这个进程压根没开库；</item>
///   <item>启动早期或退出之后崩了，那时候库还没挂上 / 已经关了。</item>
/// </list>
///
/// 正常跑一天，这个文件**一个字节都不该长**。里面有东西 = 出事了。
///
/// 写失败一律吞掉——**记不上话绝不能把程序搞崩**。
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static readonly string Path_ = System.IO.Path.Combine(AppData.Dir, "itamiben.log");
    private static readonly string Old = Path_ + ".old";

    /// <summary>
    /// 到这个大小就挪成 `.old`，只留一份旧的。
    ///
    /// 这个文件本来就该是空的，门槛纯粹是**防止某个高频错误把磁盘灌满**——
    /// 那种情况下最早那几行才有用，所以 256KB 绰绰有余。
    /// </summary>
    private const long MaxBytes = 256 * 1024;

    /// <summary>
    /// <see cref="Arm"/> 调过没有。**没调过就一个字都不写。**
    ///
    /// ⚠️ 这不是开关，是护栏：`ItamiBen.App.Tests` 引的是 App 工程本身，被测到的代码
    /// （比如 <see cref="Platform.Sound"/> 读不到文件时）照样会报错，而那时数据库是空的、
    /// 会落到这里来——于是**单元测试往用户真实的 itamiben.log 里写东西**
    /// （2026-09-16 实测撞上过，看见日志里冒出 `itamiben-no-such-file.wav` 才发现）。
    ///
    /// ⚠️ 挂在 <c>Program.Main</c> 的最前面，**不是挂在窗口里**：被单实例挡回去的那个
    /// 进程根本走不到窗口，而它恰恰是最需要留句话的那一个。
    /// </summary>
    private static bool _armed;

    public static void Arm() => _armed = true;

    /// <summary>留一句话。<paramref name="text"/> 自带级别和分类，这里只管落盘。</summary>
    public static void Fallback(string text)
    {
        if (!_armed) return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppData.Dir);
                Roll();
                File.AppendAllText(Path_,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId}  {text}\n");
            }
            catch { }
        }
    }

    public static void Error(string what, Exception e)
        => Fallback($"error {what}: {e.GetType().Name} {e.Message}");

    /// <summary>调用方必须已经持有 <see cref="Gate"/>，而且自己负责吞异常。</summary>
    private static void Roll()
    {
        var f = new FileInfo(Path_);
        if (!f.Exists || f.Length < MaxBytes) return;
        if (File.Exists(Old)) File.Delete(Old);
        File.Move(Path_, Old);
    }
}
