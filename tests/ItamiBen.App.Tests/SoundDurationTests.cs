using System.Text;
using ItamiBen.App.Platform;

namespace ItamiBen.App.Tests;

/// <summary>
/// 音频文件头解析。字节在内存里拼，**不碰磁盘、不依赖这台机器装了哪些系统音**。
///
/// 值得单独测是因为它的失败方式很阴：算错了不会抛异常，只会让连响的间隔不对——
/// 四遍变成一次结巴，而这在单元测试之外基本看不出来。
/// </summary>
public class SoundDurationTests
{
    private static TimeSpan? Of(byte[] bytes) => Sound.Duration(new MemoryStream(bytes));

    [Fact]
    public void wav的时长是数据字节数除以每秒字节数()
    {
        // 44100 × 2 声道 × 2 字节 = 176400 字节/秒；264600 字节 ⇒ 1.5 秒
        var d = Of(Wav(44100, 2, 16, dataBytes: 264600));
        Assert.NotNull(d);
        Assert.Equal(1.5, d!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void data前面有别的块也照样解析出来()
    {
        // ⚠️ C:\Windows\Media 里真有 LIST 块排在 data 前面的 wav——
        //    所以解析必须**走**块，不能读固定偏移
        var d = Of(Wav(44100, 2, 16, dataBytes: 176400, junkBefore: 13));
        Assert.NotNull(d);
        Assert.Equal(1.0, d!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void 每秒字节数为零时算不出来而不是除零()
        => Assert.Null(Of(Wav(0, 1, 16, dataBytes: 1000)));

    [Fact]
    public void aiff的时长是帧数除以采样率()
    {
        var d = Of(Aiff(44100, frames: 22050));
        Assert.NotNull(d);
        Assert.Equal(0.5, d!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(22050)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void 采样率是从八十位扩展浮点里拆出来的(double rate)
    {
        // 80 位扩展浮点没有对应的 .NET 类型，是手拆的：15 位指数 + **64 位显式尾数**
        var d = Of(Aiff(rate, frames: (uint)rate));
        Assert.NotNull(d);
        Assert.Equal(1.0, d!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void 根本不是音频文件时算不出来()
        => Assert.Null(Of(Encoding.ASCII.GetBytes("this is not audio at all, not even close")));

    [Fact]
    public void 截断的文件头算不出来()
    {
        var full = Wav(44100, 2, 16, dataBytes: 176400);
        Assert.Null(Of(full[..20]));
    }

    [Fact]
    public void 文件不存在时算不出来而不是抛异常()
        => Assert.Null(Sound.Duration(Path.Combine(Path.GetTempPath(), "itamiben-no-such-file.wav")));

    // ── 拼字节 ───────────────────────────────────────────────────────────

    private static byte[] Wav(int sampleRate, short channels, short bits, int dataBytes, int junkBefore = 0)
    {
        var byteRate = sampleRate * channels * bits / 8;
        var m = new MemoryStream();
        var w = new BinaryWriter(m);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(0);                                   // 容器长度：故意写 0，解析器不该依赖它
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        if (junkBefore > 0)
        {
            w.Write(Encoding.ASCII.GetBytes("LIST"));
            w.Write(junkBefore);
            w.Write(new byte[junkBefore + (junkBefore & 1)]);   // 补齐到偶数长度
        }

        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);

        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        return m.ToArray();                           // 不写实际音频数据：解析只看 size
    }

    private static byte[] Aiff(double sampleRate, uint frames)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);

        w.Write(Encoding.ASCII.GetBytes("FORM"));
        w.Write(0);
        w.Write(Encoding.ASCII.GetBytes("AIFF"));

        w.Write(Encoding.ASCII.GetBytes("COMM"));
        w.Write(Be(18));
        w.Write(Be((short)2));
        w.Write(Be((int)frames));
        w.Write(Be((short)16));
        w.Write(Ext80(sampleRate));
        return m.ToArray();
    }

    private static byte[] Be(int v) => [.. BitConverter.GetBytes(v).Reverse()];
    private static byte[] Be(short v) => [.. BitConverter.GetBytes(v).Reverse()];

    /// <summary>把一个正数打包成 80 位扩展浮点——<c>Extended80</c> 的逆运算。</summary>
    private static byte[] Ext80(double v)
    {
        var exponent = 0;
        var mantissa = v;
        while (mantissa >= 1)  { mantissa /= 2; exponent++; }
        while (mantissa < 0.5 && mantissa > 0) { mantissa *= 2; exponent--; }

        var bits = (ulong)(mantissa * Math.Pow(2, 64));
        var e = (ushort)(exponent + 16383 - 1);
        return [(byte)(e >> 8), (byte)e, .. BitConverter.GetBytes(bits).Reverse()];
    }
}
