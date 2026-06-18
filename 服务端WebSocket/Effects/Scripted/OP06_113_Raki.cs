using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-113 拉奇（角色 / 光 1 费 1000 / 空岛·山迪亚战士）
/// 我方场上存在“拉奇”以外的拥有《山迪亚战士》特征的角色的场合，此角色获得【阻挡者】效果。
///
/// 实现：注册条件性 GrantKeyword="阻挡者" 的持续效果，谓词限定本卡自身，
/// 条件为我方场上存在本卡之外、含《山迪亚战士》特征且名称非“拉奇”的角色。
/// </summary>
public class OP06_113_Raki : IScriptedEffect
{
    public string CardNumber => "OP06-113";

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
                s.Players[owner].Characters.Any(ch =>
                    ch.Id != selfId &&
                    ch.Info.HasKeyword("山迪亚战士") &&
                    !ch.Info.NameContains("拉奇")),
        });

        return Task.CompletedTask;
    }
}
