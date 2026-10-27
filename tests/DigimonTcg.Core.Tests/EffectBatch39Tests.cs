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
/// v0.39.0 미지원 문장 13차 묶음: 강제 어택 ("that Digimon attacks" — 종합 룰 15-9-1 · Q&A BT21-081 · BT20-102) · 이름 제외 고르기 · 여러 곳에서 등장 · 사용 ·
/// 상한 늘리기 · 줄이기 · 상대 트래시 비용 · 시큐리티 ("At the end of the battle" · 서로의 시큐리티 · N장이 되도록) · 테이머 아래에서 진화 · 정한 턴 종료 시 되돌리기
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch39Tests
{
    /// <summary>질문에 정한 답을 하는 선택기 — 강제 어택이 "어택할까요?"를 묻지 않는지, 대상을 플레이어가 고르는지 본다</summary>
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public int Confirms;
        public Func<IReadOnlyList<CardInstance>, bool, CardInstance?>? Pick;
        public bool? LastOptional;
        public int? OptionAnswer;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Confirms++; return ConfirmAnswer; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional)
        {
            LastOptional = optional;
            return Pick != null ? Pick(options, optional) : recommended ?? options.FirstOrDefault();
        }
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => OptionAnswer ?? recommended;
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

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue, string? name = null) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name);

    private static (GameState State, GoldfishEngine Engine, Scripted Choices) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B39-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B39-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B39-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B39-OSEC" + i, 3, dp: 1000, color: CardColor.White));
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

    private static bool Has(PlayerZones z, CardData top) => z.BattleArea.Any(d => ReferenceEquals(d.TopCard, top));

    // =====================================================================
    // 0. 강제 어택 (종합 룰 15-9-1 강제 처리 · Q&A BT21-081 · BT23-070 · BT20-102 · BT24-051)
    // =====================================================================

    [Fact]
    public void ThatDigimonAttacks_IsNotAskedEvenIfPlayerWouldDecline_EX7_013()
    {
        var (state, engine, choices) = NewMainPhase();
        choices.ConfirmAnswer = true;   // 비용(옵션 파기)만 "예"
        var opt = Card("B39-OPTSRC", type: CardType.Option);
        var card = Digimon("B39-EX7013", 6, "[End of Your Turn] [Once Per Turn] By trashing 1 Option card in this Digimon's digivolution card, 1 of your Digimon gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for the turn and that Digimon attacks.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: opt);
        int confirmsBefore = choices.Confirms;
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, me, card);
        Assert.Equal(confirmsBefore + 1, choices.Confirms);   // 비용 1번만 묻고, 어택은 묻지 않는다
        Assert.Equal(3, state.DummyOpponent.SecurityStack.Count);   // 《S 어택 +1》로 2장 체크
        Assert.True(Diag39.ForcedAttack > 0);
    }

    [Fact]
    public void ThisDigimonAttacksPlayer_Forced_BT19_062()
    {
        var (state, engine, choices) = NewMainPhase();
        choices.ConfirmAnswer = false;   // 무엇을 물어도 "아니요" — 강제 어택은 묻지 않으니 그대로 어택
        var card = Digimon("B39-BT19062", 5, "[End of Your Turn] If your opponent has an unsuspended Digimon, this Digimon attacks a player.", dp: 9000);
        AssertFull(card);
        var me = Put(state.Player, card);
        Put(state.DummyOpponent, Digimon("B39-OUNS", 3));
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, me, card);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.True(me.IsSuspended);
    }

    [Fact]
    public void ThisDigimonAttacksWithoutSuspending_Forced_PlayerChoosesTarget_BT23_070()
    {
        var (state, engine, choices) = NewMainPhase();
        var victim = Put(state.DummyOpponent, Digimon("B39-VICT", 4, dp: 3000), suspended: true);
        choices.Pick = (opts, optional) => opts.First();   // 플레이어가 디지몬을 고른다
        var card = Digimon("B39-BT23070", 6, "[When Digivolving] This Digimon attacks without suspending.", dp: 12000);
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.True(choices.LastOptional);   // 시큐리티에도 어택할 수 있으니 "고르지 않음" = 시큐리티
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);
        Assert.False(me.IsSuspended);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void MayGainAndAttack_DeclineSkipsBoth_AcceptForcesAttack_BT20_102()
    {
        const string text = "[End of Your Turn] [Once Per Turn] 1 of your Digimon may gain ＜Rush＞ (This Digimon can attack the turn it comes into play.) for the turn and attack without suspending.";
        var (state, engine, choices) = NewMainPhase();
        var card = Digimon("B39-BT20102", 7, text, dp: 13000);
        var me = Put(state.Player, card);
        choices.ConfirmAnswer = false;
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, me, card);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);
        Assert.DoesNotContain(me.TimedKeywords, k => k.Keyword == Keyword.Rush);   // 발휘하지 않으면 얻지도 않는다 (v0.38.0은 얻고 어택만 고름)

        var (state2, engine2, choices2) = NewMainPhase();
        var card2 = Digimon("B39-BT20102B", 7, text, dp: 13000);
        var me2 = Put(state2.Player, card2);
        choices2.ConfirmAnswer = true;
        int before = choices2.Confirms;
        engine2.RunEffects(EffectTrigger.EndOfYourTurn, state2.Player, me2, card2);
        Assert.Equal(before + 1, choices2.Confirms);   // "발휘할까요?" 한 번 — 어택은 묻지 않는다 (Q&A BT20-102)
        Assert.Equal(4, state2.DummyOpponent.SecurityStack.Count);
        Assert.False(me2.IsSuspended);
    }

    [Fact]
    public void MayGetDpAndAttackDigimon_BT24_051()
    {
        var (state, engine, choices) = NewMainPhase();
        var victim = Put(state.DummyOpponent, Digimon("B39-V51", 5, dp: 8000), suspended: true);
        var card = Digimon("B39-BT24051", 5, "[On Play] 1 of your Digimon may get +5000 DP for the turn and attack your opponent's Digimon.", dp: 5000);
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);   // 10000 > 8000
        Assert.Equal("【등장 시】 이 턴 동안, 자신의 디지몬 1마리의 DP를 +5000 하고, 그 디지몬으로 상대의 디지몬에게 어택할 수 있다.",
            KoreanEffectRenderer.RenderClause("[On Play] 1 of your Digimon may get +5000 DP for the turn and attack your opponent's Digimon."));
    }

    [Fact]
    public void GrantedForcedAttack_CannotAttackPlayers_TargetNotOptional()
    {
        var (state, engine, choices) = NewMainPhase();
        var victim = Put(state.DummyOpponent, Digimon("B39-GV", 3, dp: 2000), suspended: true);
        Put(state.DummyOpponent, Digimon("B39-GV2", 3, dp: 2500), suspended: true);
        choices.Pick = (opts, optional) => opts.First(o => ReferenceEquals(o, victim));
        var card = Digimon("B39-GFA", 5, "[Your Turn] This Digimon can't attack players.", dp: 7000);
        var me = Put(state.Player, card);
        me.IsOwnersTurn = true;
        me.GrantedEffects.Add((Digimon("B39-GRANTED", 0, "[Start of Your Main Phase] This Digimon attacks."), 99, null));
        engine.RecalculateFieldBuffs();
        Assert.False(EffectRules.CanAttackPlayer(me));
        Assert.True(engine.ForcedAttack(state.Player, me, "테스트"));
        Assert.False(choices.LastOptional ?? true);   // 플레이어에게 어택할 수 없으니 "고르지 않음"(=시큐리티)이 없다
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);
    }

    // =====================================================================
    // 1. 이름 제외 · 같은 명칭
    // =====================================================================

    [Fact]
    public void WithoutNameInSources_Cost_BT11_073()
    {
        var (state, engine, _) = NewMainPhase();
        var accel = Digimon("B39-ACCEL", 6, name: "Justimon: Accel Arm");
        var blitz = Digimon("B39-BLITZ", 6, name: "Justimon: Blitz Arm");
        var card = Digimon("B39-BT11073", 7, "[When Digivolving] By returning 1 level 6 Digimon card without [Justimon: Accel Arm] in its name from this Digimon's digivolution cards to its owner's hand, this Digimon gains ＜Piercing＞ for the turn.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: new[] { accel, blitz });
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Contains(blitz, state.Player.Hand);
        Assert.DoesNotContain(accel, state.Player.Hand);
    }

    [Fact]
    public void OtherThanSeaAnimal_TraitPick_EX13_042()
    {
        var (state, engine, _) = NewMainPhase();
        var sea = Digimon("B39-SEA", 4, traits: "Sea Animal");
        var beast = Digimon("B39-BEAST", 4, traits: "Beast");
        state.Player.Hand.Add(sea);
        state.Player.Hand.Add(beast);
        var card = Digimon("B39-EX13042", 5, "[When Digivolving] You may play 1 play cost 4 or lower Digimon card with [Beast], [Animal] or [Sovereign], other than [Sea Animal], in any of its traits from your hand without paying the cost.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.True(Has(state.Player, beast));
        Assert.False(Has(state.Player, sea));
    }

    [Fact]
    public void NamedTargetWithoutSource_BT19_078()
    {
        var (state, engine, _) = NewMainPhase();
        var jeri = Digimon("B39-JERI", 3, name: "ADR-01 Jeri");
        var withJeri = Put(state.Player, Digimon("B39-MDR1", 6, name: "Mother D-Reaper"), sources: jeri);
        var plain = Put(state.Player, Digimon("B39-MDR2", 6, name: "Mother D-Reaper"));
        var card = Digimon("B39-BT19078", 4, "[Main] Place this Digimon as the bottom digivolution card of 1 of your [Mother D-Reaper] without [ADR-01 Jeri] in its digivolution cards.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, me, card);
        Assert.Contains(card, plain.EvolutionSources);
        Assert.DoesNotContain(card, withJeri.EvolutionSources);
        Assert.Equal("【메인】 이 디지몬을 진화원에 「ADR-01=쥬리」가 없는 자신의 「마더 디・리퍼」 1마리의 진화원 아래에 놓는다.",
            KoreanEffectRenderer.RenderClause("[Main] Place this Digimon as the bottom digivolution card of 1 of your [Mother D-Reaper] without [ADR-01 Jeri] in its digivolution cards."));
    }

    // =====================================================================
    // 2. 여러 곳에서 등장 · 사용
    // =====================================================================

    [Fact]
    public void PlayFromOneOfYourBlueDigimonsSources_EX3_022()
    {
        var (state, engine, _) = NewMainPhase();
        var lv3 = Digimon("B39-L3", 3, color: CardColor.Blue);
        var host = Put(state.Player, Digimon("B39-HOST", 5, color: CardColor.Blue), sources: lv3);
        var card = Digimon("B39-EX3022", 4, "[When Attacking] You may play 1 blue level 3 Digimon card from 1 of your blue Digimon's digivolution cards without paying the cost.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.True(Has(state.Player, lv3));
        Assert.Empty(host.EvolutionSources);
        Assert.Equal("【어택 시】 블루인 자신의 디지몬 1마리의 진화원에서 Lv.3인 블루의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다.",
            KoreanEffectRenderer.RenderClause("[When Attacking] You may play 1 blue level 3 Digimon card from 1 of your blue Digimon's digivolution cards without paying the cost."));
    }

    [Fact]
    public void PlayFromHandOrThisDigimonsSources_BT24_065()
    {
        var (state, engine, _) = NewMainPhase();
        var diab = Digimon("B39-DIAB", 6, name: "Diaboromon");
        var card = Digimon("B39-BT24065", 6, "[On Play] You may play 1 [Diaboromon] from your hand or this Digimon's digivolution cards without paying the cost.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: diab);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.True(Has(state.Player, diab));
        Assert.Empty(me.EvolutionSources);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReturnToHandOrPlay_BT18_017(int option)
    {
        var (state, engine, choices) = NewMainPhase();
        choices.OptionAnswer = option;
        var red3 = Digimon("B39-R3", 3, color: CardColor.Red);
        var card = Digimon("B39-BT18017", 5, "[On Deletion] You may return to the hand or play 1 level 4 or lower red Digimon card from this Digimon's digivolution cards without paying the cost.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: red3);
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, me, card);
        Assert.Equal(option == 1, Has(state.Player, red3));
        Assert.Equal(option == 0, state.Player.Hand.Contains(red3));
    }

    [Fact]
    public void PlayOneEach_FromThisDigimonsSources_BT10_027()
    {
        var (state, engine, _) = NewMainPhase();
        var a = Digimon("B39-A3", 3); var b = Digimon("B39-B4", 4); var c3 = Digimon("B39-C3", 3);
        var card = Digimon("B39-BT10027", 5, "[When Attacking] You may play 1 level 3 Digimon card and 1 level 4 Digimon card from this Digimon's digivolution cards without paying their memory costs.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: new[] { a, b, c3 });
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.True(Has(state.Player, b));
        Assert.Equal(1, new[] { a, c3 }.Count(x => Has(state.Player, x)));   // Lv.3은 1장만
    }

    [Fact]
    public void UseOptionFromSources_BT25_085()
    {
        var (state, engine, _) = NewMainPhase();
        var opt = Card("B39-OPT", "[Main] Gain 2 memory.", CardType.Option, color: CardColor.Blue, traits: "TS");
        var card = Digimon("B39-BT25085", 6, "[When Attacking] You may use 1 [Three Musketeers] or [TS] trait Option card from your hand or this Digimon's digivolution cards without paying the cost.");
        AssertFull(card);
        var me = Put(state.Player, card, sources: opt);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Equal(mem + 2, state.MemoryGauge);
        Assert.Contains(opt, state.Player.Trash);
    }

    [Fact]
    public void PlayOrUseWithCostReduced_PaysReducedCost_EX12_043()
    {
        var (state, engine, _) = NewMainPhase();
        var sw = Digimon("B39-SW", 5, playCost: 6, traits: "SW");
        state.Player.Hand.Add(sw);
        var card = Digimon("B39-EX12043", 4, "[Main] [Once Per Turn] You may play or use 1 card with the [SW] trait from your hand with the cost reduced by 2.");
        AssertFull(card);
        var me = Put(state.Player, card);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, me, card);
        Assert.True(Has(state.Player, sw));
        Assert.Equal(mem - 4, state.MemoryGauge);
    }

    [Fact]
    public void OpponentPlaysFromTheirTrashSuspended_NoOnPlay_EX5_060()
    {
        var (state, engine, _) = NewMainPhase();
        var onPlay = Digimon("B39-OP4", 4, "[On Play] Gain 3 memory.");
        state.DummyOpponent.Trash.Add(onPlay);
        var card = Digimon("B39-EX5060", 6, "[On Play] Your opponent plays 1 level 4 or lower Digimon card from their trash suspended without paying the cost. [On Play] effects on Digimon played by this effect don't activate.");
        AssertFull(card);
        var me = Put(state.Player, card);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        var inst = state.DummyOpponent.BattleArea.Single(d => ReferenceEquals(d.TopCard, onPlay));
        Assert.True(inst.IsSuspended);
        Assert.Equal(mem, state.MemoryGauge);   // 【등장 시】 발휘하지 않음
    }

    [Fact]
    public void PlayLevelLessOrEqualToIt_TriggeredByOpponentPlayByEffect_EX5_060()
    {
        var (state, engine, _) = NewMainPhase();
        var p3 = Digimon("B39-P3", 3, color: CardColor.Purple); var p5 = Digimon("B39-P5", 5, color: CardColor.Purple);
        state.Player.Trash.Add(p5); state.Player.Trash.Add(p3);
        state.DummyOpponent.Trash.Add(Digimon("B39-OT4", 4));
        var card = Digimon("B39-EX5060B", 6, "[On Play] Your opponent plays 1 level 4 or lower Digimon card from their trash suspended without paying the cost. [On Play] effects on Digimon played by this effect don't activate.\n[All Turns] [Once Per Turn] When an effect plays an opponent's Digimon, you may play 1 purple Digimon card with a level less than or equal to it from your trash without paying the cost.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        engine.RecalculateFieldBuffs();
        Assert.Single(state.DummyOpponent.BattleArea);
        Assert.True(Has(state.Player, p3));
        Assert.False(Has(state.Player, p5));   // Lv.5 > 등장한 상대 디지몬의 Lv.4
    }

    [Fact]
    public void PlaceFromTrashAsThatDigimonsBottomSources_EX8_067()
    {
        var (state, engine, _) = NewMainPhase();
        var r1 = Card("B39-ROCK1", type: CardType.Digimon, level: 3, traits: "Rock"); var r2 = Card("B39-ROCK2", type: CardType.Digimon, level: 3, traits: "Mineral");
        state.Player.Trash.Add(r1); state.Player.Trash.Add(r2);
        var tamer = Tamer("B39-EX8067", "[Your Turn] When any of your Digimon digivolve into a [Mineral] or [Rock] trait Digimon, by suspending this Tamer, place up to 2 cards with the [Mineral] or [Rock] trait from your trash as that Digimon's bottom digivolution cards.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var evolved = Put(state.Player, Digimon("B39-ROCKD", 5, traits: "Rock"));
        engine.RunEffects(EffectTrigger.AllyDigimonDigivolved, state.Player, null, tamer, other: evolved, includeInherited: false);
        Assert.Contains(r1, evolved.EvolutionSources);
        Assert.Contains(r2, evolved.EvolutionSources);
    }

    [Fact]
    public void DigivolveIntoCardUnderTamers_BT19_008()
    {
        var (state, engine, _) = NewMainPhase();
        var omni = Digimon("B39-OMNI", 3, name: "OmniShoutmon", color: CardColor.Red);
        state.Player.Tamers.Add(Tamer("B39-TAM"));
        state.Player.UnderTamers.Add(omni);
        var card = Digimon("B39-BT19008", 2, "[On Play] This Digimon may digivolve into [OmniShoutmon] under your Tamers without paying the cost.", color: CardColor.Red);
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Same(omni, me.TopCard);
        Assert.DoesNotContain(omni, state.Player.UnderTamers);
    }

    // =====================================================================
    // 3. 상한 · 셈 · 비용
    // =====================================================================

    [Fact]
    public void RemoveFromDpDeletionMaximum_PerOpponentSecurity_BT15_089()
    {
        var (state, engine, _) = NewMainPhase();   // 상대 시큐리티 5장 → 15000 - 10000 = 5000
        var d5 = Put(state.DummyOpponent, Digimon("B39-O5K", 5, dp: 5000));
        var d6 = Put(state.DummyOpponent, Digimon("B39-O6K", 5, dp: 6000));
        var option = Card("B39-BT15089", "[Main] Delete 1 of your opponent's Digimon with 15000 DP or less. For each of your opponent's security cards, remove 2000 from this DP deletion effect's maximum.");
        AssertFull(option);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, option, chosenTarget: d6);
        Assert.Contains(d6, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(d5, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void AddToMaximum_PerDifferentlyNamedDigimon_EX7_066()
    {
        var (state, engine, _) = NewMainPhase();
        Put(state.Player, Digimon("B39-TM1", 5, name: "Athos", traits: "Three Musketeers"));
        Put(state.Player, Digimon("B39-TM2", 5, name: "Porthos", traits: "Three Musketeers"));
        Put(state.Player, Digimon("B39-TM3", 5, name: "Porthos", traits: "Three Musketeers"));
        var big = Put(state.DummyOpponent, Digimon("B39-O15K", 6, dp: 15000));
        var option = Card("B39-EX7066", "[Main] Delete 1 of your opponent's Digimon with 9000 DP or less. For each your Digimon with the [Three Musketeers] trait with different names, add 3000 to this DP-Based deletion effects maximum.");
        AssertFull(option);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, option);
        Assert.DoesNotContain(big, state.DummyOpponent.BattleArea);   // 9000 + 3000 × 2 = 15000
    }

    [Fact]
    public void ReturnDifferentLevelsFromOpponentsTrash_GainMemoryPerCard_BT18_019()
    {
        var (state, engine, _) = NewMainPhase();
        state.DummyOpponent.Trash.AddRange(new[] { Digimon("B39-T3", 3), Digimon("B39-T3B", 3), Digimon("B39-T4", 4), Digimon("B39-T5", 5) });
        var card = Digimon("B39-BT18019", 7, "[On Play] By returning 1 of each Digimon card with different levels from your opponent's trash to the top of the deck, gain 1 memory for each card returned.");
        AssertFull(card);
        var me = Put(state.Player, card);
        state.MemoryGauge = 2;   // 메모리 상한 10
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(mem + 3, state.MemoryGauge);
        Assert.Single(state.DummyOpponent.Trash);
    }

    [Fact]
    public void ReturnCardsInTrashes_ToEachOwnersDeck_BT26_016()
    {
        var (state, engine, _) = NewMainPhase();
        state.DummyOpponent.Trash.AddRange(new[] { Digimon("B39-OT1", 3), Digimon("B39-OT2", 3) });
        state.Player.Trash.Add(Digimon("B39-MT1", 3));
        int oDeck = state.DummyOpponent.MainDeck.Count, mDeck = state.Player.MainDeck.Count;
        var card = Digimon("B39-BT26016", 6, "[On Play] By returning 3 cards in trashes to the bottom of the deck, ＜Recovery +1＞ (Place the top card of your deck as your top security card.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Empty(state.DummyOpponent.Trash);
        Assert.Empty(state.Player.Trash);
        Assert.Equal(oDeck + 2, state.DummyOpponent.MainDeck.Count);
        Assert.Equal(6, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void PlaceCardUnderTamersAsSourceOfDigivolvingDigimon_ReducesCost_BT12_091()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B39-BT12091", "[Your Turn] When one of your Digimon would digivolve into a Digimon card with ＜Save＞ in its text, by suspending this Tamer and placing 1 card from under one of your Tamers as the bottom digivolution card of that Digimon, reduce the digivolution cost by 1.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var under = Digimon("B39-UNDER", 3);
        state.Player.UnderTamers.Add(under);
        var baseD = Put(state.Player, Digimon("B39-BASE4", 4));
        var evo = Digimon("B39-EVO5", 5, "＜Save＞ (test)");
        evo.EvoCost = 3;
        state.Player.Hand.Add(evo);
        int mem = state.MemoryGauge;
        Assert.True(engine.EvolveDigimon(baseD, evo).CanExecute);
        Assert.Same(evo, baseD.TopCard);
        Assert.Contains(under, baseD.EvolutionSources);
        Assert.Equal(mem - 2, state.MemoryGauge);   // 진화 코스트 3 - 1 = 2
    }

    [Fact]
    public void ReturnSourcesOfAttackingDigimonAsCost_BT18_092()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B39-BT18092", "[Your Turn] When any of your Digimon attack, by suspending this Tamer and returning 2 [Vemmon] from that Digimon's digivolution cards to the bottom of the deck, ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var v1 = Digimon("B39-VEM1", 2, name: "Vemmon"); var v2 = Digimon("B39-VEM2", 2, name: "Vemmon");
        var attacker = Put(state.Player, Digimon("B39-ATK5", 5), sources: new[] { v1, v2 });
        var victimTop = Digimon("B39-VTOP", 5);
        var victim = Put(state.DummyOpponent, victimTop, sources: Digimon("B39-VBOT", 4));
        engine.RunEffects(EffectTrigger.AllyDigimonAttacks, state.Player, null, tamer, other: attacker, includeInherited: false);
        Assert.Empty(attacker.EvolutionSources);
        Assert.Equal(new[] { v1, v2 }, state.Player.MainDeck.TakeLast(2));
        Assert.NotSame(victimTop, victim.TopCard);   // 《퇴화 1》
    }

    [Fact]
    public void AtEndOfOpponentsTurn_PlayFromTrash_Scheduled_BT13_089()
    {
        var (state, engine, _) = NewMainPhase();
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        var raven = Digimon("B39-RAVEN", 5, name: "Ravemon");
        state.Player.Trash.Add(raven);
        var card = Digimon("B39-BT13089", 5, "[On Play] At the end of your opponent's turn, play 1 [Ravemon] from your trash without paying its cost.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.False(Has(state.Player, raven));   // 아직 (상대의 턴 종료 시)
        engine.PassTurn();
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.True(Has(state.Player, raven));
        Assert.Equal("【등장 시】 상대의 턴 종료 시, 자신의 트래시에서 「레이브몬」 1장을 코스트를 지불하지 않고 등장시킨다.",
            KoreanEffectRenderer.RenderClause("[On Play] At the end of your opponent's turn, play 1 [Ravemon] from your trash without paying its cost."));
    }

    // =====================================================================
    // 4. 시큐리티
    // =====================================================================

    [Fact]
    public void TrashBothPlayersTopSecurity_BT16_036()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B39-BT16036", 6, "[End of Opponent's Turn] Trash the top card of both players' security stacks.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.EndOfOpponentsTurn, state.Player, me, card);
        Assert.Equal(4, state.Player.SecurityStack.Count);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.Equal("【상대의 턴 종료 시】 서로의 시큐리티를 위에서부터 1장 파기한다.", KoreanEffectRenderer.RenderClause("[End of Opponent's Turn] Trash the top card of both players' security stacks."));
    }

    [Fact]
    public void TrashOpponentSecuritySoThatOneLeft_BT21_098()
    {
        var (state, engine, _) = NewMainPhase();
        var option = Card("B39-BT21098", "[Main] Trash the top cards of your opponent's security stack so that it has 1 card left.");
        AssertFull(option);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, option);
        Assert.Single(state.DummyOpponent.SecurityStack);
    }

    [Fact]
    public void TrashOwnSecurityUntilThree_GainMemoryPerCard_BT6_033()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B39-BT6033", 5, "[On Play] You may trash cards from the top of your security stack until there are 3 left; gain 1 memory for each security card trashed.");
        AssertFull(card);
        var me = Put(state.Player, card);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(3, state.Player.SecurityStack.Count);
        Assert.Equal(mem + 2, state.MemoryGauge);
    }

    [Fact]
    public void OpponentPlacesHandCardAsBottomSecurity_ThenTrashTop_BT24_016()
    {
        var (state, engine, _) = NewMainPhase();
        var handCard = Digimon("B39-OHAND", 3);
        state.DummyOpponent.Hand.Add(handCard);
        var card = Digimon("B39-BT24016", 5, "[When Attacking] Your opponent places 1 card from their hand as the bottom security card. Then, trash their top security card.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, card);
        Assert.Same(handCard, state.DummyOpponent.SecurityStack[^1]);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);
    }

    [Fact]
    public void AddTopSecurityCardToHand_Rewrite_BT14_032()
    {
        var (state, engine, _) = NewMainPhase();
        var top = state.Player.SecurityStack[0];
        var card = Digimon("B39-BT14032", 4, "[On Play] Add the top card of your security stack to the hand. Then, you may place 1 card with [Sukamon] in its name from your hand on top of your security stack.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(top, state.Player.Hand);
    }

    [Fact]
    public void SecurityDigimon_AtEndOfBattle_DrawThenAddToHand_P_067()
    {
        var (state, engine, _) = NewMainPhase();
        var sec = Digimon("B39-P067", 4, "[Security] At the end of the battle, ＜Draw 2＞ (Draw 2 cards from your deck.) Then, add this card to its owner's hand.", dp: 3000);
        AssertFull(sec);
        state.DummyOpponent.SecurityStack.Insert(0, sec);
        int handBefore = state.DummyOpponent.Hand.Count;
        var attacker = Put(state.Player, Digimon("B39-ATK", 5, dp: 7000));
        Assert.True(engine.AttackSecurityOrDirect(attacker).CanExecute);
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.Contains(sec, state.DummyOpponent.Hand);   // 배틀이 끝난 뒤 트래시에서 패로
        Assert.Equal(handBefore + 3, state.DummyOpponent.Hand.Count);   // 2장 드로우 + 이 카드
        Assert.Contains(attacker, state.Player.BattleArea);   // 배틀은 했다 (7000 > 3000)
    }

    // =====================================================================
    // 5. 정한 턴 종료 시
    // =====================================================================

    [Fact]
    public void PlayedDigimon_ReturnsToHandAtNextEndOfOpponentsTurn_BT16_084()
    {
        var (state, engine, _) = NewMainPhase();
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        var hawk = Digimon("B39-HAWK", 3, name: "Hawkmon");
        state.Player.Hand.Add(hawk);
        var card = Digimon("B39-BT16084", 4, "[On Play] You may play 1 [Hawkmon] or [Salamon] from your hand without paying the cost. At the next end of your opponent's turn, return it to the hand.");
        AssertFull(card);
        var me = Put(state.Player, card);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.True(Has(state.Player, hawk));
        engine.PassTurn();   // 내 턴 종료 → 상대 턴 → 상대 턴 종료에 되돌린다
        while (state.PendingBlock != null) engine.ResolvePendingBlock(null);
        Assert.False(Has(state.Player, hawk));
        Assert.Contains(hawk, state.Player.Hand);
    }

    // =====================================================================
    // 6. 한글
    // =====================================================================

    [Theory]
    [InlineData("[End of Your Turn] If your opponent has an unsuspended Digimon, this Digimon attacks a player.", "【자신의 턴 종료 시】 액티브 상태인 상대의 디지몬이 있을 때, 이 디지몬으로 플레이어에게 어택한다.")]
    [InlineData("[On Deletion] You may play 1 [Wormmon] and 1 [Veemon] from your trash without paying the costs.", "【소멸 시】 자신의 트래시에서 「추추몬」 1장과 「브이몬」 1장을 코스트를 지불하지 않고 등장시킬 수 있다.")]
    [InlineData("[Main] Delete 1 of your opponent's Digimon with 15000 DP or less. For each of your opponent's security cards, remove 2000 from this DP deletion effect's maximum.", "【메인】 DP 15000 이하인 상대의 디지몬 1마리를 소멸시킨다. 상대의 시큐리티 1장마다 이 DP 소멸 효과의 상한 -2000.")]
    [InlineData("[Security] At the end of the battle, suspend 1 of your opponent's Digimon. Then, add this card to its owner's hand.", "【시큐리티】 배틀 종료 시, 상대의 디지몬 1마리를 레스트시킨다. 그 후, 이 카드를 패에 추가한다.")]
    [InlineData("[On Play] This Digimon may digivolve into [OmniShoutmon] under your Tamers without paying the cost.", "【등장 시】 이 디지몬을 자신의 테이머 아래의 「오메가샤우트몬」으로 코스트를 지불하지 않고 진화시킬 수 있다.")]
    public void KoreanRender(string en, string ko) => Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));

    [Theory]
    [InlineData("[When Digivolving] Your opponent plays 1 level 4 or lower Digimon card from their trash suspended without paying the cost.")]
    [InlineData("[On Play] You may play 1 [Labramon] or [Seasarmon] from your hand or from one of your Digimon's digivolution cards without paying the cost.")]
    [InlineData("[Main] You may play or use 1 card with [Gammamon] in its text or the [VB] trait from your hand with the cost reduced by 2.")]
    [InlineData("[When Attacking] 1 of your Digimon or Tamers may digivolve into a blue or red Digimon card with the [Hybrid] trait in the hand with the digivolution cost reduced by 1.")]
    [InlineData("[On Play] Play 1 [Familiar] Token. (Digimon/Yellow/3000 DP/[On Deletion] 1 of your opponent's Digimon gets -3000 DP for the turn.) At the end of your opponent's turn, delete that token.")]
    public void Supported(string text)
    {
        var card = Digimon("B39-SUP-" + text.GetHashCode(), 5, text);
        AssertFull(card);
        Assert.NotNull(KoreanEffectRenderer.RenderClause(text));
    }
}
