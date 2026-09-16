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

        // ⚠️ 下面两个是**打包时用的调试出口，不是产品功能**，跟 DECISIONS A3「不做 CLI」
        //    不冲突：A3 禁的是 v3 那种独立的 `itami` 工具，这里只是两个跑完就退的开关。
        //    图标仍然是**代码画出来的**，仓库里一张位图都没有——这条规矩从钟面一路管到这儿。
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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia 需要它保持这个签名和可见性（设计器和 XAML 编译器会找它）
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect();

    /// <summary>
    /// 两个导出开关专用的 AppBuilder。**正常启动不走这里。**
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
