using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.47.0 공식 한글 〔진화〕 진화 조건 (영어 API 데이터에는 진화 조건이 레벨 · 색 하나뿐이고 〔진화〕 문구가 빠져 있다).
/// 종합 룰 2-3-5-3: 〔진화〕 아이콘 뒤에 적힌 텍스트도 그 카드의 진화 조건이다 (이름만 적힌 조건은 그 명칭의 카드에서 적힌 코스트로 진화).
/// 종합 룰 8-1-2-1: 진화 조건이 여럿이면 어느 조건으로 진화할지 고른다 → 엔진은 맞는 조건 중 가장 싼 코스트.
/// 읽는 조건 (조건 여럿은 "/", 같은 코스트를 함께 쓰는 조건도 "/" — 종합 룰 4-24-1 "AA/BB" = AA 또는 BB):
///  - 「X」 · 「X」/「Y」 · Lv.N의 「X」 · 퍼플인 「X」 · 3색의 「X」 · 등장 코스트 4 이하의 「X」 — 명칭이 X인 카드 (종합 룰 2-3-1-2 — 일치, 포함 아님 · 2-3-1-4 「ACE」는 명칭에 포함하지 않는다).
///    테이머 이름이면 그 테이머에서 진화 (BT18-011 「우정훈」)
///  - 명칭에 「X」을 포함하는 (Lv.N) (2-3-1-3) · ~포함하고 특징으로 「Y」를 가진/갖지 않은 Lv.N · ~포함하거나 특징으로 「Y」를 가진 Lv.N · ~포함하거나 그린을 포함하는 2색의 Lv.N
///  - 특징 「X」를 가진 (블루/레드인) Lv.N (이상) (2-3-2-3 — 특징 일치) · 등장 코스트 13 이하의 · 특징으로 「X」를 갖지 않고 「Y」를 가진 Lv.N
///  - 「X」이 기술되어 있는 Lv.N · 《세이브》가 기술되어 있는 Lv.N (4-23 — 카드 전체의 정보, 주석 괄호 제외)
///  - 그린을 포함하는 2색의 Lv.N · 진화원에 「X」이 있는 「Y」
///  - 필드 조건 "자신의 「X」가 있는 동안," · "자신의 시큐리티가 N장 이하인 동안," · "자신의 트래시가 N장 이상인 동안," · "DP N 이상의 상대의 디지몬이 있는 동안,"
/// 읽지 못한 조건은 쓰지 않는다 (Unparsed로 보고).
/// 한글 문구는 로컬 data/kr/cards_ko.json에서 실행할 때 읽는다 (저장소에는 넣지 않는다).
/// </summary>
public static class KoreanEvolution47
{
    /// <summary>조건 하나: Match(맨 위 카드, 디지몬 — 테이머에서면 null) · Field(필드 조건 "~인 동안", 없으면 null) · TamerName(테이머 이름 조건)</summary>
    public sealed record Condition(string Text, int Cost, Func<CardData, CardInstance?, bool> Match, Func<StaticContext, bool>? Field, bool TamerName);

    public sealed record Parsed(IReadOnlyList<Condition> Conditions, IReadOnlyList<string> Unparsed);

    private static readonly Parsed Empty = new(Array.Empty<Condition>(), Array.Empty<string>());
    private static readonly ConcurrentDictionary<string, Parsed> Cache = new(StringComparer.Ordinal);

    private const string NL = @"「[^」]+」(?:\s*/\s*「[^」]+」)*";
    private const string Col = @"레드|블루|옐로|그린|블랙|퍼플|화이트";
    private const string Cols = "(?:" + Col + @")(?:\s*/\s*(?:" + Col + "))*";
    private const string LvP = @"Lv\.(?<lv>\d+)(?<up>\s*이상)?";
    private const string J = @"\s*[,、]?\s*";

    /// <summary>evoCard의 공식 한글 〔진화〕 조건 (한글 데이터가 없으면 빈 목록)</summary>
    public static Parsed For(CardData evoCard)
    {
        if (evoCard.Type != CardType.Digimon) return Empty;
        string? text = KoreanCardData.Get(evoCard.CardId)?.MainText;
        if (string.IsNullOrEmpty(text) || !text.Contains("〔진화〕", StringComparison.Ordinal)) return Empty;
        return Cache.GetOrAdd(text, Parse);
    }

    /// <summary>target(맨 위 카드)에서 evoCard로 〔진화〕 조건으로 진화할 수 있으면 그 코스트 (여럿이면 가장 싼 것)</summary>
    public static int? Cost(CardData evoCard, CardInstance target)
    {
        var p = For(evoCard);
        if (p.Conditions.Count == 0 || target.TamerProxyOf != null) return null;
        int? best = null;
        StaticContext? ctx = null;
        foreach (var c in p.Conditions)
        {
            if (c.TamerName || (best != null && c.Cost >= best) || !c.Match(target.TopCard, target)) continue;
            if (c.Field != null && ((ctx ??= target.LiveContext42?.Invoke()) is not { } fc || !c.Field(fc))) continue;   // 필드 조건: 필드에 있는 디지몬만 (엔진이 문맥을 채운다)
            best = c.Cost;
        }
        return best;
    }

    /// <summary>테이머 tamer에서 evoCard로 〔진화〕「테이머 이름」 조건으로 진화할 수 있으면 그 코스트 (필드 조건이 붙은 것은 제외)</summary>
    public static int? TamerCost(CardData evoCard, CardData tamer)
    {
        if (tamer.Type != CardType.Tamer) return null;
        var p = For(evoCard);
        int? best = null;
        foreach (var c in p.Conditions)
            if (c.TamerName && c.Field == null && c.Match(tamer, null) && (best == null || c.Cost < best)) best = c.Cost;
        return best;
    }

    // ---------------------------------------------------------------- 읽기

    internal static Parsed Parse(string text)
    {
        var conds = new List<Condition>();
        var bad = new List<string>();
        foreach (Match head in Regex.Matches(text, "〔진화〕"))
        {
            int pos = head.Index + head.Length;
            while (true)
            {
                var g = Regex.Match(text[pos..], @"^\s*(?<cond>(?:「[^」]*」|[^:：「」【〔\n])+?)\s*[:：]\s*코스트\s*(?<n>\d+)");
                if (!g.Success) break;
                string cond = g.Groups["cond"].Value.Trim();
                int cost = int.Parse(g.Groups["n"].Value);
                pos += g.Length;
                if (cond.StartsWith("조그레스", StringComparison.Ordinal)) { }   // 압축 표기의 조그레스 조건 (조그레스 처리에서 읽는다)
                else if (ParseGroup(cond, cost) is { } list) conds.AddRange(list);
                else bad.Add(cond + " : 코스트 " + cost);
                var sep = Regex.Match(text[pos..], @"^\s*/\s*");
                if (!sep.Success) break;
                pos += sep.Length;
            }
        }
        return new Parsed(conds, bad);
    }

    private delegate Func<CardData, CardInstance?, bool> AtomBuilder(Match m);

    private static readonly (Regex Re, AtomBuilder Build, bool NameAtom)[] Atoms =
    {
        // 「X」이 기술되어 있는 Lv.N
        (new Regex($@"^(?<n>{NL})\s*[이가]\s*기술되어\s*있는\s*(?:(?<c>{Cols})\s*인\s*)?{LvP}"),
            m => { var ns = Names(m.Groups["n"].Value); var cs = ColorsOf(m); return (c, _) => IsLv(c, m) && ColorOk(c, cs) && ns.Any(x => TextHas(c, x)); }, false),
        // 《세이브》가 기술되어 있는 (옐로/그린/퍼플인) Lv.N
        (new Regex($@"^《(?<k>[^》]+)》\s*[이가]\s*기술되어\s*있는\s*(?:(?<c>{Cols})\s*인\s*)?{LvP}"),
            m => { string k = m.Groups["k"].Value; var cs = ColorsOf(m); return (c, _) => IsLv(c, m) && ColorOk(c, cs) && KeywordWritten(c, k); }, false),
        // 명칭에 「X」을 포함하고(,) 특징(으로) 「Y」를 가진/갖지 않은 Lv.N
        (new Regex($@"^명칭에\s*(?<n>{NL})\s*[을를]?\s*포함하고{J}특징(?:으로|에)?\s*(?<t>{NL})\s*[을를]?\s*(?<neg>갖지\s*않은|가지지\s*않은|가진)\s*{LvP}"),
            m => { var ns = Names(m.Groups["n"].Value); var ts = Names(m.Groups["t"].Value); bool neg = !m.Groups["neg"].Value.StartsWith("가진", StringComparison.Ordinal);
                   return (c, _) => IsLv(c, m) && ns.Any(x => NameHas(c, x)) && (ts.Any(t => TraitIs(c, t)) != neg); }, false),
        // 명칭에 「X」을 포함하거나 특징으로 「Y」를 가진 Lv.N
        (new Regex($@"^명칭에\s*(?<n>{NL})\s*[을를]?\s*포함하거나{J}특징(?:으로|에)?\s*(?<t>{NL})\s*[을를]?\s*가진\s*{LvP}"),
            m => { var ns = Names(m.Groups["n"].Value); var ts = Names(m.Groups["t"].Value); return (c, _) => IsLv(c, m) && (ns.Any(x => NameHas(c, x)) || ts.Any(t => TraitIs(c, t))); }, false),
        // 명칭에 「X」을 포함하거나 그린을 포함하는 2색의 Lv.N
        (new Regex($@"^명칭에\s*(?<n>{NL})\s*[을를]?\s*포함하거나{J}(?<c1>{Col})\s*[을를]\s*포함하는\s*(?<k>\d+)\s*색의\s*{LvP}"),
            m => { var ns = Names(m.Groups["n"].Value); var col = ColorOf(m.Groups["c1"].Value); int k = int.Parse(m.Groups["k"].Value);
                   return (c, _) => IsLv(c, m) && (ns.Any(x => NameHas(c, x)) || (c.AllColors.Count == k && c.AllColors.Contains(col))); }, false),
        // 명칭에 「X」을 포함하는 (블루인) (2색(의)) (Lv.N (이상)) · 명칭에 「X」을 포함 · 명칭에「X」Lv.N
        (new Regex($@"^명칭에\s*(?<n>{NL})\s*(?:[을를]\s*)?(?:포함(?:하는)?\s*)?(?:(?<c>{Cols})\s*인\s*)?(?:(?<k>\d+)\s*색(?:의)?\s*)?(?:{LvP})?"),
            m => { var ns = Names(m.Groups["n"].Value); var cs = ColorsOf(m); int k = m.Groups["k"].Success ? int.Parse(m.Groups["k"].Value) : 0;
                   return (c, _) => IsLvOrAny(c, m) && ColorOk(c, cs) && (k == 0 || c.AllColors.Count == k) && ns.Any(x => NameHas(c, x)); }, false),
        // 특징으로 「X」를 갖지 않고(,) 「Y」를 가진 Lv.N
        (new Regex($@"^특징(?:으로|에)?\s*(?<x>{NL})\s*[을를]?\s*갖지\s*않고{J}(?<t>{NL})\s*[을를]?\s*가진\s*{LvP}"),
            m => { var xs = Names(m.Groups["x"].Value); var ts = Names(m.Groups["t"].Value); return (c, _) => IsLv(c, m) && !xs.Any(x => TraitIs(c, x)) && ts.Any(t => TraitIs(c, t)); }, false),
        // 특징 「X」를 가진 (등장 코스트 13 이하의) (블루/레드인) Lv.N (이상) · 특징「X」Lv.N
        (new Regex($@"^특징(?:으로|에)?\s*(?<t>{NL})\s*(?:[을를]\s*)?(?:가진\s*)?(?:등장\s*코스트\s*(?<pc>\d+)\s*(?<pcop>이하|이상)의\s*)?(?:(?<c>{Cols})\s*인\s*)?{LvP}"),
            m => { var ts = Names(m.Groups["t"].Value); var cs = ColorsOf(m); return (c, _) => IsLv(c, m) && ColorOk(c, cs) && PlayCostOk(c, m) && ts.Any(t => TraitIs(c, t)); }, false),
        // 그린을 포함하는 2색의 Lv.N
        (new Regex($@"^(?<c1>{Col})\s*[을를]\s*포함하는\s*(?<k>\d+)\s*색의\s*{LvP}"),
            m => { var col = ColorOf(m.Groups["c1"].Value); int k = int.Parse(m.Groups["k"].Value); return (c, _) => IsLv(c, m) && c.AllColors.Count == k && c.AllColors.Contains(col); }, false),
        // 진화원에 「X」을 가진 「Y」 · 진화원에 「X」이 있는 「Y」
        (new Regex($@"^진화원에\s*(?<s>{NL})\s*[을를이가]\s*(?:가진|있는)\s*(?<n>{NL})"),
            m => { var ss = Names(m.Groups["s"].Value); var ns = Names(m.Groups["n"].Value);
                   return (c, d) => d != null && ns.Any(x => NameIs(c, x)) && d.FaceUpSources.Any(s => ss.Any(x => NameIs(s, x))); }, false),
        // (퍼플인 · 3색의 · 등장 코스트 4 이하의 · Lv.N의) 「X」 · 「X」/「Y」
        (new Regex($@"^(?:(?<c>{Cols})\s*인\s*|(?<k>\d+)\s*색의\s*|등장\s*코스트\s*(?<pc>\d+)\s*(?<pcop>이하|이상)의\s*|Lv\.(?<lv>\d+)\s*의\s*)?(?<n>{NL})"),
            m => { var ns = Names(m.Groups["n"].Value); var cs = ColorsOf(m); int k = m.Groups["k"].Success ? int.Parse(m.Groups["k"].Value) : 0;
                   return (c, _) => IsLvOrAny(c, m) && ColorOk(c, cs) && (k == 0 || c.AllColors.Count == k) && PlayCostOk(c, m) && ns.Any(x => NameIs(c, x)); }, true),
    };

    /// <summary>필드 조건 "자신의 「X」가 있는 동안" · "자신의 시큐리티가 N장 이하인 동안" · "자신의 트래시가 N장 이상인 동안" · "DP N 이상의 상대의 디지몬이 있는 동안"</summary>
    private static Func<StaticContext, bool>? ParseField(string s)
    {
        Match m;
        if ((m = Regex.Match(s, $@"^자신의\s*(?<n>{NL})\s*[이가]\s*있는\s*동안$")).Success)
        {
            var ns = Names(m.Groups["n"].Value);
            return f => f.Owner.BattleArea.Any(d => ns.Any(x => NameIs(d.TopCard, x))) || f.Owner.Tamers.Any(t => ns.Any(x => NameIs(t, x)));
        }
        if ((m = Regex.Match(s, @"^자신의\s*(?<z>시큐리티|트래시|패)\s*[이가]\s*(?<n>\d+)\s*장\s*(?<op>이하|이상)인\s*동안$")).Success)
        {
            string z = m.Groups["z"].Value; int n = int.Parse(m.Groups["n"].Value); bool le = m.Groups["op"].Value == "이하";
            return f => { int v = z == "시큐리티" ? f.Owner.SecurityStack.Count : z == "트래시" ? f.Owner.Trash.Count : f.Owner.Hand.Count; return le ? v <= n : v >= n; };
        }
        if ((m = Regex.Match(s, @"^DP\s*(?<n>\d+)\s*(?<op>이하|이상)의\s*상대의\s*디지몬이\s*있는\s*동안$")).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool le = m.Groups["op"].Value == "이하";
            return f => f.Enemy.BattleArea.Any(d => d.TamerProxyOf == null && (le ? d.GetCurrentDP() <= n : d.GetCurrentDP() >= n));
        }
        return null;
    }

    /// <summary>같은 코스트의 조건 묶음 ("「A」/특징 「B」를 가진 Lv.2") → 조건 목록, 하나라도 못 읽으면 null</summary>
    private static List<Condition>? ParseGroup(string group, int cost)
    {
        var list = new List<Condition>();
        string rest = group.Trim();
        Func<StaticContext, bool>? field = null;
        var fm = Regex.Match(rest, @"^(?<f>(?:「[^」]*」|[^「」,、])*?동안)\s*[,、]\s*");
        if (fm.Success)
        {
            if ((field = ParseField(fm.Groups["f"].Value.Trim())) == null) return null;
            rest = rest[fm.Length..];
        }
        while (rest.Length > 0)
        {
            bool ok = false;
            foreach (var atom in Atoms)
            {
                var m = atom.Re.Match(rest);
                if (!m.Success || m.Length == 0) continue;
                string after = rest[m.Length..].TrimStart();
                if (after.Length > 0 && after[0] != '/') continue;   // 조건 하나가 여기서 끝나야 한다
                var match = atom.Build(m);
                bool tamerName = atom.NameAtom && !m.Groups["lv"].Success && Names(m.Groups["n"].Value).All(IsTamerName);
                list.Add(new Condition(m.Value.Trim(), cost, match, field, tamerName));
                rest = after.Length > 0 ? after[1..].TrimStart() : "";
                ok = true;
                break;
            }
            if (!ok) return null;
        }
        return list.Count > 0 ? list : null;
    }

    private static int Lv(Match m) => int.Parse(m.Groups["lv"].Value);

    /// <summary>Lv.N (이상) 조건: 디지몬 · 디지타마 카드 중 레벨이 N (테이머 · 옵션은 레벨이 없다)</summary>
    private static bool IsLv(CardData c, Match m)
    {
        if (c.Type is not (CardType.Digimon or CardType.Digitama)) return false;
        int lv = Lv(m);
        return m.Groups["up"].Success && m.Groups["up"].Length > 0 ? c.Level >= lv : c.Level == lv;
    }

    /// <summary>레벨이 적혀 있으면 Lv 조건, 없으면 아무 카드 (명칭 조건)</summary>
    private static bool IsLvOrAny(CardData c, Match m) => !m.Groups["lv"].Success || IsLv(c, m);

    /// <summary>등장 코스트 N 이하/이상</summary>
    private static bool PlayCostOk(CardData c, Match m)
    {
        if (!m.Groups["pc"].Success) return true;
        int n = int.Parse(m.Groups["pc"].Value);
        return c.Type == CardType.Digimon && (m.Groups["pcop"].Value == "이하" ? c.PlayCost <= n : c.PlayCost >= n);
    }

    private static List<string> Names(string s) => Regex.Matches(s, "「([^」]+)」").Select(x => x.Groups[1].Value.Trim()).ToList();

    private static CardColor ColorOf(string ko) => ko switch
    {
        "레드" => CardColor.Red, "블루" => CardColor.Blue, "옐로" => CardColor.Yellow, "그린" => CardColor.Green,
        "블랙" => CardColor.Black, "퍼플" => CardColor.Purple, _ => CardColor.White,
    };

    private static List<CardColor>? ColorsOf(Match m) =>
        m.Groups["c"].Success ? m.Groups["c"].Value.Split('/').Select(x => ColorOf(x.Trim())).ToList() : null;

    // 종합 룰 4-24-1 "AA/BB" = AA 또는 BB
    private static bool ColorOk(CardData c, List<CardColor>? cs) => cs == null || c.AllColors.Any(cs.Contains);

    private static string Norm(string? s)
    {
        s = Regex.Replace(s ?? "", @"\s+", "");
        return s.EndsWith("ACE", StringComparison.OrdinalIgnoreCase) ? s[..^3] : s;   // 종합 룰 2-3-1-4 명칭의 「ACE」는 명칭에 포함하지 않는다
    }

    /// <summary>공식 한글 이름 (공식 한글이 없는 카드는 같은 영어 이름의 한글 이름)</summary>
    private static string KoName(CardData c) => Norm(KoreanCardData.DisplayName(c));

    /// <summary>명칭이 x인가 (공식 한글 이름 · 같은 한글 이름의 영어 이름)</summary>
    internal static bool NameIs(CardData c, string x)
    {
        string nx = Norm(x);
        if (KoName(c) == nx) return true;
        return EnglishNames(nx).Any(en => CardNames.Is(c, en) || Norm(c.Name).Equals(Norm(en), StringComparison.OrdinalIgnoreCase));
    }

    private static bool NameHas(CardData c, string x)
    {
        string nx = Norm(x);
        if (KoName(c).Contains(nx, StringComparison.Ordinal)) return true;
        return EnglishNames(nx).Any(en => CardNames.Contains(c, en));
    }

    private static bool TraitIs(CardData c, string x)
    {
        string nx = Norm(x);
        return KoreanCardData.DisplayTraits(c).Any(t => Norm(t) == nx);
    }

    private static string StripNotes(string? s) => Regex.Replace(s ?? "", @"[（(][^（()）]*[)）]", "");

    /// <summary>「X」이 기술되어 있는 (종합 룰 4-23 — 카드 전체의 정보, 주석 괄호 제외)</summary>
    private static bool TextHas(CardData c, string x)
    {
        string nx = Norm(x);
        if (KoName(c).Contains(nx, StringComparison.Ordinal)) return true;
        if (KoreanCardData.Get(c.CardId) is { } ko)
            return Norm(StripNotes(ko.MainText) + StripNotes(ko.SourceText)).Contains(nx, StringComparison.Ordinal);
        return EnglishNames(nx).Any(en => TargetSpec.TextHas(c, en));
    }

    private static bool KeywordWritten(CardData c, string k)
    {
        if (KoreanCardData.Get(c.CardId) is { } ko)
            return $"{StripNotes(ko.MainText)}{StripNotes(ko.SourceText)}".Contains($"《{k}》", StringComparison.Ordinal);
        string en = k switch { "세이브" => "Save", _ => k };
        return $"{c.EffectText}{c.InheritedEffectText}".Contains($"＜{en}＞", StringComparison.OrdinalIgnoreCase);
    }

    // 공식 한글 이름 → 영어 이름들 (공식 한글이 없는 카드 · 내장 카드의 이름 비교)
    private static readonly Lazy<Dictionary<string, List<string>>> Reverse = new(() =>
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (en, ko) in KoreanEffectRenderer.NameGlossary)
        {
            string k = Norm(ko);
            if (k.Length == 0) continue;
            if (!map.TryGetValue(k, out var l)) map[k] = l = new List<string>();
            if (!l.Contains(en, StringComparer.OrdinalIgnoreCase)) l.Add(en);
        }
        return map;
    });

    private static IReadOnlyList<string> EnglishNames(string normKo) =>
        Reverse.Value.TryGetValue(normKo, out var l) ? l : Array.Empty<string>();

    private static readonly ConcurrentDictionary<string, bool> TamerNames = new(StringComparer.Ordinal);

    /// <summary>「X」가 테이머 이름인가 (공식 한글 카드 중 그 이름의 카드가 전부 테이머)</summary>
    private static bool IsTamerName(string x) => TamerNames.GetOrAdd(Norm(x), nx =>
    {
        var same = KoreanCardData.All.Where(k => Norm(k.Name) == nx).ToList();
        return same.Count > 0 && same.All(k => k.Type == "테이머");
    });
}
