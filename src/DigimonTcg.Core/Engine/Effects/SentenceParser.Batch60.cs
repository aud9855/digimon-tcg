using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.60.0 미지원 문장 34차 묶음: 원래 규칙(1~33차)이 읽지 못한 절만 34차 규칙으로 다시 읽는다 (34차 표현 Hint60이 있거나 바꿔 쓸 것이 있을 때만).
///  - 절 하나로 읽는 지속 효과 (ClauseSpecial60):
///    BT19-101 "This Digimon with no digivolution cards can't be suspended, and isn't affected by your opponent's effects" (진화원이 없는 동안) ·
///    EX6-010 (진화원) "While this Digimon is [RagnaLoardmon], the [Security] effects on cards checked by this Digimon don't activate" ·
///    EX6-044 (진화원) "While this Digimon is [RagnaLoardmon], this Digimon can't leave the battle area other than by your effects or by deletion" ·
///    BT12-112 "All of your opponent's [Security] effects on Option cards don't activate"
///  - 동작: EX3-073 "none of your opponent's [Security] effects can activate for the turn" · BT3-097 "1 of your Digimon gains "[Security] effects on Option cards checked by this Digimon don't activate." for the turn"
/// 엔진은 GoldfishEngine.Batch60 (SecurityEffectBlocked60 · LeaveLocked60), 렌더러도 같은 표 (KoreanEffectRenderer.Batch60.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth60;
    [ThreadStatic] private static int _gate60;
    internal static bool Gate60 => _gate60 > 0;

    private static string? TryParse60(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse59(rawClause, inherited, card, def);
        if (err == null || _clauseDepth60 > 0) return err;
        string t = Normalize58(rawClause);
        string alt = ClauseRewrite60(t);
        bool special = ClauseSpecialRe60.IsMatch(t);
        if (!special && alt == t && !Hint60.IsMatch(t)) return err;
        _clauseDepth60++;
        _gate60++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (special) return ClauseSpecial60(t, inherited, card, def) ? null : err;
            if (TryParse59(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse59(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth60--;
            _gate60--;
            if (def != null) WrapGate60(def, before);
        }
    }

    /// <summary>34차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate60(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate60++; try { inner(c); } finally { _gate60--; } } };
        }
    }

    /// <summary>34차의 동작이 쓰이는 절 (바꿔 쓰지 않아도 문을 열고 다시 읽는다)</summary>
    internal static readonly Regex Hint60 = new(@"none of your opponent's \[Security\] effects can activate for the turn|this Digimon can't leave the battle area other than by deletion|⟦[a-z]+60⟧",RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules60 =
    {
        // BT3-097 "1 of your Digimon gains "[Security] effects on Option cards checked by this Digimon don't activate." for the turn." — 준 효과는 ≪체크한 옵션 카드의 【시큐리티】 효과는 발휘하지 않는다≫ 키워드 (BT1-025와 같은 것)
        s => Regex.Replace(s, "\\bgains \"\\[Security\\] effects on Option cards checked by this Digimon don't activate\\.?\" for the turn\\.", "gains ⟦nso60⟧ for the turn.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite60(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules60) s = f(s);
        return s;
    }

    // =====================================================================
    // 절 하나로 읽는 지속 효과
    // =====================================================================

    internal static readonly Regex SourcelessStaticRe60 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*This Digimon with no digivolution cards (?<p>.+?)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex NegateSecurityRe60 = new(@"^(?:Inherited Effect\s*)?(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*(?:While this Digimon is (?<x>\[[^\]]+\]), )?the \[Security\] effects on cards checked by this Digimon don't activate\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex LeaveLockRe60 = new(@"^(?:Inherited Effect\s*)?(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*(?:While this Digimon is (?<x>\[[^\]]+\]), )?this Digimon can't leave the battle area other than by your effects or by deletion\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex EnemyOptionSecurityRe60 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*All of your opponent's \[Security\] effects on Option cards don't activate\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex LevelVsSecuritySaRe60 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*All of your opponent's Digimon with levels greater than or equal to the total cards in both players' security stacks gain ＜Security A\. (?<sign>[+-])(?<n>\d+)＞(?:\s*\([^)]*\))?\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex CollisionByDpRe60 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*All Digimon with as much or less DP (?:as|than) this Digimon gain ＜Collision＞(?:\s*\([^)]*\))?\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClauseSpecialRe60 = new($"{SourcelessStaticRe60}|{NegateSecurityRe60}|{LeaveLockRe60}|{EnemyOptionSecurityRe60}|{LevelVsSecuritySaRe60}|{CollisionByDpRe60}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"This Digimon with no digivolution cards ~"의 술어 → 키워드 (모두 읽어야 한다)</summary>
    internal static List<Keyword>? SourcelessPreds60(string preds)
    {
        var list = new List<Keyword>();
        foreach (var raw in Regex.Split(preds.Trim(), @",?\s+and\s+|,\s+(?=can't|isn't|is not)", RegexOptions.IgnoreCase))
        {
            string p = raw.Trim().TrimEnd(',', '.');
            if (Regex.IsMatch(p, @"^can't (?:be suspended|suspend)$", I)) { list.Add(Keyword.CannotSuspend); continue; }   // EX8-026과 같은 "레스트할 수 없다"
            var hit = Restrictions34.FirstOrDefault(r => r.Pattern.IsMatch(p));
            if (hit.Pattern == null || !hit.Good) return null;
            list.AddRange(hit.Keywords);
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 true (def가 있으면 등록)</summary>
    private static bool ClauseSpecial60(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (card.Type != CardType.Digimon) return false;
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        Match m;
        // BT19-101 "[All Turns] This Digimon with no digivolution cards can't be suspended, and isn't affected by your opponent's effects." — 진화원이 없는 동안의 지속 효과
        if ((m = SourcelessStaticRe60.Match(t)).Success)
        {
            if (inherited || SourcelessPreds60(m.Groups["p"].Value) is not { } kws) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            if (def != null)
                foreach (var kw in kws)
                    def.Static(StaticStat.Keyword, source, c => q(c) && c.Digimon != null && c.Digimon.EvolutionSources.Count == 0 ? 1 : 0, kw);
            return true;
        }
        // EX6-010 (진화원) "[Your Turn] While this Digimon is [RagnaLoardmon], the [Security] effects on cards checked by this Digimon don't activate." — 카드 종류 무관
        if ((m = NegateSecurityRe60.Match(t)).Success)
        {
            var cond = NamedQualifier60(m);
            def?.Static(StaticStat.Keyword, source, c => cond(c) ? 1 : 0, Keyword.NegateAllSecurity60);
            return true;
        }
        // EX6-044 (진화원) "[Opponent's Turn] While this Digimon is [RagnaLoardmon], this Digimon can't leave the battle area other than by your effects or by deletion."
        if ((m = LeaveLockRe60.Match(t)).Success)
        {
            var cond = NamedQualifier60(m);
            def?.Static(StaticStat.Keyword, source, c => cond(c) ? 1 : 0, Keyword.CannotLeaveExceptOwnOrDeletion60);
            return true;
        }
        // BT12-112 "[Your Turn] All of your opponent's [Security] effects on Option cards don't activate." — 이 디지몬의 주인이 하는 체크 전부
        if ((m = EnemyOptionSecurityRe60.Match(t)).Success)
        {
            if (inherited) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            def?.Static(StaticStat.Keyword, source, c => q(c) ? 1 : 0, Keyword.NegateEnemyOptionSecurity60);
            return true;
        }
        // EX5-033 "[Opponent's Turn] All of your opponent's Digimon with levels greater than or equal to the total cards in both players' security stacks gain ＜Security A. -2＞"
        //  — 상대 카드의 지속 ≪시큐리티 어택 -N≫ (Lv.과 서로의 시큐리티 합계를 그때마다 비교)
        if ((m = LevelVsSecuritySaRe60.Match(t)).Success)
        {
            if (inherited) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            int n = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            if (def != null)
            {
                int from = def.StaticCount;
                def.Static(StaticStat.EnemySecurityAttack, source, c => q(c) ? n : 0);
                def.PatchStatics(from, e => e with { SubjectOf = (c, d) => d.TopCard.Level > 0 && d.TopCard.Level >= c.Owner.SecurityStack.Count + c.Enemy.SecurityStack.Count });
            }
            return true;
        }
        // BT18-083 "[All Turns] All Digimon with as much or less DP as this Digimon gain ＜Collision＞" — 서로의 디지몬 (이 디지몬 자신도)
        if ((m = CollisionByDpRe60.Match(t)).Success)
        {
            if (inherited) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            if (def != null)
            {
                int from = def.StaticCount;
                def.Static(StaticStat.AllyKeyword, source, c => q(c) ? 1 : 0, Keyword.Collision);
                def.Static(StaticStat.EnemyKeyword, source, c => q(c) ? 1 : 0, Keyword.Collision);
                def.PatchStatics(from, e => e with { SubjectOf = (c, d) => c.Digimon != null && d.GetCurrentDP() <= c.Digimon.GetCurrentDP() });
            }
            return true;
        }
        return false;
    }

    /// <summary>[턴] 조건 + "While this Digimon is [X]" (이 디지몬의 이름이 X인 동안)</summary>
    private static Func<StaticContext, bool> NamedQualifier60(Match m)
    {
        var q = Qualifier58(m.Groups["q"].Value);
        if (!m.Groups["x"].Success) return q;
        var names = TargetSpec.Names(m.Groups["x"].Value);
        return c => q(c) && c.Digimon != null && names.Any(x => CardNames.Is(c.Digimon.TopCard, x));
    }

    // =====================================================================
    // 동작 (문이 열려 있을 때만)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction60(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate60) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        bool security = triggers.Contains(EffectTrigger.Security);
        s = Regex.Replace(s.Trim(), @"^Then,?\s+", "", I);
        Match m;

        // EX3-073 "none of your opponent's [Security] effects can activate for the turn" — 이 턴 동안 자신이 하는 체크에서 상대의 【시큐리티】 효과는 발휘하지 않는다
        if (Regex.IsMatch(s, @"^none of your opponent's \[Security\] effects can activate for the turn$", I))
            return (c =>
            {
                c.Owner.NoEnemySecurityUntil60 = Math.Max(c.Owner.NoEnemySecurityUntil60, c.TurnSequence);
                Diag46.Hit("noEnemySecurity60");
                Emit(c, label, "이 턴 동안 상대의 【시큐리티】 효과는 발휘할 수 없습니다");
            }, null);

        // BT16-051 "until the end of your opponent's turn, this Digimon can't leave the battle area other than by deletion" — 자신의 효과로도 떠나지 않는다 (소멸만)
        if (Regex.IsMatch(s, @"^(?:until the end of your opponent's turn, )?this Digimon can't leave the battle area other than by deletion(?: until the end of your opponent's turn)?$", I)
            && Regex.IsMatch(s, @"until the end of your opponent's turn", I) && card.Type == CardType.Digimon)
            return (c =>
            {
                var d = c.Self;
                if (d == null || !c.Owner.BattleArea.Contains(d)) { c.LastStepResolved = false; return; }
                c.Engine.AddTimedKeyword60(d, Keyword.CannotLeaveExceptDeletion60, Until(c, Duration.OpponentsTurnEnd));
                c.RecalculateFieldBuffs();
                Diag46.Hit("leaveLockTimed60");
                Emit(c, label, $"[{d.TopCard.Name}]은(는) 상대의 턴 종료 시까지 소멸 이외로는 배틀 에어리어를 떠나지 않습니다");
            }, null);

        // BT3-097 "1 of your Digimon gains ⟦nso60⟧ for the turn" (바꿔 쓴 것) — 체크한 옵션 카드의 【시큐리티】 효과는 발휘하지 않는다 (공식 Q&A BT3-097 Q1: 옵션 카드만)
        if ((m = Regex.Match(s, @"^(?<t>1 of your Digimon) gains ⟦nso60⟧ for the turn$", I)).Success
            && TargetSpec.Parse(m.Groups["t"].Value) is { Opponent: false, AnySide: false } spec)
        {
            spec.PreferOwnSide = true;
            return (c =>
            {
                var list = Pick(spec, c, security).Where(d => c.Owner.BattleArea.Contains(d)).ToList();
                if (list.Count == 0) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
                foreach (var d in list) c.Engine.AddTimedKeyword60(d, Keyword.NegateSecurityOptions, c.TurnSequence);
                c.RecalculateFieldBuffs();
                c.LastTargets = list;
                Diag46.Hit("grantNegateOptionSecurity60");
                Emit(c, label, $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}]이(가) 체크한 옵션 카드의 【시큐리티】 효과는 이 턴 동안 발휘하지 않습니다");
            }, null);
        }
        return null;
    }
}
