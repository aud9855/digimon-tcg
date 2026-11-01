using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.60.0 미지원 문장 34차 묶음의 생성 한글 — 원래 규칙(33차까지)이 옮기지 못한 절만 (SentenceParser.Batch60와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth60;
    [ThreadStatic] private static int _koGate60;
    private static bool KoGate60 => _koGate60 > 0;

    private static readonly Regex ClauseSpecialKoRe60 = new(
        $"{SentenceParser.SourcelessStaticRe60}|{SentenceParser.NegateSecurityRe60}|{SentenceParser.LeaveLockRe60}|{SentenceParser.EnemyOptionSecurityRe60}|{SentenceParser.LevelVsSecuritySaRe60}|{SentenceParser.CollisionByDpRe60}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? RenderClause60(string rawClause)
    {
        if (_clauseDepth60 > 0) return null;
        string t = Regex.Replace(rawClause.Replace(' ', ' ').Replace('’', '\'').Trim(), @"^Inherited Effect\s*", "", IC);
        string alt = SentenceParser.ClauseRewrite60(t);
        bool special = ClauseSpecialKoRe60.IsMatch(t);
        // 34차 표현이 없으면 다시 옮기지 않는다 (같은 결과 — 시간만 든다)
        if (!special && alt == t && !SentenceParser.Hint60.IsMatch(t)) return null;
        _clauseDepth60++;
        _koGate60++;
        try
        {
            if (special) return ClauseSpecialKo60(t);
            return RenderClause(alt);
        }
        finally { _clauseDepth60--; _koGate60--; }
    }

    /// <summary>"While this Digimon is [X], " → "이 디지몬이 「X」인 동안, "</summary>
    private static string WhileNamedKo60(Match m)
    {
        if (!m.Groups["x"].Success) return "";
        string x = NamesKo(m.Groups["x"].Value);
        return $"이 디지몬이 {x}인 동안, ";
    }

    /// <summary>절 하나로 옮기는 것 (SentenceParser.ClauseSpecial60)</summary>
    private static string? ClauseSpecialKo60(string t)
    {
        Match m;
        if ((m = SentenceParser.SourcelessStaticRe60.Match(t)).Success)
        {
            if (SentenceParser.SourcelessPreds60(m.Groups["p"].Value) is not { } kws) return null;
            var parts = new List<string>();
            for (int i = 0; i < kws.Count; i++)
            {
                bool last = i == kws.Count - 1;
                if (kws[i] == Keyword.CannotSuspend) { parts.Add(last ? "레스트할 수 없다" : "레스트할 수 없고"); continue; }
                if (PredKo34(new SentenceParser.Pred34("", new[] { kws[i] }, null, 0, null, true)) is not { Style: 't' } p) return null;
                parts.Add(last ? p.Plain : p.And);
            }
            return $"{LabelsKo57(m.Groups["q"].Value)}진화원이 없는 이 디지몬은 {string.Join(", ", parts)}.";
        }
        if ((m = SentenceParser.NegateSecurityRe60.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}{WhileNamedKo60(m)}이 디지몬이 체크한 카드의 【시큐리티】 효과는 발휘하지 않는다.";
        if ((m = SentenceParser.LeaveLockRe60.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}{WhileNamedKo60(m)}이 디지몬은 자신의 효과와 소멸 이외로는 배틀 에어리어를 떠나지 않는다.";
        if ((m = SentenceParser.EnemyOptionSecurityRe60.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}상대의 옵션 카드의 【시큐리티】 효과는 전부 발휘하지 않는다.";
        if ((m = SentenceParser.LevelVsSecuritySaRe60.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}Lv.이 서로의 시큐리티 합계 장수 이상인 상대의 디지몬 전부는 《시큐리티 어택 {m.Groups["sign"].Value}{m.Groups["n"].Value}》 (이 디지몬이 체크하는 매수 {m.Groups["sign"].Value}{m.Groups["n"].Value})을 얻는다.";
        if ((m = SentenceParser.CollisionByDpRe60.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}이 디지몬 이하의 DP를 가진 디지몬 전부는 《충돌》 (이 디지몬이 어택하는 동안, 상대의 디지몬 전부는 《블로커》를 얻고, 가능하면 블록해야 한다)을 얻는다.";
        return null;
    }

    /// <summary>문장 (SentenceParser.ParseAction60과 같은 표)</summary>
    private static string? SentenceFallback60(string s, bool nested)
    {
        if (!KoGate60) return null;
        s = s.Trim().TrimEnd('.').Trim();
        string pre = "";
        var then = Regex.Match(s, @"^Then,?\s+(?<r>.+)$", IC);
        if (then.Success) { pre = "그 후, "; s = then.Groups["r"].Value; }
        // EX3-073
        if (Regex.IsMatch(s, @"^none of your opponent's \[Security\] effects can activate for the turn$", IC))
            return $"{pre}이 턴 동안, 상대의 【시큐리티】 효과는 발휘할 수 없다.";
        // BT16-051
        if (Regex.IsMatch(s, @"^(?:until the end of your opponent's turn, )?this Digimon can't leave the battle area other than by deletion(?: until the end of your opponent's turn)?$", IC))
            return $"{pre}상대의 턴 종료 시까지, 이 디지몬은 소멸 이외로는 배틀 에어리어를 떠나지 않는다.";
        // BT3-097 (바꿔 쓴 것)
        if (Regex.IsMatch(s, @"^1 of your Digimon gains ⟦nso60⟧ for the turn$", IC))
            return $"{pre}이 턴 동안, 자신의 디지몬 1마리는 「이 디지몬이 체크한 옵션 카드의 【시큐리티】 효과는 발휘하지 않는다」를 얻는다.";
        return null;
    }
}
