using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.46.0 미지원 문장 20차 묶음: 원래 규칙(1~19차)이 읽지 못한 절 · 문장 · 동작 · 비용 · 조건 · 셈만 20차 규칙으로 다시 읽는다
/// (엔진: GoldfishEngine.Batch46.cs). 렌더러도 같은 순서 (KoreanEffectRenderer.Batch46.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth46;
    [ThreadStatic] private static int _fallbackDepth46;

    /// <summary>
    /// 20차 규칙을 켜는 문 — 원래 규칙(19차까지)이 읽지 못한 절 · 문장을 다시 읽을 때만 연다
    /// (켜 두면 원래 규칙이 다른 길로 읽던 문장의 처리가 바뀔 수 있다 — 수정 전 지원 문장은 그대로 둔다)
    /// </summary>
    [ThreadStatic] private static int _gate46;
    internal static bool Gate46 => _gate46 > 0;

    // =====================================================================
    // 1. 절 — 원래 규칙이 읽지 못한 절만 (TryParse)
    // =====================================================================

    private static string? TryParse46(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse45(rawClause, inherited, card, def);
        if (err == null || _clauseDepth46 > 0) return err;
        _clauseDepth46++;
        _gate46++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (TryParse45(rawClause, inherited, card, null) == null)
            {
                if (def != null) TryParse45(rawClause, inherited, card, def);
                return null;
            }
            _gate45++; _gate44++;   // 20차 절 규칙은 19 · 18차 대상 · 셈 · 조건도 쓴다
            try { if (ExtraClause46(rawClause, inherited, card, def)) return null; }
            finally { _gate45--; _gate44--; }
            foreach (var alt in ClauseRewrites46(rawClause))
            {
                if (alt == rawClause) continue;
                if (TryParse45(alt, inherited, card, null) != null) continue;
                if (def != null) TryParse45(alt, inherited, card, def);
                return null;
            }
            return err;
        }
        finally
        {
            _clauseDepth46--;
            _gate46--;
            if (def != null) WrapGate46(def, before);
        }
    }

    /// <summary>20차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다 (처리 중에 다시 읽는 문장)</summary>
    private static void WrapGate46(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate46++; _gate45++; _gate44++; try { inner(c); } finally { _gate46--; _gate45--; _gate44--; } } };
        }
    }

    /// <summary>고르는 효과 · ≪딜레이≫: 원래 규칙이 읽지 못했을 때만 20차 규칙(문을 연 채로)으로 다시 읽는다</summary>
    private static string? TryParseChoice46(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoice45(rawHeader, rawOptions, inherited, card, def);
        if (err == null || _gate46 > 0) return err;
        _gate46++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            string header = HeaderRewrite46(rawHeader);
            var opts = rawOptions.Select(OptionRewrite46).ToList();
            if (TryParseChoice45(header, opts, inherited, card, null) != null) return err;
            if (def != null) TryParseChoice45(header, opts, inherited, card, def);
            return null;
        }
        finally
        {
            _gate46--;
            if (def != null) WrapGate46(def, before);
        }
    }

    /// <summary>절 전체의 같은 뜻 다른 표기 (렌더러도 쓴다)</summary>
    internal static IEnumerable<string> ClauseRewrites46(string rawClause)
    {
        string c = rawClause.Trim();
        string whole = c;
        foreach (var f in ClauseRules46) whole = f(whole);
        if (whole != c) yield return whole;
        var lead = Regex.Match(whole, @"^(?<labels>(?:Inherited Effect\s*|Security Effect\s*)?(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
        string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value.Trim();
        var sentences = Regex.Split(body, @"(?<=\.)\s+(?=[A-Z＜・•])");
        bool changed = false;
        var outs = new List<string>();
        foreach (var s in sentences)
        {
            string t = s.Trim();
            string end = t.EndsWith(".") ? "." : "";
            string core = t.TrimEnd('.');
            string? alt = Rewrites46(core).LastOrDefault();
            if (alt != null && alt != core) { changed = true; outs.Add(alt + end); }
            else outs.Add(t);
        }
        if (changed) yield return labels + string.Join(" ", outs);
    }

    // =====================================================================
    // 2. 문장 — 원래 규칙이 읽지 못한 문장만 (ParseStep 끝)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback46(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth46 >= 3 || !Gate46) return null;
        _fallbackDepth46++;
        try
        {
            if (StepExtra46(sentence, card, triggers) is { } ex) return (ex, null);
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites46(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            return null;
        }
        finally { _fallbackDepth46--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다). 여러 규칙을 차례로 겹쳐 적용한 결과도 낸다</summary>
    internal static IEnumerable<string> Rewrites46(string s)
    {
        string acc = s;
        foreach (var f in RewriteRules46)
        {
            string next = f(acc);
            if (next != acc)
            {
                yield return next;
                acc = next;
            }
        }
    }

    // =====================================================================
    // 3. 동작 — 원래 규칙이 읽지 못한 동작만 (ParseAction 끝)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction46(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth46 >= 3 || !Gate46) return null;
        _fallbackDepth46++;
        try
        {
            string s = raw.Trim().TrimEnd('.');
            if (ActionExtra46(s, card, triggers) is { } ex) return ex;
            foreach (var alt in Rewrites46(s))
            {
                if (alt == s) continue;
                var r = ParseAction(alt, card, triggers);
                if (r.Error == null && r.Act != null) return r;
            }
            return null;
        }
        finally { _fallbackDepth46--; }
    }
}
