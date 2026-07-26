using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-001 可比（领航 / 炎・地 / 海军・利刃）
/// 我方拥有《利刃》特征的角色可以在登场的回合中攻击角色。
/// 【每回合1次】我方原本的力量不高于7000且拥有《海军》特征的角色因对方效果将要离开场上的场合，
///   可以改为将我方废弃区中的3张卡牌自选顺序放回卡组最下方，以代替离场。
///
/// 实现说明：
///   - 第一段（《利刃》角色登场回合可攻击角色）= 持续静态许可，OnGameStart 注册
///     GrantKeyword="登场回合可攻击角色"，谓词限定我方且角色含《利刃》。ActionValidator 据此放行。
///   - 第二段同时覆盖效果KO和非KO效果离场；将废弃区3张卡按选择顺序放回卡组最下方后阻止离场。
/// </summary>
public class OP11_001_Koby : IScriptedEffect
{
    public string CardNumber => "OP11-001";
    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnGameStart or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var sid = self.Id.ToString();

        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == sid);
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = sid,
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "登场回合可攻击角色",
                Predicate = (s, sideIdx, c) => sideIdx == owner && c.Info.HasKeyword("利刃"),
            });
            return;
        }

        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - owner)) return;

        var me = ctx.State.Players[owner];
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victimOwner != owner || victim is null ||
            victim.Info.Power > 7000 || !victim.Info.HasKeyword("海军")) return;

        var key = $"OP11-001-guard:{self.Id}";
        if (me.TurnOnceUsed.Contains(key) || me.Trash.Count < 3) return;
        if (!await ctx.Prompts.ConfirmOptional(owner,
            "可比【每回合1次】：将废弃区3张卡放回卡组最下方，使该《海军》角色不离场？")) return;

        var candidates = me.Trash.ToList();
        var chosen = await ctx.Prompts.ChooseCards(owner, "OwnTrashToDeckBottom",
            "按放回顺序选择废弃区3张卡（选择顺序即卡组底部顺序）",
            candidates.Select(c => c.Id.ToString()).ToList(), 3, 3);
        if (chosen.Count < 3) return;
        foreach (var id in chosen)
        {
            var card = candidates.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        if (nonKoLeave) ctx.State.MarkPreventLeave(victim.Id);
        else ctx.State.MarkPreventKO(victim.Id);
        me.TurnOnceUsed.Add(key);
    }
}
