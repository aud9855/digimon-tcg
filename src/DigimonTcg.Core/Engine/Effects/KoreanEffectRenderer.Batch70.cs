using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.70.0 미지원 문장 44차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>
    /// v0.70.0 용어집(공식 한글 카드 데이터의 카드 번호 맞추기)이 옮기지 못한 특징 · 이름 조각 — 공식 한글 특징에서 확인한 표기:
    /// 「Animal」을 포함하는 특징은 Dark Animal(마수형)뿐 (Sea Animal은 문장이 뺀다) · 「Sovereign」은 Four Sovereigns(4성수)뿐 ·
    /// 「Plant」는 식충식물형 · 고대식물형 (공통 「식물」) · 「saur」는 공룡형 · 갑룡형 · 수장룡형 (공식 EX3-006 「용」 또는 「룡」) ·
    /// 어플몬 특징 Creation (App Name) = 창조 · Awakening = 각성 · Invincible = 무적 · 공식 BT4-093 명칭에 「가오」
    /// </summary>
    private static readonly Dictionary<string, string> ExtraTerms70 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Animal"] = "마수형", ["Sovereign"] = "4성수", ["Plant"] = "식물", ["saur"] = "룡",
        ["Creation"] = "창조", ["Awakening"] = "각성", ["Invincible"] = "무적",
        ["Composite"] = "합성형", ["Wicked God"] = "사신형", ["Reptile"] = "파충류형", ["Dragonkin"] = "용인형", ["Evil"] = "소악마형",
        ["DigiPolice"] = "디지대", ["SEEKERS"] = "시커즈", ["Dark Dragon"] = "마룡형", ["Evil Dragon"] = "사룡형", ["Fallen Angel"] = "타천사형",
        ["Gao"] = "가오",
    };

    /// <summary>원래 규칙이 옮기지 못한 절만</summary>
    private static string? RenderClause70(string rawClause)
    {
        string t = rawClause.Trim();
        // EX14-012 "[Security] [On Deletion] You may use this card without paying the cost." — 공식 표기 "코스트를 지불하지 않고 사용할 수 있다"
        if (Regex.Match(t, @"^(?<lab>(?:\[[^\]]+\]\s*)+)You may use this card without paying (?:the|its) cost\.?$", RegexOptions.IgnoreCase) is { Success: true } m
            && RenderClause(m.Groups["lab"].Value + "Gain 1 memory.") is { } ko && ko.EndsWith("메모리를 +1 한다."))
            return ko[..^"메모리를 +1 한다.".Length] + "이 카드를 코스트를 지불하지 않고 사용할 수 있다.";
        // BT13-099 같은 뜻 다른 표기 — 바꿔 쓴 문장을 앞의 규칙으로. 조건 "서로의 시큐리티 합계가 N장 이하라면" (공식 BT13-099)은 자리표시 조건으로 옮긴 뒤 바꾼다
        string alt = SentenceParser.ClauseRewrite70(t);
        if (alt != t)
        {
            if (RenderClause(alt) is { } ko1) return ko1;
            if (Regex.Match(alt, @"\bIf the total cards in both players' security stacks is (?<n>\d+) or less, ", RegexOptions.IgnoreCase) is { Success: true } sm
                && RenderClause(alt.Remove(sm.Index, sm.Length).Insert(sm.Index, "If you have 97 or less security cards, ")) is { } ko2 && ko2.Contains("자신의 시큐리티가 97장 이하일 때"))
                return ko2.Replace("자신의 시큐리티가 97장 이하일 때", $"서로의 시큐리티 합계가 {sm.Groups["n"].Value}장 이하라면");
        }
        // BT24-085 "~ Option card with as high or lower a use cost as your opponent's memory from your hand ~" — "사용 코스트가 상대의 메모리 이하인"
        //  · ST22-07 "~ as that Digimon's level from under this Tamer ~" — "이 테이머 아래에서 ~ 사용 코스트가 그 디지몬의 Lv. 이하인"
        if (Regex.Match(t, @"\bOption card with as high or lower a (?:use )?cost as (?<ref>your opponent's memory|that Digimon's level) from (?<from>your hand|under this Tamer)\b", RegexOptions.IgnoreCase) is { Success: true } om
            && RenderClause(t.Remove(om.Index, om.Length).Insert(om.Index, "Option card with a cost of 97 or less from your hand")) is { } ko3 && ko3.Contains("코스트 97 이하인") && ko3.Contains("자신의 패에서 "))
        {
            ko3 = ko3.Replace("코스트 97 이하인", om.Groups["ref"].Value.EndsWith("level", StringComparison.OrdinalIgnoreCase) ? "사용 코스트가 그 디지몬의 Lv. 이하인" : "사용 코스트가 상대의 메모리 이하인");
            return om.Groups["from"].Value.StartsWith("under", StringComparison.OrdinalIgnoreCase) ? ko3.Replace("자신의 패에서 ", "이 테이머 아래에서 ") : ko3;
        }
        // EX3-024 "your opponent attacks with 1 of their Digimon" — 공식 "상대는 본인의 디지몬 1마리를 어택시킨다"
        if (Regex.Match(t, @"\byour opponent attacks with (?:1|one) of their Digimon\.?$", RegexOptions.IgnoreCase) is { Success: true } am
            && RenderClause(t[..am.Index] + "gain 1 memory.") is { } ko4 && ko4.EndsWith("메모리를 +1 한다."))
            return ko4[..^"메모리를 +1 한다.".Length] + "상대는 본인의 디지몬 1마리를 어택시킨다.";
        return null;
    }
}
