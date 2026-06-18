using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-081 格尔尼卡（角色 / 地 / 1 费 / 2000 / CP0）
/// 【攻击时】可以将我方废弃区中 3 张拥有的特征中包含《CP》的卡牌自选顺序放回卡组最下方：
///   将对方最多 1 张费用为 0 的角色 KO。
///
/// 说明 / 简化点：
///   - 成本为可选：需要废弃区中含《CP》特征的卡牌 ≥3 张才可发动；选 3 张放回卡组最下方。
///   - "自选顺序放回卡组最下方"用 AtomicOps.ReturnTrashToDeckBottom 依玩家选择顺序逐张放底实现。
///   - "拥有的特征中包含《CP》"：HasKeyword 任一以 "CP" 开头的特征（CP0/CP9/CP-0 等），按 Keywords 判定。
/// </summary>
public class OP08_081_Guernica : IScriptedEffect
{
    public string CardNumber => "OP08-081";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本候选：废弃区中拥有的特征含《CP》的卡牌
        var cpTrash = me.Trash.Where(c => c.Info.Keywords.Any(k => k.Contains("CP"))).ToList();
        if (cpTrash.Count < 3) return;

        // 可选成本
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "格尔尼卡【攻击时】：将废弃区 3 张含《CP》特征的卡牌放回卡组最下方，将对方最多 1 张费用为 0 的角色 KO？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cpTrash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var picks = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Trash",
            "选择废弃区 3 张含《CP》特征的卡牌放回卡组最下方（自选顺序）",
            cpTrash.Select(c => c.Id.ToString()).ToList(), 3, 3, extra);
        if (picks.Count < 3) return;

        // 按玩家选择顺序逐张放回卡组最下方
        foreach (var id in picks)
        {
            var card = me.Trash.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 效果：将对方最多 1 张费用为 0 的角色 KO
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) == 0).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用为 0 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
