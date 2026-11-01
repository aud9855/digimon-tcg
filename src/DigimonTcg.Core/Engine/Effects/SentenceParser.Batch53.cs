using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.53.0 미지원 문장 27차 묶음: 원래 규칙(1~26차)이 읽지 못한 절 · 문장 · 사건 · 비용만 27차 규칙으로 다시 읽는다.
///  - 절 (TryParse53): EX5-053 시큐리티가 체크되었을 때 배틀하지 않고 등장 · EX11-062 ≪볼텍스≫로 플레이어에게도 어택
///  - 같은 뜻 다른 표기 (ClauseRules53): BT16-070 선택한 디지몬과 함께 소멸 · BT19-011 DP 합계 소멸 · P-094 등장 코스트 합계 (디지몬과 테이머) ·
///    EX11-062 효과로 레스트했을 때 · BT20-086 "such trait" · BT25-060 링크하는 것으로 · P-187 시큐리티 위 또는 아래에
///  - 사건 (ParseEventPhrase53): 이 디지몬이 다른 디지몬을 소멸시켰을 때 (배틀 · 이 디지몬의 효과 — 공식 Q&amp;A EX1-064) (BT16-061)
///  - 동작 (ParseAction53): 디지몬과 테이머의 등장 코스트 합계 소멸 (P-094) · 색이 서로 다른 상대의 디지몬 1마리씩 소멸 (EX9-074) ·
///    육성 에어리어에 등장 코스트 N으로 등장 (BT16-090)
///  - 비용 (ParseCost53): 육성 에어리어의 디지몬 파기 (BT16-090) · 다른 디지몬/테이머를 시큐리티 위 또는 아래에 (P-187)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch53.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth53;
    [ThreadStatic] private static int _gate53;
    internal static bool Gate53 => _gate53 > 0;

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules53 =
    {
        // BT16-070 "You may choose 1 of your Digimon. Delete it and 1 of your opponent's Digimon with as much or less DP as it."
        s => Regex.Replace(s, @"\bYou may choose 1 of your Digimon\. Delete it and (?<t>1 of your opponent's Digimon) with as much or less DP as it\.",
            "You may choose 1 of your Digimon. Delete that Digimon and ${t} with DP less than or equal to the chosen Digimon.", RegexOptions.IgnoreCase),
        // BT19-011 "Delete any of your opponent's Digimon with DP adding up to 3000. For each of your opponent's Digimon, add 2000 to this DP-Based deletion effect's maximum."
        s => Regex.Replace(s, @"\bDelete any of your opponent's Digimon with DP adding up to (?<n>\d+)\. For (?<e>each|every) (?<cnt>[^,.]+), add (?<k>\d+) to this DP-Based deletion effect's maximum\.",
            "Delete up to ${n} DP total worth of your opponent's Digimon. For ${e} ${cnt}, add ${k} to the maximum you can choose with this effect.", RegexOptions.IgnoreCase),
        // P-094 "Delete your opponent's Digimon and Tamers with a total play cost of 3. For every [Vemmon] in this Digimon's digivolution cards, increase the maximum play cost you can choose by this effect by 1."
        s => Regex.Replace(s, @"\bDelete your opponent's Digimon and Tamers with a total play cost of (?<n>\d+)\. For (?<e>each|every) (?<cnt>[^,.]+), increase the maximum play cost you can choose by this effect by (?<k>\d+)\.",
            "Delete up to ${n} play cost's total worth of your opponent's Digimon and Tamers. For ${e} ${cnt}, add ${k} to the maximum you can choose with this effect.", RegexOptions.IgnoreCase),
        // EX11-062 "When any Digimon suspend, by suspending this Tamer, if effects suspended those Digimon, ~" — 효과로 레스트했을 때 (어택 선언의 레스트는 아니다 — 공식 Q&A BT17-089 · EX4-002)
        s => Regex.Replace(s, @"\bWhen any Digimon suspend, by suspending this Tamer, if effects suspended those Digimon, ", "When any Digimon are suspended by effects, by suspending this Tamer, ", RegexOptions.IgnoreCase),
        s => Regex.Replace(s, @"(?<a>in any of its traits) in any of its traits\b", "${a}", RegexOptions.IgnoreCase),
        // BT20-086 "~ from your hand or trash at the bottom of your Digimon with such trait"
        s => Regex.Replace(s, @"(?<a>\bBy placing 1 (?:[a-z]+ )?Digimon card with the (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait[^,]*? from your hand(?: or trash)?) at the bottom of your Digimon with such trait,",
            "${a} as 1 of your ${x} trait Digimon's bottom digivolution card,", RegexOptions.IgnoreCase),
        // BT25-060 "By linking 1 ~ to this Digimon without paying the cost, 1 of your Digimon may unsuspend." — 링크했다면 (링크하지 않았으면 그 뒤도 없다)
        s => Regex.Replace(s, @"\bBy linking (?<x>1 [^,]+? to this Digimon without paying the cost), (?<b>[^.]+)\.", m =>
            $"You may link {m.Groups["x"].Value}. If you did, {m.Groups["b"].Value}.", RegexOptions.IgnoreCase),
        // BT25-060 "~ this Digimon gains ＜Piercing＞ (~) and ＜Blocker＞ (~), and your opponent's Digimon effects don't affect it." — 같은 기간의 두 문장
        s => Regex.Replace(s, @"\b(?<dur>until your turn ends), this Digimon gains (?<k>＜[^＞]+＞(?: \([^)]*\))? and ＜[^＞]+＞(?: \([^)]*\))?), and your opponent's Digimon effects don't affect it\.",
            "${dur}, this Digimon gains ${k}. Until your turn ends, your opponent's Digimon effects don't affect this Digimon.", RegexOptions.IgnoreCase),
    };

    /// <summary>27차 규칙(사건 · 비용 · 동작)이 읽을 수 있는 말이 있는 절만 문을 열고 다시 읽는다</summary>
    internal static readonly Regex Hint53 = new(string.Join("|", new[]
    {
        @"this Digimon deletes another Digimon", @"in the breeding area", @"as the top or bottom security card", @"1 of each of your opponent's Digimon with different colou?rs",
        @"to an empty space in your breeding area", @"Digimon and Tamers", @"⟦pair53⟧",
    }), RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string ClauseRewrite53(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules53) s = f(s);
        return s;
    }

    /// <summary>엔진만 (한글은 ClauseRules53의 문장으로): BT16-070 고르기와 소멸을 한 동작으로 — 자신의 디지몬과 상대의 디지몬을 함께 보고 고른다 (AI가 이 디지몬 자신을 고르지 않도록)</summary>
    private static string EngineRewrite53(string s) =>
        Regex.Replace(s, @"\bYou may choose 1 of your Digimon\. Delete that Digimon and (?<t>1 of your opponent's Digimon) with DP less than or equal to the chosen Digimon\.",
            "You may delete 1 of your Digimon and ${t} with DP less than or equal to it ⟦pair53⟧.", RegexOptions.IgnoreCase);

    private static string? TryParse53(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse52(rawClause, inherited, card, def);
        if (err == null || _clauseDepth53 > 0) return err;
        _clauseDepth53++;
        _gate53++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (ClauseSpecial53(rawClause, card, def) is { } special) return special.Length == 0 ? null : err;
            string alt = EngineRewrite53(ClauseRewrite53(rawClause));
            if (Hint53.IsMatch(alt) && TryParse52(alt, inherited, card, null) == null)
            {
                if (def != null) TryParse52(alt, inherited, card, def);
                return null;
            }
            if (alt == rawClause || TryParse52(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse52(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth53--;
            _gate53--;
            if (def != null) WrapGate53(def, before, rawClause);
        }
    }

    /// <summary>27차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate53(CardScriptDefinition def, int from, string rawClause)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate53++; try { inner(c); } finally { _gate53--; } }, OnceKey53 = "#53:" + rawClause.Trim() };
        }
    }

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 "" (등록함), 해당 없으면 null</summary>
    private static string? ClauseSpecial53(string rawClause, CardData card, CardScriptDefinition? def)
    {
        string t = rawClause.Trim();
        Match m;
        // EX5-053 "[Opponent's Turn] [Once Per Turn] When your security is checked, if that card is a Digimon card with the [Deva] trait, play it without battling and without paying the cost."
        if (card.Type == CardType.Digimon
            && (m = Regex.Match(t, @"^\[Opponent's Turn\]\s*\[Once Per Turn\]\s*When your security (?:stack )?is checked, if that card is (?<p>a Digimon card .+?), play it without battling and without paying the cost\.?$", I)).Success
            && ParseCardPick(Regex.Replace(m.Groups["p"].Value, @"^an? ", "1 ", I)) is { Count: 1 } pk53)
        {
            if (def != null) def.Script41.SecurityCheckedPlay53 = x => x.Type == CardType.Digimon && pk53.Match(x);
            return "";
        }
        // EX11-062 "[Your Turn] While your opponent has no unsuspended Digimon, your ＜Vortex＞ can also attack players." (테이머) — 엔진 GoldfishEngine.EndOfTurnAttacks
        if (Regex.IsMatch(t, @"^\[Your Turn\]\s*While your opponent has no unsuspended Digimon, your ＜Vortex＞ (?:Digimon )?can also attack (?:players|your opponent)\.?$", I))
        {
            if (def != null) def.Script41.VortexPlayers53 = true;
            return "";
        }
        return null;
    }

    /// <summary>
    /// v0.53.0 "Suspend 1 Digimon" · "Unsuspend 1 of your Digimon" 추천 순서 (AI · 기본 선택): 0 = 상태를 바꿀 수 있고 이로운 쪽 (액티브로 할 자신의 디지몬은 이번 턴 어택할 수 있는 것 먼저) →
    /// 2 = 이미 그 상태 (아무 일도 없다) → 3 = 상태를 바꾸지만 해로운 쪽 (양쪽에서 고르는 효과의 다른 쪽). 전에는 DP 순서라 이미 레스트인 상대 디지몬을 골라 아무 일도 없었다 (BT24-047)
    /// </summary>
    private static int StateChangeRank53(EffectContext c, CardInstance d, bool suspend, bool anySide)
    {
        if (d.IsSuspended == suspend) return 2;
        bool own = c.Owner.BattleArea.Contains(d);
        if (anySide && own == suspend) return 3;   // 레스트는 상대 쪽 · 액티브는 자신 쪽이 이롭다
        if (!suspend && own && c.Engine.IsTurnOf(c.Owner)
            && !((d.TurnPlayed < c.State.CurrentTurn || CardScripts.HasKeyword(d, Keyword.Rush)) && !EffectRules.IsAttackLocked(c.State, d)))
            return 1;   // 액티브로 해도 이번 턴 어택할 수 없다 (등장한 턴 · 어택 불가)
        return 0;
    }

    // =====================================================================
    // 사건
    // =====================================================================

    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase53(string ev)
    {
        if (!Gate53) return null;
        // BT16-061 "this Digimon deletes another Digimon" — 배틀 또는 이 디지몬의 효과로 (공식 Q&A EX1-064: 【등장 시】 효과로 소멸시켜도 "이 디지몬이 소멸시켰을 때")
        if (Regex.IsMatch(ev.Trim(), @"^this Digimon deletes (?:another|other) Digimon$", I))
            return (new List<EffectTrigger> { EffectTrigger.ThisDigimonDeletedAnother53 }, null);
        return null;
    }

    // =====================================================================
    // 비용
    // =====================================================================

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost53(string cost, CardData card)
    {
        if (!Gate53) return null;
        string c0 = cost.Trim();
        // BT16-090 "trashing 1 of your Digimon in the breeding area" (파기 — 소멸이 아니다)
        if (Regex.IsMatch(c0, @"^trashing (?:1 of )?your Digimon in (?:the|your) breeding area$", I))
            return (c => c.Owner.BreedingArea is { } b && b.TopCard.Type == CardType.Digimon,
                    c =>
                    {
                        if (!c.Engine.TrashBreeding53(c.Owner)) { c.CostFailed = true; return; }
                        Diag46.Hit("trashBreedingCost53");
                    },
                    _ => true, "육성 에어리어의 디지몬을 파기하여");
        // P-187 "placing 1 other Digimon or Tamer as the top or bottom security card"
        //  어느 쪽 것이든 (그 카드 주인의 시큐리티 — 종합 룰). 디지몬은 맨 위 카드만 시큐리티로, 나머지는 트래시
        if (Regex.IsMatch(c0, @"^placing 1 other Digimon or Tamer as the top or bottom security card$", I))
        {
            List<(PlayerZones Z, CardInstance? D, CardData? T)> Pool(EffectContext c)
            {
                var list = new List<(PlayerZones, CardInstance?, CardData?)>();
                foreach (var z in new[] { c.Enemy, c.Owner })
                {
                    list.AddRange(z.BattleArea.Where(d => d.TamerProxyOf == null && !ReferenceEquals(d, c.Self) && (z == c.Owner || !c.Engine.IsUnaffectedBy(c.Owner, d))).Select(d => (z, (CardInstance?)d, (CardData?)null)));
                    list.AddRange(z.Tamers.Distinct().Select(t => (z, (CardInstance?)null, (CardData?)t)));
                }
                return list;
            }
            return (c => Pool(c).Count > 0,
                    c =>
                    {
                        // 추천: 상대의 디지몬(DP 높은 것)을 상대 시큐리티 아래 → 상대 테이머 → 자신의 테이머 → 자신의 디지몬(낮은 것)
                        var pool = Pool(c).OrderByDescending(p => p.Z == c.Enemy).ThenByDescending(p => p.D != null && p.Z == c.Enemy).ThenBy(p => p.D != null && p.Z == c.Owner)
                            .ThenByDescending(p => p.D?.GetCurrentDP() ?? 0).ThenBy(p => p.D?.GetCurrentDP() ?? 0).ToList();
                        if (pool.Count == 0) { c.CostFailed = true; return; }
                        var labels = pool.Select(p => $"{(p.Z == c.Owner ? "자신" : "상대")}의 {(p.D != null ? p.D.TopCard.Name : p.T!.Name + " (테이머)")}").ToList();
                        int i = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 시큐리티에 놓을 다른 디지몬/테이머를 고르세요.", labels, 0), 0, pool.Count - 1);
                        var pick = pool[i];
                        bool top = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 시큐리티 위 또는 아래?", new List<string> { "아래", "위" }, pick.Z == c.Enemy ? 0 : 1) == 1;
                        if (!c.Engine.PlaceAsSecurity53(pick.Z, pick.D, pick.T, top)) { c.CostFailed = true; return; }
                        c.Log($"[{c.Card.Name}] 비용: {labels[i]}을(를) 시큐리티 {(top ? "위" : "아래")}에 놓았습니다");
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("placeAsSecurityCost53");
                    },
                    _ => true, "다른 디지몬/테이머를 시큐리티 위 또는 아래에 놓아");
        }
        return null;
    }

    // =====================================================================
    // 동작 — 원래 규칙이 읽지 못한 것만 (문이 열려 있을 때)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction53(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate53) return null;
        string s = Regex.Replace(raw.Trim().TrimEnd('.'), @"^you may ", "", I);
        string label = card.Name;
        Match m;
        // BT16-070 "You may choose 1 of your Digimon. Delete it and 1 of your opponent's Digimon with as much or less DP as it." — 자신의 디지몬을 고르고(임의), 그 DP 이하의 상대 디지몬 1마리와 함께 소멸
        //  추천: 상대 디지몬의 가치 - 자신의 디지몬의 가치가 가장 큰 짝 (이득이 없으면 고르지 않는다)
        if ((m = Regex.Match(s, @"^delete 1 of your Digimon and (?<t>1 of your opponent's Digimon) with DP less than or equal to it ⟦pair53⟧$", I)).Success)
        {
            static int Value(CardInstance d) => d.TopCard.Level * 2000 + d.GetCurrentDP() + d.EvolutionSources.Count * 500;
            return (c =>
            {
                List<CardInstance> Targets(CardInstance own) => c.Enemy.BattleArea.Where(e => e.TamerProxyOf == null && e.GetCurrentDP() <= own.GetCurrentDP() && !c.Engine.IsUnaffectedBy(c.Owner, e)).ToList();
                var owns = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null).ToList();
                var ranked = owns.Select(o => (o, gain: Targets(o).Select(Value).DefaultIfEmpty(-1_000_000).Max() - Value(o) - (ReferenceEquals(o, c.Self) ? 3000 : 0)))
                    .OrderByDescending(x => x.gain).Select(x => x.o).ToList();
                var best = ranked.FirstOrDefault();
                bool recommend = best != null && Targets(best).Select(Value).DefaultIfEmpty(-1_000_000).Max() - Value(best) - (ReferenceEquals(best, c.Self) ? 3000 : 0) > 0;
                var own = ranked.Count == 0 ? null : c.Engine.ChooseTargets(c.Owner, ranked, 1, $"[{c.Card.Name}] 자신의 디지몬 1마리를 선택할 수 있습니다 (그 디지몬의 DP 이하인 상대의 디지몬 1마리와 함께 소멸, 고르지 않으려면 취소).", optional: true, preselected: recommend ? best : null).FirstOrDefault();
                if (own != null && !recommend && (c.Owner != c.Engine.State.Player || c.Engine.PlayerChoices == null)) own = null;   // AI · 묻지 않을 때: 이득이 없으면 고르지 않는다
                if (own == null) { c.LastStepResolved = false; Emit(c, label, "자신의 디지몬을 선택하지 않았습니다"); return; }
                c.Chosen34 = own;
                var tgts = Targets(own).OrderByDescending(Value).ToList();
                var victim = tgts.Count == 0 ? null : c.Engine.ChooseTargets(c.Owner, tgts, 1, $"[{c.Card.Name}] [{own.TopCard.Name}]의 DP 이하인 상대의 디지몬 1마리를 고르세요.", optional: false, preselected: tgts[0]).FirstOrDefault();
                var names = new List<string>();
                if (c.Owner.BattleArea.Contains(own) && c.Engine.DeleteFrom(c.Owner, own)) names.Add(own.TopCard.Name);
                if (victim != null && c.Enemy.BattleArea.Contains(victim) && c.DeleteEnemy(victim)) names.Add(victim.TopCard.Name);
                c.LastTargets = victim != null ? new List<CardInstance> { victim } : new List<CardInstance>();
                if (names.Count == 0) c.LastStepResolved = false; else Diag46.Hit("deleteChosenPair53");
                Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸" : "소멸시킬 대상이 없습니다", attackerRemoved: victim != null && ReferenceEquals(victim, c.Other) && names.Contains(victim.TopCard.Name));
            }, null);
        }
        // P-094 "Delete up to 3 play cost's total worth of your opponent's Digimon and Tamers" — 합계 상한 안에서 디지몬과 테이머를 고른다 (LastTargets = 고른 디지몬)
        if ((m = Regex.Match(s, @"^delete up to (?<n>\d+) play cost's total worth of your opponent's Digimon and(?:/or)? Tamers$", I)).Success)
        {
            int n0 = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int budget = n0;
                var tamerOf = new Dictionary<CardInstance, CardData>(ReferenceEqualityComparer.Instance);
                var all = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
                foreach (var t in c.Enemy.Tamers.Distinct())
                {
                    var fake = new CardInstance(t, 0);
                    tamerOf[fake] = t;
                    all.Add(fake);
                }
                var picked = new List<CardInstance>();
                while (true)
                {
                    var pool = all.Where(d => !picked.Contains(d) && d.PlayCost45 <= budget)
                        .OrderByDescending(d => d.PlayCost45).ThenByDescending(d => tamerOf.ContainsKey(d) ? 0 : 1).ThenByDescending(d => d.GetCurrentDP()).ToList();
                    if (pool.Count == 0) break;
                    var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 등장 코스트 합계 {budget} 이하가 되도록 상대의 디지몬/테이머를 고르세요 (남은 {budget}, 그만 고르려면 취소).", optional: true, preselected: null).FirstOrDefault();
                    if (d == null) break;
                    picked.Add(d);
                    budget -= Math.Max(0, d.PlayCost45);
                }
                var names = new List<string>();
                foreach (var d in picked)
                {
                    if (tamerOf.TryGetValue(d, out var t)) { if (c.Enemy.Tamers.Contains(t)) { c.Engine.DeleteTamer(c.Enemy, t); names.Add(t.Name); } continue; }
                    if (c.Enemy.BattleArea.Contains(d) && c.DeleteEnemy(d)) names.Add(d.TopCard.Name);
                }
                c.LastTargets = picked.Where(d => !tamerOf.ContainsKey(d)).ToList();
                c.LastTamers35 = picked.Where(tamerOf.ContainsKey).Select(d => tamerOf[d]).ToList();
                if (picked.Count == 0) c.LastStepResolved = false; else Diag46.Hit("deleteCostTotalWithTamers53");
                Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸 (등장 코스트 합계)" : "고를 디지몬/테이머가 없습니다");
            }, null);
        }
        // EX9-074 "delete 1 of each of your opponent's Digimon with different colors"
        //  고른 디지몬끼리 같은 색이 없도록 1마리씩 (여러 색 디지몬은 그 색 전부를 쓴다)
        if (Regex.IsMatch(s, @"^delete 1 of each of your opponent's Digimon with different colou?rs$", I))
        {
            return (c =>
            {
                var picked = new List<CardInstance>();
                var used = new HashSet<CardColor>();
                while (true)
                {
                    var pool = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !picked.Contains(d) && !c.Engine.IsUnaffectedBy(c.Owner, d) && !d.Colors.Any(used.Contains))
                        .OrderBy(d => d.Colors.Distinct().Count()).ThenByDescending(d => d.GetCurrentDP()).ThenByDescending(d => d.TopCard.Level).ToList();
                    if (pool.Count == 0) break;
                    var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 색이 서로 다른 상대의 디지몬을 1마리씩 고르세요 (그만 고르려면 취소).", optional: true, preselected: null).FirstOrDefault();
                    if (d == null) break;
                    picked.Add(d);
                    foreach (var col in d.Colors) used.Add(col);
                }
                var names = new List<string>();
                foreach (var d in picked)
                    if (c.Enemy.BattleArea.Contains(d) && c.DeleteEnemy(d)) names.Add(d.TopCard.Name);
                c.LastTargets = picked;
                if (picked.Count == 0) c.LastStepResolved = false; else Diag46.Hit("deleteEachColor53");
                Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸 (색이 서로 다른 디지몬)" : "소멸시킬 대상이 없습니다");
            }, null);
        }
        // BT16-090 "play 1 [BigUkkomon] from your hand to an empty space in your breeding area for a play cost of 3"
        if ((m = Regex.Match(s, @"^play (?<p>1 .+?) from your hand to (?:an empty space in )?your (?:empty )?breeding area for a play cost of (?<n>\d+)$", I)).Success
            && (ParseNamePick(m.Groups["p"].Value) ?? ParseCardPick(m.Groups["p"].Value)) is { Count: 1 } pk)
        {
            int cost = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                if (c.Owner.BreedingArea != null) { c.LastStepResolved = false; Emit(c, label, "육성 에어리어가 비어 있지 않습니다"); return; }
                var cands = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && pk.Match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand)).Distinct().ToList();
                if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 카드가 없습니다"); return; }
                var x = cands.Count == 1 ? cands[0] : cands[Math.Clamp(c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, cands, 1, upTo: false, $"[{c.Card.Name}] 육성 에어리어에 등장시킬 카드를 고르세요.").FirstOrDefault(), 0, cands.Count - 1)];
                if (!c.Engine.PlayToBreedingPaying53(c.Owner, x, cost)) { c.LastStepResolved = false; Emit(c, label, "등장시키지 못했습니다"); return; }
                Diag46.Hit("playToBreedingPaying53");
                Emit(c, label, $"[{x.Name}]을(를) 육성 에어리어에 등장 코스트 {cost}로 등장");
            }, null);
        }
        return null;
    }
}
