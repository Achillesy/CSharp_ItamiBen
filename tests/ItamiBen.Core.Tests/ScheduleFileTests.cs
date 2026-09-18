using Xunit;

namespace ItamiBen.Core.Tests;

/// <summary>
/// `schedule.cron` 行尾那个 `!命令` 的解析（2026-09-18 定的格式）。
///
/// ⚠️ 这一组守的是**四条互相牵制的规矩**，任何一条单独看都像小事：
/// 命令放行尾、提醒文字必填、文字里的 `!` 不算数、命令名不在这里校验。
/// </summary>
public class ScheduleFileTests
{
    [Fact]
    public void 行尾的命令被切出来_文字留在前面()
    {
        var f = AlarmsList.Read("0 23 * * *   该睡了 !sleep\n");

        var e = Assert.Single(f.Entries);
        Assert.Equal("该睡了", e.Text);
        Assert.Equal("sleep", e.Run);
        Assert.Empty(f.Skipped);
    }

    [Fact]
    public void 没有命令的普通行照旧_Run_是_null()
    {
        var e = Assert.Single(AlarmsList.Read("0 9 * * 1   海贼王\n").Entries);
        Assert.Equal("海贼王", e.Text);
        Assert.Null(e.Run);
    }

    [Fact]
    public void 只有命令没有文字的行不合法_跳过并说清楚为什么()
    {
        // ⚠️ 这条是整个格式的核心：它让「机器自己做了事却没说为什么」写不出来
        var f = AlarmsList.Read("0 23 * * *   !sleep\n");

        Assert.Empty(f.Entries);
        var why = Assert.Single(f.Skipped);
        Assert.Contains("line 1", why);
        Assert.Contains("needs reminder text", why);
    }

    [Fact]
    public void 提醒文字里的叹号不在词首_就不是命令()
    {
        // `快去做作业!` —— 叹号在词尾，整句都是文字
        var e = Assert.Single(AlarmsList.Read("0 9 * * 1   快去做作业!\n").Entries);
        Assert.Equal("快去做作业!", e.Text);
        Assert.Null(e.Run);
    }

    [Fact]
    public void 命令名的大小写不在这里校验_留到到点那一刻去失败()
    {
        // ⚠️ 在这里拦下来的话，一个大小写错误会把整行**连同提醒一起**吞掉。
        //    交出去，到点时因「没有这个名字」失败并记日志，而提醒照常弹。
        var e = Assert.Single(AlarmsList.Read("0 23 * * *   该睡了 !Sleep\n").Entries);
        Assert.Equal("Sleep", e.Run);
    }

    [Fact]
    public void 注释和空行不算读不懂_不进跳过清单()
    {
        // crontab 里「这条先别响」的惯用法就是注释掉，它不是错误
        var f = AlarmsList.Read("# 例子\n\n#0 23 * * *   该睡了 !sleep\n");

        Assert.Empty(f.Entries);
        Assert.Empty(f.Skipped);
    }

    [Fact]
    public void 写坏的表达式记一条跳过_并且不拖累同一份文件里其余的行()
    {
        var f = AlarmsList.Read("99 99 * * *  坏的\n0 9 * * 1   海贼王\n");

        Assert.Single(f.Entries);
        Assert.Contains("line 1", Assert.Single(f.Skipped));
    }

    [Fact]
    public void 出厂默认的那份_一条都不生效_但也一条都不报错()
    {
        var f = AlarmsList.Read(
            MarkdownConfig.Extract(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "defaults", "schedule.sample.md"))));

        Assert.Empty(f.Entries);    // 样例全是注释掉的：样例不该有副作用
        Assert.Empty(f.Skipped);    // 而且不该在 error.log 里留噪音
    }
}
