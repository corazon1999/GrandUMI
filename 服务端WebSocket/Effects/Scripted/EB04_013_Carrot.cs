using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-013 凯罗特（角色 / 风 / 纯毛族）
/// 【登场时】我方领袖拥有《纯毛族》特征的场合，将我方最多 2 张拥有《纯毛族》特征的角色和领袖转为活跃状态。
///
/// 实现说明：
///   - 条件：我方领袖含《纯毛族》。
///   - 候选 = 我方领袖（若含《纯毛族》）+ 我方所有含《纯毛族》的角色；玩家选最多 2 张转为活跃。
///   - "转为活跃"用 ActivateCard（对领袖与角色同样适用）。
/// </summary>
public class EB04_013_Carrot : IScriptedEffect
{
    public string CardNumber => "EB04-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("纯毛族")) return;

        var cands = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("纯毛族")) cands.Add(me.Leader);
        cands.AddRange(me.Characters.Where(c => c.Info.HasKeyword("纯毛族")));
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 2 张《纯毛族》领袖或角色转为活跃状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 2);

        foreach (var id in chosen)
        {
            var tgt = cands.FirstOrDefault(c => c.Id.ToString() == id);
            if (tgt is not null) AtomicOps.ActivateCard(tgt);
        }
    }
}
