using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-003 克罗卡斯
/// 【KO时】可以公开我方手牌中的 2 张事件：
///   将我方手牌中最多 1 张力量不高于 3000 的红色角色卡牌登场。
///
/// 实现说明：
///   - "公开 2 张事件" 为发动成本：手牌需有 ≥2 张事件，公开（选择展示）2 张，不丢弃。
///   - 登场目标：手牌中力量≤3000 且颜色含红（元素色"红"）的角色卡，最多 1 张，免费登场。
///   - 客户端通过 prompt 的 extra.choiceCards 显示手牌卡面。
/// </summary>
public class OP12_003_Crocus : IScriptedEffect
{
    public string CardNumber => "OP12-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本：手牌中需有 ≥2 张事件
        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count < 2) return;

        // 可登场目标：力量≤3000 的红色角色
        bool HasRed(CardInstance c) => c.Info.ColorList.Contains("红");
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Power <= 3000 && HasRed(c)).ToList();
        if (playable.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克罗卡斯【KO时】：公开手牌中 2 张事件，将手牌中 1 张力量≤3000 的红色角色登场？");
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

        // 选 1 张红色角色登场
        var playExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "选择最多 1 张力量≤3000 的红色角色登场",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, playExtra);
        if (chosen.Count == 0) return;

        var picked = playable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
