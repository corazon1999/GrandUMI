using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-027 山智（角色 / 风 5 费 7000，FILM/草帽一伙）
/// 【登场时】将我方最多2张咚!!转为活跃状态。
/// 【我方的回合结束时】我方领袖拥有《FILM》或《草帽一伙》特征的场合，
///   将我方最多1张咚!!转为活跃状态。
///
/// 实现：
///   - 两个触发：OnEnterField（登场时）+ OnMyTurnEnd（我方的回合结束时）。
///   - "最多N张"咚转活跃：由玩家明确选择 0..N 张，响应后按当前费用区重新校验再结算。
///   - 回合结束时附带领袖特征条件（FILM 或 草帽一伙）。
/// </summary>
public class OP13_027_Sanji : IScriptedEffect
{
    public string CardNumber => "OP13-027";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await ActivateRestDon(ctx, 2, "山智【登场时】：选择要转为活跃状态的休息咚!!张数（最多 2 张）");
        }
        else if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            // 我方领袖拥有《FILM》或《草帽一伙》特征
            if (me.Leader.Info.HasKeyword("FILM") || me.Leader.Info.HasKeyword("草帽一伙"))
                await ActivateRestDon(ctx, 1, "山智【回合结束时】：选择要转为活跃状态的休息咚!!张数（最多 1 张）");
        }
    }

    private static Task<int> ActivateRestDon(EffectContext ctx, int max, string text)
        => AtomicOps.PromptChooseAndApplyDonCount(
            ctx.State,
            ctx.Prompts,
            ctx.OwnerIndex,
            max,
            text,
            don => don.State == DonState.Rest && don.AttachedToCardId is null,
            don => don.State = DonState.Active);
}
