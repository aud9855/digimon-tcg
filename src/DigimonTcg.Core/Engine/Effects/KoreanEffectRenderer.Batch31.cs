using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.31.0 미지원 문장 5차 묶음의 생성 한글 (SentenceParser.Batch31.cs와 같은 범위) — 공식 한글 표기를 따른다:
/// "이 테이머가 레스트했을 때," (BT17-087) · "레드/옐로인 자신의 테이머가 소멸했을 때," (BT13-015) · "이 디지몬의 링크 카드가 효과로 파기되었을 때," (EX10-001) ·
/// "자신의 패가 자신의 효과로 파기되었을 때," (ST16-13) · "상대의 덱이 파기되었을 때," (BT14-077) · "2색 이상인 자신의 디지몬이 어택했을 때," (BT8-085) ·
/// "상대의 디지몬이 등장/진화했을 때," (BT17-053) · "다른 디지몬/테이머가 소멸했을 때," (BT19-075) · "DP의 합계가 8000 이하가 되도록 상대 디지몬을 골라, 고른 디지몬 전부를 소멸시킨다" (ST7-12) ·
/// "이 디지몬의 【진화 시】 효과 1개를 발휘한다" (BT20-035) · "이 디지몬은 이 디지몬의 진화원에 있는 명칭에 「감마몬」을 포함하는 카드의 효과 전부를 얻는다" (BT10-011)
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>절 전체 (라벨 포함): 진화원 카드의 효과 전부를 얻는다 · 다른 카드의 등장 코스트 감소</summary>
    private static string? ClauseKo31(string clause)
    {
        Match m;
        if ((m = Regex.Match(clause, $@"^(?<lab>\[All Turns\]\s*)?This Digimon gains all (?:of the )?effects (?:of|on) (?:cards with (?<inname>{TargetSpec.NameList}) in (?:their|its) names?|(?<exact>{TargetSpec.NameList})(?: cards?)?) in this Digimon's digivolution cards\.?$", RegexOptions.IgnoreCase)).Success)
        {
            string what;
            if (m.Groups["inname"].Success) { string x = NamesKo(m.Groups["inname"].Value); what = $"명칭에 {x}{J(x, "을", "를")} 포함하는 카드"; }
            else
            {
                var xs = TargetSpec.Names(m.Groups["exact"].Value).Select(n => $"「{NameKo(n)}」").ToList();
                what = string.Join(" ", xs.Select((x, i) => i < xs.Count - 1 ? x + J(x.Trim('「', '」'), "과", "와") : x));
            }
            return $"{(m.Groups["lab"].Success ? "【서로의 턴】" : "")}이 디지몬은 이 디지몬의 진화원에 있는 {what}의 효과 전부를 얻는다.";
        }
        if (SentenceParser.FieldPlayCostClause(clause) is { } fp)
        {
            var labels = new List<string>();
            foreach (Match l in Regex.Matches(fp.Groups["labels"].Value, @"\[([^\]]+)\]"))
            {
                if (LabelKo(l.Groups[1].Value.Trim()) is not { } lk) return null;
                labels.Add(lk);
            }
            if (FieldPlayCostTargetKo(fp.Groups["p"].Value) is not { } who) return null;
            string? cost = fp.Groups["cost"].Success ? CostKo(fp.Groups["cost"].Value) : "";
            string? per = fp.Groups["cnt"].Success ? CounterKo(fp.Groups["cnt"].Value) : "";
            if (cost == null || per == null) return null;
            string mid = string.Join(" ", new[] { cost, per }.Where(x => x.Length > 0));
            string verb = fp.Groups["may"].Success ? "할 수 있다" : "한다";
            return $"{string.Join(" ", labels)}{(labels.Count > 0 ? " " : "")}자신의 패에서 {who}{J(who, "이", "가")} 등장할 때, {(mid.Length > 0 ? mid + " " : "")}지불하는 등장 코스트를 -{fp.Groups["n"].Value} {verb}.";
        }
        return null;
    }

    /// <summary>등장 코스트 감소를 받는 카드 → "특징으로 「어드벤처」를 가진 디지몬 카드" · "「마린엔젤몬」" · "그린인 테이머 카드"</summary>
    private static string? FieldPlayCostTargetKo(string p)
    {
        p = Regex.Replace(p.Trim(), @"^(?:any of your|one of your|any|an?|1)\s+", "", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(p, $@"^{TargetSpec.NameList}$", RegexOptions.IgnoreCase))
            return NamesKo(p);
        if (Regex.Match(p, @"^Digimon or Tamers? (?<with>with .+)$", RegexOptions.IgnoreCase) is { Success: true } dt && WithKo(dt.Groups["with"].Value) is { } dtw)
            return $"{dtw} 디지몬/테이머";
        string q = Regex.Replace(p, @"\b(Digimon|Tamer|Option) cards?\b", "$1 card", RegexOptions.IgnoreCase);
        q = Regex.Replace(q, @"\b(Digimon|Tamer)(?! card)\b", "$1 card", RegexOptions.IgnoreCase);
        q = Regex.Replace(q, @"\bwith (\[[^\]]+\]) trait\b", "with the $1 trait", RegexOptions.IgnoreCase);
        return PickKo("1 " + q) is { } k ? Regex.Replace(k, @"\s*1장$", "") : null;
    }

    /// <summary>문장 하나 (Sentence가 먼저 본다)</summary>
    private static string? SentenceKo31(string s)
    {
        Match m;
        // 합계로 고르기 — 공식 (ST7-12 · BT8-105 · BT12-014 · BT14-068)
        if ((m = Regex.Match(s, @"^(?<may>you may )?(?:choose|delete) any number of (?:your opponent's|their) Digimon whose (?:total )?(?<k>DP|play costs?|levels?) adds? up to (?:(?<n>\d+) or (?:less|lower)|(?<self>equal (?:to )?or less than this Digimon's DP))(?: and (?<v>delete|return) them(?<bottom> to the bottom of (?:their owners?'s?|the|its owner's) decks?)?)?$", RegexOptions.IgnoreCase)).Success)
        {
            string k = m.Groups["k"].Value.ToLowerInvariant();
            string sum = k.StartsWith("dp") ? (m.Groups["self"].Success ? "DP의 합계가 이 디지몬의 DP 이하가 되도록" : $"DP의 합계가 {m.Groups["n"].Value} 이하가 되도록")
                : k.StartsWith("play") ? $"등장 코스트 합계 {m.Groups["n"].Value}까지" : $"Lv. 합계 {m.Groups["n"].Value}까지";
            bool ret = m.Groups["v"].Value.Equals("return", StringComparison.OrdinalIgnoreCase);
            if (!m.Groups["v"].Success && !s.StartsWith("delete", StringComparison.OrdinalIgnoreCase)) return null;
            string act = ret ? "덱 아래로 되돌린" : "소멸시킨";
            string end = m.Groups["may"].Success ? (ret ? "덱 아래로 되돌릴 수 있다" : "소멸시킬 수 있다") : act + "다";
            return $"{sum} 상대의 디지몬을 골라, 고른 디지몬 전부를 {end}";
        }
        if ((m = Regex.Match(s, @"^(?<may>you may )?(?<v>delete|return) up to (?<n>\d+) (?<k>DP|play cost's|play costs'|levels') total worth of (?:your opponent's|their) Digimon(?<bottom> to the bottom of (?:their owners?'s?|the|its owner's) decks?)?$", RegexOptions.IgnoreCase)).Success)
        {
            string k = m.Groups["k"].Value.ToLowerInvariant();
            string sum = k.StartsWith("dp") ? $"DP 합계 {m.Groups["n"].Value}까지" : k.StartsWith("play") ? $"등장 코스트 합계 {m.Groups["n"].Value}까지" : $"Lv. 합계 {m.Groups["n"].Value}까지";
            bool ret = m.Groups["v"].Value.Equals("return", StringComparison.OrdinalIgnoreCase);
            return $"{sum} 상대의 디지몬을 {(ret ? "덱 아래로 되돌" : "소멸시")}{(m.Groups["may"].Success ? (ret ? "릴 수 있다" : "킬 수 있다") : (ret ? "린다" : "킨다"))}";
        }
        // "For every 2 cards in this Digimon's digivolution cards, add 3000 to the maximum you can choose with this effect." — 공식 (BT12-014): "이 디지몬의 진화원 2장마다, 이 DP 소멸 효과의 상한 +3000"
        if ((m = Regex.Match(s, @"^(?:For (?:each|every) (?<cnt>.+?)|If (?<cond>[^,]+)), add (?<n>\d+) to (?:the (?<what>maximum(?: [^.]*)?)|this (?<what>level deletion effect's maximum))$", RegexOptions.IgnoreCase)).Success)
        {
            string what = m.Groups["what"].Value.ToLowerInvariant();
            string capKo = what.Contains("level") ? "Lv. 상한" : what.Contains("play cost") ? "등장 코스트 상한" : what.Contains("dp") ? "DP 상한" : "상한";
            string? head = m.Groups["cnt"].Success ? CounterKo(m.Groups["cnt"].Value)?.TrimEnd(',') : CondKo(m.Groups["cond"].Value)?.Then;
            return head == null ? null : $"{head} 이 효과의 {capKo} +{m.Groups["n"].Value}";
        }
        // 공식 (BT20-035 · EX6-043 · BT11-112): "이 디지몬의 【진화 시】 효과 1개를 발휘한다" · "~ 발휘할 수 있다" · "그 디지몬의 【진화 시】 효과 1개를 발휘한다"
        if ((m = Regex.Match(s, @"^(?<may>you may )?activate (?:1|one) of (?<who>this|that) Digimon's \[When Digivolving\] effects?$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["who"].Value.Equals("this", StringComparison.OrdinalIgnoreCase) ? "이" : "그")} 디지몬의 【진화 시】 효과 1개를 발휘{(m.Groups["may"].Success ? "할 수 있다" : "한다")}";
        // 공식 (BT13-021 · EX4-015): "서로는 덱에서 카드를 1장 뽑는다" · "서로는 덱 위에서부터 카드를 1장 뽑는다"
        if ((m = Regex.Match(s, @"^Both players draw (?:(?<n>\d+) cards? from their decks?|(?<top>the top card of their decks?))$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["top"].Success ? "서로는 덱 위에서부터 카드를 1장 뽑는다" : $"서로는 덱에서 카드를 {m.Groups["n"].Value}장 뽑는다";
        // "You may trash any 4 digivolution cards from your opponent's Digimon" — 공식 (BT14-023): "상대의 디지몬의 진화원을 선택하여 2장 파기한다"
        if ((m = Regex.Match(s, @"^you may trash (?:any )?(?<n>\d+) digivolution cards? (?:from|of|under) (?<one>(?:1|one) of )?your opponent's Digimon$", RegexOptions.IgnoreCase)).Success)
            return $"상대의 디지몬{(m.Groups["one"].Success ? " 1마리" : "")}의 진화원을 선택하여 {m.Groups["n"].Value}장 파기할 수 있다";
        // "Place the top 2 cards of your deck face down under this Tamer"
        if ((m = Regex.Match(s, @"^(?<may>you may )?place the top (?<n>\d+) cards of your deck(?<fd> face down)? under (?<who>this Tamer|(?:1|one) of your Tamers|your Tamers)$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 덱 위에서부터 {m.Groups["n"].Value}장을 {(m.Groups["fd"].Success ? "뒷면으로 " : "")}{(m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 테이머" : "자신의 테이머")} 아래에 놓{(m.Groups["may"].Success ? "을 수 있다" : "는다")}";
        return null;
    }

    /// <summary>비용 (CostKo가 먼저 본다)</summary>
    private static string? CostKo31(string cost)
    {
        if (Regex.IsMatch(cost, @"^returning this card to the bottom of (?:the|your|its owner's) deck$", RegexOptions.IgnoreCase)) return "이 카드를 덱 아래로 되돌리는 것으로,";
        if (Regex.IsMatch(cost, @"^deleting this Tamer$", RegexOptions.IgnoreCase)) return "이 테이머를 소멸시키는 것으로,";
        // 공식 (BT14-098): "자신의 트래시에서 특징으로 「D-브리가드」/「디지대」를 가진 카드 3장을 덱 위로 되돌리는 것으로,"
        if (Regex.Match(cost, @"^returning (?<pick>\d+ cards? .+?) from your trash to the (?<pos>top|bottom) of (?:the|your) deck$", RegexOptions.IgnoreCase) is { Success: true } rt
            && PickKo(rt.Groups["pick"].Value) is { } rk)
            return $"자신의 트래시에서 {rk}{J(rk, "을", "를")} 덱 {(rt.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}로 되돌리는 것으로,";
        return null;
    }

    /// <summary>셈 (CounterKo가 먼저 본다) — 공식 (BT11-059 · BT18-085): "그린/블랙인 자신의 테이머 1명마다" · "상대의 트래시의 카드의 색 1색마다"</summary>
    private static string? CounterKo31(string counter)
    {
        Match m;
        if ((m = Regex.Match(counter, $@"^(?:of your (?<color>(?:{Colors})(?: or (?:{Colors}))+) Tamers|(?<color>(?:{Colors})(?: or (?:{Colors}))+) Tamer you have(?: in play)?)$", RegexOptions.IgnoreCase)).Success)
            return $"{ColorsKo(m.Groups["color"].Value)}인 자신의 테이머 1명마다";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?colou?rs? in your (?<opp>opponent's )?trash$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["opp"].Success ? "상대" : "자신")}의 트래시의 카드의 색 {(m.Groups["n"].Success ? m.Groups["n"].Value : "1")}색마다";
        return null;
    }

    /// <summary>"When ~," 사건 (SourceEventKo 다음)</summary>
    private static string? EventKo31(string ev)
    {
        Match m;
        // ---- 테이머 ----
        if (Regex.IsMatch(ev, $@"^this Tamer {SuspendVerbEn}$", RegexOptions.IgnoreCase)) return "이 테이머가 레스트했을 때,";
        if (Regex.IsMatch(ev, $@"^this Tamer {UnsuspendVerbEn}$", RegexOptions.IgnoreCase)) return "이 테이머가 액티브가 되었을 때,";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers? (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["color"].Success ? ColorsKo(m.Groups["color"].Value) + "인 " : "")}자신의 테이머가 소멸했을 때,";
        if ((m = Regex.Match(ev, @"^(?:an effect trashes|effects trash) (?:a card|cards) from under (?<who>your Tamers|this Tamer|(?:one|any) of your Tamers)$", RegexOptions.IgnoreCase)).Success)
            return $"효과로 {(m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 테이머" : "자신의 테이머")} 아래의 카드가 파기되었을 때,";
        // ---- 링크 ----
        if ((m = Regex.Match(ev, @"^(?:an effect trashes|effects trash) (?:any of |one of )?(?:(?<self>this Digimon's)|your Digimon's) link cards?$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["self"].Success ? "이 디지몬" : "자신의 디지몬")}의 링크 카드가 효과로 파기되었을 때,";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your Digimon (?:gets?|are|is) linked (?:to|with) (?:an? |1 )?(?<x>{TargetSpec.NameList}) trait card$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return $"자신의 디지몬에 특징으로 {x}{J(x, "을", "를")} 가진 카드가 링크했을 때,"; }
        // ---- 패 · 덱 ----
        if (Regex.IsMatch(ev, @"^an effect trashes (?:a card|cards) in your hand$", RegexOptions.IgnoreCase)) return "자신의 패가 효과로 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your hand by (?:your|one of your) effects?$", RegexOptions.IgnoreCase)) return "자신의 패가 자신의 효과로 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your hand$", RegexOptions.IgnoreCase)) return "자신의 패가 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^hands are trashed from$|^(?:a player's|either player's|any player's) hand is trashed from$", RegexOptions.IgnoreCase)) return "어느 쪽이든 패가 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your opponent's hand$", RegexOptions.IgnoreCase)) return "상대의 패가 파기되었을 때,";
        if ((m = Regex.Match(ev, @"^(?:an? )?(?<pick>[^,]+?cards?(?: with [^,]+?)?) (?:is|are) trashed from your hand$", RegexOptions.IgnoreCase)).Success)
        {
            string pick = Regex.Replace(Regex.Replace(m.Groups["pick"].Value, @"\bcards\b", "card", RegexOptions.IgnoreCase), @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase);
            if (PickKo("1 " + pick) is { } pk) { pk = Regex.Replace(pk, @"\s*1장$", ""); return $"자신의 패에서 {pk}{J(pk, "이", "가")} 파기되었을 때,"; }
        }
        if (Regex.IsMatch(ev, @"^you trash this card in your hand (?:using|with|by) (?:one of your|your) effects?$|^one of your effects trashes this card in your hand$", RegexOptions.IgnoreCase)) return "자신의 효과로 이 카드가 패에서 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^your opponent's deck is trashed from$|^(?:a card|cards) in your opponent's deck (?:is|are) trashed$|^(?:a card is|cards are) trashed from your opponent's deck$", RegexOptions.IgnoreCase)) return "상대의 덱이 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect trashes|effects trash(?:es)?) (?:a card|cards) from your deck$|^your deck is trashed from$", RegexOptions.IgnoreCase)) return "효과로 자신의 덱이 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect adds|effects add) (?:(?:a card|cards) )?to your opponent's hand$", RegexOptions.IgnoreCase)) return "효과로 상대의 패에 카드가 추가되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect adds|effects add) (?:(?:a card|cards) )?to (?:your or your opponent's|either player's|a player's) hand$", RegexOptions.IgnoreCase)) return "효과로 자신 또는 상대의 패에 카드가 추가되었을 때,";
        // ---- 어택 ----
        if ((m = Regex.Match(ev, @"^you attack with (?<subj>(?:an?|1) [^,]*?Digimon(?: (?:with|that has) [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
            && AttackerSubjectKo(m.Groups["subj"].Value) is { } atk)
            return $"{atk}{J(atk, "이", "가")} 어택했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?!opponent)[^,]*?Digimon(?: with [^,]+?)?) attacks?$", RegexOptions.IgnoreCase)).Success
            && AttackerSubjectKo(m.Groups["subj"].Value) is { } ally)
            return $"{ally}{J(ally, "이", "가")} 어택했을 때,";
        if ((m = Regex.Match(ev, @"^(?:any )?Digimon attack(?<p> (?:players|a player))?$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["p"].Success ? "디지몬이 플레이어에게 어택했을 때," : "디지몬이 어택했을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon attacks? (?:your opponent's|an opponent's|a) Digimon$", RegexOptions.IgnoreCase)) return "자신의 디지몬이 상대의 디지몬에게 어택했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of your|an|any of your) opponent's Digimon attacks (?:one of |any of )?your (?<spec>[^,]*?)Digimon$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["spec"].Value + "Digimon", bare: true) is { } def)
            return $"상대의 디지몬이 {def}에게 어택했을 때,";
        if (Regex.IsMatch(ev, @"^(?:your|an) opponent's Digimon with the highest DP attacks$", RegexOptions.IgnoreCase)) return "가장 DP가 높은 상대의 디지몬이 어택했을 때,";
        // ---- 진화 ----
        if ((m = Regex.Match(ev, @"^(?:an|one of your|any of your) opponent's Digimon digivolves?(?: into (?<into>[^,]+))?$", RegexOptions.IgnoreCase)).Success)
        {
            if (!m.Groups["into"].Success) return "상대의 디지몬이 진화했을 때,";
            string into = Regex.Replace(m.Groups["into"].Value.Trim(), @"^an? ", "", RegexOptions.IgnoreCase);
            if (PickKo("1 " + Regex.Replace(into, @"\bDigimon$", "Digimon card", RegexOptions.IgnoreCase)) is { } ik)
            { ik = Regex.Replace(ik, @"\s*(?:카드\s*)?1장$", ""); ik = Regex.Replace(ik, @"디지몬 카드$", "디지몬"); return $"상대의 디지몬이 {ik}{J(ik, "으로", "로")} 진화했을 때,"; }
            return null;
        }
        if ((m = Regex.Match(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:is|are) played or (?<w>digivolves?|deleted)$", RegexOptions.IgnoreCase)).Success)
            return $"상대의 디지몬이 등장/{(m.Groups["w"].Value.StartsWith("digivolv", StringComparison.OrdinalIgnoreCase) ? "진화" : "소멸")}했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect digivolves|effects digivolve) (?:your|an|one of your) opponent's Digimon$", RegexOptions.IgnoreCase)) return "효과로 상대의 디지몬이 진화했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect plays or digivolves|effects play or digivolve) (?:your|an|one of your) opponent's Digimon$", RegexOptions.IgnoreCase)) return "효과로 상대의 디지몬이 등장/진화했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect plays or digivolves|effects play or digivolve) (?:any|a) Digimon$", RegexOptions.IgnoreCase)) return "효과로 디지몬이 등장/진화했을 때,";
        // ---- 등장 ----
        if ((m = Regex.Match(ev, @"^(?<other>other |another )?(?:any )?Digimon (?:is |are )?played(?<evo> or digivolves?)?$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["other"].Success ? "다른 " : "")}디지몬이 등장{(m.Groups["evo"].Success ? "/진화" : "")}했을 때,";
        if ((m = Regex.Match(ev, @"^(?:an|your) opponent plays (?:a|an|1) Digimon(?: card)?(?<tamer> or Tamer)?$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["tamer"].Success ? "상대의 디지몬/테이머가 등장했을 때," : "상대의 디지몬이 등장했을 때,";
        if (Regex.IsMatch(ev, @"^any of your opponent's Digimon (?:is|are) played$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 등장했을 때,";
        if ((m = Regex.Match(ev, @"^(?<mine>one of your effects|an effect) plays another Digimon$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["mine"].Value.StartsWith("one of your", StringComparison.OrdinalIgnoreCase) ? "자신의 효과로 다른 디지몬이 등장했을 때," : "효과로 다른 디지몬이 등장했을 때,";
        if ((m = Regex.Match(ev, @"^(?:you play (?:a|an|1) (?<s>card[^,]*)|(?:a|an) (?<s>card[^,]*?) is played)$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + m.Groups["s"].Value) is { } cp)
        {
            cp = Regex.Replace(cp, @"\s*1장$", "");
            return ev.StartsWith("you play", StringComparison.OrdinalIgnoreCase) ? $"자신이 {cp}{J(cp, "을", "를")} 등장시켰을 때," : $"{cp}{J(cp, "이", "가")} 등장했을 때,";
        }
        // ---- 소멸 ----
        if ((m = Regex.Match(ev, @"^(?<other>other |another )?(?:any )?Digimon(?<tam> or Tamers?)? (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["other"].Success ? "다른 " : "")}디지몬{(m.Groups["tam"].Success ? "/테이머" : "")}{(m.Groups["tam"].Success ? "가" : "이")} 소멸했을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your other Digimon or Tamers (?:is|are) deleted$", RegexOptions.IgnoreCase)) return "다른 자신의 디지몬/테이머가 소멸했을 때,";
        if ((m = Regex.Match(ev, @"^(?:an|your|one of your|any of your) opponent's Digimon (?:is|are) deleted (?<how>in battle(?: or by having 0 DP)?|by having 0 DP)$", RegexOptions.IgnoreCase)).Success)
        {
            string how = m.Groups["how"].Value.ToLowerInvariant();
            return how.Contains("battle") && how.Contains("0 dp") ? "상대의 디지몬이 배틀 또는 DP 0으로 소멸했을 때," : how.Contains("battle") ? "상대의 디지몬이 배틀에서 소멸했을 때," : "상대의 디지몬이 DP 0으로 소멸했을 때,";
        }
        if ((m = Regex.Match(ev, @"^an effect deletes (?:(?<another>another Digimon)|(?<opp>an opponent's Digimon|one of your opponent's Digimon)|one of your other Digimon)$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["opp"].Success ? "효과로 상대의 디지몬이 소멸했을 때," : m.Groups["another"].Success ? "효과로 다른 디지몬이 소멸했을 때," : "다른 자신의 디지몬이 효과로 소멸했을 때,";
        if ((m = Regex.Match(ev, @"^(?:another|any other) (?<spec>[^,]*?Digimon(?: with [^,]+?)?) (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + Regex.Replace(m.Groups["spec"].Value, @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase), bare: true) is { } od)
        {
            string mods = Regex.Replace(od, @"자신의 디지몬$", "").Trim();
            return $"{(mods.Length > 0 ? mods + " " : "")}다른 디지몬이 소멸했을 때,";
        }
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your other Digimon with the same name as this Digimon (?:is|are) deleted$", RegexOptions.IgnoreCase)) return "이 디지몬과 같은 명칭의 다른 자신의 디지몬이 소멸했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<spec>[^,]*?Digimon) with digivolution cards (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["spec"].Value, bare: true) is { } ws)
            return $"진화원을 가진 {ws}{J(ws, "이", "가")} 소멸했을 때,";
        // ---- 옵션 · 시큐리티 ----
        if (Regex.IsMatch(ev, @"^(?:an|any) Option cards? (?:is|are) used$", RegexOptions.IgnoreCase)) return "옵션 카드가 사용되었을 때,";
        if (Regex.IsMatch(ev, @"^your security stack is added to$", RegexOptions.IgnoreCase)) return "자신의 시큐리티에 카드가 놓였을 때,";
        if (Regex.IsMatch(ev, @"^your opponent's security (?:stack )?is removed from$", RegexOptions.IgnoreCase)) return "상대의 시큐리티에서 카드가 없어졌을 때,";

        // ---- 진화 코스트 감소의 시점 (이름 · "you would digivolve this Digimon into") — 공식 (BT3-111 · P-029) ----
        if ((m = Regex.Match(ev, $@"^(?:one|any) of your (?<x>{TargetSpec.NameList}) would digivolve into this card(?: in (?:the|your) hand)?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"자신의 {x}{J(x, "이", "가")} 패의 이 카드로 진화할 때,"; }
        if ((m = Regex.Match(ev, @"^you would digivolve this Digimon into (?:an? |1 )?(?<into>.+?)(?: in (?:the|your) hand)?$", RegexOptions.IgnoreCase)).Success
            && (Regex.IsMatch(m.Groups["into"].Value, $@"^{TargetSpec.NameList}$") ? NamesKo(m.Groups["into"].Value) : EvoIntoKo("a " + m.Groups["into"].Value)) is { } yi)
            return $"이 디지몬이 패의 {yi}{J(yi, "으로", "로")} 진화할 때,";
        // 사건 안의 쉼표 목록 "any of your [Machine], [Cyborg] or [ME] trait Digimon" — "[A] or [B]"와 같은 한글 (「A」/「B」/「C」)
        if (ev.Contains(", [") && EventKo(Regex.Replace(ev, @",\s*(?=\[)", " or ")) is { } listed)
            return listed;

        // ---- "A or B" (각각 한글로 바꿀 수 있을 때만): "~되거나, ~되었을 때," ----
        foreach (Match or in Regex.Matches(ev, @"\s+or\s+"))
        {
            string left = ev[..or.Index].Trim(), right = ev[(or.Index + or.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0 || EventKo(left) is not { } a) continue;
            string? b = EventKo(right);
            if (b == null && Regex.Match(left, @"^(?<subj>this Digimon|this Tamer)\b", RegexOptions.IgnoreCase) is { Success: true } subj)
                b = EventKo(subj.Groups["subj"].Value + " " + right) is { } bb ? Regex.Replace(bb, @"^이 (?:디지몬|테이머)(?:이|가) ", "") : null;
            if (b == null || OrForm(a) is not { } aa) continue;
            return $"{aa} {b}";
        }
        return null;
    }

    /// <summary>"~했을 때," → "~하거나," · "~되었을 때," → "~되거나," (앞 사건을 "또는"으로 잇는다)</summary>
    private static string? OrForm(string ko)
    {
        ko = ko.TrimEnd(',', ' ');
        if (!ko.EndsWith("때")) return null;
        string stem = Regex.Replace(ko, @"\s*때$", "");
        if (stem.EndsWith("했을")) return stem[..^2] + "하거나,";
        if (stem.EndsWith("되었을")) return stem[..^3] + "되거나,";
        if (stem.EndsWith("났을")) return stem[..^2] + "나거나,";
        if (stem.EndsWith("졌을")) return stem[..^2] + "지거나,";
        return null;
    }

    /// <summary>어택한 자신의 디지몬 (SentenceParser.AttackerSubject와 같은 범위) → "명칭에 「앙고라몬」을 포함하거나, Lv.5 이상인 자신의 디지몬" 등</summary>
    private static string? AttackerSubjectKo(string subj)
    {
        string s = Regex.Replace(subj.Trim(), @"^(?:an?|1|one of your|any of your)\s+", "", RegexOptions.IgnoreCase);
        Match m;
        if ((m = Regex.Match(s, $@"^Digimon (?:that has|with) (?<x>{TargetSpec.NameList}) in (?:its|their) names? or is level (?<lv>\d+) or (?<dir>higher|lower)$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"명칭에 {x}{J(x, "을", "를")} 포함하거나, Lv.{m.Groups["lv"].Value} {Dir(m.Groups["dir"].Value)}인 자신의 디지몬"; }
        if ((m = Regex.Match(s, $@"^Digimon with (?<x>{TargetSpec.NameList}) in (?:its|their) digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"진화원에 {x}{J(x, "을", "를")} 가진 자신의 디지몬"; }
        if ((m = Regex.Match(s, @"^Digimon with (?<n>\d+) or more colors$", RegexOptions.IgnoreCase)).Success)
            return $"{m.Groups["n"].Value}색 이상인 자신의 디지몬";
        if ((m = Regex.Match(s, $@"^(?<n>\d+)-colou?r (?<a>{Colors}) and (?<b>{Colors}) Digimon$", RegexOptions.IgnoreCase)).Success)
            return $"{ColorKo(m.Groups["a"].Value)}/{ColorKo(m.Groups["b"].Value)}인 {m.Groups["n"].Value}색의 자신의 디지몬";
        s = Regex.Replace(s, @"\bin any of their traits\b", "in any of its traits", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase);
        return TargetKo("all of your " + s, bare: true);
    }
}
