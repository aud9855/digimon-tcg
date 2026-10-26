using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.38.0 미지원 문장 12차 묶음: 다른 카드와 비교하는 대상 (놓은 카드 · 소멸한 디지몬 · 등장한 디지몬 · 시큐리티 매수) ·
/// 액티브 + 다른 동작 · 테이머 액티브 · 여러 곳에서 고르는 비용 · 얻는 ≪가드≫ · ≪디코이≫ · ≪천승≫ · "~ may gain ＜Rush＞ and attack" ·
/// 진화원 위 또는 아래 · 시큐리티 위 또는 아래 · [시큐리티]【상대의 턴 종료 시】
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch38Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, System.Collections.Generic.IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, System.Collections.Generic.IReadOnlyList<string> options, int recommended) => recommended;
    }

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

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B38-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B38-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B38-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B38-OSEC" + i, 3, dp: 1000));
        }
        return (state, new GoldfishEngine(state) { PlayerChoices = new AcceptAll() });
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    private static string L(GameState s) => string.Join(" | ", s.ActionLogs.TakeLast(40));

    private static bool Has(PlayerZones z, CardData top) => z.BattleArea.Any(d => ReferenceEquals(d.TopCard, top));

    // =====================================================================
    // 1. 다른 카드와 비교하는 대상
    // =====================================================================

    [Fact]
    public void SameLevelAsPlacedCard_DeletesOnlyThatLevel_BT18_042()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT18042", 6, "[When Digivolving] By placing 1 Digimon card from this Digimon's digivolution cards as your bottom security card, delete all of your opponent's Digimon with the same level as the placed card.");
        AssertFull(card);
        var src = Digimon("B38-SRC4", 4);
        var me = Put(state.Player, card, false, src);
        var lv4a = Digimon("B38-O4A", 4); var lv4b = Digimon("B38-O4B", 4); var lv5 = Digimon("B38-O5", 5);
        Put(state.DummyOpponent, lv4a); Put(state.DummyOpponent, lv4b); Put(state.DummyOpponent, lv5);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Same(src, state.Player.SecurityStack[^1]);
        Assert.False(Has(state.DummyOpponent, lv4a));
        Assert.False(Has(state.DummyOpponent, lv4b));
        Assert.True(Has(state.DummyOpponent, lv5));
        Assert.Equal("【진화 시】 이 디지몬의 진화원에서 디지몬 카드 1장을 시큐리티 아래에 놓는 것으로, 놓은 카드와 같은 Lv.의 상대의 디지몬 전부를 소멸시킨다.",
            KoreanEffectRenderer.RenderClause("[When Digivolving] By placing 1 Digimon card from this Digimon's digivolution cards as your bottom security card, delete all of your opponent's Digimon with the same level as the placed card."));
    }

    [Fact]
    public void LevelLessOrEqualToDeletedDigimon_BT11_076()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT11076", 5, "[When Attacking] By deleting 1 of your other Digimon, delete 1 of your opponent's unsuspended Digimon with a level less than or equal to the deleted Digimon's level.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var fodder = Digimon("B38-FOD3", 3);
        Put(state.Player, fodder);
        var o3 = Digimon("B38-O3", 3); var o4 = Digimon("B38-O4", 4);
        Put(state.DummyOpponent, o3); Put(state.DummyOpponent, o4);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.False(Has(state.Player, fodder));
        Assert.False(Has(state.DummyOpponent, o3));
        Assert.True(Has(state.DummyOpponent, o4));   // Lv.4 > 소멸한 Lv.3
    }

    [Fact]
    public void DpLessOrEqualToDeletedDigimon_UsesLastDp_RB1_029()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-RB1029", 5, "[End of Attack] By deleting this Digimon, delete 1 of your opponent's Digimon with DP less than or equal to the deleted Digimon's DP.", dp: 6000);
        AssertFull(card);
        var me = Put(state.Player, card);
        me.TimedDpBuffs.Add((2000, state.TurnSequence));   // 떠나기 전 DP 8000
        engine.RecalculateFieldBuffs();
        var o8 = Digimon("B38-O8K", 5, dp: 8000); var o9 = Digimon("B38-O9K", 6, dp: 9000);
        Put(state.DummyOpponent, o8); Put(state.DummyOpponent, o9);
        engine.RunEffects(EffectTrigger.EndOfAttack, state.Player, me, card);
        Assert.False(Has(state.Player, card), L(state));
        Assert.False(Has(state.DummyOpponent, o8), L(state));
        Assert.True(Has(state.DummyOpponent, o9));
    }

    [Fact]
    public void LevelLessOrEqualToTrashedCard_OptionMain_BT15_099()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B38-BT15099", "[Main] By trashing 1 Digimon card in your hand, delete 1 of your opponent's Digimon whose level is less than or equal to the trashed card's level.", playCost: 2);
        AssertFull(option);
        Put(state.Player, Digimon("B38-HOST", 4));
        var lv5 = Digimon("B38-H5", 5);
        state.Player.Hand.Add(lv5);
        state.Player.Hand.Add(option);
        var o5 = Digimon("B38-O5", 5, dp: 9000); var o6 = Digimon("B38-O6", 6, dp: 12000);
        Put(state.DummyOpponent, o5); Put(state.DummyOpponent, o6);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.True(state.Player.Trash.Contains(lv5), L(state));
        Assert.False(Has(state.DummyOpponent, o5));
        Assert.True(Has(state.DummyOpponent, o6));
    }

    [Fact]
    public void PlayCostLessOrEqualToDigimonYouPlayed_BT10_106()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B38-BT10106", "[Main] You may play 1 Digimon card with [Justimon] in its name from your hand without paying its memory cost. If you do, delete 1 of your opponent's Digimon with a play cost less than or equal to the Digimon you played.", playCost: 2);
        AssertFull(option);
        Put(state.Player, Digimon("B38-HOST", 4));   // 옵션의 색 조건
        var just = Digimon("B38-JUST", 6, name: "Justimon: Blitz Arm", playCost: 9);
        state.Player.Hand.Add(just);
        state.Player.Hand.Add(option);
        var cheap = Digimon("B38-OC9", 6, playCost: 9, dp: 9000); var pricey = Digimon("B38-OC12", 7, playCost: 12, dp: 13000);
        Put(state.DummyOpponent, cheap); Put(state.DummyOpponent, pricey);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.True(Has(state.Player, just), L(state));
        Assert.False(Has(state.DummyOpponent, cheap));
        Assert.True(Has(state.DummyOpponent, pricey));
    }

    [Fact]
    public void LevelAsHighOrLowerThanSecurityCount_EX6_028()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-EX6028", 5, "[On Play] Return 1 of your opponent's Digimon with a level as high or lower than the number of cards in your security stack to the hand.");
        AssertFull(card);
        state.Player.SecurityStack.RemoveRange(0, 1);   // 시큐리티 4장
        var me = Put(state.Player, card);
        var o4 = Digimon("B38-O4", 4, dp: 4000); var o5 = Digimon("B38-O5", 5, dp: 9000);
        Put(state.DummyOpponent, o4); Put(state.DummyOpponent, o5);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(o4, state.DummyOpponent.Hand);
        Assert.True(Has(state.DummyOpponent, o5));
    }

    [Fact]
    public void DeleteChosenAndOpponentWithLowerDp_BT12_108()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B38-BT12108", "[Main] Choose 1 of your Digimon with the [Machine] or [Cyborg] trait. Delete that Digimon and 1 of your opponent's Digimon with DP less than or equal to the chosen Digimon.", playCost: 4);
        AssertFull(option);
        var mine = Digimon("B38-MACH", 5, dp: 7000, traits: "Machine");
        Put(state.Player, mine);
        state.Player.Hand.Add(option);
        var o7 = Digimon("B38-O7K", 5, dp: 7000); var o8 = Digimon("B38-O8K", 5, dp: 8000);
        Put(state.DummyOpponent, o7); Put(state.DummyOpponent, o8);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.False(Has(state.Player, mine));
        Assert.False(Has(state.DummyOpponent, o7));
        Assert.True(Has(state.DummyOpponent, o8));
    }

    [Fact]
    public void ChooseAndReturnAllOfSameLevel_BT17_078()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT17078", 7, "[On Play] Choose 1 of your opponent's Digimon and return all of your opponent's Digimon with the same level as it to the bottom of the deck.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var a = Digimon("B38-OA6", 6, dp: 12000); var b = Digimon("B38-OB6", 6, dp: 11000); var c = Digimon("B38-OC4", 4);
        Put(state.DummyOpponent, a); Put(state.DummyOpponent, b); Put(state.DummyOpponent, c);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.False(Has(state.DummyOpponent, a));
        Assert.False(Has(state.DummyOpponent, b));
        Assert.True(Has(state.DummyOpponent, c));
    }

    // =====================================================================
    // 2. 액티브 + 다른 동작 · 테이머 액티브
    // =====================================================================

    [Fact]
    public void UnsuspendAndGainMemoryForEach_BT11_017()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT11017", 5, "[On Play] Unsuspend this Digimon, and gain 1 memory for each red Tamer you have in play.");
        AssertFull(card);
        var me = Put(state.Player, card, suspended: true);
        state.Player.Tamers.Add(Tamer("B38-RT1", color: CardColor.Red));
        state.Player.Tamers.Add(Tamer("B38-RT2", color: CardColor.Red));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.False(me.IsSuspended);
        Assert.Equal(10, state.MemoryGauge);
        Assert.Equal("【등장 시】 이 디지몬을 액티브로 하고, 자신의 레드의 테이머 1장마다 메모리를 +1 한다.",
            KoreanEffectRenderer.RenderClause("[On Play] Unsuspend this Digimon, and gain 1 memory for each red Tamer you have in play."));
    }

    [Fact]
    public void UnsuspendThisDigimonOrBlueTamer_BT12_029()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT12029", 5, "[When Digivolving] Unsuspend this Digimon or 1 of your blue Tamers.");
        AssertFull(card);
        var me = Put(state.Player, card, suspended: false);
        var tamer = Tamer("B38-BT", color: CardColor.Blue);
        state.Player.Tamers.Add(tamer);
        state.Player.SuspendedTamers.Add(tamer);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);   // 이 디지몬은 액티브 — 테이머를 액티브로
        Assert.DoesNotContain(tamer, state.Player.SuspendedTamers);
        Assert.Equal("【진화 시】 이 디지몬 또는 블루인 자신의 테이머 1명을 액티브로 한다.", KoreanEffectRenderer.RenderClause("[When Digivolving] Unsuspend this Digimon or 1 of your blue Tamers."));
    }

    [Fact]
    public void UnsuspendThisDigimonAndTamer_ByCost_P_048()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-P048", 5, "[When Digivolving] By returning 3 non Digi-Egg cards from your trash to the bottom of your deck in any order, unsuspend this Digimon and 1 of your Tamers.");
        AssertFull(card);
        for (int i = 0; i < 3; i++) state.Player.Trash.Add(Digimon("B38-TR" + i, 3));
        var me = Put(state.Player, card, suspended: true);
        var tamer = Tamer("B38-T");
        state.Player.Tamers.Add(tamer);
        state.Player.SuspendedTamers.Add(tamer);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.False(me.IsSuspended, L(state));
        Assert.DoesNotContain(tamer, state.Player.SuspendedTamers);
        Assert.Empty(state.Player.Trash);
    }

    [Fact]
    public void UntilEndOfOpponentsTurn_OneDoesntUnsuspend_BT13_058()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT13058", 5, "[When Digivolving] Suspend 1 of your opponent's Digimon. Until the end of your opponent's turn, 1 of your opponent's Digimon doesn't unsuspend.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var foe = Put(state.DummyOpponent, Digimon("B38-FOE", 5));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.True(foe.IsSuspended);
        Assert.True(foe.CannotUnsuspendUntil >= state.TurnSequence + 1);
    }

    [Fact]
    public void ThisDigimonWithNameMayUnsuspend_OnlyWhenNameMatches_BT17_013()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B38-BT17013", 4, inherited: "[On Play] You may unsuspend this Digimon with [Gallantmon] in its name.");
        AssertFull(src);
        var other = Put(state.Player, Digimon("B38-NOTG", 5), true, src);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, other, other.TopCard);
        Assert.True(other.IsSuspended);
        var gall = Put(state.Player, Digimon("B38-GALL", 6, name: "Gallantmon"), true, src);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, gall, gall.TopCard);
        Assert.False(gall.IsSuspended);
    }

    [Fact]
    public void DeleteOneTamer_PrefersOpponents_BT10_069()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT10069", 6, "[On Play] Delete 1 Tamer, and unsuspend this Digimon.");
        AssertFull(card);
        var me = Put(state.Player, card, suspended: true);
        var mine = Tamer("B38-MYT"); var theirs = Tamer("B38-OT");
        state.Player.Tamers.Add(mine);
        state.DummyOpponent.Tamers.Add(theirs);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(mine, state.Player.Tamers);
        Assert.Contains(theirs, state.DummyOpponent.Trash);
        Assert.False(me.IsSuspended);
    }

    // =====================================================================
    // 3. 비용
    // =====================================================================

    [Fact]
    public void TrashFromHandOrDigivolutionCards_PrefersSource_EX11_038()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-EX11038", 5, "[On Play] By trashing 1 [Mineral] or [Rock] trait card from your hand or your Digimon's digivolution cards, ＜Draw 1＞ (Draw 1 card from your deck.)");
        AssertFull(card);
        var rockSrc = Digimon("B38-ROCK", 3, traits: "Rock");
        var host = Put(state.Player, Digimon("B38-HOST", 5), false, rockSrc);
        var rockHand = Digimon("B38-ROCKH", 4, traits: "Mineral");
        state.Player.Hand.Add(rockHand);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Empty(host.EvolutionSources);   // 추천: 진화원 (패를 지킨다)
        Assert.Contains(rockSrc, state.Player.Trash);
        Assert.Equal(2, state.Player.Hand.Count);
    }

    [Fact]
    public void TrashOptionFromLinkCards_Unsuspends_BT25_085()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT25085", 6, "[When Attacking] By trashing 1 Option card from any of your Digimon's digivolution cards or link cards, this Digimon unsuspends.");
        AssertFull(card);
        var me = Put(state.Player, card, suspended: true);
        var opt = Card("B38-LINKOPT", "[Main] Gain 1 memory.");
        me.LinkedCards.Add(opt);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Empty(me.LinkedCards);
        Assert.Contains(opt, state.Player.Trash);
        Assert.False(me.IsSuspended);
    }

    [Fact]
    public void ReturnFromSourcesOrTrashToDeckBottom_EX6_054()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-EX6054", 6, "[On Play] By returning 1 [Lucemon] from this Digimon's digivolution cards or from your trash to the bottom of the deck, gain 2 memory.");
        AssertFull(card);
        var luce = Digimon("B38-LUCE", 3, name: "Lucemon");
        state.Player.Trash.Add(luce);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Same(luce, state.Player.MainDeck[^1]);
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void DeleteTwoOfYourDigimon_Cost_EX10_011()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-EX10011", 6, "[On Play] By deleting 2 of your level 5 or higher Digimon with [Myotismon] in their texts, gain 3 memory.");
        AssertFull(card);
        var a = Digimon("B38-MY5", 5, "[On Play] Search for [Myotismon]."); var b = Digimon("B38-MY6", 6, "[On Play] Search for [Myotismon].");
        var low = Digimon("B38-MY4", 4, "[On Play] Search for [Myotismon].");
        state.MemoryGauge = 5;
        var other = Put(state.Player, Digimon("B38-ELSE", 3));
        Put(state.Player, low);   // Lv.4 — 조건에 맞지 않는다
        Put(state.Player, a); Put(state.Player, b);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, other, card);   // 효과를 가진 디지몬이 따로 있다고 보고 (이 카드 자신은 [Myotismon]이 적혀 있지 않은 디지몬)
        Assert.Equal(8, state.MemoryGauge);   // 5 + 3 (메모리 상한 10)
        Assert.False(Has(state.Player, a));
        Assert.False(Has(state.Player, b));
    }

    [Fact]
    public void ReturnTopCardToHand_Cost_BT13_107()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT13107", 6, "[On Play] By returning the top card of one of your [Leopardmon: Leopard Mode] to hand, unsuspend all of your Digimon.");
        AssertFull(card);
        var leoTop = Digimon("B38-LEO", 7, name: "Leopardmon: Leopard Mode");
        var under = Digimon("B38-UNDER", 6);
        var leo = Put(state.Player, leoTop, true, under);
        var me = Put(state.Player, card, true);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(leoTop, state.Player.Hand);
        Assert.Same(under, leo.TopCard);
        Assert.False(me.IsSuspended);
        Assert.False(leo.IsSuspended);
    }

    [Fact]
    public void PlaceFromHandOrTrashAsTopOrBottomSources_AD1_007()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-AD1007", 6, "[When Attacking] By placing 2 Digimon cards with [Gammamon] in their texts from your hand or trash as this Digimon's top or bottom digivolution cards, gain 1 memory.");
        AssertFull(card);
        var keep = Digimon("B38-KEEP", 3);
        var me = Put(state.Player, card, false, keep);
        var g1 = Digimon("B38-G1", 3, "[On Play] [Gammamon]"); var g2 = Digimon("B38-G2", 4, "[On Play] [Gammamon]");
        state.Player.Trash.Add(g1); state.Player.Trash.Add(g2);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Equal(3, me.EvolutionSources.Count);
        Assert.Same(keep, me.EvolutionSources.Peek());   // 추천: 아래 — 바로 아래 진화원은 그대로
        Assert.Equal(9, state.MemoryGauge);
    }

    // =====================================================================
    // 4. 키워드 부여 · 어택
    // =====================================================================

    [Fact]
    public void GrantedGuard_ProtectsOtherFromOpponentsBounce_EX12_072()
    {
        var (state, engine) = NewMainPhase();
        var aura = Digimon("B38-AURA", 5, "[All Turns] All of your [ME] trait Digimon gain ＜Guard＞ (When any of your other Digimon would leave the battle area by your opponent's effects, by deleting this Digimon, they don't leave.)");
        AssertFull(aura);
        var guard = Digimon("B38-ME", 3, traits: "ME");
        Put(state.Player, aura);
        var g = Put(state.Player, guard);
        var vip = Digimon("B38-VIP", 6, dp: 12000);
        var v = Put(state.Player, vip);
        engine.RecalculateFieldBuffs();
        Assert.Contains(Keyword.Guard, g.ActiveKeywords);
        var bouncer = Digimon("B38-BOUNCE", 5, "[On Play] Return 1 of your opponent's Digimon with 12000 DP or more to the hand.");
        var b = Put(state.DummyOpponent, bouncer);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, b, bouncer);
        Assert.Contains(v, state.Player.BattleArea);
        Assert.False(Has(state.Player, guard));
    }

    [Fact]
    public void GrantedDecoyBlack_ProtectsBlackDigimonFromDeletion_BT8_060()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT8060", 4, "[All Turns] While this Digimon has the [X Antibody] trait, it gains ＜Decoy (Black)＞ (When your other Black Digimon would be deleted by an opponent's effect, you may delete this Digimon to prevent 1 of those Digimon's deletion.)", color: CardColor.Black, traits: "X Antibody");
        AssertFull(card);
        var decoy = Put(state.Player, card);
        var blackVip = Put(state.Player, Digimon("B38-BLK", 6, dp: 11000, color: CardColor.Black));
        engine.RecalculateFieldBuffs();
        var killer = Digimon("B38-KILL", 5, "[On Play] Delete 1 of your opponent's Digimon with 11000 DP or more.");
        var k = Put(state.DummyOpponent, killer);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, k, killer);
        Assert.Contains(blackVip, state.Player.BattleArea);
        Assert.DoesNotContain(decoy, state.Player.BattleArea);
    }

    [Fact]
    public void GrantedAscension_PlacesTopCardOnSecurity_BT26_030()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT26030", 5, "[On Play] 1 of your [Iliad] trait Digimon gains ＜Execute＞ and ＜Ascension＞ for the turn.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var iliadTop = Digimon("B38-ILIAD", 6, traits: "Iliad");
        var iliad = Put(state.Player, iliadTop);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        engine.RecalculateFieldBuffs();
        Assert.Contains(Keyword.Ascension, iliad.ActiveKeywords);
        Assert.True(engine.DeleteFrom(state.Player, iliad));
        Assert.Same(iliadTop, state.Player.SecurityStack[0]);
    }

    [Fact]
    public void MayGainRushAndAttackPlayer_BT16_077()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT16077", 6, "[On Play] 1 of your Digimon may gain ＜Rush＞ (This Digimon can attack the turn it comes into play.) for the turn and attack a player.");
        AssertFull(card);
        var me = Put(state.Player, card);
        me.TurnPlayed = state.CurrentTurn;   // 이번 턴에 등장 — ≪속공≫으로 어택
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(me.TimedKeywords, k => k.Keyword == Keyword.Rush);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void PiercingAndCanAttackUnsuspended_Static_BT20_019()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT20019", 6, "[Your Turn] All of your Digimon with [Sistermon] in their names or the [Royal Knight] trait gain ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon and survives the battle, it performs any security checks it normally would.) and can also attack unsuspended Digimon.");
        AssertFull(card);
        Put(state.Player, card);
        var rk = Put(state.Player, Digimon("B38-RK", 6, traits: "Royal Knight"));
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(rk, Keyword.Piercing));
        Assert.True(CardScripts.HasKeyword(rk, Keyword.CanAttackUnsuspended));
    }

    // =====================================================================
    // 5. 시큐리티
    // =====================================================================

    [Fact]
    public void TrashTopOrBottomOfOpponentsSecurity_BT16_056()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT16056", 5, "[On Play] Trash the top or bottom card of their security stack.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var top = state.DummyOpponent.SecurityStack[0];
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.Contains(top, state.DummyOpponent.Trash);   // 추천: 상대는 위
        Assert.Equal("【등장 시】 상대의 시큐리티를 위 또는 아래에서부터 1장 파기한다.", KoreanEffectRenderer.RenderClause("[On Play] Trash the top or bottom card of their security stack."));
    }

    [Fact]
    public void PlaceTopCardOfOpponentDigimonOnTheirSecurity_BT16_056()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B38-BT16056B", 5, "[On Play] You may place the top card of 1 of your opponent's [Vaccine] trait Digimon on top of their security stack.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var vacTop = Digimon("B38-VAC", 5, traits: "Vaccine");
        var vacUnder = Digimon("B38-VACU", 4, traits: "Vaccine");
        var foe = Put(state.DummyOpponent, vacTop, false, vacUnder);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Same(vacTop, state.DummyOpponent.SecurityStack[0]);
        Assert.Same(vacUnder, foe.TopCard);
    }

    [Fact]
    public void FaceUpSecurityDigimon_PlaysAtEndOfOpponentsTurn_BT20_052()
    {
        var (state, engine) = NewMainPhase();
        var sec = Digimon("B38-BT20052", 4, "[Security] [End of Opponent's Turn] Play this card without paying the cost.");
        AssertFull(sec);
        var down = Digimon("B38-BT20052D", 4, "[Security] [End of Opponent's Turn] Play this card without paying the cost.", name: "Face-down copy");
        state.DummyOpponent.SecurityStack.Insert(0, down);   // 뒷면이면 발휘하지 않는다 (종합 룰 15-14-5-1)
        Assert.True(CardScripts.For(sec)!.SecurityPlayAtEndOfOpponentTurn38);
        engine.PlaceSecurityFaceUp(state.DummyOpponent, sec, top: true);
        Assert.True(GoldfishEngine.IsFaceUp(state.DummyOpponent, state.DummyOpponent.SecurityStack[0]));   // 앞면은 복사한 카드
        state.MemoryGauge = -1;
        engine.CheckTurnEndAndAutoReset();   // 플레이어의 턴 종료 = 상대(DummyOpponent)에게는 상대의 턴 종료 시
        Assert.True(state.DummyOpponent.BattleArea.Any(d => d.TopCard.CardId == sec.CardId), L(state) + " SEC=" + string.Join(",", state.DummyOpponent.SecurityStack.Select(x => x.CardId)) + " BA=" + string.Join(",", state.DummyOpponent.BattleArea.Select(x => x.TopCard.CardId)));
        Assert.DoesNotContain(state.DummyOpponent.BattleArea, d => ReferenceEquals(d.TopCard, down));
        Assert.Equal("[시큐리티]【상대의 턴 종료 시】 이 카드를 코스트를 지불하지 않고 등장시킨다.", KoreanEffectRenderer.RenderClause("[Security] [End of Opponent's Turn] Play this card without paying the cost."));
    }

    // =====================================================================
    // 6. 지원 범위 · 한글
    // =====================================================================

    [Theory]
    [InlineData("[On Play] Delete 1 of your opponent's Digimon with level equal to or less than the deleted Digimon.")]
    [InlineData("[On Play] You may play 1 level 5 or lower [Eosmon] from your hand without paying its memory cost. Then, delete 1 of your opponent's Digimon with DP less than or equal to the Digimon played with this effect.")]
    [InlineData("[When Digivolving] You may place 1 Digimon card with [Gammamon] in its text from your trash as this Digimon's bottom digivolution card. Then, you may delete 1 Digimon whose level is less than or equal to the number of cards in this Digimon's digivolution cards.")]
    [InlineData("[When Attacking] By returning 1 Digimon card from your trash to the bottom of the deck, delete 1 of your opponent's Digimon with the same level as that card.")]
    [InlineData("[Main] 1 of your opponent's Digimon gets -3000 DP for the turn. Then, 1 of your [Rasenmon] gains ＜Security A. +1＞ for the turn.")]
    public void NewSentences_AreSupported_WithKorean(string text)
    {
        var card = Digimon("B38-SUP" + text.Length, 5, text);
        AssertFull(card);
        Assert.NotNull(KoreanEffectRenderer.RenderClause(text));
    }

    [Fact]
    public void Korean_Conjunction_ForSplitSentences()
    {
        Assert.Equal("【진화 시】 이 디지몬을 액티브로 하고, 자신의 트래시의 특징으로 「아머체」를 가진 카드 1장마다 다음 상대의 턴 종료 시까지, 이 디지몬의 DP를 +2000 한다.",
            KoreanEffectRenderer.RenderClause("[When Digivolving] Unsuspend this Digimon, and it gets +2000 DP until the end of your opponent's next turn for each card with the [Armor Form] trait in your trash."));
        Assert.Equal("【진화 시】 상대의 디지몬 1마리를 레스트시킨다. 상대의 턴 종료 시까지, 상대의 디지몬 1마리는 액티브가 되지 않는다.",
            KoreanEffectRenderer.RenderClause("[When Digivolving] Suspend 1 of your opponent's Digimon. Until the end of your opponent's turn, 1 of your opponent's Digimon doesn't unsuspend."));
    }
}
