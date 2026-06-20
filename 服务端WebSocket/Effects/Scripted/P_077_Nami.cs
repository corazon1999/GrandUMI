using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-077 润媞（角色 / 地 / 百兽海盗团）
/// 【每回合1次】当我方场上的 2 张或更多咚!! 放回咚!!卡组时，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///   之后，将我方最多 1 张紫色舞台转为活跃状态。
///
/// 实现说明：用 OnDonReturnedToDeck watcher，payload count≥2 才触发。
/// </summary>
public class P_077_Nami : IScriptedEffect
{
    public string CardNumber => "P-077";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        int count = ctx.Vars.TryGetValue("count", out var v) && v is int n ? n : 0;
        if (count < 2) return;

        var key = "P-077-donreturn:" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);

        // 将我方最多 1 张紫色舞台转为活跃状态
        if (me.StageCard is not null && me.StageCard.Info.ColorList.Contains("紫") && me.StageCard.IsTapped)
        {
            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将我方紫色舞台转为活跃状态？");
            if (use) AtomicOps.ActivateCard(me.StageCard);
        }
    }
}
