using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-108 夏洛特·克力架（角色）
/// 【咚!!×1】我方生命卡牌的张数少于对方的场合，此角色获得【双重攻击】效果，力量+1000。
///
/// 说明 / 简化点：
///   - 这是持续/条件型修正：用两条 ContinuousEffect 注册（OnEnterField）。
///     · 一条 GrantKeyword="双重攻击"，仅本卡、且我方生命少于对方时生效；
///     · 一条 PowerDelta=1000，同条件。
///   - 【咚!!×1】为发动该静态加成的门槛（赋予 1 咚），由引擎在登场/赋予咚时评估；
///     此处条件以"我方生命少于对方"为准（贴咚需求由引擎咚条件机制处理，简化为生命比较条件）。
///   - 【触发】丢弃手牌登场为触发关键词，引擎单独处理，不在此实现。
/// </summary>
public class OP03_108_Cracker : IScriptedEffect
{
    public string CardNumber => "OP03-108";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 条件：本卡 + 我方生命少于对方
        Func<GameState, int, CardInstance, bool> cond = (s, sideIdx, card) =>
            card.Id == selfId &&
            s.Players[owner].LifeCount < s.Players[1 - owner].LifeCount;

        // 力量 +1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = cond,
        });

        // 获得【双重攻击】
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "双重攻击",
            Predicate = cond,
        });

        return Task.CompletedTask;
    }
}
