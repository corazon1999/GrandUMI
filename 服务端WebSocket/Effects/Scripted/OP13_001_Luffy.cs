using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-001 蒙奇·D·路飞（领航 4 费 5000，超新星/草帽一伙）
/// 【咚!!×1】【对方的攻击时】我方处于活跃状态的咚!!不多于 5 张的场合，
///   可以将我方任意张数的咚!!转为休息状态。每有 1 张转为休息状态的咚!!，
///   本次战斗中，此领袖或我方最多 1 张拥有《草帽一伙》特征的角色力量 +2000。
///
/// 实现：
///   - 【咚!!×1】= 此领袖须被赋予 ≥1 张咚。
///   - 条件：我方活跃咚 ≤5。
///   - 让玩家选择要把多少张活跃咚转为休息（0..活跃咚数），逐张把活跃咚改为休息状态。
///   - 每转 1 张，进行一次目标选择：本领袖 或 1 张《草帽一伙》角色，本次战斗 +2000。
///     （文本"此领袖或我方最多1张《草帽一伙》角色"= 每个 +2000 可分别指给领袖或某张草帽角色，
///      允许把多个 +2000 叠加到同一张目标上。）
/// 简化点：用 ChooseOption 让玩家选择转休息的咚张数。
/// </summary>
public class OP13_001_Luffy : IScriptedEffect
{
    public string CardNumber => "OP13-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【咚!!×1】：此领袖须被赋予 ≥1 张咚
        if (me.AttachedDonCount(me.Leader.Id) < 1) return;

        // 条件：我方活跃咚 ≤5
        int active = me.ActiveDonCount;
        if (active > 5) return;
        if (active <= 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞：将任意张数活跃咚!!转为休息状态，每张使此领袖或 1 张《草帽一伙》角色本次战斗力量 +2000？");
        if (!use) return;

        // 选择要转为休息的活跃咚张数（1..active）
        var options = new List<string>();
        for (int i = 1; i <= active; i++) options.Add($"转 {i} 张");
        int pick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择要转为休息状态的活跃咚!!张数", options);
        int restCount = pick < 0 ? 0 : pick + 1;
        if (restCount <= 0) return;
        restCount = Math.Min(restCount, active);

        // 逐张把活跃咚改为休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= restCount) break;
            if (d.State == DonState.Active)
            {
                d.State = DonState.Rest;
                rested++;
            }
        }

        // 每转 1 张，进行一次 +2000 目标选择：本领袖 或 1 张《草帽一伙》角色
        for (int i = 0; i < rested; i++)
        {
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters.Where(c => c.Info.HasKeyword("草帽一伙")));
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrStrawHat",
                $"第 {i + 1}/{rested} 个 +2000：选择此领袖或 1 张《草帽一伙》角色（本次战斗力量 +2000）",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) continue;
            var tgt = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (tgt is not null) AtomicOps.AddPowerThisBattle(tgt, 2000);
        }
    }
}
