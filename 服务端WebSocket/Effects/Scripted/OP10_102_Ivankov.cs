using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-102 安普里奥·伊万科夫（角色）
/// 【启动主要】【每回合1次】本回合中，我方最多 3 张拥有《革命军》特征的角色力量 +1000。
///   之后，将我方生命区最上方的 1 张卡牌加入手牌。
///
/// 实现说明：
///   - "最多 3 张分别 +1000" 用 ChooseCards(min=0,max=3) 选取后逐张 AddPowerThisTurn(+1000)。
///   - 生命顶加入手牌：直接将 LifeArea[0] 移入手牌。
/// </summary>
public class OP10_102_Ivankov : IScriptedEffect
{
    public string CardNumber => "OP10-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 我方最多 3 张《革命军》角色各 +1000
        var revos = me.Characters.Where(c => c.Info.HasKeyword("革命军")).ToList();
        if (revos.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择最多 3 张《革命军》角色，本回合各力量 +1000",
                revos.Select(c => c.Id.ToString()).ToList(), 0, 3);
            foreach (var id in chosen)
            {
                var c = revos.First(x => x.Id.ToString() == id);
                AtomicOps.AddPowerThisTurn(c, 1000);
            }
        }

        // 之后：将生命区最上方的 1 张卡牌加入手牌
        if (me.LifeArea.Count > 0)
        {
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Hand.Add(top);
        }
    }
}
