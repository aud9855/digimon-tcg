using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.48.0 〔진화〕 조건 더 읽기:
///  - 한글 데이터가 없는 카드 (BT24 · BT25 · BT26 · EX11 ~ EX13 · AD1 · ST23 · ST24 · 프로모 등)는 공식 영어 카드 리스트(world.digimoncard.com)의
///    [Special Digivolution Condition] "[Digivolve] Lv.2 w/[TS] trait: Cost 0" 을 같은 조건 모양으로 읽는다 (로컬 data/en/card_rules.json — Data.EnglishCardRules)
///  - 테이머 아래 카드 조건 "특징으로 「X」를 가진 카드가 아래에 N장 있는 「Y」" · "「X」이 아래에 있는 「Y」" · "[Y] w/N or more [X] trait cards under"
///    (종합 룰 4-26-1 "X장 있다" = 그 장수 이상 · 4-7 겹쳐져 있는 카드는 그 카드 아래의 카드). 엔진은 테이머 아래 카드를 테이머마다 나누어 두지 않으므로
///    테이머가 그 1명뿐일 때만 아래 카드를 그 테이머의 것으로 센다 (테이머에서 진화할 때 아래 카드를 진화원으로 옮기는 처리와 같은 기준)
///  - 필드 조건 "특징 「X」를 가진 자신의 테이머가 N명 이상 있는 동안" (BT23-101)
/// </summary>
public static partial class KoreanEvolution47
{
    // ---------------------------------------------------------------- 한글: 테이머 아래 카드 · 테이머 수

    private static readonly Regex UnderTraitKo = new($@"^특징(?:으로|에)?\s*(?<t>{NL})\s*[을를]?\s*가진\s*카드가\s*아래에\s*(?<k>\d+)\s*장\s*있는\s*(?<n>{NL})");
    private static readonly Regex UnderNameKo = new($@"^(?<s>{NL})\s*[이가]\s*아래에\s*있는\s*(?<n>{NL})");

    /// <summary>테이머 아래 카드 조건 하나 (조건 묶음 앞부분) — 맞으면 조건과 나머지 ("/~" 또는 "")</summary>
    private static (Condition Cond, string After)? Extra48(string rest, int cost, Func<StaticContext, bool>? field)
    {
        Match m;
        Func<CardData, bool> under;
        int k;
        if ((m = UnderTraitKo.Match(rest)).Success)
        {
            var ts = Names(m.Groups["t"].Value); k = int.Parse(m.Groups["k"].Value);
            under = c => ts.Any(t => TraitIs(c, t));
        }
        else if ((m = UnderNameKo.Match(rest)).Success)
        {
            var ss = Names(m.Groups["s"].Value); k = 1;
            under = c => ss.Any(x => NameIs(c, x));
        }
        else return null;
        string after = rest[m.Length..].TrimStart();
        if (after.Length > 0 && after[0] != '/') return null;
        var ns = Names(m.Groups["n"].Value);
        return (new Condition(m.Value.Trim(), cost, (c, _) => c.Type == CardType.Tamer && ns.Any(x => NameIs(c, x)), And(field, UnderField(under, k)), true), after);
    }

    /// <summary>테이머 아래에 under 카드가 k장 이상 (앞면만 — 종합 룰 4-7-9 뒷면 카드는 정보를 참조할 수 없다). 테이머가 1명일 때만</summary>
    private static Func<StaticContext, bool> UnderField(Func<CardData, bool> under, int k) =>
        f => f.Owner.Tamers.Count == 1 && f.Owner.FaceUpUnderTamers.Count(under) >= k;

    private static Func<StaticContext, bool> And(Func<StaticContext, bool>? a, Func<StaticContext, bool> b) =>
        a == null ? b : f => a(f) && b(f);

    /// <summary>"특징 「X」를 가진 자신의 테이머가 N명 이상 있는 동안"</summary>
    private static Func<StaticContext, bool>? Field48(string s)
    {
        var m = Regex.Match(s, $@"^특징(?:으로|에)?\s*(?<t>{NL})\s*[을를]?\s*가진\s*자신의\s*테이머가\s*(?<k>\d+)\s*명\s*이상\s*있는\s*동안$");
        if (!m.Success) return null;
        var ts = Names(m.Groups["t"].Value); int k = int.Parse(m.Groups["k"].Value);
        return f => f.Owner.Tamers.Count(t => ts.Any(x => TraitIs(t, x))) >= k;
    }

    // ---------------------------------------------------------------- 영어: [Special Digivolution Condition]

    private const string ENL = @"\[[^\]]+\](?:\s*(?:/|\sor\s)\s*\[[^\]]+\])*";
    private const string ColE = @"(?:red|blue|yellow|green|black|purple|white)";
    private const string ColsE = ColE + @"(?:\s*/\s*" + ColE + ")*";
    private const RegexOptions IC = RegexOptions.IgnoreCase;

    /// <summary>한글 데이터가 없는 카드의 공식 영어 [Digivolve] 조건 (영어 데이터도 없으면 빈 목록)</summary>
    private static Parsed English48(CardData evoCard)
    {
        if (EnglishCardRules.Get(evoCard.CardId) is not { SpecialDigivolve.Count: > 0 } en) return Empty;
        string text = string.Join("\n", en.SpecialDigivolve);
        return Cache.GetOrAdd("EN\n" + text, _ => ParseEnglish(en.SpecialDigivolve));
    }

    internal static Parsed ParseEnglish(IEnumerable<string> lines)
    {
        var conds = new List<Condition>();
        var bad = new List<string>();
        foreach (var line0 in lines)
        {
            string line = line0.Trim();
            if (!line.StartsWith("[Digivolve]", StringComparison.Ordinal)) continue;
            string rest = line["[Digivolve]".Length..].Trim();
            while (rest.Length > 0)
            {
                // v0.49.0 "Cost 1 for each of your security cards" (BT24-101 — 자신의 시큐리티 1장마다 코스트 1, 진화할 때의 장수)
                var per = Regex.Match(rest, @"^(?<cond>.+?)\s*:\s*Cost\s*(?<n>\d+) for each of your security cards?\s*$", IC);
                if (per.Success)
                {
                    int k = int.Parse(per.Groups["n"].Value);
                    if (ParseGroupEn(per.Groups["cond"].Value.Trim(), 0) is { } pl) conds.AddRange(pl.Select(x => x with { CostFn = f => k * f.Owner.SecurityStack.Count }));
                    else bad.Add(rest);
                    break;
                }
                var g = Regex.Match(rest, @"^(?<cond>.+?)\s*:\s*Cost\s*(?<n>\d+)\s*(?<tail>/|$)?");
                if (!g.Success) { bad.Add(rest); break; }
                string cond = g.Groups["cond"].Value.Trim();
                int cost = int.Parse(g.Groups["n"].Value);
                if (!g.Groups["tail"].Success) { bad.Add(rest); break; }   // "Cost 1 for each of your security cards" 같은 코스트는 읽지 않는다
                if (ParseGroupEn(cond, cost) is { } list) conds.AddRange(list);
                else bad.Add(cond + ": Cost " + cost);
                rest = rest[g.Length..].Trim();
            }
        }
        return new Parsed(conds, bad);
    }

    private delegate Func<CardData, CardInstance?, bool> AtomBuilderEn(Match m);

    private static readonly (Regex Re, AtomBuilderEn Build, bool NameAtom)[] AtomsEn =
    {
        // Lv.N w/[A] in text or w/[B] trait
        (new Regex($@"^Lv\.(?<lv>\d+) w/(?<n>{ENL}) in text or w/(?<t>{ENL}) trait", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); var ts = NamesEn(m.Groups["t"].Value); return (c, i59) => IsLvEn(c, m) && (ns.Any(x => TargetSpec.TextHas(c, x)) || ts.Any(t => TraitIsEn59(c, t, i59))); }, false),
        // Lv.N w/[A] in name or w/[B] trait
        (new Regex($@"^Lv\.(?<lv>\d+) w/(?<n>{ENL}) in name or w/(?<t>{ENL}) trait", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); var ts = NamesEn(m.Groups["t"].Value); return (c, i59) => IsLvEn(c, m) && (ns.Any(x => CardNames.Contains(c, x)) || ts.Any(t => TraitIsEn59(c, t, i59))); }, false),
        // Lv.N w/[A] in name and [B] trait
        (new Regex($@"^Lv\.(?<lv>\d+) w/(?<n>{ENL}) in name and (?<t>{ENL}) trait", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); var ts = NamesEn(m.Groups["t"].Value); return (c, i59) => IsLvEn(c, m) && ns.Any(x => CardNames.Contains(c, x)) && ts.Any(t => TraitIsEn59(c, t, i59)); }, false),
        // Lv.N w/[A] or [B] in any trait or w/[C] trait
        (new Regex($@"^Lv\.(?<lv>\d+) w/(?<x>{ENL}) in any trait or w/(?<t>{ENL}) trait", IC),
            m => { var xs = NamesEn(m.Groups["x"].Value); var ts = NamesEn(m.Groups["t"].Value); return (c, i59) => IsLvEn(c, m) && (xs.Any(x => (TargetSpec.HasTrait(c, x) || Gained59(i59, c, x))) || ts.Any(t => TraitIsEn59(c, t, i59))); }, false),
        // Lv.N w/[A] in text
        (new Regex($@"^Lv\.(?<lv>\d+) w/(?<n>{ENL}) in text", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); return (c, _) => IsLvEn(c, m) && ns.Any(x => TargetSpec.TextHas(c, x)); }, false),
        // Lv.N w/[A] in name
        (new Regex($@"^Lv\.(?<lv>\d+) w/(?<n>{ENL}) in name", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); return (c, _) => IsLvEn(c, m) && ns.Any(x => CardNames.Contains(c, x)); }, false),
        // (Yellow/purple · Non-white) Lv.N w/[A]/[B] trait
        (new Regex($@"^(?:(?<c>{ColsE})\s+|Non-(?<nc>{ColE})\s+)?Lv\.(?<lv>\d+) w/(?<t>{ENL}) trait", IC),
            m => { var ts = NamesEn(m.Groups["t"].Value); var cs = ColorsEn(m); return (c, i59) => IsLvEn(c, m) && ColorOk(c, cs) && NonColorOk(c, m) && ts.Any(t => TraitIsEn59(c, t, i59)); }, false),
        // Play cost N [A]
        (new Regex($@"^Play cost (?<pc>\d+) (?<n>{ENL})", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); int pc = int.Parse(m.Groups["pc"].Value); return (c, _) => c.Type == CardType.Digimon && c.PlayCost == pc && ns.Any(x => CardNames.Is(c, x)); }, false),
        // [A] w/o N colors
        (new Regex($@"^(?<n>{ENL}) w/o (?<k>\d+) colors", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); int k = int.Parse(m.Groups["k"].Value); return (c, _) => c.AllColors.Count != k && ns.Any(x => CardNames.Is(c, x)); }, false),
        // [A] · [A]/[B] — 이름 (테이머 이름이면 테이머에서도: 아래에서 디지몬 쪽 · 테이머 쪽 조건 2개로 나눈다)
        (new Regex($@"^(?<n>{ENL})", IC),
            m => { var ns = NamesEn(m.Groups["n"].Value); return (c, _) => ns.Any(x => CardNames.Is(c, x)); }, true),
    };

    private static readonly Regex UnderTraitEn = new($@"^(?<n>{ENL}) w/(?<k>\d+) or more (?<t>{ENL}) trait cards under", IC);

    private static List<Condition>? ParseGroupEn(string group, int cost)
    {
        var list = new List<Condition>();
        string rest = group.Trim();
        Func<StaticContext, bool>? field = null;
        var fm = Regex.Match(rest, @"^While you have (?<f>(?:\[[^\]]*\]|[^\[\],])+),\s*", IC);
        if (fm.Success)
        {
            if ((field = ParseFieldEn(fm.Groups["f"].Value.Trim())) == null) return null;
            rest = rest[fm.Length..];
        }
        while (rest.Length > 0)
        {
            string after;
            var um = UnderTraitEn.Match(rest);
            if (um.Success)
            {
                after = rest[um.Length..].TrimStart();
                if (!AtomEnds(ref after)) return null;
                var ns = NamesEn(um.Groups["n"].Value); var ts = NamesEn(um.Groups["t"].Value); int k = int.Parse(um.Groups["k"].Value);
                list.Add(new Condition(um.Value.Trim(), cost, (c, _) => c.Type == CardType.Tamer && ns.Any(x => CardNames.Is(c, x)),
                    And(field, UnderField(c => ts.Any(t => TraitIsEn(c, t)), k)), true));
                rest = after;
                continue;
            }
            bool ok = false;
            foreach (var atom in AtomsEn)
            {
                var m = atom.Re.Match(rest);
                if (!m.Success || m.Length == 0) continue;
                after = rest[m.Length..].TrimStart();
                if (!AtomEnds(ref after)) continue;   // 조건 하나가 여기서 끝나야 한다
                var match = atom.Build(m);
                if (atom.NameAtom)
                {
                    list.Add(new Condition(m.Value.Trim(), cost, (c, d) => c.Type != CardType.Tamer && match(c, d), field, false));
                    list.Add(new Condition(m.Value.Trim(), cost, (c, d) => c.Type == CardType.Tamer && match(c, d), field, true));
                }
                else list.Add(new Condition(m.Value.Trim(), cost, match, field, false));
                rest = after;
                ok = true;
                break;
            }
            if (!ok) return null;
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>조건 하나 뒤: 끝 · "/" · " or " (다음 조건) — 맞으면 after를 다음 조건 앞으로</summary>
    private static bool AtomEnds(ref string after)
    {
        if (after.Length == 0) return true;
        if (after[0] == '/') { after = after[1..].TrimStart(); return after.Length > 0; }
        if (after.StartsWith("or ", StringComparison.OrdinalIgnoreCase)) { after = after[3..].TrimStart(); return after.Length > 0; }
        return false;
    }

    /// <summary>"While you have [X]" · "While you have N or fewer/more security cards"</summary>
    private static Func<StaticContext, bool>? ParseFieldEn(string s)
    {
        Match m;
        if ((m = Regex.Match(s, $@"^(?<n>{ENL})$", IC)).Success)
        {
            var ns = NamesEn(m.Groups["n"].Value);
            return f => f.Owner.BattleArea.Any(d => d.TamerProxyOf == null && ns.Any(x => CardNames.Is(d, x))) || f.Owner.Tamers.Any(t => ns.Any(x => CardNames.Is(t, x)));
        }
        if ((m = Regex.Match(s, @"^(?<n>\d+) or (?<op>fewer|less|more) security cards?$", IC)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool le = !m.Groups["op"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
            return f => le ? f.Owner.SecurityStack.Count <= n : f.Owner.SecurityStack.Count >= n;
        }
        return null;
    }

    private static List<string> NamesEn(string s) => Regex.Matches(s, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim()).ToList();

    private static bool IsLvEn(CardData c, Match m) =>
        c.Type is CardType.Digimon or CardType.Digitama && c.Level == int.Parse(m.Groups["lv"].Value);

    /// <summary>특징 일치 (종합 룰 2-3-2-3) — 영어 API 특징</summary>
    private static bool TraitIsEn(CardData c, string x) => c.Traits.Any(t => t.Equals(x, StringComparison.OrdinalIgnoreCase));

    private static List<CardColor>? ColorsEn(Match m) =>
        m.Groups["c"].Success ? m.Groups["c"].Value.Split('/').Select(x => EnglishCardRules.ColorOf(x)).Where(x => x != null).Select(x => x!.Value).ToList() : null;

    /// <summary>"Non-white" — 그 색을 갖지 않은</summary>
    private static bool NonColorOk(CardData c, Match m) =>
        !m.Groups["nc"].Success || EnglishCardRules.ColorOf(m.Groups["nc"].Value) is not { } col || !c.AllColors.Contains(col);
}
