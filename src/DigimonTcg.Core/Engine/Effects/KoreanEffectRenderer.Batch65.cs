using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.65.0 미지원 문장 39차 묶음의 생성 한글 — 원래 규칙(38차까지)이 옮기지 못한 절만 (SentenceParser.Batch65와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth65;

    private static string? RenderClause65(string rawClause)
    {
        if (_koDepth65 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        var labels = Regex.Match(t, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        string lab = labels.Groups[1].Value.Trim();
        Match m;
        // ST13-02 · ST13-09 "~ 놓는 것으로, 자신의 덱 위에서부터 1장 오픈할 수 있다. 그 카드가 ~ 디지몬 카드라면, 코스트를 지불하지 않고 등장시킬 수 있다. 나머지는 패에 추가한다."
        if ((m = SentenceParser.RevealMayPlayRe65.Match(t)).Success)
        {
            const string gain = "메모리를 +1 한다.";
            string? pre = Older65(t[..m.Index] + $"{m.Groups["pre"].Value}, gain 1 memory.");
            string? rv = Older65($"{lab} Reveal the top card of your deck. If that card is {m.Groups["pick"].Value}, play it without paying its memory cost. Add the rest to your hand.");
            if (pre == null || rv == null || !pre.EndsWith(gain, StringComparison.Ordinal)) return null;
            rv = Regex.Replace(rv, @"^(?:【[^】]+】\s*)+", "");
            const string open = "1장 오픈한다.", play = "등장시킨다.";
            if (!rv.Contains(open) || !rv.Contains(play)) return null;
            rv = rv.Replace(open, "1장 오픈할 수 있다.").Replace(play, "등장시킬 수 있다.");
            return pre[..^gain.Length] + rv;
        }
        // BT9-103 "~ 플레이어에게 어택할 수 없고, 상대의 효과로 시큐리티에 카드를 추가할 수 없다."
        if ((m = SentenceParser.NoSecAddRe65.Match(t)).Success)
        {
            string? a = Older65(t[..m.Index] + m.Groups["a"].Value + ".");
            const string end = "없다.";
            if (a == null || !a.EndsWith(end, StringComparison.Ordinal)) return null;
            return a[..^end.Length] + "없고, 상대의 효과로 시큐리티에 카드를 추가할 수 없다.";
        }
        // P-116 "자신의 「아구몬」, 「펄스몬」, 「감마몬」이 있는 동안, 이 카드의 사용 코스트는 0이 된다."
        if ((m = SentenceParser.CostsZeroRe65.Match(t)).Success)
        {
            var names = Regex.Matches(m.Groups["x"].Value, @"\[[^\]]+\]");
            var ko = new System.Collections.Generic.List<string>();
            foreach (Match n in names) ko.Add(NamesKo(n.Value));
            string all = string.Join(", ", ko);
            return $"자신의 필드에 {all}{J(all, "이", "가")} 모두 있는 동안, 이 카드의 사용 코스트는 0이 된다.";
        }
        // BT13-092 "~ 되돌리는 것으로, 그 카드와 같은 명칭을 가진 상대의 디지몬을 전부 소멸시킨다."
        if ((m = SentenceParser.SameNameDeleteRe65.Match(t)).Success)
        {
            const string gain = "메모리를 +1 한다.";
            string? pre = Older65(t[..m.Index] + $"{m.Groups["pre"].Value}, gain 1 memory.");
            if (pre == null || !pre.EndsWith(gain, StringComparison.Ordinal)) return null;
            return pre[..^gain.Length] + "그 카드와 같은 명칭을 가진 상대의 디지몬을 전부 소멸시킨다.";
        }
        // 그 밖의 같은 뜻 다른 표기 (BT6-078 "place it under ~ at the bottom of its digivolution cards")
        string alt = SentenceParser.ClauseRewrite65(t);
        if (alt != t && !alt.Contains('⟦')) return Older65(alt);
        return null;
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older65(string english)
    {
        _koDepth65++;
        try { return RenderClause(english); }
        finally { _koDepth65--; }
    }
}
