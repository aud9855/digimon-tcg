using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.78.0 미지원 문장 52차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I78 = RegexOptions.IgnoreCase;

    /// <summary>v0.78.0 원래 규칙이 옮기지 못한 절만 (BT17-102 · BT17-050 · BT25-038 · EX13-076)</summary>
    private static string? RenderClause78(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // BT17-102 "[All Turns] This Digimon has all the names of level 3 and lower cards in its digivolution cards."
        if ((m = SentenceParser.SourceNamesRe78.Match(t)).Success)
            return $"【서로의 턴】 이 디지몬은 이 디지몬의 진화원에 있는 Lv.{m.Groups["lv"].Value} 이하의 카드의 명칭 전부를 얻는다.";
        // BT17-050 "~, you may suspend 1 of your opponent's Digimon and attack an opponent's Digimon with the Digimon this was placed under."
        if ((m = Regex.Match(t, @"^(?<a>.+?, you may suspend 1 of your opponent's Digimon) and attack an opponent's Digimon with the Digimon this was placed under\.?$", I78)).Success
            && RenderClause(m.Groups["a"].Value + ".") is { } k50 && k50.EndsWith("레스트시킬 수 있다.", StringComparison.Ordinal))
            return k50[..^"레스트시킬 수 있다.".Length].Replace(" 지불하고, 이 디지몬을 ", " 지불하고, 이 카드를 ") + "레스트시키고, 이 카드를 놓은 디지몬으로 상대의 디지몬에게 어택할 수 있다.";
        // BT25-038 "~ from your hand or your Digimon's digivolution cards as the top or bottom security card. ~"
        if ((m = Regex.Match(t, @"^(?<a>.+?) from your hand or your Digimon's digivolution cards (?<b>as the (?:top or bottom|top|bottom) security card.*)$", I78)).Success
            && RenderClause($"{m.Groups["a"].Value} from your hand {m.Groups["b"].Value}") is { } k38 && k38.Contains("자신의 패에서", StringComparison.Ordinal))
            return k38.Replace("자신의 패에서", "자신의 패 또는 자신의 디지몬의 진화원에서");
        // EX13-076 "~. Then, you may return all digivolution cards of 1 of their Digimon to the bottom of the deck and have this Digimon battle it. Compare the number of digivolution cards instead of DP in this battle."
        if ((m = Regex.Match(t, @"^(?<a>.+?)\. Then, you may return all (?:of the )?digivolution cards of 1 of (?:their|your opponent's) Digimon to the bottom of (?:the|its owner's) deck and have this Digimon battle it\. Compare the number of digivolution cards instead of DP in this battle\.?$", I78)).Success
            && RenderClause(m.Groups["a"].Value + ".") is { } k76)
            return $"{k76} 그 후, 상대의 디지몬 1마리의 진화원을 전부 덱 아래로 되돌리고, 이 디지몬과 배틀시킬 수 있다. 이 배틀에서는 DP 대신 진화원의 매수를 비교한다.";
        return null;
    }
}
