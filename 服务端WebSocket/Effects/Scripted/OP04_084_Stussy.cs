using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-084 斯图西（角色 / 地 / CP0）
/// 【登场时】确认我方卡组最上方的 3 张卡牌，将其中最多 1 张"斯图西"以外、拥有《CP》特征且费用不高于 2 的角色卡牌登场。
///   之后，将剩余的卡牌放置到废弃区。
///
/// 实现说明：
///   - "从卡组顶登场角色" 通过 卡组→手牌→PlayFromHandFree 的等价路径实现（净效果同为从卡组直接登场）。
///   - 《CP》特征用 HasKeyword 模糊匹配（CP0 等亦含 "CP"）。排除名称含"斯图西"的卡。
///   - 之后剩余的 3 张顶牌放置废弃区。
/// </summary>
public class OP04_084_Stussy : IScriptedEffect
{
    public string CardNumber => "OP04-084";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cands = top.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 2 &&
            c.Info.HasKeyword("CP") &&
            !c.Info.NameContains("斯图西")
        ).ToList();

        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 3 张，将其中最多 1 张《CP》特征费用≤2 的角色登场",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                // 卡组 → 手牌 → 登场（等价于从卡组直接登场）
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }

        // 之后：将剩余仍在顶部的卡牌放置废弃区
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest)
        {
            me.Deck.Remove(c);
            me.Trash.Add(c);
        }
    }
}
