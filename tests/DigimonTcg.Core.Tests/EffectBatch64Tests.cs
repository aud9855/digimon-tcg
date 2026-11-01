using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.64.0 미지원 문장 38차 묶음: 턴 종료 조건 (종합 룰 6-1-4-1 · 6-6-1 — BT14-081 · BT17-069, 공식 Q&amp;A BT14-081 Q1) · BT14-081 등장 매수 상한 +2
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch64Tests
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

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int security = 5)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B64-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B64-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B64-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B64-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    private const string TurnEndText = "[Your Turn] Your turn end condition is the opponent having 3 or more memory.";
    private const string TurnEndInherited = "[Your Turn] While this Digimon has [Fenriloogamon] in its name, the turn end condition is the opponent having 3 or more memory.";

    // =====================================================================
    // 1. 턴 종료 조건 (BT14-081 · BT17-069)
    // =====================================================================

    [Fact]
    public void TurnEnd_Player_OpponentSide1And2_TurnContinues_3Ends_BT14_081()
    {
        var fen = Digimon(Uid("B64-FEN-"), 6, TurnEndText, name: "Fenriloogamon");
        AllSupported(fen);
        Assert.Equal("【자신의 턴】 자신의 턴 종료 조건은 메모리가 상대 쪽의 3 이상이 되는 것이다.", Ko(TurnEndText));
        var (state, engine) = NewMainPhase(memory: 2);
        Put(state.Player, fen);
        engine.RecalculateFieldBuffs();
        Assert.Equal(3, state.Player.TurnEndMemory64);
        int turn = state.CurrentTurn;
        var a = Digimon("B64-A", 3, playCost: 3);
        state.Player.Hand.Add(a);
        Assert.True(engine.PlayCard(a).CanExecute, Logs(state));
        Assert.Equal(-1, state.MemoryGauge);   // 상대 쪽의 1 — 턴이 이어진다 (공식 Q&A BT14-081 Q1)
        Assert.Equal(turn, state.CurrentTurn);
        Assert.Equal(Phase.Main, state.CurrentPhase);
        var b = Digimon("B64-B", 3, playCost: 1);
        state.Player.Hand.Add(b);
        Assert.True(engine.PlayCard(b).CanExecute, Logs(state));
        Assert.Equal(turn, state.CurrentTurn);   // 상대 쪽의 2 — 아직 이어진다
        var c = Digimon("B64-C", 3, playCost: 1);
        state.Player.Hand.Add(c);
        Assert.True(engine.PlayCard(c).CanExecute, Logs(state));
        Assert.Equal(turn + 1, state.CurrentTurn);   // 상대 쪽의 3 — 턴 종료 (연습 모드는 다음 턴이 바로 시작)
    }

    [Fact]
    public void TurnEnd_WithoutEffect_Ends_AtOpponentSide1()
    {
        var (state, engine) = NewMainPhase(memory: 2);
        int turn = state.CurrentTurn;
        var a = Digimon("B64-A", 3, playCost: 3);
        state.Player.Hand.Add(a);
        Assert.True(engine.PlayCard(a).CanExecute);
        Assert.Equal(turn + 1, state.CurrentTurn);
    }

    [Fact]
    public void TurnEnd_EffectLeaves_WhileOpponentSide1_TurnEndsAfterProcessing()
    {
        var fen = Digimon(Uid("B64-FEN-"), 6, TurnEndText, name: "Fenriloogamon");
        var (state, engine) = NewMainPhase(memory: -1);
        var me = Put(state.Player, fen);
        engine.RecalculateFieldBuffs();
        int turn = state.CurrentTurn;
        Assert.False(engine.CheckTurnEndAndAutoReset());
        engine.DeleteFrom(state.Player, me);   // 효과가 없어졌다 — 처리가 끝난 때 원래 조건 (상대 쪽의 1 이상)
        Assert.True(engine.CheckTurnEndAndAutoReset());
        Assert.Equal(turn + 1, state.CurrentTurn);
    }

    [Fact]
    public void TurnEnd_Pass_WhileOpponentSide1_MovesTo3AndEnds()
    {
        var fen = Digimon(Uid("B64-FEN-"), 6, TurnEndText, name: "Fenriloogamon");
        var (state, engine) = NewMainPhase(memory: -1);
        Put(state.Player, fen);
        engine.RecalculateFieldBuffs();
        int turn = state.CurrentTurn;
        Assert.True(engine.PassTurn().CanExecute);
        Assert.Contains(state.ActionLogs, l => l.Contains("상대에게 넘어간 메모리: +3"));   // 패스 — 메모리를 상대 쪽의 3으로 (6-5-1-7-1)
        Assert.Equal(turn + 1, state.CurrentTurn);
    }

    [Fact]
    public void TurnEnd_Inherited_OnlyWhileNameHasFenriloogamon_AndOnlyOwnTurn_BT17_069()
    {
        var src = Digimon(Uid("B64-FSRC-"), 5, inherited: TurnEndInherited, name: "Fenriloogamon");
        AllSupported(src);
        Assert.Equal("【자신의 턴】 이 디지몬의 명칭에 「펜리루가몬」이 포함되는 동안, 자신의 턴 종료 조건은 메모리가 상대 쪽의 3 이상이 되는 것이다.", Ko(TurnEndInherited));
        var (state, engine) = NewMainPhase(memory: 0);
        var host = Put(state.Player, Digimon("B64-HOST", 6, name: "Fenriloogamon: Takemikazuchi"), false, src);
        engine.RecalculateFieldBuffs();
        Assert.Equal(3, state.Player.TurnEndMemory64);
        var other = Put(state.Player, Digimon("B64-OTHER", 6, name: "Kazuchimon"), false, Digimon(Uid("B64-FSRC2-"), 5, inherited: TurnEndInherited, name: "Fenriloogamon"));
        state.Player.BattleArea.Remove(host);
        engine.RecalculateFieldBuffs();
        Assert.Equal(1, state.Player.TurnEndMemory64);   // 이름에 「펜리루가몬」이 없다
        other.IsOwnersTurn = false;
        state.Player.BattleArea.Add(host);
        host.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.Equal(1, state.Player.TurnEndMemory64);   // 【자신의 턴】 — 상대의 턴에는 없다
    }

    [Fact]
    public void TurnEnd_Validator_GuideAndTamerEvolutionNotBlockedByBlitzWhileTurnContinues()
    {
        var fen = Digimon(Uid("B64-FEN-"), 6, TurnEndText, name: "Fenriloogamon");
        var (state, engine) = NewMainPhase(memory: 1);
        Put(state.Player, fen);
        engine.RecalculateFieldBuffs();
        var a = Digimon("B64-A", 3, playCost: 2);
        var r = ActionValidator.ValidatePlay(state, a);
        Assert.DoesNotContain("턴이 종료됩니다", r.GuideMessage);   // 상대 쪽의 1 — 턴이 이어진다
        var big = Digimon("B64-BIG", 3, playCost: 5);
        Assert.Contains("턴이 종료됩니다", ActionValidator.ValidatePlay(state, big).GuideMessage);
        Assert.False(EffectRules.PlayerTurnEndMet64(state));
    }

    [Fact]
    public void TurnEnd_Ai_KeepsActingAtOpponentSide1And2_HandsOverAtLeast3()
    {
        var fen = Digimon(Uid("B64-FEN-"), 6, TurnEndText, name: "Fenriloogamon", dp: 13000);
        var (state, engine) = NewMainPhase(memory: 0);
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        Put(state.DummyOpponent, fen);
        for (int i = 0; i < 4; i++) state.DummyOpponent.Hand.Add(Digimon("B64-AIH" + i, 4, playCost: 2, dp: 5000));
        engine.RecalculateFieldBuffs();
        int before = state.DummyOpponent.BattleArea.Count;
        engine.PassTurn();   // AI는 메모리 3으로 시작
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        int played = state.DummyOpponent.BattleArea.Count - before + state.DummyOpponent.Trash.Count(c => c.CardId.StartsWith("B64-AIH"));
        Assert.True(played >= 3, Logs(state));   // 3 → 1 → 상대 쪽의 1 (턴이 이어진다) → 상대 쪽의 3
        Assert.True(state.MemoryGauge >= 3, Logs(state));   // 내 턴은 상대 쪽의 3 이상으로 시작
    }

    [Fact]
    public void BT14_081_PlayCountUp_WithEijiInSources_UpTo3_Else1()
    {
        const string text = "[When Digivolving] You may play 1 level 4 or lower card with the [Dark Animal] or [SoC] trait from your trash without paying its cost. If [Eiji Nagasumi] is in this Digimon's digivolution cards, increase the number of Digimon you can play with this effect by 2.";
        var fen = Digimon(Uid("B64-FENW-"), 6, text, name: "Fenriloogamon");
        AllSupported(fen);
        Assert.EndsWith("이 디지몬의 진화원에 「나가스미 에이지」가 있다면, 이 효과로 등장시킬 수 있는 디지몬의 수 +2.", Ko(text));
        foreach (bool eiji in new[] { true, false })
        {
            var (state, engine) = NewMainPhase();
            var srcs = eiji ? new[] { Card(Uid("B64-EIJI-"), "", CardType.Tamer, name: "Eiji Nagasumi") } : Array.Empty<CardData>();
            var me = Put(state.Player, fen, false, srcs);
            for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon("B64-SOC" + i, 4, traits: "SoC"));
            state.Player.Trash.Add(Digimon("B64-SOC5", 5, traits: "SoC"));
            engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, fen, includeInherited: false);
            int played = state.Player.BattleArea.Count(d => d.TopCard.CardId.StartsWith("B64-SOC"));
            Assert.Equal(eiji ? 3 : 1, played);
            Assert.DoesNotContain(state.Player.BattleArea, d => d.TopCard.CardId == "B64-SOC5");   // Lv.5는 아니다
        }
    }

    // =====================================================================
    // 2. EX8-070 "that Digimon" = 비용으로 진화원을 버린 디지몬
    // =====================================================================

    [Fact]
    public void EX8_070_ThatDigimon_IsTheCostDigimon_GetsKeywordsDpAndReturnShield()
    {
        var real = CardCatalogFor("EX8-070");
        var opt = Card(Uid("B64-ZOFR-"), real);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var other = Put(state.Player, Digimon("B64-ROCK0", 5, dp: 9000, traits: "Rock"));   // 진화원 없음 — 비용으로 고를 수 없다
        var host = Put(state.Player, Digimon("B64-ROCK1", 5, dp: 5000, traits: "Mineral"), false, Digimon("B64-RS", 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.True(host.EvolutionSources.Count == 0, Logs(state));
        Assert.Equal(8000, host.GetCurrentDP());
        foreach (var k in new[] { Keyword.Collision, Keyword.Piercing, Keyword.Reboot, Keyword.CannotBeReturnedByOpponentEffects })
            Assert.True(CardScripts.HasKeyword(host, k), k.ToString());
        Assert.Equal(9000, other.GetCurrentDP());
        Assert.False(CardScripts.HasKeyword(other, Keyword.Collision));
        state.TurnSequence += 2;   // 상대의 턴 종료 뒤
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, host.GetCurrentDP());
        Assert.False(CardScripts.HasKeyword(host, Keyword.CannotBeReturnedByOpponentEffects));
    }

    // =====================================================================
    // 3. BT19-078 이 효과로 등장한 디지몬으로 어택 대상 변경
    // =====================================================================

    [Fact]
    public void BT19_078_SwitchToPlayedJeri_WhenOpponentAttacks()
    {
        const string text = "[Opponent's Turn] When any of your opponent's Digimon attack, you may play 1 [ADR-01 Jeri] from this Digimon's digivolution cards without paying the cost. If you played, you may change the attack target to the Digimon played by this effect.";
        var jeri = Digimon(Uid("B64-JERI-"), 3, inherited: text, name: "ADR-01 Jeri", dp: 2000);
        AllSupported(jeri);
        Assert.EndsWith("등장시켰다면, 어택의 대상을 이 효과로 등장한 디지몬으로 변경할 수 있다.", Ko(text));
        var (state, engine) = NewMainPhase(memory: 5);
        var atk = Put(state.Player, Digimon("B64-ATK", 4, dp: 1000));
        var mother = Put(state.DummyOpponent, Digimon("B64-MDR", 6, name: "Mother D-Reaper", dp: 12000), false, jeri);
        mother.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        int sec = state.DummyOpponent.SecurityStack.Count;
        engine.AttackSecurityOrDirect(atk);
        Assert.Contains(state.DummyOpponent.BattleArea, d => d.TopCard.CardId == jeri.CardId);   // 진화원에서 등장
        Assert.Equal(sec, state.DummyOpponent.SecurityStack.Count);   // 어택 대상이 쥬리로 바뀌었다
        Assert.DoesNotContain(atk, state.Player.BattleArea);   // DP 2000에게 졌다
        Assert.Contains(state.ActionLogs, l => l.Contains("어택 대상을"));
    }

    // =====================================================================
    // 4. 수를 센 고르는 효과 (종합 룰 15-15-7-2 — 1개씩 골라 지정된 횟수만큼)
    // =====================================================================

    private sealed class PickOption : IChoiceProvider
    {
        public int Option;
        public int Asked;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) { Asked++; return Option; }
    }

    [Fact]
    public void EX13_077_ForEvery2Colors_ActivateOneEffect_RepeatedAndSameOptionAllowed()
    {
        var mon = Digimon(Uid("B64-OMM-"), 7, "[On Play] [When Digivolving] 1 of your Digimon may attack without suspending. Then, for every 2 of your Digimon and Tamers' colors, activate 1 effect below:\n・This Digimon may battle 1 of your opponent's Digimon.\n・By returning 5 cards from your opponent's trash to the bottom of the deck, ＜Recovery +1＞ (Place the top card of your deck as your top security card.)", dp: 15000);
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var pick = new PickOption { Option = 1 };
        engine.PlayerChoices = pick;
        var me = Put(state.Player, mon);
        Put(state.Player, Digimon("B64-RED", 5, color: CardColor.Red));
        Put(state.Player, Digimon("B64-YEL", 5, color: CardColor.Yellow));
        state.Player.Tamers.Add(Card("B64-TGR", "", CardType.Tamer, color: CardColor.Green));   // 파랑 · 빨강 · 노랑 · 초록 = 4색 → 2번
        for (int i = 0; i < 12; i++) state.DummyOpponent.Trash.Add(Digimon("B64-OT" + i, 3));
        int sec = state.Player.SecurityStack.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon, includeInherited: false);
        Assert.Equal(2, pick.Asked);
        Assert.Equal(sec + 2, state.Player.SecurityStack.Count);   // 같은 효과를 2번 (15-15-7-2)
        Assert.Equal(2, state.DummyOpponent.Trash.Count);
    }

    [Fact]
    public void EX4_073_ForEachTrashed_DeleteLowestCost_Then3TrashedTrashesSecurity()
    {
        var mon = Digimon(Uid("B64-ALTB-"), 7, "[When Attacking] By trashing up to 3 level 6 or higher cards in this Digimon's digivolution cards, for each card trashed, activate the effect below. Then, if you trashed 3 cards, trash the top 2 cards of your opponent's security stack.\n・Delete 1 of your opponent's Digimon or Tamers with the lowest play cost.", dp: 15000);
        AllSupported(mon);
        foreach (int lv6 in new[] { 3, 2 })
        {
            var (state, engine) = NewMainPhase();
            engine.PlayerChoices = new PickOption();
            var srcs = Enumerable.Range(0, lv6).Select(i => Digimon("B64-L6S" + i, 6)).Append(Digimon("B64-L5S", 5)).ToArray();
            var me = Put(state.Player, mon, false, srcs);
            var o1 = Put(state.DummyOpponent, Digimon("B64-O1", 3, playCost: 2));
            var o2 = Put(state.DummyOpponent, Digimon("B64-O2", 4, playCost: 5));
            var o3 = Put(state.DummyOpponent, Digimon("B64-O3", 6, playCost: 12));
            var tamer = Card("B64-OT", "", CardType.Tamer, playCost: 1);
            state.DummyOpponent.Tamers.Add(tamer);
            int sec = state.DummyOpponent.SecurityStack.Count;
            engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, mon, includeInherited: false);
            Assert.Single(me.EvolutionSources);   // Lv.6 이상만 버린다
            Assert.True(!state.DummyOpponent.Tamers.Contains(tamer), Logs(state));   // 가장 등장 코스트가 낮은 것부터 (테이머 1)
            Assert.DoesNotContain(o1, state.DummyOpponent.BattleArea);
            if (lv6 == 3)
            {
                Assert.DoesNotContain(o2, state.DummyOpponent.BattleArea);
                Assert.Contains(o3, state.DummyOpponent.BattleArea);
                Assert.Equal(sec - 2, state.DummyOpponent.SecurityStack.Count);   // 3장 버렸다 → 시큐리티 2장
            }
            else
            {
                Assert.Contains(o2, state.DummyOpponent.BattleArea);
                Assert.Equal(sec, state.DummyOpponent.SecurityStack.Count);
            }
        }
    }

    // =====================================================================
    // 5. EX12-047 되돌린 카드의 색 1색마다
    // =====================================================================

    [Fact]
    public void EX12_047_MinusDpForEachColorOfReturnedCards()
    {
        const string text = "[On Play] [When Digivolving] Delete 1 of your opponent's lowest DP Digimon. Then, by returning 2 cards from their trash to the bottom of the deck, for the turn, this Digimon gets +6000 DP and, to 1 of their Digimon, give -5000 DP for each of those returned cards' colors.";
        var mon = Digimon(Uid("B64-AMA-"), 6, text, dp: 11000);
        AllSupported(mon);
        Assert.EndsWith("이 턴 동안, 이 디지몬의 DP를 +6000 하고, 상대의 디지몬 1마리를 되돌린 카드의 색 1색마다 DP -5000 한다.", Ko(text));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var weak = Put(state.DummyOpponent, Digimon("B64-W", 3, dp: 2000));
        var big = Put(state.DummyOpponent, Digimon("B64-BIG", 7, dp: 20000));
        var multi = Digimon("B64-MULTI", 5, color: CardColor.Yellow);
        multi.SecondaryColor = CardColor.Blue;
        state.DummyOpponent.Trash.Add(Digimon("B64-TR", 4, color: CardColor.Red));
        state.DummyOpponent.Trash.Add(multi);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon, includeInherited: false);
        Assert.DoesNotContain(weak, state.DummyOpponent.BattleArea);
        Assert.Equal(17000, me.GetCurrentDP());
        Assert.Equal(20000 - 5000 * multi.AllColors.Concat(new[] { CardColor.Red }).Distinct().Count(), big.GetCurrentDP());   // 빨강 · 노랑 · 파랑
        Assert.Contains(weak.TopCard, state.DummyOpponent.Trash);
        Assert.Single(state.DummyOpponent.Trash);
    }

    // =====================================================================
    // 6. 그 밖의 문장 (EX10-032 · EX10-033 · BT7-064 · BT4-098) · "Digimon or Tamers" 소멸에 테이머도
    // =====================================================================

    [Fact]
    public void EX10_032_SuchDigimon_IsMineralOrRockTrait_GainsKeywordsAndDp()
    {
        const string text = "[On Play] [When Digivolving] [When Attacking] By trashing any 1 [Mineral] or [Rock] trait card from your Digimon's digivolution cards, 1 of your such Digimon gains ＜Collision＞ (During this Digimon's attack, all of your opponent's Digimon gain ＜Blocker＞, and must block if possible.), ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon and survives the battle, it performs any security checks it normally would.) and +3000 DP until your opponent's turn ends.";
        var mon = Digimon(Uid("B64-GOLEM-"), 5, text, dp: 7000, traits: "Rock");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var plain = Put(state.Player, Digimon("B64-PLAIN", 6, dp: 12000));
        var me = Put(state.Player, mon, false, Digimon("B64-RSRC", 4, traits: "Mineral"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon, includeInherited: false);
        Assert.Empty(me.EvolutionSources);
        Assert.Equal(10000, me.GetCurrentDP());
        Assert.True(CardScripts.HasKeyword(me, Keyword.Collision) && CardScripts.HasKeyword(me, Keyword.Piercing));
        Assert.Equal(12000, plain.GetCurrentDP());   // 특징이 없는 디지몬은 아니다
    }

    [Fact]
    public void EX10_033_ReducePlayCostOfOneOpponentDigimon_ByTwoPerTrashedCard()
    {
        const string text = "[When Digivolving] [When Attacking] By trashing up to 3 [Mineral] or [Rock] trait cards from any of your Digimon's digivolution cards, to 1 of your opponent's Digimon, reduce the play cost by 2 until their turn ends for each card trashed.";
        var mon = Digimon(Uid("B64-ROCK-"), 6, text, dp: 10000, traits: "Rock");
        AllSupported(mon);
        Assert.Contains("상대의 디지몬 1마리를 파기한 카드 1장마다 등장 코스트 -2 한다", Ko(text));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new PickOption();
        var me = Put(state.Player, mon, false, Digimon("B64-R1", 4, traits: "Rock"), Digimon("B64-R2", 4, traits: "Mineral"));
        var o = Put(state.DummyOpponent, Digimon("B64-O", 6, playCost: 10));
        var o2 = Put(state.DummyOpponent, Digimon("B64-O2", 3, playCost: 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon, includeInherited: false);
        Assert.Equal(6, o.PlayCost45);   // 2장 × 2 — 한 마리에
        Assert.Equal(3, o2.PlayCost45);
        state.TurnSequence += 2;
        engine.RecalculateFieldBuffs();
        Assert.Equal(10, o.PlayCost45);   // 상대의 턴 종료까지
    }

    [Fact]
    public void BT7_064_PlaceXAntibody_CantBeDeletedByEffects_DpNotReduced()
    {
        const string text = "[When Digivolving] You may place 1 black card with the [X Antibody] trait from your hand at the bottom of this Digimon's digivolution cards to prevent effects from deleting it or reducing its DP until the end of your opponent's next turn.";
        var mon = Digimon(Uid("B64-BWX-"), 6, text, color: CardColor.Black, dp: 12000);
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new PickOption();
        var me = Put(state.Player, mon);
        var x = Digimon("B64-XA", 4, color: CardColor.Black, traits: "X Antibody");
        state.Player.Hand.Add(x);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon, includeInherited: false);
        Assert.Contains(x, me.EvolutionSources);
        Assert.True(CardScripts.HasKeyword(me, Keyword.CannotBeDeletedByEffects));
        Assert.True(CardScripts.HasKeyword(me, Keyword.DpCannotBeReduced));
    }

    [Fact]
    public void BT4_098_DpSecurityAttackAndGrantedBlockedMemory()
    {
        const string text = "[Main] 1 of your Digimon with the [Hybrid] trait gets +3000 DP, ＜Security A. +1＞ (This Digimon checks 1 additional security card.) and \"[Your Turn] When this Digimon is blocked, gain +3 memory.\" for the turn.";
        var opt = Card(Uid("B64-SPIRIT-"), text);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var h = Put(state.Player, Digimon("B64-HY", 5, dp: 6000, traits: "Hybrid"));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(9000, h.GetCurrentDP());
        Assert.Equal(2, h.GetSecurityAttackCount());
        Assert.NotEmpty(h.GrantedEffects);
    }

    [Fact]
    public void DeleteDigimonOrTamers_HighestPlayCost_IncludesTamers()
    {
        var opt = Card(Uid("B64-DEL-"), "[Main] Delete 1 of your opponent's Digimon or Tamers with the highest play cost.");
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var d = Put(state.DummyOpponent, Digimon("B64-OD", 5, playCost: 5));
        var tamer = Card("B64-OTM", "", CardType.Tamer, playCost: 6);
        state.DummyOpponent.Tamers.Add(tamer);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.DoesNotContain(tamer, state.DummyOpponent.Tamers);   // 테이머도 후보 (카드에 적힌 대로)
        Assert.Contains(d, state.DummyOpponent.BattleArea);
    }

    private static string CardCatalogFor(string id) => id switch
    {
        // 영어 효과 문장 (카드 API — 엔진이 읽는 원문)
        "EX8-070" => "[Main] By trashing any 1 digivolution card of 1 of your Digimon with the [Mineral] or [Rock] trait, until the end of your opponent's turn, that Digimon gains ＜Collision＞ (During this Digimon's attack, all of your opponent's Digimon gain ＜Blocker＞, and must block if possible.), ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon and survives the battle, it performs any security checks it normally would.), ＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.), and your opponent's effects can't return it to hands or decks, and it gets +3000 DP.",
        _ => throw new ArgumentException(id),
    };
}
