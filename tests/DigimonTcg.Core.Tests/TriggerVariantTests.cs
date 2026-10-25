using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// 발동 시점 묶음: 코스트 N 이상 옵션 사용 · 패가 버려졌을 때 · 덱에서 파기되었을 때 · 효과로 상대 패에 카드 · 효과로 등장 ·
/// 효과로 레스트(어택 레스트는 아님) · 어느 디지몬이든 레스트
/// </summary>
public class TriggerVariantTests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3, CardColor color = CardColor.Blue)
    {
        var card = new CardData
        {
            CardId = id, Name = id, Type = type, Color = color, Level = level, DP = level * 1000, PlayCost = playCost, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main,
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    /// <summary>테이머(발동 효과)를 놓은 메인 페이즈 — 메모리 8</summary>
    private static (GameState State, GoldfishEngine Engine) NewMainPhase(string tamerEffect)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 6; i++)
        {
            state.Player.MainDeck.Add(Card("TV-FILL" + i, type: CardType.Digimon, level: 3));
            state.DummyOpponent.MainDeck.Add(Card("TV-OFILL" + i, type: CardType.Digimon, level: 3));
        }
        var tamer = Card("TV-TAMER-" + tamerEffect.GetHashCode(), tamerEffect, CardType.Tamer);
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(tamer).Level);
        state.Player.Tamers.Add(tamer);
        return (state, new GoldfishEngine(state));
    }

    private static void Play(GameState state, GoldfishEngine engine, CardData card)
    {
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(1, false)]
    public void OptionWithCostOrMore(int cost, bool triggers)
    {
        var (state, engine) = NewMainPhase("[Your Turn] When you use an Option card with a cost of 3 or more, gain 1 memory.");

        Play(state, engine, Card("TV-OPT-" + cost, "[Main] ＜Draw 1＞ (Draw 1 card from your deck.)", playCost: cost));

        Assert.Equal(8 - cost + (triggers ? 1 : 0), state.MemoryGauge);
    }

    [Fact]
    public void HandTrashed()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When your hand is trashed from, gain 2 memory.");
        state.Player.Hand.Add(Card("TV-JUNK"));

        Play(state, engine, Card("TV-DISCARD", "[Main] Trash 1 card in your hand."));

        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }

    [Fact]
    public void DeckTrashed_OncePerMill()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When a card is trashed from your deck, gain 1 memory.");

        Play(state, engine, Card("TV-MILL", "[Main] Trash the top 2 cards of your deck."));

        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    [Fact]
    public void EffectAddsCardsToOpponentsHand()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When an effect adds cards to your opponent's hand, gain 1 memory.");
        state.DummyOpponent.BattleArea.Add(new CardInstance(Card("TV-OPP", type: CardType.Digimon, level: 3), 0));

        Play(state, engine, Card("TV-BOUNCE", "[Main] Return 1 of your opponent's Digimon to the hand."));

        Assert.Single(state.DummyOpponent.Hand);
        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EffectPlaysOneOfYourDigimon(bool byEffect)
    {
        var (state, engine) = NewMainPhase("[Your Turn] When an effect plays one of your Digimon, gain 2 memory.");
        var digi = Card("TV-DIGI", type: CardType.Digimon, level: 3, playCost: 2);
        if (byEffect)
        {
            state.Player.Hand.Add(digi);
            Play(state, engine, Card("TV-PLAYER", "[Main] Play 1 level 3 Digimon card from your hand without paying the cost."));
            Assert.Equal(8 - 3 + 2, state.MemoryGauge);
        }
        else
        {
            state.Player.Hand.Add(digi);
            Assert.True(engine.PlayCard(digi).CanExecute);
            Assert.Equal(8 - 2, state.MemoryGauge);                                     // 패에서 그냥 등장 — 발동하지 않는다
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EffectSuspends_NotByAttack(bool byEffect)
    {
        var (state, engine) = NewMainPhase("[Your Turn] When an effect suspends one of your Digimon, gain 1 memory.");
        var mine = new CardInstance(Card("TV-MINE", type: CardType.Digimon, level: 4), 1);
        state.Player.BattleArea.Add(mine);
        state.DummyOpponent.SecurityStack.Add(Card("TV-OSEC", type: CardType.Digimon, level: 3));

        if (byEffect) Play(state, engine, Card("TV-SUS", "[Main] Suspend 1 of your Digimon."));
        else engine.AttackSecurityOrDirect(mine);

        Assert.True(mine.IsSuspended);
        Assert.Equal(byEffect ? 8 - 3 + 1 : 8, state.MemoryGauge);
    }

    [Fact]
    public void AnyDigimonSuspend_IncludesOpponents()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When any Digimon suspend, gain 1 memory.");
        state.DummyOpponent.BattleArea.Add(new CardInstance(Card("TV-OPP2", type: CardType.Digimon, level: 3), 0));

        Play(state, engine, Card("TV-SUS2", "[Main] Suspend 1 of your opponent's Digimon."));

        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    /// <summary>
    /// "When one of your opponent's level 5 or lower Digimon is deleted" (디지몬 카이저 BT8-094) — 상대 디지몬이 주어.
    /// 예전에는 "your ~ Digimon"(자신의 디지몬) 규칙에 먼저 걸려 자신의 디지몬이 소멸했을 때 발동했다.
    /// </summary>
    [Fact]
    public void OpponentsDigimonDeleted_WithLevel_OnlyOpponentsMatchingDigimon()
    {
        var (state, engine) = NewMainPhase("[All Turns] When one of your opponent's level 5 or lower Digimon is deleted, you may suspend this Tamer to ＜Draw 1＞ (Draw 1 card from your deck.)");
        var tamer = state.Player.Tamers[0];
        Assert.Contains(CardScripts.For(tamer)!.Effects, e => e.Trigger == EffectTrigger.OpponentDigimonDeleted);
        var mine = new CardInstance(Card("TV-DMINE", type: CardType.Digimon, level: 4), 1);
        var keep = new CardInstance(Card("TV-DKEEP", type: CardType.Digimon, level: 3), 1);
        var enemyLv6 = new CardInstance(Card("TV-DLV6", type: CardType.Digimon, level: 6), 0);
        var enemyLv4 = new CardInstance(Card("TV-DLV4", type: CardType.Digimon, level: 4), 0);
        state.Player.BattleArea.AddRange(new[] { mine, keep });
        state.DummyOpponent.BattleArea.AddRange(new[] { enemyLv6, enemyLv4 });
        int hand = state.Player.Hand.Count;

        void Delete(string id, string text, CardInstance target)
        {
            var opt = Card(id, text, playCost: 0);
            state.Player.Hand.Add(opt);
            Assert.True(engine.PlayCard(opt, target).CanExecute);
        }

        Delete("TV-DEL-MINE", "[Main] Delete 1 of your Digimon.", mine);                 // 자신의 Lv.4
        Assert.DoesNotContain(mine, state.Player.BattleArea);
        Assert.Equal(hand, state.Player.Hand.Count);
        Assert.True(state.Player.HasActiveTamer(tamer));

        Delete("TV-DEL-LV6", "[Main] Delete 1 of your opponent's Digimon.", enemyLv6);   // 상대 Lv.6
        Assert.DoesNotContain(enemyLv6, state.DummyOpponent.BattleArea);
        Assert.Equal(hand, state.Player.Hand.Count);
        Assert.True(state.Player.HasActiveTamer(tamer));

        Delete("TV-DEL-LV4", "[Main] Delete 1 of your opponent's Digimon.", enemyLv4);   // 상대 Lv.4 → 레스트하고 1드로우
        Assert.Equal(hand + 1, state.Player.Hand.Count);
        Assert.False(state.Player.HasActiveTamer(tamer));
    }

    /// <summary>"When one of your opponent's Digimon attacks" (사나다 아라타 BT22-091 등) — 자신의 디지몬 어택이 아니라 상대 디지몬 어택</summary>
    [Fact]
    public void OneOfYourOpponentsDigimonAttacks_IsOpponentAttack()
    {
        var tamer = Card("TV-OATK", "[Opponent's Turn] When one of your opponent's Digimon attacks, gain 1 memory.", CardType.Tamer);

        var effect = Assert.Single(CardScripts.For(tamer)!.Effects);
        Assert.Equal(EffectTrigger.OpponentDigimonAttacks, effect.Trigger);
    }
}
