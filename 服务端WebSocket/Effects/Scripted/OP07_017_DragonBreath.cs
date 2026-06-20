using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-017 龙之气息（事件）
/// 【主要】将对方最多 1 张力量不高于 3000 的角色和最多 1 张费用不高于 1 的舞台 KO。
///
/// 说明 / 简化点：
///   - DSL 仅有 OwnStage/AnyStage prompt，无法精确选取"对方费用≤1的舞台"，故脚本实现：
///     对方舞台唯一（opp.StageCard），费用判定用 CurrentCost()。
///   - 角色 KO 走 AtomicOps.KO；舞台 KO 由于 KOCard 只处理 Characters，故直接将对方舞台移入废弃区。
/// </summary>
public class OP07_017_DragonBreath : IScriptedEffect
{
    public string CardNumber => "OP07-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 对方最多 1 张力量≤3000 的角色 KO ──
        var charCands = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 3000)
            .ToList();
        if (charCands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张力量≤3000 的角色 KO",
                charCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = charCands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // ── 对方最多 1 张费用≤1 的舞台 KO ──
        var stage = opp.StageCard;
        if (stage is not null && ctx.State.CurrentCostOf(stage) <= 1)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = new[] { new { id = stage.Id.ToString(), number = stage.Info.Number } }.ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentStage",
                "将对方最多 1 张费用≤1 的舞台 KO",
                new List<string> { stage.Id.ToString() }, 0, 1, extra);
            if (chosen.Count > 0 && opp.StageCard is not null && opp.StageCard.Id.ToString() == chosen[0])
            {
                opp.Trash.Add(opp.StageCard);
                opp.StageCard = null;
            }
        }
    }
}
