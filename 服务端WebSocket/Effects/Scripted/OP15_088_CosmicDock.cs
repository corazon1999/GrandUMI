using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-088 宇宙对接6（角色）
/// 【登场时】可以将我方卡组最上方的3张卡牌放置到废弃区：
///   将我方废弃区中最多1张费用不高于2且拥有《草帽一伙》特征的角色卡牌登场。
///
/// 实现要点（原 DSL 漏了①可选确认 ②冒号前成本"卡组顶3张放废弃区" ③《草帽一伙》/费≤2 过滤，故改脚本）：
///   - "可以…：…" → 先 ConfirmOptional 询问是否发动。
///   - 冒号前为成本：opt-in 后无条件先 MillTop(3) 把卡组顶≤3张放入废弃区（按卡面顺序先付）。
///   - 这3张随即成为废弃区候选 → 登场目标从"含本次磨出的牌"的废弃区中筛选。
///   - 收益：从废弃区选≤1张 费用≤2 且含《草帽一伙》特征 的角色登场。
/// </summary>
public class OP15_088_CosmicDock : IScriptedEffect
{
    public string CardNumber => "OP15-088";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;
        var selfId = ctx.Source.Id;

        // 第①段（无条件持续）：「此角色的费用+6」。登场即注册永续效果，仅作用于自身(card.Id==selfId)，
        // 离场由 TurnEngine 结束阶段按 SourceCardId 自动清理。反馈#194:此前完全没注册→费用恒显示5、判定按5。
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = owner, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 6,
            Predicate = (s, sideIdx, c) => sideIdx == owner && c.Id == selfId,
        });

        // 第②段【登场时】："可以…" 可选发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "宇宙对接6【登场时】：可以将卡组顶3张放置废弃区，登场废弃区中1张费用≤2的《草帽一伙》角色？");
        if (!use) return;

        // 冒号前成本：将卡组最上方最多3张放入废弃区（先付，不可逆）
        AtomicOps.MillTop(me, 3);

        // 候选含刚磨出的牌：废弃区中 费用≤2 且含《草帽一伙》特征 的角色
        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 2 &&
            c.Info.HasKeyword("草帽一伙")
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashCharacter",
            "选择废弃区中最多1张费用≤2的《草帽一伙》角色登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
