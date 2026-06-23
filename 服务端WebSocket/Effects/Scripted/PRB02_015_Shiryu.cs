using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-015 希流（角色 / 地 / 黑胡子海盗团 / cost4 power5000 counter1000）【阻挡者】
/// ① 我方领袖拥有《黑胡子海盗团》特征的场合，此角色获得【阻挡者】效果，费用+4。
/// ②【KO时】我方领袖拥有《黑胡子海盗团》特征的场合，将对方最多1张原本的费用不高于4的角色KO。
///
/// 实现说明 / 取舍：
///   - ① 阻挡者 + 费用+4 为同一条件持续光环：登场时注册 ContinuousEffect（仅作用自身，Predicate=我方领袖含《黑胡子海盗团》，
///     GrantKeyword="阻挡者" + CostDelta=+4）。卡库 abilities 已移除「阻挡者」，改由此条件评估。
///   - ②【KO时】领袖含《黑胡子海盗团》→ KO 对方最多1张原本费用(印刷)≤4 的角色（迁移自原 DSL）。
///   - 本卡同时有持续光环+触发效果，故整卡用 C# 脚本实现（脚本优先于 DSL，原 PRB02.json 条目已移除）。
/// </summary>
public class PRB02_015_Shiryu : IScriptedEffect
{
    public string CardNumber => "PRB02-015";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];
        var opp = ctx.State.Players[1 - owner];

        // ── ① 领袖含《黑胡子海盗团》→ 此角色费用+4（持续光环，登场时注册）──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true, Filter = c => c.Id == selfId },
                CostDelta = 4,
                GrantKeyword = "阻挡者",   // 同一条件（领袖含《黑胡子海盗团》）下亦获得【阻挡者】
                // 注：引擎 ContinuousCostBonus / HasContinuousKeyword 均仅按 Predicate 判定、不应用 Scope，故须在此显式限定仅作用本卡自身
                Predicate = (s, side, card) => card.Id == selfId && s.Players[owner].Leader.Info.HasKeyword("黑胡子海盗团"),
            });
            return;
        }

        // ── ②【KO时】领袖含《黑胡子海盗团》→ KO 对方最多1张原本费用≤4 的角色 ──
        if (!me.Leader.Info.HasKeyword("黑胡子海盗团")) return;
        var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(owner, "OpponentCharacter",
            "将对方最多1张原本费用≤4的角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == pick[0]);
            await AtomicOps.KOByEffectAsync(ctx.State, 1 - owner, tgt, ctx.Prompts, owner);
        }
    }
}
