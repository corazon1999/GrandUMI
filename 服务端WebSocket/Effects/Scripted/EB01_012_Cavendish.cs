using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-012 凯文迪修（角色 / 风 / 超新星·俊美海盗团）
/// 【登场时】/【攻击时】我方领袖拥有《超新星》特征，且我方场上不存在其他角色"凯文迪修"的场合，
///   将我方最多2张咚!!转为活跃状态。
///
/// 实现说明：
///   - 条件：领袖含《超新星》且我方场上无其他名为"凯文迪修"的角色（自身除外）。
///   - 将费用区最多2张休息状态的咚!!转为活跃状态。
/// </summary>
public class EB01_012_Cavendish : IScriptedEffect
{
    public string CardNumber => "EB01-012";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (!me.Leader.Info.HasKeyword("超新星")) return Task.CompletedTask;
        // 场上不存在其他角色"凯文迪修"
        if (me.Characters.Any(c => c.Id != self.Id && c.MatchesName("凯文迪修"))) return Task.CompletedTask;

        // 将最多2张休息咚转为活跃
        int n = 0;
        foreach (var d in me.CostArea)
        {
            if (n >= 2) break;
            if (d.State == DonState.Rest) { d.State = DonState.Active; n++; }
        }

        return Task.CompletedTask;
    }
}
