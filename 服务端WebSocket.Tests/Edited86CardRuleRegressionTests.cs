using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>“游戏内待批改86”中已确认卡牌规则缺口的定向回归。</summary>
public class Edited86CardRuleRegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP10_103_PutsChosenSupernovaIntoLifeFaceUp()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var oldLife = Card("OP15-003");
        var target = Card("OP10-103");
        me.LifeArea.Add(oldLife);
        me.Hand.Add(target);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP10-103"), EffectTrigger.OnEnterField, prompts);

        Assert.Same(target, me.LifeArea[0]);
        Assert.True(target.IsLifeFaceUp);
        Assert.Contains(oldLife, me.Hand);
    }

    [Fact]
    public async Task EB03_059_PutsTriggerCharacterIntoLifeFaceUp()
    {
        var state = TestScene.New("OP07-097").Build();
        var me = state.Players[0];
        var target = Card("ST20-003");
        Assert.False(string.IsNullOrEmpty(target.Info.Trigger));
        me.LifeArea.AddRange([Card("OP15-003"), Card("OP15-004")]);
        me.Hand.Add(target);

        await EffectRuntime.Resolve(
            state, 0, Card("EB03-059"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Same(target, me.LifeArea[0]);
        Assert.True(target.IsLifeFaceUp);
        Assert.DoesNotContain(target, me.Hand);
    }

    [Fact]
    public async Task OP08_079_TrashesTargetWithoutResolvingOnKo()
    {
        var state = TestScene.New().Build();
        state.TurnCount = 3;
        var me = state.Players[0];
        var opponent = state.Players[1];
        var source = Card("OP08-079");
        source.TurnPlayed = state.TurnCount;
        me.Characters.Add(source);
        var discard = Card("OP15-003");
        me.Hand.Add(discard);
        var target = Card("OP10-005");
        opponent.Characters.Add(target);
        var wouldBeDrawn = Card("OP15-004");
        opponent.Deck.Add(wouldBeDrawn);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(discard, me.Trash);
        Assert.Contains(target, opponent.Trash);
        Assert.DoesNotContain(target, opponent.Characters);
        Assert.Contains(wouldBeDrawn, opponent.Deck);
        Assert.DoesNotContain(wouldBeDrawn, opponent.Hand);
        Assert.Empty(state.PendingKOEffects);
    }

    [Fact]
    public async Task OP11_010_AttackEffectLetsNavyLeaderAttackActiveCharacter()
    {
        var state = TestScene.New("OP11-001").OppCharacter("OP11-005").Build();
        state.TurnCount = 3;
        state.CurrentTurnPlayer = 0;
        var me = state.Players[0];
        var target = state.Players[1].Characters.Single();
        var source = Card("OP11-010");
        me.Characters.Add(source);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = source.Id,
            TargetIsLeader = true,
        };
        var prompts = new MockPromptService().QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(1000, source.PowerModThisTurn);
        Assert.True(ActionValidator.HasKeyword(state, me.Leader, "可攻击活跃"));
        Assert.Equal("OwnLeader", Assert.Single(prompts.ChooseHistory).kind);
        state.CurrentBattle = null;
        Assert.False(target.IsTapped);
        Assert.True(ActionValidator.CanAttack(
            state, 0, me.Leader.Id, targetIsLeader: false, target.Id).Ok);
    }

    [Fact]
    public async Task OP11_010_DoesNotGrantActiveAttackToNonNavyLeader()
    {
        var state = TestScene.New("OP01-001").Build();
        var source = Card("OP11-010");
        state.Players[0].Characters.Add(source);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = source.Id,
            TargetIsLeader = true,
        };
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Equal(1000, source.PowerModThisTurn);
        Assert.False(ActionValidator.HasKeyword(state, state.Players[0].Leader, "可攻击活跃"));
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP15_015_AttachesOnlyOpponentRestedDon()
    {
        var state = TestScene.New().OppCharacter("OP15-003").Build();
        var opponent = state.Players[1];
        var target = opponent.Characters.Single();
        var restedDon = new DonCard { State = DonState.Rest };
        var activeDon = new DonCard { State = DonState.Active };
        opponent.CostArea.AddRange([restedDon, activeDon]);
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP15-015"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(DonState.Attached, restedDon.State);
        Assert.Equal(target.Id, restedDon.AttachedToCardId);
        Assert.Equal(DonState.Active, activeDon.State);
        Assert.Null(activeDon.AttachedToCardId);
        Assert.Equal(-1000, target.PowerModThisTurn);
    }

    [Fact]
    public async Task ST21_015_StaticRushIsNotAnOnEnterEffectAndRemainsSanjiEligible()
    {
        var state = TestScene.New("PRB01-001").Build();
        state.TurnCount = 3;
        var me = state.Players[0];
        var zoro = Card("ST21-015");
        zoro.TurnPlayed = state.TurnCount;
        me.Characters.Add(zoro);
        Assert.DoesNotContain("OnEnterField", zoro.Info.EffectTags);

        await EffectRuntime.Resolve(
            state, 0, zoro, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.False(ActionValidator.HasKeyword(state, zoro, "速攻"));

        me.CostArea.AddRange([
            new DonCard { State = DonState.Attached, AttachedToCardId = zoro.Id },
            new DonCard { State = DonState.Attached, AttachedToCardId = zoro.Id },
        ]);
        Assert.True(ActionValidator.HasKeyword(state, zoro, "速攻"));

        me.CostArea.Clear();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(zoro.Id.ToString());
        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(zoro.Id.ToString(), Assert.Single(prompts.ChooseHistory).choices);
        Assert.True(ActionValidator.HasKeyword(state, zoro, "速攻"));
        Assert.True(ActionValidator.CanAttack(
            state, 0, zoro.Id, targetIsLeader: true, targetId: null).Ok);
    }

    [Fact]
    public void OP08_030And032_HaveTheirPrintedCounterValues()
    {
        _ = TestScene.New().Build();

        Assert.Equal(1000, CardDatabase.Get("OP08-030")!.Counter);
        Assert.Equal(2000, CardDatabase.Get("OP08-032")!.Counter);
    }

    [Fact]
    public async Task ST16_005_CountsRestedUtaLeaderForStaticPower()
    {
        var state = TestScene.New("ST11-001").Build();
        var luffy = Card("ST16-005");
        state.Players[0].Characters.Add(luffy);
        state.Players[0].Leader.IsTapped = true;

        await EffectRuntime.Resolve(state, 0, luffy, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.DoesNotContain("OnEnterField", luffy.Info.EffectTags);
        Assert.Equal(luffy.Info.Power + 1000, state.CurrentPowerOf(0, luffy));
        state.Players[0].Leader.IsTapped = false;
        Assert.Equal(luffy.Info.Power, state.CurrentPowerOf(0, luffy));
    }

    [Fact]
    public async Task EB02_013_CanPlayZouStageAlreadyInHandWhenTopSevenHasNone()
    {
        var state = TestScene.New().MyActiveDon(3).MyDeckTop("OP15-003").Build();
        var me = state.Players[0];
        var zou = Card("OP08-039");
        me.Hand.Add(zou);
        var prompts = new MockPromptService()
            .QueueChooseEmpty()
            .QueueChoose(zou.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("EB02-013"), EffectTrigger.OnEnterField, prompts);

        Assert.Same(zou, me.StageCard);
        Assert.DoesNotContain(zou, me.Hand);
        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "OwnHandStage");
    }

    [Fact]
    public async Task OP11_118_CanBounceOwnCharacterThenAttachRestedDon()
    {
        var state = TestScene.New().MyCharacter("OP15-004").Build();
        var me = state.Players[0];
        var source = Card("OP11-118");
        var ownTarget = me.Characters.Single();
        me.Characters.Add(source);
        var cost = Card("OP15-003");
        me.Hand.Add(cost);
        var restedDon = new DonCard { State = DonState.Rest };
        me.CostArea.Add(restedDon);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = source.Id,
            TargetIsLeader = true,
        };
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(cost.Id.ToString())
            .QueueChoose(ownTarget.Id.ToString())
            .QueueChoose(me.Leader.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Contains(cost, me.Trash);
        Assert.Contains(ownTarget, me.Hand);
        Assert.DoesNotContain(ownTarget, me.Characters);
        Assert.Equal(DonState.Attached, restedDon.State);
        Assert.Equal(me.Leader.Id, restedDon.AttachedToCardId);
    }

    [Fact]
    public async Task OP15_104_OnEnterDrawsTwoThenDiscardsTwo()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        state.Players[1].LifeArea.Add(Card("OP15-005"));
        var first = me.Deck[0];
        var second = me.Deck[1];
        var prompts = new MockPromptService().QueueChoose(first.Id.ToString(), second.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP15-104"), EffectTrigger.OnEnterField, prompts);

        Assert.Empty(me.Deck);
        Assert.Empty(me.Hand);
        Assert.Contains(first, me.Trash);
        Assert.Contains(second, me.Trash);
    }

    [Fact]
    public async Task OP15_104_LifeTriggerDrawsTwoThenDiscardsOne()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004").Build();
        var me = state.Players[0];
        var source = Card("OP15-104");
        me.Trash.Add(source);
        var discard = me.Deck[0];

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnLifeRevealTrigger,
            new MockPromptService().QueueChoose(discard.Id.ToString()));

        Assert.Single(me.Hand);
        Assert.Contains(discard, me.Trash);
    }

    [Fact]
    public async Task OP16_095_GrantsRecognizedUnblockableKeyword()
    {
        var state = TestScene.New().MyCharacter("OP16-081").Build();
        var target = state.Players[0].Characters.Single();

        await EffectRuntime.Resolve(
            state, 0, Card("OP16-095"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.True(ActionValidator.HasKeyword(state, target, "不可阻挡"));
        Assert.DoesNotContain(target.GainedKeywords, keyword => keyword.Keyword == "Unblockable");
    }

    [Fact]
    public async Task ST32_005_KeepsPrintedAttackPermissionWhenOnEnterIsNullified()
    {
        var state = TestScene.New("OP09-081").OppCharacter("OP16-081").Build();
        state.TurnCount = 3;
        var me = state.Players[0];
        var target = state.Players[1].Characters.Single();
        var restedTarget = Card("OP15-003");
        restedTarget.IsTapped = true;
        state.Players[1].Characters.Add(restedTarget);
        var zoro = Card("ST32-005");
        zoro.TurnPlayed = state.TurnCount;
        me.Characters.Add(zoro);

        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, zoro, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.True(state.IsTriggerNullified(zoro, EffectTrigger.OnEnterField));
        Assert.False(target.IsTapped);
        Assert.True(ActionValidator.HasKeyword(state, zoro, "登场回合可攻击角色"));
        var attack = ActionValidator.CanAttack(
            state, 0, zoro.Id, targetIsLeader: false, restedTarget.Id);
        Assert.True(attack.Ok, attack.Reason);
    }

    [Fact]
    public async Task OP16_054_StaticPowerTracksDonTurnAndHandCount()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 0;
        var me = state.Players[0];
        var source = Card("OP16-054");
        me.Characters.Add(source);
        for (var i = 0; i < 5; i++) me.Hand.Add(Card("OP15-003"));
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = source.Id });

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(source.Info.Power + 4000, state.CurrentPowerOf(0, source));
        me.Hand.RemoveAt(0);
        Assert.Equal(source.Info.Power + 1000, state.CurrentPowerOf(0, source));
        me.Hand.Add(Card("OP15-003"));
        state.CurrentTurnPlayer = 1;
        Assert.Equal(source.Info.Power, state.CurrentPowerOf(0, source));
    }

    [Fact]
    public async Task OP05_067_AttackAddsActiveDonAtThreeLife()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        me.LifeArea.AddRange([Card("OP15-003"), Card("OP15-004"), Card("OP15-005")]);
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });

        await EffectRuntime.Resolve(
            state, 0, Card("OP05-067"), EffectTrigger.OnAttackDeclare, new MockPromptService());

        Assert.Equal(1, me.ActiveDonCount);
        Assert.Empty(me.DonDeck);
    }

    [Fact]
    public async Task OP17_110_PlayedOP17_104ResolvesOnEnterAndAddsLife()
    {
        var state = TestScene.New("OP17-099").MyActiveDon(2)
            .MyHandAdd("OP17-104").MyDeckTop("OP15-003").Build();
        state.TurnCount = 3;
        state.CurrentTurnPlayer = 0;
        var me = state.Players[0];
        var source = Card("OP17-110");
        me.Characters.Add(source);
        var summoned = me.Hand.Single();
        var prompts = new MockPromptService()
            .QueueChoose(summoned.Id.ToString())
            .QueueConfirm(true);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(summoned, me.Characters);
        Assert.Equal(2, me.RestDonCount);
        Assert.Single(me.LifeArea);
        Assert.Empty(me.Deck);
        Assert.True(ActionValidator.HasKeyword(state, source, "速攻"));
    }
}
