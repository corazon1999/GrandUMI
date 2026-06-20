using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-106 蒙奇·D·路飞（角色 / 光 / 艾格赫德・四皇・草帽一伙，cost5 power6000）
/// 【我方的回合结束时】可以将我方生命区最上方的 1 张卡牌翻至正面朝上：
///   将我方最多 1 张拥有《艾格赫德》特征的角色转为活跃状态。
/// 【触发】抽取 1 张卡牌，将对方最多 1 张费用不高于 2 的角色 KO。（由触发数据处理，不在本脚本内）
///
/// 实现说明：
///   - 「将生命顶 1 张翻至正面朝上」为可选成本，引擎无生命正面朝上的状态通道，按惯例简化为
///     仅询问是否发动并实现收益（不实际翻面）；reason 在外部记录。
///   - 收益：将我方最多 1 张《艾格赫德》角色转为活跃状态（ActivateCard）。
/// </summary>
public class P_106_Luffy : IScriptedEffect
{
    public string CardNumber => "P-106";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters.Where(c => c.Info.HasKeyword("艾格赫德") && c.IsTapped).ToList();
        if (cands.Count == 0) return;
        if (me.LifeArea.Count == 0) return; // 无生命牌可作为成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【我方回合结束时】：将生命顶 1 张翻至正面朝上，将我方 1 张《艾格赫德》角色转为活跃状态？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张《艾格赫德》角色转为活跃状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ActivateCard(tgt);
        }
    }
}
