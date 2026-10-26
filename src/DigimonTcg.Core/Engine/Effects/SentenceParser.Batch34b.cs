using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.34.0 8차 묶음 (2) — 셈 ("for each color in its digivolution cards" · "for each card in your trash with the [Cyborg] trait" · "for each of your [Hudie] trait Digimon") ·
/// "Choose 1 of your Digimon. ~ with DP less than or equal to the chosen Digimon" · 테이머 대상 ("Delete 1 of your opponent's play cost 3 or lower Digimon or Tamers" ·
/// "Return 1 of your opponent's Tamers to its owner's hand") · "Play 1 ~ from under your Tamers" 표기 등
/// </summary>
internal static partial class SentenceParser
{
    // =====================================================================
    // 셈
    // =====================================================================

    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter34(string counter)
    {
        Match m;
        string c0 = counter.Trim();
        int Per(Match x) => x.Groups["n"].Success && x.Groups["n"].Value.Length > 0 ? Math.Max(1, int.Parse(x.Groups["n"].Value)) : 1;
        // "color in its digivolution cards" · "color in this Digimon's digivolution cards" — 진화원의 색 수
        if (Regex.IsMatch(c0, @"^colou?r in (?:its|this Digimon's) digivolution cards$", I))
            return (_, _, self) => self == null ? 0 : self.EvolutionSources.SelectMany(s => s.AllColors).Distinct().Count();
        // "of its colors" · "of this Digimon's colors"
        if (Regex.IsMatch(c0, @"^of (?:its|this Digimon's) colou?rs$", I))
            return (_, _, self) => self == null ? 0 : self.TopCard.AllColors.Distinct().Count();
        // "of its digivolution cards"
        if (Regex.IsMatch(c0, @"^of its digivolution cards$", I))
            return (_, _, self) => self?.EvolutionSources.Count ?? 0;
        // "card with the [X Antibody] trait in this Digimon's digivolution cards" · "Digimon card with the [Insectoid] trait in this Digimon's digivolution cards" · "2 [Argomon] in its digivolution cards"
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?(?<pick>.+?) in (?:its|this Digimon's) digivolution cards$", I)).Success
            && (ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value)) is { } sp)
        {
            int per = Per(m);
            return (_, _, self) => self == null ? 0 : self.EvolutionSources.Count(sp.Match) / per;
        }
        // "Digimon card with [Etemon] in its name in your trash" · "card in your trash with the [Cyborg] or [Machine] trait" · "[Eyesmon: Scatter Mode] in your trash"
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?(?<pick>.+?) in your trash$", I)).Success
            && (ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value)) is { } tp)
        {
            int per = Per(m);
            return (o, _, _) => o.Trash.Count(tp.Match) / per;
        }
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?card in your trash (?<with>with .+)$", I)).Success
            && ParseCardPick("1 card " + m.Groups["with"].Value) is { } tw)
        {
            int per = Per(m);
            return (o, _, _) => o.Trash.Count(tw.Match) / per;
        }
        // "other Digimon you have in play with the same name as this Digimon"
        if (Regex.IsMatch(c0, @"^other Digimon you have in play with the same name as this Digimon$", I))
            return (o, _, self) => self == null ? 0 : o.BattleArea.Count(d => !ReferenceEquals(d, self) && string.Equals(CardNames.MatchName(d.TopCard), CardNames.MatchName(self.TopCard), StringComparison.OrdinalIgnoreCase));
        // "[Diaboromon] you have" · "[Diaboromon] you have in play"
        if ((m = Regex.Match(c0, $@"^(?<x>{TargetSpec.NameList}) you have(?: in play)?$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (o, _, _) => o.BattleArea.Count(d => xs.Any(x => CardNames.Is(d.TopCard, x)));
        }
        // "of your Digimon with the [Appmon] trait" · "of your [Hudie] trait Digimon" · "of your Digimon with [Chuumon], [Sukamon] or [Etemon] in their names"
        if ((m = Regex.Match(c0, @"^of your (?<other>other )?(?<spec>(?:[^,]*? )?Digimon(?: with .+)?)$", I)).Success
            && TargetSpec.Parse("all of your " + (m.Groups["other"].Success ? "other " : "") + m.Groups["spec"].Value) is { } ys && ys.ContextFilters.Count <= (m.Groups["other"].Success ? 1 : 0))
        {
            bool other = m.Groups["other"].Success;
            return (o, _, self) => o.BattleArea.Count(d => (!other || !ReferenceEquals(d, self)) && ys.Filters.All(f => f(d)));
        }
        // "of your Digimon and Tamers"
        if (Regex.IsMatch(c0, @"^of your Digimon and Tamers(?: in play)?$", I))
            return (o, _, _) => o.BattleArea.Count + o.Tamers.Count;
        // "suspended Digimon" (양쪽)
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?suspended Digimon$", I)).Success)
        {
            int per = Per(m);
            return (o, e, _) => o.BattleArea.Concat(e.BattleArea).Count(d => d.IsSuspended) / per;
        }
        return null;
    }

    /// <summary>"white Digimon" · "[Apocalymon]" · "Digimon cards with [Lucemon] in their name" → 진화할 수 있는 카드</summary>
    internal static Func<CardData, bool>? OnlyIntoFilter34(string what)
    {
        Match m;
        what = what.Trim().TrimEnd('.');
        if ((m = Regex.Match(what, $@"^(?<x>{TargetSpec.NameList})$", I)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return x => xs.Any(n => CardNames.Is(x, n)); }
        if ((m = Regex.Match(what, $@"^(?<c>{Colors}) Digimon(?: cards?)?$", I)).Success)
        { var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); return x => x.AllColors.Contains(col); }
        string single = Regex.Replace(Regex.Replace(what, @"\bcards\b", "card", I), @"\bin their names?\b", "in its name", I);
        return ParseCardPick("1 " + single)?.Match;
    }

    // =====================================================================
    // "Choose 1 of your Digimon." — 고른 디지몬을 다음 문장이 쓴다 ("the chosen Digimon" · "that Digimon" · "it")
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ChooseAction34(string s, string label, bool security)
    {
        var m = Regex.Match(s, @"^Choose (?<t>(?:1|one) of your (?!opponent).+?|(?:1|one) of your opponent's .+?)$", I);
        if (!m.Success) return null;
        var target = TargetSpec.Parse(m.Groups["t"].Value);
        if (target == null || target.Count != 1 || target.UpTo) return null;
        target.PreferOwnSide = !target.Opponent;
        return (c =>
        {
            var d = Pick(target, c, security).FirstOrDefault();
            if (d == null) { c.LastStepResolved = false; c.LastTargets = new List<CardInstance>(); Emit(c, label, "고를 디지몬이 없습니다"); return; }
            c.LastTargets = new List<CardInstance> { d };
            c.Chosen34 = d;
            Emit(c, label, $"[{d.TopCard.Name}]을(를) 선택했습니다");
        }, null);
    }

    // =====================================================================
    // 테이머도 고르는 대상
    // =====================================================================

    /// <summary>
    /// "Delete 1 of your opponent's play cost 3 or lower Digimon or Tamers" · "Return 1 of your opponent's Tamers to its owner's hand" ·
    /// "Delete 1 of your opponent's Digimon or Tamers with the lowest play cost" — 디지몬과 테이머를 함께 후보로 (테이머는 등장 코스트 · 색 · 특징 조건만)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? TamerTargetAction34(string s, string label, bool security)
    {
        var m = Regex.Match(s, @"^(?<v>Delete|Suspend|Return) (?<t>(?<n>1|one|up to \d+|all) of (?:your opponent's|their) (?<body>.*?\b(?:Digimon or Tamers|Tamers)\b.*?))(?<to> to (?:the|its owner's|their owners'?|their|the owner's) hands?| to the bottom of (?:the|its owner's|their owners'?|the owner's) decks?)?$", I);
        if (!m.Success) return null;
        string verb = m.Groups["v"].Value.ToLowerInvariant();
        bool toHand = m.Groups["to"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase), toDeck = m.Groups["to"].Value.Contains("deck", StringComparison.OrdinalIgnoreCase);
        if (verb == "return" && !toHand && !toDeck) return null;
        if (verb != "return" && m.Groups["to"].Success) return null;
        string body = m.Groups["body"].Value;
        bool tamersOnly = !Regex.IsMatch(body, @"\bDigimon or Tamers\b", I);
        string digiPhrase = Regex.Replace(m.Groups["t"].Value, tamersOnly ? @"\bTamers\b" : @"\bDigimon or Tamers\b", "Digimon", I);
        var spec = TargetSpec.Parse(digiPhrase);
        if (spec == null || spec.AnySide || !spec.Opponent) return null;
        // 테이머 조건: DP 조건이 있으면 읽지 않는다 (테이머에는 DP가 없다)
        if (Regex.IsMatch(digiPhrase, @"\bDP\b|digivolution cards|\blevel\b", I)) return null;
        string n = m.Groups["n"].Value.ToLowerInvariant();
        int count = n == "all" ? 0 : int.Parse(Regex.Match(n == "one" ? "1" : n, @"\d+").Value);
        bool upTo = n.StartsWith("up to");
        return (c =>
        {
            // 후보: 상대의 디지몬(조건) + 상대의 테이머(조건) — 테이머는 가짜 디지몬으로 조건을 본다
            var pool = new List<CardInstance>();
            if (!tamersOnly) pool.AddRange(c.Enemy.BattleArea.Where(d => spec.Filters.All(f => f(d)) && spec.ContextFilters.All(f => f(c, d)) && !c.Engine.IsUnaffectedBy(c.Owner, d)));
            var tamerOf = new Dictionary<CardInstance, CardData>(ReferenceEqualityComparer.Instance);
            foreach (var t in c.Enemy.Tamers.Distinct().Where(t => verb != "suspend" || c.Enemy.HasActiveTamer(t)))
            {
                var fake = new CardInstance(t, 0) { IsSuspended = !c.Enemy.HasActiveTamer(t) };
                if (spec.Filters.All(f => f(fake)) && spec.ContextFilters.All(f => f(c, fake))) { pool.Add(fake); tamerOf[fake] = t; }
            }
            IEnumerable<CardInstance> cands = pool;
            if (spec.Selector != null) cands = spec.Selector(cands);
            var ranked = cands.OrderByDescending(d => tamerOf.ContainsKey(d) ? 0 : 1).ThenByDescending(d => d.TopCard.PlayCost).ThenByDescending(d => d.GetCurrentDP()).ToList();
            if (verb == "suspend") ranked = ranked.Where(d => tamerOf.ContainsKey(d) || !d.IsSuspended).ToList();
            var picks = count == 0 ? ranked : c.Engine.ChooseTargets(c.Owner, ranked, count, $"[{c.Card.Name}] 효과의 대상(상대의 디지몬 또는 테이머 {count}장{(upTo ? "까지" : "")})을 고르세요.", upTo, null);
            var names = new List<string>();
            foreach (var d in picks)
            {
                if (tamerOf.TryGetValue(d, out var t))
                {
                    switch (verb)
                    {
                        case "delete": c.Engine.DeleteTamer(c.Enemy, t); break;
                        case "suspend": c.Engine.SuspendTamer(c.Enemy, t); break;
                        default: if (toHand) c.Engine.ReturnTamerToHand(c.Enemy, t); else c.Engine.MoveTamerToDeckBottom34(c.Enemy, t); break;
                    }
                    names.Add(t.Name);
                    continue;
                }
                switch (verb)
                {
                    case "delete": if (c.DeleteEnemy(d)) names.Add(d.TopCard.Name); break;
                    case "suspend": c.Engine.Suspend(d); names.Add(d.TopCard.Name); break;
                    default: if (toHand) c.Engine.ReturnToHand(c.Enemy, d); else c.Engine.BottomDeck(c.Enemy, d); names.Add(d.TopCard.Name); break;
                }
            }
            c.LastTargets = picks.Where(d => !tamerOf.ContainsKey(d)).ToList();
            if (picks.Count == 0) c.LastStepResolved = false;
            Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] {(verb == "delete" ? "소멸" : verb == "suspend" ? "레스트" : toHand ? "패로" : "덱 아래로")}" : "대상이 없습니다");
        }, null);
    }

    // =====================================================================
    // 그 밖의 동작 (ParseAction34에서 부른다)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? MoreAction34(string s, CardData card, List<EffectTrigger> triggers, string label, bool security)
    {
        Match m;
        if (ChooseAction34(s, label, security) is { } choose) return choose;
        if (TamerTargetAction34(s, label, security) is { } tamer) return tamer;
        // "Trash 2 cards in your hand to end the attack" — 비용 + 어택 종료
        if ((m = Regex.Match(s, @"^(?<verb>trash|suspend|delete|return|place)\b(?<obj>[^,]*?) to (?<rest>end (?:the|that) attack)$", I)).Success)
        {
            var step = ParseStep($"By {Gerund(m.Groups["verb"].Value)}{m.Groups["obj"].Value}, {m.Groups["rest"].Value}.", card, triggers);
            if (step.Step != null) return (step.Step.Act, null);
        }
        return null;
    }
}
