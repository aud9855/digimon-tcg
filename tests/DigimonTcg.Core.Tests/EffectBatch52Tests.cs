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
/// v0.52.0 미지원 문장 26차 묶음: 규칙대로 고친 것 (BT12-100 이름으로 액티브로 한 디지몬의 어택 · 셈이 0인 DP 대상의 어택) ·
/// 한글 생성 캐시 (같은 입력 · 같은 상태 = 같은 결과) · ≪흡수진화≫ 대신 상대 디지몬 (BT3-056) · 쉼표로 이은 사건 (LM-023) ·
/// 진화할 때 (P-075 · EX2-056) · 상대 필드를 보는 색 조건 무시 (EX6-072 · BT19-089) · 비용 (BT23-089 · BT14-030 · BT18-102 · EX5-064) · 대상 (BT9-082 · ST20-04)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch52Tests
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

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, string inherited = "", params string[] traits) =>
        Card(id, main, CardType.Digimon, level, 3, color, dp, name, 1, inherited, traits);

    private static CardData Tamer(string id, string main = "", string? name = null, CardColor color = CardColor.Blue) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name, 0, "");

    private static CardData Option(string id, string main = "", int cost = 3, CardColor color = CardColor.Blue) =>
        Card(id, main, CardType.Option, 0, cost, color, 0, null, 0, "");

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B52-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B52-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B52-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B52-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    // 1. 규칙대로 고친 것 — 효과로 하는 어택
    // =====================================================================

    [Fact]
    public void UnsuspendNamedDigimon_ThenItMayAttackPlayer_BT12_100()
    {
        var opt = Option(Uid("B52-BT12100-"), "[Main] Delete 1 of your opponent's Digimon. Then, unsuspend 1 of your [Shoutmon X7: Superior Mode] cards, and it may attack a player.", 9, CardColor.Red);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(10);
        Put(state.DummyOpponent, Digimon("B52-OPP", 5));
        var x7 = Put(state.Player, Digimon(Uid("B52-X7-"), 7, name: "Shoutmon X7: Superior Mode", dp: 15000));
        x7.IsSuspended = true;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        engine.RunDeclaredEffectAttack57();   // v0.57.0 효과로 선언한 어택은 행동이 끝날 때 카운터 · 블록 · 배틀
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);   // 액티브로 한 그 디지몬으로 플레이어에게 어택 (전에는 앞의 소멸 대상을 가리켜 어택하지 않았다)
        Assert.True(x7.IsSuspended, Logs(state));
    }

    [Fact]
    public void ScaledDpOfZero_StillChoosesTarget_ThenItMayAttack_BT25_086()
    {
        var tamer = Tamer(Uid("B52-BT25086-"), "[End of Your Turn] By suspending this Tamer, 1 of your [TS] trait Digimon gains +1000 DP for the turn for each memory your opponent has. Then, it may attack.");
        AllSupported(tamer);
        var (state, engine) = NewMainPhase(0);   // 상대 메모리 0 → +0
        state.Player.Tamers.Add(tamer);
        var ts = Put(state.Player, Digimon(Uid("B52-TS-"), 5, traits: "TS"));
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, null, tamer);
        engine.RunDeclaredEffectAttack57();   // v0.57.0 효과로 선언한 어택은 행동이 끝날 때 카운터 · 블록 · 배틀
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.True(ts.IsSuspended, Logs(state));
    }

    // =====================================================================
    // 2. 한글 생성 캐시
    // =====================================================================

    [Fact]
    public void KoreanMemo_SameResultWithAndWithoutCache()
    {
        var clauses = new[]
        {
            "[When Digivolving] When DNA digivolving, delete 1 of your opponent's level 6 or higher Digimon and all of their level 5 or lower Digimon. For each Digimon deleted by this effect, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)",
            "[On Play] [When Digivolving] You may place 1 Digimon card with the [Blue Flare] trait from your hand, or from under your Tamers, under this Digimon as its bottom digivolution card.",
            "[Opponent's Turn] When an effect would trash one of your other Digimon's digivolution cards, you may trash this Digimon's digivolution cards with that effect instead.",
            "[Main] Delete 1 of your opponent's Digimon. Then, unsuspend 1 of your [Shoutmon X7: Superior Mode] cards, and it may attack a player.",
            "＜Rush＞ (This Digimon can attack the turn it comes into play.)",
        };
        var cached = clauses.Select(Ko).Concat(clauses.Select(Ko)).ToList();
        KoreanEffectRenderer.MemoEnabled52 = false;
        try
        {
            var plain = clauses.Select(Ko).Concat(clauses.Select(Ko)).ToList();
            Assert.Equal(plain, cached);
        }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }

    // =====================================================================
    // 3. ≪흡수진화≫ 대신 상대 디지몬 (BT3-056)
    // =====================================================================

    [Fact]
    public void DigisorptionSuspendsOpponentInstead_OncePerTurn_BT3_056()
    {
        var cer = Digimon(Uid("B52-CERES-"), 6, "[Your Turn] [Once Per Turn] When your ＜Digisorption＞ effect would suspend one of your Digimon, you may suspend your opponent's Digimon with that effect instead.", color: CardColor.Green);
        AllSupported(cer);
        Assert.Contains("《흡수진화》", Ko(cer.EffectText));
        var evo = Digimon(Uid("B52-DIGISORP-"), 5, "＜Digisorption -2＞ (When one of your Digimon would digivolve into this card from your hand, you may suspend 1 of your Digimon to reduce the memory cost of the digivolution by 2.)", color: CardColor.Green);
        evo.EvoCost = 3;
        var (state, engine) = NewMainPhase(3);
        var me = Put(state.Player, cer);
        var base1 = Put(state.Player, Digimon("B52-BASE4", 4, color: CardColor.Green));
        var opp = Put(state.DummyOpponent, Digimon("B52-OPP5", 5));
        state.Player.Hand.Add(evo);
        Assert.True(engine.EvolveDigimon(base1, evo).CanExecute);
        Assert.True(opp.IsSuspended, Logs(state));   // 상대 디지몬을 레스트시켜 코스트 -2
        Assert.False(me.IsSuspended);
        Assert.Equal(2, state.MemoryGauge);
        // 턴에 1회: 두 번째는 상대 디지몬을 고를 수 없다
        var base2 = Put(state.Player, Digimon("B52-BASE4B", 4, color: CardColor.Green));
        var opp2 = Put(state.DummyOpponent, Digimon("B52-OPP5B", 5));
        state.Player.Hand.Add(evo);
        state.MemoryGauge = 3;
        engine.EvolveDigimon(base2, evo);
        Assert.False(opp2.IsSuspended, Logs(state));
    }

    // =====================================================================
    // 4. 사건
    // =====================================================================

    [Fact]
    public void OptionUsedOrSecurityAdded_EitherPlayer_LM_023()
    {
        var mon = Digimon(Uid("B52-LM023-"), 6, "[All Turns] [Once Per Turn] When an Option card is used, or when a card is added to a security stack, 1 of your opponent's Digimon gets -6000 DP for the turn.", color: CardColor.Yellow);
        AllSupported(mon);
        Assert.Contains("시큐리티가 늘어났을 때", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        Put(state.Player, mon);
        var o = Put(state.DummyOpponent, Digimon("B52-OPP6", 6, dp: 12000));
        engine.NotifySecurityAdded(state.DummyOpponent, Digimon("B52-X", 3));   // 상대의 시큐리티가 늘어도
        engine.RecalculateFieldBuffs();
        Assert.Equal(6000, o.GetCurrentDP());
        engine.NotifySecurityAdded(state.Player, Digimon("B52-Y", 3));   // [턴에 1회]
        engine.RecalculateFieldBuffs();
        Assert.Equal(6000, o.GetCurrentDP());
    }

    [Fact]
    public void ThisDigimonWouldDigivolveInto_GrantsOpponentsDigimon_P_075()
    {
        var mon = Digimon(Uid("B52-P075-"), 5, "[Your Turn] When this Digimon would digivolve into a card with the [Insectoid] trait, all of your opponent's Digimon gain \"[All Turns] When this Digimon becomes suspended, lose 1 memory.\" until the end of your opponent's turn.", color: CardColor.Green);
        AllSupported(mon);
        var (state, engine) = NewMainPhase(5);
        var me = Put(state.Player, mon);
        var o = Put(state.DummyOpponent, Digimon("B52-OPP4", 4));
        var plain = Digimon(Uid("B52-PLAIN6-"), 6, color: CardColor.Green);
        var bug = Digimon(Uid("B52-BUG6-"), 6, color: CardColor.Green, traits: "Insectoid");
        state.Player.Hand.Add(plain);
        state.Player.Hand.Add(bug);
        var other = Put(state.Player, mon);
        Assert.True(engine.EvolveDigimon(other, plain).CanExecute);
        Assert.Empty(o.GrantedEffects);   // 곤충형이 아니면 주지 않는다
        Assert.True(engine.EvolveDigimon(me, bug).CanExecute);
        Assert.Single(o.GrantedEffects);
    }

    [Fact]
    public void AllyWouldDigivolveInto_ThatDigimonGainsWhenDigivolvingBlitz_EX2_056()
    {
        var tamer = Tamer(Uid("B52-EX2056-"), "[Your Turn] When one of your Digimon would digivolve into a Digimon with [Gallantmon] or [Growlmon] in its name, that Digimon gains \"[When Digivolving] ＜Blitz＞ (If your opponent has 1 or more memory, this Digimon may attack.).\" for the turn.", color: CardColor.Red);
        AllSupported(tamer);
        var (state, engine) = NewMainPhase(5);
        state.Player.Tamers.Add(tamer);
        var guil = Put(state.Player, Digimon("B52-GUIL", 3, color: CardColor.Red));
        var growl = Digimon(Uid("B52-GROWL-"), 4, name: "Growlmon", color: CardColor.Red);
        growl.EvoCost = 7;   // 진화 뒤 메모리가 상대측 2 (≪진격≫ 조건)
        state.Player.Hand.Add(growl);
        Assert.True(engine.EvolveDigimon(guil, growl).CanExecute);
        Assert.Contains("[≪진격≫] [그라우몬]", Logs(state));   // 얻은 【진화 시】 ≪진격≫이 진화할 때 발휘된다 (상대 쪽 메모리 2)
    }

    // =====================================================================
    // 5. 상대 필드를 보는 색 조건 무시
    // =====================================================================

    [Fact]
    public void IgnoreColorRequirement_ByOpponentsField_EX6_072_BT19_089()
    {
        var a = Option(Uid("B52-EX6072-"), "While your opponent has a level 6 or higher Digimon, you may ignore this card's color requirements.\n[Main] 1 of your level 6 Digimon and 1 card in the hand may DNA digivolve into a level 7 Digimon card in your hand.", 2, CardColor.White);
        var b = Option(Uid("B52-BT19089-"), "While your opponent has a white Digimon or Tamer, you may ignore this card's color requirements.\n[Main] Until the end of your opponent's turn, 1 of your Digimon isn't affected by the effects of your opponent's Option cards and it can't have its DP reduced.", 2, CardColor.Red);
        AllSupported(a);
        AllSupported(b);
        Assert.Contains("화이트인 상대의 디지몬/테이머가 있는 동안", Ko("While your opponent has a white Digimon or Tamer, you may ignore this card's color requirements."));
        var (state, _) = NewMainPhase();
        Put(state.Player, Digimon("B52-MINE", 5, color: CardColor.Blue));
        Assert.NotEmpty(ActionValidator.MissingOptionColors(state.Player, a, state.DummyOpponent));
        Assert.NotEmpty(ActionValidator.MissingOptionColors(state.Player, b, state.DummyOpponent));
        Put(state.DummyOpponent, Digimon("B52-OPP6", 6, color: CardColor.Green));
        Assert.Empty(ActionValidator.MissingOptionColors(state.Player, a, state.DummyOpponent));
        Assert.NotEmpty(ActionValidator.MissingOptionColors(state.Player, b, state.DummyOpponent));
        state.DummyOpponent.Tamers.Add(Tamer("B52-WT", color: CardColor.White));
        Assert.Empty(ActionValidator.MissingOptionColors(state.Player, b, state.DummyOpponent));
        Assert.NotEmpty(ActionValidator.MissingOptionColors(state.Player, b));   // 상대를 모르면 그대로
    }

    // =====================================================================
    // 6. 비용
    // =====================================================================

    [Fact]
    public void Costs_BT23_089_BT14_030_BT18_102_EX5_064()
    {
        var csTamer = Tamer(Uid("B52-BT23089-"), "[All Turns] When any of your Digimon with the [CS] trait would leave the battle area, by suspending this Tamer and trash 2 same-level cards from 1 of your [CS] trait Digimon's digivolution cards, they don't leave.");
        var bounce = Digimon(Uid("B52-BT14030-"), 5, "[On Play] [When Digivolving] By returning 1 of your opponent's level 3 Digimon or 1 of your Digimon to the hand, return 1 of your opponent's Digimon with an equal or lower level to the hand.");
        var susa = Digimon(Uid("B52-BT18102-"), 7, "[When Attacking] By placing up to 5 Tamer cards from this Digimon's digivolution cards as your bottom security cards, trash your opponent's top security card for each card placed by this effect.");
        var kou = Tamer(Uid("B52-EX5064-"), "[On Play] [Main] By suspending this Tamer and placing the top card of one of your [Light Fang] or [Night Claw] trait trait Digimon as that Digimon's bottom digivolution card, 1 of your Digimon may digivolve into a Digimon card in your hand without paying the cost.");
        foreach (var c in new[] { csTamer, bounce, susa, kou }) AllSupported(c);
        foreach (var c in new[] { csTamer, bounce, susa, kou }) Assert.DoesNotContain("<null>", Ko(c.EffectText));

        // BT23-089
        var (s1, e1) = NewMainPhase();
        s1.Player.Tamers.Add(csTamer);
        var cs = Put(s1.Player, Digimon("B52-CS5", 5, traits: "CS"), Digimon("B52-S4A", 4), Digimon("B52-S4B", 4), Digimon("B52-S3", 3));
        e1.DeleteByOpponentEffect(s1.Player, cs);
        Assert.Contains(cs, s1.Player.BattleArea);
        Assert.Single(cs.EvolutionSources);
        Assert.Contains(csTamer, s1.Player.SuspendedTamers);

        // BT14-030: 자신의 Lv.5를 되돌려 상대의 Lv.5를 되돌린다 (상대의 Lv.3만 되돌리면 뒤가 비어 있다)
        var (s2, e2) = NewMainPhase();
        var me = Put(s2.Player, bounce);
        var mine = Put(s2.Player, Digimon("B52-MINE5", 5));
        var o3 = Put(s2.DummyOpponent, Digimon("B52-O3", 3));
        var o5 = Put(s2.DummyOpponent, Digimon("B52-O5", 5));
        var o6 = Put(s2.DummyOpponent, Digimon("B52-O6", 6));
        e2.RunEffects(EffectTrigger.OnPlay, s2.Player, me, bounce);
        Assert.DoesNotContain(mine, s2.Player.BattleArea);
        Assert.DoesNotContain(o5, s2.DummyOpponent.BattleArea);
        Assert.Contains(o6, s2.DummyOpponent.BattleArea);

        // BT18-102: 테이머 카드 2장 → 상대 시큐리티 2장 파기
        var (s3, e3) = NewMainPhase();
        var su = Put(s3.Player, susa, Tamer("B52-T1"), Tamer("B52-T2"), Digimon("B52-S6", 6));
        e3.RunEffects(EffectTrigger.WhenAttacking, s3.Player, su, susa);
        Assert.Equal(7, s3.Player.SecurityStack.Count);
        Assert.Equal(3, s3.DummyOpponent.SecurityStack.Count);
        Assert.Single(su.EvolutionSources);

        // EX5-064: 맨 위 카드를 진화원 아래로 + 테이머 레스트 → 코스트 없이 진화
        var (s4, e4) = NewMainPhase();
        s4.Player.Tamers.Add(kou);
        var lf = Put(s4.Player, Digimon("B52-LF5", 5, traits: "Light Fang"), Digimon("B52-LF4", 4));
        Put(s4.Player, Digimon("B52-BASE5", 5));
        s4.Player.Hand.Add(Digimon("B52-LV6", 6));
        e4.RunEffects(EffectTrigger.OnPlay, s4.Player, null, kou);
        Assert.Contains(kou, s4.Player.SuspendedTamers);
        Assert.Contains(s4.Player.BattleArea, d => d.TopCard.CardId == "B52-LV6");
        Assert.Equal(8, s4.MemoryGauge);
    }

    // =====================================================================
    // 7. 대상
    // =====================================================================

    [Fact]
    public void DeleteOneAndAll_SameEffect_DnaOnly_BT9_082()
    {
        var text = "[When Digivolving] When DNA digivolving, delete 1 of your opponent's level 6 or higher Digimon and all of their level 5 or lower Digimon. For each Digimon deleted by this effect, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)";
        var mon = Digimon(Uid("B52-BT9082-"), 7, text);
        AllSupported(mon);
        Assert.Contains("디지몬 1마리와", Ko(text));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        Put(state.DummyOpponent, Digimon("B52-O6", 6)); Put(state.DummyOpponent, Digimon("B52-O5", 5)); Put(state.DummyOpponent, Digimon("B52-O4", 4));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon);
        Assert.Equal(3, state.DummyOpponent.BattleArea.Count);   // 조그레스 진화가 아니면 아무것도 하지 않는다
        Assert.Equal(5, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void SecurityAttackAndDpPerTamerColors_ST20_04()
    {
        var mon = Digimon(Uid("B52-ST2004-"), 4, "[On Play] [When Digivolving] For the turn, 1 of your Digimon gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.), and for every 2 colors your Tamers have, gets +2000 DP.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        foreach (var col in new[] { CardColor.Red, CardColor.Blue, CardColor.Green, CardColor.Yellow })
            state.Player.Tamers.Add(Tamer("B52-T" + col, color: col));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon);
        engine.RecalculateFieldBuffs();
        Assert.Equal(4000 + 4000, me.GetCurrentDP());
        Assert.Single(me.TimedSecurityAttackBuffs);
    }
}
