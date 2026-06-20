using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-009 多古拉&马古拉（角色）
/// 【登场时】本回合中，我方最多 1 张费用为 1 的红色（炎）角色获得【双重攻击】效果。
///
/// 实现说明：
///   - 候选限定为我方场上费用为 1、颜色含"红"的角色。
///   - 用 GiveKeyword 赋予【双重攻击】，持续到本回合（KeywordDuration.ThisTurn）。
/// </summary>
public class OP07_009_Dogura_Magura : IScriptedEffect
{
    public string CardNumber => "OP07-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters.Where(c =>
            c.Info.Cost == 1 &&
            c.Info.ColorList.Contains("红")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张费用为 1 的红色角色，本回合获得【双重攻击】",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.GiveKeyword(tgt, "双重攻击", KeywordDuration.ThisTurn);
        }
    }
}
