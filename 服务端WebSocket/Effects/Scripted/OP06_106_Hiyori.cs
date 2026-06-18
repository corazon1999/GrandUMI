using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-106 光月日和（角色 / 光 / 和之国・光月家）
/// 【登场时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   将我方最多 1 张手牌加入生命区最上方。
///
/// 说明 / 简化点：
///   - 触发节无 cost 通道，故脚本实现"取生命顶/底 1 张入手"作为可选成本。
///   - 取生命牌入手：手动 me.LifeArea.RemoveAt + me.Hand.Add（生命牌私有，经
///     extra.choiceCards 下发卡面让玩家选择顶 / 底）。
///   - 将手牌置入生命区最上方：引擎无对应 AtomicOps，手动 me.Hand.Remove + me.LifeArea.Insert(0,..)。
/// </summary>
public class OP06_106_Hiyori : IScriptedEffect
{
    public string CardNumber => "OP06-106";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本前置：生命区需有牌
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "光月日和【登场时】：将生命区最上方或最下方 1 张卡牌加入手牌，再将 1 张手牌加入生命区最上方？");
        if (!use) return;

        // 成本：选择生命顶或底 1 张加入手牌
        var top = me.LifeArea[0];
        var bottom = me.LifeArea[me.LifeArea.Count - 1];
        // 顶 / 底可能为同一张（仅 1 张生命）
        var lifeCands = new List<CardInstance> { top };
        if (!ReferenceEquals(top, bottom)) lifeCands.Add(bottom);

        var lifeExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = lifeCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var lifeChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LifeTopOrBottom",
            "选择生命区最上方或最下方的 1 张卡牌加入手牌",
            lifeCands.Select(c => c.Id.ToString()).ToList(), 1, 1, lifeExtra);
        if (lifeChoice.Count == 0) return; // 未支付成本
        var takenLife = lifeCands.First(c => c.Id.ToString() == lifeChoice[0]);
        me.LifeArea.Remove(takenLife);
        me.Hand.Add(takenLife);

        // 效果：将我方最多 1 张手牌加入生命区最上方
        if (me.Hand.Count == 0) return;
        var handChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "选择最多 1 张手牌加入生命区最上方",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (handChoice.Count == 0) return;
        var toLife = me.Hand.First(c => c.Id.ToString() == handChoice[0]);
        me.Hand.Remove(toLife);
        me.LifeArea.Insert(0, toLife);
    }
}
