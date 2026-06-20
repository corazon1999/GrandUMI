using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-037 Mr.9（角色 / 暗 / 巴洛克工作室）
/// 【对方的攻击时】【每回合1次】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   将对方最多 1 张费用不高于 2 的角色 KO。
///
/// 实现说明：
///   - 【对方的攻击时】= OnOppAttackDeclare 时机。
///   - 【每回合1次】用 TurnOnceUsed 去重。
///   - 可选成本【咚!!-1】：ConfirmOptional 确认；需费用区有可放回的咚!!。
///   - 收益：KO 对方最多 1 张费用≤2 角色。
/// </summary>
public class EB01_037_Mr9 : IScriptedEffect
{
    public string CardNumber => "EB01-037";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 需费用区有可放回的咚!!
        if (me.TotalDonInCostArea < 1) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.9【对方的攻击时】：咚!!-1，将对方最多 1 张费用≤2 的角色 KO？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        me.TurnOnceUsed.Add(key);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用不高于 2 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
    }
}
