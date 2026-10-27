using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.41.0 미지원 문장 15차 묶음의 생성 한글 — 원래 규칙(14차까지)이 읽지 못한 절 · 문장만 (SentenceParser.Batch41과 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth41;
    [ThreadStatic] private static int _fallbackDepth41;

    // =====================================================================
    // 0. 테이머를 세는 말 — 공식 "자신의 테이머가 1명 이하라면" · "자신의 테이머 1명마다" · "상대의 테이머 1명을 소멸시킨다"
    //    ("테이머 카드 1장" · "디지몬 또는 테이머 1장" · "디지몬과 테이머 1장마다"는 공식도 "장" — 그대로)
    // =====================================================================

    internal static string? TamerCount41(string? ko) =>
        ko == null ? null : Regex.Replace(ko, @"(?<!(?:디지몬 또는|디지몬과|디지몬/|디지몬이나) ?)테이머(?<j>가|는)?(?<sp> ?)(?<n>\d+)장", "테이머${j}${sp}${n}명");

    // =====================================================================
    // 1. 절 (RenderClause 끝)
    // =====================================================================

    private static string? RenderClause41(string rawClause)
    {
        if (_clauseDepth41 > 0) return null;
        _clauseDepth41++;
        try
        {
            string clause = Regex.Replace(SentenceParser.NormalizeForRender(rawClause), @"^(?:Inherited|Security) Effect\s*", "", IC).Trim();
            var lead = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
            string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value.Trim();

            // 링크 코스트 감소 (BT25-004 · BT25-045) — 공식 표기 예 BT23-016 "이 디지몬이 링크했을 때" · "지불하는 ~ 코스트 -1"
            Match m;
            if ((m = SentenceParser.LinkCostRe41.Match(SentenceParser.PipeList41(clause))).Success
                && PickKo("1 " + Regex.Replace(m.Groups["pick"].Value, @"\bcards?$", "card", IC)) is { } lpk)
            {
                string what = Regex.Replace(lpk, @"\s*1장$", "");
                return $"{LabelsKo40(m.Groups["labels"].Value)}{what}{J(what, "이", "가")} 이 디지몬에게 링크할 때, 지불하는 링크 코스트를 -{m.Groups["n"].Value} {(m.Groups["may"].Success ? "할 수 있다" : "한다")}.";
            }
            // ≪계승 (지정)≫ (BT26-032)
            if ((m = SentenceParser.SuccessionRe41.Match(clause)).Success && SpecKo(m.Groups["arg"].Value) is { } sk)
                return $"《계승 ({sk})》 (이 디지몬은 가장 위의 지정된 진화원 카드의 《계승》 이외의 효과를 모두 얻는다)";
            // 공식 BT9-074 "【소멸 시】 이 디지몬이 2색 이상이었다면, 메모리 +2"
            if ((m = Regex.Match(body, @"^When this Digimon had (?<n>\d+) or more colo?u?rs, (?<rest>.+)$", IC)).Success
                && Sentence(char.ToUpperInvariant(m.Groups["rest"].Value[0]) + m.Groups["rest"].Value[1..]) is { } had)
                return $"{LabelsKo40(labels)}이 디지몬이 {m.Groups["n"].Value}색 이상이었다면, {had.TrimEnd('.')}.";
            foreach (var alt in SentenceParser.ClauseRewrites41(rawClause, new CardData { Type = CardType.Digimon }))
                if (alt != rawClause && (RenderClauseCore(alt) ?? RenderClause40(alt)) is { } ko)
                    return ko;
            return null;
        }
        finally { _clauseDepth41--; }
    }

    // =====================================================================
    // 2. 문장 (Sentence 끝)
    // =====================================================================

    private static string? SentenceFallback41(string s, bool nested)
    {
        if (_fallbackDepth41 >= 3) return null;
        _fallbackDepth41++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (Regex.IsMatch(s, @"^Then,?\s", IC)) return null;
            Match m;
            // "다음에 ~ 진화할 때" — 공식 BT1-109 "이 턴 동안, 다음에 그린인 자신의 디지몬이 Lv.5에서 Lv.6으로 진화할 때에 지불하는 진화 코스트를 -4 한다" · EX5-029 "턴 종료까지 다음에 자신의 디지몬이 진화할 때, 지불하는 진화 코스트 -2"
            if (NextEvoKo41(s) is { } ne) return ne;
            // 자신의 어택 대상 변경 — 공식 BT9-052 "어택의 대상을 레스트 상태인 상대 디지몬 1마리로 변경할 수 있다"
            if ((m = Regex.Match(s, @"^(?<may>you may )?(?:Switch|Change) the (?:target of (?:the |this )?attack|attack target) to (?<t>\d+ of your opponent's .+)$", IC)).Success
                && TargetKo(m.Groups["t"].Value) is { } tk)
                return $"어택의 대상을 {tk}{J(tk, "으로", "로")} 변경{(m.Groups["may"].Success ? "할 수 있다" : "한다")}";
            // 공식 BT11-088 "상대의 디지몬 1마리를 다른 상대의 디지몬 1마리의 진화원 아래에 놓는다" · BT12-083 "~ 다른 상대의 디지몬의 진화원 아래 또는, 상대의 테이머 아래에 놓는다"
            if ((m = SentenceParser.OppUnderOppRe41.Match(Regex.Replace(s, @"^you may ", "", IC) is var s2 && s2.Length > 0 ? char.ToUpperInvariant(s2[0]) + s2[1..] : s2)).Success
                && TargetKo(m.Groups["t"].Value) is { } ot)
            {
                bool may = Regex.IsMatch(s, @"^you may ", IC);
                return $"{ot}{J(ot, "을", "를")} 다른 상대의 디지몬 1마리의 진화원 아래{(m.Groups["tamer"].Success ? " 또는 상대의 테이머 아래" : "")}에 놓{(may ? "을 수 있다" : "는다")}";
            }
            // 양쪽의 디지몬 전부를 패로 — 공식 BT14-027 "Lv.3의 디지몬 전부를 패로 되돌린다"
            if ((m = Regex.Match(s, @"^Return all (?<x>level \d+(?: or (?:lower|higher))?) Digimon to their owners?'s? hands?$", IC)).Success
                && TargetKo($"all of your opponent's {m.Groups["x"].Value} Digimon") is { } allKo)
                return $"{allKo.Replace("상대의 디지몬", "디지몬")}를 패로 되돌린다";
            // 효과 발휘 — 공식 BT18-066 "놓은 카드의 【등장 시】 효과 1개를 이 디지몬의 효과로서 발휘할 수 있다" · EX5-059 "이 디지몬의 【등장 시】 효과를 발휘한다"
            if ((m = SentenceParser.ActivateRe41.Match(s)).Success && ActivateKo41(m) is { } ak) return ak;
            // 공식 BT13-091 "이 디지몬의 맨 위 카드를 파기한다"
            if ((m = Regex.Match(s, @"^Trash the top card of (?<t>this Digimon|1 of your .+)$", IC)).Success)
            {
                string who = m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : TargetKo(m.Groups["t"].Value) ?? "";
                if (who.Length > 0) return $"{who}의 맨 위 카드를 파기한다";
            }
            foreach (var alt in SentenceParser.Rewrites41(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return ko;
            return null;
        }
        finally { _fallbackDepth41--; }
    }

    /// <summary>SentenceParser.NextEvoStatic41의 문장 → "이 턴 동안, 다음에 ~ 진화할 때, ~"</summary>
    private static string? NextEvoKo41(string s)
    {
        if (SentenceParser.NextEvoStatic41(s) is not { } ns) return null;
        if (RenderClauseCore(ns.Static) is not { } ko) return null;
        ko = ko.TrimEnd('.', ' ');
        // "Lv.5인 그린인 자신의 디지몬이 Lv.6의 디지몬 카드로 진화할 때" → 공식 "그린인 자신의 디지몬이 Lv.5에서 Lv.6으로 진화할 때"
        ko = Regex.Replace(ko, @"Lv\.(?<a>\d+)인 (?<col>\S+인 )?자신의 디지몬이 Lv\.(?<b>\d+)(?:인|의) 디지몬 카드로 진화할 때",
            x => $"{x.Groups["col"].Value}자신의 디지몬이 Lv.{x.Groups["a"].Value}에서 Lv.{x.Groups["b"].Value}{(x.Groups["b"].Value is "3" or "6" or "0" ? "으로" : "로")} 진화할 때");
        return ns.OneShot ? $"이 턴 동안, 다음에 {ko}" : $"이 턴 동안, {ko}";
    }

    [ThreadStatic] private static int _pickDepth41;

    /// <summary>"1 non-white level 4 or lower [System] or [Seven Code] trait card" — 색 이외 (공식 표기 "화이트 이외의 ~")</summary>
    private static string? PickKo41(string p)
    {
        if (_pickDepth41 > 0) return null;
        var m = Regex.Match(p, @"\bnon-(?<c>red|blue|yellow|green|black|purple|white) ", IC);
        if (!m.Success) return null;
        _pickDepth41++;
        try
        {
            string stripped = p[..m.Index] + p[(m.Index + m.Length)..];
            if (PickKo(stripped) is not { } ko) return null;
            return $"{ColorKo(m.Groups["c"].Value)} 이외의 {ko}";
        }
        finally { _pickDepth41--; }
    }

    /// <summary>SentenceParser.ActivateRe41 → "~의 【등장 시】 효과 (1개)를 (이 디지몬의 효과로서) 발휘한다"</summary>
    private static string? ActivateKo41(Match m)
    {
        var trigs = Regex.Matches(m.Groups["trig"].Value, @"\[([^\]]+)\]").Select(x => LabelKo(x.Groups[1].Value.Trim())).ToList();
        if (trigs.Count == 0 || trigs.Any(t => t == null)) return null;
        string trig = string.Join(" 또는 ", trigs);
        string who = m.Groups["who"].Value.ToLowerInvariant(), what = m.Groups["what"].Value;
        string? owner = who switch
        {
            "this digimon's" => "이 디지몬",
            "its" or "that digimon's" => "그 디지몬",
            "that card's" or "the placed card's" => who == "that card's" ? "그 카드" : "놓은 카드",
            _ when m.Groups["name"].Success => "자신의 " + NamesKo(m.Groups["name"].Value),
            _ when what.Equals("that card", StringComparison.OrdinalIgnoreCase) => "그 카드",
            _ when what.Equals("the placed card", StringComparison.OrdinalIgnoreCase) => "놓은 카드",
            _ when what.Equals("the Digimon this effect played", StringComparison.OrdinalIgnoreCase) => "이 효과로 등장한 디지몬",
            _ when what.StartsWith("1 of your", StringComparison.OrdinalIgnoreCase) => TargetKo(what),
            _ when Regex.Match(what, @"^(?<p>1 Digimon card with .+?) in this Digimon's digivolution cards$", IC) is { Success: true } sm
                && PickKo(sm.Groups["p"].Value) is { } pk => "이 디지몬의 진화원의 " + Regex.Replace(pk, @"\s*1장$", "") + " 1장",
            _ => null,
        };
        if (owner == null) return null;
        bool asThis = Regex.IsMatch(m.Value, @" as (?:an effect of this Digimon|if (?:it )?was this Digimon's effect)$", IC);
        string count = m.Groups["one"].Success ? " 1개" : "";
        string verb = m.Groups["may"].Success ? "발휘할 수 있다" : "발휘한다";
        return $"{owner}의 {trig} 효과{count}{(count.Length > 0 ? "를" : "를")} {(asThis ? "이 디지몬의 효과로서 " : "")}{verb}";
    }
}
