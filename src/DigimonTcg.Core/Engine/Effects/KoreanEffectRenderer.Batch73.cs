using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.73.0 미지원 문장 47차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I73 = RegexOptions.IgnoreCase;

    /// <summary>"[A], [B] or [C] in any of its traits" — 특징 포함 (종합 룰 2-3-2-4) 목록</summary>
    private static readonly Regex ContainsList73 = new(@"(?<x>\[[^\]]+\](?:(?:,\s*(?:or\s+|and\s+)?|\s+or\s+|\s+and\s+)\[[^\]]+\])*)(?:,? other than (?:the )?\[[^\]]+\](?: traits?)?,?)?\s+in (?:any|one) of (?:its|their) traits", I73 | RegexOptions.Compiled);

    /// <summary>"the [A] or [B] trait(s)" — 특징 일치 (종합 룰 2-3-2-3) 목록</summary>
    private static readonly Regex ExactList73 = new(@"(?<x>\[[^\]]+\](?:(?:,\s*(?:or\s+|and\s+)?|\s+or\s+|\s+and\s+)\[[^\]]+\])*)\s+traits?\b", I73 | RegexOptions.Compiled);

    /// <summary>한글 "특징으로 「A」/「B」를 (「C」는 제외) 가진 · 가지거나"</summary>
    private static readonly Regex KoHave73 = new(@"특징으로 (?<l>「[^」]+」(?:/「[^」]+」)*)(?<j>[을를])(?<ex> \([^)]*\))? (?<v>가진|가지거나)", RegexOptions.Compiled);

    /// <summary>
    /// v0.73.0 영어 "in any of its traits"(특징 포함, 종합 룰 2-3-2-4)를 "가진"으로 옮기던 한글 → 공식 표기 "특징으로 「X」를 포함하는" · "포함하거나"
    /// (공식 한글 카드 문형 — BT13-050 · P-208 · ST18-05 등).
    /// 같은 한글 목록이 일치 · 포함 둘 다에 맞으면 바꾸지 않는다
    /// </summary>
    private static string? TraitContains73(string english, string? ko)
    {
        if (ko == null || !ko.Contains("특징으로 ", StringComparison.Ordinal)) return ko;
        var contains = ContainsList73.Matches(english).Select(m => Names73(m.Groups["x"].Value)).ToList();
        if (contains.Count == 0) return ko;
        var exact = ExactList73.Matches(english).Select(m => Names73(m.Groups["x"].Value)).ToList();
        return KoHave73.Replace(ko, m =>
        {
            var koNames = Regex.Matches(m.Groups["l"].Value, "「([^」]+)」").Select(x => x.Groups[1].Value).ToHashSet();
            bool c = contains.Any(set => Covers73(set, koNames)), e = exact.Any(set => Covers73(set, koNames));
            if (!c || e) return m.Value;
            return $"특징으로 {m.Groups["l"].Value}{m.Groups["j"].Value}{m.Groups["ex"].Value} {(m.Groups["v"].Value == "가진" ? "포함하는" : "포함하거나")}";
        });
    }

    /// <summary>v0.73.0 원래 규칙이 옮기지 못한 절만 (EX11-027 · EX11-053)</summary>
    private static string? RenderClause73(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // EX11-027 "~. Then, you may link this Digimon or 1 [Maquinamon] in your hand to 1 of your other Digimon without paying the cost."
        if ((m = Regex.Match(t, @"^(?<first>.+?\.)\s+Then, you may link this Digimon or 1 (?<x>\[[^\]]+\]) in your hand to (?:1|one) of your other Digimon without paying (?:the|its) costs?\.?$", I73)).Success
            && RenderClause(m.Groups["first"].Value) is { } f1)
            return f1 + $" 그 후, 이 디지몬 또는 자신의 패의 「{NameKo(m.Groups["x"].Value.Trim('[', ']'))}」 1장을 자신의 다른 디지몬 1마리에 코스트를 지불하지 않고 링크시킬 수 있다.";
        // EX11-053 "~ you may play 1 [X] from your hand or under your [Y]s on the field without paying the cost. Then, place this card as the played Digimon's bottom digivolution card."
        if ((m = Regex.Match(t, @"^(?<head>.+?)from your hand or under your (?<y>\[[^\]]+\])s? on the field (?<tail>without paying (?:the|its) costs?\.)\s+Then, place this card as the played Digimon's bottom digivolution card\.?$", I73)).Success
            && RenderClause(m.Groups["head"].Value + "from your hand " + m.Groups["tail"].Value) is { } f2 && f2.Contains("자신의 패에서 ", StringComparison.Ordinal))
        {
            string y = NameKo(m.Groups["y"].Value.Trim('[', ']'));
            return f2.Replace("자신의 패에서 ", $"자신의 패 또는 필드의 자신의 「{y}」의 진화원에서 ") + " 그 후, 이 카드를 등장한 디지몬의 진화원 맨 아래에 놓는다.";
        }
        // EX5-070 (옵션) "~ may digivolve into ~. If it did, place this card as its bottom digivolution card." — 공식 한글 EX5-070 문형
        if ((m = Regex.Match(t, @"^(?<first>.+?\.)\s+If it did, place this card as its bottom digivolution card\.?$", I73)).Success
            && RenderClause(m.Groups["first"].Value) is { } f3)
            return f3 + " 그랬다면, 이 카드를 그 디지몬의 진화원 아래에 놓는다.";
        // EX5-070 "1 of your Digimon without [X Antibody] in its digivolution cards may digivolve ~" — 공식 한글 EX5-070 문형 (진화원에 「X」가 없는)
        if ((m = Regex.Match(t, @"\b1 of your Digimon without (?<x>\[[^\]]+\]) in its digivolution cards may digivolve\b", I73)).Success
            && RenderClause(t[..m.Index] + $"1 of your Digimon with {m.Groups["x"].Value} in its digivolution cards may digivolve" + t[(m.Index + m.Length)..]) is { } f5)
        {
            string x = NameKo(m.Groups["x"].Value.Trim('[', ']'));
            string with = $"진화원에 「{x}」{J("「" + x + "」", "을", "를")} 가진 자신의 디지몬";
            if (f5.Contains(with, StringComparison.Ordinal)) return f5.Replace(with, $"진화원에 「{x}」{J("「" + x + "」", "이", "가")} 없는 자신의 디지몬");
        }
        // EX5-070 (진화원 효과) "~, from this Digimon's digivolution cards, return 1 Digimon card to the hand and place 1 [X Antibody] on top of your security stack."
        //  — 공식 한글 EX5-070 문형
        if ((m = Regex.Match(t, @"^(?<head>.+?, )from this Digimon's digivolution cards, return 1 Digimon card to (?:the|your) hand and place 1 (?<x>\[[^\]]+\]) on top of your security stack\.?$", I73)).Success
            && Head72(m.Groups["head"].Value) is { } h6)
            return h6 + $"이 디지몬의 진화원에서 디지몬 카드 1장을 패로 되돌리고, 「{NameKo(m.Groups["x"].Value.Trim('[', ']'))}」 1장을 시큐리티 위에 놓는다.";
        // BT7-110 "~ with matching colors and [Ten Warriors] trait ~" — 공식 한글 BT7-110 문형 (같은 색)
        if (Regex.IsMatch(t, @"\bwith matching colou?rs and \[[^\]]+\] trait\b", I73) && SentenceParser.ClauseRewrite73(t) is { } alt7 && alt7 != t && RenderClause(alt7) is { } f7
            && Regex.Match(f7, "패의 (?<tr>특징으로 「[^」]+」[을를] 가진) 디지몬 카드") is { Success: true } km)
            return f7[..km.Index] + km.Groups["tr"].Value + " 같은 색인 패의 디지몬 카드" + f7[(km.Index + km.Length)..];
        // BT20-003 (진화원 효과) — 공식 한글 문형
        if ((m = Regex.Match(t, @"^(?<head>(?:Inherited Effect\s*)?(?:\[[^\]]+\]\s*)+)You may place 1 of your Tamers with (?<w>.+?) as the bottom digivolution card of this Digimon with no Tamer cards in its digivolution cards\.?$", I73)).Success
            && Head72(m.Groups["head"].Value) is { } h4 && PickKo("1 Tamer card with " + m.Groups["w"].Value) is { } pk && pk.EndsWith(" 1장", StringComparison.Ordinal))
            return h4 + pk[..^" 1장".Length].Replace("테이머 카드", "자신의 테이머") + " 1명을 진화원에 테이머 카드가 없는 이 디지몬의 진화원 아래에 놓을 수 있다.";
        // 같은 뜻 다른 표기로 읽은 절 (BT8-066 · BT7-110 · EX5-070 진화원 효과)
        if (SentenceParser.ClauseRewrite73(t) is { } alt && alt != t)
            return RenderClause(alt);
        return null;
    }

    /// <summary>영어 목록 항목마다 한글 후보 (특징 · 이름 표기)</summary>
    private static List<HashSet<string>> Names73(string list) =>
        Regex.Matches(list, @"\[([^\]]+)\]").Select(x => new HashSet<string> { TraitKo(x.Groups[1].Value), NameKo(x.Groups[1].Value), x.Groups[1].Value.Trim() }).ToList();

    /// <summary>한글 목록이 영어 목록과 같은가 (한글 이름마다 영어 항목 하나 · 영어 항목마다 한글 이름 하나)</summary>
    private static bool Covers73(List<HashSet<string>> en, HashSet<string> ko) =>
        ko.All(k => en.Any(s => s.Contains(k))) && en.All(s => s.Overlaps(ko));
}
