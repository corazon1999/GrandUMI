using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-024 哥顿（角色 / 风 1 费 0，FILM）
/// 【登场时】可以公开我方手牌中1张拥有《音乐》或《FILM》特征的卡牌：
///   当本回合结束时，将我方最多2张咚!!转为活跃状态。
///
/// 实现：
///   - 【登场时】（可选）：若手牌存在《音乐》或《FILM》特征的卡牌，玩家可选 1 张公开
///     （公开 = 选择，仍留在手牌）。公开成功则在此卡上标记一个延迟效果。
///   - 【我方的回合结束时】：若已标记，则把我方最多 2 张休息状态的咚!! 转为活跃。
///     （“当本回合结束时”即本回合的我方结束时，借 OnMyTurnEnd 触发兑现该延迟效果。）
///   - 标记存于本卡 OncePerTurnUsedKeys（回合结束阶段清理），与延迟时点一致。
///
/// 简化点：
///   - 延迟效果以“登场时设标记 + 我方回合结束时结算”近似实现；若此卡在结束前离场，
///     则随实例消失而不结算（与延迟效果绑定来源角色的直觉一致）。
/// </summary>
public class OP13_024_Gordon : IScriptedEffect
{
    private const string PendingKey = "OP13-024-PendingActivate";

    public string CardNumber => "OP13-024";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 候选：手牌中拥有《音乐》或《FILM》特征的卡牌
            var candidates = me.Hand
                .Where(c => c.Info.HasKeyword("音乐") || c.Info.HasKeyword("FILM"))
                .ToList();
            if (candidates.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealMusicOrFilm",
                "可以公开手牌中 1 张《音乐》或《FILM》特征卡牌（本回合结束时活跃最多2张咚!!）",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count == 0) return; // 未公开 → 不触发延迟效果

            // 向对手公开所选 1 张《音乐》/《FILM》卡牌：广播牌面浮层（公开≠丢弃，仍留手牌）
            ctx.Engine?.BroadcastReveal(ctx.OwnerIndex,
                chosen.Select(id => candidates.First(c => c.Id.ToString() == id).Info.Number).ToList());

            // 公开成功，标记延迟效果待我方回合结束时结算
            ctx.Source.OncePerTurnUsedKeys.Add(PendingKey);
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            if (!ctx.Source.OncePerTurnUsedKeys.Contains(PendingKey)) return;
            ctx.Source.OncePerTurnUsedKeys.Remove(PendingKey);

            // 将我方最多 2 张休息状态的咚!! 转为活跃状态
            int activated = 0;
            foreach (var d in me.CostArea)
            {
                if (activated >= 2) break;
                if (d.State == DonState.Rest)
                {
                    d.State = DonState.Active;
                    d.AttachedToCardId = null;
                    activated++;
                }
            }
        }
    }
}
