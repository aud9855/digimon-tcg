using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.44.0 18차 묶음 — 고르고 나머지를 소멸 · 상대가 하는 동작 · 두 대상 중 하나:
///  - "Choose 1 of each player's Digimon (with the highest play cost) and delete all other Digimon" (EX4-069 · EX11-011 · BT20-102 — 공식 "서로의 디지몬 1마리씩을 선택하고, 선택한 디지몬 이외의 디지몬 전부를 소멸시킨다")
///  - "Your opponent chooses 1 of their Digimon. Delete all Digimon other than this Digimon and your opponent's chosen Digimon" (BT8-019) · "~ Delete all of their other Digimon" (EX3-063)
///  - "Your opponent attacks with the chosen Digimon" (BT13-077 · BT22-060 · BT22-062 · BT18-069 — 공식 "상대는, 선택한 디지몬을 어택시킨다")
///  - "Your opponent may play 1 Tamer card from their hand without paying the cost" (BT17-074 · BT17-075 · BT6-083 — 상대가 정한다)
///  - "Suspend 1 of your opponent's Digimon, or 2 of your opponent's Digimon with 5000 DP or less" (BT1-111 · P-044 — 공식 "~ 1마리 또는, ~ 2마리를 레스트시킨다")
/// </summary>
internal static partial class SentenceParser
{
    internal static readonly Regex ChooseEachDeleteRe44 = new(
        @"^Choose 1 of (?:each player's|both players') Digimon(?<hi> with the highest play cost)?,? and delete all other Digimon$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static readonly Regex HandTrimRe44 = new(
        @"^(?<who>Your opponent trashes|Both players trash|All players trash|Trash) cards (?:in|from) (?:their|your) hands? (?:until|so that) (?:it has|they have|there are) (?<n>\d+)(?: cards)? left$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (Action<EffectContext>? Act, string? Error)? ChooseAndOpponentAction44(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;
        // 서로의 디지몬 1마리씩을 선택하고, 선택한 디지몬 이외의 디지몬 전부를 소멸
        if ((m = ChooseEachDeleteRe44.Match(s)).Success)
        {
            bool hi = m.Groups["hi"].Success;
            return (c =>
            {
                List<CardInstance> Eligible(PlayerZones z)
                {
                    var all = z.BattleArea.ToList();
                    if (!hi || all.Count == 0) return all;
                    int max = all.Max(d => d.PlayCost45);
                    return all.Where(d => d.PlayCost45 == max).ToList();
                }
                var own = Eligible(c.Owner).OrderByDescending(d => ReferenceEquals(d, c.Self)).ThenByDescending(d => d.GetCurrentDP()).ThenByDescending(d => d.EvolutionSources.Count).ToList();
                var opp = Eligible(c.Enemy).OrderBy(d => d.GetCurrentDP()).ThenBy(d => d.TopCard.Level).ToList();
                var keepOwn = c.Engine.ChooseTargets(c.Owner, own, 1, $"[{c.Card.Name}] 남길 자신의 디지몬을 고르세요.", false, null).FirstOrDefault();
                var keepOpp = c.Engine.ChooseTargets(c.Owner, opp, 1, $"[{c.Card.Name}] 남길 상대의 디지몬을 고르세요.", false, null).FirstOrDefault();
                var victims = c.Owner.BattleArea.Concat(c.Enemy.BattleArea).Where(d => !ReferenceEquals(d, keepOwn) && !ReferenceEquals(d, keepOpp)).ToList();
                var names = new List<string>();
                foreach (var v in victims)
                {
                    bool gone = c.Enemy.BattleArea.Contains(v) ? c.DeleteEnemy(v) : c.Owner.BattleArea.Contains(v) && c.Engine.DeleteFrom(c.Owner, v);
                    if (gone) names.Add(v.TopCard.Name);
                }
                c.LastTargets = victims;
                Diag44.Hit("chooseEachDelete");
                Emit(c, label, $"남김: [{keepOwn?.TopCard.Name ?? "-"}] · [{keepOpp?.TopCard.Name ?? "-"}] — " + (names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸" : "소멸시킬 디지몬이 없습니다"));
            }, null);
        }
        // 상대는 본인의 디지몬 1마리를 고른다 (BT8-019 · EX3-063) — 상대가 남길 디지몬을 고른다 (AI: 가장 강한 디지몬)
        if (Regex.IsMatch(s, @"^Your opponent chooses 1 of their Digimon$", I))
            return (c =>
            {
                var ranked = c.Enemy.BattleArea.OrderByDescending(d => d.GetCurrentDP()).ThenByDescending(d => d.TopCard.Level).ToList();
                if (ranked.Count == 0) { c.Chosen34 = null; c.LastStepResolved = false; Emit(c, label, "상대의 디지몬이 없습니다"); return; }
                var pick = c.Engine.ChooseTargets(c.Enemy, ranked, 1, $"[{c.Card.Name}] 상대의 효과 — 자신의 디지몬 1마리를 고르세요.", false, null).FirstOrDefault() ?? ranked[0];
                c.Chosen34 = pick;
                c.LastTargets = new List<CardInstance> { pick };
                Emit(c, label, $"상대가 [{pick.TopCard.Name}]을(를) 골랐습니다");
            }, null);
        // "Delete all Digimon other than this Digimon and your opponent's chosen Digimon" (BT8-019) · "Delete all of their other Digimon" (EX3-063 — 고른 디지몬 이외)
        if ((m = Regex.Match(s, @"^Delete all (?:(?<both>Digimon other than this Digimon and your opponent's chosen Digimon)|(?<their>of their other Digimon))$", I)).Success)
        {
            bool both = m.Groups["both"].Success;
            return (c =>
            {
                var keep = c.Chosen34;
                var victims = (both ? c.Owner.BattleArea.Concat(c.Enemy.BattleArea) : c.Enemy.BattleArea)
                    .Where(d => !ReferenceEquals(d, keep) && !(both && ReferenceEquals(d, c.Self))).ToList();
                var names = new List<string>();
                foreach (var v in victims)
                {
                    bool gone = c.Enemy.BattleArea.Contains(v) ? c.DeleteEnemy(v) : c.Owner.BattleArea.Contains(v) && c.Engine.DeleteFrom(c.Owner, v);
                    if (gone) names.Add(v.TopCard.Name);
                }
                c.LastTargets = victims;
                Diag44.Hit("deleteOthersChosen");
                Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸" : "소멸시킬 디지몬이 없습니다");
            }, null);
        }
        // "Your opponent attacks with the chosen Digimon" — 앞 문장에서 고른 상대의 디지몬 (상대가 대상을 고른다, 어택할 수 없으면 어택하지 않는다)
        if (Regex.IsMatch(s, @"^Your opponent attacks with (?:the chosen Digimon|it|that Digimon)$", I))
            return (c =>
            {
                var d = c.Chosen34 ?? c.LastTargets.FirstOrDefault();
                if (d == null || !c.Enemy.BattleArea.Contains(d)) { c.LastStepResolved = false; Emit(c, label, "어택시킬 디지몬이 없습니다"); return; }
                bool ok = c.Engine.EffectAttack(c.Enemy, d, EffectAttackTarget.Any, withoutSuspending: false, label, forced: true);
                if (ok) Diag44.Hit("opponentAttacksChosen");
                else c.LastStepResolved = false;
                Emit(c, label, ok ? $"상대의 [{d.TopCard.Name}]이(가) 어택합니다" : $"[{d.TopCard.Name}]은(는) 어택할 수 없습니다");
            }, null);
        // "Your opponent may play 1 Tamer card from their hand without paying the cost" — 상대가 정한다 (AI: 등장시킬 수 있으면 등장)
        if (Regex.IsMatch(s, @"^Your opponent may play 1 Tamer card from their hand without paying (?:the|its)(?: memory)? cost$", I))
            return (c =>
            {
                var pool = c.Enemy.Hand.Where(x => x.Type == CardType.Tamer && c.Engine.CanPlayByEffect(c.Enemy, x, PlayOrigin.Hand)).Distinct().OrderByDescending(x => x.PlayCost).ToList();
                if (pool.Count == 0 || !c.Engine.AskYesNo(c.Enemy, null, $"[{c.Card.Name}] 상대의 효과 — 패의 테이머 카드 1장을 코스트 없이 등장시킬까요?", recommended: true))
                { c.LastStepResolved = false; Emit(c, label, "상대는 테이머를 등장시키지 않았습니다"); return; }
                int i = c.Engine.ChooseCardIndexes(c.Enemy, CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] 등장시킬 테이머 카드를 고르세요.").FirstOrDefault();
                var x = pool[Math.Clamp(i, 0, pool.Count - 1)];
                bool played = c.Engine.TryPlayByEffect(c.Enemy, x, PlayOrigin.Hand, () => c.Enemy.Hand.Remove(x));
                c.LastStepResolved = played;
                if (played) Diag44.Hit("opponentPlaysTamer");
                Emit(c, label, played ? $"상대가 테이머 [{x.Name}]을(를) 등장시켰습니다" : "상대는 테이머를 등장시키지 않았습니다");
            }, null);
        // [트래시] "1 of your [Sistermon Ciel]s may digivolve into this card without paying the cost" (BT20-084 · BT15-081 · BT24-080 · P-250 — 트래시의 이 카드로, 진화 조건은 지킨다)
        if ((m = Regex.Match(s, @"^(?<t>1 of your .+?) may digivolve into this card(?: (?<free>without paying the cost)| with the digivolution cost reduced by (?<red>\d+))?$", I)).Success
            && card.Type == CardType.Digimon
            && TargetSpec.Parse(Regex.Replace(m.Groups["t"].Value, @"(?<x>\])s$", "${x}")) is { Opponent: false, AnySide: false, IsThis: false, IsThat: false } evoT)
        {
            bool free = m.Groups["free"].Success;
            int red = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : 0;
            return (c =>
            {
                var zone = c.Owner.Trash.Contains(c.Card) ? c.Owner.Trash : c.Owner.Hand.Contains(c.Card) ? c.Owner.Hand : null;
                var bases = zone == null ? new List<CardInstance>()
                    : c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && evoT.Filters.All(f => f(d)) && evoT.ContextFilters.All(f => f(c, d)) && ActionValidator.ValidateEvolution(d, c.Card).CanExecute)
                        .OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.EvolutionSources.Count).ToList();
                int cost = free ? 0 : Math.Max(0, c.Card.EvoCost - (c.Engine.CanReduceEvoCost(c.Owner) ? red : 0));
                if (bases.Count == 0 || zone == null || (!free && cost > Math.Max(0, c.OwnerMemory) && c.OwnerIsPlayer)
                    || !c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 디지몬을 이 카드로 진화시킬까요?", recommended: true))
                { c.LastStepResolved = false; Emit(c, label, bases.Count == 0 ? "이 카드로 진화할 수 있는 디지몬이 없습니다" : "진화하지 않았습니다"); return; }
                var d = c.Engine.ChooseTargets(c.Owner, bases, 1, $"[{c.Card.Name}](으)로 진화할 디지몬을 고르세요.", optional: false, preselected: bases[0]).FirstOrDefault() ?? bases[0];
                c.Engine.EvolveByEffect(c.Owner, d, c.Card, cost, zone);
                c.LastTargets = new List<CardInstance> { d };
                Diag44.Hit("digivolveIntoThisCard");
                Emit(c, label, $"[{d.TopCard.Name}]이(가) 이 카드로 진화 (코스트 {cost})");
            }, null);
        }
        // [트래시] "2 of your Digimon may DNA digivolve into this card" (BT17-101 — 트래시의 이 카드로 조그레스 진화, 조건 · 코스트는 그 카드의 것)
        if ((m = Regex.Match(s, @"^2 of your Digimon may DNA digivolve into this card(?<free> without paying the cost)?$", I)).Success && card.Type == CardType.Digimon)
        {
            bool free = m.Groups["free"].Success;
            return (c =>
            {
                var zone = c.Owner.Trash.Contains(c.Card) ? c.Owner.Trash : c.Owner.Hand.Contains(c.Card) ? c.Owner.Hand : null;
                var pairs = zone == null ? new List<(CardInstance Left, CardInstance Right)>() : c.Engine.DnaPairs(c.Owner, c.Card).ToList();
                int cost = free ? 0 : GoldfishEngine.GetDnaCost(c.Card) ?? 99;
                if (pairs.Count == 0 || cost > Math.Max(0, c.OwnerMemory) && c.OwnerIsPlayer || cost >= 99
                    || !c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 이 카드로 DNA 진화할까요?", recommended: true))
                { c.LastStepResolved = false; Emit(c, label, pairs.Count == 0 ? "DNA 진화할 디지몬이 없습니다" : "DNA 진화하지 않았습니다"); return; }
                var firsts = pairs.SelectMany(p => new[] { p.Left, p.Right }).Distinct().OrderByDescending(d => d.EvolutionSources.Count).ToList();
                var a = c.Engine.ChooseTargets(c.Owner, firsts, 1, $"[{c.Card.Name}]으로 DNA 진화할 디지몬 1마리를 고르세요.", optional: false, preselected: firsts[0]).FirstOrDefault() ?? firsts[0];
                var partners = pairs.Where(p => ReferenceEquals(p.Left, a) || ReferenceEquals(p.Right, a)).Select(p => ReferenceEquals(p.Left, a) ? p.Right : p.Left).Distinct().ToList();
                var b = partners.Count == 1 ? partners[0] : c.Engine.ChooseTargets(c.Owner, partners, 1, $"[{a.TopCard.Name}]과(와) 함께 DNA 진화할 디지몬을 고르세요.", optional: false, preselected: partners[0]).FirstOrDefault() ?? partners[0];
                var msg = c.Engine.DnaDigivolveCore(c.Owner, c.Card, a, b, cost, zone!);
                var dna = c.Owner.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TopCard, c.Card));
                c.LastTargets = dna != null ? new List<CardInstance> { dna } : new List<CardInstance>();
                c.LastStepResolved = dna != null;
                if (dna != null) Diag44.Hit("dnaIntoThisCard");
                Emit(c, label, msg);
            }, null);
        }
        // 패를 N장이 되도록 파기: "Your opponent trashes cards in their hand until they have 5 left" (BT19-075 — 공식 "상대는 본인의 패가 5장이 되도록 패를 파기한다") ·
        //  "All players trash cards in their hand until they have 5 cards left" (EX2-043) · "Both players trash ~ so that they have 4 left" (BT26-079) · "Trash cards in your hand until it has 4 left" (BT20-077)
        if ((m = HandTrimRe44.Match(s)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant();
            int n = int.Parse(m.Groups["n"].Value);
            bool opp = who.StartsWith("your opponent"), both = who.StartsWith("both") || who.StartsWith("all");
            return (c =>
            {
                int total = 0, mine = 0;
                foreach (var z in both ? new[] { c.Owner, c.Enemy } : opp ? new[] { c.Enemy } : new[] { c.Owner })
                {
                    int k = z.Hand.Count - n;
                    if (k <= 0) continue;
                    // 파기하는 플레이어가 고른다 (AI: 레벨 · 코스트가 낮은 카드부터)
                    var ranked = z.Hand.Distinct().OrderBy(x => x.Type == CardType.Digimon ? x.Level : 0).ThenBy(x => x.PlayCost).ToList();
                    var order = z.Hand.OrderBy(x => ranked.IndexOf(x)).ToList();
                    var picks = c.Engine.ChooseCardIndexes(z, CardChoiceZone.Hand, order, k, upTo: false, $"[{c.Card.Name}] 패가 {n}장이 되도록 파기할 카드 {k}장을 고르세요.")
                        .Distinct().Where(i => i >= 0 && i < order.Count).Select(i => order[i]).ToList();
                    if (picks.Count < k) picks.AddRange(order.Where(x => !picks.Contains(x)).Take(k - picks.Count));
                    foreach (var x in picks)
                        if (z.Hand.Remove(x)) { z.Trash.Add(x); total++; if (ReferenceEquals(z, c.Owner)) mine++; }
                    if (picks.Count > 0) c.Engine.NotifyHandTrashed(z, picks);
                }
                c.PaidCount = both ? total : opp ? total : mine;
                c.LastStepResolved = total > 0;
                if (total > 0) Diag44.Hit("handTrim");
                Emit(c, label, total > 0 ? $"패를 {n}장이 되도록 {total}장 파기했습니다" : "파기할 카드가 없습니다");
            }, null);
        }
        // "Cards this effect suspended can't unsuspend during your opponent's next unsuspend phase" (BT16-046 — 공식 "다음 상대의 액티브 페이즈에서는, 이 효과로 레스트한 카드는 액티브가 되지 않는다") — 디지몬과 테이머
        if (Regex.IsMatch(s, @"^Cards this effect suspended (?:can't|don't) unsuspend during your opponent's next unsuspend phase$", I))
            return (c =>
            {
                bool enemyTurn = c.Engine.IsTurnOf(c.Enemy);
                int until = c.TurnSequence + (enemyTurn ? 2 : 1);
                foreach (var d in c.LastTargets.Where(d => c.Enemy.BattleArea.Contains(d) && d.IsSuspended))
                    d.SkipUnsuspendUntil = Math.Max(d.SkipUnsuspendUntil, until);
                foreach (var t in c.LastTamers35.Where(t => c.Enemy.Tamers.Contains(t)))
                    c.Enemy.TamerCannotUnsuspendUntil[t] = Math.Max(c.Enemy.TamerCannotUnsuspendUntil.GetValueOrDefault(t), until);
                Diag44.Hit("suspendedCardsSkipUnsuspend");
                Emit(c, label, "이 효과로 레스트한 카드는 다음 액티브 페이즈에 액티브가 되지 않는다");
            }, null);
        // "1 of your Digimon may change orientation" (EX13-023 — 액티브 ↔ 레스트, AI는 레스트 상태인 디지몬을 액티브로)
        if (Regex.IsMatch(s, @"^1 of your Digimon may change (?:its )?orientation$", I))
            return (c =>
            {
                var pool = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null).OrderByDescending(d => d.IsSuspended).ThenByDescending(d => d.GetCurrentDP()).ToList();
                bool player = ReferenceEquals(c.Owner, c.State.Player);
                if (pool.Count == 0 || !player && !pool[0].IsSuspended) { c.LastStepResolved = false; return; }
                var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 방향을 바꿀 디지몬을 고르세요 (액티브 ↔ 레스트).", optional: true, preselected: null).FirstOrDefault();
                if (d == null) { c.LastStepResolved = false; return; }
                if (d.IsSuspended) c.Engine.Unsuspend(d); else c.Engine.Suspend(d);
                c.LastTargets = new List<CardInstance> { d };
                Diag44.Hit("changeOrientation");
                Emit(c, label, $"[{d.TopCard.Name}]의 방향을 바꿨습니다 ({(d.IsSuspended ? "레스트" : "액티브")})");
            }, null);
        // 상대의 카드 전부는 다음 액티브 페이즈에 액티브가 되지 않는다 (BT12-106 — 공식 "상대의 카드 전부는 다음 상대의 액티브 페이즈에서는, 액티브가 되지 않는다")
        if (Regex.IsMatch(s, @"^(?:Your opponent's cards|All of your opponent's Digimon and Tamers) (?:don't|can't) unsuspend during their next unsuspend phase$", I))
            return (c =>
            {
                bool enemyTurn = c.Engine.IsTurnOf(c.Enemy);
                int until = c.TurnSequence + (enemyTurn ? 2 : 1);
                foreach (var d in c.Enemy.BattleArea.Where(d => !c.Engine.IsUnaffectedBy(c.Owner, d)))
                    d.SkipUnsuspendUntil = Math.Max(d.SkipUnsuspendUntil, until);
                foreach (var t in c.Enemy.Tamers.Distinct())
                    c.Enemy.TamerCannotUnsuspendUntil[t] = Math.Max(c.Enemy.TamerCannotUnsuspendUntil.GetValueOrDefault(t), until);
                Diag44.Hit("enemyCardsSkipUnsuspend");
                Emit(c, label, "상대의 카드 전부는 다음 액티브 페이즈에 액티브가 되지 않는다");
            }, null);
        // "Suspend 1 of your opponent's Digimon, or 2 of your opponent's Digimon with 5000 DP or less" — 두 대상 중 하나를 고른다
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Delete) (?<a>(?:up to )?\d+ of your opponent's [^,]+?), or (?<b>(?:up to )?\d+ of your opponent's .+)$", I)).Success)
        {
            var ta = TargetSpec.Parse(m.Groups["a"].Value);
            var tb = TargetSpec.Parse(m.Groups["b"].Value);
            var aa = ParseAction($"{m.Groups["v"].Value} {m.Groups["a"].Value}", card, triggers);
            var ab = ParseAction($"{m.Groups["v"].Value} {m.Groups["b"].Value}", card, triggers);
            if (ta == null || tb == null || aa.Error != null || ab.Error != null || aa.Act == null || ab.Act == null) return null;
            bool susp = m.Groups["v"].Value.Equals("Suspend", StringComparison.OrdinalIgnoreCase);
            var (actA, actB) = (aa.Act, ab.Act);
            return (c =>
            {
                int Pool(TargetSpec t) => c.Enemy.BattleArea.Count(d => t.Filters.All(f => f(d)) && t.ContextFilters.All(f => f(c, d)) && (!susp || !d.IsSuspended) && !c.Engine.IsUnaffectedBy(c.Owner, d));
                int na = Math.Min(Pool(ta), Math.Max(1, ta.Count)), nb = Math.Min(Pool(tb), Math.Max(1, tb.Count));
                int rec = nb > na ? 1 : 0;
                int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 어느 쪽을 {(susp ? "레스트시킬" : "소멸시킬")}까요?", new[] { m.Groups["a"].Value, m.Groups["b"].Value }, rec);
                Diag44.Hit("eitherTarget");
                if (pick == 1) actB(c); else actA(c);
            }, null);
        }
        return null;
    }
}
