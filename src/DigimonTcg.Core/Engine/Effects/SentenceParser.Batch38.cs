using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.38.0 미지원 문장 12차 묶음: 다른 카드와 비교하는 대상 (TargetSpec.Batch38 · RefCard38) · 액티브 + 다른 동작 · 테이머 액티브 ·
/// 여러 곳에서 고르는 비용 · "When DNA digivolving, ~" (엔진: GoldfishEngine.Batch38.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _fallbackDepth38;

    // =====================================================================
    // 1. 문장 — 원래 규칙이 읽지 못한 문장만 (ParseStep)
    // =====================================================================

    /// <summary>원래 규칙이 읽지 못한 문장을 다른 표기로 다시 읽는다. 읽지 못하면 null (원래 오류를 그대로)</summary>
    private static (Step? Step, string? Error)? StepFallback38(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth38 >= 3) return null;
        _fallbackDepth38++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites38(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            if (GainAndAttack38(s, card, triggers) is { } gainAtk) return (gainAtk, null);
            if (SplitStep38(s, card, triggers) is { } split) return (split, null);
            if (ThisMayUnsuspend38(s) is { } thisMay) return thisMay;
            return null;
        }
        finally { _fallbackDepth38--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다)</summary>
    internal static IEnumerable<string> Rewrites38(string s)
    {
        // "When DNA digivolving, ~" (BT8-042 · ST9-11 — 공식 "조그레스 진화하고 있었을 때,") = "If DNA digivolving, ~"
        if (Regex.IsMatch(s, @"^When DNA digivolving, ", I))
            yield return Regex.Replace(s, @"^When DNA digivolving, ", "If DNA digivolving, ", I);
        // "Until the end of your opponent's turn, 1 of your opponent's Digimon doesn't unsuspend" (BT13-053 · BT13-058 — 공식 "상대의 턴 종료까지 상대의 디지몬 1마리는 액티브가 되지 않는다")
        var m = Regex.Match(s, @"^Until (?<d>the end of (?:your opponent's|their) turn|(?:your opponent's|their) turn ends), (?<t>.+?) (?:doesn't|don't|can't|cannot) (?:unsuspend|be unsuspended)$", I);
        if (m.Success)
        {
            string t = Regex.Replace(m.Groups["t"].Value, @"\btheir\b", "your opponent's", I);
            t = Regex.Replace(t, @"^all of your opponent's Digimon and Tamers$", "all of your opponent's Digimon or Tamers", I);
            string d = m.Groups["d"].Value.StartsWith("the end", StringComparison.OrdinalIgnoreCase) ? "until the end of their turn" : "until their turn ends";
            yield return $"{t} can't unsuspend {d}";
        }
        foreach (var b in Rewrites38b(s)) yield return b;   // SentenceParser.Batch38b.cs
        // "1 of your opponent's [Data] trait Digimon can't be unsuspended until ~" (BT19-046) · "1 of their Tamers can't unsuspend until ~" (EX8-041)
        if (Regex.IsMatch(s, @"\bcan't be unsuspended\b|^(?:\d+|all) of their ", I))
            yield return Regex.Replace(Regex.Replace(s, @"\bcan't be unsuspended\b", "can't unsuspend", I), @"^(?<n>\d+|all) of their ", "${n} of your opponent's ", I);
    }

    /// <summary>
    /// "Unsuspend this Digimon, and gain 1 memory for each red Tamer you have in play" (BT11-017 — 공식 "이 디지몬을 액티브로 하고, ~") ·
    /// "Unsuspend this Digimon, and it gets +2000 DP ~ for each ~" (BT8-038) · "Unsuspend 1 of your Digimon, and for each Tamer ~, return ~" (EX2-024) —
    /// 앞 · 뒤가 각각 읽히는 문장만 나눈다
    /// </summary>
    private static Step? SplitStep38(string s, CardData card, List<EffectTrigger> triggers)
    {
        foreach (Match sep in Regex.Matches(s, @",? and then |, and |, then | and (?=(?:it|they|this Digimon|that Digimon) (?:gets?|gains?|can|unsuspends?|isn't|doesn't)\b|(?:unsuspends?|gain|gains|draw|delete|suspend|return|trash)\b)", I))
        {
            string left = s[..sep.Index].Trim(), right = s[(sep.Index + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            if (!Regex.IsMatch(left, @"^(?:Unsuspend|Suspend|Delete|Return|Trash|Gain|＜Draw|This Digimon|It|1 of your Digimon)\b", I)) continue;
            var a = ParseStep(left, card, triggers);
            if (a.Error != null || a.Step == null) continue;
            string r = char.ToUpperInvariant(right[0]) + right[1..];
            r = Regex.Replace(r, @"^Unsuspends\b", "Unsuspend this Digimon", I);   // "it gains ~ for the turn and unsuspends" (BT18-018)
            var b = ParseStep(r, card, triggers);
            if (b.Error != null || b.Step == null) continue;
            var (sa, sb) = (a.Step, b.Step);
            // 뒤의 "it" · "that Digimon"은 앞 동작이 고른 디지몬 (대상을 고르는 동작은 EffectContext.LastTargets에 남긴다 — EX12-052 "~ gets +3000 DP ~, then it may battle ~")
            return new Step(null, c => { Diag38.Hit(ref Diag38.SplitStep); RunStep(sa, c); RunStep(sb, c); }, false, s);
        }
        return null;
    }

    /// <summary>
    /// "you may unsuspend this Digimon with [Gallantmon] in its name" (BT17-013 진화원 — 공식 "명칭에 「듀크몬」을 포함하는 이 디지몬을 액티브로 할 수 있다") ·
    /// "this Digimon with [Veedramon] in its name may unsuspend" (EX13-002) · "this Digimon with the [Vortex Warriors] trait may unsuspend" (ST22-13) ·
    /// "this blue Digimon with the [TS] trait unsuspends" (BT24-002) · "this [Alphamon: Ouryuken] may unsuspend" (BT27-015) — 조건에 맞는 이 디지몬만
    /// </summary>
    private static (Step? Step, string? Error)? ThisMayUnsuspend38(string s)
    {
        var m = Regex.Match(s, @"^(?:(?<may>you may )unsuspend this (?<adj>[^,]*?)Digimon(?<f> with .+)?|this (?<adj>[^,]*?)Digimon(?<f> with .+?)? (?<may>may )?unsuspends?|this (?<nm>\[[^\]]+\]) (?<may>may )?unsuspends?)$", I);
        if (!m.Success) return null;
        Func<CardInstance, bool> ok;
        if (m.Groups["nm"].Success)
        {
            var names = TargetSpec.Names(m.Groups["nm"].Value);
            ok = d => names.Any(n => CardNames.Is(d.TopCard, n));
        }
        else
        {
            string f = m.Groups["f"].Value.Trim();
            if (f.Length == 0 && m.Groups["adj"].Value.Trim().Length == 0) return null;
            if (TargetSpec.Parse($"all of your {m.Groups["adj"].Value}Digimon {f}".Trim()) is not { } spec || spec.ContextFilters.Count > 0) return null;
            ok = d => spec.Filters.All(x => x(d));
        }
        bool may = m.Groups["may"].Success;
        Action<EffectContext> act = c =>
        {
            if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self) || !ok(c.Self) || !c.Self.IsSuspended) { c.LastStepResolved = false; return; }
            c.Engine.Unsuspend(c.Self);
            Diag38.Hit(ref Diag38.ThisUnsuspend);
            c.LastTargets = new List<CardInstance> { c.Self };
            c.Log($"[{c.Card.Name}] [{c.Self.TopCard.Name}] 액티브");
        };
        return (new Step(c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && ok(c.Self) && c.Self.IsSuspended, act, may, s), null);
    }

    // =====================================================================
    // 2. 동작 (ParseAction37 다음)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction38(string raw, CardData card, List<EffectTrigger> triggers)
    {
        string s = raw.Trim().TrimEnd('.');
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        bool security = triggers.Contains(EffectTrigger.Security);
        if (UnsuspendTamers38(s, label) is { } tamers) return tamers;
        if (DeleteAnyTamer38(s, label) is { } anyTamer) return anyTamer;
        if (SecurityTopBottom38(s, label) is { } secTb) return secTb;   // Batch38c
        if (TopCardToSecurity38(s, label, security) is { } topSec) return topSec;
        if (PlaceSourcesAction38(s, label) is { } placeSrc) return placeSrc;
        if (DeleteChosenAnd38(s, label, security) is { } chosenAnd) return chosenAnd;
        if (ChooseAnd38(s, card, triggers, label, security) is { } chooseAnd) return chooseAnd;
        return null;
    }

    /// <summary>
    /// 테이머를 액티브로: "Unsuspend 1 of your Tamers" (BT10-009) · "Unsuspend this Digimon or 1 of your blue Tamers" (BT12-029 — 공식 "이 디지몬 또는 블루인 자신의 테이머 1명을 액티브로 한다") ·
    /// "Unsuspend this Digimon or Tamer" (P-153 — 공식 "이 디지몬/테이머를 액티브로 한다") · "Unsuspend this Digimon and 1 of your Tamers" (P-048) ·
    /// "Unsuspend 1 of your Digimon and 1 of your [Kiyoshiro Higashimitarai]" (LM-004 — 이름이 테이머면 테이머)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? UnsuspendTamers38(string s, string label)
    {
        Match m;
        const string Col = @"(?:red|blue|yellow|green|black|purple|white)";
        if ((m = Regex.Match(s, $@"^Unsuspend (?:(?<dig>this Digimon|1 of your Digimon) (?<conj>or|and) )?(?:(?:1|one) of your (?:(?<col>{Col}) )?Tamers|(?<bare>Tamer)|(?:1|one) of your (?<nm>\[[^\]]+\]))$", I)).Success)
        {
            var col = m.Groups["col"].Success ? Enum.Parse<CardColor>(m.Groups["col"].Value, ignoreCase: true) : (CardColor?)null;
            var names = m.Groups["nm"].Success ? TargetSpec.Names(m.Groups["nm"].Value) : null;
            bool orForm = m.Groups["conj"].Value.Equals("or", StringComparison.OrdinalIgnoreCase);
            if (m.Groups["bare"].Success && !orForm) return null;
            string dig = m.Groups["dig"].Value;
            var digSpec = dig.Length > 0 ? TargetSpec.Parse(dig) : null;
            if (digSpec != null) digSpec.PreferOwnSide = true;
            bool TamerOk(CardData t) => (col == null || t.AllColors.Contains(col.Value)) && (names == null || names.Any(n => CardNames.Is(t, n)));
            return (c =>
            {
                var tamers = c.Owner.SuspendedTamers.Where(t => c.Owner.Tamers.Contains(t) && TamerOk(t) && !c.Engine.TamerUnsuspendLocked(c.Owner, t)).ToList();
                var digis = digSpec == null ? new List<CardInstance>()
                    : digSpec.IsThis ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) && c.Self.IsSuspended ? new List<CardInstance> { c.Self } : new List<CardInstance>())
                    : c.Owner.BattleArea.Where(d => d.IsSuspended && digSpec.Filters.All(f => f(d))).ToList();
                // 이름이 테이머가 아니면 (LM-004 「이청솔」이 디지몬일 때) 그 이름의 디지몬
                var namedDigis = names != null ? c.Owner.BattleArea.Where(d => d.IsSuspended && names.Any(n => CardNames.Is(d.TopCard, n))).ToList() : new List<CardInstance>();
                var done = new List<string>();
                if (orForm)
                {
                    // 둘 중 하나: 추천 = 이 디지몬 (어택할 수 있다), 없으면 테이머
                    var opts = new List<string>();
                    if (digis.Count > 0) opts.Add($"[{digis[0].TopCard.Name}]");
                    if (tamers.Count > 0) opts.Add($"테이머 [{tamers[0].Name}]");
                    if (opts.Count == 0) { c.LastStepResolved = false; Emit(c, label, "액티브로 할 대상이 없습니다"); return; }
                    int pick = opts.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 액티브로 할 것을 고르세요.", opts, 0);
                    if (digis.Count > 0 && pick == 0) { c.Engine.Unsuspend(digis[0]); done.Add(digis[0].TopCard.Name); }
                    else { var t = PickTamer38(c, tamers); c.Engine.UnsuspendTamer38(c.Owner, t); done.Add(t.Name); }
                }
                else
                {
                    if (digSpec != null)
                    {
                        var d = digSpec.IsThis ? digis.FirstOrDefault() : Pick(digSpec, c, false).FirstOrDefault(x => x.IsSuspended);
                        if (d != null) { c.Engine.Unsuspend(d); done.Add(d.TopCard.Name); }
                    }
                    if (tamers.Count > 0) { var t = PickTamer38(c, tamers); c.Engine.UnsuspendTamer38(c.Owner, t); done.Add(t.Name); }
                    else if (namedDigis.Count > 0) { c.Engine.Unsuspend(namedDigis[0]); done.Add(namedDigis[0].TopCard.Name); }
                }
                if (done.Count == 0) c.LastStepResolved = false;
                Emit(c, label, done.Count > 0 ? $"[{string.Join(", ", done)}] 액티브" : "액티브로 할 대상이 없습니다");
            }, null);
        }
        return null;
    }

    private static CardData PickTamer38(EffectContext c, List<CardData> tamers)
    {
        if (tamers.Count == 1) return tamers[0];
        int i = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 액티브로 할 테이머를 고르세요.", tamers.Select(t => t.Name).ToList(), 0);
        return tamers[Math.Clamp(i, 0, tamers.Count - 1)];
    }

    /// <summary>
    /// "Delete that Digimon and 1 of your opponent's Digimon with DP less than or equal to the chosen Digimon" (BT12-108 — 공식 "그 디지몬의 DP 이하의 상대의 디지몬 1마리와 선택한 자신의 디지몬을 소멸시킨다") ·
    /// "Delete 1 of your chosen Digimon and 1 of your opponent's Digimon with a level equal to or lower than that Digimon" (BT17-067) —
    /// 상대의 디지몬을 먼저 고르고(선택한 디지몬이 필드에 있을 때의 값), 둘을 소멸시킨다
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? DeleteChosenAnd38(string s, string label, bool security)
    {
        var m = Regex.Match(s, @"^Delete (?:that Digimon|(?:1|one) of your chosen Digimon|the chosen Digimon) and (?<t>(?:1|one) of your opponent's .+)$", I);
        if (!m.Success || TargetSpec.Parse(m.Groups["t"].Value) is not { Opponent: true } target) return null;
        return (c =>
        {
            var own = c.Chosen34 ?? c.LastTargets.FirstOrDefault();
            if (own != null && !c.Owner.BattleArea.Contains(own)) own = null;
            var victims = DeletionCap.With(c.Engine.DeletionCapBonus(c.Owner, c.Self), () => Pick(target, c, security));
            var names = new List<string>();
            if (own != null && c.Engine.DeleteFrom(c.Owner, own)) names.Add(own.TopCard.Name);
            foreach (var v in victims)
                if (c.Enemy.BattleArea.Contains(v) && c.DeleteEnemy(v)) names.Add(v.TopCard.Name);
            if (names.Count == 0) c.LastStepResolved = false; else Diag38.Hit(ref Diag38.ChosenDelete);
            Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸" : "소멸시킬 대상이 없습니다", attackerRemoved: c.Other != null && victims.Contains(c.Other) && names.Count > 0);
        }, null);
    }

    /// <summary>"Choose 1 of your opponent's Digimon and return all of your opponent's Digimon with the same level as it to the bottom of the deck" (BT17-078) — 고른 뒤 그 디지몬을 기준으로</summary>
    private static (Action<EffectContext>? Act, string? Error)? ChooseAnd38(string s, CardData card, List<EffectTrigger> triggers, string label, bool security)
    {
        var m = Regex.Match(s, @"^(?<a>Choose (?:1|one) of (?:your|your opponent's) [^,]+?) and (?<b>(?:return|delete|suspend|place)\b.+)$", I);
        if (!m.Success || ChooseAction34(m.Groups["a"].Value, label, security) is not { Error: null, Act: { } choose }) return null;
        string r = char.ToUpperInvariant(m.Groups["b"].Value[0]) + m.Groups["b"].Value[1..];
        var b = ParseAction(r, card, triggers);
        if (b.Error != null || b.Act == null) return null;
        var act = b.Act;
        return (c => { c.Chosen34 = null; choose(c); if (c.Chosen34 == null) return; act(c); }, null);
    }
}
