using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>十月二十日新卡共用的自身废弃成本与舞台登场效果。</summary>
internal static class October20CardHelpers
{
    public static async Task<bool> TrashSelf(EffectContext ctx, string text)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Characters.Contains(ctx.Source)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, text)
            || !me.Characters.Contains(ctx.Source)) return false;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, ctx.Source, ignoreEffectLeaveGuard: true); }
        finally { EffectRuntime.PayingCost = previous; }
        return me.Trash.Contains(ctx.Source);
    }

    public static async Task StageOnPlay(EffectContext ctx)
    {
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!ctx.State.IsGameOver && me.DonDeck.Count > 0
            && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "从咚!!卡组追加1张休息状态的咚？"))
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }

    public static bool CanRestStage(GameState state, int owner, CardInstance source)
        => state.Players[owner].StageCards.Contains(source) && !source.IsTapped
            && AtomicOps.CanRestCard(state, source, owner);
}

/// <summary>睫毛：对方攻击时废弃自身作为成本，为阿拉巴斯坦领袖降低对方一张卡的力量。</summary>
public sealed class OP18_015_Eyelashes : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-015";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnOppAttackDeclare;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => state.CurrentTurnPlayer != owner && state.Players[owner].Characters.Contains(source);
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await October20CardHelpers.TrashSelf(ctx, "将睫毛放置到废弃区，使对方最多1张领袖或角色本回合力量-3000？")) return;
        if (!ctx.State.Players[ctx.OwnerIndex].Leader.Info.HasKeyword("阿拉巴斯坦王国")) return;
        int side = 1 - ctx.OwnerIndex;
        var opponent = ctx.State.Players[side];
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppLeaderOrCharacter",
            "使对方最多1张领袖或角色本回合力量-3000", opponent.Characters.Prepend(opponent.Leader));
        if (picked.Count == 1 && (opponent.Leader == picked[0] || opponent.Characters.Contains(picked[0])))
            AtomicOps.AddPowerThisTurn(picked[0], -3000);
    }
}

/// <summary>路飞：我方回合结束时废弃自身，抽一张后重置领袖。</summary>
public sealed class OP18_036_Luffy : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-036";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnMyTurnEnd;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => state.CurrentTurnPlayer == owner && state.Players[owner].Characters.Contains(source);
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await October20CardHelpers.TrashSelf(ctx, "将路飞放置到废弃区，抽取1张卡牌并将我方领袖转为活跃状态？")) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        if (!ctx.State.IsGameOver) AtomicOps.ActivateCard(ctx.State.Players[ctx.OwnerIndex].Leader);
    }
}

/// <summary>Mr.1：登场时赋予十字公会或巴洛克特征的我方领袖本回合不可阻挡。</summary>
public sealed class OP18_047_Mr1 : IScriptedEffect
{
    public string CardNumber => "OP18-047";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.HasKeyword("十字公会") || card.Info.HasKeywordContaining("巴洛克工作室");
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnLeader",
            "使符合《十字公会》或特征包含《巴洛克工作室》的我方领袖本回合获得不可阻挡", new[] { me.Leader }.Where(Eligible));
        if (picked.Count == 1 && ctx.State.Players[ctx.OwnerIndex].Leader == picked[0] && Eligible(picked[0]))
            AtomicOps.GiveKeyword(picked[0], "不可阻挡", KeywordDuration.ThisTurn);
    }
}

/// <summary>千里阳光号：登场抽牌加咚，主要阶段返还两咚并休息自身以重置低力量草帽角色。</summary>
public sealed class OP18_075_ThousandSunny : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-075";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;
    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
        => !October20CardHelpers.CanRestStage(state, owner, source) ? "需要可转为休息状态的千里·阳光号"
            : state.Players[owner].CostArea.Count(don => don.State is DonState.Active or DonState.Rest or DonState.Attached) < 2
                ? "需要可放回咚!!卡组的2张咚" : null;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField) { await October20CardHelpers.StageOnPlay(ctx); return; }
        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var dons = me.CostArea.Where(don => don.State is DonState.Active or DonState.Rest or DonState.Attached).ToList();
        var answer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReturnOwnDon",
            "选择放回咚!!卡组的2张咚并将阳光号休息，或取消发动", dons.Select(don => don.Id.ToString()).ToList(), 0, 2,
            new Dictionary<string, object?> { ["donChoices"] = AtomicOps.BuildDonPromptChoices(me, dons), ["canCancel"] = true });
        if (answer.Count != 2 || answer.Distinct(StringComparer.Ordinal).Count() != 2) return;
        var selected = answer.Select(id => dons.FirstOrDefault(don => don.Id.ToString() == id)).OfType<DonCard>().ToList();
        if (selected.Count != 2 || !October20CardHelpers.CanRestStage(ctx.State, ctx.OwnerIndex, ctx.Source)
            || selected.Any(don => !me.CostArea.Contains(don) || don.State is not (DonState.Active or DonState.Rest or DonState.Attached))) return;

        // 全部选择复验后按卡面顺序无等待提交；休息失败时恢复尚未广播的咚成本。
        var snapshots = selected.Select(don => (don, index: me.CostArea.IndexOf(don), don.State, don.AttachedToCardId)).ToList();
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try
        {
            foreach (var don in selected)
            {
                me.CostArea.Remove(don);
                don.State = DonState.InDeck;
                don.AttachedToCardId = null;
                me.DonDeck.Add(don);
            }
            if (!AtomicOps.RestCard(ctx.State, ctx.Source, ctx.OwnerIndex))
            {
                foreach (var snapshot in snapshots.OrderBy(item => item.index))
                {
                    me.DonDeck.Remove(snapshot.don);
                    snapshot.don.State = snapshot.State;
                    snapshot.don.AttachedToCardId = snapshot.AttachedToCardId;
                    me.CostArea.Insert(snapshot.index, snapshot.don);
                }
                return;
            }
        }
        finally { EffectRuntime.PayingCost = previous; }
        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
            new Dictionary<string, object?> { ["count"] = 2, ["owner"] = ctx.OwnerIndex });
        bool Eligible(CardInstance card) => card.IsTapped && card.Info.HasKeyword("草帽一伙")
            && ctx.State.CurrentPowerOf(ctx.OwnerIndex, card) <= 7000;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnCharacter",
            "将我方最多1张力量不高于7000的《草帽一伙》角色转为活跃状态", me.Characters.Where(Eligible));
        if (picked.Count == 1 && me.Characters.Contains(picked[0]) && Eligible(picked[0])) AtomicOps.ActivateCard(picked[0]);
    }
}

/// <summary>白木马一号：登场抽牌加咚，主要阶段休息自身以赋予草帽角色速攻角色。</summary>
public sealed class OP18_077_WhiteHobbyHorse : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-077";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;
    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
        => October20CardHelpers.CanRestStage(state, owner, source) ? null : "需要可转为休息状态的白木马1号";
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField) { await October20CardHelpers.StageOnPlay(ctx); return; }
        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将白木马1号转为休息状态，使我方草帽角色本回合获得速攻：角色？")
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        bool paid;
        try { paid = AtomicOps.RestCard(ctx.State, ctx.Source, ctx.OwnerIndex); }
        finally { EffectRuntime.PayingCost = previous; }
        if (!paid) return;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnCharacter",
            "使我方最多1张《草帽一伙》角色本回合获得速攻：角色", me.Characters.Where(card => card.Info.HasKeyword("草帽一伙")));
        if (picked.Count == 1 && me.Characters.Contains(picked[0]) && picked[0].Info.HasKeyword("草帽一伙"))
            AtomicOps.GiveKeyword(picked[0], "速攻：角色", KeywordDuration.ThisTurn);
    }
}
