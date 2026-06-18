using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-026 杰丽·邦妮（角色）
/// 【登场时】对方最多 1 张处于休息状态的角色或咚!!，在下个对方的重置阶段不会转为活跃状态。
///
/// 说明 / 简化点：
///   - "不会转为活跃"由 AtomicOps.PreventActivateNextReset 实现（设置卡的 CannotActivateNextReset 标记）。
///   - 该标记只作用于 CardInstance（角色），DonCard 无对应字段，引擎无"指定单张咚不活跃"通道，
///     故此处仅实现"对方休息角色"分支（最常用目标），舍弃"休息咚!!"分支。
/// </summary>
public class OP07_026_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "OP07-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters.Where(c => c.IsTapped).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张休息状态的角色，使其在下个对方的重置阶段不会转为活跃",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PreventActivateNextReset(tgt);
        }
    }
}
