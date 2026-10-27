using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.44.0 미지원 문장 18차 묶음의 생성 한글 — 원래 규칙(17차까지)이 옮기지 못한 절 · 문장만 (SentenceParser.Batch44와 같은 읽기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth44;
    [ThreadStatic] private static int _fallbackDepth44;

    /// <summary>18차 한글 규칙을 켜는 문 — 원래 규칙(17차까지)이 옮기지 못한 절을 다시 옮길 때만 (수정 전 한글은 그대로)</summary>
    [ThreadStatic] private static int _koGate44;
    private static bool KoGate44 => _koGate44 > 0;

    private static string? RenderUpTo43(string alt) => RenderClauseCore(alt) ?? RenderClause40(alt) ?? RenderClause41(alt) ?? RenderClause42(alt) ?? RenderClause43(alt);

    // =====================================================================
    // 1. 절 (RenderClause 끝)
    // =====================================================================

    private static string? RenderClause44(string rawClause)
    {
        if (_clauseDepth44 > 0) return null;
        _clauseDepth44++;
        _koGate44++;
        try
        {
            if (RenderUpTo43(rawClause) is { } again) return again;   // 18차 문장 · 대상 · 셈 · 조건 · 비용 한글로 다시
            string clause = Regex.Replace(SentenceParser.NormalizeForRender(rawClause), @"^(?:Inherited|Security) Effect\s*", "", IC).Trim();
            if (ClauseKo44(clause, rawClause) is { } k) return k;
            foreach (var alt in SentenceParser.ClauseRewrites44(rawClause))
                if (alt != rawClause && RenderUpTo43(alt) is { } ko)
                    return ko;
            return null;
        }
        finally { _clauseDepth44--; _koGate44--; }
    }

    // =====================================================================
    // 2. 문장 (Sentence 끝)
    // =====================================================================

    private static string? SentenceFallback44(string s, bool nested)
    {
        if (_fallbackDepth44 >= 3 || !KoGate44) return null;
        _fallbackDepth44++;
        try
        {
            s = s.Trim().TrimEnd('.').Trim();
            if (SentenceKo44(s, nested) is { } k) return k;
            foreach (var alt in SentenceParser.Rewrites44(s))
                if (alt != s && Sentence(alt, nested) is { } ko) return ko;
            return null;
        }
        finally { _fallbackDepth44--; }
    }
}
