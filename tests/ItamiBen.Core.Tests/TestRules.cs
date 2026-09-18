using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>几个测试共用的规则。故意写成跟 rules.md 里那个配置块一模一样的样子。</summary>
internal static class TestRules
{
    public const string Json = """
    {
      "Groups": {
        "编程": {
          "Rules": [
            { "App": "^Code$" },
            { "App": "^Google Chrome$", "Title": "GitHub" }
          ]
        },
        "读书": {
          "Rules": [ { "Title": "\\.pdf" } ]
        },
        "去年的目标": {
          "Disabled": true,
          "Rules": [ { "App": "^Xcode$" } ]
        }
      }
    }
    """;

    public static GoalRules Rules { get; } = GoalRules.Parse(Json);
}
