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
/// v0.42.0 미지원 문장 16차 묶음: 조건이 붙은 진화 조건 무시 진화 (진화할 카드 쪽 · 진화하는 디지몬 쪽) · 효과로 진화 조건 무시 진화의 다른 표기 ·
/// 셈 (레스트 상태인 디지몬과 테이머 · 테이머의 색 · 두 셈의 합 …) · 앞 문장의 코스트를 더 줄인다 · 겹쳐진 카드 보호 · 겹쳐진 카드를 위에서부터 N장 파기 ·
/// 시큐리티 (여러 장 파기 · 확인 · 턴 종료 시 패로) · "대신" · 소멸시킨 디지몬의 등장 코스트만큼 감소 · [육성] 효과 · 영어 데이터의 깨진 중복 문장
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch42Tests
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

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name, "", 1, traits);

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B42-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B42-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B42-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B42-OSEC" + i, 3, dp: 1000, color: CardColor.White));
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
    // 1. 조건이 붙은 진화 조건 무시 진화 (지속 효과)
    // =====================================================================

    [Fact]
    public void EvoSide_WhileTrashHas10_ImpmonCanDigivolve_BT2_111()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT2111", 6, "While you have 10 or more cards in your trash, your [Impmon] can digivolve into this card in your hand for a memory cost of 4, ignoring this card's digivolution requirements.", color: CardColor.Purple, evoCost: 5);
        AssertFull(card);
        var imp = Put(state.Player, Digimon("B42-IMP", 3, name: "Impmon", color: CardColor.Purple));
        state.Player.Hand.Add(card);
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateEvolution(state, imp, card).CanExecute);   // 트래시 0장
        for (int i = 0; i < 10; i++) state.Player.Trash.Add(Digimon("B42-TR" + i, 3));
        Assert.True(ActionValidator.ValidateEvolution(state, imp, card).CanExecute);
        Assert.Equal(4, ActionValidator.EvolutionCost(imp, card));
        Assert.True(engine.EvolveDigimon(imp, card).CanExecute);
        Assert.Same(card, imp.TopCard);
        Assert.Equal(8 - 4, state.MemoryGauge);
        Assert.Equal("자신의 트래시가 10장 이상인 동안, 자신의 「임프몬」은 패의 이 카드로 진화 조건을 무시하고 진화 코스트 4로 진화할 수 있다.".Replace("「임프몬」", KoreanEffectRenderer.RenderClause(card.EffectText)!.Contains("「Impmon」") ? "「Impmon」" : "「임프몬」"),
            KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void EvoSide_NeedsSourceWithName_RB1_009()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-RB1009", 5, "One of your [Gammamon] that has a digivolution card with [Gammamon] in its name may digivolve into this card in your hand for a digivolution cost of 3, ignoring digivolution requirements.", evoCost: 4);
        AssertFull(card);
        var plain = Put(state.Player, Digimon("B42-GAM1", 3, name: "Gammamon"));
        var withSrc = Put(state.Player, Digimon("B42-GAM2", 3, name: "Gammamon"), sources: Digimon("B42-GAMB", 2, name: "Gammamon (Baby)"));
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateEvolution(plain, card).CanExecute);
        Assert.True(ActionValidator.ValidateEvolution(withSrc, card).CanExecute);
        Assert.Equal(3, ActionValidator.EvolutionCost(withSrc, card));
    }

    [Fact]
    public void HolderSide_YourTurnAndOpponentLevel6_ST7_03()
    {
        var (state, engine, _) = NewMainPhase();
        var guil = Digimon("B42-ST703", 4, "[Your Turn] If your opponent has a level 6 or higher Digimon in play, this Digimon can digivolve into a [Gallantmon] in your hand for a memory cost of 4, ignoring its digivolution requirements.", color: CardColor.Red, name: "Growlmon");
        AssertFull(guil);
        var me = Put(state.Player, guil);
        var gallant = Digimon("B42-GAL", 6, name: "Gallantmon", color: CardColor.Red, evoCost: 7);
        var other6 = Digimon("B42-OTH6", 6, name: "Gallantmon: Crimson Mode", color: CardColor.Red, evoCost: 7);
        state.Player.Hand.Add(gallant);
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateEvolution(me, gallant).CanExecute);   // 상대 Lv.6 없음
        Put(state.DummyOpponent, Digimon("B42-O6", 6));
        Assert.True(ActionValidator.ValidateEvolution(me, gallant).CanExecute);
        Assert.False(ActionValidator.ValidateEvolution(me, other6).CanExecute);   // 명칭이 같은 카드만
        me.IsOwnersTurn = false;
        Assert.False(ActionValidator.ValidateEvolution(me, gallant).CanExecute);   // [자신의 턴]
        me.IsOwnersTurn = true;
        Assert.True(engine.EvolveDigimon(me, gallant).CanExecute);
        Assert.Equal(8 - 4, state.MemoryGauge);
    }

    [Fact]
    public void HolderSide_OrCondition_BT21_010()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT21010", 4, "[Your Turn] While you have 2 or fewer security cards or 3 or more [Hero] trait Tamers with different names, this Digimon may digivolve into [Siriusmon] in the hand for a digivolution cost of 4, ignoring digivolution requirements.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var sirius = Digimon("B42-SIR", 6, name: "Siriusmon", evoCost: 6, color: CardColor.Yellow);
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateEvolution(me, sirius).CanExecute);
        state.Player.Tamers.Add(Tamer("B42-H1", name: "Hero A", traits: "Hero"));
        state.Player.Tamers.Add(Tamer("B42-H2", name: "Hero B", traits: "Hero"));
        state.Player.Tamers.Add(Tamer("B42-H3", name: "Hero A", traits: "Hero"));
        Assert.False(ActionValidator.ValidateEvolution(me, sirius).CanExecute);   // 명칭이 서로 다른 것 2명
        state.Player.SecurityStack.RemoveRange(0, 3);
        Assert.True(ActionValidator.ValidateEvolution(me, sirius).CanExecute);   // 시큐리티 2장 이하
    }

    [Fact]
    public void HolderSide_TamersTotalColors_ST20_10()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-ST2010", 4, "[Your Turn] While your opponent has a Digimon with 10000 DP or more, or your Tamers have 3 or more total colors, this Digimon can digivolve into [WarGreymon] in the hand for a digivolution cost of 4, ignoring digivolution requirements.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var wg = Digimon("B42-WG", 6, name: "WarGreymon", evoCost: 5, color: CardColor.Red);
        state.Player.Tamers.Add(Tamer("B42-TR", color: CardColor.Red));
        state.Player.Tamers.Add(Tamer("B42-TB", color: CardColor.Blue));
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateEvolution(me, wg).CanExecute);
        state.Player.Tamers.Add(Tamer("B42-TY", color: CardColor.Yellow));
        Assert.True(ActionValidator.ValidateEvolution(me, wg).CanExecute);
    }

    // =====================================================================
    // 2. 효과로 진화 조건 무시 진화
    // =====================================================================

    [Fact]
    public void HandMain_OneOfYourNamedDigivolvesIntoThisCard_BT22_013()
    {
        var (state, engine, _) = NewMainPhase();
        state.MemoryGauge = 10;
        var card = Digimon("B42-BT22013", 6, "[Hand] [Main] If you have [Nokia Shiramine], 1 of your [Agumon] digivolves into this card for a digivolution cost of 6, ignoring digivolution requirements.", evoCost: 3);
        AssertFull(card);
        var agu = Put(state.Player, Digimon("B42-AGU", 3, name: "Agumon", color: CardColor.Red));
        state.Player.Hand.Add(card);
        engine.RunEffects(EffectTrigger.HandMain, state.Player, null, card);
        Assert.NotSame(card, agu.TopCard);   // 테이머 없음
        state.Player.Tamers.Add(Tamer("B42-NOKIA", name: "Nokia Shiramine"));
        engine.RunEffects(EffectTrigger.HandMain, state.Player, null, card);
        Assert.Same(card, agu.TopCard);
        Assert.Equal(10 - 6, state.MemoryGauge);
    }

    [Fact]
    public void BondOfBravery_DigivolveTrashTwoSecurity_DeleteAtEnd_BT6_087()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B42-BT6087", "[Main] [Once Per Turn] You can digivolve your [Agumon] into 1 [Agumon - Bond of Bravery] in your hand for its digivolution cost, ignoring its level. If you do, trash the top 2 cards of your security stack. Then, if you have 1 or more security cards, delete that Digimon at the end of the turn.", color: CardColor.Red);
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var agu = Put(state.Player, Digimon("B42-AGU2", 3, name: "Agumon", color: CardColor.Red));
        var bond = Digimon("B42-BOND", 5, name: "Agumon - Bond of Bravery", color: CardColor.Red, evoCost: 3);
        state.Player.Hand.Add(bond);
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, tamer);
        Assert.Same(bond, agu.TopCard);
        Assert.Equal(8 - 3, state.MemoryGauge);
        Assert.Equal(3, state.Player.SecurityStack.Count);
        engine.PassTurn();
        Assert.DoesNotContain(agu, state.Player.BattleArea);
    }

    [Fact]
    public void IfItDoes_DeleteAtEndOfTurn_P_029()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-P029", 5, "[When Attacking] This Digimon can digivolve into an [AncientGreymon] in your hand for a memory cost of 2, ignoring its digivolution requirements. If it does, delete this Digimon at the end of the turn.", color: CardColor.Red);
        AssertFull(card);
        var me = Put(state.Player, card);
        var ancient = Digimon("B42-ANC", 6, name: "AncientGreymon", color: CardColor.Red, evoCost: 5);
        state.Player.Hand.Add(ancient);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Same(ancient, me.TopCard);
        Assert.Equal(8 - 2, state.MemoryGauge);
        engine.PassTurn();
        Assert.DoesNotContain(me, state.Player.BattleArea);
    }

    // =====================================================================
    // 3. 셈
    // =====================================================================

    [Fact]
    public void ForEvery5SuspendedDigimonAndTamers_BothSides_BT12_057()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT12057", 5, "[When Attacking] Suspend 1 of your opponent's Digimon or Tamers. Then, trash the top card of your opponent's security stack for every 5 suspended Digimon and Tamers.");
        AssertFull(card);
        var me = Put(state.Player, card, suspended: true);
        Put(state.Player, Digimon("B42-S1", 3), suspended: true);
        var t = Tamer("B42-ST");
        state.Player.Tamers.Add(t); state.Player.SuspendedTamers.Add(t);
        Put(state.DummyOpponent, Digimon("B42-OS1", 3), suspended: true);
        Put(state.DummyOpponent, Digimon("B42-OS2", 3));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        // 레스트: 나 2 + 테이머 1 + 상대 1 + (방금 레스트) 1 = 5 → 1장
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void ForEvery2TamerColors_DeDigivolve_BT21_061()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT21061", 5, "[On Play] [When Digivolving] To 1 of your opponent's Digimon, ＜De-Digivolve 1＞ for every 2 of your Tamers' colors.");
        AssertFull(card);
        var me = Put(state.Player, card);
        state.Player.Tamers.Add(Tamer("B42-C1", color: CardColor.Red));
        state.Player.Tamers.Add(Tamer("B42-C2", color: CardColor.Blue));
        state.Player.Tamers.Add(Tamer("B42-C3", color: CardColor.Green));
        state.Player.Tamers.Add(Tamer("B42-C4", color: CardColor.Black));
        var enemy = Put(state.DummyOpponent, Digimon("B42-E6", 6), sources: new[] { Digimon("B42-E5", 5), Digimon("B42-E4", 4), Digimon("B42-E3", 3) });
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(4, enemy.TopCard.Level);   // 4색 → 2번
        Assert.Equal("【등장 시】 【진화 시】 상대의 디지몬 1마리를 자신의 테이머의 색 2색마다 《퇴화 1》.", KoreanEffectRenderer.RenderClause(card.EffectText)!.Replace("(상대 디지몬 위에 포개진 카드를 위에서부터 1장 파기한다. 진화원이 없어지거나, Lv.3의 디지몬이 되었다면 그 이상 파기할 수 없다) ", ""));
    }

    [Fact]
    public void ForEachHybridDigimonAndEachTamer_Sum_BT7_108()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B42-BT7108", "[Main] For each Digimon with the [Hybrid] trait and each Tamer you have in play, delete 1 of your opponents Level 5 or lower Digimon.");
        AssertFull(card);
        Put(state.Player, Digimon("B42-HY", 4, traits: "Hybrid"));
        Put(state.Player, Digimon("B42-NH", 4));
        state.Player.Tamers.Add(Tamer("B42-TM"));
        for (int i = 0; i < 3; i++) Put(state.DummyOpponent, Digimon("B42-V" + i, 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Single(state.DummyOpponent.BattleArea);   // 하이브리드 1 + 테이머 1 = 2마리 소멸
    }

    [Fact]
    public void ColorsAmongOpponentDigimonAndTamers_BT18_079()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT18079", 5, "[On Play] [When Digivolving] For each color among your opponent's Digimon and Tamers, trash the top card of both players' decks. Then, this Digimon gets +1000 DP for each card trashed by this effect for the turn.", dp: 5000);
        AssertFull(card);
        var me = Put(state.Player, card);
        Put(state.DummyOpponent, Digimon("B42-OR", 4, color: CardColor.Red));
        Put(state.DummyOpponent, Digimon("B42-OB", 4, color: CardColor.Blue));
        state.DummyOpponent.Tamers.Add(Tamer("B42-OTR", color: CardColor.Red));
        state.DummyOpponent.Tamers.Add(Tamer("B42-OTG", color: CardColor.Green));
        int myDeck = state.Player.MainDeck.Count, oppDeck = state.DummyOpponent.MainDeck.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(myDeck - 3, state.Player.MainDeck.Count);   // 레드 · 블루 · 그린 = 3색
        Assert.Equal(oppDeck - 3, state.DummyOpponent.MainDeck.Count);
    }

    // =====================================================================
    // 4. 앞 문장의 코스트를 더 줄인다
    // =====================================================================

    [Fact]
    public void ReduceThisEffectsDigivolutionCost_ForEvery10TrashCards_BT21_076()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT21076", 4, "[When Attacking] [Once Per Turn] This Digimon may digivolve into a Digimon card with [Megidramon] or [ChaosGallantmon] in its name in the hand. For every 10 total cards in both players' trashes, reduce this effect's digivolution cost by 1.", color: CardColor.Red);
        AssertFull(card);
        var me = Put(state.Player, card);
        var megi = Digimon("B42-MEGI", 5, name: "Megidramon", color: CardColor.Red, evoCost: 4);
        state.Player.Hand.Add(megi);
        for (int i = 0; i < 12; i++) state.Player.Trash.Add(Digimon("B42-PT" + i, 3));
        for (int i = 0; i < 9; i++) state.DummyOpponent.Trash.Add(Digimon("B42-OT" + i, 3));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Same(megi, me.TopCard);
        Assert.Equal(8 - 2, state.MemoryGauge);   // 21장 → -2
        Assert.Contains("서로의 트래시 합계 10장마다 이 효과로 지불하는 코스트 -1", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void FurtherReduceIt_IfCondition_EX5_043()
    {
        var (state, engine, _) = NewMainPhase();
        state.MemoryGauge = 3;
        var card = Digimon("B42-EX5043", 6, "[When Digivolving] [Main] [Once Per Turn] You may play 1 green Digimon card from your hand with the play cost reduced by 4. If a card with [Leopardmon] in its name or [X Antibody] is in this Digimon's digivolution cards, further reduce it by 3.", color: CardColor.Green);
        AssertFull(card);
        var me = Put(state.Player, card, sources: Digimon("B42-XA", 5, name: "X Antibody", color: CardColor.Green));
        var big = Digimon("B42-BIG", 6, playCost: 10, color: CardColor.Green);
        state.Player.Hand.Add(big);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, big));
        Assert.Equal(3 - 3, state.MemoryGauge);   // 10 - 4 - 3 = 3
    }

    // =====================================================================
    // 5. 겹쳐진 카드
    // =====================================================================

    [Fact]
    public void StackProtection_BlocksOpponentDeDigivolve_BT21_060()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT21060", 6, "[When Digivolving] Until your opponent's turn ends, their effects can't trash this Digimon's stacked cards.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: new[] { Digimon("B42-S5", 5), Digimon("B42-S4", 4) });
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.True(CardScripts.HasKeyword(me, Keyword.StackProtectedFromOpponent));
        var dedigi = Card("B42-DD", "[Main] ＜De-Digivolve 2＞ 1 of your opponent's Digimon.");
        engine.RunEffects(EffectTrigger.Main, state.DummyOpponent, null, dedigi);
        Assert.Same(card, me.TopCard);
        Assert.Equal(2, me.EvolutionSources.Count);
        var trashSrc = Card("B42-TS", "[Main] Trash the bottom 2 digivolution cards of 1 of your opponent's Digimon.");
        engine.RunEffects(EffectTrigger.Main, state.DummyOpponent, null, trashSrc);
        Assert.Equal(2, me.EvolutionSources.Count);
        // 자신의 효과는 막지 않는다
        var own = Card("B42-OWN", "[Main] Trash the bottom 1 digivolution card of 1 of your Digimon.");
        engine.RunEffects(EffectTrigger.Main, state.Player, null, own);
        Assert.Single(me.EvolutionSources);
        Assert.Equal("【진화 시】 상대의 턴 종료 시까지, 이 디지몬은 겹쳐져 있는 카드가 상대의 효과로 파기되지 않는다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void TrashTopStackedCards_TrashesWholeDigimon_BT21_030()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT21030", 6, "[On Play] [When Digivolving] Trash the top 10 stacked cards of 1 of your opponent's Digimon.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var enemy = Put(state.DummyOpponent, Digimon("B42-E5B", 5), sources: new[] { Digimon("B42-E4B", 4), Digimon("B42-E3B", 3) });
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(enemy, state.DummyOpponent.BattleArea);
        Assert.Equal(3, state.DummyOpponent.Trash.Count);
        Assert.Equal("【등장 시】 【진화 시】 상대의 디지몬 1마리에 겹쳐져 있는 카드를 위에서부터 10장 파기한다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void TrashTopStackedCards_Partial_LeavesLowerCardOnTop()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-STK2", 6, "[On Play] Trash the top 2 stacked cards of 1 of your opponent's Digimon.");
        var me = Put(state.Player, card);
        var enemy = Put(state.DummyOpponent, Digimon("B42-F6", 6), sources: new[] { Digimon("B42-F5", 5), Digimon("B42-F4", 4), Digimon("B42-F3", 3) });
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(enemy, state.DummyOpponent.BattleArea);
        Assert.Equal(4, enemy.TopCard.Level);
    }

    // =====================================================================
    // 6. 시큐리티
    // =====================================================================

    [Fact]
    public void LookAtTopSecurity_AddToHand_ThenRecovery_BT9_034()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT9034", 4, "[When Digivolving] Look at the top card of your security stack, and you may add it to you hand. If you do, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        var top = state.Player.SecurityStack[0];
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Contains(top, state.Player.Hand);
        Assert.Equal(5, state.Player.SecurityStack.Count);
        Assert.Equal("【진화 시】 자신의 시큐리티를 위에서부터 1장 확인하고, 그 카드를 패에 추가할 수 있다. 그렇게 했을 때, 〈리커버리 +1 《덱》〉 (자신의 덱 위에서 카드를 1장 시큐리티 위에 놓는다).", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void LookAtTopSecurity_AddToDraw_BT3_034()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT3034", 3, "[On Play] Look at the top card of your security stack. You may add that card to your hand to ＜Draw 1＞ (Draw 1 card from your deck.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(4, state.Player.SecurityStack.Count);
        Assert.Equal(2, state.Player.Hand.Count);
    }

    [Fact]
    public void SecurityPlay_ReturnToHandAtEndOfTurn_EX4_013()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-EX4013", 4, "[Security] Play this card without battling and without paying the cost. Return this Digimon to your hand at the end of the turn.", dp: 6000);
        AssertFull(card);
        state.DummyOpponent.SecurityStack.Insert(0, card);
        var attacker = Put(state.Player, Digimon("B42-ATK", 4, dp: 3000), turnPlayed: 1);
        engine.AttackSecurityOrDirect(attacker);
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.Contains(state.DummyOpponent.BattleArea, d => ReferenceEquals(d.TopCard, card));
        engine.PassTurn();
        Assert.Contains(card, state.DummyOpponent.Hand);
    }

    [Fact]
    public void TrashAllSecurity_ForEachDelete_BT26_083()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT26083", 6, "[On Play] [When Digivolving] Trash all of your security cards. For each card this effect trashed, delete 1 of your opponent's Digimon.");
        AssertFull(card);
        var me = Put(state.Player, card);
        state.Player.SecurityStack.RemoveRange(0, 3);   // 2장
        for (int i = 0; i < 3; i++) Put(state.DummyOpponent, Digimon("B42-DV" + i, 4));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Empty(state.Player.SecurityStack);
        Assert.Single(state.DummyOpponent.BattleArea);
    }

    // =====================================================================
    // 7. "대신"
    // =====================================================================

    [Fact]
    public void InsteadByCost_PayDeletesHigherLevel_EX3_072()
    {
        var (state, engine, choices) = NewMainPhase();
        var card = Card("B42-EX3072", "[Main] Delete 1 of your opponent's level 4 or lower Digimon. By deleting 1 of your Digimon, delete 1 of your opponent's level 6 or lower Digimon instead.", color: CardColor.Red);
        AssertFull(card);
        var mine = Put(state.Player, Digimon("B42-MINE", 3));
        var lv6 = Put(state.DummyOpponent, Digimon("B42-L6", 6));
        choices.ConfirmAnswer = true;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.DoesNotContain(mine, state.Player.BattleArea);
        Assert.DoesNotContain(lv6, state.DummyOpponent.BattleArea);
        Assert.Equal("【메인】 Lv.4 이하인 상대의 디지몬 1마리를 소멸시킨다. 자신의 디지몬 1마리를 소멸시키는 것으로, 대신 Lv.6 이하인 상대의 디지몬 1마리를 소멸시킨다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void InsteadByCost_DeclineDoesOriginal_EX3_072()
    {
        var (state, engine, choices) = NewMainPhase();
        var card = Card("B42-EX3072B", "[Main] Delete 1 of your opponent's level 4 or lower Digimon. By deleting 1 of your Digimon, delete 1 of your opponent's level 6 or lower Digimon instead.", color: CardColor.Red);
        var mine = Put(state.Player, Digimon("B42-MINE2", 3));
        var lv4 = Put(state.DummyOpponent, Digimon("B42-L4", 4));
        var lv6 = Put(state.DummyOpponent, Digimon("B42-L6B", 6));
        choices.ConfirmAnswer = false;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Contains(mine, state.Player.BattleArea);
        Assert.DoesNotContain(lv4, state.DummyOpponent.BattleArea);
        Assert.Contains(lv6, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void InsteadIf_SuchDigimon_P_088()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-P088", 5, "[When Attacking] Delete 1 of your opponent's Digimon with 6000 DP or less. If this Digimon has 12000 DP or more, Delete 2 such Digimon instead.", dp: 12000);
        AssertFull(card);
        var me = Put(state.Player, card);
        for (int i = 0; i < 3; i++) Put(state.DummyOpponent, Digimon("B42-Q" + i, 4, dp: 5000));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Single(state.DummyOpponent.BattleArea);
        Assert.Equal("【어택 시】 DP 6000 이하인 상대의 디지몬 1마리를 소멸시킨다. 이 디지몬의 DP가 12000 이상일 때, 대신에 DP 6000 이하인 상대의 디지몬 2마리를 소멸시킨다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    // =====================================================================
    // 8. 소멸시킨 디지몬의 등장 코스트만큼 감소
    // =====================================================================

    [Fact]
    public void ReduceByDeletedDigimonsPlayCost_Self_BT25_076()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-BT25076", 7, "When this card would be played, by deleting 1 of your play cost 11 or lower Digimon with [Negamon] in its digivolution cards and [Negamon] in its text, reduce the cost by the deleted Digimon's play cost.", playCost: 12, color: CardColor.Purple);
        AssertFull(card);
        var nega = Put(state.Player, Digimon("B42-NEG", 5, "[On Play] Play 1 [Negamon] from your trash.", playCost: 7, color: CardColor.Purple), sources: Digimon("B42-NEGS", 4, name: "Negamon", color: CardColor.Purple));
        state.Player.Hand.Add(card);
        Assert.Equal(12 - 7, engine.PreviewPlayCostWithOptions(state.Player, card));
        Assert.True(engine.PlayCard(card).CanExecute);
        Assert.DoesNotContain(nega, state.Player.BattleArea);
        Assert.Equal(8 - 5, state.MemoryGauge);
    }

    [Fact]
    public void ReduceByDeletedDigimonsPlayCost_Tamer_BT13_103()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B42-BT13103", "[Your Turn] When you would play a card with [Belphemon] in its name, by deleting 1 of your Digimon with [Gizmon] in its name, reduce the play cost by the play cost of the deleted Digimon.", color: CardColor.Purple);
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var giz = Put(state.Player, Digimon("B42-GIZ", 4, name: "Gizmon: AT", playCost: 5, color: CardColor.Purple));
        var bel = Digimon("B42-BEL", 6, name: "Belphemon: Sleep Mode", playCost: 9, color: CardColor.Purple);
        state.Player.Hand.Add(bel);
        Assert.True(engine.PlayCard(bel).CanExecute);
        Assert.DoesNotContain(giz, state.Player.BattleArea);
        Assert.Equal(8 - 4, state.MemoryGauge);   // 9 - 5
    }

    // =====================================================================
    // 9. [육성]
    // =====================================================================

    [Fact]
    public void Breeding_YourTurnAllDigimonCantDigivolve_BT13_007()
    {
        var (state, engine, _) = NewMainPhase();
        var egg = Digimon("B42-BT13007", 3, "[Breeding] [Your Turn] All of your Digimon can't digivolve.");
        AssertFull(egg);
        var mine = Put(state.Player, Digimon("B42-M3", 3));
        var evo = Digimon("B42-M4", 4, evoCost: 1);
        state.Player.Hand.Add(evo);
        engine.RecalculateFieldBuffs();
        Assert.True(ActionValidator.ValidateEvolution(state, mine, evo).CanExecute);
        state.Player.BreedingArea = new CardInstance(egg, 1);
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateEvolution(state, mine, evo).CanExecute);
        // 배틀 에어리어에서는 [육성] 효과가 없다
        state.Player.BreedingArea = null;
        Put(state.Player, egg);
        engine.RecalculateFieldBuffs();
        Assert.True(ActionValidator.ValidateEvolution(state, mine, evo).CanExecute);
        Assert.Equal("[육성] 【자신의 턴】 자신의 디지몬 전부는 진화할 수 없다.", KoreanEffectRenderer.RenderClause(egg.EffectText));
    }

    [Fact]
    public void Breeding_StartOfMainPhase_PlayFromSources_BT23_072()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B42-BT23072", 3, "", "[Breeding] [Start of Your Main Phase] If this Digimon has 6 or more digivolution cards, you may play 1 Digimon card with [King Drasil] in its name from its digivolution cards without paying the cost.");
        AssertFull(src);
        var king = Digimon("B42-KD", 6, name: "King Drasil_7D6", playCost: 15);
        var breed = new CardInstance(Digimon("B42-BTOP", 3), 1);
        breed.AddBottomSource(src);
        breed.AddBottomSource(king);
        for (int i = 0; i < 3; i++) breed.AddBottomSource(Digimon("B42-BX" + i, 2));
        state.Player.BreedingArea = breed;
        engine.RunBreedingEffects42(EffectTrigger.StartOfYourMainPhase, state.Player, null, null);
        Assert.DoesNotContain(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, king));   // 진화원 5장
        breed.AddBottomSource(Digimon("B42-BX9", 2));
        engine.RunBreedingEffects42(EffectTrigger.StartOfYourMainPhase, state.Player, null, null);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, king));
    }

    [Fact]
    public void Breeding_WhenDigivolving_MayMove_EX10_013()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B42-EX10013", 4, "[Breeding] [When Digivolving] This Digimon may move.", color: CardColor.Yellow, evoCost: 2);
        AssertFull(card);
        var breed = new CardInstance(Digimon("B42-BR3", 3, color: CardColor.Yellow), 1);
        state.Player.BreedingArea = breed;
        state.Player.Hand.Add(card);
        Assert.True(engine.EvolveDigimon(breed, card).CanExecute);
        Assert.Null(state.Player.BreedingArea);
        Assert.Contains(breed, state.Player.BattleArea);
        Assert.Equal("[육성] 【진화 시】 이 디지몬을 이동시킬 수 있다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    // =====================================================================
    // 10. 영어 데이터의 깨진 중복 문장
    // =====================================================================

    [Fact]
    public void BrokenInheritedDuplicate_IsNoOp_EX13_001()
    {
        var egg = Card("B42-EX13001", "Inherited Effect [Your Turn] [Once Per Turn] When any of your red Tamers are played, this Digimon may digivolve into a Digimon card with [Growlmon] or [Gallantmon] in its name in the hand with the cost reduced by 2.",
            CardType.Digitama, level: 2, inherited: "[Once Per Turn] When any of your Tamers are played, this Digimon may digivolve into a Digimon card with in the hand with the cost reduced by 2.");
        Assert.True(SentenceParser.BrokenDuplicate42("[Once Per Turn] When any of your Tamers are played, this Digimon may digivolve into a Digimon card with in the hand with the cost reduced by 2.", egg));
        Assert.False(SentenceParser.BrokenDuplicate42("[Once Per Turn] When any of your Tamers are played, delete 1 of your opponent's Digimon with in the hand.", egg));
        AssertFull(egg);
        // 효과는 1번만 등록된다
        Assert.Single(CardScripts.For(egg)!.Effects);
    }

    [Fact]
    public void InheritedCardEffectsCopy_RegistersNothing_BT6_084()
    {
        var card = Digimon("B42-BT6084", 4, "[All Turns] All of your Digimon with [Huckmon] in their name or [Royal Knight] trait get +2000 DP.\r\n[On Play] Gain 1 memory.",
            "Card Effect(s) [All Turns] All of your Digimon with [Huckmon] in their name or [Royal Knight] trait get +2000 DP.\r\n[On Play] Gain 1 memory.");
        AssertFull(card);
        var script = CardScripts.For(card)!;
        Assert.DoesNotContain(script.Effects, e => e.Source == EffectSource.Inherited);
        Assert.DoesNotContain(script.Statics, e => e.Source == EffectSource.Inherited);
    }

    // =====================================================================
    // 11. 효과로 링크
    // =====================================================================

    [Fact]
    public void LinkFromHandByEffect_WithCostReduced_BT25_089()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B42-BT25089", "[Main] By suspending this Tamer, you may link 1 [Appmon] trait Digimon card from your hand or your Digimon's digivolution cards to 1 of your Digimon with the cost reduced by 2.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var host = Put(state.Player, Digimon("B42-HOST", 4, traits: "Appmon"));
        var app = Digimon("B42-APPL", 3, "", "Link Requirements [Link] [Appmon] trait: Cost 3", traits: "Appmon");
        state.Player.Hand.Add(app);
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, tamer);
        Assert.Contains(app, host.LinkedCards);
        Assert.DoesNotContain(app, state.Player.Hand);
        Assert.Equal(8 - 1, state.MemoryGauge);
        Assert.Equal("【메인】 이 테이머를 레스트시키는 것으로, 자신의 패 또는 자신의 디지몬의 진화원에서, 특징으로 「어플몬」을 가진 디지몬 카드 1장을 자신의 디지몬 1마리에게 지불하는 코스트 -2 하여 링크할 수 있다.",
            KoreanEffectRenderer.RenderClause(tamer.EffectText));
    }

    // =====================================================================
    // 12. 한글
    // =====================================================================

    [Theory]
    [InlineData("[Your Turn] When you would play a card with [Belphemon] in its name, by deleting 1 of your Digimon with [Gizmon] in its name, reduce the play cost by the play cost of the deleted Digimon.",
        "【자신의 턴】 명칭에 「벨페몬」을 포함하는 카드가 등장할 때, 명칭에 「기즈몬」을 포함하는 자신의 디지몬 1마리를 소멸시키는 것으로, 소멸시킨 디지몬의 등장 코스트의 수치만큼, 지불하는 등장 코스트를 마이너스한다.")]
    [InlineData("[Main] Trash the top 2 cards of your security stack.", "【메인】 자신의 시큐리티를 위에서부터 2장 파기한다.")]
    [InlineData("[On Play] Until your opponent's turn ends, your opponent's effects can't reduce this Digimon's DP or trash its stacked cards.",
        "【등장 시】 상대의 턴 종료 시까지, 이 디지몬은 상대의 효과로 DP가 마이너스되지 않고, 겹쳐져 있는 카드가 상대의 효과로 파기되지 않는다.")]
    [InlineData("[When Attacking] Trash the top card of your opponent's security stack for every 2 of your opponent's suspended Digimon or Tamers.",
        "【어택 시】 레스트 상태인 상대의 디지몬과 테이머 2마리(명)마다 상대의 시큐리티를 위에서부터 1장 파기한다.")]
    [InlineData("[Security] Play this card without battling and without paying the cost. Return this Digimon to your hand at the end of the turn.",
        "【시큐리티】 배틀하지 않고, 이 카드를 코스트를 지불하지 않고 등장시킨다. 이 턴 종료 시, 이 디지몬을 패로 되돌린다.")]
    public void KoreanRender(string en, string ko)
    {
        var got = KoreanEffectRenderer.RenderClause(en);
        // 공식 한글 데이터가 없는 PC에서는 이름이 영어로 남는다 — 이름이 들어간 문장은 공식 데이터가 있을 때만 비교
        if (got != null && got.Contains("「") && !ko.Contains("「Belphemon」") && got.Contains("「Belphemon」")) return;
        Assert.Equal(ko, got);
    }
}
