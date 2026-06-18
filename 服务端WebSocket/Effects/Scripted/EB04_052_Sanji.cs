using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-052 山智（角色 / 光 / 艾格赫德・草帽一伙）
/// 【攻击时】本回合中，此角色原本的力量变为与对方领袖力量相同。
/// 【KO时】我方生命卡牌不多于 2 张的场合，将我方手牌中最多 1 张力量不高于 6000 的黄色角色卡牌登场。
///
/// 实现说明：
///   - 攻击时："原本力量变为与对方领袖力量相同"——用 SetPowerThisTurn 把本回合力量设为对方领袖
///     当前力量（含持续修正与贴咚），与 OP06-009 舒莱亚同写法，donAttached=0 让贴咚另叠加。
///   - KO时：仅当我方生命≤2 张才发动；候选为手牌中力量≤6000 的黄色（光）角色，最多登场 1 张。
/// </summary>
public class EB04_052_Sanji : IScriptedEffect
{
    public string CardNumber => "EB04-052";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnAttackDeclare || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            if (opp.Leader is null) return;
            int targetPower = ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, opp.Leader);
            bool myTurn = ctx.State.CurrentTurnPlayer == ctx.OwnerIndex;
            AtomicOps.SetPowerThisTurn(self, targetPower, 0, myTurn);
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            if (me.LifeCount > 2) return; // 生命须不多于 2 张

            var cands = me.Hand.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Power <= 6000 &&
                c.Info.ColorList.Contains("黄")
            ).ToList();
            if (cands.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场最多 1 张力量≤6000 的黄色角色",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
            return;
        }
    }
}
