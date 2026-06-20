using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-098 卡尔加拉（领航）
/// 【咚!!×1】【攻击时】将我方手牌中最多 1 张费用不高于我方场上咚!!的张数且拥有
///   《山迪亚战士》特征的角色卡牌登场。将其登场的场合，将我方生命区最上方的 1 张卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】为引擎处理的发动条件（赋予咚数量门槛），脚本只实现效果本体。
///   - 费用上限"我方场上咚!!的张数"为动态值，直接用 me.TotalDonInCostArea；
///     候选费用取卡面 c.Info.Cost（手牌牌无场上费用修正）。
///   - 生命牌私有：手动 me.LifeArea[0] 移入手牌。
/// </summary>
public class OP08_098_Kalgara : IScriptedEffect
{
    public string CardNumber => "OP08-098";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        int donCount = me.TotalDonInCostArea;
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= donCount &&
            c.Info.HasKeyword("山迪亚战士")
        ).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            $"登场我方手牌中最多 1 张费用≤{donCount} 且拥有《山迪亚战士》特征的角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = playable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);

        // 登场成功 → 将生命区最上方 1 张加入手牌
        if (me.LifeArea.Count > 0)
        {
            var lifeTop = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Hand.Add(lifeTop);
        }
    }
}
