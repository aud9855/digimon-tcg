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
/// v0.41.0 미지원 문장 15차 묶음: 링크 조건이 빠진 어플몬의 【링크 시】 문장 · 다른 표기 ("Until their turn ends" · "To 1 of ~, give ~ for each ~" · "Draw 1") ·
/// "다음에 ~ 진화할 때" 진화 코스트 감소 · 자신의 어택 대상 변경 · 진화원 조작 (상대 디지몬을 상대의 진화원 아래에 · 맨 위 카드 파기) · 조건 (A 또는 B · 트래시에서 진화 …) ·
/// 효과 발휘 ("activate ~ effects") · 링크 코스트 감소 (BT25-004 · BT25-045) · AI 링크 · ≪계승≫ (BT26-032) · 「라센몬」 ≪디지버스트≫ (BT8-081) · 테이머를 세는 "명"
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch41Tests
{
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public int Confirms;
        public int OptionAnswer = -1;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Confirms++; return ConfirmAnswer; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) =>
            recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => OptionAnswer >= 0 ? OptionAnswer : recommended;
        public IReadOnlyList<int> ChooseCards(GameState state, string question, CardChoiceZone zone, IReadOnlyList<CardData> options, int min, int max, IReadOnlyList<int> recommended) => recommended;
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

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", int dp = -1, string? name = null, int playCost = 3,
        CardColor color = CardColor.Blue, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, inherited, 1, traits);

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null, params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name, "", 1, traits);

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B41-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B41-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B41-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B41-OSEC" + i, 3, dp: 1000, color: CardColor.White));
        }
        var choices = new Scripted();
        return (state, new GoldfishEngine(state) { PlayerChoices = choices }, choices);
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, int turnPlayed = 1, params CardData[] sources)
    {
        var d = new CardInstance(card, turnPlayed) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    // =====================================================================
    // 1. 링크 조건이 빠진 어플몬의 라벨 없는 진화원 칸 문장 = 【링크 시】 (BT21-043 · BT22-009 · BT26-019 …)
    // =====================================================================

    [Fact]
    public void AppLinkEffectWithoutLabel_FiresWhenLinked_BT21_043()
    {
        var (state, engine, _) = NewMainPhase();
        var app = Digimon("B41-APP", 3, "", "1 of your opponent's Digimon gets -2000 DP until their turn ends.", traits: new[] { "Pop (App Name)", "Appmon" });
        AssertFull(app);
        var host = Put(state.Player, Digimon("B41-HOST", 4, dp: 5000, traits: "Appmon"));
        var enemy = Put(state.DummyOpponent, Digimon("B41-OPP", 4, dp: 5000));
        engine.LinkCard(state.Player, host, app);
        Assert.Equal(3000, enemy.GetCurrentDP());   // 【링크 시】로 발휘
        Assert.Equal(5000, host.GetCurrentDP() - GoldfishEngine.LinkDp(app));
    }

    [Fact]
    public void AppLinkEffect_NotInheritedEffect_WhenInSources()
    {
        // 진화원에 있을 때 [진화원 효과]로 발휘하지 않는다 (【링크 시】 효과)
        var (state, engine, _) = NewMainPhase();
        var app = Digimon("B41-APP2", 3, "", "Draw 1", traits: new[] { "Mail (App Name)" });
        AssertFull(app);
        var script = CardScripts.For(app)!;
        Assert.Contains(script.Effects, e => e.Trigger == EffectTrigger.OnLinked && e.Source == EffectSource.Main);
        Assert.DoesNotContain(script.Effects, e => e.Source == EffectSource.Inherited);
        var host = Put(state.Player, Digimon("B41-HOST2", 4));
        int hand = state.Player.Hand.Count;
        engine.LinkCard(state.Player, host, app);
        Assert.Equal(hand + 1, state.Player.Hand.Count);   // "Draw 1" = ＜Draw 1＞
    }

    [Fact]
    public void AppLinkRule_OnlyForUnlabeledClausesOfAppCards()
    {
        Assert.True(SentenceParser.IsUnlabeledAppLinkEffect41("Suspend 1 of your opponent's Digimon.", Digimon("B41-A3", 3, traits: "Roam (App Name)")));
        Assert.False(SentenceParser.IsUnlabeledAppLinkEffect41("[When Attacking] Suspend 1 of your opponent's Digimon.", Digimon("B41-A4", 3, traits: "Roam (App Name)")));
        Assert.False(SentenceParser.IsUnlabeledAppLinkEffect41("Suspend 1 of your opponent's Digimon.", Digimon("B41-A5", 3, traits: "Vaccine")));
        Assert.False(SentenceParser.IsUnlabeledAppLinkEffect41("Suspend 1 of your opponent's Digimon.", Digimon("B41-A6", 2, traits: "Tap (App Name)")));   // 디지타마(Lv.2)의 진화원 효과
    }

    // =====================================================================
    // 2. 다른 표기
    // =====================================================================

    [Fact]
    public void UntilTheirTurnEnds_TheirDigimon_BT21_053()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-UT", "[Main] Until your opponent's turn ends, 1 of their Digimon can't attack players.");
        AssertFull(card);
        var enemy = Put(state.DummyOpponent, Digimon("B41-UTO", 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.True(CardScripts.HasKeyword(enemy, Keyword.CannotAttackPlayers));
        Assert.Equal("【메인】 상대의 턴 종료 시까지, 상대의 디지몬 1마리는 플레이어에게 어택할 수 없다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void ToOneGiveDpForEach_BT22_035()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-TO", "[Main] To 1 of your opponent's Digimon, give -4000 DP for the turn for each of your Digimon with the [Appmon] trait.");
        AssertFull(card);
        Put(state.Player, Digimon("B41-AP1", 3, traits: "Appmon"));
        Put(state.Player, Digimon("B41-AP2", 3, traits: "Appmon"));
        var enemy = Put(state.DummyOpponent, Digimon("B41-TOO", 6, dp: 10000));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Equal(2000, enemy.GetCurrentDP());
    }

    [Fact]
    public void ToOneDeDigivolveForEach_BT24_065()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-DD", 6, "[When Digivolving] To 1 of your opponent's Digimon, ＜De-Digivolve 1＞ (Trash the top card. You can't trash past level 3 cards.) for each of your Digimon.");
        AssertFull(card);
        var me = Put(state.Player, card);
        Put(state.Player, Digimon("B41-DD2", 3));
        var enemy = Put(state.DummyOpponent, Digimon("B41-DDO6", 6), sources: new[] { Digimon("B41-DDO5", 5), Digimon("B41-DDO4", 4), Digimon("B41-DDO3", 3) });
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Equal(4, enemy.TopCard.Level);   // 자신의 디지몬 2마리 → ≪퇴화 1≫ 2번
    }

    [Fact]
    public void FromOpponentDigimonTrashAnySourceForEvery_ST21_04()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-ST2104", 4, "[On Play] From 1 of your opponent's Digimon, trash any 1 digivolution card for every 2 colors your Tamers have.");
        AssertFull(card);
        var me = Put(state.Player, card);
        state.Player.Tamers.Add(Tamer("B41-T1", color: CardColor.Red));
        state.Player.Tamers.Add(Tamer("B41-T2", color: CardColor.Blue));
        var enemy = Put(state.DummyOpponent, Digimon("B41-SO5", 5), sources: new[] { Digimon("B41-SO4", 4), Digimon("B41-SO3", 3) });
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Single(enemy.EvolutionSources);
    }

    [Fact]
    public void GetsKeyword_IsGains_EX7_064()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-GK", "[Main] 1 of your Digimon gets ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon and survives the battle, it performs any security checks it normally would.) and ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn.");
        AssertFull(card);
        var mine = Put(state.Player, Digimon("B41-GKM", 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.True(CardScripts.HasKeyword(mine, Keyword.Piercing));
        Assert.True(CardScripts.HasKeyword(mine, Keyword.Blocker));
    }

    // =====================================================================
    // 3. "다음에 ~ 진화할 때" (ST12-15 · BT1-109 · EX5-029 · BT14-013)
    // =====================================================================

    [Fact]
    public void NextTimeDigivolve_ReducesOnlyOnce_ST12_15()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-NX", "[Main] The next time one of your Digimon would digivolve this turn, reduce the digivolution cost by 1.");
        AssertFull(card);
        var a = Put(state.Player, Digimon("B41-NXA", 3), turnPlayed: 1);
        var b = Put(state.Player, Digimon("B41-NXB", 3), turnPlayed: 1);
        var evo1 = Card("B41-NX4A", type: CardType.Digimon, level: 4, evoCost: 3);
        var evo2 = Card("B41-NX4B", type: CardType.Digimon, level: 4, evoCost: 3);
        state.Player.Hand.Add(evo1); state.Player.Hand.Add(evo2);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, a, evo1));
        Assert.True(engine.EvolveDigimon(a, evo1).CanExecute);
        Assert.Equal(8 - 2, state.MemoryGauge);
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, b, evo2));   // 한 번만
        Assert.Equal("【메인】 이 턴 동안, 다음에 자신의 디지몬이 진화할 때, 지불하는 진화 코스트를 -1 한다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void NextTimeDigivolve_LevelAndColorCondition_BT1_109()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-BT1109", "[Main] For the turn, the next time you would digivolve one of your green Digimon from level 5 to level 6, decrease the digivolution cost by 4.");
        AssertFull(card);
        var g5 = Put(state.Player, Digimon("B41-G5", 5, color: CardColor.Green));
        var b5 = Put(state.Player, Digimon("B41-B5", 5, color: CardColor.Blue));
        var g6 = Card("B41-G6", type: CardType.Digimon, level: 6, color: CardColor.Green, evoCost: 5);
        var b6 = Card("B41-B6", type: CardType.Digimon, level: 6, color: CardColor.Blue, evoCost: 5);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Equal(5, engine.PreviewEvoCost(state.Player, b5, b6));   // 블루는 아니다
        Assert.Equal(1, engine.PreviewEvoCost(state.Player, g5, g6));
        Assert.Equal("【메인】 이 턴 동안, 다음에 그린인 자신의 디지몬이 Lv.5에서 Lv.6으로 진화할 때, 지불하는 진화 코스트를 -4 한다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void NextTimeDigivolve_ExpiresAtTurnEnd()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-NXE", "[Main] The next time one of your Digimon would digivolve this turn, reduce the digivolution cost by 1.");
        var a = Put(state.Player, Digimon("B41-NXEA", 3));
        var evo = Card("B41-NXE4", type: CardType.Digimon, level: 4, evoCost: 3);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        state.TurnSequence++;   // 다음 턴
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, a, evo));
    }

    [Fact]
    public void ForTheTurnThisDigimonDigivolve_NotOneShot_BT14_013()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-BT14013", 4, "[Start of Your Main Phase] For the turn, when this Digimon would digivolve into a card with [Tyrannomon] in its name, reduce the digivolution cost by 1.", color: CardColor.Red);
        AssertFull(card);
        var me = Put(state.Player, card);
        var other = Put(state.Player, Digimon("B41-OTH4", 4, color: CardColor.Red));
        var tyr = Card("B41-TYR", type: CardType.Digimon, level: 5, color: CardColor.Red, evoCost: 3, name: "MasterTyrannomon");
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, me, card);
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, me, tyr));
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, other, tyr));   // 이 디지몬만
    }

    // =====================================================================
    // 4. 진화원 조작
    // =====================================================================

    [Fact]
    public void OpponentDigimonUnderOpponentDigimon_BT11_088()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-BT11088", 6, "[On Play] Place 1 of your opponent's Digimon under 1 of your opponent's other Digimon as its bottom digivolution card.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var victim = Put(state.DummyOpponent, Digimon("B41-VIC", 3), sources: Digimon("B41-VICS", 2));
        var host = Put(state.DummyOpponent, Digimon("B41-HST", 5));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        var left = Assert.Single(state.DummyOpponent.BattleArea);   // 한 마리가 다른 한 마리의 진화원 아래로
        var gone = ReferenceEquals(left, host) ? victim : host;
        Assert.Contains(left.EvolutionSources, s => ReferenceEquals(s, gone.TopCard));
        if (ReferenceEquals(gone, victim)) Assert.Contains(state.DummyOpponent.Trash, s => s.CardId == "B41-VICS");   // 아래에 놓이는 디지몬의 진화원은 트래시 (종합 룰 4-7-7)
        Assert.StartsWith("【등장 시】 상대의 디지몬 1마리를 ", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void OpponentDigimonUnderTamer_WhenNoOtherDigimon_EX10_056()
    {
        var (state, engine, choices) = NewMainPhase();
        var card = Digimon("B41-EX10056", 5, "[On Play] You may place 1 of your opponent's Digimon as any of their other Digimon's bottom digivolution card or under any of their Tamers.");
        AssertFull(card);
        var me = Put(state.Player, card);
        Put(state.DummyOpponent, Digimon("B41-ALONE", 4));
        state.DummyOpponent.Tamers.Add(Tamer("B41-OT"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Empty(state.DummyOpponent.BattleArea);
        Assert.Contains(state.DummyOpponent.UnderTamers, c => c.CardId == "B41-ALONE");
    }

    [Fact]
    public void TrashTopCardOfThisDigimon_BT13_058()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-BT13058", 6, "[End of Your Turn] Trash the top card of this Digimon, and then unsuspend all of your Digimon.");
        AssertFull(card);
        var five = Digimon("B41-T5", 5);
        var me = Put(state.Player, card, suspended: true, sources: five);
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, me, card);
        Assert.Same(five, me.TopCard);
        Assert.Contains(card, state.Player.Trash);
        Assert.False(me.IsSuspended);
    }

    [Fact]
    public void AtTheBottomOfDigivolutionCards_Variant_BT7_039()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-BT7039", 5, "[When Digivolving] You may place up to 2 level 4 or lower yellow Digimon cards from your hand at the bottom of this Digimon's digivolution cards in any order.", color: CardColor.Yellow);
        AssertFull(card);
        var me = Put(state.Player, card);
        state.Player.Hand.Add(Digimon("B41-Y3", 3, color: CardColor.Yellow));
        state.Player.Hand.Add(Digimon("B41-Y4", 4, color: CardColor.Yellow));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Equal(2, me.EvolutionSources.Count);
    }

    // =====================================================================
    // 5. 조건
    // =====================================================================

    [Fact]
    public void OrCondition_BT21_010_Style()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-OR", 3, "[Your Turn] [On Play] If you have 2 or fewer security cards or 3 or more [Hero] trait Tamers with different names, ＜Draw 1＞.");
        AssertFull(card);
        var me = Put(state.Player, card);
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(hand, state.Player.Hand.Count);   // 시큐리티 5장 · 테이머 없음
        state.Player.Tamers.Add(Tamer("B41-H1", name: "A", traits: "Hero"));
        state.Player.Tamers.Add(Tamer("B41-H2", name: "B", traits: "Hero"));
        state.Player.Tamers.Add(Tamer("B41-H3", name: "B", traits: "Hero"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(hand, state.Player.Hand.Count);   // 명칭이 서로 다른 것은 2명
        state.Player.Tamers.Add(Tamer("B41-H4", name: "C", traits: "Hero"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    [Fact]
    public void DarkMastersOnlyCondition_EX10_012()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-DM", "[Main] If you don't have any Digimon other than Digimon with [Dark Masters] in their texts, ＜Draw 1＞.");
        AssertFull(card);
        Put(state.Player, Digimon("B41-DM1", 5, "[On Play] If you have [Dark Masters], ＜Draw 1＞."));
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
        Put(state.Player, Digimon("B41-NODM", 3));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    [Fact]
    public void NamedDigimonWithSourcesCondition_EX2_053()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B41-MDR", "[Main] If one of your [Mother D-Reaper]s has 2 or more digivolution cards, ＜Draw 1＞.");
        AssertFull(card);
        var mom = Put(state.Player, Digimon("B41-MOM", 6, name: "Mother D-Reaper"), sources: Digimon("B41-MS1", 5));
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Equal(hand, state.Player.Hand.Count);
        mom.AddBottomSource(Digimon("B41-MS2", 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    // =====================================================================
    // 6. 효과 발휘 ("activate ~ effects")
    // =====================================================================

    [Fact]
    public void ActivateThisDigimonsOnPlay_EX5_059()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-EX5059", 5, "[On Play] ＜Draw 1＞.\n[When Digivolving] If [Dobermon] is in this Digimon's digivolution cards, activate this Digimon's [On Play] effects.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: Digimon("B41-DOB", 4, name: "Dobermon"));
        int hand = state.Player.Hand.Count;
        Diag41.Reset();
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
        Assert.Equal(1, Diag41.ActivateEffect);
        Assert.Equal("【진화 시】 이 디지몬의 진화원에 「도베르몬」이 있을 때, 이 디지몬의 【등장 시】 효과를 발휘한다.", KoreanEffectRenderer.RenderClause("[When Digivolving] If [Dobermon] is in this Digimon's digivolution cards, activate this Digimon's [On Play] effects."));
    }

    [Fact]
    public void ActivatePlacedCardsOnPlay_AsThisDigimon_BT18_066()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-BT18066", 5, "[On Play] By placing 1 level 4 or lower card with the [Hybrid] trait from your hand or trash as this Digimon's bottom digivolution card, you may activate 1 [On Play] effect on the placed card as an effect of this Digimon.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var hybrid = Digimon("B41-HYB", 4, "[On Play] This Digimon gets +3000 DP for the turn.", traits: "Hybrid");
        state.Player.Hand.Add(hybrid);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(hybrid, me.EvolutionSources);
        Assert.Equal(5000 + 3000, me.GetCurrentDP());   // 놓은 카드의 【등장 시】를 이 디지몬의 효과로서
    }

    [Fact]
    public void ActivateEffects_NoRecursionLoop()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B41-LOOP", 5, "[On Play] ＜Draw 1＞. Then, activate this Digimon's [On Play] effects.");
        var me = Put(state.Player, card);
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.InRange(state.Player.Hand.Count - hand, 1, 4);   // 끝없이 이어지지 않는다
    }

    // =====================================================================
    // 7. 링크 코스트 감소 (BT25-004 · BT25-045) · AI 링크 (≪분리≫ 지정 카드 먼저)
    // =====================================================================

    [Fact]
    public void LinkCostReduction_OncePerTurn_BT25_045()
    {
        var (state, engine, _) = NewMainPhase();
        var host = Digimon("B41-BT25045", 4, "[Your Turn] [Once Per Turn] When a [Social], [Tool] or [Game] trait card would link to this Digimon, you may reduce the cost by 1.", traits: "Appmon");
        AssertFull(host);
        var h = Put(state.Player, host);
        var link = Digimon("B41-LNK", 3, inherited: "Link Requirements [Link] [Appmon] trait: Cost 2 \r\n(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", traits: new[] { "Game", "Appmon" });
        var link2 = Digimon("B41-LNK2", 3, inherited: "Link Requirements [Link] [Appmon] trait: Cost 2 \r\n(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", traits: new[] { "Game", "Appmon" });
        state.Player.Hand.Add(link); state.Player.Hand.Add(link2);
        Assert.True(engine.LinkFromHand(link, h).CanExecute);
        Assert.Equal(8 - 1, state.MemoryGauge);
        h.LinkedCards.Clear();
        Assert.True(engine.LinkFromHand(link2, h).CanExecute);
        Assert.Equal(7 - 2, state.MemoryGauge);   // [턴에 1회]
    }

    [Fact]
    public void LinkCostReduction_InheritedDuplicateRegisteredOnce_BT25_004()
    {
        var tap = Digimon("B41-BT25004", 2,
            "Inherited Effect [Your Turn] [Once Per Turn] When a [Social], [Tool] or [Game] trait card would link to this Digimon, you may reduce the cost by 1.",
            "[Once Per Turn] When a [Social|Tool|Game] trait card would link to this Digimon, you may reduce the cost by 1.", traits: new[] { "Tap (App Name)", "Appmon", "Tool" });
        AssertFull(tap);
        var script = CardScripts.For(tap)!;
        var mod = Assert.Single(script.LinkCostModifiers41);
        Assert.Equal(EffectSource.Inherited, mod.Source);
        Assert.True(mod.YourTurnOnly);
    }

    [Fact]
    public void AiPrefersDetachSpecifiedLink()
    {
        var (state, engine, _) = NewMainPhase();
        var detach = Digimon("B41-DET", 3, "＜Detach ([Seven Code] trait)＞ (When this Digimon would leave the battle area other than by your effects, by trashing 1 of its specified link cards, it doesn't leave.)", traits: new[] { "Mail (App Name)", "Seven Code", "Appmon" });
        var plain = Digimon("B41-PLN", 5, traits: "Appmon");
        var d1 = Put(state.DummyOpponent, plain);
        var d2 = Put(state.DummyOpponent, detach);
        var seven = Digimon("B41-7C", 3, inherited: "Link Requirements [Link] [Appmon] trait: Cost 1 \r\n(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", traits: new[] { "Role (App Name)", "Seven Code", "Appmon" });
        state.DummyOpponent.Hand.Add(seven);
        var pick = engine.ChooseAiLink41(state.DummyOpponent, 3);
        Assert.NotNull(pick);
        Assert.Same(d2, pick!.Value.Target);
        Assert.Null(engine.ChooseAiLink41(state.DummyOpponent, 0));   // 메모리를 넘기지 않는다
    }

    [Fact]
    public void AppmonLinkRequirement_AcceptsAppNameTrait_SevenCodeDetach()
    {
        var (state, engine, _) = NewMainPhase();
        var detach = Digimon("B41-DET2", 3, "＜Detach ([Seven Code] trait)＞ (When this Digimon would leave the battle area other than by your effects, by trashing 1 of its specified link cards, it doesn't leave.)", traits: new[] { "Role-playing (App Name)", "Seven Code", "Stnd." });
        var d = Put(state.DummyOpponent, detach);
        var gomi = Digimon("B41-GOMI", 3, inherited: "Link Requirements [Link] [Appmon] trait: Cost 3 \r\n(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)", traits: new[] { "Trashbin (App Name)", "Seven Code", "Stnd." });
        Assert.True(GoldfishEngine.MeetsLinkRequirement(gomi, d) == true);   // 영어 데이터에 [Appmon]이 빠졌어도 "(App Name)" = 어플몬
        state.DummyOpponent.Hand.Add(gomi);
        var pick = engine.ChooseAiLink41(state.DummyOpponent, 3);
        Assert.Same(d, pick!.Value.Target);
        Assert.False(GoldfishEngine.MeetsLinkRequirement(gomi, Put(state.DummyOpponent, Digimon("B41-NOAPP", 4, traits: "Vaccine"))) == true);
    }

    // =====================================================================
    // 8. ≪계승≫ (BT26-032) · 「라센몬」 ≪디지버스트≫ (BT8-081)
    // =====================================================================

    [Fact]
    public void Succession_GainsOnlyTopmostSpecified_BT26_032()
    {
        var (state, engine, _) = NewMainPhase();
        var top = Digimon("B41-BT26032", 6, "＜Succession ([Ceresmon])＞ (This Digimon gains all effects other than ＜Succession＞ on its topmost specified digivolution card.)");
        AssertFull(top);
        var upper = Digimon("B41-CER1", 5, "＜Piercing＞", name: "Ceresmon");
        var lower = Digimon("B41-CER2", 5, "＜Blocker＞", name: "Ceresmon");
        var me = Put(state.Player, top, sources: new[] { upper, lower });   // upper가 위 (AddBottomSource 순서)
        Assert.True(CardScripts.HasKeyword(me, Keyword.Piercing));
        Assert.False(CardScripts.HasKeyword(me, Keyword.Blocker));
        Assert.StartsWith("《계승 (「케레스몬」)》", KoreanEffectRenderer.RenderClause(top.EffectText));
    }

    [Fact]
    public void RasenmonDigiBurst_TrashedSourceUnsuspends_BT8_081()
    {
        var (state, engine, _) = NewMainPhase();
        var fury = Digimon("BT8-081", 6, inherited: "[Your Turn] When this card is trashed as a digivolution card by the effect of [Rasenmon], unsuspend 1 of your Digimon and it gets +3000 DP for the turn.", name: "Rasenmon Fury Mode");
        AssertFull(fury);
        var rasen = Put(state.Player, Digimon("B41-RASEN", 6, dp: 11000, name: "Rasenmon"), sources: new[] { Digimon("B41-RS5", 5), fury });
        rasen.IsSuspended = true;
        Diag40.Reset();
        engine.PayDigiBurst(state.Player, rasen, 1);   // 맨 아래 = BT8-081
        Assert.Contains(fury, state.Player.Trash);
        Assert.True(Diag40.EventMisc > 0);   // 「라센몬」의 ≪디지버스트≫로 진화원에서 파기되었을 때
        Assert.False(rasen.IsSuspended);
        Assert.Equal(11000 + 3000, rasen.GetCurrentDP());
        // 「라센몬」 이외의 ≪디지버스트≫로는 발휘하지 않는다
        var (state2, engine2, _) = NewMainPhase();
        var other = Put(state2.Player, Digimon("B41-OTHER", 6, name: "Other"), sources: new[] { Digimon("B41-OS5", 5), fury });
        var tired2 = Put(state2.Player, Digimon("B41-TIRED2", 4), suspended: true);
        engine2.PayDigiBurst(state2.Player, other, 1);
        Assert.True(tired2.IsSuspended);
    }

    // =====================================================================
    // 9. 테이머를 세는 "명" (공식 "자신의 테이머가 1명 이하라면") — "테이머 카드 1장" · "디지몬 또는 테이머 1장"은 그대로
    // =====================================================================

    [Fact]
    public void TamerCountKorean_UsesMyeong()
    {
        Assert.Contains("테이머가 1명 이하", KoreanEffectRenderer.RenderClause("[When Digivolving] If you have 1 or fewer Tamers, ＜Draw 1＞."));
        Assert.Contains("테이머 1명마다", KoreanEffectRenderer.RenderClause("[Your Turn] This Digimon gets +1000 DP for each of your Tamers."));
        Assert.Contains("테이머 카드 1장", KoreanEffectRenderer.RenderClause("[On Play] You may play 1 Tamer card from your hand without paying the cost."));
        Assert.Equal("테이머가 2명 · 테이머 3명마다 · 디지몬 또는 테이머 1장 · 테이머 카드 1장", KoreanEffectRenderer.TamerCount41("테이머가 2장 · 테이머 3장마다 · 디지몬 또는 테이머 1장 · 테이머 카드 1장"));
    }
}
