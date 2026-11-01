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
/// v0.66.0 미지원 문장 40차 묶음: EX2-039 이 카드를 파기한 효과 · BT15-060 · BT17-068 공개 중의 명칭 · Lv. · BT18-078 원래 색 바꾸기 ·
/// 덱 위 공개 후 둘 중 하나 (BT21-087 · EX2-072 · BT25-078) · 진화원에서 다른 디지몬으로서 등장 (BT7-097 · BT3-030) · AI 패 → 진화원 비용 · 그 밖의 문장
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch66Tests
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
            state.Player.MainDeck.Add(Digimon("B66-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B66-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < security; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B66-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B66-OSEC" + i, 3, color: CardColor.White, dp: 1000));
        }
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, params CardData[] sources)
    {
        var d = new CardInstance(card, 1) { IsOwnersTurn = true, IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static string Uid(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static readonly Random Rng = new();
    /// <summary>카드 번호 꼴 ("TQ66-123456") — 효과 문장 안의 "[번호 이름]"과 맞춘다</summary>
    private static string CardNo() => "TQ66-" + Rng.Next(100000, 999999);

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    private sealed class Answers : IChoiceProvider
    {
        public bool Yes = true;
        public int? Option;
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => Yes;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => Option ?? recommended;
    }

    // =====================================================================
    // 1. EX2-039 — "if it wasn't trashed by [EX2-039 Impmon]'s effect"
    // =====================================================================

    private static CardData Impmon(string no) =>
        Digimon(no, 3, $"When this card is trashed from your deck, if it wasn't trashed by [{no} Impmon]'s effect, you may trash up to 3 cards from the top of your deck.", name: "Impmon", color: CardColor.Purple);

    [Fact]
    public void EX2_039_TrashedByOtherEffect_TrashesUpTo3()
    {
        string no = CardNo();
        var imp = Impmon(no);
        AllSupported(imp);
        Assert.Equal($"이 카드가 「{no} 임프몬」의 효과 이외로 덱에서 파기되었을 때, 자신의 덱 위에서부터 3장까지 파기할 수 있다.", Ko(imp.EffectText));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        state.Player.MainDeck.Insert(0, imp);
        int deck = state.Player.MainDeck.Count;
        var other = Digimon(Uid("B66-MILL-"), 4, "[On Play] Trash the top card of your deck.");
        var inst = Put(state.Player, other);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, inst, other, includeInherited: false);
        Assert.Contains(imp, state.Player.Trash);
        Assert.True(deck - 4 == state.Player.MainDeck.Count, Logs(state));   // 1장 + 3장까지
    }

    [Fact]
    public void EX2_039_TrashedByEX2_039Effect_DoesNotChain()
    {
        string no = CardNo();
        var imp = Impmon(no);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var imp2 = Impmon(no);   // 같은 번호의 다른 카드 객체
        // 첫 임프몬이 덱에서 파기되어 3장 파기 — 그중 둘째 임프몬은 "EX2-039의 효과로" 파기되었다 → 더 파기하지 않는다
        state.Player.MainDeck.Insert(0, imp);
        state.Player.MainDeck.Insert(1, imp2);
        int deck = state.Player.MainDeck.Count;
        engine.Mill(state.Player, 1, "시험");   // 효과 밖 (룰 · 다른 효과와 같다)
        Assert.Contains(imp2, state.Player.Trash);
        Assert.True(deck - 4 == state.Player.MainDeck.Count, Logs(state));   // 1 + 3 (둘째 임프몬의 3장은 없다)
    }

    [Fact]
    public void EX2_039_TrashedWhileItsOwnCardEffectResolving_DoesNotTrigger()
    {
        string no = CardNo();
        var imp = Impmon(no);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        state.Player.MainDeck.Insert(0, imp);
        int deck = state.Player.MainDeck.Count;
        var src = Impmon(no);
        engine.CurrentEffectCard = src;   // 같은 번호의 카드에 적힌 효과가 처리되는 중
        try { engine.Mill(state.Player, 1, "시험"); }
        finally { engine.CurrentEffectCard = null; }
        Assert.Equal(deck - 1, state.Player.MainDeck.Count);
    }

    // =====================================================================
    // 2. BT15-060 · BT17-068 — 덱에서 공개되어 있는 동안 명칭 · Lv.
    // =====================================================================

    [Fact]
    public void BT15_060_RevealedNameAlsoOmnimon_CanBePicked_ButNotElsewhere()
    {
        const string alias = "While this card is revealed from the deck, its name is also treated as [Omnimon].";
        var omeka = Digimon(Uid("B66-OMEKA-"), 4, alias, name: "Omekamon", color: CardColor.Black);
        AllSupported(omeka);
        Assert.Equal("이 카드가 덱에서 오픈되어 있는 동안, 이 카드의 명칭은 「오메가몬」으로도 취급한다.", Ko(alias));
        var (state, engine) = NewMainPhase();
        state.Player.MainDeck.Insert(0, omeka);
        var seeker = Digimon(Uid("B66-SEEK-"), 3, "[On Play] Reveal the top 3 cards of your deck. Add 1 Digimon card with [Omnimon] in its name among them to the hand. Return the rest to the bottom of the deck.");
        var inst = Put(state.Player, seeker);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, inst, seeker, includeInherited: false);
        Assert.Contains(omeka, state.Player.Hand);
        Assert.Equal("Omekamon", omeka.Name);   // 카드 자체는 그대로
        Assert.False(TargetSpec.NameHas(omeka, "Omnimon"));   // 패에서는 「오메가몬」이 아니다
    }

    [Fact]
    public void BT17_068_RevealedAlsoLevel6_CanBePickedAsLevel6()
    {
        const string alias = "While this card is revealed from your deck, this card is also treated as level 6.";
        var meph = Digimon(Uid("B66-MEPH-"), 5, alias, name: "Mephismon", color: CardColor.Purple);
        AllSupported(meph);
        Assert.Equal("이 카드가 덱에서 오픈되어 있는 동안, 이 카드는 Lv.6으로도 취급한다.", Ko(alias));
        foreach (var (pick, taken) in new[] { ("2 level 6 or higher cards", true), ("1 level 5 Digimon card", true), ("1 level 4 Digimon card", false) })
        {
            var (state, engine) = NewMainPhase();
            state.Player.MainDeck.Insert(0, meph);
            var seeker = Digimon(Uid("B66-SEEK-"), 3, $"[On Play] Reveal the top 4 cards of your deck. Add {pick} among them to the hand. Return the rest to the bottom of the deck.");
            var inst = Put(state.Player, seeker);
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, inst, seeker, includeInherited: false);
            Assert.True(taken == state.Player.Hand.Contains(meph), pick);   // Lv.5이면서 Lv.6
        }
    }

    // =====================================================================
    // 3. BT18-078 — 상대의 디지몬 / 테이머의 원래 색을 화이트 이외의 1색으로 (상대의 턴 종료까지)
    // =====================================================================

    private const string Bt18078 = "[On Play] [When Digivolving] Until the end of your opponent's turn, change 1 of their Digimon or Tamers into a color other than white.";

    [Fact]
    public void BT18_078_ChangesDigimonBaseColor_UntilOpponentsTurnEnds()
    {
        var dusk = Digimon(Uid("B66-DUSK-"), 4, Bt18078, name: "Duskmon", color: CardColor.Purple);
        AllSupported(dusk);
        Assert.Equal("【등장 시】 【진화 시】 상대의 턴 종료까지, 상대의 디지몬 또는 테이머 1마리(명)의 원래 색을 화이트 이외의 1색으로 바꾼다.", Ko(Bt18078));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers { Option = null };
        var foe = Put(state.DummyOpponent, Digimon("B66-FOE", 5, color: CardColor.Red));
        var me = Put(state.Player, dusk);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, dusk, includeInherited: false);
        Assert.NotNull(foe.BaseColor66);
        Assert.DoesNotContain(CardColor.Red, foe.Colors);   // 원래 색이 바뀐다 (빨강이 아니게 — 추천은 지금 색이 아닌 색)
        Assert.Single(foe.Colors);
        Assert.NotEqual(CardColor.White, foe.Colors[0]);
        // 상대의 턴 종료까지
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.NotNull(foe.BaseColor66);
        state.TurnSequence += 1;
        engine.RecalculateFieldBuffs();
        Assert.Null(foe.BaseColor66);
        Assert.Equal(new[] { CardColor.Red }, foe.Colors);
    }

    [Fact]
    public void BT18_078_ChangesTamerColor_SeenByColorConditions()
    {
        var dusk = Digimon(Uid("B66-DUSK-"), 4, Bt18078, name: "Duskmon", color: CardColor.Purple);
        var (state, engine) = NewMainPhase();
        var tamer = Card(Uid("B66-OTAM-"), "", CardType.Tamer, color: CardColor.Red);
        state.DummyOpponent.Tamers.Add(tamer);
        // 테이머만 있으면 테이머 · 색은 초록을 고른다
        engine.PlayerChoices = new Answers { Option = 3 };   // 0 = 테이머 (디지몬이 없다) → 색 목록의 3 = 그린
        var me = Put(state.Player, dusk);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, dusk, includeInherited: false);
        Assert.Equal(new[] { CardColor.Green }, state.DummyOpponent.TamerColorsOf66(tamer));
        Assert.Equal(new[] { CardColor.Red }, tamer.AllColors);   // 카드 자체는 그대로
        // 상대의 "빨간 테이머가 있다면" 조건은 만족하지 않는다
        var cond = Digimon(Uid("B66-COND-"), 4, "[On Play] If you have a red Tamer, gain 2 memory.");
        var ci = Put(state.DummyOpponent, cond);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, ci, cond, includeInherited: false);
        Assert.Equal(mem, state.MemoryGauge);
        // 기간이 끝나면 원래 색
        state.TurnSequence += 2;
        engine.RecalculateFieldBuffs();
        Assert.Equal(new[] { CardColor.Red }, state.DummyOpponent.TamerColorsOf66(tamer));
    }

    // =====================================================================
    // 4. 덱 위 공개 후 둘 중 하나 (BT21-087 · EX2-072 · BT25-078)
    // =====================================================================

    private const string Bt21087 = "[On Play] Reveal the top 3 cards of your deck. Among them, play 1 [Vemmon] without paying the cost or add 1 card with [Vemmon] in its text to the hand. Trash the rest.";

    [Fact]
    public void BT21_087_PlayVemmon_OrAddVemmonTextCard_RestTrashed()
    {
        var zenith = Card(Uid("B66-ZEN-"), Bt21087, CardType.Tamer, name: "Zenith", color: CardColor.Black);
        AllSupported(zenith);
        Assert.Equal("【등장 시】 자신의 덱 위에서부터 3장 오픈한다. 그중 「벰몬」 1장을 코스트를 지불하지 않고 등장시키거나, 「벰몬」이 기술되어 있는 카드 1장을 패에 추가한다. 나머지는 파기한다.", Ko(Bt21087));
        foreach (int choice in new[] { 0, 1 })
        {
            var (state, engine) = NewMainPhase();
            engine.PlayerChoices = new Answers { Option = choice };
            var vem = Digimon(Uid("B66-VEM-"), 3, name: "Vemmon", color: CardColor.Black);
            var txt = Card(Uid("B66-VTXT-"), "[Main] Play 1 [Vemmon] from your trash.", CardType.Option);
            var junk = Digimon(Uid("B66-JUNK-"), 4);
            state.Player.MainDeck.InsertRange(0, new[] { vem, txt, junk });
            int trash = state.Player.Trash.Count;
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, null, zenith, includeInherited: false);
            if (choice == 0)
            {
                Assert.True(state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, vem)), Logs(state));
                Assert.Equal(trash + 2, state.Player.Trash.Count);
            }
            else
            {
                Assert.Contains(txt, state.Player.Hand);   // 「벰몬」이 기술되어 있는 카드 (벰몬 자신도 고를 수 있지만 추천은 등장으로도 쓸 수 없는 카드)
                Assert.Equal(trash + 2, state.Player.Trash.Count);
            }
        }
    }

    private const string Ex2072 = "[Main] Reveal the top 5 cards of your deck. You may digivolve 1 of your Digimon into 1 non-white Digimon card among them without paying its memory cost. If you don't, add 1 Digimon card among them to your hand. Place the remaining cards at the bottom of your deck in any order.";

    [Fact]
    public void EX2_072_DigivolveOrElseAdd()
    {
        var blue = Card(Uid("B66-BLUE-"), Ex2072, color: CardColor.White);
        AllSupported(blue);
        Assert.Equal("【메인】 자신의 덱 위에서부터 5장 오픈한다. 자신의 디지몬 1마리는 그중 화이트 이외의 디지몬 카드 1장으로 코스트를 지불하지 않고 진화할 수 있다. 그렇게 하지 않았을 때, 그중 디지몬 카드 1장을 패에 추가한다. 나머지는 원하는 순서대로 덱 아래로 되돌린다.", Ko(Ex2072));
        // 진화
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers { Option = 0 };
        var baseD = Put(state.Player, Digimon("B66-BASE", 3, color: CardColor.Red));
        var evo = Digimon(Uid("B66-EVO-"), 4, color: CardColor.Red);
        state.Player.MainDeck.Insert(0, evo);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, blue, includeInherited: false);
        Assert.Same(evo, baseD.TopCard);
        // 진화하지 않으면 패에
        var (s2, e2) = NewMainPhase();
        e2.PlayerChoices = new Answers { Option = 1 };
        Put(s2.Player, Digimon("B66-BASE", 3, color: CardColor.Red));
        var evo2 = Digimon(Uid("B66-EVO-"), 4, color: CardColor.Red);
        s2.Player.MainDeck.Insert(0, evo2);
        e2.RunEffects(EffectTrigger.Main, s2.Player, null, blue, includeInherited: false);
        Assert.Single(s2.Player.Hand);
        // 진화할 수 있는 카드가 없으면 패에 (화이트는 진화할 수 없다)
        var (s3, e3) = NewMainPhase();
        e3.PlayerChoices = new Answers();
        Put(s3.Player, Digimon("B66-BASE", 3, color: CardColor.White));
        e3.RunEffects(EffectTrigger.Main, s3.Player, null, blue, includeInherited: false);
        Assert.Single(s3.Player.Hand);
        Assert.Equal(10, s3.Player.MainDeck.Count + s3.Player.Hand.Count);   // 나머지는 덱 아래
    }

    private const string Bt25078 = "[When Moving] [On Play] Reveal the top 3 cards of your deck. Among them, add 1 card with [Three Musketeers] in its text to the hand, or you may place 1 [Three Musketeers] trait card as this Digimon's bottom digivolution card. Return the rest to the bottom of the deck.";

    [Fact]
    public void BT25_078_AddOrPlaceUnderThisDigimon()
    {
        var mon = Digimon(Uid("B66-MUSK-"), 4, Bt25078, name: "Musketeer");
        AllSupported(mon);
        foreach (int choice in new[] { 0, 1 })
        {
            var (state, engine) = NewMainPhase();
            engine.PlayerChoices = new Answers { Option = choice };
            var me = Put(state.Player, mon);
            var both = Digimon(Uid("B66-3M-"), 3, "[On Play] Search for [Three Musketeers].", traits: "Three Musketeers");
            state.Player.MainDeck.Insert(0, both);
            engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, mon, includeInherited: false);
            if (choice == 0) Assert.Contains(both, state.Player.Hand);
            else Assert.Contains(both, me.EvolutionSources);
        }
    }

    // =====================================================================
    // 5. 진화원에서 다른 디지몬으로서 등장 (BT7-097 · BT3-030)
    // =====================================================================

    [Fact]
    public void BT7_097_PlayUpTo2SourcesOfOneDigimon()
    {
        const string text = "[Main] Choose up to 2 Digimon cards in the digivolution cards of one of your Digimon and play them as other Digimon without paying their memory costs.";
        var tidal = Card(Uid("B66-TIDAL-"), text);
        AllSupported(tidal);
        Assert.Equal("【메인】 자신의 디지몬 1마리의 진화원에서 디지몬 카드 2장까지를 코스트를 지불하지 않고 등장시킨다.", Ko(text));
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var a = Digimon(Uid("B66-SA-"), 3); var b = Digimon(Uid("B66-SB-"), 4);
        var host = Put(state.Player, Digimon("B66-HOST", 5), false, b, a);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, tidal, includeInherited: false);
        Assert.Equal(3, state.Player.BattleArea.Count);
        Assert.Empty(host.EvolutionSources);
    }

    [Fact]
    public void BT3_030_PlayLevel4OrLowerSource()
    {
        const string text = "[When Digivolving] You may play 1 level 4 or lower digivolution card of 1 of your Digimon cards as another Digimon without paying its memory cost.";
        var duft = Digimon(Uid("B66-DUFT-"), 6, text, name: "Dukemon");
        AllSupported(duft);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var lv5 = Digimon(Uid("B66-L5-"), 5); var lv4 = Digimon(Uid("B66-L4-"), 4);
        var me = Put(state.Player, duft, false, lv5, lv4);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, duft, includeInherited: false);
        Assert.True(state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, lv4)), Logs(state));
        Assert.Contains(lv5, me.EvolutionSources);
    }

    // =====================================================================
    // 6. 그 밖의 문장 (BT9-104 · BT24-043 · BT25-074 · P-097 · EX2-049 · EX6-025 · P-021 · P-156 · BT13-046)
    // =====================================================================

    [Fact]
    public void OtherSentences_AllSupported_WithKorean()
    {
        var texts = new[]
        {
            ("[Main] Reveal the top 3 cards of your deck. You may digivolve 1 of your Digimon into a Digimon card with the [X Antibody] trait without its memory cost. Trash the rest. Then, place 1 card with the [X Antibody] trait from your trash under 1 of your Digimon with the [X Antibody] trait as its bottom digivolution card.", CardType.Option),
            ("[On Play] Reveal the top 3 cards of your deck. Add 1 Digimon card with the [Shaman] trait or with [Beast], [Animal] or [Sovereign] in any of its traits (other than [Sea Animal]), in any of its traits and 1 card with the [TS] trait among them to the hand. Return the rest to the bottom of the deck.", CardType.Digimon),
            ("[When Digivolving] [When Attacking] [Once Per Turn] Reveal the top 3 cards of your deck. You may play 1 play cost 12 or lower [D-Brigade] or [ACCEL] trait Digimon card among them with the cost reduced by 5. Trash the rest.", CardType.Digimon),
            ("[On Play] By placing this card under 1 of your other Digimon in play as its bottom digivolution card, reveal the top 3 cards of your deck. Place those cards at either the top or bottom of your deck in any order. Then, if you have a Digimon with the [Legend-Arms] trait in play, gain 2 memory.", CardType.Digimon),
            ("[Main] You may suspend this Digimon to Reveal the top 5 cards of your deck. Place 1 [ADR-02 Searcher] among them under 1 of your [Mother D-Reaper]s as its bottom digivolution card. Place the remaining cards at the bottom of your deck in any order.", CardType.Digimon),
            ("[On Play] Reveal the top 4 cards of your deck. Add 1 of each [Gokuumon], [Sagomon], [Cho-Hakkaimon] and [Shakamon] among them to the hand. Return the rest to the bottom of the deck.", CardType.Digimon),
            ("[Main] If you have [Mimi Tachikawa] in play, you may play a [Palmon] from your hand without paying its memory cost to return 1 of your [Mimi Tachikawa] cards to its owner's hand.", CardType.Option),
            ("[Main] Choose 1 Tamer. You may play 1 Digimon card with the same color as the chosen Tamer with a play cost of 3 or less from your hand or trash without paying the cost.", CardType.Option),
            ("[On Play] [When Digivolving] If the total cards in both players' security stacks is 6 or less, gain 3 memory, and reveal 1 card from your hand. If you revealed a yellow card, place it on top of your security stack face down. If not, return it to your hand.", CardType.Digimon),
        };
        foreach (var (t, type) in texts)
        {
            var c = Card(Uid("B66-OTHER-"), t, type, level: type == CardType.Digimon ? 5 : 0);
            AllSupported(c);
            Assert.NotEqual("<null>", Ko(t));
        }
        Assert.Contains("등장 코스트를 -5 하여 등장시킬 수 있다. 나머지는 파기한다.", Ko(texts[2].Item1));
    }

    [Fact]
    public void BT25_074_PlaysWithReducedCost()
    {
        var c = Digimon(Uid("B66-DB-"), 5, "[When Attacking] Reveal the top 3 cards of your deck. You may play 1 play cost 12 or lower [D-Brigade] or [ACCEL] trait Digimon card among them with the cost reduced by 5. Trash the rest.");
        var (state, engine) = NewMainPhase(memory: 8);
        engine.PlayerChoices = new Answers();
        var me = Put(state.Player, c);
        var target = Digimon(Uid("B66-DBT-"), 6, playCost: 12, traits: "D-Brigade");
        state.Player.MainDeck.Insert(0, target);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, c, includeInherited: false);
        Assert.True(state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, target)), Logs(state));
        Assert.Equal(8 - 7, state.MemoryGauge);   // 12 - 5 = 7
        Assert.Equal(2, state.Player.Trash.Count);   // 나머지는 파기
    }

    [Fact]
    public void BT9_104_DigivolveAmongRevealed_ThenPlaceFromTrash()
    {
        var c = Card(Uid("B66-XEVO-"), "[Main] Reveal the top 3 cards of your deck. You may digivolve 1 of your Digimon into a Digimon card with the [X Antibody] trait without its memory cost. Trash the rest. Then, place 1 card with the [X Antibody] trait from your trash under 1 of your Digimon with the [X Antibody] trait as its bottom digivolution card.", color: CardColor.Black);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var baseD = Put(state.Player, Digimon("B66-XBASE", 4, color: CardColor.Black));
        var x = Digimon(Uid("B66-X-"), 5, color: CardColor.Black, traits: "X Antibody");
        var xjunk = Digimon(Uid("B66-XJ-"), 3, color: CardColor.Black, traits: "X Antibody");
        state.Player.MainDeck.InsertRange(0, new[] { x, xjunk });
        engine.RunEffects(EffectTrigger.Main, state.Player, null, c, includeInherited: false);
        Assert.Same(x, baseD.TopCard);
        Assert.Contains(xjunk, baseD.EvolutionSources);   // 파기된 X항체 카드를 진화원 아래에
    }

    [Fact]
    public void EX2_049_PlacesSearcherUnderMotherDReaperInBattleArea()
    {
        var jeri = Digimon(Uid("B66-JERI-"), 3, "[Main] You may suspend this Digimon to Reveal the top 5 cards of your deck. Place 1 [ADR-02 Searcher] among them under 1 of your [Mother D-Reaper]s as its bottom digivolution card. Place the remaining cards at the bottom of your deck in any order.", color: CardColor.Purple);
        AllSupported(jeri);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var mother = new CardInstance(Card(Uid("B66-MDR-"), "", CardType.Digitama, level: 2, name: "Mother D-Reaper", color: CardColor.Purple), 1);
        mother.IsOwnersTurn = true; state.Player.BattleArea.Add(mother);   // v0.76.0 배틀 에어리어의 「마더 디・리퍼」 (육성 에어리어의 것은 고르지 않는다 — 종합 룰 3-4-7-5)
        var me = Put(state.Player, jeri);
        var searcher = Digimon(Uid("B66-ADR-"), 3, name: "ADR-02 Searcher", color: CardColor.Purple);
        state.Player.MainDeck.Insert(2, searcher);
        engine.RunEffects(EffectTrigger.ActivateMain, state.Player, me, jeri, includeInherited: false);
        Assert.True(me.IsSuspended);
        Assert.Contains(searcher, mother.EvolutionSources);
        Assert.Equal(10, state.Player.MainDeck.Count);   // 나머지 4장은 덱 아래로
    }

    [Fact]
    public void P_021_PlayPalmon_ThenReturnMimi()
    {
        var c = Card(Uid("B66-P021-"), "[Main] If you have [Mimi Tachikawa] in play, you may play a [Palmon] from your hand without paying its memory cost to return 1 of your [Mimi Tachikawa] cards to its owner's hand.", color: CardColor.Green);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        var mimi = Card(Uid("B66-MIMI-"), "", CardType.Tamer, name: "Mimi Tachikawa", color: CardColor.Green);
        state.Player.Tamers.Add(mimi);
        var palmon = Digimon(Uid("B66-PAL-"), 3, name: "Palmon", color: CardColor.Green);
        state.Player.Hand.Add(palmon);
        int mem = state.MemoryGauge;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, c, includeInherited: false);
        Assert.True(state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, palmon)), Logs(state));
        Assert.Contains(mimi, state.Player.Hand);
        Assert.Equal(mem, state.MemoryGauge);
        // 팔몬이 없으면 이미나도 그대로 (등장시키는 것으로 되돌린다)
        var (s2, e2) = NewMainPhase();
        e2.PlayerChoices = new Answers();
        s2.Player.Tamers.Add(mimi);
        e2.RunEffects(EffectTrigger.Main, s2.Player, null, c, includeInherited: false);
        Assert.Contains(mimi, s2.Player.Tamers);
    }

    [Fact]
    public void P_156_ChooseTamer_PlaySameColorFromHandOrTrash()
    {
        var c = Card(Uid("B66-P156-"), "[Main] Choose 1 Tamer. You may play 1 Digimon card with the same color as the chosen Tamer with a play cost of 3 or less from your hand or trash without paying the cost.");
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new Answers();
        state.DummyOpponent.Tamers.Add(Card(Uid("B66-OT-"), "", CardType.Tamer, color: CardColor.Yellow));   // 상대의 테이머도 고를 수 있다
        var yellow = Digimon(Uid("B66-Y-"), 3, color: CardColor.Yellow, playCost: 3);
        var red = Digimon(Uid("B66-R-"), 3, color: CardColor.Red, playCost: 3);
        state.Player.Trash.Add(yellow);
        state.Player.Hand.Add(red);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, c, includeInherited: false);
        Assert.True(state.Player.BattleArea.Any(d => ReferenceEquals(d.TopCard, yellow)), Logs(state));
        Assert.Contains(red, state.Player.Hand);
    }

    [Fact]
    public void BT13_046_RevealYellowFromHand_ToSecurityTop()
    {
        var c = Digimon(Uid("B66-1346-"), 5, "[On Play] If the total cards in both players' security stacks is 6 or less, gain 3 memory, and reveal 1 card from your hand. If you revealed a yellow card, place it on top of your security stack face down. If not, return it to your hand.", color: CardColor.Yellow);
        var (state, engine) = NewMainPhase(memory: 2, security: 3);
        engine.PlayerChoices = new Answers();
        var y = Digimon(Uid("B66-YH-"), 3, color: CardColor.Yellow);
        var b = Digimon(Uid("B66-BH-"), 3, color: CardColor.Blue);
        state.Player.Hand.AddRange(new[] { b, y });
        var me = Put(state.Player, c);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, c, includeInherited: false);
        Assert.Equal(5, state.MemoryGauge);
        Assert.Same(y, state.Player.SecurityStack[0]);
        Assert.Contains(b, state.Player.Hand);
        // 합계 7장 이상이면 아무것도 없다
        var (s2, e2) = NewMainPhase(memory: 2, security: 4);
        e2.PlayerChoices = new Answers();
        s2.Player.Hand.Add(y);
        var me2 = Put(s2.Player, c);
        e2.RunEffects(EffectTrigger.OnPlay, s2.Player, me2, c, includeInherited: false);
        Assert.Equal(2, s2.MemoryGauge);
        Assert.Contains(y, s2.Player.Hand);
    }

    // =====================================================================
    // 7. AI — 패의 카드를 진화원 아래에 놓는 비용은 뒤의 효과의 값으로
    // =====================================================================

    [Fact]
    public void AiHandSourceCost_ValueOfRest()
    {
        var removal = SentenceParser.RestValue66("delete 1 of your opponent's Digimon with 7000 DP or less.");
        var draw = SentenceParser.RestValue66("＜Draw 1＞ (Draw 1 card from your deck.)");
        var buff = SentenceParser.RestValue66("this Digimon gets +2000 DP for the turn.");
        var protect = SentenceParser.RestValue66("prevent effects from deleting it or reducing its DP until the end of your opponent's next turn.");
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B66-ME", 4));
        var c = new EffectContext(engine, state.Player, me, null, null) { Card = me.TopCard };
        Assert.False(removal(c));   // 상대의 디지몬이 없다
        Put(state.DummyOpponent, Digimon("B66-FOE", 4));
        Assert.True(removal(c));
        Assert.True(draw(c));
        Assert.False(buff(c));
        Assert.False(protect(c));   // 1장짜리 Lv.4
        me.AddBottomSource(Digimon("B66-S1", 3)); me.AddBottomSource(Digimon("B66-S2", 2));
        Assert.True(protect(c));   // 3장 쌓인 디지몬
        var lv6 = Put(state.Player, Digimon("B66-L6", 6));
        Assert.True(protect(new EffectContext(engine, state.Player, lv6, null, null) { Card = lv6.TopCard }));   // Lv.6은 1장짜리도
    }

    [Fact]
    public void AiHandSourceCost_SmallHand_PaysForRemoval_NotForBuff()
    {
        foreach (var (rest, pays) in new[] { ("delete 1 of your opponent's Digimon with 7000 DP or less.", true), ("this Digimon gets +2000 DP for the turn.", false) })
        {
            var card = Digimon(Uid("B66-GAM-"), 6, $"[On Play] By placing 1 Digimon card with [Gammamon] in its text from your hand as this Digimon's bottom digivolution card, {rest}", name: "GulusGammamon");
            AllSupported(card);
            var (state, engine) = NewMainPhase();   // AI 쪽에서
            var me = Put(state.DummyOpponent, card);
            Put(state.Player, Digimon("B66-PFOE", 4, dp: 3000));
            var g = Digimon(Uid("B66-GM-"), 3, name: "Gammamon");
            state.DummyOpponent.Hand.Add(g);   // 패 1장
            engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, me, card, includeInherited: false);
            Assert.True(pays == me.EvolutionSources.Contains(g), rest + " | " + Logs(state));
        }
    }
}
