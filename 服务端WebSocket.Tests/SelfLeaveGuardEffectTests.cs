using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class SelfLeaveGuardEffectTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    static CardInstance AddOpponentBounceSource(GameState state)
    {
        var source = Card("ST03-009");
        state.Players[1].Characters.Add(source);
        return source;
    }

    static Task ResolveOpponentBounce(GameState state, CardInstance source, MockPromptService prompts)
        => EffectRuntime.Resolve(state, 1, source, EffectTrigger.OnEnterField, prompts);

    [Theory]
    [InlineData("OP07-029")]
    [InlineData("OP07-042")]
    [InlineData("OP12-053")]
    [InlineData("OP12-070")]
    [InlineData("OP13-046")]
    [InlineData("OP14-029")]
    [InlineData("PRB02-002")]
    [InlineData("ST15-005")]
    [InlineData("ST22-005")]
    public void SelfLeaveGuard_IsRegisteredForGenericEffectLeave(string number)
    {
        Assert.True(EffectRuntime.HasEffectForTrigger(Card(number), EffectTrigger.OnAllyWillLeaveField));
    }

    [Fact]
    public async Task OP07_029_RestsOpponentCharacter_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().Build();
        var hawkins = Card("OP07-029");
        state.Players[0].Characters.Add(hawkins);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(hawkins.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(bounceSource.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(hawkins, state.Players[0].Characters);
        Assert.DoesNotContain(hawkins, state.Players[0].Hand);
        Assert.True(bounceSource.IsTapped);
    }

    [Fact]
    public async Task OP07_042_ReturnsOtherCharacterToDeckBottom_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New(myLeaderNumber: "OP01-062").Build();
        var moria = Card("OP07-042");
        var cost = Card("ST30-006");
        state.Players[0].Characters.Add(moria);
        state.Players[0].Characters.Add(cost);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(moria.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(cost.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(moria, state.Players[0].Characters);
        Assert.DoesNotContain(moria, state.Players[0].Hand);
        Assert.DoesNotContain(cost, state.Players[0].Characters);
        Assert.Equal(cost, state.Players[0].Deck.Last());
    }

    [Fact]
    public async Task OP12_053_DiscardsOneHandCard_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().Build();
        var borsalino = Card("OP12-053");
        var cost = Card("ST30-002");
        state.Players[0].Characters.Add(borsalino);
        state.Players[0].Hand.Add(cost);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(borsalino.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(cost.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(borsalino, state.Players[0].Characters);
        Assert.DoesNotContain(borsalino, state.Players[0].Hand);
        Assert.Contains(cost, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP12_070_ReturnsOneDonToDonDeck_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var sanji = Card("OP12-070");
        state.Players[0].Characters.Add(sanji);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(sanji.Id.ToString())
            .QueueConfirm(true);

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(sanji, state.Players[0].Characters);
        Assert.DoesNotContain(sanji, state.Players[0].Hand);
        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
    }

    [Fact]
    public async Task OP13_046_DiscardsWhitebeardCard_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().Build();
        var vista = Card("OP13-046");
        var cost = Card("ST15-005");
        state.Players[0].Characters.Add(vista);
        state.Players[0].Hand.Add(cost);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(vista.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(cost.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(vista, state.Players[0].Characters);
        Assert.DoesNotContain(vista, state.Players[0].Hand);
        Assert.Contains(cost, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP14_029_RestsOwnCardDuringOpponentTurn_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        state.CurrentTurnPlayer = 1;
        var tashigi = Card("OP14-029");
        state.Players[0].Characters.Add(tashigi);
        var don = Assert.Single(state.Players[0].CostArea);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(tashigi.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(don.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(tashigi, state.Players[0].Characters);
        Assert.DoesNotContain(tashigi, state.Players[0].Hand);
        Assert.Equal(DonState.Rest, don.State);
    }

    [Fact]
    public async Task PRB02_002_LosesPower_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().Build();
        var law = Card("PRB02-002");
        state.Players[0].Characters.Add(law);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(law.Id.ToString())
            .QueueConfirm(true);

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(law, state.Players[0].Characters);
        Assert.DoesNotContain(law, state.Players[0].Hand);
        Assert.Equal(-2000, law.PowerModThisTurn);
    }

    [Fact]
    public async Task ST15_005_LosesPower_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().Build();
        var ace = Card("ST15-005");
        state.Players[0].Characters.Add(ace);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(ace.Id.ToString())
            .QueueConfirm(true);

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(ace, state.Players[0].Characters);
        Assert.DoesNotContain(ace, state.Players[0].Hand);
        Assert.Equal(-2000, ace.PowerModThisTurn);
    }

    [Fact]
    public async Task ST22_005_DiscardsTwoHandCards_InsteadOfReturningSelfToHand()
    {
        var state = TestScene.New().Build();
        var oden = Card("ST22-005");
        var firstCost = Card("ST30-002");
        var secondCost = Card("ST30-003");
        state.Players[0].Characters.Add(oden);
        state.Players[0].Hand.Add(firstCost);
        state.Players[0].Hand.Add(secondCost);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(oden.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(firstCost.Id.ToString(), secondCost.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(oden, state.Players[0].Characters);
        Assert.DoesNotContain(oden, state.Players[0].Hand);
        Assert.Contains(firstCost, state.Players[0].Trash);
        Assert.Contains(secondCost, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP14_029_ActivatedPowerLastsUntilNextOpponentEndPhase()
    {
        var state = TestScene.New().MyActiveDon(2).Build();
        var tashigi = Card("OP14-029");
        state.Players[0].Characters.Add(tashigi);
        var dons = state.Players[0].CostArea.ToList();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(dons[0].Id.ToString(), dons[1].Id.ToString());

        await EffectRuntime.Resolve(state, 0, tashigi, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(2000, Assert.Single(tashigi.PowerModsUntilOppEnd).Delta);
        Assert.Equal(tashigi.Info.Power + 2000, state.CurrentPowerOf(0, tashigi));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Single(tashigi.PowerModsUntilOppEnd);

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Empty(tashigi.PowerModsUntilOppEnd);
    }

    [Fact]
    public async Task PRB02_002_DoesNotProtectAgainstBattleKO()
    {
        var state = TestScene.New().Build();
        var law = Card("PRB02-002");
        state.Players[0].Characters.Add(law);
        state.KOReason = "battle";
        state.KOActingSide = 1;
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, law, EffectTrigger.PreKO, prompts);

        Assert.Empty(prompts.ConfirmHistory);
        Assert.Equal(0, law.PowerModThisTurn);
        Assert.DoesNotContain(law.Id, state.PreventKOCardIds);
    }
}
