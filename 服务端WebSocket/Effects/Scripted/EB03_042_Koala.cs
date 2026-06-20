using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-042 克尔拉（角色 / 地 / 德莱斯罗兹·革命军 cost4 power5000）
/// 我方领袖拥有《革命军》特征的场合，此角色的费用+4。（持续 → ContinuousEffect.CostDelta）
/// 【对方的回合中】【KO时】将我方手牌或废弃区中最多1张“克尔拉”以外费用不高于6且拥有《革命军》特征的
///   角色卡牌，或费用不高于6的“妮古·罗宾”登场。
///
/// 实现说明：
///   - 费用+4 为条件持续修正，用 CostDelta 注册，Predicate 限定自身且领袖含《革命军》。
///   - KO 段限【对方的回合中】；候选合并手牌+废弃区，过滤"克尔拉"以外 cost≤6《革命军》角色或 cost≤6 妮古·罗宾。
///   - 选中后按其所在区域分别用 PlayFromHandFree / PlayFromTrashFree 登场。
/// </summary>
public class EB03_042_Koala : IScriptedEffect
{
    public string CardNumber => "EB03-042";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 持续：领袖含《革命军》时，此角色费用 +4
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                CostDelta = 4,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.Players[owner].Leader.Info.HasKeyword("革命军"),
            });
            return;
        }

        // 【KO时】仅【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == owner) return;

        bool Eligible(CardInstance c)
        {
            if (c.Info.Kind != CardKind.Character) return false;
            if (c.Info.Cost > 6) return false;
            if (c.Info.NameContains("妮古·罗宾")) return true;
            return !c.Info.NameContains("克尔拉") && c.Info.HasKeyword("革命军");
        }

        var cands = me.Hand.Where(Eligible).Concat(me.Trash.Where(Eligible)).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "HandOrTrashCharacter",
            "将手牌或废弃区中最多 1 张符合条件的角色登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        if (me.Hand.Contains(picked)) AtomicOps.PlayFromHandFree(ctx.State, owner, picked);
        else if (me.Trash.Contains(picked)) AtomicOps.PlayFromTrashFree(ctx.State, owner, picked);
    }
}
