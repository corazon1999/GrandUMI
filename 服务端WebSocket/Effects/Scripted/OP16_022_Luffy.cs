using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-022 路飞（领航）
/// 启动主要每回合 1 次：我方场上仅有《因佩尔地狱》角色时，将 2 张咚转活跃
/// </summary>
public class OP16_022_Luffy : IScriptedEffect
{
    public string CardNumber => "OP16-022";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP16-022-MainOncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        // 条件：场上仅有《因佩尔地狱》角色
        if (me.Characters.Count == 0) return Task.CompletedTask;
        if (me.Characters.Any(c => !c.Info.HasKeyword("因佩尔地狱"))) return Task.CompletedTask;

        // 把最多 2 张休息咚转活跃
        int converted = 0;
        foreach (var d in me.CostArea)
        {
            if (converted >= 2) break;
            if (d.State == DonState.Rest) { d.State = DonState.Active; converted++; }
        }
        if (converted > 0) me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}
