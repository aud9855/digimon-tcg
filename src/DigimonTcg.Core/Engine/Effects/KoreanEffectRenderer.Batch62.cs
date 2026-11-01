using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.62.0 미지원 문장 36차 묶음의 생성 한글 — 원래 규칙(35차까지)이 옮기지 못한 절만 (SentenceParser.Batch62와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth62;

    private static readonly Regex ClauseSpecialKoRe62 = new(
        $"{SentenceParser.WouldUnsuspendRe62}|{SentenceParser.SaFlipRe62}|{SentenceParser.NoIgnoreReqRe62}|{SentenceParser.SideGrantRe62}|{SentenceParser.TwoGrantsRe62}|{SentenceParser.AttackingDeleteRe62}|{SentenceParser.UnsuspendedDeDigivolveRe62}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? RenderClause62(string rawClause)
    {
        if (_koDepth62 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        bool special = ClauseSpecialKoRe62.IsMatch(t);
        string alt = special ? t : SentenceParser.ClauseRewrite62(t);
        if (!special && alt == t) return null;
        if (!special)
        {
            // "While there are N or more Digimon with ~" (EX13-035 — 서로의 디지몬): "you have"로 옮기고 "자신의 디지몬이" → "디지몬이"
            var there = Regex.Match(alt, @"\bWhile there are (?<n>\d+) or more Digimon with ", RegexOptions.IgnoreCase);
            if (there.Success)
            {
                string? ko = Older62(alt[..there.Index] + "While you have " + alt[(there.Index + "While there are ".Length)..]);
                return ko == null || !ko.Contains("자신의 디지몬이 ") ? null : ko.Replace("자신의 디지몬이 ", "디지몬이 ");
            }
            return Older62(alt);
        }
        return ClauseSpecialKo62(t);
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older62(string english)
    {
        _koDepth62++;
        try { return RenderClause(english); }
        finally { _koDepth62--; }
    }

    /// <summary>절 하나로 옮기는 것 (SentenceParser.ClauseSpecial62)</summary>
    private static string? ClauseSpecialKo62(string t)
    {
        Match m;
        if ((m = SentenceParser.WouldUnsuspendRe62.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}이 디지몬이 액티브가 될 때, 자신의 패 1장을 파기하거나, 이 디지몬을 액티브로 하지 않는다.";
        if ((m = SentenceParser.UnsuspendedDeDigivolveRe62.Match(t)).Success)
        {
            string? ko = Older62($"[Opponent's Turn] [Once Per Turn] When an opponent's Digimon becomes unsuspended, ＜De-Digivolve {m.Groups["n"].Value}＞ 1 of your opponent's Digimon.");
            return ko == null || !ko.Contains("때, 상대의 디지몬 1마리를") ? null : ko.Replace("때, 상대의 디지몬 1마리를", "때, 그 디지몬 1마리를");
        }
        if (SentenceParser.AttackingDeleteRe62.IsMatch(t))
            return Older62(SentenceParser.KoRewrite62(t));
        if ((m = SentenceParser.SaFlipRe62.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}자신의 디지몬 전부의 《시큐리티 어택 -》을 《시큐리티 어택 +》로 한다.";
        if ((m = SentenceParser.NoIgnoreReqRe62.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}서로는 진화 조건을 무시할 수 없다.";
        if ((m = SentenceParser.SideGrantRe62.Match(t)).Success)
        {
            string g = SentenceParser.KoRewrite62(m.Groups["g"].Value.Trim());
            if (Older62(g) is not { } gk) return null;
            gk = gk.TrimEnd();
            string who = m.Groups["t"].Value.Trim();
            if (!m.Groups["cost"].Success && !m.Groups["kws"].Success)
                return Older62($"[Main] All of your {who} gain \"{g}\" for the turn.");
            // 비용 · 키워드는 앞의 규칙으로 옮기고 ("~ 파기하는 것으로, 이 턴 동안, ~는 《속공》 (~)을 얻는다.") 끝에 효과 문장을 붙인다
            string kws = m.Groups["kws"].Success ? m.Groups["kws"].Value : "＜Rush＞";
            string eng = m.Groups["cost"].Success
                ? $"[Main] By trashing 1 {m.Groups["cost"].Value} in your hand, all of your {who} gain {kws} for the turn."
                : $"[Main] All of your {who} gain {kws} for the turn.";
            if (Older62(eng) is not { } ko) return null;
            var tail = Regex.Match(ko, @"(?<kw>《[^》]+》(?:\s*\([^)]*\))?)(?:을|를) 얻는다\.$");
            if (!tail.Success) return null;
            string head = ko[..tail.Index];
            return m.Groups["kws"].Success
                ? $"{head}{tail.Groups["kw"].Value}과 「{gk}」의 효과를 얻는다."
                : $"{head}「{gk}」의 효과를 얻는다.";
        }
        if ((m = SentenceParser.TwoGrantsRe62.Match(t)).Success)
        {
            string a = SentenceParser.KoRewrite62(m.Groups["a"].Value.Trim()), b = SentenceParser.KoRewrite62(m.Groups["b"].Value.Trim());
            if (Older62($"[Main] {m.Groups["d"].Value}, {m.Groups["t"].Value} gains \"{a}\".") is not { } ko || Older62(b) is not { } bk) return null;
            int i = ko.LastIndexOf("」의 효과를 얻는다", StringComparison.Ordinal);
            if (i < 0) return null;
            return $"{ko[..i]}」와 「{bk.TrimEnd()}{ko[i..]}";
        }
        return null;
    }
}
