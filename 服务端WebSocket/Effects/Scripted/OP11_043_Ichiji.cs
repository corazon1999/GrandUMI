using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-043 温思默克·伊智（角色 / 温思默克家·GERMA 66）
/// 【阻挡者】（纯关键词，由引擎处理）
/// 【对方的攻击时】【每回合1次】我方角色仅为拥有的特征中包含〈GERMA〉的角色的场合，可以发动。
///   本次战斗中，我方最多 1 张领袖或角色力量 +1000。之后，将我方卡组最上方的 2 张卡牌放置到废弃区。
///
/// 实现说明：
///   - 发动条件：我方场上所有角色都拥有〈GERMA〉特征（无角色时不满足）。用 C# 直接判定。
///   - 可选效果，先 ConfirmOptional；发动后选最多 1 张领袖/角色本次战斗 +1000，再废弃卡组顶 2 张。
///   - 每回合 1 次，用 TurnOnceUsed 约束。
/// </summary>
public class OP11_043_Ichiji : IScriptedEffect
{
    public string CardNumber => "OP11-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = "OP11-043-oppatk" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 发动条件：我方角色仅为含〈GERMA〉特征的角色（须至少有 1 张角色，且全部含 GERMA）
        if (me.Characters.Count == 0) return;
        if (!me.Characters.All(c => c.Info.HasKeyword("GERMA"))) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "温思默克·伊智【对方的攻击时】：我方最多 1 张领袖或角色本次战斗 +1000，之后废弃卡组顶 2 张？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 效果 1：我方最多 1 张领袖或角色本次战斗力量 +1000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选最多 1 张我方领袖或角色，本次战斗力量 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (target is not null) AtomicOps.AddPowerThisBattle(target, 1000);
        }

        // 效果 2：将我方卡组最上方的 2 张卡牌放置到废弃区
        AtomicOps.MillTop(me, 2);
    }
}
