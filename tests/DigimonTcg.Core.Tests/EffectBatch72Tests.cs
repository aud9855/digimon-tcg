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
/// v0.72.0 미지원 문장 46차 묶음: 특징 "가진"(일치) · "포함하는"(포함) 구분 (종합 룰 2-3-2-3 · 2-3-2-4) ·
/// 테이머 레스트 비용 묶음 (EX11-058 · EX11-060 · EX11-064 · EX11-066 · BT22-092) · 공식 영어 카드 리스트의 조그레스 조건 ·
/// EX11-033 · EX11-042 공식 영어 문장 (패 또는 이 디지몬의 링크 카드에서 등장)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch72Tests
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
            state.Player.MainDeck.Add(Digimon("B72-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B72-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B72-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B72-OSEC" + i, 3, color: CardColor.White, dp: 1000));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsOwnersTurn = true, IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    private static void WithEnglish(IEnumerable<EnglishCardRule> rules, Action run)
    {
        EnglishCardRules.Use(rules);
        try { run(); }
        finally { EnglishCardRules.Reload(); }
    }

    // =====================================================================
    // 1. 특징 "[X] trait"(일치) · "[X] in any of its traits"(포함) — 종합 룰 2-3-2-3 · 2-3-2-4
    // =====================================================================

    [Fact]
    public void ExactTraitMark_MatchesWholeTraitOnly()
    {
        string exact(string x) => x + CardNames.TraitExactMark72;
        var dragon = Digimon(Uid("B72-MD-"), 6, traits: new[] { "Machine Dragon", "Virus" });
        var machine = Digimon(Uid("B72-MA-"), 5, traits: "Machine");
        var arch = Digimon(Uid("B72-AR-"), 5, traits: "Archangel");
        var search = Digimon(Uid("B72-SE-"), 4, traits: new[] { "Search (App Name)", "Appmon" });
        var superSearch = Digimon(Uid("B72-SS-"), 5, traits: "Super Search (App Name)");
        var xab = Digimon(Uid("B72-XA-"), 5, traits: "X Antibody}}");
        Assert.False(TargetSpec.HasTrait(dragon, exact("Machine")));
        Assert.True(TargetSpec.HasTrait(dragon, "Machine"));               // 포함 판정은 그대로
        Assert.True(TargetSpec.HasTrait(machine, exact("Machine")));
        Assert.False(TargetSpec.HasTrait(arch, exact("Angel")));
        Assert.True(TargetSpec.HasTrait(search, exact("Search")));         // 어플몬 앱 이름 표시는 뗀다
        Assert.False(TargetSpec.HasTrait(superSearch, exact("Search")));
        Assert.True(TargetSpec.HasTrait(xab, exact("X Antibody")));        // 영어 데이터 잡음 "}}"
        Assert.True(TargetSpec.HasTrait(Digimon(Uid("B72-HC-"), 4, traits: "Hudie/CS"), exact("Hudie")));
        Assert.True(CardNames.Is(machine, machine.Name + CardNames.TraitExactMark72));   // 이름 판정은 표식을 뗀다
    }

    [Fact]
    public void TraitSentence_ExactForm_DoesNotPickSuperTrait()
    {
        var opt = Card(Uid("B72-RET-"), "[Main] Return 1 [Machine] trait Digimon card from your trash to the hand.", color: CardColor.Blue, playCost: 0);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon(Uid("B72-BLUE-"), 3, color: CardColor.Blue));
        var dragon = Digimon(Uid("B72-TMD-"), 6, traits: "Machine Dragon");
        state.Player.Trash.Add(dragon);
        state.Player.Hand.Add(opt);
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.Contains(dragon, state.Player.Trash);   // 전에는 「Machine Dragon」 안의 Machine으로 패에 넣었다
        // 특징이 정확히 Machine이면 패로
        var opt2 = Card(Uid("B72-RET2-"), "[Main] Return 1 [Machine] trait Digimon card from your trash to the hand.", color: CardColor.Blue, playCost: 0);
        var machine = Digimon(Uid("B72-TMA-"), 5, traits: new[] { "Machine", "Data" });
        state.Player.Trash.Add(machine);
        state.Player.Hand.Add(opt2);
        Assert.True(engine.PlayCard(opt2).CanExecute);
        Assert.Contains(machine, state.Player.Hand);
    }

    [Fact]
    public void TraitSentence_ContainsForm_StillContains()
    {
        // EX3-028 "Add 1 yellow card with [Angel], ~ in any of its traits (other than [Three Great Angels] trait) and 1 card with the [Four Great Dragons] trait among them"
        var me = Digimon(Uid("B72-ANG-"), 4, "[On Play] Reveal the top 4 cards of your deck. Add 1 yellow card with [Angel], [Cherub], [Throne], [Authority], [Seraph] or [Virtue] in any of its traits (other than [Three Great Angels] trait) and 1 card with the [Four Great Dragons] trait among them to your hand. Place the rest at the bottom of your deck in any order.", color: CardColor.Yellow);
        AllSupported(me);
        foreach (bool withArch in new[] { true, false })
        {
            var (state, engine) = NewMainPhase();
            var three = Digimon(Uid("B72-3GA-"), 6, color: CardColor.Yellow, traits: "Three Great Angels");
            var arch = Digimon(Uid("B72-TARC-"), 5, color: CardColor.Yellow, traits: "Archangel");
            var dragon = Digimon(Uid("B72-4GD-"), 6, color: CardColor.Yellow, traits: "Four Great Dragons");
            state.Player.MainDeck.InsertRange(0, withArch ? new[] { three, arch, dragon } : new[] { three, dragon });
            state.Player.Hand.Add(me);
            Assert.True(engine.PlayCard(me).CanExecute);
            Assert.Equal(withArch, state.Player.Hand.Contains(arch));   // "포함하는" — Archangel에 Angel이 들어 있다
            Assert.DoesNotContain(three, state.Player.Hand);            // 제외한 특징 (표식을 붙이지 않는다)
            Assert.Contains(dragon, state.Player.Hand);
        }
    }

    [Fact]
    public void TraitStatic_ExactForm_EX1_007Style()
    {
        // EX1-007 "[Your Turn] While this Digimon has the [Machine] trait, it gains ＜Security A. +1＞" — 맨 위가 Machine Dragon이면 얻지 않는다
        var src = Digimon(Uid("B72-SRC-"), 3, inherited: "[Your Turn] While this Digimon has the [Machine] trait, it gains ＜Security A. +1＞.");
        Assert.All(EffectSupport.For(src).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon(Uid("B72-TOPMD-"), 6, traits: "Machine Dragon"), false, src);
        var e = Put(state.Player, Digimon(Uid("B72-TOPMA-"), 6, traits: "Machine"), false, Digimon(Uid("B72-SRC2-"), 3, inherited: src.InheritedEffectText));
        engine.RecalculateFieldBuffs();
        Assert.Equal(1, d.GetSecurityAttackCount());
        Assert.Equal(2, e.GetSecurityAttackCount());
    }

    [Fact]
    public void TraitReveal_ExactForm_BT5_034Style()
    {
        var me = Digimon(Uid("B72-REV-"), 4, "[On Play] Reveal the top 3 cards of your deck. Add up to 2 yellow Digimon cards with the [Warrior] or [Holy Warrior] trait among them to the hand. Return the rest to the bottom of the deck in any order.", color: CardColor.Yellow);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var ancient = Digimon(Uid("B72-AHW-"), 5, color: CardColor.Yellow, traits: "Ancient Holy Warrior");
        var holy = Digimon(Uid("B72-HW-"), 5, color: CardColor.Yellow, traits: "Holy Warrior");
        var magic = Digimon(Uid("B72-MW-"), 5, color: CardColor.Yellow, traits: "Magic Warrior");
        state.Player.MainDeck.InsertRange(0, new[] { ancient, holy, magic });
        state.Player.Hand.Add(me);
        Assert.True(engine.PlayCard(me).CanExecute);
        Assert.Contains(holy, state.Player.Hand);
        Assert.DoesNotContain(ancient, state.Player.Hand);   // 전에는 Ancient Holy Warrior · Magic Warrior도 [Warrior]에 맞았다
        Assert.DoesNotContain(magic, state.Player.Hand);
    }

    [Fact]
    public void OfficialKoreanContains_BT13_050_KeepsContains()
    {
        var bt13050 = new CardData { CardId = "BT13-050", Name = "Sunflowmon", Type = CardType.Digimon };
        string text = "[Main] You may suspend this Digimon to digivolve 1 of your Digimon into a Digimon card with the [Fairy] trait trait in your hand.";
        Assert.DoesNotContain(CardNames.TraitExactMark72.ToString(), SentenceParser.MarkExactTraits72(text, bt13050));   // 공식 한글 "특징으로 「요정형」을 포함하는"
        Assert.Contains("[Fairy" + CardNames.TraitExactMark72 + "]", SentenceParser.MarkExactTraits72(text, new CardData { CardId = "B72-OTHER", Name = "x" }));
        Assert.DoesNotContain(CardNames.TraitExactMark72.ToString(), SentenceParser.MarkExactTraits72("Add 1 card with [Avian] or [Bird] in any of its traits", new CardData { CardId = "B72-OTHER", Name = "x" }));
        Assert.DoesNotContain(CardNames.TraitExactMark72.ToString(), SentenceParser.MarkExactTraits72("1 of your [TS] trait Digimon", new CardData { CardId = "B72-OTHER", Name = "x" }));   // 약칭은 그대로 (71차)
    }

    // =====================================================================
    // 2. 테이머 레스트 비용 묶음
    // =====================================================================

    [Fact]
    public void BT22_092_ActivatesMainEffectOfThatDigimon_IncludingInherited()
    {
        string text = "[Your Turn] When your Digimon are played or digivolve, if any of them have the [Flame] or [CS] trait, by suspending this Tamer, activate 1 of those Digimon's [Main] effects. If this activated any effect, gain 1 memory.";
        var tamer = Card(Uid("B72-T92-"), text, CardType.Tamer, color: CardColor.Red);
        AllSupported(tamer);
        Assert.Equal("【자신의 턴】 자신의 디지몬이 등장하거나 진화했을 때, 그중 특징으로 「화염형」/「CS」를 가진 디지몬이 있다면, 이 테이머를 레스트시키는 것으로, 그 디지몬의 【메인】 효과 1개를 발휘한다. 이 효과로 발휘했다면, 메모리를 +1 한다.", Ko(text));
        var (state, engine) = NewMainPhase(memory: 3);
        engine.AddTamer(state.Player, tamer);
        var flame = Put(state.Player, Digimon(Uid("B72-FL-"), 4, "[Main] Gain 1 memory.", traits: "Flame"));
        engine.FireForSide(EffectTrigger.AllyDigimonPlayed, state.Player, flame);
        Assert.Equal(5, state.MemoryGauge);   // 【메인】 +1 · 이 효과로 발휘했다면 +1
        // 공식 Q&A BT22-092 Q1: 진화원 효과의 【메인】도
        var (s2, e2) = NewMainPhase(memory: 3);
        var tamer2 = Card(Uid("B72-T92b-"), text, CardType.Tamer, color: CardColor.Red);
        e2.AddTamer(s2.Player, tamer2);
        var src = Digimon(Uid("B72-SRCM-"), 3, inherited: "[Main] Gain 1 memory.");
        var cs = Put(s2.Player, Digimon(Uid("B72-CS-"), 5, traits: "CS"), false, src);
        e2.FireForSide(EffectTrigger.AllyDigimonDigivolved, s2.Player, cs);
        Assert.Equal(5, s2.MemoryGauge);
        // 【메인】 효과가 없으면 메모리를 얻지 않는다
        var (s3, e3) = NewMainPhase(memory: 3);
        e3.AddTamer(s3.Player, Card(Uid("B72-T92c-"), text, CardType.Tamer, color: CardColor.Red));
        var plain = Put(s3.Player, Digimon(Uid("B72-PL-"), 4, traits: "Flame"));
        e3.FireForSide(EffectTrigger.AllyDigimonPlayed, s3.Player, plain);
        Assert.Equal(3, s3.MemoryGauge);
    }

    [Fact]
    public void EX11_058_PlayedByDecode_OpponentDigimonCantSuspend()
    {
        string text = "[All Turns] When your Digimon are played or digivolve, if any of them have [Aqua] or [Sea Animal] in any of their traits, by suspending this Tamer, ＜Draw 1＞ (Draw 1 card from your deck.). If played by ＜Decode＞, 1 of your opponent's Digimon can't suspend until their turn ends.";
        var tamer = Card(Uid("B72-T58-"), text, CardType.Tamer, color: CardColor.Blue);
        AllSupported(tamer);
        Assert.Contains("그 디지몬이 《디코드》로 등장했다면, 상대의 턴 종료 시까지, 상대의 디지몬 1마리는 레스트할 수 없다.", Ko(text));
        foreach (bool decode in new[] { false, true })
        {
            var (state, engine) = NewMainPhase();
            engine.AddTamer(state.Player, Card(Uid("B72-T58x-"), text, CardType.Tamer, color: CardColor.Blue));
            var opp = Put(state.DummyOpponent, Digimon(Uid("B72-OPP-"), 5));
            opp.IsOwnersTurn = false;
            var aqua = Digimon(Uid("B72-AQ-"), 4, traits: "Aquabeast");
            state.Player.Hand.Add(aqua);
            int hand = state.Player.Hand.Count;
            bool Play() => engine.TryPlayByEffect(state.Player, aqua, PlayOrigin.Sources, () => state.Player.Hand.Remove(aqua));
            Assert.True(decode ? engine.PlayByDecode72(state.Player, aqua, Play) : Play());
            Assert.Equal(hand, state.Player.Hand.Count);   // 1장 내고 1장 드로우
            Assert.Equal(decode, EffectRules.IsSuspendLocked(state, opp));
        }
    }

    [Fact]
    public void EX11_060_DeletedByOverclock_PlaysPuppet()
    {
        string text = "[All Turns] When any of your Tokens or [Puppet] trait Digimon are deleted, by suspending this Tamer, ＜Draw 1＞ (Draw 1 card from your deck.) If deleted by ＜Overclock＞, you may play 1 level 4 or lower [Puppet] trait Digimon card from your hand without paying the cost.";
        var tamer = Card(Uid("B72-T60-"), text, CardType.Tamer, color: CardColor.Yellow);
        AllSupported(tamer);
        Assert.Contains("그 디지몬이 《오버클럭》으로 소멸했다면, 자신의 패에서 Lv.4 이하인 특징으로 「퍼펫형」을 가진 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다.", Ko(text));
        foreach (bool overclock in new[] { false, true })
        {
            var (state, engine) = NewMainPhase();
            engine.AddTamer(state.Player, Card(Uid("B72-T60x-"), text, CardType.Tamer, color: CardColor.Yellow));
            var fodder = Put(state.Player, Digimon(Uid("B72-PUP-"), 3, traits: "Puppet"));
            var next = Digimon(Uid("B72-PUP4-"), 4, traits: "Puppet");
            state.Player.Hand.Add(next);
            if (overclock) engine.NoteOverclockFodder72(fodder);
            state.Player.BattleArea.Remove(fodder);
            engine.FireForSide(EffectTrigger.AllyDigimonDeleted, state.Player, fodder);
            Assert.Equal(overclock, state.Player.BattleArea.Any(d => d.TopCard == next));
        }
    }

    [Fact]
    public void EX11_064_ReducesByOpponentFaceUpSecurity()
    {
        string text = "[Your Turn] When one of your [Cyborg] or [Machine] trait Digimon attacks, by suspending this Tamer, that Digimon may digivolve into a [Cyborg] or [Machine] trait Digimon card in the hand. For each of your opponent's face-up security cards, reduce this effect's digivolution cost by 1.";
        var tamer = Card(Uid("B72-T64-"), text, CardType.Tamer, color: CardColor.Black);
        AllSupported(tamer);
        Assert.EndsWith("상대의 앞면 시큐리티 1장마다 이 효과로 지불하는 코스트 -1.", Ko(text));
        var (state, engine) = NewMainPhase(memory: 5);
        engine.AddTamer(state.Player, tamer);
        var atk = Put(state.Player, Digimon(Uid("B72-CY-"), 4, color: CardColor.Black, traits: "Cyborg"));
        var into = Digimon(Uid("B72-MA5-"), 5, color: CardColor.Black, evoCost: 3, traits: "Machine");
        var dragon = Digimon(Uid("B72-MD5-"), 5, color: CardColor.Black, evoCost: 1, traits: "Machine Dragon");   // [Machine] trait 아님
        state.Player.Hand.Add(dragon);
        state.Player.Hand.Add(into);
        foreach (var x in state.DummyOpponent.SecurityStack.Take(2)) state.DummyOpponent.FaceUpSecurity.Add(x);
        engine.FireForSide(EffectTrigger.AllyDigimonAttacks, state.Player, atk);
        Assert.Equal(into, atk.TopCard);
        Assert.Equal(5 - 1, state.MemoryGauge);   // 진화 코스트 3 - 앞면 시큐리티 2
        Assert.Contains(dragon, state.Player.Hand);
    }

    [Fact]
    public void EX11_066_PlacesVemmonUnderThatDigimon()
    {
        string text = "[All Turns] When your Digimon are played or digivolve, if any of them have [Vemmon] in their texts, by suspending this Tamer, reveal the top 2 cards of your deck. Place all [Vemmon] among them as any of those Digimon's bottom digivolution cards. Trash the rest.";
        var tamer = Card(Uid("B72-T66-"), text, CardType.Tamer, color: CardColor.Purple);
        AllSupported(tamer);
        Assert.EndsWith("자신의 덱 위에서부터 2장 오픈한다. 그중 「Vemmon」 전부를 그 디지몬 중 1마리의 진화원 맨 아래에 놓는다. 남은 카드는 파기한다.", Ko(text).Replace("「벰몬」", "「Vemmon」"));
        var (state, engine) = NewMainPhase();
        engine.AddTamer(state.Player, tamer);
        var vem = Digimon(Uid("B72-VEM-"), 3, name: "Vemmon", color: CardColor.Purple);
        var other = Digimon(Uid("B72-OTH-"), 3);
        state.Player.MainDeck.InsertRange(0, new[] { vem, other });
        var d = Put(state.Player, Digimon(Uid("B72-VT-"), 5, "[On Play] Text mentions [Vemmon].", color: CardColor.Purple));
        engine.FireForSide(EffectTrigger.AllyDigimonPlayed, state.Player, d);
        Assert.Contains(vem, d.EvolutionSources);
        Assert.Contains(other, state.Player.Trash);
    }

    // =====================================================================
    // 3. 공식 영어 카드 리스트의 조그레스 조건 (한글 〔조그레스〕를 읽지 못할 때)
    // =====================================================================

    [Fact]
    public void EnglishDna_FallbackFormats()
    {
        string a = Uid("B72-DNA1-"), b = Uid("B72-DNA2-"), c = Uid("B72-DNA3-"), d = Uid("B72-DNA4-");
        WithEnglish(new[]
        {
            new EnglishCardRule { CardId = a, Dna = { "[DNA Digivolve] Green Lv.6 + black Lv.6: Cost 0" } },
            new EnglishCardRule { CardId = b, Dna = { "[DNA Digivolve] 0 from red Lv.4 + yellow Lv.4" } },
            new EnglishCardRule { CardId = c, Dna = { "[DNA Digivolve] [B72Kimera] + [B72Machine]: Cost 2" } },
            new EnglishCardRule { CardId = d, Dna = { "[DNA Digivolve] Lv.6 w/[Greymon] in name+Lv.6 w/[Garurumon] in name: Cost 0" } },
        }, () =>
        {
            var ex = Digimon(a, 7, color: CardColor.Green);
            Assert.True(GoldfishEngine.HasDna(ex));
            Assert.Equal(0, GoldfishEngine.GetDnaCost(ex));
            var (state, engine) = NewMainPhase(memory: 3);
            var g = Put(state.Player, Digimon(Uid("B72-G6-"), 6, color: CardColor.Green));
            var k = Put(state.Player, Digimon(Uid("B72-K6-"), 6, color: CardColor.Black));
            state.Player.Hand.Add(ex);
            Assert.True(engine.DnaDigivolve(ex, g, k).CanExecute);
            Assert.Contains(state.Player.BattleArea, x => x.TopCard == ex);
            Assert.True(GoldfishEngine.HasDna(Digimon(b, 5, color: CardColor.Red)));
            Assert.Equal(2, GoldfishEngine.GetDnaCost(Digimon(c, 7)));
            var omni = Digimon(d, 7);
            var (s2, e2) = NewMainPhase();
            var wg = Put(s2.Player, Digimon(Uid("B72-WG-"), 6, name: "WarGreymon", color: CardColor.Red));
            var mg = Put(s2.Player, Digimon(Uid("B72-MG-"), 6, name: "MetalGarurumon", color: CardColor.Blue));
            var x6 = Put(s2.Player, Digimon(Uid("B72-X6-"), 6, name: "Machinedramon", color: CardColor.Black));
            s2.Player.Hand.Add(omni);
            Assert.False(e2.DnaDigivolve(omni, wg, x6).CanExecute);
            Assert.True(e2.DnaDigivolve(omni, wg, mg).CanExecute);
        });
    }

    // =====================================================================
    // 4. EX11-033 · EX11-042 공식 영어 문장 — 패 또는 이 디지몬의 링크 카드에서 등장
    // =====================================================================

    [Fact]
    public void EX11_042_OfficialText_PlayFromHandOrLinkCards()
    {
        string api = "[On Play] [When Digivolving] You may play 1 [Maquinamon] from your hand or this Digimon's digivolution cards to 1 of your Digimon without paying the cost.";
        var card = new CardData { CardId = "EX11-042", Name = "B72Ex42", Type = CardType.Digimon, Level = 5, Color = CardColor.Green, DP = 7000, EffectText = api };
        Assert.Equal("[On Play] [When Digivolving] You may play 1 [Maquinamon] from your hand or this Digimon's link cards without paying the cost.", SentenceParser.OfficialClause72(card, api));
        Assert.StartsWith("[When Moving]", SentenceParser.OfficialClause72(new CardData { CardId = "EX11-033", Name = "x" }, api));
        Assert.Equal(api, SentenceParser.OfficialClause72(new CardData { CardId = "B72-ELSE", Name = "x" }, api));   // 다른 카드는 그대로
        string official = SentenceParser.OfficialClause72(card, api);
        Assert.Equal("【등장 시】 【진화 시】 자신의 패 또는 이 디지몬의 링크 카드에서 「Maquinamon」 1장을 코스트를 지불하지 않고 등장시킬 수 있다.", Ko(official).Replace("「마키나몬」", "「Maquinamon」"));
        var host = Digimon(Uid("B72-HOST-"), 5, official, color: CardColor.Green);
        AllSupported(host);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, host);
        var linked = Digimon(Uid("B72-MQ-"), 3, name: "Maquinamon", color: CardColor.Green);
        d.LinkedCards.Add(linked);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, host);
        Assert.Contains(state.Player.BattleArea, x => x.TopCard == linked);
        Assert.DoesNotContain(linked, d.LinkedCards);
    }

    // =====================================================================
    // 5. 조용한 오판정 바로잡음: "When your Digimon are played or digivolve, if any of them have the [X] trait" — 그 디지몬으로 판단
    //    (전에는 사건 카드(TriggerCard)만 봐서 등장 · 진화 사건에서 언제나 거짓 — BT20-091 · EX11-054 · ST20-04 등 16장)
    // =====================================================================

    [Fact]
    public void AnyOfThemHaveTrait_PlayedEvent_RealPlay_EX11_054()
    {
        var tamer = Card(Uid("B72-T54-"), "[All Turns] When your Digimon are played or digivolve, if any of them have the [Reptile] or [Dragonkin] trait, by suspending this Tamer, ＜Draw 1＞ (Draw 1 card from your deck.)", CardType.Tamer, color: CardColor.Red);
        AllSupported(tamer);
        foreach (string trait in new[] { "Reptile", "Reptile Man" })
        {
            var (state, engine) = NewMainPhase();
            engine.AddTamer(state.Player, Card(Uid("B72-T54x-"), tamer.EffectText, CardType.Tamer, color: CardColor.Red));
            var rep = Digimon(Uid("B72-REP-"), 3, color: CardColor.Red, traits: trait);
            state.Player.Hand.Add(rep);
            Assert.True(engine.PlayCard(rep).CanExecute);
            Assert.Equal(trait == "Reptile" ? 1 : 0, state.Player.SuspendedTamers.Count);   // Reptile Man은 [Reptile] 특징이 아니다 (2-3-2-3)
        }
    }

    [Fact]
    public void EX11_006_LinkedWithMaquinamon_MayDigivolve()
    {
        string inh = "[When Attacking] [Once Per Turn] This Digimon linked with [Maquinamon] may digivolve into a Digimon card with [Maquinamon] in its text in the hand with the digivolution cost reduced by 2.";
        var egg = Digimon(Uid("B72-EGG-"), 2, inherited: inh, color: CardColor.Green);
        Assert.All(EffectSupport.For(egg).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));
        Assert.StartsWith("【어택 시】 [턴에 1회] 「Maquinamon」을 링크하고 있는 이 디지몬을", Ko(inh).Replace("「마키나몬」", "「Maquinamon」"));
        foreach (bool linked in new[] { false, true })
        {
            var (state, engine) = NewMainPhase(memory: 5);
            var d = Put(state.Player, Digimon(Uid("B72-ATK-"), 4, color: CardColor.Green), false, Digimon(Uid("B72-EGGx-"), 2, inherited: inh, color: CardColor.Green));
            if (linked) d.LinkedCards.Add(Digimon(Uid("B72-MQL-"), 3, name: "Maquinamon", color: CardColor.Green));
            var into = Digimon(Uid("B72-INTO-"), 5, "[On Play] Link [Maquinamon].", color: CardColor.Green, evoCost: 3);
            state.Player.Hand.Add(into);
            engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, null);
            Assert.Equal(linked, d.TopCard == into);
            if (linked) Assert.Equal(4, state.MemoryGauge);   // 진화 코스트 3 - 2
        }
    }
}
