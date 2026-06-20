using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-052 卡巴奇（角色 / 水 cost2）
/// 【登场时】我方场上存在"毛奇"的场合，抽取 2 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现说明：
///   - 条件：我方场上(领袖/角色)存在名为"毛奇"的卡。用 MatchesName 判定。
///   - 满足则抽 2 张，然后丢弃我方 1 张手牌（玩家选择）。
/// </summary>
public class OP02_052_Cabaji : IScriptedEffect
{
    public string CardNumber => "OP02-052";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        bool hasMoji = me.Characters.Any(c => c.MatchesName("毛奇")) || me.Leader.MatchesName("毛奇");
        if (!hasMoji) return;

        AtomicOps.Draw(ctx.State, owner, 2);

        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃我方的 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count > 0)
        {
            var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.DiscardHand(me, card);
        }
    }
}
