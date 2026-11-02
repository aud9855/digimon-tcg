using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.84.0 58차 묶음 — 한글 표시 (원래 규칙이 읽지 못한 절만, SentenceParser.Batch84와 같은 절):
/// EX9-062 어셈블리 Lv. 취급 · BT19-063 주인 없는 소멸 · EX13-045 어택 → DP → 배틀 · EX13-035 · BT19-102
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>앞말의 받침에 맞는 조사 (어셈블리 조건 한글 — GoldfishEngine.Assembly84)</summary>
    internal static string J84(string word, string withBatchim, string withoutBatchim) => J(word, withBatchim, withoutBatchim);

    private static string? RenderClause84(string rawClause)
    {
        string t = Regex.Replace(rawClause.Trim(), @"\s+", " ");
        Match m;
        if ((m = SentenceParser.AsmLevelRe84.Match(t)).Success)
            return $"이 카드는 「{NameKo(m.Groups["x"].Value)}」의 어셈블리에서 Lv.{m.Groups["lv"].Value}로도 취급한다.";
        // BT19-063 "~. Then, if DigiXrosing with 2 cards, you may delete 1 play cost 3 or lower Digimon or Tamer."
        if ((m = Regex.Match(t, @"^(?<pre>.+?) Then, if DigiXrosing with (?<k>\d+) cards, you may delete (?:1|one) play cost (?<n>\d+) or (?:lower|less) Digimon or Tamer\.$", RegexOptions.IgnoreCase)).Success
            && RenderClause(m.Groups["pre"].Value) is { } pre)
            return $"{pre} 그 후, {m.Groups["k"].Value}장으로 디지크로스하고 있었다면, 자신 또는 상대의 등장 코스트 {m.Groups["n"].Value} 이하의 디지몬/테이머 1장을 소멸시킬 수 있다.";
        if (t == SentenceParser.Ex13045Api84)
            return "【진화 시】 조그레스 진화했다면, 이 디지몬으로 어택하고, 상대의 턴 종료 시까지 자신의 디지몬 전부의 DP를 +10000 한다. 그 후, 이 디지몬은 상대의 디지몬 1마리와 배틀할 수 있다.";
        if (t == SentenceParser.Ex13035Api84)
        {
            string names = $"「{NameKo("Chuumon")}」/「{NameKo("Sukamon")}」/「{NameKo("Etemon")}」";
            return $"【등장 시】 【진화 시】 자신의 패/트래시에서 명칭에 {names}{J(names, "을", "를")} 포함하는 디지몬 카드 2장까지를 등장 코스트 합계 6까지 코스트를 지불하지 않고 등장시킬 수 있다. "
                + $"자신의 트래시에서 명칭에 {names}{J(names, "을", "를")} 포함하는 카드 10장을 덱 아래로 되돌리는 것으로, 이 효과의 등장 코스트 상한 +6.";
        }
        if (t == SentenceParser.Bt19102Api84)
            return "【등장 시】 【진화 시】 다른 디지몬 1마리를 고른다. 그 디지몬의 진화원에서 Lv.4 이하의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시키는 것으로, 고른 디지몬을 소멸시킨다.";
        return null;
    }
}
