using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-056 绵津见（角色 / 水）
/// 此角色无法攻击。当我方手牌因效果而被丢弃时，本回合中，此角色的效果无效。
/// 实现："此角色无法攻击"为 abilities 关键词，由 ActionValidator 自动禁攻；
/// 监听 OnHandDiscarded（我方手牌因效果被丢弃），本回合令自身效果无效——
/// 据 Wave5 约定，"此角色无法攻击"被动在自身效果无效期间解除，故本回合此角色可攻击。
/// </summary>
public class OP14_056_Watatsumi : IScriptedEffect
{
    public string CardNumber => "OP14-056";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnHandDiscarded;

    public Task Resolve(EffectContext ctx)
    {
        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return Task.CompletedTask;       // 仅我方手牌被丢弃
        AtomicOps.NullifyEffects(ctx.Source, KeywordDuration.ThisTurn);
        return Task.CompletedTask;
    }
}
