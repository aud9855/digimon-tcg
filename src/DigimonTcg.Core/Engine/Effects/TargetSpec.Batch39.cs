using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.39.0 원래 규칙이 읽지 못한 대상만: 이름을 제외하는 대상 ("without [X] in its digivolution cards" · ", other than [Sea Animal] trait,")</summary>
public sealed partial class TargetSpec
{
    /// <summary>"[Beast], [Animal] or [Sovereign], other than [Sea Animal] trait, in any of its traits" → "~ in any of its traits other than [Sea Animal]" (렌더러도 쓴다)</summary>
    internal static string NormalizeOtherThan39(string s) =>
        Regex.Replace(s, $@"(?<x>{NameList}),\s*other than (?<ex>\[[^\]]+\])(?: traits?)?,\s*in any of (?<p>its|their) traits", "${x} in any of ${p} traits other than ${ex}", RegexOptions.IgnoreCase);

    private static TargetSpec? Parse39(string phrase)
    {
        phrase = phrase.Trim().TrimEnd('.').Trim();
        if (!Regex.IsMatch(phrase, @"\bwithout \[|\bother than \[", RegexOptions.IgnoreCase)) return null;
        string norm = NormalizeOtherThan39(phrase);
        if (norm != phrase && ParseCore(norm) is { } n) return n;
        // "1 of your [Diaboromon] without [Doomsday Clock] in its digivolution cards" (BT17-100) · "1 of your [Mother D-Reaper] without [ADR-01 Jeri] in ~" (BT19-078) — 그 이름의 디지몬
        var m = Regex.Match(phrase, $@"^(?<pre>(?:\d+|all|up to \d+) of (?:your|your opponent's|their) (?:other )?)(?<x>{NameList})s?(?<rest> (?:without|with) .+)$", RegexOptions.IgnoreCase);
        if (m.Success && ParseCore(m.Groups["pre"].Value + "Digimon" + m.Groups["rest"].Value) is { IsThis: false, IsThat: false } named)
        {
            var xs = Names(m.Groups["x"].Value);
            named.Filters.Add(d => xs.Any(x => CardNames.Is(d.TopCard, x)));
            return named;
        }
        return null;
    }
}
