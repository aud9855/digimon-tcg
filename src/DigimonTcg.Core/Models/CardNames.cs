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
        x.IndexOf(TraitExactMark72) >= 0 ? Contains(card, x.Replace(TraitExactMark72.ToString(), "")) :
        x.Contains(ExceptMark70) ? ContainsExcept70(x, n => Contains(card, n), n => Is(card, n)) :
        card.Name.Contains(x, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(card.EnglishName) && card.EnglishName!.Contains(x, StringComparison.OrdinalIgnoreCase))
        || AlsoNames71(card).Any(a => a.Contains(x, StringComparison.OrdinalIgnoreCase));   // v0.71.0 〈룰〉 명칭도

    /// <summary>이름이 정확히 x인가 — Name · EnglishName 둘 다 본다 (v0.71.0 〈룰〉 명칭도)</summary>
    public static bool Is(CardData card, string x) =>
        x.IndexOf(TraitExactMark72) >= 0 ? Is(card, x.Replace(TraitExactMark72.ToString(), "")) :
        card.Name.Equals(x, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(card.EnglishName) && card.EnglishName!.Equals(x, StringComparison.OrdinalIgnoreCase))
        || AlsoNames71(card).Any(a => a.Equals(x, StringComparison.OrdinalIgnoreCase));

    // ---- v0.71.0 〈룰〉 명칭: 「A」/「B」로도 취급한다 (종합 룰 2-3-1-2 · 공식 Q&A — 이 룰을 가진 카드는 그 명칭으로도 참조된다).
    //      영어 카드 데이터에는 이 룰 문장이 없어, 공식 카드의 룰을 영어 명칭으로 옮겨 둔다 (카드 번호 → 영어 명칭).
    //      목록에 없는 "A & B" 이름의 테이머는 A · B로도 본다 (공식 BT18-088 · EX4-062 · EX10-064 · BT20-087 · BT20-089와 같은 룰) ----

    private static readonly System.Collections.Generic.Dictionary<string, string[]> AlsoNamed71 = new(StringComparer.Ordinal)
    {
        ["BT17-091"] = new[] { "Eiji Nagasumi" }, ["BT16-086"] = new[] { "Leon Alexander" },
        ["P-152"] = new[] { "Shoutmon", "Dorulumon" }, ["BT11-009"] = new[] { "Shoutmon", "Starmons" },
        ["BT11-018"] = new[] { "OmniShoutmon", "ZeigGreymon" }, ["BT11-030"] = new[] { "MetalGreymon", "Cyberdramon" },
        ["BT11-071"] = new[] { "DarkKnightmon", "Tuwarmon" }, ["BT15-012"] = new[] { "Shoutmon", "Ballistamon" },
        ["BT21-016"] = new[] { "Shoutmon" },
        ["EX3-064"] = new[] { "ChaosGallantmon" }, ["LM-026"] = new[] { "ChaosGallantmon" }, ["BT21-079"] = new[] { "ChaosGallantmon" },
        ["EX4-026"] = new[] { "Kyubimon" }, ["EX4-028"] = new[] { "Taomon" }, ["EX4-030"] = new[] { "Sakuyamon" },
        ["EX4-033"] = new[] { "Terriermon" }, ["EX4-048"] = new[] { "Greymon" }, ["P-183"] = new[] { "Greymon" },
        ["EX4-062"] = new[] { "Kiriha Aonuma", "Nene Amano" },
        ["BT14-052"] = new[] { "Leomon" }, ["EX5-030"] = new[] { "Leomon" }, ["BT14-060"] = new[] { "Commandramon" },
        ["BT14-097"] = new[] { "Sukamon" }, ["EX5-046"] = new[] { "Etemon", "Sukamon" },
        ["EX5-070"] = new[] { "X Antibody" }, ["BT20-083"] = new[] { "X Antibody" },
        ["BT15-035"] = new[] { "Numemon" }, ["P-141"] = new[] { "Mamemon", "Tyrannomon" },
        ["BT18-088"] = new[] { "Takuya Kanbara", "Koji Minamoto" }, ["BT20-087"] = new[] { "Kota Domoto", "Yuji Musya" },
        ["BT20-089"] = new[] { "Eiji Nagasumi", "Leon Alexander" }, ["EX10-064"] = new[] { "Yuu Amano", "Nene Amano" },
        ["EX10-068"] = new[] { "Ken Ichijoji" },
        // v0.82.0 공식 영어 "(Rule) Name: ~" · 공식 일본어 〈ルール〉名称 (두 곳이 같다) — 빠져 있던 카드
        ["EX4-072"] = new[] { "Plug-In" }, ["BT22-014"] = new[] { "Greymon" }, ["BT24-080"] = new[] { "ChaosGallantmon" },
        ["BT24-086"] = new[] { "Shuu Yulin" }, ["EX11-053"] = new[] { "X Antibody" }, ["EX11-066"] = new[] { "Zenith" },
        ["AD1-020"] = new[] { "Tommy Himi", "Takuya Kanbara", "Zoe Orimoto" }, ["AD1-021"] = new[] { "Marcus Damon" },
        ["AD1-023"] = new[] { "J.P. Shibayama", "Koji Minamoto", "Koichi Kimura" }, ["EX12-041"] = new[] { "Mamemon" }, ["EX13-053"] = new[] { "Mamemon" },
        ["EX13-029"] = new[] { "Wizardmon" }, ["EX13-066"] = new[] { "Sistermon Ciel (Awakened)" }, ["P-147"] = new[] { "Pulsemon" }, ["P-223"] = new[] { "Sakuyamon" },
        // v0.83.0 영어 API에 빠진 명칭 규칙 줄 (공식 영어 "The name of this card/Digimon is also treated as [X]" — 공식 일본어 · 공식 한글도 같은 규칙)
        ["BT8-061"] = new[] { "Mamemon" }, ["BT8-062"] = new[] { "SkullKnightmon", "DeadlyAxemon" }, ["BT10-061"] = new[] { "SkullKnightmon", "DeadlyAxemon" },
        ["EX2-012"] = new[] { "ChaosGallantmon" }, ["BT10-111"] = new[] { "Shoutmon" }, ["BT11-063"] = new[] { "Numemon" },
        ["P-072"] = new[] { "MetalGreymon" }, ["P-073"] = new[] { "WereGarurumon" },
        // "~ is also treated as having [X] in its name" (명칭에 X를 포함하는 것으로도 취급 — EX4-048과 같다)
        ["BT9-068"] = new[] { "Greymon" }, ["BT9-051"] = new[] { "Leomon" }, ["BT11-054"] = new[] { "Leomon" },
        // 「시스터몬 시엘」 · 「시스터몬 느와르」 두 명칭을 함께 가진 카드 (공식 영어 이름 Sistermon Ciel + 규칙 "Also ~ [Sistermon Noir]" (BT23-077 · ST12-13) ·
        //  공식 일본어 · 공식 한글 이름 Sistermon Noir + 규칙 "~ Sistermon Ciel로도 취급" (BT23-077 · ST12-13 · 공식 한글 BT6-084)) — 영어 API 이름(Ciel)에 Noir를 더한다
        ["BT6-084"] = new[] { "Sistermon Noir" }, ["ST12-13"] = new[] { "Sistermon Noir" }, ["BT23-077"] = new[] { "Sistermon Noir" },
    };

    /// <summary>v0.71.0 이 카드가 〈룰〉로 함께 가지는 명칭 (없으면 빈 목록)</summary>
    public static System.Collections.Generic.IReadOnlyList<string> AlsoNames71(CardData card)
    {
        if (AlsoNamed71.TryGetValue(card.CardId, out var xs)) return xs;
        if (card.Type == CardType.Tamer && card.Name.Contains(" & ", StringComparison.Ordinal))
            return card.Name.Split(new[] { " & ", ", " }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim().TrimStart('&').Trim()).Where(n => n.Length > 0).ToArray();
        return Array.Empty<string>();
    }

    /// <summary>두 카드가 같은 명칭인가 (내장 한글 "그레이몬"과 API "Greymon"도 같은 명칭)</summary>
    public static bool Same(CardData a, CardData b) =>
        string.Equals(MatchName(a), MatchName(b), StringComparison.OrdinalIgnoreCase)
        || string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    // ---- v0.67.0 필드의 디지몬: 효과로 원래 명칭이 바뀌었으면 그 명칭 (BT11-043 · BT14-097 · EX13-031 — 종합 룰 15-12-2). 바뀌지 않았으면 맨 위 카드 그대로 ----

    /// <summary>필드의 디지몬의 판정 기준 이름</summary>
    public static string MatchName(CardInstance d) => d.Changed67 is { } ch ? ch.Name : MatchName(d.TopCard);

    /// <summary>필드의 디지몬의 명칭에 x를 포함하는가</summary>
    public static bool Contains(CardInstance d, string x) => x.IndexOf(TraitExactMark72) >= 0 ? Contains(d, x.Replace(TraitExactMark72.ToString(), "")) : x.Contains(ExceptMark70) ? ContainsExcept70(x, n => Contains(d, n), n => Is(d, n)) : (d.Changed67 is { } ch ? ch.Name.Contains(x, StringComparison.OrdinalIgnoreCase) : Contains(d.TopCard, x))
        || SourceNames78(d).Any(n => Contains(n, x));   // v0.78.0 BT17-102 진화원의 Lv.3 이하 카드의 명칭도

    /// <summary>
    /// v0.70.0 "[Greymon] in its name (other than [DoruGreymon], [BurningGreymon] or [DexDoruGreymon])" — 문장 읽기가 이름 조건 안에 제외할 명칭을 붙인다
    /// ("Greymon⊄DoruGreymon⊄BurningGreymon⊄DexDoruGreymon"). 전에는 괄호를 설명문으로 지워 제외가 빠졌다 (BT4-092 · BT5-015 등)
    /// </summary>
    public const char ExceptMark70 = (char)0x2284;

    /// <summary>
    /// v0.72.0 "[X] trait" — 문장 읽기가 특징 이름 끝에 붙이는 "특징 일치" 표식 ("Machine≡"). 특징 판정(TargetSpec.TraitMatch71)은 일치로 보고,
    /// 이름 판정은 표식을 떼고 본다 (종합 룰 2-3-2-3 · SentenceParser.Batch72)
    /// </summary>
    public const char TraitExactMark72 = (char)0x2261;

    private static bool ContainsExcept70(string x, Func<string, bool> contains, Func<string, bool> isName)
    {
        var parts = x.Split(ExceptMark70);
        return contains(parts[0]) && !parts.Skip(1).Any(isName);
    }

    /// <summary>필드의 디지몬의 명칭이 정확히 x인가</summary>
    public static bool Is(CardInstance d, string x) => x.IndexOf(TraitExactMark72) >= 0 ? Is(d, x.Replace(TraitExactMark72.ToString(), "")) : (d.Changed67 is { } ch ? ch.Name.Equals(x, StringComparison.OrdinalIgnoreCase) : Is(d.TopCard, x))
        || SourceNames78(d).Any(n => Is(n, x));   // v0.78.0 BT17-102

    /// <summary>
    /// v0.78.0 BT17-102 "[All Turns] This Digimon has all the names of level 3 and lower cards in its digivolution cards" — 맨 위 카드가 이 효과를 가지면 그 Lv. 이하의 앞면 진화원(디지몬 · 디지타마 카드 —
    /// 뒷면은 카드 정보가 없다 4-7-9). 육성 에어리어에서도 (공식 Q&amp;A BT17-102 Q1). 명칭을 얻는 것이라 효과로 바뀐 원래 명칭(15-12-2)과 함께 가진다
    /// </summary>
    public static System.Collections.Generic.IEnumerable<CardData> SourceNames78(CardInstance d)
    {
        if (d.EvolutionSources.Count == 0 || d.TopIsFaceDown || d.TopCard.IsTamerProxy) return Array.Empty<CardData>();
        int lv = DigimonTcg.Core.Engine.Effects.CardScripts.For(d.TopCard)?.SourceNamesMaxLevel78 ?? 0;
        if (lv <= 0) return Array.Empty<CardData>();
        return d.FaceUpSources.Where(s => s.Type is CardType.Digimon or CardType.Digitama && s.Level > 0 && s.Level <= lv);
    }

    /// <summary>필드의 두 디지몬이 같은 명칭인가</summary>
    public static bool Same(CardInstance a, CardInstance b) =>
        (a.Changed67 == null && b.Changed67 == null ? Same(a.TopCard, b.TopCard) : string.Equals(MatchName(a), MatchName(b), StringComparison.OrdinalIgnoreCase))
        || (SourceNames78(a).Any() || SourceNames78(b).Any()) && SourceNames78(a).Select(MatchName).Append(MatchName(a)).Any(n => Is(b, n));   // v0.78.0 BT17-102 얻은 명칭도
}
