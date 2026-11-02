using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.80.0 미지원 문장 54차 묶음 — 한글 표시 (원래 규칙이 옮기지 못한 절만)</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I80 = RegexOptions.IgnoreCase;

    /// <summary>v0.80.0 (BT26-078 · BT23-024 · BT16-015 · BT7-112 · BT10-077)</summary>
    private static string? RenderClause80(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // BT7-112 "By placing 10 Tamer cards and/or cards with the [Hybrid] trait from your hand and/or trash at the bottom of your deck in any order, you may digivolve this card from your hand onto one of your Tamers as if the Tamer is a level 6 Digimon."
        if ((m = SentenceParser.TamerEvoPlaceRe80.Match(t)).Success)
            return $"자신의 패 또는 트래시에서 테이머 카드 또는 특징으로 「{TraitKo(m.Groups["t"].Value)}」를 가진 카드 합계 {m.Groups["n"].Value}장을 원하는 순서대로 덱 아래로 되돌리는 것으로, "
                   + $"패의 이 카드는 자신의 테이머를 Lv.{m.Groups["lv"].Value}의 디지몬으로서 취급하여 진화할 수 있다.";
        // BT16-015 "[Your Turn] While ~, attach [End of Attack] to all of this Digimon's [On Deletion] effects."
        if ((m = SentenceParser.AttachEndOfAttackRe80.Match(t)).Success
            && RenderClause($"{m.Groups["h"].Value}While {m.Groups["cond"].Value}, this Digimon gains ＜Blocker＞.") is { } k15
            && k15.IndexOf("이 디지몬은 《블로커》", StringComparison.Ordinal) is int i15 and > 0)
            return k15[..i15] + "이 디지몬의 【소멸 시】 효과 전부에 【어택 종료 시】를 추가한다.";
        // BT10-077 "~ your opponent trashes cards from their hand equal to the number of cards added to their hand by the effect."
        if ((m = Regex.Match(t, @"^(?<a>.+?, )your opponent trashes cards from their hand equal to the number of cards added to their hand by the effect\.?$", I80)).Success
            && RenderClause($"{m.Groups["a"].Value}your opponent trashes 1 card in their hand.") is { } k77
            && k77.EndsWith("상대는 자신의 패를 1장 파기한다.", StringComparison.Ordinal))
            return k77[..^"상대는 자신의 패를 1장 파기한다.".Length] + "상대는 그 효과로 늘어난 장수만큼 자신의 패를 파기한다.";
        // BT23-024 "other than their highest play cost Digimon, none of your opponent's Digimon can suspend until their turn ends"
        if ((m = Regex.Match(t, @"^(?<a>.+?, )other than their highest play cost Digimon, (?<b>none of your opponent's Digimon can suspend .+)$", I80)).Success
            && RenderClause(m.Groups["a"].Value + m.Groups["b"].Value) is { } k24
            && k24.Contains("상대의 디지몬 전부는", StringComparison.Ordinal))
            return k24.Replace("상대의 디지몬 전부는", "가장 등장 코스트가 높은 상대의 디지몬 이외의 상대의 디지몬 전부는");
        // BT26-078 "When any of your ~ Digimon are played, ~, 1 of them gains ~" — 등장한 디지몬 1마리
        if (Regex.IsMatch(t, @"\bWhen any of your [^.]*?Digimon are played,[^.]*?, 1 of them gains\b", I80)
            && SentenceParser.Rewrite80(t) is { } alt
            && RenderClause(alt) is { } k78
            && k78.Contains("그 디지몬은", StringComparison.Ordinal))
            return k78.Replace("그 디지몬은", "등장한 디지몬 1마리는");
        return null;
    }
}
