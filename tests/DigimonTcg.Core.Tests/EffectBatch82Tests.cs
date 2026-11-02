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
/// v0.82.0 56차 묶음: 영어 API 데이터 오류를 공식 값으로 — 공식 영어 · 공식 일본어 · 공식 한글 중 둘 이상이 같을 때만 (메모리에서만, 캐시 파일은 API 값).
/// 한글 데이터가 없는 카드의 수치 · 색 · 링크 카드의 링크 효과 시점 · 빠진 링크 효과 · 【소멸 시】 링크 효과 · 효과 시점 / [턴에 1회] · LM-025 ·
/// 〈룰〉 특징 · 속성 / 유형 · 형태 · BT8-081 · LM-025 한글 시점 표기 · 공식 영어 옛 표기 특수 진화 조건 · 공식 한글에 빠진 특수 진화 조건
/// </summary>
[Collection("KoreanCardData")]
public class EffectBatch82Tests
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
        string inherited = "", int evoCost = 1, List<string>? traits = null) =>
        Card(id, main, CardType.Digimon, level, 3, color, dp, name, inherited, evoCost, traits);

    private static (GameState State, GoldfishEngine Engine) NewMainPhase(int memory = 8)
    {
        var state = new GameState { CurrentTurn = 2, CurrentPhase = Phase.Main, TurnSequence = 3, MemoryGauge = memory };
        for (int i = 0; i < 10; i++)
        {
            state.Player.MainDeck.Add(Digimon("B82-FILL" + i, 3, color: CardColor.White));
            state.DummyOpponent.MainDeck.Add(Digimon("B82-OFILL" + i, 3, color: CardColor.White));
        }
        for (int i = 0; i < 5; i++)
        {
            state.Player.SecurityStack.Add(Digimon("B82-SEC" + i, 3, color: CardColor.White, dp: 1000));
            state.DummyOpponent.SecurityStack.Add(Digimon("B82-OSEC" + i, 3, color: CardColor.White, dp: 1000));
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

    private static void WithOfficialData(IEnumerable<EnglishCardRule> english, IEnumerable<KoreanCard> korean, Action run)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b82_" + Guid.NewGuid().ToString("N"));
        var kr = System.IO.Path.Combine(dir, "kr");
        System.IO.Directory.CreateDirectory(kr);
        var file = System.IO.Path.Combine(kr, "cards_ko.json");
        var ids = korean.Select(e => e.CardId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(KoreanCardData.All.Where(k => !ids.Contains(k.CardId)).Concat(korean).ToList()));
        try { KoreanCardData.Reload(file); EnglishCardRules.Use(english); run(); }
        finally { KoreanCardData.Reload(); EnglishCardRules.Reload(); EvoRequirements48.ClearCache(); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>공식 데이터 없이 (형태 Form을 넣지 않게) 카드 1장을 고친다</summary>
    private static List<OfficialCardData81.Change> Fix(CardData c)
    {
        List<OfficialCardData81.Change> ch = new();
        WithOfficialData(Array.Empty<EnglishCardRule>(), Array.Empty<KoreanCard>(), () => ch = OfficialCardData81.Apply(c));
        return ch;
    }

    private const string Plug = "(Plug this card from the hand or battle area sideways into the specified Digimon in the battle area.)";
    private static readonly List<string> AppTraits = new() { "Medical (App Name)", "Stnd." };

    // =====================================================================
    // 1. 수치 · 색 (한글 데이터 없음 — 공식 영어 = 공식 일본어)
    // =====================================================================

    [Fact]
    public void Values_EnglishOnlyCards_OfficialEnglishAndJapaneseAgree()
    {
        var sango = Digimon("EX11-013", 3, dp: 2000);
        var seiten = Digimon("EX12-048", 6, dp: 12000); seiten.PlayCost = 5;
        var yoshino = Card("BT26-091", type: CardType.Tamer, playCost: 4);
        var misty = Digimon("EX13-033", 5, color: CardColor.Yellow, dp: 7000);
        Fix(sango); Fix(seiten); Fix(yoshino); Fix(misty);
        Assert.Equal(1000, sango.DP);
        Assert.Equal(13, seiten.PlayCost); Assert.Equal(13000, seiten.DP);
        Assert.Equal(3, yoshino.PlayCost);
        Assert.Equal(CardColor.Red, misty.SecondaryColor);
        Assert.Equal(new[] { CardColor.Yellow, CardColor.Red }, misty.AllColors);
        // API 값이 이미 다르면 (데이터가 고쳐졌으면) 그대로
        var sango2 = Digimon("EX11-013", 3, dp: 1500);
        Fix(sango2);
        Assert.Equal(1500, sango2.DP);
    }

    // =====================================================================
    // 2. 링크 카드의 링크 효과 시점 · 빠진 링크 효과
    // =====================================================================

    [Fact]
    public void Link_WhenLinkingTimingWritten_StillOnLinkedEffect_BT21_018()
    {
        var dog = Digimon("BT21-018", 3, inherited: "This Digimon may attack.", traits: new() { "Search (App Name)" });
        var ch = Fix(dog);
        Assert.Equal("[When Linking] This Digimon may attack.", dog.InheritedEffectText);
        Assert.Contains(ch, x => x.Field == "진화원 효과");
        AllSupported(dog);
        var script = CardScripts.For(dog)!;
        Assert.Contains(script.Effects, e => e.Trigger == EffectTrigger.OnLinked && e.Source == EffectSource.Main);   // 전과 같은 【링크 시】 (링크된 디지몬 기준)
        Assert.DoesNotContain(script.Effects, e => e.Source == EffectSource.Inherited);
        Assert.Equal("【링크 시】 이 디지몬으로 어택할 수 있다.", KoreanEffectRenderer.RenderClause(dog.InheritedEffectText));
    }

    [Fact]
    public void Link_OnDeletionLinkEffect_RunsWhenLinkedDigimonIsDeleted_BT24_036()
    {
        var medic = Digimon("BT24-036", 3, "[On Play] [On Deletion] 1 of your opponent's Digimon gets -3000 DP for the turn.",
            inherited: "1 of your opponent's Digimon gets -5000 DP for the turn.", traits: AppTraits);
        Fix(medic);
        Assert.Equal("[On Deletion] 1 of your opponent's Digimon gets -5000 DP for the turn.", medic.InheritedEffectText);
        AllSupported(medic);
        Assert.Contains(CardScripts.For(medic)!.Effects, e => e.Trigger == EffectTrigger.OnDeletion && e.Source == EffectSource.Link43);

        var (state, engine) = NewMainPhase();
        var host = Put(state.Player, Digimon(Uid("B82-HOST-"), 5, dp: 6000));
        host.LinkedCards.Add(medic);
        var enemy = Put(state.DummyOpponent, Digimon(Uid("B82-EN-"), 5, dp: 7000));
        Assert.True(engine.DeleteFrom(state.Player, host));
        Assert.Contains(medic, state.Player.Trash);
        Assert.Equal(2000, enemy.GetCurrentDP());                               // 링크 효과 【소멸 시】 DP -5000

        // 링크되지 않은 채(진화원)로는 발휘하지 않는다
        var (state2, engine2) = NewMainPhase();
        var host2 = Put(state2.Player, Digimon(Uid("B82-HOST2-"), 5, dp: 6000), medic);
        var enemy2 = Put(state2.DummyOpponent, Digimon(Uid("B82-EN2-"), 5, dp: 7000));
        Assert.True(engine2.DeleteFrom(state2.Player, host2));
        Assert.Equal(7000, enemy2.GetCurrentDP());
    }

    [Fact]
    public void Link_MissingLinkEffectAppended_BT24_057_And_Keyword_BT22_055()
    {
        string req = "Link Requirements [Link] [Appmon] trait: Cost 2 \r\n" + Plug;
        var doc = Digimon("BT24-057", 3, inherited: req, traits: AppTraits);
        Fix(doc);
        Assert.EndsWith(Plug + "\n[On Deletion] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)", doc.InheritedEffectText);
        AllSupported(doc);
        Assert.Contains(CardScripts.For(doc)!.Effects, e => e.Trigger == EffectTrigger.OnDeletion && e.Source == EffectSource.Link43);

        var reco = Digimon("BT22-055", 3, inherited: "Link Requirements [Link] [Appmon] trait: Cost 1 \r\n" + Plug, traits: AppTraits);
        Fix(reco);
        AllSupported(reco);
        Assert.Contains(Keyword.Blocker, CardScripts.For(reco)!.LinkKeywords43);  // 링크 효과 ≪블로커≫ (진화원 효과 아님)
        Assert.False(reco.IsBlocker);
    }

    [Fact]
    public void Link_KeywordFixedSentence_BT23_052()
    {
        var consul = Digimon("BT23-052", 3, inherited: "This Digimon gains and until your opponent's turn ends.", traits: AppTraits);
        Fix(consul);
        Assert.StartsWith("[When Linking] This Digimon gains ＜Reboot＞", consul.InheritedEffectText);
        AllSupported(consul);
        Assert.Contains(CardScripts.For(consul)!.Effects, e => e.Trigger == EffectTrigger.OnLinked);
    }

    // =====================================================================
    // 3. 효과 시점 · [턴에 1회] · LM-025
    // =====================================================================

    [Fact]
    public void Timing_OncePerTurnAndTimingFixes_ExactApiTextOnly()
    {
        var gokuu = Digimon("EX6-023", 5, "[On Play] [When Attacking] [Once Per Turn] 1 Digimon may gain ＜Security A. -1＞ (This Digimon checks 1 additional security card.) until the end of your opponent's turn. Then, if DigiXrosing, delete 1 of your opponent's Digimon with 6000 DP or less.");
        Fix(gokuu);
        Assert.StartsWith("[On Play] [When Attacking] 1 Digimon may gain", gokuu.EffectText);
        var deputy = Digimon("EX7-010", 4, "[When Digivolving] [When Attacking] You may trash any 1 Option card from 1 Digimon's digivolution cards.");
        Fix(deputy);
        Assert.StartsWith("[When Digivolving] [When Attacking] [Once Per Turn] You may trash", deputy.EffectText);
        var turbo = Digimon("EX11-029", 5, "[On Play] [When Digivolving] You may link 1 [Maquinamon] from your hand or this Digimon's digivolution cards to 1 of your Digimon without paying the cost.");
        Fix(turbo);
        Assert.StartsWith("[When Moving] [When Digivolving] You may link", turbo.EffectText);
        var st23 = Card("ST23-13", "[All Turns] When effects trash cards from under this Tamer, by suspending this Tamer, 1 of your [Glowing Dawn] trait Digimon gets +3000 DP until your opponent's turn ends.", CardType.Tamer);
        Fix(st23);
        Assert.StartsWith("[Your Turn] When effects trash cards", st23.EffectText);
        // 다른 카드 번호는 그대로
        var other = Digimon(Uid("B82-OTHER-"), 4, "[When Digivolving] [When Attacking] You may trash any 1 Option card from 1 Digimon's digivolution cards.");
        Fix(other);
        Assert.DoesNotContain("[Once Per Turn]", other.EffectText);
    }

    [Fact]
    public void Timing_OncePerTurnRemoved_ForgeBeemon_CanPreventTwice_BT19_048()
    {
        var forge = Digimon("BT19-048", 4, "[All Turns] [Once Per Turn] When any of your other Digimon with the [Royal Base] trait would leave the battle area by effects, by placing this Digimon as the face-up bottom security card, they don't leave.");
        Fix(forge);
        Assert.StartsWith("[All Turns] When any of your other Digimon", forge.EffectText);
        AllSupported(forge);
        Assert.DoesNotContain(CardScripts.For(forge)!.Effects, e => e.OncePerTurn);
    }

    [Fact]
    public void Inherited_WrongOrDuplicatedApiText_ReplacedOrRemoved_BT25_026_BT6_084()
    {
        var cresce = Digimon("BT25-026", 5, inherited: "＜Security A. +1＞ (This Digimon checks 1 additional security card.)");
        Assert.Equal(1, cresce.InheritedSecurityAttackBonus);
        Fix(cresce);
        KeywordDictionary.ApplyDataKeywords(cresce);
        Assert.Equal("[Your Turn] This Digimon's attack target can't change.", cresce.InheritedEffectText);
        Assert.Equal(0, cresce.InheritedSecurityAttackBonus);
        AllSupported(cresce);

        var ciel = Digimon("BT6-084", 4, "[All Turns] All of your Digimon with [Huckmon] in their name or [Royal Knight] trait get +2000 DP.\r\n[On Play] Gain 1 memory.",
            inherited: "Card Effect(s) [All Turns] All of your Digimon with [Huckmon] in their name or [Royal Knight] trait get +2000 DP.\r\n[On Play] Gain 1 memory.");
        Fix(ciel);
        Assert.Equal("", ciel.InheritedEffectText);                                   // 공식 카드에는 진화원 효과가 없다
        Assert.DoesNotContain(CardScripts.For(ciel)!.Effects, e => e.Source == EffectSource.Inherited);
    }

    [Fact]
    public void Timing_AceInheritedEffect_LM025_WhenAttackingOncePerTurn()
    {
        const string overflow3 = "Ace Overflow ＜-3＞ (As this card moves from the field or under a card to an area other than those, lose 3 memory.)";
        var cyber = Digimon("LM-025", 5, inherited: overflow3);
        Fix(cyber);
        Assert.StartsWith(overflow3, cyber.InheritedEffectText);
        Assert.EndsWith("[When Attacking] [Once Per Turn] ＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.)", cyber.InheritedEffectText);
        AllSupported(cyber);
        var e = Assert.Single(CardScripts.For(cyber)!.Effects, x => x.Source == EffectSource.Inherited);
        Assert.Equal(EffectTrigger.WhenAttacking, e.Trigger);
        Assert.True(e.OncePerTurn);
    }

    [Fact]
    public void KoreanTimingToken_FollowsOfficialEnglishAndJapanese_BT8_081_LM_025()
    {
        WithOfficialData(Array.Empty<EnglishCardRule>(), new[]
        {
            new KoreanCard { CardId = "BT8-081", Name = "라센몬 격앙 모드", Type = "디지몬", MainText = "【어택 시】테스트 A.\n【자신의 턴 종료 시】테스트 B.", SourceText = "【자신의 턴】테스트 C." },
            new KoreanCard { CardId = "LM-025", Name = "사이버드라몬", Type = "디지몬", MainText = "【등장 시】테스트.", SourceText = "《오버플로우 《-3》》(설명)【메인】[턴에 1회]테스트 D." },
        }, () =>
        {
            Assert.StartsWith("【어택 종료 시】테스트 A.", KoreanCardData.Get("BT8-081")!.MainText);
            Assert.Contains("【자신의 턴 종료 시】테스트 B.", KoreanCardData.Get("BT8-081")!.MainText);
            Assert.Contains("【어택 시】[턴에 1회]테스트 D.", KoreanCardData.Get("LM-025")!.SourceText);
        });
    }

    // =====================================================================
    // 4. 특징: 〈룰〉 특징 · 속성 / 유형 · 형태
    // =====================================================================

    [Fact]
    public void Traits_RuleTraitAttributeAndGarbage()
    {
        var shak = Digimon("BT23-032", 5, traits: new() { "Mutant", "Hudie", "CS", "Free" });
        Fix(shak);
        Assert.Contains("Angel", shak.Traits);                                    // 〈룰〉 특징: 유형 「천사형」
        Assert.True(TargetSpec.HasTrait(shak, "Angel"));
        var sala = Digimon("BT9-072", 3, traits: new() { "Mammal", "Data" });
        Fix(sala);
        Assert.Contains("Vaccine", sala.Traits); Assert.DoesNotContain("Data", sala.Traits);
        var darc = Digimon("BT17-063", 4, traits: new() { "Angel", "Vaccine", "3" });
        Fix(darc);
        Assert.DoesNotContain("3", darc.Traits);
        var bulu = Digimon("EX7-016", 3, traits: new() { "Mini Dragon", "Data" });
        Fix(bulu);
        Assert.Contains("Ice-Snow", bulu.Traits);                                // EX7 「빙설형」 덱의 서치 대상
    }

    [Fact]
    public void RuleNames_OfficialEnglishAndJapanese()
    {
        var marcus = Card("AD1-021", type: CardType.Tamer, name: "Marcus Damon & Agumon");
        Assert.True(CardNames.Is(marcus, "Marcus Damon"));
        Assert.False(CardNames.Is(marcus, "Agumon"));                              // 공식 〈룰〉은 「다이몬 마사루」만 (전에는 " & "로 나눠 「아구몬」으로도 봤다)
        var trio = Card("AD1-020", type: CardType.Tamer, name: "Tommy, Takuya, & Zoe");
        Assert.True(CardNames.Is(trio, "Takuya Kanbara"));
        Assert.True(CardNames.Is(trio, "Zoe Orimoto"));
        var thunder = Digimon("EX13-053", 4, name: "Thundermon");
        Assert.True(CardNames.Contains(thunder, "Mamemon"));
        Assert.True(CardNames.Is(Digimon("P-223", 6, name: "Kuzuhamon"), "Sakuyamon"));
    }

    [Fact]
    public void Traits_FormFromOfficialEnglish_AddedOnlyWhenMissing()
    {
        string a = Uid("B82-FORM-"), b = Uid("B82-FORM2-"), c = Uid("B82-FORM3-");
        WithOfficialData(new[]
        {
            new EnglishCardRule { CardId = a, Form = "Mega" },
            new EnglishCardRule { CardId = b, Form = "Ultimate" },
            new EnglishCardRule { CardId = c, Form = "Stnd./Appmon" },
        }, Array.Empty<KoreanCard>(), () =>
        {
            var x = Digimon(a, 6, traits: new() { "Holy Knight", "Vaccine" });
            var y = Digimon(b, 5, traits: new() { "Ultimate", "Data" });
            var z = Digimon(c, 3, traits: new() { "Calendar (App Name)", "Stnd.", "Tool" });
            OfficialCardData81.Apply(x); OfficialCardData81.Apply(y); OfficialCardData81.Apply(z);
            Assert.Equal(new[] { "Mega", "Holy Knight", "Vaccine" }, x.Traits);
            Assert.Equal(new[] { "Ultimate", "Data" }, y.Traits);                  // 이미 있으면 그대로
            Assert.Equal(new[] { "Appmon", "Calendar (App Name)", "Stnd.", "Tool" }, z.Traits);
        });
        // 공식 영어 데이터가 없으면 형태를 넣지 않는다
        var w = Digimon(Uid("B82-FORM4-"), 6, traits: new() { "Holy Knight" });
        Fix(w);
        Assert.Equal(new[] { "Holy Knight" }, w.Traits);
    }

    [Fact]
    public void Catalog_UsesOfficialTraitsAndColor_ButCacheFileKeepsApiValues()
    {
        WithOfficialData(Array.Empty<EnglishCardRule>(), Array.Empty<KoreanCard>(), () =>
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtcg_b82cat_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                var shak = Digimon("BT23-032", 5, traits: new() { "Mutant", "Hudie", "CS", "Free" }, dp: 8000);
                var misty = Digimon("EX13-033", 5, color: CardColor.Yellow, dp: 7000);
                string cache = System.IO.Path.Combine(dir, "cards_cache.json");
                System.IO.File.WriteAllText(cache, System.Text.Json.JsonSerializer.Serialize(new[] { shak, misty }));
                var svc = new CardCatalogService(dir);
                Assert.Contains("Angel", svc.Catalog.First(x => x.CardId == "BT23-032").Traits);
                Assert.Equal(CardColor.Red, svc.Catalog.First(x => x.CardId == "EX13-033").SecondaryColor);
                svc.SaveCache();
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(cache));
                var saved = doc.RootElement.EnumerateArray().ToList();
                var s1 = saved.First(x => x.GetProperty("CardId").GetString() == "BT23-032");
                Assert.DoesNotContain(s1.GetProperty("Traits").EnumerateArray(), t => t.GetString() == "Angel");   // 파일에는 API 값 그대로
                var s2 = saved.First(x => x.GetProperty("CardId").GetString() == "EX13-033");
                Assert.Equal(System.Text.Json.JsonValueKind.Null, s2.GetProperty("SecondaryColor").ValueKind);
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
        });
    }

    // =====================================================================
    // 5. 특수 진화 조건: 공식 영어 옛 표기 · 공식 한글에 빠진 조건
    // =====================================================================

    [Fact]
    public void EnglishOldNotation_ConvertedToCurrentNotation()
    {
        Assert.Equal("[Digivolve] [Agumon]: Cost 0", KoreanEvolution47.OldEnglishToNew82("[Digivolve] 0 from [Agumon]"));
        Assert.Equal("[Digivolve] Lv.6 w/[Royal Knight] trait: Cost 5", KoreanEvolution47.OldEnglishToNew82("[Digivolve] 5 from Lv.6 w/[Royal Knight] in traits"));
        Assert.Equal("[Digivolve] Name contains [Dragon Mode]: Cost 2", KoreanEvolution47.OldEnglishToNew82("[Digivolve]2 if name contains [Dragon Mode]"));
        Assert.Equal("[Digivolve] Lv.4 w/[Growlmon] in name: Cost 3", KoreanEvolution47.OldEnglishToNew82("[Digivolve]3 from Lv.4 if name contains [Growlmon]"));
        Assert.Equal("[Digivolve] [Koromon]: Cost 0", KoreanEvolution47.OldEnglishToNew82("[Digivolve] 0 from Koromon"));
        Assert.Equal("[Digivolve] [Psychemon]: Cost 5/yellow/green/purple Lv.4 w/＜Save＞ in text: Cost 3",
            KoreanEvolution47.OldEnglishToNew82("[Digivolve] 5 from [Psychemon]/3 from yellow, green, or purple Lv.4 w/＜Save＞ text"));
        string now = "[Digivolve] Lv.2 w/[Appmon] trait: Cost 0";
        Assert.Equal(now, KoreanEvolution47.OldEnglishToNew82(now));
    }

    [Fact]
    public void EnglishOldNotation_GivesDigivolvePath_WithoutKoreanData()
    {
        string evoId = Uid("B82-OLD-"), rkId = Uid("B82-RK-"), dmId = Uid("B82-DM-");
        WithOfficialData(new[]
        {
            new EnglishCardRule { CardId = evoId, SpecialDigivolve = new() { "[Digivolve] 0 from [B82 Agumon]", "[Digivolve] 5 from Lv.6 w/[Royal Knight] in traits" } },
            new EnglishCardRule { CardId = dmId, SpecialDigivolve = new() { "[Digivolve]2 if name contains [Dragon Mode]" } },
            new EnglishCardRule { CardId = rkId, SpecialDigivolve = new() { "[Digivolve] 3 from [B82 Alphamon] w/ [B82 Ouryumon] digivolution card" } },
        }, Array.Empty<KoreanCard>(), () =>
        {
            var evo = Digimon(evoId, 7, evoCost: 6);
            var p = KoreanEvolution47.For(evo);
            Assert.Empty(p.Unparsed);
            var agu = Digimon(Uid("B82-AGU-"), 3, name: "B82 Agumon");
            var rk = Digimon(Uid("B82-RKC-"), 6, name: "B82 Knight", traits: new() { "Holy Knight", "Royal Knight" });
            var (state, engine) = NewMainPhase();
            Assert.Equal(0, KoreanEvolution47.Cost(evo, Put(state.Player, agu)));
            Assert.Equal(5, KoreanEvolution47.Cost(evo, Put(state.Player, rk)));
            Assert.Null(KoreanEvolution47.Cost(evo, Put(state.Player, Digimon(Uid("B82-NO-"), 6, name: "B82 Other"))));

            var dm = Digimon(dmId, 6);
            Assert.Equal(2, KoreanEvolution47.Cost(dm, Put(state.Player, Digimon(Uid("B82-IMP-"), 6, name: "Imperialdramon: Dragon Mode"))));

            var proxima = Digimon(rkId, 7);
            var alpha = Digimon(Uid("B82-ALPHA-"), 6, name: "B82 Alphamon");
            var ou = Digimon(Uid("B82-OU-"), 6, name: "B82 Ouryumon");
            Assert.Equal(3, KoreanEvolution47.Cost(proxima, Put(state.Player, alpha, ou)));
            Assert.Null(KoreanEvolution47.Cost(proxima, Put(state.Player, Digimon(Uid("B82-ALPHA2-"), 6, name: "B82 Alphamon"))));   // 진화원에 「오류우몬」이 없다
        });
    }

    [Fact]
    public void KoreanTextWithoutSpecialCondition_FallsBackToOfficialEnglish_BT9_012()
    {
        string evoId = Uid("B82-GREYX-");
        WithOfficialData(new[] { new EnglishCardRule { CardId = evoId, SpecialDigivolve = new() { "[Digivolve] 0 from [B82 Greymon]" } } },
            new[] { new KoreanCard { CardId = evoId, Name = "그레이몬 X항체", Type = "디지몬", MainText = "-" } }, () =>
        {
            var evo = Digimon(evoId, 4, evoCost: 2);
            var cond = Assert.Single(KoreanEvolution47.For(evo).Conditions, c => !c.TamerName);
            Assert.Equal(0, cond.Cost);
            var (state, engine) = NewMainPhase();
            var grey = Put(state.Player, Digimon(Uid("B82-GREY-"), 4, name: "B82 Greymon"));   // 같은 Lv.4 — 특수 진화 조건으로만 진화
            state.Player.Hand.Add(evo);
            Assert.True(engine.EvolveDigimon(grey, evo).CanExecute);
            Assert.Equal(8, state.MemoryGauge);
        });
    }
}
