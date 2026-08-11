using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP14_020_MihawkTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task ActivatedMain_DoesNotOfferCharacterWithCannotBeRestedRestrictionAsCost()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var protectedCharacter = Card("OP15-050");
        var validCharacter = Card("OP15-051");
        me.Characters.AddRange([protectedCharacter, validCharacter]);
        AtomicOps.AddRestriction(
            protectedCharacter,
            RestrictionKind.CannotBeRested,
            KeywordDuration.ThisTurn);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(validCharacter.Id.ToString());

        await EffectRuntime.Resolve(
            state,
            0,
            Card("OP14-020"),
            EffectTrigger.ActivatedMain,
            prompts);

        var costPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("RestOwnCardsOrDon", costPrompt.kind);
        Assert.DoesNotContain(protectedCharacter.Id.ToString(), costPrompt.choices);
        Assert.Contains(validCharacter.Id.ToString(), costPrompt.choices);
        Assert.False(protectedCharacter.IsTapped);
        Assert.True(validCharacter.IsTapped);
    }

    [Fact]
    public async Task ActivatedMain_DoesNotOfferCharacterWithContinuousCannotBeRestedBuffAsCost()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var protectedCharacter = Card("OP15-050");
        var validCharacter = Card("OP15-051");
        me.Characters.AddRange([protectedCharacter, validCharacter]);
        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = protectedCharacter.Id.ToString(),
            Scope = new ContinuousScope(),
            GrantRestriction = RestrictionKind.CannotBeRested,
            Predicate = (_, _, card) => card.Id == protectedCharacter.Id,
        });
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(validCharacter.Id.ToString());

        await EffectRuntime.Resolve(
            state,
            0,
            Card("OP14-020"),
            EffectTrigger.ActivatedMain,
            prompts);

        var costPrompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("RestOwnCardsOrDon", costPrompt.kind);
        Assert.DoesNotContain(protectedCharacter.Id.ToString(), costPrompt.choices);
        Assert.Contains(validCharacter.Id.ToString(), costPrompt.choices);
        Assert.False(protectedCharacter.IsTapped);
        Assert.True(validCharacter.IsTapped);
    }
}
