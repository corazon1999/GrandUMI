using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-035 砍人镰造（角色 / 暗 / SMILE・超新星・基德海盗团 / cost3 power4000 counter1000）
/// 【阻挡者】（关键词，由引擎处理）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，我方领袖拥有《基德海盗团》特征的场合，
///   从咚!!卡组中追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 用 OnDonReturnedToDeck watcher（"我方咚!!放回咚!!卡组时"派发）。
///   - 限我方回合、每回合1次；领袖含《基德海盗团》才结算；RefreshDonFromDeck 1 休息咚。
///   - 【阻挡者】为关键词，引擎处理，本脚本只实现触发效果。
/// </summary>
public class EB04_035_Killer : IScriptedEffect
{
    public string CardNumber => "EB04-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return; // 限我方回合

        var key = ctx.Source.Info.Number + "-don" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return; // 每回合1次

        if (!me.Leader.Info.HasKeyword("基德海盗团")) return; // 领袖条件

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "砍人镰造：从咚!!卡组中追加最多 1 张休息状态的咚!!？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
