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
/// v0.76.0 미지원 문장 50차 묶음: EX2-048 · EX2-049 · BT19-100 육성 에어리어의 「마더 디・리퍼」를 고르거나 세지 않는다 (종합 룰 3-4-7-5 · 3-4-7-8) ·
/// EX2-048 필드의 서쳐는 맨 위 카드만 (4-7-7 · 4-7-8) · P-158 공식 영어 · BT13-020 · BT15-082 · BT11-086 · BT11-105 · BT11-107 · BT8-070 ·
/// BT23-072 · BT23-073 · EX6-060 · EX10-061 · BT13-112
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch76Tests
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
            state.Player.MainDeck.Add(Digimon("B76-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B76-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B76-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B76-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    // 1. EX2-048 — 육성 에어리어의 「마더 디・리퍼」는 고르지 않는다 (3-4-7-5) · 필드의 서쳐는 맨 위 카드만 (4-7-7 · 4-7-8)
    // =====================================================================

    private const string Ex2048 = "[On Play] Place 1 of your [ADR-02 Searcher]s from in play or from your hand under 1 of your [Mother D-Reaper]s as its bottom digivolution card.";

    [Fact]
    public void Ex2048_DoesNotChooseMotherInBreedingArea()
    {
        var (state, engine) = NewMainPhase();
        var mother = Card(Uid("B76-MDR-"), "", CardType.Digitama, 2, 0, CardColor.White, 15000, "Mother D-Reaper");
        var searcher = Digimon(Uid("B76-SRCH-"), 0, name: "ADR-02 Searcher", color: CardColor.White);
        var bubbles = Digimon(Uid("B76-BUB-"), 0, Ex2048, color: CardColor.White);
        state.Player.BreedingArea = new CardInstance(mother, 1);
        state.Player.Hand.Add(searcher);
        var me = Put(state.Player, bubbles);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, bubbles, includeInherited: false);
        Assert.Empty(state.Player.BreedingArea!.EvolutionSources);
        Assert.Contains(searcher, state.Player.Hand);
    }

    [Fact]
    public void Ex2048_FieldSearcher_OnlyTopCardGoesUnder_SourcesTrashed()
    {
        var (state, engine) = NewMainPhase();
        var mother = Card(Uid("B76-MDR-"), "", CardType.Digitama, 2, 0, CardColor.White, 15000, "Mother D-Reaper");
        var searcher = Digimon(Uid("B76-SRCH-"), 0, name: "ADR-02 Searcher", color: CardColor.White);
        var under = Digimon(Uid("B76-UND-"), 3, color: CardColor.White);
        var bubbles = Digimon(Uid("B76-BUB-"), 0, Ex2048, color: CardColor.White);
        var m = Put(state.Player, mother);
        var s = Put(state.Player, searcher, under);
        var me = Put(state.Player, bubbles);
        engine.PlayerChoices = new AcceptAll();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, bubbles, includeInherited: false);
        Assert.DoesNotContain(s, state.Player.BattleArea);
        Assert.Single(m.EvolutionSources);
        Assert.Contains(searcher, m.EvolutionSources);
        Assert.Contains(under, state.Player.Trash);
    }

    [Fact]
    public void Ex2049_RevealDoesNotPlaceUnderBreedingMother()
    {
        var (state, engine) = NewMainPhase();
        var mother = Card(Uid("B76-MDR-"), "", CardType.Digitama, 2, 0, CardColor.White, 15000, "Mother D-Reaper");
        var searcher = Digimon(Uid("B76-SRCH-"), 0, name: "ADR-02 Searcher", color: CardColor.White);
        var jeri = Digimon(Uid("B76-JERI-"), 0, "[Main] You may suspend this Digimon to Reveal the top 5 cards of your deck. Place 1 [ADR-02 Searcher] among them under 1 of your [Mother D-Reaper]s as its bottom digivolution card. Place the remaining cards at the bottom of your deck in any order.", color: CardColor.White);
        AllSupported(jeri);
        state.Player.BreedingArea = new CardInstance(mother, 1);
        state.Player.MainDeck.Insert(0, searcher);
        var me = Put(state.Player, jeri);
        engine.ActivateMainAbility(me, jeri);
        Assert.Empty(state.Player.BreedingArea!.EvolutionSources);
    }

    // =====================================================================
    // 2. P-158 — 공식 영어 (카드 번호 · API 문장이 같을 때만) · 「마더 디・리퍼」의 진화원 1장마다 등장 코스트 상한 +1 (배틀 에어리어만)
    // =====================================================================

    private const string P158Api = "[Main] By returning this Tamer to the bottom of the deck. You may play 1 Digimon card with the [D-Reaper] trait and play cost of 3 or less from your hand without paying the cost. For each digivolution card under 1 of your [Mother D-Reaper], add 1 to the maximum play cost you may play with this effect.";

    [Fact]
    public void P158_OfficialEnglish_OnlyForThatCardNumber()
    {
        var real = Card("P-158", P158Api, CardType.Tamer, 0, 4, CardColor.White, name: "Jeri (Fake)");
        AllSupported(real);
        var other = Card(Uid("B76-NOTP158-"), P158Api, CardType.Tamer, 0, 4, CardColor.White);
        Assert.Contains(EffectSupport.For(other).Clauses, c => c.Support != ClauseSupport.Supported);
        Assert.Contains("등장 코스트 상한 +1", Ko(SentenceParser.OfficialClause72(real, P158Api)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void P158_CountsOnlyBattleAreaMother(bool motherInBreeding, bool expectPlayed)
    {
        var (state, engine) = NewMainPhase();
        var tamer = Card("P-158", P158Api, CardType.Tamer, 0, 4, CardColor.White, name: "Jeri (Fake)");
        var mother = Card(Uid("B76-MDR-"), "", CardType.Digitama, 2, 0, CardColor.White, 15000, "Mother D-Reaper");
        var dr5 = Digimon(Uid("B76-DR5-"), 5, color: CardColor.White, playCost: 5, traits: "D-Reaper");
        var m = motherInBreeding ? new CardInstance(mother, 1) : Put(state.Player, mother);
        if (motherInBreeding) state.Player.BreedingArea = m;
        m.AddBottomSource(Digimon(Uid("B76-S1-"), 0)); m.AddBottomSource(Digimon(Uid("B76-S2-"), 0));
        engine.AddTamer(state.Player, tamer);
        state.Player.Hand.Add(dr5);
        engine.ActivateMainAbility(null, tamer);
        Assert.Equal(expectPlayed, state.Player.BattleArea.Any(d => d.TopCard == dr5));
    }

    // =====================================================================
    // 3. BT13-020 — 이 효과로 등장한 테이머를 DP 12000 디지몬으로도 취급 · ≪속공≫ · 진화할 수 없다 (공식 Q&A BT13-020 Q1)
    // =====================================================================

    private const string Bt13020 = "[When Digivolving] You may play 1 [Marcus Damon] from your hand without paying its cost. For the turn, treat the Tamer played with this effect as a 12000 DP Digimon with ＜Rush＞ (This Digimon can attack the turn it comes into play.), and it cannot digivolve.";

    [Fact]
    public void Bt13020_PlayedTamerBecomes12000RushDigimon()
    {
        var (state, engine) = NewMainPhase();
        var marcus = Card(Uid("B76-MAR-"), "", CardType.Tamer, 0, 4, CardColor.Red, name: "Marcus Damon");
        var me = Digimon(Uid("B76-SGB-"), 7, Bt13020, color: CardColor.Red);
        AllSupported(me);
        var d = Put(state.Player, me);
        state.Player.Hand.Add(marcus);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me, includeInherited: false);
        var proxy = state.Player.BattleArea.Single(x => x.TamerProxyOf == marcus);
        Assert.Equal(12000, proxy.GetCurrentDP());
        Assert.True(CardScripts.HasKeyword(proxy, Keyword.Rush));
        Assert.True(engine.AttackSecurityOrDirect(proxy).CanExecute);
    }

    [Fact]
    public void Bt13020_NoTamerPlayed_ExistingTamerUntouched()
    {
        var (state, engine) = NewMainPhase();
        var marcus = Card(Uid("B76-MAR-"), "", CardType.Tamer, 0, 4, CardColor.Red, name: "Marcus Damon");
        engine.AddTamer(state.Player, marcus);
        var me = Digimon(Uid("B76-SGB-"), 7, Bt13020, color: CardColor.Red);
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me, includeInherited: false);
        Assert.DoesNotContain(state.Player.BattleArea, x => x.TamerProxyOf != null);
    }

    // =====================================================================
    // 4. BT15-082 — 상대의 시큐리티 1장마다 DP 상한 -2000 (공식 Q&A BT15-082 Q1)
    // =====================================================================

    [Theory]
    [InlineData(5, false)]
    [InlineData(3, true)]
    public void Bt15082_DpMaximumDropsPerOpponentSecurity(int security, bool expectHigh)
    {
        var (state, engine) = NewMainPhase();
        while (state.DummyOpponent.SecurityStack.Count > security) state.DummyOpponent.SecurityStack.RemoveAt(0);
        var sora = Card(Uid("B76-SORA-"), "[All Turns] When a red Digimon card returns from your trash to the hand, by returning this Tamer to the hand, you may play 1 13000 DP or lower red Digimon card with [Avian], [Bird], [Beast], [Animal] or [Sovereign] in any of its traits (other than [Sea Animal]) from your hand without paying the cost. For each of your opponent's security cards, remove 2000 from this effect's playable card's DP maximum.", CardType.Tamer, 0, 4, CardColor.Red);
        AllSupported(sora);
        engine.AddTamer(state.Player, sora);
        var high = Digimon(Uid("B76-BIRD7-"), 5, color: CardColor.Red, dp: 7000, traits: "Bird");
        state.Player.Hand.Add(high);
        state.Player.Trash.Add(Digimon(Uid("B76-RED-"), 3, color: CardColor.Red));
        var ret = Digimon(Uid("B76-RET-"), 3, "[On Play] Return 1 red Digimon card from your trash to the hand.", color: CardColor.Red);
        var r = Put(state.Player, ret);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, r, ret, includeInherited: false);
        Assert.Equal(expectHigh, state.Player.BattleArea.Any(d => d.TopCard == high));
    }

    // =====================================================================
    // 5. BT11-086 — 디지크로스했을 때만 1장 더 (진화로는 1장)
    // =====================================================================

    [Fact]
    public void Bt11086_DigivolvingPlaysOnlyOne()
    {
        var (state, engine) = NewMainPhase();
        var me = Digimon(Uid("B76-MERV-"), 6, "[On Play] [When Digivolving] You may play 1 purple level 4 or lower Digimon card or 1 level 4 or lower Digimon card with the [Xros Heart] trait from your trash without paying the cost. If DigiXrosing, add 1 to the number of cards you play with this effect.", color: CardColor.Purple);
        AllSupported(me);
        var p1 = Digimon(Uid("B76-P1-"), 4, color: CardColor.Purple); var p2 = Digimon(Uid("B76-P2-"), 4, color: CardColor.Purple);
        state.Player.Trash.Add(p1); state.Player.Trash.Add(p2);
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me, includeInherited: false);
        Assert.Equal(1, state.Player.BattleArea.Count(x => x.TopCard == p1 || x.TopCard == p2));
        Assert.Contains("디지크로스하고 있었다면, 이 효과로 등장시키는 매수 +1", Ko(me.EffectText));
    }

    // =====================================================================
    // 6. BT11-105 — 「벰몬」을 놓는 것으로 트래시의 카드로 진화 (진화 코스트 지불). 진화할 카드가 없어도 놓을 수 있다 (공식 Q&A BT11-105 Q1)
    // =====================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bt11105_PlaceVemmonThenDigivolveFromTrash(bool hasTarget)
    {
        var (state, engine) = NewMainPhase(10);
        var opt = Card(Uid("B76-FUS-"), "[Main] By placing 1 [Vemmon] or [Destromon] from your trash under 1 of your Digimon as its bottom digivolution card, you may digivolve 1 of your Digimon into 1 [Destromon] or [Galacticmon] from your trash for its digivolution cost.", CardType.Option, 0, 1, CardColor.Black);
        AllSupported(opt);
        var vem = Digimon(Uid("B76-VEM-"), 3, name: "Vemmon", color: CardColor.Black);
        var destro = Digimon(Uid("B76-DES-"), 5, name: "Destromon", color: CardColor.Black, evoCost: 3);
        var base4 = Digimon(Uid("B76-B4-"), 4, color: CardColor.Black);
        var d = Put(state.Player, base4);
        state.Player.Trash.Add(vem); if (hasTarget) state.Player.Trash.Add(destro);
        engine.PlayerChoices = new AcceptAll();
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Contains(vem, d.EvolutionSources);
        Assert.Equal(hasTarget ? destro : base4, d.TopCard);
        Assert.Equal(hasTarget ? 7 : 10, state.MemoryGauge);
    }

    // =====================================================================
    // 7. BT11-107 · BT8-070 — 등장 코스트 합계로 여러 장 골라 소멸 (공식 Q&A BT11-107 Q1 · BT8-070 Q1)
    // =====================================================================

    [Fact]
    public void Bt11107_BudgetIsOwnGreymonPlayCost_ThenAttack()
    {
        var (state, engine) = NewMainPhase(10);
        var opt = Card(Uid("B76-HADES-"), "[Main] Choose any number of your opponent's Digimon and/or Tamers whose combined play costs are less than or equal to the play cost of 1 of your Digimon with [Greymon] in its name, and delete all of the chosen cards. Then, 1 of your Digimon with [Greymon] in its name may attack a player.", CardType.Option, 0, 7, CardColor.Black);
        AllSupported(opt);
        var grey = Put(state.Player, Digimon(Uid("B76-GREY-"), 5, name: "MetalGreymon", playCost: 7, color: CardColor.Black, dp: 9000));
        grey.TurnPlayed = 0;
        var e5 = Put(state.DummyOpponent, Digimon(Uid("B76-E5-"), 5, playCost: 5)); e5.IsOwnersTurn = false;
        var t2 = Card(Uid("B76-OT-"), "", CardType.Tamer, 0, 2, CardColor.Red);
        engine.AddTamer(state.DummyOpponent, t2);
        var e4 = Put(state.DummyOpponent, Digimon(Uid("B76-E4-"), 4, playCost: 4)); e4.IsOwnersTurn = false;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.DoesNotContain(e5, state.DummyOpponent.BattleArea);   // 5 + 2 = 7
        Assert.DoesNotContain(t2, state.DummyOpponent.Tamers);
        Assert.Contains(e4, state.DummyOpponent.BattleArea);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Bt8070_SourceColorsChooseDigimonAndOrTamers(bool red, bool black)
    {
        var (state, engine) = NewMainPhase(10);
        var me = Digimon(Uid("B76-BWG-"), 6, "[When Digivolving] If this Digimon has a red digivolution card, choose any number of your opponent's Digimon. If this Digimon has a black digivolution card, choose any number of your opponent's Tamers. The chosen cards' play costs must add up to 6 or less. Delete the chosen cards.", color: CardColor.Black);
        AllSupported(me);
        var srcs = new List<CardData>();
        if (red) srcs.Add(Digimon(Uid("B76-R-"), 4, color: CardColor.Red));
        if (black) srcs.Add(Digimon(Uid("B76-K-"), 4, color: CardColor.Black));
        var d = Put(state.Player, me, srcs.ToArray());
        var foe = Put(state.DummyOpponent, Digimon(Uid("B76-F3-"), 3, playCost: 3)); foe.IsOwnersTurn = false;
        var t3 = Card(Uid("B76-OT-"), "", CardType.Tamer, 0, 3, CardColor.Red);
        engine.AddTamer(state.DummyOpponent, t3);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me, includeInherited: false);
        Assert.Equal(red, !state.DummyOpponent.BattleArea.Contains(foe));
        Assert.Equal(black, !state.DummyOpponent.Tamers.Contains(t3));
    }

    // =====================================================================
    // 8. 육성 에어리어를 지정한 효과 (3-4-7-5): BT23-072 · BT23-073 · EX6-060 · BT13-112
    // =====================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bt23072_PayAndPlaceUnderBreedingHost(bool hostInBreeding)
    {
        var (state, engine) = NewMainPhase(5);
        var eater = Card(Uid("B76-ME-"), "", CardType.Digitama, 2, 0, CardColor.White, name: "Mother Eater");
        var me = Digimon(Uid("B76-KD-"), 0, "[Hand] [Main] By paying 3 cost and placing this card as the bottom digivolution card of your [King Drasil_7D6] or [Mother Eater] in the breeding area, ＜Draw 1＞ (Draw 1 card from your deck.)", color: CardColor.White);
        AllSupported(me);
        if (hostInBreeding) state.Player.BreedingArea = new CardInstance(eater, 1);
        state.Player.Hand.Add(me);
        var r = engine.ActivateHandEffect(me);
        Assert.Equal(hostInBreeding, r.CanExecute);
        Assert.Equal(hostInBreeding ? 2 : 5, state.MemoryGauge);
        if (hostInBreeding) Assert.Contains(me, state.Player.BreedingArea!.EvolutionSources);
        else Assert.Contains(me, state.Player.Hand);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bt23073_DeleteOrPlaceUnderBreedingMotherEater(bool hostInBreeding)
    {
        var (state, engine) = NewMainPhase(5);
        var eater = Card(Uid("B76-ME-"), "", CardType.Digitama, 2, 0, CardColor.White, name: "Mother Eater");
        if (hostInBreeding) state.Player.BreedingArea = new CardInstance(eater, 1);
        var bitCard = Digimon(Uid("B76-BIT-"), 3, "[All Turns] [Once Per Turn] When any of your other Digimon with the [Eater] or [Hudie] trait would leave the battle area, by deleting this Digimon or placing it as the bottom digivolution card of your [Mother Eater] in the breeding area, 1 of those Digimon doesn't leave.", color: CardColor.White);
        AllSupported(bitCard);
        var src = Digimon(Uid("B76-BSRC-"), 2);
        var bit = Put(state.Player, bitCard, src);
        var victim = Put(state.Player, Digimon(Uid("B76-EAT-"), 4, traits: "Eater"));
        engine.DeleteByOpponentEffect(state.Player, victim);
        Assert.Contains(victim, state.Player.BattleArea);
        Assert.DoesNotContain(bit, state.Player.BattleArea);
        if (hostInBreeding)
        {
            Assert.Contains(bitCard, state.Player.BreedingArea!.EvolutionSources);
            Assert.Contains(src, state.Player.Trash);   // 4-7-8
        }
        else Assert.Contains(bitCard, state.Player.Trash);
    }

    [Fact]
    public void Ex6060_LeavingPlacesTraitCardUnderBreedingGate()
    {
        var (state, engine) = NewMainPhase(5);
        var gate = Card(Uid("B76-GATE-"), "", CardType.Digitama, 2, 0, CardColor.Purple, name: "Gate of Deadly Sins");
        state.Player.BreedingArea = new CardInstance(gate, 1);
        var bel = Digimon(Uid("B76-BEL-"), 6, "[All Turns] When this Digimon would leave the battle area other than by battle, place 1 card with the [Seven Great Demon Lords] trait card from your trash as the bottom digivolution card of your [Gate of Deadly Sins] in the breeding area.", color: CardColor.Purple);
        AllSupported(bel);
        var lord = Digimon(Uid("B76-SGDL-"), 6, color: CardColor.Purple, traits: "Seven Great Demon Lords");
        state.Player.Trash.Add(lord);
        var d = Put(state.Player, bel);
        engine.DeleteByOpponentEffect(state.Player, d);
        Assert.Contains(lord, state.Player.BreedingArea!.EvolutionSources);
    }

    [Fact]
    public void Ex10061_PlaysOneOfEachName_RushAndDeletedAtTurnEnd()
    {
        var (state, engine) = NewMainPhase(5);
        var me = Digimon(Uid("B76-APO-"), 6, "[On Play] [When Digivolving] You may play 1 of each [Dark Masters] trait card with different names from this Digimon's digivolution cards without paying the costs. Then, all of your [Dark Masters] trait Digimon gain ＜Rush＞ for the turn. (This Digimon can attack the turn it was played.) At turn end, delete the Digimon this effect played.", color: CardColor.Purple);
        AllSupported(me);
        var a = Digimon(Uid("B76-DMA-"), 6, name: "MetalSeadramon", traits: "Dark Masters");
        var b = Digimon(Uid("B76-DMB-"), 6, name: "Puppetmon", traits: "Dark Masters");
        var d = Put(state.Player, me, a, a, b);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me, includeInherited: false);
        var played = state.Player.BattleArea.Where(x => x.TopCard == a || x.TopCard == b).ToList();
        Assert.Equal(2, played.Count);
        Assert.Single(d.EvolutionSources);
        Assert.All(played, x => Assert.True(CardScripts.HasKeyword(x, Keyword.Rush)));
        Assert.Contains("명칭이 서로 다른", Ko(me.EffectText));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bt13112_DeleteOrPlayEachFromBreeding(bool withPool)
    {
        var (state, engine) = NewMainPhase(5);
        var egg = Card(Uid("B76-KD7-"), "", CardType.Digitama, 2, 0, CardColor.White, name: "King Drasil_7D6");
        var br = new CardInstance(egg, 1);
        var rk1 = Digimon(Uid("B76-RK1-"), 6, name: "Gallantmon", traits: "Royal Knight");
        var rk2 = Digimon(Uid("B76-RK2-"), 6, name: "Dynasmon", traits: "Royal Knight");
        if (withPool) { br.AddBottomSource(rk1); br.AddBottomSource(rk1); br.AddBottomSource(rk2); }
        state.Player.BreedingArea = br;
        var foe = Put(state.DummyOpponent, Digimon(Uid("B76-FOE-"), 3)); foe.IsOwnersTurn = false;
        var me = Digimon(Uid("B76-OMNI-"), 7, "[On Play] [When Digivolving] Delete 1 of your opponent's Digimon, or you may play 1 of each Digimon with the [Royal Knight] trait and a different name from the digivolution cards of your Digimon in the breeding area without paying their costs. When a Digimon is played by this effect, trash 1 of your Digimon in the breeding area, and all of your Digimon gain ＜Rush＞ for the turn. (This Digimon can attack the turn it comes into play.)", color: CardColor.White);
        AllSupported(me);
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me, includeInherited: false);
        if (withPool)
        {
            Assert.Equal(2, state.Player.BattleArea.Count(x => x.TopCard == rk1 || x.TopCard == rk2));
            Assert.Null(state.Player.BreedingArea);   // 디지타마 카드도 「디지몬」 (4-3-1)
            Assert.Contains(egg, state.Player.Trash);
            Assert.All(state.Player.BattleArea, x => Assert.True(CardScripts.HasKeyword(x, Keyword.Rush)));
            Assert.Contains(foe, state.DummyOpponent.BattleArea);
        }
        else
        {
            Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
            Assert.NotNull(state.Player.BreedingArea);
        }
    }

    // =====================================================================
    // 9. 원래 규칙이 읽던 문장은 그대로 (50차 규칙은 읽지 못한 절만)
    // =====================================================================

    [Fact]
    public void Rewrite76_LeavesOtherClausesAlone()
    {
        Assert.Null(SentenceParser.Rewrite76("[On Play] ＜Draw 1＞."));
        Assert.Null(SentenceParser.Rewrite76("[Main] Delete up to 5 play cost's total worth of your opponent's Digimon and Tamers."));
        Assert.Equal("【등장 시】 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다).", Ko("[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)"));
    }
}
