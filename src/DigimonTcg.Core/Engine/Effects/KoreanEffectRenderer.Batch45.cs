using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.45.0 미지원 문장 19차 묶음의 생성 한글 — 원래 규칙(18차까지)이 옮기지 못한 절 · 문장만 (SentenceParser.Batch45와 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth45;
    [ThreadStatic] private static int _fallbackDepth45;

    /// <summary>19차 한글 규칙을 켜는 문 — 원래 규칙(18차까지)이 옮기지 못한 절을 다시 옮길 때만 (수정 전 한글은 그대로)</summary>
    [ThreadStatic] private static int _koGate45;
    private static bool KoGate45 => _koGate45 > 0;

    private static string? RenderUpTo44(string alt) => RenderUpTo43(alt) ?? RenderClause44(alt);

    // =====================================================================
    // 1. 절 (RenderClause 끝)
    // =====================================================================

    private static string? RenderClause45(string rawClause)
    {
        if (_clauseDepth45 > 0) return null;
        _clauseDepth45++;
        _koGate45++;
        try
        {
            if (RenderUpTo44(rawClause) is { } again) return again;   // 19차 문장 · 대상 · 셈 · 조건 · 비용 한글로 다시
            string clause = Regex.Replace(SentenceParser.NormalizeForRender(rawClause), @"^(?:Inherited|Security) Effect\s*", "", IC).Trim();
            if (ClauseKo45(clause, rawClause) is { } k) return k;
            foreach (var alt in SentenceParser.ClauseRewrites45(rawClause))
                if (alt != rawClause && RenderUpTo44(alt) is { } ko)
                    return ko;
            return null;
        }
        finally { _clauseDepth45--; _koGate45--; }
    }

    // =====================================================================
    // 2. 문장 (Sentence 끝)
    // =====================================================================

    private static string? SentenceFallback45(string s, bool nested)
    {
        if (_fallbackDepth45 >= 3 || !KoGate45) return null;
        _fallbackDepth45++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (SentenceKo45(s, nested) is { } k) return k;
            foreach (var alt in SentenceParser.Rewrites45(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return ko;
            return null;
        }
        finally { _fallbackDepth45--; }
    }
}
