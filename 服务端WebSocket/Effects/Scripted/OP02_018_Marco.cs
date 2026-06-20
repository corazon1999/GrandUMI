using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-018 马尔高（角色 / 炎 / 白胡子海盗团）
/// 【阻挡者】（关键词，引擎处理）
/// 【KO时】可以丢弃我方手牌中1张拥有的特征中包含〈白胡子海盗团〉的卡牌：
///   我方生命卡牌不多于2张的场合，从废弃区中将此角色卡牌以休息状态登场。
///
/// 实现说明：成本=可选弃1张含〈白胡子海盗团〉的手牌（ConfirmOptional + 选弃）。
///   收益条件：我方生命 ≤2，且自身已在废弃区，则以休息状态登场（PlayFromTrashFree restState:true）。
/// </summary>
public class OP02_018_Marco : IScriptedEffect
{
    public string CardNumber => "OP02-018";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 需要可弃的手牌（含〈白胡子海盗团〉）
        var discardable = me.Hand.Where(c => c.Info.HasKeyword("白胡子海盗团")).ToList();
        if (discardable.Count == 0) return;

        // 收益前提：自身在废弃区、且生命 ≤2
        if (!me.Trash.Contains(self)) return;
        if (me.LifeCount > 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "马尔高【KO时】：丢弃1张含〈白胡子海盗团〉的手牌，将此角色从废弃区以休息状态登场？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = discardable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张含〈白胡子海盗团〉的手牌",
            discardable.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count < 1) return;

        var toDiscard = discardable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 仍需自身在废弃区方可登场
        if (me.Trash.Contains(self))
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self, restState: true);
    }
}
