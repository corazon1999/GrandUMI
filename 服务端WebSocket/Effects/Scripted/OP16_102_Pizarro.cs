using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-102 阿瓦罗·匹萨罗。
/// 【KO时】抽取1张卡牌，将我方手牌或废弃区中的最多1张“哈奇诺斯”登场。
/// </summary>
public sealed class OP16_102_Pizarro : IScriptedEffect
{
    public string CardNumber => "OP16-102";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);

        var candidates = me.Hand
            .Where(card => card.MatchesName("哈奇诺斯"))
            .Select(card => (card, fromHand: true))
            .Concat(me.Trash
                .Where(card => card.MatchesName("哈奇诺斯"))
                .Select(card => (card, fromHand: false)))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "OwnHandOrTrashCard",
            "将手牌或废弃区中最多1张“哈奇诺斯”登场",
            candidates.Select(item => item.card.Id.ToString()).ToList(),
            0,
            1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates
                    .Select(item => new
                    {
                        id = item.card.Id.ToString(),
                        number = item.card.Info.Number,
                        zone = item.fromHand ? "hand" : "trash",
                    })
                    .ToList(),
            });
        if (chosen.Count != 1) return;

        var picked = candidates.FirstOrDefault(item => item.card.Id.ToString() == chosen[0]);
        if (picked.card is null) return;

        // 按提示生成时的来源区提交，避免同名多副本或恢复后的陈旧选择移动错误实例。
        if (picked.fromHand)
        {
            if (me.Hand.Contains(picked.card))
                await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked.card);
        }
        else if (me.Trash.Contains(picked.card))
        {
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked.card);
        }
    }
}
