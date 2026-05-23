using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// A 阶段 P0 / B 阶段 P1 新增原子操作的单元测试
/// </summary>
public class NewAtomicOpsTests
{
    public NewAtomicOpsTests() { TestScene.New(); /* 触发 CardDB 加载 */ }

    [Fact]
    public void RefreshDonFromDeck_AddsActiveDon_RespectsMaxAndDeck()
    {
        var s = TestScene.New().Build();
        var p = s.Players[0];
        // 初始空 CostArea + 10 张 DonDeck
        for (int i = 0; i < 10; i++) p.DonDeck.Add(new DonCard());

        int added = AtomicOps.RefreshDonFromDeck(p, 3);
        Assert.Equal(3, added);
        Assert.Equal(3, p.CostArea.Count(d => d.State == DonState.Active));
        Assert.Equal(7, p.DonDeck.Count);

        // 上限 10：填满后追加无效
        AtomicOps.RefreshDonFromDeck(p, 10);
        Assert.Equal(10, p.CostArea.Count);
    }

    [Fact]
    public void SetPowerThisTurn_ChangesAbsoluteValue_RegardlessOfBase()
    {
        var s = TestScene.New().MyCharacter("OP15-003").Build();
        var ch = s.Players[0].Characters[0]; // base 6000
        AtomicOps.SetPowerThisTurn(ch, 0, donAttached: 0, ownerTurn: true);
        Assert.Equal(0, ch.CurrentPower(0, ownerTurn: true));
        AtomicOps.SetPowerThisTurn(ch, 4000, donAttached: 0, ownerTurn: true);
        Assert.Equal(4000, ch.CurrentPower(0, ownerTurn: true));
    }

    [Fact]
    public void ReturnFieldToDeckBottom_MovesCard_AndReleasesDon()
    {
        var s = TestScene.New().MyCharacter("OP15-003").Build();
        var p = s.Players[0];
        var ch = p.Characters[0];
        var don = new DonCard { State = DonState.Attached, AttachedToCardId = ch.Id };
        p.CostArea.Add(don);
        p.Deck.Add(new CardInstance { Info = CardDatabase.Get("OP15-004")! }); // 卡组里有 1 张

        AtomicOps.ReturnFieldToDeckBottom(s, 0, ch);

        Assert.DoesNotContain(ch, p.Characters);
        Assert.Equal(ch, p.Deck[^1]);
        Assert.Equal(DonState.Rest, don.State);
        Assert.Null(don.AttachedToCardId);
    }

    [Fact]
    public void PlayFromTrashFree_RestoresCharacter_FromTrashToField()
    {
        var s = TestScene.New().Build();
        var info = CardDatabase.Get("OP15-003")!;
        var card = new CardInstance { Info = info, IsTapped = true, PowerModThisTurn = 1234 };
        s.Players[0].Trash.Add(card);

        AtomicOps.PlayFromTrashFree(s, 0, card, restState: false);

        Assert.Contains(card, s.Players[0].Characters);
        Assert.DoesNotContain(card, s.Players[0].Trash);
        Assert.False(card.IsTapped);
        Assert.Equal(0, card.PowerModThisTurn);  // 临时态被清
    }

    [Fact]
    public void AddPowerToAllThisTurn_OnlyAffectsMatchingCards()
    {
        var s = TestScene.New().MyCharacter("OP15-003").MyCharacter("OP15-004").Build();
        // 让 OP15-003 有 keyword "东海"，OP15-004 没有（OP15-003 实际有"东海"）
        AtomicOps.AddPowerToAllThisTurn(s, 0,
            filter: c => c.Info.HasKeyword("东海"),
            delta: 2000,
            includeLeader: false);

        var c003 = s.Players[0].Characters[0];
        var c004 = s.Players[0].Characters[1];
        // OP15-003 爱比达有"东海/爱比达海盗团"
        Assert.Equal(2000, c003.PowerModThisTurn);
        // OP15-004 海猫有"动物/阿拉巴斯坦王国"，没"东海"
        Assert.Equal(0, c004.PowerModThisTurn);
    }

    [Fact]
    public void AddLifeFromDeckTop_AddsToTopOfLife()
    {
        var s = TestScene.New().Build();
        var p = s.Players[0];
        var c1 = new CardInstance { Info = CardDatabase.Get("OP15-003")! };
        var c2 = new CardInstance { Info = CardDatabase.Get("OP15-004")! };
        p.Deck.Add(c1); p.Deck.Add(c2);
        int origLife = p.LifeCount;

        int added = AtomicOps.AddLifeFromDeckTop(p, 2);
        Assert.Equal(2, added);
        Assert.Equal(origLife + 2, p.LifeCount);
        // 后加的（c2）在更深层；c1 是最上方（先入栈），最上方实际上是最后调用 Insert(0,...) 的 → c2
        Assert.Equal(c2, p.LifeArea[0]);
    }

    [Fact]
    public void AddRestriction_BlocksCannotAttack()
    {
        var s = TestScene.New().MyCharacter("OP15-003").Build();
        var ch = s.Players[0].Characters[0];
        AtomicOps.AddRestriction(ch, RestrictionKind.CannotAttack, KeywordDuration.ThisTurn);
        Assert.True(ch.HasRestriction(RestrictionKind.CannotAttack));
        // 回合结束清掉
        TurnEngine.EnterEndPhase(s);
        Assert.False(ch.HasRestriction(RestrictionKind.CannotAttack));
    }

    [Fact]
    public void NullifyEffects_SkipsTriggerDetection()
    {
        var s = TestScene.New().MyCharacter("OP15-003").Build();
        var ch = s.Players[0].Characters[0];
        // OP15-003 有 PreKO 文本（"将要被KO的场合"）
        Assert.True(EffectRuntime.HasEffectForTrigger(ch, EffectTrigger.PreKO));
        AtomicOps.NullifyEffects(ch, KeywordDuration.ThisTurn);
        Assert.False(EffectRuntime.HasEffectForTrigger(ch, EffectTrigger.PreKO));
    }

    [Fact]
    public void ContinuousPowerBonus_AppliesWhenPredicateTrue()
    {
        var s = TestScene.New().MyCharacter("OP15-003").Build();
        var ch = s.Players[0].Characters[0];
        s.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = ch.Id.ToString(),
            Scope = new ContinuousScope(),
            PowerDelta = 1500,
            Predicate = (_, _, _) => true,
        });
        int powered = s.CurrentPowerOf(0, ch);
        Assert.Equal(ch.Info.Power + 1500, powered);
    }
}
