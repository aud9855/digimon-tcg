using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.39.0 비용 (원래 규칙이 읽지 못한 것만): 상대의 트래시 · 서로의 트래시에서 덱으로 (EX13-077 · BT26-016 · BT18-019 "Lv.이 서로 다른 디지몬 카드 1장씩") ·
/// "1 [Kimeramon] and 1 [Machinedramon]" (1장씩) · "from those Digimon's digivolution cards" (BT20-097 — 공식 "그 디지몬의 진화원에서")
/// </summary>
internal static partial class SentenceParser
{
    internal static readonly Regex TrashReturnCostRe39 = new(@"^returning (?<pick>.+?) (?:in|from) (?<z>trashes|both players' trashes|your opponent's trash|their trash|your or your opponent's trash|your trash|trash) to the (?<pos>top|bottom) of (?:the|your|their|its owner's|their owners') decks?(?: in any order)?$", RegexOptions.IgnoreCase);

    internal static readonly Regex ThoseSourcesCostRe39 = new(@"^returning (?<pick>.+?) from (?:that|those) Digimon's digivolution cards to (?:the |your |its owner's )?(?<to>hand|bottom of (?:the|your|its owner's) deck)$", RegexOptions.IgnoreCase);

    internal static readonly Regex UnderTamerToThatCostRe39 = new(@"^placing (?<n>\d+) cards? from under (?:one of |1 of |any of )?your Tamers (?:as the bottom digivolution cards? of that Digimon|under that Digimon as its bottom digivolution cards?|as any of their bottom digivolution cards?)$", RegexOptions.IgnoreCase);

    private sealed record TrashCard39(CardData Card, PlayerZones Zone);

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost39(string cost, CardData card)
    {
        Match m;
        if ((m = TrashReturnCostRe39.Match(cost.Trim())).Success)
        {
            string z = m.Groups["z"].Value.ToLowerInvariant();
            bool mine = z is "your trash" or "trash", theirs = z.Contains("opponent") || z.StartsWith("their"), both = z.Contains("trashes") || z.Contains(" or ");
            if (mine && !both) theirs = false;
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            string pick = m.Groups["pick"].Value.Trim();
            List<TrashCard39> Pool(EffectContext c, Func<CardData, bool> ok) =>
                (mine || both ? c.Owner.Trash.Where(ok).Select(x => new TrashCard39(x, c.Owner)) : Enumerable.Empty<TrashCard39>())
                .Concat(theirs || both ? c.Enemy.Trash.Where(ok).Select(x => new TrashCard39(x, c.Enemy)) : Enumerable.Empty<TrashCard39>())
                .OrderBy(t => ReferenceEquals(t.Zone, c.Owner) ? 1 : 0).ThenBy(t => t.Card.Level).ToList();   // 추천: 상대 트래시의 카드부터
            void Return(EffectContext c, List<TrashCard39> picked)
            {
                foreach (var g in picked.GroupBy(t => t.Zone))
                {
                    var cards = g.Select(t => t.Card).ToList();
                    foreach (var x in cards) g.Key.Trash.Remove(x);
                    if (top) g.Key.MainDeck.InsertRange(0, cards); else g.Key.MainDeck.AddRange(cards);
                    c.Engine.NotifyTrashMoved(g.Key, cards, toHand: false);
                }
                c.PaidCount = picked.Count;
                Diag39.Hit(ref Diag39.OppTrashCost);
            }
            List<TrashCard39> Choose(EffectContext c, List<TrashCard39> pool, int n, bool upTo, string q) =>
                c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Trash, pool.Select(t => t.Card).ToList(), Math.Min(n, pool.Count), upTo, q).Select(i => pool[i]).ToList();
            string where = both ? "서로의 트래시" : theirs ? "상대의 트래시" : "자신의 트래시";
            string pos = top ? "위" : "아래";
            // "1 of each Digimon card with different levels" (BT18-019 — 공식 "Lv.이 서로 다른 디지몬 카드 1장씩") — 1장 이상, 낸 매수는 PaidCount
            var each = Regex.Match(pick, @"^1 of each (?<p>.+?) with different levels$", RegexOptions.IgnoreCase);
            if (each.Success && ParseCardPick("1 " + each.Groups["p"].Value) is { } ep)
            {
                var match = ep.Match;
                return (c => Pool(c, match).Any(t => t.Card.Level > 0),
                    c =>
                    {
                        var all = Pool(c, x => match(x) && x.Level > 0);
                        var firsts = all.GroupBy(t => t.Card.Level).Select(g => g.First()).ToList();   // 추천: Lv.마다 1장씩 먼저
                        var pool = firsts.Concat(all.Where(t => !firsts.Contains(t))).ToList();
                        int levels = firsts.Count;
                        var picked = Choose(c, pool, levels, true, $"[{c.Card.Name}] 비용으로 {where}에서 Lv.이 서로 다른 카드를 1장씩 고르세요.");
                        picked = picked.GroupBy(t => t.Card.Level).Select(g => g.First()).ToList();   // 같은 Lv.은 1장만
                        if (picked.Count == 0) { c.CostFailed = true; return; }
                        Return(c, picked);
                    },
                    c => Pool(c, match).Any(), $"{where}에서 Lv.이 서로 다른 카드 1장씩을 덱 {pos}로 되돌려");
            }
            // "1 [Kimeramon] and 1 [Machinedramon]" — 1장씩
            var parts = Regex.Split(pick, @"\s+and\s+(?=1 )", RegexOptions.IgnoreCase);
            var picks = parts.Select(p => ParseCardPick(p) ?? ParseNamePick(p)).ToList();
            if (picks.Any(p => p == null)) return null;
            bool upToAll = IsUpTo(pick);
            int total = picks.Sum(p => p!.Value.Count);
            bool CanAll(EffectContext c)
            {
                var used = new List<TrashCard39>();
                foreach (var p in picks)
                {
                    var avail = Pool(c, p!.Value.Match).Where(t => !used.Contains(t)).Take(p.Value.Count).ToList();
                    if (avail.Count < (upToAll ? 1 : p.Value.Count)) return false;
                    used.AddRange(avail);
                }
                return true;
            }
            return (CanAll,
                c =>
                {
                    var all = new List<TrashCard39>();
                    foreach (var p in picks)
                    {
                        var pool = Pool(c, p!.Value.Match).Where(t => !all.Contains(t)).ToList();
                        var picked = Choose(c, pool, p.Value.Count, upToAll, $"[{c.Card.Name}] 비용으로 {where}에서 덱 {pos}로 되돌릴 카드를 고르세요.");
                        if (picked.Count < (upToAll ? 1 : p.Value.Count)) { c.CostFailed = true; return; }
                        all.AddRange(picked);
                    }
                    Return(c, all);
                },
                c => both || theirs || Pool(c, _ => true).Count >= total + 3, $"{where}의 카드 {total}장을 덱 {pos}로 되돌려");
        }
        // "placing 1 card from under one of your Tamers as the bottom digivolution card of that Digimon" (BT12-087 · BT12-091 ~ BT12-097 — 진화 코스트 감소의 비용: 진화하는 그 디지몬) ·
        // "~ under that Digimon as its bottom digivolution card" (BT12-096) · "placing 1 card from under your Tamers as any of their bottom digivolution card" (BT21-088)
        if ((m = UnderTamerToThatCostRe39.Match(cost.Trim())).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            static CardInstance? Host(EffectContext c) =>
                c.Other != null && c.Owner.BattleArea.Contains(c.Other) ? c.Other : c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d));
            static List<CardData> Avail(EffectContext c) => c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x)).ToList();
            return (c => Host(c) != null && Avail(c).Count >= n,
                c =>
                {
                    var h = Host(c);
                    var pool = Avail(c).OrderByDescending(x => x.InheritedEffectText.Trim().Length > 0).ThenBy(x => x.Level).ToList();
                    if (h == null || pool.Count < n) { c.CostFailed = true; return; }
                    var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, n, false, $"[{c.Card.Name}] 비용으로 테이머 아래에서 [{h.TopCard.Name}]의 진화원 아래에 놓을 카드를 고르세요.");
                    foreach (var x in picked) if (c.Owner.UnderTamers.Remove(x)) h.AddBottomSource(x);
                    c.RecalculateFieldBuffs();
                    c.PaidCount = picked.Count;
                    Diag39.Hit(ref Diag39.SourcePlace);
                },
                _ => true, $"테이머 아래의 카드 {n}장을 그 디지몬의 진화원 아래에 놓아");
        }
        // "returning 1 [Dorumon] from those Digimon's digivolution cards to the hand" (BT20-097) · "returning 2 [Vemmon] from that Digimon's digivolution cards to the bottom of the deck" (BT18-092)
        if ((m = ThoseSourcesCostRe39.Match(cost.Trim())).Success && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } sp)
        {
            var (n, match) = sp;
            bool toHand = m.Groups["to"].Value.StartsWith("hand", StringComparison.OrdinalIgnoreCase);
            static CardInstance? Host(EffectContext c) =>
                c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d)) ?? (c.Other != null && c.Owner.BattleArea.Contains(c.Other) ? c.Other : null);
            List<CardData> Avail(EffectContext c) => Host(c) is { } h ? h.SourcesWhere(x => !h.IsFaceDown(x) && match(x)).ToList() : new();
            return (c => Avail(c).Count >= n,
                c =>
                {
                    var h = Host(c);
                    var pool = Avail(c);
                    if (h == null || pool.Count < n) { c.CostFailed = true; return; }
                    var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, pool, n, false, $"[{c.Card.Name}] 비용으로 [{h.TopCard.Name}]의 진화원에서 고르세요.");
                    var moved = new List<CardData>();
                    foreach (var x in picked) if (h.TakeSource(y => ReferenceEquals(y, x)) is { } t) moved.Add(t);
                    if (moved.Count < n) c.CostFailed = true;
                    if (toHand) c.Owner.Hand.AddRange(moved); else c.Owner.MainDeck.AddRange(moved);
                    c.RecalculateFieldBuffs();
                    c.PaidCount = moved.Count;
                    Diag39.Hit(ref Diag39.ThoseSourcesCost);
                },
                _ => true, $"그 디지몬의 진화원에서 {n}장을 {(toHand ? "패로" : "덱 아래로")} 되돌려");
        }
        return null;
    }
}
