using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-003 卡彭·班吉（角色 / 炎）
/// 此角色不会因对方原本的力量不高于5000的角色的效果而被KO。
/// 实现：PreKO（效果KO前对受害者派发）。仅当本次为效果KO、且KO来源卡为对方原力≤5000角色时，MarkPreventKO 取消本次KO。
/// </summary>
public class OP14_003_CaponeBege : IScriptedEffect
{
    public string CardNumber => "OP14-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        if (ctx.State.KOReason != "effect") return Task.CompletedTask;          // 仅效果KO
        var srcId = ctx.State.KOSourceCardId;
        if (srcId is null) return Task.CompletedTask;
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        // KO 来源须为对方"角色"且原本力量≤5000
        var src = opp.Characters.FirstOrDefault(c => c.Id == srcId.Value);
        if (src is null || src.Info.Power > 5000) return Task.CompletedTask;
        ctx.State.MarkPreventKO(self.Id);
        return Task.CompletedTask;
    }
}
