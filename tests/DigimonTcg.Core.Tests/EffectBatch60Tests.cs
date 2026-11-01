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
/// v0.60.0 미지원 문장 34차 묶음: 상대의 효과를 받지 않는 동안 이미 받은 기간 효과(키워드 · ≪시큐리티 어택≫ · "~ 할 수 없다") ·
/// AI 추천 (≪디지버스트≫ 진화원 · 소멸 비용으로 등장) · 진화원이 없는 동안의 지속 효과 · 【시큐리티】 효과가 발휘되지 않는다 ·
/// 배틀 에어리어를 떠나지 않는다 · Lv.과 시큐리티 합계 비교 · DP 비교 ≪충돌≫
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch60Tests
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
            state.Player.MainDeck.Add(Digimon("B60-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B60-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B60-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B60-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    // 1. 상대의 효과를 받지 않는 동안 — 이미 받은 기간 효과 (15-15-5-1 · 15-15-5-2 · 15-15-5-4)
    // =====================================================================

    /// <summary>진화원이 없는 동안 상대의 효과를 받지 않는다 (BT19-101과 같은 문장)</summary>
    private const string SourcelessImmune = "[All Turns] This Digimon with no digivolution cards can't be suspended, and isn't affected by your opponent's effects.";

    [Fact]
    public void SourcelessImmune_BT19_101()
    {
        var me = Digimon(Uid("B60-SLI-"), 6, SourcelessImmune, dp: 9000);
        AllSupported(me);
        Assert.Contains("상대의 효과를 받지 않는다", Ko(SourcelessImmune));
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(d, Keyword.UnaffectedByOpponentEffects));
        Assert.True(EffectRules.IsSuspendLocked(state, d));
        d.AddBottomSource(Digimon("B60-SRC", 5));
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(d, Keyword.UnaffectedByOpponentEffects));
        Assert.False(EffectRules.IsSuspendLocked(state, d));
    }

    [Fact]
    public void OpponentAttackLock_IgnoredWhileImmune_ThenBack()
    {
        var me = Digimon(Uid("B60-SLI-"), 6, SourcelessImmune, dp: 9000);
        var locker = Digimon(Uid("B60-LOCK-"), 4, "[On Play] 1 of your opponent's Digimon can't attack until the end of their turn.");
        AllSupported(locker);
        var (state, engine) = NewMainPhase();
        var src = Digimon("B60-SRC", 5);
        var d = Put(state.Player, me, src);   // 진화원이 있어 지금은 받는다
        var l = Put(state.DummyOpponent, locker);
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, l, locker);
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.IsActive(d.LockUntil60((int)LockKind60.Attack, d.CannotAttackUntil), state), Logs(state));
        d.ReplaceSources59(new List<CardData>());   // 진화원이 없어졌다 → 받지 않는 동안 영향이 없다
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.IsActive(d.LockUntil60((int)LockKind60.Attack, d.CannotAttackUntil), state));
        d.AddBottomSource(src);   // 다시 받게 되면 그 시점부터 영향 (15-15-5-2)
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.IsActive(d.LockUntil60((int)LockKind60.Attack, d.CannotAttackUntil), state));
    }

    [Fact]
    public void OpponentSecurityMinus_IgnoredWhileImmune()
    {
        var me = Digimon(Uid("B60-SLI-"), 6, SourcelessImmune, dp: 9000);
        var minus = Digimon(Uid("B60-SAM-"), 4, "[On Play] 1 of your opponent's Digimon gains ＜Security A. -1＞ until the end of their turn.");
        AllSupported(minus);
        var (state, engine) = NewMainPhase();
        var src = Digimon("B60-SRC", 5);
        var d = Put(state.Player, me, src);
        var m = Put(state.DummyOpponent, minus);
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, m, minus);
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, d.GetSecurityAttackCount());
        d.ReplaceSources59(new List<CardData>());
        engine.RecalculateFieldBuffs();
        Assert.Equal(1, d.GetSecurityAttackCount());   // 15-15-5-4 받지 않는 디지몬은 ≪S 어택 -1≫을 가지지 않는다
    }

    [Fact]
    public void OwnLock_StillAppliesWhileImmune()
    {
        var me = Digimon(Uid("B60-SLI-"), 6, SourcelessImmune, dp: 9000);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        engine.Lock60(d, LockKind60.Block, state.TurnSequence + 1);   // 효과 처리 밖 = 상대의 효과가 아니다
        engine.RecalculateFieldBuffs();
        Assert.True(d.OppShield59);
        Assert.True(EffectRules.IsActive(d.LockUntil60((int)LockKind60.Block, d.CannotBlockUntil), state));
    }

    // =====================================================================
    // 2. AI 추천
    // =====================================================================

    [Fact]
    public void DigiBurst_AiTrashesSourceTheEffectUses_ST6_13()
    {
        const string text = "[Main] ＜Digi-Burst 1＞ (You may trash 1 of this Digimon's digivolution cards to activate the effect below.)\r\n・Play 1 purple level 3 Digimon card from your trash without paying its memory cost.";
        var me = Digimon(Uid("B60-CRES-"), 6, text, color: CardColor.Purple);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var lv4 = Digimon("B60-SRC4", 4, color: CardColor.Blue);
        var lv3 = Digimon("B60-SRC3P", 3, color: CardColor.Purple);
        var d = Put(state.Player, me, lv3, lv4);   // 아래 → 위 = lv4, lv3 (원래 추천이면 아래의 lv4를 파기)
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, d, me);
        Assert.Equal(new[] { lv4 }, d.GetEvolutionStackBottomUp().ToArray());
        Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, lv3));   // 파기한 Lv.3을 곧바로 등장 (공식 Q&A ST6-13 Q1)
    }

    private static string ChaosText(string gal) =>
        $"[Trash] [End of Your Turn] By Deleting 1 of your Digimon with a digivolution card and [{gal}] in its name, you may play this card without paying its cost.\r\n[On Play] Delete 1 of your opponent's Digimon with 7000 DP or less.";

    [Fact]
    public void UpgradeDeleteCost_Recommended_EX4_011()
    {
        string gal = "B60 Gallantmon " + Guid.NewGuid().ToString("N")[..4];
        var chaos = Digimon(Uid("B60-CGAL-"), 6, ChaosText(gal), color: CardColor.Red, playCost: 12, dp: 12000);
        AllSupported(chaos);
        var (state, engine) = NewMainPhase();
        var g = Put(state.Player, Digimon("B60-GAL6", 6, name: gal, dp: 12000), Digimon("B60-SRC", 5));
        state.Player.Trash.Add(chaos);
        engine.RunTrashEffects(EffectTrigger.EndOfYourTurn, state.Player);
        Assert.DoesNotContain(g, state.Player.BattleArea);
        Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, chaos));
    }

    [Fact]
    public void UpgradeDeleteCost_NotForHigherLevel_EX4_011()
    {
        string gal = "B60 Gallantmon " + Guid.NewGuid().ToString("N")[..4];
        var chaos = Digimon(Uid("B60-CGAL-"), 6, ChaosText(gal), color: CardColor.Red, playCost: 12, dp: 12000);
        var (state, engine) = NewMainPhase();
        var g = Put(state.Player, Digimon("B60-GAL7", 7, name: gal + " Crimson Mode", dp: 15000), Digimon("B60-SRC", 6));
        state.Player.Trash.Add(chaos);
        engine.RunTrashEffects(EffectTrigger.EndOfYourTurn, state.Player);
        Assert.Contains(g, state.Player.BattleArea);
        Assert.Contains(chaos, state.Player.Trash);
    }

    // =====================================================================
    // 4. 【시큐리티】 효과가 발휘되지 않는다 (13-1-8-2 · 13-1-8-4 · Q&A BT1-025 Q1 · BT3-097 Q1)
    // =====================================================================

    private static CardData SecurityAddOption() => Card(Uid("B60-SOPT-"), "[Security] Add this card to the hand.", CardType.Option, 0, 2, CardColor.Red);
    private static CardData SecurityTamer() => Card(Uid("B60-STAM-"), "[Security] Play this card without paying the cost.", CardType.Tamer, 0, 3, CardColor.Red);

    [Fact]
    public void EnemyOptionSecurityDoesNotActivate_BT12_112()
    {
        const string text = "[Your Turn] All of your opponent's [Security] effects on Option cards don't activate.";
        var shout = Digimon(Uid("B60-SHOUT-"), 6, text, dp: 12000);
        AllSupported(shout);
        var (state, engine) = NewMainPhase();
        Put(state.Player, shout);
        var atk = Put(state.Player, Digimon("B60-ATK", 5, dp: 7000));
        engine.RecalculateFieldBuffs();
        var opt = SecurityAddOption();
        var tamer = SecurityTamer();
        state.DummyOpponent.SecurityStack.Insert(0, opt);
        engine.AttackSecurityOrDirect(atk);
        Assert.Contains(opt, state.DummyOpponent.Trash);
        Assert.DoesNotContain(opt, state.DummyOpponent.Hand);
        // 테이머 카드의 【시큐리티】 효과는 그대로 (옵션만 — Q&A BT3-097 Q1)
        state.DummyOpponent.SecurityStack.Insert(0, tamer);
        atk.IsSuspended = false;
        engine.AttackSecurityOrDirect(atk);
        Assert.Contains(tamer, state.DummyOpponent.Tamers);
    }

    [Fact]
    public void AllSecurityOfCheckedCards_InheritedWhileNamed_EX6_010()
    {
        const string text = "[Your Turn] While this Digimon is [B60 RagnaLoardmon], the [Security] effects on cards checked by this Digimon don't activate.";
        var src = Digimon(Uid("B60-RAGSRC-"), 5, inherited: text);
        AllSupported(src);
        Assert.Contains("체크한 카드의 【시큐리티】 효과는 발휘하지 않는다", Ko(text));
        var (state, engine) = NewMainPhase();
        var atk = Put(state.Player, Digimon(Uid("B60-RAG-"), 6, name: "B60 RagnaLoardmon", dp: 12000), src);
        engine.RecalculateFieldBuffs();
        var tamer = SecurityTamer();
        state.DummyOpponent.SecurityStack.Insert(0, tamer);
        engine.AttackSecurityOrDirect(atk);
        Assert.DoesNotContain(tamer, state.DummyOpponent.Tamers);
        Assert.Contains(tamer, state.DummyOpponent.Trash);   // 13-1-8-4
    }

    [Fact]
    public void AllSecurity_NotWhenOtherName_EX6_010()
    {
        const string text = "[Your Turn] While this Digimon is [B60 RagnaLoardmon], the [Security] effects on cards checked by this Digimon don't activate.";
        var src = Digimon(Uid("B60-RAGSRC-"), 5, inherited: text);
        var (state, engine) = NewMainPhase();
        var atk = Put(state.Player, Digimon(Uid("B60-OTHER-"), 6, name: "B60 Other", dp: 12000), src);
        engine.RecalculateFieldBuffs();
        var tamer = SecurityTamer();
        state.DummyOpponent.SecurityStack.Insert(0, tamer);
        engine.AttackSecurityOrDirect(atk);
        Assert.Contains(tamer, state.DummyOpponent.Tamers);
    }

    [Fact]
    public void NoEnemySecurityForTheTurn_EX3_073()
    {
        const string text = "[When Digivolving] By returning 1 [B60 Dragon Mode] from this Digimon's digivolution cards to the bottom of its owner's deck, none of your opponent's [Security] effects can activate for the turn.";
        var me = Digimon(Uid("B60-IMP-"), 6, text, dp: 12000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var dm = Digimon(Uid("B60-DM-"), 5, name: "B60 Dragon Mode");
        var d = Put(state.Player, me, dm);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        Assert.Equal(state.TurnSequence, state.Player.NoEnemySecurityUntil60);
        Assert.Same(dm, state.Player.MainDeck.Last());
        var tamer = SecurityTamer();
        state.DummyOpponent.SecurityStack.Insert(0, tamer);
        engine.AttackSecurityOrDirect(d);
        Assert.Contains(tamer, state.DummyOpponent.Trash);
    }

    [Fact]
    public void GrantNegateOptionSecurity_OnlyOptions_BT3_097()
    {
        const string text = "[Main] 1 of your Digimon gains \"[Security] effects on Option cards checked by this Digimon don't activate.\" for the turn.";
        var option = Card(Uid("B60-OPT-"), text, CardType.Option, 0, 1, CardColor.Blue);
        AllSupported(option);
        Assert.Contains("옵션 카드의 【시큐리티】 효과는 발휘하지 않는다", Ko(text));
        var (state, engine) = NewMainPhase();
        var atk = Put(state.Player, Digimon("B60-ATK", 5, dp: 7000));
        state.Player.Hand.Add(option);
        Assert.True(engine.PlayCard(option).CanExecute, Logs(state));
        Assert.True(CardScripts.HasKeyword(atk, Keyword.NegateSecurityOptions));
        var opt = SecurityAddOption();
        state.DummyOpponent.SecurityStack.Insert(0, opt);
        engine.AttackSecurityOrDirect(atk);
        Assert.Contains(opt, state.DummyOpponent.Trash);
    }

    // =====================================================================
    // 3 · 5. 배틀 에어리어를 떠나지 않는다 · Lv.과 시큐리티 합계 · DP 비교 ≪충돌≫
    // =====================================================================

    private const string Bounce = "[On Play] Return 1 of your opponent's Digimon to the hand.";

    [Fact]
    public void CannotLeaveOtherThanOwnEffectsOrDeletion_EX6_044()
    {
        const string text = "[Opponent's Turn] While this Digimon is [B60 RagnaLoardmon], this Digimon can't leave the battle area other than by your effects or by deletion.";
        var src = Digimon(Uid("B60-RAGSRC-"), 5, inherited: text);
        AllSupported(src);
        var bouncer = Digimon(Uid("B60-BNC-"), 4, Bounce);
        AllSupported(bouncer);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon(Uid("B60-RAG-"), 6, name: "B60 RagnaLoardmon", dp: 12000), src);
        d.IsOwnersTurn = false;   // 상대의 턴
        var b = Put(state.DummyOpponent, bouncer);
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, b, bouncer);
        Assert.Contains(d, state.Player.BattleArea);
        engine.DeleteFrom(state.Player, d, GoldfishEngine.DeletionCause.Effect);   // 소멸은 된다
        Assert.DoesNotContain(d, state.Player.BattleArea);
    }

    [Fact]
    public void CannotLeaveOtherThanDeletion_Timed_BT16_051()
    {
        const string text = "[Start of Your Main Phase] By placing 1 [B60 Kosuke] from your hand as this Digimon's bottom digivolution card, until the end of your opponent's turn, this Digimon can't leave the battle area other than by deletion.";
        var dorumon = Digimon(Uid("B60-DORU-"), 3, text);
        AllSupported(dorumon);
        var bouncer = Digimon(Uid("B60-BNC-"), 4, Bounce);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, dorumon);
        state.Player.Hand.Add(Card(Uid("B60-KOS-"), "", CardType.Tamer, 0, 2, name: "B60 Kosuke"));
        for (int i = 0; i < 3; i++) state.Player.Hand.Add(Digimon("B60-HAND" + i, 3));
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, d, dorumon);
        Assert.Single(d.EvolutionSources);
        Assert.True(CardScripts.HasKeyword(d, Keyword.CannotLeaveExceptDeletion60), Logs(state));
        var b = Put(state.DummyOpponent, bouncer);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, b, bouncer);
        Assert.Contains(d, state.Player.BattleArea);
    }

    [Fact]
    public void SecurityMinusForLevelAtLeastTotalSecurity_EX5_033()
    {
        const string text = "[Opponent's Turn] All of your opponent's Digimon with levels greater than or equal to the total cards in both players' security stacks gain ＜Security A. -2＞ (This Digimon checks 2 additional security cards.).";
        var mita = Digimon(Uid("B60-MITA-"), 6, text, color: CardColor.Yellow);
        AllSupported(mita);
        var (state, engine) = NewMainPhase(security: 2);   // 합계 4
        var m = Put(state.DummyOpponent, mita);
        m.IsOwnersTurn = false;
        var lv5 = Put(state.Player, Digimon("B60-L5", 5, dp: 7000));
        var lv3 = Put(state.Player, Digimon("B60-L3", 3, dp: 3000));
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, lv5.GetSecurityAttackCount());   // 1 - 2 → 0
        Assert.Equal(1, lv3.GetSecurityAttackCount());
    }

    [Fact]
    public void CollisionForDigimonWithLessOrEqualDp_BT18_083()
    {
        const string text = "[All Turns] All Digimon with as much or less DP as this Digimon gain ＜Collision＞ (During this Digimon's attack, all of your opponent's Digimon gain ＜Blocker＞, and must block if possible.)";
        var lord = Digimon(Uid("B60-LORD-"), 6, text, dp: 10000);
        AllSupported(lord);
        var (state, engine) = NewMainPhase();
        var l = Put(state.Player, lord);
        var small = Put(state.Player, Digimon("B60-SMALL", 4, dp: 5000));
        var big = Put(state.DummyOpponent, Digimon("B60-BIG", 7, dp: 15000));
        var oppSmall = Put(state.DummyOpponent, Digimon("B60-OSMALL", 5, dp: 8000));
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(l, Keyword.Collision));
        Assert.True(CardScripts.HasKeyword(small, Keyword.Collision));
        Assert.True(CardScripts.HasKeyword(oppSmall, Keyword.Collision));
        Assert.False(CardScripts.HasKeyword(big, Keyword.Collision));
    }

    // =====================================================================
    // 이전 문장 · 한글은 그대로
    // =====================================================================

    [Theory]
    [InlineData("[When Digivolving] 1 of your opponent's Digimon can't attack or block until the end of their turn.")]
    [InlineData("[On Play] 1 of your opponent's Digimon gains ＜Security A. -1＞ until the end of their turn.")]
    [InlineData("[Your Turn] This Digimon doesn't activate [Security] effects on Option cards it checks.")]
    public void EarlierSentences_Unchanged(string clause)
    {
        var c = Digimon(Uid("B60-OLD-"), 5, clause);
        AllSupported(c);
        Assert.DoesNotContain("⟦", Ko(clause));
    }
}
