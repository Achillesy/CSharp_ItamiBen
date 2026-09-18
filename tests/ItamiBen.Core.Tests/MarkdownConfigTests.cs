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

public class MarkdownReplaceTests
{
    private const string Md = """
        # 标题

        示例：

        ```json
        { "不要动我": 1 }
        ```

        ## 配置

        ```json itamiben
        { "老的": 1 }
        ```

        结尾的话。
        """;

    [Fact]
    public void 只换配置块_说明部分一字不动()
    {
        var after = MarkdownConfig.Replace(Md, """{ "新的": 2 }""");

        Assert.Contains("不要动我", after);      // 示例块没被碰
        Assert.Contains("结尾的话", after);      // 块后面的话还在
        Assert.Contains("# 标题", after);
        Assert.DoesNotContain("老的", after);
    }

    [Fact]
    public void 换完还能被自己读回来_往返闭合()
    {
        // ⚠️ 这一条守的是 Replace 和 Extract 用同一套围栏判定
        var body = "{\n  \"a\": 1\n}";
        Assert.Equal(body, MarkdownConfig.Extract(MarkdownConfig.Replace(Md, body)));
    }

    [Fact]
    public void 没有配置块的文件_换不了就抛()
        => Assert.Throws<InvalidDataException>(
               () => MarkdownConfig.Replace("# 只有标题\n", "{}"));
}
