using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-006 乌普·史拉普（角色）
/// 【登场时】赋予我方 1 张"蒙奇·D·路飞"最多 2 张休息状态的咚!!。
///
/// 说明 / 简化点：
///   - 目标限定为我方场上卡牌名为"蒙奇·D·路飞"的角色（含别名匹配 MatchesName）。
///     无符合目标时不发动。
///   - "1 张"目标：存在多张时让玩家选择 1 张（必选，min=1），仅 1 张时仍走选择以统一交互。
///   - "最多 2 张休息状态的咚"：从费用区只取休息状态的咚（最多 2 张）赋予目标；
///     无休息咚则少赋予/不赋予，不回退取活跃咚（见 AttachDonFromCost）。
///   - 候选为场上公开卡，无需 extra.choiceCards。
/// </summary>
public class OP13_006_Uppslap : IScriptedEffect
{
    public string CardNumber => "OP13-006";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 我方场上名为"蒙奇·D·路飞"的角色
        var targets = me.Characters.Where(c => c.MatchesName("蒙奇·D·路飞")).ToList();
        if (targets.Count == 0) return;

        CardInstance target;
        if (targets.Count == 1)
        {
            target = targets[0];
        }
        else
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLuffy",
                "选择 1 张\"蒙奇·D·路飞\"，赋予最多 2 张休息状态的咚!!",
                targets.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (chosen.Count == 0) return;
            target = targets.First(c => c.Id.ToString() == chosen[0]);
        }

        // 赋予最多 2 张休息状态的咚!!：只取费用区中已是休息状态的咚，无休息咚则少赋予/不赋予
        // （不回退取活跃咚，统一「赋予休息咚」语义，见 AttachDonFromCost 与 ST17-004 修复记录）
        AtomicOps.AttachDonFromCost(me, target.Id, 2, DonState.Rest);
    }
}
