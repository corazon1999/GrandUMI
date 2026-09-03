using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Hex;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// 规则修订版 12 的 26 个扩展海克斯，以及 30 号海克斯重制的定向回归。
/// 每个独立机制至少在本文件中有一个行为断言；涉及选择、随机与永久状态的机制另测原子性和重放投影。
/// </summary>
public sealed class HexExpansionEffectsTests
{
    [Fact]
    public void 新增海克斯目录名称_严格匹配最终文案()
    {
        Assert.Equal("death or live", HexCatalog.Get(57).Name);
        Assert.Equal("潘多拉的魔盒", HexCatalog.Get(80).Name);
    }

    [Fact]
    public async Task death_or_live_按增加后的当前生命门槛强制KO合法目标()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 57);
        state.Players[0].LifeArea.AddRange([
            Card("HEX-LIFE-1", CardKind.Character),
            Card("HEX-LIFE-2", CardKind.Character),
            Card("HEX-LIFE-3", CardKind.Character),
        ]);
        var below = Card("HEX-COST-2", CardKind.Character, cost: 2);
        var eligible = Card("HEX-COST-3", CardKind.Character, cost: 3);
        state.Players[1].Characters.AddRange([below, eligible]);

        var resolving = HexRules.OnLifeAddedAsync(engine, 0, 1, allowCriticalHeal: false);
        var prompt = await WaitForPrompt(engine, "HexDeathOrLifeKO");

        Assert.Equal([eligible.Id.ToString()], prompt.ValidChoices);
        Respond(engine, prompt, eligible.Id.ToString());
        await resolving;
        await engine.WaitSettledAsync();

        Assert.Contains(below, state.Players[1].Characters);
        Assert.DoesNotContain(eligible, state.Players[1].Characters);
        Assert.Contains(eligible, state.Players[1].Trash);
    }

    [Fact]
    public void 艾尔巴夫_只强化拥有阻挡者的己方角色()
    {
        var state = HexState();
        var blocker = Card("HEX-BLOCKER", CardKind.Character, power: 5000, abilities: ["阻挡者"]);
        var ordinary = Card("HEX-ORDINARY", CardKind.Character, power: 5000);
        state.Players[0].Characters.AddRange([blocker, ordinary]);
        OwnOnly(state, 0, 58);

        Assert.Equal(2000, HexRules.PowerBonus(state, 0, blocker));
        Assert.Equal(0, HexRules.PowerBonus(state, 0, ordinary));
    }

    [Fact]
    public async Task 鬼岛决战_每次己方咚返回牌组可选择一个敌方角色减力()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 59);
        var target = Card("HEX-ONIGASHIMA-TARGET", CardKind.Character, power: 5000);
        state.Players[1].Characters.Add(target);

        var resolving = HexRules.OnGameEventAsync(
            state,
            EffectTrigger.OnDonReturnedToDeck,
            engine.Prompts,
            new Dictionary<string, object?> { ["owner"] = 0, ["count"] = 2 });
        var prompt = await WaitForPrompt(engine, "HexOnigashimaTarget");
        Respond(engine, prompt, target.Id.ToString());
        await resolving;

        Assert.Equal(-1000, target.PowerModThisTurn);
    }

    [Fact]
    public async Task 冰冻果实与火之意志_只响应效果弃牌且前者每回合一次()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 60, 73);
        state.Players[0].Deck.AddRange([
            Card("HEX-DRAW-1", CardKind.Character),
            Card("HEX-DRAW-2", CardKind.Character),
            Card("HEX-DRAW-GUARD", CardKind.Character),
        ]);

        await NotifyDiscard(engine, 0, CardKind.Event);
        await NotifyDiscard(engine, 0, CardKind.Event);
        await NotifyDiscard(engine, 0, CardKind.Character);

        Assert.Single(state.Players[0].Hand);
        Assert.True(state.HexState.Runtime[0].IceFruitUsedThisTurn);
        Assert.Equal(4000, state.Players[0].Leader.PowerModThisTurn);
    }

    [Fact]
    public void 给我一个面子与公主链接_领袖禁攻和覆盖优先级由服务端统一裁决()
    {
        var state = HexState();
        var attacker = state.Players[0].Leader;

        OwnOnly(state, 1, 61);
        Assert.False(ActionValidator.CanAttack(state, 0, attacker.Id, true, null).Ok);

        OwnOnly(state, 0, 71);
        Assert.True(ActionValidator.CanAttack(state, 0, attacker.Id, true, null).Ok);
        Assert.Equal(2000, HexRules.PowerBonus(state, 0, attacker));

        OwnOnly(state, 0, 34);
        OwnOnly(state, 1);
        Assert.False(ActionValidator.CanAttack(state, 0, attacker.Id, true, null).Ok);
        OwnOnly(state, 0, 34, 71);
        Assert.True(ActionValidator.CanAttack(state, 0, attacker.Id, true, null).Ok);

        attacker.Restrictions.Add(new CardRestriction
        {
            Kind = RestrictionKind.CannotAttack,
            Duration = KeywordDuration.ThisTurn,
        });
        Assert.True(ActionValidator.CanAttack(state, 0, attacker.Id, true, null).Ok);
    }

    [Fact]
    public void 四皇_角色达到当前力量阈值时动态获得速攻()
    {
        var state = HexState();
        var character = Card("HEX-EMPEROR", CardKind.Character, power: 11000);
        state.Players[0].Characters.Add(character);
        OwnOnly(state, 0, 62);

        Assert.False(ActionValidator.HasKeyword(state, character, "速攻"));
        character.PowerModThisTurn = 1000;
        Assert.True(ActionValidator.HasKeyword(state, character, "速攻"));
        character.PowerModThisTurn = 999;
        Assert.False(ActionValidator.HasKeyword(state, character, "速攻"));
    }

    [Fact]
    public void 死者苏生与越狱行动_登场来源标记赋予关键词且离场后清除()
    {
        var state = HexState();
        var fromTrash = Card("HEX-RESURRECT", CardKind.Character);
        var fromHand = Card("HEX-JAILBREAK", CardKind.Character);
        state.Players[0].Characters.AddRange([fromTrash, fromHand]);

        OwnOnly(state, 0, 63, 66);
        HexRules.OnCharacterEntered(state, 0, fromTrash, "trash", byEffect: true);
        HexRules.OnCharacterEntered(state, 0, fromHand, "hand", byEffect: true);
        Assert.True(ActionValidator.HasKeyword(state, fromTrash, "流放"));
        Assert.True(ActionValidator.HasKeyword(state, fromHand, "速攻"));

        state.Players[0].Characters.Remove(fromTrash);
        state.Players[0].Characters.Remove(fromHand);
        Assert.False(fromTrash.HexEnteredFromTrash);
        Assert.False(fromHand.HexEnteredFromHandByEffect);
    }

    [Fact]
    public void 绝对防御_仅清除本次实际发动新增的对方攻击时限次标记()
    {
        var state = HexState();
        var player = state.Players[0];
        var source = Card("HEX-ABSOLUTE-DEFENSE", CardKind.Character);
        player.Characters.Add(source);
        OwnOnly(state, 0, 64);
        player.TurnOnceUsed.Add("preexisting-player-key");
        source.OncePerTurnUsedKeys.Add("preexisting-card-key");
        var playerBefore = player.TurnOnceUsed.ToHashSet(StringComparer.Ordinal);
        var cardBefore = source.OncePerTurnUsedKeys.ToHashSet(StringComparer.Ordinal);

        player.TurnOnceUsed.Add("new-player-key");
        source.OncePerTurnUsedKeys.Add("new-card-key");
        player.OncePerTurnEffectUsedCardIds.Add(source.Id);
        HexRules.ApplyInventorSecondUse(
            state,
            0,
            source,
            EffectTrigger.OnOppAttackDeclare,
            actuallyActivated: true,
            playerBefore,
            cardBefore);

        Assert.Contains("preexisting-player-key", player.TurnOnceUsed);
        Assert.Contains("preexisting-card-key", source.OncePerTurnUsedKeys);
        Assert.DoesNotContain("new-player-key", player.TurnOnceUsed);
        Assert.DoesNotContain("new-card-key", source.OncePerTurnUsedKeys);
        Assert.DoesNotContain(source.Id, player.OncePerTurnEffectUsedCardIds);

        player.TurnOnceUsed.Add("declined-key");
        HexRules.ApplyInventorSecondUse(
            state,
            0,
            source,
            EffectTrigger.OnOppAttackDeclare,
            actuallyActivated: false,
            playerBefore,
            cardBefore);
        Assert.Contains("declined-key", player.TurnOnceUsed);
    }

    [Fact]
    public void 三大将_同回合第三个高费角色登场后只赋予本批高费角色双关键词()
    {
        var state = HexState();
        OwnOnly(state, 0, 65);
        var low = Card("HEX-ADMIRAL-LOW", CardKind.Character, cost: 4);
        state.Players[0].Characters.Add(low);
        HexRules.OnCharacterEntered(state, 0, low, "hand", byEffect: false);

        var admirals = Enumerable.Range(1, 3)
            .Select(index => Card($"HEX-ADMIRAL-{index}", CardKind.Character, cost: 5))
            .ToArray();
        foreach (var admiral in admirals)
        {
            state.Players[0].Characters.Add(admiral);
            HexRules.OnCharacterEntered(state, 0, admiral, "hand", byEffect: false);
        }

        Assert.Equal(3, state.HexState.Runtime[0].HighCostCharacterEntriesThisTurn);
        Assert.All(admirals, admiral =>
        {
            Assert.True(ActionValidator.HasKeyword(state, admiral, "阻挡者"));
            Assert.True(ActionValidator.HasKeyword(state, admiral, "登场回合可攻击角色"));
        });
        Assert.False(ActionValidator.HasKeyword(state, low, "阻挡者"));
        Assert.False(ActionValidator.HasKeyword(state, low, "登场回合可攻击角色"));
    }

    [Fact]
    public void 清一色与进攻即防御_共享特征加力且结束时按原本力量转活跃()
    {
        var state = HexState();
        var first = Card("HEX-MONO-1", CardKind.Character, power: 8000, keywords: ["海军"]);
        var second = Card("HEX-MONO-2", CardKind.Character, power: 7000, keywords: ["海军", "剑士"]);
        state.Players[0].Characters.AddRange([first, second]);
        OwnOnly(state, 0, 67, 68);

        Assert.Equal(1000, HexRules.PowerBonus(state, 0, first));
        Assert.Equal(1000, HexRules.PowerBonus(state, 0, second));
        var outsider = Card("HEX-MONO-3", CardKind.Character, power: 9000, keywords: ["革命军"]);
        state.Players[0].Characters.Add(outsider);
        Assert.Equal(0, HexRules.PowerBonus(state, 0, first));

        first.IsTapped = true;
        second.IsTapped = true;
        outsider.IsTapped = true;
        second.PowerModThisTurn = 5000;
        HexRules.OnTurnEnding(state, 0);
        Assert.False(first.IsTapped);
        Assert.True(second.IsTapped);
        Assert.False(outsider.IsTapped);
    }

    [Fact]
    public async Task 仰卧起坐_可选放回生命顶部且同回合只成功一次()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 69);
        var selected = Card("HEX-SIT-UP-1", CardKind.Character);
        var untouched = Card("HEX-SIT-UP-2", CardKind.Character);
        state.Players[0].Hand.AddRange([selected, untouched]);

        var declined = HexRules.OnLifeMovedToHandByEffectAsync(engine, 0);
        var declinePrompt = await WaitForPrompt(engine, "HexSitUpLife");
        Assert.Equal(0, declinePrompt.MinChoose);
        Assert.Equal(1, declinePrompt.MaxChoose);
        Respond(engine, declinePrompt);
        await declined;
        Assert.False(state.HexState.Runtime[0].SitUpUsedThisTurn);
        Assert.Equal(2, state.Players[0].Hand.Count);

        var resolving = HexRules.OnLifeMovedToHandByEffectAsync(engine, 0);
        var prompt = await WaitForPrompt(engine, "HexSitUpLife");
        Respond(engine, prompt, selected.Id.ToString());
        await resolving;

        Assert.Same(selected, Assert.Single(state.Players[0].LifeArea));
        Assert.Same(untouched, Assert.Single(state.Players[0].Hand));
        Assert.True(state.HexState.Runtime[0].SitUpUsedThisTurn);

        var later = Card("HEX-SIT-UP-3", CardKind.Character);
        state.Players[0].Hand.Add(later);
        await HexRules.OnLifeMovedToHandByEffectAsync(engine, 0);
        Assert.Null(state.PendingPrompt);
        Assert.Contains(later, state.Players[0].Hand);
    }

    [Fact]
    public async Task 屠宰场_失败不消费且成功后同回合权威拒绝并在下回合恢复()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 70);
        var character = Card("HEX-SLAUGHTERHOUSE", CardKind.Character);
        var emptyCharacter = Card("HEX-SLAUGHTERHOUSE-EMPTY", CardKind.Character);
        state.Players[0].Characters.AddRange([character, emptyCharacter]);
        state.Players[0].CostArea.AddRange([
            new DonCard { State = DonState.Attached, AttachedToCardId = character.Id },
            new DonCard { State = DonState.Attached, AttachedToCardId = character.Id },
            new DonCard { State = DonState.Rest },
        ]);

        Assert.False(engine.HandleAction(0, "DetachAllDon", Json(new
        {
            characterId = emptyCharacter.Id.ToString(),
        })));
        Assert.False(state.HexState.Runtime[0].SlaughterhouseUsedThisTurn);

        var before = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.True(before.GetProperty("my").GetProperty("fieldCards")[0]
            .GetProperty("canDetachAllDon").GetBoolean());
        Assert.True(engine.HandleAction(0, "DetachAllDon", Json(new
        {
            characterId = character.Id.ToString(),
        })));
        await engine.WaitSettledAsync();

        Assert.Equal(2, state.Players[0].CostArea.Count(don =>
            don.State == DonState.Active && don.AttachedToCardId is null));
        Assert.True(state.HexState.Runtime[0].SlaughterhouseUsedThisTurn);

        character.IsTapped = true;
        var nextDon = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = character.Id,
        };
        state.Players[0].CostArea.Add(nextDon);
        Assert.False(engine.HandleAction(0, "DetachAllDon", Json(new
        {
            characterId = character.Id.ToString(),
        })));
        Assert.Equal(DonState.Attached, nextDon.State);
        Assert.Equal(character.Id, nextDon.AttachedToCardId);

        var privateRuntime = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state))
            .GetProperty("hexState").GetProperty("runtime")[0];
        var checkpointRuntime = DeterministicReplayCheckpointProvider.BuildFullState(state)
            .GetProperty("hexState").GetProperty("runtime")[0];
        Assert.True(privateRuntime.GetProperty("SlaughterhouseUsedThisTurn").GetBoolean());
        Assert.True(checkpointRuntime.GetProperty("SlaughterhouseUsedThisTurn").GetBoolean());

        HexRules.OnTurnStarted(state, 0);
        Assert.False(state.HexState.Runtime[0].SlaughterhouseUsedThisTurn);
        Assert.True(engine.HandleAction(0, "DetachAllDon", Json(new
        {
            characterId = character.Id.ToString(),
        })));
        Assert.Equal(2, state.Players[0].CostArea.Count(don => don.State == DonState.Rest));
    }

    [Fact]
    public void 屠宰场_规则修订版十二仍允许同回合多次成功发动()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        HexRules.SetRulesRevisionForReplay(state, HexRules.ExpansionRulesRevision);
        OwnOnly(state, 0, 70);
        var character = Card("HEX-SLAUGHTERHOUSE-LEGACY", CardKind.Character);
        state.Players[0].Characters.Add(character);

        void Attach() => state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = character.Id,
        });

        Attach();
        Assert.Equal(1, HexRules.DetachAllDon(state, 0, character.Id));
        Attach();
        Assert.Equal(1, HexRules.DetachAllDon(state, 0, character.Id));
        Assert.False(state.HexState.Runtime[0].SlaughterhouseUsedThisTurn);
    }

    [Fact]
    public async Task 无尽虚空_精确十张有序原子回填并在废弃区为空时败北()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 72);
        var trash = Enumerable.Range(0, 12)
            .Select(index => Card($"HEX-VOID-{index:00}", CardKind.Character))
            .ToArray();
        state.Players[0].Trash.AddRange(trash);

        var drawing = TurnEngine.DrawCardAsync(state, 0, 3, engine.Prompts);
        var prompt = await WaitForPrompt(engine, "HexEndlessVoidOrder");
        Assert.Equal(10, prompt.MinChoose);
        Assert.Equal(10, prompt.MaxChoose);
        Assert.Equal(12, prompt.ValidChoices.Count);
        Assert.True(state.HexState.Runtime[0].VoidRefillResolving);

        var checkpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        Assert.True(checkpoint.GetProperty("hexState").GetProperty("runtime")[0]
            .GetProperty("VoidRefillResolving").GetBoolean());
        Assert.Equal("HexEndlessVoidOrder", checkpoint.GetProperty("pendingPrompt")
            .GetProperty("Kind").GetString());

        var duplicate = Enumerable.Repeat(prompt.ValidChoices[0], 10).ToArray();
        Assert.False(engine.HandleAction(0, "PromptResponse", Json(new
        {
            promptId = prompt.PromptId,
            chosen = duplicate,
        })));
        Assert.Equal(12, state.Players[0].Trash.Count);
        Assert.Empty(state.Players[0].Deck);
        Assert.Equal(prompt.PromptId, state.PendingPrompt?.PromptId);

        var ordered = prompt.ValidChoices.Take(10).Reverse().ToArray();
        Respond(engine, prompt, ordered);
        Assert.Equal(3, await drawing);
        await engine.WaitSettledAsync();

        Assert.Equal(ordered.Take(3), state.Players[0].Hand.Select(card => card.Id.ToString()));
        Assert.Equal(ordered.Skip(3), state.Players[0].Deck.Select(card => card.Id.ToString()));
        Assert.Equal(2, state.Players[0].Trash.Count);
        Assert.False(state.HexState.Runtime[0].VoidRefillResolving);

        var emptyEngine = CreateEngine(seed: 20260902);
        ClearZones(emptyEngine.State);
        OwnOnly(emptyEngine.State, 0, 72);
        DeckOutRules.Arm(emptyEngine.State);
        Assert.Equal(0, await TurnEngine.DrawCardAsync(emptyEngine.State, 0, 1, emptyEngine.Prompts));
        Assert.True(emptyEngine.State.IsGameOver);
        Assert.Equal(1, emptyEngine.State.WinnerIndex);
    }

    [Fact]
    public async Task 沙沙果实_只在己方回合己方角色KO时抽一张()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 74);
        state.Players[0].Deck.AddRange([
            Card("HEX-SAND-DRAW", CardKind.Character),
            Card("HEX-SAND-GUARD", CardKind.Character),
        ]);

        await HexRules.OnCharacterKoAsync(engine, 0, "effect", null, actingSide: 1);
        Assert.Single(state.Players[0].Hand);
        state.CurrentTurnPlayer = 1;
        await HexRules.OnCharacterKoAsync(engine, 0, "effect", null, actingSide: 1);
        Assert.Single(state.Players[0].Hand);
    }

    [Fact]
    public void 线线果实_对方首攻有休息角色时只能选择休息角色()
    {
        var state = HexState();
        OwnOnly(state, 1, 75);
        var rested = Card("HEX-STRING-RESTED", CardKind.Character);
        rested.IsTapped = true;
        state.Players[1].Characters.Add(rested);

        Assert.False(ActionValidator.CanAttack(state, 0, state.Players[0].Leader.Id, true, null).Ok);
        Assert.True(ActionValidator.CanAttack(state, 0, state.Players[0].Leader.Id, false, rested.Id).Ok);
        state.HexState.Runtime[0].AttacksDeclaredThisTurn = 1;
        Assert.True(ActionValidator.CanAttack(state, 0, state.Players[0].Leader.Id, true, null).Ok);
    }

    [Fact]
    public async Task 鱼人空手道_新修订每次合格角色攻击都抽牌且旧修订仍每回合一次()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 76);
        var attacker = Card("HEX-FISHMAN", CardKind.Character, power: 5000);
        state.Players[0].Characters.Add(attacker);
        state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = attacker.Id,
        });
        state.Players[0].Deck.AddRange([
            Card("HEX-FISH-DRAW", CardKind.Character),
            Card("HEX-FISH-GUARD", CardKind.Character),
        ]);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            AttackerCardId = attacker.Id,
            TargetIsLeader = true,
            DefenderPlayerIndex = 1,
        };

        await HexRules.OnAttackDeclaredAsync(engine, 0);
        await HexRules.OnAttackDeclaredAsync(engine, 0);

        Assert.Equal(2, state.Players[0].Hand.Count);
        Assert.False(state.HexState.Runtime[0].FishmanKarateUsedThisTurn);

        var legacyEngine = CreateEngine();
        var legacy = legacyEngine.State;
        ClearZones(legacy);
        HexRules.SetRulesRevisionForReplay(legacy, HexRules.ExpansionRulesRevision);
        OwnOnly(legacy, 0, 76);
        var legacyAttacker = Card("HEX-FISHMAN-LEGACY", CardKind.Character, power: 5000);
        legacy.Players[0].Characters.Add(legacyAttacker);
        legacy.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = legacyAttacker.Id,
        });
        legacy.Players[0].Deck.AddRange([
            Card("HEX-FISH-LEGACY-DRAW", CardKind.Character),
            Card("HEX-FISH-LEGACY-GUARD", CardKind.Character),
        ]);
        legacy.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            AttackerCardId = legacyAttacker.Id,
            TargetIsLeader = true,
            DefenderPlayerIndex = 1,
        };

        await HexRules.OnAttackDeclaredAsync(legacyEngine, 0);
        await HexRules.OnAttackDeclaredAsync(legacyEngine, 0);

        Assert.Single(legacy.Players[0].Hand);
        Assert.True(legacy.HexState.Runtime[0].FishmanKarateUsedThisTurn);
    }

    [Fact]
    public async Task 听说你用剑了_领袖启动主要实际结算后可转活跃最多两张未附着咚()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 77);
        var first = new DonCard { State = DonState.Rest };
        var second = new DonCard { State = DonState.Rest };
        var attached = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = state.Players[0].Leader.Id,
        };
        state.Players[0].CostArea.AddRange([first, second, attached]);

        var resolving = HexRules.AfterEffectResolvedAsync(
            state,
            0,
            state.Players[0].Leader,
            EffectTrigger.ActivatedMain,
            engine.Prompts,
            alreadyCopied: false,
            actuallyActivated: true);
        var prompt = await WaitForPrompt(engine, "HexSwordDonRefresh");
        Assert.Equal(0, prompt.MinChoose);
        Assert.Equal(2, prompt.MaxChoose);
        Assert.DoesNotContain(attached.Id.ToString(), prompt.ValidChoices);
        Respond(engine, prompt, first.Id.ToString(), second.Id.ToString());
        await resolving;

        Assert.Equal(DonState.Active, first.State);
        Assert.Equal(DonState.Active, second.State);
        Assert.Equal(DonState.Attached, attached.State);
    }

    [Fact]
    public void 三号船坞重制_当前修订舞台效果仅额外复制一次而旧修订保留双槽()
    {
        var state = HexState();
        var stage = Card("HEX-DOCK-STAGE", CardKind.Stage);
        state.Players[0].StageCard = stage;
        OwnOnly(state, 0, 30);

        Assert.False(HexRules.HasLegacyDockSlots(state, 0));
        Assert.True(HexRules.ShouldCopyEffect(
            state, 0, stage, EffectTrigger.ActivatedMain, alreadyCopied: false));
        Assert.False(HexRules.ShouldCopyEffect(
            state, 0, stage, EffectTrigger.ActivatedMain, alreadyCopied: true));
        Assert.True(HexRules.ShouldCopyEffect(
            state, 0, stage, EffectTrigger.OnEnterField, alreadyCopied: false));
        Assert.Equal(0, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.True(HexRules.ShouldCopyEffect(
            state, 0, stage, EffectTrigger.OnKO, alreadyCopied: false));
        Assert.False(state.HexState.Runtime[0].FirstKoEffectCopiedThisTurn);

        state.HexState.Owned[0].Add(16);
        Assert.True(HexRules.ShouldCopyEffect(
            state, 0, stage, EffectTrigger.OnEnterField, alreadyCopied: false));
        Assert.Equal(1, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        var character = Card("HEX-DOCK-CHARACTER", CardKind.Character);
        Assert.True(HexRules.ShouldCopyEffect(
            state, 0, character, EffectTrigger.OnEnterField, alreadyCopied: false));
        Assert.Equal(2, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);

        state.HexState.RulesRevision = HexRules.CatalogConfigurationRulesRevision;
        Assert.True(HexRules.HasLegacyDockSlots(state, 0));
        Assert.False(HexRules.CanCopyEffect(
            state, 0, stage, EffectTrigger.ActivatedMain, alreadyCopied: false));
    }

    [Fact]
    public void 革命军与无果实能力者_场上费用增加且原本力量重算后继续叠加临时效果()
    {
        var state = HexState();
        var character = Card(
            "HEX-NON-FRUIT",
            CardKind.Character,
            power: 1000,
            cost: 5,
            abilities: ["阻挡者"]);
        character.CostModThisTurn = -2;
        character.EntityCostModPersistent = -1;
        state.Players[0].Characters.Add(character);
        OwnOnly(state, 0, 78);
        Assert.Equal(10, state.CurrentCostOf(0, character));

        OwnOnly(state, 0, 82);
        character.PowerModThisTurn = 500;
        character.PowerModPersistent = 200;
        state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = character.Id,
        });

        Assert.Equal(7000, state.OriginalPowerOf(0, character));
        Assert.Equal(8700, state.CurrentPowerOf(0, character));
        Assert.True(ActionValidator.HasKeyword(state, character, "阻挡者"));
    }

    [Fact]
    public async Task 牙仙子_费用区无上限且每个领袖伤害事件生成一张咚()
    {
        var engine = CreateEngine();
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 79);

        Assert.Equal(int.MaxValue, state.MaxDonInCostAreaFor(0));
        await HexRules.OnLeaderDamagedAsync(engine, defender: 1, damage: 2, attacker: null);
        await HexRules.OnLeaderDamagedAsync(engine, defender: 1, damage: 1, attacker: state.Players[0].Leader);
        Assert.Equal(2, state.Players[0].DonDeck.Count);

        OwnOnly(state, 0);
        Assert.Equal(TurnEngine.MaxDonInCostArea, state.MaxDonInCostAreaFor(0));
        OwnOnly(state, 0, 52);
        Assert.Equal(12, state.MaxDonInCostAreaFor(0));
    }

    [Fact]
    public async Task 潘多拉的魔盒_先规划后一次替换且相同种子结果确定无重复无质变()
    {
        static void Prepare(GameState state)
        {
            state.HexState.Owned[0].Clear();
            state.HexState.Owned[0].AddRange([57, 58, 80]);
            state.HexState.GrantedByTransmutation[0].Add(57);
        }

        var first = CreateEngine(seed: 8080);
        var second = CreateEngine(seed: 8080);
        Prepare(first.State);
        Prepare(second.State);
        int firstRandomBefore = first.State.RandomSeq;
        int secondRandomBefore = second.State.RandomSeq;

        await HexRules.ApplyOnAcquireAsync(first, 0, 80);
        await HexRules.ApplyOnAcquireAsync(second, 0, 80);

        Assert.Equal(first.State.HexState.Owned[0], second.State.HexState.Owned[0]);
        Assert.Equal(firstRandomBefore + 2, first.State.RandomSeq);
        Assert.Equal(secondRandomBefore + 2, second.State.RandomSeq);
        Assert.Equal(2, first.State.HexState.Owned[0].Count);
        Assert.Equal(2, first.State.HexState.Owned[0].Distinct().Count());
        Assert.All(first.State.HexState.Owned[0], id =>
        {
            Assert.Equal(HexTier.Rainbow, HexCatalog.TierForState(id, first.State.HexState));
            Assert.False(HexCatalog.IsTransmutation(id));
            Assert.NotEqual(80, id);
        });
        Assert.Empty(first.State.HexState.GrantedByTransmutation[0]);
    }

    [Fact]
    public async Task 物法皆修_两条触发都随机永久减费且费用不低于零()
    {
        var engine = CreateEngine(seed: 8181);
        var state = engine.State;
        ClearZones(state);
        OwnOnly(state, 0, 81);
        var attacker = Card("HEX-HYBRID-ATTACKER", CardKind.Character);
        var zeroEvent = Card("HEX-HYBRID-ZERO", CardKind.Event, cost: 0);
        state.Players[0].Characters.Add(attacker);
        state.Players[0].Hand.Add(zeroEvent);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            AttackerCardId = attacker.Id,
            TargetIsLeader = true,
            DefenderPlayerIndex = 1,
        };
        int randomBefore = state.RandomSeq;
        await HexRules.OnAttackDeclaredAsync(engine, 0);
        Assert.Equal(randomBefore + 1, state.RandomSeq);
        Assert.Equal(0, zeroEvent.EntityCostModPersistent);
        Assert.Equal(0, state.HandPlayCost(0, zeroEvent));

        state.Players[0].Hand.Clear();
        var eventCard = Card("HEX-HYBRID-EVENT", CardKind.Event, cost: 3);
        state.Players[0].Hand.Add(eventCard);
        await HexRules.OnAttackDeclaredAsync(engine, 0);
        Assert.Equal(-1, eventCard.EntityCostModPersistent);
        Assert.Equal(2, state.HandPlayCost(0, eventCard));

        var character = Card("HEX-HYBRID-CHAR", CardKind.Character, cost: 4);
        state.Players[0].Hand.Add(character);
        await HexRules.OnCardPlayedAsync(
            engine,
            0,
            new PlayResult(PlayKind.Event, eventCard, PaidCost: 2));
        Assert.Equal(-1, character.EntityCostModPersistent);
        Assert.Equal(3, state.HandPlayCost(0, character));
    }

    [Fact]
    public void 品质效果修订运行态进入快照且规则修订版十二投影保持冻结()
    {
        var state = HexState();
        var card = Card("HEX-CHECKPOINT", CardKind.Character, cost: 5);
        card.EntityCostModPersistent = -1;
        card.HexEnteredFromTrash = true;
        card.HexEnteredFromHandByEffect = true;
        card.HexThreeAdmiralsGranted = true;
        card.HexHighCostEntryTurn = state.TurnCount;
        state.Players[0].Characters.Add(card);
        state.HexState.Runtime[0].IceFruitUsedThisTurn = true;
        state.HexState.Runtime[0].SitUpUsedThisTurn = true;
        state.HexState.Runtime[0].FishmanKarateUsedThisTurn = true;
        state.HexState.Runtime[0].CharacterAttacksDeclaredThisTurn = 1;
        state.HexState.Runtime[0].SlaughterhouseUsedThisTurn = true;
        state.HexState.Runtime[0].HighCostCharacterEntriesThisTurn = 3;

        var currentPrivateText = JsonSerializer.Serialize(
            PrivateStateSnapshotBuilder.Build(state));
        var current = DeterministicReplayCheckpointProvider.BuildFullState(state);
        var currentText = current.GetRawText();
        Assert.Contains("EntityCostModPersistent", currentText, StringComparison.Ordinal);
        Assert.Contains("HexEnteredFromTrash", currentText, StringComparison.Ordinal);
        Assert.Contains("IceFruitUsedThisTurn", currentText, StringComparison.Ordinal);
        Assert.Contains("CharacterAttacksDeclaredThisTurn", currentText, StringComparison.Ordinal);
        Assert.Contains("SlaughterhouseUsedThisTurn", currentText, StringComparison.Ordinal);
        Assert.Contains("CharacterAttacksDeclaredThisTurn", currentPrivateText, StringComparison.Ordinal);
        Assert.Contains("SlaughterhouseUsedThisTurn", currentPrivateText, StringComparison.Ordinal);

        state.HexState.RulesRevision = HexRules.ExpansionRulesRevision;
        var legacyPrivateText = JsonSerializer.Serialize(
            PrivateStateSnapshotBuilder.Build(state));
        var legacyText = DeterministicReplayCheckpointProvider.BuildFullState(state).GetRawText();
        Assert.Contains("EntityCostModPersistent", legacyText, StringComparison.Ordinal);
        Assert.Contains("HexEnteredFromTrash", legacyText, StringComparison.Ordinal);
        Assert.Contains("IceFruitUsedThisTurn", legacyText, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterAttacksDeclaredThisTurn", legacyText, StringComparison.Ordinal);
        Assert.DoesNotContain("SlaughterhouseUsedThisTurn", legacyText, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterAttacksDeclaredThisTurn", legacyPrivateText, StringComparison.Ordinal);
        Assert.DoesNotContain("SlaughterhouseUsedThisTurn", legacyPrivateText, StringComparison.Ordinal);
    }

    private static async Task NotifyDiscard(GameEngine engine, int owner, CardKind kind)
        => await HexRules.OnGameEventAsync(
            engine.State,
            EffectTrigger.OnHandDiscarded,
            engine.Prompts,
            new Dictionary<string, object?>
            {
                ["owner"] = owner,
                ["cardKind"] = kind.ToString(),
                ["isCost"] = false,
            });

    private static GameState HexState()
    {
        var state = TestScene.New().Build();
        state.MatchKind = MatchKind.Hex;
        HexRules.Initialize(state);
        state.OpeningStage = OpeningStage.Playing;
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        return state;
    }

    private static GameEngine CreateEngine(int seed = 20260901)
    {
        TestScene.New();
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 50));
        var engine = new GameEngine(
            $"hex-expansion-{seed}-{Guid.NewGuid():N}",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: seed,
            matchKind: MatchKind.Hex);
        engine.State.OpeningStage = OpeningStage.Playing;
        engine.State.CurrentTurnPlayer = 0;
        engine.State.TurnCount = 3;
        engine.State.Phase = Phase.Main;
        engine.State.HexState.ActiveDraft = null;
        engine.State.HexState.DraftResolving = false;
        return engine;
    }

    private static void ClearZones(GameState state)
    {
        foreach (var player in state.Players)
        {
            player.Hand.Clear();
            player.Deck.Clear();
            player.Trash.Clear();
            player.LifeArea.Clear();
            player.Characters.Clear();
            player.StageCard = null;
            player.ExtraStageCard = null;
            player.CostArea.Clear();
            player.DonDeck.Clear();
        }
    }

    private static void OwnOnly(GameState state, int player, params int[] ids)
    {
        state.HexState.Owned[player].Clear();
        state.HexState.Owned[player].AddRange(ids);
    }

    private static CardInstance Card(
        string number,
        CardKind kind,
        int power = 0,
        int cost = 0,
        string[]? keywords = null,
        string[]? abilities = null)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = "红",
                Kind = kind,
                Property = "打",
                Power = power,
                Cost = cost,
                Keywords = keywords ?? [],
                Abilities = abilities ?? [],
            },
        };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task<PendingPrompt> WaitForPrompt(
        GameEngine engine,
        string kind,
        int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
            await Task.Delay(5);
        }
        throw new TimeoutException($"等待海克斯提示 {kind} 超时");
    }

    private static void Respond(GameEngine engine, PendingPrompt prompt, params string[] chosen)
    {
        Assert.True(engine.HandleAction(prompt.PlayerIndex, "PromptResponse", Json(new
        {
            promptId = prompt.PromptId,
            chosen,
        })));
    }
}
