using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-015 甚平（角色 / 风 / 鱼人族·鱼人岛·太阳海盗团）
/// 【阻挡者】（关键词，由引擎处理）
/// 【KO时】可以将我方的 1 张卡牌转为休息状态：我方领袖拥有《鱼人族》或《人鱼族》特征的场合，
///   将我方手牌中最多 1 张费用不高于 6 的绿色角色卡牌登场。
///
/// 实现说明：
///   - 可选成本：将我方 1 张活跃的卡牌（领袖或角色）转为休息状态；无可横置目标则无法支付。
///   - 收益条件：领袖含《鱼人族》或《人鱼族》；满足则从手牌登场最多 1 张费用≤6 的绿色（风）角色。
///   - 绿色 = ColorList.Contains("绿")；登场用 PlayFromHandFree。
/// </summary>
public class EB04_015_Jinbe : IScriptedEffect
{
    public string CardNumber => "EB04-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (AtomicOps.RestableCount(me) < 1) return; // 无活跃可休置项(领袖/角色/舞台/咚)，无法支付

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "甚平【KO时】：将我方 1 张卡牌转为休息状态，若领袖含《鱼人族》或《人鱼族》则登场 1 张费用≤6 的绿色角色？");
        if (!use) return;

        // 支付成本：选择 1 张活跃 领袖/角色/舞台/咚 转为休息状态
        if (!await AtomicOps.PromptRestOwnCards(ctx, 1,
            "将我方 1 张卡牌转为休息状态（成本，可选活跃 领袖/角色/舞台/咚!!）")) return;

        // 收益条件：领袖含《鱼人族》或《人鱼族》
        if (!me.Leader.Info.HasKeyword("鱼人族") && !me.Leader.Info.HasKeyword("人鱼族")) return;

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 6 &&
            c.Info.ColorList.Contains("绿")).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场 1 张费用≤6 的绿色角色（最多 1 张）",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
