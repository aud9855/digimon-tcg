using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.32.0 미지원 문장 6차 묶음: "떠나려 할 때"의 다른 표기 · 막기 · 비용 (Lv.이 같은 진화원 2장 등) · 효과를 준다 (따옴표 효과 · 강제 어택) ·
/// 사건 (등장한 곳 · 효과로 레스트 · 상대 진화원 파기 · 배틀에서 이김 등) · 테이머의 진화 코스트 감소 · 두 동작 문장 · 시큐리티 어택 셈 ·
/// [육성] 두 문장 등장 코스트 감소 · 코스트 감소 조건 · 공개한 카드로 진화 / 옵션 사용 · 대상 표기
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch32Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

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
            state.Player.MainDeck.Add(Digimon("B32-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B32-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B32-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B32-OSEC" + i, 3));
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
        if (!state.Player.BattleArea.Any(d => d.TopCard.AllColors.Contains(CardColor.Blue)) && !state.Player.Tamers.Any(t => t.AllColors.Contains(CardColor.Blue)))
            state.Player.Tamers.Add(Tamer("B32-COLOR"));
        state.Player.Hand.Add(option);
        var result = engine.PlayCard(option);
        Assert.True(result.CanExecute, result.Reason);
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    // =====================================================================
    // 떠나려 할 때 · 소멸할 때
    // =====================================================================

    private const string YellowData = "[All Turns] [Once Per Turn] When this yellow Digimon with the [Data] or [Witchelny] trait would leave the battle area by your opponent's effects, by trashing your top security card, it doesn't leave.";

    [Fact]
    public void Leave_YellowWithTrait_ByOpponentEffect_TrashesSecurityAndStays_BT18_030()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B32-YDATA", 4, inherited: YellowData, color: CardColor.Yellow, traits: "Data");
        var host = Digimon("B32-YHOST", 5, color: CardColor.Yellow, traits: "Data");
        AssertFull(card);
        var d = Put(state.Player, host, sources: card);

        engine.ReturnToHand(state.Player, d);   // 효과 주인이 없다 = 자신의 효과가 아님 (상대의 효과로 본다)

        Assert.Contains(d, state.Player.BattleArea);
        Assert.Equal(4, state.Player.SecurityStack.Count);
        // [턴에 1회] — 두 번째는 막지 않는다
        engine.ReturnToHand(state.Player, d);
        Assert.DoesNotContain(d, state.Player.BattleArea);
    }

    [Fact]
    public void Leave_YellowCondition_NotYellow_DoesNotPrevent()
    {
        var (state, engine) = NewMainPhase();
        var host = Digimon("B32-BHOST", 5, color: CardColor.Blue, traits: "Data");
        var d = Put(state.Player, host, sources: Digimon("B32-YDATA2", 4, inherited: YellowData, color: CardColor.Yellow, traits: "Data"));

        engine.ReturnToHand(state.Player, d);

        Assert.DoesNotContain(d, state.Player.BattleArea);
        Assert.Equal(5, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void Leave_ByAnOpponentsEffect_OwnEffectDoesNotTrigger()
    {
        var (state, engine) = NewMainPhase();
        var guard = Digimon("B32-GUARD", 5, "[All Turns] When this Digimon would leave the battle area by an opponent's effect, by trashing the top card of your security stack, prevent it.");
        AssertFull(guard);
        var d = Put(state.Player, guard);

        Use(state, engine, Option("B32-SELFBOUNCE", "[Main] Return 1 of your Digimon to the hand."));   // 자신의 효과

        Assert.DoesNotContain(d, state.Player.BattleArea);
        Assert.Equal(5, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void DeletedOrReturned_PreventsHandAndDeletion_ButNotSecurity_EX3_013()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B32-EX3013", 6, "[All Turns] When this Digimon would be deleted or returned to your hand or deck, you may trash 2 level 5 cards in this Digimon's digivolution cards to prevent it from leaving play.");
        AssertFull(card);
        var d = Put(state.Player, card, sources: new[] { Digimon("B32-L5A", 5), Digimon("B32-L5B", 5), Digimon("B32-L5C", 5), Digimon("B32-L5D", 5) });

        engine.ReturnToHand(state.Player, d);
        Assert.Contains(d, state.Player.BattleArea);
        Assert.Equal(2, d.EvolutionSources.Count);

        engine.DeleteFrom(state.Player, d, GoldfishEngine.DeletionCause.Battle);   // 단서 없음 = 배틀 소멸도
        Assert.Contains(d, state.Player.BattleArea);
        Assert.Empty(d.EvolutionSources);

        var other = Put(state.Player, Digimon("B32-EX3013B", 6, card.EffectText), sources: new[] { Digimon("B32-L5E", 5), Digimon("B32-L5F", 5) });
        engine.PlaceOnSecurity(state.Player, other);   // 시큐리티로 가는 것은 "소멸 · 패 · 덱"이 아니다
        Assert.DoesNotContain(other, state.Player.BattleArea);
    }

    [Fact]
    public void DeletedOrReturnedByEffect_BattleDeletionIsNotPrevented_BT11_062()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B32-BT11062", 4, inherited: "[All Turns] When this Digimon with [Greymon] or [Omnimon] in its name would be deleted or returned to your hand or deck by an effect, by placing 1 [X Antibody] from this Digimon's digivolution cards at the bottom of its owner's deck, prevent this Digimon from leaving.");
        AssertFull(src);
        var xab = Digimon("B32-XAB", 3, name: "X Antibody");
        var d = Put(state.Player, Digimon("B32-METALGREY", 5, name: "MetalGreymon"), sources: new[] { src, xab });

        engine.DeleteFrom(state.Player, d, GoldfishEngine.DeletionCause.Battle);
        Assert.DoesNotContain(d, state.Player.BattleArea);   // 공식 한글: "효과로 소멸할 때, 또는 패/덱으로 되돌아갈 때"

        var d2 = Put(state.Player, Digimon("B32-METALGREY2", 5, name: "MetalGreymon"), sources: new[] { src, xab });
        engine.DeleteFrom(state.Player, d2);
        Assert.Contains(d2, state.Player.BattleArea);
        Assert.Equal(xab, state.Player.MainDeck[^1]);
    }

    [Fact]
    public void SameLevelPair_TrashesTwoSourcesOfTheSameLevel_QnaBT9_012()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B32-BT9012", 4, inherited: "[All Turns] When this Digimon has [Greymon] or [Omnimon] in its name and an effect would delete it or return it to your hand or deck, you may trash 2 cards of the same level in this Digimon's digivolution cards to prevent it from leaving play.");
        AssertFull(src);
        var l5a = Digimon("B32-P5A", 5); var l5b = Digimon("B32-P5B", 5); var l4 = Digimon("B32-P4", 4);
        var d = Put(state.Player, Digimon("B32-WARGREY", 6, name: "WarGreymon"), sources: new[] { src, l5a, l5b, l4 });

        engine.ReturnToHand(state.Player, d);

        Assert.Contains(d, state.Player.BattleArea);
        Assert.Contains(l5a, state.Player.Trash);   // 공식 Q&A BT9-012: 이 카드와 같은 Lv.이 아니라 진화원 중 서로 Lv.이 같은 2장
        Assert.Contains(l5b, state.Player.Trash);
        Assert.DoesNotContain(l4, state.Player.Trash);

        engine.ReturnToHand(state.Player, d);   // 이제 같은 Lv. 2장이 없다 (Lv.4 · 이 카드 Lv.4 → 2장!) — src(Lv.4)와 l4(Lv.4)
        Assert.Contains(d, state.Player.BattleArea);
        engine.ReturnToHand(state.Player, d);
        Assert.DoesNotContain(d, state.Player.BattleArea);
    }

    [Fact]
    public void OpponentsEffectWouldDelete_PlaysFromSources_BT17_050()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B32-BT17050", 5, inherited: "[All Turns] When an opponent's effect would delete this Digimon, you may play 1 [Parasitemon] from this Digimon's digivolution cards without paying the cost.");
        AssertFull(src);
        var parasite = Digimon("B32-PARA", 5, name: "Parasitemon");
        var d = Put(state.Player, Digimon("B32-HOST17", 6), sources: new[] { src, parasite });

        engine.DeleteFrom(state.Player, d);

        Assert.Contains(state.Player.BattleArea, x => x.TopCard == parasite);
    }

    [Fact]
    public void AllyListSubject_DeletesThisToProtectAnother_BT15_072()
    {
        var (state, engine) = NewMainPhase();
        var guard = Digimon("B32-BT15072", 5, "[All Turns] When one of your [Apocalymon] or Digimon with the [Dark Masters] trait would leave the battle area other than by one of your effects, by deleting this Digimon, prevent 1 of those Digimon from leaving.");
        AssertFull(guard);
        engine.PlayerChoices = new AcceptAll();   // 비용 (이 디지몬을 소멸시킨다)을 낸다
        var g = Put(state.Player, guard);
        var dm = Put(state.Player, Digimon("B32-DM", 6, traits: "Dark Masters"));

        engine.ReturnToHand(state.Player, dm);

        Assert.Contains(dm, state.Player.BattleArea);
        Assert.DoesNotContain(g, state.Player.BattleArea);
        Assert.Contains(guard, state.Player.Trash);
    }

    [Fact]
    public void LeaveCosts_BottomSecurity_BothTopSecurity_ReturnSourcesToDeck()
    {
        var (state, engine) = NewMainPhase();
        var bottom = Put(state.Player, Digimon("B32-BT18082", 6, "[All Turns] [Once Per Turn] When this Digimon would leave the battle area, by trashing the bottom card of your security stack, it doesn't leave."));
        AssertFull(bottom.TopCard);
        var lastSec = state.Player.SecurityStack[^1];
        engine.ReturnToHand(state.Player, bottom);
        Assert.Contains(bottom, state.Player.BattleArea);
        Assert.Contains(lastSec, state.Player.Trash);

        var vem = Enumerable.Range(0, 4).Select(i => Digimon("B32-VEM" + i, 3, name: "Vemmon")).ToArray();
        var snatch = Put(state.Player, Digimon("B32-BT21062", 6, "[All Turns] When this Digimon would leave the battle area, by returning 4 [Vemmon] from its digivolution cards to the bottom of the deck, it doesn't leave."), sources: vem);
        AssertFull(snatch.TopCard);
        engine.DeleteFrom(state.Player, snatch);
        Assert.Contains(snatch, state.Player.BattleArea);
        Assert.Empty(snatch.EvolutionSources);
        Assert.All(vem, v => Assert.Contains(v, state.Player.MainDeck));
    }

    // =====================================================================
    // 효과를 준다 (따옴표) · 강제 어택
    // =====================================================================

    [Fact]
    public void Grant_OnDeletionLoseMemory_ToOpponentsDigimon_BT15_068()
    {
        var (state, engine) = NewMainPhase();
        var target = Put(state.DummyOpponent, Digimon("B32-OPP1", 4));
        Use(state, engine, Option("B32-GRANT1", "[Main] Until the end of your opponent's turn, 1 of their Digimon gains \"[On Deletion] Lose 1 memory.\""));
        Assert.Single(target.GrantedEffects);
        int before = state.MemoryGauge;

        Use(state, engine, Option("B32-DEL1", "[Main] Delete 1 of your opponent's Digimon."));

        Assert.DoesNotContain(target, state.DummyOpponent.BattleArea);
        Assert.Equal(before + 1, state.MemoryGauge);   // 상대가 메모리 -1 = 내 쪽 +1
    }

    [Fact]
    public void Grant_ExpiresAfterDuration_AndGivesKeywordsAndDp()
    {
        var (state, engine) = NewMainPhase();
        var target = Put(state.DummyOpponent, Digimon("B32-OPP2", 5));
        var option = Option("B32-GRANT2", "[Main] Give 1 of your opponent's Digimon ＜Collision＞ and \"[Start of Your Main Phase] This Digimon attacks.\" until their turn ends.");
        Use(state, engine, option);

        var (effect, until, granter) = Assert.Single(target.GrantedEffects);
        Assert.Equal(state.TurnSequence + 1, until);   // 상대의 턴 종료 시까지
        Assert.Same(state.Player, granter);
        Assert.Contains(target.TimedKeywords, k => k.Keyword == Keyword.Collision);
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(effect).Level);
    }

    [Fact]
    public void Grant_ForcedAttack_AiDigimonAttacksAtStartOfMainPhase()
    {
        var (state, engine) = NewMainPhase();
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        var target = Put(state.DummyOpponent, Digimon("B32-OPP3", 4, dp: 4000));
        Use(state, engine, Option("B32-GRANT3", "[Main] Until the end of your opponent's turn, 1 of your opponent's Digimon gains \"[Start of Your Main Phase] This Digimon attacks.\""));
        int securityBefore = state.Player.SecurityStack.Count;

        engine.PassTurn();
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);

        Assert.Contains(state.ActionLogs, l => l.Contains("효과로 어택합니다"));
        Assert.True(state.Player.SecurityStack.Count < securityBefore || target.IsSuspended || !state.DummyOpponent.BattleArea.Contains(target));
    }

    [Fact]
    public void Grant_SelfOnDeletion_GainMemory_BT6_104()
    {
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Digimon("B32-MINE", 4));
        Use(state, engine, Option("B32-GRANT4", "[Main] 1 of your Digimon gains \"[On Deletion] Gain 2 memory\" until the end of your opponent's next turn."));
        Assert.Single(mine.GrantedEffects);
        int before = state.MemoryGauge;

        engine.DeleteFrom(state.Player, mine);

        Assert.Equal(before + 2, state.MemoryGauge);
    }

    [Fact]
    public void Grant_ThenSentenceAfterQuote_IsReadAsSeparateSentence_BT12_105()
    {
        var card = Option("B32-BT12105", "[Main] Until the end of your opponent's turn, 1 of your opponent's Digimon gains \"[On Deletion] Trash the top card of your security stack.\" Then, if you have a blue Digimon in play, you may play 1 green level 4 or lower Digimon card from your hand without paying the cost.");
        AssertFull(card);
        Assert.Equal(EffectSupportLevel.None, EffectSupport.For(Option("B32-TAMERGRANT", "[Main] Give 1 of their Digimon or Tamers \"[End of Your Turn] Delete 1 of your Digimon.\" until their turn ends.")).Level);   // 테이머에게 주는 효과는 아직 읽지 않는다
    }

    // =====================================================================
    // 사건
    // =====================================================================

    [Fact]
    public void Event_PlayFromTrash_OnlyTrashOrigin_EX1_060()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B32-EX1060", 4, "[Your Turn] When you play a Digimon from your trash, gain 1 memory."));
        state.Player.Trash.Add(Digimon("B32-TRASH3", 3));
        state.Player.Hand.Add(Digimon("B32-HAND3", 3));

        Use(state, engine, Option("B32-FROMHAND", "[Main] You may play 1 level 3 Digimon card from your hand without paying the cost."));
        Assert.Equal(8, state.MemoryGauge);
        Use(state, engine, Option("B32-FROMTRASH", "[Main] You may play 1 level 3 Digimon card from your trash without paying the cost."));
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void Event_PlayedFromDigivolutionCards_BT17_002()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B32-WATCH", 4, "[Your Turn] When one of your Digimon is played from digivolution cards, gain 1 memory."));
        var decoder = Digimon("B32-DECODE", 6, "[On Play] You may play 1 level 3 Digimon card from this Digimon's digivolution cards without paying the cost.");
        AssertFull(decoder);
        state.Player.Hand.Add(decoder);
        // 이 카드 자신은 패에서 (코스트 3) → [등장 시]로 진화원에서 등장시킬 진화원이 없으면 아무 일도 없다
        Assert.True(engine.PlayCard(decoder).CanExecute);
        Assert.Equal(5, state.MemoryGauge);
        var host = state.Player.BattleArea.Single(d => d.TopCard == decoder);
        host.AddBottomSource(Digimon("B32-SRC3", 3));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, host, decoder);
        Assert.Equal(6, state.MemoryGauge);
    }

    [Fact]
    public void Event_SuspendedByEffect_NotByAttack_BT10_004()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Put(state.Player, Digimon("B32-WATCH2", 4, "[Your Turn] [Once Per Turn] When a Digimon is suspended by an effect, this Digimon gets +1000 DP for the turn."));
        var attacker = Put(state.Player, Digimon("B32-ATK", 4));
        engine.AttackSecurityOrDirect(attacker);
        Assert.Equal(4000, watcher.GetCurrentDP());

        Put(state.DummyOpponent, Digimon("B32-OPPS", 4));
        Use(state, engine, Option("B32-SUS", "[Main] Suspend 1 of your opponent's Digimon."));
        Assert.Equal(5000, watcher.GetCurrentDP());
    }

    [Fact]
    public void Event_OneOfYourEffectsSuspendsTamer_AndColorTamerSuspends()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Put(state.Player, Digimon("B32-WATCH3", 4, "[Your Turn] [Once Per Turn] When one of your effects suspends a Tamer, this Digimon gets +2000 DP for the turn."));
        var red = Tamer("B32-REDT", "[Main] By suspending this Tamer, gain 1 memory.", CardColor.Red);
        state.Player.Tamers.Add(red);
        Put(state.Player, Digimon("B32-WATCH4", 3, "[Your Turn] [Once Per Turn] When any of your yellow or red Tamers suspend, ＜Draw 1＞"));
        int hand = state.Player.Hand.Count;

        Assert.True(engine.ActivateMainAbility(null, red).CanExecute);

        Assert.Equal(6000, watcher.GetCurrentDP());
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    [Fact]
    public void Event_OpponentsDigivolutionCardTrashed_BT12_026()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B32-BT12026", 5, "[All Turns] [Once Per Turn] When a digivolution card of an opponent's Digimon is trashed, gain 1 memory."));
        Put(state.DummyOpponent, Digimon("B32-OPPH", 5), sources: Digimon("B32-OPPS1", 4));
        Use(state, engine, Option("B32-TRASHSRC", "[Main] Trash the bottom digivolution card of 1 of your opponent's Digimon."));
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void Event_WinBattle_TrashesOpponentsSecurity_BT25_020()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B32-BT25020", 3, "[All Turns] [Once Per Turn] When any of your [TS] trait Digimon win a battle, trash your opponent's top security card."));
        var ts = Put(state.Player, Digimon("B32-TS", 6, dp: 9000, traits: "TS"));
        var weak = Put(state.DummyOpponent, Digimon("B32-WEAK", 3, dp: 2000), suspended: true);

        Assert.True(engine.AttackOpponentDigimon(ts, weak).CanExecute);

        Assert.DoesNotContain(weak, state.DummyOpponent.BattleArea);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void Event_PlayTwoColorDigimon_BT13_101()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B32-BT13101", "[All Turns] When you play a 2-color black and yellow Digimon, by suspending this Tamer, ＜Draw 1＞ and gain 1 memory.", CardColor.Black);
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var mono = Digimon("B32-MONO", 3, color: CardColor.Black, playCost: 3);
        var duo = Digimon("B32-DUO", 3, color: CardColor.Black, second: CardColor.Yellow, playCost: 3);
        state.Player.Hand.Add(mono);
        state.Player.Hand.Add(duo);
        Assert.True(engine.PlayCard(mono).CanExecute);
        Assert.DoesNotContain(tamer, state.Player.SuspendedTamers);
        Assert.True(engine.PlayCard(duo).CanExecute);
        Assert.Contains(tamer, state.Player.SuspendedTamers);
        Assert.Equal(8 - 6 + 1, state.MemoryGauge);
    }

    // =====================================================================
    // 테이머의 진화 코스트 감소 · 두 동작 · 셈
    // =====================================================================

    [Fact]
    public void TamerEvoReduction_YouMaySuspendThisTamerToReduce_BT2_088()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B32-BT2088", "[Your Turn] When one of your Digimon would digivolve into a Digimon card in your hand with [Tyrannomon] in its name, you may suspend this Tamer to reduce the memory cost of the digivolution by 1.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var baby = Put(state.Player, Digimon("B32-BABY", 3));
        var tyranno = Digimon("B32-TYRANNO", 4, name: "MasterTyrannomon", evoCost: 2);
        state.Player.Hand.Add(tyranno);

        Assert.True(engine.EvolveDigimon(baby, tyranno).CanExecute);

        Assert.Contains(tamer, state.Player.SuspendedTamers);
        Assert.Equal(8 - 1, state.MemoryGauge);
    }

    [Fact]
    public void Compound_SuspendAndGrantPiercing_EX3_070()
    {
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, Digimon("B32-MINE2", 5));
        var opp = Put(state.DummyOpponent, Digimon("B32-OPP4", 4));
        Use(state, engine, Option("B32-COMP", "[Main] Suspend 1 of your opponent's Digimon, and 1 of your Digimon gains ＜Piercing＞ for the turn."));
        Assert.True(opp.IsSuspended);
        Assert.Contains(mine.TimedKeywords, k => k.Keyword == Keyword.Piercing);
    }

    [Fact]
    public void Compound_SuspendThenUnsuspend_P_109()
    {
        var card = Digimon("B32-P109", 4, "[On Play] [When Digivolving] Suspend 1 Digimon, then unsuspend 1 Digimon.");
        AssertFull(card);
    }

    [Fact]
    public void StaticSecurityAttack_ForEvery2SuspendedDigimon_BT10_057()
    {
        var (state, _) = NewMainPhase();
        var card = Digimon("B32-BT10057", 5, "[Your Turn] For every 2 suspended Digimon you have in play, this Digimon gets +2000 DP and gains ＜Security A. +1＞", dp: 5000);
        AssertFull(card);
        var d = Put(state.Player, card, suspended: true);
        Put(state.Player, Digimon("B32-S1", 3), suspended: true);
        var engine = new GoldfishEngine(state);
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, d.GetCurrentDP());
        Assert.Equal(2, d.GetSecurityAttackCount());
    }

    // =====================================================================
    // [육성] 두 문장 · 코스트 감소 조건 · 공개한 카드로 진화 / 옵션 사용 · 대상 표기
    // =====================================================================

    [Fact]
    public void Breeding_ReductionShrinksPerSource_OfficialKoreanBT13_007()
    {
        var (state, engine) = NewMainPhase();
        var egg = Card("B32-BT13007", "[Breeding] [Your Turn] [Once Per Turn] When a [Royal Knight] trait Digimon card would be played, you may reduce the play cost by 4. Further reduce it by 1 for each of this Digimon's digivolution cards.", CardType.Digitama, level: 2);
        AssertFull(egg);
        state.Player.BreedingArea = new CardInstance(egg, 1);
        state.Player.BreedingArea.AddBottomSource(Digimon("B32-RK-SRC", 3));
        var rk = Digimon("B32-RK", 5, playCost: 6, traits: "Royal Knight");
        state.Player.Hand.Add(rk);

        Assert.True(engine.PlayCard(rk).CanExecute);

        Assert.Equal(8 - (6 - 3), state.MemoryGauge);   // 공식 한글: "이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -1"
    }

    [Fact]
    public void Breeding_AlternativeReduction_WithFiveDifferentNames_EX6_006()
    {
        var (state, engine) = NewMainPhase();
        var egg = Card("B32-EX6006", "[Breeding] [Your Turn] [Once Per Turn] When one of your Digimon with the [Seven Great Demon Lords] trait would be played, you may reduce the play cost by 3. If this Digimon has 5 or more cards with different names in its digivolution cards, you may reduce the play cost by 4 instead.", CardType.Digitama, level: 2);
        AssertFull(egg);
        state.Player.BreedingArea = new CardInstance(egg, 1);
        for (int i = 0; i < 5; i++) state.Player.BreedingArea.AddBottomSource(Digimon("B32-DL" + i, 3));
        var lord = Digimon("B32-LORD", 6, playCost: 10, traits: "Seven Great Demon Lords");
        state.Player.Hand.Add(lord);

        Assert.True(engine.PlayCard(lord).CanExecute);

        Assert.Equal(8 - 6, state.MemoryGauge);
    }

    [Fact]
    public void UseCostReduction_ListAndMemoryCostOfThisCard_BT20_057_BT9_095()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B32-JES", 5, name: "Jesmon"));
        var card = Digimon("B32-BT20057", 6, "When this card would be played, if you have a Digimon with [Huckmon], [Jesmon] or [Sistermon] in its name, reduce the play cost by 4.", playCost: 7);
        AssertFull(card);
        state.Player.Hand.Add(card);
        Assert.True(engine.PlayCard(card).CanExecute);
        Assert.Equal(8 - 3, state.MemoryGauge);

        var xab = Option("B32-BT9095", "When you would use this card, if you have a Digimon with [X Antibody] in its digivolution cards in play, reduce the memory cost of this card by 2.\n[Main] Gain 1 memory.", cost: 3);
        AssertFull(xab);
    }

    [Fact]
    public void Reveal_DigivolveThisDigimonIntoRevealedCard_BT7_049()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B32-BT7049", 5, "[On Play] Reveal the top 3 cards of your deck. You may digivolve this Digimon into a green level 6 card among them without paying its memory cost. Place the remaining cards at the bottom of your deck in any order.", color: CardColor.Green, playCost: 4);
        AssertFull(card);
        var six = Digimon("B32-GREEN6", 6, color: CardColor.Green, evoCost: 3);
        state.Player.MainDeck.Insert(1, six);
        state.Player.Hand.Add(card);
        state.Player.Tamers.Add(Tamer("B32-GT", color: CardColor.Green));

        Assert.True(engine.PlayCard(card).CanExecute);

        var d = state.Player.BattleArea.Single(x => x.EvolutionSources.Contains(card) || x.TopCard == card);
        Assert.Equal(six, d.TopCard);
        Assert.Equal(8 - 4, state.MemoryGauge);   // 진화는 코스트 없이
    }

    [Fact]
    public void Reveal_UseOptionAmongThem_EX7_048()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B32-EX7048", 5, "[On Play] Reveal the top 6 cards of your deck. You may use 1 [Three Musketeers] trait Option card among them without paying the cost. Return the rest to the top or bottom of the deck.", playCost: 4);
        AssertFull(card);
        var opt = Card("B32-MUSK", "[Main] Gain 2 memory.", CardType.Option, playCost: 5, traits: "Three Musketeers");
        state.Player.MainDeck.Insert(2, opt);
        state.Player.Hand.Add(card);

        Assert.True(engine.PlayCard(card).CanExecute);

        Assert.Equal(8 - 4 + 2, state.MemoryGauge);
        Assert.Contains(opt, state.Player.Trash);
    }

    [Fact]
    public void TargetPhrase_AsMuchDpAsThisDigimonOrLess_BT25_093()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B32-BT25093", 5, "[When Attacking] [Once Per Turn] Delete 1 of your opponent's Digimon with as much DP as this Digimon or less.", dp: 6000);
        AssertFull(card);
        Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(Digimon("B32-PC7", 5, "[On Play] Delete 1 of your opponent's play cost 7 or lower Digimon.")).Level);
    }

    // =====================================================================
    // 생성 한글 (공식 표기)
    // =====================================================================

    [Theory]
    [InlineData("[All Turns] [Once Per Turn] When this yellow Digimon with the [Data] or [Witchelny] trait would leave the battle area by your opponent's effects, by trashing your top security card, it doesn't leave.",
        "【서로의 턴】 [턴에 1회] 특징으로 「데이터종」/「윗체르니」를 가진 옐로인 이 디지몬이 상대의 효과로 배틀 에어리어를 벗어날 때, 자신의 시큐리티를 위에서부터 1장 파기하는 것으로, 그 디지몬은 배틀 에어리어를 벗어나지 않는다.")]
    [InlineData("[All Turns] When an opponent's effect would delete this Digimon, you may play 1 [Parasitemon] from this Digimon's digivolution cards without paying the cost.",
        "【서로의 턴】 이 디지몬이 상대의 효과로 소멸할 때, 이 디지몬의 진화원에서 「패러사이몬」 1장을 코스트를 지불하지 않고 등장시킬 수 있다.")]
    [InlineData("[All Turns] When this Digimon would be deleted or returned to your hand or deck, you may trash 2 level 5 cards in this Digimon's digivolution cards to prevent it from leaving play.",
        "【서로의 턴】 이 디지몬이 소멸할 때 또는 패/덱으로 되돌아갈 때, 이 디지몬의 진화원에서 Lv.5인 카드 2장을 파기하는 것으로, 소멸하지 않고, 패/덱으로 되돌아가지 않는다.")]
    [InlineData("[All Turns] [Once Per Turn] When this Digimon would leave the battle area, by trashing the bottom card of your security stack, it doesn't leave.",
        "【서로의 턴】 [턴에 1회] 이 디지몬이 배틀 에어리어를 벗어날 때, 자신의 시큐리티를 아래에서부터 1장 파기하는 것으로, 그 디지몬은 배틀 에어리어를 벗어나지 않는다.")]
    [InlineData("[On Play] [When Digivolving] Give 1 of your opponent's Digimon \"[Start of Your Main Phase] This Digimon attacks.\" until their turn ends.",
        "【등장 시】 【진화 시】 상대의 턴 종료 시까지 상대의 디지몬 1마리에게 「【자신의 메인 페이즈 개시 시】 이 디지몬으로 어택한다.」의 효과를 준다.")]
    [InlineData("[On Play] Until the end of your opponent's turn, 1 of their Digimon gains \"[On Deletion] Lose 1 memory.\"",
        "【등장 시】 상대의 턴 종료 시까지 상대의 디지몬 1마리에게 「【소멸 시】 메모리를 -1 한다.」의 효과를 준다.")]
    [InlineData("[Your Turn] [Once Per Turn] When you play a Digimon from your trash, gain 1 memory.",
        "【자신의 턴】 [턴에 1회] 자신의 트래시에서 디지몬이 등장했을 때, 메모리를 +1 한다.")]
    [InlineData("[All Turns] [Once Per Turn] When a digivolution card of an opponent's Digimon is trashed, gain 1 memory.",
        "【서로의 턴】 [턴에 1회] 상대의 디지몬의 진화원이 파기되었을 때, 메모리를 +1 한다.")]
    [InlineData("[Your Turn] [Once Per Turn] When a Digimon is suspended by an effect, this Digimon gets +1000 DP for the turn.",
        "【자신의 턴】 [턴에 1회] 디지몬이 효과로 레스트했을 때, 이 턴 동안, 이 디지몬의 DP를 +1000 한다.")]
    [InlineData("[Your Turn] When one of your Digimon would digivolve into a 2-color black Digimon, you may suspend this Tamer to reduce the digivolution cost by 1.",
        "【자신의 턴】 자신의 디지몬이 블랙을 포함하는 2색의 디지몬 카드로 진화할 때, 이 테이머를 레스트시키는 것으로, 지불하는 진화 코스트를 -1 한다.")]
    [InlineData("[Breeding] [Your Turn] [Once Per Turn] When a [Royal Knight] trait Digimon card would be played, you may reduce the play cost by 4. Further reduce it by 1 for each of this Digimon's digivolution cards.",
        "[육성] 【자신의 턴】 [턴에 1회] 자신의 패에서 특징으로 「로얄 나이츠」를 가진 디지몬 카드가 등장할 때, 지불하는 등장 코스트를 -4 할 수 있다. 이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -1.")]
    [InlineData("[On Play] [When Digivolving] Reveal the top 6 cards of your deck. You may use 1 [Three Musketeers] trait Option card among them without paying the cost. Return the rest to the top or bottom of the deck.",
        "【등장 시】 【진화 시】 자신의 덱 위에서부터 6장 오픈한다. 그중 특징으로 「3총사」를 가진 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다. 나머지는 덱 위 또는 아래로만 되돌린다.")]
    [InlineData("[Your Turn] For each Digimon with no digivolution cards your opponent has in play, this Digimon gains ＜Security A. +1＞",
        "【자신의 턴】 진화원을 갖지 않는 상대 디지몬 1마리당 이 디지몬은 《시큐리티 어택 +1》 (이 디지몬이 체크하는 시큐리티의 매수 +1)을 얻는다.")]
    public void KoreanRender_OfficialNotation(string en, string ko) =>
        Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));
}
