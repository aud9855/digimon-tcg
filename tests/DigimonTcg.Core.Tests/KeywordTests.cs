using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>효과 확장 2단계 — 키워드 사전(API 카드 영어 문장)과 키워드 규칙</summary>
public class KeywordTests
{
    private static Dictionary<string, CardData> Cards() => StarterDecks.GetAllCatalogCards().ToDictionary(c => c.CardId);

    /// <summary>API 카드처럼 영어 효과 문장을 가진 카드 (카드 번호는 테스트마다 달라야 한다 — 자동 스크립트가 번호로 캐시된다)</summary>
    private static CardData Api(string id, int level, int dp, string main = "", string inherited = "",
        CardColor color = CardColor.Red, CardType type = CardType.Digimon, int playCost = 3, int evoCost = 1)
    {
        var card = new CardData
        {
            CardId = id, Name = id, Type = type, Color = color, Level = level, DP = dp, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited,
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static (GameState State, GoldfishEngine Engine, Dictionary<string, CardData> Cards) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, MemoryGauge = 3, TurnSequence = 3 };
        return (state, new GoldfishEngine(state), Cards());
    }

    // ===== 키워드 사전 =====

    [Fact]
    public void DataKeywords_ShouldReadStandaloneKeywordLinesAnywhere()
    {
        var card = Api("KW-DATA-1", 4, 5000,
            main: "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)\n＜Blocker＞ (This Digimon can block in the blocker timing.)\n＜Security A. -1＞ (This Digimon checks 1 fewer security card.)",
            inherited: "＜Security A. +1＞ (This Digimon checks 1 additional security card.)");
        Assert.True(card.IsBlocker);                         // 두 번째 줄의 키워드만 있는 절
        Assert.Equal(-1, card.SecurityAttackBonus);
        Assert.Equal(1, card.InheritedSecurityAttackBonus);

        var conditional = Api("KW-DATA-2", 4, 5000, main: "[Your Turn] This Digimon gains ＜Blocker＞.");
        Assert.False(conditional.IsBlocker);                 // 조건부로 얻는 키워드는 문장 파서(3단계) 대상
    }

    [Fact]
    public void Analyze_ShouldMarkKeywordClausesSupportedAndSentencesUnsupported()
    {
        var card = Api("KW-ANALYZE", 5, 7000,
            main: "＜Rush＞ ＜Piercing＞ (This Digimon can attack the turn it comes into play.)\n[On Play] [When Digivolving] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card.)\n[Your Turn] When a purple card is placed under this Tamer, gain 1 memory.");   // v0.31.0: 앞의 예("attack targets change or effects trash cards from under your Tamers")는 지원됨
        var clauses = KeywordDictionary.Analyze(card);

        Assert.Equal(3, clauses.Count);
        Assert.Equal(ClauseSupport.Supported, clauses[0].Support);
        Assert.Equal(ClauseSupport.Supported, clauses[1].Support);
        Assert.Equal(ClauseSupport.Unsupported, clauses[2].Support);
        var script = CardScripts.For(card)!;
        Assert.Contains(Keyword.Rush, script.Keywords);
        Assert.Contains(Keyword.Piercing, script.Keywords);
        Assert.Equal(2, script.Effects.Count);   // 등장 시 + 진화 시
    }

    // ===== 동작 키워드 (드로우 · 리커버리 · 퇴화) =====

    [Fact]
    public void GeneratedOnPlayDraw_ShouldDrawCards()
    {
        var (state, engine, cards) = NewMainPhase();
        var drawer = Api("KW-DRAW", 3, 3000, main: "[On Play] ＜Draw 2＞ (Draw 2 cards from your deck.)");
        state.Player.Hand.Add(drawer);
        for (int i = 0; i < 3; i++) state.Player.MainDeck.Add(cards["ST1-02"]);

        Assert.True(engine.PlayCard(drawer).CanExecute);

        Assert.Equal(2, state.Player.Hand.Count);
        Assert.Single(state.Player.MainDeck);
    }

    [Fact]
    public void GeneratedOptionDeDigivolveAll_ShouldHitEveryOpponentDigimon()
    {
        var (state, engine, cards) = NewMainPhase();
        state.Player.Tamers.Add(cards["ST1-12"]);   // 옵션 색 조건 (레드)
        var option = Api("KW-DEDIGI-ALL", 0, 0, main: "[Main] ＜De-Digivolve 1＞ all of your opponent's Digimon. (Trash the top card.)", type: CardType.Option, playCost: 2);
        state.Player.Hand.Add(option);
        var a = new CardInstance(cards["ST2-03"], 1); a.Evolve(cards["ST2-06"]);
        var b = new CardInstance(cards["ST1-02"], 1); b.Evolve(cards["ST1-05"]);
        state.DummyOpponent.BattleArea.AddRange(new[] { a, b });

        Assert.True(engine.PlayCard(option).CanExecute);

        Assert.Equal("ST2-03", a.TopCard.CardId);
        Assert.Equal("ST1-02", b.TopCard.CardId);
    }

    [Fact]
    public void SecurityDigimonWithSecurityEffect_ShouldActivateBeforeBattle()
    {
        var (state, engine, cards) = NewMainPhase();
        var attacker = new CardInstance(cards["ST1-06"], 1);   // DP 6000
        state.Player.BattleArea.Add(attacker);
        var secDigimon = Api("KW-SECDIGI", 3, 2000, main: "[Security] ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)");
        state.DummyOpponent.SecurityStack.Add(secDigimon);
        state.DummyOpponent.MainDeck.Add(cards["ST1-02"]);

        engine.AttackSecurityOrDirect(attacker);

        Assert.Single(state.DummyOpponent.SecurityStack);   // 1장 체크 + ≪리커버리 +1≫
        Assert.Contains(attacker, state.Player.BattleArea);  // 6000 > 2000
    }

    // ===== 어택 키워드 =====

    [Fact]
    public void Rush_ShouldAttackTheTurnItIsPlayed()
    {
        var (state, _, _) = NewMainPhase();
        var rusher = new CardInstance(Api("KW-RUSH", 4, 5000, main: "＜Rush＞ (This Digimon can attack the turn it comes into play.)"), currentTurn: 2);
        var normal = new CardInstance(Api("KW-NORUSH", 4, 5000), currentTurn: 2);
        state.Player.BattleArea.AddRange(new[] { rusher, normal });

        Assert.True(ActionValidator.ValidateAttack(state, rusher, null).CanExecute);
        Assert.False(ActionValidator.ValidateAttack(state, normal, null).CanExecute);
    }

    [Fact]
    public void Raid_ShouldSwitchSecurityAttackToStrongestActiveDigimon()
    {
        var (state, engine, cards) = NewMainPhase();
        var raider = new CardInstance(Api("KW-RAID", 6, 11000, main: "＜Raid＞ (When this Digimon attacks, you may switch the target of attack to 1 of your opponent's unsuspended Digimon with the highest DP.)"), 1);
        state.Player.BattleArea.Add(raider);
        var enemy = new CardInstance(cards["ST2-08"], 1);   // Lv.5 DP 7000, 액티브
        state.DummyOpponent.BattleArea.Add(enemy);
        state.DummyOpponent.SecurityStack.Add(cards["ST1-02"]);

        engine.AttackSecurityOrDirect(raider);

        Assert.DoesNotContain(enemy, state.DummyOpponent.BattleArea);
        Assert.Single(state.DummyOpponent.SecurityStack);   // 시큐리티는 체크하지 않았다
    }

    [Fact]
    public void Alliance_ShouldAddRestedAllyDpAndOneSecurityCheckForTheAttack()
    {
        var (state, engine, cards) = NewMainPhase();
        var leader = new CardInstance(Api("KW-ALLIANCE", 4, 5000, main: "＜Alliance＞ (When this Digimon attacks, by suspending 1 of your other Digimon, add the suspended Digimon's DP to this Digimon and it gains ＜Security A. +1＞ for the attack.)"), 1);
        var ally = new CardInstance(cards["ST1-04"], currentTurn: 2);   // 이번 턴 등장 (어택 불가) DP 4000
        state.Player.BattleArea.AddRange(new[] { leader, ally });
        for (int i = 0; i < 3; i++) state.DummyOpponent.SecurityStack.Add(cards["ST2-08"]);   // DP 7000

        engine.AttackSecurityOrDirect(leader);

        Assert.True(ally.IsSuspended);
        Assert.Single(state.DummyOpponent.SecurityStack);    // 9000 > 7000 으로 2장 체크
        Assert.Contains(leader, state.Player.BattleArea);
        Assert.Equal(5000, leader.GetCurrentDP());           // "이 어택 동안"만
    }

    [Fact]
    public void Blitz_ShouldLetTheDigimonAttackOnceAfterMemoryGoesToOpponent()
    {
        var (state, engine, cards) = NewMainPhase();
        state.MemoryGauge = 1;
        var baseDigimon = new CardInstance(cards["ST1-07"], currentTurn: 1);   // 그레이몬 Lv.4 레드
        state.Player.BattleArea.Add(baseDigimon);
        var blitzEvo = Api("KW-BLITZ", 5, 8000, main: "[When Digivolving] ＜Blitz＞ (If your opponent has 1 or more memory, this Digimon may attack.)", evoCost: 3);
        state.Player.Hand.Add(blitzEvo);
        state.Player.Hand.Add(cards["ST1-02"]);
        state.Player.MainDeck.Add(cards["ST1-02"]);
        for (int i = 0; i < 3; i++) state.DummyOpponent.SecurityStack.Add(cards["ST1-02"]);

        engine.EvolveDigimon(baseDigimon, blitzEvo);   // 메모리 1 → -2

        Assert.Equal(2, state.CurrentTurn);            // 턴이 끝나지 않았다
        Assert.Same(baseDigimon, state.BlitzAttacker);
        Assert.False(engine.PlayCard(cards["ST1-02"]).CanExecute);   // ≪진격≫ 어택 · 턴 종료만

        Assert.True(engine.AttackSecurityOrDirect(baseDigimon).CanExecute);
        Assert.Single(state.DummyOpponent.SecurityStack);   // 그레이몬 진화원 ≪시큐리티 어택 +1≫ → 2장 체크
        Assert.Equal(3, state.CurrentTurn);            // 어택이 끝나 턴이 넘어갔다
        Assert.Null(state.BlitzAttacker);
    }

    // ===== 소멸을 막는 키워드 =====

    [Fact]
    public void Barrier_ShouldTrashSecurityInsteadOfBeingDeletedInBattle()
    {
        var (state, engine, cards) = NewMainPhase();
        var barrier = new CardInstance(Api("KW-BARRIER", 4, 4000, main: "＜Barrier＞ (When this Digimon would be deleted in battle, by trashing the top card of your security stack, prevent that deletion.)"), 1);
        state.Player.BattleArea.Add(barrier);
        for (int i = 0; i < 3; i++) state.Player.SecurityStack.Add(cards["ST1-02"]);
        var enemy = new CardInstance(cards["ST2-08"], 1) { IsSuspended = true };   // DP 7000
        state.DummyOpponent.BattleArea.Add(enemy);

        engine.AttackOpponentDigimon(barrier, enemy);

        Assert.Contains(barrier, state.Player.BattleArea);
        Assert.Equal(2, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void Evade_ShouldSuspendInsteadOfBeingDeletedByEffect()
    {
        var (state, engine, cards) = NewMainPhase();
        state.MemoryGauge = 10;
        state.Player.Tamers.Add(cards["ST1-12"]);   // 옵션 색 조건 (레드)
        var evader = new CardInstance(Api("KW-EVADE", 5, 7000, main: "＜Evade＞ (When this Digimon would be deleted, you may suspend it to prevent that deletion.)"), 1);
        state.DummyOpponent.BattleArea.Add(evader);
        state.Player.Hand.Add(cards["ST1-16"]);     // 가이아 포스: 상대 디지몬 1마리 소멸

        engine.PlayCard(cards["ST1-16"], evader);

        Assert.Contains(evader, state.DummyOpponent.BattleArea);
        Assert.True(evader.IsSuspended);
    }

    [Fact]
    public void AiRaid_ShouldAttackMyStrongestActiveDigimonInsteadOfSecurity()
    {
        var (state, engine, cards) = NewMainPhase();
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        var raider = new CardInstance(Api("KW-AIRAID", 6, 11000, main: "＜Raid＞ (When this Digimon attacks, you may switch the target of attack to 1 of your opponent's unsuspended Digimon with the highest DP.)"), 1);
        state.DummyOpponent.BattleArea.Add(raider);
        state.DummyOpponent.MainDeck.Add(cards["ST1-16"]);
        var mine = new CardInstance(cards["ST2-08"], 1);   // Lv.5 DP 7000, 액티브
        state.Player.BattleArea.Add(mine);
        for (int i = 0; i < 2; i++) state.Player.SecurityStack.Add(cards["ST1-02"]);
        state.Player.MainDeck.Add(cards["ST1-02"]);

        engine.PassTurn();

        Assert.DoesNotContain(mine, state.Player.BattleArea);
        Assert.Equal(2, state.Player.SecurityStack.Count);
    }
}
