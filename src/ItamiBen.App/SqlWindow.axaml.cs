using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 「在线修改配置」——**给没有装智能体的用户用的那条路**。
///
/// 配置住在库里、由智能体改（DECISIONS I15），但不是每个人手边都有一个能操作文件的
/// 智能体。这扇窗把流程压成四步，全程不需要装任何东西：
///
/// <list type="number">
///   <item>显示当前配置（**可编辑**——不想外传的行自己删掉）；</item>
///   <item>写一句你想要什么；</item>
///   <item>Copy all ⇒ 剪贴板里是 `AGENT.md` + 当前配置 + 你的需求，
///   **粘一次就够**，不用上传任何文件；</item>
///   <item>把网页 AI 给的 SQL 贴回来，Apply。</item>
/// </list>
///
/// ⚠️ **不违反 DECISIONS A3（没有 CLI）**：那条禁的是 v3 那种独立的 `itami` 工具。
/// 这是设置里的一扇窗。
/// </summary>
public partial class SqlWindow : Window
{
    /// <summary>
    /// 设置表里**智能体该动的**那几个键。其余的是程序自己记的状态（音色、窗口位置、
    /// 闹钟时刻），导出去只会让 AI 以为那些也归它管。
    /// </summary>
    private static readonly string[] AgentKeys = ["layout", "opacityPercent", "alarmCommand"];

    private const string RequestTemplate =
        "e.g. Only count VS Code and Chrome when the title mentions GitHub.\n"
        + "e.g. Remind me to stand up every hour between 9 and 6 on weekdays.\n"
        + "e.g. Make the window compact and 60% opaque.";

    private readonly MainWindow? _owner;
    private readonly SampleStore? _store;

    public SqlWindow() => InitializeComponent();

    public SqlWindow(MainWindow owner, SampleStore? store)
    {
        InitializeComponent();
        _owner = owner;
        _store = store;

        var config = this.FindControl<TextBox>("ConfigText")!;
        var request = this.FindControl<TextBox>("RequestText")!;
        var sql = this.FindControl<TextBox>("SqlText")!;
        var result = this.FindControl<TextBlock>("Result")!;

        config.Text = store?.DumpConfig(AgentKeys) ?? "-- the database is not open";
        request.Watermark = RequestTemplate;

        this.FindControl<Button>("CopyAll")!.Click += async (_, _) =>
        {
            // ⚠️ **把 AGENT.md 一起装进去**：网页 AI 还缺「规矩」——改完要 bump 版本、
            //    正则区分大小写且两个平台报的名字不同、账本那几张表别碰、
            //    `setting` 的值是带引号的 JSON 片段。它才 9KB，拼进来就省掉了上传文件。
            var text = $"""
                {ReadAgentDoc()}

                ================================================================
                -- CURRENT CONFIGURATION
                ================================================================

                {config.Text}

                ================================================================
                -- WHAT I WANT
                ================================================================

                {(string.IsNullOrWhiteSpace(request.Text) ? "(the user left this blank — ask them what they want)" : request.Text)}

                ================================================================
                Reply with SQL only. Do not explain. Do not wrap it in markdown.
                You do not need to touch `config.version` — ItamiBen bumps it itself.
                """;

            if (Clipboard is { } clip) await clip.SetTextAsync(text);
            result.Text = $"Copied {text.Length:N0} characters. Paste that into a web AI, then bring back the SQL.";
        };

        this.FindControl<Button>("PasteSql")!.Click += async (_, _) =>
        {
            // ⚠️ Avalonia 12 把剪贴板换成了 `IAsyncDataTransfer`，取文本走
            //    `ClipboardExtensions.TryGetTextAsync`——**不是**旧的 `GetTextAsync`
            var text = Clipboard is { } clip ? await clip.TryGetTextAsync() : null;
            if (string.IsNullOrWhiteSpace(text)) { result.Text = "The clipboard is empty."; return; }

            sql.Text = StripFences(text);
            result.Text = "Pasted. Read it, then press Apply.";
        };

        this.FindControl<Button>("Apply")!.Click += (_, _) => RunSql(sql, result);
    }

    /// <summary>
    /// 跑那段 SQL。顺序是有讲究的，**每一步都有理由**：
    ///
    /// <list type="number">
    ///   <item><b>先把内存里的设置写回库。</b> 程序启动时把设置读进内存、退出时整份写回，
    ///   所以 SQL 改了 `setting` 的某一行，**退出时会被内存里那份盖掉**。
    ///   先 flush 让库成为唯一真相，这个冲突就不存在了；</item>
    ///   <item>备份整个库——一句话就能撤销，这是对付「AI 写错一句 UPDATE」最有效的东西；</item>
    ///   <item>事务里执行 + 核对账本指纹，对不上整个回滚（见 <see cref="SampleStore.ApplySql"/>）；</item>
    ///   <item>从库里重新装配置和设置，**当场生效**。</item>
    /// </list>
    ///
    /// ⚠️ **不关闭程序。** 关窗口走的是 `OnClosing` → `Settle(EndReason.Closed)`，
    /// 也就是说改一次配置就把**正在跑的那一轮作废掉**——代价和「改个提醒文字」完全
    /// 不成比例。`config.version` + 每分钟重读那套机制就是为这个建的。
    /// 只有 `layout` / `opacityPercent` 真需要重启（它们只在启动时读一次），
    /// 那就在结果里多说一句。
    /// </summary>
    /// ⚠️ 方法名**不能叫 `Apply`**：axaml 里那个 `Name="Apply"` 会让 Avalonia 的
    /// 名字生成器造一个同名字段，撞上就是 `CS0102`。这个项目为这类冲突改过两次名
    /// （`TotalsText` / `AlarmText`）。
    private void RunSql(TextBox sql, TextBlock result)
    {
        if (_store is not { } store) { result.Text = "The database is not open."; return; }
        if (string.IsNullOrWhiteSpace(sql.Text)) { result.Text = "There is no SQL to apply."; return; }

        var text = sql.Text;
        _owner?.FlushSettings();

        var backup = AppData.DbPath() + ".bak";
        try { store.BackupTo(backup); }
        catch (Exception e)
        {
            // ⚠️ 备份失败就**不跑**：没有退路的情况下执行外来 SQL 不值得
            result.Text = $"Could not make a backup, so nothing was run: {e.Message}";
            Events.Error("sql", "Backup failed; refused to apply", e);
            return;
        }

        var r = store.ApplySql(text);
        if (!r.Ok)
        {
            result.Text = $"Nothing was changed.\n\n{r.Message}";
            Events.Warn("sql", $"apply failed: {r.Message}");
            return;
        }

        _owner?.ReloadAfterSql();

        var restartNote = text.Contains("layout", StringComparison.OrdinalIgnoreCase)
                       || text.Contains("opacity", StringComparison.OrdinalIgnoreCase)
            ? "\nWindow size and opacity take effect the next time you start ItamiBen."
            : "";

        result.Text = $"Applied. {r.RowsChanged} row(s) changed, configuration reloaded."
                    + $"{restartNote}\n\nA copy of the database from just before this was saved as "
                    + $"{Path.GetFileName(backup)}.";
        Events.Info("sql", $"applied, {r.RowsChanged} rows changed");
    }

    /// <summary>随程序发的那份说明。读不到就退回一句提示——**少一份说明不该让这扇窗废掉**。</summary>
    private static string ReadAgentDoc()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "AGENT.md");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch { }
        return "-- AGENT.md could not be read; see the project README for the schema.";
    }

    /// <summary>
    /// 剥掉 markdown 的代码围栏。
    ///
    /// ⚠️ 不是可有可无的修饰：网页 AI **几乎一定**会把 SQL 包在 ```sql … ``` 里，
    /// 而那三个反引号会让整段 SQL 第一行就语法错误。
    /// </summary>
    private static string StripFences(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[0].TrimStart().StartsWith("```", StringComparison.Ordinal))
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].TrimEnd().EndsWith("```", StringComparison.Ordinal))
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).Trim();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
