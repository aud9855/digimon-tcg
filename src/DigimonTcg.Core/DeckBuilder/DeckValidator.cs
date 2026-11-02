using System.Collections.Generic;
using System.Linq;
using System.Text;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.DeckBuilder;

public record RatioItemStatus(
    string Label,
    int CurrentCount,
    int RecommendedMin,
    int RecommendedMax,
    string StatusText // "적정", "부족", "초과"
);

public record DeckRatioSummary(
    int DigitamaCount,
    int Lv3Count,
    int Lv4Count,
    int Lv5Count,
    int Lv6PlusCount,
    int TamerOptionCount,
    IReadOnlyList<RatioItemStatus> Items
);

public static class DeckValidator
{
    public const int RequiredMainDeckSize = 50;
    public const int MaxDigitamaDeckSize = 5;
    public const int MaxCopiesPerCard = 4;

    public static (bool IsValid, List<string> Errors) ValidateDeck(
        IReadOnlyList<CardData> mainDeck,
        IReadOnlyList<CardData> digitamaDeck)
    {
        var errors = new List<string>();

        if (mainDeck.Count != RequiredMainDeckSize)
            errors.Add($"메인 덱은 정확히 {RequiredMainDeckSize}장이어야 합니다. (현재 {mainDeck.Count}장)");

        if (digitamaDeck.Count > MaxDigitamaDeckSize)
            errors.Add($"디지타마 덱은 최대 {MaxDigitamaDeckSize}장까지 구성할 수 있습니다. (현재 {digitamaDeck.Count}장)");

        if (digitamaDeck.Any(c => c.Type != CardType.Digitama || c.Level != 2))
            errors.Add("디지타마 덱에는 Lv.2 디지타마 카드만 넣을 수 있습니다.");

        if (mainDeck.Any(c => c.Type == CardType.Digitama))
            errors.Add("메인 덱에는 디지타마 카드를 넣을 수 없습니다.");

        var allCards = mainDeck.Concat(digitamaDeck);
        foreach (var group in allCards.GroupBy(c => CopyGroupKey(c.CardId)))
        {
            int max = MaxCopiesFor(group.Key);
            if (group.Count() > max)
                errors.Add($"동일 카드 번호({string.Join(" · ", group.Select(c => c.CardId).Distinct())})는 최대 {max}장까지만 투입할 수 있습니다. (현재 {group.Count()}장)");
        }

        return (errors.Count == 0, errors);
    }

    // ---- v0.83.0 카드에 적힌 덱 구성 규칙 (영어 API 문구에는 없다 — 공식 영어 카드 리스트 · 공식 한글 카드) ----

    /// <summary>"You can include up to 50 copies of cards with this card's card number in your deck." (공식 영어 카드 리스트 — 공식 한글 · 일본어도 같은 규칙)</summary>
    private static readonly HashSet<string> FiftyCopies83 = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "BT6-085", "EX2-046", "BT11-061", "EX9-048", "BT22-079", "EX11-027",
    };

    /// <summary>"※Card Number: Also treated as [P-009]. A deck may not have more than 4 total copies of this and [P-009]." (공식 영어 카드 리스트 RB1)</summary>
    private static readonly Dictionary<string, string> AlsoCardNumber83 = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["RB1-004"] = "P-009", ["RB1-006"] = "P-058", ["RB1-007"] = "P-010",
    };

    /// <summary>같은 카드 번호로 세는 묶음의 대표 번호 (RB1-004 → P-009)</summary>
    public static string CopyGroupKey(string cardId) =>
        AlsoCardNumber83.TryGetValue(cardId, out var other) ? other : cardId;

    /// <summary>이 카드 번호를 덱에 넣을 수 있는 최대 장수 (보통 4 · 카드에 적힌 50장 규칙)</summary>
    public static int MaxCopiesFor(string cardId) =>
        FiftyCopies83.Contains(CopyGroupKey(cardId)) ? 50 : MaxCopiesPerCard;

    /// <summary>덱에 이미 들어 있는 같은 카드 번호(묶음 포함)의 장수</summary>
    public static int CopiesInDeck(IEnumerable<CardData> cards, string cardId)
    {
        string key = CopyGroupKey(cardId);
        return cards.Count(c => string.Equals(CopyGroupKey(c.CardId), key, System.StringComparison.OrdinalIgnoreCase));
    }

    public static DeckRatioSummary CalculateRatios(
        IReadOnlyList<CardData> mainDeck,
        IReadOnlyList<CardData> digitamaDeck)
    {
        int digitama = digitamaDeck.Count;
        int lv3 = mainDeck.Count(c => c.Type == CardType.Digimon && c.Level == 3);
        int lv4 = mainDeck.Count(c => c.Type == CardType.Digimon && c.Level == 4);
        int lv5 = mainDeck.Count(c => c.Type == CardType.Digimon && c.Level == 5);
        int lv6Plus = mainDeck.Count(c => c.Type == CardType.Digimon && c.Level >= 6);
        int tamerOpt = mainDeck.Count(c => c.Type is CardType.Tamer or CardType.Option);

        var items = new List<RatioItemStatus>
        {
            BuildItem("Lv.2 디지타마", digitama, 4, 5),
            BuildItem("Lv.3 성장기", lv3, 12, 14),
            BuildItem("Lv.4 성숙기", lv4, 10, 12),
            BuildItem("Lv.5 완전체", lv5, 8, 10),
            BuildItem("Lv.6~7 궁극체", lv6Plus, 4, 6),
            BuildItem("테이머/옵션", tamerOpt, 6, 8)
        };

        return new DeckRatioSummary(digitama, lv3, lv4, lv5, lv6Plus, tamerOpt, items);
    }

    private static RatioItemStatus BuildItem(string label, int count, int min, int max)
    {
        string status = count < min ? "부족" : count > max ? "초과" : "적정";
        return new RatioItemStatus(label, count, min, max, status);
    }

    public static string FormatGoldenRatioHudText(DeckRatioSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[ 덱 구성 현황 - 황금 비율 HUD ]");
        foreach (var item in summary.Items)
        {
            int bars = System.Math.Clamp(item.CurrentCount, 0, 16);
            string barStr = new string('█', bars).PadRight(14, '░');
            sb.AppendLine($"{item.Label,-12} : [{barStr}] {item.CurrentCount,2} / {item.RecommendedMin}~{item.RecommendedMax} ({item.StatusText})");
        }
        return sb.ToString();
    }
}
