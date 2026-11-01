using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.72.0 미지원 문장 46차 묶음 — 한글 표시 (원래 규칙이 옮기지 못한 절만)</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I72 = RegexOptions.IgnoreCase;

    private static string? RenderClause72(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // BT22-092 — 공식 "그 디지몬의 【메인】 효과 1개를 발휘한다. 이 효과로 발휘했다면, 메모리 +1."
        if ((m = Regex.Match(t, @"^(?<head>.+?, )activate 1 of (?:those|that|these) Digimon's \[Main\] effects?\. If this activated any effects?, gain (?<n>\d+) memory\.?$", I72)).Success
            && Head72(m.Groups["head"].Value) is { } h1)
            return h1 + $"그 디지몬의 【메인】 효과 1개를 발휘한다. 이 효과로 발휘했다면, 메모리를 +{m.Groups["n"].Value} 한다.";
        // EX11-058 "~. If played by ＜Decode＞, ~" · EX11-060 "~ (~.) If deleted by ＜Overclock＞, ~" (마침표가 빠진 문장은 나눠 읽는다)
        string t2 = SentenceParser.ClauseRewrite72(t);
        if ((m = Regex.Match(t2, @"^(?<first>.+?\))\.?\s+If (?<c>played by ＜Decode＞|deleted by ＜Overclock＞), (?<rest>.+)$", I72)).Success
            && RenderClause(m.Groups["first"].Value + ".") is { } first && MainBody72(m.Groups["rest"].Value) is { } rest)
        {
            string cond = m.Groups["c"].Value.StartsWith("played", StringComparison.OrdinalIgnoreCase) ? "그 디지몬이 《디코드》로 등장했다면, " : "그 디지몬이 《오버클럭》으로 소멸했다면, ";
            return first + " " + cond + rest;
        }
        // EX11-064 "~. For each of your opponent's face-up security cards, reduce this effect's digivolution cost by 1."
        if ((m = Regex.Match(t, @"^(?<first>.+?\.)\s+For each of your (?<opp>opponent's )?face-up security cards, reduce this effect's (?:digivolution |paid )?cost by (?<n>\d+)\.?$", I72)).Success
            && RenderClause(m.Groups["first"].Value) is { } f3)
            return f3 + $" {(m.Groups["opp"].Success ? "상대의" : "자신의")} 앞면 시큐리티 1장마다 이 효과로 지불하는 코스트 -{m.Groups["n"].Value}.";
        // EX11-066 "~, reveal the top 2 cards of your deck. Place all [Vemmon] among them as any of those Digimon's bottom digivolution cards. Trash the rest."
        if ((m = Regex.Match(t, @"^(?<head>.+?, )reveal the top (?<n>\d+) cards? of your deck\. Place all (?<x>\[[^\]]+\]) among them as any of (?:those|these) Digimon's bottom digivolution cards\. Trash the rest\.?$", I72)).Success
            && Head72(m.Groups["head"].Value) is { } h4)
            return h4 + $"자신의 덱 위에서부터 {m.Groups["n"].Value}장 오픈한다. 그중 「{NameKo(m.Groups["x"].Value.Trim('[', ']'))}」 전부를 그 디지몬 중 1마리의 진화원 맨 아래에 놓는다. 남은 카드는 파기한다.";
        // EX11-006 "This Digimon linked with [Maquinamon] may digivolve into ~" → "「X」를 링크하고 있는 이 디지몬을 ~로 진화시킬 수 있다"
        if ((m = Regex.Match(t, @"^(?<head>(?:Inherited Effect\s*)?(?:\[[^\]]+\]\s*)+)This Digimon linked with (?<x>\[[^\]]+\]) (?<rest>may digivolve into .+)$", I72)).Success
            && RenderClause(m.Groups["head"].Value + "This Digimon " + m.Groups["rest"].Value) is { } k6 && k6.IndexOf("이 디지몬을 ", StringComparison.Ordinal) is var at6 and >= 0)
        {
            string x = NameKo(m.Groups["x"].Value.Trim('[', ']'));
            return k6[..at6] + $"「{x}」{J("「" + x + "」", "을", "를")} 링크하고 있는 " + k6[at6..];
        }
        // EX11-033 · EX11-042 (공식 영어) "You may play 1 [Maquinamon] from your hand or this Digimon's link cards without paying the cost."
        if ((m = Regex.Match(t, @"^(?<head>(?:\[[^\]]+\]\s*)+)You may play 1 (?<x>\[[^\]]+\]) from your hand or this Digimon's link cards without paying the cost\.?$", I72)).Success
            && Head72(m.Groups["head"].Value) is { } h5)
            return h5 + $"자신의 패 또는 이 디지몬의 링크 카드에서 「{NameKo(m.Groups["x"].Value.Trim('[', ']'))}」 1장을 코스트를 지불하지 않고 등장시킬 수 있다.";
        return null;
    }

    /// <summary>시점 · 조건 · 비용 머리말 ("~, by suspending this Tamer, ") → 한글 머리말 (메모리 +1 문장으로 옮겨 끝을 뗀다)</summary>
    private static string? Head72(string head)
    {
        const string tail = "메모리를 +1 한다.";
        return RenderClause(head + "gain 1 memory.") is { } ko && ko.EndsWith(tail, StringComparison.Ordinal) ? ko[..^tail.Length] : null;
    }

    /// <summary>시점 없는 문장 하나 → 한글 ("【메인】"으로 옮겨 머리말을 뗀다)</summary>
    private static string? MainBody72(string sentence)
    {
        const string lead = "【메인】 ";
        return RenderClause("[Main] " + sentence.Trim()) is { } ko && ko.StartsWith(lead, StringComparison.Ordinal) ? ko[lead.Length..] : null;
    }
}
