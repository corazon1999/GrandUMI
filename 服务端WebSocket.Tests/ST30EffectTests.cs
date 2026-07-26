using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ST30EffectTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ST30_004_RevealsTwoPower6000Characters_BeforeDrawAndDiscard()
    {
        var s = TestScene.New()
            .MyDeckTop("ST30-002", "ST30-003", "ST30-005")
            .Build();
        var me = s.Players[0];
        var ivankov = Card("ST30-004");
        var first = Card("ST30-006");
        var second = Card("ST30-007");
        me.Characters.Add(ivankov);
        me.Hand.Add(first);
        me.Hand.Add(second);

        var prompts = new MockPromptService()
            .QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueChoose(first.Id.ToString(), second.Id.ToString());

        await EffectRuntime.Resolve(s, 0, ivankov, EffectTrigger.OnEnterField, prompts);

        var reveal = Assert.Single(prompts.ChooseHistory.Where(h => h.kind == "RevealOwnHand"));
        Assert.Equal(2, reveal.max);
        Assert.Contains(first.Id.ToString(), reveal.choices);
        Assert.Contains(second.Id.ToString(), reveal.choices);
        Assert.Equal(3, me.Hand.Count); // 原有2张，抽3张，再丢2张
        Assert.Equal(2, me.Trash.Count);
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task ST30_006_DiscardCost_OnlyAllowsPower6000Character()
    {
        var s = TestScene.New()
            .MyDeckTop("ST30-002", "ST30-003")
            .Build();
        var me = s.Players[0];
        var jinbe = Card("ST30-006");
        var valid = Card("ST30-007");
        var invalid = Card("ST30-004");
        me.Characters.Add(jinbe);
        me.Hand.Add(valid);
        me.Hand.Add(invalid);
        var prompts = new MockPromptService().QueueChoose(valid.Id.ToString());

        await EffectRuntime.Resolve(s, 0, jinbe, EffectTrigger.OnEnterField, prompts);

        var discard = Assert.Single(prompts.ChooseHistory.Where(h => h.kind == "DiscardOwnChosen"));
        Assert.Contains(valid.Id.ToString(), discard.choices);
        Assert.DoesNotContain(invalid.Id.ToString(), discard.choices);
        Assert.Contains(valid, me.Trash);
        Assert.Contains(invalid, me.Hand);
        Assert.Empty(me.Deck);
    }

    [Fact]
    public async Task ST30_008_DiscardCost_OnlyAllowsPower6000Character_AndReturnsRested()
    {
        var s = TestScene.New().Build();
        var me = s.Players[0];
        var marco = Card("ST30-008");
        var valid = Card("ST30-006");
        var invalid = Card("ST30-004");
        me.Trash.Add(marco);
        me.Hand.Add(valid);
        me.Hand.Add(invalid);
        var prompts = new MockPromptService().QueueChoose(valid.Id.ToString());

        await EffectRuntime.Resolve(s, 0, marco, EffectTrigger.OnKO, prompts);

        var discard = Assert.Single(prompts.ChooseHistory.Where(h => h.kind == "DiscardOwnChosen"));
        Assert.Contains(valid.Id.ToString(), discard.choices);
        Assert.DoesNotContain(invalid.Id.ToString(), discard.choices);
        Assert.Contains(valid, me.Trash);
        Assert.Contains(marco, me.Characters);
        Assert.True(marco.IsTapped);
    }

    [Fact]
    public async Task ST30_009_PreventsOpponentEffectBounce_ByTrashingSelfAndDrawing()
    {
        var s = TestScene.New().MyDeckTop("ST30-002").Build();
        var me = s.Players[0];
        var guard = Card("ST30-009");
        var victim = Card("ST30-006");
        var opponentSource = Card("ST03-009");
        me.Characters.Add(guard);
        me.Characters.Add(victim);
        s.Players[1].Characters.Add(opponentSource);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);

        Assert.True(EffectRuntime.HasEffectForTrigger(guard, EffectTrigger.OnAllyWillLeaveField));
        await EffectRuntime.Resolve(s, 1, opponentSource, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(victim, me.Characters);
        Assert.DoesNotContain(victim, me.Hand);
        Assert.Contains(guard, me.Trash);
        Assert.Single(me.Hand); // 小奥兹Jr.的成本结算后抽1张
    }

    [Fact]
    public async Task ST30_011_PreventsOpponentEffectBounce_ByRestingSelf()
    {
        var s = TestScene.New().Build();
        var me = s.Players[0];
        var guard = Card("ST30-011");
        var victim = Card("ST30-006");
        var opponentSource = Card("ST03-009");
        me.Characters.Add(guard);
        me.Characters.Add(victim);
        s.Players[1].Characters.Add(opponentSource);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);

        Assert.True(EffectRuntime.HasEffectForTrigger(guard, EffectTrigger.OnAllyWillLeaveField));
        await EffectRuntime.Resolve(s, 1, opponentSource, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(victim, me.Characters);
        Assert.DoesNotContain(victim, me.Hand);
        Assert.True(guard.IsTapped);
    }

    [Fact]
    public async Task ST30_015_CounterRequiresTwoOriginalPower6000Characters()
    {
        var eligible = TestScene.New()
            .MyCharacter("ST30-006")
            .MyCharacter("ST30-007")
            .Build();
        var eligibleEvent = Card("ST30-015");
        await EffectRuntime.Resolve(eligible, 0, eligibleEvent, EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(eligible.Players[0].Leader.Id.ToString()));
        Assert.Equal(4000, eligible.Players[0].Leader.PowerModThisBattle);

        var ineligible = TestScene.New().MyCharacter("ST30-006").Build();
        var ineligiblePrompts = new MockPromptService();
        await EffectRuntime.Resolve(ineligible, 0, Card("ST30-015"), EffectTrigger.EventCounter, ineligiblePrompts);
        Assert.Equal(0, ineligible.Players[0].Leader.PowerModThisBattle);
        Assert.Empty(ineligiblePrompts.ChooseHistory);
    }

    [Fact]
    public async Task ST30_016_AlwaysAddsPower_AndDrawsOnlyWithPower6000AceAndLuffy()
    {
        var eligible = TestScene.New()
            .MyCharacter("ST30-007")
            .MyCharacter("ST30-012")
            .MyDeckTop("ST30-002")
            .Build();
        await EffectRuntime.Resolve(eligible, 0, Card("ST30-016"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(eligible.Players[0].Leader.Id.ToString()));
        Assert.Equal(3000, eligible.Players[0].Leader.PowerModThisBattle);
        Assert.Single(eligible.Players[0].Hand);

        var ineligible = TestScene.New()
            .MyCharacter("ST30-012")
            .MyDeckTop("ST30-002")
            .Build();
        await EffectRuntime.Resolve(ineligible, 0, Card("ST30-016"), EffectTrigger.EventCounter,
            new MockPromptService().QueueChoose(ineligible.Players[0].Leader.Id.ToString()));
        Assert.Equal(3000, ineligible.Players[0].Leader.PowerModThisBattle);
        Assert.Empty(ineligible.Players[0].Hand);
        Assert.Single(ineligible.Players[0].Deck);
    }
}
