using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-033 弗兰奇（角色 / 风 / 3 费 / 5000 / FILM・草帽一伙）
/// 【KO时】将对方最多 2 张卡牌转为休息状态。
///
/// 实现：AtomicOps.PromptRestOpponentCards(ctx,2)——对方活跃的 领袖/角色/舞台/咚!! 中选最多 2 张休置（可不选）。
/// </summary>
public class OP13_033_Franky : IScriptedEffect
{
    public string CardNumber => "OP13-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        await AtomicOps.PromptRestOpponentCards(ctx, 2);
    }
}
