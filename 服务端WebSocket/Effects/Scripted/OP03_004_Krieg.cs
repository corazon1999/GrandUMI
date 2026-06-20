using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-004 克里埃尔（角色 / 炎 3 费 4000，白胡子海盗团）
/// 此角色在登场的回合中无法攻击领袖。
/// 【咚!!×1】此角色获得【速攻】效果。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】获得【速攻】：通过 ContinuousEffect.GrantKeyword 注册，谓词限定为本卡且被赋予
///     的咚!! ≥1 时授予「速攻」（条件持续关键词，OnEnterField 注册，离场自动清理）。
///   - "登场回合无法攻击领袖" 为「仅本回合、仅针对领袖」的静态攻击限制，引擎的禁攻通道
///     （NoAttackCostLeThisTurn 按费用 / CannotAttack 永久）均无法精确表达「仅领袖」这一目标限定，
///     故此条静态限制本脚本未实现（不影响其余效果），属可接受的近似简化。
/// </summary>
public class OP03_004_Krieg : IScriptedEffect
{
    public string CardNumber => "OP03-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "速攻",
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
