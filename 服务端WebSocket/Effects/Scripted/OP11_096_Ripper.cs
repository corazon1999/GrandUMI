using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-096 利帕（角色 / 地 / 东海・海军）
/// 我方场上存在“利帕”以外的拥有《海军》特征的黑色（地）角色的场合，此角色获得【阻挡者】效果。
///
/// 实现：OnEnterField 注册条件性 GrantKeyword="阻挡者"，谓词限定本卡且满足场况条件时生效。
/// 黑色按元素色"黑"判定。
/// </summary>
public class OP11_096_Ripper : IScriptedEffect
{
    public string CardNumber => "OP11-096";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId &&
                s.Players[owner].Characters.Any(x =>
                    x.Id != selfId &&
                    !x.Info.NameContains("利帕") &&
                    x.Info.HasKeyword("海军") &&
                    x.Info.ColorList.Contains("黑")),
        });
        return Task.CompletedTask;
    }
}
