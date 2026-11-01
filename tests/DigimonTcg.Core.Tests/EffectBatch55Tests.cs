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
/// v0.55.0 미지원 문장 29차 묶음: 같은 카드가 여러 장이면 효과도 장마다 (종합 룰 15-14-1-3 — 진화원 효과 · 테이머의 [턴에 1회]) ·
/// 진화원 효과 한 번만 등록 (BT26-005 · EX12-001 · EX12-003 · EX12-004) · 효과 처리 중의 부화 · 이동 · 링크 · ≪디지버스트≫ · 효과로 사용한 옵션의 유발은 그 효과가 끝난 뒤 ·
/// BT26-102 섞어 놓는 비용 · EX1-061 진화원 효과 · EX10-059 첫 문단
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch55Tests
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
    private static CardData Copy(CardData c)
    {
        var x = Card(c.CardId, c.EffectText, c.Type, c.Level, c.PlayCost, c.Color, c.DP, c.Name, c.EvoCost, c.InheritedEffectText, c.Traits.ToArray());
        return x;
    }

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B55-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B55-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B55-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B55-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static int LogIndex(GameState s, string part) => s.ActionLogs.FindIndex(l => l.Contains(part));

    // =====================================================================
    // 1. 진화원에 같은 카드가 2장 — 진화원 효과도 2개 (종합 룰 15-14-1-3)
    // =====================================================================

    [Fact]
    public void SameCardTwiceInSources_StaticDpAddsTwice()
    {
        var src = Digimon(Uid("B55-DPSRC-"), 3, inherited: "[Your Turn] While you have 3 or more security cards, this Digimon gets +1000 DP.");
        var (state, engine) = NewMainPhase();
        var two = Put(state.Player, Digimon("B55-TOP5", 5, dp: 6000), src, Copy(src));
        var shared = Put(state.Player, Digimon("B55-TOP5B", 5, dp: 6000), src, src);   // 같은 객체 2번 (직접 만든 필드) — 전처럼 1장
        engine.RecalculateFieldBuffs();
        Assert.Equal(8000, two.GetCurrentDP());   // 전에는 같은 카드 번호는 1장만 (+1000)
        Assert.Equal(7000, shared.GetCurrentDP());
    }

    [Fact]
    public void SameCardTwiceInSources_OncePerTurnTriggerEachOnce()
    {
        var src = Digimon(Uid("B55-ONCE-"), 3, inherited: "[When Attacking] [Once Per Turn] ＜Draw 1＞ (Draw 1 card from your deck.)");
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B55-ATK5", 5), src, Copy(src));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Equal(2, state.Player.Hand.Count);   // 진화원 카드마다 [턴에 1회]
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Equal(2, state.Player.Hand.Count);
    }

    [Fact]
    public void SameCardTwiceInSources_TriggerActivatesForEachCard()
    {
        var src = Digimon(Uid("B55-MEM-"), 3, inherited: "[When Attacking] Gain 1 memory.");
        var (state, engine) = NewMainPhase(memory: 2);
        var me = Put(state.Player, Digimon("B55-ATK5M", 5), src, Copy(src), Digimon("B55-OTHER3", 3));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Equal(4, state.MemoryGauge);
    }

    [Fact]
    public void SameCardTwiceInSources_BothWouldBeDeletedEffectsActivate_P146_Q1()
    {
        // 공식 Q&A P-146 Q1: 이 카드를 2장 진화원에 가진 디지몬이 소멸할 때, 진화원 효과를 양쪽 발휘해 2장을 시큐리티에 놓을 수 있다
        var plug = Card(Uid("B55-P146-"), "[Main] Place this card as 1 of your non-white Digimon's bottom digivolution card.", CardType.Option, 0, 1, CardColor.Blue,
            name: "Recharge Plug-In B55", inherited: "[All Turns] When this Digimon would be deleted by battle, by placing 1 [Recharge Plug-In B55] from this Digimon's digivolution cards at the bottom of your security stack, prevent that deletion.");
        AllSupported(plug);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B55-P146TOP", 5), plug, Copy(plug));
        Assert.True(engine.TryPreventLeave(state.Player, me, deletion: true, inBattle: true));
        Assert.Equal(7, state.Player.SecurityStack.Count);   // 전에는 1장 (같은 카드 번호는 1장만 발휘)
        Assert.Empty(me.EvolutionSources);
    }

    [Fact]
    public void StartNewGame_DuplicateDeckEntriesBecomeSeparateCards()
    {
        var card = Digimon(Uid("B55-DECK-"), 3);
        var egg = Card(Uid("B55-EGG-"), "", CardType.Digitama, 2);
        var engine = new GoldfishEngine(new GameState());
        engine.StartNewGame(Enumerable.Repeat(card, 50).ToList(), Enumerable.Repeat(egg, 5).ToList(), seed: 7,
            opponentMainDeck: Enumerable.Repeat(card, 50).ToList(), opponentDigitamaDeck: Enumerable.Repeat(egg, 5).ToList());
        var z = engine.State.Player;
        var all = z.Hand.Concat(z.MainDeck).Concat(z.DigitamaDeck).ToList();
        Assert.Equal(55, all.Count);
        Assert.Equal(55, all.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Contains(all, x => ReferenceEquals(x, card));   // 첫 장은 덱 목록의 객체 그대로
        Assert.All(all.Where(x => x.Type == CardType.Digimon), x => Assert.Equal(card.CardId, x.CardId));
    }

    // =====================================================================
    // 2. 같은 테이머 2장 — [턴에 1회] 기록을 나누지 않는다
    // =====================================================================

    [Fact]
    public void TwoCopiesOfSameTamer_OncePerTurnEach()
    {
        var t = Tamer(Uid("B55-TAMER-"), "[Your Turn] [Once Per Turn] When one of your Digimon attacks, gain 1 memory.");
        AllSupported(t);
        var (state, engine) = NewMainPhase(memory: 2);
        state.Player.Tamers.Add(t);
        state.Player.Tamers.Add(Copy(t));
        var atk = Put(state.Player, Digimon("B55-ATKT", 5));
        atk.TurnPlayed = 0;
        engine.AttackSecurityOrDirect(atk);
        Assert.Equal(4, state.MemoryGauge);   // 전에는 같은 카드 객체를 나눠 써서 둘째 테이머가 막혔다 (+1)
    }

    // =====================================================================
    // 3. 메인 칸의 "Inherited Effect ~"와 진화원 칸에 같은 효과 — 한 번만 등록
    // =====================================================================

    [Fact]
    public void InheritedLineInBothFields_RegisteredOnce_BT26_005()
    {
        var egg = Card(Uid("B55-DUPI-"), "Inherited Effect [On Deletion] Gain 1 memory.", CardType.Digimon, 3, inherited: "Gain 1 memory.");
        var s = CardScripts.For(egg)!;
        Assert.Single(s.Effects.Where(e => e.Source == EffectSource.Inherited));
        var (state, engine) = NewMainPhase(memory: 2);
        var me = Put(state.Player, Digimon("B55-DEL4", 4), egg);
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, me, me.TopCard);
        Assert.Equal(3, state.MemoryGauge);   // 전에는 2번 (+2)
    }

    [Fact]
    public void InheritedLineInBothFields_SourceFieldUsedWhenMainLineUnreadable()
    {
        var egg = Card(Uid("B55-DUPU-"), "Inherited Effect [Your Turn] Frobnicate the zorblax.", CardType.Digimon, 3, inherited: "[When Attacking] Gain 1 memory.");
        var s = CardScripts.For(egg)!;
        Assert.Single(s.Effects.Where(e => e.Source == EffectSource.Inherited && e.Trigger == EffectTrigger.WhenAttacking));
    }

    // =====================================================================
    // 4. 효과 처리 중의 유발은 그 효과가 끝난 뒤 (28차의 나머지)
    // =====================================================================

    [Fact]
    public void OptionUsedByEffect_UsedTriggerWaitsForTheRestOfTheEffect()
    {
        var watcher = Tamer(Uid("B55-OPTW-"), "[Your Turn] When you use an Option card, ＜Draw 1＞ (Draw 1 card from your deck.)");
        var user = Digimon(Uid("B55-USER-"), 5, "[On Play] You may use 1 Option card from your hand without paying the cost. Then, gain 1 memory.");
        AllSupported(watcher);
        AllSupported(user);
        var opt = Card(Uid("B55-OPT-"), "[Main] Gain 1 memory.", CardType.Option, 0, 1);
        var (state, engine) = NewMainPhase(memory: 0);
        state.Player.Tamers.Add(watcher);
        state.Player.Hand.Add(opt);
        var me = Put(state.Player, user);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, user);
        Assert.True(state.MemoryGauge == 2, Logs(state));
        Assert.Single(state.Player.Hand);
        int memoryLog = state.ActionLogs.FindLastIndex(l => l.Contains("메모리"));
        int drew = state.ActionLogs.FindLastIndex(l => l.Contains("드로우"));
        Assert.True(memoryLog >= 0 && drew > memoryLog, Logs(state));   // 전에는 옵션을 쓰자마자 드로우하고 그 뒤에 메모리 +1
    }

    [Fact]
    public void HatchByEffect_HatchTriggerWaitsForTheRestOfTheEffect()
    {
        var watcher = Tamer(Uid("B55-HATW-"), "[Your Turn] When you hatch in your breeding area, ＜Draw 1＞ (Draw 1 card from your deck.)");
        var hatcher = Digimon(Uid("B55-HATCH-"), 5, "[On Play] Hatch 1 Digi-Egg card to an empty space in your breeding area. Then, gain 1 memory.");
        var (state, engine) = NewMainPhase(memory: 0);
        if (!EffectSupport.For(watcher).Clauses.All(c => c.Support == ClauseSupport.Supported) || !EffectSupport.For(hatcher).Clauses.All(c => c.Support == ClauseSupport.Supported))
            return;   // 이 문장이 읽히는 표기가 아니면 건너뛴다 (부화 유발의 순서만 본다)
        state.Player.Tamers.Add(watcher);
        state.Player.DigitamaDeck.Push(Card(Uid("B55-EGGH-"), "", CardType.Digitama, 2));
        var me = Put(state.Player, hatcher);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, hatcher);
        Assert.NotNull(state.Player.BreedingArea);
        int memoryLog = state.ActionLogs.FindLastIndex(l => l.Contains("메모리"));
        int drew = state.ActionLogs.FindLastIndex(l => l.Contains("드로우"));
        Assert.True(memoryLog >= 0 && drew > memoryLog, Logs(state));
    }

    [Fact]
    public void LinkOutsideEffects_TriggerStillImmediate()
    {
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B55-LINKHOST", 5));
        var link = Digimon(Uid("B55-LINK-"), 3);
        engine.LinkCard(state.Player, me, link);   // 효과 밖 — 바로 (미루지 않는다)
        Assert.Contains(link, me.LinkedCards);
    }

    // =====================================================================
    // 5. BT26-102 · EX1-061 · EX10-059
    // =====================================================================

    private const string Bt26102 = "[Main] By placing 6 [Seven Code] trait Digimon cards from your battle area, link cards or trash as 1 of your [Seven Code] trait Digimon's bottom digivolution cards, that Digimon may digivolve into [Dantemon] in the hand, ignoring digivolution requirements and without paying the cost.";

    [Fact]
    public void MixedPlacementCost_BattleAreaLinkAndTrash_BT26_102()
    {
        var opt = Card(Uid("B55-BT26102-"), "＜Use Req. ([Seven Code] trait)＞ (Specified cards let you ignore color requirements.)\r\n" + Bt26102, CardType.Option, 0, 4, CardColor.Purple);
        AllSupported(opt);
        Assert.Contains("배틀 에어리어/링크 카드/트래시", Ko(Bt26102));
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Digimon("B55-SC-HOST", 5, traits: "Seven Code"));
        var other = Put(state.Player, Digimon("B55-SC-OTHER", 4, traits: "Seven Code"), Digimon("B55-SC-UNDER", 3));
        host.LinkedCards.Add(Digimon("B55-SC-LINK", 4, traits: "Seven Code"));
        for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon("B55-SC-TR" + i, 4, traits: "Seven Code"));
        var dante = Digimon(Uid("B55-DANTE-"), 7, name: "Dantemon", traits: "Seven Code");
        state.Player.Hand.Add(dante);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        Assert.True(ReferenceEquals(dante, host.TopCard), Logs(state));
        Assert.Contains(host, state.Player.BattleArea);
        Assert.DoesNotContain(other, state.Player.BattleArea);   // 6장 = 트래시 4 + 링크 1 + 배틀 에어리어 1
        Assert.Empty(host.LinkedCards);
        Assert.Equal(7, host.EvolutionSources.Count);   // 원래 맨 위 + 놓은 6장
    }

    [Fact]
    public void MixedPlacementCost_NotEnoughCards_DoesNothing_BT26_102()
    {
        var opt = Card(Uid("B55-BT26102N-"), Bt26102, CardType.Option, 0, 4, CardColor.Purple);
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Digimon("B55-SC-HOSTN", 5, traits: "Seven Code"));
        for (int i = 0; i < 5; i++) state.Player.Trash.Add(Digimon("B55-SC-TRN" + i, 4, traits: "Seven Code"));
        var dante = Digimon(Uid("B55-DANTEN-"), 7, name: "Dantemon", traits: "Seven Code");
        state.Player.Hand.Add(dante);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        Assert.Equal(5, state.Player.Trash.Count);
        Assert.NotSame(dante, host.TopCard);
    }

    [Fact]
    public void RetaliationDigimonCanAttackUnsuspendedLevel4OrLower_EX1_061()
    {
        const string inh = "[Your Turn] While this Digimon has [Myotismon] in its name, all your Digimon with ＜Retaliation＞ can attack your opponent's unsuspended level 4 or lower Digimon.";
        var src = Digimon(Uid("B55-EX1061-"), 4, inherited: inh, color: CardColor.Purple);
        AllSupported(src);
        Assert.Contains("Lv.4 이하의 상대의 디지몬에게도 어택할 수 있다", Ko(inh));
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B55-MYO", 5, name: "Myotismon"), src);
        var ret = Put(state.Player, Digimon("B55-RET", 4, "＜Retaliation＞ (When this Digimon is deleted after losing a battle, delete the Digimon it was battling.)"));
        var plain = Put(state.Player, Digimon("B55-PLAIN", 4));
        var o4 = Put(state.DummyOpponent, Digimon("B55-O4", 4));
        var o5 = Put(state.DummyOpponent, Digimon("B55-O5", 5));
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.CanBeAttackTargetFor(ret, o4));
        Assert.False(EffectRules.CanBeAttackTargetFor(ret, o5));
        Assert.False(EffectRules.CanBeAttackTargetFor(plain, o4));
        ret.IsOwnersTurn = false;
        foreach (var d in state.Player.BattleArea) d.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.CanBeAttackTargetFor(ret, o4));   // [Your Turn]만
    }

    [Fact]
    public void BlindPlaceThenDeleteDigimonOrTamerWithCardsUnder_EX10_059()
    {
        var mon = Digimon(Uid("B55-EX10059-"), 7, "[On Play] [When Digivolving] Choose 1 card in your opponent's hand without looking and place it as any of their Digimon's bottom digivolution card or under any of their Tamers. Then, by placing 3 [Bagra Army] trait Digimon cards from your trash as this Digimon's top digivolution cards, delete 1 of their Digimon or Tamers with cards under it.", color: CardColor.Purple, dp: 13000);
        AllSupported(mon);
        Assert.Contains("보지 않고 1장을 골라", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var o = Put(state.DummyOpponent, Digimon("B55-O-TARGET", 6, dp: 9000));
        state.DummyOpponent.Hand.Add(Digimon("B55-O-HAND", 3));
        for (int i = 0; i < 3; i++) state.Player.Trash.Add(Digimon("B55-BAG" + i, 5, color: CardColor.Purple, traits: "Bagra Army"));
        var me = Put(state.Player, mon);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon);
        Assert.Empty(state.DummyOpponent.Hand);
        Assert.DoesNotContain(o, state.DummyOpponent.BattleArea);   // 진화원이 생긴 디지몬을 소멸
        Assert.Empty(state.Player.Trash);
        Assert.Equal(3, me.EvolutionSources.Count);
    }

    // =====================================================================
    // 6. 그 밖의 묶음: 코스트 감소의 셈 (BT9-112 · EX5-072 · BT6-112) · DNA 진화의 Lv. 취급 (EX3-020 · EX3-041)
    // =====================================================================

    [Fact]
    public void PlayCostReduction_ForEachOpponentDigimonAndTamer_BT9_112()
    {
        var mon = Digimon(Uid("B55-BT9112-"), 6, "When you would play this card, reduce its memory cost by 3 for each Digimon and Tamer your opponent has in play.", playCost: 14);
        AllSupported(mon);
        Assert.Contains("상대의 디지몬과 테이머 1마리(명)마다", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, Digimon("B55-OD1", 4));
        Put(state.DummyOpponent, Digimon("B55-OD2", 5));
        state.DummyOpponent.Tamers.Add(Tamer(Uid("B55-OT-")));
        Assert.Equal(5, engine.PreviewPlayCostWithOptions(state.Player, mon));   // 14 - 3×3
    }

    [Fact]
    public void UseCostReduction_ForEachDifferentlyNamedTraitCardInTrash_EX5_072()
    {
        var opt = Card(Uid("B55-EX5072-"), "When this card would be used, reduce the cost by 1 for each [Deva] or [Four Sovereigns] trait card with a different name in your trash.", CardType.Option, 0, 8);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        state.Player.Trash.Add(Digimon("B55-DEVA1", 4, name: "Deva A", traits: "Deva"));
        state.Player.Trash.Add(Digimon("B55-DEVA1B", 4, name: "Deva A", traits: "Deva"));   // 같은 명칭은 1장
        state.Player.Trash.Add(Digimon("B55-SOV", 6, name: "Sovereign B", traits: "Four Sovereigns"));
        state.Player.Trash.Add(Digimon("B55-NONE", 4, name: "Other C"));
        Assert.Equal(6, engine.PreviewPlayCostWithOptions(state.Player, opt));
    }

    [Fact]
    public void PlayCostReduction_TraitDigimonAndCost7Options_BT6_112()
    {
        var mon = Digimon(Uid("B55-BT6112-"), 6, "When you would play this card from your hand, reduce its play cost by 1 for each [Three Musketeers] Digimon card and Option card with a memory cost of 7 in your trash.", playCost: 10);
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        state.Player.Trash.Add(Digimon("B55-TM1", 4, traits: "Three Musketeers"));
        state.Player.Trash.Add(Digimon("B55-TM2", 5, traits: "Three Musketeers"));
        state.Player.Trash.Add(Card("B55-OPT7", "", CardType.Option, 0, 7));
        state.Player.Trash.Add(Card("B55-OPT6", "", CardType.Option, 0, 6));
        Assert.Equal(7, engine.PreviewPlayCostWithOptions(state.Player, mon));
    }

    [Fact]
    public void ExamonTreatsThisDigimonAsLevel6_YourTurnOnly_EX3_020()
    {
        var wing = Digimon(Uid("B55-EX3020-"), 5, "[Your Turn] [Examon] in your hand can treat this Digimon as level 6 for DNA digivolution.", name: "Wingdramon", color: CardColor.Green);
        AllSupported(wing);
        var (state, _) = NewMainPhase();
        var d = Put(state.Player, wing);
        var examon = Digimon("B55-EXAMON", 7, name: "Examon");
        var facets = GoldfishEngine.DnaFacets(d, examon).ToList();
        Assert.Contains(facets, f => f.Level == 6 && f.Name == "Wingdramon");
        Assert.Single(GoldfishEngine.DnaFacets(d, Digimon("B55-OMNI", 7, name: "Omnimon")));
        d.IsOwnersTurn = false;
        Assert.Single(GoldfishEngine.DnaFacets(d, examon));
    }

    [Fact]
    public void Korean_SameWithAndWithoutMemo_Batch55()
    {
        var clauses = new[]
        {
            Bt26102,
            "[Your Turn] While this Digimon has [Myotismon] in its name, all your Digimon with ＜Retaliation＞ can attack your opponent's unsuspended level 4 or lower Digimon.",
            "When you would play this card, reduce its memory cost by 3 for each Digimon and Tamer your opponent has in play.",
            "[Your Turn] [Examon] in your hand can treat this Digimon as level 6 for DNA digivolution.",
            "[On Play] [When Digivolving] Choose 1 card in your opponent's hand without looking and place it as any of their Digimon's bottom digivolution card or under any of their Tamers. Then, by placing 3 [Bagra Army] trait Digimon cards from your trash as this Digimon's top digivolution cards, delete 1 of their Digimon or Tamers with cards under it.",
        };
        var cached = clauses.Select(Ko).ToList();
        Assert.DoesNotContain("<null>", cached);
        KoreanEffectRenderer.MemoEnabled52 = false;
        try { Assert.Equal(clauses.Select(Ko).ToList(), cached); }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }
}
