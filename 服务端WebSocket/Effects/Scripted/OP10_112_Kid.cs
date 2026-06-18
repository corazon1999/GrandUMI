using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-112 尤斯塔斯·基德（角色）
/// 【登场时】可以将此角色转为休息状态：将对方生命区最上方的最多 1 张卡牌放置到废弃区。
/// 【我方的回合结束时】对方生命卡牌不多于 2 张的场合，抽取 1 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现说明：
///   - 登场时为可选成本（横置自身），用 ConfirmOptional 询问后 RestCard，再将对方生命顶移入对方废弃区。
///   - 回合结束时抽 1 后由玩家选 1 张手牌丢弃。
/// </summary>
public class OP10_112_Kid : IScriptedEffect
{
    public string CardNumber => "OP10-112";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (opp.LifeArea.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "基德【登场时】：将此角色转为休息状态，把对方生命区最上方 1 张放置到废弃区？");
            if (!use) return;

            // 成本：横置自身
            AtomicOps.RestCard(self);

            // 效果：对方生命顶 → 对方废弃区
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Trash.Add(top);
        }
        else // OnMyTurnEnd
        {
            if (opp.LifeArea.Count > 2) return;

            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

            if (me.Hand.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                    "丢弃我方的 1 张手牌",
                    me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
                if (chosen.Count > 0)
                {
                    var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.DiscardHand(me, card);
                }
            }
        }
    }
}
