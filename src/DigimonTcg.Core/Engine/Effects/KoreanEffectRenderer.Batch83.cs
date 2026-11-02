using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.83.0 57차 묶음 — 한글 표시 (원래 규칙이 옮기지 못한 절만 · SentenceParser.Batch83)</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I83 = RegexOptions.IgnoreCase;

    /// <summary>v0.83.0 (BT24-061 · BT26-060 · BT26-080 · BT26-027 · BT26-089)</summary>
    private static string? RenderClause83(string rawClause)
    {
        string t = rawClause.Trim().Replace('’', '\'');
        Match m;
        // EX4-072 "Choose 1 of your [Gallantmon], [Sakuyamon] or [MegaGargomon]. ~" — "자신의 「A」/「B」/「C」 1마리를 고른다."
        string t2 = SentenceParser.Rewrite83(t) ?? t;
        if ((m = Regex.Match(t2, @"^(?<h>.*?)\bChoose (?:1|one) of your (?<names>\[[^\]]+\](?:(?:,| or|, or) \[[^\]]+\])*)\. (?<rest>.+)$", I83)).Success
            && RenderClause($"{m.Groups["h"].Value}Choose 1 of your level 6 Digimon. {m.Groups["rest"].Value}") is { } kChoose
            && kChoose.Contains("자신의 Lv.6 디지몬 1마리를 고른다.", StringComparison.Ordinal))
        {
            string names = string.Join("/", TargetSpec.Names(m.Groups["names"].Value).Select(n => $"「{NameKo(n)}」"));
            return kChoose.Replace("자신의 Lv.6 디지몬 1마리를 고른다.", $"자신의 {names} 1마리를 고른다.");
        }
        // BT25-101 "~ link this card or 1 [TS] trait card from your trash to 1 of your Digimon ~" — "이 카드 또는 자신의 트래시의 ~"
        if ((m = Regex.Match(t, @"^(?<a>.*?\blink )this card or (?<b>(?:1|one) .+? from your trash to .+)$", I83)).Success
            && RenderClause(m.Groups["a"].Value + m.Groups["b"].Value) is { } kLink
            && kLink.LastIndexOf("자신의 트래시에서 ", StringComparison.Ordinal) is int li and >= 0)
            return kLink[..li] + "이 카드 또는 자신의 트래시의 " + kLink[(li + "자신의 트래시에서 ".Length)..];
        // BT26-027 비용의 늘어놓은 특징 "[A], [B] or [C] trait"
        if (SentenceParser.Rewrite83(t) is { } alt && RenderClause(alt) is { } kAlt)
            return kAlt;
        // BT24-061 "Return ~ to the top of the deck" — 덱 아래로의 표시에서 놓는 곳만
        if ((m = Regex.Match(t, @"^(?<a>.*?)\bReturn (?<t>(?:\d+|up to \d+|all) of (?:your opponent's|their) .+?) to the top of (?:the|its owner's|their owners'?|their|the owner's) decks?\.?$", I83)).Success
            && RenderClause($"{m.Groups["a"].Value}Return {m.Groups["t"].Value} to the bottom of the deck.") is { } kTop
            && kTop.EndsWith("덱 아래로 되돌린다.", StringComparison.Ordinal))
            return kTop[..^"덱 아래로 되돌린다.".Length] + "덱 위로 되돌린다.";
        // BT26-060 "Return the top 5 stacked cards of 3 of your opponent's Digimon to the top of the deck" — 공식 "겹쳐져 있는 카드를 위에서부터 N장" (BT21-030 파기와 같은 표기)
        if ((m = Regex.Match(t, @"^(?<a>.*?)\bReturn the top (?<n>\d+) stacked cards of (?<t>.+?) to the top of (?:the|its owner's|their owners'?|their) decks?\.?$", I83)).Success
            && RenderClause($"{m.Groups["a"].Value}Trash the top {m.Groups["n"].Value} stacked cards of {m.Groups["t"].Value}.") is { } kStack
            && kStack.EndsWith("장 파기한다.", StringComparison.Ordinal))
            return kStack[..^"파기한다.".Length] + "덱 위로 되돌린다.";
        // BT26-080 "Delete 1 of your opponent's Digimon with the same orientation as this Digimon" — "이 디지몬과 같은 표시 형식인 상대의 디지몬"
        if ((m = Regex.Match(t, @"^(?<a>.*?)\b(?<v>Delete|Suspend) (?<n>\d+) of your opponent's Digimon with the same orientation as this Digimon\.?$", I83)).Success
            && RenderClause($"{m.Groups["a"].Value}{m.Groups["v"].Value} {m.Groups["n"].Value} of your opponent's suspended Digimon.") is { } kOri
            && kOri.Contains("레스트 상태인 상대의 디지몬", StringComparison.Ordinal))
            return kOri.Replace("레스트 상태인 상대의 디지몬", "이 디지몬과 같은 표시 형식(액티브/레스트)인 상대의 디지몬");
        // BT26-089 "~. After, if removed from by effects, ~" — "또한, 효과로 줄어들었다면,"
        if ((m = Regex.Match(t, @"^(?<a>.+?\.) After, if removed from by effects, (?<b>.+)$", I83)).Success
            && RenderClause($"{m.Groups["a"].Value} Then, {m.Groups["b"].Value}") is { } kAfter
            && kAfter.IndexOf("그 후, ", StringComparison.Ordinal) is int at and > 0)
            return kAfter[..at] + "또한, 효과로 줄어들었다면, " + kAfter[(at + "그 후, ".Length)..];
        return null;
    }
}
