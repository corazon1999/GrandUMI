using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-073 夏洛特·玲玲（角色 10 费 12000，四皇 / 大妈海盗团）
/// （被动）我方领袖拥有《大妈海盗团》特征的场合，此角色获得【速攻】效果。
/// 【对方的攻击时】【每回合1次】咚!!-5：宣言任意的费用，并公开对方卡组最上方的 1 张卡牌。
///   公开的卡牌费用与宣言的费用相同的场合，本回合中，我方最多 1 张领袖力量 +2000。
///
/// 实现说明：
///   - 登场时注册条件性持续关键词；我方领袖拥有《大妈海盗团》特征时，自身获得【速攻】。
///   - 成本：咚!!-5（须有 ≥5 张可放回的咚）。每回合 1 次。
///   - "宣言任意费用"用 ChooseOption（0..10）；公开 opp.Deck[0]，比较其卡面原费用。
///   - 命中：我方领袖本回合力量 +2000（文本"最多 1 张领袖"即我方领袖）。
/// </summary>
public class OP11_073_CharlotteLinlin : IScriptedEffect
{
    public string CardNumber => "OP11-073";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId = self.Id;
            int owner = ctx.OwnerIndex;
            ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "速攻",
                Predicate = (state, side, card) =>
                    side == owner
                    && card.Id == selfId
                    && !card.IsEffectsNullified
                    && state.Players[owner].Leader.Info.HasKeyword("大妈海盗团"),
            });
            return;
        }

        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        // 咚!!-5 须有 ≥5 张可放回的咚
        if (me.CostArea.Count < 5) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "玲玲【对方的攻击时】：咚!!-5，宣言费用并公开对方卡组顶 1 张，命中则本回合我方领袖力量 +2000？");
        if (!use) return;

        // 成本：咚!!-5
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 5)) return;
        me.TurnOnceUsed.Add(key);

        // 宣言任意费用
        var costOptions = Enumerable.Range(0, 11).Select(i => i.ToString()).ToList();
        int declared = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "宣言任意的费用", costOptions);

        // 公开对方卡组最上方 1 张
        if (opp.Deck.Count == 0) return;
        var revealed = opp.Deck[0];
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new[] { revealed.Info.Number });
        if (revealed.Info.Cost != declared) return; // 未命中

        // 命中：我方领袖本回合力量 +2000
        AtomicOps.AddPowerThisTurn(me.Leader, 2000);
    }
}
