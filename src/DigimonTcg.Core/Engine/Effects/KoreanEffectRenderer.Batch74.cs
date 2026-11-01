using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.74.0 미지원 문장 48차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// 한글 특징 포함 목록 — "특징으로 · 특징에 「A」/「B」를 포함하는 · 포함하거나",
    /// 제외는 앞 "를 (「C」는 제외) 포함하는" · 뒤 "를 포함(「C」는 제외)하는" 두 꼴
    /// </summary>
    private static readonly Regex KoContains74 = new(
        @"특징(?:으로|에) (?<l>「[^」]+」(?:/「[^」]+」)*)[을를](?: \((?<ex1>「[^」]+」(?:/「[^」]+」)*)[은는] 제외\))? 포함(?:\((?<ex2>「[^」]+」(?:/「[^」]+」)*)[은는] 제외\))?(?<v>하는|하거나)",
        RegexOptions.Compiled);

    /// <summary>
    /// 특징 포함(종합 룰 2-3-2-4)에서 영어 특징 묶음 → 공식 한글 카드가 쓰는 부분 낱말.
    /// 영어 특징 이름과 한글 특징 이름의 부분 낱말이 다르다 — [Avian] · [Bird] → 「조」/「새」/「병아리」 (공식 EX4-064 · BT13-094 · P-133 등),
    /// [Beast] · [Animal] · [Sovereign] → 「수」/「짐승」 (공식 BT13-048 · RB1-034), [Aqua] · [Sea Animal] → 「수생」 (공식 BT19-082 등),
    /// [Vegetation] · [Plant] → 「식물형」 (공식 BT10-046 · BT18-050), [Dragon] · [saur] · [Ceratopsian] → 「용」/「룡」 (공식 EX3-003),
    /// 천사 6종 → 「천사형」 (공식 EX3-028), [Insect] → 「곤충형」 (공식 BT11-102).
    /// 한 묶음 안의 순서는 상관없다 (BT13-089 "[Bird] or [Avian]")
    /// </summary>
    private static readonly (string[] En, string[] Ko)[] ContainsGloss74 =
    {
        (new[] { "Angel", "Cherub", "Throne", "Authority", "Seraph", "Virtue" }, new[] { "천사형" }),
        (new[] { "Beast", "Animal", "Sovereign" }, new[] { "수", "짐승" }),
        (new[] { "Dragon", "saur", "Ceratopsian" }, new[] { "용", "룡" }),
        (new[] { "Avian", "Bird" }, new[] { "조", "새", "병아리" }),
        (new[] { "Aqua", "Sea Animal" }, new[] { "수생" }),
        (new[] { "Vegetation", "Plant" }, new[] { "식물형" }),
        (new[] { "Bird" }, new[] { "조", "새", "병아리" }),
        (new[] { "Insect" }, new[] { "곤충형" }),
    };

    /// <summary>목록 전체가 정해진 표기 — BT18-054 "[Avian], [Bird] or [Fairy]" (공식 「조」/「새」/「병아리」/「요정」)</summary>
    private static readonly (string[] En, string[] Ko)[] ContainsWhole74 =
    {
        (new[] { "Avian", "Bird", "Fairy" }, new[] { "조", "새", "병아리", "요정" }),
    };

    /// <summary>「수」를 포함하는 목록의 [Sea Animal] 제외 — 공식 BT13-048 · BT15-001 · RB1-034 (「수」가 들어간 다른 특징도 뺀다)</summary>
    private static readonly string[] SeaAnimalExcludeKo74 = { "수장룡형", "수생형", "수생포유류형", "정보수집 타입" };

    /// <summary>
    /// v0.74.0 특징 포함 한글 통일 (승인된 1 · 2번):
    /// ① 문형 — "특징에 「X」를 포함하는" · "를 (「Y」는 제외) 포함하는" → 공식 카드 문형 "특징으로 「X」를 포함하는" · "를 포함(「Y」는 제외)하는" (공식 BT13-048 · EX3-045 · BT13-100)
    /// ② 특징 이름 — 영어 특징 묶음을 공식 한글 카드가 쓰는 부분 낱말로 (<see cref="ContainsGloss74"/>).
    /// 영어에 "in any of its traits"가 있고 그 목록과 맞는 한글 목록만 바꾼다 (같은 목록이 "[X] trait" 일치에도 맞으면 그대로)
    /// </summary>
    private static string? TraitContains74(string english, string? ko)
    {
        if (ko == null || !ko.Contains("특징", StringComparison.Ordinal) || !ko.Contains("포함", StringComparison.Ordinal)) return ko;
        var contains = ContainsList73.Matches(english).Select(m => Regex.Matches(m.Groups["x"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim()).ToList()).ToList();
        if (contains.Count == 0) return ko;
        var exact = ExactList73.Matches(english).Select(m => Names73(m.Groups["x"].Value)).ToList();
        return KoContains74.Replace(ko, m =>
        {
            var koNames = Regex.Matches(m.Groups["l"].Value, "「([^」]+)」").Select(x => x.Groups[1].Value).ToList();
            var koSet = koNames.ToHashSet();
            var en = contains.FirstOrDefault(list => Covers73(list.Select(x => new HashSet<string> { TraitKo(x), NameKo(x), x }).ToList(), koSet));
            if (en == null || exact.Any(set => Covers73(set, koSet))) return m.Value;
            var newNames = ContainsKo74(en, koNames);
            string list = string.Join("/", newNames.Select(x => $"「{x}」"));
            string exKo = m.Groups["ex1"].Success ? m.Groups["ex1"].Value : m.Groups["ex2"].Success ? m.Groups["ex2"].Value : "";
            if (exKo == "「수생포유류형」" && newNames.Contains("수")) exKo = string.Join("/", SeaAnimalExcludeKo74.Select(x => $"「{x}」"));
            string ex = exKo.Length > 0 ? $"({exKo}{J(exKo, "은", "는")} 제외)" : "";
            return $"특징으로 {list}{J(list, "을", "를")} 포함{ex}{m.Groups["v"].Value}";
        });
    }

    /// <summary>영어 특징 목록 → 공식 한글 부분 낱말 목록 (묶음에 없는 항목은 지금 한글 그대로)</summary>
    private static List<string> ContainsKo74(List<string> en, List<string> koNow)
    {
        foreach (var w in ContainsWhole74)
            if (w.En.Length == en.Count && w.En.All(x => en.Contains(x, StringComparer.OrdinalIgnoreCase))) return w.Ko.ToList();
        var outp = new List<string>();
        void Add(string s) { if (!outp.Contains(s)) outp.Add(s); }
        for (int i = 0; i < en.Count;)
        {
            var g = ContainsGloss74.FirstOrDefault(g => i + g.En.Length <= en.Count
                && g.En.All(x => en.Skip(i).Take(g.En.Length).Contains(x, StringComparer.OrdinalIgnoreCase)));
            if (g.En != null) { foreach (var k in g.Ko) Add(k); i += g.En.Length; continue; }
            var cand = new HashSet<string> { TraitKo(en[i]), NameKo(en[i]), en[i] };
            Add(koNow.FirstOrDefault(cand.Contains) ?? TraitKo(en[i]));
            i++;
        }
        return outp;
    }

    /// <summary>v0.74.0 원래 규칙이 옮기지 못한 절만 (AD1-009 · BT11-097 · BT26-047 · EX8-043 · BT5-109 · EX1-071 · BT23-060)</summary>
    private static string? RenderClause74(string rawClause)
    {
        string t = rawClause.Trim();
        Match m;
        // BT5-109 "~ reduce ~. At the end of the turn, return the Digimon that digivolved with this effect to the bottom of its owner's deck."
        if ((m = Regex.Match(t, @"^(?<first>.+?\.)\s+At the end of (?:the|this) turn, return the Digimon that digivolved (?:with|by) this effect to the bottom of (?:its owner's|your|the) deck\.?$", I73)).Success
            && RenderClause(m.Groups["first"].Value) is { } f1)
            return f1 + " 이 턴 종료 시, 이 효과로 진화한 디지몬을 덱 아래로 되돌린다 (그 디지몬의 진화원은 파기한다).";
        // BT23-060 "Activate 1 [On Play] effect on a face-up [Zaxon] trait Digimon card in your security stack as an effect of this Digimon"
        if ((m = Regex.Match(t, @"^(?<head>.*?)Activate 1 (?<trig>\[[^\]]+\]) effect on a face[- ]up \[(?<x>[^\]]+)\] trait Digimon card in your security(?: stack)? as an effect of this Digimon\.?$", I73)).Success
            && RenderClause(m.Groups["head"].Value + "Activate 1 " + m.Groups["trig"].Value + " effect on the placed card as an effect of this Digimon.") is { } f2 && f2.Contains("놓은 카드의 ", StringComparison.Ordinal))
        {
            string x = "「" + TraitKo(m.Groups["x"].Value) + "」";
            return f2.Replace("놓은 카드의 ", $"자신의 시큐리티의 특징으로 {x}{J(x, "을", "를")} 가진 앞면의 디지몬 카드 1장의 ");
        }
        if (SentenceParser.ClauseRewrite74(t) is not { } alt || alt == t) return null;
        // EX1-071 같은 색 비용 — 비용의 색 조건을 빼고 옮긴 뒤 붙인다
        if (Regex.IsMatch(alt, @"by trashing 1 Digimon card in your hand of the same colou?r as the digivolving Digimon, ", I73))
        {
            var ko = RenderClause(Regex.Replace(alt, @"(?<=by trashing 1 Digimon card in your hand) of the same colou?r as the digivolving Digimon", "", I73));
            const string plain = "자신의 패에서 디지몬 카드 1장을 파기하는 것으로";
            return ko != null && ko.Contains(plain, StringComparison.Ordinal) ? ko.Replace(plain, "자신의 패에서 진화하는 디지몬과 같은 색의 디지몬 카드 1장을 파기하는 것으로") : null;
        }
        var r = RenderClause(alt);
        if (r == null) return null;
        // AD1-009 "이 디지몬은 ~. 그 후, ~, X 자신의 다른 디지몬 1마리는 ~" → "이 디지몬과 X 자신의 다른 디지몬 1마리는 ~"
        r = Regex.Replace(r, @"그 후, (?<d>[^,]+), 이 디지몬은 (?<v>상대의 디지몬의 효과를 받지 않는다)\. 그 후, \k<d>, (?<x>[^.]+? 자신의 다른 디지몬 1마리)는 \k<v>\.", "그 후, ${d}, 이 디지몬과 ${x}는 ${v}.");
        // BT26-047 "X 전부는 상대의 옵션 카드의 효과를 받지 않는다. 그 후, ~, X 전부의 DP를 +N 한다" → "X 전부는 ~ 받지 않고, DP를 +N 한다"
        r = Regex.Replace(r, @"(?<x>[^,]+ 전부)는 (?<v>상대의 옵션 카드의 효과를 받지 않)는다\. 그 후, [^,]+, \k<x>의 DP를 \+(?<n>\d+) 한다\.", "${x}는 ${v}고, DP를 +${n} 한다.");
        // EX8-043 "《퇴화 1》 (~) 한다. 그 후, 이 디지몬이 레스트 상태일 때, 상대의 턴 종료 시까지, ~" → "《퇴화 1》 (~) 하고, 상대의 턴 종료 시까지, ~"
        r = Regex.Replace(r, @"\) 한다\. 그 후, 이 디지몬이 레스트 상태일 때, (?=상대의 턴 종료 시까지, 이 디지몬은)", ") 하고, ");
        return r;
    }
}
