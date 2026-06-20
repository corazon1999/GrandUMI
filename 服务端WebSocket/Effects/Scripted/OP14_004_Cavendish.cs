using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-004 凯文迪修（角色 / 炎）
/// 此角色的力量为 5000 或更高的场合，此角色获得【速攻】效果。
///
/// 实现说明：
///   - 条件性持续赋予关键词，用 ContinuousEffect.GrantKeyword="速攻" 注册（登场时）。
///   - Predicate 限定仅本卡自身、且当前力量（含持续效果）≥5000 时授予。
///   - 当前力量用 ctx.State.CurrentPowerOf 评估（含贴咚与持续修正）。
/// </summary>
public class OP14_004_Cavendish : IScriptedEffect
{
    public string CardNumber => "OP14-004";

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
                c.Id == selfId && sideIdx == owner &&
                s.CurrentPowerOf(sideIdx, c) >= 5000,
        });

        return Task.CompletedTask;
    }
}
