using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.55.0 미지원 문장 29차 묶음의 생성 한글 — 원래 규칙(28차까지)이 옮기지 못한 절만 (SentenceParser.Batch55와 같은 절)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth55;
    [ThreadStatic] private static int _koGate55;
    private static bool KoGate55 => _koGate55 > 0;

    private static string? RenderClause55(string rawClause)
    {
        if (_clauseDepth55 > 0) return null;
        _clauseDepth55++;
        try
        {
            if (ClauseSpecialKo55(Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", IC)) is { } special) return special;
            // 29차의 셈 (CounterKo55)은 문이 열려 있을 때만 — 원래 규칙이 옮긴 절은 그대로
            _koGate55++;
            try { return RenderClause(rawClause); }
            finally { _koGate55--; }
        }
        finally { _clauseDepth55--; }
    }

    private static string? ClauseSpecialKo55(string t)
    {
        Match m;
        // BT26-102
        if ((m = Regex.Match(t, @"^\[Main\]\s*By placing (?<n>\d+) (?<t>\[[^\]]+\]) trait Digimon cards from your battle area, link cards,? or trash as 1 of your (?<h>\[[^\]]+\]) trait Digimon's bottom digivolution cards, that Digimon may digivolve into (?<x>\[[^\]]+\]) in the hand, ignoring (?:its )?digivolution requirements and without paying the cost\.?$", IC)).Success)
        {
            string tr = NamesKo(m.Groups["t"].Value, trait: true), h = NamesKo(m.Groups["h"].Value, trait: true), x = NamesKo(m.Groups["x"].Value);
            return $"【메인】 자신의 배틀 에어리어/링크 카드/트래시에서 특징으로 {tr}{J(tr, "을", "를")} 가진 디지몬 카드 {m.Groups["n"].Value}장을 특징으로 {h}{J(h, "을", "를")} 가진 자신의 디지몬 1마리의 진화원 아래에 놓는 것으로, 그 디지몬을 패의 {x}{J(x, "으로", "로")} 진화 조건을 무시하고 코스트를 지불하지 않고 진화시킬 수 있다.";
        }
        // EX1-061 진화원 효과
        if ((m = Regex.Match(t, @"^\[Your Turn\]\s*While this Digimon has (?<n>\[[^\]]+\]) in its name, all (?:of )?your Digimon with ＜(?<k>[^＞]+)＞ can (?:also )?attack your opponent's unsuspended level 4 or lower Digimon\.?$", IC)).Success
            && KeywordKo(m.Groups["k"].Value, false) is { } kw)
        {
            string n = NamesKo(m.Groups["n"].Value);
            return $"【자신의 턴】 이 디지몬의 명칭에 {n}{J(n, "이", "가")} 포함되는 동안, {kw}{J(kw, "을", "를")} 가진 자신의 디지몬 전부는 액티브 상태인 Lv.4 이하의 상대의 디지몬에게도 어택할 수 있다.";
        }
        // EX10-059 첫 문단
        if ((m = Regex.Match(t, @"^\[On Play\]\s*\[When Digivolving\]\s*Choose 1 card in your opponent's hand without looking and place it as any of their Digimon's bottom digivolution card or under any of their Tamers\. Then, by placing (?<n>\d+) (?<t>\[[^\]]+\]) trait Digimon cards from your trash as this Digimon's top digivolution cards, delete 1 of their Digimon or Tamers with cards under it\.?$", IC)).Success)
        {
            string tr = NamesKo(m.Groups["t"].Value, trait: true);
            return $"【등장 시】 【진화 시】 상대의 패를 보지 않고 1장을 골라, 상대의 디지몬 1마리의 진화원 맨 아래 또는 상대의 테이머 1명의 아래에 놓는다. 그 후, 자신의 트래시에서 특징으로 {tr}{J(tr, "을", "를")} 가진 디지몬 카드 {m.Groups["n"].Value}장을 이 디지몬의 진화원 위에 놓는 것으로, 아래에 카드가 있는 상대의 디지몬 또는 테이머 1개를 소멸시킨다.";
        }
        // EX3-020 · EX3-041
        if ((m = SentenceParser.DnaLevelRe55.Match(t)).Success)
        {
            string f = NamesKo("[" + m.Groups["for"].Value + "]");
            return $"【자신의 턴】 패의 {f}{J(f, "은", "는")} 이 디지몬을 Lv.{m.Groups["lv"].Value}인 디지몬으로 취급하여 조그레스 진화할 수 있다.";
        }
        return null;
    }

    /// <summary>셈 (SentenceParser.ParseCounter55와 같은 표)</summary>
    private static string? CounterKo55(string counter)
    {
        if (!KoGate55) return null;
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if (Regex.IsMatch(s, @"^Digimon and Tamers? your opponent has (?:in play|on the field)$", IC))
            return "상대의 디지몬과 테이머 1마리(명)마다";
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait card with a different name in your trash$", IC)).Success)
        {
            string n = NamesKo(m.Groups["x"].Value, trait: true);
            return $"자신의 트래시의 특징으로 {n}{J(n, "을", "를")} 가진 명칭이 서로 다른 카드 1장마다";
        }
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\]) Digimon card and Option card with a (?:memory |use )?cost of (?<n>\d+) in your trash$", IC)).Success)
        {
            string n = NamesKo(m.Groups["x"].Value, trait: true);
            return $"자신의 트래시의 특징으로 {n}{J(n, "을", "를")} 가진 디지몬 카드와 코스트 {m.Groups["n"].Value}의 옵션 카드 1장마다";
        }
        return null;
    }
}
