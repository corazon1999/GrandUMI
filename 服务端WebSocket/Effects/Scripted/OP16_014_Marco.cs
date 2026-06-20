using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-014 马尔高（角色 / 炎 6 费 8000，白胡子海盗团）
/// 持续：我方角色因对方的效果将要离开场上的场合，可以改为将此角色 KO，使该角色不离场。
/// 【KO时】可以丢弃我方手牌中 1 张力量为 8000 的角色卡牌：从废弃区中登场此角色卡牌。
///
/// 实现说明 / 简化点：
///   - "持续替身保护(其他角色将离场→改KO自身)"为持续监听他卡离场的静态机制，PreKO 仅能拦截
///     被KO卡自身、无法持续监听其他角色，引擎无此通道，未实现(仅注释保留)。
///   - 仅实现可脚本的【KO时】：此卡被KO进入废弃区后触发；成本为可选丢弃 1 张力量8000角色手牌，
///     收益为从废弃区将此角色登场(活跃状态)。
/// </summary>
public class OP16_014_Marco : IScriptedEffect
{
    public string CardNumber => "OP16-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 此角色须确实在废弃区（被 KO 后由引擎放入）
        if (!me.Trash.Contains(ctx.Source)) return;

        // 成本：丢弃我方手牌中 1 张力量为 8000 的角色卡牌（可选）
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Power == 8000).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "马尔高【KO时】：丢弃 1 张力量8000的角色手牌，从废弃区登场此角色?");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方手牌中 1 张力量为 8000 的角色卡牌",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count < 1) return;
        var discard = cands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.DiscardHand(me, discard);

        // 收益：从废弃区登场此角色
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
    }
}
