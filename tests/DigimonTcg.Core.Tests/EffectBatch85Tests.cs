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
/// v0.85.0 59차 묶음: 테이머 아래 카드를 테이머마다 (PlayerZones.UnderTamerHosts85 · GoldfishEngine.UnderTamer85) —
/// 종합 룰 4-4-2 (맨 아래에) · 4-7-8 (떠난 테이머 아래 카드만 파기) · 4-7-9 (뒷면) · 16-20 ≪세이브≫ "테이머 1명" · 16-28 ≪마인드 링크≫ · 3-4-5/4-3-2 (테이머에서 진화) ·
/// 공식 Q&amp;A ST24-06 Q6212 (여러 테이머 아래에서 합계 N장) · BT10-093 Q2024 · AD1-006 Q6059 ~ Q6063 · BT11-087 Q1 · BT21-092 Q4606 · BT12-083.
/// 새로 읽는 문장 21개 (BT19-090 ×3 포함)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch85Tests
{
    /// <summary>테이머를 고르는 창에서 이름이 맞는 테이머를 고른다 (없으면 추천) · 카드 고르기는 추천</summary>
    private sealed class PickTamer(string name) : IChoiceProvider
    {
        public List<string> Questions { get; } = new();
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) { Questions.Add(question); return true; }
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional)
        {
            Questions.Add(question);
            return options.FirstOrDefault(o => o.TopCard.Name == name) ?? recommended ?? options.FirstOrDefault();
        }
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int playCost = 3, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, -1, name, "", traits);

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Red, string? name = null, string inherited = "", params string[] traits) =>
        Card(id, main, CardType.Tamer, 0, 2, color, 0, name, inherited, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 12; i++)
        {
            state.Player.MainDeck.Add(Digimon("B85-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B85-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B85-SEC" + i, 3, color: CardColor.White));
            state.DummyOpponent.SecurityStack.Add(Digimon("B85-OSEC" + i, 3, color: CardColor.White));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones z, CardData c, params CardData[] sources)
    {
        var d = new CardInstance(c, 1) { IsOwnersTurn = true };
        foreach (var s in sources) d.AddBottomSource(s);
        z.BattleArea.Add(d);
        return d;
    }

    private static string L(GameState s) => string.Join(" | ", s.ActionLogs.TakeLast(12));

    private static void AssertFull(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, cl => Assert.True(cl.Support == ClauseSupport.Supported, $"{card.CardId}: {cl.Text} — {cl.Note}"));

    // =====================================================================
    // 1. 모델 — 테이머마다
    // =====================================================================

    [Fact]
    public void Save_PlacesUnderTheChosenTamer_Rule16_20()
    {
        var (state, engine) = NewMainPhase();
        var a = Tamer("B85-TA", name: "Tamer A");
        var b = Tamer("B85-TB", name: "Tamer B");
        state.Player.Tamers.Add(a);
        state.Player.Tamers.Add(b);
        var saver = Digimon("B85-SAVE", 3, "[On Deletion] ＜Save＞");
        AssertFull(saver);
        state.Player.Trash.Add(saver);
        engine.PlayerChoices = new PickTamer("Tamer B");
        engine.RunEffects(EffectTrigger.OnDeletion, state.Player, null, saver, includeInherited: false);
        Assert.Same(b, state.Player.HostOfUnderTamer(saver));
        Assert.Equal(0, state.Player.UnderCountOf(a));
        Assert.Equal(1, state.Player.UnderCountOf(b));
    }

    [Fact]
    public void TamerLeaving_TrashesOnlyItsOwnUnderCards_Rule4_7_8()
    {
        var (state, engine) = NewMainPhase();
        var a = Tamer("B85-LA", name: "Tamer A");
        var b = Tamer("B85-LB", name: "Tamer B");
        state.Player.Tamers.Add(a);
        state.Player.Tamers.Add(b);
        var ua = Digimon("B85-UA", 3); var ub = Digimon("B85-UB", 3);
        state.Player.PlaceUnderTamer85(a, ua);
        state.Player.PlaceUnderTamer85(b, ub);
        engine.DeleteTamer(state.Player, a);
        Assert.Contains(ua, state.Player.Trash);
        Assert.Contains(ub, state.Player.UnderTamers);   // v0.85.0 전에는 테이머가 남아 있으면 둘 다 남았다
        Assert.Same(b, state.Player.HostOfUnderTamer(ub));
        Assert.Equal(0, engine.UnderTamerLateSweeps85);
    }

    [Fact]
    public void MindLink_TamerLeavesArea_ItsUnderCardsAreTrashed_Rule16_28()
    {
        var (state, engine) = NewMainPhase();
        var link = Tamer("B85-MIND", "[Main] ＜Mind Link＞ with 1 of your Digimon. (Place this Tamer as the bottom digivolution card of 1 of your Digimon without Tamer cards in its digivolution cards.)", name: "Linker");
        var other = Tamer("B85-OTHER", name: "Other");
        state.Player.Tamers.Add(link);
        state.Player.Tamers.Add(other);
        var u1 = Digimon("B85-MU1", 3); var u2 = Digimon("B85-MU2", 3);
        state.Player.PlaceUnderTamer85(link, u1);
        state.Player.PlaceUnderTamer85(other, u2);
        var d = Put(state.Player, Digimon("B85-HOSTMON", 4));
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, link, includeInherited: false);
        Assert.Contains(link, d.EvolutionSources);
        Assert.Contains(u1, state.Player.Trash);
        Assert.Contains(u2, state.Player.UnderTamers);
    }

    [Fact]
    public void TamerEvolution_TakesOnlyThatTamersUnderCards_Rule3_4_5()
    {
        var (state, engine) = NewMainPhase();
        var a = Tamer("B85-EA", name: "Tamer A", color: CardColor.Red);
        var b = Tamer("B85-EB", name: "Tamer B");
        state.Player.Tamers.Add(a);
        state.Player.Tamers.Add(b);
        var ua1 = Digimon("B85-EUA1", 3); var ua2 = Digimon("B85-EUA2", 3); var ub = Digimon("B85-EUB", 3);
        state.Player.PlaceUnderTamer85(a, ua1);
        state.Player.PlaceUnderTamer85(b, ub);
        state.Player.PlaceUnderTamer85(a, ua2);
        var fd = state.Player.AddUnderTamerFaceDown(a, Digimon("B85-EFD", 3));
        var evo = Digimon("B85-EVO", 4, color: CardColor.Red);
        state.Player.Hand.Add(evo);
        var inst = engine.EvolveTamerByEffect(state.Player, a, evo, 0);
        Assert.NotNull(inst);
        Assert.Contains(ua1, inst!.EvolutionSources);
        Assert.Contains(ua2, inst.EvolutionSources);
        Assert.True(inst.IsFaceDown(fd));   // 뒷면은 뒷면 그대로
        Assert.DoesNotContain(ub, inst.EvolutionSources);
        Assert.Same(b, state.Player.HostOfUnderTamer(ub));
    }

    [Fact]
    public void ThisTamerHasNCardsUnderIt_CountsOnlyThisTamer()
    {
        var (state, engine) = NewMainPhase();
        var t = Tamer("B85-COND", "[Start of Your Main Phase] If this Tamer has 2 or fewer cards under it, gain 1 memory.", name: "Counter");
        var other = Tamer("B85-COND-O", name: "Other");
        AssertFull(t);
        state.Player.Tamers.Add(t);
        state.Player.Tamers.Add(other);
        for (int i = 0; i < 4; i++) state.Player.PlaceUnderTamer85(other, Digimon("B85-CU" + i, 3));
        state.Player.PlaceUnderTamer85(t, Digimon("B85-CT", 3));
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, null, t, includeInherited: false);
        Assert.Equal(9, state.MemoryGauge);   // 이 테이머 아래 1장 (전에는 모든 테이머 아래 5장으로 세어 조건을 못 채웠다)
    }

    [Fact]
    public void FaceDownTrash_TotalAcrossTamers_Q6212_AndThisTamerTrigger()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Tamer("B85-FDW", "[Your Turn] When effects trash cards from under this Tamer, gain 1 memory.", name: "Watcher");
        var other = Tamer("B85-FDO", name: "Other");
        AssertFull(watcher);
        state.Player.Tamers.Add(watcher);
        state.Player.Tamers.Add(other);
        state.Player.AddUnderTamerFaceDown(other, Digimon("B85-FD1", 3));
        state.Player.AddUnderTamerFaceDown(other, Digimon("B85-FD2", 3));
        // 다른 테이머 아래에서만 파기 — "이 테이머 아래에서" 유발하지 않는다
        engine.TrashBottomFaceDownUnderTamers(state.Player, 1);
        Assert.Equal(8, state.MemoryGauge);
        state.Player.AddUnderTamerFaceDown(watcher, Digimon("B85-FD3", 3));
        var trashed = engine.TrashBottomFaceDownUnderTamers(state.Player, 2);   // 두 테이머 아래에서 합계 2장 (Q6212)
        Assert.Equal(2, trashed.Count);
        Assert.Equal(0, state.Player.FaceDownUnderTamerCount);
        Assert.Equal(9, state.MemoryGauge);
    }

    [Fact]
    public void OpponentEffectTrashingUnderMyTamer_FiresMyTrigger()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Tamer("B85-OPW", "[All Turns] When effects trash cards from under this Tamer, gain 1 memory.", name: "Watcher");
        state.DummyOpponent.Tamers.Add(watcher);
        state.DummyOpponent.PlaceUnderTamer85(watcher, Digimon("B85-OPU", 3));
        var trasher = Card("B85-TRASHER", "[Main] Trash any 1 card under your opponent's Digimon or Tamers.");
        AssertFull(trasher);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, trasher, includeInherited: false);
        Assert.True(state.DummyOpponent.UnderTamers.Count == 0, L(state));
        Assert.Equal(mem - 1, state.MemoryGauge);   // 상대의 메모리 +1 (게이지는 상대 쪽으로 1)
    }

    [Fact]
    public void RevealPlace_TraitTamerFilter_ChoosesOnlyMatchingTamer()
    {
        var (state, engine) = NewMainPhase();
        var plain = Tamer("B85-RP-PLAIN", name: "Plain");
        var gd = Tamer("B85-RP-GD", name: "Dawn", traits: "Glowing Dawn");
        state.Player.Tamers.Add(plain);
        state.Player.Tamers.Add(gd);
        var card = Card("B85-RP", "[Main] Place the top card of your deck face down under any of your [Glowing Dawn] trait Tamers.");
        AssertFull(card);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card, includeInherited: false);
        Assert.True(state.Player.FaceDownUnderCountOf(gd) == 1, L(state));
        Assert.Equal(0, state.Player.UnderCountOf(plain));
    }

    // =====================================================================
    // 2. 새로 읽는 문장
    // =====================================================================

    [Fact]
    public void BT21_012_PlaceThisDigimonUnderThePlayedTamer()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("BT21-012", 3, "[Main] By suspending this Digimon, you may play 1 red Tamer card with inherited effects from your hand without paying the cost. If you did, place this Digimon under the played Tamer.", color: CardColor.Red);
        AssertFull(card);
        var me = Put(state.Player, card, Digimon("B85-12SRC", 2));
        var tamer = Tamer("B85-12T", name: "Hybrid Tamer", inherited: "[Your Turn] This Digimon gets +1000 DP.");
        state.Player.Hand.Add(tamer);
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, me, card);
        Assert.Contains(tamer, state.Player.Tamers);
        Assert.DoesNotContain(me, state.Player.BattleArea);
        Assert.Same(tamer, state.Player.HostOfUnderTamer(card));
        Assert.NotNull(KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void BT27_098_PlaceThisTamerUnderPlayed_ItsUnderCardsTrashed()
    {
        var (state, engine) = NewMainPhase();
        var t = Tamer("BT27-098", "[End of All Turns] By trashing 5 bottom face-down cards from under any of your Tamers, you may play 1 Digimon card with [Gizmon] or [Belphemon] in its name from your hand or trash without paying the cost. After, place this Tamer as its bottom digivolution card.", CardColor.Black);
        AssertFull(t);
        state.Player.Tamers.Add(t);
        for (int i = 0; i < 6; i++) state.Player.AddUnderTamerFaceDown(t, Digimon("B85-98FD" + i, 3));
        var giz = Digimon("B85-98GIZ", 5, name: "Gizmon: XT");
        state.Player.Hand.Add(giz);
        engine.RunEffects(EffectTrigger.EndOfYourTurn, state.Player, null, t, includeInherited: false);
        var d = Assert.Single(state.Player.BattleArea, x => x.TopCard == giz);
        Assert.Contains(t, d.EvolutionSources);
        Assert.DoesNotContain(t, state.Player.Tamers);
        Assert.Empty(state.Player.UnderTamers);   // 남은 뒷면 1장은 테이머와 함께 파기 (4-7-8)
    }

    [Fact]
    public void BT21_013_DualDestination_TamerWhenItCanDigivolve()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("BT21-013", 4, "[When Digivolving] You may place 1 [Hybrid] or [Hero] trait Digimon card from your hand or trash as this Digimon's bottom digivolution card or under any of your red Tamers with inherited effects.", color: CardColor.Red);
        AssertFull(card);
        var me = Put(state.Player, card);
        var red = Tamer("B85-13R", name: "Red Hybrid", color: CardColor.Red, inherited: "[Your Turn] This Digimon gets +1000 DP.");
        var blue = Tamer("B85-13B", name: "Blue Hybrid", color: CardColor.Blue, inherited: "[Your Turn] This Digimon gets +1000 DP.");
        state.Player.Tamers.Add(red);
        state.Player.Tamers.Add(blue);
        var h = Digimon("B85-13H", 3, traits: "Hybrid");
        state.Player.Trash.Add(h);
        engine.PlayerChoices = new PickTamer("Red Hybrid");
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Same(red, state.Player.HostOfUnderTamer(h));   // 블루 테이머는 후보가 아니다
        Assert.NotNull(KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void BT21_092_AllDigimonSourcesUnderOneTamer_ThenPlayReduced()
    {
        var (state, engine) = NewMainPhase(memory: 3);
        var card = Card("BT21-092", "[Main] Place all Digimon cards in 1 of your [Xros Heart] trait Digimon's digivolution cards under 1 of your Tamers. Then, you may play 1 Digimon card with the [Xros Heart] trait from your hand with the play cost reduced by 1 for each card this effect placed.", color: CardColor.Red);
        AssertFull(card);
        var t = Tamer("B85-92T");
        state.Player.Tamers.Add(t);
        var s1 = Digimon("B85-92S1", 3); var s2 = Digimon("B85-92S2", 4);
        Put(state.Player, Digimon("B85-92X", 5, traits: "Xros Heart"), s1, s2);
        var hand = Digimon("B85-92H", 5, playCost: 5, traits: "Xros Heart");
        state.Player.Hand.Add(hand);
        engine.PlayerChoices = new PickTamer("-");   // "할 수 있다" — 등장한다
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card, includeInherited: false);
        Assert.True(state.Player.UnderCountOf(t) == 2, L(state));
        Assert.True(state.Player.BattleArea.Any(d => d.TopCard == hand), L(state));
        Assert.Equal(0, state.MemoryGauge);   // 5 - 2 = 3 지불
    }

    [Fact]
    public void AD1_006_PlaceAtLeastOneThenPlayFromRemainingSources_Q6062()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("AD1-006", 6, "[All Turns] When this Digimon would leave the battle area other than by DigiXros, from this Digimon's digivolution cards, you may place up to 4 [Xros Heart] or [Blue Flare] trait Digimon cards under 1 of your Tamers and play 1 such card without paying the cost.", color: CardColor.Red);
        var t = Tamer("B85-06T");
        state.Player.Tamers.Add(t);
        var big = Digimon("B85-06BIG", 5, playCost: 7, traits: "Xros Heart");
        var small = Digimon("B85-06S", 3, playCost: 3, traits: "Blue Flare");
        var me = Put(state.Player, card, big, small);
        engine.DeleteByOpponentEffect(state.Player, me);
        Assert.Same(t, state.Player.HostOfUnderTamer(small));   // 1장 이상 테이머 아래
        Assert.True(state.Player.BattleArea.Any(d => d.TopCard == big), L(state));   // 남은 진화원에서 등장
        Assert.NotNull(KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void BT7_085_FiveHybridUnderThisTamer_DigivolveAsLevel5Red()
    {
        var (state, engine) = NewMainPhase();
        var t = Tamer("BT7-085", SentenceParser.Bt7085Api85, CardColor.Red, name: "Takuya Kanbara");
        AssertFull(t);
        state.Player.Tamers.Add(t);
        for (int i = 0; i < 5; i++) state.Player.Trash.Add(Digimon("B85-85H" + i, 3 + i % 3, traits: "Hybrid"));
        var emp = Digimon("B85-85EMP", 6, name: "EmperorGreymon", color: CardColor.Red);
        emp.EvoRequirementLevel = 5; emp.EvoCost = 3;
        state.Player.Hand.Add(emp);
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, null, t, includeInherited: false);
        var d = Assert.Single(state.Player.BattleArea, x => x.TopCard == emp);
        Assert.Equal(6, d.EvolutionSources.Count);   // 테이머 + 아래 5장
        Assert.Equal(5, state.MemoryGauge);
        Assert.Contains("레드인 Lv.5", KoreanEffectRenderer.RenderClause(t.EffectText));
    }

    [Fact]
    public void BT12_081_QuartzmonInstead_WhenFourSources()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("BT12-081", 5, SentenceParser.Bt12081Api85, color: CardColor.Purple);
        AssertFull(card);
        var me = Put(state.Player, card, Digimon("B85-81S1", 4), Digimon("B85-81S2", 3), Digimon("B85-81S3", 3), Digimon("B85-81S4", 2));
        var t = Tamer("B85-81T");
        state.Player.Tamers.Add(t);
        var q = Digimon("B85-81Q", 6, name: "Quartzmon", color: CardColor.Purple);
        q.EvoCost = 5;
        state.Player.PlaceUnderTamer85(t, q);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, card);
        Assert.Same(q, me.TopCard);   // 테이머 아래의 「쿼츠몬」으로 진화
        Assert.Equal(6, state.MemoryGauge);   // 5 - 3 = 2
    }

    [Fact]
    public void P_249_PlaceUnderTamerThenTamerDigivolves()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("P-249", 3, SentenceParser.P249Api85, color: CardColor.Yellow);
        AssertFull(card);
        var me = Put(state.Player, card);
        var t = Tamer("B85-249T", color: CardColor.Yellow, inherited: "[Your Turn] This Digimon gets +1000 DP.");
        state.Player.Tamers.Add(t);
        var placed = Digimon("B85-249H", 3, traits: "Hybrid");
        state.Player.Trash.Add(placed);
        var evo = Digimon("B85-249EVO", 4, color: CardColor.Yellow, playCost: 5, traits: "Hybrid");
        evo.EvoCost = 3;
        state.Player.Hand.Add(evo);
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, me, card);
        Assert.True(me.TopCard == evo, L(state));   // 이 디지몬의 진화원 맨 아래에 놓고 패의 「하이브리드체」로 코스트 -2 진화
        Assert.Contains(placed, me.EvolutionSources);
        Assert.Equal(7, state.MemoryGauge);   // 3 - 2 = 1
    }

    [Fact]
    public void BT18_096_PlaceTamersWithDifferentColors_MemoryPerTamer()
    {
        var (state, engine) = NewMainPhase(memory: 0);
        var card = Card("BT18-096", "[Main] 1 of your Digimon or Tamers may digivolve into [Susanoomon] in the hand or trash without paying the cost. Then, by placing up to 4 of your Tamers with different colors under 1 of your [Susanoomon] as its bottom digivolution cards, gain 1 memory for each placed card.");
        AssertFull(card);
        var su = Put(state.Player, Digimon("B85-96SU", 7, name: "Susanoomon"));
        var r = Tamer("B85-96R", color: CardColor.Red); var r2 = Tamer("B85-96R2", color: CardColor.Red); var y = Tamer("B85-96Y", color: CardColor.Yellow);
        state.Player.Tamers.AddRange(new[] { r, r2, y });
        var underR = Digimon("B85-96U", 3);
        state.Player.PlaceUnderTamer85(r, underR);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card, includeInherited: false);
        Assert.True(state.MemoryGauge == 2, state.MemoryGauge + " " + L(state));   // 레드 · 옐로 2명 (레드 2명은 색이 같다)
        Assert.Contains(underR, state.Player.Trash);   // 놓인 테이머 아래 카드는 파기 (4-7-8)
        Assert.Equal(2, su.EvolutionSources.Count(x => x.Type == CardType.Tamer));
        Assert.True(SentenceParser.ColorsDistinct85(new[] { Card("x1", type: CardType.Tamer, color: CardColor.Red), new CardData { Type = CardType.Tamer, Color = CardColor.Red, SecondaryColor = CardColor.Blue } }));
    }

    [Fact]
    public void BT19_090_UnsuspendPairCost_AndFromUnderYourTamer()
    {
        var (state, engine) = NewMainPhase();
        var card = Card("BT19-090", "[Main] Activate 1 of the following effects:\n・You may play 1 Digimon card with the [Xros Heart] trait and 4000 DP or less from under your Tamer without paying the cost.\n・By unsuspending 1 of your [Shoutmon EX6] and 1 of your [ShootingStarmon], attack a player with 1 of your Digimon.");
        AssertFull(card);
        var t = Tamer("B85-90T");
        state.Player.Tamers.Add(t);
        var x = Digimon("B85-90X", 3, traits: "Xros Heart");
        state.Player.PlaceUnderTamer85(t, x);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, card, includeInherited: false);
        Assert.True(state.Player.BattleArea.Any(d => d.TopCard == x), L(state));
    }

    [Fact]
    public void BT10_093_PlayCost_PlacesPurpleCardsFromUnderTamers()
    {
        var (state, engine) = NewMainPhase(memory: 5);
        var t = Tamer("BT10-093", "[All Turns] [Once Per Turn] When a purple card is placed under this Tamer, ＜Draw 1＞ (Draw 1 card from your deck.) and memory +1.\n[Your Turn] [Once Per Turn] When you would play 1 level 4 or higher Digimon card with [Bagra Army] trait, by placing up to 3 purple Digimon cards from under your Tamers in the digivolution cards of the Digimon card played, reduce the memory cost of that Digimon by 2 for each card placed.", CardColor.Purple);
        var other = Tamer("B85-93O");
        state.Player.Tamers.Add(t);
        state.Player.Tamers.Add(other);
        state.Player.PlaceUnderTamer85(t, Digimon("B85-93P1", 3, color: CardColor.Purple));
        state.Player.PlaceUnderTamer85(other, Digimon("B85-93P2", 3, color: CardColor.Purple));   // 여러 테이머 아래에서 (Q2024)
        var bagra = Digimon("B85-93BAGRA", 5, playCost: 6, color: CardColor.Purple, traits: "Bagra Army");
        state.Player.Hand.Add(bagra);
        Assert.True(engine.PlayCard(bagra).CanExecute);
        var d = Assert.Single(state.Player.BattleArea);
        Assert.True(d.EvolutionSources.Count == 2, L(state));
        Assert.Equal(3, state.MemoryGauge);   // 6 - 4 = 2 지불
        Assert.Empty(state.Player.UnderTamers);
    }

    [Fact]
    public void BT25_049_OptionUseCostReduction_TrashesFaceDownUnderTamer()
    {
        var (state, engine) = NewMainPhase(memory: 5);
        var card = Digimon("BT25-049", 4, "[Your Turn][Once Per Turn] When you would use an Option card with the [Glowing Dawn] trait, by trashing the bottom face-down card under any of your Tamers, reduce the cost by 3.", color: CardColor.Green);
        AssertFull(card);
        Put(state.Player, card);
        var t = Tamer("B85-49T", color: CardColor.Green);
        state.Player.Tamers.Add(t);
        state.Player.AddUnderTamerFaceDown(t, Digimon("B85-49FD", 3));
        var opt = Card("B85-49OPT", "[Main] ＜Draw 1＞ (Draw 1 card from your deck.)", playCost: 4, color: CardColor.Green, traits: "Glowing Dawn");
        state.Player.Hand.Add(opt);
        Assert.True(engine.PlayCard(opt).CanExecute);
        Assert.Equal(4, state.MemoryGauge);   // 4 - 3 = 1 지불
        Assert.Equal(0, state.Player.FaceDownUnderTamerCount);
        Assert.Contains("사용할 때", KoreanEffectRenderer.RenderClause(card.EffectText));
    }

    [Fact]
    public void EX10_056_Event_OpponentEffectPlacesCardUnderTheirTamer()
    {
        var (state, engine) = NewMainPhase();
        var bagramon = Digimon("EX10-056", 7, "[All Turns] [Once Per Turn] When any of your opponent's Digimon or Tamers digivolve or effects place cards under them, by trashing any 2 of this Digimon's digivolution cards, trash your opponent's top security card.", color: CardColor.Purple);
        AssertFull(bagramon);
        Put(state.Player, bagramon, Digimon("B85-56S1", 5), Digimon("B85-56S2", 4));
        var t = Tamer("B85-56T");
        state.DummyOpponent.Tamers.Add(t);
        state.DummyOpponent.Trash.Add(Digimon("B85-56SAVE", 3, "[On Deletion] ＜Save＞"));
        var placer = Card("B85-56PLACER", "[Main] Place 1 Digimon card from your trash under 1 of your Tamers.");
        engine.PlayerChoices = new PickTamer("-");
        engine.RunEffects(EffectTrigger.Main, state.DummyOpponent, null, placer, includeInherited: false);   // 상대의 효과로 상대의 테이머 아래에
        Assert.True(state.DummyOpponent.SecurityStack.Count == 4, L(state));
    }

    [Fact]
    public void BT11_087_AddThenPlaceUnderOneTamer_Q1()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("BT11-087", 6, "[On Play] Trash the top 4 cards of your deck. Then, add up to 2 cards with [Bagra Army] in one of their traits from your trash to your hand, and place up to 2 Digimon cards with the [Bagra Army] trait from your trash under 1 of your Tamers.", color: CardColor.Purple);
        AssertFull(card);
        var me = Put(state.Player, card);
        var t = Tamer("B85-87T");
        state.Player.Tamers.Add(t);
        for (int i = 0; i < 4; i++) state.Player.Trash.Add(Digimon("B85-87B" + i, 3 + i % 2, traits: "Bagra Army"));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, card);
        Assert.Equal(2, state.Player.Hand.Count(x => x.CardId.StartsWith("B85-87B")));
        Assert.Equal(2, state.Player.UnderCountOf(t));
    }

    [Fact]
    public void BT12_109_And_BT11_109_And_EX10_039_And_BT7_087_Parse_AndKorean()
    {
        var cards = new[]
        {
            Card("BT12-109", "[Main] Digivolve 1 of your Digimon into a Digimon card with ＜Save＞ in its text placed under one of your Tamers for its digivolution cost.", color: CardColor.Yellow),
            Card("BT11-109", "[Main] You may place up to 3 Digimon cards with the [Bagra Army] trait from your trash under 1 of your Digimon as its bottom digivolution cards or under 1 of your Tamers. Then, if you have a Digimon or Tamer with the [Bagra Army] trait in play, place 1 of your opponent's Digimon under 1 of your opponent's other Digimon as its bottom digivolution card.", color: CardColor.Purple),
            Digimon("EX10-039", 3, "[Start of Your Main Phase] You may place 1 Digimon card with the [Bagra Army] trait from your hand or trash as any of your [Bagra Army] trait Digimon's bottom digivolution cards or under any of your [Bagra Army] trait Tamers.", color: CardColor.Purple),
            Tamer("BT7-087", SentenceParser.Bt7087Api85, CardColor.Blue),
            Tamer("BT25-090", SentenceParser.Bt25090Api85, CardColor.Green),
        };
        foreach (var c in cards)
        {
            AssertFull(c);
            Assert.NotNull(KoreanEffectRenderer.RenderClause(c.EffectText.Split('\n')[0]));
        }
    }

    [Fact]
    public void EX10_039_PlacesUnderBagraTamerOnly()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("EX10-039", 3, "[Start of Your Main Phase] You may place 1 Digimon card with the [Bagra Army] trait from your hand or trash as any of your [Bagra Army] trait Digimon's bottom digivolution cards or under any of your [Bagra Army] trait Tamers.", color: CardColor.Purple);
        var me = Put(state.Player, card);   // 이 카드는 특징이 없다 — 디지몬 후보 없음
        var plain = Tamer("B85-39P");
        var bagra = Tamer("B85-39B", traits: "Bagra Army");
        state.Player.Tamers.Add(plain);
        state.Player.Tamers.Add(bagra);
        var x = Digimon("B85-39X", 3, traits: "Bagra Army");
        state.Player.Trash.Add(x);
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, me, card);
        Assert.Same(bagra, state.Player.HostOfUnderTamer(x));
    }
}
