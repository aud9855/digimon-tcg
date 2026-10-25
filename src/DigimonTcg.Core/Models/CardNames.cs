using System;

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
}
