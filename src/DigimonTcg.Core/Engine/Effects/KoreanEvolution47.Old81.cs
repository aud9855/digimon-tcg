using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.81.0 공식 한글의 옛 표기 특수 진화 조건 "진화:「X」에서 N" · "진화 : 『특징으로 「Y」를 가진 Lv.2』에서 0" (BT8 ~ BT10 · P-072 등 60장).
/// 지금 표기 "〔진화〕X : 코스트 N"과 같은 조건이다 (종합 룰 2-3-5-3 — 카드에 적힌 진화 조건). 공식 영어 카드 리스트의 [Special Digivolution Condition]과 모두 같다
/// ("[Digivolve] 0 from [Agumon]" · "[Digivolve] 5 from Lv.6 w/[Royal Knight] in traits" — scratchpad 대조).
/// 〔진화〕 표기가 없는 문구에서만 바꾼다 (〔진화〕가 있는 카드는 지금처럼).
/// </summary>
public static partial class KoreanEvolution47
{
    // 조건: 『~』 · 「명칭에 「X」를 포함」(겹친 낫표) · 「X」(/「Y」)
    private static readonly Regex OldNotation81 = new(
        @"《?진화\s*[:：]\s*(?<c>『[^』]+』|「(?:[^「」]|「[^」]*」)+」(?:\s*/\s*「[^」]+」)*)\s*에서\s*(?<n>\d+)》?",
        RegexOptions.Compiled);

    /// <summary>옛 표기를 〔진화〕 표기로 (옛 표기가 없거나 〔진화〕가 이미 있으면 그대로)</summary>
    internal static string OldNotationToNew81(string text)
    {
        if (text.Contains("〔진화〕", StringComparison.Ordinal) || !OldNotation81.IsMatch(text)) return text;
        return OldNotation81.Replace(text, m =>
        {
            string c = m.Groups["c"].Value.Trim();
            if (c.StartsWith('『')) c = c[1..^1];
            // 「명칭에 「X」를 포함」 — 바깥 낫표는 조건을 묶은 것 (안쪽에 낫표가 있을 때만)
            else if (c.StartsWith('「') && c.EndsWith('」') && c.IndexOf('「', 1) is > 0 and var inner && inner < c.Length - 1 && !c.Contains("」/", StringComparison.Ordinal)
                     && Regex.IsMatch(c, @"^「(?:명칭|특징|진화원)")) c = c[1..^1];
            return $"〔진화〕{c} : 코스트 {m.Groups["n"].Value}\n";
        });
    }
}
