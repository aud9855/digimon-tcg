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
/// v0.62.0 미지원 문장 36차 묶음: BT7-055가 주는 "액티브가 되려 할 때" · EX6-031 ≪시큐리티 어택 -≫ → + · 효과 문장을 주는 옵션 (BT4-101 · BT13-108 · BT9-102) ·
/// BT8-059 진화 조건을 무시할 수 없다 · 겹쳐진 카드 보호를 진화원 파기 한 곳에서 · AI 어택 판단 (효과로 받은 【어택 시】 손해)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch62Tests
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
            state.Player.MainDeck.Add(Digimon("B62-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B62-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B62-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B62-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    // =====================================================================
    // 1. BT7-055가 주는 "액티브가 되려 할 때" (공식 Q&A BT7-055 Q1)
    // =====================================================================

    private const string Ebon = "[Opponent's Turn] All of your opponent's Digimon gain \"[Your Turn] When this Digimon would unsuspend, you must trash 1 card in your hand or prevent this Digimon from unsuspending.\"";

    [Fact]
    public void WouldUnsuspend_PayPerDigimon_OrStaySuspended_BT7_055()
    {
        var ebon = Digimon(Uid("B62-EBON-"), 6, Ebon);
        AllSupported(ebon);
        Assert.Contains("이 디지몬이 액티브가 될 때, 자신의 패 1장을 파기하거나, 이 디지몬을 액티브로 하지 않는다", Ko(Ebon));
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, ebon);
        var a = Put(state.Player, Digimon("B62-A", 5), suspended: true);
        var b = Put(state.Player, Digimon("B62-B", 5), suspended: true);
        var hand = Digimon("B62-H", 3);
        state.Player.Hand.Add(hand);   // 패 1장 — 1마리만 액티브로 할 수 있다 (1마리마다 1장)
        engine.ExecuteActivePhase();
        Assert.Empty(state.Player.Hand);
        Assert.Contains(hand, state.Player.Trash);
        Assert.Equal(1, new[] { a, b }.Count(d => !d.IsSuspended));
        Assert.Equal(1, new[] { a, b }.Count(d => d.IsSuspended));
    }

    [Fact]
    public void WouldUnsuspend_EffectUnsuspend_NoHand_Prevented_AndOnlyOnGrantedSide()
    {
        var ebon = Digimon(Uid("B62-EBON-"), 6, Ebon);
        var (state, engine) = NewMainPhase();
        var e = Put(state.DummyOpponent, ebon);
        e.IsOwnersTurn = false;   // 플레이어의 턴 = 현무몬의 상대의 턴
        var mine = Put(state.Player, Digimon("B62-M", 4), suspended: true);
        engine.RecalculateFieldBuffs();
        Assert.Single(mine.AuraGranted61);
        Assert.Empty(e.AuraGranted61);
        engine.Unsuspend(mine);   // 패가 없다 → 액티브가 되지 않는다
        Assert.True(mine.IsSuspended, Logs(state));
        state.Player.Hand.Add(Digimon("B62-H1", 3));
        state.Player.Hand.Add(Digimon("B62-H2", 3));
        engine.Unsuspend(mine);   // 패 2장 → 1장 파기하고 액티브 (추천)
        Assert.False(mine.IsSuspended);
        Assert.Single(state.Player.Hand);
        // 현무몬 쪽 디지몬은 받지 않는다
        e.IsSuspended = true;
        engine.Unsuspend(e);
        Assert.False(e.IsSuspended);
    }

    // =====================================================================
    // 2. EX6-031 ≪시큐리티 어택 -≫ → ≪시큐리티 어택 +≫ (효과마다 부호)
    // =====================================================================

    private const string Flip = "[Your Turn] Change ＜Security A. -＞ to ＜Security A. +＞ on all of your Digimon.";

    [Fact]
    public void SecurityMinusBecomesPlus_PerEffectSign_EX6_031()
    {
        var shaka = Digimon(Uid("B62-SHAKA-"), 7, Flip);
        AllSupported(shaka);
        Assert.Contains("자신의 디지몬 전부의 《시큐리티 어택 -》을 《시큐리티 어택 +》로 한다", Ko(Flip));
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B62-D", 5));
        engine.RecalculateFieldBuffs();
        engine.AddTimedSa60(d, -1, state.TurnSequence + 1);
        engine.AddTimedSa60(d, +1, state.TurnSequence);
        engine.RecalculateFieldBuffs();
        Assert.Equal(1, d.GetSecurityAttackCount());   // -1 + 1 → 1장
        var s = Put(state.Player, shaka);
        engine.RecalculateFieldBuffs();
        Assert.Equal(3, d.GetSecurityAttackCount());   // -1 → +1, +1 그대로 → 3장 (합계로 보면 0이지만 효과마다 부호를 본다)
        engine.AddAttackSa60(d, -1);   // 이 어택 동안 -1도 +1로
        Assert.Equal(4, d.GetSecurityAttackCount());
        foreach (var x in state.Player.BattleArea) x.IsOwnersTurn = false;   // 【자신의 턴】만
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, d.GetSecurityAttackCount());
    }

    // =====================================================================
    // 3. 효과 문장을 주는 옵션 (BT4-101 · BT13-108 · BT9-102)
    // =====================================================================

    private const string Drag = "[Main] All of your Digimon gain \"[Your Turn] When attacking an opponent's Digimon with no digivolution cards, delete that Digimon.\" for the turn.";

    [Fact]
    public void AllYourDigimonGainDeleteSourcelessOnAttack_LaterOnesToo_BT4_101()
    {
        var opt = Card(Uid("B62-DRAG-"), Drag);
        AllSupported(opt);
        Assert.Contains("이 턴 동안 자신의 디지몬 전부는 「【자신의 턴】 이 디지몬이 진화원을 갖지 않은 상대의 디지몬에게 어택했을 때, 그 디지몬을 소멸시킨다.」의 효과를 얻는다", Ko(Drag));
        var (state, engine) = NewMainPhase();
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        var atk = Put(state.Player, Digimon("B62-ATK", 4, dp: 1000));   // 효과를 쓴 뒤에 나온 디지몬도 (공식 Q&A BT9-102 Q1과 같은 "전부")
        var big = Put(state.DummyOpponent, Digimon("B62-BIG", 6, dp: 12000), suspended: true);
        engine.RecalculateFieldBuffs();
        Assert.Single(atk.AuraGranted61);
        engine.AttackOpponentDigimon(atk, big);
        Assert.True(!state.DummyOpponent.BattleArea.Contains(big), Logs(state));   // 배틀이 아니라 효과로 소멸 (공식 Q&A BT4-101 Q1)
        Assert.Contains(atk, state.Player.BattleArea);
        state.TurnSequence++;   // 이 턴이 끝나면 없어진다
        engine.RecalculateFieldBuffs();
        Assert.Empty(atk.AuraGranted61);
    }

    private const string Waltz = "[Main] Until the end of your opponent's turn, 1 of your Digimon gains \"[Opponent's Turn] When this Digimon becomes suspended, delete all of your opponent's Digimon with a play cost less than or equal to this Digimon\" and \"[Opponent's Turn] This Digimon isn't affected by your opponent's Option cards.\"";

    [Fact]
    public void OneDigimonGainsTriggerAndOptionImmunity_OnOpponentsTurn_BT13_108()
    {
        var opt = Card(Uid("B62-WALTZ-"), Waltz);
        AllSupported(opt);
        Assert.Contains("」와 「【상대의 턴】 이 디지몬은 상대의 옵션 카드의 효과를 받지 않는다.」의 효과를 얻는다", Ko(Waltz));
        var (state, engine) = NewMainPhase();
        // 상대(AI)가 자신의 턴에 쓴 것으로 — 지금(플레이어의 턴)이 그 디지몬의 【상대의 턴】
        var d = Put(state.DummyOpponent, Digimon("B62-W", 5, playCost: 6));
        engine.RunEffects(EffectTrigger.Main, state.DummyOpponent, null, opt, chosenTarget: d, includeInherited: false);
        Assert.Equal(2, d.GrantedEffects.Count);
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(d, Keyword.UnaffectedByOpponentOptionEffects));   // 그 디지몬의 주인의 턴에는 아니다
        d.IsOwnersTurn = false;   // 상대의 턴
        var cheap = Put(state.Player, Digimon("B62-C", 4, playCost: 5));
        var costly = Put(state.Player, Digimon("B62-X", 6, playCost: 9));
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(d, Keyword.UnaffectedByOpponentOptionEffects));
        var del = Card(Uid("B62-DEL-"), "[Main] Delete 1 of your opponent's Digimon.");
        engine.RunEffects(EffectTrigger.Main, state.Player, null, del, chosenTarget: d, includeInherited: false);
        Assert.Contains(d, state.DummyOpponent.BattleArea);   // 상대의 옵션 카드의 효과를 받지 않는다
        engine.Suspend(d);   // 레스트 상태가 되었을 때 — 이 디지몬의 등장 코스트 이하인 상대의 디지몬 전부
        Assert.DoesNotContain(cheap, state.Player.BattleArea);
        Assert.Contains(costly, state.Player.BattleArea);
    }

    private const string Heavy = "[Main] You may trash 1 card with the [Cyborg] or [Machine] trait from your hand to have all of your level 6 Digimon with the [Machine] trait gain ＜Rush＞ (This Digimon can attack the turn it comes into play.) and \"[On Play] If this Digimon has a digivolution card, ＜Blitz＞\"\" for the turn.";

    [Fact]
    public void TrashToGiveLevel6MachineRushAndOnPlay_ForTheTurn_BT9_102()
    {
        var opt = Card(Uid("B62-HEAVY-"), Heavy);
        AllSupported(opt);
        Assert.Contains("《속공》 (이 디지몬은 등장한 턴에도 어택할 수 있다)과 「【등장 시】 이 디지몬의 진화원이 1장 이상일 때, 《진격》", Ko(Heavy));
        var (state, engine) = NewMainPhase();
        var cost = Digimon("B62-CYB", 3, traits: new[] { "Cyborg" });
        state.Player.Hand.Add(cost);
        var lv5 = Put(state.Player, Digimon("B62-L5", 5, traits: new[] { "Machine" }));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Contains(cost, state.Player.Trash);
        var lv6 = Put(state.Player, Digimon("B62-L6", 6, traits: new[] { "Machine" }));   // 나중에 나온 디지몬도 (공식 Q&A BT9-102 Q1)
        var other = Put(state.Player, Digimon("B62-L6O", 6, traits: new[] { "Dragon" }));
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(lv6, Keyword.Rush));
        Assert.Single(lv6.AuraGranted61);
        Assert.False(CardScripts.HasKeyword(lv5, Keyword.Rush));
        Assert.False(CardScripts.HasKeyword(other, Keyword.Rush));
        state.TurnSequence++;
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(lv6, Keyword.Rush));
    }

    [Fact]
    public void TrashToGive_NoMatchingHandCard_NothingHappens_BT9_102()
    {
        var opt = Card(Uid("B62-HEAVY-"), Heavy);
        var (state, engine) = NewMainPhase();
        state.Player.Hand.Add(Digimon("B62-NOPE", 3, traits: new[] { "Dragon" }));
        var lv6 = Put(state.Player, Digimon("B62-L6", 6, traits: new[] { "Machine" }));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(lv6, Keyword.Rush));
        Assert.Single(state.Player.Hand);
    }

    // =====================================================================
    // 4. BT8-059 서로는 진화 조건을 무시할 수 없다 (공식 Q&A BT8-059 Q1)
    // =====================================================================

    private const string NoIgnore = "[All Turns] Players can't ignore digivolution requirements.";

    [Fact]
    public void CantIgnoreDigivolutionRequirements_BothPlayers_BT8_059()
    {
        var koku = Digimon(Uid("B62-KOKU-"), 3, NoIgnore);
        AllSupported(koku);
        Assert.Contains("서로는 진화 조건을 무시할 수 없다", Ko(NoIgnore));
        var special = Digimon(Uid("B62-SPC-"), 6, "Your [B62-BASE] can digivolve into this card in your hand for a memory cost of 4, ignoring this card's digivolution requirements.", color: CardColor.Red);
        AllSupported(special);
        var (state, engine) = NewMainPhase();
        var basis = Put(state.Player, Digimon("B62-BASE", 3, color: CardColor.Green));
        engine.RecalculateFieldBuffs();
        Assert.True(ActionValidator.ValidateEvolution(basis, special).CanExecute);   // 진화 조건을 무시하는 진화
        var k = Put(state.DummyOpponent, koku);   // 상대의 코쿠와몬도 (서로)
        engine.RecalculateFieldBuffs();
        Assert.True(basis.NoIgnoreReq62);
        Assert.False(ActionValidator.ValidateEvolution(basis, special).CanExecute);
        // 효과로 진화 조건을 무시하는 진화도 할 수 없다 (진화 조건에 맞는 카드는 된다)
        var odd = Digimon("B62-ODD", 6, color: CardColor.Purple);
        engine.EvolveByEffect(state.Player, basis, odd, 0, new List<CardData> { odd });
        Assert.Equal("B62-BASE", basis.TopCard.CardId);
        var normal = Digimon("B62-NORM", 4, color: CardColor.Green);
        var from = new List<CardData> { normal };
        engine.EvolveByEffect(state.Player, basis, normal, 0, from);
        Assert.Equal("B62-NORM", basis.TopCard.CardId);
        state.DummyOpponent.BattleArea.Remove(k);
        engine.RecalculateFieldBuffs();
        Assert.False(basis.NoIgnoreReq62);
    }

    // =====================================================================
    // 5. 겹쳐진 카드 보호를 진화원 파기 한 곳에서 (공식 Q&A BT21-060 Q1)
    // =====================================================================

    private const string Replace = "[Opponent's Turn] When an effect would trash one of your other Digimon's digivolution cards, you may trash this Digimon's digivolution cards with that effect instead.";

    [Fact]
    public void ReplacementDigimonWithStackProtection_NothingTrashed()
    {
        var tacti = Digimon(Uid("B62-TAC-"), 6, Replace + "\n[All Turns] Your opponent's effects can't trash this Digimon's stacked cards.");
        AllSupported(tacti);
        var trasher = Digimon(Uid("B62-TR-"), 5, "[On Play] Trash the bottom 1 digivolution card of 1 of your opponent's Digimon.");
        var (state, engine) = NewMainPhase();
        var a = Digimon("B62-A", 3);
        var host = Put(state.Player, Digimon("B62-HOST", 6), false, a);
        var tx = Digimon("B62-TX", 5);
        var ty = Digimon("B62-TY", 4);
        var tac = Put(state.Player, tacti, false, tx, ty);
        host.IsOwnersTurn = tac.IsOwnersTurn = false;
        var t = Put(state.DummyOpponent, trasher);
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(tac, Keyword.StackProtectedFromOpponent));
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, t, trasher, chosenTarget: host);
        Assert.Contains(a, host.EvolutionSources);   // 대신했다
        Assert.Equal(2, tac.EvolutionSources.Count);   // 대신한 디지몬의 겹쳐진 카드는 상대의 효과로 파기되지 않는다
    }

    [Fact]
    public void BuiltInBottomTrash_RespectsStackProtection()
    {
        var (state, engine) = NewMainPhase();
        var a = Digimon("B62-A", 3);
        var prot = Put(state.DummyOpponent, Digimon(Uid("B62-PROT-"), 5, "[All Turns] Your opponent's effects can't trash this Digimon's stacked cards."), false, a);
        var jude = Digimon(Uid("B62-J-"), 5, "[On Play] Trash the bottom 1 digivolution card of 1 of your opponent's Digimon.");
        var j = Put(state.Player, jude);
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, j, jude, chosenTarget: prot);
        Assert.Contains(a, prot.EvolutionSources);
        var removed = engine.TrashSourcesAt61(state.DummyOpponent, prot, 1, top: false);   // 효과 밖 (룰 처리)은 보호와 관계없다
        Assert.Single(removed);
    }

    // =====================================================================
    // 6. AI — 효과로 받은 【어택 시】 손해
    // =====================================================================

    [Fact]
    public void Ai_GrantedLoseMemoryOnAttack_IsAPenalty_BT5_091()
    {
        var takumi = Card(Uid("B62-TAK-"), "[All Turns] All level 3 Digimon gain \"[When Attacking] Lose 1 memory.\"", CardType.Tamer);
        AllSupported(takumi);
        var (state, engine) = NewMainPhase();
        var l3 = Put(state.DummyOpponent, Digimon("B62-AI3", 3));
        var l5 = Put(state.DummyOpponent, Digimon("B62-AI5", 5));
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, SmartAiPolicy.GrantedAttackPenalty62(engine, l3, 3));
        state.Player.Tamers.Add(takumi);
        engine.RecalculateFieldBuffs();
        double p = SmartAiPolicy.GrantedAttackPenalty62(engine, l3, 3);
        Assert.True(p > 0 && p < 1.0, p + " aura=" + l3.AuraGranted61.Count + " " + string.Join(";", l3.AuraGranted61.Select(x => x.EffectText)));
        Assert.True(SmartAiPolicy.GrantedAttackPenalty62(engine, l3, 0) > 3.0);   // 메모리가 상대 쪽으로 넘어가면 턴이 끝난다
        Assert.Equal(0, SmartAiPolicy.GrantedAttackPenalty62(engine, l5, 3));
    }

    // =====================================================================
    // 7. 그 밖의 문장 (EX2-037 · BT4-090 · EX13-035)
    // =====================================================================

    private const string Reaper = "[Opponent's Turn] [Once Per Turn] When an opponent's Digimon becomes unsuspended, ＜De-Digivolve 1＞ 1 of those Digimon. (Trash the top card. You can't trash past level 3 cards.)";

    [Fact]
    public void OpponentDigimonUnsuspended_DeDigivolveThatOne_OncePerTurn_EX2_037()
    {
        var reaper = Digimon(Uid("B62-REAP-"), 6, Reaper);
        AllSupported(reaper);
        Assert.Contains("상대의 디지몬이 액티브가 되었을 때, 그 디지몬 1마리를 《퇴화 1》", Ko(Reaper));
        var (state, engine) = NewMainPhase();
        var r = Put(state.DummyOpponent, reaper);
        r.IsOwnersTurn = false;   // 플레이어의 턴 = 리퍼몬의 상대의 턴
        var a3 = Digimon("B62-A3", 3);
        var a4 = Digimon("B62-A4", 4);
        var other = Put(state.Player, Digimon("B62-OTHER", 5), true, a4, a3);
        var target = Put(state.Player, Digimon("B62-TGT", 5), true, a4, a3);
        engine.RecalculateFieldBuffs();
        engine.Unsuspend(target);
        Assert.False(target.IsSuspended);
        Assert.Equal("B62-A4", target.TopCard.CardId);   // 액티브가 된 그 디지몬을 ≪퇴화 1≫
        Assert.Equal("B62-OTHER", other.TopCard.CardId);
        engine.Unsuspend(other);   // [턴에 1회]
        Assert.Equal("B62-OTHER", other.TopCard.CardId);
    }

    [Fact]
    public void UnsuspendThenMayAttackUnsuspended_BT4_090()
    {
        const string text = "[When Digivolving] Unsuspend this Digimon. Then, it can attack your opponent's Digimon. This effect allows you to attack unsuspended Digimon as well.";
        var chaos = Digimon(Uid("B62-CHAOS-"), 7, text);
        AllSupported(chaos);
        Assert.Contains("이 효과로는 액티브 상태인 상대의 디지몬에게도 어택할 수 있다", Ko(text));
    }

    private const string KingEtemon = "[All Turns] While there are 3 or more Digimon with [Sukamon] or [Etemon] in their names, give all of your opponent's Digimon ＜Security A. -1＞ (This Digimon checks 1 fewer security card.) and -3000 DP.";

    [Fact]
    public void BothSidesSukamonCount_GivesOpponentMinus_EX13_035()
    {
        var king = Digimon(Uid("B62-KE-"), 6, KingEtemon, name: "KingEtemon");
        AllSupported(king);
        Assert.Contains("명칭에 「스카몬」/「에테몬」을 포함하는 디지몬이 3마리 이상 있는 동안", Ko(KingEtemon));
        var (state, engine) = NewMainPhase();
        Put(state.Player, king);
        Put(state.Player, Digimon("B62-SK1", 3, name: "Sukamon"));
        var opp = Put(state.DummyOpponent, Digimon("B62-OPP", 5, dp: 6000));
        engine.RecalculateFieldBuffs();
        Assert.Equal(6000, opp.GetCurrentDP());   // 2마리
        Put(state.DummyOpponent, Digimon("B62-SK2", 3, name: "Sukamon", dp: 5000));   // 상대의 디지몬도 센다 (서로의 디지몬)
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, opp.GetCurrentDP());
        Assert.Equal(0, opp.GetSecurityAttackCount());
    }

    // =====================================================================
    // 이전 문장 · 한글은 그대로
    // =====================================================================

    [Theory]
    [InlineData("[Main] All of your Digimon gain ＜Rush＞ for the turn.")]
    [InlineData("[Main] All of your Digimon gain \"[Your Turn] When this Digimon attacks an opponent's Digimon with no digivolution cards, delete that Digimon.\" for the turn.")]
    [InlineData("[Opponent's Turn] This Digimon isn't affected by your opponent's Option cards' effects.")]
    [InlineData("[On Play] If this Digimon has 1 or more digivolution cards, ＜Blitz＞")]
    public void EarlierSentences_Unchanged(string clause)
    {
        var c = Card(Uid("B62-OLD-"), clause, clause.StartsWith("[Main]") ? CardType.Option : CardType.Digimon, clause.StartsWith("[Main]") ? 0 : 5);
        AllSupported(c);
        Assert.DoesNotContain("⟦", Ko(clause));
    }
}
