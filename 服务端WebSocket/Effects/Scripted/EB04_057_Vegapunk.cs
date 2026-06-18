using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-057 贝加班克（角色 / 光 / 奥哈拉・科学家，cost2 power0）
/// 我方生命卡牌不多于 2 张的场合，我方所有拥有《科学家》特征的黄色角色不会因对方的效果而离场。
/// 【咚!!×1】此角色获得【阻挡者】效果。
///
/// 实现说明（M6 持续防离场光环）：
///   - 防离场用 ContinuousEffect.LeaveGuard="effect"（生命≤2 时生效），引擎在各离场口
///     （KO/退手/放底/置生命）同步查询 IsLeaveGuarded。"对方的效果"近似为"因效果离场"（自伤罕见）。
///   - 【咚×1】阻挡者用 ContinuousEffect.GrantKeyword="阻挡者"（自身被贴咚≥1）。
///   - 二者均在 OnEnterField 注册。
/// </summary>
public class EB04_057_Vegapunk : IScriptedEffect
{
    public string CardNumber => "EB04-057";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 持续防离场：生命≤2 时，我方《科学家》黄色(光)角色不会因效果离场
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            LeaveGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Info.HasKeyword("科学家") && card.Info.ColorList.Contains("黄") &&
                s.Players[owner].LifeCount <= 2,
        });

        // 【咚!!×1】此角色获得【阻挡者】
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
