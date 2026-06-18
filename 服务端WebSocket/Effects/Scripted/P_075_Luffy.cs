using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-075 蒙奇·D·路飞（角色 / 地 / 超新星·草帽一伙）
/// 【登场时】赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!。
/// 【攻击时】我方场上存在费用为 8 或更高的角色的场合，抽取 1 张卡牌，丢弃我方的 1 张手牌。
/// </summary>
public class P_075_Luffy : IScriptedEffect
{
    public string CardNumber => "P-075";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!
            var donTargets = new List<CardInstance> { me.Leader };
            donTargets.AddRange(me.Characters);
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择 1 张领袖或角色，赋予最多 1 张休息状态的咚!!",
                donTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var target = donTargets.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
            }
            return;
        }

        // OnAttackDeclare：条件——我方场上存在费用≥8 的角色
        bool hasBig = me.Characters.Any(c => c.Info.Cost >= 8);
        if (!hasBig) return;

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (card is not null) AtomicOps.DiscardHand(me, card);
    }
}
