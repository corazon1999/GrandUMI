using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-100 马歇尔·D·提奇（角色 / 地 / 四皇・黑胡子海盗团，cost10 power12000）
/// 【攻击时】本回合中，对方所有的领袖和角色效果无效。
///
/// 实现说明：
///   - 群体效果无效用 ContinuousEffect.NullifyEffect=true（Side=1 对方，含领袖与角色）。
///   - 「本回合中」用 Predicate 限定到注册时的回合数（TurnCount==baseTurn），回合结束后自动失效。
///   - 来源卡离场由引擎清理；同一卡多次攻击先去重。
/// </summary>
public class P_100_Teach : IScriptedEffect
{
    public string CardNumber => "P-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int baseTurn = ctx.State.TurnCount;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = true, IncludeCharacters = true },
            NullifyEffect = true,
            Predicate = (s, sideIdx, card) => s.TurnCount == baseTurn,
        });

        return Task.CompletedTask;
    }
}
