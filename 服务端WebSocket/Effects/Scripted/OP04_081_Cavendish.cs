using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-081 凯文迪修（角色 / 地 / 德莱斯罗兹·俊美海盗团）
/// 【咚!!×1】此角色也可以攻击处于活跃状态的角色。
/// 【攻击时】可以将我方的领袖转为休息状态：将对方最多 1 张费用不高于 1 的角色 KO。
///   之后，将我方卡组最上方的 2 张卡牌放置到废弃区。
///
/// 实现说明：
///   - 【咚!!×1】= 此角色被赋予咚 ≥1 时获得"可攻击活跃"——用 ContinuousEffect.GrantKeyword + 谓词判断。
///   - 【攻击时】= OnAttackDeclare，成本 = 横置我方领袖（RestCard），收益 = KO 对方费用≤1 角色 + MillTop(2)。
/// </summary>
public class OP04_081_Cavendish : IScriptedEffect
{
    public string CardNumber => "OP04-081";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 【咚!!×1】此角色也可以攻击活跃状态的角色（被赋予咚 ≥1 时）
            var selfId = self.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "可攻击活跃",
                Predicate = (s, sideIdx, c) =>
                    c.Id == selfId && sideIdx == owner &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1,
            });
            return;
        }

        // 【攻击时】
        if (me.Leader.IsTapped) return; // 领袖须为活跃才可横置支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凯文迪修【攻击时】：将我方领袖转为休息状态，KO 对方最多 1 张费用≤1 的角色，并将卡组顶 2 张废弃？");
        if (!use) return;

        AtomicOps.RestCard(me.Leader);

        var cands = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤1 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // 之后：将我方卡组顶 2 张放置废弃区
        AtomicOps.MillTop(me, 2);
    }
}
