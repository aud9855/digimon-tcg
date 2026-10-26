using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.36.0 미지원 문장 10차 묶음: 효과로 등장 한 곳(GoldfishEngine.TryPlayByEffect) · 플레이어 제한 (효과로 등장 · 등장 코스트 마이너스 · 옵션 사용) ·
/// 테이머를 디지몬으로도 취급 (「최건우」) · 두 술어 보호 · 공개 + 상한 · 진화할 때 비용을 내는 진화 코스트 감소 · Lv.3/4/5 1장씩 · 용어집
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch36Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
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

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", int dp = -1, string? name = null, int evoCost = 1, int playCost = 3, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, CardColor.Yellow, dp, name, inherited, evoCost, traits);

    private static CardData Tamer(string id, string main = "", string? name = null, params string[] traits) => Card(id, main, CardType.Tamer, name: name, traits: traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B36-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B36-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B36-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B36-OSEC" + i, 3, dp: 1000));
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

    private static int CountSide(PlayerZones z) =>
        z.MainDeck.Count + z.Hand.Count + z.SecurityStack.Count + z.Trash.Count + z.Tamers.Count + z.UnderTamers.Count
        + z.BattleArea.Where(d => d.TamerProxyOf == null).Sum(d => 1 + d.EvolutionSources.Count);

    private const string PlayFromHand = "[On Play] You may play 1 level 3 Digimon card from your hand without paying the cost.";

    // =====================================================================
    // 1. 효과로 등장 — 한 곳 · 플레이어 제한
    // =====================================================================

    [Fact]
    public void PlayersCantPlayDigimonByEffects_CardStaysInHand_BT9_033()
    {
        var (state, engine) = NewMainPhase();
        var pillo = Digimon("B36-BT9033", 3, "[All Turns] Players can't play Digimon by effects.");
        AssertFull(pillo);
        Put(state.DummyOpponent, pillo);
        var player = Digimon("B36-PLAYER", 4, PlayFromHand);
        var me = Put(state.Player, player);
        var lv3 = Digimon("B36-LV3", 3);
        state.Player.Hand.Add(lv3);
        engine.RecalculateFieldBuffs();
        int before = CountSide(state.Player);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, player);
        Assert.Contains(lv3, state.Player.Hand);
        Assert.Single(state.Player.BattleArea);
        Assert.Equal(before, CountSide(state.Player));

        // 그 디지몬이 없어지면 다시 등장시킬 수 있다
        state.DummyOpponent.BattleArea.Clear();
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, player);
        Assert.DoesNotContain(lv3, state.Player.Hand);
        Assert.Equal(2, state.Player.BattleArea.Count);
    }

    [Fact]
    public void PlayersCantPlayDigimonByEffects_BlocksTokensButNotTamers()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B36-BT14009", 3, "[All Turns] Players can't play Digimon by effects."));
        engine.RecalculateFieldBuffs();
        var tamer = Tamer("B36-TAMER");
        state.Player.Hand.Add(tamer);
        var tamerPlayer = Digimon("B36-TP", 4, "[On Play] You may play 1 Tamer card from your hand without paying the cost.");
        var me = Put(state.Player, tamerPlayer);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, tamerPlayer);
        Assert.Contains(tamer, state.Player.Tamers);   // 테이머는 막지 않는다
        Assert.False(engine.CanPlayByEffect(state.Player, Digimon("TOKEN-X", 0), PlayOrigin.Token));
    }

    [Fact]
    public void RevealPlayRefused_CardGoesWithTheRest()
    {
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, Digimon("B36-BT9047", 3, "[All Turns] Players can't play Digimon by effects."));
        var revealer = Digimon("B36-REV", 4, "[On Play] Reveal the top 3 cards of your deck. You may play 1 level 3 Digimon card among them without paying the cost. Return the rest to the bottom of the deck.");
        AssertFull(revealer);
        var me = Put(state.Player, revealer);
        engine.RecalculateFieldBuffs();
        int deck = state.Player.MainDeck.Count, before = CountSide(state.Player);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, revealer);
        Assert.Single(state.Player.BattleArea);
        Assert.Equal(deck, state.Player.MainDeck.Count);
        Assert.Equal(before, CountSide(state.Player));
    }

    [Fact]
    public void OpponentCantPlayDigimonByEffectsUntilTheirTurnEnds_BT8_097()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B36-BT8097", "[Main] Your opponent can't play Digimon by effects until the end of their turn. Delete all of your opponent's Digimon with 6000 DP or less.", playCost: 0);
        AssertFull(option);
        Put(state.Player, Digimon("B36-HOST", 4));
        state.Player.Hand.Add(option);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.True(state.DummyOpponent.CannotPlayDigimonByEffectsUntil >= state.TurnSequence + 1);
        Assert.False(engine.CanPlayByEffect(state.DummyOpponent, Digimon("B36-X", 3), PlayOrigin.Hand));
        Assert.True(engine.CanPlayByEffect(state.Player, Digimon("B36-X", 3), PlayOrigin.Hand));   // 자신은 된다
        Assert.True(engine.CanPlayByEffect(state.DummyOpponent, Tamer("B36-T"), PlayOrigin.Hand));
    }

    [Fact]
    public void OpponentCantPlayDigimonOrTamersFromTrash_BT23_014_AndBoth_BT20_020()
    {
        var (state, engine) = NewMainPhase();
        var gallant = Digimon("B36-BT23014", 6, "[On Play] [When Digivolving] Until your opponent's turn ends, their effects can't play Digimon or Tamers from the trash.");
        AssertFull(gallant);
        var me = Put(state.Player, gallant);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, gallant);
        Assert.False(engine.CanPlayByEffect(state.DummyOpponent, Digimon("B36-X", 3), PlayOrigin.Trash));
        Assert.False(engine.CanPlayByEffect(state.DummyOpponent, Tamer("B36-T"), PlayOrigin.Trash));
        Assert.True(engine.CanPlayByEffect(state.DummyOpponent, Digimon("B36-X", 3), PlayOrigin.Hand));

        var imperial = Digimon("B36-BT20020", 6, "[When Digivolving] Your opponent can't play Digimon or Tamers by effects until the end of their turn.");
        AssertFull(imperial);
        var me2 = Put(state.Player, imperial);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me2, imperial);
        Assert.False(engine.CanPlayByEffect(state.DummyOpponent, Tamer("B36-T"), PlayOrigin.Hand));
    }

    [Fact]
    public void PlayersCantReducePlayCosts_BT8_071()
    {
        var (state, engine) = NewMainPhase();
        var reducer = Digimon("B36-RED", 5, "When this card would be played, if you have a Tamer with [Tai Kamiya] in its name, reduce the play cost by 3.", playCost: 7);
        state.Player.Tamers.Add(Tamer("B36-TAI", name: "Tai Kamiya"));
        Assert.Equal(4, engine.PreviewPlayCostWithOptions(state.Player, reducer));
        var psyche = Digimon("B36-BT8071", 3, "[All Turns] Players can't reduce play costs.");
        AssertFull(psyche);
        Put(state.DummyOpponent, psyche);
        engine.RecalculateFieldBuffs();
        Assert.Equal(7, engine.PreviewPlayCostWithOptions(state.Player, reducer));
        Assert.Equal(7, CardScripts.EffectivePlayCost(state.Player, reducer, state.DummyOpponent));

        // 효과의 "with the play cost reduced by 3"도 원래 코스트를 낸다
        var payer = Digimon("B36-PAY", 4, "[On Play] Play 1 level 5 Digimon card from your hand with the play cost reduced by 3.");
        AssertFull(payer);
        var plain = Digimon("B36-L5", 5, playCost: 5);
        state.Player.Hand.Add(plain);
        var me = Put(state.Player, payer);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, payer);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, plain));
        Assert.Equal(mem - 5, state.MemoryGauge);
    }

    [Fact]
    public void OpponentCantUseOptions_EX1_072()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B36-EX1072", "[Main] Your opponent can't use Option cards until the end of their next turn.", playCost: 0, color: CardColor.White);
        AssertFull(option);
        Put(state.Player, Card("B36-W", "", CardType.Digimon, 4, color: CardColor.White));
        state.Player.Hand.Add(option);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.True(EffectRules.OptionUseBlocked(state, state.DummyOpponent));
        Assert.False(EffectRules.OptionUseBlocked(state, state.Player));
        state.DummyOpponent.Hand.Add(Card("B36-OPT", "[Main] Gain 1 memory.", playCost: 0));
        Assert.Null(new SmartAiPolicy().ChooseOption(engine, 5, beforeAttacks: true));
    }

    [Fact]
    public void WhileAllSuspended_OpponentCantUseOptions_BT8_057()
    {
        var (state, engine) = NewMainPhase();
        var shiva = Digimon("B36-BT8057", 6, "[Opponent's Turn] While all of your Digimon are suspended, your opponent can't use Option cards.");
        AssertFull(shiva);
        var me = Put(state.DummyOpponent, shiva, suspended: true);
        me.IsOwnersTurn = false;   // 플레이어의 턴 = 그 디지몬 주인(상대)의 상대 턴
        engine.RecalculateFieldBuffs();
        Assert.True(EffectRules.OptionUseBlocked(state, state.Player));
        var option = Card("B36-OPT", "[Main] Gain 1 memory.", playCost: 0);
        Put(state.Player, Card("B36-Y", "", CardType.Digimon, 4));
        state.Player.Hand.Add(option);
        Assert.False(engine.PlayCard(option).CanExecute);
        me.IsSuspended = false;
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.OptionUseBlocked(state, state.Player));
    }

    // =====================================================================
    // 3. 두 술어 보호 · 공개 + 상한 · 비용
    // =====================================================================

    [Fact]
    public void DeDigivolveImmune_AndDpPerFaceDown_BT22_060()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-BT22060", 4, "[On Play] [When Digivolving] Until your opponent's turn ends, their ＜De-Digivolve＞ effects don't affect this Digimon, and it gets +1000 DP for each of its face-down digivolution cards.", dp: 4000);
        AssertFull(card);
        var me = Put(state.Player, card, sources: Digimon("B36-SRC", 3));
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B36-FD", 3));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        engine.RecalculateFieldBuffs();
        Assert.Equal(5000, me.GetCurrentDP());
        var dedigi = Digimon("B36-DEDIGI", 5, "[On Play] ＜De-Digivolve 1＞ 1 of your opponent's Digimon.");
        var foe = Put(state.DummyOpponent, dedigi);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, dedigi);
        Assert.Same(card, me.TopCard);
    }

    [Fact]
    public void UnaffectedByOpponentDigimonEffects_AndPlusDp_BT26_057()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-BT26057", 5, "[When Digivolving] By trashing the bottom face-down card under card from under any of your Tamers, until your opponent's turn ends, their Digimon effects don't affect this Digimon, and it gets +3000 DP.", dp: 6000);
        AssertFull(card);
        var me = Put(state.Player, card);
        state.Player.Tamers.Add(Tamer("B36-T"));
        state.Player.AddUnderTamerFaceDown(Digimon("B36-U", 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        engine.RecalculateFieldBuffs();
        Assert.Equal(9000, me.GetCurrentDP());
        Assert.True(CardScripts.HasKeyword(me, Keyword.UnaffectedByOpponentDigimonEffects));
        Assert.Empty(state.Player.UnderTamers);
    }

    [Fact]
    public void DeleteOwnAndAllOpponentLowestPlayCost_BT26_055()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-BT26055", 5, "[On Play] You may delete 1 of your Digimon with the [Ver.3] trait and all of your opponent's Digimon with the lowest play cost.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var ver3 = Put(state.Player, Digimon("B36-V3", 3, traits: "Ver.3"));
        var cheap1 = Put(state.DummyOpponent, Digimon("B36-C1", 3, playCost: 2));
        var cheap2 = Put(state.DummyOpponent, Digimon("B36-C2", 3, playCost: 2));
        var big = Put(state.DummyOpponent, Digimon("B36-BIG", 5, playCost: 7));
        engine.PlayerChoices = new AcceptAll();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(ver3, state.Player.BattleArea);
        Assert.DoesNotContain(cheap1, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(cheap2, state.DummyOpponent.BattleArea);
        Assert.Contains(big, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void PlayCostMaximumPerFaceDown_BT26_077()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-BT26077", 5, "[On Play] You may play 1 play cost of 6 or lower [Ver.3] trait Digimon card from your trash without paying the cost. For each of this Digimon's face-down digivolution cards, add 1 to the play cost maximum.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var seven = Digimon("B36-P7", 5, playCost: 7, traits: "Ver.3");
        state.Player.Trash.Add(seven);
        engine.PlayerChoices = new AcceptAll();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(seven, state.Player.Trash);   // 상한 6
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B36-FD", 3));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(seven, state.Player.Trash);   // 상한 7
    }

    [Fact]
    public void RevealWithPlayCostMaximum_EX9_053()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-EX9053", 4, "[On Play] [When Digivolving] Reveal the top 3 cards of your deck. you may play 1 play cost 4 or lower [DM] trait card among them without paying the cost. For each of this Digimon's face-down digivolution cards, add 1 to this effect's play cost maximum. Return the rest to the bottom of the deck.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B36-FD", 3));
        var five = Digimon("B36-DM5", 4, playCost: 5, traits: "DM");
        state.Player.MainDeck.Insert(0, five);
        engine.PlayerChoices = new AcceptAll();
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, five));
    }

    [Fact]
    public void TrashOrPlaceFaceDownUnderNamedTamer_BT26_072()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-BT26072", 5, "[On Play] [When Digivolving] By trashing 1 card in your hand or placing it face down under any of your [Keenan Crier]s, delete 1 of your opponent's level 4 or lower Digimon.");
        AssertFull(card);
        var me = Put(state.Player, card);
        state.Player.Tamers.Add(Tamer("B36-KEENAN", name: "Keenan Crier"));
        state.Player.Hand.Add(Digimon("B36-H", 3));
        var foe = Put(state.DummyOpponent, Digimon("B36-F", 4));
        engine.PlayerChoices = new AcceptAll();   // 예 = 앞 비용 (파기)
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
        Assert.Empty(state.Player.Hand);
    }

    [Fact]
    public void PlaceAsSourceOrFaceDownUnderTamer_BT27_020()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-BT27020", 4, "[On Play] By placing 1 level 4 or lower [Aquatic] or [DATA SQUAD] trait Digimon card from your hand as any of your Digimon's bottom digivolution card, or face down under any of your [DATA SQUAD] trait Tamers, ＜Draw 1＞ (Draw 1 card from your deck.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        var aqua = Digimon("B36-AQ", 3, traits: "Aquatic");
        state.Player.Hand.Add(aqua);
        int deck = state.Player.MainDeck.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(aqua, state.Player.Hand);
        Assert.Equal(deck - 1, state.Player.MainDeck.Count);
    }

    [Fact]
    public void SuspendNamedTamerCost_Rhythm_BT17_048()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-RHY", 5, inherited: "[When Attacking] [Once Per Turn] By suspending 1 of your [Rhythm], unsuspend this Digimon.");
        var host = Digimon("B36-HOST", 6);
        var me = Put(state.Player, host, suspended: true, card);
        var rhythm = Tamer("B36-RHYTHM", name: "Rhythm");
        state.Player.Tamers.Add(rhythm);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, host);
        Assert.False(me.IsSuspended);
        Assert.False(state.Player.HasActiveTamer(rhythm));
    }

    // =====================================================================
    // 4. 진화할 때 비용을 내는 진화 코스트 감소
    // =====================================================================

    [Fact]
    public void DigivolveCostReducedPerTamerSuspended_BothSides_BT17_048()
    {
        var (state, engine) = NewMainPhase();
        var argo = Digimon("B36-BT17048", 5, "When digivolving into this card, by suspending up to 5 Tamers, for each Tamer suspended by this effect, reduce the digivolution cost by 1.\n[Opponent's Turn] None of your opponent's Tamers can unsuspend.", evoCost: 4);
        AssertFull(argo);
        var baseD = Put(state.Player, Digimon("B36-BASE", 4));
        state.Player.Tamers.Add(Tamer("B36-MYT"));
        var oppT = Tamer("B36-OPT");
        state.DummyOpponent.Tamers.Add(oppT);
        state.Player.Hand.Add(argo);
        int mem = state.MemoryGauge;
        Assert.True(engine.EvolveDigimon(baseD, argo).CanExecute);
        Assert.Equal(mem - 2, state.MemoryGauge);   // 진화 코스트 4 - 테이머 2명
        Assert.False(state.DummyOpponent.HasActiveTamer(oppT));   // 상대의 테이머도 고를 수 있다 (Q&A)
        // 상대의 턴: 상대의 테이머는 액티브가 되지 않는다
        baseD.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.True(engine.TamerUnsuspendLocked(state.DummyOpponent, oppT));
    }

    [Fact]
    public void DigivolveCostReducedPerReturnedCard_EX3_054()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-EX3054", 5, "When you would digivolve into this card, by returning up to 5 cards with the [D-Brigade] trait from your trash to the top of your deck, reduce the digivolution cost by 1 for each returned card.", evoCost: 4);
        AssertFull(card);
        var baseD = Put(state.Player, Digimon("B36-BASE", 4));
        for (int i = 0; i < 3; i++) state.Player.Trash.Add(Digimon("B36-DB" + i, 3, traits: "D-Brigade"));
        state.Player.Hand.Add(card);
        int mem = state.MemoryGauge;
        Assert.True(engine.EvolveDigimon(baseD, card).CanExecute);
        Assert.Equal(mem - 1, state.MemoryGauge);   // 4 - 3
        Assert.Empty(state.Player.Trash);
    }

    // =====================================================================
    // 5. Lv.3/4/5 1장씩
    // =====================================================================

    [Fact]
    public void PlayOneEachOfLevel345FromSources_BT22_028()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B36-BT22028", 6, "[When Digivolving] You may play 1 each of level 3, level 4 and level 5 Digimon cards with [Aqua] or [Sea Animal] in any of their traits from this Digimon's digivolution cards without paying the cost.");
        AssertFull(card);
        var l3 = Digimon("B36-A3", 3, traits: "Aquatic");
        var l4 = Digimon("B36-A4", 4, traits: "Sea Animal");
        var l5 = Digimon("B36-A5", 5, traits: "Aqua Beast");
        var l5b = Digimon("B36-A5B", 5, traits: "Aquatic");
        var me = Put(state.Player, card, false, l3, l4, l5, l5b);
        engine.PlayerChoices = new AcceptAll();
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Equal(4, state.Player.BattleArea.Count);   // 1장씩만
        Assert.Single(me.EvolutionSources);
    }

    [Fact]
    public void PlaceOneEachAndDigivolveWithReducedCost_EX13_071()
    {
        var (state, engine) = NewMainPhase();
        var card = Tamer("B36-EX13071", "[Main] [Once Per Turn] By trashing 3 bottom face-down cards from under any of your Tamers and placing 1 each of level 4 and level 5 [Holy Beast] trait yellow Digimon cards from your trash as 1 of your [Kudamon] 's bottom digivolution cards, it may digivolve into [Kentaurosmon] in the hand or trash, ignoring level and with the cost reduced by 1.");
        AssertFull(card);
        state.Player.Tamers.Add(card);
        for (int i = 0; i < 3; i++) state.Player.AddUnderTamerFaceDown(Digimon("B36-U" + i, 3));
        var kuda = Put(state.Player, Digimon("B36-KUDA", 3, name: "Kudamon"));
        state.Player.Trash.Add(Digimon("B36-HB4", 4, traits: "Holy Beast"));
        state.Player.Trash.Add(Digimon("B36-HB5", 5, traits: "Holy Beast"));
        var kentauros = Digimon("B36-KENT", 6, name: "Kentaurosmon", evoCost: 3);
        state.Player.Hand.Add(kentauros);
        engine.PlayerChoices = new AcceptAll();
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, card, includeInherited: false);
        Assert.True(ReferenceEquals(kentauros, kuda.TopCard), string.Join(" / ", state.ActionLogs));
        Assert.Equal(3, kuda.EvolutionSources.Count);
        Assert.Equal(mem - 2, state.MemoryGauge);
        Assert.Empty(state.Player.UnderTamers);
    }

    // =====================================================================
    // 2. 테이머를 디지몬으로도 취급
    // =====================================================================

    private const string Agumon = "[Main] [Once Per Turn] For the turn, 1 of your [Marcus Damon]s is also treated as a 3000 DP Digimon and can't digivolve.";

    [Fact]
    public void MarcusTreatedAsDigimon_AttacksAndSuspendsTamer_BT13_008()
    {
        var (state, engine) = NewMainPhase();
        var agumon = Digimon("B36-BT13008", 3, Agumon, name: "Agumon");
        AssertFull(agumon);
        var me = Put(state.Player, agumon);
        var watcher = Digimon("B36-WATCH", 5, "[Your Turn] When one of your red or yellow Tamers becomes suspended, delete 1 of your opponent's Digimon with 3000 DP or less.");
        Put(state.Player, watcher);
        var marcus = Tamer("B36-MARCUS", name: "Marcus Damon");
        state.Player.Tamers.Add(marcus);
        var small = Put(state.DummyOpponent, Digimon("B36-SMALL", 3, dp: 2000));
        engine.RecalculateFieldBuffs();

        Assert.True(engine.ActivateMainAbility(me, agumon).CanExecute);
        var proxy = state.Player.BattleArea.Single(d => d.TamerProxyOf != null);
        Assert.Same(marcus, proxy.TamerProxyOf);
        Assert.Equal(3000, proxy.GetCurrentDP());
        Assert.Contains(marcus, state.Player.Tamers);   // 테이머 존에도 그대로
        Assert.True(EffectRules.IsDigivolveLocked(state, proxy));

        int sec = state.DummyOpponent.SecurityStack.Count;
        Assert.True(engine.AttackSecurityOrDirect(proxy).CanExecute);
        Assert.True(proxy.IsSuspended || !state.Player.BattleArea.Contains(proxy));
        Assert.False(state.Player.HasActiveTamer(marcus) && state.Player.Tamers.Contains(marcus));   // 테이머도 레스트
        Assert.DoesNotContain(small, state.DummyOpponent.BattleArea);   // "자신의 테이머가 레스트했을 때"
        Assert.True(state.DummyOpponent.SecurityStack.Count < sec || !state.Player.BattleArea.Contains(proxy));
    }

    [Fact]
    public void MarcusAsDigimon_DeletedInBattle_GoesToTrash()
    {
        var (state, engine) = NewMainPhase();
        var agumon = Digimon("B36-AG", 3, Agumon, name: "Agumon");
        var me = Put(state.Player, agumon);
        var marcus = Tamer("B36-MARCUS", name: "Marcus Damon");
        state.Player.Tamers.Add(marcus);
        var big = Put(state.DummyOpponent, Digimon("B36-BIG", 5, dp: 8000), suspended: true);
        int before = CountSide(state.Player);
        Assert.True(engine.ActivateMainAbility(me, agumon).CanExecute);
        var proxy = state.Player.BattleArea.Single(d => d.TamerProxyOf != null);
        engine.AttackOpponentDigimon(proxy, big);
        engine.RecalculateFieldBuffs();
        Assert.DoesNotContain(proxy, state.Player.BattleArea);
        Assert.DoesNotContain(marcus, state.Player.Tamers);
        var trashed = state.Player.Trash.Single(c => c.CardId == "B36-MARCUS");
        Assert.Equal(CardType.Tamer, trashed.Type);   // 원래 카드 정보로
        Assert.False(trashed.IsTamerProxy);
        Assert.Equal(before, CountSide(state.Player));
    }

    [Fact]
    public void MarcusAsDigimon_EndsAtTurnEnd_TamerStays()
    {
        var (state, engine) = NewMainPhase();
        var agumon = Digimon("B36-AG", 3, Agumon, name: "Agumon");
        var me = Put(state.Player, agumon);
        var marcus = Tamer("B36-MARCUS", name: "Marcus Damon");
        state.Player.Tamers.Add(marcus);
        Assert.True(engine.ActivateMainAbility(me, agumon).CanExecute);
        Assert.Contains(state.Player.BattleArea, d => d.TamerProxyOf != null);
        engine.ExpireTamerProxies();
        Assert.DoesNotContain(state.Player.BattleArea, d => d.TamerProxyOf != null);
        Assert.Contains(marcus, state.Player.Tamers);
    }

    [Fact]
    public void ThisTamerTreatedAsDigimon_BlockerUntilOpponentsTurn_BT17_087()
    {
        var (state, engine) = NewMainPhase();
        var marcus = Tamer("B36-BT17087", "[On Play] 1 of your [Marcus Damon] is also treated as a 3000 DP Digimon that can't digivolve and gains ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn.", name: "Marcus Damon");
        AssertFull(marcus);
        state.Player.Tamers.Add(marcus);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, marcus, includeInherited: false);
        var proxy = state.Player.BattleArea.Single(d => d.TamerProxyOf != null);
        Assert.True(proxy.HasBlocker);
        engine.ExpireTamerProxies();   // 이 턴 종료 — 상대의 턴 종료까지라 남는다
        Assert.Contains(proxy, state.Player.BattleArea);
    }

    [Fact]
    public void MarcusPlayedThisTurn_CantAttackAsDigimon()
    {
        var (state, engine) = NewMainPhase();
        var agumon = Digimon("B36-AG", 3, Agumon, name: "Agumon");
        var me = Put(state.Player, agumon);
        var marcus = Tamer("B36-MARCUS", name: "Marcus Damon");
        state.Player.Hand.Add(marcus);
        Assert.True(engine.PlayCard(marcus).CanExecute);
        Assert.True(engine.ActivateMainAbility(me, agumon).CanExecute);
        var proxy = state.Player.BattleArea.Single(d => d.TamerProxyOf != null);
        Assert.False(engine.AttackSecurityOrDirect(proxy).CanExecute);   // 공식 Q&A: 등장한 턴의 테이머는 어택할 수 없다
    }

    [Fact]
    public void AllMarcusAreDigimonOnYourTurn_Static_BT25_104()
    {
        var (state, engine) = NewMainPhase();
        var shine = Digimon("B36-BT25104", 6, "[Your Turn] All of your [Marcus Damon]s are also treated as 12000 DP Digimon and gain ＜Rush＞ (This Digimon can attack the turn it comes into play.)");
        AssertFull(shine);
        var me = Put(state.Player, shine);
        var marcus = Tamer("B36-MARCUS", name: "Marcus Damon");
        state.Player.Tamers.Add(marcus);
        engine.RecalculateFieldBuffs();
        engine.RecalculateFieldBuffs();
        var proxy = state.Player.BattleArea.Single(d => d.TamerProxyOf != null);
        Assert.Equal(12000, proxy.GetCurrentDP());
        Assert.True(CardScripts.HasKeyword(proxy, Keyword.Rush));
        me.IsOwnersTurn = false;   // 상대의 턴
        engine.RecalculateFieldBuffs();
        engine.RecalculateFieldBuffs();
        Assert.DoesNotContain(state.Player.BattleArea, d => d.TamerProxyOf != null);
        Assert.Contains(marcus, state.Player.Tamers);
    }

    [Fact]
    public void TamerSuspendedByCost_ProxyFollows()
    {
        var (state, engine) = NewMainPhase();
        var agumon = Digimon("B36-AG", 3, Agumon, name: "Agumon");
        var me = Put(state.Player, agumon);
        var marcus = Tamer("B36-MARCUS", name: "Marcus Damon");
        state.Player.Tamers.Add(marcus);
        Assert.True(engine.ActivateMainAbility(me, agumon).CanExecute);
        var proxy = state.Player.BattleArea.Single(d => d.TamerProxyOf != null);
        engine.SuspendTamer(state.Player, marcus);
        Assert.True(proxy.IsSuspended);
        engine.Unsuspend(proxy);
        Assert.True(state.Player.HasActiveTamer(marcus));
    }

    // =====================================================================
    // 한글 · 용어집
    // =====================================================================

    [Theory]
    [InlineData("[All Turns] Players can't play Digimon by effects.", "【서로의 턴】 서로는 효과로 디지몬을 등장시킬 수 없다.")]
    [InlineData("[All Turns] Players can't reduce play costs.", "【서로의 턴】 서로는 지불하는 등장 코스트를 마이너스할 수 없다.")]
    [InlineData("[Main] Your opponent can't use Option cards until the end of their next turn.", "【메인】 다음 상대의 턴 종료 시까지, 상대는 옵션 카드를 사용할 수 없다.")]
    [InlineData("[On Play] [When Digivolving] Until your opponent's turn ends, their effects can't play Digimon or Tamers from the trash.", "【등장 시】 【진화 시】 상대의 턴 종료 시까지, 상대는 트래시에서, 디지몬과 테이머를 효과로 등장시킬 수 없다.")]
    [InlineData("[Opponent's Turn] None of your opponent's Tamers can unsuspend.", "【상대의 턴】 상대의 테이머 전부는 액티브가 되지 않는다.")]
    [InlineData("When digivolving into this card, by suspending up to 5 Tamers, for each Tamer suspended by this effect, reduce the digivolution cost by 1.", "이 카드로 진화할 때, 테이머 5명까지를 레스트시키는 것으로, 이 효과로 레스트한 1명마다 지불하는 진화 코스트 -1.")]
    public void KoreanRendering(string en, string ko) => Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));

    [Fact]
    public void KoreanRendering_MarcusAndLevels()
    {
        Assert.Contains("디지몬・DP 3000으로도 취급하며, 《블로커》를 얻고, 진화할 수 없다",
            KoreanEffectRenderer.RenderClause("[Start of Your Main Phase] [When Digivolving] Until the end of your opponent's turn, 1 of your [Marcus Damon]s is also treated as a 3000 DP Digimon, can't digivolve, and gains ＜Blocker＞."));
        Assert.Contains("Lv.3과 Lv.4와 Lv.5의 디지몬 카드 1장씩을 코스트를 지불하지 않고 등장시킬 수 있다",
            KoreanEffectRenderer.RenderClause("[When Digivolving] You may play 1 each of level 3, level 4 and level 5 Digimon cards with [Aqua] or [Sea Animal] in any of their traits from this Digimon's digivolution cards without paying the cost."));
        Assert.Contains("상대의 《퇴화》의 효과를 받지 않고",
            KoreanEffectRenderer.RenderClause("[On Play] Until your opponent's turn ends, their ＜De-Digivolve＞ effects don't affect this Digimon, and it gets +1000 DP for each of its face-down digivolution cards."));
    }

    [Fact]
    public void Glossary_NamePiecesFromOfficialKorean()
    {
        string? ko = KoreanEffectRenderer.RenderClause("[On Play] Reveal the top 3 cards of your deck. Add 1 Digimon card with [Vee] in its name among them to the hand. Return the rest to the bottom of the deck.");
        Assert.NotNull(ko);
        if (KoreanCardData.Count > 0)   // 공식 한글 데이터가 있을 때만 (개인 PC)
            Assert.Contains("「브이」", ko);
        string? dex = KoreanEffectRenderer.RenderClause("[Main] Trash 1 card with [Dex] or [DeathX] in its name in your hand.");
        if (dex != null && KoreanCardData.Count > 0)
            Assert.DoesNotContain("「데크스」/「데크스」", dex);
    }
}
