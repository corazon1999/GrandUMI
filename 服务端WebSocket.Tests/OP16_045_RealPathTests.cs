using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects.Dsl;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// 真实路径复现：用户反馈 OP16-045 克洛克达尔【登场时】回手成本 + 收益登场后，
/// 领航 OP16-041 巴奇的离场触发被"吞掉"，不弹 prompt。
/// 走真实 GameEngine + 异步 PromptSystem + DebugSummon(单人GM调试)路径，而非同步 mock，
/// 以暴露同步单元测试测不出的真实差异。
/// </summary>
public class OP16_045_RealPathTests
{
    private static bool _loaded;
    private static void EnsureLoaded()
    {
        if (_loaded) return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? root = null;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "卡牌数据"))) { root = dir.FullName; break; }
            dir = dir.Parent;
        }
        Assert.NotNull(root);
        CardDatabase.LoadFrom(Path.Combine(root!, "卡牌数据"));
        DslInterpreter.LoadDirectory(Path.Combine(root!, "服务端WebSocket", "Effects", "Definitions"));
        _loaded = true;
    }

    private const string Deck =
        "OP16-080\nOP16-103\nOP16-103\nOP16-103\nOP16-103\nOP16-109\nOP16-109\nOP16-109\nOP16-109\n" +
        "OP16-110\nOP16-110\nOP16-110\nOP16-110\nOP16-115\nOP16-115\nOP09-096\nOP09-096\nOP09-096\nOP09-096\n" +
        "OP09-099\nOP09-099\nOP09-099\nOP09-099\nOP16-104\nOP16-104\nOP16-104\nOP16-104\nOP09-086\nOP09-086\n" +
        "OP09-086\nOP09-086\nEB04-058\nEB04-058\nEB04-058\nEB04-058\nOP16-108\nOP16-108\nOP16-108\nOP16-108\n" +
        "OP16-119\nOP16-119\nOP16-119\nOP16-119\nOP16-116\nOP16-116\nOP14-112\nOP14-112\nOP14-112\n" +
        "OP09-093\nOP09-093\nOP09-093";

    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;

    private static async Task<bool> WaitPrompt(GameEngine live, int timeoutMs = 3000)
    {
        for (int i = 0; i < timeoutMs / 10; i++)
        {
            if (live.State.PendingPrompt is not null) return true;
            await Task.Delay(10);
        }
        return live.State.PendingPrompt is not null;
    }

    [Theory]
    [InlineData(true)]   // 收益登场选 1 个
    [InlineData(false)]  // 收益登场选 0 个（跳过）
    public async Task DebugSummon_OP16_045_Bounce_ShouldTrigger_OP16_041(bool summonBenefit)
    {
        EnsureLoaded();
        var live = new GameEngine("rp", ("s0", "alice", Deck), ("s1", "bob", Deck), firstPlayer: 0, rngSeed: 1);
        await live.WaitSettledAsync();

        // 强制 P0 领航为 OP16-041 巴奇（绕过牌组领航限制；Leader 为 init-only，用反射写）
        var leaderInst = new CardInstance { Info = CardDatabase.Get("OP16-041")! };
        typeof(PlayerState).GetProperty(nameof(PlayerState.Leader))!.SetValue(live.State.Players[0], leaderInst);

        async Task Apply(int pi, string a, string d) { live.HandleAction(pi, a, J(d)); await live.WaitSettledAsync(); }
        await Apply(0, "Mulligan", "{\"redraw\":false}");
        await Apply(1, "Mulligan", "{\"redraw\":false}");

        var me = live.State.Players[0];
        Assert.Equal("OP16-041", me.Leader.Info.Number);

        // 满足条件：领航附 1 咚 + 手牌有囚犯(OP16-042) + 收益候选(OP16-044, cost2 因佩尔地狱)
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });
        var prisoner = new CardInstance { Info = CardDatabase.Get("OP16-042")! };
        var benefit = new CardInstance { Info = CardDatabase.Get("OP16-044")! };
        me.Hand.Add(prisoner);
        me.Hand.Add(benefit);

        // 场上摆 1 张费用≥2《因佩尔地狱》角色作回手目标
        live.HandleAction(0, "DebugSummon", J("{\"cardNumber\":\"OP16-024\"}"));
        Assert.True(await WaitPrompt(live, 1500) || me.Characters.Any(c => c.Info.Number == "OP16-024"));
        var bounceTarget = me.Characters.First(c => c.Info.Number == "OP16-024");

        // DebugSummon OP16-045 → 触发【登场时】(fire-and-forget, 不走 Track)
        live.HandleAction(0, "DebugSummon", J("{\"cardNumber\":\"OP16-045\"}"));

        bool buggyPrompted = false;
        var seenKinds = new List<string>();
        for (int step = 0; step < 12; step++)
        {
            if (!await WaitPrompt(live, 3000)) break;
            var pp = live.State.PendingPrompt!;
            seenKinds.Add(pp.Kind);
            if (pp.Kind == "OwnHandPrisoner") { buggyPrompted = true; break; }

            List<string> pick = pp.Kind switch
            {
                "Option" => new() { "0" },                                   // 克洛克达尔确认: 是
                "OwnCharacter" => new() { bounceTarget.Id.ToString() },      // 回手成本
                "PlayCharFromHand" => summonBenefit
                    ? new List<string> { benefit.Id.ToString() }            // 收益登场 1 个
                    : new List<string>(),                                    // 收益跳过 0 个
                _ => pp.ValidChoices.Take(Math.Max(pp.MinChoose, 0)).ToList()
            };
            live.HandleAction(pp.PlayerIndex, "PromptResponse",
                J($"{{\"promptId\":\"{pp.PromptId}\",\"chosen\":{JsonSerializer.Serialize(pick)}}}"));
        }

        Assert.True(buggyPrompted,
            $"OP16-045 回手后领航 OP16-041 未弹 prompt(吞效果). summonBenefit={summonBenefit}, 出现的prompt顺序=[{string.Join(",", seenKinds)}]");
    }
}
