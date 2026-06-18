using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-083 奥兹（角色）
/// 此角色无法攻击。（卡面含"此角色无法攻击"，由引擎自动禁攻，脚本不处理。）
/// 【启动主要】可以将我方 1 张拥有《恐怖之船海盗团》特征的角色 KO：本回合中，此角色的效果无效。
///
/// 实现说明：
/// - "此角色无法攻击" 是被动自限，引擎按卡面文本自动施加，无需脚本。
/// - 【启动主要】"可以"=可选成本：KO 我方 1 张《恐怖之船海盗团》角色（可为自身以外）。
/// - 收益"本回合此角色效果无效" 用 NullifyEffects(ctx.Source, ThisTurn)，本回合解除"无法攻击"限制。
/// </summary>
public class OP06_083_Oars : IScriptedEffect
{
    public string CardNumber => "OP06-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 候选：我方拥有《恐怖之船海盗团》特征的角色
        var cands = me.Characters.Where(c => c.Info.HasKeyword("恐怖之船海盗团")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "奥兹【启动主要】：将我方 1 张《恐怖之船海盗团》角色 KO，本回合此角色效果无效（可攻击）？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张拥有《恐怖之船海盗团》特征的我方角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, tgt);

        // 收益：本回合此角色效果无效（解除"无法攻击"被动）
        AtomicOps.NullifyEffects(self, KeywordDuration.ThisTurn);
    }
}
