using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-031 堂吉诃德·多弗拉门戈（角色）
/// 【登场时】对方合计最多 3 张处于休息状态的领袖和角色，在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明：
///   - 对方处于休息状态（IsTapped）的领袖与角色为候选；玩家最多选 3 张，
///     对每张设置 CannotActivateNextReset = true（引擎在对方重置阶段会跳过这些卡的活跃）。
/// </summary>
public class OP04_031_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP04-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = new List<CardInstance>();
        if (opp.Leader != null && opp.Leader.IsTapped) cands.Add(opp.Leader);
        cands.AddRange(opp.Characters.Where(c => c.IsTapped));
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选择对方合计最多 3 张休息状态的领袖/角色，使其下个重置阶段不转为活跃",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 3);

        foreach (var id in chosen)
        {
            var c = cands.First(x => x.Id.ToString() == id);
            c.CannotActivateNextReset = true;
        }
    }
}
