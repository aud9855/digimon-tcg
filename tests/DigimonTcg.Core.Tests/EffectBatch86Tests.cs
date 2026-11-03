using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.86.0 트래시 목록 · 트래시에서 쓰는 효과: GoldfishEngine.DescribeTrash (맨 위부터 · [트래시]【메인】 / [트래시] 시점 표시) ·
/// TrashMainStatus (자신의 메인 페이즈 → 트래시에 있음 → 이번 턴에 쓴 횟수 → 발동 조건 "If ~," → 비용 "By ~," 를 처리하지 않고 미리 본다) ·
/// ActivateTrashEffect가 그 이유를 돌려준다 · AI도 조건 · 비용이 맞지 않는 [트래시]【메인】은 쓰지 않는다.
/// 실제 카드 번호와 영어 API 문구 (EX7-060 · BT24-076 · BT26-079 · BT20-096 · EX10-054 · EX10-011 · LM-006 · BT12-110 · EX4-011)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch86Tests
{
    private const string Ex7060 = "[Trash] [Main] If you have 4 or fewer cards in your hand, you may play this card from your trash with the play cost reduced by 4.\r\n＜Blocker＞ (This Digimon can block in the blocker timing.).\r\n[On Deletion] You may play 1 level 5 or lower Digimon card with the [Dark Dragon] or [Evil Dragon]\u00A0trait trait from your trash without paying the cost.";
    private const string Bt24076 = "[Trash] [Main] If you have 4 or fewer cards in your hand, play this card with the play cost reduced by 2.\r\n[On Play] [When Digivolving] Delete 1 of your opponent's level 4 or lower Digimon.";
    private const string Bt24076Inh = "[On Deletion] You may play 1 level 4 or lower [Dark Dragon] or [Evil Dragon] Digimon card from your trash without paying the cost.";
    private const string Bt26079 = "[Trash] [Main] If your hand has 5 or fewer cards, play this card with the cost reduced by 4.\r\n＜Security A. +1＞ (This Digimon checks 1 additional security card.)\r\n＜Decode ([Plutomon])＞ (When this Digimon would leave the battle area other than in battle, you may play 1 specified Digimon card from its digivolution cards without paying the cost.)\r\n＜Retaliation＞ (When only this Digimon is deleted in battle, delete the Digimon it battled.) \r\n[On Play] [When Digivolving] [When Attacking] By trashing 1 card in your hand, delete 1 of your opponent's level 6 or lower Digimon.\r\n[All Turns] [Once Per Turn] When any of your opponent's Digimon are played or digivolve, both players trash cards in their hand so that they have 4 left.";
    private const string Bt20096 = "[Trash] [Main] If you have 4 or fewer cards in your hand, by paying 6 cost, return this card to the bottom of the deck and delete 1 of your opponent's unsuspended Digimon.\r\n[Main] Trash 1 card in your hand. Then, delete 1 of your opponent's level 4 or lower Digimon.";
    private const string Bt20096Sec = "Security Effect [Security] Delete 1 of your opponent's level 6 or lower Digimon.";
    private const string Ex10054 = "[Trash] [Main] By deleting 1 of your level 5 Digimon with [Myotismon]\u00A0in its text, play this card with the play cost reduced by 7.\r\n[On Play] [When Digivolving] You may suspend 2 of your opponent's Digimon or Tamers. Then, 2 of their Digimon or Tamers can't unsuspend until their turn ends.\r\n[On Deletion] Delete 1 of your opponent's suspended Digimon.";
    private const string Ex10011 = "[Trash] [Main] By deleting 2 of your level 5 or higher Digimon with [Myotismon]\u00A0in their texts, play this card with the play cost reduced by 11.\r\n[On Play] [When Digivolving] [When Attacking] [Once Per Turn] Delete 2 other unsuspended Digimon.\r\n[All Turns] [Once Per Turn] When any of your other Digimon or Tamers are deleted, trash your opponent's top security card and return 1 of their Digimon with the lowest DP to the bottom of the deck.";
    private const string Lm006 = "[Trash] [Main] By returning 1 of your Tamers to the bottom of the deck, play this card with the play cost reduced by the play cost of the returned Tamer.\r\n[On Play] [When Digivolving] Trash the bottom 3 digivolution cards of 1 of your opponent's Digimon. Then, until the end of their turn, none of their Digimon with no digivolution cards can attack.";
    private const string Bt12110 = "[Trash] [Your Turn] When one of your Digimon digivolves into [Beelzemon (X Antibody)], by returning this card to the bottom of your deck, delete 1 of your opponent's Digimon with the lowest level.\r\n[Main] Delete 1 of your opponent's Digimon with the lowest level.";
    private const string Ex4011 = "[Trash] [End of Your Turn] By Deleting 1 of your Digimon with a digivolution card and [Gallantmon]\u00A0in its name, you may play this card without paying its cost.\r\n[On Play] Delete 1 of your opponent's Digimon with 7000 DP or less. For every 10 total cards in both player's trashes, add 2000 to the maximum DP you can choose with DP-based deletion effects.";

    private static CardData Card(string id, string name, CardType type, int level, int playCost, int dp, string main, string inherited = "",
        CardColor color = CardColor.Purple, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name, Type = type, Color = color, Level = level, DP = dp, PlayCost = playCost, EvoCost = 3,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Nidhoggmon() => Card("EX7-060", "Nidhoggmon", CardType.Digimon, 6, 11, 11000, Ex7060, traits: new[] { "Dark Dragon", "Virus" });
    private static CardData WarGrowlmon() => Card("BT24-076", "WarGrowlmon", CardType.Digimon, 5, 7, 7000, Bt24076, Bt24076Inh, traits: new[] { "Cyborg", "Virus" });
    private static CardData ZombiePlutomon() => Card("BT26-079", "ZombiePlutomon", CardType.Digimon, 6, 12, 12000, Bt26079, traits: new[] { "Undead", "Titan", "TS", "Virus" });
    private static CardData BlackSabbath() => Card("BT20-096", "Black Sabbath", CardType.Option, 0, 2, 0, Bt20096, Bt20096Sec, traits: "LIBERATOR");
    private static CardData VenomMyotismon() => Card("EX10-054", "VenomMyotismon", CardType.Digimon, 6, 12, 12000, Ex10054, traits: new[] { "Dark Animal", "Virus" });
    private static CardData MaloMyotismon() => Card("EX10-011", "MaloMyotismon", CardType.Digimon, 6, 14, 14000, Ex10011, color: CardColor.Red, traits: new[] { "Demon Lord", "Virus" });
    private static CardData Cthyllamon() => Card("LM-006", "Cthyllamon", CardType.Digimon, 6, 11, 11000, Lm006, color: CardColor.Blue, traits: new[] { "Fairy", "Mega", "Virus" });
    private static CardData SeventhFullCluster() => Card("BT12-110", "Seventh Full Cluster", CardType.Option, 0, 7, 0, Bt12110, "Security Effect [Security] Activate this card's [Main] effects.", traits: "Seven Great Demon Lords");
    private static CardData ChaosGallantmon() => Card("EX4-011", "ChaosGallantmon", CardType.Digimon, 6, 12, 12000, Ex4011, color: CardColor.Red, traits: new[] { "Dark Knight", "Virus" });

    private static CardData Filler(string id) => Card(id, id, CardType.Digimon, 3, 3, 3000, "", color: CardColor.White);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8, int hand = 3)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 12; i++)
        {
            state.Player.MainDeck.Add(Filler("B86-FILL" + i));
            state.DummyOpponent.MainDeck.Add(Filler("B86-OFILL" + i));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Filler("B86-SEC" + i));
            state.DummyOpponent.SecurityStack.Add(Filler("B86-OSEC" + i));
        }
        for (int i = 0; i < hand; i++)
            state.Player.Hand.Add(Filler("B86-HAND" + i));
        var engine = new GoldfishEngine(state) { PlayerChoices = new Answers() };
        return (state, engine);
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false)
    {
        var d = new CardInstance(card, 1) { IsOwnersTurn = true, IsSuspended = suspended };
        zones.BattleArea.Add(d);
        return d;
    }

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    private sealed class Answers : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    // ---------- 트래시 목록 ----------

    [Fact]
    public void DescribeTrash_NewestFirst_WithTrashEffectFlags()
    {
        var (state, engine) = NewMainPhase();
        var plain = Filler("B86-PLAIN");
        var cluster = SeventhFullCluster();
        var nid = Nidhoggmon();
        state.Player.Trash.Add(plain);     // 가장 먼저 간 카드
        state.Player.Trash.Add(cluster);
        state.Player.Trash.Add(nid);       // 맨 위

        var list = engine.DescribeTrash(state.Player);

        Assert.Equal(new[] { nid, cluster, plain }, list.Select(i => i.Card));
        Assert.Equal(new[] { 0, 1, 2 }, list.Select(i => i.Position));
        Assert.True(list[0].HasTrashMain);
        Assert.True(list[0].CanUse, list[0].Reason);
        Assert.False(list[1].HasTrashMain);
        Assert.True(list[1].HasTrashTiming);   // [트래시]【자신의 턴】 — 자동
        Assert.False(list[2].HasTrashMain || list[2].HasTrashTiming);
        Assert.Empty(engine.DescribeTrash(state.DummyOpponent));
    }

    [Fact]
    public void OpponentTrash_IsListed_ButNotUsableOnMyTurn()
    {
        var (state, engine) = NewMainPhase();
        var nid = Nidhoggmon();
        state.DummyOpponent.Trash.Add(Filler("B86-OPLAIN"));
        state.DummyOpponent.Trash.Add(nid);

        var list = engine.DescribeTrash(state.DummyOpponent);

        Assert.Equal(2, list.Count);
        Assert.Same(nid, list[0].Card);
        Assert.True(list[0].HasTrashMain);
        Assert.False(list[0].CanUse);
        Assert.Contains("메인 페이즈", list[0].Reason);
    }

    // ---------- 발동 조건 "If ~," ----------

    [Fact]
    public void EX7_060_HandCondition_ReasonThenPlayWithCostReducedBy4()
    {
        var (state, engine) = NewMainPhase(memory: 8, hand: 5);
        var nid = Nidhoggmon();
        AllSupported(nid);
        state.Player.Trash.Add(nid);

        var (ok, why) = engine.TrashMainStatus(state.Player, nid);
        Assert.False(ok);
        Assert.Equal("조건이 맞지 않습니다 (자신의 패가 4장 이하일 때).", why);
        var fail = engine.ActivateTrashEffect(nid);
        Assert.False(fail.CanExecute);
        Assert.Contains("자신의 패가 4장 이하일 때", fail.Reason);
        Assert.Contains(nid, state.Player.Trash);
        Assert.Equal(8, state.MemoryGauge);

        state.Player.Hand.RemoveAt(0);   // 패 4장
        Assert.True(engine.TrashMainStatus(state.Player, nid).CanUse);
        Assert.True(engine.ActivateTrashEffect(nid).CanExecute, Logs(state));
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, nid));
        Assert.DoesNotContain(nid, state.Player.Trash);
        Assert.Equal(8 - (11 - 4), state.MemoryGauge);
    }

    [Fact]
    public void BT24_076_PlayWithCostReducedBy2()
    {
        var (state, engine) = NewMainPhase(memory: 8, hand: 4);
        var war = WarGrowlmon();
        AllSupported(war);
        state.Player.Trash.Add(war);

        Assert.True(engine.ActivateTrashEffect(war).CanExecute, Logs(state));
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, war));
        Assert.Equal(8 - (7 - 2), state.MemoryGauge);
    }

    [Fact]
    public void BT26_079_CostReducedBy4_HandFiveOrFewer_AndKoreanText()
    {
        Assert.Equal("[트래시] 【메인】 자신의 패가 5장 이하일 때, 이 카드를 지불하는 등장 코스트 -4 하여 등장시킨다.",
            KoreanEffectRenderer.RenderClause("[Trash] [Main] If your hand has 5 or fewer cards, play this card with the cost reduced by 4."));
        var (state, engine) = NewMainPhase(memory: 9, hand: 6);
        var zombie = ZombiePlutomon();
        AllSupported(zombie);
        state.Player.Trash.Add(zombie);

        Assert.Equal("조건이 맞지 않습니다 (자신의 패가 5장 이하일 때).", engine.TrashMainStatus(state.Player, zombie).Reason);
        state.Player.Hand.RemoveAt(0);   // 5장
        Assert.True(engine.ActivateTrashEffect(zombie).CanExecute, Logs(state));
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, zombie));
        Assert.Equal(9 - (12 - 4), state.MemoryGauge);
    }

    // ---------- 비용 "By ~," ----------

    [Fact]
    public void BT20_096_PayingMemoryCost_NeverBlocks_ThenReturnsToDeckBottom()
    {
        // 메모리 지불 비용은 메모리가 상대 쪽으로 넘어가도 낼 수 있다 (엔진 ParseCost: CanPay = 언제나 — 턴이 끝날 뿐)
        var (state, engine) = NewMainPhase(memory: 3, hand: 2);
        var sabbath = BlackSabbath();
        AllSupported(sabbath);
        var foe = Put(state.DummyOpponent, Filler("B86-FOE"));
        state.Player.Trash.Add(sabbath);
        Assert.True(engine.TrashMainStatus(state.Player, sabbath).CanUse);

        state.MemoryGauge = 7;
        Assert.True(engine.ActivateTrashEffect(sabbath).CanExecute, Logs(state));
        Assert.Same(sabbath, state.Player.MainDeck[^1]);     // 덱 아래로
        Assert.DoesNotContain(foe, state.DummyOpponent.BattleArea);
        Assert.Equal(1, state.MemoryGauge);
    }

    [Fact]
    public void BT20_096_ConditionCheckedBeforeCost()
    {
        var (state, engine) = NewMainPhase(memory: 8, hand: 5);
        var sabbath = BlackSabbath();
        state.Player.Trash.Add(sabbath);

        Assert.Equal("조건이 맞지 않습니다 (자신의 패가 4장 이하일 때).", engine.TrashMainStatus(state.Player, sabbath).Reason);
    }

    [Fact]
    public void EX10_054_NeedsLevel5MyotismonToDelete()
    {
        var (state, engine) = NewMainPhase(memory: 6, hand: 3);
        var venom = VenomMyotismon();
        AllSupported(venom);
        state.Player.Trash.Add(venom);
        // 필드에 디지몬이 없다 — API 문구의 줄바꿈 없는 공백(U+00A0)도 효과 처리와 같이 정규화해서 읽는다
        Assert.Equal("비용을 낼 수 없습니다 (「묘티스몬」이 기술되어 있는 Lv.5인 자신의 디지몬 1마리를 소멸시키는 것으로).", engine.TrashMainStatus(state.Player, venom).Reason);
        Assert.Equal("[트래시] 【메인】 「묘티스몬」이 기술되어 있는 Lv.5인 자신의 디지몬 1마리를 소멸시키는 것으로, 이 카드를 지불하는 등장 코스트 -7 하여 등장시킨다.",
            KoreanEffectRenderer.RenderClause(Ex10054.Split('\n')[0].Trim()));
        Put(state.Player, Card("B86-LV4MYO", "Myotismon", CardType.Digimon, 4, 5, 5000, ""));   // Lv.4 — 맞지 않음

        var (ok, why) = engine.TrashMainStatus(state.Player, venom);
        Assert.False(ok);
        Assert.StartsWith("비용을 낼 수 없습니다", why);

        var myo = Put(state.Player, Card("B86-MYO", "Myotismon", CardType.Digimon, 5, 7, 7000, ""));
        Assert.True(engine.TrashMainStatus(state.Player, venom).CanUse);
        Assert.True(engine.ActivateTrashEffect(venom).CanExecute, Logs(state));
        Assert.DoesNotContain(myo, state.Player.BattleArea);
        Assert.Contains(state.Player.BattleArea, d => ReferenceEquals(d.TopCard, venom));
        Assert.Equal(6 - (12 - 7), state.MemoryGauge);
    }

    [Fact]
    public void EX10_011_NeedsTwoMyotismonTextDigimon()
    {
        var (state, engine) = NewMainPhase(memory: 6, hand: 3);
        var malo = MaloMyotismon();
        AllSupported(malo);
        state.Player.Trash.Add(malo);
        Put(state.Player, Card("B86-MYO1", "Myotismon", CardType.Digimon, 5, 7, 7000, ""));

        Assert.StartsWith("비용을 낼 수 없습니다", engine.TrashMainStatus(state.Player, malo).Reason);

        Put(state.Player, Card("B86-MYO2", "VenomMyotismon", CardType.Digimon, 6, 12, 12000, ""));
        Assert.True(engine.TrashMainStatus(state.Player, malo).CanUse);
        Assert.True(engine.ActivateTrashEffect(malo).CanExecute, Logs(state));
        Assert.Single(state.Player.BattleArea);
        Assert.Same(malo, state.Player.BattleArea[0].TopCard);
        Assert.Equal(6 - (14 - 11), state.MemoryGauge);
    }

    [Fact]
    public void LM_006_NeedsTamerToReturn()
    {
        var (state, engine) = NewMainPhase(memory: 8, hand: 3);
        var cthylla = Cthyllamon();
        AllSupported(cthylla);
        state.Player.Trash.Add(cthylla);

        Assert.StartsWith("비용을 낼 수 없습니다", engine.TrashMainStatus(state.Player, cthylla).Reason);

        var tamer = Card("B86-TAMER", "Tamer", CardType.Tamer, 0, 5, 0, "", color: CardColor.Blue);
        state.Player.Tamers.Add(tamer);
        Assert.True(engine.ActivateTrashEffect(cthylla).CanExecute, Logs(state));
        Assert.DoesNotContain(tamer, state.Player.Tamers);
        Assert.Same(tamer, state.Player.MainDeck[^1]);
        Assert.Equal(8 - (11 - 5), state.MemoryGauge);
    }

    // ---------- 트래시에서만 · 메인 페이즈에만 · 장마다 ----------

    [Fact]
    public void TrashMain_OnlyFromTrash_AndOnlyInMainPhase()
    {
        var (state, engine) = NewMainPhase(memory: 8, hand: 3);
        var nid = Nidhoggmon();
        state.Player.Hand.Add(nid);   // 패에 있는 카드 — [트래시] 효과는 쓸 수 없다

        Assert.Equal("트래시에 없는 카드입니다.", engine.TrashMainStatus(state.Player, nid).Reason);
        Assert.False(engine.ActivateTrashEffect(nid).CanExecute);
        Assert.Contains(nid, state.Player.Hand);

        state.Player.Hand.Remove(nid);
        state.Player.Trash.Add(nid);
        state.CurrentPhase = Phase.Breeding;
        Assert.Equal("자신의 메인 페이즈에만 쓸 수 있습니다.", engine.TrashMainStatus(state.Player, nid).Reason);
        Assert.False(engine.ActivateTrashEffect(nid).CanExecute);
        Assert.Contains(nid, state.Player.Trash);
    }

    [Fact]
    public void TrashMain_EachCopyOncePerTurn()
    {
        var (state, engine) = NewMainPhase(memory: 9, hand: 2);
        var nid = Nidhoggmon();
        state.Player.Trash.Add(nid);
        state.Player.Trash.Add(nid);   // 같은 카드 2장

        Assert.True(engine.ActivateTrashEffect(nid).CanExecute, Logs(state));
        Assert.Single(state.Player.Trash);
        Assert.True(engine.TrashMainStatus(state.Player, nid).CanUse);   // 남은 1장은 아직 쓰지 않았다
    }

    [Fact]
    public void BT10_025_HandMain_SecondCopyStillUsable()
    {
        // [Hand] [Main]도 같은 규칙 — 1장을 진화원으로 놓아 패를 떠났으면 남은 1장은 아직 쓰지 않았다
        const string text = "[Hand] [Main] If you have a Digimon with the [Blue Flare] trait\u00A0in play, by paying 3 memory, place this card under that Digimon as its bottom digivolution card. Then, unsuspend that Digimon.";
        var (state, engine) = NewMainPhase(memory: 9, hand: 0);
        var cyber = Card("BT10-025", "Cyberdramon", CardType.Digimon, 5, 8, 7000, text, color: CardColor.Blue, traits: new[] { "Alien Humanoid", "Blue Flare", "Vaccine" });
        AllSupported(cyber);
        var host = Put(state.Player, Card("B86-BF", "Gumdramon", CardType.Digimon, 4, 5, 5000, "", color: CardColor.Blue, traits: "Blue Flare"), suspended: true);
        state.Player.Hand.Add(cyber);
        state.Player.Hand.Add(cyber);

        Assert.True(engine.ActivateHandEffect(cyber).CanExecute, Logs(state));
        Assert.Single(state.Player.Hand);
        Assert.Contains(engine.GetHandMainAbilities(state.Player), a => ReferenceEquals(a.Card, cyber));
        Assert.True(engine.ActivateHandEffect(cyber).CanExecute, Logs(state));
        Assert.Empty(state.Player.Hand);
        Assert.True(host.EvolutionSources.Count == 2, Logs(state));
        Assert.Equal(3, state.MemoryGauge);
    }

    [Fact]
    public void NoTrashMain_ReasonSaysSo()
    {
        var (state, engine) = NewMainPhase();
        var gallant = ChaosGallantmon();   // [트래시]【자신의 턴 종료 시】 — 자동 효과뿐
        state.Player.Trash.Add(gallant);

        var info = engine.DescribeTrash(state.Player).Single();
        Assert.False(info.HasTrashMain);
        Assert.True(info.HasTrashTiming);
        Assert.False(engine.TrashMainStatus(state.Player, gallant).CanUse);
        Assert.Null(info.Reason);
    }

    // ---------- AI ----------

    [Fact]
    public void Ai_SkipsTrashMainWhenConditionFails()
    {
        var (state, engine) = NewMainPhase(memory: 0, hand: 0);
        var nid = Nidhoggmon();
        state.DummyOpponent.Trash.Add(nid);
        for (int i = 0; i < 7; i++)
            state.DummyOpponent.Hand.Add(Filler("B86-OHAND" + i));   // 패 7장 이상 — 조건이 맞지 않는다

        engine.PassTurn();

        Assert.Contains(nid, state.DummyOpponent.Trash);
        Assert.DoesNotContain(state.DummyOpponent.BattleArea, d => ReferenceEquals(d.TopCard, nid));
        Assert.DoesNotContain(state.ActionLogs, l => l.Contains("[트래시] [메인] Nidhoggmon"));
    }
}
