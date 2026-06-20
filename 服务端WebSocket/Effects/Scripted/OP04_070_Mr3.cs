using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-070 Mr.3(加尔迪诺)（角色 / 暗 / 巴洛克工作室）
/// 【对方攻击时】【每回合1次】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   本回合中，对方最多 1 张角色力量-1000。
///
/// 实现说明：
///   - 每回合 1 次；可选成本咚-1；对方最多 1 张角色本回合 -1000。
/// </summary>
public class OP04_070_Mr3 : IScriptedEffect
{
    public string CardNumber => "OP04-070";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.TotalDonInCostArea < 1) return;

        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.3【对方攻击时】：咚!!-1，本回合对方 1 张角色 -1000？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        me.TurnOnceUsed.Add(key);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量 -1000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -1000);
        }
    }
}
