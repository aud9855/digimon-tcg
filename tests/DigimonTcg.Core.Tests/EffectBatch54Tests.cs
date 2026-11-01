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
/// v0.54.0 미지원 문장 28차 묶음: 효과 처리 중에 유발한 효과는 그 효과가 끝난 뒤 (종합 룰 15-8-3-2 · 15-4-4) ·
/// [턴에 1회]는 효과(줄)마다 (15-14-1 · 공식 Q&amp;A BT13-056 · EX5-025) · 진화할 때 지불하는 코스트 감소 (BT11-064 · BT14-013 · BT9-077 · EX1-061 · ST13-04 · P-076 · P-181) ·
/// 진화원 카드의 [메인] / 【서로의 턴】 효과 얻기 (BT22-078 · EX10-059) · 발휘하지 않는다 (BT17-092 · BT5-085 · P-095) · 디지크로스 재료 (BT18-065 · BT19-087 · EX10-064 · EX4-062)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch54Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, int evoCost = 1, string inherited = "", CardColor? second = null, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, SecondaryColor = second, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, string inherited = "", int playCost = 3, int evoCost = 1, CardColor? second = null, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, evoCost, inherited, second, traits);

    private static CardData Tamer(string id, string main = "", string? name = null, CardColor color = CardColor.Blue, int playCost = 2) =>
        Card(id, main, CardType.Tamer, 0, playCost, color, 0, name, 0, "", null);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B54-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B54-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B54-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B54-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static int LogIndex(GameState s, string part) => s.ActionLogs.FindIndex(l => l.Contains(part));

    // =====================================================================
    // 1. 효과 처리 중에 유발한 효과는 그 효과가 끝난 뒤 (종합 룰 15-8-3-2)
    // =====================================================================

    private const string Training = "＜Training＞ (In the main phase, by suspending this Digimon, place your deck's top card face down as this Digimon's bottom digivolution card. This effect can also activate in the breeding area.)";

    [Fact]
    public void Training_SuspendTriggerWaitsUntilTrainingResolves()
    {
        var trainee = Digimon(Uid("B54-TRAIN-"), 4, Training);
        var watcher = Tamer(Uid("B54-WATCH-"), "[Your Turn] [Once Per Turn] When one of your Digimon becomes suspended, ＜Draw 1＞ (Draw 1 card from your deck.)");
        AllSupported(trainee);
        AllSupported(watcher);
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(watcher);
        var me = Put(state.Player, trainee);
        Assert.True(engine.ActivateMainAbility(me, trainee).CanExecute, Logs(state));
        Assert.True(me.IsSuspended);
        Assert.Equal(1, me.FaceDownSourceCount);
        Assert.Single(state.Player.Hand);
        int placed = LogIndex(state, "≪트레이닝≫");
        int drew = state.ActionLogs.FindLastIndex(l => l.Contains("드로우"));
        Assert.True(placed >= 0 && drew > placed, Logs(state));   // 전에는 레스트하자마자 드로우하고 그 뒤에 덱 위 카드를 놓았다
    }

    [Fact]
    public void SuspendByEffect_TriggerRunsAfterTheWholeEffect()
    {
        var watcher = Digimon(Uid("B54-OPPW-"), 5, "[All Turns] [Once Per Turn] When this Digimon becomes suspended, suspend 1 of your opponent's Digimon or Tamers with a play cost less than or equal to this Digimon's play cost.", playCost: 9);
        AllSupported(watcher);
        var opt = Card(Uid("B54-SUSOPT-"), "[Main] Suspend 1 of your opponent's Digimon. Then, return 1 of your opponent's suspended Digimon to the bottom of its owner's deck.", CardType.Option, 0, 1);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var w = Put(state.DummyOpponent, watcher);
        var mine = Put(state.Player, Digimon("B54-MINE", 5, playCost: 5));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        // 옵션 처리 중에 유발한 "레스트 상태가 되었을 때"는 옵션이 끝난 뒤 — 그 디지몬은 이미 덱 아래로 가 발휘하지 않는다 (15-4-4-3)
        Assert.DoesNotContain(w, state.DummyOpponent.BattleArea);
        Assert.False(mine.IsSuspended, Logs(state));   // 전에는 레스트하자마자 발휘해 자신의 디지몬을 레스트시켰다
    }

    [Fact]
    public void DigivolveByEffect_WhenDigivolvingWaitsForTheRestOfTheEffect()
    {
        var evo = Digimon(Uid("B54-SUKA-"), 5, "[When Digivolving] Trash the top card of your opponent's security stack.", name: "Sukamon B54");
        var opt = Card(Uid("B54-EVOOPT-"), "[Main] 1 of your Digimon may digivolve into a Digimon card with [Sukamon] in its name in your hand without paying the cost, ignoring its digivolution requirements. Then, ＜Draw 1＞ (Draw 1 card from your deck.)", CardType.Option, 0, 1);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B54-BASE4", 4));
        state.Player.Hand.Add(evo);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        Assert.Same(evo, me.TopCard);
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        int evolved = LogIndex(state, "효과로 진화");
        int trashed = state.ActionLogs.FindIndex(l => l.Contains("시큐리티") && l.Contains("파기"));
        Assert.True(evolved >= 0 && trashed > evolved, Logs(state));
        Assert.Equal(2, state.Player.Hand.Count);   // 진화 드로우 1장 · 옵션의 ≪1 드로우≫
    }

    [Fact]
    public void OutsideEffects_SuspendTriggerStillImmediate()
    {
        var watcher = Tamer(Uid("B54-WATCH2-"), "[Your Turn] [Once Per Turn] When one of your Digimon becomes suspended, ＜Draw 1＞ (Draw 1 card from your deck.)");
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(watcher);
        var atk = Put(state.Player, Digimon("B54-ATK", 5));
        atk.TurnPlayed = 0;
        engine.AttackSecurityOrDirect(atk);   // 어택 선언의 레스트 — 효과 밖 (그대로)
        Assert.Single(state.Player.Hand);
    }

    // =====================================================================
    // 2. [턴에 1회]는 효과(줄)마다
    // =====================================================================

    [Fact]
    public void OncePerTurn_TwoEffectsOfSameCard_EachOnce_ST14_08()
    {
        var mon = Digimon(Uid("B54-ST1408-"), 6, "[All Turns] [Once Per Turn] When a card is trashed from your deck, gain 1 memory.\r\n[Your Turn] [Once Per Turn] When a card is trashed from your deck, ＜Draw 1＞ (Draw 1 card from your deck.)");
        AllSupported(mon);
        var (state, engine) = NewMainPhase(memory: 3);
        Put(state.Player, mon);
        engine.Mill(state.Player, 1, "test");
        Assert.Equal(4, state.MemoryGauge);
        Assert.Single(state.Player.Hand);   // 전에는 같은 카드의 첫 [턴에 1회]가 둘째를 막았다
        engine.Mill(state.Player, 1, "test");
        Assert.Equal(4, state.MemoryGauge);
        Assert.Single(state.Player.Hand);
    }

    [Fact]
    public void OncePerTurn_TwoTimingsOnOneLine_IsOneEffect_EX5_025()
    {
        var mon = Digimon(Uid("B54-EX5025-"), 5, "[When Digivolving] [When Attacking] [Once Per Turn] ＜Draw 1＞ (Draw 1 card from your deck.)");
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, mon);
        engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, mon);
        Assert.Single(state.Player.Hand);   // 공식 Q&A EX5-025 · BT13-056: 한 효과 — 그 턴에 1번
    }

    [Fact]
    public void OncePerTurn_DuplicatedInheritedLine_StillOnce()
    {
        const string line = "[Your Turn] [Once Per Turn] When a card is trashed from your deck, ＜Draw 1＞ (Draw 1 card from your deck.)";
        var egg = Card(Uid("B54-EGG-"), "Inherited Effect " + line, CardType.Digimon, 3, inherited: line.Replace("[Your Turn] ", ""));
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B54-TOP", 4), egg);
        engine.Mill(state.Player, 1, "test");
        Assert.Single(state.Player.Hand);   // 영어 데이터의 메인 칸 "Inherited Effect ~"와 진화원 칸은 같은 효과
    }

    // =====================================================================
    // 3. 진화할 때 지불하는 코스트 감소
    // =====================================================================

    [Fact]
    public void EvoCost_ForEachOfThatCardsColors_BT11_064()
    {
        var mon = Digimon(Uid("B54-BT11064-"), 4, "[Your Turn] When this Digimon would digivolve into a card with [Greymon] in its name, reduce the cost by 1 for each of that card's colors.", color: CardColor.Black);
        AllSupported(mon);
        Assert.Contains("그 카드의 색 1색마다", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var two = Digimon(Uid("B54-GREY2-"), 5, name: "MetalGreymon B54", color: CardColor.Black, second: CardColor.Red, evoCost: 3);
        var one = Digimon(Uid("B54-GREY1-"), 5, name: "SkullGreymon B54", color: CardColor.Black, evoCost: 3);
        var other = Digimon(Uid("B54-OTHER-"), 5, name: "Other B54", color: CardColor.Black, evoCost: 3);
        Assert.Equal(1, engine.PreviewEvoCost(state.Player, me, two));
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, me, one));
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, me, other));
    }

    [Fact]
    public void EvoCost_IntoCardsDescribedWithOr_P_076_ST13_04()
    {
        var p076 = Digimon(Uid("B54-P076-"), 4, "[Your Turn] When this Digimon would digivolve into a card with 2 colors or [Composite] trait, reduce the digivolution cost by 2.", color: CardColor.Black);
        var st13 = Digimon(Uid("B54-ST1304-"), 5, "[Your Turn] When this Digimon would digivolve into a card in your hand that's black or has the [Legend-Arms] trait, reduce the digivolution cost by 1.", color: CardColor.Red);
        AllSupported(p076);
        AllSupported(st13);
        var (state, engine) = NewMainPhase();
        var a = Put(state.Player, p076);
        var b = Put(state.Player, st13);
        Assert.Equal(1, engine.PreviewEvoCost(state.Player, a, Digimon(Uid("B54-2C-"), 5, color: CardColor.Black, second: CardColor.Red, evoCost: 3)));
        Assert.Equal(1, engine.PreviewEvoCost(state.Player, a, Digimon(Uid("B54-COMP-"), 5, color: CardColor.Black, evoCost: 3, traits: "Composite")));
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, a, Digimon(Uid("B54-1C-"), 5, color: CardColor.Black, evoCost: 3)));
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, b, Digimon(Uid("B54-BLK-"), 6, color: CardColor.Black, evoCost: 3)));
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, b, Digimon(Uid("B54-LA-"), 6, color: CardColor.Red, evoCost: 3, traits: "Legend-Arms")));
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, b, Digimon(Uid("B54-RED-"), 6, color: CardColor.Red, evoCost: 3)));
    }

    [Fact]
    public void EvoCost_OnlyFromTrash_BT9_077()
    {
        var mon = Digimon(Uid("B54-BT9077-"), 5, "[Your Turn] When this Digimon would digivolve into a Digimon card in your trash, reduce the memory cost of the digivolution by 1.", color: CardColor.Purple);
        AllSupported(mon);
        Assert.Contains("트래시의 디지몬 카드로 진화할 때", Ko(mon.EffectText));
        var (state, engine) = NewMainPhase(memory: 5);
        var me = Put(state.Player, mon);
        var evo = Digimon(Uid("B54-T6-"), 6, color: CardColor.Purple, evoCost: 3);
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, me, evo));   // 패에서는 줄지 않는다
        state.Player.Trash.Add(evo);
        engine.EvolveByEffect(state.Player, me, evo, 3, state.Player.Trash);
        Assert.Same(evo, me.TopCard);
        Assert.Equal(3, state.MemoryGauge);   // 5 - (3 - 1)
    }

    [Fact]
    public void EvoCost_ForTheTurnFromStartOfMainPhase_BT14_013()
    {
        var mon = Digimon(Uid("B54-BT14013-"), 4, "[Start of Your Main Phase] For the turn, when this Digimon would digivolve into a card with [Tyrannomon] in its name, or the [Dinosaur] or [Ceratopsian] trait, reduce the digivolution cost by 1.", color: CardColor.Red);
        AllSupported(mon);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, mon);
        var dino = Digimon(Uid("B54-DINO-"), 5, color: CardColor.Red, evoCost: 3, traits: "Dinosaur");
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, me, dino));
        engine.RunEffects(EffectTrigger.StartOfYourMainPhase, state.Player, me, mon);
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, me, dino));
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, me, Digimon(Uid("B54-NOTD-"), 5, color: CardColor.Red, evoCost: 3)));
    }

    [Fact]
    public void EvoCost_FaceUpSecurityEffect_P_181()
    {
        var opt = Card(Uid("B54-P181-"), "[Security] [Your Turn] [Once Per Turn] When any of your Digimon would digivolve into a Digimon card with the [Royal Base] trait, reduce the digivolution cost by 1.\r\n[Main] Add your top security card to the hand. Then, place this card face up as your bottom security card.", CardType.Option, 0, 2, CardColor.Green, traits: "Royal Base");
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, Digimon("B54-BEE", 4, color: CardColor.Green));
        var rb = Digimon(Uid("B54-RB-"), 5, color: CardColor.Green, evoCost: 3, traits: "Royal Base");
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, me, rb));
        state.Player.Hand.Add(opt);
        Assert.True(engine.PlayCard(opt).CanExecute, Logs(state));
        Assert.Contains(state.Player.SecurityStack, c => c.CardId == opt.CardId && GoldfishEngine.IsFaceUp(state.Player, c));
        Assert.Equal(2, engine.PreviewEvoCost(state.Player, me, rb));
        Assert.Equal(3, engine.PreviewEvoCost(state.Player, me, Digimon(Uid("B54-NRB-"), 5, color: CardColor.Green, evoCost: 3)));
    }

    // =====================================================================
    // 4. 진화원 카드의 [메인] / 【서로의 턴】 효과 얻기
    // =====================================================================

    [Fact]
    public void GainMainEffectsOfFlameSources_YourTurnOnly_BT22_078()
    {
        var top = Digimon(Uid("B54-BT22078-"), 6, "[Your Turn] This Digimon gains all [Main] effects of its digivolution cards with the [Flame] trait.", color: CardColor.Purple);
        AllSupported(top);
        Assert.Contains("【메인】 효과를 전부 얻는다", Ko(top.EffectText));
        var flame = Digimon(Uid("B54-MERA-"), 4, "[Main] [Once Per Turn] By paying 2 cost, delete 1 of your opponent's Digimon with DP less than or equal to this Digimon's DP.", color: CardColor.Red, traits: "Flame");
        var plain = Digimon(Uid("B54-PLAIN-"), 4, "[Main] [Once Per Turn] By paying 2 cost, delete 1 of your opponent's Digimon with DP less than or equal to this Digimon's DP.", color: CardColor.Red);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, top, flame, plain);
        var abilities = engine.GetMainAbilities(state.Player);
        Assert.Contains(abilities, a => ReferenceEquals(a.Digimon, me) && a.Gained && a.Card.CardId == flame.CardId);
        Assert.DoesNotContain(abilities, a => a.Gained && a.Card.CardId == plain.CardId);
        var o = Put(state.DummyOpponent, Digimon("B54-O5", 5));
        Assert.True(engine.ActivateMainAbility(me, flame).CanExecute, Logs(state));
        Assert.DoesNotContain(o, state.DummyOpponent.BattleArea);
        me.IsOwnersTurn = false;
        Assert.DoesNotContain(engine.GetMainAbilities(state.Player), a => a.Gained);
    }

    [Fact]
    public void GainAllTurnsEffectsOfLevel6Sources_EX10_059()
    {
        var top = Digimon(Uid("B54-EX10059-"), 7, "[All Turns] This Digimon gains all [All Turns] effects on all level 6 [Bagra Army] trait Digimon cards in its digivolution cards.", color: CardColor.Purple, dp: 13000);
        AllSupported(top);
        var lv6 = Digimon(Uid("B54-BAG6-"), 6, "[All Turns] This Digimon gets +2000 DP.\r\n[Your Turn] This Digimon gets +1000 DP.", color: CardColor.Purple, traits: "Bagra Army");
        var lv5 = Digimon(Uid("B54-BAG5-"), 5, "[All Turns] This Digimon gets +4000 DP.", color: CardColor.Purple, traits: "Bagra Army");
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, top, lv6, lv5);
        engine.RecalculateFieldBuffs();
        Assert.Equal(15000, me.GetCurrentDP());   // Lv.6의 【서로의 턴】만 (+2000)
    }

    // =====================================================================
    // 5. 발휘하지 않는다
    // =====================================================================

    [Fact]
    public void OpponentTamerOnPlayDoesNotActivate_WhileYouHaveEosmon_BT17_092()
    {
        var menoa = Tamer(Uid("B54-BT17092-"), "[All Turns] While you have [Eosmon], all of your opponent's Tamers [On Play] effects don't activate.", color: CardColor.White);
        AllSupported(menoa);
        foreach (bool eos in new[] { false, true })
        {
            var (state, engine) = NewMainPhase();
            state.DummyOpponent.Tamers.Add(menoa);
            if (eos) Put(state.DummyOpponent, Digimon("B54-EOS", 5, name: "Eosmon"));
            var t = Tamer(Uid("B54-PT-"), "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)", playCost: 1);
            state.Player.Hand.Add(t);
            Assert.True(engine.PlayCard(t).CanExecute, Logs(state));
            Assert.Equal(eos ? 0 : 1, state.Player.Hand.Count);
        }
    }

    [Fact]
    public void Level7WhenDigivolvingDoesNotActivate_BothSides_BT5_085()
    {
        var arma = Digimon(Uid("B54-BT5085-"), 7, "[All Turns] The [When Digivolving] effects on level 7 Digimon don't activate.", color: CardColor.White);
        AllSupported(arma);
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, arma);
        var me = Put(state.Player, Digimon("B54-L6", 6));
        var l7 = Digimon(Uid("B54-L7-"), 7, "[When Digivolving] ＜Draw 1＞ (Draw 1 card from your deck.)");
        var l6b = Digimon(Uid("B54-L6B-"), 6, "[When Digivolving] ＜Draw 1＞ (Draw 1 card from your deck.)");
        state.Player.Hand.Add(l7);
        engine.EvolveByEffect(state.Player, me, l7, 0);
        Assert.Single(state.Player.Hand);   // 진화 드로우만 — Lv.7의 【진화 시】는 발휘하지 않는다
        var me2 = Put(state.Player, Digimon("B54-L5", 5));
        state.Player.Hand.Add(l6b);
        engine.EvolveByEffect(state.Player, me2, l6b, 0);
        Assert.Equal(3, state.Player.Hand.Count);   // 진화 드로우 + Lv.6의 【진화 시】 ≪1 드로우≫
    }

    [Fact]
    public void MinusDpAndWhenDigivolvingDontActivate_P_095()
    {
        var opt = Card(Uid("B54-P095-"), "[Main] Until the end of your opponent's turn, 1 of your opponent's Digimon gets -6000 DP and that Digimon's [When Digivolving] effects don't activate.", CardType.Option, 0, 2, CardColor.Yellow);
        AllSupported(opt);
        Assert.Contains("DP를 -6000 하고, 그 디지몬의 【진화 시】 효과는 발휘하지 않는다", Ko(opt.EffectText));
        var (state, engine) = NewMainPhase();
        var o = Put(state.DummyOpponent, Digimon("B54-O6", 6, dp: 9000));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt);
        engine.RecalculateFieldBuffs();
        Assert.Equal(3000, o.GetCurrentDP());
        Assert.True(CardScripts.HasKeyword(o, Keyword.CannotActivateWhenDigivolving), Logs(state));
    }

    // =====================================================================
    // 6. 디지크로스 재료 · 한글
    // =====================================================================

    [Fact]
    public void DigiXrosMaterialClauses_Supported_WithKorean()
    {
        var snatch = Digimon(Uid("B54-BT18065-"), 4, "While you have no Digimon other than [Vemmon], cards from your trash may also be placed for this card's DigiXros.", color: CardColor.Black);
        var nene = Tamer(Uid("B54-BT19087-"), "[All Turns] When any of your [Composite] or [Twilight] trait Digimon cards with DigiXros requirements would be played, by suspending this Tamer, 1 card under your Tamers and 1 card in your trash can also be placed for their DigiXros.", color: CardColor.Black);
        var kiriha = Tamer(Uid("B54-EX4062-"), "[All Turns] When you would play 1 Digimon card with the [Blue Flare] trait or [Twilight] trait with DigiXros requirements, by suspending this Tamer, you may place 1 card from under your Tamers and 1 card from your trash as digivolution cards for a DigiXros.");
        AllSupported(snatch);
        AllSupported(nene);
        AllSupported(kiriha);
        Assert.Contains("「벰몬」 이외의 자신의 디지몬이 없는 동안", Ko(snatch.EffectText));
        Assert.Contains("테이머 아래와 트래시의 카드도 1장씩", Ko(nene.EffectText));
        var s = CardScripts.For(nene)!;
        Assert.NotNull(s.XrosUnderTamersAndTrash54);
        Assert.True(s.XrosUnderTamersAndTrash54!(Digimon("B54-TW", 5, traits: "Twilight")));
        Assert.False(s.XrosUnderTamersAndTrash54!(Digimon("B54-NTW", 5, traits: "Xros Heart")));
        var trashOk = CardScripts.For(snatch)!.XrosFromTrash!;
        var (state, _) = NewMainPhase();
        Put(state.Player, Digimon("B54-VEM", 3, name: "Vemmon"));
        Assert.True(trashOk(state.Player));
        Put(state.Player, Digimon("B54-NOTVEM", 3));
        Assert.False(trashOk(state.Player));
    }

    [Fact]
    public void Korean_SameWithAndWithoutMemo_Batch54()
    {
        var clauses = new[]
        {
            "[Your Turn] When this Digimon would digivolve into a card with [Greymon] in its name, reduce the cost by 1 for each of that card's colors.",
            "[Start of Your Main Phase] For the turn, when this Digimon would digivolve into a card with [Tyrannomon] in its name, or the [Dinosaur] or [Ceratopsian] trait, reduce the digivolution cost by 1.",
            "[Your Turn] This Digimon gains all [Main] effects of its digivolution cards with the [Flame] trait.",
            "[All Turns] The [When Digivolving] effects on level 7 Digimon don't activate.",
            "[Main] Until the end of your opponent's turn, 1 of your opponent's Digimon gets -6000 DP and that Digimon's [When Digivolving] effects don't activate.",
        };
        var cached = clauses.Select(Ko).ToList();
        Assert.DoesNotContain("<null>", cached);
        KoreanEffectRenderer.MemoEnabled52 = false;
        try { Assert.Equal(clauses.Select(Ko).ToList(), cached); }
        finally { KoreanEffectRenderer.MemoEnabled52 = true; }
    }
}
