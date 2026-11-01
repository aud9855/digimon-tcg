using System;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.67.0 미지원 문장 41차 묶음의 생성 한글 — 원래 규칙(40차까지)이 옮기지 못한 절만 (SentenceParser.Batch67과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth67;

    private const string Placeholder67 = "메모리를 +1 한다";

    /// <summary>앞의 규칙이 (지원하지 않던 절을) 어색하게 옮기던 것 — 이 묶음에서 지원하는 절만 먼저 (BT20-096 · BT8-043)</summary>
    private static string? RenderFixed67(string rawClause)
    {
        if (_koDepth67 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        Match m;
        // BT20-096 [트래시] "~ 코스트 지불하는 것으로, 이 카드를 덱 아래로 되돌리고, ~ 소멸시킨다." (트래시의 이 카드)
        if ((m = Regex.Match(t, @"^(?<pre>\[Trash\].*?\bby paying (?<n>\d+) cost), return this card to the bottom of the deck and (?<rest>delete [^.]+)\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string? ko = Older67($"{m.Groups["pre"].Value}, {m.Groups["rest"].Value}.");
            string from = $"메모리를 -{m.Groups["n"].Value} 하는 것으로, ";
            if (ko == null || !ko.Contains(from, StringComparison.Ordinal)) return null;
            return ko.Replace(from, $"{m.Groups["n"].Value} 코스트 지불하는 것으로, 이 카드를 덱 아래로 되돌리고, ");
        }
        // BT8-043 "패의 이 카드를 등장시킬 때, 퍼플인 자신의 「케루비몬」 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -8 한다."
        if ((m = Regex.Match(t, @"^When you would play this card from your hand, you may delete 1 of your (?<c>red|blue|yellow|green|black|purple|white) (?<n>\[[^\]]+\]) to reduce this card's play cost by (?<r>\d+)\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string col = SentenceParser.ColorKo66(Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true));
            return $"패의 이 카드를 등장시킬 때, {col}인 자신의 {NamesKo(m.Groups["n"].Value)} 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -{m.Groups["r"].Value} 한다.";
        }
        return null;
    }

    private static string? RenderClause67(string rawClause)
    {
        if (_koDepth67 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        string alt = SentenceParser.ClauseRewrite67(t);
        if (alt == t) return null;
        // BT9-079 "다른 자신의 디지몬 1마리를 자신의 트래시의 ~ 1장으로 코스트를 지불하지 않고 진화시킬 수 있다."
        Match bm;
        if ((bm = Regex.Match(alt, @"^(?<lab>(?:\[[^\]]+\]\s*)*)1 of your other Digimon may digivolve into (?<p>an? [^.]+?) from your trash without paying the cost\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string? pre = LabelsKo66(bm.Groups["lab"].Value);
            string? p = PickKo(Regex.Replace(bm.Groups["p"].Value, @"^an? ", "1 ", RegexOptions.IgnoreCase));
            if (pre == null || p == null) return null;
            return $"{pre}다른 자신의 디지몬 1마리를 자신의 트래시의 {p}{J(p, "으로", "로")} 코스트를 지불하지 않고 진화시킬 수 있다.";
        }
        if (!alt.Contains('⟦')) return Older67(alt);   // 같은 뜻 다른 표기 (ST9-08)
        // 표시 하나하나를 "메모리 +1"로 바꿔 앞의 규칙으로 옮긴 뒤, 그 자리에 이 묶음의 한글을 넣는다
        var marks = Regex.Matches(alt, @"⟦(?<k>[a-z]+)67(?::(?<id>\d+))?⟧");
        if (marks.Count != 1) return null;
        var mk = marks[0];
        string? body = MarkKo67(mk.Groups["k"].Value, mk.Groups["id"].Success ? int.Parse(mk.Groups["id"].Value) : 0);
        if (body == null) return null;
        string before = alt[..mk.Index];
        bool lower = Regex.IsMatch(before, @",\s*$");
        string eng = before + (lower ? "gain 1 memory" : "Gain 1 memory") + alt[(mk.Index + mk.Length)..];
        string? ko = Older67(eng);
        if (ko == null) return null;
        int at = ko.IndexOf(Placeholder67, StringComparison.Ordinal);
        if (at < 0 || ko.IndexOf(Placeholder67, at + 1, StringComparison.Ordinal) >= 0) return null;
        return ko[..at] + body + ko[(at + Placeholder67.Length)..];
    }

    /// <summary>표시의 한글 (끝의 마침표 없이 — "~한다" · "~할 수 있다")</summary>
    private static string? MarkKo67(string kind, int id)
    {
        if (kind == "handcolors") return "자신의 패 1장을 파기하는 것으로, 이 턴 동안 이 디지몬의 색은 파기한 카드의 색으로도 취급한다";
        if (kind == "selfbottom") return "이 카드를 덱 아래로 되돌린다";
        if (SentenceParser.Spec67(id) is not { } v) return null;
        switch (kind)
        {
            case "chg":
            {
                string name = NamesKo($"[{v[0]}]");
                string col = SentenceParser.ColorKo66(Enum.Parse<CardColor>(v[1], ignoreCase: true));
                string until = v[3] == "own" ? "자신의 턴 종료까지" : "상대의 턴 종료까지";
                return $"{until} 상대의 디지몬 1마리를 원래 명칭 {name}・{col}・DP {v[2]}으로 변경{(v[4] == "may" ? "할 수 있다" : "한다")}";
            }
            case "revdiffcol":
            {
                string? p = PickKo(v[2]);
                if (p == null || !p.EndsWith(" 1장", StringComparison.Ordinal)) return null;
                return $"자신의 덱 위에서부터 {v[0]}장 오픈한다. 그중 색이 서로 다른 {p[..^3]} {v[1]}장까지를 패에 추가한다. 나머지는 원하는 순서대로 덱 아래로 되돌린다";
            }
            case "namedevo":
                return $"자신의 Lv.{v[0]} 디지몬 1마리를 고른다. 그 디지몬은 자신의 패에서, 고른 디지몬의 명칭을 포함하고 명칭이 서로 다른 Lv.{v[1]}의 디지몬 카드로 진화 조건을 무시하고 코스트를 지불하지 않고 진화할 수 있다";
            case "revevothat":
            {
                string? p = PickKo(v[1]);
                if (p == null) return null;
                return $"자신의 덱 위에서부터 {v[0]}장 오픈한다. 그중 {p}{J(p, "으로", "로")}, 어택한 디지몬을 코스트를 지불하지 않고 진화시킬 수 있다. 나머지는 원하는 순서대로 덱 아래로 되돌린다";
            }
            case "revtop":
            {
                string col = SentenceParser.ColorKo66(Enum.Parse<CardColor>(v[0], ignoreCase: true));
                return $"자신의 덱 위에서부터 1장 오픈한다. 그 카드가 {col}인 등장 코스트 {v[1]} 이하의 디지몬 카드라면 코스트를 지불하지 않고 등장시킬 수 있다. 등장시키지 않은 카드는 패에 추가한다";
            }
        }
        return null;
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older67(string english)
    {
        _koDepth67++;
        try { return RenderClause(english); }
        finally { _koDepth67--; }
    }
}
