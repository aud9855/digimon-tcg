using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.40.0 문장 · 동작 — 원래 규칙(13차까지)이 읽지 못한 문장만 (StepFallback40 · ParseAction40)</summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _fallbackDepth40;

    private static (Step? Step, string? Error)? StepFallback40(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth40 >= 3) return null;
        _fallbackDepth40++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites40(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            return null;
        }
        finally { _fallbackDepth40--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다)</summary>
    internal static IEnumerable<string> Rewrites40(string s)
    {
        // "＜Draw 1＞ and memory +1" (BT10-093) → "＜Draw 1＞ and gain 1 memory"
        if (Regex.IsMatch(s, @"\bmemory \+\d+$", I))
            yield return Regex.Replace(s, @"\bmemory \+(?<n>\d+)$", "gain ${n} memory", I);
        // "All Digimon played by this effect gain ＜Blocker＞ until ~" (BT10-084) → "They gain ~" (그 디지몬 = 이 효과로 등장한 디지몬)
        if (Regex.IsMatch(s, @"^All (?:of the )?Digimon played (?:by|with) this effect gain\b", I))
            yield return Regex.Replace(s, @"^All (?:of the )?Digimon played (?:by|with) this effect gain\b", "They gain", I);
        // "~ gains ＜Collision＞ and ＜Piercing＞ for the turn, and attacks" (EX11-063) → "~ for the turn and that Digimon attacks" (강제 어택 — v0.39.0)
        if (Regex.IsMatch(s, @" for the turn, and attacks$", I))
            yield return Regex.Replace(s, @" for the turn, and attacks$", " for the turn and that Digimon attacks", I);
        // 원문 "1 of your Digimon with [Knightmon] text Digimon" (EX13-064) → "~ with [Knightmon] in its text"
        if (Regex.IsMatch(s, @"\bwith \[[^\]]+\] text Digimon\b", I))
            yield return Regex.Replace(s, @"\bwith (?<x>\[[^\]]+\]) text Digimon\b", "with ${x} in its text", I);
        // "this Digimon gains ＜Security A. +1＞ for the turn for each of your opponent's Digimon deleted" (BT8-019 — 공식 "소멸한 상대 디지몬 1마리마다": 엔진은 소멸한 디지몬마다 따로 알린다)
        if (Regex.IsMatch(s, @" for the turn for each of your opponent's Digimon deleted$", I))
            yield return Regex.Replace(s, @" for the turn for each of your opponent's Digimon deleted$", " for the turn", I);
        // "~ from the hand at the bottom of your security stack" (BT16-024) → "from your hand"
        if (Regex.IsMatch(s, @"\bfrom the hand (?=at the bottom of your security stack|on top of your security stack)", I))
            yield return NoListCommas40(Regex.Replace(s, @"\bfrom the hand (?=at the bottom of your security stack|on top of your security stack)", "from your hand ", I));
        // 공개한 카드의 등장 코스트 합계: "play any number of ~ whose total play costs add up to 7 or less among them" (BT11-044 · BT11-056) ·
        //  "play up to 7 play cost's total worth of ~ among them" (BT14-068 · EX7-047) → 합계 상한 표시 ⟦budget:N⟧ (ParseReveal이 1장씩 고른다)
        if (Regex.IsMatch(s, @"\b(?:any number of .+? whose total play costs add up to \d+ or less|up to \d+ play cost's total worth of .+?) among them\b", I))
        {
            string b = Regex.Replace(s, @"\bany number of (?<x>.+?) whose total play costs add up to (?<n>\d+) or less among them\b", "99 ${x} ⟦budget:${n}⟧ among them", I);
            b = Regex.Replace(b, @"\bup to (?<n>\d+) play cost's total worth of (?<x>.+?) among them\b", "99 ${x} ⟦budget:${n}⟧ among them", I);
            yield return b;
        }
        // "If you have 4 or more Tamers in play, ~" (BT12-098)
        if (Regex.IsMatch(s, @"^If you have \d+ or more Tamers in play,", I))
            yield return Regex.Replace(s, @"^If you have (?<n>\d+) or more Tamers in play,", "If you have ${n} or more Tamers,", I);
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction40(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth40 >= 3) return null;
        _fallbackDepth40++;
        try
        {
            string s = raw.Trim().TrimEnd('.');
            if (MultiLevelDelete40(s, triggers) is { } mld) return (mld, null);
            if (SecurityDigivolve40(s, triggers) is { } sdv) return (sdv, null);
            if (TamersDigivolve40(s, triggers) is { } tdv) return (tdv, null);
            if (RevealTopPlay40(s, card, triggers) is { } rtp) return rtp;
            // "Delete this Digimon at the end of the turn" (BT17-011 · BT17-022 — 이 턴의 종료 시에 소멸, 진화해도 같은 디지몬)
            if (Regex.Match(s, @"^Delete (?<t>this Digimon|it|that Digimon) at the end of (?:the|this) turn$", I) is { Success: true } eot)
            {
                bool self = eot.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
                string lbl = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
                return (c =>
                {
                    var list = (self ? (c.Self != null ? new List<CardInstance> { c.Self } : new()) : c.LastTargets.ToList()).Where(c.Owner.BattleArea.Contains).ToList();
                    if (list.Count == 0) { c.LastStepResolved = false; return; }
                    foreach (var x in list) c.Engine.ScheduleDeletionAtTurnEnd(x, c.Owner, c.State.TurnSequence);
                    Diag40.Hit(ref Diag40.EventMisc);
                    Emit(c, lbl, $"[{string.Join(", ", list.Select(x => x.TopCard.Name))}] 턴 종료 시 소멸");
                }, null);
            }
            if (RevealOpponentSecurity40(s, card, triggers) is { } ros) return ros;
            foreach (var alt in Rewrites40(s))
            {
                if (alt == s) continue;
                var r = ParseAction(alt, card, triggers);
                if (r.Error == null && r.Act != null) return r;
                foreach (var f in new Func<string, CardData, List<EffectTrigger>, (Action<EffectContext>? Act, string? Error)?>[] { Compound32, ParseAction33, ParseAction34, ParseAction35, ParseAction36, ParseAction37, ParseAction38, ParseAction39 })
                    if (f(alt, card, triggers) is { Error: null, Act: not null } r2) return r2;
            }
            if (Compound40(s, card, triggers) is { } comp) return (comp, null);
            return null;
        }
        finally { _fallbackDepth40--; }
    }

    /// <summary>"1 of your opponent's level 3, 1 of their level 4 and 1 of their level 5 Digimon" 같은 Lv.마다 1마리 → Lv. 목록 (렌더러도 쓴다). 아니면 null</summary>
    internal static List<int>? MultiLevelPick40(string t)
    {
        if (!Regex.IsMatch(t, @"^1 (?:each )?of (?:your opponent's|their) level \d+", I)) return null;
        var levels = Regex.Matches(t, @"\blevel (\d+)\b", I).Select(x => int.Parse(x.Groups[1].Value)).ToList();
        string rest = Regex.Replace(t, @"\b1 (?:each )?of (?:your opponent's|their)\b|\blevel \d+\b|\bDigimon\b|\band\b|,", " ", I);
        if (levels.Count < 2 || levels.Distinct().Count() != levels.Count || rest.Trim().Length > 0) return null;
        return levels;
    }

    /// <summary>
    /// "Delete 1 of your opponent's level 3, 1 of their level 5 and 1 of their level 7 Digimon" (BT15-081 · BT15-100 · BT19-070 · BT24-075 · EX7-056 · EX7-071) —
    /// Lv.마다 1마리씩 골라 (없는 Lv.는 건너뛴다) 모두 소멸
    /// </summary>
    private static Action<EffectContext>? MultiLevelDelete40(string s, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s, @"^Delete (?<t>1 (?:each )?of (?:your opponent's|their) level .+)$", I);
        if (!m.Success || MultiLevelPick40(m.Groups["t"].Value) is not { } levels) return null;
        var specs = levels.Select(lv => TargetSpec.Parse($"1 of your opponent's level {lv} Digimon")).ToList();
        if (specs.Any(x => x == null)) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        bool security = triggers.Contains(EffectTrigger.Security);
        return c =>
        {
            var victims = new List<CardInstance>();
            foreach (var spec in specs)
                victims.AddRange(DeletionCap.With(c.Engine.DeletionCapBonus(c.Owner, c.Self), () => Pick(spec!, c, security)).Where(v => !victims.Contains(v)));
            c.LastTargets = victims;
            var names = new List<string>();
            foreach (var v in victims)
                if (c.Enemy.BattleArea.Contains(v) && c.DeleteEnemy(v)) names.Add(v.TopCard.Name);
            if (names.Count > 0) Diag40.Hit(ref Diag40.MultiLevelDelete);
            Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸" : "소멸시킬 대상이 없습니다", attackerRemoved: victims.Contains(c.Other!) && names.Count > 0);
        };
    }

    /// <summary>
    /// "A and B" — 두 동작을 차례로 (둘 다 원래 규칙으로 읽힐 때만). 끝의 ", for the turn"은 양쪽에:
    /// "1 of your opponent's Digimon gets -7000 DP and this Digimon gains ＜Security A. +1＞, for the turn" (BT14-041 — 공식 "턴 종료까지 이 디지몬은 《S 어택 +1》을 얻고, 상대의 디지몬 1마리를 DP -7000")
    /// </summary>
    private static Action<EffectContext>? Compound40(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s, @",? and their (?<k>＜[^＞]+＞) effects don't affect it(?= until |$)", " and it isn't affected by their ${k} effects", I);
        // (본문, 양쪽에 붙일 기간) 후보: 그대로 · 끝의 기간 · 앞에 둔 기간 ("Until your opponent's turn ends, ~" — EX13-034)
        var tries = new List<(string Body, string Tail)>();   // 기간이 있으면 양쪽에 붙인 읽기를 먼저 (공식 BT19-035 · EX8-073 "상대의 턴 종료까지 ~하고, ~")
        if (Regex.Match(s, @"^(?<body>.+?),?\s+(?<d>for the turn|until (?:the end of )?your opponent's turn(?: ends)?)$", I) is { Success: true } tm)
            tries.Add((tm.Groups["body"].Value, " " + tm.Groups["d"].Value));
        if (Regex.Match(s, @"^(?<d>Until (?:the end of )?your opponent's turn(?: ends)?|For the turn), (?<body>.+)$", I) is { Success: true } lm)
            tries.Add((lm.Groups["body"].Value, " " + char.ToLowerInvariant(lm.Groups["d"].Value[0]) + lm.Groups["d"].Value[1..]));
        tries.Add((s, ""));
        foreach (var (body, tail) in tries)
            foreach (Match m in Regex.Matches(body, @",? and (?=(?:this Digimon|1 of your|all of your|it|gains?|gets?|unsuspends?)\b)", I))
            {
                string a = body[..m.Index].Trim() + tail, b = body[(m.Index + m.Length)..].Trim() + tail;
                // 주어 없는 뒤 동작 "This Digimon unsuspends and gains ＜Security A. +1＞ for the turn" (EX8-055) — 같은 디지몬
                if (Regex.IsMatch(b, @"^(?:gains?|gets?|unsuspends?)\b", I))
                {
                    if (!Regex.IsMatch(a, @"^(?:this Digimon|it|that Digimon|1 of your [^,]*?Digimon[^,]*?) (?:gains?|gets?|unsuspends?)\b", I)) continue;
                    b = (Regex.IsMatch(a, @"^this Digimon\b", I) ? "this Digimon " : "it ") + Regex.Replace(b, @"^(gain|get|unsuspend)\b", "$1s", I);
                }
                if (a.Length == 0 || b.Length == 0) continue;
                if (Regex.IsMatch(a, @"\battacks?\b", I)) continue;   // 앞 동작이 어택이면 나누지 않는다 (어택은 효과 처리 중에 일어나 뒤 동작보다 먼저 끝난다)
                var ra = ParseAction(char.ToUpperInvariant(a[0]) + a[1..], card, triggers);
                var rb = ParseAction(char.ToUpperInvariant(b[0]) + b[1..], card, triggers);
                if (ra.Error == null && ra.Act is { } actA && rb.Error == null && rb.Act is { } actB)
                    return c => { actA(c); actB(c); };
            }
        return null;
    }
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 문장 묶기 (MergeRevealSentences 끝) — 렌더러도 같은 묶음을 본다
    // =====================================================================

    private static List<string> MergeSentences40(List<string> sentences)
    {
        var result = new List<string>();
        for (int i = 0; i < sentences.Count; i++)
        {
            // 한 문장으로 붙은 "Search your security stack. 1 of your Digimon may digivolve in to ~ among them ~" (BT14-093 — 숫자로 시작해 나뉘지 않았다)
            if (Regex.Match(sentences[i], @"^(?<a>(?:If [^,]+, )?search your security stack)\. (?<b>1 of your .+? may digivolve (?:in ?to|into) .+ among them\b.*)$", I) is { Success: true } one)
            {
                result.Add($"{one.Groups["a"].Value}, and {one.Groups["b"].Value}");
                continue;
            }
            // "Search your security stack. This Digimon may digivolve into ~ among them ~." (BT14-033 · BT14-093 · BT16-024) → 한 문장
            if (Regex.IsMatch(sentences[i], @"^(?:If [^,]+, )?search your security stack\.?$", I) && i + 1 < sentences.Count
                && Regex.IsMatch(sentences[i + 1], @"\bmay digivolve (?:in ?to|into) .+ among them\b", I))
            {
                result.Add($"{sentences[i].TrimEnd('.')}, and {char.ToLowerInvariant(sentences[i + 1][0])}{sentences[i + 1][1..]}");
                i++;
                continue;
            }
            // "Reveal the top 4 cards of your deck. Place those cards on the top or bottom of your deck in any order." (ST13-16) → 보기만 하는 공개 (원래 규칙의 한 문장)
            if (Regex.Match(sentences[i], @"^(?<a>Reveal the top \d+ cards of your deck)\.?$", I) is { Success: true } rv && i + 1 < sentences.Count
                && Regex.IsMatch(sentences[i + 1], @"^Place (?:those|the revealed) cards (?:on|at) the top or (?:the )?bottom of (?:your|the) deck(?: in any order)?\.?$", I))
            {
                result.Add($"{rv.Groups["a"].Value}, and return them to the top or bottom of the deck.");
                i++;
                continue;
            }
            // "Reveal the top card of your deck. If that card is ~, play it without paying the cost. Add the rest to the hand." (EX5-042) → 한 문장
            if (Regex.IsMatch(sentences[i], @"^Reveal the top card of your deck\.?$", I) && i + 2 < sentences.Count
                && Regex.IsMatch(sentences[i + 1], @"^If that card is [^,]+, play it without paying (?:the|its) (?:memory )?cost\.?$", I)
                && Regex.IsMatch(sentences[i + 2], @"^Add the rest to (?:the|your) hand\.?$", I))
            {
                result.Add($"{sentences[i].TrimEnd('.')}. {sentences[i + 1].TrimEnd('.')}. {sentences[i + 2].TrimEnd('.')}");
                i += 2;
                continue;
            }
            // "Reveal the top card of your opponent's security stack. If that card is ~, ~. Place the revealed card on ~ face down." (P-078 · RB1-027) → 한 문장
            if (Regex.IsMatch(sentences[i], @"^Reveal the top card of your opponent's security stack\.?$", I))
            {
                int j = i + 1;
                while (j < sentences.Count && Regex.IsMatch(sentences[j], @"^If that card is ", I)) j++;
                if (j > i + 1 && j < sentences.Count && Regex.IsMatch(sentences[j], @"^Place the revealed card (?:on|at) (?:the )?top(?: or bottom)? of your opponent's security stack face down\.?$", I))
                {
                    result.Add(string.Join(" ", sentences.Skip(i).Take(j - i + 1)));
                    i = j;
                    continue;
                }
            }
            result.Add(sentences[i]);
        }
        return result;
    }

    // =====================================================================
    // 조건 (ParseEventCondition 끝)
    // =====================================================================

    private static Func<EffectContext, bool>? ParseEventCondition40(string cond)
    {
        string c0 = cond.Trim();
        // "If digivolved by this effect" · "If this effect digivolved" (BT14-033 · BT16-024 · BT17-011 · BT17-022 · BT17-085) — 이 효과를 처리하는 동안 효과로 진화했다
        if (Regex.IsMatch(c0, @"^(?:digivolved by this effect|this effect digivolved|this Digimon digivolved by this effect)$", I))
            return c => c.Engine.EffectEvolveSerial40 != c.EvolveSerialAtStart40;
        // "If you deleted a Digimon with this effect" (BT14-018) — 이 효과로 고른 디지몬이 배틀 에어리어를 떠났다
        if (Regex.IsMatch(c0, @"^you deleted (?:a|any|1) Digimon (?:with|by) this effect$", I))
            return c => c.LastTargets.Any(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d));
        // "you have Tamer with [T.K. Takaishi] in its name" (BT14-093 원문 — 관사 없음)
        if (Regex.IsMatch(c0, @"^you have Tamer with ", I)) return ParseEventCondition(Regex.Replace(c0, @"^you have Tamer with ", "you have a Tamer with ", I));
        // "A and B" — 둘 다 (앞의 "digivolved by this effect"처럼 이 묶음에서 읽는 조건이 들어 있을 때)
        var and = Regex.Match(c0, @"^(?<a>.+?) and (?<b>you .+)$", I);
        if (and.Success && ParseEventCondition(and.Groups["a"].Value) is { } ca && ParseEventCondition(and.Groups["b"].Value) is { } cb)
            return c => ca(c) && cb(c);
        return null;
    }

    // =====================================================================
    // 시큐리티에서 진화 (BT14-033 · BT14-093 · BT16-024): 시큐리티를 보고, 그중 조건에 맞는 카드로 진화 — 플레이어는 시큐리티 카드 목록에서 고른다
    // =====================================================================

    internal static readonly Regex SecurityDigivolveRe40 = new(
        @"^Search your security stack, and (?<who>this Digimon|1 of your (?<spec>[^,]*?Digimon(?: with [^,]+?)?)) may digivolve (?:in ?to|into) (?<pick>.+?) among them (?:(?<free>without paying (?:the|its) (?:digivolution |memory )?cost)|with the digivolution cost reduced by (?<n>\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Action<EffectContext>? SecurityDigivolve40(string s, List<EffectTrigger> triggers)
    {
        var m = SecurityDigivolveRe40.Match(s);
        if (!m.Success) return null;
        string pickText = Regex.Replace(m.Groups["pick"].Value.Trim(), @"^an? ", "1 ", I);
        pickText = Regex.Replace(pickText, @"\bDigimon(?! card)\b", "Digimon card", I);
        if ((ParseCardPick(pickText) ?? ParseNamePick(pickText)) is not { } pick) return null;
        TargetSpec? who = null;
        if (m.Groups["spec"].Success && (who = TargetSpec.Parse("all of your " + m.Groups["spec"].Value)) is not { Opponent: false }) return null;
        bool free = m.Groups["free"].Success;
        int reduce = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 0;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return c =>
        {
            var bodies = who == null
                ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new List<CardInstance> { c.Self } : new List<CardInstance>())
                : c.Owner.BattleArea.Where(d => who.Filters.All(f => f(d)) && who.ContextFilters.All(f => f(c, d))).ToList();
            bool CanInto(CardInstance d, CardData x) => !EffectRules.IsDigivolveLocked(c.Engine.State, d) && CardScripts.EvoAllowed(d, x) && ActionValidator.ValidateEvolution(d, x).CanExecute;
            var pool = c.Owner.SecurityStack.Where(x => x.Type == CardType.Digimon && pick.Match(x) && bodies.Any(d => CanInto(d, x)))
                .Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
            Diag40.Hit(ref Diag40.SecuritySearch);
            if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "시큐리티에 진화할 수 있는 카드가 없습니다"); return; }
            var chosen = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: true, $"[{c.Card.Name}] 시큐리티를 확인했습니다 — 진화할 카드를 고르세요 (고르지 않아도 됩니다).").FirstOrDefault();
            if (chosen == null) { c.LastStepResolved = false; Emit(c, label, "시큐리티의 카드로 진화하지 않았습니다"); return; }
            var targets = bodies.Where(d => CanInto(d, chosen)).ToList();
            var target = targets.Count == 1 ? targets[0]
                : c.Engine.ChooseTargets(c.Owner, targets.OrderByDescending(d => d.TopCard.Level).ToList(), 1, $"[{c.Card.Name}] [{chosen.Name}]으로 진화할 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
            if (target == null) { c.LastStepResolved = false; return; }
            int cost = free ? 0 : Math.Max(0, ActionValidator.EvolutionCost(target, chosen) - reduce);
            int before = c.Engine.EffectEvolveSerial40;
            c.Engine.EvolveByEffect(c.Owner, target, chosen, cost, from: c.Owner.SecurityStack);
            if (c.Engine.EffectEvolveSerial40 == before) { c.LastStepResolved = false; return; }
            c.Engine.NotifySecurityRemoved(c.Owner, chosen);
            c.LastTargets = new List<CardInstance> { target };
            Emit(c, label, $"시큐리티의 [{chosen.Name}]으로 [{target.TopCard.Name}] 진화 (코스트 {cost})");
        };
    }

    // =====================================================================
    // 상대 시큐리티 맨 위 공개 (P-078 · RB1-027): 공개 → "If that card is ~" 처리 → 뒷면으로 맨 위 (또는 아래)에 둔다
    // =====================================================================

    internal static readonly Regex RevealOppSecurityRe40 = new(
        @"^Reveal the top card of your opponent's security stack\. (?<ifs>(?:If that card is [^.]+\. )+)Place the revealed card (?:on|at) (?:the )?top(?<bottom> or bottom)? of your opponent's security stack face down$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (Action<EffectContext>? Act, string? Error)? RevealOpponentSecurity40(string s, CardData card, List<EffectTrigger> triggers)
    {
        var m = RevealOppSecurityRe40.Match(s);
        if (!m.Success) return null;
        var branches = new List<(Func<CardData, bool> When, Action<EffectContext> Act)>();
        foreach (Match b in Regex.Matches(m.Groups["ifs"].Value, @"If that card is (?<what>[^,]+), (?<act>[^.]+)\.", I))
        {
            Func<CardData, bool>? when = RevealedKind40(b.Groups["what"].Value);
            if (when == null) return (null, $"미지원 공개 조건 (If that card is {b.Groups["what"].Value})");
            var act = ParseAction(b.Groups["act"].Value.Trim(), card, triggers);
            if (act.Error != null || act.Act == null) return (null, act.Error ?? "미지원 공개 후 처리");
            branches.Add((when, act.Act));
        }
        bool orBottom = m.Groups["bottom"].Success;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return (c =>
        {
            if (c.Enemy.SecurityStack.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 시큐리티가 없습니다"); return; }
            var top = c.Enemy.SecurityStack[0];
            Diag40.Hit(ref Diag40.RevealVariant);
            c.LastCard = top;
            c.Log($"[{c.Card.Name}] 상대의 시큐리티 맨 위 카드를 공개합니다: [{top.Name}]");
            foreach (var (when, act) in branches)
                if (when(top)) act(c);
            if (orBottom && c.Enemy.SecurityStack.Count > 1 && ReferenceEquals(c.Enemy.SecurityStack[0], top)
                && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 공개한 상대의 시큐리티 [{top.Name}]을(를) 시큐리티 맨 아래에 둘까요? (아니요 = 맨 위 그대로)",
                    recommended: top.Type != CardType.Digimon || top.DP >= 6000))
            {
                c.Enemy.SecurityStack.RemoveAt(0);
                c.Enemy.SecurityStack.Add(top);
                Emit(c, label, $"공개한 [{top.Name}]을(를) 상대의 시큐리티 맨 아래에 뒷면으로");
            }
            else Emit(c, label, $"공개한 [{top.Name}]을(를) 상대의 시큐리티 맨 위에 뒷면으로");
        }, null);
    }

    /// <summary>"a Digimon card" · "a non-Digimon" · "a Tamer card" · "an Option card" (렌더러도 쓴다)</summary>
    internal static Func<CardData, bool>? RevealedKind40(string what) => what.Trim().ToLowerInvariant() switch
    {
        "a digimon card" or "a digimon" => x => x.Type == CardType.Digimon,
        "a non-digimon" or "a non-digimon card" or "not a digimon card" => x => x.Type != CardType.Digimon,
        "a tamer card" => x => x.Type == CardType.Tamer,
        "an option card" => x => x.Type == CardType.Option,
        _ => null,
    };
}

internal static partial class SentenceParser
{
    /// <summary>
    /// v0.40.0 "you have a blue or green Digimon or Tamer" — 색이 디지몬과 테이머 양쪽에 걸린다 (공식 BT17-011 "블루/그린인 자신의 디지몬/테이머가 있다면").
    /// 원래 규칙은 "you have a blue or green Digimon" 또는 "(아무) 테이머"로 나누어 읽어, 다른 색의 테이머로도 조건을 채웠다 (EX6-016 등) — 원래 규칙보다 먼저 본다
    /// </summary>
    /// <summary>v0.40.0 원문 표기 정리: "1 [Thomas H. Norstein] trait" (BT25-021 · BT25-023 — 테이머 카드 이름에 "trait"이 잘못 붙었다. 이름에 마침표가 있어 전에는 문장이 잘려 읽지 못했다)</summary>
    private static string Normalize40(string s) =>
        Regex.Replace(s, @"\[Thomas H\. Norstein\] trait\b", "[Thomas H. Norstein]", I);

    /// <summary>v0.40.0 고르는 카드의 다른 표기 (ParseCardPick 끝): "2 cards that are either black with [Knightmon] or [DeadlyAxemon] in their names or with the [Twilight] trait" (BT10-058)</summary>
    private static (int Count, Func<CardData, bool> Match)? ParseCardPick40(string p)
    {
        var m = Regex.Match(p.Trim(), $@"^(?<n>\d+) cards that are either (?<color>(?:{Colors}) )?(?<a>with .+?) or (?<b>with .+)$", I);
        if (!m.Success) return null;
        string a = Regex.Replace(m.Groups["a"].Value, @"\bin their names\b", "in its name", I);
        var pa = ParseCardPick($"1 {m.Groups["color"].Value}card {a}");
        var pb = ParseCardPick($"1 card {Regex.Replace(m.Groups["b"].Value, @"\bin their names\b", "in its name", I)}");
        if (pa == null || pb == null) return null;
        var (ma, mb) = (pa.Value.Match, pb.Value.Match);
        return (int.Parse(m.Groups["n"].Value), x => ma(x) || mb(x));
    }

    private static Func<EffectContext, bool>? ColorDigimonOrTamer40(string cond)
    {
        var m = Regex.Match(cond.Trim(), $@"^you have (?:a|an) (?<c>(?:{Colors})(?:,? (?:or )?(?:{Colors}))*) Digimon or Tamer(?: in play)?$", I);
        if (!m.Success) return null;
        var colors = Regex.Matches(m.Groups["c"].Value, Colors, I).Select(x => Enum.Parse<CardColor>(x.Value, true)).ToList();
        return c => c.Owner.BattleArea.Any(d => d.Colors.Any(colors.Contains)) || c.Owner.Tamers.Any(t => t.AllColors.Any(colors.Contains));
    }
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 테이머만 진화 (BT21-091 "1 of your Tamers may digivolve into a Digimon card with the [Hybrid] trait in the hand without paying the cost")
    // =====================================================================

    internal static readonly Regex TamersEvoRe40 = new(
        @"^1 of your (?<t>(?:[^,]*? )?Tamers?(?: with [^,]+?)?) may digivolve into (?<pick>.+?) in (?:the|your) (?<from>hand or trash|hand|trash) without paying (?:the|its) (?:digivolution |memory )?cost$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Action<EffectContext>? TamersDigivolve40(string s, List<EffectTrigger> triggers)
    {
        var m = TamersEvoRe40.Match(s);
        if (!m.Success || Regex.IsMatch(m.Groups["t"].Value, @"\bDigimon\b", I)) return null;
        var pick = ParseCardPick(Regex.Replace(m.Groups["pick"].Value, @"^(?:an?|1) ", "1 ", I)) ?? ParseNamePick("1 " + m.Groups["pick"].Value);
        if (pick == null) return null;
        Func<CardData, bool> tamerOk = _ => true;
        string tspec = Regex.Replace(m.Groups["t"].Value, @"^Tamers?$", "", I).Trim();
        if (tspec.Length > 0)
        {
            if (EventCardSubject(m.Groups["t"].Value) is not { } ts) return null;
            tamerOk = ts;
        }
        var match = pick.Value.Match;
        string from = m.Groups["from"].Value.ToLowerInvariant();
        bool hand = from.Contains("hand"), trash = from.Contains("trash");
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return c =>
        {
            var zones = new List<List<CardData>>();
            if (hand) zones.Add(c.Owner.Hand);
            if (trash) zones.Add(c.Owner.Trash);
            var options = new List<(CardData Tamer, CardData Evo, List<CardData> Zone)>();
            foreach (var z in zones)
                foreach (var x in z.Where(x => x.Type == CardType.Digimon && match(x)).Distinct().ToList())
                    foreach (var t in c.Owner.Tamers.Distinct().Where(tamerOk).ToList())
                        if (CardScripts.TamerEvolutionCost(x, t) is int) options.Add((t, x, z));
            if (options.Count == 0) { c.LastStepResolved = false; Emit(c, label, "진화할 수 있는 테이머가 없습니다"); return; }
            options = options.OrderByDescending(o => o.Evo.Level).ThenByDescending(o => o.Evo.DP).ToList();
            var texts = options.Select(o => $"[{o.Tamer.Name}] → [{o.Evo.Name}] ({(ReferenceEquals(o.Zone, c.Owner.Trash) ? "트래시" : "패")})").ToList();
            texts.Add("진화하지 않는다");
            int idx = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 진화시킬 테이머와 카드를 고르세요.", texts, 0);
            if (idx < 0 || idx >= options.Count) { c.LastStepResolved = false; Emit(c, label, "진화하지 않았습니다"); return; }
            var o = options[idx];
            if (c.Engine.EvolveTamerByEffect(c.Owner, o.Tamer, o.Evo, 0, o.Zone) is { } ti) c.LastTargets = new List<CardInstance> { ti };
            else { c.LastStepResolved = false; return; }
            Diag40.Hit(ref Diag40.EventMisc);
            Emit(c, label, $"테이머 [{o.Tamer.Name}] → [{o.Evo.Name}] 진화 (코스트 0)");
        };
    }

    // =====================================================================
    // 덱 위 1장 공개 · 조건에 맞으면 등장 · 아니면 패에 (EX5-042)
    // =====================================================================

    internal static readonly Regex RevealTopPlayRe40 = new(
        @"^Reveal the top card of your deck\. If that card is (?<pick>an? [^,]+?), play it without paying (?:the|its) (?:memory )?cost\. Add the rest to (?:the|your) hand$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (Action<EffectContext>? Act, string? Error)? RevealTopPlay40(string s, CardData card, List<EffectTrigger> triggers)
    {
        var m = RevealTopPlayRe40.Match(s);
        if (!m.Success) return null;
        string pickText = Regex.Replace(m.Groups["pick"].Value, @"^an? ", "1 ", I);
        if ((ParseCardPick(pickText) ?? ParseNamePick(pickText)) is not { } pick) return null;
        var match = pick.Match;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return (c =>
        {
            if (c.Owner.MainDeck.Count == 0) { c.LastStepResolved = false; Emit(c, label, "덱이 없습니다"); return; }
            var top = c.Owner.MainDeck[0];
            c.Owner.MainDeck.RemoveAt(0);
            Diag40.Hit(ref Diag40.RevealVariant);
            if (match(top) && top.Type is CardType.Digimon or CardType.Tamer && c.Engine.TryPlayByEffect(c.Owner, top, PlayOrigin.Revealed, null))
            {
                Emit(c, label, $"덱 위 [{top.Name}] 공개 → 코스트 없이 등장");
                return;
            }
            c.Owner.Hand.Add(top);
            Emit(c, label, $"덱 위 [{top.Name}] 공개 → 패에 추가");
        }, null);
    }
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 비용 (ParseCost 끝)
    // =====================================================================

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost40(string cost, CardData card)
    {
        Match m;
        // "trashing up to 3 cards from the top of your security stack" (P-074) — 몇 장을 낼지 고른다 (낸 매수 = PaidCount)
        if ((m = Regex.Match(cost.Trim(), @"^trashing up to (?<n>\d+) cards? from the top of your security stack$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => c.Owner.SecurityStack.Count > 0,
                    c =>
                    {
                        int max = Math.Min(n, c.Owner.SecurityStack.Count);
                        var opts = Enumerable.Range(1, max).Select(k => $"{k}장").ToList();
                        int rec = Math.Max(0, Math.Min(max, c.Owner.SecurityStack.Count - 3) - 1);
                        int k = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 시큐리티 위에서 몇 장을 파기할까요?", opts, rec) + 1;
                        k = Math.Clamp(k, 1, max);
                        for (int i = 0; i < k && c.Owner.SecurityStack.Count > 0; i++)
                        {
                            var x = c.Owner.SecurityStack[0];
                            c.Owner.SecurityStack.RemoveAt(0);
                            c.Owner.Trash.Add(x);
                            c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: true);
                        }
                        c.PaidCount = k;
                        Diag40.Hit(ref Diag40.SetCost);
                    },
                    c => c.Owner.SecurityStack.Count >= 4, $"시큐리티 위에서 {n}장까지 파기하여");
        }
        // "deleting 1 of your Digimon other than the digivolving one" (EX2-064 — 진화하려는 디지몬은 고르지 않는다)
        if (Regex.IsMatch(cost.Trim(), @"^deleting 1 of your Digimon other than the digivolving one$", I))
        {
            List<CardInstance> Pool(EffectContext c) => c.Owner.BattleArea.Where(d => !ReferenceEquals(d, c.Other)).ToList();
            return (c => Pool(c).Count > 0,
                    c =>
                    {
                        var pool = Pool(c).OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).ToList();
                        var victim = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 비용으로 소멸시킬 자신의 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (victim != null) c.DeleteOwn(victim);
                        Diag40.Hit(ref Diag40.SetCost);
                    },
                    c => Pool(c).Any(d => d.TopCard.Level <= 3 || GoldfishEngine.IsToken(d.TopCard)), "자신의 디지몬 1마리를 소멸시켜");
        }
        return null;
    }
}
