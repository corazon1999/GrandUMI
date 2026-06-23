using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// OP16 6 张领航的定向 Fact 骨架
///
/// 每张领航：
///   1. Arrange：构造特定场景（领航 + 配合卡 + 资源）
///   2. Act：调 EffectRuntime.Resolve
///   3. Assert：主断言已写；// TODO 处按需补充
///
/// 模板可复制改卡号给其他卡用。
/// </summary>
public class OP16LeaderTests
{
    // ──────────────────────────────────────────────────────────────
    // OP16-001 艾斯
    // 启动主要每回合 1 次：本回合中，我方最多 1 张力量 ≥8000 的
    //   "蒙奇·D·路飞" 或力量 ≥8000 且含《白胡子海盗团》的角色获得【速攻】
    // ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task OP16_001_Ace_Activated_GivesRushTo_PowerGe8000_Whitebeard()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-001");
        // 清掉默认填充的两张通用角色，换成 1 张白胡子海盗团 P=10000
        s.Players[0].Characters.Clear();
        var nyugot = new CardInstance { Info = CardDatabase.Get("OP16-003")!, TurnPlayed = s.TurnCount - 1 };
        s.Players[0].Characters.Add(nyugot);

        var mock = new MockPromptService()
            .QueueChoose(nyugot.Id.ToString());

        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.ActivatedMain, mock);

        // 主断言：纽哥特获得【速攻】
        Assert.Contains(nyugot.GainedKeywords, k => k.Keyword == "速攻");
        // TODO 进阶：再次触发应被每回合 1 次锁阻止（验证 me.TurnOnceUsed 含 "OP16-001-MainOncePerTurn"）

        var keywordCountBefore = nyugot.GainedKeywords.Count;
        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(nyugot.Id.ToString()));
        Assert.Equal(keywordCountBefore, nyugot.GainedKeywords.Count);   // 没新增 keyword

        // TODO 进阶：放一张力量 7000 白胡子海盗团角色 + 让用户选它 → 不应在 mock 答案的可选列表里
    }

    // ──────────────────────────────────────────────────────────────
    // OP16-022 路飞
    // 启动主要每回合 1 次：场上仅有《因佩尔地狱》角色时，将 2 张休息咚转活跃
    // ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task OP16_022_Luffy_Activated_ActivatesTwoRestDons_WhenAllImpelDown()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-022");
        // 清掉默认填充的通用角色，换成 2 张《因佩尔地狱》角色
        s.Players[0].Characters.Clear();
        s.Players[0].Characters.Add(new CardInstance { Info = CardDatabase.Get("OP16-015")!, TurnPlayed = s.TurnCount - 1 });
        s.Players[0].Characters.Add(new CardInstance { Info = CardDatabase.Get("OP16-023")!, TurnPlayed = s.TurnCount - 1 });

        // 把 2 张活跃咚改为休息态（其他保持）
        int converted = 0;
        foreach (var d in s.Players[0].CostArea)
        {
            if (converted >= 2) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; converted++; }
        }
        int activeBefore = s.Players[0].ActiveDonCount;
        int restBefore = s.Players[0].RestDonCount;

        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.ActivatedMain, new MockPromptService());

        // 主断言：2 张休息咚转活跃
        Assert.Equal(activeBefore + 2, s.Players[0].ActiveDonCount);
        Assert.Equal(restBefore - 2, s.Players[0].RestDonCount);
        // TODO 反例：场上若有非因佩尔地狱角色，效果不应发动
    }

    // ──────────────────────────────────────────────────────────────
    // OP16-041 巴奇
    // 【咚!!×1】【每回合1次】当我方《因佩尔地狱》角色离场时：从手牌登场1张"因佩尔地狱的囚犯"。
    // 重写后监听 OnAnyCharKOd（战斗/效果KO）+ OnCharLeaveField（非KO离场）；需领袖被赋予咚≥1。
    // ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task OP16_041_Buggy_OnImpelDownLeave_PlaysPrisonerFromHand()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-041");
        // 手牌塞 1 张囚犯（清空原 max scenario 的手牌避免目标干扰）
        s.Players[0].Hand.Clear();
        var prisoner = new CardInstance { Info = CardDatabase.Get("OP16-042")! };
        s.Players[0].Hand.Add(prisoner);

        // 【咚!!×1】：给领袖赋 1 咚
        var don0 = s.Players[0].CostArea[0];
        don0.State = DonState.Attached;
        don0.AttachedToCardId = s.Players[0].Leader.Id;

        // 模拟一张我方《因佩尔地狱》角色离场（此刻在废弃区），用 OnAnyCharKOd 派发
        var koChar = new CardInstance { Info = CardDatabase.Get("OP16-034")! }; // OP16-034 含《因佩尔地狱》
        s.Players[0].Trash.Add(koChar);
        var payload = new Dictionary<string, object?>
        {
            ["owner"] = 0,
            ["cardId"] = koChar.Id.ToString(),
        };

        int charsBefore = s.Players[0].Characters.Count;
        var mock = new MockPromptService()
            .QueueChoose(prisoner.Id.ToString());

        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.OnAnyCharKOd, mock, payload);

        // 主断言：囚犯登场，手牌移除
        Assert.Contains(prisoner, s.Players[0].Characters);
        Assert.DoesNotContain(prisoner, s.Players[0].Hand);
        Assert.Equal(charsBefore + 1, s.Players[0].Characters.Count);
    }

    // 复现用户反馈：OP16-045 克洛克达尔【登场时】把己方《因佩尔地狱》角色放回手牌作为成本，
    // 该角色「离开场上」应触发领航 OP16-041 巴奇 → 弹窗登场囚犯。走 OnCharLeaveField（非KO离场）+ 排空链路。
    [Fact]
    public async Task OP16_041_TriggersOn_OP16_045_BounceCost_PlaysPrisoner()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-041"); // MaxScenario 已给领航 2 张赋予中咚

        // 场上：克洛克达尔(刚登场) + 1 张费用≥2《因佩尔地狱》角色作为回手成本目标
        s.Players[0].Characters.Clear();
        var croc = new CardInstance { Info = CardDatabase.Get("OP16-045")! };            // cost4
        var bounceTarget = new CardInstance { Info = CardDatabase.Get("OP16-037")! };    // cost2 《因佩尔地狱》
        s.Players[0].Characters.Add(croc);
        s.Players[0].Characters.Add(bounceTarget);

        // 手牌仅放 1 张囚犯(OP16-042, cost6)：不会成为克洛克达尔登场收益(≤2)的候选，
        // 故克洛克达尔不再二次弹窗，仅做回手成本，随后触发巴奇。
        s.Players[0].Hand.Clear();
        var prisoner = new CardInstance { Info = CardDatabase.Get("OP16-042")! };
        s.Players[0].Hand.Add(prisoner);

        var mock = new MockPromptService()
            .QueueConfirm(true)                       // 克洛克达尔【登场时】确认发动
            .QueueChoose(bounceTarget.Id.ToString())  // 选费用≥2角色回手
            .QueueChoose(prisoner.Id.ToString());     // 巴奇：选囚犯登场

        await EffectRuntime.Resolve(s, 0, croc, EffectTrigger.OnEnterField, mock);

        // 回手成本生效
        Assert.Contains(bounceTarget, s.Players[0].Hand);
        Assert.DoesNotContain(bounceTarget, s.Players[0].Characters);
        // 巴奇触发：囚犯登场
        Assert.Contains(prisoner, s.Players[0].Characters);
        Assert.DoesNotContain(prisoner, s.Players[0].Hand);
    }

    // 完整实战(严格)：克洛克达尔回手成本 + 收益(登场≤2《因佩尔地狱》) 同时发生。
    // 用户反馈：选择收益登场时会"吞掉"领航 OP16-041 的离场触发；跳过收益则正常。
    // 严格化：回手目标 OP16-024(被回手不触发其【K.O.时】)、收益卡 OP16-044(effectTags=[]，登场无弹窗)，
    //   保证 mock 队列与每一步弹窗严格一一对应，不会被中途效果偷吃而假阳性。
    [Fact]
    public async Task OP16_041_TriggersOn_OP16_045_WithBenefitSummon()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-041");

        s.Players[0].Characters.Clear();
        var croc = new CardInstance { Info = CardDatabase.Get("OP16-045")! };
        var bounceTarget = new CardInstance { Info = CardDatabase.Get("OP16-024")! }; // cost2《因佩尔地狱》, 无登场/离场弹窗
        s.Players[0].Characters.Add(croc);
        s.Players[0].Characters.Add(bounceTarget);

        s.Players[0].Hand.Clear();
        var prisoner = new CardInstance { Info = CardDatabase.Get("OP16-042")! };          // 巴奇登场目标
        var benefit  = new CardInstance { Info = CardDatabase.Get("OP16-044")! };          // cost2《因佩尔地狱》effectTags=[] 收益
        s.Players[0].Hand.Add(prisoner);
        s.Players[0].Hand.Add(benefit);

        var mock = new MockPromptService()
            .QueueConfirm(true)                       // 克洛克达尔确认
            .QueueChoose(bounceTarget.Id.ToString())  // 回手成本
            .QueueChoose(benefit.Id.ToString())       // 收益：登场 OP16-044
            .QueueChoose(prisoner.Id.ToString());     // 巴奇：登场囚犯

        await EffectRuntime.Resolve(s, 0, croc, EffectTrigger.OnEnterField, mock);

        Assert.Contains(benefit, s.Players[0].Characters);   // 收益登场
        Assert.Contains(prisoner, s.Players[0].Characters);  // 巴奇仍触发(关键)
        Assert.DoesNotContain(prisoner, s.Players[0].Hand);
    }

    // 真实 bug 复现：回手目标 == 收益登场目标(同一张 OP16-044：cost2 既满足≥2回手又满足≤2因佩尔地狱收益)。
    // 该卡 场上→回手→又被收益登场回场上；巴奇 FindLeft 只搜废弃/手牌/卡组/生命，找不到回到场上的它 → 静默不触发。
    [Fact]
    public async Task OP16_041_Triggers_When_BounceTarget_IsReSummonedAsBenefit()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-041");
        s.Players[0].Characters.Clear();
        var croc = new CardInstance { Info = CardDatabase.Get("OP16-045")! };
        var iva = new CardInstance { Info = CardDatabase.Get("OP16-044")! }; // 同时是回手目标和收益候选
        s.Players[0].Characters.Add(croc);
        s.Players[0].Characters.Add(iva);

        s.Players[0].Hand.Clear();
        var prisoner = new CardInstance { Info = CardDatabase.Get("OP16-042")! };
        s.Players[0].Hand.Add(prisoner); // 手牌无其他收益候选 → 收益只能选回手回来的 iva

        var mock = new MockPromptService()
            .QueueConfirm(true)                    // 克洛克达尔确认
            .QueueChoose(iva.Id.ToString())        // 回手 iva
            .QueueChoose(iva.Id.ToString())        // 收益：把同一张 iva 登场回场上
            .QueueChoose(prisoner.Id.ToString());  // 巴奇：选囚犯

        await EffectRuntime.Resolve(s, 0, croc, EffectTrigger.OnEnterField, mock);

        Assert.Contains(iva, s.Players[0].Characters);       // iva 回手后又登场, 现在在场上
        Assert.Contains(prisoner, s.Players[0].Characters);  // 巴奇必须触发(修复前此处失败)
        Assert.DoesNotContain(prisoner, s.Players[0].Hand);
    }

    // ──────────────────────────────────────────────────────────────
    // OP16-060 战国
    // 启动主要：把 8 张活跃咚放回咚卡组 →
    //   从手牌选最多 3 张卡名不同的《大将》角色登场
    // ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task OP16_060_Sengoku_Activated_Returns8Don_AndPlaysThreeAdmirals()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-060");
        // 手牌塞 3 张卡名不同的《大将》（库赞 / 萨卡斯基 / 波尔萨利诺）
        s.Players[0].Hand.Clear();
        var kuzan  = new CardInstance { Info = CardDatabase.Get("OP16-063")! };
        var saka   = new CardInstance { Info = CardDatabase.Get("OP16-065")! };
        var bors   = new CardInstance { Info = CardDatabase.Get("OP16-073")! };
        s.Players[0].Hand.Add(kuzan);
        s.Players[0].Hand.Add(saka);
        s.Players[0].Hand.Add(bors);

        // 清掉默认填充的 2 张己方角色腾位
        s.Players[0].Characters.Clear();

        // 确保 8 活跃咚（MaxScenario 给 10 张，足够）
        Assert.True(s.Players[0].ActiveDonCount >= 8);
        int donDeckBefore = s.Players[0].DonDeck.Count;

        var mock = new MockPromptService()
            .QueueChoose(kuzan.Id.ToString())
            .QueueChoose(saka.Id.ToString())
            .QueueChoose(bors.Id.ToString());

        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.ActivatedMain, mock);

        // 主断言：3 张大将登场。
        // 批次1修复（卡效登场的角色现在会触发各自【登场时】）后，被领袖效果登场的大将会发动登场时：
        //   OP16-063 库赞 拉2咚(休息)、OP16-073 波尔萨利诺 拉2咚(1活跃+1休息) → 放回的 8 张被回拉 4 张，净 +4。
        Assert.True(s.Players[0].DonDeck.Count >= donDeckBefore + 4,
            $"DonDeck={s.Players[0].DonDeck.Count}, before={donDeckBefore}");
        Assert.Contains(kuzan, s.Players[0].Characters);
        Assert.Contains(saka,  s.Players[0].Characters);
        Assert.Contains(bors,  s.Players[0].Characters);
        // TODO 反例：若手牌只有 2 张大将，应只登场 2 张不报错
        // TODO 反例：活跃咚不足 8 时，效果不应发动（don 不被消耗，手牌不动）
    }

    // ──────────────────────────────────────────────────────────────
    // OP16-079 大和
    // 当从废弃区登场《和之国》角色时，本回合该角色获得【速攻】
    // 当前脚本是占位（OnEnterField 仅响应领航自身登场，但领航不登场）
    // 完整实现需要引擎"PlayFromTrash 时打标记"再"OnEnterField 检查标记"
    // ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task OP16_079_Yamato_Placeholder_DoesNotCrash()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-079");
        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.OnEnterField, new MockPromptService());
        // 主断言：占位脚本不抛异常即可
        Assert.False(s.IsGameOver);
        // TODO 完整实现后改成：从废弃区登场《和之国》角色 → 该角色 GainedKeywords 含"速攻"
    }

    // ──────────────────────────────────────────────────────────────
    // OP16-080 马歇尔·D·提奇
    // 对方攻击时每回合 1 次：可丢弃 1 张带【触发】的手牌
    //   → 攻击对象变为此领袖或《黑胡子海盗团》角色
    // 当前脚本仅消耗弃牌（攻击对象重定向需 BattleEngine 扩展）
    // ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task OP16_080_Teach_OnOppAttack_DiscardsTriggerCard()
    {
        var s = TestScene.MaxScenario(myLeader: "OP16-080");
        // 手牌塞 1 张带【触发】的卡（OP16-019）
        s.Players[0].Hand.Clear();
        var triggerCard = new CardInstance { Info = CardDatabase.Get("OP16-019")! };
        s.Players[0].Hand.Add(triggerCard);

        int trashBefore = s.Players[0].Trash.Count;

        var mock = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(triggerCard.Id.ToString());

        await EffectRuntime.Resolve(s, 0, s.Players[0].Leader, EffectTrigger.OnOppAttackDeclare, mock);

        // 主断言：触发卡进入废弃区
        Assert.DoesNotContain(triggerCard, s.Players[0].Hand);
        Assert.Contains(triggerCard, s.Players[0].Trash);
        Assert.Equal(trashBefore + 1, s.Players[0].Trash.Count);
        // TODO BattleEngine 接入 redirect 后：验证 CurrentBattle.TargetCardId 变为本领袖 / 黑胡子海盗团角色
        // TODO 进阶：手牌中没有触发卡时不应发动
        // TODO 进阶：拒绝（Confirm false）时也不应发动
    }
}
