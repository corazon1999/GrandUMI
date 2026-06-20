using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-077 兰德鲁夫（角色 1 费 2000，Homies/大妈海盗团）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，直到下个对方的回合结束时为止，
/// 我方最多1张拥有《大妈海盗团》特征的角色费用+2。
///
/// 实现：反应式 watcher OnDonReturnedToDeck（当我方咚放回咚卡组时派发）。
///   - 仅在我方回合触发，每回合 1 次。
///   - 让玩家选择最多 1 张《大妈海盗团》角色，对其注册 ContinuousEffect.CostDelta=+2，
///     用 TurnCount 限定到"下个对方回合结束"（注册时记录基准回合数，TurnCount<=base+1 期间生效）。
///   - 该限时持续效果用唯一 SourceCardId（含 TurnCount），避免覆盖本卡其它持续效果；
///     过期后 Predicate 恒为 false 自动失效。
/// </summary>
public class OP11_077_Randolph : IScriptedEffect
{
    public string CardNumber => "OP11-077";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != owner) return;

        // 【每回合1次】
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 选择最多 1 张《大妈海盗团》特征的我方角色
        var cands = me.Characters.Where(c => c.Info.HasKeyword("大妈海盗团")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(owner, "OwnCharacter",
            "选择最多 1 张《大妈海盗团》特征的角色，费用+2（直到下个对方回合结束）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = cands.First(c => c.Id.ToString() == chosen[0]);
        var targetId = target.Id;

        me.TurnOnceUsed.Add(key);

        // 注册限时持续费用 +2，有效到下个对方回合结束（TurnCount <= baseTurn + 1）
        int baseTurn = ctx.State.TurnCount;
        string srcId = self.Id.ToString() + "-cost-" + baseTurn;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == srcId);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = srcId,
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Id == targetId,
            },
            CostDelta = 2,
            Predicate = (s, sideIdx, c) => s.TurnCount <= baseTurn + 1,
        });
    }
}
