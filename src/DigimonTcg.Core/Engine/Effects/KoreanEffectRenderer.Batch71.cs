using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.71.0 미지원 문장 45차 묶음 — 한글 표시 (원래 규칙이 옮기지 못한 절만)</summary>
public static partial class KoreanEffectRenderer
{
    private static string? RenderClause71(string rawClause)
    {
        string t = rawClause.Trim();
        // BT16-091 같은 뜻 다른 표기 — 바꿔 쓴 문장을 앞의 규칙으로
        string alt = SentenceParser.ClauseRewrite71(t);
        if (alt != t && RenderClause(alt) is { } ko0 && ko0.LastIndexOf("그 디지몬은 ", StringComparison.Ordinal) is var at0 and >= 0)
            return ko0[..at0] + "이 효과로 조그레스 진화한 디지몬은 " + ko0[(at0 + "그 디지몬은 ".Length)..];   // 공식 BT16-091과 같은 주어
        // BT18-015 · BT18-073 · EX11-059 필드의 디지몬 + 트래시의 카드로 조그레스 진화 — 공식 한글 카드와 같은 문형
        Match m;
        if ((m = Regex.Match(t, @"(?<pre>^|, |\. |\] )(?:You may DNA digivolve 1 of your (?<f>.+?) in play and 1 (?<p>.+?) in (?:the|your) trash into (?<into>.+?) in (?:the|your) hand|1 of your (?<f>.+?) and 1 (?<p>.+?) in (?:the|your) trash may DNA digivolve into (?<into>.+?) in (?:the|your) hand)\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string f = m.Groups["f"].Value.Trim();
            string? who = Bare71(f) is { } bf ? $"자신의 {bf} 1마리" : TargetKo("1 of your " + f + (Regex.IsMatch(f, @"\bDigimon$", RegexOptions.IgnoreCase) ? "" : " Digimon"));
            string? partner = Bare71(m.Groups["p"].Value.Trim()) ?? PickNoCount45(m.Groups["p"].Value.Trim());
            string? into = Bare71(m.Groups["into"].Value.Trim()) ?? PickNoCount45(Regex.Replace(m.Groups["into"].Value.Trim(), @"^an? ", "", RegexOptions.IgnoreCase));
            if (who == null || partner == null || into == null) return null;
            string action = $"{who}와 트래시의 {partner} 1장으로 패의 {into}{J(into, "으로", "로")} 조그레스 진화할 수 있다.";
            string head = t[..m.Index] + m.Groups["pre"].Value;
            if (head.Trim().Length == 0) return action;
            if (RenderClause(head + "gain 1 memory.") is { } ko && ko.EndsWith("메모리를 +1 한다.")) return ko[..^"메모리를 +1 한다.".Length] + action;
        }
        return null;
    }

    /// <summary>괄호만 있는 명칭 "[A]" · "[A] or [B]" → 「A」/「B」 (아니면 null)</summary>
    private static string? Bare71(string x) =>
        Regex.IsMatch(x, @"^\[[^\]]+\](?: or \[[^\]]+\])*$")
            ? string.Join("/", Regex.Matches(x, @"\[([^\]]+)\]").Select(n => $"「{NameKo(n.Groups[1].Value)}」"))
            : null;
}
