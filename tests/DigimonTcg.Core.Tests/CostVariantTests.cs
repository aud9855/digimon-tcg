using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>"By ~ing, ~" 비용의 여러 표기: 디지몬 N마리 레스트 · 시큐리티 위/아래 파기 · 패의 카드를 진화원 아래에 · 테이머 아래 카드 파기</summary>
public class CostVariantTests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    private static CardData Card(string id, string main = "", CardType type = CardType.Digimon, int level = 4, CardColor color = CardColor.Green)
    {
        var card = new CardData
        {
            CardId = id, Name = id, Type = type, Color = color, Level = level, DP = level * 1000, PlayCost = 3, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main,
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 6; i++)
        {
            state.Player.MainDeck.Add(Card("CV-FILL" + i, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Card("CV-OFILL" + i, color: CardColor.White));
        }
        state.Player.BattleArea.Add(new CardInstance(Card("CV-A"), 1));
        state.Player.BattleArea.Add(new CardInstance(Card("CV-B"), 1));
        return (state, new GoldfishEngine(state) { PlayerChoices = new AcceptAll() });
    }

    private static void Play(GameState state, GoldfishEngine engine, CardData card)
    {
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute);
    }

    [Fact]
    public void SuspendingTwoDigimon()
    {
        var (state, engine) = NewMainPhase();

        Play(state, engine, Card("CV-OPT-1", "[Main] By suspending 2 Digimon, gain 2 memory.", CardType.Option, level: 0));

        Assert.All(state.Player.BattleArea, d => Assert.True(d.IsSuspended));
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }

    [Fact]
    public void TrashingTopOrBottomSecurityCard()
    {
        var (state, engine) = NewMainPhase();
        var top = Card("CV-SEC-TOP");
        state.Player.SecurityStack.Add(top);
        state.Player.SecurityStack.Add(Card("CV-SEC-BOTTOM"));
        int hand = state.Player.Hand.Count;

        Play(state, engine, Card("CV-OPT-2", "[Main] By trashing your top or bottom security card, ＜Draw 1＞ (Draw 1 card from your deck.)", CardType.Option, level: 0));

        Assert.Single(state.Player.SecurityStack);
        Assert.Contains(top, state.Player.Trash);                                        // "예" = 맨 위
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    [Fact]
    public void PlacingHandCardAsBottomDigivolutionCard()
    {
        var (state, engine) = NewMainPhase();
        var spare = Card("CV-SPARE", level: 3);
        state.Player.Hand.Add(spare);
        var digi = Card("CV-DIGI", "[On Play] By placing 1 card in your hand face down as this Digimon's bottom digivolution card, gain 1 memory.");

        Play(state, engine, digi);

        var played = state.Player.BattleArea.Single(d => ReferenceEquals(d.TopCard, digi));
        // v0.35.0 뒷면으로 놓는다 (종합 룰 4-7-4 · 4-7-9) — 엔진이 카드를 복사해 뒷면 표시
        var placed = Assert.Single(played.EvolutionSources);
        Assert.Equal(spare.CardId, placed.CardId);
        Assert.True(played.IsFaceDown(placed));
        Assert.DoesNotContain(spare, state.Player.Hand);
        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    [Fact]
    public void TrashingBottomCardUnderTamers()
    {
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(Card("CV-TAMER", type: CardType.Tamer, level: 0));
        // v0.35.0 "face-down" 비용은 뒷면으로 놓인 카드만 (앞면 ≪세이브≫ 카드는 아니다)
        var first = state.Player.AddUnderTamerFaceDown(Card("CV-UNDER-1"));
        var last = state.Player.AddUnderTamerFaceDown(Card("CV-UNDER-2"));

        Play(state, engine, Card("CV-OPT-3", "[Main] By trashing the bottom face-down card from under any of your Tamers, gain 2 memory.", CardType.Option, level: 0));

        Assert.Equal(new[] { first }, state.Player.UnderTamers);                       // 마지막에 놓인 카드가 맨 아래
        Assert.Contains(last, state.Player.Trash);
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }
}
