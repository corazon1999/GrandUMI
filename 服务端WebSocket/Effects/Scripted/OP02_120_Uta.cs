using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-120 乌塔（角色）
/// 【登场时】咚!!-2（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   直到下个我方的回合开始时为止，我方所有的领袖和角色力量+1000。
///
/// 实现说明：
///   - 成本"咚!!-2"为可选：需我方咚区合计≥2，ConfirmOptional 询问后 ReturnDonToDeck(me, 2)。
///   - "直到下个我方回合开始时" 的跨回合群体加成用 ContinuousEffect.PowerDelta 注册：
///     Scope 覆盖我方领袖+全部角色；Predicate 用 TurnCount 限制有效期。
///     在我方回合 N 注册，下个我方回合为 N+2（中间夹对方回合 N+1），故 TurnCount < N+2 时生效。
///   - 来源为本卡，本卡离场时引擎自动清理；此处不去重（登场一次注册一次符合文本）。
/// </summary>
public class OP02_120_Uta : IScriptedEffect
{
    public string CardNumber => "OP02-120";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        // 成本：咚!!-2（可选）
        if (me.CostArea.Count < 2) return;
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乌塔【登场时】：将2张咚!!放回咚!!卡组，使我方全体领袖和角色力量+1000（直到下个我方回合开始）？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;

        int baseTurn = ctx.State.TurnCount;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = ctx.Source.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) => sideIdx == owner && s.TurnCount < baseTurn + 2,
        });
    }
}
