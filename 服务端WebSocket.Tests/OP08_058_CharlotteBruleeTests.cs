using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class OP08_058_CharlotteBruleeTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task AttackEffect_FlipsTopTwoLifeFaceUp_AndRevealsThemToOpponent()
    {
        var state = TestScene.New(myLeaderNumber: "OP08-058").Build();
        var me = state.Players[0];
        var top = Card("OP17-104");
        var second = Card("EB03-051");
        var third = Card("OP17-109");
        me.LifeArea.AddRange([top, second, third]);
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });

        await EffectRuntime.Resolve(
            state,
            0,
            me.Leader,
            EffectTrigger.OnAttackDeclare,
            new MockPromptService().QueueConfirm(true));

        Assert.True(top.IsLifeFaceUp);
        Assert.True(second.IsLifeFaceUp);
        Assert.False(third.IsLifeFaceUp);
        Assert.Empty(me.DonDeck);
        Assert.Equal(DonState.Rest, Assert.Single(me.CostArea).State);

        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(
            StateSnapshotBuilder.Build(state, viewerIndex: 1, lastAction: "Test")));
        var opponentLife = snapshot.RootElement
            .GetProperty("opponent")
            .GetProperty("lifeFaceUp");
        Assert.Equal(3, opponentLife.GetArrayLength());
        Assert.True(opponentLife[0].GetProperty("faceUp").GetBoolean());
        Assert.Equal("OP17-104", opponentLife[0].GetProperty("number").GetString());
        Assert.True(opponentLife[1].GetProperty("faceUp").GetBoolean());
        Assert.Equal("EB03-051", opponentLife[1].GetProperty("number").GetString());
        Assert.False(opponentLife[2].GetProperty("faceUp").GetBoolean());
        Assert.Equal(JsonValueKind.Null, opponentLife[2].GetProperty("number").ValueKind);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task AttackEffect_DoesNotStart_WhenFlipCostCannotBePaid(int lifeCount, bool topAlreadyFaceUp)
    {
        var state = TestScene.New(myLeaderNumber: "OP08-058").Build();
        var me = state.Players[0];
        for (var i = 0; i < lifeCount; i++) me.LifeArea.Add(Card("OP17-104"));
        if (topAlreadyFaceUp) me.LifeArea[0].IsLifeFaceUp = true;
        me.DonDeck.Add(new DonCard { State = DonState.InDeck });
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAttackDeclare, prompts);

        Assert.Empty(prompts.ConfirmHistory);
        Assert.Single(me.DonDeck);
        Assert.Empty(me.CostArea);
    }
}
