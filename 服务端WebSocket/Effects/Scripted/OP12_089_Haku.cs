using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-089 哈库（4 费 5000，鱼人族/德莱斯罗兹/革命军）
///
/// 卡面效果：
///   1. 我方领袖拥有《革命军》特征的场合，此角色获得【阻挡者】效果，费用 +4。（条件式静态）
///   2. 【KO时】我方领袖拥有《革命军》特征的场合，将对方最多 1 张原本的费用不高于 4 的角色 KO。
///
/// 本脚本实现：
///   (1) 登场时注册 ContinuousEffect：我方领袖含《革命军》时，此角色获得【阻挡者】、费用+4（按领袖动态评估）。
///   (2)【KO时】若领袖含《革命军》，从对方场上选最多 1 张原本费用≤4 的角色 KO（可选 min=0）。
///       "原本的费用"按 Info.Cost 判定，不计费用修正。
/// </summary>
public class OP12_089_Haku : IScriptedEffect
{
    public string CardNumber => "OP12-089";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnKO || t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        int owner = ctx.OwnerIndex;

        // ── ① 持续光环：我方领袖含《革命军》→ 此角色获得【阻挡者】、费用+4（登场时注册，按领袖动态评估）──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId0 = ctx.Source.Id;
            s.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId0.ToString());
            s.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId0.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "阻挡者",
                CostDelta = 4,
                // HasContinuousKeyword / ContinuousCostBonus 仅按 Predicate 判定、不应用 Scope，须显式限定本卡自身
                Predicate = (st, side, card) => card.Id == selfId0 && st.Players[owner].Leader.Info.HasKeyword("革命军"),
            });
            return;
        }

        // ── ②【KO时】──
        var me = s.Players[owner];
        var opp = s.Players[1 - owner];

        // 条件：我方领袖拥有《革命军》特征
        if (!me.Leader.Info.HasKeyword("革命军")) return;

        // 候选：对方场上原本的费用不高于 4 的角色
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本的费用不高于 4 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        AtomicOps.KO(s, 1 - ctx.OwnerIndex, target);
    }
}
