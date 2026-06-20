using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-055 凤凰印（事件）
/// 【主要】可以公开我方手牌中 2 张拥有的特征中包含《白胡子海盗团》的卡牌：
///   将最多 1 张费用不高于 6 的角色放回其持有者的卡组最下方。
///
/// 实现说明 / 简化点：
///   - 成本"公开手牌 2 张白胡子卡牌"用 ChooseCards 选择实现：被公开的卡仍留在手牌（公开≠弃牌），
///     与文本一致。手牌中需 ≥2 张含《白胡子海盗团》特征的卡才能发动。
///   - "将最多 1 张费用不高于 6 的角色放回其持有者卡组最下方"对双方场上的角色均可选取；
///     使用 ReturnFieldToDeckBottom，按角色所属方放回各自卡组最下方。
/// </summary>
public class OP08_055_PhoenixSeal : IScriptedEffect
{
    public string CardNumber => "OP08-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本前置条件：手牌中至少 2 张含《白胡子海盗团》特征的卡牌
        var revealable = me.Hand.Where(c => c.Info.HasKeyword("白胡子海盗团")).ToList();
        if (revealable.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凤凰印【主要】：公开手牌中 2 张《白胡子海盗团》卡牌，将最多 1 张费用≤6 的角色放回其持有者卡组最下方？");
        if (!use) return;

        // 成本：公开（选择）2 张《白胡子海盗团》卡牌（仍留在手牌）
        var revealExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = revealable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var revealed = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealCards",
            "公开手牌中的 2 张《白胡子海盗团》卡牌",
            revealable.Select(c => c.Id.ToString()).ToList(), 2, 2, revealExtra);
        if (revealed.Count < 2) return; // 成本未支付

        // 向对手公开所选 2 张《白胡子海盗团》卡牌：广播牌面浮层（公开≠丢弃，仍留手牌）
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex,
            revealed.Select(id => revealable.First(c => c.Id.ToString() == id).Info.Number).ToList());

        // 效果：将最多 1 张费用≤6 的角色（双方）放回其持有者卡组最下方
        var myTargets = me.Characters.Where(c => c.Info.Cost <= 6).ToList();
        var oppTargets = opp.Characters.Where(c => c.Info.Cost <= 6).ToList();
        var all = new List<CardInstance>();
        all.AddRange(myTargets);
        all.AddRange(oppTargets);
        if (all.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Character",
            "选择最多 1 张费用≤6 的角色放回其持有者卡组最下方",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = all.First(c => c.Id.ToString() == chosen[0]);
            int ownerOfTgt = myTargets.Contains(picked) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, ownerOfTgt, picked);
        }
    }
}
