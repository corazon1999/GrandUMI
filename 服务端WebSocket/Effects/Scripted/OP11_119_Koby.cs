using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-119 可比（角色 / 地 8 费 9000，海军/利刃）
/// 【登场时】本回合中，我方最多1张角色可以攻击对方处于活跃状态的角色。
/// 【攻击时】可以将我方废弃区中的2张卡牌自选顺序放回卡组最下方：
///   直到下个对方的回合结束时为止，我方最多1张领袖或角色力量+1000。
///
/// 实现说明 / 简化点：
///   - 【登场时】"使我方角色可以攻击对方活跃角色"：引擎攻击校验硬性禁止攻击活跃状态角色，
///     且无"允许攻击活跃角色"的开关通道（参见 OP11_wf.json 注记），该段无法表达，已忽略。
///   - 故本脚本仅实现【攻击时】段：
///     成本"将废弃区 2 张自选顺序放回卡组最下方"用 ReturnTrashToDeckBottom 实现
///     （需废弃区≥2 张方可支付；"自选顺序"简化为玩家逐张选择，按选择先后放底）。
///     收益"+1000 持续到下个对方回合结束"用 AddPowerThisTurn 近似（引擎无该持续时长的力量通道）。
/// </summary>
public class OP11_119_Koby : IScriptedEffect
{
    public string CardNumber => "OP11-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本前提：废弃区≥2 张
        if (me.Trash.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "可比【攻击时】：将废弃区 2 张放回卡组最下方，使我方 1 张领袖或角色 +1000？");
        if (!use) return;

        // 成本：将废弃区 2 张自选顺序放回卡组最下方
        var trashExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var picks = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Trash",
            "将废弃区 2 张放回卡组最下方（选择顺序即放底顺序）",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 2, 2, trashExtra);
        if (picks.Count < 2) return; // 未完成成本

        foreach (var id in picks)
        {
            var card = me.Trash.FirstOrDefault(c => c.Id.ToString() == id);
            if (card != null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 收益：我方最多 1 张领袖或角色 +1000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张领袖或角色，力量 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(tgt, 1000);
    }
}
