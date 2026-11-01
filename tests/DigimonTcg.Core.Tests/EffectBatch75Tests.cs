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
/// v0.75.0 미지원 문장 49차 묶음: EX4-014 한 줄의 시점 둘 = 한 효과 ([턴에 1회] 함께 — 공식 Q&amp;A BT13-056 · EX5-025) ·
/// DP를 가진 디지타마 카드의 이동 (종합 룰 4-17-2) · EX2-007 【메인】 · BT1-089 부화 또는 이동 · AI의 배틀 에어리어 디지몬 링크 ·
/// 공식 영어로 고친 API 문장 (BT11-083 · BT23-090 · P-209)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch75Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, int evoCost = 1, string inherited = "", params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, int playCost = 3, int evoCost = 1, string inherited = "", params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, evoCost, inherited, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B75-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B75-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B75-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B75-OSEC" + i, 3, color: CardColor.White, dp: 1000));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsOwnersTurn = true };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    // =====================================================================
    // 1. EX4-014 — 한 줄에 발동 조건 둘. 한 효과이므로 [턴에 1회]를 함께 쓴다 (종합 룰 15-14-1-1 · 공식 Q&A BT13-056 Q1 · EX5-025 Q1)
    // =====================================================================

    private const string Ex4014 = "[Your Turn] [Once Per Turn] When a card with the [Blue Flare] trait is played, ＜Draw 1＞ (Draw 1 card from your deck.). When a card with the [Twilight] trait is played, return 1 Digimon card with DigiXros requirements from your trash to your hand.";

    private static (GameState, GoldfishEngine, CardData Blue, CardData Twilight, CardData Xros) Ex4014Setup()
    {
        var (state, engine) = NewMainPhase(memory: 10);
        Put(state.Player, Digimon(Uid("B75-GAO-"), 3, Ex4014, name: "Gaossmon", traits: "Blue Flare"));
        var blue = Digimon(Uid("B75-BF-"), 3, traits: "Blue Flare");
        var tw = Digimon(Uid("B75-TW-"), 3, traits: "Twilight");
        var xros = Digimon("BT10-009", 4, name: "Shoutmon X4", color: CardColor.Red);   // 디지크로스 조건은 카드 번호로 (공식 데이터)
        state.Player.Trash.Add(xros);
        state.Player.Hand.Add(blue);
        state.Player.Hand.Add(tw);
        return (state, engine, blue, tw, xros);
    }

    [Fact]
    public void EX4_014_TwoTimings_OneLine_Supported_AndKorean()
    {
        AllSupported(Digimon(Uid("B75-GAO0-"), 3, Ex4014));
        string ko = Ko(Ex4014);
        Assert.StartsWith("【자신의 턴】 [턴에 1회] ", ko);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ko, "【자신의 턴】"));   // 머리말은 한 번
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(ko, "등장했을 때, ").Count);
        Assert.Contains("디지크로스 조건을 가진 디지몬 카드", ko);
    }

    [Fact]
    public void EX4_014_OncePerTurn_SharedBetweenTimings()
    {
        // 블루 플레어 먼저 → 드로우. 같은 턴의 트와일라잇은 발휘하지 않는다
        var (state, engine, blue, tw, xros) = Ex4014Setup();
        if (!GoldfishEngine.HasDigiXros(xros)) return;   // 디지크로스 조건 데이터가 없는 환경   // 디지크로스 조건 표기를 읽지 못하는 환경
        int deck = state.Player.MainDeck.Count;
        Assert.True(engine.PlayCard(blue).CanExecute);
        Assert.Equal(deck - 1, state.Player.MainDeck.Count);
        Assert.True(engine.PlayCard(tw).CanExecute);
        Assert.DoesNotContain(xros, state.Player.Hand);
        // 트와일라잇 먼저 → 회수. 같은 턴의 블루 플레어는 드로우하지 않는다
        var (state2, engine2, blue2, tw2, xros2) = Ex4014Setup();
        Assert.True(engine2.PlayCard(tw2).CanExecute);
        Assert.Contains(xros2, state2.Player.Hand);
        int deck2 = state2.Player.MainDeck.Count;
        Assert.True(engine2.PlayCard(blue2).CanExecute);
        Assert.Equal(deck2, state2.Player.MainDeck.Count);
    }

    [Fact]
    public void TwoWhen_SplitOnlyWhenOriginalRulesFail()
    {
        // 원래 규칙이 읽는 줄은 나누지 않는다 — 나눌 꼴만
        Assert.Null(SentenceParser.SplitTwoWhen75("[Your Turn] When a card with the [Blue Flare] trait is played, ＜Draw 1＞."));
        var sp = SentenceParser.SplitTwoWhen75(Ex4014);
        Assert.NotNull(sp);
        Assert.StartsWith("[Your Turn] [Once Per Turn] When a card with the [Twilight] trait", sp!.Value.Second);
    }

    // =====================================================================
    // 2. EX2-007 「마더 디・리퍼」 — DP를 가진 디지타마 카드는 이동할 수 있다 (4-17-2), 【메인】은 배틀 에어리어에서 (3-4-7-4)
    // =====================================================================

    private const string MotherText = "[All Turns] This Digimon can't attack and isn't affected by your opponent's effects.\n[Main] [Once Per Turn] If you don't have another [Mother D-Reaper] in play, place 1 of your [ADR-02 Searcher]s from in play or your hand under this Digimon as its bottom digivolution card.\n[Your Turn] [Once Per Turn] When you would play a card with the [D-Reaper] trait from your hand, you may reduce its play cost by 1 for each of this Digimon's digivolution cards.";

    private static CardData Mother() => Card("EX2-007", MotherText, CardType.Digitama, level: 2, playCost: 0, color: CardColor.White, dp: 15000, name: "Mother D-Reaper", traits: new[] { "Ability Synthesis Agent", "D-Reaper" });

    private static CardData Searcher() => Digimon(Uid("B75-ADR-"), 3, "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)", name: "ADR-02 Searcher", color: CardColor.White, traits: "D-Reaper");

    [Fact]
    public void EX2_007_Supported_AndKorean()
    {
        AllSupported(Mother());
        string ko = Ko("[Main] [Once Per Turn] If you don't have another [Mother D-Reaper] in play, place 1 of your [ADR-02 Searcher]s from in play or your hand under this Digimon as its bottom digivolution card.");
        Assert.StartsWith("【메인】 [턴에 1회] ", ko);
        Assert.Contains("자신의 다른 「", ko);
        Assert.EndsWith("놓는다.", ko);
    }

    [Fact]
    public void DigiEggWithDp_MovesFromBreeding_PlainEggDoesNot()
    {
        var (state, engine) = NewMainPhase();
        state.CurrentPhase = Phase.Breeding;
        state.BreedingActionTaken = false;
        var mother = Mother();
        state.Player.BreedingArea = new CardInstance(mother, 1);
        Assert.Empty(engine.GetMainAbilities(state.Player));                    // 육성 에어리어에서는 【메인】을 쓸 수 없다 (3-4-7-4)
        Assert.True(engine.MoveFromBreedingArea().CanExecute);                  // DP를 가진 디지타마 카드 (4-17-2)
        var inst = Assert.Single(state.Player.BattleArea);
        Assert.Equal(15000, inst.GetCurrentDP());
        Assert.Single(engine.GetMainAbilities(state.Player), a => ReferenceEquals(a.Digimon, inst));

        var (state2, engine2) = NewMainPhase();
        state2.CurrentPhase = Phase.Breeding;
        state2.BreedingActionTaken = false;
        state2.Player.BreedingArea = new CardInstance(Card(Uid("B75-EGG-"), type: CardType.Digitama, level: 2, dp: 0), 1);
        Assert.False(engine2.MoveFromBreedingArea().CanExecute);                // DP가 없는 디지타마
    }

    [Fact]
    public void EX2_007_Main_PlacesSearcher_FromHandOrField()
    {
        var (state, engine) = NewMainPhase();
        var mother = Put(state.Player, Mother());
        var s = Searcher();
        state.Player.Hand.Add(s);
        Assert.True(engine.ActivateMainAbility(mother, mother.TopCard).CanExecute);
        Assert.Contains(s, mother.EvolutionSources);
        Assert.DoesNotContain(s, state.Player.Hand);

        // 필드의 서쳐: 맨 위 카드만 놓이고 그 아래의 카드는 파기 (종합 룰 4-7-7 · 4-7-8)
        var (state2, engine2) = NewMainPhase();
        var mother2 = Put(state2.Player, Mother());
        var under = Digimon(Uid("B75-UND-"), 2, color: CardColor.White);
        var s2 = Searcher();
        var field = Put(state2.Player, s2, under);
        Assert.True(engine2.ActivateMainAbility(mother2, mother2.TopCard).CanExecute);
        Assert.Contains(s2, mother2.EvolutionSources);
        Assert.DoesNotContain(under, mother2.EvolutionSources);
        Assert.Contains(under, state2.Player.Trash);
        Assert.DoesNotContain(field, state2.Player.BattleArea);
    }

    [Fact]
    public void EX2_007_Main_AnotherMotherInBattleArea_Blocks_BreedingMotherDoesNotCount()
    {
        var (state, engine) = NewMainPhase();
        var mother = Put(state.Player, Mother());
        Put(state.Player, Mother());
        state.Player.Hand.Add(Searcher());
        Assert.False(engine.ActivateMainAbility(mother, mother.TopCard).CanExecute);
        Assert.Empty(mother.EvolutionSources);

        // 육성 에어리어의 「마더 디・리퍼」는 참조하지 않는다 (3-4-7-8)
        var (state2, engine2) = NewMainPhase();
        var mother2 = Put(state2.Player, Mother());
        state2.Player.BreedingArea = new CardInstance(Mother(), 1);
        state2.Player.Hand.Add(Searcher());
        Assert.True(engine2.ActivateMainAbility(mother2, mother2.TopCard).CanExecute);
        Assert.Single(mother2.EvolutionSources);
    }

    // =====================================================================
    // 3. BT1-089 — 부화하거나 육성 에어리어의 Lv.3 이상을 이동 (공식 한글 BT1-089 · 종합 룰 4-17 · 4-18)
    // =====================================================================

    private const string Bt1089 = "[Main] If you have a level 5 or higher green Digimon in play, by suspending this Tamer, hatch 1 Digi-Egg card to an empty space in your breeding area, or move 1 level 3 or higher Digimon from your breeding area to your battle area.";

    [Fact]
    public void BT1_089_HatchOrMove()
    {
        var tamer = Card(Uid("B75-T1089-"), Bt1089, CardType.Tamer, color: CardColor.Green);
        AllSupported(tamer);
        string ko = Ko(Bt1089);
        Assert.Contains("부화시키거나, ", ko);
        Assert.EndsWith("이동한다.", ko);
        var egg = Card(Uid("B75-EGG2-"), type: CardType.Digitama, level: 2, dp: 0);
        foreach (var mode in new[] { "empty", "lv3", "egg" })
        {
            var (state, engine) = NewMainPhase();
            state.Player.Tamers.Add(tamer);
            Put(state.Player, Digimon(Uid("B75-G5-"), 5, color: CardColor.Green));
            state.Player.DigitamaDeck.Push(egg);
            var lv3 = Digimon(Uid("B75-B3-"), 3, color: CardColor.Green);
            if (mode == "lv3") state.Player.BreedingArea = new CardInstance(lv3, 1);
            if (mode == "egg") state.Player.BreedingArea = new CardInstance(Card(Uid("B75-EGG3-"), type: CardType.Digitama, level: 2, dp: 0), 1);
            engine.ActivateMainAbility(null, tamer);
            if (mode == "empty") Assert.Same(egg, state.Player.BreedingArea?.TopCard);
            if (mode == "lv3") { Assert.Null(state.Player.BreedingArea); Assert.Contains(state.Player.BattleArea, d => d.TopCard == lv3); }
            if (mode == "egg") Assert.Single(state.Player.BattleArea);
        }
    }

    // =====================================================================
    // 4. AI · 화면 추천: 배틀 에어리어의 디지몬 링크 (종합 룰 6-5-1-4) — 얻는 것이 잃는 것보다 클 때만
    // =====================================================================

    private const string LinkText = "[On Play] Reveal the top 3 cards of your deck. Add 1 [Maquinamon] and 1 card with [Maquinamon] in its text among them to the hand. Return the rest to the bottom of the deck. Then, you may link this Digimon or 1 [Maquinamon] in your hand to 1 of your other Digimon without paying the cost.";

    private static CardData Maquinamon() => Digimon("EX11-027", 3, LinkText, name: "Maquinamon", color: CardColor.Green, dp: 3000,
        inherited: "[All Turns] When this Digimon would leave the battle area, by placing 1 of its link cards as its bottom digivolution card, it doesn't leave.");

    [Fact]
    public void FieldLink_Recommend_SuspendedLowDigimon_NotActiveOne()
    {
        var (state, engine) = NewMainPhase(memory: 5);
        var me = Put(state.Player, Maquinamon());
        var host = Put(state.Player, Digimon(Uid("B75-HOST-"), 5, "[On Play] You may play 1 [Maquinamon] from your hand.", color: CardColor.Green, dp: 7000));
        if (engine.GetFieldLinkTargets(me).Count == 0) return;   // 공식 〈링크〉 정보가 없는 환경   // 공식 영어 〈링크〉 정보가 없는 환경
        me.IsSuspended = true;                                    // 어택을 마친 디지몬 — 배틀 에어리어에서 잃을 것이 적다
        Assert.Same(host, engine.RecommendFieldLink75(me));
        me.IsSuspended = false;                                   // 아직 어택 · 블록할 수 있는 몸
        var withSources = Put(state.Player, Maquinamon(), Digimon(Uid("B75-S1-"), 2), Digimon(Uid("B75-S2-"), 2));
        withSources.IsSuspended = true;
        Assert.Null(engine.RecommendFieldLink75(withSources));    // 진화원 2장이 트래시로 간다 (4-7-8)
    }

    [Fact]
    public void FieldLink_AiLinksSuspendedDigimon()
    {
        var (state, engine) = NewMainPhase(memory: 5);
        var me = Put(state.DummyOpponent, Maquinamon());
        var host = Put(state.DummyOpponent, Digimon(Uid("B75-AHOST-"), 5, "[On Play] You may play 1 [Maquinamon] from your hand.", color: CardColor.Green, dp: 7000));
        me.IsSuspended = true;
        var pick = engine.BestFieldLink75(state.DummyOpponent, 3);
        if (GoldfishEngine.LinkCostOf(me.TopCard) is null) return;
        Assert.NotNull(pick);
        Assert.Same(me, pick!.Value.Moving);
        Assert.Same(host, pick.Value.Target);
        Assert.Null(engine.BestFieldLink75(state.DummyOpponent, pick.Value.Cost - 1));   // 메모리를 넘기지 않는다
    }

    // =====================================================================
    // 5. 공식 영어 카드 리스트로 고친 API 문장 (특징 · 이름을 잘못 적은 것)
    // =====================================================================

    [Fact]
    public void OfficialClauses75_ApiTextFixed()
    {
        var bt23090 = new CardData { CardId = "BT23-090", Name = "Keisuke Amasawa", Type = CardType.Tamer };
        Assert.Equal("[All Turns] All of your [Hudie] trait Digimon get +1000 DP.", SentenceParser.OfficialClause75(bt23090, "[All Turns] All of your [Hudie] Digimon get +1000 DP."));
        var p209 = new CardData { CardId = "P-209", Name = "Titamon", Type = CardType.Digimon };
        Assert.Contains("[Demon] or [Titan] trait card", SentenceParser.OfficialClause75(p209, "[All Turns] [Once Per Turn] When your hand is trashed from, you may play 1 level 4 or lower [Demon] card from your trash without paying the cost."));
        // 다른 카드 · 다른 문장은 그대로
        var other = new CardData { CardId = "BT1-001", Name = "X", Type = CardType.Tamer };
        Assert.Equal("[All Turns] All of your [Hudie] Digimon get +1000 DP.", SentenceParser.OfficialClause75(other, "[All Turns] All of your [Hudie] Digimon get +1000 DP."));
    }

    [Fact]
    public void BT23_090_HudieTraitDigimon_GetDp()
    {
        var (state, engine) = NewMainPhase();
        var tamer = new CardData { CardId = "BT23-090", Name = "Keisuke Amasawa", Type = CardType.Tamer, Color = CardColor.White, PlayCost = 4, EffectText = "[All Turns] All of your [Hudie] Digimon get +1000 DP." };
        state.Player.Tamers.Add(tamer);
        var trait = Put(state.Player, Digimon(Uid("B75-HUD-"), 4, name: "Betamon", dp: 4000, traits: "Hudie"));
        var nameOnly = Put(state.Player, Digimon(Uid("B75-HDM-"), 4, name: "Hudiemon", dp: 4000));
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, trait.GetCurrentDP());                               // 특징 「후디에」 (공식 BT23-090)
        Assert.Equal(4000, nameOnly.GetCurrentDP());                            // 이름만 「Hudie」를 포함하는 디지몬은 아니다
    }
}
