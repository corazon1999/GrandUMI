using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-027 凸眼（角色 / 风 / 海王类）
/// 我方领袖为“白星”的场合，此角色可以在登场的回合中攻击角色。
///
/// 实现：OnEnterField 注册条件性 GrantKeyword="登场回合可攻击角色"，谓词限定本卡且我方领袖名为"白星"。
/// </summary>
public class OP11_027_Bulldog : IScriptedEffect
{
    public string CardNumber => "OP11-027";
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
            GrantKeyword = "登场回合可攻击角色",
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && s.Players[owner].Leader.Info.NameContains("白星"),
        });
        return Task.CompletedTask;
    }
}
