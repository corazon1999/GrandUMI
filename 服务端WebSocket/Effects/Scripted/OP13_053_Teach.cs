using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-053 马歇尔·D·提奇（角色）
/// 【攻击时】可以将我方 1 张拥有的特征中包含〈白胡子海盗团〉的角色放置到废弃区：
///   抽取 1 张卡牌，本回合中，此角色获得【流放】效果。
///
/// 说明：
///   - 可选效果（“可以”），先 ConfirmOptional 询问；成本为“将我方 1 张含〈白胡子海盗团〉角色放置废弃区”。
///   - 放置废弃区使用 AtomicOps.KO（走同步 KOCard，不触发“被 KO 时”效果），与既有“放置到废弃区”范式一致。
///   - “此角色”指本攻击角色（提奇自身，ctx.Source）获得【流放】；若玩家选择把提奇自身丢弃则该 buff 失去意义但抽牌仍生效。
/// </summary>
public class OP13_053_Teach : IScriptedEffect
{
    public string CardNumber => "OP13-053";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 成本候选：我方场上含〈白胡子海盗团〉特征的角色
        var candidates = me.Characters
            .Where(c => c.Info.HasKeyword("白胡子海盗团"))
            .ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "提奇【攻击时】：将我方 1 张〈白胡子海盗团〉角色放置废弃区，抽 1 张，本回合此角色获得【流放】？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacterWhitebeard",
            "选 1 张〈白胡子海盗团〉角色放置到废弃区",
            candidates.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var sacrifice = candidates.First(c => c.Id.ToString() == chosen[0]);

        // 支付成本：放置到废弃区（同步 KOCard，不触发“被 KO 时”效果）
        AtomicOps.KO(s, ctx.OwnerIndex, sacrifice);

        // 效果：抽 1 张
        AtomicOps.Draw(s, ctx.OwnerIndex, 1);

        // 本回合此角色（提奇自身）获得【流放】
        AtomicOps.GiveKeyword(ctx.Source, "流放", KeywordDuration.ThisTurn);
    }
}
