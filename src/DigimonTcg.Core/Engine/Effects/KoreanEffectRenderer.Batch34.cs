using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.34.0 미지원 문장 8차 묶음의 생성 한글 (SentenceParser.Batch34와 같은 범위) — 공식 한글 표기를 따른다:
/// "이 디지몬은 플레이어에게 어택할 수 없다" (ST7-04) · "이 디지몬은 상대의 효과로 패/덱으로 되돌아가지 않는다" (BT11-060) · "상대의 턴 종료까지 이 디지몬은 배틀에서 소멸하지 않는다" (BT14-028) ·
/// "이 디지몬은 상대의 효과로 소멸하지 않는다" (BT14-062) · "레스트 상태인 이 디지몬은 상대의 디지몬의 효과를 받지 않는다" (BT15-047) · "~ 이 디지몬은 DP가 마이너스되지 않는다" (BT13-072) ·
/// "이 디지몬은, 액티브 상태인 상대의 디지몬에게도 어택할 수 있다" (BT2-051) · "특징으로 「크로스 하트」를 가진 이 디지몬은 《관통》을 얻는다" (BT19-033) ·
/// "진화원이 4장 이상 있는 이 디지몬을 DP +2000" (BT20-001) · "상대의 디지몬 1마리의 【진화 시】 효과는 발휘하지 않고" (BT19-093) ·
/// "상대는 본인의 패에서 디지몬 카드 1장을 파기할 수 있다. 이 효과로 파기하지 않았다면," (BT15-074) · "상대는 본인의 디지몬/테이머 1마리(명)를 소멸시킬 수 있다. 이 효과로 소멸하지 않았다면," (BT18-082) ·
/// "상대의 패 1장을 보지 않고 파기한다" (BT14-075) · "상대의 패를 전부 확인하고, 그중 1장을 선택하여 파기한다" (BT13-092) · "그 어택을 종료한다" (BT5-111) ·
/// "이 디지몬의 진화원을 선택하여 2장 파기하는 것으로," (BT14-023 · BT5-111) · "이 카드가 등장할 때, ~ 이 카드의 아래에 놓는 것으로, 놓은 1장마다 지불하는 등장 코스트 -4." (BT15-102)
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>앞 문장의 상대가 고르는 동작 ("파기" · "소멸") — 뒤의 "If they don't"를 "이 효과로 파기하지 않았다면,"으로</summary>
    [ThreadStatic] private static string? _oppVerb34;

    // =====================================================================
    // 대상 조건
    // =====================================================================

    private static (Match M, string Ko)? FilterKo34(string rest)
    {
        Match m;
        if ((m = Regex.Match(rest, @"^(?:with |and )?(?:(?:as many or fewer|equal or fewer|the same number or fewer) digivolution cards (?:as|than) this Digimon(?:'s)?|as many digivolution cards as this Digimon or fewer|(?:digivolution cards )?less than or equal to (?:the number of )?this Digimon's digivolution cards)", RegexOptions.IgnoreCase)).Success)
            return (m, "진화원 매수가 이 디지몬 이하인");
        if ((m = Regex.Match(rest, @"^(?:with |and )?(?:as many or more|equal or more) digivolution cards (?:as|than) this Digimon", RegexOptions.IgnoreCase)).Success)
            return (m, "진화원 매수가 이 디지몬 이상인");
        if ((m = Regex.Match(rest, @"^with (?:DP less than or equal to|as much or less DP (?:as|than)|less or equal DP (?:as|than)) (?<w>this|that) Digimon(?:'s DP)?", RegexOptions.IgnoreCase)).Success)
            return (m, $"{(m.Groups["w"].Value.Equals("this", StringComparison.OrdinalIgnoreCase) ? "이" : "그")} 디지몬의 DP 이하인");
        if ((m = Regex.Match(rest, @"^with (?:as high or lower a play cost as|a play cost less than or equal to|a play cost equal to or less than) (?<w>this|that) Digimon(?:'s play cost)?", RegexOptions.IgnoreCase)).Success)
            return (m, $"{(m.Groups["w"].Value.Equals("this", StringComparison.OrdinalIgnoreCase) ? "이" : "그")} 디지몬의 등장 코스트 이하인");
        if ((m = Regex.Match(rest, @"^with (?:play costs? less than or equal to (?:the number of )?this Digimon's digivolution cards|as high or lower a play cost as the number of this Digimon's digivolution cards)", RegexOptions.IgnoreCase)).Success)
            return (m, "등장 코스트가 이 디지몬의 진화원 매수 이하인");
        if ((m = Regex.Match(rest, $@"^(?<neg>without)\s*(?<x>{TargetSpec.NameList}) in (?:its|their) digivolution cards", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return (m, $"진화원에 {x}{J(x, "이", "가")} 없는"); }
        if ((m = Regex.Match(rest, $@"^(?:that has |that have )(?<x>{TargetSpec.NameList}) in (?:its|their) digivolution cards", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return (m, $"진화원에 {x}{J(x, "을", "를")} 가진"); }
        if ((m = Regex.Match(rest, $@"^without (?:the )?(?<x>{TargetSpec.NameList}) traits?", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return (m, $"특징으로 {x}{J(x, "을", "를")} 갖지 않은"); }
        if ((m = Regex.Match(rest, $@"^without (?<x>{TargetSpec.NameList}) in (?:its|their) names?", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return (m, $"명칭에 {x}{J(x, "을", "를")} 포함하지 않는"); }
        return FilterKoRef34(rest);   // KoreanEffectRenderer.Batch34b.cs
    }

    // =====================================================================
    // 절: 등장 · 사용 코스트 감소 · 색 조건
    // =====================================================================

    private static string? ClauseKo34(string clause)
    {
        Match m;
        if ((m = SentenceParser.PlayCostClause34.Match(clause)).Success)
        {
            bool use = m.Groups["use"].Success;
            string? pay = PlayCostPayKo34(m.Groups["cost"].Value) ?? (CostKo(m.Groups["cost"].Value) is { } ck ? ck.TrimEnd(',') : null);
            if (pay == null) return null;
            string each = m.Groups["each"].Success ? (Regex.IsMatch(m.Groups["each"].Value, @"trashed", RegexOptions.IgnoreCase) ? "파기한 1장마다 " : Regex.IsMatch(m.Groups["each"].Value, @"returned", RegexOptions.IgnoreCase) ? "되돌린 1장마다 " : "놓은 1장마다 ") : "";
            return $"{(use ? "이 카드를 사용할 때," : "이 카드가 등장할 때,")} {pay.TrimEnd(',')}, {each}지불하는 {(use ? "사용" : "등장")} 코스트 -{m.Groups["n"].Value}.";
        }
        if ((m = Regex.Match(clause, $@"^(?<c>{Colors}) also meets this card's colou?r requirements?\.?$", RegexOptions.IgnoreCase)).Success)
            return $"{ColorKo(m.Groups["c"].Value)}인 자신의 디지몬/테이머로도 이 카드의 색 조건을 충족할 수 있다.";
        return null;
    }

    private static string? PlayCostPayKo34(string cost)
    {
        Match m;
        if ((m = Regex.Match(cost, @"^placing (?<up>up to )?(?<n>\d+) (?<spec>.+?) cards? with different names from your (?<from>battle area or trash|trash or battle area|trash|battle area) under (?:it|this card)$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + m.Groups["spec"].Value + " card") is { } pk)
        {
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string zone = from.Contains("battle") && from.Contains("trash") ? "배틀 에어리어/트래시" : from.Contains("battle") ? "배틀 에어리어" : "트래시";
            string kind = Regex.Replace(pk, @"\s*1장$", "");
            return $"자신의 {zone}에서 명칭이 서로 다른 {kind} {m.Groups["n"].Value}장{(m.Groups["up"].Success ? "까지" : "")}를 이 카드의 아래에 놓는 것으로,";
        }
        if ((m = Regex.Match(cost, @"^placing 1 of each face-up (?<spec>.+?) cards? with different names from your security stack under (?:it|this card)$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + m.Groups["spec"].Value + " card") is { } fk)
            return $"자신의 시큐리티에서, 명칭이 서로 다른 앞면의 {Regex.Replace(fk, @"\s*1장$", "")} 1장씩을 이 카드 아래에 놓는 것으로,";
        if ((m = Regex.Match(cost, @"^placing up to (?<n>\d+) (?<pick>.+?) from your trash (?:at|on|to) the bottom of (?:the|your) deck$", RegexOptions.IgnoreCase)).Success
            && (PickKo("1 " + Regex.Replace(m.Groups["pick"].Value, @"^cards\b", "card", RegexOptions.IgnoreCase)) ?? PickKo("1 " + m.Groups["pick"].Value)) is { } dk)
            return $"자신의 트래시에서 {Regex.Replace(dk, @"\s*1장$", "")} {m.Groups["n"].Value}장까지를 덱 아래에 놓는 것으로,";
        if ((m = Regex.Match(cost, @"^trashing cards from the top of your security stack, up to having (?<n>\d+) left$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 시큐리티가 {m.Groups["n"].Value}장 남을 때까지 위에서부터 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^returning (?<n>\d+) (?<pick>.+?) from your trash to the top or bottom of (?:the|your) deck$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["n"].Value + " " + m.Groups["pick"].Value) is { } tk)
            return $"자신의 트래시에서 {tk}{J(tk, "을", "를")} 덱 위 또는 아래로 되돌리는 것으로,";
        return null;
    }

    // =====================================================================
    // 비용
    // =====================================================================

    /// <summary>앞의 규칙보다 먼저 (원래 규칙이 "up to"를 읽지 못해 null로 끝나는 것)</summary>
    private static string? CostKoPre34(string cost)
    {
        if (Regex.IsMatch(cost, @"^returning (?:1|one) of your (?:[^,]*? )?Tamers to |^returning (?:1|one) of your \[[^\]]+\]s? to |^suspending (?:up to )?\d+ of your (?:[^,]*? )?Tamers$", RegexOptions.IgnoreCase) && CostKo34(cost) is { } tamer34) return tamer34;
        if (Regex.Match(cost, @"^returning (?<up>up to )(?<pick>\d+ .+?) from your trash to the (?<pos>top|bottom) of (?:the|your) deck(?: in any order)?$", RegexOptions.IgnoreCase) is { Success: true } m
            && PickKo(m.Groups["pick"].Value) is { } uk)
            return $"자신의 트래시에서 {uk}까지를 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "원하는 순서대로 덱 위" : "덱 아래")}로 되돌리는 것으로,";
        return null;
    }

    private static string? CostKo34(string cost)
    {
        Match m;
        cost = cost.Trim();
        if ((m = Regex.Match(cost, @"^(?:trashing any (?<n>\d+) of this Digimon's digivolution cards|choosing and trashing (?<n>\d+) of this Digimon's digivolution cards)$", RegexOptions.IgnoreCase)).Success)
            return $"이 디지몬의 진화원을 선택하여 {m.Groups["n"].Value}장 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^trashing (?:(?<up>up to )|any )?(?<pick>\d+ .+?) (?:from|in) (?:any of |1 of |one of )?(?<who>your Digimon's|your Digimon|this Digimon's) digivolution cards?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } pk)
        {
            string who = m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 디지몬의 진화원에서" : "자신의 디지몬의 진화원에서";
            string what = pk + (m.Groups["up"].Success ? "까지" : "");
            return $"{who} {what}{J(what, "을", "를")} 파기하는 것으로,";
        }
        if ((m = Regex.Match(cost, @"^trashing (?:any )?(?<n>\d+) digivolution cards? (?:from|of) (?:(?<one>1|one) of )?(?<t>your .+)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of " + m.Groups["t"].Value, bare: true) is { } host)
            return $"{host}{(m.Groups["one"].Success ? " 1마리" : "")}의 진화원을 선택하여 {m.Groups["n"].Value}장 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^returning (?<pick>\d+ .+?) from (?:your opponent's|their) trash to the (?<pos>top|bottom) of (?:the|their|its owner's) deck$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } ok)
            return $"상대의 트래시에서 {ok}{J(ok, "을", "를")} 덱 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}로 되돌리는 것으로,";
        if ((m = Regex.Match(cost, @"^returning (?<pick>\d+ .+?) (?:from|in) this Digimon's digivolution cards to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } rk)
            return $"이 디지몬의 진화원에서 {rk}{J(rk, "을", "를")} 패로 되돌리는 것으로,";
        if ((m = Regex.Match(cost, @"^suspending (?:1|one) (?<who>of your opponent's |of their )?Digimon or Tamers?$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["who"].Success ? "상대의 " : "")}디지몬 또는 테이머 1마리(명)를 레스트시키는 것으로,";
        if ((m = Regex.Match(cost, @"^(?<un>un)?suspending it$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["un"].Success ? "그 디지몬을 액티브로 하는 것으로," : "그 디지몬을 레스트시키는 것으로,";
        if (Regex.IsMatch(cost, @"^unsuspending (?:1|one) of your Digimon$", RegexOptions.IgnoreCase))
            return "자신의 디지몬 1마리를 액티브로 하는 것으로,";
        if ((m = Regex.Match(cost, @"^returning this Digimon(?: with (?<x>.+?))? to (?:(?<hand>the|your|its owner's) hand|the bottom of (?:the|your|its owner's) deck)$", RegexOptions.IgnoreCase)).Success)
        {
            string self = "이 디지몬";
            if (m.Groups["x"].Success)
            {
                if (FiltersKo("with " + m.Groups["x"].Value) is not { } xs) return null;
                self = string.Join(" ", xs) + " 이 디지몬";
            }
            return $"{self}을 {(m.Groups["hand"].Success ? "패로" : "덱 아래로")} 되돌리는 것으로,";
        }
        if ((m = Regex.Match(cost, @"^returning (?:1|one) (?:of your |(?<other>other ))?(?<spec>(?:[^,]*? )?Digimon(?: with [^,]+?)?) to the bottom of (?:the|your|its owner's) deck$", RegexOptions.IgnoreCase)).Success
            && TargetKo("1 of your " + (m.Groups["other"].Success ? "other " : "") + m.Groups["spec"].Value) is { } bk)
            return $"{bk}{J(bk, "을", "를")} 덱 아래로 되돌리는 것으로,";
        if ((m = Regex.Match(cost, $@"^returning (?:1|one) of your (?:(?<color>{Colors}) |(?<x>{TargetSpec.NameList}) traits? )?Tamers to (?:(?<hand>the|your|its owner's) hand|the bottom of (?:the|your|its owner's) deck)$|^returning (?:1|one) of your (?<name>\[[^\]]+\])s? to (?:(?<hand>the|your|its owner's) hand|the bottom of (?:the|your|its owner's) deck)$", RegexOptions.IgnoreCase)).Success)
        {
            string who = m.Groups["color"].Success ? $"{ColorKo(m.Groups["color"].Value)}인 자신의 테이머 1명"
                : m.Groups["x"].Success ? $"특징으로 {NamesKo(m.Groups["x"].Value, trait: true)}{J(NamesKo(m.Groups["x"].Value, trait: true), "을", "를")} 가진 자신의 테이머 1명"
                : m.Groups["name"].Success ? $"자신의 {NamesKo(m.Groups["name"].Value)} 1명" : "자신의 테이머 1명";
            return $"{who}{J(who, "을", "를")} {(m.Groups["hand"].Success ? "패로" : "덱 아래로")} 되돌리는 것으로,";
        }
        if ((m = Regex.Match(cost, $@"^suspending up to (?<n>\d+) (?:of your )?(?:(?<color>{Colors}) )?Tamers$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["color"].Success ? ColorKo(m.Groups["color"].Value) + "인 " : "")}자신의 테이머 {m.Groups["n"].Value}명까지를 레스트시키는 것으로,";
        if ((m = Regex.Match(cost, @"^trashing the top (?<n>\d+) cards of your deck$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 덱을 위에서부터 {m.Groups["n"].Value}장 파기하는 것으로,";
        if (Regex.IsMatch(cost, @"^returning (?:the top card of your security stack|your top security card) to (?:the|your) hand$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티를 위에서부터 1장 패에 추가하는 것으로,";
        if (Regex.IsMatch(cost, @"^trashing (?:the top or bottom card of your security stack|your top or bottom security card)$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티 위 또는 아래의 카드 1장을 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^returning (?<up>up to )(?<pick>\d+ .+?) from your trash to the (?<pos>top|bottom) of (?:the|your) deck(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } uk)
            return $"자신의 트래시에서 {uk}까지를 덱 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}로 되돌리는 것으로,";
        if (Regex.IsMatch(cost, @"^adding the top or bottom card of your security stack to (?:the|your) hand$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티 위 또는 아래의 카드 1장을 패에 추가하는 것으로,";
        return null;
    }

    // =====================================================================
    // 조건
    // =====================================================================

    private static Cond? CondKo34(string cond)
    {
        string c0 = cond.Trim();
        string verb = _oppVerb34 ?? "";
        Match m;
        if (Regex.IsMatch(c0, @"^this effect trashed$", RegexOptions.IgnoreCase)) return new("이 효과로 파기했다면,", "이 효과로 파기했다면,");
        if (Regex.IsMatch(c0, @"^this effect deleted$", RegexOptions.IgnoreCase)) return new("이 효과로 소멸시켰다면,", "이 효과로 소멸시켰다면,");
        if (Regex.IsMatch(c0, @"^this effect suspend(?:s|ed) (?:your|one of your) Digimon$", RegexOptions.IgnoreCase)) return new("이 효과로 자신의 디지몬을 레스트시켰다면,", "이 효과로 자신의 디지몬을 레스트시켰다면,");
        if (Regex.IsMatch(c0, @"^a card (?:is|was) placed by this effect$", RegexOptions.IgnoreCase)) return new("이 효과로 놓았다면,", "이 효과로 놓았다면,");
        if (Regex.IsMatch(c0, @"^it's your opponent's turn$", RegexOptions.IgnoreCase)) return Be("상대의 턴");
        if (Regex.IsMatch(c0, @"^your opponent has no Digimon with digivolution cards(?: in play)?$", RegexOptions.IgnoreCase))
            return NotHave("진화원을 가진 상대의 디지몬이");
        if ((m = Regex.Match(c0, @"^your opponent has no Digimon (?<rest>with .+)$", RegexOptions.IgnoreCase)).Success && FiltersKo(m.Groups["rest"].Value) is { } nf)
            return NotHave($"{string.Join(" ", nf)} 상대의 디지몬이");
        if ((m = Regex.Match(c0, $@"^you have (?<x>{TargetSpec.NameList})(?: in play)?$", RegexOptions.IgnoreCase)).Success && TargetSpec.Names(m.Groups["x"].Value).Count > 1)
        { string x = string.Join(" 또는 ", TargetSpec.Names(m.Groups["x"].Value).Select(n => $"「{NameKo(n)}」")); return Have($"자신의 {x}{J(x.TrimEnd('」'), "이", "가")}"); }
        if (Regex.IsMatch(c0, @"^this effect didn't delete$", RegexOptions.IgnoreCase))
            return new("이 효과로 소멸하지 않았다면,", "이 효과로 소멸하지 않았다면,");
        if (Regex.IsMatch(c0, @"^(?:they don't|they didn't|they do not|they did not|they do not trash a card|your opponent didn't trash with this effect|your opponent didn't|this effect didn't (?:trash|play|add))$", RegexOptions.IgnoreCase))
        {
            string v = Regex.IsMatch(c0, @"trash", RegexOptions.IgnoreCase) || verb == "파기" ? "파기하지" : verb == "소멸" ? "소멸하지" : verb == "등장" ? "등장시키지" : "그렇게 하지";
            return new($"이 효과로 {v} 않았다면,", $"이 효과로 {v} 않았다면,");
        }
        if (Regex.IsMatch(c0, @"^(?:they do|they did|your opponent did)$", RegexOptions.IgnoreCase))
        {
            string v = verb == "소멸" ? "소멸시켰다면" : verb == "등장" ? "등장시켰다면" : verb == "파기" ? "파기했다면" : "그렇게 했다면";
            return new($"이 효과로 {v},", $"이 효과로 {v},");
        }
        return null;
    }

    // =====================================================================
    // 문장
    // =====================================================================

    /// <summary>앞의 규칙보다 먼저: 1장씩 놓기 · 뒤집힌 보호 표기 + "and it gets ~"</summary>
    private static string? PreSentenceKo34(string s, bool may = false)
    {
        s = s.Trim().TrimEnd('.');
        Match m;
        if ((m = SentenceParser.PlaceEach34.Match(s)).Success)
        {
            var names = TargetSpec.Names(m.Groups["names"].Value).Select(n => $"「{NameKo(n)}」").ToList();
            string pair = string.Join("", names.Select((n, i) => i < names.Count - 1 ? n + J(n.Trim('「', '」'), "과", "와") + " " : n));
            string from = m.Groups["from"].Value.ToLowerInvariant() switch { "trash" => "자신의 트래시에서", "hand" => "자신의 패에서", _ => "자신의 패/트래시에서" };
            string who = m.Groups["who"].Value.StartsWith("that", StringComparison.OrdinalIgnoreCase) || m.Groups["who"].Value.Equals("it", StringComparison.OrdinalIgnoreCase) ? "그 디지몬" : "이 디지몬";
            return $"{from} {pair} 1장씩을 {who}의 진화원 아래에 원하는 순서대로 {(may ? "놓을 수 있다" : "놓는다")}";
        }
        if (Regex.IsMatch(s, @"^(?:Until [^,]+, )?(?:your opponent's|their|the opponent's) (?:Digimon(?:'s)? )?effects (?:don't|do not) affect .+ and (?:it|they) .+$", RegexOptions.IgnoreCase))
            return RestrictionKo34(s, may: false);
        return null;
    }

    private static string? SentenceKo34(string s, bool may)
    {
        s = s.Trim().TrimEnd('.');
        if (SentenceParser.PlaceEach34.IsMatch(s) && PreSentenceKo34(s, may) is { } each) return each;
        string n34 = SentenceParser.NormalizeAction34(s);
        if (n34 != s)
        {
            // "They trash 1 card in their hand" → 원래 규칙의 표기 (공식: "상대는 본인의 패 1장을 파기한다")
            if (Regex.Match(n34, @"^Your opponent trashes (?<n>\d+) cards in their hand$", RegexOptions.IgnoreCase) is { Success: true } th)
                return $"상대는 본인의 패 {th.Groups["n"].Value}장을 파기{(may ? "할 수 있다" : "한다")}";
            if (Sentence(may ? "You may " + Uncap(n34) : n34) is { } viaNorm) return viaNorm;
        }
        Match m;
        string Verb(string stem) => may ? $"{stem}할 수 있다" : $"{stem}한다";
        // ---- 상대가 고르는 동작 ----
        if ((m = Regex.Match(s, @"^Your opponent may trash (?<pick>\d+ .*?cards?) in their hand$", RegexOptions.IgnoreCase)).Success)
        {
            _oppVerb34 = "파기";
            if (Regex.Match(m.Groups["pick"].Value, @"^(?<n>\d+) cards?$", RegexOptions.IgnoreCase) is { Success: true } plain)
                return $"상대는 본인의 패 {plain.Groups["n"].Value}장을 파기할 수 있다";
            return PickKo(m.Groups["pick"].Value) is { } pk ? $"상대는 본인의 패에서 {pk}{J(pk, "을", "를")} 파기할 수 있다" : null;
        }
        if ((m = Regex.Match(s, @"^Trash (?<n>\d+) cards? (?:in|from) your opponent's hand without looking$", RegexOptions.IgnoreCase)).Success)
            return $"상대의 패 {m.Groups["n"].Value}장을 보지 않고 {Verb("파기")}";
        if ((m = Regex.Match(s, @"^(?:Look at your opponent's hand and trash (?<n>\d+) cards? (?:in|from) it|Search your opponent's hand, choose (?<n>\d+) cards?, and trash (?:it|them))$", RegexOptions.IgnoreCase)).Success)
            return $"상대의 패를 전부 확인하고, 그중 {m.Groups["n"].Value}장을 선택하여 {Verb("파기")}";
        if (Regex.IsMatch(s, @"^Your opponent may trash their top security card$", RegexOptions.IgnoreCase))
        { _oppVerb34 = "파기"; return "상대는 본인의 시큐리티를 위에서부터 1장 파기할 수 있다"; }
        if (Regex.IsMatch(s, @"^Your opponent may delete (?:1|one) of their Digimon or Tamers$", RegexOptions.IgnoreCase))
        { _oppVerb34 = "소멸"; return "상대는 본인의 디지몬/테이머 1마리(명)를 소멸시킬 수 있다"; }
        if (Regex.IsMatch(s, @"^(?:Your opponent|They) adds? (?:the top card of their security stack|their top security card) to (?:the|their) hand$", RegexOptions.IgnoreCase))
            return "상대는 본인의 시큐리티를 위에서부터 1장 패에 추가한다";
        // "Place 1 Digimon card with the [Xros Heart] trait from your hand or from under one of your Tamers under this Digimon as its bottom digivolution card"
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from your hand or from under (?:one of |any of )?your Tamers (?:under this Digimon as its bottom digivolution card|as this Digimon's bottom digivolution card)$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } ph)
            return $"자신의 패 또는 테이머 아래에서 {ph}{J(ph, "을", "를")} 이 디지몬의 진화원 아래에 {(may ? "놓을 수 있다" : "놓는다")}";
        // ---- 어택 종료 ----
        if (Regex.IsMatch(s, @"^End (?:the|that|this) attack$", RegexOptions.IgnoreCase))
            return $"그 어택을 {(may ? "종료할 수 있다" : "종료한다")}";
        // ---- 배틀 ----
        if ((m = Regex.Match(s, @"^(?<a>this Digimon|it|(?:1|one) of your .+?) (?<may>may )?battle (?<t>(?:1|one) of (?:your opponent's|their) .+)$", RegexOptions.IgnoreCase)).Success)
        {
            string? a = m.Groups["a"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : m.Groups["a"].Value.Equals("it", StringComparison.OrdinalIgnoreCase) ? "그 디지몬" : TargetKo(m.Groups["a"].Value);
            string? t = TargetKo(Regex.Replace(m.Groups["t"].Value, @"\btheir\b", "your opponent's", RegexOptions.IgnoreCase));
            if (a == null || t == null) return null;
            return $"{a}{J(a, "은", "는")} {t}{J(t, "과", "와")} {(m.Groups["may"].Success || may ? "배틀할 수 있다" : "배틀한다")}";
        }
        // ---- 패 · 덱 ----
        if ((m = Regex.Match(s, @"^Trash (?<up>up to )?(?<pick>\d+ .+?) (?:in|from) your hand$", RegexOptions.IgnoreCase)).Success)
        {
            string up = m.Groups["up"].Success ? "까지" : "";
            if (Regex.Match(m.Groups["pick"].Value, @"^(?<n>\d+) cards?$", RegexOptions.IgnoreCase) is { Success: true } pl)
                return $"자신의 패를 {pl.Groups["n"].Value}장{up} {Verb("파기")}";
            if (PickKo(m.Groups["pick"].Value) is not { } pk) return null;
            string what = pk + up;
            return $"자신의 패의 {what}{J(what, "을", "를")} {Verb("파기")}";
        }
        if ((m = Regex.Match(s, @"^Trash (?<up>up to )?(?<n>\d+) cards? from the top of (?<who>your|your opponent's) deck$|^Trash (?<who>their) deck's top (?<n>\d+) cards?$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["who"].Value.Equals("your", StringComparison.OrdinalIgnoreCase) ? "자신" : "상대")}의 덱 위에서부터 {m.Groups["n"].Value}장{(m.Groups["up"].Success ? "까지" : "")} {Verb("파기")}";
        if ((m = Regex.Match(s, @"^Return (?<up>up to )?(?<pick>\d+ .+?) from your (?<from>hand and/or trash|hand or trash|trash) to the top of (?:the|your) deck(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } tk)
        {
            string from = m.Groups["from"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? "자신의 패/트래시에서" : "자신의 트래시에서";
            string what = tk + (m.Groups["up"].Success ? "까지" : "");
            return $"{from} {what}{J(what, "을", "를")} 덱 위로 {(may ? "되돌릴 수 있다" : "되돌린다")}";
        }
        // ---- "~ for each card trashed" ----
        if ((m = Regex.Match(s, @"^(?<core>.+?) for each (?:card|security card|one) (?:(?:this effect )?(?<v>trashed|placed|returned)(?: (?:this way|by this effect|with this effect))?|you (?<v>return) this way)(?<dur> for the turn| until .+)?$", RegexOptions.IgnoreCase)).Success
            || (m = Regex.Match(s, @"^(?<core>.+?) for each card this effect (?<v>trashed|placed|returned)(?<dur> for the turn| until .+)?$", RegexOptions.IgnoreCase)).Success)
        {
            string v = m.Groups["v"].Value.ToLowerInvariant();
            string per = v == "trashed" ? "이 효과로 파기한 1장마다" : v == "placed" ? "이 효과로 놓은 1장마다" : "이 효과로 되돌린 1장마다";
            return Action(Cap(m.Groups["core"].Value) + m.Groups["dur"].Value, may ? Form.May : Form.Plain) is { } core ? $"{per} {core}" : null;
        }
        if (SentenceKo34b(s, may) is { } b34) return b34;   // KoreanEffectRenderer.Batch34b.cs
        // 공식 (BT3-061): "상대는 테이머의 효과 이외로 메모리를 플러스할 수 없다"
        if (Regex.IsMatch(s, @"^your opponent can't gain memory (?:other than|except) (?:by|with|through) Tamer(?:s'|'s)? effects$", RegexOptions.IgnoreCase))
            return "상대는 테이머의 효과 이외로 메모리를 플러스할 수 없다";
        // v0.35.0 "Players can't gain memory other than by Tamer effects" — 서로의 플레이어 (BT3-061 표기를 따른다)
        if (Regex.IsMatch(s, @"^players can't gain memory (?:other than|except) (?:by|with|through) Tamer(?:s'|'s)? effects$", RegexOptions.IgnoreCase))
            return "서로의 플레이어는 테이머의 효과 이외로 메모리를 플러스할 수 없다";
        // ---- 제한 · 보호 (기간 · 지속) ----
        return RestrictionKo34(s, may);
    }

    /// <summary>"Until your opponent's turn ends, this Digimon can't be deleted in battle" · "This Digimon can't attack players" (지속) 등</summary>
    private static string? RestrictionKo34(string s, bool may)
    {
        string t = s;
        if (Regex.Match(t, @"^(?<d>until [^,]+|for the turn), (?<rest>.+)$", RegexOptions.IgnoreCase) is { Success: true } lead)
            t = $"{lead.Groups["rest"].Value} {lead.Groups["d"].Value.ToLowerInvariant()}";
        var (durKo, core) = SplitDuration(t);
        core = SentenceParser.NormalizeSubjectForms34(SentenceParser.NormalizeProtection34(core));
        if (SentenceParser.SplitSubject34(core) is not { } split) return null;
        var preds = SentenceParser.ParsePreds34(split.Preds);
        if (preds == null) return null;
        var subj = SubjectKo34(split.Subject);
        if (subj == null) return null;
        var body = PredsKo34(subj.Value.Ko, subj.Value.Ref, preds, may);
        if (body == null) return null;
        return (durKo != null ? durKo + ", " : "") + body;
    }

    /// <summary>주어 → (처음 말할 때, 다시 가리킬 때)</summary>
    private static (string Ko, string Ref)? SubjectKo34(string subj)
    {
        Match m;
        if (Regex.IsMatch(subj, @"^(?:this Digimon|this card)$", RegexOptions.IgnoreCase)) return ("이 디지몬", "이 디지몬");
        if (Regex.IsMatch(subj, @"^(?:it|they|them)$", RegexOptions.IgnoreCase)) return TargetKo(subj) is { } it ? (it, it) : null;
        if (Regex.IsMatch(subj, @"^(?:that Digimon|the Digimon this effect digivolved|the Digimon that digivolved with this effect)$", RegexOptions.IgnoreCase)) return ("그 디지몬", "그 디지몬");
        // "this [Glowing Dawn] trait Digimon" · "this suspended Digimon" · "this Digimon with the [Xros Heart] trait" · "this Digimon with 4 or more digivolution cards"
        if ((m = Regex.Match(subj, @"^this (?<adj>(?:(?:suspended|unsuspended|level \d+(?: or (?:lower|higher))?|\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*(?: trait)?|(?:" + Colors + @")(?: or (?:" + Colors + @"))*) )*)Digimon(?<with> (?:with|without|that has) .+)?$", RegexOptions.IgnoreCase)).Success)
        {
            string adj = m.Groups["adj"].Value, with = m.Groups["with"].Value;
            if (Regex.Match(adj, $@"^(?<x>{TargetSpec.NameList}) traits? $", RegexOptions.IgnoreCase) is { Success: true } tx)
            { adj = ""; with = $" with the {tx.Groups["x"].Value} trait" + (with.Length > 0 ? " and" + Regex.Replace(with, @"^ with", "", RegexOptions.IgnoreCase) : ""); }
            // "with 4 or more digivolution cards" — 공식 (BT20-001): "진화원이 4장 이상 있는 이 디지몬"
            if (Regex.Match(with, @"^ with (?<n>\d+) or more (?<x>\[[^\]]+\] )?digivolution cards$", RegexOptions.IgnoreCase) is { Success: true } nm)
            {
                string pre = adj.Length > 0 ? (TargetKo($"all of your {adj}Digimon", bare: true) is { } ak ? Regex.Replace(ak, @"자신의 디지몬$", "").Trim() + " " : "") : "";
                string x = nm.Groups["x"].Success ? $"에 {NamesKo(nm.Groups["x"].Value.Trim())}이" : "이";
                return ($"{pre}진화원{x} {nm.Groups["n"].Value}장 이상 있는 이 디지몬", "이 디지몬");
            }
            if (TargetKo($"all of your {adj}Digimon{with}", bare: true) is not { } full) return null;
            string ko = Regex.Replace(full, @"자신의 디지몬$", "이 디지몬");
            return ko.EndsWith("이 디지몬") ? (ko, "이 디지몬") : null;
        }
        if ((m = Regex.Match(subj, $@"^(?:all of )?your (?<other>other )?(?<x>{TargetSpec.NameList})s?(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return ($"{(m.Groups["other"].Success ? "다른 " : "")}자신의 {x} 전부", "그 디지몬"); }
        if (Regex.IsMatch(subj, @"^your (?!opponent)", RegexOptions.IgnoreCase)) subj = "all of " + subj;
        return TargetKo(subj) is { } k ? (k, "그 디지몬") : null;
    }

    /// <summary>술어 → (말투, 끝맺음, 이어짐, "~할 수 있다"). 말투: t = "~은/는", o = "~을/를 DP", p = "~의 【진화 시】 효과는"</summary>
    private static (char Style, string Plain, string And)? PredKo34(SentenceParser.Pred34 p)
    {
        if (p.Dp != 0) return ('o', $"DP {(p.Dp > 0 ? "+" : "")}{p.Dp} 한다", $"DP {(p.Dp > 0 ? "+" : "")}{p.Dp} 하고");
        if (p.Grants != null)
            return KeywordListKo(Regex.Replace(p.Text, @"^(?:gains?|gets?) ", "", RegexOptions.IgnoreCase)) is { } kk ? ('t', $"{kk}{J(kk, "을", "를")} 얻는다", $"{kk}{J(kk, "을", "를")} 얻고") : null;
        if (p.Field != null)
            return p.Field switch
            {
                "attack" => ('t', "어택할 수 없다", "어택할 수 없고"),
                "block" => ('t', "블록할 수 없다", "블록할 수 없고"),
                "attack+block" => ('t', "어택과 블록을 할 수 없다", "어택과 블록을 할 수 없고"),
                "digivolve" => ('t', "진화할 수 없다", "진화할 수 없고"),
                "suspend" => ('t', "레스트할 수 없다", "레스트할 수 없고"),
                "unsuspend" => ('t', "액티브가 되지 않는다", "액티브가 되지 않고"),
                _ => null,
            };
        var k = p.Keywords;
        if (k.Length == 2 && k.Contains(Keyword.CannotActivateWhenDigivolving) && k.Contains(Keyword.CannotActivateWhenAttacking))
            return ('p', "【진화 시】/【어택 시】 효과는 발휘하지 않는다", "【진화 시】/【어택 시】 효과는 발휘하지 않고");
        if (k.Length != 1) return null;
        return k[0] switch
        {
            Keyword.CannotAttackPlayers => ('t', "플레이어에게 어택할 수 없다", "플레이어에게 어택할 수 없고"),
            Keyword.CannotAttackDigimon => ('t', "상대의 디지몬에게 어택할 수 없다", "상대의 디지몬에게 어택할 수 없고"),
            Keyword.CannotBeDeletedInBattle => ('t', "배틀에서 소멸하지 않는다", "배틀에서 소멸하지 않고"),
            Keyword.CannotBeDeletedByOpponentEffects => ('t', "상대의 효과로 소멸하지 않는다", "상대의 효과로 소멸하지 않고"),
            Keyword.CannotBeDeletedByEffects => ('t', "효과로 소멸하지 않는다", "효과로 소멸하지 않고"),
            Keyword.CannotBeReturnedByOpponentEffects => ('t', "상대의 효과로 패/덱으로 되돌아가지 않는다", "상대의 효과로 패/덱으로 되돌아가지 않고"),
            Keyword.CannotBeReturnedByEffects => ('t', "패/덱으로 되돌아가지 않는다", "패/덱으로 되돌아가지 않고"),
            Keyword.UnaffectedByOpponentEffects => ('t', "상대의 효과를 받지 않는다", "상대의 효과를 받지 않고"),
            Keyword.UnaffectedByOpponentDigimonEffects => ('t', "상대의 디지몬의 효과를 받지 않는다", "상대의 디지몬의 효과를 받지 않고"),
            Keyword.UnaffectedByOpponentOptionEffects => ('t', "상대의 옵션 카드의 효과를 받지 않는다", "상대의 옵션 카드의 효과를 받지 않고"),
            Keyword.UnaffectedByOpponentDeDigivolve => ('t', "상대의 《퇴화》의 효과를 받지 않는다", "상대의 《퇴화》의 효과를 받지 않고"),   // v0.36.0 공식 BT22-060
            Keyword.DpCannotBeReduced => ('t', "DP가 마이너스되지 않는다", "DP가 마이너스되지 않고"),
            Keyword.DpCannotBeReducedByOpponent => ('t', "상대의 효과로 DP가 마이너스되지 않는다", "상대의 효과로 DP가 마이너스되지 않고"),
            Keyword.Unblockable => ('t', "블록당하지 않는다", "블록당하지 않고"),
            Keyword.CannotBeAttacked => ('t', "어택당하지 않는다", "어택당하지 않고"),
            Keyword.CanAttackUnsuspended => ('t', "액티브 상태인 상대의 디지몬에게도 어택할 수 있다", "액티브 상태인 상대의 디지몬에게도 어택할 수 있고"),
            Keyword.CanAttackUnsuspendedSourceless => ('t', "진화원을 갖지 않은 액티브 상태인 상대의 디지몬에게도 어택할 수 있다", "진화원을 갖지 않은 액티브 상태인 상대의 디지몬에게도 어택할 수 있고"),
            Keyword.CannotActivateWhenDigivolving => ('p', "【진화 시】 효과는 발휘하지 않는다", "【진화 시】 효과는 발휘하지 않고"),
            Keyword.CannotActivateWhenAttacking => ('p', "【어택 시】 효과는 발휘하지 않는다", "【어택 시】 효과는 발휘하지 않고"),
            _ => null,
        };
    }

    /// <summary>주어 + 술어들 → 한 문장 ("이 디지몬은 어택할 수 없고, 상대의 효과를 받지 않는다" · "그 디지몬을 DP +2000 하고, 상대의 효과로 소멸하지 않는다")</summary>
    private static string? PredsKo34(string subj, string reference, List<SentenceParser.Pred34> preds, bool may)
    {
        var parts = new List<string>();
        char prev = ' ';
        for (int i = 0; i < preds.Count; i++)
        {
            if (PredKo34(preds[i]) is not { } pk) return null;
            string who = i == 0 ? subj : reference;
            string head = i > 0 && pk.Style == prev ? "" : pk.Style switch
            {
                'o' => $"{who}{J(who, "을", "를")} ",
                'p' => $"{who}의 ",
                _ => $"{who}{J(who, "은", "는")} ",
            };
            bool last = i == preds.Count - 1;
            string verb = last ? pk.Plain : pk.And;
            if (last && may && verb.EndsWith("한다")) verb = verb[..^2] + "할 수 있다";
            parts.Add(head + verb);
            prev = pk.Style;
        }
        return string.Join(", ", parts);
    }
}
