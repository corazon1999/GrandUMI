using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-116 燃烧炮（事件 / 光 2 费，空岛/山迪亚战士）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +4000。
///   之后，可以将我方生命区最上方或最下方的 1 张卡牌加入手牌。那样做的场合，
///   将我方手牌中最多 1 张拥有《山迪亚战士》特征的卡牌正面朝上加入生命区最上方。
///
/// 实现说明 / 简化点：
///   - "本次战斗中 +4000" 用 AddPowerThisBattle。
///   - 生命牌私有：手动 me.LifeArea 取出加入手牌；放入生命区用 me.LifeArea.Insert(0,...)。
///   - 生命牌取顶/底由玩家二选一；取出后才询问是否将《山迪亚战士》入生命区顶。
/// </summary>
public class OP08_116_BurningCannon : IScriptedEffect
{
    public string CardNumber => "OP08-116";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // ① 本次战斗中我方最多 1 张领袖或角色力量 +4000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，选择最多 1 张我方领袖或角色力量 +4000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(tgt, 4000);
        }

        // ② 可以将生命区最上方或最下方的 1 张卡牌加入手牌
        if (me.LifeArea.Count == 0) return;

        bool takeLife = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "是否将我方生命区最上方或最下方的 1 张卡牌加入手牌？");
        if (!takeLife) return;

        int idx = 0; // 默认取最上方
        if (me.LifeArea.Count >= 2)
        {
            int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "选择从生命区取出的位置", new[] { "最上方", "最下方" });
            idx = opt == 1 ? me.LifeArea.Count - 1 : 0;
        }
        var taken = me.LifeArea[idx];
        me.LifeArea.RemoveAt(idx);
        me.Hand.Add(taken);

        // ③ 那样做的场合：将手牌中最多 1 张《山迪亚战士》正面朝上加入生命区最上方
        var sandia = me.Hand.Where(c => c.Info.HasKeyword("山迪亚战士")).ToList();
        if (sandia.Count == 0) return;

        var toLifePick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将最多 1 张《山迪亚战士》加入生命区最上方",
            sandia.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (toLifePick.Count > 0)
        {
            var toLife = sandia.First(c => c.Id.ToString() == toLifePick[0]);
            me.Hand.Remove(toLife);
            me.LifeArea.Insert(0, toLife);
        }
    }
}
