using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-002 特拉法尔加·罗（角色 / 炎 / 班克禁区・王下七武海・心脏海盗团 / cost6 power7000）
/// 【每回合1次】此角色因对方的效果将要离开场上的场合，可以改为此角色在本回合中力量-2000，使此角色不离场。
/// 【攻击时】本回合中，对方最多1张角色力量-2000。
///
/// 实现说明 / 简化点：
///   - 置换防离场用 PreKO 钩子（自身将要被KO时触发）。引擎无“因效果将要离场”（含退回手牌/放回卡组等
///     非KO离场）的统一前置钩子，故该置换仅覆盖“将要被KO”的场合；非KO离场分支无通道，未实现。
///     置换内容：本回合力量-2000 + MarkPreventKO 取消本次KO。每回合1次。
///   - 【攻击时】对方最多1张角色本回合力量-2000，可 DSL/脚本表达，此处一并实现。
/// </summary>
public class PRB02_002_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "PRB02-002";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.PreKO || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            var key = self.Info.Number + "-guard" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "罗【每回合1次】：改为本回合此角色力量-2000，使其不被KO？");
            if (!use) return;

            AtomicOps.AddPowerThisTurn(self, -2000);
            ctx.State.MarkPreventKO(self.Id);
            me.TurnOnceUsed.Add(key);
            return;
        }

        // ── 【攻击时】对方最多1张角色力量-2000 ──
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "本回合中，对方最多1张角色力量-2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
