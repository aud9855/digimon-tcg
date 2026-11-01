using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.56.0 미지원 문장 30차 묶음: 원래 규칙(1~29차)이 읽지 못한 절만 30차 규칙(같은 뜻 다른 표기)으로 다시 읽는다.
///  - 손패의 이 카드의 코스트 감소 (BT8-097 · BT2-099) · 지불하는 등장 코스트 감소 (BT13-056 · EX11-034)
///  - "for each one" (EX5-074) · "To 1 of ~, give -N DP for each card this effect trashed" (EX12-030)
///  - BT8-111 · EX13-034 · EX13-057 · BT3-105 · EX12-015 · RB1-035 · 한 줄에 두 효과 (RB1-10 · EX13-077)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch56.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth56;

    /// <summary>절 하나에 효과가 둘 적혀 있는 것 (데이터에 줄바꿈이 빠짐) — 나눈 두 절</summary>
    internal static (string First, string Second)? SplitTwoEffects56(string rawClause)
    {
        string t = rawClause.Trim();
        // RB1-10 "~ this Digimon's DP. [Your Turn] (Once Per Turn) When an opponent’s Digimon is deleted, unsuspend this Digimon."
        var m = Regex.Match(t, @"^(?<a>\[When Digivolving\].+?\.)\s+(?<b>\[Your Turn\]\s*\(Once Per Turn\)\s*When an opponent[’']s Digimon is deleted, unsuspend this Digimon\.?)$", I);
        if (m.Success)
            return (m.Groups["a"].Value, Regex.Replace(m.Groups["b"].Value.Replace('’', '\''), @"\(Once Per Turn\)", "[Once Per Turn]", I));
        // EX13-077 "[All Turns] This Digimon gains all colors in its digivolution cards, and +1000 DP for each of your Digimon and Tamers' colors."
        m = Regex.Match(t, @"^\[All Turns\]\s*This Digimon gains all colors in its digivolution cards, and \+(?<n>\d+) DP for each of your Digimon and Tamers' colors\.?$", I);
        if (m.Success)
            return ("[All Turns] This Digimon gains all colors in its digivolution cards.", $"[All Turns] This Digimon gets +{m.Groups["n"].Value} DP for each color your Digimon and Tamers have.");
        return null;
    }

    private static string? TryParse56(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse55(rawClause, inherited, card, def);
        if (err == null || _clauseDepth56 > 0) return err;
        _clauseDepth56++;
        try
        {
            if (SplitTwoEffects56(rawClause) is { } two)
            {
                if (TryParse55(two.First, inherited, card, null) != null || TryParse55(two.Second, inherited, card, null) != null) return err;
                if (def != null) { TryParse55(two.First, inherited, card, def); TryParse55(two.Second, inherited, card, def); }
                return null;
            }
            string alt = ClauseRewrite56(rawClause);
            if (alt == rawClause || TryParse55(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse55(alt, inherited, card, def);
            return null;
        }
        finally { _clauseDepth56--; }
    }

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules56 =
    {
        // BT8-097 · BT2-099 "Reduce the memory cost of this card in your hand by 1 for each ~." (옵션 — 사용 코스트)
        s => Regex.Replace(s, @"^Reduce the memory cost of this card in your hand by (?<n>\d+) for each (?<x>[^.]+)\.?$", "When you would use this card, reduce its cost by ${n} for each ${x}.", RegexOptions.IgnoreCase),
        // BT13-056 "from your hand for its play cost. When played by this effect, reduce the play cost by 4."
        s => Regex.Replace(s, @"\bfrom your hand for its play cost\. When played by this effect, reduce the play cost by (?<n>\d+)\.", "from your hand with the play cost reduced by ${n}.", RegexOptions.IgnoreCase),
        // EX11-034 "from your hand. For each of your face-up security cards, reduce this effect's paid play cost by 1."
        s => Regex.Replace(s, @"\bfrom your hand\. For each of your face-up security cards, reduce this effect's paid play cost by (?<n>\d+)\.", "from your hand with the play cost reduced by ${n} for each of your face-up security cards.", RegexOptions.IgnoreCase),
        // EX5-074 "By returning ~ to the bottom of the deck, for each one, all your opponent's Digimon get -4000 DP for the turn."
        s => Regex.Replace(s, @"\bto the bottom of the deck, for each one, all (?:of )?your opponent's Digimon get (?<dp>-\d+) DP for the turn\.", "to the bottom of the deck, all of your opponent's Digimon get ${dp} DP for the turn for each card returned.", RegexOptions.IgnoreCase),
        // EX12-030 "You may trash up to 3 cards from your hand. To 1 of your opponent's Digimon, give -2000 DP for the turn for each card this effect trashed."
        s => Regex.Replace(s, @"\bYou may trash up to (?<n>\d+) cards from your hand\. To 1 of your opponent's Digimon, give (?<dp>-\d+) DP for the turn for each card this effect trashed\.", "By trashing up to ${n} cards from your hand, 1 of your opponent's Digimon gets ${dp} DP for the turn for each card trashed.", RegexOptions.IgnoreCase),
        // BT8-111 "For every 10 cards in your trash, trash the top 3 cards of your opponent's deck, and this Digimon gets +3000 DP for the turn." (DP는 1번)
        s => Regex.Replace(s, @"\b(?<a>For every \d+ cards in your trash, trash the top \d+ cards of your opponent's deck), and this Digimon gets (?<dp>\+\d+) DP for the turn\.", "${a}. Then, this Digimon gets ${dp} DP for the turn.", RegexOptions.IgnoreCase),
        // EX13-034 ", and their ＜De-Digivolve＞ effects don't affect it."
        s => Regex.Replace(s, @"\), and their ＜De-Digivolve＞ effects don't affect it\.$", "), and isn't affected by ＜De-Digivolve＞ effects.", RegexOptions.IgnoreCase),
        // EX13-057 ". If during an attack, it also isn't affected by their Digimon effects and gets +5000 DP." (기간은 문장 앞의 "Until your opponent's turn ends")
        s => Regex.Replace(s, @"^(?<a>.*\bUntil your opponent's turn ends, .+?\.)\s*If during an attack, it also isn't affected by their Digimon effects and gets (?<dp>\+\d+) DP\.$",
            "${a} Then, if during an attack, that Digimon isn't affected by your opponent's Digimon effects and gets ${dp} DP until your opponent's turn ends.", RegexOptions.IgnoreCase),
        // BT3-105 "gains ＜Reboot＞ (~) and "This Digimon can't have its DP reduced or be returned to its owner's hand or deck" until the end of your opponent's next turn."
        s => Regex.Replace(s, @"^\[Main\] 1 of your Digimon gains ＜Reboot＞(?: \([^)]*\))? and ""This Digimon can't have its DP reduced or be returned to its owner's hand or deck"" until the end of your opponent's next turn\.$",
            "[Main] 1 of your Digimon gains ＜Reboot＞ until the end of your opponent's next turn. Then, that Digimon can't have its DP reduced and can't be returned to its owner's hand or deck until the end of your opponent's next turn.", RegexOptions.IgnoreCase),
        // EX12-015 "may gain ＜Alliance＞ (~) and attack." — 이 턴 동안
        s => Regex.Replace(s, @"\bmay gain ＜Alliance＞(?: \((?:[^()]|\([^)]*\))*\))? and attack\.$", "may gain ＜Alliance＞ for the turn and attack.", RegexOptions.IgnoreCase),
        // RB1-035 "by suspending this Tamer, gain 1 memory if that Digimon is level 4 or higher, and ＜Draw 1＞ if it is level 3."
        s => Regex.Replace(s, @"\bby suspending this Tamer, gain (?<m>\d+) memory if that Digimon is level 4 or higher, and ＜Draw (?<d>\d+)＞ if it is level 3\.$",
            "by suspending this Tamer, if that Digimon is level 4 or higher, gain ${m} memory. Then, if that Digimon is level 3 or lower, ＜Draw ${d}＞.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite56(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules56) s = f(s);
        return s;
    }
}
