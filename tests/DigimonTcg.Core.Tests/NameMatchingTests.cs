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
/// 내장 한글 카드(Name = "그레이몬")도 영어 효과 문장의 이름 조건("[Greymon]" · "with [X] in its name")에 맞아야 한다 —
/// 공식 영어 이름(CardData.EnglishName)을 함께 본다 (Models.CardNames).
/// </summary>
public class NameMatchingTests
{
    private static readonly Lazy<Dictionary<string, CardData>> Catalog =
        new(() => AiMechanicBenchmarkTests.LoadCatalogCopy().ToDictionary(c => c.CardId));

    private static CardData Real(string id) =>
        Catalog.Value.TryGetValue(id, out var card) ? card : throw new InvalidOperationException($"{id} 카드가 data/cards_cache.json에 없습니다");

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, MemoryGauge = memory, TurnSequence = 3 };
        var filler = StarterDecks.GetAllCatalogCards().First(c => c.CardId == "ST1-16");
        for (int i = 0; i < 5; i++)
        {
            state.Player.MainDeck.Add(filler);
            state.DummyOpponent.MainDeck.Add(filler);
            state.DummyOpponent.SecurityStack.Add(filler);
        }
        return (state, new GoldfishEngine(state));
    }

    [Fact]
    public void EveryBuiltInCard_HasEnglishName()
    {
        var cards = StarterDecks.GetAllCatalogCards();
        Assert.Equal(120, cards.Count);
        Assert.All(cards, c => Assert.False(string.IsNullOrWhiteSpace(c.EnglishName), $"{c.CardId} {c.Name}: 영어 이름 없음"));
        // 한글 이름(화면 표시)은 그대로
        var greymon = cards.Single(c => c.CardId == "ST1-07");
        Assert.Equal("그레이몬", greymon.Name);
        Assert.Equal("Greymon", greymon.EnglishName);
        Assert.Equal("그레이몬", greymon.DisplayName);
    }

    [Fact]
    public void CardNames_MatchKoreanBuiltInByEnglishName()
    {
        var builtIn = StarterDecks.GetAllCatalogCards().ToDictionary(c => c.CardId);
        Assert.True(TargetSpec.NameHas(builtIn["ST1-07"], "Greymon"));
        Assert.True(TargetSpec.NameHas(builtIn["ST1-09"], "Greymon"));          // MetalGreymon
        Assert.True(TargetSpec.NameHas(builtIn["ST1-07"], "그레이몬"));          // 한글 이름도 그대로 맞는다
        Assert.False(TargetSpec.NameHas(builtIn["ST1-03"], "Greymon"));
        Assert.True(CardNames.Is(builtIn["ST1-12"], "Tai Kamiya"));
        Assert.True(CardNames.Same(builtIn["ST1-07"], Real("BT10-019")));        // 내장 그레이몬 = API Greymon (같은 명칭)
        Assert.False(CardNames.Same(builtIn["ST1-07"], builtIn["ST1-09"]));
    }

    [Fact]
    public void MarcusDamon_BT4_092_FiresWhenBuiltInKoreanGreymonAttacks()
    {
        var (state, engine) = NewMainPhase(memory: 2);
        var marcus = Real("BT4-092");
        state.Player.Tamers.Add(marcus);
        var greymon = new CardInstance(StarterDecks.GetAllCatalogCards().Single(c => c.CardId == "ST1-07"), 1);
        state.Player.BattleArea.Add(greymon);

        Assert.True(engine.AttackSecurityOrDirect(greymon).CanExecute);

        Assert.Contains(marcus, state.Player.SuspendedTamers);   // "you may suspend this Tamer to gain 1 memory"
        Assert.Equal(3, state.MemoryGauge);
    }

    [Fact]
    public void MarcusDamon_BT4_092_DoesNotFireForNonGreymon()
    {
        var (state, engine) = NewMainPhase(memory: 2);
        var marcus = Real("BT4-092");
        state.Player.Tamers.Add(marcus);
        var agumon = new CardInstance(StarterDecks.GetAllCatalogCards().Single(c => c.CardId == "ST1-03"), 1);
        state.Player.BattleArea.Add(agumon);

        engine.AttackSecurityOrDirect(agumon);

        Assert.DoesNotContain(marcus, state.Player.SuspendedTamers);
        Assert.Equal(2, state.MemoryGauge);
    }

    [Fact]
    public void Greymon_BT14_012_SeesBuiltInKoreanTaiKamiyaTamer()
    {
        var (state, engine) = NewMainPhase(memory: 2);
        state.Player.Tamers.Add(StarterDecks.GetAllCatalogCards().Single(c => c.CardId == "ST1-12"));   // 신태일 = [Tai Kamiya]
        var greymon = new CardInstance(Real("BT14-012"), 1);
        state.Player.BattleArea.Add(greymon);

        engine.AttackSecurityOrDirect(greymon);

        Assert.Equal(3, state.MemoryGauge);   // "if you have a Tamer with [Tai Kamiya] in its name, gain 1 memory"
    }

    [Fact]
    public void IzzyIzumi_BT1_088_WithoutLevel5Green_FailsWithoutResting()
    {
        var (state, engine) = NewMainPhase(memory: 3);
        var izzy = Real("BT1-088");
        state.Player.Tamers.Add(izzy);
        state.Player.BattleArea.Add(new CardInstance(StarterDecks.GetAllCatalogCards().Single(c => c.CardId == "ST4-03"), 1));   // 그린 Lv.3
        int hand = state.Player.Hand.Count, deck = state.Player.MainDeck.Count;

        var result = engine.ActivateMainAbility(null, izzy);

        Assert.False(result.CanExecute);
        Assert.Contains("조건 미충족", result.Reason);
        Assert.DoesNotContain(izzy, state.Player.SuspendedTamers);
        Assert.Equal(hand, state.Player.Hand.Count);
        Assert.Equal(deck, state.Player.MainDeck.Count);
        Assert.Contains(engine.GetMainAbilities(state.Player), a => ReferenceEquals(a.Card, izzy));   // 쓴 것으로 치지 않는다
    }

    [Fact]
    public void IzzyIzumi_BT1_088_WithLevel5Green_RestsAndReveals()
    {
        var (state, engine) = NewMainPhase(memory: 3);
        var izzy = Real("BT1-088");
        state.Player.Tamers.Add(izzy);
        state.Player.BattleArea.Add(new CardInstance(StarterDecks.GetAllCatalogCards().Single(c => c.CardId == "ST4-11"), 1));   // 그린 Lv.5 아트라캅테리몬
        var digimon = StarterDecks.GetAllCatalogCards().Single(c => c.CardId == "ST4-03");
        state.Player.MainDeck.Insert(0, digimon);

        var result = engine.ActivateMainAbility(null, izzy);

        Assert.True(result.CanExecute, result.Reason);
        Assert.Contains(izzy, state.Player.SuspendedTamers);
        Assert.Contains(digimon, state.Player.Hand);
    }
}
