using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.79.0 미지원 문장 53차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I79 = RegexOptions.IgnoreCase;
    private const string TokenSpec79 = @"\(Digimon/(?:[^()]|\([^()]*\))*\)";

    /// <summary>v0.79.0 원래 규칙이 옮기지 못한 절만 (EX5-058 · BT23-013 · BT19-091 · BT26-086)</summary>
    private static string? RenderClause79(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // EX5-058 토큰 효과 "[All Turns] This Digimon doesn't unsuspend."
        if (SentenceParser.SelfUnsuspendLockRe79.IsMatch(t))
            return "【서로의 턴】 이 디지몬은 액티브가 되지 않는다.";
        // EX5-058 "If there are 4 or more total Digimon, play 1 [X] Token suspended to your battle area. (~) If there are 3 or fewer, play it suspended to your opponent's battle area."
        if ((m = Regex.Match(t, @"^(?<h>(?:\[[^\]]+\]\s*)*)If there are 4 or more total Digimon, play 1 (?<x>\[[^\]]+\]) Token suspended to your battle area\.\s*(?<spec>" + TokenSpec79 + @")\s*If there are 3 or fewer, play it suspended to your opponent's battle area\.?$", I79)).Success
            && RenderClause($"{m.Groups["h"].Value}Play 1 {m.Groups["x"].Value} Token. {m.Groups["spec"].Value}") is { } k58
            && k58.EndsWith("토큰 1마리를 등장시킨다.", StringComparison.Ordinal))
            return k58[..^"등장시킨다.".Length] + "서로의 디지몬의 합계가 4마리 이상이라면, 자신의 배틀 에어리어에 레스트 상태로 등장시킨다. 3마리 이하라면, 상대의 배틀 에어리어에 레스트 상태로 등장시킨다.";
        // BT23-013 "You may play 1 [X] Token (~) or, from your hand or trash, 1 ~ without paying the cost. This effect can't play cards with the same names as any of your Digimon."
        if ((m = Regex.Match(t, @"^(?<h>(?:\[[^\]]+\]\s*)*)You may play 1 (?<x>\[[^\]]+\]) Token (?<spec>" + TokenSpec79 + @") or, from your hand or trash, (?<pick>1 .+?) without paying the cost\. This effect can't play cards with the same names? as (?:any of )?your Digimon\.?$", I79)).Success
            && RenderClause($"{m.Groups["h"].Value}You may play 1 {m.Groups["x"].Value} Token {m.Groups["spec"].Value} without paying the cost.") is { } ka
            && RenderClause($"{m.Groups["h"].Value}You may play {m.Groups["pick"].Value} from your hand or trash without paying the cost.") is { } kb
            && ka.IndexOf("토큰 1마리를 ", StringComparison.Ordinal) is int ia and >= 0
            && kb.IndexOf("자신의 패 또는 트래시에서", StringComparison.Ordinal) is int ib and >= 0)
            return ka[..ia] + "토큰 1마리 또는 " + kb[ib..] + " 이 효과로는 자신의 디지몬과 같은 명칭의 카드는 등장시킬 수 없다.";
        // BT19-091 "Play 1 [A] Token (~), 1 [B] Token (~), and 1 [C] Token (~). This effect can't play tokens with the same names as your Digimon. Then, 1 of your level 5 Digimon gains ＜Alliance＞ twice for the turn and attacks."
        if ((m = Regex.Match(t, @"^(?<h>(?:\[[^\]]+\]\s*)*)(?<a>Play 1 \[[^\]]+\] Token " + TokenSpec79 + @"(?:,(?: and)? 1 \[[^\]]+\] Token " + TokenSpec79 + @")*)\. This effect can't play tokens with the same names? as (?:any of )?your Digimon\. Then, (?<b>.+? gains ＜Alliance＞) twice (?<c>for the turn and attacks)\.?$", I79)).Success
            && RenderClause($"{m.Groups["h"].Value}{m.Groups["a"].Value}.") is { } k91a
            && RenderClause($"{m.Groups["h"].Value}{m.Groups["b"].Value} {m.Groups["c"].Value}.") is { } k91b
            && k91b.IndexOf("이 턴 동안", StringComparison.Ordinal) is int i91 and >= 0
            && k91b.Contains("를 얻고,", StringComparison.Ordinal))
            return k91a + " 이 효과로는 자신의 디지몬과 같은 명칭의 토큰은 등장시킬 수 없다. 그 후, " + k91b[i91..].Replace("를 얻고,", "를 2개 얻고,");
        // BT20-098 "By returning 9 levels' total worth of Digimon cards from your opponent's trash to the bottom of the deck, you may play 1 [Ghost] trait Digimon card of each returned card's level from your trash without paying the costs. Then, ~"
        if ((m = Regex.Match(t, @"^(?<h>(?:\[[^\]]+\]\s*)*)By returning (?<n>\d+) levels' total worth of Digimon cards from your opponent's trash to the bottom of the deck, you may play 1 (?<pick>.+?) of each returned card's level from your trash without paying the costs?\.(?<rest>.*)$", I79)).Success
            && RenderClause($"{m.Groups["h"].Value}You may play 1 {m.Groups["pick"].Value} from your trash without paying the cost.{m.Groups["rest"].Value}") is { } k98
            && Regex.Match(k98, @"^(?<h>.*?)자신의 트래시에서 (?<p>.+?) 1장을 (?<r>코스트를 지불하지 않고 등장시킬 수 있다\..*)$") is { Success: true } km)
            return $"{km.Groups["h"].Value}상대의 트래시에서 디지몬 카드를 Lv. 합계가 {m.Groups["n"].Value}{SentenceParser.IGa79(int.Parse(m.Groups["n"].Value))} 되도록 덱 아래로 되돌리는 것으로, 되돌린 카드의 Lv.마다 자신의 트래시에서 그 Lv.의 {km.Groups["p"].Value} 1장씩을 {km.Groups["r"].Value}";
        // BT10-041 "You may use 1 Option card with [Plug-In] in its name or that's yellow and has a memory cost of 5 or less from your hand ~. Place the Option card used with this effect on top of your security stack face down instead of placing it in your trash."
        if ((m = Regex.Match(t, @"^(?<h>(?:\[[^\]]+\]\s*)*)You may use 1 Option card with (?<x>\[[^\]]+\]) in its name or that's (?<col>\w+) and has a memory cost of (?<n>\d+) or less (?<rest>from your hand without meeting its colou?r requirements or paying its memory cost)\. Place the Option card used with this effect on top of your security stack face down instead of placing it in your trash\.?$", I79)).Success
            && RenderClause($"{m.Groups["h"].Value}You may use 1 {m.Groups["col"].Value} Option card with a memory cost of {m.Groups["n"].Value} or less {m.Groups["rest"].Value}.") is { } k41
            && k41.IndexOf("자신의 패에서 ", StringComparison.Ordinal) is int i41 and >= 0)
            return k41[..(i41 + "자신의 패에서 ".Length)] + $"명칭에 {NamesKo(m.Groups["x"].Value)}{J(NamesKo(m.Groups["x"].Value).TrimEnd('」'), "을", "를")} 포함하는 옵션 카드 또는 " + k41[(i41 + "자신의 패에서 ".Length)..]
                   + " 이 효과로 사용한 옵션 카드는 트래시에 놓지 않고 자신의 시큐리티 맨 위에 뒷면으로 놓는다.";
        // BT26-086 "link up to 7 [Appmon] trait cards with different names from this Digimon's digivolution cards to this Digimon"
        if ((m = Regex.Match(t, @"^(?<a>.+? link up to \d+ .+? cards) with different names (?<b>from this Digimon's digivolution cards to this Digimon .+)$", I79)).Success
            && RenderClause($"{m.Groups["a"].Value} {m.Groups["b"].Value}") is { } k86
            && k86.Contains("이 디지몬의 진화원에서 ", StringComparison.Ordinal))
            return k86.Replace("이 디지몬의 진화원에서 ", "이 디지몬의 진화원에서 명칭이 서로 다른 ");
        return null;
    }
}
