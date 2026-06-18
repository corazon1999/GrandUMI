using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-034 蒙奇·D·路飞（角色 / 基础力量 0）
/// 【咚!!×1】【我方的回合中】我方场上每有 1 张卡牌名称不同的角色，此角色的力量 +1000。
/// 【登场时】确认卡组顶 3 张，公开≤1 张《因佩尔地狱》加入手牌，其余放卡组底。
///
/// 实现说明（持续力量光环此前完全未注册，反馈 #69「赋予1咚后力量计算错误」）：
///   - 持续力量与 OP01-072 史姆丽同构：ContinuousEffect.PowerDelta 为固定值，无法直接表达
///     "每个异名角色 +1000"，故注册多条阈值持续效果（异名角色数≥1,≥2,…,≥30 各 +1000）累加等价。
///   - 仅【我方回合中】且此卡被赋予咚!!≥1 时生效；Predicate 必含 card.Id==selfId 只加成自身
///     （引擎 ContinuousEffect 只看 Predicate 不看 Scope，漏写会全场加成）。
///   - 异名角色数 = 我方所有角色按 Info.Name 去重计数（含自身，与 OP16-038 口径一致）。
///   - 【登场时】检索沿用 DSL 定义（Definitions/OP16.json）。因脚本一旦处理 OnEnterField 会覆盖 DSL，
///     故在注册持续力量后显式补跑 DSL 解释器执行检索段（参考 EffectRuntime 派发：脚本命中即 return）。
/// </summary>
public class OP16_034_Luffy : IScriptedEffect
{
    public string CardNumber => "OP16-034";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        // 注册持续力量（先清自身旧注册，防重复登场叠加）
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        for (int i = 1; i <= 30; i++)
        {
            int threshold = i;
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.CurrentTurnPlayer == owner &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                    s.Players[owner].Characters.Select(c => c.Info.Name).Distinct().Count() >= threshold,
            });
        }

        // 【登场时】检索沿用 DSL 段（脚本覆盖 DSL，需显式补跑解释器执行检索）
        await DslInterpreter.TryResolve(ctx);
    }
}
