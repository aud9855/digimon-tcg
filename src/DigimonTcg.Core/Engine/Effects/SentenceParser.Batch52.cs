using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.52.0 미지원 문장 26차 묶음: 원래 규칙(1~25차)이 읽지 못한 절 · 문장 · 사건 · 비용만 26차 규칙으로 다시 읽는다.
///  - 절 (TryParse52): ≪흡수진화≫로 레스트시킬 디지몬을 상대 디지몬으로 (BT3-056) · 상대 필드를 보는 색 조건 무시 (EX6-072 · BT19-089)
///  - 같은 뜻 다른 표기 (ClauseRules52): 쉼표로 이은 두 사건 (LM-023) · "and"로 이은 두 대상 (BT9-082 · ST20-04) · 비용 표기 (EX5-064 · BT23-089) ·
///    되돌린 디지몬과 같거나 낮은 Lv. (BT14-030)
///  - 사건 (ParseEventPhrase52): 시큐리티가 늘어났을 때 (어느 쪽이든) · 이 디지몬이 ~로 진화할 때 (P-075) · 자신의 디지몬이 ~로 진화할 때 (EX2-056)
///  - 비용 (ParseCost52): "A and B" 두 비용 · 같은 Lv. 진화원 2장 파기 (BT23-089) · 상대의 Lv.3 / 자신의 디지몬을 패로 (BT14-030) ·
///    진화원의 테이머 카드를 시큐리티 아래에 (BT18-102)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch52.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth52;
    [ThreadStatic] private static int _gate52;
    [ThreadStatic] private static int _costDepth52;
    internal static bool Gate52 => _gate52 > 0;

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules52 =
    {
        // LM-023 "When an Option card is used, or when a card is added to a security stack, ~" — 쉼표가 사건을 끊었다 (공식 "옵션 카드를 사용했을 때 또는 시큐리티가 늘어났을 때")
        s => Regex.Replace(s, @"\bWhen an Option card is used, or when a card is added to a security stack, ", "When an Option card is used or a card is added to a security stack, ", RegexOptions.IgnoreCase),
        // BT9-082 "delete 1 of your opponent's level 6 or higher Digimon and all of their level 5 or lower Digimon." — 렌더러만 (엔진은 ParseAction52가 한 동작으로 읽는다: 조건 · "이 효과로 소멸한 디지몬 1마리마다"가 둘 다에)
        s => Regex.Replace(s, @"\b(?<v>[Dd]elete) (?<a>1 of your opponent's [^.]*?Digimon) and all of their (?<b>[^.]*?Digimon)\.", "${v} ${a}. Delete all of your opponent's ${b}."),
        // ST20-04 "For the turn, 1 of your Digimon gains ＜Security A. +1＞ (~), and for every 2 colors your Tamers have, gets +2000 DP."
        s => Regex.Replace(s, @"\bFor the turn, (?<t>1 of your Digimon) gains (?<k>＜[^＞]+＞)(?<ex> \([^)]*\))?, and for every (?<n>\d+) colou?rs your Tamers have, gets \+(?<d>\d+) DP\.",
            "${t} gains ${k} for the turn.${ex} That Digimon gets +${d} DP for the turn for every ${n} colors your Tamers have.", RegexOptions.IgnoreCase),
        // EX5-064 "placing the top card of one of your [Light Fang] or [Night Claw] trait Digimon as that Digimon's bottom digivolution card"
        s => Regex.Replace(s, @"(?<a>\bplacing the top (?:stacked )?card of (?:one|1|any) of your [^,]+?) as that Digimon's bottom digivolution card", "${a} as its bottom digivolution card", RegexOptions.IgnoreCase),
        // BT23-089 "by suspending this Tamer and trash 2 same-level cards from ~" — 동사 꼴
        s => Regex.Replace(s, @"\bby suspending this Tamer and trash (?<n>\d+) same-level cards\b", "by suspending this Tamer and trashing ${n} same-level cards", RegexOptions.IgnoreCase),
        // BT14-030 "return 1 of your opponent's Digimon with an equal or lower level to the hand" — 비용으로 되돌린 디지몬의 Lv. 이하
        s => Regex.Replace(s, @"\breturn 1 of your opponent's Digimon with an equal or lower level to the hand\.", "return 1 of your opponent's Digimon with a level less than or equal to the returned Digimon to the hand.", RegexOptions.IgnoreCase),
    };

    /// <summary>26차 규칙(사건 · 비용 · 절)이 읽을 수 있는 말이 있는 절만 문을 열고 다시 읽는다</summary>
    internal static readonly Regex Hint52 = new(string.Join("|", new[]
    {
        @"would digivolve into", @"added to a security stack", @"ignore this card's color requirements", @"＜Digisorption＞ effect would suspend",
        @"same-level cards", @"Digimon and all of their", @"as your bottom security cards", @"or 1 of your Digimon to the hand", @" and (?:suspending|trashing|placing|deleting|returning) ",
    }), RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string ClauseRewrite52(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules52) s = f(s);
        return s;
    }

    private static string? TryParse52(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse51(rawClause, inherited, card, def);
        if (err == null || _clauseDepth52 > 0) return err;
        _clauseDepth52++;
        _gate52++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (ClauseSpecial52(rawClause, card, def) is { } special) return special.Length == 0 ? null : err;
            if (Hint52.IsMatch(rawClause) && TryParse51(rawClause, inherited, card, null) == null)
            {
                if (def != null) TryParse51(rawClause, inherited, card, def);
                return null;
            }
            string alt = ClauseRewrite52(rawClause);
            if (alt == rawClause || TryParse51(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse51(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth52--;
            _gate52--;
            if (def != null) WrapGate52(def, before);
        }
    }

    /// <summary>26차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate52(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate52++; try { inner(c); } finally { _gate52--; } } };
        }
    }

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 "" (등록함), 해당 없으면 null</summary>
    private static string? ClauseSpecial52(string rawClause, CardData card, CardScriptDefinition? def)
    {
        string t = rawClause.Trim();
        // BT3-056 "[Your Turn] [Once Per Turn] When your ＜Digisorption＞ effect would suspend one of your Digimon, you may suspend your opponent's Digimon with that effect instead."
        //  (공식 "【자신의 턴】 [턴에 1회] 자신의 《흡수진화》로 레스트시킬 디지몬을 상대 디지몬으로 할 수 있다" — 엔진 GoldfishEngine.TryDigisorption)
        if (card.Type == CardType.Digimon
            && Regex.IsMatch(t, @"^\[Your Turn\]\s*\[Once Per Turn\]\s*When your ＜Digisorption＞ effect would suspend one of your Digimon, you may suspend (?:1 of )?your opponent's Digimon with that effect instead\.?$", I))
        {
            if (def != null) def.Script41.DigisorptionOpponent52 = true;
            return "";
        }
        // 상대 필드를 보는 색 조건 무시: "While your opponent has a level 6 or higher Digimon, you may ignore this card's color requirements." (EX6-072) ·
        //  "While your opponent has a white Digimon or Tamer, ~" (BT19-089)
        var ig = Regex.Match(t, @"^While (?<cond>.+?), you (?:can|may) ignore this card's color requirements\.?$", I);
        if (ig.Success && Regex.IsMatch(ig.Groups["cond"].Value, @"\byour opponent\b", I))
        {
            string cond = ig.Groups["cond"].Value.Trim();
            Func<PlayerZones, PlayerZones, bool>? sc = null;
            Match m;
            // "your opponent has a white Digimon or Tamer" — 배틀 에어리어의 디지몬 · 테이머 (육성 에어리어 제외)
            if ((m = Regex.Match(cond, $@"^your opponent has (?:a|an) (?<col>{Colors}) Digimon or Tamer$", I)).Success)
            {
                var col = Enum.Parse<CardColor>(m.Groups["col"].Value, ignoreCase: true);
                sc = (_, e) => e.BattleArea.Any(d => d.TamerProxyOf == null && d.Colors.Contains(col)) || e.Tamers.Any(x => x.AllColors.Contains(col));
            }
            else
                sc = ParseSideCondition(cond) ?? ParseSideCondition33(cond);
            if (sc == null) return null;
            if (def != null) def.Script41.IgnoreColorRequirementVs52 = sc;
            return "";
        }
        return null;
    }

    // =====================================================================
    // 사건
    // =====================================================================

    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase52(string ev)
    {
        if (!Gate52) return null;
        Match m;
        // LM-023 "an Option card is used or a card is added to a security stack" — 어느 쪽이든 (공식 "옵션 카드를 사용했을 때 또는 시큐리티가 늘어났을 때")
        if (Regex.IsMatch(ev, @"^an Option card is used or (?:when )?(?:a card is|cards are) added to a security stack$", I))
            return (new List<EffectTrigger> { EffectTrigger.AllyOptionUsed, EffectTrigger.OpponentOptionUsed, EffectTrigger.AllySecurityAdded, EffectTrigger.OpponentSecurityAdded }, null);
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) added to (?:a|any) security stack$", I))
            return (new List<EffectTrigger> { EffectTrigger.AllySecurityAdded, EffectTrigger.OpponentSecurityAdded }, null);
        // P-075 "this Digimon would digivolve into a card with the [Insectoid] trait" (공식 "이 디지몬이 특징으로 「곤충형」을 가진 카드로 진화할 때") — 진화하기 직전 (TriggerCard = 진화할 카드)
        if ((m = Regex.Match(ev, @"^this Digimon would digivolve into (?<p>.+)$", I)).Success && IntoPick52(m.Groups["p"].Value) is { } into)
            return (new List<EffectTrigger> { EffectTrigger.ThisDigimonWouldDigivolve }, c => c.TriggerCard != null && into(c.TriggerCard) && Diag46.HitTrue("wouldDigivolveInto52"));
        // EX2-056 "one of your Digimon would digivolve into a Digimon with [Gallantmon] or [Growlmon] in its name" (공식 "자신의 디지몬이 ~ 디지몬으로 진화할 때") — Other = 진화할 디지몬
        if ((m = Regex.Match(ev, @"^(?:one|any) of your Digimon would digivolve into (?<p>.+)$", I)).Success && IntoPick52(m.Groups["p"].Value) is { } into2)
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonWouldDigivolve52 }, c => c.TriggerCard != null && into2(c.TriggerCard) && Diag46.HitTrue("allyWouldDigivolveInto52"));
        return null;
    }

    /// <summary>"a card with the [Insectoid] trait" · "a Digimon with [Gallantmon] or [Growlmon] in its name" → 진화할 카드 조건</summary>
    private static Func<CardData, bool>? IntoPick52(string p)
    {
        string one = Regex.Replace(p.Trim(), @"^(?:a|an)\s+", "1 ", I);
        if (Regex.IsMatch(one, @"^1 Digimon\b", I)) one = "1 Digimon card" + one[9..];
        return (ParseCardPick(one) ?? ParseNamePick(one))?.Match;
    }

    // =====================================================================
    // 비용
    // =====================================================================

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost52(string cost, CardData card)
    {
        if (!Gate52) return null;
        Match m;
        string c0 = cost.Trim();
        // BT23-089 "trashing 2 same-level cards from 1 of your [CS] trait Digimon's digivolution cards" (같은 Lv.의 진화원 2장)
        if ((m = Regex.Match(c0, @"^trashing (?<n>\d+) same-level cards from (?<t>(?:1|one) of your .+?)'s digivolution cards$", I)).Success && OwnDigimonFilter33(m.Groups["t"].Value) is { } host)
        {
            int n = int.Parse(m.Groups["n"].Value);
            List<(CardInstance D, int Lv)> Able(EffectContext c) =>
                c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && host(d))
                    .SelectMany(d => c.Engine.TrashableSources61(d, d.SourcesWhere(x => !d.IsFaceDown(x) && x.Level > 0)).GroupBy(x => x.Level) /* v0.61.0 BT9-109 */.Where(g => g.Count() >= n).Select(g => (d, g.Key))).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var pool = Able(c);
                        if (pool.Count == 0) { c.CostFailed = true; return; }
                        var hosts = pool.Select(p => p.D).Distinct().ToList();
                        var d = hosts.Count == 1 ? hosts[0] : c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{c.Card.Name}] 같은 Lv.의 진화원 {n}장을 파기할 디지몬을 고르세요.", optional: false, preselected: hosts[0]).FirstOrDefault() ?? hosts[0];
                        var lvs = pool.Where(p => ReferenceEquals(p.D, d)).Select(p => p.Lv).Distinct().ToList();
                        int lv = lvs.Count == 1 ? lvs[0] : lvs[Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 파기할 진화원의 Lv.을 고르세요.", lvs.Select(x => $"Lv.{x}").ToList(), 0), 0, lvs.Count - 1)];
                        var cands = c.Engine.TrashableSources61(d, d.SourcesWhere(x => !d.IsFaceDown(x) && x.Level == lv)).ToList();   // v0.61.0
                        var picks = cands.Count == n ? cands : c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, cands, n, upTo: false, $"[{c.Card.Name}] 파기할 Lv.{lv} 진화원 {n}장을 고르세요.").Select(i => cands[i]).ToList();
                        if (picks.Count < n) picks = cands.Take(n).ToList();
                        var trashed = c.Engine.TrashChosenSources61(c.Owner, d, picks, cost: true);   // v0.61.0 진화원 파기는 한 곳으로
                        if (trashed.Count < n) { c.CostFailed = true; return; }
                        c.Log($"[{c.Card.Name}] [{d.TopCard.Name}]의 Lv.{lv} 진화원 {trashed.Count}장 파기 ({string.Join(", ", trashed.Select(x => x.Name))})");
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("trashSameLevelSourcesCost52");
                    },
                    _ => true, $"같은 Lv.의 진화원 {n}장을 파기하여");
        }
        // BT14-030 "returning 1 of your opponent's level 3 Digimon or 1 of your Digimon to the hand" — 둘 중 하나 (되돌린 디지몬은 RefCard38)
        if ((m = Regex.Match(c0, @"^returning (?<a>1 of your opponent's (?:.+? )?Digimon) or (?<b>1 of your (?:.+? )?Digimon) to (?:the|its owner's|their owners'|your) hands?$", I)).Success
            && TargetSpec.Parse(m.Groups["a"].Value) is { Opponent: true, AnySide: false } ta && TargetSpec.Parse(m.Groups["b"].Value) is { Opponent: false, AnySide: false, IsThis: false, IsThat: false } tb)
        {
            List<CardInstance> Pool(EffectContext c) =>
                c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && ta.Filters.All(f => f(d)) && ta.ContextFilters.All(f => f(c, d)) && !c.Engine.IsUnaffectedBy(c.Owner, d))
                    .Concat(c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && tb.Filters.All(f => f(d)) && tb.ContextFilters.All(f => f(c, d)))).ToList();
            return (c => Pool(c).Count > 0,
                    c =>
                    {
                        // 추천: 되돌린 뒤 "그 Lv. 이하"로 되돌릴 상대 디지몬이 남는 것 → 상대의 디지몬 → 그 뒤에 되돌릴 상대 디지몬의 Lv.이 높은 것 → 자신의 디지몬은 Lv.이 낮은 것
                        int Next(CardInstance r) => c.Enemy.BattleArea.Where(d => !ReferenceEquals(d, r) && d.TamerProxyOf == null && d.TopCard.Level <= r.TopCard.Level).Select(d => d.TopCard.Level).DefaultIfEmpty(0).Max();
                        var ranked = Pool(c).OrderByDescending(d => Next(d) > 0).ThenByDescending(d => c.Enemy.BattleArea.Contains(d)).ThenByDescending(Next).ThenBy(d => ReferenceEquals(d, c.Self)).ThenBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).ToList();
                        if (ranked.Count == 0) { c.CostFailed = true; return; }
                        var d = c.Engine.ChooseTargets(c.Owner, ranked, 1, $"[{c.Card.Name}] 비용으로 패로 되돌릴 디지몬을 고르세요 (상대의 Lv.3 디지몬 또는 자신의 디지몬).", optional: false, preselected: ranked[0]).FirstOrDefault() ?? ranked[0];
                        var zones = c.Enemy.BattleArea.Contains(d) ? c.Enemy : c.Owner;
                        c.Engine.ReturnToHand(zones, d);
                        if (zones.BattleArea.Contains(d)) { c.CostFailed = true; return; }
                        c.Log($"[{c.Card.Name}] 비용: [{d.TopCard.Name}]을(를) 패로 되돌렸습니다");
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("returnEitherCost52");
                    },
                    _ => true, "디지몬 1마리를 패로 되돌려");
        }
        // BT18-102 "placing up to 5 Tamer cards from this Digimon's digivolution cards as your bottom security cards" (놓은 매수 = PaidCount)
        if ((m = Regex.Match(c0, @"^placing up to (?<n>\d+) Tamer cards from this Digimon's digivolution cards as your bottom security cards$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            List<CardData> Able(EffectContext c) => c.Self == null ? new List<CardData>() : c.Self.SourcesWhere(x => !c.Self.IsFaceDown(x) && x.Type == CardType.Tamer).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var cands = Able(c);
                        var picks = cands.Count <= n && c.Owner != c.Engine.State.Player ? cands
                            : c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, cands, Math.Min(n, cands.Count), upTo: true, $"[{c.Card.Name}] 시큐리티 아래에 놓을 테이머 카드를 고르세요 ({n}장까지).").Select(i => cands[i]).ToList();
                        int placed = 0;
                        foreach (var x in picks)
                        {
                            if (c.Self!.TakeSource(y => ReferenceEquals(y, x)) is not { } took) continue;
                            c.Owner.SecurityStack.Add(took);   // 시큐리티 아래 (리스트 끝 = 맨 아래)
                            c.Engine.NotifySecurityAdded(c.Owner, took);
                            placed++;
                        }
                        c.PaidCount = placed;
                        if (placed == 0) { c.CostFailed = true; return; }
                        c.Log($"[{c.Card.Name}] 진화원의 테이머 카드 {placed}장을 시큐리티 아래에 놓았습니다");
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("tamerSourcesToSecurityCost52");
                    },
                    _ => true, "진화원의 테이머 카드를 시큐리티 아래에 놓아");
        }
        // "A and B" — 두 비용을 차례로 (BT23-089 "suspending this Tamer and trashing ~" · EX5-064 "suspending this Tamer and placing ~")
        if (_costDepth52 == 0)
        {
            foreach (Match am in Regex.Matches(c0, @" and (?=(?:suspending|trashing|placing|deleting|returning) )", I))
            {
                string a = c0[..am.Index], b = c0[(am.Index + am.Length)..];
                _costDepth52++;
                (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ca, cb;
                try { ca = ParseCost(a, card); cb = ca == null ? null : ParseCost(b, card); }
                finally { _costDepth52--; }
                if (ca is not { } x || cb is not { } y) continue;
                return (c => x.CanPay(c) && y.CanPay(c),
                        c =>
                        {
                            x.Pay(c);
                            if (c.CostFailed) return;
                            y.Pay(c);
                            Diag46.Hit("twoCosts52");
                        },
                        c => x.Recommend(c) && y.Recommend(c), $"{x.Describe.TrimEnd(',', ' ')} {y.Describe}");
            }
        }
        return null;
    }

    // =====================================================================
    // 동작 — 원래 규칙이 읽지 못한 것만 (문이 열려 있을 때)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction52(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate52) return null;
        string s = Regex.Replace(raw.Trim().TrimEnd('.'), @"^you may ", "", I);
        Match m;
        // BT9-082 "delete 1 of your opponent's level 6 or higher Digimon and all of their level 5 or lower Digimon" — 한 효과로 함께 소멸 (LastTargets = 둘 다)
        if ((m = Regex.Match(s, @"^delete (?<a>1 of your opponent's .+?Digimon) and all of their (?<b>.+?Digimon)$", I)).Success
            && TargetSpec.Parse(m.Groups["a"].Value) is { Opponent: true } ta && TargetSpec.Parse("all of your opponent's " + m.Groups["b"].Value) is { Opponent: true } tb)
        {
            string label = card.Name;
            return (c =>
            {
                var one = Pick(ta, c, false);
                var rest = tb.Resolve(c, null).Where(d => !one.Contains(d)).ToList();
                var all = one.Concat(rest).ToList();
                var deleted = new List<CardInstance>();
                foreach (var d in all)
                    if (c.Enemy.BattleArea.Contains(d) && c.DeleteEnemy(d)) deleted.Add(d);
                c.LastTargets = all;
                c.LastDidDelete34 = deleted.Count > 0;
                if (all.Count == 0) { c.LastStepResolved = false; Emit(c, label, "소멸시킬 대상이 없습니다"); return; }
                Diag46.Hit("deleteOneAndAll52");
                Emit(c, label, $"[{string.Join(", ", deleted.Select(d => d.TopCard.Name))}] 소멸");
            }, null);
        }
        return null;
    }
}
