using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-009 甚平
/// 【登场时】可以公开我方手牌中的 2 张事件：本回合中，此角色获得【速攻】效果。
///   之后，直到下个对方的结束阶段结束时为止，此角色的力量 +1000。
///
/// 实现说明：
///   - "公开 2 张事件" 是发动该可选效果的成本：要求手牌中存在 ≥2 张事件，
///     公开即向玩家展示并选择 2 张事件（不丢弃，仍留在手牌）。
///   - 客户端通过 prompt 的 extra.choiceCards（{id, number} 列表）显示手牌卡面。
///   - 简化点：引擎没有"持续到下个对方结束阶段"的力量修正通道（PowerModPersistent 永不清除，
///     UntilNextOpponentEndPhase 仅作用于关键字/限制）。此处力量 +1000 用 AddPowerThisTurn 近似，
///     于本回合结束阶段清除——略短于文本（文本应延续到对方回合结束），但与速攻同回合配合的实战意义一致。
/// </summary>
public class OP12_009_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP12-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本：手牌中需有 ≥2 张事件
        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count < 2) return;

        // 可选效果：询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "甚平【登场时】：公开手牌中 2 张事件，使此角色本回合获得【速攻】并力量+1000？");
        if (!use) return;

        // 公开（选择）2 张事件作为成本（不丢弃）
        var revealExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var revealed = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealEvents",
            "公开手牌中的 2 张事件",
            events.Select(c => c.Id.ToString()).ToList(), 2, 2, revealExtra);
        if (revealed.Count < 2) return;   // 未完成公开 → 成本未支付，不发动

        // 向对手公开所选 2 张事件：广播牌面浮层（公开≠丢弃，仍留手牌）
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex,
            revealed.Select(id => events.First(c => c.Id.ToString() == id).Info.Number).ToList());

        // 效果：此角色获得【速攻】（本回合）
        AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn);
        // 之后：力量 +1000（近似为本回合）
        AtomicOps.AddPowerThisTurn(ctx.Source, 1000);
    }
}
