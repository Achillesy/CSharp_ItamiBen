using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ItamiBen.App.Platform;

/// <summary>前台窗口的一次采样。<paramref name="Title"/> 为空时看 <paramref name="TitleReadable"/> 区分「真的没标题」和「读不到」。</summary>
public readonly record struct Foreground(string App, string Title, bool TitleReadable, string Note);

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

    public static Foreground Read()
    {
        if (OperatingSystem.IsMacOS()) return Mac.Read();
        if (OperatingSystem.IsWindows()) return Win.Read();
        return new Foreground("", "", false, "unsupported platform");
    }

    /// <summary>macOS 的辅助功能授权状态（Windows 上恒为 true——那边读标题不需要授权）。</summary>
    public static bool TitlePermissionGranted
        => !OperatingSystem.IsMacOS() || Mac.AxTrusted(prompt: false);

    /// <summary>弹一次系统授权框（只有 macOS 有意义）。用户点了「打开系统设置」之后仍要手动勾选。</summary>
    public static void RequestTitlePermission()
    {
        if (OperatingSystem.IsMacOS()) Mac.AxTrusted(prompt: true);
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

        public static Foreground Read()
        {
            if (!AppKit.Value) return new Foreground("", "", false, "AppKit 加载失败");

            // ① app 身份：NSWorkspace.sharedWorkspace.frontmostApplication —— 零权限、不阻塞
            var cls = objc_getClass("NSWorkspace");
            if (cls == IntPtr.Zero) return new Foreground("", "", false, "找不到 NSWorkspace 类");

            var ws = Send(cls, sel_registerName("sharedWorkspace"));
            var app = Send(ws, sel_registerName("frontmostApplication"));
            if (app == IntPtr.Zero) return new Foreground("", "", false, "没有前台应用");

            var name = NsString(Send(app, sel_registerName("localizedName")));
            var pid = SendInt(app, sel_registerName("processIdentifier"));

            // ② 标题：AX，尽力而为。**这一半失败不影响上面那一半**——整个设计的要点
            var (title, readable, note) = AxTitle(pid);
            return new Foreground(name, title, readable, note);
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

        public static bool AxTrusted(bool prompt)
        {
            if (!prompt) return AXIsProcessTrustedWithOptions(IntPtr.Zero);

            // kAXTrustedCheckOptionPrompt 的字符串值就是它的名字；kCFBooleanTrue 用 dlsym 取
            var key = CFStringCreateWithCString(IntPtr.Zero, "AXTrustedCheckOptionPrompt", Utf8);
            var boolTruePtr = dlsym(RtldDefault, "kCFBooleanTrue");
            var boolTrue = boolTruePtr == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(boolTruePtr);
            if (boolTrue == IntPtr.Zero) { CFRelease(key); return AXIsProcessTrustedWithOptions(IntPtr.Zero); }

            var dict = CFDictionaryCreate(IntPtr.Zero, [key], [boolTrue], 1, IntPtr.Zero, IntPtr.Zero);
            try { return AXIsProcessTrustedWithOptions(dict); }
            finally { CFRelease(dict); CFRelease(key); }
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

        public static Foreground Read()
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return new Foreground("", "", false, "no foreground window");

            // ① app 身份：进程名。不发消息给对方，卡不住
            GetWindowThreadProcessId(h, out var pid);
            var app = "";
            try { app = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName + ".exe"; }
            catch { /* 进程刚没了 */ }

            // ② 标题：⚠️ **一定要用 SendMessageTimeout，不能用 GetWindowText**
            //    ——后者对外部窗口发的是同步 WM_GETTEXT，对方卡住我们就跟着卡住，
            //    正是 macOS 那边 AX 的同一个坑
            var len = GetWindowTextLength(h);
            var sb = new System.Text.StringBuilder(Math.Max(len + 1, 256));
            var ok = SendMessageTimeoutW(h, WmGetText, sb.Capacity, sb, SmtoAbortIfHung, 250, out _) != IntPtr.Zero;
            return new Foreground(app, ok ? sb.ToString() : "", ok, ok ? "ok" : "WM_GETTEXT 超时（对方没响应）");
        }
    }
}
