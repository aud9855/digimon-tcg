using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.45.0 미지원 문장 19차 묶음: 【소멸 시】 · "소멸했을 때"의 조건은 소멸 직전 모습으로 (종합 룰 15-8-3-9-4) · 지속 "액티브가 되지 않는다" ·
/// 덱 위 공개 묶음 · ≪딜레이≫ 선택지 · "one of them" · [육성] [메인] · DP 16000 취급 · 비교 지속 효과 · DP 하한 · 등장 코스트 합계로 여러 장 ·
/// 앞 문장의 결과 조건 · 원래 규칙이 읽던 절은 그대로 (19차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch45Tests
{
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public int OptionAnswer = -1;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => ConfirmAnswer;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) =>
            recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => OptionAnswer >= 0 ? OptionAnswer : recommended;
        public IReadOnlyList<int> ChooseCards(GameState state, string question, CardChoiceZone zone, IReadOnlyList<CardData> options, int min, int max, IReadOnlyList<int> recommended) => recommended;
    }

    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", int evoCost = 1, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", int dp = -1, string? name = null, int playCost = 3,
        CardColor color = CardColor.Blue, int evoCost = 1, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, inherited, evoCost, traits);

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, int playCost = 2, string inherited = "", params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, playCost, color, 0, name, inherited, 1, traits);

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B45-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B45-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B45-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B45-OSEC" + i, 3, dp: 1000, color: CardColor.White));
        }
        var choices = new Scripted();
        return (state, new GoldfishEngine(state) { PlayerChoices = choices }, choices);
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, int turnPlayed = 1, params CardData[] sources)
    {
        var d = new CardInstance(card, turnPlayed) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    private static int CountAll(PlayerZones z) =>
        z.MainDeck.Count + z.Hand.Count + z.SecurityStack.Count + z.Trash.Count + z.Tamers.Count + z.OptionsInPlay.Count
        + z.BattleArea.Sum(d => 1 + d.EvolutionSources.Count + d.LinkedCards.Count) + (z.BreedingArea is { } b ? 1 + b.EvolutionSources.Count : 0);

    // =====================================================================
    // 1. 【소멸 시】 · "소멸했을 때"의 조건은 소멸 직전 모습으로 (종합 룰 15-8-3-9-4)
    // =====================================================================

    [Fact]
    public void OnDeletion_SourceCondition_UsesSourcesBeforeDeletion_BT7_075()
    {
        var (state, engine, _) = NewMainPhase();
        var raihi = Digimon("B45-RAIHI", 6, "[On Deletion] If a card with the [Hybrid] trait is in this Digimon's digivolution cards, you may play 1 purple Tamer card from your trash without paying its memory cost.");
        AssertFull(raihi);
        var hybrid = Digimon("B45-HYB", 5, traits: "Hybrid");
        var tamer = Tamer("B45-PT", color: CardColor.Purple);
        state.Player.Trash.Add(tamer);
        var me = Put(state.Player, raihi, sources: hybrid);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(tamer, state.Player.Tamers);
        Assert.Contains(hybrid, state.Player.Trash);   // 진화원은 트래시에 (조건만 소멸 직전 모습으로 본다)
    }

    [Fact]
    public void OnDeletion_WasInSources_P145()
    {
        var (state, engine, _) = NewMainPhase();
        var p145 = Digimon("B45-P145", 6, "[On Deletion] If [Myotismon] or [X Antibody] was in this Digimon's digivolution cards, you may play 1 level 6 Digimon card with [Myotismon] in its name from your trash without paying the cost.");
        AssertFull(p145);
        var myo = Digimon("B45-MYO", 5, name: "Myotismon");
        var malo = Digimon("B45-MALO", 6, name: "MaloMyotismon");
        state.Player.Trash.Add(malo);
        var me = Put(state.Player, p145, sources: myo);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == malo);
    }

    [Fact]
    public void AllyDeleted_EventFilterWithSources_SeesSourcesBeforeDeletion_BT7_080()
    {
        var (state, engine, _) = NewMainPhase();
        var lobo = Digimon("B45-LOBO", 4, "[All Turns] [Once Per Turn] When one of your Digimon with a Tamer card in its digivolution cards is deleted, you may play 1 Tamer card from your trash without paying its memory cost.");
        AssertFull(lobo);
        Put(state.Player, lobo);
        var tamerSrc = Tamer("B45-TSRC");
        var victim = Put(state.Player, Digimon("B45-VIC", 5), sources: tamerSrc);
        engine.DeleteByOpponentEffect(state.Player, victim);
        // 소멸한 디지몬의 진화원에 테이머 카드가 있었다 → 트래시의 테이머 카드(방금 트래시로 간 그 카드)를 등장
        Assert.Contains(tamerSrc, state.Player.Tamers);
    }

    [Fact]
    public void OnDeletion_DeletionView_IsOnlyForConditions_NoDuplicateCards()
    {
        var (state, engine, _) = NewMainPhase();
        var mill = Digimon("B45-MILL", 7, "[On Deletion] If this card had digivolution cards, you may play this card from your trash without paying its memory cost.");
        AssertFull(mill);
        var me = Put(state.Player, mill, sources: new[] { Digimon("B45-S1", 5), Digimon("B45-S2", 6) });
        int before = CountAll(state.Player);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == mill);
        Assert.Equal(before, CountAll(state.Player));
    }

    // =====================================================================
    // 2. 지속 "액티브가 되지 않는다"
    // =====================================================================

    [Fact]
    public void AllOtherDigimonAndTamersDontUnsuspend_PhaseAndEffects_BT12_057()
    {
        var (state, engine, _) = NewMainPhase();
        var quartz = Digimon("B45-QUARTZ", 7, "[All Turns] All other Digimon and Tamers don't unsuspend.");
        AssertFull(quartz);
        var q = Put(state.Player, quartz, suspended: true);
        var other = Put(state.Player, Digimon("B45-OTHER", 4), suspended: true);
        var tamer = Tamer("B45-TMR");
        state.Player.Tamers.Add(tamer);
        state.Player.SuspendedTamers.Add(tamer);
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.IsUnsuspendLocked(state, other));   // 효과로도 액티브가 되지 않는다
        engine.ExecuteActivePhase();
        Assert.False(q.IsSuspended);
        Assert.True(other.IsSuspended);
        Assert.Contains(tamer, state.Player.SuspendedTamers);
    }

    [Fact]
    public void NoUnsuspendInUnsuspendPhase_OnlyPhase_EX10_023()
    {
        var (state, engine, _) = NewMainPhase();
        var quartz = Digimon("B45-QUARTZ2", 7, "[All Turns] Other than this Digimon, no Digimon or Tamers can unsuspend in the unsuspend phase.");
        AssertFull(quartz);
        Put(state.DummyOpponent, quartz);
        var mine = Put(state.Player, Digimon("B45-MINE", 4), suspended: true);
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.IsUnsuspendLocked(state, mine));   // 효과로는 액티브가 될 수 있다
        engine.ExecuteActivePhase();
        Assert.True(mine.IsSuspended);
    }

    [Fact]
    public void OpponentTamersDontUnsuspendDuringTheirPhase_WhileEosmon_BT6_092()
    {
        var (state, engine, _) = NewMainPhase();
        var menoa = Tamer("B45-MENOA", "[Opponent's Turn] While you have an [Eosmon] in play, your opponent's Tamers don't unsuspend during their unsuspend phase.");
        AssertFull(menoa);
        state.DummyOpponent.Tamers.Add(menoa);
        Put(state.DummyOpponent, Digimon("B45-EOS", 5, name: "Eosmon"));
        var myTamer = Tamer("B45-MYT");
        state.Player.Tamers.Add(myTamer);
        state.Player.SuspendedTamers.Add(myTamer);
        engine.ExecuteActivePhase();
        Assert.Contains(myTamer, state.Player.SuspendedTamers);

        // 에오스몬이 없으면 액티브가 된다
        var (state2, engine2, _) = NewMainPhase();
        state2.DummyOpponent.Tamers.Add(menoa);
        var t2 = Tamer("B45-MYT2");
        state2.Player.Tamers.Add(t2);
        state2.Player.SuspendedTamers.Add(t2);
        engine2.ExecuteActivePhase();
        Assert.DoesNotContain(t2, state2.Player.SuspendedTamers);
    }

    // =====================================================================
    // 3. 덱 위 공개 묶음
    // =====================================================================

    [Fact]
    public void RevealChoose_DeleteUpToChosenPlayCost_TrashRevealed_BT9_105()
    {
        var (state, engine, _) = NewMainPhase();
        var soul = Card("B45-SOUL", "[Main] Reveal the top 3 cards of your deck. Choose 1 Digimon card with the [X Antibody] trait among them, and delete 1 of your opponent's Digimon whose play cost is less than or equal to the chosen card's play cost. Trash the revealed cards.");
        AssertFull(soul);
        state.Player.MainDeck.Insert(0, Digimon("B45-XA", 5, playCost: 7, traits: "X Antibody"));
        var low = Put(state.DummyOpponent, Digimon("B45-LOW", 5, playCost: 6));
        var high = Put(state.DummyOpponent, Digimon("B45-HIGH", 6, playCost: 9));
        int deck = state.Player.MainDeck.Count, trash = state.Player.Trash.Count;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, soul);
        Assert.DoesNotContain(low, state.DummyOpponent.BattleArea);
        Assert.Contains(high, state.DummyOpponent.BattleArea);
        Assert.Equal(deck - 3, state.Player.MainDeck.Count);
        Assert.Equal(trash + 3, state.Player.Trash.Count);
    }

    [Fact]
    public void OpponentReveals_DeDigivolveForEachDigimon_ReturnedToDeck_BT14_065()
    {
        var (state, engine, _) = NewMainPhase();
        var vade = Digimon("B45-VADE", 5, "[On Play] Your opponent reveals the top 3 cards of their deck. ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.) for each Digimon card among them. Return the cards to either the top or bottom of the deck.");
        AssertFull(vade);
        var target = Put(state.DummyOpponent, Digimon("B45-T6", 6), sources: new[] { Digimon("B45-T3", 3), Digimon("B45-T4", 4), Digimon("B45-T5", 5) });
        int oppDeck = state.DummyOpponent.MainDeck.Count;
        var me = Put(state.Player, vade);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, vade);
        Assert.Equal(3, target.TopCard.Level);   // 공개한 3장 모두 디지몬 → ≪퇴화 1≫ 3번 (Lv.3에서 멈춤)
        Assert.Equal(oppDeck, state.DummyOpponent.MainDeck.Count);
    }

    [Fact]
    public void RevealIfAllBlack_GainMemory_PlaceOnTop_BT4_096()
    {
        var (state, engine, _) = NewMainPhase();
        var izzy = Tamer("B45-IZZY", "[On Play] Reveal the top 3 cards of your deck. If all of them are black, gain 1 memory. Place the cards at the top of the deck in any order.");
        AssertFull(izzy);
        var top = Enumerable.Range(0, 3).Select(i => Digimon("B45-BK" + i, 3, color: CardColor.Black)).ToList();
        state.Player.MainDeck.InsertRange(0, top);
        state.Player.Tamers.Add(izzy);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, izzy, includeInherited: false);
        Assert.Equal(9, state.MemoryGauge);
        Assert.Equal(top, state.Player.MainDeck.Take(3).ToList());
    }

    [Fact]
    public void RevealTwo_OneToSecurity_RestToHand_BT6_100()
    {
        var (state, engine, _) = NewMainPhase();
        var boost = Card("B45-BOOST", "[Main] Reveal the top 2 cards of your deck. Place 1 of them on top of your security stack face down. Add the remaining card to your hand.");
        AssertFull(boost);
        int sec = state.Player.SecurityStack.Count, hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, boost);
        Assert.Equal(sec + 1, state.Player.SecurityStack.Count);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    [Fact]
    public void RevealForEach_ReducesOpponentPlayCosts_ThenDelete_BT16_060()
    {
        var (state, engine, _) = NewMainPhase();
        var tank = Digimon("B45-TANK", 5, "[On Play] Reveal the top 3 cards of your deck. For each [D-Brigade] or [DigiPolice] trait card among them, reduce the play costs of all of your opponent's Digimon by 1 until the end of the turn. Return the revealed cards to the top or the bottom of the deck. Then, delete 1 of your opponent's Digimon with a play cost of 4 or less.");
        AssertFull(tank);
        state.Player.MainDeck.Insert(0, Digimon("B45-DB1", 3, traits: "D-Brigade"));
        state.Player.MainDeck.Insert(0, Digimon("B45-DB2", 3, traits: "DigiPolice"));
        var six = Put(state.DummyOpponent, Digimon("B45-SIX", 5, playCost: 6));
        var me = Put(state.Player, tank);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, tank);
        Assert.DoesNotContain(six, state.DummyOpponent.BattleArea);   // 등장 코스트 6 - 2 = 4
    }

    // =====================================================================
    // 4. ≪딜레이≫ 선택지 · "one of them"
    // =====================================================================

    [Fact]
    public void DelayWithBulletDot_WhenMyotismonDeleted_PlaysVenomMyotismon_BT15_098()
    {
        var (state, engine, _) = NewMainPhase();
        var mist = Card("B45-MIST", "[All Turns] When one of your [Myotismon] is deleted, ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n• You may play 1 [VenomMyotismon] from your trash without paying the cost.");
        AssertFull(mist);
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = mist, PlacedTurn = 1 });
        var venom = Digimon("B45-VENOM", 6, name: "VenomMyotismon");
        state.Player.Trash.Add(venom);
        var myo = Put(state.Player, Digimon("B45-MYO2", 5, name: "Myotismon"));
        engine.DeleteByOpponentEffect(state.Player, myo);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == venom);
        Assert.Contains(mist, state.Player.Trash);
    }

    [Fact]
    public void DelayOption_PlayedDigimonCantDigivolveToLevel7_EX3_069()
    {
        var (state, engine, _) = NewMainPhase();
        var trial = Card("B45-TRIAL", "[Main] ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n・Play 1 Digimon card with the [Four Great Dragons] trait from your hand without paying the cost. The Digimon played by this effect can't digivolve to level 7, and at the end of your opponent's turn, delete that Digimon.");
        AssertFull(trial);
        var dragon = Digimon("B45-DRAGON", 6, traits: "Four Great Dragons");
        state.Player.Hand.Add(dragon);
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = trial, PlacedTurn = 1 });
        Assert.True(engine.ActivateMainAbility(null, trial).CanExecute);
        var played = Assert.Single(state.Player.BattleArea, d => d.TopCard == dragon);
        Assert.False(CardScripts.EvoAllowed(played, Digimon("B45-LV7", 7)));
        Assert.True(CardScripts.EvoAllowed(played, Digimon("B45-LV6B", 6)));
    }

    [Fact]
    public void DelayOption_PlayTamerOfSameColor_CostReducedBy4_P206()
    {
        var (state, engine, _) = NewMainPhase();
        var gate = Card("B45-GATE", "You can ignore this card's color requirements.\n[Main] ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n・You may play 1 Tamer card with the same color as any of your Digimon on the field from your hand with the play cost reduced by 4.");
        AssertFull(gate);
        Put(state.Player, Digimon("B45-RED", 4, color: CardColor.Red));
        var redTamer = Tamer("B45-REDT", color: CardColor.Red, playCost: 5);
        var blueTamer = Tamer("B45-BLUET", color: CardColor.Blue, playCost: 1);
        state.Player.Hand.Add(redTamer);
        state.Player.Hand.Add(blueTamer);
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = gate, PlacedTurn = 1 });
        Assert.True(engine.ActivateMainAbility(null, gate).CanExecute);
        Assert.Contains(redTamer, state.Player.Tamers);
        Assert.DoesNotContain(blueTamer, state.Player.Tamers);
        Assert.Equal(7, state.MemoryGauge);   // 5 - 4 = 1
    }

    [Fact]
    public void DelayOption_CostDigivolveFromTrash_ThenTrashOpponentsOption_BT18_100()
    {
        var (state, engine, _) = NewMainPhase();
        var gospel = Card("B45-GOSPEL", "[Main] ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n・By digivolving 1 of your Digimon with [Lucemon] in its name into a Digimon card with [Lucemon] in its name from your trash with the digivolution cost reduced by 3, trash 1 Option card in your opponent's battle area.");
        AssertFull(gospel);
        var luce = Put(state.Player, Digimon("B45-LUCE3", 3, name: "Lucemon"));
        var falldown = Digimon("B45-LUCE4", 4, name: "Lucemon: Falldown Mode", evoCost: 3);
        state.Player.Trash.Add(falldown);
        var oppOpt = Card("B45-OPPOPT");
        state.DummyOpponent.OptionsInPlay.Add(new PlacedOption { Card = oppOpt, PlacedTurn = 1 });
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = gospel, PlacedTurn = 1 });
        Assert.True(engine.ActivateMainAbility(null, gospel).CanExecute);
        Assert.Equal(falldown, luce.TopCard);
        Assert.Empty(state.DummyOpponent.OptionsInPlay);
        Assert.Contains(oppOpt, state.DummyOpponent.Trash);
    }

    [Fact]
    public void OneOfThem_EventDigimonMayAttack_BT25_075()
    {
        var card = Tamer("B45-LINKATK", "[Your Turn] When your Digimon get linked, one of them may attack.");
        AssertFull(card);
        Assert.Equal("【자신의 턴】 자신의 디지몬이 링크했을 때, 그 디지몬으로 어택할 수 있다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    // =====================================================================
    // 5. [육성] [메인] · DP 16000 취급
    // =====================================================================

    [Fact]
    public void BreedingMain_PlaysNegamonAndPlacesEggUnder_EX9_005()
    {
        var (state, engine, _) = NewMainPhase();
        var nega = Card("B45-NEGA", "[Breeding] [Main] [Once Per Turn] You may play 1 Digimon card with [Negamon] in its text from your hand with the play cost reduced by 2. For each [Negamon] in your trash or your Digimon's digivolution cards, further reduce it by 1. Then, place this Digimon as the played Digimon's bottom digivolution card.\n[Breeding] [All Turns] This Digimon can't digivolve and effects can't delete or trash it.",
            CardType.Digitama, 2, name: "Negamon");
        AssertFull(nega);
        state.Player.BreedingArea = new CardInstance(nega, 1);
        var big = Digimon("B45-BIG", 5, "[On Play] Draw 1 if [Negamon] is here.", playCost: 6);
        state.Player.Hand.Add(big);
        state.Player.Trash.Add(Digimon("B45-NEGA2", 3, name: "Negamon"));
        var ability = Assert.Single(engine.GetMainAbilities(state.Player), a => a.Card == nega);
        Assert.True(engine.ActivateMainAbility(ability.Digimon, nega).CanExecute);
        var played = Assert.Single(state.Player.BattleArea, d => d.TopCard == big);
        Assert.Null(state.Player.BreedingArea);
        Assert.Contains(nega, played.EvolutionSources);
        Assert.Equal(8 - (6 - 3), state.MemoryGauge);   // 코스트 -2, 트래시의 「네가몬」 1장 → -1 더
        Assert.True(EffectRules.IsDigivolveLocked(state, new CardInstance(nega, 1)));   // 육성 에어리어에서 진화할 수 없다
    }

    [Fact]
    public void BreedingAura_MotherEatersTreatedAs16000Dp_BT22_007()
    {
        var (state, engine, _) = NewMainPhase();
        var egg = Card("B45-MEGG", "[Breeding] [All Turns] All of your [Mother Eater]s in the battle area are treated as having 16000 DP.", CardType.Digitama, 2, name: "Mother Eater");
        AssertFull(egg);
        state.Player.BreedingArea = new CardInstance(egg, 1);
        var eater = Put(state.Player, Digimon("B45-MEATER", 6, dp: 12000, name: "Mother Eater"));
        var other = Put(state.Player, Digimon("B45-OTH", 6, dp: 12000));
        engine.RecalculateFieldBuffs();
        Assert.Equal(16000, eater.GetCurrentDP());
        Assert.Equal(12000, other.GetCurrentDP());
    }

    // =====================================================================
    // 6. 비교 지속 효과 · DP 하한
    // =====================================================================

    [Fact]
    public void CompareToThisDigimon_EnemyWithLessDpCantAttackPlayers_BT18_046()
    {
        var (state, engine, _) = NewMainPhase();
        var wasp = Digimon("B45-WASP", 4, "[Opponent's Turn] None of your opponent's Digimon with as much or less DP as this Digimon can attack players.", dp: 6000);
        AssertFull(wasp);
        var me = Put(state.Player, wasp);
        me.IsOwnersTurn = false;   // 상대의 턴
        var weak = Put(state.DummyOpponent, Digimon("B45-WEAK", 4, dp: 5000));
        var strong = Put(state.DummyOpponent, Digimon("B45-STRONG", 5, dp: 9000));
        engine.RecalculateFieldBuffs();
        Assert.Contains(Keyword.CannotAttackPlayers, weak.ActiveKeywords);
        Assert.DoesNotContain(Keyword.CannotAttackPlayers, strong.ActiveKeywords);
    }

    [Fact]
    public void DpFloor_CantHaveLessThan1000Dp_EX11_070()
    {
        var (state, engine, _) = NewMainPhase();
        var unchained = Tamer("B45-UNCH", inherited: "[All Turns] This Digimon with [Maquinamon] in its text can't have less than 1000 DP, and your opponent's effects can't trash its stacked cards.");
        AssertFull(unchained);
        var maq = Put(state.Player, Digimon("B45-MAQ", 5, "[On Play] [Maquinamon] text.", dp: 5000), sources: unchained);
        engine.RecalculateFieldBuffs();
        engine.ApplyDpMinus(maq, 9000, "test");
        Assert.Contains(maq, state.Player.BattleArea);
        Assert.Equal(1000, maq.GetCurrentDP());
    }

    // =====================================================================
    // 7. 등장 코스트 합계로 여러 장
    // =====================================================================

    [Fact]
    public void PlayUpToTotalWorth_FromHandOrTrash_BT26_081()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B45-ILIAD", 6, "[On Play] You may play up to 8 play cost's total worth of [Iliad] trait cards from your hand or trash without paying the costs.");
        AssertFull(card);
        var a = Digimon("B45-IL5", 4, playCost: 5, traits: "Iliad");
        var b = Digimon("B45-IL3", 3, playCost: 3, traits: "Iliad");
        var c = Digimon("B45-IL4", 3, playCost: 4, traits: "Iliad");
        state.Player.Hand.Add(a);
        state.Player.Trash.Add(b);
        state.Player.Hand.Add(c);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        int total = state.Player.BattleArea.Where(d => d != me).Sum(d => d.TopCard.PlayCost);
        Assert.True(total <= 8 && total >= 7, $"total {total}");
        Assert.Equal(8, state.MemoryGauge);   // 코스트 없이
    }

    // =====================================================================
    // 8. 앞 문장의 결과 조건
    // =====================================================================

    [Fact]
    public void IfYouTrashAPurpleCard_LooksAtTheTrashedCard_BT9_076()
    {
        var (state, engine, _) = NewMainPhase();
        var may = Digimon("B45-MAY", 5, "[On Play] You may trash 1 card in your hand. If you trash a purple card, delete 1 of your opponent's level 3 Digimon. If you trash a yellow card, 1 of your opponent's Digimon gets -3000 DP for the turn.");
        AssertFull(may);
        state.Player.Hand.Add(Digimon("B45-PURP", 4, color: CardColor.Purple));
        var lv3 = Put(state.DummyOpponent, Digimon("B45-OL3", 3, dp: 9000));
        var me = Put(state.Player, may);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, may);
        Assert.DoesNotContain(lv3, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void IfThisEffectTrashedATraitCard_EX5_069()
    {
        var (state, engine, _) = NewMainPhase();
        var crush = Card("B45-CRUSH", "[Main] By trashing 1 card in your hand, delete 1 of your opponent's level 6 or lower Digimon. If this effect trashed a card with the [Seven Great Demon Lords] trait, place this card in the battle area.");
        AssertFull(crush);
        state.Player.Hand.Add(Digimon("B45-SGDL", 6, traits: "Seven Great Demon Lords"));
        Put(state.DummyOpponent, Digimon("B45-OPP6", 6));
        Put(state.Player, Digimon("B45-BLUE", 4));   // 색 조건
        state.Player.Hand.Add(crush);
        Assert.True(engine.PlayCard(crush).CanExecute);
        Assert.Contains(state.Player.OptionsInPlay, p => p.Card == crush);

        // 다른 카드를 파기했으면 놓지 않는다 (트래시로)
        var (state2, engine2, _) = NewMainPhase();
        state2.Player.Hand.Add(Digimon("B45-PLAIN", 6));
        Put(state2.DummyOpponent, Digimon("B45-OPP6B", 6));
        Put(state2.Player, Digimon("B45-BLUE2", 4));
        state2.Player.Hand.Add(crush);
        Assert.True(engine2.PlayCard(crush).CanExecute);
        Assert.DoesNotContain(state2.Player.OptionsInPlay, p => p.Card == crush);
        Assert.Contains(crush, state2.Player.Trash);
    }

    // =====================================================================
    // 9. 읽기 · 한글 · 원래 규칙은 그대로
    // =====================================================================

    [Theory]
    [InlineData("[All Turns] All other Digimon and Tamers don't unsuspend.", "【서로의 턴】 이 디지몬 이외의 디지몬과 테이머 전부는 액티브가 되지 않는다.")]
    [InlineData("[All Turns] Other than this Digimon, no Digimon or Tamers can unsuspend in the unsuspend phase.", "【서로의 턴】 액티브 페이즈에서는, 이 디지몬 이외의 디지몬과 테이머 전부는 액티브가 되지 않는다.")]
    [InlineData("[On Play] [When Digivolving] Your opponent reveals the top 3 cards of their deck. Choose 1 Digimon card among them, and delete up to its play cost's total worth of your opponent's Digimon. Return the revealed cards to either the top or bottom of the deck.",
        "【등장 시】 【진화 시】 상대의 덱 위에서부터 3장 오픈한다. 그중 디지몬 카드 1장을 선택하고, 선택한 카드의 등장 코스트 합계까지 상대의 디지몬을 소멸시킨다. 오픈한 카드는 덱 위 또는 아래로만 되돌린다.")]
    [InlineData("[Breeding] [All Turns] All of your [Mother Eater]s in the battle area are treated as having 16000 DP.", "[육성] 【서로의 턴】 배틀 에어리어의 자신의 「마더 이터」 전부는 DP 16000으로 취급한다.")]
    [InlineData("[When Digivolving] You may use 1 Option card with [Plug-In] in its name from your hand without meeting its color requirements or paying its memory cost.", "【진화 시】 자신의 패에서 명칭에 「플러그인」을 포함하는 옵션 카드 1장을 색 조건을 무시하고 코스트를 지불하지 않고 사용할 수 있다.")]
    [InlineData("[On Play] [When Digivolving] You may trash 1 card in your hand. If you trash a purple card, delete 1 of your opponent's level 3 Digimon. If you trash a yellow card, 1 of your opponent's Digimon gets -3000 DP for the turn.",
        "【등장 시】 【진화 시】 자신의 패를 1장 파기할 수 있다. 파기한 카드가 퍼플이라면, Lv.3인 상대의 디지몬 1마리를 소멸시킨다. 파기한 카드가 옐로라면, 이 턴 동안, 상대의 디지몬 1마리의 DP를 -3000 한다.")]
    public void KoreanRender_NewSentences(string en, string ko) => Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));

    [Theory]
    [InlineData(CardType.Digimon, "[On Play] Reveal the top 3 cards of your deck. Delete 1 of your opponent's Digimon with as high or lower a play cost as 1 Digimon card among them. Trash the revealed cards.")]
    [InlineData(CardType.Option, "[Main] Reveal the top 3 cards of your deck. Add all Digimon cards with ＜Digi-Burst＞ among them to your hand. Place the remaining cards at the bottom of your deck in any order. Then, suspend 1 of your opponent's Digimon for each card added to your hand by this effect.")]
    [InlineData(CardType.Digimon, "[When Digivolving] You may Reveal the top 3 cards of your deck. For each of your opponent's Digimon, you may play 1 Digimon card with [Mamemon] in its name and a memory cost of 10 or less among the revealed cards without paying its memory cost. Trash the remaining cards.")]
    [InlineData(CardType.Tamer, "[Your Turn] When your Digimon are played or digivolve, if any of them have the [Xros Heart] or [Hero] trait, by suspending this Tamer, one of them may attack.")]
    [InlineData(CardType.Digimon, "[Opponent's Turn] None of your opponent's Digimon with as many or fewer digivolution cards as this Digimon can suspend.")]
    [InlineData(CardType.Digimon, "[On Play] [On Deletion] If your opponent has 3 or fewer security cards, delete 1 of their Digimon with the lowest DP. If they have 4 or more, trash the top card of their security stack.")]
    [InlineData(CardType.Digimon, "[Your Turn] While your opponent has a Digimon with a digivolution card in play, this Digimon can't attack.")]
    [InlineData(CardType.Digimon, "[When Digivolving] Trash the top 2 cards of your deck for each of your opponent's Digimon in play. If you trash 4 or more cards with this effect, you may play 1 level 5 or lower purple Digimon card from your trash without paying its memory cost.")]
    public void NewSentences_ParseFully(CardType type, string text)
    {
        var card = Card("B45-PARSE-" + Math.Abs(text.GetHashCode()), text, type, type == CardType.Digimon ? 5 : 0);
        AssertFull(card);
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.NotNull(KoreanEffectRenderer.RenderClause(c.Text)));
    }

    [Fact]
    public void PreviouslySupportedReveal_StillUsesOriginalRule()
    {
        // 원래 규칙이 읽던 공개 문장은 묶음 규칙(⟦rv45⟧)으로 바뀌지 않는다 — 한글도 그대로
        const string text = "[On Play] Reveal the top 3 cards of your deck. Add 1 Digimon card among them to the hand. Return the rest to the bottom of the deck.";
        var card = Digimon("B45-OLDREV", 3, text);
        AssertFull(card);
        Assert.DoesNotContain(SentenceParser.RevealMark45, KoreanEffectRenderer.RenderClause(text) ?? "");
    }

    [Fact]
    public void PendingDecisionCards_StayAsTheyWere()
    {
        var ex10029 = Digimon("B45-EX10029", 4, inherited: "By trashing 1 of this Digimon's link cards, effects don't affect 1 of your Digimon until your opponent's turn ends.", traits: "Sport (App Name)");
        Assert.Contains(EffectSupport.For(ex10029).Clauses, c => c.Support == ClauseSupport.Unsupported);
    }
}
