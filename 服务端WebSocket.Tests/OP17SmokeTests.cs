using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Debug;
using Xunit;
using Xunit.Abstractions;

namespace GrandUMI.Tests;

/// <summary>OP17 全卡脚本注册与触发链路冒烟测试。</summary>
public class OP17SmokeTests
{
    private static readonly HashSet<string> BlankCards = new(StringComparer.Ordinal)
    {
        "OP17-006", "OP17-035", "OP17-051", "OP17-070", "OP17-088", "OP17-100",
    };

    private readonly ITestOutputHelper _log;

    public OP17SmokeTests(ITestOutputHelper log) => _log = log;

    public static IEnumerable<object[]> AllEffectCards()
    {
        _ = TestScene.New().Build();
        foreach (var card in CardDatabase.GetBySet("OP17")
                     .Where(x => !BlankCards.Contains(x.Number))
                     .OrderBy(x => x.Number))
            yield return new object[] { card.Number };
    }

    [Fact]
    public void OP17_AllEffectCards_HaveScriptRegistration()
    {
        _ = TestScene.New().Build();
        var cards = CardDatabase.GetBySet("OP17").OrderBy(x => x.Number).ToList();

        Assert.Equal(119, cards.Count);
        Assert.Equal(113, cards.Count(x => !BlankCards.Contains(x.Number)));
        foreach (var card in cards.Where(x => !BlankCards.Contains(x.Number)))
            Assert.NotNull(ScriptedEffectRegistry.TryGet(card.Number));
        foreach (var card in cards.Where(x => BlankCards.Contains(x.Number)))
            Assert.Null(ScriptedEffectRegistry.TryGet(card.Number));
    }

    [Theory]
    [MemberData(nameof(AllEffectCards))]
    public async Task OP17_EffectCard_ApplicableTriggers_DoNotCrash(string cardNumber)
    {
        var info = CardDatabase.Get(cardNumber)!;
        var triggers = ApplicableTriggers(info).ToList();
        var crashes = new List<string>();

        foreach (var trigger in triggers)
        {
            var state = TestScene.MaxScenario(info.Kind == CardKind.Leader ? cardNumber : null);
            var source = PlaceSource(state, info, trigger);
            PrepareBattle(state, source, trigger);
            var payload = new Dictionary<string, object?>
            {
                ["victimId"] = source.Id.ToString(),
                ["victimOwner"] = 0,
                ["cardId"] = source.Id.ToString(),
                ["owner"] = 0,
                ["restedCardId"] = source.Id.ToString(),
                ["restedOwner"] = 0,
                ["AttackerIdx"] = trigger == EffectTrigger.OnOppAttackDeclare ? 1 : 0,
                ["attackerId"] = state.CurrentBattle?.AttackerCardId.ToString(),
                ["targetLeaderOwner"] = state.CurrentBattle?.DefenderPlayerIndex,
            };

            try
            {
                await EffectRuntime.Resolve(state, 0, source, trigger, new MockPromptService(), payload);
            }
            catch (Exception ex)
            {
                crashes.Add($"{trigger}: {ex.GetType().Name}: {ex.Message}");
            }

            Assert.True(state.Players[0].ActiveDonCount >= 0);
            Assert.True(state.Players[1].ActiveDonCount >= 0);
            Assert.True(state.Players[0].Hand.Count >= 0);
            Assert.True(state.Players[0].Deck.Count >= 0);
        }

        if (crashes.Count == 0) return;
        foreach (var crash in crashes) _log.WriteLine($"{cardNumber} {crash}");
        Assert.Fail($"{cardNumber} 在 {crashes.Count} 个适用触发中发生异常");
    }

    [Theory]
    [InlineData("红", 19)]
    [InlineData("绿", 19)]
    [InlineData("蓝", 20)]
    [InlineData("紫", 21)]
    [InlineData("黑", 21)]
    [InlineData("黄", 19)]
    public async Task OP17_GMColorCoverageRunner_CoversEveryCardWithoutFailure(string color, int expectedCount)
    {
        _ = TestScene.New().Build();

        var report = await OP17CoverageRunner.RunColorAsync(color);

        Assert.Equal(expectedCount, report.Total);
        Assert.Equal(expectedCount, report.Passed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(expectedCount, report.Results.Select(result => result.Number).Distinct().Count());
        Assert.All(report.Results, result =>
        {
            Assert.True(result.Passed, $"{result.Number}: {result.Message}");
            Assert.NotEmpty(result.Triggers);
        });
    }

    private static IEnumerable<EffectTrigger> ApplicableTriggers(CardInfo info)
    {
        var result = new HashSet<EffectTrigger>();
        foreach (var tag in info.EffectTags)
            if (Enum.TryParse<EffectTrigger>(tag, out var trigger)) result.Add(trigger);

        if (info.Kind == CardKind.Leader) result.Add(EffectTrigger.OnGameStart);
        if (info.Kind is CardKind.Character or CardKind.Stage) result.Add(EffectTrigger.OnEnterField);
        if (!string.IsNullOrWhiteSpace(info.Trigger)) result.Add(EffectTrigger.OnLifeRevealTrigger);
        return result;
    }

    private static CardInstance PlaceSource(GameState state, CardInfo info, EffectTrigger trigger)
    {
        if (info.Kind == CardKind.Leader) return state.Players[0].Leader;

        var source = new CardInstance { Info = info, TurnPlayed = state.TurnCount };
        if (trigger is EffectTrigger.OnKO or EffectTrigger.OnLifeRevealTrigger)
            state.Players[0].Trash.Add(source);
        else if (info.Kind == CardKind.Character)
            state.Players[0].Characters.Add(source);
        else if (info.Kind == CardKind.Stage)
            state.Players[0].StageCard = source;
        return source;
    }

    private static void PrepareBattle(GameState state, CardInstance source, EffectTrigger trigger)
    {
        if (trigger is not (EffectTrigger.OnAttackDeclare
            or EffectTrigger.OnOppAttackDeclare
            or EffectTrigger.OnBlockDeclare
            or EffectTrigger.OnLeaderBattle
            or EffectTrigger.OnBattleEnd)) return;

        bool opponentAttacks = trigger == EffectTrigger.OnOppAttackDeclare;
        int attackerSide = opponentAttacks ? 1 : 0;
        var attacker = opponentAttacks
            ? state.Players[1].Leader
            : source.Info.Kind == CardKind.Leader ? source : state.Players[0].Leader;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = attackerSide,
            AttackerCardId = attacker.Id,
            DefenderPlayerIndex = 1 - attackerSide,
            TargetIsLeader = true,
        };
    }
}
