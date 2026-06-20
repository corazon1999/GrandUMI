using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-029 角科夫（角色，风）
/// 【攻击时】我方场上存在"兔科夫"的场合，将我方手牌中最多 1 张费用不高于 2 的角色卡牌登场。
///
/// 实现说明：
///   - 触发条件"场上存在卡名为兔科夫的角色"用 me.Characters.Any(MatchesName) 表达。
///   - 登场对象限定为手牌中费用≤2 的角色卡，使用 PlayFromHandFree。
///   - 关键词《因佩尔地狱》为特征，无需脚本处理。
/// </summary>
public class OP16_029_Karuko : IScriptedEffect
{
    public string CardNumber => "OP16-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方场上存在"兔科夫"
        if (!me.Characters.Any(c => c.MatchesName("兔科夫"))) return;

        // 候选：手牌中费用≤2 的角色
        var playable = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 2)
            .ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacterCostLe2",
            "将手牌中最多 1 张费用不高于 2 的角色登场",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = playable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
