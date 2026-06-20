using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-016 『坚信不疑』即是『强大』!!（事件）
/// 【主要】可以赋予我方的 1 张"希尔巴兹·雷利"2 张活跃状态的咚!!：
///   本回合中，当该被赋予咚!! 的卡牌进行攻击时，对方无法发动【阻挡者】效果。
/// 【反击】本次战斗中，我方最多 1 张角色或"希尔巴兹·雷利"力量 +2000。
///
/// 说明 / 简化点：
/// - "赋予 1 张希尔巴兹·雷利" 对象包括我方领袖（若名为"希尔巴兹·雷利"）及同名角色。
/// - "对方无法发动【阻挡者】效果"：本引擎以攻击者获得【不可阻挡】关键字（本回合）建模，
///   引擎在结算战斗时若攻击者带【不可阻挡】则自动跳过防守方阻挡宣言，效果等价。
/// - 整段【主要】为可选（无可赋予对象 / 活跃咚 <2 时无法支付费用，跳过）。
/// - 【反击】候选为我方所有角色，外加领袖（仅当领袖名为"希尔巴兹·雷利"）。
/// </summary>
public class OP12_016_Kakushin : IScriptedEffect
{
    public string CardNumber => "OP12-016";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 【反击】本次战斗中，我方最多 1 张角色或"希尔巴兹·雷利"力量 +2000。
            var counterTargets = new List<CardInstance>();
            counterTargets.AddRange(me.Characters);
            if (me.Leader.MatchesName("希尔巴兹·雷利")) counterTargets.Add(me.Leader);
            if (counterTargets.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择最多 1 张角色或\"希尔巴兹·雷利\"，本次战斗力量+2000",
                counterTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = counterTargets.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisBattle(tgt, 2000);
            }
            return;
        }

        // ── 【主要】──
        // 费用：赋予我方 1 张"希尔巴兹·雷利"2 张活跃状态的咚!!
        var rayleighTargets = new List<CardInstance>();
        if (me.Leader.MatchesName("希尔巴兹·雷利")) rayleighTargets.Add(me.Leader);
        rayleighTargets.AddRange(me.Characters.Where(c => c.MatchesName("希尔巴兹·雷利")));

        // 需要有可赋予对象且有 2 张活跃咚，否则无法支付费用
        if (rayleighTargets.Count == 0 || me.ActiveDonCount < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "『坚信不疑』即是『强大』：赋予 1 张\"希尔巴兹·雷利\"2 张活跃咚，本回合该卡攻击时对方无法发动阻挡者？");
        if (!use) return;

        var pickTgt = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnRayleigh",
            "选择 1 张\"希尔巴兹·雷利\"赋予 2 张活跃咚",
            rayleighTargets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pickTgt.Count == 0) return;
        var target = rayleighTargets.First(c => c.Id.ToString() == pickTgt[0]);

        int attached = AtomicOps.AttachDonFromCost(me, target.Id, 2, DonState.Active);
        if (attached < 2) return; // 活跃咚不足 2 张 → 费用未支付（理论上已被上方拦截）

        // 效果：本回合中，该卡攻击时对方无法发动【阻挡者】→ 赋予【不可阻挡】（本回合）
        AtomicOps.GiveKeyword(target, "不可阻挡", KeywordDuration.ThisTurn);
    }
}
