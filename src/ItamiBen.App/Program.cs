using Avalonia;
using Avalonia.Headless;

namespace ItamiBen.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 界面对用户是沉默的，所以**崩溃的原因更要留得下来**——否则程序就是凭空消失，
        // 谁也说不清发生了什么
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("Unhandled exception; the program is about to exit", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved exception in a background task", e.Exception);
            e.SetObserved();
        };

        // ⚠️ 下面三个是**调试出口，不是产品功能**，跟 DECISIONS A3「不做 CLI」
        //    不冲突：A3 禁的是 v3 那种独立的 `itami` 工具，这里只是两个跑完就退的开关。
        //    图标仍然是**代码画出来的**，仓库里一张位图都没有——这条规矩从钟面一路管到这儿。
        // ⚠️ **这个出口排在最前面，而且一点 Avalonia 都不碰**：它只读 SQLite。
        //    删掉每秒那行日志之后（2026-09-16），观测数据只在 samples.db 里——
        //    那就必须留一条**不用装 SQLite 工具也能把它读出来**的路，
        //    否则「观测归数据库」在没有工具的机器上等于「观测没了」。
        if (args is ["--dump-samples", ..])
        {
            DumpSamples(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
            return;
        }

        if (args is ["--dial-specimens", var specDir, ..])
        {
            HeadlessBuilder().SetupWithoutStarting();
            DialSpecimens.Render(specDir);
            return;
        }

        if (args is ["--export-icon", var icoPath, ..])
        {
            HeadlessBuilder().SetupWithoutStarting();
            IconExport.Write(icoPath);
            return;
        }

        if (args is ["--export-iconset", var iconsetDir, ..])
        {
            HeadlessBuilder().SetupWithoutStarting();
            IconExport.WriteIconset(iconsetDir);
            return;
        }

        // ⚠️ **排在两个导出开关后面**：它们跑完就退、不碰任何运行时文件，
        //    没理由因为「已经有一个在跑」而被挡住（打包时程序多半正开着）。
        if (!SingleInstance.TryAcquire())
        {
            // ⚠️ **不能用 Log.Line**：日志还没 Start，那句会被静默丢掉；
            //    而 Log.Start 是整份重写，会把**正在跑的那个实例**的日志擦干净。
            //    Aside 只追加一行，落在对方的日志里，正好是想要的。
            Log.Aside($"another instance (pid {Environment.ProcessId}) tried to start — exiting");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// 把 <c>samples.db</c> 的一段观测按 TSV 打到标准输出：`时刻 空闲 app 标题`。
    ///
    /// 不给区间就是**今天**。时刻按本地时区（<see cref="Core.SampleStore"/> 在边界上
    /// 已经归一过了，这里不用再转）。
    ///
    /// ⚠️ 程序正开着也能读：SQLite 走 WAL，读不挡写。
    /// </summary>
    private static void DumpSamples(string? from, string? to)
    {
        var path = AppData.SamplesPath();
        if (!File.Exists(path)) { Console.Error.WriteLine($"no samples.db at {path}"); return; }

        var start = Parse(from) ?? new DateTimeOffset(DateTime.Today, DateTimeOffset.Now.Offset);
        var end = Parse(to) ?? start.AddDays(1);

        using var db = Core.SampleStore.Open(path);
        var rows = db.Read(start, end);
        Console.WriteLine($"# {path}  {start:yyyy-MM-dd HH:mm} → {end:yyyy-MM-dd HH:mm}  ({rows.Count} rows)");
        foreach (var o in rows)
            Console.WriteLine($"{o.At:yyyy-MM-dd HH:mm:ss}\t{o.IdleSeconds}\t{o.App}\t{o.Title}");

        static DateTimeOffset? Parse(string? s)
            => DateTimeOffset.TryParse(s, out var t) ? t : null;
    }

    // Avalonia 需要它保持这个签名和可见性（设计器和 XAML 编译器会找它）
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect();

    /// <summary>
    /// 三个调试出口专用的 AppBuilder。**正常启动不走这里。**
    ///
    /// ⚠️ **为什么不能共用一个**：导出只往 `RenderTargetBitmap` 上画，根本不需要窗口
    /// 系统，而 `UsePlatformDetect` 会去初始化原生窗口系统——有图形会话时看不出区别，
    /// **没有图形会话时当场崩**：
    ///
    /// <code>Avalonia.Native was not able to start the RenderTimer   (-6661)</code>
    ///
    /// 而导出图标正是**打包步骤的一部分**（`run-macos.sh` 会调它）。
    /// **打包不该关心有没有人登录着桌面**——2026-09-16 我自己就因为屏幕锁着而撞过这个
    /// 错误码（记在 CLAUDE.md）。
    ///
    /// <c>UseHeadlessDrawing = false</c> 是另外关键的一半：headless 平台默认**不画**，
    /// 关掉它再挂上 <c>UseSkia</c> 才会渲染出真实像素，用的是跟正常启动同一个 Skia 后端。
    /// </summary>
    private static AppBuilder HeadlessBuilder() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();
}
