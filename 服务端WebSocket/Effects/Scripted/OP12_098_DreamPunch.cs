using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-098 梦打击处理拳（事件）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///   之后，我方场上存在费用为 8 或更高且拥有《革命军》特征的角色的场合，
///   本次战斗中，该卡牌（被选中的领袖或角色）的力量再 +2000。
/// 【触发】抽取 1 张卡牌，将我方卡组最上方的 1 张卡牌放置到废弃区。
/// </summary>
public class OP12_098_DreamPunch : IScriptedEffect
{
    public string CardNumber => "OP12-098";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】抽 1 张 + 卡组顶 1 张入废弃区
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            AtomicOps.MillTop(me, 1);
            return;
        }

        // 【反击】选我方最多 1 张领袖或角色 +2000（本次战斗）
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多 1 张领袖或角色力量 +2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        AtomicOps.AddPowerThisBattle(target, 2000);

        // 之后：我方场上存在费用≥8 且拥有《革命军》特征的角色 → 该卡牌再 +2000
        bool hasRevolutionary8 = me.Characters.Any(c => c.Info.Cost >= 8 && c.Info.HasKeyword("革命军"));
        if (hasRevolutionary8)
            AtomicOps.AddPowerThisBattle(target, 2000);
    }
}
