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
/// v0.56.0 미지원 문장 30차 묶음: 다른 효과의 처리 일부로 처리한 효과(효과로 사용한 옵션의 【메인】 · 효과로 발휘한 효과)의 유발은 바깥 효과가 끝난 뒤 (종합 룰 15-8-3-2 · 15-4-3-2) ·
/// 얻은 진화원 카드의 효과 · 링크 효과도 카드마다 (4-3-3 · 4-3-5 · 15-14-1-3) · 그 밖의 문장
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch56Tests
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

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, string inherited = "", int playCost = 3, int evoCost = 1, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, evoCost, inherited, traits);

    private static CardData Tamer(string id, string main = "", string? name = null, CardColor color = CardColor.Blue, int playCost = 2) =>
        Card(id, main, CardType.Tamer, 0, playCost, color, 0, name);

    /// <summary>같은 카드 번호의 다른 카드 (덱의 2번째 장)</summary>
    private static CardData Copy(CardData c) =>
        Card(c.CardId, c.EffectText, c.Type, c.Level, c.PlayCost, c.Color, c.DP, c.Name, c.EvoCost, c.InheritedEffectText, c.Traits.ToArray());

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B56-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B56-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B56-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B56-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    // =====================================================================
    // 1. 효과로 사용한 옵션의 【메인】에서 유발한 효과는 바깥 효과가 끝난 뒤 (종합 룰 15-8-3-2 · 15-4-3-2 · 공식 Q&A EX8-037 Q1)
    // =====================================================================

    private static (GameState State, GoldfishEngine Engine) OptionPlaysDigimonInsideEffect()
    {
        var drawer = Digimon(Uid("B56-DRAWER-"), 3, "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)", name: "B56 Drawer");
        var opt = Card(Uid("B56-OPT-"), "[Main] You may play 1 [B56 Drawer] from your hand without paying the cost.", CardType.Option, 0, 1);
        var user = Digimon(Uid("B56-USER-"), 5, "[On Play] You may use 1 Option card from your hand without paying the cost. Then, gain 1 memory.");
        AllSupported(drawer);
        AllSupported(opt);
        AllSupported(user);
        var (state, engine) = NewMainPhase(memory: 0);
        state.Player.Hand.Add(opt);
        state.Player.Hand.Add(drawer);
        var me = Put(state.Player, user);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, user);
        return (state, engine);
    }

    [Fact]
    public void OptionUsedByEffect_OnPlayOfDigimonItPlayed_WaitsForOuterEffect()
    {
        var (state, _) = OptionPlaysDigimonInsideEffect();
        Assert.True(state.MemoryGauge == 1, Logs(state));
        Assert.Equal(2, state.Player.BattleArea.Count);
        int memoryLog = state.ActionLogs.FindLastIndex(l => l.Contains("메모리"));
        int drew = state.ActionLogs.FindLastIndex(l => l.Contains("드로우"));
        Assert.True(memoryLog >= 0 && drew > memoryLog, Logs(state));   // 전에는 옵션의 【메인】이 끝나자마자 【등장 시】 드로우 → 그 뒤에 메모리 +1
    }

    [Fact]
    public void OptionUsedByEffect_JoinDisabled_OldOrder()
    {
        GoldfishEngine.JoinOuterEffect56 = false;
        try
        {
            var (state, _) = OptionPlaysDigimonInsideEffect();
            int memoryLog = state.ActionLogs.FindLastIndex(l => l.Contains("메모리"));
            int drew = state.ActionLogs.FindLastIndex(l => l.Contains("드로우"));
            Assert.True(drew >= 0 && drew < memoryLog, Logs(state));
        }
        finally { GoldfishEngine.JoinOuterEffect56 = true; }
    }

    [Fact]
    public void OptionUsedFromHand_OnPlayStillAfterOptionMain()
    {
        var drawer = Digimon(Uid("B56-DRAWH-"), 3, "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)", name: "B56 DrawerH");
        var opt = Card(Uid("B56-OPTH-"), "[Main] You may play 1 [B56 DrawerH] from your hand without paying the cost. Then, gain 1 memory.", CardType.Option, 0, 0);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 0);
        state.Player.Hand.Add(opt);
        state.Player.Hand.Add(drawer);
        Put(state.Player, Digimon("B56-COLOR", 3));   // 색 조건
        var r = engine.PlayCard(opt);
        Assert.True(r.CanExecute, r.ToString());
        Assert.Equal(2, state.Player.BattleArea.Count);
        Assert.Single(state.Player.Hand);   // 효과 밖에서 쓴 옵션 — 그 【메인】이 끝난 뒤 【등장 시】 (전과 같다)
    }

    // =====================================================================
    // 2. 얻은 진화원 카드의 효과 · 링크 효과도 카드마다 (종합 룰 4-3-3 · 4-3-5 · 15-14-1-3)
    // =====================================================================

    [Fact]
    public void GainedSourceEffects_SameCardTwice_TriggerForEachCard()
    {
        var gx = Digimon(Uid("B56-GX-"), 4, "[When Attacking] Gain 1 memory.", name: "B56 Gx");
        var top = Digimon(Uid("B56-GTOP-"), 6, "[All Turns] This Digimon gains all effects of [B56 Gx] in this Digimon's digivolution cards.");
        AllSupported(top);
        var (state, engine) = NewMainPhase(memory: 0);
        var me = Put(state.Player, top, gx, Copy(gx));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.True(state.MemoryGauge == 2, Logs(state));   // 전에는 같은 카드 번호는 1장만 (+1)
    }

    [Fact]
    public void GainedSourceEffects_OncePerTurn_EachCardOnce()
    {
        var gx = Digimon(Uid("B56-GXO-"), 4, "[When Attacking] [Once Per Turn] ＜Draw 1＞ (Draw 1 card from your deck.)", name: "B56 Gxo");
        var top = Digimon(Uid("B56-GTOPO-"), 6, "[All Turns] This Digimon gains all effects of [B56 Gxo] in this Digimon's digivolution cards.");
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, top, gx, Copy(gx));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Equal(2, state.Player.Hand.Count);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Equal(2, state.Player.Hand.Count);
    }

    [Fact]
    public void GainedSourceEffects_PerCardDisabled_OneOnly()
    {
        CardIdentity55.PerCard = false;
        try
        {
            var gx = Digimon(Uid("B56-GXN-"), 4, "[When Attacking] Gain 1 memory.", name: "B56 Gxn");
            var top = Digimon(Uid("B56-GTOPN-"), 6, "[All Turns] This Digimon gains all effects of [B56 Gxn] in this Digimon's digivolution cards.");
            var (state, engine) = NewMainPhase(memory: 0);
            var me = Put(state.Player, top, gx, Copy(gx));
            engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
            Assert.Equal(1, state.MemoryGauge);
        }
        finally { CardIdentity55.PerCard = true; }
    }

    [Fact]
    public void LinkEffects_SameCardTwice_ForEachLinkCard()
    {
        var link = Digimon(Uid("B56-LINK-"), 4, inherited: "[When Attacking] Gain 1 memory.", traits: "Weather (App Name)");
        Assert.Contains(CardScripts.For(link)!.Effects, e => e.Trigger == EffectTrigger.WhenAttacking && e.Source == EffectSource.Link43);
        var (state, engine) = NewMainPhase(memory: 0);
        var host = Put(state.Player, Digimon("B56-LHOST", 5));
        host.LinkedCards.Add(link);
        host.LinkedCards.Add(Copy(link));   // 링크 상한을 늘리는 효과로 2장
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, host, host.TopCard);
        Assert.True(state.MemoryGauge == 2, Logs(state));
    }

    // =====================================================================
    // 3. 30차 문장 (같은 뜻 다른 표기)
    // =====================================================================

    [Fact]
    public void HandUseCostReduction_ForEachOpponentDigimon_BT8_097()
    {
        var opt = Card(Uid("B56-CB-"), "Reduce the memory cost of this card in your hand by 1 for each Digimon your opponent has in play.\n[Main] Delete all of your opponent's Digimon with 6000 DP or less.", CardType.Option, 0, 6);
        AllSupported(opt);
        var (state, _) = NewMainPhase();
        for (int i = 0; i < 3; i++) Put(state.DummyOpponent, Digimon("B56-O4-" + i, 4));
        Assert.Equal(3, CardScripts.EffectivePlayCost(state.Player, opt, state.DummyOpponent));
    }

    [Fact]
    public void HandUseCostReduction_ForEachYellowTamer_BT2_099()
    {
        var opt = Card(Uid("B56-GB-"), "Reduce the memory cost of this card in your hand by 1 for each yellow Tamer you have in play.\n[Main] 1 of your opponent's Digimon gets -12000 DP for the turn.", CardType.Option, 0, 9, CardColor.Yellow);
        AllSupported(opt);
        var (state, _) = NewMainPhase();
        state.Player.Tamers.Add(Tamer("B56-TY1", color: CardColor.Yellow));
        state.Player.Tamers.Add(Tamer("B56-TY2", color: CardColor.Yellow));
        state.Player.Tamers.Add(Tamer("B56-TR", color: CardColor.Red));
        Assert.Equal(7, CardScripts.EffectivePlayCost(state.Player, opt, state.DummyOpponent));
    }

    [Fact]
    public void ReturnUpTo4_EachReturnedGivesAllOpponentDigimonMinus4000_EX5_074()
    {
        var me = Digimon(Uid("B56-HUANG-"), 7, "[On Play] [When Attacking] By returning up to 4 cards with the [Deva] or [Four Sovereigns] trait from your trash to the bottom of the deck, for each one, all your opponent's Digimon get -4000 DP for the turn.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        state.Player.Trash.Add(Digimon("B56-DEVA1", 4, traits: "Deva"));
        state.Player.Trash.Add(Digimon("B56-DEVA2", 4, traits: "Deva"));
        var a = Put(state.DummyOpponent, Digimon("B56-OA", 6, dp: 12000));
        var b = Put(state.DummyOpponent, Digimon("B56-OB", 6, dp: 11000));
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Equal(4000, a.GetCurrentDP());
        Assert.Equal(3000, b.GetCurrentDP());
        Assert.Empty(state.Player.Trash);
    }

    [Fact]
    public void TrashUpTo3_EachTrashedGivesMinus2000_EX12_030()
    {
        var me = Digimon(Uid("B56-THET-"), 5, "[On Play] [When Digivolving] You may trash up to 3 cards from your hand. To 1 of your opponent's Digimon, give -2000 DP for the turn for each card this effect trashed. Then, return 1 of their Digimon with 5000 DP or less to the bottom of the deck.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        for (int i = 0; i < 6; i++) state.Player.Hand.Add(Digimon("B56-H" + i, 3));
        var a = Put(state.DummyOpponent, Digimon("B56-OT", 6, dp: 12000));
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Equal(3, state.Player.Trash.Count);
        Assert.Equal(6000, a.GetCurrentDP());
    }

    [Fact]
    public void ForEvery10Trash_TrashOpponentDeck_ThenPlus3000Once_BT8_111()
    {
        var me = Digimon(Uid("B56-BEEL-"), 6, "[When Attacking] [Once Per Turn] For every 10 cards in your trash, trash the top 3 cards of your opponent's deck, and this Digimon gets +3000 DP for the turn.", dp: 11000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        for (int i = 0; i < 20; i++) state.Player.Trash.Add(Digimon("B56-T" + i, 3));
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, me);
        Assert.Equal(6, state.DummyOpponent.Trash.Count);
        Assert.Equal(14000, d.GetCurrentDP());
    }

    [Theory]
    [InlineData(4, 1, 0)]
    [InlineData(3, 0, 1)]
    public void OpponentPlays_MemoryIfLevel4OrHigher_DrawIfLevel3_RB1_035(int level, int memory, int draws)
    {
        var tamer = Tamer(Uid("B56-HOKUTO-"), "[All Turns] When an opponent plays a Digimon, by suspending this Tamer, gain 1 memory if that Digimon is level 4 or higher, and ＜Draw 1＞ if it is level 3.");
        AllSupported(tamer);
        var (state, engine) = NewMainPhase(memory: 3);
        state.Player.Tamers.Add(tamer);
        var o = Put(state.DummyOpponent, Digimon("B56-OPL", level));
        engine.FireForSide(EffectTrigger.OpponentDigimonPlayed, state.Player, o);
        Assert.Equal(3 + memory, state.MemoryGauge);
        Assert.Equal(draws, state.Player.Hand.Count);
        Assert.Contains(tamer, state.Player.SuspendedTamers);
    }

    [Fact]
    public void TwoEffectsOnOneLine_BothRegistered_RB1_10()
    {
        var me = Digimon(Uid("B56-SIRIUS-"), 6, "[When Digivolving] By placing 1 Digimon card with [Gammamon] in text from your hand as this Digimon's bottom digivolution card, delete 1 of your opponent's Digimon with DP less than or equal to this Digimon's DP. [Your Turn] (Once Per Turn) When an opponent’s Digimon is deleted, unsuspend this Digimon.");
        AllSupported(me);
        var effects = CardScripts.For(me)!.Effects;
        Assert.Contains(effects, e => e.Trigger == EffectTrigger.WhenDigivolving);
        Assert.Contains(effects, e => e.Trigger != EffectTrigger.WhenDigivolving && e.OncePerTurn);
        Assert.DoesNotContain("<null>", Ko(me.EffectText));
    }

    [Fact]
    public void GainsSourceColors_AndDpForEachColor_CountsGainedColors_EX13_077()
    {
        var me = Digimon(Uid("B56-OMNI-"), 7, "[All Turns] This Digimon gains all colors in its digivolution cards, and +1000 DP for each of your Digimon and Tamers' colors.", color: CardColor.White, dp: 16000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me, Digimon("B56-SRED", 5, color: CardColor.Red), Digimon("B56-SGRN", 4, color: CardColor.Green));
        Put(state.Player, Digimon("B56-YEL", 4, color: CardColor.Yellow));
        state.Player.Tamers.Add(Tamer("B56-TPUR", color: CardColor.Purple));
        engine.RecalculateFieldBuffs();
        Assert.Equal(21000, d.GetCurrentDP());   // 흰 · 빨강 · 초록(얻은 색) · 노랑 · 보라
    }

    [Fact]
    public void Korean_SameWithAndWithoutMemo_Batch56()
    {
        var clauses = new[]
        {
            "Reduce the memory cost of this card in your hand by 1 for each Digimon your opponent has in play.",
            "[On Play] [When Attacking] By returning up to 4 cards with the [Deva] or [Four Sovereigns] trait from your trash to the bottom of the deck, for each one, all your opponent's Digimon get -4000 DP for the turn.",
            "[All Turns] When an opponent plays a Digimon, by suspending this Tamer, gain 1 memory if that Digimon is level 4 or higher, and ＜Draw 1＞ if it is level 3.",
            "[All Turns] This Digimon gains all colors in its digivolution cards, and +1000 DP for each of your Digimon and Tamers' colors.",
            "[When Attacking] [Once Per Turn] For every 10 cards in your trash, trash the top 3 cards of your opponent's deck, and this Digimon gets +3000 DP for the turn.",
        };
        var cached = clauses.Select(Ko).ToList();
        Assert.DoesNotContain("<null>", cached);
        Assert.Contains("Lv.3이라면", cached[2]);
        KoreanEffectRenderer.MemoEnabled52 = false;
        try { Assert.Equal(clauses.Select(Ko).ToList(), cached); }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }
}
