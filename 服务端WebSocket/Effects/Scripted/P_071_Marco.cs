using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-071 马尔高（角色 / 水 4 费 6000 / 白胡子海盗团）
/// 【KO时】可以将此角色卡牌加入手牌。
///
/// 实现说明：
///   - 【KO时】触发时此角色已离场进入废弃区。"可以"型可选效果，先 ConfirmOptional。
///   - 将自身从废弃区加入手牌：TrashToHand(me, self)（self 此时应在 me.Trash 中）。
/// </summary>
public class P_071_Marco : IScriptedEffect
{
    public string CardNumber => "P-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 此卡须确在废弃区（已被KO）
        if (!me.Trash.Contains(self)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "马尔高【KO时】：将此角色卡牌加入手牌？");
        if (!use) return;

        AtomicOps.TrashToHand(me, self);
    }
}
