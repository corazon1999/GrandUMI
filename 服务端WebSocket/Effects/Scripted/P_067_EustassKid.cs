using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-067 尤斯塔斯·基德（角色 / 风 5 费 6000 / 超新星・基德海盗团）
/// 此角色处于休息状态的场合，对方只能攻击角色“尤斯塔斯·基德”。
///
/// 实现说明：
///   - 强制改变攻击对象：当此角色处于休息状态时，对方的任何攻击（无论原目标为领袖或其他角色）
///     都被重定向到此角色自身。
///   - 用 OnOppAttackDeclare 钩子；此时 ctx.State.CurrentBattle 已建立（我方为防守方），
///     直接改其目标字段（12.3 攻击目标重定向）。
///   - 仅当本卡确处于休息状态（IsTapped）时才重定向；若攻击对象已是本卡则无需改动。
/// </summary>
public class P_067_EustassKid : IScriptedEffect
{
    public string CardNumber => "P-067";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;

        // 仅在此角色处于休息状态时强制对方只能攻击本角色
        if (!self.IsTapped) return Task.CompletedTask;

        var b = ctx.State.CurrentBattle;
        if (b is null) return Task.CompletedTask;

        // 已指向本卡则无需改动
        if (!b.TargetIsLeader && b.TargetCardId == self.Id) return Task.CompletedTask;

        b.TargetIsLeader = false;
        b.TargetCardId = self.Id;
        return Task.CompletedTask;
    }
}
