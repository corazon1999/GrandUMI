using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-059 ——如果没有你…！！我……就当不上海盗王了啊！！！（事件 / 光 / 草帽一伙，cost4 反击）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +1000。之后，我方生命卡牌不多于 1 张的场合，
///   将我方手牌中最多 1 张费用不高于 5 且拥有《草帽一伙》特征的黄色角色卡牌或"山智"登场。
///
/// 实现说明：
///   - 【反击】= EventCounter 时机。
///   - 本次战斗 +1000 用 AddPowerThisBattle。
///   - 黄色 = 颜色集合含 "黄"。候选 = 手牌中（费用≤5 且《草帽一伙》且黄色）或 名为"山智" 的角色卡。
/// </summary>
public class EB02_059_PirateKing : IScriptedEffect
{
    public string CardNumber => "EB02-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本次战斗中，我方最多 1 张领袖或角色 +1000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗力量 +1000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(tgt, 1000);
        }

        // 之后：生命≤1 → 从手牌登场 1 张符合条件的角色
        if (me.LifeArea.Count > 1) return;

        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            (
                (c.Info.Cost <= 5 && c.Info.HasKeyword("草帽一伙") && c.Info.ColorList.Contains("黄"))
                || c.Info.NameContains("山智")
            )
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张费用≤5 的黄色《草帽一伙》角色或\"山智\"登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
