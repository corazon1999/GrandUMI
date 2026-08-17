using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP07_078_MillionTonKyubiKaisokuTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 主要效果可将休息状态的福克斯领航转为活跃()
    {
        var state = TestScene.New("OP07-059")
            .MyActiveDon(3)
            .OppActiveDon(3)
            .Build();
        var leader = state.Players[0].Leader;
        leader.IsTapped = true;
        var prompts = new MockPromptService().QueueChoose(leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP07-078"), EffectTrigger.EventMain, prompts);

        Assert.False(leader.IsTapped);
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnLeaderOrCharacter", prompt.kind);
        Assert.Contains(leader.Id.ToString(), prompt.choices);
    }
}
