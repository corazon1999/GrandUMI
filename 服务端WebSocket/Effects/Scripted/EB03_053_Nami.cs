using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-053 奈美（角色 / 光 / 草帽一伙，cost5 power6000）
/// 【登场时】赋予我方领袖最多 1 张休息状态的咚!!。之后，对方生命卡牌为 3 张或更多的场合，
///   将对方生命区最上方的最多 1 张卡牌加入其持有者的手牌。
/// 【KO时】可以将我方生命区最上方的 1 张卡牌翻至正面朝上：将我方手牌中最多 1 张力量不高于 6000 的角色卡牌登场。
///
/// 实现说明：
///   - 登场段（均为强制，无"可以"）：①赋予领袖 1 张咚（AttachDonFromDeck；引擎 Attached 不分横竖，
///     力量+1000，下个准备阶段解除→回费用区，与"休息赋予咚"力量等价）；②对方生命≥3 时，将对方
///     生命区最上方 1 张加入其（对方）手牌。
///   - 【KO时】段需"翻生命正面"作为成本——引擎无翻面通道（参见 OP08_058 注记），暂未实现；
///     不在本反馈（#33 登场时效果）诉求内。
/// </summary>
public class EB03_053_Nami : IScriptedEffect
{
    public string CardNumber => "EB03-053";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ① 赋予我方领袖最多 1 张休息状态的咚!!（从费用区取休息咚，与汉库珂/阿兰/戈耳工统一走
        //    AttachDonFromCost；原 AttachDonFromDeck 从咚卡组取，解除后咚永久留费用区→玩家咚+1，故改）
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

        // ② 之后：对方生命卡牌≥3 时，将对方生命区最上方 1 张加入其（对方）手牌
        if (opp.LifeArea.Count >= 3)
        {
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Hand.Add(top);
        }

        return Task.CompletedTask;
    }
}
