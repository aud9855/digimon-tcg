using System;
using System.Linq;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.47.0 미지원 문장 21차 묶음의 생성 한글 — 원래 규칙(20차까지)이 옮기지 못한 절만 (SentenceParser.Batch47과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth47;

    /// <summary>영어 데이터에서 잘린 문장 → 공식 영어 카드 리스트 구조로 고친 문장의 한글 (깨진 복사본은 옮기지 않는다 — 메인 칸 문장이 같은 효과)</summary>
    private static string? RenderClause47(string rawClause)
    {
        if (_clauseDepth47 > 0) return null;
        string t = rawClause.Trim();
        // [육성]【자신의 메인 페이즈 개시 시】 디지타마 덱 (BT13-007 · BT22-007 — 공식 문구 구조)
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^\[Breeding\]\s*\[Start of Your Main Phase\]\s*Reveal the top card of your Digi-Egg deck, then place that card and all of your \[(?<t>[^\]]+)\] trait Digimon as this Digimon's bottom digivolution cards\.?$", IC);
        if (m.Success)
        {
            string tk = TraitKo(m.Groups["t"].Value);
            return $"[육성] 【자신의 메인 페이즈 개시 시】 자신의 디지타마 덱 위에서부터 1장 오픈하고, 그 카드와 특징으로 「{tk}」{J(tk, "을", "를")} 가진 자신의 디지몬 전부를 이 디지몬의 진화원 아래에 놓는다.";
        }
        m = System.Text.RegularExpressions.Regex.Match(t, @"^\[Breeding\]\s*\[Start of Your Main Phase\]\s*Look at your Digi-Egg deck's top card\. Among them, you may place \[(?<n>[^\]]+)\]s? as this Digimon's top digivolution cards\. Then, if this Digimon has (?<k>\d+) or more digivolution cards, you may play (?<p>\d+) \[\k<n>\]s? from its digivolution cards without paying the costs?\.?$", IC);
        if (m.Success)
        {
            string nk = NameKo(m.Groups["n"].Value);
            return $"[육성] 【자신의 메인 페이즈 개시 시】 자신의 디지타마 덱을 위에서부터 1장 확인한다. 그중 「{nk}」{J(nk, "을", "를")} 이 디지몬의 진화원 위에 놓을 수 있다. 그 후, 이 디지몬의 진화원이 {m.Groups["k"].Value}장 이상이라면, 이 디지몬의 진화원에서 「{nk}」 {m.Groups["p"].Value}장을 코스트를 지불하지 않고 등장시킬 수 있다.";
        }
        var fix = SentenceParser.CardTextFixes47.FirstOrDefault(f => f.Broken == t && f.Fixed != null);
        string? from = fix.Fixed ?? (SentenceParser.DuplicateKoFrom47.TryGetValue(t, out var full) ? full : null);
        if (from == null) return null;
        _clauseDepth47++;
        try { return RenderClause(from); }
        finally { _clauseDepth47--; }
    }

    /// <summary>잘린 문장 표(CardTextFixes47)의 원문과 정확히 같은 절은 원래 규칙보다 먼저 고친 문장으로 옮긴다 (BT26-057 — 원래 규칙이 잘린 문장을 그대로 옮기던 것)</summary>
    private static string? RenderClause47Fixed(string rawClause)
    {
        string t = rawClause.Trim();
        return SentenceParser.CardTextFixes47.Any(f => f.Fixed != null && f.Broken == t) ? RenderClause47(rawClause) : null;
    }
}
