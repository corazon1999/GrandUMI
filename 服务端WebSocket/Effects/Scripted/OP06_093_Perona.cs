using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-093 佩罗娜（角色）
/// 【登场时】对方持有 5 张或更多手牌的场合，选择以下的 1 项：
///   ・对方丢弃其 1 张手牌。
///   ・本回合中，对方最多 1 张角色费用-3。
///
/// 实现说明 / 简化点：
///   - 触发条件：对方手牌 ≥5。
///   - 模态二选一用 ChooseOption。
///   - 第一项对方自选弃 1 张用 OpponentDiscardChosen（需 ctx.Engine）。
///   - 第二项费用-3 本回合用 AddCostModifier(ThisTurn)。
/// </summary>
public class OP06_093_Perona : IScriptedEffect
{
    public string CardNumber => "OP06-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (opp.Hand.Count < 5) return;

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择以下的 1 项",
            new[] { "对方丢弃其 1 张手牌", "本回合中对方最多 1 张角色费用-3" });

        if (opt == 0)
        {
            if (ctx.Engine != null)
                await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 1);
        }
        else
        {
            var cands = opp.Characters.ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张对方角色，本回合费用-3", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddCostModifier(tgt, -3, KeywordDuration.ThisTurn);
            }
        }
    }
}
