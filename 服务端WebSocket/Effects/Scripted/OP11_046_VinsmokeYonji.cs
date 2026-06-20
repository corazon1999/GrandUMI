using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-046 温思默克·勇智（角色 / 水 / 温思默克家・GERMA 66）
/// 【阻挡者】（引擎关键词，自动处理）
/// 我方角色仅为拥有的特征中包含〈GERMA〉的角色的场合，此角色不会因对方效果而被KO和转为休息状态。
///
/// 实现说明：
///   - 条件"我方角色全部都含〈GERMA〉特征"用谓词判定（场上每张角色都 HasKeyword("GERMA")）。
///   - "不会因对方效果被KO" → 条件性 KoGuard="effect"（仅本卡）。
///   - "不会因对方效果转为休息状态" → 条件性 GrantRestriction=CannotBeRested（仅本卡）。
///   - 两段分别注册一条 ContinuousEffect；来源离场时引擎一并清理。
///   - "对方效果"这一来源限定无法在持续谓词中区分，简化为不因任意效果被KO/被休息（覆盖主要场景）。
/// </summary>
public class OP11_046_VinsmokeYonji : IScriptedEffect
{
    public string CardNumber => "OP11-046";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        var sid = selfId.ToString();

        bool Cond(GameState s, CardInstance c) =>
            c.Id == selfId &&
            s.Players[owner].Characters.All(x => x.Info.HasKeyword("GERMA"));

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == sid);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sid,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, c) => Cond(s, c),
        });
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sid,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantRestriction = RestrictionKind.CannotBeRested,
            Predicate = (s, sideIdx, c) => Cond(s, c),
        });
        return Task.CompletedTask;
    }
}
