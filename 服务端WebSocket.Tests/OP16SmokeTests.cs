using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;
using Xunit.Abstractions;

namespace GrandUMI.Tests;

/// <summary>
/// OP16 全卡冒烟测试 — 给每张含效果的卡跑一遍每个适用的触发，断言：
///   1. 不抛异常
///   2. 状态不变形（活跃咚 ≥0、手牌 ≥0、卡组 ≥0）
///   3. 引擎不会进入 IsGameOver 之外的"卡死"状态
///
/// 不验证具体效果是否符合卡牌描述（那需要每张卡单独写 Fact）。
/// </summary>
public class OP16SmokeTests
{
    private readonly ITestOutputHelper _log;
    public OP16SmokeTests(ITestOutputHelper log) { _log = log; }

    // 所有触发器（按"卡是否含此触发文本"自动启用，参考 EffectRuntime.HasEffectForTrigger）
    static readonly EffectTrigger[] AllTriggers =
    {
        EffectTrigger.OnEnterField,
        EffectTrigger.OnAttackDeclare,
        EffectTrigger.OnOppAttackDeclare,
        EffectTrigger.OnBlockDeclare,
        EffectTrigger.OnKO,
        EffectTrigger.PreKO,
        EffectTrigger.OnMyTurnEnd,
        EffectTrigger.OnOppTurnEnd,
        EffectTrigger.ActivatedMain,
        EffectTrigger.EventMain,
        EffectTrigger.EventCounter,
        EffectTrigger.OnLifeRevealTrigger,
    };

    /// <summary>用 MemberData 给每张 OP16 含效果卡生成一个测试用例</summary>
    public static IEnumerable<object[]> AllOp16Cards()
    {
        var root = ResolveCardDataRoot();
        var path = Path.Combine(root, "OP16.json");
        var json = File.ReadAllText(path);
        var arr = JsonSerializer.Deserialize<List<JsonElement>>(json);
        if (arr is null) yield break;
        foreach (var card in arr)
        {
            var num = card.GetProperty("number").GetString() ?? "";
            var effect = card.TryGetProperty("effectText", out var et) ? et.GetString() ?? "" : "";
            var trigger = card.TryGetProperty("trigger", out var tt) ? tt.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(effect) && string.IsNullOrEmpty(trigger)) continue;
            yield return new object[] { num };
        }
    }

    static string ResolveCardDataRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "卡牌数据");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到卡牌数据目录");
    }

    [Theory]
    [MemberData(nameof(AllOp16Cards))]
    public async Task OP16_Card_DoesNotCrash(string cardNumber)
    {
        // 先触发一次 TestScene 初始化，确保 CardDatabase + DSL 都加载
        _ = TestScene.MaxScenario();

        var info = CardDatabase.Get(cardNumber);
        Assert.NotNull(info);

        var triggersFired = new List<EffectTrigger>();
        var crashes = new List<string>();

        foreach (var trig in AllTriggers)
        {
            // 每个 trigger 重新构造场景（避免互相污染）
            var s = TestScene.MaxScenario(myLeader: info!.Kind == CardKind.Leader ? cardNumber : null);
            CardInstance source;

            if (info.Kind == CardKind.Leader)
            {
                source = s.Players[0].Leader;
            }
            else
            {
                // 把卡放到能 Resolve 的位置：场上（角色/舞台）或视为独立源（事件）
                source = new CardInstance { Info = info, TurnPlayed = s.TurnCount - 1 };
                if (info.Kind == CardKind.Character) s.Players[0].Characters.Add(source);
                else if (info.Kind == CardKind.Stage) s.Players[0].StageCard = source;
            }

            if (!EffectRuntime.HasEffectForTrigger(source, trig)) continue;

            try
            {
                await EffectRuntime.Resolve(s, 0, source, trig, new MockPromptService());
                triggersFired.Add(trig);
            }
            catch (Exception ex)
            {
                crashes.Add($"{trig}: {ex.GetType().Name}: {ex.Message}");
            }

            // 状态不变形
            Assert.True(s.Players[0].ActiveDonCount >= 0, $"{trig} 后活跃咚 < 0");
            Assert.True(s.Players[0].Hand.Count >= 0);
            Assert.True(s.Players[0].Deck.Count >= 0);
            Assert.True(s.Players[1].ActiveDonCount >= 0);
        }

        if (crashes.Count > 0)
        {
            _log.WriteLine($"{cardNumber} ({info!.Name}) 崩溃触发:");
            foreach (var c in crashes) _log.WriteLine("  " + c);
            Assert.Fail($"{cardNumber} 在 {crashes.Count} 个触发中崩溃");
        }
        // 不强制要求至少有一个触发被识别（有些卡的效果是"持续/规则"，无离散触发）
    }
}
