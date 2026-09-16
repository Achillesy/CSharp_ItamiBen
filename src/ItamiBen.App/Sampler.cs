using Avalonia.Threading;
using ItamiBen.App.Platform;

namespace ItamiBen.App;

/// <summary>
/// 一拍采样的结果——**一秒观测的全部内容**，原样写进库（DESIGN §9）。
/// <paramref name="Note"/> 只进日志。
/// </summary>
/// <param name="Idle">距上次键鼠输入多少秒。⚠️ **原始值**，不在这里算 afk（DECISIONS F2）。</param>
public readonly record struct Sample(DateTimeOffset At, string App, string Title, int Idle, string Note);

/// <summary>
/// **唯一那口钟。** 每一拍读一次前台窗口，交给 <see cref="Core.Round"/>。
///
/// ⚠️ **高频 tick + 哨兵，不是 `Interval = 1s`**（DESIGN §2.1.1，实测结论）：
/// 13 分钟里 `DispatcherTimer(1s)` **漏掉了 5 拍**，间隔规律得可怕（约 165 秒一次），
/// 典型的累积漂移——每拍比 1 秒多一点点，攒够一秒就整秒跳过去，而且**不报错**。
/// 现在一秒有十次机会，漂移吃不掉任何一秒；「这一秒采过没有」的哨兵在
/// <see cref="Core.Round.Observe"/> 里，重复的拍会被它挡掉。
///
/// ⚠️ **app 身份和标题分两条路，绝不合进一个循环**（DECISIONS B1）——这是 AW 的死因：
/// 标题卡住，连它已经拿到的 app 变化都吐不出来，实测哑了 402 秒。这里 app 走 UI 线程
/// （微秒级、零权限），标题走下面那个后台线程（带超时、尽力而为）。
/// </summary>
public sealed class Sampler : IDisposable
{
    /// <summary>一秒十次机会。读 app 身份是微秒级的，这个频率的开销可以忽略。</summary>
    private const int TickMs = 100;

    /// <summary>UI 线程告诉 worker「现在前台是这个」。</summary>
    private sealed record Wanted(string App, nint Handle);

    /// <summary>worker 读回来的标题，**带着它是从哪个 app 上读的**——B2 靠这个字段成立。</summary>
    private sealed record Got(string App, nint Handle, string Text, string Note);

    private readonly DispatcherTimer _timer;
    private readonly Thread _worker;
    private readonly CancellationTokenSource _stop = new();

    private Wanted? _want;
    private Got? _got;

    /// <summary>每一拍都触发（不是每秒一次）——去重是 <see cref="Core.Round"/> 的事。</summary>
    public event Action<Sample>? Ticked;

    public Sampler()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick += (_, _) => Tick();

        _worker = new Thread(TitleLoop)
        {
            IsBackground = true,
            Name = "ItamiBen title reader",
        };
    }

    public void Start()
    {
        _worker.Start();
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        ForegroundApp app;
        try { app = ForegroundWindow.ReadApp(); }
        catch (Exception e)
        {
            // 平台层抛了 ⇒ 这一秒什么都没读到。**不猜、不沿用上一次**
            Events.Error("sampler", "ReadApp failed", e);
            Ticked?.Invoke(new Sample(DateTimeOffset.Now, "", "", InputIdle.Seconds(), "ReadApp threw"));
            return;
        }

        Volatile.Write(ref _want, new Wanted(app.Name, app.Handle));

        // ⚠️ **app 变了而标题没跟上 ⇒ 当标题未知**（DECISIONS B2）。
        //    绝不沿用上一次的标题「凑合一下」——那正好是 v3 把全屏 mame 判成
        //    「学习经济学」的那个动作。代价是切窗口后的头一两拍标题是空的。
        var got = Volatile.Read(ref _got);
        var fresh = got is not null && got.Handle == app.Handle && got.App == app.Name;

        // ⚠️ 空闲也在 UI 线程读：它是纯查询（macOS 那条连辅助功能授权都不要），
        //    微秒级，没有挪到后台去的理由——真正需要隔离的只有标题那条同步 IPC
        Ticked?.Invoke(new Sample(
            DateTimeOffset.Now,
            app.Name,
            fresh ? got!.Text : "",
            InputIdle.Seconds(),
            fresh ? got!.Note : $"title not caught up ({app.Note})"));
    }

    /// <summary>
    /// 标题那条路，**单独一个线程**。
    ///
    /// 它卡住最多只是标题旧一点（下一拍 B2 会把它作废），**永远不会影响主钟**。
    /// AX 那边已经设了 250ms 超时（DECISIONS B3，廉价防御）——B5 实测推翻了
    /// 「AX 会被全屏应用卡死」，所以这里不需要更复杂的隔离，一个线程就够。
    /// </summary>
    private void TitleLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var want = Volatile.Read(ref _want);
                if (want is not null && want.Handle != 0)
                {
                    var t = ForegroundWindow.ReadTitle(want.Handle);
                    Volatile.Write(ref _got, new Got(want.App, want.Handle, t.Text, t.Note));
                }
            }
            catch (Exception e)
            {
                Events.Error("sampler", "ReadTitle failed", e);
            }

            try { _stop.Token.WaitHandle.WaitOne(TickMs); }
            catch (ObjectDisposedException) { return; }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _stop.Cancel();
        _worker.Join(TimeSpan.FromSeconds(1));
        _stop.Dispose();
    }
}
