using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-010 道格拉斯·巴雷特（角色 / 炎 6 费 7000 / FILM·航海世博会）
/// 我方领袖拥有《FILM》特征的场合，此角色获得【阻挡者】效果。
///
/// 实现：注册条件性 GrantKeyword="阻挡者" 的持续效果，谓词限定本卡自身，
/// 条件为我方领袖含《FILM》特征。
/// </summary>
public class OP06_010_DouglasBullet : IScriptedEffect
{
    public string CardNumber => "OP06-010";

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
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].Leader.Info.HasKeyword("FILM"),
        });

        return Task.CompletedTask;
    }
}
