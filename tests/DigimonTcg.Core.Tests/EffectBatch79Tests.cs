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
/// v0.79.0 미지원 문장 53차 묶음: 모든 디지크로스 카드 — 효과로 등장시킬 때도 디지크로스 (종합 룰 7-2-2-13 · 7-2-3-3 · Q&amp;A P-098 Q1 · BT11-076 Q1) ·
/// "this Digimon may unsuspend"가 이미 액티브일 때 로그 · EX5-058 서로의 디지몬 합계로 토큰을 놓을 쪽 (Q&amp;A EX5-058 Q1) · BT23-013 · BT19-091 같은 명칭 제외 ·
/// BT19-091 ≪연계≫ 2개 · BT26-086 명칭이 서로 다른 링크 · BT20-098 Lv. 합계 9 (Q&amp;A BT20-098 Q1) · BT10-041 사용한 옵션을 시큐리티 위로 (Q&amp;A BT10-041 Q1)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch79Tests
{
    private sealed class AcceptAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => true;
        public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional) => recommended ?? options.FirstOrDefault();
        public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended) => recommended;
    }

    private sealed class DeclineAll : IChoiceProvider
    {
        public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended) => false;
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
            state.Player.MainDeck.Add(Digimon("B79-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B79-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B79-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B79-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static void WithKoreanCards(IEnumerable<KoreanCard> extra, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b79_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "cards_ko.json");
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Concat(extra).ToList()));
        try { KoreanCardData.Reload(file); run(); }
        finally { KoreanCardData.Reload(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 1. 효과 등장의 디지크로스 (7-2-2-13)
    // =====================================================================

    private const string IfXrosDelete = "[On Play] Then, if DigiXrosing with 2 cards, delete 1 of your opponent's Digimon with a play cost of 5 or less.";

    /// <summary>디지크로스 -2:「A」×「B」 카드와 재료 2장 (공식 한글 문구 형식은 테스트용 가짜 카드)</summary>
    private static void WithXros(string main, Action<CardData, CardData, CardData> run)
    {
        string id = Uid("B79-X-"), a = Uid("B79-XA-"), b = Uid("B79-XB-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = id, Name = "테스트크로스", MainText = "디지크로스 -2:「테스트에이」×「테스트비」" },
            new KoreanCard { CardId = a, Name = "테스트에이" }, new KoreanCard { CardId = b, Name = "테스트비" },
        }, () => run(Digimon(id, 5, main, playCost: 8), Digimon(a, 3), Digimon(b, 3)));
    }

    [Fact]
    public void Xros_FreeEffectPlay_PlayerDigiXrosesFromHand_7_2_2_13()
    {
        WithXros(IfXrosDelete, (x, a, b) =>
        {
            var (state, engine) = NewMainPhase(memory: 3);
            engine.PlayerChoices = new AcceptAll();
            state.Player.Hand.AddRange(new[] { x, a, b });
            var victim = Put(state.DummyOpponent, Digimon("B79-V", 4, playCost: 5));
            Assert.True(engine.TryPlayByEffect(state.Player, x, PlayOrigin.Hand, () => state.Player.Hand.Remove(x)));
            var me = Assert.Single(state.Player.BattleArea);
            Assert.Equal(new[] { a, b }, me.EvolutionSources.ToArray());   // 왼쪽 칸이 위 (7-2-2-8)
            Assert.Empty(state.Player.Hand);
            Assert.Equal(3, state.MemoryGauge);   // 코스트 없이 — 감소는 의미 없다 (7-2-3-3)
            Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);   // "if DigiXrosing with 2 cards" (P-098 Q1)
        });
    }

    [Fact]
    public void Xros_FreeEffectPlay_Declined_NoMaterials_7_2_2_12()
    {
        WithXros(IfXrosDelete, (x, a, b) =>
        {
            var (state, engine) = NewMainPhase(memory: 3);
            engine.PlayerChoices = new DeclineAll();
            state.Player.Hand.AddRange(new[] { x, a, b });
            var victim = Put(state.DummyOpponent, Digimon("B79-V2", 4, playCost: 5));
            Assert.True(engine.TryPlayByEffect(state.Player, x, PlayOrigin.Hand, () => state.Player.Hand.Remove(x)));
            Assert.Empty(state.Player.BattleArea.Single().EvolutionSources);
            Assert.Equal(2, state.Player.Hand.Count);
            Assert.Contains(victim, state.DummyOpponent.BattleArea);
        });
    }

    [Fact]
    public void Xros_PaidEffectPlay_FieldMaterial_ReducesCost()
    {
        WithXros("", (x, a, b) =>
        {
            var (state, engine) = NewMainPhase(memory: 8);
            engine.PlayerChoices = new AcceptAll();
            state.Player.Hand.AddRange(new[] { x, b });
            var under = Digimon("B79-UNDER", 2);
            var field = Put(state.Player, a, under);
            Assert.True(engine.TryPlayByEffect(state.Player, x, PlayOrigin.Hand, () => state.Player.Hand.Remove(x), payCost: 6));
            var me = state.Player.BattleArea.Single(d => d.TopCard == x);
            Assert.DoesNotContain(field, state.Player.BattleArea);
            Assert.Equal(new[] { a, b }, me.EvolutionSources.ToArray());
            Assert.Contains(under, state.Player.Trash);   // 필드의 디지몬은 그 카드만, 진화원은 트래시
            Assert.Equal(6, state.MemoryGauge);   // 6 - 2×2 = 2 지불
        });
    }

    [Fact]
    public void Xros_AiFreePlay_UsesHandOnlyWhenIfDigiXrosing()
    {
        WithXros("", (x, a, b) =>
        {
            var (state, engine) = NewMainPhase();
            state.DummyOpponent.Hand.AddRange(new[] { x, a });
            Assert.True(engine.TryPlayByEffect(state.DummyOpponent, x, PlayOrigin.Hand, () => state.DummyOpponent.Hand.Remove(x)));
            Assert.Empty(state.DummyOpponent.BattleArea.Single().EvolutionSources);
            Assert.Contains(a, state.DummyOpponent.Hand);
            // 코스트를 내는 등장은 보통 등장처럼 패의 카드로 코스트를 줄인다
            var (s2, e2) = NewMainPhase();
            s2.DummyOpponent.Hand.AddRange(new[] { x, a });
            Assert.True(e2.TryPlayByEffect(s2.DummyOpponent, x, PlayOrigin.Hand, () => s2.DummyOpponent.Hand.Remove(x), payCost: 4));
            Assert.Single(s2.DummyOpponent.BattleArea.Single().EvolutionSources);
        });
        WithXros(IfXrosDelete, (x, a, b) =>
        {
            var (state, engine) = NewMainPhase();
            state.DummyOpponent.Hand.AddRange(new[] { x, a, b });
            var victim = Put(state.Player, Digimon("B79-V3", 4, playCost: 5));
            Assert.True(engine.TryPlayByEffect(state.DummyOpponent, x, PlayOrigin.Hand, () => state.DummyOpponent.Hand.Remove(x)));
            Assert.Equal(2, state.DummyOpponent.BattleArea.Single().EvolutionSources.Count);
            Assert.DoesNotContain(victim, state.Player.BattleArea);
        });
    }

    [Fact]
    public void Xros_ToggleOff_V078Behavior()
    {
        WithXros("", (x, a, b) =>
        {
            GoldfishEngine.EffectPlayXros79 = false;
            try
            {
                var (state, engine) = NewMainPhase();
                engine.PlayerChoices = new AcceptAll();
                state.Player.Hand.AddRange(new[] { x, a, b });
                Assert.True(engine.TryPlayByEffect(state.Player, x, PlayOrigin.Hand, () => state.Player.Hand.Remove(x)));
                Assert.Empty(state.Player.BattleArea.Single().EvolutionSources);
            }
            finally { GoldfishEngine.EffectPlayXros79 = true; }
        });
    }

    [Fact]
    public void Xros_BT10_104_TrashAndHandMaterials()
    {
        string id = Uid("B79-DK-"), a = Uid("B79-SK-"), b = Uid("B79-AX-");
        WithKoreanCards(new[]
        {
            new KoreanCard { CardId = id, Name = "테스트다크", MainText = "디지크로스 -2:「테스트스컬」×「테스트액스」" },
            new KoreanCard { CardId = a, Name = "테스트스컬" }, new KoreanCard { CardId = b, Name = "테스트액스" },
        }, () =>
        {
            var dk = Digimon(id, 5, name: "DarkKnightmon", playCost: 8);
            var sk = Digimon(a, 3); var ax = Digimon(b, 3);
            var opt = Card(Uid("B79-OPT-"), "[Main] Trash 3 cards from the top of your deck. Then, you may play 1 [DarkKnightmon] from your trash for its memory cost. If you play a Digimon card with DigiXros requirements by this effect, you may DigiXros by placing any of the specified Digimon from your trash at the bottom of this card as digivolution cards.");
            AllSupported(opt);
            var (state, engine) = NewMainPhase(memory: 10);
            engine.PlayerChoices = new AcceptAll();
            state.Player.Trash.AddRange(new[] { dk, sk });
            state.Player.Hand.Add(ax);
            engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
            var me = state.Player.BattleArea.Single(d => d.TopCard == dk);
            Assert.Equal(2, me.EvolutionSources.Count);   // 트래시 1장 + 패 1장 (7-2-3-2 패 · 배틀 에어리어는 언제나)
            Assert.Equal(6, state.MemoryGauge);   // 8 - 4
        });
    }

    // =====================================================================
    // 2. "this Digimon may unsuspend"가 이미 액티브 — 로그가 "대상이 없습니다"로 겹쳐 보이던 것
    // =====================================================================

    [Fact]
    public void UnsuspendSelf_AlreadyActive_LogsAlreadyActive()
    {
        var x = Digimon(Uid("B79-UNS-"), 5, "[On Play] Gain 1 memory. Then, this Digimon may unsuspend.");
        var (state, engine) = NewMainPhase();
        var d = Put(state.Player, x);
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, d, x, includeInherited: false);
        Assert.Contains("이미 액티브 상태입니다", Logs(state));
        Assert.DoesNotContain("대상이 없습니다", Logs(state));
    }

    // =====================================================================
    // 3. 모델 변경 문장
    // =====================================================================

    private const string Ex5058 = "[On Play] [When Digivolving] If there are 4 or more total Digimon, play 1 [Fujitsumon] Token suspended to your battle area. (Digimon/Purple/3000 DP/[All Turns] This Digimon doesn't unsuspend. [On Deletion] Trash 1 card in your hand.) If there are 3 or fewer, play it suspended to your opponent's battle area.";

    [Fact]
    public void EX5_058_TokenSideByTotalDigimon_AndCantUnsuspend()
    {
        var x = Digimon(Uid("B79-OCT-"), 4, Ex5058, color: CardColor.Purple);
        AllSupported(x);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, x); Put(state.Player, Digimon("B79-P1", 3)); Put(state.DummyOpponent, Digimon("B79-O1", 3)); Put(state.DummyOpponent, Digimon("B79-O2", 3));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, x, includeInherited: false);
        var tok = state.Player.BattleArea.Single(d => d.TopCard.Name.Contains("Fujitsumon"));
        Assert.True(tok.IsSuspended);
        engine.Unsuspend(tok);
        Assert.True(tok.IsSuspended);   // "This Digimon doesn't unsuspend" — 효과로도

        var (s2, e2) = NewMainPhase();
        var me2 = Put(s2.Player, x); Put(s2.DummyOpponent, Digimon("B79-O3", 3));
        e2.RunEffects(EffectTrigger.OnPlay, s2.Player, me2, x, includeInherited: false);
        var tok2 = s2.DummyOpponent.BattleArea.Single(d => d.TopCard.Name.Contains("Fujitsumon"));   // 상대의 디지몬으로 (Q&A EX5-058 Q1)
        Assert.True(tok2.IsSuspended);
        Assert.Single(s2.Player.BattleArea);
        Assert.Contains("후지쓰몬", Ko(Ex5058));
        Assert.Contains("3마리 이하라면, 상대의 배틀 에어리어에 레스트 상태로 등장시킨다.", Ko(Ex5058));
    }

    private const string Bt23013 = "[When Digivolving] [When Attacking] You may play 1 [Atho, René & Por] Token (Digimon/White/6000 DP/＜Reboot＞ ＜Blocker＞ ＜Decoy (Red/Black)＞) or, from your hand or trash, 1 Digimon card with [Sistermon] in its name without paying the cost. This effect can't play cards with the same names as any of your Digimon.";

    [Fact]
    public void BT23_013_TokenOrCard_NotSameNameAsYourDigimon()
    {
        var x = Digimon(Uid("B79-JES-"), 6, Bt23013, color: CardColor.Red);
        AllSupported(x);
        var blanc = Digimon(Uid("B79-SB-"), 3, name: "Sistermon Blanc");
        var ciel = Digimon(Uid("B79-SC-"), 4, name: "Sistermon Ciel");
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var me = Put(state.Player, x); Put(state.Player, ciel);
        state.Player.Hand.Add(ciel); state.Player.Trash.Add(blanc);
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, x, includeInherited: false);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == blanc);   // 추천 = 같은 명칭이 아닌 카드
        Assert.Contains(ciel, state.Player.Hand);

        // 토큰이 이미 있으면 같은 명칭의 토큰은 등장시킬 수 없다
        var (s2, e2) = NewMainPhase();
        e2.PlayerChoices = new AcceptAll();
        var me2 = Put(s2.Player, x);
        e2.RunEffects(EffectTrigger.WhenDigivolving, s2.Player, me2, x, includeInherited: false);
        e2.RunEffects(EffectTrigger.WhenDigivolving, s2.Player, me2, x, includeInherited: false);
        Assert.Single(s2.Player.BattleArea, d => d.TopCard.Name.Contains("Atho"));
        Assert.EndsWith("이 효과로는 자신의 디지몬과 같은 명칭의 카드는 등장시킬 수 없다.", Ko(Bt23013));
    }

    private const string Bt19091 = "[Main] Play 1 [WarGrowlmon] Token (Digimon/Red/6000 DP), 1 [Taomon] Token (Digimon/Yellow/6000 DP), and 1 [Rapidmon] Token (Digimon/Green/6000 DP). This effect can't play tokens with the same names as your Digimon. Then, 1 of your level 5 Digimon gains ＜Alliance＞ twice for the turn and attacks.";

    [Fact]
    public void BT19_091_SkipsSameNameToken_AllianceTwice()
    {
        var opt = Card(Uid("B79-TB-"), Bt19091, playCost: 8, color: CardColor.Red);
        AllSupported(opt);
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var wg = Put(state.Player, Digimon(Uid("B79-WG-"), 5, name: "WarGrowlmon", dp: 7000, color: CardColor.Red), Digimon("B79-WGU", 4));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        engine.RunDeclaredEffectAttack57();
        Assert.DoesNotContain(state.Player.BattleArea, d => d != wg && d.TopCard.Name.Contains("WarGrowlmon"));
        Assert.Equal(2, state.Player.BattleArea.Count(d => d != wg && GoldfishEngine.IsToken(d.TopCard)));
        Assert.Equal(2, state.Player.BattleArea.Count(d => d != wg && d.IsSuspended));   // ≪연계≫ 2번
        Assert.Equal(2, state.DummyOpponent.SecurityStack.Count);   // 시큐리티 어택 +2 → 3장 체크
        Assert.Contains("를 2개 얻고,", Ko(Bt19091));
    }

    private const string Bt26086 = "[On Play] [When Digivolving] You may link up to 7 [Appmon] trait cards with different names from this Digimon's digivolution cards to this Digimon without paying the costs. Then, this Digimon may attack without suspending.";

    [Fact]
    public void BT26_086_LinksOnlyDifferentNames()
    {
        var x = Digimon(Uid("B79-DAN-"), 7, Bt26086 + "\n＜Link +6＞ (Add 6 to this Digimon's maximum links.)", dp: 15000);
        AllSupported(x);
        var a = Digimon(Uid("B79-APA-"), 3, name: "Swipemon", traits: "Appmon");
        var b = Digimon(Uid("B79-APB-"), 3, name: "Gatchmon", traits: "Appmon");
        var (state, engine) = NewMainPhase();
        engine.PlayerChoices = new AcceptAll();
        var me = Put(state.Player, x, a, a, b, Digimon("B79-NA", 3));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, x, includeInherited: false);
        Assert.Equal(2, me.LinkedCards.Count);
        Assert.Equal(2, me.LinkedCards.Select(c => c.Name).Distinct().Count());
        Assert.Contains("명칭이 서로 다른", Ko(Bt26086));
    }

    private const string Bt20098 = "[Main] By returning 9 levels' total worth of Digimon cards from your opponent's trash to the bottom of the deck, you may play 1 [Ghost] trait Digimon card of each returned card's level from your trash without paying the costs. Then, the Digimon this effect played gain ＜Rush＞ (This Digimon can attack the turn it comes into play.) and ＜Blocker＞ (This Digimon can block in the blocker timing.) until the end of your opponent's turn.";

    [Fact]
    public void BT20_098_ReturnExactlyLevelTotal9_PlayEachLevel()
    {
        var opt = Card(Uid("B79-AL-"), Bt20098, playCost: 8, color: CardColor.Purple);
        AllSupported(opt);
        var g4 = Digimon(Uid("B79-G4-"), 4, traits: "Ghost"); var g5 = Digimon(Uid("B79-G5-"), 5, traits: "Ghost");
        var (state, engine) = NewMainPhase();
        state.DummyOpponent.Trash.AddRange(new[] { Digimon("B79-T3", 3), Digimon("B79-T4", 4), Digimon("B79-T5", 5), Digimon("B79-T6", 6) });
        state.Player.Trash.AddRange(new[] { g4, g5 });
        int deck = state.DummyOpponent.MainDeck.Count;
        engine.RunEffects(EffectTrigger.Main, state.Player, null, opt, includeInherited: false);
        Assert.Equal(deck + 2, state.DummyOpponent.MainDeck.Count);
        Assert.Equal(9, state.DummyOpponent.MainDeck.Skip(deck).Sum(c => c.Level));
        var p4 = state.Player.BattleArea.Single(d => d.TopCard == g4);
        Assert.Contains(state.Player.BattleArea, d => d.TopCard == g5);
        Assert.True(CardScripts.HasKeyword(p4, Keyword.Rush));
        Assert.True(CardScripts.HasKeyword(p4, Keyword.Blocker));

        // 합계 9를 만들 수 없으면 비용을 낼 수 없다 (Q&A BT20-098 Q1 — 일부만 실행할 수 없다)
        var (s2, e2) = NewMainPhase();
        s2.DummyOpponent.Trash.AddRange(new[] { Digimon("B79-U4", 4), Digimon("B79-U6", 6) });
        s2.Player.Trash.Add(g4);
        e2.RunEffects(EffectTrigger.Main, s2.Player, null, opt, includeInherited: false);
        Assert.Equal(2, s2.DummyOpponent.Trash.Count);
        Assert.Empty(s2.Player.BattleArea);
        Assert.Contains("Lv. 합계가 9가 되도록", Ko(Bt20098));
    }

    private const string Bt10041 = "[When Digivolving] You may use 1 Option card with [Plug-In] in its name or that's yellow and has a memory cost of 5 or less from your hand without meeting its color requirements or paying its memory cost. Place the Option card used with this effect on top of your security stack face down instead of placing it in your trash.";

    [Fact]
    public void BT10_041_UsedOptionGoesOnTopOfSecurity()
    {
        var x = Digimon(Uid("B79-SAK-"), 6, Bt10041, color: CardColor.Yellow);
        AllSupported(x);
        var plug = Card(Uid("B79-PLUG-"), "[Main] Gain 1 memory.", name: "Plug-In Z", playCost: 9, color: CardColor.Red);
        var (state, engine) = NewMainPhase(memory: 2);
        engine.PlayerChoices = new AcceptAll();
        var me = Put(state.Player, x);
        state.Player.Hand.Add(plug);
        int sec = state.Player.SecurityStack.Count;
        engine.RunEffects(EffectTrigger.WhenDigivolving, state.Player, me, x, includeInherited: false);
        Assert.Equal(3, state.MemoryGauge);   // 색 조건 무시 · 코스트 없이 사용
        Assert.Equal(sec + 1, state.Player.SecurityStack.Count);
        Assert.Same(plug, state.Player.SecurityStack[0]);
        Assert.DoesNotContain(plug, state.Player.Trash);
        Assert.Contains("시큐리티 맨 위에 뒷면으로", Ko(Bt10041));
    }
}
