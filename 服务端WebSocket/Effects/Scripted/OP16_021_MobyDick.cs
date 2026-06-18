using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-021 莫比·迪克号（舞台 / 炎 / 费用1 / 白胡子海盗团）
/// 【登场时】我方领袖拥有《白胡子海盗团》特征的场合，确认我方卡组最上方的3张卡牌，
///   将其中最多1张加入手牌。之后，将剩余的卡牌自选顺序放回卡组最下方。
/// 【启动主要】可以将此舞台放置到废弃区：赋予我方1张领袖或角色最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 登场检索范式同 OP05-043 润媞（私有「确认」→ choiceCards 下发候选，非公开 reveal）。
///   - 剩余牌按原相对顺序放卡组最下方（"自选顺序"简化为原序，对实战影响极小，沿用 Nami）。
///   - 启动主要："可以"=可选发动；成本为将本舞台移入废弃区；之后给 1 张我方领袖/角色附 1 张咚。
///     本引擎附着咚统一为 Attached 态（无"休息附着"子态），故"休息状态"为表述简化；
///     咚来源优先取活跃，无活跃则取休息（保证有咚可给时即可发动），与 OP13-021 同款措辞对齐。
/// </summary>
public class OP16_021_MobyDick : IScriptedEffect
{
    public string CardNumber => "OP16-021";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
            await OnEnter(ctx);
        else
            await OnActivated(ctx);
    }

    // 【登场时】确认顶3张，最多1张加入手牌，剩余放卡组最下方
    static async Task OnEnter(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖含《白胡子海盗团》特征
        if (!me.Leader.Info.HasKeyword("白胡子海盗团")) return;

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
            $"确认卡组顶 {peek} 张，将最多 1 张加入手牌",
            top.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = top.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
        }

        // 剩余按原序放卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        if (rest.Count > 0)
            AtomicOps.ReorderTopK(me, rest.Select(c => c.Id).ToList(), toBottom: true);
    }

    // 【启动主要】将本舞台移入废弃区：给我方1张领袖/角色附1张咚
    static async Task OnActivated(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // "可以"=可选发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莫比·迪克号【启动主要】：将此舞台放置到废弃区，赋予我方1张领袖或角色1张休息状态的咚!!？");
        if (!use) return;

        // 是否有可给的咚（活跃或休息）
        bool hasDon = me.CostArea.Any(d => d.State == DonState.Active || d.State == DonState.Rest);

        // 成本：将本舞台移入废弃区
        if (me.StageCard is not null && me.StageCard.Id == self.Id)
        {
            me.Trash.Add(me.StageCard);
            me.StageCard = null;
        }

        if (!hasDon) return; // 无咚可给，仅完成移入废弃区

        // 选我方1张领袖或角色，赋予最多1张咚
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方1张领袖或角色，赋予最多1张休息状态的咚!!",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = targets.First(c => c.Id.ToString() == chosen[0]);
        int n = AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Active);
        if (n == 0) AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
    }
}
