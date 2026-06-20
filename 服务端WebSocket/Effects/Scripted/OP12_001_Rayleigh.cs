using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-001 希尔巴兹·雷利（领航）
/// 规则上，我方无法将费用为 5 或更高的卡牌加入卡组。（构筑限制，非对战时效果，引擎对战阶段不建模，忽略）
/// 【启动主要】【每回合 1 次】可以公开我方手牌中的 2 张事件：
///   本回合中，我方最多 1 张原本的力量不高于 4000 的角色力量 +2000。
///
/// 实现说明：
///   - "公开 2 张事件" 是发动该启动效果的成本。要求手牌中存在 ≥2 张事件；
///     公开即向玩家展示并选择 2 张事件（不丢弃，仍留在手牌）。
///   - 客户端通过 prompt 的 extra.choiceCards（{id, number} 列表）显示手牌卡面。
///   - "原本的力量不高于 4000" 取 c.Info.Power（卡面原始力量）。
/// </summary>
public class OP12_001_Rayleigh : IScriptedEffect
{
    public string CardNumber => "OP12-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP12-001-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：手牌中需有 ≥2 张事件
        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count < 2) return;

        // 可选效果：询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "雷利【启动主要】：公开手牌中 2 张事件，使我方最多 1 张原本力量≤4000 的角色力量+2000？");
        if (!use) return;

        // 公开（选择）2 张事件作为成本
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

        // 效果：我方最多 1 张原本力量≤4000 的角色 +2000
        var targets = me.Characters.Where(c => c.Info.Power <= 4000).ToList();
        if (targets.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择最多 1 张原本力量≤4000 的角色，本回合力量+2000",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, 2000);
            }
        }

        me.TurnOnceUsed.Add(key);
    }
}
