using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.77.0 미지원 문장 51차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I77 = RegexOptions.IgnoreCase;

    /// <summary>v0.77.0 원래 규칙이 옮기지 못한 절만 (BT12-112 · BT21-030 · BT10-111 · EX6-006 · BT12-089 · BT26-097)</summary>
    private static string? RenderClause77(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // BT12-112 · BT21-030 "When you would play this card, by placing 1 [Shoutmon] ~ under this Digimon, reduce the cost by 1 and ~ cards in your trash ~ for a DigiXros."
        //  — 공식 한글 문형 ("이 카드가 등장할 때, ~ 놓는 것으로, 지불하는 등장 코스트 -N 하고, ~ 트래시의 카드도 디지크로스로 놓을 수 있다")
        if ((m = SentenceParser.XrosFieldCostRe77.Match(t)).Success)
            return $"이 카드가 등장할 때, 자신의 {NamesKo(m.Groups["x"].Value)} 1마리를 이 카드 아래에 놓는 것으로, 지불하는 등장 코스트를 -{m.Groups["n"].Value} 하고, 자신의 트래시의 카드도 디지크로스로 놓을 수 있다.";
        // BT10-111 "[On Play] You may return 1 card with a DigiXros requirement from your trash to your hand. When you would DigiXros this turn, this Digimon may replace 1 of the DigiXros requirements."
        if ((m = Regex.Match(t, @"^(?<h>(?:\s*\[[^\]]+\])+)\s*You may return 1 card with (?:a )?DigiXros requirements? from your trash to (?:your|the) hand\. When you would DigiXros this turn, this Digimon may replace 1 of the DigiXros requirements\.?$", I77)).Success
            && Header76(m.Groups["h"].Value) is { } h111)
            return $"{h111} 자신의 트래시에서 디지크로스 조건을 가진 카드 1장을 패로 되돌릴 수 있다. 이 턴 동안 자신이 디지크로스할 때, 이 디지몬은 디지크로스 조건 1장을 대신할 수 있다.";
        // EX6-006 "~ By deleting this Digimon with 7 or more cards with different names in its digivolution cards, ~"
        if ((m = Regex.Match(t, @"^(?<a>.*?\b[Bb]y )deleting this Digimon with (?<n>\d+) or more cards with different names in its digivolution cards, (?<b>.+)$", I77)).Success
            && RenderClause($"{m.Groups["a"].Value}deleting this Digimon, {m.Groups["b"].Value}") is { } k6 && k6.Contains("이 디지몬을 소멸시키는 것으로", StringComparison.Ordinal))
            return k6.Replace("이 디지몬을 소멸시키는 것으로", $"진화원에 명칭이 서로 다른 카드가 {m.Groups["n"].Value}장 이상 있는 이 디지몬을 소멸시키는 것으로");
        // EX6-029 "~. Then, if DNA digivolving, place 1 other Digimon at the bottom of its owner's security stack, and trash cards from the top of your opponent's security stack until it has 4 left."
        //  — 공식 한글 문형 ("~ 시큐리티 아래에 놓고, 상대의 시큐리티가 N장이 되도록 위에서부터 파기한다")
        if ((m = Regex.Match(t, @"^(?<a>.+?\. Then, if DNA digivolving, place 1 other Digimon) at the bottom of its owner's security stack, and trash cards from the top of your opponent's security stack until it has (?<n>\d+) left\.?$", I77)).Success
            && RenderClause(m.Groups["a"].Value + " as the bottom security card.") is { } k29 && k29.EndsWith("놓는다.", StringComparison.Ordinal))
            return $"{k29[..^"놓는다.".Length]}놓고, 상대의 시큐리티가 {m.Groups["n"].Value}장이 되도록 위에서부터 파기한다.";
        // BT12-089 · BT26-097 · BT13-055 · ST10-06 — 같은 뜻 다른 표기 (SentenceParser.Rewrite77)
        if (SentenceParser.Rewrite77(t) is { } alt && !alt.Contains('⟦'))
            return RenderClause(alt);
        return null;
    }
}
