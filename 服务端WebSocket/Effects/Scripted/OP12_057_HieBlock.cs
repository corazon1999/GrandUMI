using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-057 冰块 暴雉嘴（事件 / 海军）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +4000。之后，丢弃我方的 1 张手牌。
/// 【触发】可以丢弃我方的 1 张手牌：抽取 1 张卡牌。
///
/// 说明：
///   - 反击：先让玩家选最多 1 张我方领袖或角色（可选，min=0），本次战斗 +4000；
///     "之后" 丢弃我方 1 张手牌为强制（手牌为空则跳过）。
///   - 触发：可选成本——询问是否弃 1 张手牌；同意则弃 1 张并抽 1 张。
///   - 丢弃手牌经 extra.choiceCards 下发卡面给客户端。
/// </summary>
public class OP12_057_HieBlock : IScriptedEffect
{
    public string CardNumber => "OP12-057";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
            await ResolveCounter(ctx);
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await ResolveTrigger(ctx);
    }

    // 【反击】最多 1 张领袖或角色 +4000（本战斗），之后强制弃 1 张手牌
    private static async Task ResolveCounter(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗力量+4000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 4000);
        }

        // 之后：强制丢弃 1 张手牌
        await DiscardOne(ctx, me, "之后：丢弃我方 1 张手牌");
    }

    // 【触发】可以丢弃 1 张手牌：抽 1 张
    private static async Task ResolveTrigger(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "冰块 暴雉嘴【触发】：丢弃我方 1 张手牌，抽 1 张卡牌？");
        if (!use) return;

        bool discarded = await DiscardOne(ctx, me, "丢弃我方 1 张手牌");
        if (discarded) AtomicOps.Draw(s, ctx.OwnerIndex, 1);
    }

    private static async Task<bool> DiscardOne(EffectContext ctx, PlayerState me, string prompt)
    {
        if (me.Hand.Count == 0) return false;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "HieBlockDiscard",
            prompt, me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        var card = chosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0])
            : me.Hand[0];
        if (card is null) return false;
        AtomicOps.DiscardHand(me, card);
        return true;
    }
}
