using System.Linq;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-054 Mr.1(达兹·波涅斯)（角色，因佩尔地狱/原巴洛克工作室，力量2000）
/// 【咚!!×1】【我方的回合中】我方持有5张或更多手牌的场合，此角色的力量+3000。
/// 【登场时】抽取1张卡牌。
///
/// 实现：登场时注册固定 +3000 的 ContinuousEffect（门槛【咚×1】+【我方回合】+手牌≥5 写进 Predicate，仅作用自身）；
///       登场时抽1沿用 DSL（脚本结尾委托解释器）。
/// </summary>
public class OP16_054_DazBones : IScriptedEffect
{
    public string CardNumber => "OP16-054";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 3000,
            Predicate = (s, side, c) =>
                side == owner && c.Id == selfId
                && s.CurrentTurnPlayer == owner
                && s.Players[owner].Hand.Count >= 5
                && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        // 登场时抽1沿用 DSL
        await Dsl.DslInterpreter.TryResolve(ctx);
    }
}
