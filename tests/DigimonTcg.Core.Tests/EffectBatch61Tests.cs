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
/// v0.61.0 미지원 문장 35차 묶음: LM-020 카드 종류 내성 (이미 받은 그 종류의 효과도 받지 않는 동안 영향이 없다) ·
/// 진화원 파기를 한 곳으로 (BT9-109 「X항체」 보호 · BT10-084 치환) · BT10-042 ≪시큐리티 어택≫을 가진 디지몬 ·
/// 효과 문장을 주는 지속 효과 (BT5-091 · BT8-031 · BT10-056 · BT12-088)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch61Tests
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
            state.Player.MainDeck.Add(Digimon("B61-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B61-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B61-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B61-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    /// <summary>LM-020처럼 카드 종류의 효과를 이 턴 동안 받지 않게 한다</summary>
    private static void Shield(GameState state, CardInstance d, CardType type)
    {
        d.UnaffectedCategory50 = type;
        d.UnaffectedCategory50Until = state.TurnSequence;
    }

    // =====================================================================
    // 1. LM-020 — 이미 받은 그 카드 종류의 효과 (15-15-5-1 · 15-15-5-2 · 15-15-5-4)
    // =====================================================================

    [Fact]
    public void CategoryShield_IgnoresTimedDpFromThatCategory_ThenBack()
    {
        var opt = Card(Uid("B61-OPT-"), "[Main] 1 of your Digimon gets +3000 DP until the end of your opponent's turn.");
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B61-Q", 5, dp: 5000));
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, chosenTarget: d, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.Equal(8000, d.GetCurrentDP());
        Shield(state, d, CardType.Option);   // 옵션 카드의 효과를 받지 않는다 → 이미 받은 +3000도 영향이 없다
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, d.GetCurrentDP());
        Shield(state, d, CardType.Tamer);   // 다른 종류면 그대로
        engine.RecalculateFieldBuffs();
        Assert.Equal(8000, d.GetCurrentDP());
        d.UnaffectedCategory50 = null;   // 받지 않는 상태가 끝나면 다시 (15-15-5-2)
        engine.RecalculateFieldBuffs();
        Assert.Equal(8000, d.GetCurrentDP());
    }

    [Fact]
    public void CategoryShield_IgnoresThisTurnDpFromThatCategory()
    {
        var opt = Card(Uid("B61-OPT-"), "[Main] 1 of your Digimon gets +2000 DP for the turn.");
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B61-Q", 5, dp: 5000));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, chosenTarget: d, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, d.GetCurrentDP());
        Shield(state, d, CardType.Option);
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, d.GetCurrentDP());
    }

    [Fact]
    public void CategoryShield_IgnoresAttackLockFromDigimon()
    {
        var locker = Digimon(Uid("B61-LOCK-"), 4, "[On Play] 1 of your opponent's Digimon can't attack until the end of their turn.");
        AllSupported(locker);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B61-Q", 5));
        var l = Put(state.DummyOpponent, locker);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, l, locker);
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.IsAttackLocked(state, d), Logs(state));
        Shield(state, d, CardType.Tamer);
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.IsAttackLocked(state, d));
        Shield(state, d, CardType.Digimon);
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.IsAttackLocked(state, d));
    }

    [Fact]
    public void CategoryShield_IgnoresSecurityMinusAndKeywordFromDigimon()
    {
        var minus = Digimon(Uid("B61-SAM-"), 4, "[On Play] 1 of your opponent's Digimon gains ＜Security A. -1＞ until the end of their turn.");
        var blocker = Digimon(Uid("B61-BLK-"), 4, "[On Play] 1 of your Digimon gains ＜Blocker＞ until the end of your opponent's turn.");
        AllSupported(minus);
        AllSupported(blocker);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B61-Q", 5));
        var m = Put(state.DummyOpponent, minus);
        var b = Put(state.Player, blocker);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, m, minus);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, b, blocker, chosenTarget: d);
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, d.GetSecurityAttackCount());
        Assert.True(d.HasBlocker, Logs(state));
        Shield(state, d, CardType.Digimon);   // 자신의 디지몬의 효과도 (LM-020 — 주인이 적혀 있지 않다)
        engine.RecalculateFieldBuffs();
        Assert.Equal(1, d.GetSecurityAttackCount());
        Assert.False(d.HasBlocker);
    }

    [Fact]
    public void CategoryShield_ThisDigimonsOwnEffectStillApplies()
    {
        var self = Digimon(Uid("B61-SELF-"), 5, "[On Play] This Digimon gets +2000 DP until the end of your opponent's turn.", dp: 5000);
        AllSupported(self);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, self);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, self);
        Shield(state, d, CardType.Digimon);   // "other than this Digimon's effects"
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, d.GetCurrentDP());
    }

    [Fact]
    public void CategoryShield_GrantedEffectFromThatCategoryIsNotHad()
    {
        var opt = Card(Uid("B61-GOPT-"), "[Main] 1 of your Digimon gains \"[On Deletion] Gain 3 memory.\" until the end of your opponent's turn.");
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B61-Q", 5));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, chosenTarget: d, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.Single(engine.ActiveGrantedEffects(d));
        Shield(state, d, CardType.Option);
        engine.RecalculateFieldBuffs();
        Assert.Empty(engine.ActiveGrantedEffects(d));
    }

    // =====================================================================
    // 2. 진화원 파기 — BT9-109 보호 · BT10-084 치환
    // =====================================================================

    private const string XAntibodyInherited = "[All Turns] Effects can't trash [X Antibody] in this Digimon's digivolution cards.";

    private static CardData XAntibody() => Card(Uid("B61-XA-"), "[Main] Place this card under 1 of your Digimon as its bottom digivolution card.", name: "X Antibody", inherited: XAntibodyInherited);

    [Fact]
    public void XAntibody_CantBeTrashedByEffects_BottomSkips()
    {
        var xa = XAntibody();
        AllSupported(xa);
        Assert.Contains("「X항체」", Ko(XAntibodyInherited));
        var trasher = Digimon(Uid("B61-TR-"), 5, "[On Play] Trash the bottom 2 digivolution cards of 1 of your opponent's Digimon.");
        AllSupported(trasher);
        var (state, engine) = NewMainPhase();
        var a = Digimon("B61-A", 3);
        var b = Digimon("B61-B", 4);
        var c = Digimon("B61-C", 2);
        var d = Put(state.Player, Digimon("B61-H", 6), b, a, xa, c);   // 위 → 아래: b, a, xa, c
        var t = Put(state.DummyOpponent, trasher);
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(d, Keyword.XAntibodySourcesProtected61));
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, t, trasher);
        // 아래에서 2장: c, (xa는 파기할 수 없다 — 건너뛴다) a  (공식 Q&A EX2-055 Q1)
        Assert.Contains(xa, d.EvolutionSources);
        Assert.DoesNotContain(c, d.EvolutionSources);
        Assert.DoesNotContain(a, d.EvolutionSources);
        Assert.Contains(b, d.EvolutionSources);
        Assert.Equal(1, engine.TrashableSourceCount61(d));   // 남은 b만 (xa는 세지 않는다)
    }

    [Fact]
    public void XAntibody_ChosenTrashCantPickIt()
    {
        var xa = XAntibody();
        var trasher = Digimon(Uid("B61-TRA-"), 5, "[On Play] Trash any 2 digivolution cards of 1 of your opponent's Digimon.");
        AllSupported(trasher);
        var (state, engine) = NewMainPhase();
        var a = Digimon("B61-A", 3);
        var d = Put(state.Player, Digimon("B61-H", 6), a, xa);
        var t = Put(state.DummyOpponent, trasher);
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, t, trasher);
        Assert.Contains(xa, d.EvolutionSources);
        Assert.DoesNotContain(a, d.EvolutionSources);
    }

    [Fact]
    public void NoProtection_BottomTrashUnchanged()
    {
        var trasher = Digimon(Uid("B61-TR-"), 5, "[On Play] Trash the bottom 2 digivolution cards of 1 of your opponent's Digimon.");
        var (state, engine) = NewMainPhase();
        var a = Digimon("B61-A", 3);
        var b = Digimon("B61-B", 4);
        var c = Digimon("B61-C", 2);
        var d = Put(state.Player, Digimon("B61-H", 6), b, a, c);
        var t = Put(state.DummyOpponent, trasher);
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, t, trasher);
        Assert.Equal(new[] { b }, d.EvolutionSources.ToArray());
        Assert.Equal(new[] { c, a }, state.Player.Trash.TakeLast(2).ToArray());
    }

    private const string Replace = "[Opponent's Turn] When an effect would trash one of your other Digimon's digivolution cards, you may trash this Digimon's digivolution cards with that effect instead.";

    [Fact]
    public void ReplaceSourceTrash_OnOpponentsTurn_BT10_084()
    {
        var tacti = Digimon(Uid("B61-TAC-"), 6, Replace);
        AllSupported(tacti);
        Assert.Contains("대신 그 효과로 이 디지몬의 진화원을 파기할 수 있다", Ko(Replace));
        var trasher = Digimon(Uid("B61-TR-"), 5, "[On Play] Trash the bottom 1 digivolution card of 1 of your opponent's Digimon.");
        var (state, engine) = NewMainPhase();
        var a = Digimon("B61-A", 3);
        var tx = Digimon("B61-TX", 5);
        var ty = Digimon("B61-TY", 4);
        var host = Put(state.Player, Digimon("B61-H", 6), a);   // 진화원 1장 — 파기되면 진화원이 없어진다
        var tac = Put(state.Player, tacti, tx, ty);   // 대신해도 진화원이 남는다 → 추천: 대신한다
        host.IsOwnersTurn = tac.IsOwnersTurn = false;   // 상대의 턴
        var t = Put(state.DummyOpponent, trasher);
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(tac, Keyword.ReplaceOtherSourceTrash61));
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, t, trasher, chosenTarget: host);
        Assert.Contains(a, host.EvolutionSources);   // 대신 택티몬의 진화원이 (같은 방법 — 아래에서 1장) 파기되었다
        Assert.Equal(new[] { tx }, tac.EvolutionSources.ToArray());
        Assert.Contains(ty, state.Player.Trash);
    }

    [Fact]
    public void ReplaceSourceTrash_EvenWithNoSources_AndNotOnOwnTurn()
    {
        var tacti = Digimon(Uid("B61-TAC-"), 6, Replace);
        var trasher = Digimon(Uid("B61-TR-"), 5, "[On Play] Trash the bottom 1 digivolution card of 1 of your opponent's Digimon.");
        var (state, engine) = NewMainPhase();
        var a = Digimon("B61-A", 3);
        var host = Put(state.Player, Digimon("B61-H", 6), a);
        var tac = Put(state.Player, tacti);
        host.IsOwnersTurn = tac.IsOwnersTurn = false;
        var t = Put(state.DummyOpponent, trasher);
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, t, trasher, chosenTarget: host);
        Assert.Contains(a, host.EvolutionSources);   // 진화원이 없어도 대체할 수 있다 (공식 Q&A BT10-084 Q1) — 아무것도 파기되지 않는다
        host.IsOwnersTurn = tac.IsOwnersTurn = true;   // 자신의 턴에는 대체하지 않는다
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, t, trasher, chosenTarget: host);
        Assert.Empty(host.EvolutionSources);
    }

    // =====================================================================
    // 3. BT10-042 ≪시큐리티 어택≫을 가진 상대의 디지몬
    // =====================================================================

    private const string SaLock = "[Opponent's Turn] Your opponent's Digimon with ＜Security A.＞ can't attack this Digimon and can't activate [When Attacking] and [When Digivolving] effects.";

    [Fact]
    public void SaHolders_CantAttackThis_AndCantActivate_BT10_042()
    {
        var venus = Digimon(Uid("B61-VEN-"), 6, SaLock);
        AllSupported(venus);
        Assert.Contains("《시큐리티 어택》을 가진 상대의 디지몬은 이 디지몬에게 어택할 수 없고", Ko(SaLock));
        var (state, engine) = NewMainPhase();
        var v = Put(state.Player, venus);
        v.IsOwnersTurn = false;
        v.IsSuspended = true;
        var plain = Put(state.DummyOpponent, Digimon("B61-P", 5));
        var sa = Put(state.DummyOpponent, Digimon("B61-SA", 5, "＜Security A. +1＞"));
        var minus = Put(state.DummyOpponent, Digimon("B61-SM", 5));
        engine.RecalculateFieldBuffs();
        engine.AddTimedSa60(minus, -1, state.TurnSequence);   // ≪시큐리티 어택 -1≫도 (공식 Q&A BT10-042 Q1)
        engine.RecalculateFieldBuffs();
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.CanBeAttackTargetFor(plain, v));
        Assert.False(EffectRules.CanBeAttackTargetFor(sa, v));
        Assert.False(EffectRules.CanBeAttackTargetFor(minus, v));
        Assert.True(CardScripts.HasKeyword(sa, Keyword.CannotActivateWhenAttacking));
        Assert.True(CardScripts.HasKeyword(minus, Keyword.CannotActivateWhenDigivolving));
        Assert.False(CardScripts.HasKeyword(plain, Keyword.CannotActivateWhenAttacking));
        v.IsOwnersTurn = true;   // 【상대의 턴】만
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.CanBeAttackTargetFor(sa, v));
    }

    // =====================================================================
    // 4. 효과 문장을 주는 지속 효과
    // =====================================================================

    private const string Level3Lose = "[All Turns] All level 3 Digimon gain \"[When Attacking] Lose 1 memory.\"";

    [Fact]
    public void AllLevel3GainWhenAttackingLoseMemory_BothSides_BT5_091()
    {
        var takumi = Card(Uid("B61-TAK-"), Level3Lose, CardType.Tamer);
        AllSupported(takumi);
        Assert.Contains("「【어택 시】 메모리를 -1 한다.」의 효과를 얻는다", Ko(Level3Lose));
        var (state, engine) = NewMainPhase(memory: 5);
        state.Player.Tamers.Add(takumi);
        var mine3 = Put(state.Player, Digimon("B61-L3", 3));
        var mine4 = Put(state.Player, Digimon("B61-L4", 4));
        var opp3 = Put(state.DummyOpponent, Digimon("B61-OL3", 3));
        engine.RecalculateFieldBuffs();
        Assert.Single(mine3.AuraGranted61);
        Assert.Empty(mine4.AuraGranted61);
        Assert.Single(opp3.AuraGranted61);   // 공식 Q&A BT5-091 Q1: 자신의 디지몬에게도 · 상대의 디지몬에게도
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, mine3, mine3.TopCard);
        Assert.Equal(4, state.MemoryGauge);
        state.Player.Tamers.Clear();   // 테이머가 없어지면 얻지 않는다
        engine.RecalculateFieldBuffs();
        Assert.Empty(mine3.AuraGranted61);
    }

    private const string OppTrashBottom = "[Opponent's Turn] All of your opponent's Digimon gain \"[When Attacking] Trash the bottom digivolution card of this Digimon.\"";

    [Fact]
    public void OpponentDigimonGainTrashBottomWhenAttacking_BT8_031()
    {
        var fros = Digimon(Uid("B61-FROS-"), 6, OppTrashBottom);
        AllSupported(fros);
        var (state, engine) = NewMainPhase();
        var f = Put(state.DummyOpponent, fros);
        f.IsOwnersTurn = false;   // 플레이어의 턴 = 프로스벨그르몬의 상대의 턴
        var a = Digimon("B61-A", 3);
        var b = Digimon("B61-B", 4);
        var atk = Put(state.Player, Digimon("B61-ATK", 5), b, a);
        engine.RecalculateFieldBuffs();
        Assert.Single(atk.AuraGranted61);
        Assert.Empty(f.AuraGranted61);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, atk, atk.TopCard);
        Assert.Equal(new[] { b }, atk.EvolutionSources.ToArray());
        Assert.Contains(a, state.Player.Trash);
    }

    private const string PlantOnDeletion = "[Opponent's Turn] All of your other Digimon with [Vegetation], [Plant] or [Fairy] in one of their traits gain \"[On Deletion] Gain 2 memory.\"";

    [Fact]
    public void OtherTraitDigimonGainOnDeletion_BT10_056()
    {
        var lotus = Digimon(Uid("B61-LOT-"), 6, PlantOnDeletion);
        AllSupported(lotus);
        var (state, engine) = NewMainPhase(memory: 3);
        var l = Put(state.Player, lotus);
        var plant = Put(state.Player, Digimon("B61-PL", 4, traits: new[] { "Plant" }));
        var other = Put(state.Player, Digimon("B61-OT", 4, traits: new[] { "Dragon" }));
        foreach (var d in state.Player.BattleArea) d.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.Single(plant.AuraGranted61);
        Assert.Empty(other.AuraGranted61);
        Assert.Empty(l.AuraGranted61);   // "other"
        Assert.True(engine.DeleteFrom(state.Player, plant), Logs(state));
        Assert.Equal(5, state.MemoryGauge);
    }

    private const string DpGrant = "[Your Turn] This Digimon gets +2000 DP. While this Digimon has 10000 DP or more, it gains \"[Your Turn] [Once Per Turn] When this Digimon checks your opponent's security stack, gain 2 memory.\"";

    [Fact]
    public void InheritedDpAndGrantWhileDpAtLeast_BT12_088()
    {
        var tamerCard = Card(Uid("B61-T88-"), "", CardType.Tamer, inherited: DpGrant);
        AllSupported(tamerCard);
        var (state, engine) = NewMainPhase();
        var big = Put(state.Player, Digimon("B61-BIG", 6, dp: 8000), tamerCard);
        var small = Put(state.Player, Digimon("B61-SML", 5, dp: 6000), tamerCard);
        engine.RecalculateFieldBuffs();
        engine.RecalculateFieldBuffs();
        Assert.Equal(10000, big.GetCurrentDP());
        Assert.Single(big.AuraGranted61);
        Assert.Equal(8000, small.GetCurrentDP());
        Assert.Empty(small.AuraGranted61);
        foreach (var d in state.Player.BattleArea) d.IsOwnersTurn = false;   // 【자신의 턴】만
        engine.RecalculateFieldBuffs();
        engine.RecalculateFieldBuffs();
        Assert.Empty(big.AuraGranted61);
    }

    // =====================================================================
    // 이전 문장 · 한글은 그대로
    // =====================================================================

    [Theory]
    [InlineData("[When Digivolving] Trash the bottom 2 digivolution cards of 1 of your opponent's Digimon.")]
    [InlineData("[Main] Until the end of your opponent's turn, all of their Digimon gain \"[All Turns] When this Digimon becomes suspended, lose 1 memory.\"")]
    [InlineData("[On Play] Trash any 1 digivolution card of 1 of your opponent's Digimon.")]
    public void EarlierSentences_Unchanged(string clause)
    {
        var c = Digimon(Uid("B61-OLD-"), 5, clause);
        AllSupported(c);
        Assert.DoesNotContain("⟦", Ko(clause));
    }
}
