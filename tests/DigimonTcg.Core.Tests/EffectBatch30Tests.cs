using System.IO;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.30.0 미지원 문장 4차 묶음: 듀얼 카드 〈아츠 진화〉 · 진화원을 비용으로 옵션 쪽 [메인] (켄타루몬 · 크레니엄몬 · 로드나이트몬 X) ·
/// 이 효과로 등장한 디지몬 (진화 불가 · 턴 종료 시 소멸) · 배틀 에어리어의 옵션 카드 파기 · 디지몬의 효과로 패가 늘어났을 때 ·
/// 비용 "상대의 디지몬을 소멸시키는 것으로" · [타이탄족] 트래시 진화 · 비용 안의 쉼표 목록
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch30Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", bool dual = false, params string[] traits)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = 1,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits.ToList(), IsDual = dual,
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string inherited = "", CardColor color = CardColor.Blue, int dp = -1, string? name = null, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, color: color, dp: dp, name: name, inherited: inherited, traits: traits);

    private static CardData Dual(string id, string optionSide, string main = "", int useCost = 3, CardColor color = CardColor.Yellow, string? name = null) =>
        Card(id, main, CardType.Digimon, 6, useCost, color, 12000, name: name, inherited: optionSide, dual: true);

    /// <summary>메인 페이즈 (메모리 8) — 블루 테이머 1명 · 옐로 테이머 1명 (옵션 색 조건)</summary>
    private static (GameState State, GoldfishEngine Engine) NewMainPhase()
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3 };
        state.MemoryGauge = 8;
        for (int i = 0; i < 8; i++)
        {
            state.Player.MainDeck.Add(Digimon("B30-FILL" + i, 3));
            state.DummyOpponent.MainDeck.Add(Digimon("B30-OFILL" + i, 3));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B30-SEC" + i, 3));
            state.DummyOpponent.SecurityStack.Add(Digimon("B30-OSEC" + i, 3));
        }
        state.Player.Tamers.Add(Card("B30-TAMER", "", CardType.Tamer));
        state.Player.Tamers.Add(Card("B30-TAMER-Y", "", CardType.Tamer, color: CardColor.Yellow));
        return (state, new GoldfishEngine(state));
    }

    private static CardInstance Put(PlayerZones zones, CardData card, bool suspended = false, int turnPlayed = 1, params CardData[] sources)
    {
        var d = new CardInstance(card, turnPlayed) { IsSuspended = suspended };
        foreach (var s in sources) d.AddBottomSource(s);
        zones.BattleArea.Add(d);
        return d;
    }

    private static void Use(GameState state, GoldfishEngine engine, CardData option)
    {
        AssertFull(option);
        state.Player.Hand.Add(option);
        var result = engine.PlayCard(option);
        Assert.True(result.CanExecute, result.Reason);
    }

    private static void AssertFull(CardData card) => Assert.Equal(EffectSupportLevel.Full, EffectSupport.For(card).Level);

    /// <summary>〈아츠 진화〉 데이터 (data/kr/dual_rules.json 형식)를 임시 파일로 읽게 한다 — 끝나면 원래 파일로</summary>
    private static void WithArts(string[] ids, System.Action body)
    {
        string path = Path.Combine(Path.GetTempPath(), "b30-dual-" + System.Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{\"Cards\":[" + string.Join(",", ids.Select(id => $"{{\"CardId\":\"{id}\",\"ArtsDigivolve\":true}}")) + "]}");
        try
        {
            DualCardRules.Reload(path);
            body();
        }
        finally
        {
            DualCardRules.Reload();
            File.Delete(path);
        }
    }

    // =====================================================================
    // 듀얼 카드 〈아츠 진화〉 (종합 룰 2-11 · 4-20, 공식 Q&A Q6313)
    // =====================================================================

    [Fact]
    public void ArtsDigivolve_InsteadOfTrashing_DigivolvesWithoutCost_AndTriggersWhenDigivolving_QnaQ6313() => WithArts(new[] { "B30-ARTS" }, () =>
    {
        var (state, engine) = NewMainPhase();
        var dual = Dual("B30-ARTS", "[Main] Gain 1 memory.", main: "[When Digivolving] Gain 2 memory.", useCost: 3);
        Assert.True(DualCardRules.HasArtsDigivolve(dual));
        var lv5 = Put(state.Player, Digimon("B30-LV5", 5, color: CardColor.Yellow));
        int hand = state.Player.Hand.Count;

        Use(state, engine, dual);

        Assert.Same(dual, lv5.TopCard);                         // 트래시 대신 진화
        Assert.DoesNotContain(dual, state.Player.Trash);
        Assert.Equal(8 - 3 + 1 + 2, state.MemoryGauge);         // 사용 코스트 3, [메인] +1, 진화 코스트 0, [진화 시] +2
        Assert.Equal(hand + 1, state.Player.Hand.Count);         // 진화 보너스 1장 드로우 (패에 넣은 듀얼 카드는 이미 사용)
        Assert.True(engine.LastOptionArtsDigivolved);
    });

    [Fact]
    public void ArtsDigivolve_NoDigimonMeetsTheRequirement_TrashesAsUsual() => WithArts(new[] { "B30-ARTS2" }, () =>
    {
        var (state, engine) = NewMainPhase();
        var dual = Dual("B30-ARTS2", "[Main] Gain 1 memory.");
        Put(state.Player, Digimon("B30-LV4", 4, color: CardColor.Yellow));   // Lv.5가 아니다

        Use(state, engine, dual);

        Assert.Contains(dual, state.Player.Trash);
        Assert.False(engine.LastOptionArtsDigivolved);
    });

    [Fact]
    public void ArtsDigivolve_NotInOfficialData_IsNotGuessed() => WithArts(new[] { "B30-OTHER" }, () =>
    {
        var (state, engine) = NewMainPhase();
        var dual = Dual("B30-NOARTS", "[Main] Gain 1 memory.");
        Put(state.Player, Digimon("B30-LV5B", 5, color: CardColor.Yellow));

        Use(state, engine, dual);

        Assert.Contains(dual, state.Player.Trash);   // 공식 카드 리스트에 〈아츠 진화〉가 확인되지 않은 듀얼 카드는 트래시
    });

    [Fact]
    public void ArtsDigivolve_KoreanDisplay_AddsTheRuleLine() => WithArts(new[] { "B30-ARTS-KO" }, () =>
    {
        var dual = Dual("B30-ARTS-KO", "[Main] Gain 1 memory.");
        Assert.Contains(KoreanCardData.ArtsDigivolveText, KoreanCardData.DisplayInheritedEffectText(dual));
        Assert.DoesNotContain(KoreanCardData.ArtsDigivolveText, KoreanCardData.DisplayInheritedEffectText(Dual("B30-NOARTS-KO", "[Main] Gain 1 memory.")));
    });

    // =====================================================================
    // 진화원을 비용으로 옵션 쪽 [메인] (BT27-043 · BT27-070 · BT27-084)
    // =====================================================================

    [Fact]
    public void Kentaurosmon_SecurityRemoved_PlacesSourceAsBottomSecurity_ThenActivatesOptionSide()
    {
        var (state, engine) = NewMainPhase();
        var kentauros = Dual("B30-KENTA", "[Main] Gain 2 memory.",
            main: "[All Turns] [Once Per Turn] When your security stack is removed from, by placing 1 [Kentaurosmon] or [X Antibody] from this Digimon's digivolution cards as the bottom security card, activate 1 [Main] effect on this card's Option side.",
            name: "Kentaurosmon (X Antibody)");
        AssertFull(kentauros);
        var src = Digimon("B30-KENTA-SRC", 5, name: "Kentaurosmon");
        var d = Put(state.Player, kentauros, sources: src);

        Use(state, engine, Card("B30-TRASH-SEC", "[Main] Trash your top security card."));

        Assert.Same(src, state.Player.SecurityStack[^1]);           // 진화원 → 시큐리티 맨 아래
        Assert.Empty(d.EvolutionSources);
        Assert.Equal(5, state.Player.SecurityStack.Count);          // 1장 파기 · 1장 놓음
        Assert.Equal(8 - 3 + 2, state.MemoryGauge);                 // 옵션 쪽 [메인] 메모리 +2
    }

    [Fact]
    public void Craniamon_TrashesTheSpecifiedSource_OnlyIfPresent()
    {
        var (state, engine) = NewMainPhase();
        var cranium = Dual("B30-CRANI", "[Main] Gain 2 memory.",
            main: "[All Turns] [Once Per Turn] When any of your other Digimon are played or deleted, by trashing 1 [Craniamon] or [X Antibody] from this Digimon's digivolution cards, activate 1 [Main] effect on this card's Option side.");
        AssertFull(cranium);
        // 지정 카드가 진화원에 없으면 비용을 낼 수 없다
        var noSrc = Put(state.Player, cranium, sources: Digimon("B30-OTHER-SRC", 5, name: "Gallantmon"));
        var playMe = Digimon("B30-PLAYME", 3);
        state.Player.Hand.Add(playMe);
        Assert.True(engine.PlayCard(playMe).CanExecute);
        Assert.Single(noSrc.EvolutionSources);
        Assert.Equal(8 - 3, state.MemoryGauge);

        // 다른 디지몬이 등장 → 진화원의 「크레니엄몬」 파기 → 옵션 쪽 [메인]
        var (state2, engine2) = NewMainPhase();
        var src = Digimon("B30-CRANI-SRC", 5, name: "Craniamon");
        var host = Put(state2.Player, cranium, sources: src);
        var playMe2 = Digimon("B30-PLAYME2", 3);
        state2.Player.Hand.Add(playMe2);
        Assert.True(engine2.PlayCard(playMe2).CanExecute);
        Assert.Empty(host.EvolutionSources);
        Assert.Contains(src, state2.Player.Trash);
        Assert.Equal(8 - 3 + 2, state2.MemoryGauge);
    }

    // =====================================================================
    // 이 효과로 등장한 디지몬 (BT23-017 · EX11-022 · P-216 · EX4-063)
    // =====================================================================

    [Fact]
    public void PlayedByThisEffect_IsDeletedAtTurnEnd()
    {
        var (state, engine) = NewMainPhase();
        var puppet = Digimon("B30-PUPPET", 3, traits: "Puppet");
        state.Player.Hand.Add(puppet);
        var host = Put(state.Player, Digimon("B30-PUPHOST", 4, main: "[On Play] You may play 1 [Puppet] trait Digimon card with 4000 DP or less from your hand or trash without paying the cost. At turn end, delete the Digimon this effect played."));
        AssertFull(host.TopCard);
        var player = Digimon("B30-PUPHOST2", 4, main: host.TopCard.EffectText);
        state.Player.Hand.Add(player);

        Assert.True(engine.PlayCard(player).CanExecute);
        var played = Assert.Single(state.Player.BattleArea, d => d.TopCard == puppet);

        engine.PassTurn();

        Assert.DoesNotContain(played, state.Player.BattleArea);
        Assert.Contains(puppet, state.Player.Trash);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard.CardId == "B30-PUPHOST2");   // 효과를 쓴 디지몬은 그대로
    }

    [Fact]
    public void PlayedByThisEffect_CantDigivolve_AndIsDeletedAtTheEndOfOpponentsTurn_NotYours()
    {
        var (state, engine) = NewMainPhase();
        var hudie = Digimon("B30-HUDIE", 3, traits: "Hudie");
        state.Player.Hand.Add(hudie);
        var attacker = Put(state.Player, Digimon("B30-HUDIE-ATK", 5, dp: 9000,
            main: "[When Attacking] [Once Per Turn] You may play 1 play cost 5 or lower Digimon card with the [Hudie] trait from your hand without paying the cost. The Digimon this effect played can't digivolve and is deleted at the end of your opponent's turn."));
        AssertFull(attacker.TopCard);

        engine.AttackSecurityOrDirect(attacker);
        var played = Assert.Single(state.Player.BattleArea, d => d.TopCard == hudie);
        Assert.True(EffectRules.IsDigivolveLocked(state, played));
        var evo = Digimon("B30-HUDIE4", 4);
        state.Player.Hand.Add(evo);
        Assert.False(engine.EvolveDigimon(played, evo).CanExecute);

        engine.PassTurn();   // 자신의 턴 종료 — 아직 소멸하지 않는다 (상대의 턴 종료 시)

        Assert.Contains(played, state.Player.BattleArea);
    }

    [Fact]
    public void PlayedByThisEffect_NothingPlayed_DeletesNothing()
    {
        var (state, engine) = NewMainPhase();
        var keep = Put(state.Player, Digimon("B30-KEEP", 3, traits: "Puppet"));
        Use(state, engine, Card("B30-NOPLAY", "[Main] You may play 1 [Puppet] trait Digimon card with 4000 DP or less from your hand without paying the cost. At turn end, delete the Digimon this effect played."));

        engine.PassTurn();

        Assert.Contains(keep, state.Player.BattleArea);   // 원래 있던 디지몬은 "이 효과로 등장한 디지몬"이 아니다
    }

    // =====================================================================
    // 배틀 에어리어의 옵션 카드 (공식 Q&A BT19-062 · BT23-059: 「배틀 에어리어에 놓는다」로 놓인 옵션)
    // =====================================================================

    [Fact]
    public void TrashingAPlacedOptionAsCost_TriggersItsTrashedFromBattleAreaEffect()
    {
        var (state, engine) = NewMainPhase();
        var device = Card("B30-DEVICE", "When this card is trashed from the battle area, gain 2 memory.\r\n[Main] Gain 1 memory. Then, place this card in the battle area.");
        AssertFull(device);
        Use(state, engine, device);
        Assert.Contains(state.Player.OptionsInPlay, p => p.Card == device);
        Assert.Equal(8 - 3 + 1, state.MemoryGauge);

        var user = Put(state.Player, Digimon("B30-DEVUSER", 5, main: "[When Attacking] By trashing 1 of your Option cards in the battle area, gain 1 memory."));
        AssertFull(user.TopCard);
        engine.AttackSecurityOrDirect(user);

        Assert.DoesNotContain(state.Player.OptionsInPlay, p => p.Card == device);
        Assert.Contains(device, state.Player.Trash);
        Assert.Equal(8 - 3 + 1 + 1 + 2, state.MemoryGauge);
    }

    [Fact]
    public void WhenOptionCardsInTheBattleAreaAreTrashed_EitherSide()
    {
        var (state, engine) = NewMainPhase();
        var watcher = Put(state.Player, Digimon("B30-WATCH", 5, main: "[All Turns] [Once Per Turn] When Option cards in the battle area are trashed, this Digimon unsuspends."), suspended: true);
        AssertFull(watcher.TopCard);
        state.DummyOpponent.OptionsInPlay.Add(new PlacedOption { Card = Card("B30-OPP-OPT"), PlacedTurn = 1 });

        Use(state, engine, Card("B30-TRASH-OPP-OPT", "[Main] Trash 1 of your opponent's Option cards in the battle area."));

        Assert.Empty(state.DummyOpponent.OptionsInPlay);
        Assert.False(watcher.IsSuspended);
    }

    [Fact]
    public void Device_IgnoresColorRequirements_WhileTheNamedCardIsNotInTheBattleArea()
    {
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Clear();
        var queen = Card("B30-KNIGHT-DEV", "While you don't have [Queen Device] in the battle area, you may ignore this card's color requirements.\r\n[Main] Gain 1 memory.", color: CardColor.Purple);
        AssertFull(queen);
        state.Player.Hand.Add(queen);
        Assert.True(engine.PlayCard(queen).CanExecute);

        var (state2, engine2) = NewMainPhase();
        state2.Player.Tamers.Clear();
        state2.Player.OptionsInPlay.Add(new PlacedOption { Card = Card("B30-QUEEN", name: "Queen Device"), PlacedTurn = 1 });
        var queen2 = Card("B30-KNIGHT-DEV2", queen.EffectText, color: CardColor.Purple);
        state2.Player.Hand.Add(queen2);
        Assert.False(engine2.PlayCard(queen2).CanExecute);   // 놓인 옵션도 "배틀 에어리어에 있다"
    }

    // =====================================================================
    // 비용 "by deleting 1 of your opponent's ~" (공식 Q&A EX13-015 Q7247 · BT24-018 Q5599)
    // =====================================================================

    private const string DeleteOpponentToStay = "[All Turns] [Once Per Turn] When this Digimon would leave the battle area other than by your effects, by deleting 1 of your opponent's 9000 DP or lower Digimon, it doesn't leave.";

    [Fact]
    public void DeletingOpponentsDigimonAsCost_PreventsLeaving()
    {
        var (state, engine) = NewMainPhase();
        var guard = Put(state.DummyOpponent, Digimon("B30-STAY", 6, main: DeleteOpponentToStay));
        AssertFull(guard.TopCard);
        var victim = Put(state.Player, Digimon("B30-VICTIM", 4, dp: 5000));

        Use(state, engine, Card("B30-DEL-6", "[Main] Delete 1 of your opponent's level 6 or lower Digimon."));

        Assert.Contains(guard, state.DummyOpponent.BattleArea);
        Assert.DoesNotContain(victim, state.Player.BattleArea);
    }

    [Fact]
    public void DeletingOpponentsDigimonAsCost_Prevented_ThenItLeaves_QnaQ7247()
    {
        var (state, engine) = NewMainPhase();
        var guard = Put(state.DummyOpponent, Digimon("B30-STAY2", 6, main: DeleteOpponentToStay));
        // 소멸하지 않는 디지몬 (≪회피≫ — 레스트시켜 소멸을 막는다)
        var victim = Put(state.Player, Digimon("B30-EVADER", 4, dp: 5000, main: "＜Evade＞ (When this Digimon would be deleted, you may suspend it to prevent that deletion.)"));

        Use(state, engine, Card("B30-DEL-6B", "[Main] Delete 1 of your opponent's level 6 or lower Digimon."));

        Assert.Contains(victim, state.Player.BattleArea);
        Assert.DoesNotContain(guard, state.DummyOpponent.BattleArea);   // "~하는 것으로"를 채우지 못해 벗어난다
    }

    // =====================================================================
    // "When one of your Digimon's effects adds cards to your hand" (공식 Q&A BT15-002 · BT15-083)
    // =====================================================================

    private const string DigimonHandAdd = "[Your Turn] When one of your Digimon's effects adds cards to your hand, gain 1 memory.";

    [Fact]
    public void DigimonEffectAddsToHand_Triggers_EvenIfAHandCardWasThenTrashed_QnaBT15002()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B30-HANDWATCH", 4, main: DigimonHandAdd));
        var drawer = Digimon("B30-DRAWER", 3, main: "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.) Then, trash 1 card in your hand.");
        AssertFull(drawer);
        state.Player.Hand.Add(drawer);

        Assert.True(engine.PlayCard(drawer).CanExecute);

        Assert.Equal(8 - 3 + 1, state.MemoryGauge);
    }

    [Fact]
    public void OptionEffectAddsToHand_DoesNotTrigger()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B30-HANDWATCH2", 4, main: DigimonHandAdd));

        Use(state, engine, Card("B30-OPT-DRAW", "[Main] ＜Draw 1＞ (Draw 1 card from your deck.)"));

        Assert.Equal(8 - 3, state.MemoryGauge);
    }

    // =====================================================================
    // [타이탄족]: "When your hand is trashed from, this [Titan] trait Digimon may digivolve into ~ in the trash" (BT24 · BT26)
    // =====================================================================

    [Fact]
    public void Titan_HandTrashed_DigivolvesIntoATitanCardInTheTrash_WithCostReduced()
    {
        var (state, engine) = NewMainPhase();
        var titan = Put(state.Player, Digimon("B30-TITAN4", 4, color: CardColor.Blue, traits: "Titan",
            main: "[Your Turn] [Once Per Turn] When your hand is trashed from, this [Demon] or [Titan] trait Digimon may digivolve into [Titamon] or a [Titan] trait Digimon card in the trash with the digivolution cost reduced by 1."));
        AssertFull(titan.TopCard);
        var evo = Digimon("B30-TITAN5", 5, color: CardColor.Blue, traits: "Titan");
        evo.EvoCost = 3;
        state.Player.Trash.Add(evo);
        state.Player.Hand.Add(Digimon("B30-JUNK", 3));

        Use(state, engine, Card("B30-DISCARD", "[Main] Trash 1 card in your hand."));

        Assert.Same(evo, titan.TopCard);
        Assert.Equal(8 - 3 - 2, state.MemoryGauge);   // 진화 코스트 3 - 1
    }

    [Fact]
    public void Titan_WithoutTheTrait_DoesNotDigivolve()
    {
        var (state, engine) = NewMainPhase();
        var notTitan = Put(state.Player, Digimon("B30-NOTTITAN", 4, traits: "Dragon",
            main: "[Your Turn] [Once Per Turn] When your hand is trashed from, this [Titan] trait Digimon may digivolve into [Titamon] or a [Titan] trait Digimon card in the trash with the cost reduced by 1."));
        AssertFull(notTitan.TopCard);
        state.Player.Trash.Add(Digimon("B30-TITAN5B", 5, traits: "Titan"));
        state.Player.Hand.Add(Digimon("B30-JUNK2", 3));

        Use(state, engine, Card("B30-DISCARD2", "[Main] Trash 1 card in your hand."));

        Assert.Equal("B30-NOTTITAN", notTitan.TopCard.CardId);
    }

    // =====================================================================
    // 테이머에서 진화 (하이브리드체, 공식 Q&A BT4-011 · BT7-011 · BT12-012)
    // =====================================================================

    private static CardData Hybrid(string id, string tamerText, int evoCost = 2, string main = "")
    {
        var card = Digimon(id, 4, main: tamerText + (main.Length > 0 ? "\r\n" + main : ""), color: CardColor.Red, traits: "Hybrid");
        card.EvoCost = evoCost;
        return card;
    }

    private const string RedTamerHybrid = "You may digivolve this card from your hand onto one of your red Tamers as if the Tamer is a level 3 red Digimon.";

    [Fact]
    public void TamerEvolution_TamerBecomesTheBottomSource_DrawsAndTriggersWhenDigivolving_QnaBT4011()
    {
        var (state, engine) = NewMainPhase();
        var red = Card("B30-RED-TAMER", "", CardType.Tamer, color: CardColor.Red);
        state.Player.Tamers.Add(red);   // 이전 턴부터 있던 테이머
        state.Player.UnderTamers.Clear();
        Put(state.Player, Digimon("B30-EVOWATCH", 4, main: "[Your Turn] When one of your Digimon digivolves, gain 1 memory."));
        var agu = Hybrid("B30-AGUNI", RedTamerHybrid, main: "[When Digivolving] Gain 2 memory.");
        AssertFull(agu);
        state.Player.Hand.Add(agu);
        int hand = state.Player.Hand.Count;

        var result = engine.EvolveTamer(red, agu);

        Assert.True(result.CanExecute, result.Reason);
        Assert.DoesNotContain(red, state.Player.Tamers);
        var d = Assert.Single(state.Player.BattleArea, x => x.TopCard == agu);
        Assert.Equal(new[] { red }, d.GetEvolutionStackBottomUp());
        Assert.Equal(8 - 2 + 2 + 1, state.MemoryGauge);       // 진화 코스트 2 · [진화 시] +2 · "디지몬이 진화했을 때" +1 (Q&A: 디지몬이 진화하는 것으로 취급)
        Assert.Equal(hand - 1 + 1, state.Player.Hand.Count);    // 진화 보너스 드로우
        Assert.True(ActionValidator.ValidateAttack(state, d).CanExecute);   // 이번 턴에 나온 테이머가 아니면 어택할 수 있다 (종합 룰 3-4-5)
    }

    [Fact]
    public void TamerEvolution_MemoryCostOverride_AndWrongColorTamer()
    {
        var (state, engine) = NewMainPhase();
        var blue = state.Player.Tamers[0];   // 블루 테이머
        var burning = Hybrid("B30-BURNING", "You may digivolve this card from your hand onto one of your red Tamers as if the Tamer is a level 3 red Digimon for a memory cost of 2.", evoCost: 3);
        AssertFull(burning);
        state.Player.Hand.Add(burning);
        Assert.False(engine.EvolveTamer(blue, burning).CanExecute);

        var red = Card("B30-RED-TAMER2", "", CardType.Tamer, color: CardColor.Red);
        state.Player.Tamers.Add(red);
        Assert.True(engine.EvolveTamer(red, burning).CanExecute);
        Assert.Equal(8 - 2, state.MemoryGauge);
    }

    [Fact]
    public void TamerEvolution_TamerPlayedThisTurn_CantAttack_SuspendedStaysSuspended()
    {
        var (state, engine) = NewMainPhase();
        var red = Card("B30-RED-NEW", "", CardType.Tamer, color: CardColor.Red, playCost: 1);
        state.Player.Hand.Add(red);
        Assert.True(engine.PlayCard(red).CanExecute);
        var agu = Hybrid("B30-AGUNI2", RedTamerHybrid);
        state.Player.Hand.Add(agu);

        Assert.True(engine.EvolveTamer(red, agu).CanExecute);
        var d = Assert.Single(state.Player.BattleArea, x => x.TopCard == agu);
        Assert.False(ActionValidator.ValidateAttack(state, d).CanExecute);   // 이번 턴에 나온 테이머

        var (state2, engine2) = NewMainPhase();
        var red2 = Card("B30-RED-REST", "", CardType.Tamer, color: CardColor.Red);
        state2.Player.Tamers.Add(red2);
        state2.Player.SuspendedTamers.Add(red2);
        var agu2 = Hybrid("B30-AGUNI3", RedTamerHybrid);
        state2.Player.Hand.Add(agu2);
        Assert.True(engine2.EvolveTamer(red2, agu2).CanExecute);
        Assert.True(Assert.Single(state2.Player.BattleArea, x => x.TopCard == agu2).IsSuspended);
        Assert.Empty(state2.Player.SuspendedTamers);
    }

    [Fact]
    public void TamerEvolution_NamedTamer_AndCardsUnderTheOnlyTamerBecomeSources()
    {
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Clear();
        var takuya = Card("B30-TAKUYA", "", CardType.Tamer, color: CardColor.Red, name: "Takuya Kanbara");
        state.Player.Tamers.Add(takuya);
        var under = Digimon("B30-UNDER", 3);
        state.Player.UnderTamers.Add(under);
        var agu = Hybrid("B30-AGUNI4", "You may digivolve this card from your hand onto one of your [Takuya Kanbara]s as if the Tamer is a level 3 red Digimon.");
        AssertFull(agu);
        state.Player.Hand.Add(agu);

        Assert.True(engine.EvolveTamer(takuya, agu).CanExecute);

        var d = Assert.Single(state.Player.BattleArea, x => x.TopCard == agu);
        Assert.Equal(new[] { under, takuya }, d.GetEvolutionStackBottomUp());
        Assert.Empty(state.Player.UnderTamers);
    }

    [Fact]
    public void TamerEvolution_Ai_DigivolvesItsTamerInItsTurn()
    {
        var (state, engine) = NewMainPhase();
        state.CurrentStage = PracticeStage.Stage3_AiBattle;
        var red = Card("B30-AI-RED", "", CardType.Tamer, color: CardColor.Red);
        state.DummyOpponent.Tamers.Add(red);
        var agu = Hybrid("B30-AI-AGUNI", RedTamerHybrid);
        state.DummyOpponent.Hand.Add(agu);

        engine.PassTurn();

        Assert.DoesNotContain(red, state.DummyOpponent.Tamers);
        // 진화한 디지몬은 어택 뒤 배틀로 소멸했을 수도 있다
        Assert.True(state.DummyOpponent.BattleArea.Any(d => d.TopCard == agu) || state.DummyOpponent.Trash.Contains(agu));
        Assert.Contains(state.ActionLogs, l => l.Contains("AI 테이머에서 진화"));
    }

    [Fact]
    public void TamerEvolution_KoreanOfficialWording()
    {
        Assert.Equal("패의 이 카드는, 레드인 자신의 테이머를 레드인 Lv.3의 디지몬으로서 취급하여 진화할 수 있다.", KoreanEffectRenderer.RenderClause(RedTamerHybrid));
        Assert.Equal("패의 이 카드는, 블루인 자신의 테이머를 블루인 Lv.3의 디지몬으로서 취급하여 진화 코스트 2를 지불하여 진화할 수 있다.",
            KoreanEffectRenderer.RenderClause("You may digivolve this card from your hand onto one of your blue Tamers as if the Tamer is a level 3 blue Digimon for a memory cost of 2."));
    }

    // =====================================================================
    // "add N to this effect's level / play cost / DP maximum" (공식 BT18-029 · BT21-078 · BT27-043) · "it further gets" (BT26-031)
    // =====================================================================

    [Fact]
    public void LevelMaximum_GrowsForEachOfYourOtherDigimon()
    {
        var (state, engine) = NewMainPhase();
        var lv5 = Put(state.DummyOpponent, Digimon("B30-OPP-LV5", 5));
        Put(state.Player, Digimon("B30-MINE1", 3));
        var bounce = Digimon("B30-BOUNCE", 4, main: "[On Play] Return 1 of your opponent's level 4 or lower Digimon to the hand. For each of your other Digimon, add 1 to this effect's level maximum.");
        AssertFull(bounce);
        state.Player.Hand.Add(bounce);

        Assert.True(engine.PlayCard(bounce).CanExecute);

        Assert.DoesNotContain(lv5, state.DummyOpponent.BattleArea);   // 다른 자신의 디지몬 1마리 → Lv.5까지
        Assert.Contains(lv5.TopCard, state.DummyOpponent.Hand);
    }

    [Fact]
    public void LevelMaximum_IfTamersHaveTwoColors()
    {
        var (state, engine) = NewMainPhase();   // 블루 · 옐로 테이머 = 2색
        var lv5 = Put(state.DummyOpponent, Digimon("B30-OPP-LV5B", 5));
        Use(state, engine, Card("B30-DEL-MAX", "[Main] Delete 1 of your opponent's level 4 or lower Digimon. If your Tamers have 2 or more total colors, add 1 to this effect's level maximum."));
        Assert.DoesNotContain(lv5, state.DummyOpponent.BattleArea);

        var (state2, engine2) = NewMainPhase();
        state2.Player.Tamers.RemoveAt(1);   // 1색
        var lv5b = Put(state2.DummyOpponent, Digimon("B30-OPP-LV5C", 5));
        Use(state2, engine2, Card("B30-DEL-MAX2", "[Main] Delete 1 of your opponent's level 4 or lower Digimon. If your Tamers have 2 or more total colors, add 1 to this effect's level maximum."));
        Assert.Contains(lv5b, state2.DummyOpponent.BattleArea);
    }

    [Fact]
    public void DpReduction_GrowsForEachCardInBothSecurityStacks()
    {
        var (state, engine) = NewMainPhase();   // 시큐리티 5 + 5
        var big = Put(state.DummyOpponent, Digimon("B30-BIG", 7, dp: 29000));
        Use(state, engine, Card("B30-DPRED", "[Main] 1 of your opponent's Digimon gets -20000 DP until their turn ends. For each card in both players' security stacks, add 1000 to this effect's DP reduction."));
        Assert.DoesNotContain(big, state.DummyOpponent.BattleArea);   // -30000 → DP 0 소멸
    }

    [Fact]
    public void ItFurtherGets_SameTargetAndDuration_AfterTheCost()
    {
        var (state, engine) = NewMainPhase();
        var target = Put(state.DummyOpponent, Digimon("B30-FURTHER", 7, dp: 15000));
        Use(state, engine, Card("B30-FURTHER-OPT", "[Main] 1 of your opponent's Digimon gets -8000 DP until their turn ends. By trashing your top security card, it further gets -5000 DP."));
        Assert.Equal(15000 - 13000, target.GetCurrentDP());
        Assert.Equal(4, state.Player.SecurityStack.Count);
    }

    [Fact]
    public void WhenYourTraitOptionIsPlacedInTheBattleArea()
    {
        var (state, engine) = NewMainPhase();
        Put(state.Player, Digimon("B30-PLACEWATCH", 4, main: "[Your Turn] When any of your [CS] trait Option cards are placed in the battle area, gain 2 memory."));
        Use(state, engine, Card("B30-CS-DELAY", "[Main] Gain 1 memory. Then, place this card in the battle area.", traits: "CS"));
        Assert.Equal(8 - 3 + 1 + 2, state.MemoryGauge);
    }

    // =====================================================================
    // 비용 안의 쉼표 목록 · 원문 오타
    // =====================================================================

    [Fact]
    public void CostWithACommaSeparatedTraitList_IsRead()
    {
        var (state, engine) = NewMainPhase();
        var card = Digimon("B30-COMMA", 3, main: "[On Play] By trashing 1 card with the [Demon], [Shaman] or [Titan] trait from your hand, ＜Draw 2＞ (Draw 2 cards from your deck.)");
        AssertFull(card);
        state.Player.Hand.Add(Digimon("B30-SHAMAN", 3, traits: "Shaman"));
        state.Player.Hand.Add(Digimon("B30-KEEP1", 4));
        state.Player.Hand.Add(Digimon("B30-KEEP2", 4));
        state.Player.Hand.Add(card);
        int hand = state.Player.Hand.Count;

        Assert.True(engine.PlayCard(card).CanExecute);

        Assert.Contains(state.Player.Trash, c => c.CardId == "B30-SHAMAN");
        Assert.Equal(hand - 1 - 1 + 2, state.Player.Hand.Count);
    }

    // =====================================================================
    // 생성 한글 (공식 표기)
    // =====================================================================

    [Theory]
    [InlineData("[Your Turn] When one of your Digimon's effects adds cards to your hand, by suspending this Tamer, gain 1 memory.",
        "【자신의 턴】 자신의 패가 자신의 디지몬의 효과로 늘어났을 때, 이 테이머를 레스트시키는 것으로, 메모리를 +1 한다.")]
    [InlineData("[When Attacking] Trash 1 of your Option cards in the battle area.", "【어택 시】 배틀 에어리어의 자신의 옵션 카드 1장을 파기한다.")]
    [InlineData("[When Digivolving] [When Attacking] [Once Per Turn] By trashing 1 Option card in the battle area, unsuspend this Digimon.",
        "【진화 시】 【어택 시】 [턴에 1회] 배틀 에어리어의 옵션 카드 1장을 파기하는 것으로, 이 디지몬을 액티브로 한다.")]
    [InlineData("When this card is trashed from the battle area, until the end of your opponent's turn, 1 of their Digimon or Tamers can't suspend.",
        "이 카드가 배틀 에어리어에서 파기되었을 때, 상대의 턴 종료 시까지, 상대의 디지몬 또는 테이머 1마리는 레스트할 수 없다.")]
    [InlineData("[When Attacking] [Once Per Turn] You may play 1 play cost 5 or lower Digimon card with the [Hudie] trait from your hand without paying the cost. The Digimon this effect played can't digivolve and is deleted at the end of your opponent's turn.",
        "【어택 시】 [턴에 1회] 자신의 패에서 등장 코스트 5 이하인 특징으로 「후디에」를 가진 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다. 이 효과로 등장한 디지몬은 진화할 수 없으며, 상대의 턴 종료 시, 소멸한다.")]
    [InlineData("[All Turns] [Once Per Turn] When your security stack is removed from, by placing 1 [Kentaurosmon] or [X Antibody] from this Digimon's digivolution cards as the bottom security card, activate 1 [Main] effect on this card's Option side.",
        "【서로의 턴】 [턴에 1회] 자신의 시큐리티에서 카드가 없어졌을 때, 이 디지몬의 진화원에서 「슬레이프몬」/「X항체」 1장을 시큐리티 아래에 놓는 것으로, 이 카드의 옵션 쪽의 【메인】 효과를 1개 발휘한다.")]
    [InlineData("[All Turns] When this Digimon would be deleted, you may trash 2 level 5 Digimon cards in this Digimon's digivolution cards to prevent this Digimon from being deleted.",
        "【서로의 턴】 이 디지몬이 소멸할 때, 이 디지몬의 진화원에서 Lv.5인 디지몬 카드 2장을 파기하는 것으로, 이 디지몬은 소멸하지 않는다.")]
    [InlineData("[On Play] By trashing 1 card with the [Demon], [Shaman] or [Titan] trait from your hand, ＜Draw 2＞ (Draw 2 cards from your deck.)",
        "【등장 시】 자신의 패에서 특징으로 「귀인형」/「신인형」/「타이탄족」을 가진 카드 1장을 파기하는 것으로, 《2 드로우》 (자신의 덱에서 카드를 2장 드로우한다).")]
    public void KoreanRender_NewSentences(string en, string ko) =>
        Assert.Equal(ko, KoreanEffectRenderer.RenderClause(en));

    [Fact]
    public void KoreanRender_TitanTrashDigivolve()
    {
        var ko = KoreanEffectRenderer.RenderClause("[Your Turn] [Once Per Turn] When your hand is trashed from, this [Titan] trait Digimon may digivolve into [Titamon] or a [Titan] trait Digimon card in the trash with the cost reduced by 1.");
        Assert.NotNull(ko);
        Assert.Contains("자신의 패가 파기되었을 때, 특징으로 「타이탄족」을 가진 이 디지몬을 트래시의", ko);
        Assert.Contains("진화 코스트를 -1 하고 진화시킬 수 있다", ko);
    }
}
