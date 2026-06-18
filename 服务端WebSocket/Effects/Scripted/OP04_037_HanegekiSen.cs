using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-037 羽击线（事件 / 风 / 王下七武海・堂吉诃德海盗团）
/// 【反击】我方领袖拥有《堂吉诃德海盗团》特征的场合，本回合中，我方最多 1 张领袖或角色力量+2000。
/// 【触发】将对方最多 1 张处于休息状态且费用不高于 4 的角色 KO。（触发节由引擎/DSL 处理，此处仅实现反击。）
///
/// 实现说明：
///   - 反击节带领袖特征前置条件，C# 直接判 me.Leader.Info.HasKeyword。
///   - 选我方 1 张领袖或角色，本回合 +2000（AddPowerThisTurn）。
/// </summary>
public class OP04_037_HanegekiSen : IScriptedEffect
{
    public string CardNumber => "OP04-037";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 前置：我方领袖拥有《堂吉诃德海盗团》特征
        if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本回合中，我方最多 1 张领袖或角色力量+2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 2000);
        }
    }
}
