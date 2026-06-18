using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-079 莉姆（角色 / 风 / 时光旅诗）
/// 【阻挡者】（关键词，由引擎处理）
/// 【我方的回合结束时】我方场上存在 2 张或更多处于休息状态且拥有《时光旅诗》特征的角色的场合，
///   将此角色转为活跃状态。
/// </summary>
public class P_079_Lim : IScriptedEffect
{
    public string CardNumber => "P-079";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int rested = me.Characters.Count(c => c.IsTapped && c.Info.HasKeyword("时光旅诗"));
        if (rested >= 2)
            AtomicOps.ActivateCard(ctx.Source);
        return Task.CompletedTask;
    }
}
