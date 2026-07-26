using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class AllyLeaveGuardEffectTests
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
    [InlineData("OP11-001")]
    [InlineData("OP12-102")]
    [InlineData("OP14-061")]
    [InlineData("OP15-098")]
    [InlineData("OP16-014")]
    [InlineData("ST25-003")]
    public void AllyLeaveGuard_IsRegisteredForGenericEffectLeave(string number)
    {
        Assert.True(EffectRuntime.HasEffectForTrigger(Card(number), EffectTrigger.OnAllyWillLeaveField));
    }

    [Fact]
    public async Task OP11_001_ReturnsThreeTrashCardsInChosenOrder_AndProtectsNavyCharacter()
    {
        var state = TestScene.New(myLeaderNumber: "OP11-001").Build();
        var victim = Card("EB01-049");
        var first = Card("ST30-002");
        var second = Card("ST30-003");
        var third = Card("ST30-004");
        state.Players[0].Characters.Add(victim);
        state.Players[0].Trash.AddRange([first, second, third]);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(third.Id.ToString(), first.Id.ToString(), second.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(victim, state.Players[0].Characters);
        Assert.DoesNotContain(victim, state.Players[0].Hand);
        Assert.Empty(state.Players[0].Trash);
        Assert.Equal([third, first, second], state.Players[0].Deck);
    }

    [Fact]
    public async Task OP12_102_FlipsTopLifeFaceUp_AndProtectsCostSixOrLessCharacter()
    {
        var state = TestScene.New().Build();
        var shirahoshi = Card("OP12-102");
        var victim = Card("EB01-049");
        var life = Card("ST30-002");
        state.Players[0].Characters.Add(shirahoshi);
        state.Players[0].Characters.Add(victim);
        state.Players[0].LifeArea.Add(life);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(victim, state.Players[0].Characters);
        Assert.DoesNotContain(victim, state.Players[0].Hand);
        Assert.True(life.IsLifeFaceUp);
    }

    [Fact]
    public async Task OP14_061_ReturnsOneDon_AndProtectsDonquixoteCharacter()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var vergo = Card("OP14-061");
        var victim = Card("OP14-062");
        state.Players[0].Characters.Add(vergo);
        state.Players[0].Characters.Add(victim);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(victim, state.Players[0].Characters);
        Assert.DoesNotContain(victim, state.Players[0].Hand);
        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
    }

    [Fact]
    public async Task OP15_098_TakesTopLife_AndProtectsHighPowerSkyIslandCharacter()
    {
        var state = TestScene.New(myLeaderNumber: "OP15-098").Build();
        var victim = Card("OP15-099");
        var life = Card("ST30-002");
        life.IsLifeFaceUp = true;
        state.Players[0].Characters.Add(victim);
        state.Players[0].LifeArea.Add(life);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true);

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(victim, state.Players[0].Characters);
        Assert.DoesNotContain(victim, state.Players[0].Hand);
        Assert.Empty(state.Players[0].LifeArea);
        Assert.Contains(life, state.Players[0].Hand);
        Assert.False(life.IsLifeFaceUp);
    }

    [Fact]
    public async Task OP16_014_KOsSelf_TriggersOnKO_AndProtectsOriginalCharacter()
    {
        var state = TestScene.New().Build();
        var marco = Card("OP16-014");
        var victim = Card("OP11-002");
        var reviveCost = Card("OP16-014");
        state.Players[0].Characters.Add(marco);
        state.Players[0].Characters.Add(victim);
        state.Players[0].Hand.Add(reviveCost);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true)
            .QueueConfirm(true)
            .QueueChoose(reviveCost.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(victim, state.Players[0].Characters);
        Assert.DoesNotContain(victim, state.Players[0].Hand);
        Assert.Contains(marco, state.Players[0].Characters);
        Assert.Contains(reviveCost, state.Players[0].Trash);
        Assert.DoesNotContain(marco, state.Players[0].Trash);
    }

    [Fact]
    public async Task ST25_003_DiscardsOneHandCard_AndProtectsCrossGuildCharacter()
    {
        var state = TestScene.New().Build();
        var guard = Card("ST25-003");
        var victim = Card("ST25-004");
        var cost = Card("ST30-002");
        state.Players[0].Characters.Add(guard);
        state.Players[0].Characters.Add(victim);
        state.Players[0].Hand.Add(cost);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(cost.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.Contains(victim, state.Players[0].Characters);
        Assert.DoesNotContain(victim, state.Players[0].Hand);
        Assert.Contains(cost, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP11_001_AlsoProtectsAgainstOpponentEffectKO()
    {
        var state = TestScene.New(myLeaderNumber: "OP11-001").Build();
        var victim = Card("EB01-049");
        var first = Card("ST30-002");
        var second = Card("ST30-003");
        var third = Card("ST30-004");
        state.Players[0].Characters.Add(victim);
        state.Players[0].Trash.AddRange([first, second, third]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(first.Id.ToString(), second.Id.ToString(), third.Id.ToString());

        bool wasKOd = await AtomicOps.KOByEffectAsync(state, 0, victim, prompts, actingSide: 1);

        Assert.False(wasKOd);
        Assert.Contains(victim, state.Players[0].Characters);
        Assert.DoesNotContain(victim, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP15_098_DoesNotProtectNonSkyIslandCharacter()
    {
        var state = TestScene.New(myLeaderNumber: "OP15-098").Build();
        var victim = Card("OP11-002");
        var life = Card("ST30-002");
        state.Players[0].Characters.Add(victim);
        state.Players[0].LifeArea.Add(life);
        var bounceSource = AddOpponentBounceSource(state);
        var prompts = new MockPromptService().QueueChoose(victim.Id.ToString());

        await ResolveOpponentBounce(state, bounceSource, prompts);

        Assert.DoesNotContain(victim, state.Players[0].Characters);
        Assert.Contains(victim, state.Players[0].Hand);
        Assert.Contains(life, state.Players[0].LifeArea);
        Assert.Empty(prompts.ConfirmHistory);
    }
}
