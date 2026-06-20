using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-102 甘·福尔（角色 / 光 4 费 4000，空岛）
/// 持续：我方场上存在力量 7000+ 且拥有《空岛》特征的角色时，手牌中的此卡牌费用-3。
/// 【登场时】将对方最多 1 张费用不高于"对方生命卡牌张数"的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - "手牌中此卡费用-3"（我方场上有力量7000+且含〈空岛〉的角色）已由 Effects/HandStaticCost.cs 统一实现。
///   - 仅实现可脚本的【登场时】：阈值 = 对方生命卡牌张数(opp.LifeArea.Count)，
///     选对方 1 张费用≤该阈值的角色转为休息(RestCard)。"最多 1 张" → min=0 可不选。
/// </summary>
public class OP15_102_GanFall : IScriptedEffect
{
    public string CardNumber => "OP15-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int threshold = opp.LifeArea.Count;
        // 用含持续光环的当前费用评估对方角色费用
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= threshold)
            .ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 1 张费用≤{threshold} 的角色转为休息状态（可不选）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(tgt);
    }
}
