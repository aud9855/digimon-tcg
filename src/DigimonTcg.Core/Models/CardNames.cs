using System;
using System.Linq;

namespace DigimonTcg.Core.Models;

/// <summary>
/// 카드 이름 판정 (효과 문장의 이름 조건 · 같은 명칭 판정은 모두 여기를 거친다).
/// 내장 한글 카드는 Name이 한글("그레이몬")이라 영어 효과 문장의 "[Greymon]"과 맞지 않으므로,
/// 공식 영어 이름(CardData.EnglishName)도 함께 본다. API 카드는 Name이 영어라 Name만 본다.
/// </summary>
public static class CardNames
{
    /// <summary>판정 기준 이름: 영어 이름이 있으면 그것, 없으면 Name (같은 명칭 비교 · 이름 집합에 쓴다)</summary>
    public static string MatchName(CardData card) =>
        string.IsNullOrWhiteSpace(card.EnglishName) ? card.Name : card.EnglishName!;

    /// <summary>이름에 x를 포함하는가 ("with [X] in its name", "your [X]" 등) — Name · EnglishName 둘 다 본다</summary>
    public static bool Contains(CardData card, string x) =>
        x.Contains(ExceptMark70) ? ContainsExcept70(x, n => Contains(card, n), n => Is(card, n)) :
        card.Name.Contains(x, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(card.EnglishName) && card.EnglishName!.Contains(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>이름이 정확히 x인가 — Name · EnglishName 둘 다 본다</summary>
    public static bool Is(CardData card, string x) =>
        card.Name.Equals(x, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(card.EnglishName) && card.EnglishName!.Equals(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>두 카드가 같은 명칭인가 (내장 한글 "그레이몬"과 API "Greymon"도 같은 명칭)</summary>
    public static bool Same(CardData a, CardData b) =>
        string.Equals(MatchName(a), MatchName(b), StringComparison.OrdinalIgnoreCase)
        || string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    // ---- v0.67.0 필드의 디지몬: 효과로 원래 명칭이 바뀌었으면 그 명칭 (BT11-043 · BT14-097 · EX13-031 — 종합 룰 15-12-2). 바뀌지 않았으면 맨 위 카드 그대로 ----

    /// <summary>필드의 디지몬의 판정 기준 이름</summary>
    public static string MatchName(CardInstance d) => d.Changed67 is { } ch ? ch.Name : MatchName(d.TopCard);

    /// <summary>필드의 디지몬의 명칭에 x를 포함하는가</summary>
    public static bool Contains(CardInstance d, string x) => x.Contains(ExceptMark70) ? ContainsExcept70(x, n => Contains(d, n), n => Is(d, n)) : d.Changed67 is { } ch ? ch.Name.Contains(x, StringComparison.OrdinalIgnoreCase) : Contains(d.TopCard, x);

    /// <summary>
    /// v0.70.0 "[Greymon] in its name (other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon])" — 문장 읽기가 이름 조건 안에 제외할 명칭을 붙인다
    /// ("Greymon⊄DoruGreymon⊄BurningGreymon⊄DexDoruGreymon"). 전에는 괄호를 설명문으로 지워 제외가 빠졌다 (BT4-092 · BT5-015 등)
    /// </summary>
    public const char ExceptMark70 = (char)0x2284;

    private static bool ContainsExcept70(string x, Func<string, bool> contains, Func<string, bool> isName)
    {
        var parts = x.Split(ExceptMark70);
        return contains(parts[0]) && !parts.Skip(1).Any(isName);
    }

    /// <summary>필드의 디지몬의 명칭이 정확히 x인가</summary>
    public static bool Is(CardInstance d, string x) => d.Changed67 is { } ch ? ch.Name.Equals(x, StringComparison.OrdinalIgnoreCase) : Is(d.TopCard, x);

    /// <summary>필드의 두 디지몬이 같은 명칭인가</summary>
    public static bool Same(CardInstance a, CardInstance b) =>
        a.Changed67 == null && b.Changed67 == null ? Same(a.TopCard, b.TopCard) : string.Equals(MatchName(a), MatchName(b), StringComparison.OrdinalIgnoreCase);
}
