using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-017 见闻色的霸气（事件）
/// 【主要】可以赋予我方的 1 张"希尔巴兹·雷利"1 张活跃状态的咚!!：
///   确认我方卡组最上方的 4 张卡牌，公开其中最多 1 张红色事件或费用为 3 或更高的角色卡牌并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 简化点：
/// - "赋予 1 张希尔巴兹·雷利"——可对象包括我方领袖（若名为"希尔巴兹·雷利"）及同名角色。
/// - "自选顺序放回卡组最下方"实现为保持原相对顺序放底（对实战影响极小）。
/// - 整段【主要】为可选（无可赋予对象 / 无活跃咚 时无法发动，跳过）。
/// </summary>
public class OP12_017_Kenbunshoku : IScriptedEffect
{
    public string CardNumber => "OP12-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // ── 费用：赋予我方 1 张"希尔巴兹·雷利"1 张活跃状态的咚!! ──
        var rayleighTargets = new List<CardInstance>();
        if (me.Leader.MatchesName("希尔巴兹·雷利")) rayleighTargets.Add(me.Leader);
        rayleighTargets.AddRange(me.Characters.Where(c => c.MatchesName("希尔巴兹·雷利")));

        // 需要有可赋予对象且有 1 张活跃咚，否则无法支付费用
        if (rayleighTargets.Count == 0 || me.ActiveDonCount < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "见闻色的霸气：赋予 1 张\"希尔巴兹·雷利\"1 张活跃咚，确认卡组顶 4 张并公开取 1 张？");
        if (!use) return;

        // 选择被赋予咚的"希尔巴兹·雷利"
        var pickTgt = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnRayleigh",
            "选择 1 张\"希尔巴兹·雷利\"赋予 1 张活跃咚",
            rayleighTargets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pickTgt.Count == 0) return;
        var target = rayleighTargets.First(c => c.Id.ToString() == pickTgt[0]);
        AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Active);

        // ── 效果：确认卡组顶 4 张，公开最多 1 张（红色事件 或 费用≥3 的角色）加入手牌 ──
        int peek = Math.Min(4, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var candidates = top.Where(c =>
            (c.Info.Kind == CardKind.Event && c.Info.ColorList.Contains("红")) ||
            (c.Info.Kind == CardKind.Character && c.Info.Cost >= 3)
        ).ToList();

        {
            var extra = new Dictionary<string, object?>
            {
                // 检索卡规范：choiceCards = 确认到的全部 top（让玩家看全），validChoices = 仅可公开 candidates
                ["choiceCards"] = top
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                $"确认卡组顶 {peek} 张，公开最多 1 张红色事件或费用≥3 的角色加入手牌",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 其余仍在顶部的牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
