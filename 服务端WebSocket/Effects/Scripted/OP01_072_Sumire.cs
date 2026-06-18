using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-072 史姆丽（角色 / 水 / 生物兵器·班克禁区）
/// 【咚!!×1】【我方的回合中】我方每有1张手牌，此角色的力量+1000。
///
/// 实现说明：
///   - 纯持续/动态力量修正，用 ContinuousEffect 注册（登场时机）。
///   - PowerDelta 为固定值，无法直接表达"每1张手牌+1000"，故注册多条各 +1000 的阈值持续效果
///     （手牌≥1,≥2,...,≥20 各 +1000），累加即等价于"每张手牌+1000"（覆盖远超实战上限）。
///   - 仅在【我方回合中】且此卡至少被赋予 1 张咚!! 时生效（咚!!×1 激活条件）。
/// </summary>
public class OP01_072_Sumire : IScriptedEffect
{
    public string CardNumber => "OP01-072";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        for (int i = 1; i <= 20; i++)
        {
            int threshold = i;
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.CurrentTurnPlayer == owner &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                    s.Players[owner].Hand.Count >= threshold,
            });
        }

        return Task.CompletedTask;
    }
}
