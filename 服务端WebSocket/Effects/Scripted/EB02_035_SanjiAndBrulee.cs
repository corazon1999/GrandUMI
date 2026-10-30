using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-035 山智&布玲（角色 / 暗 / 大妈海盗团·草帽一伙，cost5 power7000）
/// 【我方的回合中】【每回合1次】当我方场上的2张或更多咚!!放回咚!!卡组时，
///   从咚!!卡组中追加最多1张活跃状态的咚!!。
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，抽取1张卡牌。
///
/// 实现说明：
///   - 反应式 watcher OnDonReturnedToDeck（payload ctx.Vars["count"]）：仅我方回合、本次放回≥2张、每回合1次时，
///     RefreshDonFromDeck(me, 1, Active) 追加1张活跃咚。
///   - 【登场时】条件「我方咚≤对方咚」用 TotalDonInCostArea 比较。
/// </summary>
public class EB02_035_SanjiAndBrulee : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "EB02-035";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnDonReturnedToDeck;

    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnDonReturnedToDeck
            || (state.CurrentTurnPlayer == owner
                && payload?.GetValueOrDefault("owner") is int returnedOwner && returnedOwner == owner
                && payload.GetValueOrDefault("count") is int count && count >= 2
                && !state.Players[owner].TurnOnceUsed.Contains($"{CardNumber}-don:{source.Id}"));

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.TotalDonInCostArea <= opp.TotalDonInCostArea)
                await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnDonReturnedToDeck)
        {
            // 回合与返咚归属必须同时匹配，不能因对手反击返咚而消耗我方次数。
            if (!IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;

            // 【每回合1次】——标记移到"确认追加"之后再消耗，选择不追加则不占用本回合机会
            var key = ctx.Source.Info.Number + "-don" + ":" + ctx.Source.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            // 卡面"从咚!!卡组追加【最多1张】活跃咚!!"→可选，先问玩家是否追加（反馈#174:无法自选是否跳咚）
            if (await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "是否从咚!!卡组追加1张活跃状态的咚!!？"))
            {
                AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
                me.TurnOnceUsed.Add(key);
            }
            return;
        }

        return;
    }
}
