using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-117 天上之火（事件 / 炎 / 四皇・大妈海盗团）
/// 【主要】将对方最多 1 张费用不高于 3 的角色正面朝上加入其生命区的最上方或最下方。
/// 【触发】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：将我方最多 1 张手牌加入生命区最上方。
///
/// 实现说明：
///   - 【主要】(EventMain)：将对方角色置入"对方生命区"用 MoveCharToLife(ctx.State, 1-owner, ...)，
///     顶/底由玩家二选一。
///   - 【触发】(OnLifeRevealTrigger)：可选成本=将我方生命顶/底 1 张加入手牌；
///     收益=将我方 1 张手牌置入生命区最上方。两步均为"可以/最多"故允许放弃。
/// </summary>
public class OP04_117_HeavensFire : IScriptedEffect
{
    public string CardNumber => "OP04-117";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            var cands = opp.Characters
                .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 3)
                .ToList();
            if (cands.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤3 的角色加入对方生命区",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count == 0) return;
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);

            int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "加入对方生命区的位置", new[] { "最上方", "最下方" });
            AtomicOps.MoveCharToLife(ctx.State, 1 - ctx.OwnerIndex, tgt, toTop: pos == 0);
            return;
        }

        // 【触发】可选：将我方生命顶/底 1 张加入手牌
        if (me.LifeArea.Count > 0)
        {
            int pick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "可以将我方生命区 1 张卡牌加入手牌", new[] { "最上方", "最下方", "不发动" });
            if (pick == 0 || pick == 1)
            {
                CardInstance lifeCard = pick == 0 ? me.LifeArea[0] : me.LifeArea[^1];
                me.LifeArea.Remove(lifeCard);
                lifeCard.IsLifeFaceUp = false;
                me.Hand.Add(lifeCard);

                // 收益：将我方最多 1 张手牌加入生命区最上方
                if (me.Hand.Count > 0)
                {
                    var toLife = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                        "将我方最多 1 张手牌加入生命区最上方",
                        me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1);
                    if (toLife.Count > 0)
                    {
                        var card = me.Hand.First(c => c.Id.ToString() == toLife[0]);
                        AtomicOps.HandToLife(me, card, toTop: true, faceUp: false);
                    }
                }
            }
        }
    }
}
