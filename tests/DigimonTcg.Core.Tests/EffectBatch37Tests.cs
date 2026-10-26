using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.37.0 미지원 문장 11차 묶음: 메모리 조건 지속 효과 · DP 소멸 효과의 상한 · "can't play (or move) Digimon with N DP or less" ·
/// 색 추가 · DNA 진화 이름 취급 · 진화원 위 또는 아래 · 이 카드를 진화원에 · 시큐리티 (상대 디지몬을 시큐리티로 · 시큐리티 디지몬 DP · 배틀 없이 등장) ·
/// 등장 제한 ("This effect can't play [X]" · "~ cards of the same level")
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch37Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Yellow, int dp = -1, string? name = null, string inherited = "", int evoCost = 1, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", int dp = -1, string? name = null, int playCost = 3,
        CardColor color = CardColor.Yellow, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, inherited, 1, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B37-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B37-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B37-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B37-OSEC" + i, 3, dp: 1000));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    // =====================================================================
    // 1. 메모리 조건 지속 효과
    // =====================================================================

    [Fact]
    public void MemoryCondition_SecurityAttack_FollowsMemory_BT1_018()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-BT1018", 5, "[Your Turn] While you have 3 or more memory, this Digimon gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RecalculateFieldBuffs();
        Assert.Equal(1, me.EffectSecurityAttackBonus);
        state.MemoryGauge = 2;
        engine.RecalculateFieldBuffs();
        Assert.Equal(0, me.EffectSecurityAttackBonus);
    }

    [Fact]
    public void MemoryChange_ByEffect_RecalculatesContinuousEffects()
    {
        var (state, engine) = NewMainPhase();
        state.MemoryGauge = 2;
        var card = Digimon("B37-MEM", 5, "[Your Turn] While you have 3 or more memory, this Digimon gets +3000 DP.");
        var gainer = Digimon("B37-GAIN", 3, "[On Play] Gain 1 memory.");
        var me = Put(state.Player, card);
        var g = Put(state.Player, gainer);
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, me.GetCurrentDP());
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, g, gainer);   // 메모리 +1 → 3 (다시 계산하지 않아도 바로)
        Assert.Equal(3, state.MemoryGauge);
        Assert.Equal(8000, me.GetCurrentDP());
    }

    [Fact]
    public void OpponentMemory_NoneCanSuspend_EX8_026()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-EX8026", 5, "[All Turns] While you have 1 or more memory, none of your opponent's Digimon can suspend.");
        AssertFull(card);
        Put(state.DummyOpponent, card);
        var mine = Put(state.Player, Digimon("B37-MINE", 4));
        state.MemoryGauge = 3;   // 상대(효과 주인)의 메모리 = -3
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.IsAttackLocked(state, mine));
        state.MemoryGauge = -2;  // 상대 쪽 2
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.IsAttackLocked(state, mine));
        Assert.True(EffectRules.IsSuspendLocked(state, mine));
    }

    [Fact]
    public void NoneCanActivateOnPlayOrWhenDigivolving_EX8_029_EX8_035()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-EX8029", 6, "[All Turns] While you have 1 or more memory, none of your [DS] trait Digimon are affected by your opponent's Digimon's effects. While you have 1 or less, none of your opponent's Digimon can activate [On Play] effects.");
        AssertFull(card);
        AssertFull(Digimon("B37-EX8035", 5, "[All Turns] While you have 1 or more memory, none of your opponent's Digimon can activate [When Digivolving] effects."));
        Put(state.DummyOpponent, card);
        state.MemoryGauge = 4;   // 상대(주인) 메모리 -4 ≤ 1
        engine.RecalculateFieldBuffs();
        var drawer = Digimon("B37-DRAW", 3, "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)");
        state.Player.Hand.Add(drawer);
        int hand = state.Player.Hand.Count;
        Assert.True(engine.PlayCard(drawer).CanExecute);
        Assert.Equal(hand - 1, state.Player.Hand.Count);   // 드로우하지 않았다
    }

    [Fact]
    public void ShorthandMemoryCondition_IsRead_BT15_016_LM_041()
    {
        AssertFull(Digimon("B37-BT15016", 5, "[On Play] [When Digivolving] If your opponent has 4 or less memory, 1 of your opponent's Digimon with 8000 DP or more can't attack until the end of their turn. If they have 4 or more, delete 1 of their Digimon with 6000 DP or less."));
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-SHORT", 5, "[On Play] If you have 1 or more memory, gain 1 memory. Then, if you have 2 or less, ＜Draw 1＞.");
        AssertFull(card);
        state.MemoryGauge = 1;
        var me = Put(state.Player, card);
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(2, state.MemoryGauge);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    // =====================================================================
    // 2. DP 소멸 효과의 상한
    // =====================================================================

    [Fact]
    public void DeletionCap_ThisDigimon_WhileZeroMemory_BT17_008()
    {
        foreach (var (memory, deleted) in new[] { (0, true), (3, false) })
        {
            var (state, engine) = NewMainPhase();
            state.MemoryGauge = memory;
            var src = Digimon("B37-BT17008", 3, inherited: "[All Turns] While you have 0 or less memory, add 2000 to this Digimon's DP-based deletion effects' maximums.");
            AssertFull(src);
            var top = Digimon("B37-DELTOP", 5, "[On Play] Delete 1 of your opponent's Digimon with 3000 DP or less.");
            var me = Put(state.Player, top, false, src);
            var enemy = Put(state.DummyOpponent, Digimon("B37-E5000", 5, dp: 5000));
            engine.RecalculateFieldBuffs();
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, top);
            Assert.Equal(!deleted, state.DummyOpponent.BattleArea.Contains(enemy));
        }
    }

    [Fact]
    public void DeletionCap_AllYourEffects_OnlyDeletion_BT12_001()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B37-BT12001", 3, inherited: "[Your Turn] Add 1000 to the maximum DP you can choose with DP-based deletion effects.");
        AssertFull(src);
        Put(state.Player, Digimon("B37-HOST", 4), false, src);
        var bouncer = Digimon("B37-BOUNCE", 4, "[On Play] Return 1 of your opponent's Digimon with 3000 DP or less to the hand.");
        var deleter = Digimon("B37-DEL", 4, "[On Play] Delete 1 of your opponent's Digimon with 3000 DP or less.");
        var b = Put(state.Player, bouncer);
        var d = Put(state.Player, deleter);
        var enemy = Put(state.DummyOpponent, Digimon("B37-E4000", 4, dp: 4000));
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, b, bouncer);
        Assert.Contains(enemy, state.DummyOpponent.BattleArea);   // 소멸 효과가 아니면 상한이 늘지 않는다
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, deleter);
        Assert.DoesNotContain(enemy, state.DummyOpponent.BattleArea);
    }

    // =====================================================================
    // 3. DP N 이하의 디지몬을 등장시킬 수 없다
    // =====================================================================

    [Fact]
    public void StaticPlayLock_WhileOpponentHasMemory_BT14_017()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-BT14017", 6, "[All Turns] While your opponent has 1 or more memory, they can't play Digimon with 6000 DP or less, and this Digimon gets +4000 DP.", dp: 11000);
        AssertFull(card);
        var foe = Put(state.DummyOpponent, card);
        engine.RecalculateFieldBuffs();
        Assert.Equal(15000, foe.GetCurrentDP());
        var small = Digimon("B37-SMALL", 4, dp: 6000);
        var big = Digimon("B37-BIG", 5, dp: 7000);
        state.Player.Hand.Add(small);
        state.Player.Hand.Add(big);
        Assert.False(engine.PlayCard(small).CanExecute);
        Assert.False(engine.CanPlayByEffect(state.Player, small, PlayOrigin.Hand));   // 효과로도 (Q&A EX3-012)
        Assert.True(engine.PlayCard(big).CanExecute);
        state.MemoryGauge = 0;   // 메모리가 0 → 해제
        engine.RecalculateFieldBuffs();
        Assert.True(engine.PlayCard(small).CanExecute);
    }

    [Fact]
    public void TimedPlayAndMoveLock_EX7_014()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-EX7014", 6, "[When Digivolving] Your opponent can't play or move Digimon with 6000 DP or less until the end of their turn.");
        AssertFull(card);
        AssertFull(Digimon("B37-EX3012", 6, "[On Play] Delete all of your opponent's Digimon with the lowest DP. If no Digimon is deleted by this effect, your opponent can't play Digimon with 5000 DP or less until the end of their turn."));
        var foe = Put(state.DummyOpponent, card);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.DummyOpponent, foe, card);
        var small = Digimon("B37-SMALL", 4, dp: 6000);
        state.Player.Hand.Add(small);
        Assert.False(engine.PlayCard(small).CanExecute);
        Assert.True(EffectRules.PlayDpBlocked(state, state.Player, small));
        // 육성 에어리어에서 이동도 막는다 (DP 6000 이하)
        state.Player.BreedingArea = new CardInstance(Digimon("B37-BREED", 4, dp: 5000), 1);
        state.CurrentPhase = Phase.Breeding;
        Assert.False(ActionValidator.ValidateMoveFromBreeding(state).CanExecute);
        // 기간이 끝나면 해제
        state.TurnSequence += 2;
        Assert.True(ActionValidator.ValidateMoveFromBreeding(state).CanExecute);
    }

    // =====================================================================
    // 4. 색 · DNA 진화 이름
    // =====================================================================

    [Fact]
    public void AlsoTreatedAsYellow_OnYourTurn_CanDigivolveIntoYellow_BT3_014()
    {
        var (state, engine) = NewMainPhase();
        var red = Digimon("B37-BT3014", 4, "[Your Turn] This Digimon is also treated as yellow.", color: CardColor.Red);
        AssertFull(red);
        var me = Put(state.Player, red);
        engine.RecalculateFieldBuffs();
        var yellow5 = Digimon("B37-Y5", 5, color: CardColor.Yellow);
        Assert.Contains(CardColor.Yellow, me.Colors);
        Assert.True(ActionValidator.ValidateEvolution(me, yellow5).CanExecute);
        me.IsOwnersTurn = false;   // 상대의 턴에는 레드뿐
        engine.RecalculateFieldBuffs();
        Assert.DoesNotContain(CardColor.Yellow, me.Colors);
        Assert.False(ActionValidator.ValidateEvolution(me, yellow5).CanExecute);
    }

    [Fact]
    public void ColorsOfDigivolutionCards_FourColors_BT8_084()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-BT8084", 5, "[Your Turn] This Digimon is treated as also having the color of its digivolution cards. While this Digimon has 4 or more color, it gets +4000 DP.", dp: 7000);
        AssertFull(card);
        var me = Put(state.Player, card, false, Digimon("B37-R", 4, color: CardColor.Red), Digimon("B37-B", 3, color: CardColor.Blue));
        engine.RecalculateFieldBuffs();
        Assert.Equal(3, me.Colors.Count);
        Assert.Equal(7000, me.GetCurrentDP());
        me.AddBottomSource(Digimon("B37-G", 3, color: CardColor.Green));
        engine.RecalculateFieldBuffs();
        engine.RecalculateFieldBuffs();   // DP 조건은 색을 다시 센 뒤
        Assert.Equal(4, me.Colors.Count);
        Assert.Equal(11000, me.GetCurrentDP());
    }

    [Fact]
    public void DnaAlias_OnlyForNamedDnaCard_BT20_025()
    {
        var card = Digimon("B37-BT20025", 5, "[All Turns] This Digimon is also treated as Lv.6 [Slayerdramon] for [Examon]'s DNA digivolution.", color: CardColor.Green);
        AssertFull(card);
        var d = new CardInstance(card, 1);
        var facets = GoldfishEngine.DnaFacets(d, Digimon("B37-EXAMON", 7, name: "Examon")).ToList();
        Assert.Equal(2, facets.Count);
        Assert.Equal(6, facets[1].Level);
        Assert.True(CardNames.Is(facets[1], "Slayerdramon"));
        Assert.Single(GoldfishEngine.DnaFacets(d, Digimon("B37-OTHER", 7, name: "Omnimon")));
    }

    [Fact]
    public void NegateSecurityOptions_WithTraitCondition_BT7_014()
    {
        var (state, engine) = NewMainPhase();
        const string text = "[Your Turn] While this Digimon has the [Hybrid] or [Ten Warriors] trait, it doesn't activate [Security] effects on Option cards it checks.";
        var hybrid = Digimon("B37-BT7014", 5, text, traits: "Hybrid");
        AssertFull(hybrid);
        var plain = Digimon("B37-PLAIN", 5, text);
        var a = Put(state.Player, hybrid);
        var b = Put(state.Player, plain);
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(a, Keyword.NegateSecurityOptions));
        Assert.False(CardScripts.HasKeyword(b, Keyword.NegateSecurityOptions));
    }

    // =====================================================================
    // 5. 진화원 · 이 카드를 진화원에
    // =====================================================================

    [Fact]
    public void TrashTopOrBottomSource_RecommendsTheValuableOne_BT27_020()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B37-BT27020", 4, inherited: "[When Attacking] [Once Per Turn] Trash the top or bottom digivolution card of 1 of your opponent's Digimon.");
        AssertFull(src);
        var me = Put(state.Player, Digimon("B37-ATK", 5), false, src);
        var valuable = Digimon("B37-VAL", 4, inherited: "[Your Turn] This Digimon gets +2000 DP.");
        var plainSrc = Digimon("B37-PLN", 3);
        var foe = Put(state.DummyOpponent, Digimon("B37-FOE", 5));
        foe.AddBottomSource(plainSrc);
        foe.AddBottomSource(valuable);   // 맨 아래 = 진화원 효과가 있는 카드
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, me.TopCard);
        Assert.Single(foe.EvolutionSources);
        Assert.Contains(valuable, state.DummyOpponent.Trash);
        Assert.Contains(plainSrc, foe.EvolutionSources);
    }

    [Fact]
    public void OptionPlacesItselfUnderDigimon_EX7_070()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B37-EX7070", "[Main] Delete 1 of your opponent's Digimon with the lowest play cost. Then, place this card as the bottom digivolution card of 1 of your Digimon with the [Three Musketeers] trait.", playCost: 2);
        AssertFull(option);
        var host = Put(state.Player, Digimon("B37-3M", 5, traits: "Three Musketeers"));
        Put(state.Player, Digimon("B37-OTHER", 5));
        Put(state.DummyOpponent, Digimon("B37-FOE", 3));
        state.Player.Hand.Add(option);
        state.Player.Trash.Add(option);   // 같은 카드 1장이 트래시에도 (같은 카드 여러 장은 같은 객체) — 그 카드를 빼면 안 된다
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.Contains(option, host.EvolutionSources);
        Assert.Single(state.Player.Trash, x => ReferenceEquals(x, option));
        Assert.Empty(state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void HandMainPlacesThisCardUnder_BT9_029()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-BT9029", 4, "[Hand] [Main] If you have a Digimon with [Justimon] or [Raidenmon] in its name in play, you may pay 1 memory to place this card under that Digimon as its bottom digivolution card.");
        AssertFull(card);
        state.Player.Hand.Add(card);
        engine.ActivateHandEffect(card);   // 놓을 디지몬이 없다 — 메모리를 내지 않고 패에 그대로
        Assert.Contains(card, state.Player.Hand);
        Assert.Equal(8, state.MemoryGauge);
        var host = Put(state.Player, Digimon("B37-JUST", 6, name: "Justimon: Blitz Arm"));
        int mem = state.MemoryGauge;
        var res = engine.ActivateHandEffect(card);
        Assert.True(res.CanExecute, res.Reason);
        Assert.Contains(card, host.EvolutionSources);
        Assert.DoesNotContain(card, state.Player.Hand);
        Assert.Equal(mem - 1, state.MemoryGauge);
    }

    // =====================================================================
    // 6. 시큐리티
    // =====================================================================

    [Fact]
    public void PlaceOpponentDigimonAtBottomOfSecurity_RB1_019()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-BOTSEC", 5, "[On Play] Place 1 of your opponent's Digimon face down at the bottom of their security stack.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var foeCard = Digimon("B37-FOE", 4);
        var foe = Put(state.DummyOpponent, foeCard, false, Digimon("B37-FOESRC", 3));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Empty(state.DummyOpponent.BattleArea);
        Assert.Equal(6, state.DummyOpponent.SecurityStack.Count);
        Assert.Same(foeCard, state.DummyOpponent.SecurityStack[^1]);
        Assert.Single(state.DummyOpponent.Trash);   // 진화원은 트래시
    }

    [Fact]
    public void OneDigimonAndAllSecurityDigimonGetMinusDp_BT7_098()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B37-BT7098", "[Main] 1 of your opponent's Digimon and all of your opponent's Security Digimon get -3000 DP for the turn.", playCost: 1);
        AssertFull(option);
        Put(state.Player, Digimon("B37-HOST", 4));
        var foe = Put(state.DummyOpponent, Digimon("B37-FOE", 5, dp: 6000));
        state.Player.Hand.Add(option);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.Equal(3000, foe.GetCurrentDP());
        Assert.Contains(engine.SecurityDpBuffsOf(state.DummyOpponent), b => b.Dp == -3000 && b.Until == state.TurnSequence);
    }

    [Fact]
    public void PlaceThisCardAtBottomOfSecurity_OnDeletion_BT14_038()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-BT14038", 4, "[On Deletion] Place this card at the bottom of your security stack.");
        AssertFull(card);
        var me = Put(state.Player, card);
        Assert.True(engine.DeleteFrom(state.Player, me));
        Assert.Same(card, state.Player.SecurityStack[^1]);
        Assert.DoesNotContain(card, state.Player.Trash);
    }

    [Fact]
    public void SecurityDigimon_PlaysWithoutBattling_BT5_112()
    {
        var (state, engine) = NewMainPhase();
        var sec = Digimon("B37-BT5112", 6, "[Security] Play this card without battling and without paying the cost.", dp: 12000);
        AssertFull(sec);
        state.DummyOpponent.SecurityStack.Insert(0, sec);
        var attacker = Put(state.Player, Digimon("B37-ATK", 5, dp: 5000));
        engine.AttackSecurityOrDirect(attacker);
        Assert.Contains(attacker, state.Player.BattleArea);   // 배틀하지 않았다
        Assert.Contains(state.DummyOpponent.BattleArea, d => ReferenceEquals(d.TopCard, sec));
        Assert.Equal(1, engine.SecurityPlayWithoutBattleTotal);
    }

    [Fact]
    public void SecurityDigimon_ConditionalPlay_BattlesWhenConditionFails_BT24_015()
    {
        var (state, engine) = NewMainPhase();
        var sec = Digimon("B37-BT24015", 5, "[Security] If your opponent has a level 6 or higher Digimon, play this card without battling and without paying the cost.", dp: 3000);
        AssertFull(sec);
        state.DummyOpponent.SecurityStack.Insert(0, sec);
        var attacker = Put(state.Player, Digimon("B37-ATK", 5, dp: 5000));
        engine.AttackSecurityOrDirect(attacker);
        Assert.DoesNotContain(state.DummyOpponent.BattleArea, d => ReferenceEquals(d.TopCard, sec));
        Assert.Contains(sec, state.DummyOpponent.Trash);   // Lv.6 이상이 없으니 보통 배틀
    }

    // =====================================================================
    // 7. 등장 제한
    // =====================================================================

    [Fact]
    public void ThisEffectCantPlayNamedCard_BT26_059()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-BT26059", 6, "[On Play] You may play 1 [Titan] trait Digimon card from your trash without paying the cost. This effect can't play [Plutomon].");
        AssertFull(card);
        var pluto = Digimon("B37-PLUTO", 7, name: "Plutomon", traits: "Titan");
        var other = Digimon("B37-TITAN", 5, traits: "Titan");
        state.Player.Trash.Add(pluto);
        state.Player.Trash.Add(other);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, other));
        Assert.Contains(pluto, state.Player.Trash);
    }

    [Fact]
    public void ThisEffectCantPlaySameLevel_P_220()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B37-P220", 6, "[On Play] You may play 2 level 6 or lower Digimon cards from your trash without paying the costs. This effect can't play cards of the same level.");
        AssertFull(card);
        var a = Digimon("B37-L6A", 6);
        var b = Digimon("B37-L6B", 6);
        var c4 = Digimon("B37-L4", 4);
        state.Player.Trash.AddRange(new[] { a, b, c4 });
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        var played = state.Player.BattleArea.Where(d => !ReferenceEquals(d, me)).Select(d => d.TopCard.Level).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 4, 6 }, played);
    }

    // =====================================================================
    // 8. 한글
    // =====================================================================

    [Theory]
    [InlineData("[All Turns] While you have 0 or less memory, add 2000 to this Digimon's DP-based deletion effects' maximums.", "【서로의 턴】 메모리가 0 이하인 동안, 이 디지몬의 DP 소멸 효과의 상한 +2000.")]
    [InlineData("[Your Turn] Add 1000 to the maximum DP you can choose with DP-based deletion effects.", "【자신의 턴】 자신이 발휘하는 DP 소멸 효과의 상한 +1000.")]
    [InlineData("[Your Turn] This Digimon is also treated as yellow.", "【자신의 턴】 이 디지몬의 색은 옐로로도 취급한다.")]
    [InlineData("[When Digivolving] Your opponent can't play or move Digimon with 6000 DP or less until the end of their turn.", "【진화 시】 상대의 턴 종료 시까지, 상대는 DP 6000 이하의 디지몬을 등장시킬 수 없으며, 이동시킬 수 없다.")]
    [InlineData("[On Deletion] Place this card at the bottom of your security stack.", "【소멸 시】 이 카드를 시큐리티 아래에 놓는다.")]
    [InlineData("[All Turns] While you have 1 or more memory, none of your opponent's Digimon can suspend.", "【서로의 턴】 메모리가 1 이상인 동안, 상대의 디지몬 전부는 레스트할 수 없다.")]
    public void KoreanRendering(string en, string ko) => Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));
}
