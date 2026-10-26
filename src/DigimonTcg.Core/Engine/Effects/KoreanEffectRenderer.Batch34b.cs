using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.34.0 8차 묶음 (2)의 생성 한글 — 공식 표기: "자신의 디지몬 1마리를 선택한다. 진화원 매수가 선택한 디지몬 이하의 ~" (BT14-091) ·
/// "자신의 트래시의 특징으로 「사이보그형」 또는 「머신형」을 가진 카드 1장마다 이 디지몬을 DP+1000" (EX2-036) · "자신의 테이머 아래에서 「슈팅스타몬」 1장을 코스트를 지불하지 않고 등장시킬 수 있다" (BT19-031)
/// </summary>
public static partial class KoreanEffectRenderer
{
    private static (Match M, string Ko)? FilterKoRef34(string rest)
    {
        Match m;
        string Who(string w) => w.Contains("chosen", StringComparison.OrdinalIgnoreCase) ? "선택한 디지몬" : "그 디지몬";
        if ((m = Regex.Match(rest, @"^with (?:DP less than or equal to (?:the DP of )?|as much or less DP (?:as|than) )(?<w>the chosen Digimon(?:'s DP)?|it\b)", RegexOptions.IgnoreCase)).Success)
            return (m, $"{Who(m.Groups["w"].Value)}의 DP 이하인");
        if ((m = Regex.Match(rest, @"^with (?:as many or fewer|equal or fewer) digivolution cards (?:as|at|than) (?<w>that Digimon|it\b|the chosen Digimon)", RegexOptions.IgnoreCase)).Success)
            return (m, $"진화원 매수가 {Who(m.Groups["w"].Value)} 이하인");
        if ((m = Regex.Match(rest, @"^with (?:a level equal to or lower than|as high or lower a level as|a level less than or equal to) (?<w>that Digimon|it\b|the chosen Digimon)(?:'s level)?", RegexOptions.IgnoreCase)).Success)
            return (m, $"{Who(m.Groups["w"].Value)}의 Lv. 이하인");
        if ((m = Regex.Match(rest, $@"^(?:with |and )?(?<n>\d+) or more (?<x>{TargetSpec.NameList}) (?:digivolution cards|in (?:its|their) digivolution cards)", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return (m, $"진화원에 {x}{J(x.TrimEnd('」'), "이", "가")} {m.Groups["n"].Value}장 이상 있는"); }
        return null;
    }

    private static string? CounterKo34(string counter)
    {
        Match m;
        string c0 = counter.Trim();
        string Per(Match x) => x.Groups["n"].Success && x.Groups["n"].Value.Length > 0 ? x.Groups["n"].Value : "1";
        if (Regex.IsMatch(c0, @"^colou?r in (?:its|this Digimon's) digivolution cards$", RegexOptions.IgnoreCase)) return "이 디지몬의 진화원의 색 1색마다";
        if (Regex.IsMatch(c0, @"^of (?:its|this Digimon's) colou?rs$", RegexOptions.IgnoreCase)) return "이 디지몬의 색 1색마다";
        if (Regex.IsMatch(c0, @"^of its digivolution cards$", RegexOptions.IgnoreCase)) return "이 디지몬의 진화원 1장마다";
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?(?<pick>.+?) in (?:its|this Digimon's) digivolution cards$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + m.Groups["pick"].Value) is { } sk)
            return $"이 디지몬의 진화원의 {Regex.Replace(sk, @"\s*1장$", "")} {Per(m)}장마다";
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?(?<pick>.+?) in your trash$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + m.Groups["pick"].Value) is { } tk)
            return $"자신의 트래시의 {Regex.Replace(tk, @"\s*1장$", "")} {Per(m)}장마다";
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?card in your trash (?<with>with .+)$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 card " + m.Groups["with"].Value) is { } wk)
            return $"자신의 트래시의 {Regex.Replace(wk, @"\s*1장$", "")} {Per(m)}장마다";
        if (Regex.IsMatch(c0, @"^other Digimon you have in play with the same name as this Digimon$", RegexOptions.IgnoreCase))
            return "이 디지몬과 같은 명칭의 다른 자신의 디지몬 1마리마다";
        if ((m = Regex.Match(c0, $@"^(?<x>{TargetSpec.NameList}) you have(?: in play)?$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 {NamesKo(m.Groups["x"].Value)} 1마리마다";
        if ((m = Regex.Match(c0, @"^of your (?<other>other )?(?<spec>(?:[^,]*? )?Digimon(?: with .+)?)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + (m.Groups["other"].Success ? "other " : "") + m.Groups["spec"].Value, bare: true) is { } dk)
            return $"{dk} 1마리마다";
        if (Regex.IsMatch(c0, @"^of your Digimon and Tamers(?: in play)?$", RegexOptions.IgnoreCase)) return "자신의 디지몬과 테이머 1장마다";
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?suspended Digimon$", RegexOptions.IgnoreCase)).Success)
            return $"레스트 상태인 디지몬 {Per(m)}마리마다";
        return null;
    }

    private static string? SentenceKo34b(string s, bool may)
    {
        Match m;
        // 공식 (BT15-031): "이 디지몬은 화이트인 디지몬으로만 진화할 수 있다"
        if ((m = Regex.Match(s, @"^this Digimon can only digivolve into (?<what>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            string what = m.Groups["what"].Value.Trim();
            string? wk = Regex.Match(what, $@"^(?<x>{TargetSpec.NameList})$") is { Success: true } nx ? NamesKo(nx.Groups["x"].Value)
                : Regex.Match(what, $@"^(?<c>{Colors}) Digimon(?: cards?)?$", RegexOptions.IgnoreCase) is { Success: true } cx ? $"{ColorKo(cx.Groups["c"].Value)}인 디지몬"
                : Regex.Match(what, $@"^Digimon cards? with (?<x>{TargetSpec.NameList}) in (?:its|their) names?$", RegexOptions.IgnoreCase) is { Success: true } inx ? $"명칭에 {NamesKo(inx.Groups["x"].Value)}{J(NamesKo(inx.Groups["x"].Value), "을", "를")} 포함하는 디지몬 카드" : null;
            return wk == null ? null : $"이 디지몬은 {wk}{J(wk.TrimEnd('」'), "으로", "로")}만 진화할 수 있다";
        }
        // "you may trash 2 cards in your hand to end the attack" — 비용 + 어택 종료
        if ((m = Regex.Match(s, @"^(?<verb>trash|suspend|delete|return|place)\b(?<obj>[^,]*?) to (?<rest>end (?:the|that) attack)$", RegexOptions.IgnoreCase)).Success)
            return Sentence($"By {Gerund(m.Groups["verb"].Value)}{m.Groups["obj"].Value}, {m.Groups["rest"].Value}");
        // "Choose 1 of your Digimon with [Insect] in any of its traits" — 공식 (BT14-091): "자신의 디지몬 1마리를 선택한다"
        if ((m = Regex.Match(s, @"^Choose (?<t>(?:1|one) of your .+)$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } ct)
            return $"{ct}{J(ct, "을", "를")} {(may ? "선택할 수 있다" : "선택한다")}";
        // "Play 1 [ShootingStarmon] from under your Tamers without paying the cost" — 공식 (BT19-031)
        if ((m = Regex.Match(s, @"^Play (?<pick>.+?) from under (?:one of |any of |1 of )?your Tamers without paying (?:the|its) (?:memory )?costs?$", RegexOptions.IgnoreCase)).Success && PickKo(m.Groups["pick"].Value) is { } pk)
            return $"자신의 테이머 아래에서 {pk}{J(pk, "을", "를")} 코스트를 지불하지 않고 {(may ? "등장시킬 수 있다" : "등장시킨다")}";
        // 테이머도 고르는 대상: "Delete 1 of your opponent's play cost 3 or lower Digimon or Tamers" · "Return 1 of your opponent's Tamers to its owner's hand"
        if ((m = Regex.Match(s, @"^(?<v>Delete|Suspend|Return) (?<t>(?:1|one|up to \d+|all) of (?:your opponent's|their) .*?\b(?:Digimon or Tamers|Tamers)\b.*?)(?<to> to (?:the|its owner's|their owners'?|their|the owner's) hands?| to the bottom of (?:the|its owner's|their owners'?|the owner's) decks?)?$", RegexOptions.IgnoreCase)).Success)
        {
            string t = Regex.Replace(m.Groups["t"].Value, @"\bplay cost (?:of )?(?<n>\d+) or (?<d>lower|less) (?<k>Digimon or Tamers|Tamers)$", "${k} with a play cost of ${n} or less", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\btheir\b", "your opponent's", RegexOptions.IgnoreCase);
            if (TargetKo(t) is not { } tk) return null;
            string v = m.Groups["v"].Value.ToLowerInvariant();
            string verb = v == "delete" ? (may ? "소멸시킬 수 있다" : "소멸시킨다") : v == "suspend" ? (may ? "레스트시킬 수 있다" : "레스트시킨다")
                : m.Groups["to"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? (may ? "패로 되돌릴 수 있다" : "패로 되돌린다") : (may ? "덱 아래로 되돌릴 수 있다" : "덱 아래로 되돌린다");
            return $"{tk}{J(tk, "을", "를")} {verb}";
        }
        return null;
    }
}
