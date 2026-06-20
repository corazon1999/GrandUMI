using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-069 克洛克达尔（6 费 8000，王下七武海/巴洛克工作室）
/// 【对方的攻击时】【每回合1次】咚!!-1：
///   我方领袖拥有的特征中包含〈巴洛克工作室〉的场合，
///   本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///
/// 实现：对方攻击时触发，每回合限 1 次。需领袖带《巴洛克工作室》特征。
/// 询问是否发动；发动则支付代价咚!!-1（将 1 张活跃咚放回咚卡组），
/// 然后从我方领袖或角色中最多选 1 张，本次战斗力量 +2000。
/// </summary>
public class OP12_069_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP12-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = "OP12-069-OncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：我方领袖须拥有《巴洛克工作室》特征
        if (!me.Leader.Info.HasKeyword("巴洛克工作室")) return;

        // 代价：咚!!-1，需至少 1 张可放回的咚
        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克洛克达尔：咚!!-1，使我方最多 1 张领袖或角色本次战斗力量 +2000？");
        if (!use) return;

        // 支付代价：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 标记本回合已用
        me.TurnOnceUsed.Add(key);

        // 效果：我方领袖或角色最多 1 张本次战斗力量 +2000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选最多 1 张我方领袖或角色，本次战斗力量 +2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.AddPowerThisBattle(target, 2000);
    }
}
