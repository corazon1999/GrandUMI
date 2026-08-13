using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class ST21EffectTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ST21_003_OffersStrawHatCharacterWhoseCurrentPowerReaches6000()
    {
        var state = TestScene.New().Build();
        var source = Card("ST21-003");
        var target = Card("ST21-003");
        state.Players[0].Characters.Add(target);
        for (int i = 0; i < 3; i++)
        {
            state.Players[0].CostArea.Add(new DonCard
            {
                State = DonState.Attached,
                AttachedToCardId = target.Id,
            });
        }
        Assert.Equal(3000, target.Info.Power);
        Assert.Equal(6000, state.CurrentPowerOf(0, target));
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var targetPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnCharacter", targetPrompt.kind);
        Assert.Contains(target.Id.ToString(), targetPrompt.choices);
        Assert.Contains(target.GainedKeywords, keyword =>
            keyword.Keyword == "不可阻挡" && keyword.Duration == KeywordDuration.ThisTurn);
    }

    [Theory]
    [InlineData(EffectTrigger.EventMain)]
    [InlineData(EffectTrigger.OnLifeRevealTrigger)]
    public async Task ST21_017_KOsReducedCharacter_WhenOwnCharacterHas6000Power(EffectTrigger trigger)
    {
        var state = TestScene.New().Build();
        var source = Card("ST21-017");
        var qualifyingCharacter = Card("ST30-006");
        var target = Card("ST30-007");
        state.Players[0].Characters.Add(qualifyingCharacter);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService()
            .QueueChoose(target.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, trigger, prompts);

        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
        Assert.Equal(2, prompts.ChooseHistory.Count(h => h.kind == "OpponentCharacter"));
    }

    [Fact]
    public async Task ST21_017_DoesNotOfferKO_WhenOwnCharactersStayBelow6000Power()
    {
        var state = TestScene.New().Build();
        var source = Card("ST21-017");
        var nonQualifyingCharacter = Card("ST30-004");
        var target = Card("ST30-007");
        state.Players[0].Characters.Add(nonQualifyingCharacter);
        state.Players[1].Characters.Add(target);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, prompts);

        Assert.Contains(target, state.Players[1].Characters);
        Assert.DoesNotContain(target, state.Players[1].Trash);
        Assert.Single(prompts.ChooseHistory.Where(h => h.kind == "OpponentCharacter"));
    }
}
