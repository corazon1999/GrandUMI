using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-002 特拉法尔加·罗（角色 / 炎 / 班克禁区・王下七武海・心脏海盗团 / cost6 power7000）
/// 【每回合1次】此角色因对方的效果将要离开场上的场合，可以改为此角色在本回合中力量-2000，使此角色不离场。
/// 【攻击时】本回合中，对方最多1张角色力量-2000。
///
/// 实现说明：
///   - 对方效果KO走 PreKO，非KO效果离场走 OnAllyWillLeaveField。
///     置换内容：本回合力量-2000并阻止对应离场；两条路径共用每回合1次标记。
///   - 【攻击时】对方最多1张角色本回合力量-2000，可 DSL/脚本表达，此处一并实现。
/// </summary>
public class PRB02_002_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "PRB02-002";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.PreKO or EffectTrigger.OnAllyWillLeaveField or EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillLeaveField)
        {
            bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
            if (!nonKoLeave &&
                (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;
            if (nonKoLeave)
            {
                var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
                var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
                if (victimOwner != ctx.OwnerIndex || victimId != self.Id.ToString()) return;
            }

            var key = self.Info.Number + "-guard" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "罗【每回合1次】：改为本回合此角色力量-2000，使其不离场？");
            if (!use) return;

            AtomicOps.AddPowerThisTurn(self, -2000);
            if (nonKoLeave) ctx.State.MarkPreventLeave(self.Id);
            else ctx.State.MarkPreventKO(self.Id);
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
