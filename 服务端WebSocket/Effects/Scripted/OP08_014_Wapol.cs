using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-014 瓦波尔（角色）
/// 【咚!!×1】【攻击时】本回合中，对方最多1张角色力量-2000。
///   之后，直到下个对方的回合结束时为止，此角色的力量+2000。
///
/// 实现说明：
///   - 【咚!!×1】为发动条件（已附着咚数≥1），由引擎在调用脚本前校验，此处不重复实现。
///   - 时机：OnAttackDeclare（攻击时）。
///   - 让玩家选择对方最多 1 张角色，本回合力量-2000（AddPowerThisTurn 负值）。
///   - 此角色力量+2000：引擎无"持续到下个对方回合结束"的精确力量通道，
///     用 AddPowerThisTurn 近似（与同类卡处理一致；本回合结束阶段清除，略短于文本）。
/// </summary>
public class OP08_014_Wapol : IScriptedEffect
{
    public string CardNumber => "OP08-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (ctx.State.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) < 1) return;

        // 本回合中，对方最多 1 张角色力量-2000
        var cands = opp.Characters.ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合力量-2000",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, -2000);
            }
        }

        // 之后：此角色力量+2000（近似为本回合）
        AtomicOps.AddPowerThisTurn(ctx.Source, 2000);
    }
}
