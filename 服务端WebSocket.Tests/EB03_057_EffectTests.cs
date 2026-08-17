using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>EB03-057 大和的登场时与 KO 时效果回归测试。</summary>
public class EB03_057_EffectTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OnEnterField_WithWanoLeader_AttachesThreeRestedDonToLeader()
    {
        var state = TestScene.New("OP06-022").Build();
        var player = state.Players[0];
        for (int i = 0; i < 4; i++)
            player.CostArea.Add(new DonCard { State = DonState.Rest });
        player.CostArea.Add(new DonCard { State = DonState.Active });
        player.DonDeck.Add(new DonCard());
        int donDeckCountBefore = player.DonDeck.Count;

        await EffectRuntime.Resolve(
            state, 0, Card("EB03-057"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(3, player.CostArea.Count(d =>
            d.State == DonState.Attached && d.AttachedToCardId == player.Leader.Id));
        Assert.Single(player.CostArea.Where(d => d.State == DonState.Rest));
        Assert.Single(player.CostArea.Where(d => d.State == DonState.Active));
        Assert.Equal(donDeckCountBefore, player.DonDeck.Count);
    }

    [Fact]
    public async Task OnEnterField_WithoutWanoLeader_DoesNotAttachDon()
    {
        var state = TestScene.New("OP15-001").Build();
        var player = state.Players[0];
        for (int i = 0; i < 3; i++)
            player.CostArea.Add(new DonCard { State = DonState.Rest });

        await EffectRuntime.Resolve(
            state, 0, Card("EB03-057"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(3, player.CostArea.Count(d => d.State == DonState.Rest));
        Assert.DoesNotContain(player.CostArea, d => d.State == DonState.Attached);
    }

    [Fact]
    public async Task OnKO_WhenDeclined_LeavesOpponentTopLifeInPlace()
    {
        var state = TestScene.New().MyCharacter("EB03-057").Build();
        var yamato = state.Players[0].Characters[0];
        var life = Card("OP15-003");
        state.Players[1].LifeArea.Add(life);
        var prompts = new MockPromptService().QueueConfirm(false);

        await BattleEngine.KOCardAsync(state, 0, yamato, prompts);

        Assert.Same(life, Assert.Single(state.Players[1].LifeArea));
        Assert.DoesNotContain(life, state.Players[1].Trash);
        Assert.Single(prompts.ConfirmHistory);
    }
}
