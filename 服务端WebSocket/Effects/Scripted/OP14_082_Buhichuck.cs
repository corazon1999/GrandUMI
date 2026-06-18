using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-082 噗嘻恰克（角色 / 地 / 2 费 / 3000 / 恐怖之船海盗团）
/// 【KO时】直到下个对方的结束阶段结束时为止，我方所有拥有《恐怖之船海盗团》特征的角色费用 +4。
///
/// 实现说明 / 简化点：
///   - 用 OnKO 钩子。对我方场上所有《恐怖之船海盗团》角色逐张施加费用 +4
///     (KeywordDuration.UntilNextOpponentEndPhase)。
///   - 自身此时已被 KO 离场，不在 Characters 中，无需排除。
///   - 【触发】(从废弃区登场 1 张费用≤2 的《恐怖之船海盗团》角色) 由触发节单独处理，不在本脚本内。
/// </summary>
public class OP14_082_Buhichuck : IScriptedEffect
{
    public string CardNumber => "OP14-082";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        foreach (var c in me.Characters.Where(c => c.Info.HasKeyword("恐怖之船海盗团")))
        {
            AtomicOps.AddCostModifier(c, 4, KeywordDuration.UntilNextOpponentEndPhase);
        }

        return Task.CompletedTask;
    }
}
