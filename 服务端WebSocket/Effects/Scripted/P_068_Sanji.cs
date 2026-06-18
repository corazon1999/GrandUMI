using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-068 山智（角色 / 水 3 费 4000 / GERMA王国・温思默克家）
/// 【启动主要】可以将此角色放置到废弃区：确认我方卡组最上方的5张卡牌，
///   将其自选顺序排列并放回卡组的最上方或最下方。
///
/// 实现说明：
///   - 成本：将此角色放置到废弃区（"可以"型可选），用 KOCard 同型——这里直接将自身从场上
///     移到废弃区（用 ReturnFieldToDeckBottom 不合适，应进废弃区，使用 BattleEngine.KOCard 不可调，
///     故用 AtomicOps 无直接“self入废弃”接口——改用引擎离场：把自身从 Characters 移除入 Trash）。
///   - 实际上脚本可直接操作：me.Characters.Remove(self); me.Trash.Add(self)。
///   - 效果：确认卡组顶5张，自选顺序放回卡组最上方或最下方，用 ReorderTopK。
/// </summary>
public class P_068_Sanji : IScriptedEffect
{
    public string CardNumber => "P-068";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "山智【启动主要】：将此角色放置到废弃区，确认卡组顶5张并自选顺序放回卡组顶/底？");
        if (!use) return;

        // 成本：将此角色放置到废弃区
        if (me.Characters.Remove(self))
            me.Trash.Add(self);
        // 离场后清理本卡注册的持续效果（保险）
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == self.Id.ToString());

        // 效果：确认卡组顶5张，自选顺序放回顶/底
        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "SanjiReorder",
            "确认卡组顶5张，自选顺序排列后放置到卡组最上方或最下方",
            top.Select(c => c.Id.ToString()).ToList(), k, k, extra);

        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将这些卡牌放置到卡组的位置", new[] { "最上方", "最下方" });

        var order = ordered
            .Select(id => top.FirstOrDefault(c => c.Id.ToString() == id))
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .ToList();
        foreach (var c in top)
            if (!order.Contains(c.Id)) order.Add(c.Id);

        AtomicOps.ReorderTopK(me, order, toBottom: where == 1);
    }
}
