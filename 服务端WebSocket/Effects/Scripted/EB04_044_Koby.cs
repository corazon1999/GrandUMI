using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-044 可比（角色 / 地 / 海军・利刃，cost6 power7000）
/// 【每回合1次】我方领袖拥有〈海军〉特征，且此角色将要离开场上的场合，
///   可以改为丢弃我方的 1 张手牌，使此角色不离场。
/// 【我方的回合中】【每回合1次】当对方的角色被KO时，抽取 1 张卡牌。
///
/// 实现说明（M6）：
///   - 自身防离场置换走 PreKO（受害者自身钩子）：覆盖战斗KO与DSL效果KO；
///     退回手牌/放回卡组等非KO离场不经 PreKO，本卡此情形未覆盖（已文档化）。
///   - 第二段走 OnAnyCharKOd（战斗/效果KO均派发）：我方回合中、被KO者属对方、每回合1次 → 抽1。
///   - 两段各自独立的【每回合1次】计数。effectTags 已设为 ["PreKO","OnAnyCharKOd"]。
/// </summary>
public class EB04_044_Koby : IScriptedEffect
{
    public string CardNumber => "EB04-044";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.PreKO || t == EffectTrigger.OnAnyCharKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            var key = ctx.Source.Info.Number + "-guard" + ":" + ctx.Source.Id;
            if (me.TurnOnceUsed.Contains(key)) return;
            if (!me.Leader.Info.HasKeyword("海军")) return;
            if (me.Hand.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "可比【每回合1次】：丢弃我方 1 张手牌，使此角色不离场（不被KO）？");
            if (!use) return;

            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃 1 张手牌作为置换成本", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (pick.Count == 0) return;
            var disc = me.Hand.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.DiscardHand(me, disc);

            ctx.State.MarkPreventKO(ctx.Source.Id);
            me.TurnOnceUsed.Add(key);
            return;
        }

        // OnAnyCharKOd：我方回合中，对方角色被KO时抽1
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
        var koOwner = ctx.Vars.TryGetValue("owner", out var o) && o is int oi ? oi : -1;
        if (koOwner != 1 - ctx.OwnerIndex) return; // 仅对方角色被KO
        var dkey = ctx.Source.Info.Number + "-draw" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(dkey)) return;
        me.TurnOnceUsed.Add(dkey);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
