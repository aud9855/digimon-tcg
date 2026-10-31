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
/// v0.48.0 미지원 문장 22차 묶음: 공식 영어 카드 리스트의 진화 조건 (여러 색 · 두 번째 칸 · 한글 데이터가 없는 카드의 [Special Digivolution Condition]) ·
/// 테이머 아래 카드 조건 · 테이머 수 조건 · 필드 조건이 붙은 테이머 이름 조건 · BT13-110 · 덱 아래로 되돌리기 · 등장 조건 다른 표기
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch48Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, int evoCost = 1, CardColor? second = null, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, SecondaryColor = second, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int evoCost = 1, CardColor? second = null, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, 3, color, -1, name, evoCost, second, traits);

    private static CardData Tamer(string id, string name, CardColor color = CardColor.Red, params string[] traits) =>
        Card(id, "", CardType.Tamer, 0, 2, color, 0, name, 0, null, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = 8 };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B48-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B48-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B48-SEC" + i, 3, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B48-OSEC" + i, 3, color: CardColor.White));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, params CardData[] sources)
    {
        var d = new CardInstance(card, 1);
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static EnglishEvoCost Evo(int level, int cost, params string[] colors) => new() { Level = level, Cost = cost, Colors = colors.ToList(), From = "Lv." + level };

    private static void WithEnglish(IEnumerable<EnglishCardRule> rules, Action run)
    {
        EnglishCardRules.Use(rules);
        EvoRequirements48.ClearCache();
        try { run(); }
        finally { EnglishCardRules.Reload(); EvoRequirements48.ClearCache(); }
    }

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b48_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 1. 보통 진화 조건 칸 전부 (공식 영어 카드 리스트)
    // =====================================================================

    [Fact]
    public void NormalEvo_MultiColorRequirement_FromEitherColor()
    {
        string id = Uid("B48-SILPHY-");
        WithEnglish(new[] { new EnglishCardRule { CardId = id, Evo = { Evo(4, 3, "red", "yellow") } } }, () =>
        {
            var (state, _) = NewMainPhase();
            var silphy = Digimon(id, 5, color: CardColor.Red, evoCost: 3);   // API: Lv.4 레드뿐
            var yellow = Put(state.Player, Digimon(Uid("B48-Y4-"), 4, color: CardColor.Yellow));
            var blue = Put(state.Player, Digimon(Uid("B48-B4-"), 4, color: CardColor.Blue));
            Assert.True(ActionValidator.ValidateEvolution(yellow, silphy).CanExecute);
            Assert.Equal(3, ActionValidator.EvolutionCost(yellow, silphy));
            Assert.False(ActionValidator.ValidateEvolution(blue, silphy).CanExecute);
        });
    }

    [Fact]
    public void NormalEvo_SecondRequirement_SameLevelCheaper()
    {
        string id = Uid("B48-ALT-");
        WithEnglish(new[] { new EnglishCardRule { CardId = id, Evo = { Evo(4, 3, "red"), Evo(5, 1, "red") } } }, () =>
        {
            var (state, _) = NewMainPhase();
            var alt = Digimon(id, 5, color: CardColor.Red, evoCost: 3);
            var red5 = Put(state.Player, Digimon(Uid("B48-R5-"), 5, color: CardColor.Red));
            var red4 = Put(state.Player, Digimon(Uid("B48-R4-"), 4, color: CardColor.Red));
            Assert.True(ActionValidator.ValidateEvolution(red5, alt).CanExecute);
            Assert.Equal(1, ActionValidator.EvolutionCost(red5, alt));
            Assert.Equal(3, ActionValidator.EvolutionCost(red4, alt));
        });
    }

    [Fact]
    public void NormalEvo_MultiColorTarget_UsesAllColors_WithoutEnglishData()
    {
        var (state, _) = NewMainPhase();
        var evo = Digimon(Uid("B48-REDREQ-"), 5, color: CardColor.Red);   // 영어 데이터 없음 → API 칸 (Lv.4 레드)
        var blueRed = Put(state.Player, Digimon(Uid("B48-BR4-"), 4, color: CardColor.Blue, second: CardColor.Red));
        Assert.True(ActionValidator.ValidateEvolution(blueRed, evo).CanExecute);   // 종합 룰 2-4-3-1 다색은 가진 색 전부
    }

    [Fact]
    public void NormalEvo_NoLevelRequirementInOfficialList_CannotNormallyDigivolve()
    {
        string id = Uid("B48-XROS-");
        WithEnglish(new[] { new EnglishCardRule { CardId = id } }, () =>
        {
            var (state, _) = NewMainPhase();
            var x4 = Digimon(id, 6, color: CardColor.Red);
            var red5 = Put(state.Player, Digimon(Uid("B48-R5-"), 5, color: CardColor.Red));
            Assert.False(ActionValidator.ValidateEvolution(red5, x4).CanExecute);
        });
    }

    [Fact]
    public void NormalEvo_MulticolorIcon_AnyColor()
    {
        string id = Uid("B48-ALL-");
        WithEnglish(new[] { new EnglishCardRule { CardId = id, Evo = { Evo(6, 6, "all") } } }, () =>
        {
            var (state, _) = NewMainPhase();
            var evo = Digimon(id, 7, color: CardColor.Red, evoCost: 6);
            var purple = Put(state.Player, Digimon(Uid("B48-P6-"), 6, color: CardColor.Purple));
            Assert.True(ActionValidator.ValidateEvolution(purple, evo).CanExecute);
        });
    }

    // =====================================================================
    // 2. 한글 데이터가 없는 카드의 [Special Digivolution Condition]
    // =====================================================================

    [Fact]
    public void EnglishSpecial_Parse_NameTraitAndCosts()
    {
        var p = KoreanEvolution47.ParseEnglish(new[] { "[Digivolve] [Lanamon]: Cost 1/Lv.3 w/[TS] trait: Cost 3" });
        Assert.Empty(p.Unparsed);
        Assert.Equal(new[] { 1, 1, 3 }, p.Conditions.Select(c => c.Cost));   // 이름 조건은 디지몬 쪽 · 테이머 쪽 2개
        var bad = KoreanEvolution47.ParseEnglish(new[] { "[Digivolve] Lv.5 w/[Aegiochusmon] in name: Cost 1 for each of your security cards" });
        Assert.Empty(bad.Conditions);
        Assert.Single(bad.Unparsed);
        var colors = KoreanEvolution47.ParseEnglish(new[] { "[Digivolve] [Dorimon]/Black Lv.2 w/[X Antibody] trait: Cost 0" });
        Assert.Empty(colors.Unparsed);
        Assert.Equal(3, colors.Conditions.Count);
    }

    [Fact]
    public void EnglishSpecial_EnglishOnlyCard_DigivolvesFromTraitLevel()
    {
        string id = Uid("B48-TS4-");
        WithEnglish(new[] { new EnglishCardRule { CardId = id, Evo = { Evo(3, 3, "red") }, SpecialDigivolve = { "[Digivolve] Lv.3 w/[TS] trait: Cost 2" } } }, () =>
        {
            var (state, engine) = NewMainPhase();
            var evo = Digimon(id, 4, color: CardColor.Red, evoCost: 3);
            var ts = Put(state.Player, Digimon(Uid("B48-TS3-"), 3, color: CardColor.Purple, traits: "TS"));
            var other = Put(state.Player, Digimon(Uid("B48-NT3-"), 3, color: CardColor.Purple));
            Assert.True(ActionValidator.ValidateEvolution(ts, evo).CanExecute);
            Assert.Equal(2, ActionValidator.EvolutionCost(ts, evo));
            Assert.False(ActionValidator.ValidateEvolution(other, evo).CanExecute);
            state.Player.Hand.Add(evo);
            int mem = state.MemoryGauge;
            Assert.True(engine.EvolveDigimon(ts, evo).CanExecute);
            Assert.Equal(mem - 2, state.MemoryGauge);
        });
    }

    [Fact]
    public void EnglishSpecial_TamerWithHybridCardsUnder()
    {
        string id = Uid("B48-ALDA-");
        WithEnglish(new[] { new EnglishCardRule { CardId = id, Evo = { Evo(4, 3, "red") }, SpecialDigivolve = { "[Digivolve] [Takuya Kanbara] w/2 or more [Hybrid] trait cards under: Cost 3" } } }, () =>
        {
            var (state, engine) = NewMainPhase();
            var alda = Digimon(id, 5, color: CardColor.Red, evoCost: 3);
            var takuya = Tamer(Uid("B48-TAK-"), "Takuya Kanbara");
            state.Player.Tamers.Add(takuya);
            state.Player.Hand.Add(alda);
            state.Player.UnderTamers.Add(Digimon(Uid("B48-H1-"), 3, traits: "Hybrid"));
            Assert.Null(engine.TamerEvolutionCostFor(state.Player, alda, takuya));   // 1장
            Assert.Null(CardScripts.TamerEvolutionCost(alda, takuya));               // 필드를 모르면 쓰지 않는다
            state.Player.UnderTamers.Add(Digimon(Uid("B48-H2-"), 4, traits: "Hybrid"));
            Assert.Equal(3, engine.TamerEvolutionCostFor(state.Player, alda, takuya));
            state.Player.Tamers.Add(Tamer(Uid("B48-OTHER-"), "Other Tamer"));
            Assert.Null(engine.TamerEvolutionCostFor(state.Player, alda, takuya));   // 테이머가 여럿이면 누구 아래인지 모른다
            state.Player.Tamers.RemoveAt(1);
            Assert.True(engine.EvolveTamer(takuya, alda).CanExecute, string.Join(" | ", state.ActionLogs));
            var d = Assert.Single(state.Player.BattleArea);
            Assert.Same(alda, d.TopCard);
            Assert.Equal(3, d.EvolutionSources.Count);   // 테이머 + 아래 카드 2장
        });
    }

    // =====================================================================
    // 3. 한글 〔진화〕 조건 — 테이머 아래 카드 · 테이머 수 · 필드 조건이 붙은 테이머 이름
    // =====================================================================

    [Fact]
    public void KoreanEvo_TamerWithTraitCardsUnder_BT18_018Style()
    {
        string evoId = Uid("B48-KAIZ-"), tamerId = Uid("B48-WOO-"), h1 = Uid("B48-HY1-"), h2 = Uid("B48-HY2-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트카이젤", MainText = "〔진화〕특징으로 「테스트하이브리드」를 가진 카드가 아래에 2장 있는 「테스트우정훈2」 : 코스트 5" },
            new KoreanCard { CardId = tamerId, Name = "테스트우정훈2", Type = "테이머", MainText = "" },
            new KoreanCard { CardId = h1, Name = "테스트하이브리드1", MainText = "", Traits = new List<string> { "테스트하이브리드" } },
            new KoreanCard { CardId = h2, Name = "테스트하이브리드2", MainText = "", Traits = new List<string> { "테스트하이브리드" } },
        }, () =>
        {
            var (state, engine) = NewMainPhase();
            var kaiz = Digimon(evoId, 6, color: CardColor.Red, evoCost: 4);
            var tamer = Tamer(tamerId, "TestTakuya2");
            state.Player.Tamers.Add(tamer);
            state.Player.UnderTamers.Add(Digimon(h1, 3));
            state.Player.UnderTamers.Add(Digimon(Uid("B48-NOHY-"), 3));
            Assert.Null(engine.TamerEvolutionCostFor(state.Player, kaiz, tamer));   // 특징을 가진 카드 1장
            state.Player.UnderTamers.Add(Digimon(h2, 4));
            Assert.Equal(5, engine.TamerEvolutionCostFor(state.Player, kaiz, tamer));   // 종합 룰 4-26-1 "N장 있다" = N장 이상
        });
    }

    [Fact]
    public void KoreanEvo_NameUnderTamer_And_SecurityFieldTamerName()
    {
        string evoId = Uid("B48-LUMI-"), tamerId = Uid("B48-NENE-"), shadeId = Uid("B48-SHADE-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트루미나", MainText = "〔진화〕「테스트셰이드」이 아래에 있는 「테스트노유라」 : 코스트 3/자신의 시큐리티가 3장 이하인 동안, 「테스트노유라」 : 코스트 5" },
            new KoreanCard { CardId = tamerId, Name = "테스트노유라", Type = "테이머", MainText = "" },
            new KoreanCard { CardId = shadeId, Name = "테스트셰이드", MainText = "" },
        }, () =>
        {
            var (state, engine) = NewMainPhase();
            var lumi = Digimon(evoId, 5, color: CardColor.Yellow, evoCost: 3);
            var nene = Tamer(tamerId, "TestNene", CardColor.Purple);
            state.Player.Tamers.Add(nene);
            Assert.Null(engine.TamerEvolutionCostFor(state.Player, lumi, nene));
            while (state.Player.SecurityStack.Count > 3) state.Player.SecurityStack.RemoveAt(0);
            Assert.Equal(5, engine.TamerEvolutionCostFor(state.Player, lumi, nene));   // 필드 조건이 붙은 테이머 이름 조건 (BT22-063 · BT22-067 모양)
            state.Player.UnderTamers.Add(Digimon(shadeId, 3, name: "TestShade"));
            Assert.Equal(3, engine.TamerEvolutionCostFor(state.Player, lumi, nene));   // 「셰이드몬」이 아래에 있는 (BT19-102 모양)
        });
    }

    [Fact]
    public void KoreanEvo_FourTamersWithTrait_BT23_101Style()
    {
        string evoId = Uid("B48-FUDI-"), ericaId = Uid("B48-ERICA-");
        var ids = Enumerable.Range(0, 4).Select(i => Uid("B48-FT" + i + "-")).ToList();
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = evoId, Name = "테스트후디에몬", MainText = "〔진화〕특징 「테스트후디에」를 가진 자신의 테이머가 4명 이상 있는 동안, 「테스트에리카」 : 코스트 3" },
            new KoreanCard { CardId = ericaId, Name = "테스트에리카", Type = "테이머", MainText = "", Traits = new List<string> { "테스트후디에" } },
        }.Concat(ids.Select(x => new KoreanCard { CardId = x, Name = "테스트후디에테이머" + x, Type = "테이머", MainText = "", Traits = new List<string> { "테스트후디에" } })), () =>
        {
            var (state, engine) = NewMainPhase();
            var fudie = Digimon(evoId, 6, color: CardColor.Green, evoCost: 4);
            var erica = Tamer(ericaId, "TestErica", CardColor.Green);
            state.Player.Tamers.Add(erica);
            for (int i = 0; i < 2; i++) state.Player.Tamers.Add(Tamer(ids[i], "FT" + i));
            Assert.Null(engine.TamerEvolutionCostFor(state.Player, fudie, erica));   // 3명
            state.Player.Tamers.Add(Tamer(ids[2], "FT2"));
            Assert.Equal(3, engine.TamerEvolutionCostFor(state.Player, fudie, erica));
        });
    }

    // =====================================================================
    // 4. BT13-110 · 덱 아래로 · 등장 조건 다른 표기
    // =====================================================================

    private const string Bt13110Main = "[Main] ＜Draw 1＞ (Draw 1 card from your deck.) You may place 1 Digimon card from your hand under 1 of your [King Drasil_7D6] in the breeding area as its bottom digivolution card. Then, place this card in the battle area.\n[Main] ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n・ Play 1 card with the [Royal Knight] trait from the digivolution cards of your Digimon in the breeding area without paying its cost. Any [On Play] effects on Digimon played with this effect don't activate, and they gain ＜Rush＞ for the turn. (This Digimon can attack the turn it comes into play.)";

    [Fact]
    public void BT13_110_AllClausesSupported_AndKorean()
    {
        var opt = new CardData { CardId = "BT13-110", Name = "Royal Knights of the Purge", Type = CardType.Option, Color = CardColor.White, PlayCost = 2, EffectText = Bt13110Main };
        Assert.All(EffectSupport.For(opt).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        Assert.Contains("위그드라실_7D6", KoreanEffectRenderer.RenderClause(Bt13110Main.Split('\n')[0]) ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void BT13_110_Main_DrawsAndPlacesUnderBreedingYggdrasil()
    {
        var (state, engine) = NewMainPhase();
        var opt = new CardData { CardId = "BT13-110", Name = "Royal Knights of the Purge", Type = CardType.Option, Color = CardColor.White, PlayCost = 2, EffectText = Bt13110Main };
        state.Player.BreedingArea = new CardInstance(new CardData { CardId = "B48-KD", Name = "King Drasil_7D6", Type = CardType.Digitama, Level = 2, Color = CardColor.White }, 1);
        var rk = Digimon("B48-RK", 6, traits: "Royal Knight");
        state.Player.Hand.Add(rk);
        state.Player.Hand.Add(opt);
        int hand0 = state.Player.Hand.Count - 1;
        Assert.True(engine.PlayCard(opt).CanExecute, string.Join(" | ", state.ActionLogs));
        Assert.Single(state.Player.BreedingArea!.EvolutionSources);   // 패의 디지몬 카드 1장을 진화원 아래에 (AI 추천: 낮은 레벨)
        Assert.Equal(hand0, state.Player.Hand.Count);   // 1장 드로우 · 1장 놓기
        Assert.Contains(state.Player.OptionsInPlay, o => ReferenceEquals(o.Card, opt));   // 그 후, 배틀 에어리어에 놓는다 (≪딜레이≫)
    }

    [Fact]
    public void BT13_110_Delay_PlaysRoyalKnightFromBreedingSources_NoOnPlay_GainsRush()
    {
        var (state, engine) = NewMainPhase();
        var opt = new CardData { CardId = "BT13-110", Name = "Royal Knights of the Purge", Type = CardType.Option, Color = CardColor.White, PlayCost = 2, EffectText = Bt13110Main };
        var kd = new CardInstance(new CardData { CardId = "B48-KD2", Name = "King Drasil_7D6", Type = CardType.Digitama, Level = 2, Color = CardColor.White }, 1);
        var rk = Digimon("B48-RKD", 6, "[On Play] Gain 3 memory.", traits: "Royal Knight");
        kd.AddBottomSource(rk);
        state.Player.BreedingArea = kd;
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = opt, PlacedTurn = 1 });
        int mem = state.MemoryGauge;
        Assert.True(engine.ActivateMainAbility(null, opt).CanExecute, string.Join(" | ", state.ActionLogs));
        var played = Assert.Single(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, rk));
        Assert.Equal(mem, state.MemoryGauge);                       // 【등장 시】는 발휘하지 않는다
        Assert.True(CardScripts.HasKeyword(played, Keyword.Rush));  // 이 턴 동안 ≪속공≫
        Assert.Empty(kd.EvolutionSources);
    }

    [Fact]
    public void BottomOfDeck_HandCard_BT26_009()
    {
        var (state, engine) = NewMainPhase();
        var mon = Digimon("B48-HANDBOT", 4, "[When Attacking] ＜Draw 1＞ (Draw 1 card from your deck.) Then, if your hand has 6 or more cards, return 1 card in your hand to the bottom of the deck.");
        Assert.All(EffectSupport.For(mon).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        var inst = Put(state.Player, mon);
        for (int i = 0; i < 5; i++) state.Player.Hand.Add(Digimon("B48-H" + i, 5));
        int deck0 = state.Player.MainDeck.Count;
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, inst, mon);
        Assert.Equal(5, state.Player.Hand.Count);                 // 1장 드로우 → 6장 → 1장 덱 아래로
        Assert.Equal(deck0, state.Player.MainDeck.Count);
        Assert.Contains(state.Player.MainDeck[^1].CardId, new[] { "B48-H0", "B48-H1", "B48-H2", "B48-H3", "B48-H4", "B48-FILL0" });
    }

    [Fact]
    public void BottomOfDeck_OpponentTrashAndSecurity()
    {
        var (state, engine) = NewMainPhase();
        var a = Digimon("B48-TRBOT", 6, "[On Play] Delete 1 of your opponent's Digimon with 9000 DP or less. Then, you may return up to 3 non-Digi-Egg cards from their trash to the bottom of the deck.");
        Assert.All(EffectSupport.For(a).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        for (int i = 0; i < 4; i++) state.DummyOpponent.Trash.Add(Digimon("B48-OT" + i, 4));
        state.DummyOpponent.Trash.Add(Card("B48-OEGG", type: CardType.Digitama, level: 2));
        int deck0 = state.DummyOpponent.MainDeck.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, a), a);
        Assert.Equal(deck0 + 3, state.DummyOpponent.MainDeck.Count);
        Assert.Contains(state.DummyOpponent.Trash, c => c.CardId == "B48-OEGG");   // 디지타마는 되돌리지 않는다

        var b = Digimon("B48-SECBOT", 6, "[On Play] Return your opponent's top security card to the bottom of the deck.");
        var top = state.DummyOpponent.SecurityStack[0];
        int sec0 = state.DummyOpponent.SecurityStack.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, b), b);
        Assert.Equal(sec0 - 1, state.DummyOpponent.SecurityStack.Count);
        Assert.Same(top, state.DummyOpponent.MainDeck[^1]);
    }

    [Fact]
    public void PlayCondition_Rewrites_Supported_AndLevelKept()
    {
        foreach (var text in new[]
        {
            "[Security] You may play 1 level 5 [WarGrowlmon], [Taomon] or [Rapidmon] from your hand without paying the cost.",
            "[All Turns] When this Digimon would leave the battle area, you may play 1 Digimon card with a play cost 11 or lower and the [Cyborg] or [Machine] trait from this Digimon's digivolution cards without paying the cost.",
            "[On Play] [When Digivolving] By deleting this Digimon, you may play 1 play cost 12 or lower [Chronomon] text or [Titan] trait card from your trash without paying the cost.",
        })
        {
            var mon = Digimon(Uid("B48-PC-"), 5, text);
            Assert.All(EffectSupport.For(mon).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        }
        var (state, engine) = NewMainPhase();
        var opt = Digimon("B48-LV5PLAY", 4, "[On Play] You may play 1 level 5 [WarGrowlmon], [Taomon] or [Rapidmon] from your hand without paying the cost.");
        var war6 = Digimon("B48-WAR6", 6, name: "WarGrowlmon");
        var war5 = Digimon("B48-WAR5", 5, name: "WarGrowlmon");
        state.Player.Hand.Add(war6);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, opt);
        Assert.DoesNotContain(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, war6));   // Lv.6은 안 된다
        state.Player.Hand.Add(war5);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, opt);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, war5));
    }
}
