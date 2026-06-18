using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-038 波雅·汉库珂（领航 / 领袖类）
/// 【我方的回合中】【每回合1次】当角色因我方的效果离开场上时可以发动。
///   我方手牌不多于5张的场合，抽取1张卡牌。
///
/// 实现说明：
/// - 用 Wave2 反应式 watcher OnCharLeaveField 监听"角色因效果离开场上时"。
/// - 仅我方回合内有效；每回合1次；"可以发动" → ConfirmOptional。
/// - 条件：我方手牌 ≤5 张时才抽。
/// </summary>
public class OP07_038_BoaHancock : IScriptedEffect
{
    public string CardNumber => "OP07-038";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合1次
        var key = self.Info.Number + "-leave" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：我方手牌不多于5张
        if (me.Hand.Count > 5) return;

        // "可以发动"
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "波雅·汉库珂：当角色因我方效果离开场上时，抽取1张卡牌？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
