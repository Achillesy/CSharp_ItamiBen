using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ItamiBen.App.Platform;

/// <summary>
/// 前台 <b>app 身份</b>的一次采样。<paramref name="Handle"/> 是读标题要用的句柄，
/// 平台各不相同（macOS 是 pid，Windows 是 HWND）——**调用方不该关心它是什么**，
/// 原样交回 <see cref="ForegroundWindow.ReadTitle"/> 就行。
/// </summary>
/// <param name="Name">macOS 的 <c>localizedName</c>／Windows 的进程名 + <c>.exe</c>。空 = 什么都没读到。</param>
public readonly record struct ForegroundApp(string Name, nint Handle, string Note);

/// <summary>
/// 前台<b>窗口标题</b>的一次采样。<paramref name="Text"/> 为空时看
/// <paramref name="Readable"/> 区分「真的没标题」和「读不到」。
///
/// ⚠️ 判定层不需要这个区分（DECISIONS C2：读到什么就是什么，空标题自然匹配不上），
/// 它只进日志——排查「为什么这一分钟全红」时，`APIDisabled` 和「这窗口本来就没标题」
/// 是完全不同的两件事。
/// </summary>
public readonly record struct ForegroundTitle(string Text, bool Readable, string Note);

/// <summary>
/// **读当前最前面的窗口**——这个项目存在的理由（DESIGN §3）。
///
/// ⚠️ **两条路必须独立，绝不能合进一个调用里。** 这是 ItamiTimer 的死因：
/// `aw-watcher-window-macos` 的 app 身份走 NSWorkspace、标题走**没设超时**的同步 AX IPC，
/// 两半焊在一个轮询循环里；全屏应用（mame）不理会 accessibility 请求时 AX 卡死，
/// 于是连它**已经从 NSWorkspace 拿到的** app 变化都吐不出来，整整哑了 402 秒。
///
/// <list type="bullet">
///   <item><b>app 身份</b>：macOS 走 NSWorkspace，Windows 走 GetForegroundWindow + 进程名。
///         **零权限、不阻塞、几乎不会失败。**</item>
///   <item><b>标题</b>：macOS 走 AX（<b>必须</b> SetMessagingTimeout），Windows 走
///         SendMessageTimeout。**尽力而为，读不到就空着。**</item>
/// </list>
/// </summary>
public static class ForegroundWindow
{
    /// <summary>AX 的 IPC 超时。250ms——对方不理我们就走人，绝不站在那儿等。</summary>
    private const float AxTimeoutSeconds = 0.25f;

    /// <summary>
    /// **第一条路：app 身份。** 零权限、不阻塞、几乎不会失败，微秒级。
    /// 主钟每一拍只调它。
    /// </summary>
    public static ForegroundApp ReadApp()
    {
        if (OperatingSystem.IsMacOS()) return Mac.ReadApp();
        if (OperatingSystem.IsWindows()) return Win.ReadApp();
        return new ForegroundApp("", 0, "unsupported platform");
    }

    /// <summary>
    /// **第二条路：窗口标题。** 尽力而为，带超时，读不到就空着。
    ///
    /// ⚠️ **绝不能跟 <see cref="ReadApp"/> 在同一个循环里调**（DECISIONS B1）——
    /// AW 就是这么死的：标题卡住，连它已经拿到的 app 变化都吐不出来，实测哑了 402 秒。
    /// 产品里它跑在 <see cref="Sampler"/> 的后台线程上。
    /// </summary>
    /// <param name="handle"><see cref="ForegroundApp.Handle"/>，原样传回来。</param>
    public static ForegroundTitle ReadTitle(nint handle)
    {
        if (handle == 0) return new ForegroundTitle("", false, "no window");
        if (OperatingSystem.IsMacOS()) return Mac.ReadTitle(handle);
        if (OperatingSystem.IsWindows()) return Win.ReadTitle(handle);
        return new ForegroundTitle("", false, "unsupported platform");
    }

    /// <summary>macOS 的辅助功能授权状态（Windows 上恒为 true——那边读标题不需要授权）。</summary>
    public static bool TitlePermissionGranted
        => !OperatingSystem.IsMacOS() || Mac.AxTrusted(prompt: false);

    /// <summary>
    /// 请求读标题的权限（只有 macOS 有意义）：弹一次系统授权框，**并且**直接把系统设置里
    /// 那一页打开——弹框只出现一次而且很容易被忽略，两条一起来才靠谱。用户仍要手动勾选。
    /// </summary>
    public static void RequestTitlePermission()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Mac.AxTrusted(prompt: true);
        Mac.OpenAccessibilitySettings();
    }

    // ── macOS ───────────────────────────────────────────────────────────────

    [SupportedOSPlatform("macos")]
    private static class Mac
    {
        private const string Objc = "/usr/lib/libobjc.dylib";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        private const uint Utf8 = 0x08000100;

        [DllImport(Objc)] private static extern IntPtr objc_getClass(string name);
        [DllImport(Objc)] private static extern IntPtr sel_registerName(string name);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr self, IntPtr sel);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern int SendInt(IntPtr self, IntPtr sel);

        [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);
        [DllImport(CoreFoundation)] private static extern bool CFStringGetCString(IntPtr s, byte[] buf, long size, uint encoding);
        [DllImport(CoreFoundation)] private static extern long CFStringGetLength(IntPtr s);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreate(
            IntPtr alloc, IntPtr[] keys, IntPtr[] values, long count, IntPtr keyCb, IntPtr valueCb);

        [DllImport(AppServices)] private static extern IntPtr AXUIElementCreateApplication(int pid);
        [DllImport(AppServices)] private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);
        [DllImport(AppServices)] private static extern int AXUIElementSetMessagingTimeout(IntPtr element, float seconds);
        [DllImport(AppServices)] private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

        [DllImport("/usr/lib/libSystem.dylib")] private static extern IntPtr dlsym(IntPtr handle, string symbol);
        [DllImport("/usr/lib/libSystem.dylib")] private static extern IntPtr dlopen(string path, int mode);
        private static readonly IntPtr RtldDefault = -2;

        /// <summary>
        /// ⚠️ **NSWorkspace 住在 AppKit 里，而 AppKit 不一定已经加载**。GUI 进程里 Avalonia
        /// 会把它带起来，但纯控制台进程（比如探针、单测）里没有——那时 `objc_getClass`
        /// 直接返回 0，后面每一步都安静地拿到空指针，症状是「永远读不到前台窗口」而**不报错**。
        /// 2026-09-15 第一次跑探针就撞上了。所以这里显式 dlopen 一次，不靠别人替我们加载。
        /// </summary>
        private static readonly Lazy<bool> AppKit = new(() =>
            dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 1 /* RTLD_LAZY */) != IntPtr.Zero);

        /// <summary>
        /// ⚠️ **只在 UI 线程调**：AppKit 的东西不保证线程安全，而这一次调用本来就是
        /// 微秒级的，没有挪到后台去的理由。真正需要隔离的是标题那一半。
        /// </summary>
        public static ForegroundApp ReadApp()
        {
            if (!AppKit.Value) return new ForegroundApp("", 0, "AppKit 加载失败");

            // NSWorkspace.sharedWorkspace.frontmostApplication —— 零权限、不阻塞
            var cls = objc_getClass("NSWorkspace");
            if (cls == IntPtr.Zero) return new ForegroundApp("", 0, "找不到 NSWorkspace 类");

            var ws = Send(cls, sel_registerName("sharedWorkspace"));
            var app = Send(ws, sel_registerName("frontmostApplication"));
            if (app == IntPtr.Zero) return new ForegroundApp("", 0, "没有前台应用");

            var name = NsString(Send(app, sel_registerName("localizedName")));
            var pid = SendInt(app, sel_registerName("processIdentifier"));
            return new ForegroundApp(name, pid, "ok");
        }

        /// <summary>句柄是 pid。AX 走的是同步 IPC，所以这个方法**跑在后台线程上**。</summary>
        public static ForegroundTitle ReadTitle(nint pid)
        {
            var (title, readable, note) = AxTitle((int)pid);
            return new ForegroundTitle(title, readable, note);
        }

        private static (string Title, bool Readable, string Note) AxTitle(int pid)
        {
            var el = AXUIElementCreateApplication(pid);
            if (el == IntPtr.Zero) return ("", false, "AXUIElementCreateApplication failed");
            try
            {
                // ⚠️ 这一行就是 AW 缺的那一行。不设超时 = 全屏应用能把我们卡死几分钟。
                AXUIElementSetMessagingTimeout(el, AxTimeoutSeconds);

                var rc = CopyAttr(el, "AXFocusedWindow", out var win);
                if (rc != 0) return ("", false, $"AXFocusedWindow → {AxError(rc)}");
                try
                {
                    AXUIElementSetMessagingTimeout(win, AxTimeoutSeconds);
                    var rc2 = CopyAttr(win, "AXTitle", out var cfTitle);
                    if (rc2 != 0) return ("", false, $"AXTitle → {AxError(rc2)}");
                    try { return (CfString(cfTitle), true, "ok"); }
                    finally { CFRelease(cfTitle); }
                }
                finally { CFRelease(win); }
            }
            finally { CFRelease(el); }
        }

        private static int CopyAttr(IntPtr element, string attribute, out IntPtr value)
        {
            var key = CFStringCreateWithCString(IntPtr.Zero, attribute, Utf8);
            try { return AXUIElementCopyAttributeValue(element, key, out value); }
            finally { CFRelease(key); }
        }

        /// <summary>常见 AXError 的人话版本——探针阶段要看得懂失败原因。</summary>
        private static string AxError(int rc) => rc switch
        {
            -25211 => "APIDisabled（没有辅助功能授权）",
            -25212 => "NoValue（这个窗口没有标题）",
            -25204 => "AttributeUnsupported（这个 app 不给标题）",
            -25205 => "ActionUnsupported",
            -25202 => "InvalidUIElement（窗口刚没了）",
            -25200 => "Failure",
            -25201 => "IllegalArgument",
            -25206 => "NotificationUnsupported",
            -25213 => "ParameterizedAttributeUnsupported",
            -25214 => "NotEnoughPrecision",
            -25215 => "CannotComplete（对方没响应／超时）",
            _ => $"AXError {rc}",
        };

        /// <summary>某个全局常量**变量**的值（dlsym 给的是变量地址，要再解一次）。</summary>
        private static IntPtr GlobalValue(string symbol)
        {
            var addr = dlsym(RtldDefault, symbol);
            return addr == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(addr);
        }

        /// <summary>某个全局常量**结构体**的地址（CF 的回调表要的就是地址，**不能**再解一次）。</summary>
        private static IntPtr GlobalAddress(string symbol) => dlsym(RtldDefault, symbol);

        /// <summary>
        /// 弹一次系统授权框。
        ///
        /// ⚠️ **2026-09-15 这里崩过一次，两个坑都很隐蔽，别再踩**：
        ///
        /// <list type="number">
        ///   <item><b>key 必须是 AX 那个真常量，不能自己造一个同内容的 CFString。</b>
        ///     第一版用 <c>CFStringCreateWithCString("AXTrustedCheckOptionPrompt")</c>
        ///     自己造，配上 NULL 回调表的字典——那种字典按**指针相等**比较 key，AX 拿它的
        ///     常量指针来查当然查不到，返回 NULL，接着 <c>CFGetTypeID(NULL)</c> 直接
        ///     SIGSEGV（读地址 0x8）。**整个 app 当场崩掉**。</item>
        ///   <item><b>回调表要用 kCFType…CallBacks，不能传 NULL。</b> 传 NULL 就是上面那个
        ///     指针相等的语义；用标准回调表才会走 CFEqual/CFHash，也才会正确 retain。</item>
        /// </list>
        ///
        /// 教训：当时我"验证"过字典**造得出来**，但没验 AX **认不认**——验了个寂寞。
        /// 现在任何一个常量取不到就干脆不弹框，绝不拿半截参数去调它。
        /// </summary>
        public static bool AxTrusted(bool prompt)
        {
            if (!prompt) return AXIsProcessTrustedWithOptions(IntPtr.Zero);

            var key = GlobalValue("kAXTrustedCheckOptionPrompt");
            var boolTrue = GlobalValue("kCFBooleanTrue");
            var keyCb = GlobalAddress("kCFTypeDictionaryKeyCallBacks");
            var valCb = GlobalAddress("kCFTypeDictionaryValueCallBacks");

            // 少一个都不弹——宁可不弹，也不能再崩一次
            if (key == IntPtr.Zero || boolTrue == IntPtr.Zero || keyCb == IntPtr.Zero || valCb == IntPtr.Zero)
                return AXIsProcessTrustedWithOptions(IntPtr.Zero);

            var dict = CFDictionaryCreate(IntPtr.Zero, [key], [boolTrue], 1, keyCb, valCb);
            if (dict == IntPtr.Zero) return AXIsProcessTrustedWithOptions(IntPtr.Zero);

            try { return AXIsProcessTrustedWithOptions(dict); }
            finally { CFRelease(dict); }
        }

        /// <summary>直接把「系统设置 → 隐私与安全性 → 辅助功能」那一页打开。弹框容易被忽略，这个更实在。</summary>
        public static void OpenAccessibilitySettings()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility" },
                    UseShellExecute = false,
                });
            }
            catch { /* 打不开就算了，窗口里写了路径 */ }
        }

        private static string NsString(IntPtr ns)
            => ns == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Send(ns, sel_registerName("UTF8String"))) ?? "";

        private static string CfString(IntPtr cf)
        {
            if (cf == IntPtr.Zero) return "";
            var buf = new byte[CFStringGetLength(cf) * 4 + 1];
            return CFStringGetCString(cf, buf, buf.Length, Utf8)
                ? System.Text.Encoding.UTF8.GetString(buf, 0, Array.IndexOf(buf, (byte)0) is var n && n >= 0 ? n : buf.Length)
                : "";
        }
    }

    // ── Windows ─────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static class Win
    {
        private const uint WmGetText = 0x000D;
        private const uint SmtoAbortIfHung = 0x0002;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutW(
            IntPtr hWnd, uint msg, IntPtr wParam, System.Text.StringBuilder lParam,
            uint flags, uint timeoutMs, out IntPtr result);

        /// <summary>
        /// 进程名，不发消息给对方，所以卡不住。
        ///
        /// ⚠️ **锁屏时 <c>GetForegroundWindow()</c> 返回 0**，这里于是返回空 app 名——
        /// 判定层会把这一秒当成「没采到」（DECISIONS C10），这是对的：锁着屏幕的人
        /// 既没在专注也没在摸鱼。
        /// </summary>
        public static ForegroundApp ReadApp()
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return new ForegroundApp("", 0, "no foreground window");

            GetWindowThreadProcessId(h, out var pid);
            var app = "";
            try { app = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName + ".exe"; }
            catch { /* 进程刚没了 */ }

            return new ForegroundApp(app, h, "ok");
        }

        /// <summary>
        /// 句柄是 HWND。⚠️ **一定要用 SendMessageTimeout，不能用 GetWindowText**
        /// ——后者对外部窗口发的是同步 WM_GETTEXT，对方卡住我们就跟着卡住，
        /// 正是 macOS 那边 AX 的同一个坑。
        /// </summary>
        public static ForegroundTitle ReadTitle(nint hWnd)
        {
            var len = GetWindowTextLength(hWnd);
            var sb = new System.Text.StringBuilder(Math.Max(len + 1, 256));
            var ok = SendMessageTimeoutW(hWnd, WmGetText, sb.Capacity, sb, SmtoAbortIfHung, 250, out _) != IntPtr.Zero;
            return new ForegroundTitle(ok ? sb.ToString() : "", ok, ok ? "ok" : "WM_GETTEXT 超时（对方没响应）");
        }
    }
}
