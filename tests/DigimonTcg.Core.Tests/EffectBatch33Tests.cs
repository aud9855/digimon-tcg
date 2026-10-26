using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.33.0 미지원 문장 7차 묶음: "~를 놓는 것으로" 비용 (다른 디지몬 · 이 디지몬 · 테이머를 진화원으로, 테이머 아래, 시큐리티 · 덱) ·
/// 진화 조건 무시 진화 · 하이브리드체 [패] [메인] 테이머 진화 · 테이머가 떠나려 할 때 · 사건 (패로 되돌아감 · 배틀 에어리어를 떠남 · 효과로 소멸 · 시큐리티 체크 등) ·
/// 조건 ("If this ~" · "If you ~" · "While ~") · 생성 한글
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch33Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", int evoCost = 1, CardColor? second = null, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, SecondaryColor = second, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", CardColor color = CardColor.Blue, int dp = -1, string? name = null,
        int playCost = 3, int evoCost = 1, CardColor? second = null, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, inherited, evoCost, second, traits);

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, params string[] traits) =>
        Card(id, main, CardType.Tamer, color: color, name: name, traits: traits);

    private static CardData Option(string id, string main, int cost = 0) => Card(id, main, CardType.Option, playCost: cost);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B33-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B33-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B33-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B33-OSEC" + i, 3));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, int turnPlayed = 1, params CardData[] sources)
    {
        var d = new CardInstance(card, turnPlayed) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static void Use(GameState state, GoldfishEngine engine, CardData option)
    {
        AssertFull(option);
        if (!state.Player.BattleArea.Any(d => d.TopCard.AllColors.Contains(CardColor.Blue)) && !state.Player.Tamers.Any(t => t.AllColors.Contains(CardColor.Blue)))
            state.Player.Tamers.Add(Tamer("B33-COLOR"));
        state.Player.Hand.Add(option);
        var result = engine.PlayCard(option);
        Assert.True(result.CanExecute, result.Reason);
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    // =====================================================================
    // 비용: 디지몬 · 테이머를 진화원으로
    // =====================================================================

    [Fact]
    public void Cost_PlaceOtherDigimonUnderThis_TopCardOnly_SourcesTrashed_BT15_029()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-BT15029", 5, "[When Attacking] [Once Per Turn] By placing 1 of your other blue Digimon as this Digimon's bottom digivolution card, unsuspend this Digimon.");
        AssertFull(card);
        var self = Put(state.Player, card, suspended: true, sources: Digimon("B33-OLDSRC", 4));
        var otherSrc = Digimon("B33-OTHERSRC", 2);
        var other = Put(state.Player, Digimon("B33-OTHER", 3), sources: otherSrc);

        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, self, card);

        Assert.DoesNotContain(other, state.Player.BattleArea);
        Assert.False(self.IsSuspended);
        // 종합 룰 4-7-8: 놓인 것은 맨 위 카드뿐 — 그 디지몬의 진화원은 트래시
        Assert.Equal("B33-OTHER", self.GetEvolutionStackBottomUp()[0].CardId);
        Assert.Equal(2, self.EvolutionSources.Count);
        Assert.Contains(otherSrc, state.Player.Trash);
    }

    [Fact]
    public void Cost_PlaceOtherDigimon_NoCandidate_DoesNothing()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-NOCAND", 5, "[When Attacking] By placing 1 of your other Digimon as this Digimon's bottom digivolution card, gain 2 memory.");
        var self = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, self, card);
        Assert.Equal(8, state.MemoryGauge);
        Assert.Empty(self.EvolutionSources);
    }

    [Fact]
    public void Cost_PlaceThisDigimonUnderOther_ThenThatDigimonActs_ST13_03()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-ST1303", 3, "[On Play] By placing this Digimon under 1 of your other Digimon that's black or has the [Legend-Arms] trait as its bottom digivolution card, delete 1 of your opponent's Digimon with 5000 DP or less.");
        AssertFull(card);
        var host = Put(state.Player, Digimon("B33-LA", 5, color: CardColor.Red, traits: "Legend-Arms"));
        var self = Put(state.Player, card);
        var victim = Put(state.DummyOpponent, Digimon("B33-VICT", 4, dp: 4000));

        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);

        Assert.DoesNotContain(self, state.Player.BattleArea);
        Assert.Same(card, host.GetEvolutionStackBottomUp()[0]);
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Cost_PlaceThisDigimon_NoValidHost_DoesNotPay()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-ST1303B", 3, "[On Play] By placing this Digimon under 1 of your other Digimon that's black or has the [Legend-Arms] trait as its bottom digivolution card, delete 1 of your opponent's Digimon with 5000 DP or less.");
        Put(state.Player, Digimon("B33-BLUE", 5, color: CardColor.Blue));
        var self = Put(state.Player, card);
        var victim = Put(state.DummyOpponent, Digimon("B33-VICT2", 4, dp: 4000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);
        Assert.Contains(self, state.Player.BattleArea);
        Assert.Contains(victim, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Cost_PlaceThisTamerUnderDigimon_BT13_103()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B33-BT13103", "[End of Opponent's Turn] [Once Per Turn] By placing this Tamer under 1 of your Digimon with [Belphemon] in its name as its bottom digivolution card, delete 1 of your opponent's level 6 Digimon.", CardColor.Purple);
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var bel = Put(state.Player, Digimon("B33-BEL", 6, name: "Belphemon: Rage Mode"));
        var victim = Put(state.DummyOpponent, Digimon("B33-L6", 6));

        engine.RunEffects(EffectTrigger.EndOfOpponentsTurn, state.Player, null, tamer, includeInherited: false);

        Assert.DoesNotContain(tamer, state.Player.Tamers);
        Assert.Same(tamer, bel.GetEvolutionStackBottomUp()[0]);
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Cost_ThisTamerAndTwoTrashCards_ThenIgnoreRequirementsDigivolve_ST17_10()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B33-ST1710", "[Main] By placing this Tamer and 1 [Gargomon] and 1 [Rapidmon] from your trash in any order as one of your [Terriermon]'s bottom digivolution cards, that Digimon may digivolve into [MegaGargomon] in the hand for a digivolution cost of 4, ignoring its digivolution requirements.", CardColor.Green);
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var terrier = Put(state.Player, Digimon("B33-TERRIER", 3, name: "Terriermon", color: CardColor.Green));
        state.Player.Trash.Add(Digimon("B33-GARGO", 4, name: "Gargomon", color: CardColor.Green));
        state.Player.Trash.Add(Digimon("B33-RAPID", 5, name: "Rapidmon", color: CardColor.Green));
        var mega = Digimon("B33-MEGA", 6, name: "MegaGargomon", color: CardColor.Yellow);   // 진화 조건(Lv.5 옐로)을 무시한다
        state.Player.Hand.Add(mega);

        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);

        Assert.Same(mega, terrier.TopCard);
        Assert.Equal(8 - 4, state.MemoryGauge);
        var stack = terrier.GetEvolutionStackBottomUp().Select(c => c.CardId).ToList();
        Assert.Contains("B33-ST1710", stack);
        Assert.Contains("B33-GARGO", stack);
        Assert.Contains("B33-RAPID", stack);
        Assert.Empty(state.Player.Tamers);
    }

    [Fact]
    public void HandMain_TamerDigivolvesIntoThisCard_BT18_053()
    {
        var (state, engine) = NewMainPhase();
        var hybrid = Digimon("B33-BT18053", 5, "[Hand] [Main] By placing 1 [Kazemon] and [Zephyrmon] from your trash under 1 of your green or red Tamers, that Tamer digivolves into this card for digivolution cost of 3, ignoring its digivolution requirements.", color: CardColor.Green);
        AssertFull(hybrid);
        var tamer = Tamer("B33-ZOE", color: CardColor.Green);
        state.Player.Tamers.Add(tamer);
        state.Player.Trash.Add(Digimon("B33-KAZE", 3, name: "Kazemon"));
        state.Player.Trash.Add(Digimon("B33-ZEPH", 4, name: "Zephyrmon"));
        state.Player.Hand.Add(hybrid);

        Assert.True(engine.ActivateHandEffect(hybrid).CanExecute);

        var inst = Assert.Single(state.Player.BattleArea);
        Assert.Same(hybrid, inst.TopCard);
        Assert.Equal(8 - 3, state.MemoryGauge);
        // 테이머 아래의 카드(유일한 테이머) → 진화원
        var ids = inst.GetEvolutionStackBottomUp().Select(c => c.CardId).ToList();
        Assert.Contains("B33-ZOE", ids);
        Assert.Contains("B33-KAZE", ids);
        Assert.Contains("B33-ZEPH", ids);
    }

    [Fact]
    public void Cost_HandCardUnderTamers_BT19_016()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-BT19016", 3, "[On Play] By placing 1 [Blue Flare] trait Digimon card from your hand under any of your Tamers, ＜Draw 1＞");
        AssertFull(card);
        state.Player.Tamers.Add(Tamer("B33-T"));
        var bf = Digimon("B33-BF", 3, traits: "Blue Flare");
        state.Player.Hand.Add(bf);
        state.Player.Hand.Add(Digimon("B33-H1", 3));
        state.Player.Hand.Add(Digimon("B33-H2", 3));
        var self = Put(state.Player, card);

        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);

        Assert.Contains(bf, state.Player.UnderTamers);
        Assert.Equal(3, state.Player.Hand.Count);   // 3 - 1 + 1
    }

    [Fact]
    public void Cost_PlacedOptionUnderTamers_ST23_15()
    {
        var (state, engine) = NewMainPhase();
        var opt = Option("B33-ST2315", "[Start of Your Main Phase] By placing this card from the battle area face down under any of your [BEATBREAK] trait Tamers, ＜Draw 1＞ and gain 1 memory.");
        AssertFull(opt);
        state.Player.Tamers.Add(Tamer("B33-BB", traits: "BEATBREAK"));
        state.Player.OptionsInPlay.Add(new PlacedOption { Card = opt, PlacedTurn = 1 });
        int hand = state.Player.Hand.Count;

        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, null, opt, includeInherited: false);

        Assert.Empty(state.Player.OptionsInPlay);
        Assert.Contains(state.Player.UnderTamers, x => x.CardId == opt.CardId);   // v0.35.0 뒷면으로 (복사본)
        Assert.Equal(1, state.Player.FaceDownUnderTamerCount);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
        Assert.Equal(9, state.MemoryGauge);
    }

    // =====================================================================
    // 비용 · 동작: 시큐리티
    // =====================================================================

    [Fact]
    public void Cost_ThisDigimonFaceUpBottomSecurity_PreventsOtherLeaving_BT19_048()
    {
        var (state, engine) = NewMainPhase();
        var guard = Digimon("B33-BT19048", 3, "[All Turns] [Once Per Turn] When any of your other Digimon with the [Royal Base] trait would leave the battle area by effects, by placing this Digimon as the face-up bottom security card, they don't leave.", traits: "Royal Base");
        AssertFull(guard);
        var g = Put(state.Player, guard);
        var ally = Put(state.Player, Digimon("B33-RB", 5, traits: "Royal Base"));

        engine.ReturnToHand(state.Player, ally);

        Assert.Contains(ally, state.Player.BattleArea);
        Assert.DoesNotContain(g, state.Player.BattleArea);
        Assert.Equal(6, state.Player.SecurityStack.Count);
        Assert.True(GoldfishEngine.IsFaceUp(state.Player, state.Player.SecurityStack[^1]));
    }

    [Fact]
    public void Cost_DigimonWithoutSourcesToBottomSecurity_PrefersOpponents_EX8_028()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-EX8028", 5, "[When Attacking] [Once Per Turn] By placing 1 Digimon with no digivolution cards as the bottom security card, this Digimon unsuspends.");
        AssertFull(card);
        var self = Put(state.Player, card, suspended: true, sources: Digimon("B33-S", 4));
        var opp = Put(state.DummyOpponent, Digimon("B33-OPPNS", 3));

        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, self, card);

        Assert.False(self.IsSuspended);
        Assert.DoesNotContain(opp, state.DummyOpponent.BattleArea);
        Assert.Same(opp.TopCard, state.DummyOpponent.SecurityStack[^1]);   // 그 디지몬 주인의 시큐리티
    }

    [Fact]
    public void Cost_TopStackedCardToSecurity_PreventsLeaving_EX13_032()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-EX13032", 6, "[All Turns] When this Digimon would leave the battle area, by placing its top stacked card as the top security card, it doesn't leave.");
        AssertFull(card);
        var under = Digimon("B33-UNDER", 5);
        var self = Put(state.Player, card, sources: under);

        engine.ReturnToHand(state.Player, self);

        Assert.Contains(self, state.Player.BattleArea);
        Assert.Same(under, self.TopCard);
        Assert.Same(card, state.Player.SecurityStack[0]);
    }

    [Fact]
    public void Action_PlaceOpponentDigimonOnSecurity_CanBePreventedByItsOwnEffect()
    {
        var (state, engine) = NewMainPhase();
        var opp = Put(state.DummyOpponent, Digimon("B33-OPPG", 3, "[All Turns] When this Digimon would leave the battle area, by trashing the bottom card of your security stack, it doesn't leave."));
        Use(state, engine, Option("B33-PLACESEC", "[Main] Place 1 of your opponent's Digimon as the top security card."));
        Assert.Contains(opp, state.DummyOpponent.BattleArea);   // 동작으로 놓는 것은 "떠나려 할 때" 효과로 막을 수 있다 (비용은 아님)
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Action_PlaceHandCardOnTopOfSecurity_LM_023()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-LM023", 4, "[On Play] [When Digivolving] You may place 1 yellow Tamer card or 1 single-color Option card with a cost of 5 or less from your hand on top of your security stack.");
        AssertFull(card);
        var yt = Tamer("B33-YT", color: CardColor.Yellow);
        state.Player.Hand.Add(yt);
        var self = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);
        Assert.Same(yt, state.Player.SecurityStack[0]);
    }

    [Fact]
    public void Action_TrashBothPlayersTopSecurity_BT25_044()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-BT25044", 5, "[On Play] [When Digivolving] By placing 1 other Digimon as the top security card, trash both players' top security cards.");
        AssertFull(card);
        var self = Put(state.Player, card);
        var opp = Put(state.DummyOpponent, Digimon("B33-OP", 4));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);
        Assert.DoesNotContain(opp, state.DummyOpponent.BattleArea);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);   // 5 + 1 - 1
        Assert.Equal(4, state.Player.SecurityStack.Count);
    }

    // =====================================================================
    // 테이머가 떠나려 할 때 · 사건
    // =====================================================================

    [Fact]
    public void TamerWouldLeave_ByOpponentEffect_Prevented_BT23_058()
    {
        var (state, engine) = NewMainPhase();
        var guard = Digimon("B33-BT23058", 5, "[All Turns] When any of your Digimon or Tamers would leave the battle area by your opponent's effects, by suspending this Digimon, 1 of those Digimon or Tamers doesn't leave.");
        AssertFull(guard);
        var g = Put(state.DummyOpponent, guard);
        var oppTamer = Tamer("B33-OT");
        state.DummyOpponent.Tamers.Add(oppTamer);

        Use(state, engine, Option("B33-DELT", "[Main] Delete 1 of your opponent's Tamers."));

        Assert.Contains(oppTamer, state.DummyOpponent.Tamers);
        Assert.True(g.IsSuspended);
    }

    [Fact]
    public void TamerWouldLeave_TraitSubject_TrashSecurity_BT24_101()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-BT24101", 5, "[All Turns] [Once Per Turn] When any of your [TS] trait Digimon or Tamers would leave the battle area, by trashing your top security card, they don't leave.");
        AssertFull(card);
        Put(state.DummyOpponent, card);
        var ts = Tamer("B33-TST", traits: "TS");
        var plain = Tamer("B33-PLAINT");
        state.DummyOpponent.Tamers.Add(plain);
        state.DummyOpponent.Tamers.Add(ts);

        Use(state, engine, Option("B33-DELALL", "[Main] Delete all of your opponent's Tamers."));

        Assert.Contains(ts, state.DummyOpponent.Tamers);
        Assert.DoesNotContain(plain, state.DummyOpponent.Tamers);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Event_AnotherDigimonReturnedToHand_BT14_030()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Put(state.Player, Digimon("B33-BT14030", 4, "[Your Turn] [Once Per Turn] When another Digimon is returned to the hand, ＜Recovery +1＞"));
        var opp = Put(state.DummyOpponent, Digimon("B33-BOUNCE", 3));
        Use(state, engine, Option("B33-BNC", "[Main] Return 1 of your opponent's Digimon to the hand."));
        Assert.DoesNotContain(opp, state.DummyOpponent.BattleArea);
        Assert.Equal(6, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void Event_OpponentDigimonLeaves_AD1_025()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B33-AD1025", 5, "[All Turns] [Once Per Turn] When any of your opponent's Digimon leave the battle area, trash 1 of their Option cards in the battle area and trash their top security card."));
        Put(state.DummyOpponent, Digimon("B33-LEAVE", 3));
        Use(state, engine, Option("B33-BOTTOM", "[Main] Return 1 of your opponent's Digimon to the bottom of the deck."));
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Event_EffectDeletesOtherDigimon_NotBattle_BT16_081()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B33-BT16081", 5, "[All Turns] When an effect deletes one of your other Digimon or Tamers, trash the top card of your opponent's security stack."));
        var mine = Put(state.Player, Digimon("B33-MINE", 3));
        engine.DeleteFrom(state.Player, mine, GoldfishEngine.DeletionCause.Effect);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        var mine2 = Put(state.Player, Digimon("B33-MINE2", 3));
        engine.DeleteFrom(state.Player, mine2, GoldfishEngine.DeletionCause.Battle);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Event_CardAddedToOpponentSecurity_BT16_056Style()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B33-OSA", 5, "[All Turns] When a card is added to your opponent's security stack, gain 1 memory."));
        engine.Recovery(state.DummyOpponent, 1);
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void Event_ThisDigimonChecksSecurity_BT22_080Style()
    {
        var (state, engine) = NewMainPhase();
        var attacker = Put(state.Player, Digimon("B33-CHECK", 4, "[Your Turn] When this Digimon checks your opponent's security stack, gain 1 memory."));
        engine.AttackSecurityOrDirect(attacker);
        Assert.True(state.MemoryGauge >= 9);
    }

    [Fact]
    public void Event_EffectRemovesSecurity_NotCheck_BT15_084()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B33-BT15084", "[All Turns] When an effect removes cards from your security stack, by suspending this Tamer, gain 2 memory.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        Use(state, engine, Option("B33-TRASHSEC", "[Main] Trash your top security card."));
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void Event_RedCardReturnsFromTrash_BT16_011()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Put(state.Player, Digimon("B33-BT16011", 4, "[Your Turn] [Once Per Turn] When a red card returns from your trash to the hand, this Digimon gets +2000 DP for the turn."));
        state.Player.Trash.Add(Digimon("B33-RED", 3, color: CardColor.Red));
        Use(state, engine, Option("B33-RET", "[Main] Return 1 red Digimon card from your trash to the hand."));
        Assert.Equal(6000, watcher.GetCurrentDP());
    }

    [Fact]
    public void Event_TamerDigivolves_BT7_081()
    {
        var (state, engine) = NewMainPhase();
        var watcherTamer = Tamer("B33-BT7081", "[Your Turn] [Once Per Turn] When one of your Tamers digivolves, gain 2 memory.", CardColor.Red);
        AssertFull(watcherTamer);
        state.Player.Tamers.Add(watcherTamer);
        var takuya = Tamer("B33-TAKUYA", color: CardColor.Red);
        state.Player.Tamers.Add(takuya);
        var agu = Digimon("B33-AGUNI", 4, "You may digivolve this card from your hand onto one of your red Tamers as if the Tamer is a level 3 red Digimon for a memory cost of 2.", color: CardColor.Red);
        state.Player.Hand.Add(agu);
        var r = engine.EvolveTamer(takuya, agu); Assert.True(r.CanExecute, r.Reason);
        Assert.Equal(8 - 2 + 2, state.MemoryGauge);
    }

    // =====================================================================
    // 조건
    // =====================================================================

    [Fact]
    public void Condition_StackHasSameLevelCards_BT22_047Style()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-SAME", 4, "[When Digivolving] If this Digimon's stack has 2 or more same-level cards, gain 2 memory.");
        AssertFull(card);
        var a = Put(state.Player, card, sources: Digimon("B33-L3", 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, a, card);
        Assert.Equal(8, state.MemoryGauge);
        var b = Put(state.Player, card, sources: Digimon("B33-L4", 4));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, b, card);
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void Condition_TamerCardWithTraitInSources_BT15_058Style()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-DPOL", 4, "[When Digivolving] If a Tamer card with the [DigiPolice] trait is in this Digimon's digivolution cards, gain 2 memory.");
        AssertFull(card);
        var a = Put(state.Player, card, sources: Tamer("B33-DPT", traits: "DigiPolice"));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, a, card);
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void Condition_NotDeletedInBattle_OnDeletion_BT13_079Style()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-NOTBATTLE", 4, "[On Deletion] If not deleted in battle, gain 2 memory.");
        AssertFull(card);
        engine.DeleteFrom(state.Player, Put(state.Player, card), GoldfishEngine.DeletionCause.Battle);
        Assert.Equal(8, state.MemoryGauge);
        engine.DeleteFrom(state.Player, Put(state.Player, card), GoldfishEngine.DeletionCause.Effect);
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void Condition_PlayedByEffect_BT15_022Style()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-BYEFF", 3, "[On Play] If played by an effect, gain 2 memory.", playCost: 3);
        AssertFull(card);
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute);
        Assert.Equal(5, state.MemoryGauge);
        engine.PlayWithoutCost(state.Player, card);
        Assert.Equal(7, state.MemoryGauge);
    }

    [Fact]
    public void Condition_AnotherSuspendedDigimon_And_FewerSecurity()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-ANOTHER", 4, "[End of Attack] If you have another suspended Digimon, gain 1 memory.");
        AssertFull(card);
        var self = Put(state.Player, card, suspended: true);
        engine.RunEffects(EffectTrigger.EndOfAttack, state.Player, self, card);
        Assert.Equal(8, state.MemoryGauge);   // 이 디지몬 자신은 세지 않는다
        Put(state.Player, Digimon("B33-SUSP", 3), suspended: true);
        engine.RunEffects(EffectTrigger.EndOfAttack, state.Player, self, card);
        Assert.Equal(9, state.MemoryGauge);

        var fewer = Digimon("B33-FEWER", 4, "[On Play] If you have fewer security cards than your opponent, gain 2 memory.");
        AssertFull(fewer);
        state.Player.SecurityStack.RemoveAt(0);
        state.MemoryGauge = 5;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, fewer), fewer);
        Assert.Equal(7, state.MemoryGauge);
    }

    [Fact]
    public void Condition_NamesJoinedWithAnd_RequireBoth_P_022()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-BOTH", 4, "[On Play] If you have [Davis Motomiya] and [Ken Ichijoji] in play, gain 2 memory.");
        AssertFull(card);
        state.Player.Tamers.Add(Tamer("B33-DAVIS", name: "Davis Motomiya"));
        var self = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);
        Assert.Equal(8, state.MemoryGauge);
        state.Player.Tamers.Add(Tamer("B33-KEN", name: "Ken Ichijoji"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void Condition_ConditionWithCommaList_EX6_017()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-EX6017", 5, "[When Attacking] [Once Per Turn] If this Digimon has the [Angel], [Archangel] or [Three Great Angels] trait, ＜Draw 1＞", traits: "Archangel");
        AssertFull(card);
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, Put(state.Player, card), card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    [Fact]
    public void Condition_IfThisEffectDidntReturn_DrawsOnlyWhenNothingReturned_BT25_091Style()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-DIDNT", 3, "[On Play] You may return 1 [TS] trait Option card from your trash to the hand. If this effect didn't return, ＜Draw 1＞");
        AssertFull(card);
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, card), card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);   // 되돌릴 카드가 없었다 → 드로우

        state.Player.Trash.Add(Card("B33-TSOPT", "[Main] Gain 1 memory.", traits: "TS"));
        hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, Put(state.Player, card), card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);   // 되돌렸다 → 드로우 없음 (1장은 되돌린 카드)
        Assert.Empty(state.Player.Trash);
    }

    [Fact]
    public void ColorRequirement_IgnoredWithOwnFieldCondition_EX4_068()
    {
        var opt = Option("B33-EX4068", "While you have a green Digimon or Tamer, you can ignore this card's color requirements.");
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(opt).Level);
    }

    // =====================================================================
    // 진화 조건 무시 · 이 효과로 등장한 디지몬
    // =====================================================================

    [Fact]
    public void IgnoreRequirements_NamedDigimon_BT22_013Option()
    {
        var (state, engine) = NewMainPhase();
        var gab = Put(state.Player, Digimon("B33-GABU", 3, name: "Gabumon"));
        var mg = Digimon("B33-MG", 6, name: "MetalGarurumon", color: CardColor.Black);
        state.Player.Hand.Add(mg);
        Use(state, engine, Option("B33-IGN", "[Main] 1 of your [Gabumon] may digivolve into [MetalGarurumon] in the hand, ignoring digivolution requirements and without paying the cost."));
        Assert.Same(mg, gab.TopCard);
        Assert.Equal(8, state.MemoryGauge);
    }

    [Fact]
    public void IgnoreLevel_KeepsColorRequirement_BT24_025Style()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B33-IGNLV", 4, "[On Play] This Digimon may digivolve into [Venusmon] in the hand, ignoring level.", color: CardColor.Blue);
        AssertFull(card);
        var self = Put(state.Player, card);
        var wrongColor = Digimon("B33-VENUS-R", 6, name: "Venusmon", color: CardColor.Red);
        state.Player.Hand.Add(wrongColor);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);
        Assert.Same(card, self.TopCard);   // 색 조건은 무시하지 않는다
        var right = Digimon("B33-VENUS-B", 6, name: "Venusmon", color: CardColor.Blue, evoCost: 3);
        state.Player.Hand.Add(right);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, self, card);
        Assert.Same(right, self.TopCard);
    }

    [Fact]
    public void PlayedByThisEffect_GainsRush_ST14_09Style()
    {
        var (state, engine) = NewMainPhase();
        var imp = Digimon("B33-IMP", 3, name: "Impmon", color: CardColor.Purple);
        state.Player.Trash.Add(imp);
        Use(state, engine, Option("B33-RUSHPLAY", "[Main] You may play 1 [Impmon] from your trash without paying the cost. The Digimon played by this effect gains ＜Rush＞ for the turn."));
        var played = state.Player.BattleArea.Single(d => d.TopCard == imp);
        Assert.Contains(Keyword.Rush, played.ActiveKeywords);
    }

    // =====================================================================
    // 생성 한글 (공식 표기)
    // =====================================================================

    [Theory]
    [InlineData("[When Attacking] [Once Per Turn] By placing 1 of your other blue Digimon as this Digimon's bottom digivolution card, unsuspend this Digimon.",
        "【어택 시】 [턴에 1회] 블루인 다른 자신의 디지몬 1마리를 이 디지몬의 진화원 아래에 놓는 것으로, 이 디지몬을 액티브로 한다.")]
    [InlineData("[On Play] By placing this Digimon under 1 of your other Digimon that's black or has the [Legend-Arms] trait as its bottom digivolution card, delete 1 of your opponent's Digimon with 5000 DP or less.",
        "【등장 시】 이 디지몬을 특징으로 「Legend-Arms」를 가지거나 블랙인 자신의 다른 디지몬 1마리의 진화원 아래에 놓는 것으로, DP 5000 이하인 상대의 디지몬 1마리를 소멸시킨다.")]
    [InlineData("[When Digivolving] [When Attacking] [Once Per Turn] By placing 1 Digimon with no digivolution cards as the bottom security card, this Digimon unsuspends.",
        "【진화 시】 【어택 시】 [턴에 1회] 진화원을 갖지 않은 디지몬 1마리를 시큐리티 아래에 놓는 것으로, 이 디지몬을 액티브로 한다.")]
    [InlineData("[End of Attack] By placing this Digimon's top card as your top security card, unsuspend this Digimon.",
        "【어택 종료 시】 이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 자신의 시큐리티 위에 놓는 것으로, 이 디지몬을 액티브로 한다.")]
    [InlineData("[When Digivolving] If a Tamer card with the [DigiPolice] trait is in this Digimon's digivolution cards, gain 1 memory.",
        "【진화 시】 이 디지몬의 진화원에 특징으로 「디지대」를 가진 테이머 카드가 있을 때, 메모리를 +1 한다.")]
    [InlineData("[On Play] [When Digivolving] Suspend 1 of your opponent's Digimon. Then, if this Digimon's stack has 2 or more same-level cards, 1 of your opponent's Digimon can't unsuspend until their turn ends.",
        "【등장 시】 【진화 시】 상대의 디지몬 1마리를 레스트시킨다. 그 후, 이 디지몬에 Lv.이 같은 카드가 2장 이상 겹쳐져 있을 때, 상대의 턴 종료 시까지, 상대의 디지몬 1마리는 액티브가 되지 않는다.")]
    [InlineData("[On Deletion] If not deleted in battle, gain 1 memory.",
        "【소멸 시】 배틀 이외로 소멸하고 있었다면, 메모리를 +1 한다.")]
    [InlineData("[When Attacking] [Once Per Turn] If your opponent has no Digimon with as many or more digivolution cards as this Digimon, unsuspend this Digimon.",
        "【어택 시】 [턴에 1회] 진화원 매수가 이 디지몬 이상의 상대의 디지몬이 없을 때, 이 디지몬을 액티브로 한다.")]
    [InlineData("[Your Turn] [Once Per Turn] When another Digimon is returned to the hand, gain 1 memory.",
        "【자신의 턴】 [턴에 1회] 다른 디지몬이 패로 되돌아갔을 때, 메모리를 +1 한다.")]
    [InlineData("[Hand] [Main] By placing 1 [Kazemon] and [Zephyrmon] from your trash under 1 of your green or red Tamers, that Tamer digivolves into this card for digivolution cost of 3, ignoring its digivolution requirements.",
        "[패] 【메인】 자신의 트래시에서 「페어리몬」과 「슈츠몬」 1장씩을 그린/레드인 자신의 테이머 1명의 아래에 놓는 것으로, 그 테이머를 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다.")]
    [InlineData("[Main] By placing 1 [Garurumon] and 1 [WereGarurumon] from your trash as 1 of your [Gabumon]'s bottom digivolution cards, that Digimon may digivolve into [MetalGarurumon] in your hand, ignoring its digivolution requirements and without paying the cost.",
        "【메인】 자신의 트래시에서 「가루몬」과 「워가루몬」 1장씩을 자신의 「파피몬」 1마리의 진화원 아래에 놓는 것으로, 그 디지몬을 패의 「메탈가루몬」으로 진화 조건을 무시하고 코스트를 지불하지 않고 진화시킬 수 있다.")]
    [InlineData("[All Turns] When any of your Digimon or Tamers would leave the battle area by your opponent's effects, by suspending this Digimon, 1 of those Digimon or Tamers doesn't leave.",
        "【서로의 턴】 자신의 디지몬/테이머가 상대의 효과로 배틀 에어리어를 벗어날 때, 이 디지몬을 레스트시키는 것으로, 그 디지몬 1마리는 배틀 에어리어를 벗어나지 않는다.")]
    public void KoreanRender_OfficialNotation(string en, string ko) =>
        Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));
}
