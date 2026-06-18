using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-043 蒙奇·D·路飞（角色）
/// 【登场时】将最多 1 张费用不高于 3 的角色放回其持有者的手牌。
///   → 双方任意费用≤3 的角色均为候选；选中后用 BounceToHand 退回各自持有者手牌。
/// </summary>
public class P_043_Luffy : IScriptedEffect
{
    public string CardNumber => "P-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];
        var opp = ctx.State.Players[1 - owner];

        // 候选：双方场上费用≤3 的角色（含本卡自身亦可被退回，按文本"角色"不排除）
        var mine = me.Characters.Where(c => c.Info.Cost <= 3).ToList();
        var theirs = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        var all = new List<CardInstance>();
        all.AddRange(mine);
        all.AddRange(theirs);
        if (all.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(owner, "AnyCharacter",
            "将最多 1 张费用≤3 的角色放回其持有者手牌（最多 1 张）",
            all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = all.First(c => c.Id.ToString() == chosen[0]);
        // 判定目标所属方
        int tgtOwner = mine.Contains(tgt) ? owner : 1 - owner;
        AtomicOps.BounceToHand(ctx.State, tgtOwner, tgt);
    }
}
