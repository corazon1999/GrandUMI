using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class EB02_030_EffectTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public void CardData_DeclaresEventCounter()
    {
        _ = TestScene.New().Build();

        Assert.Contains("EventCounter", CardDatabase.Get("EB02-030")!.EffectTags);
    }

    [Fact]
    public async Task EventCounter_DiscardsOneCardToPreventBattleKO()
    {
        var state = TestScene.New().MyCharacter("EB02-001").MyHandAdd("EB02-001").Build();
        var me = state.Players[0];
        var victim = me.Characters[0];
        var discard = me.Hand[0];
        var source = Card("EB02-030");
        me.Trash.Add(source);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);
        bool wasKOd = await BattleEngine.KOCardAsync(state, 0, victim, prompts);

        Assert.False(wasKOd);
        Assert.Contains(victim, me.Characters);
        Assert.DoesNotContain(discard, me.Hand);
        Assert.Contains(discard, me.Trash);
        Assert.True(me.HandDiscardedByEffectThisTurn);
        Assert.Single(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task EventCounter_CanProtectMultipleCharactersWithSeparateDiscards()
    {
        var state = TestScene.New()
            .MyCharacter("EB02-001")
            .MyCharacter("EB02-001")
            .MyHandAdd("EB02-001")
            .MyHandAdd("EB02-001")
            .Build();
        var me = state.Players[0];
        var victims = me.Characters.ToList();
        var discards = me.Hand.ToList();
        var source = Card("EB02-030");
        me.Trash.Add(source);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(true)
            .QueueChoose(discards[0].Id.ToString())
            .QueueChoose(discards[1].Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);
        bool firstWasKOd = await BattleEngine.KOCardAsync(state, 0, victims[0], prompts);
        bool secondWasKOd = await BattleEngine.KOCardAsync(state, 0, victims[1], prompts);

        Assert.False(firstWasKOd);
        Assert.False(secondWasKOd);
        Assert.Equal(2, me.Characters.Count);
        Assert.Empty(me.Hand);
        Assert.All(discards, discard => Assert.Contains(discard, me.Trash));
    }

    [Fact]
    public async Task EventCounter_WhenReplacementDeclined_CharacterIsKOd()
    {
        var state = TestScene.New().MyCharacter("EB02-001").MyHandAdd("EB02-001").Build();
        var me = state.Players[0];
        var victim = me.Characters[0];
        var discard = me.Hand[0];
        var source = Card("EB02-030");
        me.Trash.Add(source);
        var prompts = new MockPromptService().QueueConfirm(false);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);
        bool wasKOd = await BattleEngine.KOCardAsync(state, 0, victim, prompts);

        Assert.True(wasKOd);
        Assert.DoesNotContain(victim, me.Characters);
        Assert.Contains(discard, me.Hand);
    }

    [Fact]
    public async Task EventCounter_DoesNotProtectAgainstEffectKO()
    {
        var state = TestScene.New().MyCharacter("EB02-001").MyHandAdd("EB02-001").Build();
        var me = state.Players[0];
        var victim = me.Characters[0];
        var discard = me.Hand[0];
        var source = Card("EB02-030");
        me.Trash.Add(source);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);
        bool wasKOd = await AtomicOps.KOByEffectAsync(state, 0, victim, prompts, actingSide: 1);

        Assert.True(wasKOd);
        Assert.DoesNotContain(victim, me.Characters);
        Assert.Contains(discard, me.Hand);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task EventCounter_ExpiresAfterCurrentTurn()
    {
        var state = TestScene.New().MyCharacter("EB02-001").MyHandAdd("EB02-001").Build();
        var me = state.Players[0];
        var victim = me.Characters[0];
        var discard = me.Hand[0];
        var source = Card("EB02-030");
        me.Trash.Add(source);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);
        state.TurnCount++;
        bool wasKOd = await BattleEngine.KOCardAsync(state, 0, victim, prompts);

        Assert.True(wasKOd);
        Assert.DoesNotContain(victim, me.Characters);
        Assert.Contains(discard, me.Hand);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task LifeTrigger_StillDrawsOneCard()
    {
        var state = TestScene.New().MyDeckTop("EB02-001").Build();

        await EffectRuntime.Resolve(state, 0, Card("EB02-030"),
            EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Single(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Deck);
    }
}
