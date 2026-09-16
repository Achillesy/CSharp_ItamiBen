using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ItamiBen.App.Platform;

/// <summary>
/// macOS 这边的播放底座，对应 Windows 那边的 winmm `PlaySound`。
/// 从 v3 搬过来（DESIGN §6），跟 AW 无关。
///
/// **为什么用 AudioToolbox 而不是 `afplay`**：起子进程播一次声音，光 afplay 自己的
/// 启动延迟就有几十毫秒。AudioServices 是进程内调用，SystemSoundID 建一次留着，
/// 播放就是一行。
///
/// **不引任何 NuGet 音频包**：AudioToolbox 是系统自带的框架，跟 Windows 那边只用
/// winmm 是同一条规矩——钟面不用位图，声音不打包 wav，播放不引第三方。
///
/// v3 实测（macOS 26.5.2 / arm64）：`AudioServicesCreateSystemSoundID` 返回 0，正常出声。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacAudio
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFURLCreateFromFileSystemRepresentation(
        IntPtr allocator, byte[] path, nint length, [MarshalAs(UnmanagedType.I1)] bool isDirectory);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(AudioToolbox)]
    private static extern int AudioServicesCreateSystemSoundID(IntPtr fileUrl, out uint soundId);

    [DllImport(AudioToolbox)]
    private static extern void AudioServicesPlaySystemSound(uint soundId);

    [DllImport(AudioToolbox)]
    private static extern int AudioServicesDisposeSystemSoundID(uint soundId);

    /// <summary>
    /// 路径 → SystemSoundID。**建一次留着**：创建那一步会把整个文件读进来解码，
    /// 每次重建就是每次读盘。可选的系统音只有十几个，这个缓存不会长大。
    ///
    /// ⚠️ 2026-09-16 搬滴答声时把 <see cref="Forget"/> 补回来了——那时才有了调用方。
    /// 这正是「有调用方才搬」那条纪律该有的用法（DECISIONS E5）。
    /// </summary>
    private static readonly Dictionary<string, uint> Ids = [];
    private static readonly Lock Gate = new();

    /// <summary>
    /// 播一个。**播不出来就安静地不播**——跟 winmm 那边的 `SND_NODEFAULT` 同一个意思：
    /// 找不到文件宁可没声音，也别退回去发一个不知所谓的系统「叮」。
    /// </summary>
    public static void Play(string path)
    {
        try
        {
            uint id;
            lock (Gate)
            {
                if (!Ids.TryGetValue(path, out id))
                {
                    id = Create(path);
                    if (id == 0) return;
                    Ids[path] = id;
                }
            }
            AudioServicesPlaySystemSound(id);
        }
        catch (Exception e)
        {
            Events.Error("sound", $"Playback failed: {path}", e);
        }
    }

    /// <summary>
    /// 忘掉某个文件已经建好的 SoundID。
    ///
    /// ⚠️ **音量一变就必须调它**：SystemSoundID 在**创建那一刻**就把音频数据吃进去了，
    /// 之后覆盖同名文件毫无效果——症状是拖音量滑块什么都不发生，永远按老音量响。
    /// </summary>
    public static void Forget(string path)
    {
        lock (Gate)
        {
            if (!Ids.Remove(path, out var id)) return;
            try { AudioServicesDisposeSystemSoundID(id); } catch { /* 释放失败无所谓 */ }
        }
    }

    private static uint Create(string path)
    {
        // CFURL 要的是文件系统表示（UTF-8 字节，不需要结尾 0，长度另外传）
        var bytes = Encoding.UTF8.GetBytes(path);
        var url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, bytes, bytes.Length, false);
        if (url == IntPtr.Zero) { Events.Warn("sound", $"Could not create CFURL for {path}"); return 0; }

        try
        {
            var status = AudioServicesCreateSystemSoundID(url, out var id);
            if (status != 0) { Events.Warn("sound", $"Could not create SystemSoundID (status {status}) for {path}"); return 0; }
            return id;
        }
        finally
        {
            CFRelease(url);
        }
    }
}
