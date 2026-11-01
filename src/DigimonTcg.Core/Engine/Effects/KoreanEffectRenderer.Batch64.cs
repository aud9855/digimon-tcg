using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.64.0 미지원 문장 38차 묶음의 생성 한글 — 원래 규칙(37차까지)이 옮기지 못한 절만 (SentenceParser.Batch64와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth64;

    private static string? RenderClause64(string rawClause)
    {
        if (_koDepth64 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        Match m;
        // 턴 종료 조건 (BT14-081 · BT17-069 진화원)
        if ((m = SentenceParser.TurnEndRe64.Match(t)).Success)
        {
            string head = "【자신의 턴】 ";
            if (m.Groups["cond"].Success)
            {
                var nm = Regex.Match(m.Groups["cond"].Value, @"^this Digimon has (?<n>\[[^\]]+\]) in its name$", RegexOptions.IgnoreCase);
                if (!nm.Success) return null;
                string n = NamesKo(nm.Groups["n"].Value);
                head += $"이 디지몬의 명칭에 {n}{J(n, "이", "가")} 포함되는 동안, ";
            }
            return head + $"자신의 턴 종료 조건은 메모리가 상대 쪽의 {m.Groups["n"].Value} 이상이 되는 것이다.";
        }
        // BT14-081 "~ If [Eiji Nagasumi] is in this Digimon's digivolution cards, increase the number of Digimon you can play with this effect by 2."
        if ((m = SentenceParser.PlayCountUpRe64.Match(t)).Success)
        {
            string? ko = Older64(t[..m.Index] + $"You may play 1 {m.Groups["p"].Value} card {m.Groups["rest"].Value}.");
            if (ko == null) return null;
            string x = NamesKo(m.Groups["x"].Value);
            return ko.TrimEnd() + $" 이 디지몬의 진화원에 {x}{J(x, "이", "가")} 있다면, 이 효과로 등장시킬 수 있는 디지몬의 수 +{m.Groups["k"].Value}.";
        }
        var labels = Regex.Match(t, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        string lab = labels.Groups[1].Value.Trim(), body = labels.Groups[2].Value.Trim();
        // EX13-077 고르는 효과 머리말 — "그 후, 자신의 디지몬과 테이머의 색 2색마다, 아래의 효과 중 1개를 발휘한다"
        if ((m = SentenceParser.ForEveryChooseRe64.Match(body)).Success
            && Regex.Match(m.Groups["cnt"].Value, @"^(?:(?<n>\d+) of )?your Digimon and Tamers' colou?rs$", RegexOptions.IgnoreCase) is { Success: true } cm)
        {
            string? pre = Older64($"{lab} {m.Groups["pre"].Value}.");
            if (pre == null) return null;
            string n = cm.Groups["n"].Success ? cm.Groups["n"].Value : "1";
            return pre.TrimEnd() + $" 그 후, 자신의 디지몬과 테이머의 색 {n}색마다, 아래의 효과 중 1개를 발휘한다.";
        }
        // EX4-073 머리말 — "~ 파기하는 것으로, 파기한 카드 1장마다 아래의 효과를 발휘한다. 그 후, 3장 파기했다면 ~"
        if ((m = SentenceParser.ForEachTrashedBelowRe64.Match(body)).Success)
        {
            string? pre = Older64($"{lab} {m.Groups["cost"].Value}, gain 1 memory.");
            const string gain = "메모리를 +1 한다.";
            if (pre == null || !pre.EndsWith(gain, StringComparison.Ordinal)) return null;
            string ko = pre[..^gain.Length] + "파기한 카드 1장마다 아래의 효과를 발휘한다.";
            if (m.Groups["post"].Success)
            {
                var pm = Regex.Match(m.Groups["post"].Value, @"^Then, if you trashed (?<n>\d+) cards, (?<x>.+)$", RegexOptions.IgnoreCase);
                if (!pm.Success) return null;
                string x = pm.Groups["x"].Value;
                string? xko = Older64($"{lab} {char.ToUpperInvariant(x[0])}{x[1..]}");
                if (xko == null) return null;
                ko += $" 그 후, {pm.Groups["n"].Value}장 파기했다면 {Regex.Replace(xko, @"^(?:【[^】]+】\s*)+", "")}";
            }
            return ko;
        }
        string alt = SentenceParser.ClauseRewrite64(t);
        if (alt == t) return null;
        string en = alt;
        var repl = new List<(string From, string To)>();
        string suffix = "";
        Match mm;
        if (en.EndsWith(" Then, ⟦switchplayed64⟧.", StringComparison.Ordinal))
        {
            en = en[..^" Then, ⟦switchplayed64⟧.".Length];
            suffix = " 등장시켰다면, 어택의 대상을 이 효과로 등장한 디지몬으로 변경할 수 있다.";
        }
        if ((mm = Regex.Match(en, @" ⟦retcolor64:(?<a>\d+):(?<b>\d+)⟧\.$")).Success)
        {
            en = en[..mm.Index] + $" this Digimon gets +{mm.Groups["a"].Value} DP for the turn.";
            repl.Add(($"이 디지몬의 DP를 +{mm.Groups["a"].Value} 한다.", $"이 디지몬의 DP를 +{mm.Groups["a"].Value} 하고, 상대의 디지몬 1마리를 되돌린 카드의 색 1색마다 DP -{mm.Groups["b"].Value} 한다."));
        }
        if ((mm = Regex.Match(en, @", ⟦oppcostdown64:(?<n>\d+)⟧\.$")).Success)
        {
            en = en[..mm.Index] + ", ＜Draw 1＞.";
            repl.Add(("《1 드로우》 (자신의 덱에서 카드를 1장 드로우한다)", $"상대의 턴 종료 시까지, 상대의 디지몬 1마리를 파기한 카드 1장마다 등장 코스트 -{mm.Groups["n"].Value} 한다"));
        }
        if (en.Contains('⟦')) return null;
        string? r = Older64(en);
        if (r == null) return null;
        r = r.TrimEnd() + suffix;
        foreach (var (from, to) in repl)
        {
            if (!r.Contains(from)) return null;
            r = r.Replace(from, to);
        }
        return r;
    }

    /// <summary>
    /// 원래 규칙이 읽지 못했지만 한글은 있던 절 중 이 묶음이 읽게 된 것 — 읽은 대로 옮긴다 (렌더러 맨 앞, 이 묶음의 정규식에 맞는 절만):
    /// EX8-070 "그 디지몬(비용으로 진화원을 버린 디지몬)은 ~을 얻고, 상대의 효과로 패/덱으로 되돌아가지 않으며, DP +3000"
    /// </summary>
    private static string? RenderFixed64(string rawClause)
    {
        if (_koDepth64 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        Match m;
        if ((m = SentenceParser.KeysReturnDpRe64.Match(t)).Success)
        {
            string? ko = Older64(t[..m.Index] + $"{m.Groups["pre"].Value}until the end of your opponent's turn, that Digimon gains {m.Groups["k1"].Value}, {m.Groups["k2"].Value} and {m.Groups["k3"].Value}.");
            if (ko == null || !Regex.IsMatch(ko, @"[을를] 얻는다.$")) return null;
            string dp = m.Groups["dp"].Value.Replace(" DP", "");
            return ko[..^3] + $"고, 상대의 효과로 패/덱으로 되돌아가지 않으며, DP {dp} 한다.";
        }
        return null;
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older64(string english)
    {
        _koDepth64++;
        try { return RenderClause(english); }
        finally { _koDepth64--; }
    }
}
