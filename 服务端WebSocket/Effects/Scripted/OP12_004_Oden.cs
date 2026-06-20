using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-004 光月御殿
/// 【启动主要】【每回合 1 次】可以公开我方手牌中的 2 张事件：本回合中，此角色的力量 +2000。
///
/// 实现说明：
///   - "公开 2 张事件" 为发动成本：手牌需有 ≥2 张事件，公开（选择展示）2 张，不丢弃。
///   - 客户端通过 prompt 的 extra.choiceCards 显示手牌卡面。
/// </summary>
public class OP12_004_Oden : IScriptedEffect
{
    public string CardNumber => "OP12-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP12-004-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：手牌中需有 ≥2 张事件
        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "光月御殿【启动主要】：公开手牌中 2 张事件，使此角色本回合力量+2000？");
        if (!use) return;

        // 公开（选择）2 张事件作为成本
        var revealExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var revealed = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealEvents",
            "公开手牌中的 2 张事件",
            events.Select(c => c.Id.ToString()).ToList(), 2, 2, revealExtra);
        if (revealed.Count < 2) return;

        // 向对手公开所选 2 张事件：广播牌面浮层（公开≠丢弃，仍留手牌）
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex,
            revealed.Select(id => events.First(c => c.Id.ToString() == id).Info.Number).ToList());

        // 效果：此角色本回合力量 +2000
        AtomicOps.AddPowerThisTurn(ctx.Source, 2000);

        me.TurnOnceUsed.Add(key);
    }
}
