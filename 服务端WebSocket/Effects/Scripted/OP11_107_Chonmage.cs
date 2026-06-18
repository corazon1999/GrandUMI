using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-107 丁髷（角色 / 光 5 费 6000，海王类）
/// 【阻挡者】（关键词，由引擎处理）
/// 【启动主要】【每回合1次】我方领袖为"白星"的场合，可以将我方生命区最上方的1张卡牌翻至正面朝下：
///   当本回合结束时，将此角色转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 启动条件：领袖名含"白星"。
///   - 成本"将生命区最上方1张翻至正面朝下"：生命牌默认即为正面朝下，引擎不跟踪生命牌正反面状态，
///     故该成本无可表达的状态变化，按惯例只实现收益（仍要求生命区非空作为可支付前提）。
///   - "当本回合结束时转为活跃"为延迟到回合结束的效果：ActivatedMain 时设置标记，
///     借 OnMyTurnEnd 兑现将自身转为活跃。标记存于本卡 OncePerTurnUsedKeys（回合结束阶段清理）。
///   - 每回合1次：用 me.TurnOnceUsed 防重复。
/// </summary>
public class OP11_107_Chonmage : IScriptedEffect
{
    private const string PendingKey = "OP11-107-PendingActivate";

    public string CardNumber => "OP11-107";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.ActivatedMain || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            if (!self.OncePerTurnUsedKeys.Contains(PendingKey)) return;
            self.OncePerTurnUsedKeys.Remove(PendingKey);
            AtomicOps.ActivateCard(self);
            return;
        }

        // ActivatedMain
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 启动条件：领袖为"白星"
        if (!me.Leader.Info.NameContains("白星")) return;

        // 成本前提：生命区非空
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "丁髷【启动主要】：将生命区最上方 1 张翻至正面朝下，使此角色在本回合结束时转为活跃？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：将生命区最上方 1 张翻至正面朝下（生命牌默认即正面朝下，无状态变化）

        // 收益：标记延迟效果，待我方回合结束时将自身转为活跃
        self.OncePerTurnUsedKeys.Add(PendingKey);
    }
}
