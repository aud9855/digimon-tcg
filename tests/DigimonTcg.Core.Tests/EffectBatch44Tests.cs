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
/// v0.44.0 미지원 문장 18차 묶음 (넓게 훑기): 비용 뒤 "If this effect didn't ~" · 테이머 대상 · 고르고 나머지 소멸 · 상대가 하는 동작 · 패를 N장으로 ·
/// 상대의 디지몬 전부 DP 지속 효과 · 준 【소멸 시】 · 시큐리티 디지몬 DP · 아래의 카드 파기 · 대상 비교 · 트래시의 이 카드로 진화 · 상한 늘리기 다른 표기 · 지속 제한 ·
/// 원래 규칙이 읽던 절은 그대로 (18차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch44Tests
{
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public int Confirms;
        public int OptionAnswer = -1;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Confirms++; return ConfirmAnswer; }
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

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, int playCost = 2) =>
        Card(id, main, CardType.Tamer, 0, playCost, color, 0, name);

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B44-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B44-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B44-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B44-OSEC" + i, 3, dp: 1000, color: CardColor.White));
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

    // =====================================================================
    // 1. 비용 뒤 "If this effect didn't ~" — 비용을 냈을 때만
    // =====================================================================

    [Fact]
    public void IfThisEffectDidnt_AfterCost_RunsOnlyWhenCostPaid_BT22_074()
    {
        var (state, engine, choices) = NewMainPhase();
        var card = Digimon("B44-IFNOT", 5, "[Main] [Once Per Turn] By paying 3 cost, delete 1 of your opponent's level 5 or lower Digimon. If this effect didn't delete, ＜Draw 2＞ (Draw 2 cards from your deck.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        // 대상이 없음 → 비용을 냈고 소멸시키지 않았다 → 《2 드로우》
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, me, card);
        Assert.Equal(5, state.MemoryGauge);
        Assert.Equal(hand + 2, state.Player.Hand.Count);

        // 비용을 내지 않으면 (거절) 뒤 문장도 처리하지 않는다
        var (state2, engine2, choices2) = NewMainPhase();
        choices2.ConfirmAnswer = false;
        var me2 = Put(state2.Player, card);
        int hand2 = state2.Player.Hand.Count;
        engine2.RunEffects(EffectTrigger.ActivateMain, state2.Player, me2, card);
        Assert.Equal(8, state2.MemoryGauge);
        Assert.Equal(hand2, state2.Player.Hand.Count);
    }

    // =====================================================================
    // 2. 테이머가 대상
    // =====================================================================

    [Fact]
    public void SuspendThisTamer_And_BothSideTamers()
    {
        var (state, engine, _) = NewMainPhase();
        var marcus = Tamer("B44-TMR", "[On Play] You may suspend this Tamer.");
        AssertFull(marcus);
        state.Player.Tamers.Add(marcus);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, marcus, includeInherited: false);
        Assert.Contains(marcus, state.Player.SuspendedTamers);

        // "Suspend 1 non-purple Tamer" — 상대의 테이머를 먼저 (자신의 퍼플 이외 테이머는 뒤)
        var fang = Digimon("B44-FANG", 4, "[On Play] [When Digivolving] Suspend 1 non-purple Tamer.");
        AssertFull(fang);
        var oppPurple = Tamer("B44-OPP-P", color: CardColor.Purple);
        var oppRed = Tamer("B44-OPP-R", color: CardColor.Red);
        state.DummyOpponent.Tamers.Add(oppPurple);
        state.DummyOpponent.Tamers.Add(oppRed);
        var me = Put(state.Player, fang);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, fang);
        Assert.Contains(oppRed, state.DummyOpponent.SuspendedTamers);
        Assert.DoesNotContain(oppPurple, state.DummyOpponent.SuspendedTamers);

        // "return all Tamers to their owners' hands" (양쪽)
        var bag = Digimon("B44-RETT", 7, "[On Deletion] Return all Tamers to their owners' hands.");
        AssertFull(bag);
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, null, bag);
        Assert.Empty(state.Player.Tamers);
        Assert.Empty(state.DummyOpponent.Tamers);
        Assert.Contains(marcus, state.Player.Hand);
        Assert.Contains(oppRed, state.DummyOpponent.Hand);
    }

    [Fact]
    public void OpponentTamerCantSuspend_And_MindLinkCommaList()
    {
        var (state, engine, _) = NewMainPhase();
        var sea = Digimon("B44-SEA", 5, "[On Play] [When Digivolving] 1 of your opponent's Tamers can't suspend until the end of their turn.");
        AssertFull(sea);
        var oppT = Tamer("B44-OPP-T");
        state.DummyOpponent.Tamers.Add(oppT);
        var me = Put(state.Player, sea);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, sea);
        Assert.True(engine.TamerSuspendLocked(state.DummyOpponent, oppT));

        var marvin = Tamer("B44-MARVIN", "[Main] ＜Mind Link＞ to 1 of your Digimon with the [Machine], [Cyborg] or [SoC] trait. (Place this Tamer as that Digimon's bottom digivolution card if there are no Tamer cards in its digivolution cards.)");
        AssertFull(marvin);
        state.Player.Tamers.Add(marvin);
        var cy = Put(state.Player, Digimon("B44-CYB", 5, traits: "Cyborg"));
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, marvin, includeInherited: false);
        Assert.DoesNotContain(marvin, state.Player.Tamers);
        Assert.Contains(marvin, cy.EvolutionSources);
    }

    // =====================================================================
    // 3. 패를 N장으로 · 파기한 매수
    // =====================================================================

    [Fact]
    public void OpponentTrashesHandUntil5_And_ForEvery2Trashed()
    {
        var (state, engine, _) = NewMainPhase();
        var moon = Digimon("B44-MOON", 7, "[On Play] [When Digivolving] Your opponent trashes cards in their hand until they have 5 left. For every 2 cards trashed by this effect, delete 1 of your opponent's Tamers.");
        AssertFull(moon);
        for (int i = 0; i < 9; i++) state.DummyOpponent.Hand.Add(Digimon("B44-OH" + i, 3));
        var t1 = Tamer("B44-OT1"); var t2 = Tamer("B44-OT2"); var t3 = Tamer("B44-OT3");
        state.DummyOpponent.Tamers.AddRange(new[] { t1, t2, t3 });
        var me = Put(state.Player, moon);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, moon);
        Assert.Equal(5, state.DummyOpponent.Hand.Count);
        Assert.Single(state.DummyOpponent.Tamers);   // 4장 파기 → 2명 소멸
    }

    // =====================================================================
    // 4. 고르고 나머지 소멸 · 상대가 고른다 · 상대가 어택한다 · 상대가 테이머를 등장
    // =====================================================================

    [Fact]
    public void ChooseEachPlayersHighestPlayCost_DeleteAllOther_EX4_069()
    {
        var (state, engine, _) = NewMainPhase();
        var gaia = Card("B44-GAIA", "[Main] Choose 1 of each player's Digimon with the highest play cost. Delete all other Digimon.");
        AssertFull(gaia);
        var mineHigh = Put(state.Player, Digimon("B44-MH", 6, playCost: 12));
        var mineLow = Put(state.Player, Digimon("B44-ML", 3, playCost: 3));
        var oppHigh = Put(state.DummyOpponent, Digimon("B44-OHI", 6, playCost: 11));
        var oppLow = Put(state.DummyOpponent, Digimon("B44-OLO", 3, playCost: 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, gaia, includeInherited: false);
        Assert.Contains(mineHigh, state.Player.BattleArea);
        Assert.DoesNotContain(mineLow, state.Player.BattleArea);
        Assert.Contains(oppHigh, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(oppLow, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void OpponentChoosesOne_DeleteOthers_GainPerDeleted_BT8_019()
    {
        var (state, engine, _) = NewMainPhase();
        var zhu = Digimon("B44-ZHU", 6, "[When Digivolving] Your opponent chooses 1 of their Digimon. Delete all Digimon other than this Digimon and your opponent's chosen Digimon. For each Digimon deleted by this effect, gain 1 memory.");
        AssertFull(zhu);
        var me = Put(state.Player, zhu);
        var mine2 = Put(state.Player, Digimon("B44-ZM", 3));
        var big = Put(state.DummyOpponent, Digimon("B44-ZBIG", 6, dp: 12000));
        var small = Put(state.DummyOpponent, Digimon("B44-ZSM", 3, dp: 2000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, zhu);
        Assert.Contains(me, state.Player.BattleArea);
        Assert.Contains(big, state.DummyOpponent.BattleArea);   // 상대(AI)는 가장 강한 디지몬을 남긴다
        Assert.DoesNotContain(small, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(mine2, state.Player.BattleArea);
        Assert.Equal(10, state.MemoryGauge);   // 2마리 소멸 → +2
    }

    [Fact]
    public void OpponentMayPlayTamer_IfTheyDont_YouMay_BT17_075()
    {
        var (state, engine, _) = NewMainPhase();
        var eos = Digimon("B44-EOS", 5, "[On Play] [When Digivolving] Your opponent may play 1 Tamer card from their hand without paying the cost. If they don't, you may play 1 white Tamer card with a play cost of 4 or less from your hand without paying the cost.");
        AssertFull(eos);
        var oppT = Tamer("B44-EOS-OT");
        state.DummyOpponent.Hand.Add(oppT);
        var myT = Tamer("B44-EOS-MT", color: CardColor.White);
        state.Player.Hand.Add(myT);
        var me = Put(state.Player, eos);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, eos);
        Assert.Contains(oppT, state.DummyOpponent.Tamers);   // 상대(AI)는 등장시킨다
        Assert.DoesNotContain(myT, state.Player.Tamers);    // 등장했으므로 자신은 등장시키지 않는다
    }

    [Fact]
    public void EitherTarget_SuspendOneOrTwoSmall_P_044()
    {
        var (state, engine, _) = NewMainPhase();
        var herc = Digimon("B44-HERC", 6, "[When Digivolving] Suspend 1 of your opponent's Digimon, or 2 of your opponent's Digimon with 5000 DP or less.");
        AssertFull(herc);
        var a = Put(state.DummyOpponent, Digimon("B44-HA", 3, dp: 3000));
        var b = Put(state.DummyOpponent, Digimon("B44-HB", 3, dp: 4000));
        var me = Put(state.Player, herc);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, herc);   // 추천: 2마리 쪽
        Assert.True(a.IsSuspended && b.IsSuspended);
    }

    // =====================================================================
    // 5. 지속 효과 · 준 효과
    // =====================================================================

    [Fact]
    public void EnemyDigimonDpStatic_WhileSourceCondition_BT16_101()
    {
        var (state, engine, _) = NewMainPhase();
        var rapid = Digimon("B44-RAPID", 6, "[All Turns] While [Rapidmon] or [X Antibody] is in this Digimon's digivolution cards, all of your opponent's suspended Digimon get -4000 DP.");
        AssertFull(rapid);
        var sus = Put(state.DummyOpponent, Digimon("B44-RS", 5, dp: 9000), suspended: true);
        var act = Put(state.DummyOpponent, Digimon("B44-RA", 5, dp: 9000));
        Put(state.Player, rapid, sources: Digimon("B44-XA", 0, name: "X Antibody"));
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, sus.GetCurrentDP());
        Assert.Equal(9000, act.GetCurrentDP());
    }

    [Fact]
    public void StaticGrantOnDeletion_UsesSourcesBeforeDeletion_BT15_057()
    {
        var (state, engine, _) = NewMainPhase();
        var mon = Digimon("B44-MONZ", 5, "[All Turns] While [Numemon] or [X Antibody] is in this Digimon's digivolution cards, it gains \"[On Deletion] You may play 1 Digimon card with [Numemon] in its name from your trash without paying the cost.\"");
        AssertFull(mon);
        var nume = Digimon("B44-NUME", 3, name: "Numemon");
        var me = Put(state.Player, mon, sources: nume);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, nume));   // 진화원이던 「누메몬」이 트래시에서 등장

        // 진화원에 없으면 발휘하지 않는다
        var (state2, engine2, _) = NewMainPhase();
        var me2 = Put(state2.Player, mon, sources: Digimon("B44-OTHER", 3));
        state2.Player.Trash.Add(nume);
        engine2.DeleteByOpponentEffect(state2.Player, me2);
        Assert.DoesNotContain(state2.Player.BattleArea, d => ReferenceEquals(d.TopCard, nume));
    }

    [Fact]
    public void OnDeletion_PlayThisCardFromTrash_IfItHadSources_BT2_083()
    {
        var (state, engine, _) = NewMainPhase();
        var mill = Digimon("B44-MILL", 7, "[On Deletion] If this card had digivolution cards, you may play this card from your trash without paying its memory cost.");
        AssertFull(mill);
        var me = Put(state.Player, mill, sources: Digimon("B44-MS", 6));
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, mill));
    }

    [Fact]
    public void QuotedGrant_CurlyQuotes_And_TwoTargets()
    {
        AssertFull(Digimon("B44-RB30", 5, "[When Digivolving] [When Attacking] [Once Per Turn] By trashing 1 card with [Gammamon] in its text in your hand, 1 of your Digimon gains ”[On Deletion] Delete 1 of your opponent's Digimon with the lowest level” until the end of your opponent's next turn."));
        var (state, engine, _) = NewMainPhase();
        var cheru = Digimon("B44-CHERU", 6, "[When Digivolving] Until the end of your opponent's turn, this Digimon and 1 of your level 5 or lower Digimon gain \"[On Deletion] You may play this card without paying the cost.\"");
        AssertFull(cheru);
        var me = Put(state.Player, cheru);
        var low = Put(state.Player, Digimon("B44-LOW", 5));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, cheru);
        Assert.NotEmpty(me.GrantedEffects);
        Assert.NotEmpty(low.GrantedEffects);
        engine.DeleteByOpponentEffect(state.Player, low);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard.CardId == "B44-LOW");   // 받은 【소멸 시】로 다시 등장
    }

    [Fact]
    public void SecurityDigimonDp_DigimonAndSecurity_ForEachSecurityCard_BT9_043()
    {
        var (state, engine, _) = NewMainPhase();
        var magna = Digimon("B44-MAGNA", 6, "[When Digivolving] If [Magnadramon] or [X Antibody] is in this Digimon's digivolution cards, all of your opponent's Digimon and Security Digimon get -1000 DP for the turn for each card in your security stack.");
        AssertFull(magna);
        var opp = Put(state.DummyOpponent, Digimon("B44-MO", 5, dp: 9000));
        var me = Put(state.Player, magna, sources: Digimon("B44-MXA", 0, name: "X Antibody"));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, magna);
        Assert.Equal(4000, opp.GetCurrentDP());   // 시큐리티 5장 → -5000
        Assert.Contains(engine.SecurityDpBuffsOf(state.DummyOpponent), b => b.Dp == -5000);
    }

    [Fact]
    public void StaticRestriction_OpponentSourcelessCantAttackOrBlock_BT5_032()
    {
        var (state, engine, _) = NewMainPhase();
        var hexe = Digimon("B44-HEXE", 6, "[All Turns] Your opponent's Digimon with no digivolution cards can't attack or block.");
        AssertFull(hexe);
        Put(state.Player, hexe);
        var bare = Put(state.DummyOpponent, Digimon("B44-BARE", 4));
        var stacked = Put(state.DummyOpponent, Digimon("B44-STK", 4), sources: Digimon("B44-STKS", 3));
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(bare, Keyword.CannotAttack));
        Assert.True(CardScripts.HasKeyword(bare, Keyword.CannotBlock));
        Assert.False(CardScripts.HasKeyword(stacked, Keyword.CannotAttack));
    }

    // =====================================================================
    // 6. 아래의 카드 · 진화원 파기
    // =====================================================================

    [Fact]
    public void TrashAnyCardsUnder_And_WithoutCardsUnderCantSuspend_BT16_069()
    {
        var (state, engine, _) = NewMainPhase();
        var geso = Digimon("B44-GESO", 4, "[On Play] [When Digivolving] If [Gesomon] or [X Antibody] is in this Digimon's digivolution cards, trash any 3 cards under 1 of your opponent's Digimon or Tamers. Then, 1 of your opponent's Digimon or Tamers without cards under it can't suspend until the end of their turn");
        AssertFull(geso);
        var opp = Put(state.DummyOpponent, Digimon("B44-GO", 5, dp: 7000), sources: new[] { Digimon("B44-GO1", 4), Digimon("B44-GO2", 3), Digimon("B44-GO3", 2) });
        var me = Put(state.Player, geso, sources: Digimon("B44-GXA", 0, name: "X Antibody"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, geso);
        Assert.Empty(opp.EvolutionSources);
        Assert.Equal(3, state.DummyOpponent.Trash.Count);
        Assert.True(opp.CannotSuspendUntil >= state.TurnSequence);
    }

    [Fact]
    public void ForEachOne_TrashOneCardUnder_UsesTrashedCount_LM_005()
    {
        var (state, engine, _) = NewMainPhase();
        var amphi = Digimon("B44-AMPHI", 6, "[On Play] [When Digivolving] You may trash up to 4 blue cards in your hand. For each one, trash any 1 card under your opponent's Digimon or Tamers. Then, return 1 of their Digimon or Tamers without cards under it to the hand.");
        AssertFull(amphi);
        state.Player.Hand.AddRange(new[] { Digimon("B44-BL1", 3), Digimon("B44-BL2", 3) });
        var opp = Put(state.DummyOpponent, Digimon("B44-AO", 5), sources: new[] { Digimon("B44-AO1", 4), Digimon("B44-AO2", 3) });
        var me = Put(state.Player, amphi);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, amphi);
        Assert.DoesNotContain(opp, state.DummyOpponent.BattleArea);   // 2장 파기 → 아래에 카드가 없음 → 패로
        Assert.Contains(opp.TopCard, state.DummyOpponent.Hand);
    }

    // =====================================================================
    // 7. 대상 비교 · 셈 · 상한
    // =====================================================================

    [Fact]
    public void TargetCompare_AsMuchOrLessDpAsOneOfYourDigimon_And_LevelVsHand()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B44-ST2208", "[Main] Delete 1 of your opponent's Digimon with as much or less DP as 1 of your Digimon.");
        AssertFull(opt);
        Put(state.Player, Digimon("B44-MINE7", 5, dp: 7000));
        var weak = Put(state.DummyOpponent, Digimon("B44-W", 5, dp: 7000));
        var strong = Put(state.DummyOpponent, Digimon("B44-S", 6, dp: 8000));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.DoesNotContain(weak, state.DummyOpponent.BattleArea);
        Assert.Contains(strong, state.DummyOpponent.BattleArea);

        var opt2 = Card("B44-BT23097", "[Main] Delete 1 of your opponent's Digimon with a level as high or higher as the number of cards in your hand.");
        AssertFull(opt2);
        state.Player.Hand.Clear();
        for (int i = 0; i < 6; i++) state.Player.Hand.Add(Digimon("B44-HH" + i, 3));
        var lv5 = Put(state.DummyOpponent, Digimon("B44-L5", 5));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt2, includeInherited: false);
        Assert.Contains(lv5, state.DummyOpponent.BattleArea);   // Lv.5 < 패 6장
        Assert.DoesNotContain(strong, state.DummyOpponent.BattleArea);   // Lv.6 ≥ 6
    }

    [Fact]
    public void MaxUpRewrite_AddToLevelOfDigimonYouCanSelect_BT10_108()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B44-BT10108", "[Main] Delete 1 of your opponent's level 6 or lower Digimon. If there are 10 or more cards in your trash, add 1 to the level of the Digimon you can select with this effect.");
        AssertFull(opt);
        var lv7 = Put(state.DummyOpponent, Digimon("B44-LV7", 7));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Contains(lv7, state.DummyOpponent.BattleArea);
        for (int i = 0; i < 10; i++) state.Player.Trash.Add(Digimon("B44-TR" + i, 3));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.DoesNotContain(lv7, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void TrashCardDigivolvesIntoThisCard_FromTrash_BT20_084()
    {
        var (state, engine, _) = NewMainPhase();
        var ciel = Digimon("B44-CIELA", 4, "[Trash] [All Turns] When any of your Digimon are played, 1 of your [Sistermon Ciel]s may digivolve into this card without paying the cost.", color: CardColor.Blue);
        AssertFull(ciel);
        var baseCiel = Put(state.Player, Digimon("B44-CIEL", 3, name: "Sistermon Ciel", color: CardColor.Blue));
        state.Player.Trash.Add(ciel);
        engine.RunTrashEffects(EffectTrigger.AllyDigimonPlayed, state.Player, baseCiel, baseCiel.TopCard);
        Assert.Same(ciel, baseCiel.TopCard);
        Assert.DoesNotContain(ciel, state.Player.Trash);
    }

    [Fact]
    public void HandTrim_DrawUntil_TrashTopOrBottomSecurity()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B44-DRAWUNTIL", "[Main] Draw cards until there are 6 cards in your hand.");
        AssertFull(opt);
        state.Player.Hand.Add(Digimon("B44-DU1", 3));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(6, state.Player.Hand.Count);

        var gulf = Digimon("B44-GULF", 6, "[When Digivolving] All players trash cards in their hand until they have 5 cards left.");
        AssertFull(gulf);
        for (int i = 0; i < 8; i++) state.DummyOpponent.Hand.Add(Digimon("B44-GO" + i, 3));
        var me = Put(state.Player, gulf);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, gulf);
        Assert.Equal(5, state.Player.Hand.Count);
        Assert.Equal(5, state.DummyOpponent.Hand.Count);
    }

    // =====================================================================
    // 8. 읽기 · 한글 (원래 규칙이 읽던 절은 그대로)
    // =====================================================================

    [Theory]
    [InlineData("[On Deletion] Return all Tamers to their owners' hands. Then, you may play 1 Tamer card from your hand and 1 [Ukkomon] from your trash without paying the costs.", "【소멸 시】 테이머 전부를 패로 되돌린다. 그 후, 자신의 패에서 테이머 카드 1장과 자신의 트래시에서 「웃코몬」 1장을 코스트를 지불하지 않고 등장시킬 수 있다.")]
    [InlineData("[Main] Suspend 1 of your opponent's Digimon, or 2 of your opponent's Digimon with 5000 DP or less.", "【메인】 상대의 디지몬 1마리 또는, DP 5000 이하인 상대의 디지몬 2마리를 레스트시킨다.")]
    [InlineData("[End of Opponent's Turn] You may choose 1 of your opponent's Digimon. Your opponent attacks with the chosen Digimon.", "【상대의 턴 종료 시】 상대의 디지몬 1마리를 선택할 수 있다. 상대는, 선택한 디지몬을 어택시킨다.")]
    [InlineData("[When Digivolving] All players trash cards in their hand until they have 5 cards left.", "【진화 시】 서로는 각각 패가 5장이 되도록 파기한다.")]
    public void KoreanRender_NewSentences(string en, string ko) => Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));

    [Theory]
    [InlineData(CardType.Digimon, "[When Digivolving] If you have 1 or fewer Tamers, from your hand and without paying the cost, you may play 1 Tamer card with [CS] trait from your hand without paying the cost.")]
    [InlineData(CardType.Digimon, "[Your Turn] his Digimon gets +1000 DP for each of your opponent's suspended Digimon.")]
    [InlineData(CardType.Digimon, "[On Play] Suspend 1 Tamer. None of your opponent's Tamers can unsuspend until the end of their turn.")]
    [InlineData(CardType.Digimon, "[When Digivolving] Unsuspend up to 2 Digimon and/or Tamers. Then, gain 1 memory for each of your opponent's unsuspended Digimon and Tamers.")]
    [InlineData(CardType.Digimon, "[End of Your Turn] This Digimon and any of your other Digimon may DNA digivolve into a Digimon card with [Chaosmon] in its name in the hand. Then, the DNA digivolved Digimon may attack.")]
    [InlineData(CardType.Digimon, "[When Digivolving] You may trash up to 3 blue cards from your hand. For each card trashed by this effect, you may trash 1 card under 1 of your opponent's Digimon or Tamers. Then, 1 of your opponent's Digimon or Tamers without cards under it can't suspend until the end of your opponent's turn.")]
    [InlineData(CardType.Option, "[Main] Return 1 of your opponent's level 3, 1 of their level 4 and 1 of their level 5 Digimon to the hand.")]
    [InlineData(CardType.Digimon, "[On Play] [When Digivolving] 1 of your opponent's Digimon gets -3000 DP for the turn. If there are 3 or more Digimon, increase the DP reduction of this effect by -3000.")]
    [InlineData(CardType.Digimon, "When this card would be played, by deleting 1 of your level 2 Digimon in the breeding area, reduce the cost by 2.")]
    [InlineData(CardType.Digimon, "[Opponent's Turn] While a card with the [Hybrid] trait is in this Digimon's digivolution cards, none of your opponent's level 3 Digimon can attack.")]
    public void NewSentences_ParseFully(CardType type, string text)
    {
        var card = Card("B44-PARSE-" + Math.Abs(text.GetHashCode()), text, type, type == CardType.Digimon ? 5 : 0);
        AssertFull(card);
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.NotNull(KoreanEffectRenderer.RenderClause(c.Text)));
    }

    [Fact]
    public void PreviouslyUnsupportedPendingCards_StayAsTheyWere()
    {
        // BT11-034 (결정 대기)와 EX10-029 (영어 원문과 공식 한글이 다름)는 이 묶음에서 건드리지 않는다
        var ex10029 = Digimon("B44-EX10029", 4, inherited: "By trashing 1 of this Digimon's link cards, effects don't affect 1 of your Digimon until your opponent's turn ends.", traits: "Sport (App Name)");
        Assert.Contains(EffectSupport.For(ex10029).Clauses, c => c.Support == ClauseSupport.Unsupported);
    }
}
