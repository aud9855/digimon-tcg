using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.44.0 18차 묶음 — 그 밖의 작은 동작 · 문장 (원래 규칙이 읽지 못한 것만):
///  - "Return 1 card from your hand to the top of your deck" (ST14-11) · "Draw cards until there are 6 cards in your hand" (EX7-013)
///  - "Trash your top or bottom security card" (EX13-025) · "gain 1 memory if that Digimon is level 4 or higher, and ＜Draw 1＞ if it is level 3" (RB1-035)
///  - 한 동사 · 여러 대상: "Return 1 of your opponent's level 3, 1 of their level 4 and 1 of their level 5 Digimon to the hand" (P-153) ·
///    "Return 1 of your opponent's suspended Digimon and 1 of their suspended Tamers to the bottom of their owners' decks" (BT9-101)
///  - 진화원 고르기 파기: "Trash up to 2 digivolution cards of 1 of your opponent's Digimon" (EX4-020) · "trash any 1 digivolution card from your opponent's Digimon" (BT25-103) ·
///    "trash any 1 Option card from 1 Digimon's digivolution cards" (EX7-010)
///  - 문장: 두 조건 "If A, or if B, ~" · "If A, and B, ~" (EX4-027 · BT12-016 · BT14-013) · "For each ~, X" 반복 (원래 규칙이 셈을 읽지 못한 것 — BT9-018 등)
/// </summary>
internal static partial class SentenceParser
{
    private static (Action<EffectContext>? Act, string? Error)? MoreAction44(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;
        // 공식 ST14-11 "자신의 패 1장을 덱 위로 되돌린다"
        if (Regex.IsMatch(s, @"^Return 1 card from your hand to the top of your deck$", I))
            return (c =>
            {
                if (c.Owner.Hand.Count == 0) { c.LastStepResolved = false; Emit(c, label, "패가 없습니다"); return; }
                var order = c.Owner.Hand.OrderBy(x => x.Type == CardType.Digimon ? x.Level : 0).ToList();
                int i = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, order, 1, upTo: false, $"[{c.Card.Name}] 덱 위로 되돌릴 패 1장을 고르세요.").FirstOrDefault();
                var x = order[Math.Clamp(i, 0, order.Count - 1)];
                if (!c.Owner.Hand.Remove(x)) { c.LastStepResolved = false; return; }
                c.Owner.MainDeck.Insert(0, x);
                Diag44.Hit("handToDeckTop");
                Emit(c, label, "패 1장을 덱 위로 되돌렸습니다");
            }, null);
        // 공식 EX7-013 "자신의 패가 6장이 될 때까지 드로우한다"
        if ((m = Regex.Match(s, @"^Draw cards until there are (?<n>\d+) cards in your hand$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int k = 0;
                while (c.Owner.Hand.Count < n && k < 20 && c.Draw() != null) k++;
                if (k > 0) Diag44.Hit("drawUntil");
                Emit(c, label, k > 0 ? $"{k}장 드로우" : "드로우하지 않았습니다");
            }, null);
        }
        // "Trash your top or bottom security card" (EX13-025 — 위 또는 아래)
        if (Regex.IsMatch(s, @"^Trash your top or bottom security card$", I))
            return (c =>
            {
                if (c.Owner.SecurityStack.Count == 0) { c.LastStepResolved = false; return; }
                int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 어느 시큐리티를 파기할까요?", new[] { "위", "아래" }, 0);
                int idx = pick == 1 ? c.Owner.SecurityStack.Count - 1 : 0;
                var x = c.Owner.SecurityStack[idx];
                c.Owner.SecurityStack.RemoveAt(idx);
                c.Owner.Trash.Add(x);
                c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: true);
                Diag44.Hit("trashTopOrBottomSecurity");
                Emit(c, label, $"자신의 시큐리티 {(pick == 1 ? "아래" : "위")}의 카드를 파기했습니다");
            }, null);
        // "gain 1 memory if that Digimon is level 4 or higher, and ＜Draw 1＞ if it is level 3" (RB1-035)
        if ((m = Regex.Match(s, @"^gain (?<n>\d+) memory if (?<a>that Digimon is level \d+ or (?:higher|lower)), and ＜Draw (?<d>\d+)＞ if it is (?<lv>level \d+)$", I)).Success
            && ParseEventCondition(m.Groups["a"].Value) is { } ca && ParseEventCondition("that Digimon is " + m.Groups["lv"].Value) is { } cb)
        {
            int n = int.Parse(m.Groups["n"].Value), d = int.Parse(m.Groups["d"].Value);
            return (c =>
            {
                if (ca(c)) { c.AddMemory(n); Diag44.Hit("memoryOrDrawByLevel"); }
                if (cb(c)) { for (int i = 0; i < d; i++) c.Draw(); Diag44.Hit("memoryOrDrawByLevel"); }
            }, null);
        }
        // 한 동사 · 여러 대상 — "Return 1 of your opponent's level 3, 1 of their level 4 and 1 of their level 5 Digimon to the hand" (P-153)
        if ((m = Regex.Match(s, @"^(?<v>Return|Delete|Suspend) 1 of your opponent's level (?<a>\d+), 1 of their level (?<b>\d+) and 1 of their level (?<c>\d+) Digimon(?<to> to .+)?$", I)).Success)
        {
            var acts = new[] { m.Groups["a"].Value, m.Groups["b"].Value, m.Groups["c"].Value }
                .Select(lv => ParseAction($"{m.Groups["v"].Value} 1 of your opponent's level {lv} Digimon{m.Groups["to"].Value}", card, triggers)).ToList();
            if (acts.Any(a => a.Error != null || a.Act == null)) return null;
            var list = acts.Select(a => a.Act!).ToList();
            return (c => { foreach (var a in list) a(c); Diag44.Hit("multiTargetVerb"); }, null);
        }
        // "Return 1 of your opponent's suspended Digimon and 1 of their suspended Tamers to the bottom of their owners' decks" (BT9-101)
        if ((m = Regex.Match(s, @"^Return (?<a>1 of your opponent's .+?Digimon) and (?<b>1 of their .+?Tamers) to the bottom of their owners' decks$", I)).Success)
        {
            var a = ParseAction($"Return {m.Groups["a"].Value} to the bottom of the deck", card, triggers);
            var b = ParseAction($"Return {m.Groups["b"].Value} to the bottom of the deck", card, triggers);
            if (a.Act == null && TamerTargetAction34($"Return {m.Groups["a"].Value} to the bottom of the deck", label, security) is { Act: { } aa }) a = (aa, null);
            if (b.Error != null || b.Act == null) b = TamerTargetAction34($"Return {m.Groups["b"].Value} to the bottom of the deck", label, security) ?? b;
            if (a.Error != null || a.Act == null || b.Error != null || b.Act == null) return null;
            var (actA, actB) = (a.Act, b.Act);
            return (c => { actA(c); actB(c); Diag44.Hit("multiTargetVerb"); }, null);
        }
        // 두 장 등장: "play 1 Tamer card from your hand and 1 [Ukkomon] from your trash without paying the costs" (BT16-083) ·
        //  "Play 1 [Guilmon] and 1 [Takato Matsuki] from your hand and/or trash without paying their memory costs" (EX2-012) — 1장씩
        if ((m = Regex.Match(s, @"^(?<may>you may )?play (?<a>1 [^,]+?) from your (?<za>hand|trash) and (?<b>1 [^,]+?) from your (?<zb>hand|trash) without paying the(?:ir)? (?:memory )?costs?$", I)).Success
            || (m = Regex.Match(s, @"^(?<may>you may )?play (?<a>1 \[[^\]]+\]) and (?<b>1 \[[^\]]+\]) from your (?<za>hand and/or trash|hand or trash) without paying the(?:ir)? (?:memory )?costs?$", I)).Success)
        {
            string za = Regex.Replace(m.Groups["za"].Value, "and/or", "or", I), zb = m.Groups["zb"].Success ? m.Groups["zb"].Value : za;
            var ra = ParseStep($"Play {m.Groups["a"].Value} from your {za} without paying the cost.", card, triggers);
            var rb = ParseStep($"Play {m.Groups["b"].Value} from your {zb} without paying the cost.", card, triggers);
            if (ra.Error == null && ra.Step != null && rb.Error == null && rb.Step != null)
            {
                var (sa, sb) = (ra.Step, rb.Step);
                return (c => { RunStep(sa, c); RunStep(sb, c); Diag44.Hit("twoPlays"); }, null);
            }
        }
        // 진화원을 골라 파기 — "Trash up to 2 digivolution cards of 1 of your opponent's Digimon" (EX4-020) · "trash any 1 digivolution card from your opponent's Digimon" (BT25-103) ·
        //  "trash any 1 Option card from 1 Digimon's digivolution cards" (EX7-010 — 양쪽)
        if ((m = Regex.Match(s, @"^trash (?:(?<up>up to )|any )?(?<n>\d+) (?<kind>digivolution cards?|Option cards?) (?:of|from) (?<who>1 of your opponent's Digimon|your opponent's Digimon|1 Digimon's digivolution cards)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            bool upTo = m.Groups["up"].Success;
            bool optionOnly = m.Groups["kind"].Value.StartsWith("Option", StringComparison.OrdinalIgnoreCase);
            string who = m.Groups["who"].Value.ToLowerInvariant();
            bool oneHost = who.StartsWith("1 ");
            bool anySide = who.Contains("1 digimon's");
            return (c => TrashSources44(c, n, upTo, oneHost, anySide, optionOnly ? x => x.Type == CardType.Option : _ => true, label), null);
        }
        return null;
    }

    /// <summary>디지몬의 진화원을 골라 파기한다 (oneHost = 디지몬 1마리에서만, anySide = 양쪽 디지몬). 겹쳐진 카드 보호를 지킨다. 파기한 매수</summary>
    private static int TrashSources44(EffectContext c, int n, bool upTo, bool oneHost, bool anySide, Func<CardData, bool> match, string label)
    {
        var sides = anySide ? new[] { c.Enemy, c.Owner } : new[] { c.Enemy };
        var hosts = sides.SelectMany(z => z.BattleArea.Where(d => d.TamerProxyOf == null && d.EvolutionSources.Count > 0 && d.GetEvolutionStackBottomUp().Any(match)
                && (ReferenceEquals(z, c.Owner) || !c.Engine.IsUnaffectedBy(c.Owner, d)) && !c.Engine.StackProtected42(z, d)).Select(d => (Z: z, D: d)))
            .OrderByDescending(h => ReferenceEquals(h.Z, c.Enemy)).ThenByDescending(h => h.D.GetCurrentDP()).ToList();
        if (hosts.Count == 0) { c.LastStepResolved = false; Emit(c, label, "파기할 진화원이 없습니다"); return 0; }
        if (oneHost)
        {
            var pick = c.Engine.ChooseTargets(c.Owner, hosts.Select(h => h.D).ToList(), 1, $"[{c.Card.Name}] 진화원을 파기할 디지몬을 고르세요.", optional: false, preselected: hosts[0].D).FirstOrDefault() ?? hosts[0].D;
            hosts = hosts.Where(h => ReferenceEquals(h.D, pick)).ToList();
        }
        var pool = hosts.SelectMany(h => h.D.GetEvolutionStackBottomUp().Reverse().Where(match).Select(x => (h.Z, h.D, X: x))).ToList();
        int want = Math.Min(n, pool.Count);
        var picks = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, pool.Select(p => p.X).ToList(), want, upTo,
            $"[{c.Card.Name}] 파기할 진화원을 고르세요 ({want}장{(upTo ? "까지" : "")}).").Distinct().Where(i => i >= 0 && i < pool.Count).Select(i => pool[i]).ToList();
        int done = 0;
        foreach (var grp in picks.GroupBy(p => p.D))
        {
            var z = grp.First().Z;
            var trashed = new List<CardData>();
            foreach (var p in grp)
                if (grp.Key.TakeSource(y => ReferenceEquals(y, p.X)) != null) { z.Trash.Add(p.X); trashed.Add(p.X); done++; }
            if (trashed.Count > 0) c.Engine.ApplyOverflowPublic34(z, trashed);
        }
        c.RecalculateFieldBuffs();
        c.PaidCount = done;
        if (done == 0) c.LastStepResolved = false;
        else Diag44.Hit("trashChosenSources");
        Emit(c, label, done > 0 ? $"진화원 {done}장을 파기했습니다" : "파기하지 않았습니다");
        return done;
    }

    /// <summary>
    /// 두 조건 · 반복 문장: "If A, or if B, X" (EX4-027 — 둘 중 하나) · "If A, and B, X" (BT12-016 — 둘 다) · "If this Digimon has [X] in its name, or the [Y] trait, X" (BT14-013) ·
    /// "For each C, X" · "X for each C" (원래 규칙이 셈 뒤 동작을 읽지 못한 것 — 동작을 C번 처리, DP 변화는 제외)
    /// </summary>
    private static Step? MoreStep44(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        string s = sentence.Trim().TrimEnd('.');
        s = Regex.Replace(s, @"^Then,?\s+", "", I);
        Match m;
        if ((m = Regex.Match(s, @"^If (?<a>[^,]+), (?<op>or if|or|and) (?<b>[^,]+), (?<rest>.+)$", I)).Success)
        {
            string a = m.Groups["a"].Value, b = m.Groups["b"].Value;
            bool or = m.Groups["op"].Value.StartsWith("or", StringComparison.OrdinalIgnoreCase);
            // "If this Digimon has [Tyrannomon] in its name, or the [Dinosaur] or [Ceratopsian] trait" — 주어를 이어받는다
            if (Regex.IsMatch(b, @"^the \[", I) && Regex.Match(a, @"^(?<subj>this Digimon) has ", I) is { Success: true } sm) b = $"{sm.Groups["subj"].Value} has {b}";
            var ca = ParseEventCondition(a);
            var cb = ParseEventCondition(b);
            if (ca == null || cb == null) return null;
            string rest = m.Groups["rest"].Value;
            var inner = ParseStep(char.ToUpperInvariant(rest[0]) + rest[1..] + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            Func<EffectContext, bool> cond = or ? c => ca(c) || cb(c) : c => ca(c) && cb(c);
            return new Step(c => { bool ok = cond(c); if (ok) Diag44.Hit("twoConditions"); return ok; }, ctx => RunStep(st, ctx), false, sentence.Trim());
        }
        // "For each C, X" / "X for each C" — 셈 C번 반복 (DP 변화 · "for the turn" 기간이 셈 뒤에 붙은 문장은 원래 규칙의 몫)
        if (((m = Regex.Match(s, @"^For (?:each|every) (?<cnt>[^,]+), (?<core>.+)$", I)).Success
             || (m = Regex.Match(s, @"^(?<core>.+?) for (?:each|every) (?<cnt>[^,]+)$", I)).Success)
            && !Regex.IsMatch(m.Groups["core"].Value, @"\bgets? [+-]\d+ DP\b|\bDP\b", I)
            && ParseCounter(m.Groups["cnt"].Value) is { } counter)
        {
            string core = m.Groups["core"].Value;
            var inner = ParseStep(char.ToUpperInvariant(core[0]) + core[1..] + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            return new Step(null, c =>
            {
                int k = Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
                if (k > 0) Diag44.Hit("forEachRepeat");
                else c.LastStepResolved = false;
                for (int i = 0; i < k; i++) RunStep(st, c);
            }, false, sentence.Trim());
        }
        return null;
    }
}
