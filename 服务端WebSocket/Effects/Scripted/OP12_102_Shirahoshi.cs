using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-102 白星（角色）
/// 【对方的回合中】我方场上没有其他原本费用为2的"白星"的场合，
///   我方所有拥有《海王类》特征的角色力量+2000。
///   —— 通过 ContinuousEffect 注册：仅对方回合、且我方场上无其他原本费用2的"白星"时生效，
///      作用于我方所有含《海王类》特征的角色。
///
/// 简化点（部分效果判 complex，本脚本只实现持续力量修正部分）：
///   - 文本另含"我方原本费用≤6的角色因对方效果将要离场时可改为翻开生命牌使其不离场"的
///     离场替代效果（无对应触发钩子，引擎无该机制）——未实现。
///   - 本脚本仅实现可由 ContinuousEffect 承载的条件性全体《海王类》+2000。
/// </summary>
public class OP12_102_Shirahoshi : IScriptedEffect
{
    public string CardNumber => "OP12-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("海王类"),
            },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                s.CurrentTurnPlayer != owner &&
                // 我方场上没有"其他"原本费用为2的"白星"
                !s.Players[owner].Characters.Any(c =>
                    c.Id != selfId && c.Info.Cost == 2 && c.Info.Name.Contains("白星")),
        });

        return Task.CompletedTask;
    }
}
