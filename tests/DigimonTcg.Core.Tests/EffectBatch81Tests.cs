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
/// v0.81.0 55차 묶음: 영어 API 수치 · 문구 오류를 공식 값으로 (공식 영어 카드 리스트 + 공식 한글이 같을 때만 — 메모리에서만, 캐시 파일은 API 값) ·
/// "When an effect adds cards to your opponent's hand" 카드 전부를 효과 하나로 상대의 패가 늘어났을 때로 (드로우 · 서치 포함 — 공식 Q&amp;A BT10-077 Q1 · EX7-013 Q1) ·
/// 공식 한글 옛 표기 특수 진화 조건 "진화:「X」에서 N" · 여러 술어 효과 로그의 한글 · 요약체 한국 사이트 문구
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch81Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", int evoCost = 1)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited,
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, string inherited = "", int evoCost = 1) =>
        Card(id, main, CardType.Digimon, level, 3, color, dp, name, inherited, evoCost);

    private static CardData Tamer(string id, string main = "", CardColor color = CardColor.Blue) => Card(id, main, CardType.Tamer, 0, 3, color);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B81-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B81-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B81-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B81-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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
    private static string Logs(GameState s) => string.Join(" | ", s.ActionLogs);

    private static void AllSupported(CardData card) =>
        Assert.All(EffectSupport.For(card).Clauses, c => Assert.True(c.Support == ClauseSupport.Supported, c.Text + " | " + c.Note));

    /// <summary>공식 영어 카드 리스트 · 공식 한글 데이터를 이 내용으로 바꿔 실행한다</summary>
    private static void WithOfficialData(IEnumerable<EnglishCardRule> english, IEnumerable<KoreanCard> korean, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b81_" + Guid.NewGuid().ToString("N"));
        var kr = System.IO.Path.Combine(dir, "kr");
        System.IO.Directory.CreateDirectory(kr);
        var file = System.IO.Path.Combine(kr, "cards_ko.json");
        var ids = korean.Select(e => e.CardId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Where(k => !ids.Contains(k.CardId)).Concat(korean).ToList()));
        try { KoreanCardData.Reload(file); EnglishCardRules.Use(english); run(); }
        finally { KoreanCardData.Reload(); EnglishCardRules.Reload(); EvoRequirements48.ClearCache(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 1. 영어 API 수치 오류 → 공식 값 (공식 영어 + 공식 한글이 같을 때만)
    // =====================================================================

    [Fact]
    public void Stats_OfficialEnglishAndKoreanAgree_OverridesPlayCostAndDp()
    {
        string a = Uid("B81-COST-"), b = Uid("B81-DP-");
        WithOfficialData(
            new[] { new EnglishCardRule { CardId = a, PlayCost = 12, DP = 11000 }, new EnglishCardRule { CardId = b, PlayCost = 7, DP = 12000 } },
            new[] { new KoreanCard { CardId = a, Name = "알파", Type = "디지몬", PlayCost = "12", DP = "11000" }, new KoreanCard { CardId = b, Name = "반쵸", Type = "디지몬", PlayCost = "7", DP = "12000" } },
            () =>
            {
                var alpha = Digimon(a, 6, dp: 11000); alpha.PlayCost = 0;      // BT20-056 모양: API 등장 코스트 0
                var bancho = Digimon(b, 6, dp: 12); bancho.PlayCost = 7;       // BT23-036 모양: API DP 12
                var ch1 = OfficialCardData81.Apply(alpha);
                var ch2 = OfficialCardData81.Apply(bancho);
                Assert.Equal(12, alpha.PlayCost);
                Assert.Equal(12000, bancho.DP);
                Assert.Single(ch1); Assert.Equal("등장 코스트", ch1[0].Field); Assert.Equal("0", ch1[0].Before); Assert.Equal("12", ch1[0].After);
                Assert.Single(ch2); Assert.Equal("DP", ch2[0].Field);
            });
    }

    [Fact]
    public void Stats_KoreanDisagrees_OrMissingWithNonZeroApi_KeepsApiValue()
    {
        string a = Uid("B81-P058-"), b = Uid("B81-NOKR-"), c = Uid("B81-ZERO-"), egg = Uid("B81-EGG-");
        WithOfficialData(
            new[]
            {
                new EnglishCardRule { CardId = a, DP = 7000 },                 // P-058 모양: 공식 영어 리스트의 틀린 DP (공식 한글 · API 4000)
                new EnglishCardRule { CardId = b, DP = 7000 },                 // 한글 데이터 없음 + API 값 있음 → 그대로
                new EnglishCardRule { CardId = c, PlayCost = 6, DP = 6000 },   // 한글 데이터 없음 + API 0 (BT24-028 모양) → 공식 영어
                new EnglishCardRule { CardId = egg, DP = 1000 },               // 디지타마 DP는 보지 않는다 (BT22-001)
            },
            new[] { new KoreanCard { CardId = a, Name = "감마", Type = "디지몬", PlayCost = "4", DP = "4000" } },
            () =>
            {
                var gamma = Digimon(a, 3, dp: 4000);
                var noKr = Digimon(b, 5, dp: 5000);
                var zero = Digimon(c, 5, dp: 0); zero.PlayCost = 0;
                var e = Card(egg, "", CardType.Digitama, 2, 0, dp: 0);
                Assert.Empty(OfficialCardData81.Apply(gamma));
                Assert.Empty(OfficialCardData81.Apply(noKr));
                Assert.Equal(2, OfficialCardData81.Apply(zero).Count);
                Assert.Empty(OfficialCardData81.Apply(e));
                Assert.Equal(4000, gamma.DP);
                Assert.Equal(5000, noKr.DP);
                Assert.Equal((6, 6000), (zero.PlayCost, zero.DP));
                Assert.Equal(0, e.DP);
            });
    }

    [Fact]
    public void Stats_CatalogUsesOfficialValues_ButCacheFileKeepsApiValues()
    {
        string id = Uid("B81-CAT-");
        WithOfficialData(new[] { new EnglishCardRule { CardId = id, PlayCost = 9 } }, new[] { new KoreanCard { CardId = id, Name = "왕룡검", Type = "디지몬", PlayCost = "9" } }, () =>
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b81cat_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                var api = Digimon(id, 6, "[On Play] Gain 1 memory.", dp: 12000); api.PlayCost = 6;   // BT20-060 모양
                string cache = System.IO.Path.Combine(dir, "cards_cache.json");
                System.IO.File.WriteAllText(cache, System.Text.Json.JsonSerializer.Serialize(new[] { api }));
                var svc = new CardCatalogService(dir);
                Assert.Equal(9, svc.Catalog.First(x => x.CardId == id).PlayCost);
                Assert.Contains(svc.OfficialChanges81, x => x.CardId == id && x.Before == "6" && x.After == "9");
                svc.SaveCache();
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(cache));
                var saved = doc.RootElement.EnumerateArray().First(x => x.GetProperty("CardId").GetString() == id);
                Assert.Equal(6, saved.GetProperty("PlayCost").GetInt32());   // 파일에는 API 값 그대로
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
        });
    }

    // =====================================================================
    // 2. 효과 시점 에라타 · ACE 진화원 효과 (카드 번호 + API 문장이 정확히 같을 때만)
    // =====================================================================

    private static CardData Fixed(string id, string main, string inherited = "", CardType type = CardType.Digimon, int level = 5)
    {
        var c = Card(id, main, type, level, 5, inherited: inherited);
        WithOfficialData(Array.Empty<EnglishCardRule>(), Array.Empty<KoreanCard>(), () => OfficialCardData81.Apply(c));
        return c;
    }

    [Fact]
    public void Text_TimingErrata_ReplacedOnlyOnExactApiText()
    {
        // API 문장에는 줄바꿈 없는 공백(U+00A0)이 섞여 있다
        var bt6 = Fixed("BT6-066", "＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.)\r\n[Your Turn] [Once Per Turn] When one of your other Digimon is deleted, ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)");
        Assert.Contains("[Opponent's Turn] [Once Per Turn] When one of your other Digimon is deleted", bt6.EffectText);
        Assert.StartsWith("＜Reboot＞", bt6.EffectText);
        var st22 = Fixed("ST22-03", "[When Moving] [On Play] Reveal the top 3 cards of your deck. Add 1 card ~");
        Assert.StartsWith("[When Moving] [When Digivolving] Reveal", st22.EffectText);
        var ex6 = Fixed("EX6-022", "", "[All Turns] While this Digimon has the [Angel] or [Three Great Angels] trait, it gains ＜Alliance＞ (When ~)");
        Assert.StartsWith("[Your Turn] While this Digimon has the [Angel]", ex6.InheritedEffectText);
        // 다른 카드 번호 · 다른 문장은 그대로
        var other = Fixed(Uid("B81-OTHER-"), "[When Moving] [On Play] Reveal the top 3 cards of your deck.");
        Assert.StartsWith("[When Moving] [On Play]", other.EffectText);
        var changed = Fixed("ST22-03", "[When Moving] [On Play] Reveal the top 5 cards of your deck.");
        Assert.StartsWith("[When Moving] [On Play] Reveal the top 5", changed.EffectText);
    }

    [Fact]
    public void Text_OpponentsTurnTiming_PileVolcamon_TriggersOnlyInOwnersOpponentsTurn_BT6_066()
    {
        var fixedText = Fixed("BT6-066", "[Your Turn] [Once Per Turn] When one of your other Digimon is deleted, ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)").EffectText;
        var pile = Digimon(Uid("B81-PILE-"), 5, fixedText, dp: 6000);
        AllSupported(pile);
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, pile);
        var victim = Put(state.DummyOpponent, Digimon("B81-VICTIM", 3, dp: 2000));
        var mine = Put(state.Player, Digimon("B81-MINE", 5, dp: 6000), Digimon("B81-MSRC", 4));
        var killer = Digimon(Uid("B81-KILL-"), 4, "[On Play] Delete 1 of your opponent's Digimon with 3000 DP or less.");
        var k = Put(state.Player, killer);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, k, killer);   // 플레이어의 턴 = 필레볼카몬 주인의 상대의 턴
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);
        Assert.Empty(mine.EvolutionSources);                                   // ≪퇴화 1≫
    }

    [Fact]
    public void Text_AceInheritedEffects_AppendedAfterOverflow()
    {
        string overflow3 = "Ace Overflow ＜-3＞ (As this card moves from the field or under a card to an area other than those, lose 3 memory.)";
        var rapid = Fixed("BT19-050", "", overflow3);
        Assert.StartsWith(overflow3, rapid.InheritedEffectText);
        Assert.EndsWith("[Your Turn] This Digimon gets +4000 DP.", rapid.InheritedEffectText);
        var blitz = Fixed("EX9-013", "", overflow3.Replace("-3", "-4").Replace("lose 3", "lose 4"));
        KeywordDictionary.ApplyDataKeywords(blitz);
        Assert.Equal(1, blitz.InheritedSecurityAttackBonus);                    // 진화원 ≪시큐리티 어택 +1≫

        // 진화원으로 있을 때 자신의 턴 DP +4000 (BT19-050 공식 진화원 효과)
        var src = Digimon(Uid("B81-RAPID-"), 6, inherited: rapid.InheritedEffectText);
        AllSupported(src);
        var (state, engine) = NewMainPhase();
        var top = Put(state.Player, Digimon("B81-TOP", 7, dp: 12000), src);
        engine.GetType().GetMethod("RecalculateFieldBuffs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!.Invoke(engine, null);
        Assert.Equal(16000, top.GetCurrentDP());
    }

    [Fact]
    public void Text_MainTimingEvoCostReduction_IsSupported_BT5_092()
    {
        var nokia = Fixed("BT5-092", "[On Play] You may play 1 [Agumon] or [Gabumon] from your hand without paying the cost.\r\n[Your Turn] When one of your Digimon would digivolve into a Digimon card in your hand with [Greymon], [Garurumon] or [Omnimon] in its name, by suspending this Tamer, reduce the digivolution cost by 1.",
            type: CardType.Tamer, level: 0);
        Assert.Contains("[Main] When one of your Digimon would digivolve", nokia.EffectText);
        Assert.Contains("(other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon])", nokia.EffectText);
        var t = Tamer(Uid("B81-NOKIA-"), nokia.EffectText);
        AllSupported(t);
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(t);
        var baseD = Put(state.Player, Digimon("B81-AGU", 4));
        var grey = Digimon(Uid("B81-GREY-"), 5, name: "MetalGreymon", evoCost: 3);
        state.Player.Hand.Add(grey);
        Assert.True(engine.EvolveDigimon(baseD, grey).CanExecute);
        Assert.Equal(8 - 2, state.MemoryGauge);                                  // 테이머를 레스트시켜 진화 코스트 -1
        Assert.Contains(t, state.Player.SuspendedTamers);
        var baseD2 = Put(state.Player, Digimon("B81-AGU2", 4));
        var doru = Digimon(Uid("B81-DORU-"), 5, name: "DoruGreymon", evoCost: 3);
        state.Player.Hand.Add(doru);
        state.Player.SuspendedTamers.Clear();
        Assert.True(engine.EvolveDigimon(baseD2, doru).CanExecute);
        Assert.Equal(6 - 3, state.MemoryGauge);                                  // 「도르그레이몬」은 제외 (공식 영어)
    }

    // =====================================================================
    // 3. "When an effect adds cards to your opponent's hand" — 효과 하나로 상대의 패가 늘어났을 때 (드로우 · 서치 포함)
    // =====================================================================

    private const string UnsuspendOnOppHand = "[All Turns] [Once Per Turn] When an effect adds cards to your opponent's hand, unsuspend this Digimon.";

    [Fact]
    public void OppHandAdded_OpponentDrawsByEffect_Triggers_OncePerEffect_BT11_028()
    {
        var x = Digimon(Uid("B81-GAOS-"), 4, UnsuspendOnOppHand);
        AllSupported(x);
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, x);
        mine.IsSuspended = true;
        var draw = Digimon(Uid("B81-DRAW-"), 5, "[On Play] ＜Draw 2＞ (Draw 2 cards from your deck.)");
        var opp = Put(state.DummyOpponent, draw);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, opp, draw);   // 상대가 효과로 드로우 (공식 Q&A BT10-077 Q1 — 드로우도 패가 늘어난 것)
        Assert.Equal(2, state.DummyOpponent.Hand.Count);
        Assert.False(mine.IsSuspended);
        Assert.Contains("효과로 상대의 패가 늘어났을 때", Logs(state));
    }

    [Fact]
    public void OppHandAdded_ReturnOpponentsDigimonToHand_StillTriggers()
    {
        var x = Digimon(Uid("B81-GAOS-"), 4, UnsuspendOnOppHand);
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, x);
        mine.IsSuspended = true;
        Put(state.DummyOpponent, Digimon("B81-OPP3", 3, dp: 2000));
        var bounce = Digimon(Uid("B81-BOUNCE-"), 5, "[On Play] Return 1 of your opponent's Digimon to the hand.");
        var b = Put(state.Player, bounce);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, b, bounce);
        Assert.Single(state.DummyOpponent.Hand);
        Assert.False(mine.IsSuspended);
    }

    [Fact]
    public void OppHandAdded_OwnHandAdd_DoesNotTrigger()
    {
        var x = Digimon(Uid("B81-GAOS-"), 4, UnsuspendOnOppHand);
        var (state, engine) = NewMainPhase();
        var mine = Put(state.Player, x);
        mine.IsSuspended = true;
        var draw = Digimon(Uid("B81-DRAW-"), 5, "[On Play] ＜Draw 2＞ (Draw 2 cards from your deck.)");
        var d = Put(state.Player, draw);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, draw);
        Assert.Equal(2, state.Player.Hand.Count);
        Assert.True(mine.IsSuspended);
    }

    [Fact]
    public void OppHandAdded_YourOrOpponentsHand_BothSides_BT17_028()
    {
        var x = Digimon(Uid("B81-BT17028-"), 5, "[Your Turn] [Once Per Turn] When an effect adds cards to your or your opponent's hand, your opponent adds the top card of their security stack to the hand.");
        AllSupported(x);
        var (state, engine) = NewMainPhase();
        Put(state.Player, x);
        var draw = Digimon(Uid("B81-DRAW-"), 5, "[On Play] ＜Draw 1＞ (Draw 1 card from your deck.)");
        var opp = Put(state.DummyOpponent, draw);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, opp, draw);   // 자신의 턴에 상대가 효과로 드로우
        Assert.Equal(4, state.DummyOpponent.SecurityStack.Count);
        Assert.Equal(2, state.DummyOpponent.Hand.Count);                             // 드로우 1 + 시큐리티 1
    }

    // =====================================================================
    // 4. 공식 한글 옛 표기 특수 진화 조건 "진화:「X」에서 N"
    // =====================================================================

    [Fact]
    public void OldNotation_ConvertedToDigivolveCondition()
    {
        Assert.Equal("〔진화〕「아구몬」 : 코스트 0\n 【등장 시】테스트.", KoreanEvolution47.OldNotationToNew81("진화 : 「아구몬」에서 0 【등장 시】테스트."));
        Assert.StartsWith("〔진화〕특징으로 「로얄 나이츠」를 가진 Lv.6 : 코스트 5", KoreanEvolution47.OldNotationToNew81("진화:『특징으로 「로얄 나이츠」를 가진 Lv.6』에서 5【진화 시】테스트."));
        Assert.StartsWith("〔진화〕명칭에 「드래곤 모드」를 포함 : 코스트 2", KoreanEvolution47.OldNotationToNew81("《진화:「명칭에 「드래곤 모드」를 포함」에서 2》"));
        Assert.StartsWith("〔진화〕「오메가몬」 : 코스트 3\n진화원에", KoreanEvolution47.OldNotationToNew81("진화:「오메가몬」에서 3진화원에 「X항체」가 있는 디지몬이 ~"));
        string now = "〔진화〕「아구몬」 : 코스트 0 진화:「파피몬」에서 1";
        Assert.Equal(now, KoreanEvolution47.OldNotationToNew81(now));            // 〔진화〕 표기가 있으면 그대로
    }

    [Fact]
    public void OldNotation_GivesDigivolvePath_BT9_008()
    {
        string evoId = Uid("B81-AGUX-"), baseId = Uid("B81-AGUBASE-");
        WithOfficialData(Array.Empty<EnglishCardRule>(), new[]
        {
            new KoreanCard { CardId = evoId, Name = "아구몬 X항체", Type = "디지몬", MainText = "진화 : 「테스트아구몬」에서 0 【진화 시】테스트 문구." },
            new KoreanCard { CardId = baseId, Name = "테스트아구몬", Type = "디지몬" },
        }, () =>
        {
            var evo = Digimon(evoId, 3, evoCost: 2);
            var cond = Assert.Single(KoreanEvolution47.For(evo).Conditions);
            Assert.Equal(0, cond.Cost);
            var (state, engine) = NewMainPhase();
            var agu = Put(state.Player, Digimon(baseId, 3, name: "테스트아구몬"));   // 같은 Lv.3 — 보통 진화 조건으로는 진화할 수 없다
            state.Player.Hand.Add(evo);
            Assert.True(engine.EvolveDigimon(agu, evo).CanExecute);
            Assert.Equal(8, state.MemoryGauge);                                     // 코스트 0
        });
    }

    // =====================================================================
    // 5. 여러 술어 효과의 로그 한글 · 요약체 한국 사이트 문구
    // =====================================================================

    [Fact]
    public void MultiPredicateLog_IsKorean()
    {
        var x = Digimon(Uid("B81-PRED-"), 5, "[On Play] 1 of your opponent's Digimon gets -2000 DP and can't suspend until the end of their turn.");
        AllSupported(x);
        var (state, engine) = NewMainPhase();
        Put(state.DummyOpponent, Digimon("B81-OPP5", 5, dp: 7000));
        var me = Put(state.Player, x);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, x);
        string logs = Logs(state);
        Assert.DoesNotContain("can't suspend", logs);
        Assert.Contains("레스트할 수 없다", logs);
    }

    [Fact]
    public void SummaryStyleKoreanText_WithWrongMeaning_UsesGeneratedKorean()
    {
        Assert.True(KoreanCardData.MainFromEnglish80Ids.ContainsKey("BT23-032"));
        Assert.True(KoreanCardData.MainFromEnglish80Ids.ContainsKey("EX9-021"));
        Assert.Contains("BT23-032", KoreanCardData.SourceFromEnglish80Ids);
        Assert.False(KoreanCardData.MainFromEnglish80Ids.ContainsKey("BT11-069"));   // 요약체이지만 뜻이 맞는 카드는 그대로
        Assert.False(KoreanCardData.MainFromEnglish80Ids.ContainsKey("BT16-026"));
    }
}
