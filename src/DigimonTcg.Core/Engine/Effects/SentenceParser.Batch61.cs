using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.61.0 미지원 문장 35차 묶음: 원래 규칙(1~34차)이 읽지 못한 절만 35차 규칙으로 다시 읽는다.
///  - 절 하나로 읽는 지속 효과 (ClauseSpecial61):
///    BT9-109 (진화원) "Effects can't trash [X Antibody] in this Digimon's digivolution cards" (진화원 파기는 GoldfishEngine.Batch61 한 곳으로) ·
///    BT10-084 "When an effect would trash one of your other Digimon's digivolution cards, you may trash this Digimon's digivolution cards with that effect instead" (치환 — 공식 Q&amp;A BT10-084 Q1) ·
///    BT10-042 "Your opponent's Digimon with ＜Security A.＞ can't attack this Digimon and can't activate [When Attacking] and [When Digivolving] effects" (공식 Q&amp;A BT10-042 Q1: + · - 모두) ·
///    효과 문장을 주는 지속 효과 "All level 3 Digimon gain "[When Attacking] Lose 1 memory."" (BT5-091 — 공식 Q&amp;A BT5-091 Q1: 자신의 디지몬에게도) · BT8-031 · BT10-056
/// 엔진은 GoldfishEngine.Batch61, 렌더러도 같은 표 (KoreanEffectRenderer.Batch61.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth61;

    private static string? TryParse61(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse60(rawClause, inherited, card, def);
        if (err == null || _clauseDepth61 > 0) return err;
        string t = Normalize58(rawClause);
        if (!ClauseSpecialRe61.IsMatch(t)) return err;
        _clauseDepth61++;
        try { return ClauseSpecial61(t, inherited, card, def) ? null : err; }
        finally { _clauseDepth61--; }
    }

    // =====================================================================
    // 절 하나로 읽는 지속 효과
    // =====================================================================

    internal static readonly Regex SourceTrashProtectRe61 = new(@"^(?:Inherited Effect\s*)?(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*Effects can't trash (?<x>\[[^\]]+\]) in this Digimon's digivolution cards\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex ReplaceSourceTrashRe61 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*When an effect would trash one of your other Digimon's digivolution cards, you may trash this Digimon's digivolution cards with that effect instead\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex SaHolderLockRe61 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*Your opponent's Digimon with ＜Security A\.＞ can't attack this Digimon(?<act> and can't activate \[When Attacking\] and \[When Digivolving\] effects)?\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex GrantAuraRe61 = new("^(?<q>\\[(?:Your Turn|All Turns|Opponent's Turn)\\])\\s*All (?<t>(?:of your (?:opponent's |other )?)?[^\"]*?Digimon(?: with [^\"]+?)?) gains? \"(?<g>[^\"]+)\"\\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex SelfGrantByDpRe61 = new(@"^(?:Inherited Effect\s*)?(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*This Digimon gets \+(?<n>\d+) DP\. While this Digimon has (?<dp>\d+) DP or more, it gains ""(?<g>[^""]+)""\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClauseSpecialRe61 = new($"{SourceTrashProtectRe61}|{ReplaceSourceTrashRe61}|{SaHolderLockRe61}|{GrantAuraRe61}|{SelfGrantByDpRe61}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>효과 문장을 주는 지속 효과를 가진 카드 (엔진의 빠른 확인 — 맨 위 카드 · 진화원)</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> GrantAuraCards61 = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"All level 3 Digimon" · "All of your opponent's Digimon" · "All of your other Digimon with ~" → (받는 쪽, 조건, 이 디지몬 제외). 읽지 못하면 null</summary>
    internal static (bool Ally, bool Enemy, bool Other, TargetSpec Spec)? GrantAuraTarget61(string t)
    {
        t = t.Trim();
        bool other = Regex.IsMatch(t, @"^of your other\b", I);
        bool enemy = Regex.IsMatch(t, @"^of your opponent's\b", I);
        bool both = !t.StartsWith("of ", StringComparison.OrdinalIgnoreCase);
        string who = both ? "all of your " + t : "all " + Regex.Replace(t, @"^of your other ", "of your ", I);
        // "Digimon with [A], [B] or [C] in one of their traits" — 특징에 포함 (TargetSpec.Batch44와 같은 판단)
        var tr = Regex.Match(who, @"^(?<pre>.+?Digimon) with (?<x>\[[^\]]+\](?:(?:,\s*|,?\s+or\s+)\[[^\]]+\])*) in (?:any|one) of (?:its|their) traits$", I);
        if (tr.Success && TargetSpec.Parse(tr.Groups["pre"].Value) is { IsThis: false, IsThat: false, Selector: null } pre && pre.ContextFilters.Count == 0)
        {
            var xs = TargetSpec.Names(tr.Groups["x"].Value);
            pre.Filters.Add(d => d.TopCard.Traits.Concat(d.ExtraTraits58).Any(x => xs.Any(n => x.Contains(n, StringComparison.OrdinalIgnoreCase))));
            return (both || !enemy, both || enemy, other, pre);
        }
        if (TargetSpec.Parse(who) is not { } spec || spec.IsThis || spec.IsThat || spec.ContextFilters.Count > 0 || spec.Selector != null) return null;
        return (both || !enemy, both || enemy, other, spec);
    }

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 true (def가 있으면 등록)</summary>
    private static bool ClauseSpecial61(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        Match m;
        // BT9-109 (진화원 — 옵션 카드) "[All Turns] Effects can't trash [X Antibody] in this Digimon's digivolution cards." — 효과가 이 디지몬의 진화원의 「X항체」를 파기할 수 없다
        if ((m = SourceTrashProtectRe61.Match(t)).Success)
        {
            var names = TargetSpec.Names(m.Groups["x"].Value);
            if (names.Count != 1 || !names[0].Equals("X Antibody", StringComparison.OrdinalIgnoreCase)) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            def?.Static(StaticStat.Keyword, source, c => q(c) ? 1 : 0, Keyword.XAntibodySourcesProtected61);
            return true;
        }
        // BT12-088 (진화원 — 테이머 카드의 진화원 효과) "[Your Turn] This Digimon gets +2000 DP. While this Digimon has 10000 DP or more, it gains "[Your Turn] [Once Per Turn] When this Digimon checks your opponent's security stack, gain 2 memory.""
        //  — DP +N과 DP 조건으로 이 디지몬이 얻는 효과 문장 (지속 효과)
        if ((m = SelfGrantByDpRe61.Match(t)).Success)
        {
            if (!inherited && card.Type != CardType.Digimon) return false;
            if (GrantedCard32(card, m.Groups["g"].Value) is not { } granted) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            int n = int.Parse(m.Groups["n"].Value), dpMin = int.Parse(m.Groups["dp"].Value);
            if (def == null) return true;
            def.Static(StaticStat.Dp, source, c => q(c) ? n : 0);
            int from = def.StaticCount;
            def.Static(StaticStat.AllyGrant61, source, c => q(c) ? 1 : 0);
            def.PatchStatics(from, e => e with { Grant61 = granted, SubjectOf = (c, d) => ReferenceEquals(d, c.Digimon) && d.GetCurrentDP() >= dpMin });
            GrantAuraCards61[card.CardId] = true;
            return true;
        }
        if (card.Type != CardType.Digimon) return GrantAura61(t, inherited, card, def);
        // BT10-084 "[Opponent's Turn] When an effect would trash one of your other Digimon's digivolution cards, you may trash this Digimon's digivolution cards with that effect instead."
        if ((m = ReplaceSourceTrashRe61.Match(t)).Success)
        {
            if (inherited) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            def?.Static(StaticStat.Keyword, source, c => q(c) ? 1 : 0, Keyword.ReplaceOtherSourceTrash61);
            return true;
        }
        // BT10-042 "[Opponent's Turn] Your opponent's Digimon with ＜Security A.＞ can't attack this Digimon and can't activate [When Attacking] and [When Digivolving] effects."
        //  — ≪시큐리티 어택 +N≫ · ≪시큐리티 어택 -N≫ 어느 쪽이든 (공식 Q&A BT10-042 Q1)
        if ((m = SaHolderLockRe61.Match(t)).Success)
        {
            if (inherited) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            if (def != null)
            {
                def.Static(StaticStat.Keyword, source, c => q(c) ? 1 : 0, Keyword.CannotBeAttackedBySa61);
                if (m.Groups["act"].Success)
                {
                    int from = def.StaticCount;
                    def.Static(StaticStat.EnemyKeyword, source, c => q(c) ? 1 : 0, Keyword.CannotActivateWhenAttacking);
                    def.Static(StaticStat.EnemyKeyword, source, c => q(c) ? 1 : 0, Keyword.CannotActivateWhenDigivolving);
                    def.PatchStatics(from, e => e with { Subject = GoldfishEngine.HasSecurityAttackKeyword61 });
                }
            }
            return true;
        }
        return GrantAura61(t, inherited, card, def);
    }

    /// <summary>효과 문장을 주는 지속 효과 (디지몬 · 테이머) — "All level 3 Digimon gain "[When Attacking] Lose 1 memory."" (BT5-091)</summary>
    private static bool GrantAura61(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        Match m;
        if (!(m = GrantAuraRe61.Match(t)).Success || inherited || card.Type is not (CardType.Digimon or CardType.Tamer)) return false;
        if (GrantAuraTarget61(m.Groups["t"].Value) is not { } target) return false;
        if (GrantedCard32(card, m.Groups["g"].Value) is not { } granted) return false;
        var q = Qualifier58(m.Groups["q"].Value);
        if (def == null) return true;
        var spec = target.Spec;
        Func<CardInstance, bool> subject = d => d.TamerProxyOf == null && spec.Filters.All(f => f(d));
        int from = def.StaticCount;
        if (target.Ally) def.Static(StaticStat.AllyGrant61, EffectSource.Main, c => q(c) ? 1 : 0, subject: subject, excludeSource: target.Other);
        if (target.Enemy) def.Static(StaticStat.EnemyGrant61, EffectSource.Main, c => q(c) ? 1 : 0, subject: subject);
        def.PatchStatics(from, e => e with { Grant61 = granted });
        GrantAuraCards61[card.CardId] = true;
        return true;
    }
}
