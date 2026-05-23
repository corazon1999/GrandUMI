using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-041 巴奇（领航）
/// 当我方《因佩尔地狱》角色离场时，可发动每回合 1 次：
/// 从手牌登场 1 张"因佩尔地狱的囚犯"
/// 通过 OnKO 触发实现（"离场"含 KO；放回手牌/卡组等需扩展）
/// </summary>
public class OP16_041_Buggy : IScriptedEffect
{
    public string CardNumber => "OP16-041";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;
    public async Task Resolve(EffectContext ctx)
    {
        // 注意：当前 OnKO 仅 Resolve 被 KO 的卡自身的脚本
        // 此领航是"任何我方《因佩尔地狱》离场都触发自身效果"
        // 需要全局监听（M+1 改造 EffectRuntime 后再完善）
        // 占位：当本卡（领航）受 OnKO 时（实际不会发生）也无害
        var me = ctx.State.Players[ctx.OwnerIndex];
        const string key = "OP16-041-OncePerTurn";
        if (me.TurnOnceUsed.Contains(key)) return;

        var prisoners = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.MatchesName("因佩尔地狱的囚犯"))
            .ToList();
        if (prisoners.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandPrisoner",
            "选 1 张'因佩尔地狱的囚犯'登场", prisoners.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var card = prisoners.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
        me.TurnOnceUsed.Add(key);
    }
}
