using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST15-005 波特夹斯·D·艾斯（角色 / 红 / 5费 6000，白胡子海盗团，SR）
/// 我方领袖的特征中包含《白胡子海盗团》的场合，此角色获得【速攻】。
/// 【每回合1次】此角色将要因对方的效果离开场上时，可以代替使此角色本回合力量-2000。
///
/// 实现：登场时注册条件【速攻】持续效果；效果KO与非KO效果离场均可改为本回合力量-2000并阻止离场。
/// （ST15 联网补全：本卡数据据英文卡表翻译，卡图待补、效果待官方校对。）
/// </summary>
public class ST15_005_Ace : IScriptedEffect
{
    public string CardNumber => "ST15-005";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnEnterField or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "速攻",
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.Players[owner].Leader.Info.HasKeyword("白胡子海盗团"),
            });
            return;
        }

        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        if (victimOwner != ctx.OwnerIndex || victimId != selfId.ToString()) return;

        var key = self.Info.Number + "-guard:" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾斯【每回合1次】：本回合此角色力量-2000，使其不离场？")) return;
        AtomicOps.AddPowerThisTurn(self, -2000);
        if (nonKoLeave) ctx.State.MarkPreventLeave(selfId);
        else ctx.State.MarkPreventKO(selfId);
        me.TurnOnceUsed.Add(key);
    }
}
