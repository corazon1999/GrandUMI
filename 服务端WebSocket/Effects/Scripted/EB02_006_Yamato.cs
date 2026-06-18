using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-006 大和（角色 / 炎 / 和之国）
/// 【启动主要】【每回合1次】我方领袖拥有《和之国》特征或为"波特夹斯·D·艾斯"的场合，
///   赋予我方 1 张领袖最多 1 张休息状态的咚!!。之后，本回合中，此角色获得【速攻】效果。
///
/// 实现说明：
///   - 领袖条件为「含《和之国》特征」OR「名称为艾斯」的跨键 OR，用 C# 直接 || 表达。
///   - 赋予领袖 1 张休息咚用 AttachDonFromCost(... DonState.Rest)；本回合速攻用 GiveKeyword(ThisTurn)。
///   - 每回合1次按 TurnOnceUsed 记录。
/// </summary>
public class EB02_006_Yamato : IScriptedEffect
{
    public string CardNumber => "EB02-006";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 领袖条件：含《和之国》特征 或 名为"波特夹斯·D·艾斯"
        bool cond = me.Leader.Info.HasKeyword("和之国") || me.Leader.Info.NameContains("波特夹斯·D·艾斯");
        if (!cond) return Task.CompletedTask;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 赋予我方领袖最多 1 张休息状态的咚!!
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

        // 之后，本回合中此角色获得【速攻】
        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);

        return Task.CompletedTask;
    }
}
