using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-052 雷欧（角色 / 水）
/// 我方原本的力量不高于7000的角色因对方的效果将要离开场上的场合，可以改为将我方1张角色放回其持有者的卡组最下方，使该角色不离场。
/// 成本"将我方1张角色放回卡组底"由我方主动发起(actingSide=我方)，不会再触发离场守护。
/// </summary>
public class OP15_052_Leo : IScriptedEffect
{
    public string CardNumber => "OP15-052";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Power > 7000) return;
        if (me.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"雷欧：将我方1张角色放回卡组最下方，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter", "将我方1张角色放回卡组最下方",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (ch.Count == 0) return;
        var cost = me.Characters.FirstOrDefault(c => c.Id.ToString() == ch[0]);
        if (cost is null) return;
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, cost);
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
