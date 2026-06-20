using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-074 贝宝（角色）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，
///   本回合中，我方最多1张领袖或角色力量+1000。
///
/// 实现说明：
///   - 用 Wave2 反应式 watcher OnDonReturnedToDeck 监听"咚!!放回咚!!卡组"。
///   - 仅在我方回合、每回合1次；让玩家从我方领袖/角色中选最多1张本回合+1000。
/// </summary>
public class OP09_074_Bepo : IScriptedEffect
{
    public string CardNumber => "OP09-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 【每回合1次】
        var key = CardNumber + "-donreturn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 我方最多1张领袖或角色本回合+1000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        if (targets.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本回合力量+1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 1000);
        }
    }
}