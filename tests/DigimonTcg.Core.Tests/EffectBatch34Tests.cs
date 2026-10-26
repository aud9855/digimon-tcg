using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.34.0 미지원 문장 8차 묶음: 제한 · 보호 ("can't attack players" · "can't be deleted in battle / by your opponent's effects" · "isn't affected by ~" ·
/// "can't be returned to the hand or deck" · "can't have its DP reduced" · "can also attack your opponent's unsuspended Digimon" · "can't activate [When Digivolving] effects") ·
/// "End the attack" · 상대가 고르는 동작 · 배틀 · 대상 조건 · 비용 · 셈 · 테이머 대상 · 등장 코스트 (1장마다) · 색 조건 · 진화 제한 · 생성 한글
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch34Tests
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

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, int playCost = 3, params string[] traits) =>
        Card(id, main, CardType.Tamer, color: color, name: name, playCost: playCost, traits: traits);

    private static CardData Option(string id, string main, int cost = 0, CardColor color = CardColor.Blue) => Card(id, main, CardType.Option, playCost: cost, color: color);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B34-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B34-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B34-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B34-OSEC" + i, 3, dp: 1000));
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
        if (!state.Player.BattleArea.Any(d => d.TopCard.AllColors.Contains(option.Color)) && !state.Player.Tamers.Any(t => t.AllColors.Contains(option.Color)))
            state.Player.Tamers.Add(Tamer("B34-COLOR", color: option.Color));
        state.Player.Hand.Add(option);
        var result = engine.PlayCard(option);
        Assert.True(result.CanExecute, result.Reason);
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    // =====================================================================
    // 어택 제한
    // =====================================================================

    [Fact]
    public void Static_CantAttackPlayers_BlocksSecurityAttackOnly_ST7_04()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-ST704", 4, "[Your Turn] This Digimon can't attack players.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var foe = Put(state.DummyOpponent, Digimon("B34-FOE", 3), suspended: true);
        engine.RecalculateFieldBuffs();

        Assert.False(ActionValidator.ValidateAttack(state, me, null).CanExecute);
        Assert.True(ActionValidator.ValidateAttack(state, me, foe).CanExecute);
    }

    [Fact]
    public void Static_CantAttackDigimon_EX2_036()
    {
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B34-EX2036", 4, "[Your Turn] This Digimon can't attack your opponent's Digimon."));
        var foe = Put(state.DummyOpponent, Digimon("B34-FOE2", 3), suspended: true);
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateAttack(state, me, foe).CanExecute);
        Assert.True(ActionValidator.ValidateAttack(state, me, null).CanExecute);
    }

    [Fact]
    public void Static_CanAlsoAttackUnsuspended_BT8_018()
    {
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B34-BT8018", 5, "[Your Turn] This Digimon can also attack your opponent's unsuspended Digimon."));
        var plain = Put(state.Player, Digimon("B34-PLAIN", 5));
        var foe = Put(state.DummyOpponent, Digimon("B34-ACTIVE", 3));
        engine.RecalculateFieldBuffs();

        Assert.True(ActionValidator.ValidateAttack(state, me, foe).CanExecute);
        Assert.False(ActionValidator.ValidateAttack(state, plain, foe).CanExecute);
        Assert.True(engine.AttackOpponentDigimon(me, foe).CanExecute);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Static_CanAttackUnsuspendedWithNoSources_Only_EX1_016()
    {
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B34-EX1016", 5, "[Your Turn] This Digimon can also attack your opponent's unsuspended Digimon with no digivolution cards."));
        var bare = Put(state.DummyOpponent, Digimon("B34-BARE", 3));
        var stacked = Put(state.DummyOpponent, Digimon("B34-STACK", 4), sources: Digimon("B34-SRC", 3));
        engine.RecalculateFieldBuffs();
        Assert.True(ActionValidator.ValidateAttack(state, me, bare).CanExecute);
        Assert.False(ActionValidator.ValidateAttack(state, me, stacked).CanExecute);
    }

    [Fact]
    public void Timed_OpponentDigimonCantAttackPlayers_AiDoesNotAttackSecurity_BT16_053()
    {
        var (state, engine) = NewMainPhase();
        var foe = Put(state.DummyOpponent, Digimon("B34-AIATK", 5, dp: 9000));
        Use(state, engine, Option("B34-NOPLAYER", "[Main] 1 of your opponent's Digimon can't attack players until the end of their turn."));
        Assert.Contains(Keyword.CannotAttackPlayers, foe.ActiveKeywords);
        Assert.False(EffectRules.CanAttackPlayer(foe));
        // AI 정책도 시큐리티 어택을 고르지 않는다
        var ai = new SmartAiPolicy();
        foe.IsOwnersTurn = true;
        var pick = ai.ChooseNextAttack(engine, 5);
        Assert.True(pick == null || pick.Value.Target != null);
    }

    [Fact]
    public void Static_CantAttackOrBlock_BT19_077()
    {
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B34-BT19077", 4, "[All Turns] This Digimon can't attack or block.\n＜Blocker＞"));
        engine.RecalculateFieldBuffs();
        Assert.False(ActionValidator.ValidateAttack(state, me, null).CanExecute);
        Assert.False(EffectRules.CanBlock(state, me));
    }

    // =====================================================================
    // 보호
    // =====================================================================

    [Fact]
    public void Timed_CantBeDeletedInBattle_SurvivesLosingBattle_BT16_018()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-BT16018", 4, "[On Play] [When Digivolving] 1 of your Digimon can't be deleted in battle until the end of your opponent's turn.", dp: 1000);
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        var big = Put(state.DummyOpponent, Digimon("B34-BIG", 6, dp: 12000), suspended: true);

        Assert.True(engine.AttackOpponentDigimon(me, big).CanExecute);
        Assert.Contains(me, state.Player.BattleArea);
    }

    [Fact]
    public void Static_CantBeDeletedByOpponentEffects_OwnEffectStillDeletes_BT14_062()
    {
        var (state, engine) = NewMainPhase();
        var foe = Put(state.DummyOpponent, Digimon("B34-BT14062", 4, "[All Turns] This Digimon can't be deleted by your opponent's effects."));
        engine.RecalculateFieldBuffs();
        Use(state, engine, Option("B34-DEL", "[Main] Delete 1 of your opponent's Digimon."));
        Assert.Contains(foe, state.DummyOpponent.BattleArea);

        // 주인의 효과로는 소멸한다
        var mine = Put(state.Player, Digimon("B34-MYPROT", 4, "[All Turns] This Digimon can't be deleted by your opponent's effects."));
        engine.RecalculateFieldBuffs();
        Use(state, engine, Option("B34-SELFDEL", "[Main] Delete 1 of your Digimon."));
        Assert.DoesNotContain(mine, state.Player.BattleArea);
    }

    [Fact]
    public void Static_UnaffectedByOpponentDigimonEffects_OptionStillWorks_EX5_074()
    {
        var (state, engine) = NewMainPhase();
        var foe = Put(state.DummyOpponent, Digimon("B34-EX5074", 6, "[All Turns] This Digimon isn't affected by the effects of your opponent's Digimon."));
        engine.RecalculateFieldBuffs();
        var deleter = Digimon("B34-DELETER", 5, "[On Play] Delete 1 of your opponent's Digimon.");
        var me = Put(state.Player, deleter);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, deleter);
        Assert.Contains(foe, state.DummyOpponent.BattleArea);

        Use(state, engine, Option("B34-OPTDEL", "[Main] Delete 1 of your opponent's Digimon."));
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Timed_OpponentEffectsDontAffect_EX13_062()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-EX13062", 5, "[On Play] [When Digivolving] Your opponent's effects don't affect this Digimon until their turn ends.");
        AssertFull(card);
        var foe = Put(state.DummyOpponent, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, card);
        Use(state, engine, Option("B34-OPTDEL2", "[Main] Delete 1 of your opponent's Digimon."));
        Assert.Contains(foe, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Static_CantBeReturnedByOpponentEffects_BT11_060()
    {
        var (state, engine) = NewMainPhase();
        var foe = Put(state.DummyOpponent, Digimon("B34-BT11060", 4, "[All Turns] This Digimon can't be returned to hands or decks by your opponent's effects."));
        engine.RecalculateFieldBuffs();
        Use(state, engine, Option("B34-BOUNCE", "[Main] Return 1 of your opponent's Digimon to its owner's hand."));
        Assert.Contains(foe, state.DummyOpponent.BattleArea);
        Use(state, engine, Option("B34-BOTTOM", "[Main] Return 1 of your opponent's Digimon to the bottom of the deck."));
        Assert.Contains(foe, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Static_OpponentEffectsCantReduceDp_BT6_053()
    {
        var (state, engine) = NewMainPhase();
        var foe = Put(state.DummyOpponent, Digimon("B34-BT6053", 4, "[All Turns] Your opponent's effects can't reduce this Digimon's DP.", dp: 4000));
        engine.RecalculateFieldBuffs();
        Use(state, engine, Option("B34-MINUS", "[Main] 1 of your opponent's Digimon gets -5000 DP for the turn."));
        Assert.Contains(foe, state.DummyOpponent.BattleArea);
        Assert.Equal(4000, foe.GetCurrentDP());
    }

    [Fact]
    public void Timed_CantActivateWhenDigivolving_BlocksEngineDigivolve_BT20_033()
    {
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Digimon("B34-MYBASE", 3, dp: 5000));
        // 상대(AI)의 효과: "1 of their Digimon" = 플레이어의 디지몬
        var aiCard = Digimon("B34-BT20033", 4, "[On Play] Until your opponent's turn ends, 1 of their Digimon can't activate [When Digivolving] effects and gets -3000 DP.");
        AssertFull(aiCard);
        var foe = Put(state.DummyOpponent, aiCard);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, aiCard);
        Assert.Contains(Keyword.CannotActivateWhenDigivolving, mine.ActiveKeywords);
        Assert.Equal(2000, mine.GetCurrentDP());

        var evo = Digimon("B34-MYEVO", 4, "[When Digivolving] Gain 1 memory.", evoCost: 2);
        state.Player.Hand.Add(evo);
        Assert.True(engine.EvolveDigimon(mine, evo).CanExecute);
        Assert.Equal(6, state.MemoryGauge);   // 8 - 2 (【진화 시】 메모리 +1은 발휘하지 않는다)
    }

    // =====================================================================
    // 어택 종료
    // =====================================================================

    [Fact]
    public void EndTheAttack_OpponentEffectEndsPlayersSecurityAttack_EX6_045()
    {
        var (state, engine) = NewMainPhase();
        var guard = Digimon("B34-EX6045", 5, "[Opponent's Turn] [Once Per Turn] When an opponent's Digimon attacks, by deleting 1 of your other Digimon, end the attack.");
        AssertFull(guard);
        Put(state.DummyOpponent, guard);
        var fodder = Put(state.DummyOpponent, Digimon("B34-FODDER", 3));
        var attacker = Put(state.Player, Digimon("B34-ATTACKER", 5, dp: 9000));

        Assert.True(engine.AttackSecurityOrDirect(attacker).CanExecute);
        Assert.DoesNotContain(fodder, state.DummyOpponent.BattleArea);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);   // 시큐리티 체크 없이 끝났다
        Assert.True(attacker.IsSuspended);
    }

    // =====================================================================
    // 상대가 고르는 동작
    // =====================================================================

    [Theory]
    [InlineData(6, false)]   // AI는 패가 5장 이상이면 버린다 → 메모리 없음
    [InlineData(2, true)]
    public void OpponentMayTrash_IfTheyDont_GainMemory_BT15_074(int handSize, bool gain)
    {
        var (state, engine) = NewMainPhase();
        for (int i = 0; i < handSize; i++) state.DummyOpponent.Hand.Add(Digimon("B34-OH" + i, 3));
        var card = Digimon("B34-BT15074", 4, "[On Play] [When Digivolving] Your opponent may trash 1 Digimon card in their hand. If they don't, gain 1 memory.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(8 + (gain ? 1 : 0), state.MemoryGauge);
        Assert.Equal(handSize - (gain ? 0 : 1), state.DummyOpponent.Hand.Count);
    }

    [Fact]
    public void TheyTrash_And_WithoutLooking_And_AddSecurity()
    {
        var (state, engine) = NewMainPhase();
        for (int i = 0; i < 4; i++) state.DummyOpponent.Hand.Add(Digimon("B34-OHX" + i, 3));
        Use(state, engine, Option("B34-THEY", "[Main] If your opponent has 3 or more cards in their hand, they trash 1 card in their hand."));
        Assert.Equal(3, state.DummyOpponent.Hand.Count);
        Use(state, engine, Option("B34-BLIND", "[Main] Trash 1 card in your opponent's hand without looking."));
        Assert.Equal(2, state.DummyOpponent.Hand.Count);
        Use(state, engine, Option("B34-ADDSEC", "[Main] Your opponent adds the top card of their security stack to the hand."));
        Assert.Equal(3, state.DummyOpponent.Hand.Count);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void OpponentMayDelete_AiDeletesLevel3_NoRecovery_BT18_082()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-BT18082", 6, "[On Play] [When Digivolving] Your opponent may delete 1 of their Digimon or Tamers. If this effect didn't delete, ＜Recovery +1 (Deck)＞ and trash the top card of your opponent's security stack.");
        AssertFull(card);
        var small = Put(state.DummyOpponent, Digimon("B34-SMALL", 3));
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(small, state.DummyOpponent.BattleArea);
        Assert.Equal(5, state.Player.SecurityStack.Count);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void OpponentMayDelete_NothingToDelete_PunishmentHappens()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-BT18082B", 6, "[On Play] Your opponent may delete 1 of their Digimon or Tamers. If this effect didn't delete, ＜Recovery +1 (Deck)＞ and trash the top card of your opponent's security stack.");
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(6, state.Player.SecurityStack.Count);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    // =====================================================================
    // 배틀 · 대상 조건 · 고르기
    // =====================================================================

    [Fact]
    public void MayBattle_DeletesWeakerOpponent_BT25_057()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-BT25057", 5, "[When Digivolving] This Digimon may battle 1 of your opponent's Digimon.", dp: 7000);
        AssertFull(card);
        var me = Put(state.Player, card);
        var weak = Put(state.DummyOpponent, Digimon("B34-WEAK", 4, dp: 5000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.DoesNotContain(weak, state.DummyOpponent.BattleArea);
        Assert.Contains(me, state.Player.BattleArea);
        Assert.False(me.IsSuspended);
    }

    [Fact]
    public void Target_AsManyOrFewerDigivolutionCards_BT15_032()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-BT15032", 5, "[When Digivolving] Return 1 of your opponent's Digimon with as many or fewer digivolution cards as this Digimon to the hand.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: Digimon("B34-S1", 4));
        var big = Put(state.DummyOpponent, Digimon("B34-BIGSTACK", 6, dp: 15000), sources: new[] { Digimon("B34-S2", 4), Digimon("B34-S3", 5) });
        var small = Put(state.DummyOpponent, Digimon("B34-SMALLSTACK", 4), sources: Digimon("B34-S4", 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Contains(big, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(small, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Target_TwoOrMoreColors_BT8_095()
    {
        var (state, engine) = NewMainPhase();
        var mono = Put(state.Player, Digimon("B34-MONO", 4, dp: 9000));
        var multi = Put(state.Player, Digimon("B34-MULTI", 4, second: CardColor.Red, dp: 5000));
        Use(state, engine, Option("B34-BT8095", "[Main] 1 of your Digimon with 2 or more colors gains ＜Security A. +1＞ for the turn."));
        Assert.Equal(2, multi.GetSecurityAttackCount());
        Assert.Equal(1, mono.GetSecurityAttackCount());
    }

    [Fact]
    public void Choose_ThenDeleteWithDpLessOrEqualToChosen_BT7_093()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B34-HYB", 5, dp: 6000, traits: "Hybrid"));
        var weak = Put(state.DummyOpponent, Digimon("B34-W6", 4, dp: 6000));
        var strong = Put(state.DummyOpponent, Digimon("B34-W7", 5, dp: 7000));
        Use(state, engine, Option("B34-BT7093", "[Main] Choose 1 of your Digimon with the [Hybrid] trait. Delete 1 of your opponent's Digimon with DP less than or equal to the chosen Digimon's DP."));
        Assert.DoesNotContain(weak, state.DummyOpponent.BattleArea);
        Assert.Contains(strong, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void DeleteDigimonOrTamers_TamerCanBeChosen_EX10_010()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B34-OTAMER", playCost: 4);
        state.DummyOpponent.Tamers.Add(tamer);
        Use(state, engine, Option("B34-EX10010", "[Main] Delete 1 of your opponent's play cost 7 or lower Digimon or Tamers."));
        Assert.DoesNotContain(tamer, state.DummyOpponent.Tamers);
        Assert.Contains(tamer, state.DummyOpponent.Trash);
    }

    [Fact]
    public void ReturnOpponentTamer_BT13_031()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B34-OTAMER2");
        state.DummyOpponent.Tamers.Add(tamer);
        Use(state, engine, Option("B34-BT13031", "[Main] Return 1 of your opponent's Tamers to its owner's hand."));
        Assert.Contains(tamer, state.DummyOpponent.Hand);
    }

    // =====================================================================
    // 비용 · 셈
    // =====================================================================

    [Fact]
    public void Cost_TrashUpTo3InHand_GainMemoryForEach_ST16_12()
    {
        var (state, engine) = NewMainPhase();
        state.MemoryGauge = 3;   // 메모리는 10이 상한
        for (int i = 0; i < 5; i++) state.Player.Hand.Add(Digimon("B34-H" + i, 3));
        var card = Digimon("B34-ST1612", 4, "[When Digivolving] By trashing up to 3 cards in your hand, gain 1 Memory for each card trashed.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        int trashed = 5 - state.Player.Hand.Count;
        Assert.InRange(trashed, 1, 3);
        Assert.Equal(3 + trashed, state.MemoryGauge);
    }

    [Fact]
    public void Cost_ReturnUpTo5FromTrash_Gain2ForEach_BT4_074()
    {
        var (state, engine) = NewMainPhase();
        state.MemoryGauge = 2;
        for (int i = 0; i < 2; i++) state.Player.Trash.Add(Digimon("B34-DB" + i, 3, traits: "D-Brigade"));
        var card = Digimon("B34-BT4074", 4, "[On Play] By returning up to 5 Digimon cards with [D-Brigade] trait from your trash to the top of your deck in any order, gain 2 memory for each card you return this way.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Empty(state.Player.Trash);
        Assert.Equal(6, state.MemoryGauge);
    }

    [Fact]
    public void Cost_TrashAny2Sources_EX10_058Style()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-ANY2", 5, "[On Play] By trashing any 2 of this Digimon's digivolution cards, gain 2 memory.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: new[] { Digimon("B34-A", 3), Digimon("B34-B", 4), Digimon("B34-C", 4) });
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Single(me.EvolutionSources);
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void Cost_ReturnFromOpponentTrash_RB1_028()
    {
        var (state, engine) = NewMainPhase();
        state.DummyOpponent.Trash.Add(Digimon("B34-OTR", 4));
        var card = Digimon("B34-RB1028", 3, "[On Play] By returning 1 Digimon card from your opponent's trash to the bottom of their deck, ＜Draw 1＞.");
        AssertFull(card);
        int hand = state.Player.Hand.Count;
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Empty(state.DummyOpponent.Trash);
        Assert.Equal("B34-OTR", state.DummyOpponent.MainDeck.Last().CardId);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    [Fact]
    public void Cost_SuspendOpponentDigimonOrTamer_PicksTamer_BT13_057()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B34-OTAMER3");
        state.DummyOpponent.Tamers.Add(tamer);
        var card = Digimon("B34-BT13057", 5, "[When Digivolving] By suspending 1 of your opponent's Digimon or Tamers, unsuspend this Digimon.");
        AssertFull(card);
        var me = Put(state.Player, card, suspended: true);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.False(state.DummyOpponent.HasActiveTamer(tamer));
        Assert.False(me.IsSuspended);
    }

    [Fact]
    public void Counter_ColorsInDigivolutionCards_EX9_074()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B34-EX9074", 5, "[All Turns] This Digimon gets +1000 DP for each color in its digivolution cards.", dp: 5000);
        AssertFull(card);
        var me = Put(state.Player, card, sources: new[] { Digimon("B34-R", 3, color: CardColor.Red), Digimon("B34-G", 4, color: CardColor.Green), Digimon("B34-G2", 4, color: CardColor.Green) });
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, me.GetCurrentDP());
    }

    [Fact]
    public void PlaceOneEach_GainMemoryPerCard_BT9_067()
    {
        var (state, engine) = NewMainPhase();
        state.Player.Trash.Add(Digimon("B34-RAI", 4, name: "Raijinmon"));
        state.Player.Trash.Add(Digimon("B34-FUJ", 4, name: "Fujinmon"));
        var card = Digimon("B34-BT9067", 6, "[On Play] Place 1 [Raijinmon], [Fujinmon], and [Suijinmon] from your trash under this Digimon in any order as its bottom digivolution cards. Gain 1 memory for each card placed.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(2, me.EvolutionSources.Count);
        Assert.Equal(10, state.MemoryGauge);
    }

    // =====================================================================
    // 지속 효과 · 등장 코스트 · 색 조건 · 진화 제한
    // =====================================================================

    [Fact]
    public void Static_ThisDigimonWithTrait_GainsRush_BT19_008()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B34-BT19008", 2, inherited: "[Your Turn] This Digimon with the [Xros Heart] trait gains ＜Rush＞.");
        var xh = Put(state.Player, Digimon("B34-XH", 4, traits: "Xros Heart"), turnPlayed: 2, sources: src);
        var other = Put(state.Player, Digimon("B34-NOXH", 4), turnPlayed: 2, sources: Digimon("B34-BT19008B", 2, inherited: "[Your Turn] This Digimon with the [Xros Heart] trait gains ＜Rush＞."));
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(xh, Keyword.Rush));
        Assert.False(CardScripts.HasKeyword(other, Keyword.Rush));
    }

    [Fact]
    public void Static_AllOfYourOtherDigimon_ExcludesSelf_BT16_050()
    {
        var (state, engine) = NewMainPhase();
        var self = Put(state.Player, Digimon("B34-BT16050", 5, "[All Turns] All of your other Digimon with the [D-Brigade] or [DigiPolice] trait get +1000 DP.", dp: 5000, traits: "D-Brigade"));
        var mate = Put(state.Player, Digimon("B34-MATE", 4, dp: 4000, traits: "DigiPolice"));
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, self.GetCurrentDP());
        Assert.Equal(5000, mate.GetCurrentDP());
    }

    [Fact]
    public void PlayCost_PlaceUpTo3DifferentNamesUnderIt_4Each_BT15_102()
    {
        var (state, engine) = NewMainPhase();
        state.MemoryGauge = 3;
        foreach (var n in new[] { "MetalSeadramon", "Puppetmon", "Machinedramon" })
            state.Player.Trash.Add(Digimon("B34-DM-" + n, 6, name: n, traits: "Dark Masters"));
        var card = Digimon("B34-BT15102", 7, "When this card would be played, by placing up to 3 [Dark Masters] trait cards with different names from your battle area or trash under it, reduce the play cost by 4 for each one.", playCost: 14);
        AssertFull(card);
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute);
        var played = state.Player.BattleArea.Single(d => d.TopCard == card);
        Assert.Equal(3, played.EvolutionSources.Count);
        Assert.Equal(1, state.MemoryGauge);   // 3 - (14 - 12)
    }

    [Fact]
    public void ColorRequirement_AlsoMetByBlack_LM_033()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B34-BLACK", 4, color: CardColor.Black));
        var option = Card("B34-LM033", "Black also meets this card's color requirements.\n[Main] Gain 1 memory.", CardType.Option, playCost: 0, color: CardColor.Red);
        AssertFull(option);
        state.Player.Hand.Add(option);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void EvoOnlyInto_White_BT15_031()
    {
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B34-BT15031", 5, "[Your Turn] This Digimon can only digivolve into white Digimon.", color: CardColor.Blue));
        var blueEvo = Digimon("B34-BLUE6", 6, color: CardColor.Blue);
        var whiteEvo = Card("B34-WHITE6", "", CardType.Digimon, 6, color: CardColor.White);
        whiteEvo.EvoRequirementColor = CardColor.Blue;
        me.IsOwnersTurn = true;
        Assert.False(ActionValidator.ValidateEvolution(me, blueEvo).CanExecute);
        Assert.True(ActionValidator.ValidateEvolution(me, whiteEvo).CanExecute);
    }

    [Fact]
    public void OpponentCantGainMemoryOtherThanTamers_BT3_061()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B34-BT3061", 5, "[All Turns] Your opponent can't gain memory other than by Tamer effects."));
        engine.RecalculateFieldBuffs();
        var gainer = Digimon("B34-AIGAIN", 4, "[On Play] Gain 2 memory.");
        var foe = Put(state.DummyOpponent, gainer);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, gainer);
        Assert.Equal(8, state.MemoryGauge);   // AI가 메모리를 얻으면 플레이어 쪽 게이지가 줄어든다 — 그대로
    }

    // =====================================================================
    // 생성 한글 (공식 표기)
    // =====================================================================

    [Theory]
    [InlineData("[Your Turn] This Digimon can't attack players.", "【자신의 턴】 이 디지몬은 플레이어에게 어택할 수 없다.")]
    [InlineData("[All Turns] This Digimon can't be returned to hands or decks by your opponent's effects.", "【서로의 턴】 이 디지몬은 상대의 효과로 패/덱으로 되돌아가지 않는다.")]
    [InlineData("[All Turns] This Digimon can't be deleted by your opponent's effects.", "【서로의 턴】 이 디지몬은 상대의 효과로 소멸하지 않는다.")]
    [InlineData("[All Turns] While this Digimon is suspended, it isn't affected by the effects of your opponent's Digimon.", "【서로의 턴】 이 디지몬이 레스트 상태인 동안, 이 디지몬은 상대의 디지몬의 효과를 받지 않는다.")]
    [InlineData("[Your Turn] This Digimon can also attack your opponent's unsuspended Digimon.", "【자신의 턴】 이 디지몬은 액티브 상태인 상대의 디지몬에게도 어택할 수 있다.")]
    [InlineData("[Your Turn] This Digimon with the [Xros Heart] trait gains ＜Rush＞.", "【자신의 턴】 특징으로 「크로스 하트」를 가진 이 디지몬은 《속공》 (이 디지몬은 등장한 턴에도 어택할 수 있다)을 얻는다.")]
    [InlineData("[On Play] [When Digivolving] By placing 1 [Belphemon: Rage Mode] from your trash on top of this Digimon's digivolution cards, this Digimon can't attack and isn't affected by your opponent's effects until the end of your opponent's turn.",
        "【등장 시】 【진화 시】 자신의 트래시에서 「벨페몬: 레이지 모드」 1장을 이 디지몬의 진화원 위에 놓는 것으로, 상대의 턴 종료 시까지, 이 디지몬은 어택할 수 없고, 상대의 효과를 받지 않는다.")]
    [InlineData("[On Play] [When Digivolving] Until your opponent's turn ends, 1 of their Digimon can't activate [When Digivolving] effects and gets -3000 DP.",
        "【등장 시】 【진화 시】 상대의 턴 종료 시까지, 상대의 디지몬 1마리의 【진화 시】 효과는 발휘하지 않고, 그 디지몬을 DP -3000 한다.")]
    [InlineData("[On Play] [When Digivolving] Your opponent may trash 1 Digimon card in their hand. If they don't, gain 1 memory.",
        "【등장 시】 【진화 시】 상대는 본인의 패에서 디지몬 카드 1장을 파기할 수 있다. 이 효과로 파기하지 않았다면, 메모리를 +1 한다.")]
    [InlineData("[On Play] [When Digivolving] Your opponent may delete 1 of their Digimon or Tamers. If this effect didn't delete, trash the top card of your opponent's security stack.",
        "【등장 시】 【진화 시】 상대는 본인의 디지몬/테이머 1마리(명)를 소멸시킬 수 있다. 이 효과로 소멸하지 않았다면, 상대의 시큐리티를 위에서부터 1장 파기한다.")]
    [InlineData("[On Deletion] Trash 1 card in your opponent's hand without looking.", "【소멸 시】 상대의 패 1장을 보지 않고 파기한다.")]
    [InlineData("[Opponent's Turn] [Once Per Turn] When an opponent's Digimon attacks, by deleting 1 of your other Digimon, end the attack.",
        "【상대의 턴】 [턴에 1회] 상대의 디지몬이 어택했을 때, 자신의 다른 디지몬 1마리를 소멸시키는 것으로, 그 어택을 종료한다.")]
    [InlineData("[When Digivolving] This Digimon may battle 1 of your opponent's Digimon.", "【진화 시】 이 디지몬은 상대의 디지몬 1마리와 배틀할 수 있다.")]
    [InlineData("[When Digivolving] [When Attacking] [Once Per Turn] Return 1 of your opponent's Digimon with as many or fewer digivolution cards as this Digimon to the hand.",
        "【진화 시】 【어택 시】 [턴에 1회] 진화원 매수가 이 디지몬 이하인 상대의 디지몬 1마리를 패로 되돌린다.")]
    [InlineData("[Main] Choose 1 of your Digimon with the [Hybrid] trait. Delete 1 of your opponent's Digimon with DP less than or equal to the chosen Digimon's DP.",
        "【메인】 특징으로 「하이브리드체」를 가진 자신의 디지몬 1마리를 선택한다. 선택한 디지몬의 DP 이하인 상대의 디지몬 1마리를 소멸시킨다.")]
    [InlineData("[When Digivolving] By trashing up to 3 cards in your hand, gain 1 Memory for each card trashed.",
        "【진화 시】 자신의 패에서 카드 3장까지를 파기하는 것으로, 이 효과로 파기한 1장마다 메모리를 +1 한다.")]
    [InlineData("When this card would be played, by placing up to 3 [Dark Masters] trait cards with different names from your battle area or trash under it, reduce the play cost by 4 for each one.",
        "이 카드가 등장할 때, 자신의 배틀 에어리어/트래시에서 명칭이 서로 다른 특징으로 「어둠의 4천왕」을 가진 카드 3장까지를 이 카드의 아래에 놓는 것으로, 놓은 1장마다 지불하는 등장 코스트 -4.")]
    [InlineData("[Your Turn] This Digimon can only digivolve into white Digimon.", "【자신의 턴】 이 디지몬은 화이트인 디지몬으로만 진화할 수 있다.")]
    [InlineData("[All Turns] Your opponent can't gain memory other than by Tamer effects.", "【서로의 턴】 상대는 테이머의 효과 이외로 메모리를 플러스할 수 없다.")]
    [InlineData("[All Turns] This Digimon gets +1000 DP for each color in its digivolution cards.", "【서로의 턴】 이 디지몬의 진화원의 색 1색마다 이 디지몬의 DP를 +1000 한다.")]
    [InlineData("[On Play] Place 1 [Raijinmon], [Fujinmon], and [Suijinmon] from your trash under this Digimon in any order as its bottom digivolution cards. Gain 1 memory for each card placed.",
        "【등장 시】 자신의 트래시에서 「뇌신몬」과 「풍신몬」과 「수신몬」 1장씩을 이 디지몬의 진화원 아래에 원하는 순서대로 놓는다. 이 효과로 놓은 1장마다 메모리를 +1 한다.")]
    public void KoreanRender_OfficialNotation(string en, string ko) =>
        Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));
}
