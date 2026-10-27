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
/// v0.40.0 미지원 문장 14차 묶음: "When ~" 사건 (≪연계≫로 레스트 · 진화하려 할 때 · 맨 위 카드가 진화원으로 · 진화원이 덱으로 · 테이머 아래에 놓였을 때 · 메인 페이즈의 액티브 · 같은 Lv. 진화) ·
/// 효과를 가진 디지몬과 같은 명칭 (P-045 · BT5-063 · EX1-051) · ≪분리≫ · ≪디코드≫ · ≪디코이≫ 조건 · 키워드 부여 · 여러 Lv. 1마리씩 소멸 · 공개 (합계 상한 · 상대 시큐리티) · 시큐리티에서 진화
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch40Tests
{
    private sealed class Scripted : IChoiceProvider
    {
        public bool ConfirmAnswer = true;
        public int Confirms;
        public Func<IReadOnlyList<CardData>, int, int, IReadOnlyList<int>>? CardPick;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Confirms++; return ConfirmAnswer; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) =>
            recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
        public IReadOnlyList<int> ChooseCards(GameState state, string question, CardChoiceZone zone, IReadOnlyList<CardData> options, int min, int max, IReadOnlyList<int> recommended) =>
            CardPick != null ? CardPick(options, min, max) : recommended;
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
            state.Player.MainDeck.Add(Digimon("B40-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B40-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B40-SEC" + i, 3, dp: 1000, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B40-OSEC" + i, 3, dp: 1000, color: CardColor.White));
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
    // 0. 조건 확인 (BT17-011 "a blue or green Digimon or Tamer")
    // =====================================================================

    [Fact]
    public void ColorDigimonOrTamerCondition_TamerMustHaveColor_BT17_011()
    {
        const string text = "[When Digivolving] If [BurningGreymon] is in this Digimon's digivolution cards or you have a blue or green Digimon or Tamer, ＜Draw 1＞.";
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT17011", 5, text, color: CardColor.Red);
        var me = Put(state.Player, card);
        state.Player.Tamers.Add(Tamer("B40-WTAMER", color: CardColor.White));
        int hand = state.Player.Hand.Count;
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Equal(hand, state.Player.Hand.Count);   // 화이트 테이머는 조건에 맞지 않는다
        state.Player.Tamers.Add(Tamer("B40-GTAMER", color: CardColor.Green));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Equal(hand + 1, state.Player.Hand.Count);
    }

    // =====================================================================
    // 1. "When ~" 사건
    // =====================================================================

    [Fact]
    public void AllianceSuspend_TriggersInheritedDigivolve_EX4_032()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B40-EX4032", 3, inherited: "[Your Turn] When one of your Digimon is suspended by an ＜Alliance＞ effect, you may digivolve this Digimon into a 2-color green card in your hand for its digivolution cost. When this Digimon would digivolve with this effect, reduce the digivolution cost by 2.", color: CardColor.Green);
        AssertFull(src);
        var top = Digimon("B40-ALLY4", 4, "＜Alliance＞ (When this Digimon attacks, by suspending 1 of your other Digimon, add the suspended Digimon's DP to this Digimon and it gains ＜Security A. +1＞ for the attack.)", dp: 4000, color: CardColor.Green);
        var leader = Put(state.Player, top, sources: src);
        var helper = Put(state.Player, Digimon("B40-HELP", 3, dp: 3000), turnPlayed: 2);
        var evo = Card("B40-EVO5", type: CardType.Digimon, level: 5, color: CardColor.Green, dp: 7000);
        evo.SecondaryColor = CardColor.Yellow;
        evo.EvoCost = 3; evo.EvoRequirementLevel = 4; evo.EvoRequirementColor = CardColor.Green;
        state.Player.Hand.Add(evo);
        Diag40.Reset();
        engine.AttackSecurityOrDirect(leader);
        Assert.True(helper.IsSuspended);
        Assert.True(Diag40.AllianceSuspend > 0);
        Assert.Same(evo, leader.TopCard);   // ≪연계≫로 레스트 → 그 디지몬(진화원 효과를 가진)이 진화
        Assert.Equal(8 - 1, state.MemoryGauge);   // 진화 코스트 3 - 2
    }

    [Fact]
    public void OpponentUnsuspendInMainPhase_NotInUnsuspendPhase_BT9_089()
    {
        var (state, engine, choices) = NewMainPhase();
        var tamer = Tamer("B40-BT9089", "[All Turns] When an opponent's Digimon becomes unsuspended during a main phase, you may suspend this Tamer to gain 1 memory.", CardColor.White);
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var enemy = Put(state.DummyOpponent, Digimon("B40-OPPU", 3), suspended: true);
        engine.Unsuspend(enemy);   // 메인 페이즈에 효과로 액티브
        Assert.Equal(9, state.MemoryGauge);
        Assert.Contains(tamer, state.Player.SuspendedTamers);
        Assert.Equal("【서로의 턴】 상대의 디지몬이 메인 페이즈에서 액티브 상태가 되었을 때, 이 테이머를 레스트시키는 것으로, 메모리를 +1 한다.", KoreanEffectRenderer.RenderClause(tamer.EffectText));
    }

    [Fact]
    public void SameLevelDigivolve_BT9_092()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B40-BT9092", "[Your Turn] When one of your Digimon digivolves into a same-level Digimon with the [X Antibody] trait, you may suspend this Tamer to gain 1 memory and ＜Draw 1＞ (Draw 1 card from your deck.)", CardColor.White);
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var d = Put(state.Player, Digimon("B40-L5", 5));
        var x = Card("B40-L5X", type: CardType.Digimon, level: 5, dp: 6000, traits: "X Antibody");
        x.EvoRequirementLevel = 5; x.EvoCost = 1;
        state.Player.Hand.Add(x);
        engine.EvolveDigimon(d, x);
        Assert.Equal(8 - 1 + 1, state.MemoryGauge);   // 진화 코스트 1, 효과 +1
    }

    [Fact]
    public void PlayOrPlaceOption_EX3_034()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-EX3034", 6, "[Your Turn] [Once Per Turn] When you play a Digimon with the [Four Great Dragons] trait or place [Trial of the Four Great Dragons] in your battle area, 1 of your opponent's Digimon gets -3000 DP for the turn.");
        AssertFull(card);
        Put(state.Player, card);
        var victim = Put(state.DummyOpponent, Digimon("B40-V", 4, dp: 6000));
        var dragon = Digimon("B40-FGD", 3, traits: "Four Great Dragons");
        state.Player.Hand.Add(dragon);
        engine.PlayCard(dragon);
        Assert.Equal(3000, victim.GetCurrentDP());
        Assert.Equal("【자신의 턴】 [턴에 1회] 특징으로 「4대용」을 가진 자신의 디지몬이 등장했을 때 또는 자신의 「4대용의 시련」이 배틀 에어리어에 놓였을 때, 이 턴 동안, 상대의 디지몬 1마리의 DP를 -3000 한다.",
            KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void WouldDigivolve_RecoveryBeforeDigivolving_BT8_024()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT8024", 4, "[Your Turn] When this Digimon would digivolve, if you have 3 or fewer security cards, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)");
        AssertFull(card);
        while (state.Player.SecurityStack.Count > 3) state.Player.SecurityStack.RemoveAt(0);
        var d = Put(state.Player, card);
        d.IsOwnersTurn = true;
        var evo = Digimon("B40-EVO5B", 5);
        state.Player.Hand.Add(evo);
        Diag40.Reset();
        engine.EvolveDigimon(d, evo);
        Assert.Equal(4, state.Player.SecurityStack.Count);
        Assert.True(Diag40.WouldDigivolve > 0);
        Assert.Same(evo, d.TopCard);
    }

    [Fact]
    public void SourcesToDeck_VemmonUnsuspendsAndBlocker_BT11_065()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B40-BT11065", 3, inherited: "[All Turns] [Once Per Turn] When [Vemmon] is placed from this Digimon's digivolution cards at the bottom of its owner's deck, unsuspend this Digimon, and it gains ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn.");
        AssertFull(src);
        var vemmon = Digimon("B40-VEM", 2, name: "Vemmon");
        var top = Digimon("B40-GALA", 6, "[Main] By placing 1 [Vemmon] from this Digimon's digivolution cards at the bottom of its owner's deck, ＜Draw 1＞.");
        AssertFull(top);
        var d = Put(state.Player, top, suspended: true, sources: new[] { src, vemmon });
        Diag40.Reset();
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, d, top);
        Assert.Same(vemmon, state.Player.MainDeck.Last());
        Assert.False(d.IsSuspended);
        Assert.True(d.HasBlocker);
        Assert.True(Diag40.SourcesToDeck > 0);
    }

    [Fact]
    public void TopPlacedUnder_InheritedDraw_BT22_006()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B40-BT22006", 3, inherited: "[Your Turn] [Once Per Turn] When effects place this Digimon's top stacked card as its bottom digivolution card, ＜Draw 1＞ (Draw 1 card from your deck.) and trash 1 card in your hand.", traits: "CS");
        AssertFull(src);
        var mid = Digimon("B40-MID", 4, traits: "CS");
        var top = Digimon("B40-CSTOP", 5, "[Main] [Once Per Turn] By placing this [CS] trait Digimon's top stacked card as its bottom digivolution card, ＜Draw 1＞ (Draw 1 card from your deck.)", traits: "CS");
        var d = Put(state.Player, top, sources: new[] { mid, src });
        d.IsOwnersTurn = true;
        state.Player.Hand.Add(Digimon("B40-HAND", 3));
        int deck = state.Player.MainDeck.Count;
        Diag40.Reset();
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, d, top);
        Assert.Same(mid, d.TopCard);
        Assert.Same(top, d.EvolutionSources.Last());
        Assert.True(Diag40.TopPlacedUnder > 0);
        Assert.Equal(deck - 2, state.Player.MainDeck.Count);   // 비용 효과 1장 + 진화원 효과 1장
    }

    [Fact]
    public void CardPlacedUnderTamer_PurpleOnly_BT10_093()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B40-BT10093", "[All Turns] [Once Per Turn] When a purple card is placed under this Tamer, ＜Draw 1＞ (Draw 1 card from your deck.) and memory +1.", CardColor.Purple);
        AssertFull(tamer);
        var placer = Tamer("B40-PLACER", "[Main] By placing 1 purple card from your hand under this Tamer, ＜Draw 1＞.", CardColor.Purple);
        state.Player.Tamers.Add(tamer);
        state.Player.Tamers.Add(placer);
        state.Player.Hand.Add(Digimon("B40-PURP", 3, color: CardColor.Purple));
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, placer, includeInherited: false);
        Assert.Equal(9, state.MemoryGauge);
        Assert.Equal("【서로의 턴】 [턴에 1회] 이 테이머 아래에 퍼플인 카드가 놓였을 때, 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다)하고, 메모리를 +1 한다.", KoreanEffectRenderer.RenderClause(tamer.EffectText));
    }

    [Fact]
    public void WouldDigivolveOrLeave_DeletesTokensAndRecovery_BT14_018()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT14018", 6, "[All Turns] When this Digimon would digivolve or leave the battle area, delete all of your [Amon of Crimson Flame]s and [Umon of Blue Thunder]s. If you deleted a Digimon with this effect, ＜Recovery +1 (Deck)＞ (Place the top card of your deck on top of your security stack.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        var amon = Put(state.Player, Digimon("B40-AMON", 0, name: "Amon of Crimson Flame"));
        int sec = state.Player.SecurityStack.Count;
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.DoesNotContain(amon, state.Player.BattleArea);
        Assert.Equal(sec + 1, state.Player.SecurityStack.Count);
    }

    // =====================================================================
    // 2. 효과를 가진 디지몬과 같은 명칭 (P-045 · BT5-063 · EX1-051)
    // =====================================================================

    [Fact]
    public void SameNameAura_OnlySameNameOthers_EX1_051()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B40-EX1051", 3, inherited: "[All Turns] All of your other Digimon with the same name as this Digimon get +2000 DP.");
        AssertFull(src);
        var holder = Put(state.Player, Digimon("B40-GAB1", 4, name: "Gabumon", dp: 4000), sources: src);
        var same = Put(state.Player, Digimon("B40-GAB2", 4, name: "Gabumon", dp: 4000));
        var other = Put(state.Player, Digimon("B40-AGU", 4, name: "Agumon", dp: 4000));
        engine.RecalculateFieldBuffs();
        Assert.Equal(6000, same.GetCurrentDP());
        Assert.Equal(4000, other.GetCurrentDP());
        Assert.Equal(4000, holder.GetCurrentDP());   // "other" — 자신은 받지 않는다
        Assert.True(Diag40.OwnerAura > 0);
        Assert.Equal("【서로의 턴】 이 디지몬과 같은 명칭의 다른 자신의 디지몬 전부의 DP를 +2000 한다.", KoreanEffectRenderer.RenderClause(src.InheritedEffectText));
    }

    [Fact]
    public void SameNameAura_LostWhenNameChanges_BT5_063()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B40-BT5063", 3, inherited: "[Your Turn] All of your other Digimon with the same name as this Digimon gain ＜Rush＞ (This Digimon can attack the turn it comes into play.).");
        AssertFull(src);
        var holder = Put(state.Player, Digimon("B40-H1", 4, name: "Kotemon"), sources: src);
        holder.IsOwnersTurn = true;
        var same = Put(state.Player, Digimon("B40-H2", 4, name: "Kotemon"), turnPlayed: 2);
        same.IsOwnersTurn = true;
        engine.RecalculateFieldBuffs();
        Assert.Contains(Keyword.Rush, same.ActiveKeywords);
        holder.Evolve(Digimon("B40-H1X", 5, name: "Dinohumon"));   // 공식 Q&A BT5-063: 명칭이 바뀌면 《속공》을 잃는다
        engine.RecalculateFieldBuffs();
        Assert.DoesNotContain(Keyword.Rush, same.ActiveKeywords);
    }

    [Fact]
    public void SameNameAura_DecoyBlackWhite_P_045_Full()
    {
        var src = Digimon("B40-P045", 3, inherited: "Inherited Effect [All Turns] All of your other Digimon with the same name as this Digimon gain ＜Decoy (Black/White)＞ (When your other Black or White Digimon would be deleted by an opponent's effect, you may delete this Digimon to prevent 1 of those Digimon's deletion.)");
        AssertFull(src);
        Assert.StartsWith("【서로의 턴】 이 디지몬과 같은 명칭의 다른 자신의 디지몬 전부는 《디코이《블랙》/《화이트》》", KoreanEffectRenderer.RenderClause(src.InheritedEffectText));
    }

    // =====================================================================
    // 3. 키워드 (≪분리≫ · ≪디코드≫ · ≪디코이≫) · 키워드 부여
    // =====================================================================

    [Fact]
    public void Detach_TrashLinkCardToStay_BT26_010()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT26010", 6, "＜Detach ([Seven Code] trait)＞ (When this Digimon would leave the battle area other than by your effects, by trashing 1 of its specified link cards, it doesn't leave.)");
        AssertFull(card);
        var me = Put(state.Player, card);
        var link = Card("B40-LINK", type: CardType.Digimon, level: 3, traits: "Seven Code");
        me.LinkedCards.Add(link);
        Diag40.Reset();
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(me, state.Player.BattleArea);
        Assert.Empty(me.LinkedCards);
        Assert.Contains(link, state.Player.Trash);
        Assert.Equal(1, Diag40.Detach);
        engine.DeleteByOpponentEffect(state.Player, me);   // 링크 카드가 없으면 떠난다
        Assert.DoesNotContain(me, state.Player.BattleArea);
        Assert.StartsWith("《분리《특징「Seven Code」》》", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void Decode_DecoySpecVariants_Full_And_Korean()
    {
        var a = Digimon("B40-BT26083", 6, "＜Decode ([Junomon]/Lv.5 or lower w/[Iliad] trait)＞ (When this Digimon would leave the battle area other than in battle, you may play 1 specified Digimon card from its digivolution cards without paying the cost.)");
        var b = Digimon("B40-EX12028", 5, "＜Decode ([Lv.4 or lower w/[DS] trait])＞ (When this Digimon would leave the battle area other than in battle, you may play 1 specified Digimon card from its digivolution cards without paying the cost.)");
        var c = Digimon("B40-EX5050", 5, "＜Decoy ([Deva] or [Four Sovereigns] trait)＞ (When your other [Deva] or [Four Sovereigns] trait Digimon would be deleted by an opponent's effect, you may delete this Digimon to prevent 1 of those Digimon's deletion.)");
        AssertFull(a); AssertFull(b); AssertFull(c);
        Assert.StartsWith("《디코드《Lv.4 이하 특징「DS」》》", KoreanEffectRenderer.RenderClause(b.EffectText));
        Assert.StartsWith("《디코이《특징「데바」/「4성수」》》", KoreanEffectRenderer.RenderClause(c.EffectText));
    }

    [Fact]
    public void DecodeLevelSpec_PlaysLowLevelIliadOnly()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-DEC", 6, "＜Decode ([Lv.4 or lower w/[DS] trait])＞ (When this Digimon would leave the battle area other than in battle, you may play 1 specified Digimon card from its digivolution cards without paying the cost.)");
        var ds = Digimon("B40-DS4", 4, traits: "DS");
        var me = Put(state.Player, card, sources: ds);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, ds));
    }

    [Fact]
    public void KeywordGrants_VariousForms_Full()
    {
        string[] texts =
        {
            "[On Play] You may play 2 level 4 or lower Digimon cards with the [Bagra Army] trait from your trash without paying their memory costs. All Digimon played by this effect gain ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn.",
            "[All Turns] While a card with the [Royal Knight] trait is in this Digimon's digivolution cards, this Digimon gains ＜Piercing＞ (When this Digimon attacks and deletes an opponent's Digimon and survives the battle, it performs any security checks it normally would.) and ＜Blocker＞ (This Digimon can block in the blocker timing.), and gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for each card with the [Royal Knight] trait in this Digimon's digivolution cards.",
            "[All Turns] When you play another Digimon, all of your Digimon with the [Royal Knight] trait and all your green Digimon gain ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn.",
            "[All Turns] [Once Per Turn] When a card is added to your security stack, 1 of your opponent's Digimon gets -7000 DP and this Digimon gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.), for the turn.",
            "[Your Turn] When an opponent's Digimon is deleted, this Digimon gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.) for the turn for each of your opponent's Digimon deleted.",
            "[When Digivolving] [When Attacking] By trashing any 3 digivolution cards with the [Mineral] or [Rock] trait from your Digimon, this Digimon unsuspends and gains ＜Security A. +1＞ for the turn. (This Digimon checks 1 additional security card.)",
            "[Security] [All Turns] All of your level 4 or highter Digimon with the [NSo] trait gain ＜Scapegoat＞ (When this Digimon would be deleted other than by your effects, by deleting 1 of your other Digimon, prevent that deletion.)",
            "[All Turns] All of your Digimon with the [Xros Heart] trait or with ＜Retaliation＞ gain ＜Rush＞ and ＜Blocker＞.",
            "[All Turns] All of your Tokens and [Puppet] trait Digimon gain ＜Alliance＞ (When this Digimon attacks, by suspending 1 of your other Digimon, add the suspended Digimon's DP to this Digimon and it gains ＜Security A. +1＞ for the attack.)",
        };
        int i = 0;
        foreach (var t in texts)
        {
            var card = Digimon("B40-KW" + i++, 6, t);
            AssertFull(card);
            Assert.NotNull(KoreanEffectRenderer.RenderClause(t));
        }
    }

    [Fact]
    public void SecurityAndPlayedByEffects_KeywordGrants_Full()
    {
        var sec = Card("B40-BT25094", "[Security] [Your Turn] All of your red or blue [TS] trait Digimon gain ＜Alliance＞ (When this Digimon attacks, by suspending 1 of your other Digimon, add the suspended Digimon's DP to this Digimon and it gains ＜Security A. +1＞ for the attack.). While you have [Apollomon] or [Dianamon], they also gain ＜Rush＞ (This Digimon can attack the turn it comes into play.)", type: CardType.Option);
        AssertFull(sec);
        Assert.StartsWith("[시큐리티]【자신의 턴】 특징으로 「TS」를 가진 레드/블루인 자신의 디지몬 전부는 《연계》", KoreanEffectRenderer.RenderClause(sec.EffectText));
        var t = Tamer("B40-BT12098", "[Main] If you have 4 or more Tamers in play, by suspending this Tamer, 1 of your Digimon with ＜Save＞ in its text gains ＜Security A. +1＞ for the turn. (This Digimon checks 1 additional security card.)");
        AssertFull(t);
        Assert.Contains("이 테이머를 레스트시키는 것으로", KoreanEffectRenderer.RenderClause(t.EffectText));
        Assert.Contains("《세이브》가 기술되어 있는 자신의 디지몬 1마리", KoreanEffectRenderer.RenderClause(t.EffectText));
    }

    [Fact]
    public void CompoundForTheTurn_BothActions_BT14_041()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT14041", 6, "[All Turns] [Once Per Turn] When a card is added to your security stack, 1 of your opponent's Digimon gets -7000 DP and this Digimon gains ＜Security A. +1＞ (This Digimon checks 1 additional security card.), for the turn.");
        var me = Put(state.Player, card);
        var victim = Put(state.DummyOpponent, Digimon("B40-V7", 5, dp: 9000));
        engine.Recovery(state.Player, 1);
        Assert.Equal(2000, victim.GetCurrentDP());
        Assert.Equal(1, me.EffectSecurityAttackBonus);
    }

    // =====================================================================
    // 4. 여러 Lv. 1마리씩 소멸
    // =====================================================================

    [Fact]
    public void MultiLevelDelete_OneOfEachLevel_EX7_071()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B40-EX7071", "[Main] Delete 1 of your opponent's level 3, 1 of their level 4, and 1 of their level 5 Digimon.", type: CardType.Option);
        AssertFull(card);
        var l3 = Put(state.DummyOpponent, Digimon("B40-O3", 3));
        var l4a = Put(state.DummyOpponent, Digimon("B40-O4A", 4));
        var l4b = Put(state.DummyOpponent, Digimon("B40-O4B", 4));
        var l6 = Put(state.DummyOpponent, Digimon("B40-O6", 6));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card, includeInherited: false);
        Assert.DoesNotContain(l3, state.DummyOpponent.BattleArea);
        Assert.Single(state.DummyOpponent.BattleArea.Where(d => d.TopCard.Level == 4));   // Lv.4는 1마리만
        Assert.Contains(l6, state.DummyOpponent.BattleArea);
        Assert.Equal("【메인】 Lv.3과 Lv.4와 Lv.5의 상대의 디지몬 1마리씩을 소멸시킨다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    // =====================================================================
    // 5. 공개 · 시큐리티
    // =====================================================================

    [Fact]
    public void RevealBudget_PlaysWithinTotalCost_BT14_068()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT14068", 6, "[End of Your Turn] [Once Per Turn] Reveal the top 3 cards of your deck. You may play up to 7 play cost's total worth of cards with the [D-Brigade] or [DigiPolice] trait among them without paying the costs. Trash the rest.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var a = Digimon("B40-DB5", 4, playCost: 5, traits: "D-Brigade");
        var b = Digimon("B40-DB4", 3, playCost: 4, traits: "D-Brigade");
        var c = Digimon("B40-DB2", 3, playCost: 2, traits: "DigiPolice");
        state.Player.MainDeck.InsertRange(0, new[] { a, b, c });
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, me, card);
        var played = state.Player.BattleArea.Select(d => d.TopCard).ToList();
        Assert.Contains(a, played);   // 5 + 2 = 7 (4는 넘친다)
        Assert.Contains(c, played);
        Assert.DoesNotContain(b, played);
        Assert.Contains(b, state.Player.Trash);
        Assert.Contains("등장 코스트 합계 7까지", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void RevealOpponentSecurity_DigimonOrNot_RB1_027()
    {
        var (state, engine, choices) = NewMainPhase();
        var card = Digimon("B40-RB1027", 5, "[On Play] [When Digivolving] Reveal the top card of your opponent's security stack. If that card is a Digimon card, gain 1 memory. If that card is a non-Digimon, ＜Draw 1＞. Place the revealed card on the top or bottom of your opponent's security stack face down.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var top = state.DummyOpponent.SecurityStack[0];
        choices.ConfirmAnswer = true;   // 맨 아래로
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(9, state.MemoryGauge);
        Assert.Same(top, state.DummyOpponent.SecurityStack.Last());
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);
        Assert.Equal("【등장 시】 【진화 시】 상대의 시큐리티를 위에서부터 1장 오픈한다. 그 카드가 디지몬 카드라면, 메모리를 +1 한다. 디지몬 카드 이외라면, 《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다). 오픈한 카드는 시큐리티 위 또는 아래로 뒷면으로 되돌린다.",
            KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void SecuritySearchDigivolve_ChoosesFromSecurity_BT14_033()
    {
        var (state, engine, choices) = NewMainPhase();
        var card = Digimon("B40-BT14033", 5, "[Start of Your Main Phase] Search your security stack. This Digimon may digivolve into a yellow Digimon card with the [Vaccine] trait among them without paying the cost. Then, shuffle your security stack. If digivolved by this effect, you may place 1 yellow card with the [Vaccine] trait from your hand at the bottom of your security stack.", color: CardColor.Yellow);
        AssertFull(card);
        var me = Put(state.Player, card);
        var target = Card("B40-VAC6", type: CardType.Digimon, level: 6, color: CardColor.Yellow, dp: 11000, traits: "Vaccine");
        target.EvoRequirementLevel = 5; target.EvoRequirementColor = CardColor.Yellow; target.EvoCost = 3;
        state.Player.SecurityStack.Insert(2, target);
        var back = Card("B40-VACH", type: CardType.Option, color: CardColor.Yellow, traits: "Vaccine");
        state.Player.Hand.Add(back);
        IReadOnlyList<CardData>? offered = null;
        choices.CardPick = (opts, min, max) => { offered ??= opts; return new[] { 0 }; };
        Diag40.Reset();
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, me, card);
        Assert.NotNull(offered);
        Assert.Contains(target, offered!);   // 시큐리티 카드 목록에서 고른다
        Assert.Same(target, me.TopCard);
        Assert.Equal(8, state.MemoryGauge);   // 코스트 없이
        Assert.Contains(back, state.Player.SecurityStack);   // "If digivolved by this effect" — 패의 카드를 시큐리티 아래에
        Assert.Equal(6, state.Player.SecurityStack.Count);   // 5 + 넣은 1장 - 진화한 1장 + 아래에 놓은 1장
        Assert.True(Diag40.SecuritySearch > 0);
        Assert.Contains("이 디지몬을 그중 특징으로 「백신종」을 가진 옐로의 디지몬 카드로 코스트를 지불하지 않고 진화시킬 수 있다", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void RevealTopPlayOrAdd_EX5_042()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-EX5042", 5, "[On Play] [When Digivolving] Reveal the top card of your deck. If that card is a level 5 or lower Digimon card with ＜Fortitude＞, play it without paying the cost. Add the rest to the hand.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var fort = Digimon("B40-FORT", 4, "＜Fortitude＞ (When this Digimon with digivolution cards is deleted, play this card without paying the cost.)");
        state.Player.MainDeck.Insert(0, fort);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, fort));
        var plain = Digimon("B40-PLAIN", 4);
        state.Player.MainDeck.Insert(0, plain);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Contains(plain, state.Player.Hand);
    }

    // =====================================================================
    // 6. 코스트
    // =====================================================================

    [Fact]
    public void UseCostEqualsSecurity_BT7_100()
    {
        var (state, _, _) = NewMainPhase();
        var card = Card("B40-BT7100", "When this card would be used from your hand, its memory cost is equal to the number of cards in your security stack.", type: CardType.Option, playCost: 5);
        AssertFull(card);
        Assert.Equal(5, CardScripts.EffectivePlayCost(state.Player, card, state.DummyOpponent));
        state.Player.SecurityStack.RemoveRange(0, 3);
        Assert.Equal(2, CardScripts.EffectivePlayCost(state.Player, card, state.DummyOpponent));
        state.Player.SecurityStack.Clear();
        Assert.Equal(0, CardScripts.EffectivePlayCost(state.Player, card, state.DummyOpponent));   // 공식 Q&A: 0장이면 0 코스트
    }

    [Fact]
    public void EvoCostEqualsSecurity_ZeroMeansOne_BT7_040()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT7040", 6, "When you would digivolve into this card from your hand, the memory cost of the digivolution is equal to the number of cards in your security stack. If you have 0 security cards, the memory cost is 1.");
        card.EvoCost = 4; card.EvoRequirementLevel = 5;
        AssertFull(card);
        var d = Put(state.Player, Digimon("B40-B5", 5));
        Assert.Equal(5, engine.PreviewEvoCost(state.Player, d, card));   // 시큐리티 5장 (원래 4보다 많다)
        state.Player.SecurityStack.RemoveRange(0, 3);
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, d, card));
        state.Player.SecurityStack.Clear();
        Assert.Equal(1, engine.PreviewEvoCost(state.Player, d, card));
    }

    [Fact]
    public void OpponentEvoCostIncrease_NoSources_EX3_016()
    {
        var (state, engine, _) = NewMainPhase();
        var src = Digimon("B40-EX3016", 3, inherited: "[Opponent's Turn] When an opponent's Digimon with no digivolution cards would digivolve, increase the digivolution cost by 1.");
        AssertFull(src);
        Put(state.DummyOpponent, Digimon("B40-OTOP", 5), sources: src);
        var mine = Put(state.Player, Digimon("B40-M3", 3));
        var evo = Digimon("B40-M4", 4);
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, mine, evo));   // 1 + 1
        mine.AddBottomSource(Digimon("B40-EGG", 2));
        Assert.Equal(1, engine.PreviewEvoCost(state.Player, mine, evo));
        Assert.Equal("【상대의 턴】 진화원을 갖지 않은 상대의 디지몬이 진화할 때, 지불하는 진화 코스트 +1.", KoreanEffectRenderer.RenderClause(src.InheritedEffectText));
    }

    [Fact]
    public void TamerFieldCost_NoDigimonReducesMore_BT26_088()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B40-BT26088", "[Your Turn] When any [Boss] or [TS] trait Digimon cards would be played, by suspending this Tamer, reduce the costs by 1. If you have no Digimon, instead reduce the costs by 2.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var boss = Digimon("B40-BOSS", 5, playCost: 6, traits: "Boss");
        Assert.Equal(4, engine.PreviewPlayCostWithOptions(state.Player, boss));
        Put(state.Player, Digimon("B40-ANY", 3));
        Assert.Equal(5, engine.PreviewPlayCostWithOptions(state.Player, boss));
    }

    [Fact]
    public void TrashSecurityForEvoReduction_P_074()
    {
        var (state, engine, choices) = NewMainPhase();
        var card = Digimon("B40-P074", 4, "[Your Turn] When this Digimon would digivolve into a card with the [Shaman] or [Wizard] trait, you may trash up to 3 cards from the top of your security stack. For each security card trashed by this effect, reduce the digivolution cost by 1.");
        AssertFull(card);
        var d = Put(state.Player, card);
        d.IsOwnersTurn = true;
        var evo = Card("B40-SHAMAN", type: CardType.Digimon, level: 5, traits: "Shaman");
        evo.EvoRequirementLevel = 4; evo.EvoCost = 3;
        state.Player.Hand.Add(evo);
        engine.EvolveDigimon(d, evo);
        Assert.Same(evo, d.TopCard);
        int trashed = 5 - state.Player.SecurityStack.Count;
        Assert.InRange(trashed, 1, 3);
        Assert.Equal(8 - (3 - trashed), state.MemoryGauge);
    }

    [Fact]
    public void DeleteOtherForEvoReduction_NotTheDigivolvingOne_EX2_064()
    {
        var (state, engine, _) = NewMainPhase();
        var tamer = Tamer("B40-EX2064", "[Your Turn] [Once Per Turn] When one of your Digimon would digivolve from level 5 to level 6, you may delete 1 of your Digimon to reduce the digivolution cost by 3.");
        AssertFull(tamer);
        state.Player.Tamers.Add(tamer);
        var d = Put(state.Player, Digimon("B40-L5E", 5));
        d.IsOwnersTurn = true;
        var fodder = Put(state.Player, Digimon("B40-FOD", 3));
        var evo = Digimon("B40-L6E", 6);
        evo.EvoCost = 4;
        state.Player.Hand.Add(evo);
        engine.EvolveDigimon(d, evo);
        Assert.Same(evo, d.TopCard);
        Assert.Contains(d, state.Player.BattleArea);
        Assert.DoesNotContain(fodder, state.Player.BattleArea);
        Assert.Equal(8 - 1, state.MemoryGauge);
    }

    // =====================================================================
    // 7. 그 밖
    // =====================================================================

    [Fact]
    public void LeaveProtection_ExcludesNamedDigimon_LM_019()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-LM019", 4, "[All Turns] When one of your Digimon with [Gammamon] in its text, other than [Bokomon], would leave the battle area other than by one of your effects, by deleting this Digimon, prevent it from leaving.");
        AssertFull(card);
        var me = Put(state.Player, card);
        var boko = Put(state.Player, Digimon("B40-BOKO", 3, "[On Play] Play 1 [Gammamon] from your hand.", name: "Bokomon"));
        engine.DeleteByOpponentEffect(state.Player, boko);
        Assert.DoesNotContain(boko, state.Player.BattleArea);   // 「보코몬」은 지키지 않는다
        Assert.Contains(me, state.Player.BattleArea);
        var gamma = Put(state.Player, Digimon("B40-GAMMA", 4, "[On Play] Add 1 [Gammamon] from your trash to your hand."));
        engine.DeleteByOpponentEffect(state.Player, gamma);
        Assert.Contains(gamma, state.Player.BattleArea);
        Assert.DoesNotContain(me, state.Player.BattleArea);
        Assert.StartsWith("【서로의 턴】 「보코몬」 이외의 「감마몬」이 기술되어 있는 자신의 디지몬이", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void TamersOnlyDigivolve_BT21_091()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Card("B40-BT21091", "[Main] 1 of your Tamers may digivolve into a Digimon card with the [Hybrid] trait in the hand without paying the cost.", type: CardType.Option);
        AssertFull(card);
        var tamer = Tamer("B40-TAKUYA", color: CardColor.Red);
        state.Player.Tamers.Add(tamer);
        var d = Put(state.Player, Digimon("B40-NOTME", 3, color: CardColor.Red));
        var hybrid = Digimon("B40-AGUNI", 4, "You may digivolve this card from your hand onto one of your red Tamers as if the Tamer is a level 3 red Digimon.", color: CardColor.Red, traits: "Hybrid");
        hybrid.EvoRequirementLevel = 3;   // 엔진 규칙: 진화 조건 Lv. = 취급하는 Lv.
        state.Player.Hand.Add(hybrid);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card, includeInherited: false);
        Assert.DoesNotContain(tamer, state.Player.Tamers);
        Assert.Contains(state.Player.BattleArea, x => ReferenceEquals(x.TopCard, hybrid));
        Assert.Equal(3, d.TopCard.Level);   // 디지몬은 고르지 않는다
        Assert.Equal("【메인】 자신의 테이머 1명을 패의 특징으로 「하이브리드체」를 가진 디지몬 카드로 코스트를 지불하지 않고 진화시킬 수 있다.", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void ItAfterPlay_KoreanReferent_EX13_060()
    {
        Assert.Contains("이 턴 동안, 그 디지몬은 《속공》",
            KoreanEffectRenderer.RenderClause("[End of Your Turn] [Once Per Turn] You may play 1 [Chronicle] trait card without [Alphamon] in its name from your hand with the cost reduced by 6. It gains ＜Rush＞ for the turn. (This Digimon can attack the turn it was played.)"));
    }

    [Fact]
    public void NameWithPeriod_SentenceNotSplit_BT13_025()
    {
        var card = Digimon("B40-BT13025", 5, "[When Digivolving] If you don't have a [Thomas H. Norstein] in play, you may play 1 [Thomas H. Norstein] from your hand without paying its cost.");
        AssertFull(card);
        var (state, engine, _) = NewMainPhase();
        var me = Put(state.Player, card);
        var thomas = Tamer("B40-THOMAS", name: "Thomas H. Norstein");
        state.Player.Hand.Add(thomas);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Contains(thomas, state.Player.Tamers);
    }

    [Fact]
    public void ScheduledSelfDeletion_IfDigivolvedByThisEffect_BT17_011()
    {
        var (state, engine, _) = NewMainPhase();
        var card = Digimon("B40-BT17011B", 5, "[When Digivolving] If [BurningGreymon] is in this Digimon's digivolution cards or you have a blue or green Digimon or Tamer, this Digimon may digivolve into [AncientGreymon] in the hand for a digivolution cost of 3, ignoring its digivolution requirements. If digivolved by this effect, delete this Digimon at the end of the turn.", color: CardColor.Red);
        AssertFull(card);
        var me = Put(state.Player, card);
        state.Player.Tamers.Add(Tamer("B40-BLUE", color: CardColor.Blue));
        var ancient = Digimon("B40-ANC", 6, name: "AncientGreymon", color: CardColor.Red);
        state.Player.Hand.Add(ancient);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Same(ancient, me.TopCard);
        Assert.Equal(5, state.MemoryGauge);
        engine.PassTurn();
        Assert.DoesNotContain(me, state.Player.BattleArea);
    }
}
