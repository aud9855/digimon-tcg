using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

/// <summary>한국 공식 사이트(digimoncard.co.kr)에서 받은 카드 1장 — tools/krsite/collect_kr.ps1 출력 형식</summary>
public sealed class KoreanCard
{
    public string CardId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // 디지타마 · 디지몬 · 테이머 · 옵션
    public string Level { get; set; } = string.Empty;
    public List<string> Colors { get; set; } = new();
    public List<string> Traits { get; set; } = new();
    public string? PlayCost { get; set; }                       // v0.80.0 영어 카드와 대조용 (표시에는 쓰지 않는다)
    public string? DP { get; set; }
    public string? MainText { get; set; }                       // 상단 텍스트 (〔진화〕 특수 진화 조건 + 메인 효과)
    public string? SourceText { get; set; }                     // 하단 텍스트 (디지몬: 진화원 효과 / 테이머 · 옵션: [시큐리티] 효과)
    public string Image { get; set; } = string.Empty;
    public List<string> ParallelImages { get; set; } = new();
    public string Set { get; set; } = string.Empty;

    /// <summary>v0.80.0 한글 ↔ 영어 대조: 이 카드의 공식 한글 메인 문구는 다른 카드의 문구이거나 깨져 있어 쓰지 않는다 (표시는 생성 한글, 〔진화〕 조건은 공식 영어)</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool MainFromEnglish80 { get; set; }
    /// <summary>v0.80.0 한글 ↔ 영어 대조: 공식 한글 하단 문구(진화원 효과)가 빠졌거나 틀려 쓰지 않는다 (표시는 생성 한글)</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool SourceFromEnglish80 { get; set; }
}

/// <summary>
/// 화면 표시용 공식 한글 카드 데이터 (data/kr/cards_ko.json, 카드 번호 기준).
/// 대전 엔진은 계속 영어 API 데이터(Name · EffectText)로 판정하고, 이 데이터는 이름 · 특징 · 효과 문구 표시에만 쓴다.
/// 파일이 없으면 아무 카드도 찾지 못하고, 화면은 지금처럼 원래 데이터를 보여 준다.
/// </summary>
public static class KoreanCardData
{
    private static Lazy<Dictionary<string, KoreanCard>> _cards = new(() => Load(DefaultPath()));

    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(CardCatalogService.ResolveImagesDirectory()) ?? ".", "kr", "cards_ko.json");

    public static int Count => _cards.Value.Count;

    public static KoreanCard? Get(string cardId) =>
        _cards.Value.TryGetValue(cardId, out var card) ? card : null;

    public static IReadOnlyCollection<KoreanCard> All => _cards.Value.Values;

    /// <summary>다른 파일로 다시 읽는다 (테스트 · 수집 후 새로 고침용)</summary>
    public static void Reload(string? path = null)
    {
        var p = path ?? DefaultPath();
        _cards = new Lazy<Dictionary<string, KoreanCard>>(() => Load(p));
    }

    private static Dictionary<string, KoreanCard> Load(string path)
    {
        var map = new Dictionary<string, KoreanCard>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return map;
        try
        {
            var list = JsonSerializer.Deserialize<List<KoreanCard>>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var c in list ?? new())
            {
                if (!string.IsNullOrWhiteSpace(c.CardId))
                    map[c.CardId.Trim()] = c;
            }
        }
        catch
        {
            // 파일이 깨졌으면 한글 표시 없이 진행
        }
        try { Audit80(map, Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path))) ?? ".", "cards_cache.json")); }
        catch { /* 대조하지 못하면 한글 데이터 그대로 */ }
        return map;
    }

    // ---- v0.80.0 한글 ↔ 영어 카드 동일성 대조 ----
    // 한국 사이트 데이터(cards_ko.json)와 영어 데이터(cards_cache.json) · 공식 영어 카드 리스트를 카드 번호마다 대조한 결과 (scratchpad 감사 — 3,814장).
    // 다른 카드가 같은 번호에 붙은 경우는 없었고 (이름 · 종류 · Lv. · 색 · 코스트 · DP), 한국 사이트 문구가 틀린 카드만 있었다.
    // 카드 문구는 코드에 넣지 않는다 — 카드 번호와 고칠 수치만.

    /// <summary>공식 한글 메인 문구가 그 카드의 것이 아닌 카드 (공식 영어 카드 리스트 · 한글 카드 이미지와 대조)</summary>
    internal static readonly IReadOnlyDictionary<string, string> MainFromEnglish80Ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["EX9-062"] = "다른 카드의 문구 (〔진화〕 Lv.3 · 코스트 2 · 《트레이닝》 · 【어택 시】 — 카드 이미지와 공식 영어는 〔진화〕 Lv.4 · 코스트 3 · 【등장 시】【진화 시】)",
        ["EX1-029"] = "메인 문구가 비어 있고 하단 문구가 메인 효과를 바꿔 적은 것 (+4000 → +1000 · [턴에 1회])",
        ["EX7-022"] = "〔진화〕 조건 뒤가 잘림 (【등장 시】 · 【자신의 턴】 효과가 없다)",
        ["P-011"] = "메인 문구가 진화원 효과와 같다 (【어택 시】 파란 테이머 · 덱 위 3장 효과가 없다)",
        // v0.81.0 요약체 문구 (공식 한글 표기가 아닌 줄임말 4장) 중 뜻이 틀린 카드 (요약체이지만 뜻이 맞는 BT11-069 · BT16-026은 그대로)
        ["BT23-032"] = "요약체 — 【서로의 턴】 효과의 \"자신의\" 효과 이외가 그냥 효과 이외로 줄었다 (한글 카드 이미지 · 공식 영어 other than by your effects) · 진화원 효과가 빠짐",
        ["EX9-021"] = "요약체 — 【진화 시】 기간이 다르다 (한글 카드 이미지 · 영어 API: 이 턴 동안) · 【어택 종료 시】 이 효과로 등장시켰을 때만 시큐리티 위에 놓는 조건이 빠짐",
    };

    /// <summary>공식 한글 하단 문구(진화원 효과)가 빠진 카드 — 공식 영어 카드 리스트에는 진화원 효과가 있다</summary>
    internal static readonly IReadOnlySet<string> SourceFromEnglish80Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "EX1-029", "BT3-068", "BT17-001", "BT17-002", "BT17-003", "BT17-004", "BT17-005", "BT17-006",
        "BT23-032",   // v0.81.0 요약체 — 진화원 효과가 빠짐 (공식 영어에는 【서로의 턴】[턴에 1회] 진화원 효과)
    };

    /// <summary>〔진화〕 조건의 Lv.가 카드 이미지 · 공식 영어와 다른 카드: (틀린 Lv., 맞는 Lv.)</summary>
    internal static readonly IReadOnlyDictionary<string, (int Wrong, int Right)> EvoLevelFix80 = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
    {
        ["BT19-053"] = (4, 5),   // 카드 이미지 · 공식 영어 "[Digivolve] Lv.5 w/[Royal Base] trait: Cost 3"
    };

    /// <summary>같은 번호의 영어 카드와 다른 카드로 보여 한글 데이터를 쓰지 않은 카드 번호 (지금 데이터에는 없다 — 데이터가 바뀌었을 때를 위한 안전장치)</summary>
    public static IReadOnlyCollection<string> IdentityMismatches80 => _identityMismatches80;
    private static List<string> _identityMismatches80 = new();

    private static void Audit80(Dictionary<string, KoreanCard> map, string englishCache)
    {
        foreach (var (id, _) in MainFromEnglish80Ids)
            if (map.TryGetValue(id, out var k)) { k.MainText = null; k.MainFromEnglish80 = true; }
        foreach (var id in SourceFromEnglish80Ids)
            if (map.TryGetValue(id, out var k)) { k.SourceText = null; k.SourceFromEnglish80 = true; }
        foreach (var (id, fix) in EvoLevelFix80)
            if (map.TryGetValue(id, out var k) && k.MainText is { } t)
            {
                // 〔진화〕 조건(첫 "코스트 N"까지) 안의 Lv.만 고친다
                var m = System.Text.RegularExpressions.Regex.Match(t, @"〔진화〕[^〔【]*?코스트\s*\d+");
                if (m.Success && m.Value.Contains($"Lv.{fix.Wrong}", StringComparison.Ordinal))
                    k.MainText = t[..m.Index] + m.Value.Replace($"Lv.{fix.Wrong}", $"Lv.{fix.Right}", StringComparison.Ordinal) + t[(m.Index + m.Length)..];
            }
        // 메인 문구 뒤에 진화원 효과를 한 번 더 붙여 적은 카드 (EX3-004 "・진화원 효과 【자신의 턴】~") — 하단 문구가 있으면 그 앞까지만
        foreach (var k in map.Values)
            if (k.MainText is { } mt && !string.IsNullOrWhiteSpace(k.SourceText) && k.SourceText.Trim() != "-"
                && mt.IndexOf("・진화원 효과", StringComparison.Ordinal) is var cut and > 0)
                k.MainText = mt[..cut].TrimEnd();

        // 다른 카드 대조 (안전장치): 종류가 다르거나, Lv. · 등장 코스트 · DP 중 둘 이상이 다르면 다른 카드로 보고 한글 데이터를 쓰지 않는다
        // (하나만 다른 카드는 영어 API의 수치 오류 · 한국 사이트의 오타였다 — BT20-056 코스트 · BT23-036 DP · ST7-04 Lv.)
        var removed = new List<string>();
        if (File.Exists(englishCache))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(englishCache));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("CardId", out var idp) || idp.GetString() is not { } id || !map.TryGetValue(id, out var k)) continue;
                string enType = e.TryGetProperty("Type", out var tp) ? tp.ToString() : "";
                string? koType = enType switch { "Digimon" => "디지몬", "Tamer" => "테이머", "Option" => "옵션", "Digitama" or "DigiEgg" => "디지타마", _ => null };
                if (koType != null && k.Type.Length > 0 && k.Type != koType) { removed.Add(id); continue; }
                int diffs = 0;
                if (Num(k.Level) is { } kl && Int(e, "Level") is { } el && el > 0 && kl != el) diffs++;
                if (Num(k.PlayCost) is { } kc && Int(e, "PlayCost") is { } ec && kc != ec) diffs++;
                if (Num(k.DP) is { } kd && Int(e, "DP") is { } ed && ed > 0 && kd != ed) diffs++;
                if (diffs >= 2) removed.Add(id);
            }
        }
        foreach (var id in removed) map.Remove(id);
        _identityMismatches80 = removed;

        static int? Int(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
    }

    private static int? Num(string? s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"\d+(?:\.\d+)?");
        return m.Success ? (int)Math.Round(double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)) : null;
    }

    // ---- CardData 표시용 ----

    /// <summary>이름 표시: 공식 한글 → (공식 한글이 없는 카드) 같은 영어 이름의 공식 한글 이름 → 영어 이름</summary>
    public static string DisplayName(CardData card) =>
        Get(card.CardId) is { Name.Length: > 0 } ko ? ko.Name
        : StarterDecks.IsBuiltInCard(card.CardId) ? card.Name
        : Engine.Effects.KoreanNameLocalizer.KoreanNameFor(card.Name) ?? card.Name;

    /// <summary>
    /// 메인 효과 표시 — 테이머 · 옵션은 [시큐리티] 효과(하단 텍스트)까지 붙인다 (내장 카드와 같은 형식).
    /// 순서: ① 공식 한글(cards_ko.json) ② 내장 카드는 원래 문구(이미 한글판에서 옮긴 한글) ③ 생성한 한글(KoreanEffectRenderer) ④ 영어 원문
    /// </summary>
    public static string DisplayEffectText(CardData card)
    {
        var ko = Get(card.CardId);
        if (ko == null)
        {
            if (StarterDecks.IsBuiltInCard(card.CardId))
                return card.EffectText;
            if (card.Type is CardType.Tamer or CardType.Option)
            {
                string genMain = Generated(card, inherited: false);
                string genSec = Generated(card, inherited: true);
                return genSec.Length == 0 ? genMain : genMain.Length == 0 ? genSec : genMain + "\n" + genSec;
            }
            return Generated(card, inherited: false);
        }
        string main = ko.MainFromEnglish80 ? Generated(card, inherited: false) : Clean(ko.MainText);   // v0.80.0 한글 문구가 그 카드의 것이 아니면 생성 한글
        if (card.Type is CardType.Tamer or CardType.Option)
        {
            string sec = Clean(ko.SourceText);
            // 공식 한글 데이터에 [시큐리티] 효과가 빠진 카드(BT11 옵션 등): 영어 [시큐리티] 효과의 생성 한글로 채운다
            if (sec.Length == 0 && !main.Contains("시큐리티】") && !string.IsNullOrWhiteSpace(card.InheritedEffectText))
                sec = Generated(card, inherited: true);
            return sec.Length == 0 ? main : main.Length == 0 ? sec : main + "\n" + sec;
        }
        return main;
    }

    public static string DisplayInheritedEffectText(CardData card)
    {
        var ko = Get(card.CardId);
        if (ko == null)
        {
            if (StarterDecks.IsBuiltInCard(card.CardId))
                return card.InheritedEffectText;
            if (card.Type is CardType.Tamer or CardType.Option) return string.Empty;
            string gen = Generated(card, inherited: true);
            // 듀얼 카드 옵션 쪽의 〈아츠 진화〉 (공식 카드 리스트의 [DUAL Rule] — 영어 API 문장에는 없다). 설명은 종합 룰 4-20-1 문구
            return DualCardRules.HasArtsDigivolve(card) ? (gen.Length == 0 ? ArtsDigivolveText : gen + "\n" + ArtsDigivolveText) : gen;
        }
        if (card.Type is CardType.Tamer or CardType.Option) return string.Empty;
        if (ko.SourceFromEnglish80)   // v0.80.0 한글 진화원 효과가 빠진 카드: 영어 진화원 효과의 생성 한글 (영어 데이터가 메인 칸에 "Inherited Effect ~"로 둔 카드도)
            return InheritedGenerated80(card);
        return Clean(ko.SourceText);
    }

    private static string InheritedGenerated80(CardData card)
    {
        string gen = Generated(card, inherited: true);
        if (gen.Length > 0 || !card.EffectText.TrimStart().StartsWith("Inherited Effect", StringComparison.OrdinalIgnoreCase)) return gen;
        return GeneratedCache.GetOrAdd((card.CardId + "#inh80", true), _ =>
        {
            var c2 = new CardData { CardId = card.CardId, Name = card.Name, Type = card.Type, Level = card.Level, Color = card.Color, Traits = card.Traits, InheritedEffectText = card.EffectText.TrimStart()["Inherited Effect".Length..].Trim() };
            try { return Engine.Effects.KoreanEffectRenderer.Render(c2, true) ?? c2.InheritedEffectText; }
            catch { return c2.InheritedEffectText; }
        });
    }

    /// <summary>〈아츠 진화〉 표시 문구 (종합 룰 2-11 · 4-20-1)</summary>
    public const string ArtsDigivolveText = "〈아츠 진화〉 (사용한 뒤 트래시하는 대신, 자신의 카드를 이 카드로 코스트를 지불하지 않고 진화시킬 수 있다)";

    /// <summary>공식 한글이 없는 카드: 영어 효과 문장으로 만든 한글 (절 하나도 번역하지 못하면 영어 원문)</summary>
    private static string Generated(CardData card, bool inherited)
    {
        string english = inherited ? card.InheritedEffectText : card.EffectText;
        if (string.IsNullOrWhiteSpace(english))
            return string.Empty;
        return GeneratedCache.GetOrAdd((card.CardId, inherited), _ =>
        {
            try { return Engine.Effects.KoreanEffectRenderer.Render(card, inherited) ?? english; }
            catch { return english; }   // 번역 오류로 화면이 깨지지 않게 — 영어 원문
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string CardId, bool Inherited), string> GeneratedCache = new();

    /// <summary>특징 표시: 공식 한글 → (공식 한글이 없는 카드) 특징 이름표로 하나씩 (모르는 특징은 영어 그대로)</summary>
    public static IReadOnlyList<string> DisplayTraits(CardData card) =>
        Get(card.CardId) is { Traits.Count: > 0 } ko ? ko.Traits
        : StarterDecks.IsBuiltInCard(card.CardId) || Count == 0 ? card.Traits
        : card.Traits.Select(Engine.Effects.KoreanEffectRenderer.TraitKo).ToList();

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-")
            return string.Empty;
        // 사이트 문구는 효과가 한 줄로 이어져 있다 → 새 효과의 발동 시점(【…】) 앞에서 줄을 바꾼다
        // (문장이 끝난 뒤의 【…】만 — "이 카드의 【메인】 효과"처럼 문장 속 표기는 그대로)
        string t = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"(?<=[.。)）》」])\s*(?=【|〔)", "\n");
        return t.Trim();
    }
}
