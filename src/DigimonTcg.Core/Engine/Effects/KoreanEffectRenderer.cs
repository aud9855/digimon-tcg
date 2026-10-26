using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// 효과 확장 6단계 — 영어 효과 문장을 공식 한글판 표기(【등장 시】 · 《블로커》 · 「카드명」 · "~의 DP를 +2000 한다")에 맞춘 한글 문장으로 만든다.
/// 공식 한글 문구가 없는 카드(신규 세트 · 다른 PC)의 화면 표시용이며, 대전 판정은 계속 영어 문장(SentenceParser)으로 한다.
/// 문장 틀은 SentenceParser와 같은 구조(조건 · "~할 수 있다" · 비용 · 셈 · 동작 · 기간)를 따른다.
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>번역하지 못한 절 앞에 붙는 표시 (영어 원문을 그대로 보여 준다)</summary>
    public const string UntranslatedMark = "※(원문) ";

    /// <summary>
    /// 카드의 효과 칸 하나(메인 / 진화원)를 한글로. 번역하지 못한 절은 영어 원문 앞에 <see cref="UntranslatedMark"/>를 붙인다.
    /// 칸이 비어 있으면 빈 문자열, 절을 하나도 번역하지 못하면 null.
    /// </summary>
    public static string? Render(CardData card, bool inherited)
    {
        string text = inherited ? card.InheritedEffectText : card.EffectText;
        var clauses = KeywordDictionary.SplitClauses(text).ToList();
        if (clauses.Count == 0)
            return string.Empty;
        var lines = new List<string>();
        int translated = 0;
        foreach (var clause in clauses)
        {
            var ko = RenderClause(clause);
            if (ko != null) { translated++; lines.Add(ko); }
            else lines.Add(UntranslatedMark + Regex.Replace(SentenceParser.NormalizeForRender(clause), @"^(?:Inherited|Security) Effect\s*", ""));
        }
        return translated == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>절 하나 → 한글 (번역할 수 없으면 null)</summary>
    public static string? RenderClause(string rawClause)
    {
        _mentionedTarget = false;
        _eventOther = false;
        _tamerSubject = false;
        _oppVerb34 = null;
        string clause = SentenceParser.NormalizeForRender(rawClause);
        _leaveKind = LeaveKindOf(clause);   // v0.32.0 "Prevent it"의 한글 (벗어나지 / 소멸하지 / 소멸하지 않고, 패/덱으로 되돌아가지)
        clause = Regex.Replace(clause, @"^(?:Inherited|Security) Effect\s*", "", RegexOptions.IgnoreCase).Trim();
        clause = Regex.Replace(clause, @"\byou have don't have\b", "you don't have", RegexOptions.IgnoreCase);   // API 원문 오타
        // API 원문의 짝 없는 괄호: "…cost.))" · "＜Security A. +1}＞"
        while (clause.EndsWith(')') && clause.Count(ch => ch == ')') > clause.Count(ch => ch == '('))
            clause = clause[..^1].TrimEnd();
        if (!clause.Contains('{')) clause = clause.Replace("}", "");
        if (clause.Length == 0)
            return string.Empty;

        // 고르는 효과의 선택지 줄 "・~"
        if (clause.StartsWith("・"))
            return Body(clause.TrimStart('・').Trim()) is { } option ? "・" + option : null;

        // 키워드만 있는 절: "＜Blocker＞" · "＜Rush＞ ＜Piercing＞"
        if (Regex.IsMatch(clause, @"^(?:＜[^＞]+＞\s*)+\.?$"))
        {
            var parts = Regex.Matches(clause, @"＜([^＞]+)＞").Select(m => KeywordKo(m.Groups[1].Value, withReminder: true)).ToList();
            return parts.All(p => p != null) ? string.Join(" ", parts) : null;
        }
        // 링크 조건: "Link Requirements [Link] [Appmon] trait: Cost 2" → 〈링크〉특징 「어플몬」 : 코스트 2
        if (Regex.Match(clause, @"^Link Requirements \[Link\]\s*(?<req>.+?)\s*:\s*Cost (?<n>\d+)\.?$", RegexOptions.IgnoreCase) is { Success: true } link
            && SpecKo(link.Groups["req"].Value) is { } linkReq)
            return $"〈링크〉{linkReq} : 코스트 {link.Groups["n"].Value} (패/배틀 에어리어에서 이 카드를 배틀 에어리어의 지정된 디지몬에 가로로 끼운다)";
        // 사용 조건: "Use Requirement: Glowing Dawn trait" → 사용 조건: 특징「글로잉 던」
        if (Regex.Match(clause, @"^Use Requirement:\s*(?<x>.+?)\s+trait\.?$", RegexOptions.IgnoreCase) is { Success: true } useReq)
            return $"사용 조건: 특징「{TraitKo(useReq.Groups["x"].Value)}」";
        if (Regex.Match(clause, @"^(?:Ace )?Overflow ＜-(?<n>\d+)＞\.?$", RegexOptions.IgnoreCase) is { Success: true } of)
            return $"〈오버플로우 《-{of.Groups["n"].Value}》〉 (이 카드가 배틀 에어리어나 진화원에서 다른 곳으로 이동할 때, 메모리를 -{of.Groups["n"].Value} 한다)";
        if (Regex.Match(clause, @"^Your (?<who>.+?) can digivolve into this card in (?:your|the) hand for a (?:memory |digivolution )?cost of (?<n>\d+), ignoring (?:this card's )?digivolution requirements\.?$", RegexOptions.IgnoreCase) is { Success: true } sp
            && WhoKo(sp.Groups["who"].Value) is { } spWho)
            return $"자신의 {spWho}{J(spWho, "은", "는")} 패의 이 카드로, 진화 조건을 무시하고 진화 코스트 {sp.Groups["n"].Value}로 진화할 수 있다.";
        // v0.30.0 테이머에서 진화 — 공식 (BT4-011 · BT7-011 · BT12-012): "패의 이 카드는, 레드인 자신의 테이머를 레드인 Lv.3의 디지몬으로서 취급하여 (진화 코스트 2를 지불하여) 진화할 수 있다."
        if (Regex.Match(clause, $@"^You may digivolve this card from your hand onto one of your (?:(?<tcolor>{Colors}) Tamers|(?<any>Tamers)|(?<names>\[[^\]]+\](?: or \[[^\]]+\])*)s?) as if (?:the Tamer|that card|it) (?:is|were) a level (?<lv>\d+) (?<lcolor>{Colors}) Digimon(?: for a (?:memory |digivolution )?cost of (?<n>\d+))?\.?$", RegexOptions.IgnoreCase) is { Success: true } te)
        {
            string tamerKo = te.Groups["tcolor"].Success ? $"{ColorKo(te.Groups["tcolor"].Value)}인 자신의 테이머" : te.Groups["names"].Success ? $"자신의 {NamesKo(te.Groups["names"].Value)}" : "자신의 테이머";
            string pay = te.Groups["n"].Success ? $"진화 코스트 {te.Groups["n"].Value}{J(te.Groups["n"].Value, "을", "를")} 지불하여 " : "";
            return $"패의 이 카드는, {tamerKo}{J(tamerKo, "을", "를")} {ColorKo(te.Groups["lcolor"].Value)}인 Lv.{te.Groups["lv"].Value}의 디지몬으로서 취급하여 {pay}진화할 수 있다.";
        }
        // 코스트 감소 (공식 표기): "패의 이 카드를 등장시킬 때, 자신의 「디아블로몬」 1마리를 소멸시키는 것으로, 지불하는 등장 코스트를 -12 한다."
        // v0.29.0: 사용 코스트 증가 "For each of your security cards, add 1 to this card's use cost." · "Add 1 to this card's use cost for each ~"
        if (Regex.Match(clause, @"^(?:For (?:each|every) (?<c1>[^,]+), add (?<n1>\d+) to this card's (?:use |play )?cost|Add (?<n2>\d+) to this card's (?:use |play )?cost for (?:each|every) (?<c2>.+?))\.?$", RegexOptions.IgnoreCase) is { Success: true } addc
            && CounterKo(addc.Groups["c1"].Success ? addc.Groups["c1"].Value : addc.Groups["c2"].Value) is { } addPer)
            return $"{addPer} 지불하는 사용 코스트를 +{(addc.Groups["n1"].Success ? addc.Groups["n1"].Value : addc.Groups["n2"].Value)} 한다.";
        if (SentenceParser.PlayCostClause34.Match(clause) is { Success: true } pc34 && pc34.Groups["each"].Success && ClauseKo34(clause) is { } each34) return each34;   // v0.34.0 "~ for each one"
        if (Regex.Match(clause, @"^When (?:(?<play>this card would be played|you would play this card(?: from your hand)?)|you would use this card|this card would be used), (?:if (?<cond>(?:[^,]|,\s*(?=\[))+), )?(?:by (?<cost>[^,]+), |you may (?<cverb>suspend|trash|delete|return|place)\b(?<cobj>[^,]*?) to )?reduce (?:the|its|this card's) (?:play |use |memory )?cost by (?<n>\d+)(?: for (?:each|every) (?<cnt>[^,]+?))?(?: if (?<cond2>[^,]+?))?\.?$", RegexOptions.IgnoreCase) is { Success: true } red)
        {
            bool play = red.Groups["play"].Success;
            // 조건이 앞("if ~, reduce") 또는 뒤("reduce ~ if ~") — 공식은 "~있다면 지불하는"
            string? cond = red.Groups["cond"].Success ? CondKo(red.Groups["cond"].Value)?.Then : red.Groups["cond2"].Success ? CondKo(red.Groups["cond2"].Value)?.Then : "";
            string? cost = red.Groups["cost"].Success ? CostKo(red.Groups["cost"].Value)
                : red.Groups["cverb"].Success ? CostKo($"{Gerund(red.Groups["cverb"].Value)}{red.Groups["cobj"].Value}") : "";
            string? per = red.Groups["cnt"].Success ? CounterKo(red.Groups["cnt"].Value) : "";
            if (cond == null || cost == null || per == null) return null;
            string mid = string.Join(" ", new[] { cond.TrimEnd(','), cost, per }.Where(x => x.Length > 0));
            return $"{(play ? "패의 이 카드를 등장시킬 때," : "이 카드를 사용할 때,")} {(mid.Length > 0 ? mid + " " : "")}지불하는 {(play ? "등장" : "사용")} 코스트를 -{red.Groups["n"].Value} 한다.";
        }

        if (ClauseKo31(clause) is { } clause31) return clause31;   // v0.31.0 (KoreanEffectRenderer.Batch31.cs)
        if (ClauseKo32(clause) is { } clause32) return clause32;   // v0.32.0 (KoreanEffectRenderer.Batch32.cs)
        if (ClauseKo34(clause) is { } clause34) return clause34;   // v0.34.0 (KoreanEffectRenderer.Batch34.cs)

        var prefix = Regex.Match(clause, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        var labels = new List<string>();
        foreach (Match l in Regex.Matches(prefix.Groups[1].Value, @"\[([^\]]+)\]"))
        {
            var ko = LabelKo(l.Groups[1].Value.Trim());
            if (ko == null) return null;
            labels.Add(ko);
        }
        string body = prefix.Groups[2].Value.Trim();
        if (body.Length == 0)
            return string.Join(" ", labels);
        var rendered = Body(body);
        if (rendered == null) return null;
        return labels.Count > 0 ? $"{string.Join(" ", labels)} {rendered}" : rendered;
    }

    // =====================================================================
    // 문장
    // =====================================================================

    private static string? Body(string body)
    {
        // 고르는 효과 머리말
        if (Regex.Match(body, @"^(?:If (?<cond>[^,]+), )?(?:you may )?(?:By (?<cost>[^,]+), )?(?:you may )?activate (?:1|one) of the (?:effects below|following effects)[:.]?$", RegexOptions.IgnoreCase) is { Success: true } choice)
        {
            string head = "";
            if (choice.Groups["cond"].Success) { if (CondKo(choice.Groups["cond"].Value) is not { } c) return null; head += c.If + " "; }
            if (choice.Groups["cost"].Success) { if (CostKo(choice.Groups["cost"].Value) is not { } k) return null; head += k + " "; }
            return head + "이하의 효과에서 1개를 골라 발휘한다.";
        }
        if (Regex.Match(body, @"^(?:If (?<cond>[^,]+), )?＜Digi-Burst (?<n>\d+)＞\.?$", RegexOptions.IgnoreCase) is { Success: true } burst)
        {
            string head = burst.Groups["cond"].Success ? CondKo(burst.Groups["cond"].Value)?.If ?? "\0" : "";
            if (head == "\0") return null;
            return $"{(head.Length > 0 ? head + " " : "")}{KeywordKo("Digi-Burst " + burst.Groups["n"].Value, withReminder: true)}";
        }
        if (Regex.Match(body, @"^activate the (?:following effect|effect below) (?<k>twice|\d+ times)[:.]?$", RegexOptions.IgnoreCase) is { Success: true } times)
            return $"이하의 효과를 {(times.Groups["k"].Value.Equals("twice", StringComparison.OrdinalIgnoreCase) ? "2" : Regex.Match(times.Groups["k"].Value, @"\d+").Value)}회 발휘한다.";
        if (Regex.Match(body, @"^For (?:each|every) (?<cnt>[^,]+), activate the (?:following effect|effect below)[:.]?$", RegexOptions.IgnoreCase) is { Success: true } fe)
            return CounterKo(fe.Groups["cnt"].Value) is { } fc ? $"{fc} 이하의 효과를 발휘한다." : null;
        if (Regex.Match(body, @"^activate (?:1|one) of the effects below\. If (?<all>[^,]+), activate all of the effects below instead[:.]?$", RegexOptions.IgnoreCase) is { Success: true } oa)
            return CondKo(oa.Groups["all"].Value) is { } ac ? $"이하의 효과에서 1개를 골라 발휘한다. {ac.Then} 대신에 이하의 효과를 전부 발휘한다." : null;

        // 지속 효과 · "When ~," 발동
        var sentences = SentenceParser.SentencesForRender(body);
        var outList = new List<string>();
        foreach (var raw in sentences)
        {
            var ko = Sentence(raw.Trim());
            if (ko == null) return null;
            outList.Add(ko.TrimEnd('.', ' '));
        }
        return string.Join(". ", outList) + ".";
    }

    /// <summary>문장 하나 → 한글 (마침표 없이)</summary>
    private static string? Sentence(string s, bool nested = false)
    {
        s = s.Trim().TrimEnd('.').Trim();
        Match m;
        if ((m = Regex.Match(s, @"^Then,?\s+(?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
            return Sentence(Cap(m.Groups["rest"].Value)) is { } then ? "그 후, " + then : null;
        if (PreSentenceKo34(s) is { } pre34) return pre34;   // v0.34.0 (KoreanEffectRenderer.Batch34.cs) — 1장씩 놓기 · "~ don't affect this Digimon and it gets ~"
        if (SentenceKo31(s) is { } sentence31) return sentence31;   // v0.31.0
        if (SentenceKo32(s) is { } sentence32) return sentence32;   // v0.32.0

        // v0.30.0 — 공식 (BT18-029 · BT19-052 · ST21-06 · BT21-078): "다른 자신의 디지몬 1마리마다 이 효과의 Lv. 상한 +1" · "~ 이 효과의 등장 코스트 상한 +2" ·
        // "~ 이 효과의 DP 상한 +2000" · "자신의 테이머의 색 합계가 2색 이상이라면, 이 효과의 Lv. 상한 +1"
        if ((m = Regex.Match(s, @"^(?:For (?:each|every) (?<cnt>.+?)|If (?<cond>[^,]+)), add (?<n>\d+) to this effect's (?<what>choosable levels|level maximum|maximum level|play cost maximum|maximum play cost|DP maximum|maximum DP|DP reduction)$", RegexOptions.IgnoreCase)).Success)
        {
            string what = m.Groups["what"].Value.ToLowerInvariant();
            string capKo = what.Contains("level") ? "Lv. 상한" : what.Contains("play cost") ? "등장 코스트 상한" : what.Contains("reduction") ? "DP 마이너스" : "DP 상한";
            string? head = m.Groups["cnt"].Success ? CounterKo(m.Groups["cnt"].Value)?.TrimEnd(',') : CondKo(m.Groups["cond"].Value)?.Then;
            return head == null ? null : $"{head} 이 효과의 {capKo} +{m.Groups["n"].Value}";
        }
        // "If ~, ~ instead" — 대신
        if ((m = Regex.Match(s, @"^If (?<c>[^,]+), (?:instead,? (?<rest>.+?)|(?<rest>.+?),? instead)$", RegexOptions.IgnoreCase)).Success)
            return CondKo(m.Groups["c"].Value) is { } ic && Sentence(Cap(m.Groups["rest"].Value)) is { } ir ? $"{(nested ? ic.Then : ic.If)} 대신에 {ir}" : null;
        // "If ~," / "While ~,"
        if ((m = Regex.Match(s, @"^(?<w>If|While) (?<c>[^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            var c = CondKo(m.Groups["c"].Value);
            // v0.33.0 조건 안의 쉼표 목록 "If this Digimon has the [Angel], [Archangel] or [Three Great Angels] trait, ~" (SentenceParser.ParseStep과 같은 읽기)
            if (c == null && Regex.Match(s, SentenceParser.IfWithListPattern, RegexOptions.IgnoreCase) is { Success: true } il && il.Groups["c"].Value != m.Groups["c"].Value
                && CondKo(_eventOther && Regex.IsMatch(il.Groups["c"].Value, @"^it (?:has|have|is)\b", RegexOptions.IgnoreCase) ? "that Digimon" + il.Groups["c"].Value[2..] : il.Groups["c"].Value) is { } lc)
            { c = lc; m = il; }
            if (c == null) return null;
            var r = Sentence(Cap(m.Groups["rest"].Value), nested: true);
            // v0.33.0 앞의 조건이 쉼표 목록 앞에서 끊겨도 읽혔을 때 ("While you have a level 5 [WarGrowlmon], [Taomon] or [Rapidmon], ~") — 목록까지 넓혀 다시
            if (r == null && Regex.Match(s, SentenceParser.IfWithListPattern, RegexOptions.IgnoreCase) is { Success: true } il2 && il2.Groups["c"].Value != m.Groups["c"].Value
                && CondKo(il2.Groups["c"].Value) is { } lc2 && Sentence(Cap(il2.Groups["rest"].Value), nested: true) is { } r2)
            { c = lc2; m = il2; r = r2; }
            if (r == null) return null;
            return $"{(m.Groups["w"].Value.Equals("While", StringComparison.OrdinalIgnoreCase) ? c.While : nested ? c.Then : c.If)} {r}";
        }
        // "When ~," (문장 안의 발동 시점)
        if ((m = Regex.Match(s, @"^When (?<ev>[^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            // v0.31.0 사건 안의 쉼표 목록 "When any of your Digimon get linked to a [Social], [Creation] or [Tool] trait card, ~"
            if (EventKo(m.Groups["ev"].Value) == null && CondKo(m.Groups["ev"].Value) == null
                && Regex.Match(s, SentenceParser.WhenWithListPattern, RegexOptions.IgnoreCase) is { Success: true } wl && wl.Groups["ev"].Value != m.Groups["ev"].Value)
                m = wl;
            string? ev = EventKo(m.Groups["ev"].Value) ?? (CondKo(m.Groups["ev"].Value)?.While);
            if (ev == null) return null;
            // v0.33.0 사건의 디지몬이 있는 사건 — 뒤 조건의 "it"은 그 디지몬 (SentenceParser.IfSubject33)
            _eventOther = Regex.IsMatch(m.Groups["ev"].Value, @"\b(?:played|digivolves?|attacks?)\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(m.Groups["ev"].Value, @"^this \w+ (?:attacks|digivolves)", RegexOptions.IgnoreCase);
            // "When an opponent's Digimon attacks, trash its bottom 2 ~" — 사건의 디지몬이 뒤의 "it" (공식 BT14-019: "그 디지몬의 진화원을")
            if (Regex.IsMatch(m.Groups["ev"].Value, @"^(?:an|one of your|any of your) opponent's [^,]*Digimon (?:attacks?|is played|are played|digivolves?)$", RegexOptions.IgnoreCase))
                _mentionedTarget = true;
            return Sentence(Cap(m.Groups["rest"].Value), nested: true) is { } r ? $"{ev} {r}" : null;
        }
        // "For each ~, ~" · "~ for each ~" — 셈
        if ((m = Regex.Match(s, @"^For (?:each|every) (?<cnt>[^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
            return CounterKo(m.Groups["cnt"].Value) is { } fc && Sentence(Cap(m.Groups["rest"].Value)) is { } fr ? $"{fc} {fr}" : null;

        // 시큐리티 찾기 (v0.28.0) — 공식 (EX5-027 · BT13-012 · EX3-029): "자신의 시큐리티를 전부 확인한다. 그중 명칭에 「레오몬」을 포함하는 카드 1장을 패에 추가할 수 있다"
        //  · "그중 1장을 오픈하고 패에 추가한다" · "그 후, 자신의 시큐리티를 셔플한다"
        if ((m = Regex.Match(s, @"^(?<may0>you may )?Search your security stack(?: for (?<p1>[^,]+?),? reveal it,? and add it to (?:the|your) hand|,? reveal (?<p2>.+?) (?:among|from) it,? and add it to (?:the|your) hand|,? (?:and )?(?<may>you may )?(?<verb>add|play) (?<p3>.+?) among (?:it|them)(?: to (?:the|your) hand)?(?<free> without paying (?:the|its) (?:memory )?costs?)?)$", RegexOptions.IgnoreCase)).Success)
        {
            bool optional = m.Groups["may0"].Success || m.Groups["may"].Success;
            string pickText = m.Groups["p1"].Success ? m.Groups["p1"].Value : m.Groups["p2"].Success ? m.Groups["p2"].Value : m.Groups["p3"].Value;
            string? pk = Regex.IsMatch(pickText.Trim(), @"^1 card$", RegexOptions.IgnoreCase) ? "" : PickKo(pickText);
            if (pk == null) return null;
            bool play = m.Groups["verb"].Value.Equals("play", StringComparison.OrdinalIgnoreCase);
            string what = pk.Length == 0 ? "1장을 오픈하고" : $"{pk}{J(pk, "을", "를")}";
            string verb = play ? (optional ? "코스트를 지불하지 않고 등장시킬 수 있다" : "코스트를 지불하지 않고 등장시킨다") : (optional ? "패에 추가할 수 있다" : "패에 추가한다");
            return $"자신의 시큐리티를 전부 확인한다. 그중 {what} {verb}";
        }
        // 보기만 하는 공개 — 공식 (BT18-068 · BT7-004): "자신/상대의 덱 위에서부터 5장 오픈한다. 오픈한 카드는 덱 위 또는 아래로만 되돌린다"
        if ((m = Regex.Match(s, @"^Reveal the top (?:(?<n>\d+) cards|card) of (?<who>your|your opponent's|your or your opponent's) deck,? and (?:return them|place it) (?:to|at) the top or bottom of (?:the|your) deck$", RegexOptions.IgnoreCase)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant() switch { "your" => "자신", "your opponent's" => "상대", _ => "자신/상대" };
            return m.Groups["n"].Success
                ? $"{who}의 덱 위에서부터 {m.Groups["n"].Value}장 오픈한다. 오픈한 카드는 덱 위 또는 아래로만 되돌린다"
                : $"{who}의 덱 위에서부터 1장 오픈하고, 그 카드를 덱의 위 또는 아래로 되돌린다";
        }
        // 고르는 진화원 파기 — 공식 (BT14-023 · BT14-020): "상대의 디지몬의 진화원을 선택하여 2장 파기한다" · "상대의 디지몬 1마리의 진화원을 선택하여 1장 파기한다"
        if ((m = Regex.Match(s, @"^Trash (?:any )?(?<n>\d+) digivolution cards? (?:from|of|under) (?<one>(?:1|one) of )?your opponent's Digimon$", RegexOptions.IgnoreCase)).Success)
            return $"상대의 디지몬{(m.Groups["one"].Success ? " 1마리" : "")}의 진화원을 선택하여 {m.Groups["n"].Value}장 파기한다";
        if (Regex.IsMatch(s, @"^shuffle your security stack$", RegexOptions.IgnoreCase)) return "자신의 시큐리티를 셔플한다";
        if (Regex.IsMatch(s, @"^your opponent shuffles their security stack$", RegexOptions.IgnoreCase)) return "상대는 자신의 시큐리티를 셔플한다";

        bool may = false;
        if ((m = Regex.Match(s, @"^you may\s+(?<rest>.+)$", RegexOptions.IgnoreCase)).Success) { may = true; s = Cap(m.Groups["rest"].Value); }

        // 비용: "suspend this Tamer to gain 1 memory" · "unsuspend this Digimon by decreasing your memory by 3" → "By ~ing, ~"
        if ((m = Regex.Match(s, @"^(?<verb>suspend|trash|delete|return|pay|place|add)\b(?<obj>[^,]*?) to (?<rest>(?:gain|draw|unsuspend|delete|suspend|give|reveal|return|play|trash|add|reduce|have|prevent|＜)\b?.+)$", RegexOptions.IgnoreCase)).Success)
            s = $"By {Gerund(m.Groups["verb"].Value)}{m.Groups["obj"].Value}, {m.Groups["rest"].Value}";
        else if ((m = Regex.Match(s, @"^(?<act>[^,]+?) by (?<cost>(?:suspending|trashing|deleting|returning|paying|placing|decreasing your memory by \d+)\b[^,]*)$", RegexOptions.IgnoreCase)).Success)
            s = $"By {m.Groups["cost"].Value}, {m.Groups["act"].Value}";
        if ((m = Regex.Match(s, @"^By (?<cost>[^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            // v0.30.0: 비용 안의 쉼표 목록 "By trashing 1 card with the [Demon], [Shaman] or [Titan] trait from your hand, ~" (SentenceParser.ParseStep과 같은 읽기)
            if (CostKo(m.Groups["cost"].Value) == null)
                for (int at = s.IndexOf(", ", m.Groups["cost"].Index + m.Groups["cost"].Length + 1, StringComparison.Ordinal); at > 0; at = s.IndexOf(", ", at + 2, StringComparison.Ordinal))
                {
                    var wider = Regex.Match(s, $@"^By (?<cost>.{{{at - 3}}}), (?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (wider.Success && Regex.Matches(wider.Groups["cost"].Value, @", ").All(x => Regex.IsMatch(wider.Groups["cost"].Value[(x.Index + 2)..], @"^\["))
                        && CostKo(wider.Groups["cost"].Value) != null) { m = wider; break; }
                }
            if (CostKo(m.Groups["cost"].Value) is not { } cost) return null;
            // 비용이 있는 "~할 수 있다"는 비용 쪽이 이미 선택 — 동작은 평서형 ("~하는 것으로, ~한다")
            return Sentence(Cap(m.Groups["rest"].Value)) is { } r ? $"{cost} {r}" : null;
        }

        var mult = Regex.Match(s, @"^(?<core>.+?) for (?:every|each) (?<cnt>.+?)(?<dur> for the turn| until the end of .+)?$", RegexOptions.IgnoreCase);
        if (mult.Success && CounterKo(mult.Groups["cnt"].Value) is { } mc && Action(Cap(mult.Groups["core"].Value) + mult.Groups["dur"].Value, may ? Form.May : Form.Plain) is { } ma)
            return $"{mc} {ma}";

        var direct = Action(s, may ? Form.May : Form.Plain) ?? (may ? null : SentenceKo33(s, false));   // v0.33.0
        // "1 of your Digimon may unsuspend" · "1 Digimon may gain ＜Security A. -1＞ ~" — 주어 뒤의 may (SentenceParser.ParseStep과 같은 읽기)
        if (direct == null && !may && Regex.Match(s, @"^(?<t>(?!you\b)[^,]+?) may (?<v>unsuspend|gain|get)\b(?<rest>[^,]*)$", RegexOptions.IgnoreCase) is { Success: true } sm)
            return Action($"{sm.Groups["t"].Value} {sm.Groups["v"].Value}s{sm.Groups["rest"].Value}", Form.May);
        return direct ?? (may ? SentenceKo33(s, true) : null) ?? SentenceKo34(s, may);   // v0.33.0 (KoreanEffectRenderer.Batch33.cs) · v0.34.0 (Batch34)
    }

    // =====================================================================
    // 동작
    // =====================================================================

    private enum Form { Plain, May, By, And }

    /// <summary>동사 활용 4가지: 평서 · "~할 수 있다" · 비용 "~하는 것으로" · 이어짐 "~하고"</summary>
    private sealed record Verb(string Plain, string May, string By, string And)
    {
        public string Get(Form f) => f switch { Form.May => May, Form.By => By, Form.And => And, _ => Plain };
    }

    private static Verb Ha(string stem = "") => new($"{stem}한다", $"{stem}할 수 있다", $"{stem}하는 것으로", $"{stem}하고");
    private static Verb Siki(string stem) => new($"{stem}시킨다", $"{stem}시킬 수 있다", $"{stem}시키는 것으로", $"{stem}시키고");
    private static readonly Verb Dollida = new("되돌린다", "되돌릴 수 있다", "되돌리는 것으로", "되돌리고");
    private static readonly Verb Eotda = new("얻는다", "얻을 수 있다", "얻는 것으로", "얻고");
    private static readonly Verb Juda = new("준다", "줄 수 있다", "주는 것으로", "주고");
    private static readonly Verb Notda = new("놓는다", "놓을 수 있다", "놓는 것으로", "놓고");
    private static readonly Verb Cannot = new("할 수 없다", "할 수 없다", "할 수 없는 것으로", "할 수 없고");
    private static readonly Verb Anhda = new("않는다", "않는다", "않는 것으로", "않고");
    /// <summary>키워드 동작 (《1 드로우》 · 〈리커버리 +1 《덱》〉) — 명사만으로 문장이 된다</summary>
    private static readonly Verb KeywordAct = new("", " 할 수 있다", " 하는 것으로", "하고");

    private sealed record Phrase(string Text, Verb Verb)
    {
        public string Render(Form f) => Text + Verb.Get(f);
    }

    /// <summary>동작 문장 → 한글 (기간 · 두 동작 포함)</summary>
    private static string? Action(string s, Form form)
    {
        s = s.Trim().TrimEnd('.');
        if (Act(s, form) is { } direct)
            return direct;
        // "A and B" — 두 동작
        foreach (Match and in Regex.Matches(s, @"\s+and\s+", RegexOptions.IgnoreCase))
        {
            string left = s[..and.Index].Trim(), right = s[(and.Index + and.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            if (Act(left, Form.And) is { } a && Act(Cap(right), form) is { } b)
                return $"{a}, {b}";
        }
        // v0.34.0 오른쪽이 제한 · 보호 문장 ("Trash ~ and this Digimon isn't affected by your opponent's effects until ~") — 원래 규칙이 읽지 못할 때만
        foreach (Match and in Regex.Matches(s, @"\s+and\s+", RegexOptions.IgnoreCase))
        {
            string left = s[..and.Index].Trim(), right = s[(and.Index + and.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            if (Act(left, Form.And) is { } a && RestrictionKo34(Cap(right), form == Form.May) is { } b)
                return $"{a}, {b}";
        }
        return null;
    }

    /// <summary>동작 하나 (앞뒤 기간 포함) → 활용된 한글</summary>
    private static string? Act(string s, Form form)
    {
        // 앞에 오는 기간 "Until your opponent's turn ends, 1 of your Digimon gains ~" → 뒤로 옮겨 같은 처리
        if (Regex.Match(s, @"^(?<d>until [^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase) is { Success: true } lead)
            s = $"{lead.Groups["rest"].Value} {lead.Groups["d"].Value.ToLowerInvariant()}";
        var (dur, core) = SplitDuration(s);
        var phrase = Core(core);
        if (phrase == null) return null;
        return (dur != null ? dur + ", " : "") + phrase.Render(form);
    }

    private static (string? Ko, string Core) SplitDuration(string s)
    {
        var pairs = new (string Pattern, string Ko)[]
        {
            (@"\s*for the turn$", "이 턴 동안"),
            (@"\s*for the attack$", "이 어택 동안"),
            (@"\s*until (?:their|your opponent's|the opponent's) turn ends$", "상대의 턴 종료 시까지"),
            (@"\s*until your turn ends$", "자신의 턴 종료 시까지"),
            (@"\s*until the end of (?:your opponent's|their|the opponent's) next turn$", "다음 상대의 턴 종료 시까지"),
            (@"\s*until (?:your opponent's|their) next end of turn$", "다음 상대의 턴 종료 시까지"),
            (@"\s*until the end of (?:your opponent's|their|the opponent's) turn$", "상대의 턴 종료 시까지"),
            (@"\s*until the end of your turn$", "자신의 턴 종료 시까지"),
            (@"\s*until the end of (?:the|this) turn$", "이 턴 동안"),
            (@"\s*until the end of your next turn$", "다음 자신의 턴 종료 시까지"),
        };
        foreach (var (p, ko) in pairs)
        {
            var m = Regex.Match(s, p, RegexOptions.IgnoreCase);
            if (m.Success) return (ko, s[..m.Index]);
        }
        var lead = Regex.Match(s, @"^(?<d>for the turn|until the end of (?:your opponent's|their) (?:next )?turn|until the end of your turn), (?<rest>.+)$", RegexOptions.IgnoreCase);
        if (lead.Success)
        {
            string d = lead.Groups["d"].Value.ToLowerInvariant();
            string ko = d == "for the turn" ? "이 턴 동안" : d.Contains("next") ? "다음 상대의 턴 종료 시까지" : d.Contains("opponent") || d.Contains("their") ? "상대의 턴 종료 시까지" : "자신의 턴 종료 시까지";
            return (ko, Cap(lead.Groups["rest"].Value));
        }
        return (null, s);
    }

    /// <summary>동작 핵심 → (목적어 구절, 동사)</summary>
    private static Phrase? Core(string s)
    {
        s = s.Trim().TrimEnd('.');
        Match m;
        // ---- 키워드 동작 ----
        if ((m = Regex.Match(s, @"^＜Draw (?<n>\d+)＞$", RegexOptions.IgnoreCase)).Success)
            return new(KeywordKo("Draw " + m.Groups["n"].Value, true)!, KeywordAct);
        if ((m = Regex.Match(s, @"^＜Draw (?<n>\d+)＞ and trash (?<t>\d+) cards? (?:in|from) your hand$", RegexOptions.IgnoreCase)).Success)
            return new($"{KeywordKo("Draw " + m.Groups["n"].Value, true)}하고, 자신의 패를 {m.Groups["t"].Value}장 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^＜Recovery \+(?<n>\d+)\s*(?:\(Deck\))?\s*＞$", RegexOptions.IgnoreCase)).Success)
            return new(KeywordKo("Recovery +" + m.Groups["n"].Value + " (Deck)", true)!, KeywordAct);
        if ((m = Regex.Match(s, @"^＜De-Digivolve (?<n>\d+)＞ (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
            return TargetKo(m.Groups["t"].Value) is { } dt ? new($"{dt}{J(dt, "을", "를")} {KeywordKo("De-Digivolve " + m.Groups["n"].Value, true)}", new Verb(" 한다", " 할 수 있다", " 하는 것으로", "하고")) : null;

        // ---- 메모리 · 덱 · 시큐리티 ----
        if ((m = Regex.Match(s, @"^(?<v>Gain|Lose) (?<n>\d+) memory$", RegexOptions.IgnoreCase)).Success)
            return new($"메모리를 {(m.Groups["v"].Value.Equals("Gain", StringComparison.OrdinalIgnoreCase) ? "+" : "-")}{m.Groups["n"].Value} ", Ha());
        if ((m = Regex.Match(s, @"^Set (?:it|your memory) to (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return new($"메모리를 {m.Groups["n"].Value}{J(m.Groups["n"].Value, "으로", "로")} ", Ha());
        if ((m = Regex.Match(s, @"^Trash (?:the top (?:card|(?<n>\d+) cards) of your opponent's security stack|your opponent's top (?:security card|(?<n>\d+) security cards)|their top (?:security card|(?<n>\d+) security cards)|the top (?:card|(?<n>\d+) cards) of their security stack)$", RegexOptions.IgnoreCase)).Success)
            return new($"상대의 시큐리티를 위에서부터 {N(m)}장 ", Ha("파기"));
        if (Regex.IsMatch(s, @"^Trash (?:the top card of your security stack|your top security card)$", RegexOptions.IgnoreCase))
            return new("자신의 시큐리티를 위에서부터 1장 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^Trash the top (?:card|(?<n>\d+) cards) of your deck$", RegexOptions.IgnoreCase)).Success)
            return new($"자신의 덱을 위에서부터 {N(m)}장 ", Ha("파기"));
        // 공식 (BT14-077 · BT14-080): "서로의 덱 위에서부터 2장 파기한다." · "상대의 덱 위에서부터 3장 파기한다."
        if ((m = Regex.Match(s, @"^Trash the top (?:card|(?<n>\d+) cards) of (?:(?<both>both players'|each player's) decks?|your opponent's deck)$", RegexOptions.IgnoreCase)).Success)
            return new($"{(m.Groups["both"].Success ? "서로의" : "상대의")} 덱 위에서부터 {N(m)}장 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^Trash (?<n>\d+) cards? (?:in|from) your hand$", RegexOptions.IgnoreCase)).Success)
            return new($"자신의 패를 {m.Groups["n"].Value}장 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^Your opponent trashes (?<n>\d+) cards? in their hand$", RegexOptions.IgnoreCase)).Success)
            return new($"상대는 자신의 패를 {m.Groups["n"].Value}장 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^Both players trash the top (?<n>\d+) cards? of their decks?$", RegexOptions.IgnoreCase)).Success)
            return new($"서로의 플레이어는 자신의 덱을 위에서부터 {m.Groups["n"].Value}장 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^Add your (?<pos>top|bottom) security card to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success)
            return new($"자신의 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "맨 위" : "맨 아래")}의 카드를 패에 ", Ha("추가"));
        if ((m = Regex.Match(s, @"^At (?:the )?end of (?:the |this )?turn, lose (?<n>\d+) memory$", RegexOptions.IgnoreCase)).Success)
            return new($"이 턴 종료 시, 메모리를 -{m.Groups["n"].Value} ", Ha());
        if (Regex.IsMatch(s, @"^Look at the top card of your deck$", RegexOptions.IgnoreCase))
            return new("자신의 덱 위의 카드 1장을 ", Ha("확인"));

        // 키워드만 있는 문장 ("[On Deletion] ＜Save＞" · "[When Digivolving] ＜Blitz＞")
        if (Regex.IsMatch(s, @"^(?:＜[^＞]+＞\s*)+$"))
        {
            var kws = Regex.Matches(s, @"＜([^＞]+)＞").Select(x => KeywordKo(x.Groups[1].Value, withReminder: true)).ToList();
            return kws.All(k => k != null) ? new(string.Join(" ", kws), KeywordAct) : null;
        }

        // ---- 이 카드 ([시큐리티]) ----
        if (Regex.IsMatch(s, @"^Play (?:this card|it) without paying (?:the|its) (?:memory )?cost$", RegexOptions.IgnoreCase))
            return new("이 카드를 코스트를 지불하지 않고 ", Siki("등장"));
        if (Regex.IsMatch(s, @"^Place (?:this card|it) in (?:the|your) battle area$", RegexOptions.IgnoreCase))
            return new("이 카드를 배틀 에어리어에 ", Notda);
        // v0.30.0 배틀 에어리어의 옵션 카드 — 공식 (BT19-062 · BT23-047 · BT19-064 · P-155): "배틀 에어리어의 자신의 옵션 카드 1장을 파기한다" ·
        // "배틀 에어리어의 상대의 옵션 카드 1장을 파기한다" · "배틀 에어리어의 옵션 카드 1장을 파기하는" · "배틀 에어리어의 레드 이외의 자신의 옵션 카드 1장을 파기하는"
        if ((m = Regex.Match(s, $@"^Trash (?<n>\d+) (?:of (?<who>your opponent's|their|your) )?(?<non>non-)?(?:(?<color>{Colors}) )?option cards? in the battle area$", RegexOptions.IgnoreCase)).Success)
        {
            string who = !m.Groups["who"].Success ? "" : m.Groups["who"].Value.Equals("your", StringComparison.OrdinalIgnoreCase) ? "자신의 " : "상대의 ";
            string col = m.Groups["color"].Success ? $"{ColorKo(m.Groups["color"].Value)}{(m.Groups["non"].Success ? " 이외의" : "인")} " : "";
            return new($"배틀 에어리어의 {col}{who}옵션 카드 {m.Groups["n"].Value}장을 ", Ha("파기"));
        }
        // 공식 (BT19-086 · EX8-052 · BT17-100 · EX3-025): "자신의 패/트래시에서 특징으로 「디바이스」를 가진 옵션 카드 1장을 배틀 에어리어에 놓는다" · "이 디지몬의 진화원에서 「종말의 시계」 1장을 배틀 에어리어에 놓는다"
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from (?<from>your hand or (?:your )?trash|your hand|your trash|this Digimon's digivolution cards) in (?:the|your) battle area$", RegexOptions.IgnoreCase)).Success
            && PickKo(Regex.Replace(m.Groups["pick"].Value, @"\bwith a use cost of\b", "with a cost of", RegexOptions.IgnoreCase)) is { } opk)
        {
            string fz = m.Groups["from"].Value.ToLowerInvariant();
            string from = fz.Contains("digivolution") ? "이 디지몬의 진화원에서" : fz.Contains("hand") && fz.Contains("trash") ? "자신의 패/트래시에서" : fz.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
            return new($"{from} {opk}{J(opk, "을", "를")} 배틀 에어리어에 ", Notda);
        }
        // v0.30.0 — 공식 (EX10-072): "자신의 시큐리티에서, 특징 「어둠의 4천왕」을 가진 앞면의 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다"
        if ((m = Regex.Match(s, @"^Play (?<n>\d+) (?<fu>face[- ]up )?(?<rest>.+?) from your security stack without paying (?:the|its) (?:memory |play )?cost$", RegexOptions.IgnoreCase)).Success
            && PickKo($"{m.Groups["n"].Value} {m.Groups["rest"].Value}") is { } secPick)
        {
            string pk = m.Groups["fu"].Success ? Regex.Replace(secPick, @"(디지몬 카드|카드)(?= \d+장)", "앞면의 $1") : secPick;
            return new($"자신의 시큐리티에서 {pk}{J(pk, "을", "를")} 코스트를 지불하지 않고 ", Siki("등장"));
        }
        // v0.30.0 이 효과로 등장한 디지몬 — 공식 (BT23-017 · EX10-072): "이 효과로 등장한 디지몬은 진화할 수 없으며, 상대의 턴 종료 시, 소멸한다" ·
        // "턴 종료 시, 이 효과로 등장한 디지몬을 소멸시킨다" · "자신의 턴 종료 시, 이 효과로 등장한 디지몬을 소멸시킨다"
        if ((m = Regex.Match(s, @"^(?:the Digimon this effect played|(?:the )?Digimon played by this effect) (?<nd>can't digivolve and )?(?:is|are) deleted at (?<when>turn end|the end of (?:the|this) turn|the end of your opponent's turn|the end of their turn|the end of your turn)$", RegexOptions.IgnoreCase)).Success)
            return new($"이 효과로 등장한 디지몬은 {(m.Groups["nd"].Success ? "진화할 수 없으며, " : "")}{EndWhenKo(m.Groups["when"].Value)}, 소멸한다", new Verb("", "", "", ""));
        if ((m = Regex.Match(s, @"^At (?<when>turn end|the end of (?:the|this) turn|the end of your opponent's turn|the end of their turn|the end of your turn), delete (?:the Digimon this effect played|(?:the )?Digimon played by this effect)$", RegexOptions.IgnoreCase)).Success)
            return new($"{EndWhenKo(m.Groups["when"].Value)}, 이 효과로 등장한 디지몬을 소멸시킨다", new Verb("", "", "", ""));
        // v0.29.0 — 공식 (BT13-106 · BT15-092): "이 카드의 【메인】 효과를 발휘한다" · "이 카드의 【시큐리티】 효과를 발휘한다"
        if (Regex.IsMatch(s, @"^Activate (?:this card'?s|its) \[Main\] effects?$", RegexOptions.IgnoreCase))
            return new("이 카드의 【메인】 효과를 ", Ha("발휘"));
        if (Regex.IsMatch(s, @"^Activate (?:this card'?s|its) \[Security\] effects?$", RegexOptions.IgnoreCase))
            return new("이 카드의 【시큐리티】 효과를 ", Ha("발휘"));
        // 듀얼 카드 (종합 룰 4-6 "옵션 쪽"): "Activate 1 [Main] effect on this card's Option side"
        if (Regex.IsMatch(s, @"^Activate (?:1|one) \[Main\] effect on this card's Option side$", RegexOptions.IgnoreCase))
            return new("이 카드의 옵션 쪽의 【메인】 효과를 1개 ", Ha("발휘"));
        if (Regex.IsMatch(s, @"^Return it to (?:the|your) hand$", RegexOptions.IgnoreCase))
            return new("이 카드를 패로 ", Dollida);
        if (Regex.IsMatch(s, @"^(?:you (?:can|may) )?ignore this card's color requirements$", RegexOptions.IgnoreCase))
            return new("이 카드의 색 조건을 ", new Verb("무시할 수 있다", "무시할 수 있다", "무시하는 것으로", "무시하고"));
        if (Regex.IsMatch(s, @"^Add this card to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase))
            return new("이 카드를 패에 ", Ha("추가"));
        if (Regex.IsMatch(s, @"^(?:Return|Add) this card (?:from (?:the|your) trash )?to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase))
            return new("이 카드를 패로 ", Dollida);
        if (Regex.IsMatch(s, @"^Activate this card'?s \[Main\] effects?$", RegexOptions.IgnoreCase))
            return new("이 카드의 【메인】 효과를 ", Ha("발휘"));
        if (Regex.IsMatch(s, @"^At the end of the battle, play this card without paying (?:the|its) (?:memory )?cost$", RegexOptions.IgnoreCase))
            return new("배틀 종료 시, 이 카드를 코스트를 지불하지 않고 ", Siki("등장"));
        if (Regex.IsMatch(s, @"^Place this card (?:on|at the) top of your security stack(?: face down)?$", RegexOptions.IgnoreCase))
            return new("이 카드를 자신의 시큐리티 위에 뒷면으로 ", Notda);

        // ---- 덱 위 공개 ----
        if ((m = Regex.Match(s, @"^Reveal (?:the top (?<n>\d+) cards? of your deck|(?<n>\d+) cards? from the top of your deck)(?: for each (?<counter>[^.]+?))?\.? (?:Add (?<adds>.+?) among them to (?:the|your) hand|(?:You may )?play (?<play>.+?) among them(?: without paying (?:its|the|their) (?:memory )?costs?)?(?: ⟦no-onplay⟧)?)\.? (?<rest>.+?)$", RegexOptions.IgnoreCase)).Success)
        {
            string n = m.Groups["n"].Value;
            string head = $"자신의 덱 위에서부터 {n}장 오픈한다. ";
            if (m.Groups["counter"].Success) { if (CounterKo(m.Groups["counter"].Value) is not { } cnt) return null; head = $"{cnt} 자신의 덱 위에서부터 {n}장 오픈한다. "; }
            bool play = m.Groups["play"].Success;
            var parts = Regex.Split(m.Groups[play ? "play" : "adds"].Value, @"\s+and\s+(?=(?:up to )?\d+ )", RegexOptions.IgnoreCase);
            // "1 blue and 1 green Digimon card" — 앞 조건에 뒤의 종류를 붙인다
            string kindTail = Regex.Match(parts[^1], @"(?:Digimon|Tamer|Option) cards?$", RegexOptions.IgnoreCase).Value;
            var picks = parts.Select(p => kindTail.Length > 0 && !Regex.IsMatch(p, @"\b(?:cards?|Digimon|Tamers?|Options?)\b|\[", RegexOptions.IgnoreCase) ? $"{p} {kindTail}" : p).Select(PickKo).ToList();
            if (picks.Any(p => p == null)) return null;
            string picked = picks.Count == 1 ? picks[0]! : string.Join(" ", picks.Take(picks.Count - 1).Select(p => p + J(p!, "과", "와"))) + " " + picks[^1];
            string rest = m.Groups["rest"].Value.TrimEnd('.');
            string restKo = Regex.IsMatch(rest, @"^Trash the (?:rest|remaining cards)$", RegexOptions.IgnoreCase) ? "나머지는 파기한다"
                : Regex.IsMatch(rest, @"top of (?:the |your )?deck", RegexOptions.IgnoreCase) && !Regex.IsMatch(rest, @"bottom", RegexOptions.IgnoreCase) ? "나머지는 원하는 순서대로 덱 위로 되돌린다"
                : "나머지는 원하는 순서대로 덱 아래로 되돌린다";
            string mid = play ? $"그중 {picked}{J(picked, "을", "를")} 코스트를 지불하지 않고 등장시킬 수 있다. " : $"그중 {picked}{J(picked, "을", "를")} 패에 추가한다. ";
            return new(head + mid + restKo, new Verb("", "", "", ""));
        }
        if ((m = Regex.Match(s, @"^(?:Then, )?Reveal the top card of your deck\.? (?:Add it to (?:the|your) hand if it's (?<pick>an? [^.]+?)|If (?:that card|it) is (?<pick>an? [^,]+?), add it to (?:the|your) hand)\.? Otherwise,? (?:place|put|return) it (?:at|on|to) the bottom of (?:the|your) deck$", RegexOptions.IgnoreCase)).Success
            && PickKo(Regex.Replace(m.Groups["pick"].Value, @"^an? ", "1 ")) is { } top)
            return new($"자신의 덱 위에서부터 1장 오픈한다. 그 카드가 {Regex.Replace(top, @" 1장$", "")}{J(top, "이면", "면")} 패에 추가하고, 그렇지 않으면 덱 아래로 되돌린다", new Verb("", "", "", ""));

        // ---- 카드 고르기 (트래시 · 패 · 진화원) ----
        if ((m = Regex.Match(s, @"^(?:Return|Add) (?<pick>.+?) from your trash to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success)
            return PickKo(m.Groups["pick"].Value) is { } rp ? new($"자신의 트래시에서 {rp}{J(rp, "을", "를")} 패로 ", Dollida) : null;
        if ((m = Regex.Match(s, @"^Play (?<n>\d+) (?<adj>.*?)digivolution cards? (?:under this (?:card|Digimon)|of this Digimon) as (?:another|other) Digimon (?:without paying .+)$", RegexOptions.IgnoreCase)).Success
            && PickKo($"{m.Groups["n"].Value} {m.Groups["adj"].Value}Digimon card") is { } srcPick)
            return new($"이 디지몬의 진화원인 {srcPick}{J(srcPick, "을", "를")} 코스트를 지불하지 않고 다른 디지몬으로서 ", Siki("등장"));
        if ((m = Regex.Match(s, @"^Play (?<pick>.+?)(?<sus> suspended)? from (?<from>your hand or (?:from your )?trash|your hand or from your trash|your trash or hand|your hand|your trash|this Digimon's digivolution cards)(?<sus> suspended)? without paying (?:the|its) (?:memory |play )?cost(?<noplay> ⟦no-onplay⟧)?$", RegexOptions.IgnoreCase)).Success)
        {
            if (PickKo(m.Groups["pick"].Value) is not { } pp) return null;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string fromKo = from.Contains("digivolution") ? "이 디지몬의 진화원에서" : from.Contains("hand") && from.Contains("trash") ? "자신의 패 또는 트래시에서" : from.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
            var play = Siki("등장");
            // "Any [On Play] effects on Digimon played by this effect don't activate" — 빼먹으면 효과가 달라진다
            if (m.Groups["noplay"].Success)
            {
                const string noOnPlay = ". 이 효과로 등장한 카드의 【등장 시】 효과는 발휘하지 않는다";
                play = new(play.Plain + noOnPlay, play.May + noOnPlay, play.By, play.And);
            }
            return new($"{fromKo} {pp}{J(pp, "을", "를")} 코스트를 지불하지 않고 {(m.Groups["sus"].Success ? "레스트 상태로 " : "")}", play);
        }
        // ---- 자주 보이는 짧은 동작 (화면 점검 2026-10-22) ----
        if (Regex.IsMatch(s, @"^＜Delay＞$", RegexOptions.IgnoreCase))
            return new(KeywordKo("Delay", true)!, KeywordAct);
        if ((m = Regex.Match(s, @"^Both players ＜Draw (?<n>\d+)＞$", RegexOptions.IgnoreCase)).Success)
            return new($"서로 {KeywordKo("Draw " + m.Groups["n"].Value, true)}", KeywordAct);
        if (Regex.IsMatch(s, @"^(?:this Digimon's|its) attack target can't (?:change|be switched|be changed)$", RegexOptions.IgnoreCase))
            return new("이 디지몬의 어택의 대상은 변경되지 ", Anhda);
        if ((m = Regex.Match(s, @"^(?:change|switch) the attack target to (?<t>1 of your .+|this Digimon)$", RegexOptions.IgnoreCase)).Success
            && (TargetKo(m.Groups["t"].Value)
                ?? (Regex.Match(m.Groups["t"].Value, $@"^1 of your (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase) is { Success: true } nm ? $"자신의 {NamesKo(nm.Groups["x"].Value)} 1마리" : null)) is { } chg)
            return new($"어택의 대상을 {chg}{J(chg, "으로", "로")} ", Ha("변경"));
        if ((m = Regex.Match(s, @"^(?<t>this Digimon|it|that Digimon) (?<may>may )?unsuspends?$", RegexOptions.IgnoreCase)).Success)
        {
            // "it" — 앞에 대상이 없으면 이 디지몬 ("When this Digimon gets linked, it may unsuspend" — 공식 BT23-022 계열)
            string t = m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬"
                : m.Groups["t"].Value.Equals("it", StringComparison.OrdinalIgnoreCase) ? TargetKo("it")! : "그 디지몬";
            return new($"{t}{J(t, "을", "를")} 액티브로 ", m.Groups["may"].Success ? new Verb("할 수 있다", "할 수 있다", "하는 것으로", "하고") : Ha());
        }
        // 효과로 하는 어택: "1 of your Digimon may attack" · "this Digimon may attack" · "it may attack"
        if ((m = Regex.Match(s, @"^(?<t>\d+ of your Digimon(?: with [^,]+?)?|this Digimon|it|that Digimon|the DNA digivolved Digimon|that DNA digivolved Digimon)(?: with (?<n>\d+) or more digivolution cards)? (?<may>may )?attack(?<who> a player| your opponent| an opponent's Digimon| your opponent's Digimon)?(?<nosus> without suspending)?$", RegexOptions.IgnoreCase)).Success)
        {
            string atkWho = m.Groups["t"].Value;
            string? t = atkWho.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : Regex.IsMatch(atkWho, @"^(?:it|that|the)", RegexOptions.IgnoreCase) ? "그 디지몬" : TargetKo(atkWho);
            if (t == null) return null;
            if (m.Groups["n"].Success) t = $"진화원이 {m.Groups["n"].Value}장 이상인 {t}";   // "this Digimon with 5 or more digivolution cards"
            string whom = m.Groups["who"].Value.Contains("player", StringComparison.OrdinalIgnoreCase) || m.Groups["who"].Value.Trim().Equals("your opponent", StringComparison.OrdinalIgnoreCase) ? "플레이어에게 "
                : m.Groups["who"].Success ? "상대의 디지몬에게 " : "";
            string nosus = m.Groups["nosus"].Success ? "레스트시키지 않고 " : "";
            var atk = new Verb("어택한다", "어택할 수 있다", "어택하는 것으로", "어택하고");
            return new($"{t}{(J(t, "으로", "로"))} {nosus}{whom}", m.Groups["may"].Success ? new Verb("어택할 수 있다", "어택할 수 있다", "어택하는 것으로", "어택하고") : atk);
        }
        // "Your opponent's effects don't affect this Digimon" · "their Digimon's effects don't affect 1 of your Digimon"
        if ((m = Regex.Match(s, @"^(?:your opponent's|their|the opponent's) (?<dig>Digimon(?:'s)? )?effects don't affect (?<t>this Digimon|it|1 of your Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success)
        {
            string t = m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : m.Groups["t"].Value.Equals("it", StringComparison.OrdinalIgnoreCase) ? "그 디지몬" : TargetKo(m.Groups["t"].Value) ?? "";
            if (t.Length == 0) return null;
            return new($"{t}{J(t, "은", "는")} 상대의 {(m.Groups["dig"].Success ? "디지몬의 " : "")}효과를 받지 ", Anhda);
        }
        if (Regex.IsMatch(s, @"^play this card without battling and without paying (?:the|its) (?:memory )?cost$", RegexOptions.IgnoreCase))
            return new("배틀하지 않고, 이 카드를 코스트를 지불하지 않고 ", Siki("등장"));
        // ---- 7단계 (벗어나지 않음 · 레스트 / 액티브 불가 · 주인 없는 대상 · DNA 진화 · 링크 · 앞면 시큐리티) ----
        if ((m = Regex.Match(s, @"^(?:prevent (?:it|that Digimon|this Digimon|them|those Digimon) from (?:leaving(?: the battle area)?|being deleted)|(?<one>1 of those Digimon|it|they|that Digimon|this Digimon) (?:doesn't|don't|does not|do not) leave(?: the battle area)?|prevent (?:that|its|this|the) deletion|(?:it|this Digimon|that Digimon) (?:isn't|is not) deleted)$", RegexOptions.IgnoreCase)).Success)
        {
            bool deletion = Regex.IsMatch(s, @"delet", RegexOptions.IgnoreCase);
            string who = m.Groups["one"].Value.StartsWith("1 of", StringComparison.OrdinalIgnoreCase) ? "그 디지몬 1마리는"
                : Regex.IsMatch(s, @"^prevent this Digimon\b", RegexOptions.IgnoreCase) ? "이 디지몬은" : "그 디지몬은";   // v0.30.0 공식 (EX1-073): "이 디지몬은 소멸하지 않는다"
            return new($"{who} {(deletion ? "소멸하지" : "배틀 에어리어를 벗어나지")} ", Anhda);
        }
        if ((m = Regex.Match(s, @"^(?<t>.+?) can't (?<v>suspend|unsuspend)(?<next> (?:in|during) (?:their|your opponent's|its owner's) next unsuspend phase)?$", RegexOptions.IgnoreCase)).Success
            && TargetKo(Regex.Replace(m.Groups["t"].Value, @"\btheir\b", "your opponent's", RegexOptions.IgnoreCase), bare: false) is { } lockT)
        {
            bool sus = m.Groups["v"].Value.Equals("suspend", StringComparison.OrdinalIgnoreCase);
            if (!sus && m.Groups["next"].Success)
                return new($"{lockT}{J(lockT, "은", "는")} 다음 상대의 액티브 페이즈에 액티브가 되지 ", Anhda);
            var v = sus ? new Verb("레스트할 수 없다", "레스트할 수 없다", "레스트할 수 없는 것으로", "레스트할 수 없고") : new Verb("액티브가 되지 않는다", "액티브가 되지 않는다", "액티브가 되지 않는 것으로", "액티브가 되지 않고");
            return new($"{lockT}{J(lockT, "은", "는")} ", v);
        }
        // 주인을 말하지 않은 대상: "Suspend 1 Digimon" · "Delete up to 2 level 4 or lower Digimon" (양쪽 디지몬 중에서)
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Delete|Unsuspend) (?<n>(?:up to )?\d+) (?<adj>(?:[^,]*? )?)Digimon$", RegexOptions.IgnoreCase)).Success
            && !Regex.IsMatch(m.Groups["adj"].Value, @"\byour\b|opponent", RegexOptions.IgnoreCase)
            && TargetKo($"{m.Groups["n"].Value} of your opponent's {m.Groups["adj"].Value}Digimon") is { } anyT)
        {
            string t = anyT.Replace("상대의 ", "");
            string v = m.Groups["v"].Value.ToLowerInvariant();
            return v == "unsuspend" ? new($"{t}{J(t, "을", "를")} 액티브로 ", Ha()) : new($"{t}{J(t, "을", "를")} ", Siki(v == "delete" ? "소멸" : "레스트"));
        }
        // DNA 진화: "2 of your Digimon may DNA digivolve into [Silphymon] in the hand" · "This Digimon and any of your other Digimon may DNA digivolve into ~"
        if ((m = Regex.Match(s, @"^(?<who>2 of your Digimon|this Digimon and (?:any of )?(?:your other|another of your) (?<spec>[^,]*?)Digimon(?<with> with [^,]+?)?) (?<may>may )?DNA digivolve into (?<pick>.+?) in (?:the|your) hand(?<free> without paying (?:the|its) cost)?$", RegexOptions.IgnoreCase)).Success
            && (PickKo("1 " + m.Groups["pick"].Value) ?? PickKo(m.Groups["pick"].Value)) is { } dnaPick)
        {
            // 공식 표기: "이 디지몬과 다른 자신의 디지몬으로, 패의 디지몬 카드로 조그레스 진화할 수 있다"
            string? who = m.Groups["who"].Value.StartsWith("2", StringComparison.Ordinal) ? "자신의 디지몬 2마리로"
                : TargetKo("1 of your other " + m.Groups["spec"].Value + "Digimon" + m.Groups["with"].Value) is { } partner ? $"이 디지몬과 {Regex.Replace(partner, @"^자신의 다른 ", "다른 자신의 ").Replace(" 1마리", "")}으로," : null;
            if (who == null) return null;
            string pick = Regex.Replace(dnaPick, @" 1장$", "");
            string free = m.Groups["free"].Success ? "코스트를 지불하지 않고 " : "";
            var dna = m.Groups["may"].Success ? new Verb("조그레스 진화할 수 있다", "조그레스 진화할 수 있다", "조그레스 진화하는 것으로", "조그레스 진화하고") : new Verb("조그레스 진화한다", "조그레스 진화할 수 있다", "조그레스 진화하는 것으로", "조그레스 진화하고");
            return new($"{who} 패의 {pick}{J(pick, "으로", "로")} {free}", dna);
        }
        // 링크: "link 1 level 4 or lower Digimon card from your hand to this Digimon without paying the cost" · "Link this card to 1 of your Digimon ~"
        var linkVerb = new Verb("링크한다", "링크할 수 있다", "링크하는 것으로", "링크하고");
        if ((m = Regex.Match(s, @"^Link (?<pick>.+?) from (?<from>your hand(?: or (?:your )?trash| or this Digimon's digivolution cards| or its digivolution cards)?|your trash|this Digimon's digivolution cards|its digivolution cards) to (?<t>this Digimon|it|1 of your Digimon(?: with [^,]+?)?)(?: on the field)?(?<how> without paying (?:the|its) cost| with the (?:link )?cost reduced by (?<red>\d+))?$", RegexOptions.IgnoreCase)).Success
            && (PickKo(m.Groups["pick"].Value) ?? PickKo("1 " + m.Groups["pick"].Value)) is { } lp && (m.Groups["t"].Value.Equals("it", StringComparison.OrdinalIgnoreCase) ? "그 디지몬" : TargetKo(m.Groups["t"].Value)) is { } lt)
        {
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string fromKo = from.Contains("hand") && from.Contains("digivolution") ? "자신의 패 또는 이 디지몬의 진화원에서" : from.Contains("hand") && from.Contains("trash") ? "자신의 패 또는 트래시에서"
                : from.Contains("hand") ? "자신의 패에서" : from.Contains("trash") ? "자신의 트래시에서" : "이 디지몬의 진화원에서";
            string how = m.Groups["red"].Success ? $"링크 코스트를 -{m.Groups["red"].Value} 하고 " : m.Groups["how"].Success ? "코스트를 지불하지 않고 " : "";
            return new($"{fromKo} {lp}{J(lp, "을", "를")} {lt}에게 {how}", linkVerb);
        }
        if ((m = Regex.Match(s, @"^Link this card to (?<t>1 of your Digimon(?: with [^,]+?)?|this Digimon)(?: on the field)?(?<how> without paying (?:the|its) cost)?$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } ltc)
            return new($"이 카드를 {ltc}에게 {(m.Groups["how"].Success ? "코스트를 지불하지 않고 " : "")}", linkVerb);
        // 앞면 시큐리티
        if ((m = Regex.Match(s, @"^Place this (?:card|Digimon) face[- ]up as (?:the|your) (?<pos>top|bottom) security card$", RegexOptions.IgnoreCase)).Success)
            return new($"이 카드를 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}에 앞면으로 ", Notda);
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from (?<from>your hand or (?:from )?(?:your )?trash|your trash|your hand) face[- ]up as (?:the|your) (?<pos>top or bottom|top|bottom) security cards?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } fp)
        {
            string from = m.Groups["from"].Value.ToLowerInvariant();
            string pos = m.Groups["pos"].Value.ToLowerInvariant() switch { "top" => "위", "bottom" => "아래", _ => "위 또는 아래" };
            return new($"{(from.Contains("hand") && from.Contains("trash") ? "자신의 패 또는 트래시에서" : from.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서")} {fp}{J(fp, "을", "를")} 시큐리티 {pos}에 앞면으로 ", Notda);
        }
        if (Regex.IsMatch(s, @"^Flip your opponent's top face[- ]down security card face[- ]up$", RegexOptions.IgnoreCase))
            return new("상대의 뒷면의 시큐리티를 위에서부터 1장 앞면으로 ", Ha());
        if (Regex.IsMatch(s, @"^Add your top face[- ]down security card to (?:the|your) hand$", RegexOptions.IgnoreCase))
            return new("자신의 뒷면의 시큐리티를 위에서부터 1장 패에 ", Ha("추가"));
        // 토큰 정보가 적힌 것 (v0.28.0) — 공식 (BT22-040 · BT14-018 · BT17-100):
        //   "「사역마」(디지몬・옐로・DP 3000・【소멸 시】~) 토큰 1마리를 등장시킬 수 있다." · "「A」(~)과 「B」(~) 토큰 1마리씩을 등장시킨다." · "~토큰 1마리를 코스트를 지불하지 않고 등장시킨다."
        if ((m = Regex.Match(s, @"^Play (?<list>(?:\d+|a|an) \[[^\]]+\] Tokens?⦃[^⦄]*⦄(?:(?:,|,? and) (?:\d+|a|an) \[[^\]]+\] Tokens?⦃[^⦄]*⦄)*)(?<free> without paying (?:the|its) (?:memory )?costs?)?$", RegexOptions.IgnoreCase)).Success)
        {
            var toks = Regex.Matches(m.Groups["list"].Value, @"(?<n>\d+|a|an) \[(?<name>[^\]]+)\] Tokens?⦃(?<spec>[^⦄]*)⦄", RegexOptions.IgnoreCase)
                .Select(t => (N: int.TryParse(t.Groups["n"].Value, out int tn) ? tn : 1, Ko: TokenKo(t.Groups["name"].Value, t.Groups["spec"].Value))).ToList();
            if (toks.Any(t => t.Ko == null)) return null;
            string head = toks.Count == 1 ? toks[0].Ko! : string.Join(" ", toks.Take(toks.Count - 1).Select(t => t.Ko + J(Regex.Replace(t.Ko!, @"[)》】」.\s]+$", ""), "과", "와"))) + " " + toks[^1].Ko;
            string count = toks.Count == 1 ? $"{toks[0].N}마리를" : toks.All(t => t.N == 1) ? "1마리씩을" : string.Join("·", toks.Select(t => $"{t.N}마리")) + "를";
            return new($"{head} 토큰 {count} {(m.Groups["free"].Success ? "코스트를 지불하지 않고 " : "")}", Siki("등장"));
        }
        if ((m = Regex.Match(s, @"^Play (?<n>\d+|a|an) \[(?<name>[^\]]+)\] Tokens? without paying (?:the|its) (?:memory )?costs?$", RegexOptions.IgnoreCase)).Success)
            return new($"「{NameKo(m.Groups["name"].Value)}」 토큰을 {(int.TryParse(m.Groups["n"].Value, out int tk) ? tk : 1)}장 코스트를 지불하지 않고 ", Siki("등장"));
        // "Place ~ from your hand or trash as this Digimon's bottom digivolution card" (v0.28.0) — 공식 (BT21-069 · EX7-011): "자신의 패/트래시에서 ~를 이 디지몬의 진화원 아래에 놓는"
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from (?<from>your hand or (?:your )?trash|your trash or (?:your )?hand|your trash|your hand) (?:under (?<t>.+?) (?:at the bottom of its digivolution cards|as its bottom digivolution cards?)|as (?<t>this Digimon|(?:1|one|any) of your [^']+?)'s bottom digivolution cards?)$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } pu && TargetKo(m.Groups["t"].Value) is { } ptu)
        {
            string fz = m.Groups["from"].Value.ToLowerInvariant();
            string from = fz.Contains("hand") && fz.Contains("trash") ? "자신의 패/트래시에서" : fz.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서";
            return new($"{from} {pu}{J(pu, "을", "를")} {ptu}의 진화원 아래에 ", Notda);
        }
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from your trash (?:on|at the) top of your security stack(?: face down)?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value + (Regex.IsMatch(m.Groups["pick"].Value, @"\bcards?$") ? "" : " card")) is { } ps)
            return new($"자신의 트래시에서 {ps}{J(ps, "을", "를")} 시큐리티 위에 뒷면으로 ", Notda);

        // ---- 진화원 ----
        if ((m = Regex.Match(s, @"^Trash (?:the (?<pos>top|bottom) (?:(?<n>\d+) )?digivolution cards? (?:of|from)|(?:up to )?(?<n>\d+) digivolution cards? (?:at|from) the (?<pos>bottom|top) of|(?:up to )?(?<n>\d+) (?<pos>bottom|top) digivolution cards? of) (?<t>.+)$", RegexOptions.IgnoreCase)).Success
            && TargetKo(m.Groups["t"].Value) is { } tt)
        {
            string pos = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래";
            return new($"{tt}의 진화원을 {pos}에서부터 {N(m)}장 ", Ha("파기"));
        }
        if ((m = Regex.Match(s, @"^Trash all (?:of the )?digivolution cards (?:under|of) (?<t>.+)$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } ta)
            return new($"{ta}의 진화원을 전부 ", Ha("파기"));
        if ((m = Regex.Match(s, @"^Choose (?<n>\d+) digivolution cards? of (?<t>.+?) and trash (?:it|them)$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } tc)
            return new($"{tc}의 진화원을 {m.Groups["n"].Value}장 골라 ", Ha("파기"));

        // ---- 대상을 가진 동작 ----
        if ((m = Regex.Match(s, @"^Delete (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
            return TargetKo(m.Groups["t"].Value) is { } d ? new($"{d}{J(d, "을", "를")} ", Siki("소멸")) : null;
        // 공식 (BT12-106 · BT15-054): "상대의 디지몬과 테이머 전부를 레스트시킨다." · "상대의 디지몬과 테이머 1마리(명)씩을 레스트시킨다."
        if (Regex.IsMatch(s, @"^Suspend all of your opponent's Digimon and Tamers$", RegexOptions.IgnoreCase))
            return new("상대의 디지몬과 테이머 전부를 ", Siki("레스트"));
        if (Regex.IsMatch(s, @"^Suspend 1 of your opponent's Digimon and 1 of (?:their|your opponent's) Tamers$", RegexOptions.IgnoreCase))
            return new("상대의 디지몬과 테이머 1마리(명)씩을 ", Siki("레스트"));
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Unsuspend) (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            if (TargetKo(m.Groups["t"].Value) is not { } t) return null;
            return m.Groups["v"].Value.Equals("Suspend", StringComparison.OrdinalIgnoreCase) ? new($"{t}{J(t, "을", "를")} ", Siki("레스트")) : new($"{t}{J(t, "을", "를")} 액티브로 ", Ha());
        }
        if ((m = Regex.Match(s, @"^(?<t>.+?) unsuspends?$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } un)
            return new($"{un}{J(un, "을", "를")} 액티브로 ", Ha());
        if ((m = Regex.Match(s, @"^Return (?<t>.+?) to (?:the|its owner's|their owners'?|their|the owner's|your) hands?$", RegexOptions.IgnoreCase)).Success)
            return TargetKo(m.Groups["t"].Value) is { } r ? new($"{r}{J(r, "을", "를")} 패로 ", Dollida) : null;
        if ((m = Regex.Match(s, @"^(?:Place|Return) (?<t>.+?) (?:at|on|to) the bottom of (?:its owner's|their owners'?|their|the owner's|the|your opponent's) decks?(?: in any order)?$", RegexOptions.IgnoreCase)).Success)
            return TargetKo(m.Groups["t"].Value) is { } b ? new($"{b}{J(b, "을", "를")} 덱 아래로 ", Dollida) : null;
        if ((m = Regex.Match(s, @"^Place (?<t>(?:\d+|all) of (?:your|your opponent's) .+?) (?:on|at the) top of (?:your|its owner's|their owners'?) security stacks?(?: face down)?$", RegexOptions.IgnoreCase)).Success)
            return TargetKo(m.Groups["t"].Value) is { } st ? new($"{st}{J(st, "을", "를")} 시큐리티 위에 뒷면으로 ", Notda) : null;
        if ((m = Regex.Match(s, @"^Change the original DP of (?<t>.+?) to (?<n>\d+)$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } od)
            return new($"{od}의 원래 DP를 {m.Groups["n"].Value}{J(m.Groups["n"].Value, "으로", "로")} ", Ha());
        if ((m = Regex.Match(s, @"^(?<v>Delete|Suspend) (?<n>\d+|all) of your opponent's Tamers$", RegexOptions.IgnoreCase)).Success)
        {
            string who = m.Groups["n"].Value.Equals("all", StringComparison.OrdinalIgnoreCase) ? "상대의 테이머 전부" : $"상대의 테이머 {m.Groups["n"].Value}장";
            return new($"{who}{J(who, "을", "를")} ", Siki(m.Groups["v"].Value.Equals("Delete", StringComparison.OrdinalIgnoreCase) ? "소멸" : "레스트"));
        }

        // "Give ~ ＜K＞" · "have ~ get ~"
        if ((m = Regex.Match(s, @"^Give (?<t>.+?) (?<kws>＜.+)$", RegexOptions.IgnoreCase)).Success)
            return Core($"{m.Groups["t"].Value} gains {m.Groups["kws"].Value}");
        if ((m = Regex.Match(s, @"^Give (?<t>.+?) (?<dp>[+-]\d+ DP.*)$", RegexOptions.IgnoreCase)).Success)
            return Core($"{m.Groups["t"].Value} gets {m.Groups["dp"].Value}");
        if ((m = Regex.Match(s, @"^Have (?<t>.+?) (?<v>get|gain) (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
            return Core($"{m.Groups["t"].Value} {m.Groups["v"].Value}s {m.Groups["rest"].Value}");

        // ---- DP · 키워드 ----
        if ((m = Regex.Match(s, @"^(?<t>.+?) (?:gains? (?<kws>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*)\s*,?\s*and gets? (?<dp>[+-]\d+) DP|gets? (?<dp>[+-]\d+) DP\s*,?\s*and (?:gains? )?(?<kws>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*))$", RegexOptions.IgnoreCase)).Success
            && TargetKo(m.Groups["t"].Value) is { } ct && KeywordListKo(m.Groups["kws"].Value) is { } ck)
            return new($"{ct}{J(ct, "은", "는")} DP {m.Groups["dp"].Value}{J(m.Groups["dp"].Value, "과", "와")} {ck}{J(ck, "을", "를")} ", Eotda);
        // "gains ＜Piercing＞ and +3000 DP" (gets 없는 표기) — 공식: "이 디지몬은 《관통》을 얻고, DP +3000." (BT21-014)
        if ((m = Regex.Match(s, @"^(?<t>.+?) gains? (?<kws>＜[^＞]+＞(?:\s*,?\s*(?:and\s*)?＜[^＞]+＞)*)\s*,?\s*and (?<dp>[+-]\d+) DP$", RegexOptions.IgnoreCase)).Success
            && TargetKo(m.Groups["t"].Value) is { } gt && KeywordListKo(m.Groups["kws"].Value) is { } gk)
        {
            bool enemyDp = Regex.IsMatch(m.Groups["t"].Value, @"opponent's", RegexOptions.IgnoreCase);
            return enemyDp
                ? new($"{gt}에게 {gk}{J(gk, "을", "를")} 주고, DP {m.Groups["dp"].Value}", new Verb("", " 할 수 있다", " 하는 것으로", "하고"))
                : new($"{gt}{J(gt, "은", "는")} {gk}{J(gk, "을", "를")} 얻고, DP {m.Groups["dp"].Value}", new Verb("", " 할 수 있다", " 하는 것으로", "하고"));
        }
        // v0.30.0 "it further gets -5000 DP" (BT26-031) — "그 디지몬의 DP를 추가로 -5000 한다"
        if ((m = Regex.Match(s, @"^(?:it|that Digimon) further gets? (?<dp>[+-]\d+) DP$", RegexOptions.IgnoreCase)).Success)
            return new($"그 디지몬의 DP를 추가로 {m.Groups["dp"].Value} ", Ha());
        if ((m = Regex.Match(s, @"^(?<t>.+?) gets? (?<dp>[+-]\d+) DP$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } dpT)
            return new($"{dpT}의 DP를 {m.Groups["dp"].Value} ", Ha());
        if ((m = Regex.Match(s, @"^(?<t>.+?) gains? (?<kws>＜.+)$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } kt && KeywordListKo(m.Groups["kws"].Value) is { } kk)
        {
            // 상대 디지몬 · 양쪽 대상("1 Digimon")에 《S 어택 -N》만 줄 때는 "~에게 주다" (공식 EX6-023: "디지몬 1마리에게 《S 어택 -1》을 줄 수 있다")
            bool enemy = Regex.IsMatch(m.Groups["t"].Value, @"opponent's", RegexOptions.IgnoreCase)
                || Regex.IsMatch(m.Groups["t"].Value, @"^(?:\d+|up to \d+) Digimon\b", RegexOptions.IgnoreCase) && Regex.IsMatch(m.Groups["kws"].Value, @"^＜Security A\.\s*[-−]\d+＞\s*$", RegexOptions.IgnoreCase);
            return enemy ? new($"{kt}에게 {kk}{J(kk, "을", "를")} ", Juda) : new($"{kt}{J(kt, "은", "는")} {kk}{J(kk, "을", "를")} ", Eotda);
        }
        // v0.29.0 — 공식 (BT13-080 · BT19-073): "이 디지몬은 진화할 수 없다" · "상대의 디지몬 1마리는 진화할 수 없다"
        if ((m = Regex.Match(s, @"^(?<t>.+?) (?:can't|cannot) digivolve$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } cantEvo)
            return new($"{cantEvo}{J(cantEvo, "은", "는")} 진화", Cannot);
        if ((m = Regex.Match(s, @"^(?<t>.+?) can't (?<what>attack or block|attack and block|attack|block)$", RegexOptions.IgnoreCase)).Success && TargetKo(m.Groups["t"].Value) is { } cantT)
        {
            string what = m.Groups["what"].Value.ToLowerInvariant();
            string w = what.Contains("attack") && what.Contains("block") ? "어택과 블록을 " : what == "attack" ? "어택" : "블록";
            return new($"{cantT}{J(cantT, "은", "는")} {w}", Cannot);
        }
        if ((m = Regex.Match(s, @"^(?<t>.+?) (?:doesn't|don't|does not|do not|can't) (?:unsuspend|become unsuspended) during (?:its owner's|their owner's|your opponent's|their|your|its) next unsuspend phase$", RegexOptions.IgnoreCase)).Success
            && TargetKo(Regex.Replace(m.Groups["t"].Value, @"^none of ", "all of ", RegexOptions.IgnoreCase)) is { } ns)
            return new($"{ns}{J(ns, "은", "는")} 다음 액티브 페이즈에 액티브가 되지 ", Anhda);
        // ---- 지속 효과 문장 ----
        if ((m = Regex.Match(s, @"^(?:this Digimon|it|this card) can't be blocked by your opponent's Digimon (?<rest>with .+)$", RegexOptions.IgnoreCase)).Success && FiltersKo(m.Groups["rest"].Value) is { } nb)
            return new($"이 디지몬은, {string.Join(" ", nb)} 상대의 디지몬에게는 블록당하지 ", Anhda);
        if (Regex.IsMatch(s, @"^(?:this Digimon|it|this card) (?:can't be blocked(?: by your opponent's Digimon)?|is unblockable)$", RegexOptions.IgnoreCase))
            return new("이 디지몬은 블록당하지 ", Anhda);
        if (Regex.IsMatch(s, @"^(?:this Digimon|it|this card) can't be attacked$", RegexOptions.IgnoreCase))
            return new("이 디지몬은 어택당하지 ", Anhda);
        if (Regex.IsMatch(s, @"^(?:this Digimon|it) can't attack$", RegexOptions.IgnoreCase))
            return new("이 디지몬은 어택", Cannot);
        if (Regex.IsMatch(s, @"^Your opponent can't reduce (?:digivolution|digivolve) costs$", RegexOptions.IgnoreCase))
            return new("상대는 진화 코스트를 줄일 수 ", new Verb("없다", "없다", "없는 것으로", "없고"));
        if ((m = Regex.Match(s, @"^(?<who>all of your (?:[^,]*? )?Digimon(?: with [^,]+?)?|your (?:[^,]*? )?Digimon(?: with [^,]+?)?) gets? (?<dp>[+-]\d+) DP$", RegexOptions.IgnoreCase)).Success
            && !m.Groups["who"].Value.Contains("opponent", StringComparison.OrdinalIgnoreCase)
            && TargetKo(Regex.Replace(m.Groups["who"].Value, @"^your ", "all of your ", RegexOptions.IgnoreCase)) is { } auraT)
            return new($"{auraT}의 DP를 {m.Groups["dp"].Value} ", Ha());
        if ((m = Regex.Match(s, @"^(?:all of )?your Security Digimon gets? (?<dp>[+-]\d+) DP$", RegexOptions.IgnoreCase)).Success)
            return new($"자신의 시큐리티 디지몬 전부의 DP를 {m.Groups["dp"].Value} ", Ha());
        if ((m = Regex.Match(s, @"^all of your opponent's Security Digimon gets? (?<dp>[+-]\d+) DP$", RegexOptions.IgnoreCase)).Success)
            return new($"상대의 시큐리티 디지몬 전부의 DP를 {m.Groups["dp"].Value} ", Ha());
        if (Regex.IsMatch(s, @"^Change the attack target to this Digimon$", RegexOptions.IgnoreCase))
            return new("어택의 대상을 이 디지몬으로 ", Ha("변경"));

        // 옵션을 코스트 없이 사용 — 공식: "자신의 패에서 사용 코스트 5 이하의 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다"
        if ((m = Regex.Match(s, $@"^Use (?:1|an?) (?<single>single-color )?(?:use cost (?<pc>\d+) or (?<pdir>lower|less|higher|more) )?(?<col>(?:{Colors}) )?Option card(?: with (?<filter>.+?))? (?:in|from) your (?<from>hand or under your Tamers|hand or trash|hand|trash) without paying (?:the|its) (?:memory |use )?cost$", RegexOptions.IgnoreCase)).Success)
        {
            var mods = new List<string>();
            // 공식 순서: 색 → 조건 · 코스트 → 1색 → 옵션 카드 ("특징 「CS」를 가진 1색의 옵션 카드" · "퍼플인 사용 코스트 7 이하의 옵션 카드")
            if (m.Groups["col"].Success) mods.Add($"{ColorKo(m.Groups["col"].Value.Trim())}인");
            if (m.Groups["filter"].Success)
            {
                string f = m.Groups["filter"].Value;
                Match fm;
                if ((fm = Regex.Match(f, @"^an? (?:memory |use |play )?cost (?:of )?(?<n>\d+)(?: or (?<dir>less|lower|more|higher))?$", RegexOptions.IgnoreCase)).Success)
                    mods.Add($"사용 코스트 {fm.Groups["n"].Value}{(fm.Groups["dir"].Success ? " " + Dir(fm.Groups["dir"].Value) : "")}의");
                else if ((fm = Regex.Match(f, $@"^(?:the )?(?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
                { string x = NamesKo(fm.Groups["x"].Value, trait: true); mods.Add($"특징으로 {x}{J(x, "을", "를")} 가진"); }
                else if ((fm = Regex.Match(f, $@"^(?<x>{TargetSpec.NameList}) in (?:its|their) (?<w>names?|texts?)$", RegexOptions.IgnoreCase)).Success)
                {
                    string x = NamesKo(fm.Groups["x"].Value);
                    mods.Add(fm.Groups["w"].Value.StartsWith("name", StringComparison.OrdinalIgnoreCase) ? $"명칭에 {x}{J(x, "을", "를")} 포함하는" : $"{x}{J(x, "이", "가")} 기술되어 있는");
                }
                else return null;
            }
            if (m.Groups["pc"].Success) mods.Add($"사용 코스트 {m.Groups["pc"].Value} {Dir(m.Groups["pdir"].Value)}의");
            if (m.Groups["single"].Success) mods.Add("1색의");
            string from = m.Groups["from"].Value.ToLowerInvariant() switch
            {
                "hand or under your tamers" => "자신의 패 또는 테이머 아래에서",
                "hand or trash" => "자신의 패 또는 트래시에서",
                "trash" => "자신의 트래시에서",
                _ => "자신의 패에서",
            };
            string what = string.Join(" ", mods.Append("옵션 카드 1장"));
            return new($"{from} {what}{J(what, "을", "를")} 코스트를 지불하지 않고 ", Ha("사용"));
        }

        // [패]【메인】 — 공식: "그 디지몬을 이 카드로 진화 조건을 무시하고 진화 코스트 3으로 진화시킨다"
        // "it digivolves into this card ~" · "1 of your [Agumon] digivolves ~" · "digivolve that Digimon into this card ~"
        if ((m = Regex.Match(s, @"^(?:(?<t>it|that Digimon|(?:1|one) of your .+?) (?<may>may )?digivolves?|digivolve (?<t>it|that Digimon|this Digimon)) into this card for a digivolution cost of (?<n>\d+),? ignoring (?:its |this card's )?digivolution requirements$", RegexOptions.IgnoreCase)).Success
            && (Regex.IsMatch(m.Groups["t"].Value, "^(?:it|that Digimon)$", RegexOptions.IgnoreCase) ? "그 디지몬"
                : m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬"
                : TargetKo(m.Groups["t"].Value) ?? (Regex.Match(m.Groups["t"].Value, $@"^(?:1|one) of your (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase) is { Success: true } evoName ? $"자신의 {NamesKo(evoName.Groups["x"].Value)} 1마리" : null)) is { } evoT)
        {
            string n = m.Groups["n"].Value;
            var evo = new Verb("진화시킨다", "진화시킬 수 있다", "진화시키는 것으로", "진화시키고");
            return new($"{evoT}{J(evoT, "을", "를")} 이 카드로 진화 조건을 무시하고 진화 코스트 {n}{J(n, "으로", "로")} ", m.Groups["may"].Success ? new Verb("진화시킬 수 있다", "진화시킬 수 있다", "진화시키는 것으로", "진화시키고") : evo);
        }
        // [트래시]【메인】 — 공식: "이 카드를 지불하는 등장 코스트 -4 하여 등장시킨다"
        if ((m = Regex.Match(s, @"^play this card(?: from your (?:hand|trash))? with the play cost reduced by (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return new($"이 카드를 지불하는 등장 코스트 -{m.Groups["n"].Value} 하여 ", Siki("등장"));

        // ---- 진화 ----
        if ((m = Regex.Match(s, @"^reduce (?:the|its) (?:digivolution cost|memory cost of the digivolution) by (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return new($"지불하는 진화 코스트를 -{m.Groups["n"].Value} ", Ha());
        if ((m = Regex.Match(s, @"^(?<t>this Digimon|this (?<selfx>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon|\d+ of your (?:[^,]*? )?Digimon(?: with [^,]+?)?) (?<may>may )?digivolves? into (?<pick>.+?) in (?:the|your) (?<zone>hand or (?:the |your )?trash|hand|trash)(?: for (?:a digivolution cost of (?<cost>\d+)|its digivolution cost))? ?(?<free>without paying (?:the|its) (?:memory |digivolution )?cost|with the (?:digivolution )?cost reduced by (?<red>\d+))?$", RegexOptions.IgnoreCase)).Success
            // v0.30.0 "this [Demon] or [Titan] trait Digimon" — 공식 표기 "특징으로 「데몬」/「타이탄」을 가진 이 디지몬"
            && (m.Groups["selfx"].Success ? $"특징으로 {NamesKo(m.Groups["selfx"].Value, trait: true)}{J(NamesKo(m.Groups["selfx"].Value, trait: true), "을", "를")} 가진 이 디지몬" : TargetKo(m.Groups["t"].Value)) is { } et
            && (PickKo("1 " + m.Groups["pick"].Value) ?? PickKo(m.Groups["pick"].Value) ?? PickKo(Regex.Replace(m.Groups["pick"].Value, @"^an? ", "1 ", RegexOptions.IgnoreCase))) is { } ep)
        {
            string how = m.Groups["free"].Value.StartsWith("without", StringComparison.OrdinalIgnoreCase) ? "코스트를 지불하지 않고 "
                : m.Groups["red"].Success ? $"진화 코스트를 -{m.Groups["red"].Value} 하고 " : m.Groups["cost"].Success ? $"진화 코스트 {m.Groups["cost"].Value}로 " : "";
            // "this Digimon may digivolve ~" — 가운데의 may도 "~할 수 있다"
            string plain = m.Groups["may"].Success ? "진화시킬 수 있다" : "진화시킨다";
            return new($"{et}{J(et, "을", "를")} {(m.Groups["zone"].Value.Contains("trash", StringComparison.OrdinalIgnoreCase) ? (m.Groups["zone"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? "패 또는 트래시의" : "트래시의") : "패의")} {ep}{J(ep, "으로", "로")} {how}", new Verb(plain, "진화시킬 수 있다", "진화시키는 것으로", "진화시키고"));
        }
        return null;
    }

    /// <summary>"turn end" · "the end of your opponent's turn" → "턴 종료 시" · "상대의 턴 종료 시" (v0.30.0)</summary>
    private static string EndWhenKo(string when)
    {
        string w = when.ToLowerInvariant();
        return w.Contains("opponent's") || w.Contains("their") ? "상대의 턴 종료 시" : w.Contains("your turn") ? "자신의 턴 종료 시" : "턴 종료 시";
    }

    /// <summary>비용 "~ing ..." → "~하는 것으로,"</summary>
    private static string? CostKo(string cost)
    {
        cost = cost.Trim().TrimEnd('.');
        Match m;
        if (CostKoPre34(cost) is { } pre34) return pre34;   // v0.34.0 "returning up to 5 ~ from your trash ~"
        if (CostKo31(cost) is { } cost31) return cost31;   // v0.31.0
        // 공식 (BT21-083): "자신의 패에서 특징 「크로스 하트」/「블루 플레어」/「히어로」를 가진 디지몬 카드 1장을 이 테이머 아래에 놓는 것으로,"
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from your hand (?:face down )?under (?<who>this Tamer|(?:1|one) of your Tamers|your Tamers)$", RegexOptions.IgnoreCase)).Success && PickKo(m.Groups["pick"].Value) is { } underK)
            return $"자신의 패에서 {underK}{J(underK, "을", "를")} {(m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 테이머" : "자신의 테이머")} 아래에 놓는 것으로,";
        // v0.30.0 이 디지몬의 진화원 중 지정된 카드 — 공식 (EX7-059 · EX7-073 · EX1-073): "이 디지몬의 진화원에서 옵션 카드 1장을 파기하는 것으로," ·
        // "이 디지몬의 진화원에서 특징으로 「3총사」를 가진 카드 2장을 파기하는 것으로," · (BT27-043) "~ 1장을 시큐리티 아래에 놓는 것으로,"
        if ((m = Regex.Match(cost, @"^(?<v>trashing|placing) (?<pick>.+?) (?:from|in|of) this Digimon's digivolution cards(?: as (?:the|your) (?<pos>top|bottom) security card)?$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } srcK)
            return m.Groups["pos"].Success
                ? $"이 디지몬의 진화원에서 {srcK}{J(srcK, "을", "를")} 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}에 놓는 것으로,"
                : $"이 디지몬의 진화원에서 {srcK}{J(srcK, "을", "를")} 파기하는 것으로,";
        // v0.29.0 — 공식 (BT22-043 · EX5-007 · BT23-008): "특징 「CS」를 가진 이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 이 디지몬의 진화원 아래에 놓는 것으로,"
        if ((m = Regex.Match(cost, @"^placing (?:this (?:(?<t1>\[[^\]]+\](?: or \[[^\]]+\])*) trait )?Digimon's top stacked card|the top card of this Digimon(?: with the (?<t2>\[[^\]]+\](?: or \[[^\]]+\])*) traits?)?) as (?:its|this Digimon's) bottom digivolution card$", RegexOptions.IgnoreCase)).Success)
        {
            string tt = m.Groups["t1"].Success ? m.Groups["t1"].Value : m.Groups["t2"].Value;
            string topWho = tt.Length == 0 ? "" : NamesKo(tt, trait: true) is { } tk ? $"특징으로 {tk}{J(tk, "을", "를")} 가진 " : "";
            return $"{topWho}이 디지몬에 겹쳐져 있는 카드를 위에서부터 1장 이 디지몬의 진화원 아래에 놓는 것으로,";
        }
        if ((m = Regex.Match(cost, @"^trashing (?<n>\d+) of this Digimon's digivolution cards$", RegexOptions.IgnoreCase)).Success)
            return $"이 디지몬의 진화원을 {m.Groups["n"].Value}장 파기하는 것으로,";
        // "deleting 1 other Digimon with [Sukamon] in its name"
        if ((m = Regex.Match(cost, @"^deleting (?:1|one) other (?<what>Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("1 of your other " + m.Groups["what"].Value) is { } otherDel)
            return $"{otherDel}{J(otherDel, "을", "를")} 소멸시키는 것으로,";
        // 7단계: 링크 카드 · 앞면 시큐리티 비용
        if ((m = Regex.Match(cost, @"^trashing (?<n>\d+) of (?:this Digimon's|its) link cards$", RegexOptions.IgnoreCase)).Success)
            return $"이 디지몬의 링크 카드를 {m.Groups["n"].Value}장 파기하는 것으로,";
        if (Regex.IsMatch(cost, @"^flipping your top face[- ]up security card face[- ]down$", RegexOptions.IgnoreCase))
            return "자신의 앞면의 시큐리티를 위에서부터 1장 뒷면으로 하는 것으로,";
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:from|in) (?<from>your hand or (?:your )?trash|your trash|your hand) face[- ]up as (?:the|your) (?<pos>top|bottom) security card$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } fuCost)
            return $"{(m.Groups["from"].Value.Contains("trash") && m.Groups["from"].Value.Contains("hand") ? "자신의 패 또는 트래시에서" : m.Groups["from"].Value.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서")} {fuCost}{J(fuCost, "을", "를")} 시큐리티 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}에 앞면으로 놓는 것으로,";
        // [패]【메인】 — 공식: "1 코스트 지불하고, 이 카드를 특징으로 「Legend-Arms」를 가지거나 Lv.3의 자신의 디지몬의 진화원 아래에 놓는 것으로,"
        if ((m = Regex.Match(cost, @"^paying (?<n>\d+) (?:cost|memory) and (?<b>.+)$", RegexOptions.IgnoreCase)).Success && CostKo(m.Groups["b"].Value) is { } paidCost)
            return $"{m.Groups["n"].Value} 코스트 지불하고, {paidCost}";
        if ((m = Regex.Match(cost, $@"^placing this card as the bottom digivolution card of (?:1|one) of your Digimon that's level (?<lv>\d+) or has the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"이 카드를 특징으로 {x}{J(x, "을", "를")} 가지거나 Lv.{m.Groups["lv"].Value}의 자신의 디지몬의 진화원 아래에 놓는 것으로,";
        }
        // 공식: "자신의 패의 「테슬라젤리몬」 1장을 자신의 「젤리몬」의 진화원 아래에 놓는 것으로,"
        if ((m = Regex.Match(cost, $@"^placing (?:1|one) (?<x>{TargetSpec.NameList}) from your (?<z>hand|trash) (?:as (?:any|one|1) of your (?<y>{TargetSpec.NameList})'s bottom digivolution card|under (?:any|one|1) of your (?<y>{TargetSpec.NameList}) as its bottom digivolution card)$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value), y = NamesKo(m.Groups["y"].Value);
            return $"자신의 {(m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase) ? "패" : "트래시")}의 {x} 1장을 자신의 {y}의 진화원 아래에 놓는 것으로,";
        }
        // 공식: "이 테이머를 레스트시키고, 자신의 테이머 아래의 카드 1장을 그 디지몬의 진화원 아래에 놓는 것으로,"
        if ((m = Regex.Match(cost, @"^suspending this (?<w>Tamer|Digimon) and (?<b>.+)$", RegexOptions.IgnoreCase)).Success && CostKo(m.Groups["b"].Value) is { } andCost)
            return $"이 {(m.Groups["w"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase) ? "테이머" : "디지몬")}를 레스트시키고, {andCost}";
        if (Regex.IsMatch(cost, @"^placing 1 card from under (?:one|any) of your Tamers as (?:the|that Digimon's) bottom digivolution card(?: of that Digimon)?$", RegexOptions.IgnoreCase))
            return "자신의 테이머 아래의 카드 1장을 그 디지몬의 진화원 아래에 놓는 것으로,";
        // v0.17.0 비용: 시큐리티 위/아래 · 뒷면 진화원 · 테이머 아래 뒷면 카드
        if (Regex.IsMatch(cost, @"^trashing the top security card of 1 player with the most security cards$", RegexOptions.IgnoreCase))
            return "시큐리티가 가장 많은 플레이어 1명의 시큐리티를 위에서부터 1장 파기하는 것으로,";
        if (Regex.IsMatch(cost, @"^trashing your top or bottom security card$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티의 위 또는 아래에서 1장 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^placing (?:(?<deck>the top card of your deck)|(?<pick>.+?) (?:from|in) (?<from>your hand|your trash)) face[- ]down as this Digimon's bottom digivolution card$", RegexOptions.IgnoreCase)).Success
            && (m.Groups["deck"].Success ? "자신의 덱 위에서 카드 1장" : PickKo(m.Groups["pick"].Value) is { } fd ? $"{(m.Groups["from"].Value.Contains("hand") ? "자신의 패에서" : "자신의 트래시에서")} {fd}" : null) is { } fdWhat)
            return $"{fdWhat}{J(fdWhat, "을", "를")} 이 디지몬의 진화원 가장 아래에 뒷면으로 놓는 것으로,";
        if ((m = Regex.Match(cost, @"^trashing the bottom face-down card from under any of your Tamers(?<sec> or your top security card)?$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 테이머 아래의 뒷면 카드를 아래에서부터 1장{(m.Groups["sec"].Success ? " 또는 자신의 시큐리티를 위에서부터 1장" : "")} 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^trashing (?<n>\d+) bottom face-down cards from under any of your Tamers$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 테이머 아래의 뒷면 카드를 아래에서부터 {m.Groups["n"].Value}장 파기하는 것으로,";
        if ((m = Regex.Match(cost, @"^(?:paying (?<n>\d+) (?:memory|cost)|decreasing your memory by (?<n>\d+))$", RegexOptions.IgnoreCase)).Success)
            return $"메모리를 -{m.Groups["n"].Value} 하는 것으로,";
        if ((m = Regex.Match(cost, @"^(?<v>deleting|returning) (?:one|1) of your (?!opponent)(?<other>other )?(?<what>[^,]*?)(?<hand> to (?:its owner's|your|the) hand)?$", RegexOptions.IgnoreCase)).Success
            && WhoKo((m.Groups["other"].Success ? "other " : "") + m.Groups["what"].Value) is { } who)
        {
            string w = $"자신의 {who} 1마리";
            return m.Groups["v"].Value.Equals("deleting", StringComparison.OrdinalIgnoreCase) ? $"{w}{J(w, "을", "를")} 소멸시키는 것으로," : $"{w}{J(w, "을", "를")} 패로 되돌리는 것으로,";
        }
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from your hand (?:on top of|at the top of) this (?:card|Digimon)'s digivolution cards$", RegexOptions.IgnoreCase)).Success && PickKo(m.Groups["pick"].Value) is { } pp)
            return $"자신의 패에서 {pp}{J(pp, "을", "를")} 이 디지몬의 진화원 위에 놓는 것으로,";
        if ((m = Regex.Match(cost, @"^trashing (?<pick>.+?) (?:in|from) your hand$", RegexOptions.IgnoreCase)).Success && !Regex.IsMatch(m.Groups["pick"].Value, @"^\d+ cards?$") && PickKo(m.Groups["pick"].Value) is { } tp)
            return $"자신의 패에서 {tp}{J(tp, "을", "를")} 파기하는 것으로,";
        // 그 밖: 동명사 → 명령형으로 되돌려 동작 틀로 ("suspending 1 of your Digimon" → "Suspend 1 of your Digimon")
        if ((m = Regex.Match(cost, @"^(?<g>suspending|trashing|deleting|returning|placing|adding|unsuspending)\b(?<rest>.*)$", RegexOptions.IgnoreCase)).Success)
        {
            string verb = m.Groups["g"].Value.ToLowerInvariant() switch
            {
                "suspending" => "Suspend", "trashing" => "Trash", "deleting" => "Delete", "returning" => "Return",
                "placing" => "Place", "adding" => "Add", _ => "Unsuspend",
            };
            if (Core(verb + m.Groups["rest"].Value) is { } p)
                return p.Render(Form.By) + ",";
        }
        return CostKo32(cost) ?? CostKo33(cost) ?? CostKo34(cost);   // v0.32.0 (KoreanEffectRenderer.Batch32.cs) · v0.33.0 (Batch33)
    }

    // =====================================================================
    // 조건 · 사건 · 셈
    // =====================================================================

    /// <summary>조건 → ("~일 때," / "~인 동안,")</summary>
    private sealed record Cond(string If, string While, string? Ra = null)
    {
        /// <summary>다른 조건 · 사건 뒤에 이어지는 "~라면," (없으면 "~일 때,")</summary>
        public string Then => Ra ?? If;
    }

    private static Cond Have(string subject) => new($"{subject} 있을 때,", $"{subject} 있는 동안,", $"{subject} 있다면,");
    private static Cond NotHave(string subject) => new($"{subject} 없을 때,", $"{subject} 없는 동안,", $"{subject} 없다면,");
    private static Cond Be(string noun) => new($"{noun}일 때,", $"{noun}인 동안,", $"{noun}{(Batchim(noun) ? "이라면" : "라면")},");

    private static Cond? CondKo(string cond)
    {
        cond = cond.Trim();
        Match m;
        // 화면 점검 2026-10-22: 상대 디지몬이 없을 때 · 이 디지몬이 N색 이상 · "그중 ~ 특징을 가진 디지몬이 있으면"
        if ((m = Regex.Match(cond, @"^your opponent has no (?<s>unsuspended |suspended )?Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = $"{(m.Groups["s"].Value.Trim().Equals("unsuspended", StringComparison.OrdinalIgnoreCase) ? "액티브 상태인 " : m.Groups["s"].Success ? "레스트 상태인 " : "")}상대의 디지몬이";
            return new($"{x} 없을 때,", $"{x} 없는 동안,", $"{x} 없다면,");
        }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has (?<n>\d+) or more colors$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬이 {m.Groups["n"].Value}색 이상");
        // v0.30.0 — 공식 (BT13-111 · P-186): "자신의 디지몬이 없다면" · "DP 13000 이상의 디지몬이 있다면,"
        if (Regex.IsMatch(cond, @"^you have no Digimon(?: in play)?$", RegexOptions.IgnoreCase))
            return NotHave("자신의 디지몬이");
        if ((m = Regex.Match(cond, @"^there is a Digimon with (?<n>\d+) DP or (?<dir>more|higher|less|lower)$", RegexOptions.IgnoreCase)).Success)
            return Have($"DP {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}의 디지몬이");
        // v0.30.0 — 공식 (BT21-078): "자신의 테이머의 색 합계가 2색 이상이라면,"
        if ((m = Regex.Match(cond, @"^your Tamers have (?<n>\d+) or more (?:total )?colors$", RegexOptions.IgnoreCase)).Success)
            return Be($"자신의 테이머의 색 합계가 {m.Groups["n"].Value}색 이상");
        if ((m = Regex.Match(cond, $@"^any of them (?:have|has) the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return Have($"그중 특징으로 {x}{J(x, "을", "를")} 가진 디지몬이");
        }
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:have|has) (?<n>\d+) or (?<dir>more|fewer|less) security cards$", RegexOptions.IgnoreCase)).Success)
            return Be($"{Side(m.Groups["who"].Value)}의 시큐리티가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) security cards?$", RegexOptions.IgnoreCase)).Success)
            return Have($"자신의 시큐리티가 {m.Groups["n"].Value}장");
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:have|has) (?<n>\d+) or (?<dir>more|fewer|less) memory$", RegexOptions.IgnoreCase)).Success)
            return Be($"{(m.Groups["who"].Value.StartsWith("your", StringComparison.OrdinalIgnoreCase) ? "상대의 메모리가" : "메모리가")} {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:have|has) (?<n>\d+) or (?<dir>more|fewer|less) cards in (?:your|their|your opponent's) (?<z>hand|trash)$|^(?:there are|you have) (?<n>\d+) or (?<dir>more) cards in (?<who>your|your opponent's) (?<z>trash)$", RegexOptions.IgnoreCase)).Success)
            return Be($"{Side(m.Groups["who"].Value)}의 {(m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase) ? "패가" : "트래시가")} {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(cond, @"^your hand has (?<n>\d+) or (?<dir>fewer|less|more) cards$", RegexOptions.IgnoreCase)).Success)
            return Be($"자신의 패가 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?<neg>don't |doesn't |do not |does not )?(?:have|has) (?:a|an|any) (?<what>Tamer|Digimon)(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string s = $"{Side(m.Groups["who"].Value)}의 {(m.Groups["what"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase) ? "테이머가" : "디지몬이")}";
            return m.Groups["neg"].Success ? NotHave(s) : Have(s);
        }
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?<neg>don't |doesn't |do not |does not )?(?:have|has) (?:a|an|any|1|(?<n>\d+) or more) (?<spec>[^,]*?(?:Digimon|Tamers?))(?: in play)?(?<with> with [^,]+?)?(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool opp = m.Groups["who"].Value.Contains("opponent", StringComparison.OrdinalIgnoreCase);
            string spec = $"all of {(opp ? "your opponent's" : "your")} {m.Groups["spec"].Value}{m.Groups["with"].Value}";
            if (TargetKo(spec, bare: true) is { } t)   // 읽지 못하면 아래의 다른 틀로
            {
                string count = m.Groups["n"].Success ? $" {m.Groups["n"].Value}{(m.Groups["spec"].Value.Contains("Tamer") ? "장" : "마리")} 이상" : "";
                string subj = $"{t}{(count.Length > 0 ? J(t, "이", "가") + count : J(t, "이", "가"))}";
                return m.Groups["neg"].Success ? NotHave(subj) : Have(subj);
            }
        }
        if ((m = Regex.Match(cond, @"^you (?:have )?(?<neg>don't have|do not have|have) \[(?<x>[^\]]+)\] in (?:the|your) battle area$", RegexOptions.IgnoreCase)).Success)
        {
            string s = $"배틀 에어리어에 「{NameKo(m.Groups["x"].Value)}」{J(NameKo(m.Groups["x"].Value), "이", "가")}";
            return m.Groups["neg"].Value.Equals("have", StringComparison.OrdinalIgnoreCase) ? Have(s) : NotHave(s);
        }
        if ((m = Regex.Match(cond, $@"^you have (?:an? )?(?<kind>Digimon|Tamer) with (?<x>{TargetSpec.NameList}) in its name(?: on the field| in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return Have($"명칭에 {x}{J(x, "을", "를")} 포함하는 자신의 {(m.Groups["kind"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase) ? "테이머가" : "디지몬이")}");
        }
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:(?<neg>don't|doesn't|do not|does not) )?ha(?:ve|s) (?:(?:a|an|any) )?\[(?<x>[^\]]+)\](?: card)?(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string s = $"{Side(m.Groups["who"].Value)}의 「{NameKo(m.Groups["x"].Value)}」{J(NameKo(m.Groups["x"].Value), "이", "가")}";
            return m.Groups["neg"].Success ? NotHave(s) : Have(s);
        }
        if (Regex.IsMatch(cond, @"^you have no other Digimon(?: in play)?$", RegexOptions.IgnoreCase))
            return NotHave("자신의 다른 디지몬이");
        if (Regex.IsMatch(cond, @"^you have no face-up security cards$", RegexOptions.IgnoreCase))
            return NotHave("자신의 앞면 시큐리티가");
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:have|has) (?<n>\d+) or (?<dir>more|fewer|less) (?<what>Tamers|Digimon)(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool tamer = m.Groups["what"].Value.StartsWith("Tamer", StringComparison.OrdinalIgnoreCase);
            return Have($"{Side(m.Groups["who"].Value)}의 {(tamer ? "테이머가" : "디지몬이")} {m.Groups["n"].Value}{(tamer ? "장" : "마리")} {Dir(m.Groups["dir"].Value)}");
        }
        // "you have a Digimon or Tamer with the [CS] trait on the field" · "you have [X] trait Digimon (or Tamer)"
        if ((m = Regex.Match(cond, $@"^you have (?:an? )?(?:(?<x>{TargetSpec.NameList}) trait (?<kind>Digimon or Tamers?|Digimon|Tamers?)|(?<kind>Digimon or Tamers?|Digimon|Tamers?) with the (?<x>{TargetSpec.NameList}) trait)(?: on the field| in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            string kind = m.Groups["kind"].Value.ToLowerInvariant().StartsWith("digimon or") ? "디지몬 또는 테이머" : m.Groups["kind"].Value.StartsWith("Tamer", StringComparison.OrdinalIgnoreCase) ? "테이머" : "디지몬";
            string subj = $"특징으로 {x}{J(x, "을", "를")} 가진 자신의 {kind}";
            return Have(subj + J(subj, "이", "가"));
        }
        if ((m = Regex.Match(cond, $@"^(?:a|an) (?:(?<kind>Digimon|Tamer|Option) )?card with (?:the )?(?<x>{TargetSpec.NameList}) (?<what>traits?|in its name) is in your trash$", RegexOptions.IgnoreCase)).Success)
        {
            bool trait = m.Groups["what"].Value.StartsWith("trait", StringComparison.OrdinalIgnoreCase);
            string x = NamesKo(m.Groups["x"].Value, trait);
            string kind = m.Groups["kind"].Value.ToLowerInvariant() switch { "digimon" => "디지몬 카드", "tamer" => "테이머 카드", "option" => "옵션 카드", _ => "카드" };
            return Have($"자신의 트래시에 {(trait ? $"특징으로 {x}{J(x, "을", "를")} 가진" : $"명칭에 {x}{J(x, "을", "를")} 포함하는")} {kind}{J(kind, "이", "가")}");
        }
        // 공식: "자신의 트래시에 명칭에 「묘티스몬」을 포함하는 카드가 5장 이상 있을 때,"
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or more (?<pick>(?:[^,]+? )?cards?(?: with [^,]+?)?) in your trash$", RegexOptions.IgnoreCase)).Success
            && PickKo("1 " + Regex.Replace(m.Groups["pick"].Value, @"\bcards\b", "card", RegexOptions.IgnoreCase)) is { } trashPick)
        {
            string tp = Regex.Replace(trashPick, @" 1장$", "");
            return Have($"자신의 트래시에 {tp}{J(tp, "이", "가")} {m.Groups["n"].Value}장 이상");
        }
        if ((m = Regex.Match(cond, $@"^(?:one of|any of) your Digimon (?:has|have) (?<x>{TargetSpec.NameList}) in its name(?: or (?<y>{TargetSpec.NameList}) in its name)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value) + (m.Groups["y"].Success ? " 또는 " + NamesKo(m.Groups["y"].Value) : "");
            return Have($"명칭에 {x}{J(x, "을", "를")} 포함하는 자신의 디지몬이");
        }
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) has (?<x>{TargetSpec.NameList}) in its texts?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return Have($"이 디지몬의 텍스트에 {x}{J(x, "이", "가")}");
        }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has ＜(?<k>[^＞]+?)\s*＞ in its texts?$", RegexOptions.IgnoreCase)).Success && KeywordKo(m.Groups["k"].Value, false) is { } tk)
            return Have($"이 디지몬의 텍스트에 {tk}{J(tk, "이", "가")}");
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) is (?<s>suspended|unsuspended)$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬이 {(m.Groups["s"].Value.StartsWith("un", StringComparison.OrdinalIgnoreCase) ? "액티브 상태" : "레스트 상태")}");
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) is (?:a )?level (?<n>\d+)(?: or (?<dir>higher|lower|more|less))?(?: Digimon)?$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬이 Lv.{m.Groups["n"].Value}{(m.Groups["dir"].Success ? " " + Dir(m.Groups["dir"].Value) : "")}");
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has ＜(?<k>[^＞]+?)\s*＞$", RegexOptions.IgnoreCase)).Success && KeywordKo(m.Groups["k"].Value, false) is { } hk)
            return new($"이 디지몬이 {hk}{J(hk, "을", "를")} 가지고 있을 때,", $"이 디지몬이 {hk}{J(hk, "을", "를")} 가지는 동안,");
        // "this Digimon is [Titamon] or has the [Titan] trait" → 이 디지몬이 「티타몬」이거나 특징으로 「타이탄족」을 가질 때
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) is (?<n>{TargetSpec.NameList}) or has (?:the )?(?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        {
            string n = NamesKo(m.Groups["n"].Value), x = NamesKo(m.Groups["x"].Value, trait: true);
            return new($"이 디지몬이 {n}이거나 특징으로 {x}{J(x, "을", "를")} 가질 때,", $"이 디지몬이 {n}이거나 특징으로 {x}{J(x, "을", "를")} 가지는 동안,");
        }
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) has (?:the )?(?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return new($"이 디지몬이 특징으로 {x}{J(x, "을", "를")} 가질 때,", $"이 디지몬이 특징으로 {x}{J(x, "을", "를")} 가지는 동안,");
        }
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) has (?<x>{TargetSpec.NameList}) in its names?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return new($"이 디지몬의 명칭에 {x}{J(x, "이", "가")} 포함될 때,", $"이 디지몬의 명칭에 {x}{J(x, "이", "가")} 포함되는 동안,");
        }
        if ((m = Regex.Match(cond, $@"^(?:(?:a|an) (?:Digimon |Tamer )?card with )?(?<x>{TargetSpec.NameList})(?: in its name)? (?:is|are) in this Digimon's digivolution cards$|^this Digimon's digivolution cards include (?:a card with )?(?<x>{TargetSpec.NameList})(?: in its name)?$|^(?:this Digimon|it) has (?<x>{TargetSpec.NameList}) in its digivolution cards$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return Have($"이 디지몬의 진화원에 {x}{J(x, "이", "가")}");
        }
        if ((m = Regex.Match(cond, $@"^this Digimon's digivolution cards include (?:a |an )?(?:(?<kind>Digimon|Tamer) )?card with the (?<x>{TargetSpec.NameList}) traits?(?: or (?:a |an )?(?<color>{Colors}) Tamer card)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            string extra = m.Groups["color"].Success ? $" 또는 {ColorKo(m.Groups["color"].Value)}의 테이머 카드가" : "";
            return Have($"이 디지몬의 진화원에 특징으로 {x}{J(x, "을", "를")} 가진 카드{(extra.Length > 0 ? extra : "가")}");
        }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has (?<n>\d+) or (?<dir>more|fewer|less) digivolution cards$", RegexOptions.IgnoreCase)).Success)
            return Be($"이 디지몬의 진화원이 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}");
        if (Regex.IsMatch(cond, @"^(?:this Digimon|it) has no digivolution cards$", RegexOptions.IgnoreCase))
            return NotHave("이 디지몬의 진화원이");
        if (Regex.IsMatch(cond, @"^it's your turn$", RegexOptions.IgnoreCase))
            return Be("자신의 턴");
        if (Regex.IsMatch(cond, @"^you do$", RegexOptions.IgnoreCase))
            return new("그렇게 했을 때,", "그렇게 한 동안,");
        // 시큐리티 찾기 뒤 — 공식 (EX5-027 · BT13-012 · EX3-029): "이 효과로 추가했다면," · "등장했다면," · "그 카드가 옐로인 카드라면"
        if (Regex.IsMatch(cond, @"^you added(?: (?:a card|cards))?(?: to (?:the|your) hand)?$", RegexOptions.IgnoreCase))
            return new("이 효과로 추가했다면,", "이 효과로 추가한 동안,");
        if (Regex.IsMatch(cond, @"^you did$", RegexOptions.IgnoreCase))
            return new("그렇게 했다면,", "그렇게 한 동안,");
        if ((m = Regex.Match(cond, $@"^it's an? (?<c>{Colors}) card$", RegexOptions.IgnoreCase)).Success)
            return new($"그 카드가 {ColorKo(m.Groups["c"].Value)}인 카드라면,", $"그 카드가 {ColorKo(m.Groups["c"].Value)}인 카드인 동안,");
        if (Regex.IsMatch(cond, @"^no Digimon (?:was|were|is) deleted by this effect$", RegexOptions.IgnoreCase))
            return new("이 효과로 디지몬이 소멸하지 않았을 때,", "이 효과로 디지몬이 소멸하지 않은 동안,");
        // 공식 (BT17-010 · BT16-025): "이 효과로 소멸하지 않았다면," · "이 효과로 레스트하지 않았다면,"
        if ((m = Regex.Match(cond, @"^this effect didn't (?<v>delete|suspend)(?<o> (?:an|your|1 of your) opponent's Digimon| a Digimon| any Digimon)?$", RegexOptions.IgnoreCase)).Success)
        {
            string v = m.Groups["v"].Value.Equals("delete", StringComparison.OrdinalIgnoreCase) ? "소멸하지" : "레스트하지";
            string who = m.Groups["o"].Value.Contains("opponent", StringComparison.OrdinalIgnoreCase) ? "상대의 디지몬이 " : "";
            return new($"이 효과로 {who}{v} 않았다면,", $"이 효과로 {who}{v} 않은 동안,");
        }
        // 공식 (BT20-018 · BT6-007): "이 디지몬이 「알파몬: 왕룡검」이라면," · "이 디지몬이 「아구몬 -용기의 유대-」인 동안,"
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) is (?<x>{TargetSpec.NameList})(?: or has the (?<t>{TargetSpec.NameList}) traits?)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            if (!m.Groups["t"].Success) return Be($"이 디지몬이 {x}");
            string t = NamesKo(m.Groups["t"].Value, trait: true);
            return new($"이 디지몬이 {x}이거나 특징으로 {t}{J(t, "을", "를")} 가질 때,", $"이 디지몬이 {x}이거나 특징으로 {t}{J(t, "을", "를")} 가진 동안,", $"이 디지몬이 {x}이거나 특징으로 {t}{J(t, "을", "를")} 가진다면,");
        }
        if (Regex.IsMatch(cond, @"^this (?:card|Digimon) was deleted by (?:an|your opponent's) effects?$", RegexOptions.IgnoreCase))
            return new("이 카드가 효과로 소멸했을 때,", "이 카드가 효과로 소멸한 동안,");
        if ((m = Regex.Match(cond, @"^(?:that Digimon|it) has (?<n>\d+) DP or (?<dir>more|less)$", RegexOptions.IgnoreCase)).Success)
            return Be($"그 디지몬의 DP가 {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(cond, @"^(?:that Digimon's|its) play cost is (?<n>\d+) or (?<dir>less|more|lower|higher)$", RegexOptions.IgnoreCase)).Success)
            return Be($"그 디지몬의 등장 코스트가 {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}");
        if ((m = Regex.Match(cond, @"^you have another Digimon with the same name as this Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
            return Have("이 디지몬과 같은 명칭의 자신의 다른 디지몬이");
        if (Regex.IsMatch(cond, @"^(?:DNA digivolving|DigiXrosing|it DNA digivolved|this Digimon DNA digivolved)$", RegexOptions.IgnoreCase))
            return new(cond.StartsWith("Digi", StringComparison.OrdinalIgnoreCase) ? "디지크로스하고 있었을 때," : "조그레스 진화했을 때,", "조그레스 진화한 동안,");
        return CondKo32(cond) ?? CondKo33(cond) ?? CondKo34(cond);   // v0.32.0 · v0.33.0
    }

    /// <summary>"When ~," 사건 → "~했을 때,"</summary>
    private static string? EventKo(string ev)
    {
        ev = ev.Trim();
        Match m;
        if (Regex.IsMatch(ev, @"^this Digimon (?:is|becomes|gets) blocked$", RegexOptions.IgnoreCase)) return "이 디지몬이 블록당했을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon attacks$", RegexOptions.IgnoreCase)) return "이 디지몬이 어택했을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon attacks (?:a player|your opponent|the opponent)$", RegexOptions.IgnoreCase)) return "이 디지몬이 플레이어에게 어택했을 때,";
        if ((m = Regex.Match(ev, @"^(?:you attack|this Digimon attacks) (?:one of |1 of |an? )?(?:your )?opponent's (?<spec>[^,]*?Digimon(?: with [^,]+)?)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your opponent's " + m.Groups["spec"].Value, bare: true) is { } atk)
            return $"이 디지몬이 {atk}에게 어택했을 때,";
        if ((m = Regex.Match(ev, @"^this Digimon deletes (?:an|one of your|1 of your|your|one of|1 of) ?(?:your )?opponent's (?<spec>[^,]*?Digimon(?: with [^,]+?)?) in battle(?<s> and survives)?$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your opponent's " + m.Groups["spec"].Value, bare: true) is { } del)
            return $"이 디지몬이 배틀에서 {del}{J(del, "을", "를")} 소멸시켰을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon wins a battle$", RegexOptions.IgnoreCase)) return "이 디지몬이 배틀에서 이겼을 때,";
        // 시큐리티에서 카드가 빠졌을 때 / 놓였을 때 (SentenceParser.ParseEventPhrase와 같은 표기)
        if (Regex.IsMatch(ev, @"^(?:your security stack is removed from|(?:a card is|cards are|one or more cards are) (?:removed|trashed) from your security stack|your security cards? (?:is|are) (?:removed|trashed))$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티에서 카드가 없어졌을 때,";
        if (Regex.IsMatch(ev, @"^(?:your opponent's security stack is removed from|(?:a card is|cards are|one or more cards are) (?:removed|trashed) from your opponent's security stack|your opponent's security cards? (?:is|are) (?:removed|trashed))$", RegexOptions.IgnoreCase))
            return "상대의 시큐리티에서 카드가 없어졌을 때,";
        if (Regex.IsMatch(ev, @"^(?:security stacks are removed from|any security stack is removed from|(?:a card is|cards are) removed from (?:a|any) (?:player's )?security stack)$", RegexOptions.IgnoreCase))
            return "어느 쪽이든 시큐리티에서 카드가 없어졌을 때,";
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) (?:added to|placed in|placed on top of|placed at the top of) your security stack$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티에 카드가 놓였을 때,";
        // 7단계: 벗어날 때 · 소멸할 때 (단서: 자신의 효과 이외 · 배틀 이외 · 배틀에서 · 상대의 효과로)
        if ((m = Regex.Match(ev, @"^(?:(?<self>this Digimon)(?<selfwith> with [^,]+?)?|(?:one of |any of )?your (?<subj>(?:other )?[^,]*?(?:Digimon|\]))(?<with> with [^,]+?)?) would (?<what>leave the battle area|be deleted)(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && LeaveQualifierKo(m.Groups["q"].Value) is { } lq)
        {
            string what = m.Groups["what"].Value.StartsWith("be deleted", StringComparison.OrdinalIgnoreCase) ? "소멸할 때," : "배틀 에어리어를 벗어날 때,";
            string? who = m.Groups["self"].Success
                ? (m.Groups["selfwith"].Success ? WithKo(m.Groups["selfwith"].Value.Trim()) is { } sw ? $"{sw} 이 디지몬" : null : "이 디지몬")
                : TargetKo("all of your " + m.Groups["subj"].Value + m.Groups["with"].Value, bare: true);
            if (who != null) return $"{who}{J(who, "이", "가")} {lq}{what}";
        }
        if (Regex.IsMatch(ev, @"^this Digimon (?:gets|is|becomes) linked$|^a card (?:is linked|links) to this Digimon$", RegexOptions.IgnoreCase)) return "이 디지몬이 링크했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) (?:get|gets|are|is|become|becomes) linked$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["subj"].Value, bare: true) is { } linked)
            return $"{linked}{J(linked, "이", "가")} 링크했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) (?:is |are )?(?:moves?|moved) from (?:your|the) breeding area(?: to (?:your|the) battle area)?$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["subj"].Value, bare: true) is { } moved)
            return $"{moved}{J(moved, "이", "가")} 육성 에어리어에서 배틀 에어리어로 이동했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's (?:[^,]*?)Digimon (?:is |are )?(?:moves?|moved) from (?:their|the) breeding area(?: to (?:their|the) battle area)?$|^your opponent moves a Digimon from their breeding area$", RegexOptions.IgnoreCase))
            return "상대의 디지몬이 육성 에어리어에서 배틀 에어리어로 이동했을 때,";
        if (Regex.IsMatch(ev, @"^you hatch (?:a Digi-Egg )?in (?:the|your) breeding area$", RegexOptions.IgnoreCase)) return "자신의 육성 에어리어에 부화했을 때,";
        if (Regex.IsMatch(ev, @"^cards? (?:are|is) placed face[- ]up in your security stack$", RegexOptions.IgnoreCase)) return "자신의 시큐리티에 카드가 앞면으로 놓였을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon checks (?:a )?face[- ]up security cards?$", RegexOptions.IgnoreCase)) return "이 디지몬이 앞면의 시큐리티를 체크했을 때,";
        if (Regex.IsMatch(ev, @"^(?:any of |one of )?your Digimon checks? (?:a )?face[- ]up security cards?$", RegexOptions.IgnoreCase)) return "자신의 디지몬이 앞면의 시큐리티를 체크했을 때,";
        if (Regex.IsMatch(ev, @"^your hand is trashed from$|^cards? (?:are|is) trashed from your hand$", RegexOptions.IgnoreCase)) return "자신의 패가 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^this card is trashed from (?:the|your) hand$", RegexOptions.IgnoreCase)) return "이 카드가 패에서 파기되었을 때,";
        // v0.29.0 — 공식 (BT10-080 · BT15-084 · P-077 · EX10-041 · BT9-002)
        if (Regex.IsMatch(ev, @"^this card is trashed from (?:the|your) hand by (?:one of )?your effects?$", RegexOptions.IgnoreCase)) return "자신의 효과로 이 카드가 패에서 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:(?:an effect trashes|effects trash) this card from|this card is trashed from) (?:the|your) security stack$", RegexOptions.IgnoreCase)) return "이 카드가 시큐리티에서 효과로 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:this card is trashed from (?:the top of )?(?:the|your) deck|(?:an effect trashes|effects trash) this card from (?:the|your) deck)$", RegexOptions.IgnoreCase)) return "이 카드가 덱에서 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect trashes|effects trash) this card from the deck or security stack$", RegexOptions.IgnoreCase)) return "이 카드가 덱 또는 시큐리티에서 효과로 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^an effect adds (?:a card|cards) to your hand$", RegexOptions.IgnoreCase)) return "효과로 자신의 패가 늘어났을 때,";
        // v0.30.0 — 공식 (BT15-002 · BT15-083): "자신의 패가 자신의 디지몬의 효과로 늘어났을 때,"
        if (Regex.IsMatch(ev, @"^(?:one of your|any of your|your) Digimon's effects? adds? (?:a card|cards) to your hand$", RegexOptions.IgnoreCase)) return "자신의 패가 자신의 디지몬의 효과로 늘어났을 때,";
        // v0.30.0 — 공식 (BT19-093 · P-159 · BT23-059): "이 카드가 배틀 에어리어에서 (효과로) 파기되었을 때," · "배틀 에어리어의 옵션 카드가 파기되었을 때,"
        if (Regex.IsMatch(ev, @"^this card is trashed from (?:the|your) battle area$", RegexOptions.IgnoreCase)) return "이 카드가 배틀 에어리어에서 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect trashes|effects trash) this card from (?:the|your) battle area$", RegexOptions.IgnoreCase)) return "이 카드가 배틀 에어리어에서 효과로 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an )?Option cards? in the battle area (?:is|are) trashed$", RegexOptions.IgnoreCase)) return "배틀 에어리어의 옵션 카드가 파기되었을 때,";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?:(?<x>{TargetSpec.NameList}) trait )?Option cards? (?:is|are) placed in (?:the|your) battle area$", RegexOptions.IgnoreCase)).Success)
        {
            string trait = m.Groups["x"].Success ? NamesKo(m.Groups["x"].Value, trait: true) : "";
            return $"{(trait.Length > 0 ? $"특징으로 {trait}{J(trait, "을", "를")} 가진 " : "")}자신의 옵션 카드가 배틀 에어리어에 놓였을 때,";
        }
        // 어택 대상이 바뀌었을 때 (v0.15.0 — SentenceParser와 같은 표기)
        if (Regex.IsMatch(ev, @"^(?:an|the) attack targets? (?:is|are) (?:switched|changed)$|^attack targets? (?:change|changes|switch|switches)$", RegexOptions.IgnoreCase)) return "어택의 대상이 변경되었을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon's attack target (?:is switched|is changed|changes|switches)$", RegexOptions.IgnoreCase)) return "이 디지몬의 어택의 대상이 변경되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:one|any) of your Digimon's attack targets? (?:is|are) (?:switched|changed)$", RegexOptions.IgnoreCase)) return "자신의 디지몬의 어택의 대상이 변경되었을 때,";
        // v0.19.0 시점 묶음 (공식 표기)
        if (Regex.IsMatch(ev, @"^(?:one of your effects trashes (?:a card|cards) in your hand|you trash (?:a card|cards) in your hand using one of your effects)$", RegexOptions.IgnoreCase)) return "자신의 효과로 자신의 패를 파기했을 때,";
        if ((m = Regex.Match(ev, @"^your opponent's hand is trashed from(?: or effects trash cards from under (?<t>this Tamer|your Tamers))?$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["t"].Success ? $"상대의 패가 파기되거나, 효과로 {(m.Groups["t"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 테이머" : "자신의 테이머")} 아래의 카드가 파기되었을 때," : "상대의 패가 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your deck$", RegexOptions.IgnoreCase)) return "자신의 덱이 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^an effect adds (?:a card|cards) to your opponent's hand$", RegexOptions.IgnoreCase)) return "효과로 상대의 패에 카드가 추가되었을 때,";
        if ((m = Regex.Match(ev, @"^you use an Option card with a (?:use |memory )?cost (?:of )?(?<n>\d+) or (?<dir>more|less|higher|lower)$", RegexOptions.IgnoreCase)).Success)
            return $"자신이 사용 코스트 {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}의 옵션 카드를 사용했을 때,";
        if ((m = Regex.Match(ev, $@"^you (?<would>would )?use (?:an? )?(?<x>{TargetSpec.NameList}) trait Option cards?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"자신이 특징으로 {x}{J(x, "을", "를")} 가진 옵션 카드를 {(m.Groups["would"].Success ? "사용할" : "사용했을")} 때,";
        }
        if ((m = Regex.Match(ev, @"^an effect plays (?<t>one of your Digimon|a Digimon|an opponent's Digimon(?<tm> or Tamer)?|one of your opponent's Digimon)$", RegexOptions.IgnoreCase)).Success)
        {
            string t = m.Groups["t"].Value.ToLowerInvariant();
            string who = t.StartsWith("one of your digimon") ? "자신의 디지몬" : t.StartsWith("a digimon") ? "디지몬" : m.Groups["tm"].Success ? "상대의 디지몬 또는 테이머" : "상대의 디지몬";
            return $"효과로 {who}{J(who, "이", "가")} 등장했을 때,";
        }
        if ((m = Regex.Match(ev, @"^an effect suspends (?<t>one of your Digimon|this Digimon|(?:a|another) Digimon(?<tm> or Tamer)?)$", RegexOptions.IgnoreCase)).Success)
        {
            string t = m.Groups["t"].Value.ToLowerInvariant();
            string who = t.StartsWith("one of") ? "자신의 디지몬" : t.StartsWith("this") ? "이 디지몬" : (t.StartsWith("another") ? "다른 " : "") + (m.Groups["tm"].Success ? "디지몬 또는 테이머" : "디지몬");
            return $"{who}{J(who, "이", "가")} 효과로 레스트했을 때,";
        }
        if (Regex.IsMatch(ev, @"^any Digimon suspends?$", RegexOptions.IgnoreCase)) return "디지몬이 레스트했을 때,";
        // 진화 코스트 감소의 시점 (공식: "이 디지몬이 특징으로 「곤충형」을 가진 디지몬 카드로 진화할 때,")
        if ((m = Regex.Match(ev, @"^(?:(?<who>this Digimon|you)|(?:one|any) of your (?<spec>.*?Digimon(?: with .+?)?)|an? (?<spec>Digimon with .+?)) would digivolve(?: into (?<into>this card(?: in (?:the|your) hand)?|.+?))?$", RegexOptions.IgnoreCase)).Success)
        {
            string? who = m.Groups["who"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase) ? "이 디지몬이"
                : !m.Groups["spec"].Success || m.Groups["spec"].Value.Equals("Digimon", StringComparison.OrdinalIgnoreCase) ? "자신의 디지몬이"
                : TargetKo("1 of your " + m.Groups["spec"].Value) is { } spec ? Regex.Replace(spec, @" 1마리$", "") + "이" : null;
            if (who == null) return null;
            if (!m.Groups["into"].Success) return $"{who} 진화할 때,";
            if (m.Groups["into"].Value.StartsWith("this card", StringComparison.OrdinalIgnoreCase)) return $"{who} 패의 이 카드로 진화할 때,";
            if (PickKo("1 " + Regex.Replace(m.Groups["into"].Value, @"^(?:an?|1) ", "", RegexOptions.IgnoreCase)) is not { } ik) return EventKo32(ev);   // v0.32.0 "into a 2-color black Digimon"
            ik = Regex.Replace(ik, @" 1장(?= 또는 |$)", "");
            return $"{who} {ik}{J(ik, "으로", "로")} 진화할 때,";
        }
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<other>other )?Digimon (?:are|is) played or digivolves?$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 {(m.Groups["other"].Success ? "다른 " : "")}디지몬이 등장하거나 진화했을 때,";
        if (Regex.IsMatch(ev, @"^effects trash this card from (?:the|your) security stack$|^this card is trashed from (?:the|your) security stack by (?:an )?effects?$", RegexOptions.IgnoreCase)) return "효과로 이 카드가 시큐리티에서 파기되었을 때,";
        // 이름 주어: "When any of your [Suzune Kazuki]s are played" · "When any of your [Owen Dreadnought]s suspend"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<x>{TargetSpec.NameList})(?:s|'s)? (?<v>(?:is|are) played|suspends?|become suspended|is suspended|are suspended)$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"자신의 {x}{J(x, "이", "가")} {(m.Groups["v"].Value.Contains("played", StringComparison.OrdinalIgnoreCase) ? "등장했을" : "레스트했을")} 때,";
        }
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon suspends?$|^any of your Digimon suspend$", RegexOptions.IgnoreCase)) return "자신의 디지몬이 레스트 상태가 되었을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon (?:unsuspends|becomes unsuspended)$", RegexOptions.IgnoreCase)) return "이 디지몬이 액티브가 되었을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon (?:suspends|becomes suspended|is suspended)$", RegexOptions.IgnoreCase)) return "이 디지몬이 레스트 상태가 되었을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon (?:becomes unsuspended|unsuspends) during your main phase$", RegexOptions.IgnoreCase)) return "자신의 메인 페이즈에 이 디지몬이 액티브가 되었을 때,";
        if (Regex.IsMatch(ev, @"^this Digimon (?:becomes unsuspended|unsuspends) during your unsuspend phase$", RegexOptions.IgnoreCase)) return "자신의 액티브 페이즈에 이 디지몬이 액티브가 되었을 때,";
        if (Regex.IsMatch(ev, @"^this card is trashed (?:due to|by) activating this Digimon's ＜Digi-Burst＞$|^activating this Digimon's ＜Digi-Burst＞ trashes this card$", RegexOptions.IgnoreCase))
            return "이 디지몬의 《디지버스트》로 이 카드가 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon activates ＜Digi-Burst＞$", RegexOptions.IgnoreCase)) return "자신의 디지몬이 《디지버스트》를 발휘했을 때,";
        if (Regex.IsMatch(ev, @"^you use an Option card$", RegexOptions.IgnoreCase)) return "자신이 옵션 카드를 사용했을 때,";
        if (Regex.IsMatch(ev, @"^your opponent uses an Option card$", RegexOptions.IgnoreCase)) return "상대가 옵션 카드를 사용했을 때,";
        if (Regex.IsMatch(ev, @"^(?:a|any) player uses an Option card$", RegexOptions.IgnoreCase)) return "플레이어가 옵션 카드를 사용했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|your) opponent plays a Tamer$", RegexOptions.IgnoreCase)) return "상대가 테이머를 등장시켰을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Tamers? (?:becomes suspended|suspends|is suspended)$", RegexOptions.IgnoreCase)) return "상대의 테이머가 레스트 상태가 되었을 때,";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers? (?:becomes suspended|suspends|is suspended|become suspended)$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 {(m.Groups["color"].Success ? ColorsKo(m.Groups["color"].Value) + "의 " : "")}테이머가 레스트 상태가 되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon attacks? (?:a player|you)$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 플레이어에게 어택했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon attacks?$|^your opponent's Digimon attacks?$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 어택했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:becomes suspended|suspends|is suspended)$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 레스트 상태가 되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:is|are) deleted by (?:dropping|being reduced) to 0 DP$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 DP 0으로 소멸했을 때,";
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:is|are) deleted$|^your opponent's Digimon (?:is|are) deleted$", RegexOptions.IgnoreCase)) return "상대의 디지몬이 소멸했을 때,";
        if ((m = Regex.Match(ev, @"^(?:an|one of your) opponent's (?<spec>[^,]*?)Digimon (?:is|are) played$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your opponent's " + m.Groups["spec"].Value + "Digimon", bare: true) is { } op)
            return $"{op}{J(op, "이", "가")} 등장했을 때,";
        if (Regex.IsMatch(ev, @"^you play another Digimon with the same name as this Digimon$", RegexOptions.IgnoreCase)) return "자신이 이 디지몬과 같은 명칭의 다른 디지몬을 등장시켰을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) (?<what>attacks(?: a player| your opponent)?|suspends?|becomes suspended|(?:is|are) deleted|(?:is |are )?(?:played or digivolves?|played or deleted|played|digivolves?|digivolved))$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["subj"].Value, bare: true) is { } ally)
        {
            string w = m.Groups["what"].Value.ToLowerInvariant();
            string verb = w.StartsWith("attacks a player") || w.StartsWith("attacks your") ? "플레이어에게 어택했을 때," : w.StartsWith("attack") ? "어택했을 때," : w.Contains("suspend") ? "레스트 상태가 되었을 때,"
                : w.Contains("played") && w.Contains("deleted") ? "등장하거나 소멸했을 때," : w.Contains("deleted") ? "소멸했을 때," : w.Contains("played") && w.Contains("digivolv") ? "등장하거나 진화했을 때," : w.Contains("played") ? "등장했을 때," : "진화했을 때,";
            return $"{ally}{J(ally, "이", "가")} {verb}";
        }
        if ((m = Regex.Match(ev, @"^your opponent plays (?<subj>(?:an?|1) [^,]*?Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your opponent's " + Regex.Replace(m.Groups["subj"].Value, @"^(?:an?|1)\s+", "", RegexOptions.IgnoreCase), bare: true) is { } opPlay)
            return $"{opPlay}{J(opPlay, "이", "가")} 등장했을 때,";
        if ((m = Regex.Match(ev, @"^you play (?<subj>(?:an?|another|1) [^,]*?Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success)
        {
            string subj = Regex.Replace(m.Groups["subj"].Value, @"^(?:an?|1)\s+", "", RegexOptions.IgnoreCase);
            subj = Regex.Replace(subj, @"^another\s+", "other ", RegexOptions.IgnoreCase);
            if (TargetKo("all of your " + subj, bare: true) is { } pl)
                return $"{pl}{J(pl, "이", "가")} 등장했을 때,";
        }
        if ((m = Regex.Match(ev, @"^(?:another|a|an) (?<spec>(?![^,]*opponent)[^,]*?)Digimon (?:is|are) (?<what>played|deleted)$", RegexOptions.IgnoreCase)).Success)
        {
            string mods = TargetKo("all of your " + m.Groups["spec"].Value + "Digimon", bare: true) is { } t ? Regex.Replace(t, @"자신의 디지몬$", "").Trim() : "";
            string subj = $"{(ev.StartsWith("another", StringComparison.OrdinalIgnoreCase) ? "다른 " : "")}{(mods.Length > 0 ? mods + " " : "")}디지몬";
            return $"{subj}{J(subj, "이", "가")} {(m.Groups["what"].Value.Equals("played", StringComparison.OrdinalIgnoreCase) ? "등장했을 때," : "소멸했을 때,")}";
        }
        return EventKoMore(ev);
    }

    private const string SuspendVerbEn = @"(?:suspends?|becomes? suspended|(?:is|are) suspended|gets? suspended)";
    private const string UnsuspendVerbEn = @"(?:unsuspends?|becomes? unsuspended|(?:is|are) unsuspended)";

    /// <summary>"blue Tamer" · "Tamers with [Tai Kamiya] in their names" · "[ADVENTURE] trait Digimon or Tamers" → "블루인 자신의 테이머" 등 (자신의 카드 주어)</summary>
    private static string? AllyCardSubjectKo(string spec)
    {
        var m = Regex.Match(spec.Trim(), $@"^(?<other>other )?(?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?(?:(?<trait>{TargetSpec.NameList}) trait )?(?<kind>Digimon or Tamers?|Tamers?)(?<with> with .+)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var mods = new List<string>();
        if (m.Groups["with"].Success)
        {
            if (Regex.IsMatch(m.Groups["with"].Value, @"^ with inherited effects$", RegexOptions.IgnoreCase)) mods.Add("진화원 효과를 가진");
            else if (WithKo(m.Groups["with"].Value) is { } w) mods.Add(w);
            else return null;
        }
        if (m.Groups["trait"].Success) { string t = NamesKo(m.Groups["trait"].Value, trait: true); mods.Add($"특징으로 {t}{J(t, "을", "를")} 가진"); }
        if (m.Groups["color"].Success) mods.Add($"{ColorsKo(m.Groups["color"].Value)}인");
        string kind = m.Groups["kind"].Value.StartsWith("Digimon", StringComparison.OrdinalIgnoreCase) ? "디지몬 또는 테이머" : "테이머";
        return $"{string.Join(" ", mods)}{(mods.Count > 0 ? " " : "")}{(m.Groups["other"].Success ? "다른 " : "")}자신의 {kind}";
    }

    /// <summary>"a red or yellow Digimon" · "a Digimon with [Greymon] in its name" · "a [Puppet] trait Digimon" · "[Diaboromon]" → 진화한 카드 (공식: "레드/옐로인 디지몬으로 진화했을 때")</summary>
    private static string? EvoIntoKo(string into)
    {
        into = into.Trim();
        Match m;
        if ((m = Regex.Match(into, $@"^(?:an? )?(?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
            return NamesKo(m.Groups["x"].Value);
        if ((m = Regex.Match(into, $@"^an? (?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?(?:level (?<lv>\d+) )?(?:(?<trait>{TargetSpec.NameList})(?: trait)? )?Digimon(?<with> with .+)?$", RegexOptions.IgnoreCase)).Success)
        {
            var mods = new List<string>();
            if (m.Groups["with"].Success)
            {
                if (Regex.Match(m.Groups["with"].Value, @"^ with (?<n>\d+) or more colors$", RegexOptions.IgnoreCase) is { Success: true } mc) mods.Add($"{mc.Groups["n"].Value}색 이상의");
                else if (WithKo(m.Groups["with"].Value) is { } w) mods.Add(w);
                else return null;
            }
            if (m.Groups["trait"].Success) { string t = NamesKo(m.Groups["trait"].Value, trait: true); mods.Add($"특징으로 {t}{J(t, "을", "를")} 가진"); }
            if (m.Groups["color"].Success) mods.Add($"{ColorsKo(m.Groups["color"].Value)}인");
            if (m.Groups["lv"].Success) mods.Add($"Lv.{m.Groups["lv"].Value}의");
            return $"{string.Join(" ", mods)}{(mods.Count > 0 ? " " : "")}디지몬";
        }
        return null;
    }

    /// <summary>v0.27.0 사건 표기 (SentenceParser.ParseEventPhraseMore와 같은 범위) — 공식 한글 표기</summary>
    private static string? EventKoMore(string ev)
    {
        Match m;
        // 테이머 등장 — 공식 (BT11-023 · BT6-007 · BT7-070): "블루인 자신의 테이머가 등장했을 때," · "명칭에 「신태일」을 포함하는 자신의 테이머가 등장했을 때,"
        if (((m = Regex.Match(ev, @"^you play (?<a>a|an|another|1) (?<s>(?![^,]*Digimon)[^,]*?Tamer(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
             || (m = Regex.Match(ev, @"^(?:one of |any of )?your (?<a>other )?(?<s>(?![^,]*Digimon)[^,]*?Tamers?(?: with [^,]+?)?) (?:is|are) played$", RegexOptions.IgnoreCase)).Success)
            && AllyCardSubjectKo((Regex.IsMatch(m.Groups["a"].Value, "other", RegexOptions.IgnoreCase) ? "other " : "") + m.Groups["s"].Value) is { } tamer)
            return $"{tamer}{J(tamer, "이", "가")} 등장했을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<other>other )?(?<s>[^,]*?Digimon or Tamers?(?: with [^,]+?)?) (?:is |are )?(?<what>played or digivolves?|played)$", RegexOptions.IgnoreCase)).Success
            && AllyCardSubjectKo(m.Groups["other"].Value + m.Groups["s"].Value) is { } dt)
            return $"{dt}{J(dt, "이", "가")} {(m.Groups["what"].Value.Contains("digivolv", StringComparison.OrdinalIgnoreCase) ? "등장하거나 진화했을" : "등장했을")} 때,";
        // 공식 (BT11-076 · BT11-054): "자신의 디지몬이 효과로 등장했을 때," · "다른 자신의 디지몬이 효과로 등장했을 때,"
        if ((m = Regex.Match(ev, @"^you play (?<a>a|an|another) (?<subj>[^,]*?Digimon(?: with [^,]+?)?) (?:by|using|with) (?:an|one of your) effects?$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + (m.Groups["a"].Value.Equals("another", StringComparison.OrdinalIgnoreCase) ? "other " : "") + m.Groups["subj"].Value, bare: true) is { } eff)
            return $"{eff}{J(eff, "이", "가")} 효과로 등장했을 때,";
        // 공식 (EX3-047): "자신의 「쿠리하라 히나」가 등장했을 때,"
        if ((m = Regex.Match(ev, $@"^you play (?:an? |another )?(?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"자신의 {x}{J(x, "이", "가")} 등장했을 때,";
        }
        // 진화 — 공식 (BT16-084 · BT23-087): "자신의 디지몬이 레드/옐로인 디지몬으로 진화했을 때,"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) digivolves? into (?<into>.+)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["subj"].Value, bare: true) is { } evo && EvoIntoKo(m.Groups["into"].Value) is { } into)
            return $"{evo}{J(evo, "이", "가")} {into}{J(into, "으로", "로")} 진화했을 때,";
        // 레스트 — 공식 (BT9-056 · BT9-018 · EX4-037 · BT13-099)
        if ((m = Regex.Match(ev, $@"^(?:an|one of your|any of your) opponent's (?<spec>[^,]*?)Digimon(?<tam> or Tamers?)?(?<with> with [^,]+?)? {SuspendVerbEn}$", RegexOptions.IgnoreCase)).Success)
        {
            if (m.Groups["tam"].Success) return m.Groups["spec"].Value.Length == 0 && !m.Groups["with"].Success ? "상대의 디지몬 또는 테이머가 레스트했을 때," : null;
            return TargetKo("all of your opponent's " + m.Groups["spec"].Value + "Digimon" + m.Groups["with"].Value, bare: true) is { } os ? $"{os}{J(os, "이", "가")} 레스트했을 때," : null;
        }
        if (Regex.IsMatch(ev, $@"^(?:an|one of your|any of your) opponent's Tamers? {SuspendVerbEn}$", RegexOptions.IgnoreCase)) return "상대의 테이머가 레스트했을 때,";
        if ((m = Regex.Match(ev, $@"^(?<a>a|an|another|any) (?<spec>(?![^,]*opponent)[^,]*?)Digimon(?<tam> or Tamers?)? {SuspendVerbEn}$", RegexOptions.IgnoreCase)).Success)
        {
            string mods = TargetKo("all of your " + m.Groups["spec"].Value + "Digimon", bare: true) is { } t ? Regex.Replace(t, @"자신의 디지몬$", "").Trim() : "";
            string subj = $"{(m.Groups["a"].Value.Equals("another", StringComparison.OrdinalIgnoreCase) ? "다른 " : "")}{(mods.Length > 0 ? mods + " " : "")}디지몬{(m.Groups["tam"].Success ? " 또는 테이머" : "")}";
            return $"{subj}{J(subj, "이", "가")} 레스트했을 때,";
        }
        if ((m = Regex.Match(ev, @"^(?<who>another|this) Digimon (?:is|becomes) suspended by an effect$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["who"].Value.Equals("this", StringComparison.OrdinalIgnoreCase) ? "이 디지몬" : "다른 디지몬")}이 효과로 레스트했을 때,";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<subj>(?!opponent)(?:other )?[^,]*?Digimon(?: with [^,]+?)?) {SuspendVerbEn}$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["subj"].Value, bare: true) is { } sus)
            return $"{sus}{J(sus, "이", "가")} 레스트했을 때,";
        if ((m = Regex.Match(ev, $@"^this Digimon (?<with>with [^,]+?) {SuspendVerbEn}$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } sw)
            return $"{sw} 이 디지몬이 레스트했을 때,";
        // 액티브 — 공식 (BT15-005 · BT11-112 · EX3-001): "디지몬이 액티브가 되었을 때," · "블루인 자신의 디지몬이 액티브 상태가 되었을 때,"
        if ((m = Regex.Match(ev, $@"^this Digimon (?<with>with [^,]+?) {UnsuspendVerbEn}$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } uw)
            return $"{uw} 이 디지몬이 액티브가 되었을 때,";
        if ((m = Regex.Match(ev, $@"^(?<a>a|an|another|any) (?<spec>(?![^,]*opponent)[^,]*?)Digimon {UnsuspendVerbEn}$", RegexOptions.IgnoreCase)).Success)
        {
            string mods = TargetKo("all of your " + m.Groups["spec"].Value + "Digimon", bare: true) is { } t ? Regex.Replace(t, @"자신의 디지몬$", "").Trim() : "";
            string subj = $"{(m.Groups["a"].Value.Equals("another", StringComparison.OrdinalIgnoreCase) ? "다른 " : "")}{(mods.Length > 0 ? mods + " " : "")}디지몬";
            return $"{subj}{J(subj, "이", "가")} 액티브가 되었을 때,";
        }
        if ((m = Regex.Match(ev, $@"^(?:an|one of your|any of your) opponent's (?<spec>[^,]*?)Digimon {UnsuspendVerbEn}$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your opponent's " + m.Groups["spec"].Value + "Digimon", bare: true) is { } ou)
            return $"{ou}{J(ou, "이", "가")} 액티브가 되었을 때,";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<subj>(?!opponent)(?:other )?[^,]*?Digimon(?: with [^,]+?)?) {UnsuspendVerbEn}$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["subj"].Value, bare: true) is { } un)
            return $"{un}{J(un, "이", "가")} 액티브 상태가 되었을 때,";
        // 다른 디지몬 소멸 · 어택 — 공식 (EX8-064 · P-111 · BT18-042): "다른 디지몬이 소멸했을 때," · "다른 디지몬이 어택했을 때," · "디지몬이 어택했을 때,"
        if ((m = Regex.Match(ev, @"^other Digimon (?:is|are) deleted(?<battle> in battle)?$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["battle"].Success ? "다른 디지몬이 배틀에서 소멸했을 때," : "다른 디지몬이 소멸했을 때,";
        if ((m = Regex.Match(ev, @"^(?<a>a|an|another|any) (?<spec>(?![^,]*opponent)[^,]*?)Digimon(?<with> with [^,]+?)? attacks?$", RegexOptions.IgnoreCase)).Success)
        {
            string mods = TargetKo("all of your " + m.Groups["spec"].Value + "Digimon" + m.Groups["with"].Value, bare: true) is { } t ? Regex.Replace(t, @"자신의 디지몬$", "").Trim() : "";
            string subj = $"{(m.Groups["a"].Value.Equals("another", StringComparison.OrdinalIgnoreCase) ? "다른 " : "")}{(mods.Length > 0 ? mods + " " : "")}디지몬";
            return $"{subj}{J(subj, "이", "가")} 어택했을 때,";
        }
        // 배틀에서 소멸시켰을 때 — 공식 (EX1-041 · BT12-055): "~ 자신의 디지몬이 배틀에서 상대 디지몬만을 소멸시켰을 때," · "~ 배틀에서 상대의 디지몬을 소멸시켰을 때,"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?!opponent)(?:other )?[^,]*?Digimon(?: with [^,]+?)?) deletes? (?:an|1 of your|your|one of your) opponent's Digimon (?:in|by) battle(?<surv> and survives(?: the battle)?)?$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + Regex.Replace(m.Groups["subj"].Value, @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase), bare: true) is { } win)
            return $"{win}{J(win, "이", "가")} 배틀에서 {(m.Groups["surv"].Success ? "상대 디지몬만을" : "상대의 디지몬을")} 소멸시켰을 때,";
        return SourceEventKo(ev);
    }

    /// <summary>
    /// 진화원 시점 (v0.28.0 — SentenceParser.ParseSourceEvent와 같은 범위) — 공식 표기:
    /// "이 카드가 효과로 진화원에서 파기되었을 때," (BT10-072) · "이 카드가 특징 「광물형」/「광석형」을 가진 디지몬의 진화원에서 효과로 파기되었을 때," (EX10-025) ·
    /// "상대 디지몬의 진화원을 파기했을 때," (BT2-085) · "이 디지몬의 진화원이 효과로 늘어났을 때," (BT8-005) · "자신의 효과로 이 디지몬의 진화원이 늘어났을 때," (BT7-005) ·
    /// "이 디지몬의 진화원에 테이머 카드가 효과로 놓였을 때," (BT17-090) · "이 디지몬의 진화원에 테이머 카드가 놓였을 때," (BT20-071)
    /// </summary>
    private static string? SourceEventKo(string ev)
    {
        Match m;
        string? HostKo(string spec, bool mine)
        {
            string sp = Regex.Replace(spec.Trim(), @"^(?:an?|any of your|one of your|1 of your|your)\s+", "", RegexOptions.IgnoreCase);
            sp = Regex.Replace(sp, @"\]s\b", "]");
            sp = Regex.Replace(sp, @"\bin (?:their|its) (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase);
            sp = Regex.Replace(sp, @"\bin any of their traits\b", "in any of its traits", RegexOptions.IgnoreCase);
            if (TargetKo("all of your " + sp, bare: true) is not { } t) return null;
            return mine ? t : Regex.Replace(t, @"자신의 디지몬$", "디지몬");
        }
        string? CardKo(string what)
        {
            what = what.Trim();
            if (Regex.IsMatch(what, @"^(?:an? |any )?digivolution cards?$", RegexOptions.IgnoreCase)) return "";
            string single = Regex.Replace(what, @"^(?:an?|any) ", "", RegexOptions.IgnoreCase);
            single = Regex.Replace(single, @"\bcards\b", "card", RegexOptions.IgnoreCase);
            single = Regex.Replace(single, @"\bin any of their traits\b", "in any of its traits", RegexOptions.IgnoreCase);
            return PickKo("1 " + single) is { } k ? Regex.Replace(k, @"\s*1장$", "") : null;
        }

        if ((m = Regex.Match(ev, @"^(?:this card is trashed from (?:(?<h>[^,]+?)'s )?digivolution cards by (?:an effect|effects)|(?<v>an effect trashes|effects trash) this (?:card from (?:(?<h>[^,]+?)'s )?digivolution cards(?: of (?<h2>[^,]+?))?|(?<dc>digivolution card)))$", RegexOptions.IgnoreCase)).Success)
        {
            string h = m.Groups["h"].Success ? m.Groups["h"].Value : m.Groups["h2"].Value;
            if (m.Groups["dc"].Success || Regex.IsMatch(h, @"^your Digimon$", RegexOptions.IgnoreCase)) return "이 카드가 진화원에서 효과로 파기되었을 때,";
            if (h.Length == 0)
                return m.Groups["v"].Value.Equals("effects trash", StringComparison.OrdinalIgnoreCase) ? "이 카드가 디지몬의 진화원에서 효과로 파기되었을 때," : "이 카드가 효과로 진화원에서 파기되었을 때,";
            return HostKo(h, mine: false) is { } hk ? $"이 카드가 {hk}의 진화원에서 효과로 파기되었을 때," : null;
        }
        if (Regex.IsMatch(ev, @"^one of your opponent's digivolution cards is trashed$", RegexOptions.IgnoreCase))
            return "상대 디지몬의 진화원을 파기했을 때,";
        if (Regex.IsMatch(ev, @"^an opponent's Digimon's digivolution cards? (?:is|are) trashed$", RegexOptions.IgnoreCase))
            return "상대의 디지몬의 진화원이 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:an effect trashes|effects trash) (?:a )?digivolution cards? (?:from|of) (?:your opponent's|an opponent's) Digimon$", RegexOptions.IgnoreCase))
            return "상대 디지몬의 진화원이 효과로 파기되었을 때,";
        if ((m = Regex.Match(ev, @"^(?:an effect trashes|effects trash) (?:(?:any of|one of) (?:(?<self>this Digimon)|(?<h>your [^,]*?Digimon))'s digivolution cards|digivolution cards (?:of|from) (?:(?<self>this Digimon)|(?<h>(?:any of |one of )?your [^,]*?Digimon)))$", RegexOptions.IgnoreCase)).Success)
        {
            if (m.Groups["self"].Success) return "이 디지몬의 진화원이 효과로 파기되었을 때,";
            return HostKo(m.Groups["h"].Value, mine: true) is { } hk ? $"{hk}의 진화원이 효과로 파기되었을 때," : null;
        }
        const string Placer = @"(?:(?<mine>one of your effects) places?|(?:an effect|effects) (?:places?|adds?))";
        bool byEffect = true;
        if ((m = Regex.Match(ev, $@"^{Placer} (?:(?<what>[^,]+?) (?:under|to) (?:(?<self>this Digimon)|(?<h>(?:one of |any of )?your [^,]*?Digimon(?: with [^,]+?)?))|(?<what>[^,]+?) (?:in|as) (?:(?<self>this Digimon)|(?<h>(?:one of |any of )?your [^,]*?Digimon(?: with [^,]+?)?))'s digivolution cards|to (?:(?<self>this Digimon)|(?<h>(?:one of |any of )?your [^,]*?Digimon))'s digivolution cards)$", RegexOptions.IgnoreCase)).Success
            || ((m = Regex.Match(ev, @"^(?<what>[^,]+?) (?:is|are) placed (?:in|under) (?:(?<self>this Digimon)'s digivolution cards|(?<h>(?:one of |any of )?your [^,]*?Digimon)'s digivolution cards)$", RegexOptions.IgnoreCase)).Success && !(byEffect = false)))
        {
            string? host = m.Groups["self"].Success ? "이 디지몬" : HostKo(m.Groups["h"].Value, mine: true);
            string? card = m.Groups["what"].Success ? CardKo(m.Groups["what"].Value) : "";
            if (host == null || card == null) return null;
            if (card.Length == 0)
                return m.Groups["mine"].Success ? $"자신의 효과로 {host}의 진화원이 늘어났을 때," : $"{host}의 진화원이 효과로 늘어났을 때,";
            return $"{(m.Groups["mine"].Success ? "자신의 효과로 " : "")}{host}의 진화원에 {card}{J(card, "이", "가")} {(byEffect && !m.Groups["mine"].Success ? "효과로 " : "")}놓였을 때,";
        }
        return EventKo31(ev) ?? EventKo32(ev) ?? EventKo33(ev);   // v0.31.0 (KoreanEffectRenderer.Batch31.cs) · v0.32.0 (Batch32)
    }

    /// <summary>벗어날 때 · 소멸할 때의 단서 → "자신의 효과 이외로 " · "배틀 이외로 " · "배틀에서 " · "상대의 효과로 " (빈 단서 = "", 모르면 null)</summary>
    private static string? LeaveQualifierKo(string q)
    {
        q = q.Trim();
        if (q.Length == 0) return "";
        if (Regex.IsMatch(q, @"^other than (?:by )?(?:your (?:own )?effects|one of your effects) (?:or|and) (?:a |in |by )battles?$|^other than (?:in|by) battle (?:or|and) (?:by )?your (?:own )?effects$", RegexOptions.IgnoreCase)) return "자신의 효과와 배틀 이외로 ";
        if (Regex.IsMatch(q, @"^other than (?:by )?(?:your (?:own )?effects?|one of your effects)$", RegexOptions.IgnoreCase)) return "자신의 효과 이외로 ";
        if (Regex.IsMatch(q, @"^other than (?:in|by) battle$", RegexOptions.IgnoreCase)) return "배틀 이외로 ";
        if (Regex.IsMatch(q, @"^other than by DigiXros$", RegexOptions.IgnoreCase)) return "디지크로스 이외로 ";
        if (Regex.IsMatch(q, @"^in battle$", RegexOptions.IgnoreCase)) return "배틀에서 ";
        if (Regex.IsMatch(q, @"^by (?:an |your )?opponent's effects?$|^by your opponent's effects?$|^by effects of your opponent$", RegexOptions.IgnoreCase)) return "상대의 효과로 ";
        if (Regex.IsMatch(q, @"^by (?:an )?effects?$", RegexOptions.IgnoreCase)) return "효과로 ";
        return null;
    }

    /// <summary>"with [X] in its name" · "with the [X] trait" · "with [X] in its text" → 디지몬 앞의 꾸밈말 (모르면 null)</summary>
    private static string? WithKo(string with)
    {
        Match m;
        with = with.Trim();
        if ((m = Regex.Match(with, $@"^with (?<x>{TargetSpec.NameList}) in (?:its|their) names?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"명칭에 {x}{J(x, "을", "를")} 포함하는"; }
        if ((m = Regex.Match(with, $@"^with (?:the )?(?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return $"특징으로 {x}{J(x, "을", "를")} 가진"; }
        if ((m = Regex.Match(with, $@"^with (?<x>{TargetSpec.NameList}) in (?:its|their) texts?$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"{x}{J(x, "이", "가")} 기술되어 있는"; }
        // v0.31.0 — 공식 (BT13-100): "특징으로 「식물형」/「요정형」을 포함하는"
        if ((m = Regex.Match(with, $@"^with (?<x>{TargetSpec.NameList}) in any of (?:its|their) traits$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value, trait: true); return $"특징으로 {x}{J(x, "을", "를")} 포함하는"; }
        return null;
    }

    /// <summary>"for each ~" 셈 → "~마다"</summary>
    private static string? CounterKo(string counter)
    {
        counter = counter.Trim().TrimEnd('.');
        Match m;
        if (CounterKo31(counter) is { } counter31) return counter31;   // v0.31.0
        string Per(Match x) => x.Groups["n"].Success ? x.Groups["n"].Value : "1";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:security cards? you have|cards? in your security stack|of your security cards)$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 시큐리티 {Per(m)}장마다";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:security cards? your opponent has|cards? in your opponent's security stack)$", RegexOptions.IgnoreCase)).Success)
            return $"상대의 시큐리티 {Per(m)}장마다";
        if (Regex.IsMatch(counter, @"^(?:of your opponent's suspended Digimon|suspended Digimon your opponent has)$", RegexOptions.IgnoreCase))
            return "레스트 상태인 상대의 디지몬 1마리마다";
        if (Regex.IsMatch(counter, @"^(?:of your opponent's Digimon|Digimon your opponent has)(?: in play)?$", RegexOptions.IgnoreCase))
            return "상대의 디지몬 1마리마다";
        if (Regex.IsMatch(counter, @"^(?:of your opponent's Digimon with no digivolution cards|Digimon with no digivolution cards your opponent has)$", RegexOptions.IgnoreCase))
            return "진화원을 갖지 않은 상대의 디지몬 1마리마다";
        if ((m = Regex.Match(counter, $@"^(?:of your (?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers|(?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers? you have(?: in play)?)$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 {(m.Groups["color"].Success ? ColorsKo(m.Groups["color"].Value) + "의 " : "")}테이머 1장마다";
        if (Regex.IsMatch(counter, @"^of (?:its|their|that Digimon's) face-down digivolution cards$", RegexOptions.IgnoreCase))
            return "그 디지몬의 뒷면의 진화원 1장마다";
        if ((m = Regex.Match(counter, @"^(?:of your (?<other>other )?(?<sus>suspended )?Digimon|(?<other>other )?(?<sus>suspended )?Digimon you have)(?: in play)?$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["sus"].Success ? "레스트 상태인 " : "")}자신의 {(m.Groups["other"].Success ? "다른 " : "")}디지몬 1마리마다";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?cards? in (?<who>your|your opponent's) (?<z>trash|hand)$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["who"].Value.Contains("opponent") ? "상대" : "자신")}의 {(m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase) ? "패" : "트래시")} {Per(m)}장마다";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:cards? in this Digimon's digivolution cards|of this Digimon's digivolution cards|digivolution cards? (?:this Digimon has|under this Digimon))$", RegexOptions.IgnoreCase)).Success)
            return $"이 디지몬의 진화원 {Per(m)}장마다";
        // "for each of your opponent's Digimon with ＜Security A.＞" → 《시큐리티 어택》을 가진 상대의 디지몬 1마리마다
        if ((m = Regex.Match(counter, @"^(?:of your opponent's Digimon|Digimon your opponent has) with (?<k>＜[^＞]+＞)$", RegexOptions.IgnoreCase)).Success
            && KeywordKo(m.Groups["k"].Value.Trim('＜', '＞'), withReminder: false) is { } withKw)
            return $"{withKw}{J(withKw, "을", "를")} 가진 상대의 디지몬 1마리마다";
        if (Regex.IsMatch(counter, @"^Digimon deleted$", RegexOptions.IgnoreCase))
            return "소멸한 디지몬 1마리마다";
        // v0.30.0 — 공식 (BT19-052 · BT21-102 · ST21-06 · BT21-079)
        if (Regex.IsMatch(counter, @"^(?:of your face[- ]up security cards|face[- ]up security cards? you have)$", RegexOptions.IgnoreCase))
            return "자신의 앞면의 시큐리티 1장마다";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) colors? your Tamers have|of your Tamers' colors|color your Tamers have)$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 테이머의 색 {Per(m)}색마다";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:total )?cards? in both players' trash(?:es)?$", RegexOptions.IgnoreCase)).Success)
            return $"서로의 트래시 합계 {Per(m)}장마다";
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:total )?cards? in both players' security stacks$", RegexOptions.IgnoreCase)).Success)
            return $"서로의 시큐리티 {Per(m)}장마다";
        if ((m = Regex.Match(counter, $@"^(?<x>{TargetSpec.NameList}) in this Digimon's digivolution cards$", RegexOptions.IgnoreCase)).Success)
            return $"이 디지몬의 진화원의 {NamesKo(m.Groups["x"].Value)} 1장마다";
        return CounterKo32(counter) ?? CounterKo34(counter);   // v0.32.0 · v0.34.0
    }

    // =====================================================================
    // 대상 · 카드 고르기
    // =====================================================================

    private const string Colors = "red|blue|yellow|green|black|purple|white";

    // 절 안에서 앞에 대상(상대의 디지몬 1마리 …)이 나왔는지 — "it"을 "그 디지몬" / "이 디지몬" 중 무엇으로 쓸지
    [ThreadStatic] private static bool _mentionedTarget;

    private static readonly Regex Head = new(
        @"^(?:(?<count>\d+|all|up to \d+) of )?(?<side>your opponent's|your|their|its owner's)\s+(?<other>other\s+)?(?<adj>(?:(?:unsuspended|suspended|(?:lowest|highest) (?:level|DP|play cost)|level \d+(?: or (?:lower|higher|more|less))?|\[[^\]]+\](?: trait)?|(?:red|blue|yellow|green|black|purple|white)(?: (?:or|and/or) (?:red|blue|yellow|green|black|purple|white))*)\s+)*)(?<kind>Digimon or Tamers|Digimon|Security Digimon|Tamers?)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 대상 구절 → 한글 ("1 of your opponent's Digimon with 4000 DP or less" → "DP 4000 이하인 상대의 디지몬 1마리").
    /// bare = 수 없이 ("자신의 디지몬이 있을 때" 같은 조건 · 사건용)
    /// </summary>
    /// <summary>대상 구절의 한글 ("1 of your opponent's suspended Digimon" → 상대의 레스트 상태 디지몬 1마리) — 옵션 대상 안내(MainTargetRule)용</summary>
    internal static string? TargetPhraseKo(string phrase) => TargetKo(phrase);

    private static string? TargetKo(string phrase, bool bare = false)
    {
        phrase = phrase.Trim().TrimEnd('.').Trim();
        if (Regex.Match(phrase, @"^(?<pre>(?:(?:\d+|all|up to \d+|any) of )?(?<side>your|their|your opponent's) )Tokens? or (?<other>other )?(?<body>.*\bDigimon\b.*)$", RegexOptions.IgnoreCase) is { Success: true } tok)
        {
            string tokPre = Regex.Replace(tok.Groups["pre"].Value, @"^any of ", "all of ", RegexOptions.IgnoreCase);
            if (TargetKo(tokPre + tok.Groups["other"].Value + tok.Groups["body"].Value, bare) is not { } innerKo) return null;
            string sideKo = tok.Groups["side"].Value.ToLowerInvariant() is "your" ? "자신" : "상대";
            // "~ 자신의 다른 디지몬 1마리" → "~ 다른 자신의 디지몬 1마리" (공식 어순), 앞에 "자신의 토큰 또는"
            innerKo = Regex.Replace(innerKo, @"(자신|상대)의 (다른 )?디지몬", "$2$1의 디지몬");
            return $"{sideKo}의 토큰 또는 {innerKo}";
        }
        phrase = TargetSpec.NormalizePreNoun(phrase);
        if (Regex.IsMatch(phrase, @"^this Digimon$", RegexOptions.IgnoreCase)) return "이 디지몬";
        if (Regex.IsMatch(phrase, @"^this Tamer$", RegexOptions.IgnoreCase)) return "이 테이머";
        if (Regex.IsMatch(phrase, @"^this card$", RegexOptions.IgnoreCase)) return "이 디지몬";
        if (Regex.IsMatch(phrase, @"^(?:it|they|them)$", RegexOptions.IgnoreCase)) return _mentionedTarget ? "그 디지몬" : "이 디지몬";   // 앞에 대상이 없으면 이 디지몬
        if (Regex.IsMatch(phrase, @"^(?:that Digimon|those Digimon|that card|the Digimon)$", RegexOptions.IgnoreCase)) return "그 디지몬";
        // "1 Digimon" · "up to 2 level 4 or lower Digimon" — 주인 없는 대상(양쪽) = "디지몬 1마리" (공식 EX6-023: "디지몬 1마리에게")
        if (Regex.Match(phrase, @"^(?<count>\d+|up to \d+) (?!of )(?<body>(?:other )?(?:[^,]*? )?Digimon\b.*)$", RegexOptions.IgnoreCase) is { Success: true } any
            && TargetKo($"{any.Groups["count"].Value} of your {any.Groups["body"].Value}", bare) is { } anyKo)
            return Regex.Replace(anyKo, @"자신의 (?=(?:다른 )?디지몬)", "");
        var m = Head.Match(phrase);
        if (!m.Success) return null;
        if (!bare) _mentionedTarget = true;
        var mods = new List<string>();
        if (FiltersKo(m.Groups["rest"].Value) is not { } filters) return null;
        mods.AddRange(filters);
        var colorMods = new List<string>();
        foreach (Match a in Regex.Matches(m.Groups["adj"].Value, @"(?<sel>(?:lowest|highest) (?:level|DP|play cost))|(?<!(?:lowest|highest) )(?<sus>unsuspended|suspended)|level (?<lv>\d+)(?: or (?<dir>lower|higher|more|less))?|\[(?<x>[^\]]+)\](?<trait> trait)?|(?<colors>(?:red|blue|yellow|green|black|purple|white)(?: (?:or|and/or) (?:red|blue|yellow|green|black|purple|white))*)", RegexOptions.IgnoreCase))
        {
            if (a.Groups["sel"].Success)
            {
                string sel = a.Groups["sel"].Value.ToLowerInvariant();
                string what = sel.EndsWith("level") ? "Lv.이" : sel.EndsWith("dp") ? "DP가" : "등장 코스트가";
                mods.Add($"가장 {what} {(sel.StartsWith("lowest") ? "낮은" : "높은")}");
            }
            else if (a.Groups["sus"].Success) mods.Add(a.Groups["sus"].Value.StartsWith("un", StringComparison.OrdinalIgnoreCase) ? "액티브 상태인" : "레스트 상태인");
            else if (a.Groups["lv"].Success) mods.Add($"Lv.{a.Groups["lv"].Value}{(a.Groups["dir"].Success ? " " + Dir(a.Groups["dir"].Value) : "")}인");
            else if (a.Groups["x"].Success) mods.Add(a.Groups["trait"].Success ? $"특징으로 「{TraitKo(a.Groups["x"].Value)}」{J(TraitKo(a.Groups["x"].Value), "을", "를")} 가진" : $"「{NameKo(a.Groups["x"].Value)}」인");
            else if (a.Groups["colors"].Success) colorMods.Add(ColorsKo(a.Groups["colors"].Value) + "인");
        }
        string side = m.Groups["side"].Value.ToLowerInvariant() switch { "your opponent's" or "their" => "상대의", _ => "자신의" };
        string kind = m.Groups["kind"].Value.ToLowerInvariant() switch
        {
            "digimon or tamers" => "디지몬 또는 테이머", "security digimon" => "시큐리티 디지몬", "tamer" or "tamers" => "테이머", _ => "디지몬",
        };
        string other = m.Groups["other"].Success ? "다른 " : "";
        string count = m.Groups["count"].Value.ToLowerInvariant();
        string unit = kind == "테이머" ? "장" : "마리";
        string countKo = bare ? "" : count.Length == 0 || count == "all" ? " 전부" : count.StartsWith("up to") ? $" {Regex.Match(count, @"\d+").Value}{unit}까지" : $" {count}{unit}";
        string pre = string.Join(" ", mods.Concat(colorMods));
        return $"{(pre.Length > 0 ? pre + " " : "")}{side} {other}{kind}{countKo}";
    }

    /// <summary>"with 4000 DP or less" · "with no digivolution cards" · "with [X] in its name" … → 앞에 붙는 꾸밈말들</summary>
    private static List<string>? FiltersKo(string rest)
    {
        var list = new List<string>();
        rest = rest.Trim().TrimEnd(',').Trim();
        while (rest.Length > 0)
        {
            Match m;
            // "or"로 이어진 조건 — 공식: "명칭에 「황제드라몬」을 포함하거나 특징으로 「프리」를 가진" (앞 조건을 "~거나"로)
            if (list.Count > 0 && Regex.IsMatch(rest, @"^or\s", RegexOptions.IgnoreCase))
            {
                string prev = list[^1];
                list[^1] = prev.EndsWith("포함하는") ? prev[..^2] + "하거나" : prev.EndsWith("가진") ? prev[..^2] + "가지거나"
                    : prev.EndsWith("제외)하는") ? prev[..^2] + "하거나"
                    : prev.EndsWith("있는") ? prev[..^2] + "있거나" : prev.EndsWith("인") ? prev[..^1] + "이거나" : prev;
            }
            if ((m = Regex.Match(rest, @"^(?:with |that has |that have |and |or )?(?:(?:a|an) )?(?<n>\d+) DP or (?<dir>less|more)", RegexOptions.IgnoreCase)).Success)
                list.Add($"DP {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}인");
            // 공식: "그 디지몬의 DP 이하의 상대의 디지몬 1마리"
            else if ((m = Regex.Match(rest, @"^with as much or less DP (?:as|than) (?<who>that|this) Digimon", RegexOptions.IgnoreCase)).Success)
                list.Add($"{(m.Groups["who"].Value.Equals("this", StringComparison.OrdinalIgnoreCase) ? "이" : "그")} 디지몬의 DP 이하의");
            else if ((m = Regex.Match(rest, @"^(?:with |and )?(?:a |the )?play costs? of (?<n>\d+) or (?<dir>less|more)", RegexOptions.IgnoreCase)).Success)
                list.Add($"등장 코스트 {m.Groups["n"].Value} {Dir(m.Groups["dir"].Value)}인");
            else if ((m = Regex.Match(rest, @"^(?:with |and )?the (?<which>highest|lowest) (?<what>DP|level|play cost)", RegexOptions.IgnoreCase)).Success)
            {
                string what = m.Groups["what"].Value.ToLowerInvariant() switch { "dp" => "DP가", "level" => "Lv.이", _ => "등장 코스트가" };
                list.Add($"가장 {what} {(m.Groups["which"].Value.Equals("lowest", StringComparison.OrdinalIgnoreCase) ? "낮은" : "높은")}");
            }
            else if ((m = Regex.Match(rest, @"^(?:with |and )?the (?<which>fewest|most) digivolution cards", RegexOptions.IgnoreCase)).Success)
                list.Add($"진화원이 가장 {(m.Groups["which"].Value.Equals("fewest", StringComparison.OrdinalIgnoreCase) ? "적은" : "많은")}");
            else if ((m = Regex.Match(rest, @"^(?:with no|without|that has no) digivolution cards", RegexOptions.IgnoreCase)).Success)
                list.Add("진화원을 갖지 않은");
            else if ((m = Regex.Match(rest, @"^(?:with |and )?(?<n>\d+) or (?<dir>more|fewer|less) digivolution cards", RegexOptions.IgnoreCase)).Success)
                list.Add($"진화원이 {m.Groups["n"].Value}장 {Dir(m.Groups["dir"].Value)}인");
            // v0.31.0 — 공식 (P-062 · BT8-085): "진화원에 「감마몬」을 가진" · "2색 이상인"
            else if ((m = Regex.Match(rest, $@"^(?:with |and |or )?(?<x>{TargetSpec.NameList}) in (?:its|their) digivolution cards", RegexOptions.IgnoreCase)).Success)
            { string x = NamesKo(m.Groups["x"].Value); list.Add($"진화원에 {x}{J(x, "을", "를")} 가진"); }
            else if ((m = Regex.Match(rest, @"^with (?<n>\d+) or more colors", RegexOptions.IgnoreCase)).Success)
                list.Add($"{m.Groups["n"].Value}색 이상인");
            else if ((m = Regex.Match(rest, $@"^(?:with |and |or )?(?<x>{TargetSpec.NameList}) in (?:its|their) names?", RegexOptions.IgnoreCase)).Success)
            { string x = NamesKo(m.Groups["x"].Value); list.Add($"명칭에 {x}{J(x, "을", "를")} 포함하는"); }
            // v0.33.0 — 공식 (BT13-048 · BT23-078 · EX4-064): "특징으로 「수」/「짐승」을 포함(「수생형」은 제외)하거나" · "~ 또는 특징으로 「조」/「새」를 포함하는"
            else if ((m = Regex.Match(rest, $@"^or (?<x>{TargetSpec.NameList}) in any of (?:its|their) traits(?: other than (?<ex>{TargetSpec.NameList}))?|^with (?<x>{TargetSpec.NameList}) in any of (?:its|their) traits other than (?<ex>{TargetSpec.NameList})", RegexOptions.IgnoreCase)).Success)
            {
                string x = NamesKo(m.Groups["x"].Value, trait: true);
                string ex = m.Groups["ex"].Success ? $"({NamesKo(m.Groups["ex"].Value, trait: true)}은 제외)" : "";
                list.Add($"특징으로 {x}{J(x, "을", "를")} 포함{ex}하는");
            }
            else if ((m = Regex.Match(rest, $@"^(?:with |and |or )?(?:the )?(?<x>{TargetSpec.NameList}) traits?|^with (?<x>{TargetSpec.NameList}) in any of its traits", RegexOptions.IgnoreCase)).Success)
            { string x = NamesKo(m.Groups["x"].Value, trait: true); list.Add($"특징으로 {x}{J(x, "을", "를")} 가진"); }
            // v0.33.0 — 공식 (BT17-090): "진화원에 테이머 카드가 있는"
            else if ((m = Regex.Match(rest, @"^(?:with |that has |and )?(?:a|an) (?<kind>Tamer|Option|Digimon) card in its digivolution cards", RegexOptions.IgnoreCase)).Success)
                list.Add($"진화원에 {(m.Groups["kind"].Value.ToLowerInvariant() switch { "tamer" => "테이머", "option" => "옵션", _ => "디지몬" })} 카드가 있는");
            else if ((m = Regex.Match(rest, $@"^(?:with |and |or )?(?<x>{TargetSpec.NameList}) in (?:its|their) texts?", RegexOptions.IgnoreCase)).Success)
            { string x = NamesKo(m.Groups["x"].Value); list.Add($"{x}{J(x, "이", "가")} 기술되어 있는"); }
            else if ((m = Regex.Match(rest, @"^(?<neg>with|without|that (?:has|have)|that (?:doesn't|don't) have) ＜(?<k>[^＞]+?)\s*＞(?:\s*or\s*＜(?<k2>[^＞]+?)\s*＞)?", RegexOptions.IgnoreCase)).Success)
            {
                var ks = new[] { m.Groups["k"].Value, m.Groups["k2"].Value }.Where(k => k.Length > 0).Select(k => KeywordKo(k, false)).ToList();
                if (ks.Any(k => k == null)) return null;
                string kw = string.Join(" 또는 ", ks);
                bool without = m.Groups["neg"].Value.StartsWith("without", StringComparison.OrdinalIgnoreCase) || m.Groups["neg"].Value.Contains("n't");
                list.Add($"{kw}{J(kw, "을", "를")} {(without ? "갖지 않은" : "가진")}");
            }
            else if ((m = Regex.Match(rest, @"^with an? \[(?<t>On Play|On Deletion|When Digivolving|When Attacking|Security|Main)\] effect", RegexOptions.IgnoreCase)).Success)
                list.Add($"{LabelKo(m.Groups["t"].Value)} 효과를 가진");
            else if ((m = Regex.Match(rest, @"^with (?:(?:as much or less|less than or equal to) (?:DP )?(?:as|to) this Digimon(?:'s DP)?|DP less than or equal to this Digimon's DP)", RegexOptions.IgnoreCase)).Success)
                list.Add("이 디지몬의 DP 이하인");
            else if ((m = Regex.Match(rest, @"^with (?:a )?lower (?<w>DP|level) than this Digimon", RegexOptions.IgnoreCase)).Success)
                list.Add($"이 디지몬보다 {(m.Groups["w"].Value.Equals("DP", StringComparison.OrdinalIgnoreCase) ? "DP가" : "Lv.이")} 낮은");
            else if (FilterKo34(rest) is { } f34) { m = f34.M; list.Add(f34.Ko); }   // v0.34.0 (KoreanEffectRenderer.Batch34.cs)
            else return null;
            rest = rest[m.Length..].Trim().TrimStart(',').Trim();
        }
        return list;
    }

    /// <summary>
    /// 카드 고르기 구절 → 한글 ("1 card with [Agumon] in its name" → "명칭에 「아구몬」을 포함하는 카드 1장",
    /// "1 level 4 or lower green Digimon card" → "Lv.4 이하인 그린의 디지몬 카드 1장")
    /// </summary>
    /// <summary>
    /// 카드 고르기 → 한글. v0.28.0: SentenceParser.ParseCardPick과 같은 다른 표기 — "1 yellow" · "1 [Angel] trait"(뒤의 card 생략) ·
    /// "1 card with the [Hybrid] trait, or [Susanoomon] or [Takuya Kanbara]" (공식 BT7-008: "특징으로 「하이브리드체」를 갖는 카드 또는 「스사노오몬」 또는 「우정훈」 1장")
    /// </summary>
    private static string? PickKo(string part)
    {
        if (PickKoCore(part) is { } ko) return ko;
        string p = part.Trim();
        var lead = Regex.Match(p, @"^(?:up to )?(?:\d+|a|an) ");
        if (!lead.Success) return null;
        if (Regex.IsMatch(p, $@"(?:\btraits?|\b(?:{Colors}))$", RegexOptions.IgnoreCase) && PickKoCore(p + " card") is { } withCard) return withCard;
        foreach (Match or in Regex.Matches(p, @",?\s+or\s+(?=(?:(?:\d+|a|an) )?(?:card\b|\[|\d+-colou?r|(?:" + Colors + @")\b))", RegexOptions.IgnoreCase))
        {
            string left = p[..or.Index], right = p[(or.Index + or.Length)..];
                if (Regex.IsMatch(left, @"\]$") && right.StartsWith("[")) continue;   // "[A], [B] or [C]" — 이름 목록 안은 나누지 않는다
                // "1 red or [CS] trait Tamer card" — 앞쪽에 종류가 없으면 뒤쪽의 종류 · 조건을 붙인다
                var kindTail = Regex.Match(right, @"\b(?:Digimon|Tamer|Option) cards?\b.*$", RegexOptions.IgnoreCase);
                if (kindTail.Success && !Regex.IsMatch(left, @"\bcards?\b|\b(?:Digimon|Tamer|Option)\b", RegexOptions.IgnoreCase)) left += " " + kindTail.Value;
            if (!Regex.IsMatch(right, @"^(?:\d+|a|an) ", RegexOptions.IgnoreCase)) right = lead.Value + right;
            string? Alt(string x) => PickKoCore(x) ?? (Regex.IsMatch(x, $@"(?:\btraits?|\b(?:{Colors}))$", RegexOptions.IgnoreCase) ? PickKoCore(x + " card") : null);
            if (Alt(left) is { } a && Alt(right) is { } b)
            {
                var cnt = Regex.Match(b, @"\s(\d+장(?:까지)?)$");
                string aa = cnt.Success ? Regex.Replace(a, @"\s\d+장(?:까지)?$", "") : a;
                return $"{aa} 또는 {b}";
            }
        }
        return null;
    }

    private static string? PickKoCore(string part)
    {
        part = part.Trim();
        var alternatives = Regex.Split(part, @"(?<!level \d+)\s+or\s+(?=(?:up to )?(?:\d+|a|an) )", RegexOptions.IgnoreCase);
        // "1 purple Digimon card or Option card" — 뒤쪽에 수가 없으면 앞의 수를 붙인다
        var lead = Regex.Match(part, @"^(?:up to )?(?:\d+|a|an) ");
        if (alternatives.Length == 1 && lead.Success)
        {
            var tail = Regex.Split(part, @"\s+or\s+(?=(?:Digimon|Tamer|Option) cards?\b)", RegexOptions.IgnoreCase);
            if (tail.Length > 1) alternatives = tail.Select((t, i) => i == 0 ? t : lead.Value + t).ToArray();
        }
        if (alternatives.Length > 1)
        {
            var ko = alternatives.Select(PickKo).ToList();
            return ko.Any(k => k == null) ? null : string.Join(" 또는 ", ko);
        }
        var nm = Regex.Match(part, @"^(?:(?<up>up to )?(?<n>\d+|a|an)) (?<names>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*)$", RegexOptions.IgnoreCase);
        if (nm.Success)
            return $"{NamesKo(nm.Groups["names"].Value)} {Count(nm)}";
        var m = Regex.Match(part,
            $@"^(?<up>up to )?(?<n>\d+|a|an|all) (?<adj>(?:(?:{TargetSpec.NameList}(?: traits?)?|level \d+(?: or (?:lower|higher|(?:level )?\d+))?|play costs? \d+ or (?:lower|higher|less|more)|single-colou?r|(?:{Colors})(?:(?:,| or| and/or|/) (?:{Colors}))*)\s+)*)(?<kind>Digimon|Tamer|Option)?\s*(?:cards?)?(?<filter>.*)$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var mods = new List<string>();
        var colorMods = new List<string>();
        foreach (Match a in Regex.Matches(m.Groups["adj"].Value, $@"(?<names>{TargetSpec.NameList})(?<trait> traits?)?|level (?<lv>\d+)(?: or (?:(?<dir>lower|higher)|(?:level )?(?<lv2>\d+)))?|play costs? (?<pc>\d+) or (?<pdir>lower|higher|less|more)|(?<single>single-colou?r)|(?<colors>(?:{Colors})(?:(?:,| or| and/or|/) (?:{Colors}))*)", RegexOptions.IgnoreCase))
        {
            if (a.Groups["single"].Success) mods.Add("1색의");   // v0.33.0 (LM-023)
            else if (a.Groups["names"].Success)
            {
                string x = NamesKo(a.Groups["names"].Value, trait: a.Groups["trait"].Success);
                mods.Add(a.Groups["trait"].Success ? $"특징으로 {x}{J(x, "을", "를")} 가진" : $"{x}인");
            }
            else if (a.Groups["pc"].Success) mods.Add($"등장 코스트 {a.Groups["pc"].Value} {Dir(a.Groups["pdir"].Value)}인");
            else if (a.Groups["lv2"].Success) mods.Add($"Lv.{a.Groups["lv"].Value} 또는 Lv.{a.Groups["lv2"].Value}인");
            else if (a.Groups["lv"].Success) mods.Add($"Lv.{a.Groups["lv"].Value}{(a.Groups["dir"].Success ? " " + Dir(a.Groups["dir"].Value) : "")}인");
            else if (a.Groups["colors"].Success) colorMods.Add(ColorsKo(a.Groups["colors"].Value) + "의");
        }
        string filter = m.Groups["filter"].Value.Trim();
        while (filter.Length > 0)
        {
            Match f;
            if ((f = Regex.Match(filter, $@"^(?:with |and |or |,\s*)?(?<x>{TargetSpec.NameList}) in (?:its|their) names?", RegexOptions.IgnoreCase)).Success)
            { string x = NamesKo(f.Groups["x"].Value); mods.Add($"명칭에 {x}{J(x, "을", "를")} 포함하는"); }
            else if ((f = Regex.Match(filter, $@"^(?:with |and |or |,\s*)?(?:the )?(?<x>{TargetSpec.NameList}) traits?|^(?:with |or |,\s*)?(?:the )?(?<x>{TargetSpec.NameList}) in any of (?:its|their) traits(?: other than (?<ex>\[[^\]]+\]))?", RegexOptions.IgnoreCase)).Success)
            {
                string x = NamesKo(f.Groups["x"].Value, trait: true);
                string ex = f.Groups["ex"].Success ? $"({NamesKo(f.Groups["ex"].Value, trait: true)}은 제외) " : "";
                mods.Add($"특징으로 {x}{J(x, "을", "를")} {ex}가진");
            }
            else if ((f = Regex.Match(filter, $@"^(?:with |and |or |,\s*)?(?<x>{TargetSpec.NameList}) in (?:its|their) texts?", RegexOptions.IgnoreCase)).Success)
            { string x = NamesKo(f.Groups["x"].Value); mods.Add($"{x}{J(x, "이", "가")} 기술되어 있는"); }
            else if ((f = Regex.Match(filter, @"^(?:with |and |,\s*)?(?:a |an )?(?:memory |play )?costs? of (?<n>\d+) or (?<dir>less|more|lower|higher)", RegexOptions.IgnoreCase)).Success)
                mods.Add($"코스트 {f.Groups["n"].Value} {Dir(f.Groups["dir"].Value)}인");
            else if ((f = Regex.Match(filter, @"^(?:with |and |,\s*)?(?<n>\d+) DP or (?<dir>less|more)", RegexOptions.IgnoreCase)).Success)
                mods.Add($"DP {f.Groups["n"].Value} {Dir(f.Groups["dir"].Value)}인");
            else if ((f = Regex.Match(filter, @"^(?:with |and |,\s*)?(?:a )?level (?:of )?(?<n>\d+)(?: or (?<dir>lower|higher|less|more))?", RegexOptions.IgnoreCase)).Success)
                mods.Add($"Lv.{f.Groups["n"].Value}{(f.Groups["dir"].Success ? " " + Dir(f.Groups["dir"].Value) : "")}인");
            else if ((f = Regex.Match(filter, $@"^(?:,\s*)?other than (?<x>{TargetSpec.NameList})", RegexOptions.IgnoreCase)).Success)
                mods.Add($"{NamesKo(f.Groups["x"].Value)} 이외의");
            else if ((f = Regex.Match(filter, @"^(?:with |and |or )?＜(?<k>[^＞]+?)\s*＞ in (?:its|their) texts?", RegexOptions.IgnoreCase)).Success && KeywordKo(f.Groups["k"].Value, false) is { } kt)
                mods.Add($"{kt}{J(kt, "이", "가")} 기술되어 있는");
            else if ((f = Regex.Match(filter, @"^(?:with |and |or )?＜(?<k>[^＞]+?)\s*＞", RegexOptions.IgnoreCase)).Success && KeywordKo(f.Groups["k"].Value, false) is { } kk)
                mods.Add($"{kk}{J(kk, "을", "를")} 가진");
            else if ((f = Regex.Match(filter, @"^with (?:an? )?inherited effects?", RegexOptions.IgnoreCase)).Success)
                mods.Add("진화원 효과를 가진");
            else if ((f = Regex.Match(filter, @"^with an? \[(?<t>[^\]]+)\] effect", RegexOptions.IgnoreCase)).Success && LabelKo(f.Groups["t"].Value) is { } lk)
                mods.Add($"{lk} 효과를 가진");
            else if ((f = Regex.Match(filter, $@"^(?<x>{TargetSpec.NameList})", RegexOptions.IgnoreCase)).Success)
                mods.Add($"{NamesKo(f.Groups["x"].Value)}인");
            else return null;
            // "with [Ravemon] in its name or [Bird] in any of its traits" — 둘 중 하나 (공식 BT23-011: "특징 「CS」를 가지거나 레드인")
            if (Regex.IsMatch(f.Value, @"^or\s", RegexOptions.IgnoreCase) && mods.Count >= 2)
            {
                string prev = mods[^2];
                string joined = Regex.Replace(prev, @"(?:가진|갖는)$", "가지거나");
                joined = Regex.Replace(joined, @"포함하는$", "포함하거나");
                joined = Regex.Replace(joined, @"있는$", "있거나");
                joined = Regex.Replace(joined, @"인$", "이거나");
                if (joined != prev) { mods[^2] = joined + " " + mods[^1]; mods.RemoveAt(mods.Count - 1); }
            }
            filter = filter[f.Length..].Trim().TrimStart(',').Trim();
        }
        string kind = m.Groups["kind"].Value.ToLowerInvariant() switch { "digimon" => "디지몬 카드", "tamer" => "테이머 카드", "option" => "옵션 카드", _ => "카드" };
        string pre = string.Join(" ", mods.Concat(colorMods));
        return $"{(pre.Length > 0 ? pre + " " : "")}{kind} {Count(m)}";
    }

    private static string Count(Match m)
    {
        string n = m.Groups["n"].Value.ToLowerInvariant();
        if (n == "all") return "전부";
        string k = int.TryParse(n, out int v) ? v.ToString() : "1";
        return m.Groups["up"].Success ? $"{k}장까지" : $"{k}장";
    }

    /// <summary>"[Shoutmon]" · "your [Keramon]" · "Digimon with [Omnimon] in its name" (특수 진화 주어)</summary>
    private static string? WhoKo(string who)
    {
        who = who.Trim();
        if (Regex.IsMatch(who, @"^(?:other )?\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*$"))
            return (who.StartsWith("other", StringComparison.OrdinalIgnoreCase) ? "다른 " : "") + NamesKo(who);
        var m = Regex.Match(who, $@"^(?<other>other )?Digimon(?: with (?<x>{TargetSpec.NameList}) in (?:its|their) names?)?$", RegexOptions.IgnoreCase);
        if (m.Success)
            return $"{(m.Groups["x"].Success ? $"명칭에 {NamesKo(m.Groups["x"].Value)}{J(NamesKo(m.Groups["x"].Value), "을", "를")} 포함하는 " : "")}{(m.Groups["other"].Success ? "다른 " : "")}디지몬";
        return TargetKo("all of your " + who, bare: true) is { } t ? Regex.Replace(t, @"^자신의 ", "") : null;
    }

    // =====================================================================
    // 용어집: 발동 시점 · 키워드 · 색 · 카드 이름 · 특징
    // =====================================================================

    private static string? LabelKo(string label) => label.ToLowerInvariant() switch
    {
        "on play" => "【등장 시】",
        "when digivolving" => "【진화 시】",
        "when attacking" => "【어택 시】",
        "on deletion" => "【소멸 시】",
        "your turn" => "【자신의 턴】",
        "opponent's turn" => "【상대의 턴】",
        "all turns" => "【서로의 턴】",
        "main" => "【메인】",
        "security" => "【시큐리티】",
        "start of your turn" => "【자신의 턴 개시 시】",
        "end of your turn" => "【자신의 턴 종료 시】",
        "start of opponent's turn" or "start of your opponent's turn" => "【상대의 턴 개시 시】",
        "end of opponent's turn" or "end of your opponent's turn" => "【상대의 턴 종료 시】",
        "start of your main phase" => "【자신의 메인 페이즈 개시 시】",
        "end of attack" => "【어택 종료 시】",
        "end of all turns" => "【서로의 턴 종료 시】",
        "on move" or "when moving" => "【이동 시】",
        "counter" => "【카운터】",
        "hand" => "[패]",
        "trash" => "[트래시]",
        "breeding" => "[육성]",
        "once per turn" => "[턴에 1회]",
        "twice per turn" => "[턴에 2회]",
        _ => null,
    };

    /// <summary>공식 한글 키워드 이름과 설명 (공식 한글판 카드 문구에서 가장 많이 쓰인 표기 — N은 수치)</summary>
    private static readonly (string En, string Ko, string Reminder)[] Keywords =
    {
        ("Blocker", "블로커", "이 디지몬은 블록 타이밍에 블록할 수 있다"),
        ("Piercing", "관통", "이 디지몬이 어택한 배틀에서 상대의 디지몬만을 소멸시켰을 때, 이 디지몬은 시큐리티를 체크한다"),
        ("Jamming", "재밍", "이 디지몬은 시큐리티 디지몬과의 배틀에서는 소멸하지 않는다"),
        ("Reboot", "재기동", "이 디지몬은 상대의 액티브 페이즈에도 액티브가 된다"),
        ("Rush", "속공", "이 디지몬은 등장한 턴에도 어택할 수 있다"),
        ("Retaliation", "길동무", "배틀에서 이 디지몬만이 소멸했을 때, 배틀한 상대의 디지몬을 소멸시킨다"),
        ("Raid", "돌진", "이 디지몬이 어택했을 때, 어택의 대상을 가장 DP가 높은 액티브 상태인 상대의 디지몬 1마리로 변경할 수 있다"),
        ("Alliance", "연계", "이 디지몬이 어택했을 때, 다른 자신의 디지몬 1마리를 레스트시키는 것으로, 이 어택 동안, 레스트시킨 디지몬의 DP를 더하고 《시큐리티 어택 +1》을 얻는다"),
        ("Barrier", "방벽", "이 디지몬이 배틀에서 소멸할 때, 자신의 시큐리티를 위에서부터 1장 파기하는 것으로, 소멸하지 않는다"),
        ("Evade", "회피", "이 디지몬이 소멸할 때, 이 디지몬을 레스트시키는 것으로, 소멸하지 않는다"),
        ("Blitz", "진격", "메모리가 상대측 1 이상일 때, 이 디지몬은 어택할 수 있다"),
        ("Delay", "딜레이", "배틀 에어리어에 있는 이 카드를 파기하는 것으로, 이하의 효과를 발휘한다. 이 효과는 이 카드를 놓은 턴에는 발휘할 수 없다"),
        ("Save", "세이브", "이 카드를 자신의 테이머 아래에 놓을 수 있다"),
        ("Fortitude", "불굴", "진화원을 가진 이 디지몬이 소멸했을 때, 이 카드를 코스트를 지불하지 않고 등장시킨다"),
        ("Armor Purge", "아머 퍼지", "이 디지몬이 소멸할 때, 이 디지몬에 겹쳐진 카드를 위에서부터 1장 파기하는 것으로, 소멸하지 않는다"),
        ("Blast Digivolve", "블래스트 진화", "자신의 디지몬을 이 카드로 코스트를 지불하지 않고 진화시킬 수 있다"),
        ("Iceclad", "빙장", "이 디지몬의 시큐리티 디지몬 이외와의 배틀에서는 DP가 아닌 진화원 매수를 비교한다"),
        ("Progress", "프로그레스", "어택 중인 이 디지몬은 상대의 효과를 받지 않는다"),
        ("Mind Link", "마인드 링크", "이 테이머를 진화원에 테이머 카드가 없는 디지몬의 진화원 아래에 놓는다"),
        ("Vortex", "볼텍스", "자신의 턴 종료 시, 이 디지몬으로 상대의 디지몬에게 어택할 수 있다. 이 효과로는 등장한 턴에도 어택할 수 있다"),
        ("Execute", "에그제큐트", "자신의 턴 종료 시, 이 디지몬으로 어택할 수 있다. 이 어택 종료 시, 이 디지몬은 소멸한다. 이 효과로는 액티브 상태인 상대의 디지몬에게도 어택할 수 있다"),
        ("Collision", "충돌", "이 디지몬이 어택하고 있는 동안, 상대의 디지몬 전부는 《블로커》를 얻고, 상대는 가능하다면 블록한다"),
        // 조건 없이 이름만 나올 때 ("gains ＜Decode＞" 등)
        ("Decode", "디코드", "이 디지몬이 배틀 이외로 배틀 에어리어를 벗어날 때, 이 디지몬의 진화원에서 지정된 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다"),
        ("Partition", "파티션", "진화원에 지정된 카드가 1장씩 있는 이 디지몬이 자신의 효과와 배틀 이외로 배틀 에어리어를 벗어날 때, 지정된 카드 1장씩을 코스트를 지불하지 않고 등장시킬 수 있다"),
        ("Fragment", "프래그먼트", "이 디지몬이 소멸할 때, 이 디지몬의 진화원을 지정된 장수만큼 파기하는 것으로, 소멸하지 않는다"),
        ("Overclock", "오버클럭", "자신의 턴 종료 시, 지정된 다른 자신의 디지몬 1마리를 소멸시키는 것으로, 이 디지몬으로 레스트시키지 않고 플레이어에게 어택한다"),
        ("Decoy", "디코이", "지정된 다른 자신의 디지몬이 상대의 효과로 소멸할 때, 이 디지몬을 소멸시키는 것으로, 그 디지몬 1마리는 소멸하지 않는다"),
    };

    /// <summary>"Blocker" · "Draw 1" · "Security A. +1" · "De-Digivolve 2" · "Recovery +1 (Deck)" → 《…》 (+ 설명)</summary>
    private static string? KeywordKo(string en, bool withReminder)
    {
        en = en.Trim();
        Match m;
        string R(string text) => withReminder ? $" ({text})" : "";
        if ((m = Regex.Match(en, @"^Draw (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return $"《{m.Groups["n"].Value} 드로우》{R($"자신의 덱에서 카드를 {m.Groups["n"].Value}장 드로우한다")}";
        if ((m = Regex.Match(en, @"^Security A(?:ttack)?\.?\s*(?<sign>[+-])(?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return $"《시큐리티 어택 {m.Groups["sign"].Value}{m.Groups["n"].Value}》{R($"이 디지몬이 체크하는 시큐리티의 매수 {m.Groups["sign"].Value}{m.Groups["n"].Value}")}";
        if (Regex.IsMatch(en, @"^Security A(?:ttack)?\.?$", RegexOptions.IgnoreCase))
            return "《시큐리티 어택》";
        if ((m = Regex.Match(en, @"^De-Digivolve (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return $"《퇴화 {m.Groups["n"].Value}》{R($"상대 디지몬 위에 포개진 카드를 위에서부터 {m.Groups["n"].Value}장 파기한다. 진화원이 없어지거나, Lv.3의 디지몬이 되었다면 그 이상 파기할 수 없다")}";
        if ((m = Regex.Match(en, @"^Recovery \+(?<n>\d+)\s*(?:\(Deck\))?$", RegexOptions.IgnoreCase)).Success)
            return $"〈리커버리 +{m.Groups["n"].Value} 《덱》〉{R($"자신의 덱 위에서 카드를 {m.Groups["n"].Value}장 시큐리티 위에 놓는다")}";
        if ((m = Regex.Match(en, @"^Digi-Burst (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return $"《디지버스트 {m.Groups["n"].Value}》{R($"이 디지몬의 진화원을 {m.Groups["n"].Value}장 골라 파기하는 것으로 이하의 효과를 발휘한다")}";
        if (Regex.IsMatch(en, @"^Digi-Burst$", RegexOptions.IgnoreCase))
            return "《디지버스트》";
        if ((m = Regex.Match(en, @"^Digisorption\s*-\s*(?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return $"《흡수진화 -{m.Groups["n"].Value}》{R($"자신의 디지몬이 패의 이 카드로 진화할 때, 자신의 디지몬 1마리를 레스트시키는 것으로, 지불하는 진화 코스트를 -{m.Groups["n"].Value} 한다")}";
        if (Regex.IsMatch(en, @"^Digisorption$", RegexOptions.IgnoreCase))
            return "《흡수진화》";
        if ((m = Regex.Match(en, @"^Material Save (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return $"《머티리얼 세이브 {m.Groups["n"].Value}》{R($"이 디지몬이 소멸할 때, 이 디지몬의 디지크로스 조건에 있는 진화원의 카드 {m.Groups["n"].Value}장을 자신의 테이머 아래에 놓을 수 있다")}";
        if ((m = Regex.Match(en, @"^Scapegoat$", RegexOptions.IgnoreCase)).Success)
            return $"《스케이프고트》{R("이 디지몬이 자신의 효과 이외로 소멸할 때, 다른 자신의 디지몬 1마리를 소멸시키는 것으로, 소멸하지 않는다")}";
        if ((m = Regex.Match(en, @"^Training$", RegexOptions.IgnoreCase)).Success)
            return $"《트레이닝》{R("메인 중, 이 디지몬을 레스트시키는 것으로, 자신의 덱 위에서부터 1장을 이 디지몬의 진화원 아래에 뒷면으로 놓는다. 이 효과는 육성 에어리어에서도 발휘할 수 있다")}";
        // 7단계 메커닉 키워드 (공식 한글 표기 《이름《조건》》 — 공식 표기가 없는 신규 키워드는 음역)
        if ((m = Regex.Match(en, @"^Link \+(?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            return $"《링크 +{m.Groups["n"].Value}》{R($"이 디지몬의 링크 매수 상한 +{m.Groups["n"].Value}")}";
        if ((m = Regex.Match(en, @"^Fragment\s*\(?(?<n>\d+)\)?$", RegexOptions.IgnoreCase)).Success)
            return $"《프래그먼트《{m.Groups["n"].Value}》》{R($"이 디지몬이 소멸할 때, 이 디지몬의 진화원을 {m.Groups["n"].Value}장 파기하는 것으로, 소멸하지 않는다")}";
        if ((m = Regex.Match(en, @"^Decode\s*\((?<x>.+)\)$", RegexOptions.IgnoreCase)).Success && SpecKo(m.Groups["x"].Value) is { } decode)
            return $"《디코드《{decode}》》{R("이 디지몬이 배틀 이외로 배틀 에어리어를 벗어날 때, 이 디지몬의 진화원에서 지정된 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다")}";
        if ((m = Regex.Match(en, @"^Partition\s*\((?<a>.+?)\s*&\s*(?<b>.+)\)$", RegexOptions.IgnoreCase)).Success && SpecKo(m.Groups["a"].Value) is { } pa && SpecKo(m.Groups["b"].Value) is { } pb)
            return $"《파티션《{pa}&{pb}》》{R("진화원에 지정된 카드가 1장씩 있는 이 디지몬이 자신의 효과와 배틀 이외로 배틀 에어리어를 벗어날 때, 지정된 카드 1장씩을 코스트를 지불하지 않고 등장시킬 수 있다")}";
        if ((m = Regex.Match(en, @"^Overclock\s*\((?<x>.+)\)$", RegexOptions.IgnoreCase)).Success && SpecKo(m.Groups["x"].Value) is { } oc)
        {
            // 공식 (EX7-030 · BT19-101): "자신의 토큰 또는 특징으로 「퍼펫형」을 가진 다른 자신의 디지몬 1마리를 소멸시키는 것으로"
            string who = Regex.Match(oc, @"^특징「(?<t>[^」]+)」$") is { Success: true } ot
                ? $"특징으로 「{ot.Groups["t"].Value}」{J(ot.Groups["t"].Value, "을", "를")} 가진"
                : oc.EndsWith("를 포함", StringComparison.Ordinal) || oc.EndsWith("을 포함", StringComparison.Ordinal) ? oc + "하는" : oc + "인";
            return $"《오버클럭《{oc}》》{R($"자신의 턴 종료 시, 자신의 토큰 또는 {who} 다른 자신의 디지몬 1마리를 소멸시키는 것으로, 이 디지몬으로 레스트시키지 않고 플레이어에게 어택한다")}";
        }
        // 공식 (BT16-052 · BT20-017): 《디코이《블랙》》 · 《디코이《레드》/《블랙》》 (색이 여럿이면 색마다 《》 — "(Red/Black)" · "(Red)/(Black)" 모두)
        if ((m = Regex.Match(en, @"^Decoy\s*\((?<x>.+)\)$", RegexOptions.IgnoreCase)).Success
            && Regex.Replace(m.Groups["x"].Value, @"\)\s*/\s*\(", "/") is { } decoyX && Regex.IsMatch(decoyX, $@"^(?:{Colors})(?:/(?:{Colors}))+$", RegexOptions.IgnoreCase))
        {
            string dc = string.Join("/", decoyX.Split('/').Select(x => "《" + ColorKo(x.Trim()) + "》"));
            return $"《디코이{dc}》{R($"{string.Join("/", decoyX.Split('/').Select(x => ColorKo(x.Trim())))}의 다른 자신의 디지몬이 상대의 효과로 소멸할 때, 이 디지몬을 소멸시키는 것으로, 그 디지몬 1마리는 소멸하지 않는다")}";
        }
        if ((m = Regex.Match(en, @"^Decoy\s*\((?<x>.+)\)$", RegexOptions.IgnoreCase)).Success && SpecKo(m.Groups["x"].Value) is { } decoy)
            return $"《디코이《{decoy}》》{R($"{decoy}의 다른 자신의 디지몬이 상대의 효과로 소멸할 때, 이 디지몬을 소멸시키는 것으로, 그 디지몬 1마리는 소멸하지 않는다")}";
        if ((m = Regex.Match(en, @"^Detach\s*\((?<x>.+)\)$", RegexOptions.IgnoreCase)).Success && SpecKo(m.Groups["x"].Value) is { } detach)
            return $"《분리《{detach}》》{R("이 디지몬이 자신의 효과 이외로 배틀 에어리어를 벗어날 때, 지정된 링크 카드 1장을 파기하는 것으로, 벗어나지 않는다")}";
        if ((m = Regex.Match(en, @"^Use Req(?:\.|uirement)?\s*\((?<x>.+)\)$", RegexOptions.IgnoreCase)).Success && SpecKo(m.Groups["x"].Value) is { } useReq)
            return $"《사용 조건《{useReq}》》{R("지정된 조건이 색 조건을 충족한다")}";
        if (Regex.IsMatch(en, @"^Engage$", RegexOptions.IgnoreCase))
            return $"《급습》{R("자신의 턴 종료 시, 이 디지몬으로 어택할 수 있다")}";
        if (Regex.IsMatch(en, @"^Ascension$", RegexOptions.IgnoreCase))
            return $"《천승》{R("이 디지몬이 소멸했을 때, 이 카드를 시큐리티 위에 놓을 수 있다")}";
        if (Regex.IsMatch(en, @"^Guard$", RegexOptions.IgnoreCase))
            return $"《수호》{R("다른 자신의 디지몬이 상대의 효과로 배틀 에어리어를 벗어날 때, 이 디지몬을 소멸시키는 것으로, 벗어나지 않는다")}";
        if ((m = Regex.Match(en, @"^(?<k>Raid|Barrier|Armor Purge)\s*(?<n>\d+)?$", RegexOptions.IgnoreCase)).Success && !m.Groups["n"].Success)
        {
            var kw0 = Keywords.First(k => k.En.Equals(m.Groups["k"].Value, StringComparison.OrdinalIgnoreCase));
            return $"《{kw0.Ko}》{R(kw0.Reminder)}";
        }
        var kw = Keywords.FirstOrDefault(k => k.En.Equals(en, StringComparison.OrdinalIgnoreCase));
        return kw.En == null ? null : $"《{kw.Ko}》{R(kw.Reminder)}";
    }

    /// <summary>"＜Blocker＞ and ＜Reboot＞" · "＜Security A. +1＞" → 《블로커》와 《재기동》 (설명 포함)</summary>
    private static string? KeywordListKo(string text)
    {
        // 키워드와 구분(쉼표 · and)만 있어야 한다 — "＜A＞ for each ~ with ＜B＞"처럼 다른 말이 섞이면 키워드 목록이 아니다
        if (Regex.IsMatch(Regex.Replace(text, @"＜[^＞]+＞|\band\b|[,\s.]", ""), "[A-Za-z]")) return null;
        var parts = Regex.Matches(text, @"＜([^＞]+)＞").Select(m => KeywordKo(m.Groups[1].Value, withReminder: true)).ToList();
        if (parts.Count == 0 || parts.Any(p => p == null)) return null;
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + J(parts[^2]!, "과", "와") + " " + parts[^1];
    }

    /// <summary>
    /// 키워드 괄호 속 조건: "Blue Lv.4" → 블루 Lv.4 · "[Aegiomon]" → 「아이기오몬」 · "[Seven Code] trait" → 특징「세븐 코드」 ·
    /// "[Maquinamon] in text" → 「마키나몬」 기술 · "Lv.4 or lower w/[Agumon]/[Greymon] in name or w/[ME] trait" (옮기지 못한 영어가 남으면 null)
    /// </summary>
    private static string? SpecKo(string spec)
    {
        string s = spec.Trim();
        // 특징: "[A]/[B] trait" · "[A] Trait" · "w/[A] trait"
        s = Regex.Replace(s, @"(?:w/)?(?<x>\[[^\]]+\](?:\s*/\s*\[[^\]]+\])*)\s+trait\b", m => "특징" + NamesKo(m.Groups["x"].Value, trait: true), RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?:w/)?(?<x>\[[^\]]+\](?:\s*/\s*\[[^\]]+\])*)\s+in (?:its )?name\b", m => "명칭에 " + NamesKo(m.Groups["x"].Value) + "를 포함", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?:w/)?(?<x>\[[^\]]+\](?:\s*/\s*\[[^\]]+\])*)\s+in (?:its )?text\b", m => NamesKo(m.Groups["x"].Value) + " 기술", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[[^\]]+\](?:\s*/\s*\[[^\]]+\])*", m => NamesKo(m.Value));
        s = Regex.Replace(s, @"\b(?:Level|Lv\.)\s*(?<n>\d+)\s+or\s+(?<d>lower|higher)\b", m => $"Lv.{m.Groups["n"].Value} {(m.Groups["d"].Value.Equals("lower", StringComparison.OrdinalIgnoreCase) ? "이하" : "이상")}", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bLevel\s*(?<n>\d+)\b", "Lv.${n}", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, Colors, m => ColorKo(m.Value), RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+or\s+", " 또는 ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        // 남은 영어(카드 이름 「」 안 · Lv. 제외)가 있으면 옮기지 못한 것
        string rest = Regex.Replace(s, @"「[^」]*」|Lv\.", "");
        return Regex.IsMatch(rest, "[A-Za-z]") ? null : s;
    }

    private static string ColorKo(string color) => color.ToLowerInvariant() switch
    {
        "red" => "레드", "blue" => "블루", "yellow" => "옐로", "green" => "그린", "black" => "블랙", "purple" => "퍼플", "white" => "화이트", _ => color,
    };

    private static string ColorsKo(string text) =>
        string.Join("/", Regex.Matches(text, Colors, RegexOptions.IgnoreCase).Select(x => ColorKo(x.Value)));

    /// <summary>"[A], [B] or [C]" → 「A」/「B」/「C」 (공식 한글 이름이 있으면 한글)</summary>
    private static string NamesKo(string names, bool trait = false)
    {
        var list = Regex.Matches(names, @"\[([^\]]+)\]").Select(x => $"「{(trait ? TraitKo(x.Groups[1].Value) : NameKo(x.Groups[1].Value))}」").ToList();
        return list.Count == 0 ? names : string.Join("/", list);
    }

    /// <summary>토큰 이름 (공식 한글 — 카드 효과 문장 속 이름이라 카드 데이터 사전에는 없다)</summary>
    private static readonly Dictionary<string, string> TokenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Familiar"] = "사역마", ["Pipe Fox"] = "대롱 여우", ["Atho, René & Por"] = "아트＆르네＆포르", ["Amon of Crimson Flame"] = "홍염의 아몬",
        ["Umon of Blue Thunder"] = "창뢰의 우몬", ["KoHagurumon"] = "꼬마 톱니몬", ["Uka no Mitama"] = "우카노미타마", ["Hinukamuy"] = "히누카무이",
    };

    /// <summary>토큰 "[Familiar]" + 정보 "Digimon/Yellow/3000 DP/[On Deletion] ~" → 「사역마」(디지몬・옐로・DP 3000・【소멸 시】 ~) (공식 BT22-040 · BT17-100 표기)</summary>
    private static string? TokenKo(string name, string spec)
    {
        if (SentenceParser.ParseTokenSpec(spec) is not { } t) return null;
        var parts = new List<string> { "디지몬" };
        if (t.Cost > 0) parts.Add($"{t.Cost} 코스트");
        if (t.Level > 0) parts.Add($"Lv.{t.Level}");
        parts.Add(ColorKo(t.Color.ToString()));
        parts.AddRange(t.Traits.Select(TraitKo));
        parts.Add($"DP {t.Dp}");
        var keywords = new List<string>();
        foreach (var e in t.Effects)
        {
            if (Regex.IsMatch(e, @"^(?:＜[^＞]+＞\s*)+$"))
            {
                foreach (Match k in Regex.Matches(e, @"＜([^＞]+)＞"))
                    keywords.Add(KeywordKo(k.Groups[1].Value, withReminder: false) ?? "?");
                continue;
            }
            if (keywords.Count > 0) { parts.Add(string.Concat(keywords)); keywords.Clear(); }
            if (RenderClause(e) is not { } ko) return null;
            parts.Add(ko);
        }
        if (keywords.Count > 0) parts.Add(string.Concat(keywords));
        if (parts.Any(x => x.Contains("?"))) return null;
        string n = TokenNames.TryGetValue(name.Trim(), out var tk) ? tk : NameKo(name);
        return $"「{n}」({string.Join("・", parts)})";
    }

    internal static string NameKo(string en) => Glossary.Value.Names.TryGetValue(en.Trim(), out var ko) ? ko
        // v0.31.0 "Creepymon (X Antibody)" · "Sistermon Noir (Awakened)" — 용어집에 없으면 기본 이름 + "X항체" / "(각성)" (공식: 「베르제브몬 X항체」 · 「시스터몬 블랑(각성)」)
        : Regex.Match(en.Trim(), @"^(?<b>.+?) \((?<p>X Antibody|Awakened)\)$") is { Success: true } xp && Glossary.Value.Names.TryGetValue(xp.Groups["b"].Value, out var baseKo)
            ? (xp.Groups["p"].Value == "X Antibody" ? baseKo + " X항체" : baseKo + "(각성)")
        : Glossary.Value.Names.Count > 0 && ExtraNames34.TryGetValue(en.Trim(), out var extra) ? extra   // v0.34.0 카드 이름이 아닌 이름 조각 (공식 한글 데이터가 있을 때만)
        : en.Trim();

    internal static string TraitKo(string en) => Glossary.Value.Traits.TryGetValue(en.Trim(), out var ko) ? ko
        : Glossary.Value.Traits.Count > 0 && ExtraTraits34.TryGetValue(en.Trim(), out var extra) ? extra   // v0.34.0
        : en.Trim();

    /// <summary>
    /// v0.34.0 카드 이름 그대로가 아닌 이름 조각 ("with [Sistermon] in its name") — 공식 한글 표기 (BT10-016 「시스터몬」 · BT13-068 「체스몬」 · BT12-021 「황제드라몬」 ·
    /// ST8-03 「드라몬」 · BT9-029 「저스티몬」 · BT13-103 「벨페몬」/「기즈몬」 · EX2-021 「플러그인」). 용어집(공식 한글 데이터)이 있을 때만 쓴다
    /// </summary>
    private static readonly Dictionary<string, string> ExtraNames34 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sistermon"] = "시스터몬", ["Chessmon"] = "체스몬", ["Imperialdramon"] = "황제드라몬", ["Dramon"] = "드라몬",
        ["Justimon"] = "저스티몬", ["Belphemon"] = "벨페몬", ["Gizmon"] = "기즈몬", ["Plug-In"] = "플러그인", ["Three Musketeers"] = "3총사",
    };

    /// <summary>v0.34.0 특징 조각 ("[Aqua] or [Sea Animal] in any of its traits") — 공식 (BT22-018 · P-164): 「수생」</summary>
    private static readonly Dictionary<string, string> ExtraTraits34 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aqua"] = "수생",
    };

    /// <summary>
    /// 카드 이름 · 특징 용어집: 영어 카드 캐시와 공식 한글 데이터(data/kr/cards_ko.json)를 카드 번호로 맞춰 만든다.
    /// 공식 데이터가 없는 PC에서는 비어 있고, 이름 · 특징은 영어 그대로 쓴다.
    /// </summary>
    private static readonly Lazy<(Dictionary<string, string> Names, Dictionary<string, string> Traits)> Glossary = new(BuildGlossary);

    /// <summary>영어 카드 이름 → 공식 한글 이름 (공식 한글 데이터가 없으면 비어 있다) — 로그 · 메시지의 이름 바꾸기에 쓴다</summary>
    internal static IReadOnlyDictionary<string, string> NameGlossary => Glossary.Value.Names;

    private static (Dictionary<string, string>, Dictionary<string, string>) BuildGlossary()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var traits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (KoreanCardData.Count == 0)
                return (names, traits);
            string cache = Path.Combine(Path.GetDirectoryName(CardCatalogService.ResolveImagesDirectory()) ?? ".", "cards_cache.json");
            if (!File.Exists(cache))
                return (names, traits);
            var opts = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var cards = JsonSerializer.Deserialize<List<CardData>>(File.ReadAllText(cache), opts) ?? new();
            var traitVotes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var nameVotes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var enTraitCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var koTraitCount = new Dictionary<string, int>();
            foreach (var c in cards)
            {
                if (KoreanCardData.Get(c.CardId) is not { } ko) continue;
                // 이름: 같은 영어 이름의 공식 한글 이름 중 가장 많이 나온 것 ("WereGarurumon" → "워가루몬", ACE 카드 이름 아님)
                if (ko.Name.Length > 0)
                {
                    if (!nameVotes.TryGetValue(CardNames.MatchName(c), out var nv)) nameVotes[CardNames.MatchName(c)] = nv = new();   // 내장 한글 카드는 영어 이름으로
                    nv[ko.Name] = nv.GetValueOrDefault(ko.Name) + 1;
                }
                // 특징: 같은 카드에 함께 나온 횟수로 짝짓는다 (영어 · 한글 특징의 순서가 다른 카드가 있어 위치로 짝지으면 틀린다 — Hybrid ≠ 배리어블종)
                foreach (var en in c.Traits.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    enTraitCount[en] = enTraitCount.GetValueOrDefault(en) + 1;
                    if (!traitVotes.TryGetValue(en, out var v)) traitVotes[en] = v = new();
                    foreach (var k in ko.Traits.Distinct()) v[k] = v.GetValueOrDefault(k) + 1;
                }
                foreach (var k in ko.Traits.Distinct()) koTraitCount[k] = koTraitCount.GetValueOrDefault(k) + 1;
            }
            // 점수 = 함께² / (영어 특징 수 × 한글 특징 수) — 늘 함께 나오는 짝이 1에 가깝다
            // 점수가 높은 짝부터 1:1로 배정 — 늘 함께 붙는 특징(Hybrid · Variable, App Driver · Appmon)이 같은 한글 특징을 나눠 갖지 않게
            var scored = traitVotes.SelectMany(kv => kv.Value.Select(v => (En: kv.Key, Ko: v.Key, Score: (double)v.Value * v.Value / (enTraitCount[kv.Key] * (double)koTraitCount[v.Key]), Co: v.Value)))
                .OrderByDescending(x => x.Score).ThenByDescending(x => x.Co).ToList();
            var usedKo = new HashSet<string>();
            foreach (var x in scored)
                if (!traits.ContainsKey(x.En) && usedKo.Add(x.Ko))
                    traits[x.En] = x.Ko;
            foreach (var x in scored)   // 짝을 못 얻은 영어 특징은 가장 좋은 짝 (나눠 갖기)
                traits.TryAdd(x.En, x.Ko);
            foreach (var (en, votes) in nameVotes)
                names[en] = votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key.Length).First().Key;
        }
        catch
        {
            // 용어집 없이 영어 이름 그대로
        }
        return (names, traits);
    }

    // =====================================================================
    // 한글 조사 · 작은 도우미
    // =====================================================================

    /// <summary>앞말의 받침에 맞는 조사 (을/를 · 이/가 · 은/는 · 으로/로 · 와/과 …)</summary>
    private static string J(string word, string withBatchim, string withoutBatchim)
    {
        bool b = Batchim(word);
        if (withBatchim == "으로" && b && LastHangulJong(word) == 8) return "로";   // ㄹ 받침 + 로
        if (withBatchim == "와") return b ? "과" : "와";
        return b ? withBatchim : withoutBatchim;
    }

    private static bool Batchim(string word)
    {
        word = Regex.Replace(word, @"\s*\([^()]*\)\s*$", "");   // 《관통》 (설명) → 키워드 이름에 맞춘다
        for (int i = word.Length - 1; i >= 0; i--)
        {
            char c = word[i];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0;
            if (char.IsDigit(c)) return "013678".IndexOf(c) >= 0;   // 영 · 일 · 삼 · 육 · 칠 · 팔
            if (char.IsLetter(c)) return "lmnkptbdgLMNKPTBDG".IndexOf(c) >= 0;
        }
        return false;
    }

    private static int LastHangulJong(string word)
    {
        for (int i = word.Length - 1; i >= 0; i--)
        {
            char c = word[i];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28;
            if (char.IsDigit(c)) return c is '1' or '7' or '8' ? 8 : 0;
        }
        return 0;
    }

    private static string Side(string who) => who.Contains("opponent", StringComparison.OrdinalIgnoreCase) ? "상대" : "자신";

    private static string Dir(string dir) => dir.ToLowerInvariant() is "more" or "higher" ? "이상" : "이하";

    private static string N(Match m) => m.Groups["n"].Success && m.Groups["n"].Value.Length > 0 ? m.Groups["n"].Value : "1";

    private static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    private static string Gerund(string verb) => verb.ToLowerInvariant() switch
    {
        "delete" => "deleting",
        "place" => "placing",
        var v => v + "ing",
    };
}
