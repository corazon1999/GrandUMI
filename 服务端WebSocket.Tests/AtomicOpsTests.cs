using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class AtomicOpsTests
{
    [Fact]
    public void AddPowerThisTurn_AccumulatesAndCleansAtEnd()
    {
        var s = TestScene.New().MyCharacter("OP15-003").Build();
        var ch = s.Players[0].Characters[0];
        int basePower = ch.Info.Power;
        AtomicOps.AddPowerThisTurn(ch, 2000);
        Assert.Equal(basePower + 2000, ch.CurrentPower(0, ownerTurn: true));

        // 回合结束清除
        TurnEngine.EnterEndPhase(s);
        Assert.Equal(0, ch.PowerModThisTurn);
    }

    [Fact]
    public void KO_MovesCardToTrash_AndReleasesDon()
    {
        var s = TestScene.New().MyCharacter("OP15-003").Build();
        var p = s.Players[0];
        var ch = p.Characters[0];
        // 给该角色附 1 张咚
        var don = new DonCard { State = DonState.Attached, AttachedToCardId = ch.Id };
        p.CostArea.Add(don);

        AtomicOps.KO(s, 0, ch);

        Assert.DoesNotContain(ch, p.Characters);
        Assert.Contains(ch, p.Trash);
        Assert.Equal(DonState.Rest, don.State);
        Assert.Null(don.AttachedToCardId);
    }

    [Fact]
    public void Draw_TakesTopOfDeck_OrEndsGame()
    {
        var s = TestScene.New().Build();
        var info = Cards.CardDatabase.GetBySet("OP15").First(c => c.Kind != Cards.CardKind.Leader);
        s.Players[0].Deck.Add(new CardInstance { Info = info });
        AtomicOps.Draw(s, 0, 1);
        Assert.Single(s.Players[0].Hand);

        // 卡组空抽 = 判负
        AtomicOps.Draw(s, 0, 1);
        Assert.True(s.IsGameOver);
        Assert.Equal(1, s.WinnerIndex);
    }
}
