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
/// v0.47.0 미지원 문장 21차 묶음: 공식 한글 〔진화〕 진화 조건 (종합 룰 2-3-5-3) · 영어 데이터에서 잘린 문장 · [육성]【자신의 메인 페이즈 개시 시】 디지타마 덱 ·
/// 효과 하나로 함께 등장한 카드는 동시에 등장 (종합 룰 3-1-3-3 · 15-5-3 · 공식 Q&A ST12-04 Q1)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch47Tests
{
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => ConfirmAnswer;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) =>
            recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
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

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B47-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B47-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B47-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B47-OSEC" + i, 3, dp: 1000, color: CardColor.White));
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

    private static int CountAll(PlayerZones z) =>
        z.MainDeck.Count + z.Hand.Count + z.SecurityStack.Count + z.Trash.Count + z.Tamers.Count + z.OptionsInPlay.Count + z.DigitamaDeck.Count
        + z.BattleArea.Sum(d => 1 + d.EvolutionSources.Count + d.LinkedCards.Count) + (z.BreedingArea is { } b ? 1 + b.EvolutionSources.Count : 0);

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b47_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    // =====================================================================
    // 1. 공식 한글 〔진화〕 진화 조건
    // =====================================================================

    [Fact]
    public void KoreanEvo_Parse_GroupsAndCosts()
    {
        var p = KoreanEvolution47.Parse("〔진화〕「테스트A」/「테스트B」 : 코스트 1/특징 「테스트특징」를 가진 Lv.3 : 코스트 2【등장 시】 어쩌고");
        Assert.Equal(2, p.Conditions.Count);   // 「테스트A」/「테스트B」는 조건 하나 (A 또는 B)
        Assert.Equal(new[] { 1, 2 }, p.Conditions.Select(c => c.Cost));
        Assert.Empty(p.Unparsed);
        var bad = KoreanEvolution47.Parse("〔진화〕특징으로 「하이브리드체」를 가진 카드가 아래에 5장 있는 「테스트테이머」 : 코스트 5");
        Assert.Empty(bad.Conditions);
        Assert.Single(bad.Unparsed);
        // 이름에 ":"가 들어 있어도
        var colon = KoreanEvolution47.Parse("〔진화〕「저스티몬: 블리츠 암」/「저스티몬: 액셀 암」 : 코스트 1");
        Assert.Equal(1, Assert.Single(colon.Conditions).Cost);
    }

    [Fact]
    public void KoreanEvo_NameOnly_AllowsNormalDigivolve_FromOtherLevelAndColor_AndExactNameOnly()
    {
        string evoId = Uid("B47-AGU-"), koroId = Uid("B47-KORO-"), koroXId = Uid("B47-KOROX-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트아구몬", MainText = "〔진화〕「테스트코로몬」 : 코스트 0【등장 시】 1장 드로우." },
            new KoreanCard { CardId = koroId, Name = "테스트코로몬", MainText = "" },
            new KoreanCard { CardId = koroXId, Name = "테스트코로몬X", MainText = "" },
        }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var agu = Digimon(evoId, 4, name: "TestAgumon", color: CardColor.Black, evoCost: 3);   // 영어 진화 조건: Lv.3 블랙
            var koro = Put(state.Player, Digimon(koroId, 2, name: "TestKoromon", color: CardColor.Red));
            var koroX = Put(state.Player, Digimon(koroXId, 2, name: "TestKoromonX", color: CardColor.Red));
            Assert.True(ActionValidator.ValidateEvolution(koro, agu).CanExecute);
            Assert.Equal(0, ActionValidator.EvolutionCost(koro, agu));
            Assert.False(ActionValidator.ValidateEvolution(koroX, agu).CanExecute);   // 종합 룰 2-3-1-2 — 명칭 일치 (포함 아님)
            state.Player.Hand.Add(agu);
            int mem = state.MemoryGauge;
            Assert.True(engine.EvolveDigimon(koro, agu).CanExecute);
            Assert.Same(agu, koro.TopCard);
            Assert.Equal(mem, state.MemoryGauge);   // 코스트 0
        });
    }

    [Fact]
    public void KoreanEvo_TraitLevel_NameContains_AndCheaperCostWins()
    {
        string evoId = Uid("B47-LILY-"), advId = Uid("B47-ADV-"), grId = Uid("B47-GR-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트릴리몬", MainText = "〔진화〕특징 「테스트어드벤처」를 가진 Lv.4 : 코스트 2/명칭에 「그레이」을 포함하는 Lv.4 : 코스트 1" },
            new KoreanCard { CardId = advId, Name = "테스트토게몬", MainText = "", Traits = new List<string> { "테스트어드벤처" } },
            new KoreanCard { CardId = grId, Name = "테스트그레이몬", MainText = "" },
        }, () =>
        {
            var (state, _, _) = NewMainPhase();
            var lily = Digimon(evoId, 5, name: "TestLillymon", color: CardColor.Green, evoCost: 3);
            var adv = Put(state.Player, Digimon(advId, 4, name: "TestTogemon", color: CardColor.Purple));
            var gr = Put(state.Player, Digimon(grId, 4, name: "TestGreymon", color: CardColor.Red));
            var gr3 = Put(state.Player, Digimon(Uid("B47-GR3-"), 3, name: "TestGreymon3", color: CardColor.Red));
            Assert.True(ActionValidator.ValidateEvolution(adv, lily).CanExecute);
            Assert.Equal(2, ActionValidator.EvolutionCost(adv, lily));
            Assert.True(ActionValidator.ValidateEvolution(gr, lily).CanExecute);
            Assert.Equal(1, ActionValidator.EvolutionCost(gr, lily));
            Assert.False(ActionValidator.ValidateEvolution(gr3, lily).CanExecute);   // Lv.4만
            var green4 = Put(state.Player, Digimon(Uid("B47-G4-"), 4, color: CardColor.Green));
            Assert.Equal(3, ActionValidator.EvolutionCost(green4, lily));   // 보통 진화 조건은 그대로
        });
    }

    [Fact]
    public void KoreanEvo_TamerName_DigivolvesFromTamer()
    {
        string evoId = Uid("B47-AGUNI-"), tamerId = Uid("B47-TAKUYA-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트아그니몬", MainText = "〔진화〕「테스트우정훈」 : 코스트 2" },
            new KoreanCard { CardId = tamerId, Name = "테스트우정훈", Type = "테이머", MainText = "" },
        }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var aguni = Digimon(evoId, 4, name: "TestAgunimon", color: CardColor.Red, evoCost: 3);
            var takuya = Card(tamerId, type: CardType.Tamer, name: "TestTakuya", color: CardColor.Red);
            state.Player.Tamers.Add(takuya);
            state.Player.Hand.Add(aguni);
            Assert.Equal(2, CardScripts.TamerEvolutionCost(aguni, takuya));
            Assert.True(engine.EvolveTamer(takuya, aguni).CanExecute, string.Join(" | ", state.ActionLogs));
            var d = Assert.Single(state.Player.BattleArea);
            Assert.Same(aguni, d.TopCard);
            Assert.Empty(state.Player.Tamers);
        });
    }

    [Fact]
    public void KoreanEvo_FieldCondition_UsesOwnersField()
    {
        string evoId = Uid("B47-LM-"), aguId = Uid("B47-LMAGU-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트샤인그레이몬", MainText = "〔진화〕자신의 시큐리티가 2장 이하인 동안, 「테스트아구몬2」 : 코스트 3" },
            new KoreanCard { CardId = aguId, Name = "테스트아구몬2", MainText = "" },
        }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var shine = Digimon(evoId, 6, name: "TestShine", color: CardColor.Red, evoCost: 4);
            var agu = Put(state.Player, Digimon(aguId, 3, name: "TestAgu2", color: CardColor.Red));
            engine.RecalculateFieldBuffs();
            Assert.False(ActionValidator.ValidateEvolution(agu, shine).CanExecute);   // 시큐리티 5장
            while (state.Player.SecurityStack.Count > 2) state.Player.SecurityStack.RemoveAt(0);
            Assert.True(ActionValidator.ValidateEvolution(agu, shine).CanExecute);
            Assert.Equal(3, ActionValidator.EvolutionCost(agu, shine));
        });
    }

    [Fact]
    public void KoreanEvo_SourcesCondition()
    {
        string evoId = Uid("B47-SIR-"), arcId = Uid("B47-ARC-"), sirId = Uid("B47-SIRO-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트시리우스몬2", MainText = "〔진화〕진화원에 「테스트아크투루스몬」이 있는 「테스트시리우스몬」 : 코스트 3" },
            new KoreanCard { CardId = arcId, Name = "테스트아크투루스몬", MainText = "" },
            new KoreanCard { CardId = sirId, Name = "테스트시리우스몬", MainText = "" },
        }, () =>
        {
            var (state, _, _) = NewMainPhase();
            var into = Digimon(evoId, 7, name: "TestSirius2", color: CardColor.White);
            var with = Put(state.Player, Digimon(sirId, 6, name: "TestSirius", color: CardColor.Red), sources: Digimon(arcId, 5, name: "TestArc"));
            var without = Put(state.Player, Digimon(sirId, 6, name: "TestSirius", color: CardColor.Red));
            Assert.True(ActionValidator.ValidateEvolution(with, into).CanExecute);
            Assert.False(ActionValidator.ValidateEvolution(without, into).CanExecute);
        });
    }

    // =====================================================================
    // 2. 영어 데이터에서 잘린 문장 (카드 번호 · 원문이 정확히 같을 때만)
    // =====================================================================

    [Fact]
    public void TruncatedDualOptionSide_EX13_065_ReadAsOfficialStructure()
    {
        var dual = new CardData
        {
            CardId = "EX13-065", Name = "Sistermon Blanc (Awakened)", Type = CardType.Digimon, Level = 3, Color = CardColor.White, IsDual = true, PlayCost = 5,
            EffectText = "＜Guard＞ (When any of your other Digimon would leave the battle area by your opponent's effects, by deleting this Digimon, they don't leave.)",
            InheritedEffectText = "[Main] You may play 1 play cost 4 or lower card with from your hand or trash without paying the cost. Then, to 1 of your opponent's Digimon, give -3000 DP for the turn for each of your Digimon.",
        };
        Assert.All(EffectSupport.For(dual).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        // 다른 카드 번호의 같은 문장은 고치지 않는다
        var other = new CardData { CardId = "B47-NOTEX13", Name = "X", Type = CardType.Option, PlayCost = 3, EffectText = dual.InheritedEffectText };
        Assert.Contains(EffectSupport.For(other).Clauses, c => c.Support != ClauseSupport.Supported);
        // 한글은 고친 문장으로
        Assert.Contains("「시스터몬」", KoreanEffectRenderer.RenderClause(dual.InheritedEffectText) ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedDualOptionSide_BT26_056_DeDigivolve3()
    {
        var (state, engine, _) = NewMainPhase();
        var dual = new CardData
        {
            CardId = "BT26-056", Name = "Cerberusmon: Werewolf Mode", Type = CardType.Digimon, Level = 5, Color = CardColor.Black, IsDual = true, PlayCost = 3,
            InheritedEffectText = "[Main] Trash 1 card in your hand. Then,",
        };
        Assert.All(EffectSupport.For(dual).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        var opp = Put(state.DummyOpponent, Digimon("B47-OPP6", 6), sources: new[] { Digimon("B47-OPP5", 5), Digimon("B47-OPP4", 4), Digimon("B47-OPP3", 3) });
        state.Player.Hand.Add(Digimon("B47-JUNK", 3));
        var side = CardScripts.OptionSideFor(dual)!;
        var main = Assert.Single(side.Effects.Where(e => e.Trigger == EffectTrigger.Main));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, dual, includeInherited: false, optionSide: true);
        Assert.Empty(state.Player.Hand);
        Assert.Equal(3, opp.TopCard.Level);   // ≪퇴화 3≫ — Lv.3에서 멈춘다
    }

    [Fact]
    public void DigiEggBrokenDuplicate_P245_NotRegisteredTwice()
    {
        var egg = new CardData
        {
            CardId = "P-245", Name = "Kakkinmon", Type = CardType.Digitama, Level = 2, Color = CardColor.Black,
            EffectText = "Inherited Effect [End of All Turns] [Once Per Turn] By suspending 1 of your black Digimon with ＜Blocker＞, if your hand has 7 or fewer cards, ＜Draw 1＞ (Draw 1 card from your deck.)",
            InheritedEffectText = "[Once Per Turn] By suspending 1 of your Digimon with , if your hand has 7 or fewer cards, Draw 1",
        };
        Assert.All(EffectSupport.For(egg).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        var only = new CardData { CardId = "B47-P245MAIN", Name = egg.Name, Type = egg.Type, Level = 2, Color = egg.Color, EffectText = egg.EffectText };
        Assert.Equal(CardScripts.For(only)!.Effects.Count(e => e.Source == EffectSource.Inherited), CardScripts.For(egg)!.Effects.Count(e => e.Source == EffectSource.Inherited));   // 깨진 복사본은 등록하지 않는다
        Assert.Contains("블로커", KoreanEffectRenderer.RenderClause(egg.InheritedEffectText) ?? "", StringComparison.Ordinal);
    }

    // =====================================================================
    // 3. [육성]【자신의 메인 페이즈 개시 시】 디지타마 덱
    // =====================================================================

    [Fact]
    public void BT13_007_Breeding_RevealEggAndPlaceRoyalKnightsUnder()
    {
        var (state, engine, _) = NewMainPhase();
        var kd = new CardData
        {
            CardId = "B47-KD7D6", Name = "King Drasil_7D6", Type = CardType.Digitama, Level = 2, Color = CardColor.White,
            EffectText = "[Breeding] [Start of Your Main Phase] Reveal the top card of your Digi-Egg deck, then place that card and all of your [Royal Knight] trait Digimon as this Digimon's bottom digivolution cards.",
        };
        Assert.All(EffectSupport.For(kd).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        state.Player.BreedingArea = new CardInstance(kd, 1);
        var egg = Card("B47-EGG", type: CardType.Digitama, level: 2);
        state.Player.DigitamaDeck.Push(egg);
        var rk = Put(state.Player, Digimon("B47-RK", 6, traits: "Royal Knight"), sources: Digimon("B47-RK5", 5));
        var other = Put(state.Player, Digimon("B47-NORK", 5));
        int total = CountAll(state.Player);
        engine.RunBreedingEffects42(EffectTrigger.StartOfYourMainPhase, state.Player, null, null);
        var b = state.Player.BreedingArea!;
        Assert.True(b.HasSource("B47-EGG"));
        Assert.True(b.HasSource("B47-RK"));
        Assert.DoesNotContain(rk, state.Player.BattleArea);
        Assert.Contains(other, state.Player.BattleArea);
        Assert.Contains(state.Player.Trash, c => c.CardId == "B47-RK5");   // 아래에 놓이는 디지몬의 진화원은 파기 (종합 룰 4-7-8)
        Assert.Equal(total, CountAll(state.Player));
    }

    [Fact]
    public void BT22_007_Breeding_LookPlaceMotherEaterOnTop_ThenPlayThree()
    {
        var (state, engine, _) = NewMainPhase();
        CardData Mother(string id) => new CardData
        {
            CardId = id, Name = "Mother Eater", Type = CardType.Digitama, Level = 2, Color = CardColor.Purple,
            EffectText = "[Breeding] [Start of Your Main Phase] Look at your Digi-Egg deck's top card. Among them, you may place [Mother Eater]s as this Digimon's top digivolution cards. Then, if this Digimon has 10 or more digivolution cards, you may play 3 [Mother Eater]s from its digivolution cards without paying the costs.",
        };
        var host = new CardInstance(Mother("B47-ME0"), 1);
        for (int i = 0; i < 9; i++) host.AddBottomSource(i < 4 ? Mother("B47-ME" + (i + 1)) : Digimon("B47-EAT" + i, 3, traits: "Eater"));
        state.Player.BreedingArea = host;
        state.Player.DigitamaDeck.Push(Mother("B47-METOP"));
        int total = CountAll(state.Player);
        engine.RunBreedingEffects42(EffectTrigger.StartOfYourMainPhase, state.Player, null, null);
        Assert.Equal(3, state.Player.BattleArea.Count(d => d.TopCard.Name == "Mother Eater"));
        Assert.Equal(7, host.EvolutionSources.Count);   // 9 + 1 - 3
        Assert.Empty(state.Player.DigitamaDeck);
        Assert.Equal(total, CountAll(state.Player));
    }

    [Fact]
    public void BT22_007_NotMotherEaterOnTop_StaysInEggDeck()
    {
        var (state, engine, _) = NewMainPhase();
        var me = new CardData
        {
            CardId = "B47-MEX", Name = "Mother Eater", Type = CardType.Digitama, Level = 2,
            EffectText = "[Breeding] [Start of Your Main Phase] Look at your Digi-Egg deck's top card. Among them, you may place [Mother Eater]s as this Digimon's top digivolution cards. Then, if this Digimon has 10 or more digivolution cards, you may play 3 [Mother Eater]s from its digivolution cards without paying the costs.",
        };
        state.Player.BreedingArea = new CardInstance(me, 1);
        var other = Card("B47-OTHEREGG", type: CardType.Digitama, level: 2, name: "Koromon");
        state.Player.DigitamaDeck.Push(other);
        engine.RunBreedingEffects42(EffectTrigger.StartOfYourMainPhase, state.Player, null, null);
        Assert.Same(other, state.Player.DigitamaDeck.Peek());
        Assert.Empty(state.Player.BreedingArea!.EvolutionSources);
    }

    // =====================================================================
    // 4. 효과 하나로 함께 등장 = 동시에 등장
    // =====================================================================

    private const string PlayTwo = "[Main] You may play up to 2 Digimon cards with [Twin] in their names from your hand without paying the cost.";

    [Fact]
    public void SimultaneousPlay_TamerWhenPlayed_TriggersOnce()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Card("B47-HUCK", "[Your Turn] When any of your Digimon are played, gain 1 memory.", CardType.Tamer);
        state.Player.Tamers.Add(tamer);
        var opt = Card("B47-PLAY2", PlayTwo, playCost: 0);
        Assert.All(EffectSupport.For(opt).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        state.Player.Hand.Add(Digimon("B47-TWIN1", 3, name: "Twinmon A"));
        state.Player.Hand.Add(Digimon("B47-TWIN2", 3, name: "Twinmon B"));
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(2, state.Player.BattleArea.Count);
        Assert.Equal(mem + 1, state.MemoryGauge);   // 공식 Q&A ST12-04 Q1 — 1번만
    }

    [Fact]
    public void SimultaneousPlay_PlayedTogether_SeeEachOther()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B47-PLAY2B", PlayTwo, playCost: 0);
        string watcher = "[Your Turn] When any of your other Digimon are played, gain 1 memory.";
        state.Player.Hand.Add(Digimon("B47-TWINW1", 3, watcher, name: "Twinmon A"));
        state.Player.Hand.Add(Digimon("B47-TWINW2", 3, watcher, name: "Twinmon B"));
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(2, state.Player.BattleArea.Count);
        Assert.Equal(mem + 2, state.MemoryGauge);   // 둘 다 서로의 등장에 반응 (종합 룰 15-5-3) — 전에는 먼저 등장한 쪽만
    }

    [Fact]
    public void SimultaneousPlay_OnPlayRunsAfterEffect_AndStillRuns()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B47-PLAY2C", PlayTwo, playCost: 0);
        state.Player.Hand.Add(Digimon("B47-TWO1", 3, "[On Play] Gain 1 memory.", name: "Twinmon A"));
        state.Player.Hand.Add(Digimon("B47-TWO2", 3, "[On Play] Gain 1 memory.", name: "Twinmon B"));
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(mem + 2, state.MemoryGauge);   // 【등장 시】는 각각
    }
}
