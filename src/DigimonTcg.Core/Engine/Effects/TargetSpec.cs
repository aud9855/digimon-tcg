using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// 효과 문장의 대상 구절 — "1 of your opponent's Digimon with 4000 DP or less", "all of your opponent's level 4 or lower Digimon", "this Digimon" …
/// 파서가 만들고, 효과를 처리할 때 Resolve로 실제 디지몬을 고른다.
/// </summary>
public sealed class TargetSpec
{
    public bool IsThis { get; private init; }
    /// <summary>"that Digimon" · "it" — 앞 문장의 대상 (EffectContext.LastTargets, 없으면 이 디지몬)</summary>
    public bool IsThat { get; private init; }
    public bool Opponent { get; private init; }
    /// <summary>"1 Digimon" · "up to 2 Digimon" — 주인을 말하지 않은 대상 = 양쪽 디지몬 중에서 고른다</summary>
    public bool AnySide { get; private init; }
    /// <summary>양쪽에서 고를 때 자신의 디지몬을 먼저 볼지 (액티브 · DP + 같은 이로운 효과). 동작을 읽는 쪽이 정한다</summary>
    public bool PreferOwnSide { get; set; }
    /// <summary>고르는 수 (0 = 전부)</summary>
    public int Count { get; private init; } = 1;
    public List<Func<CardInstance, bool>> Filters { get; } = new();
    /// <summary>효과 상황이 필요한 조건 (예: "with as much or less DP as this Digimon")</summary>
    public List<Func<EffectContext, CardInstance, bool>> ContextFilters { get; } = new();
    /// <summary>"가장 DP가 높은 / 낮은" 같은 고르기 기준 (null이면 효과 종류에 따라 엔진이 고른다)</summary>
    public Func<IEnumerable<CardInstance>, IEnumerable<CardInstance>>? Selector { get; private set; }
    public string Text { get; private init; } = string.Empty;
    /// <summary>"up to N" — 고르지 않아도 된다</summary>
    public bool UpTo { get; private init; }

    private static readonly Regex Head = new(
        @"^(?:(?<count>\d+|all|up to \d+) of )?(?<side>your opponent's|your|their|its owner's)\s+(?<other>other\s+)?(?<adj>(?:(?:unsuspended|suspended|(?:lowest|highest) (?:level|DP|play cost)|level \d+(?: or (?:lower|higher|more|less))?|\[[^\]]+\](?: trait)?|(?:red|blue|yellow|green|black|purple|white)(?: (?:or|and/or) (?:red|blue|yellow|green|black|purple|white))*)\s+)*)(?<kind>Digimon or Tamers|Digimon|Security Digimon)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>대상 구절을 읽는다. 읽지 못하면 null</summary>
    public static TargetSpec? Parse(string phrase)
    {
        phrase = phrase.Trim().TrimEnd('.').Trim();
        phrase = NormalizePreNoun(phrase);
        if (Regex.IsMatch(phrase, @"^this Digimon$", RegexOptions.IgnoreCase))
            return new TargetSpec { IsThis = true, Text = phrase };
        if (Regex.IsMatch(phrase, @"^(?:it|they|that Digimon|those Digimon|them|that card|the Digimon)$", RegexOptions.IgnoreCase))
            return new TargetSpec { IsThat = true, Count = 0, Text = phrase };

        // "1 Digimon" · "up to 2 level 4 or lower Digimon" — 주인이 없으면 양쪽 디지몬 (읽기는 "of your ~"로)
        var anySide = Regex.Match(phrase, @"^(?<count>\d+|up to \d+|all) (?!of )(?<body>(?:other )?(?:[^,]*? )?Digimon\b.*)$", RegexOptions.IgnoreCase);
        if (anySide.Success && Parse($"{anySide.Groups["count"].Value} of your {anySide.Groups["body"].Value}") is { } own && !own.IsThis && !own.IsThat)
            return own.WithAnySide(phrase);

        var m = Head.Match(phrase);
        if (!m.Success || m.Groups["kind"].Value.Equals("Security Digimon", StringComparison.OrdinalIgnoreCase))
            return null;
        string side = m.Groups["side"].Value.ToLowerInvariant();
        string count = m.Groups["count"].Value.ToLowerInvariant();
        bool countGiven = count.Length > 0;
        var spec = new TargetSpec
        {
            Opponent = side is "your opponent's" or "their",
            Count = !countGiven ? 0 : count == "all" ? 0 : int.Parse(Regex.Match(count, @"\d+").Value),
            UpTo = count.StartsWith("up to", StringComparison.Ordinal),
            Text = phrase,
        };
        if (m.Groups["other"].Success)
            spec.ContextFilters.Add((c, d) => !ReferenceEquals(d, c.Self));   // "your other Digimon"
        // "your Digimon" (수 없음) = 자신의 디지몬 전부
        if (!countGiven && side is "its owner's")
            return null;

        var sel = Regex.Match(m.Groups["adj"].Value, @"(lowest|highest) (level|DP|play cost)", RegexOptions.IgnoreCase);
        if (sel.Success && !ParseFilters($"with the {sel.Groups[1].Value} {sel.Groups[2].Value}", spec))
            return null;
        foreach (Match a in Regex.Matches(m.Groups["adj"].Value, @"(?<!(?:lowest|highest) )unsuspended|(?<!(?:lowest|highest) )suspended|level (\d+)(?: or (lower|higher|more|less))?|\[([^\]]+)\](?: trait)?", RegexOptions.IgnoreCase))
        {
            string word = a.Value.ToLowerInvariant();
            if (word == "unsuspended") spec.Filters.Add(d => !d.IsSuspended);
            else if (word == "suspended") spec.Filters.Add(d => d.IsSuspended);
            else if (word.StartsWith("level"))
            {
                int lv = int.Parse(a.Groups[1].Value);
                string dir = a.Groups[2].Value.ToLowerInvariant();
                spec.Filters.Add(dir is "lower" or "less" ? d => d.TopCard.Level <= lv : dir is "higher" or "more" ? d => d.TopCard.Level >= lv : d => d.TopCard.Level == lv);
            }
            else
            {
                string x = a.Groups[3].Value;
                spec.Filters.Add(word.EndsWith("trait") ? d => HasTrait(d.TopCard, x) : d => NameHas(d.TopCard, x) || HasTrait(d.TopCard, x));
            }
        }
        // 색: "your blue Digimon" · "red or black Digimon"
        var colors = Regex.Matches(Regex.Replace(m.Groups["adj"].Value, @"\[[^\]]*\]", ""), @"\b(red|blue|yellow|green|black|purple|white)\b", RegexOptions.IgnoreCase)
            .Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
        if (colors.Count > 0)
            spec.Filters.Add(d => d.TopCard.AllColors.Any(colors.Contains));

        string rest = m.Groups["rest"].Value.Trim();
        if (!ParseFilters(rest, spec))
            return null;
        return spec;
    }

    /// <summary>이름 앞에 둔 조건 (v0.28.0): "[Mineral] or [Rock] trait Digimon" → "Digimon with the [Mineral] or [Rock] trait" · "7000 DP or lower Digimon" → "Digimon with 7000 DP or less" (렌더러도 쓴다)</summary>
    internal static string NormalizePreNoun(string phrase)
    {
        phrase = Regex.Replace(phrase, @"(?<x>\[[^\]]+\](?:,?\s+(?:or\s+|and/or\s+)?\[[^\]]+\])+) traits? Digimon\b(?<rest>.*)$", m => "Digimon with the " + m.Groups["x"].Value + " trait" + (m.Groups["rest"].Value.Trim().Length > 0 ? " and " + Regex.Replace(m.Groups["rest"].Value.Trim(), @"^with ", "", RegexOptions.IgnoreCase) : ""), RegexOptions.IgnoreCase);
        phrase = Regex.Replace(phrase, @"\b(?<n>\d+) DP or (?<d>lower|less|higher|more) Digimon\b(?<rest>.*)$", m => $"Digimon with {m.Groups["n"].Value} DP or {(m.Groups["d"].Value.ToLowerInvariant() is "lower" or "less" ? "less" : "more")}" + (m.Groups["rest"].Value.Trim().Length > 0 ? " and " + Regex.Replace(m.Groups["rest"].Value.Trim(), @"^with ", "", RegexOptions.IgnoreCase) : ""), RegexOptions.IgnoreCase);
        return phrase;
    }

    private TargetSpec WithAnySide(string text)
    {
        var spec = new TargetSpec { AnySide = true, Count = Count, UpTo = UpTo, Text = text, Selector = Selector };
        spec.Filters.AddRange(Filters);
        spec.ContextFilters.AddRange(ContextFilters);
        return spec;
    }

    /// <summary>"with 4000 DP or less", "with the highest DP", "with no digivolution cards", "with [X] in its name" … (and로 이어질 수 있음)</summary>
    private static bool ParseFilters(string rest, TargetSpec spec)
    {
        rest = rest.Trim().TrimEnd(',').Trim();
        while (rest.Length > 0)
        {
            Match m;
            int before = spec.Filters.Count;
            if ((m = Regex.Match(rest, @"^(?:with |that has |that have |and |or )?(?:(?:a|an) )?(?:(?<n>\d+) DP or (?<dir>less|more))", RegexOptions.IgnoreCase)).Success)
            {
                int n = int.Parse(m.Groups["n"].Value);
                bool less = m.Groups["dir"].Value.Equals("less", StringComparison.OrdinalIgnoreCase);
                spec.Filters.Add(less ? d => d.GetCurrentDP() <= n : d => d.GetCurrentDP() >= n);
            }
            else if ((m = Regex.Match(rest, @"^(?:with |and )?(?:a |the )?play costs? of (?<n>\d+) or (?<dir>less|more)", RegexOptions.IgnoreCase)).Success)
            {
                int n = int.Parse(m.Groups["n"].Value);
                bool less = m.Groups["dir"].Value.Equals("less", StringComparison.OrdinalIgnoreCase);
                spec.Filters.Add(less ? d => d.TopCard.PlayCost <= n : d => d.TopCard.PlayCost >= n);
            }
            else if ((m = Regex.Match(rest, @"^(?:with |and )?the (?<which>highest|lowest) (?<what>DP|level|play cost)", RegexOptions.IgnoreCase)).Success)
            {
                bool high = m.Groups["which"].Value.Equals("highest", StringComparison.OrdinalIgnoreCase);
                Func<CardInstance, int> key = m.Groups["what"].Value.ToLowerInvariant() switch
                {
                    "dp" => d => d.GetCurrentDP(),
                    "level" => d => d.TopCard.Level,
                    _ => d => d.TopCard.PlayCost,
                };
                spec.Selector = list =>
                {
                    var l = list.ToList();
                    if (l.Count == 0) return l;
                    int best = high ? l.Max(key) : l.Min(key);
                    return l.Where(d => key(d) == best);
                };
            }
            else if ((m = Regex.Match(rest, @"^(?:with no|without|with no digivolution cards|that has no) digivolution cards", RegexOptions.IgnoreCase)).Success
                     || (m = Regex.Match(rest, @"^with no digivolution cards", RegexOptions.IgnoreCase)).Success)
            {
                spec.Filters.Add(d => d.EvolutionSources.Count == 0);
            }
            else if ((m = Regex.Match(rest, @"^(?:with |that has |and )?(?:a|an) (?<kind>Tamer|Option|Digimon) card in its digivolution cards", RegexOptions.IgnoreCase)).Success)
            {
                // "with a Tamer card in its digivolution cards" — 진화원에 그 종류의 카드가 있다
                var kind = Enum.Parse<CardType>(m.Groups["kind"].Value, ignoreCase: true);
                spec.Filters.Add(d => d.EvolutionSources.Any(s => s.Type == kind));
            }
            else if ((m = Regex.Match(rest, @"^(?:with |and )?(?<n>\d+) or (?<dir>more|fewer|less) digivolution cards", RegexOptions.IgnoreCase)).Success)
            {
                int n = int.Parse(m.Groups["n"].Value);
                bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
                spec.Filters.Add(more ? d => d.EvolutionSources.Count >= n : d => d.EvolutionSources.Count <= n);
            }
            else if ((m = Regex.Match(rest, $@"^(?:with |and |or )?(?<x>{NameList}) in (?:its|their) names?", RegexOptions.IgnoreCase)).Success)
            {
                var xs = Names(m.Groups["x"].Value);
                spec.Filters.Add(d => xs.Any(x => NameHas(d.TopCard, x)));
            }
            else if ((m = Regex.Match(rest, $@"^(?:with |and |or )?(?:the )?(?<x>{NameList}) traits?|^(?:with |or )?(?:the )?(?<x>{NameList}) in any of (?:its|their) traits(?: other than (?<ex>\[[^\]]+\]))?", RegexOptions.IgnoreCase)).Success)
            {
                var xs = Names(m.Groups["x"].Value);
                var ex = Names(m.Groups["ex"].Value);
                spec.Filters.Add(d => xs.Any(x => HasTrait(d.TopCard, x, ex)));
            }
            else if ((m = Regex.Match(rest, $@"^(?:with |and |or )?(?<x>{NameList}) in (?:its|their) texts?", RegexOptions.IgnoreCase)).Success)
            {
                var xs = Names(m.Groups["x"].Value);
                spec.Filters.Add(d => xs.Any(x => TextHas(d.TopCard, x)));
            }
            else if ((m = Regex.Match(rest, @"^(?:with |and )?the (?<which>fewest|most) digivolution cards", RegexOptions.IgnoreCase)).Success)
            {
                bool fewest = m.Groups["which"].Value.Equals("fewest", StringComparison.OrdinalIgnoreCase);
                spec.Selector = list =>
                {
                    var l = list.ToList();
                    if (l.Count == 0) return l;
                    int b = fewest ? l.Min(d => d.EvolutionSources.Count) : l.Max(d => d.EvolutionSources.Count);
                    return l.Where(d => d.EvolutionSources.Count == b);
                };
            }
            else if ((m = Regex.Match(rest, @"^(?:with |and )?DP less than or equal to this Digimon's DP", RegexOptions.IgnoreCase)).Success)
            {
                spec.ContextFilters.Add((c, d) => c.Self != null && d.GetCurrentDP() <= c.Self.GetCurrentDP());
            }
            else if ((m = Regex.Match(rest, @"^(?<neg>with|without|that (?:has|have)|that (?:doesn't|don't) have) ＜(?<k>[A-Za-z .]+?)\s*＞(?!\s*or\s*＜)", RegexOptions.IgnoreCase)).Success
                     && KeywordNames.TryGetValue(m.Groups["k"].Value.Trim(), out var kw))
            {
                bool without = m.Groups["neg"].Value.StartsWith("without", StringComparison.OrdinalIgnoreCase) || m.Groups["neg"].Value.Contains("n't");
                spec.Filters.Add(d => (kw == Keyword.Blocker ? d.HasBlocker : CardScripts.HasKeyword(d, kw)) != without);
            }
            else if ((m = Regex.Match(rest, @"^(?<neg>with|without|that (?:has|have)|that (?:doesn't|don't) have) ＜(?<k>[A-Za-z .\-]+?)\s*＞(?:\s*or\s*＜(?<k2>[A-Za-z .\-]+?)\s*＞)?", RegexOptions.IgnoreCase)).Success)
            {
                // 동작 키워드(≪디지버스트≫ · ≪블리츠≫ …)나 "＜A＞ or ＜B＞": 맨 위 카드 · 진화원 효과 문장에 그 키워드가 있는지
                bool without = m.Groups["neg"].Value.StartsWith("without", StringComparison.OrdinalIgnoreCase) || m.Groups["neg"].Value.Contains("n't");
                var ks = new[] { m.Groups["k"].Value.Trim(), m.Groups["k2"].Value.Trim() }.Where(k => k.Length > 0).ToList();
                spec.Filters.Add(d => ks.Any(k => KeywordNames.TryGetValue(k, out var kk)
                    ? (kk == Keyword.Blocker ? d.HasBlocker : CardScripts.HasKeyword(d, kk))
                    : d.GetEvolutionStackBottomUp().Append(d.TopCard).Any(x => x.EffectText.Contains($"＜{k}", StringComparison.OrdinalIgnoreCase))) != without);
            }
            else if ((m = Regex.Match(rest, @"^with an? \[(?<t>On Play|On Deletion|When Digivolving|When Attacking|Security|Main)\] effect", RegexOptions.IgnoreCase)).Success)
            {
                string t = m.Groups["t"].Value;
                spec.Filters.Add(d => d.TopCard.EffectText.Contains($"[{t}]", StringComparison.OrdinalIgnoreCase));
            }
            else if ((m = Regex.Match(rest, @"^with (?:as much or less|less than or equal to|DP less than or equal to) (?:DP )?(?:as|to) this Digimon(?:'s DP)?", RegexOptions.IgnoreCase)).Success)
            {
                spec.ContextFilters.Add((c, d) => c.Self != null && d.GetCurrentDP() <= c.Self.GetCurrentDP());
            }
            else if ((m = Regex.Match(rest, @"^with (?:a )?lower (?:DP|level) than this Digimon", RegexOptions.IgnoreCase)).Success)
            {
                bool dp = m.Value.Contains("DP", StringComparison.OrdinalIgnoreCase);
                spec.ContextFilters.Add((c, d) => c.Self != null && (dp ? d.GetCurrentDP() < c.Self.GetCurrentDP() : d.TopCard.Level < c.Self.TopCard.Level));
            }
            else
            {
                return false;
            }
            // "with [Imperialdramon] in its name or [Free] trait" — "or"로 이어진 조건은 앞 조건과 둘 중 하나 (v0.27.0 전에는 둘 다로 읽었다)
            if (Regex.IsMatch(m.Value, @"^or\s", RegexOptions.IgnoreCase) && spec.Filters.Count == before + 1 && before >= 1)
            {
                var (a, b) = (spec.Filters[before - 1], spec.Filters[before]);
                spec.Filters.RemoveRange(before - 1, 2);
                spec.Filters.Add(d => a(d) || b(d));
            }
            rest = rest[m.Length..].Trim().TrimStart(',').Trim();
        }
        return true;
    }

    private static readonly Dictionary<string, Keyword> KeywordNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blocker"] = Keyword.Blocker, ["Jamming"] = Keyword.Jamming, ["Piercing"] = Keyword.Piercing, ["Retaliation"] = Keyword.Retaliation,
        ["Reboot"] = Keyword.Reboot, ["Rush"] = Keyword.Rush, ["Raid"] = Keyword.Raid, ["Alliance"] = Keyword.Alliance,
        ["Barrier"] = Keyword.Barrier, ["Evade"] = Keyword.Evade,
        ["Armor Purge"] = Keyword.ArmorPurge, ["Fortitude"] = Keyword.Fortitude, ["Collision"] = Keyword.Collision, ["Scapegoat"] = Keyword.Scapegoat,
        ["Iceclad"] = Keyword.Iceclad, ["Progress"] = Keyword.Progress,
        ["Link +1"] = Keyword.LinkPlusOne, ["Vortex"] = Keyword.Vortex, ["Execute"] = Keyword.Execute,
    };

    /// <summary>"[A]" · "[A] or [B]" · "[A], [B] or [C]" · "[A] and/or [B]" — 이름 / 특징 목록</summary>
    internal const string NameList = @"\[[^\]]+\](?:(?:,\s*|\s+)(?:or\s+|and/or\s+|and\s+)?\[[^\]]+\])*";

    internal static List<string> Names(string text) =>
        Regex.Matches(text, @"\[([^\]]+)\]").Select(m => m.Groups[1].Value.Trim()).ToList();

    public static bool NameHas(CardData card, string x) => CardNames.Contains(card, x);   // 내장 한글 카드는 공식 영어 이름도 본다

    public static bool TextHas(CardData card, string x) =>
        NameHas(card, x) || card.EffectText.Contains($"[{x}]", StringComparison.OrdinalIgnoreCase)
        || card.InheritedEffectText.Contains($"[{x}]", StringComparison.OrdinalIgnoreCase);

    public static bool HasTrait(CardData card, string x) =>
        card.Traits.Any(t => t.Equals(x, StringComparison.OrdinalIgnoreCase) || t.Contains(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>"[Animal] in any of its traits other than [Sea Animal]" — 제외한 특징은 빼고 본다</summary>
    public static bool HasTrait(CardData card, string x, IReadOnlyCollection<string> except) =>
        except.Count == 0 ? HasTrait(card, x)
        : card.Traits.Any(t => !except.Any(e => t.Equals(e, StringComparison.OrdinalIgnoreCase)) && t.Contains(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 대상 고르기. harmful = 상대에게 불리한 효과(소멸 · DP − · 레스트 …)면 DP가 높은 것부터, 아니면 자신의 이 디지몬 → DP 높은 순.
    /// preferred = UI가 고른 대상 / 어택 중인 디지몬 (조건에 맞으면 먼저)
    /// </summary>
    public List<CardInstance> Resolve(EffectContext c, CardInstance? preferred) =>
        ResolveCore(c, preferred).Where(d => !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();   // ≪프로그레스≫: 어택 중이면 상대 효과를 받지 않는다

    private List<CardInstance> ResolveCore(EffectContext c, CardInstance? preferred)
    {
        if (IsThat && c.LastTargets.Count > 0)
            return c.LastTargets.Where(d => c.Owner.BattleArea.Contains(d) || c.Enemy.BattleArea.Contains(d)).ToList();
        if (IsThis || IsThat)
            return c.Self != null && (c.Owner.BattleArea.Contains(c.Self) || ReferenceEquals(c.Owner.BreedingArea, c.Self)) ? new List<CardInstance> { c.Self } : new List<CardInstance>();

        var pool = AnySide ? c.Owner.BattleArea.Concat(c.Enemy.BattleArea) : (Opponent ? c.Enemy : c.Owner).BattleArea;
        IEnumerable<CardInstance> candidates = pool.Where(d => Filters.All(f => f(d)) && ContextFilters.All(f => f(c, d)));
        if (Selector != null)
            candidates = Selector(candidates);
        var list = candidates.ToList();
        if (Count == 0)
            return list;

        var ordered = list
            .OrderByDescending(d => ReferenceEquals(d, preferred))
            .ThenByDescending(d => AnySide && c.Owner.BattleArea.Contains(d) == PreferOwnSide)   // 양쪽: 해로운 효과는 상대 먼저, 이로운 효과는 자신 먼저
            .ThenByDescending(d => (!Opponent || (AnySide && PreferOwnSide)) && ReferenceEquals(d, c.Self))
            .ThenByDescending(d => d.GetCurrentDP())
            .ThenByDescending(d => d.TopCard.Level)
            .ToList();
        string side = AnySide ? "디지몬" : Opponent ? "상대 디지몬" : "자신의 디지몬";
        return c.Engine.ChooseTargets(c.Owner, ordered, Count, $"[{c.Card?.Name}] 효과의 대상({side} {Count}마리{(UpTo ? "까지" : "")})을 고르세요.", UpTo, preferred);
    }
}
