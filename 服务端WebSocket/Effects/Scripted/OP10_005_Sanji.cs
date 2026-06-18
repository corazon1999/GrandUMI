using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-005 山智（角色 / 炎 3 费 3000，班克禁区/草帽一伙）
/// 【我方的回合中】此角色的力量+3000。
/// 【KO时】抽取1张卡牌。
///
/// 实现说明：
/// - 第一段为自身在「我方回合中」的持续力量修正 → ContinuousEffect 注册（OnEnterField 时机），
///   Predicate 限定我方回合（s.CurrentTurnPlayer == owner），仅作用于自身。
/// - 第二段 OnKO 时抽 1 张。
/// </summary>
public class OP10_005_Sanji : IScriptedEffect
{
    public string CardNumber => "OP10-005";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId = self.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 3000,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId && s.CurrentTurnPlayer == owner,
            });
            return Task.CompletedTask;
        }

        // 【KO时】抽取 1 张卡牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}
