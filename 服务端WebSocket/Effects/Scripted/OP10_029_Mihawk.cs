using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-029 杰拉基尔·米霍克（角色）
/// 【登场时】我方场上存在 2 张或更多处于休息状态的角色的场合，
///   将我方最多 1 张处于休息状态、费用不高于 5 且拥有《时光旅诗》特征的角色转为活跃状态。
///
/// 实现说明：
///   - "费用不高于 5"取卡面原始费用 c.Info.Cost。
///   - "处于休息状态"= IsTapped。
/// </summary>
public class OP10_029_Mihawk : IScriptedEffect
{
    public string CardNumber => "OP10-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：场上存在 2 张或更多处于休息状态的角色
        int restingCount = me.Characters.Count(c => c.IsTapped);
        if (restingCount < 2) return;

        // 候选：处于休息状态、费用≤5、拥有《时光旅诗》特征的角色
        var cands = me.Characters.Where(c =>
            c.IsTapped &&
            c.Info.Cost <= 5 &&
            c.Info.HasKeyword("时光旅诗")
        ).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张休息状态、费用≤5 且拥有《时光旅诗》特征的角色转为活跃状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ActivateCard(target);
        }
    }
}
