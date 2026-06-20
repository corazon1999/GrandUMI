using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-046 朵尔（角色 / 地 / 艾格赫德・海军）
/// 【阻挡者】（关键词，由引擎处理）
/// 【对方的回合中】我方所有拥有《海军》特征的角色费用 +2。
///
/// 实现说明：
///   - 【阻挡者】为关键词，引擎自行处理，脚本只实现持续费用修正部分。
///   - 持续费用修正用 ContinuousEffect.CostDelta（+2）注册（OnEnterField）。
///   - Scope.Side=0 同方，仅角色（IncludeCharacters）；Filter 限《海军》特征。
///   - Predicate 限定对方回合中生效（CurrentTurnPlayer != owner）。
/// </summary>
public class EB04_046_Dol : IScriptedEffect
{
    public string CardNumber => "EB04-046";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("海军"),
            },
            CostDelta = 2,
            Predicate = (s, sideIdx, card) => s.CurrentTurnPlayer != owner,
        });

        return Task.CompletedTask;
    }
}
