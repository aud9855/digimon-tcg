using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.29.0 미지원 문장 3차 묶음: 진화원의 [메인] (맨 위 카드를 진화원 아래로) · 듀얼 카드 옵션 쪽 · ≪오버클럭≫ · 토큰 또는 [퍼펫형] ·
/// ≪급습≫ · ≪천승≫ · 진화할 수 없다 · 이 카드가 시큐리티 · 덱 · 패에서 파기되었을 때 · 효과로 패가 늘어났을 때
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch29Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", bool dual = false, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(), IsDual = dual,
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", CardColor color = CardColor.Blue, int dp = -1, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, color: color, dp: dp, inherited: inherited, traits: traits);

    /// <summary>메인 페이즈 (메모리 8) — 블루 테이머 1명 (옵션 색 조건)</summary>
    private static (GameState State, GoldfishEngine Engine) NewMainPhase(bool tamer = true)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B29-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B29-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B29-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B29-OSEC" + i, 3));
        }
        if (tamer) state.Player.Tamers.Add(Card("B29-TAMER", "", CardType.Tamer));
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, int turnPlayed = 1, params CardData[] sources)
    {
        var d = new CardInstance(card, turnPlayed) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    /// <summary>플레이어가 패에서 옵션을 쓴다 (문장이 모두 지원되는지 확인)</summary>
    private static void Use(GameState state, GoldfishEngine engine, CardData option)
    {
        AssertFull(option);
        state.Player.Hand.Add(option);
        var result = engine.PlayCard(option);
        Assert.True(result.CanExecute, result.Reason);
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    // =====================================================================
    // 진화원의 [메인] — 맨 위 카드를 진화원 아래로 (공식 Q&A EX5-007 · EX5-016)
    // =====================================================================

    private const string CoronamonInherited = "[Main] [Once Per Turn] By placing the top card of this Digimon with the [Light Fang] or [Night Claw] trait as this Digimon's bottom digivolution card, gain 2 memory.";

    [Fact]
    public void InheritedMain_PlacesTopCardUnderAndBecomesLevel3_QnaEX5007()
    {
        var (state, engine) = NewMainPhase();
        var corona = Digimon("B29-CORONA", 3, inherited: CoronamonInherited, traits: "Light Fang");
        AssertFull(corona);
        var lv4 = Digimon("B29-FIRAMON", 4, traits: "Light Fang");
        var d = Put(state.Player, lv4, sources: corona);

        var ability = Assert.Single(engine.GetMainAbilities(state.Player), a => a.Inherited);
        Assert.Same(d, ability.Digimon);
        Assert.Same(corona, ability.Card);
        Assert.True(engine.ActivateMainAbility(d, corona).CanExecute);

        // Q&A EX5-007: 이 카드 1장만 진화원인 Lv.4 → Lv.4 카드를 이 카드 밑에 → Lv.4를 진화원으로 가진 Lv.3
        Assert.Same(corona, d.TopCard);
        Assert.Equal(new[] { lv4 }, d.GetEvolutionStackBottomUp());
        Assert.Equal(10, state.MemoryGauge);
        // [턴에 1회] — 같은 디지몬의 진화원 [메인]은 다시 쓸 수 없다 (이 카드는 이제 맨 위라 진화원 효과도 아님)
        Assert.DoesNotContain(engine.GetMainAbilities(state.Player), a => a.Inherited);
    }

    [Fact]
    public void InheritedMain_TraitConditionNotMet_FailsAndIsNotUsed()
    {
        var (state, engine) = NewMainPhase();
        var corona = Digimon("B29-CORONA2", 3, inherited: CoronamonInherited, traits: "Light Fang");
        var d = Put(state.Player, Digimon("B29-NOTRAIT", 4, traits: "Dragon"), sources: corona);

        var result = engine.ActivateMainAbility(d, corona);

        Assert.False(result.CanExecute);
        Assert.Equal("B29-NOTRAIT", d.TopCard.CardId);
        Assert.Equal(8, state.MemoryGauge);
        Assert.Contains(engine.GetMainAbilities(state.Player), a => a.Inherited);   // 쓰지 않았으므로 그대로
    }

    [Fact]
    public void InheritedMain_MovingTopCardUnder_IsAnEffectPlacingADigivolutionCard()
    {
        // BT22-043 테리어몬: 메인 효과 "이 디지몬의 진화원에 특징 「CS」를 가진 디지몬 카드가 효과로 놓였을 때" + 진화원 [메인] "겹쳐져 있는 카드를 ~ 진화원 아래에"
        var (state, engine) = NewMainPhase();
        var terrier = Digimon("B29-TERRIER", 3,
            main: "[Your Turn] [Once Per Turn] When effects place Digimon cards with the [CS] trait in this Digimon's digivolution cards, gain 1 memory.",
            inherited: "[Main] [Once Per Turn] By placing this [CS] trait Digimon's top stacked card as its bottom digivolution card, ＜Draw 1＞ (Draw 1 card from your deck.)",
            traits: "CS");
        AssertFull(terrier);
        var top = Digimon("B29-GARGO", 4, traits: "CS");
        var d = Put(state.Player, top, sources: terrier);
        int hand = state.Player.Hand.Count;

        Assert.True(engine.ActivateMainAbility(d, terrier).CanExecute);

        Assert.Same(terrier, d.TopCard);
        Assert.Equal(hand + 1, state.Player.Hand.Count);   // ≪1 드로우≫
        Assert.Equal(9, state.MemoryGauge);                 // 맨 위가 된 테리어몬의 "진화원에 [CS] 디지몬 카드가 효과로 놓였을 때"
    }

    [Fact]
    public void InheritedMain_IsNotOfferedInTheBreedingArea_OrOnOptionSources()
    {
        var (state, engine) = NewMainPhase();
        state.Player.BreedingArea = new CardInstance(Digimon("B29-BREED-TOP", 4, traits: "Light Fang"), 1);
        state.Player.BreedingArea.AddBottomSource(Digimon("B29-BREED-SRC", 3, inherited: CoronamonInherited));

        Assert.DoesNotContain(engine.GetMainAbilities(state.Player), a => a.Inherited);
    }

    // =====================================================================
    // 듀얼 카드 (종합 룰 4-6): 패에서 옵션 쪽으로 사용 · 디지몬 쪽으로 진화 · 등장하지 않는다
    // =====================================================================

    private static CardData Dual(string id, string optionSide, string main = "", int useCost = 3, CardColor color = CardColor.Yellow, params string[] traits) =>
        Card(id, main, CardType.Digimon, 6, useCost, color, 12000, inherited: optionSide, dual: true, traits: traits);

    [Fact]
    public void Dual_FromHand_IsUsedAsItsOptionSide_WithUseRequirement()
    {
        var (state, engine) = NewMainPhase();   // 블루 테이머 — 옐로 색 조건은 없지만 사용 조건(특징)으로 쓴다
        Put(state.Player, Digimon("B29-GD", 4, color: CardColor.Red, traits: "Glowing Dawn"));
        var enemy = Put(state.DummyOpponent, Digimon("B29-ENEMY", 5, dp: 7000));
        var dual = Dual("B29-DUAL", "Use Requirement: Glowing Dawn trait\r\n[Main] 1 of your opponent's Digimon gets -8000 DP for the turn.", useCost: 4);
        AssertFull(dual);
        state.Player.Hand.Add(dual);

        var result = engine.PlayCard(dual);

        Assert.True(result.CanExecute, result.Reason);
        Assert.Equal(4, state.MemoryGauge);                                    // 사용 코스트 4
        Assert.DoesNotContain(state.Player.BattleArea, d => d.TopCard == dual); // 디지몬으로 등장하지 않는다
        Assert.Contains(dual, state.Player.Trash);
        Assert.DoesNotContain(enemy, state.DummyOpponent.BattleArea);          // DP 0 → 소멸
    }

    [Fact]
    public void Dual_WithoutColorOrUseRequirement_CannotBeUsed()
    {
        var (state, engine) = NewMainPhase();   // 블루 테이머뿐 — 옐로 · 「Glowing Dawn」 없음
        var dual = Dual("B29-DUAL-NO", "Use Requirement: Glowing Dawn trait\r\n[Main] 1 of your opponent's Digimon gets -8000 DP for the turn.");
        state.Player.Hand.Add(dual);

        var result = engine.PlayCard(dual);

        Assert.False(result.CanExecute);
        Assert.Contains(dual, state.Player.Hand);
        Assert.Equal(8, state.MemoryGauge);
    }

    [Fact]
    public void Dual_CanStillDigivolve_AndItsOptionSideIsNotAnInheritedEffect()
    {
        var (state, engine) = NewMainPhase();
        var dual = Dual("B29-DUAL-EVO", "[Main] Gain 3 memory.");
        var baseD = Put(state.Player, Digimon("B29-BASE5", 5, color: CardColor.Yellow));
        state.Player.Hand.Add(dual);

        Assert.True(engine.EvolveDigimon(baseD, dual).CanExecute);
        Assert.Same(dual, baseD.TopCard);

        // 진화원이 된 듀얼 카드의 옵션 쪽 [Main]은 진화원 [메인] 효과가 아니다 (종합 룰 4-6-6)
        var host = Put(state.Player, Digimon("B29-HOST7", 7), sources: dual);
        Assert.DoesNotContain(engine.GetMainAbilities(state.Player), a => ReferenceEquals(a.Digimon, host));
        Assert.Null(CardScripts.For(dual)?.Effects.FirstOrDefault(e => e.Source == EffectSource.Inherited));
    }

    [Fact]
    public void Dual_DigimonSide_ActivatesItsOptionSideMain()
    {
        var (state, engine) = NewMainPhase();
        var dual = Dual("B29-DUAL-ACT", "[Main] Gain 3 memory.", main: "[When Digivolving] Activate 1 [Main] effect on this card's Option side.");
        AssertFull(dual);
        var baseD = Put(state.Player, Digimon("B29-BASE5B", 5, color: CardColor.Yellow));
        state.Player.Hand.Add(dual);

        Assert.True(engine.EvolveDigimon(baseD, dual).CanExecute);

        Assert.Equal(8 - 1 + 3, state.MemoryGauge);   // 진화 코스트 1, 옵션 쪽 [메인] 메모리 +3
    }

    [Fact]
    public void Dual_UseCostIncrease_ForEachSecurityCard()
    {
        var (state, engine) = NewMainPhase();
        var dual = Dual("B29-DUAL-COST", "For each of your security cards, add 1 to this card's use cost.\r\n[Main] Gain 1 memory.", useCost: 2, color: CardColor.Blue);
        AssertFull(dual);

        Assert.Equal(2 + 5, CardScripts.EffectivePlayCost(state.Player, dual, state.DummyOpponent));
    }

    [Fact]
    public void Ai_DoesNotPlayDualCardsAsDigimon()
    {
        var (state, engine) = NewMainPhase();
        state.DummyOpponent.Hand.Add(Dual("B29-AI-DUAL", "[Main] Gain 1 memory."));

        Assert.Null(new SmartAiPolicy().ChooseDigimonToPlay(engine, 10));
    }

    // =====================================================================
    // ≪오버클럭≫ (종합 룰 16-34) · 토큰 또는 [퍼펫형]
    // =====================================================================

    private const string Overclock = "＜Overclock ([Puppet] Trait)＞ (At the end of your turn, by deleting 1 of your Tokens or other [Puppet] trait Digimon, this Digimon attacks a player without suspending.)";

    [Fact]
    public void Overclock_DeletesATokenAndAttacksThePlayerWithoutSuspending()
    {
        var (state, engine) = NewMainPhase();
        var oc = Put(state.Player, Digimon("B29-OC", 5, main: Overclock, traits: "Puppet"));
        AssertFull(oc.TopCard);
        var token = Put(state.Player, new CardData { CardId = "TOKEN-B29", Name = "Familiar", Type = CardType.Digimon, Level = 0, DP = 3000 });
        int sec = state.DummyOpponent.SecurityStack.Count;

        engine.PassTurn();

        Assert.DoesNotContain(token, state.Player.BattleArea);
        Assert.Equal(sec - 1, state.DummyOpponent.SecurityStack.Count);
        Assert.False(oc.IsSuspended);   // 레스트하지 않고 어택
    }

    [Fact]
    public void Overclock_WithoutTokenOrSpecifiedDigimon_DoesNotAttack()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B29-OC2", 5, main: Overclock, traits: "Puppet"));
        var other = Put(state.Player, Digimon("B29-NOTPUPPET", 3, traits: "Dragon"));
        int sec = state.DummyOpponent.SecurityStack.Count;

        engine.PassTurn();

        Assert.Contains(other, state.Player.BattleArea);
        Assert.Equal(sec, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void TokensOrOtherPuppet_DeletionCost_PreventsLeaving()
    {
        var (state, engine) = NewMainPhase();
        var guard = Put(state.DummyOpponent, Digimon("B29-GUARDED", 5, dp: 7000, inherited: "", main:
            "[All Turns] [Once Per Turn] When this Digimon would leave the battle area other than by your effects, by deleting 1 of your Tokens or other [Puppet] trait Digimon, it doesn't leave."));
        AssertFull(guard.TopCard);
        var puppet = Put(state.DummyOpponent, Digimon("B29-PUPPET", 3, traits: "Puppet"));

        // 상대(이 디지몬의 주인에게는 "자신의 효과 이외")의 효과로 소멸 → 퍼펫형 디지몬을 대신 소멸 (AI는 추천대로 비용을 낸다)
        Use(state, engine, Card("B29-OPP-DEL", "[Main] Delete 1 of your opponent's Digimon with 6000 DP or more."));

        Assert.Contains(guard, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(puppet, state.DummyOpponent.BattleArea);
    }

    // =====================================================================
    // ≪급습≫ (16-44) · ≪천승≫ (16-43) · 진화할 수 없다
    // =====================================================================

    private const string Engage = "＜Engage＞ (At the end of your turn, this Digimon may attack.)";

    [Fact]
    public void Engage_AttacksAtTheEndOfTheTurn()
    {
        var (state, engine) = NewMainPhase();
        var eng = Put(state.Player, Digimon("B29-ENGAGE", 5, main: Engage));
        AssertFull(eng.TopCard);
        int sec = state.DummyOpponent.SecurityStack.Count;

        engine.PassTurn();

        Assert.Equal(sec - 1, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Engage_NeedsNormalAttackConditions_NotTheTurnItWasPlayed()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B29-ENGAGE-NEW", 5, main: Engage), turnPlayed: state.CurrentTurn);
        int sec = state.DummyOpponent.SecurityStack.Count;

        engine.PassTurn();

        Assert.Equal(sec, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Ascension_PlacesThisCardOnTopOfSecurityWhenDeleted()
    {
        var (state, engine) = NewMainPhase();
        var asc = Digimon("B29-ASC", 5, main: "＜Ascension＞ (When this Digimon is deleted, you may place this card as the top security card.)");
        AssertFull(asc);
        Put(state.DummyOpponent, asc, sources: Digimon("B29-ASC-SRC", 4));
        int sec = state.DummyOpponent.SecurityStack.Count;

        Use(state, engine, Card("B29-OPP-DEL-ALL", "[Main] Delete all of your opponent's Digimon."));

        Assert.Equal(sec + 1, state.DummyOpponent.SecurityStack.Count);
        Assert.Same(asc, state.DummyOpponent.SecurityStack[0]);
        Assert.DoesNotContain(asc, state.DummyOpponent.Trash);
        Assert.Contains(state.DummyOpponent.Trash, c => c.CardId == "B29-ASC-SRC");
    }

    [Fact]
    public void ThisDigimonCantDigivolve_BlocksDigivolutionFromHand()
    {
        var (state, engine) = NewMainPhase();
        var locked = Put(state.Player, Digimon("B29-LOCKED", 4, main: "[All Turns] This Digimon can't digivolve."));
        AssertFull(locked.TopCard);
        var evo = Digimon("B29-EVO5", 5);
        state.Player.Hand.Add(evo);
        engine.RecalculateFieldBuffs();

        Assert.False(engine.EvolveDigimon(locked, evo).CanExecute);
        Assert.Contains(evo, state.Player.Hand);
    }

    [Fact]
    public void CantDigivolveUntilTheirTurnEnds_LocksTheTarget()
    {
        var (state, engine) = NewMainPhase();
        var target = Put(state.DummyOpponent, Digimon("B29-OPP-LOCK", 4));
        Use(state, engine, Card("B29-LOCKER", "[Main] 1 of your opponent's Digimon can't digivolve until their turn ends."));

        Assert.True(EffectRules.IsDigivolveLocked(state, target));
    }

    // =====================================================================
    // 이 카드가 시큐리티 · 덱 · 패에서 파기되었을 때 (공식 Q&A BT13-098 · P-077 · BT10-080)
    // =====================================================================

    [Fact]
    public void ThisCardTrashedFromSecurityByEffect_Triggers()
    {
        var (state, engine) = NewMainPhase();
        var secCard = Card("B29-SEC-TRASH", "When effects trash this card from the security stack, gain 2 memory.");
        AssertFull(secCard);
        state.Player.SecurityStack.Insert(0, secCard);

        Use(state, engine, Card("B29-TRASH-SEC", "[Main] Trash your top security card."));

        Assert.Contains(secCard, state.Player.Trash);
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }

    [Fact]
    public void ThisCardTrashedFromSecurity_NotWhenCheckedByAnAttack_QnaBT13098()
    {
        var (state, engine) = NewMainPhase();
        var secCard = Card("B29-SEC-CHECK", "When effects trash this card from the security stack, gain 2 memory.");
        state.DummyOpponent.SecurityStack.Insert(0, secCard);
        var attacker = Put(state.Player, Digimon("B29-ATK", 5, dp: 9000));
        int memory = state.MemoryGauge;

        engine.AttackSecurityOrDirect(attacker);

        Assert.Contains(secCard, state.DummyOpponent.Trash);
        Assert.Equal(memory, state.MemoryGauge);   // 체크 뒤 파기는 "시큐리티에서 파기"가 아니다 (상대 카드라도 메모리 변화 없음)
    }

    [Fact]
    public void ThisCardTrashedFromDeck_ByMill_ButNotWhenRevealed_QnaP077()
    {
        var (state, engine) = NewMainPhase();
        var wiz = Card("B29-WIZ", "When this card is trashed from your deck, gain 1 memory.", CardType.Digimon, 3);
        AssertFull(wiz);
        state.Player.MainDeck.Insert(0, wiz);
        Use(state, engine, Card("B29-MILL", "[Main] Trash the top 2 cards of your deck."));
        Assert.Equal(8 - 3 + 1, state.MemoryGauge);

        var (state2, engine2) = NewMainPhase();
        var wiz2 = Card("B29-WIZ2", "When this card is trashed from your deck, gain 1 memory.", CardType.Digimon, 3);
        state2.Player.MainDeck.Insert(0, wiz2);
        Use(state2, engine2, Card("B29-REVEAL", "[Main] Reveal the top 3 cards of your deck. Add 1 Tamer card among them to the hand. Trash the rest."));
        Assert.Contains(wiz2, state2.Player.Trash);
        Assert.Equal(8 - 3, state2.MemoryGauge);   // 오픈한 카드가 파기된 것은 아님
    }

    [Fact]
    public void ThisCardTrashedFromHand_ByOneOfYourEffects()
    {
        var (state, engine) = NewMainPhase();
        var skull = Card("B29-SKULL", "When this card is trashed from your hand by one of your effects, gain 2 memory.", CardType.Digimon, 5);
        AssertFull(skull);
        state.Player.Hand.Add(skull);

        Use(state, engine, Card("B29-DISCARD", "[Main] Trash 1 card in your hand."));

        Assert.Contains(skull, state.Player.Trash);
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }

    [Fact]
    public void WhenAnEffectAddsACardToYourHand_DrawByEffectCounts()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B29-HANDADD", 4, main: "[Your Turn] [Once Per Turn] When an effect adds a card to your hand, gain 1 memory."));

        Use(state, engine, Card("B29-DRAW", "[Main] ＜Draw 1＞ (Draw 1 card from your deck.)"));

        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    // =====================================================================
    // 생성 한글 (공식 표기)
    // =====================================================================

    [Theory]
    [InlineData("[All Turns] This Digimon can't digivolve.", "【서로의 턴】 이 디지몬은 진화할 수 없다.")]
    [InlineData("When an effect trashes this card from the security stack, activate this card's [Security] effects.", "이 카드가 시큐리티에서 효과로 파기되었을 때, 이 카드의 【시큐리티】 효과를 발휘한다.")]
    [InlineData("When this card is trashed from your deck, gain 1 memory.", "이 카드가 덱에서 파기되었을 때, 메모리를 +1 한다.")]
    [InlineData("[Your Turn] [Once Per Turn] When an effect adds a card to your hand, you may unsuspend 1 of your Digimon.", "【자신의 턴】 [턴에 1회] 효과로 자신의 패가 늘어났을 때, 자신의 디지몬 1마리를 액티브로 할 수 있다.")]
    [InlineData("＜Engage＞ (At the end of your turn, this Digimon may attack.)", "《급습》 (자신의 턴 종료 시, 이 디지몬으로 어택할 수 있다)")]
    [InlineData("＜Ascension＞ (When this Digimon is deleted, you may place this card as the top security card.)", "《천승》 (이 디지몬이 소멸했을 때, 이 카드를 시큐리티 위에 놓을 수 있다)")]
    [InlineData("[Main] [Once Per Turn] By placing this [CS] trait Digimon's top stacked card as its bottom digivolution card, ＜Draw 1＞ (Draw 1 card from your deck.)",
        "【메인】 [턴에 1회] 특징으로 「CS」를 가진 이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 이 디지몬의 진화원 아래에 놓는 것으로, 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다).")]
    [InlineData("[When Digivolving] [When Attacking] [Once Per Turn] Activate 1 [Main] effect on this card's Option side.", "【진화 시】 【어택 시】 [턴에 1회] 이 카드의 옵션 쪽의 【메인】 효과를 1개 발휘한다.")]
    public void KoreanRender_NewSentences(string en, string ko) =>
        Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));

    [Fact]
    public void KoreanRender_OverclockAndTokens_OfficialWording()
    {
        Assert.Equal("《오버클럭《특징「퍼펫형」》》 (자신의 턴 종료 시, 자신의 토큰 또는 특징으로 「퍼펫형」을 가진 다른 자신의 디지몬 1마리를 소멸시키는 것으로, 이 디지몬으로 레스트시키지 않고 플레이어에게 어택한다)",
            KoreanEffectRenderer.RenderClause(Overclock));
        Assert.Contains("자신의 토큰 또는 특징으로 「퍼펫형」을 가진 다른 자신의 디지몬 1마리를 소멸시키는 것으로,",
            KoreanEffectRenderer.RenderClause("[All Turns] [Once Per Turn] When this Digimon would leave the battle area other than by your effects, by deleting 1 of your Tokens or other [Puppet] trait Digimon, it doesn't leave."));
    }
}
