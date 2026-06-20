using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-110 猫蝮蛇（角色）
/// 【咚!!×2】此角色也可以攻击对方处于活跃状态的角色。
///
/// 实现说明：
///   - 用 ContinuousEffect.GrantKeyword="可攻击活跃" 持续/条件赋予自身该关键词，
///     条件为本卡被赋予的咚!! ≥ 2（AttachedDonCount(self.Id) >= 2）。
///   - ActionValidator 攻击校验时通过 HasContinuousKeyword 读取该关键词，从而允许攻击活跃角色。
///   - 在 OnEnterField 时机注册，离场后由引擎自动清理；重复登场前先去重避免叠加。
/// </summary>
public class OP06_110_Nekomamushi : IScriptedEffect
{
    public string CardNumber => "OP06-110";

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
            GrantKeyword = "可攻击活跃",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 2,
        });

        return Task.CompletedTask;
    }
}
