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
/// v0.51.0 미지원 문장 25차 묶음: 규칙대로 고친 것 (EX11-012 서로의 토큰 · LM-020 자신의 효과도 받지 않는다 · EX12 고르는 효과의 AI 추천) ·
/// 진화원 / 트래시 / 테이머 아래에서 등장하는 비용 (BT24-060 · LM-026 · EX5-065 · BT20-092) · 그 밖의 비용 · 조건 (BT1-007 이번 턴에 진화 · BT9-067 · EX4-065 · EX3-025) ·
/// "and"로 이은 대상 · AD1-020 어택 비용 · BT19-094 · BT5-110 · BT12-075 · BT17-100
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch51Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, int evoCost = 1, CardColor? second = null, string inherited = "", params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, SecondaryColor = second, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int playCost = 3, int dp = -1, string inherited = "", params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, 1, null, inherited, traits);

    private static CardData Tamer(string id, string main = "", string? name = null, CardColor color = CardColor.Blue, params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name, 0, null, "", traits);

    private static CardData Option(string id, string main = "", int cost = 3, string? name = null, CardColor color = CardColor.Blue, params string[] traits) =>
        Card(id, main, CardType.Option, 0, cost, color, 0, name, 0, null, "", traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B51-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B51-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B51-SEC" + i, 3, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B51-OSEC" + i, 3, color: CardColor.White));
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
    // 1. 규칙대로 고친 것
    // =====================================================================

    [Fact]
    public void Medusamon_DeleteAnyToken_IncludingOpponents_EX11_012()
    {
        var mon = Digimon(Uid("B51-MEDUSA-"), 6, "[All Turns] When this Digimon would leave the battle area, by deleting 1 Token, it doesn't leave.");
        AllSupported(mon);
        Assert.Contains("토큰 1마리를 소멸시키는 것으로", Ko(mon.EffectText));
        Assert.DoesNotContain("자신의 토큰", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var tok = Put(state.DummyOpponent, Digimon("TOKEN-B51PETRI", 0, name: "Petrification Token", color: CardColor.White, dp: 3000));
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.True(state.Player.BattleArea.Contains(me), Logs(state));   // 상대의 「석화」 토큰을 소멸시켜 떠나지 않는다
        Assert.DoesNotContain(tok, state.DummyOpponent.BattleArea);
        // "of your Tokens"는 그대로 자신의 토큰만
        Assert.Contains("자신의 토큰", Ko("[All Turns] When this Digimon would leave the battle area, by deleting 1 of your Tokens, it doesn't leave."));
    }

    [Fact]
    public void Quantumon_CategoryImmunity_CoversOwnStaticEffects_LM_020()
    {
        var (state, engine) = NewMainPhase();
        var q = Put(state.Player, Digimon("B51-QUANT", 6, dp: 13000));
        var buffer = Put(state.Player, Digimon(Uid("B51-AURA-"), 5, "[All Turns] All of your Digimon get +1000 DP."));
        state.Player.Tamers.Add(Tamer(Uid("B51-TAMERAURA-"), "[All Turns] All of your Digimon get +2000 DP."));
        engine.RecalculateFieldBuffs();
        int before = q.GetCurrentDP();
        q.UnaffectedCategory50 = CardType.Digimon;
        q.UnaffectedCategory50Until = state.TurnSequence;
        engine.RecalculateFieldBuffs();
        Assert.Equal(before - 1000, q.GetCurrentDP());   // 자신의 다른 디지몬의 지속 효과도 받지 않는다
        q.UnaffectedCategory50 = CardType.Tamer;
        engine.RecalculateFieldBuffs();
        Assert.Equal(before - 2000, q.GetCurrentDP());   // 테이머의 지속 효과
        Assert.True(buffer.GetCurrentDP() > 5000);        // 다른 디지몬은 그대로
        // 자신의 디지몬 효과의 대상으로도 고를 수 없다
        q.UnaffectedCategory50 = CardType.Digimon;
        var pump = Digimon(Uid("B51-PUMP-"), 5, "[On Play] 1 of your Digimon gets +5000 DP for the turn.");
        var p = Put(state.Player, pump);
        int qdp = q.GetCurrentDP();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, p, pump);
        Assert.Equal(qdp, q.GetCurrentDP());
    }

    [Fact]
    public void Ex12Choice_AiSkipsInfeasibleDigivolveOption()
    {
        string text = "[Your Turn] When one of your Digimon with [Gammamon] in its text or the [VB] trait attacks, by suspending this Tamer, activate 1 of the effects below:\n· It may digivolve into a level 6 or lower card with [Gammamon] in its text or the [VB] trait in the hand with the cost reduced by 1.\n· You may use 1 Option card with [Gammamon] in its text or the [VB] trait from your hand with the cost reduced by 2.";
        var hiro = Tamer(Uid("B51-HIRO-"), text, name: "Hiro");
        var (state, engine) = NewMainPhase(memory: 5);
        state.Player.Tamers.Add(hiro);
        var gam = Put(state.Player, Digimon("B51-GAMMA", 4, "[On Play] [Gammamon]", name: "Gammamon"));
        state.Player.Hand.Add(Option("B51-VBOPT", "[Main] Gain 1 memory.", cost: 2, traits: new[] { "VB" }));   // 진화할 카드는 없다
        engine.RunEffects(EffectTrigger.AllyDigimonAttacks, state.Player, null, hiro, other: gam);
        Assert.True(state.Player.Trash.Any(x => x.CardId == "B51-VBOPT"), Logs(state));   // 할 수 있는 옵션 쪽을 고른다
        // 둘 다 할 수 없으면 비용(테이머 레스트)을 내지 않는다
        var (s2, e2) = NewMainPhase(memory: 5);
        s2.Player.Tamers.Add(hiro);
        var g2 = Put(s2.Player, Digimon("B51-GAMMA2", 4, "[On Play] [Gammamon]", name: "Gammamon"));
        e2.RunEffects(EffectTrigger.AllyDigimonAttacks, s2.Player, null, hiro, other: g2);
        Assert.True(s2.Player.HasActiveTamer(hiro), Logs(s2));
    }

    // =====================================================================
    // 2. 비용
    // =====================================================================

    [Fact]
    public void PlayTamerFromSources_TheyDontLeave_BT24_060()
    {
        var src = Digimon(Uid("B51-BT24060-"), 5, inherited: "[All Turns] [Once Per Turn] When any of your [DigiPolice] or [SEEKERS] trait Digimon would leave the battle area, by playing 1 [DigiPolice] or [SEEKERS] trait Tamer card from this Digimon's digivolution cards without paying the cost, they don't leave.");
        AllSupported(src);
        Assert.DoesNotContain("<null>", Ko(src.InheritedEffectText));
        var (state, engine) = NewMainPhase();
        var tamerCard = Tamer("B51-SEEKT", traits: new[] { "SEEKERS" });
        var me = Put(state.Player, Digimon("B51-TOP", 6, traits: new[] { "SEEKERS" }), src, tamerCard);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.True(state.Player.BattleArea.Contains(me), Logs(state));
        Assert.Contains(tamerCard, state.Player.Tamers);
        Assert.DoesNotContain(tamerCard, me.EvolutionSources);
    }

    [Fact]
    public void PlayGuilmonThenPlaceUnder_LM_026()
    {
        var mon = Digimon(Uid("B51-LM026-"), 6, "[All Turns] When this Digimon would leave the battle area, by playing 1 [Guilmon] from this Digimon's digivolution cards or from your trash, place this Digimon as that Digimon's bottom digivolution card.", name: "Chaosdukemon");
        AllSupported(mon);
        Assert.DoesNotContain("<null>", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var guil = Digimon("B51-GUIL", 3, name: "Guilmon");
        var me = Put(state.Player, mon, guil, Digimon("B51-GROWL", 4, name: "Growlmon"));
        engine.DeleteByOpponentEffect(state.Player, me);
        var host = state.Player.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TopCard, guil));
        Assert.True(host != null, Logs(state));
        Assert.Contains(mon, host!.EvolutionSources);           // 이 디지몬(맨 위 카드와 진화원)이 길몬의 진화원 아래로
        Assert.DoesNotContain(mon, state.Player.Trash);
        Assert.DoesNotContain(me, state.Player.BattleArea);
    }

    [Fact]
    public void PlaySameLevelSource_DnaDigivolve_EX5_065()
    {
        var t = Tamer(Uid("B51-EX5065-"), "[Start of Opponent's Turn] By playing 1 card with the same level as one of your [Night Claw] or [Light Fang] trait Digimon from that Digimon's digivolution cards without paying the cost, 2 of your Digimon may DNA digivolve into a Digimon card in your hand. At the end of the turn, return the Digimon played by this effect to the hand.");
        AllSupported(t);
        Assert.DoesNotContain("<null>", Ko(t.EffectText));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(t);
        var same = Digimon("B51-SAME5", 5, traits: new[] { "Light Fang" });
        var host = Put(state.Player, Digimon("B51-LF5", 5, traits: new[] { "Light Fang" }), same);
        engine.RunEffects(EffectTrigger.StartOfOpponentsTurn50, state.Player, null, t);
        Assert.True(state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, same)), Logs(state));
        Assert.DoesNotContain(same, host.EvolutionSources);
    }

    [Fact]
    public void OtherCosts_EX3_035_P_179_BT19_086_BT20_092_BT12_048_BT7_045_EX2_055()
    {
        var ex3035 = Digimon(Uid("B51-EX3035-"), 6, "[When Attacking] 1 of your opponent's Digimon gets -6000 DP for the turn. Then, by returning 1 [Magnadramon], 1 [Azulongmon], and 1 [Megidramon] from your trash to the bottom of your deck in any order, trash the top 2 cards of your opponent's security stack.");
        AllSupported(ex3035);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, ex3035);
        foreach (var n in new[] { "Magnadramon", "Azulongmon", "Megidramon" }) state.Player.Trash.Add(Digimon("B51-" + n, 6, name: n));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, ex3035);
        Assert.True(3 == state.DummyOpponent.SecurityStack.Count, Logs(state));
        Assert.Empty(state.Player.Trash);

        foreach (var text in new[]
        {
            "[When Digivolving] By placing 1 Option card with the [Device] trait from your hand or trash into the battle area, this Digimon gets +3000 DP until your opponent's turn ends.",
            "[On Play] By revealing up to 3 Tamer cards in your hand and placing them at the bottom of your deck in any order, ＜Draw 1＞ (Draw 1 card from your deck.) for each card placed at the bottom of your deck.",
            "[When Attacking] If the level 6 cards in this Digimon's digivolution cards have 3 or more colors among them, this Digimon gain +3000 DP until the end of your opponent's next turn. If they have 4 or more colors, ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)",
            "When you would play this Digimon, you may trash 7 or more digivolution cards from the bottom of 1 of your [Mother D-Reaper]s to set this Digimon's play cost to 0.",
            "[When Attacking] You may place 2 [ADR-02 Searcher]s from your trash under this Digimon in any order as its bottom digivolution cards to unsuspend this Digimon.",
        })
        {
            AllSupported(Digimon(Uid("B51-COST-"), 6, text));
            Assert.DoesNotContain("<null>", Ko(text));
        }
        AllSupported(Digimon(Uid("B51-BT7045-"), 3, inherited: "[When Attacking] You may reveal 1 green Digimon card from your hand and place it on top of your deck to have this Digimon get +3000 DP for the turn."));
        foreach (var text in new[]
        {
            "[Main] By suspending this Tamer and trashing 4 of your Option cards with the [Device] trait in the battle area, you may play 1 [Cyberdramon] from your hand or trash without paying the cost.",
            "[Start of Your Main Phase] If you don't have a Digimon, by playing 1 play cost 3 or lower Digimon card from under this Tamer without paying the cost, delete this Tamer.",
        })
        {
            AllSupported(Tamer(Uid("B51-TCOST-"), text));
            Assert.DoesNotContain("<null>", Ko(text));
        }

        // BT12-048 공개한 장수만큼 드로우
        var bt12048 = Digimon(Uid("B51-BT12048-"), 4, "[On Play] By revealing up to 3 Tamer cards in your hand and placing them at the bottom of your deck in any order, ＜Draw 1＞ (Draw 1 card from your deck.) for each card placed at the bottom of your deck.");
        var (s2, e2) = NewMainPhase();
        var m2 = Put(s2.Player, bt12048);
        s2.Player.Hand.Add(Tamer("B51-HT1")); s2.Player.Hand.Add(Tamer("B51-HT2"));
        e2.RunEffects(EffectTrigger.OnPlay, s2.Player, m2, bt12048);
        Assert.True(2 == s2.Player.Hand.Count && s2.Player.Hand.All(x => x.Type == CardType.Digimon), Logs(s2));
        Assert.Equal("B51-HT2", s2.Player.MainDeck[^1].CardId);
    }

    [Fact]
    public void AttackCost_SecurityPlus_AD1_020()
    {
        var src = Digimon(Uid("B51-AD1020-"), 3, inherited: "[End of Your Turn] [Once Per Turn] By attacking with this Digimon with the [Hybrid] or [Ten Warriors] trait, it gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for the attack.");
        AllSupported(src);
        Assert.Contains("시큐리티 어택 +1", Ko("[End of Your Turn] [Once Per Turn] By attacking with this Digimon with the [Hybrid] or [Ten Warriors] trait, it gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for the attack."));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B51-HYB", 5, traits: new[] { "Hybrid" }), src);
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, me, src);
        engine.RunDeclaredEffectAttack57();   // v0.57.0 효과로 선언한 어택은 행동이 끝날 때 카운터 · 블록 · 배틀
        Assert.True(state.DummyOpponent.SecurityStack.Count <= 3, Logs(state));   // 시큐리티 2장 체크
        // 특징이 없으면 어택하지 않는다
        var (s2, e2) = NewMainPhase();
        var m2 = Put(s2.Player, Digimon("B51-NOHYB", 5), src);
        e2.RunEffects(EffectTrigger.EndOfYourTurn, s2.Player, m2, src);
        e2.RunDeclaredEffectAttack57();   // v0.57.0 효과로 선언한 어택은 행동이 끝날 때 카운터 · 블록 · 배틀
        Assert.Equal(5, s2.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void TamerSecurityEffectLabel_ThisDigimon_IsInherited_AD1()
    {
        var t = Card(Uid("B51-AD1T-"), "[Security] Play this card without paying the cost.", CardType.Tamer, inherited: "Security Effect [All Turns] [Once Per Turn] When this Digimon with the [Hybrid] or [Ten Warriors] trait would leave the battle area, by adding your top security card to the hand, it doesn't leave.");
        Assert.Contains(EffectSupport.For(t).Clauses, c => c.Inherited && c.Text.Contains("would leave"));
        var o = Card(Uid("B51-SECT-"), "", CardType.Tamer, inherited: "Security Effect [Security] Play this card without paying the cost.");
        Assert.DoesNotContain(EffectSupport.For(o).Clauses, c => c.Inherited);   // 진짜 [시큐리티] 효과는 그대로
        Assert.Contains("등장시킬 때", Ko("[Your Turn] When you would play [Pulsemon], by suspending this Tamer, if you have 3 or more security cards, ＜Draw 1＞. If you have 3 or less security cards, gain 1 memory."));
        Assert.Contains("과 「청룡몬」과 「메기드라몬」 1장씩을", Ko("[When Attacking] 1 of your opponent's Digimon gets -6000 DP for the turn. Then, by returning 1 [Magnadramon], 1 [Azulongmon], and 1 [Megidramon] from your trash to the bottom of your deck in any order, trash the top 2 cards of your opponent's security stack."));
    }

    // =====================================================================
    // 3. 조건
    // =====================================================================

    [Fact]
    public void DigivolvedThisTurn_BT1_007()
    {
        var src = Digimon(Uid("B51-TANE-"), 2, inherited: "[When Attacking] If you've digivolved this turn, this Digimon gets +1000 DP for the turn.");
        AllSupported(src);
        Assert.Contains("1회 이상 진화시키고 있는 경우", Ko("[When Attacking] If you've digivolved this turn, this Digimon gets +1000 DP for the turn."));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B51-KOKU", 3, dp: 3000), src);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Equal(3000, me.GetCurrentDP());
        var evo = Digimon("B51-EVO4", 4, dp: 4000);
        state.Player.Hand.Add(evo);
        Assert.True(engine.EvolveDigimon(me, evo).CanExecute, Logs(state));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Equal(5000, me.GetCurrentDP());
    }

    [Fact]
    public void Conditions_EX4_065_BT21_089_EX4_027_EX3_033_EX3_025()
    {
        var trident = Option(Uid("B51-EX4065-"), "[Main] Delete 1 of your opponent's Digimon with the highest DP. If a Digimon with 13000 DP or more is deleted by this effect, trash the top card of your opponent's security stack.");
        AllSupported(trident);
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, Digimon("B51-BIG", 6, dp: 13000));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, trident);
        Assert.True(4 == state.DummyOpponent.SecurityStack.Count, Logs(state));
        var (s2, e2) = NewMainPhase();
        Put(s2.DummyOpponent, Digimon("B51-MID", 5, dp: 12000));
        e2.RunEffects(EffectTrigger.Main, s2.Player, null, trident);
        Assert.Equal(5, s2.DummyOpponent.SecurityStack.Count);

        foreach (var text in new[]
        {
            "[All Turns] When any of your Digimon are played or digivolve, by suspending this Tamer, until your opponent's turn ends, 1 of your Digimon with [Guilmon], [Growlmon], [Gallantmon] or [Megidramon] in its name or the [Hero] trait gains ＜Blocker＞ (This Digimon can block in the blocker timing.) and, if there are 10 or more total cards in both players' trashes, gets +2000 DP.",
        })
        {
            AllSupported(Tamer(Uid("B51-COND-"), text));
            Assert.DoesNotContain("<null>", Ko(text));
        }
        foreach (var text in new[]
        {
            "[When Digivolving] 1 of your opponent's Digimon gets -2000 DP for the turn. If you have a blue or yellow Tamer in play, or if you have a card with the [Armor Form] trait in your trash, 1 of your opponent's Digimon with 6000 DP or less can't attack or block until the end of your opponent's turn.",
            "[Opponent's Turn] While you have a Digimon with the [Four Great Dragons] trait in play, or [Trial of the Four Great Dragons] is in your battle area, this Digimon gains ＜Blocker＞ (This Digimon can block in the blocker timing.).",
            "[On Play] ＜Draw 2＞ (Draw 2 cards from your deck.) Then, if this card was played by [Trial of the Four Great Dragons]'s effect, gain 2 memory.",
        })
        {
            AllSupported(Digimon(Uid("B51-DCOND-"), 6, text));
            Assert.DoesNotContain("<null>", Ko(text));
        }
    }

    [Fact]
    public void IfNoOptionUsed_BT6_065()
    {
        var mon = Digimon(Uid("B51-BT6065-"), 6, "[When Digivolving] Reveal the top 5 cards of your deck. You may use 1 Option card with a memory cost of 7 among them without paying its memory cost. Trash the remaining cards. If you don't use an Option card with this effect, delete 1 of your opponent's Digimon with a play cost of 4 or less.");
        AllSupported(mon);
        Assert.Contains("이 효과로 옵션 카드를 사용하지 않았을 때", Ko(mon.EffectText));
        // 쓸 옵션이 없다 → 소멸
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var victim = Put(state.DummyOpponent, Digimon("B51-V3", 3, playCost: 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon);
        Assert.True(!state.DummyOpponent.BattleArea.Contains(victim), Logs(state));
        // 옵션을 썼다 → 소멸하지 않는다
        var (s2, e2) = NewMainPhase();
        var m2 = Put(s2.Player, mon);
        s2.Player.MainDeck.Insert(0, Option("B51-OPT7", "[Main] Gain 1 memory.", cost: 7));
        var v2 = Put(s2.DummyOpponent, Digimon("B51-V3B", 3, playCost: 3));
        e2.RunEffects(EffectTrigger.WhenDigivolving, s2.Player, m2, mon);
        Assert.True(s2.Player.Trash.Any(x => x.CardId == "B51-OPT7"), Logs(s2));
        Assert.True(s2.DummyOpponent.BattleArea.Contains(v2), Logs(s2));
    }

    // =====================================================================
    // 4. 동작 · "and"로 이은 대상
    // =====================================================================

    [Fact]
    public void Actions_BT19_094_BT5_110_BT12_075_BT17_100()
    {
        var bt19094 = Option(Uid("B51-BT19094-"), "[Main] Delete your opponent's Digimon until they have as many as the number of your security cards. If this effect deleted, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)");
        AllSupported(bt19094);
        var (state, engine) = NewMainPhase();
        state.Player.SecurityStack.RemoveRange(0, 3);   // 시큐리티 2장
        for (int i = 0; i < 4; i++) Put(state.DummyOpponent, Digimon("B51-OD" + i, 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, bt19094);
        Assert.True(2 == state.DummyOpponent.BattleArea.Count, Logs(state));
        Assert.Equal(3, state.Player.SecurityStack.Count);

        var bt5110 = Option(Uid("B51-BT5110-"), "[Main] You may return 1 of your Digimon with [Omnimon] in its name to its owner's hand to delete all Digimon and Tamers. Trash all of the digivolution cards of the Digimon you returned with this effect.");
        AllSupported(bt5110);
        var (s2, e2) = NewMainPhase();
        var omni = Put(s2.Player, Digimon("B51-OMNI", 7, name: "Omnimon"), Digimon("B51-WG", 6));
        var other = Put(s2.Player, Digimon("B51-MINE", 4));
        Put(s2.DummyOpponent, Digimon("B51-THEIRS", 5));
        s2.DummyOpponent.Tamers.Add(Tamer("B51-OTAMER"));
        e2.RunEffects(EffectTrigger.Main, s2.Player, null, bt5110);
        Assert.True(s2.Player.Hand.Any(x => x.CardId == "B51-OMNI"), Logs(s2));
        Assert.Empty(s2.Player.BattleArea);
        Assert.Empty(s2.DummyOpponent.BattleArea);
        Assert.Empty(s2.DummyOpponent.Tamers);

        var bt12075 = Tamer(Uid("B51-BT12075-"), "[On Play] You may return 1 Digimon card with ＜Save＞ in its text from under one of your Tamers to your hand.");
        AllSupported(bt12075);
        var (s3, e3) = NewMainPhase();
        var save = Digimon("B51-SAVE", 4, "＜Save＞ (You may place this card under one of your Tamers.)");
        s3.Player.UnderTamers.Add(save);
        s3.Player.Tamers.Add(bt12075);
        e3.RunEffects(EffectTrigger.OnPlay, s3.Player, null, bt12075);
        Assert.Contains(save, s3.Player.Hand);

        var clock = Option(Uid("B51-CLOCK-"), "[Start of Your Turn] If 4 [Doomsday Clock]s are placed in your battle area, you win the game.", name: "Doomsday Clock");
        AllSupported(clock);
        Assert.DoesNotContain("<null>", Ko(clock.EffectText));
    }

    [Fact]
    public void CompoundTargets_Supported()
    {
        foreach (var (type, text) in new[]
        {
            (CardType.Tamer, "[End of Your Turn] By suspending this Tamer, suspend 1 of your opponent's Digimon and, until the end of their turn, 1 of your Digimon with the [Vortex Warriors] trait gets +2000 DP."),
            (CardType.Option, "[Main] 1 of your opponent's Digimon gets -4000 DP for the turn, and all of your opponent's Digimon get -1000 DP for the turn for each card with the [Armor Form] trait in your trash."),
            (CardType.Digimon, "[When Digivolving] ＜De-Digivolve 3＞ 1 of your opponent's Digimon and, for the turn, all of their Digimon get -6000 DP. Then, if DNA digivolving, you may play up to 10 play cost's total worth of [NSo] trait Digimon cards from your trash without paying the cost."),
            (CardType.Digimon, "[On Play] [When Digivolving] ＜De-Digivolve 2＞ 1 of your opponent's Digimon. (Trash up to 2 cards from the top. You can't trash past level 3 cards.) and flip your opponent's top face-down security card face up. Then, delete 1 of your opponent's Digimon with 1 or fewer digivolution cards."),
            (CardType.Digimon, "[On Play] [When Digivolving] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.) with as much or less DP as this Digimon. Then, delete 1 Digimon with a play cost of 3 or less."),
            (CardType.Option, "[Main] Until the end of your opponent's turn, 1 of your Digimon with [Numemon], [Sukamon], [Nanimon] or [Etemon] in its name can't be blocked and gains \"[On Deletion] Gain 3 memory.\""),
            (CardType.Option, "[Main] Delete 1 of your opponent's Digimon. Then, unsuspend 1 of your [Shoutmon X7: Superior Mode] cards, and it may attack a player."),
            (CardType.Digimon, "[End of Attack] Delete this Digimon and 1 of your opponent's Digimon, and ＜Recovery +1 (Deck)＞. Then, if you have a Tamer in play, hatch 1 Digi-Egg card to an empty space in your breeding area."),
            (CardType.Digimon, "[Main] You may suspend this Digimon to digivolve 1 of your Digimon into a Digimon card with the [Fairy] trait trait in your hand. When it would digivolve by this effect, reduce the digivolution cost by 2."),
            (CardType.Digimon, "[On Play] [When Digivolving] You may delete 1 of your other purple Digimon to digivolve this Digimon into a Digimon card with [Belphemon] in its name in your hand without paying its digivolution cost."),
            (CardType.Digimon, "[On Play] [When Digivolving] Delete any of your opponent's Digimon with a total play cost up to 15."),
            (CardType.Digimon, "[When Digivolving] Reveal the top 5 cards of your deck. You may use 1 Option card with a memory cost of 7 among them without paying its memory cost. Trash the remaining cards. If you don't use an Option card with this effect, delete 1 of your opponent's Digimon with a play cost of 4 or less."),
            (CardType.Digimon, "[On Play] Delete 1 level 4 or lower Digimon. If you did, you may play 1 [Gyuukimon] Token without paying the cost. (Digimon/Cost 7/Lv.5/Purple/Ultimate/Virus/Dark Animal/3000 DP)"),
        })
        {
            var card = type == CardType.Tamer ? Tamer(Uid("B51-CT-"), text) : type == CardType.Option ? Option(Uid("B51-CO-"), text) : Digimon(Uid("B51-CD-"), 6, text);
            AllSupported(card);
            Assert.True(!Ko(text).Contains("<null>"), text);
        }
        AllSupported(Digimon(Uid("B51-AD1004-"), 3, inherited: "[When Attacking] [Once Per Turn] Delete 1 of your opponent's Digimon with as much or less DP as this Digimon with [Greymon] or [Omnimon] in its name in its name."));
        AllSupported(Digimon(Uid("B51-ST2213-"), 3, inherited: "[When Attacking] [Once Per Turn] If your opponent has no unsuspended Digimon, this Digimon with the [Vortex Warriors] trait may unsuspend."));

        // BT8-101 두 대상
        var bt8101 = Option(Uid("B51-BT8101-"), "[Main] 1 of your opponent's Digimon gets -4000 DP for the turn, and all of your opponent's Digimon get -1000 DP for the turn for each card with the [Armor Form] trait in your trash.");
        var (state, engine) = NewMainPhase();
        var a = Put(state.DummyOpponent, Digimon("B51-OA", 6, dp: 10000));
        var b = Put(state.DummyOpponent, Digimon("B51-OB", 5, dp: 9000));
        state.Player.Trash.Add(Digimon("B51-ARMOR", 4, traits: new[] { "Armor Form" }));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, bt8101);
        Assert.Equal(19000 - 4000 - 2000, a.GetCurrentDP() + b.GetCurrentDP());
    }
}
