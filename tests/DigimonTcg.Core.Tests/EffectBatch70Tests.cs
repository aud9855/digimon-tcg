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
/// v0.70.0 미지원 문장 44차 묶음: 앞면 시큐리티 효과의 공식 표기 "[시큐리티]【서로의 턴】" · 사건의 쉼표 목록 가운데 항목 (EX6-074 · EX2-060 · EX9-068) ·
/// 괄호 안의 명칭 제외 "(other than [DoruGreymon], ~)" (BT4-092 · BT5-015 등 17장) · 특징 용어 (「마수형」 · 「4성수」 등) ·
/// 듀얼 카드 "[Security] [On Deletion] use this card" (EX14-012) · 받는 디지몬마다 다른 앞면 시큐리티 지속 효과 (EX9-072) ·
/// 사용 코스트가 상대의 메모리 / 그 디지몬의 Lv. 이하인 옵션 (BT24-085 · ST22-07) · [상대의 메인 페이즈 개시 시] (EX3-024) · 테이머를 디지몬으로 (BT13-099)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch70Tests
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

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, int playCost = 3, int evoCost = 1, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, evoCost, "", traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int security = 5)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B70-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B70-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B70-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B70-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    // 1. 앞면 시큐리티 효과의 공식 표기 "[시큐리티]【서로의 턴】" (BT25-095 공식 한글)
    // =====================================================================

    [Fact]
    public void FaceUpSecurityEffects_UseOfficialNotation()
    {
        Assert.StartsWith("[시큐리티]【서로의 턴】 ", Ko("[Security] [All Turns] All of your [B70Base] trait Digimon get +1000 DP."));
        Assert.StartsWith("[시큐리티]【상대의 턴】 ", Ko("[Security] [Opponent's Turn] All of your [B70Base] trait Digimon gain ＜Blocker＞ (This Digimon can block in the blocker timing.)"));
        Assert.StartsWith("[시큐리티]【자신의 턴】 [턴에 1회] ", Ko("[Security] [Your Turn] [Once Per Turn] When any of your Digimon would digivolve into a Digimon card with the [Royal Base] trait, reduce the digivolution cost by 1."));
        // 체크했을 때의 【시큐리티】 효과는 그대로
        Assert.StartsWith("【시큐리티】 ", Ko("Security Effect [Security] Play this card without paying the cost."));
        Assert.StartsWith("【시큐리티】 【소멸 시】 ", Ko("[Security] [On Deletion] Delete 1 of your opponent's Digimon with the lowest level."));
        Assert.StartsWith("[시큐리티]【상대의 턴 종료 시】", Ko("[Security] [End of Opponent's Turn] Play this card without paying the cost."));
    }

    // =====================================================================
    // 2. 사건의 쉼표 목록 — 가운데 항목 (전에는 "[[B70Mid" 로 바뀌어 맞지 않았다)
    // =====================================================================

    [Fact]
    public void EventTraitList_MiddleItemCounts()
    {
        var tamer = Card(Uid("B70-TLIST-"), "[Your Turn] When one of your Digimon with the [B70First], [B70Mid] or [B70Last] trait is played, gain 1 memory.", CardType.Tamer);
        AllSupported(tamer);
        foreach (var (trait, gain) in new[] { ("B70First", 1), ("B70Mid", 1), ("B70Last", 1), ("B70None", 0) })
        {
            var (state, engine) = NewMainPhase(memory: 3);
            engine.AddTamer(state.Player, tamer);
            var d = Put(state.Player, Digimon(Uid("B70-P-"), 4, traits: trait));
            engine.FireForSide(EffectTrigger.AllyDigimonPlayed, state.Player, d);
            Assert.True(3 + gain == state.MemoryGauge, trait + ": " + Logs(state));
        }
    }

    [Fact]
    public void EventNameList_AttackWithMiddleName()
    {
        var tamer = Card(Uid("B70-NLIST-"), "[Your Turn] When you attack with a Digimon with [Alfa70], [Beta70], [Gamma70] or [Delta70] in its name, gain 1 memory.", CardType.Tamer);
        AllSupported(tamer);
        foreach (var (name, gain) in new[] { ("Beta70mon", 1), ("Gamma70mon", 1), ("Delta70mon", 1), ("Omega70mon", 0) })
        {
            var (state, engine) = NewMainPhase(memory: 3);
            engine.AddTamer(state.Player, tamer);
            var d = Put(state.Player, Digimon(Uid("B70-A-"), 4, name: name));
            engine.FireForSide(EffectTrigger.AllyDigimonAttacks, state.Player, d);
            Assert.True(3 + gain == state.MemoryGauge, name + ": " + Logs(state));
        }
    }

    // =====================================================================
    // 3. 괄호 안의 명칭 제외 "(other than [X], [Y] or [Z])" — 전에는 설명 괄호로 지워 제외가 빠졌다
    // =====================================================================

    [Fact]
    public void OtherThanNames_ExcludedOnEventAndStatic()
    {
        var tamer = Card(Uid("B70-EXC-"), "[Your Turn] When one of your Digimon with [Kappa70mon] in its name attacks (other than [DoruKappa70mon], [BurnKappa70mon] or [DexKappa70mon]), gain 1 memory.", CardType.Tamer);
        AllSupported(tamer);
        foreach (var (name, gain) in new[] { ("MetalKappa70mon", 1), ("BurnKappa70mon", 0), ("DoruKappa70mon", 0), ("DexKappa70mon", 0) })
        {
            var (state, engine) = NewMainPhase(memory: 3);
            engine.AddTamer(state.Player, tamer);
            var d = Put(state.Player, Digimon(Uid("B70-E-"), 5, name: name));
            engine.FireForSide(EffectTrigger.AllyDigimonAttacks, state.Player, d);
            Assert.True(3 + gain == state.MemoryGauge, name + ": " + Logs(state));
        }
        // 진화원 효과 "When this Digimon has ~ in its name (other than ~), it gets +2000 DP"
        var src = Card(Uid("B70-EXCI-"), "", CardType.Digimon, 3, inherited: "[Your Turn] When this Digimon has [Omni70mon] or [Kappa70mon] in its name (other than [DoruKappa70mon], [BurnKappa70mon] or [DexKappa70mon]), it gets +2000 DP.");
        AllSupported(src);
        foreach (var (name, plus) in new[] { ("MetalKappa70mon", 2000), ("BurnKappa70mon", 0), ("Omni70mon", 2000) })
        {
            var (state, engine) = NewMainPhase();
            var d = Put(state.Player, Digimon(Uid("B70-ES-"), 5, name: name, dp: 7000), false, src);
            engine.RecalculateFieldBuffs();
            Assert.True(7000 + plus == d.GetCurrentDP(), name);
        }
    }

    [Fact]
    public void OtherThanNames_PickFromRevealExcludesName()
    {
        var card = Digimon(Uid("B70-REV-"), 3, "[On Play] Reveal the top 3 cards of your deck. Add 1 Digimon card with [Kappa70mon] in its name (other than [BurnKappa70mon]) among them to the hand. Return the rest to the bottom of the deck.");
        AllSupported(card);
        var (state, engine) = NewMainPhase();
        var burn = Digimon(Uid("B70-BURN-"), 4, name: "BurnKappa70mon");
        state.Player.MainDeck.InsertRange(0, new[] { burn, Digimon(Uid("B70-F-"), 3), Digimon(Uid("B70-F-"), 3) });
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(burn, state.Player.Hand);
        var (state2, engine2) = NewMainPhase();
        var metal = Digimon(Uid("B70-METAL-"), 5, name: "MetalKappa70mon");
        state2.Player.MainDeck.InsertRange(0, new[] { burn, metal, Digimon(Uid("B70-F-"), 3) });
        var me2 = Put(state2.Player, card);
        engine2.RunEffects(EffectTrigger.OnPlay, state2.Player, me2, card);
        Assert.Contains(metal, state2.Player.Hand);
    }

    [Fact]
    public void OtherThanNames_KoreanUnchanged()
    {
        // 한글은 전과 같이 원문 괄호를 옮긴다 (이름에 제외 표식이 새지 않는다)
        string ko = Ko("[Your Turn] When one of your Digimon with [Greymon] in its name attacks (other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon]), you may suspend this Tamer to gain 1 memory.");
        Assert.DoesNotContain(((char)0x2284).ToString(), ko);
    }

    // =====================================================================
    // 4. 특징 용어 (공식 한글 데이터가 있을 때만 — 개인 PC)
    // =====================================================================

    [Fact]
    public void Glossary_AnimalSovereignAndOthers()
    {
        string? ko = KoreanEffectRenderer.RenderClause("[On Deletion] You may play 1 Digimon card with [Beast], [Animal] or [Sovereign] in any of its traits (other than [Sea Animal]) and 3000 DP or less from your hand without paying the cost.");
        string? saur = KoreanEffectRenderer.RenderClause("[When Attacking] [Once Per Turn] If this Digimon has [Dragon], [saur] or [Ceratopsian] in any of its traits, ＜Draw 1＞ (Draw 1 card from your deck.)");
        if (KoreanCardData.Count > 0)
        {
            Assert.NotNull(ko);
            Assert.Contains("「수」/「짐승」을 포함", ko);   // v0.74.0 특징 포함은 공식 부분 낱말 (공식 BT13-048 「수」/「짐승」)
            Assert.DoesNotContain("「Animal」", ko);
            Assert.Contains("「룡」", saur);
        }
    }

    // =====================================================================
    // 5. 듀얼 카드 "[Security] [On Deletion] You may use this card without paying the cost." (EX14-012 — 종합 룰 4-6 · 9-1)
    // =====================================================================

    private static CardData Dual(string main, CardColor color = CardColor.Red) => new()
    {
        CardId = Uid("B70-DUAL-"), Name = "B70Dual", Type = CardType.Digimon, Color = color, Level = 6, DP = 12000, PlayCost = 7, EvoCost = 3, EvoRequirementLevel = 5, EvoRequirementColor = color,
        EffectText = main, InheritedEffectText = "[Main] Delete 1 of your opponent's Digimon.", IsDual = true,
    };

    private const string DualText = "[Security] [On Deletion] You may use this card without paying the cost.";

    [Fact]
    public void DualUseThisCard_OnDeletion_UsesOptionSide_ThenTrash()
    {
        var dual = Dual(DualText);
        AllSupported(dual);
        Assert.Equal("【시큐리티】 【소멸 시】 이 카드를 코스트를 지불하지 않고 사용할 수 있다.", Ko(DualText));
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon(Uid("B70-RED-"), 3, color: CardColor.Red));   // 색 조건 (9-1-1)
        var me = Put(state.Player, dual);
        var foe = Put(state.DummyOpponent, Digimon(Uid("B70-FOE-"), 5, dp: 9000));
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
        Assert.Equal(1, state.Player.Trash.Count(x => ReferenceEquals(x, dual)));   // 사용 뒤 트래시 (9-1-5) — 한 장만
    }

    [Fact]
    public void DualUseThisCard_NeedsColorRequirement()
    {
        var dual = Dual(DualText);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, dual);
        Put(state.Player, Digimon(Uid("B70-BLUE-"), 3, color: CardColor.Blue));
        state.Player.BattleArea.Remove(me);   // 빨강 디지몬이 없다
        state.Player.Trash.Add(dual);
        var foe = Put(state.DummyOpponent, Digimon(Uid("B70-FOE-"), 5, dp: 9000));
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, me, dual);
        Assert.Contains(foe, state.DummyOpponent.BattleArea);
        Assert.Contains(dual, state.Player.Trash);
    }

    [Fact]
    public void DualUseThisCard_FromSecurity_NoBattle()
    {
        var dual = Dual(DualText);
        var (state, engine) = NewMainPhase();
        state.DummyOpponent.SecurityStack.Insert(0, dual);
        Put(state.DummyOpponent, Digimon(Uid("B70-ORED-"), 3, color: CardColor.Red));
        var bystander = Put(state.Player, Digimon(Uid("B70-BY-"), 3, dp: 3000));
        var atk = Put(state.Player, Digimon(Uid("B70-ATK-"), 6, dp: 20000));
        atk.TurnPlayed = 0;
        int sec = state.DummyOpponent.SecurityStack.Count;
        engine.AttackSecurityOrDirect(atk);
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        // 상대가 옵션 쪽 [메인]으로 디지몬 1마리를 소멸시키고, 배틀은 없다 (어택한 디지몬이 남았으면 배틀 승리 기록이 아니라 "옵션으로 사용")
        Assert.Equal(sec - 1, state.DummyOpponent.SecurityStack.Count);
        Assert.Contains(dual, state.DummyOpponent.Trash);
        Assert.True(!state.Player.BattleArea.Contains(atk) || !state.Player.BattleArea.Contains(bystander), Logs(state));
        Assert.DoesNotContain(state.SecurityCheckHistory, h => ReferenceEquals(h.SecurityCard, dual) && h.OutcomeSummary.Contains("배틀 승리"));
    }

    // =====================================================================
    // 6. 받는 디지몬마다 다른 앞면 시큐리티 지속 효과 (EX9-072)
    // =====================================================================

    [Fact]
    public void FaceUpPerFaceDownSources_ValuePerReceiver()
    {
        var sec = Card(Uid("B70-PFD-"), "[Security] [All Turns] All of your [B70DM] trait Digimon get +1000 DP for each of their face-down digivolution cards.");
        AllSupported(sec);
        Assert.DoesNotContain(CardScripts.For(sec)!.Effects, e => e.Trigger == EffectTrigger.Security);
        Assert.StartsWith("[시큐리티]【서로의 턴】 ", Ko(sec.EffectText));
        var (state, engine) = NewMainPhase();
        var s1 = Digimon(Uid("B70-S1-"), 3); var s2 = Digimon(Uid("B70-S2-"), 3); var s3 = Digimon(Uid("B70-S3-"), 4);
        var a = Put(state.Player, Digimon(Uid("B70-DM-"), 5, dp: 6000, traits: "B70DM"), false, s1, s2, s3);
        a.FaceDownSources.Add(s1); a.FaceDownSources.Add(s2);
        var b = Put(state.Player, Digimon(Uid("B70-DM-"), 5, dp: 6000, traits: "B70DM"), false, Digimon(Uid("B70-S4-"), 3));
        var other = Put(state.Player, Digimon(Uid("B70-NDM-"), 5, dp: 6000), false, s1);
        state.Player.SecurityStack.Add(sec);
        state.Player.FaceUpSecurity.Add(sec);
        engine.RecalculateFieldBuffs();
        Assert.Equal(8000, a.GetCurrentDP());
        Assert.Equal(6000, b.GetCurrentDP());
        Assert.Equal(6000, other.GetCurrentDP());
        state.Player.FaceUpSecurity.Clear();
        engine.RecalculateFieldBuffs();
        Assert.Equal(6000, a.GetCurrentDP());
    }

    // =====================================================================
    // 7. 사용 코스트가 상대의 메모리 / 그 디지몬의 Lv. 이하인 옵션 (BT24-085 · ST22-07)
    // =====================================================================

    [Fact]
    public void UseOptionUpToOpponentsMemory()
    {
        var tamer = Card(Uid("B70-OPPMEM-"), "[End of Your Turn] By suspending this Tamer, you may use 1 [B70TS] trait Option card with as high or lower a use cost as your opponent's memory from your hand without paying the cost.", CardType.Tamer, color: CardColor.Yellow);
        AllSupported(tamer);
        Assert.Contains("사용 코스트가 상대의 메모리 이하인", Ko(tamer.EffectText));
        foreach (var (gauge, expectUsed) in new[] { (-3, 3), (-2, 2), (1, 0) })
        {
            var (state, engine) = NewMainPhase(memory: gauge);
            engine.AddTamer(state.Player, tamer);
            var o2 = Card(Uid("B70-O2-"), "[Main] Gain 1 memory.", playCost: 2, color: CardColor.Yellow, traits: "B70TS");
            var o3 = Card(Uid("B70-O3-"), "[Main] Gain 1 memory.", playCost: 3, color: CardColor.Yellow, traits: "B70TS");
            state.Player.Hand.AddRange(new[] { o2, o3 });
            Put(state.DummyOpponent, Digimon(Uid("B70-OF-"), 4));
            engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, null, tamer);
            if (expectUsed == 0) { Assert.Contains(o2, state.Player.Hand); Assert.Contains(o3, state.Player.Hand); }
            else Assert.DoesNotContain(expectUsed == 3 ? o3 : o2, state.Player.Hand);
            if (expectUsed == 2) Assert.Contains(o3, state.Player.Hand);
        }
    }

    [Fact]
    public void UseOptionUpToAttackersLevel_FromUnderTamer()
    {
        var tamer = Card(Uid("B70-LVUSE-"), "[Your Turn] When one of your Digimon with [Rena70mon] in its name attacks, by suspending this Tamer, you may use 1 [B70Plug] trait Option card with as high or lower a use cost as that Digimon's level from under this Tamer without paying the cost.", CardType.Tamer, color: CardColor.Yellow);
        AllSupported(tamer);
        Assert.Contains("이 테이머 아래에서", Ko(tamer.EffectText));
        Assert.Contains("사용 코스트가 그 디지몬의 Lv. 이하인", Ko(tamer.EffectText));
        var (state, engine) = NewMainPhase(memory: 3);
        engine.AddTamer(state.Player, tamer);
        var hi = Card(Uid("B70-HI-"), "[Main] Gain 1 memory.", playCost: 5, color: CardColor.Yellow, traits: "B70Plug");
        var lo = Card(Uid("B70-LO-"), "[Main] Gain 1 memory.", playCost: 4, color: CardColor.Yellow, traits: "B70Plug");
        state.Player.UnderTamers.AddRange(new[] { hi, lo });
        var atk = Put(state.Player, Digimon(Uid("B70-RENA-"), 4, name: "Kyubi Rena70mon", color: CardColor.Yellow));
        engine.FireForSide(EffectTrigger.AllyDigimonAttacks, state.Player, atk);
        Assert.Contains(hi, state.Player.UnderTamers);   // Lv.4보다 높다
        Assert.DoesNotContain(lo, state.Player.UnderTamers);
        Assert.Equal(4, state.MemoryGauge);
    }

    // =====================================================================
    // 8. [상대의 메인 페이즈 개시 시] (EX3-024) · 테이머를 디지몬으로 (BT13-099)
    // =====================================================================

    [Fact]
    public void StartOfOpponentsMainPhase_ForcesAttack()
    {
        const string text = "[Start of Opponent's Main Phase] By suspending 1 of your Digimon with [Dra70mon] in its name, your opponent attacks with 1 of their Digimon.";
        var slayer = Digimon(Uid("B70-SLAY-"), 6, text, name: "Slayer Dra70mon");
        AllSupported(slayer);
        Assert.Contains(CardScripts.For(slayer)!.Effects, e => e.Trigger == EffectTrigger.StartOfOpponentsMainPhase70);
        Assert.Equal("【상대의 메인 페이즈 개시 시】 명칭에 「Dra70mon」을 포함하는 자신의 디지몬 1마리를 레스트시키는 것으로, 상대는 본인의 디지몬 1마리를 어택시킨다.", Ko(text));
        Assert.Equal("상대의 메인 페이즈 개시 시", SentenceParser.TriggerLabel69(EffectTrigger.StartOfOpponentsMainPhase70));
        // AI의 카드 → 플레이어의 메인 페이즈 개시 시에 플레이어의 디지몬이 어택한다
        var (state, engine) = NewMainPhase();
        var ai = Put(state.DummyOpponent, slayer);
        ai.IsOwnersTurn = false;
        var mine = Put(state.Player, Digimon(Uid("B70-MINE-"), 5, dp: 5000));
        mine.TurnPlayed = 0;
        engine.RunEffects(EffectTrigger.StartOfOpponentsMainPhase70, state.DummyOpponent, ai, slayer);
        Assert.True(ai.IsSuspended, Logs(state));
        Assert.True(mine.IsSuspended, Logs(state));
    }

    [Fact]
    public void TreatThisTamerAs_DigimonWithBlocker()
    {
        var tamer = Card(Uid("B70-TAS-"), "[End of Your Turn] [Once Per Turn] If the total cards in both players' security stacks is 6 or less, until the end of your opponent's turn, treat this Tamer as a 3000 DP Digimon that can't digivolve and gains ＜Blocker＞ (This Digimon can block in the blocker timing.).", CardType.Tamer, color: CardColor.Yellow);
        AllSupported(tamer);
        Assert.Equal("【자신의 턴 종료 시】 [턴에 1회] 서로의 시큐리티 합계가 6장 이하라면, 상대의 턴 종료 시까지, 이 테이머는 디지몬・DP 3000으로도 취급하며, 《블로커》를 얻고, 진화할 수 없다.", Ko(tamer.EffectText));
        foreach (var (each, proxy) in new[] { (3, true), (4, false) })
        {
            var (state, engine) = NewMainPhase(security: each);
            engine.AddTamer(state.Player, tamer);
            engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, null, tamer);
            engine.RecalculateFieldBuffs();
            var p = state.Player.BattleArea.FirstOrDefault(d => d.TamerProxyOf != null);
            Assert.Equal(proxy, p != null);
            if (p != null) { Assert.Equal(3000, p.GetCurrentDP()); Assert.True(CardScripts.HasKeyword(p, Keyword.Blocker)); }
        }
    }
}
