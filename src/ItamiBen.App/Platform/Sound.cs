using System.Runtime.InteropServices;
using System.Text;

namespace ItamiBen.App.Platform;

/// <summary>
/// 提示音。**只用系统自带的声音**（用户 2026-07-28 定的规矩，v3 原样沿用）：
/// 一个音频文件都不打包——跟钟面不用位图是同一条。
///
/// 平台差异只有两处，**都收口在这一个文件里**：去哪儿找，和用什么放。
///
/// <code>
///            音库                                 播放
/// Windows    C:\Windows\Media\*.wav              winmm 的 PlaySound
/// macOS      ~/Library/Sounds
///            /Library/Sounds
///            /System/Library/Sounds  \*.aiff     AudioToolbox（见 MacAudio）
/// </code>
///
/// macOS 那三个目录是**从高到低的优先级**——这是系统自己的约定，`~/Library/Sounds`
/// 里的同名文件会盖掉系统那份。所以枚举时要去重，先到先得。
/// </summary>
public static class Sound
{
    private const string WindowsDir = @"C:\Windows\Media";
    private const string WindowsExt = ".wav";

    private static readonly string[] MacDirs =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Sounds"),
        "/Library/Sounds",
        "/System/Library/Sounds",
    ];
    private const string MacExt = ".aiff";

    /// <summary>读不出文件头时用的间隔。比两个平台上任何一个系统音都长，所以最坏是节奏松一点，绝不会截断。</summary>
    private static readonly TimeSpan FallbackGap = TimeSpan.FromSeconds(3);

    /// <summary>在量出来的长度上再加一点静音：定时器抖动是双向的，晚 120ms 听不出来，早 20ms 在 Windows 上就会把尾巴削掉。</summary>
    private static readonly TimeSpan Cushion = TimeSpan.FromMilliseconds(120);

    private const int SndAsync = 0x0001;       // 立刻返回，别阻塞 UI
    private const int SndFilename = 0x00020000;
    private const int SndNoDefault = 0x0002;   // 找不到就不出声，别退回去发一个通用的「叮」

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? name, IntPtr mod, int flags);

    private static (string[] Dirs, string Ext) Library => OperatingSystem.IsWindows()
        ? ([WindowsDir], WindowsExt)
        : (MacDirs, MacExt);

    /// <summary>可以挑的系统音，按名字排序。名字就是去掉扩展名的文件名。</summary>
    public static IReadOnlyList<string> Available()
    {
        var (dirs, ext) = Library;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*" + ext))
                    if (Path.GetFileNameWithoutExtension(f) is { Length: > 0 } n)
                        names.Add(n);   // 高优先级目录先走，同名的后来者自然进不来
            }
            catch (Exception e)
            {
                Log.Error($"Failed to enumerate system sounds in {dir}", e);
            }
        }

        return [.. names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>名字 → 完整路径，按优先级取第一个存在的；都不在就是 null。</summary>
    private static string? Resolve(string name)
    {
        var (dirs, ext) = Library;
        foreach (var dir in dirs)
        {
            var p = Path.Combine(dir, name + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// 播一个。名字是空的、文件找不到、播放失败——**一律安静地失败**。
    /// 提示音绝不能把程序搞崩，跟日志同一条原则。
    /// </summary>
    public static void Play(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Resolve(name) is not { } path) { Log.Warn($"Sound file not found: {name}"); return; }
        PlayFile(path);
    }

    /// <summary>
    /// 连着播 <paramref name="times"/> 遍。
    ///
    /// ⚠️ **间隔必须是文件自己的长度**，<see cref="Duration"/> 存在的唯一理由就是它：
    /// winmm 的 `PlaySound` 只有一个通道，第二次调用早到了不会叠上去，而是**把第一遍
    /// 从中间掐断**，四遍变成一次结巴；macOS 那边则是叠着一起响。两种都不叫「响了四遍」。
    ///
    /// 读不出文件头就退回 <see cref="FallbackGap"/>：差一点只是节奏怪，这里出错**不会
    /// 让闹钟哑掉**。
    ///
    /// **不加锁，也没有东西可锁**（v3 的 E12，2026-08-03 当场定死）：这条路只碰局部变量，
    /// 音频层唯一的共享可变状态是 <see cref="MacAudio"/> 的 SoundID 缓存，它自己有 Gate。
    /// 加锁真正能买到的是「声音不重叠」，那是策略不是正确性——代价只能是**丢**一声或者
    /// **排队**（迟到，还要维护跨轮次、跨关窗的状态）。
    /// </summary>
    public static void Repeat(string? name, int times)
    {
        if (string.IsNullOrWhiteSpace(name) || times <= 0) return;
        if (Resolve(name) is not { } path) { Log.Warn($"Sound file not found: {name}"); return; }
        if (times == 1) { PlayFile(path); return; }

        var gap = (Duration(path) ?? FallbackGap) + Cushion;
        _ = RingLoop(path, times, gap);
    }

    /// <summary>
    /// 连响的循环。**故意不让调用方 await**：闹钟判断挂在采样节拍上，
    /// 为了四声把它堵住会让秒针停住。这里不碰 UI，所以也不需要 dispatcher。
    /// </summary>
    private static async Task RingLoop(string path, int times, TimeSpan gap)
    {
        try
        {
            for (var i = 0; i < times; i++)
            {
                if (i > 0) await Task.Delay(gap).ConfigureAwait(false);
                PlayFile(path);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to repeat sound: {path}", e);
        }
    }

    /// <summary>真正出声的唯一一处。失败是安静的——理由见 <see cref="Play"/>。</summary>
    private static void PlayFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                PlaySound(path, IntPtr.Zero, SndAsync | SndFilename | SndNoDefault);
            else if (OperatingSystem.IsMacOS())
                MacAudio.Play(path);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to play sound: {path}", e);
        }
    }

    /// <summary>
    /// 一个音频文件能放多久，**从文件头算出来**——不解码、不播放、不引第三方库。
    ///
    /// 只处理这个程序会枚举到的两种格式，每种只有一个形状值得知道：
    ///
    /// <code>
    /// .wav  (RIFF, 小端)   fmt   byteRate            时长 = dataBytes / byteRate
    /// .aiff (FORM, 大端)   COMM  frames, sampleRate  时长 = frames / sampleRate
    /// </code>
    ///
    /// ⚠️ 两种格式都是分块的，**块的顺序任意**、长度补齐到偶数——所以这里是**走**块，
    /// 不是读固定偏移。`C:\Windows\Media` 里确实有 `LIST` 块排在 `data` 前面的 wav。
    ///
    /// 认不出来的一律返回 null（byteRate 为 0 的压缩 wav、AIFC、截断的文件、读不了的
    /// 文件）。调用方会退回默认间隔；它**从不抛异常**。
    /// </summary>
    public static TimeSpan? Duration(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            return Duration(s);
        }
        catch (Exception e)
        {
            Log.Warn($"Could not read the length of {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// <see cref="Duration(string)"/> 的纯计算那一半——**拆出来是为了可测**
    /// （`SoundDurationTests` 在内存里拼字节，不碰磁盘、不依赖哪台机器装了什么系统音）。
    /// </summary>
    public static TimeSpan? Duration(Stream s)
    {
        try
        {
            using var r = new BinaryReader(s, Encoding.Latin1, leaveOpen: true);
            var form = Id(r);
            r.ReadInt32();                              // 容器长度——野外的文件经常不可靠，而且用不着
            var kind = Id(r);

            if (form == "RIFF" && kind == "WAVE") return Wave(r, s);
            if (form == "FORM" && kind == "AIFF") return Aiff(r, s);
            return null;
        }
        catch (Exception e)
        {
            Log.Warn($"Could not parse an audio header: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 四个字符的块 id。按**原始字节**读，不用 <c>BinaryReader.ReadChars</c>：
    /// 后者走 UTF-8 解码，一个 0x7F 以上的字节会把后面的字节吞掉，**悄悄**把之后每一个
    /// 块的偏移都挪位。按规范 id 是 ASCII，所以这只在坏文件上才有区别——而在那里，
    /// 它把「失步」变成了「返回 null」。
    /// </summary>
    private static string Id(BinaryReader r) => Encoding.Latin1.GetString(r.ReadBytes(4));

    /// <summary>RIFF/WAVE：`fmt ` 的 byteRate 加上 `data` 的长度。两个块都要，顺序任意都合法。</summary>
    private static TimeSpan? Wave(BinaryReader r, Stream s)
    {
        int byteRate = 0, dataBytes = 0;

        while (s.Position + 8 <= s.Length)
        {
            var id = Id(r);
            var size = r.ReadInt32();
            if (size < 0) return null;
            var next = s.Position + size + (size & 1);   // 块补齐到偶数长度，补的那一字节不算在 size 里

            if (id == "fmt " && size >= 16)
            {
                r.ReadInt16();                          // format tag
                r.ReadInt16();                          // 声道数
                r.ReadInt32();                          // 采样率
                byteRate = r.ReadInt32();               // 每秒字节数——正好就是要的那个数，不用自己算
            }
            else if (id == "data")
            {
                dataBytes = size;
            }

            if (byteRate > 0 && dataBytes > 0) return TimeSpan.FromSeconds((double)dataBytes / byteRate);
            if (next > s.Length) break;
            s.Position = next;
        }

        return null;
    }

    /// <summary>AIFF：`COMM` 的帧数 / 采样率。全程大端，采样率是 80 位扩展浮点（见 <see cref="Extended80"/>）。</summary>
    private static TimeSpan? Aiff(BinaryReader r, Stream s)
    {
        while (s.Position + 8 <= s.Length)
        {
            var id = Id(r);
            var size = BitConverter.ToInt32([.. r.ReadBytes(4).Reverse()]);
            if (size < 0) return null;
            var next = s.Position + size + (size & 1);

            if (id == "COMM" && size >= 18)
            {
                r.ReadBytes(2);                                         // 声道数
                var frames = BitConverter.ToUInt32([.. r.ReadBytes(4).Reverse()]);
                r.ReadBytes(2);                                         // 位深
                var rate = Extended80(r.ReadBytes(10));
                return rate > 0 ? TimeSpan.FromSeconds(frames / rate) : null;
            }

            if (next > s.Length) break;
            s.Position = next;
        }

        return null;
    }

    /// <summary>
    /// AIFF 用来存采样率的 80 位 IEEE 754 扩展浮点——.NET 里没有对应类型，只能手拆：
    /// 1 位符号、15 位指数（偏移 16383），然后是**64 位显式尾数**（跟 float/double 不同，
    /// 前导 1 不是隐含的）。这里恒为正，符号位直接掩掉。
    /// </summary>
    private static double Extended80(byte[] b)
    {
        if (b.Length < 10) return 0;
        var exponent = ((b[0] & 0x7F) << 8) | b[1];
        var mantissa = BitConverter.ToUInt64([.. b[2..10].Reverse()]);
        if (exponent == 0 && mantissa == 0) return 0;
        return Math.ScaleB((double)mantissa, exponent - 16383 - 63);
    }

    /// <summary>按偏好挑第一个这台机器真的装了的；都没有就退回音库里的第一个。</summary>
    public static string? PreferredOrFirst(params string[] wanted)
    {
        var all = Available();
        foreach (var w in wanted)
            if (all.Contains(w, StringComparer.OrdinalIgnoreCase)) return w;
        return all.Count > 0 ? all[0] : null;
    }
}
