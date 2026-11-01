using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.78.0 미지원 문장 52차 묶음 (원래 규칙이 읽지 못한 절만):
///  - BT17-102 "[All Turns] This Digimon has all the names of level 3 and lower cards in its digivolution cards" — 이 디지몬은 진화원의 Lv.3 이하 카드(앞면 — 4-7-9)의 명칭 전부를 얻는다.
///    육성 에어리어에서도 (공식 Q&amp;A BT17-102 Q1 — 이동한 것은 얻은 명칭의 디지몬이기도 하다). 판정은 CardNames (필드의 디지몬 이름 판정 한 곳)
///  - BT17-050 [패]【메인】 "By paying 4 cost and placing this card as 1 of your level 5 or higher Digimon's bottom digivolution card, you may suspend 1 of your opponent's Digimon and
///    attack an opponent's Digimon with the Digimon this was placed under" — 비용(4 코스트 + 진화원 아래)을 내야 발휘 (공식 Q&amp;A BT17-050 Q1). 레스트 → 놓은 디지몬으로 상대의 디지몬에게 어택
///    (효과 어택은 지금 엔진의 효과 어택 그대로 — GoldfishEngine.EffectAttack: 자기 턴 · 어택할 수 있는 디지몬 · 어택 중 새 어택 선언 불가 11-2-4)
///  - BT25-038 "~ from your hand or your Digimon's digivolution cards as the top or bottom security card" — 패 또는 자신의 디지몬(배틀 에어리어)의 앞면 진화원에서 1장, 위 · 아래는 고른다
///  - EX13-076 "Then, you may return all digivolution cards of 1 of their Digimon to the bottom of the deck and have this Digimon battle it. Compare the number of digivolution cards instead of DP in this battle."
///    — 진화원을 덱 아래로 되돌리고 (주인의 덱) 이 디지몬과 배틀, 그 배틀은 DP 대신 진화원 매수 비교 (≪빙장≫과 같은 비교 — 종합 룰 4-12의 배틀 판정을 효과가 바꾼다)
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I78 = RegexOptions.IgnoreCase;

    /// <summary>EX13-076 표식 — "진화원을 덱 아래로 되돌리고 이 디지몬과 진화원 매수로 배틀"</summary>
    internal const string ReturnSourcesBattleMarker78 = "⟦retsrcbattle78⟧";

    /// <summary>BT17-102 절</summary>
    internal static readonly Regex SourceNamesRe78 = new(
        @"^\[All Turns\] This Digimon has all (?:the )?names of level (?<lv>\d+) (?:and|or) lower cards in its digivolution cards\.?$", I78 | RegexOptions.Compiled);

    /// <summary>원래 규칙이 읽지 못한 절 → 같은 뜻의 다른 표기 (바꾸지 못하면 null)</summary>
    internal static string? Rewrite78(string rawClause)
    {
        string t = rawClause.Trim();
        string a = Regex.Replace(t, @"\. Then, you may return all (?:of the )?digivolution cards of 1 of (?:their|your opponent's) Digimon to the bottom of (?:the|its owner's) deck and have this Digimon battle it\. Compare the number of digivolution cards instead of DP in this battle\.?$",
            ". Then, you may " + ReturnSourcesBattleMarker78 + ".", I78);
        return a == t ? null : a;
    }

    private static string? TryParse78(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse77(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만
        if (!inherited && card.Type == CardType.Digimon && SourceNamesRe78.Match(rawClause.Trim()) is { Success: true } sm)
        {
            if (def != null)
            {
                def.Script41.SourceNamesMaxLevel78 = int.Parse(sm.Groups["lv"].Value);
                Diag46.Hit("sourceNames78");
            }
            return null;
        }
        if (Rewrite78(rawClause) is not { } alt || TryParse77(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse77(alt, inherited, card, def);
            Diag46.Hit("rewrite78");
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction78(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I78);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        // BT17-050 "suspend 1 of your opponent's Digimon and attack an opponent's Digimon with that Digimon" — that Digimon = 비용으로 이 카드를 놓은 디지몬
        if (Regex.IsMatch(s, @"^suspend (?:1|one) of your opponent's Digimon and attack (?:an|1 of your) opponent's Digimon with that Digimon$", I78))
        {
            var oppSpec = TargetSpec.Parse("1 of your opponent's Digimon");
            if (oppSpec == null) return null;
            return (c =>
            {
                var attacker = c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d));
                var pool = oppSpec.Resolve(c, null).Where(d => !d.IsSuspended).ToList();
                if (pool.Count == 0) pool = oppSpec.Resolve(c, null);
                if (pool.Count > 0)
                {
                    var best = attacker == null ? pool.OrderByDescending(d => d.GetCurrentDP()).First()
                        : pool.Where(d => d.GetCurrentDP() < attacker.GetCurrentDP()).OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).FirstOrDefault()
                          ?? pool.OrderByDescending(d => d.GetCurrentDP()).First();
                    var t = c.Engine.ChooseTargets(c.Owner, pool.OrderByDescending(d => ReferenceEquals(d, best)).ToList(), 1, $"[{c.Card.Name}] 레스트시킬 상대의 디지몬을 고르세요.", optional: false, preselected: best).FirstOrDefault() ?? best;
                    c.Engine.Suspend(t);
                    Emit(c, label, $"[{t.TopCard.Name}] 레스트");
                }
                if (attacker == null || !c.Engine.EffectAttack(c.Owner, attacker, EffectAttackTarget.DigimonOnly, withoutSuspending: false, label, forced: true))
                {
                    c.LastStepResolved = false;
                    Emit(c, label, attacker == null ? "어택할 디지몬이 없습니다" : $"[{attacker.TopCard.Name}]은(는) 어택할 수 없습니다");
                    return;
                }
                Diag46.Hit("placedUnderAttack78");
            }, null);
        }
        // EX13-076 ⟦retsrcbattle78⟧ — 상대의 디지몬 1마리의 진화원 전부를 덱 아래로 되돌리고 이 디지몬과 배틀 (진화원 매수 비교)
        if (s.Equals(ReturnSourcesBattleMarker78, StringComparison.Ordinal) && card.Type == CardType.Digimon)
        {
            var oppSpec = TargetSpec.Parse("1 of your opponent's Digimon");
            if (oppSpec == null) return null;
            return (c =>
            {
                var self = c.Self;
                if (self == null || !c.Owner.BattleArea.Contains(self)) { c.LastStepResolved = false; Emit(c, label, "배틀할 이 디지몬이 없습니다"); return; }
                var pool = oppSpec.Resolve(c, null);
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
                var best = pool.OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.EvolutionSources.Count).ThenByDescending(d => d.GetCurrentDP()).First();
                var d = c.Engine.ChooseTargets(c.Owner, pool.OrderByDescending(x => ReferenceEquals(x, best)).ToList(), 1,
                    $"[{c.Card.Name}] 진화원을 덱 아래로 되돌리고 이 디지몬과 배틀할 상대의 디지몬을 고르세요 (진화원 매수로 비교).", optional: false, preselected: best).FirstOrDefault() ?? best;
                int n = c.Engine.ReturnAllSourcesToDeckBottom78(c.Engine.OwnerOf(d), d);
                c.LastTargets = new List<CardInstance> { d };
                Emit(c, label, $"[{d.TopCard.Name}]의 진화원 {n}장을 덱 아래로");
                if (!c.Engine.OwnerOf(d).BattleArea.Contains(d) || !c.Owner.BattleArea.Contains(self)) { c.LastStepResolved = false; return; }
                Emit(c, label, c.Engine.EffectBattleBySourceCount78(self, d));
                Diag46.Hit("sourceCountBattle78");
            }, null);
        }
        return null;
    }

    /// <summary>
    /// 비용 (원래 규칙이 읽지 못한 것만 — 체인 맨 끝):
    ///  - BT25-038 "placing 1 [Angel] ~ trait Digimon card from your hand or your Digimon's digivolution cards as the top or bottom security card" — 패 또는 자신의 디지몬(배틀 에어리어)의 앞면 진화원
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost78(string cost, CardData card)
    {
        Match m;
        if ((m = Regex.Match(cost.Trim(), @"^placing (?<pick>.+?) from your hand or (?:from )?(?:1 of )?your Digimon's digivolution cards as (?:the|your) (?<pos>top or bottom|top|bottom) security card$", I78)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } pick)
        {
            var (n, match) = pick;
            string pos = m.Groups["pos"].Value.ToLowerInvariant();
            List<(CardData Card, CardInstance? From)> Able(EffectContext c) =>
                c.Owner.Hand.Where(match).Select(x => (x, (CardInstance?)null))
                    .Concat(c.Owner.BattleArea.Where(d => d.TamerProxyOf == null).SelectMany(d => d.SourcesWhere(match).Select(x => (x, (CardInstance?)d)))).ToList();
            return (c => Able(c).Count >= n && !c.Engine.SecurityAddBlocked65(c.Owner),   // v0.65.0 BT9-103
                    c =>
                    {
                        // 추천: 진화원 → 패 (패를 남긴다)
                        var able = Able(c).OrderBy(p => p.From == null ? 1 : 0).ThenBy(p => p.Card.Level).ToList();
                        var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, able.Select(p => p.Card).ToList(), n, upTo: false,
                            $"[{c.Card.Name}] 시큐리티에 놓을 카드를 패 또는 자신의 디지몬의 진화원에서 {n}장 고르세요.");
                        foreach (var p in idx.Distinct().Where(i => i >= 0 && i < able.Count).Select(i => able[i]))
                        {
                            if (p.From == null) { if (!c.Owner.Hand.Remove(p.Card)) continue; }
                            else if (!c.Owner.BattleArea.Contains(p.From) || p.From.TakeSource(y => ReferenceEquals(y, p.Card)) == null) continue;
                            bool top = pos == "top" || (pos != "bottom" && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] [{p.Card.Name}]을(를) 시큐리티 맨 위에 놓을까요? (아니오 = 맨 아래)", recommended: true));
                            if (top) c.Owner.SecurityStack.Insert(0, p.Card); else c.Owner.SecurityStack.Add(p.Card);
                            c.Log($"[{c.Card.Name}] {(p.From == null ? "패" : $"[{p.From.TopCard.Name}]의 진화원")}의 [{p.Card.Name}]을(를) 시큐리티 맨 {(top ? "위" : "아래")}에 놓았습니다");
                            c.Engine.NotifySecurityAdded(c.Owner, p.Card);
                            c.LastCard = p.Card;
                            Diag46.Hit("handOrSourceToSecurity78");
                        }
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"패 또는 자신의 디지몬의 진화원의 카드 {n}장을 시큐리티 {(pos == "top" ? "위" : pos == "bottom" ? "아래" : "위 또는 아래")}에 놓아");
        }
        return null;
    }
}
