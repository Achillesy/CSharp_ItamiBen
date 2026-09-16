namespace ItamiBen.App;

/// <summary>
/// **库里装不下的那些话。** 两类，都在这个文件里：
///
/// <list type="number">
///   <item><b>每一次手动改配置</b>（<see cref="Applied"/>）——用户要的是什么、实际跑的
///   是什么、成没成。这是整个程序里唯一不可逆、且由外人写的动作，而**结果在库里、
///   那句话推不出来**。⚠️ 它**必须在数据库外面**：SQLite 够不着普通文件
///   （`ATTACH` 只能挂另一个库），所以这份记录不在任何一句外来 SQL 的射程之内；
///   库坏了要修的时候，「我到底干过什么」也不在那个坏掉的库里面。</item>
///
///   <item><b>够不着数据库时的求救</b>（<see cref="Fallback"/>）——库打不开、被单实例
///   挡回去、启动早期或退出之后崩了。</item>
/// </list>
///
/// ⚠️ **原来这个文件是二值的**（里面有东西 = 出事了），2026-09-16 用户把记账并进来，
/// 这个性质就没了——**这是知情的取舍**：换来的是智能体只有一个地方要写。
/// 找毛病改成翻 `error` / `FAILED` 那几行，或者 `--query events`。
///
/// ⚠️ **不滚存、不截断**：滚掉的正好是最早、最难回忆的那些改动。一次几百字节、
/// 一个月几次，一年也就几 KB。
///
/// 写失败一律吞掉——**记不上话绝不能把程序搞崩**。
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static readonly string Path_ = System.IO.Path.Combine(AppData.Dir, "itamiben.log");

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
                File.AppendAllText(Path_,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId}  {text}\n");
            }
            catch { }
        }
    }

    public static void Error(string what, Exception e)
        => Fallback($"error {what}: {e.GetType().Name} {e.Message}");

    /// <summary>
    /// 记一次手动改配置。<paramref name="request"/> 是用户当时写的那句需求。
    ///
    /// ⚠️ **意图和产物要配在一起**：光有 SQL，谁也看不出智能体有没有理解错那句话。
    /// ⚠️ **成功失败都记**，失败的更值钱——尤其是「这段 SQL 想干什么、为什么没跑成」。
    /// </summary>
    public static void Applied(string? request, string statement, bool ok, int rows, string? message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppData.Dir);
                var head = ok ? $"OK  {rows} row(s)" : $"FAILED  {message}";
                File.AppendAllText(Path_, $"""

                    ────────────────────────────────────────────────────────
                    {DateTime.Now:yyyy-MM-dd HH:mm:ss}  applied  {head}
                    {(string.IsNullOrWhiteSpace(request) ? "asked: (not recorded)" : "asked: " + request.Trim())}

                    {statement.Trim()}

                    """);
            }
            catch { }
        }
    }

    /// <summary>整份读出来给 <c>--query log</c>。</summary>
    public static string Read()
    {
        try { return File.Exists(Path_) ? File.ReadAllText(Path_) : ""; }
        catch (Exception e) { return $"(could not read {Path_}: {e.Message})"; }
    }
}
