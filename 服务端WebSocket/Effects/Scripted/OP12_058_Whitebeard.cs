using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-058 我会让白胡子……成为海上之王的!（事件）
/// 【主要】我方领袖拥有的特征中包含〈白胡子海盗团〉的场合，公开我方卡组最上方的 1 张卡牌，
///   该卡牌为费用不高于 9 且拥有的特征中包含〈白胡子海盗团〉的角色卡牌的场合，可以将其登场。
///   将其登场的场合，本回合中，该角色获得【速攻】效果。
/// 【触发】抽取 1 张卡牌。
///
/// 公开的顶牌通过公共公开事件向双方展示；符合条件时再询问是否登场。
/// </summary>
public class OP12_058_Whitebeard : IScriptedEffect
{
    public string CardNumber => "OP12-058";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】抽取 1 张卡牌
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【主要】 ──
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖特征含〈白胡子海盗团〉
        if (!me.Leader.Info.HasKeyword("白胡子海盗团")) return;
        if (me.Deck.Count == 0) return;

        // 公开卡组最上方 1 张
        var top = me.Deck[0];
        ctx.BroadcastReveal(top);
        bool eligible = top.Info.Kind == CardKind.Character
                        && top.Info.Cost <= 9
                        && top.Info.HasKeyword("白胡子海盗团");
        if (!eligible)
        {
            // 不符合条件：公开后留在卡组顶（无放回底部/废弃的指示，原样保留）
            return;
        }

        // 可以将其登场
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = new[] { new { id = top.Id.ToString(), number = top.Info.Number } },
        };
        bool play = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"公开卡组顶：{top.Info.Name}（费用 {top.Info.Cost}）。是否将其登场并获得【速攻】？");
        if (!play) return;

        // 登场：从卡组顶移除，按 5 张上限处理后入场
        await AtomicOps.PlayFromDeckFree(ctx.State, ctx.OwnerIndex, top);

        // 本回合获得【速攻】
        AtomicOps.GiveKeyword(top, "速攻", KeywordDuration.ThisTurn);
    }
}
