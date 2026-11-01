using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.61.0 미지원 문장 35차 묶음의 생성 한글 — 원래 규칙(34차까지)이 옮기지 못한 절만 (SentenceParser.Batch61와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth61;

    private static readonly Regex ClauseSpecialKoRe61 = new(
        $"{SentenceParser.SourceTrashProtectRe61}|{SentenceParser.ReplaceSourceTrashRe61}|{SentenceParser.SaHolderLockRe61}|{SentenceParser.GrantAuraRe61}|{SentenceParser.SelfGrantByDpRe61}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? RenderClause61(string rawClause)
    {
        if (_clauseDepth61 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        if (!ClauseSpecialKoRe61.IsMatch(t)) return null;
        _clauseDepth61++;
        try { return ClauseSpecialKo61(t); }
        finally { _clauseDepth61--; }
    }

    /// <summary>절 하나로 옮기는 것 (SentenceParser.ClauseSpecial61)</summary>
    private static string? ClauseSpecialKo61(string t)
    {
        Match m;
        if ((m = SentenceParser.SourceTrashProtectRe61.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}효과로 이 디지몬의 진화원의 {NamesKo(m.Groups["x"].Value)}를 파기할 수 없다.";
        if ((m = SentenceParser.ReplaceSourceTrashRe61.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}효과로 자신의 다른 디지몬의 진화원이 파기될 때, 대신 그 효과로 이 디지몬의 진화원을 파기할 수 있다.";
        if ((m = SentenceParser.SaHolderLockRe61.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}《시큐리티 어택》을 가진 상대의 디지몬은 이 디지몬에게 어택할 수 없{(m.Groups["act"].Success ? "고, 【어택 시】 효과와 【진화 시】 효과를 발휘할 수 없다" : "다")}.";
        if ((m = SentenceParser.SelfGrantByDpRe61.Match(t)).Success)
        {
            if (RenderClause(m.Groups["g"].Value.Trim()) is not { } g) return null;
            return $"{LabelsKo57(m.Groups["q"].Value)}이 디지몬의 DP +{m.Groups["n"].Value}. 이 디지몬의 DP가 {m.Groups["dp"].Value} 이상인 동안, 이 디지몬은 「{g.TrimEnd()}」의 효과를 얻는다.";
        }
        if ((m = SentenceParser.GrantAuraRe61.Match(t)).Success)
        {
            if (SentenceParser.GrantAuraTarget61(m.Groups["t"].Value) is not { } target) return null;
            if (GrantAuraTargetKo61(m.Groups["t"].Value, target.Other) is not { } who) return null;
            if (RenderClause(m.Groups["g"].Value.Trim()) is not { } g) return null;
            return $"{LabelsKo57(m.Groups["q"].Value)}{who}는 「{g.TrimEnd()}」의 효과를 얻는다.";
        }
        return null;
    }

    /// <summary>"level 3 Digimon" → "Lv.3인 디지몬 전부" · "of your opponent's Digimon" → "상대의 디지몬 전부" · "of your other Digimon with [A] or [B] in one of their traits" → "특징에 「A」/「B」를 포함하는 자신의 다른 디지몬 전부"</summary>
    private static string? GrantAuraTargetKo61(string t, bool other)
    {
        t = t.Trim();
        var tr = Regex.Match(t, @"^of your (?:other )?Digimon with (?<x>\[[^\]]+\](?:(?:, | or |, or )\[[^\]]+\])*) in (?:one of )?their traits$", IC);
        if (tr.Success) return $"특징에 {NamesKo(tr.Groups["x"].Value, trait: true)}를 포함하는 자신의 {(other ? "다른 " : "")}디지몬 전부";
        bool both = !t.StartsWith("of ", StringComparison.OrdinalIgnoreCase);
        string who = both ? "all of your " + t : "all " + Regex.Replace(t, @"^of your other ", "of your ", IC);
        if (TargetKo(who) is not { } ko) return null;
        if (both) ko = ko.Replace("자신의 ", "");
        else if (other) ko = ko.Replace("자신의 ", "자신의 다른 ");
        return ko;
    }
}
