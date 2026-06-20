using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-080 月光·莫利亚（领航）
/// 【咚!!×1】【攻击时】②（可以将费用区中指定数量的咚!!转为休息状态），可以丢弃我方的 1 张手牌：
///   将我方卡组最上方的 2 张卡牌放置到废弃区，
///   将我方废弃区中最多 1 张费用不高于 4 且拥有《恐怖之船海盗团》特征的角色卡牌登场。
///
/// 说明 / 简化点：
///   - 攻击时触发不支持复合可选成本表达；引擎亦无"将费用区咚转为休息状态"的原子操作。
///     故按惯例只实现效果收益（弃顶 2 张 + 废弃区条件登场），不实现"横置 2 咚 / 丢 1 手牌"成本。
///     （此点见规范第十节，触发节无法表达的可选成本只实现收益。）
///   - 效果整体作为"可以"（可选）发动，先以 ConfirmOptional 询问。
///   - 登场目标：废弃区中费用不高于 4 且拥有《恐怖之船海盗团》特征的角色卡牌，最多 1 张。
/// </summary>
public class OP06_080_Moria : IScriptedEffect
{
    public string CardNumber => "OP06-080";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莫利亚【攻击时】：将卡组顶 2 张放入废弃区，并从废弃区登场 1 张费用≤4 且拥有《恐怖之船海盗团》特征的角色？");
        if (!use) return;

        // 将卡组最上方 2 张放入废弃区
        AtomicOps.MillTop(me, 2);

        // 将废弃区中最多 1 张费用≤4 且《恐怖之船海盗团》的角色登场
        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 4 &&
            c.Info.HasKeyword("恐怖之船海盗团")
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "从废弃区登场最多 1 张费用≤4 的《恐怖之船海盗团》角色",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
