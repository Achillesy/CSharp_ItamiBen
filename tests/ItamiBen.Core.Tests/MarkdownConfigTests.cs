using Xunit;

namespace ItamiBen.Core.Tests;

public class MarkdownConfigTests
{
    [Fact]
    public void 说明里的示例代码块不会被当成配置()
    {
        // ⚠️ 这一条是整个标记方案存在的理由：说明部分**一定**会带示例代码块
        var md = """
        # 规则

        比如这样写一条：

        ```json
        { "App": "^假的$" }
        ```

        下面才是真的：

        ```json itamiben
        { "App": "^真的$" }
        ```
        """;

        Assert.Contains("真的", MarkdownConfig.Extract(md));
        Assert.DoesNotContain("假的", MarkdownConfig.Extract(md));
    }

    [Fact]
    public void 语言在前标记在后_编辑器的高亮照常()
    {
        Assert.Equal("0 9 * * 1   海贼王",
            MarkdownConfig.Extract("```cron itamiben\n0 9 * * 1   海贼王\n```\n"));
    }

    [Fact]
    public void 一个都没有就抛_而且说清楚是怎么回事()
    {
        var e = Assert.Throws<InvalidDataException>(
            () => MarkdownConfig.Extract("# 只有标题\n\n```json\n{}\n```\n"));
        Assert.Contains("missing", e.Message);
    }

    [Fact]
    public void 有两个也抛_不猜哪个才是真的()
    {
        var md = "```json itamiben\n{}\n```\n\n```json itamiben\n{}\n```\n";
        Assert.Contains("more than one", Assert.Throws<InvalidDataException>(
            () => MarkdownConfig.Extract(md)).Message);
    }

    [Fact]
    public void 围栏没收尾也算没有_不把剩下半个文件当配置()
    {
        Assert.Throws<InvalidDataException>(
            () => MarkdownConfig.Extract("```json itamiben\n{ \"a\": 1 }\n"));
    }

    [Fact]
    public void 标记必须是独立的词_itamiben_old_不算()
    {
        Assert.Throws<InvalidDataException>(
            () => MarkdownConfig.Extract("```json itamiben-old\n{}\n```\n"));
    }

    [Fact]
    public void 配置块里的空行和缩进原样保留()
    {
        var md = "```json itamiben\n{\n  \"a\": 1\n\n  \n}\n```\n";
        Assert.Equal("{\n  \"a\": 1\n\n  \n}", MarkdownConfig.Extract(md));
    }
}
