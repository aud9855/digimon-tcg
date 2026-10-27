using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.41.0 조건 · 진화원 동작의 생성 한글 — 원래 규칙이 읽지 못한 것만 (SentenceParser.Batch41Cond · Batch41Src와 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _condDepth41;

    private static Cond? CondKo41(string cond)
    {
        if (_condDepth41 >= 3) return null;
        _condDepth41++;
        try
        {
            string c0 = cond.Trim();
            Match m;
            foreach (var alt in SentenceParser.CondRewrites41(c0))
                if (alt != c0 && CondKo(alt) is { } ra) return ra;
            // 공식 BT23-066 "트래시에서 진화하고 있었다면,"
            if (SentenceParser.FromTrashCondRe41.IsMatch(c0))
                return new("트래시에서 진화하고 있었을 때,", "트래시에서 진화하고 있는 동안,", "트래시에서 진화하고 있었다면,");
            if ((m = Regex.Match(c0, @"^(?:this Digimon has (?<n>\d+) or more colors in its digivolution cards|this Digimon's digivolution cards have (?<n>\d+) or more colors)$", IC)).Success)
                return Be($"이 디지몬의 진화원의 색이 {m.Groups["n"].Value}색 이상");
            // 공식 EX2-053 "자신의 「마더 디・리퍼」의 진화원이 5장 이상 있을 때," · EX2-054 "진화원을 6장 이상 가진 자신의 「마더 디・리퍼」가 있는 동안,"
            if ((m = Regex.Match(c0, @"^you have an? (?<x>\[[^\]]+\]) with (?<n>\d+) or more digivolution cards(?: in play)?$", IC)).Success)
            {
                string x = NamesKo(m.Groups["x"].Value);
                return Have($"진화원을 {m.Groups["n"].Value}장 이상 가진 자신의 {x}{J(x, "이", "가")}");
            }
            if ((m = Regex.Match(c0, @"^one of your (?<x>\[[^\]]+\])s? has (?<n>\d+) or more digivolution cards$", IC)).Success)
            {
                string x = NamesKo(m.Groups["x"].Value);
                return Have($"자신의 {x}의 진화원이 {m.Groups["n"].Value}장 이상");
            }
            // 공식 BT8-029 "진화원을 가진 상대의 디지몬이 있는 동안,"
            if (Regex.IsMatch(c0, @"^your opponent has a Digimon with (?:a digivolution card|digivolution cards)(?: in play)?$", IC))
                return Have("진화원을 가진 상대의 디지몬이");
            if ((m = Regex.Match(c0, @"^you have an? (?<x>(?:[\w-]+ )?(?:Tamer|Digimon)) with (?<rest>.+)$", IC)).Success
                && PickKo($"1 {m.Groups["x"].Value} card with {m.Groups["rest"].Value}") is { } pk)
            {
                string who = Regex.Replace(Regex.Replace(pk, @"\s*1장$", ""), @"(테이머|디지몬) 카드$", "자신의 $1");
                return Have(who + J(who, "이", "가"));
            }
            if ((m = Regex.Match(c0, @"^you have (?<n>\d+) or more suspended Digimon with (?<x>\[[^\]]+\])$", IC)).Success)
                return Have($"특징에 {NamesKo(m.Groups["x"].Value)}{J(NamesKo(m.Groups["x"].Value), "을", "를")} 포함하는 레스트 상태의 자신의 디지몬이 {m.Groups["n"].Value}마리 이상");
            // 공식 BT21-010 "특징 「히어로」를 가진 명칭이 서로 다른 자신의 테이머가 3명 이상 있거나,"
            if ((m = Regex.Match(c0, @"^you have (?<n>\d+) or more (?<x>\[[^\]]+\]) trait Tamers with different names$", IC)).Success)
            {
                string x = NamesKo(m.Groups["x"].Value);
                return Have($"특징으로 {x}{J(x, "을", "를")} 가진 명칭이 서로 다른 자신의 테이머가 {m.Groups["n"].Value}명 이상");
            }
            // 공식 EX10-012 "「어둠의 4천왕」이 기술되어 있는 디지몬 이외의 자신의 디지몬이 없다면,"
            if ((m = Regex.Match(c0, @"^you don't have any Digimon other than Digimon with (?<x>\[[^\]]+\]) in their texts?$", IC)).Success)
            {
                string x = NamesKo(m.Groups["x"].Value);
                return NotHave($"{x}{J(x, "이", "가")} 기술되어 있는 디지몬 이외의 자신의 디지몬이");
            }
            // "A or B" — 공식 BT21-010 "~ 있거나, ~인 동안," · BT7-062 "~ 있는 동안, 또는 ~ 있는 동안,"
            foreach (var (a, b) in SentenceParser.OrSplits41(c0))
                if (CondKo(a) is { } ka && CondKo(b) is { } kb)
                    return new($"{ka.If.TrimEnd(',')} 또는 {kb.If}", $"{ka.While.TrimEnd(',')} 또는 {kb.While}", $"{ka.Then.TrimEnd(',')} 또는 {kb.Then}");
            return null;
        }
        finally { _condDepth41--; }
    }
}
