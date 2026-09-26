using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ItamiBen.App.Platform;

/// <summary>
/// Linux/X11 的前台窗口与空闲检测。跟 Mac/Win 那两份一样，**判定层只关心三件事**：
/// 这一秒前台是谁（<see cref="ReadActiveApp"/>）、它的标题是什么（<see cref="ReadWindowTitle"/>）、
/// 人离开多久了（<see cref="IdleElapsed"/>）。X11 上这三件事都不需要任何权限：
/// <c>_NET_ACTIVE_WINDOW</c>、<c>WM_CLASS</c>、XScreenSaver，全是问 X server 要的，
/// 本地 socket 一问一答。
///
/// ⚠️ **Wayland 下没有 X11**：<c>XOpenDisplay</c> 直接返回空指针，所有方法安静地返回空，
/// 判定层会把每一秒当成「没采到」（跟 Windows 锁屏一个待遇）。WSL 默认是 WSLg
/// （X11 走本地 socket），没问题；原生 Wayland 会话暂时无解。
///
/// ⚠️ **库名必须带版本号**（<c>libX11.so.6</c> 而不是 <c>libX11</c>）：Ubuntu 的
/// <c>libx11-6</c> 包里只有带版本号的 .so，不带版本号的那个是 libx11-dev 的，
/// 用户机器上大概率没有。不带版本号 DllImport 直接 DllNotFoundException。
///
/// 所有公开方法**永不抛异常**：X server 连不上、窗口刚没了、库缺了，一律返回空。
/// 方向跟 <see cref="InputIdle"/> 一致——宁可这一秒没采到，也不崩。
/// </summary>
[SupportedOSPlatform("linux")]
internal static class X11
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXss = "libXss.so.1";

    // EWMH：_NET_ACTIVE_WINDOW 是 root window 上的一个属性，类型 WINDOW，值就是前台窗口 id
    private const nint AnyPropertyType = 0;

    [DllImport(LibX11)] private static extern nint XOpenDisplay(string? display);
    [DllImport(LibX11)] private static extern int XCloseDisplay(nint display);
    [DllImport(LibX11)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(LibX11)] private static extern nint XInternAtom(nint display, string name, bool onlyIfExists);
    [DllImport(LibX11)]
    private static extern int XGetWindowProperty(
        nint display, nint window, nint property,
        nint longOffset, nint longLength, bool delete, nint reqType,
        out nint actualType, out int actualFormat,
        out nuint nItems, out nuint bytesAfter, out nint prop);
    [DllImport(LibX11)] private static extern int XGetClassHint(nint display, nint window, nint hint);
    [DllImport(LibX11)] private static extern int XFetchName(nint display, nint window, out nint name);
    [DllImport(LibX11)] private static extern int XFree(nint ptr);

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint
    {
        public nint ResName;
        public nint ResClass;
    }

    [DllImport(LibXss)]
    private static extern int XScreenSaverQueryInfo(nint display, nint drawable, ref XScreenSaverInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public nint Window;
        public int State;
        public int Kind;
        public nuint Since;
        public nuint Idle; // 距上次输入的毫秒数
        public nuint EventMask;
    }

    /// <summary>
    /// 前台应用的身份 + 窗口句柄。身份取 <c>WM_CLASS</c> 的 res_class
    /// （VS Code 是 "Code"，Chrome 是 "Google-chrome"），对应 macOS 的 localizedName、
    /// Windows 的进程名。句柄是 X11 Window id，原样交回 <see cref="ReadWindowTitle"/>。
    ///
    /// ⚠️ **每次调用都开关一次 display**：XOpenDisplay 只是连一下本地 socket，
    /// 微秒级；但连接不能跨线程复用，各调各的最省心。跟 Win 那边每秒 GetProcessById
    /// 一个量级。
    /// </summary>
    public static (string App, nint Window) ReadActiveApp()
    {
        nint dpy = nint.Zero;
        try
        {
            dpy = XOpenDisplay(null); // 读 $DISPLAY；无头机器／Wayland 直接返回空指针
            if (dpy == nint.Zero) return ("", nint.Zero);

            var root = XDefaultRootWindow(dpy);
            var activeAtom = XInternAtom(dpy, "_NET_ACTIVE_WINDOW", false);
            var rc = XGetWindowProperty(dpy, root, activeAtom, 0, 1, false, AnyPropertyType,
                out _, out var format, out var nItems, out _, out var prop);
            if (rc != 0 || format != 32 || nItems == 0 || prop == nint.Zero)
                return ("", nint.Zero);

            nint active;
            try { active = Marshal.ReadIntPtr(prop); }
            finally { XFree(prop); }
            if (active == nint.Zero) return ("", nint.Zero); // 桌面／没聚焦

            return (ClassHint(dpy, active), active);
        }
        catch { return ("", nint.Zero); }
        finally { if (dpy != nint.Zero) XCloseDisplay(dpy); }
    }

    private static string ClassHint(nint dpy, nint window)
    {
        var size = Marshal.SizeOf<XClassHint>();
        var hintPtr = Marshal.AllocHGlobal(size);
        try
        {
            // XGetClassHint 只写成功的那两个指针，先清零，免得 XFree 到野指针
            for (var i = 0; i < size; i += nint.Size) Marshal.WriteIntPtr(hintPtr, i, nint.Zero);
            if (XGetClassHint(dpy, window, hintPtr) == 0) return "";
            var hint = Marshal.PtrToStructure<XClassHint>(hintPtr);
            try
            {
                return hint.ResClass != nint.Zero ? Marshal.PtrToStringUTF8(hint.ResClass) ?? "" : "";
            }
            finally
            {
                if (hint.ResName != nint.Zero) XFree(hint.ResName);
                if (hint.ResClass != nint.Zero) XFree(hint.ResClass);
            }
        }
        finally { Marshal.FreeHGlobal(hintPtr); }
    }

    /// <summary>
    /// 窗口标题。先试 <c>_NET_WM_NAME</c>（UTF-8，现代 WM 都设这个），
    /// 没有再退到 <c>WM_NAME</c>。读不到返回空——标题本来就是尽力而为。
    /// </summary>
    public static string ReadWindowTitle(nint window)
    {
        if (window == nint.Zero) return "";
        nint dpy = nint.Zero;
        try
        {
            dpy = XOpenDisplay(null);
            if (dpy == nint.Zero) return "";

            var netWmName = XInternAtom(dpy, "_NET_WM_NAME", false);
            var utf8 = XInternAtom(dpy, "UTF8_STRING", false);
            // 长度单位是 32 位：1024 = 最多 4KB 标题，够了
            var rc = XGetWindowProperty(dpy, window, netWmName, 0, 1024, false, utf8,
                out _, out var format, out var nItems, out _, out var prop);
            if (rc == 0 && format == 8 && nItems > 0 && prop != nint.Zero)
            {
                try
                {
                    var bytes = new byte[(int)nItems];
                    Marshal.Copy(prop, bytes, 0, bytes.Length);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally { XFree(prop); }
            }

            if (XFetchName(dpy, window, out var name) != 0 && name != nint.Zero)
            {
                try { return Marshal.PtrToStringUTF8(name) ?? ""; }
                finally { XFree(name); }
            }
            return "";
        }
        catch { return ""; }
        finally { if (dpy != nint.Zero) XCloseDisplay(dpy); }
    }

    /// <summary>
    /// 距上次键鼠输入多久。XScreenSaver 扩展问 X server 要的，
    /// 范围是整个登录会话，跟 Win 的 GetLastInputInfo、macOS 的
    /// CGEventSourceSecondsSinceLastEventType 一致。读不出来返回零
    /// （<see cref="InputIdle"/> 的规矩：宁可少判一次离开）。
    /// </summary>
    public static TimeSpan IdleElapsed()
    {
        nint dpy = nint.Zero;
        try
        {
            dpy = XOpenDisplay(null);
            if (dpy == nint.Zero) return TimeSpan.Zero;
            var info = new XScreenSaverInfo();
            if (XScreenSaverQueryInfo(dpy, XDefaultRootWindow(dpy), ref info) == 0)
                return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds((double)info.Idle);
        }
        catch { return TimeSpan.Zero; }
        finally { if (dpy != nint.Zero) XCloseDisplay(dpy); }
    }
}
