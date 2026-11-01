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
/// v0.67.0 미지원 문장 41차 묶음: 테이머 기록은 카드(객체)마다 · BT8-040 이 턴 동안의 색 · 원래 명칭 「스카몬」 변경 (BT11-043 · BT14-097 · EX13-031) ·
/// BT12-086 색이 서로 다른 카드 · EX4-072 · BT5-089 · P-070 · ST9-08 · BT20-096 · BT8-043 · BT9-079 · BT7-039 · LM-068
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch67Tests
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

    private static CardData Tamer(string id, string main = "", string? name = null, CardColor color = CardColor.Blue) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name);

    /// <summary>같은 카드 번호의 다른 카드 (게임을 시작할 때 CardIdentity55가 만드는 복사본과 같다)</summary>
    private static CardData Copy(CardData c) =>
        Card(c.CardId, c.EffectText, c.Type, c.Level, c.PlayCost, c.Color, c.DP, c.Name, c.EvoCost, c.InheritedEffectText, c.Traits.ToArray());

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int security = 5)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B67-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B67-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B67-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B67-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
        public int? Option;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => Yes;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => Option ?? recommended;
    }

    // =====================================================================
    // 0. 테이머 기록은 카드(객체)마다 — 같은 카드 2장은 따로 (종합 룰 3-4-4 · 15-14-1-3)
    // =====================================================================

    private const string Bt18078 = "[On Play] [When Digivolving] Until the end of your opponent's turn, change 1 of their Digimon or Tamers into a color other than white.";

    [Fact]
    public void TwoCopiesOfSameTamer_RecolorOnlyOne()
    {
        var (state, engine) = NewMainPhase();
        var t1 = Tamer(Uid("B67-OTAM-"), color: CardColor.Red);
        var t2 = Copy(t1);
        state.DummyOpponent.Tamers.Add(t1);
        state.DummyOpponent.Tamers.Add(t2);
        var dusk = Digimon(Uid("B67-DUSK-"), 4, Bt18078, color: CardColor.Purple);
        engine.PlayerChoices = new Answers { Option = 3 };   // 0 = 첫 테이머 → 색 3 = 그린
        var me = Put(state.Player, dusk);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, dusk, includeInherited: false);
        Assert.Equal(new[] { CardColor.Green }, state.DummyOpponent.TamerColorsOf66(t1));
        Assert.Equal(new[] { CardColor.Red }, state.DummyOpponent.TamerColorsOf66(t2));   // 같은 카드 번호의 다른 장은 그대로
    }

    [Fact]
    public void TamerReplayed_IsNewTamer_LosesRecolor()
    {
        var (state, engine) = NewMainPhase();
        var t = Tamer(Uid("B67-OTAM-"), color: CardColor.Red);
        engine.AddTamer(state.DummyOpponent, t);
        state.DummyOpponent.TamerColors66.Add((t, CardColor.Green, state.TurnSequence + 1));
        Assert.Equal(new[] { CardColor.Green }, state.DummyOpponent.TamerColorsOf66(t));
        state.DummyOpponent.Tamers.Remove(t);   // 패로 되돌아갔다가 (지속 효과를 다시 계산하기 전에)
        engine.AddTamer(state.DummyOpponent, t);   // 다시 등장
        Assert.Equal(new[] { CardColor.Red }, state.DummyOpponent.TamerColorsOf66(t));
    }

    // =====================================================================
    // 1. BT8-040 이 턴 동안 파기한 카드의 색으로도
    // =====================================================================

    private const string Bt8040 = "[When Digivolving] You may trash 1 card in your hand to treat this Digimon as also having the colors of the trashed card for the turn. Then, if this Digimon has 2 or more colors, ＜Draw 2＞ (Draw 2 cards from your deck.)";

    [Fact]
    public void BT8_040_TrashRedCard_TwoColors_Draw2_UntilTurnEnds()
    {
        var bets = Digimon(Uid("B67-BETS-"), 5, Bt8040, name: "Betsumon", color: CardColor.Yellow);
        AllSupported(bets);
        Assert.Equal("【진화 시】 자신의 패 1장을 파기하는 것으로, 이 턴 동안 이 디지몬의 색은 파기한 카드의 색으로도 취급한다. 그 후, 이 디지몬이 2색 이상일 때, 《2 드로우》 (자신의 덱에서 카드를 2장 드로우한다).", Ko(Bt8040));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var red = Digimon("B67-RED", 3, color: CardColor.Red);
        var yel = Digimon("B67-YEL", 3, color: CardColor.Yellow);
        state.Player.Hand.Add(yel);
        state.Player.Hand.Add(red);
        var me = Put(state.Player, bets);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, bets, includeInherited: false);
        Assert.Contains(red, state.Player.Trash);   // 추천: 이 디지몬에 없는 색의 카드
        Assert.Equal(new[] { CardColor.Yellow, CardColor.Red }, me.Colors);   // 공식 Q&A BT8-040 Q1
        Assert.Equal(3, state.Player.Hand.Count);   // 1장 남음 + 2 드로우
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.Equal(new[] { CardColor.Yellow }, me.Colors);   // 이 턴 동안만
    }

    [Fact]
    public void BT8_040_OnlySameColorInHand_AiDoesNotTrash_NoDraw()
    {
        var bets = Digimon(Uid("B67-BETS-"), 5, Bt8040, name: "Betsumon", color: CardColor.Yellow);
        var (state, engine) = NewMainPhase();
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        var yel = Digimon("B67-YEL", 3, color: CardColor.Yellow);
        state.DummyOpponent.Hand.Add(yel);
        var me = Put(state.DummyOpponent, bets);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.DummyOpponent, me, bets, includeInherited: false);
        Assert.Contains(yel, state.DummyOpponent.Hand);
        Assert.Single(state.DummyOpponent.Hand);
    }

    // =====================================================================
    // 2. 원래 명칭 「스카몬」 · 화이트 · DP 3000 (종합 룰 15-12-2)
    // =====================================================================

    private const string Bt11043 = "[On Play] [When Digivolving] If your opponent has 16 or more cards in their trash, or you have 3 or more cards with [Sukamon] in their names in your trash, change 1 of your opponent's Digimon into a white Digimon with 3000 DP and an original name of [Sukamon] until the end of your opponent's turn.";

    [Fact]
    public void BT11_043_ChangesNameColorDp_KeepsTraitsAndBuffs_UntilOpponentsTurnEnds()
    {
        var king = Digimon(Uid("B67-KSUKA-"), 5, Bt11043 + "\n[When Attacking] For each other Digimon with [Sukamon] in its name in play, this Digimon gains ＜Security A. +1＞ for the turn.", name: "KingSukamon", color: CardColor.Yellow);
        AllSupported(king);
        Assert.Equal("【등장 시】 【진화 시】 상대의 트래시가 16장 이상이라면 또는 자신의 트래시에 명칭에 「스카몬」을 포함하는 카드가 3장 이상 있다면, 상대의 턴 종료까지 상대의 디지몬 1마리를 원래 명칭 「스카몬」・화이트・DP 3000으로 변경한다.", Ko(Bt11043));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        for (int i = 0; i < 16; i++) state.DummyOpponent.Trash.Add(Digimon("B67-OTR" + i, 3));
        var foe = Put(state.DummyOpponent, Digimon("B67-WARG", 6, name: "WarGreymon", color: CardColor.Red, dp: 12000, traits: "Dragon Warrior"));
        foe.TimedDpBuffs.Add((2000, state.TurnSequence + 1));
        var small = Put(state.DummyOpponent, Digimon("B67-AGU", 3, name: "Agumon", color: CardColor.Red));
        var me = Put(state.Player, king);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, king, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.NotNull(foe.Changed67);   // 추천: DP가 가장 높은 상대의 디지몬
        Assert.Null(small.Changed67);
        Assert.True(CardNames.Is(foe, "Sukamon"));
        Assert.False(TargetSpec.NameHas(foe, "Greymon"));   // 원래 명칭이 바뀌었다
        Assert.True(TargetSpec.HasTrait(foe, "Dragon Warrior"));   // 특징은 그대로
        Assert.Equal(new[] { CardColor.White }, foe.Colors);
        Assert.Equal(5000, foe.GetCurrentDP());   // 3000 + 기간 DP +2000
        Assert.Equal(6, foe.TopCard.Level);
        // [어택 시] "명칭에 「스카몬」을 포함하는 다른 디지몬 1마리마다" — 상대의 바뀐 디지몬도 센다 (공식 Q&A BT11-043 Q1과 같은 취급)
        int before = me.GetSecurityAttackCount();
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, king, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.Equal(before + 1, me.GetSecurityAttackCount());
        // 상대의 턴 종료까지
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.True(CardNames.Is(foe, "Sukamon"));
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.Null(foe.Changed67);
        Assert.True(TargetSpec.NameHas(foe, "Greymon"));
        Assert.Equal(new[] { CardColor.Red }, foe.Colors);
        Assert.Equal(12000, foe.GetCurrentDP());
    }

    [Fact]
    public void BT11_043_ConditionNotMet_NoChange()
    {
        var king = Digimon(Uid("B67-KSUKA-"), 5, Bt11043, name: "KingSukamon", color: CardColor.Yellow);
        var (state, engine) = NewMainPhase();
        var foe = Put(state.DummyOpponent, Digimon("B67-WARG", 6, name: "WarGreymon", color: CardColor.Red));
        var me = Put(state.Player, king);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, king, includeInherited: false);
        Assert.Null(foe.Changed67);
    }

    [Fact]
    public void BT14_097_Security_UntilEndOfOwnersTurn()
    {
        const string sec = "[Security] Until the end of your turn, change 1 of your opponent's Digimon into being white and having 3000 DP and an original name of [Sukamon]";
        var opt = Card(Uid("B67-CURSE-"), "[Main] Gain 1 memory.", CardType.Option, color: CardColor.Yellow, inherited: "Security Effect " + sec);
        AllSupported(opt);
        Assert.Equal("【시큐리티】 자신의 턴 종료까지 상대의 디지몬 1마리를 원래 명칭 「스카몬」・화이트・DP 3000으로 변경한다.", Ko(sec));
        var (state, engine) = NewMainPhase();
        state.DummyOpponent.SecurityStack.Insert(0, opt);
        var atk = Put(state.Player, Digimon("B67-ATK", 6, name: "MetalGarurumon", dp: 11000));
        atk.TurnPlayed = 0;
        Assert.True(engine.AttackSecurityOrDirect(atk).CanExecute, Logs(state));
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.NotNull(atk.Changed67);
        Assert.True(CardNames.Is(atk, "Sukamon"));
        Assert.Equal(3000, atk.GetCurrentDP());
        // 상대(시큐리티 주인)의 턴 종료까지 — 내 턴이 끝나도 남는다
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.NotNull(atk.Changed67);
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.Null(atk.Changed67);
    }

    [Fact]
    public void EX13_031_TrashSukamonFromHand_ChangesOpponentDigimon()
    {
        const string text = "[On Play] [When Digivolving] [On Deletion] By trashing 1 card with [Chuumon] or [Sukamon] in its name from your hand or your Digimon's digivolution cards, you may change the base name, color and DP of 1 of your opponent’s Digimon to [Sukamon], white and 3000 until their turn ends.";
        var king = Digimon(Uid("B67-KSUKA2-"), 5, text, name: "KingSukamon", color: CardColor.Yellow);
        AllSupported(king);
        Assert.Contains("원래 명칭 「스카몬」・화이트・DP 3000으로 변경할 수 있다.", Ko(text));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var suka = Digimon("B67-SUKA", 3, name: "Sukamon", color: CardColor.Yellow);
        state.Player.Hand.Add(suka);
        var foe = Put(state.DummyOpponent, Digimon("B67-FOE", 5, name: "Garudamon", color: CardColor.Red, dp: 7000));
        var me = Put(state.Player, king);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, king, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.Contains(suka, state.Player.Trash);
        Assert.True(CardNames.Is(foe, "Sukamon"));
        Assert.Equal(3000, foe.GetCurrentDP());
    }

    // =====================================================================
    // 3. 그 밖의 카드
    // =====================================================================

    [Fact]
    public void BT12_086_AddUpTo2DifferentColors()
    {
        const string text = "[On Play] Reveal the top 3 cards of your deck. Add up to 2 Digimon cards with different colors and ＜Save＞ in their texts among them to your hand. Place the rest at the bottom of your deck in any order.";
        var clock = Digimon(Uid("B67-CLOCK-"), 4, text, name: "Clockmon");
        AllSupported(clock);
        Assert.Equal("【등장 시】 자신의 덱 위에서부터 3장 오픈한다. 그중 색이 서로 다른 《세이브》가 기술되어 있는 디지몬 카드 2장까지를 패에 추가한다. 나머지는 원하는 순서대로 덱 아래로 되돌린다.", Ko(text));
        var (state, engine) = NewMainPhase();
        const string save = "[On Deletion] ＜Save＞ (You may place this card under one of your Tamers.)";
        var r1 = Digimon("B67-R1", 4, save, color: CardColor.Red);
        var r2 = Digimon("B67-R2", 5, save, color: CardColor.Red);
        var b1 = Digimon("B67-B1", 3, save, color: CardColor.Blue);
        state.Player.MainDeck.InsertRange(0, new[] { r1, r2, b1 });
        var me = Put(state.Player, clock);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, clock, includeInherited: false);
        Assert.Contains(r2, state.Player.Hand);   // Lv.가 높은 레드
        Assert.Contains(b1, state.Player.Hand);   // 다른 색
        Assert.DoesNotContain(r1, state.Player.Hand);   // 같은 레드는 못 고른다
        Assert.Same(r1, state.Player.MainDeck[^1]);
    }

    [Fact]
    public void EX4_072_DigivolveIntoDifferentNameIncludingChosenName()
    {
        const string text = "[Main] Choose 1 of your level 6 Digimon. It may digivolve into a level 6 Digimon card in your hand with a different name that includes the name of the chosen Digimon, ignoring digivolution requirements and without paying the cost.";
        var opt = Card(Uid("B67-TRANS-"), text, CardType.Option, color: CardColor.Yellow);
        AllSupported(opt);
        var (state, engine) = NewMainPhase(memory: 3);
        engine.PlayerChoices = new Answers();
        var war = Put(state.Player, Digimon("B67-WAR", 6, name: "WarGreymon", color: CardColor.Red));
        var same = Digimon("B67-WAR2", 6, name: "WarGreymon", color: CardColor.Red, dp: 13000);
        var lv7 = Digimon("B67-WARX7", 7, name: "WarGreymon X", color: CardColor.Red, dp: 15000);
        var black = Digimon("B67-BWAR", 6, name: "BlackWarGreymon", color: CardColor.Black, dp: 12000);
        state.Player.Hand.AddRange(new[] { same, lv7, black });
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Same(black, war.TopCard);   // 공식 Q&A EX4-072 Q1 — 명칭의 일부에 「워그레이몬」, 명칭이 다른 Lv.6 (색 조건 무시)
        Assert.Equal(3, state.MemoryGauge);   // 코스트 없이
        Assert.Contains(same, state.Player.Hand);
    }

    [Fact]
    public void BT5_089_AttackingLevel5Green_DigivolvesIntoRevealedGreen6()
    {
        const string text = "[Your Turn] When you attack with a level 5 green Digimon, you may suspend this Tamer to reveal 3 cards from the top of your deck. You may digivolve 1 green level 6 Digimon card among them onto the attacking Digimon without paying its memory cost. Place the remaining cards at the bottom of your deck in any order.";
        var tamer = Tamer(Uid("B67-IZZY-"), text, color: CardColor.Green);
        AllSupported(tamer);
        Assert.Contains("어택한 디지몬을 코스트를 지불하지 않고 진화시킬 수 있다.", Ko(text));
        var (state, engine) = NewMainPhase(memory: 4);
        engine.PlayerChoices = new Answers();
        state.Player.Tamers.Add(tamer);
        var six = Digimon("B67-G6", 6, color: CardColor.Green, evoCost: 3, dp: 11000);
        state.Player.MainDeck.Insert(1, six);
        var atk = Put(state.Player, Digimon("B67-G5", 5, color: CardColor.Green, dp: 7000));
        atk.TurnPlayed = 0;
        Assert.True(engine.AttackSecurityOrDirect(atk).CanExecute, Logs(state));
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.Same(six, atk.TopCard);
        Assert.Contains(tamer, state.Player.SuspendedTamers);
        Assert.True(state.MemoryGauge >= 4);   // 코스트 없이 (진화 보너스 드로우만)
    }

    [Fact]
    public void P_070_EndOfBattle_PlayRevealedBlackCost4_ThenAddThisToHand()
    {
        const string text = "[Security] At the end of the battle, reveal the top card of your deck. If that card is a black Digimon card with a play cost of 4 or less, you may play it without paying its memory cost. Add any remaining cards to your hand. Then, add this card to its owner's hand.";
        var doru = Digimon(Uid("B67-DORU-"), 3, text, name: "Dorumon", color: CardColor.Purple, dp: 3000);
        AllSupported(doru);
        Assert.Equal("【시큐리티】 배틀 종료 시, 자신의 덱 위에서부터 1장 오픈한다. 그 카드가 블랙인 등장 코스트 4 이하의 디지몬 카드라면 코스트를 지불하지 않고 등장시킬 수 있다. 등장시키지 않은 카드는 패에 추가한다. 그 후, 이 카드를 패에 추가한다.", Ko(text));
        var (state, engine) = NewMainPhase();
        var black = Digimon("B67-BLK", 4, color: CardColor.Black, playCost: 4);
        state.DummyOpponent.MainDeck.Insert(0, black);
        state.DummyOpponent.SecurityStack.Insert(0, doru);
        var atk = Put(state.Player, Digimon("B67-ATK", 5, dp: 7000));
        atk.TurnPlayed = 0;
        Assert.True(engine.AttackSecurityOrDirect(atk).CanExecute, Logs(state));
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.Contains(state.DummyOpponent.BattleArea, d => ReferenceEquals(d.TopCard, black));
        Assert.Contains(doru, state.DummyOpponent.Hand);
    }

    [Fact]
    public void P_070_RevealedNotMatching_GoesToHand()
    {
        const string text = "[Security] At the end of the battle, reveal the top card of your deck. If that card is a black Digimon card with a play cost of 4 or less, you may play it without paying its memory cost. Add any remaining cards to your hand. Then, add this card to its owner's hand.";
        var doru = Digimon(Uid("B67-DORU-"), 3, text, name: "Dorumon", color: CardColor.Purple, dp: 3000);
        var (state, engine) = NewMainPhase();
        var red = Digimon("B67-REDX", 4, color: CardColor.Red, playCost: 4);
        state.DummyOpponent.MainDeck.Insert(0, red);
        state.DummyOpponent.SecurityStack.Insert(0, doru);
        var atk = Put(state.Player, Digimon("B67-ATK", 5, dp: 7000));
        atk.TurnPlayed = 0;
        Assert.True(engine.AttackSecurityOrDirect(atk).CanExecute, Logs(state));
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.Contains(red, state.DummyOpponent.Hand);
        Assert.Contains(doru, state.DummyOpponent.Hand);
    }

    [Fact]
    public void BT20_096_TrashMain_PayCost_ReturnThisToBottom_DeleteUnsuspended()
    {
        const string text = "[Trash] [Main] If you have 4 or fewer cards in your hand, by paying 6 cost, return this card to the bottom of the deck and delete 1 of your opponent's unsuspended Digimon.";
        var card = Digimon(Uid("B67-TRASH-"), 5, text, color: CardColor.Purple);
        AllSupported(card);
        Assert.Equal("[트래시] 【메인】 자신의 패가 4장 이하일 때, 6 코스트 지불하는 것으로, 이 카드를 덱 아래로 되돌리고, 액티브 상태인 상대의 디지몬 1마리를 소멸시킨다.", Ko(text));
        var (state, engine) = NewMainPhase(memory: 8);
        engine.PlayerChoices = new Answers();
        var foe = Put(state.DummyOpponent, Digimon("B67-FOE", 5));
        var rested = Put(state.DummyOpponent, Digimon("B67-FOE2", 6), suspended: true);
        state.Player.Trash.Add(card);
        Assert.True(engine.ActivateTrashEffect(card).CanExecute, Logs(state));
        Assert.Same(card, state.Player.MainDeck[^1]);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
        Assert.Contains(rested, state.DummyOpponent.BattleArea);
        Assert.Equal(2, state.MemoryGauge);
    }

    [Fact]
    public void BT8_043_DeletePurpleCherubimon_ReducesPlayCostBy8()
    {
        const string text = "When you would play this card from your hand, you may delete 1 of your purple [Cherubimon] to reduce this card's play cost by 8.";
        var card = Digimon(Uid("B67-CHERU-"), 6, text, name: "Cherubimon", color: CardColor.Purple, playCost: 12);
        AllSupported(card);
        Assert.Equal("패의 이 카드를 등장시킬 때, 퍼플인 자신의 「케루비몬」 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -8 한다.", Ko(text));
        var (state, engine) = NewMainPhase(memory: 8);
        engine.PlayerChoices = new Answers();
        var old = Put(state.Player, Digimon("B67-OLDCH", 6, name: "Cherubimon", color: CardColor.Purple));
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute, Logs(state));
        Assert.DoesNotContain(old, state.Player.BattleArea);
        Assert.Equal(8 - 4, state.MemoryGauge);
    }

    [Theory]
    [InlineData("[End of Attack] [Once Per Turn] You may digivolve 1 of your other Digimon in play into a Digimon card with the [Undead] or [Dark Animal] trait from your trash without paying its memory cost.", "【어택 종료 시】 [턴에 1회] 다른 자신의 디지몬 1마리를 자신의 트래시의 특징으로 「언데드형」/「마수형」을 가진 디지몬 카드 1장으로 코스트를 지불하지 않고 진화시킬 수 있다.")]
    [InlineData("[When Digivolving] If this Digimon has 1 digivolution card, you may place up to 2 level 4 or lower yellow Digimon cards from your hand at the bottom of this Digimon's digivolution cards in any order. Then, ＜Draw 1＞ (Draw 1 card from your deck.) for each Digimon card you placed.", "【진화 시】 이 디지몬의 진화원이 1장일 때, 자신의 패에서 Lv.4 이하인 옐로의 디지몬 카드 2장까지를 이 디지몬의 진화원 아래에 놓을 수 있다. 그 후, 이 효과로 놓은 1장마다 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다).")]
    [InlineData("[When Digivolving] [End of Attack] If your hand has 4 or fewer cards, you may play 1 level 4 or lower Digimon card with the [Evil], [Dark Dragon] or [Evil Dragon] trait from your trash to the field without paying the cost.", "【진화 시】 【어택 종료 시】 자신의 패가 4장 이하일 때, 자신의 트래시에서 Lv.4 이하인 특징으로 「소악마형」/「마룡형」/「사룡형」을 가진 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다.")]
    public void OtherSentences_Supported_WithKorean(string text, string ko)
    {
        AllSupported(Digimon(Uid("B67-OTH-"), 5, text));
        Assert.Equal(ko, Ko(text));
    }

    [Fact]
    public void ST9_08_InheritedDnaDigivolve_Supported()
    {
        const string inh = "[End of Your Turn] You may DNA digivolve this Digimon and one of your other Digimon in play into a Digimon card in your hand by paying its DNA digivolve cost.";
        var worm = Digimon(Uid("B67-WORM-"), 3, "", name: "Wormmon", color: CardColor.Green, inherited: inh);
        AllSupported(worm);
        Assert.Equal("【자신의 턴 종료 시】 이 디지몬과 다른 자신의 디지몬으로, 패의 디지몬 카드로 조그레스 진화할 수 있다.", Ko(inh));
    }

    [Fact]
    public void BT7_039_DrawsPerPlacedCard()
    {
        const string text = "[When Digivolving] If this Digimon has 1 digivolution card, you may place up to 2 level 4 or lower yellow Digimon cards from your hand at the bottom of this Digimon's digivolution cards in any order. Then, ＜Draw 1＞ (Draw 1 card from your deck.) for each Digimon card you placed.";
        var card = Digimon(Uid("B67-ANGE-"), 5, text, color: CardColor.Yellow);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var y1 = Digimon("B67-Y1", 3, color: CardColor.Yellow);
        var y2 = Digimon("B67-Y2", 4, color: CardColor.Yellow);
        state.Player.Hand.AddRange(new[] { y1, y2 });
        var me = Put(state.Player, card, false, Digimon("B67-BASE", 4, color: CardColor.Yellow));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card, includeInherited: false);
        Assert.Equal(3, me.EvolutionSources.Count);
        Assert.Equal(2, state.Player.Hand.Count);   // 2장 놓고 2장 드로우
    }

    [Fact]
    public void NameChange_SeenByNameConditions()
    {
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Digimon("B67-GREY", 5, name: "MetalGreymon", color: CardColor.Red));
        var cond = Digimon(Uid("B67-COND-"), 4, "[On Play] If you have a Digimon with [Greymon] in its name, gain 2 memory.");
        var ci = Put(state.Player, cond);
        mine.Changed67 = ("Sukamon", 3000, state.TurnSequence + 1);
        engine.RecalculateFieldBuffs();
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, ci, cond, includeInherited: false);
        Assert.Equal(mem, state.MemoryGauge);   // 「그레이몬」을 포함하지 않는다
        mine.Changed67 = null;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, ci, cond, includeInherited: false);
        Assert.Equal(mem + 2, state.MemoryGauge);
    }
}
