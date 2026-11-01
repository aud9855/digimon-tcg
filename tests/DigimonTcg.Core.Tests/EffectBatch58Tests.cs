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
/// v0.58.0 미지원 문장 32차 묶음: ≪디지버스트 N까지≫ · 소멸한 디지몬의 Lv. 1마다 · 되돌린 1장마다 상한 +1 · 패의 카드를 공개하고 덱 위에 ·
/// 어택 대상이 바뀌지 않는다 · 어택 중에는 상대의 효과를 받지 않는다 · 특징을 얻는다 · AI가 고르는 효과의 추천 (할 수 있는 선택지)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch58Tests
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
            state.Player.MainDeck.Add(Digimon("B58-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B58-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B58-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B58-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    // =====================================================================
    // 1. ≪디지버스트 N까지≫ (BT7-040)
    // =====================================================================

    private const string BurstUpTo = "[Main] [Once Per Turn] ＜Digi-Burst up to 4＞ (You may trash up to 4 of this Digimon's digivolution cards to activate the effect below.)\r\n・1 of your opponent's Digimon gets -3000 DP for the turn for each card trashed by this card's effect.";

    [Fact]
    public void DigiBurstUpTo_TrashesJustEnough_BT7_040()
    {
        var me = Digimon(Uid("B58-BURST-"), 6, BurstUpTo, dp: 11000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me, Enumerable.Range(0, 4).Select(i => Digimon("B58-S" + i, 3 + i % 2)).ToArray());
        var foe = Put(state.DummyOpponent, Digimon("B58-FOE", 5, dp: 7000));
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.Single(d.EvolutionSources);   // 7000 DP → 3장 (-9000)
        Assert.True(!state.DummyOpponent.BattleArea.Contains(foe) || foe.GetCurrentDP() <= 0, Logs(state));
        Assert.Equal(3, state.Player.Trash.Count);
    }

    [Fact]
    public void DigiBurstUpTo_CannotDelete_DoesNotActivate_BT7_040()
    {
        var me = Digimon(Uid("B58-BURST-"), 6, BurstUpTo, dp: 11000);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me, Digimon("B58-S0", 3), Digimon("B58-S1", 4));
        var foe = Put(state.DummyOpponent, Digimon("B58-FOE", 6, dp: 12000));
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.Equal(2, d.EvolutionSources.Count);   // 2장으로는 12000을 지울 수 없다 — AI는 발휘하지 않는다
        Assert.Equal(12000, foe.GetCurrentDP());
    }

    // =====================================================================
    // 2. 셈: 소멸한 디지몬의 Lv. (EX6-058) · 되돌린 매수 (EX13-053)
    // =====================================================================

    [Fact]
    public void PerLevelOfDeletedDigimon_MillsDeck_EX6_058()
    {
        var me = Digimon(Uid("B58-BAR-"), 6, "[On Play] [When Digivolving] Delete 1 of your opponent's Digimon with the lowest DP. For each level of the Digimon deleted by this effect, trash the top card of your deck.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        Put(state.DummyOpponent, Digimon("B58-LV5", 5, dp: 3000));
        Put(state.DummyOpponent, Digimon("B58-LV6", 6, dp: 9000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Single(state.DummyOpponent.BattleArea);
        Assert.Equal(5, state.Player.MainDeck.Count);   // Lv.5 → 5장
        Assert.Equal(5, state.Player.Trash.Count);
    }

    [Fact]
    public void PlayCostMaxPerReturned_EX13_053()
    {
        var me = Digimon(Uid("B58-MAME-"), 5, "[On Play] [On Deletion] You may return up to 3 Digimon cards with [Mamemon] in their texts from your trash to the top of the deck. Then, delete 1 of your opponent's Digimon with a play cost of 3 or less. For each one this effect returned, add 1 to this effect's play cost maximum.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        state.Player.Trash.Add(Digimon("B58-M1", 3, name: "B58 Mamemon"));
        state.Player.Trash.Add(Digimon("B58-M2", 4, "[On Play] Play 1 [Mamemon] from your hand."));
        var foe = Put(state.DummyOpponent, Digimon("B58-PC5", 5, playCost: 5, dp: 6000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Empty(state.Player.Trash);
        Assert.Equal(12, state.Player.MainDeck.Count);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);   // 3 + 2 = 5
    }

    // =====================================================================
    // 3. 패의 카드를 공개하고 덱 위에 (P-077 · BT7-043)
    // =====================================================================

    [Theory]
    [InlineData("[On Play] You may reveal 1 green Digimon card from your hand. If you do, place it on top of your deck.")]
    [InlineData("[On Play] You may reveal 1 green card from your hand and place it on top of your deck.")]
    public void RevealFromHand_PlaceOnTopOfDeck_BT7_043_P_077(string text)
    {
        var me = Digimon(Uid("B58-REV-"), 3, text, color: CardColor.Green);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var red = Digimon("B58-RED", 3, color: CardColor.Red);
        var green = Digimon("B58-GREEN", 4, color: CardColor.Green);
        state.Player.Hand.Add(red);
        state.Player.Hand.Add(green);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        Assert.Same(green, state.Player.MainDeck[0]);   // 초록 카드만 고를 수 있다
        Assert.Equal(11, state.Player.MainDeck.Count);
        Assert.Equal(new[] { red }, state.Player.Hand);
    }

    // =====================================================================
    // 그 밖의 문장 (BT9-009 · P-142 · BT6-042 · ST17-13 · BT17-047)
    // =====================================================================

    [Fact]
    public void DeleteCapPlus1000_Inherited_BT9_009()
    {
        var src = Digimon(Uid("B58-CAP-"), 3, inherited: "[Your Turn] Add 1000 to the maximum DP you can choose with DP-based delete effects.");
        AllSupported(src);
        var me = Digimon(Uid("B58-DEL-"), 4, "[When Digivolving] Delete 1 of your opponent's Digimon with 3000 DP or less.");
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me, src);
        var foe = Put(state.DummyOpponent, Digimon("B58-4000", 4, dp: 4000));
        engine.RecalculateFieldBuffs();
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void OnDeletion_OtherThanInBattle_P_142()
    {
        var src = Digimon(Uid("B58-P142-"), 3, inherited: "[On Deletion] When this Digimon is deleted other than in battle, your opponent trashes 1 card in their hand.");
        AllSupported(src);
        Assert.Contains("배틀", Ko("[On Deletion] When this Digimon is deleted other than in battle, your opponent trashes 1 card in their hand."));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlayNamedOrUpToTwo_FromTrash_BT6_042(bool hasNamed)
    {
        var me = Digimon(Uid("B58-LIL-"), 5, "[On Deletion] You may play 1 [B58 Rosemon] or up to 2 yellow level 3 Digimon Cards from your trash without paying their memory costs.", color: CardColor.Yellow);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var rose = Digimon("B58-ROSE", 6, name: "B58 Rosemon", color: CardColor.Yellow, playCost: 12);
        if (hasNamed) state.Player.Trash.Add(rose);
        state.Player.Trash.Add(Digimon("B58-Y3A", 3, color: CardColor.Yellow));
        state.Player.Trash.Add(Digimon("B58-Y3B", 3, color: CardColor.Yellow));
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, d, me);
        int played = state.Player.BattleArea.Count - 1;
        if (hasNamed) { Assert.Equal(1, played); Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, rose)); }
        else Assert.Equal(2, played);
    }

    [Fact]
    public void TrashTopSourcePerColor_ST17_13()
    {
        var me = Digimon(Uid("B58-ST17-"), 6, "[When Digivolving] Trash the top digivolution card of 1 of your opponent's Digimon for each of that Digimon's colors. Then, return 1 of your opponent's Digimon with no digivolution cards to the hand.");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var two = Card("B58-2C", "", CardType.Digimon, 5, 5, CardColor.Red, 7000);
        two.SecondaryColor = CardColor.Blue;
        var foe = Put(state.DummyOpponent, two, Digimon("B58-S1", 4), Digimon("B58-S2", 3));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);   // 2색 → 2장 → 진화원 없음 → 패로
        Assert.Contains(two, state.DummyOpponent.Hand);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SecurityPlay_IfNoDigimon_BT17_047(bool haveDigimon, bool played)
    {
        var me = Digimon(Uid("B58-SEC17-"), 4, "[Security] At the end of the battle, if you don't have Digimon, play this card without paying the cost.", dp: 1000);
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var attacker = Put(state.Player, Digimon("B58-ATK", 5, dp: 9000));
        if (haveDigimon) Put(state.DummyOpponent, Digimon("B58-MINE", 3, dp: 1000));
        state.DummyOpponent.SecurityStack.Insert(0, me);
        engine.AttackSecurityOrDirect(attacker);
        Assert.Equal(played, state.DummyOpponent.BattleArea.Any(x => ReferenceEquals(x.TopCard, me)));
    }

    // =====================================================================
    // 4. 어택 대상이 바뀌지 않는다 (EX7-022 · BT26-021) · 어택 중 효과를 받지 않는다 (BT27-103) · 특징 (EX7-010)
    // =====================================================================

    [Fact]
    public void AllNSpDigimon_AttackTargetLocked_OnYourTurn_EX7_022()
    {
        var me = Digimon(Uid("B58-NSP-"), 5, "[Your Turn] All of your Digimon with the [NSp] trait trait can't have their attack targets switched.", traits: "NSp");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var ally = Put(state.Player, Digimon("B58-NSP2", 4, traits: "NSp"));
        var other = Put(state.Player, Digimon("B58-OTHER", 4));
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(d, Keyword.AttackTargetLocked));
        Assert.True(CardScripts.HasKeyword(ally, Keyword.AttackTargetLocked));
        Assert.False(CardScripts.HasKeyword(other, Keyword.AttackTargetLocked));
        d.IsOwnersTurn = false; ally.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(ally, Keyword.AttackTargetLocked));
    }

    [Fact]
    public void OneTsDigimon_AttackTargetLockedForTheTurn_BT26_021()
    {
        var me = Digimon(Uid("B58-TS-"), 4, "[On Play] [When Digivolving] 1 of your [TS] trait Digimon's attack target can't change for the turn.", traits: "TS");
        AllSupported(me);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        Assert.True(CardScripts.HasKeyword(d, Keyword.AttackTargetLocked), Logs(state));
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.False(CardScripts.HasKeyword(d, Keyword.AttackTargetLocked));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuringAttacks_OpponentEffectsDontAffect_BT27_103(bool hasEffect)
    {
        var me = Digimon(Uid("B58-ALP-"), 7, hasEffect ? "[All Turns] During attacks, your opponent's effects don't affect this Digimon." : "", dp: 16000);
        if (hasEffect) AllSupported(me);
        var trap = Card(Uid("B58-TRAP-"), "[Main] Delete 1 of your opponent's Digimon.\r\n[Security] Activate this card's [Main] effect.", CardType.Option, 0, 0);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        state.DummyOpponent.SecurityStack.Insert(0, trap);
        engine.AttackSecurityOrDirect(d);
        Assert.Equal(hasEffect, state.Player.BattleArea.Contains(d));
    }

    [Fact]
    public void GainsTrait_OnYourTurn_TargetedByTraitEffects_EX7_010()
    {
        var me = Digimon(Uid("B58-MUSK-"), 4, "[Your Turn] This Digimon gains the [Three Musketeers] trait.", dp: 5000);
        AllSupported(me);
        var opt = Card(Uid("B58-BUFF-"), "[Main] 1 of your Digimon with the [Three Musketeers] trait gets +3000 DP for the turn.", CardType.Option, 0, 0);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me);
        var plain = Put(state.Player, Digimon("B58-PLAIN", 5, dp: 6000));
        engine.RecalculateFieldBuffs();
        Assert.Contains("Three Musketeers", d.ExtraTraits58);
        state.Player.Hand.Add(opt);
        engine.PlayCard(opt);
        engine.RecalculateFieldBuffs();
        Assert.Equal(8000, d.GetCurrentDP());
        Assert.Equal(6000, plain.GetCurrentDP());
        d.IsOwnersTurn = false;
        engine.RecalculateFieldBuffs();
        Assert.Empty(d.ExtraTraits58);
    }

    // =====================================================================
    // 5. AI가 고르는 효과: 할 수 있는 선택지를 추천
    // =====================================================================

    [Fact]
    public void ChooseOne_AiPicksFeasibleOption_WhenNoTarget()
    {
        var opt = Card(Uid("B58-CH-"), "[Main] Activate 1 of the effects below:\r\n・Delete 1 of your opponent's Digimon.\r\n・Trash your opponent's top security card.", CardType.Option, 0, 0);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B58-MINE", 4));
        state.Player.Hand.Add(opt);
        engine.PlayCard(opt);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);   // 상대 디지몬이 없으니 시큐리티 쪽
    }

    [Fact]
    public void ChooseEachTime_SecondPickFeasible_EX12_037()
    {
        var me = Digimon(Uid("B58-OMN-"), 7, "[When Digivolving] [When Attacking] [Once Per Turn] Delete 1 of your opponent's Digimon. Then, for every 5 of this Digimon's digivolution cards, activate 1 of the effects below:\r\n・1 of your opponent's Digimon gets -13000 DP until their turn ends.\r\n・Trash your opponent's top security card. Then, ＜Recovery +1＞ (Place the top card of your deck as your top security card.).");
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, me, Enumerable.Range(0, 10).Select(i => Digimon("B58-S" + i, 3)).ToArray());
        Put(state.DummyOpponent, Digimon("B58-F0", 6, dp: 10000));
        Put(state.DummyOpponent, Digimon("B58-F1", 6, dp: 10000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, me);
        engine.RecalculateFieldBuffs();
        // 1마리 소멸 → 남은 1마리에 -13000 → 다음은 대상이 없으니 시큐리티
        Assert.DoesNotContain(state.DummyOpponent.BattleArea, x => x.GetCurrentDP() > 0);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.Equal(6, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void Korean_SameWithAndWithoutMemo_Batch58()
    {
        var clauses = new[]
        {
            "[Main] [Once Per Turn] ＜Digi-Burst up to 4＞ (You may trash up to 4 of this Digimon's digivolution cards to activate the effect below.)",
            "・1 of your opponent's Digimon gets -3000 DP for the turn for each card trashed by this card's effect.",
            "[On Play] [When Digivolving] Delete 1 of your opponent's Digimon with the lowest DP. For each level of the Digimon deleted by this effect, trash the top card of your deck.",
            "[When Attacking] You may reveal 1 purple card from your hand and place it on top of your deck.",
            "[On Play] You may reveal 1 green Digimon card from your hand. If you do, place it on top of your deck.",
            "[Your Turn] All of your Digimon with the [NSp] trait trait can't have their attack targets switched.",
            "[On Play] [When Digivolving] 1 of your [TS] trait Digimon's attack target can't change for the turn.",
            "[All Turns] During attacks, your opponent's effects don't affect this Digimon.",
            "[Your Turn] This Digimon gains the [Three Musketeers] trait.",
        };
        var cached = clauses.Select(Ko).ToList();
        Assert.DoesNotContain("<null>", cached);
        KoreanEffectRenderer.MemoEnabled52 = false;
        try { Assert.Equal(clauses.Select(Ko).ToList(), cached); }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }
}
