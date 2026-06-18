using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-002 马尔高（领袖）
/// 【咚!!×1】【启动主要】【每回合1次】抽取 1 张卡牌，将我方的 1 张手牌放回卡组最上方或最下方。
///   之后，本回合中，对方最多 1 张角色力量 -2000。
///
/// 说明 / 简化点：
///   - "放回卡组最上方或最下方"用 ChooseOption 二选一：最上方用 me.Deck.Insert(0, card)，
///     最下方用 AtomicOps.ReturnHandToDeckBottom。
///   - 【咚!!×1】为发动条件（场上至少 1 张被赋予的咚），引擎按惯例不在此处校验消耗，
///     脚本仅实现收益。
/// </summary>
public class OP08_002_Marco : IScriptedEffect
{
    public string CardNumber => "OP08-002";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 抽取 1 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 将 1 张手牌放回卡组最上方或最下方
        if (me.Hand.Count > 0)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "将 1 张手牌放回卡组最上方或最下方",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (pick.Count > 0)
            {
                var card = me.Hand.First(c => c.Id.ToString() == pick[0]);
                int mode = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "将该手牌放回卡组的位置", new[] { "最上方", "最下方" });
                if (mode == 0)
                {
                    me.Hand.Remove(card);
                    me.Deck.Insert(0, card);
                }
                else
                {
                    AtomicOps.ReturnHandToDeckBottom(me, card);
                }
            }
        }

        // 之后：本回合中，对方最多 1 张角色力量 -2000
        if (opp.Characters.Count > 0)
        {
            var debuff = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "本回合中，对方最多 1 张角色力量 -2000",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (debuff.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == debuff[0]);
                AtomicOps.AddPowerThisTurn(tgt, -2000);
            }
        }

        me.TurnOnceUsed.Add(key);
    }
}
