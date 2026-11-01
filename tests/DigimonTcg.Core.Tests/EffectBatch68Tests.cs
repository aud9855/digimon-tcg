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
/// v0.68.0 미지원 문장 42차 묶음: 효과를 받지 않는 디지몬과 정보를 바꾸는 효과 (15-15-5-2) · AI 비용 추천 (EX13-031 · BT8-043) ·
/// 앞면 시큐리티 · 패의 시점 효과 (EX12-069 · EX12-074 · LM-068 · BT25-039) · BT19-053 · EX2-070 · BT14-078 · BT15-099 · BT17-102 · BT18-033 · BT20-077 · BT22-075
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch68Tests
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
            state.Player.MainDeck.Add(Digimon("B68-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B68-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B68-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B68-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private sealed class Answers : IChoiceProvider
    {
        public bool Yes = true;
        public int? Option;
        public List<string> Asked { get; } = new();
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Asked.Add(question); return Yes; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => Option ?? recommended;
    }

    // =====================================================================
    // 1. 효과를 받지 않는 디지몬과 바뀐 원래 명칭 · 색 · DP (종합 룰 15-15-5-1 · 15-15-5-2)
    // =====================================================================

    private const string ChangeOpp = "[On Play] Change 1 of your opponent's Digimon into a white Digimon with 3000 DP and an original name of [Sukamon] until the end of your opponent's turn.";
    private const string DuringAttacks = "[All Turns] During attacks, your opponent's effects don't affect this Digimon.";

    [Fact]
    public void OpponentNameChange_IgnoredWhileUnaffectedDuringAttack()
    {
        var me = Digimon(Uid("B68-ALPHA-"), 6, DuringAttacks, name: "B68Alpha", dp: 7000);
        var foeCard = Digimon(Uid("B68-KS-"), 5, ChangeOpp, dp: 6000);
        AllSupported(foeCard);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var foe = Put(state.DummyOpponent, foeCard);
        foe.IsOwnersTurn = false;
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, foeCard);   // 상대의 효과
        engine.RecalculateFieldBuffs();
        Assert.Equal("Sukamon", CardNames.MatchName(d));
        Assert.Equal(3000, d.GetCurrentDP());
        Assert.Equal(new[] { CardColor.White }, d.Colors);
        state.DummyOpponent.SecurityStack.Insert(0, Digimon("B68-SECD", 5, dp: 5000));
        Attack(engine, state, d);
        Assert.Contains(d, state.Player.BattleArea);   // 어택 중에는 원래 DP 7000 > 5000 (받지 않는 동안 영향이 없다)
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, d.GetCurrentDP());   // 어택이 끝나면 그 시점부터 다시 받는다 (15-15-5-2)
        Assert.Equal("Sukamon", CardNames.MatchName(d));
    }

    [Fact]
    public void OpponentNameChange_IgnoredWhileCategoryUnaffected_LM020()
    {
        var foeCard = Digimon(Uid("B68-KS-"), 5, ChangeOpp, dp: 6000);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B68-PLAIN", 6, name: "B68Plain", color: CardColor.Red, dp: 11000));
        var foe = Put(state.DummyOpponent, foeCard);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, foeCard);
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, d.GetCurrentDP());
        Assert.Equal((true, (CardType?)CardType.Digimon), d.ChangeSrc68);
        d.UnaffectedCategory50 = CardType.Digimon;   // LM-020 "디지몬 카드의 효과를 받지 않는다" (이 턴)
        d.UnaffectedCategory50Until = state.TurnSequence;
        engine.RecalculateFieldBuffs();
        Assert.Equal(11000, d.GetCurrentDP());
        Assert.Equal("B68Plain", CardNames.MatchName(d));
        Assert.Equal(new[] { CardColor.Red }, d.Colors);
        Assert.Contains("지금은 받지 않음", KoreanEffectRenderer.ChangedInfoBadge68(d));
        d.UnaffectedCategory50Until = 0;   // 받지 않는 상태가 끝났다
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, d.GetCurrentDP());
        Assert.DoesNotContain("지금은 받지 않음", KoreanEffectRenderer.ChangedInfoBadge68(d));
    }

    [Fact]
    public void OwnEffectChange_NotIgnoredByOpponentImmunity()
    {
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B68-SELF", 5, dp: 6000));
        d.Changed67 = ("Sukamon", 3000, state.TurnSequence + 2);
        d.ChangeSrc68 = (false, null);
        d.OppShield59 = true;
        Assert.Equal(3000, d.GetCurrentDP());
        Assert.Equal("Sukamon", CardNames.MatchName(d));
    }

    [Fact]
    public void RecolorFromOpponent_IgnoredWhileUnaffected_AndExpiresWhileShielded()
    {
        const string recolor = "[On Play] [When Digivolving] Until the end of your opponent's turn, change 1 of their Digimon or Tamers into a color other than white.";
        var foeCard = Digimon(Uid("B68-RC-"), 5, recolor);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B68-RED", 5, color: CardColor.Red, dp: 6000));
        var foe = Put(state.DummyOpponent, foeCard);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, foeCard);
        engine.RecalculateFieldBuffs();
        Assert.NotNull(d.BaseColor66);
        Assert.DoesNotContain(CardColor.Red, d.Colors);
        d.UnaffectedCategory50 = CardType.Digimon;
        d.UnaffectedCategory50Until = state.TurnSequence + 10;
        engine.RecalculateFieldBuffs();
        Assert.Null(d.BaseColor66);
        Assert.Equal(new[] { CardColor.Red }, d.Colors);
        state.TurnSequence += 3;   // 기간이 받지 않는 동안 끝났다 — 기록도 지운다
        engine.RecalculateFieldBuffs();
        d.UnaffectedCategory50Until = 0;
        engine.RecalculateFieldBuffs();
        Assert.Equal(new[] { CardColor.Red }, d.Colors);
        Assert.Null(KoreanEffectRenderer.ChangedInfoBadge68(d));
    }

    [Fact]
    public void ChangedInfoBadge_ShowsNameColorDp()
    {
        var (state, _) = NewMainPhase();
        var d = Put(state.Player, Digimon("B68-B", 5, dp: 6000));
        Assert.Null(KoreanEffectRenderer.ChangedInfoBadge68(d));
        d.Changed67 = ("Sukamon", 3000, 99);
        d.BaseColor66 = (CardColor.White, 99);
        var text = KoreanEffectRenderer.ChangedInfoBadge68(d);
        Assert.NotNull(text);
        Assert.Contains("3,000", text);
        Assert.Contains("화이트", text);
        Assert.StartsWith("✦", text);
    }

    // =====================================================================
    // 2. AI 비용 추천 — 강한 효과 (EX13-031 · BT8-043)
    // =====================================================================

    private const string Ex13031 = "[On Play] [When Digivolving] [On Deletion] By trashing 1 card with [Chuumon] or [Sukamon] in its name from your hand or your Digimon's digivolution cards, you may change the base name, color and DP of 1 of your opponent’s Digimon to [Sukamon], white and 3000 until their turn ends.";

    [Fact]
    public void RenameCost_PaidWithOnlyOneCardInHand()
    {
        var ks = Digimon(Uid("B68-KS2-"), 5, Ex13031, color: CardColor.Yellow);
        AllSupported(ks);
        var (state, engine) = NewMainPhase();
        state.Player.Hand.Add(Digimon("B68-SUKA", 3, name: "Sukamon"));
        var foe = Put(state.DummyOpponent, Digimon("B68-BIG", 6, dp: 12000));
        var me = Put(state.Player, ks);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, ks);
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, foe.GetCurrentDP());
        Assert.Empty(state.Player.Hand);
    }

    [Fact]
    public void RenameCost_NotPaidWithoutWorthwhileTarget()
    {
        var ks = Digimon(Uid("B68-KS3-"), 5, Ex13031, color: CardColor.Yellow);
        var (state, engine) = NewMainPhase();
        state.Player.Hand.Add(Digimon("B68-SUKA", 3, name: "Sukamon"));
        Put(state.DummyOpponent, Digimon("B68-SMALL", 3, dp: 2000));
        var me = Put(state.Player, ks);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, ks);
        Assert.Single(state.Player.Hand);   // DP 3000 이하만 있으면 내지 않는다
    }

    private const string Bt8043 = "When you would play this card from your hand, you may delete 1 of your purple [B68Cheru] to reduce this card's play cost by 8.";

    [Fact]
    public void DeleteCostReduction_WhenMemoryShort()
    {
        var card = Digimon(Uid("B68-CH-"), 6, Bt8043, name: "B68Cheru", color: CardColor.Yellow, playCost: 11, dp: 12000);
        var (state, engine) = NewMainPhase(memory: 3);
        Put(state.Player, Digimon("B68-PCH", 6, name: "B68Cheru", color: CardColor.Purple, dp: 12000));
        state.Player.Hand.Add(card);
        Assert.Equal(3, engine.PreviewPlayCostWithOptions(state.Player, card));
        Assert.True(engine.PlayCard(card).CanExecute);
        Assert.Single(state.Player.BattleArea);
        Assert.Equal(0, state.MemoryGauge);
    }

    [Fact]
    public void DeleteCostReduction_NotWhenMemoryEnough()
    {
        var card = Digimon(Uid("B68-CH-"), 6, Bt8043, name: "B68Cheru", color: CardColor.Yellow, playCost: 11, dp: 12000);
        var (state, engine) = NewMainPhase(memory: 10);
        state.MemoryGauge = 10;
        Put(state.Player, Digimon("B68-PCH", 6, name: "B68Cheru", color: CardColor.Purple, dp: 12000));
        state.Player.Hand.Add(card);
        Assert.Equal(3, engine.PreviewPlayCostWithOptions(state.Player, card));   // 10 < 11 — 줄이지 않으면 상대 쪽으로 넘어간다
        var (state2, engine2) = NewMainPhase(memory: 10);
        state2.MemoryGauge = 10;
        var big = Digimon(Uid("B68-CH-"), 6, Bt8043.Replace("by 8", "by 8"), name: "B68Cheru", color: CardColor.Yellow, playCost: 9, dp: 12000);
        Put(state2.Player, Digimon("B68-PCH", 6, name: "B68Cheru", color: CardColor.Purple, dp: 12000));
        state2.Player.Hand.Add(big);
        Assert.Equal(9, engine2.PreviewPlayCostWithOptions(state2.Player, big));   // 낼 수 있으면 디지몬을 남긴다
    }

    // =====================================================================
    // 3. 앞면 시큐리티 · 패의 시점 효과 (종합 룰 15-14-5-1)
    // =====================================================================

    private const string Ex12069 = "[Security] [Your Turn] When one of your level 4 or higher [VB] trait Digimon attacks, you may play 1 [VB] trait Digimon card of the same level from your hand with the cost reduced by 3.";
    private const string Ex12074 = "[Security] [Your Turn] [Once Per Turn] When one of your [Shambala] trait Digimon attacks, it may digivolve into a [Shambala] trait Digimon card in the hand with the cost reduced by 1.";

    [Fact]
    public void FaceUpSecurity_PlaysSameLevelFromHand()
    {
        var opt = Card(Uid("B68-VB-"), Ex12069, color: CardColor.Yellow, traits: "VB");
        AllSupported(opt);
        Assert.DoesNotContain("<null>", Ko(Ex12069));
        var (state, engine) = NewMainPhase(memory: 5);
        FaceUp(state.Player, opt);
        var atk = Put(state.Player, Digimon("B68-VB4", 4, traits: "VB"));
        var same = Digimon("B68-VB4B", 4, playCost: 4, traits: "VB");
        var other = Digimon("B68-VB5", 5, playCost: 4, traits: "VB");
        state.Player.Hand.Add(other); state.Player.Hand.Add(same);
        Attack(engine, state, atk);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == same);
        Assert.Contains(other, state.Player.Hand);
        Assert.Equal(4, state.MemoryGauge);   // 4 - 3 = 1
    }

    [Fact]
    public void FaceDownSecurity_DoesNotTrigger()
    {
        var opt = Card(Uid("B68-VB-"), Ex12069, color: CardColor.Yellow, traits: "VB");
        var (state, engine) = NewMainPhase(memory: 5);
        state.Player.SecurityStack.Add(opt);   // 뒷면
        var atk = Put(state.Player, Digimon("B68-VB4", 4, traits: "VB"));
        var same = Digimon("B68-VB4B", 4, playCost: 4, traits: "VB");
        state.Player.Hand.Add(same);
        Attack(engine, state, atk);
        Assert.Contains(same, state.Player.Hand);
    }

    [Fact]
    public void FaceUpSecurity_DigivolvesAttackerWithReducedCost()
    {
        var opt = Card(Uid("B68-SH-"), Ex12074, color: CardColor.White, traits: "Shambala");
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 5);
        FaceUp(state.Player, opt);
        var atk = Put(state.Player, Digimon("B68-SH4", 4, traits: "Shambala"));
        var evo = Digimon("B68-SH5", 5, evoCost: 3, traits: "Shambala");
        state.Player.Hand.Add(evo);
        Attack(engine, state, atk);
        Assert.Equal(evo, atk.TopCard);
        Assert.Equal(3, state.MemoryGauge);   // 5 - (3 - 1)
    }

    private const string Lm068Option = "When one of your [Evil|Dark Dragon|Evil Dragon] trait Digimon attacks, if your hand has 4 or fewer cards, you may use this card with the cost reduced by 3.\n[Main] Delete 1 of your opponent's Digimon with a level as high as the number of cards in your hand or higher.";

    [Fact]
    public void DualCardInHand_UsedOnAttack_LM068()
    {
        var dual = Digimon(Uid("B68-LM-"), 6, "", color: CardColor.Purple, playCost: 6, inherited: Lm068Option, traits: "Evil Dragon");
        dual.IsDual = true;
        AllSupported(dual);
        var (state, engine) = NewMainPhase(memory: 6);
        var atk = Put(state.Player, Digimon("B68-EVIL", 4, color: CardColor.Purple, traits: "Evil"));
        state.Player.Hand.Add(dual);
        var foe = Put(state.DummyOpponent, Digimon("B68-FOE", 5, dp: 7000));
        Attack(engine, state, atk);
        Assert.DoesNotContain(dual, state.Player.Hand);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
        Assert.Equal(3, state.MemoryGauge);   // 6 - (6 - 3)
    }

    [Fact]
    public void DualCardInHand_NotUsedWhenHandTooBig()
    {
        var dual = Digimon(Uid("B68-LM-"), 6, "", color: CardColor.Purple, playCost: 6, inherited: Lm068Option, traits: "Evil Dragon");
        dual.IsDual = true;
        var (state, engine) = NewMainPhase(memory: 6);
        var atk = Put(state.Player, Digimon("B68-EVIL", 4, color: CardColor.Purple, traits: "Evil"));
        state.Player.Hand.Add(dual);
        for (int i = 0; i < 4; i++) state.Player.Hand.Add(Digimon("B68-H" + i, 3));
        Put(state.DummyOpponent, Digimon("B68-FOE", 5, dp: 7000));
        Attack(engine, state, atk);
        Assert.Contains(dual, state.Player.Hand);
    }

    [Fact]
    public void FaceUpSecurity_EndOfYourTurn_PlaysAndPlacesUnder_BT25_039()
    {
        const string text = "[Security] [End of Your Turn] You may play 1 [B68Ceres] from your hand with the cost reduced by 7. If this effect played, you may place this card as the played Digimon's bottom digivolution card.";
        var sec = Digimon(Uid("B68-SIR-"), 4, text, color: CardColor.Green);
        AllSupported(sec);
        var (state, engine) = NewMainPhase(memory: 2);
        FaceUp(state.Player, sec);
        var ceres = Digimon("B68-CER", 6, name: "B68Ceres", playCost: 10, color: CardColor.Green);
        state.Player.Hand.Add(ceres);
        engine.PlayerChoices = new Answers();
        engine.PassTurn();
        var played = state.Player.BattleArea.Single(d => d.TopCard == ceres);
        Assert.Contains(sec, played.EvolutionSources);
        Assert.DoesNotContain(sec, state.Player.SecurityStack);
    }

    // =====================================================================
    // 4. 그 밖의 문장
    // =====================================================================

    [Fact]
    public void FaceUpSecurityPlay_WithReducedCost_BT19_053()
    {
        const string text = "[When Attacking] [Once Per Turn] You may play 1 [Royal Base] trait Digimon card from your face-up security cards with the play cost reduced by 8.";
        var queen = Digimon(Uid("B68-QB-"), 6, text, color: CardColor.Green, dp: 12000, traits: "Royal Base");
        AllSupported(queen);
        var (state, engine) = NewMainPhase(memory: 2);
        var bee = Digimon("B68-BEE", 5, playCost: 9, traits: "Royal Base");
        FaceUp(state.Player, bee);
        var me = Put(state.Player, queen);
        Attack(engine, state, me);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == bee);
        Assert.DoesNotContain(bee, state.Player.SecurityStack);
        Assert.Equal(1, state.MemoryGauge);
    }

    [Fact]
    public void DigivolveForCostAtMost3_EX2_070()
    {
        const string text = "[Main] ＜Draw 1＞ (Draw 1 card from your deck.) Then, you may digivolve 1 of your Digimon into a Digimon card in your hand that can digivolve for a digivolution cost of 3 or less without paying its digivolution cost.";
        var opt = Card(Uid("B68-PLUG-"), text, color: CardColor.Green);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 5);
        var d = Put(state.Player, Digimon("B68-G4", 4, color: CardColor.Green));
        var pricey = Digimon("B68-G5X", 5, color: CardColor.Green, evoCost: 4, dp: 9000);
        var cheap = Digimon("B68-G5", 5, color: CardColor.Green, evoCost: 3);
        state.Player.Hand.Add(pricey); state.Player.Hand.Add(cheap);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        Assert.Equal(cheap, d.TopCard);
        Assert.Equal(5, state.MemoryGauge);
        Assert.Contains(pricey, state.Player.Hand);
    }

    private const string Bt14078 = "[End of Your Turn] Delete this Digimon and ＜Draw 2＞ (Draw 2 cards from your deck.) Then, you may return 1 [Loogamon] from your trash to your hand. [On Deletion] You may trash up to 3 [Dark Animal] or [SoC] trait cards from your hand. Then, delete 1 of your opponent's level 3 or lower Digimon. For each card you trashed with this effect, increase this level by 1.";

    [Fact]
    public void TwoEffectsInOneClause_BT14_078()
    {
        var card = Digimon(Uid("B68-HL-"), 5, Bt14078, color: CardColor.Purple);
        AllSupported(card);
        var ko = Ko(Bt14078);
        Assert.Contains("【자신의 턴 종료 시】", ko);
        Assert.Contains("【소멸 시】", ko);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, card);
        for (int i = 0; i < 3; i++) state.Player.Hand.Add(Digimon("B68-DA" + i, 3, traits: "Dark Animal"));
        var lv5 = Put(state.DummyOpponent, Digimon("B68-L5", 5));
        var lv7 = Put(state.DummyOpponent, Digimon("B68-L7", 7));
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, me, card);
        Assert.DoesNotContain(lv5, state.DummyOpponent.BattleArea);   // 2장 파기 → Lv.5 이하
        Assert.Contains(lv7, state.DummyOpponent.BattleArea);
        Assert.Single(state.Player.Hand);
    }

    [Fact]
    public void TrashedTextDraw_BT15_099()
    {
        const string text = "[Main] By trashing 1 Digimon card in your hand, delete 1 of your opponent's Digimon whose level is less than or equal to the trashed card's level. When a card with [Myotismon] in its text is trashed by this effect, ＜Draw 2＞ (Draw 2 cards from your deck.)";
        var opt = Card(Uid("B68-NS-"), text, color: CardColor.Purple);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        state.Player.Hand.Add(Digimon("B68-MYO", 6, "[On Play] Play 1 [Myotismon] from your trash."));
        var foe = Put(state.DummyOpponent, Digimon("B68-L6", 6));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
        Assert.Equal(2, state.Player.Hand.Count);
        var (state2, engine2) = NewMainPhase();
        state2.Player.Hand.Add(Digimon("B68-PLAIN6", 6));
        Put(state2.DummyOpponent, Digimon("B68-L6", 6));
        engine2.RunEffects(EffectTrigger.Main, state2.Player, null, opt);
        Assert.Empty(state2.Player.Hand);
    }

    private const string Bt17102 = "[On Deletion] You may play 1 Tamer card with [Tai Kamiya] or [Kari Kamiya] in its name from your hand without paying the cost, or hatch in your breeding area.";

    [Fact]
    public void TamerOrHatch_BT17_102()
    {
        var card = Digimon(Uid("B68-GR-"), 5, Bt17102, color: CardColor.Red);
        AllSupported(card);
        var (state, engine) = NewMainPhase();
        var tai = Card("B68-TAI", type: CardType.Tamer, name: "Tai Kamiya", color: CardColor.Red);
        state.Player.Hand.Add(tai);
        state.Player.DigitamaDeck.Push(Card("B68-EGG", type: CardType.Digitama, level: 2));
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, me, card);
        Assert.Contains(tai, state.Player.Tamers);
        Assert.Null(state.Player.BreedingArea);
        var (state2, engine2) = NewMainPhase();
        state2.Player.DigitamaDeck.Push(Card("B68-EGG", type: CardType.Digitama, level: 2));
        var me2 = Put(state2.Player, card);
        engine2.RunEffects(EffectTrigger.OnDeletion, state2.Player, me2, card);
        Assert.NotNull(state2.Player.BreedingArea);
    }

    [Fact]
    public void HandMain_PlayToBreedingArea_BT18_033()
    {
        const string text = "[Hand] [Main] If your breeding area is empty, by returning 1 Digimon card with the [Three Great Angels] trait from your trash to the bottom of the deck, play this card in your breeding area without paying the cost.";
        var card = Digimon(Uid("B68-PT-"), 3, text, color: CardColor.Yellow);
        AllSupported(card);
        var (state, engine) = NewMainPhase();
        state.Player.Hand.Add(card);
        var angel = Digimon("B68-ANG", 6, traits: "Three Great Angels");
        state.Player.Trash.Add(angel);
        Assert.True(engine.ActivateHandEffect(card).CanExecute);
        Assert.Equal(card, state.Player.BreedingArea?.TopCard);
        Assert.Equal(angel, state.Player.MainDeck[^1]);
    }

    [Fact]
    public void TrashHandToFour_DpCap_BT20_077()
    {
        const string text = "[On Play] [When Digivolving] Trash cards in your hand until it has 4 left. Then, play 1 8000 DP or lower Digimon card from your trash without paying the cost. For each card this effect trashed, remove 2000 from this effect's DP maximum.";
        var card = Digimon(Uid("B68-HM-"), 6, text, color: CardColor.Purple);
        AllSupported(card);
        var (state, engine) = NewMainPhase();
        for (int i = 0; i < 6; i++) state.Player.Hand.Add(Digimon("B68-HH" + i, 3));
        var d6000 = Digimon("B68-D6", 5, dp: 6000);
        var d4000 = Digimon("B68-D4", 4, dp: 4000);
        state.Player.Trash.Add(d6000); state.Player.Trash.Add(d4000);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(4, state.Player.Hand.Count);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == d4000);
        Assert.Contains(d6000, state.Player.Trash);
    }

    [Fact]
    public void WouldLeave_PlaysLinkCard_BT22_075()
    {
        const string text = "[All Turns] [Once Per Turn] When this Digimon would leave the battle area, you may play 1 of this Digimon's link cards without paying the cost.";
        var card = Digimon(Uid("B68-FK-"), 5, text, color: CardColor.Blue);
        AllSupported(card);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, card);
        var link = Digimon("B68-LINK", 3, dp: 3000);
        me.LinkedCards.Add(link);
        var foeCard = Digimon(Uid("B68-DEL-"), 5, "[On Play] Delete 1 of your opponent's Digimon.");
        var foe = Put(state.DummyOpponent, foeCard);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, foeCard);
        Assert.DoesNotContain(me, state.Player.BattleArea);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == link);
    }

    [Fact]
    public void NewSentences_HaveKorean()
    {
        foreach (var s in new[]
        {
            Ex12069, Ex12074, Bt14078, Bt17102,
            "When one of your [Evil|Dark Dragon|Evil Dragon] trait Digimon attacks, if your hand has 4 or fewer cards, you may use this card with the cost reduced by 3.",
            "[When Attacking] [Once Per Turn] You may play 1 [Royal Base] trait Digimon card from your face-up security cards with the play cost reduced by 8.",
            "[All Turns] [Once Per Turn] When this Digimon would leave the battle area, you may play 1 of this Digimon's link cards without paying the cost.",
        })
            Assert.NotEqual("<null>", Ko(s));
        Assert.Contains("패의 이 카드를 지불하는 사용 코스트 -3 하여 사용할 수 있다", Ko("When one of your [Evil|Dark Dragon|Evil Dragon] trait Digimon attacks, if your hand has 4 or fewer cards, you may use this card with the cost reduced by 3."));
    }
}
