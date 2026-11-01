using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.68.0 미지원 문장 42차 묶음의 생성 한글 — 원래 규칙(41차까지)이 옮기지 못한 절만 (SentenceParser.Batch68과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _koDepth68;

    private const string Placeholder68 = "메모리를 +1 한다";

    private static string? RenderClause68(string rawClause)
    {
        if (_koDepth68 > 0) return null;
        string t = rawClause.Replace(' ', ' ').Replace('’', '\'').Trim();
        // 한 절에 이어 적힌 두 효과 (BT14-078) — 따로 옮겨 잇는다
        if (SentenceParser.SplitTwoEffects68(t) is { } parts)
        {
            var kos = parts.Select(p => Older68(p) ?? RenderOne68(p)).ToList();
            return kos.All(k => k != null) ? string.Join(" ", kos) : null;
        }
        return RenderOne68(t);
    }

    private static string? RenderOne68(string t)
    {
        Match m;
        // [시큐리티]【자신의 턴】 · 패의 이 카드의 시점 효과는 원래 문장 그대로 옮긴다 ([트래시]로 바꿔 읽은 것은 내부 표시)
        string eng = Regex.Replace(t, @"\[(?<x>[^\]|]+(?:\|[^\]|]+)+)\]", x =>
        {
            var xs = x.Groups["x"].Value.Split('|').Select(y => $"[{y.Trim()}]").ToList();
            return xs.Count == 2 ? $"{xs[0]} or {xs[1]}" : string.Join(", ", xs.Take(xs.Count - 1)) + " or " + xs[^1];
        });
        string? body = null;
        string? head = null;
        // EX12-069 "~, you may play 1 [VB] trait Digimon card of the same level from your hand with the cost reduced by 3."
        if ((m = Regex.Match(eng, @"^(?<h>.+?, )you may play (?<p>1 .+?) of the same level from your hand with the (?:play |memory )?cost reduced by (?<r>\d+)\.?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["p"].Value) is { } p1)
        { head = m.Groups["h"].Value; body = $"자신의 패에서 어택한 디지몬과 같은 Lv.의 {p1}{J(p1, "을", "를")} 지불하는 코스트 -{m.Groups["r"].Value} 하여 등장시킬 수 있다"; }
        // BT19-053 "~ You may play 1 [Royal Base] trait Digimon card from your face-up security cards with the play cost reduced by 8."
        else if ((m = Regex.Match(eng, @"^(?<h>(?:\[[^\]]+\]\s*)*)You may play (?<p>1 .+?) from your face[- ]up security (?:cards|stack) with the (?:play |memory )?cost reduced by (?<r>\d+)\.?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["p"].Value) is { } p2)
        { head = m.Groups["h"].Value; body = $"자신의 앞면의 시큐리티에서 {p2}{J(p2, "을", "를")} 지불하는 등장 코스트 -{m.Groups["r"].Value} 하여 등장시킬 수 있다"; }
        // LM-068 "~, you may use this card with the cost reduced by 3."
        else if ((m = Regex.Match(eng, @"^(?<h>.+?, )you may use this card with the (?:use |memory )?cost reduced by (?<r>\d+)\.?$", RegexOptions.IgnoreCase)).Success)
        { head = m.Groups["h"].Value; body = $"패의 이 카드를 지불하는 사용 코스트 -{m.Groups["r"].Value} 하여 사용할 수 있다"; }
        // EX2-070 "~ Then, you may digivolve 1 of your Digimon into a Digimon card in your hand that can digivolve for a digivolution cost of 3 or less without paying its digivolution cost."
        else if ((m = Regex.Match(eng, @"^(?<h>.+? Then, )you may digivolve (?:1|one) of your Digimon into a Digimon card in your hand that can digivolve for a digivolution cost of (?<n>\d+) or less without paying (?:its|the) (?:digivolution |memory )?cost\.?$", RegexOptions.IgnoreCase)).Success)
        { head = m.Groups["h"].Value; body = $"자신의 디지몬 1마리를 패의 진화 코스트 {m.Groups["n"].Value} 이하로 진화할 수 있는 디지몬 카드 1장으로 코스트를 지불하지 않고 진화시킬 수 있다"; }
        // BT14-078 "[On Deletion] You may trash up to 3 ~ from your hand. Then, delete 1 of your opponent's level 3 or lower Digimon. For each card you trashed with this effect, increase this level by 1."
        else if ((m = Regex.Match(eng, @"^(?<h>(?:\[[^\]]+\]\s*)*)You may trash up to (?<n>\d+) (?<p>[^.]+?) from your hand\. Then, delete 1 of your opponent's level (?<lv>\d+) or lower Digimon\. For each card you trashed with this effect, increase this level by 1\.?$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + Regex.Replace(m.Groups["p"].Value, @"\bcards$", "card", RegexOptions.IgnoreCase)) is { } p3 && p3.EndsWith(" 1장", StringComparison.Ordinal))
        {
            head = m.Groups["h"].Value;
            body = $"자신의 패에서 {p3[..^3]} {m.Groups["n"].Value}장까지를 파기할 수 있다. 그 후, Lv.{m.Groups["lv"].Value} 이하의 상대의 디지몬 1마리를 소멸시킨다. 이 효과로 파기한 카드 1장마다 소멸시킬 수 있는 Lv.의 상한 +1";
        }
        // BT15-099 "~. When a card with [Myotismon] in its text is trashed by this effect, ＜Draw 2＞ (Draw 2 cards from your deck.)"
        else if ((m = SentenceParser.TrashedTextRe68.Match(eng)).Success && Regex.Match(m.Groups["d"].Value, @"\d+") is { Success: true } dn)
        {
            head = eng[..m.Index] + ". Then, ";
            body = $"이 효과로 파기한 카드에 {NamesKo(m.Groups["x"].Value)}{J(NamesKo(m.Groups["x"].Value), "이", "가")} 기술되어 있다면, 《{dn.Value} 드로우》 (자신의 덱에서 카드를 {dn.Value}장 드로우한다)";
        }
        // BT17-102 "[On Deletion] You may play 1 Tamer card with [Tai Kamiya] or [Kari Kamiya] in its name from your hand without paying the cost, or hatch in your breeding area."
        else if ((m = SentenceParser.TamerOrHatchRe68.Match(eng)).Success)
        {
            head = eng[..m.Index];
            body = $"자신의 패에서 명칭에 {NamesKo(m.Groups["x"].Value)}{J(NamesKo(m.Groups["x"].Value), "을", "를")} 포함하는 테이머 카드 1장을 코스트를 지불하지 않고 등장시키거나, 육성 에어리어에 부화할 수 있다";
        }
        // BT18-033 "[Hand] [Main] If your breeding area is empty, by returning 1 ~ from your trash to the bottom of the deck, play this card in your breeding area without paying the cost."
        else if ((m = Regex.Match(eng, @"^\[Hand\]\s*\[Main\]\s*If your breeding area is empty, by returning (?<p>1 [^,]+?) from your trash to the bottom of the deck, play this card in your breeding area without paying (?:the|its) cost\.?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["p"].Value) is { } p4)
            return $"[패] 【메인】 자신의 육성 에어리어가 비어 있다면, 자신의 트래시에서 {p4}{J(p4, "을", "를")} 덱 아래로 되돌리는 것으로, 이 카드를 육성 에어리어에 코스트를 지불하지 않고 등장시킨다.";
        // BT22-075 "~ When this Digimon would leave the battle area, you may play 1 of this Digimon's link cards without paying the cost."
        else if ((m = Regex.Match(eng, @"^(?<h>.+?, )you may play 1 of this Digimon's link cards without paying (?:the|its) cost\.?$", RegexOptions.IgnoreCase)).Success)
        { head = m.Groups["h"].Value; body = "이 디지몬의 링크 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다"; }
        // BT25-039 "~ If this effect played, you may place this card as the played Digimon's bottom digivolution card."
        else if ((m = Regex.Match(eng, @"^(?<h>.+? If this effect played, )you may place this card as the played Digimon's bottom digivolution card\.?$", RegexOptions.IgnoreCase)).Success)
        { head = m.Groups["h"].Value; body = "이 카드를 등장한 디지몬의 진화원 맨 아래에 놓을 수 있다"; }
        if (body == null || head == null) return null;
        bool lower = Regex.IsMatch(head, @",\s*$|Then,\s*$");
        string? ko = Older68(head + (lower ? "gain 1 memory." : "Gain 1 memory."));
        if (ko == null) return null;
        int at = ko.IndexOf(Placeholder68, StringComparison.Ordinal);
        if (at < 0 || ko.IndexOf(Placeholder68, at + 1, StringComparison.Ordinal) >= 0) return null;
        return ko[..at] + body + ko[(at + Placeholder68.Length)..];
    }

    /// <summary>
    /// v0.68.0 화면 표시: 효과로 바뀐 정보 (원래 명칭 · 색 · DP — BT11-043 등, 원래 색 — BT18-078, 이 턴 동안 더해진 색 — BT8-040). 바뀐 것이 없으면 null.
    /// 그 효과를 받지 않는 동안 (종합 룰 15-15-5-2)이면 "(지금은 받지 않음)"을 붙인다
    /// </summary>
    public static string? ChangedInfoBadge68(CardInstance d)
    {
        var parts = new System.Collections.Generic.List<string>();
        bool shielded = false;
        if (d.Changed67Raw is { } ch)
        {
            string col = d.BaseColor66Raw is { } bc0 ? " · " + SentenceParser.ColorKo66(bc0.Color) : "";
            parts.Add($"원래 명칭 「{NameKo(ch.Name)}」{col} · DP {ch.Dp:N0}");
            shielded = d.Changed67 == null;
        }
        else if (d.BaseColor66Raw is { } bc)
        {
            parts.Add($"원래 색 {SentenceParser.ColorKo66(bc.Color)}");
            shielded = d.BaseColor66 == null;
        }
        if (d.TimedColors67.Count > 0)
            parts.Add($"이 턴 동안 {string.Join("/", d.TimedColors67.Select(x => x.Color).Distinct().Select(SentenceParser.ColorKo66))}로도 취급");
        if (parts.Count == 0) return null;
        return "✦ " + string.Join(" · ", parts) + (shielded ? " (지금은 받지 않음)" : "");
    }

    /// <summary>앞의 규칙으로 옮긴다 (이 묶음의 규칙은 끄고 — 같은 절이 다시 여기로 오지 않게)</summary>
    private static string? Older68(string english)
    {
        _koDepth68++;
        try { return RenderClause(english); }
        finally { _koDepth68--; }
    }
}
