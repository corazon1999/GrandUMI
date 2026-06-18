using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-081 杰拉基尔·米霍克（角色 / 水 / 十字公会）
/// 【启动主要】可以将此角色放回其持有者的手牌：我方场上存在 3 张或更多拥有《十字公会》特征的
///   蓝色角色的场合，将我方手牌中最多 1 张费用为 5 且拥有《十字公会》特征的角色卡牌登场。
///
/// 实现说明：成本"将此角色放回手牌"无 DSL 通道，脚本实现。
/// 注：条件计数在支付成本(放回自身)前进行（自身亦计入"3 张或更多"判定）。
/// </summary>
public class P_081_Mihawk : IScriptedEffect
{
    public string CardNumber => "P-081";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方场上 ≥3 张《十字公会》特征的蓝色角色（含自身）
        int crossBlue = me.Characters.Count(c =>
            c.Info.HasKeyword("十字公会") && c.Info.ColorList.Contains("蓝"));
        if (crossBlue < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "米霍克【启动主要】：将此角色放回手牌，登场 1 张费用 5 的《十字公会》角色？");
        if (!use) return;

        // 成本：将此角色放回持有者手牌
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 效果：登场手牌中最多 1 张费用 5 且《十字公会》的角色
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost == 5 &&
            c.Info.HasKeyword("十字公会")).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场 1 张费用 5 的《十字公会》角色（最多 1 张）",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;
        var picked = playable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
