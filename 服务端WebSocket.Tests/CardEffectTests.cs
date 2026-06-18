using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// 卡牌效果单元测试模板
///
/// 用法：每张卡一个 [Fact]，构造 GameState → 触发对应入口 → 断言状态变化
///   构造：TestScene.New(myLeader, oppLeader).MyHandAdd/OppCharacter/AttachDon*/MyDeckTop...
///   触发：EffectRuntime.Resolve(state, ownerIdx, sourceCardInstance, trigger, mockPrompts)
///   断言：检查 PowerMod/IsTapped/Hand.Count/Deck.Count/Trash.Contains/PreventKOCardIds 等
/// </summary>
public class CardEffectTests
{
    /// <summary>
    /// OP15-004 海猫
    /// DSL: 【登场时】我方领袖力量≤0 → 对方最多 1 张角色力量 -3000
    /// </summary>
    [Fact]
    public async Task OP15_004_HaiMao_OnEnter_ReducesOppCharBy3000_WhenLeaderPowerLeZero()
    {
        var s = TestScene.New()
            .OppCharacter("OP15-050")        // 任意对方角色作为目标
            .Build();

        // 把我方领航力量临时降到 0（满足 leaderPowerNotMoreThan: 0 条件）
        s.Players[0].Leader.PowerModThisTurn = -s.Players[0].Leader.Info.Power;

        // 把海猫直接放上场，模拟"登场后触发"
        var haiMao = new CardInstance { Info = CardDatabase.Get("OP15-004")!, TurnPlayed = s.TurnCount };
        s.Players[0].Characters.Add(haiMao);

        var mock = new MockPromptService()
            .QueueChoose(s.Players[1].Characters[0].Id.ToString());

        await EffectRuntime.Resolve(s, 0, haiMao, EffectTrigger.OnEnterField, mock);

        Assert.Equal(-3000, s.Players[1].Characters[0].PowerModThisTurn);
    }

    /// <summary>
    /// OP15-074 放电（事件）
    /// DSL main: cost{donReturn:1}, then [Draw 1]
    /// </summary>
    [Fact]
    public async Task OP15_074_FangDian_EventMain_PaysDonAndDraws1()
    {
        var s = TestScene.New()
            .MyActiveDon(2)
            .MyDeckTop("OP15-050", "OP15-051")
            .Build();

        int handBefore = s.Players[0].Hand.Count;
        int deckBefore = s.Players[0].Deck.Count;
        int activeDonBefore = s.Players[0].ActiveDonCount;

        var card = new CardInstance { Info = CardDatabase.Get("OP15-074")! };
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.EventMain, new MockPromptService());

        Assert.Equal(handBefore + 1, s.Players[0].Hand.Count);
        Assert.Equal(deckBefore - 1, s.Players[0].Deck.Count);
        Assert.Equal(activeDonBefore - 1, s.Players[0].ActiveDonCount);
    }

    /// <summary>
    /// OP15-001 克里克（领航）
    /// 手写脚本 ActivatedMain：将对方最多 1 张被赋予 ≥2 咚的角色转休息（每回合 1 次，需自身被赋 ≥1 咚）
    /// </summary>
    [Fact]
    public async Task OP15_001_Krieg_Activated_RestsOppCharWith2PlusDon_OncePerTurn()
    {
        var s = TestScene.New(myLeaderNumber: "OP15-001")
            .OppCharacter("OP15-050")        // 目标 0：被赋予 2 咚
            .OppCharacter("OP15-051")        // 目标 1：未被赋予
            .AttachDonToOppChar(0, 2)
            .AttachDonToMyLeader(1)          // 满足【咚!!×1】条件
            .Build();

        var target = s.Players[1].Characters[0];
        Assert.False(target.IsTapped);

        var mock = new MockPromptService()
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.ActivatedMain, mock);
        Assert.True(target.IsTapped);

        // 再触发一次 → 每回合 1 次锁应阻止
        target.IsTapped = false;
        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(target.Id.ToString()));
        Assert.False(target.IsTapped);  // 没生效
    }

    /// <summary>
    /// OP15-003 爱比达
    /// PreKO 拦截：丢手牌中 1 张力量 ≤6000 的角色 → MarkPreventKO 取消本次 KO
    /// </summary>
    [Fact]
    public async Task OP15_003_Apis_PreKO_DiscardsHandCard_AndMarksPreventKO()
    {
        var s = TestScene.New()
            .MyHandAdd("OP15-050")            // 任意手牌角色（力量 ≤6000 假定满足）
            .Build();

        var apis = new CardInstance { Info = CardDatabase.Get("OP15-003")! };
        s.Players[0].Characters.Add(apis);

        var handCard = s.Players[0].Hand[0];
        int handBefore = s.Players[0].Hand.Count;
        int trashBefore = s.Players[0].Trash.Count;

        var mock = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(handCard.Id.ToString());

        await EffectRuntime.Resolve(s, 0, apis, EffectTrigger.PreKO, mock);

        Assert.Equal(handBefore - 1, s.Players[0].Hand.Count);
        Assert.Equal(trashBefore + 1, s.Players[0].Trash.Count);
        Assert.Contains(apis.Id, s.State_PreventKOCardIds());
    }

    /// <summary>
    /// OP15-019 防护罩 蛮牛突进（事件）
    /// DSL main: AddPowerThisTurn selfLeader +1000（gen-dsl 简化版，未含抽 1）
    /// </summary>
    [Fact]
    public async Task OP15_019_HuLianBangCun_EventMain_GivesLeaderPlus1000()
    {
        // 卡组备 1 张供新增的【主要】"抽1"步骤；空卡组抽牌会触发败北，RunSteps 遇 IsGameOver 提前中止
        var s = TestScene.New().MyDeckTop("OP15-050").Build();
        int powerBefore = s.Players[0].Leader.PowerModThisTurn;
        int handBefore = s.Players[0].Hand.Count;

        var card = new CardInstance { Info = CardDatabase.Get("OP15-019")! };
        await EffectRuntime.Resolve(s, 0, card, EffectTrigger.EventMain, new MockPromptService());

        Assert.Equal(powerBefore + 1000, s.Players[0].Leader.PowerModThisTurn);
        Assert.Equal(handBefore + 1, s.Players[0].Hand.Count); // 【主要】抽 1
    }
}

internal static class TestExtensions
{
    public static HashSet<Guid> State_PreventKOCardIds(this GameState s) => s.PreventKOCardIds;
}
