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
/// v0.73.0 미지원 문장 47차 묶음: "in any of its traits" 한글 "특징으로 「X」를 포함하는" (공식 표기) ·
/// "When your Digimon or Tamers are played ~, if any of them have [X] in their names" 테이머 등장 바로잡음 (AD1-001 · AD1-010) ·
/// EX11-027 이 디지몬 · 패의 카드 링크 (종합 룰 6-5-1-4 · 4-9-4 · 4-7-8) · EX11-053 「위그드라실_7D6」의 진화원에서 등장 ·
/// BT20-003 · BT7-110 · BT8-066 · EX5-070
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch73Tests
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
            state.Player.MainDeck.Add(Digimon("B73-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B73-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B73-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B73-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    // 1. "in any of its traits"(특징 포함, 종합 룰 2-3-2-4) — 공식 한글 "특징으로 「X」를 포함하는" · "포함하거나" (BT13-050 · P-208)
    // =====================================================================

    [Fact]
    public void Korean_InAnyOfItsTraits_SaysContains_ExactTraitStaysHave()
    {
        string ko = Ko("[On Play] Reveal the top 3 cards of your deck. Add 1 card with [Avian] or [Bird] in any of its traits and 1 card with the [WG] trait among them to the hand. Return the rest to the bottom of the deck.");
        Assert.Matches("특징으로 「[^」]+」(?:/「[^」]+」)+[을를] 포함하는 카드 1장과 특징으로 「[^」]+」[을를] 가진 카드 1장", ko);   // v0.74.0 [Avian] · [Bird] → 「조」/「새」/「병아리」
        // "~ in any of its traits or the [X] trait" → "포함하거나 ~ 가진"
        string ko2 = Ko("[On Deletion] You may play 1 play cost 5 or lower card with [Avian] or [Bird] in any of its traits or the [DATA SQUAD] trait from your trash without paying the cost.");
        Assert.Matches("특징으로 「[^」]+」(?:/「[^」]+」)+[을를] 포함하거나 특징으로 「DATA SQUAD」[을를] 가진", ko2);
        // "(other than [Sea Animal])" 괄호가 사이에 있어도
        string ko3 = Ko("[When Digivolving] [When Attacking] [Once Per Turn] You may play 1 play cost 4 or lower Digimon card with [Beast], [Animal] or [Sovereign], other than [Sea Animal], in any of its traits from your hand without paying the cost.");
        Assert.Contains("포함(「수장룡형」/「수생형」/「수생포유류형」/「정보수집 타입」은 제외)하는 디지몬 카드", ko3);   // v0.74.0 공식 BT13-048 제외 표기
        Assert.DoesNotContain("가진 디지몬 카드", ko3);
    }

    [Fact]
    public void Korean_ExactTraitOnly_Unchanged()
    {
        string ko = Ko("[On Play] Reveal the top 3 cards of your deck. Add 1 card with the [WG] trait among them to the hand. Return the rest to the bottom of the deck.");
        Assert.Contains("가진 카드 1장", ko);
        Assert.DoesNotContain("포함", ko);
    }

    // =====================================================================
    // 2. 사건 조건 감사: "When your Digimon or Tamers are played ~, if any of them have [X] in their names" — 테이머 등장은 그 테이머
    //    (공식 한글 AD1-001: 그 디지몬/테이머로 판단 — 전에는 디지몬만 봐서 테이머 등장에서 언제나 거짓)
    // =====================================================================

    [Theory]
    [InlineData("Tai Kamiya", true)]
    [InlineData("Matt Ishida", false)]
    public void AnyOfThemNamed_TamerPlayed_AD1_001(string tamerName, bool digivolves)
    {
        string text = "[All Turns] When your Digimon or Tamers are played or digivolve, if any of them have [Garurumon] or [Tai Kamiya] in their names, this Digimon may digivolve into a Digimon card with [Greymon] in its name in the hand without paying the cost.";
        var self = Digimon(Uid("B73-AD1-"), 4, text, name: "Greymon", color: CardColor.Red);
        AllSupported(self);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, self);
        var into = Digimon(Uid("B73-MG-"), 5, name: "MetalGreymon", color: CardColor.Red, evoCost: 3);
        state.Player.Hand.Add(into);
        var tamer = Card(Uid("B73-TT-"), "", CardType.Tamer, name: tamerName, color: CardColor.Red, playCost: 2);
        state.Player.Hand.Add(tamer);
        Assert.True(engine.PlayCard(tamer).CanExecute);
        Assert.Equal(digivolves, d.TopCard == into);
    }

    [Fact]
    public void AnyOfThemNamed_DigimonPlayed_Unchanged()
    {
        string text = "[All Turns] When your Digimon or Tamers are played or digivolve, if any of them have [Garurumon] or [Tai Kamiya] in their names, this Digimon may digivolve into a Digimon card with [Greymon] in its name in the hand without paying the cost.";
        foreach (var (name, expect) in new[] { ("Garurumon", true), ("Gabumon", false) })
        {
            var (state, engine) = NewMainPhase();
            var d = Put(state.Player, Digimon(Uid("B73-AD1b-"), 4, text, name: "Greymon", color: CardColor.Red));
            var into = Digimon(Uid("B73-MGb-"), 5, name: "MetalGreymon", color: CardColor.Red, evoCost: 3);
            state.Player.Hand.Add(into);
            var played = Digimon(Uid("B73-PL-"), 3, name: name, color: CardColor.Blue, playCost: 2);
            state.Player.Hand.Add(played);
            Assert.True(engine.PlayCard(played).CanExecute);
            Assert.Equal(expect, d.TopCard == into);
        }
    }

    // =====================================================================
    // 3. EX11-027 "you may link this Digimon or 1 [Maquinamon] in your hand to 1 of your other Digimon without paying the cost"
    // =====================================================================

    private const string LinkText = "[On Play] Reveal the top 3 cards of your deck. Add 1 [Maquinamon] and 1 card with [Maquinamon] in its text among them to the hand. Return the rest to the bottom of the deck. Then, you may link this Digimon or 1 [Maquinamon] in your hand to 1 of your other Digimon without paying the cost.";

    [Fact]
    public void LinkThisDigimon_LeavesBattleArea_SourcesTrashed()
    {
        var me = Digimon(Uid("B73-MQ-"), 3, LinkText, name: "Maquinamon", color: CardColor.Green);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Digimon(Uid("B73-HOST-"), 5, color: CardColor.Green));
        var src = Digimon(Uid("B73-SRC-"), 2, color: CardColor.Green);
        var self = Put(state.Player, me, src);
        var old = Digimon(Uid("B73-OLDL-"), 3, color: CardColor.Green);
        self.LinkedCards.Add(old);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, me, includeInherited: false);
        Assert.DoesNotContain(self, state.Player.BattleArea);          // 링크 카드는 에어리어에 속하지 않는다 (4-9-4)
        Assert.Contains(me, host.LinkedCards);
        Assert.Contains(src, state.Player.Trash);                       // 겹쳐져 있던 카드는 파기 (4-7-8)
        Assert.Contains(old, state.Player.Trash);                       // 가진 링크 카드도 파기
    }

    [Fact]
    public void LinkFromHand_PreferredOverThisDigimon_NoTargetDoesNothing()
    {
        var me = Digimon(Uid("B73-MQ2-"), 3, LinkText, name: "Maquinamon", color: CardColor.Green);
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Digimon(Uid("B73-HOST2-"), 5, color: CardColor.Green));
        var self = Put(state.Player, me);
        var inHand = Digimon(Uid("B73-MQH-"), 3, name: "Maquinamon", color: CardColor.Green);
        state.Player.Hand.Add(inHand);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, me, includeInherited: false);
        Assert.Contains(self, state.Player.BattleArea);
        Assert.Contains(inHand, host.LinkedCards);
        Assert.DoesNotContain(inHand, state.Player.Hand);
        // 다른 디지몬이 없으면 링크하지 않는다
        var (state2, engine2) = NewMainPhase();
        var self2 = Put(state2.Player, me);
        engine2.RunEffects(EffectTrigger.OnPlay, state2.Player, self2, me, includeInherited: false);
        Assert.Contains(self2, state2.Player.BattleArea);
    }

    [Fact]
    public void EX11_027_OfficialLinkInfo_RequirementCostDp()
    {
        var real = Digimon("EX11-027", 3, LinkText, name: "Maquinamon", color: CardColor.Green,
            inherited: "[All Turns] When this Digimon would leave the battle area, by placing 1 of its link cards as its bottom digivolution card, it doesn't leave.");
        Assert.True(SentenceParser.IsLinkCard43(real));                 // 하단 텍스트는 링크 효과
        var withText = new CardInstance(Digimon(Uid("B73-MAN-"), 5, "[On Play] You may play 1 [Maquinamon] from your hand.", color: CardColor.Green), 1);
        var plain = new CardInstance(Digimon(Uid("B73-PLN-"), 5, color: CardColor.Green), 1);
        Assert.True(GoldfishEngine.MeetsLinkRequirement(real, withText));   // [Maquinamon] in text
        Assert.False(GoldfishEngine.MeetsLinkRequirement(real, plain));
        Assert.Equal(2, GoldfishEngine.LinkCostOf(real));
        Assert.Equal(2000, GoldfishEngine.LinkDp(real));
        Assert.Contains(CardScripts.For(real)!.Effects, e => e.Source == EffectSource.Link43 && e.Trigger == EffectTrigger.ThisDigimonWouldLeave);
    }

    // =====================================================================
    // 4. EX11-053 [소멸 시] 패 또는 필드의 「위그드라실_7D6」 진화원에서 등장 → 이 카드를 그 디지몬의 진화원 맨 아래에
    // =====================================================================

    [Theory]
    [InlineData(1, true, true)]    // 시큐리티 1장 · 진화원에서
    [InlineData(1, false, true)]   // 시큐리티 1장 · 패에서
    [InlineData(3, true, false)]   // 시큐리티 3장 — 조건 불만족
    public void PlayFromHandOrUnderKingDrasil_ThenThisUnder(int security, bool fromSources, bool expect)
    {
        string text = "[On Deletion] If you have 1 or fewer security cards, you may play 1 [Omnimon (X Antibody)] from your hand or under your [King Drasil_7D6]s on the field without paying the cost. Then, place this card as the played Digimon's bottom digivolution card.";
        var me = Digimon(Uid("B73-OMK-"), 4, text, name: "Omekamon", color: CardColor.White);
        AllSupported(me);
        var (state, engine) = NewMainPhase(security: security);
        var omni = Digimon(Uid("B73-OMX-"), 7, name: "Omnimon (X Antibody)", color: CardColor.White, playCost: 16);
        var kd = new CardInstance(Card(Uid("B73-KD-"), "", CardType.Digitama, 2, name: "King Drasil_7D6", color: CardColor.White), 1);
        if (fromSources) kd.AddBottomSource(omni); else state.Player.Hand.Add(omni);
        state.Player.BreedingArea = kd;                                 // 필드 = 배틀 에어리어 + 육성 에어리어 (3-4-6)
        var d = Put(state.Player, me);
        engine.DeleteByOpponentEffect(state.Player, d);
        var played = state.Player.BattleArea.FirstOrDefault(x => x.TopCard == omni);
        Assert.Equal(expect, played != null);
        if (expect)
        {
            Assert.Same(me, played!.EvolutionSources.Last());
            Assert.DoesNotContain(me, state.Player.Trash);
            Assert.Empty(kd.EvolutionSources);
        }
    }

    // =====================================================================
    // 5. BT20-003 · BT7-110 · BT8-066 · EX5-070
    // =====================================================================

    [Fact]
    public void BT20_003_PlaceTamerUnderThis_OnlyWithoutTamerSources()
    {
        string inh = "[End of Your Turn] [Once Per Turn] You may place 1 of your Tamers with [Pulsemon] in its text or the [SoC] or [SEEKERS] trait as the bottom digivolution card of this Digimon with no Tamer cards in its digivolution cards.";
        var egg = Card(Uid("B73-BB-"), "", CardType.Digitama, 2, inherited: inh, color: CardColor.Yellow);
        AllSupported(egg);
        Assert.Contains("진화원에 테이머 카드가 없는 이 디지몬의 진화원 아래에 놓을 수 있다", Ko("Inherited Effect " + inh));
        foreach (bool hasTamerSource in new[] { false, true })
        {
            var (state, engine) = NewMainPhase();
            var d = Put(state.Player, Digimon(Uid("B73-BBD-"), 4, color: CardColor.Yellow), egg);
            if (hasTamerSource) d.AddBottomSource(Card(Uid("B73-OLDT-"), "", CardType.Tamer, color: CardColor.Yellow));
            var tamer = Card(Uid("B73-SEEK-"), "", CardType.Tamer, color: CardColor.Yellow, traits: "SEEKERS");
            engine.AddTamer(state.Player, tamer);
            engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, d, d.TopCard);
            Assert.Equal(!hasTamerSource, d.EvolutionSources.Contains(tamer));
            Assert.Equal(hasTamerSource, state.Player.Tamers.Contains(tamer));
        }
    }

    [Fact]
    public void BT7_110_IgnoringLevel_SameColorOnly()
    {
        string text = "[Main] Your level 4 Digimon can digivolve into 1 Digimon card in your hand with matching colors and [Ten Warriors] trait for its digivolution cost, ignoring its level.";
        var opt = Card(Uid("B73-EA-"), text, color: CardColor.White, playCost: 0);
        AllSupported(opt);
        Assert.Contains("같은 색인 패의 디지몬 카드", Ko(text));
        foreach (bool sameColor in new[] { true, false })
        {
            var (state, engine) = NewMainPhase();
            var d = Put(state.Player, Digimon(Uid("B73-L4-"), 4, color: CardColor.Red));
            var into = Digimon(Uid("B73-TW-"), 6, color: sameColor ? CardColor.Red : CardColor.Blue, evoCost: 4, traits: "Ten Warriors");
            state.Player.Hand.Add(into);
            engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
            Assert.Equal(sameColor, d.TopCard == into);
            Assert.Equal(sameColor ? 4 : 8, state.MemoryGauge);
        }
    }

    [Fact]
    public void BT8_066_SourceAddedByOwnEffect_DigivolveCostMinus1()
    {
        string text = "[Your Turn] When one of your effects places a digivolution card under this Digimon, you may digivolve this Digimon into a Digimon card in your hand with the [X Antibody] trait by paying its digivolution cost. When digivolving with this effect, reduce the digivolution cost by 1.";
        var me = Digimon(Uid("B73-HSR-"), 5, text, color: CardColor.Black);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var into = Digimon(Uid("B73-XA6-"), 6, color: CardColor.Black, evoCost: 5, traits: "X Antibody");
        state.Player.Hand.Add(into);
        var opt = Card(Uid("B73-PLC-"), "[Main] Place this card under 1 of your Digimon without [X Antibody] in its digivolution cards as its bottom digivolution card.", color: CardColor.Black, playCost: 0);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Same(into, d.TopCard);
        Assert.Equal(4, state.MemoryGauge);   // 8 - (5 - 1)
    }

    [Fact]
    public void EX5_070_DigivolveWithoutXAntibody_ThenPlaceThisUnder()
    {
        string text = "[Main] 1 of your Digimon without [X Antibody] in its digivolution cards may digivolve into a Digimon card with the [X Antibody] trait in your hand with the digivolution cost reduced by 1. If it did, place this card as its bottom digivolution card.";
        var opt = Card(Uid("B73-XPF-"), text, color: CardColor.White, playCost: 0, name: "X Antibody");
        AllSupported(opt);
        Assert.Contains("진화원에 「", Ko(text));
        Assert.Contains("없는 자신의 디지몬 1마리를", Ko(text));
        foreach (bool hasX in new[] { false, true })
        {
            var (state, engine) = NewMainPhase();
            var d = Put(state.Player, Digimon(Uid("B73-L5-"), 5, color: CardColor.Black));
            if (hasX) d.AddBottomSource(Card(Uid("B73-XAS-"), "", name: "X Antibody", color: CardColor.White));
            var into = Digimon(Uid("B73-XA6b-"), 6, color: CardColor.Black, evoCost: 5, traits: "X Antibody");
            state.Player.Hand.Add(into);
            engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
            Assert.Equal(!hasX, d.TopCard == into);
            Assert.Equal(!hasX, d.EvolutionSources.Contains(opt));
            if (!hasX) Assert.Equal(4, state.MemoryGauge);
        }
    }

    [Fact]
    public void EX5_070_Inherited_LeaveByOpponent_ReturnAndSecurity()
    {
        string inh = "[All Turns] When this Digimon would leave the battle area other than by one of your effects, from this Digimon's digivolution cards, return 1 Digimon card to the hand and place 1 [X Antibody] on top of your security stack.";
        var xpf = Card(Uid("B73-XPFi-"), "", name: "X Antibody", color: CardColor.White, inherited: inh);
        Assert.Contains("이 디지몬의 진화원에서 디지몬 카드 1장을 패로 되돌리고", Ko(inh));
        var (state, engine) = NewMainPhase();
        var lv3 = Digimon(Uid("B73-L3-"), 3, color: CardColor.Black);
        var d = Put(state.Player, Digimon(Uid("B73-TOP-"), 6, color: CardColor.Black), lv3, xpf);
        int sec = state.Player.SecurityStack.Count;
        engine.DeleteByOpponentEffect(state.Player, d);
        Assert.Contains(lv3, state.Player.Hand);
        Assert.Same(xpf, state.Player.SecurityStack[0]);
        Assert.Equal(sec + 1, state.Player.SecurityStack.Count);
    }
}
