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
/// v0.59.0 미지원 문장 33차 묶음: ≪디지버스트≫로 파기할 진화원 고르기 · 상대의 효과를 받지 않는 동안의 지속 효과 · 얻은 특징과 진화 조건 ·
/// 시큐리티 ≪퇴화≫ 디지몬 1마리 + 배틀 종료 시 진화 · 【등장 시】 발휘 불가 기간 · 파기한 카드의 DP · 이름으로 고르는 테이머 레스트 ·
/// [트래시] 등장 · 패에서 파기된 카드로 진화 / 등장 · 상대의 패를 무작위로 덱 아래에 (비용)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch59Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, int evoCost = 1, string inherited = "", params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, string inherited = "", int playCost = 3, int evoCost = 1, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, evoCost, inherited, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int security = 5)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B59-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B59-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B59-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B59-OSEC" + i, 3, color: CardColor.White, dp: 1000));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsOwnersTurn = true };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    /// <summary>진화원 고르기만 답하는 선택기 (나머지는 추천 그대로)</summary>
    private sealed class PickCards : IChoiceProvider
    {
        public Func<IReadOnlyList<CardData>, IReadOnlyList<int>>? Cards { get; init; }
        public bool? ConfirmAnswer { get; init; }
        public List<string> Asked { get; } = new();
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Asked.Add("confirm:" + question); return ConfirmAnswer ?? recommended; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended;
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
        public IReadOnlyList<int> ChooseCards(GameState state, string question, CardChoiceZone zone, IReadOnlyList<CardData> options, int min, int max, IReadOnlyList<int> recommended)
        {
            Asked.Add($"cards:{zone}:{question}");
            return Cards?.Invoke(options) ?? recommended;
        }
    }

    // =====================================================================
    // 1-a. ≪디지버스트≫ — 파기할 진화원은 주인이 고른다 (종합 룰 16-14-1)
    // =====================================================================

    private const string Burst1 = "[Main] ＜Digi-Burst 1＞ (You may trash 1 of this Digimon's digivolution cards to activate the effect below.)\r\n・Gain 1 memory.";
    private const string BurstReturn = "[Your Turn] When this card is trashed due to activating this Digimon's ＜Digi-Burst＞, return this card to its owner's hand.";

    [Fact]
    public void DigiBurst_PlayerChoosesWhichSource()
    {
        var me = Digimon(Uid("B59-BURST-"), 6, Burst1);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var bottom = Digimon("B59-BOTTOM", 3);
        var middle = Digimon("B59-MIDDLE", 4);
        var top = Digimon("B59-TOP", 5);
        var d = Put(state.Player, me, top, middle, bottom);   // AddBottomSource: 아래 → 위 = bottom, middle, top
        var choices = new PickCards { Cards = opts => new[] { opts.ToList().FindIndex(x => ReferenceEquals(x, top)) } };
        engine.PlayerChoices = choices;
        Assert.True(engine.ActivateMainAbility(d, me).CanExecute, Logs(state));
        Assert.Contains(choices.Asked, a => a.StartsWith("cards:DigivolutionCards"));
        Assert.Equal(new[] { bottom, middle }, d.GetEvolutionStackBottomUp().ToArray());
        Assert.Same(top, Assert.Single(state.Player.Trash));
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void DigiBurst_AiTrashesBurstReturnCardFirst()
    {
        var me = Digimon(Uid("B59-BURST-"), 6, Burst1);
        var (state, engine) = NewMainPhase();
        var bottom = Digimon("B59-BOTTOM", 3);
        var ret = Digimon(Uid("B59-RET-"), 4, inherited: BurstReturn);
        var d = Put(state.Player, me, ret, bottom);   // 아래 → 위 = bottom, ret
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, d, me);
        Assert.Equal(new[] { bottom }, d.GetEvolutionStackBottomUp().ToArray());
        Assert.Contains(ret, state.Player.Hand);   // 파기된 카드의 효과로 패에
    }

    [Fact]
    public void DigiBurst_AllSourcesNotAsked()
    {
        var me = Digimon(Uid("B59-BURST-"), 6, Burst1);
        var (state, engine) = NewMainPhase();
        var only = Digimon("B59-ONLY", 5);
        var d = Put(state.Player, me, only);
        var choices = new PickCards { ConfirmAnswer = true };
        engine.PlayerChoices = choices;
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, d, me);
        Assert.DoesNotContain(choices.Asked, a => a.StartsWith("cards:"));
        Assert.Empty(d.EvolutionSources);
    }

    // =====================================================================
    // 1-b. 상대의 효과를 받지 않는 동안 — 상대 카드의 지속 효과 · 이미 받은 DP 효과 (15-15-5-1 · 15-15-5-2)
    // =====================================================================

    private const string DuringAttacks = "[All Turns] During attacks, your opponent's effects don't affect this Digimon.";
    private const string MinusAura = "[All Turns] All of your opponent's Digimon get -4000 DP.";

    [Fact]
    public void DuringAttacks_OpponentAuraIgnoredInBattle_BT27_103()
    {
        var me = Digimon(Uid("B59-ALPHA-"), 6, DuringAttacks, dp: 7000);
        AllSupported(me);
        var aura = Digimon(Uid("B59-AURA-"), 5, MinusAura, dp: 9000);
        AllSupported(aura);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        Put(state.DummyOpponent, aura).IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, d.GetCurrentDP());   // 어택 중이 아니면 받는다
        state.DummyOpponent.SecurityStack.Insert(0, Digimon("B59-SECD", 4, dp: 5000));
        engine.AttackSecurityOrDirect(d);
        Assert.Contains(d, state.Player.BattleArea);   // 어택 중에는 7000 > 5000
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, d.GetCurrentDP());   // 어택이 끝나면 다시 받는다
        Assert.False(d.OppShield59);
    }

    [Fact]
    public void DuringAttacks_EarlierOpponentDpMinusIgnoredInBattle()
    {
        var me = Digimon(Uid("B59-ALPHA-"), 6, DuringAttacks, dp: 7000);
        var minus = Digimon(Uid("B59-MINUS-"), 4, "[On Play] 1 of your opponent's Digimon gets -3000 DP until the end of their turn.");
        AllSupported(minus);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var m = Put(state.DummyOpponent, minus);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, m, minus);   // 상대의 효과 (이 턴 동안 · 다음 턴까지 모두 기간 DP)
        engine.RecalculateFieldBuffs();
        Assert.Equal(4000, d.GetCurrentDP());
        state.DummyOpponent.SecurityStack.Insert(0, Digimon("B59-SECD", 4, dp: 5000));
        engine.AttackSecurityOrDirect(d);
        Assert.Contains(d, state.Player.BattleArea);   // 어택 중에는 7000
        engine.RecalculateFieldBuffs();
        Assert.Equal(4000, d.GetCurrentDP());
    }

    [Fact]
    public void UnaffectedByOpponent_AlsoIgnoresOpponentAura()
    {
        var me = Digimon(Uid("B59-IMM-"), 6, "[All Turns] This Digimon isn't affected by your opponent's effects.", dp: 7000);
        AllSupported(me);
        var aura = Digimon(Uid("B59-AURA-"), 5, MinusAura, dp: 9000);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var other = Put(state.Player, Digimon("B59-PLAIN", 5, dp: 6000));
        Put(state.DummyOpponent, aura).IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.Equal(7000, d.GetCurrentDP());
        Assert.Equal(2000, other.GetCurrentDP());
    }

    // =====================================================================
    // 1-c. 얻은 특징은 필드의 디지몬의 진화 조건에도 (EX7-010)
    // =====================================================================

    [Fact]
    public void GainedTrait_CountsForTraitDigivolveCondition()
    {
        string tag = Guid.NewGuid().ToString("N")[..6];
        var gainer = Digimon(Uid("B59-GAIN-"), 4, $"[Your Turn] This Digimon gains the [B59Trait{tag}] trait.", color: CardColor.Red);
        AllSupported(gainer);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, gainer);
        engine.RecalculateFieldBuffs();
        Assert.Contains($"B59Trait{tag}", d.ExtraTraits58);
        Assert.True(TargetSpec.HasTrait(d, $"B59Trait{tag}"));
        var lv4 = Digimon("B59-L4", 4, color: CardColor.Red);
        Assert.False(TargetSpec.HasTrait(new CardInstance(lv4, 1), $"B59Trait{tag}"));
    }

    // =====================================================================
    // 2. ST17-13 [시큐리티] ≪퇴화 1≫ 디지몬 1마리 · 배틀 종료 시 이 카드로 진화
    // =====================================================================

    private const string MagnaSecurity = "[Security] ＜De-Digivolve 1＞ 1 Digimon. (Trash the top card. You can't trash past level 3 cards.) At the end of the battle, 1 of your Digimon may digivolve into this card without paying the digivolution cost.";

    [Fact]
    public void SecurityDeDigivolveAnyDigimon_ThenDigivolveAfterBattle_ST17_13()
    {
        var magna = Digimon(Uid("B59-MAGNA-"), 4, MagnaSecurity, color: CardColor.Black, dp: 7000, evoCost: 3);
        AllSupported(magna);
        Assert.Contains("디지몬 1마리", Ko(MagnaSecurity));
        var (state, engine) = NewMainPhase();
        var atk = Put(state.Player, Digimon("B59-ATK6", 6, color: CardColor.Red, dp: 12000), Digimon("B59-ATK5", 5, color: CardColor.Red), Digimon("B59-ATK4", 4, color: CardColor.Red));
        var oppBase = Put(state.DummyOpponent, Digimon("B59-OPP3", 3, color: CardColor.Black, dp: 3000));
        state.DummyOpponent.SecurityStack.Insert(0, magna);
        engine.AttackSecurityOrDirect(atk);
        Assert.Equal("B59-ATK5", atk.TopCard.CardId);   // 상대(AI)는 어택한 디지몬을 퇴화 (DP 12000 → 5000 — 7000에 진다)
        Assert.DoesNotContain(atk, state.Player.BattleArea);
        Assert.Same(magna, oppBase.TopCard);   // 배틀 종료 시 Lv.3 블랙 → 이 카드 (진화 조건대로)
        Assert.DoesNotContain(magna, state.DummyOpponent.Trash);
    }

    [Fact]
    public void SecurityDigivolveAfterBattle_KeepsRequirements_ST17_13()
    {
        var magna = Digimon(Uid("B59-MAGNA-"), 4, MagnaSecurity, color: CardColor.Black, dp: 7000);
        var (state, engine) = NewMainPhase();
        var atk = Put(state.Player, Digimon("B59-ATK6", 6, color: CardColor.Red, dp: 12000));
        var oppRed = Put(state.DummyOpponent, Digimon("B59-OPPRED3", 3, color: CardColor.Red, dp: 3000));   // 색이 맞지 않는다 (공식 Q&A ST17-13 Q1: 진화 조건은 무시할 수 없다)
        state.DummyOpponent.SecurityStack.Insert(0, magna);
        engine.AttackSecurityOrDirect(atk);
        Assert.Equal("B59-OPPRED3", oppRed.TopCard.CardId);
        Assert.Contains(magna, state.DummyOpponent.Trash);
    }

    // =====================================================================
    // 3. BT20-037 상대의 디지몬 · 테이머는 그 턴 종료 시까지 【등장 시】를 발휘할 수 없다
    // =====================================================================

    [Fact]
    public void OnPlayBlockedUntilEndOfTheirTurn_BT20_037()
    {
        const string text = "[When Digivolving] For each of this Digimon's level 6 digivolution cards, suspend 1 of your opponent's Digimon or Tamers and gain 1 memory. Then none of their Digimon or Tamers can activate [On Play] effects or unsuspend until the end of their turn.";
        var me = Digimon(Uid("B59-CHAOS-"), 7, text, dp: 15000);
        AllSupported(me);
        Assert.Contains("【등장 시】", Ko(text));
        var (state, engine) = NewMainPhase(memory: 3);
        var d = Put(state.Player, me, Digimon("B59-L6A", 6), Digimon("B59-L6B", 6));
        var foe = Put(state.DummyOpponent, Digimon("B59-F4", 4));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        Assert.True(foe.IsSuspended);
        Assert.Equal(5, state.MemoryGauge);   // Lv.6 진화원 2장 — 레스트시킬 대상이 1마리여도 메모리는 2번 ("A하고 B한다"는 할 수 있는 만큼)
        Assert.Equal(4, state.DummyOpponent.OnPlayBlockedUntil59);   // 상대의 다음 턴 (TurnSequence 3 → 4)
        // 상대가 등장시킨 디지몬의 【등장 시】는 발휘하지 않는다
        var onPlay = Digimon(Uid("B59-OP-"), 3, "[On Play] Trash the top 2 cards of your deck.");
        state.DummyOpponent.Hand.Add(onPlay);
        state.TurnSequence = 4;
        int deck = state.DummyOpponent.MainDeck.Count;
        Assert.True(engine.TryPlayByEffect(state.DummyOpponent, onPlay, PlayOrigin.Hand, () => state.DummyOpponent.Hand.Remove(onPlay)));
        Assert.Equal(deck, state.DummyOpponent.MainDeck.Count);
        state.TurnSequence = 5;   // 기간이 끝나면 다시
        var onPlay2 = Digimon(Uid("B59-OP-"), 3, "[On Play] Trash the top 2 cards of your deck.");
        Assert.True(engine.TryPlayByEffect(state.DummyOpponent, onPlay2, PlayOrigin.Hand, null));
        Assert.Equal(deck - 2, state.DummyOpponent.MainDeck.Count);
    }

    // =====================================================================
    // 4. BT5-018 파기한 카드의 DP만큼 DP +
    // =====================================================================

    [Fact]
    public void AddTrashedCardDp_BT5_018()
    {
        const string text = "[When Attacking] You may trash 1 red Digimon card in your hand to add the trashed card's DP to this Digimon for the turn.";
        var me = Digimon(Uid("B59-DORB-"), 6, text, color: CardColor.Red, dp: 11000);
        AllSupported(me);
        Assert.Contains("DP만큼", Ko(text));
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var small = Digimon("B59-R3", 3, color: CardColor.Red, dp: 3000);
        var big = Digimon("B59-R6", 6, color: CardColor.Red, dp: 12000);
        state.Player.Hand.Add(small); state.Player.Hand.Add(big); state.Player.Hand.Add(Digimon("B59-B7", 6, color: CardColor.Blue, dp: 14000));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, me);
        Assert.Contains(big, state.Player.Trash);
        Assert.Equal(23000, d.GetCurrentDP());
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, me);   // 공식 Q&A BT5-018 Q1: 같은 턴에 또 하면 더해진다
        Assert.Equal(26000, d.GetCurrentDP());
    }

    // =====================================================================
    // 5. BT21-086 자신의 [Marcus Damon] 1명을 레스트
    // =====================================================================

    [Fact]
    public void SuspendNamedTamer_TriggersWhenThisTamerSuspends_BT21_086()
    {
        string name = "B59 Marcus " + Guid.NewGuid().ToString("N")[..4];
        string text = $"[On Play] 1 of your [{name}]s may suspend.\r\n[All Turns] [Once Per Turn] When this Tamer suspends, 1 of your Digimon gets +3000 DP for the turn.";
        var tamer = Card(Uid("B59-MARCUS-"), text, CardType.Tamer, 0, 4, CardColor.Yellow, name: name);
        AllSupported(tamer);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, Digimon("B59-AG", 5, dp: 6000));
        state.Player.Tamers.Add(tamer);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, tamer, includeInherited: false);
        engine.RecalculateFieldBuffs();
        Assert.Contains(tamer, state.Player.SuspendedTamers);
        Assert.Equal(9000, d.GetCurrentDP());
    }

    // =====================================================================
    // 6. [트래시]에서 등장 (EX4-011 · LM-006)
    // =====================================================================

    [Fact]
    public void TrashEndOfTurn_DeleteThenPlayThisCard_EX4_011()
    {
        string gal = "B59 Gallantmon " + Guid.NewGuid().ToString("N")[..4];
        string text = $"[Trash] [End of Your Turn] By Deleting 1 of your Digimon with a digivolution card and [{gal}] in its name, you may play this card without paying its cost.";
        var chaos = Digimon(Uid("B59-CGAL-"), 6, text, color: CardColor.Red, playCost: 12);
        AllSupported(chaos);
        var (state, engine) = NewMainPhase();
        var g = Put(state.Player, Digimon("B59-GAL4", 4, name: gal + " X"), Digimon("B59-SRC", 3));
        state.Player.Trash.Add(chaos);
        engine.RunTrashEffects(EffectTrigger.EndOfYourTurn, state.Player);
        Assert.DoesNotContain(g, state.Player.BattleArea);
        Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, chaos));
        Assert.Equal(8, state.MemoryGauge);
    }

    [Fact]
    public void TrashMain_ReturnTamer_PlayReducedByTamerCost_LM_006()
    {
        const string text = "[Trash] [Main] By returning 1 of your Tamers to the bottom of the deck, play this card with the play cost reduced by the play cost of the returned Tamer.";
        var cthy = Digimon(Uid("B59-CTHY-"), 6, text, playCost: 11);
        AllSupported(cthy);
        Assert.Contains("되돌린 테이머의 등장 코스트만큼", Ko(text));
        var (state, engine) = NewMainPhase(memory: 8);
        var cheap = Card("B59-T2", "", CardType.Tamer, 0, 2);
        var dear = Card("B59-T5", "", CardType.Tamer, 0, 5);
        state.Player.Tamers.Add(cheap); state.Player.Tamers.Add(dear);
        state.Player.Trash.Add(cthy);
        Assert.True(engine.ActivateTrashEffect(cthy).CanExecute, Logs(state));
        Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, cthy));
        Assert.Same(dear, state.Player.MainDeck[^1]);   // AI: 등장 코스트가 큰 테이머
        Assert.Equal(2, state.MemoryGauge);   // 11 - 5 = 6
    }

    // =====================================================================
    // 7. 패에서 파기된 그 카드로 진화 / 그중 1장을 등장 (BT14-006 · BT24-007)
    // =====================================================================

    [Fact]
    public void DigivolveIntoCardTrashedFromHand_BT14_006()
    {
        const string inh = "[Your Turn] When a Digimon card with the [Dark Animal] or [SoC] trait is trashed from your hand, this Digimon may digivolve into that card.";
        var egg = Card(Uid("B59-BOW-"), "", CardType.Digimon, 2, 0, CardColor.Purple, inherited: inh);
        AllSupported(egg);
        var (state, engine) = NewMainPhase(memory: 5);
        var d = Put(state.Player, Digimon("B59-P3", 3, color: CardColor.Purple), egg);
        var dober = Digimon("B59-DOBER", 4, color: CardColor.Purple, evoCost: 2, traits: "Dark Animal");
        state.Player.Hand.Add(dober);
        engine.DiscardFromHand(state.Player, 1);
        Assert.Same(dober, d.TopCard);
        Assert.Equal(3, state.MemoryGauge);   // 진화 코스트를 낸다 (종합 룰 8-1-3-2)
    }

    [Fact]
    public void PlayOneOfCardsTrashedFromHand_BT24_007()
    {
        const string inh = "[Your Turn] [Once Per Turn] When level 4 or higher Digimon cards with the [Demon] or [Titan] trait are trashed from your hand, you may play 1 of them with the play cost reduced by 2.";
        var egg = Card(Uid("B59-TSUNO-"), "", CardType.Digimon, 2, 0, CardColor.Purple, inherited: inh);
        AllSupported(egg);
        var (state, engine) = NewMainPhase(memory: 5);
        Put(state.Player, Digimon("B59-P3", 3, color: CardColor.Purple), egg);
        var demon = Digimon("B59-DEMON", 5, playCost: 6, traits: "Demon");
        var low = Digimon("B59-DEMON3", 3, playCost: 2, traits: "Demon");
        state.Player.Hand.Add(demon); state.Player.Hand.Add(low);
        engine.DiscardFromHand(state.Player, 2);
        Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, demon));
        Assert.Contains(low, state.Player.Trash);   // Lv.3은 조건 밖
        Assert.Equal(1, state.MemoryGauge);   // 6 - 2 = 4
    }

    // =====================================================================
    // 8. 비용: 상대의 패를 보지 않고 N장이 될 때까지 덱 아래로 (BT13-033 · BT27-035)
    // =====================================================================

    [Fact]
    public void ReturnOpponentHandRandomUntilN_Unsuspend_BT13_033()
    {
        const string text = "[When Attacking] If your opponent has 9 or more cards in their hand, by returning random cards from their hand to the bottom of their owner's deck without looking at them until they have only 8 cards left, unsuspend this Digimon.";
        var me = Digimon(Uid("B59-MIRAGE-"), 7, text, dp: 15000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        d.IsSuspended = true;
        for (int i = 0; i < 11; i++) state.DummyOpponent.Hand.Add(Digimon("B59-H" + i, 3));
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, d, me);
        Assert.Equal(8, state.DummyOpponent.Hand.Count);
        Assert.Equal(13, state.DummyOpponent.MainDeck.Count);
        Assert.False(d.IsSuspended);
    }

    [Fact]
    public void ReturnOpponentHandTo7_TheirTopSecurityToDeckBottom_BT27_035()
    {
        const string text = "[When Digivolving] [When Attacking] If your opponent's hand has 8 or more cards, by returning cards without looking to the bottom of the deck so that it has 7 left, return their top security card to the bottom of the deck.";
        var me = Digimon(Uid("B59-MIRAGE-"), 7, text, dp: 13000);
        AllSupported(me);
        Assert.Contains("시큐리티 맨 위", Ko(text));
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var top = state.DummyOpponent.SecurityStack[0];
        for (int i = 0; i < 7; i++) state.DummyOpponent.Hand.Add(Digimon("B59-H" + i, 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);   // 7장이면 조건 밖
        state.DummyOpponent.Hand.Add(Digimon("B59-H7", 3)); state.DummyOpponent.Hand.Add(Digimon("B59-H8", 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        Assert.Equal(7, state.DummyOpponent.Hand.Count);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.Same(top, state.DummyOpponent.MainDeck[^1]);
    }

    // =====================================================================
    // 9. 그 밖 (BT16-031 · BT16-072 · BT20-059 · EX11-067)
    // =====================================================================

    [Fact]
    public void ReturnTwoColorRedOrPurple_BT16_031()
    {
        const string text = "[On Play] [When Digivolving] By trashing 1 card in your hand, return 1 level 6 or lower 2-color Digimon card that's red or purple from your trash to the hand.";
        var me = Digimon(Uid("B59-GATO-"), 4, text, color: CardColor.Yellow);
        AllSupported(me);
        Assert.Contains("2색", Ko(text));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new PickCards { ConfirmAnswer = true };
        var d = Put(state.Player, me);
        state.Player.Hand.Add(Digimon("B59-JUNK", 3));
        var mono = Digimon("B59-MONO", 5, color: CardColor.Red);
        var two = Card("B59-TWO", "", CardType.Digimon, 5, 5, CardColor.Blue);
        two.SecondaryColor = CardColor.Purple;
        state.Player.Trash.Add(mono); state.Player.Trash.Add(two);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Contains(two, state.Player.Hand);
        Assert.Contains(mono, state.Player.Trash);
    }

    [Fact]
    public void PlayTamerWithoutSameName_BT16_072()
    {
        const string text = "[On Deletion] From your trash and without paying the cost, you may play 1 Tamer card with [B59Myo] in its text and without the same name as any of your Tamers.";
        var me = Digimon(Uid("B59-ARUK-"), 5, text);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var same = Card("B59-T-SAME", "[Your Turn] When [B59Myo] attacks, gain 1 memory.", CardType.Tamer, 0, 3, name: "B59 Same Tamer");
        var other = Card("B59-T-OTHER", "[Your Turn] When [B59Myo] attacks, gain 1 memory.", CardType.Tamer, 0, 3, name: "B59 Other Tamer");
        state.Player.Tamers.Add(same);
        state.Player.Trash.Add(Card("B59-T-SAME2", "[Your Turn] When [B59Myo] attacks, gain 1 memory.", CardType.Tamer, 0, 5, name: "B59 Same Tamer"));
        state.Player.Trash.Add(other);
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, d, me);
        Assert.True(state.Player.Tamers.Contains(other), Logs(state));
        Assert.Equal(2, state.Player.Tamers.Count);
    }

    [Theory]
    [InlineData("[Opponent's Turn] While this Digimon is [Jesmon GX] all of your Digimon gain ＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.) and ＜Blocker＞ (This Digimon can block in the blocker timing.).", true)]
    [InlineData("[On Play] 1 of your Digimon with [Lucemon] in its text on the field may digivolve into a Digimon card with [Lucemon] in its name in the hand or trash without paying the cost.", false)]
    public void RewrittenSentences_Supported(string clause, bool inherited)
    {
        var c = inherited ? Digimon(Uid("B59-RW-"), 5, inherited: clause) : Card(Uid("B59-RW-"), clause, CardType.Tamer, 0, 3);
        AllSupported(c);
        Assert.NotEqual("<null>", Ko(clause));
    }

    // =====================================================================
    // 이전 문장 · 한글은 그대로
    // =====================================================================

    [Theory]
    [InlineData("[When Digivolving] None of your opponent's Digimon or Tamers can unsuspend until the end of their turn.")]
    [InlineData("[Main] By returning 1 of your Tamers to the bottom of the deck, gain 1 memory.")]
    [InlineData("[Trash] [Main] Play this card with the play cost reduced by 4.")]
    public void EarlierSentences_Unchanged(string clause)
    {
        var c = Digimon(Uid("B59-OLD-"), 5, clause);
        AllSupported(c);
        Assert.DoesNotContain("⟦", Ko(clause));
    }
}
