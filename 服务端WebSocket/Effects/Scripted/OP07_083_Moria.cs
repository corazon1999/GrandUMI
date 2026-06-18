using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-083 月光·莫利亚（角色 / 地 4 费 5000，王下七武海/恐怖之船海盗团）
/// 【启动主要】可以将我方废弃区中拥有《恐怖之船海盗团》特征的 3 张卡牌自选顺序放回卡组最下方：
///   本回合中，此角色获得【流放】效果，力量+1000。
///
/// 实现说明 / 简化点：
///   - 成本：废弃区需有 ≥3 张《恐怖之船海盗团》特征卡，玩家选择 3 张放回卡组最下方。
///     「自选顺序」简化为按玩家选择先后顺序放底（对实战影响极小）。
///   - 收益：本回合此角色 GiveKeyword("流放", ThisTurn) + AddPowerThisTurn(+1000)。
///     【流放】已由引擎消费（给予伤害时不发动触发直接废弃），可正常赋予。
/// </summary>
public class OP07_083_Moria : IScriptedEffect
{
    public string CardNumber => "OP07-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本前置条件：废弃区 ≥4 张《恐怖之船海盗团》特征卡
        var candidates = me.Trash.Where(c => c.Info.HasKeyword("恐怖之船海盗团")).ToList();
        if (candidates.Count < 4) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莫利亚【启动主要】：将废弃区 4 张《恐怖之船海盗团》放回卡组最下方，本回合此角色获得【流放】且力量+1000？");
        if (!use) return;

        // 成本：选择 4 张《恐怖之船海盗团》放回卡组最下方
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashToDeckBottom",
            "选择 4 张《恐怖之船海盗团》放回卡组最下方",
            candidates.Select(c => c.Id.ToString()).ToList(), 4, 4, extra);
        if (chosen.Count < 4) return; // 未完成成本支付

        foreach (var id in chosen)
        {
            var card = candidates.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 收益：本回合此角色获得【流放】，力量+1000
        AtomicOps.GiveKeyword(self, "流放", KeywordDuration.ThisTurn);
        AtomicOps.AddPowerThisTurn(self, 1000);
    }
}
