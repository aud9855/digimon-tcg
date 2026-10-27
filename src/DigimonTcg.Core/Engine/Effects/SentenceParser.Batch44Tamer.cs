using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.44.0 18차 묶음 — 테이머가 대상인 동작 (원래 규칙이 읽지 못한 것만):
///  - "Suspend this Tamer" (BT13-095 · BT17-089 · EX1-066 · BT24-102 — 공식 "이 테이머를 레스트시킨다")
///  - 주인을 말하지 않은 테이머 (양쪽): "Suspend 1 Tamer" (LM-010) · "Suspend 1 non-purple Tamer" (BT25-081) · "delete 1 Tamer" (BT10-069) ·
///    "Return all Tamers to their owners' hands" (BT12-111 · BT16-083) · "Delete 1 blue, yellow, black or white Tamer with a play cost of 3 or less" (BT18-080) ·
///    "Suspend all other Digimon and (all) Tamers" (BT12-057 · EX10-023) · "Unsuspend up to 2 Digimon and/or Tamers" (BT9-069)
///  - 상대의 테이머만 레스트 · 액티브 불가: "1 of your opponent's Tamers can't suspend until the end of their turn" (BT20-024) · "1 of their Tamers can't unsuspend ~" (EX8-041)
///  - ≪마인드 링크≫의 쉼표 목록 · "This Tamer may ＜Mind Link＞ with ~" (BT15-086 · BT24-086 · EX11-070)
/// </summary>
internal static partial class SentenceParser
{
    private const string ColorWords44 = "red|blue|yellow|green|black|purple|white";

    /// <summary>테이머 조건: "non-purple" · "blue, yellow, black or white" · "with a play cost of 3 or less" — 읽지 못하면 null</summary>
    private static Func<CardData, bool>? TamerFilter44(string adj, string with)
    {
        adj = adj.Trim();
        with = with.Trim();
        var fs = new List<Func<CardData, bool>>();
        Match m;
        if (adj.Length > 0)
        {
            if ((m = Regex.Match(adj, $@"^non-(?<c>{ColorWords44})$", I)).Success)
            {
                var col = Enum.Parse<CardColor>(m.Groups["c"].Value, true);
                fs.Add(t => !t.AllColors.Contains(col));
            }
            else if (Regex.IsMatch(adj, $@"^(?:(?:{ColorWords44})(?:,\s*|\s+or\s+|\s+and/or\s+)?)+$", I))
            {
                var cols = Regex.Matches(adj, ColorWords44, I).Select(x => Enum.Parse<CardColor>(x.Value, true)).ToList();
                fs.Add(t => t.AllColors.Any(cols.Contains));
            }
            else if (Regex.IsMatch(adj, @"^(?:suspended|unsuspended)$", I))
            { /* 부르는 쪽이 본다 */ }
            else return null;
        }
        if (with.Length > 0)
        {
            if ((m = Regex.Match(with, @"^with a play cost of (?<n>\d+) or (?<d>less|lower|more|higher)$", I)).Success)
            {
                int n = int.Parse(m.Groups["n"].Value);
                bool less = m.Groups["d"].Value is "less" or "lower";
                fs.Add(t => less ? t.PlayCost <= n : t.PlayCost >= n);
            }
            else return null;
        }
        return t => fs.All(f => f(t));
    }

    private static (Action<EffectContext>? Act, string? Error)? TamerAction44(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;
        // "Suspend this Tamer" (테이머 카드의 효과)
        if (card.Type == CardType.Tamer && Regex.IsMatch(s, @"^Suspend this Tamer$", I))
            return (c =>
            {
                if (!c.Owner.HasActiveTamer(c.Card) || c.Engine.TamerSuspendLocked(c.Owner, c.Card)) { c.LastStepResolved = false; Emit(c, label, "이 테이머를 레스트시킬 수 없습니다"); return; }
                c.Engine.SuspendTamer(c.Owner, c.Card);
                Diag44.Hit("suspendThisTamer");
                Emit(c, label, "이 테이머를 레스트");
            }, null);

        // 양쪽의 테이머: "Suspend 1 Tamer" · "Return all Tamers to their owners' hands" · "Delete 1 blue, yellow, black or white Tamer with a play cost of 3 or less"
        if ((m = Regex.Match(s, $@"^(?<v>Suspend|Delete|Unsuspend|Return) (?<n>1|all|up to \d+) (?<adj>(?:non-(?:{ColorWords44})|(?:(?:{ColorWords44})(?:,\s*|\s+or\s+|\s+and/or\s+)?)+|suspended|unsuspended)\s+)?Tamers?(?<with> with a play cost of \d+ or (?:less|lower|more|higher))?(?<to> to (?:their owners'|its owner's|the|their) hands?| to the bottom of (?:their owners'|its owner's|the|their) decks?)?$", I)).Success)
        {
            string verb = m.Groups["v"].Value.ToLowerInvariant();
            bool toHand = m.Groups["to"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase);
            if (verb == "return" && !m.Groups["to"].Success || verb != "return" && m.Groups["to"].Success) return null;
            var filter = TamerFilter44(m.Groups["adj"].Value, m.Groups["with"].Value);
            if (filter == null) return null;
            string n = m.Groups["n"].Value.ToLowerInvariant();
            int count = n == "all" ? 0 : int.Parse(Regex.Match(n, @"\d+").Value);
            bool upTo = n.StartsWith("up to");
            bool adjSusp = Regex.IsMatch(m.Groups["adj"].Value, @"^\s*suspended", I), adjUnsusp = Regex.IsMatch(m.Groups["adj"].Value, @"^\s*unsuspended", I);
            return (c => BothSideTamers44(c, verb, toHand, count, upTo, t => filter(t), adjSusp, adjUnsusp, label), null);
        }

        // "Suspend all other Digimon and (all) Tamers" (BT12-057 · EX10-023) — 다른 디지몬 전부 + 테이머 전부 (양쪽)
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Unsuspend) all other Digimon and (?:all )?Tamers$", I)).Success)
        {
            var a = ParseAction($"{m.Groups["v"].Value} all other Digimon", card, triggers);
            if (a.Error != null || a.Act == null) return null;
            string verb = m.Groups["v"].Value.ToLowerInvariant();
            var actA = a.Act;
            return (c => { actA(c); BothSideTamers44(c, verb, false, 0, false, _ => true, false, false, label); }, null);
        }

        // "Unsuspend up to 2 Digimon and/or Tamers" (BT9-069) — 양쪽의 디지몬 · 테이머 중에서 (자신의 것을 먼저 추천)
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Unsuspend) (?<n>up to \d+|\d+) Digimon and/or Tamers$", I)).Success)
        {
            bool un = m.Groups["v"].Value.Equals("Unsuspend", StringComparison.OrdinalIgnoreCase);
            int count = int.Parse(Regex.Match(m.Groups["n"].Value, @"\d+").Value);
            bool upTo = m.Groups["n"].Value.StartsWith("up to", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                var tamerOf = new Dictionary<CardInstance, (PlayerZones Z, CardData T)>(ReferenceEqualityComparer.Instance);
                var pool = new List<CardInstance>();
                foreach (var z in new[] { c.Owner, c.Enemy })
                {
                    bool mineSide = ReferenceEquals(z, c.Owner);
                    foreach (var d in z.BattleArea.Where(d => d.TamerProxyOf == null && (un ? d.IsSuspended : !d.IsSuspended) && (mineSide || !c.Engine.IsUnaffectedBy(c.Owner, d))))
                        pool.Add(d);
                    foreach (var t in z.Tamers.Distinct().Where(t => un ? z.SuspendedTamers.Contains(t) && !c.Engine.TamerUnsuspendLocked(z, t) : z.HasActiveTamer(t) && !c.Engine.TamerSuspendLocked(z, t)))
                    {
                        var fake = new CardInstance(t, 0) { IsSuspended = !z.HasActiveTamer(t) };
                        pool.Add(fake);
                        tamerOf[fake] = (z, t);
                    }
                }
                bool Mine(CardInstance d) => tamerOf.TryGetValue(d, out var x) ? ReferenceEquals(x.Z, c.Owner) : c.Owner.BattleArea.Contains(d);
                var ranked = pool.OrderByDescending(d => un ? Mine(d) : !Mine(d)).ThenByDescending(d => tamerOf.ContainsKey(d) ? 0 : d.GetCurrentDP()).ToList();
                var picks = c.Engine.ChooseTargets(c.Owner, ranked, count, $"[{c.Card.Name}] {(un ? "액티브로 할" : "레스트시킬")} 디지몬 · 테이머를 고르세요 ({count}장{(upTo ? "까지" : "")}).", upTo, null);
                var names = new List<string>();
                foreach (var d in picks)
                {
                    if (tamerOf.TryGetValue(d, out var x))
                    {
                        if (un ? c.Engine.UnsuspendTamer38(x.Z, x.T) : (c.Engine.SuspendTamerReturning44(x.Z, x.T))) names.Add("테이머 " + x.T.Name);
                        continue;
                    }
                    if (un) c.Engine.Unsuspend(d); else c.Engine.Suspend(d);
                    names.Add(d.TopCard.Name);
                }
                c.LastTargets = picks.Where(d => !tamerOf.ContainsKey(d)).ToList();
                if (picks.Count == 0) c.LastStepResolved = false;
                if (names.Count > 0) Diag44.Hit("digimonAndOrTamers");
                Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] {(un ? "액티브" : "레스트")}" : "대상이 없습니다");
            }, null);
        }

        // "1 of your opponent's Tamers can't suspend until the end of their turn" (BT20-024) · "1 of their Tamers can't unsuspend ~" (EX8-041) — 상대의 테이머만
        //  · "3 of your opponent's Digimon and Tamers can't suspend ~" (BT15-101 — 공식 "상대의 디지몬/테이머 3마리(명)")
        if ((m = Regex.Match(s, @"^(?<n>\d+|all) of (?<w>your opponent's|their) (?<k>Tamers|Digimon and Tamers) (?:can't|cannot) (?<v>unsuspend|suspend)(?<dur> .+)$", I)).Success)
        {
            var dur = ParseDuration(m.Groups["dur"].Value.Trim(), out var restDur);
            if (dur is Duration.None or Duration.ThisAttack || restDur.Trim().Length > 0) return null;
            var target = TargetSpec.Parse($"{m.Groups["n"].Value} of your opponent's Digimon or Tamers");
            if (target == null) return null;
            if (m.Groups["k"].Value.Equals("Tamers", StringComparison.OrdinalIgnoreCase))
                target.Filters.Add(d => d.TopCard.Type == CardType.Tamer && d.TamerProxyOf == null);
            bool un = m.Groups["v"].Value.Equals("unsuspend", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                TamerLock35(c, target, un, Until(c, dur), label, security);
                Diag44.Hit("tamerLock");
            }, null);
        }

        // ≪마인드 링크≫: 쉼표 목록 · "This Tamer may ＜Mind Link＞ with ~" (EX11-070 — 공식 "이 테이머는 ~ 디지몬 1마리에 《마인드 링크》할 수 있다")
        if (card.Type == CardType.Tamer && (m = Regex.Match(s, @"^(?<may>This Tamer may )?＜Mind Link＞ (?:to|with) (?<t>(?:1|one) of your Digimon with .+)$", I)).Success
            && TargetSpec.Parse(Regex.Replace(m.Groups["t"].Value, @"\bwith (?<x>\[[^\]]+\](?:,\s*\[[^\]]+\])*(?:,? or \[[^\]]+\])) trait$", "with the ${x} trait", I)) is { } mindTarget)
        {
            bool may = m.Groups["may"].Success;
            return (c =>
            {
                if (!c.Owner.Tamers.Contains(c.Card)) { c.LastStepResolved = false; return; }
                var pool = c.Owner.BattleArea.Where(x => x.TamerProxyOf == null && mindTarget.Filters.All(f => f(x)) && mindTarget.ContextFilters.All(f => f(c, x)) && !x.SourcesAny(sc => sc.Type == CardType.Tamer)).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "≪마인드 링크≫할 디지몬이 없습니다"); return; }
                if (may && !c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] ≪마인드 링크≫할까요?", recommended: true)) { c.LastStepResolved = false; return; }
                var d = pool.Count == 1 ? pool[0] : c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] ≪마인드 링크≫할 디지몬을 고르세요.", optional: false, preselected: pool[0]).FirstOrDefault() ?? pool[0];
                c.Owner.Tamers.Remove(c.Card);
                c.Owner.SuspendedTamers.Remove(c.Card);
                d.AddBottomSource(c.Card);
                c.Engine.ClearUnderTamersIfNoTamer(c.Owner);
                c.RecalculateFieldBuffs();
                c.LastTargets = new List<CardInstance> { d };
                Diag44.Hit("mindLink");
                Emit(c, label, $"≪마인드 링크≫ 이 테이머를 [{d.TopCard.Name}]의 진화원 맨 아래에");
            }, null);
        }
        return null;
    }

    /// <summary>양쪽의 테이머 중에서 고르거나 전부 (해로운 동작은 상대의 테이머를 먼저 추천, 액티브는 자신의 것을 먼저)</summary>
    private static void BothSideTamers44(EffectContext c, string verb, bool toHand, int count, bool upTo, Func<CardData, bool> filter, bool onlySuspended, bool onlyUnsuspended, string label)
    {
        var tamerOf = new Dictionary<CardInstance, (PlayerZones Z, CardData T)>(ReferenceEqualityComparer.Instance);
        var pool = new List<CardInstance>();
        foreach (var z in new[] { c.Enemy, c.Owner })
            foreach (var t in z.Tamers.Distinct().Where(filter))
            {
                bool active = z.HasActiveTamer(t);
                if (onlySuspended && active || onlyUnsuspended && !active) continue;
                if (verb == "suspend" && (!active || c.Engine.TamerSuspendLocked(z, t))) continue;
                if (verb == "unsuspend" && (active || c.Engine.TamerUnsuspendLocked(z, t))) continue;
                var fake = new CardInstance(t, 0) { IsSuspended = !active };
                pool.Add(fake);
                tamerOf[fake] = (z, t);
            }
        bool Mine(CardInstance d) => ReferenceEquals(tamerOf[d].Z, c.Owner);
        var ranked = pool.OrderBy(d => verb == "unsuspend" ? (Mine(d) ? 0 : 1) : (Mine(d) ? 1 : 0)).ThenByDescending(d => d.TopCard.PlayCost).ToList();
        var picks = count == 0 ? ranked : c.Engine.ChooseTargets(c.Owner, ranked, count, $"[{c.Card.Name}] 효과의 대상(테이머 {count}명{(upTo ? "까지" : "")})을 고르세요.", upTo, null);
        var names = new List<string>();
        var enemyPicked = new List<CardData>();
        foreach (var d in picks)
        {
            var (z, t) = tamerOf[d];
            bool done = true;
            switch (verb)
            {
                case "delete": c.Engine.DeleteTamer(z, t); break;
                case "suspend": done = c.Engine.SuspendTamerReturning44(z, t); break;
                case "unsuspend": done = c.Engine.UnsuspendTamer38(z, t); break;
                default: if (toHand) c.Engine.ReturnTamerToHand(z, t); else c.Engine.MoveTamerToDeckBottom34(z, t); break;
            }
            if (!done) continue;
            names.Add((ReferenceEquals(z, c.Owner) ? "자신의 " : "상대의 ") + t.Name);
            if (!ReferenceEquals(z, c.Owner)) enemyPicked.Add(t);
        }
        c.LastTargets = new List<CardInstance>();
        c.LastTamers35 = enemyPicked;   // "It can't unsuspend ~" — 상대의 테이머만
        if (names.Count == 0) c.LastStepResolved = false;
        else Diag44.Hit("bothSideTamers");
        Emit(c, label, names.Count > 0 ? $"테이머 [{string.Join(", ", names)}] {(verb == "delete" ? "소멸" : verb == "suspend" ? "레스트" : verb == "unsuspend" ? "액티브" : toHand ? "패로" : "덱 아래로")}" : "대상 테이머가 없습니다");
    }
}
