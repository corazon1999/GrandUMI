using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-089 缪斯加鲁德圣（角色）
/// 【启动主要】①（可以将费用区中指定数量的咚!!转为休息状态），可以将此角色和我方的 1 张角色转为休息状态：
///   将我方废弃区中最多 1 张费用为 1 的黑色角色卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - 标头 ①（横置 1 张咚的成本）按惯例不在脚本内强制扣除（启动成本由引擎层处理）。
///   - "可以"=可选发动；成本为"横置此角色 + 横置我方另 1 张角色"，需存在另 1 张角色。
///   - 收益：从废弃区取最多 1 张费用 1 的黑色角色加入手牌。
/// </summary>
public class OP05_089_Mjosgard : IScriptedEffect
{
    public string CardNumber => "OP05-089";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本目标：我方此角色以外的 1 张角色
        var others = me.Characters.Where(c => c.Id != self.Id).ToList();
        if (others.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "缪斯加鲁德圣【启动主要】：横置此角色与我方另 1 张角色，将废弃区最多 1 张费用1 的黑色角色加入手牌？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 成本：横置我方另 1 张角色
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方另 1 张角色转为休息状态（成本）",
            others.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return; // 未支付成本
        var resting = others.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.RestCard(resting);

        // 收益：废弃区中最多 1 张费用 1 的黑色角色加入手牌
        var cand = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost == 1 &&
            c.Info.ColorList.Contains("黑")
        ).ToList();
        if (cand.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "将废弃区最多 1 张费用 1 的黑色角色加入手牌",
            cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cand.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.TrashToHand(me, picked);
        }
    }
}
