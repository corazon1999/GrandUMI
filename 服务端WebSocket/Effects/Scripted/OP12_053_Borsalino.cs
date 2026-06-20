using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-053 波尔萨利诺（角色）
/// 【对方的回合中】我方领袖拥有《海军》特征的场合，此角色力量+1000。
///   —— 通过 ContinuousEffect 注册，仅在对方回合且我方领袖含《海军》时生效。
///
/// 简化点（部分效果判 complex，本脚本只实现持续力量修正部分）：
///   - 文本另含【每回合1次】"因对方效果将要离场时可改为弃1手牌使此角色不离场"的
///     离场替代效果（无对应触发钩子），以及【对方的回合中】持续"获得【阻挡者】"的
///     持续关键词赋予（引擎无持续关键词通道）。这两部分无法表达，未实现。
///   - 本脚本仅实现可由 ContinuousEffect 承载的"对方回合中力量+1000"。
/// </summary>
public class OP12_053_Borsalino : IScriptedEffect
{
    public string CardNumber => "OP12-053";

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
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Leader.Info.HasKeyword("海军"),
        });

        return Task.CompletedTask;
    }
}
