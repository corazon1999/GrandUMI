using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>机器人 2026-08-15 汇总问题的定向回归。</summary>
public class RobotBugBatch20260815Tests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    [Fact]
    public void OP07_097_LeaderLife_IsTwo()
    {
        _ = TestScene.New().Build();
        Assert.Equal(2, CardDatabase.Get("OP07-097")!.Cost);

        string deck = "OP07-097\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 50));
        var engine = new GameEngine("op07-097-life", ("s0", "p0", deck), ("s1", "p1", deck), 0, 1);
        Assert.Equal(2, engine.State.Players[0].LifeArea.Count);
        Assert.Equal(2, engine.State.Players[1].LifeArea.Count);
    }

    [Fact]
    public async Task OP13_079_ActivatedMain_TrashesChosenCelestialDragonBeforeDrawing()
    {
        var state = TestScene.New("OP13-079").MyCharacter("OP13-085").MyDeckTop("OP13-080").Build();
        var source = state.Players[0].Leader;
        var costCard = state.Players[0].Characters.Single();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueOption(0)
            .QueueChoose(costCard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.DoesNotContain(costCard, state.Players[0].Characters);
        Assert.Contains(costCard, state.Players[0].Trash);
        Assert.Single(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Deck);
    }

    [Fact]
    public async Task OP14_105_OnlyAttachesRestedDonToChosenTargets()
    {
        var state = TestScene.New().MyCharacter("OP14-105").MyCharacter("OP14-103").Build();
        var me = state.Players[0];
        var source = me.Characters[0];
        var chosenTarget = me.Characters[1];
        foreach (var number in new[] { "OP14-103", "OP14-106", "OP14-107" }) me.Hand.Add(Card(number));
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(me.Hand.Select(card => card.Id.ToString()).ToArray())
            .QueueChoose(chosenTarget.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(1, me.CostArea.Count(d => d.State == DonState.Attached && d.AttachedToCardId == chosenTarget.Id));
        Assert.DoesNotContain(me.CostArea, d => d.State == DonState.Attached && d.AttachedToCardId == me.Leader.Id);
        Assert.Single(me.CostArea.Where(d => d.State == DonState.Rest));
    }

    [Fact]
    public async Task OP15_058_AllowsChoosingDonAddAndAttachCounts()
    {
        var state = TestScene.New("OP15-058").MyCharacter("OP15-050").Build();
        state.TurnCount = 2;
        var me = state.Players[0];
        for (int i = 0; i < 6; i++) me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        var target = me.Characters.Single();
        var prompts = new MockPromptService()
            .QueueOption(1) // 追加1张活跃咚
            .QueueOption(2) // 追加2张休息咚
            .QueueChoose(target.Id.ToString())
            .QueueOption(1); // 只赋予1张休息咚

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(3, me.DonDeck.Count);
        Assert.Equal(1, me.CostArea.Count(d => d.State == DonState.Active));
        Assert.Equal(1, me.CostArea.Count(d => d.State == DonState.Rest));
        Assert.Equal(1, me.CostArea.Count(d => d.State == DonState.Attached && d.AttachedToCardId == target.Id));
    }

    [Fact]
    public async Task OP09_093_NullifiedPrintedBlocker_CannotProvideBlockerKeyword()
    {
        var state = TestScene.New("OP09-081").MyCharacter("OP09-093").Build();
        var teach = state.Players[0].Characters.Single();
        teach.TurnPlayed = state.TurnCount;
        var blocker = Card("ST33-004");
        state.Players[1].Characters.Add(blocker);
        var prompts = new MockPromptService().QueueChoose(blocker.Id.ToString());

        await EffectRuntime.Resolve(state, 0, teach, EffectTrigger.ActivatedMain, prompts);

        Assert.True(state.IsContinuouslyNullified(blocker));
        Assert.False(ActionValidator.HasKeyword(state, blocker, "阻挡者"));
    }

    [Fact]
    public async Task OP17_111_OnPlay_CanDeclineRevealAndKoEffect()
    {
        var state = TestScene.New().Build();
        var source = Card("OP17-111");
        state.Players[0].Characters.Add(source);
        state.Players[0].Hand.Add(Card("OP17-019"));
        state.Players[0].Hand.Add(Card("OP17-071"));
        var prompts = new MockPromptService().QueueConfirm(false);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Single(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(2, state.Players[0].Hand.Count);
    }

    [Fact]
    public async Task OP13_065_SearchAcceptsFormerRogerPiratesBarrett()
    {
        var state = TestScene.New().MyDeckTop("OP13-068", "OP13-080").Build();
        var source = Card("OP13-065");
        state.Players[0].Characters.Add(source);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Contains(state.Players[0].Hand, card => card.Info.Number == "OP13-068");
    }

    [Fact]
    public async Task ST21_010_AttackTargetsCharacterByCurrentPower()
    {
        var state = TestScene.New().Build();
        var source = Card("ST21-010");
        state.Players[0].Characters.Add(source);
        state.Players[0].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = source.Id });
        state.Players[0].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = source.Id });
        var target = Card("OP13-080");
        target.PowerModThisTurn = -1000;
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        var choice = Assert.Single(prompts.ChooseHistory.Where(history => history.kind == "OpponentCharacter"));
        Assert.Contains(target.Id.ToString(), choice.choices);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP06_017_CounterPaysLifeAndBuffsUntilTurnEnd()
    {
        var state = TestScene.New().Build();
        state.Players[0].LifeArea.Add(Card("OP13-080"));
        var source = Card("OP06-017");
        var prompts = new MockPromptService().QueueChoose(state.Players[0].Leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Empty(state.Players[0].LifeArea);
        Assert.Single(state.Players[0].Hand);
        Assert.Equal(3000, state.Players[0].Leader.PowerModThisTurn);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP09_022_MakesNormalAndEffectPlayedCharactersEnterRested()
    {
        var state = TestScene.New("OP09-022").MyHandAdd("OP13-085").Build();

        var played = CardPlayer.Play(state, 0, 0).Card;
        Assert.True(played.IsTapped);

        var fromTrash = Card("OP13-086");
        state.Players[0].Trash.Add(fromTrash);
        await AtomicOps.PlayFromTrashFree(state, 0, fromTrash);
        Assert.True(fromTrash.IsTapped);
    }
}
