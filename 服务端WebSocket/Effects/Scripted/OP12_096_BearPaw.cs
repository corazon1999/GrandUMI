using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-096 熊掌冲击（事件）
/// 【主要】选择对方最多 1 张费用不高于 4 的角色 KO。
///   我方场上存在费用为 8 或更高的角色的场合，改为可选择费用不高于 6 的角色。
/// 【触发】抽取 1 张卡牌，将我方卡组最上方的 1 张卡牌放置到废弃区。
///
/// 费用阈值用 c.Info.Cost（基础费用）判定，与 DSL filter 一致。
/// </summary>
public class OP12_096_BearPaw : IScriptedEffect
{
    public string CardNumber => "OP12-096";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】抽 1 张 + 卡组顶 1 张入废弃区
            var meTrig = ctx.State.Players[ctx.OwnerIndex];
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            AtomicOps.MillTop(meTrig, 1);
            return;
        }

        // 【主要】
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 我方场上存在费用 ≥ 8 的角色 → 阈值提升到 6，否则 4
        int threshold = me.Characters.Any(c => c.Info.Cost >= 8) ? 6 : 4;

        var candidates = opp.Characters.Where(c => c.Info.Cost <= threshold).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"选择对方最多 1 张费用不高于 {threshold} 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
    }
}
