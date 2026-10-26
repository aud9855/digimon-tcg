using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.28.0 미지원 문장 2차 묶음: 진화원 시점(효과로 진화원에서 파기 · 진화원에 놓임) · ≪사용 조건≫ · 정보가 적힌 토큰 ·
/// 시큐리티 찾기 · 보기만 하는 공개 · 공개 조건의 다른 표기 · 고르는 진화원 파기 · 진화원 아래에 놓기 표기 · 조건의 "or"
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch28Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    /// <summary>메인 페이즈 (메모리 8) — tamerEffect가 있으면 그 효과의 블루 테이머를 놓는다</summary>
    private static (GameState State, GoldfishEngine Engine) NewMainPhase(string? tamerEffect = null, string tamerId = "B28-TAMER")
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 6; i++)
        {
            state.Player.MainDeck.Add(Card("B28-FILL" + i, type: CardType.Digimon, level: 3));
            state.DummyOpponent.MainDeck.Add(Card("B28-OFILL" + i, type: CardType.Digimon, level: 3));
        }
        var tamer = Card(tamerId + "-" + (tamerEffect?.GetHashCode() ?? 0), tamerEffect ?? "", CardType.Tamer);
        if (tamerEffect != null)
            Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(tamer).Level);
        state.Player.Tamers.Add(tamer);
        return (state, new GoldfishEngine(state));
    }

    private static void Play(GameState state, GoldfishEngine engine, CardData card)
    {
        if (card.EffectText.Length > 0)
            Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute);
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static CardData Source(string id, string inherited = "", params string[] traits) =>
        Card(id, type: CardType.Digimon, level: 3, inherited: inherited, traits: traits);

    // ---- 이 카드가 효과로 진화원에서 파기되었을 때 (공식 Q&A BT10-006 · BT2-085) ----

    [Fact]
    public void ThisCardTrashedFromDigivolutionCards_ByOwnEffect_Triggers()
    {
        var (state, engine) = NewMainPhase();
        var src = Source("B28-TRASHED-SRC", "When an effect trashes this card from digivolution cards, gain 2 memory.");
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(src).Level);
        Put(state.Player, Card("B28-HOST", type: CardType.Digimon, level: 4), false, src);

        // 자신의 효과로 파기해도 발휘한다 (Q&A BT10-006)
        Play(state, engine, Card("B28-TRASH-OWN", "[Main] Trash the bottom 1 digivolution card of 1 of your Digimon."));

        Assert.Contains(src, state.Player.Trash);
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }

    [Fact]
    public void ThisCardTrashedFromDigivolutionCards_NotWhenTheDigimonReturnsToHand()
    {
        var (state, engine) = NewMainPhase();
        var src = Source("B28-BOUNCE-SRC", "When an effect trashes this card from digivolution cards, gain 2 memory.");
        Put(state.DummyOpponent, Card("B28-BOUNCE-HOST", type: CardType.Digimon, level: 4), false, src);

        // 패로 되돌린 디지몬의 진화원이 트래시로 가는 것은 "진화원을 파기했다"가 아니다 (Q&A BT2-085 · P-004)
        Play(state, engine, Card("B28-BOUNCE", "[Main] Return 1 of your opponent's Digimon to the hand."));

        Assert.Contains(src, state.DummyOpponent.Trash);
        Assert.Equal(8 - 3, state.MemoryGauge);
    }

    [Theory]
    [InlineData("Mineral", true)]
    [InlineData("Dragon", false)]
    public void ThisCardTrashed_FromATraitDigimonsDigivolutionCards(string hostTrait, bool triggers)
    {
        var (state, engine) = NewMainPhase();
        var src = Source("B28-MINERAL-SRC-" + hostTrait, "When effects trash this card from a [Mineral] or [Rock] trait Digimon's digivolution cards, gain 2 memory.");
        Put(state.Player, Card("B28-MINERAL-HOST-" + hostTrait, type: CardType.Digimon, level: 4, traits: hostTrait), false, src);

        Play(state, engine, Card("B28-MINERAL-TRASH", "[Main] Trash the bottom 1 digivolution card of 1 of your Digimon."));

        Assert.Equal(8 - 3 + (triggers ? 2 : 0), state.MemoryGauge);
    }

    [Fact]
    public void WhenOneOfYourOpponentsDigivolutionCardsIsTrashed_Tamer()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When one of your opponent's digivolution cards is trashed, you may suspend this Tamer to gain 1 memory.");
        Put(state.DummyOpponent, Card("B28-OPP-HOST", type: CardType.Digimon, level: 4), false, Source("B28-OPP-SRC"));

        Play(state, engine, Card("B28-OPP-TRASH", "[Main] Trash the bottom 1 digivolution card of 1 of your opponent's Digimon."));

        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
        Assert.NotEmpty(state.Player.SuspendedTamers);
    }

    // ---- 효과로 진화원에 카드가 놓였을 때 (공식 Q&A BT7-005 · BT17-003) ----

    [Fact]
    public void WhenAnEffectPlacesADigivolutionCardUnderThisDigimon()
    {
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Card("B28-PLACE-HOST", "[Your Turn] [Once Per Turn] When an effect places a digivolution card under this Digimon, gain 2 memory.", CardType.Digimon, level: 4));
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(host.TopCard).Level);
        state.Player.Trash.Add(Card("B28-PLACE-CARD", type: CardType.Digimon, level: 3));

        Play(state, engine, Card("B28-PLACE-OPT", "[Main] Place 1 Digimon card from your trash under 1 of your Digimon as its bottom digivolution card."));

        Assert.Single(host.EvolutionSources);
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }

    [Fact]
    public void Digivolving_IsNotPlacingADigivolutionCardByAnEffect()
    {
        // 진화(효과로 진화해도)로 겹친 카드는 "효과로 진화원이 늘어났을 때"가 아니다 (Q&A BT7-005)
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Card("B28-EVO-HOST", type: CardType.Digimon, level: 3,
            inherited: "[Your Turn] When an effect places a digivolution card under this Digimon, gain 2 memory."));
        var evo = Card("B28-EVO-TO", type: CardType.Digimon, level: 4);
        state.Player.Hand.Add(evo);
        state.Player.Hand.Add(Card("B28-EVO-OPT", "[Main] 1 of your Digimon may digivolve into a level 4 Digimon card in your hand without paying the cost."));

        Play(state, engine, state.Player.Hand.Last());

        Assert.Same(evo, host.TopCard);
        Assert.Equal(8 - 3, state.MemoryGauge);
    }

    [Fact]
    public void WhenTamerCardsArePlacedInThisDigimonsDigivolutionCards_MindLink()
    {
        // ≪마인드 링크≫로 테이머 카드를 놓아도 발휘한다 (Q&A BT17-003)
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Card("B28-ML-HOST", "[All Turns] When Tamer cards are placed in this Digimon's digivolution cards, gain 2 memory.", CardType.Digimon, level: 5));
        var tamer = Card("B28-ML-TAMER", "[Main] ＜Mind Link＞ with 1 of your Digimon.", CardType.Tamer);
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(tamer).Level);
        state.Player.Tamers.Add(tamer);

        Assert.True(engine.ActivateMainAbility(null, tamer).CanExecute);

        Assert.Contains(tamer, host.EvolutionSources);
        Assert.Equal(8 + 2, state.MemoryGauge);
    }

    // ---- ≪사용 조건≫ (공식 공지 2026-10-16: 지정된 디지몬 · 테이머가 색 조건을 충족한다) ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UseRequirement_SpecifiedTraitMeetsColorRequirement(bool hasTs)
    {
        var (state, engine) = NewMainPhase();   // 블루 테이머만 있다
        if (hasTs) Put(state.Player, Card("B28-TS", type: CardType.Digimon, level: 4, color: CardColor.Green, traits: "TS"));
        var option = Card("B28-USEREQ", "＜Use Req. ([TS] trait)＞ (Specified cards let you ignore color requirements.)\n[Main] Gain 2 memory.", color: CardColor.Red);
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(option).Level);

        Assert.Equal(hasTs, ActionValidator.MissingOptionColors(state.Player, option).Count == 0);
        state.Player.Hand.Add(option);
        Assert.Equal(hasTs, engine.PlayCard(option).CanExecute);
    }

    [Fact]
    public void UseRequirement_CountsBreedingAreaAndTamers()
    {
        var (state, _) = NewMainPhase();
        var option = Card("B28-USEREQ-BR", "＜Use Req. ([NSp]/[DS] trait)＞\n[Main] Gain 2 memory.", color: CardColor.Red);
        Assert.NotEmpty(ActionValidator.MissingOptionColors(state.Player, option));
        state.Player.BreedingArea = new CardInstance(Card("B28-DS-BABY", type: CardType.Digimon, level: 3, color: CardColor.Yellow, traits: "DS"), 1);
        Assert.Empty(ActionValidator.MissingOptionColors(state.Player, option));
    }

    // ---- 정보가 적힌 토큰 ----

    [Fact]
    public void PlayFamiliarToken_WithOnDeletion()
    {
        var (state, engine) = NewMainPhase();
        Play(state, engine, Card("B28-FAMILIAR", "[Main] Play 1 [Familiar] Token. (Digimon/Yellow/3000 DP/[On Deletion] 1 of your opponent's Digimon gets -3000 DP for the turn.)"));

        var token = Assert.Single(state.Player.BattleArea);
        Assert.Equal("TOKEN-Familiar", token.TopCard.CardId);
        Assert.Equal(CardColor.Yellow, token.TopCard.Color);
        Assert.Equal(3000, token.GetCurrentDP());

        var opp = Put(state.DummyOpponent, Card("B28-FAM-OPP", type: CardType.Digimon, level: 4, dp: 5000));
        Play(state, engine, Card("B28-FAM-DEL", "[Main] Delete 1 of your Digimon."));

        Assert.Empty(state.Player.BattleArea);
        Assert.DoesNotContain(state.Player.Trash, c => c.CardId.StartsWith("TOKEN-"));   // 토큰은 트래시에 남지 않는다
        Assert.Equal(2000, opp.GetCurrentDP());   // 토큰의 【소멸 시】
    }

    [Fact]
    public void PlayTwoTokensWithKeywords()
    {
        var (state, engine) = NewMainPhase();
        Play(state, engine, Card("B28-AMON", "[Main] Play 1 [Amon of Crimson Flame] Token (Digimon/Red/6000 DP/＜Rush＞) and 1 [Umon of Blue Thunder] Token. (Digimon/Yellow/6000 DP/＜Blocker＞)"));

        Assert.Equal(2, state.Player.BattleArea.Count);
        var umon = state.Player.BattleArea.Single(d => d.TopCard.Name.StartsWith("Umon"));
        Assert.True(umon.HasBlocker);
        var amon = state.Player.BattleArea.Single(d => d.TopCard.Name.StartsWith("Amon"));
        Assert.True(CardScripts.HasKeyword(amon, Keyword.Rush));
    }

    [Fact]
    public void TokenWithUnsupportedEffect_IsLeftUnsupported()
    {
        var card = Card("B28-TOKEN-BAD", "[Main] Play 1 [Weird] Token. (Digimon/White/3000 DP/[Your Turn] This Digimon does something unknown.)");
        Assert.NotEqual(EffectSupportLevel.Full, EffectSupport.For(card).Level);
    }

    // ---- 시큐리티 찾기 ----

    [Theory]
    [InlineData(CardColor.Yellow, 3)]
    [InlineData(CardColor.Red, 2)]
    public void SearchYourSecurityStack_RevealAndAdd_IfYellowRecovery(CardColor color, int securityAfter)
    {
        var (state, engine) = NewMainPhase();
        var picked = Card("B28-SEC-PICK", type: CardType.Digimon, level: 5, color: color);
        state.Player.SecurityStack.Add(Card("B28-SEC-A", type: CardType.Digimon, level: 3));
        state.Player.SecurityStack.Add(picked);
        state.Player.SecurityStack.Add(Card("B28-SEC-B", type: CardType.Digimon, level: 3));

        Play(state, engine, Card("B28-SEC-SEARCH", "[Main] Search your security stack, reveal 1 card among it and add it to your hand. If it's a yellow card, ＜Recovery +1 (Deck)＞. Then, shuffle your security stack."));

        Assert.Contains(picked, state.Player.Hand);   // 레벨이 가장 높은 카드를 추천
        Assert.Equal(securityAfter, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void SearchYourSecurityStack_MayPlay()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Card("B28-SEC-TAMER", type: CardType.Tamer, color: CardColor.Red);
        state.Player.SecurityStack.Add(tamer);
        state.Player.SecurityStack.Add(Card("B28-SEC-C", type: CardType.Option));

        Play(state, engine, Card("B28-SEC-PLAY", "[Main] Search your security stack, and you may play 1 red or yellow Tamer card among it without paying its cost. If you did, ＜Recovery +1 (Deck)＞. Then, shuffle your security stack."));

        Assert.Contains(tamer, state.Player.Tamers);
        Assert.Equal(2, state.Player.SecurityStack.Count);
    }

    // ---- 공개 · 진화원 파기 ----

    [Fact]
    public void RevealPick_TraitOrNames()
    {
        var (state, engine) = NewMainPhase();
        state.Player.MainDeck.Clear();
        var susano = Card("B28-SUSANO", type: CardType.Digimon, level: 6, name: "Susanoomon");
        state.Player.MainDeck.Add(Card("B28-R1", type: CardType.Digimon, level: 3));
        state.Player.MainDeck.Add(susano);
        state.Player.MainDeck.Add(Card("B28-R3", type: CardType.Digimon, level: 4));

        Play(state, engine, Card("B28-REVEAL-OR", "[Main] Reveal the top 3 cards of your deck. Add 1 card with the [Hybrid] trait, or [Susanoomon] or [Takuya Kanbara] among them to your hand. Return the rest to the bottom of the deck."));

        Assert.Contains(susano, state.Player.Hand);
    }

    [Fact]
    public void RevealLookOnly_KeepsOnTop()
    {
        var (state, engine) = NewMainPhase();
        var top = state.Player.MainDeck[0];
        Play(state, engine, Card("B28-LOOK", "[Main] Reveal the top 3 cards of your deck. Return the revealed cards to the top or bottom of the deck."));
        Assert.Same(top, state.Player.MainDeck[0]);
        Assert.Equal(6, state.Player.MainDeck.Count);
    }

    [Fact]
    public void TrashAnyDigivolutionCardsFromYourOpponentsDigimon()
    {
        var (state, engine) = NewMainPhase();
        var a = Put(state.DummyOpponent, Card("B28-ANY-A", type: CardType.Digimon, level: 5), false, Source("B28-ANY-A1"), Source("B28-ANY-A2"));
        var b = Put(state.DummyOpponent, Card("B28-ANY-B", type: CardType.Digimon, level: 5), false, Source("B28-ANY-B1"));

        Play(state, engine, Card("B28-ANY", "[Main] Trash any 2 digivolution cards from your opponent's Digimon."));

        Assert.Equal(1, a.EvolutionSources.Count + b.EvolutionSources.Count);
        Assert.Equal(2, state.DummyOpponent.Trash.Count);
    }

    [Fact]
    public void PlaceFromTrashAsThisDigimonsBottomDigivolutionCards()
    {
        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Card("B28-VEM-HOST", "[Main] [Once Per Turn] You may place up to 2 [Vemmon] from your trash as this Digimon's bottom digivolution cards.", CardType.Digimon, level: 6));
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(host.TopCard).Level);
        state.Player.Trash.Add(Card("B28-VEM1", type: CardType.Digimon, level: 3, name: "Vemmon"));
        state.Player.Trash.Add(Card("B28-VEM2", type: CardType.Digimon, level: 3, name: "Vemmon"));

        Assert.True(engine.ActivateMainAbility(host, host.TopCard).CanExecute);

        Assert.Equal(2, host.EvolutionSources.Count);
    }

    // ---- 조건의 "or" · 이름 앞 조건 · 제외 특징 ----

    [Fact]
    public void CardPick_FilterOr_IsEitherOne()
    {
        var spec = TargetSpec.Parse("1 of your [Mineral] or [Rock] trait Digimon");
        Assert.NotNull(spec);
        var rock = new CardInstance(Card("B28-ROCK", type: CardType.Digimon, level: 4, traits: "Rock"), 1);
        var other = new CardInstance(Card("B28-OTHER", type: CardType.Digimon, level: 4, traits: "Dragon"), 1);
        Assert.True(spec!.Filters.All(f => f(rock)));
        Assert.False(spec.Filters.All(f => f(other)));

        var dp = TargetSpec.Parse("1 of your opponent's 7000 DP or lower Digimon");
        Assert.NotNull(dp);
        Assert.True(dp!.Opponent);
    }

    [Fact]
    public void InAnyOfItsTraits_OtherThanSeaAnimal()
    {
        var spec = TargetSpec.Parse("1 of your Digimon with [Beast] or [Animal] in any of its traits other than [Sea Animal]");
        Assert.NotNull(spec);
        var seal = new CardInstance(Card("B28-SEAL", type: CardType.Digimon, level: 4, traits: "Sea Animal"), 1);
        var lion = new CardInstance(Card("B28-LION", type: CardType.Digimon, level: 4, traits: "Beast"), 1);
        Assert.False(spec!.Filters.All(f => f(seal)));
        Assert.True(spec.Filters.All(f => f(lion)));
    }

    // ---- 생성 한글 (공식 표기) ----

    [Theory]
    [InlineData("[Opponent's Turn] When this card is trashed from digivolution cards by an effect, gain 1 memory.",
                "【상대의 턴】 이 카드가 효과로 진화원에서 파기되었을 때, 메모리를 +1 한다.")]
    [InlineData("When effects trash this card from a [Mineral] or [Rock] trait Digimon's digivolution cards, ＜Draw 1＞ (Draw 1 card from your deck.)",
                "이 카드가 특징으로 「광물형」/「광석형」을 가진 디지몬의 진화원에서 효과로 파기되었을 때, 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다).")]
    [InlineData("[Your Turn] [Once Per Turn] When an effect places a digivolution card under this Digimon, ＜Draw 1＞ (Draw 1 card from your deck.)",
                "【자신의 턴】 [턴에 1회] 이 디지몬의 진화원이 효과로 늘어났을 때, 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다).")]
    [InlineData("[Your Turn] When an effect places a Tamer card in one of your Digimon's digivolution cards, by suspending this Tamer, gain 1 memory.",
                "【자신의 턴】 자신의 디지몬의 진화원에 테이머 카드가 효과로 놓였을 때, 이 테이머를 레스트시키는 것으로, 메모리를 +1 한다.")]
    [InlineData("[Your Turn] When one of your opponent's digivolution cards is trashed, you may suspend this Tamer to gain 1 memory.",
                "【자신의 턴】 상대 디지몬의 진화원을 파기했을 때, 이 테이머를 레스트시키는 것으로, 메모리를 +1 한다.")]
    [InlineData("[When Digivolving] Trash any 2 digivolution cards from your opponent's Digimon.",
                "【진화 시】 상대의 디지몬의 진화원을 선택하여 2장 파기한다.")]
    [InlineData("[On Play] [When Digivolving] Reveal the top 5 cards of your or your opponent's deck. Return the revealed cards to the top or the bottom of the deck.",
                "【등장 시】 【진화 시】 자신/상대의 덱 위에서부터 5장 오픈한다. 오픈한 카드는 덱 위 또는 아래로만 되돌린다.")]
    [InlineData("[On Play] Search your security stack. You may add 1 card with [Leomon] in its name among them to the hand. If you added a card, ＜Recovery +1 (Deck)＞. Then, shuffle your security stack.",
                "【등장 시】 자신의 시큐리티를 전부 확인한다. 그중 명칭에 「레오몬」을 포함하는 카드 1장을 패에 추가할 수 있다. 이 효과로 추가했다면, 〈리커버리 +1 《덱》〉 (자신의 덱 위에서 카드를 1장 시큐리티 위에 놓는다). 그 후, 자신의 시큐리티를 셔플한다.")]
    [InlineData("[On Play] [When Digivolving] You may play 1 [Familiar] Token. (Digimon/Yellow/3000 DP/[On Deletion] 1 of your opponent's Digimon gets -3000 DP for the turn.)",
                "【등장 시】 【진화 시】 「사역마」(디지몬・옐로・DP 3000・【소멸 시】 이 턴 동안, 상대의 디지몬 1마리의 DP를 -3000 한다.) 토큰 1마리를 등장시킬 수 있다.")]
    [InlineData("[On Play] [When Digivolving] Play 1 [Amon of Crimson Flame] Token (Digimon/Red/6000 DP/＜Rush＞) and 1 [Umon of Blue Thunder] Token. (Digimon/Yellow/6000 DP/＜Blocker＞)",
                "【등장 시】 【진화 시】 「홍염의 아몬」(디지몬・레드・DP 6000・《속공》)과 「창뢰의 우몬」(디지몬・옐로・DP 6000・《블로커》) 토큰 1마리씩을 등장시킨다.")]
    [InlineData("[Opponent's Turn] [Once Per Turn] When an opponent's Digimon attacks, trash its bottom 2 digivolution cards.",
                "【상대의 턴】 [턴에 1회] 상대의 디지몬이 어택했을 때, 그 디지몬의 진화원을 아래에서부터 2장 파기한다.")]
    public void KoreanRendering_FollowsOfficialWording(string english, string korean)
    {
        Assert.Equal(korean, KoreanEffectRenderer.RenderClause(english));
    }

    [Fact]
    public void KoreanRendering_DecoyWithTwoColors()
    {
        string? ko = KoreanEffectRenderer.RenderClause("[On Play] [When Digivolving] You may play 1 [Atho, René & Por] Token. (Digimon/White/6000 DP/＜Reboot＞ ＜Blocker＞ ＜Decoy (Red/Black)＞)");
        Assert.Equal("【등장 시】 【진화 시】 「아트＆르네＆포르」(디지몬・화이트・DP 6000・《재기동》《블로커》《디코이《레드》/《블랙》》) 토큰 1마리를 등장시킬 수 있다.", ko);   // 공식 BT20-017
    }
}
