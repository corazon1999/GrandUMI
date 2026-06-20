using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-043 斯巴达因（角色 / 地 / CP9）
/// 【登场时】可以将我方废弃区中 3 张拥有的特征中包含＜CP＞的卡牌自选顺序放回卡组最下方：
///   将我方废弃区中最多 1 张“斯巴达因”以外的费用不高于 4 且拥有的特征中包含＜CP＞的角色卡牌以休息状态登场。
///
/// 实现说明 / 简化点：
///   - 成本：废弃区中选择 3 张含＜CP＞特征的卡放回卡组最下方（"自选顺序"简化为所选顺序放底）。
///     废弃区含＜CP＞特征的卡不足 3 张则无法支付成本，不发动。
///   - 收益：废弃区中选 1 张满足条件的角色以休息状态登场（PlayFromTrashFree restState:true）。
///   - ＜CP＞特征：用 HasKeyword 前缀匹配（"CP9"/"CP0" 等均含 "CP"）。
/// </summary>
public class EB01_043_Spandine : IScriptedEffect
{
    public string CardNumber => "EB01-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        bool HasCP(CardInstance c) => c.Info.Keywords.Any(k => k.Contains("CP"));

        // 成本候选：废弃区中含＜CP＞特征的卡
        var costCands = me.Trash.Where(HasCP).ToList();
        if (costCands.Count < 3) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "斯巴达因【登场时】：将废弃区 3 张含＜CP＞特征的卡放回卡组最下方，登场 1 张费用≤4 含＜CP＞的角色（休息状态）？");
        if (!use) return;

        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashToDeckBottom",
            "选择 3 张含＜CP＞特征的废弃区卡放回卡组最下方",
            costCands.Select(c => c.Id.ToString()).ToList(), 3, 3, costExtra);
        if (costPick.Count < 3) return; // 未完成成本

        foreach (var id in costPick)
        {
            var c = costCands.First(x => x.Id.ToString() == id);
            AtomicOps.ReturnTrashToDeckBottom(me, c);
        }

        // 收益：废弃区中 1 张“斯巴达因”以外、费用≤4、含＜CP＞的角色以休息状态登场
        var playCands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            !c.Info.NameContains("斯巴达因") &&
            c.Info.Cost <= 4 &&
            HasCP(c)).ToList();
        if (playCands.Count == 0) return;

        var playExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "登场 1 张费用≤4 含＜CP＞的角色（休息状态）",
            playCands.Select(c => c.Id.ToString()).ToList(), 0, 1, playExtra);
        if (chosen.Count > 0)
        {
            var picked = playCands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked, restState: true);
        }
    }
}
