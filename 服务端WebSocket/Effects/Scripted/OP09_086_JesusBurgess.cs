using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-086 吉扎斯·巴杰斯（角色）
/// 此角色不会因对方的效果而被KO。
/// 我方领袖拥有《黑胡子海盗团》特征的场合，我方废弃区中每有4张卡牌，此角色的力量+1000。
/// 实现说明：
///   - "不会因对方效果被KO"→ ContinuousEffect.KoGuard="effect"（引擎无法区分是否"对方的"效果，近似为任意效果KO均免）。
///   - "废弃区每4张+1000"为动态阶梯加成；ContinuousEffect.PowerDelta 为定值，故注册若干阶梯，
///     第 k 阶为 +1000 且仅当废弃区张数 ≥ 4*k 时生效（覆盖至 +12000，实战足够）。
/// </summary>
public class OP09_086_JesusBurgess : IScriptedEffect
{
    public string CardNumber => "OP09-086";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 持续：此角色不会因效果被KO
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, c) => c.Id == selfId,
        });

        // 持续：领袖含《黑胡子海盗团》时，废弃区每4张 +1000（阶梯式，最多 12 阶）
        for (int k = 1; k <= 12; k++)
        {
            int threshold = 4 * k;
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                Predicate = (s, sideIdx, c) =>
                    c.Id == selfId &&
                    s.Players[owner].Leader.Info.HasKeyword("黑胡子海盗团") &&
                    s.Players[owner].Trash.Count >= threshold,
            });
        }

        return Task.CompletedTask;
    }
}
