using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-070 夫查（角色）
/// 我方的所有"修罗"和此角色获得【不可阻挡】。
/// 【对方的回合中】我方的所有"修罗"和此角色原本的力量变为 6000。
///
/// 实现说明 / 简化点：
///   - 【不可阻挡】用持续 GrantKeyword（13.1），谓词选中我方"修罗"及自身（恒定生效），可精确实现。
///   - "原本力量变为 6000"：ContinuousEffect 仅支持固定 int PowerDelta，无 per-card 力量函数，
///     无法把基础力量各异的多张"修罗"统一设为 6000；此处对自身（夫查原力量 4000）按
///     PowerDelta = +2000（= 6000 - 4000）在对方回合中近似实现，多张"修罗"的精确换算无通道。
///     （与同机制卡 OP15-071 保利的处理一致。）
///   均在 OnEnterField 注册，离场由引擎清理。
/// </summary>
public class OP15_070_Fuza : IScriptedEffect
{
    public string CardNumber => "OP15-070";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        bool IsTarget(int sideIdx, CardInstance c) =>
            sideIdx == owner && (c.Id == selfId || c.MatchesName("修罗"));

        // 我方"修罗"与自身获得【不可阻挡】（恒定）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "不可阻挡",
            Predicate = (s, sideIdx, c) => IsTarget(sideIdx, c),
        });

        // 【对方的回合中】原本力量变为 6000（仅自身精确：夫查基础 4000 → +2000）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 6000 - self.Info.Power,
            Predicate = (s, sideIdx, c) => c.Id == selfId && s.CurrentTurnPlayer != owner,
        });

        return Task.CompletedTask;
    }
}
