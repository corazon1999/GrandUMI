using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class WatcherReachabilityTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    public static IEnumerable<object[]> WatcherMappings()
    {
        yield return new object[] { "EB02-023", EffectTrigger.OnCharLeaveField };
        yield return new object[] { "OP01-062", EffectTrigger.OnOppEventPlayed };
        yield return new object[] { "OP02-094", EffectTrigger.OnAnyCharKOd };
        yield return new object[] { "OP06-048", EffectTrigger.OnOppBlocker };
        yield return new object[] { "OP06-048", EffectTrigger.OnOppEventPlayed };
        yield return new object[] { "OP07-097", EffectTrigger.OnTurnStart };
        yield return new object[] { "OP08-074", EffectTrigger.OnMyTurnEnd };
        yield return new object[] { "OP08-101", EffectTrigger.OnMyTurnEnd };
        yield return new object[] { "OP11-107", EffectTrigger.OnMyTurnEnd };
        yield return new object[] { "OP13-024", EffectTrigger.OnMyTurnEnd };
        yield return new object[] { "OP13-066", EffectTrigger.OnMyTurnEnd };
    }

    [Theory]
    [MemberData(nameof(WatcherMappings))]
    public void ConfiguredWatcher_IsReachableFromRuntime(string number, EffectTrigger trigger)
    {
        _ = TestScene.New().Build(); // 确保卡牌数据库与 DSL 已加载
        Assert.True(EffectRuntime.HasEffectForTrigger(Card(number), trigger));
    }

    [Fact]
    public async Task EB02_023_ReceivesCharacterLeaveWatcher()
    {
        var s = TestScene.New()
            .MyCharacter("EB02-023")
            .MyDeckTop("ST30-002", "ST30-003", "ST30-004", "ST30-005")
            .Build();
        var me = s.Players[0];
        var originalFourth = me.Deck[3];

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnCharLeaveField,
            new MockPromptService().QueueOption(1),
            new Dictionary<string, object?> { ["owner"] = 1 });

        Assert.Same(originalFourth, me.Deck[0]); // 原顶3张已移至牌组底
        Assert.Contains(me.TurnOnceUsed, key => key.StartsWith("EB02-023-leave:"));
    }

    [Fact]
    public async Task OP01_062_EventWatcher_RequiresAttachedDon()
    {
        var eligible = TestScene.New(myLeaderNumber: "OP01-062")
            .AttachDonToMyLeader(1)
            .MyDeckTop("ST30-002")
            .Build();
        await EffectRuntime.TriggerEvent(eligible, EffectTrigger.OnOppEventPlayed,
            new MockPromptService().QueueConfirm(true),
            new Dictionary<string, object?> { ["owner"] = 0 });
        Assert.Single(eligible.Players[0].Hand);

        var ineligible = TestScene.New(myLeaderNumber: "OP01-062")
            .MyDeckTop("ST30-002")
            .Build();
        await EffectRuntime.TriggerEvent(ineligible, EffectTrigger.OnOppEventPlayed,
            new MockPromptService().QueueConfirm(true),
            new Dictionary<string, object?> { ["owner"] = 0 });
        Assert.Empty(ineligible.Players[0].Hand);
        Assert.Single(ineligible.Players[0].Deck);
    }

    [Fact]
    public async Task OP02_094_BattleKoWatcher_ActivatesAttacker()
    {
        var s = TestScene.New().MyCharacter("OP02-094").Build();
        var me = s.Players[0];
        var isuka = me.Characters[0];
        isuka.IsTapped = true;
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = isuka.Id });
        s.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            AttackerCardId = isuka.Id,
            DefenderPlayerIndex = 1,
            TargetIsLeader = false,
        };

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnAnyCharKOd, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 1, ["reason"] = "battle" });

        Assert.False(isuka.IsTapped);
    }

    [Fact]
    public async Task OP06_048_OpponentEventWatcher_MillsFourCards()
    {
        var s = TestScene.New(myLeaderNumber: "OP03-040")
            .MyCharacter("OP06-048")
            .MyDeckTop("ST30-002", "ST30-003", "ST30-004", "ST30-005")
            .Build();

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnOppEventPlayed,
            new MockPromptService().QueueConfirm(true),
            new Dictionary<string, object?> { ["owner"] = 1 });

        Assert.Empty(s.Players[0].Deck);
        Assert.Equal(4, s.Players[0].Trash.Count);
    }

    [Fact]
    public async Task OP07_097_TurnStartWatcher_PreventsAttack_AndLifePlacementIsFaceUp()
    {
        var s = TestScene.New(myLeaderNumber: "OP07-097")
            .MyActiveDon(1)
            .MyHandAdd("OP07-098")
            .Build();
        var me = s.Players[0];

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnTurnStart, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0 });
        Assert.True(me.Leader.HasRestriction(RestrictionKind.CannotAttack));

        var egghead = me.Hand[0];
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService()
                .QueueConfirm(true)
                .QueueChoose(egghead.Id.ToString())
                .QueueOption(0));

        Assert.Same(egghead, me.LifeArea[0]);
        Assert.True(egghead.IsLifeFaceUp);
    }

    [Fact]
    public async Task OP08_074_EndTurnWatcher_ReturnsExcessDon()
    {
        var s = TestScene.New().MyCharacter("OP08-074").Build();
        var me = s.Players[0];
        for (int i = 0; i < 5; i++) me.DonDeck.Add(new DonCard());
        var maria = me.Characters[0];

        await EffectRuntime.Resolve(s, 0, maria, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.Equal(5, me.CostArea.Count);

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnMyTurnEnd, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0 });

        Assert.Empty(me.CostArea);
        Assert.Equal(5, me.DonDeck.Count);
    }

    [Fact]
    public async Task OP08_101_EndTurnWatcher_ReplacesPaidLifeFromDeck()
    {
        var s = TestScene.New(myLeaderNumber: "OP03-077")
            .MyCharacter("OP08-101")
            .MyDeckTop("ST30-002")
            .Build();
        var me = s.Players[0];
        me.LifeArea.Add(Card("ST30-003"));
        var amande = me.Characters[0];

        await EffectRuntime.Resolve(s, 0, amande, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true));
        Assert.Empty(me.LifeArea);

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnMyTurnEnd, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0 });

        Assert.Single(me.LifeArea);
        Assert.Empty(me.Deck);
        Assert.Single(me.Trash);
    }

    [Fact]
    public async Task OP11_107_FlipsFaceUpLifeDown_ThenActivatesAtEndTurn()
    {
        var s = TestScene.New(myLeaderNumber: "OP11-022")
            .MyCharacter("OP11-107")
            .Build();
        var me = s.Players[0];
        var chonmage = me.Characters[0];
        chonmage.IsTapped = true;
        var life = Card("ST30-002");
        life.IsLifeFaceUp = true;
        me.LifeArea.Add(life);

        await EffectRuntime.Resolve(s, 0, chonmage, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true));
        Assert.False(life.IsLifeFaceUp);
        Assert.True(chonmage.IsTapped);

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnMyTurnEnd, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0 });
        Assert.False(chonmage.IsTapped);
    }

    [Fact]
    public async Task OP13_024_EndTurnWatcher_ActivatesTwoDon()
    {
        var s = TestScene.New()
            .MyCharacter("OP13-024")
            .MyHandAdd("OP13-023")
            .Build();
        var me = s.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var gordon = me.Characters[0];
        var reveal = me.Hand[0];

        await EffectRuntime.Resolve(s, 0, gordon, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(reveal.Id.ToString()));
        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnMyTurnEnd, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0 });

        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
    }

    [Fact]
    public async Task OP13_066_EndTurnWatcher_AddsOneActiveDon()
    {
        var s = TestScene.New()
            .MyCharacter("OP13-066")
            .OppCharacter("ST30-004")
            .Build();
        var me = s.Players[0];
        var rayleigh = me.Characters[0];
        me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });
        me.DonDeck.Add(new DonCard());
        var target = s.Players[1].Characters[0];

        await EffectRuntime.Resolve(s, 0, rayleigh, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.True(target.IsTapped);

        await EffectRuntime.TriggerEvent(s, EffectTrigger.OnMyTurnEnd, new MockPromptService(),
            new Dictionary<string, object?> { ["owner"] = 0 });

        Assert.Equal(2, me.CostArea.Count);
        Assert.Single(me.CostArea.Where(d => d.State == DonState.Active));
        Assert.Empty(me.DonDeck);
    }
}
