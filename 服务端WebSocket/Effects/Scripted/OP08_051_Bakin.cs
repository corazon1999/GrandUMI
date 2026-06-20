using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-051 芭金（角色）
/// 【我方的回合中】【登场时】本回合中，我方最多 1 张"爱德华·维布鲁"力量 +2000。
///
/// 实现说明 / 简化点：
///   - "【我方的回合中】" 表示仅在我方回合发动；登场时若非我方回合则无收益，直接返回。
///   - 选择最多 1 张名称为"爱德华·维布鲁"的我方角色，本回合 +2000（AddPowerThisTurn）。
/// </summary>
public class OP08_051_Bakin : IScriptedEffect
{
    public string CardNumber => "OP08-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅在我方回合中有意义
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters.Where(c => c.MatchesName("爱德华·维布鲁")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张\"爱德华·维布鲁\"，本回合力量 +2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 2000);
        }
    }
}
