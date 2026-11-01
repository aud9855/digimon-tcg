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
/// v0.74.0 미지원 문장 48차 묶음: 특징 포함 한글 통일 (문형 "특징으로 「X」를 포함하는" · 공식 부분 낱말) ·
/// 메인 페이즈의 배틀 에어리어 디지몬 링크 (종합 룰 6-5-1-4 · 10-1-3) · BT5-109 · EX1-071 · BT11-097 · BT23-060 · BT26-047 · EX8-043 · AD1-009 · BT23-033 · BT24-056
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch74Tests
{
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

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int security = 5)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B74-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B74-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B74-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B74-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    // 1. 특징 포함 문형 통일 — "특징에 「X」를 포함하는" · "를 (「Y」는 제외) 포함하는" → 공식 "특징으로 「X」를 포함하는" · "를 포함(「Y」는 제외)하는"
    // =====================================================================

    [Fact]
    public void Korean_ContainsWording_TokuchouDe_ExclusionInside()
    {
        string ko = Ko("[Opponent's Turn] All of your other Digimon with [Vegetation], [Plant] or [Fairy] in one of their traits gain \"[On Deletion] Gain 2 memory.\"");
        Assert.DoesNotContain("특징에 ", ko);
        Assert.Contains("특징으로 「식물형」/「요정형」을 포함하는 자신의 다른 디지몬 전부", ko);
        // 제외는 공식 BT13-048 "을 포함(「~」은 제외)하는"
        string ko2 = Ko("[When Digivolving] [When Attacking] [Once Per Turn] You may play 1 play cost 4 or lower Digimon card with [Beast], [Animal] or [Sovereign], other than [Sea Animal], in any of its traits from your hand without paying the cost.");
        Assert.Contains("특징으로 「수」/「짐승」을 포함(「수장룡형」/「수생형」/「수생포유류형」/「정보수집 타입」은 제외)하는", ko2);
        Assert.DoesNotContain(") 포함하는", ko2);
    }

    // =====================================================================
    // 2. 특징 포함의 공식 부분 낱말 (EX4-064 · BT13-094 · BT13-048 · BT19-082 · EX3-003 · EX3-028 · BT11-102 · BT18-054)
    // =====================================================================

    [Theory]
    [InlineData("[Main] Choose 1 of your Digimon with [Bird] in any of its traits. Gain 1 memory.", "특징으로 「조」/「새」/「병아리」를 포함하는")]
    [InlineData("[Start of Your Main Phase] If you have a Digimon with [Avian] or [Bird] in any of its traits in play, gain 1 memory.", "특징으로 「조」/「새」/「병아리」를 포함하는")]
    [InlineData("[On Play] [When Digivolving] You may place 1 level 5 or lower Digimon card with [Aqua] or [Sea Animal] in any of its traits from your hand as any of your Digimon's bottom digivolution card.", "특징으로 「수생」을 포함하는")]
    [InlineData("[When Attacking] Reveal the top 3 cards of your deck. Add 1 Digimon card with [Dragon], [saur] or [Ceratopsian] in any of its traits among them to your hand. Place the rest at the bottom of your deck in any order.", "특징으로 「용」/「룡」을 포함하는")]
    [InlineData("[Main] Choose 1 of your Digimon with [Insect] in any of its traits. Suspend 2 of your opponent's Digimon with DP less than or equal to the DP of the chosen Digimon.", "특징으로 「곤충형」을 포함하는")]
    [InlineData("[All Turns] When this Digimon would leave the battle area, you may play 1 level 4 or lower Digimon card with the [Hybrid] trait or [Avian], [Bird] or [Fairy] in any of its traits from this Digimon's digivolution cards without paying the cost.", "특징으로 「조」/「새」/「병아리」/「요정」을 포함하는")]
    public void Korean_ContainsGlossary_OfficialSubstrings(string clause, string expected)
    {
        string ko = Ko(clause);
        Assert.Contains(expected, ko);
    }

    [Fact]
    public void Korean_ExactTraitAndNames_Unchanged()
    {
        // "the [X] trait"(일치)은 그대로 "가진"
        string ko = Ko("[On Play] Reveal the top 3 cards of your deck. Add 1 card with the [Avian] trait among them to the hand. Return the rest to the bottom of the deck.");
        Assert.DoesNotContain("「조」", ko);
        Assert.Contains("가진 카드 1장", ko);
        // 명칭 포함은 그대로
        string ko2 = Ko("[On Play] You may return 1 card with [Greymon] in its name from your trash to the hand.");
        Assert.Contains("명칭에", ko2);
    }

    // =====================================================================
    // 3. 메인 페이즈 행동: 배틀 에어리어의 디지몬 링크 (종합 룰 6-5-1-4 · 10-1-1 · 10-1-3-2 코스트 · 4-9-4 · 4-7-8)
    // =====================================================================

    private const string LinkText = "[On Play] Reveal the top 3 cards of your deck. Add 1 [Maquinamon] and 1 card with [Maquinamon] in its text among them to the hand. Return the rest to the bottom of the deck. Then, you may link this Digimon or 1 [Maquinamon] in your hand to 1 of your other Digimon without paying the cost.";

    private static CardData Maquinamon() => Digimon("EX11-027", 3, LinkText, name: "Maquinamon", color: CardColor.Green,
        inherited: "[All Turns] When this Digimon would leave the battle area, by placing 1 of its link cards as its bottom digivolution card, it doesn't leave.");

    [Fact]
    public void LinkFromField_PaysCost_LeavesBattleArea_SourcesTrashed()
    {
        var (state, engine) = NewMainPhase(memory: 3);
        var me = Maquinamon();
        var src = Digimon(Uid("B74-SRC-"), 2, color: CardColor.Green);
        var self = Put(state.Player, me, src);
        var host = Put(state.Player, Digimon(Uid("B74-HOST-"), 4, "[On Play] You may play 1 [Maquinamon] from your hand.", color: CardColor.Green, dp: 5000));
        var plain = Put(state.Player, Digimon(Uid("B74-PLN-"), 4, color: CardColor.Green));
        var targets = engine.GetFieldLinkTargets(self);
        Assert.Contains(host, targets);
        Assert.DoesNotContain(plain, targets);                                  // 링크 조건 (10-1-3-1)
        Assert.Equal(2, engine.GetFieldLinkCost(self, host));
        Assert.True(engine.LinkFromField(self, host).CanExecute);
        Assert.Equal(1, state.MemoryGauge);                                     // 링크 코스트 2 (10-1-3-2)
        Assert.DoesNotContain(self, state.Player.BattleArea);                   // 링크 카드는 에어리어에 속하지 않는다 (4-9-4)
        Assert.Contains(me, host.LinkedCards);
        Assert.Contains(src, state.Player.Trash);                               // 겹쳐져 있던 카드는 파기 (4-7-8)
        Assert.Equal(7000, host.GetCurrentDP());                                // 링크 DP +2000
        // 링크 조건에 맞지 않는 디지몬에는 할 수 없다
        var (state2, engine2) = NewMainPhase();
        var self2 = Put(state2.Player, Maquinamon());
        var plain2 = Put(state2.Player, Digimon(Uid("B74-PLN2-"), 4, color: CardColor.Green));
        Assert.Empty(engine2.GetFieldLinkTargets(self2));
        Assert.False(engine2.LinkFromField(self2, plain2).CanExecute);
        Assert.Contains(self2, state2.Player.BattleArea);
    }

    [Fact]
    public void LinkFromField_OnlyInOwnMainPhase_AndOnlyLinkCards()
    {
        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, Maquinamon());
        var host = Put(state.Player, Digimon(Uid("B74-HOST3-"), 4, "[On Play] You may play 1 [Maquinamon] from your hand.", color: CardColor.Green));
        state.CurrentPhase = Phase.Breeding;
        Assert.Empty(engine.GetFieldLinkTargets(self));
        Assert.False(engine.LinkFromField(self, host).CanExecute);
        state.CurrentPhase = Phase.Main;
        // 링크 조건이 없는 카드(맨 위 카드)는 링크할 수 없다
        var plain = Put(state.Player, Digimon(Uid("B74-PLN3-"), 4, color: CardColor.Green));
        Assert.Empty(engine.GetFieldLinkTargets(plain));
        Assert.False(engine.LinkFromField(plain, host).CanExecute);
    }

    // =====================================================================
    // 4. BT5-109 — 이 효과로 Lv.6 → Lv.7 진화한 디지몬은 턴 종료 시 덱 아래로, 진화원은 파기 (공식 한글 BT5-109 · Q&A BT5-109 Q1)
    // =====================================================================

    private const string Bt5109 = "[Main] The next time one of your Digimon would digivolve from level 6 to level 7 this turn, reduce the cost of the digivolution by 6. At the end of the turn, return the Digimon that digivolved with this effect to the bottom of its owner's deck.";

    [Fact]
    public void BT5_109_DigivolvedWithThisEffect_ReturnsToDeckBottom_SourcesTrashed()
    {
        var opt = Card(Uid("B74-BT5109-"), Bt5109, color: CardColor.Red);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 8);
        var lv5 = Digimon(Uid("B74-L5-"), 5, color: CardColor.Red);
        var lv6 = Digimon(Uid("B74-L6-"), 6, color: CardColor.Red);
        var d = Put(state.Player, lv6, lv5);
        var lv7 = Digimon(Uid("B74-L7-"), 7, color: CardColor.Red, evoCost: 8);
        state.Player.Hand.Add(lv7);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.True(engine.EvolveDigimon(d, lv7).CanExecute);
        Assert.Equal(6, state.MemoryGauge);                                     // 8 - (8 - 6) (+ 진화 드로우는 메모리와 무관)
        engine.PassTurn();
        Assert.DoesNotContain(d, state.Player.BattleArea);
        Assert.Contains(lv7, state.Player.MainDeck);                            // 덱 아래로
        Assert.Contains(lv6, state.Player.Trash);                               // 그 디지몬이 갖는 진화원은 파기
        Assert.Contains(lv5, state.Player.Trash);
    }

    [Fact]
    public void BT5_109_OtherDigivolution_NotReturned()
    {
        var opt = Card(Uid("B74-BT5109b-"), Bt5109, color: CardColor.Red);
        var (state, engine) = NewMainPhase(memory: 8);
        var d = Put(state.Player, Digimon(Uid("B74-L4-"), 4, color: CardColor.Red));
        var lv5 = Digimon(Uid("B74-L5b-"), 5, color: CardColor.Red, evoCost: 2);
        state.Player.Hand.Add(lv5);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.True(engine.EvolveDigimon(d, lv5).CanExecute);
        Assert.Equal(6, state.MemoryGauge);                                     // Lv.4 → Lv.5는 감소 없음
        engine.PassTurn();
        Assert.DoesNotContain(lv5, state.Player.MainDeck);
    }

    // =====================================================================
    // 5. EX1-071 — 진화하는 디지몬(배틀 에어리어)과 같은 색의 패의 디지몬 카드를 버려 -4 (공식 Q&A EX1-071 Q1 · P-206 Q1)
    // =====================================================================

    [Theory]
    [InlineData(CardColor.Red, true)]
    [InlineData(CardColor.Blue, false)]
    public void EX1_071_TrashSameColorDigimon_Reduce4(CardColor handColor, bool reduced)
    {
        var opt = Card(Uid("B74-EX1071-"), "[Main] The next time one of your Digimon would digivolve this turn, you may trash 1 Digimon card in your hand of the same color as the digivolving Digimon to reduce the memory cost of the digivolution by 4.", color: CardColor.Yellow);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 8);
        var d = Put(state.Player, Digimon(Uid("B74-R4-"), 4, color: CardColor.Red));
        var into = Card(Uid("B74-W5-"), "", CardType.Digimon, 5, color: CardColor.White, evoCost: 5);
        into.EvoRequirementColor = CardColor.Red;
        var fodder = Digimon(Uid("B74-FOD-"), 3, color: handColor);
        state.Player.Hand.Add(into);
        state.Player.Hand.Add(fodder);
        state.Player.Hand.Add(Digimon(Uid("B74-KEEP-"), 3, color: CardColor.Purple));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.True(engine.EvolveDigimon(d, into).CanExecute);
        Assert.Equal(reduced ? 7 : 3, state.MemoryGauge);
        Assert.Equal(reduced, state.Player.Trash.Contains(fodder));
    }

    // =====================================================================
    // 6. BT11-097 · BT23-060 다른 카드의 효과를 발휘
    // =====================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BT11_097_ActivateOnDeletionOfRedVaccine_WithRedTamer(bool redTamer)
    {
        var opt = Card(Uid("B74-BT11097-"), "[Main] Delete 1 of your opponent's Digimon with 8000 DP or less. Then, if you have a red Tamer in play, activate 1 of the [On Deletion] effects of 1 of your red Digimon with the [Vaccine] trait.", color: CardColor.Red);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 3);
        Put(state.Player, Digimon(Uid("B74-VAC-"), 5, "[On Deletion] Gain 2 memory.", color: CardColor.Red, traits: "Vaccine"));
        var foe = Put(state.DummyOpponent, Digimon(Uid("B74-FOE-"), 5, color: CardColor.Blue, dp: 6000));
        foe.IsOwnersTurn = false;
        engine.AddTamer(state.Player, Card(Uid("B74-TAM-"), "", CardType.Tamer, color: redTamer ? CardColor.Red : CardColor.Blue));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
        Assert.Equal(redTamer ? 5 : 3, state.MemoryGauge);
    }

    [Fact]
    public void BT23_060_ActivateOnPlayOfFaceUpZaxonSecurity()
    {
        var me = Digimon(Uid("B74-BT23060-"), 6, "[When Attacking] [Once Per Turn] Activate 1 [On Play] effect on a face-up [Zaxon] trait Digimon card in your security stack as an effect of this Digimon.", color: CardColor.White);
        AllSupported(me);
        var (state, engine) = NewMainPhase(memory: 3);
        var d = Put(state.Player, me);
        var zax = Digimon(Uid("B74-ZAX-"), 4, "[On Play] Gain 1 memory.", color: CardColor.White, traits: "Zaxon");
        state.Player.SecurityStack.Insert(0, zax);
        state.Player.FaceUpSecurity.Add(zax);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, me);
        Assert.Equal(4, state.MemoryGauge);
        Assert.Contains(zax, state.Player.SecurityStack);                       // 카드는 시큐리티에 그대로
        // 뒷면이면 발휘하지 않는다
        var (state2, engine2) = NewMainPhase(memory: 3);
        var d2 = Put(state2.Player, me);
        state2.Player.SecurityStack.Insert(0, zax);
        engine2.RunEffects(EffectTrigger.WhenAttacking, state2.Player, d2, me);
        Assert.Equal(3, state2.MemoryGauge);
    }

    // =====================================================================
    // 7. 효과를 받지 않는 범위 — AD1-009 · EX8-043 · BT26-047
    // =====================================================================

    [Fact]
    public void AD1_009_ThisAndGarurumon_NotAffectedByOpponentDigimonEffects()
    {
        var me = Digimon(Uid("B74-AD1009-"), 7, "[On Play] [When Digivolving] ＜De-Digivolve 3＞ 1 of your opponent's Digimon. (Trash up to 3 cards from the top. You can't trash past level 3 cards.) Then, until your opponent's turn ends, their Digimon's effects don't affect this Digimon and 1 of your Digimon with [Garurumon] in its name.", name: "Omnimon Alter-S", color: CardColor.White);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var garu = Put(state.Player, Digimon(Uid("B74-GARU-"), 6, name: "MetalGarurumon", color: CardColor.Blue));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me, includeInherited: false);
        var foeCard = Digimon(Uid("B74-RET-"), 5, "[On Play] Return 1 of your opponent's Digimon to the hand.", color: CardColor.Blue);
        var foe = Put(state.DummyOpponent, foeCard);
        foe.IsOwnersTurn = false;
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, foeCard);
        Assert.Contains(d, state.Player.BattleArea);
        Assert.Contains(garu, state.Player.BattleArea);
    }

    [Fact]
    public void EX8_043_Suspended_DeDigivolve_ThenNotReturnedByOpponent()
    {
        var me = Digimon(Uid("B74-EX8043-"), 5, "[On Play] [When Digivolving] You may suspend 1 Digimon. Then, if this Digimon is suspended, ＜De-Digivolve 1＞ 1 of your opponent's Digimon and, until the end of your opponent's turn, their effects can't return this Digimon to hands or decks or affect it with ＜De-Digivolve＞ effects.", color: CardColor.Black);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        d.IsSuspended = true;
        var foeTop = Digimon(Uid("B74-FT-"), 5, "[On Play] Return 1 of your opponent's Digimon to the hand.", color: CardColor.Blue);
        var foeSrc = Digimon(Uid("B74-FS-"), 4, color: CardColor.Blue);
        var foe = Put(state.DummyOpponent, foeTop, foeSrc);
        foe.IsOwnersTurn = false;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me, includeInherited: false);
        Assert.Same(foeSrc, foe.TopCard);                                       // 《퇴화 1》
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, foeTop);
        Assert.Contains(d, state.Player.BattleArea);                            // 상대의 효과로 패로 되돌아가지 않는다
    }

    [Fact]
    public void BT26_047_SuspendedTraitDigimon_PlusDp()
    {
        var me = Digimon(Uid("B74-BT26047-"), 5, "[On Play] By suspending 1 Digimon, until your opponent's turn ends, none of your suspended [Insectoid] or [Titan] trait Digimon are affected by your opponent's Option effects, and they get +3000 DP.", color: CardColor.Green, traits: "Insectoid");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var bug = Put(state.Player, Digimon(Uid("B74-BUG-"), 4, color: CardColor.Green, dp: 4000, traits: "Insectoid"));
        bug.IsSuspended = true;
        var other = Put(state.Player, Digimon(Uid("B74-OTH-"), 4, color: CardColor.Green, dp: 4000, traits: "Beast"));
        other.IsSuspended = true;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, bug.GetCurrentDP());
        Assert.Equal(4000, other.GetCurrentDP());
        // 상대의 옵션 효과를 받지 않는다
        var foeOpt = Card(Uid("B74-FOPT-"), "[Main] Return 1 of your opponent's suspended Digimon to the hand.", color: CardColor.Blue);
        engine.RunEffects(EffectTrigger.Main, state.DummyOpponent, null, foeOpt, includeInherited: false);
        Assert.Contains(bug, state.Player.BattleArea);
    }

    // =====================================================================
    // 8. 공식 영어로 고친 문장 — BT23-033 (링크 효과 《퇴화》) · BT24-056 (뒤에 붙은 조각)
    // =====================================================================

    [Fact]
    public void OfficialEnglish_BT23_033_BT24_056()
    {
        var a = Digimon("BT23-033", 5, "＜Barrier＞ (When this Digimon would be deleted in battle, by trashing your top security card, it isn't deleted.)", name: "Beautymon", color: CardColor.Yellow,
            inherited: "Until your opponent's turn ends, their effects can't return this Digimon to hands or decks or affect it with effects.");
        Assert.Contains(EffectSupport.For(a).Clauses, c => c.Text.Contains("affect it with ＜De-Digivolve＞ effects"));   // 공식 영어 문장으로 (링크 효과로 읽는 것은 링크 정보가 있는 실제 카드 — 시나리오 확인)
        var b = Digimon("BT24-056", 4, "[On Play] [When Digivolving] Until your opponent's turn ends, their effects can't return 1 of your Digimon with the [System], [Life] or [Transmutation (App Name)] trait to hands or decks. with the [Appmon] trait from your trash without paying the cost.", name: "Dezipmon", color: CardColor.Black);
        AllSupported(b);
        Assert.DoesNotContain(EffectSupport.For(b).Clauses, c => c.Text.Contains("[Appmon] trait from your trash"));
    }

    [Fact]
    public void Korean_NewSentences_Rendered()
    {
        Assert.Contains("이 효과로 진화한 디지몬을 덱 아래로", Ko(Bt5109));
        Assert.Contains("진화하는 디지몬과 같은 색의 디지몬 카드 1장을 파기하는 것으로", Ko("[Main] The next time one of your Digimon would digivolve this turn, you may trash 1 Digimon card in your hand of the same color as the digivolving Digimon to reduce the memory cost of the digivolution by 4."));
        Assert.Contains("앞면의 디지몬 카드 1장의 【등장 시】 효과 1개를 이 디지몬의 효과로서 발휘한다", Ko("[When Attacking] [Once Per Turn] Activate 1 [On Play] effect on a face-up [Zaxon] trait Digimon card in your security stack as an effect of this Digimon."));
        Assert.Contains("이 디지몬과 명칭에", Ko("[On Play] [When Digivolving] ＜De-Digivolve 3＞ 1 of your opponent's Digimon. (Trash up to 3 cards from the top. You can't trash past level 3 cards.) Then, until your opponent's turn ends, their Digimon's effects don't affect this Digimon and 1 of your Digimon with [Garurumon] in its name."));
    }
}
