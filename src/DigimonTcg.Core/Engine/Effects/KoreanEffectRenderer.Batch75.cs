using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.75.0 미지원 문장 49차 묶음 — 한글 표시</summary>
public static partial class KoreanEffectRenderer
{
    private const RegexOptions I75 = RegexOptions.IgnoreCase;

    /// <summary>
    /// v0.75.0 용어집이 카드 번호로 맞추지 못한 이름 · 이름 조각 — 공식 한글 효과문에서 확인한 표기 (공식 한글 데이터가 있을 때만):
    /// 「시스터몬 느와르(각성)」 (공식 BT7-083 — 이 이름을 쓰는 카드는 BT7-083뿐. 영어판은 「느와르」/「시엘」 이름이 일본 · 한국판과 서로 바뀌어 있어 [Sistermon Noir]만은 넣지 않는다: 영어 [Sistermon Ciel] = 공식 한글 「시스터몬 느와르」 BT6-084), 「위그드라실」 (공식 BT23-072 "명칭에 「위그드라실」을 포함하는"), 토큰 「볼레＆제드루켄」 (공식 EX7-058)
    /// </summary>
    private static readonly Dictionary<string, string> ExtraNames75 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sistermon Noir (Awakened)"] = "시스터몬 느와르(각성)",
        ["King Drasil"] = "위그드라실", ["Volée & Zerdrücken"] = "볼레＆제드루켄",
    };

    /// <summary>한글 머리말 (【자신의 턴】 [턴에 1회] 등)</summary>
    private static readonly Regex KoHeader75 = new(@"^(?:\s*(?:【[^】]+】|\[[^\]]+\]|〔[^〕]+〕))+\s*", RegexOptions.Compiled);

    /// <summary>v0.75.0 원래 규칙이 옮기지 못한 절만 (EX4-014)</summary>
    private static string? RenderClause75(string rawClause)
    {
        string t = rawClause.Trim();
        // EX4-014 "[Your Turn] [Once Per Turn] When A, X. When B, Y." — 머리말은 한 번 (공식 한글 EX4-014도 머리말 하나 아래 두 문장)
        if (SentenceParser.SplitTwoWhen75(t) is { } sp
            && Render75(sp.First) is { } k1 && Render75(sp.Second) is { } k2
            && KoHeader75.Match(k2) is { Success: true, Length: > 0 } h2)
            return k1 + " " + k2[h2.Length..];
        // BT1-089 "~, hatch 1 Digi-Egg card to an empty space in your breeding area, or move 1 level 3 or higher Digimon from your breeding area to your battle area."
        //  — 공식 한글 BT1-089 문형 ("~ 부화시키거나, ~ 이동한다")
        Match mh;
        if ((mh = Regex.Match(t, @"^(?<head>.+?)hatch (?:1|a) Digi-Egg(?: card)? (?:to|in|into) (?:an empty space in )?your breeding area, or move 1 level (?<lv>\d+) or higher Digimon from your breeding area to your battle area\.?$", I75)).Success
            && RenderClause(mh.Groups["head"].Value + "hatch 1 Digi-Egg card to an empty space in your breeding area.") is { } hko && hko.EndsWith("부화시킨다.", StringComparison.Ordinal))
            return hko[..^"부화시킨다.".Length] + $"부화시키거나, 육성 에어리어의 Lv.{mh.Groups["lv"].Value} 이상인 자신의 디지몬 1마리를 배틀 에어리어로 이동한다.";
        // EX2-007 "[Main] [Once Per Turn] If you don't have another [X] in play, place 1 of your [Y]s from in play or your hand under this Digimon as its bottom digivolution card."
        //  — 공식 한글 EX2-007 문형 ("자신의 다른 「X」가 없을 때")
        Match m;
        if ((m = Regex.Match(t, @"^(?<h>(?:\s*\[[^\]]+\])+)\s*(?:If you don't have another (?<n>\[[^\]]+\]) in play, )?place 1 of your (?<x>\[[^\]]+\])s? from in play or (?:from )?your hand under this Digimon as its bottom digivolution card\.?$", I75)).Success
            && RenderClause(m.Groups["h"].Value.Trim() + " ＜Draw 1＞.") is { } hk && KoHeader75.Match(hk) is { Success: true, Length: > 0 } hh)
        {
            string x = NamesKo(m.Groups["x"].Value);
            string cond = "";
            if (m.Groups["n"].Success) { string n = NamesKo(m.Groups["n"].Value); cond = $"자신의 다른 {n}{J(n, "이", "가")} 없을 때, "; }
            return $"{hk[..hh.Length].TrimEnd()} {cond}자신의 {x} 1마리 또는 패의 {x} 1장을 이 디지몬의 진화원의 가장 아래에 놓는다.";
        }
        return null;
    }

    /// <summary>원래 규칙 + "card with DigiXros requirements"(디지크로스 조건을 가진 카드)</summary>
    private static string? Render75(string clause)
    {
        if (RenderClause(clause) is { } ko) return ko;
        const string ph = "Qzxros75";
        string alt = Regex.Replace(clause, @"\bcard with (?:a )?DigiXros requirements?\b", $"card with the [{ph}] trait", I75);
        if (alt == clause || RenderClause(alt) is not { } k || !k.Contains($"특징으로 「{ph}」를 가진", StringComparison.Ordinal)) return null;
        return k.Replace($"특징으로 「{ph}」를 가진", "디지크로스 조건을 가진");
    }
}
