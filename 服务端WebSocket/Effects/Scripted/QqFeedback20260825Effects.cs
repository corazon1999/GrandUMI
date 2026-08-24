using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP16-048 巴奇：只有实际选中囚犯后才消耗【每回合1次】。</summary>
public sealed class OP16_048_Buggy : IScriptedEffect
{
    public string CardNumber => "OP16-048";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"{ctx.Source.Id}-Trigger-{EffectTrigger.OnOppAttackDeclare}";
        if (me.TurnOnceUsed.Contains(key)) return;

        var candidates = me.Characters
            .Where(card => card.MatchesName("因佩尔地狱的囚犯"))
            .ToList();
        if (candidates.Count == 0) return;
        var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OwnCharacter", "选择我方最多1张「因佩尔地狱的囚犯」本回合获得【阻挡者】", candidates);
        if (target is null) return;

        AtomicOps.GiveKeyword(target, "阻挡者", KeywordDuration.ThisTurn, ctx.OwnerIndex);
        me.TurnOnceUsed.Add(key);
    }
}

/// <summary>OP14-117 砖蝙蝠：反击力量可赋予恐怖之船海盗团领袖或角色。</summary>
public sealed class OP14_117_BrickBat : IScriptedEffect
{
    public string CardNumber => "OP14-117";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = new[] { me.Leader }.Concat(me.Characters)
            .Where(card => card.Info.HasKeywordContaining("恐怖之船海盗团"))
            .ToList();
        var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OwnLeaderOrCharacter", "选择我方最多1张《恐怖之船海盗团》领袖或角色，本次战斗力量+3000", candidates);
        if (target is not null) AtomicOps.AddPowerThisBattle(target, 3000);
    }
}

/// <summary>OP14-058 海流过肩摔：补齐登场后的6000原本力量角色退手。</summary>
public sealed class OP14_058_SharkBrickFist : IScriptedEffect
{
    public string CardNumber => "OP14-058";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var state = ctx.State;
        var me = state.Players[ctx.OwnerIndex];
        var opp = state.Players[1 - ctx.OwnerIndex];
        if (!await OfficialCoverageHelpers.PayRestDon(ctx, 3)) return;

        var playable = me.Hand.Where(card => card.Info.Kind == CardKind.Character
            && card.Info.Cost <= 3
            && card.Info.HasKeywordContaining("鱼人族")).ToList();
        var played = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OwnHandCharacter", "将手牌中最多1张费用不高于3的《鱼人族》角色登场", playable);
        if (played is not null) await AtomicOps.PlayFromHandFree(state, ctx.OwnerIndex, played);

        var bounceCandidates = me.Characters.Concat(opp.Characters)
            .Where(card => card.Info.Power == 6000)
            .ToList();
        var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "AnyCharacter", "将最多1张原本力量6000的角色放回持有者手牌", bounceCandidates);
        if (target is null) return;
        int owner = me.Characters.Contains(target) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
        if (!await AtomicOps.TryEffectLeaveGuard(state, owner, target, ctx.Prompts, "bounce"))
            AtomicOps.BounceToHand(state, owner, target);
    }
}
