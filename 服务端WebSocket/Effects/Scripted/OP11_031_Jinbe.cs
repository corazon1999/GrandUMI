using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-031 甚平（角色）
/// 【登场时】我方领袖拥有《鱼人族》或《人鱼族》特征的场合，将对方最多 1 张费用不高于 5 的角色转为休息状态。
/// 【启动主要】【每回合1次】我方最多 1 张拥有《鱼人族》或《人鱼族》特征的角色可以在登场的回合中攻击角色。
///
/// </summary>
public class OP11_031_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP11-031";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            string key = $"OP11-031-act:{ctx.Source.Id}";
            if (me.TurnOnceUsed.Contains(key)) return;
            var targets = me.Characters
                .Where(card => card.Info.HasKeyword("鱼人族") || card.Info.HasKeyword("人鱼族"))
                .ToList();
            if (targets.Count == 0) return;
            var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择最多 1 张《鱼人族》或《人鱼族》角色，使其可在登场回合攻击角色",
                targets.Select(card => card.Id.ToString()).ToList(), 0, 1);
            if (selected.Count == 0) return;
            var target = targets.First(card => card.Id.ToString() == selected[0]);
            AtomicOps.GiveKeyword(target, "登场回合可攻击角色", KeywordDuration.ThisTurn, ctx.OwnerIndex);
            me.TurnOnceUsed.Add(key);
            return;
        }

        // 条件：我方领袖拥有《鱼人族》或《人鱼族》特征
        bool ok = me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("人鱼族");
        if (!ok) return;

        // 将对方最多 1 张费用不高于 5 的角色转为休息状态
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 5)
            .ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用不高于 5 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
