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
///   - 离场置换守护用 OnAllyWillBeKOd（效果KO/离场流程派发，KOReason=="effect" 且发起方为对方）。
///     受害者须为本卡自身；置换成本为将对方 1 张角色转为休息，再 MarkPreventKO 取消本次KO。
///   - 每回合1次用 TurnOnceUsed 记录。
/// </summary>
public class OP07_029_Hawkins : IScriptedEffect
{
    public string CardNumber => "OP07-029";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAllyWillBeKOd;

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

        // OnAllyWillBeKOd：本卡自身因对方效果将要离场时的置换守护
        if (ctx.Trigger == EffectTrigger.OnAllyWillBeKOd)
        {
            if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;

            var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
            if (victimId is null || victimId != selfId.ToString()) return;

            var key = self.Info.Number + "-guard" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            if (opp.Characters.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "霍金斯【每回合1次】：将对方 1 张角色转为休息状态，使此角色不离场？");
            if (!use) return;

            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方 1 张角色转为休息状态",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (pick.Count == 0) return;

            var tgt = opp.Characters.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.RestCard(tgt);

            ctx.State.MarkPreventKO(selfId);
            me.TurnOnceUsed.Add(key);
        }
    }
}
