using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-088 蔡义（角色 / 地 3 费 4000 / 德莱斯罗兹·八宝水军）
/// 我方领袖拥有《德莱斯罗兹》特征，且我方领袖处于活跃状态的场合，此角色的力量+2000。
///
/// 实现：注册条件性 PowerDelta=+2000 的持续效果，谓词限定本卡自身，
/// 条件为我方领袖含《德莱斯罗兹》特征且领袖处于活跃状态（!IsTapped）。
/// </summary>
public class OP06_088_Sai : IScriptedEffect
{
    public string CardNumber => "OP06-088";

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
            PowerDelta = 2000,
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].Leader.Info.HasKeyword("德莱斯罗兹") &&
                !s.Players[owner].Leader.IsTapped,
        });

        return Task.CompletedTask;
    }
}
