using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-015 蒙奇·D·路飞（角色 / 炎 4 费 6000，因佩尔地狱/草帽一伙）
/// 持续：我方领袖名称含〈艾斯〉且我方场上存在 6 张或更多咚!! 时，手牌中的此卡牌费用-2。
/// 【对方的攻击时】可以丢弃我方手牌中 1 张力量为 8000 的角色卡牌：
///   本回合中，我方领袖和此角色原本的力量变为 7000。
///
/// 实现说明 / 简化点：
///   - "手牌中此卡费用-2"（领袖名含〈艾斯〉且我方场上咚!!≥6）已由 Effects/HandStaticCost.cs 统一实现。
///   - 仅实现可脚本的【对方的攻击时】：成本为可选丢弃 1 张力量8000角色手牌；
///     收益"原本力量变为7000"用 SetPowerThisTurn 将本回合力量设为绝对 7000(简化：直接设定当前总力量为 7000)。
/// </summary>
public class OP16_015_Luffy : IScriptedEffect
{
    public string CardNumber => "OP16-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本：丢弃我方手牌中 1 张力量为 8000 的角色卡牌（可选）
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Power == 8000).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【对方的攻击时】：丢弃 1 张力量8000的角色手牌，使我方领袖和此角色本回合力量变为7000?");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方手牌中 1 张力量为 8000 的角色卡牌",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count < 1) return;
        var discard = cands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.DiscardHand(me, discard);

        // 收益：本回合我方领袖和此角色原本力量变为 7000
        bool myTurn = ctx.State.CurrentTurnPlayer == ctx.OwnerIndex; // 对方攻击时 → 一般为对方回合
        AtomicOps.SetPowerThisTurn(me.Leader, 7000, 0, myTurn);
        AtomicOps.SetPowerThisTurn(self, 7000, 0, myTurn);
    }
}
