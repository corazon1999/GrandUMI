using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-095 蒙奇·D·戈普（角色，地）
/// 【登场时】可以将我方废弃区中 3 张拥有《海军》特征的卡牌自选顺序放回卡组最下方：
///   赋予我方 1 张领袖最多 1 张休息状态的咚!!。
///   之后，场上存在费用为 9 或更高的角色的场合，将对方最多 1 张费用不高于 7 的角色 KO。
///
/// 实现说明 / 简化点：
///   - 可选成本 = 我方废弃区 3 张《海军》卡放回卡组最下方（不足 3 张则无法发动）。
///   - “自选顺序”实现为按选择先后依次放底。
///   - 赋予领袖最多 1 张休息状态咚!!（仅领袖为目标）。
///   - 条件“场上存在费用≥9 的角色”检测双方场上所有角色（含本卡）。
/// </summary>
public class OP11_095_Garp : IScriptedEffect
{
    public string CardNumber => "OP11-095";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        var navyTrash = me.Trash.Where(c => c.Info.HasKeyword("海军")).ToList();
        if (navyTrash.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "戈普【登场时】：将废弃区 3 张《海军》卡放回卡组最下方，赋予领袖 1 张休息咚!!，并按条件 KO 对方角色？");
        if (!use) return;

        // 成本：废弃区 3 张《海军》自选顺序放回卡组最下方
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = navyTrash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "将废弃区中 3 张《海军》卡自选顺序放回卡组最下方",
            navyTrash.Select(c => c.Id.ToString()).ToList(), 3, 3, extra);
        if (costPick.Count < 3) return; // 成本未支付

        foreach (var id in costPick)
        {
            var card = navyTrash.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 效果 1：赋予我方领袖最多 1 张休息状态的咚!!
        var leaderPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeader",
            "赋予我方领袖最多 1 张休息状态的咚!!",
            new List<string> { me.Leader.Id.ToString() }, 0, 1);
        if (leaderPick.Count > 0)
            AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

        // 效果 2：场上存在费用≥9 的角色时，KO 对方最多 1 张费用≤7 的角色
        bool hasBigChar =
            me.Characters.Any(c => c.Info.Cost >= 9) ||
            opp.Characters.Any(c => c.Info.Cost >= 9);
        if (!hasBigChar) return;

        var koCands = opp.Characters.Where(c => c.Info.Cost <= 7).ToList();
        if (koCands.Count == 0) return;

        var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用不高于 7 的角色 KO",
            koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (koPick.Count > 0)
        {
            var tgt = koCands.FirstOrDefault(c => c.Id.ToString() == koPick[0]);
            if (tgt is not null) AtomicOps.KO(s, oppIdx, tgt);
        }
    }
}
