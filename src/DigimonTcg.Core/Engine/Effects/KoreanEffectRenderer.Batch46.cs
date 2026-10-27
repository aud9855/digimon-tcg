using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.46.0 미지원 문장 20차 묶음의 생성 한글 — 원래 규칙(19차까지)이 옮기지 못한 절 · 문장만 (SentenceParser.Batch46과 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth46;
    [ThreadStatic] private static int _fallbackDepth46;

    /// <summary>20차 한글 규칙을 켜는 문 — 원래 규칙(19차까지)이 옮기지 못한 절을 다시 옮길 때만 (수정 전 한글은 그대로)</summary>
    [ThreadStatic] private static int _koGate46;
    private static bool KoGate46 => _koGate46 > 0;

    private static string? RenderUpTo45(string alt) => RenderUpTo44(alt) ?? RenderClause45(alt);

    // =====================================================================
    // 1. 절 (RenderClause 끝)
    // =====================================================================

    private static string? RenderClause46(string rawClause)
    {
        if (_clauseDepth46 > 0) return null;
        _clauseDepth46++;
        _koGate46++;
        try
        {
            if (RenderUpTo45(rawClause) is { } again) return again;   // 20차 문장 · 대상 · 셈 · 조건 · 비용 한글로 다시
            string clause = Regex.Replace(SentenceParser.NormalizeForRender(rawClause), @"^(?:Inherited|Security) Effect\s*", "", IC).Trim();
            if (ClauseKo46(clause, rawClause) is { } k) return k;
            foreach (var alt in SentenceParser.ClauseRewrites46(rawClause))
                if (alt != rawClause && RenderUpTo45(alt) is { } ko)
                    return ko;
            return null;
        }
        finally { _clauseDepth46--; _koGate46--; }
    }

    // =====================================================================
    // 2. 문장 (Sentence 끝)
    // =====================================================================

    private static string? SentenceFallback46(string s, bool nested)
    {
        if (_fallbackDepth46 >= 3 || !KoGate46) return null;
        _fallbackDepth46++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (SentenceKo46(s, nested) is { } k) return k;
            foreach (var alt in SentenceParser.Rewrites46(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return ko;
            return null;
        }
        finally { _fallbackDepth46--; }
    }
}
