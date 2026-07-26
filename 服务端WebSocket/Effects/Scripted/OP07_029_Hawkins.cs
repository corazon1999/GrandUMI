using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-029 巴基尔·霍金斯（角色 / 风 / 超新星・霍金斯海盗团）
/// 我方领袖拥有《超新星》特征的场合，此角色获得【阻挡者】效果。
///   （条件性持续赋予关键词，用 ContinuousEffect.GrantKeyword + Predicate 实现。）
/// 【每回合1次】此角色因对方的效果将要离开场上的场合，可以改为将对方的 1 张角色转为
///   休息状态，使此角色不离场。
///
/// 实现说明：
///   - 效果KO走 OnAllyWillBeKOd，回手、回牌组、置入生命等非KO离场走 OnAllyWillLeaveField。
///     受害者须为本卡自身；置换成本为将对方1张活跃角色转为休息，再阻止对应离场。
///   - 每回合1次用 TurnOnceUsed 记录。
/// </summary>
public class OP07_029_Hawkins : IScriptedEffect
{
    public string CardNumber => "OP07-029";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField ||
        t == EffectTrigger.OnAllyWillBeKOd ||
        t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "阻挡者",
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    sideIdx == owner &&
                    s.Players[owner].Leader.Info.HasKeyword("超新星"),
            });
            return;
        }

        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        if (victimOwner != ctx.OwnerIndex || victimId != selfId.ToString()) return;

        var key = self.Info.Number + "-guard" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var candidates = opp.Characters.Where(c =>
            !c.IsTapped &&
            !c.HasRestriction(RestrictionKind.CannotBeRested) &&
            !ctx.State.HasContinuousRestriction(c, RestrictionKind.CannotBeRested)).ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "霍金斯【每回合1次】：将对方1张角色转为休息状态，使此角色不离场？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方1张活跃角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.RestCard(target);

        if (nonKoLeave) ctx.State.MarkPreventLeave(selfId);
        else ctx.State.MarkPreventKO(selfId);
        me.TurnOnceUsed.Add(key);
    }
}
