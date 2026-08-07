using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>选目标、休息、KO、回手、回卡组及持续修改型生命【触发】回归测试。</summary>
public class LifeTriggerTargetCompletionTests
{
    public static IEnumerable<object[]> TargetCards()
    {
        foreach (var number in new[]
        {
            "EB01-028", "EB01-029", "EB01-053", "EB01-059", "EB02-018",
            "EB03-059", "EB04-028", "OP02-069", "OP04-037", "OP06-023",
            "OP06-038", "OP06-101", "OP07-036", "OP07-116", "OP11-019",
            "OP12-113", "P-106", "PRB02-017", "ST01-016", "OP16-115",
        })
            yield return [number];
    }

    [Theory]
    [MemberData(nameof(TargetCards))]
    public async Task LifeTrigger_WithLegalTarget_AppliesPrintedEffect(string sourceNumber)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opp = state.Players[1];
        var source = Card(sourceNumber);
        CardInstance? target = null;

        switch (sourceNumber)
        {
            case "EB01-053":
                target = AddCharacter(opp, "EB01-005");
                break;
            case "EB01-059":
                me.LifeArea.Add(Card("OP15-003"));
                opp.LifeArea.Add(Card("OP15-004"));
                target = AddCharacter(opp, "EB01-005");
                break;
            case "OP04-037":
            case "OP06-038":
                target = AddCharacter(opp, "EB01-005", rested: true);
                break;
            case "OP11-019":
            case "OP16-115":
                break; // 合法目标包含领袖，默认选择首个领袖。
            case "OP12-113":
                me.Trash.Add(source);
                target = AddCharacter(opp, "EB01-005");
                break;
            case "P-106":
                me.Deck.Add(Card("OP15-003"));
                target = AddCharacter(opp, "EB01-005");
                break;
            case "ST01-016":
                target = AddCharacter(opp, "EB01-017");
                break;
            default:
                target = AddCharacter(opp, "EB01-005");
                break;
        }

        await EffectRuntime.Resolve(state, 0, source,
            EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        switch (sourceNumber)
        {
            case "EB01-028":
                Assert.NotNull(target);
                Assert.Contains(target!, opp.Deck);
                Assert.DoesNotContain(target!, opp.Characters);
                break;
            case "EB01-029":
            case "EB04-028":
            case "OP02-069":
                Assert.NotNull(target);
                Assert.Contains(target!, opp.Hand);
                Assert.DoesNotContain(target!, opp.Characters);
                break;
            case "EB01-053":
                Assert.Equal(-3000, opp.Leader.PowerModThisTurn);
                Assert.Equal(-3000, target!.PowerModThisTurn);
                break;
            case "EB02-018":
            case "OP06-023":
            case "OP07-036":
            case "OP07-116":
                Assert.True(target!.IsTapped);
                break;
            case "EB03-059":
                Assert.Contains(target!.Restrictions, r => r.Kind == RestrictionKind.CannotAttack);
                break;
            case "OP11-019":
                Assert.Equal(1000, me.Leader.PowerModThisTurn);
                break;
            case "OP12-113":
                Assert.Contains(source, me.Hand);
                Assert.Contains(target!, opp.Trash);
                break;
            case "P-106":
                Assert.Single(me.Hand);
                Assert.Contains(target!, opp.Trash);
                break;
            case "OP16-115":
                Assert.True(opp.Leader.IsEffectsNullified);
                break;
            default:
                Assert.Contains(target!, opp.Trash);
                Assert.DoesNotContain(target!, opp.Characters);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(TargetCards))]
    public async Task LifeTrigger_WithoutLegalSelection_DoesNotAffectInvalidCards(string sourceNumber)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opp = state.Players[1];
        var source = Card(sourceNumber);
        var prompts = new MockPromptService();
        CardInstance? invalid = null;

        switch (sourceNumber)
        {
            case "EB01-053":
            case "OP11-019":
            case "OP16-115":
                prompts.QueueChooseEmpty();
                break;
            case "EB01-059":
                invalid = AddCharacter(opp, "EB01-005"); // 双方生命为0，费用1不合法。
                break;
            case "OP12-113":
                me.Trash.Add(source);
                break;
            case "P-106":
                me.Deck.Add(Card("OP15-003"));
                break;
            case "ST01-016":
                invalid = AddCharacter(opp, "EB01-005"); // 不是【阻挡者】。
                break;
        }

        await EffectRuntime.Resolve(state, 0, source,
            EffectTrigger.OnLifeRevealTrigger, prompts);

        switch (sourceNumber)
        {
            case "EB01-053":
                Assert.Equal(0, opp.Leader.PowerModThisTurn);
                break;
            case "EB01-059":
            case "ST01-016":
                Assert.Contains(invalid!, opp.Characters);
                Assert.DoesNotContain(invalid!, opp.Trash);
                break;
            case "OP11-019":
                Assert.Equal(0, me.Leader.PowerModThisTurn);
                break;
            case "OP12-113":
                Assert.Contains(source, me.Hand); // “最多1张”可不选，之后仍将自身加入手牌。
                Assert.Empty(opp.Trash);
                break;
            case "P-106":
                Assert.Single(me.Hand); // 抽牌仍结算，KO段无目标时跳过。
                Assert.Empty(opp.Trash);
                break;
            case "OP16-115":
                Assert.False(opp.Leader.IsEffectsNullified);
                break;
            default:
                Assert.Empty(opp.Trash);
                Assert.Empty(opp.Hand);
                Assert.Empty(opp.Deck);
                Assert.Empty(opp.Characters);
                break;
        }
    }

    private static CardInstance AddCharacter(PlayerState player, string number, bool rested = false)
    {
        var card = Card(number);
        card.IsTapped = rested;
        player.Characters.Add(card);
        return card;
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };
}
