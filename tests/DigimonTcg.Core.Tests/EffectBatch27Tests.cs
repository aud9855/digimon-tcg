using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.27.0 미지원 문장 1차 묶음: 테이머 등장 · 진화한 카드 조건 · 액티브가 되었을 때 · 자신의 디지몬이 배틀에서 소멸시켰을 때 ·
/// "If this effect didn't delete / suspend" · 주어 뒤의 may · 키워드 + DP · 서로의 덱 파기 · 상대 디지몬과 테이머 레스트 ·
/// "this Digimon is [X]" · 생략된 조건 · 대상 조건의 "or"
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch27Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    /// <summary>메인 페이즈 (메모리 8) — tamerEffect가 있으면 그 효과의 테이머를 놓는다</summary>
    private static (GameState State, GoldfishEngine Engine) NewMainPhase(string? tamerEffect = null)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 6; i++)
        {
            state.Player.MainDeck.Add(Card("B27-FILL" + i, type: CardType.Digimon, level: 3));
            state.DummyOpponent.MainDeck.Add(Card("B27-OFILL" + i, type: CardType.Digimon, level: 3));
        }
        // 블루 테이머 — 옵션(블루)의 색 조건을 맞추고, 효과가 있으면 그 효과를 시험한다
        var tamer = Card("B27-TAMER-" + (tamerEffect?.GetHashCode() ?? 0), tamerEffect ?? "", CardType.Tamer);
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

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false)
    {
        var d = new CardInstance(card, 1) { IsSuspended = suspended };
        zones.BattleArea.Add(d);
        return d;
    }

    // ---- 테이머 등장 (BT11-023 · BT7-070) ----

    [Theory]
    [InlineData(CardColor.Blue, true)]
    [InlineData(CardColor.Red, false)]
    public void WhenYouPlayABlueTamer(CardColor color, bool triggers)
    {
        var (state, engine) = NewMainPhase("[Your Turn] [Once Per Turn] When you play a blue Tamer, gain 1 memory.");

        Play(state, engine, Card("B27-TPLAY-" + color, type: CardType.Tamer, playCost: 2, color: color));

        Assert.Equal(8 - 2 + (triggers ? 1 : 0), state.MemoryGauge);
    }

    [Fact]
    public void WhenAnyOfYourTamersArePlayed_AlsoByEffect()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When any of your Tamers are played, gain 1 memory.");
        state.Player.Hand.Add(Card("B27-TFREE", type: CardType.Tamer, playCost: 4));

        Play(state, engine, Card("B27-TFREE-OPT", "[Main] You may play 1 Tamer card from your hand without paying the cost."));

        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    // ---- 진화한 카드 조건 (BT16-084) ----

    [Theory]
    [InlineData(CardColor.Red, true)]
    [InlineData(CardColor.Blue, false)]
    public void WhenOneOfYourDigimonDigivolvesIntoARedOrYellowDigimon(CardColor color, bool triggers)
    {
        var (state, engine) = NewMainPhase("[Your Turn] When one of your Digimon digivolves into a red or yellow Digimon, gain 1 memory.");
        var baseDigi = Put(state.Player, Card("B27-EVOBASE-" + color, type: CardType.Digimon, level: 3, color: color));
        var evo = Card("B27-EVO-" + color, type: CardType.Digimon, level: 4, color: color);
        state.Player.Hand.Add(evo);

        Assert.True(engine.EvolveDigimon(baseDigi, evo).CanExecute);

        Assert.Equal(8 - 1 + (triggers ? 1 : 0), state.MemoryGauge);
    }

    // ---- 액티브가 되었을 때 (공식 Q&A BT11-069 · BT2-021) ----

    [Fact]
    public void WhenADigimonBecomesUnsuspended_CountsOpponentsDigimon_AndNotAlreadyActiveOnes()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When a Digimon becomes unsuspended, gain 1 memory.");
        Put(state.Player, Card("B27-UN-MINE", type: CardType.Digimon, level: 4), suspended: true);
        Put(state.Player, Card("B27-UN-ACTIVE", type: CardType.Digimon, level: 4));          // 이미 액티브 — "액티브가 되었을 때"가 아니다 (Q&A BT2-021)
        Put(state.DummyOpponent, Card("B27-UN-OPP", type: CardType.Digimon, level: 4), suspended: true);

        Play(state, engine, Card("B27-UN-ALL", "[Main] Unsuspend all of your Digimon."));
        Assert.Equal(8 - 3 + 1, state.MemoryGauge);

        Play(state, engine, Card("B27-UN-OPPOPT", "[Main] Unsuspend 1 of your opponent's Digimon."));   // 상대 디지몬도 (Q&A BT11-069)
        Assert.Equal(8 - 3 + 1 - 3 + 1, state.MemoryGauge);
    }

    [Fact]
    public void ThisDigimonMayUnsuspend()
    {
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Card("B27-MAYUN", type: CardType.Digimon, level: 4), suspended: true);

        Play(state, engine, Card("B27-MAYUN-OPT", "[Main] 1 of your Digimon may unsuspend."));

        Assert.False(mine.IsSuspended);
    }

    // ---- 자신의 디지몬이 배틀에서 소멸시켰을 때 (EX1-041 · BT9-088) ----

    [Theory]
    [InlineData(CardColor.Blue, true)]
    [InlineData(CardColor.Red, false)]
    public void WhenOneOfYourGreenOrBlueDigimonDeletesInBattleAndSurvives(CardColor color, bool triggers)
    {
        var (state, engine) = NewMainPhase("[Your Turn] When one of your green or blue Digimon deletes an opponent's Digimon in battle and survives, gain 1 memory.");
        var attacker = Put(state.Player, Card("B27-WIN-" + color, type: CardType.Digimon, level: 5, color: color));
        var defender = Put(state.DummyOpponent, Card("B27-LOSE-" + color, type: CardType.Digimon, level: 3), suspended: true);

        Assert.True(engine.AttackOpponentDigimon(attacker, defender).CanExecute);

        Assert.DoesNotContain(defender, state.DummyOpponent.BattleArea);
        Assert.Equal(8 + (triggers ? 1 : 0), state.MemoryGauge);
    }

    // ---- "If this effect didn't delete / suspend" (공식 Q&A BT21-068 · BT18-082) ----

    [Theory]
    [InlineData(3000, false)]
    [InlineData(5000, true)]
    public void IfThisEffectDidntDelete(int oppDp, bool bonus)
    {
        var (state, engine) = NewMainPhase();
        var opp = Put(state.DummyOpponent, Card("B27-DD-" + oppDp, type: CardType.Digimon, level: 4, dp: oppDp));

        Play(state, engine, Card("B27-DD-OPT", "[Main] Delete 1 of your opponent's Digimon with 4000 DP or less. If this effect didn't delete, gain 2 memory."));

        Assert.Equal(!bonus, !state.DummyOpponent.BattleArea.Contains(opp));   // 대상이 있으면 반드시 소멸시킨다 (Q&A BT21-068)
        Assert.Equal(8 - 3 + (bonus ? 2 : 0), state.MemoryGauge);
    }

    [Fact]
    public void IfThisEffectDidntSuspend()
    {
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, Card("B27-DS-OPP", type: CardType.Digimon, level: 4), suspended: true);   // 액티브인 상대 디지몬이 없다

        Play(state, engine, Card("B27-DS-OPT", "[Main] Suspend 1 of your opponent's unsuspended Digimon. If this effect didn't suspend, gain 2 memory."));

        Assert.Equal(8 - 3 + 2, state.MemoryGauge);
    }

    [Fact]
    public void IfThisEffectDidntDelete_AfterCost_IsLeftUnsupported()
    {
        // 비용을 내지 않았을 때 뒷문장을 처리할지 Q&A가 없다 (BT16-081)
        var card = Card("B27-DD-COST", "[When Digivolving] By deleting 1 of your Digimon or Tamers, delete 1 of your opponent's unsuspended Digimon. If this effect didn't delete an opponent's Digimon, delete 1 of their Tamers.",
            CardType.Digimon, level: 6);
        Assert.NotEqual(EffectSupportLevel.Full, EffectSupport.For(card).Level);
    }

    // ---- 키워드 + DP · 양쪽 대상의 may (BT21-014 · EX6-023) ----

    [Fact]
    public void GainsKeywordAndDp_WithoutGets()
    {
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Card("B27-KD", type: CardType.Digimon, level: 4));

        Play(state, engine, Card("B27-KD-OPT", "[Main] 1 of your Digimon gains ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon and survives the battle, it performs any security checks it normally would.) and +3000 DP for the turn."));

        Assert.Equal(4000 + 3000, mine.GetCurrentDP());
        Assert.True(CardScripts.HasKeyword(mine, Keyword.Piercing));
    }

    [Fact]
    public void OneDigimonMayGainSecurityMinus_PrefersOpponentsDigimon()
    {
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Card("B27-SA-MINE", type: CardType.Digimon, level: 6));
        var opp = Put(state.DummyOpponent, Card("B27-SA-OPP", type: CardType.Digimon, level: 4));

        Play(state, engine, Card("B27-SA-OPT", "[Main] 1 Digimon may gain ＜Security A. -1＞ until the end of your opponent's turn."));

        Assert.Contains(opp.TimedSecurityAttackBuffs, b => b.Item1 == -1);
        Assert.DoesNotContain(mine.TimedSecurityAttackBuffs, b => b.Item1 == -1);
    }

    // ---- 서로의 덱 파기 (공식 Q&A EX4-008) · 상대 디지몬과 테이머 레스트 ----

    [Fact]
    public void TrashTheTopCardsOfBothPlayersDecks()
    {
        var (state, engine) = NewMainPhase();

        Play(state, engine, Card("B27-MILL2", "[Main] Trash the top 2 cards of both players' decks."));

        Assert.Equal(4, state.Player.MainDeck.Count);
        Assert.Equal(4, state.DummyOpponent.MainDeck.Count);
        Assert.Equal(2, state.DummyOpponent.Trash.Count);
    }

    [Fact]
    public void SuspendAllOfYourOpponentsDigimonAndTamers()
    {
        var (state, engine) = NewMainPhase();
        var opp = Put(state.DummyOpponent, Card("B27-ST-OPP", type: CardType.Digimon, level: 4));
        var oppTamer = Card("B27-ST-OTAMER", type: CardType.Tamer);
        state.DummyOpponent.Tamers.Add(oppTamer);

        Play(state, engine, Card("B27-ST-OPT", "[Main] Suspend all of your opponent's Digimon and Tamers."));

        Assert.True(opp.IsSuspended);
        Assert.Contains(oppTamer, state.DummyOpponent.SuspendedTamers);
    }

    // ---- "this Digimon is [X]" · 생략된 조건 · 다른 디지몬 소멸 ----

    [Theory]
    [InlineData("Argomon", true)]
    [InlineData("Otheramon", false)]
    public void WhileThisDigimonIsNamed(string name, bool blocker)
    {
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Card("B27-NAMED-" + name, "[All Turns] While this Digimon is [Argomon], it gains ＜Blocker＞.", CardType.Digimon, level: 4, name: name));

        engine.RecalculateFieldBuffs();

        Assert.Equal(blocker, d.HasBlocker);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 1)]
    public void IfYouHave3OrFewer_MeansSecurityCards(int security, int gained)
    {
        var (state, engine) = NewMainPhase();
        for (int i = 0; i < security; i++) state.Player.SecurityStack.Add(Card("B27-SEC" + i, type: CardType.Digimon, level: 3));

        Play(state, engine, Card("B27-ELLIPSIS", "[Main] If you have 3 or more security cards, gain 1 memory. If you have 3 or fewer, gain 2 memory."));

        Assert.Equal(8 - 3 + gained, state.MemoryGauge);
    }

    [Fact]
    public void WhenOtherDigimonAreDeleted()
    {
        var (state, engine) = NewMainPhase("[Your Turn] When other Digimon are deleted, gain 1 memory.");
        Put(state.DummyOpponent, Card("B27-OD-OPP", type: CardType.Digimon, level: 3));

        Play(state, engine, Card("B27-OD-OPT", "[Main] Delete 1 of your opponent's Digimon."));

        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    // ---- 대상 조건의 "or" (v0.27.0 전에는 둘 다로 읽었다 — ST12-13 등) ----

    [Fact]
    public void TargetFilter_NameOrTrait_IsEitherOne()
    {
        var spec = TargetSpec.Parse("all of your Digimon with [Huckmon] in their names or [Royal Knight] trait");
        Assert.NotNull(spec);
        var knight = new CardInstance(Card("B27-OR-KNIGHT", type: CardType.Digimon, level: 6, name: "Gallantmon", traits: "Royal Knight"), 1);
        var huck = new CardInstance(Card("B27-OR-HUCK", type: CardType.Digimon, level: 3, name: "Huckmon", traits: "Minor"), 1);
        var other = new CardInstance(Card("B27-OR-OTHER", type: CardType.Digimon, level: 3, name: "Agumon", traits: "Reptile"), 1);

        Assert.True(spec!.Filters.All(f => f(knight)));
        Assert.True(spec.Filters.All(f => f(huck)));
        Assert.False(spec.Filters.All(f => f(other)));
    }

    // ---- 생성 한글 (공식 표기) ----

    [Theory]
    [InlineData("[Your Turn] [Once Per Turn] When you play a blue Tamer, gain 1 memory.",
                "【자신의 턴】 [턴에 1회] 블루인 자신의 테이머가 등장했을 때, 메모리를 +1 한다.")]
    [InlineData("[When Digivolving] Delete 1 of your opponent's Digimon with 4000 DP or less. If this effect didn't delete, this Digimon gets +3000 DP for the turn.",
                "【진화 시】 DP 4000 이하인 상대의 디지몬 1마리를 소멸시킨다. 이 효과로 소멸하지 않았다면, 이 턴 동안, 이 디지몬의 DP를 +3000 한다.")]
    [InlineData("[On Play] [When Digivolving] Trash the top 2 cards of both players' decks.",
                "【등장 시】 【진화 시】 서로의 덱 위에서부터 2장 파기한다.")]
    [InlineData("[Your Turn] When one of your Digimon digivolves into a red or yellow Digimon, gain 1 memory.",
                "【자신의 턴】 자신의 디지몬이 레드/옐로인 디지몬으로 진화했을 때, 메모리를 +1 한다.")]
    [InlineData("[Opponent's Turn] [Once Per Turn] When a Digimon becomes unsuspended, ＜Draw 1＞ (Draw 1 card from your deck.)",
                "【상대의 턴】 [턴에 1회] 디지몬이 액티브가 되었을 때, 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다).")]
    [InlineData("[All Turns] [Once Per Turn] When other Digimon are deleted, trash your opponent's top security card.",
                "【서로의 턴】 [턴에 1회] 다른 디지몬이 소멸했을 때, 상대의 시큐리티를 위에서부터 1장 파기한다.")]
    [InlineData("[End of Your Turn] [Once Per Turn] 1 of your Digimon may unsuspend.",
                "【자신의 턴 종료 시】 [턴에 1회] 자신의 디지몬 1마리를 액티브로 할 수 있다.")]
    [InlineData("[When Attacking] [Once Per Turn] 1 Digimon may gain ＜Security A. -1＞ until the end of your opponent's turn.",
                "【어택 시】 [턴에 1회] 상대의 턴 종료 시까지, 디지몬 1마리에게 《시큐리티 어택 -1》 (이 디지몬이 체크하는 시큐리티의 매수 -1)을 줄 수 있다.")]
    [InlineData("[All Turns] [Once Per Turn] When an opponent's Digimon or Tamer becomes suspended, you may unsuspend this Digimon.",
                "【서로의 턴】 [턴에 1회] 상대의 디지몬 또는 테이머가 레스트했을 때, 이 디지몬을 액티브로 할 수 있다.")]
    [InlineData("[Your Turn] When one of your green or blue Digimon deletes an opponent's Digimon in battle and survives, gain 1 memory.",
                "【자신의 턴】 그린/블루인 자신의 디지몬이 배틀에서 상대 디지몬만을 소멸시켰을 때, 메모리를 +1 한다.")]
    public void KoreanRendering_FollowsOfficialWording(string english, string korean)
    {
        Assert.Equal(korean, KoreanEffectRenderer.RenderClause(english));
    }

    [Fact]
    public void KoreanRendering_GainsKeywordAndDp()
    {
        string? ko = KoreanEffectRenderer.RenderClause("[On Play] [When Digivolving] This Digimon gains ＜Piercing＞ and +3000 DP for the turn.");
        Assert.NotNull(ko);
        Assert.StartsWith("【등장 시】 【진화 시】 이 턴 동안, 이 디지몬은 《관통》", ko);
        Assert.EndsWith("을 얻고, DP +3000.", ko);   // 공식 BT21-014: "이 디지몬은 《관통》을 얻고, DP +3000."
    }
}
