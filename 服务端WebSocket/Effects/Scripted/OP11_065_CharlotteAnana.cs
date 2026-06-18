using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-065 夏洛特·亚娜娜（角色 / 暗 / 大妈海盗团）
/// 我方场上存在“夏洛特·亚娜娜”以外的拥有《大妈海盗团》特征的紫色（暗）角色的场合，
/// 此角色获得【阻挡者】效果。
///
/// 实现：OnEnterField 注册条件性 GrantKeyword="阻挡者"，谓词限定本卡且满足场况条件时生效。
/// 紫色按元素色"紫"判定。
/// </summary>
public class OP11_065_CharlotteAnana : IScriptedEffect
{
    public string CardNumber => "OP11-065";
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
                    !x.Info.NameContains("夏洛特·亚娜娜") &&
                    x.Info.HasKeyword("大妈海盗团") &&
                    x.Info.ColorList.Contains("紫")),
        });
        return Task.CompletedTask;
    }
}
