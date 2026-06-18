using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-096 少女（角色）
/// 【登场时】抽取1张卡牌，丢弃我方的1张手牌。
/// 【启动主要】【每回合1次】赋予我方1张"奈美"最多1张休息状态的咚!!。
///
/// 之前完全未实现（仅在 _GAP_wf.json 缺口标记）。P 系列无 DSL 文件，按 P-107 惯例写脚本。
///   - 登场段：抽1，再选1张手牌丢弃（强制，有手牌才丢）。
///   - 启动主要（每回合1次）：选我方名为"奈美"的领袖或角色，赋1张休息咚
///     （AttachDonFromCost Rest，与全局赋休息咚机制一致：无休息咚则不赋）。无"奈美"则不发动。
/// </summary>
public class P_096_Shoujo : IScriptedEffect
{
    public string CardNumber => "P-096";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            if (me.Hand.Count > 0)
            {
                var dExtra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                    "丢弃我方1张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, dExtra);
                if (dch.Count >= 1)
                {
                    var dcard = me.Hand.First(c => c.Id.ToString() == dch[0]);
                    AtomicOps.DiscardHand(me, dcard);
                }
            }
            return;
        }

        // ActivatedMain：每回合1次，赋"奈美"休息咚
        const string key = "P-096-Activated";
        if (me.TurnOnceUsed.Contains(key)) return;

        var cands = new List<CardInstance>();
        if (me.Leader.Info.NameContains("奈美")) cands.Add(me.Leader);
        cands.AddRange(me.Characters.Where(c => c.Info.NameContains("奈美")));
        if (cands.Count == 0) return; // 场上无"奈美" → 不发动

        me.TurnOnceUsed.Add(key);

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选我方1张《奈美》赋予最多1张休息状态的咚!!",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var target = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
        }
    }
}
