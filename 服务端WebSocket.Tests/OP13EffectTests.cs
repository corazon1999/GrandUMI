using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP13EffectTests
{
    [Fact]
    public async Task OP13_004_Plays_OP13_016_StillShowsTopFourWhenNoCardIsEligible()
    {
        TestScene.New(); // 确保测试卡库已加载
        var deck = BuildLegalDeck("OP13-004");
        var engine = new GameEngine(
            "op13-016-no-eligible-test",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260817);
        var state = engine.State;
        var me = state.Players[0];

        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        me.Hand.Clear();
        me.Hand.Add(new CardInstance { Info = CardDatabase.Get("OP13-016")! });
        me.Characters.Clear();
        me.CostArea.Clear();
        me.CostArea.Add(new DonCard { State = DonState.Active });
        me.Deck.Clear();
        for (var index = 0; index < 4; index++)
            me.Deck.Add(new CardInstance { Info = CardDatabase.Get("OP13-016")! });
        me.Deck.Add(new CardInstance { Info = CardDatabase.Get("OP13-017")! });
        var originalTopFour = me.Deck.Take(4).ToList();
        var originalTail = me.Deck[4];

        engine.HandleAction(0, "PlayCard", JsonSerializer.SerializeToElement(new { handIndex = 0 }));
        await engine.WaitSettledAsync();

        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        Assert.Equal("LookTopReveal", prompt.Kind);
        Assert.Empty(prompt.ValidChoices);
        var choiceCards = Assert.IsAssignableFrom<IEnumerable<object>>(prompt.Extra["choiceCards"]);
        Assert.Equal(4, choiceCards.Count());

        engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = Array.Empty<string>(),
        }));
        await engine.WaitSettledAsync(resolvingPromptId: prompt.PromptId);

        Assert.Null(state.PendingPrompt);
        Assert.Contains(me.Characters, card => card.Info.Number == "OP13-016");
        Assert.Equal(originalTail.Id, me.Deck[0].Id);
        Assert.Equal(originalTopFour.Select(card => card.Id), me.Deck.Skip(1).Select(card => card.Id));
    }

    [Fact]
    public async Task OP13_113_CanSearchBurningSwordWithPrintedLifeTrigger()
    {
        var state = TestScene.New()
            .MyDeckTop("OP08-117", "OP15-003")
            .Build();
        var me = state.Players[0];
        var source = new CardInstance { Info = CardDatabase.Get("OP13-113")! };
        var burningSword = me.Deck[0];
        me.Characters.Add(source);
        var prompts = new MockPromptService()
            .QueueChoose(burningSword.Id.ToString());

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.OnEnterField,
            prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("LilithReveal", prompt.kind);
        Assert.Contains(burningSword.Id.ToString(), prompt.choices);
        Assert.False(string.IsNullOrWhiteSpace(burningSword.Info.Trigger));
        Assert.Contains(burningSword, me.Hand);
        Assert.DoesNotContain(burningSword, me.Deck);
    }

    [Fact]
    public async Task OP13_004_CurrentCostAtLeast8_BuffsLeaderAndAllOwnCharacters()
    {
        var state = TestScene.New("OP13-004")
            .MyCharacter("OP02-013")
            .MyCharacter("OP15-003")
            .OppCharacter("OP15-003")
            .AttachDonToMyLeader(1)
            .Build();
        var me = state.Players[0];
        var currentCost8 = me.Characters[0];
        currentCost8.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state,
            0,
            me.Leader,
            EffectTrigger.OnGameStart,
            new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, currentCost8));
        Assert.Equal(1000, state.ContinuousPowerBonus(0, me.Leader));
        Assert.All(me.Characters, card =>
            Assert.Equal(1000, state.ContinuousPowerBonus(0, card)));
        Assert.Equal(0, state.ContinuousPowerBonus(1, state.Players[1].Characters[0]));
    }

    [Fact]
    public async Task OP13_004_RequiresAttachedDonAndCurrentCostAtLeast8()
    {
        var state = TestScene.New("OP13-004")
            .MyCharacter("OP02-013")
            .Build();
        var me = state.Players[0];
        var character = me.Characters[0];
        character.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state,
            0,
            me.Leader,
            EffectTrigger.OnGameStart,
            new MockPromptService());

        Assert.Equal(0, state.ContinuousPowerBonus(0, me.Leader));

        me.CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = me.Leader.Id,
        });
        Assert.Equal(1000, state.ContinuousPowerBonus(0, me.Leader));

        character.CostModThisTurn = 0;
        Assert.Equal(7, state.CurrentCostOf(0, character));
        Assert.Equal(0, state.ContinuousPowerBonus(0, me.Leader));
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP13")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            var count = counts.GetValueOrDefault(card.Number);
            if (count >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = count + 1;
        }
        return string.Join('\n', lines);
    }
}
