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
/// v0.78.0 미지원 문장 52차 묶음: BT12-112 · BT21-030 효과로 등장할 때도 「샤우트몬」 비용 · 디지크로스 (종합 룰 7-2-2-13 · 7-2-3-1 · Q&amp;A BT12-112 Q1) ·
/// BT17-102 진화원의 Lv.3 이하 카드의 명칭 전부 (Q&amp;A BT17-102 Q1 · 4-7-9) · BT17-050 놓은 디지몬으로 어택 (Q&amp;A BT17-050 Q1) ·
/// BT25-038 패 또는 디지몬의 진화원에서 시큐리티로 · EX13-076 진화원 매수로 비교하는 배틀
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch78Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

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

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, int playCost = 3, int evoCost = 1, string inherited = "", params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, evoCost, inherited, traits);

    private static CardData Egg(string id, string? name = null) => Card(id, "", CardType.Digitama, 2, 0, CardColor.Blue, 0, name, 0, "");

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B78-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B78-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B78-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B78-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b78_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 1. BT12-112 · BT21-030 효과로 등장할 때도 (7-2-2-13 효과로 등장시킬 때도 디지크로스할 수 있다)
    // =====================================================================

    private const string Bt12112 = "When you would play this card, by placing 1 [Shoutmon] as a digivolution card under this Digimon, reduce the cost by 1 and you may place cards in your trash as digivolution cards for a DigiXros.";
    private const string Bt21030 = "When this card would be played, by placing 1 of your [Shoutmon] under it, reduce the play cost by 1 and cards in your trash can also be placed for DigiXros.";

    [Theory]
    [InlineData(Bt12112)]
    [InlineData(Bt21030)]
    public void X7_PaidEffectPlay_PlacesShoutmon_TrashXros_ReducesCost(string text)
    {
        string id = Uid("B78-X7-"), a = Uid("B78-XA-"), b = Uid("B78-XB-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = id, Name = "테스트X7E", MainText = "디지크로스 -2:「테스트에이E」×「테스트비E」" },
            new KoreanCard { CardId = a, Name = "테스트에이E" }, new KoreanCard { CardId = b, Name = "테스트비E" },
        }, () =>
        {
            var x7 = Digimon(id, 7, text, playCost: 15);
            var (state, engine) = NewMainPhase(memory: 10);
            engine.PlayerChoices = new AcceptAll();
            var shoutTop = Digimon("B78-SHOUT", 3, name: "Shoutmon");
            var shoutUnder = Digimon("B78-SHU", 2);
            Put(state.Player, shoutTop, shoutUnder);
            var ma = Digimon(a, 3); var mb = Digimon(b, 3);
            state.Player.Trash.Add(ma); state.Player.Trash.Add(mb);
            state.Player.Hand.Add(x7);
            // "with the play cost reduced by 5" 같은 코스트를 내는 효과 등장: 15 - 5 = 10 → 샤우트몬 -1 · 디지크로스 2장 × -2 → 5
            Assert.True(engine.TryPlayByEffect(state.Player, x7, PlayOrigin.Hand, () => state.Player.Hand.Remove(x7), payCost: 10));
            var me = Assert.Single(state.Player.BattleArea);
            Assert.Same(x7, me.TopCard);
            Assert.Contains(shoutTop, me.EvolutionSources);
            Assert.Same(shoutTop, me.EvolutionSources.Last());   // 디지크로스 재료 아래 (맨 아래)
            Assert.Contains(ma, me.EvolutionSources);
            Assert.Contains(mb, me.EvolutionSources);
            Assert.Contains(shoutUnder, state.Player.Trash);
            Assert.Equal(5, state.MemoryGauge);
        });
    }

    [Fact]
    public void X7_FreeEffectPlay_DoesNotAskShoutmon()
    {
        string id = Uid("B78-X7F-");
        WithKoreanCards(new[] { new KoreanCard { CardId = id, Name = "테스트X7F", MainText = "디지크로스 -2:「테스트에이F」" } }, () =>
        {
            var x7 = Digimon(id, 7, Bt12112, playCost: 15);
            var (state, engine) = NewMainPhase(memory: 4);
            engine.PlayerChoices = new AcceptAll();
            var shout = Put(state.Player, Digimon("B78-SHOUTF", 3, name: "Shoutmon"));
            state.Player.Trash.Add(x7);
            Assert.True(engine.TryPlayByEffect(state.Player, x7, PlayOrigin.Trash, () => state.Player.Trash.Remove(x7)));
            Assert.Contains(shout, state.Player.BattleArea);
            Assert.Equal(2, state.Player.BattleArea.Count);
            Assert.Equal(4, state.MemoryGauge);
        });
    }

    // =====================================================================
    // 2. BT17-102 진화원의 Lv.3 이하 카드의 명칭 전부 (Q&A BT17-102 Q1)
    // =====================================================================

    private const string Bt17102 = "[When Digivolving] If this Digimon's name is [Koromon], it gets +3000 DP for the turn. Then, delete 1 of your opponent's Digimon with as much or less DP as this Digimon.\n[All Turns] This Digimon has all the names of level 3 and lower cards in its digivolution cards.";

    [Fact]
    public void BT17_102_HasNamesOfLevel3AndLowerSources()
    {
        var grey = Digimon(Uid("B78-GR-"), 4, Bt17102, name: "Greymon", dp: 5000);
        AllSupported(grey);
        var agu = Digimon("B78-AGU", 3, name: "Agumon");
        var koro = Egg("B78-KORO", "Koromon");
        var lv4 = Digimon("B78-L4", 4, name: "Tyrannomon");
        var d = new CardInstance(grey, 1) { IsOwnersTurn = true };
        d.AddBottomSource(lv4); d.AddBottomSource(agu); d.AddBottomSource(koro);
        Assert.True(CardNames.Is(d, "Greymon"));
        Assert.True(CardNames.Is(d, "Agumon"));
        Assert.True(CardNames.Is(d, "Koromon"));
        Assert.True(CardNames.Contains(d, "Agu"));
        Assert.False(CardNames.Is(d, "Tyrannomon"));   // Lv.4
        var other = new CardInstance(Digimon("B78-PLAIN", 4, name: "Greymon"), 1);
        other.AddBottomSource(agu);
        Assert.False(CardNames.Is(other, "Agumon"));   // 이 효과가 없으면 맨 위 카드만
        Assert.True(CardNames.Same(d, new CardInstance(Digimon("B78-AG2", 3, name: "Agumon"), 1)));
    }

    [Fact]
    public void BT17_102_FaceDownSourceHasNoName_4_7_9()
    {
        var grey = Digimon(Uid("B78-GRF-"), 4, Bt17102, name: "Greymon");
        var agu = Digimon("B78-AGUF", 3, name: "Agumon");
        var d = new CardInstance(grey, 1);
        d.AddBottomSource(agu);
        d.FaceDownSources.Add(agu);
        Assert.False(CardNames.Is(d, "Agumon"));
    }

    [Fact]
    public void BT17_102_WhenDigivolving_KoromonNameGetsDp()
    {
        var grey = Digimon(Uid("B78-GRK-"), 4, Bt17102, name: "Greymon", dp: 5000);
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, grey, Digimon("B78-AGUK", 3, name: "Agumon"), Egg("B78-KOROK", "Koromon"));
        var big = Put(state.DummyOpponent, Digimon("B78-OPP8", 5, dp: 8000));
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, d, grey, includeInherited: false);
        Assert.Equal(8000, d.GetCurrentDP());
        Assert.DoesNotContain(big, state.DummyOpponent.BattleArea);
    }

    // =====================================================================
    // 3. BT17-050 [패]【메인】 놓은 디지몬으로 어택 (Q&A BT17-050 Q1)
    // =====================================================================

    private const string Bt17050 = "[Hand] [Main] By paying 4 cost and placing this card as 1 of your level 5 or higher Digimon's bottom digivolution card, you may suspend 1 of your opponent's Digimon and attack an opponent's Digimon with the Digimon this was placed under.";

    [Fact]
    public void BT17_050_SuspendsThenAttacksWithHost()
    {
        var para = Digimon(Uid("B78-PARA-"), 6, Bt17050, color: CardColor.Green);
        AllSupported(para);
        var (state, engine) = NewMainPhase(memory: 5);
        engine.PlayerChoices = new AcceptAll();
        var host = Put(state.Player, Digimon("B78-HOST", 5, dp: 9000));
        var target = Put(state.DummyOpponent, Digimon("B78-OT", 4, dp: 4000));
        state.Player.Hand.Add(para);
        Assert.True(engine.ActivateHandEffect(para).CanExecute);
        engine.RunDeclaredEffectAttack57();
        Assert.Equal(1, state.MemoryGauge);
        Assert.Contains(para, host.EvolutionSources);
        Assert.True(host.IsSuspended);
        Assert.DoesNotContain(target, state.DummyOpponent.BattleArea);
    }

    [Fact]
    public void BT17_050_NoLevel5_CannotPayCost_QA_Q1()
    {
        var para = Digimon(Uid("B78-PARB-"), 6, Bt17050, color: CardColor.Green);
        var (state, engine) = NewMainPhase(memory: 5);
        engine.PlayerChoices = new AcceptAll();
        Put(state.Player, Digimon("B78-L4H", 4));
        Put(state.DummyOpponent, Digimon("B78-OT2", 4));
        state.Player.Hand.Add(para);
        Assert.False(engine.ActivateHandEffect(para).CanExecute);
        Assert.Equal(5, state.MemoryGauge);
        Assert.Contains(para, state.Player.Hand);
    }

    // =====================================================================
    // 4. BT25-038 패 또는 자신의 디지몬의 진화원에서 시큐리티로
    // =====================================================================

    private const string Bt25038 = "[On Play] [When Digivolving] You may place 1 [Angel], [Archangel], [Three Great Angels] or [Iliad] trait Digimon card from your hand or your Digimon's digivolution cards as the top or bottom security card. Then, if DNA digivolving, trash both players' top security cards.";

    [Fact]
    public void BT25_038_PlacesFromDigivolutionCards()
    {
        var shak = Digimon(Uid("B78-SHK-"), 5, Bt25038, traits: "Iliad");
        AllSupported(shak);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var angel = Digimon("B78-ANG", 4, traits: "Angel");
        var other = Put(state.Player, Digimon("B78-OTHER", 5), angel);
        var me = Put(state.Player, shak);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, shak, includeInherited: false);
        Assert.DoesNotContain(angel, other.EvolutionSources);
        Assert.Same(angel, state.Player.SecurityStack[0]);
        Assert.Equal(6, state.Player.SecurityStack.Count);
        Assert.Equal(5, state.DummyOpponent.SecurityStack.Count);   // 조그레스 진화가 아니다
    }

    [Fact]
    public void BT25_038_PlacesFromHand()
    {
        var shak = Digimon(Uid("B78-SHH-"), 5, Bt25038, traits: "Iliad");
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var angel = Digimon("B78-ANGH", 4, traits: "Archangel");
        state.Player.Hand.Add(angel);
        var me = Put(state.Player, shak);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, shak, includeInherited: false);
        Assert.DoesNotContain(angel, state.Player.Hand);
        Assert.Contains(angel, state.Player.SecurityStack);
    }

    // =====================================================================
    // 5. EX13-076 진화원 매수로 비교하는 배틀
    // =====================================================================

    private const string Ex13076 = "[On Play] [When Digivolving] [When Attacking] [Once Per Turn] You may suspend 1 of your opponent's Digimon. Then, you may return all digivolution cards of 1 of their Digimon to the bottom of the deck and have this Digimon battle it. Compare the number of digivolution cards instead of DP in this battle.";

    [Fact]
    public void EX13_076_ReturnsSources_BattlesBySourceCount()
    {
        var pal = Digimon(Uid("B78-PAL-"), 7, Ex13076, dp: 1000);
        AllSupported(pal);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var me = Put(state.Player, pal, Digimon("B78-S1", 6));
        var s1 = Digimon("B78-OS1", 5); var s2 = Digimon("B78-OS2", 4); var s3 = Digimon("B78-OS3", 3);
        var opp = Put(state.DummyOpponent, Digimon("B78-OBIG", 6, dp: 15000), s1, s2, s3);
        int deckBefore = state.DummyOpponent.MainDeck.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, pal, includeInherited: false);
        Assert.DoesNotContain(opp, state.DummyOpponent.BattleArea);   // 1장 vs 0장 — DP가 낮아도 이긴다
        Assert.Contains(me, state.Player.BattleArea);
        Assert.Equal(deckBefore + 3, state.DummyOpponent.MainDeck.Count);
        Assert.Equal(new[] { s1, s2, s3 }, state.DummyOpponent.MainDeck.Skip(deckBefore).ToArray());
    }

    [Fact]
    public void EX13_076_BothZeroSources_BothDeleted()
    {
        var pal = Digimon(Uid("B78-PAZ-"), 7, Ex13076, dp: 16000);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var me = Put(state.Player, pal);
        var opp = Put(state.DummyOpponent, Digimon("B78-OSM", 3, dp: 1000));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, pal, includeInherited: false);
        Assert.DoesNotContain(opp, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(me, state.Player.BattleArea);
    }

    [Fact]
    public void EX13_076_NormalBattlesStillCompareDp()
    {
        var (state, engine) = NewMainPhase();
        var a = Put(state.Player, Digimon("B78-NA", 5, dp: 6000), Digimon("B78-NS", 4));
        var b = Put(state.DummyOpponent, Digimon("B78-NB", 5, dp: 3000), Digimon("B78-NS1", 4), Digimon("B78-NS2", 3));
        engine.EffectBattle(a, b);
        Assert.DoesNotContain(b, state.DummyOpponent.BattleArea);
        Assert.Contains(a, state.Player.BattleArea);
    }

    // =====================================================================
    // 6. 한글
    // =====================================================================

    [Fact]
    public void Korean_NewClauses()
    {
        Assert.Equal("【서로의 턴】 이 디지몬은 이 디지몬의 진화원에 있는 Lv.3 이하의 카드의 명칭 전부를 얻는다.",
            Ko("[All Turns] This Digimon has all the names of level 3 and lower cards in its digivolution cards."));
        Assert.EndsWith("이 카드를 Lv.5 이상인 자신의 디지몬 1마리의 진화원 아래에 놓는 것으로, 상대의 디지몬 1마리를 레스트시키고, 이 카드를 놓은 디지몬으로 상대의 디지몬에게 어택할 수 있다.", Ko(Bt17050));
        Assert.Contains("자신의 패 또는 자신의 디지몬의 진화원에서", Ko(Bt25038));
        Assert.EndsWith("이 배틀에서는 DP 대신 진화원의 매수를 비교한다.", Ko(Ex13076));
    }
}
