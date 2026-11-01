using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.54.0 미지원 문장 28차 묶음의 생성 한글 — 원래 규칙(27차까지)이 옮기지 못한 절만 (SentenceParser.Batch54와 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth54;

    private static string? RenderClause54(string rawClause)
    {
        if (_clauseDepth54 > 0) return null;
        _clauseDepth54++;
        try
        {
            if (ClauseSpecialKo54(rawClause.Replace(' ', ' ').Trim()) is { } special) return special;
            string alt = SentenceParser.ClauseRewrite54(rawClause);
            if (alt == rawClause || RenderClause(alt) is not { } ko) return null;
            // P-095 같은 기간의 두 문장을 한 문장으로 — "DP를 -6000 하고, 그 디지몬의 【진화 시】 효과는 발휘하지 않는다"
            return Regex.Replace(ko, @"DP를 (?<v>[+-]\d+) 한다\. 상대의 턴 종료 시까지, 그 디지몬의", "DP를 ${v} 하고, 그 디지몬의");
        }
        finally { _clauseDepth54--; }
    }

    private static string? ClauseSpecialKo54(string t)
    {
        if (EvoReductionKo54(t) is { } evo) return evo;
        if (GainLabeledKo54(t) is { } gain) return gain;
        if (XrosKo54(t) is { } xros) return xros;
        Match m;
        if ((m = SentenceParser.TamerOnPlayRe54.Match(t)).Success
            && Regex.Match(m.Groups["cond"].Value.Trim(), @"^you have (?<n>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*)$", IC) is { Success: true } who)
        {
            string n = NamesKo(who.Groups["n"].Value);
            return $"【서로의 턴】 자신의 {n}{J(n, "이", "가")} 있는 동안, 상대의 테이머 전부의 【등장 시】 효과는 발휘하지 않는다.";
        }
        if ((m = SentenceParser.LevelWhenDigivolvingRe54.Match(t)).Success)
            return $"【서로의 턴】 Lv.{m.Groups["n"].Value}인 디지몬 전부의 【진화 시】 효과는 발휘하지 않는다.";
        return null;
    }

    /// <summary>진화원 카드의 [메인] / 【서로의 턴】 효과 얻기 (SentenceParser.GainLabeled54)</summary>
    private static string? GainLabeledKo54(string t)
    {
        var m = SentenceParser.GainLabeledRe54.Match(t);
        if (!m.Success || SentenceParser.GainLabeledWhich54(m) == null) return null;
        string head = LabelKo(m.Groups["turn"].Value) ?? "";
        string label = LabelKo(m.Groups["label"].Value) ?? "";
        string what;
        if (m.Groups["t"].Success)
        {
            string tr = NamesKo(m.Groups["t"].Value, trait: true);
            what = $"특징으로 {tr}{J(tr, "을", "를")} 가진 카드";
        }
        else
        {
            string spec = Regex.Replace(m.Groups["spec"].Value.Trim(), @"\bcards$", "card", IC);
            if (PickKo("1 " + spec) is not { } pk) return null;
            what = Regex.Replace(pk, @"\s*1(?:장|마리|명)$", "");
        }
        return $"{head} 이 디지몬은 이 디지몬의 진화원에 있는 {what}의 {label} 효과를 전부 얻는다.";
    }

    /// <summary>진화 코스트 감소 (SentenceParser.EvoReduction54)</summary>
    private static string? EvoReductionKo54(string t)
    {
        var m = SentenceParser.EvoReduce54.Match(t);
        if (!m.Success || SentenceParser.Into54(m.Groups["into"].Value) == null) return null;
        string labels = m.Groups["labels"].Value;
        bool security = labels.Contains("[Security]", StringComparison.OrdinalIgnoreCase);
        bool startMain = labels.Contains("[Start of Your Main Phase]", StringComparison.OrdinalIgnoreCase);
        if (startMain != m.Groups["turn"].Success) return null;
        string head = LabelsKo40(Regex.Replace(labels, @"\[Security\]\s*", "", IC));
        if (security) head = "[시큐리티]" + head;
        if (IntoKo54(m.Groups["into"].Value) is not { } into) return null;
        bool any = !m.Groups["who"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
        string who = any ? "자신의 디지몬" : "이 디지몬";
        string turn = startMain ? "이 턴 동안, " : "";
        string each = m.Groups["colors"].Success ? "그 카드의 색 1색마다 " : "";
        return $"{head}{turn}{who}이 {into}로 진화할 때, {each}지불하는 진화 코스트를 -{m.Groups["n"].Value} 한다.";
    }

    /// <summary>진화할 카드 (SentenceParser.Into54와 같은 표) — "~ 카드" (조사 "로"를 붙인다)</summary>
    private static string? IntoKo54(string into)
    {
        string s = into.Trim();
        string zone = "";
        Match m;
        if ((m = Regex.Match(s, @"^(?<a>an? (?:Digimon )?card) in your (?<z>hand|trash) that's (?<rest>.+)$", IC)).Success)
        {
            zone = m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase) ? "패의 " : "트래시의 ";
            s = m.Groups["a"].Value + " that's " + m.Groups["rest"].Value;
        }
        else if ((m = Regex.Match(s, @"^(?<a>.+?) (?:from|in) your (?<z>hand|trash)$", IC)).Success)
        {
            zone = m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase) ? "패의 " : "트래시의 ";
            s = m.Groups["a"].Value;
        }
        string noun = Regex.IsMatch(s, @"^an? Digimon card\b", IC) ? "디지몬 카드" : "카드";
        if ((m = Regex.Match(s, @"^an? (?:Digimon )?card with (?<n>\[[^\]]+\]) in its name,? or the (?<t>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) traits?$", IC)).Success)
        {
            string n = NamesKo(m.Groups["n"].Value), tr = NamesKo(m.Groups["t"].Value, trait: true);
            return $"{zone}명칭에 {n}{J(n, "을", "를")} 포함하거나 특징으로 {tr}{J(tr, "을", "를")} 가진 {noun}";
        }
        if ((m = Regex.Match(s, $@"^an? (?:Digimon )?card that's (?<c>{Colors}) or has the (?<t>\[[^\]]+\]) trait$", IC)).Success)
        {
            string tr = NamesKo(m.Groups["t"].Value, trait: true);
            return $"{ColorKo(m.Groups["c"].Value)}이거나 특징으로 {tr}{J(tr, "을", "를")} 가진 {zone}{noun}";
        }
        if ((m = Regex.Match(s, @"^an? (?:Digimon )?card with (?<k>\d+) colou?rs or (?:the )?(?<t>\[[^\]]+\]) trait$", IC)).Success)
        {
            string tr = NamesKo(m.Groups["t"].Value, trait: true);
            return $"{m.Groups["k"].Value}색이거나 특징으로 {tr}{J(tr, "을", "를")} 가진 {zone}{noun}";
        }
        if (Regex.IsMatch(s, @"^an? Digimon card$", IC)) return $"{zone}디지몬 카드";
        if ((m = Regex.Match(s, @"^an? (?:Digimon )?card with (?<n>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) in (?:its|their) names?$", IC)).Success)
        {
            string n = NamesKo(m.Groups["n"].Value);
            return $"명칭에 {n}{J(n, "을", "를")} 포함하는 {zone}{noun}";
        }
        if ((m = Regex.Match(s, @"^an? (?:Digimon )?card with the (?<t>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) traits?$", IC)).Success)
        {
            string tr = NamesKo(m.Groups["t"].Value, trait: true);
            return $"특징으로 {tr}{J(tr, "을", "를")} 가진 {zone}{noun}";
        }
        return null;
    }
}

/// <summary>v0.54.0 디지크로스 재료 (SentenceParser.Xros54)</summary>
public static partial class KoreanEffectRenderer
{
    private static string? XrosKo54(string t)
    {
        Match m;
        if ((m = SentenceParser.XrosTrashOnlyRe54.Match(t)).Success)
        {
            string n = NamesKo(m.Groups["n"].Value);
            return $"{n} 이외의 자신의 디지몬이 없는 동안, 이 카드의 디지크로스로 자신의 트래시의 카드도 놓을 수 있다.";
        }
        if ((m = SentenceParser.XrosUnderAndTrashRe54.Match(t)).Success && SentenceParser.XrosTraits54(m.Groups["spec"].Value) is { } traits)
        {
            string tr = NamesKo(string.Join(" or ", traits.ConvertAll(x => $"[{x}]")), trait: true);
            return $"【서로의 턴】 특징으로 {tr}{J(tr, "을", "를")} 가지고, 디지크로스 조건을 가진 자신의 디지몬 카드가 등장할 때, 이 테이머를 레스트시키는 것으로, 그 카드의 디지크로스로 자신의 테이머 아래와 트래시의 카드도 1장씩 놓을 수 있다.";
        }
        return null;
    }
}
