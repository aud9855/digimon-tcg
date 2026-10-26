using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.38.0 (3) 위 또는 아래: 진화원 위 · 아래에 놓기 (AD1-007 · EX12-018 · EX12-077 — 공식 "이 디지몬의 진화원 위 또는 아래에 놓는"),
/// 시큐리티 위 또는 아래 파기 (BT16-056 · EX13-025 — 공식 "시큐리티를 위 또는 아래에서 1장 파기"), 상대 디지몬의 맨 위 카드를 시큐리티 위에 (BT16-056)
/// </summary>
internal static partial class SentenceParser
{
    private static readonly Regex PlaceSources38Re = new(
        @"^(?:placing|place) (?<pick>.+?) from your (?<z>hand or (?:your )?trash|hand|trash) (?:as (?<host>this Digimon's|(?:1|one) of your (?<hf>[^,]*?)Digimon's) (?<pos>top or bottom|top|bottom) digivolution cards?|under (?<host2>this Digimon|(?:1|one) of your (?<hf2>[^,]*?)Digimon) as (?:its|their) (?<pos2>top or bottom|top|bottom) digivolution cards?)$",
        I | RegexOptions.Compiled);

    /// <summary>패 · 트래시의 카드를 진화원 위 · 아래에 놓는 동작 (비용 · 동작 같이). 반환: 놓은 매수 (못 하면 0). 이미 원래 규칙이 읽는 "아래"만이면 null</summary>
    private static (Func<EffectContext, bool> Can, Func<EffectContext, int> Run, string Describe, int N, bool UpTo)? PlaceSources38(string text)
    {
        var m = PlaceSources38Re.Match(text.Trim().TrimEnd('.'));
        if (!m.Success) return null;
        string pos = m.Groups["pos"].Success ? m.Groups["pos"].Value.ToLowerInvariant() : m.Groups["pos2"].Value.ToLowerInvariant();
        string pickText = Regex.Replace(m.Groups["pick"].Value.Trim(), @"\bin its text\b", "in their texts", I);
        bool upTo = IsUpTo(pickText);
        if ((ParseCardPick(pickText) ?? ParseNamePick(pickText)) is not { } pick) return null;
        var (n, match) = pick;
        string z = m.Groups["z"].Value.ToLowerInvariant();
        bool fromHand = z.Contains("hand"), fromTrash = z.Contains("trash");
        bool thisHost = (m.Groups["host"].Success ? m.Groups["host"].Value : m.Groups["host2"].Value).StartsWith("this", StringComparison.OrdinalIgnoreCase);
        string hf = m.Groups["hf"].Success ? m.Groups["hf"].Value : m.Groups["hf2"].Value;
        var hostSpec = thisHost ? null : TargetSpec.Parse($"1 of your {hf}Digimon");
        if (!thisHost && hostSpec == null) return null;
        List<(CardData Card, bool Hand)> Pool(EffectContext c) =>
            (fromHand ? c.Owner.Hand.Where(match).Select(x => (x, true)) : Enumerable.Empty<(CardData, bool)>())
            .Concat(fromTrash ? c.Owner.Trash.Where(match).Select(x => (x, false)) : Enumerable.Empty<(CardData, bool)>()).ToList();
        IEnumerable<CardInstance> Hosts(EffectContext c) => thisHost
            ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new[] { c.Self } : Array.Empty<CardInstance>())
            : c.Owner.BattleArea.Where(d => hostSpec!.Filters.All(f => f(d)));
        int need = upTo ? 1 : n;
        string posKo = pos == "top" ? "위" : pos == "bottom" ? "아래" : "위 또는 아래";
        return (c => Hosts(c).Any() && Pool(c).Count >= need,
                c =>
                {
                    var host = thisHost ? Hosts(c).FirstOrDefault()
                        : c.Engine.ChooseTargets(c.Owner, Hosts(c).OrderByDescending(d => d.GetCurrentDP()).ToList(), 1, $"[{c.Card.Name}] 진화원을 놓을 자신의 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                    if (host == null) return 0;
                    var pool = Pool(c).OrderBy(p => p.Hand ? 1 : 0).ThenBy(p => p.Card.Level).ToList();
                    var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.HandOrTrash, pool.Select(p => p.Card).ToList(), Math.Min(n, pool.Count), upTo,
                        $"[{c.Card.Name}] 진화원 {posKo}에 놓을 카드 {n}장{(upTo ? "까지" : "")}를 고르세요.");
                    if (idx.Count == 0 || (!upTo && idx.Count < n)) return 0;
                    // 위 또는 아래: 고른다 (추천: 아래 — 맨 위 카드 바로 아래의 진화원 효과를 바꾸지 않는다)
                    bool top = pos == "top" || (pos == "top or bottom" && c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 진화원의 어디에 놓을까요?", new[] { "아래", "위" }, 0) == 1);
                    var fromTrashMoved = new List<CardData>();
                    int placed = 0;
                    foreach (var i in idx)
                    {
                        var (x, hand) = pool[i];
                        if (!(hand ? c.Owner.Hand.Remove(x) : c.Owner.Trash.Remove(x))) continue;
                        if (!hand) fromTrashMoved.Add(x);
                        if (top) host.AddTopSource(x); else host.AddBottomSource(x);
                        placed++;
                    }
                    if (fromTrashMoved.Count > 0) c.Engine.NotifyTrashMoved(c.Owner, fromTrashMoved, toHand: false);
                    c.RecalculateFieldBuffs();
                    c.LastTargets = new List<CardInstance> { host };
                    if (placed > 0) Diag38.Hit(ref Diag38.PlaceTopBottom);
                    return placed;
                },
                $"{(fromHand && fromTrash ? "패/트래시" : fromHand ? "패" : "트래시")}의 카드 {n}장{(upTo ? "까지" : "")}를 진화원 {posKo}에 놓아", n, upTo);
    }

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? PlaceSourcesCost38(string cost)
    {
        if (PlaceSources38(cost) is not { } p) return null;
        return (p.Can, c => { int k = p.Run(c); c.PaidCount = k; if (k == 0) c.CostFailed = true; }, _ => true, p.Describe);
    }

    private static (Action<EffectContext>? Act, string? Error)? PlaceSourcesAction38(string s, string label)
    {
        if (PlaceSources38(s) is not { } p) return null;
        return (c =>
        {
            int k = p.Can(c) ? p.Run(c) : 0;
            c.PaidCount = k;
            if (k == 0) c.LastStepResolved = false;
            Emit(c, label, k > 0 ? $"진화원에 {k}장 놓음" : "놓을 카드가 없습니다");
        }, null);
    }

    /// <summary>
    /// "Trash the top or bottom card of their security stack" (BT16-056) · "Trash your top or bottom security card" (EX13-025) · "Trash the bottom card of ~" —
    /// 위 또는 아래는 고른다 (추천: 상대 = 위, 자신 = 아래)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? SecurityTopBottom38(string s, string label)
    {
        var m = Regex.Match(s, @"^Trash (?:the (?<pos>top or bottom|bottom) card of (?<who>your opponent's|their|your) security stack|(?<who>your opponent's|their|your) (?<pos>top or bottom|bottom) security card)$", I);
        if (!m.Success) return null;
        bool enemy = !m.Groups["who"].Value.Equals("your", StringComparison.OrdinalIgnoreCase);
        bool choose = m.Groups["pos"].Value.StartsWith("top", StringComparison.OrdinalIgnoreCase);
        return (c =>
        {
            var zone = enemy ? c.Enemy : c.Owner;
            if (zone.SecurityStack.Count == 0) { c.LastStepResolved = false; Emit(c, label, "시큐리티가 없습니다"); return; }
            bool top = choose && c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] {(enemy ? "상대" : "자신")}의 시큐리티의 어느 쪽을 파기할까요?", new[] { "위", "아래" }, enemy ? 0 : 1) == 0;
            int at = top ? 0 : zone.SecurityStack.Count - 1;
            var x = zone.SecurityStack[at];
            zone.SecurityStack.RemoveAt(at);
            zone.Trash.Add(x);
            c.Engine.NotifySecurityRemoved(zone, x, trashed: true);
            Diag38.Hit(ref Diag38.SecurityTopBottom);
            Emit(c, label, $"{(enemy ? "상대" : "자신")}의 시큐리티 {(top ? "위" : "아래")}에서 1장 파기");
        }, null);
    }

    /// <summary>"Place the top card of 1 of your opponent's [Vaccine] trait Digimon on top of their security stack" (BT16-056 — 공식 "~에 겹쳐져 있는 카드를 위에서부터 1장 상대의 시큐리티 위에 놓는다")</summary>
    private static (Action<EffectContext>? Act, string? Error)? TopCardToSecurity38(string s, string label, bool security)
    {
        var m = Regex.Match(s, @"^Place the top card of (?<t>(?:1|one) of your opponent's .+?) (?:on (?<pos>top|the bottom) of their security stack|as their (?<pos>top|bottom) security card)$", I);
        if (!m.Success || TargetSpec.Parse(m.Groups["t"].Value) is not { Opponent: true } target) return null;
        bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
        return (c =>
        {
            var d = Pick(target, c, security).FirstOrDefault();
            if (d == null) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
            string name = d.TopCard.Name;
            // 진화원이 없으면 그 디지몬 자체가 시큐리티로 (떠나려 할 때 효과 · ≪회피≫는 상대 효과로 막을 수 있다)
            bool ok = d.EvolutionSources.Count > 0 ? c.Engine.PlaceTopCardInSecurity(c.Enemy, d, top) : c.Engine.PlaceDigimonInSecurity(c.Enemy, d, top, faceUp: false, canPrevent: true);
            if (!ok) c.LastStepResolved = false; else Diag38.Hit(ref Diag38.SecurityTopBottom);
            Emit(c, label, ok ? $"[{name}]의 맨 위 카드를 상대의 시큐리티 {(top ? "위" : "아래")}에" : "놓지 못했습니다");
        }, null);
    }
}
