using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-094 蒙奇·D·龙
/// 【登场时】可以将我方废弃区中 3 张拥有《革命军》特征的卡牌自选顺序放回卡组最下方：
/// 我方领袖拥有《革命军》特征的场合，将我方废弃区中最多 1 张费用不高于 6 的角色卡牌登场。
///
/// 说明：
///   - 整个效果为可选（"可以…"）。支付成本需废弃区有 3 张《革命军》卡牌；不足则无法发动。
///   - 成本：从废弃区《革命军》卡中选 3 张放回卡组最下方（"自选顺序"简化为玩家选择顺序即放底顺序）。
///   - 之后若我方领袖拥有《革命军》特征，可从废弃区（成本已移走后剩余）中将最多 1 张
///     费用不高于 6 的角色登场。
///   - 废弃区选牌经 extra.choiceCards 下发卡面给客户端。
/// </summary>
public class OP12_094_Dragon : IScriptedEffect
{
    public string CardNumber => "OP12-094";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 成本候选：废弃区中拥有《革命军》特征的卡
        var costPool = me.Trash.Where(c => c.Info.HasKeyword("革命军")).ToList();
        if (costPool.Count < 3) return; // 不足 3 张无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "龙：将废弃区 3 张《革命军》卡放回卡组最下方，从废弃区登场最多 1 张费用≤6 的角色？");
        if (!use) return;

        // 选 3 张《革命军》卡作为成本（顺序即放回卡组底部的顺序）
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costPool.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var costChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Dragon094Cost",
            "选择 3 张《革命军》卡放回卡组最下方（自选顺序）",
            costPool.Select(c => c.Id.ToString()).ToList(), 3, 3, costExtra);
        if (costChosen.Count < 3) return; // 未完成支付

        foreach (var cid in costChosen)
        {
            var card = me.Trash.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 我方领袖拥有《革命军》特征的场合，才可登场
        if (!me.Leader.Info.HasKeyword("革命军")) return;

        // 从废弃区登场最多 1 张费用≤6 的角色
        var playPool = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost <= 6).ToList();
        if (playPool.Count == 0) return;

        var playExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playPool.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var playChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Dragon094Play",
            "从废弃区将最多 1 张费用≤6 的角色登场",
            playPool.Select(c => c.Id.ToString()).ToList(), 0, 1, playExtra);
        if (playChosen.Count == 0) return;

        var pick = playPool.FirstOrDefault(c => c.Id.ToString() == playChosen[0]);
        if (pick is null) return;
        AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, pick);
    }
}
