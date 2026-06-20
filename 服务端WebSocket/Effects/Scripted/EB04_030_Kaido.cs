using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-030 盖德（角色 / 暗 / 四皇・百兽海盗团 / cost7 power9000）
/// 此角色将要被KO的场合，可以改为将我方场上的1张咚!!放回咚!!卡组，使此角色不会被KO。
/// 【登场时】咚!!-2：我方领袖拥有《百兽海盗团》特征的场合，本回合中，此角色获得【速攻】效果。
///   之后，将对方最多1张费用不高于7的角色转为休息状态。
///
/// 实现说明：
///   - 置换防KO用 PreKO 钩子：自身将要被KO时，若我方费用区有可放回的咚!!（活跃/休息），
///     询问是否放回 1 张以取消本次 KO（MarkPreventKO）。
///   - 【登场时】成本为咚!!-2（需费用区≥2 张可放回咚）；满足领袖《百兽海盗团》才赋予自身本回合【速攻】，
///     并令对方最多 1 张费用≤7 的角色转为休息。
/// </summary>
public class EB04_030_Kaido : IScriptedEffect
{
    public string CardNumber => "EB04-030";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.PreKO || t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            // 仅守护自身
            if (me.CostArea.Count < 1) return; // 无咚可放回 → 无法置换

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "盖德：将我方场上的 1 张咚!!放回咚!!卡组，使此角色不会被KO？");
            if (!use) return;

            if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
            ctx.State.MarkPreventKO(ctx.Source.Id);
            return;
        }

        // 【登场时】咚!!-2
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.CostArea.Count < 2) return; // 无法支付成本 咚!!-2

            // 咚!!-2（强制成本，发动本效果即支付）
            if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;

            // 条件：领袖含《百兽海盗团》
            if (!me.Leader.Info.HasKeyword("百兽海盗团")) return;

            // 本回合此角色获得【速攻】
            AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn);

            // 之后：对方最多 1 张费用≤7 的角色转为休息
            var cands = opp.Characters.Where(c => c.Info.Cost <= 7).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤7 的角色转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }
    }
}
