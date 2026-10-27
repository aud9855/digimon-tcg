using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.40.0 원래 규칙이 읽지 못한 대상만</summary>
public sealed partial class TargetSpec
{
    private static TargetSpec? Parse40(string phrase)
    {
        phrase = phrase.Trim().TrimEnd('.').Trim();
        Match m;
        // "all of your [Amon of Crimson Flame]s and [Umon of Blue Thunder]s" (BT14-018) — 그 이름의 디지몬 전부
        if ((m = Regex.Match(phrase, @"^all of your (?<a>\[[^\]]+\])s? and (?<b>\[[^\]]+\])s?$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = Names(m.Groups["a"].Value + " or " + m.Groups["b"].Value);
            var spec = new TargetSpec { Count = 0, Text = phrase };
            spec.Filters.Add(d => xs.Any(x => CardNames.Is(d.TopCard, x)));
            return spec;
        }
        // "all of your Digimon with the [Royal Knight] trait and all your green Digimon" (BT13-056) · "All of your Digimon with the [Xros Heart] trait or with ＜Retaliation＞" — 어느 쪽 조건이든
        if ((m = Regex.Match(phrase, @"^(?<a>all of your (?:[^,]*? )?Digimon(?: with .+?)?) and all (?:of )?your (?<b>(?:[^,]*? )?Digimon(?: with .+?)?)$", RegexOptions.IgnoreCase)).Success
            || (m = Regex.Match(phrase, @"^(?<a>all of your (?:[^,]*? )?Digimon with .+?) or (?<b>with .+)$", RegexOptions.IgnoreCase)).Success)
        {
            string b = m.Groups["b"].Value.StartsWith("with ", StringComparison.OrdinalIgnoreCase)
                ? Regex.Replace(m.Groups["a"].Value, @" with .+$", " ", RegexOptions.IgnoreCase) + m.Groups["b"].Value
                : "all of your " + m.Groups["b"].Value;
            if (ParseCore(m.Groups["a"].Value) is { IsThis: false, IsThat: false, Opponent: false, Count: 0, ContextFilters.Count: 0 } sa
                && ParseCore(b) is { IsThis: false, IsThat: false, Opponent: false, Count: 0, ContextFilters.Count: 0 } sb)
            {
                var fa = sa.Filters.ToList(); var fb = sb.Filters.ToList();
                var union = new TargetSpec { Count = 0, Text = phrase };
                union.Filters.Add(d => fa.All(f => f(d)) || fb.All(f => f(d)));
                return union;
            }
        }
        // "1 of your Digimon with ＜Save＞ in its text" (BT12-098) — 키워드가 적힌 디지몬 (맨 위 카드의 효과 문장)
        if ((m = Regex.Match(phrase, @"^(?<pre>.+?Digimon) with ＜(?<k>[^＞]+)＞ in (?:its|their) texts?$", RegexOptions.IgnoreCase)).Success
            && ParseCore(m.Groups["pre"].Value) is { IsThis: false, IsThat: false } withKw)
        {
            string k = "＜" + m.Groups["k"].Value;
            withKw.Filters.Add(d => (d.TopCard.EffectText + "\n" + d.TopCard.InheritedEffectText).Contains(k, StringComparison.OrdinalIgnoreCase));
            return withKw;
        }
        // "All your Digimon with ＜Fortitude＞ and no digivolution cards" (EX5-042) — 두 조건 모두
        if ((m = Regex.Match(phrase, @"^(?<pre>(?:all of |all )?your (?:[^,]*? )?Digimon) with (?<a>＜[^＞]+＞) and (?<b>no digivolution cards|\d+ or (?:more|fewer) digivolution cards)$", RegexOptions.IgnoreCase)).Success)
        {
            string pre = Regex.Replace(m.Groups["pre"].Value, @"^all your\b", "all of your", RegexOptions.IgnoreCase);
            if (ParseCore($"{pre} with {m.Groups["a"].Value}") is { IsThis: false, IsThat: false } sa && ParseCore($"{pre} with {m.Groups["b"].Value}") is { IsThis: false, IsThat: false } sb)
            {
                sa.Filters.AddRange(sb.Filters);
                return sa;
            }
        }
        // "All of your Tokens and [Puppet] trait Digimon" (토큰 또는 [Puppet] 특징 디지몬 전부) → 원래 규칙의 "your Tokens or ~"
        if ((m = Regex.Match(phrase, @"^(?<pre>(?:all of )?your) Tokens and (?<rest>.+Digimon.*)$", RegexOptions.IgnoreCase)).Success)
            return ParseCore($"{m.Groups["pre"].Value} Tokens or {m.Groups["rest"].Value}");
        return null;
    }
}
