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
/// v0.80.0 미지원 문장 54차 묶음: 한글 ↔ 영어 카드 대조 (한국 사이트 문구가 그 카드의 것이 아니면 쓰지 않는다 · 빠진 진화원 효과 · 〔진화〕 Lv. 오타 · 다른 카드 안전장치) ·
/// BT26-078 [트래시] 등장한 디지몬 1마리에게 ≪속공≫ ≪에그제큐트≫ (16-38) · BT23-024 가장 등장 코스트가 높은 디지몬 이외 레스트 불가 (15-11-2-2 · 15-11-2-3) ·
/// BT16-015 【소멸 시】에 【어택 종료 시】 추가 (Q&amp;A BT16-015 Q1) · BT7-112 패 · 트래시 10장을 덱 아래로 테이머에서 진화 (Q&amp;A BT7-112 Q1) ·
/// BT10-077 효과 하나로 늘어난 장수만큼 (Q&amp;A BT10-077 Q1 · EX7-013 Q1)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch80Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
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

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1, int playCost = 3, params string[] traits) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, "", traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B80-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B80-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B80-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B80-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static string Ko(string clause) => KoreanEffectRenderer.RenderClause(clause) ?? "<null>";

    /// <summary>kr/cards_ko.json (+ 있으면 그 위 폴더의 cards_cache.json)을 임시 폴더에 만들어 읽는다</summary>
    private static void WithKoreanData(IEnumerable<KoreanCard> extra, Action run, string? englishCacheJson = null)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b80_" + Guid.NewGuid().ToString("N"));
        var kr = System.IO.Path.Combine(dir, "kr");
        System.IO.Directory.CreateDirectory(kr);
        var file = System.IO.Path.Combine(kr, "cards_ko.json");
        var ids = extra.Select(e => e.CardId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Where(k => !ids.Contains(k.CardId)).Concat(extra).ToList()));
        if (englishCacheJson != null) System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "cards_cache.json"), englishCacheJson);
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 1. 한글 ↔ 영어 카드 대조
    // =====================================================================

    [Fact]
    public void Audit_WrongKoreanMainText_UsesGeneratedKoreanAndEnglishDigivolveCondition_EX9_062()
    {
        // 한국 사이트의 EX9-062 메인 문구는 다른 카드의 것 (카드 이미지 · 공식 영어와 다르다) — 문구는 테스트용 가짜
        WithKoreanData(new[] { new KoreanCard { CardId = "EX9-062", Name = "테스트스컬", Type = "디지몬", MainText = "〔진화〕Lv.3 : 코스트 2 【어택 시】테스트용 다른 카드 문구.", SourceText = "【소멸 시】테스트용 진화원 문구." } }, () =>
        {
            var ko = KoreanCardData.Get("EX9-062")!;
            Assert.True(ko.MainFromEnglish80);
            Assert.Null(ko.MainText);
            var card = Digimon("EX9-062", 5, "[On Play] Gain 1 memory.", name: "SkullGreymon", color: CardColor.Purple);
            Assert.DoesNotContain("다른 카드 문구", card.DisplayEffectText);
            Assert.Contains("메모리", card.DisplayEffectText);                     // 영어 효과의 생성 한글
            Assert.Contains("테스트용 진화원 문구", card.DisplayInheritedEffectText);  // 하단 문구는 그 카드의 것 — 그대로
            Assert.Equal("테스트스컬", card.DisplayName);                           // 이름 · 특징은 같은 카드
            Assert.DoesNotContain(KoreanEvolution47.For(card).Conditions, c => c.Cost == 2);   // 한글 〔진화〕 Lv.3 : 코스트 2를 쓰지 않는다
        });
    }

    [Fact]
    public void Audit_MissingKoreanInheritedEffect_ShowsGeneratedKorean_BT17_001()
    {
        WithKoreanData(new[] { new KoreanCard { CardId = "BT17-001", Name = "테스트기기", Type = "디지타마", MainText = "-", SourceText = "-" } }, () =>
        {
            Assert.True(KoreanCardData.Get("BT17-001")!.SourceFromEnglish80);
            // 영어 API는 진화원 효과를 메인 칸에 "Inherited Effect ~"로 둔다
            var egg = Card("BT17-001", "Inherited Effect [When Attacking] Gain 1 memory.", CardType.Digitama, level: 2);
            Assert.Contains("메모리", egg.DisplayInheritedEffectText);
            Assert.Contains("【어택 시】", egg.DisplayInheritedEffectText);
        });
    }

    [Fact]
    public void Audit_DigivolveLevelTypo_FixedInsideDigivolveCondition_BT19_053()
    {
        WithKoreanData(new[] { new KoreanCard { CardId = "BT19-053", Name = "테스트여왕", Type = "디지몬", MainText = "〔진화〕특징으로 「테스트」를 가진 Lv.4 : 코스트 3【어택 시】Lv.4 테스트 문구." } }, () =>
        {
            string t = KoreanCardData.Get("BT19-053")!.MainText!;
            Assert.StartsWith("〔진화〕특징으로 「테스트」를 가진 Lv.5 : 코스트 3", t);   // 카드 이미지 · 공식 영어 "Lv.5 w/[Royal Base] trait: Cost 3"
            Assert.Contains("【어택 시】Lv.4 테스트 문구.", t);                             // 〔진화〕 조건 밖은 그대로
        });
    }

    [Fact]
    public void Audit_MainTextRepeatingInheritedEffect_IsCut()
    {
        string id = Uid("B80-DUP-");
        WithKoreanData(new[] { new KoreanCard { CardId = id, Name = "테스트", Type = "디지몬", MainText = "【등장 시】테스트 메인. ・진화원 효과 【자신의 턴】테스트 진화원.", SourceText = "【자신의 턴】테스트 진화원." } }, () =>
        {
            Assert.Equal("【등장 시】테스트 메인.", KoreanCardData.Get(id)!.MainText);
        });
    }

    [Fact]
    public void Audit_DifferentCardOnSameNumber_KoreanDataNotUsed()
    {
        // 안전장치: 종류가 다르거나 Lv. · 등장 코스트 · DP 중 둘 이상이 다르면 다른 카드 — 하나만 다르면 (API 수치 오류 · 오타) 같은 카드
        string a = Uid("B80-IDA-"), b = Uid("B80-IDB-"), c = Uid("B80-IDC-");
        string cache = System.Text.Json.JsonSerializer.Serialize(new object[]
        {
            new { CardId = a, Type = "Tamer", Level = 0, PlayCost = 3, DP = 0 },
            new { CardId = b, Type = "Digimon", Level = 5, PlayCost = 7, DP = 7000 },
            new { CardId = c, Type = "Digimon", Level = 5, PlayCost = 7, DP = 7000 },
        });
        WithKoreanData(new[]
        {
            new KoreanCard { CardId = a, Name = "다른종류", Type = "디지몬", Level = "Lv.3", PlayCost = "3", DP = "3000" },
            new KoreanCard { CardId = b, Name = "다른카드", Type = "디지몬", Level = "Lv.3", PlayCost = "4", DP = "7000" },
            new KoreanCard { CardId = c, Name = "같은카드", Type = "디지몬", Level = "Lv.5", PlayCost = "8", DP = "7000" },
        }, () =>
        {
            Assert.Null(KoreanCardData.Get(a));
            Assert.Null(KoreanCardData.Get(b));
            Assert.NotNull(KoreanCardData.Get(c));
            Assert.Contains(a, KoreanCardData.IdentityMismatches80);
            Assert.Contains(b, KoreanCardData.IdentityMismatches80);
            Assert.Equal("Unknown", Digimon(b, 5, name: "Unknown").DisplayName);
        }, cache);
    }

    // =====================================================================
    // 2. BT26-078 [트래시] 등장한 디지몬 1마리 ≪속공≫ · ≪에그제큐트≫
    // =====================================================================

    private const string TrashExecute = "[Trash] [Your Turn] When any of your [Chronomon] text or [Titan] trait Digimon are played, if your opponent has 5 or more memory, by returning this card to the bottom of the deck, 1 of them gains ＜Rush＞ (This Digimon can attack the turn it was played.) and ＜Execute＞ for the turn. (At the end of your turn, this Digimon may attack. At the end of that attack, delete this Digimon. Your opponent's unsuspended Digimon can also be attacked with this effect.)";

    [Fact]
    public void TrashExecute_PlayedTitanGainsRushAndExecute_CardToDeckBottom_BT26_078()
    {
        var x = Digimon(Uid("B80-CHRONO-"), 6, TrashExecute);
        AllSupported(x);
        var (state, engine) = NewMainPhase(memory: -5);   // 상대의 메모리 5
        engine.PlayerChoices = new AcceptAll();
        state.Player.Trash.Add(x);
        var titan = Digimon(Uid("B80-TITAN-"), 4, traits: "Titan");
        state.Player.Hand.Add(titan);
        Assert.True(engine.TryPlayByEffect(state.Player, titan, PlayOrigin.Hand, () => state.Player.Hand.Remove(titan)));
        var d = state.Player.BattleArea.Single(i => i.TopCard == titan);
        Assert.True(CardScripts.HasKeyword(d, Keyword.Rush), Logs(state));
        Assert.True(CardScripts.HasKeyword(d, Keyword.Execute));
        Assert.Same(x, state.Player.MainDeck.Last());
        Assert.DoesNotContain(x, state.Player.Trash);
    }

    [Fact]
    public void TrashExecute_OpponentHas4Memory_Nothing_BT26_078()
    {
        var x = Digimon(Uid("B80-CHRONO-"), 6, TrashExecute);
        var (state, engine) = NewMainPhase(memory: -4);
        engine.PlayerChoices = new AcceptAll();
        state.Player.Trash.Add(x);
        var titan = Digimon(Uid("B80-TITAN-"), 4, traits: "Titan");
        state.Player.Hand.Add(titan);
        engine.TryPlayByEffect(state.Player, titan, PlayOrigin.Hand, () => state.Player.Hand.Remove(titan));
        Assert.False(CardScripts.HasKeyword(state.Player.BattleArea.Single(), Keyword.Execute));
        Assert.Contains(x, state.Player.Trash);
    }

    [Fact]
    public void TrashExecute_Korean_PlayedDigimon1()
    {
        string k = Ko(TrashExecute);
        Assert.StartsWith("[트래시] 【자신의 턴】", k);
        Assert.Contains("상대의 메모리가 5 이상이라면, 이 카드를 덱 아래로 되돌리는 것으로, 이 턴 동안, 등장한 디지몬 1마리는 《속공》", k);
        Assert.Contains("《에그제큐트》", k);
    }

    // =====================================================================
    // 3. BT23-024 가장 등장 코스트가 높은 디지몬 이외 — 전체 처리 (15-11-2-2 · 15-11-2-3-2 · 15-11-2-3-3)
    // =====================================================================

    private const string NotHighest = "[All Turns] [Once Per Turn] When this Digimon gets linked, by unsuspending it, other than their highest play cost Digimon, none of your opponent's Digimon can suspend until their turn ends.";

    [Fact]
    public void NotHighest_OthersCantSuspend_RecheckedWhenHighestLeaves_BT23_024()
    {
        var x = Digimon(Uid("B80-POSEI-"), 6, NotHighest);
        AllSupported(x);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, x);
        me.IsSuspended = true;
        var hi = Put(state.DummyOpponent, Digimon("B80-HI", 6, playCost: 12));
        var mid = Put(state.DummyOpponent, Digimon("B80-MID", 5, playCost: 7));
        var low = Put(state.DummyOpponent, Digimon("B80-LOW", 3, playCost: 3));
        var trigger = CardScripts.For(x)!.Effects.Single().Trigger;
        engine.RunEffects(trigger, state.Player, me, x);
        int seq = state.TurnSequence;
        bool Locked(CardInstance d) => d.CannotSuspendUntil >= seq;
        Assert.False(me.IsSuspended, Logs(state));   // 비용: 이 디지몬을 액티브로
        Assert.False(Locked(hi));
        Assert.True(Locked(mid));
        Assert.True(Locked(low));
        // 가장 높은 디지몬이 떠나면 다음으로 높은 디지몬이 대상이 아니게 되고, 나중에 나온 디지몬은 대상
        state.DummyOpponent.BattleArea.Remove(hi);
        var late = Put(state.DummyOpponent, Digimon("B80-LATE", 3, playCost: 2));
        engine.RecalculateFieldBuffs();
        Assert.False(Locked(mid));
        Assert.True(Locked(low));
        Assert.True(Locked(late));
    }

    [Fact]
    public void NotHighest_Korean()
    {
        Assert.Equal("【서로의 턴】 [턴에 1회] 이 디지몬이 링크했을 때, 이 디지몬을 액티브로 하는 것으로, 상대의 턴 종료 시까지, 가장 등장 코스트가 높은 상대의 디지몬 이외의 상대의 디지몬 전부는 레스트할 수 없다.", Ko(NotHighest));
    }

    // =====================================================================
    // 4. BT16-015 【소멸 시】 효과에 【어택 종료 시】 추가 (Q&A BT16-015 Q1)
    // =====================================================================

    private static string AttachText(string name) =>
        $"[Your Turn] While [{name}] or [X Antibody] is in this Digimon's digivolution cards, attach [End of Attack] to all of this Digimon's [On Deletion] effects.\n[On Deletion] Gain 2 memory.";

    [Fact]
    public void AttachEndOfAttack_OnDeletionAlsoAtEndOfAttack_BT16_015()
    {
        string phx = Uid("B80PHX");
        var x = Digimon(Uid("B80-PHXX-"), 6, AttachText(phx), dp: 13000);
        AllSupported(x);
        foreach (bool has in new[] { true, false })
        {
            var (state, engine) = NewMainPhase(memory: 3);
            var me = has ? Put(state.Player, x, Digimon(phx, 5, name: phx)) : Put(state.Player, x, Digimon("B80-OTHER", 5));
            Assert.True(engine.AttackSecurityOrDirect(me).CanExecute);
            Assert.Equal(has ? 5 : 3, state.MemoryGauge);   // 조건이 맞을 때만 어택 종료 시에 【소멸 시】 +2
        }
    }

    [Fact]
    public void AttachEndOfAttack_Korean()
    {
        Assert.Equal("【자신의 턴】 이 디지몬의 진화원에 「테스트」/「X항체」가 있는 동안, 이 디지몬의 【소멸 시】 효과 전부에 【어택 종료 시】를 추가한다.",
            Ko("[Your Turn] While [테스트] or [X Antibody] is in this Digimon's digivolution cards, attach [End of Attack] to all of this Digimon's [On Deletion] effects."));
    }

    // =====================================================================
    // 5. BT7-112 패 · 트래시의 10장을 덱 아래로 — 테이머에서 진화 (Q&A BT7-112 Q1)
    // =====================================================================

    private const string Susano = "By placing 10 Tamer cards and/or cards with the [Hybrid] trait from your hand and/or trash at the bottom of your deck in any order, you may digivolve this card from your hand onto one of your Tamers as if the Tamer is a level 6 Digimon.";

    [Fact]
    public void TamerEvoPlace_Pays10ToDeckBottom_DigivolvesFromTamer_BT7_112()
    {
        var x = Digimon(Uid("B80-SUSA-"), 7, Susano, color: CardColor.White, playCost: 15);
        AllSupported(x);
        var (state, engine) = NewMainPhase(memory: 8);
        var tamer = Card(Uid("B80-TAMER-"), type: CardType.Tamer);
        state.Player.Tamers.Add(tamer);
        state.Player.Hand.Add(x);
        var mats = Enumerable.Range(0, 10).Select(i => i % 2 == 0 ? Card(Uid("B80-T-"), type: CardType.Tamer) : Digimon(Uid("B80-H-"), 4, traits: "Hybrid")).ToList();
        for (int i = 0; i < 10; i++) (i < 6 ? state.Player.Trash : state.Player.Hand).Add(mats[i]);
        int deck = state.Player.MainDeck.Count;
        var r = engine.EvolveTamer(tamer, x);
        Assert.True(r.CanExecute, r.Reason);
        var d = state.Player.BattleArea.Single(i => i.TopCard == x);
        Assert.Contains(tamer, d.EvolutionSources);
        Assert.Equal(deck + 10 - 1, state.Player.MainDeck.Count);   // 10장 덱 아래 · 진화 드로우 1장
        Assert.All(mats, m => Assert.DoesNotContain(m, state.Player.Trash.Concat(state.Player.Hand)));
        Assert.Equal(8 - 1, state.MemoryGauge);   // Lv.6에서의 진화 코스트 (테스트 카드 1)
    }

    [Fact]
    public void TamerEvoPlace_Only9_CannotDigivolve_BT7_112()
    {
        var x = Digimon(Uid("B80-SUSA-"), 7, Susano, color: CardColor.White, playCost: 15);
        var (state, engine) = NewMainPhase(memory: 8);
        var tamer = Card(Uid("B80-TAMER-"), type: CardType.Tamer);
        state.Player.Tamers.Add(tamer);
        state.Player.Hand.Add(x);
        for (int i = 0; i < 9; i++) state.Player.Trash.Add(Card(Uid("B80-T-"), type: CardType.Tamer));
        Assert.Null(engine.TamerEvolutionCostFor(state.Player, x, tamer));
        Assert.False(engine.EvolveTamer(tamer, x).CanExecute);
        Assert.Contains(tamer, state.Player.Tamers);
    }

    [Fact]
    public void TamerEvoPlace_Korean()
    {
        Assert.Equal("자신의 패 또는 트래시에서 테이머 카드 또는 특징으로 「하이브리드체」를 가진 카드 합계 10장을 원하는 순서대로 덱 아래로 되돌리는 것으로, 패의 이 카드는 자신의 테이머를 Lv.6의 디지몬으로서 취급하여 진화할 수 있다.", Ko(Susano));
    }

    // =====================================================================
    // 6. BT10-077 효과 하나로 늘어난 장수만큼 (Q&A BT10-077 Q1 · EX7-013 Q1)
    // =====================================================================

    private const string MadLeo = "[Opponent's Turn] [Once Per Turn] When an effect adds cards to your opponent's hand, by choosing and trashing 1 of this Digimon's digivolution cards, your opponent trashes cards from their hand equal to the number of cards added to their hand by the effect.";

    [Fact]
    public void HandAddCount_OpponentDraws2ByOneEffect_Trashes2_OncePerTurn_BT10_077()
    {
        var x = Digimon(Uid("B80-MADLEO-"), 4, MadLeo);
        AllSupported(x);
        var (state, engine) = NewMainPhase();
        var mad = Put(state.DummyOpponent, x, Digimon("B80-S1", 3), Digimon("B80-S2", 3));   // 상대(AI)의 매드레오몬 — 지금은 플레이어의 턴 (그 주인에게는 상대의 턴)
        var draw = Digimon(Uid("B80-DRAW-"), 5, "[On Play] ＜Draw 2＞ (Draw 2 cards from your deck.)");
        var me = Put(state.Player, draw);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, draw);
        Assert.Empty(state.Player.Hand);                         // 2장 늘고 2장 파기
        Assert.Single(mad.EvolutionSources);
        Assert.Contains("늘어난 2장", Logs(state));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, draw);
        Assert.Equal(2, state.Player.Hand.Count);               // [턴에 1회]
    }

    [Fact]
    public void HandAddCount_OwnersTurn_NoTrigger_BT10_077()
    {
        var x = Digimon(Uid("B80-MADLEO-"), 4, MadLeo);
        var (state, engine) = NewMainPhase();
        var mad = Put(state.Player, x, Digimon("B80-S1", 3));
        var draw = Digimon(Uid("B80-DRAW-"), 5, "[On Play] ＜Draw 2＞ (Draw 2 cards from your deck.)");
        var opp = Put(state.DummyOpponent, draw);
        engine.RunEffects(EffectTrigger.OnPlay, state.DummyOpponent, opp, draw);
        Assert.Equal(2, state.DummyOpponent.Hand.Count);
        Assert.Single(mad.EvolutionSources);
    }

    [Fact]
    public void HandAddCount_Korean()
    {
        Assert.Equal("【상대의 턴】 [턴에 1회] 효과로 상대의 패에 카드가 추가되었을 때, 이 디지몬의 진화원을 선택하여 1장 파기하는 것으로, 상대는 그 효과로 늘어난 장수만큼 자신의 패를 파기한다.", Ko(MadLeo));
    }
}
