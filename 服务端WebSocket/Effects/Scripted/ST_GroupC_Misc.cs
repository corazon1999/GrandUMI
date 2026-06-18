using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST11-003 逆光（事件）
/// 【主要】我方领袖为“乌塔”的场合，选择1项：①将对方最多1张费用≤5角色转休息；②KO对方最多1张休息且费用≤5角色。
/// </summary>
public class ST11_003_Reverse : IScriptedEffect
{
    public string CardNumber => "ST11-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.NameIs("乌塔")) return;
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择以下1项",
            new List<string> { "将对方最多1张费用≤5角色转为休息状态", "KO对方最多1张休息且费用≤5的角色" });
        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
            if (cands.Count == 0) return;
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter", "转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0) AtomicOps.RestCard(cands.First(c => c.Id.ToString() == ch[0]));
        }
        else if (opt == 1)
        {
            var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 5).ToList();
            if (cands.Count == 0) return;
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter", "KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0) AtomicOps.KO(ctx.State, oppIdx, cands.First(c => c.Id.ToString() == ch[0]));
        }
    }
}

/// <summary>
/// ST12-006 约撒&强尼（角色）
/// 【咚!!×1】【攻击时】选择1项：①将对方最多1张费用≤2角色转休息；②KO对方最多1张休息且费用≤2角色。
/// </summary>
public class ST12_006_YosakuJohnny : IScriptedEffect
{
    public string CardNumber => "ST12-006";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择以下1项",
            new List<string> { "将对方最多1张费用≤2角色转为休息状态", "KO对方最多1张休息且费用≤2的角色" });
        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
            if (cands.Count == 0) return;
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter", "转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0) AtomicOps.RestCard(cands.First(c => c.Id.ToString() == ch[0]));
        }
        else if (opt == 1)
        {
            var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 2).ToList();
            if (cands.Count == 0) return;
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter", "KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0) AtomicOps.KO(ctx.State, oppIdx, cands.First(c => c.Id.ToString() == ch[0]));
        }
    }
}

/// <summary>
/// ST29-002 撒谎布（角色）
/// 【登场时】/【攻击时】将对方最多1张费用不高于对方生命卡牌张数的角色转为休息状态。（动态费用阈值）
/// </summary>
public class ST29_002_Usopp : IScriptedEffect
{
    public string CardNumber => "ST29-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];
        int threshold = opp.LifeCount;
        var cands = opp.Characters.Where(c => c.Info.Cost <= threshold).ToList();
        if (cands.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多1张费用≤{threshold} 角色转为休息状态", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count > 0) AtomicOps.RestCard(cands.First(c => c.Id.ToString() == ch[0]));
    }
}

/// <summary>
/// ST29-013 罗布·鲁兹（角色）
/// 【触发】将对方最多1张费用不高于双方生命卡牌合计张数的角色KO。（动态费用阈值）
/// </summary>
public class ST29_013_RobLucci : IScriptedEffect
{
    public string CardNumber => "ST29-013";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];
        int threshold = ctx.State.Players[ctx.OwnerIndex].LifeCount + opp.LifeCount;
        var cands = opp.Characters.Where(c => c.Info.Cost <= threshold).ToList();
        if (cands.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"KO 对方最多1张费用≤{threshold} 的角色", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count > 0) AtomicOps.KO(ctx.State, oppIdx, cands.First(c => c.Id.ToString() == ch[0]));
    }
}

/// <summary>
/// ST02-008 打碟人·阿保（角色）
/// 【咚!!×1】【攻击时】将对方最多1张咚!!转为休息状态。（咚为同质资源，直接横置对方1张活跃咚）
/// </summary>
public class ST02_008_Abbot : IScriptedEffect
{
    public string CardNumber => "ST02-008";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var don = opp.CostArea.FirstOrDefault(d => d.State == DonState.Active && d.AttachedToCardId is null);
        if (don is not null) don.State = DonState.Rest;
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST16-002 哥顿（角色）
/// 【阻挡者】【对方的攻击时】可以丢弃我方手牌中任意张数的拥有《音乐》特征的卡牌。每丢弃1张，本次战斗中，我方的1张领袖或角色力量+1000。
/// 实现：选择若干《音乐》手牌丢弃，按张数合计加到所选 1 张领袖或角色（近似“每张各选1张”）。
/// </summary>
public class ST16_002_Gordon : IScriptedEffect
{
    public string CardNumber => "ST16-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var musicCards = me.Hand.Where(c => c.Info.HasKeyword("音乐")).ToList();
        if (musicCards.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃任意张数《音乐》卡牌，每张使我方1张领袖或角色本次战斗+1000",
            musicCards.Select(c => c.Id.ToString()).ToList(), 0, musicCards.Count);
        if (chosen.Count == 0) return;
        foreach (var cid in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }
        int bonus = chosen.Count * 1000;
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            $"本次战斗使我方1张领袖或角色力量+{bonus}", targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(targets.First(c => c.Id.ToString() == pick[0]), bonus);
    }
}
