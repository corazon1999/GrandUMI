using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-048 堂吉诃德·罗西南德（角色 / 蓝 / 1费）
/// 【对方的回合中】我方拥有《海军》特征的蓝色角色因对方的效果将要离开场上的场合，
///   可以改为将此角色转为休息状态，并丢弃我方1张手牌，使该角色不离场。
/// 实现：OnAllyWillLeaveField 守护（仅"对方效果"离场，含KO/退手/回卡组/置生命）；
///   限对方回合；victim=我方《海军》蓝色角色；成本=横置自身+丢1手牌；MarkPreventLeave 取消离场。
/// </summary>
public class OP12_048_Rosinante : IScriptedEffect
{
    public string CardNumber => "OP12-048";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.State.CurrentTurnPlayer == owner) return;    // 仅【对方的回合中】

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;
        if (!victim.Info.HasKeyword("海军")) return;           // 《海军》特征
        if (!victim.Info.ColorList.Contains("蓝")) return;     // 蓝色
        if (self.IsTapped) return;                             // 成本：自身需活跃
        if (me.Hand.Count == 0) return;                        // 成本：需手牌可弃

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"罗西南德：将此角色转为休息状态并丢弃1张手牌，使「{victim.Info.Name}」不离场？");
        if (!use) return;

        // 先确认弃牌（玩家可在此放弃 → 不支付任何成本、不守护）
        var ch = await ctx.Prompts.ChooseCards(owner, "OwnHand", "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (ch.Count < 1) return;
        var disc = me.Hand.First(c => c.Id.ToString() == ch[0]);

        AtomicOps.RestCard(self);
        AtomicOps.DiscardHand(me, disc);
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
