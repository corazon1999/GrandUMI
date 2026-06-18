using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-025 兔科夫（角色）
/// 【攻击时】我方场上存在"角科夫"的场合，将我方手牌中最多1张费用不高于2的角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 条件"场上存在指定卡名角色"用 me.Characters.Any(c => c.MatchesName("角科夫")) 判定（含本卡之外）。
/// </summary>
public class OP16_025_Tsukofu : IScriptedEffect
{
    public string CardNumber => "OP16-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方场上存在"角科夫"
        bool hasKakofu = me.Characters.Any(c => c.MatchesName("角科夫"));
        if (!hasKakofu) return;

        // 收益：将手牌中最多1张费用≤2的角色登场
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost <= 2).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多1张费用≤2的角色登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
