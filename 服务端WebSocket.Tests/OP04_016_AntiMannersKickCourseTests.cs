using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// OP04-016 反礼仪踢技套餐回归测试。
/// </summary>
public class OP04_016_AntiMannersKickCourseTests
{
    [Fact]
    public async Task EventCounter_CanDiscardOneCardAndChooseOwnLeader()
    {
        var state = TestScene.New()
            .MyHandAdd("OP01-006")
            .MyCharacter("OP01-006")
            .Build();
        var leader = state.Players[0].Leader;
        var character = state.Players[0].Characters[0];
        var discarded = state.Players[0].Hand[0];
        var source = new CardInstance { Info = CardDatabase.Get("OP04-016")! };
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discarded.Id.ToString())
            .QueueChoose(leader.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Equal(2, prompts.ChooseHistory.Count);
        var discardPrompt = prompts.ChooseHistory[0];
        Assert.Equal("DiscardOwnChosen", discardPrompt.kind);
        Assert.Contains(discarded.Id.ToString(), discardPrompt.choices);

        var targetPrompt = prompts.ChooseHistory[1];
        Assert.Equal("OwnLeaderOrCharacter", targetPrompt.kind);
        Assert.Equal("选择己方最多1张领袖或角色，本次战斗力量+3000", targetPrompt.text);
        Assert.Contains(leader.Id.ToString(), targetPrompt.choices);
        Assert.Contains(character.Id.ToString(), targetPrompt.choices);

        Assert.DoesNotContain(discarded, state.Players[0].Hand);
        Assert.Contains(discarded, state.Players[0].Trash);
        Assert.Equal(3000, leader.PowerModThisBattle);
        Assert.Equal(0, character.PowerModThisBattle);
    }
}
