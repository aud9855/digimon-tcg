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
/// v0.77.0 미지원 문장 51차 묶음: 에어리어의 디지타마 카드도 「디지몬」 (종합 룰 4-3-1 · Q&amp;A BT16-090 Q1 — BT16-090 · BT13-080 · BT27-075 · 육성 에어리어에서 진화) ·
/// 디지몬을 진화원으로 놓으면 맨 위 카드만 (4-7-7 · 4-7-8 · Q&amp;A BT11-088 — BT22-007 · LM-026 · EX9-005) ·
/// BT12-112 · BT21-030 배틀 에어리어의 「샤우트몬」을 놓아 코스트 -1 · 트래시도 디지크로스 (Q&amp;A BT12-112 Q1 · 7-2-2-2) · BT10-111 디지크로스 조건 1장 대신 (15-15-6) ·
/// EX6-006 · BT13-055 · BT26-097 · BT12-089 · BT11-107 기준 디지몬 선택 · ST10-06 · EX6-029
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch77Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    /// <summary>"기준"을 묻는 선택에서 마지막(가장 낮은) 후보를 고른다</summary>
    private sealed class PickLowestReference : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => recommended;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) =>
            question.Contains("기준") ? options.Last() : recommended ?? (optional ? null : options.FirstOrDefault());
        public IReadOnlyList<CardInstance> ChooseDigimons(GameState state, string question, IReadOnlyList<CardInstance> options, int min, int max, IReadOnlyList<CardInstance> recommended) =>
            question.Contains("기준") ? new[] { options.Last() } : recommended;
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

    private static CardData Egg(string id, CardColor color = CardColor.Blue, string inherited = "") =>
        Card(id, "", CardType.Digitama, 2, 0, color, 0, null, 0, inherited);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B77-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B77-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B77-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B77-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b77_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 1. 에어리어의 디지타마 카드도 「디지몬」 (종합 룰 4-3-1)
    // =====================================================================

    private const string Bt16090 = "[Main] [Once Per Turn] By deleting 1 of your [B77 Ukko] and trashing 1 of your Digimon in the breeding area, you may play 1 [B77 BigUkko] from your hand to an empty space in your breeding area for a play cost of 3.";

    [Fact]
    public void BT16_090_TrashesDigiEggInBreedingArea_QA_Q1()
    {
        var tamer = Card(Uid("B77-T90-"), Bt16090, CardType.Tamer, playCost: 3);
        AllSupported(tamer);
        var (state, engine) = NewMainPhase(memory: 5);
        var egg = Egg("B77-EGG");
        state.Player.BreedingArea = new CardInstance(egg, 1);
        engine.AddTamer(state.Player, tamer);
        var ukko = Put(state.Player, Digimon("B77-UKKO", 3, name: "B77 Ukko"));
        var big = Digimon("B77-BIG", 6, name: "B77 BigUkko", playCost: 12);
        state.Player.Hand.Add(big);
        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);
        Assert.Same(big, state.Player.BreedingArea?.TopCard);
        Assert.Contains(egg, state.Player.Trash);
        Assert.DoesNotContain(ukko, state.Player.BattleArea);
        Assert.Equal(2, state.MemoryGauge);
    }

    [Fact]
    public void DeleteLevel2InBreeding_IncludesDigiEgg_BT27_075()
    {
        var card = Digimon(Uid("B77-GZ-"), 3, "When this card would be played, by deleting 1 of your level 2 Digimon in the breeding area, reduce the cost by 2.", playCost: 4);
        AllSupported(card);
        var (state, engine) = NewMainPhase(memory: 8);
        engine.PlayerChoices = new AcceptAll();
        var egg = Egg("B77-EGG2");
        state.Player.BreedingArea = new CardInstance(egg, 1);
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute);
        Assert.Null(state.Player.BreedingArea);
        Assert.Contains(egg, state.Player.Trash);
        Assert.Equal(6, state.MemoryGauge);
    }

    [Fact]
    public void BreedingDigivolve_OntoDigiEgg()
    {
        var me = Digimon(Uid("B77-BR-"), 5, "[On Play] 1 of your Digimon in the breeding area may digivolve into a level 3 Digimon card in the hand without paying the cost.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var egg = Egg("B77-EGG3", CardColor.Red);
        var b = new CardInstance(egg, 1);
        state.Player.BreedingArea = b;
        var l3 = Digimon("B77-L3", 3, color: CardColor.Red);
        state.Player.Hand.Add(l3);
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me, includeInherited: false);
        Assert.Same(l3, state.Player.BreedingArea?.TopCard);
        Assert.Contains(egg, state.Player.BreedingArea!.EvolutionSources);
    }

    // =====================================================================
    // 2. 디지몬을 진화원으로 놓으면 맨 위 카드만 — 그 진화원은 트래시 (4-7-7 · 4-7-8 · Q&A BT11-088 · BT12-102 · BT12-027)
    // =====================================================================

    [Fact]
    public void BT22_007_Inherited_LeavingEaterPlacedUnder_TopCardOnly()
    {
        var eggCard = Egg(Uid("B77-ME-"), inherited: "[Breeding] [All Turns] [Once Per Turn] When any of your [Eater] trait Digimon would leave the battle area other than by your effects, you may place them as this Digimon's bottom digivolution cards.");
        AllSupported(eggCard);
        var (state, engine) = NewMainPhase();
        var host = new CardInstance(Digimon("B77-HOST", 3), 1);
        host.AddBottomSource(eggCard);
        state.Player.BreedingArea = host;
        var eaterTop = Digimon("B77-EATER", 4, traits: "Eater");
        var under = Digimon("B77-UNDER", 3);
        var eater = Put(state.Player, eaterTop, under);
        engine.DeleteByOpponentEffect(state.Player, eater);
        Assert.DoesNotContain(eater, state.Player.BattleArea);
        Assert.Contains(eaterTop, host.EvolutionSources);
        Assert.DoesNotContain(under, host.EvolutionSources);
        Assert.Contains(under, state.Player.Trash);
    }

    [Fact]
    public void LM_026_LeavingPlacedUnderPlayedGuilmon_TopCardOnly()
    {
        var lm = Digimon(Uid("B77-LM-"), 6, "[All Turns] When this Digimon would leave the battle area, by playing 1 [B77 Guilmon] from this Digimon's digivolution cards or from your trash, place this Digimon as that Digimon's bottom digivolution card.");
        AllSupported(lm);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var gu = Digimon("B77-GU", 3, name: "B77 Guilmon");
        var other = Digimon("B77-OTHER", 4);
        var d = Put(state.Player, lm, other, gu);
        engine.DeleteByOpponentEffect(state.Player, d);
        var g = Assert.Single(state.Player.BattleArea);
        Assert.Same(gu, g.TopCard);
        Assert.Equal(new[] { lm }, g.EvolutionSources.ToArray());
        Assert.Contains(other, state.Player.Trash);
    }

    [Fact]
    public void EX9_005_BreedingPlacedUnderPlayed_SourcesTrashed()
    {
        var neg = Egg(Uid("B77-NEG-"));
        neg.EffectText = "[Breeding] [Main] [Once Per Turn] You may play 1 Digimon card with [Negamon] in its text from your hand with the play cost reduced by 2. For each [Negamon] in your trash or your Digimon's digivolution cards, further reduce it by 1. Then, place this Digimon as the played Digimon's bottom digivolution card.";
        AllSupported(neg);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var b = new CardInstance(neg, 1);
        var stuck = Digimon("B77-STUCK", 3);
        b.AddBottomSource(stuck);
        state.Player.BreedingArea = b;
        var played = Digimon("B77-NP", 4, "[On Play] Trash 1 card in your hand. [Negamon]", playCost: 4);
        state.Player.Hand.Add(played);
        engine.ActivateMainAbility(b, neg);
        var p = Assert.Single(state.Player.BattleArea);
        Assert.Same(played, p.TopCard);
        Assert.Contains(neg, p.EvolutionSources);
        Assert.DoesNotContain(stuck, p.EvolutionSources);
        Assert.Contains(stuck, state.Player.Trash);
    }

    // =====================================================================
    // 3. 디지크로스 — BT12-112 · BT21-030 · BT10-111
    // =====================================================================

    private const string Bt12112 = "When you would play this card, by placing 1 [Shoutmon] as a digivolution card under this Digimon, reduce the cost by 1 and you may place cards in your trash as digivolution cards for a DigiXros.";
    private const string Bt21030 = "When this card would be played, by placing 1 of your [Shoutmon] under it, reduce the play cost by 1 and cards in your trash can also be placed for DigiXros.";

    [Theory]
    [InlineData(Bt12112)]
    [InlineData(Bt21030)]
    public void BT12_112_PlacesBattleAreaShoutmon_TopOnly_TrashForXros(string text)
    {
        string id = Uid("B77-X7-"), a = Uid("B77-XA-"), b = Uid("B77-XB-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = id, Name = "테스트X7", MainText = "디지크로스 -2:「테스트에이」×「테스트비」" },
            new KoreanCard { CardId = a, Name = "테스트에이" }, new KoreanCard { CardId = b, Name = "테스트비" },
        }, () =>
        {
            var x7 = Digimon(id, 7, text, playCost: 15);
            AllSupported(x7);
            Assert.True(GoldfishEngine.HasDigiXros(x7));
            var (state, engine) = NewMainPhase(memory: 10);
            var shoutTop = Digimon("B77-SHOUT", 3, name: "Shoutmon");
            var shoutUnder = Digimon("B77-SHU", 2);
            var shout = Put(state.Player, shoutTop, shoutUnder);
            var ma = Digimon(a, 3); var mb = Digimon(b, 3);
            state.Player.Trash.Add(ma); state.Player.Trash.Add(mb);
            state.Player.Hand.Add(x7);
            Assert.True(engine.PlayCard(x7).CanExecute);
            var me = Assert.Single(state.Player.BattleArea);
            Assert.Same(x7, me.TopCard);
            Assert.Contains(shoutTop, me.EvolutionSources);
            Assert.Contains(ma, me.EvolutionSources);
            Assert.Contains(mb, me.EvolutionSources);
            Assert.DoesNotContain(shoutUnder, me.EvolutionSources);
            Assert.Contains(shoutUnder, state.Player.Trash);
            Assert.Contains(state.ActionLogs, l => l.Contains("(코스트 10 지불)"));   // 15 - 1 - 2×2
        });
    }

    [Fact]
    public void BT12_112_HandShoutmonIsNotUsed_TrashNotOpened_QA_Q1()
    {
        string id = Uid("B77-X7N-"), a = Uid("B77-XNA-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = id, Name = "테스트X7N", MainText = "디지크로스 -2:「테스트엔에이」" },
            new KoreanCard { CardId = a, Name = "테스트엔에이" },
        }, () =>
        {
            var x7 = Digimon(id, 7, Bt12112, playCost: 15);
            var (state, engine) = NewMainPhase(memory: 10);
            var handShout = Digimon("B77-HSHOUT", 3, name: "Shoutmon");
            state.Player.Hand.Add(handShout);
            var ma = Digimon(a, 3);
            state.Player.Trash.Add(ma);
            state.Player.Hand.Add(x7);
            engine.PlayCard(x7);
            Assert.Contains(handShout, state.Player.Hand);
            Assert.Contains(ma, state.Player.Trash);
            Assert.Contains(state.ActionLogs, l => l.Contains("(코스트 15 지불)"));
        });
    }

    private const string Bt10111 = "[On Play] You may return 1 card with a DigiXros requirement from your trash to your hand. When you would DigiXros this turn, this Digimon may replace 1 of the DigiXros requirements.";

    [Fact]
    public void BT10_111_ReplacesOneDigiXrosRequirement_ThisTurn_QA_Q1()
    {
        string id = Uid("B77-X4B-");
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트X4B", MainText = "디지크로스-2:「테스트X4」×「테스트벨제」" } }, () =>
        {
            var king = Digimon(Uid("B77-KING-"), 4, Bt10111, name: "B77 King");
            AllSupported(king);
            var x4b = Digimon(id, 6, playCost: 9);
            var (state, engine) = NewMainPhase(memory: 10);
            var k = Put(state.Player, king);
            state.Player.Trash.Add(x4b);
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, k, king, includeInherited: false);
            Assert.Contains(x4b, state.Player.Hand);
            Assert.True(engine.PlayCard(x4b).CanExecute);
            var me = state.Player.BattleArea.Single(d => ReferenceEquals(d.TopCard, x4b));
            Assert.Contains(king, me.EvolutionSources);
            Assert.Equal(3, state.MemoryGauge);   // 10 - (9 - 2)
        });
    }

    [Fact]
    public void BT10_111_NotForDistinctNumberSlot_15_15_6_2()
    {
        string id = Uid("B77-XD-");
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트XD", MainText = "디지크로스 -1 : 특징으로 「테스트특징」을 가진 카드 넘버가 서로 다른 디지몬 카드 ∞장" } }, () =>
        {
            var king = Digimon(Uid("B77-KING2-"), 4, Bt10111, name: "B77 King");
            var xd = Digimon(id, 6, playCost: 9);
            var (state, engine) = NewMainPhase(memory: 10);
            var k = Put(state.Player, king);
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, k, king, includeInherited: false);
            state.Player.Hand.Add(xd);
            engine.PlayCard(xd);
            Assert.Contains(k, state.Player.BattleArea);
        });
    }

    [Fact]
    public void BT10_111_WithoutOnPlay_NotASubstitute()
    {
        string id = Uid("B77-X4C-");
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트X4C", MainText = "디지크로스-2:「테스트X4」×「테스트벨제」" } }, () =>
        {
            var king = Digimon(Uid("B77-KING3-"), 4, Bt10111, name: "B77 King");
            var x4c = Digimon(id, 6, playCost: 9);
            var (state, engine) = NewMainPhase(memory: 10);
            var k = Put(state.Player, king);
            state.Player.Hand.Add(x4c);
            engine.PlayCard(x4c);
            Assert.Contains(k, state.Player.BattleArea);
        });
    }

    // =====================================================================
    // 4. EX6-006 [육성]【상대의 턴 종료 시】
    // =====================================================================

    [Theory]
    [InlineData(7, true)]
    [InlineData(6, false)]
    public void EX6_006_DeletesItselfWithSevenDistinctNames_PlaysOgudomon(int distinct, bool expected)
    {
        var gate = Egg(Uid("B77-GATE-"));
        gate.EffectText = "[Breeding][End of Opponent's Turn] By deleting this Digimon with 7 or more cards with different names in its digivolution cards, you may play 1 [B77 Ogudomon] from your trash without paying the cost.";
        AllSupported(gate);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var b = new CardInstance(gate, 1);
        for (int i = 0; i < distinct; i++) b.AddBottomSource(Digimon("B77-DL" + i, 6, name: "B77 Lord " + i));
        b.AddBottomSource(Digimon("B77-DLX", 6, name: "B77 Lord 0"));   // 같은 명칭은 1장으로 센다
        state.Player.BreedingArea = b;
        var og = Digimon("B77-OG", 7, name: "B77 Ogudomon", playCost: 16);
        state.Player.Trash.Add(og);
        engine.RunBreedingEffects42(EffectTrigger.EndOfOpponentsTurn, state.Player, null, null);
        Assert.Equal(expected, state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, og)));
        Assert.Equal(expected, state.Player.BreedingArea == null);
    }

    // =====================================================================
    // 5. 진화 대상 · 비용 조합 — BT13-055 · BT26-097 · BT12-089
    // =====================================================================

    [Fact]
    public void BT13_055_HandMain_DigivolvesForCost3IgnoringRequirements()
    {
        var la = Digimon(Uid("B77-LA-"), 5, "[Hand] [Main] If you have a [B77 Ruli] in play, by placing 1 [B77 Symbare] from your hand under 1 of your [B77 Angora] as its bottom digivolution card, digivolve that Digimon into this card for a digivolution cost of 3, ignoring this card's digivolution requirements.", color: CardColor.Green, playCost: 8, evoCost: 3);
        AllSupported(la);
        var (state, engine) = NewMainPhase(memory: 6);
        engine.AddTamer(state.Player, Card("B77-RULI", "", CardType.Tamer, name: "B77 Ruli"));
        var an = Put(state.Player, Digimon("B77-ANG", 3, name: "B77 Angora", color: CardColor.Red));
        var sy = Digimon("B77-SYM", 4, name: "B77 Symbare");
        state.Player.Hand.Add(sy);
        state.Player.Hand.Add(la);
        Assert.True(engine.ActivateHandEffect(la).CanExecute);
        Assert.Same(la, an.TopCard);
        Assert.Contains(sy, an.EvolutionSources);
        Assert.Equal(3, state.MemoryGauge);
    }

    [Fact]
    public void BT26_097_TamerUnderAegiomon_DigivolveFree_ThenTopSourceFromTrash()
    {
        var op = Card(Uid("B77-OPJ-"), "[Main] By placing 1 of your Tamers with [B77 Dan] or [B77 Kanan] in their names as any of your [B77 Aegio]'s bottom digivolution card, it may digivolve into [B77 Jupiter] in the hand or trash, ignoring digivolution requirements and without paying the cost. After, you may place 1 card with [B77 Aegiochus] in its name in your trash as any of your [B77 Jupiter]'s top digivolution card.", CardType.Option, playCost: 0);
        AllSupported(op);
        var (state, engine) = NewMainPhase(memory: 5);
        engine.PlayerChoices = new AcceptAll();
        var ae = Put(state.Player, Digimon("B77-AEG", 4, name: "B77 Aegio"));
        var dan = Card("B77-DAN", "", CardType.Tamer, name: "B77 Dan Yuki");
        engine.AddTamer(state.Player, dan);
        var ju = Digimon("B77-JUP", 6, name: "B77 Jupiter", color: CardColor.Red, evoCost: 4);
        state.Player.Hand.Add(ju);
        var ag = Digimon("B77-AGC", 5, name: "B77 Aegiochus: Blue");
        state.Player.Trash.Add(ag);
        state.Player.Hand.Add(op);
        engine.PlayCard(op, ae);
        Assert.Same(ju, ae.TopCard);
        Assert.Contains(dan, ae.EvolutionSources);
        Assert.Same(ag, ae.EvolutionSources.First());   // 진화원 맨 위
        Assert.Equal(5, state.MemoryGauge);
    }

    [Fact]
    public void BT12_089_DigivolvedDigimonGetsDp_OnlyIfDigivolved_QA_Q1()
    {
        var t = Card(Uid("B77-TY-"), "[Main] [Once Per Turn] By placing this Tamer and 1 [B77 Growl] and 1 [B77 WarGrowl] from your Trash in any order as the bottom digivolution cards of one of your [B77 Guil], that Digimon may digivolve into [B77 Gallant] in your hand for its digivolution cost, ignoring its level. The Digimon digivolved by this effect gets +2000 DP for the turn.", CardType.Tamer);
        AllSupported(t);
        var (state, engine) = NewMainPhase(memory: 8);
        engine.PlayerChoices = new AcceptAll();
        engine.AddTamer(state.Player, t);
        var g = Put(state.Player, Digimon("B77-GUIL", 3, name: "B77 Guil", color: CardColor.Red));
        state.Player.Trash.Add(Digimon("B77-GROWL", 4, name: "B77 Growl"));
        state.Player.Trash.Add(Digimon("B77-WGROWL", 5, name: "B77 WarGrowl"));
        var gal = Digimon("B77-GAL", 6, name: "B77 Gallant", color: CardColor.Red, dp: 12000, evoCost: 4);
        state.Player.Hand.Add(gal);
        engine.ActivateMainAbility(null, t);
        engine.RecalculateFieldBuffs();
        Assert.Same(gal, g.TopCard);
        Assert.Contains(t, g.EvolutionSources);
        Assert.Equal(14000, g.GetCurrentDP());
        Assert.Equal(4, state.MemoryGauge);
    }

    [Fact]
    public void BT12_089_NoGallantInHand_PlacedCardsStay_NoDp_QA_Q1()
    {
        var t = Card(Uid("B77-TY2-"), "[Main] [Once Per Turn] By placing this Tamer and 1 [B77 Growl] and 1 [B77 WarGrowl] from your Trash in any order as the bottom digivolution cards of one of your [B77 Guil], that Digimon may digivolve into [B77 Gallant] in your hand for its digivolution cost, ignoring its level. The Digimon digivolved by this effect gets +2000 DP for the turn.", CardType.Tamer);
        var (state, engine) = NewMainPhase(memory: 8);
        engine.PlayerChoices = new AcceptAll();
        engine.AddTamer(state.Player, t);
        var guil = Digimon("B77-GUIL2", 3, name: "B77 Guil", color: CardColor.Red);
        var g = Put(state.Player, guil);
        state.Player.Trash.Add(Digimon("B77-GROWL2", 4, name: "B77 Growl"));
        state.Player.Trash.Add(Digimon("B77-WGROWL2", 5, name: "B77 WarGrowl"));
        engine.ActivateMainAbility(null, t);
        engine.RecalculateFieldBuffs();
        Assert.Same(guil, g.TopCard);
        Assert.Equal(3, g.EvolutionSources.Count);
        Assert.Equal(3000, g.GetCurrentDP());
    }

    // =====================================================================
    // 6. BT11-107 기준으로 할 디지몬 고르기
    // =====================================================================

    private const string Bt11107 = "[Main] Choose any number of your opponent's Digimon and/or Tamers whose combined play costs are less than or equal to the play cost of 1 of your Digimon with [Greymon] in its name, and delete all of the chosen cards.";

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void BT11_107_ChoosesReferenceDigimon(bool pickLowest, bool deleted)
    {
        var op = Card(Uid("B77-OPG-"), Bt11107, CardType.Option, playCost: 0);
        var (state, engine) = NewMainPhase(memory: 5);
        engine.PlayerChoices = pickLowest ? new PickLowestReference() : new AcceptAll();
        Put(state.Player, Digimon("B77-G1", 3, name: "Greymon", playCost: 4));
        Put(state.Player, Digimon("B77-G2", 6, name: "WarGreymon", playCost: 12));
        var f1 = Put(state.DummyOpponent, Digimon("B77-F1", 4, playCost: 5));
        state.Player.Hand.Add(op);
        engine.PlayCard(op);
        Assert.Equal(deleted, !state.DummyOpponent.BattleArea.Contains(f1));
    }

    // =====================================================================
    // 7. ST10-06 · EX6-029 (같은 뜻 다른 표기 · 상대의 시큐리티를 N장이 되도록)
    // =====================================================================

    [Fact]
    public void ST10_06_NotDnaDigivolving_PlacesAndShufflesOnly_QA_Q1()
    {
        var card = Digimon(Uid("B77-MAS-"), 6, "[When Digivolving] Place 1 yellow or purple Digimon card from your trash on top of your security stack face down. When DNA digivolving, you may search your security stack for 1 level 5 or lower Digimon card and play it without paying its memory cost. Then, shuffle your security stack.", color: CardColor.Yellow);
        AllSupported(card);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        state.Player.Trash.Add(Digimon("B77-YEL", 4, color: CardColor.Yellow));
        var d = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, card, includeInherited: false);
        Assert.Equal(6, state.Player.SecurityStack.Count);
        Assert.Single(state.Player.BattleArea);
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(3, 3)]
    public void EX6_029_TrashUntilFourLeft(int before, int after)
    {
        var card = Digimon(Uid("B77-TU-"), 6, "[On Play] Trash cards from the top of your opponent's security stack until it has 4 left.");
        AllSupported(card);
        var (state, engine) = NewMainPhase();
        while (state.DummyOpponent.SecurityStack.Count > before) state.DummyOpponent.SecurityStack.RemoveAt(0);
        var d = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, card, includeInherited: false);
        Assert.Equal(after, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void EX6_029_WholeClauseSupported()
    {
        var card = Digimon(Uid("B77-ANG-"), 6, "[On Play] [When Digivolving] You may play 1 level 5 or lower Digimon card with the [Angel], [Archangel] or [Fallen Angel] trait from your hand or trash without paying the cost. Then, if DNA digivolving, place 1 other Digimon at the bottom of its owner's security stack, and trash cards from the top of your opponent's security stack until it has 4 left.");
        AllSupported(card);
    }

    // =====================================================================
    // 8. 한글
    // =====================================================================

    [Fact]
    public void Korean_Batch77()
    {
        Assert.Contains("이 카드 아래에 놓는 것으로, 지불하는 등장 코스트를 -1 하고, 자신의 트래시의 카드도 디지크로스로 놓을 수 있다.", Ko(Bt12112));
        Assert.Contains("이 카드 아래에 놓는 것으로, 지불하는 등장 코스트를 -1 하고, 자신의 트래시의 카드도 디지크로스로 놓을 수 있다.", Ko(Bt21030));
        Assert.Contains("이 턴 동안 자신이 디지크로스할 때, 이 디지몬은 디지크로스 조건 1장을 대신할 수 있다.", Ko(Bt10111));
        Assert.Contains("진화원에 명칭이 서로 다른 카드가 7장 이상 있는 이 디지몬을 소멸시키는 것으로", Ko("[Breeding][End of Opponent's Turn] By deleting this Digimon with 7 or more cards with different names in its digivolution cards, you may play 1 [Ogudomon] from your trash without paying the cost."));
        Assert.Contains("이 효과로 진화했다면, 이 턴 동안, 그 디지몬의 DP를 +2000 한다.", Ko("[Main] [Once Per Turn] By placing this Tamer and 1 [Growlmon] and 1 [WarGrowlmon] from your Trash in any order as the bottom digivolution cards of one of your [Guilmon], that Digimon may digivolve into [Gallantmon] in your hand for its digivolution cost, ignoring its level. The Digimon digivolved by this effect gets +2000 DP for the turn."));
        Assert.Contains("상대의 시큐리티가 4장이 되도록 위에서부터 파기한다.", Ko("[On Play] [When Digivolving] You may play 1 level 5 or lower Digimon card with the [Angel], [Archangel] or [Fallen Angel] trait from your hand or trash without paying the cost. Then, if DNA digivolving, place 1 other Digimon at the bottom of its owner's security stack, and trash cards from the top of your opponent's security stack until it has 4 left."));
        Assert.Contains("그 후, 자신의 시큐리티를 셔플한다.", Ko("[When Digivolving] Place 1 yellow or purple Digimon card from your trash on top of your security stack face down. When DNA digivolving, you may search your security stack for 1 level 5 or lower Digimon card and play it without paying its memory cost. Then, shuffle your security stack."));
    }

    [Fact]
    public void Korean_SameWithAndWithoutMemo_Batch77()
    {
        var clauses = new[]
        {
            Bt12112, Bt21030, Bt10111,
            "[Breeding][End of Opponent's Turn] By deleting this Digimon with 7 or more cards with different names in its digivolution cards, you may play 1 [Ogudomon] from your trash without paying the cost.",
            "[Main] By placing 1 of your Tamers with [Dan Yuki] or [Kanan Yuki] in their names as any of your [Aegiomon]'s bottom digivolution card, it may digivolve into [Jupitermon] in the hand or trash, ignoring digivolution requirements and without paying the cost. After, you may place 1 card with [Aegiochusmon] in its name in your trash as any of your [Jupitermon]'s top digivolution card.",
            "[Hand] [Main] If you have a [Ruli Tsukiyono] in play, by placing 1 [SymbareAngoramon] from your hand under 1 of your [Angoramon] as its bottom digivolution card, digivolve that Digimon into this card for a digivolution cost of 3, ignoring this card's digivolution requirements.",
        };
        var cached = clauses.Select(Ko).ToList();
        Assert.DoesNotContain("<null>", cached);
        KoreanEffectRenderer.MemoEnabled52 = false;
        try { Assert.Equal(clauses.Select(Ko).ToList(), cached); }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }
}
