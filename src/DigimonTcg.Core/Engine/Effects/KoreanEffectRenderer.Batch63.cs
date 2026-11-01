using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.63.0 미지원 문장 37차 묶음의 생성 한글 — 원래 규칙(36차까지)이 옮기지 못한 절만 (SentenceParser.Batch63과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth63;

    private static readonly Regex SwitchSelfOrPlayerKoRe63 = new(@"\byou may (?:switch|change) the (?:target of (?:the )?attack|attack target) to this Digimon or the player\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SwitchOtherOrPlayerKoRe63 = new(@"\byou may (?:switch|change) the (?:target of (?:the )?attack|attack target) to another of your opponent's Digimon or the player\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string SuspendedSwitchEn63 = "you may switch the target of attack to 1 of your opponent's suspended Digimon";
    private const string SuspendedSwitchKo63 = "어택의 대상을 레스트 상태인 상대의 디지몬 1마리로 변경할 수 있다";

    private static string? RenderClause63(string rawClause)
    {
        if (_koDepth63 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        var labels = Regex.Match(t, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        string body = labels.Groups[2].Value.Trim();
        Match m;
        // BT5-017 진화원 — "이 디지몬이 ≪진격≫으로 어택할 때, 액티브 상태인 상대의 디지몬에게도 어택할 수 있다"
        if (SentenceParser.BlitzUnsuspendedRe63.IsMatch(t))
        {
            string? ko = Older63(labels.Groups[1].Value.Trim() + " This Digimon can also attack your opponent's unsuspended Digimon.");
            string blitz = KeywordKo("Blitz", false) ?? "《진격》";
            return ko == null || !ko.Contains("이 디지몬은 액티브") ? null : ko.Replace("이 디지몬은 액티브", $"이 디지몬이 {blitz}으로 어택할 때, 액티브");
        }
        // BT26-101 고르는 효과 머리말 "~. Then, activate 1 of the effects below:"
        if ((m = SentenceParser.ThenChooseRe63.Match(body)).Success)
        {
            string? pre = Older63(labels.Groups[1].Value.Trim() + " " + m.Groups["pre"].Value.Trim() + ".");
            return pre == null ? null : pre + " 그 후, 이하의 효과에서 1개를 골라 발휘한다.";
        }
        // EX5-054 "you may switch the target of attack to this Digimon or the player"
        if (SwitchSelfOrPlayerKoRe63.IsMatch(t))
        {
            string? ko = Older63(SwitchSelfOrPlayerKoRe63.Replace(t, "you may switch the attack target to this Digimon"));
            return ko == null || !ko.Contains("어택의 대상을 이 디지몬으로 변경할 수 있다") ? null : ko.Replace("어택의 대상을 이 디지몬으로 변경할 수 있다", "어택의 대상을 이 디지몬 또는 플레이어로 변경할 수 있다");
        }
        // ST18-14 "you may change the attack target to another of your opponent's Digimon or the player"
        if (SwitchOtherOrPlayerKoRe63.IsMatch(t))
        {
            string? ko = Older63(SwitchOtherOrPlayerKoRe63.Replace(t, SuspendedSwitchEn63));
            return ko == null || !ko.Contains(SuspendedSwitchKo63) ? null : ko.Replace(SuspendedSwitchKo63, "어택의 대상을 다른 상대의 디지몬 또는 플레이어로 변경할 수 있다");
        }
        string alt = SentenceParser.ClauseRewrite63(t);
        if (alt == t) return null;
        string en = alt;
        var repl = new System.Collections.Generic.List<(string From, string To)>();
        // BT17-101 "메모리를 상대 쪽의 3으로 할 수 있다"
        foreach (Match mm in Regex.Matches(alt, @"⟦oppmem63:(?<n>\d+)⟧"))
        {
            en = en.Replace(mm.Value, "you may gain 77 memory");
            string n = mm.Groups["n"].Value;
            repl.Add(("메모리를 +77 할 수 있다", $"메모리를 상대 쪽의 {n}{J(n, "으로", "로")} 할 수 있다"));
        }
        // BT15-078 "어택의 대상을 그 디지몬으로 변경할 수 있다"
        if (en.Contains("⟦switchthat63⟧"))
        {
            en = en.Replace("⟦switchthat63⟧", SuspendedSwitchEn63);
            repl.Add((SuspendedSwitchKo63, "어택의 대상을 그 디지몬으로 변경할 수 있다"));
        }
        // BT4-075 "상대는 액티브 상태인 본인의 디지몬 1마리를 고를 수 있다. 골랐다면, 어택의 대상을 그 디지몬으로 변경한다"
        if (en.Contains("⟦oppswitch63⟧"))
        {
            en = en.Replace("⟦oppswitch63⟧", "＜Draw 1＞");
            repl.Add(("《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다)", "상대는 액티브 상태인 본인의 디지몬 1마리를 고를 수 있다. 골랐다면, 어택의 대상을 그 디지몬으로 변경한다"));
        }
        if (en.Contains('⟦')) return null;
        string? r = Older63(en);
        if (r == null) return null;
        foreach (var (from, to) in repl)
        {
            if (!r.Contains(from)) return null;
            r = r.Replace(from, to);
        }
        return r;
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older63(string english)
    {
        _koDepth63++;
        try { return RenderClause(english); }
        finally { _koDepth63--; }
    }
}
