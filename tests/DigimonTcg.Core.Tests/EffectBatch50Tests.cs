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
/// v0.50.0 미지원 문장 24차 묶음: "~ suspend, or effects trash cards from under your Tamers" 사건 · "· ~" 고르는 효과와 사건 시점 머리말 ·
/// 상대가 등장시키는 「석화」 토큰 · "can only attack suspended Digimon" · 색 / 명칭이 서로 다른 카드를 테이머 아래에 · P-024 (덱 아래로 · 진화원은 파기) ·
/// 이 디지몬의 【시큐리티】 효과 발휘 (EX13-036) · EX6-073 · EX6-015 · LM-020 【상대의 턴 개시 시】 · BT7-111 · BT14-048 · BT21-102
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch50Tests
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

    private static CardData Tamer(string id, string main = "", string? name = null, CardColor color = CardColor.Blue, params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name, 0, null, traits);

    private static CardData Option(string id, string main = "", int cost = 3, string? name = null, CardColor color = CardColor.Blue, params string[] traits) =>
        Card(id, main, CardType.Option, 0, cost, color, 0, name, 0, null, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B50-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B50-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B50-SEC" + i, 3, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B50-OSEC" + i, 3, color: CardColor.White));
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

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    // =====================================================================
    // 1. "~ suspend, or effects trash cards from under your Tamers, ~" (BT26-044 · BT26-049 · BT26-091 · ST24-11)
    // =====================================================================

    [Fact]
    public void SuspendOrUnderTamerTrash_Event_ST24_11()
    {
        string text = "[All Turns] [Once Per Turn] When any of your opponent's Digimon or Tamers suspend, or effects trash cards from under your Tamers, trash your opponent's top security card.";
        var mon = Digimon(Uid("B50-ST2411-"), 6, text);
        AllSupported(mon);
        var triggers = CardScripts.For(mon)!.Effects.Select(e => e.Trigger).ToHashSet();
        Assert.Contains(EffectTrigger.OpponentDigimonSuspended, triggers);
        Assert.Contains(EffectTrigger.OpponentTamerSuspended, triggers);
        Assert.Contains(EffectTrigger.UnderTamersTrashed, triggers);
        Assert.DoesNotContain("<null>", Ko(text));

        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, mon);
        engine.RunEffects(EffectTrigger.UnderTamersTrashed, state.Player, self, mon);
        Assert.True(4 == state.DummyOpponent.SecurityStack.Count, Logs(state));
        engine.RunEffects(EffectTrigger.UnderTamersTrashed, state.Player, self, mon);   // [턴에 1회]
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);

        foreach (var t in new[]
        {
            "[Your Turn] [Once Per Turn] When any of your opponent's Digimon or Tamers suspend, or effects trash cards from under your Tamers, this Digimon may digivolve into a [Vegetation], [Fairy] or [DATA SQUAD] trait Digimon card in the hand with the cost reduced by 1.",
            "[All Turns] [Once Per Turn] When any of your opponent's Digimon or Tamers suspend, or effects trash cards from under your Tamers, you may play or use 1 play cost or use cost 3 or lower [DATA SQUAD] trait card from your hand without paying the cost. For each suspended Digimon or Tamer, add 1 to the cost maximum.",
        })
            AllSupported(Digimon(Uid("B50-ROSE-"), 6, t));
        AllSupported(Tamer(Uid("B50-YOSHINO-"), "[Your Turn] When any of your opponent's Digimon or Tamers suspend, or effects trash cards from under this Tamer, by suspending this Tamer, 1 of your Digimon may digivolve into a [Vegetation], [Fairy] or [DATA SQUAD] trait Digimon card in the hand with the cost reduced by 1."));
    }

    [Fact]
    public void PlayOrUse_CostMaxPerSuspended_BT26_049()
    {
        var mon = Digimon(Uid("B50-BT26049-"), 6, "[All Turns] [Once Per Turn] When any of your opponent's Digimon or Tamers suspend, or effects trash cards from under your Tamers, you may play or use 1 play cost or use cost 3 or lower [DATA SQUAD] trait card from your hand without paying the cost. For each suspended Digimon or Tamer, add 1 to the cost maximum.");
        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, mon);
        var opp1 = Put(state.DummyOpponent, Digimon("B50-OPP1", 4)); opp1.IsSuspended = true;
        var opp2 = Put(state.DummyOpponent, Digimon("B50-OPP2", 4)); opp2.IsSuspended = true;
        var ds5 = Digimon("B50-DS5", 5, playCost: 5, traits: new[] { "DATA SQUAD" });
        var ds6 = Digimon("B50-DS6", 5, playCost: 6, traits: new[] { "DATA SQUAD" });
        state.Player.Hand.Add(ds6); state.Player.Hand.Add(ds5);
        engine.RunEffects(EffectTrigger.OpponentDigimonSuspended, state.Player, self, mon, other: opp1);
        Assert.True(state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, ds5)), Logs(state));   // 3 + 레스트 2마리 = 5 이하
        Assert.DoesNotContain(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, ds6));
    }

    // =====================================================================
    // 2. "· ~" 고르는 효과 + 사건 시점 머리말 (EX12-066 ~ EX12-068)
    // =====================================================================

    [Fact]
    public void EventChoice_MiddleDotOptions_EX12_066()
    {
        string text = "[Start of Your Turn] If you have 2 or less memory, set it to 3.\n[Your Turn] When one of your Digimon with [Gammamon] in its text or the [VB] trait attacks, by suspending this Tamer, activate 1 of the effects below:\n· It may digivolve into a level 6 or lower card with [Gammamon] in its text or the [VB] trait in the hand with the cost reduced by 1.\n· You may use 1 Option card with [Gammamon] in its text or the [VB] trait from your hand with the cost reduced by 2.";
        var hiro = Tamer(Uid("B50-HIRO-"), text, name: "Hiro");
        AllSupported(hiro);
        Assert.Contains(CardScripts.For(hiro)!.Effects, e => e.Trigger == EffectTrigger.AllyDigimonAttacks);
        Assert.Contains("이하의 효과에서 1개를 골라 발휘한다", Ko("[Your Turn] When one of your Digimon with [Gammamon] in its text or the [VB] trait attacks, by suspending this Tamer, activate 1 of the effects below:"));
        Assert.DoesNotContain("<null>", Ko("· You may use 1 Option card with [Gammamon] in its text or the [VB] trait from your hand with the cost reduced by 2."));
        // "you may activate 1 of the effects below." (BT13-059 — 사건 시점 머리말, 발휘하지 않을 수 있다)
        AllSupported(Digimon(Uid("B50-BT13059-"), 5, "[All Turns] [Once Per Turn] When an opponent's Digimon becomes suspended, you may activate 1 of the effects below.\n・ Suspend 1 of your opponent's Digimon.\n・ Unsuspend 1 of your Digimon."));
        Assert.EndsWith("이하의 효과에서 1개를 골라 발휘할 수 있다.", Ko("[All Turns] [Once Per Turn] When an opponent's Digimon becomes suspended, you may activate 1 of the effects below."));

        var (state, engine) = NewMainPhase(memory: 5);
        state.Player.Tamers.Add(hiro);
        var gam = Put(state.Player, Digimon("B50-GAMMA", 4, "[On Play] [Gammamon]", name: "Gammamon"));
        state.Player.Hand.Add(Digimon("B50-GAM5", 5, "[Gammamon]", traits: new[] { "VB" }, playCost: 6));
        state.Player.Hand.Add(Option("B50-VBOPT", "[Main] Gain 1 memory.", cost: 2, traits: new[] { "VB" }));
        engine.RunEffects(EffectTrigger.AllyDigimonAttacks, state.Player, null, hiro, other: gam);
        Assert.False(state.Player.HasActiveTamer(hiro), Logs(state));   // 비용: 이 테이머를 레스트
        Assert.True(gam.TopCard.CardId == "B50-GAM5" || state.Player.Trash.Any(x => x.CardId == "B50-VBOPT"), Logs(state));
    }

    // =====================================================================
    // 3. 상대가 등장시키는 「석화」 토큰 (BT21-029 · BT24-017 · EX11-012)
    // =====================================================================

    [Fact]
    public void Petrification_OpponentPlaysToken()
    {
        const string tok = "they play 1 [Petrification] Token. (Digimon/White/3000 DP/[Your Turn] This Digimon can't suspend. [On Deletion] Trash your top security card.)";
        var mon = Digimon(Uid("B50-MEDUSA-"), 6, "[All Turns] [Once Per Turn] When any of your opponent's Digimon are deleted or their security stack is removed from, " + tok);
        AllSupported(mon);
        var ko = Ko(mon.EffectText);
        Assert.Contains("상대는 「석화」", ko);
        Assert.Contains("이 디지몬은 레스트할 수 없다", ko);
        AllSupported(Digimon(Uid("B50-MEDUSA2-"), 6, "[When Digivolving] Delete 1 of your opponent's lowest DP Digimon. Then, by returning 2 cards from their trash to the bottom of the deck, they play 2 [Petrification] Tokens. (Digimon/White/3000 DP/[Your Turn] This Digimon can't suspend. [On Deletion] Trash your top security card.) After, this Digimon gets +2000 DP for each of your opponent's Digimon until their turn ends."));

        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, mon);
        engine.RunEffects(EffectTrigger.OpponentDigimonDeleted, state.Player, self, mon);
        var token = Assert.Single(state.DummyOpponent.BattleArea);
        Assert.Equal("Petrification Token", token.TopCard.Name);
        Assert.Equal(3000, token.TopCard.DP);
        Assert.Empty(state.Player.BattleArea.Where(d => d.TopCard.Name.Contains("Petrification")));
        // 토큰의 "[Your Turn] This Digimon can't suspend." — 토큰 주인(상대)의 턴에만
        var tokScript = CardScripts.For(token.TopCard)!;
        Assert.Contains(tokScript.Statics, s => s.Stat == StaticStat.Keyword && s.Keyword == Keyword.CannotSuspend);
        Assert.Contains(tokScript.Effects, e => e.Trigger == EffectTrigger.OnDeletion);
    }

    // =====================================================================
    // 4. "can only attack suspended Digimon" (EX11-011 · EX8-016)
    // =====================================================================

    [Fact]
    public void OnlyAttackSuspended_EX11_011()
    {
        string text = "[Opponent's Turn] While this Digimon is suspended, all of your opponent's Digimon can only attack suspended Digimon.";
        var mon = Digimon(Uid("B50-DINO-"), 6, text);
        AllSupported(mon);
        Assert.Contains(CardScripts.For(mon)!.Statics, s => s.Stat == StaticStat.EnemyKeyword && s.Keyword == Keyword.CannotAttackPlayers);
        Assert.Equal("【상대의 턴】 이 디지몬이 레스트 상태인 동안, 상대의 디지몬 전부는 레스트 상태의 디지몬에게만 어택할 수 있다.", Ko(text));
    }

    // =====================================================================
    // 5. 색 / 명칭이 서로 다른 카드를 테이머 아래에 (AD1-020 · AD1-023 · BT18-088 · BT18-095 · BT18-097)
    // =====================================================================

    [Fact]
    public void DifferentColors_UnderThisTamer_AD1_020()
    {
        var t = Tamer(Uid("B50-AD1020-"), "[Start of Your Main Phase] [On Play] You may place up to 2 [Hybrid] trait cards with different colors from your hand or trash under this Tamer. If this effect placed, ＜Draw 1＞ (Draw 1 card from your deck.) Then, if there are 4 or more [Hybrid] trait cards under this Tamer, gain 2 memory.");
        Assert.Contains(EffectSupport.For(t).Clauses, c => c.Text.Contains("different colors") && c.Support == ClauseSupport.Supported);
        var (state, engine) = NewMainPhase(memory: 3);
        state.Player.Tamers.Add(t);
        var red1 = Digimon("B50-HYR1", 5, color: CardColor.Red, traits: new[] { "Hybrid" });
        var red2 = Digimon("B50-HYR2", 5, color: CardColor.Red, traits: new[] { "Hybrid" });
        var blue = Digimon("B50-HYB", 5, color: CardColor.Blue, traits: new[] { "Hybrid" });
        state.Player.Trash.AddRange(new[] { red1, red2, blue });
        state.Player.UnderTamers.Add(Digimon("B50-HYU1", 5, traits: new[] { "Hybrid" }));
        state.Player.UnderTamers.Add(Digimon("B50-HYU2", 5, traits: new[] { "Hybrid" }));
        int hand0 = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, null, t);
        Assert.Equal(4, state.Player.UnderTamers.Count);
        Assert.Single(state.Player.UnderTamers, x => x.Color == CardColor.Red);   // 같은 색은 1장만
        Assert.Equal(hand0 + 1, state.Player.Hand.Count);                         // ＜드로우 1＞
        Assert.True(5 == state.MemoryGauge, Logs(state));                         // 하이브리드체 4장 → 메모리 +2
    }

    [Fact]
    public void DifferentNames_PlusOtherTamers_BT18_088_And_TamerDigivolve_BT18_095()
    {
        var t = Tamer(Uid("B50-BT18088-"), "[Start of Your Main Phase] You may place up to 1 [Hybrid] trait card with different names from your trash under this Tamer. For each of your other Tamers, add 2 to the maximum number this effect may place.");
        AllSupported(t);
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(t);
        state.Player.Tamers.Add(Tamer("B50-OTHER-T"));
        foreach (var n in new[] { "A", "A", "B", "C", "D" }) state.Player.Trash.Add(Digimon("B50-H" + n + Guid.NewGuid().ToString("N")[..4], 5, name: "Hy" + n, traits: new[] { "Hybrid" }));
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, null, t);
        Assert.Equal(3, state.Player.UnderTamers.Count);                                  // 1 + 다른 테이머 1명 × 2
        Assert.Equal(3, state.Player.UnderTamers.Select(x => x.Name).Distinct().Count());   // 명칭이 서로 다르다

        var opt = Option(Uid("B50-BT18095-"), "[Main] You may place up to 5 [Hybrid] trait cards with different names from your hand or trash under 1 of your Tamers. Then, 1 of your Tamers with 5 or more cards under it may digivolve into [EmperorGreymon] in the hand or trash, ignoring digivolution requirements and without paying the cost.");
        AllSupported(opt);
        var (s2, e2) = NewMainPhase(memory: 2);
        var takuya = Tamer("B50-TAKUYA", name: "Takuya", color: CardColor.Red);
        s2.Player.Tamers.Add(takuya);
        foreach (var n in new[] { "A", "B", "C", "D", "E" }) s2.Player.Trash.Add(Digimon("B50-H2" + n, 5, name: "Hz" + n, traits: new[] { "Hybrid" }));
        var emperor = Digimon("B50-EMP", 6, name: "EmperorGreymon", color: CardColor.Red, playCost: 12, traits: new[] { "Hybrid" });
        s2.Player.Hand.Add(emperor);
        e2.RunEffects(EffectTrigger.Main, s2.Player, null, opt);
        Assert.True(s2.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, emperor)), Logs(s2));
        Assert.DoesNotContain(takuya, s2.Player.Tamers);
        Assert.Equal(2, s2.MemoryGauge);   // 코스트 없이
    }

    // =====================================================================
    // 6. P-024 — 덱 아래로 (진화원은 파기)
    // =====================================================================

    [Fact]
    public void BottomDeck_SourcesTrashed_P024()
    {
        var opt = Option(Uid("B50-P024-"), "[Main] If you have [Tai Kamiya] in play, place 1 of your [Agumon] at the bottom of its owner's deck to ＜Draw 3＞ (Draw 3 cards from your deck.) Trash that Digimon's digivolution cards.");
        AllSupported(opt);
        Assert.Contains("진화원은 파기하는 것으로", Ko(opt.EffectText));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(Tamer("B50-TAI", name: "Tai Kamiya"));
        var egg = Card("B50-KOROMON", "", CardType.Digitama, 2);
        var agu = Put(state.Player, Digimon("B50-AGU", 3, name: "Agumon"), egg);
        int deck0 = state.Player.MainDeck.Count, hand0 = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        Assert.DoesNotContain(agu, state.Player.BattleArea);
        Assert.Equal(hand0 + 3, state.Player.Hand.Count);
        Assert.Equal(deck0 + 1 - 3, state.Player.MainDeck.Count);   // 맨 위 카드만 덱으로
        Assert.Contains(egg, state.Player.Trash);                     // 진화원은 파기

        // 원래 동작 ("returning this Digimon to the bottom of the deck" 비용 등)은 진화원째 덱으로
        var (s2, e2) = NewMainPhase();
        var d2 = Put(s2.Player, Digimon("B50-AGU2", 4), egg);
        e2.BottomDeckAsCost(s2.Player, d2);
        Assert.Contains(egg, s2.Player.MainDeck);
    }

    // =====================================================================
    // 7. 이 디지몬의 【시큐리티】 효과 (EX13-036) · EX6-073 · EX6-015 · LM-020
    // =====================================================================

    [Fact]
    public void ActivateOwnSecurityEffect_EX13_036()
    {
        var mon = Digimon(Uid("B50-KENTAUR-"), 6, "[Security] [On Play] 1 of your opponent's Digimon gets -7000 DP for the turn.\n[When Digivolving] By trashing the top security card of 1 player with the most security cards, you may activate 1 of this Digimon's [Security] effects.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        state.DummyOpponent.SecurityStack.Add(Digimon("B50-OSEC5", 3));
        var opp = Put(state.DummyOpponent, Digimon("B50-OPPBIG", 6, dp: 10000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, Put(state.Player, mon), mon);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);   // 시큐리티가 가장 많은 쪽(상대 6장)에서 1장 파기
        Assert.True(3000 == opp.GetCurrentDP(), Logs(state));
    }

    [Fact]
    public void Ogudomon_DeleteThenTrashMinusDeleted_EX6_073()
    {
        string text = "[When Attacking] By returning 7 cards with different names and the [Seven Great Demon Lords] trait from this Digimon's digivolution cards to the bottom of the deck, delete 7 of your opponent's Digimon or Tamers. Then, trash the top 7 cards of your opponent's security stack. For each card deleted by this effect, reduce the cards trashed by 1.";
        var mon = Digimon(Uid("B50-OGUDO-"), 7, text);
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var srcs = Enumerable.Range(0, 7).Select(i => Digimon("B50-SGDL" + i, 6, name: "Lord" + i, traits: new[] { "Seven Great Demon Lords" })).ToArray();
        var self = Put(state.Player, mon, srcs);
        Put(state.DummyOpponent, Digimon("B50-V1", 5)); Put(state.DummyOpponent, Digimon("B50-V2", 5));
        state.DummyOpponent.Tamers.Add(Tamer("B50-OT"));
        int deck0 = state.Player.MainDeck.Count;
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, self, mon);
        Assert.Equal(deck0 + 7, state.Player.MainDeck.Count);
        Assert.Empty(state.DummyOpponent.BattleArea);
        Assert.Empty(state.DummyOpponent.Tamers);
        Assert.True(1 == state.DummyOpponent.SecurityStack.Count, Logs(state));   // 5 - (7 - 3)
    }

    [Fact]
    public void Xiangpengmon_PlaceOthersThenReturn_EX6_015()
    {
        var mon = Digimon(Uid("B50-XIANG-"), 6, "[On Play] [When Digivolving] You may place up to 3 of your other blue Digimon as this Digimon's bottom digivolution cards. Then, return all other level 4 or lower Digimon to the hand. For each card placed in this Digimon's digivolution cards, add 1 to the level this effect may return.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, mon);
        Put(state.Player, Digimon("B50-MYBLUE", 3));
        var opp5 = Put(state.DummyOpponent, Digimon("B50-OPP5", 5, color: CardColor.Red));
        var opp6 = Put(state.DummyOpponent, Digimon("B50-OPP6", 6, color: CardColor.Red));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, mon);
        Assert.Single(self.EvolutionSources);                         // 블루 Lv.3을 놓았다
        Assert.DoesNotContain(opp5, state.DummyOpponent.BattleArea);   // Lv.4 + 1 = Lv.5 이하
        Assert.Contains(opp6, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Quantumon_StartOfOpponentsTurn_DeclareCategory_LM_020()
    {
        var mon = Digimon(Uid("B50-QUANT-"), 6, "[Start of Opponent's Turn] Declare 1 card category. Then, reveal the top card of your opponent's deck. If it's of that category, this Digimon isn't affected by that category's effects, other than this Digimon's effects, for the turn. Return the revealed cards to the top or bottom of your opponent's deck.");
        AllSupported(mon);
        Assert.Contains(CardScripts.For(mon)!.Effects, e => e.Trigger == EffectTrigger.StartOfOpponentsTurn50);
        Assert.StartsWith("【상대의 턴 개시 시】", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, mon);
        engine.RunEffects(EffectTrigger.StartOfOpponentsTurn50, state.Player, self, mon);   // 추천: 디지몬 (상대의 덱 위는 디지몬)
        Assert.Equal(CardType.Digimon, self.UnaffectedCategory50);
        Assert.Equal(state.TurnSequence, self.UnaffectedCategory50Until);
    }

    // =====================================================================
    // 8. 그 밖의 묶음 (BT7-111 · BT14-048 · BT21-102)
    // =====================================================================

    [Fact]
    public void Extras_BT7_111_BT14_048_BT21_102()
    {
        var luce = Digimon(Uid("B50-LUCE-"), 7, "[On Play] [When Digivolving] Delete 1 of your opponent's Tamers or level 6 or lower Digimon.");
        AllSupported(luce);
        var (state, engine) = NewMainPhase();
        var o5 = Put(state.DummyOpponent, Digimon("B50-O5", 5));
        var o7 = Put(state.DummyOpponent, Digimon("B50-O7", 7));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, luce), luce);
        Assert.DoesNotContain(o5, state.DummyOpponent.BattleArea);
        Assert.Contains(o7, state.DummyOpponent.BattleArea);

        var leo = Digimon(Uid("B50-LEO-"), 5, "[When Attacking] If attacking a Digimon with DP higher than this Digimon, this Digimon may digivolve into 1 level 6 Digimon card with [Leomon] in its name in your hand for a cost of 6, ignoring its digivolution requirements.", dp: 6000);
        AllSupported(leo);
        var (s2, e2) = NewMainPhase(memory: 8);
        var self = Put(s2.Player, leo);
        var big = Put(s2.DummyOpponent, Digimon("B50-BIG", 6, dp: 9000));
        var into = Digimon("B50-LEO6", 6, name: "SaberLeomon", dp: 12000);
        s2.Player.Hand.Add(into);
        e2.RunEffects(EffectTrigger.WhenAttacking, s2.Player, self, leo, other: big);
        Assert.True(ReferenceEquals(self.TopCard, into), Logs(s2));

        var tamer = Tamer(Uid("B50-BT21102-"), "[Main] [Once Per Turn] You may play 1 [ADVENTURE] or [Hero] trait card with a play cost of 2 or less from your hand without paying the cost. For each of your Tamers' colors, add 1 to this effect's play cost maximum. Then, return this Tamer to the bottom of the deck.");
        AllSupported(tamer);
        var (s3, e3) = NewMainPhase();
        s3.Player.Tamers.Add(tamer);
        int deck0 = s3.Player.MainDeck.Count;
        e3.RunEffects(EffectTrigger.ActivateMain, s3.Player, null, tamer);   // 테이머의 [메인] = 기동 효과
        Assert.DoesNotContain(tamer, s3.Player.Tamers);
        Assert.Equal(tamer, s3.Player.MainDeck[^1]);
        Assert.Equal(deck0 + 1, s3.Player.MainDeck.Count);
    }

    // =====================================================================
    // 9. 원래 지원되던 문장은 그대로
    // =====================================================================

    [Fact]
    public void PreviouslySupported_Unchanged()
    {
        Assert.Equal("【자신의 턴】 이 디지몬은 어택과 블록을 할 수 없다.", Ko("[Your Turn] This Digimon can't attack or block."));
        var mon = Digimon(Uid("B50-OLD-"), 5, "[All Turns] When any of your opponent's Digimon or Tamers suspend, ＜Draw 1＞.");
        AllSupported(mon);
        var old = Digimon(Uid("B50-OLD2-"), 5, "[On Play] Trash the top card of your opponent's security stack.");
        AllSupported(old);
    }
}
