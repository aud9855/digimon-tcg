using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.42.0 소멸시킨 디지몬의 등장 코스트만큼 등장 코스트 감소 (공식 BT13-103 "명칭에 「기즈몬」을 포함하는 자신의 디지몬 1마리를 소멸시키는 것으로, 소멸시킨 디지몬의 등장 코스트의 수치만큼, 지불하는 등장 코스트를 마이너스한다"):
///  - 이 카드: "When this card would be played, by deleting 1 of your play cost 11 or lower Digimon with [Negamon] in its digivolution cards and [Negamon] in its text, reduce the cost by the deleted Digimon's play cost" (BT25-076)
///  - 다른 카드 (테이머): "[Your Turn] When you would play a card with [Belphemon] in its name, by deleting 1 of your Digimon with [Gizmon] in its name, reduce the play cost by the play cost of the deleted Digimon" (BT13-103)
/// </summary>
internal static partial class SentenceParser
{
    internal static readonly Regex DeletedCostRe42 = new(
        @"^(?<labels>(?:\[(?:Your Turn|All Turns|Once Per Turn)\]\s*)*)When (?:(?<self>this card would be played|you would play this card)|you would play (?<p>[^,]+?)|(?<p>[^,]+?) would be played)(?: from (?:your|the) hand)?, by deleting (?:1|one) of your (?<t>.+?), reduce (?:the|its) (?:play )?cost by (?:the deleted Digimon's play cost|the play cost of the deleted Digimon)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"play cost 11 or lower Digimon with [Negamon] in its digivolution cards and [Negamon] in its text" → 자신의 디지몬 조건 (렌더러도 쓴다)</summary>
    internal static Func<CardInstance, bool>? OwnDigimonFilter42(string t)
    {
        t = t.Trim();
        if (TargetSpec.Parse("all of your " + t) is { Opponent: false, AnySide: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } spec)
        {
            var fs = spec.Filters.ToList();
            return d => fs.All(f => f(d));
        }
        // "~ Digimon with A and B" — 두 조건
        var two = Regex.Match(t, @"^(?<pre>.+?Digimon) with (?<a>.+?) and (?<b>\[[^\]]+\] in its (?:text|name|digivolution cards)|the \[[^\]]+\] trait)$", I);
        if (two.Success && OwnDigimonFilter42($"{two.Groups["pre"].Value} with {two.Groups["a"].Value}") is { } fa && OwnDigimonFilter42($"{two.Groups["pre"].Value} with {two.Groups["b"].Value}") is { } fb)
            return d => fa(d) && fb(d);
        // "~ Digimon with [Negamon] in its text" — 맨 위 카드의 효과 문장에 이름이 적혀 있다
        var txt = Regex.Match(t, @"^(?<pre>.+?Digimon) with (?<x>\[[^\]]+\]) in its text$", I);
        if (txt.Success && OwnDigimonFilter42(txt.Groups["pre"].Value) is { } fp)
        {
            string x = txt.Groups["x"].Value;
            return d => fp(d) && (d.TopCard.EffectText + "\n" + d.TopCard.InheritedEffectText).Contains(x, StringComparison.OrdinalIgnoreCase);
        }
        // "play cost 11 or lower Digimon with [Negamon] in its digivolution cards" — 앞 조건 + "Digimon with ~" 조건
        var with = Regex.Match(t, @"^(?<pre>.+?) Digimon with (?<x>.+)$", I);
        if (with.Success && TargetSpec.Parse($"all of your {with.Groups["pre"].Value} Digimon") is { Opponent: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } sp1
            && TargetSpec.Parse($"all of your Digimon with {with.Groups["x"].Value}") is { Opponent: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } sp2)
        {
            var fs = sp1.Filters.Concat(sp2.Filters).ToList();
            return d => fs.All(f => f(d));
        }
        return null;
    }

    private static bool DeletedCostClause42Impl(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var m = DeletedCostRe42.Match(Normalize(rawClause));
        if (!m.Success || inherited) return false;
        if (OwnDigimonFilter42(m.Groups["t"].Value) is not { } filter) return false;
        List<CardInstance> Pool(EffectContext c) => c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && filter(d)).OrderByDescending(d => d.PlayCost45).ToList();
        bool CanPay(EffectContext c) => Pool(c).Count > 0;
        // 낸 비용: 고른 디지몬을 소멸시키고 그 등장 코스트를 PaidCount에 (소멸이 막히면 0)
        void Pay(EffectContext c)
        {
            var pool = Pool(c);
            c.PaidCount = 0;
            if (pool.Count == 0) return;
            var d = c.Engine.AskDigimon(c.Owner, "비용으로 소멸시킬 자신의 디지몬을 고르세요.", pool, pool[0], optional: false) ?? pool[0];
            int pc = d.PlayCost45;
            c.DeleteOwn(d);
            if (!c.Owner.BattleArea.Contains(d))
            {
                c.PaidCount = Math.Max(0, pc);
                Diag42.Hit(ref Diag42.DeletedCost);
            }
        }
        int Preview(EffectContext c) => Pool(c).Select(d => d.PlayCost45).DefaultIfEmpty(0).Max();
        // AI 추천: 소멸시킬 디지몬의 등장 코스트가 3 이상일 때
        bool Recommend(EffectContext c) => Preview(c) >= 3;
        const string describe = "자신의 디지몬 1마리를 소멸시켜";
        string costText = "deleting 1 of your " + m.Groups["t"].Value;
        if (m.Groups["self"].Success)
        {
            if (card.Type != CardType.Digimon) return false;
            def?.OptionalPlayCostReduction(new PlayCostOption(1, CanPay, Pay, Recommend, describe, costText) { PerCount = true, PreviewCount = Preview });
            return true;
        }
        if (FieldPlayCostTarget(m.Groups["p"].Value) is not { } which) return false;
        string labels = m.Groups["labels"].Value;
        def?.FieldPlayCostModifier(new FieldPlayCostModifier(EffectSource.Main, which, 0, new PlayCostOption(0, CanPay, Pay, Recommend, describe, costText),
            Optional: false,
            OncePerTurn: labels.Contains("[Once Per Turn]", StringComparison.OrdinalIgnoreCase),
            YourTurnOnly: labels.Contains("[Your Turn]", StringComparison.OrdinalIgnoreCase),
            Breeding: false) { Amount = Preview, PaidAmount42 = c => c.PaidCount });
        return true;
    }
}
