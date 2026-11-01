using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.66.0 미지원 문장 40차 묶음의 생성 한글 — 원래 규칙(39차까지)이 옮기지 못한 절만 (SentenceParser.Batch66과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth66;

    private static readonly Regex TrashedByRe66 = new(@"^(?<pre>When this card is trashed from your deck), if it wasn't trashed by \[(?<id>[A-Z]+\d*-\d+) (?<name>[^\]]+)\]'s effect, (?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>앞의 규칙이 (지원하지 않던 절을) 잘못 옮기던 것 — 이 묶음에서 지원하는 절만 먼저 (BT25-074 코스트를 줄여 등장 · 나머지는 파기)</summary>
    private static string? RenderFixed66(string rawClause)
    {
        if (_koDepth66 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\x27').Trim();
        Match m;
        // BT25-074 "그중 ~ 1장을 등장 코스트를 -5 하여 등장시킬 수 있다. 나머지는 파기한다."
        if ((m = Regex.Match(t, @"^(?<lab>(?:\[[^\]]+\]\s*)*)Reveal the top (?<n>\d+) cards of your deck\. You may play (?<p>1 [^.]+? Digimon card) among them with the (?:play )?cost reduced by (?<r>\d+)\. (?<rest>Trash the rest|Return the rest to the bottom of the deck)\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string? pre = LabelsKo66(m.Groups["lab"].Value);
            string? p = PickKo(m.Groups["p"].Value);
            if (pre == null || p == null) return null;
            string rest = m.Groups["rest"].Value.StartsWith("Trash", StringComparison.OrdinalIgnoreCase) ? "나머지는 파기한다." : "나머지는 덱 아래로 되돌린다.";
            return $"{pre}자신의 덱 위에서부터 {m.Groups["n"].Value}장 오픈한다. 그중 {p}{J(p, "을", "를")} 등장 코스트를 -{m.Groups["r"].Value} 하여 등장시킬 수 있다. {rest}";
        }
        return null;
    }

    private static string? RenderClause66(string rawClause)
    {
        if (_koDepth66 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        var labels = Regex.Match(t, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        string lab = labels.Groups[1].Value.Trim();
        Match m;
        // EX2-039 "이 카드가 「EX2-039 임프몬」의 효과 이외로 덱에서 파기되었을 때, ~"
        if ((m = TrashedByRe66.Match(t)).Success)
        {
            const string head = "이 카드가 덱에서 파기되었을 때, ";
            string? ko = Older66($"{m.Groups["pre"].Value}, {m.Groups["rest"].Value}");
            if (ko == null || !ko.StartsWith(head, StringComparison.Ordinal)) return null;
            string name = NamesKo($"[{m.Groups["name"].Value}]").Trim('「', '」');
            return $"이 카드가 「{m.Groups["id"].Value} {name}」의 효과 이외로 덱에서 파기되었을 때, " + ko[head.Length..];
        }
        // BT15-060 · BT17-068 공개되어 있는 동안의 명칭 · Lv.
        if ((m = RevealAlias66.ClauseRe.Match(t)).Success)
        {
            if (m.Groups["x"].Success)
            {
                string n = NamesKo($"[{m.Groups["x"].Value}]");
                return $"이 카드가 덱에서 오픈되어 있는 동안, 이 카드의 명칭은 {n}{J(n, "으로", "로")}도 취급한다.";
            }
            string lv = "Lv." + m.Groups["lv"].Value;
            return $"이 카드가 덱에서 오픈되어 있는 동안, 이 카드는 {lv}{J(lv, "으로", "로")}도 취급한다.";
        }
        string alt = SentenceParser.ClauseRewrite66(t);
        if (alt == t) return null;
        // BT9-104 "그중 ~ 1장으로 자신의 디지몬 1마리를 코스트를 지불하지 않고 진화할 수 있다. 나머지는 파기한다. (그 후, ~)"
        if ((m = Regex.Match(t, @"^(?<lab>(?:\[[^\]]+\]\s*)*)Reveal the top (?<n>\d+) cards of your deck\. You may digivolve 1 of your Digimon into an? (?<p>[^.]+?) without its memory cost\. Trash the rest\.(?:\s+(?<tail>Then, .+))?$", RegexOptions.IgnoreCase)).Success)
        {
            string? pre = LabelsKo66(m.Groups["lab"].Value);
            string? p = PickKo("1 " + m.Groups["p"].Value);
            if (pre == null || p == null) return null;
            string ko = $"{pre}자신의 덱 위에서부터 {m.Groups["n"].Value}장 오픈한다. 그중 {p}{J(p, "으로", "로")} 자신의 디지몬 1마리를 코스트를 지불하지 않고 진화할 수 있다. 나머지는 파기한다.";
            if (m.Groups["tail"].Success)
            {
                string? tail = Older66("[Main] " + m.Groups["tail"].Value);
                const string main = "【메인】 ";
                if (tail == null || !tail.StartsWith(main, StringComparison.Ordinal)) return null;
                ko += " " + tail[main.Length..];
            }
            return ko;
        }
        // EX2-049 "이 디지몬을 레스트시키는 것으로, ~ 오픈한다. 그중 「~」 1장을 자신의 「~」 1마리의 진화원 맨 아래에 놓는다. ~"
        if ((m = Regex.Match(t, @"^(?<lab>(?:\[[^\]]+\]\s*)*)You may suspend this Digimon to Reveal the top (?<n>\d+) cards of your deck\. Place (?<p>1 \[[^\]]+\]) among them under 1 of your (?<t>\[[^\]]+\])s as its bottom digivolution card\. Place the remaining cards at the bottom of your deck in any order\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string? pre = LabelsKo66(m.Groups["lab"].Value + " By suspending this Digimon,");
            string? p = PickKo(m.Groups["p"].Value);
            string host = NamesKo(m.Groups["t"].Value);
            if (pre == null || p == null) return null;
            return $"{pre}자신의 덱 위에서부터 {m.Groups["n"].Value}장 오픈한다. 그중 {p}{J(p, "을", "를")} 자신의 {host} 1마리의 진화원 맨 아래에 놓는다. 나머지는 원하는 순서대로 덱 아래로 되돌린다.";
        }
        // P-021 "자신의 「~」가 있을 때, 자신의 패에서 「~」 1장을 코스트를 지불하지 않고 등장시키는 것으로, 자신의 「~」 1명을 패로 되돌린다."
        if ((m = Regex.Match(alt, @"^(?<pre>.*?)⟦playret66:(?<p>\[[^\]]+\])\|(?<t>\[[^\]]+\])⟧\.?$")).Success)
        {
            string? pre = LabelsKo66(m.Groups["pre"].Value.TrimEnd().TrimEnd(',') + ",");
            if (pre == null) return null;
            return $"{pre}자신의 패에서 {NamesKo(m.Groups["p"].Value)} 1장을 코스트를 지불하지 않고 등장시킬 수 있다. 그렇게 했을 때, 자신의 {NamesKo(m.Groups["t"].Value)} 1명을 패로 되돌린다.";
        }
        // BT21-087 · EX2-072 · BT25-078 덱 위 공개 후 둘 중 하나
        if ((m = Regex.Match(alt, @"⟦rvor66:(?<id>\d+)⟧\.?$")).Success && SentenceParser.RevealOrSpec66(int.Parse(m.Groups["id"].Value)) is { } spec)
        {
            string? pre = LabelsKo66(alt[..m.Index]);
            string? a = PickKo(spec.PickA), b = PickKo(spec.PickB);
            if (pre == null || a == null || b == null) return null;
            string rest = spec.Rest == "trash" ? "나머지는 파기한다." : "나머지는 원하는 순서대로 덱 아래로 되돌린다.";
            string body = (spec.VerbA, spec.VerbB) switch
            {
                ("play", "add") => $"그중 {a}{J(a, "을", "를")} 코스트를 지불하지 않고 등장시키거나, {b}{J(b, "을", "를")} 패에 추가한다.",
                ("evo", "add") => $"자신의 디지몬 1마리는 그중 {a}{J(a, "으로", "로")} 코스트를 지불하지 않고 진화할 수 있다. 그렇게 하지 않았을 때, 그중 {b}{J(b, "을", "를")} 패에 추가한다.",
                ("add", "place") => $"그중 {a}{J(a, "을", "를")} 패에 추가하거나, {b}{J(b, "을", "를")} 이 디지몬의 진화원 맨 아래에 놓을 수 있다.",
                _ => null,
            } ?? "";
            if (body.Length == 0) return null;
            return $"{pre}자신의 덱 위에서부터 {spec.N}장 오픈한다. {body} {rest}";
        }
        // BT18-078
        if ((m = Regex.Match(alt, @"⟦recolor66⟧\.?$")).Success)
        {
            string? pre = LabelsKo66(alt[..m.Index]);
            if (pre == null) return null;
            return $"{pre}상대의 턴 종료까지, 상대의 디지몬 또는 테이머 1마리(명)의 원래 색을 화이트 이외의 1색으로 바꾼다.";
        }
        // P-156 "테이머 1명을 고른다. 자신의 패/트래시에서 고른 테이머와 같은 색의 ~ 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다."
        if ((m = Regex.Match(alt, @"⟦tamercolorplay66:(?<n>\d+)⟧\.?$")).Success)
        {
            string? pre = LabelsKo66(alt[..m.Index]);
            if (pre == null) return null;
            return $"{pre}테이머 1명을 고른다. 자신의 패/트래시에서 고른 테이머와 같은 색의 등장 코스트 {m.Groups["n"].Value} 이하인 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다.";
        }
        // BT13-046 "~ 메모리를 +3 하고, 자신의 패 1장을 공개한다. 그 카드가 옐로인 카드라면, 시큐리티 맨 위에 뒷면으로 놓는다. 아니라면, 패로 되돌린다."
        if ((m = Regex.Match(alt, @"⟦gainrevsec66:(?<g>\d+):(?<col>[a-z]+)⟧\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string g = m.Groups["g"].Value;
            string end = $"메모리를 +{g} 한다.";
            string? ko = Older66(alt[..m.Index] + $"gain {g} memory.");
            // "If the total cards in both players' security stacks is 6 or less, ~" (앞의 규칙이 옮기지 못한 조건 — 서로의 시큐리티 합계)
            if (ko == null && Regex.Match(alt[..m.Index], @"^(?<lab>(?:\[[^\]]+\]\s*)*)If the total cards in both players' security stacks is (?<n>\d+) or less, $", RegexOptions.IgnoreCase) is { Success: true } sm
                && LabelsKo66(sm.Groups["lab"].Value) is { } lab66)
                ko = $"{lab66}서로의 시큐리티 합계가 {sm.Groups["n"].Value}장 이하라면, {end}";
            if (ko == null || !ko.EndsWith(end, StringComparison.Ordinal)) return null;
            string col = SentenceParser.ColorKo66(Enum.Parse<DigimonTcg.Core.Models.CardColor>(m.Groups["col"].Value, ignoreCase: true));
            return ko[..^end.Length] + $"메모리를 +{g} 하고, 자신의 패 1장을 공개한다. 그 카드가 {col}인 카드라면, 시큐리티 맨 위에 뒷면으로 놓는다. 아니라면, 패로 되돌린다.";
        }
        // 그 밖의 같은 뜻 다른 표기 (BT7-097 · BT3-030)
        if (!alt.Contains('⟦')) return Older66(alt);
        return null;
    }

    /// <summary>절 앞의 시점 표시만 한글로 ("[On Play] " → "【등장 시】 ") — 빈 문자열이면 ""</summary>
    private static string? LabelsKo66(string englishPrefix)
    {
        if (englishPrefix.Trim().Length == 0) return "";
        const string gain = "메모리를 +1 한다.";
        string e = englishPrefix.TrimEnd();
        string? ko = Older66(e + (e.EndsWith(",") ? " gain 1 memory." : " Gain 1 memory."));
        if (ko == null || !ko.EndsWith(gain, StringComparison.Ordinal)) return null;
        return ko[..^gain.Length];
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older66(string english)
    {
        _koDepth66++;
        try { return RenderClause(english); }
        finally { _koDepth66--; }
    }
}
