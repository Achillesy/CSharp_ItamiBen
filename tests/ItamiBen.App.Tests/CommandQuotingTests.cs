using ItamiBen.App.Platform;

namespace ItamiBen.App.Tests;

/// <summary>
/// <see cref="Command"/> 必须把 `commands.md` 里那条命令**原样**交给 shell——
/// 尤其是命令自己带双引号的时候。
///
/// 这是 v3 在 2026-08-08 修过的那个真 bug 的回归防线（它的 L1）：当时 macOS 分支把命令
/// 拼成一整条 <c>-c "{cmd}"</c> 的 `Arguments` 字符串，而默认那几条命令全是
/// <c>osascript -e 'tell application "System Events" to ...'</c>——内层的双引号把外层
/// 引号提前闭合，.NET 再按 C 运行库规则切回 argv 时脚本就断了，sh 报 unexpected EOF。
///
/// ⚠️ **那个 bug 最阴的地方是闹钟那一侧全对**：到点了、也真调了、日志照常，
/// 只有命令自己安静地失败。所以这里断言的**不是「进程起来了」**——「调用到了」恰恰是
/// 当时唯一还正常的那一环，拿它当断言什么都测不出来。断言的是
/// **命令的副作用真的发生了，而且引号原封不动**。
///
/// ⚠️ **只喂无害命令**，绝不碰 `commands.md` 里真实那条：这台机器上
/// `executeCommand.macos[0]` 就是重启。所以测的是 <see cref="Command.RunDetached"/>
/// 而不是 <see cref="Command.LaunchDetached"/>——后者到点会现读 rules.json。
/// 两者走的是同一条起进程的代码，引号那段一个字节都没绕开。
/// </summary>
public class CommandQuotingTests
{
    /// <summary>命令里嵌的这段文字带一对双引号，正是当年被切碎的那个形状。</summary>
    private const string Quoted = "tell application \"System Events\" to restart";

    [Fact]
    public async Task 带双引号的命令原封不动地到达_shell()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"itamiben-cmd-{Guid.NewGuid():N}.txt");
        try
        {
            // 两个平台各写各的：Windows 走 cmd.exe 的 echo，Unix 走 sh 的 echo + 单引号。
            // 两条都把 Quoted 原样写进 marker 文件，除此之外什么都不做。
            var cmd = OperatingSystem.IsWindows()
                ? $"echo {Quoted}>\"{marker}\""
                : $"echo '{Quoted}' > \"{marker}\"";

            Command.RunDetached(cmd);

            // RunDetached **故意不 await**（真实场景里那条命令多半是关机），
            // 所以这里只能轮询等副作用落盘
            var text = await WaitForFile(marker, TimeSpan.FromSeconds(5));

            Assert.NotNull(text);
            // 关键断言：双引号必须**原样还在**。修复前这里拿到的是被 shell 拆碎的残句，
            // 或者根本没有文件——因为 sh 直接语法错误退出了
            Assert.Contains(Quoted, text);
        }
        finally
        {
            try { File.Delete(marker); } catch { /* 残留的临时文件不该让测试失败 */ }
        }
    }

    /// <summary>轮询等文件出现并且写完（非空），超时返回 null。</summary>
    private static async Task<string?> WaitForFile(string path, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    if (text.Trim().Length > 0) return text;
                }
            }
            catch (IOException) { /* 子进程还在写，下一轮再看 */ }
            await Task.Delay(100);
        }
        return null;
    }
}
