using Avalonia;
using Avalonia.Headless;

namespace ItamiBen.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ⚠️ **挂在最前面，不是挂在窗口里**：被单实例挡回去的那个进程根本走不到窗口，
        //    而它恰恰是最需要留句话的那一个。测试从不走 Main，所以这道闸同时也是
        //    「单元测试别往用户真实的 itamiben.log 里写东西」的护栏。
        Log.Arm();

        // 界面对用户是沉默的，所以**崩溃的原因更要留得下来**——否则程序就是凭空消失，
        // 谁也说不清发生了什么
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Events.Error("crash", "Unhandled exception; the program is about to exit", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Events.Error("crash", "Unobserved exception in a background task", e.Exception);
            e.SetObserved();
        };

        // ⚠️ 下面三个是**调试出口，不是产品功能**，跟 DECISIONS A3「不做 CLI」
        //    不冲突：A3 禁的是 v3 那种独立的 `itami` 工具，这里只是两个跑完就退的开关。
        //    图标仍然是**代码画出来的**，仓库里一张位图都没有——这条规矩从钟面一路管到这儿。
        // ⚠️ **这个出口排在最前面，而且一点 Avalonia 都不碰**：它只读 SQLite。
        //    2026-09-16 起观测和事件都只在 samples.db 里（DECISIONS I11），
        //    这条路就是「出了问题直接查数据库」的那个入口。
        if (args is ["--query", var what, ..])
        {
            Query.Run(what, args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
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
            // ⚠️ 日志是**追加**的，所以这一行落在对方的 `event.log` 里正好——
            //    被挡回去的这个进程什么都做不了，留一句话是它唯一能做的事。
            Events.Warn("start", $"another instance (pid {Environment.ProcessId}) tried to start — exiting");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
