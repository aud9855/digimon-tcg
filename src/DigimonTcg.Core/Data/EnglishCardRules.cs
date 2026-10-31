using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

/// <summary>공식 영어 카드 리스트의 진화 조건 칸 하나 ("Red Blue 3 from Lv.4") — tools/ensite/collect_en.ps1 출력 형식</summary>
public sealed class EnglishEvoCost
{
    public List<string> Colors { get; set; } = new();   // red · blue · … · all(멀티컬러 아이콘 = 아무 색)
    public int Cost { get; set; }
    public int Level { get; set; }                      // "from Lv.N" 의 N (Lv.이 아닌 칸 — Tamer · 앱몬 등급 — 은 0)
    public string From { get; set; } = string.Empty;
}

/// <summary>공식 영어 카드 리스트의 카드 1장 (진화 조건만)</summary>
public sealed class EnglishCardRule
{
    public string CardId { get; set; } = string.Empty;
    public List<EnglishEvoCost> Evo { get; set; } = new();
    public List<string> SpecialDigivolve { get; set; } = new();   // [Special Digivolution Condition] 의 "[Digivolve] ~: Cost N" 줄
}

/// <summary>
/// v0.48.0 공식 영어 카드 리스트(world.digimoncard.com)의 진화 조건 (data/en/card_rules.json, 카드 번호 기준).
/// 영어 API 데이터에는 진화 조건이 하나(레벨 · 색 1개 · 코스트)뿐이라, 여러 색 진화 조건 · 두 번째 진화 조건과
/// 한글 데이터가 없는 카드의 [Special Digivolution Condition]을 여기서 읽는다.
/// 개인 로컬 사용 전용 파일이라 저장소에는 넣지 않는다 — 파일이 없으면 아무 카드도 찾지 못하고, 엔진은 API 데이터로 판정한다.
/// </summary>
public static class EnglishCardRules
{
    private static Lazy<Dictionary<string, EnglishCardRule>> _cards = new(() => Load(DefaultPath()));

    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(CardCatalogService.ResolveImagesDirectory()) ?? ".", "en", "card_rules.json");

    public static int Count => _cards.Value.Count;

    /// <summary>카드 번호의 공식 영어 진화 조건 (내장 카드 · 파일에 없는 카드는 null)</summary>
    public static EnglishCardRule? Get(string cardId) =>
        !StarterDecks.IsBuiltInCard(cardId) && _cards.Value.TryGetValue(cardId, out var card) ? card : null;

    /// <summary>다른 파일로 다시 읽는다 (테스트용 — null이면 기본 경로)</summary>
    public static void Reload(string? path = null)
    {
        var p = path ?? DefaultPath();
        _cards = new Lazy<Dictionary<string, EnglishCardRule>>(() => Load(p));
    }

    /// <summary>테스트용: 파일 대신 이 목록을 쓴다</summary>
    public static void Use(IEnumerable<EnglishCardRule> cards)
    {
        var map = cards.Where(c => !string.IsNullOrWhiteSpace(c.CardId)).GroupBy(c => c.CardId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _cards = new Lazy<Dictionary<string, EnglishCardRule>>(() => map);
    }

    private static Dictionary<string, EnglishCardRule> Load(string path)
    {
        var map = new Dictionary<string, EnglishCardRule>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return map;
        try
        {
            var list = JsonSerializer.Deserialize<List<EnglishCardRule>>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var c in list ?? new())
                if (!string.IsNullOrWhiteSpace(c.CardId) && !map.ContainsKey(c.CardId.Trim()))
                    map[c.CardId.Trim()] = c;
        }
        catch
        {
            // 파일이 깨졌으면 API 데이터만으로 진행
        }
        return map;
    }

    public static CardColor? ColorOf(string en) => en.Trim().ToLowerInvariant() switch
    {
        "red" => CardColor.Red, "blue" => CardColor.Blue, "yellow" => CardColor.Yellow, "green" => CardColor.Green,
        "black" => CardColor.Black, "purple" => CardColor.Purple, "white" => CardColor.White, _ => null,
    };
}
