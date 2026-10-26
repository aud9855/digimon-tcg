using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

internal static partial class SentenceParser
{
    // =====================================================================
    // v0.38.0 비용 — 원래 규칙이 읽지 못한 것만 (ParseCost)
    // =====================================================================

    private enum Zone38 { Hand, Trash, SelfSources, OwnSources, Links }

    /// <summary>
    /// v0.38.0 비용: 여러 곳에서 고르는 파기 · 되돌리기 ("trashing 1 [Mineral] or [Rock] trait card from your hand or your Digimon's digivolution cards" EX11-038 ·
    /// "trashing 1 Option card from any of your Digimon's digivolution cards or link cards" BT25-085 · "returning 1 [Lucemon] from this Digimon's digivolution cards or from your trash to the bottom of the deck" EX6-054 ·
    /// "returning 3 cards with the [Three Musketeers] trait from your hand or trash to the top of the deck" EX7-043) · 자신의 디지몬 N마리 소멸 · 토큰 소멸 ·
    /// 진화원이 있는 이 디지몬 소멸 · 색 테이머 레스트 · 맨 위 카드를 패로 · 패의 같은 Lv. 카드 파기
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost38(string cost, CardData card)
    {
        Match m;
        cost = cost.Trim().TrimEnd('.');
        // 다른 표기 → 원래 규칙
        string alt = cost;
        alt = Regex.Replace(alt, @"^(?<v>deleting \d+ of your Digimon with the \[[^\]]+\](?: or \[[^\]]+\])* trait) Digimon$", "${v}", I);   // BT19-075
        alt = Regex.Replace(alt, @"^trashing (?<n>\d+) of your Digimon (?<f>with .+?) from your hand$", "trashing ${n} Digimon card ${f} from your hand", I);   // BT12-108
        alt = Regex.Replace(alt, @"\bwith a digivolution card and ", "with 1 or more digivolution cards and ", I);   // EX4-011
        alt = Regex.Replace(alt, @"^Deleting\b", "deleting");
        if (alt != cost && ParseCostCore(alt, card) is { } same) return same;

        if (MultiZoneCost38(cost) is { } multi) return multi;
        if (PlaceSourcesCost38(cost) is { } placeSrc) return placeSrc;   // AD1-007 · EX12-077 "top or bottom" (Batch38c)

        // "deleting 2 of your level 5 or higher Digimon with [Myotismon] in their texts" (EX10-011) · "deleting 1 of your Digimon with 1 or more digivolution cards and [Gallantmon] in its name" (EX4-011)
        if ((m = Regex.Match(alt, @"^deleting (?<n>\d+) of your (?<other>other )?(?<t>.+)$", I)).Success
            && TargetSpec.Parse("all of your " + Regex.Replace(m.Groups["t"].Value, @"\btheir (names?|texts?)\b", "its $1", I)) is { } spec && spec.ContextFilters.Count == 0 && !spec.Opponent && !spec.AnySide)
        {
            int n = int.Parse(m.Groups["n"].Value); bool other = m.Groups["other"].Success;
            IEnumerable<CardInstance> Pool(EffectContext c) => c.Owner.BattleArea.Where(d => (!other || !ReferenceEquals(d, c.Self)) && spec.Filters.All(f => f(d)))
                .OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP());
            return (c => Pool(c).Count() >= n,
                    c =>
                    {
                        var pool = Pool(c).ToList();
                        var picks = c.Engine.ChooseTargets(c.Owner, pool, n, $"[{c.Card.Name}] 비용으로 소멸시킬 자신의 디지몬 {n}마리를 고르세요.", optional: false, preselected: null);
                        if (picks.Count < n) { c.CostFailed = true; return; }
                        foreach (var d in picks) c.DeleteOwn(d);
                        Diag38.Hit(ref Diag38.OtherCost);
                        c.PaidCount = picks.Count;
                    },
                    c => Pool(c).Take(n).All(d => !ReferenceEquals(d, c.Self) && d.TopCard.Level <= 4), $"자신의 디지몬 {n}마리를 소멸시켜");
        }
        // "deleting 1 Token" (EX11-012) — 자신의 토큰
        if (Regex.IsMatch(cost, @"^deleting (?:1|one) (?:of your )?Tokens?$", I))
            return (c => c.Owner.BattleArea.Any(d => GoldfishEngine.IsToken(d.TopCard)),
                    c => { var t = c.Owner.BattleArea.FirstOrDefault(d => GoldfishEngine.IsToken(d.TopCard)); if (t == null) c.CostFailed = true; else c.DeleteOwn(t); },
                    _ => true, "자신의 토큰 1마리를 소멸시켜");
        // "deleting this Digimon that has a digivolution card with [Bird] or [Avian] in any of its traits" (EX4-058 · BT13-089)
        if ((m = Regex.Match(cost, @"^deleting this Digimon that has a digivolution card (?<pick>with .+)$", I)).Success
            && ParseCardPick("1 card " + m.Groups["pick"].Value) is { } srcPick)
        {
            var match = srcPick.Match;
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && c.Self.SourcesAny(match),
                    c => c.DeleteOwn(c.Self!), _ => false, "조건에 맞는 진화원이 있는 이 디지몬을 소멸시켜");
        }
        // "suspending 1 of your yellow or red Tamers" (BT21-045)
        if ((m = Regex.Match(cost, $@"^suspending (?:1|one) of your (?<col>(?:{Colors})(?: or (?:{Colors}))*) Tamers$", I)).Success)
        {
            var cols = Regex.Matches(m.Groups["col"].Value, Colors, I).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
            IEnumerable<CardData> Pool(EffectContext c) => c.Owner.Tamers.Where(t => c.Owner.HasActiveTamer(t) && t.AllColors.Any(cols.Contains) && !c.Engine.TamerSuspendLocked(c.Owner, t));
            return (c => Pool(c).Any(),
                    c => { var t = Pool(c).FirstOrDefault(); if (t == null) c.CostFailed = true; else c.Engine.SuspendTamer(c.Owner, t); },
                    _ => true, "조건에 맞는 자신의 테이머 1명을 레스트시켜");
        }
        // "returning the top card of one of your [Leopardmon: Leopard Mode] to hand" (BT13-107)
        if ((m = Regex.Match(cost, $@"^returning the top card of (?:1|one) of your (?<x>{TargetSpec.NameList}) to (?:the |your |its owner's )?hand$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            IEnumerable<CardInstance> Pool(EffectContext c) => c.Owner.BattleArea.Where(d => d.EvolutionSources.Count > 0 && xs.Any(x => CardNames.Is(d.TopCard, x)));
            return (c => Pool(c).Any(),
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Pool(c).ToList(), 1, $"[{c.Card.Name}] 맨 위 카드를 패로 되돌릴 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        var top = d?.TrashTopCard();
                        if (top == null) { c.CostFailed = true; return; }
                        c.Owner.Hand.Add(top);
                        c.RecalculateFieldBuffs();
                        Diag38.Hit(ref Diag38.OtherCost);
                    },
                    _ => true, "맨 위 카드를 패로 되돌려");
        }
        // "trashing 1 card from your hand with the same level as the deleted Digimon" (EX4-052 — 소멸한 디지몬 = 시점의 디지몬)
        if (Regex.IsMatch(cost, @"^trashing (?:1|one) card from your hand with the same level as the deleted Digimon$", I))
        {
            static bool Same(EffectContext c, CardData x) => c.Other != null && c.Other.TopCard.Level > 0 && x.Level == c.Other.TopCard.Level;
            return (c => c.Owner.Hand.Any(x => Same(c, x)),
                    c =>
                    {
                        var pool = c.Owner.Hand.Where(x => Same(c, x)).ToList();
                        var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] 비용으로 버릴 패를 고르세요.");
                        if (picked.Count == 0) { c.CostFailed = true; return; }
                        foreach (var x in picked) { c.Owner.Hand.Remove(x); c.Owner.Trash.Add(x); }
                        c.Engine.NotifyHandTrashed(c.Owner, picked);
                        c.PaidCount = picked.Count;
                    },
                    c => c.Owner.Hand.Count >= 3, "소멸한 디지몬과 같은 Lv.의 패 1장을 버려");
        }
        return null;
    }

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? MultiZoneCost38(string cost)
    {
        var m = Regex.Match(cost, @"^(?<v>trashing|returning) (?<pick>.+?) (?:from|in) (?<zones>(?:(?:your |the |any of your |in |from )*(?:hand|trash|this Digimon's digivolution cards|Digimon's digivolution cards|digivolution cards|Digimon|link cards)(?:,? (?:or|and/or) )?)+?)(?: to (?:the |your |its owner's )?(?<dest>hand|top of (?:the|your) deck|bottom of (?:the|your) deck)(?: in any order)?)?$", I);
        if (!m.Success) return null;
        bool trash = m.Groups["v"].Value.Equals("trashing", StringComparison.OrdinalIgnoreCase);
        if (trash == m.Groups["dest"].Success) return null;
        string dest = trash ? "trash" : m.Groups["dest"].Value.ToLowerInvariant().StartsWith("hand") ? "hand" : m.Groups["dest"].Value.ToLowerInvariant().StartsWith("top") ? "top" : "bottom";
        var zones = new List<Zone38>();
        foreach (var z in Regex.Split(m.Groups["zones"].Value, @",? (?:or|and/or) ", I))
        {
            string zz = Regex.Replace(z.Trim(), @"^(?:(?:your|the|any of your|in|from) )+", "", I).ToLowerInvariant();
            Zone38? zone = zz switch
            {
                "hand" => Zone38.Hand,
                "trash" => Zone38.Trash,
                "this digimon's digivolution cards" => Zone38.SelfSources,
                "digimon's digivolution cards" or "digivolution cards" or "digimon" => Zone38.OwnSources,
                "link cards" => Zone38.Links,
                _ => null,
            };
            if (zone == null) return null;
            zones.Add(zone.Value);
        }
        // "any 3 digivolution cards with the [X] trait from your Digimon" · "up to 3 [Three Musketeers] text cards" · "1 [Mineral] or [Rock] trait card"
        string pickText = m.Groups["pick"].Value.Trim();
        pickText = Regex.Replace(pickText, @"^any (?<n>\d+) digivolution cards\b", "${n} cards", I);
        pickText = Regex.Replace(pickText, $@"^(?<n>(?:up to )?\d+) (?<x>{TargetSpec.NameList}) text cards?$", "${n} cards with ${x} in their texts", I);
        // 한 곳(패 · 트래시 · 이 디지몬의 진화원)만이면 원래 규칙의 것 — 자신의 디지몬 전부의 진화원 · 링크 카드만 여기서
        if (zones.Count < 2 && !(zones.Count == 1 && zones[0] is Zone38.OwnSources or Zone38.Links)) return null;
        bool upTo = IsUpTo(pickText);
        if ((ParseCardPick(pickText) ?? ParseNamePick(pickText)) is not { } pick) return null;
        var (n, match) = pick;
        List<(CardData Card, Zone38 Zone, CardInstance? Host)> Pool(EffectContext c)
        {
            var list = new List<(CardData, Zone38, CardInstance?)>();
            foreach (var z in zones)
            {
                switch (z)
                {
                    case Zone38.Hand: list.AddRange(c.Owner.Hand.Where(match).Select(x => (x, z, (CardInstance?)null))); break;
                    case Zone38.Trash: list.AddRange(c.Owner.Trash.Where(match).Select(x => (x, z, (CardInstance?)null))); break;
                    case Zone38.SelfSources:
                        if (c.Self != null && c.Owner.BattleArea.Contains(c.Self)) list.AddRange(c.Self.SourcesWhere(match).Select(x => (x, z, (CardInstance?)c.Self)));
                        break;
                    case Zone38.OwnSources:
                        foreach (var d in c.Owner.BattleArea) list.AddRange(d.SourcesWhere(match).Select(x => (x, z, (CardInstance?)d)));
                        break;
                    case Zone38.Links:
                        foreach (var d in c.Owner.BattleArea) list.AddRange(d.LinkedCards.Where(match).Select(x => (x, z, (CardInstance?)d)));
                        break;
                }
            }
            return list;
        }
        string where = string.Join("/", zones.Select(z => z switch { Zone38.Hand => "패", Zone38.Trash => "트래시", Zone38.SelfSources => "이 디지몬의 진화원", Zone38.OwnSources => "자신의 디지몬의 진화원", _ => "링크 카드" }));
        string destKo = dest switch { "trash" => "파기하여", "hand" => "패로 되돌려", "top" => "덱 위로 되돌려", _ => "덱 아래로 되돌려" };
        return (c => Pool(c).Count >= (upTo ? 1 : n),
                c =>
                {
                    // 추천: 트래시 → 진화원 효과가 없는 진화원 · 링크 → 패, Lv.이 낮은 것부터
                    var ranked = Pool(c).OrderBy(p => p.Zone == Zone38.Trash ? 0 : p.Zone == Zone38.Hand ? 2 : 1)
                        .ThenBy(p => p.Host != null && p.Card.InheritedEffectText.Length > 0 ? 1 : 0).ThenBy(p => p.Card.Level).ToList();
                    int want = Math.Min(n, ranked.Count);
                    var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, ranked.Select(p => p.Card).ToList(), want, upTo, $"[{c.Card.Name}] 비용으로 {where}에서 카드 {n}장{(upTo ? "까지" : "")}를 고르세요.");
                    var moved = new List<CardData>();
                    var fromHand = new List<CardData>();
                    var fromTrash = new List<CardData>();
                    var fromSources = new List<CardData>();
                    foreach (var i in idx)
                    {
                        var (x, z, host) = ranked[i];
                        bool took = z switch
                        {
                            Zone38.Hand => c.Owner.Hand.Remove(x),
                            Zone38.Trash => c.Owner.Trash.Remove(x),
                            Zone38.Links => host!.LinkedCards.Remove(x),
                            _ => host!.TakeSource(s => ReferenceEquals(s, x)) != null,
                        };
                        if (!took) continue;
                        moved.Add(x);
                        if (z == Zone38.Hand) fromHand.Add(x); else if (z == Zone38.Trash) fromTrash.Add(x); else fromSources.Add(x);
                        switch (dest)
                        {
                            case "trash": c.Owner.Trash.Add(x); break;
                            case "hand": c.Owner.Hand.Add(x); break;
                            case "top": c.Owner.MainDeck.Insert(0, x); break;
                            default: c.Owner.MainDeck.Add(x); break;
                        }
                    }
                    c.PaidCount = moved.Count;
                    if (moved.Count > 0) Diag38.Hit(ref Diag38.MultiZoneCost);
                    if ((!upTo && moved.Count < n) || moved.Count == 0) c.CostFailed = true;
                    if (fromHand.Count > 0 && dest == "trash") c.Engine.NotifyHandTrashed(c.Owner, fromHand);
                    if (fromTrash.Count > 0) c.Engine.NotifyTrashMoved(c.Owner, fromTrash, toHand: dest == "hand");
                    if (fromSources.Count > 0) c.Engine.ApplyOverflowPublic34(c.Owner, fromSources);
                    c.RecalculateFieldBuffs();
                },
                c => upTo || Pool(c).Count(p => p.Zone != Zone38.Hand) >= n || c.Owner.Hand.Count >= n + 2,
                $"{where}에서 카드 {n}장{(upTo ? "까지" : "")}를 {destKo}");
    }
}
