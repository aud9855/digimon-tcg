using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.55.0 미지원 문장 29차 묶음: 원래 규칙(1~28차)이 읽지 못한 절만 29차 규칙으로 다시 읽는다.
///  - BT26-102 비용 "placing 6 [Seven Code] trait Digimon cards from your battle area, link cards or trash as 1 of your ~'s bottom digivolution cards" (세 곳을 섞어서 6장)
///  - EX1-061 진화원 효과 "all your Digimon with ＜Retaliation＞ can attack your opponent's unsuspended level 4 or lower Digimon"
///  - EX10-059 "Choose 1 card in your opponent's hand without looking and place it as any of their Digimon's bottom digivolution card or under any of their Tamers"
///    · "delete 1 of their Digimon or Tamers with cards under it" (테이머 아래 카드는 지금 모델대로 한 묶음 — BT18-095와 같은 판단)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch55.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth55;
    [ThreadStatic] private static int _gate55;
    internal static bool Gate55 => _gate55 > 0;

    private static string? TryParse55(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse54(rawClause, inherited, card, def);
        if (err == null || _clauseDepth55 > 0) return err;
        _clauseDepth55++;
        _gate55++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (ClauseSpecial55(rawClause, inherited, card, def) is { } special) return special.Length == 0 ? null : err;
            // 문이 열린 채로 다시 읽는다 (29차의 비용 · 동작 · 셈은 문이 열려 있을 때만) — 바꿔 쓸 것이 없어도
            string alt = ClauseRewrite55(rawClause);
            if (TryParse54(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse54(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth55--;
            _gate55--;
            if (def != null) WrapGate55(def, before);
        }
    }

    /// <summary>29차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate55(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate55++; try { inner(c); } finally { _gate55--; } } };
        }
    }

    internal static readonly Regex DnaLevelRe55 = new(@"^\[Your Turn\]\s*\[(?<for>[^\]]+)\] in your hand can treat this Digimon as level (?<lv>\d+) for DNA digivolution\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 "" (등록함), 해당 없으면 null</summary>
    private static string? ClauseSpecial55(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string t = rawClause.Replace(' ', ' ').Trim();
        // EX3-020 · EX3-041 "[Your Turn] [Examon] in your hand can treat this Digimon as level 6 for DNA digivolution." — 이름은 그대로, Lv.만 (자신의 턴)
        if (!inherited && card.Type == CardType.Digimon && DnaLevelRe55.Match(t) is { Success: true } m)
        {
            def?.Script41.DnaAliasesYourTurn55.Add((m.Groups["for"].Value, card.Name, int.Parse(m.Groups["lv"].Value)));
            return "";
        }
        return null;
    }

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules55 =
    {
        // BT26-102 "from your battle area, link cards or trash" — 쉼표에서 비용이 끊기지 않게
        s => Regex.Replace(s, @"\bfrom your battle area, link cards,? or trash\b", "from your battle area or link cards or trash", RegexOptions.IgnoreCase),
        // EX1-061 "[Your Turn] While this Digimon has [Myotismon] in its name, all your Digimon with ＜Retaliation＞ can attack your opponent's unsuspended level 4 or lower Digimon."
        s => Regex.Replace(s, @"\ball (?:of )?your Digimon with (?<k>＜[^＞]+＞) can (?:also )?attack your opponent's unsuspended level 4 or lower Digimon\.?$",
            "all of your Digimon with ${k} gain ＜CanAttackUnsuspendedLowLevelInternal＞.", RegexOptions.IgnoreCase),
        // EX10-059 "Choose 1 card in your opponent's hand without looking and place it as any of their Digimon's bottom digivolution card or under any of their Tamers."
        s => Regex.Replace(s, @"\bChoose 1 card in your opponent's hand without looking and place it as any of their Digimon's bottom digivolution card or under any of their Tamers\.",
            "Place 1 random card from your opponent's hand as any of their Digimon's bottom digivolution card or under any of their Tamers ⟦blind55⟧.", RegexOptions.IgnoreCase),
        // EX10-059 "delete 1 of their Digimon or Tamers with cards under it" — 진화원이 있는 디지몬 · 아래에 카드가 있는 테이머
        s => Regex.Replace(s, @"\bdelete 1 of their Digimon or Tamers with cards under it\.", "delete 1 of your opponent's Digimon or Tamers with cards under it ⟦under55⟧.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite55(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules55) s = f(s);
        return s;
    }

    /// <summary>비용 (29차): 배틀 에어리어 · 링크 카드 · 트래시를 섞어서 N장을 다른 자신의 디지몬의 진화원 맨 아래에 (BT26-102)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost55(string cost, CardData card)
    {
        if (!Gate55) return null;
        string c0 = cost.Trim();
        var m = Regex.Match(c0, @"^placing (?<n>\d+) (?<spec>.+?) cards from your battle area or link cards or trash (?<dest>as .+)$", I);
        if (!m.Success || ParseCardPick("1 " + m.Groups["spec"].Value + " card") is not { } pick
            || ParsePlaceDest33(Regex.Replace(m.Groups["dest"].Value, @"\bdigivolution cards\b", "digivolution card", I)) is not { Other: { } destFilter, Top: false }) return null;
        int n = int.Parse(m.Groups["n"].Value);
        var match = pick.Match;
        bool Digi(CardData x) => x.Type == CardType.Digimon && match(x);
        // (카드, 배틀 에어리어의 디지몬, 링크한 디지몬) — 트래시의 카드는 둘 다 null
        List<(CardData Card, CardInstance? Field, CardInstance? LinkedTo)> Pool(EffectContext c, CardInstance host)
        {
            var pool = new List<(CardData, CardInstance?, CardInstance?)>();
            pool.AddRange(c.Owner.Trash.Where(x => Digi(x) && !ReferenceEquals(x, c.Card)).Select(x => (x, (CardInstance?)null, (CardInstance?)null)));
            foreach (var d in c.Owner.BattleArea)
                pool.AddRange(d.LinkedCards.Where(Digi).Select(x => (x, (CardInstance?)null, (CardInstance?)d)));
            pool.AddRange(c.Owner.BattleArea.Where(d => !ReferenceEquals(d, host) && d.TamerProxyOf == null && !GoldfishEngine.IsToken(d.TopCard) && Digi(d.TopCard))
                .Select(d => (d.TopCard, (CardInstance?)d, (CardInstance?)null)));
            return pool;
        }
        List<CardInstance> Hosts(EffectContext c) => c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && destFilter(d)).ToList();
        bool CanPay(EffectContext c) => Hosts(c).Any(h => Pool(c, h).Count >= n);
        string? followName = Regex.Match(card.EffectText ?? "", @"digivolve into \[(?<x>[^\]]+)\] in the hand", I) is { Success: true } fm ? fm.Groups["x"].Value : null;
        return (CanPay,
                c =>
                {
                    var hosts = Hosts(c).Where(h => Pool(c, h).Count >= n).ToList();
                    var host = c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{c.Card.Name}] 진화원 맨 아래에 카드 {n}장을 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                    if (host == null) { c.CostFailed = true; return; }
                    var placed = new List<CardData>();
                    for (int i = 0; i < n; i++)
                    {
                        if (!c.Owner.BattleArea.Contains(host)) break;
                        // 트래시 · 링크 카드 먼저 (필드의 디지몬은 아낀다) — 플레이어는 1장씩 고른다
                        var opts = Pool(c, host).OrderBy(p => p.Field != null ? 2 : p.LinkedTo != null ? 1 : 0).ThenBy(p => p.Card.Level).ToList();
                        if (opts.Count == 0) break;
                        var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, opts.Select(p => p.Card).ToList(), 1, upTo: false,
                            $"[{c.Card.Name}] [{host.TopCard.Name}]의 진화원 맨 아래에 놓을 카드를 고르세요 ({i + 1}/{n} — 배틀 에어리어 · 링크 카드 · 트래시).");
                        if (idx.Count == 0) break;
                        var (x, field, linkedTo) = opts[idx[0]];
                        if (field != null)
                        {
                            if (c.Engine.DetachDigimonAsCost(c.Owner, field) is not { } top) continue;
                            x = top;
                        }
                        else if (linkedTo != null)
                        {
                            if (!linkedTo.LinkedCards.Remove(x)) continue;
                        }
                        else if (!c.Owner.Trash.Remove(x)) continue;
                        host.AddBottomSource(x);
                        placed.Add(x);
                    }
                    if (placed.Count < n) c.CostFailed = true;
                    if (placed.Count > 0) Diag46.Hit("mixedPlaceCost55");
                    c.LastCard = placed.LastOrDefault();
                    c.LastTargets = new List<CardInstance> { host };
                    c.RecalculateFieldBuffs();
                },
                // AI 추천: 낼 수 있고, 뒤의 "digivolve into [X] in the hand"의 카드가 패에 있을 때 (없으면 6장만 잃는다)
                c => CanPay(c) && (followName == null || c.Owner.Hand.Any(x => CardNames.Is(x, followName))),
                $"배틀 에어리어 · 링크 카드 · 트래시의 카드 {n}장을 자신의 디지몬의 진화원 맨 아래에 놓아");
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// 셈 (29차): "each Digimon and Tamer your opponent has in play" (BT9-112) ·
    /// "each [Deva] or [Four Sovereigns] trait card with a different name in your trash" (EX5-072 — 명칭이 서로 다른 장 수) ·
    /// "each [Three Musketeers] Digimon card and Option card with a memory cost of 7 in your trash" (BT6-112 — 둘을 더한다)
    /// </summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter55(string counter)
    {
        if (!Gate55) return null;
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if (Regex.IsMatch(s, @"^Digimon and Tamers? your opponent has (?:in play|on the field)$", I))
            return (_, e, _) => e.BattleArea.Count(d => d.TamerProxyOf == null) + e.Tamers.Count;
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait card with a different name in your trash$", I)).Success)
        {
            var ts = TargetSpec.Names(m.Groups["x"].Value);
            return (o, _, _) => o.Trash.Where(c => ts.Any(t => TargetSpec.HasTrait(c, t))).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\]) Digimon card and Option card with a (?:memory |use )?cost of (?<n>\d+) in your trash$", I)).Success)
        {
            string t = TargetSpec.Names(m.Groups["x"].Value)[0];
            int n = int.Parse(m.Groups["n"].Value);
            return (o, _, _) => o.Trash.Count(c => c.Type == CardType.Digimon && TargetSpec.HasTrait(c, t)) + o.Trash.Count(c => c.Type == CardType.Option && c.PlayCost == n);
        }
        return null;
    }

    // =====================================================================
    // 동작 — 원래 규칙이 읽지 못한 것만 (문이 열려 있을 때)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction55(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate55) return null;
        string s = Regex.Replace(raw.Trim().TrimEnd('.'), @"^you may ", "", I);
        string label = card.Name;
        // EX10-059 "Choose 1 card in your opponent's hand without looking and place it as any of their Digimon's bottom digivolution card or under any of their Tamers"
        //  무작위 1장 → 효과 주인이 놓을 곳을 고른다 (상대의 디지몬 진화원 맨 아래 · 상대 테이머 아래). 놓은 디지몬이 다음 "that Digimon"
        if (Regex.IsMatch(s, @"^place 1 random card from your opponent's hand as any of their Digimon's bottom digivolution card or under any of their Tamers ⟦blind55⟧$", I))
            return (c =>
            {
                if (c.Enemy.Hand.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 패가 없습니다"); return; }
                var dests = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d))
                    .OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).ToList();
                var tamerOf = new Dictionary<CardInstance, CardData>(ReferenceEqualityComparer.Instance);
                foreach (var t in c.Enemy.Tamers)
                {
                    var fake = new CardInstance(t, 0);
                    tamerOf[fake] = t;
                    dests.Add(fake);
                }
                if (dests.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 곳(상대의 디지몬 · 테이머)이 없습니다"); return; }
                var rng = new Random(c.State.TurnSequence * 7919 + c.Enemy.Hand.Count * 31 + c.Enemy.Trash.Count);
                var x = c.Enemy.Hand[rng.Next(c.Enemy.Hand.Count)];
                var dest = c.Engine.ChooseTargets(c.Owner, dests, 1, $"[{c.Card.Name}] 상대의 패에서 무작위로 고른 카드를 놓을 곳을 고르세요 (상대의 디지몬의 진화원 맨 아래 또는 상대의 테이머 아래).", optional: false, preselected: dests[0]).FirstOrDefault() ?? dests[0];
                c.Enemy.Hand.Remove(x);
                if (tamerOf.TryGetValue(dest, out var tamer))
                {
                    c.Enemy.UnderTamers.Add(x);
                    c.LastTargets = new List<CardInstance>();
                    Emit(c, label, $"상대의 패 1장을 상대의 테이머 [{tamer.Name}] 아래에 놓았습니다");
                }
                else
                {
                    dest.AddBottomSource(x);
                    c.LastTargets = new List<CardInstance> { dest };
                    Emit(c, label, $"상대의 패 1장을 [{dest.TopCard.Name}]의 진화원 맨 아래에 놓았습니다");
                }
                c.LastCard = x;
                Diag46.Hit("blindPlace55");
                c.RecalculateFieldBuffs();
            }, null);
        // EX10-059 "delete 1 of their Digimon or Tamers with cards under it" — 진화원이 있는 디지몬 · 아래에 카드가 있는 테이머 (테이머 아래 카드는 지금 모델대로 한 묶음)
        if (Regex.IsMatch(s, @"^delete 1 of your opponent's Digimon or Tamers with cards under it ⟦under55⟧$", I))
            return (c =>
            {
                var pool = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && d.EvolutionSources.Count > 0 && !c.Engine.IsUnaffectedBy(c.Owner, d))
                    .OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).ToList();
                var tamerOf = new Dictionary<CardInstance, CardData>(ReferenceEqualityComparer.Instance);
                if (c.Enemy.UnderTamers.Count > 0)
                    foreach (var t in c.Enemy.Tamers)
                    {
                        var fake = new CardInstance(t, 0);
                        tamerOf[fake] = t;
                        pool.Add(fake);
                    }
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "아래에 카드가 있는 상대의 디지몬 · 테이머가 없습니다"); return; }
                var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 아래에 카드가 있는 상대의 디지몬 또는 테이머 1개를 고르세요.", optional: false, preselected: pool[0]).FirstOrDefault() ?? pool[0];
                string? name = null;
                if (tamerOf.TryGetValue(d, out var tamer)) { if (c.Enemy.Tamers.Contains(tamer)) { c.Engine.DeleteTamer(c.Enemy, tamer); name = tamer.Name; } c.LastTargets = new List<CardInstance>(); }
                else
                {
                    if (c.Enemy.BattleArea.Contains(d) && c.DeleteEnemy(d)) name = d.TopCard.Name;
                    c.LastTargets = new List<CardInstance> { d };
                }
                if (name == null) c.LastStepResolved = false; else Diag46.Hit("deleteWithUnder55");
                Emit(c, label, name != null ? $"[{name}] 소멸" : "소멸시킬 대상이 없습니다", attackerRemoved: ReferenceEquals(d, c.Other) && name != null);
            }, null);
        return null;
    }
}
