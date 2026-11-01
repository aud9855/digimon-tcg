using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.57.0 미지원 문장 31차 묶음의 생성 한글 — 원래 규칙(30차까지)이 옮기지 못한 절만 (SentenceParser.Batch57과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth57;
    [ThreadStatic] private static int _koGate57;
    private static bool KoGate57 => _koGate57 > 0;

    private static string? RenderClause57(string rawClause)
    {
        if (_clauseDepth57 > 0) return null;
        _clauseDepth57++;
        _koGate57++;
        try
        {
            string t = Regex.Replace(rawClause.Replace(' ', ' ').Trim(), @"^Inherited Effect\s*", "", IC);
            if (ClauseSpecialKo57(t) is { } special) return special;
            if (SentenceParser.SplitTwoEffects57(t) is { } two)
                return RenderClause(two.First) is { } a && RenderClause(two.Second) is { } b ? a + " " + b : null;
            string alt = SentenceParser.ClauseRewrite57(t);
            // 바꿔 쓴 것도 31차 셈 · 조건 · 비용도 없으면 다시 옮기지 않는다 (같은 결과 — 시간만 든다)
            if (alt == t && !Hint57.IsMatch(t)) return null;
            if (RenderClause(alt) is not { } ko) return null;
            Match m;
            // BT10-057 "If you gain 2 or more memory by this effect" (읽을 때는 그 디지몬의 수로)
            if ((m = Regex.Match(t, @"If you gain (?<n>\d+) or more memory by this effect, ", IC)).Success)
                ko = Regex.Replace(ko, @"그 후, [^,]*?\d+마리 이상 있을 때, ", $"이 효과로 메모리를 {m.Groups["n"].Value} 이상 얻었다면, ");
            // EX2-018 "This effect can't increase the number of cards in your security stack to 6 or more."
            if ((m = Regex.Match(t, @"This effect can't increase the number of cards in your security stack to (?<n>\d+) or more\.?$", IC)).Success)
                ko = Regex.Replace(ko, @"자신의 시큐리티가 \d+장 이하일 때, ", "") + $" 이 효과로 자신의 시큐리티를 {m.Groups["n"].Value}장 이상으로 늘릴 수 없다.";
            return ko;
        }
        finally { _clauseDepth57--; _koGate57--; }
    }

    /// <summary>절 하나로 옮기는 것 (SentenceParser.ClauseSpecial57 · 고르는 효과 머리말 · 31차 동작)</summary>
    private static string? ClauseSpecialKo57(string t)
    {
        Match m;
        // EX5-052
        if ((m = SentenceParser.TamerSuspendLockRe57.Match(t)).Success)
            return $"【상대의 턴】 등장 코스트가 {m.Groups["n"].Value} 이하인 상대의 테이머 전부는 레스트할 수 없다.";
        // EX4-068 머리말
        var prefix = Regex.Match(t, @"^(?<lab>(?:\s*\[[^\]]+\])*)\s*(?<body>.*)$");
        string lab = LabelsKo57(prefix.Groups["lab"].Value);
        string body = prefix.Groups["body"].Value.Trim();
        if ((m = SentenceParser.AgainRe57.Match(body)).Success && CounterKo(m.Groups["cnt"].Value) is { } c1)
            return $"{lab}이하의 효과를 발휘한다. {c1} 그 효과를 한 번 더 발휘한다.";
        // BT2-041 머리말
        if ((m = SentenceParser.SuspendEachRe57.Match(body)).Success)
        {
            string col = m.Groups["c"].Success ? ColorKo57(m.Groups["c"].Value.Trim()) + "인 " : "";
            return $"{lab}{col}자신의 테이머를 전부 레스트시킨다. 이 효과로 레스트시킨 {col}테이머 1명마다, 이하의 효과를 발휘한다.";
        }
        // EX12-037 머리말 "Delete 1 of your opponent's Digimon. Then, for every 5 of this Digimon's digivolution cards, activate 1 of the effects below:"
        if ((m = SentenceParser.ThenEveryChooseRe57.Match(body)).Success && CounterKo(m.Groups["cnt"].Value) is { } c2
            && RenderClause("[Main] " + m.Groups["pre"].Value + ".") is { } preKo && preKo.StartsWith("【메인】 ", StringComparison.Ordinal))
            return $"{lab}{preKo["【메인】 ".Length..]} 그 후, {c2} 이하의 효과에서 1개를 골라 발휘한다.";
        // BT17-041
        if ((m = Regex.Match(body, @"^By suspending up to (?<n>\d+) of your (?<c>Red|Blue|Yellow|Green|Black|Purple|White) Tamers, for (?:every|each) Tamer this effect suspended, this Digimon gains ＜Security A\. \+(?<k>\d+)＞ for the turn\.?(?:\s*\([^)]*\))?$", IC)).Success
            && KeywordKo("Security A. +" + m.Groups["k"].Value, withReminder: true) is { } sa)
            return $"{lab}{ColorKo57(m.Groups["c"].Value)}인 자신의 테이머를 {m.Groups["n"].Value}명까지 레스트시키는 것으로, 이 효과로 레스트시킨 테이머 1명마다, 이번 턴 동안, 이 디지몬은 {sa}{J(sa, "을", "를")} 얻는다.";
        // BT10-104
        if ((m = Regex.Match(body, @"^(?<a>[^.]+)\. Then, you may play 1 (?<x>\[[^\]]+\]) from your trash for its memory cost\. If you play a Digimon card with DigiXros requirements by this effect, you may DigiXros by placing any of the specified Digimon from your trash at the bottom of this card as digivolution cards\.?$", IC)).Success
            && RenderClause("[Main] " + m.Groups["a"].Value + ".") is { } aKo && aKo.StartsWith("【메인】 ", StringComparison.Ordinal))
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"{lab}{aKo["【메인】 ".Length..]} 그 후, 자신의 트래시에서 {x} 1장을 등장 코스트를 지불하고 등장시킬 수 있다. 이 효과로 디지크로스 조건을 가진 디지몬 카드를 등장시킨다면, 자신의 트래시에서 지정된 디지몬을 이 카드의 진화원 아래에 놓아 디지크로스할 수 있다.";
        }
        // BT6-111
        if ((m =Regex.Match(body, @"^You may pay up to (?<n>\d+) memory\. If you do, this Digimon gets \+(?<dp>\d+) DP for the turn for each memory paid\.?$", IC)).Success)
            return $"{lab}메모리를 {m.Groups["n"].Value}까지 지불할 수 있다. 지불했다면, 이번 턴 동안, 이 디지몬은 지불한 메모리 1마다 DP +{m.Groups["dp"].Value}.";
        return null;
    }

    /// <summary>31차 셈 · 조건 · 비용 (CounterKo57 · CondKo57 · CostKo57)이 쓰이는 절</summary>
    private static readonly Regex Hint57 = new(@"DP this Digimon has|of this Digimon's link cards|same-level cards|colou?r your Digimon have|or more colou?rs in its digivolution|no Digimon with DP equal|paying up to \d+ memory", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string LabelsKo57(string labels)
    {
        string s = "";
        foreach (Match l in Regex.Matches(labels, @"\[([^\]]+)\]"))
            s += (LabelKo57(l.Groups[1].Value.Trim()) ?? $"[{l.Groups[1].Value.Trim()}]") + " ";
        return s;
    }

    private static string? LabelKo57(string label) => label.ToLowerInvariant() switch
    {
        "main" => "【메인】",
        "on play" => "【등장 시】",
        "when digivolving" => "【진화 시】",
        "when attacking" => "【어택 시】",
        "on deletion" => "【소멸 시】",
        "end of attack" => "【어택 종료 시】",
        "your turn" => "【자신의 턴】",
        "opponent's turn" => "【상대의 턴】",
        "all turns" => "【서로의 턴】",
        "security" => "【시큐리티】",
        "once per turn" => "[턴에 1회]",
        _ => null,
    };

    private static string ColorKo57(string c) => c.ToLowerInvariant() switch
    {
        "red" => "레드", "blue" => "블루", "yellow" => "옐로", "green" => "그린", "black" => "블랙", "purple" => "퍼플", "white" => "화이트", _ => c,
    };

    /// <summary>셈 (SentenceParser.ParseCounter57과 같은 표)</summary>
    private static string? CounterKo57(string counter)
    {
        if (!KoGate57) return null;
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if ((m = Regex.Match(s, @"^(?<n>\d+) DP this Digimon has$", IC)).Success)
            return $"이 디지몬의 DP {m.Groups["n"].Value}마다";
        if (Regex.IsMatch(s, @"^of this Digimon's link cards$", IC))
            return "이 디지몬의 링크 카드 1장마다";
        if ((m = Regex.Match(s, @"^(?<n>\d+) same-level cards this Digimon's stack has$", IC)).Success)
            return $"이 디지몬의 겹쳐진 카드 중 같은 Lv.의 카드 {m.Groups["n"].Value}장마다";
        if (Regex.IsMatch(s, @"^colou?r your Digimon have$", IC))
            return "자신의 디지몬이 가진 색 1색마다";
        return null;
    }

    /// <summary>조건 (SentenceParser.ParseStaticCondition57과 같은 표)</summary>
    private static Cond? CondKo57(string cond)
    {
        if (!KoGate57) return null;
        string s = cond.Trim();
        Match m;
        if ((m = Regex.Match(s, @"^this Digimon has (?<n>\d+) or more colou?rs in its digivolution cards$", IC)).Success)
        {
            string head = $"이 디지몬의 진화원의 색이 {m.Groups["n"].Value}색 이상";
            return new(head + "이라면,", head + "인 동안,", head + "이라면,");
        }
        if (Regex.IsMatch(s, @"^your opponent has no Digimon with DP equal to or higher than this Digimon$", IC))
            return new("이 디지몬의 DP 이상인 상대의 디지몬이 없다면,", "이 디지몬의 DP 이상인 상대의 디지몬이 없는 동안,", "이 디지몬의 DP 이상인 상대의 디지몬이 없다면,");
        return null;
    }

    /// <summary>비용 (SentenceParser.ParseCost57과 같은 표)</summary>
    private static string? CostKo57(string cost)
    {
        if (!KoGate57) return null;
        if (Regex.Match(cost.Trim(), @"^paying up to (?<n>\d+) memory$", IC) is { Success: true } m)
            return $"메모리를 {m.Groups["n"].Value}까지 지불하는 것으로,";
        return null;
    }
}
