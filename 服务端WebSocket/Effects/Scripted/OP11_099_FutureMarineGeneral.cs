using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-099 我是……会成为海军将领的人!!!（事件，地）
/// 【主要】确认我方卡组最上方的 3 张卡牌，公开其中最多 1 张
///   “我是……会成为海军将领的人!!!”以外的拥有《海军》特征的卡牌并加入手牌。
///   之后，将剩余的卡牌放置到废弃区。
///
/// 实现说明 / 简化点：
///   - 仅实现【主要】效果本体；卡上【触发】为“发动此卡牌的【主要】效果”（复制/重新发动本卡），
///     属规范第十节未列出的特殊机制，触发节不在此实现。
///   - 看顶 3 张：公开 1 张《海军》（排除同名卡）加入手牌，其余顶部卡放置到废弃区。
///   - 卡组牌为非公开区，prompt 传 choiceCards 展示候选卡面。
/// </summary>
public class OP11_099_FutureMarineGeneral : IScriptedEffect
{
    public string CardNumber => "OP11-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int k = Math.Min(3, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var cands = top.Where(c =>
            c.Info.HasKeyword("海军") &&
            c.Info.Name != "我是……会成为海军将领的人!!!").ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 3 张，公开最多 1 张《海军》卡加入手牌",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 剩余顶部卡牌放置到废弃区
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest)
        {
            me.Deck.Remove(c);
            me.Trash.Add(c);
        }
    }
}
