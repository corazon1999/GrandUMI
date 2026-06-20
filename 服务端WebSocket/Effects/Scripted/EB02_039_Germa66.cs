using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-039 GERMA 66（事件 / 暗 / 温思默克家·GERMA 66，cost4）
/// 【主要】可以丢弃我方手牌中1张力量不高于4000且拥有《GERMA 66》特征的角色卡牌：
///   我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   将我方废弃区中最多1张力量为5000到7000且与被丢弃的卡牌名称相同的角色卡牌登场。
///
/// 实现说明：
///   - 成本（可选）：丢弃手牌中1张力量≤4000且含《GERMA 66》特征的角色。无符合卡则无法发动。
///   - 收益条件：我方咚总数 ≤ 对方咚总数。
///   - 登场对象：废弃区中力量5000-7000、含《GERMA 66》、且名称与被丢弃卡相同的角色（动态同名约束用被丢弃卡的 Name 捕获）。
/// </summary>
public class EB02_039_Germa66 : IScriptedEffect
{
    public string CardNumber => "EB02-039";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本候选：手牌中力量≤4000且含《GERMA 66》的角色
        var costCands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 4000 &&
            c.Info.HasKeyword("GERMA 66")).ToList();
        if (costCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "GERMA 66【主要】：丢弃1张力量≤4000的《GERMA 66》角色，从废弃区登场1张同名的力量5000-7000角色?");
        if (!use) return;

        // 成本：选择并丢弃1张
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张力量≤4000的《GERMA 66》角色",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1, costExtra);
        if (chosen.Count == 0) return; // 未完成成本

        var discarded = costCands.First(c => c.Id.ToString() == chosen[0]);
        var discardedName = discarded.Info.Name;
        AtomicOps.DiscardHand(me, discarded);

        // 条件：我方咚数 ≤ 对方咚数
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;

        // 收益：废弃区中力量5000-7000且与被丢弃卡同名的角色，登场最多1张
        var playCands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power >= 5000 && c.Info.Power <= 7000 &&
            c.Info.Name == discardedName).ToList();
        if (playCands.Count == 0) return;

        var playExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "从废弃区登场最多1张同名的力量5000-7000角色",
            playCands.Select(c => c.Id.ToString()).ToList(), 0, 1, playExtra);
        if (pick.Count > 0)
        {
            var picked = playCands.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
