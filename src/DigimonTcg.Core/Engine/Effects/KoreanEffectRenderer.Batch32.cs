using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.32.0 미지원 문장 6차 묶음의 생성 한글 (SentenceParser.Batch32.cs와 같은 범위) — 공식 한글 표기를 따른다:
/// "특징으로 「데이터종」/「윗체르니」를 가진 옐로인 이 디지몬이 상대의 효과로 배틀 에어리어를 벗어날 때," (BT18-030) ·
/// "자신의 「아포카리몬」 또는 특징으로 「어둠의 4천왕」을 가진 자신의 디지몬이 ~" (BT15-072) · "이 디지몬이 상대의 효과로 소멸할 때," (BT17-050) ·
/// "이 디지몬이 효과로 소멸할 때, 또는 패/덱으로 되돌아갈 때," (BT11-062) · "~ 소멸하지 않고, 패/덱으로 되돌아가지 않는다" ·
/// "이 디지몬의 진화원에서 Lv.이 같은 카드 2장을 파기하는 것으로," (BT9-012) · "자신의 시큐리티를 아래에서부터 1장 파기하는 것으로," (BT18-082) ·
/// "서로의 시큐리티를 위에서부터 1장 파기하는 것으로," (BT19-043)
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>지금 그리는 절의 "떠나려 할 때" 종류 — "Prevent it"을 "벗어나지 않는다" / "소멸하지 않는다" / "소멸하지 않고, 패/덱으로 되돌아가지 않는다"로 (RenderClause가 정한다)</summary>
    [ThreadStatic] private static string? _leaveKind;

    private static string? LeaveKindOf(string clause) =>
        Regex.IsMatch(clause, @"would be deleted or returned to|an effect would delete it or return it to", RegexOptions.IgnoreCase) ? "delret"
        : Regex.IsMatch(clause, @"\bwould be deleted\b|\bwould delete (?:this|it)\b", RegexOptions.IgnoreCase) ? "del"
        : null;

    /// <summary>절 전체: 다른 표기를 원래 읽는 표기로 바꿔 다시 그린다 (진화 코스트 감소 — SentenceParser.EvoReductionText32와 같은 범위)</summary>
    private static string? ClauseKo32(string clause)
    {
        string t = SentenceParser.EvoReductionText32(clause);
        // "When this card would be played from the hand, ~" (BT23-044) = "When this card would be played, ~" (패의 이 카드)
        t = Regex.Replace(t, @"^When this card would be played from (?:the|your) hand,", "When this card would be played,", RegexOptions.IgnoreCase);
        if (t != clause) return RenderClause(t);
        // [육성] 등장 코스트 감소 + 두 번째 문장 — 공식 (BT13-007 · EX6-006): "이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -1." ·
        // "이 디지몬의 진화원에 명칭이 서로 다른 카드가 5장 이상 있다면, 대신 지불하는 등장 코스트 -4 할 수 있다."
        var two = Regex.Match(clause, @"^(?<first>.+?reduce (?:the|its|their) (?:play |memory )?costs? by \d+)\.\s+(?:Further reduce it by (?<k>\d+) for (?:each|every) of this Digimon's digivolution cards|If this Digimon has (?<c>\d+) or more cards with different names in its digivolution cards, you may reduce (?:the|its|their) (?:play |memory )?costs? by (?<m>\d+) instead)\.?$", RegexOptions.IgnoreCase);
        if (two.Success && SentenceParser.FieldPlayCostClause(two.Groups["first"].Value + ".") != null && ClauseKo31(two.Groups["first"].Value + ".") is { } first)
            return two.Groups["k"].Success
                ? $"{first} 이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -{two.Groups["k"].Value}."
                : $"{first} 이 디지몬의 진화원에 명칭이 서로 다른 카드가 {two.Groups["c"].Value}장 이상 있다면, 대신 지불하는 등장 코스트 -{two.Groups["m"].Value} 할 수 있다.";
        return null;
    }

    /// <summary>"When ~," 사건 (EventKo31 다음)</summary>
    private static string? EventKo32(string ev)
    {
        Match m;
        string fixedEv = Regex.Replace(ev, @"\bwould leave battle area\b", "would leave the battle area", RegexOptions.IgnoreCase);
        fixedEv = Regex.Replace(fixedEv, @"^this would\b", "this Digimon would", RegexOptions.IgnoreCase);
        fixedEv = Regex.Replace(fixedEv, $@"\byour (?<x>{TargetSpec.NameList}) text Digimon\b", "your Digimon with ${x} in their texts", RegexOptions.IgnoreCase);
        if (fixedEv != ev) return EventKo(fixedEv);

        // 색: "this yellow Digimon with the [Data] or [Witchelny] trait would leave ~" → "특징으로 ~을 가진 옐로인 이 디지몬이 ~"
        if ((m = Regex.Match(ev, $@"^this (?<col>(?:{Colors})(?: or (?:{Colors}))*) Digimon(?<rest> (?:with .+? )?would .+)$", RegexOptions.IgnoreCase)).Success
            && EventKo("this Digimon" + m.Groups["rest"].Value) is { } colored && colored.Contains("이 디지몬"))
        {
            int at = colored.IndexOf("이 디지몬", StringComparison.Ordinal);
            return colored[..at] + $"{ColorsKo(m.Groups["col"].Value)}인 " + colored[at..];
        }
        // 이 디지몬 (쉼표 목록 조건) — 원래 규칙은 쉼표 앞까지만 본다
        if ((m = Regex.Match(ev, @"^this Digimon (?<with>with .+?) would (?<what>leave the battle area|be deleted)(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && WithKo(m.Groups["with"].Value) is { } sw && LeaveQualifierKo(m.Groups["q"].Value) is { } lq1)
            return $"{sw} 이 디지몬이 {lq1}{(m.Groups["what"].Value.StartsWith("be", StringComparison.OrdinalIgnoreCase) ? "소멸할 때," : "배틀 에어리어를 벗어날 때,")}";
        // 자신의 디지몬: "one of your [Apocalymon] or Digimon with the [Dark Masters] trait" · "one of your [Eosmon]" · 쉼표 목록 조건
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?!opponent)(?<subj>(?:other )?.+?) would (?<what>leave the battle area|be deleted)(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && !Regex.IsMatch(m.Groups["subj"].Value, @"\bTamers?\b", RegexOptions.IgnoreCase)
            && LeaveQualifierKo(m.Groups["q"].Value) is { } lq2 && LeaveSubjectKo32(m.Groups["subj"].Value) is { } who)
            return $"{who}{J(who, "이", "가")} {lq2}{(m.Groups["what"].Value.StartsWith("be", StringComparison.OrdinalIgnoreCase) ? "소멸할 때," : "배틀 에어리어를 벗어날 때,")}";
        // 공식 (BT11-062 · EX3-013): "이 디지몬이 효과로 소멸할 때, 또는 패/덱으로 되돌아갈 때," · "이 디지몬이 소멸할 때 또는 패/덱으로 되돌아갈 때,"
        if ((m = Regex.Match(ev, @"^this Digimon(?<with> with .+?)? would be deleted or returned to (?:your|the|its owner's) hands? or decks?(?<eff> by (?:an )?effects?)?$", RegexOptions.IgnoreCase)).Success)
        {
            string? pre = m.Groups["with"].Success ? WithKo(m.Groups["with"].Value) : "";
            if (pre == null) return null;
            return $"{(pre.Length > 0 ? pre + " " : "")}이 디지몬이 {(m.Groups["eff"].Success ? "효과로 소멸할 때, 또는" : "소멸할 때 또는")} 패/덱으로 되돌아갈 때,";
        }
        // 공식 (BT9-012 · BT9-024): "명칭에 「그레이몬」 또는 「오메가몬」을 포함하는 이 디지몬이 효과로 소멸할 때, 또는 패 및 덱으로 돌아갈 때," · "~ 배틀에서 소멸할 때,"
        if ((m = Regex.Match(ev, $@"^this Digimon has (?<x>{TargetSpec.NameList}) in its name and (?:(?<eff>an effect would delete it or return it to (?:your|the|its owner's) hand or deck)|(?<battle>would be deleted in battle))$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value);
            return $"명칭에 {x}{J(x, "을", "를")} 포함하는 이 디지몬이 {(m.Groups["battle"].Success ? "배틀에서 소멸할 때," : "효과로 소멸할 때, 또는 패/덱으로 되돌아갈 때,")}";
        }
        // 공식 (BT17-050): "이 디지몬이 상대의 효과로 소멸할 때,"
        if (Regex.IsMatch(ev, @"^(?:an|your) opponent's effects? would delete this Digimon$", RegexOptions.IgnoreCase))
            return "이 디지몬이 상대의 효과로 소멸할 때,";

        // ---- 등장한 곳 — 공식 (EX1-060 · BT7-002 · BT20-028): "자신의 트래시에서 디지몬이 등장했을 때," · "자신의 디지몬의 진화원에서 디지몬이 등장했을 때," · "진화원에서 자신의 디지몬이 등장했을 때," ----
        if ((m = Regex.Match(ev, @"^you play (?:an? |1 )?Digimon(?: cards?)? from (?<z>your trash|(?<one>one of your Digimon's |your Digimon's )?digivolution cards)$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["z"].Value.Contains("trash", StringComparison.OrdinalIgnoreCase) ? "자신의 트래시에서 디지몬이 등장했을 때," : "자신의 디지몬의 진화원에서 디지몬이 등장했을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon (?:is|are) played from (?:their |its )?digivolution cards$", RegexOptions.IgnoreCase))
            return "진화원에서 자신의 디지몬이 등장했을 때,";
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon (?:is|are) played from your trash$", RegexOptions.IgnoreCase))
            return "자신의 트래시에서 디지몬이 등장했을 때,";
        // 공식 (BT12-026 · BT14-083): "상대의 디지몬의 진화원이 파기되었을 때," · "~ 효과로 파기되었을 때,"
        if (Regex.IsMatch(ev, @"^(?:a digivolution card of (?:an|one of your) opponent's Digimon is trashed|(?:an|your) opponent's Digimon's digivolution cards? (?:is|are) trashed)$", RegexOptions.IgnoreCase))
            return "상대의 디지몬의 진화원이 파기되었을 때,";
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from under (?:one of your|an|your) opponent's Digimon$", RegexOptions.IgnoreCase))
            return "상대의 디지몬의 진화원이 효과로 파기되었을 때,";
        // 공식 (BT21-004): "옐로/레드인 자신의 테이머가 레스트했을 때,"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<color>(?:{Colors})(?: or (?:{Colors}))*) Tamers? {SuspendVerbEn}$", RegexOptions.IgnoreCase)).Success)
            return $"{ColorsKo(m.Groups["color"].Value)}인 자신의 테이머가 레스트했을 때,";
        // 효과로 레스트 — 공식 (BT10-004 · BT14-004 · EX9-042): "디지몬이 효과로 레스트했을 때," · "테이머가 자신의 효과로 레스트했을 때," · "특징 「WG」를 가진 자신의 디지몬이 효과로 레스트했을 때,"
        if ((m = Regex.Match(ev, @"^(?:(?<mine>one of your effects) suspends|(?:an effect|effects) suspends?) (?<other>another |other )?(?:an? |any |any of )?(?<who>your (?<spec>[^,]*?)Digimon|Digimon or Tamers?|Digimon|Tamers?)$|^(?:an? |any )?(?<who>Digimon|Tamer) (?:is|are|becomes?) suspended by (?:an )?effects?$", RegexOptions.IgnoreCase)).Success)
        {
            string whoEn = m.Groups["who"].Value;
            string? subj = whoEn.StartsWith("your", StringComparison.OrdinalIgnoreCase)
                ? TargetKo("all of " + whoEn, bare: true)
                : (m.Groups["other"].Success ? "다른 " : "") + (Regex.IsMatch(whoEn, "Digimon or Tamer", RegexOptions.IgnoreCase) ? "디지몬 또는 테이머" : whoEn.StartsWith("Tamer", StringComparison.OrdinalIgnoreCase) ? "테이머" : "디지몬");
            if (subj == null) return null;
            return $"{subj}{J(subj, "이", "가")} {(m.Groups["mine"].Success ? "자신의 효과로" : "효과로")} 레스트했을 때,";
        }
        // 공식 (BT14-074): "특징으로 「마수형」/「SoC」를 가진 자신의 카드가 등장했을 때,"
        if ((m = Regex.Match(ev, @"^your card (?<with>with .+?) is played$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } cw)
            return $"{cw} 자신의 카드가 등장했을 때,";
        // 공식 (BT13-101 · BT9-091): "블랙과 옐로 2색의 자신의 디지몬이 등장했을 때,"
        if ((m = Regex.Match(ev, $@"^you play (?:an?|1) (?<n>\d+)-colou?r (?<a>{Colors}) and (?<b>{Colors}) Digimon$", RegexOptions.IgnoreCase)).Success)
            return $"{ColorKo(m.Groups["a"].Value)}과 {ColorKo(m.Groups["b"].Value)} {m.Groups["n"].Value}색의 자신의 디지몬이 등장했을 때,";
        // 공식 (BT12-005): "《세이브》가 기술되어 있는 자신의 디지몬이 등장했을 때,"
        if ((m = Regex.Match(ev, @"^you play (?:an?|1) Digimon with ＜(?<k>[^＞]+)＞ in (?:its|their) texts?$", RegexOptions.IgnoreCase)).Success && KeywordKo(m.Groups["k"].Value, withReminder: false) is { } kk)
            return $"{kk}{J(kk.Trim('《', '》'), "이", "가")} 기술되어 있는 자신의 디지몬이 등장했을 때,";
        // 공식 (BT13-030): "특징으로 「로얄 나이츠」를 가진 자신의 디지몬 또는 블루인 자신의 테이머가 등장했을 때,"
        if ((m = Regex.Match(ev, $@"^you play (?:an?|1) Digimon (?<with>with .+?) or (?:an?|1) (?<col>{Colors}) Tamer$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } dw)
            return $"{dw} 자신의 디지몬 또는 {ColorKo(m.Groups["col"].Value)}인 자신의 테이머가 등장했을 때,";
        // 공식 (BT17-044): "다른 「에오스몬」이 등장했을 때,"
        if ((m = Regex.Match(ev, $@"^another (?<x>{TargetSpec.NameList}) is played$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"다른 {x}{J(x, "이", "가")} 등장했을 때,"; }
        // 공식 (BT17-069): "「펄스몬」이 기술되어 있거나 특징으로 「SoC」를 가진 자신의 디지몬/테이머가 등장했을 때,"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your Digimon or Tamers (?:that|which) (?:have|has) the (?<t>{TargetSpec.NameList}) trait or (?<x>{TargetSpec.NameList}) in (?:its|their) texts? (?:is|are) played$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value), t = NamesKo(m.Groups["t"].Value, trait: true);
            return $"{x}{J(x, "이", "가")} 기술되어 있거나 특징으로 {t}{J(t, "을", "를")} 가진 자신의 디지몬/테이머가 등장했을 때,";
        }
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<x>{TargetSpec.NameList})s? (?:is|are) played or (?:suspends?|becomes? suspended)$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"자신의 {x}{J(x, "이", "가")} 등장하거나 레스트했을 때,"; }
        // 공식 (BT17-043): "자신의 「테리어몬」/「로프몬」 또는 그린인 테이머가 효과로 등장했을 때,"
        if ((m = Regex.Match(ev, $@"^(?:an effect plays|effects play) (?:one of |any of )?your (?<x>{TargetSpec.NameList}) or (?<col>{Colors}) Tamers?$", RegexOptions.IgnoreCase)).Success)
            return $"자신의 {NamesKo(m.Groups["x"].Value)} 또는 {ColorKo(m.Groups["col"].Value)}인 테이머가 효과로 등장했을 때,";
        // 공식 (ST19-14): "자신의 토큰 또는 특징으로 「퍼펫형」을 가진 자신의 디지몬이 효과로 등장했을 때,"
        if ((m = Regex.Match(ev, @"^(?:an effect plays|effects play) (?:one of |any of )?your Tokens? or (?:an? )?Digimon (?<with>with .+)$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } tw)
            return $"자신의 토큰 또는 {tw} 자신의 디지몬이 효과로 등장했을 때,";
        // 공식 (EX6-061): "상대의 디지몬 또는 특징으로 「7대마왕」을 가진 자신의 디지몬이 등장했을 때,"
        if ((m = Regex.Match(ev, @"^(?:an|one of your) opponent's Digimon or (?:one of |any of )?your Digimon (?<with>with .+?) (?:is|are) played$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } ow)
            return $"상대의 디지몬 또는 {ow} 자신의 디지몬이 등장했을 때,";
        // 공식 (BT18-010): "자신의 디지몬/테이머가 특징으로 「하이브리드체」/「10투사」를 가진 디지몬으로 진화했을 때,"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your Digimon(?<tam> or Tamers)? digivolves? into (?:an? |1 )?Digimon(?<card> card)? (?<with>with .+)$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } iw)
            return $"자신의 디지몬{(m.Groups["tam"].Success ? "/테이머" : "")}{(m.Groups["tam"].Success ? "가" : "이")} {iw} {(m.Groups["card"].Success ? "디지몬 카드로" : "디지몬으로")} 진화했을 때,";
        // 공식 (BT9-090): "자신의 디지몬이 블랙을 포함하는 2색의 디지몬 카드로 진화할 때,"
        if ((m = Regex.Match(ev, $@"^(?<who>(?:one|any) of your Digimon|this Digimon) would digivolve into (?:an? |1 )?(?<n>\d+)-colou?r (?<c>{Colors}) Digimon(?: card)?$", RegexOptions.IgnoreCase)).Success)
        {
            string c = ColorKo(m.Groups["c"].Value);
            return $"{(m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase) ? "이 디지몬이" : "자신의 디지몬이")} {c}{J(c, "을", "를")} 포함하는 {m.Groups["n"].Value}색의 디지몬 카드로 진화할 때,";
        }
        // 공식 (BT15-032): "진화원 매수가 이 디지몬 이하의 상대의 디지몬이 어택했을 때,"
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon with as many or fewer digivolution cards as this Digimon attacks$", RegexOptions.IgnoreCase))
            return "진화원 매수가 이 디지몬 이하의 상대의 디지몬이 어택했을 때,";
        // 공식 (BT18-090): "이 디지몬이 배틀에서 상대의 디지몬을 소멸시켰을 때,"
        if (Regex.IsMatch(ev, @"^this Digimon deletes (?:an|your) opponent's Digimon (?:by|in) battle$", RegexOptions.IgnoreCase))
            return "이 디지몬이 배틀에서 상대의 디지몬을 소멸시켰을 때,";
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>[^,]*?Digimon(?: with [^,]+?)?) wins? (?:a|the) battle$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["subj"].Value, bare: true) is { } wk)
            return $"{wk}{J(wk, "이", "가")} 배틀에서 이겼을 때,";
        if ((m = Regex.Match(ev, $@"^this Digimon with (?<x>{TargetSpec.NameList}) in its text or the (?<t>{TargetSpec.NameList}) trait wins (?:a|the) battle$", RegexOptions.IgnoreCase)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value), t = NamesKo(m.Groups["t"].Value, trait: true);
            return $"{x}{J(x, "이", "가")} 기술되어 있거나 특징으로 {t}{J(t, "을", "를")} 가진 이 디지몬이 배틀에서 이겼을 때,";
        }
        return null;
    }

    /// <summary>떠나려 하는 자신의 디지몬 → "자신의 「아포카리몬」 또는 특징으로 「어둠의 4천왕」을 가진 자신의 디지몬" · "자신의 「이오스몬」"</summary>
    private static string? LeaveSubjectKo32(string subj)
    {
        Match m;
        subj = subj.Trim();
        if ((m = Regex.Match(subj, $@"^(?<x>{TargetSpec.NameList}) or (?<rest>(?:[^,]*? )?Digimon with .+)$", RegexOptions.IgnoreCase)).Success
            && TargetKo("all of your " + m.Groups["rest"].Value, bare: true) is { } rest)
            return $"자신의 {NamesKo(m.Groups["x"].Value)} 또는 {rest}";
        if ((m = Regex.Match(subj, $@"^(?<other>other )?(?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["other"].Success ? "다른 " : "")}자신의 {NamesKo(m.Groups["x"].Value)}";
        return TargetKo("all of your " + subj, bare: true);
    }

    /// <summary>비용 (CostKo 끝에서 본다)</summary>
    private static string? CostKo32(string cost)
    {
        Match m;
        if (Regex.IsMatch(cost, @"^trashing 2 (?:same-level cards|cards of the same level|cards with the same level) (?:from|in) (?:its|this Digimon's) digivolution cards$", RegexOptions.IgnoreCase))
            return "이 디지몬의 진화원에서 Lv.이 같은 카드 2장을 파기하는 것으로,";
        // 공식 (BT11-111 · BT13-075 · BT21-022): "이 디지몬의 진화원에서 「벰몬」 4장을 덱 아래로 되돌리는 것으로," · "~ 디지몬 카드 3장을 파기하는 것으로,"
        if ((m = Regex.Match(cost, @"^(?<v>returning|placing|trashing) (?<pick>.+?) (?:from|in) (?:its|this Digimon's) digivolution cards(?<deck> (?:to|at) the bottom of (?:the|your|its owner's|their owners'?) decks?)?$", RegexOptions.IgnoreCase)).Success
            && (m.Groups["v"].Value.Equals("trashing", StringComparison.OrdinalIgnoreCase) != m.Groups["deck"].Success)
            && SourcePickKo32(m.Groups["pick"].Value) is { } pk)
            return $"이 디지몬의 진화원에서 {pk}{J(pk, "을", "를")} {(m.Groups["deck"].Success ? "덱 아래로 되돌리는" : "파기하는")} 것으로,";
        if (Regex.IsMatch(cost, @"^trashing (?:the bottom card of your security stack|your bottom security card)$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티를 아래에서부터 1장 파기하는 것으로,";
        if (Regex.IsMatch(cost, @"^returning (?:your top security card|the top card of your security stack) to the bottom of (?:the|your) deck$", RegexOptions.IgnoreCase))
            return "자신의 시큐리티를 위에서부터 1장 덱 아래로 되돌리는 것으로,";
        if (Regex.IsMatch(cost, @"^trashing both players' top security cards?$", RegexOptions.IgnoreCase))
            return "서로의 시큐리티를 위에서부터 1장 파기하는 것으로,";
        // "~ to the bottom of your deck in any order" — 공식 (RB1-016): "자신의 트래시에서 「젤리몬」이 기술되어 있는 카드 3장을 덱 아래로 되돌리는 것으로,"
        if ((m = Regex.Match(cost, @"^(?<head>.+) in any order$", RegexOptions.IgnoreCase)).Success)
            return CostKo(m.Groups["head"].Value);
        // 공식 (BT8-112): "자신의 트래시에서 화이트인 Lv.7의 디지몬 카드 1장을 덱 아래로 되돌리는 것으로,"
        if ((m = Regex.Match(cost, @"^returning (?<pick>\d+ .+?) from your trash to the (?<pos>top|bottom) of (?:the|your) deck$", RegexOptions.IgnoreCase)).Success
            && PickKo(m.Groups["pick"].Value) is { } tk)
            return $"자신의 트래시에서 {tk}{J(tk, "을", "를")} 덱 {(m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) ? "위" : "아래")}로 되돌리는 것으로,";
        return null;
    }

    /// <summary>진화원에서 고르는 카드 → "「벰몬」 4장" · "특징으로 「X항체」/「로얄 나이츠」를 가진 카드 1장" · "2색의 카드 1장"</summary>
    private static string? SourcePickKo32(string pick)
    {
        pick = Regex.Replace(pick.Trim(), @"\[(?<a>[^\]]+)\] trait or the \[(?<b>[^\]]+)\] trait\b", "[${a}] or [${b}] trait", RegexOptions.IgnoreCase);
        Match m;
        if ((m = Regex.Match(pick, $@"^(?<n>\d+) (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
            return $"{NamesKo(m.Groups["x"].Value)} {m.Groups["n"].Value}장";
        // 공식 (BT8-112): "2색의 카드 1장"
        if ((m = Regex.Match(pick, @"^(?<n>\d+) (?<c>\d+)-colou?r card$", RegexOptions.IgnoreCase)).Success)
            return $"{m.Groups["c"].Value}색의 카드 {m.Groups["n"].Value}장";
        // 공식 (BT13-075): "특징으로 「X항체」/「로얄 나이츠」를 가진 카드 1장" (진화원 안 — 디지몬 카드도 "카드")
        if ((m = Regex.Match(pick, $@"^(?<n>\d+) Digimon cards? (?<with>with .+)$", RegexOptions.IgnoreCase)).Success && WithKo(m.Groups["with"].Value) is { } dw)
            return $"{dw} 카드 {m.Groups["n"].Value}장";
        return PickKo(pick);
    }

    /// <summary>문장 하나 (SentenceKo31 다음)</summary>
    private static string? SentenceKo32(string s)
    {
        // 막기: "Prevent it" · "Prevent it from leaving play" · "Prevent 1 of those Digimon from leaving" · "1 of those Digimon doesn't leave"
        if (Regex.IsMatch(s, @"^(?:prevent (?:it|that|it from leaving play)|(?<one>prevent (?:1|one) of (?:those|them|the) Digimon from (?:leaving(?: the battle area| play)?|being deleted)|(?:1|one) of (?:those|them|the) Digimon (?:doesn't|does not) leave(?: the battle area)?))$", RegexOptions.IgnoreCase))
        {
            bool one = Regex.IsMatch(s, @"\b(?:1|one) of\b", RegexOptions.IgnoreCase);
            if (_leaveKind == "delret") return "소멸하지 않고, 패/덱으로 되돌아가지 않는다";
            return $"{(one ? "그 디지몬 1마리는" : "그 디지몬은")} {(_leaveKind == "del" || Regex.IsMatch(s, "deleted", RegexOptions.IgnoreCase) ? "소멸하지" : "배틀 에어리어를 벗어나지")} 않는다";
        }
        // 강제 어택 — 공식 (EX10-034): "이 디지몬으로 어택한다"
        if (Regex.IsMatch(s, @"^(?:This Digimon attacks|Attack with this Digimon)$", RegexOptions.IgnoreCase))
            return "이 디지몬으로 어택한다";
        if (s.Contains('"') && GrantKo32(s) is { } grant) return grant;
        if (RevealKo32(s) is { } reveal) return reveal;
        // "You may play 1 ~ from your trash with the play cost reduced by 3" (P-205) — "자신의 트래시에서 ~ 1장을 지불하는 등장 코스트 -3 하여 등장시킬 수 있다"
        if (Regex.Match(s, @"^(?<may>You may )?play (?<p>\d+ .+?) from (?<z>your trash|your hand|your hand or trash) with the play cost reduced by (?<n>\d+)$", RegexOptions.IgnoreCase) is { Success: true } rp
            && PickKo(rp.Groups["p"].Value) is { } rpk)
        {
            string z = rp.Groups["z"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase) ? (rp.Groups["z"].Value.Contains("trash", StringComparison.OrdinalIgnoreCase) ? "자신의 패/트래시에서" : "자신의 패에서") : "자신의 트래시에서";
            return $"{z} {rpk}{J(rpk, "을", "를")} 지불하는 등장 코스트 -{rp.Groups["n"].Value} 하여 {(rp.Groups["may"].Success ? "등장시킬 수 있다" : "등장시킨다")}";
        }
        if (CompoundKo32(s) is { } compound) return compound;
        return null;
    }

    /// <summary>두 동작을 한 문장에 쓴 것 (SentenceParser.Compound32와 같은 범위 — Sentence가 읽지 못할 때만): "~를 레스트시키고, ~" · "~. ~"</summary>
    private static string? CompoundKo32(string s)
    {
        if (!Regex.IsMatch(s, @"^(?:Suspend|Unsuspend|Delete|Return|Trash|＜De-Digivolve)\b", RegexOptions.IgnoreCase)) return null;
        foreach (Match sep in Regex.Matches(s, @"(?:,? and then|, then|, and|\.) "))
        {
            string left = s[..sep.Index].Trim(), right = s[(sep.Index + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            if (!Regex.IsMatch(right, @"^(?:(?:up to )?\d+|all) of |^(?:this Digimon|it|that Digimon|they|those Digimon|this Tamer) |^(?:gain|suspend|unsuspend|delete|return|trash|＜Draw)\b", RegexOptions.IgnoreCase)) continue;
            bool period = sep.Value.StartsWith('.');
            if (Action(left, period ? Form.Plain : Form.And) is not { } a) continue;
            if (Sentence(Cap(right), nested: true) is not { } b) continue;
            return period ? $"{a}. {b}" : $"{a}, {b}";
        }
        return null;
    }

    /// <summary>
    /// 효과를 준다 — 공식 (EX10-034 · BT15-068 · EX5-048 · EX8-012): "상대의 턴 종료 시까지 상대의 디지몬 1마리에게 「【자신의 메인 페이즈 개시 시】이 디지몬으로 어택한다.」의 효과를 준다" ·
    /// "~ 「~」의 효과를 주고, DP -3000" · "상대의 턴 종료까지 이 디지몬은 「【소멸 시】~」의 효과를 얻는다"
    /// </summary>
    private static string? GrantKo32(string s)
    {
        if (Regex.Match(s, @"^(?<d>until [^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase) is { Success: true } lead)
            s = $"{lead.Groups["rest"].Value} {lead.Groups["d"].Value.ToLowerInvariant()}";
        var (dur, core) = SplitDuration(s);
        var g = Regex.Match(core, "^(?:give (?<t>.+?)(?: (?<kws>＜[^＞]+＞(?:\\s*,?\\s*(?:and\\s*)?＜[^＞]+＞)*) and)? \"(?<q>[^\"]+)\"|(?<t>.+?) (?:(?:gets? (?<sign>[+-])(?<n>\\d+) DP|gains? (?<kws>＜[^＞]+＞(?:\\s*,?\\s*(?:and\\s*)?＜[^＞]+＞)*)) and )?(?:gains?|gets?) \"(?<q>[^\"]+)\")$", RegexOptions.IgnoreCase);
        if (!g.Success) return null;
        string t = g.Groups["t"].Value;
        bool self = Regex.IsMatch(t, @"^(?:this Digimon|it)$", RegexOptions.IgnoreCase);
        string? who = self ? "이 디지몬" : TargetKo(t);
        if (who == null) return null;
        // 안쪽 효과 문장 (절 하나로 그린다 — 바깥 절의 상태는 되돌린다)
        bool mentioned = _mentionedTarget; string? leave = _leaveKind;
        string? inner;
        try { inner = RenderClause(g.Groups["q"].Value); }
        finally { _mentionedTarget = mentioned; _leaveKind = leave; }
        if (string.IsNullOrEmpty(inner)) return null;
        inner = inner.Trim();
        if (!inner.EndsWith('.')) inner += ".";
        string kw = "";
        if (g.Groups["kws"].Success)
        {
            var parts = Regex.Matches(g.Groups["kws"].Value, @"＜([^＞]+)＞").Select(x => KeywordKo(x.Groups[1].Value, withReminder: false)).ToList();
            if (parts.Any(p => p == null)) return null;
            kw = string.Join("과 ", parts) + "과 ";
        }
        string head = dur != null ? dur + " " : "";
        bool enemy = Regex.IsMatch(t, @"\b(?:your opponent's|their)\b", RegexOptions.IgnoreCase);
        string tail = g.Groups["n"].Success ? $"의 효과를 주고, DP {g.Groups["sign"].Value}{g.Groups["n"].Value}" : "의 효과를 준다";
        if (enemy) return $"{head}{who}에게 {kw}「{inner}」{tail}";
        string gain = g.Groups["n"].Success ? $"의 효과를 얻고, DP {g.Groups["sign"].Value}{g.Groups["n"].Value}" : "의 효과를 얻는다";
        return $"{head}{who}{J(who, "은", "는")} {kw}「{inner}」{gain}";
    }

    /// <summary>셈 (CounterKo 끝에서 본다) — 공식 (BT6-029 · BT10-057): "진화원을 갖지 않는 상대 디지몬 1마리당" · "레스트 상태인 자신의 디지몬 2마리마다"</summary>
    private static string? CounterKo32(string counter)
    {
        Match m;
        if ((m = Regex.Match(counter, @"^(?<n>\d+) (?<other>other )?(?<sus>suspended )?Digimon you have(?: in play)?$", RegexOptions.IgnoreCase)).Success)
            return $"{(m.Groups["sus"].Success ? "레스트 상태인 " : "")}{(m.Groups["other"].Success ? "다른 " : "")}자신의 디지몬 {m.Groups["n"].Value}마리마다";
        if (Regex.IsMatch(counter, @"^Digimon with no digivolution cards your opponent has in play$", RegexOptions.IgnoreCase))
            return "진화원을 갖지 않는 상대 디지몬 1마리당";
        if ((m = Regex.Match(counter, $@"^(?<other>other )?Digimon with (?<x>{TargetSpec.NameList}) in (?:its|their) names? in play$", RegexOptions.IgnoreCase)).Success)
        { string x = NamesKo(m.Groups["x"].Value); return $"명칭에 {x}{J(x, "을", "를")} 포함하는 {(m.Groups["other"].Success ? "다른 " : "")}디지몬 1마리마다"; }
        return null;
    }

    /// <summary>조건 (CondKo 끝에서 본다) — 공식 (BT23-044 · ST22-14): "「카미시로 유우코」 또는 특징 「CS」를 가진 디지몬이 있다면," · "상대의 패 또는 트래시가 10장 이상이라면,"</summary>
    private static Cond? CondKo32(string cond)
    {
        Match m;
        if ((m = Regex.Match(cond, $@"^you have (?<x>{TargetSpec.NameList}) or (?:a|an) (?<t>{TargetSpec.NameList}) trait Digimon$", RegexOptions.IgnoreCase)).Success)
        {
            string t = NamesKo(m.Groups["t"].Value, trait: true);
            return Have($"자신의 {NamesKo(m.Groups["x"].Value)} 또는 특징으로 {t}{J(t, "을", "를")} 가진 자신의 디지몬이");
        }
        if ((m = Regex.Match(cond, @"^(?:your opponent|they) (?:has|have) (?<n>\d+) or more cards in either their hand or (?:their )?trash$", RegexOptions.IgnoreCase)).Success)
            return Be($"상대의 패 또는 트래시가 {m.Groups["n"].Value}장 이상");
        if (Regex.IsMatch(cond, @"^(?:it's )?during (?:an|the|this) attack$", RegexOptions.IgnoreCase))
            return new("어택 중일 때,", "어택 중인 동안,", "어택 중이라면,");
        return null;
    }

    /// <summary>
    /// 덱 위 공개 → 공개한 카드로 진화 · 옵션 사용 — 공식 (BT7-049 · BT9-104 · EX7-048): "그중 그린인 Lv.6의 카드 1장으로, 이 디지몬에서 코스트를 지불하지 않고 진화할 수 있다." ·
    /// "그중 특징으로 「3총사」를 가진 옵션 카드 1장을 코스트를 지불하지 않고 사용할 수 있다."
    /// </summary>
    private static string? RevealKo32(string s)
    {
        var m = Regex.Match(s, @"^Reveal (?:the top (?<n>\d+) cards? of your deck|(?<n>\d+) cards? from the top of your deck)\.? (?:(?:You may )?digivolve (?<who>this card|this Digimon|1 of your Digimon) into|(?<who>this card|this Digimon|1 of your Digimon) may digivolve into|(?<use>You may use|Use)) (?<p>.+?)(?: (?:from )?among them)? without (?:paying )?(?:its|the) (?:memory |digivolution |use )?cost\.? (?<rest>.+?)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string p = Regex.Replace(m.Groups["p"].Value.Trim(), @"^an? ", "1 ", RegexOptions.IgnoreCase);
        if (!Regex.IsMatch(p, @"^\d+ ")) p = "1 " + p;
        if (PickKo(p) is not { } pk) return null;
        string rest = m.Groups["rest"].Value.TrimEnd('.');
        string restKo = Regex.IsMatch(rest, @"^Trash the (?:rest|remaining cards)$", RegexOptions.IgnoreCase) ? "나머지는 파기한다"
            : Regex.IsMatch(rest, @"\btop or (?:the )?bottom\b", RegexOptions.IgnoreCase) ? "나머지는 덱 위 또는 아래로만 되돌린다"
            : Regex.IsMatch(rest, @"top of (?:the |your )?deck", RegexOptions.IgnoreCase) ? "나머지는 원하는 순서대로 덱 위로 되돌린다"
            : "남은 카드는 원하는 순서대로 덱 아래로 되돌린다";
        string mid = m.Groups["use"].Success
            ? $"그중 {pk}{J(pk, "을", "를")} 코스트를 지불하지 않고 사용할 수 있다."
            : m.Groups["who"].Value.StartsWith("1", StringComparison.Ordinal)
                ? $"그중 {pk}{J(pk, "으로", "로")} 자신의 디지몬 1마리를 코스트를 지불하지 않고 진화시킬 수 있다."
                : $"그중 {pk}{J(pk, "으로", "로")}, 이 디지몬에서 코스트를 지불하지 않고 진화할 수 있다.";
        return $"자신의 덱 위에서부터 {m.Groups["n"].Value}장 오픈한다. {mid} {restKo}";
    }
}
