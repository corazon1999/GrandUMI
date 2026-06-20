using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-071 保利（角色）
/// 持续①：我方所有"欧姆"和此角色获得【双重攻击】。
/// 持续②：【对方的回合中】我方所有"欧姆"和此角色原本的力量变为 6000。
///
/// 实现说明 / 简化点：
///   - 两条均为持续/静态效果，登场时注册 ContinuousEffect，离场时引擎自动清理。
///   - 【双重攻击】用 ContinuousEffect.GrantKeyword（Wave1 13.1），谓词选中自身或"欧姆"。
///   - "原本力量变为 6000"：ContinuousEffect 仅支持固定的 int PowerDelta，无 per-card 力量函数，
///     无法把基础力量各异的多张"欧姆"统一设为 6000；此处对自身（保利原力量 4000）按
///     PowerDelta = +2000 在对方回合中近似实现"变为 6000"，多张"欧姆"的精确换算无通道。
/// </summary>
public class OP15_071_Pauly : IScriptedEffect
{
    public string CardNumber => "OP15-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 防重复注册
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 选中目标：我方自身 或 我方"欧姆"
        bool IsTarget(int sideIdx, CardInstance c) =>
            sideIdx == owner && (c.Id == selfId || c.MatchesName("欧姆"));

        // 持续①：获得【双重攻击】
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "双重攻击",
            Predicate = (s, sideIdx, c) => IsTarget(sideIdx, c),
        });

        // 持续②：【对方的回合中】自身原本力量变为 6000（保利基础 4000，近似为 +2000）
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
