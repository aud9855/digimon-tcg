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
/// v0.69.0 미지원 문장 43차 묶음: 로그의 시점 이름 한글 · 앞면 시큐리티의 지속 효과 (종합 룰 15-14-5-1 — BT26-100 등) ·
/// 앞면 시큐리티의 "떠나려 할 때" (ST22-10) · 상대의 디지몬이 어택했을 때 (BT19-100) · 앞면 시큐리티의 카드로 진화 (BT19-084) ·
/// 테이머 진화 코스트 감소 (BT22-090 · BT22-101) · 되돌린 카드와 같은 색 (EX10-068) · 진화한 디지몬과 비교 (BT11-094 · BT11-093 · BT22-093) ·
/// 그 디지몬을 패 · 트래시의 카드로 진화 (BT12-090 · BT22-102) · BT20-053 · BT11-089
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch69Tests
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
            state.Player.MainDeck.Add(Digimon("B69-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B69-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B69-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B69-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static void FaceUp(PlayerZones z, CardData card) { z.SecurityStack.Add(card); z.FaceUpSecurity.Add(card); }

    private static void Attack(GoldfishEngine engine, GameState state, CardInstance atk)
    {
        atk.TurnPlayed = 0;
        engine.AttackSecurityOrDirect(atk);
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
    }

    // =====================================================================
    // 1. 로그의 시점 이름 (영어 enum 이름이 남지 않는다)
    // =====================================================================

    [Fact]
    public void EveryTrigger_HasKoreanLabel_WithoutSecurityOrMainRouting()
    {
        foreach (var t in Enum.GetValues<EffectTrigger>())
        {
            string label = SentenceParser.TriggerLabel69(t);
            Assert.NotEqual(t.ToString(), label);
            Assert.DoesNotMatch("[A-Za-z]{4,}", label);
            // Emit은 "시큐리티" · "메인" 라벨을 결과 메시지로 보낸다 — 원래 그런 시점만
            if (t != EffectTrigger.Security) Assert.DoesNotContain("시큐리티", label);
            if (t != EffectTrigger.Main) Assert.NotEqual("메인", label);
        }
        Assert.Equal("자신의 디지몬이 어택했을 때", SentenceParser.TriggerLabel69(EffectTrigger.AllyDigimonAttacks));
        Assert.Equal("이 디지몬이 배틀 에어리어를 떠나려 할 때", SentenceParser.TriggerLabel69(EffectTrigger.ThisDigimonWouldLeave));
    }

    // =====================================================================
    // 2. 앞면 시큐리티의 지속 효과 (종합 룰 15-14-5-1)
    // =====================================================================

    private const string SecDp = "[Security] [All Turns] All of your [B69Base] trait Digimon get +1000 DP.";

    [Fact]
    public void FaceUpSecurityStatic_AppliesOnlyWhileFaceUp_AndIsNotASecurityEffect()
    {
        var sec = Card(Uid("B69-SECDP-"), SecDp, playCost: 2);
        AllSupported(sec);
        Assert.DoesNotContain(CardScripts.For(sec)!.Effects, e => e.Trigger == EffectTrigger.Security);   // 체크했을 때의 【시큐리티】 효과가 아니다
        Assert.Contains(CardScripts.For(sec)!.Statics, s => s.Source == EffectSource.FaceUpSecurity68);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B69-BASE", 4, dp: 4000, traits: "B69Base"));
        var other = Put(state.Player, Digimon("B69-OTHER", 4, dp: 4000));
        state.Player.SecurityStack.Add(sec);   // 뒷면
        engine.RecalculateFieldBuffs();
        Assert.Equal(4000, d.GetCurrentDP());
        state.Player.FaceUpSecurity.Add(sec);   // 앞면
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, d.GetCurrentDP());
        Assert.Equal(4000, other.GetCurrentDP());
        state.Player.SecurityStack.Remove(sec);   // 시큐리티를 떠났다
        engine.RecalculateFieldBuffs();
        Assert.Equal(4000, d.GetCurrentDP());
        Assert.Equal(Ko(SecDp), Ko(SecDp));   // 한글은 그대로 (원래 규칙의 생성 한글)
    }

    [Fact]
    public void FaceUpSecurityStatic_DigimonCardOnField_DoesNotApplyItsSecurityStatic()
    {
        var card = Digimon(Uid("B69-RB-"), 4, SecDp, dp: 4000, traits: "B69Base");
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, card);
        engine.RecalculateFieldBuffs();
        Assert.Equal(4000, d.GetCurrentDP());
    }

    private const string Bt26100 = "[Security] [All Turns] All of your [B69Titan] trait Digimon gain ＜Blocker＞ (This Digimon can block in the blocker timing.) While you have a Digimon with [B69Pluto] in its name, they also get +3000 DP.";

    [Fact]
    public void Bt26100_BlockerAndConditionalDp_FromFaceUpSecurity()
    {
        var sec = Card(Uid("B69-DF-"), Bt26100);
        AllSupported(sec);
        Assert.StartsWith("【시큐리티】 【서로의 턴】", Ko(Bt26100));
        Assert.Contains("DP를 +3000 한다", Ko(Bt26100));
        var (state, engine) = NewMainPhase();
        var titan = Put(state.Player, Digimon("B69-TITAN", 4, dp: 4000, traits: "B69Titan"));
        FaceUp(state.Player, sec);
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(titan, Keyword.Blocker));
        Assert.Equal(4000, titan.GetCurrentDP());
        Put(state.Player, Digimon("B69-PLUTO", 6, name: "B69Pluto", dp: 11000));
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, titan.GetCurrentDP());
        state.Player.FaceUpSecurity.Clear();
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(titan, Keyword.Blocker));
        Assert.Equal(4000, titan.GetCurrentDP());
    }

    [Fact]
    public void FaceUpSecurityStatic_OpponentsTurnOnly()
    {
        var sec = Card(Uid("B69-OT-"), "[Security] [Opponent's Turn] All of your [B69Base] trait Digimon gain ＜Blocker＞ (This Digimon can block in the blocker timing.)");
        AllSupported(sec);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B69-BASE2", 4, traits: "B69Base"));
        FaceUp(state.Player, sec);
        var od = Put(state.DummyOpponent, Digimon("B69-OBASE", 4, traits: "B69Base"));
        od.IsOwnersTurn = false;
        FaceUp(state.DummyOpponent, Card(Uid("B69-OT2-"), "[Security] [Opponent's Turn] All of your [B69Base] trait Digimon gain ＜Blocker＞ (This Digimon can block in the blocker timing.)"));
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(d, Keyword.Blocker));   // 자신의 턴에는 없다
        Assert.True(CardScripts.HasKeyword(od, Keyword.Blocker));   // 플레이어의 턴 = AI에게는 상대의 턴
    }

    // =====================================================================
    // 3. ST22-10 앞면 시큐리티의 이 카드를 파기하는 것으로 떠나지 않는다
    // =====================================================================

    private const string Guard = "[Security] [All Turns] When any of your Digimon with [B69Ren] or [B69Kyu] in their names would leave the battle area other than by battle, by trashing this card, 1 of those Digimon doesn't leave.";

    [Fact]
    public void St2210_FaceUpGuard_PreventsEffectDeletion_TrashesItself()
    {
        var sec = Card(Uid("B69-AM-"), Guard + "\nSecurity Effect [Security] 1 of your opponent's Digimon gets -9000 DP for the turn.", playCost: 6);
        AllSupported(sec);
        Assert.Contains("이 카드를 파기하는 것으로, 그 디지몬 1마리는 벗어나지 않는다", Ko(Guard));
        var (state, engine) = NewMainPhase();
        FaceUp(state.Player, sec);
        var ren = Put(state.Player, Digimon("B69-REN", 3, name: "B69Ren"));
        engine.DeleteByOpponentEffect(state.Player, ren);
        Assert.Contains(ren, state.Player.BattleArea);
        Assert.Empty(state.Player.FaceUpSecurity);
        Assert.Contains(sec, state.Player.Trash);
    }

    [Fact]
    public void St2210_NotInBattle_NotOtherNames_NotFaceDown()
    {
        var sec = Card(Uid("B69-AM2-"), Guard);
        var (state, engine) = NewMainPhase();
        FaceUp(state.Player, sec);
        var plain = Put(state.Player, Digimon("B69-PLAIN", 3));
        engine.DeleteByOpponentEffect(state.Player, plain);
        Assert.DoesNotContain(plain, state.Player.BattleArea);
        Assert.Single(state.Player.FaceUpSecurity);
        var ren = Put(state.Player, Digimon("B69-REN2", 3, name: "B69Ren"));
        Assert.False(engine.TryPreventLeave(state.Player, ren, deletion: true, inBattle: true));   // 배틀로 떠나는 것은 아니다
        Assert.Single(state.Player.FaceUpSecurity);
        state.Player.FaceUpSecurity.Clear();   // 뒷면이면 효과가 없다
        engine.DeleteByOpponentEffect(state.Player, ren);
        Assert.DoesNotContain(ren, state.Player.BattleArea);
    }

    // =====================================================================
    // 4. BT19-100 「마더 디・리퍼」의 진화원 1장마다 (육성 에어리어의 디지타마) · 【시큐리티】 진화원 매수 이하의 등장 코스트
    // =====================================================================

    private const string Dzone = "[Security] [Opponent's Turn] When an opponent's Digimon attacks, if all of your Digimon and Tamers have the [B69DR] trait, for each of 1 of your [B69Mother]'s digivolution cards, the attacking Digimon get -1000 DP for the turn.";

    [Fact]
    public void Bt19100_AttackingDigimonLosesDpPerMotherSource()
    {
        var sec = Card(Uid("B69-DZ-"), Dzone);
        AllSupported(sec);
        Assert.Contains("1마리의 진화원 1장마다 DP -1000 한다", Ko(Dzone));
        Assert.Contains(CardScripts.For(sec)!.Effects, e => e.Source == EffectSource.FaceUpSecurity68 && e.Trigger == EffectTrigger.OpponentDigimonAttacks);
        var (state, engine) = NewMainPhase();
        var opp = state.DummyOpponent;   // 플레이어의 턴 = AI에게는 상대의 턴
        FaceUp(opp, sec);
        var mother = new CardInstance(Card("B69-MOTHER", "", CardType.Digitama, level: 2, name: "B69Mother"), 1);
        mother.AddBottomSource(Digimon("B69-S1", 3)); mother.AddBottomSource(Digimon("B69-S2", 3)); mother.AddBottomSource(Digimon("B69-S3", 4));
        opp.BreedingArea = mother;   // 「마더 디・리퍼」는 디지타마 — 육성 에어리어
        Put(opp, Digimon("B69-DRD", 4, traits: "B69DR")).IsOwnersTurn = false;
        var attacker = Put(state.Player, Digimon("B69-ATK", 6, dp: 12000));
        engine.FireForSide(EffectTrigger.OpponentDigimonAttacks, opp, attacker);
        Assert.Equal(9000, attacker.GetCurrentDP());
        opp.Tamers.Add(Card("B69-T", "", CardType.Tamer));   // 특징이 없는 테이머 — 전부가 아니다 (공식 Q&A BT19-100 Q1)
        var attacker2 = Put(state.Player, Digimon("B69-ATK2", 6, dp: 12000));
        engine.FireForSide(EffectTrigger.OpponentDigimonAttacks, opp, attacker2);
        Assert.Equal(12000, attacker2.GetCurrentDP());
    }

    [Fact]
    public void Bt19100_SecurityPlay_CostUpToMotherSources()
    {
        const string text = "Security Effect [Security] You may play 1 [B69DR] trait card with a play cost equal to or lower than the number of digivolution cards of 1 of your [B69Mother]'s from your hand without paying the cost.";
        var sec = Card(Uid("B69-DZS-"), text);
        AllSupported(sec);
        var (state, engine) = NewMainPhase();
        var mother = new CardInstance(Card("B69-MOTHER2", "", CardType.Digitama, level: 2, name: "B69Mother"), 1);
        mother.AddBottomSource(Digimon("B69-S4", 3)); mother.AddBottomSource(Digimon("B69-S5", 3));
        state.Player.BreedingArea = mother;
        var cheap = Digimon("B69-CHEAP", 4, playCost: 2, traits: "B69DR");
        var dear = Digimon("B69-DEAR", 6, playCost: 7, traits: "B69DR");
        state.Player.Hand.Add(dear); state.Player.Hand.Add(cheap);
        engine.RunEffects(EffectTrigger.Security, state.Player, null, sec);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == cheap);
        Assert.Contains(dear, state.Player.Hand);
    }

    // =====================================================================
    // 5. BT19-084 앞면 시큐리티의 카드로 진화 (진화 코스트를 낸다)
    // =====================================================================

    [Fact]
    public void Bt19084_DigivolveIntoFaceUpSecurityCard_PaysCost_ThenPlacesFaceUp()
    {
        const string text = "[Main] By suspending this Tamer, digivolve 1 of your Digimon into a Digimon card in your face-up security cards. If this effect digivolved, you may place 1 Digimon card with the [B69RB] trait from your hand face-up as your bottom security card.";
        var tamer = Card(Uid("B69-WINR-"), text, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("자신의 디지몬 1마리를 자신의 앞면의 시큐리티의 디지몬 카드로 진화시킨다", Ko(text));
        var (state, engine) = NewMainPhase(memory: 6);
        state.Player.Tamers.Add(tamer);
        var body = Put(state.Player, Digimon("B69-L3", 3, color: CardColor.Green));
        var lv4 = Digimon("B69-L4", 4, color: CardColor.Green, evoCost: 3, traits: "B69RB");
        FaceUp(state.Player, lv4);
        var rb = Digimon("B69-RB3", 3, traits: "B69RB");
        state.Player.Hand.Add(rb);
        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);
        Assert.Equal(lv4, body.TopCard);
        Assert.Equal(6 - 3, state.MemoryGauge);
        Assert.DoesNotContain(lv4, state.Player.SecurityStack);
        Assert.DoesNotContain(rb, state.Player.Hand);
        Assert.True(GoldfishEngine.IsFaceUp(state.Player, state.Player.SecurityStack[^1]));
    }

    // =====================================================================
    // 6. BT22-090 · BT22-101 이 테이머를 패의 카드로 진화 코스트 -N (진화 조건은 만족해야 한다 — 공식 Q&A BT22-090 Q1)
    // =====================================================================

    private const string HybridOntoRed = "You may digivolve this card from your hand onto one of your red Tamers as if the Tamer is a level 3 red Digimon.";

    [Fact]
    public void Bt22101_TamerUnsuspends_DigivolvesWithReducedCost()
    {
        const string text = "[Your Turn] [Once Per Turn] When this Tamer unsuspends, if you don't have [B69Alpha], this Tamer may digivolve into [B69Alpha] in the hand with the digivolution cost reduced by 2.";
        var tamer = Card(Uid("B69-KYO-"), text, CardType.Tamer, color: CardColor.Red);
        AllSupported(tamer);
        Assert.Contains("이 테이머를 패의 「B69Alpha」", Ko(text));
        var alpha = Card(Uid("B69-ALPHA-"), HybridOntoRed, CardType.Digimon, level: 4, color: CardColor.Red, name: "B69Alpha", evoCost: 3);
        var (state, engine) = NewMainPhase(memory: 4);
        state.Player.Tamers.Add(tamer);
        state.Player.Hand.Add(alpha);
        int cost = Assert.IsType<int>(engine.TamerEvolutionCostFor(state.Player, alpha, tamer));
        engine.FireForSide(EffectTrigger.AllyTamerUnsuspended, state.Player, null, triggerCard: tamer);
        var d = Assert.Single(state.Player.BattleArea);
        Assert.Equal(alpha, d.TopCard);
        Assert.DoesNotContain(tamer, state.Player.Tamers);
        Assert.Equal(4 - Math.Max(0, cost - 2), state.MemoryGauge);
    }

    [Fact]
    public void Bt22090_RequirementNotMet_NoCostPaid()
    {
        const string text = "[End of Your Turn] [Once Per Turn] By deleting 1 of your other Digimon or Tamers with [B69Knight] in its text or the [B69CS] trait, this Tamer may digivolve into [B69Lord] in the hand with the digivolution cost reduced by 3.";
        var tamer = Card(Uid("B69-RIE-"), text, CardType.Tamer, color: CardColor.Blue);   // 블루 — 레드 테이머 진화 조건을 만족하지 않는다
        AllSupported(tamer);
        var lord = Digimon(Uid("B69-LORD-"), 4, HybridOntoRed, name: "B69Lord", color: CardColor.Red, evoCost: 3);
        var (state, engine) = NewMainPhase(memory: 1);
        state.Player.Tamers.Add(tamer);
        var victim = Put(state.Player, Digimon("B69-CSD", 3, traits: "B69CS"));
        state.Player.Hand.Add(lord);
        Assert.Null(engine.TamerEvolutionCostFor(state.Player, lord, tamer));
        engine.PassTurn();
        Assert.Contains(victim, state.Player.BattleArea);   // AI(추천)는 진화할 카드가 없으면 비용을 내지 않는다
        Assert.Contains(tamer, state.Player.Tamers);
    }

    // =====================================================================
    // 7. EX10-068 · BT11-094 · BT11-093 · BT22-093
    // =====================================================================

    [Fact]
    public void Ex10068_PlaysCardSharingAColorWithTheReturnedCard()
    {
        const string text = "[On Play] Delete 1 of your opponent's play cost 5 or lower Digimon. Then, by returning 1 Digimon card from your opponent's trash to the bottom of the deck, from your hand or trash and without paying the cost, you may play 1 level 4 or lower Digimon card with the same color as the card this effect returned.";
        var me = Digimon(Uid("B69-KAISER-"), 5, text);
        AllSupported(me);
        Assert.Contains("이 효과로 되돌린 카드와 같은 색의", Ko(text));
        var (state, engine) = NewMainPhase();
        var back = Digimon("B69-BACK", 5, color: CardColor.Green);
        back.SecondaryColor = CardColor.Yellow;
        state.DummyOpponent.Trash.Add(back);
        var red = Digimon("B69-REDH", 4, color: CardColor.Red);
        var yellow = Digimon("B69-YELT", 4, color: CardColor.Yellow);
        state.Player.Hand.Add(red); state.Player.Trash.Add(yellow);
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Contains(state.Player.BattleArea, x => x.TopCard == yellow);   // 2색 중 한 색 (공식 Q&A EX10-068 Q1)
        Assert.Contains(red, state.Player.Hand);
        Assert.Equal(back, state.DummyOpponent.MainDeck[^1]);
    }

    [Fact]
    public void Bt11094_PlaysTheOtherName()
    {
        const string text = "[Your Turn] When one of your Digimon digivolves into [B69Ange] or [B69Lady], if you have 1 or fewer Digimon in play, by suspending this Tamer, you may play 1 [B69Ange] or [B69Lady] with a different name than the Digimon you digivolved into from your hand without paying the cost.";
        var tamer = Card(Uid("B69-MIREI-"), text, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("진화한 디지몬과 명칭이 서로 다른 1장", Ko(text));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(tamer);
        var ange = Digimon("B69-ANGE", 5, name: "B69Ange");
        var d = Put(state.Player, ange, false, Digimon("B69-GATO", 4));
        var ange2 = Digimon("B69-ANGE2", 5, name: "B69Ange");
        var lady = Digimon("B69-LADY", 5, name: "B69Lady");
        state.Player.Hand.Add(ange2); state.Player.Hand.Add(lady);
        engine.FireForSide(EffectTrigger.AllyDigimonDigivolved, state.Player, d, triggerCard: ange);
        Assert.Contains(state.Player.BattleArea, x => x.TopCard == lady);
        Assert.Contains(ange2, state.Player.Hand);   // 같은 명칭은 고를 수 없다 (공식 Q&A BT11-094 Q1)
    }

    private const string Yuya = "[Your Turn] When one of your Digimon digivolves into a Digimon with [B69Grey] in its name, by suspending this Tamer, that Digimon gets +2000 DP until the end of your opponent's turn. If it digivolved into a Digimon with the same level, that Digimon isn't affected by your opponent's Option cards until the end of your opponent's turn.\"";

    [Theory]
    [InlineData(5, true)]
    [InlineData(4, false)]
    public void Bt11093_SameLevel_GrantsOptionImmunity(int sourceLevel, bool immune)
    {
        var tamer = Card(Uid("B69-YUYA-"), Yuya, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("같은 Lv.로 진화하고 있었다면", Ko(Yuya));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(tamer);
        var grey = Digimon("B69-GREY", 5, name: "B69Greymon", dp: 7000);
        var d = Put(state.Player, grey, false, Digimon("B69-PREV", sourceLevel));
        engine.FireForSide(EffectTrigger.AllyDigimonDigivolved, state.Player, d, triggerCard: grey);
        engine.RecalculateFieldBuffs();
        Assert.Equal(9000, d.GetCurrentDP());
        Assert.Equal(immune, CardScripts.HasKeyword(d, Keyword.UnaffectedByOpponentOptionEffects));
    }

    [Fact]
    public void Bt22093_SameLevelSource_FreeDigivolve()
    {
        const string text = "[Your Turn] When any of your Digimon digivolve into a Digimon with the [B69CS] trait, if it has a digivolution card with the same level as the digivolved Digimon, by suspending this Tamer, that Digimon may digivolve into a Digimon card with the [B69CS] trait in the hand without paying the cost.";
        var tamer = Card(Uid("B69-AMI-"), text, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("그 디지몬의 진화원에 진화한 디지몬과 Lv.이 같은 카드가 있다면", Ko(text));
        foreach (bool same in new[] { true, false })
        {
            var (state, engine) = NewMainPhase();
            state.Player.Tamers.Add(tamer);
            var cs5 = Digimon("B69-CS5", 5, traits: "B69CS");
            var d = Put(state.Player, cs5, false, Digimon("B69-P", same ? 5 : 4));
            var cs6 = Digimon("B69-CS6", 6, traits: "B69CS", evoCost: 3);
            state.Player.Hand.Add(cs6);
            engine.FireForSide(EffectTrigger.AllyDigimonDigivolved, state.Player, d, triggerCard: cs5);
            Assert.Equal(same ? cs6 : cs5, d.TopCard);
            Assert.Equal(8, state.MemoryGauge);
        }
    }

    // =====================================================================
    // 8. BT12-090 · BT22-102 그 디지몬을 패 · 트래시의 카드로 진화 · BT20-053 · BT11-089
    // =====================================================================

    [Fact]
    public void Bt22102_AttackingDigimonDigivolvesFromTrash_CostReduced()
    {
        const string text = "[Your Turn] When one of your Digimon with 2 or more same-level cards in its stack attacks, by suspending this Tamer, digivolve it into a Digimon card with the [B69NC] or [B69CS] trait in the trash with the digivolution cost reduced by 2.";
        var tamer = Card(Uid("B69-SAYO-"), text, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("지불하는 진화 코스트 -2 하여 진화시킨다", Ko(text));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(tamer);
        var atk = Put(state.Player, Digimon("B69-ATK5", 5), false, Digimon("B69-P5", 5));
        var into = Digimon("B69-NC6", 6, traits: "B69NC", evoCost: 3);
        state.Player.Trash.Add(into);
        Attack(engine, state, atk);
        Assert.Equal(into, atk.TopCard);
        Assert.Equal(8 - 1, state.MemoryGauge);
    }

    [Fact]
    public void Bt12090_AttackingDigimon_MayDigivolveFromHand_PayingCost()
    {
        const string text = "[Your Turn] When one of your 2-color blue and green Digimon attacks, by suspending this Tamer, digivolve that Digimon into a Digimon card with [B69Imp] in its name in your hand for its digivolution cost.";
        var tamer = Card(Uid("B69-SANHAE-"), text, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("디지몬 카드로 진화시킬 수 있다", Ko(text));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(tamer);
        var bg = Digimon("B69-BG5", 5, color: CardColor.Blue);
        bg.SecondaryColor = CardColor.Green;
        var atk = Put(state.Player, bg);
        var imp = Digimon("B69-IMP6", 6, name: "B69Imp", color: CardColor.Blue, evoCost: 3);
        state.Player.Hand.Add(imp);
        Attack(engine, state, atk);
        Assert.Equal(imp, atk.TopCard);
        Assert.Equal(8 - 3, state.MemoryGauge);
    }

    [Fact]
    public void Bt20053_OnlyDuringAttack()
    {
        const string text = "[On Play] [When Digivolving] You may play 1 [B69Doru] or [B69Ryuda] from your hand to your empty breeding area without paying the cost. Then, if during an attack until the end of your opponent's turn, 1 of your Digimon isn't affected by your opponent's Digimon's effects and gets +5000 DP.";
        var me = Digimon(Uid("B69-GRADE-"), 5, text, dp: 7000);
        AllSupported(me);
        Assert.Contains("그 후, 어택 중이라면, 상대의 턴 종료 시까지", Ko(text));
        Assert.Contains("비어 있는 자신의 육성 에어리어에", Ko(text));
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var doru = Digimon("B69-DORU", 3, name: "B69Doru");
        state.Player.Hand.Add(doru);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.Equal(doru, state.Player.BreedingArea?.TopCard);
        Assert.Equal(7000, d.GetCurrentDP());   // 어택 중이 아니다
    }

    [Fact]
    public void Bt11089_CommaTraitList_PlayedByEffect_GainsRush()
    {
        const string text = "[Your Turn] When you play a red Digimon with [B69Avian], [B69Bird], [B69Beast] or [B69Sov] in any of its traits (other than [B69Sea Animal] trait), by an effect, by suspending this Tamer, that Digimon gains ＜Rush＞ for the turn. (This Digimon can attack the turn it comes into play.)";
        var tamer = Card(Uid("B69-AKIHO-"), text, CardType.Tamer);
        AllSupported(tamer);
        Assert.Contains("효과로 등장했을 때", Ko(text));
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(tamer);
        var bird = Digimon("B69-BIRD", 4, color: CardColor.Red, traits: "Giant B69Bird");
        state.Player.Trash.Add(bird);
        var player = Digimon(Uid("B69-PLAYER-"), 5, "[On Play] You may play 1 level 4 or lower red Digimon card from your trash without paying the cost.");
        var p = Put(state.Player, player);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, p, player);
        engine.RecalculateFieldBuffs();
        var played = Assert.Single(state.Player.BattleArea, x => x.TopCard == bird);
        Assert.True(CardScripts.HasKeyword(played, Keyword.Rush), Logs(state));
    }
}
