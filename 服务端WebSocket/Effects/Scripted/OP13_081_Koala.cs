using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-081 克尔拉（角色）
/// 【启动主要】【每回合1次】可以将我方废弃区中的1张卡牌放回卡组最下方：
///   赋予我方1张领袖或角色最多1张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 卡面另含静态费用修正"我方领袖拥有《革命军》特征时此角色费用+3"，属非力量的持续修正，
///     引擎无持续通道，本脚本不实现该部分。
///   - 【启动主要】成本为"将废弃区 1 张卡放回卡组最下方"，用 AtomicOps.ReturnTrashToDeckBottom 支付。
///   - "可以"=可选，先 ConfirmOptional。
/// </summary>
public class OP13_081_Koala : IScriptedEffect
{
    public string CardNumber => "OP13-081";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        if (me.Trash.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克尔拉【启动主要】：将废弃区 1 张卡放回卡组最下方，赋予我方 1 张领袖/角色 1 张休息状态的咚!!？");
        if (!use) return;

        // 成本：将废弃区 1 张卡放回卡组最下方
        var trashExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var trashPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "选择废弃区 1 张卡放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 1, 1, trashExtra);
        if (trashPick.Count == 0) return;
        var trashCard = me.Trash.First(c => c.Id.ToString() == trashPick[0]);
        AtomicOps.ReturnTrashToDeckBottom(me, trashCard);

        // 效果：赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!
        var donTargets = new List<CardInstance> { me.Leader };
        donTargets.AddRange(me.Characters);
        var pickTgt = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张领袖或角色，赋予最多 1 张休息状态的咚!!",
            donTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pickTgt.Count > 0)
        {
            var target = donTargets.First(c => c.Id.ToString() == pickTgt[0]);
            AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
        }

        me.TurnOnceUsed.Add(key);
    }
}
