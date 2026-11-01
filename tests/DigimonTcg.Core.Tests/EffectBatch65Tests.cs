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
/// v0.65.0 미지원 문장 39차 묶음: 종합 룰 6-6-4 (턴 종료 시에 메모리가 자신 쪽으로 돌아오면 턴이 이어진다 — 공식 Q&amp;A BT9-111 Q1) ·
/// ST13-02 · ST13-09 공개한 카드를 등장시킬 수 있다 · BT9-103 시큐리티에 추가 금지 (공식 Q&amp;A BT10-041 Q1) · EX10-058 테이머에게 효과 문장 · AI 비용 추천
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch65Tests
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
            state.Player.MainDeck.Add(Digimon("B65-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B65-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B65-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B65-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private sealed class Answers : IChoiceProvider
    {
        public bool Yes = true;
        public int Option { get; set; }
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => Yes;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => Option;
    }

    private const string EotGainOnce = "[End of Your Turn] [Once Per Turn] Gain 2 memory.";

    // =====================================================================
    // 1. 종합 룰 6-6-4 — 턴 종료 시에 메모리가 0 이상이 되면 턴 종료가 되지 않고 그 페이즈 그대로
    // =====================================================================

    [Fact]
    public void Rule664_Player_EndOfTurnGainsMemoryBack_TurnContinues_ThenPassEnds()
    {
        var (state, engine) = NewMainPhase(memory: 1);
        Put(state.Player, Digimon(Uid("B65-EOT-"), 5, EotGainOnce));
        int turn = state.CurrentTurn;
        var a = Digimon("B65-A", 3, playCost: 2);
        state.Player.Hand.Add(a);
        Assert.True(engine.PlayCard(a).CanExecute, Logs(state));   // 1 → 상대 쪽의 1 → 턴 종료 시 → +2 → 자신 쪽의 1
        Assert.Equal(turn, state.CurrentTurn);   // 턴 교대 없이 자신의 턴이 계속된다 (공식 Q&A BT9-111 Q1)
        Assert.Equal(Phase.Main, state.CurrentPhase);
        Assert.Equal(1, state.MemoryGauge);
        Assert.True(engine.TurnResumed65);
        Assert.True(state.ActionLogs.Any(l => l.Contains("6-6-4")), Logs(state));
        var r = engine.PassTurn();   // [턴에 1회] — 이번에는 메모리가 돌아오지 않는다
        Assert.True(r.CanExecute);
        Assert.Equal(turn + 1, state.CurrentTurn);
    }

    [Fact]
    public void Rule664_Player_PassWithEndOfTurnGain_StillOnOpponentSide_Ends()
    {
        var (state, engine) = NewMainPhase(memory: 4);
        Put(state.Player, Digimon(Uid("B65-EOT-"), 5, EotGainOnce));
        int turn = state.CurrentTurn;
        engine.PassTurn();   // 상대 쪽의 3 → +2 → 상대 쪽의 1 — 턴 종료
        Assert.Equal(turn + 1, state.CurrentTurn);
        Assert.False(engine.TurnResumed65);
    }

    [Fact]
    public void Rule664_Player_RepeatingEffect_IsCapped_AndTurnEnds()
    {
        var (state, engine) = NewMainPhase(memory: 0);
        Put(state.Player, Digimon(Uid("B65-EOTR-"), 5, "[End of Your Turn] Gain 4 memory."));   // 턴에 1회가 아니다 — 매번 돌아온다
        int turn = state.CurrentTurn;
        for (int i = 0; i < 20 && state.CurrentTurn == turn; i++) engine.PassTurn();
        Assert.Equal(turn + 1, state.CurrentTurn);   // 무한 반복 방지 (상한 10번)
    }

    [Fact]
    public void Rule664_WithTurnEndCondition3_EndOfTurnBackToOpponentSide1_Continues()
    {
        // BT14-081 턴 종료 조건이 상대 쪽의 3 이상인 동안: 턴 종료 시에 상대 쪽의 1이 되어도 조건을 만족하지 않는다 → 턴이 이어진다
        var (state, engine) = NewMainPhase(memory: 0);
        Put(state.Player, Digimon(Uid("B65-FEN-"), 6, "[Your Turn] Your turn end condition is the opponent having 3 or more memory.", name: "Fenriloogamon"));
        Put(state.Player, Digimon(Uid("B65-EOT-"), 5, EotGainOnce));
        engine.RecalculateFieldBuffs();
        int turn = state.CurrentTurn;
        engine.PassTurn();   // 상대 쪽의 3 → +2 → 상대 쪽의 1
        Assert.Equal(turn, state.CurrentTurn);
        Assert.Equal(-1, state.MemoryGauge);
        engine.PassTurn();
        Assert.Equal(turn + 1, state.CurrentTurn);
    }

    [Fact]
    public void Rule664_Ai_EndOfTurnGainsMemoryBack_AiKeepsActing()
    {
        var (state, engine) = NewMainPhase(memory: 0);
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        Put(state.DummyOpponent, Digimon(Uid("B65-AEOT-"), 5, "[End of Your Turn] [Once Per Turn] Gain 4 memory.", dp: 10000));
        var h = Digimon("B65-AIH", 4, playCost: 3, dp: 6000);
        engine.PassTurn();   // AI는 메모리 3으로 시작
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.True(state.ActionLogs.Any(l => l.Contains("6-6-4")), Logs(state));
        Assert.True(state.MemoryGauge >= 1, Logs(state));
        _ = h;
    }

    [Fact]
    public void Rule664_Ai_PassSetsMemoryBeforeEndOfTurnEffects()
    {
        // 종합 룰 6-5-1-7-1: 패스하면 즉시 상대 쪽의 3 — 【자신의 턴 종료 시】는 그 뒤 (전에는 AI만 효과 뒤에 3으로 덮어썼다)
        var (state, engine) = NewMainPhase(memory: 0);
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        Put(state.DummyOpponent, Digimon(Uid("B65-AEOT1-"), 3, "[End of Your Turn] Gain 1 memory.", dp: 10000));
        state.DummyOpponent.MainDeck.Clear();
        for (int i = 0; i < 5; i++) state.DummyOpponent.MainDeck.Add(Card("B65-ODUD" + i, "", CardType.Option, playCost: 9));   // 낼 수 없는 카드
        engine.PassTurn();
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.True(2 == state.MemoryGauge, Logs(state));   // 상대 쪽의 3 → +1 → 상대 쪽의 2 = 내 시작 메모리 2
    }

    // =====================================================================
    // 2. ST13-02 · ST13-09 — 놓는 것으로 덱 위 1장 공개, 조건에 맞으면 등장시킬 수 있다 (아니면 패에)
    // =====================================================================

    private const string St1302 = "[On Play] By placing this Digimon under 1 of your other Digimon that's black or has the [Legend-Arms] trait as its bottom digivolution card, you may reveal the top card of your deck. If that card is a Digimon card with the [Legend-Arms] trait and a play cost of 7 or less, you may play it without paying its memory cost. Add the rest to your hand.";

    [Fact]
    public void ST13_02_PlaceUnder_RevealLegendArms_Play_ElseHand()
    {
        var zuba = Digimon(Uid("B65-ZUBA-"), 3, St1302, name: "Zubamon", color: CardColor.Black, inherited: "[When Attacking] If you have a Digimon that's black or has the [Legend-Arms] trait in play, delete 1 of your opponent's Digimon with 3000 DP or less.");
        AllSupported(zuba);
        Assert.Equal("【등장 시】 이 디지몬을 특징으로 「Legend-Arms」를 가지거나 블랙인 자신의 다른 디지몬 1마리의 진화원 아래에 놓는 것으로, 자신의 덱 위에서부터 1장 오픈할 수 있다. 그 카드가 특징으로 「Legend-Arms」를 가진 코스트 7 이하인 디지몬 카드라면, 코스트를 지불하지 않고 등장시킬 수 있다. 나머지는 패에 추가한다.", Ko(St1302));
        foreach (var (legend, cost, yes, played) in new[] { (true, 7, true, true), (true, 8, true, false), (false, 3, true, false), (true, 5, false, false) })
        {
            var (state, engine) = NewMainPhase();
            engine.PlayerChoices = new Answers { Yes = yes };
            var host = Put(state.Player, Digimon("B65-HOST", 5, color: CardColor.Black));
            var me = Put(state.Player, zuba);
            var top = legend ? Digimon(Uid("B65-LA-"), 5, playCost: cost, traits: "Legend-Arms") : Digimon(Uid("B65-NLA-"), 5, playCost: cost);
            state.Player.MainDeck.Insert(0, top);
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, zuba, includeInherited: false);
            if (!yes) { Assert.Contains(me, state.Player.BattleArea); continue; }   // 놓지 않으면 공개도 없다 ("you may")
            Assert.DoesNotContain(me, state.Player.BattleArea);
            Assert.Contains(zuba, host.EvolutionSources);
            Assert.Equal(played, state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, top)));
            Assert.Equal(!played, state.Player.Hand.Contains(top));
        }
    }

    [Fact]
    public void ST13_02_DeclinePlay_GoesToHand()
    {
        var zuba = Digimon(Uid("B65-ZUBA-"), 3, St1302, name: "Zubamon", color: CardColor.Black);
        var (state, engine) = NewMainPhase();
        var answers = new QueueAnswers(true, false);   // 비용은 낸다 · 등장시키지 않는다
        engine.PlayerChoices = answers;
        Put(state.Player, Digimon("B65-HOST", 5, color: CardColor.Black));
        var me = Put(state.Player, zuba);
        var top = Digimon(Uid("B65-LA-"), 5, playCost: 5, traits: "Legend-Arms");
        state.Player.MainDeck.Insert(0, top);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, zuba, includeInherited: false);
        Assert.Contains(top, state.Player.Hand);   // "Add the rest to your hand"
    }

    private sealed class QueueAnswers : IChoiceProvider
    {
        private readonly Queue<bool> _yes;
        public QueueAnswers(params bool[] yes) => _yes = new Queue<bool>(yes);
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => _yes.Count > 0 ? _yes.Dequeue() : recommended;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    // =====================================================================
    // 3. BT9-103 — 상대의 효과로는 시큐리티에 카드를 추가할 수 없다 (상대의 턴 종료까지)
    // =====================================================================

    private const string Bt9103 = "[Main] Until the end of your opponent's turn, your opponent's Digimon with play costs of 7 or less can't attack players, and cards can't be added to security stacks by your opponent's effects.";

    [Fact]
    public void BT9_103_OpponentEffectsCantAddSecurity_OwnCan_ExpiresAfterOpponentTurn()
    {
        var kongou = Card(Uid("B65-KONG-"), Bt9103, inherited: "Security Effect [Security] Activate this card's [Main] effects.");
        AllSupported(kongou);
        Assert.Equal("【메인】 상대의 턴 종료 시까지, 등장 코스트 7 이하인 상대의 디지몬 전부는 플레이어에게 어택할 수 없고, 상대의 효과로 시큐리티에 카드를 추가할 수 없다.", Ko(Bt9103));
        var (state, engine) = NewMainPhase();
        var foe = Put(state.DummyOpponent, Digimon("B65-FOE", 4, playCost: 5));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, kongou, includeInherited: false);
        Assert.False(EffectRules.CanAttackPlayer(foe));
        // 상대의 ≪리커버리≫ — 덱의 카드는 그대로
        var rec = Digimon(Uid("B65-REC-"), 4, "[On Play] ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)");
        var recInst = Put(state.DummyOpponent, rec);
        int oppSec = state.DummyOpponent.SecurityStack.Count, oppDeck = state.DummyOpponent.MainDeck.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, recInst, rec, includeInherited: false);
        Assert.Equal(oppSec, state.DummyOpponent.SecurityStack.Count);
        Assert.Equal(oppDeck, state.DummyOpponent.MainDeck.Count);
        // 상대의 효과로 내 디지몬을 내 시큐리티에 — 놓을 수 없다
        var mine = Put(state.Player, Digimon("B65-MINE", 4));
        var bounce = Card(Uid("B65-BOUNCE-"), "[Main] Place 1 of your opponent's Digimon at the top of their security stack.");
        int mySec = state.Player.SecurityStack.Count;
        engine.RunEffects(EffectTrigger.Main, state.DummyOpponent, null, bounce, includeInherited: false);
        Assert.Contains(mine, state.Player.BattleArea);
        Assert.Equal(mySec, state.Player.SecurityStack.Count);
        // 내 효과는 막지 않는다
        var myRec = Put(state.Player, rec);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, myRec, rec, includeInherited: false);
        Assert.Equal(mySec + 1, state.Player.SecurityStack.Count);
        // 상대의 턴이 끝나면 풀린다
        state.TurnSequence += 2;
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, recInst, rec, includeInherited: false);
        Assert.Equal(oppSec + 1, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void BT9_103_OptionThatWouldGoToSecurity_IsTrashed_QA_BT10_041()
    {
        var (state, engine) = NewMainPhase();
        state.Player.NoSecurityAdd65 = state.TurnSequence;   // 이 턴 동안 플레이어의 효과로는 시큐리티에 추가할 수 없다
        Put(state.Player, Digimon("B65-BLUE", 4));   // 옵션 색 조건
        var oldBottom = state.Player.SecurityStack.Last();
        var opt = Card(Uid("B65-SECOPT-"), "[Main] Add your bottom security card to the hand and place this card face up as the bottom security card.", playCost: 0);
        state.Player.Hand.Add(opt);
        int sec = state.Player.SecurityStack.Count;
        Assert.True(engine.PlayCard(opt).CanExecute, Logs(state));
        Assert.Contains(oldBottom, state.Player.Hand);
        Assert.Equal(sec - 1, state.Player.SecurityStack.Count);   // 놓을 수 없다
        Assert.Contains(opt, state.Player.Trash);   // 공식 Q&A BT10-041 Q1 — 버려진다
    }

    // =====================================================================
    // 4. EX10-058 — 상대의 디지몬 또는 테이머에게 효과 문장을 준다
    // =====================================================================

    private const string Ex10058 = "[On Play] [When Digivolving] Until your opponent's turn ends, give 1 of their Digimon or Tamers \"[End of Your Turn] Delete 1 of your Digimon.\"";

    [Fact]
    public void EX10_058_GiveToTamer_OwnerDeletesOwnDigimonAtTheirEndOfTurn()
    {
        var lil = Digimon(Uid("B65-LILI-"), 6, Ex10058, name: "Lilithmon", color: CardColor.Purple);
        AllSupported(lil);
        Assert.Equal("【등장 시】 【진화 시】 상대의 턴 종료 시까지 상대의 디지몬 또는 테이머 1마리에게 「【자신의 턴 종료 시】 자신의 디지몬 1마리를 소멸시킨다.」의 효과를 준다.", Ko(Ex10058));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, lil);
        var tamer = Card(Uid("B65-OTAM-"), "", CardType.Tamer);
        state.DummyOpponent.Tamers.Add(tamer);
        var foe = Put(state.DummyOpponent, Digimon("B65-FOE", 5));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, lil, includeInherited: false);   // 추천: 테이머
        Assert.Single(state.DummyOpponent.TamerGrants65);
        Assert.Empty(foe.GrantedEffects);
        state.TurnSequence++;   // 상대의 턴
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.DummyOpponent, null, tamer, includeInherited: false);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);   // 그 테이머의 주인이 자신의 디지몬 1마리를 소멸
        // 기간이 끝나면 없다
        state.TurnSequence += 2;
        var foe2 = Put(state.DummyOpponent, Digimon("B65-FOE2", 5));
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.DummyOpponent, null, tamer, includeInherited: false);
        Assert.Contains(foe2, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void EX10_058_NoTamer_GivesToDigimon_AndTamerLeavingForgetsGrant()
    {
        var lil = Digimon(Uid("B65-LILI-"), 6, Ex10058, name: "Lilithmon", color: CardColor.Purple);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, lil);
        var foe = Put(state.DummyOpponent, Digimon("B65-FOE", 5));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, lil, includeInherited: false);
        Assert.Single(foe.GrantedEffects);
        var (state2, engine2) = NewMainPhase();
        var me2 = Put(state2.Player, lil);
        var tamer = Card(Uid("B65-OTAM-"), "", CardType.Tamer);
        state2.DummyOpponent.Tamers.Add(tamer);
        engine2.RunEffects(EffectTrigger.OnPlay, state2.Player, me2, lil, includeInherited: false);
        state2.DummyOpponent.Tamers.Remove(tamer);
        engine2.RecalculateFieldBuffs();
        Assert.Empty(state2.DummyOpponent.TamerGrants65);
    }

    // =====================================================================
    // 5. AI 비용 추천
    // =====================================================================

    [Fact]
    public void EX12_047_ReturnCost_RecommendsCardsAddingMostColors()
    {
        var order = SentenceParser.ColorGreedyOrder65(new[]
        {
            Digimon("B65-R1", 6, color: CardColor.Red),
            Digimon("B65-R2", 7, color: CardColor.Red),
            Card("B65-BG", "", CardType.Digimon, 5, color: CardColor.Blue),
            Digimon("B65-Y", 3, color: CardColor.Yellow),
        });
        Assert.Equal(3, order.Take(3).SelectMany(x => x.AllColors).Distinct().Count());
        Assert.Equal("B65-R2", order[0].CardId);   // 같은 수면 Lv.가 높은 것
    }

    [Fact]
    public void HandToSources_LowHand_PaysOnlyWhenWorthIt()
    {
        const string text = "[When Digivolving] You may place 1 black card with the [X Antibody] trait from your hand at the bottom of this Digimon's digivolution cards to prevent effects from deleting it or reducing its DP until the end of your opponent's next turn.";
        foreach (var (lv, srcs, pays) in new[] { (5, 1, false), (6, 1, true), (5, 4, true) })
        {
            var mon = Digimon(Uid("B65-DORU-"), lv, text, color: CardColor.Black);
            var (state, engine) = NewMainPhase();
            var me = Put(state.Player, mon, false, Enumerable.Range(0, srcs).Select(i => Digimon("B65-S" + i, 3)).ToArray());
            state.Player.Hand.Add(Card(Uid("B65-XA-"), "", CardType.Digimon, 5, color: CardColor.Black, traits: "X Antibody"));
            state.Player.Hand.Add(Digimon("B65-H2", 4));
            engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon, includeInherited: false);   // 패 2장 — 추천 답 그대로
            Assert.Equal(pays, me.EvolutionSources.Count == srcs + 1);
        }
    }

    // =====================================================================
    // 6. 그 밖의 문장 — BT13-092 · BT6-078 · P-116
    // =====================================================================

    [Fact]
    public void BT13_092_ReturnCardFromOpponentTrash_DeleteAllSameName()
    {
        const string text = "[When Attacking] By returning 1 Digimon card from your opponent's trash to the bottom of their deck, delete all of your opponent's Digimon with the same name as that card.";
        var mon = Digimon(Uid("B65-ZAMIEL-"), 6, text);
        AllSupported(mon);
        Assert.Equal("【어택 시】 상대의 트래시에서 디지몬 카드 1장을 덱 아래로 되돌리는 것으로, 그 카드와 같은 명칭을 가진 상대의 디지몬을 전부 소멸시킨다.", Ko(text));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var a1 = Put(state.DummyOpponent, Digimon("B65-AG1", 3, name: "Agumon"));
        var a2 = Put(state.DummyOpponent, Digimon("B65-AG2", 3, name: "Agumon"));
        var other = Put(state.DummyOpponent, Digimon("B65-GB", 4, name: "Greymon"));
        state.DummyOpponent.Trash.Add(Digimon("B65-T-GAB", 6, name: "Gabumon"));
        state.DummyOpponent.Trash.Add(Digimon("B65-T-AG", 3, name: "Agumon"));   // 추천: 상대의 디지몬과 같은 명칭
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, mon, includeInherited: false);
        Assert.DoesNotContain(a1, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(a2, state.DummyOpponent.BattleArea);
        Assert.Contains(other, state.DummyOpponent.BattleArea);
        Assert.Equal("B65-T-AG", state.DummyOpponent.MainDeck.Last().CardId);
    }

    [Fact]
    public void BT6_078_TrashedFromHandByOwnEffect_PlaceUnderPurpleDigimon()
    {
        const string text = "When you trash this card in your hand using one of your effects, you may place it under 1 of your purple Digimon at the bottom of its digivolution cards.";
        var skull = Digimon(Uid("B65-SKULL-"), 5, text, color: CardColor.Purple);
        AllSupported(skull);
        Assert.Equal("자신의 효과로 이 카드가 패에서 파기되었을 때, 이 카드를 퍼플인 자신의 디지몬 1마리의 진화원 아래에 놓을 수 있다.", Ko(text));
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Digimon("B65-PURP", 5, color: CardColor.Purple));
        state.Player.Hand.Add(skull);
        var disc = Card(Uid("B65-DISC-"), "[Main] Trash 1 card in your hand.", playCost: 0, color: CardColor.Purple);
        state.Player.Hand.Add(disc);
        Assert.True(engine.PlayCard(disc).CanExecute, Logs(state));
        Assert.Contains(skull, host.EvolutionSources);
        Assert.DoesNotContain(skull, state.Player.Trash);
    }

    [Fact]
    public void P_116_CostsZero_WhileAllThreeNamesInPlay()
    {
        const string text = "While you have [Agumon], [Pulsemon] and [Gammamon], this card costs 0.";
        var opt = Card(Uid("B65-P116-"), text + "\n[Main] Gain 1 memory.", playCost: 2, color: CardColor.Red);
        AllSupported(opt);
        Assert.Equal("자신의 필드에 「아구몬」, 「펄스몬」, 「감마몬」이 모두 있는 동안, 이 카드의 사용 코스트는 0이 된다.", Ko(text));
        var (state, _) = NewMainPhase();
        Put(state.Player, Digimon("B65-AGU", 3, name: "Agumon", color: CardColor.Red));
        Put(state.Player, Digimon("B65-PUL", 3, name: "Pulsemon"));
        Assert.Equal(2, CardScripts.EffectivePlayCost(state.Player, opt, state.DummyOpponent));
        Put(state.Player, Digimon("B65-GAM", 3, name: "Gammamon"));
        Assert.Equal(0, CardScripts.EffectivePlayCost(state.Player, opt, state.DummyOpponent));
    }
}
