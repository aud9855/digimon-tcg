using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.35.0 미지원 문장 9차 묶음: 뒷면 진화원 (Ver.N · DM 계열 · ≪트레이닝≫) · 테이머 아래 뒷면 카드 (DATA SQUAD · Glowing Dawn).
/// 종합 룰 4-7-4 (아래 카드는 지정이 없으면 앞면) · 4-7-9 (뒷면 카드는 정보가 없다) · 4-7-10 (주인은 볼 수 있다) · 16-41 ≪트레이닝≫ · 17-1-3-2-4 (에어리어의 뒷면 카드는 룰 처리로 파기).
/// 엔진: GoldfishEngine.FaceDown.cs · CardInstance.FaceDownSources · PlayerZones.FaceDownUnderTamers
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>"of its face-down digivolution cards" · "of this Digimon's face-down digivolution cards" · "2 of ~" (2장마다)</summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter35(string counter)
    {
        Match m;
        string c0 = counter.Trim().TrimEnd('.');
        if ((m = Regex.Match(c0, @"^(?:(?<n>\d+) )?(?:of )?(?:its|their|this Digimon's|that Digimon's) face[- ]down digivolution cards$", I)).Success
            || (m = Regex.Match(c0, @"^(?:(?<n>\d+) )?face[- ]down digivolution cards? (?:this Digimon has|under this Digimon)$", I)).Success)
        {
            int per = m.Groups["n"].Success ? Math.Max(1, int.Parse(m.Groups["n"].Value)) : 1;
            return (_, _, self) => (self?.FaceDownSourceCount ?? 0) / per;
        }
        // "face-down card under your Tamers" — 테이머 아래 뒷면 카드
        if (Regex.IsMatch(c0, @"^(?:of the )?face[- ]down cards? (?:from )?under (?:any of )?your Tamers$", I))
            return (o, _, _) => o.FaceDownUnderTamerCount;
        return null;
    }

    /// <summary>뒷면 진화원 · 테이머 아래 뒷면 카드 비용 + "A or B" 둘 중 하나를 내는 비용</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost35(string cost, CardData card)
    {
        Match m;
        cost = cost.Trim();
        // "trashing this Digimon's bottom face-down digivolution card" · "trashing its bottom face-down digivolution card" · "trashing 2 of this Digimon's bottom face-down digivolution cards"
        if ((m = Regex.Match(cost, @"^trashing (?:(?<n>\d+) of )?(?:this Digimon's|its) bottom (?:(?<n>\d+) )?face[- ]down digivolution cards?$", I)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            bool Has(EffectContext c) => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && c.Self.FaceDownSourceCount >= n;
            return (Has,
                    c => { if (!Has(c) || c.Engine.TrashBottomFaceDownSources(c.Owner, c.Self!, n).Count < n) c.CostFailed = true; else c.RecalculateFieldBuffs(); },
                    _ => true, $"이 디지몬의 뒷면 진화원을 아래에서부터 {n}장 파기하여");
        }
        // "trashing any of your Digimon's bottom face-down digivolution card" (BT26-048) · "trashing 1 of your [DM] trait Digimon's bottom 2 face-down digivolution cards" (EX9-071 — 그 디지몬이 다음 문장의 "it")
        if ((m = Regex.Match(cost, @"^trashing (?:any|1|one) of your (?<spec>.*?)Digimon's bottom (?:(?<n>\d+) )?face[- ]down digivolution cards?$", I)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            string spec = m.Groups["spec"].Value.Trim();
            var filter = spec.Length == 0 ? (_ => true) : OwnDigimonFilter("1 of your " + spec + " Digimon");
            if (filter == null) return null;
            List<CardInstance> Hosts(EffectContext c) => c.Owner.BattleArea.Where(d => d.FaceDownSourceCount >= n && filter(d)).OrderByDescending(d => d.FaceDownSourceCount).ToList();
            return (c => Hosts(c).Count > 0,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Hosts(c), 1, $"[{c.Card.Name}] 뒷면 진화원을 파기할 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null || c.Engine.TrashBottomFaceDownSources(c.Owner, d, n).Count < n) { c.CostFailed = true; return; }
                        c.LastTargets = new List<CardInstance> { d };
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"자신의 디지몬의 뒷면 진화원을 아래에서부터 {n}장 파기하여");
        }
        // "trashing its bottom 2 face-down or [Cyborg] trait digivolution cards" (EX9-073) — 아래에서부터, 뒷면이거나 조건에 맞는 진화원
        if ((m = Regex.Match(cost, @"^trashing (?:its|this Digimon's) bottom (?<n>\d+) face[- ]down or (?<pick>.+?) digivolution cards$", I)).Success
            && (ParseCardPick("1 " + m.Groups["pick"].Value + " card") ?? ParseNamePick("1 " + m.Groups["pick"].Value)) is { } orPick)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var match = orPick.Match;
            List<CardData> Able(EffectContext c) => c.Self == null || !c.Owner.BattleArea.Contains(c.Self) ? new List<CardData>()
                : c.Self.GetEvolutionStackBottomUp().Where(x => c.Self.IsFaceDown(x) || match(x)).ToList();
            return (c => Able(c).Count >= n,
                    c =>
                    {
                        var picks = Able(c).Take(n).ToList();
                        if (picks.Count < n) { c.CostFailed = true; return; }
                        foreach (var x in picks)
                            if (c.Self!.TakeSource(y => ReferenceEquals(y, x)) != null) c.Owner.Trash.Add(x);
                        c.Log($"[{c.Card.Name}] 진화원 {n}장을 아래에서부터 파기했습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"이 디지몬의 아래 진화원 {n}장을 파기하여");
        }
        // "placing it face down under any of your [Keenan Crier]s" — 앞 비용("trashing 1 card in your hand or ~")의 "it" = 패의 카드 1장 (BT26-072)
        // "A or B" — 둘 중 하나를 낸다 (뒤쪽에 동사가 없으면 앞 동사를 붙인다): "trashing the bottom face-down card from under any of your Tamers or your top security card" ·
        // "adding your top security card to the hand or trashing the bottom face-down card under any of your Tamers" · "deleting this Digimon or trashing 2 bottom face-down cards ~"
        foreach (Match om in Regex.Matches(cost, @" or (?=(?:[a-z]+ing |your |the |this |its |\d+ ))", I))
        {
            string a = cost[..om.Index].Trim(), b = cost[(om.Index + om.Length)..].Trim();
            if (!Regex.IsMatch(b, @"^[a-z]+ing ", I))
            {
                var verb = Regex.Match(a, @"^[a-z]+ing ", I);
                if (!verb.Success) continue;
                b = verb.Value + b;
            }
            if (!Regex.IsMatch(a, @"^[a-z]+ing ", I)) continue;
            if (ParseCost(a, card) is not { } ca || ParseCost(b, card) is not { } cb) continue;
            return (c => ca.CanPay(c) || cb.CanPay(c),
                    c =>
                    {
                        bool canA = ca.CanPay(c), canB = cb.CanPay(c);
                        bool useA = canA && (!canB || c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 비용: {ca.Describe.TrimEnd('여', '서', ',')} (예) / {cb.Describe.TrimEnd('여', '서', ',')} (아니요)", recommended: ca.Recommend(c) || !cb.Recommend(c)));
                        if (useA) ca.Pay(c); else if (canB) cb.Pay(c); else c.CostFailed = true;
                    },
                    c => ca.Recommend(c) || cb.Recommend(c), $"{ca.Describe} 또는 {cb.Describe}");
        }
        return null;
    }

    /// <summary>뒷면 진화원 시점: "When face-down cards are placed in (this Digimon's / any of your Digimon's) digivolution cards" · "When effects trash face-down digivolution cards from your Digimon"</summary>
    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase35(string ev)
    {
        Match m;
        string e0 = ev.Trim();
        // 놓였을 때 (효과로 — 엔진의 진화원 비교: GoldfishEngine.SourceEvents)
        if ((m = Regex.Match(e0, @"^face[- ]down cards are placed (?:in|as|under) (?:(?<self>this Digimon)'s digivolution cards|(?:any of |one of )?your (?<spec>.*?)Digimon(?:'s)? digivolution cards|(?<self>this Digimon))$", I)).Success)
        {
            bool self = m.Groups["self"].Success;
            string spec = m.Groups["spec"].Value.Trim();
            var filter = self || spec.Length == 0 ? null : OwnDigimonFilter("1 of your " + spec + " Digimon");
            if (!self && spec.Length > 0 && filter == null) return null;
            return (new List<EffectTrigger> { EffectTrigger.AllySourcesAdded }, c => c.Other != null
                && (self ? c.Self != null && ReferenceEquals(c.Other, c.Self) : filter == null || filter(c.Other))
                && c.Engine.CurrentSourceCards.Any(c.Other.IsFaceDown));
        }
        // 파기되었을 때: "When effects trash face-down digivolution cards from your Digimon" (BT26-048) · "~ from this Digimon"
        if ((m = Regex.Match(e0, @"^(?:an effect trashes|effects trash) face[- ]down digivolution cards? (?:from|of) (?:(?<self>this Digimon)|(?:any of |one of )?your (?<spec>.*?)Digimon)$", I)).Success)
        {
            bool self = m.Groups["self"].Success;
            return (new List<EffectTrigger> { EffectTrigger.AllySourcesTrashed }, c => c.Other != null
                && (!self || c.Self != null && ReferenceEquals(c.Other, c.Self))
                && c.Engine.CurrentSourceCards.Any(c.Other.IsFaceDown));
        }
        return null;
    }

    /// <summary>"this Digimon has face-down digivolution cards" · "you have as many or fewer security cards as this Digimon has face-down digivolution cards"</summary>
    private static Func<EffectContext, bool>? ParseEventCondition35(string cond)
    {
        Match m;
        string c0 = cond.Trim();
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?:(?<n>\d+) or more )?face[- ]down digivolution cards$", I)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            return c => c.Self != null && c.Self.FaceDownSourceCount >= n;
        }
        if (Regex.IsMatch(c0, @"^(?:this Digimon|it) has no face[- ]down digivolution cards$", I))
            return c => c.Self == null || c.Self.FaceDownSourceCount == 0;
        if ((m = Regex.Match(c0, @"^you have as many or (?<dir>fewer|more) security cards as this Digimon has face[- ]down digivolution cards$", I)).Success)
        {
            bool fewer = m.Groups["dir"].Value.Equals("fewer", StringComparison.OrdinalIgnoreCase);
            return c => c.Self != null && (fewer ? c.Owner.SecurityStack.Count <= c.Self.FaceDownSourceCount : c.Owner.SecurityStack.Count >= c.Self.FaceDownSourceCount);
        }
        // "you have face-down cards under your Tamers" · "there are face-down cards under your Tamers"
        if (Regex.IsMatch(c0, @"^(?:you have|there are) face[- ]down cards under (?:any of )?your Tamers$", I))
            return c => c.Owner.FaceDownUnderTamerCount > 0;
        return null;
    }

    /// <summary>절 정리 (Normalize 끝): "After, ~" = "Then, ~" (그 후) · "It further gets -2000 DP for each ~" = 앞 문장의 기간을 이어받는다 (EX9-030)</summary>
    internal static string Normalize35(string s)
    {
        s = Regex.Replace(s, @"(?<=^|[.＞\]]\s)After, ", "Then, ");
        // 진화 코스트 감소의 "for each of its face-down digivolution cards" — 진화하는 그 디지몬 (공식 BT22-038: "그 디지몬의 뒷면의 진화원 1장마다")
        s = Regex.Replace(s, @"(?<=would digivolve into this card, for each of )(?:its|their)(?= face[- ]down digivolution cards)", "that Digimon's", I);
        s = Regex.Replace(s, @"(?<pre>gets? [+-]\d+ DP (?<dur>until [^.,]+?|for the turn))\. (?:It|That Digimon) further gets (?<v>[+-]\d+) DP for (?<e>each|every) (?<cnt>[^.]+)\.",
            "DOLLAR{pre}. It gets DOLLAR{v} DP DOLLAR{dur} for DOLLAR{e} DOLLAR{cnt}.", I);
        return s;
    }

    /// <summary>
    /// 문장 앞 정리 (ParseStep): 조건이 붙은 주어 "This Digimon with the [Glowing Dawn] trait unsuspends" · "This [Glowing Dawn] trait Digimon unsuspends"
    ///  → "If this Digimon has the [Glowing Dawn] trait, this Digimon unsuspends" (ST23-04 · ST23-08 · BT25-041 진화원 효과)
    /// </summary>
    internal static string NormalizeStep35(string s)
    {
        var m = Regex.Match(s, $@"^This (?:Digimon with the (?<x>{TargetSpec.NameList}) traits?|(?<x>{TargetSpec.NameList}) traits? Digimon) (?<rest>(?:unsuspends|gets [+-]\d+ DP|gains ＜).*)$", I);
        if (m.Success) return $"If this Digimon has the {m.Groups["x"].Value} trait, this Digimon {m.Groups["rest"].Value}";
        return s;
    }

    /// <summary>뒷면 진화원 · 테이머 아래 뒷면 동작</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction35(string raw, CardData card, List<EffectTrigger> triggers)
    {
        string s = raw.Trim().TrimEnd('.');
        string label = triggers.Count > 0 && Labels.TryGetValue(triggers[0], out var l) ? l : "효과";
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;

        // "Place the top card of your deck face down under any of your [DATA SQUAD] trait Tamers" (ST24-03 · ST24-09) · "Place your deck's top card face down under this Tamer" (EX13-071)
        if ((m = Regex.Match(s, @"^Place (?:the top card of your deck|your deck's top card)(?<fd> face[- ]down)? (?<dest>under .+)$", I)).Success
            && ParsePlaceDest33(m.Groups["dest"].Value) is { Tamer: { } tamerOk })
        {
            bool fd = m.Groups["fd"].Success;
            bool thisTamer = Regex.IsMatch(m.Groups["dest"].Value, @"^under this Tamer$", I);
            if (thisTamer && card.Type != CardType.Tamer) return null;
            return (c =>
            {
                bool ok = thisTamer ? c.Owner.Tamers.Contains(c.Card) : c.Owner.Tamers.Any(tamerOk);
                if (!ok || c.Owner.MainDeck.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 테이머 · 카드가 없습니다"); return; }
                var top = c.Owner.MainDeck[0];
                c.Owner.MainDeck.RemoveAt(0);
                if (fd) c.Engine.PlaceFaceDownUnderTamer(c.Owner, top); else c.Owner.UnderTamers.Add(top);
                Emit(c, label, fd ? "덱 위 1장을 테이머 아래에 뒷면으로 놓는다" : "덱 위 1장을 테이머 아래에 놓는다");
            }, null);
        }
        // "From 1 of your opponent's Digimon, trash any 1 digivolution card for each of this Digimon's face-down digivolution cards" (EX9-018) — 한 마리에서 셈만큼
        if ((m = Regex.Match(s, @"^From (?<t>(?:1|one) of your opponent's (?:[^,]*?)Digimon), trash (?:any )?(?<n>\d+) digivolution cards? for (?:each|every) (?<cnt>.+)$", I)).Success
            && TargetSpec.Parse(m.Groups["t"].Value) is { Opponent: true } fromT && ParseCounter(m.Groups["cnt"].Value) is { } fromCnt)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int k = Math.Min(n * fromCnt(c.Owner, c.Enemy, c.Self), 40);
                var d = Pick(fromT, c, security).FirstOrDefault();
                if (d == null || k <= 0 || d.EvolutionSources.Count == 0) { Emit(c, label, "진화원을 파기할 대상이 없습니다"); c.LastStepResolved = false; return; }
                var zone = c.Engine.OwnerOf(d);
                var pool = d.EvolutionSources.OrderByDescending(x => !d.IsFaceDown(x) && (x.InheritedEffectText.Trim().Length > 0 || x.InheritedDpBonus > 0)).ToList();
                int take = Math.Min(k, pool.Count);
                var picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, pool, take, upTo: false, $"[{c.Card.Name}] [{d.TopCard.Name}]의 진화원에서 파기할 카드 {take}장을 고르세요.");
                foreach (var x in picks)
                    if (d.TakeSource(y => ReferenceEquals(y, x)) != null) zone.Trash.Add(x);
                c.RecalculateFieldBuffs();
                Emit(c, label, $"[{d.TopCard.Name}]의 진화원 {picks.Count}장 파기");
            }, null);
        }
        // "To 1 of your opponent's Digimon, ＜De-Digivolve 1＞ for each of this Digimon's face-down digivolution cards" (EX9-043)
        if ((m = Regex.Match(s, @"^To (?<t>(?:1|one) of your opponent's (?:[^,]*?)Digimon), ＜De-Digivolve (?<n>\d+)＞ for (?:each|every) (?<cnt>.+)$", I)).Success
            && TargetSpec.Parse(m.Groups["t"].Value) is { Opponent: true } toT && ParseCounter(m.Groups["cnt"].Value) is { } toCnt)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int k = Math.Min(n * toCnt(c.Owner, c.Enemy, c.Self), 40);
                if (k <= 0) { Emit(c, label, "≪퇴화≫할 수가 0입니다"); c.LastStepResolved = false; return; }
                var d = Pick(toT, c, security).FirstOrDefault();
                if (d == null) { Emit(c, label, "≪퇴화≫할 대상이 없습니다"); c.LastStepResolved = false; return; }
                string before = d.TopCard.Name;
                c.DeDigivolveEnemy(d, k);
                Emit(c, label, $"≪퇴화 {k}≫ [{before}] → [{d.TopCard.Name}]");
            }, null);
        }
        // 여러 대상: "Until your opponent's turn ends, 1 of their Digimon and 1 of their Tamers can't suspend" (BT23-092) — 디지몬 1마리와 테이머 1명을 따로 고른다
        var durMulti = ParseDuration(s, out string multiRest);
        if (durMulti != Duration.None && durMulti != Duration.ThisAttack
            && (m = Regex.Match(multiRest.Trim(), @"^(?<a>(?:1|one) of (?:their|your opponent's) (?:[^,]*?)Digimon) and (?:1|one) of (?:their|your opponent's) Tamers (?:can't|cannot) (?<v>unsuspend|suspend)$", I)).Success
            && TargetSpec.Parse(m.Groups["a"].Value) is { Opponent: true } digiT)
        {
            bool un = m.Groups["v"].Value.Equals("unsuspend", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                int until = Until(c, durMulti);
                var names = new List<string>();
                foreach (var d in Pick(digiT, c, security))
                {
                    if (un) d.CannotUnsuspendUntil = Math.Max(d.CannotUnsuspendUntil, until); else d.CannotSuspendUntil = Math.Max(d.CannotSuspendUntil, until);
                    names.Add(d.TopCard.Name);
                }
                // 테이머: "can't suspend"는 액티브 상태인 것, "can't unsuspend"는 레스트 상태인 것을 먼저 추천
                var tamers = c.Enemy.Tamers.Distinct().OrderByDescending(t => un ? !c.Enemy.HasActiveTamer(t) : c.Enemy.HasActiveTamer(t)).ToList();
                var picked = tamers.Count == 0 ? new List<CardData>() : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, tamers, 1, upTo: false, $"[{c.Card.Name}] {(un ? "액티브" : "레스트")}가 될 수 없게 할 상대의 테이머를 고르세요.");
                foreach (var t in picked)
                {
                    var dict = un ? c.Enemy.TamerCannotUnsuspendUntil : c.Enemy.TamerCannotSuspendUntil;
                    dict[t] = Math.Max(dict.GetValueOrDefault(t), until);
                    names.Add("테이머 " + t.Name);
                }
                c.LastTamers35 = picked.ToList();
                if (names.Count == 0) c.LastStepResolved = false;
                Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] {(un ? "액티브" : "레스트")}가 될 수 없음" : "대상이 없습니다");
            }, null);
        }
        // DP 기간이 셈 뒤에 온 표기: "This Digimon gets +1000 DP for each of its face-down digivolution cards until your opponent's turn ends" (EX9-009) → 기간을 앞으로
        if ((m = Regex.Match(s, @"^(?<a>.+? gets? [+-]\d+ DP) for (?<e>each|every) (?<cnt>.+?) (?<dur>until .+|for the turn)$", I)).Success)
        {
            // "~ for each ~"은 문장 단위(ParseStep)가 읽는다
            var re = ParseStep($"{m.Groups["a"].Value} {m.Groups["dur"].Value} for {m.Groups["e"].Value} {m.Groups["cnt"].Value}", card, triggers);
            if (re.Step is { } step && step.Condition == null && !step.Optional) return (c => RunStep(step, c), null);
        }
        return null;
    }

    /// <summary>
    /// v0.35.0 "N of your opponent's Digimon or Tamers can't suspend / unsuspend until ~" — 상대의 디지몬(조건)과 테이머를 함께 후보로 고른다.
    /// 테이머는 PlayerZones.TamerCannotSuspendUntil / TamerCannotUnsuspendUntil (같은 카드 객체 단위)
    /// </summary>
    private static void TamerLock35(EffectContext c, TargetSpec target, bool un, int until, string label, bool security)
    {
        var pool = c.Enemy.BattleArea.Where(d => target.Filters.All(f => f(d)) && target.ContextFilters.All(f => f(c, d)) && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
        var tamerOf = new Dictionary<CardInstance, CardData>(ReferenceEqualityComparer.Instance);
        foreach (var t in c.Enemy.Tamers.Distinct())
        {
            var fake = new CardInstance(t, 0) { IsSuspended = !c.Enemy.HasActiveTamer(t) };
            if (target.Filters.All(f => f(fake)) && target.ContextFilters.All(f => f(c, fake))) { pool.Add(fake); tamerOf[fake] = t; }
        }
        IEnumerable<CardInstance> cands = pool;
        if (target.Selector != null) cands = target.Selector(cands);
        // 추천: "can't unsuspend"는 레스트 상태인 것 · "can't suspend"는 액티브 상태인 것 (그다음 DP 높은 디지몬)
        var ranked = cands.OrderByDescending(d => un ? d.IsSuspended : !d.IsSuspended).ThenByDescending(d => tamerOf.ContainsKey(d) ? 0 : d.GetCurrentDP()).ToList();
        var picks = target.Count == 0 ? ranked : c.Engine.ChooseTargets(c.Owner, ranked, target.Count, $"[{c.Card.Name}] 효과의 대상(상대의 디지몬 또는 테이머 {target.Count}장{(target.UpTo ? "까지" : "")})을 고르세요.", target.UpTo, null);
        var names = new List<string>();
        foreach (var d in picks)
        {
            if (tamerOf.TryGetValue(d, out var t))
            {
                var dict = un ? c.Enemy.TamerCannotUnsuspendUntil : c.Enemy.TamerCannotSuspendUntil;
                dict[t] = Math.Max(dict.GetValueOrDefault(t), until);
                names.Add("테이머 " + t.Name);
                continue;
            }
            if (un) d.CannotUnsuspendUntil = Math.Max(d.CannotUnsuspendUntil, until);
            else d.CannotSuspendUntil = Math.Max(d.CannotSuspendUntil, until);
            names.Add(d.TopCard.Name);
        }
        c.LastTargets = picks.Where(d => !tamerOf.ContainsKey(d)).ToList();
        c.LastTamers35 = picks.Where(tamerOf.ContainsKey).Select(d => tamerOf[d]).ToList();
        if (picks.Count == 0) c.LastStepResolved = false;
        Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] {(un ? "액티브" : "레스트")}가 될 수 없음" : "대상이 없습니다");
    }

    /// <summary>
    /// v0.35.0 카드별 문장 보정 — 영어 API 원문이 공식 한글 · 룰과 다른 곳:
    /// BT19-051 "1 of your Digimon can't be returned to the hand or deck and gets +3000 DP ~" — 공식 한글 "자신의 디지몬 1마리는 상대의 효과로 패/덱으로 되돌아가지 않고"
    /// (자신의 효과로는 되돌릴 수 있다)
    /// </summary>
    internal static string CardTextFix35(string clause, CardData card) =>
        card.CardId == "BT19-051"
            ? Regex.Replace(clause, @"can't be returned to the hand or deck and gets", "can't be returned to the hand or deck by your opponent's effects and gets", I)
            : clause;

    /// <summary>진화 코스트 감소 문장 정리: ", for each of their face-down digivolution cards, reduce the digivolution cost by 1" → "reduce ~ by 1 for each of its face-down digivolution cards"</summary>
    internal static string EvoReductionText35(string t) =>
        Regex.Replace(t, @", for each of (?:its|their|that Digimon's) face[- ]down digivolution cards, reduce (?<what>the digivolution cost|the cost|it) by (?<n>\d+)(?<end>\.?)$",
            ", reduce the digivolution cost by ${n} for each of its face-down digivolution cards${end}", I);
}
