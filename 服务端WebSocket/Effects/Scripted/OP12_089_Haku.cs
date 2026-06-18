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
/// 本脚本实现 (2)：【KO时】若领袖含《革命军》，从对方场上选最多 1 张原本费用≤4 的角色 KO（可选 min=0）。
/// "原本的费用"按 Info.Cost 判定，不计费用修正。
///
/// 简化点：(1) 条件式获得【阻挡者】+ 费用 +4 未实现。
///   - 引擎 HasKeyword 仅做卡面文本 contains("【阻挡者】") + 临时关键字判定；由于本卡卡面文本
///     含"【阻挡者】"字样，现引擎会恒将此角色视为带【阻挡者】，无法按"领袖含革命军"动态评估。
///   - 费用 +4 为条件式持续修正，引擎无条件式连续关键字/费用钩子（ContinuousEffect 仅力量）。
///   此为既有引擎缺口，非本脚本可修正。
/// </summary>
public class OP12_089_Haku : IScriptedEffect
{
    public string CardNumber => "OP12-089";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

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
