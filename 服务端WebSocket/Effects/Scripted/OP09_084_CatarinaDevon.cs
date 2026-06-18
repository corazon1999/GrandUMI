using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-084 卡特琳·蝶美（角色）
/// 【启动主要】【每回合1次】我方领袖拥有《黑胡子海盗团》特征的场合，直到下个对方的回合结束时为止，
///   此角色获得【双重攻击】或【流放】或【阻挡者】效果。
///
/// 实现说明 / 简化点：
/// - 用 ChooseOption 三选一，再用 GiveKeyword(self, kw, UntilNextOpponentEndPhase) 赋予所选关键词。
/// - 【双重攻击】【阻挡者】由引擎按关键词生效；【流放】关键词引擎未实现，选它仅记录关键词但无机制收益
///   （与文本一致提供选项，玩家自行权衡）。
/// - 每回合 1 次用 TurnOnceUsed 防重复发动。
/// </summary>
public class OP09_084_CatarinaDevon : IScriptedEffect
{
    public string CardNumber => "OP09-084";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 前提：领袖《黑胡子海盗团》
        if (!me.Leader.Info.HasKeyword("黑胡子海盗团")) return;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var options = new[] { "双重攻击", "流放", "阻挡者" };
        int idx = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "蝶美【启动主要】：此角色获得以下其一效果（直到下个对方回合结束）", options);
        if (idx < 0 || idx >= options.Length) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.GiveKeyword(self, options[idx], KeywordDuration.UntilNextOpponentEndPhase);
    }
}
