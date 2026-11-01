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
/// v0.63.0 미지원 문장 37차 묶음: 기간 동안의 전체 처리 (종합 룰 15-11-2-2 · 15-11-2-3 — 나중에 늘어난 대상 · 조건을 잃은 대상) ·
/// BT17-101 메모리를 상대 쪽의 3으로 · 어택 대상 변경 (BT15-078 · EX5-054 · ST18-14 · BT4-075) · BT5-017 ≪진격≫ 어택 · BT26-101 "그 후, 아래 효과 중 1개" ·
/// 효과를 처리하지 않고 놓인 카드의 지속 효과 문장 · AI 어택 판단 (BT7-055 다음 턴의 패 1장)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch63Tests
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
            state.Player.MainDeck.Add(Digimon("B63-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B63-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B63-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B63-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    /// <summary>나중에 배틀 에어리어에 나온 디지몬 (등장 처리 — 【등장 시】 뒤의 룰 처리까지)</summary>
    private static CardInstance Enter(GoldfishEngine engine, PlayerZones zones, CardData card, params CardData[] sources)
    {
        var d = new CardInstance(card, engine.State.CurrentTurn) { IsOwnersTurn = ReferenceEquals(zones, engine.State.Player) };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        engine.RecalculateFieldBuffs();
        engine.ApplyOnPlayEffects(zones, d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    // =====================================================================
    // 1. 기간 동안의 전체 처리 — 나중에 늘어난 대상 (15-11-2-2) · 조건 (15-11-2-3)
    // =====================================================================

    [Fact]
    public void WholeDpMinus_LaterDigimonToo_ZeroDpDeletedByRule_ExpiresNextTurn()
    {
        var mon = Digimon(Uid("B63-TERRA-"), 6, "[On Play] All of your opponent's Digimon get -5000 DP for the turn.");
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var first = Put(state.DummyOpponent, Digimon("B63-O1", 6, dp: 8000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon, includeInherited: false);
        Assert.Equal(3000, first.GetCurrentDP());
        var late6 = Enter(engine, state.DummyOpponent, Digimon("B63-L6", 4, dp: 6000));
        var late5 = Enter(engine, state.DummyOpponent, Digimon("B63-L5", 4, dp: 5000));
        Assert.Equal(1000, late6.GetCurrentDP());
        Assert.DoesNotContain(late5, state.DummyOpponent.BattleArea);   // DP 0 — 룰 처리로 소멸
        Assert.Equal(3000, first.GetCurrentDP());   // 처음 대상은 한 번만
        state.TurnSequence++;
        engine.RecalculateFieldBuffs();
        var next = Enter(engine, state.DummyOpponent, Digimon("B63-NX", 4, dp: 6000));
        Assert.Equal(6000, next.GetCurrentDP());   // 기간이 끝났다
    }

    [Fact]
    public void WholeCantAttack_ConditionLostByDigivolving_FreesIt_NewSourcelessLocked_BT1_100()
    {
        const string text = "[Main] Until the end of your opponent's next turn, their Digimon with no digivolution cards can't attack.";
        var opt = Card(Uid("B63-GCF-"), text);
        AllSupported(opt);
        string ko = Ko(text);
        var (state, engine) = NewMainPhase();
        var a = Put(state.DummyOpponent, Digimon("B63-A", 3));
        var b = Put(state.DummyOpponent, Digimon("B63-B", 4), false, Digimon("B63-BS", 3));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.True(a.CannotAttackUntil > 0);
        Assert.Equal(0, b.CannotAttackUntil);
        a.Evolve(Digimon("B63-A4", 4));   // 진화원을 가지면 대상이 아니게 된다 (공식 Q&A BT1-100 Q1)
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, a.CannotAttackUntil);
        var late = Enter(engine, state.DummyOpponent, Digimon("B63-LATE", 3));
        Assert.True(late.CannotAttackUntil > 0, Logs(state));
        b.TakeSource(_ => true);   // 나중에 조건을 만족하면 대상이 된다 (15-11-2-3-3)
        engine.RecalculateFieldBuffs();
        Assert.True(b.CannotAttackUntil > 0);
        Assert.Equal(ko, Ko(text));   // 한글은 그대로
    }

    [Fact]
    public void SingleTarget_IsNotWhole_KeepsLockAfterConditionChange_ST21_03()
    {
        var opt = Card(Uid("B63-ST21-"), "[Main] 1 of your opponent's Digimon with no digivolution cards can't attack or block until the end of their turn.");
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var a = Put(state.DummyOpponent, Digimon("B63-A", 3));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        a.Evolve(Digimon("B63-A4", 4));
        engine.RecalculateFieldBuffs();
        var late = Enter(engine, state.DummyOpponent, Digimon("B63-LATE", 3));
        Assert.True(a.CannotAttackUntil > 0);   // 단일 처리 — 15-11-1-3-2 · 공식 Q&A ST21-03 Q1
        Assert.Equal(0, late.CannotAttackUntil);
        Assert.False(engine.AnyWhole63);
    }

    [Fact]
    public void WholeKeywordAndDpBuff_OwnLaterDigimon_And_SecurityCantAttackPlayers()
    {
        var opt = Card(Uid("B63-BUFF-"), "[Main] All of your Digimon get +3000 DP until the end of your opponent's turn.");
        var sec = Card(Uid("B63-SEC-"), "[Security] Your opponent's Digimon can't attack players for the turn.");
        AllSupported(opt);
        AllSupported(sec);
        var (state, engine) = NewMainPhase();
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        var late = Enter(engine, state.Player, Digimon("B63-L", 4, dp: 4000));
        Assert.Equal(7000, late.GetCurrentDP());
        engine.RunEffects(EffectTrigger.Security, state.Player, null, sec, includeInherited: false);
        var o = Enter(engine, state.DummyOpponent, Digimon("B63-O", 4));   // 공식 Q&A BT3-105 Q1 — 나중에 나온 디지몬도
        Assert.False(EffectRules.CanAttackPlayer(o));
    }

    [Fact]
    public void WholeCardsDontUnsuspend_LaterDigimonAndTamer_BT12_106()
    {
        var opt = Card(Uid("B63-NIGHT-"), "[Main] Suspend all of your opponent's Digimon and Tamers. Your opponent's cards don't unsuspend during their next unsuspend phase.");
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var a = Put(state.DummyOpponent, Digimon("B63-A", 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        var late = Enter(engine, state.DummyOpponent, Digimon("B63-L", 4));
        var tamer = Card(Uid("B63-T-"), "", CardType.Tamer);
        state.DummyOpponent.Tamers.Add(tamer);
        engine.RecalculateFieldBuffs();
        Assert.Equal(a.SkipUnsuspendUntil, late.SkipUnsuspendUntil);
        Assert.True(late.SkipUnsuspendUntil > state.TurnSequence);
        Assert.True(state.DummyOpponent.TamerCannotUnsuspendUntil.GetValueOrDefault(tamer) > state.TurnSequence);   // 공식 Q&A BT12-106 Q1
    }

    [Fact]
    public void WholeRecords_ClearedOnNewGame()
    {
        var opt = Card(Uid("B63-BUFF2-"), "[Main] All of your Digimon get +3000 DP until the end of your opponent's turn.");
        var (state, engine) = NewMainPhase();
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.True(engine.AnyWhole63);
        var deck = Enumerable.Range(0, 50).Select(i => Digimon("B63-DK" + i, 3)).ToList();
        var eggs = Enumerable.Range(0, 5).Select(i => Card("B63-EGG" + i, "", CardType.Digitama, 2)).ToList();
        engine.StartNewGame(deck, eggs, seed: 1);
        Assert.False(engine.AnyWhole63);
    }

    // =====================================================================
    // 2. BT17-101 메모리를 상대 쪽의 3으로
    // =====================================================================

    [Fact]
    public void SetMemoryOnOpponentSide_OnlyWhenBetter_AndTamerSourceCondition_BT17_101()
    {
        const string text = "[Main] You may set the memory to 3 on the opponent's side. Then, if this Digimon has a Tamer in its digivolution cards, gain 1 memory.";
        var opt = Card(Uid("B63-TK-"), text);
        AllSupported(opt);
        Assert.Contains("메모리를 상대 쪽의 3으로 할 수 있다", Ko(text));
        var (state, engine) = NewMainPhase(memory: -6);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(-3, state.MemoryGauge);   // 상대 쪽 6 → 3 (추천)
        var (s2, e2) = NewMainPhase(memory: 2);
        e2.RunEffects(EffectTrigger.Main, s2.Player, null, opt, includeInherited: false);
        Assert.Equal(2, s2.MemoryGauge);   // 자신 쪽이면 하지 않는다
        var mon = Digimon(Uid("B63-TKD-"), 7, "[When Digivolving] If this Digimon has a Tamer in its digivolution cards, gain 1 memory.");
        AllSupported(mon);
        var (s3, e3) = NewMainPhase(memory: 2);
        var d = Put(s3.Player, mon, false, Card("B63-TAMER", "", CardType.Tamer));
        e3.RunEffects(EffectTrigger.WhenDigivolving, s3.Player, d, mon, includeInherited: false);
        Assert.Equal(3, s3.MemoryGauge);
    }

    // =====================================================================
    // 3. 어택 대상 변경 (BT15-078 · EX5-054 · ST18-14 · BT4-075) · BT5-017
    // =====================================================================

    [Fact]
    public void ChangeAttackTargetToOtherOrPlayer_Tamer_ST18_14()
    {
        const string text = "[Your Turn] When one of your Digimon attacks your opponent's Digimon, by suspending this Tamer, you may change the attack target to another of your opponent's Digimon or the player.";
        var tamer = Card(Uid("B63-SHOTO-"), text, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("어택의 대상을 다른 상대의 디지몬 또는 플레이어로 변경할 수 있다", Ko(text));
        var (state, engine) = NewMainPhase(memory: 5);
        state.Player.Tamers.Add(tamer);
        var atk = Put(state.Player, Digimon("B63-ATK", 4, dp: 5000));
        var big = Put(state.DummyOpponent, Digimon("B63-BIG", 5, dp: 9000), suspended: true);
        var small = Put(state.DummyOpponent, Digimon("B63-SMALL", 3, dp: 3000));
        engine.RecalculateFieldBuffs();
        engine.AttackOpponentDigimon(atk, big);
        Assert.Contains(big, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(small, state.DummyOpponent.BattleArea);   // 액티브 상태인 디지몬으로도 (공식 Q&A ST18-14 Q1)
        Assert.Contains(atk, state.Player.BattleArea);

        var (s2, e2) = NewMainPhase(memory: 5);
        s2.Player.Tamers.Add(tamer);
        var atk2 = Put(s2.Player, Digimon("B63-ATK2", 4, dp: 5000));
        var big2 = Put(s2.DummyOpponent, Digimon("B63-BIG2", 5, dp: 9000), suspended: true);
        e2.RecalculateFieldBuffs();
        e2.AttackOpponentDigimon(atk2, big2);
        Assert.Equal(4, s2.DummyOpponent.SecurityStack.Count);   // 이길 디지몬이 없으면 플레이어로 — 시큐리티 체크
        Assert.Contains(atk2, s2.Player.BattleArea);
    }

    [Fact]
    public void SwitchToThisDigimonOrPlayer_DefenderSide_EX5_054()
    {
        const string text = "[Opponent's Turn] [Once Per Turn] When an opponent's Digimon attacks, by placing 1 card with [Etemon] or [Sukamon] in its name from your hand on top of your security stack, you may switch the target of attack to this Digimon or the player.";
        var meta = Digimon(Uid("B63-META-"), 6, text, dp: 12000);
        AllSupported(meta);
        Assert.Contains("어택의 대상을 이 디지몬 또는 플레이어로 변경할 수 있다", Ko(text));
        var (state, engine) = NewMainPhase(memory: 5);
        var atk = Put(state.Player, Digimon("B63-ATK", 5, dp: 7000));
        var m = Put(state.DummyOpponent, meta);
        m.IsOwnersTurn = false;
        var weak = Put(state.DummyOpponent, Digimon("B63-WEAK", 4, dp: 4000), suspended: true);
        weak.IsOwnersTurn = false;
        foreach (var x in new[] { "B63-H1", "B63-H2" }) state.DummyOpponent.Hand.Add(Digimon(x, 3));
        state.DummyOpponent.Hand.Add(Digimon("B63-SUKA", 3, name: "Sukamon"));
        engine.RecalculateFieldBuffs();
        engine.AttackOpponentDigimon(atk, weak);
        Assert.Contains(weak, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(atk, state.Player.BattleArea);   // 12000에게 졌다
        Assert.Equal(6, state.DummyOpponent.SecurityStack.Count);   // 비용으로 패 1장을 시큐리티 위에
    }

    [Fact]
    public void OpponentChoosesUnsuspended_BT4_075_And_SwitchToPlayedDigimon_BT15_078()
    {
        const string blast = "[When Attacking] Your opponent may choose 1 of their unsuspended Digimon. If they do, switch the target of attack to that Digimon.";
        var mon = Digimon(Uid("B63-BLAST-"), 6, blast, dp: 13000);
        AllSupported(mon);
        var (state, engine) = NewMainPhase(memory: 5);
        var me = Put(state.Player, mon);
        Put(state.DummyOpponent, Digimon("B63-WALL", 6, dp: 14000));
        engine.RecalculateFieldBuffs();
        engine.AttackSecurityOrDirect(me);
        Assert.DoesNotContain(me, state.Player.BattleArea);   // 상대가 이기는 디지몬을 골랐다
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);

        const string devil = "[When Attacking] Your opponent plays 1 level 4 or lower Digimon card from their trash suspended without paying the cost. [On Play] effects on Digimon played by this effect don't activate. Then, you may switch the target of attack to that Digimon.";
        var sea = Digimon(Uid("B63-DEVIL-"), 5, devil, dp: 8000);
        AllSupported(sea);
        Assert.Contains("어택의 대상을 그 디지몬으로 변경할 수 있다", Ko(devil));
        var (s2, e2) = NewMainPhase(memory: 5);
        var d = Put(s2.Player, sea);
        var fodder = Digimon("B63-FOD", 4, dp: 5000);
        s2.DummyOpponent.Trash.Add(fodder);
        e2.RecalculateFieldBuffs();
        e2.AttackSecurityOrDirect(d);
        Assert.Equal(5, s2.DummyOpponent.SecurityStack.Count);   // 등장한 디지몬으로 바꿔 배틀
        Assert.Contains(fodder, s2.DummyOpponent.Trash);
    }

    [Fact]
    public void BlitzAttack_CanTargetUnsuspended_OnlyAsBlitzAttacker_BT5_017()
    {
        const string inh = "[Your Turn] When this Digimon attacks with ＜Blitz＞, it can also attack your opponent's unsuspended Digimon.";
        var src = Digimon(Uid("B63-ZIG-"), 6, "", inherited: inh);
        AllSupported(src);
        Assert.Contains("《진격》으로 어택할 때, 액티브 상태인 상대의 디지몬에게도 어택할 수 있다", Ko(inh));
        var (state, engine) = NewMainPhase(memory: -1);
        var me = Put(state.Player, Digimon("B63-TOP", 6, dp: 11000), false, src);
        var o = Put(state.DummyOpponent, Digimon("B63-O", 4, dp: 5000));
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.CanBeAttackTargetFor(me, o));
        Assert.True(engine.TryGrantBlitz(state.Player, me));
        Assert.True(EffectRules.CanBeAttackTargetFor(me, o));
        Assert.True(engine.AttackOpponentDigimon(me, o).CanExecute);
        Assert.DoesNotContain(o, state.DummyOpponent.BattleArea);
    }

    // =====================================================================
    // 4. BT26-101 "~. Then, activate 1 of the effects below:"
    // =====================================================================

    [Fact]
    public void ThenActivateOneOfBelow_PreSentenceThenChoice_BT26_101()
    {
        string text = "[Main] All of your [TS] trait Digimon gain ＜Blocker＞ (This Digimon can block in the blocker timing.) and +3000 DP until your opponent's turn ends. Then, activate 1 of the effects below:\n・Delete 1 of your opponent's Digimon with as much DP as 1 of your [TS] trait Digimon or less.\n・1 of your [TS] trait Digimon unsuspends.";
        var opt = Card(Uid("B63-TS-"), text);
        AllSupported(opt);
        Assert.Contains("그 후, 이하의 효과에서 1개를 골라 발휘한다", Ko("[Main] All of your [TS] trait Digimon gain ＜Blocker＞ and +3000 DP until your opponent's turn ends. Then, activate 1 of the effects below:"));
        var (state, engine) = NewMainPhase(memory: 5);
        var ts = Put(state.Player, Digimon("B63-TSD", 5, dp: 8000, traits: "TS"), suspended: true);
        var o = Put(state.DummyOpponent, Digimon("B63-O", 5, dp: 10000));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(11000, ts.GetCurrentDP());
        Assert.True(ts.HasBlocker);
        Assert.DoesNotContain(o, state.DummyOpponent.BattleArea);   // 11000 이하 — 소멸을 고른다
    }

    // =====================================================================
    // 5. 효과를 처리하지 않고 놓인 카드의 지속 효과 문장 · 6. AI (BT7-055)
    // =====================================================================

    [Fact]
    public void GrantAura_CardPlacedWithoutBuildingScript_StillGrants()
    {
        var aura = Digimon(Uid("B63-AURA-"), 6, "[Opponent's Turn] All of your opponent's level 3 Digimon gain \"[When Attacking] Lose 1 memory.\"");
        var (state, engine) = NewMainPhase();
        var a = Put(state.DummyOpponent, aura);
        a.IsOwnersTurn = false;
        var mine = Put(state.Player, Digimon("B63-L3", 3));
        engine.RecalculateFieldBuffs();   // 이 카드의 스크립트를 아무도 만들지 않았다
        Assert.Single(mine.AuraGranted61);
    }

    [Fact]
    public void Ai_AttackPenalty_WhenUnsuspendCostsHandNextTurn_BT7_055()
    {
        var ebon = Digimon(Uid("B63-EBON-"), 6, "[Opponent's Turn] All of your opponent's Digimon gain \"[Your Turn] When this Digimon would unsuspend, you must trash 1 card in your hand or prevent this Digimon from unsuspending.\"");
        var (state, engine) = NewMainPhase();
        var e = Put(state.Player, ebon);
        e.IsOwnersTurn = false;
        var ai = Put(state.DummyOpponent, Digimon("B63-AI", 4));
        var ai5 = Put(state.DummyOpponent, Digimon("B63-AI5", 5));
        engine.RecalculateFieldBuffs();
        state.DummyOpponent.Hand.Clear();
        Assert.Equal(1.0, SmartAiPolicy.UnsuspendPenalty63(engine, ai));   // 다음 턴에 패 1장 — 파기하지 않고 레스트로 남는다
        Assert.Equal(0.6, SmartAiPolicy.UnsuspendPenalty63(engine, ai5));   // Lv.5 — 패 1장을 파기하고 액티브
        state.Player.BattleArea.Remove(e);
        engine.RecalculateFieldBuffs();
        Assert.Equal(0.0, SmartAiPolicy.UnsuspendPenalty63(engine, ai));
    }
}
