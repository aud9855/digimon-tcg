using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.36.0 미지원 문장 10차 묶음의 생성 한글 — 공식 한글 표기를 따른다:
/// "서로는 효과로 디지몬을 등장시킬 수 없다" (BT9-033) · "서로는 지불하는 등장 코스트를 마이너스할 수 없다" (BT8-071) ·
/// "상대는 옵션 카드를 사용할 수 없다" (EX1-072 · BT8-057) · "상대는 디지몬과 테이머를 효과로 등장시킬 수 없다" (BT20-020) ·
/// "상대는 트래시에서, 디지몬과 테이머를 효과로 등장시킬 수 없다" (BT23-014)
/// </summary>
public static partial class KoreanEffectRenderer
{
    private static string? SentenceKo36(string s, bool may)
    {
        s = s.Trim().TrimEnd('.');
        Match m;
        // 공식 BT22-028 "이 디지몬의 진화원에서 특징으로 「수생」을 포함하는, Lv.3과 Lv.4와 Lv.5의 디지몬 카드 1장씩을 코스트를 지불하지 않고 등장시킬 수 있다"
        if ((m = Regex.Match(s, @"^(?<may>you may )?play 1 each of (?<lv>level \d+(?:, level \d+)*,? and level \d+) (?<rest>.+?) from (?<from>this Digimon's digivolution cards|its digivolution cards|your trash|your hand|your hand or trash) without paying (?:the|its) (?:memory )?costs?$", IC)).Success)
        {
            bool mayPlay = may || m.Groups["may"].Success;
            string one = Regex.Replace(Regex.Replace(m.Groups["rest"].Value, @"\bcards\b", "card", IC), @"\bin any of their traits\b", "in any of its traits", IC);
            if (PickKo("1 " + one) is not { } pk || !pk.EndsWith("카드 1장", StringComparison.Ordinal)) return null;
            var lvNums = Regex.Matches(m.Groups["lv"].Value, @"\d+").Select(x => x.Value).ToList();
            string lvKo = string.Concat(lvNums.Select((x, i) => "Lv." + x + (i < lvNums.Count - 1 ? LvAnd36(x) + " " : "")));
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string fromKo = from.Contains("digivolution") ? "이 디지몬의 진화원에서" : from.Contains("hand") && from.Contains("trash") ? "자신의 패/트래시에서" : from.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
            string body = pk[..^"카드 1장".Length].Replace("디지몬 ", "").TrimEnd();
            return $"{fromKo} {body}, {lvKo}의 디지몬 카드 1장씩을 코스트를 지불하지 않고 {(mayPlay ? "등장시킬 수 있다" : "등장시킨다")}";
        }
        if (TamerDigimonKo36(s) is { } tamerKo) return tamerKo;
        if (s.EndsWith(" ⟦no-tamer-name⟧", StringComparison.Ordinal))   // "This effect can't play cards with the same name as any of your Tamers"
            return Sentence((may ? "You may " : "") + s[..^" ⟦no-tamer-name⟧".Length]) is { } playKo ? $"{playKo}. 이 효과로는 자신의 테이머와 같은 명칭의 카드는 등장시킬 수 없다" : null;
        if (Regex.Match(s, @"^(?<pre>.+) ⟦unsus⟧$", IC) is { Success: true } us36)   // 공식 BT21-096
            return Sentence(Cap(us36.Groups["pre"].Value)) is { } pre ? $"{pre}. 이 효과로는 액티브 상태인 상대의 디지몬에게도 어택할 수 있다" : null;
        if (may) return null;
        // 두 술어 (공식 BT22-060 "상대의 턴 종료까지 이 디지몬은 상대의 《퇴화》의 효과를 받지 않고, 이 디지몬의 뒷면의 진화원 1장마다 DP +1000.")
        if ((m = Regex.Match(s, @"^(?<dur>until [^,]+|for the turn), (?<a>this Digimon (?:isn't|is not) affected by [^,]+?) and (?:it )?(?<b>gets [+-]\d+ ?DP for (?:each|every) .+)$", IC)).Success)
            return Sentence($"{Cap(m.Groups["dur"].Value)}, {m.Groups["a"].Value}") is { } ka && ka.EndsWith("않는다", StringComparison.Ordinal)
                && Sentence($"This Digimon {m.Groups["b"].Value}") is { } kb ? ka[..^"않는다".Length] + "않고, " + kb : null;
        // 지속 (서로)
        if (Regex.IsMatch(s, @"^players can't play Digimon by effects?$", IC)) return "서로는 효과로 디지몬을 등장시킬 수 없다";
        if (Regex.IsMatch(s, @"^players can't reduce play costs?$", IC)) return "서로는 지불하는 등장 코스트를 마이너스할 수 없다";
        // 기간
        string? dur = null;
        string rest = s;
        if ((m = Regex.Match(s, @"^until (?:your opponent's|their) turn ends, (?<rest>.+)$", IC)).Success) { dur = "상대의 턴 종료 시까지"; rest = m.Groups["rest"].Value; }
        else if ((m = Regex.Match(s, @"^(?<rest>.+?) this turn$", IC)).Success) { dur = "이 턴 동안"; rest = m.Groups["rest"].Value; }
        else { var sd = SplitDuration(s); dur = sd.Ko; rest = sd.Core; }
        string? core = null;
        if ((m = Regex.Match(rest.Trim(), @"^your opponent can't play (?<what>Digimon|Tamers|Digimon or Tamers|Digimon and Tamers) by effects$", IC)).Success)
        {
            string what = m.Groups["what"].Value;
            bool dg = what.Contains("Digimon", StringComparison.OrdinalIgnoreCase), tm = what.Contains("Tamers", StringComparison.OrdinalIgnoreCase);
            core = dg && tm ? "상대는 디지몬과 테이머를 효과로 등장시킬 수 없다" : dg ? "상대는 효과로 디지몬을 등장시킬 수 없다" : "상대는 효과로 테이머를 등장시킬 수 없다";
        }
        else if (Regex.IsMatch(rest.Trim(), @"^(?:their|your opponent's) effects can't play Digimon or Tamers from (?:the|their) trash$", IC))
            core = "상대는 트래시에서, 디지몬과 테이머를 효과로 등장시킬 수 없다";
        else if (Regex.IsMatch(rest.Trim(), @"^your opponent can't use Option cards$", IC))
            core = "상대는 옵션 카드를 사용할 수 없다";
        if (core == null) return null;
        return dur != null ? $"{dur}, {core}" : core;
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// 절 전체: 진화할 때 비용을 내는 진화 코스트 감소 — 공식 BT17-048 "이 카드로 진화할 때, 테이머 5명까지를 레스트시키는 것으로, 이 효과로 레스트한 1명마다 지불하는 진화 코스트 -1." ·
    /// EX3-054 "이 카드로 진화할 때, 자신의 트래시에서 특징으로 「D-브리가드」를 가진 카드 5장까지를 덱 위로 되돌리는 것으로, 되돌린 1장마다 지불하는 진화 코스트 -1."
    /// </summary>
    private static string? ClauseKo36(string clause)
    {
        var norm = SentenceParser.EvoReductionText36(clause);
        if (ReferenceEquals(norm, clause) || norm == clause) return null;
        var m = Regex.Match(norm, @"^(?<labels>(?:\[[^\]]+\]\s*)*)When this Digimon would digivolve into this card, by (?<cost>[^,]+?), reduce the digivolution cost by (?<n>\d+) for each", IC);
        if (!m.Success || m.Groups["labels"].Value.Trim().Length > 0) return null;
        string cost = m.Groups["cost"].Value;
        string? costKo;
        string per;
        if (Regex.Match(cost, @"^suspending up to (?<n>\d+) Tamers$", IC) is { Success: true } st)
        { costKo = $"테이머 {st.Groups["n"].Value}명까지를 레스트시키는 것으로,"; per = "이 효과로 레스트한 1명마다"; }
        else
        {
            costKo = CostKo(cost);
            per = Regex.IsMatch(cost, @"^returning ", IC) ? "되돌린 1장마다" : Regex.IsMatch(cost, @"^trashing ", IC) ? "파기한 1장마다" : Regex.IsMatch(cost, @"^suspending ", IC) ? "레스트한 1장마다" : "1장마다";
        }
        return costKo == null ? null : $"이 카드로 진화할 때, {costKo.Replace("원하는 순서대로 ", "")} {per} 지불하는 진화 코스트 -{m.Groups["n"].Value}.";
    }
}

public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// 테이머를 디지몬으로도 취급 — 공식 BT13-008 "턴 종료까지 자신의 「최건우」 1명은 디지몬・DP 3000으로도 취급하며, 진화할 수 없다." ·
    /// BT13-018 "상대의 턴 종료까지 자신의 「최건우」 1명은 디지몬・DP 3000으로도 취급하며, 《블로커》를 얻고, 진화할 수 없다." · BT12-092 "이 테이머는 디지몬・DP 3000으로도 취급하며, 진화할 수 없다."
    /// </summary>
    private static string? TamerDigimonKo36(string s)
    {
        string? durKo = null;
        string rest = s;
        var lead = Regex.Match(s, @"^(?<d>for the turn|until the end of (?:your opponent's|their) turn|until (?:your opponent's|their) turn ends), (?<rest>.+)$", IC);
        if (lead.Success) { durKo = lead.Groups["d"].Value.Equals("for the turn", StringComparison.OrdinalIgnoreCase) ? "이 턴 동안" : "상대의 턴 종료 시까지"; rest = lead.Groups["rest"].Value; }
        else if (Regex.Match(s, @"^(?<rest>.+?) (?<d>for the turn|until the end of (?:your opponent's|their) turn|until (?:your opponent's|their) turn ends)$", IC) is { Success: true } trail)
        { durKo = trail.Groups["d"].Value.Equals("for the turn", StringComparison.OrdinalIgnoreCase) ? "이 턴 동안" : "상대의 턴 종료 시까지"; rest = trail.Groups["rest"].Value; }
        var m = Regex.Match(rest, @"^(?:(?<self>this Tamer)|(?:1|one) of your (?<name>\[[^\]]+\])s?|(?<all>all of your (?<name>\[[^\]]+\])s?)) (?:is|are) also treated as (?:an? )?(?<dp>\d+) DP Digimon(?<tail>.*)$", IC);
        if (!m.Success) return null;
        string tail = m.Groups["tail"].Value;
        var kws = Regex.Matches(tail, @"＜([^＞]+)＞").Select(x => KeywordKo(x.Groups[1].Value, withReminder: false)).ToList();
        if (kws.Any(k => k == null)) return null;
        bool noEvo = Regex.IsMatch(tail, @"\bcan't digivolve\b", IC);
        if (Regex.Replace(tail, @"＜[^＞]+＞|\bcan't digivolve\b|\bgains?\b|\bthat\b|\band\b|,", "", IC).Trim().Length > 0) return null;
        string subj = m.Groups["self"].Success ? "이 테이머는" : m.Groups["all"].Success ? $"자신의 {NamesKo(m.Groups["name"].Value)} 전부는" : $"자신의 {NamesKo(m.Groups["name"].Value)} 1명은";
        string dp = m.Groups["dp"].Value;
        string kwList = string.Concat(kws.Select((k, i) => k + (i < kws.Count - 1 ? J(k!, "과", "와") + " " : "")));
        string body = $"{subj} 디지몬・DP {dp}{J(dp, "으로", "로")}도 취급하며";
        body += kws.Count > 0 && noEvo ? $", {kwList}{J(kwList, "을", "를")} 얻고, 진화할 수 없다" : kws.Count > 0 ? $", {kwList}{J(kwList, "을", "를")} 얻는다" : noEvo ? ", 진화할 수 없다" : "";
        if (kws.Count == 0 && !noEvo) body = $"{subj} 디지몬・DP {dp}{J(dp, "으로", "로")}도 취급한다";
        return durKo != null ? $"{durKo}, {body}" : body;
    }

    /// <summary>"Lv.3과 Lv.4와 Lv.5" — 숫자 읽기(삼 · 사 · 오)의 받침에 따라 과/와</summary>
    private static string LvAnd36(string n) => n.Length > 0 && "2459".Contains(n[^1]) ? "와" : "과";

    private static string? CostKo36(string cost)
    {
        Match m;
        cost = cost.Trim();
        if ((m = Regex.Match(cost, @"^(?<a>trashing (?<pick>1 card|\d+ cards?) (?:in|from) your hand) or placing (?:it|them) (?<rest>face[- ]down under .+)$", IC)).Success)
            return CostKo($"{m.Groups["a"].Value} or placing {m.Groups["pick"].Value} from your hand {m.Groups["rest"].Value}");
        if ((m = Regex.Match(cost, @"^(?<a>placing (?<pick>.+?) from your hand) as (?<dest>(?:any|1|one) of your .+?bottom digivolution cards?) or (?<rest>face[- ]down under .+)$", IC)).Success)
            return CostKo($"{m.Groups["a"].Value} as {m.Groups["dest"].Value} or placing {m.Groups["pick"].Value} from your hand {m.Groups["rest"].Value}");
        // "placing 1 each of level 4 and level 5 ~ from your trash as 1 of your [Kudamon]'s bottom digivolution cards" (EX13-071)
        if ((m = Regex.Match(cost, @"^placing 1 each of (?<lv>level \d+(?:, level \d+)*,? and level \d+) (?<rest>.+?) from (?<from>your trash|your hand) (?<dest>as .+)$", IC)).Success)
        {
            var lvs = Regex.Matches(m.Groups["lv"].Value, @"\d+").Select(x => x.Value).ToList();
            string one = Regex.Replace(m.Groups["rest"].Value, @"\bcards\b", "card", IC);
            if (CostKo($"placing 1 level {lvs[0]} {one} from {m.Groups["from"].Value} {m.Groups["dest"].Value}") is not { } single
                || !single.Contains($"Lv.{lvs[0]}인 ", StringComparison.Ordinal) || !single.Contains("카드 1장을", StringComparison.Ordinal)) return null;
            string lvList = string.Concat(lvs.Select((x, i) => "Lv." + x + (i < lvs.Count - 1 ? LvAnd36(x) + " " : "")));
            string replaced = single.Replace($"Lv.{lvs[0]}인 ", "");
            return replaced.Contains("디지몬 카드 1장을", StringComparison.Ordinal) ? replaced.Replace("디지몬 카드 1장을", $"{lvList}의 디지몬 카드 1장씩을") : replaced.Replace("카드 1장을", $"{lvList}의 카드 1장씩을");
        }
        // "A and B" — 둘 다 (SentenceParser.BothCosts36과 같은 읽기): "~하고, ~하는 것으로,"
        foreach (Match am in Regex.Matches(cost, @" and (?=(?:placing|trashing|returning|suspending|deleting|adding) )", IC))
        {
            string a = cost[..am.Index].Trim(), b = cost[(am.Index + am.Length)..].Trim();
            if (!Regex.IsMatch(a, @"^[a-z]+ing ", IC) || CostKo(a) is not { } ka || CostKo(b) is not { } kb || !ka.EndsWith("는 것으로,", StringComparison.Ordinal)) continue;
            return $"{ka[..^"는 것으로,".Length]}고, {kb}";
        }
        // 공식 BT17-048 진화원: "자신의 「리즈무」 1명을 레스트시키는 것으로,"
        if ((m = Regex.Match(cost, @"^suspending (?:1|one) of your (?<name>\[[^\]]+\])s?$", IC)).Success)
        {
            string n = NamesKo(m.Groups["name"].Value);
            return $"자신의 {n} 1명을 레스트시키는 것으로,";
        }
        return null;
    }
}
