using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-013 小八（1 费 2000）
/// 【启动主要】可以将此角色转为休息状态，并公开我方手牌中的 2 张事件：
///   赋予我方 1 张领袖或角色最多 2 张休息状态的咚!!。
///
/// 实现说明：
///   - 成本一：将此角色转为休息状态（要求此角色当前为活跃状态）。
///   - 成本二：公开我方手牌中的 2 张事件（要求手牌中存在 ≥2 张事件；公开不丢弃，仍留手牌）。
///     客户端通过 prompt 的 extra.choiceCards（{id, number} 列表）显示手牌卡面。
///   - 效果："赋予 1 张领袖或角色最多 2 张休息状态的咚!!"：从费用区休息状态的咚中取最多 2 张，
///     赋予玩家选定的 1 张我方领袖或角色（沿用 OP12-031 的 AttachDonFromCost(DonState.Rest) 约定）。
///   - 可选效果（"可以"）：通过 ConfirmOptional 询问是否发动；任一成本未满足/未支付则不发动。
/// </summary>
public class OP12_013_Hachi : IScriptedEffect
{
    public string CardNumber => "OP12-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本前置检查：此角色需为活跃状态
        if (ctx.Source.IsTapped) return;
        // 成本前置检查：手牌中需有 ≥2 张事件
        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count < 2) return;

        // 可选效果：询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "小八【启动主要】：将此角色转为休息状态并公开手牌中 2 张事件，赋予我方 1 张领袖或角色最多 2 张休息状态的咚!!？");
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

        // 支付成本：此角色转为休息状态
        ctx.Source.IsTapped = true;

        // 效果：选定 1 张我方领袖或角色，赋予最多 2 张休息状态的咚!!
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张领袖或角色，赋予最多 2 张休息状态的咚!!",
            targets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.AttachDonFromCost(me, target.Id, 2, DonState.Rest);
    }
}
