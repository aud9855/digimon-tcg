using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.35.0 미지원 문장 9차 묶음(뒷면 진화원 · 테이머 아래 뒷면 카드)의 생성 한글 — 공식 한글 표기를 따른다:
/// "이 디지몬의 뒷면의 진화원을 아래에서부터 1장 파기하는 것으로," (BT22-038 · EX9-004) · "그 디지몬의 뒷면의 진화원 1장마다 지불하는 코스트 -1" (BT22-038) ·
/// "이 디지몬의 뒷면의 진화원 1장마다" (EX9-009 · EX9-039) · "이 디지몬의 진화원에 뒷면의 카드가 놓였을 때," (EX9-002) · "자신의 디지몬의 진화원에 뒷면의 카드가 놓였을 때," (EX9-069) ·
/// "뒷면의 진화원을 가진 이 디지몬" (EX9-001 · EX9-003) · "뒷면의 진화원을 가진 자신의 디지몬 전부는 《재기동》을 얻는다" (EX9-069) ·
/// "자신의 시큐리티 매수가 이 디지몬의 뒷면의 진화원 매수 이하라면," (EX9-029) · "이 디지몬의 진화원에서 특징 「사이보그형」을 가지거나 뒷면의 카드를 아래에서부터 2장 파기하는 것으로," (EX9-073) ·
/// "자신의 트래시에서 특징 「Ver.2」를 가진 디지몬 카드 3장을 이 디지몬의 진화원 아래에 뒷면으로 놓는 것으로," (BT22-049) ·
/// "상대의 디지몬 1마리의 진화원을 이 디지몬의 뒷면의 진화원 1장마다 선택하여 1장 파기한다" (EX9-018) · "상대의 디지몬 1마리를 이 디지몬의 뒷면의 진화원 1장마다 《퇴화 1》" (EX9-043)
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>"it may digivolve ~"의 "it"이 다른 디지몬(비용으로 진화원을 놓은 그 디지몬)인지 — 절마다 RenderClause가 정한다</summary>
    [ThreadStatic] private static bool _itOther35;

    private static void BeginClause35(string clause) =>
        _itOther35 = Regex.IsMatch(clause, @"\b(?:any|1|one) of your [^,.]*?Digimon's bottom digivolution cards?|\b(?:any|1|one) of your \[[^\]]+\] ?'s bottom digivolution cards?|as the bottom digivolution cards? of (?:any|1|one) of your\b|\bone of those Digimon\b|\bany of those Digimon\b", RegexOptions.IgnoreCase);

    private const RegexOptions IC = RegexOptions.IgnoreCase;

    private static string? CostKo35(string cost)
    {
        Match m;
        cost = cost.Trim();
        if ((m = Regex.Match(cost, @"^trashing (?:(?<n>\d+) of )?(?:this Digimon's|its) bottom (?:(?<n>\d+) )?face[- ]down digivolution cards?$", IC)).Success)
            return $"이 디지몬의 뒷면의 진화원을 아래에서부터 {(m.Groups["n"].Success ? m.Groups["n"].Value : "1")}장 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^trashing (?:any|1|one) of your (?<spec>.*?)Digimon's bottom (?:(?<n>\d+) )?face[- ]down digivolution cards?$", IC)).Success
            && (m.Groups["spec"].Value.Trim().Length == 0 ? "자신의 디지몬 1마리" : TargetKo("1 of your " + m.Groups["spec"].Value.Trim() + " Digimon")) is { } host)
            return $"{host}의 뒷면의 진화원을 아래에서부터 {(m.Groups["n"].Success ? m.Groups["n"].Value : "1")}장 파기하는 것으로,";
        if ((m = Regex.Match(cost, $@"^trashing (?:its|this Digimon's) bottom (?<n>\d+) face[- ]down or (?<x>{TargetSpec.NameList}) traits? digivolution cards$", IC)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"이 디지몬의 진화원에서 특징으로 {x}{J(x, "을", "를")} 가지거나 뒷면의 카드를 아래에서부터 {m.Groups["n"].Value}장 파기하는 것으로,";
        }
        if ((m = Regex.Match(cost, @"^trashing (?:the|(?<n>\d+)) bottom face[- ]down cards? (?:under card )?(?:from )?under (?:any of |one of |1 of )?your Tamers$", IC)).Success)
            return $"자신의 테이머 아래의 뒷면 카드를 아래에서부터 {(m.Groups["n"].Success ? m.Groups["n"].Value : "1")}장 파기하는 것으로,";
        // "placing 3 Digimon cards with the [Ver.2] trait from your trash face down as this Digimon's bottom digivolution cards" · "placing your deck's top card face down as ~"
        if ((m = Regex.Match(cost, @"^placing (?:(?<deck>the top card of your deck|your deck's top card)|(?<pick>.+?) (?:from|in) (?<from>your hand|your trash)) face[- ]down as (?:this Digimon's|its) bottom digivolution cards?$", IC)).Success
            && (m.Groups["deck"].Success ? "자신의 덱 위에서부터 카드 1장" : PickKo(m.Groups["pick"].Value) is { } fd ? $"{(m.Groups["from"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? "자신의 패에서" : "자신의 트래시에서")} {fd}" : null) is { } what)
            return $"{what}{J(what, "을", "를")} 이 디지몬의 진화원 가장 아래에 뒷면으로 놓는 것으로,";
        // "placing 1 card from your hand face down as any of your [DM] trait Digimon's bottom digivolution card" (EX9-070)
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:from|in) (?<from>your hand|your trash) face[- ]down as (?:any|1|one) of your (?<spec>.*?)Digimon's bottom digivolution card$", IC)).Success
            && PickKo(m.Groups["pick"].Value) is { } pk
            && (m.Groups["spec"].Value.Trim().Length == 0 ? "자신의 디지몬 1마리" : TargetKo("1 of your " + m.Groups["spec"].Value.Trim() + " Digimon")) is { } dest)
            return $"{(m.Groups["from"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? "자신의 패에서" : "자신의 트래시에서")} {pk}{J(pk, "을", "를")} {dest}의 진화원 아래에 뒷면으로 놓는 것으로,";
        // "A or B" — 둘 중 하나 (SentenceParser.ParseCost35와 같은 읽기): "~하거나, ~하는 것으로,"
        foreach (Match om in Regex.Matches(cost, @" or (?=(?:[a-z]+ing |your |the |this |its |\d+ ))", IC))
        {
            string a = cost[..om.Index].Trim(), b = cost[(om.Index + om.Length)..].Trim();
            if (!Regex.IsMatch(a, @"^[a-z]+ing ", IC)) continue;
            if (!Regex.IsMatch(b, @"^[a-z]+ing ", IC)) b = Regex.Match(a, @"^[a-z]+ing ", IC).Value + b;
            if (CostKo(a) is not { } ka || CostKo(b) is not { } kb || !ka.EndsWith("는 것으로,", StringComparison.Ordinal)) continue;
            return $"{ka[..^"는 것으로,".Length]}거나, {kb}";
        }
        return null;
    }

    private static string? EventKo35(string ev)
    {
        Match m;
        string e0 = ev.Trim();
        if ((m = Regex.Match(e0, @"^face[- ]down cards are placed (?:in|as|under) (?:(?<self>this Digimon)'s digivolution cards|(?:any of |one of )?your (?<spec>.*?)Digimon(?:'s)? digivolution cards|(?<self>this Digimon))$", IC)).Success)
        {
            if (m.Groups["self"].Success) return "이 디지몬의 진화원에 뒷면의 카드가 놓였을 때,";
            string spec = m.Groups["spec"].Value.Trim();
            string? host = spec.Length == 0 ? "자신의 디지몬" : TargetKo("all of your " + spec + " Digimon", bare: true);
            return host == null ? null : $"{host}의 진화원에 뒷면의 카드가 놓였을 때,";
        }
        if ((m = Regex.Match(e0, @"^(?:an effect trashes|effects trash) face[- ]down digivolution cards? (?:from|of) (?:(?<self>this Digimon)|(?:any of |one of )?your (?<spec>.*?)Digimon)$", IC)).Success)
            return m.Groups["self"].Success ? "효과로 이 디지몬의 뒷면의 진화원이 파기되었을 때," : "효과로 자신의 디지몬의 뒷면의 진화원이 파기되었을 때,";
        return null;
    }

    private static Cond? CondKo35(string cond)
    {
        Match m;
        string c0 = cond.Trim();
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?:(?<n>\d+) or more )?face[- ]down digivolution cards$", IC)).Success)
            return m.Groups["n"].Success ? Have($"이 디지몬의 뒷면의 진화원이 {m.Groups["n"].Value}장 이상") : Have("이 디지몬에게 뒷면의 진화원이");
        if (Regex.IsMatch(c0, @"^(?:this Digimon|it) has no face[- ]down digivolution cards$", IC))
            return NotHave("이 디지몬에게 뒷면의 진화원이");
        if ((m = Regex.Match(c0, @"^you have as many or (?<dir>fewer|more) security cards as this Digimon has face[- ]down digivolution cards$", IC)).Success)
            return Be($"자신의 시큐리티 매수가 이 디지몬의 뒷면의 진화원 매수 {(m.Groups["dir"].Value.Equals("fewer", StringComparison.OrdinalIgnoreCase) ? "이하" : "이상")}");
        if (Regex.IsMatch(c0, @"^(?:you have|there are) face[- ]down cards under (?:any of )?your Tamers$", IC))
            return Have("자신의 테이머 아래에 뒷면의 카드가");
        return null;
    }

    /// <summary>뒷면 진화원 셈 — "its" · "this Digimon's"는 이 디지몬, "their" · "that Digimon's"는 그 디지몬 (진화 코스트 감소 문장은 Normalize35가 "that Digimon's"로)</summary>
    private static string? CounterKo35(string counter)
    {
        Match m;
        string c0 = counter.Trim();
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?of (?<who>its|this Digimon's|their|that Digimon's) face[- ]down digivolution cards$", IC)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant() is "its" or "this digimon's" ? "이 디지몬" : "그 디지몬";
            return $"{who}의 뒷면의 진화원 {(m.Groups["n"].Success ? m.Groups["n"].Value : "1")}장마다";
        }
        return null;
    }

    private static (Match M, string Ko)? FilterKo35(string rest)
    {
        Match m;
        if ((m = Regex.Match(rest, @"^(?:with|that has) face[- ]down digivolution cards", IC)).Success)
            return (m, "뒷면의 진화원을 가진");
        return null;
    }

    /// <summary>문장 — 뒷면 · 테이머 아래 · 셈이 앞에 오는 표기</summary>
    private static string? SentenceKo35(string s)
    {
        Match m;
        // "Place the top card of your deck face down under this Tamer / any of your [DATA SQUAD] trait Tamers" · "you may ~"
        if ((m = Regex.Match(s, @"^(?<may>you may )?place (?:the top card of your deck|your deck's top card|the top (?<n>\d+) cards of your deck)(?<fd> face[- ]down)? under (?:(?<this>this Tamer)|(?:any of |1 of |one of )?your (?<spec>.*?)Tamers?)$", IC)).Success)
        {
            string n = m.Groups["n"].Success ? m.Groups["n"].Value : "1";
            string? who = m.Groups["this"].Success ? "이 테이머" : m.Groups["spec"].Value.Trim().Length == 0 ? "자신의 테이머"
                : Regex.Match(m.Groups["spec"].Value.Trim(), $@"^(?<x>{TargetSpec.NameList}) traits?$", IC) is { Success: true } tm
                    ? $"특징으로 {NamesKo(tm.Groups["x"].Value, trait: true)}{J(NamesKo(tm.Groups["x"].Value, trait: true), "을", "를")} 가진 자신의 테이머" : null;
            if (who == null) return null;
            return $"자신의 덱 위에서부터 {n}장을 {who}의 아래에 {(m.Groups["fd"].Success ? "뒷면으로 " : "")}{(m.Groups["may"].Success ? "놓을 수 있다" : "놓는다")}";
        }
        // "From 1 of your opponent's Digimon, trash any 1 digivolution card for each of this Digimon's face-down digivolution cards" (EX9-018)
        if ((m = Regex.Match(s, @"^From (?:1|one) of your opponent's Digimon, trash (?:any )?(?<n>\d+) digivolution cards? for (?:each|every) (?<cnt>.+)$", IC)).Success
            && CounterKo(m.Groups["cnt"].Value) is { } fc)
            return $"상대의 디지몬 1마리의 진화원을 {fc} 선택하여 {m.Groups["n"].Value}장 파기한다";
        // "To 1 of your opponent's Digimon, ＜De-Digivolve 1＞ for each ~" (EX9-043)
        if ((m = Regex.Match(s, @"^To (?:1|one) of your opponent's Digimon, ＜De-Digivolve (?<n>\d+)＞ for (?:each|every) (?<cnt>.+)$", IC)).Success
            && CounterKo(m.Groups["cnt"].Value) is { } dc)
            return $"상대의 디지몬 1마리를 {dc} 《퇴화 {m.Groups["n"].Value}》";
        // DP 기간이 셈 뒤: "This Digimon gets +1000 DP for each of its face-down digivolution cards until your opponent's turn ends" (SentenceParser.ParseAction35와 같은 읽기)
        if ((m = Regex.Match(s, @"^(?<a>.+? gets? [+-]\d+ DP) for (?<e>each|every) (?<cnt>.+?) (?<dur>until .+|for the turn)$", IC)).Success)
            return Sentence($"{m.Groups["a"].Value} {m.Groups["dur"].Value} for {m.Groups["e"].Value} {m.Groups["cnt"].Value}");
        // "Place 1 card in your hand face down as this Digimon's bottom digivolution card" · "~ under this Tamer" — 비용 표기("placing ~")를 동작으로
        if ((m = Regex.Match(s, @"^(?<may>you may )?place (?<rest>.+ face[- ]down (?:as|under) .+)$", IC)).Success
            && CostKo("placing " + m.Groups["rest"].Value) is { } pc && pc.EndsWith("놓는 것으로,", StringComparison.Ordinal))
            return pc[..^"놓는 것으로,".Length] + (m.Groups["may"].Success ? "놓을 수 있다" : "놓는다");
        // 공식 (BT23-092): "상대의 턴 종료까지, 상대의 디지몬과 테이머 1마리(명)씩은 레스트할 수 없다"
        if ((m = Regex.Match(s, @"^(?:Until (?<d1>[^,]+), )?(?:1|one) of (?:their|your opponent's) Digimon and (?:1|one) of (?:their|your opponent's) Tamers (?:can't|cannot) (?<v>suspend|unsuspend)(?: (?<d2>until .+))?$", IC)).Success)
        {
            string d = (m.Groups["d1"].Success ? m.Groups["d1"].Value : m.Groups["d2"].Value).ToLowerInvariant();
            string durKo = d.Length == 0 ? "" : d.Contains("opponent") || d.Contains("their") ? "상대의 턴 종료 시까지, " : d.Contains("your turn") ? "자신의 턴 종료 시까지, " : "이 턴 동안, ";
            return $"{durKo}상대의 디지몬과 테이머 1마리(명)씩은 {(m.Groups["v"].Value.Equals("suspend", StringComparison.OrdinalIgnoreCase) ? "레스트할 수 없다" : "액티브가 되지 않는다")}";
        }
        // "When ~, ＜Delay＞" (BT26-099)
        if (Regex.IsMatch(s, @"^＜Delay＞$", IC)) return KeywordKo("Delay", withReminder: true);
        // 조건이 붙은 주어 "This [Glowing Dawn] trait Digimon unsuspends" — 공식 표기 "특징 「~」을 가진 이 디지몬을 액티브로 한다" (엔진은 "If this Digimon has the ~ trait, ~")
        if ((m = Regex.Match(s, $@"^This (?:Digimon with the (?<x>{TargetSpec.NameList}) traits?|(?<x>{TargetSpec.NameList}) traits? Digimon) (?<rest>(?:unsuspends|gets [+-]\d+ DP|gains ＜).*)$", IC)).Success
            && Sentence("This Digimon " + m.Groups["rest"].Value) is { } plain && plain.StartsWith("이 디지몬", StringComparison.Ordinal))
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"특징으로 {x}{J(x, "을", "를")} 가진 {plain}";
        }
        return null;
    }
}
