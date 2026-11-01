using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.69.0 미지원 문장 43차 묶음의 생성 한글 — 원래 규칙(42차까지)이 옮기지 못한 절만 (SentenceParser.Batch69와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth69;

    private const string Placeholder69 = "메모리를 +1 한다";

    private static string? RenderClause69(string rawClause)
    {
        if (_koDepth69 > 0) return null;
        string t = Regex.Replace(rawClause.Replace(' ', ' ').Replace('’', '\'').Trim(), @"\.""$", ".");
        Match m;
        // BT26-100 "~ gain ＜Blocker＞ (~) While you have ~, they also get +3000 DP." — 두 지속 효과로 옮겨 잇는다 (뒤의 효과는 앞의 표시 없이)
        if (SentenceParser.SplitClause69(t) is { } parts)
        {
            var a = Older69(parts[0]);
            var b = Older69(parts[1]);
            if (a == null || b == null) return null;
            b = Regex.Replace(b, @"^(?:【[^】]+】\s*|\[[^\]]+\]\s*)+", "");
            return $"{a} {b}";
        }
        // BT11-093 "~. If it digivolved into a Digimon with the same level, that Digimon isn't affected by your opponent's Option cards until the end of your opponent's turn."
        if ((m = Regex.Match(t, @"^(?<h>.+?\.) If it digivolved into a Digimon with the same level, that Digimon (?<rest>isn't affected by your opponent's Option cards? (?:effects )?until [^.]+)\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string rest = Regex.Replace(m.Groups["rest"].Value, @"Option cards? (?:effects )?until", "Option card effects until", RegexOptions.IgnoreCase);
            string? ko = Older69($"{m.Groups["h"].Value} Then, that Digimon {rest}.");
            if (ko == null || !ko.Contains("그 후, ", StringComparison.Ordinal)) return null;
            int at = ko.LastIndexOf("그 후, ", StringComparison.Ordinal);
            return ko[..at] + "같은 Lv.로 진화하고 있었다면, " + ko[(at + "그 후, ".Length)..];
        }
        // BT22-093 "~, if it has a digivolution card with the same level as the digivolved Digimon, by suspending this Tamer, ~" — 조건은 사건 뒤에 넣는다
        if ((m = Regex.Match(t, @"^(?<a>.+?, )if it has a digivolution card with the same level as the digivolved Digimon, (?<b>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            string? ko = Older69(m.Groups["a"].Value + m.Groups["b"].Value);
            int at = ko?.IndexOf("때, ", StringComparison.Ordinal) ?? -1;
            if (ko == null || at < 0) return null;
            at += "때, ".Length;
            return ko[..at] + "그 디지몬의 진화원에 진화한 디지몬과 Lv.이 같은 카드가 있다면, " + ko[at..];
        }
        // BT12-090 · BT22-102 "~, digivolve that Digimon into ~ in your hand for its digivolution cost." · "~, digivolve it into ~ in the trash with the digivolution cost reduced by 2."
        if ((m = Regex.Match(t, @"^(?<h>.+?, )digivolve (?:that Digimon|it) into (?<p>.+?) in (?:your |the )?(?<from>hand|trash) (?:for (?:its|the) digivolution cost|with the (?:digivolution )?cost reduced by (?<r>\d+))\.?$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + Regex.Replace(m.Groups["p"].Value, @"^(?:an?|1) ", "", RegexOptions.IgnoreCase)) is { } pe && pe.EndsWith(" 1장", StringComparison.Ordinal))
        {
            string card = pe[..^3];
            bool trash = m.Groups["from"].Value.Equals("trash", StringComparison.OrdinalIgnoreCase);
            string body2 = m.Groups["r"].Success
                ? $"그 디지몬을 {(trash ? "트래시" : "패")}의 {card}{J(card, "으로", "로")} 지불하는 진화 코스트 -{m.Groups["r"].Value} 하여 진화시킨다"
                : $"그 디지몬을 {(trash ? "트래시" : "패")}의 {card}{J(card, "으로", "로")} 진화시킬 수 있다";
            return Fill69(Older69(m.Groups["h"].Value + "gain 1 memory."), body2);
        }
        // BT20-053 "~ Then, if during an attack until the end of your opponent's turn, ~" — 공식 "그 후, 어택 중이라면, 상대의 턴 종료까지 ~"
        if (SentenceParser.DuringAttackRe69.IsMatch(t))
        {
            string en = SentenceParser.DuringAttackRe69.Replace(t, "until ");
            string? breed = null;
            // "You may play 1 [Dorumon] or [Ryudamon] from your hand to your empty breeding area without paying the cost." — 앞의 규칙에 한글이 없다
            if ((m = Regex.Match(en, @"^(?<lab>(?:\[[^\]]+\]\s*)*)You may play (?<p>1 [^.]+?) from your hand to your empty breeding area without paying (?:the|its) cost\. (?<rest>.+)$", RegexOptions.IgnoreCase)).Success
                && PickKo(m.Groups["p"].Value) is { } pb)
            {
                breed = $"자신의 패에서 {pb}{J(pb, "을", "를")} 비어 있는 자신의 육성 에어리어에 코스트를 지불하지 않고 등장시킬 수 있다";
                en = $"{m.Groups["lab"].Value}Gain 1 memory. {m.Groups["rest"].Value}";
            }
            string? ko = Older69(en);
            if (breed != null) ko = Fill69(ko, breed);
            int at = ko?.LastIndexOf("그 후, ", StringComparison.Ordinal) ?? -1;
            if (ko == null || at < 0) return null;
            at += "그 후, ".Length;
            return ko[..at] + "어택 중이라면, " + ko[at..];
        }
        // BT11-089 같은 뜻 다른 표기 (쉼표) — 바꿔 쓴 문장을 앞의 규칙으로
        {
            string alt = SentenceParser.ClauseRewriteText69(t);
            if (alt != t && !alt.Contains('⟦')) return Older69(alt);
        }
        string? head = null, body = null;
        // BT19-084 "[Main] By suspending this Tamer, digivolve 1 of your Digimon into a Digimon card in your face-up security cards. If this effect digivolved, ~"
        if ((m = Regex.Match(t, @"^(?<h>.+?, )digivolve (?:1|one) of your Digimon into (?:an?|1) Digimon card in your face[- ]up security (?:cards|stack)\.(?<tail>.*)$", RegexOptions.IgnoreCase)).Success)
        {
            string? ko = Older69($"{m.Groups["h"].Value}gain 1 memory.{m.Groups["tail"].Value}");
            return Fill69(ko, "자신의 디지몬 1마리를 자신의 앞면의 시큐리티의 디지몬 카드로 진화시킨다");
        }
        // BT19-100 "[Security] [Opponent's Turn] When an opponent's Digimon attacks, if all of your Digimon and Tamers have the [D-Reaper] trait, for each of 1 of your [Mother D-Reaper]'s digivolution cards, the attacking Digimon get -1000 DP for the turn."
        if ((m = SentenceParser.SecurityAttackedRe69.Match(t)).Success)
        {
            string cond = "";
            if (m.Groups["cond"].Success)
            {
                var all = Regex.Match(m.Groups["cond"].Value, @"^if all of your Digimon and Tamers have the (?<x>\[[^\]]+\]) trait, $", RegexOptions.IgnoreCase);
                if (!all.Success) return null;
                cond = $"자신의 디지몬과 테이머 전부가 특징으로 {NamesKo(all.Groups["x"].Value, trait: true)}{J(NamesKo(all.Groups["x"].Value, trait: true), "을", "를")} 가진다면, ";
            }
            string? ko = Older69($"[Security] {m.Groups["lab"].Value}When an opponent's Digimon attacks, gain 1 memory.");
            return Fill69(ko, $"{cond}턴 종료까지 어택한 디지몬을 자신의 {NamesKo(m.Groups["n"].Value)} 1마리의 진화원 1장마다 DP -{m.Groups["dp"].Value} 한다");
        }
        // ST22-10 "[Security] [All Turns] When any of your ~ would leave the battle area other than by battle, by trashing this card, 1 of those Digimon doesn't leave."
        if ((m = SentenceParser.SecurityGuardRe69.Match(t)).Success)
        {
            string? ko = Older69($"[Security] {m.Groups["lab"].Value}When {m.Groups["ev"].Value}, gain 1 memory.");
            return Fill69(ko, "이 카드를 파기하는 것으로, 그 디지몬 1마리는 벗어나지 않는다");
        }
        // BT19-100 【시큐리티】 "You may play 1 [D-Reaper] trait card with a play cost equal to or lower than the number of digivolution cards of 1 of your [Mother D-Reaper]'s from your hand without paying the cost."
        if ((m = SentenceParser.SourceCountPlayRe69.Match(t)).Success && PickKo(m.Groups["p"].Value) is { } p1)
        { head = t[..m.Index]; body = $"자신의 패에서 등장 코스트가 자신의 {NamesKo(m.Groups["n"].Value)} 1마리의 진화원 매수 이하인 {p1}{J(p1, "을", "를")} 코스트를 지불하지 않고 등장시킬 수 있다"; }
        // EX10-068 "~ by returning ~, from your hand or trash and without paying the cost, you may play 1 level 4 or lower Digimon card with the same color as the card this effect returned."
        else if ((m = SentenceParser.SameColorPlayRe69.Match(t)).Success && PickKo(m.Groups["p"].Value) is { } p2)
        { head = t[..m.Index]; body = $"자신의 패/트래시에서, 이 효과로 되돌린 카드와 같은 색의 {p2}{J(p2, "을", "를")} 코스트를 지불하지 않고 등장시킬 수 있다"; }
        // BT11-094 "~, you may play 1 [Angewomon] or [LadyDevimon] with a different name than the Digimon you digivolved into from your hand without paying the cost."
        else if ((m = SentenceParser.DiffNamePlayRe69.Match(t)).Success)
        { head = t[..m.Index]; body = $"자신의 패에서 {NamesKo(m.Groups["p"].Value[2..])} 중 진화한 디지몬과 명칭이 서로 다른 1장을 코스트를 지불하지 않고 등장시킬 수 있다"; }
        // BT22-090 · BT22-101 "~, this Tamer may digivolve into [LordKnightmon] in the hand with the digivolution cost reduced by 3."
        else if ((m = Regex.Match(t, @"^(?<h>.+?, )this Tamer may digivolve into (?<p>\[[^\]]+\]) in (?:the|your) hand with the (?:digivolution |memory )?cost reduced by (?<n>\d+)\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string nk = NamesKo(m.Groups["p"].Value);
            head = m.Groups["h"].Value;
            body = $"이 테이머를 패의 {nk}{J(nk, "으로", "로")} 지불하는 진화 코스트 -{m.Groups["n"].Value} 하여 진화시킬 수 있다";
        }
        if (head == null || body == null) return null;
        bool lower = Regex.IsMatch(head, @",\s*$|Then,\s*$");
        if (Regex.IsMatch(head, @"[^\s,]$")) head += " ";
        return Fill69(Older69(head + (lower ? "gain 1 memory." : "Gain 1 memory.")), body);
    }

    /// <summary>앞의 규칙이 옮긴 한글의 자리 표시 (메모리 +1)를 이 묶음의 문장으로 — 자리 표시가 정확히 한 번일 때만</summary>
    private static string? Fill69(string? ko, string body)
    {
        if (ko == null) return null;
        int at = ko.IndexOf(Placeholder69, StringComparison.Ordinal);
        if (at < 0 || ko.IndexOf(Placeholder69, at + 1, StringComparison.Ordinal) >= 0) return null;
        return ko[..at] + body + ko[(at + Placeholder69.Length)..];
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older69(string english)
    {
        _koDepth69++;
        try { return RenderClause(english); }
        finally { _koDepth69--; }
    }
}
