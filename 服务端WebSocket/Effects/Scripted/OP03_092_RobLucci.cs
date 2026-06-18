using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-092 罗布·鲁兹（角色 / 地 / CP9）
/// 【登场时】可以将我方废弃区中 2 张拥有的特征中包含&lt;CP&gt;的卡牌自选顺序放回卡组最下方：
///   本回合中，此角色获得【速攻】效果。
///
/// 实现说明：
///   - "可以…"=可选；成本为将废弃区 2 张含 CP 特征卡放回卡组底（自选顺序简化为所选顺序）。
///   - 成本与收益强耦合：完成放回后才赋予本回合【速攻】。
///   - 本回合临时关键词用 GiveKeyword(self,"速攻",ThisTurn)。
/// </summary>
public class OP03_092_RobLucci : IScriptedEffect
{
    public string CardNumber => "OP03-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        bool HasCp(CardInstance c) => c.Info.Keywords.Any(k => k.Contains("CP"));
        var cpTrash = me.Trash.Where(HasCp).ToList();
        if (cpTrash.Count < 2) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗布·鲁兹【登场时】：将废弃区 2 张含<CP>特征卡放回卡组最下方，使此角色本回合获得【速攻】？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cpTrash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCpToDeckBottom",
            "选择 2 张含<CP>特征的废弃区卡放回卡组最下方",
            cpTrash.Select(c => c.Id.ToString()).ToList(), 2, 2, extra);
        if (chosen.Count < 2) return;

        foreach (var id in chosen)
        {
            var card = cpTrash.First(c => c.Id.ToString() == id);
            AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
    }
}
