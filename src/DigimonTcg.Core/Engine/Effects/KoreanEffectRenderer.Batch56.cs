using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.56.0 미지원 문장 30차 묶음의 생성 한글 — 원래 규칙(29차까지)이 옮기지 못한 절만 (SentenceParser.Batch56와 같은 표기 바꾸기)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth56;

    private static string? RenderClause56(string rawClause)
    {
        if (_clauseDepth56 > 0) return null;
        _clauseDepth56++;
        try
        {
            string t = Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", IC);
            if (SentenceParser.SplitTwoEffects56(t) is { } two)
                return (RenderClause(two.First) ?? PartKo56(two.First)) is { } a && (RenderClause(two.Second) ?? PartKo56(two.Second)) is { } b ? a + " " + b : null;
            string alt = SentenceParser.ClauseRewrite56(t);
            if (alt == t || RenderClause(alt) is not { } ko) return null;
            // RB1-035 "Lv.3이라면" (읽을 때는 "Lv.3 이하" — 등장한 디지몬은 Lv.3 이상)
            if (Regex.IsMatch(t, @"＜Draw \d+＞ if it is level 3\.$", IC)) ko = Regex.Replace(ko, @"Lv\.3 이하(?:인|라면)", "Lv.3이라면");
            return ko;
        }
        finally { _clauseDepth56--; }
    }

    /// <summary>나눈 절 중 원래 규칙이 옮기지 못한 것</summary>
    private static string? PartKo56(string part) =>
        Regex.IsMatch(part, @"^\[All Turns\]\s*This Digimon gains all colors in its digivolution cards\.?$", IC) ? "【서로의 턴】 이 디지몬은 진화원의 카드가 가진 색을 전부 얻는다." : null;
}
