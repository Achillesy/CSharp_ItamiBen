using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>
/// 闹钟的边界。全是纯函数 / 纯状态，now 是参数，所以不用等真实时间。
/// 用例从 v3 搬过来——那几个例子是用户 2026-07-30 亲口给的。
/// </summary>
public class AlarmClockTests
{
    private static DateTime At(int h, int m, int s = 0) => new(2026, 9, 16, h, m, s);

    /// <summary>把黄针拨到表盘时刻 h:m 上（从 12 点起顺时针数分钟）。</summary>
    private static AlarmClock HandAt(int h, int m, DateTime now)
    {
        var a = new AlarmClock();
        a.Bump(h % 12 * 60 + m, now);
        return a;
    }

    // ── NextRing：用户给的三个例子 ────────────────────────────────────────

    [Fact]
    public void 现在二十点零五黄针指九点零五今晚二十一点零五响()
        => Assert.Equal(At(21, 5), HandAt(9, 5, At(20, 5)).FireAt);

    [Fact]
    public void 现在二十点零五黄针指两点零五下午那档已经过了等明天凌晨两点零五()
        => Assert.Equal(At(2, 5).AddDays(1), HandAt(2, 5, At(20, 5)).FireAt);

    [Fact]
    public void 现在八点零五黄针指两点零五上午那档已经过了十四点零五响()
        => Assert.Equal(At(14, 5), HandAt(2, 5, At(8, 5)).FireAt);

    // ── 严格小于：正好撞上意味着 12 小时后，不是「就是现在」（v3 的 E2）──────

    [Fact]
    public void 正好撞上时针意味着十二小时后而不是当场响()
        => Assert.Equal(At(21, 5), HandAt(9, 5, At(9, 5)).FireAt);

    [Fact]
    public void 差一秒仍然算没到今天就响()
    {
        // 08:59:59 拨到 09:00 ⇒ 一秒后响，而不是跳到 21:00
        Assert.Equal(At(9, 0), HandAt(9, 0, At(8, 59, 59)).FireAt);
    }

    [Fact]
    public void 响铃时刻永远严格晚于拨针那一刻而且不超过二十四小时()
    {
        // 144 个位置 × 一天里每 7 分钟一个 now，全查一遍
        for (var slot = 0; slot < 720; slot += 5)
            for (var minute = 0; minute < 1440; minute += 7)
            {
                var now = At(0, 0).AddMinutes(minute);
                var fire = AlarmClock.NextRing(now, slot);
                Assert.True(fire > now, $"slot={slot} now={now:HH:mm} fire={fire:HH:mm}");
                Assert.True(fire - now <= TimeSpan.FromHours(24), $"slot={slot} now={now:HH:mm}");
            }
    }

    [Fact]
    public void 黄针停在十二点深夜拨的话在凌晨响()
    {
        // 黄针 0 分 = 表盘 12 点 = 表盘时刻 00:00。23:30 时今天的 00:00 和 12:00 都过了 ⇒ 明天 00:00
        Assert.Equal(At(0, 0).AddDays(1), AlarmClock.NextRing(At(23, 30), 0));
    }

    // ── 拨针 / 到点 / 响过 ────────────────────────────────────────────────

    [Fact]
    public void 顺时针拨过十二点会绕回来不会溢出()
    {
        var a = HandAt(11, 59, At(10, 0));
        Assert.Equal(719, a.Position);
        a.Bump(AlarmClock.SlotMinutes, At(10, 0));
        Assert.Equal(0, a.Position);
    }

    [Fact]
    public void 逆时针拨过十二点会绕到另一头永远不会变成负数()
    {
        var a = new AlarmClock();
        a.Bump(-AlarmClock.SlotMinutes, At(10, 0));
        Assert.Equal(719, a.Position);
    }

    [Fact]
    public void 拨出去再拨回来还是同一个响铃时刻()
    {
        var now = At(10, 0);
        var a = HandAt(3, 0, now);
        var before = a.FireAt;
        a.Bump(37, now);
        a.Bump(-37, now);
        Assert.Equal(before, a.FireAt);
    }

    [Fact]
    public void 从没拨过针就永远不响而且黄针停在十二点()
    {
        var a = new AlarmClock();
        Assert.Equal(0, a.Position);
        Assert.Null(a.FireAt);
        Assert.False(a.ShouldFire(At(23, 59)));
    }

    [Fact]
    public void 到点响一次就完不是每日闹钟()
    {
        var a = HandAt(0, 5, At(8, 58));      // 表盘 00:05，08:58 ⇒ 今天 12:05
        Assert.Equal(At(12, 5), a.FireAt);

        Assert.False(a.ShouldFire(At(12, 4, 59)));
        Assert.True(a.ShouldFire(At(12, 5)));

        a.MarkFired();
        Assert.False(a.ShouldFire(At(12, 5)));
        Assert.False(a.ShouldFire(At(12, 5).AddDays(1)));   // 明天这个点也不再响
        Assert.Equal(5, a.Position);                        // 时刻留着——黄针位置靠它
    }

    // ── 恢复：只存一个时刻，黄针位置是推导出来的 ──────────────────────────

    [Fact]
    public void 恢复之后黄针看得见但不响除非重新拨过()
    {
        var a = new AlarmClock();
        a.Restore(At(21, 5));

        Assert.Equal(21 % 12 * 60 + 5, a.Position);    // 21:05 ⇒ 表盘 9:05 的位置
        Assert.False(a.ShouldFire(At(21, 5)));         // 没激活，不响

        a.Bump(0, At(20, 0));                          // 拨一下（哪怕 0 格）才算数
        Assert.True(a.ShouldFire(At(21, 5)));
    }

    [Fact]
    public void 过期的时刻不补响()
    {
        var a = new AlarmClock();
        a.Restore(At(9, 5));
        Assert.False(a.ShouldFire(At(20, 0)));
    }

    [Fact]
    public void 从恢复的残影上接着拨会从残影的位置起算()
    {
        var a = new AlarmClock();
        a.Restore(At(9, 5));                  // 过期残影停在 545
        a.Bump(5, At(20, 0));                 // 拨到 550 = 表盘 9:10
        Assert.Equal(At(21, 10), a.FireAt);   // 20:00 < 21:10
    }
}
