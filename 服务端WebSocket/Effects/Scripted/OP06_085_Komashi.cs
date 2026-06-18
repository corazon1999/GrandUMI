using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-085 柯玛希（角色）
/// 【咚!!×2】【我方的回合中】我方废弃区中每有 5 张卡牌，此角色的力量 +1000。
///
/// 说明 / 实现：
///   - 纯持续/静态力量修正，用 ContinuousEffect 注册（在登场时机）。
///   - PowerDelta 为固定值，无法直接表达"每 5 张 +1000"的动态加成；
///     故注册多条各 +1000 的持续效果，分别以阈值 5/10/15/.../50 张废弃区为生效条件，
///     累加即等价于"每 5 张 +1000"（覆盖到 50 张废弃，远超实战上限）。
///   - 仅在【我方的回合中】生效：Predicate 判定 s.CurrentTurnPlayer == owner。
///   - 离场时引擎按 SourceCardId 自动清理全部条目。
/// </summary>
public class OP06_085_Komashi : IScriptedEffect
{
    public string CardNumber => "OP06-085";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 防重复
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 每 5 张废弃 +1000：注册 10 条阈值 +1000 持续效果（5,10,...,50）
        for (int i = 1; i <= 10; i++)
        {
            int threshold = i * 5;
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.CurrentTurnPlayer == owner &&
                    s.Players[owner].Trash.Count >= threshold,
            });
        }

        return Task.CompletedTask;
    }
}
