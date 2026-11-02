using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.DeckBuilder;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using Xunit;

namespace DigimonTcg.Core.Tests;

/// <summary>
/// v0.83.0 57차 묶음: 영어 API에 빠진 효과 문구를 공식 영어로 (공식 영어 = 공식 일본어 · 메모리에서만) — 빈 효과 칸 · [시큐리티]만 있는 테이머 ·
/// 빠진 [시큐리티] · 진화원 효과 · 잘린 줄 · 새 문장 (덱 위로 · 겹쳐진 카드를 덱 위로 · 같은 표시 형식 · 효과로 줄어들었다면 · 특징 셋 비용 · 명칭으로 고르기) ·
/// 명칭 규칙 · 콜론 · 시스터몬 · 3색 카드 · 덱 구성 규칙 (50장 · 카드 번호 묶음)
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch83Tests
{
    private static CardData Card(string id, string main = "", CardType type = CardType.Option, int level = 0, int playCost = 3,
        CardColor color = CardColor.Blue, int dp = -1, string? name = null, string inherited = "", int evoCost = 1, List<string>? traits = null)
    {
        var card = new CardData
        {
            CardId = id, Name = name ?? id, Type = type, Color = color, Level = level, DP = dp >= 0 ? dp : level * 1000, PlayCost = playCost, EvoCost = evoCost,
            EvoRequirementLevel = level - 1, EvoRequirementColor = color, EffectText = main, InheritedEffectText = inherited, Traits = traits ?? new(),
        };
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    private static CardData Digimon(string id, int level, string main = "", string? name = null, CardColor color = CardColor.Blue, int dp = -1,
        string inherited = "", int evoCost = 1, List<string>? traits = null, int playCost = 3) =>
        Card(id, main, CardType.Digimon, level, playCost, color, dp, name, inherited, evoCost, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B83-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B83-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B83-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B83-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    /// <summary>공식 데이터 파일 없이 (형태 · 한글 데이터를 넣지 않게) 카드 1장을 고친다</summary>
    private static List<OfficialCardData81.Change> Fix(CardData c)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b83_" + Guid.NewGuid().ToString("N"));
        var kr = System.IO.Path.Combine(dir, "kr");
        System.IO.Directory.CreateDirectory(kr);
        var file = System.IO.Path.Combine(kr, "cards_ko.json");
        System.IO.File.WriteAllText(file, "[]");
        List<OfficialCardData81.Change> ch = new();
        try { KoreanCardData.Reload(file); EnglishCardRules.Use(Array.Empty<EnglishCardRule>()); ch = OfficialCardData81.Apply(c); KeywordDictionary.ApplyDataKeywords(c); }
        finally { KoreanCardData.Reload(); EnglishCardRules.Reload(); EvoRequirements48.ClearCache(); try { System.IO.Directory.Delete(dir, true); } catch { } }
        return ch;
    }

    private const string TamerSecurity = "Security Effect [Security] Play this card without paying the cost.";

    // =====================================================================
    // 1. 빈 효과 칸 채우기 (공식 영어 = 공식 일본어)
    // =====================================================================

    [Theory]
    [InlineData("BT24-028", 4, "", "[On Play] [When Digivolving] By placing 1 level 5 or lower blue [TS] trait Digimon card", "[When Attacking] [Once Per Turn] You may play 1 level 4")]
    [InlineData("BT24-061", 4, "", "[On Play] [When Digivolving] Return 1 of your opponent's play cost 3 or lower Digimon or Tamers to the top of the deck.", "[When Attacking] [Once Per Turn] ＜De-Digivolve 1＞")]
    [InlineData("BT24-068", 3, "", "[On Play] Reveal the top 3 cards of your deck.", "[When Attacking] [Once Per Turn] Trash the top card of both players' decks.")]
    [InlineData("BT26-012", 3, "|applinkdp =", "[Main] [Once Per Turn] You may play or use 1 [TB] trait card", "[When Attacking] [Once Per Turn] 1 of your opponent's Digimon gets -2000 DP for the turn.")]
    [InlineData("BT26-014", 4, "|applinkdp =", "[On Play] [When Digivolving] Delete 1 of your opponent's Digimon with 7000 DP or less.", "[On Deletion] You may play 1 [TB] trait Digimon card")]
    [InlineData("BT26-024", 3, "|applinkdp =", "[Your Turn] When any of your other Digimon with the [Vegetation], [Fairy] or [WG] trait are played", "＜Barrier＞")]
    [InlineData("BT26-027", 4, "|applinkdp =", "[Start of Opponent's Main Phase] [On Play] By suspending 1 of your Digimon with the [Vegetation], [Fairy] or [WG] trait", "＜Barrier＞")]
    [InlineData("BT26-054", 5, "|applinkdp =", "[On Play] [When Digivolving] You may play 1 [CS] trait Tamer card", "[Opponent's Turn] [Once Per Turn] When one of your opponent's Digimon attacks")]
    public void Fill_EmptyDigimonFields_MainAndInherited(string id, int level, string apiInherited, string mainStart, string inheritedStart)
    {
        var d = Digimon(id, level, "", inherited: apiInherited);
        var ch = Fix(d);
        Assert.StartsWith(mainStart, d.EffectText);
        Assert.StartsWith(inheritedStart, d.InheritedEffectText);
        Assert.Contains(ch, x => x.Field == "효과");
        Assert.Contains(ch, x => x.Field == "진화원 효과");
        AllSupported(d);
        Assert.Contains(CardScripts.For(d)!.Effects, e => e.Source == EffectSource.Inherited || d.InheritedEffectText.StartsWith("＜"));
    }

    [Theory]
    [InlineData("BT24-078", 6, "[Trash] [Your Turn] When one of your [Creepymon] attacks")]
    [InlineData("BT26-058", 6, "＜Reboot＞")]
    [InlineData("BT26-060", 7, "＜Security A. +1＞")]
    [InlineData("BT26-103", 7, "＜Piercing＞")]
    public void Fill_EmptyMainField_HighLevel(string id, int level, string mainStart)
    {
        var d = Digimon(id, level, "", inherited: id == "BT24-078" ? "" : "|applinkdp =");
        Fix(d);
        Assert.StartsWith(mainStart, d.EffectText);
        AllSupported(d);
        Assert.NotEmpty(CardScripts.For(d)!.Effects);
    }

    [Fact]
    public void Fill_OnlyWhenApiFieldMatchesExactly()
    {
        // 다른 카드 번호 · 이미 문구가 있는 칸은 그대로
        var other = Digimon(Uid("B83-EMPTY-"), 4, "");
        Assert.Empty(Fix(other));
        Assert.Equal("", other.EffectText);
        var already = Digimon("BT24-061", 4, "[On Play] Gain 1 memory.");
        Fix(already);
        Assert.Equal("[On Play] Gain 1 memory.", already.EffectText);
    }

    [Fact]
    public void Fill_SuccessionKeywords_BT26_060_BlockerRebootSecurityAttack()
    {
        var d = Digimon("BT26-060", 7, "", inherited: "|applinkdp =");
        Fix(d);
        Assert.True(d.IsBlocker);
        Assert.Equal(1, d.SecurityAttackBonus);
        var script = CardScripts.For(d)!;
        Assert.NotEmpty(script.SuccessionSpecs41);
        Assert.Contains(script.Effects, e => e.Trigger == EffectTrigger.AllyEffectAddedToDeck46 && e.OncePerTurn);
    }

    // =====================================================================
    // 2. 테이머 · [시큐리티] · 진화원 효과 · 잘린 줄
    // =====================================================================

    [Theory]
    [InlineData("BT25-088", "[Start of Your Turn] If you have 2 or less memory, set it to 3.")]
    [InlineData("BT26-087", "[Start of Your Main Phase] By returning 1 [TS] trait Digimon card from your trash to the bottom of the deck, gain 1 memory.")]
    [InlineData("BT26-096", "[Start of Your Turn] If you have 2 or less memory, set it to 3.")]
    [InlineData("BT26-104", "[Start of Your Main Phase] Gain 1 memory.")]
    public void Fill_TamerWithOnlySecurityLine_GetsMainEffects(string id, string mainStart)
    {
        var t = Card(id, TamerSecurity, CardType.Tamer, inherited: "[Security] Play this card without paying the cost.");
        Fix(t);
        Assert.StartsWith(mainStart, t.EffectText);
        Assert.DoesNotContain("Security Effect", t.EffectText);
        Assert.Equal("[Security] Play this card without paying the cost.", t.InheritedEffectText);   // [시큐리티] 등장은 그대로
        AllSupported(t);
        Assert.NotEmpty(CardScripts.For(t)!.Effects);
    }

    [Fact]
    public void Fill_BT26_089_BothFieldsWereApiNoise()
    {
        var t = Card("BT26-089", "|inheriteff =", CardType.Tamer, inherited: "|applinkdp =");
        Fix(t);
        Assert.StartsWith("[Start of Your Main Phase] By placing 1 [BEATBREAK] trait card from your hand face down under this Tamer", t.EffectText);
        Assert.Equal(TamerSecurity, t.InheritedEffectText);
        AllSupported(t);
    }

    [Fact]
    public void Fill_MissingSecurityEffects_BT6_096_EX11_071_P_234()
    {
        var trident = Card("BT6-096", "[Main] 1 of your Digimon gets +2000 DP and \"[When Attacking] Return 1 of your opponent's level 3 Digimon to its owner's hand. Trash all of the digivolution cards of that Digimon.\" for the turn.");
        Fix(trident);
        Assert.Equal("Security Effect [Security] Add this card to its owner's hand.", trident.InheritedEffectText);
        AllSupported(trident);
        Assert.Contains(CardScripts.For(trident)!.Effects, e => e.Trigger == EffectTrigger.Security);
        foreach (var id in new[] { "EX11-071", "P-234" })
        {
            var t = Card(id, "[On Play] Gain 1 memory.", CardType.Tamer);
            Fix(t);
            Assert.Equal(TamerSecurity, t.InheritedEffectText);
            AllSupported(t);
        }
    }

    [Fact]
    public void Fill_InheritedAndTruncatedLines_EX11_032_P_191_P_238()
    {
        string first = "[Hand] [Main] If you have [Shoto Kazama], by placing 1 [Galemon] from your trash as any of your [Pteromon]'s bottom digivolution card, it digivolves into this card for a digivolution cost of 3, ignoring digivolution requirements.\r\n";
        var gale = Digimon("EX11-032", 5, first);
        Fix(gale);
        Assert.Contains("[When Digivolving] You may suspend 1 Digimon. Then, you may play 1 3000 DP or lower green Digimon card with [Avian] or [Bird]", gale.EffectText);
        Assert.Equal("[Your Turn] [Once Per Turn] When this Digimon wins a battle, this [Vortex Warriors] trait Digimon may unsuspend.", gale.InheritedEffectText);
        AllSupported(gale);

        const string overflow4 = "Ace Overflow ＜-4＞ (As this card moves from the field or under a card to an area other than those, lose 4 memory.)";
        var apollo = Digimon("P-191", 6, inherited: overflow4);
        Fix(apollo);
        Assert.EndsWith("[End of Your Turn] [Once Per Turn] 1 of your Digimon may attack.", apollo.InheritedEffectText);
        AllSupported(apollo);
        var e = Assert.Single(CardScripts.For(apollo)!.Effects, x => x.Source == EffectSource.Inherited);
        Assert.Equal(EffectTrigger.EndOfYourTurn, e.Trigger);

        var cannon = Card("P-238", "＜Use Req. ([CS] trait)＞ (Specified cards let you ignore color requirements.)\r\n[Main] Delete 1 of your opponent’s level 6 or lower Digimon. Then, place this card in the battle area.");
        Fix(cannon);
        Assert.Contains("[Your Turn] When one of your [CS] trait Digimon attacks, ＜Delay＞.\n・Delete 1 of your opponent's level 6 or lower Digimon.", cannon.EffectText);
        AllSupported(cannon);
    }

    [Fact]
    public void Fill_DualCard_BT26_080_DigimonSideAndOptionSide()
    {
        var bacchus = Digimon("BT26-080", 6, "Option Card Colour Requirements Purple", inherited: "Use Requirement: TS trait");
        bacchus.IsDual = true;
        Fix(bacchus);
        Assert.StartsWith("＜Security A. +1＞", bacchus.EffectText);
        Assert.Contains("Delete 1 of your opponent's Digimon with the same orientation as this Digimon.", bacchus.EffectText);
        Assert.EndsWith("[Main] You may unsuspend 1 Digimon. Then, delete all of your opponent's unsuspended Digimon with the lowest DP.", bacchus.InheritedEffectText);
        AllSupported(bacchus);
    }

    [Fact]
    public void TextFixes_UseReqAndMissingTraits_BT25_085_BT26_033_P_209_BT21_027_GlowingDawn()
    {
        var beel = Digimon("BT25-085", 6, inherited: "[Main] Delete 1 of your opponent's highest level Digimon. Then, you may place 1 card from your hand or trash as any of your Digimon's bottom digivolution card.");
        beel.IsDual = true;
        Fix(beel);
        Assert.StartsWith("＜Use Req. ([Three Musketeers] in text)＞", beel.InheritedEffectText);
        Assert.Contains("place 1 [Three Musketeers] trait card from your hand or trash", beel.InheritedEffectText);
        AllSupported(beel);

        var titamon = Digimon("P-209", 5, "[All Turns] [Once Per Turn] When your hand is trashed from, you may play 1 level 4 or lower [Demon] card from your trash without paying the cost.");
        Fix(titamon);
        Assert.Contains("[Demon] or [Titan] trait card", titamon.EffectText);
        AllSupported(titamon);

        var dx = Digimon("BT21-027", 6, "This card is also treated as [Shoutmon] or [ZeigGreymon] for DigiXros.");
        Fix(dx);
        Assert.Contains("OmniShoutmon", CardScripts.For(dx)!.XrosAliases);
        Assert.DoesNotContain("Shoutmon", CardScripts.For(dx)!.XrosAliases);

        var bearcat = Digimon("BT26-031", 5, inherited: "Use Requirement: GlowingDawn trait\r\n[Main] 1 of your opponent's Digimon gets -8000 DP until their turn ends.");
        bearcat.IsDual = true;
        Fix(bearcat);
        Assert.StartsWith("Use Requirement: Glowing Dawn trait", bearcat.InheritedEffectText);
    }

    // =====================================================================
    // 3. 새 문장 — 엔진
    // =====================================================================

    [Fact]
    public void ReturnToDeckTop_OpponentDigimonOrTamer_BT24_061()
    {
        var vade = Digimon("BT24-061", 4, "");
        Fix(vade);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, vade);
        var src = Digimon(Uid("B83-SRC-"), 3, playCost: 2);
        var victim = Put(state.DummyOpponent, Digimon(Uid("B83-V-"), 4, playCost: 3), src);
        var big = Put(state.DummyOpponent, Digimon(Uid("B83-BIG-"), 5, playCost: 7));
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, vade);
        Assert.DoesNotContain(victim, state.DummyOpponent.BattleArea);
        Assert.Contains(big, state.DummyOpponent.BattleArea);
        Assert.Same(victim.TopCard, state.DummyOpponent.MainDeck[0]);           // 덱 위 (맨 위 카드가 맨 위)
        Assert.Same(src, state.DummyOpponent.MainDeck[1]);

        // 테이머만 있으면 테이머를 덱 위로
        var (state2, engine2) = NewMainPhase();
        var me2 = Put(state2.Player, vade);
        var tamer = Card(Uid("B83-T-"), "", CardType.Tamer, playCost: 3);
        state2.DummyOpponent.Tamers.Add(tamer);
        engine2.RunEffects(EffectTrigger.OnPlay, state2.Player, me2, vade);
        Assert.DoesNotContain(tamer, state2.DummyOpponent.Tamers);
        Assert.Same(tamer, state2.DummyOpponent.MainDeck[0]);
        Assert.Equal(0, engine2.DeckTop83);
        Assert.Equal("【등장 시】 【진화 시】 등장 코스트 3 이하인 상대의 디지몬 또는 테이머 1마리를 덱 위로 되돌린다.", KoreanEffectRenderer.RenderClause(vade.EffectText));
    }

    [Fact]
    public void BottomDeck_Unchanged_WhenNotDeckTopEffect()
    {
        var (state, engine) = NewMainPhase();
        var src = Digimon(Uid("B83-SRC-"), 3);
        var victim = Put(state.DummyOpponent, Digimon(Uid("B83-V-"), 4), src);
        engine.BottomDeck(state.DummyOpponent, victim);
        Assert.Same(victim.TopCard, state.DummyOpponent.MainDeck[^2]);
        Assert.Same(src, state.DummyOpponent.MainDeck[^1]);
    }

    [Fact]
    public void ReturnTopStackedToDeckTop_BT26_060()
    {
        var chrono = Digimon("BT26-060", 7, "", inherited: "|applinkdp =");
        Fix(chrono);
        var (state, engine) = NewMainPhase();
        var me = Put(state.Player, chrono);
        var s1 = Digimon(Uid("B83-S1-"), 4); var s2 = Digimon(Uid("B83-S2-"), 3);
        var small = Put(state.DummyOpponent, Digimon(Uid("B83-SM-"), 5), s1, s2);         // 3장 — 모두 덱으로 (떠난다)
        var tall = Put(state.DummyOpponent, Digimon(Uid("B83-TL-"), 6),
            Digimon(Uid("B83-T1-"), 5), Digimon(Uid("B83-T2-"), 4), Digimon(Uid("B83-T3-"), 4), Digimon(Uid("B83-T4-"), 3), Digimon(Uid("B83-T5-"), 3));   // 6장 — 1장 남는다
        var tallBottom = tall.EvolutionSources.Last();
        int deck0 = state.DummyOpponent.MainDeck.Count;
        engine.RunEffects(EffectTrigger.OnPlay, state.Player, me, chrono);
        Assert.DoesNotContain(small, state.DummyOpponent.BattleArea);
        Assert.Same(tallBottom, tall.TopCard);                                    // 6장 중 위에서부터 5장 → 맨 아래 카드만 남는다
        Assert.Empty(tall.EvolutionSources);
        Assert.Equal(deck0 + 8, state.DummyOpponent.MainDeck.Count);
        // 이어서 이 카드의 "[All Turns] [Once Per Turn] When your effects add to decks, you may delete 1 of your opponent's Digimon" — 남은 디지몬을 소멸
        Assert.DoesNotContain(tall, state.DummyOpponent.BattleArea);
        Assert.Contains(tallBottom, state.DummyOpponent.Trash);
        Assert.Equal("【등장 시】 【진화 시】 상대의 디지몬 3마리에 겹쳐져 있는 카드를 위에서부터 5장 덱 위로 되돌린다.",
            KoreanEffectRenderer.RenderClause("[On Play] [When Digivolving] Return the top 5 stacked cards of 3 of your opponent's Digimon to the top of the deck."));
    }

    [Fact]
    public void SameOrientation_DeletesOpponentDigimonInSameState_BT26_080()
    {
        var bacchus = Digimon("BT26-080", 6, "Option Card Colour Requirements Purple", inherited: "Use Requirement: TS trait");
        bacchus.IsDual = true;
        Fix(bacchus);
        foreach (bool suspended in new[] { true, false })
        {
            var (state, engine) = NewMainPhase();
            var me = Put(state.Player, bacchus);
            me.IsSuspended = suspended;
            var rested = Put(state.DummyOpponent, Digimon(Uid("B83-R-"), 5)); rested.IsSuspended = true;
            var active = Put(state.DummyOpponent, Digimon(Uid("B83-A-"), 5));
            engine.RunEffects(EffectTrigger.WhenAttacking, state.Player, me, bacchus);
            Assert.Equal(suspended, !state.DummyOpponent.BattleArea.Contains(rested));
            Assert.Equal(!suspended, !state.DummyOpponent.BattleArea.Contains(active));
        }
        Assert.Equal("【어택 시】 [턴에 1회] 이 디지몬과 같은 표시 형식(액티브/레스트)인 상대의 디지몬 1마리를 소멸시킨다.",
            KoreanEffectRenderer.RenderClause("[When Attacking] [Once Per Turn] Delete 1 of your opponent's Digimon with the same orientation as this Digimon."));
    }

    [Fact]
    public void SecurityRemovedByEffects_Condition_BT26_089()
    {
        var kyo = Card("BT26-089", "|inheriteff =", CardType.Tamer, inherited: "|applinkdp =");
        Fix(kyo);
        var (state, engine) = NewMainPhase();
        state.Player.Tamers.Add(kyo);
        var enemy = Put(state.DummyOpponent, Digimon(Uid("B83-E-"), 5));
        Assert.Equal(1, enemy.GetSecurityAttackCount());
        // 효과로 줄어든 것이 아니면 (체크 등) 덱 위 1장만 테이머 아래
        engine.RunEffects(EffectTrigger.AllySecurityRemoved, state.Player, null, kyo, includeInherited: false);
        Assert.Single(state.Player.UnderTamers);
        Assert.Equal(1, enemy.GetSecurityAttackCount());
        Assert.Equal("【서로의 턴】 자신의 시큐리티에서 카드가 없어졌을 때, 이 테이머를 레스트시키는 것으로, 자신의 덱 위에서부터 1장을 이 테이머의 아래에 뒷면으로 놓는다. 또한, 효과로 줄어들었다면, 상대의 턴 종료 시까지, 상대의 디지몬 1마리에게 《시큐리티 어택 -1》 (이 디지몬이 체크하는 시큐리티의 매수 -1)을 준다.",
            KoreanEffectRenderer.RenderClause(kyo.EffectText.Split('\n')[1]));
    }

    [Fact]
    public void CostWithThreeTraits_BT26_027_Supported()
    {
        var peter = Digimon("BT26-027", 4, "", inherited: "|applinkdp =");
        Fix(peter);
        AllSupported(peter);
        Assert.Equal("【상대의 메인 페이즈 개시 시】 【등장 시】 특징으로 「식물형」/「요정형」/「WG」을 가진 자신의 디지몬 1마리를 레스트시키는 것으로, 상대의 턴 종료 시까지, 상대의 디지몬 1마리에게 《시큐리티 어택 -2》 (이 디지몬이 체크하는 시큐리티의 매수 -2)를 준다.",
            KoreanEffectRenderer.RenderClause(peter.EffectText));
    }

    [Fact]
    public void ChooseNamedThenDigivolve_EX4_072()
    {
        var trans = Card("EX4-072", "While you have a Tamer, you can ignore this card's color requirements.\r\n[Main] Choose 1 of your level 6 Digimon. It may digivolve into a level 6 Digimon card in your hand with a different name that includes the name of the chosen Digimon, ignoring digivolution requirements and without paying the cost.");
        Fix(trans);
        Assert.Contains("Choose 1 of your [Gallantmon], [Sakuyamon] or [MegaGargomon].", trans.EffectText);
        AllSupported(trans);
        var (state, engine) = NewMainPhase();
        var war = Put(state.Player, Digimon(Uid("B83-WG-"), 6, name: "WarGreymon"));
        state.Player.Hand.Add(Digimon(Uid("B83-WGX-"), 6, name: "WarGreymon X"));
        var gal = Put(state.Player, Digimon(Uid("B83-GAL-"), 6, name: "Gallantmon"));
        var crimson = Digimon(Uid("B83-GCM-"), 6, name: "Gallantmon X Mode");
        state.Player.Hand.Add(crimson);
        engine.RunEffects(EffectTrigger.Main, state.Player, null, trans, includeInherited: false);
        Assert.Same(crimson, gal.TopCard);
        Assert.Equal("WarGreymon", war.TopCard.Name);
        Assert.Equal("【메인】 자신의 「듀크몬」/「샤크라몬」/「세인트가르고몬」 1마리를 고른다. 그 디지몬은 자신의 패에서, 고른 디지몬의 명칭을 포함하고 명칭이 서로 다른 Lv.6의 디지몬 카드로 진화 조건을 무시하고 코스트를 지불하지 않고 진화할 수 있다.",
            KoreanEffectRenderer.RenderClause(trans.EffectText.Split('\n')[1]));
    }

    [Fact]
    public void NameExclusion_BT4_099_DoruGreymonDoesNotCount()
    {
        var heir = Card("BT4-099", "[Main] ＜Draw 2＞ (Draw 2 cards from your deck.) Then, if one of your Digimon has [Greymon] in its name or [Dramon] in its name, delete 1 of your opponent's Digimon with 4000 DP or less.");
        Fix(heir);
        Assert.Contains("(other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon])", heir.EffectText);
        AllSupported(heir);
        foreach (var (name, deletes) in new[] { ("DoruGreymon", false), ("MetalGreymon", true) })
        {
            var (state, engine) = NewMainPhase();
            Put(state.Player, Digimon(Uid("B83-G-"), 5, name: name));
            var enemy = Put(state.DummyOpponent, Digimon(Uid("B83-E-"), 4, dp: 3000));
            engine.RunEffects(EffectTrigger.Main, state.Player, null, heir, includeInherited: false);
            Assert.Equal(deletes, !state.DummyOpponent.BattleArea.Contains(enemy));
        }
    }

    // =====================================================================
    // 3-2. 링크하는 옵션 BT25-100 · BT25-101 (종합 룰 2-3-11-4 · 10-1-1)
    // =====================================================================

    private static CardData IronSlash() => Card("BT25-100",
        "＜Use Req. ([TS] trait)＞ (Specified cards let you ignore color requirements.)\r\n[Security] Activate this card's [Main] effects.\r\n[Main] ＜De-Digivolve 2＞ 1 of your opponent's Digimon. (Trash up to 2 cards from the top. You can't trash past level 3 cards.) Then, you may link this card to 1 of your Digimon on the field without paying the cost.",
        inherited: "＜Collision＞ (During this Digimon's attack, all of your opponent's Digimon gain ＜Blocker＞, and must block if possible.)\n＜Piercing＞", color: CardColor.Purple);

    private static CardData DivineArms() => Card("BT25-101",
        "＜Use Req. ([TS] trait)＞ (Specified cards let you ignore color requirements.)\r\n[Security] Activate this card's [Main] effects.\r\n[Main] By trashing 1 [TS] trait card from your hand, ＜Draw 2＞ (Draw 2 cards from your deck.) After, you may link this card or 1 [TS] trait card from your trash to 1 of your Digimon on the field without paying the cost.",
        inherited: "＜Security A. +1＞\n＜Reboot＞ (Unsuspend this Digimon during your opponent's unsuspend phase.)\r\n\r\n[All Turns] When this would leave the battle area, by trashing 1 of its link cards, it doesn't leave.", color: CardColor.Red);

    [Fact]
    public void LinkOption_BT25_100_IsLinkCard_RequirementCostDpAndLinkKeywords()
    {
        var slash = IronSlash();
        AllSupported(slash);
        Assert.True(SentenceParser.IsLinkCard43(slash));
        Assert.Contains(Keyword.Collision, CardScripts.For(slash)!.LinkKeywords43);
        Assert.Contains(Keyword.Piercing, CardScripts.For(slash)!.LinkKeywords43);
        var (state, engine) = NewMainPhase(memory: 5);
        var ts = Put(state.Player, Digimon(Uid("B83-TS-"), 5, dp: 7000, traits: new() { "TS" }));
        var plain = Put(state.Player, Digimon(Uid("B83-PL-"), 5, dp: 7000));
        state.Player.Hand.Add(slash);
        Assert.Equal(2, engine.GetLinkCost(slash));
        Assert.Equal(new[] { ts }, engine.GetLinkTargets(slash));                  // 링크 조건: 특징 [TS]
        Assert.True(engine.LinkFromHand(slash, ts).CanExecute);
        Assert.Equal(3, state.MemoryGauge);                                       // 링크 코스트 2
        Assert.Equal(9000, ts.GetCurrentDP());                                    // 링크 DP +2000
        Assert.True(CardScripts.HasKeyword(ts, Keyword.Collision));
        Assert.True(CardScripts.HasKeyword(ts, Keyword.Piercing));
        Assert.False(CardScripts.HasKeyword(plain, Keyword.Piercing));
    }

    [Fact]
    public void LinkOption_BT25_101_LinkThisCardOrTrashCard_AndLinkEffects()
    {
        var arms = DivineArms();
        AllSupported(arms);
        Assert.True(SentenceParser.IsLinkCard43(arms));
        var (state, engine) = NewMainPhase();
        var vulcan = Put(state.Player, Digimon(Uid("B83-VUL-"), 6, name: "Vulcanusmon", dp: 12000, traits: new() { "TS" }));
        state.Player.Hand.Add(Digimon(Uid("B83-TSH-"), 3, traits: new() { "TS" }));
        engine.RunEffects(EffectTrigger.Main, state.Player, null, arms, includeInherited: false);
        Assert.Contains(arms, vulcan.LinkedCards);                                 // 이 카드를 링크 (추천)
        Assert.Equal(16000, vulcan.GetCurrentDP());                                // 링크 DP +4000
        Assert.True(CardScripts.HasKeyword(vulcan, Keyword.Reboot));
        Assert.Equal(2, vulcan.GetSecurityAttackCount());

        // 「불카누스몬」이 없으면 이 카드는 링크할 수 없다 — 트래시의 [TS] 카드를 링크
        var (state2, engine2) = NewMainPhase();
        var other = Put(state2.Player, Digimon(Uid("B83-OTH-"), 5, traits: new() { "TS" }));
        var tsTrash = Digimon(Uid("B83-TST-"), 3, traits: new() { "TS" });
        state2.Player.Trash.Add(tsTrash);
        state2.Player.Hand.Add(Digimon(Uid("B83-TSH2-"), 3, traits: new() { "TS" }));
        engine2.RunEffects(EffectTrigger.Main, state2.Player, null, arms, includeInherited: false);
        Assert.DoesNotContain(arms, other.LinkedCards);
        var linked = Assert.Single(other.LinkedCards);                            // 트래시의 [TS] 카드 (비용으로 버린 카드도 트래시에 있다)
        Assert.Contains("TS", linked.Traits);
        Assert.DoesNotContain(linked, state2.Player.Trash);
        Assert.Equal("【메인】 자신의 패에서 특징으로 「TS」를 가진 카드 1장을 파기하는 것으로, 《2 드로우》 (자신의 덱에서 카드를 2장 드로우한다). 그 후, 이 카드 또는 자신의 트래시의 특징으로 「TS」를 가진 카드 1장을 자신의 디지몬 1마리에게 코스트를 지불하지 않고 링크할 수 있다.",
            KoreanEffectRenderer.RenderClause(arms.EffectText.Split('\n')[2]));
    }

    // =====================================================================
    // 4. 명칭 · 특징
    // =====================================================================

    [Fact]
    public void Names_RuleAliases_ColonFix_Sistermon()
    {
        Assert.True(CardNames.Is(Digimon("BT8-061", 5, name: "Thundermon"), "Mamemon"));
        Assert.True(CardNames.Is(Digimon("BT8-062", 6, name: "SkullKnightmon Cavalier Mode"), "DeadlyAxemon"));
        Assert.True(CardNames.Contains(Digimon("BT9-068", 6, name: "Gaiomon"), "Greymon"));
        Assert.True(CardNames.Is(Digimon("P-072", 5, name: "MetalGreymon: Alterous Mode"), "MetalGreymon"));
        Assert.False(CardNames.Is(Digimon(Uid("B83-THN-"), 5, name: "Thundermon"), "Mamemon"));

        var dragon = Digimon("EX1-022", 6, name: "Imperialdramon Dragon Mode");
        var ch = Fix(dragon);
        Assert.Equal("Imperialdramon: Dragon Mode", dragon.Name);
        Assert.Contains(ch, x => x.Field == "이름");
        Assert.True(CardNames.Is(dragon, "Imperialdramon: Dragon Mode"));

        var noir = Digimon("BT23-077", 4, name: "Sistermon Ciel", traits: new() { "Champion", "Puppet", "CS", "Data" });
        Fix(noir);
        Assert.True(CardNames.Is(noir, "Sistermon Noir"));
        Assert.True(CardNames.Is(noir, "Sistermon Ciel"));
        Assert.Contains("Virus", noir.Traits);
        Assert.Contains("Data", noir.Traits);

        var rare = Digimon("P-101", 4, traits: new() { "Undead", "Champion", "Virus" });
        Fix(rare);
        Assert.Contains("Cyborg", rare.Traits);
    }

    [Fact]
    public void Catalog_CacheFileKeepsApiNameAndText()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b83cat_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var dragon = Digimon("EX1-022", 6, name: "Imperialdramon Dragon Mode");
            var vade = Digimon("BT24-061", 4, "");
            var mag = Digimon("BT16-102", 6, color: CardColor.Yellow); mag.SecondaryColor = CardColor.Blue;
            string cache = System.IO.Path.Combine(dir, "cards_cache.json");
            System.IO.File.WriteAllText(cache, System.Text.Json.JsonSerializer.Serialize(new[] { dragon, vade, mag }));
            var svc = new CardCatalogService(dir);
            Assert.Equal("Imperialdramon: Dragon Mode", svc.Catalog.First(x => x.CardId == "EX1-022").Name);
            Assert.NotEqual("", svc.Catalog.First(x => x.CardId == "BT24-061").EffectText);
            Assert.Equal(new[] { CardColor.Yellow, CardColor.Blue, CardColor.Black }, svc.Catalog.First(x => x.CardId == "BT16-102").AllColors);
            svc.SaveCache();
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(cache));
            var saved = doc.RootElement.EnumerateArray().ToList();
            Assert.Equal("Imperialdramon Dragon Mode", saved.First(x => x.GetProperty("CardId").GetString() == "EX1-022").GetProperty("Name").GetString());
            Assert.Equal("", saved.First(x => x.GetProperty("CardId").GetString() == "BT24-061").GetProperty("EffectText").GetString());
            Assert.False(saved.First(x => x.GetProperty("CardId").GetString() == "BT16-102").TryGetProperty("ExtraColors83", out _));
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // =====================================================================
    // 5. 3색 카드
    // =====================================================================

    [Fact]
    public void ThreeColors_AllColorsAndColorChecks_BT16_102()
    {
        var mag = Digimon("BT16-102", 6, color: CardColor.Yellow); mag.SecondaryColor = CardColor.Blue;
        var ch = Fix(mag);
        Assert.Equal(new[] { CardColor.Yellow, CardColor.Blue, CardColor.Black }, mag.AllColors);
        Assert.Contains(ch, x => x.Field == "세 번째 색");
        // 옵션 색 조건: 블랙 옵션을 쓸 수 있다
        var (state, _) = NewMainPhase();
        var d = Put(state.Player, mag);
        Assert.Contains(CardColor.Black, d.Colors);
        Assert.Empty(ActionValidator.MissingOptionColors(state.Player, Card(Uid("B83-BLK-"), "[Main] Gain 1 memory.", color: CardColor.Black)));
        // 진화 조건의 색: 블랙 Lv.6에서 진화하는 카드
        var evo = Digimon(Uid("B83-EVO-"), 7, color: CardColor.Black); evo.EvoRequirementLevel = 6; evo.EvoRequirementColor = CardColor.Black;
        Assert.True(ActionValidator.ValidateEvolution(d, evo).CanExecute);
        // 3색이 아닌 카드 · 두 번째 색이 없는 카드는 그대로
        var two = Digimon(Uid("B83-TWO-"), 6, color: CardColor.Yellow); two.SecondaryColor = CardColor.Blue;
        Fix(two);
        Assert.Equal(new[] { CardColor.Yellow, CardColor.Blue }, two.AllColors);
    }

    [Fact]
    public void ThreeColors_ApiSecondColorDifferent_AddsMissingOfficialColor_BT18_097()
    {
        var opt = Card("BT18-097", "[Main] Gain 1 memory.", color: CardColor.Yellow); opt.SecondaryColor = CardColor.Black;
        Fix(opt);
        Assert.Equal(3, opt.AllColors.Count);
        Assert.Contains(CardColor.Purple, opt.AllColors);
        // 옵션의 색 조건도 3색 모두
        var (state, _) = NewMainPhase();
        Put(state.Player, Digimon(Uid("B83-Y-"), 5, color: CardColor.Yellow));
        Put(state.Player, Digimon(Uid("B83-K-"), 5, color: CardColor.Black));
        Assert.Equal(new[] { CardColor.Purple }, ActionValidator.MissingOptionColors(state.Player, opt));
    }

    // =====================================================================
    // 6. 덱 구성 규칙
    // =====================================================================

    [Fact]
    public void DeckRules_FiftyCopiesAndSharedCardNumber()
    {
        Assert.Equal(50, DeckValidator.MaxCopiesFor("BT6-085"));
        Assert.Equal(50, DeckValidator.MaxCopiesFor("EX2-046"));
        Assert.Equal(4, DeckValidator.MaxCopiesFor("BT6-084"));
        var eos = Digimon("BT6-085", 5);
        var deck = Enumerable.Repeat(eos, 50).ToList();
        var (ok, errors) = DeckValidator.ValidateDeck(deck, new List<CardData>());
        Assert.True(ok, string.Join(" / ", errors));

        var rb = Digimon("RB1-004", 3); var p = Digimon("P-009", 3);
        var mixed = Enumerable.Repeat(rb, 3).Concat(Enumerable.Repeat(p, 2)).Concat(Enumerable.Repeat(eos, 45)).ToList();
        var (ok2, errors2) = DeckValidator.ValidateDeck(mixed, new List<CardData>());
        Assert.False(ok2);
        Assert.Contains(errors2, e => e.Contains("RB1-004") && e.Contains("P-009"));
        Assert.Equal(5, DeckValidator.CopiesInDeck(mixed, "P-009"));
    }
}
