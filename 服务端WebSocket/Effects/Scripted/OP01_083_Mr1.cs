using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-083 Mr.1(达兹·波涅斯)（角色 / 水 / 巴洛克工作室）
/// 【咚!!×1】【我方的回合中】我方领袖拥有《巴洛克工作室》特征的场合，
///   我方废弃区中每有2张事件，此角色的力量+1000。
///
/// 实现说明：
///   - 纯持续/动态力量修正，用 ContinuousEffect 注册（登场时机）。
///   - PowerDelta 为固定值，无法直接表达"每2张事件+1000"，故注册多条各 +1000 的阈值持续效果
///     （废弃区事件≥2,≥4,...,≥40 各 +1000），累加即等价（覆盖远超实战上限）。
///   - 生效条件：我方回合中 + 此卡至少被赋予1张咚!!（咚!!×1）+ 领袖含《巴洛克工作室》。
/// </summary>
public class OP01_083_Mr1 : IScriptedEffect
{
    public string CardNumber => "OP01-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        for (int i = 1; i <= 20; i++)
        {
            int threshold = i * 2;
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.CurrentTurnPlayer == owner &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                    s.Players[owner].Leader.Info.HasKeyword("巴洛克工作室") &&
                    s.Players[owner].Trash.Count(c => c.Info.Kind == CardKind.Event) >= threshold,
            });
        }

        return Task.CompletedTask;
    }
}
