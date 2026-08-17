using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-086 吉扎斯·巴杰斯（角色）
/// 此角色不会因对方的效果而被KO。
/// 我方领袖拥有《黑胡子海盗团》特征的场合，我方废弃区中每有4张卡牌，此角色的力量+1000。
/// 实现说明：
///   - "不会因对方效果被KO"→ ContinuousEffect.KoGuard="effect"（引擎无法区分是否"对方的"效果，近似为任意效果KO均免）。
///   - 这两项均为静态能力，并非【登场时】效果；卡牌数据不再伪造 OnEnterField 标签，
///     因此 OP09-081 只会无效真正的【登场时】，不会阻止本卡初始化持续能力。
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

        // 持续：领袖含《黑胡子海盗团》时，废弃区每4张 +1000；按实时数量计算，不设人为上限。
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDeltaResolver = (s, _, _) => s.Players[owner].Trash.Count / 4 * 1000,
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId
                && s.Players[owner].Leader.Info.HasKeyword("黑胡子海盗团"),
        });

        return Task.CompletedTask;
    }
}
