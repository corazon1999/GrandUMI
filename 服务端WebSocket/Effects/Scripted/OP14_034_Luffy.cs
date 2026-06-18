using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-034 蒙奇·D·路飞（角色）
/// 【我方的回合中】我方所有原本的费用为 4 或更高、且拥有《草帽一伙》特征的绿色角色力量 +1000。
///   —— 通过 ContinuousEffect 注册（持续力量修正，仅在我方回合中生效）。
/// 【每回合1次】我方拥有《草帽一伙》特征的角色因对方效果将要被 KO 时，可改为将我方 1 张角色
///   转为休息状态使其不被 KO。 —— KO 替换机制，引擎无对应通道，本卡仅实现持续力量部分。
///
/// 实现说明 / 简化点：
///   - "原本的费用 4 或更高" 取卡面原始费用 c.Info.Cost。
///   - "绿色" 取元素色 "绿"（本批卡 color 字段均为 "绿"）。
///   - 持续效果在登场时注册，离场由引擎清理；重复登场前先去重避免叠加。
/// </summary>
public class OP14_034_Luffy : IScriptedEffect
{
    public string CardNumber => "OP14-034";

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
                Filter = c => c.Info.Cost >= 4
                    && c.Info.HasKeyword("草帽一伙")
                    && c.Info.ColorList.Contains("绿"),
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) => s.CurrentTurnPlayer == owner,
        });

        return Task.CompletedTask;
    }
}
