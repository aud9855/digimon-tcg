using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.31.0 미지원 문장 5차 묶음: 다른 카드의 등장 코스트 감소 (필드 · 테이머 · [육성]) · 진화 코스트 감소의 셈 · 주어 · 【카운터】(배틀 에어리어) ·
/// [트래시] 시점 효과 · 테이머 시점 (레스트 · 액티브 · 소멸 · 아래 카드 파기) · 링크 카드 파기 · 패 · 덱 파기 조건 · 어택 주어 · 상대의 진화 ·
/// 합계로 고르는 소멸 · "이 디지몬의 【진화 시】 효과 1개를 발휘" · 진화원 카드의 효과 전부를 얻는다 · 테이머의 [턴에 1회]
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch31Tests
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

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null) =>
        Card(id, main, CardType.Tamer, color: color, name: name);

    private static CardData Option(string id, string main, int cost = 0) => Card(id, main, CardType.Option, playCost: cost);

    /// <summary>메인 페이즈 (메모리 8, 테이머 없음) — 덱 · 시큐리티 5장씩</summary>
    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B31-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B31-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B31-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B31-OSEC" + i, 3));
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
        // 옵션의 색 조건 (블루) — 블루인 디지몬 · 테이머가 없으면 색 조건용 테이머를 둔다
        if (!state.Player.BattleArea.Any(d => d.TopCard.AllColors.Contains(CardColor.Blue)) && !state.Player.Tamers.Any(t => t.AllColors.Contains(CardColor.Blue)))
            state.Player.Tamers.Add(Tamer("B31-COLOR"));
        state.Player.Hand.Add(option);
        var result = engine.PlayCard(option);
        Assert.True(result.CanExecute, result.Reason);
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    private const string AdventureTamer = "[Your Turn] When any Digimon cards with the [ADVENTURE] trait would be played from your hand, by suspending this Tamer, reduce the play cost by 1.";

    // =====================================================================
    // 다른 카드의 등장 코스트 감소 (필드 · 테이머 · [육성])
    // =====================================================================

    [Fact]
    public void FieldPlayCost_TwoTamers_ReduceTheSamePlayTwice_QnaST20_12()
    {
        var (state, engine) = NewMainPhase();
        var t1 = Tamer("B31-ADV1", AdventureTamer);
        var t2 = Tamer("B31-ADV2", AdventureTamer);
        AssertFull(t1);
        state.Player.Tamers.Add(t1);
        state.Player.Tamers.Add(t2);
        var adv = Digimon("B31-ADVDIGI", 4, playCost: 5, traits: "ADVENTURE");
        state.Player.Hand.Add(adv);

        Assert.True(engine.PlayCard(adv).CanExecute);

        Assert.Equal(8 - 3, state.MemoryGauge);                 // 공식 Q&A ST20-12 · ST21-12: 등장 코스트 합계 -2
        Assert.Contains(t1, state.Player.SuspendedTamers);
        Assert.Contains(t2, state.Player.SuspendedTamers);
    }

    [Fact]
    public void FieldPlayCost_OnlyMatchingCards_AndNamedCardWithoutCost()
    {
        var (state, engine) = NewMainPhase();
        var adv = Tamer("B31-ADV3", AdventureTamer);
        state.Player.Tamers.Add(adv);
        var marine = Digimon("B31-MARINE-HOST", 4, "[Your Turn] When you would play [MarineAngemon] from your hand, reduce its play cost by 1.");
        AssertFull(marine);
        Put(state.Player, marine);
        var other = Digimon("B31-OTHER", 3, playCost: 3);
        state.Player.Hand.Add(other);
        Assert.True(engine.PlayCard(other).CanExecute);
        Assert.Equal(5, state.MemoryGauge);                      // 특징 · 이름이 맞지 않으면 그대로
        Assert.DoesNotContain(adv, state.Player.SuspendedTamers);

        var angel = Digimon("B31-MARINE", 3, playCost: 2, name: "MarineAngemon");
        state.Player.Hand.Add(angel);
        Assert.True(engine.PlayCard(angel).CanExecute);
        Assert.Equal(5 - 1, state.MemoryGauge);                  // 비용 없는 감소
    }

    [Fact]
    public void FieldPlayCost_Breeding_OptionalOncePerTurn()
    {
        var (state, engine) = NewMainPhase();
        var egg = Digimon("B31-EATER-EGG", 2, inherited: "[Breeding] [Your Turn] [Once Per Turn] When any of your Digimon cards with the [Eater] trait would be played, you may reduce the play costs by 1.");
        AssertFull(egg);
        var breeding = new CardInstance(Digimon("B31-BREED-TOP", 3), 1);
        breeding.AddBottomSource(egg);
        state.Player.BreedingArea = breeding;
        var e1 = Digimon("B31-EATER1", 3, playCost: 3, traits: "Eater");
        var e2 = Digimon("B31-EATER2", 3, playCost: 3, traits: "Eater");
        state.Player.Hand.Add(e1);
        state.Player.Hand.Add(e2);

        Assert.True(engine.PlayCard(e1).CanExecute);
        Assert.Equal(8 - 2, state.MemoryGauge);
        Assert.True(engine.PlayCard(e2).CanExecute);
        Assert.Equal(6 - 3, state.MemoryGauge);                  // [턴에 1회]
    }

    [Fact]
    public void FieldPlayCost_DeletingThisTamer_AlsoFiresTamerDeleted()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B31-ARUKE-T", "[Your Turn] When [Arukenimon] or [Mummymon] would be played from your hand, by deleting this Tamer, reduce the play cost by 3.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var watcher = Digimon("B31-TDEL-W", 4, "[All Turns] When one of your Tamers is deleted, gain 1 memory.");
        AssertFull(watcher);
        Put(state.Player, watcher);
        var aruke = Digimon("B31-ARUKE", 4, playCost: 5, name: "Arukenimon");
        state.Player.Hand.Add(aruke);

        Assert.True(engine.PlayCard(aruke).CanExecute);

        Assert.DoesNotContain(tamer, state.Player.Tamers);
        Assert.Contains(tamer, state.Player.Trash);
        Assert.Equal(8 - 2 + 1, state.MemoryGauge);
    }

    // =====================================================================
    // 진화 코스트 감소 (셈 · 주어 · "you would digivolve this Digimon into")
    // =====================================================================

    [Fact]
    public void EvoCost_ForEachGreenOrBlackTamer_BT11_059()
    {
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(Tamer("B31-TG", color: CardColor.Green));
        state.Player.Tamers.Add(Tamer("B31-TB", color: CardColor.Black));
        state.Player.Tamers.Add(Tamer("B31-TU", color: CardColor.Blue));
        var baseD = Put(state.Player, Digimon("B31-EVBASE", 4));
        var evo = Digimon("B31-EVO", 5, "When one of your Digimon would digivolve into this card, reduce the digivolution cost by 1 for each green or black Tamer you have in play.", evoCost: 4);
        AssertFull(evo);
        state.Player.Hand.Add(evo);

        Assert.True(engine.EvolveDigimon(baseD, evo).CanExecute);
        Assert.Equal(8 - 2, state.MemoryGauge);
    }

    [Fact]
    public void EvoCost_DigimonWithNameInSources_BT10_086()
    {
        var (state, engine) = NewMainPhase();
        var evoText = "When a Digimon with [X Antibody] in its digivolution cards would digivolve into this card, reduce the digivolution cost by 2.";
        var withX = Put(state.Player, Digimon("B31-XBASE", 4), sources: Digimon("B31-XSRC", 3, name: "X Antibody"));
        var evo = Digimon("B31-XEVO", 5, evoText, evoCost: 3);
        AssertFull(evo);
        state.Player.Hand.Add(evo);
        Assert.True(engine.EvolveDigimon(withX, evo).CanExecute);
        Assert.Equal(8 - 1, state.MemoryGauge);

        var plain = Put(state.Player, Digimon("B31-PBASE", 4));
        var evo2 = Digimon("B31-XEVO2", 5, evoText, evoCost: 3);
        state.Player.Hand.Add(evo2);
        Assert.True(engine.EvolveDigimon(plain, evo2).CanExecute);
        Assert.Equal(7 - 3, state.MemoryGauge);                  // 진화원에 「X항체」가 없으면 그대로
    }

    [Fact]
    public void EvoCost_YouWouldDigivolveThisDigimonInto_InheritedP029()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B31-P029", 3, inherited: "[Your Turn] When you would digivolve this Digimon into an [AncientGreymon] in your hand, reduce its digivolution cost by 2.");
        AssertFull(src);
        var baseD = Put(state.Player, Digimon("B31-AGBASE", 5), sources: src);
        var ancient = Digimon("B31-AG", 6, name: "AncientGreymon", evoCost: 4);
        state.Player.Hand.Add(ancient);

        Assert.True(engine.EvolveDigimon(baseD, ancient).CanExecute);
        Assert.Equal(8 - 2, state.MemoryGauge);
    }

    // =====================================================================
    // 【카운터】 (배틀 에어리어의 디지몬 — 종합 룰 11-3)
    // =====================================================================

    [Fact]
    public void Counter_FieldDigimonEffect_ActivatesWhenAttacked_OncePerAttackAndTurn()
    {
        var (state, engine) = NewMainPhase();
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        var counter = Digimon("B31-COUNTER", 6, "[Counter] [Once Per Turn] Delete 1 of your opponent's Digimon with 7000 DP or less.", dp: 9000);
        AssertFull(counter);
        Put(state.DummyOpponent, counter);
        var a1 = Put(state.Player, Digimon("B31-ATK1", 5, dp: 5000));
        var a2 = Put(state.Player, Digimon("B31-ATK2", 5, dp: 5000));
        int security = state.DummyOpponent.SecurityStack.Count;

        engine.AttackSecurityOrDirect(a1);
        Assert.DoesNotContain(a1, state.Player.BattleArea);       // 【카운터】로 어택한 디지몬이 소멸
        Assert.Equal(security, state.DummyOpponent.SecurityStack.Count);

        engine.AttackSecurityOrDirect(a2);
        Assert.Contains(a2, state.Player.BattleArea);              // [턴에 1회]
        Assert.Equal(security - 1, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Counter_OnNonDigimon_IsNotRead()
    {
        var option = Option("B31-COUNTER-OPT", "[Counter] Gain 1 memory.");
        Assert.NotEqual(EffectSupportLevel.Full, EffectSupport.For(option).Level);
    }

    // =====================================================================
    // [트래시] 시점 효과 · 카드명 안의 괄호
    // =====================================================================

    private const string XAntibodyTrash = "[Main] Gain 3 memory.\n[Trash] [Your Turn] When any of your Digimon digivolve into [Belphemon (X Antibody)], by returning this card to the bottom of the deck, activate this card's [Main] effects.";

    [Fact]
    public void TrashZone_WhenDigivolvingIntoXAntibody_ReturnsThisCardAndActivatesMain()
    {
        var (state, engine) = NewMainPhase();
        var opt = Option("B31-XOPT", XAntibodyTrash);
        AssertFull(opt);
        state.Player.Trash.Add(opt);
        var baseD = Put(state.Player, Digimon("B31-BELBASE", 5));
        var belX = Digimon("B31-BELX", 6, name: "Belphemon (X Antibody)", evoCost: 1);
        state.Player.Hand.Add(belX);

        Assert.True(engine.EvolveDigimon(baseD, belX).CanExecute);

        Assert.Equal(8 - 1 + 3, state.MemoryGauge);
        Assert.DoesNotContain(opt, state.Player.Trash);
        Assert.Same(opt, state.Player.MainDeck[^1]);             // 덱 맨 아래
    }

    [Fact]
    public void TrashZone_PlainNameWithoutXAntibody_DoesNotTrigger()
    {
        var (state, engine) = NewMainPhase();
        var opt = Option("B31-XOPT2", XAntibodyTrash);
        state.Player.Trash.Add(opt);
        var baseD = Put(state.Player, Digimon("B31-BELBASE2", 5));
        var bel = Digimon("B31-BEL", 6, name: "Belphemon", evoCost: 1);
        state.Player.Hand.Add(bel);

        Assert.True(engine.EvolveDigimon(baseD, bel).CanExecute);

        Assert.Equal(8 - 1, state.MemoryGauge);                  // "[Belphemon (X Antibody)]"는 「벨페몬」이 아니다
        Assert.Contains(opt, state.Player.Trash);
    }

    // =====================================================================
    // 테이머 시점
    // =====================================================================

    [Fact]
    public void Tamer_ThisTamerBecomesSuspended_OnlyThatTamer()
    {
        var (state, engine) = NewMainPhase();
        var me = Tamer("B31-TSUS", "[Your Turn] When this Tamer becomes suspended, gain 1 memory.");
        var other = Tamer("B31-TSUS-OTHER");
        AssertFull(me);
        state.Player.Tamers.Add(me);
        state.Player.Tamers.Add(other);

        engine.SuspendTamer(state.Player, other);
        Assert.Equal(8, state.MemoryGauge);
        engine.SuspendTamer(state.Player, me);
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void Tamer_BecomesUnsuspendedInUnsuspendPhase()
    {
        var (state, engine) = NewMainPhase();
        var me = Tamer("B31-TUNS", "[Your Turn] When this Tamer becomes unsuspended, gain 1 memory.");
        AssertFull(me);
        state.Player.Tamers.Add(me);
        state.Player.SuspendedTamers.Add(me);
        int before = state.MemoryGauge;

        engine.ExecuteActivePhase();

        Assert.Empty(state.Player.SuspendedTamers);
        Assert.Equal(before + 1, state.MemoryGauge);
    }

    [Fact]
    public void Tamer_Deleted_ColorFilter_BT13_015()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Digimon("B31-TDEL", 5, "[All Turns] [Once Per Turn] When one of your red or yellow Tamers is deleted, gain 2 memory.");
        AssertFull(watcher);
        Put(state.Player, watcher);
        var blue = Tamer("B31-TBLUE", color: CardColor.Blue);
        var red = Tamer("B31-TRED", color: CardColor.Red);
        state.Player.Tamers.Add(blue);
        state.Player.Tamers.Add(red);

        engine.DeleteTamer(state.Player, blue);
        Assert.Equal(8, state.MemoryGauge);
        engine.DeleteTamer(state.Player, red);
        Assert.Equal(10, state.MemoryGauge);
        Assert.Contains(red, state.Player.Trash);
    }

    [Fact]
    public void Tamer_OpponentEffectDeletesTamer_FiresOtherDigimonOrTamersDeleted()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Digimon("B31-OTHERDEL", 6, "[All Turns] [Once Per Turn] When other Digimon or Tamers are deleted, trash your opponent's top security card.");
        AssertFull(watcher);
        Put(state.Player, watcher);
        var oppTamer = Tamer("B31-OPP-TAMER");
        state.DummyOpponent.Tamers.Add(oppTamer);
        Use(state, engine, Option("B31-DELTAMER", "[Main] Delete 1 of your opponent's Tamers."));

        Assert.DoesNotContain(oppTamer, state.DummyOpponent.Tamers);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Tamer_EffectsTrashCardsFromUnderTamers_AndCompoundEvent()
    {
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(Tamer("B31-UNDER-T"));
        state.Player.UnderTamers.Add(Digimon("B31-UNDER1", 3));
        state.Player.UnderTamers.Add(Digimon("B31-UNDER2", 3));
        var a = Digimon("B31-UNDER-W", 4, "[All Turns] When effects trash cards from under your Tamers, gain 1 memory.");
        var b = Digimon("B31-UNDER-OR", 4, "[All Turns] When attack targets change or effects trash cards from under your Tamers, gain 1 memory.");
        AssertFull(a);
        AssertFull(b);
        Put(state.Player, a);
        Put(state.Player, b);

        state.MemoryGauge = 3;   // 메모리 상한 10
        Use(state, engine, Option("B31-UNDER-OPT", "[Main] By trashing the bottom card from under any of your Tamers, gain 1 memory."));

        Assert.Single(state.Player.UnderTamers);
        Assert.Equal(3 + 1 + 1 + 1, state.MemoryGauge);
    }

    [Fact]
    public void Tamer_OncePerTurn_IsNowLimitedForTamers()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B31-OPT-TAMER", "[Your Turn] [Once Per Turn] When one of your Digimon is played, gain 1 memory.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var d1 = Digimon("B31-OPT-D1", 3, playCost: 1);
        var d2 = Digimon("B31-OPT-D2", 3, playCost: 1);
        state.Player.Hand.Add(d1);
        state.Player.Hand.Add(d2);

        engine.PlayCard(d1);
        engine.PlayCard(d2);

        Assert.Equal(8 - 1 + 1 - 1, state.MemoryGauge);         // 두 번째 등장에는 발휘하지 않는다
    }

    // =====================================================================
    // 링크 카드 · 패 · 덱
    // =====================================================================

    [Fact]
    public void Link_EffectsTrashThisDigimonsLinkCards()
    {
        var (state, engine) = NewMainPhase();
        var host = Digimon("B31-LINK-HOST", 5, "[All Turns] [Once Per Turn] When effects trash any of this Digimon's link cards, gain 1 memory.\n[Main] By trashing 1 of this Digimon's link cards, gain 1 memory.");
        AssertFull(host);
        var d = Put(state.Player, host);
        d.LinkedCards.Add(Digimon("B31-LINKED", 3));

        Assert.True(engine.ActivateMainAbility(d, host).CanExecute);

        Assert.Empty(d.LinkedCards);
        Assert.Equal(8 + 1 + 1, state.MemoryGauge);
    }

    [Fact]
    public void Hand_TrashedCardFilter_AndByYourEffect()
    {
        var (state, engine) = NewMainPhase();
        var filter = Digimon("B31-HAND-F", 4, "[Your Turn] When a Digimon card with the [Dark Animal] trait is trashed from your hand, gain 1 memory.");
        var mine = Digimon("B31-HAND-M", 4, "[All Turns] When a card is trashed from your hand by your effect, gain 2 memory.");
        AssertFull(filter);
        AssertFull(mine);
        Put(state.Player, filter);
        Put(state.Player, mine);
        state.Player.Hand.Add(Digimon("B31-PLAIN", 3));

        engine.DiscardFromHand(state.Player, 1);                 // 효과 밖 — 특징도 다르다
        Assert.Equal(8, state.MemoryGauge);

        state.MemoryGauge = 3;   // 메모리 상한 10
        state.Player.Hand.Add(Digimon("B31-DARK", 3, traits: "Dark Animal"));
        Use(state, engine, Option("B31-DISCARD", "[Main] Trash 1 card in your hand."));
        Assert.Equal(3 + 1 + 2, state.MemoryGauge);
    }

    [Fact]
    public void Hand_YouTrashThisCardUsingYourEffect()
    {
        var (state, engine) = NewMainPhase();
        var self = Option("B31-SELFTRASH", "When you trash this card in your hand using one of your effects, gain 1 memory.\n[Main] Gain 1 memory.");
        AssertFull(self);
        state.Player.Hand.Add(self);

        Use(state, engine, Option("B31-DISCARD2", "[Main] Trash 1 card in your hand."));

        Assert.Contains(self, state.Player.Trash);
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void Deck_OpponentsDeckTrashed()
    {
        var (state, engine) = NewMainPhase();
        var d = Digimon("B31-DECK", 4, "[Your Turn] [Once Per Turn] When a card in your opponent's deck is trashed, gain 1 memory.");
        AssertFull(d);
        Put(state.Player, d);

        engine.Mill(state.Player, 1, "test");
        Assert.Equal(8, state.MemoryGauge);                      // 자신의 덱은 아니다
        engine.Mill(state.DummyOpponent, 2, "test");
        Assert.Equal(9, state.MemoryGauge);
    }

    // =====================================================================
    // 어택 · 진화 · 등장 · 소멸
    // =====================================================================

    [Theory]
    [InlineData("Angoramon", 4, true)]
    [InlineData("B31-LV5", 5, true)]
    [InlineData("B31-LV4", 4, false)]
    public void Attack_YouAttackWithNameOrLevel_BT10_091(string name, int level, bool fires)
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B31-ATK-T", "[Your Turn] When you attack with a Digimon that has [Angoramon] in its name or is level 5 or higher, you may suspend this Tamer to gain 1 memory.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var attacker = Put(state.Player, Digimon("B31-ATK-" + name, level, name: name));

        engine.AttackSecurityOrDirect(attacker);

        Assert.Equal(fires ? 9 : 8, state.MemoryGauge);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Attack_DigimonWithNameInDigivolutionCards_P062(bool hasSource, bool fires)
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B31-P062", "[Your Turn] When one of your Digimon with [Gammamon] in its digivolution cards attacks, by suspending this Tamer, that Digimon gets +3000 DP for the turn.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var attacker = hasSource ? Put(state.Player, Digimon("B31-GAMMA-HOST", 5, dp: 5000), sources: Digimon("B31-GAMMA", 3, name: "Gammamon"))
                                 : Put(state.Player, Digimon("B31-NOGAMMA", 5, dp: 5000));

        engine.AttackSecurityOrDirect(attacker);

        Assert.Equal(fires, state.Player.SuspendedTamers.Contains(tamer));
        Assert.Equal(fires ? 8000 : 5000, attacker.GetCurrentDP());
    }

    [Fact]
    public void Attack_TwoOrMoreColors_BT8_085()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B31-2COL", "[Your Turn] When one of your Digimon with 2 or more colors attacks, you may suspend this Tamer to delete 1 of your opponent's Digimon with 3000 DP or less.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var victim = Put(state.DummyOpponent, Digimon("B31-VICTIM", 3, dp: 3000));
        var attacker = Put(state.Player, Digimon("B31-MULTI", 5, color: CardColor.Red, second: CardColor.Blue));

        engine.AttackSecurityOrDirect(attacker);

        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void Digivolve_OpponentDigimonDigivolvesIntoLevel6()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Digimon("B31-OPPEVO", 5, "[All Turns] [Once Per Turn] When an opponent's Digimon digivolves into a level 6 or higher Digimon, gain 1 memory.");
        AssertFull(watcher);
        Put(state.Player, watcher);
        var opp = Put(state.DummyOpponent, Digimon("B31-OPP5", 5));
        var lv6 = Digimon("B31-OPP6", 6);
        state.DummyOpponent.Hand.Add(lv6);

        engine.EvolveByEffect(state.DummyOpponent, opp, lv6, 0);

        Assert.Same(lv6, opp.TopCard);
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void PlayedOrDigivolve_AnyDigimon_BothSides()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Digimon("B31-ANYPD", 5, "[All Turns] When any Digimon are played or digivolve, gain 1 memory.");
        AssertFull(watcher);
        Put(state.Player, watcher);

        engine.PlayWithoutCost(state.DummyOpponent, Digimon("B31-OPPPLAY", 3));
        Assert.Equal(9, state.MemoryGauge);
        var mine = Digimon("B31-MYPLAY", 3, playCost: 0);
        state.Player.Hand.Add(mine);
        engine.PlayCard(mine);
        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void Deleted_OpponentDigimonByZeroDp_NotByDeleteEffect()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Digimon("B31-ZERO", 5, "[All Turns] When an opponent's Digimon is deleted in battle or by having 0 DP, gain 2 memory.");
        AssertFull(watcher);
        Put(state.Player, watcher);
        state.MemoryGauge = 3;   // 메모리 상한 10
        Put(state.DummyOpponent, Digimon("B31-OPPA", 3, dp: 3000));

        Use(state, engine, Option("B31-MINUS", "[Main] 1 of your opponent's Digimon gets -5000 DP for the turn."));   // 3000 DP 디지몬은 DP 0
        Assert.Equal(5, state.MemoryGauge);
        Put(state.DummyOpponent, Digimon("B31-OPPB", 3, dp: 9000));
        Use(state, engine, Option("B31-KILL", "[Main] Delete 1 of your opponent's Digimon."));
        Assert.Empty(state.DummyOpponent.BattleArea);
        Assert.Equal(5, state.MemoryGauge);                      // 효과로 소멸은 아니다
    }

    // =====================================================================
    // 합계로 고르기 · 【진화 시】 효과 발휘 · 양쪽 드로우 · 테이머 아래
    // =====================================================================

    [Fact]
    public void TotalWorth_TotalDpUpTo6000_DeletesWithinBudget_ST7_12()
    {
        var (state, engine) = NewMainPhase();
        var d5 = Put(state.DummyOpponent, Digimon("B31-T5", 5, dp: 5000));
        var d4 = Put(state.DummyOpponent, Digimon("B31-T4", 4, dp: 4000));
        var d1 = Put(state.DummyOpponent, Digimon("B31-T1", 3, dp: 1000));

        Use(state, engine, Option("B31-TOTAL", "[Main] Choose any number of your opponent's Digimon whose total DP adds up to 6000 or less and delete them."));

        Assert.DoesNotContain(d5, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(d1, state.DummyOpponent.BattleArea);
        Assert.Contains(d4, state.DummyOpponent.BattleArea);     // 합계 6000을 넘는다
    }

    [Fact]
    public void TotalWorth_PlayCostUpTo7_AndMaximumIncreases()
    {
        var (state, engine) = NewMainPhase();
        var p5 = Put(state.DummyOpponent, Digimon("B31-PC5", 5, playCost: 5));
        var p3 = Put(state.DummyOpponent, Digimon("B31-PC3", 4, playCost: 3));
        var p2 = Put(state.DummyOpponent, Digimon("B31-PC2", 3, playCost: 2));
        Use(state, engine, Option("B31-PCTOTAL", "[Main] Delete up to 7 play cost's total worth of your opponent's Digimon."));
        Assert.Contains(p3, state.DummyOpponent.BattleArea);
        Assert.Single(state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(p5, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(p2, state.DummyOpponent.BattleArea);

        // "For each of your Tamers, add 2000 to the maximum you can choose with this effect" — 4000 + 2000
        state.Player.Tamers.Add(Tamer("B31-MAXT"));
        var a = Put(state.DummyOpponent, Digimon("B31-DPA", 5, dp: 5000));
        var b = Put(state.DummyOpponent, Digimon("B31-DPB", 3, dp: 1000));
        Use(state, engine, Option("B31-DPMAX", "[Main] Delete up to 4000 DP total worth of your opponent's Digimon. For each of your Tamers, add 2000 to the maximum you can choose with this effect."));
        Assert.DoesNotContain(a, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(b, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void TotalWorth_ReturnToBottomOfDeck()
    {
        var (state, engine) = NewMainPhase();
        var p4 = Put(state.DummyOpponent, Digimon("B31-RT4", 5, playCost: 4));
        Use(state, engine, Option("B31-RETTOTAL", "[Main] Choose any number of your opponent's Digimon whose play costs add up to 6 or less and return them to the bottom of their owner's deck."));
        Assert.Empty(state.DummyOpponent.BattleArea);
        Assert.Same(p4.TopCard, state.DummyOpponent.MainDeck[^1]);
    }

    [Fact]
    public void ActivateWhenDigivolving_WhenOpponentPlaysDigimon_EX6_043()
    {
        var (state, engine) = NewMainPhase();
        var d = Digimon("B31-ACTWD", 6, "[When Digivolving] Gain 2 memory.\n[All Turns] [Once Per Turn] When an opponent's Digimon is played, you may activate 1 of this Digimon's [When Digivolving] effects.");
        AssertFull(d);
        Put(state.Player, d);

        engine.PlayWithoutCost(state.DummyOpponent, Digimon("B31-OPP-PLAYED", 3));

        Assert.Equal(10, state.MemoryGauge);
    }

    [Fact]
    public void BothPlayersDraw_AndPlaceTopCardsUnderThisTamer()
    {
        var (state, engine) = NewMainPhase();
        int mine = state.Player.Hand.Count, theirs = state.DummyOpponent.Hand.Count;
        Use(state, engine, Option("B31-BOTHDRAW", "[Main] Both players draw 1 card from their decks."));
        Assert.Equal(mine + 1, state.Player.Hand.Count);
        Assert.Equal(theirs + 1, state.DummyOpponent.Hand.Count);

        var tamer = Tamer("B31-UNDERTOP", "[Main] Place the top 2 cards of your deck face down under this Tamer.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        int deck = state.Player.MainDeck.Count;
        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);
        Assert.Equal(2, state.Player.UnderTamers.Count);
        Assert.Equal(deck - 2, state.Player.MainDeck.Count);
    }

    // =====================================================================
    // 진화원 카드의 효과 전부를 얻는다 (공식 Q&A BT16-062 · BT16-014)
    // =====================================================================

    [Fact]
    public void GainAllEffects_WhenDigivolvingOfGammamonSourceActivates_QnaBT16_062()
    {
        var (state, engine) = NewMainPhase();
        var gammaBase = Digimon("B31-WEZEN", 4, "[When Digivolving] Gain 2 memory.\n＜Blocker＞\n[Your Turn] This Digimon gets +1000 DP.", name: "WezenGammamon");
        AssertFull(gammaBase);
        var baseD = Put(state.Player, gammaBase);
        var top = Digimon("B31-GAINALL", 5, "[All Turns] This Digimon gains all effects of cards with [Gammamon] in their names in this Digimon's digivolution cards.", dp: 7000);
        AssertFull(top);
        state.Player.Hand.Add(top);

        Assert.True(engine.EvolveDigimon(baseD, top).CanExecute);

        Assert.Equal(8 - 1 + 2, state.MemoryGauge);              // 진화원 카드의 【진화 시】를 얻어 유발
        Assert.True(CardScripts.HasKeyword(baseD, Keyword.Blocker));
        Assert.Equal(8000, baseD.GetCurrentDP());                // 지속 효과도 얻는다
    }

    [Fact]
    public void GainAllEffects_OnlyMatchingSources()
    {
        var (state, engine) = NewMainPhase();
        var baseD = Put(state.Player, Digimon("B31-NOTGAMMA", 4, "＜Blocker＞", name: "Agumon"));
        var top = Digimon("B31-GAINALL2", 5, "[All Turns] This Digimon gains all effects of [Goldramon] in this Digimon's digivolution cards.");
        state.Player.Hand.Add(top);
        Assert.True(engine.EvolveDigimon(baseD, top).CanExecute);
        Assert.False(CardScripts.HasKeyword(baseD, Keyword.Blocker));
    }

    // =====================================================================
    // 생성 한글 (공식 표기)
    // =====================================================================

    [Theory]
    [InlineData("[All Turns] This Digimon gains all effects of cards with [Gammamon] in their names in this Digimon's digivolution cards.",
        "【서로의 턴】이 디지몬은 이 디지몬의 진화원에 있는 명칭에 「감마몬」을 포함하는 카드의 효과 전부를 얻는다.")]
    [InlineData("[All Turns] This Digimon gains all effects of [Machinedramon] and [Chaosdramon] cards in this Digimon's digivolution cards.",
        "【서로의 턴】이 디지몬은 이 디지몬의 진화원에 있는 「파워드라몬」과 「카오스드라몬」의 효과 전부를 얻는다.")]
    [InlineData("[Main] Choose any number of your opponent's Digimon whose total DP adds up to 8000 or less and delete them.",
        "【메인】 DP의 합계가 8000 이하가 되도록 상대의 디지몬을 골라, 고른 디지몬 전부를 소멸시킨다.")]
    [InlineData("[When Digivolving] Delete up to 7 play cost's total worth of your opponent's Digimon.",
        "【진화 시】 등장 코스트 합계 7까지 상대의 디지몬을 소멸시킨다.")]
    [InlineData("[All Turns] [Once Per Turn] When one of your red or yellow Tamers is deleted, place 1 [Marcus Damon] from your trash on top of your security stack face down.",
        "【서로의 턴】 [턴에 1회] 레드/옐로인 자신의 테이머가 소멸했을 때, 자신의 트래시에서 「최건우」인 카드 1장을 시큐리티 위에 뒷면으로 놓는다.")]
    [InlineData("[All Turns] [Once Per Turn] When effects trash any of this Digimon's link cards, gain 1 memory.",
        "【서로의 턴】 [턴에 1회] 이 디지몬의 링크 카드가 효과로 파기되었을 때, 메모리를 +1 한다.")]
    [InlineData("[Opponent's Turn] [Once Per Turn] When an opponent's Digimon is played, you may activate 1 of this Digimon's [When Digivolving] effects.",
        "【상대의 턴】 [턴에 1회] 상대의 디지몬이 등장했을 때, 이 디지몬의 【진화 시】 효과 1개를 발휘할 수 있다.")]
    [InlineData("[When Attacking] [Once Per Turn] Both players draw the top card of their decks.",
        "【어택 시】 [턴에 1회] 서로는 덱 위에서부터 카드를 1장 뽑는다.")]
    [InlineData("When one of your [Paildramon] or [Dinobeemon] would digivolve into this card in your hand, reduce the memory cost of the digivolution by 2.",
        "자신의 「파일드라몬」/「다이노몬」이 패의 이 카드로 진화할 때, 지불하는 진화 코스트를 -2 한다.")]
    public void KoreanRender_OfficialNotation(string en, string ko) =>
        Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));

    [Fact]
    public void KoreanRender_FieldPlayCost_TamerEvents_AndCompound()
    {
        Assert.Equal("【자신의 턴】 자신의 패에서 특징으로 「어드벤처」를 가진 디지몬 카드가 등장할 때, 이 테이머를 레스트시키는 것으로, 지불하는 등장 코스트를 -1 한다.",
            KoreanEffectRenderer.RenderClause(AdventureTamer));
        Assert.Equal("【자신의 턴】 이 테이머가 레스트했을 때, 메모리를 +1 한다.",
            KoreanEffectRenderer.RenderClause("[Your Turn] When this Tamer becomes suspended, gain 1 memory."));
        Assert.Equal("【서로의 턴】 [턴에 1회] 어택의 대상이 변경되거나, 효과로 자신의 테이머 아래의 카드가 파기되었을 때, 이 디지몬을 액티브로 할 수 있다.",
            KoreanEffectRenderer.RenderClause("[All Turns] [Once Per Turn] When attack targets change or effects trash cards from under your Tamers, this Digimon may unsuspend."));
        Assert.Equal("[트래시] 【자신의 턴】 자신의 디지몬이 「벨페몬 X항체」로 진화했을 때, 이 카드를 덱 아래로 되돌리는 것으로, 이 카드의 【메인】 효과를 발휘한다.",
            KoreanEffectRenderer.RenderClause("[Trash] [Your Turn] When any of your Digimon digivolve into [Belphemon (X Antibody)], by returning this card to the bottom of the deck, activate this card's [Main] effects."));
    }
}
