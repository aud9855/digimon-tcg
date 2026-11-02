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
/// v0.84.0 58차 묶음: 어셈블리 (종합 룰 2-3-10 · 6-5-1-1-3 · 7-3 — 조건은 공식 영어 카드 리스트 [Special Play Condition]) · EX9-062 어셈블리 Lv. 취급 ·
/// BT19-063 주인 없는 소멸 (Q&amp;A BT19-063 Q3124) · EX13-045 어택 → DP → 배틀 (공식 일본어 · Q&amp;A Q7356) · EX13-035 상한 +6 (Q&amp;A Q7315 · Q7316) ·
/// BT19-102 진화원에서 등장시키는 것으로 소멸 (Q&amp;A Q3186 · 종합 룰 3-1-3-8)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch84Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    private sealed class DeclineAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => false;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    /// <summary>카드 고르기에서 정해 둔 번호를 고른다 (없으면 추천)</summary>
    private sealed class PickCards(params int[][] picks) : IChoiceProvider
    {
        private int _i;
        public List<string> Questions { get; } = new();
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Questions.Add(question); return true; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) { Questions.Add(question); return OptionPick ?? recommended; }
        public int? OptionPick { get; init; }
        public IReadOnlyList<int> ChooseCards(GameState state, string question, CardChoiceZone zone, IReadOnlyList<CardData> options, int min, int max, IReadOnlyList<int> recommended)
        {
            Questions.Add(question);
            return _i < picks.Length ? picks[_i++] : recommended;
        }
    }

    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, int playCost = 3, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, "", traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B84-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B84-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B84-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B84-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    /// <summary>어셈블리 조건 줄을 공식 영어 카드 리스트 대신 넣고 실행</summary>
    private static void WithAssembly(Dictionary<string, string> lines, Action run)
    {
        EnglishCardRules.Use(lines.Select(kv => new EnglishCardRule { CardId = kv.Key, Assembly = kv.Value }));
        try { run(); }
        finally { EnglishCardRules.Reload(); }
    }

    // =====================================================================
    // 1. 어셈블리 조건 읽기 (공식 영어 카드 리스트의 모든 모양)
    // =====================================================================

    [Theory]
    [InlineData("Assembly -3: 4 [Eyesmon: Scatter Mode]", 3, 1, 4)]
    [InlineData("Assembly -6: 4 [Negamon]", 6, 1, 4)]
    [InlineData("Assembly -6: 4 level 5 [Cyborg] trait Digimon cards w/different names", 6, 1, 4)]
    [InlineData("Assembly -7: 7 level 4 [DM] trait Digimon cards w/different names", 7, 1, 7)]
    [InlineData("Assembly -2: [Blimpmon]/Tamer card w/[TS] trait", 2, 1, 1)]
    [InlineData("Assembly -6: [Titamon]×[SkullBaluchimon]", 6, 2, 2)]
    [InlineData("Assembly -5: 5 green Digimon cards w/[Maquinamon] in text", 5, 1, 5)]
    [InlineData("Assembly -6: 8 cards w/[Vemmon] in text", 6, 1, 8)]
    [InlineData("Assembly -2: Lv.4 or lower w/[Agumon]/[Greymon] in name or w/[ME]/[VB] trait", 2, 1, 1)]
    [InlineData("Assembly -6: Lv.5 × Lv.4 × Lv.3, all w/[Agumon]/[Greymon] in name or w/[ME]/[VB] trait", 6, 3, 3)]
    [InlineData("Assembly -2: Lv.4 or lower card w/[Aqua]/[Sea Animal] in any trait or w/[TB] trait", 2, 1, 1)]
    [InlineData("Assembly -6: 3 [Gokuumon]/[Sagomon]/[Cho-Hakkaimon]/[Sanzomon] w/different names", 6, 1, 3)]
    [InlineData("Assembly -8: 6 Lv.6 or lower [Machine]/[Cyborg]/[ME] trait cards w/different names", 8, 1, 6)]
    [InlineData("Assembly -4: 2 Lv.5 or lower [Shambala] trait cards w/different levels", 4, 1, 2)]
    [InlineData("Assembly -2: Lv.3 [Life]/[System]/[Seven Code] trait Digimon card", 2, 1, 1)]
    [InlineData("Assembly -2: Lv.4 or lower Digimon card w/[Chronomon] in text or w/[TS] trait", 2, 1, 1)]
    [InlineData("Assembly -5: 5 different-level cards w/[Chronomon] in text or w/[Shaman] trait", 5, 1, 5)]
    [InlineData("Assembly -7: [WarGreymon]×[MetalGarurumon]×[Agumon]×[Gabumon]", 7, 4, 4)]
    [InlineData("Assembly -4: 3 Lv.4 or lower Digimon cards w/[Sukamon] in name", 4, 1, 3)]
    [InlineData("Assembly -5: Lv.5 × Lv.4 × Lv.3, all yellow w/[Holy Beast] trait", 5, 3, 3)]
    [InlineData("Assembly -5: Lv.5 × Lv.4 × Lv.3, all black w/＜Blocker＞", 5, 3, 3)]
    [InlineData("Assembly -5: 3 [Huckmon] text Digimon cards w/different names", 5, 1, 3)]
    [InlineData("Assembly -4: 3 Lv.5 or lower [Mamemon] text cards w/different names", 4, 1, 3)]
    [InlineData("Assembly -8: 6 [ADVENTURE] trait Digimon cards w/different colors", 8, 1, 6)]
    [InlineData("Assembly -6: 3 [Composite]/[Ver.3]/[Ver.5] trait Digimon cards w/different levels", 6, 1, 3)]
    [InlineData("Assembly -6: Lv.5 × Lv.4 × Lv.3, all w/[Gammamon] in text or w/[VB] trait", 6, 3, 3)]
    public void Parse_AllOfficialShapes(string line, int reduction, int slots, int total)
    {
        var req = GoldfishEngine.ParseAssembly84(line, Digimon(Uid("B84-P-"), 6));
        Assert.NotNull(req);
        Assert.Equal(reduction, req!.Reduction);
        Assert.Equal(slots, req.Slots.Count);
        Assert.Equal(total, req.Total);
        Assert.False(string.IsNullOrWhiteSpace(req.Ko));
    }

    [Theory]
    [InlineData("Assembly -6: 4 [Negamon] w/something unknown")]
    [InlineData("Assembly: 4 [Negamon]")]
    [InlineData("Assembly -2: Lv.4 or lower Digimon card w/[A] in its flavor text")]
    public void Parse_UnknownShape_NoAssembly(string line) =>
        Assert.Null(GoldfishEngine.ParseAssembly84(line, Digimon(Uid("B84-PU-"), 6)));

    [Fact]
    public void NoOfficialLine_NoAssembly_PlaysNormally()
    {
        WithAssembly(new(), () =>
        {
            var abba = Digimon(Uid("B84-ABBA-"), 6, playCost: 7);
            Assert.False(GoldfishEngine.HasAssembly84(abba));
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon("B84-NEGA", 3, name: "Negamon"));
            state.Player.Hand.Add(abba);
            Assert.True(engine.PlayCard(abba).CanExecute);
            Assert.Equal(1, state.MemoryGauge);
            Assert.Equal(4, state.Player.Trash.Count);
        });
    }

    // =====================================================================
    // 2. 패에서 등장 — 코스트 지불 직전에 트래시의 카드를 아래에 (7-3-3)
    // =====================================================================

    [Fact]
    public void PlayFromHand_FourNegamon_CostMinus6_PlacedFromTrash()
    {
        string id = Uid("B84-ABBA-");
        WithAssembly(new() { [id] = "Assembly -6: 4 [Negamon]" }, () =>
        {
            var abba = Digimon(id, 6, playCost: 11, name: "Abbadomon");
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            var negas = Enumerable.Range(0, 4).Select(_ => Digimon(Uid("B84-NEGA-"), 3, name: "Negamon")).ToList();
            state.Player.Trash.AddRange(negas);
            var other = Digimon(Uid("B84-OTH-"), 3);
            state.Player.Trash.Add(other);
            state.Player.Hand.Add(abba);
            Assert.True(engine.PlayCard(abba).CanExecute);
            var d = state.Player.BattleArea.Single(x => x.TopCard == abba);
            Assert.Equal(8 - 5, state.MemoryGauge);   // 11 - 6
            Assert.Equal(4, d.EvolutionSources.Count);
            Assert.All(negas, n => Assert.Contains(n, d.EvolutionSources));
            Assert.Equal(new[] { other }, state.Player.Trash);
            Assert.Contains("[어셈블리]", Logs(state));
        });
    }

    [Fact]
    public void PlayFromHand_NotEnoughCards_NoAssembly_AllOrNothing()
    {
        string id = Uid("B84-ABBA-");
        WithAssembly(new() { [id] = "Assembly -6: 4 [Negamon]" }, () =>
        {
            var abba = Digimon(id, 6, playCost: 7);
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            for (int i = 0; i < 3; i++) state.Player.Trash.Add(Digimon(Uid("B84-NEGA-"), 3, name: "Negamon"));   // 7-3-2-4 일부만은 안 된다
            state.Player.Hand.Add(abba);
            engine.PlayCard(abba);
            Assert.Equal(1, state.MemoryGauge);
            Assert.Equal(3, state.Player.Trash.Count);
            Assert.Empty(state.Player.BattleArea.Single().EvolutionSources);
        });
    }

    [Fact]
    public void PlayFromHand_PlayerDeclines_NoAssembly()
    {
        string id = Uid("B84-ABBA-");
        WithAssembly(new() { [id] = "Assembly -6: 4 [Negamon]" }, () =>
        {
            var abba = Digimon(id, 6, playCost: 7);
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new DeclineAll();   // 7-3-2-9 강제가 아니다
            for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon(Uid("B84-NEGA-"), 3, name: "Negamon"));
            state.Player.Hand.Add(abba);
            engine.PlayCard(abba);
            Assert.Equal(1, state.MemoryGauge);
            Assert.Equal(4, state.Player.Trash.Count);
        });
    }

    [Fact]
    public void PlayFromHand_NoChooser_AutoAssembles()
    {
        string id = Uid("B84-ABBA-");
        WithAssembly(new() { [id] = "Assembly -6: 4 [Negamon]" }, () =>
        {
            var abba = Digimon(id, 6, playCost: 7);
            var (state, engine) = NewMainPhase(memory: 8);
            for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon(Uid("B84-NEGA-"), 3, name: "Negamon"));
            state.Player.Hand.Add(abba);
            engine.PlayCard(abba);
            Assert.Equal(7, state.MemoryGauge);
            Assert.Empty(state.Player.Trash);
        });
    }

    [Fact]
    public void PlayersCantReducePlayCosts_NoAssembly()
    {
        string id = Uid("B84-ABBA-");
        WithAssembly(new() { [id] = "Assembly -6: 4 [Negamon]" }, () =>
        {
            var abba = Digimon(id, 6, playCost: 7);
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            Put(state.DummyOpponent, Digimon(Uid("B84-LOCK-"), 5, "[All Turns] Players can't reduce play costs."));
            engine.RecalculateFieldBuffs();
            for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon(Uid("B84-NEGA-"), 3, name: "Negamon"));
            state.Player.Hand.Add(abba);
            engine.PlayCard(abba);
            Assert.Equal(1, state.MemoryGauge);
            Assert.Equal(4, state.Player.Trash.Count);
        });
    }

    [Fact]
    public void DifferentNames_SameNameCardsDoNotCount()
    {
        string id = Uid("B84-MACH-");
        WithAssembly(new() { [id] = "Assembly -6: 4 level 5 [Cyborg] trait Digimon cards w/different names" }, () =>
        {
            var mach = Digimon(id, 6, playCost: 12);
            var (state, engine) = NewMainPhase(memory: 8);
            state.Player.Trash.Add(Digimon(Uid("B84-C1-"), 5, name: "Andromon", traits: "Cyborg"));
            state.Player.Trash.Add(Digimon(Uid("B84-C2-"), 5, name: "Andromon", traits: "Cyborg"));
            state.Player.Trash.Add(Digimon(Uid("B84-C3-"), 5, name: "MetalTyrannomon", traits: "Cyborg"));
            state.Player.Trash.Add(Digimon(Uid("B84-C4-"), 5, name: "Megadramon", traits: "Cyborg"));
            state.Player.Trash.Add(Digimon(Uid("B84-C5-"), 4, name: "Guardromon", traits: "Cyborg"));   // Lv.4
            Assert.Equal(0, engine.PreviewAssemblyReduction84(state.Player, mach));
            state.Player.Trash.Add(Digimon(Uid("B84-C6-"), 5, name: "Gigadramon", traits: "Cyborg"));
            Assert.Equal(6, engine.PreviewAssemblyReduction84(state.Player, mach));
            state.Player.Hand.Add(mach);
            engine.PlayCard(mach);
            var d = state.Player.BattleArea.Single();
            Assert.Equal(4, d.EvolutionSources.Select(x => x.Name).Distinct().Count());
            Assert.Equal(2, state.Player.Trash.Count);
        });
    }

    [Fact]
    public void ThreeLevelSlots_EachMustMatch_LeftIsTop()
    {
        string id = Uid("B84-WG-");
        WithAssembly(new() { [id] = "Assembly -6: Lv.5 × Lv.4 × Lv.3, all w/[Agumon]/[Greymon] in name or w/[ME]/[VB] trait" }, () =>
        {
            var wg = Digimon(id, 6, playCost: 12);
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            var lv3 = Digimon(Uid("B84-A3-"), 3, name: "Agumon");
            var lv4 = Digimon(Uid("B84-A4-"), 4, name: "Wizardmon", traits: "VB");
            var lv5wrong = Digimon(Uid("B84-A5X-"), 5, name: "Andromon");
            state.Player.Trash.AddRange(new[] { lv3, lv4, lv5wrong });
            Assert.Equal(0, engine.PreviewAssemblyReduction84(state.Player, wg));   // Q&A EX12-017 Q6743: 세 장 모두 조건
            var lv5 = Digimon(Uid("B84-A5-"), 5, name: "MetalGreymon");
            state.Player.Trash.Add(lv5);
            state.Player.Hand.Add(wg);
            engine.PlayCard(wg);
            var d = state.Player.BattleArea.Single();
            Assert.Equal(new[] { lv5, lv4, lv3 }, d.EvolutionSources.ToArray());   // 위 → 아래: 왼쪽(Lv.5)이 위 (7-3-2-6)
            Assert.Equal(new[] { lv5wrong }, state.Player.Trash);
            Assert.Equal(2, state.MemoryGauge);
        });
    }

    [Fact]
    public void NamedSlots_PlayerPicksPerSlot()
    {
        string id = Uid("B84-OMNI-");
        WithAssembly(new() { [id] = "Assembly -6: [WarGreymon]×[MetalGarurumon]" }, () =>
        {
            var omni = Digimon(id, 7, playCost: 10);
            var (state, engine) = NewMainPhase(memory: 8);
            var wg1 = Digimon(Uid("B84-WG1-"), 6, name: "WarGreymon");
            var wg2 = Digimon(Uid("B84-WG2-"), 6, name: "WarGreymon");
            var mg = Digimon(Uid("B84-MG-"), 6, name: "MetalGarurumon");
            state.Player.Trash.AddRange(new[] { wg1, wg2, mg });
            var chooser = new PickCards(new[] { 1 }, new[] { 0 });   // 1칸: 두 번째 후보 · 2칸: 첫 후보
            engine.PlayerChoices = chooser;
            state.Player.Hand.Add(omni);
            engine.PlayCard(omni);
            var d = state.Player.BattleArea.Single();
            Assert.Equal(2, d.EvolutionSources.Count);
            Assert.Contains(mg, d.EvolutionSources);
            Assert.Single(state.Player.Trash);
            Assert.Contains(chooser.Questions, q => q.Contains("어셈블리 -6"));
            Assert.Equal(8 - 4, state.MemoryGauge);
        });
    }

    [Fact]
    public void InvalidPlayerPick_FallsBackToRecommended()
    {
        string id = Uid("B84-GOKU-");
        WithAssembly(new() { [id] = "Assembly -6: 3 [Gokuumon]/[Sagomon]/[Cho-Hakkaimon]/[Sanzomon] w/different names" }, () =>
        {
            var goku = Digimon(id, 6, playCost: 13);
            var (state, engine) = NewMainPhase(memory: 8);
            var g1 = Digimon(Uid("B84-G1-"), 5, name: "Gokuumon");
            var g2 = Digimon(Uid("B84-G2-"), 5, name: "Gokuumon");
            var s1 = Digimon(Uid("B84-S1-"), 5, name: "Sagomon");
            var z1 = Digimon(Uid("B84-Z1-"), 5, name: "Sanzomon");
            state.Player.Trash.AddRange(new[] { g1, g2, s1, z1 });
            engine.PlayerChoices = new PickCards(new[] { 0, 1, 2 });   // 추천 3장 다음에 남은 Gokuumon — 명칭이 겹치면 추천대로
            state.Player.Hand.Add(goku);
            engine.PlayCard(goku);
            var d = state.Player.BattleArea.Single();
            Assert.Equal(3, d.EvolutionSources.Select(x => x.Name).Distinct().Count());
        });
    }

    [Fact]
    public void DifferentColors_MultiColorCardsTakeOneColorEach()
    {
        var req = GoldfishEngine.ParseAssembly84("Assembly -8: 3 [ADVENTURE] trait Digimon cards w/different colors", Digimon(Uid("B84-MM-"), 7))!;
        var red = Digimon(Uid("B84-R-"), 5, color: CardColor.Red, traits: "ADVENTURE");
        var red2 = Digimon(Uid("B84-R2-"), 5, color: CardColor.Red, traits: "ADVENTURE");
        var redBlue = Card(Uid("B84-RB-"), "", CardType.Digimon, 5, 3, CardColor.Red, -1, null, "", "ADVENTURE");
        redBlue.SecondaryColor = CardColor.Blue;
        Assert.Null(GoldfishEngine.AutoPickAsm84(req, new[] { red, red2, redBlue }));   // 빨강 둘 + 빨강/파랑 = 색 2개
        var yellow = Digimon(Uid("B84-Y-"), 5, color: CardColor.Yellow, traits: "ADVENTURE");
        var pick = GoldfishEngine.AutoPickAsm84(req, new[] { red, red2, redBlue, yellow });
        Assert.NotNull(pick);
        Assert.True(GoldfishEngine.ValidAsm84(req, pick!));
    }

    [Fact]
    public void DifferentLevels_NoLevelCardDoesNotCount()
    {
        // Q&A BT26-085 Q7210: Lv.이 없는 카드는 어셈블리에서 참조할 수 없다
        var req = GoldfishEngine.ParseAssembly84("Assembly -5: 3 different-level cards w/[Chronomon] in text or w/[Shaman] trait", Digimon(Uid("B84-GS-"), 0))!;
        var a = Digimon(Uid("B84-L3-"), 3, traits: "Shaman");
        var b = Digimon(Uid("B84-L4-"), 4, traits: "Shaman");
        var noLevel = Digimon(Uid("B84-L0-"), 0, traits: "Shaman");
        Assert.Null(GoldfishEngine.AutoPickAsm84(req, new[] { a, b, noLevel }));
        Assert.NotNull(GoldfishEngine.AutoPickAsm84(req, new[] { a, b, noLevel, Digimon(Uid("B84-L5-"), 5, traits: "Shaman") }));
    }

    [Fact]
    public void TamerAlternative_NameOrTamerTrait()
    {
        var req = GoldfishEngine.ParseAssembly84("Assembly -2: [Blimpmon]/Tamer card w/[TS] trait", Digimon(Uid("B84-MB-"), 5))!;
        var tamer = Card(Uid("B84-T-"), "", CardType.Tamer, 0, 3, CardColor.Blue, -1, null, "", "TS");
        var tamerX = Card(Uid("B84-TX-"), "", CardType.Tamer, 0, 3, CardColor.Blue, -1, null, "", "SoC");
        var blimp = Digimon(Uid("B84-BL-"), 3, name: "Blimpmon");
        Assert.True(req.Slots[0].Match(tamer));
        Assert.True(req.Slots[0].Match(blimp));
        Assert.False(req.Slots[0].Match(tamerX));
    }

    // =====================================================================
    // 3. EX9-062 "also treated as level 4 for [Kimeramon]'s assembly"
    // =====================================================================

    [Fact]
    public void Ex9062_TreatedAsLevel4_OnlyForKimeramon()
    {
        string kid = Uid("B84-KIME-"), other = Uid("B84-OTHER-");
        WithAssembly(new() { [kid] = "Assembly -7: 3 level 4 [DM] trait Digimon cards w/different names", [other] = "Assembly -7: 3 level 4 [DM] trait Digimon cards w/different names" }, () =>
        {
            var skull = Digimon(Uid("B84-SKULL-"), 5, "This card is also treated as level 4 for [Kimeramon]'s assembly.", name: "SkullGreymon", traits: "DM");
            AllSupported(skull);
            var kime = Digimon(kid, 5, name: "Kimeramon", playCost: 10);
            var notKime = Digimon(other, 5, name: "Machinedramon", playCost: 10);
            var (state, engine) = NewMainPhase();
            state.Player.Trash.Add(Digimon(Uid("B84-D1-"), 4, name: "Kuwagamon", traits: "DM"));
            state.Player.Trash.Add(Digimon(Uid("B84-D2-"), 4, name: "Monochromon", traits: "DM"));
            state.Player.Trash.Add(skull);
            Assert.Equal(7, engine.PreviewAssemblyReduction84(state.Player, kime));
            Assert.Equal(0, engine.PreviewAssemblyReduction84(state.Player, notKime));
            Assert.Equal("이 카드는 「키메라몬」의 어셈블리에서 Lv.4로도 취급한다.", Ko("This card is also treated as level 4 for [Kimeramon]'s assembly."));
        });
    }

    // =====================================================================
    // 4. 효과로 등장시킬 때도 (7-3-2-10 · Q&A P-205 Q5397 · BT26-079 Q7110)
    // =====================================================================

    [Fact]
    public void EffectPlay_PayingCost_ReducesFurther()
    {
        string id = Uid("B84-ZOMB-");
        WithAssembly(new() { [id] = "Assembly -2: [Plutomon]" }, () =>
        {
            var zp = Digimon(id, 6, playCost: 12);
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            var pluto = Digimon(Uid("B84-PLU-"), 6, name: "Plutomon");
            state.Player.Trash.Add(pluto);
            state.Player.Hand.Add(zp);
            Assert.True(engine.TryPlayByEffect(state.Player, zp, PlayOrigin.Hand, () => state.Player.Hand.Remove(zp), payCost: 5));
            var d = state.Player.BattleArea.Single();
            Assert.Contains(pluto, d.EvolutionSources);
            Assert.Equal(8 - 3, state.MemoryGauge);
        });
    }

    [Fact]
    public void EffectPlay_Free_PlayerMayStillAssemble_AiDoesNot()
    {
        string id = Uid("B84-ZOMB-");
        WithAssembly(new() { [id] = "Assembly -2: [Plutomon]" }, () =>
        {
            var zp = Digimon(id, 6, playCost: 12);
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            var pluto = Digimon(Uid("B84-PLU-"), 6, name: "Plutomon");
            state.Player.Trash.Add(pluto);
            Assert.True(engine.TryPlayByEffect(state.Player, zp, PlayOrigin.Revealed, null));
            Assert.Contains(pluto, state.Player.BattleArea.Single().EvolutionSources);
            Assert.Equal(8, state.MemoryGauge);

            var zp2 = Digimon(id, 6, playCost: 12);
            var pluto2 = Digimon(Uid("B84-PLU-"), 6, name: "Plutomon");
            state.DummyOpponent.Trash.Add(pluto2);
            Assert.True(engine.TryPlayByEffect(state.DummyOpponent, zp2, PlayOrigin.Revealed, null));
            Assert.Contains(pluto2, state.DummyOpponent.Trash);   // AI: 코스트 감소가 없으면 트래시를 남긴다
        });
    }

    [Fact]
    public void DisplayText_ShowsAssemblyLine()
    {
        string id = Uid("B84-ABBA-");
        WithAssembly(new() { [id] = "Assembly -6: 4 [Negamon]" }, () =>
        {
            var abba = Digimon(id, 6, "[On Play] Gain 1 memory.", playCost: 11);
            string text = KoreanCardData.DisplayEffectText(abba);
            Assert.Contains("어셈블리 -6:", text);
            Assert.Contains("4장", text);
            Assert.Contains("트래시에서 지정된 장수를 이 카드 아래에 놓는 것으로", text);
        });
    }

    // =====================================================================
    // 5. BT19-063 주인을 말하지 않은 소멸 = 양쪽 (Q&A BT19-063 Q3124)
    // =====================================================================

    private const string AnySideDelete = "[On Play] You may delete 1 play cost 3 or lower Digimon or Tamer.";

    [Fact]
    public void Bt19063_AnySide_RecommendsOpponent()
    {
        var src = Digimon(Uid("B84-DK-"), 6, AnySideDelete, playCost: 5);
        AllSupported(src);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var mine = Put(state.Player, Digimon(Uid("B84-M3-"), 3, playCost: 3));
        var theirs = Put(state.DummyOpponent, Digimon(Uid("B84-T3-"), 3, playCost: 3));
        Put(state.DummyOpponent, Digimon(Uid("B84-T5-"), 5, playCost: 5));
        state.Player.Hand.Add(src);
        engine.PlayCard(src);
        Assert.DoesNotContain(theirs, state.DummyOpponent.BattleArea);
        Assert.Contains(mine, state.Player.BattleArea);
    }

    [Fact]
    public void Bt19063_PlayerCanDeleteOwnTamer()
    {
        var src = Digimon(Uid("B84-DK-"), 6, AnySideDelete, playCost: 5);
        var (state, engine) = NewMainPhase();
        var tamer = Card(Uid("B84-TM-"), "", CardType.Tamer, 0, 2);
        state.Player.Tamers.Add(tamer);
        var theirs = Put(state.DummyOpponent, Digimon(Uid("B84-T3-"), 3, playCost: 3));
        engine.PlayerChoices = new PickCards() { OptionPick = 1 };   // 0: 상대의 디지몬 · 1: 자신의 테이머
        state.Player.Hand.Add(src);
        engine.PlayCard(src);
        Assert.DoesNotContain(tamer, state.Player.Tamers);
        Assert.Contains(theirs, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Bt19063_Ai_OnlyOwnCandidate_DoesNotDelete()
    {
        var src = Digimon(Uid("B84-DK-"), 6, AnySideDelete, playCost: 5);
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Digimon(Uid("B84-M3-"), 3, playCost: 3));
        state.Player.Hand.Add(src);
        engine.PlayCard(src);   // 선택 창 없음 = 추천
        Assert.Contains(mine, state.Player.BattleArea);
    }

    [Fact]
    public void Bt19063_RealClause_SupportedWithKorean()
    {
        var c = Digimon("BT19-063", 6, "[On Play] [When Digivolving] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.) Then, if DigiXrosing with 2 cards, you may delete 1 play cost 3 or lower Digimon or Tamer.");
        AllSupported(c);
        Assert.Contains("자신 또는 상대의 등장 코스트 3 이하의 디지몬/테이머 1장을 소멸시킬 수 있다", Ko(c.EffectText));
    }

    // =====================================================================
    // 6. EX13-045 어택 → DP → 배틀 (공식 일본어 · Q&A EX13-045 Q7356 · Q7357)
    // =====================================================================

    private static CardData Breakdramon(string id = "EX13-045") =>
        Digimon(id, 7, SentenceParser.Ex13045Api84, name: "Examon", dp: 15000, playCost: 14);

    [Fact]
    public void Ex13045_OnlyThisCardAndExactText_Supported()
    {
        AllSupported(Breakdramon());
        Assert.NotEqual(ClauseSupport.Supported, EffectSupport.For(Breakdramon(Uid("B84-NOTEX-"))).Clauses.Single().Support);
        Assert.StartsWith("【진화 시】 조그레스 진화했다면, 이 디지몬으로 어택하고", Ko(SentenceParser.Ex13045Api84));
    }

    [Fact]
    public void Ex13045_NotDna_NoAttackNoBuff_ButBattles()
    {
        var ex = Breakdramon();
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var baseD = Put(state.Player, Digimon(Uid("B84-B6-"), 6, dp: 12000));
        var ally = Put(state.Player, Digimon(Uid("B84-AL-"), 4, dp: 4000));
        var enemy = Put(state.DummyOpponent, Digimon(Uid("B84-EN-"), 6, dp: 11000));
        ex.EvoRequirementLevel = 6;
        state.Player.Hand.Add(ex);
        Assert.True(engine.EvolveDigimon(baseD, ex).CanExecute);
        Assert.DoesNotContain(enemy, state.DummyOpponent.BattleArea);   // 그 후 배틀 (Q7356)
        Assert.False(baseD.IsSuspended);                                  // 어택하지 않았다
        Assert.Equal(4000, ally.GetCurrentDP());                          // DP +10000 없음
    }

    [Fact]
    public void Ex13045_Dna_AttacksBuffsAndBattles()
    {
        string id = "EX13-045";
        EnglishCardRules.Use(new[] { new EnglishCardRule { CardId = id, Dna = { "[DNA Digivolve] Green Lv.6 + blue Lv.6: Cost 0" } } });
        try
        {
            var ex = Breakdramon();
            Assert.True(GoldfishEngine.HasDna(ex));
            var (state, engine) = NewMainPhase();
            engine.PlayerChoices = new AcceptAll();
            var g = Put(state.Player, Digimon(Uid("B84-G6-"), 6, color: CardColor.Green));
            var b = Put(state.Player, Digimon(Uid("B84-B6-"), 6, color: CardColor.Blue));
            var ally = Put(state.Player, Digimon(Uid("B84-AL-"), 4, dp: 4000));
            var enemy = Put(state.DummyOpponent, Digimon(Uid("B84-EN-"), 6, dp: 11000));
            int secBefore = state.DummyOpponent.SecurityStack.Count;
            state.Player.Hand.Add(ex);
            Assert.True(engine.DnaDigivolve(ex, g, b).CanExecute);
            Assert.Equal(14000, ally.GetCurrentDP());                         // DP +10000 (상대의 턴 종료 시까지)
            Assert.DoesNotContain(enemy, state.DummyOpponent.BattleArea);     // 배틀로 소멸
            var exd = state.Player.BattleArea.Single(x => x.TopCard == ex);
            Assert.True(exd.IsSuspended);                                     // 어택했다
            Assert.True(state.DummyOpponent.SecurityStack.Count < secBefore, Logs(state));   // 어택은 계속 — 플레이어에게 (≪S 어택≫은 API 키워드 칸에 따라)
        }
        finally { EnglishCardRules.Reload(); }
    }

    // =====================================================================
    // 7. EX13-035 (Q&A EX13-035 Q7315 · Q7316)
    // =====================================================================

    private static CardData KingEtemon() => Digimon("EX13-035", 6, SentenceParser.Ex13035Api84, name: "KingEtemon", playCost: 2);

    [Fact]
    public void Ex13035_WithoutReturn_TotalUpTo6_TwoCards()
    {
        var k = KingEtemon();
        AllSupported(k);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        state.Player.Hand.Add(Digimon(Uid("B84-SK5-"), 5, name: "PlatinumSukamon", playCost: 5));
        state.Player.Trash.Add(Digimon(Uid("B84-CH1-"), 3, name: "Chuumon", playCost: 1));
        state.Player.Trash.Add(Digimon(Uid("B84-ET4-"), 4, name: "Etemon", playCost: 4));
        state.Player.Hand.Add(k);
        engine.PlayCard(k);
        var played = state.Player.BattleArea.Where(d => d.TopCard != k).Select(d => d.TopCard.PlayCost).ToList();
        Assert.Equal(2, played.Count);
        Assert.True(played.Sum() <= 6);
    }

    [Fact]
    public void Ex13035_ReturnTen_RaisesMaxTo12()
    {
        var k = KingEtemon();
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        for (int i = 0; i < 10; i++) state.Player.Trash.Add(Digimon(Uid("B84-SKT-"), 3, name: "Sukamon", playCost: 1));
        var big1 = Digimon(Uid("B84-ET6-"), 6, name: "KingEtemon", playCost: 7);
        var big2 = Digimon(Uid("B84-SK5-"), 5, name: "PlatinumSukamon", playCost: 5);
        state.Player.Hand.Add(big1);
        state.Player.Hand.Add(big2);
        int deckBefore = state.Player.MainDeck.Count;
        state.Player.Hand.Add(k);
        engine.PlayCard(k);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == big1);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == big2);
        Assert.Equal(deckBefore + 10, state.Player.MainDeck.Count);
        Assert.Empty(state.Player.Trash.Where(x => x.Name == "Sukamon"));
    }

    [Fact]
    public void Ex13035_NineCards_CannotReturn()
    {
        var k = KingEtemon();
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        for (int i = 0; i < 9; i++) state.Player.Trash.Add(Digimon(Uid("B84-SKT-"), 3, name: "Sukamon", playCost: 1));
        var big1 = Digimon(Uid("B84-ET6-"), 6, name: "KingEtemon", playCost: 7);
        state.Player.Hand.Add(big1);
        int deckBefore = state.Player.MainDeck.Count;
        state.Player.Hand.Add(k);
        engine.PlayCard(k);
        Assert.DoesNotContain(state.Player.BattleArea, d => d.TopCard == big1);
        Assert.Equal(deckBefore, state.Player.MainDeck.Count);
    }

    // =====================================================================
    // 8. BT19-102 (Q&A BT19-102 Q3186 · 종합 룰 3-1-3-8)
    // =====================================================================

    private static CardData Luminamon() => Digimon("BT19-102", 5, SentenceParser.Bt19102Api84, name: "Luminamon", playCost: 6);

    [Fact]
    public void Bt19102_OpponentDigimon_SourcePlayedToOwnersArea_ChosenDeleted()
    {
        var lu = Luminamon();
        AllSupported(lu);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var small = Digimon(Uid("B84-SM-"), 4, dp: 4000);
        var big = Put(state.DummyOpponent, Digimon(Uid("B84-BIG-"), 6, dp: 12000), small);
        state.Player.Hand.Add(lu);
        engine.PlayCard(lu);
        Assert.DoesNotContain(big, state.DummyOpponent.BattleArea);
        Assert.Contains(state.DummyOpponent.BattleArea, d => d.TopCard == small);   // 상대의 카드는 상대의 배틀 에어리어에
        Assert.DoesNotContain(state.Player.BattleArea, d => d.TopCard == small);
    }

    [Fact]
    public void Bt19102_OwnDigimon_PlayerMayChoose()
    {
        var lu = Luminamon();
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var small = Digimon(Uid("B84-SM-"), 3);
        var mine = Put(state.Player, Digimon(Uid("B84-MY-"), 5), small);
        state.Player.Hand.Add(lu);
        engine.PlayCard(lu);
        Assert.DoesNotContain(mine, state.Player.BattleArea);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == small);
    }

    [Fact]
    public void Bt19102_NoLevel4OrLowerSource_NothingHappens()
    {
        var lu = Luminamon();
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var big = Put(state.DummyOpponent, Digimon(Uid("B84-BIG-"), 6), Digimon(Uid("B84-L5-"), 5));
        state.Player.Hand.Add(lu);
        engine.PlayCard(lu);
        Assert.Contains(big, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Bt19102_Ai_DoesNotSacrificeOwn()
    {
        var lu = Luminamon();
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Digimon(Uid("B84-MY-"), 5), Digimon(Uid("B84-SM-"), 3));
        state.Player.Hand.Add(lu);
        engine.PlayCard(lu);   // 선택 창 없음 = 추천 (상대 쪽에 고를 것이 없으면 하지 않는다)
        Assert.Contains(mine, state.Player.BattleArea);
    }
}
