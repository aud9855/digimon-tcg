using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.40.0 미지원 문장 14차 묶음: "When ~" 사건 (≪연계≫로 레스트 · 진화하려 할 때 · 맨 위 카드가 진화원으로 · 진화원이 덱으로 · 테이머 아래에 놓였을 때 …) ·
/// 효과를 가진 디지몬을 보는 지속 효과 대상 ("with the same name as this Digimon") · 키워드 부여 · 여러 Lv. 고르기 등.
/// 원래 규칙(1~13차)이 읽지 못한 절 · 사건 · 문장 · 대상만 14차 규칙으로 다시 읽는다 (엔진: GoldfishEngine.Batch40.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth40;

    // =====================================================================
    // 1. 절 — 원래 규칙이 읽지 못한 절만 (TryParse)
    // =====================================================================

    private static string? TryParse40(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseCore(rawClause, inherited, card, def);
        if (err == null || _clauseDepth40 > 0) return err;
        _clauseDepth40++;
        try
        {
            foreach (var parts in ClauseRewrites40(rawClause, card))
            {
                if (parts.Count == 0 || (parts.Count == 1 && parts[0] == rawClause)) continue;
                if (parts.Any(p => TryParseCore(p, inherited, card, null) != null)) continue;
                if (def != null)
                    foreach (var p in parts) TryParseCore(p, inherited, card, def);
                return null;
            }
            if (OwnerAuraClause40(rawClause, inherited, card, def)) return null;
            if (DetachClause40(rawClause, inherited, card, def)) return null;
            if (CostClause40(rawClause, inherited, card, def)) return null;
            return err;
        }
        finally { _clauseDepth40--; }
    }

    /// <summary>
    /// 같은 뜻의 다른 표기 · 둘로 나눈 절 (렌더러도 쓴다). 결과마다 절 목록 — 모두 원래 규칙으로 읽혀야 쓴다
    /// </summary>
    internal static IEnumerable<List<string>> ClauseRewrites40(string rawClause, CardData card)
    {
        string c = Regex.Replace(rawClause.Trim(), @"^(?:Inherited Effect|Security Effect)\s*", "", I);
        var lead = Regex.Match(c, @"^(?<labels>(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
        string labels = lead.Groups["labels"].Value;
        string body = lead.Groups["body"].Value.Trim();
        string bodyN = Normalize(body);
        Match m;

        // ", other than [Sea Animal], in any of its traits" → "in any of its traits other than [Sea Animal]" (BT25-010)
        if ((m = Regex.Match(c, @"^(?<a>.+?), other than (?<x>\[[^\]]+\]), in any of (?<its>its|their) traits(?<b>.*)$", I)).Success)
            yield return new List<string> { $"{m.Groups["a"].Value} in any of {m.Groups["its"].Value} traits other than {m.Groups["x"].Value}{m.Groups["b"].Value}" };
        // "When one of your Digimon with [Gammamon] in its text, other than [Bokomon], would leave ~" (LM-019 — 공식 "「보코몬」 이외의 「감마몬」이 기술되어 있는 자신의 디지몬")
        if ((m = Regex.Match(c, @"^(?<a>.*?\bWhen (?:one|any) of your [^,]*?Digimon[^,]*?), other than (?<x>\[[^\]]+\]), (?<b>would .+)$", I)).Success)
            yield return new List<string> { $"{m.Groups["a"].Value} without {m.Groups["x"].Value} in its name {m.Groups["b"].Value}" };

        // "When this card would be played or digivolved into, if ~, reduce the play or digivolution cost by 2" (EX5-012 · EX5-020 — 공식 "이 카드가 등장할 때 또는 이 카드로 진화할 때, ~ 지불하는 등장/진화 코스트 -2")
        if ((m = Regex.Match(bodyN, @"^When this card would be played or digivolved into, (?<mid>(?:if [^.]+?, )?(?:by [^.]+?, )?)reduce the play or digivolution cost by (?<n>\d+)\.?$", I)).Success)
            yield return new List<string>
            {
                $"{labels}When this card would be played, {m.Groups["mid"].Value}reduce the play cost by {m.Groups["n"].Value}.",
                $"{labels}When one of your Digimon would digivolve into this card, {NoListCommas40(m.Groups["mid"].Value)}reduce the digivolution cost by {m.Groups["n"].Value}.",
            };
        // "When a card with [Angoramon] in its text would be played or one of your Digimon would digivolve into such a card, by suspending this Digimon, reduce the play or digivolution cost by 2" (LM-009)
        if ((m = Regex.Match(bodyN, @"^When (?<p>an? [^,]+?) would be played or (?<who>one of your Digimon|this Digimon) would digivolve into such a card, (?<mid>(?:if [^.]+?, )?(?:by [^.]+?, )?)reduce the play or digivolution cost by (?<n>\d+)\.?$", I)).Success)
            yield return new List<string>
            {
                $"{labels}When {m.Groups["p"].Value} would be played, {m.Groups["mid"].Value}reduce the play cost by {m.Groups["n"].Value}.",
                $"{labels}When {m.Groups["who"].Value} would digivolve into {m.Groups["p"].Value}, {m.Groups["mid"].Value}reduce the digivolution cost by {m.Groups["n"].Value}.",
            };
        // 두 문장의 등장 코스트 감소: "When this card would be played, if ~, reduce the play cost by 6. By ~, reduce the play cost by 6." (BT16-065)
        if ((m = Regex.Match(bodyN, @"^When this card would be played, (?<a>if [^.]+?, reduce the play cost by \d+)\. (?<b>By [^.]+?, reduce the play cost by \d+)\.?$", I)).Success)
            yield return new List<string>
            {
                $"{labels}When this card would be played, {m.Groups["a"].Value}.",
                $"{labels}When this card would be played, {char.ToLowerInvariant(m.Groups["b"].Value[0])}{m.Groups["b"].Value[1..]}.",
            };
        // 지속 효과 두 개를 이은 문장: "While ~, this Digimon gains ＜Piercing＞ and ＜Blocker＞, and gains ＜Security A. +1＞ for each ~" (BT10-112) — 같은 조건의 두 지속 효과
        if ((m = Regex.Match(bodyN, @"^While (?<cond>[^,]+), this Digimon gains (?<k>＜[^＞]+＞(?:,? (?:and )?＜[^＞]+＞)*), and (?<b>(?:gains|gets) .+)$", I)).Success)
            yield return new List<string>
            {
                $"{labels}While {m.Groups["cond"].Value}, this Digimon gains {m.Groups["k"].Value}.",
                $"{labels}While {m.Groups["cond"].Value}, this Digimon {m.Groups["b"].Value}",
            };
        // "[Security] [Your Turn] All of your red or blue [TS] trait Digimon gain ＜Alliance＞. While you have [Apollomon] or [Dianamon], they also gain ＜Rush＞" (BT25-094 · BT25-095 · BT25-097 · BT25-099 · BT25-102)
        if ((m = Regex.Match(bodyN, @"^(?<a>All of your (?<x>[^.]+?Digimon) (?:gain|get) [^.]+?)\.? While (?<cond>[^,]+), they also (?<b>gain .+)$", I)).Success)
            yield return new List<string>
            {
                $"{labels}{m.Groups["a"].Value}.",
                $"{labels}While {m.Groups["cond"].Value}, all of your {m.Groups["x"].Value} {m.Groups["b"].Value}",
            };
        // "All your Digimon with ~" (EX5-042) → "All of your Digimon with ~"
        if (Regex.IsMatch(bodyN, @"^All your ", I))
            yield return new List<string> { labels + Regex.Replace(bodyN, @"^All your ", "All of your ", I) };
        // 원문 오타 "level 4 or highter" (EX8-071)
        if (Regex.IsMatch(c, @"\bhighter\b", I))
            yield return new List<string> { Regex.Replace(c, @"\bhighter\b", "higher", I) };
        // "~ until your opponent's turn ends. If played by effects, it also gains ＜Blocker＞" (BT27-025) — "also" = 앞 문장의 기간
        if ((m = Regex.Match(c, @"^(?<a>.+? until (?<d>your opponent's turn ends|the end of your opponent's turn|the end of their turn)\.) (?<ifc>If [^,]+), it also gains (?<k>＜[^＞]+＞)(?<rest>.*)$", I)).Success)
            yield return new List<string> { $"{m.Groups["a"].Value} {m.Groups["ifc"].Value}, it gains {m.Groups["k"].Value} until {m.Groups["d"].Value}.{m.Groups["rest"].Value}" };
        // "When this Digimon would digivolve into ~, you may trash up to 3 cards from the top of your security stack. For each security card trashed by this effect, reduce the digivolution cost by 1" (P-074 — 공식 "자신의 시큐리티를 3장까지 파기할 수 있다. 이 효과로 파기한 시큐리티 1장마다 지불하는 진화 코스트 -1")
        if ((m = Regex.Match(bodyN, @"^When this Digimon would digivolve into (?<into>[^.]+?), you may trash up to (?<n>\d+) cards from the top of your security stack\. For each (?:security )?card trashed by this effect, reduce the (?:digivolution |memory )?cost by (?<r>\d+)\.?$", I)).Success)
            yield return new List<string> { $"{labels}When this Digimon would digivolve into {m.Groups["into"].Value}, by trashing up to {m.Groups["n"].Value} cards from the top of your security stack, reduce the digivolution cost by {m.Groups["r"].Value} for each {PaidMarker36}." };
        // "When one of your Digimon would digivolve from level 5 to level 6, you may delete 1 of your Digimon to reduce the digivolution cost by 3" (EX2-064 — 공식 "Lv.5인 자신의 디지몬이 Lv.6으로 진화할 때, 자신의 디지몬 1마리를 소멸시키는 것으로, 지불하는 진화 코스트 -3")
        if ((m = Regex.Match(bodyN, @"^When one of your Digimon would digivolve from level (?<a>\d+) to level (?<b>\d+), you may delete 1 of your Digimon to reduce the digivolution cost by (?<n>\d+)\.?$", I)).Success)
            yield return new List<string> { $"{labels}When one of your level {m.Groups["a"].Value} Digimon would digivolve into a level {m.Groups["b"].Value} Digimon card, by deleting 1 of your Digimon other than the digivolving one, reduce the digivolution cost by {m.Groups["n"].Value}." };
        // 테이머 자신이 진화할 때:"[Your Turn] When this Tamer would digivolve into a green Digimon, reduce the memory cost of the digivolution by 1" (BT7-089 — 공식 "이 테이머가 그린인 디지몬으로 진화할 때")
        if (card.Type == CardType.Tamer && (m = Regex.Match(bodyN, @"^When this Tamer would digivolve into (?<x>[^,]+?), (?<rest>reduce .+)$", I)).Success)
        {
            string into = Regex.Replace(m.Groups["x"].Value, @"\bDigimon$", "Digimon card", I);
            yield return new List<string> { $"{labels}When this Digimon would digivolve into {into}, {m.Groups["rest"].Value}" };
        }
    }

    // =====================================================================
    // 2. 사건 — 원래 규칙이 읽지 못한 사건만 (ParseEventPhrase 끝)
    // =====================================================================

    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase40(string ev)
    {
        ev = ev.Trim();
        Match m;
        (List<EffectTrigger>, Func<EffectContext, bool>?) One(EffectTrigger t, Func<EffectContext, bool>? cond = null) => (new List<EffectTrigger> { t }, cond);

        // ≪연계≫로 레스트 (EX4-032 · EX4-033 · EX4-034 — 공식 "자신의 디지몬이 《연계》의 효과로 레스트했을 때")
        if (Regex.IsMatch(ev, @"^(?:one|any) of your Digimon (?:is|becomes|are) suspended by (?:an? )?＜Alliance＞ effects?$", I))
            return One(EffectTrigger.AllyDigimonSuspended, c => c.Engine.SuspendingForAlliance40 && Hit40(ref Diag40.AllianceSuspend));
        // "When an opponent's Digimon becomes unsuspended during a main phase" (BT9-089 — 공식 "상대의 디지몬이 메인 페이즈에서 액티브 상태가 되었을 때")
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon (?:becomes unsuspended|unsuspends|is unsuspended) during (?:a|the|any) main phase$", I))
            return One(EffectTrigger.OpponentDigimonUnsuspended, c => c.Engine.UnsuspendingInMainPhase40 && Hit40(ref Diag40.MainPhaseUnsuspend));
        // "When one of your Digimon digivolves into a same-level Digimon with the [X Antibody] trait" (BT9-092 — 공식 "같은 Lv.의 디지몬으로 진화했을 때")
        if ((m = Regex.Match(ev, @"^(?:one|any) of your Digimon digivolves? into a same-level (?<spec>Digimon(?: with .+)?)$", I)).Success
            && TargetSpec.Parse("all of your " + m.Groups["spec"].Value) is { Opponent: false } same)
            return One(EffectTrigger.AllyDigimonDigivolved, c => c.Other is { } d && d.EvolutionSources.FirstOrDefault() is { } prev
                && prev.Level == d.TopCard.Level && same.Filters.All(f => f(d)) && Hit40(ref Diag40.SameLevelEvo));
        // "When you play a Digimon with the [Four Great Dragons] trait or place (a) [Trial of the Four Great Dragons] in your battle area" (EX3-027 · EX3-034)
        if ((m = Regex.Match(ev, @"^you play (?<a>an? [^,]*?Digimon(?: with .+?)?) or place (?:an? )?(?<x>\[[^\]]+\]) in your battle area$", I)).Success
            && ParseAllySubject(m.Groups["a"].Value) is { } played)
        {
            var x = m.Groups["x"].Value.Trim('[', ']');
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.AllyOptionPlaced },
                c => c.Trigger == EffectTrigger.AllyOptionPlaced ? c.TriggerCard != null && CardNames.Is(c.TriggerCard, x) : c.Other != null && played(c, c.Other));
        }
        // 진화하려 할 때 (BT8-024 · BT14-018 "When this Digimon would digivolve or leave the battle area")
        if ((m = Regex.Match(ev, @"^this Digimon would digivolve(?<leave> or leave the battle area)?$", I)).Success)
        {
            if (!m.Groups["leave"].Success) return One(EffectTrigger.ThisDigimonWouldDigivolve, c => Hit40(ref Diag40.WouldDigivolve));
            return (new List<EffectTrigger> { EffectTrigger.ThisDigimonWouldDigivolve, EffectTrigger.ThisDigimonWouldLeave },
                c => c.Trigger == EffectTrigger.ThisDigimonWouldDigivolve ? Hit40(ref Diag40.WouldDigivolve) : c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self));
        }
        // 진화원에서 덱 아래로: "When [Vemmon] is placed from this Digimon's digivolution cards at the bottom of its owner's deck" (BT11-065) ·
        //  "When [Vemmon] returns to the bottom of the deck from this Digimon's digivolution cards" (BT18-065) · "When any [Vemmon] are returned to ~" (BT21-058)
        if ((m = Regex.Match(ev, $@"^(?:any )?(?<x>{TargetSpec.NameList}) (?:is placed from this Digimon's digivolution cards at the bottom of (?:its owner's|the|your) deck|(?:returns?|is returned|are returned) to the bottom of (?:the|its owner's|your) deck from this Digimon's digivolution cards)$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return One(EffectTrigger.ThisSourcesToDeck, c => c.Engine.CurrentSourceCards.Any(s => xs.Any(x => CardNames.Is(s, x))));
        }
        // 맨 위 카드가 효과로 진화원에: "When an effect places the top card of this Digimon in its digivolution cards" (EX5-001) ·
        //  "When effects place this Digimon's top stacked card as its bottom digivolution card" (BT22-006) · "~ the top card of one of your Digimon in a Digimon's digivolution cards" (EX5-065)
        if (Regex.IsMatch(ev, @"^(?:an effect places|effects place) the top card of this Digimon in its digivolution cards$", I))
            return One(EffectTrigger.AllyTopPlacedUnder, c => ReferenceEquals(c.Other, c.Self));
        if (Regex.IsMatch(ev, @"^(?:an effect places|effects place) this Digimon's top stacked card as its bottom digivolution card$", I))
            return One(EffectTrigger.AllyTopPlacedUnder, c => ReferenceEquals(c.Other, c.Self) && c.Engine.TopPlacedAsBottom40);
        if (Regex.IsMatch(ev, @"^(?:an effect places|effects place) the top card of (?:one|any) of your Digimon in a Digimon's digivolution cards$", I))
            return One(EffectTrigger.AllyTopPlacedUnder);
        // "When a purple card is placed under this Tamer" (BT10-093 — 엔진은 테이머 아래 카드를 쪽마다 모은다)
        if ((m = Regex.Match(ev, $@"^(?:a|an|any) (?:(?<color>{Colors}) )?cards? (?:is|are) placed under (?:this Tamer|one of your Tamers|your Tamers)$", I)).Success)
        {
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, true) : null;
            return One(EffectTrigger.AllyUnderTamerPlaced, c => color == null || c.Engine.CurrentSourceCards.Any(s => s.AllColors.Contains(color.Value)));
        }
        // "When this card is trashed as a digivolution card by the effect of [Rasenmon]" (BT8-081 — 「라센몬」의 ≪디지버스트≫로 진화원에서 파기되었을 때)
        if ((m = Regex.Match(ev, @"^this card is trashed as a digivolution card by the effect of (?<x>\[[^\]]+\])$", I)).Success)
        {
            string x = m.Groups["x"].Value.Trim('[', ']');
            return One(EffectTrigger.DigiBurstTrashedThis, c => c.Self != null && CardNames.Is(c.Self.TopCard, x) && Hit40(ref Diag40.EventMisc));
        }
        // "When effects add cards to your opponent's hand or trash cards from under your Tamers" (BT25-029)
        if (Regex.IsMatch(ev, @"^effects add cards to your opponent's hand or trash cards from under your Tamers$", I))
            return (new List<EffectTrigger> { EffectTrigger.OpponentHandAddedByEffect, EffectTrigger.UnderTamersTrashed }, null);
        // "When one of your Digimon's effects adds to this Digimon's digivolution cards" (BT17-056)
        if (Regex.IsMatch(ev, @"^(?:one|any) of your Digimon's effects adds? (?:cards )?to this Digimon's digivolution cards$", I))
            return One(EffectTrigger.AllySourcesAdded, c => ReferenceEquals(c.Other, c.Self) && ReferenceEquals(c.Engine.CurrentSourceEffectOwner, c.Owner));
        return null;
    }

    private static bool Hit40(ref int counter) { Diag40.Hit(ref counter); return true; }

    // =====================================================================
    // 3. 효과를 가진 디지몬을 보는 지속 효과 대상 — "All of your other Digimon with the same name as this Digimon gain ＜Rush＞" (BT5-063 · EX1-051 · P-045)
    //    공식 Q&A BT5-063: 진화해 명칭이 바뀌면 효과를 잃는다 → 처리할 때마다 효과를 가진 디지몬의 지금 명칭과 비교
    // =====================================================================

    private static bool OwnerAuraClause40(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string clause = Normalize(rawClause);
        var m = Regex.Match(clause, @"^(?<labels>(?:\[[^\]]+\]\s*)*)All of your (?<other>other )?Digimon with the same name as this Digimon (?<rest>(?:gain|get)s? .+)$", I);
        if (!m.Success || card.Type != CardType.Digimon) return false;
        string alt = $"{m.Groups["labels"].Value}All of your {m.Groups["other"].Value}Digimon {m.Groups["rest"].Value}";
        if (TryParseCore(alt, inherited, card, null) != null) return false;
        if (def == null) return true;
        int from = def.StaticCount;
        TryParseCore(alt, inherited, card, def);
        bool other = m.Groups["other"].Success;
        def.PatchStatics(from, e => e with { ExcludeSource = e.ExcludeSource || other, SubjectOf = (ctx, d) => ctx.Digimon != null && CardNames.Same(ctx.Digimon.TopCard, d.TopCard) && Hit40(ref Diag40.OwnerAura) });
        return true;
    }

    // =====================================================================
    // 4. 조건 — 원래 규칙이 읽지 못한 것만 (ParseSideCondition 끝)
    // =====================================================================

    private static Func<PlayerZones, PlayerZones, bool>? ParseSideCondition40(string cond)
    {
        Match m;
        // "you have a Digimon with 3 or more digivolution cards and the [Light Fang], [Night Claw] or [Galaxy] trait" (EX5-012) — 쉼표 목록
        // "there is a Digimon with the [Boss] trait" (BT16-065 — 공식 Q&A: 상대의 디지몬이어도 된다)
        if ((m = Regex.Match(cond.Trim(), @"^(?<who>you|your opponent|there) (?:has|have|is) (?:a|an|1) (?<spec>(?:[^,]*? )?Digimon with .+)$", I)).Success)
        {
            string who = m.Groups["who"].Value.ToLowerInvariant();
            var spec = TargetSpec.Parse($"all of {(who == "your opponent" ? "your opponent's" : "your")} {Regex.Replace(m.Groups["spec"].Value, @" (?:in play|on the field)$", "", I)}");
            if (spec == null || spec.ContextFilters.Count > 0) return ParseSideCondition45(cond);   // v0.45.0
            return who switch
            {
                "there" => (o, e) => o.BattleArea.Concat(e.BattleArea).Any(d => spec.Filters.All(f => f(d))),
                "your opponent" => (_, e) => e.BattleArea.Any(d => spec.Filters.All(f => f(d))),
                _ => (o, _) => o.BattleArea.Any(d => spec.Filters.All(f => f(d))),
            };
        }
        return ParseSideCondition45(cond);   // v0.45.0 (SentenceParser.Batch45Cond.cs)
    }

    // =====================================================================
    // 5. ≪분리≫ (종합 룰 16-46): 이 디지몬이 자신의 효과 이외로 배틀 에어리어를 벗어날 때, 이 디지몬의 지정된 링크 카드 1장을 파기하는 것으로, 벗어나지 않는다
    //    ("벗어나지 않는다"는 강제 — 파기는 임의). BT26-010 · BT26-019 · BT26-028 · BT26-037 · BT26-051 · BT26-063 · BT26-084
    // =====================================================================

    private static bool DetachClause40(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var m = Regex.Match(Normalize(rawClause).TrimEnd('.'), @"^＜Detach \((?<arg>.+)\)＞$", I);
        if (!m.Success || card.Type != CardType.Digimon) return false;
        if (ParseSpecifiedCard(m.Groups["arg"].Value) is not { } spec) return false;
        if (def == null) return true;
        Action<EffectContext> act = c =>
        {
            if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) return;
            var links = c.Self.LinkedCards.Where(spec).ToList();
            if (links.Count == 0) return;
            if (!c.Engine.AskYesNo(c.Owner, c.Self, $"≪분리≫ [{c.Self.TopCard.Name}]의 지정된 링크 카드 1장을 파기하고 배틀 에어리어를 벗어나지 않을까요?", recommended: true)) return;
            var x = links.Count == 1 ? links[0]
                : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, links, 1, upTo: false, $"≪분리≫ [{c.Self.TopCard.Name}]: 파기할 링크 카드를 고르세요.").FirstOrDefault() ?? links[0];
            if (!c.Self.LinkedCards.Remove(x)) return;
            c.Owner.Trash.Add(x);
            c.RecalculateFieldBuffs();
            c.PreventLeave = true;
            Diag40.Hit(ref Diag40.Detach);
            c.Log($"[≪분리≫] [{c.Self.TopCard.Name}]: 링크 카드 [{x.Name}]을(를) 파기하고 배틀 에어리어를 벗어나지 않습니다.");
            c.Engine.NotifyLinkCardsTrashed(c.Owner, c.Self, 1);
        };
        Func<EffectContext, bool> when = c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && !l.ByOwnEffect && l.CanPrevent
                                              && c.Self != null && c.Self.LinkedCards.Any(spec);
        if (inherited) def.Inherited(EffectTrigger.ThisDigimonWouldLeave, act, when);
        else def.On(EffectTrigger.ThisDigimonWouldLeave, act, when);
        return true;
    }

    /// <summary>
    /// v0.40.0 지정 카드의 다른 표기 (ParseSpecifiedCard 끝): "[Junomon]/Lv.5 or lower w/[Iliad] trait" (BT26-083) · "[Lv.4 or lower w/[DS] trait]" (EX12-028 — 바깥 괄호) ·
    /// "[Deva] or [Four Sovereigns] trait" (EX5-050)
    /// </summary>
    private static Func<CardData, bool>? ParseSpecifiedCard40(string spec)
    {
        Match m;
        spec = spec.Trim();
        if ((m = Regex.Match(spec, @"^\[(?<in>Lv\.\d+.+)\]$", I)).Success && ParseSpecifiedCard(m.Groups["in"].Value) is { } inner) return inner;
        if ((m = Regex.Match(spec, @"^(?<names>\[[^\]]+\](?:\s*/\s*\[[^\]]+\])*)\s*/\s*(?<lv>Lv\.\d+.*)$", I)).Success
            && ParseSpecifiedCard(m.Groups["names"].Value) is { } a && ParseSpecifiedCard(m.Groups["lv"].Value) is { } b)
            return c => (c.Type == CardType.Digimon && a(c)) || b(c);
        if (Regex.IsMatch(spec, @"^\[[^\]]+\](?: or \[[^\]]+\])+ trait$", I)) return ParseSpecifiedCard(Regex.Replace(spec, @"\] or \[", "]/["));
        return null;
    }
}

internal static partial class SentenceParser
{
    /// <summary>"[A], [B] or [C]" → "[A] or [B] or [C]" (쉼표로 문장을 나누는 규칙이 이름 목록을 자르지 않게)</summary>
    internal static string NoListCommas40(string s) => Regex.Replace(s, @"\],\s*(?=\[)", "] or ");
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 6. 코스트를 정하는 · 늘리는 절 (TryParse40)
    // =====================================================================

    private static bool CostClause40(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string c = Regex.Replace(Normalize(rawClause), @"^(?:Inherited Effect|Security Effect)\s*", "", I).Trim();
        Match m;
        // "When this card would be used from your hand, its memory cost is equal to the number of cards in your security stack" (BT7-100 — 공식 Q&A: 시큐리티 0장이면 0 코스트)
        if (card.Type == CardType.Option && !inherited
            && Regex.IsMatch(c, @"^When (?:this card would be used|you would use this card)(?: from your hand)?, its (?:memory |use )?cost is equal to the number of cards in your security stack\.?$", I))
        {
            int baseCost = card.PlayCost;
            def?.ReducePlayCost((z, _) => { Diag40.Hit(ref Diag40.SetCost); return baseCost - z.SecurityStack.Count; });
            return true;
        }
        // "When you would digivolve into this card from your hand, the memory cost of the digivolution is equal to the number of cards in your security stack. If you have 0 security cards, the memory cost is 1"
        // (BT7-040 — 공식 Q&A: 시큐리티 매수에서 다른 효과로 줄이는 만큼을 뺀다)
        if (card.Type == CardType.Digimon && !inherited
            && (m = Regex.Match(c, @"^When (?:you would digivolve|one of your Digimon would digivolve) into this card(?: from your hand)?, the (?:memory )?cost of the digivolution is equal to the number of cards in your security stack\.(?: If you have 0 security cards, the memory cost is (?<z>\d+)\.?)?$", I)).Success)
        {
            int zero = m.Groups["z"].Success ? int.Parse(m.Groups["z"].Value) : 0;
            def?.EvoCostModifier(new EvoCostModifier(EffectSource.Main, EvoCostSubject.IntoThisCard, null, null, null, null, 0, OncePerTurn: false, YourTurnOnly: false)
            {
                Delta40 = (ctx, d) =>
                {
                    Diag40.Hit(ref Diag40.SetCost);
                    int target = ctx.Owner.SecurityStack.Count == 0 ? zero : ctx.Owner.SecurityStack.Count;
                    return ctx.TriggerCard == null ? 0 : ActionValidator.EvolutionCost(d, ctx.TriggerCard) - target;
                },
            });
            return true;
        }
        // "[Opponent's Turn] When an opponent's Digimon with no digivolution cards would digivolve, increase the digivolution cost by 1" (EX3-016 · EX3-019)
        if (card.Type == CardType.Digimon
            && (m = Regex.Match(c, @"^\[Opponent's Turn\]\s*When an opponent's (?<spec>Digimon(?: with [^,]+)?) would digivolve, increase the (?:digivolution |memory )?cost by (?<n>\d+)\.?$", I)).Success
            && TargetSpec.Parse("all of your opponent's " + m.Groups["spec"].Value) is { Opponent: true, ContextFilters.Count: 0 } who)
        {
            var filters = who.Filters.ToList();
            def?.OpponentEvoCostIncrease40(inherited ? EffectSource.Inherited : EffectSource.Main, d => filters.All(f => f(d)), int.Parse(m.Groups["n"].Value), opponentsTurnOnly: true);
            return true;
        }
        // "[Your Turn] When any [Boss] or [TS] trait Digimon cards would be played, by suspending this Tamer, reduce the costs by 1. If you have no Digimon, instead reduce the costs by 2" (BT26-088)
        if ((m = Regex.Match(c, @"^(?<labels>(?:\[(?:Your Turn|All Turns|Once Per Turn)\]\s*)*)When (?<p>any [^,]+?) would be played, by (?<cost>[^,]+?), reduce the (?:play )?costs? by (?<n>\d+)\. If you have no Digimon, instead reduce the (?:play )?costs? by (?<m>\d+)\.?$", I)).Success
            && FieldPlayCostTarget(m.Groups["p"].Value) is { } which && ParseCost(m.Groups["cost"].Value, card) is { } pay)
        {
            int n = int.Parse(m.Groups["n"].Value), alt = int.Parse(m.Groups["m"].Value);
            string labels = m.Groups["labels"].Value;
            def?.FieldPlayCostModifier(new FieldPlayCostModifier(inherited ? EffectSource.Inherited : EffectSource.Main, which, n,
                new PlayCostOption(n, pay.CanPay, pay.Pay, pay.Recommend, pay.Describe, m.Groups["cost"].Value),
                Optional: false, OncePerTurn: labels.Contains("[Once Per Turn]", StringComparison.OrdinalIgnoreCase),
                YourTurnOnly: labels.Contains("[Your Turn]", StringComparison.OrdinalIgnoreCase), Breeding: false)
            { Amount = ctx => { Diag40.Hit(ref Diag40.SetCost); return ctx.Owner.BattleArea.Count == 0 ? alt : n; } });
            return true;
        }
        return false;
    }
}
