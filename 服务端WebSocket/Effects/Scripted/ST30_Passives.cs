using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST30-001 路飞&艾斯（领航）
/// 我方场上存在原本力量≥7000角色时，此领袖力量-2000。【对方的回合中】我方所有“波特夹斯·D·艾斯”和“蒙奇·D·路飞”力量+3000。（持续，OnGameStart 注册）
/// </summary>
public class ST30_001_LuffyAce : IScriptedEffect
{
    public string CardNumber => "ST30-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

    public Task Resolve(EffectContext ctx)
    {
        var leaderId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == leaderId.ToString());
        // 有力量≥7000角色时此领袖-2000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = -2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == leaderId && s.Players[owner].Characters.Any(c => c.Info.Power >= 7000),
        });
        // 对方回合 我方所有 艾斯/路飞 +3000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && s.CurrentTurnPlayer != owner &&
                (card.Info.NameIs("波特夹斯·D·艾斯") || card.Info.NameIs("蒙奇·D·路飞")),
        });
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST30-003 爱德华·纽哥特（角色）
/// 【我方的回合中】我方所有原本力量为6000的角色力量+1000。（持续）
/// </summary>
public class ST30_003_Newgate : IScriptedEffect
{
    public string CardNumber => "ST30-003";
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
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Id != s.Players[owner].Leader.Id &&
                s.CurrentTurnPlayer == owner &&
                card.Info.Power == 6000,
        });
        return Task.CompletedTask;
    }
}
