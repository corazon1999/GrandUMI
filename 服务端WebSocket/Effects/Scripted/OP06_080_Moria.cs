using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-080 月光·莫利亚（领航）
/// 【咚!!×1】【攻击时】②（可以将费用区中指定数量的咚!!转为休息状态），可以丢弃我方的 1 张手牌：
///   将我方卡组最上方的 2 张卡牌放置到废弃区，
///   将我方废弃区中最多 1 张费用不高于 4 且拥有《恐怖之船海盗团》特征的角色卡牌登场。
///
/// 说明：
///   - 效果整体作为"可以"（可选）发动，先以 ConfirmOptional 询问。
///   - "横置 2 张活跃咚 + 丢弃 1 张手牌"先收集并复验全部成本，再原子提交，避免只支付一半。
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
        if (me.ActiveDonCount < 2 || me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莫利亚【攻击时】：将2张咚!!转为休息并丢弃1张手牌，将卡组顶2张放入废弃区，再从废弃区登场1张费用≤4的《恐怖之船海盗团》角色？");
        if (!use) return;
        if (!await AtomicOps.PromptRestActiveDonAndDiscardOneHand(ctx, 2, _ => true)) return;

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
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
