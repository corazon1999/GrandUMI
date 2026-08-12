using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-030 那就是同伴的梦想……遭别人嘲笑的时候！（事件）
/// 【反击】本回合中，我方所有角色因战斗将要被 KO 的场合，可以改为丢弃我方的 1 张手牌，
/// 使该角色不会被 KO。
/// 【触发】抽取 1 张卡牌（由 EB02_wf.json 的 DSL 继续结算）。
/// </summary>
public sealed class EB02_030_ComradesDream : IScriptedEffect
{
    public string CardNumber => "EB02-030";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventCounter;

    public Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        int baseTurn = ctx.State.TurnCount;
        string sourceId = ctx.Source.Id.ToString();

        // 同一张事件若在本回合被回收后再次发动，刷新而不是叠加重复置换。
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == sourceId);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sourceId,
            SourceCardNumber = CardNumber,
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
            },
            DiscardHandKoReplacement = "battle",
            Predicate = (state, sideIdx, card) =>
                sideIdx == owner &&
                card.Id != state.Players[owner].Leader.Id &&
                state.TurnCount == baseTurn,
        });

        return Task.CompletedTask;
    }
}
