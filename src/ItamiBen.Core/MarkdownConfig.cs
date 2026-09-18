namespace ItamiBen.Core;

/// <summary>
/// 从一份配置 `.md` 里抠出真正的配置块。
///
/// 每份配置文件都是 Markdown，三段式：**给人的一段话 → 给 AI 的详细规矩 →
/// 标记好的配置块**。这样把**单个文件**扔给任何 AI（本地智能体或网页对话），
/// 它就拿到了完整上下文——不需要再配一份 `AGENT.md`。
///
/// ⚠️ **这是在还一笔旧账。** 说明和它描述的数据分居两个文件，就是
/// 「一件事写了两份、靠人记得同步」——这个项目为这个形状栽过四次
/// （I17 设置 / I22 目标列表 / I25 窗口标题 / I26 退出记录）。写在同一个文件里，
/// 它们被同一次复制、同一次覆盖，**结构上漂不了**。
///
/// ⚠️ **不能用「第一个/最后一个代码块」当规则**：说明部分本身就带示例代码块
/// （教 AI 怎么写一条规则）。所以真配置靠围栏信息串里那个 <c>itamiben</c> 认，
/// 而且**全文件有且只有一个**——零个或多个都当错处理，让它响，别猜。
///
/// <code>
/// ```json itamiben
/// { "Groups": { ... } }
/// ```
/// </code>
///
/// 信息串第一个词仍然是语言（`json` / `cron`），所以编辑器的语法高亮照常。
/// </summary>
public static class MarkdownConfig
{
    private const string Marker = "itamiben";

    /// <summary>
    /// 抠出那唯一一个配置块的正文。找不到或找到多个都抛——
    /// **调用方据此退回上一份能用的配置**，而不是拿半份配置往下跑。
    /// </summary>
    public static string Extract(string markdown)
    {
        string? found = null;
        var body = new List<string>();
        var fence = 0;          // >0 = 正在一个围栏里；值是开围栏的反引号个数
        var keeping = false;

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var ticks = Backticks(line);

            if (fence == 0)
            {
                if (ticks < 3) continue;
                fence = ticks;
                keeping = HasMarker(line[ticks..]);
                body.Clear();
                continue;
            }

            // 收围栏：同样多（或更多）的反引号，且这一行没有别的东西
            if (ticks >= fence && line.Trim().Length == ticks)
            {
                if (keeping)
                {
                    if (found is not null)
                        throw new InvalidDataException(
                            $"more than one `{Marker}` block — there must be exactly one");
                    found = string.Join('\n', body);
                }
                fence = 0;
                keeping = false;
                continue;
            }

            if (keeping) body.Add(line);
        }

        return found ?? throw new InvalidDataException(
            $"no ```…{Marker} block found — the configuration block is missing or its fence is not closed");
    }

    private static int Backticks(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == '`') n++;
        return n;
    }

    /// <summary>信息串里有没有 <c>itamiben</c> 这个**词**（不是子串：`itamiben-old` 不算）。</summary>
    private static bool HasMarker(string info)
    {
        foreach (var word in info.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
            if (word.Equals(Marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
