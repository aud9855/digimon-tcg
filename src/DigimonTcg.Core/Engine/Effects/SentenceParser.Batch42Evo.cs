using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.42.0 조건이 붙은 진화 조건 무시 진화 (지속 효과):
///  - 진화할 카드 쪽: "While you have 10 or more cards in your trash, your [Impmon] can digivolve into this card in your hand for a memory cost of 4, ignoring this card's digivolution requirements" (BT2-111 ·
///    EX2-022 · BT14-101 · BT15-101 · RB1-009 — 공식 BT2-111 "자신의 트래시가 10장 이상일 동안, 자신의 「임프몬」은 패의 이 카드에 진화 조건을 무시하고 진화 코스트 4로 진화할 수 있다")
///  - 진화하는 디지몬 쪽: "[Your Turn] If your opponent has a level 6 or higher Digimon in play, this Digimon can digivolve into a [Gallantmon] in your hand for a memory cost of 4, ignoring its digivolution requirements"
///    (ST7-03 · ST8-04 · BT6-060 · BT23-026 · BT21-010 · BT21-040 · ST20-10 · ST21-10 · BT25-082 — 공식 ST7-03 "【자신의 턴】 Lv.6 이상인 상대의 디지몬이 있을 때, 이 디지몬은 진화조건을 무시하고 진화 코스트 4를 지불하여 패의 「듀크몬」으로 진화할 수 있다")
/// 조건은 진화할 때마다 지금 필드로 판단한다 (엔진: CardInstance.LiveContext42 — GoldfishEngine.Batch42)
/// </summary>
internal static partial class SentenceParser
{
    internal static readonly Regex EvoSideRe42 = new(
        @"^(?:(?:While|If) (?<cond>.+?), )?(?:your|(?:1|one) of your) (?<who>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*)s?(?<src> that has a digivolution card with (?<srcname>\[[^\]]+\]) in its name)? (?:can|may) digivolve into this card in (?:your|the) hand for a (?:memory |digivolution )?cost of (?<n>\d+), ignoring (?:this card's |its )?digivolution requirements\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static readonly Regex HolderSideRe42 = new(
        @"^(?<labels>(?:\[(?:Your Turn|All Turns)\]\s*)?)(?:(?:While|If) (?<cond>.+?), )?this Digimon (?:can|may) digivolve into (?:an? |1 )?(?<pick>.+?)(?: (?:in|from) (?:your|the) hand)? for a (?:memory |digivolution )?cost of (?<n>\d+), ignoring (?:its |this card's )?digivolution requirements\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool SpecialEvoClause42(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (card.Type != CardType.Digimon) return false;
        string clause = Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", I);
        Match m;
        if (!inherited && (m = EvoSideRe42.Match(clause)).Success)
        {
            Func<StaticContext, bool>? cond = null;
            if (m.Groups["cond"].Success && (cond = StaticCond42(m.Groups["cond"].Value)) == null) return false;
            var names = TargetSpec.Names(m.Groups["who"].Value);
            string? src = m.Groups["srcname"].Success ? m.Groups["srcname"].Value.Trim('[', ']') : null;
            int cost = int.Parse(m.Groups["n"].Value);
            def?.Script41.SpecialEvolutions42.Add((cond, d => names.Any(n => CardNames.Is(d.TopCard, n)) && (src == null || d.SourcesAny(x => TargetSpec.NameHas(x, src))), cost));
            return true;
        }
        if ((m = HolderSideRe42.Match(clause)).Success)
        {
            Func<StaticContext, bool>? cond = null;
            if (m.Groups["cond"].Success && (cond = StaticCond42(m.Groups["cond"].Value)) == null) return false;
            if (EvoIntoPick42(m.Groups["pick"].Value) is not { } into) return false;
            bool yourTurn = m.Groups["labels"].Value.Contains("Your Turn", StringComparison.OrdinalIgnoreCase);
            int cost = int.Parse(m.Groups["n"].Value);
            def?.Script41.HolderEvolutions42.Add((inherited ? EffectSource.Inherited : EffectSource.Main, yourTurn, cond, into, cost));
            return true;
        }
        return false;
    }

    /// <summary>진화할 카드 조건: "[Gallantmon]" (명칭이 같은 카드) · "[Antylamon]" · "a Digimon card with the [Three Musketeers] trait" · "[Three Musketeers] trait Digimon card"</summary>
    internal static Func<CardData, bool>? EvoIntoPick42(string pick)
    {
        string p = Regex.Replace(pick.Trim(), @"^(?:an?|1) ", "", I);
        if (Regex.IsMatch(p, @"^\[[^\]]+\](?: or \[[^\]]+\])*$"))
        {
            var ns = TargetSpec.Names(p);
            return x => x.Type == CardType.Digimon && ns.Any(n => CardNames.Is(x, n));
        }
        if (ParseCardPick("1 " + p) is { } cp)
            return x => x.Type == CardType.Digimon && cp.Match(x);
        return null;
    }

    [ThreadStatic] private static int _staticCondDepth42;

    /// <summary>
    /// 지속 효과 조건 (원래 규칙 → "A or B" / "A, or B" (어느 한쪽) → 사건 조건 규칙). 사건 조건 규칙은 엔진이 있을 때만 판단한다
    /// </summary>
    internal static Func<StaticContext, bool>? StaticCond42(string cond)
    {
        cond = Regex.Replace(cond.Trim(), @"^there're\b", "there are", I);
        if (ParseStaticCondition(cond) is { } sc) return sc;
        if (_staticCondDepth42 > 1) return null;
        _staticCondDepth42++;
        try
        {
            // "A or B" · "A, or B" — 어느 한쪽 (공식 BT21-010 "~ 있거나, ~인 동안")
            foreach (Match or in Regex.Matches(cond, @",? or (?=(?:you|your|there|this)\b)", I))
            {
                string left = cond[..or.Index].Trim(), right = cond[(or.Index + or.Length)..].Trim();
                var a = StaticCond42(left);
                var b = a == null ? null : StaticCond42(right);
                if (a != null && b != null) return c => a(c) || b(c);
            }
            // "you have 2 or fewer security cards or 3 or more [Hero] trait Tamers ~" — 뒤쪽의 주어가 빠진 것
            foreach (Match or in Regex.Matches(cond, @" or (?=\d+ or (?:more|fewer) )", I))
            {
                string left = cond[..or.Index].Trim(), right = cond[(or.Index + or.Length)..].Trim();
                var subj = Regex.Match(left, @"^(?<s>you have|your opponent has) ", I);
                if (!subj.Success) continue;
                var a = StaticCond42(left);
                var b = a == null ? null : StaticCond42(subj.Groups["s"].Value + " " + right);
                if (a != null && b != null) return c => a(c) || b(c);
            }
            // "your Tamers have 3 or more total colors" (ST20-10 · ST21-10 — 공식 "자신의 테이머의 색 합계가 3색 이상")
            var tc = Regex.Match(cond, @"^your Tamers have (?<n>\d+) or more total colou?rs$", I);
            if (tc.Success)
            {
                int n = int.Parse(tc.Groups["n"].Value);
                return c => c.Owner.Tamers.SelectMany(t => t.AllColors).Distinct().Count() >= n;
            }
            // 사건 조건 규칙 ("you have a Tamer with [Three Musketeers] in its text" 등 — BT25-082)
            if (ParseEventCondition(cond) is { } ec)
                return c => c.Engine != null && ec(new EffectContext(c.Engine, c.Owner, c.Digimon, null, null));
            return null;
        }
        finally { _staticCondDepth42--; }
    }
}
