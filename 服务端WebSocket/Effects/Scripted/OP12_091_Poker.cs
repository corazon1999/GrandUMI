using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-091 扑克（3 费 4000，百兽海盗团/SMILE）
/// 【启动主要】【每回合1次】可以将我方废弃区中的 3 张卡牌自选顺序放回卡组最下方：
///   本回合中，我方最多 2 张拥有《SMILE》特征的角色力量 +2000。
///
/// 说明：
///   - 整个效果可选（"可以…"）。成本：从废弃区任选 3 张放回卡组最下方
///     （"自选顺序"简化为玩家选择顺序即放底顺序）。废弃区不足 3 张则无法发动。
///   - 效果：我方场上最多 2 张拥有《SMILE》特征的角色，本回合各力量 +2000。
///   - 废弃区选牌经 extra.choiceCards 下发卡面给客户端。
/// </summary>
public class OP12_091_Poker : IScriptedEffect
{
    public string CardNumber => "OP12-091";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【每回合1次】
        var key = "OP12-091-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本前提：废弃区至少 3 张
        if (me.Trash.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "扑克【启动主要】：将废弃区 3 张卡放回卡组最下方，使我方最多 2 张《SMILE》角色本回合力量 +2000？");
        if (!use) return;

        // 选 3 张废弃区卡作为成本（顺序即放回卡组底部的顺序）
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var costChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Poker091Cost",
            "选择 3 张废弃区卡放回卡组最下方（自选顺序）",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 3, 3, costExtra);
        if (costChosen.Count < 3) return; // 未完成支付

        foreach (var cid in costChosen)
        {
            var card = me.Trash.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 标记本回合已用
        me.TurnOnceUsed.Add(key);

        // 效果：我方最多 2 张拥有《SMILE》特征的角色力量 +2000
        var candidates = me.Characters.Where(c => c.Info.HasKeyword("SMILE")).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 2 张《SMILE》角色，本回合力量 +2000",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 2);
        foreach (var cid in chosen)
        {
            var card = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.AddPowerThisTurn(card, 2000);
        }
    }
}
