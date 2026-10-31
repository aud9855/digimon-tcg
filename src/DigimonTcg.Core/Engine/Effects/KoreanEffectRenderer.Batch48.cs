using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.48.0 미지원 문장 22차 묶음의 생성 한글 — 원래 규칙(21차까지)이 옮기지 못한 절 · 문장만 (SentenceParser.Batch48과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth48;
    [ThreadStatic] private static int _koGate48;
    private static bool KoGate48 => _koGate48 > 0;

    /// <summary>절: 22차 문장 한글을 켠 채로 다시 · 같은 뜻 다른 표기(SentenceParser.ClauseRules48)로</summary>
    private static string? RenderClause48(string rawClause)
    {
        if (_clauseDepth48 > 0) return null;
        _clauseDepth48++;
        _koGate48++;
        try
        {
            if (RenderClause(rawClause) is { } again) return again;
            string alt = SentenceParser.ClauseRewrite48(rawClause);
            return alt != rawClause ? RenderClause(alt) : null;
        }
        finally { _clauseDepth48--; _koGate48--; }
    }

    /// <summary>문장: 덱 아래로 되돌리기 (SentenceParser.ActionExtra48)</summary>
    private static string? SentenceFallback48(string s, bool nested)
    {
        if (!KoGate48) return null;
        s = s.Trim().TrimEnd('.').Trim();
        var lead = Regex.Match(s, @"^(?:Then,\s*)?(?:if (?<cond>[^,]+),\s*)?(?<may>you may )?(?<act>.+)$", IC);
        string? act = ActionKo48(lead.Groups["act"].Value, lead.Groups["may"].Success);
        if (act == null) return null;
        string pre = s.StartsWith("Then", StringComparison.OrdinalIgnoreCase) ? "그 후, " : "";
        if (lead.Groups["cond"].Success)
        {
            if (CondKo(lead.Groups["cond"].Value) is not { } ck) return null;
            pre += ck.Then + " ";
        }
        return pre + act;
    }

    private static string? ActionKo48(string a, bool may)
    {
        string end(string verb, string mayVerb) => may ? mayVerb : verb;
        Match m;
        if ((m = Regex.Match(a, @"^return (?<upto>up to )?(?<k>\d+) cards? (?:in|from) your hand to the bottom of (?:the|your) deck(?: in any order)?$", IC)).Success)
            return $"자신의 패 {m.Groups["k"].Value}장{(m.Groups["upto"].Success ? "까지" : "")}을 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}";
        if ((m = Regex.Match(a, @"^return (?<upto>up to )?(?<k>\d+) (?<nde>non-Digi-Egg )?cards? from (?:their|your opponent's) trash to the bottom of (?:the|their) deck(?: in any order)?$", IC)).Success)
            return $"상대의 트래시에서 {(m.Groups["nde"].Success ? "디지타마 이외의 " : "")}카드 {m.Groups["k"].Value}장{(m.Groups["upto"].Success ? "까지" : "")}를 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}";
        if ((m = Regex.Match(a, @"^place (?<k>\d+) (?<nde>non-Digi-Egg )?cards? in your opponent's trash at the bottom of their deck(?: in any order)?$", IC)).Success)
            return $"상대의 트래시의 {(m.Groups["nde"].Success ? "디지타마 이외의 " : "")}카드 {m.Groups["k"].Value}장을 좋아하는 순서로 덱 아래에 {end("놓는다", "놓을 수 있다")}";
        // BT13-110 (패의 디지몬 카드를 육성 에어리어의 그 이름의 디지몬 진화원 아래에)
        if ((m = Regex.Match(a, @"^place 1 Digimon card from your hand under (?:1 of |a )?your (?<x>\[[^\]]+\]) in the breeding area as its bottom digivolution card$", IC)).Success)
            return $"자신의 패에서 디지몬 카드 1장을 육성 에어리어의 자신의 {NamesKo(m.Groups["x"].Value)} 1마리의 진화원 아래에 {end("놓는다", "놓을 수 있다")}";
        // BT13-110 ≪딜레이≫ (육성 에어리어 디지몬의 진화원에서 특징을 가진 카드 등장)
        if ((m = Regex.Match(a, @"^play 1 card with the \[(?<t>[^\]]+)\] trait from the digivolution cards of your Digimon in the breeding area without paying (?:its|the) cost(?<noplay> ⟦no-onplay⟧)?$", IC)).Success)
        {
            string tk = TraitKo(m.Groups["t"].Value);
            return $"육성 에어리어의 자신의 디지몬의 진화원에서 특징으로 「{tk}」{J(tk, "을", "를")} 가진 카드 1장을 코스트를 지불하지 않고 {end("등장시킨다", "등장시킬 수 있다")}{(m.Groups["noplay"].Success ? ". 이 효과로 등장한 디지몬의 【등장 시】 효과는 발휘하지 않는다" : "")}";
        }
        if (Regex.IsMatch(a, @"^return your opponent's top security card to the bottom of (?:the|their) deck$", IC))
            return $"상대의 시큐리티 맨 위의 카드를 덱 아래로 {end("되돌린다", "되돌릴 수 있다")}";
        return null;
    }
}
