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
/// v0.46.0 미지원 문장 20차 묶음: 사건 시점에 있던 카드만 유발 (종합 룰 15-5-1) · 떠날 때 · 소멸할 때 대신 진화 · 이름 주어 지속 효과 · 덱에 추가했을 때 · 진화원 아래에 놓는 비용 ·
/// 원래 규칙이 읽던 절은 그대로 (20차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch46Tests
{
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public int OptionAnswer = -1;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => ConfirmAnswer;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) =>
            recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => OptionAnswer >= 0 ? OptionAnswer : recommended;
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

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, int playCost = 2, string inherited = "", params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, playCost, color, 0, name, inherited, 1, traits);

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B46-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B46-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B46-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B46-OSEC" + i, 3, dp: 1000, color: CardColor.White));
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


    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    private static int CountAll(PlayerZones z) =>
        z.MainDeck.Count + z.Hand.Count + z.SecurityStack.Count + z.Trash.Count + z.Tamers.Count + z.OptionsInPlay.Count
        + z.BattleArea.Sum(d => 1 + d.EvolutionSources.Count + d.LinkedCards.Count) + (z.BreedingArea is { } b ? 1 + b.EvolutionSources.Count : 0);

    // =====================================================================
    // 1. 사건 시점에 있던 카드만 유발 (종합 룰 15-5-1 · 공식 Q&A BT14-073 Q1 · BT15-052 Q1)
    // =====================================================================

    private const string P145Text = "[On Deletion] If [Myotismon] or [X Antibody] was in this Digimon's digivolution cards, you may play 1 level 6 Digimon card with [Myotismon] in its name from your trash without paying the cost.";
    private const string BT16081Text = "[All Turns] [Once Per Turn] When an effect deletes one of your other Digimon or Tamers, trash the top card of your opponent's security stack.";

    [Fact]
    public void Witness_DigimonPlayedByOnDeletion_DoesNotSeeThatDeletion_P145_BT16_081()
    {
        var (state, engine, _) = NewMainPhase();
        var p145 = Digimon("B46-P145", 5, P145Text);
        var malo = Digimon("B46-MALO", 6, BT16081Text, name: "MaloMyotismon");
        AssertFull(p145); AssertFull(malo);
        state.Player.Trash.Add(malo);
        var me = Put(state.Player, p145, sources: Digimon("B46-MYO", 4, name: "Myotismon"));
        int sec = state.DummyOpponent.SecurityStack.Count;
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == malo);
        Assert.Equal(sec, state.DummyOpponent.SecurityStack.Count);   // 등장하기 전의 소멸에는 반응하지 않는다
    }

    [Fact]
    public void Witness_DigimonAlreadyInPlay_StillSeesDeletion_BT16_081()
    {
        var (state, engine, _) = NewMainPhase();
        var malo = Digimon("B46-MALO", 6, BT16081Text, name: "MaloMyotismon");
        Put(state.Player, malo);
        var victim = Put(state.Player, Digimon("B46-V", 3));
        int sec = state.DummyOpponent.SecurityStack.Count;
        engine.DeleteByOpponentEffect(state.Player, victim);
        Assert.Equal(sec - 1, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Witness_Switch_Off_RestoresOldOrder()
    {
        try
        {
            GoldfishEngine.EventWitnessEnabled46 = false;
            var (state, engine, _) = NewMainPhase();
            var p145 = Digimon("B46-P145", 5, P145Text);
            var malo = Digimon("B46-MALO", 6, BT16081Text, name: "MaloMyotismon");
            state.Player.Trash.Add(malo);
            var me = Put(state.Player, p145, sources: Digimon("B46-MYO", 4, name: "Myotismon"));
            int sec = state.DummyOpponent.SecurityStack.Count;
            engine.DeleteByOpponentEffect(state.Player, me);
            Assert.Equal(sec - 1, state.DummyOpponent.SecurityStack.Count);   // v0.45.0까지 (잘못된) 처리
        }
        finally { GoldfishEngine.EventWitnessEnabled46 = true; }
    }

    [Fact]
    public void Witness_DigimonPlayedByOnPlay_DoesNotSeeThatPlay()
    {
        var (state, engine, _) = NewMainPhase();
        var watcher = Digimon("B46-WATCH", 4, "[All Turns] When one of your other Digimon is played, gain 1 memory.");
        AssertFull(watcher);
        var player = Digimon("B46-PLAYER", 5, "[On Play] You may play 1 [B46-WATCH] from your hand without paying the cost.");
        state.Player.Hand.Add(watcher);
        state.Player.Hand.Add(player);
        state.MemoryGauge = 5;
        var r = engine.PlayCard(player);
        Assert.True(r.CanExecute, r.Reason);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == watcher);
        // 5 - 3 (등장 코스트) = 2. 감시 디지몬이 등장하기 전의 등장에는 반응하지 않는다
        Assert.True(2 == state.MemoryGauge, string.Join(" | ", state.ActionLogs));
    }

    [Fact]
    public void Witness_TamerAlreadyInPlay_EndOfTurnFires()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B46-T", "[End of Your Turn] Gain 1 memory.");
        state.Player.Tamers.Add(tamer);
        var m = typeof(GoldfishEngine).GetMethod("TriggerEndOfTurn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        int mem = state.MemoryGauge;
        m.Invoke(engine, new object[] { state.Player });
        Assert.True(mem + 1 == state.MemoryGauge, string.Join(" | ", state.ActionLogs));
    }

    [Fact]
    public void Witness_TamerPlayedAtEndOfTurn_DoesNotTriggerThatEndOfTurn()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B46-T", "[End of Your Turn] Gain 1 memory.");
        var d = Digimon("B46-EOT", 5, "[End of Your Turn] You may play 1 [B46-T] from your hand without paying the cost.");
        AssertFull(tamer); AssertFull(d);
        state.Player.Hand.Add(tamer);
        Put(state.Player, d);
        var m = typeof(GoldfishEngine).GetMethod("TriggerEndOfTurn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        int mem = state.MemoryGauge;
        m.Invoke(engine, new object[] { state.Player });
        Assert.Contains(tamer, state.Player.Tamers);
        Assert.True(mem == state.MemoryGauge, string.Join(" | ", state.ActionLogs));   // 이미 지난 [자신의 턴 종료 시]에는 유발하지 않는다
    }

    [Fact]
    public void Witness_DigimonPlayedByWhenDigivolving_DoesNotSeeThatDigivolve()
    {
        var (state, engine, _) = NewMainPhase();
        var watcher = Digimon("B46-WATCHD", 4, "[All Turns] When one of your Digimon digivolves, gain 1 memory.");
        var evo = Digimon("B46-EVO", 4, "[When Digivolving] You may play 1 [B46-WATCHD] from your hand without paying the cost.");
        AssertFull(watcher); AssertFull(evo);
        var baby = Put(state.Player, Digimon("B46-BABY", 3));
        state.Player.Hand.Add(watcher);
        state.Player.Hand.Add(evo);
        state.MemoryGauge = 5;
        var r = engine.EvolveDigimon(baby, evo);
        Assert.True(r.CanExecute, r.Reason);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == watcher);
        Assert.True(4 == state.MemoryGauge, string.Join(" | ", state.ActionLogs));   // 5 - 1 (진화 코스트), 감시 디지몬은 진화한 뒤에 등장
    }

    [Fact]
    public void Witness_DigimonPlayedByOption_DoesNotSeeThatOptionUse()
    {
        var (state, engine, _) = NewMainPhase();
        var watcher = Digimon("B46-WATCHO", 4, "[All Turns] When you use an Option card, gain 1 memory.");
        var opt = Card("B46-OPT", "[Main] You may play 1 [B46-WATCHO] from your hand without paying the cost.", playCost: 1);
        AssertFull(watcher); AssertFull(opt);
        state.Player.Hand.Add(watcher);
        state.Player.Hand.Add(opt);
        Put(state.Player, Digimon("B46-BLUE", 3));
        state.MemoryGauge = 5;
        var r = engine.PlayCard(opt);
        Assert.True(r.CanExecute, r.Reason);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == watcher);
        Assert.True(4 == state.MemoryGauge, string.Join(" | ", state.ActionLogs));
    }

    [Fact]
    public void Witness_TamerPlayedByOnDeletion_DoesNotSeeThatDeletion()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B46-TW", "[All Turns] When one of your Digimon is deleted, gain 1 memory.");
        var d = Digimon("B46-ODT", 4, "[On Deletion] You may play 1 [B46-TW] from your hand without paying the cost.");
        AssertFull(tamer); AssertFull(d);
        state.Player.Hand.Add(tamer);
        var me = Put(state.Player, d);
        int mem = state.MemoryGauge;
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(tamer, state.Player.Tamers);
        Assert.True(mem == state.MemoryGauge, string.Join(" | ", state.ActionLogs));
    }

    // =====================================================================
    // 사용자 결정: BT11-034 ("대신") · EX10-029 (≪퇴화≫를 받지 않는다)
    // =====================================================================

    private const string BT11034Text = "[On Play] Place 1 Digimon card with the [Xros Heart] trait from your trash under 1 of your Tamers. If you have a Digimon with [Dorulumon] in its name or with [Dorulumon] in its digivolution cards, place up to 2 Digimon cards with the [Xros Heart] trait from your trash under 1 of your Tamers instead.";

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void BT11_034_Instead_PlacesTwoInsteadOfOne(bool dorulumon, int expectedUnder)
    {
        var (state, engine, _) = NewMainPhase();
        var cute = Digimon("B46-CUTE", 3, BT11034Text);
        AssertFull(cute);
        state.Player.Tamers.Add(Tamer("B46-TAMER"));
        for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon("B46-XH" + i, 3, traits: "Xros Heart"));
        if (dorulumon) Put(state.Player, Digimon("B46-DORU", 4, name: "Dorulumon"));
        state.Player.Hand.Add(cute);
        state.MemoryGauge = 5;
        var r = engine.PlayCard(cute);
        Assert.True(r.CanExecute, r.Reason);
        Assert.True(expectedUnder == state.Player.UnderTamers.Count, string.Join(" | ", state.ActionLogs));
    }

    [Fact]
    public void EX10_029_TextFix_OnLinked_GrantsDeDigivolveImmunity()
    {
        var (state, engine, _) = NewMainPhase();
        var warp = Card("EX10-029", "[Security] At the end of the battle, play this card without paying the cost.\n＜Blocker＞ (This Digimon can block in the blocker timing.)", CardType.Digimon, 4,
            name: "Warpmon", inherited: "By trashing 1 of this Digimon's link cards, effects don't affect 1 of your Digimon until your opponent's turn ends.", traits: "Transmission (App Name)");
        Assert.All(EffectSupport.For(warp).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        Assert.Contains(CardScripts.For(warp)!.Effects, e => e.Trigger == EffectTrigger.OnLinked);
        var host = Put(state.Player, Digimon("B46-HOST", 5, traits: "Appmon"));
        engine.LinkCard(state.Player, host, warp);
        Assert.Contains(warp, state.Player.Trash);   // 비용: 링크 카드(이 카드) 파기
        Assert.True(CardScripts.HasKeyword(host, Keyword.UnaffectedByOpponentDeDigivolve), string.Join(" | ", state.ActionLogs));
        // 영어 원문 그대로 "effects don't affect" 전부가 아니라 ≪퇴화≫만 (공식 한글)
        Assert.False(CardScripts.HasKeyword(host, Keyword.UnaffectedByOpponentEffects));
    }

    // =====================================================================
    // 2. "~ would leave the battle area / be deleted" — 대신 진화 · 이동 · 조그레스
    // =====================================================================

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b46_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BT17_095_DelayOption_LeavingDigimonAndHandCard_DnaIntoOmnimon_DoesNotLeave()
    {
        string id = "B46-OMNI-" + Guid.NewGuid().ToString("N")[..6];
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트오메가몬", MainText = "〔조그레스〕레드 Lv.6+블루 Lv.6 : 코스트 0" } }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var knight = Card("B46-KNIGHT", "[All Turns] When one of your level 6 Digimon with [Greymon] or [Garurumon] in its name would leave the battle area outside of a battle, ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n・That Digimon and a card in the hand may DNA digivolve into a Digimon card with [Omnimon] in its name in the hand.");
            AssertFull(knight);
            state.Player.OptionsInPlay.Add(new PlacedOption { Card = knight, PlacedTurn = 1 });
            var wg = Put(state.Player, Digimon("B46-WG", 6, name: "WarGreymon", color: CardColor.Red));
            var mg = Digimon("B46-MG", 6, name: "MetalGarurumon", color: CardColor.Blue);
            var omni = Digimon(id, 7, name: "Omnimon", color: CardColor.White);
            state.Player.Hand.Add(mg);
            state.Player.Hand.Add(omni);
            int total = CountAll(state.Player);
            engine.DeleteByOpponentEffect(state.Player, wg);
            var top = Assert.Single(state.Player.BattleArea);
            Assert.Same(omni, top.TopCard);
            Assert.True(top.HasSource("B46-WG") && top.HasSource("B46-MG"), string.Join(" | ", state.ActionLogs));
            Assert.Contains(knight, state.Player.Trash);
            Assert.Equal(total, CountAll(state.Player));
        });
    }

    [Fact]
    public void EX6_072_LevelSixAndHandCard_DnaIntoLevelSeven()
    {
        string id = "B46-L7-" + Guid.NewGuid().ToString("N")[..6];
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트Lv7", MainText = "〔조그레스〕레드 Lv.6+블루 Lv.6 : 코스트 0" } }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var opt = Card("B46-ASSEMBLY", "[Main] 1 of your level 6 Digimon and 1 card in the hand may DNA digivolve into a level 7 Digimon card in your hand.", playCost: 0, color: CardColor.Red);
            AssertFull(opt);
            Put(state.Player, Digimon("B46-R6", 6, color: CardColor.Red));
            var l7 = Digimon(id, 7, color: CardColor.White);
            state.Player.Hand.Add(Digimon("B46-B6", 6, color: CardColor.Blue));
            state.Player.Hand.Add(l7);
            state.Player.Hand.Add(opt);
            var r = engine.PlayCard(opt);
            Assert.True(r.CanExecute, r.Reason);
            Assert.Contains(state.Player.BattleArea, d => d.TopCard == l7 && d.HasSource("B46-B6"));
        });
    }

    [Fact]
    public void EX12_003_OneOfThemAndOtherDigimon_Dna_LeavingDigimonStays()
    {
        string id = "B46-ME7-" + Guid.NewGuid().ToString("N")[..6];
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트ME", MainText = "〔조그레스〕레드 Lv.5+블루 Lv.5 : 코스트 0" } }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var egg = Card("B46-KAPURI", "", CardType.Digitama, 2, inherited: "[All Turns] When any of your [ME] trait Digimon would leave the battle area other than by your effects, 1 of them and any of your other Digimon may DNA digivolve into an [ME] trait Digimon card in the hand.", traits: "ME");
            AssertFull(egg);
            var me = Put(state.Player, Digimon("B46-ME5", 5, color: CardColor.Red, traits: "ME"), sources: egg);
            var partner = Put(state.Player, Digimon("B46-P5", 5, color: CardColor.Blue));
            var dna = Digimon(id, 6, color: CardColor.Purple, traits: "ME");
            state.Player.Hand.Add(dna);
            engine.DeleteByOpponentEffect(state.Player, me);
            var nd = Assert.Single(state.Player.BattleArea);
            Assert.Same(dna, nd.TopCard);   // 떠나려던 디지몬은 조그레스 진화로 새 디지몬 — 떠나지 않는다 (공식 Q&A EX9-020 Q1)
            Assert.DoesNotContain(state.Player.Trash, x => x.CardId == "B46-ME5");
        });
    }

    [Fact]
    public void DnaEscape_ExistingCard_TwoOfYourDigimonDnaWhenLevel6WouldLeave_NewDigimonStays_EX9_020()
    {
        string id = "B46-ALTS-" + Guid.NewGuid().ToString("N")[..6];
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트알터S", MainText = "〔조그레스〕레드 Lv.6+블루 Lv.6 : 코스트 0" } }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var cres = Digimon("B46-CRES", 6, "[All Turns] When any of your level 6 Digimon would leave the battle area other than in battle, 2 of your Digimon may DNA digivolve into [Omnimon Alter-S] in the hand.", color: CardColor.Red);
            AssertFull(cres);
            var a = Put(state.Player, cres);
            Put(state.Player, Digimon("B46-B6X", 6, color: CardColor.Blue));
            var alts = Digimon(id, 7, name: "Omnimon Alter-S", color: CardColor.White);
            state.Player.Hand.Add(alts);
            engine.DeleteByOpponentEffect(state.Player, a);
            Assert.Contains(state.Player.BattleArea, d => d.TopCard == alts);   // v0.45.0까지는 조그레스 진화한 디지몬이 그대로 소멸했다
        });
    }

    [Fact]
    public void BT17_097_DelayOption_DigivolveIntoImperialdramon_PreventsDeletion()
    {
        var (state, engine, _) = NewMainPhase();
        var prim = Card("B46-PRIM", "[All Turns] When one of your Digimon with the [Free] trait would be deleted other than by one of your effects, ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n・By digivolving that Digimon into a Digimon card with [Imperialdramon] in its name in your hand without paying the cost, prevent that deletion.");
        AssertFull(prim);
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = prim, PlacedTurn = 1 });
        var pail = Put(state.Player, Digimon("B46-PAIL", 5, traits: "Free"));
        var imp = Digimon("B46-IMP", 6, name: "Imperialdramon: Dragon Mode", evoCost: 5);
        state.Player.Hand.Add(imp);
        int mem = state.MemoryGauge;
        engine.DeleteByOpponentEffect(state.Player, pail);
        Assert.Contains(pail, state.Player.BattleArea);
        Assert.Same(imp, pail.TopCard);
        Assert.Equal(mem, state.MemoryGauge);   // 코스트를 지불하지 않고
        Assert.Contains(prim, state.Player.Trash);
    }

    [Fact]
    public void BT19_099_DelayOption_PlaysWickedGodWithPlayCostOneHigher()
    {
        var (state, engine, _) = NewMainPhase();
        var desc = Card("B46-DESC", "[All Turns] When any of your Digimon with [Millenniummon] in its name would leave the battle area, ＜Delay＞ (By trashing this card after the placing turn, activate the effect below.)\n・ You may play 1 [Wicked God] trait Digimon card with a play cost 1 higher than that Digimon from your hand or trash without paying the cost.");
        AssertFull(desc);
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = desc, PlacedTurn = 1 });
        var mill = Put(state.Player, Digimon("B46-MILL", 6, name: "Millenniummon", playCost: 12));
        var wrong = Digimon("B46-WG12", 6, playCost: 12, traits: "Wicked God");
        var right = Digimon("B46-WG13", 7, playCost: 13, traits: "Wicked God");
        state.Player.Trash.Add(wrong);
        state.Player.Hand.Add(right);
        engine.DeleteByOpponentEffect(state.Player, mill);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == right);
        Assert.DoesNotContain(state.Player.BattleArea, d => d.TopCard == wrong);
        Assert.Contains(mill.TopCard, state.Player.Trash);   // 밀레니엄몬은 그대로 떠난다
    }

    [Fact]
    public void BT17_065_TrashCard_DigivolvesDeletingDorugamonIntoThisCard()
    {
        var (state, engine, _) = NewMainPhase();
        var dex = Digimon("B46-DEX", 4, "[Trash] [All Turns] When one of your [Dorugamon] would be deleted, by digivolving it into this card without paying the cost, prevent that deletion.", name: "DexDorugamon", evoCost: 3);
        AssertFull(dex);
        var doru = Put(state.Player, Digimon("B46-DORUGA", 4, name: "Dorugamon"));
        dex.EvoRequirementLevel = 4;
        state.Player.Trash.Add(dex);
        engine.DeleteByOpponentEffect(state.Player, doru);
        Assert.Contains(doru, state.Player.BattleArea);
        Assert.Same(dex, doru.TopCard);
        Assert.DoesNotContain(dex, state.Player.Trash);
    }

    [Fact]
    public void BT26_085_WouldLeave_DigivolveIntoDestroyMode_DoesNotLeave()
    {
        var (state, engine, _) = NewMainPhase();
        var slayer = Digimon("B46-SLAYER", 5, "[All Turns] When this Digimon would leave the battle area, by digivolving it into [Chronomon: Destroy Mode] in the hand or trash without paying the cost, it doesn't leave.");
        AssertFull(slayer);
        var me = Put(state.Player, slayer);
        var dm = Digimon("B46-CDM", 6, name: "Chronomon: Destroy Mode");
        state.Player.Trash.Add(dm);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(me, state.Player.BattleArea);
        Assert.Same(dm, me.TopCard);
    }

    [Fact]
    public void BT18_086_Breeding_MovesToBattleArea_SatanModeDoesNotLeave()
    {
        var (state, engine, _) = NewMainPhase();
        var larva = Digimon("B46-LARVA", 6, "[Breeding] [All Turns] When any of your [Lucemon: Satan Mode] would leave the battle area, by moving this Digimon to battle area, it doesn't leave.", dp: 0, name: "Lucemon: Larva");
        AssertFull(larva);
        var breed = new CardInstance(larva, 1);
        state.Player.BreedingArea = breed;
        var satan = Put(state.Player, Digimon("B46-SATAN", 7, name: "Lucemon: Satan Mode"));
        engine.DeleteByOpponentEffect(state.Player, satan);
        Assert.Contains(satan, state.Player.BattleArea);
        Assert.Contains(breed, state.Player.BattleArea);
        Assert.Null(state.Player.BreedingArea);
    }

    [Fact]
    public void BT18_086_ZeroDpDigimonCantBeDeleted_WhileNonWhiteLucemon()
    {
        var (state, engine, _) = NewMainPhase();
        var larva = Digimon("B46-LARVA2", 6, "[All Turns] While you have a non-white Digimon with [Lucemon] in its name, all of your 0 DP Digimon can't be deleted.", dp: 0, name: "Lucemon: Larva", color: CardColor.White);
        AssertFull(larva);
        var me = Put(state.Player, larva);
        var weak = Put(state.Player, Digimon("B46-WEAK", 3, dp: 2000));
        engine.ApplyDpMinus(weak, 3000, "test");
        Assert.DoesNotContain(weak, state.Player.BattleArea);   // 화이트 「루체몬」만 있다 — 조건이 맞지 않아 DP 0으로 소멸
        var (state2, engine2, _) = NewMainPhase();
        Put(state2.Player, larva);
        Put(state2.Player, Digimon("B46-LUCE", 5, name: "Lucemon: Chaos Mode", color: CardColor.Purple));
        var weak2 = Put(state2.Player, Digimon("B46-WEAK2", 3, dp: 2000));
        engine2.ApplyDpMinus(weak2, 3000, "test");
        Assert.Contains(weak2, state2.Player.BattleArea);   // DP 0이지만 소멸하지 않는다
        engine2.DeleteByOpponentEffect(state2.Player, weak2);
        Assert.Contains(weak2, state2.Player.BattleArea);
    }

    [Fact]
    public void BT11_082_YuuAmanoTamer_CantBeDeleted()
    {
        var (state, engine, _) = NewMainPhase();
        var tuwar = Digimon("B46-TUWAR", 4, "[All Turns] All of your [Yuu Amano] can't be deleted.");
        AssertFull(tuwar);
        Put(state.Player, tuwar);
        var yuu = Tamer("B46-YUU", name: "Yuu Amano");
        var other = Tamer("B46-OTHER", name: "Other Tamer");
        state.Player.Tamers.Add(yuu);
        state.Player.Tamers.Add(other);
        engine.DeleteTamer(state.Player, yuu);
        engine.DeleteTamer(state.Player, other);
        Assert.Contains(yuu, state.Player.Tamers);
        Assert.DoesNotContain(other, state.Player.Tamers);
    }

    [Fact]
    public void BT22_007_BreedingInherited_PlacesLeavingEaterUnderThisDigimon()
    {
        var (state, engine, _) = NewMainPhase();
        var mother = Card("B46-MOTHER", "", CardType.Digitama, 2, inherited: "[Breeding] [All Turns] [Once Per Turn] When any of your [Eater] trait Digimon would leave the battle area other than by your effects, you may place them as this Digimon's bottom digivolution cards.", traits: "Eater");
        AssertFull(mother);
        var b = new CardInstance(Digimon("B46-BREEDTOP", 3), 1);
        b.AddBottomSource(mother);
        state.Player.BreedingArea = b;
        var eater = Put(state.Player, Digimon("B46-EATER", 5, traits: "Eater"), sources: Digimon("B46-EATER4", 4));
        int total = CountAll(state.Player);
        engine.DeleteByOpponentEffect(state.Player, eater);
        Assert.DoesNotContain(eater, state.Player.BattleArea);
        Assert.True(b.HasSource("B46-EATER") && b.HasSource("B46-EATER4"), string.Join(" | ", state.ActionLogs));
        Assert.DoesNotContain(state.Player.Trash, x => x.CardId == "B46-EATER");
        Assert.Equal(total, CountAll(state.Player));
    }

    [Fact]
    public void BT19_053_RoyalBaseLeaving_PlacedFaceUpAtSecurityBottom()
    {
        var (state, engine, _) = NewMainPhase();
        var queen = Digimon("B46-QUEEN", 6, "[All Turns] When any of your [Royal Base] trait Digimon would leave the battle area other than in battle, you may place those Digimon face-up at the bottom of your security stack.", traits: "Royal Base");
        AssertFull(queen);
        Put(state.Player, queen);
        var bee = Put(state.Player, Digimon("B46-BEE", 4, traits: "Royal Base"), sources: Digimon("B46-BEE3", 3));
        int sec = state.Player.SecurityStack.Count;
        engine.DeleteByOpponentEffect(state.Player, bee);
        Assert.DoesNotContain(bee, state.Player.BattleArea);
        Assert.Equal(sec + 1, state.Player.SecurityStack.Count);
        Assert.Equal("B46-BEE", state.Player.SecurityStack[^1].CardId);
        Assert.Contains(state.Player.Trash, x => x.CardId == "B46-BEE3");   // 진화원은 트래시
    }

    [Fact]
    public void BT23_080_CsDigimonDeleted_TamerToDeckBottom_DigimonToSecurityTop()
    {
        var (state, engine, _) = NewMainPhase();
        var yu = Tamer("B46-YUNOGI", "[All Turns] When any of your Digimon with the [CS] trait would be deleted, by returning this Tamer to the bottom of the deck, place 1 of those Digimon as the top security card.");
        AssertFull(yu);
        state.Player.Tamers.Add(yu);
        var cs = Put(state.Player, Digimon("B46-CS", 5, traits: "CS"));
        engine.DeleteByOpponentEffect(state.Player, cs);
        Assert.DoesNotContain(cs, state.Player.BattleArea);
        Assert.Equal("B46-CS", state.Player.SecurityStack[0].CardId);
        Assert.Same(yu, state.Player.MainDeck[^1]);
    }

    [Fact]
    public void EX13_024_SuchDigimon_SuspendToStay()
    {
        var (state, engine, _) = NewMainPhase();
        var slay = Digimon("B46-SLAYD", 6, "[All Turns] [Once Per Turn] When any of your [Dracomon] or [Examon] text Digimon would leave the battle area, by suspending 1 of your such Digimon, they don't leave.", name: "Slayerdramon");
        AssertFull(slay);
        var me = Put(state.Player, slay);
        var draco = Put(state.Player, Digimon("B46-DRACO", 3, "[On Play] Gain 1 memory. [Dracomon]"));
        engine.DeleteByOpponentEffect(state.Player, draco);
        Assert.Contains(draco, state.Player.BattleArea);
        Assert.True(me.IsSuspended || draco.IsSuspended);
    }

    // =====================================================================
    // 3. 이름 주어 지속 효과 · 보호
    // =====================================================================

    [Fact]
    public void BT12_043_NameSubject_DpAndSecurityAttack_YourTurn()
    {
        var (state, engine, _) = NewMainPhase();
        var shine = Digimon("B46-SHINE", 6, "[Your Turn] All of your [Marcus Damon] in play get +3000 DP and gain ＜Security A. +1＞ (This Digimon checks 1 additional security card.)");
        AssertFull(shine);
        var me = Put(state.Player, shine);
        me.IsOwnersTurn = true;
        var marcus = Put(state.Player, Digimon("B46-MARCUS", 4, dp: 4000, name: "Marcus Damon"));
        marcus.IsOwnersTurn = true;
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, marcus.GetCurrentDP());
    }

    [Theory]
    [InlineData("[On Play] [When Digivolving] Until your opponent's turn ends, their ＜De-Digivolve＞ effects don't affect 1 of your Digimon, and it gets +3000 DP.")]
    [InlineData("[On Play] [When Digivolving] 1 of your [DS] trait Digimon can't have its DP reduced or be affected by your opponent's ＜De-Digivolve＞ effects until the end of their turn.")]
    public void DeDigivolveProtection_Rewrites_GrantImmunity(string text)
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B46-PROT-" + Math.Abs(text.GetHashCode()), 4, text, traits: "DS");
        AssertFull(card);
        state.Player.Hand.Add(card);
        state.MemoryGauge = 5;
        var r = engine.PlayCard(card);
        Assert.True(r.CanExecute, r.Reason);
        Assert.Contains(state.Player.BattleArea, d => CardScripts.HasKeyword(d, Keyword.UnaffectedByOpponentDeDigivolve));
    }

    [Fact]
    public void BT3_099_NeitherPlayersDigimonCanBeDeletedInBattle()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B46-STOP", "[Main] Neither player's Digimon can be deleted in battle for the turn.", playCost: 0);
        AssertFull(opt);
        var mine = Put(state.Player, Digimon("B46-M", 3));
        var theirs = Put(state.DummyOpponent, Digimon("B46-THEIRS", 3));
        state.Player.Hand.Add(opt);
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.True(CardScripts.HasKeyword(mine, Keyword.CannotBeDeletedInBattle));
        Assert.True(CardScripts.HasKeyword(theirs, Keyword.CannotBeDeletedInBattle));
    }

    [Fact]
    public void EX4_042_ThisAndKnightmon_Unblockable()
    {
        var (state, engine, _) = NewMainPhase();
        var dm = Digimon("B46-DMAIL", 4, "[Your Turn] This Digimon and all of your Digimon with [Knightmon] in their names or [Knightsmon] in their names are unblockable.");
        AssertFull(dm);
        var me = Put(state.Player, dm);
        var kn = Put(state.Player, Digimon("B46-KN", 5, name: "DarkKnightmon"));
        var other = Put(state.Player, Digimon("B46-OT", 5));
        foreach (var d in state.Player.BattleArea) d.IsOwnersTurn = true;
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(me, Keyword.Unblockable));
        Assert.True(CardScripts.HasKeyword(kn, Keyword.Unblockable));
        Assert.False(CardScripts.HasKeyword(other, Keyword.Unblockable));
    }

    // =====================================================================
    // 4. "When your effects add to decks" (BT26-015)
    // =====================================================================

    [Fact]
    public void BT26_015_WhenYourEffectsAddToDecks_InheritedUnsuspends()
    {
        var (state, engine, _) = NewMainPhase();
        var bute = Digimon("B46-BUTEN", 5, inherited: "[All Turns] [Once Per Turn] When your effects add to decks, this Digimon with [Chronomon] in its text may unsuspend.");
        AssertFull(bute);
        var holder = Put(state.Player, Digimon("B46-CHRONO", 6, "[On Play] Gain 1 memory. [Chronomon]"), suspended: true, sources: bute);
        var returner = Digimon("B46-RET", 3, "[On Play] By returning 1 card in your trash to the bottom of the deck, gain 1 memory.");
        AssertFull(returner);
        state.Player.Trash.Add(Digimon("B46-TRASHED", 3));
        state.Player.Hand.Add(returner);
        state.MemoryGauge = 5;
        Assert.True(engine.PlayCard(returner).CanExecute);
        Assert.Equal("B46-TRASHED", state.Player.MainDeck[^1].CardId);
        Assert.False(holder.IsSuspended, string.Join(" | ", state.ActionLogs));
    }

    [Fact]
    public void BT26_015_RevealedCardsReturned_DoNotCountAsAdded()
    {
        var (state, engine, _) = NewMainPhase();
        var bute = Digimon("B46-BUTEN2", 5, inherited: "[All Turns] [Once Per Turn] When your effects add to decks, this Digimon with [Chronomon] in its text may unsuspend.");
        var holder = Put(state.Player, Digimon("B46-CHRONO2", 6, "[On Play] Gain 1 memory. [Chronomon]"), suspended: true, sources: bute);
        var reveal = Digimon("B46-REV", 3, "[On Play] Reveal the top 3 cards of your deck. Add 1 [Nothing] among them to the hand. Return the rest to the bottom of the deck.");
        AssertFull(reveal);
        state.Player.Hand.Add(reveal);
        state.MemoryGauge = 5;
        Assert.True(engine.PlayCard(reveal).CanExecute);
        Assert.True(holder.IsSuspended);   // 덱에 있던 카드를 되돌린 것은 \"추가\"가 아니다
    }

    // =====================================================================
    // 5. 진화원 아래에 놓기 비용 (EX3-013 · EX1-073 · P-096 · EX2-048)
    // =====================================================================

    [Fact]
    public void EX3_013_PlaceUpTo3DifferentCardNumbers_DeDigivolveForEach()
    {
        var (state, engine, _) = NewMainPhase();
        var chaos = Digimon("B46-CHAOS", 6, "[On Play] [When Digivolving] By placing up to 3 red and/or black level 5 cards with the [Cyborg] trait and different card numbers from your hand and/or trash under this Digimon as its bottom digivolution cards, ＜De-Digivolve 1＞ 1 of your opponent's Digimon for each card placed by this effect.");
        AssertFull(chaos);
        var a = Digimon("B46-CY1", 5, color: CardColor.Red, traits: "Cyborg");
        state.Player.Trash.Add(a);
        state.Player.Trash.Add(a);   // 같은 카드 넘버 — 1장만
        state.Player.Hand.Add(Digimon("B46-CY2", 5, color: CardColor.Black, traits: "Cyborg"));
        state.Player.Trash.Add(Digimon("B46-CY3", 5, color: CardColor.Blue, traits: "Cyborg"));   // 블루 — 안 된다
        var opp = Put(state.DummyOpponent, Digimon("B46-O6", 6), sources: new[] { Digimon("B46-O5", 5), Digimon("B46-O4", 4), Digimon("B46-O3", 3) });
        state.Player.Hand.Add(chaos);
        state.MemoryGauge = 8;
        Assert.True(engine.PlayCard(chaos).CanExecute);
        var me = state.Player.BattleArea.Single(d => d.TopCard == chaos);
        Assert.Equal(2, me.EvolutionSources.Count);
        Assert.Equal("B46-O4", opp.TopCard.CardId);   // 2장 → 《퇴화 1》 2번
    }

    [Fact]
    public void EX1_073_PlaceInThisCardsSources_GainMemoryForEach()
    {
        var (state, engine, _) = NewMainPhase();
        var machine = Digimon("B46-MACHINE", 6, "[On Play] You may place up to 5 level 5 red and/or black cards with the [Cyborg] trait and different card numbers from your hand and/or trash in this card's digivolution cards to gain 1 memory for each card placed.", playCost: 0);
        AssertFull(machine);
        for (int i = 0; i < 3; i++) state.Player.Trash.Add(Digimon("B46-MC" + i, 5, color: CardColor.Black, traits: "Cyborg"));
        state.Player.Hand.Add(machine);
        state.MemoryGauge = 2;
        Assert.True(engine.PlayCard(machine).CanExecute);
        Assert.Equal(5, state.MemoryGauge);
    }

    [Fact]
    public void P_096_FromUnderTamersOrTrash_DpForEachPlaced()
    {
        var (state, engine, _) = NewMainPhase();
        var prism = Card("B46-PRISM", "[Main] By placing up to 2 Digimon cards with ＜Save＞ in their texts from under your Tamers or your trash under 1 of your Digimon with ＜Save＞ in its text as its bottom digivolution cards, that Digimon gets +1000 DP for each card placed by this effect.", playCost: 0);
        AssertFull(prism);
        state.Player.Tamers.Add(Tamer("B46-HUNTER"));
        state.Player.UnderTamers.Add(Digimon("B46-SV1", 3, "[On Deletion] ＜Save＞"));
        state.Player.Trash.Add(Digimon("B46-SV2", 3, "[On Deletion] ＜Save＞"));
        var me = Put(state.Player, Digimon("B46-SAVER", 5, "[On Deletion] ＜Save＞", dp: 5000));
        state.Player.Hand.Add(prism);
        Assert.True(engine.PlayCard(prism).CanExecute);
        Assert.Equal(2, me.EvolutionSources.Count);
        Assert.Equal(7000, me.GetCurrentDP());
        Assert.Empty(state.Player.UnderTamers);
    }

    [Fact]
    public void EX2_048_PlaceSearcherFromPlayUnderMotherDReaper()
    {
        var (state, engine, _) = NewMainPhase();
        var bub = Digimon("B46-BUBBLES", 3, "[On Play] Place 1 of your [ADR-02 Searcher]s from in play or from your hand under 1 of your [Mother D-Reaper]s as its bottom digivolution card.", playCost: 0);
        AssertFull(bub);
        var mother = Put(state.Player, Digimon("B46-MDR", 6, name: "Mother D-Reaper"));
        var searcher = Put(state.Player, Digimon("B46-SEARCH", 3, name: "ADR-02 Searcher"));
        state.Player.Hand.Add(bub);
        int total = CountAll(state.Player);
        Assert.True(engine.PlayCard(bub).CanExecute);
        Assert.DoesNotContain(searcher, state.Player.BattleArea);
        Assert.True(mother.HasSource("B46-SEARCH"));
        Assert.Equal(total, CountAll(state.Player));
    }

    // =====================================================================
    // 6. 한글 · 다른 표기
    // =====================================================================

    [Theory]
    [InlineData("[On Play] When played from digivolution cards, ＜Draw 2＞ (Draw 2 cards from your deck.)", "진화원에서 등장하고 있었다면")]
    [InlineData("Security Effect You may play 1 [Gabumon] from your hand or trash without paying the cost. Then, add this card to the hand.", "코스트를 지불하지 않고 등장")]
    [InlineData("[All Turns] This Digimon's DP can't be reduced.", "마이너스되지 않는다")]
    public void Variants_SupportedWithKorean(string text, string koPart)
    {
        var card = Digimon("B46-VAR-" + Math.Abs(text.GetHashCode()), 5, text.StartsWith("Security") ? "" : text, inherited: text.StartsWith("Security") ? text : "");
        AssertFull(card);
        var ko = KoreanEffectRenderer.RenderClause(text);
        Assert.NotNull(ko);
        Assert.Contains(koPart, ko);
    }

    [Theory]
    [InlineData("[All Turns] [Once Per Turn] When your effects add to decks, this Digimon with [Chronomon] in its text may unsuspend.", "자신의 효과로 덱에 카드가 추가되었을 때,")]
    [InlineData("・That Digimon and a card in the hand may DNA digivolve into a Digimon card with [Omnimon] in its name in the hand.", "그 디지몬과 패의 카드로")]
    [InlineData("[Main] By placing up to 2 Digimon cards with ＜Save＞ in their texts from under your Tamers or your trash under 1 of your Digimon with ＜Save＞ in its text as its bottom digivolution cards, that Digimon gets +1000 DP for each card placed by this effect.", "자신의 테이머 아래 또는 트래시에서")]
    [InlineData("[All Turns] When any of your [Royal Base] trait Digimon would leave the battle area other than in battle, you may place those Digimon face-up at the bottom of your security stack.", "시큐리티 아래에 앞면으로 놓을 수 있다")]
    public void NewClauses_KoreanRendered(string text, string koPart)
    {
        var ko = KoreanEffectRenderer.RenderClause(text);
        Assert.NotNull(ko);
        Assert.Contains(koPart, ko);
    }

    [Fact]
    public void BT17_065_KoreanNameEvolutionCondition_AllowsDigivolveFromSameLevel()
    {
        string dexId = "B46-DEXK-" + Guid.NewGuid().ToString("N")[..6], doruId = "B46-DORUK-" + Guid.NewGuid().ToString("N")[..6];
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = dexId, Name = "테스트데크스도루가몬", MainText = "〔진화〕「테스트도루가몬」 : 코스트 0[트래시]【서로의 턴】" },
            new KoreanCard { CardId = doruId, Name = "테스트도루가몬", MainText = "" },
        }, () =>
        {
            var (state, engine, _) = NewMainPhase();
            var dex = Digimon(dexId, 4, "[Trash] [All Turns] When one of your [Dorugamon] would be deleted, by digivolving it into this card without paying the cost, prevent that deletion.", name: "DexDorugamon");
            var doru = Put(state.Player, Digimon(doruId, 4, name: "Dorugamon"));   // 영어 진화 조건은 Lv.3 — 공식 한글 〔진화〕「도루가몬」으로만 진화할 수 있다
            Assert.False(ActionValidator.ValidateEvolution(doru, dex).CanExecute);
            state.Player.Trash.Add(dex);
            engine.DeleteByOpponentEffect(state.Player, doru);
            Assert.Same(dex, doru.TopCard);
            Assert.Contains(doru, state.Player.BattleArea);
        });
    }

    [Fact]
    public void EX2_048_MotherDReaperInBreedingArea_CanBeHost()
    {
        var (state, engine, _) = NewMainPhase();
        var bub = Digimon("B46-BUBBLES2", 3, "[On Play] Place 1 of your [ADR-02 Searcher]s from in play or from your hand under 1 of your [Mother D-Reaper]s as its bottom digivolution card.", playCost: 0);
        var mother = new CardInstance(Card("B46-MDR2", "", CardType.Digitama, 2, name: "Mother D-Reaper"), 1);
        state.Player.BreedingArea = mother;
        var searcher = Digimon("B46-SEARCH2", 3, name: "ADR-02 Searcher");
        state.Player.Hand.Add(searcher);
        state.Player.Hand.Add(bub);
        Assert.True(engine.PlayCard(bub).CanExecute);
        Assert.True(mother.HasSource("B46-SEARCH2"));
        Assert.DoesNotContain(searcher, state.Player.Hand);
    }
}
