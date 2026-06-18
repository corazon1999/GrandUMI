using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-054 多分支选项事件示例
/// 【主要】我方领袖为"路西"的场合，选择以下的 1 项：
///   · 抽取 2 张卡牌，丢弃我方的 1 张手牌。
///   · 将最多 1 张舞台放回其持有者的手牌。
/// </summary>
public class OP15_054_Lebecca : IScriptedEffect
{
    public string CardNumber => "OP15-054";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.NameIs("路西")) return;

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择 1 项",
            new[] { "抽 2 丢 1", "把任意舞台放回手牌" });

        if (opt == 0)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
            if (me.Hand.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                    "选择 1 张要丢弃的手牌",
                    me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
                if (chosen.Count > 0)
                {
                    var dc = me.Hand.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.DiscardHand(me, dc);
                }
            }
        }
        else if (opt == 1)
        {
            // 双方舞台都可作为目标
            var stages = new List<(int owner, CardInstance card)>();
            for (int i = 0; i < 2; i++)
                if (ctx.State.Players[i].StageCard is { } sc) stages.Add((i, sc));
            if (stages.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyStage",
                "选择最多 1 张舞台放回手牌",
                stages.Select(t => t.card.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var pick = stages.First(t => t.card.Id.ToString() == chosen[0]);
                AtomicOps.BounceToHand(ctx.State, pick.owner, pick.card);
            }
        }
    }
}
