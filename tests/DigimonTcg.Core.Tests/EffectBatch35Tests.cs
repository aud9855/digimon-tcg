using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.35.0 미지원 문장 9차 묶음: 뒷면 진화원 모델 (종합 룰 4-7-4 · 4-7-9 · 4-7-10 · 16-41 · 17-1-3-2-4) — ≪트레이닝≫ · 뒷면으로 놓기 비용 ·
/// 뒷면 진화원 파기 비용 · 뒷면 1장마다 (셈 · 진화 코스트) · 뒷면 카드가 놓였을 때 · 파기되었을 때 · 테이머 아래 뒷면 카드 · "A or B" 비용 ·
/// "it may digivolve ~ in the hand or trash" · BT19-051 상대의 효과로 · 테이머도 고르는 레스트 · 레스트/액티브 불가 · 생성 한글
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch35Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", int evoCost = 1, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", int dp = -1, string? name = null, int evoCost = 1, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, 3, CardColor.Blue, dp, name, inherited, evoCost, traits);

    private static CardData Tamer(string id, string main = "", params string[] traits) => Card(id, main, CardType.Tamer, traits: traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B35-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B35-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B35-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B35-OSEC" + i, 3, dp: 1000));
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
    // 모델 · ≪트레이닝≫ · 정보 없음 (4-7-9)
    // =====================================================================

    [Fact]
    public void Training_PlacesTopDeckCardFaceDown_EX9_008()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B35-EX9008", 3, "＜Training＞ (In the main phase, by suspending this Digimon, place your deck's top card face down as this Digimon's bottom digivolution card. This effect can also activate in the breeding area.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        int deck = state.Player.MainDeck.Count;
        Assert.True(engine.ActivateMainAbility(me, card).CanExecute);
        Assert.True(me.IsSuspended);
        Assert.Equal(deck - 1, state.Player.MainDeck.Count);
        Assert.Single(me.EvolutionSources);
        Assert.Equal(1, me.FaceDownSourceCount);
        Assert.True(me.IsFaceDown(me.EvolutionSources.Single()));
    }

    [Fact]
    public void FaceDownSource_HasNoInformation_NoInheritedDpNoKeywordNoNameCondition()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon("B35-SRC", 3, inherited: "＜Blocker＞", name: "Agumon");
        src.InheritedDpBonus = 2000;
        var faceUp = Put(state.Player, Digimon("B35-UP", 4, dp: 4000), sources: src);
        var faceDown = Put(state.Player, Digimon("B35-DOWN", 4, dp: 4000));
        engine.PlaceFaceDownSource(state.Player, faceDown, src);
        engine.RecalculateFieldBuffs();

        Assert.Equal(6000, faceUp.GetCurrentDP());
        Assert.Equal(4000, faceDown.GetCurrentDP());   // 진화원 DP는 뒷면이면 없다
        Assert.True(CardScripts.HasKeyword(faceUp, Keyword.Blocker));
        Assert.False(CardScripts.HasKeyword(faceDown, Keyword.Blocker));
        Assert.True(faceUp.SourcesAny(s => TargetSpec.NameHas(s, "Agumon")));
        Assert.False(faceDown.SourcesAny(s => TargetSpec.NameHas(s, "Agumon")));
        Assert.True(faceDown.SourcesAny(_ => true));   // 정보를 보지 않는 조건("1 card")에는 들어간다
        Assert.Single(faceDown.EvolutionSources);      // 장 수에는 들어간다
        Assert.NotSame(src, faceDown.EvolutionSources.Single());   // 뒷면은 복사본 (같은 카드 객체가 여러 장일 수 있다)
    }

    [Fact]
    public void PlaceFaceDownCost_ThenCountDp_EX9_009()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B35-EX9009", 4, "[When Attacking] [Once Per Turn] By placing your deck's top card face down as this Digimon's bottom digivolution card, this Digimon gets +1000 DP for each of its face-down digivolution cards until your opponent's turn ends.", dp: 4000);
        AssertFull(card);
        engine.PlayerChoices = new AcceptAll();
        var me = Put(state.Player, card);
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B35-X", 3));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Equal(2, me.FaceDownSourceCount);
        Assert.Equal(6000, me.GetCurrentDP());
    }

    [Fact]
    public void TrashBottomFaceDownCost_OnlyFaceDown_EX9_031()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B35-EX9031", 5, "[When Digivolving] [When Attacking] [Once Per Turn] By trashing this Digimon's bottom face-down digivolution card, gain 2 memory.");
        AssertFull(card);
        engine.PlayerChoices = new AcceptAll();
        var up = Digimon("B35-UPSRC", 4);
        var me = Put(state.Player, card, sources: up);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Equal(8, state.MemoryGauge);   // 뒷면 진화원이 없으면 낼 수 없다

        me.UsedOncePerTurn.Clear();
        var down = engine.PlaceFaceDownSource(state.Player, me, Digimon("B35-DOWNSRC", 3));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Equal(10, state.MemoryGauge);
        Assert.Contains(down, state.Player.Trash);
        Assert.Contains(up, me.EvolutionSources);
        Assert.Equal(0, me.FaceDownSourceCount);
    }

    [Fact]
    public void EvoCostReduction_PerFaceDownSource_BT22_038()
    {
        var (state, engine) = NewMainPhase();
        var evo = Digimon("B35-BT22038", 4, "When any of your Digimon with the [Ver.1] trait would digivolve into this card, for each of its face-down digivolution cards, reduce the digivolution cost by 1.", evoCost: 4);
        AssertFull(evo);
        var me = Put(state.Player, Digimon("B35-VER1", 3, traits: "Ver.1"));
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B35-A", 3));
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B35-B", 3));
        state.Player.Hand.Add(evo);
        Assert.True(engine.EvolveDigimon(me, evo).CanExecute);
        Assert.Equal(6, state.MemoryGauge);   // 8 - (4 - 2)
        Assert.Equal(2, me.FaceDownSourceCount);   // 진화해도 뒷면 그대로
    }

    [Fact]
    public void WhenFaceDownPlaced_TriggersOnEffectPlacement_EX9_002()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B35-EX9002", 4, "[Your Turn] When face-down cards are placed in this Digimon's digivolution cards, gain 1 memory.\n[On Play] By placing 1 card in your hand face down as this Digimon's bottom digivolution card, gain 1 memory.");
        AssertFull(card);
        engine.PlayerChoices = new AcceptAll();
        state.Player.Hand.Add(Digimon("B35-HAND", 3));
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(1, me.FaceDownSourceCount);
        Assert.Equal(10, state.MemoryGauge);   // 비용 효과 +1, 뒷면 카드가 놓였을 때 +1
    }

    [Fact]
    public void WhenEffectsTrashFaceDown_Triggers_BT26_048()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Digimon("B35-BT26048", 5, "[All Turns] When effects trash face-down digivolution cards from your Digimon, 1 of your opponent's Digimon gets -6000 DP for the turn.");
        var trasher = Digimon("B35-TRASHER", 4, "[On Play] By trashing this Digimon's bottom face-down digivolution card, gain 1 memory.");
        AssertFull(watcher);
        engine.PlayerChoices = new AcceptAll();
        Put(state.Player, watcher);
        var me = Put(state.Player, trasher);
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B35-FD", 3));
        var foe = Put(state.DummyOpponent, Digimon("B35-FOE", 6, dp: 6000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, trasher);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void RuleCheck_DeDigivolveExposingFaceDown_TrashesStack_17_1_3_2_4()
    {
        var (state, engine) = NewMainPhase();
        var top = Digimon("B35-TOP", 4);
        var me = Put(state.Player, top);
        var fd = engine.PlaceFaceDownSource(state.Player, me, Digimon("B35-FDLV3", 3));
        int before = state.Player.Trash.Count;
        var ai = Digimon("B35-AIDEDIGI", 4, "[On Play] ＜De-Digivolve 1＞ 1 of your opponent's Digimon.");
        var foe = Put(state.DummyOpponent, ai);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, ai);
        Assert.DoesNotContain(me, state.Player.BattleArea);
        Assert.Equal(before + 2, state.Player.Trash.Count);
        Assert.Contains(fd, state.Player.Trash);
        Assert.Contains(top, state.Player.Trash);
    }

    [Fact]
    public void FaceDownSource_NotPlayableFromSources_ByNameCondition()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B35-PLAYSRC", 5, "[On Deletion] You may play 1 [Agumon] from this Digimon's digivolution cards without paying the cost.");
        engine.PlayerChoices = new AcceptAll();
        var me = Put(state.Player, card);
        engine.PlaceFaceDownSource(state.Player, me, Digimon("B35-AGU", 3, name: "Agumon"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        var ctx = state.Player.BattleArea.Count;
        Assert.Equal(1, ctx);   // 뒷면 「아구몬」은 이름을 참조할 수 없다 — 아무것도 일어나지 않는다
    }

    // =====================================================================
    // 테이머 아래 뒷면 카드
    // =====================================================================

    [Fact]
    public void UnderTamer_FaceDownPlacement_AndFaceDownOnlyCost_ST23_13()
    {
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var tamer = Tamer("B35-ST2313", "[Start of Your Main Phase] [On Play] You may place the top card of your deck face down under this Tamer. Then, if your opponent has a Digimon, gain 1 memory.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        state.Player.UnderTamers.Add(Digimon("B35-SAVED", 3));   // 앞면 (≪세이브≫ 등)
        var user = Digimon("B35-USER", 4, "[On Play] By trashing the bottom face-down card from under any of your Tamers, gain 2 memory.");
        AssertFull(user);
        var me = Put(state.Player, user);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, user);
        Assert.Equal(8, state.MemoryGauge);   // 앞면 카드만 있으면 낼 수 없다

        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, tamer);
        Assert.Equal(1, state.Player.FaceDownUnderTamerCount);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, user);
        Assert.Equal(10, state.MemoryGauge);
        Assert.Equal(0, state.Player.FaceDownUnderTamerCount);
        Assert.Single(state.Player.UnderTamers);   // 앞면 카드는 남는다
    }

    [Fact]
    public void PlaceFromHandFaceDownUnderTamer_ST23_10()
    {
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        state.Player.Tamers.Add(Tamer("B35-GD", "", "Glowing Dawn"));
        state.Player.Hand.Add(Digimon("B35-H1", 3));
        state.Player.Hand.Add(Digimon("B35-H2", 3));
        var card = Digimon("B35-ST2310", 3, "[On Play] By placing 1 card from your hand face down under any of your Tamers with the [Glowing Dawn] trait, ＜Draw 2＞ (Draw 2 cards from your deck.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(1, state.Player.FaceDownUnderTamerCount);
        Assert.Equal(3, state.Player.Hand.Count);   // 2 - 1 + 2
    }

    [Fact]
    public void EitherOrCost_PaysTheRecommendedSide_BT26_082()
    {
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(Tamer("B35-T"));
        state.Player.AddUnderTamerFaceDown(Digimon("B35-U1", 3));
        state.Player.AddUnderTamerFaceDown(Digimon("B35-U2", 3));
        state.MemoryGauge = 5;
        var card = Digimon("B35-BT26082", 6, "[When Digivolving] By deleting this Digimon or trashing 2 bottom face-down cards from under any of your Tamers, gain 3 memory.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Equal(8, state.MemoryGauge);
        Assert.Contains(me, state.Player.BattleArea);   // 추천: 디지몬을 남기고 테이머 아래 카드를 파기
        Assert.Equal(0, state.Player.FaceDownUnderTamerCount);
    }

    // =====================================================================
    // 진화 · 대상
    // =====================================================================

    [Fact]
    public void ItMayDigivolve_FromHandOrTrash_BT22_049()
    {
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var card = Digimon("B35-BT22049", 3, "[On Play] It may digivolve into a Digimon card with the [Ver.2] trait in the hand or trash.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var evo = Digimon("B35-VER2", 4, evoCost: 2, traits: "Ver.2");
        state.Player.Trash.Add(evo);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Same(evo, me.TopCard);
        Assert.Equal(6, state.MemoryGauge);
    }

    [Fact]
    public void StaticReboot_ForDigimonWithFaceDownSources_EX9_069()
    {
        var (state, engine) = NewMainPhase();
        var tamer = Tamer("B35-EX9069", "[Opponent's Turn] All of your Digimon with face-down digivolution cards gain ＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.)");
        AssertFull(tamer);
        state.DummyOpponent.Tamers.Add(tamer);
        var withFd = Put(state.DummyOpponent, Digimon("B35-AIFD", 4));
        var plain = Put(state.DummyOpponent, Digimon("B35-AIPLAIN", 4));
        engine.PlaceFaceDownSource(state.DummyOpponent, withFd, Digimon("B35-AIX", 3));
        withFd.IsOwnersTurn = plain.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(withFd, Keyword.Reboot));
        Assert.False(CardScripts.HasKeyword(plain, Keyword.Reboot));
    }

    [Fact]
    public void PlayDpLowerCard_BT26_048_Pick()
    {
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var card = Digimon("B35-DPPICK", 5, "[On Play] You may play 1 6000 DP or lower [Ver.4] trait Digimon card from your hand without paying the cost.");
        AssertFull(card);
        var big = Digimon("B35-BIG", 6, dp: 9000, traits: "Ver.4");
        var small = Digimon("B35-SMALL", 4, dp: 5000, traits: "Ver.4");
        state.Player.Hand.Add(big);
        state.Player.Hand.Add(small);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == small);
        Assert.Contains(big, state.Player.Hand);
    }

    // =====================================================================
    // 룰에 맞춘 수정 (BT19-051 · 테이머도 고르는 레스트)
    // =====================================================================

    [Fact]
    public void BT19_051_OnlyOpponentEffectsCantReturn()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("BT19-051", 5, "[On Play] [When Digivolving] 1 of your Digimon can't be returned to the hand or deck and gets +3000 DP until the end of your opponent's turn.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        engine.RecalculateFieldBuffs();
        Assert.Contains(Keyword.CannotBeReturnedByOpponentEffects, me.ActiveKeywords);
        Assert.DoesNotContain(Keyword.CannotBeReturnedByEffects, me.ActiveKeywords);
    }

    [Fact]
    public void SuspendDigimonOrTamers_CanChooseTamer_BT8_102()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B35-BT8102", "[Main] Suspend 1 of your opponent's Digimon or Tamers.", CardType.Option, playCost: 0);
        AssertFull(option);
        var aiTamer = Tamer("B35-AITAMER");
        state.DummyOpponent.Tamers.Add(aiTamer);
        Put(state.Player, Digimon("B35-COLOR", 3));
        state.Player.Hand.Add(option);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.False(state.DummyOpponent.HasActiveTamer(aiTamer));
    }

    [Fact]
    public void CantUnsuspend_DigimonOrTamers_TamerStaysSuspended_BT19_050()
    {
        var (state, engine) = NewMainPhase();
        var myTamer = Tamer("B35-MYTAMER");
        state.Player.Tamers.Add(myTamer);
        state.Player.SuspendedTamers.Add(myTamer);
        var ai = Digimon("B35-AILOCK", 4, "[On Play] 1 of your opponent's Digimon or Tamers can't unsuspend until the end of their turn.");
        AssertFull(ai);
        var foe = Put(state.DummyOpponent, ai);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, foe, ai);
        Assert.True(state.Player.TamerCannotUnsuspendUntil.ContainsKey(myTamer));
        state.Player.TamerCannotUnsuspendUntil[myTamer] = state.TurnSequence + 5;   // 다음 액티브 페이즈까지 남도록
        engine.ExecuteActivePhase();
        Assert.False(state.Player.HasActiveTamer(myTamer));
    }

    [Fact]
    public void DigimonAndTamer_EachCantSuspend_BT23_092()
    {
        var (state, engine) = NewMainPhase();
        var option = Card("B35-BT23092", "[Main] Until your opponent's turn ends, 1 of their Digimon and 1 of their Tamers can't suspend.", CardType.Option, playCost: 0);
        AssertFull(option);
        var aiTamer = Tamer("B35-AIT2");
        state.DummyOpponent.Tamers.Add(aiTamer);
        var foe = Put(state.DummyOpponent, Digimon("B35-AID", 5));
        Put(state.Player, Digimon("B35-COLOR2", 3));
        state.Player.Hand.Add(option);
        Assert.True(engine.PlayCard(option).CanExecute);
        Assert.True(foe.CannotSuspendUntil >= state.TurnSequence);
        Assert.True(state.DummyOpponent.TamerCannotSuspendUntil.ContainsKey(aiTamer));
    }

    [Fact]
    public void PlayersCantGainMemoryOtherThanTamers_BothSides_BT25_079()
    {
        var (state, engine) = NewMainPhase();
        var lockCard = Digimon("B35-BT25079", 5, "[All Turns] Players can't gain memory other than by Tamer effects.");
        AssertFull(lockCard);
        Put(state.DummyOpponent, lockCard);
        engine.RecalculateFieldBuffs();
        var gainer = Digimon("B35-MYGAIN", 4, "[On Play] Gain 2 memory.");
        var me = Put(state.Player, gainer);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, gainer);
        Assert.Equal(8, state.MemoryGauge);   // 상대 필드의 카드여도 "Players"는 자신도 막는다
        var tamer = Tamer("B35-TGAIN", "[On Play] Gain 1 memory.");
        state.Player.Tamers.Add(tamer);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, tamer);
        Assert.Equal(9, state.MemoryGauge);   // 테이머 효과는 된다
    }

    [Fact]
    public void CantSuspend_Tamer_BlocksSuspendCost()
    {
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var tamer = Tamer("B35-SUSCOST", "[Main] By suspending this Tamer, gain 1 memory.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        state.Player.TamerCannotSuspendUntil[tamer] = state.TurnSequence + 2;
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, tamer);
        Assert.Equal(8, state.MemoryGauge);
        Assert.True(state.Player.HasActiveTamer(tamer));
    }

    // =====================================================================
    // 생성 한글 (공식 표기)
    // =====================================================================

    [Theory]
    [InlineData("[When Digivolving] [When Attacking] [Once Per Turn] By trashing this Digimon's bottom face-down digivolution card, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)",
        "【진화 시】 【어택 시】 [턴에 1회] 이 디지몬의 뒷면의 진화원을 아래에서부터 1장 파기하는 것으로, 〈리커버리 +1 《덱》〉 (자신의 덱 위에서 카드를 1장 시큐리티 위에 놓는다).")]
    [InlineData("When any of your [Ver.3] trait Digimon would digivolve into this card, for each of their face-down digivolution cards, reduce the digivolution cost by 1.",
        "특징으로 「Ver.3」을 가진 자신의 디지몬이 패의 이 카드로 진화할 때, 그 디지몬의 뒷면의 진화원 1장마다 지불하는 진화 코스트를 -1 한다.")]
    [InlineData("Inherited Effect [Your Turn] [Once Per Turn] When face-down cards are placed in this Digimon's digivolution cards, this Digimon may digivolve into a [Ver.2] trait Digimon card in the hand with the digivolution cost reduced by 1.",
        "【자신의 턴】 [턴에 1회] 이 디지몬의 진화원에 뒷면의 카드가 놓였을 때, 이 디지몬을 패의 특징으로 「Ver.2」를 가진 디지몬 카드 1장으로 진화 코스트를 -1 하고 진화시킬 수 있다.")]
    [InlineData("Inherited Effect [Your Turn] [Once Per Turn] When this Digimon with face-down digivolution cards would digivolve into a [Ver.3] trait Digimon card, reduce the digivolution cost by 1.",
        "【자신의 턴】 [턴에 1회] 뒷면의 진화원을 가진 이 디지몬이 특징으로 「Ver.3」을 가진 디지몬 카드로 진화할 때, 지불하는 진화 코스트를 -1 한다.")]
    [InlineData("[Opponent's Turn] All of your Digimon with face-down digivolution cards gain ＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.)",
        "【상대의 턴】 뒷면의 진화원을 가진 자신의 디지몬 전부는 《재기동》 (이 디지몬은 상대의 액티브 페이즈에도 액티브가 된다)을 얻는다.")]
    [InlineData("[When Digivolving] [When Attacking] [Once Per Turn] By placing 1 card in your hand face down as this Digimon's bottom digivolution card, if you have as many or fewer security cards as this Digimon has face-down digivolution cards, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)",
        "【진화 시】 【어택 시】 [턴에 1회] 자신의 패에서 카드 1장을 이 디지몬의 진화원 가장 아래에 뒷면으로 놓는 것으로, 자신의 시큐리티 매수가 이 디지몬의 뒷면의 진화원 매수 이하일 때, 〈리커버리 +1 《덱》〉 (자신의 덱 위에서 카드를 1장 시큐리티 위에 놓는다).")]
    [InlineData("・By trashing 1 of your [DM] trait Digimon's bottom 2 face-down digivolution cards, it unsuspends.",
        "특징으로 「DM」을 가진 자신의 디지몬 1마리의 뒷면의 진화원을 아래에서부터 2장 파기하는 것으로, 그 디지몬을 액티브로 한다.")]
    [InlineData("[All Turns] When this Digimon would leave the battle area, by trashing its bottom 2 face-down or [Cyborg] trait digivolution cards, it doesn't leave.",
        "【서로의 턴】 이 디지몬이 배틀 에어리어를 벗어날 때, 이 디지몬의 진화원에서 특징으로 「사이보그형」을 가지거나 뒷면의 카드를 아래에서부터 2장 파기하는 것으로, 그 디지몬은 배틀 에어리어를 벗어나지 않는다.")]
    [InlineData("[When Digivolving] [End of Attack] By deleting this Digimon or trashing 2 bottom face-down cards from under any of your Tamers, delete 1 of your opponent's highest DP Digimon.",
        "【진화 시】 【어택 종료 시】 이 디지몬을 소멸시키거나, 자신의 테이머 아래의 뒷면 카드를 아래에서부터 2장 파기하는 것으로, 가장 DP가 높은 상대의 디지몬 1마리를 소멸시킨다.")]
    [InlineData("[End of Attack] [Once Per Turn] By trashing the bottom face-down card from under any of your Tamers, this [Glowing Dawn] trait Digimon unsuspends.",
        "【어택 종료 시】 [턴에 1회] 자신의 테이머 아래의 뒷면 카드를 아래에서부터 1장 파기하는 것으로, 특징으로 「Glowing Dawn」을 가진 이 디지몬을 액티브로 한다.")]
    public void KoreanRender_OfficialNotation(string en, string ko) =>
        Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));
}
