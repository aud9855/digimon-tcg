using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.39.0 미지원 문장 13차 묶음: 강제 어택 ("that Digimon attacks") · 진화원에서 등장 (자신의 디지몬 여럿 · 패 또는 진화원) ·
/// 시큐리티 · "At the end of the battle, ~" 등. 원래 규칙(1~12차)이 읽지 못한 문장 · 비용 · 조건만 13차 규칙으로 다시 읽는다 (엔진: GoldfishEngine.Batch39.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _fallbackDepth39;

    // =====================================================================
    // 1. 문장 — 원래 규칙(12차까지)이 읽지 못한 문장만 (ParseStep)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback39(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth39 >= 3) return null;
        _fallbackDepth39++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites39(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            if (GetAndAttack39(s, card, triggers) is { } ga) return (ga, null);
            if (EndOfBattle39(s, card, triggers) is { } eob) return (eob, null);   // Batch39Sec
            return null;
        }
        finally { _fallbackDepth39--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다)</summary>
    internal static IEnumerable<string> Rewrites39(string s)
    {
        foreach (var sec in SecurityRewrites39(s)) yield return sec;   // SentenceParser.Batch39Sec.cs
        // ", other than [Sea Animal] trait," — "~ in any of its traits other than [Sea Animal]" (BT25-009 · EX13-042 · BT24-043 · EX13-038)
        string t = TargetSpec.NormalizeOtherThan39(s);
        if (t != s) yield return t;
        // 진화: "This Digimon can digivolve into ~" (BT11-073 — 공식 "~ 진화할 수 있다") · "into a Digimon with [Justimon] in its name ~ for a memory cost of 2" (BT10-067)
        if (Regex.IsMatch(s, @"\b(?:can|may) digivolve into\b", I))
        {
            string d = Regex.Replace(s, @"\b(?<t>[Tt]his Digimon) can digivolve into\b", "${t} may digivolve into", I);
            d = Regex.Replace(d, @"\binto a Digimon with\b", "into a Digimon card with", I);
            d = Regex.Replace(d, @"\bfor a memory cost of (?<n>\d+)", "for a digivolution cost of ${n}", I);
            d = Regex.Replace(d, @"\bfor (?<n>\d+) memory\b", "for a digivolution cost of ${n}", I);   // BT10-041 "for 1 memory" — 공식 "1 코스트 지불하여"
            d = Regex.Replace(d, @"\bfor the (?:digivolution )?cost$", "for its digivolution cost", I);   // BT12-016 · EX3-063 · EX3-008 "for the cost"
            if (d != s) d = Regex.Replace(d, @" from your (?<z>hand|trash)(?= for | without | with |$)", " in your ${z}", I);   // 렌더러는 "in your trash"만 읽는다 (파서는 둘 다)
            if (d != s) yield return d;
        }
    }

    // =====================================================================
    // 2. 동작 (ParseAction38 다음)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction39(string raw, CardData card, List<EffectTrigger> triggers)
    {
        string s = raw.Trim().TrimEnd('.');
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;
        // "At the end of your opponent's next turn, play 1 [Ravemon] from your trash without paying its cost" (BT13-089 · EX4-058 · EX4-071) — 정한 턴의 종료 시에 처리
        if ((m = AtTurnEndActRe39.Match(s)).Success && !AtTurnEndRe39.IsMatch(s))
        {
            string x = m.Groups["x"].Value.Trim();
            var inner = ParseAction(char.ToUpperInvariant(x[0]) + x[1..], card, triggers);
            if (inner.Error == null && inner.Act is { } innerAct)
            {
                var d = m.Groups["next"].Success ? Duration.OpponentsNextTurnEnd : Duration.OpponentsTurnEnd;
                return (c =>
                {
                    c.Engine.ScheduleAct39(() => innerAct(c), c.Owner, Until(c, d));
                    Emit(c, label, $"{(d == Duration.OpponentsNextTurnEnd ? "다음 " : "")}상대의 턴 종료 시에 발휘합니다");
                }, null);
            }
        }
        if (PlayFromZones39(s, label) is { } pfz) return pfz;   // SentenceParser.Batch39Play.cs
        if (SecurityAction39(s, card, label, security) is { } sa39) return sa39;
        if (UnderTamerEvo39(s, label, security) is { } ute) return ute;   // Batch39Play
        if (MoreAction39(s, label, security) is { } more) return more;   // Batch39More
        if (DigimonOrTamerEvo39(s, label) is { } dte) return dte;   // Batch39Play   // SentenceParser.Batch39Sec.cs
        // 강제 어택: "this Digimon attacks a player" (BT19-062) · "that Digimon attacks" (BT21-081) · "1 of your Digimon with the [Dark Dragon] ~ trait attacks a player" (BT20-090) ·
        //  "this Digimon attacks without suspending" (BT23-070 — 공식 Q&A: 어택하지 않을 수 없다)
        if ((m = Regex.Match(s, @"^(?!When\b|If\b|While\b)(?<t>.+?) attacks(?<rest>(?: a player| your opponent's Digimon| an opponent's Digimon)?(?: without suspending)?)$", I)).Success
            && !m.Groups["t"].Value.Contains(" may ", StringComparison.OrdinalIgnoreCase)
            && ForcedAttack39(m.Groups["t"].Value, m.Groups["rest"].Value, triggers, security) is { } forced)
            return (forced, null);
        return null;
    }

    /// <summary>
    /// v0.39.0 강제 어택 동작 (종합 룰 15-9-1 강제 처리 · 공식 Q&A BT21-081 "가능한 만큼 어택합니다"): 대상 디지몬이 어택할 수 있으면 어택하지 않기를 고를 수 없다 — 어택 대상만 고른다.
    /// rest = " a player" · " your opponent's Digimon" · " without suspending"
    /// </summary>
    private static Action<EffectContext>? ForcedAttack39(string who, string rest, List<EffectTrigger> triggers, bool security = false)
    {
        var target = TargetSpec.Parse(who);
        if (target == null || target.Opponent || target.AnySide || target.UpTo) return null;
        string r = rest.Trim().ToLowerInvariant();
        bool noSuspend = r.Contains("without suspending");
        var allowed = r.Contains("digimon") ? EffectAttackTarget.DigimonOnly : r.Contains("player") ? EffectAttackTarget.PlayerOnly : EffectAttackTarget.Any;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        target.PreferOwnSide = true;
        if (!target.IsThat) target.ContextFilters.Add((c, d) => c.Engine.CanEffectAttack(c.Owner, d, noSuspend));   // 어택할 수 있는 디지몬 중에서 고른다
        return c =>
        {
            var attackers = target.IsThis ? (c.Self != null ? new List<CardInstance> { c.Self } : new()) : Pick(target, c, security);
            int attacked = 0;
            foreach (var d in attackers)
                if (c.Engine.EffectAttack(c.Owner, d, allowed, noSuspend, $"{c.Card.Name} {label}", forced: true)) attacked++;
            if (attacked == 0) { c.LastStepResolved = false; Emit(c, label, "어택할 수 없습니다"); }
        };
    }
}

internal static partial class SentenceParser
{
    /// <summary>v0.39.0 카드 고르기 조건: "without [Justimon: Accel Arm] in its name" (BT11-073 — 공식 "「저스티몬: 액셀 암」 이외의") · "without [Alphamon] in its name" (EX13-060)</summary>
    private static (Match M, Func<CardData, bool> Filter)? PickFilter39(string rest)
    {
        var m = Regex.Match(rest, $@"^(?:,\s*)?without (?<x>{TargetSpec.NameList}) in (?:its|their) names?", I);
        if (!m.Success) return null;
        var xs = TargetSpec.Names(m.Groups["x"].Value);
        return (m, c => { bool ok = !xs.Any(x => TargetSpec.NameHas(c, x)); if (!ok) Diag39.Hit(ref Diag39.NameExclude); return ok; });
    }
}

internal static partial class SentenceParser
{
    internal static readonly Regex GetAndAttackRe39 = new(@"^(?<t>.+?) (?<may>may )?gets? (?<dp>[+-]\d+ DP) for the turn and (?:that Digimon |it )?attack(?<rest>(?: a player| your opponent's Digimon)?(?: without suspending)?)$", RegexOptions.IgnoreCase);

    /// <summary>
    /// "1 of your Digimon may get +5000 DP for the turn and attack your opponent's Digimon" (BT24-051 — 공식 Q&A: DP를 플러스한 디지몬은 가능한 한 어택한다) —
    /// may는 효과 전체 (발휘하기로 하면 어택은 강제)
    /// </summary>
    private static Step? GetAndAttack39(string s, CardData card, List<EffectTrigger> triggers)
    {
        var m = GetAndAttackRe39.Match(s);
        if (!m.Success) return null;
        var a = ParseStep($"{m.Groups["t"].Value} gets {m.Groups["dp"].Value} for the turn", card, triggers);
        if (a.Error != null || a.Step == null || ForcedAttack39("that Digimon", m.Groups["rest"].Value, triggers) is not { } atk) return null;
        var sa = a.Step with { Optional = false };
        return new Step(null, c =>
        {
            RunStep(sa, c);
            if (!c.LastStepResolved || c.LastTargets.Count == 0) return;
            atk(c);
        }, m.Groups["may"].Success, s);
    }
}
