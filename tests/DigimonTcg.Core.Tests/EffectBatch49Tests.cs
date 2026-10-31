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
/// v0.49.0 미지원 문장 23차 묶음: 앱몬 등급 진화 조건 칸 (Stnd. · Sup. · Ult. · 神) · 시큐리티 장수마다 바뀌는 〔진화〕 코스트 (BT24-101) ·
/// "play or use" · 양쪽 트래시를 덱 아래로 · 비교 대상 · 곳이 다른 고르기 · 색이 다른 카드 · 진화한 디지몬의 이름 · 시큐리티 · 진화원 조작
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch49Tests
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

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int playCost = 3, int dp = -1, CardColor? second = null, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, 1, second, traits);

    private static CardData Option(string id, string main = "", int cost = 3, string? name = null, CardColor color = CardColor.Blue, params string[] traits) =>
        Card(id, main, CardType.Option, 0, cost, color, 0, name, 0, null, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B49-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B49-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B49-SEC" + i, 3, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B49-OSEC" + i, 3, color: CardColor.White));
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

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    private static void WithEnglish(IEnumerable<EnglishCardRule> rules, Action run)
    {
        EnglishCardRules.Use(rules);
        EvoRequirements48.ClearCache();
        try { run(); }
        finally { EnglishCardRules.Reload(); EvoRequirements48.ClearCache(); }
    }

    // =====================================================================
    // 1. 앱몬 등급 진화 조건 칸 · BT24-101
    // =====================================================================

    [Fact]
    public void AppmonForm_EvolvesFromThatForm_AnyColorAnyLevel()
    {
        string id = Uid("B49-DOGATCH-");
        var into = Digimon(id, 4, color: CardColor.Red, traits: new[] { "Sup.", "Appmon" });
        var rules = new[] { new EnglishCardRule { CardId = id, Evo = { new EnglishEvoCost { Level = 3, Cost = 3, Colors = { "red", "green" }, From = "Lv.3" }, new EnglishEvoCost { Level = 0, Cost = 2, Colors = { "all" }, From = "Stnd." } } } };
        WithEnglish(rules, () =>
        {
            var stnd = new CardInstance(Digimon("B49-STND", 3, color: CardColor.Black, traits: new[] { "Stnd.", "Appmon" }), 1);
            Assert.True(ActionValidator.ValidateEvolution(stnd, into).CanExecute);
            Assert.Equal(2, ActionValidator.EvolutionCost(stnd, into));                       // 등급 칸의 코스트
            var red3 = new CardInstance(Digimon("B49-RED3", 3, color: CardColor.Red), 1);
            Assert.Equal(3, ActionValidator.EvolutionCost(red3, into));                       // Lv. 칸은 그대로
            var sup = new CardInstance(Digimon("B49-SUP", 4, color: CardColor.Black, traits: new[] { "Sup." }), 1);
            Assert.False(ActionValidator.ValidateEvolution(sup, into).CanExecute);             // 다른 등급
            var black3 = new CardInstance(Digimon("B49-BLK3", 3, color: CardColor.Black), 1);
            Assert.False(ActionValidator.ValidateEvolution(black3, into).CanExecute);          // 등급 없는 블랙 Lv.3
            Assert.False(EvoRequirements48.ColorOnly(black3, into));                           // "Lv.을 무시" 진화도 등급 칸의 아무 색은 쓰지 않는다
        });
    }

    [Fact]
    public void AppmonForm_GodForm_FromGodTrait()
    {
        string id = Uid("B49-DANTE-");
        var into = Digimon(id, 7, color: CardColor.White);
        var rules = new[] { new EnglishCardRule { CardId = id, Evo = { new EnglishEvoCost { Level = 0, Cost = 4, Colors = { "all" }, From = "神" } } } };
        WithEnglish(rules, () =>
        {
            Assert.True(ActionValidator.ValidateEvolution(new CardInstance(Digimon("B49-GOD", 6, traits: new[] { "God" }), 1), into).CanExecute);
            Assert.False(ActionValidator.ValidateEvolution(new CardInstance(Digimon("B49-GODB", 6, traits: new[] { "God Beast" }), 1), into).CanExecute);
        });
    }

    [Fact]
    public void English_CostPerSecurityCard_BT24_101()
    {
        var p = KoreanEvolution47.ParseEnglish(new[] { "[Digivolve] Lv.5 w/[TS] trait: Cost 5", "[Digivolve] Lv.5 w/[Aegiochusmon] in name: Cost 1 for each of your security cards" });
        Assert.Empty(p.Unparsed);
        Assert.Equal(2, p.Conditions.Count);
        Assert.NotNull(p.Conditions[1].CostFn);

        string id = Uid("B49-JUPITER-");
        var into = Digimon(id, 6, color: CardColor.Yellow);
        var rules = new[] { new EnglishCardRule { CardId = id, Evo = { new EnglishEvoCost { Level = 5, Cost = 5, Colors = { "yellow" }, From = "Lv.5" } }, SpecialDigivolve = { "[Digivolve] Lv.5 w/[Aegiochusmon] in name: Cost 1 for each of your security cards" } } };
        WithEnglish(rules, () =>
        {
            var (state, engine) = NewMainPhase();
            var aeg = Put(state.Player, Digimon("B49-AEG", 5, name: "Aegiochusmon: Holy", color: CardColor.Green));
            engine.RecalculateFieldBuffs();
            Assert.Equal(5, KoreanEvolution47.Cost(into, aeg));                    // 시큐리티 5장
            state.Player.SecurityStack.RemoveRange(0, 3);
            Assert.Equal(2, KoreanEvolution47.Cost(into, aeg));                    // 2장
            Assert.True(ActionValidator.ValidateEvolution(aeg, into).CanExecute);
            Assert.Equal(2, ActionValidator.EvolutionCost(aeg, into));
        });
    }

    // =====================================================================
    // 2. play or use
    // =====================================================================

    [Fact]
    public void PlayOrUse_PlayOrUseCost_EX13_058_064_ST24_06()
    {
        foreach (var text in new[]
        {
            "[When Attacking] [On Deletion] You may play or use 1 card with [Knightmon] in its text and a play or use cost of 4 or less from your hand without paying the cost.",
            "[When Digivolving] You may play or use 1 play or use cost 8 or lower Digimon or Option card with [Knightmon] in their texts from your hand or trash without paying the cost.",
            "[On Play] 1 of your opponent's Digimon gets -5000 DP for the turn. Then, by trashing 2 bottom face-down cards from under any of your Tamers, you may play or use 1 [DATA SQUAD] trait card with a play or use cost of 5 or less from your hand without paying the cost.",
        })
            AllSupported(Digimon(Uid("B49-POU-"), 5, text));

        var (state, engine) = NewMainPhase();
        var mon = Digimon("B49-KNIGHTS", 5, "[When Digivolving] You may play or use 1 play or use cost 8 or lower Digimon or Option card with [Knightmon] in their texts from your hand or trash without paying the cost.");
        var big = Digimon("B49-KN9", 6, "[On Play] Knightmon [Knightmon]", playCost: 9);
        var opt = Option("B49-KNOPT", "[Main] Gain 1 memory.", cost: 7, name: "Knightmon Lance");
        state.Player.Trash.Add(big); state.Player.Trash.Add(opt);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, Put(state.Player, mon), mon);
        Assert.DoesNotContain(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, big));   // 코스트 9는 안 된다
        Assert.Contains(state.ActionLogs, l => l.Contains("Lance") && l.Contains("사용"));   // 옵션을 코스트 없이 사용 (사용한 옵션은 트래시로)
        Assert.True(mem + 1 == state.MemoryGauge, string.Join(" | ", state.ActionLogs));   // 옵션을 코스트 없이 사용 → 메모리 +1
    }

    [Fact]
    public void Reveal_PlayOrUse_LM_067_BT26_084()
    {
        var lm = Digimon("B49-LM67", 6, "[When Digivolving] [Counter] [Once Per Turn] Reveal the top 6 cards of your deck. You may play or use 1 play or use cost 6 or lower [Three Musketeers] text card among them without paying the cost. Return the rest to the bottom of the deck.");
        AllSupported(lm);
        var (state, engine) = NewMainPhase();
        var tm = Digimon("B49-TM", 4, "[On Play] [Three Musketeers]", playCost: 5);
        state.Player.MainDeck.Insert(0, tm);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, Put(state.Player, lm), lm);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, tm));
        Assert.Equal(10, state.Player.MainDeck.Count);   // 나머지 5장은 덱 아래로

        var sc = Digimon("B49-SC", 6, "[Your Turn] [Once Per Turn] When this Digimon gets linked, reveal the top 3 cards of your deck. You may play or use 1 [Seven Code] trait card among them with the cost reduced by 3. Return the rest to the top or bottom of the deck.");
        AllSupported(sc);
        Assert.Contains("등장시키거나 사용할 수 있다", Ko("[Main] Reveal the top 3 cards of your deck. You may play or use 1 [Seven Code] trait card among them with the cost reduced by 3. Return the rest to the bottom of the deck."));
        var (s2, e2) = NewMainPhase(memory: 5);
        var seven = Option("B49-7OPT", "[Main] ＜Draw 1＞", cost: 5, traits: new[] { "Seven Code" });
        s2.Player.MainDeck.Insert(0, seven);
        var opt = Digimon(Uid("B49-REVMON-"), 5, "[On Play] Reveal the top 3 cards of your deck. You may play or use 1 [Seven Code] trait card among them with the cost reduced by 3. Return the rest to the bottom of the deck.");
        AllSupported(opt);
        int hand0 = s2.Player.Hand.Count;
        e2.RunEffects(EffectTrigger.OnPlay, s2.Player, Put(s2.Player, opt), opt);
        Assert.True(3 == s2.MemoryGauge, string.Join(" | ", s2.ActionLogs));   // 5 - (5-3)
        Assert.Equal(hand0 + 1, s2.Player.Hand.Count);        // 사용한 옵션의 ＜드로우 1＞
    }

    // =====================================================================
    // 3. 양쪽 트래시를 덱 아래로
    // =====================================================================

    [Fact]
    public void TrashToDeck_YourOrOpponent_BT17_077()
    {
        var mon = Digimon("B49-ORDI", 7, "[On Play] [When Digivolving] Trash all digivolution cards of all of your opponent's Digimon. Then, return all cards from your or your opponent's trash to the bottom of the deck. If this effect returned a white level 7 card, gain 3 memory.", color: CardColor.White);
        AllSupported(mon);
        var (state, engine) = NewMainPhase(memory: 3);
        state.Player.Trash.Add(Digimon("B49-W7", 7, color: CardColor.White));
        state.Player.Trash.Add(Digimon("B49-T4", 4));
        state.DummyOpponent.Trash.Add(Digimon("B49-OT", 4));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, mon), mon);
        Assert.Empty(state.Player.Trash);                       // 자신의 트래시 (화이트 Lv.7이 있으므로 추천)
        Assert.Single(state.DummyOpponent.Trash);
        Assert.Equal(12, state.Player.MainDeck.Count);
        Assert.Equal(6, state.MemoryGauge);                     // +3
        Assert.Contains("자신 또는 상대의 트래시의 카드를 전부 덱 아래로 되돌린다", Ko(mon.EffectText));
    }

    [Fact]
    public void TrashToDeck_BothPlayers_BT7_015()
    {
        var mon = Digimon("B49-MUSK", 6, "[On Play] Return all cards with the [Three Musketeers] trait and all Option cards from both players' trashes to the bottom of their owners' decks. If 7 or more cards were returned using this effect, delete 1 of your opponent's Digimon with the [Three Musketeers] trait or 8000 DP or less.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var victim = Put(state.DummyOpponent, Digimon("B49-V", 5, dp: 7000));
        for (int i = 0; i < 3; i++) state.Player.Trash.Add(Option("B49-O" + i));
        for (int i = 0; i < 3; i++) state.DummyOpponent.Trash.Add(Digimon("B49-M" + i, 4, traits: new[] { "Three Musketeers" }));
        state.DummyOpponent.Trash.Add(Digimon("B49-NOT", 4));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, mon), mon);
        Assert.Contains(state.DummyOpponent.BattleArea, d => ReferenceEquals(d, victim));   // 6장 → 소멸 없음
        Assert.Single(state.DummyOpponent.Trash);
        for (int i = 0; i < 4; i++) state.Player.Trash.Add(Option("B49-P" + i));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, mon), mon);
        Assert.Contains(state.DummyOpponent.BattleArea, d => ReferenceEquals(d, victim));   // 4장
        for (int i = 0; i < 7; i++) state.Player.Trash.Add(Option("B49-Q" + i));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, mon), mon);
        Assert.DoesNotContain(state.DummyOpponent.BattleArea, d => ReferenceEquals(d, victim));   // 7장 → 소멸
    }

    // =====================================================================
    // 4. 비교 대상 · 고르기
    // =====================================================================

    [Fact]
    public void SameLevelAsPlayed_EX3_023_AndSuspendedDp_BT16_048()
    {
        var src = Digimon("B49-EX3023", 4, "");
        src.InheritedEffectText = "[All Turns] [Once Per Turn] When you play a Digimon from digivolution cards, you may return 1 of your opponent's Digimon of the same level to the bottom of its owner's deck.";
        Assert.All(EffectSupport.For(src).Clauses, c => Assert.Equal(ClauseSupport.Supported, c.Support));

        var mon = Digimon("B49-BT16048", 5, "[End of Your Turn] [Once Per Turn] By suspending 1 of your other Digimon, return 1 of your opponent's Digimon with as much or less DP as the Digimon this effect suspended to the bottom of the deck.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, mon);
        var other = Put(state.Player, Digimon("B49-OTHER", 5, dp: 8000));
        var small = Put(state.DummyOpponent, Digimon("B49-SMALL", 5, dp: 6000));
        var huge = Put(state.DummyOpponent, Digimon("B49-HUGE", 6, dp: 12000));
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, self, mon);
        Assert.True(other.IsSuspended);
        Assert.DoesNotContain(state.DummyOpponent.BattleArea, d => ReferenceEquals(d, small));
        Assert.Contains(state.DummyOpponent.BattleArea, d => ReferenceEquals(d, huge));
        Assert.Contains("레스트", Ko(mon.EffectText));
    }

    [Fact]
    public void TrashOrBreedingSources_BT13_019()
    {
        var mon = Digimon("B49-SISTER", 4, "[On Play] [When Digivolving] You may play 1 Digimon card with [Sistermon] in its name from your trash or 1 Digimon card with the [Royal Knight] trait from the digivolution cards of your Digimon in the breeding area without paying its cost. You can't play [Gankoomon] or [Omnimon] with this effect.");
        AllSupported(mon);
        Assert.Contains("등장시킬 수 없다", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var gank = Digimon("B49-GANK", 6, name: "Gankoomon", traits: new[] { "Royal Knight" });
        var rk = Digimon("B49-RK", 6, name: "Dynasmon", traits: new[] { "Royal Knight" });
        var breed = new CardInstance(Digimon("B49-BREED", 4), 1);
        breed.AddBottomSource(gank); breed.AddBottomSource(rk);
        state.Player.BreedingArea = breed;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, mon), mon);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, rk));
        Assert.DoesNotContain(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, gank));   // 강고몬은 안 된다
        Assert.Contains(gank, breed.EvolutionSources);
    }

    [Fact]
    public void DifferentColors_EX7_037()
    {
        var mon = Digimon("B49-NSP", 6, "[When Digivolving] You may play 1 Digimon card with the [NSp] trait and a play cost of 7 or less from your hand without paying the cost. If DNA digivolving, you may play 2 Digimon cards with different color and the [NSp] trait and a play cost of 7 or less from your hand without paying the cost instead.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var r1 = Digimon("B49-R1", 4, color: CardColor.Red, traits: new[] { "NSp" });
        var r2 = Digimon("B49-R2", 4, color: CardColor.Red, traits: new[] { "NSp" });
        var g1 = Digimon("B49-G1", 3, color: CardColor.Green, traits: new[] { "NSp" });
        state.Player.Hand.AddRange(new[] { r1, r2, g1 });
        var act = typeof(SentenceParser).GetMethod("ActionExtra49", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var r = ((Action<EffectContext>? Act, string? Error)?)act.Invoke(null, new object[] { "play 2 Digimon cards with different color and the [NSp] trait and a play cost of 7 or less from your hand without paying the cost", mon });
        Assert.NotNull(r?.Act);
        var self = Put(state.Player, mon);
        var ctx = new EffectContext(engine, state.Player, self, null, null) { Card = mon };
        r!.Value.Act!(ctx);
        Assert.Equal(2, state.Player.BattleArea.Count(d => d.TopCard.Traits.Contains("NSp")));
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, g1));
        Assert.Single(state.Player.Hand, x => x.Color == CardColor.Red);   // 레드 2장은 함께 안 된다
    }

    [Fact]
    public void PlayByEvolvedName_EX13_067_EX4_061()
    {
        var and = Card("B49-TAMER67", "[Your Turn] When any of your Digimon digivolve, if you have 1 or fewer Digimon, by suspending this Tamer, you may play 1 [Gabumon] if that Digimon has [Greymon] in its name and 1 [Agumon] if it has [Garurumon] in its name from your hand or trash without paying the cost.", CardType.Tamer, 0, 3, CardColor.Red, 0);
        var or = Card("B49-TAMER61", "[Your Turn] [Once Per Turn] When one of your Digimon digivolves, if you have 1 or fewer Digimon, you may play 1 [Gabumon] if that Digimon has [Greymon] in its name or 1 [Agumon] if that Digimon has [Garurumon] in its name from your hand or trash without paying its cost.", CardType.Tamer, 0, 3, CardColor.Red, 0);
        AllSupported(and); AllSupported(or);
        Assert.Contains("「", Ko(and.EffectText));
        var (state, engine) = NewMainPhase();
        var omni = Put(state.Player, Digimon("B49-OMNI", 6, name: "Greymon Garurumon Fusion"));
        var gabu = Digimon("B49-GABU", 3, name: "Gabumon"); var agu = Digimon("B49-AGU", 3, name: "Agumon");
        state.Player.Hand.Add(gabu); state.Player.Trash.Add(agu);
        var act = typeof(SentenceParser).GetMethod("ActionExtra49", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var r = ((Action<EffectContext>? Act, string? Error)?)act.Invoke(null, new object[] { "play 1 [Gabumon] if that Digimon has [Greymon] in its name and 1 [Agumon] if it has [Garurumon] in its name from your hand or trash without paying the cost", and });
        var ctx = new EffectContext(engine, state.Player, null, omni, null) { Card = and };
        r!.Value.Act!(ctx);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, gabu));
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, agu));   // "and" — 둘 다

        var (s2, e2) = NewMainPhase();
        var grey = Put(s2.Player, Digimon("B49-GREY", 4, name: "MetalGreymon"));
        var gabu2 = Digimon("B49-GABU2", 3, name: "Gabumon"); var agu2 = Digimon("B49-AGU2", 3, name: "Agumon");
        s2.Player.Hand.Add(gabu2); s2.Player.Hand.Add(agu2);
        var r2 = ((Action<EffectContext>? Act, string? Error)?)act.Invoke(null, new object[] { "play 1 [Gabumon] if that Digimon has [Greymon] in its name or 1 [Agumon] if that Digimon has [Garurumon] in its name from your hand or trash without paying its cost", or });
        r2!.Value.Act!(new EffectContext(e2, s2.Player, null, grey, null) { Card = or });
        Assert.Contains(s2.Player.BattleArea, d => ReferenceEquals(d.TopCard, gabu2));
        Assert.DoesNotContain(s2.Player.BattleArea, d => ReferenceEquals(d.TopCard, agu2));   // 그레이몬 → 가부몬만
    }

    // =====================================================================
    // 5. 시큐리티
    // =====================================================================

    [Fact]
    public void Security_PlaceVariants()
    {
        foreach (var text in new[]
        {
            "[Hand] [Main] If you have a Digimon or Tamer with the [CS] trait, by paying 5 cost, give 3 of your opponent's Digimon ＜Security A. -1＞ (This Digimon checks 1 fewer security card.) until their turn ends. Then, place this card as the top security card.",
            "[End of Attack] You may play 1 card with [Greymon] in its name or the [Ver.1] trait and 1 card with [Garurumon] in its name or the [Ver.2] trait from this Digimon's digivolution cards without paying the costs. If this effect played, place this Digimon as your top security card.",
            "[On Play] [When Digivolving] You may place 1 [Witchelny] text card from your hand as the bottom security card. Then, by trashing your top security card, 1 of your Digimon may attack.",
            "[When Digivolving] [End of Attack] [Counter] [Once Per Turn] You may place 1 of each player's Digimon as the top security cards.",
            "[When Digivolving] Place all level 3 Digimon face down on top of their owners' security stacks in any order. Then, all of your opponent's level 4 or higher Digimon get -3000 DP and ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of their turn.",
            "[When Digivolving] [When Attacking] [Once Per Turn] By placing 1 [X Antibody] or level 6 card from this Digimon's digivolution cards at the bottom of its owner's deck, reveal all of your opponent's security cards, and trash 1 of them. Place the rest in your opponent's security stack face down. Then, your opponent shuffles their security stack.",
            "[Opponent's Turn] [Once Per Turn] When an opponent plays a Digimon, reveal 1 card of the same level in your hand, then place that card on top of your security stack face down.",
        })
        {
            var mon = Digimon(Uid("B49-SECV-"), 6, text);
            AllSupported(mon);
            Assert.DoesNotContain("<null>", Ko(text));
        }
        var opt = Card("B49-P023", "[Main] If you have [T.K. Takaishi] in play, place 1 of your [Patamon] at the bottom of your security stack face down. Trash that Digimon's digivolution cards.");
        AllSupported(opt);
    }

    [Fact]
    public void Security_EachPlayer_AllLevel3_Reveal()
    {
        var (state, engine) = NewMainPhase();
        var each = Digimon("B49-EACH", 6, "[When Digivolving] You may place 1 of each player's Digimon as the top security cards.");
        var self = Put(state.Player, each);
        var mine = Put(state.Player, Digimon("B49-MINE", 3, dp: 1000));
        var theirs = Put(state.DummyOpponent, Digimon("B49-THEIRS", 6, dp: 12000), Digimon("B49-THS", 5));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, self, each);
        Assert.Same(theirs.TopCard, state.DummyOpponent.SecurityStack[0]);
        Assert.Contains(state.DummyOpponent.Trash, x => x.CardId == "B49-THS");   // 겹쳐진 카드는 파기
        Assert.Same(mine.TopCard, state.Player.SecurityStack[0]);                // 자신은 약한 디지몬 추천

        var (s2, e2) = NewMainPhase();
        var rb = Digimon("B49-RB", 6, "[When Digivolving] Place all level 3 Digimon face down on top of their owners' security stacks in any order.");
        Put(s2.Player, Digimon("B49-A3", 3)); Put(s2.DummyOpponent, Digimon("B49-B3", 3)); var b4 = Put(s2.DummyOpponent, Digimon("B49-B4", 4));
        e2.RunEffects(EffectTrigger.WhenDigivolving, s2.Player, Put(s2.Player, rb), rb);
        Assert.Equal(6, s2.Player.SecurityStack.Count);
        Assert.Equal(6, s2.DummyOpponent.SecurityStack.Count);
        Assert.Contains(b4, s2.DummyOpponent.BattleArea);

        var (s3, e3) = NewMainPhase();
        var rv = Digimon("B49-RV", 6, "[When Digivolving] Reveal all of your opponent's security cards, and trash 1 of them. Place the rest in your opponent's security stack face down. Then, your opponent shuffles their security stack.");
        AllSupported(rv);
        var secOpt = Option("B49-SECOPT", "[Security] Delete 1 of your opponent's Digimon.");
        s3.DummyOpponent.SecurityStack.Insert(2, secOpt);
        e3.RunEffects(EffectTrigger.WhenDigivolving, s3.Player, Put(s3.Player, rv), rv);
        Assert.Equal(5, s3.DummyOpponent.SecurityStack.Count);
        Assert.Contains(secOpt, s3.DummyOpponent.Trash);   // 추천: 옵션
    }

    [Fact]
    public void Security_SameLevelFromHand_EX4_023()
    {
        var (state, engine) = NewMainPhase();
        var mon = Digimon("B49-EX4023", 4, "[Opponent's Turn] [Once Per Turn] When an opponent plays a Digimon, reveal 1 card of the same level in your hand, then place that card on top of your security stack face down.");
        var lv5 = Digimon("B49-HAND5", 5); var lv4 = Digimon("B49-HAND4", 4);
        state.Player.Hand.Add(lv4); state.Player.Hand.Add(lv5);
        var played = Put(state.DummyOpponent, Digimon("B49-OPP5", 5));
        var act = typeof(SentenceParser).GetMethod("ActionExtra49", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var r = ((Action<EffectContext>? Act, string? Error)?)act.Invoke(null, new object[] { "reveal 1 card of the same level in your hand, then place that card on top of your security stack face down", mon });
        r!.Value.Act!(new EffectContext(engine, state.Player, Put(state.Player, mon), played, null) { Card = mon });
        Assert.Same(lv5, state.Player.SecurityStack[0]);
        Assert.Contains(lv4, state.Player.Hand);
    }

    // =====================================================================
    // 6. 진화원
    // =====================================================================

    [Fact]
    public void Sources_DifferentNames_EX10_053_EX6_073()
    {
        var mon = Digimon("B49-GAMMA", 6, "[On Play] [When Digivolving] You may place up to 5 differently named Digimon cards with [Gammamon] in their names from your trash as this Digimon's bottom digivolution cards. Then, delete 1 of your opponent's Digimon with as much or less DP as this Digimon.");
        AllSupported(mon);
        var lords = Digimon("B49-LORDS", 6, "[When Digivolving] [When Attacking] You may place up to 7 cards with different names and the [Seven Great Demon Lords] trait from your trash as this Digimon's bottom digivolution cards. If you placed 4 or more cards with this effect, delete 1 of your opponent's Digimon or Tamers.");
        Assert.Equal(ClauseSupport.Supported, EffectSupport.For(lords).Clauses[0].Support);
        var (state, engine) = NewMainPhase();
        state.Player.Trash.Add(Digimon("B49-G1", 3, name: "Gammamon"));
        state.Player.Trash.Add(Digimon("B49-G1B", 3, name: "Gammamon"));
        state.Player.Trash.Add(Digimon("B49-G2", 4, name: "BetelGammamon"));
        state.Player.Trash.Add(Digimon("B49-X", 4, name: "Agumon"));
        var self = Put(state.Player, mon);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, mon);
        Assert.Equal(2, self.EvolutionSources.Count);   // 같은 이름은 1장만
        Assert.Equal(2, state.Player.Trash.Count);
    }

    [Fact]
    public void Sources_XAntibodyToDeckMemory_BT9_111_AndBottom3_EX6_061()
    {
        var mon = Digimon("B49-XAB", 7, "[End of Your Turn] [Once Per Turn] You may return up to 7 non-Digi-Egg cards with the [X Antibody] trait from this Digimon's digivolution cards to the bottom of your deck in any order, if you do, gain 1 memory for each card returned.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase(memory: 0);
        var self = Put(state.Player, mon, Digimon("B49-XA1", 6, traits: new[] { "X Antibody" }), Digimon("B49-XA2", 5, traits: new[] { "X Antibody" }), Digimon("B49-NX", 4));
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, self, mon);
        Assert.Equal(2, state.MemoryGauge);
        Assert.Single(self.EvolutionSources);

        var b3 = Digimon("B49-BOT3", 6, "[All Turns] [Once Per Turn] When an opponent's Digimon or one of your Digimon with the [Seven Great Demon Lords] trait is played, by trashing 1 card in your hand, return the bottom 3 digivolution cards of 1 of your opponent's Digimon to the bottom of the deck. Then, if your opponent has as many or fewer total Digimon and Tamers as you, delete 1 of your opponent's Digimon with no digivolution cards.");
        AllSupported(b3);
        var (s2, e2) = NewMainPhase();
        var opp = Put(s2.DummyOpponent, Digimon("B49-OP", 6), Digimon("B49-S1", 5), Digimon("B49-S2", 4), Digimon("B49-S3", 3));
        var act = typeof(SentenceParser).GetMethod("ActionExtra49", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var r = ((Action<EffectContext>? Act, string? Error)?)act.Invoke(null, new object[] { "return the bottom 3 digivolution cards of 1 of your opponent's Digimon to the bottom of the deck", b3 });
        int deck0 = s2.DummyOpponent.MainDeck.Count;
        r!.Value.Act!(new EffectContext(e2, s2.Player, Put(s2.Player, b3), null, null) { Card = b3 });
        Assert.Empty(opp.EvolutionSources);
        Assert.Equal(deck0 + 3, s2.DummyOpponent.MainDeck.Count);
        Assert.Equal("B49-S3", s2.DummyOpponent.MainDeck[deck0].CardId);   // 맨 아래 카드부터
    }

    [Fact]
    public void Sources_BottomOfEach_BT7_030_And_TwoCardsUnderTamer_BT11_031()
    {
        var bt7 = Digimon("B49-BT7030", 6, "[When Digivolving] For each card with the [Hybrid] trait in this Digimon's digivolution cards, trash 1 digivolution card from the bottom of each of your opponent's Digimon. Then, for each Digimon your opponent has with no digivolution cards, ＜Draw 2＞ (Draw 2 cards from your deck.)");
        AllSupported(bt7);
        var bt11 = Digimon("B49-BT11031", 5, "[On Deletion] ＜Save＞. Then place 1 blue [Greymon] and 1 blue [MailBirdramon] from your trash under 1 of your Tamers with the [General] trait.");
        AllSupported(bt11);
        Assert.DoesNotContain("<null>", Ko(bt11.EffectText));
    }

    // =====================================================================
    // 7. 원래 지원되던 문장은 그대로
    // =====================================================================

    [Fact]
    public void PreviouslySupported_Unchanged()
    {
        Assert.Equal("【메인】 메모리를 +1 한다. 그 후, 이 카드를 자신의 시큐리티 위에 뒷면으로 놓는다.", Ko("[Main] Gain 1 memory. Then, place this card on top of your security stack."));
        var mon = Digimon(Uid("B49-OLD-"), 5, "[On Play] You may play or use 1 card with [Knightmon] in its text and a play cost of 4 or less from your hand without paying the cost.");
        AllSupported(mon);
    }
}
