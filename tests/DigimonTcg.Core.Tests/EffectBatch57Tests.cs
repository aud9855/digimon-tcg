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
/// v0.57.0 미지원 문장 31차 묶음: 효과로 하는 어택은 효과 처리 중에 어택 선언 → 【어택 시】는 효과가 끝난 뒤 → 카운터 · 블록 · 배틀
/// (종합 룰 11-1-4 · 11-2-4 · 15-8-3-2 · 공식 Q&A BT5-086 · BT20-036 · BT20-090) · 셈 · 지속 효과 둘 · 트래시에서 코스트를 내고 등장 + 디지크로스 · 테이머 레스트 불가 · 아래 효과를 여러 번
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch57Tests
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

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int security = 5)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B57-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B57-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B57-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B57-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    // 1. 효과로 하는 어택 (종합 룰 11-1-4 · 11-2-4 · 15-8-3-2 · 공식 Q&A BT5-086 · BT20-036 · BT20-090)
    // =====================================================================

    /// <summary>"[Main] 1 of your Digimon may attack. Then, gain 1 memory." + 어택하는 디지몬 "[When Attacking] ＜Draw 1＞"</summary>
    private static (GameState State, GoldfishEngine Engine, CardInstance Attacker) OptionAttack()
    {
        var attacker = Digimon(Uid("B57-ATK-"), 5, "[When Attacking] ＜Draw 1＞ (Draw 1 card from your deck.)", dp: 9000);
        var opt = Card(Uid("B57-OPT-"), "[Main] 1 of your Digimon may attack. Then, gain 1 memory.", CardType.Option, 0, 0);
        AllSupported(attacker);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 0);
        var atk = Put(state.Player, attacker);
        state.Player.Hand.Add(opt);
        var r = engine.PlayCard(opt);
        Assert.True(r.CanExecute, r.ToString());
        return (state, engine, atk);
    }

    [Fact]
    public void EffectAttack_RestOfEffect_ThenWhenAttacking_ThenBattle()
    {
        var (state, _, atk) = OptionAttack();
        Assert.True(atk.IsSuspended);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);   // 어택은 끝까지 (시큐리티 체크)
        Assert.Equal(1, state.MemoryGauge);
        int drew = state.ActionLogs.FindIndex(l => l.Contains("드로우"));
        int check = state.ActionLogs.FindIndex(l => l.Contains("시큐리티 체크"));
        Assert.True(drew >= 0 && state.ActionLogs[drew].Contains("M:+1"), Logs(state));   // 효과의 나머지("그 후, 메모리 +1")가 먼저 → 【어택 시】 (줄 머리의 M: = 그때 메모리)
        int optionUsed = state.ActionLogs.FindIndex(l => l.Contains("옵션 사용"));
        Assert.True(check > drew && check > optionUsed, Logs(state));                     // → 옵션을 트래시에 놓은 뒤 시큐리티 체크
    }

    [Fact]
    public void EffectAttack_DeclareDisabled_OldOrder()
    {
        GoldfishEngine.EffectAttackDeclare57 = false;
        try
        {
            var (state, _, _) = OptionAttack();
            Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
            int drew = state.ActionLogs.FindIndex(l => l.Contains("드로우"));
            int check = state.ActionLogs.FindIndex(l => l.Contains("시큐리티 체크"));
            int optionUsed = state.ActionLogs.FindIndex(l => l.Contains("옵션 사용"));
            Assert.True(drew >= 0 && state.ActionLogs[drew].Contains("M:0"), Logs(state));   // 전: 효과 한가운데에서 어택을 끝까지 → 그 뒤 메모리 +1
            Assert.True(check >= 0 && check < optionUsed, Logs(state));
        }
        finally { GoldfishEngine.EffectAttackDeclare57 = true; }
    }

    [Fact]
    public void EffectAttack_DuringAnAttack_CannotDeclare()
    {
        var a = Digimon(Uid("B57-A-"), 5, "[When Attacking] 1 of your other Digimon may attack.", dp: 9000);
        var b = Digimon(Uid("B57-B-"), 5, dp: 9000);
        AllSupported(a);
        var (state, engine) = NewMainPhase();
        var atkA = Put(state.Player, a);
        var atkB = Put(state.Player, b);
        engine.AttackSecurityOrDirect(atkA);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);   // 어택 중에는 새로 어택 선언을 할 수 없다 (전에는 B가 A의 어택 한가운데에서 어택)
        Assert.False(atkB.IsSuspended, Logs(state));
    }

    [Fact]
    public void EffectAttack_DeclaredThenUnsuspendedByLaterEffect_StaysActive()
    {
        // 공식 Q&A BT5-086 Q1: 어택 선언 · 레스트는 그 시점 — 그 뒤의 효과로 액티브로 할 수 있다
        var attacker = Digimon(Uid("B57-UNS-"), 5, dp: 9000);
        var opt = Card(Uid("B57-OPTU-"), "[Main] 1 of your Digimon may attack. Then, 1 of your Digimon may unsuspend.", CardType.Option, 0, 0);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var atk = Put(state.Player, attacker);
        state.Player.Hand.Add(opt);
        engine.PlayCard(opt);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.False(atk.IsSuspended, Logs(state));
    }

    [Fact]
    public void AiEndOfTurn_TwoMayAttackEffects_OnlyFirstAttacks()
    {
        int Run(bool declare)
        {
            GoldfishEngine.EffectAttackDeclare57 = declare;
            try
            {
                var (state, engine) = NewMainPhase(security: 0);
                state.CurrentStage = PracticeStage.Stage3_AiBattle;
                for (int i = 0; i < 8; i++) state.Player.SecurityStack.Add(Digimon("B57-PSEC" + i, 3, color: CardColor.White, dp: 1000));
                for (int i = 0; i < 2; i++)
                    state.DummyOpponent.BattleArea.Add(new CardInstance(Digimon("B57-EOT" + i, 5, "[End of Your Turn] This Digimon may attack without suspending.", dp: 9000), 0));
                state.Player.BattleArea.Clear();
                engine.PassTurn();
                return state.Player.SecurityStack.Count;
            }
            finally { GoldfishEngine.EffectAttackDeclare57 = true; }
        }
        int now = Run(true), old = Run(false);
        Assert.Equal(old + 1, now);   // 턴 종료 시 1장째 효과로 어택 → 2장째는 어택 중이라 어택할 수 없다 (공식 Q&A BT20-090 Q1)
    }

    // =====================================================================
    // 2. 셈 (EX13-020 · EX11-073 · BT22-015) · 낸 만큼 (BT17-041 · BT6-111)
    // =====================================================================

    [Fact]
    public void ForEvery5000Dp_EX13_020()
    {
        var me = Digimon(Uid("B57-MAG-"), 6, "[On Play] This Digimon gets +1000 DP until your opponent's turn ends for each color in trashes. Then, to 1 of your opponent's Digimon, give -4000 DP until their turn ends for every 5000 DP this Digimon has.", dp: 9000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        state.Player.Trash.Add(Digimon("B57-TR", 3, color: CardColor.Red));
        var d = Put(state.Player, me);
        var foe = Put(state.DummyOpponent, Digimon("B57-FOE", 6, dp: 12000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.Equal(10000, d.GetCurrentDP());
        Assert.Equal(4000, foe.GetCurrentDP());   // 10000 DP → 2번 → -8000
    }

    [Fact]
    public void ForEachLinkCard_TrashSecurityAndBottomDeck_EX11_073()
    {
        var me = Digimon(Uid("B57-EXM-"), 7, "[End of Opponent's Turn] [Once Per Turn] For each of this Digimon's link cards, trash your opponent's top security card and return 1 of their Digimon to the bottom of the deck.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        d.LinkedCards.Add(Digimon("B57-L1", 3));
        d.LinkedCards.Add(Digimon("B57-L2", 3));
        Put(state.DummyOpponent, Digimon("B57-F1", 4));
        Put(state.DummyOpponent, Digimon("B57-F2", 4));
        Put(state.DummyOpponent, Digimon("B57-F3", 4));
        engine.RunEffects(EffectTrigger.EndOfOpponentsTurn, state.Player, d, me);
        Assert.Equal(3, state.DummyOpponent.SecurityStack.Count);
        Assert.Single(state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void SameLevelPairs_BT22_015()
    {
        var top = Digimon("B57-TOP", 7);
        var sources = new[] { Digimon("S3a", 3), Digimon("S3b", 3), Digimon("S5a", 5), Digimon("S5b", 5), Digimon("S5c", 5), Digimon("S6", 6) };
        var d = new CardInstance(top, 1);
        foreach (var s in sources) d.AddBottomSource(s);
        Assert.Equal(2, SentenceParser.SameLevelGroups57(d, 2));   // Lv.3 2장 · Lv.5 3장 → 2묶음
        var me = Digimon(Uid("B57-OMNI-"), 7, "[When Digivolving] For every 2 same-level cards this Digimon's stack has, return 1 of your opponent's Digimon to the bottom of the deck. Then, this Digimon may attack.");
        AllSupported(me);
    }

    [Fact]
    public void SuspendTamers_SecurityAttackPerTamer_BT17_041()
    {
        var me = Digimon(Uid("B57-SGB-"), 6, "[When Attacking] By suspending up to 2 of your yellow Tamers, for every Tamer this effect suspended, this Digimon gains ＜Security A. +1＞ for the turn. (This Digimon checks 1 additional security card.)");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        state.Player.Tamers.Add(Tamer("B57-Y1", color: CardColor.Yellow));
        state.Player.Tamers.Add(Tamer("B57-Y2", color: CardColor.Yellow));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.Equal(2, state.Player.SuspendedTamers.Count);
        Assert.Equal(3, d.GetSecurityAttackCount());
    }

    [Fact]
    public void PayUpTo5Memory_DpPerMemory_BT6_111()
    {
        var me = Digimon(Uid("B57-ALPHA-"), 7, "[When Attacking] You may pay up to 5 memory. If you do, this Digimon gets +1000 DP for the turn for each memory paid.", dp: 12000);
        AllSupported(me);
        var (state, engine) = NewMainPhase(memory: 3);
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, state.MemoryGauge);   // AI · 선택 창 없음: 메모리를 상대 쪽으로 넘기지 않는 만큼
        Assert.Equal(15000, d.GetCurrentDP());
    }

    // =====================================================================
    // 3. 지속 효과 둘 (AD1-004 · BT12-031 · EX8-045) · 테이머 레스트 불가 (EX5-052)
    // =====================================================================

    [Fact]
    public void TamerColors_DpAndSecurityAttack_AD1_004()
    {
        var me = Digimon(Uid("B57-WG-"), 6, "[All Turns] This Digimon gets +1000 DP for each of your Tamers' colors, and gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for every 3 of their colors.", dp: 12000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        state.Player.Tamers.Add(Tamer("B57-TR", color: CardColor.Red));
        state.Player.Tamers.Add(Tamer("B57-TB", color: CardColor.Blue));
        state.Player.Tamers.Add(Tamer("B57-TG", color: CardColor.Green));
        engine.RecalculateFieldBuffs();
        Assert.Equal(15000, d.GetCurrentDP());
        Assert.Equal(2, d.GetSecurityAttackCount());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void SourceColors_DpAndKeywords_BT12_031(int colors, bool keywords)
    {
        var me = Digimon(Uid("B57-IMP-"), 6, "[All Turns] This Digimon gets +1000 DP for each color in its digivolution cards. While there are 2 or more colors in its digivolution cards, it gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) and ＜Blocker＞ (This Digimon can block in the blocker timing.).", dp: 10000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var srcs = new List<CardData> { Digimon("B57-SR", 4, color: CardColor.Red) };
        if (colors > 1) srcs.Add(Digimon("B57-SG", 5, color: CardColor.Green));
        var d = Put(state.Player, me, srcs.ToArray());
        engine.RecalculateFieldBuffs();
        Assert.Equal(10000 + 1000 * colors, d.GetCurrentDP());
        Assert.Equal(keywords, d.HasBlocker);
        Assert.Equal(keywords ? 2 : 1, d.GetSecurityAttackCount());
    }

    [Theory]
    [InlineData(12000, false)]
    [InlineData(9000, true)]
    public void NoOpponentWithEqualOrHigherDp_PiercingAndSecurityAttack_EX8_045(int foeDp, bool gains)
    {
        var me = Digimon(Uid("B57-CAL-"), 6, "[Your Turn] For each color in this Digimon's digivolution cards, it gets +1000 DP. While your opponent has no Digimon with equal or higher DP, this Digimon gets ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon and survives the battle, it performs any security checks it normally would.) and ＜Security A. +1＞ (This Digimon checks 1 additional security card.)", dp: 10000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me, Digimon("B57-SY", 5, color: CardColor.Yellow));
        Put(state.DummyOpponent, Digimon("B57-FOE", 6, dp: foeDp));
        engine.RecalculateFieldBuffs();
        Assert.Equal(11000, d.GetCurrentDP());
        Assert.Equal(gains ? 2 : 1, d.GetSecurityAttackCount());
    }

    [Fact]
    public void OpponentTamersCantSuspend_EX5_052()
    {
        var lockCard = Digimon(Uid("B57-MAKU-"), 4, "[Opponent's Turn] All of your opponent's Tamers with play costs of 2 or less can't suspend.");
        AllSupported(lockCard);
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, lockCard);
        var cheap = Tamer("B57-CHEAP", playCost: 2);
        var pricey = Tamer("B57-PRICEY", playCost: 3);
        state.Player.Tamers.Add(cheap);
        state.Player.Tamers.Add(pricey);
        Assert.True(engine.TamerSuspendLocked(state.Player, cheap));    // 내 턴 = 효과 주인의 상대의 턴
        Assert.False(engine.TamerSuspendLocked(state.Player, pricey));
        engine.SuspendTamer(state.Player, cheap);
        Assert.True(state.Player.HasActiveTamer(cheap));
    }

    // =====================================================================
    // 4. 아래 효과를 여러 번 (EX4-068 · BT2-041 · EX12-037) · 그 밖의 문장
    // =====================================================================

    [Fact]
    public void ActivateAgainPerColor_EX4_068()
    {
        var opt = Card(Uid("B57-HJ-"), "[Main] Activate the effect below. For each color your Digimon have, activate it again.\r\n・1 of your opponent's Digimon gets -6000 DP for the turn.", CardType.Option, 0, 0, CardColor.Green);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B57-G", 4, color: CardColor.Green));
        Put(state.Player, Digimon("B57-R", 4, color: CardColor.Red));
        var foe = Put(state.DummyOpponent, Digimon("B57-BIG", 7, dp: 30000));
        state.Player.Hand.Add(opt);
        engine.PlayCard(opt);
        engine.RecalculateFieldBuffs();
        Assert.Equal(30000 - 18000, foe.GetCurrentDP());   // 1번 + 색 2색마다 1번
    }

    [Fact]
    public void SuspendYellowTamers_EffectPerTamer_BT2_041()
    {
        var me = Digimon(Uid("B57-SHINE-"), 6, "[When Digivolving] Suspend all of your yellow Tamers. For each yellow Tamer you suspend by this effect, activate the effect below.\r\n・1 of your opponent's Digimon gets -4000 DP for the turn.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        state.Player.Tamers.Add(Tamer("B57-Y1", color: CardColor.Yellow));
        state.Player.Tamers.Add(Tamer("B57-Y2", color: CardColor.Yellow));
        state.Player.Tamers.Add(Tamer("B57-R1", color: CardColor.Red));
        var foe = Put(state.DummyOpponent, Digimon("B57-BIG", 7, dp: 20000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.Equal(2, state.Player.SuspendedTamers.Count);
        Assert.Equal(12000, foe.GetCurrentDP());
    }

    [Fact]
    public void ForEvery5Sources_ChooseEffectEachTime_EX12_037()
    {
        var me = Digimon(Uid("B57-OMN-"), 7, "[When Digivolving] [When Attacking] [Once Per Turn] Delete 1 of your opponent's Digimon. Then, for every 5 of this Digimon's digivolution cards, activate 1 of the effects below:\r\n・1 of your opponent's Digimon gets -13000 DP until their turn ends.\r\n・Trash your opponent's top security card. Then, ＜Recovery +1＞ (Place the top card of your deck as your top security card.).");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me, Enumerable.Range(0, 10).Select(i => Digimon("B57-S" + i, 3)).ToArray());
        for (int i = 0; i < 3; i++) Put(state.DummyOpponent, Digimon("B57-F" + i, 6, dp: 10000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        int removed = 2 - state.DummyOpponent.BattleArea.Count(x => x.GetCurrentDP() > 0);
        int trashed = 5 - state.DummyOpponent.SecurityStack.Count;
        Assert.True(removed + trashed == 2, Logs(state));   // 1마리 소멸 뒤 진화원 10장 → 고르는 효과 2번
        Assert.Equal(5 + trashed, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void RecoveryCappedAt5_EX2_018()
    {
        var me = Digimon(Uid("B57-MA-"), 3, "[On Play] For each of your opponent's Digimon with no digivolution cards, ＜Recovery +1 (Deck)＞. This effect can't increase the number of cards in your security stack to 6 or more.");
        AllSupported(me);
        var (state, engine) = NewMainPhase(security: 3);
        var d = Put(state.Player, me);
        for (int i = 0; i < 4; i++) Put(state.DummyOpponent, Digimon("B57-F" + i, 3));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Equal(5, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void PlayFromTrash_PayingItsCost_BT10_104()
    {
        var dk = Digimon(Uid("B57-DK-"), 5, name: "B57 DarkKnight", playCost: 7);
        var opt = Card(Uid("B57-IR-"), "[Main] Trash 3 cards from the top of your deck. Then, you may play 1 [B57 DarkKnight] from your trash for its memory cost. If you play a Digimon card with DigiXros requirements by this effect, you may DigiXros by placing any of the specified Digimon from your trash at the bottom of this card as digivolution cards.", CardType.Option, 0, 0);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 8);
        Put(state.Player, Digimon("B57-COL", 3));
        state.Player.Trash.Add(dk);
        state.Player.Hand.Add(opt);
        engine.PlayCard(opt);
        Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, dk));
        Assert.Equal(1, state.MemoryGauge);   // 등장 코스트 7을 낸다
    }

    [Fact]
    public void Korean_SameWithAndWithoutMemo_Batch57()
    {
        var clauses = new[]
        {
            "[All Turns] This Digimon gets +1000 DP for each of your Tamers' colors, and gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for every 3 of their colors.",
            "[When Attacking] By suspending up to 2 of your yellow Tamers, for every Tamer this effect suspended, this Digimon gains ＜Security A. +1＞ for the turn. (This Digimon checks 1 additional security card.)",
            "[When Attacking] You may pay up to 5 memory. If you do, this Digimon gets +1000 DP for the turn for each memory paid.",
            "[Opponent's Turn] All of your opponent's Tamers with play costs of 2 or less can't suspend.",
            "[Main] Activate the effect below. For each color your Digimon have, activate it again.",
            "[On Play] For each of your opponent's Digimon with no digivolution cards, ＜Recovery +1 (Deck)＞. This effect can't increase the number of cards in your security stack to 6 or more.",
            "[On Play] [When Digivolving] [When Attacking] [Once Per Turn] This Digimon gets +1000 DP until your opponent's turn ends for each color in trashes. Then, to 1 of your opponent's Digimon, give -4000 DP until their turn ends for every 5000 DP this Digimon has.",
        };
        var cached = clauses.Select(Ko).ToList();
        Assert.DoesNotContain("<null>", cached);
        Assert.Contains("6장 이상으로 늘릴 수 없다", cached[5]);
        KoreanEffectRenderer.MemoEnabled52 = false;
        try { Assert.Equal(clauses.Select(Ko).ToList(), cached); }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }
}
