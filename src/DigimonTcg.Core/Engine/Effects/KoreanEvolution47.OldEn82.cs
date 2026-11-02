using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.82.0 공식 영어 카드 리스트의 옛 표기 특수 진화 조건 "[Digivolve] 0 from [Agumon]" · "[Digivolve] 2 if name contains [Dragon Mode]"
/// · "[Digivolve] 5 from Lv.6 w/[Royal Knight] in traits" (BT8 ~ BT13 · EX3 · EX4 · RB1 · P-072 등 — 지금 표기 "[Digivolve] [Agumon]: Cost 0"과 같은 조건, 종합 룰 2-3-5-3).
/// 한글 데이터(data/kr)가 없는 PC에서는 〔진화〕 조건을 공식 영어로 읽는데 (English48), 옛 표기는 읽지 못해 이 카드들의 특수 진화가 없었다.
/// 옛 표기를 지금 표기로 바꿔 같은 규칙으로 읽는다. 공식 한글 〔진화〕 조건과 같은지 scratchpad에서 모든 카드로 대조했다 (cov82 evocmp).
/// 영어 데이터도 없으면 지금처럼 특수 진화 없음.
/// </summary>
public static partial class KoreanEvolution47
{
    private static readonly Regex OldEnHead82 = new(@"^\[Digivolve\]\s*(?<rest>\d+\s+(?:from|if)\b.*)$", IC);
    private static readonly Regex OldEnPart82 = new(@"^(?<n>\d+)\s+(?:from\s+(?<c>.+)|(?<ifn>if name contains\s+.+))$", IC);

    /// <summary>옛 표기 "[Digivolve] N from X(/M from Y)" → "[Digivolve] X: Cost N/Y: Cost M" (옛 표기가 아니면 그대로)</summary>
    internal static string OldEnglishToNew82(string line)
    {
        var h = OldEnHead82.Match(line.Trim());
        if (!h.Success) return line;
        var parts = Regex.Split(h.Groups["rest"].Value.Trim(), @"\s*/\s*(?=\d+\s+(?:from|if)\b)", IC);
        var outs = new List<string>();
        foreach (var p in parts)
        {
            var m = OldEnPart82.Match(p.Trim());
            if (!m.Success) return line;
            string cond = m.Groups["ifn"].Success ? m.Groups["ifn"].Value.Trim() : m.Groups["c"].Value.Trim();
            outs.Add(OldEnglishCond82(cond) + ": Cost " + m.Groups["n"].Value);
        }
        return "[Digivolve] " + string.Join("/", outs);
    }

    private static string OldEnglishCond82(string c)
    {
        c = c.Trim();
        // "Koromon" (낫표 없는 이름 — BT12-034)
        if (Regex.IsMatch(c, @"^[A-Z][A-Za-z0-9:\- ]*$") && !c.StartsWith("Lv.", StringComparison.Ordinal)) return "[" + c + "]";
        // "Lv.4 if name contains [Growlmon]" → "Lv.4 w/[Growlmon] in name"
        c = Regex.Replace(c, $@"^Lv\.(?<lv>\d+)\s+if name contains\s+(?<n>{ENL})$", "Lv.${lv} w/${n} in name", IC);
        // "if name contains [X]" → "Name contains [X]" (레벨 없음)
        c = Regex.Replace(c, $@"^if name contains\s+(?<n>{ENL})$", "Name contains ${n}", IC);
        // "Lv.2 w/[Xros Heart] in traits" → "Lv.2 w/[Xros Heart] trait"
        c = Regex.Replace(c, $@"^Lv\.(?<lv>\d+) w/(?<t>{ENL}) in traits?$", "Lv.${lv} w/${t} trait", IC);
        // "yellow, green, or purple Lv.4 w/＜Save＞ text" → "yellow/green/purple Lv.4 w/＜Save＞ in text"
        c = Regex.Replace(c, $@"^(?<cs>{ColE}(?:\s*,\s*(?:or\s+)?{ColE})*)\s+(?=Lv\.)", m => Regex.Replace(m.Groups["cs"].Value, @"\s*,\s*(?:or\s+)?", "/") + " ", IC);
        c = Regex.Replace(c, @"w/＜(?<k>[^＞]+)＞\s+(?:in\s+)?text$", "w/＜${k}＞ in text", IC);
        return c;
    }

    // 옛 표기에만 있는 조건 모양 (ParseGroupEn이 AtomsEn 앞에서 먼저 본다)
    private static readonly (Regex Re, AtomBuilderEn Build)[] AtomsOldEn82 =
    {
        // Name contains [X] — 레벨 없는 명칭 포함 (공식 한글 "명칭에 「X」을 포함")
        (new Regex($@"^Name contains (?<n>{ENL})", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); return (c, _) => ns.Any(x => CardNames.Contains(c, x)); }),
        // (yellow/green/purple) Lv.N w/＜Save＞ in text — 키워드가 적힌 (공식 한글 "《세이브》가 기술되어 있는")
        (new Regex($@"^(?:(?<c>{ColsE})\s+)?Lv\.(?<lv>\d+) w/＜(?<k>[^＞]+)＞ in text", IC),
            m => { string k = m.Groups["k"].Value.Trim(); var cs = ColorsEn(m); return (c, _) => IsLvEn(c, m) && ColorOk(c, cs) && KeywordWrittenEn82(c, k); }),
        // Lv.N w/[X] in name or 2-color w/green (공식 한글 "명칭에 「X」을 포함하거나 그린을 포함하는 2색의 Lv.N")
        (new Regex($@"^Lv\.(?<lv>\d+) w/(?<n>{ENL}) in name or (?<k>\d+)-color w/(?<c1>{ColE})", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); int k = int.Parse(m.Groups["k"].Value); var col = EnglishCardRules.ColorOf(m.Groups["c1"].Value);
                   return (c, _) => IsLvEn(c, m) && (ns.Any(x => CardNames.Contains(c, x)) || (c.AllColors.Count == k && col is { } cc && c.AllColors.Contains(cc))); }),
        // Lv.N 2-color w/green (공식 한글 "그린을 포함하는 2색의 Lv.N")
        (new Regex($@"^Lv\.(?<lv>\d+) (?<k>\d+)-color w/(?<c1>{ColE})", IC),
            m => { int k = int.Parse(m.Groups["k"].Value); var col = EnglishCardRules.ColorOf(m.Groups["c1"].Value);
                   return (c, _) => IsLvEn(c, m) && c.AllColors.Count == k && col is { } cc && c.AllColors.Contains(cc); }),
        // [Y] w/[X] digivolution card (공식 한글 "진화원에 「X」이 있는 「Y」")
        (new Regex($@"^(?<n>{ENL}) w/\s*(?<s>{ENL}) digivolution card", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); var ss = NamesEn(m.Groups["s"].Value);
                   return (c, d) => d != null && ns.Any(x => CardNames.Is(c, x)) && d.FaceUpSources.Any(s => ss.Any(x => CardNames.Is(s, x))); }),
    };

    /// <summary>영어 카드 문구에 ＜X＞가 적혀 있다 (주석 괄호 밖 — 종합 룰 4-23)</summary>
    private static bool KeywordWrittenEn82(CardData c, string k) =>
        $"{c.EffectText}{c.InheritedEffectText}".Contains($"＜{k}＞", StringComparison.OrdinalIgnoreCase);

    /// <summary>옛 표기 조건 하나 (맞으면 조건과 길이)</summary>
    private static (Func<CardData, CardInstance?, bool> Match, int Length, string Text)? OldAtomEn82(string rest)
    {
        foreach (var (re, build) in AtomsOldEn82)
        {
            var m = re.Match(rest);
            if (!m.Success || m.Length == 0) continue;
            string after = rest[m.Length..].TrimStart();
            if (!AtomEnds(ref after)) continue;
            return (build(m), m.Length, m.Value.Trim());
        }
        return null;
    }
}
