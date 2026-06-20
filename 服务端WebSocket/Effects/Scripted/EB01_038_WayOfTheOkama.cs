using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-038 人妖之道（事件 / 暗 / 巴洛克工作室）
/// 【反击】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   我方领袖拥有的特征中包含《巴洛克工作室》的场合，选择我方的 1 张角色。将攻击对象变更为被选中的角色。
///
/// 实现说明：
///   - 【反击】= EventCounter 时机；此时我方为防守方，ctx.State.CurrentBattle 已建立。
///   - 可选成本【咚!!-1】：需费用区有可放回的咚!!。
///   - 仅领袖含《巴洛克工作室》时有收益，故仅在该前提下询问。
///   - 重定向：将 CurrentBattle 的目标改为选中的我方角色（TargetIsLeader=false, TargetCardId=该角色）。
/// </summary>
public class EB01_038_WayOfTheOkama : IScriptedEffect
{
    public string CardNumber => "EB01-038";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("巴洛克工作室")) return;

        var b = ctx.State.CurrentBattle;
        if (b is null) return;

        // 需费用区有可放回的咚!!
        if (me.TotalDonInCostArea < 1) return;

        var cands = me.Characters.ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "人妖之道【反击】：咚!!-1，将攻击对象变更为我方 1 张角色？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 1 张角色作为新的攻击对象",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        // 成本：咚!!-1（确认能完成重定向后再支付）
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var target = cands.First(c => c.Id.ToString() == chosen[0]);
        b.TargetIsLeader = false;
        b.TargetCardId = target.Id;
    }
}
