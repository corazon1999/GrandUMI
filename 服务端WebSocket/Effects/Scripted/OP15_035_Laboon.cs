using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-035 拉布（角色 / 风）
/// 我方原本的力量不高于7000的角色因对方的效果将要离开场上的场合，可以改为将我方的2张卡牌转为休息状态，使该角色不离场。
/// "2张卡牌"以我方活跃角色近似(逐张选最多2张转休息)。
/// </summary>
public class OP15_035_Laboon : IScriptedEffect
{
    public string CardNumber => "OP15-035";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Power > 7000) return;
        var actives = me.Characters.Where(c => !c.IsTapped).ToList();
        if (actives.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"拉布：将我方2张角色转为休息状态，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter", "将我方2张角色转为休息状态",
            actives.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (ch.Count < 2) return;
        foreach (var id in ch)
        {
            var c = me.Characters.FirstOrDefault(x => x.Id.ToString() == id);
            if (c is not null) AtomicOps.RestCard(c);
        }
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
