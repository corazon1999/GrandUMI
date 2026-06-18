using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-019 让你们瞧瞧我们的力量吧!!（事件）
/// 【主要】将我方手牌中最多2张力量为8000且拥有的特征中包含〈白胡子海盗团〉的角色卡牌登场。
/// 【触发】本回合中，我方领袖力量+1000。
///
/// 实现说明 / 简化点：
///   - 【主要】一次登场最多2张：用 ChooseCards(min0,max2) 选取后依次 PlayFromHandFree。
///   - 【触发】生命触发：我方领袖本回合力量+1000。
/// </summary>
public class OP16_019_ShowYouOurPower : IScriptedEffect
{
    public string CardNumber => "OP16-019";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】本回合中，我方领袖力量+1000
            AtomicOps.AddPowerThisTurn(me.Leader, 1000);
            return;
        }

        // 【主要】登场最多2张 力量8000 且含〈白胡子海盗团〉特征的角色
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power == 8000 &&
            c.Info.HasKeyword("白胡子海盗团")).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多2张力量8000的〈白胡子海盗团〉角色登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 2, extra);
        foreach (var id in chosen)
        {
            var picked = cands.First(c => c.Id.ToString() == id);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
