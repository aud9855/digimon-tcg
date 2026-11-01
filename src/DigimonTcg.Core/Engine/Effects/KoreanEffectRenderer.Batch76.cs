using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.76.0 미지원 문장 50차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I76 = RegexOptions.IgnoreCase;

    /// <summary>v0.76.0 원래 규칙이 옮기지 못한 절만 (BT13-020 · BT15-082 · BT11-086 · BT11-105)</summary>
    private static string? RenderClause76(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // BT11-086 "~ You may play 1 X from your trash without paying the cost. If DigiXrosing, add 1 to the number of cards you play with this effect."
        //  — 공식 한글 BT11-086 문형 ("~ 매수 +1")
        if ((m = Regex.Match(t, @"^(?<a>.+?\.) If DigiXrosing, add (?<n>\d+) to the number of cards you play with this effect\.?$", I76)).Success
            && RenderClause(m.Groups["a"].Value) is { } ka)
            return $"{ka} 디지크로스하고 있었다면, 이 효과로 등장시키는 매수 +{m.Groups["n"].Value}.";
        // BT13-020 "~. For the turn, treat the Tamer played with this effect as a 12000 DP Digimon with ＜Rush＞, and it cannot digivolve."
        //  — 공식 한글 BT13-020 문형 ("~으로도 취급하며, ~을 얻고, 진화할 수 없다" — 기간은 다른 카드와 같이 "이 턴 동안")
        if ((m = Regex.Match(t, @"^(?<a>.+?\.) (?<d>For the turn|Until the end of your opponent's turn), treat the Tamer (?:played|that was played) (?:with|by) this effect as an? (?<dp>\d+) DP Digimon(?: with (?<kw>＜[^＞]+＞(?:(?:,| and|, and) ＜[^＞]+＞)*))?(?: \([^)]*\))?(?<noevo>,? and it (?:cannot|can't|can not) digivolve)?\.?$", I76)).Success
            && RenderClause(m.Groups["a"].Value) is { } kb)
        {
            var kws = Regex.Matches(m.Groups["kw"].Value, @"＜([^＞]+)＞").Select(x => KeywordKo(x.Groups[1].Value, withReminder: false)).ToList();
            if (kws.Any(k => k == null)) return null;
            string dp = m.Groups["dp"].Value;
            string dur = m.Groups["d"].Value.Equals("For the turn", StringComparison.OrdinalIgnoreCase) ? "이 턴 동안" : "상대의 턴 종료 시까지";
            string kwList = string.Concat(kws.Select((k, i) => k + (i < kws.Count - 1 ? J(k!, "과", "와") + " " : "")));
            string body = $"{dur}, 이 효과로 등장한 테이머는 디지몬・DP {dp}{J(dp, "으로", "로")}도 취급하며";
            bool noEvo = m.Groups["noevo"].Success;
            body += kws.Count > 0 && noEvo ? $", {kwList}{J(kwList, "을", "를")} 얻고, 진화할 수 없다." : kws.Count > 0 ? $", {kwList}{J(kwList, "을", "를")} 얻는다." : noEvo ? ", 진화할 수 없다." : "";
            if (kws.Count == 0 && !noEvo) body = $"{dur}, 이 효과로 등장한 테이머는 디지몬・DP {dp}{J(dp, "으로", "로")}도 취급한다.";
            return $"{kb} {body}";
        }
        // BT8-070 "If this Digimon has a red digivolution card, choose any number of your opponent's Digimon. If this Digimon has a black digivolution card, choose any number of your opponent's Tamers.
        //  The chosen cards' play costs must add up to 6 or less. Delete the chosen cards." — 공식 한글 BT8-070과 같은 뜻 (진화원의 색마다 고를 수 있는 것, 합쳐서 등장 코스트 합계)
        if ((m = Regex.Match(t, @"^(?<h>(?:\s*\[[^\]]+\])+)\s*If this Digimon has an? (?<c1>red|blue|yellow|green|black|purple|white) digivolution card, choose any number of your opponent's Digimon\. If this Digimon has an? (?<c2>red|blue|yellow|green|black|purple|white) digivolution card, choose any number of your opponent's Tamers\. The chosen cards' play costs must add up to (?<n>\d+) or less\. Delete the chosen cards\.?$", I76)).Success
            && Header76(m.Groups["h"].Value) is { } h8)
        {
            string c1 = ColorKo(m.Groups["c1"].Value), c2 = ColorKo(m.Groups["c2"].Value);
            return $"{h8} 이 디지몬의 진화원에 {c1}인 카드가 있다면 상대의 디지몬을, {c2}인 카드가 있다면 상대의 테이머를 원하는 수만큼 고른다. 고른 카드는 합쳐서 등장 코스트 합계 {m.Groups["n"].Value} 이하여야 한다. 고른 카드 전부를 소멸시킨다.";
        }
        // BT11-107 "Choose any number of your opponent's Digimon and/or Tamers whose combined play costs are less than or equal to the play cost of 1 of your Digimon with [Greymon] in its name,
        //  and delete all of the chosen cards. Then, ~"
        if ((m = Regex.Match(t, @"^(?<h>(?:\s*\[[^\]]+\])+)\s*Choose any number of your opponent's (?<w>Digimon and/or Tamers|Digimon and Tamers|Digimon|Tamers) whose combined play costs are less than or equal to the play cost of (?<own>(?:1|one) of your .+?), and delete (?:all of )?(?:the chosen cards|them)\.(?<rest> .+)?$", I76)).Success
            && Header76(m.Groups["h"].Value) is { } h11 && TargetKo(m.Groups["own"].Value) is { } ownKo)
        {
            string w = m.Groups["w"].Value.ToLowerInvariant();
            string what = w.Contains("digimon") && w.Contains("tamers") ? "디지몬/테이머" : w.Contains("digimon") ? "디지몬" : "테이머";
            string body = $"{h11} 등장 코스트 합계가 {ownKo}의 등장 코스트 이하가 되도록 상대의 {what}{J(what, "을", "를")} 원하는 수만큼 골라, 고른 카드 전부를 소멸시킨다.";
            if (!m.Groups["rest"].Success) return body;
            if (RenderClause(m.Groups["h"].Value.Trim() + " " + Regex.Replace(m.Groups["rest"].Value.Trim(), @"^Then,?\s+", "", I76)) is not { } kr || KoHeader75.Match(kr) is not { Success: true, Length: > 0 } hr) return null;
            return $"{body} 그 후, {kr[hr.Length..]}";
        }
        // BT23-072 "[Hand] [Main] By paying 3 cost and placing this card as the bottom digivolution card of your [X] or [Y] in the breeding area, ~" (공식 한글 문형 "N 코스트 지불하고, ~ 놓는 것으로")
        if ((m = Regex.Match(t, @"^(?<h>(?:\s*\[[^\]]+\])+)\s*By paying (?<n>\d+) (?:cost|memory) and placing this card as the bottom digivolution card of (?:1 of |one of )?your (?<x>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) in (?:the|your) breeding area, (?<rest>.+)$", I76)).Success
            && RenderClause(m.Groups["h"].Value.Trim() + " " + Cap76(m.Groups["rest"].Value)) is { } k72 && KoHeader75.Match(k72) is { Success: true, Length: > 0 } h72)
            return $"{k72[..h72.Length].TrimEnd()} {m.Groups["n"].Value} 코스트 지불하고, 이 카드를 육성 에어리어의 자신의 {NamesKo(m.Groups["x"].Value)}의 진화원 아래에 놓는 것으로, {k72[h72.Length..]}";
        // BT23-073 "~, by deleting this Digimon or placing it as the bottom digivolution card of your [Mother Eater] in the breeding area, ~"
        if ((m = Regex.Match(t, @"^(?<a>.+?), by deleting this Digimon or placing it as the bottom digivolution card of (?:1 of |one of )?your (?<x>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) in (?:the|your) breeding area, (?<b>.+)$", I76)).Success
            && RenderClause($"{m.Groups["a"].Value}, by deleting this Digimon, {m.Groups["b"].Value}") is { } k73 && k73.Contains("이 디지몬을 소멸시키는 것으로", StringComparison.Ordinal))
            return k73.Replace("이 디지몬을 소멸시키는 것으로", $"이 디지몬을 소멸시키거나 육성 에어리어의 자신의 {NamesKo(m.Groups["x"].Value)}의 진화원 아래에 놓는 것으로");
        // EX6-060 "~, place 1 card with the [X] trait card from your trash as the bottom digivolution card of your [Y] in the breeding area." (공식 한글 문형 "~를 육성 에어리어의 자신의 「Y」의 진화원 아래에 놓는다")
        if ((m = Regex.Match(t, @"^(?<a>.+?, )place (?<pick>1 card with the \[[^\]]+\] trait)(?: card)? from your trash as the bottom digivolution card of (?:your )?(?<y>\[[^\]]+\]) in the breeding area\.?$", I76)).Success
            && RenderClause(m.Groups["a"].Value + "＜Draw 1＞.") is { } k60 && k60.IndexOf("《1 드로우》", StringComparison.Ordinal) is int at && at > 0 && PickKo(m.Groups["pick"].Value) is { } pk)
            return $"{k60[..at]}자신의 트래시에서 {pk}{J(pk, "을", "를")} 육성 에어리어의 자신의 {NamesKo(m.Groups["y"].Value)}의 진화원 아래에 놓는다.";
        // BT13-112 "Delete 1 of your opponent's Digimon, or you may play 1 of each Digimon with the [X] trait and a different name from the digivolution cards of your Digimon in the breeding area
        //  without paying their costs. When a Digimon is played by this effect, trash 1 of your Digimon in the breeding area, and all of your Digimon gain ＜Rush＞ for the turn."
        if ((m = Regex.Match(t, @"^(?<h>(?:\s*\[[^\]]+\])+)\s*Delete 1 of your opponent's Digimon, or you may play 1 of each (?<pick>.+?)(?: with (?:a )?different names?| and a different name) from the digivolution cards of your Digimon in (?:the|your) breeding area without paying (?:the|their|its) costs?\. When a Digimon is played by this effect, trash 1 of your Digimon in (?:the|your) breeding area, and all of your Digimon gain ＜Rush＞ for the turn\.?(?: \([^)]*\))?$", I76)).Success
            && Header76(m.Groups["h"].Value) is { } h112 && PickKo("1 " + Regex.Replace(m.Groups["pick"].Value, @"^Digimon with\b", "Digimon card with", I76)) is { } p112 && p112.EndsWith(" 1장", StringComparison.Ordinal))
            return $"{h112} 상대의 디지몬 1마리를 소멸시키거나, 육성 에어리어의 자신의 디지몬의 진화원에서 명칭이 서로 다른 {p112}씩을 코스트를 지불하지 않고 등장시킬 수 있다. 이 효과로 디지몬이 등장했을 때, 육성 에어리어의 자신의 디지몬을 파기하고, 이 턴 동안 자신의 디지몬 전부는 {KeywordKo("Rush", withReminder: false)}을 얻는다.";
        // EX10-061 "You may play 1 of each [X] trait card with different names from this Digimon's digivolution cards without paying the costs. ~" — "명칭이 서로 다른 ~ 1장씩"
        if ((m = Regex.Match(t, @"^(?<a>.*?\bplay )1 of each (?<pick>.+?) with (?:a )?different names? (?<b>from this Digimon's digivolution cards without paying (?:the|their|its) costs?\..*)$", I76)).Success
            && PickKo("1 " + m.Groups["pick"].Value) is { } p61 && p61.EndsWith(" 1장", StringComparison.Ordinal)
            && RenderClause($"{m.Groups["a"].Value}1 {m.Groups["pick"].Value} {m.Groups["b"].Value}") is { } k61 && k61.Contains(p61 + J(p61, "을", "를"), StringComparison.Ordinal))
            return k61.Replace(p61 + J(p61, "을", "를"), $"명칭이 서로 다른 {p61}씩을");
        // BT15-082 · BT11-105 — 같은 뜻의 다른 표기 (SentenceParser.Rewrite76)
        if (SentenceParser.Rewrite76(t) is { } alt && !Regex.IsMatch(alt, @"\bThen, if DigiXrosing, you may play 1 |⟦", I76))
            return RenderClause(alt);
        return null;
    }

    private static string Cap76(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>영어 머리말 → 한글 머리말 (【진화 시】 등)</summary>
    private static string? Header76(string header) =>
        RenderClause(header.Trim() + " ＜Draw 1＞.") is { } k && KoHeader75.Match(k) is { Success: true, Length: > 0 } h ? k[..h.Length].TrimEnd() : null;
}
