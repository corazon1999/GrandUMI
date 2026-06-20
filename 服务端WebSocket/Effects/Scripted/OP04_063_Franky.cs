using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-063 弗兰奇（角色 / 暗 / 七水之城·弗兰奇一家）
/// 【对方攻击时】【每回合1次】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   我方领袖拥有《七水之城》特征的场合，本次战斗中，我方最多 1 张领袖或角色力量+1000。
///
/// 实现说明：
///   - 仅领袖含《七水之城》时有收益；每回合 1 次；可选成本咚-1。
///   - 我方最多 1 张领袖/角色本次战斗 +1000：AddPowerThisBattle。
/// </summary>
public class OP04_063_Franky : IScriptedEffect
{
    public string CardNumber => "OP04-063";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (!me.Leader.Info.HasKeyword("七水之城")) return;
        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.TotalDonInCostArea < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "弗兰奇【对方攻击时】：咚!!-1，本次战斗我方 1 张领袖/角色 +1000？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        me.TurnOnceUsed.Add(key);

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗力量 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 1000);
        }
    }
}
