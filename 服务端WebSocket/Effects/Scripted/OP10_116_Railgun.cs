using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-116 电磁炮（事件）
/// 【主要】确认我方或对方生命区最上方的最多 1 张卡牌，放置到生命区的最上方或最下方。
///   之后，将对方最多 1 张费用不高于 5 的角色KO。
///
/// 实现说明 / 简化点：
///   - "确认生命区最上方 1 张并放回顶或底"：先选看我方/对方哪一侧，确认其生命顶牌，再选放顶或放底。
///   - 之后再 KO 对方 1 张费用≤5 角色。
/// </summary>
public class OP10_116_Railgun : IScriptedEffect
{
    public string CardNumber => "OP10-116";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 第一段：确认某一侧生命顶 1 张，放回顶或底 ──
        bool meHasLife = me.LifeArea.Count > 0;
        bool oppHasLife = opp.LifeArea.Count > 0;
        if (meHasLife || oppHasLife)
        {
            int side; // 0=我方 1=对方
            if (meHasLife && oppHasLife)
                side = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "确认哪一侧生命区最上方的卡牌？", new[] { "我方", "对方" });
            else
                side = meHasLife ? 0 : 1;

            var target = side == 0 ? me : opp;
            if (target.LifeArea.Count > 0)
            {
                var lifeCard = target.LifeArea[0];
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = new[] { new { id = lifeCard.Id.ToString(), number = lifeCard.Info.Number } }.ToList(),
                };
                // 仅作确认展示（必看），收益是决定放顶/放底
                await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RailgunPeekLife",
                    "确认该生命牌", new List<string> { lifeCard.Id.ToString() }, 0, 0, extra);

                int place = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "将该卡牌放置到生命区的？", new[] { "最上方", "最下方" });
                if (place == 1)
                {
                    target.LifeArea.RemoveAt(0);
                    target.LifeArea.Add(lifeCard);
                }
                // place==0 保持原位（已在最上方）
            }
        }

        // ── 第二段：KO 对方最多 1 张费用≤5 的角色 ──
        var cands = opp.Characters.Where(c => c.CurrentCost() <= 5).ToList();
        if (cands.Count == 0) return;

        var extra2 = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多1张费用≤5的对方角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra2);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
