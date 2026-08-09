using GrandUMI.Effects;
using Xunit;

namespace GrandUMI.Tests;

public class OP12_040_KuzanTests
{
    [Fact]
    public async Task NavyEffectCostDiscard_TriggersDraw()
    {
        var state = TestScene.New("OP12-040")
            .MyCharacter("OP06-043")
            .MyHandAdd("OP15-003")
            .MyDeckTop("OP15-004")
            .Build();
        var aramaki = state.Players[0].Characters[0];
        var discarded = state.Players[0].Hand[0];
        var drawn = state.Players[0].Deck[0];
        var prompts = new MockPromptService()
            .QueueChoose(discarded.Id.ToString())
            .QueueChooseEmpty();

        await EffectRuntime.Resolve(
            state, 0, aramaki, EffectTrigger.ActivatedMain, prompts);

        Assert.Contains(discarded, state.Players[0].Trash);
        Assert.Contains(drawn, state.Players[0].Hand);
        Assert.Empty(state.Players[0].Deck);
    }

    [Fact]
    public async Task NonNavyEffectCostDiscard_DoesNotTriggerDraw()
    {
        var state = TestScene.New("OP12-040")
            .MyDeckTop("OP15-004")
            .Build();
        var leader = state.Players[0].Leader;
        var payload = new Dictionary<string, object?>
        {
            ["owner"] = 0,
            ["sourceNumber"] = "OP15-003",
            ["actingSide"] = 0,
            ["isCost"] = true,
        };

        await EffectRuntime.Resolve(
            state, 0, leader, EffectTrigger.OnHandDiscarded,
            new MockPromptService(), payload);

        Assert.Empty(state.Players[0].Hand);
        Assert.Single(state.Players[0].Deck);
    }

    [Fact]
    public async Task OpponentsNavyEffectDiscard_DoesNotTriggerDraw()
    {
        var state = TestScene.New("OP12-040")
            .MyDeckTop("OP15-004")
            .Build();
        var leader = state.Players[0].Leader;
        var payload = new Dictionary<string, object?>
        {
            ["owner"] = 0,
            ["sourceNumber"] = "OP06-043",
            ["actingSide"] = 1,
            ["isCost"] = true,
        };

        await EffectRuntime.Resolve(
            state, 0, leader, EffectTrigger.OnHandDiscarded,
            new MockPromptService(), payload);

        Assert.Empty(state.Players[0].Hand);
        Assert.Single(state.Players[0].Deck);
    }
}
