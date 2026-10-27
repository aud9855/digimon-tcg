using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.41.0 조건 — 원래 규칙이 읽지 못한 조건만 (ParseEventCondition 끝). 렌더러(KoreanEffectRenderer.Batch41)도 같은 읽기</summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _condDepth41;

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다)</summary>
    internal static IEnumerable<string> CondRewrites41(string cond)
    {
        string c = cond.Trim();
        // 원문 오타 "your have" (BT9-080 · BT9-081)
        if (Regex.IsMatch(c, @"^your have\b", I)) yield return Regex.Replace(c, @"^your have\b", "you have", I);
        // "this Digimon has 4 or more [Vemmon] in its digivolution cards" (EX11-046) → "there are 4 or more [Vemmon] in this Digimon's digivolution cards"
        if (Regex.Match(c, @"^this Digimon has (?<n>\d+ or (?:more|fewer)) (?<x>\[[^\]]+\])s? in its digivolution cards$", I) is { Success: true } hm)
            yield return $"there are {hm.Groups["n"].Value} {hm.Groups["x"].Value} in this Digimon's digivolution cards";
        // "a Digimon with the [Royal Knight] or [X Antibody] trait is in play" (BT6-111) → "there is a Digimon with ~"
        if (Regex.Match(c, @"^an? (?<x>(?:[^,]*? )?Digimon with .+?) is in play$", I) is { Success: true } ip)
            yield return $"there is a {ip.Groups["x"].Value}";
    }

    /// <summary>트래시에서 진화하는 중 (BT10-080 · BT23-066 · BT9-075 · BT17-065 · BT17-067 · BT17-073 — 공식 BT10-080 "트래시에서 진화했을 때")</summary>
    internal static readonly Regex FromTrashCondRe41 = new(@"^(?:this card is digivolving|digivolving|this Digimon is digivolving|is digivolving|this digivolved|this Digimon digivolved|it is digivolving) from (?:the|your) trash$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Func<EffectContext, bool>? ParseEventCondition41(string cond)
    {
        if (_condDepth41 >= 3) return null;
        _condDepth41++;
        try
        {
            string c0 = cond.Trim();
            Match m;
            foreach (var alt in CondRewrites41(c0))
                if (alt != c0 && ParseEventCondition(alt) is { } ra) return ra;
            if (FromTrashCondRe41.IsMatch(c0))
                return c => c.Engine.DigivolvingFromTrash;
            // "this Digimon has 4 or more colors in its digivolution cards" (EX12-076) · "this Digimon's digivolution cards have 4 or more colors"
            if ((m = Regex.Match(c0, @"^(?:this Digimon has (?<n>\d+) or more colors in its digivolution cards|this Digimon's digivolution cards have (?<n>\d+) or more colors)$", I)).Success)
            {
                int n = int.Parse(m.Groups["n"].Value);
                return c => c.Self != null && c.Self.FaceUpSources.SelectMany(x => x.AllColors).Distinct().Count() >= n;
            }
            // "you have a [Mother D-Reaper] with 6 or more digivolution cards (in play)" (EX2-054) · "one of your [Mother D-Reaper]s has 5 or more digivolution cards" (EX2-053)
            if ((m = Regex.Match(c0, @"^(?:you have an? (?<x>\[[^\]]+\]) with (?<n>\d+) or more digivolution cards(?: in play)?|one of your (?<x>\[[^\]]+\])s? has (?<n>\d+) or more digivolution cards)$", I)).Success)
            {
                string x = m.Groups["x"].Value.Trim('[', ']'); int n = int.Parse(m.Groups["n"].Value);
                return c => c.Owner.BattleArea.Any(d => CardNames.Is(d.TopCard, x) && d.EvolutionSources.Count >= n);
            }
            // "your opponent has a Digimon with a digivolution card in play" (BT8-029)
            if (Regex.IsMatch(c0, @"^your opponent has a Digimon with (?:a digivolution card|digivolution cards)(?: in play)?$", I))
                return c => c.Enemy.BattleArea.Any(d => d.EvolutionSources.Count > 0);
            // "you have a Tamer with [Three Musketeers] in its text" (BT25-082) · "you have a non-white Digimon with [Lucemon] in its name" (BT18-086)
            if ((m = Regex.Match(c0, @"^you have an? (?<x>(?:[\w-]+ )?(?:Tamer|Digimon)) with (?<rest>.+)$", I)).Success
                && ParseCardPick($"1 {m.Groups["x"].Value} card with {m.Groups["rest"].Value}") is { } tp)
            {
                bool tamer = Regex.IsMatch(m.Groups["x"].Value, @"Tamer$", I);
                return c => tamer ? c.Owner.Tamers.Any(tp.Match) : c.Owner.BattleArea.Any(d => tp.Match(d.TopCard));
            }
            // "you have 2 or more suspended Digimon with [Vegetation]" (EX3-045 — 특징에 「식물형」을 포함)
            if ((m = Regex.Match(c0, @"^you have (?<n>\d+) or more suspended Digimon with (?<x>\[[^\]]+\])$", I)).Success)
            {
                string x = m.Groups["x"].Value.Trim('[', ']'); int n = int.Parse(m.Groups["n"].Value);
                return c => c.Owner.BattleArea.Count(d => d.IsSuspended && TargetSpec.HasTrait(d.TopCard, x)) >= n;
            }
            // "you have 3 or more [Hero] trait Tamers with different names" (BT21-010 · BT21-040 — 공식 "명칭이 서로 다른 특징 「히어로」를 가진 자신의 테이머가 3명 이상")
            if ((m = Regex.Match(c0, @"^you have (?<n>\d+) or more (?<x>\[[^\]]+\]) trait Tamers with different names$", I)).Success)
            {
                string x = m.Groups["x"].Value.Trim('[', ']'); int n = int.Parse(m.Groups["n"].Value);
                return c => c.Owner.Tamers.Where(t => TargetSpec.HasTrait(t, x)).Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= n;
            }
            // "you don't have any Digimon other than Digimon with [Dark Masters] in their texts" (EX10-012 · EX10-020 · EX10-035 · EX10-057)
            if ((m = Regex.Match(c0, @"^you don't have any Digimon other than Digimon with (?<x>\[[^\]]+\]) in their texts?$", I)).Success)
            {
                string x = m.Groups["x"].Value.Trim('[', ']');
                return c => c.Owner.BattleArea.All(d => TargetSpec.TextHas(d.TopCard, x));
            }
            // "A or B" — 어느 한쪽 (BT21-010 · BT21-040 · EX3-033 · BT7-062 · ST20-10 · ST21-10 · BT9-075 · BT17-073)
            foreach (var (a, b) in OrSplits41(c0))
                if (ParseEventCondition(a) is { } ca && ParseEventCondition(b) is { } cb)
                    return c => ca(c) || cb(c);
            return null;
        }
        finally { _condDepth41--; }
    }

    /// <summary>"A or B" 조건을 나누는 자리들 (렌더러도 쓴다) — 이름 목록 "[A] or [B]" 안은 나누지 않는다. 주어가 빠진 뒤쪽은 앞의 주어를 붙인다</summary>
    internal static IEnumerable<(string A, string B)> OrSplits41(string c0)
    {
        foreach (Match or in Regex.Matches(c0, @",? or (?=(?:you|your|this|a|an|there|the|is|has)\b|\[)", I))
        {
            string a = c0[..or.Index].Trim(), b = c0[(or.Index + or.Length)..].Trim();
            if (a.Length == 0 || b.Length == 0) continue;
            if (Regex.IsMatch(a, @"\]$") && b.StartsWith("[") && !Regex.IsMatch(b, @"^\[[^\]]+\] is\b", I)) continue;   // 이름 목록
            if (Regex.IsMatch(b, @"^(?:is|has)\b", I))
            {
                if (Regex.Match(a, @"^(?<s>this Digimon|this card)\b", I) is { Success: true } subj) b = subj.Groups["s"].Value + " " + b;
                else continue;
            }
            yield return (a, b);
        }
    }
}
