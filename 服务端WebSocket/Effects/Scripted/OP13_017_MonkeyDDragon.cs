using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-017 蒙奇·D·龙（角色 / 炎）
/// 【每回合1次】我方拥有《革命军》特征的角色因对方的效果将要离开场上的场合，可以改为此角色在本回合中力量-2000，使该角色不离场。
/// 成本：将该《革命军》角色本回合力量-2000。
/// </summary>
public class OP13_017_MonkeyDDragon : IScriptedEffect
{
    public string CardNumber => "OP13-017";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || !victim.Info.HasKeyword("革命军")) return;
        var key = "OP13-017-leaveguard" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"蒙奇·D·龙：使「{victim.Info.Name}」本回合力量-2000，令其不离场？");
        if (!use) return;
        me.TurnOnceUsed.Add(key);
        AtomicOps.AddPowerThisTurn(victim, -2000);
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
