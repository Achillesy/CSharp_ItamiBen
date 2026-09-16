using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ItamiBen.App;

/// <summary>
/// 单实例限制。
///
/// **v4 比 v3 更需要它。** v3 的判定数据在 ActivityWatch 那边，本程序基本只读；
/// v4 自己**每秒往 <c>samples.db</c> 写一行**，退出时还要整份重写 <c>during.json</c>
/// 和 <c>settings.json</c>。两个实例同时跑的后果：
///
/// <list type="bullet">
///   <item><c>during.json</c> / <c>settings.json</c> 是**整份重写**——后退出的那个把先
///   退出的那个盖掉，**一整轮的累计秒数凭空消失，而且不报错**。</item>
///   <item><c>round</c> 表用 <c>INSERT OR REPLACE</c>（`started_at` 抹到整分），同一分钟
///   里两边各按一次 Start，后写的把前一个的目标和时长覆盖掉。</item>
///   <item>闹钟响两遍，**到点的命令跑两次**。</item>
/// </list>
///
/// 采样那一层反而没事（<c>INSERT OR IGNORE</c>，而且两个实例读的是同一个前台窗口），
/// 这恰恰是它危险的地方：**最显眼的那部分看起来完全正常**，坏掉的是账本。
///
/// <para>
/// ⚠️ **锁用的是「独占打开一个文件」，不是 v3 那个命名 Mutex**——2026-09-16 实测，
/// v3 那套在 macOS 上**根本不生效**：.NET 在 Unix 上把不带前缀的命名 Mutex 放进
/// <c>/tmp/.dotnet/shm/session&lt;N&gt;/</c>，这个 N 是**进程的 session id**。
/// 用 <c>open</c> 起的 .app 落在 <c>session1</c>（launchd 那个），从终端起的落在
/// <c>session&lt;shell 的 pid&gt;</c>——**两个不同的命名空间，谁也拦不住谁**。
/// 实测两个实例同时跑起来了，`/tmp/.dotnet/shm/` 下面真的躺着两份同名的锁。
/// </para>
///
/// <para>
/// 改用 `Global\` 前缀能绕过这一条，但那是**全机器**命名空间：同一台机器上两个用户
/// （快速用户切换）会互相挡住。而独占文件锁同时解决两头——
/// 锁文件就放在**本用户的运行时目录**里，跟它保护的那些账本文件待在一起：
/// </para>
///
/// <list type="bullet">
///   <item>**天然按用户隔离**：目录本来就是每用户一份，两个平台都是。</item>
///   <item>**进程一死就释放**，`kill -9` 也算——句柄是操作系统收的，
///   不存在「上次崩了锁没放开」。</item>
///   <item>**一份实现管两个平台**：Windows 的 `FileShare.None` 和 Unix 的
///   `flock` 都由运行时兜住了。</item>
/// </list>
///
/// **第二个实例发现锁被占时，不弹错、也不是干脆退出**：它把已经在跑的那扇窗口提到
/// 前台，然后自己安静退出——用户的动作（双击图标 / 快捷键）总该有个反应，
/// 而不是变成一次毫无反馈的点击。
///
/// ⚠️ **只有 Windows 有「提到前台」那一步**：<c>FindWindow</c> + <c>SetForegroundWindow</c>
/// 是 Win32 API，macOS 没有零依赖的等价物。macOS 上退化成「安静地拒绝第二个实例」
/// ——单实例保证本身仍然成立，只是少了把旧窗口叫到前面来的那点体贴。
/// ⚠️ 这半边**没在真机上跑过**，跟 <c>ForegroundWindow.Win</c> 一样是纸面代码。
/// </summary>
public static class SingleInstance
{
    private const string LockFile = "singleton.lock";

    /// <summary>
    /// Windows 上按窗口标题找已有的那扇窗。
    ///
    /// ⚠️ **这个字符串必须跟 `MainWindow.axaml` 的 `Title` 逐字相同**，改一边不报错，
    /// 只会让「提到前台」安静失效（症状：第二次双击图标什么也没发生）。
    /// 界面文字用英文，但窗口标题是中文的产品名，所以这里也是中文。
    /// </summary>
    private const string WindowTitle = "一袋米要我洗嘞";

    /// <summary>
    /// 留着引用，**防止被 GC 回收**——FileStream 的终结器会关掉句柄，句柄一关锁就没了，
    /// 后来的实例会误以为自己是第一个。进程退出时操作系统自己收，不需要手工 Dispose。
    /// </summary>
    private static FileStream? _lock;

    /// <summary>
    /// 尝试拿下单实例锁。<c>true</c> = 自己是第一个，照常启动；
    /// <c>false</c> = 已经有一个在跑，调用方应当立刻退出
    /// （Windows 上会先把旧窗口提到前台）。
    /// </summary>
    public static bool TryAcquire()
    {
        string path;
        try
        {
            Directory.CreateDirectory(AppData.Dir);
            path = Path.Combine(AppData.Dir, LockFile);
        }
        catch (Exception e)
        {
            // ⚠️ **准备阶段出意外就放行**：单实例是便利，不是安全边界。
            //    让程序起来，比让它因为一个锁文件起不来强。
            Log.Aside($"single-instance check skipped ({e.GetType().Name}: {e.Message})");
            return true;
        }

        try
        {
            _lock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            // 独占打开失败 = 别人占着。这是**唯一**认定「已经有一个在跑」的信号
            if (OperatingSystem.IsWindows()) ActivateExistingWindow();
            return false;
        }
        catch (UnauthorizedAccessException e)
        {
            // 权限问题不等于有人在跑（目录只读、被安全软件挡住……），同样放行
            Log.Aside($"single-instance check skipped ({e.GetType().Name}: {e.Message})");
            return true;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ActivateExistingWindow()
    {
        var hwnd = FindWindow(null, WindowTitle);
        if (hwnd == IntPtr.Zero) return;   // 找不到就算了，安静退出

        const int SW_RESTORE = 9;
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowTitle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
