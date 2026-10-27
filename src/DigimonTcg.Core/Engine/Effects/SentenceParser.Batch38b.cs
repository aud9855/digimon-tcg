using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.38.0 (2) 키워드 부여 · 다른 표기 · 조건 · 셈: 얻는 ≪가드≫ · ≪디코이 (색)≫ · ≪천승≫ · "~ may gain ＜Rush＞ for the turn and attack a player" ·
/// "gain ＜Piercing＞ and can also attack unsuspended Digimon" · "If your opponent has no Digimon with as many or more digivolution cards as that Digimon" (BT14-091)
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>"Decoy (Black)" · "Decoy (Red/Black)" → ≪디코이≫ 색 키워드 (색 조건만)</summary>
    private static List<Keyword>? DecoyColors38(string name)
    {
        var m = Regex.Match(name.Trim(), @"^Decoy\s*\((?<c>(?:red|blue|yellow|green|black|purple|white)(?:\s*/\s*(?:red|blue|yellow|green|black|purple|white))*)\)$", I);
        if (!m.Success) return null;
        return Regex.Matches(m.Groups["c"].Value, "red|blue|yellow|green|black|purple|white", I).Select(x => x.Value.ToLowerInvariant() switch
        {
            "red" => Keyword.DecoyRed, "blue" => Keyword.DecoyBlue, "yellow" => Keyword.DecoyYellow, "green" => Keyword.DecoyGreen,
            "black" => Keyword.DecoyBlack, "purple" => Keyword.DecoyPurple, _ => Keyword.DecoyWhite,
        }).Distinct().ToList();
    }

    /// <summary>같은 뜻의 다른 표기 (2) — Rewrites38에서 부른다</summary>
    internal static IEnumerable<string> Rewrites38b(string s)
    {
        Match m;
        // "For the turn 1 of your Digimon gains ＜Raid＞ and gets +3000 DP" (BT20-071)
        if ((m = Regex.Match(s, @"^For the turn,? (?<rest>.+)$", I)).Success)
            yield return $"{m.Groups["rest"].Value} for the turn";
        // "1 of your [Rasenmon] gains ＜Security A. +1＞ for the turn" (BT7-100) — 이름만 있는 대상
        if ((m = Regex.Match(s, @"^(?<pre>(?:(?:\d+|all|up to \d+) of )?your )(?<x>\[[^\]]+\]) (?<v>(?:may )?(?:gains?|gets?)) (?<rest>.+)$", I)).Success)
            yield return $"{m.Groups["pre"].Value}{m.Groups["x"].Value} Digimon {m.Groups["v"].Value} {m.Groups["rest"].Value}";
        // "1 of your opponent's Digimon gets -7000 DP and this Digimon gains ＜Security A. +1＞, for the turn" (BT14-041)
        if ((m = Regex.Match(s, @"^(?<a>.+? gets? [+-]\d+ DP) and (?<b>.+? gains? ＜[^＞]+＞),? for the turn$", I)).Success)
            yield return $"{m.Groups["a"].Value} for the turn, and {m.Groups["b"].Value} for the turn";
        // "To 1 of your opponent's Digimon give -2000 DP for the turn for each ~" (BT21-036)
        if ((m = Regex.Match(s, @"^To (?<t>.+?) give (?<dp>[+-]\d+ DP)(?<rest>.*)$", I)).Success)
            yield return $"{m.Groups["t"].Value} gets {m.Groups["dp"].Value}{m.Groups["rest"].Value}";
        // "1 of your Digimon unsuspends and gets +5000 DP until your turn ends" (BT27-067)
        if ((m = Regex.Match(s, @"^(?<t>(?:1|one) of your [^,]+?) unsuspends and (?<v>gets?|gains?) (?<rest>.+)$", I)).Success)
            yield return $"Unsuspend {m.Groups["t"].Value}, and it {m.Groups["v"].Value} {m.Groups["rest"].Value}";
        // "This Digimon with 5 or more digivolution cards may attack without suspending" (AD1-007)
        if ((m = Regex.Match(s, @"^this Digimon with (?<n>\d+) or more digivolution cards (?<rest>may .+)$", I)).Success)
            yield return $"If this Digimon has {m.Groups["n"].Value} or more digivolution cards, this Digimon {m.Groups["rest"].Value}";
        // "trash your top or bottom security card, ＜Draw 1＞ and gain 1 memory" (EX13-025)
        if ((m = Regex.Match(s, @"^(?<a>[^,]+), (?<b>＜Draw \d+＞) and (?<c>gain \d+ memory)$", I)).Success)
            yield return $"{m.Groups["a"].Value}, and {m.Groups["b"].Value}, and {m.Groups["c"].Value}";
        // "this Digimon and 1 of your Digimon with [Greymon] in its name may unsuspend" (AD1-012)
        if ((m = Regex.Match(s, @"^this Digimon and (?<t>(?:1|one) of your (?:other )?[^,]*?Digimon[^,]*?) may unsuspend$", I)).Success)
            yield return $"You may unsuspend this Digimon and unsuspend {m.Groups["t"].Value}";
    }

    /// <summary>
    /// "1 of your Digimon may gain ＜Rush＞ for the turn and attack a player" (BT16-077 · BT20-102) · "The Digimon this effect DNA digivolved may gain ＜Security A. +1＞ for the turn and attack a player" ·
    /// "1 of your Digimon gains ＜Security A. +1＞ for the turn and that Digimon attacks" (EX7-013 — 어택할 수 있으면 어택: 어택은 "may attack"과 같은 처리) — 얻은 디지몬이 어택
    /// </summary>
    private static Step? GainAndAttack38(string s, CardData card, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s, @"^(?<t>.+?) (?<may>may gain|gains?) (?<k>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*) for the turn and (?:that Digimon |it )?(?<atk>attacks?|attack)(?<rest>(?: a player| your opponent's Digimon)?(?: without suspending)?)$", I);
        if (!m.Success) return null;
        // v0.39.0 공식 Q&A BT20-102 · BT24-051 · BT21-081: "may"는 효과 전체 — 발휘하기로 하면 얻은 디지몬은 가능한 만큼 어택한다 (어택하지 않기를 고를 수 없다).
        //  (v0.38.0에서는 얻는 것은 언제나 하고 어택만 고르게 했다)
        var a = ParseStep($"{m.Groups["t"].Value} gains {m.Groups["k"].Value} for the turn", card, triggers);
        if (a.Error != null || a.Step == null || ForcedAttack39("that Digimon", m.Groups["rest"].Value, triggers) is not { } atk) return null;
        var sa = a.Step with { Optional = false };
        return new Step(null, c =>
        {
            RunStep(sa, c);
            if (!c.LastStepResolved || c.LastTargets.Count == 0) return;
            Diag38.Hit(ref Diag38.GainAttack);
            atk(c);
        }, m.Groups["may"].Value.StartsWith("may", StringComparison.OrdinalIgnoreCase), s);
    }

    /// <summary>
    /// v0.38.0 조건: "your opponent has no Digimon with as many or more digivolution cards as that Digimon" (BT14-091 — 공식 "진화원 매수가 선택한 디지몬 이상의 상대의 디지몬이 없다면") ·
    /// "one of your Digimon with [Ravemon] in its name was deleted by this effect" (EX4-071 — 비용으로 소멸시킨 디지몬)
    /// </summary>
    private static Func<EffectContext, bool>? ParseEventCondition38(string cond)
    {
        Match m;
        cond = cond.Trim();
        if ((m = Regex.Match(cond, @"^your opponent has no Digimon (?<f>with .+)$", I)).Success && TargetSpec.Parse("all of your opponent's Digimon " + m.Groups["f"].Value) is { } spec)
            return c => !c.Enemy.BattleArea.Any(d => spec.Filters.All(f => f(d)) && spec.ContextFilters.All(f => f(c, d)));
        if ((m = Regex.Match(cond, @"^(?:one|1) of your (?<t>Digimon .+?) was deleted by this effect$", I)).Success
            && TargetSpec.Parse("all of your " + m.Groups["t"].Value) is { ContextFilters.Count: 0 } own)
            return c => c.RefCard38 is { } r && own.Filters.All(f => f(new CardInstance(r, 0)));
        return null;
    }

    /// <summary>v0.38.0 셈: "digivolution card this Digimon has with the [Hybrid] trait" (BT7-016 — 공식 "이 디지몬의 진화원의 특징으로 「하이브리드체」를 갖는 카드 1장마다")</summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter38(string counter)
    {
        var m = Regex.Match(counter.Trim(), @"^digivolution cards? this Digimon has (?<f>with .+)$", I);
        if (m.Success && ParseCardPick("1 card " + m.Groups["f"].Value) is { } pick)
            return (_, _, self) => self?.SourcesCount(pick.Match) ?? 0;
        return null;
    }

    /// <summary>
    /// v0.38.0 지속 효과 (원래 규칙이 읽지 못한 것만): "All of your Digimon with [Sistermon] in their names or the [Royal Knight] trait gain ＜Piercing＞ and can also attack unsuspended Digimon" (BT20-019) —
    /// 키워드 + "액티브 상태인 상대의 디지몬에게도 어택할 수 있다"
    /// </summary>
    private static bool ParseStatic38(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer)
    {
        var sentences = SplitSentences(body);
        if (sentences.Count != 1) return false;
        string s = sentences[0].Trim().TrimEnd('.');
        var m = Regex.Match(s, @"^(?<pre>(?:While [^,]+, )?)(?<who>all of your (?:[^,]*? )?Digimon(?: with [^,]+?)?) gain (?<kws>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*) and can also attack (?:your opponent's )?unsuspended Digimon$", I);
        if (!m.Success) return false;
        var trial = new Pending();
        if (ParseStatic($"{m.Groups["pre"].Value}{m.Groups["who"].Value} gain {m.Groups["kws"].Value}", inherited, card, qualifier, trial, outer) != null) return false;
        if (ParseStatic($"{m.Groups["pre"].Value}{m.Groups["who"].Value} gain ＜CanAttackUnsuspendedInternal＞", inherited, card, qualifier, trial, outer) != null) return false;
        pending.Registrations.AddRange(trial.Registrations);
        return true;
    }

    /// <summary>"Delete 1 Tamer" (BT10-069 — 양쪽의 테이머 중 1명. 추천: 상대의 등장 코스트가 가장 높은 테이머)</summary>
    private static (Action<EffectContext>? Act, string? Error)? DeleteAnyTamer38(string s, string label)
    {
        if (!Regex.IsMatch(s, @"^Delete (?:1|one) Tamer$", I)) return null;
        return (c =>
        {
            var pool = c.Enemy.Tamers.Select(t => (Zone: c.Enemy, Card: t)).OrderByDescending(x => x.Card.PlayCost)
                .Concat(c.Owner.Tamers.Select(t => (Zone: c.Owner, Card: t)).OrderBy(x => x.Card.PlayCost)).ToList();
            if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "소멸시킬 테이머가 없습니다"); return; }
            int i = pool.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 소멸시킬 테이머를 고르세요.",
                pool.Select(x => $"{(ReferenceEquals(x.Zone, c.Owner) ? "자신" : "상대")}의 [{x.Card.Name}]").ToList(), 0);
            var (zone, t) = pool[Math.Clamp(i, 0, pool.Count - 1)];
            c.Engine.DeleteTamer(zone, t);
            Emit(c, label, $"테이머 [{t.Name}] 소멸");
        }, null);
    }
}
