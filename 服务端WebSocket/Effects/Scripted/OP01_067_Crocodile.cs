using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-067 克洛克达尔（角色 / 水 / 王下七武海·巴洛克工作室 / 7 费 7000）
/// 【流放】（纯关键词，由引擎处理）
/// 【咚!!×1】我方手牌中的蓝色事件费用-1。
///
/// 实现说明：
///   - 持续手牌费用减免（Wave5 通道）：注册 ContinuousEffect.CostDelta = -1，
///     Scope.Side=0（我方），Filter 选中"蓝色(水)事件"。引擎在打出该手牌时按 HandPlayCost 扣减。
///   - 在 OnEnterField 注册；本卡离场引擎自动清理。先 RemoveAll 同源防重复。
///   - 【流放】为引擎处理的关键词，此处不另写。
/// </summary>
public class OP01_067_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP01-067";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
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
                Filter = c => c.Info.Kind == CardKind.Event && c.Info.ColorList.Contains("蓝"),
            },
            CostDelta = -1,
            Predicate = (s, sideIdx, c) => true,
        });

        return Task.CompletedTask;
    }
}
