using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-052 时悬（角色 / 水 2 费 3000 / 海军）
/// 【咚!!×1】我方手牌不多于 4 张的场合，此角色在战斗中不会被KO。
///
/// 实现：注册条件性 KoGuard="battle" 的持续效果，谓词限定本卡自身，
/// 条件为本卡被赋予的咚!!（AttachedDonCount）≥1 且我方手牌 ≤4 张。
/// </summary>
public class OP06_052_Tokitoki : IScriptedEffect
{
    public string CardNumber => "OP06-052";

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
            KoGuard = "battle",
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].Hand.Count <= 4,
        });

        return Task.CompletedTask;
    }
}
