using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.36.0 미지원 문장 10차 묶음: 플레이어 제한 (효과로 등장 · 등장 코스트 마이너스 · 옵션 사용 — 엔진: GoldfishEngine.EffectPlay.cs) ·
/// 두 술어 보호 · 공개 + 상한 늘리기 · 진화할 때 비용을 내는 진화 코스트 감소 · Lv.3/4/5 1장씩 등장
/// </summary>
internal static partial class SentenceParser
{
    // =====================================================================
    // 1. 플레이어 제한
    // =====================================================================

    /// <summary>
    /// 지속 효과 (이 디지몬이 있는 동안): "Players can't play Digimon by effects" (BT14-009 · BT9-033 · BT9-047) · "Players can't reduce play costs" (BT8-071 · EX7-015 · ST12-03 · ST13-08) ·
    /// "your opponent can't use Option cards" (BT8-057 — "[Opponent's Turn] While all of your Digimon are suspended, ~"). 읽지 못하면 null, 읽으면 ""
    /// </summary>
    private static string? ParseStatic36(string s, bool inherited, CardData card, Func<StaticContext, bool> cond, Pending pending)
    {
        Keyword? kw = null;
        if (Regex.IsMatch(s, @"^players can't play Digimon by effects?$", I)) kw = Keyword.PlayersCannotPlayDigimonByEffects;
        else if (Regex.IsMatch(s, @"^players can't reduce play costs?$", I)) kw = Keyword.PlayersCannotReducePlayCosts;
        else if (Regex.IsMatch(s, @"^your opponent can't use Option cards$", I)) kw = Keyword.OpponentCannotUseOptions;
        else if (Regex.IsMatch(s, @"^none of your opponent's Tamers can unsuspend$", I)) kw = Keyword.OpponentTamersCannotUnsuspend;   // BT17-048
        if (TamerAuraStatic36(s, inherited, card, cond, pending) is { } aura) return aura;   // BT25-104
        if (kw is not { } k || card.Type != CardType.Digimon) return null;
        var c1 = cond;
        pending.Registrations.Add(d => d.Static(StaticStat.Keyword, inherited ? EffectSource.Inherited : EffectSource.Main, c => c1(c) ? 1 : 0, k));
        return "";
    }

    /// <summary>
    /// 기간 제한 (상대 플레이어): "Your opponent can't play Digimon (or Tamers) by effects until the end of their turn" (BT8-097 · BT20-020) ·
    /// "Until your opponent's turn ends, their effects can't play Digimon or Tamers from the trash" (BT23-014) ·
    /// "Your opponent can't use Option cards until the end of their next turn" · "~ this turn" (EX1-072)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? PlayerRestriction36(string s, string label)
    {
        Match m;
        Duration dur = ParseDuration(s, out string rest);
        if (dur == Duration.None && (m = Regex.Match(s, @"^(?<rest>.+?) this turn$", I)).Success) { dur = Duration.ThisTurn; rest = m.Groups["rest"].Value; }
        if (dur is Duration.None or Duration.ThisAttack) return null;
        rest = rest.Trim().TrimEnd(',');
        if ((m = Regex.Match(rest, @"^your opponent can't play (?<what>Digimon|Tamers|Digimon or Tamers|Digimon and Tamers) by effects$", I)).Success)
        {
            string what = m.Groups["what"].Value;
            bool digimon = what.Contains("Digimon", StringComparison.OrdinalIgnoreCase), tamers = what.Contains("Tamers", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                int until = Until(c, dur);
                if (digimon) c.Enemy.CannotPlayDigimonByEffectsUntil = Math.Max(c.Enemy.CannotPlayDigimonByEffectsUntil, until);
                if (tamers) c.Enemy.CannotPlayTamersByEffectsUntil = Math.Max(c.Enemy.CannotPlayTamersByEffectsUntil, until);
                Emit(c, label, $"상대는 {(digimon && tamers ? "디지몬과 테이머를" : digimon ? "디지몬을" : "테이머를")} 효과로 등장시킬 수 없다");
            }, null);
        }
        if (Regex.IsMatch(rest, @"^(?:their|your opponent's) effects can't play Digimon or Tamers from (?:the|their) trash$", I))
            return (c =>
            {
                c.Enemy.CannotPlayFromTrashByEffectsUntil = Math.Max(c.Enemy.CannotPlayFromTrashByEffectsUntil, Until(c, dur));
                Emit(c, label, "상대는 트래시에서 디지몬과 테이머를 효과로 등장시킬 수 없다");
            }, null);
        if (Regex.IsMatch(rest, @"^your opponent can't use Option cards$", I))
            return (c =>
            {
                c.Enemy.CannotUseOptionsUntil = Math.Max(c.Enemy.CannotUseOptionsUntil, Until(c, dur));
                Emit(c, label, "상대는 옵션 카드를 사용할 수 없다");
            }, null);
        return null;
    }

    // =====================================================================
    // 4. 진화할 때 비용을 내는 진화 코스트 감소
    // =====================================================================

    /// <summary>"낸 매수마다" 표시 (진화 코스트 감소의 for each 자리)</summary>
    internal const string PaidMarker36 = "⟦paid⟧";

    /// <summary>
    /// "When digivolving into this card, by suspending up to 5 Tamers, for each Tamer suspended by this effect, reduce the digivolution cost by 1" (BT17-048) ·
    /// "When you would digivolve into this card, by returning up to 5 cards with the [D-Brigade] trait from your trash to the top of your deck, reduce the digivolution cost by 1 for each returned card" (EX3-054)
    /// → "When this Digimon would digivolve into this card, by ~, reduce the digivolution cost by 1 for each ⟦paid⟧" (패에서 이 카드로 진화할 때 · 낸 매수마다 — 공식 Q&A EX3-054: 되돌린 뒤 진화를 그만둘 수 없다)
    /// </summary>
    internal static string EvoReductionText36(string t)
    {
        var m = Regex.Match(t, @"^(?<labels>(?:\[[^\]]+\]\s*)*)When (?:digivolving|you would digivolve|you digivolve) into this card, by (?<cost>[^,]+?), (?:for (?:each|every) (?:Tamer|card|one) (?:suspended|returned|trashed|placed) (?:by|with) this effect, reduce the (?:digivolution |memory )?cost by (?<n>\d+)|reduce the (?:digivolution |memory )?cost by (?<n>\d+) for (?:each|every) (?:returned card|card returned|(?:Tamer|card|one) (?:suspended|returned|trashed|placed)(?: (?:this way|by this effect|with this effect))?))\.?$", I);
        return m.Success ? $"{m.Groups["labels"].Value}When this Digimon would digivolve into this card, by {m.Groups["cost"].Value}, reduce the digivolution cost by {m.Groups["n"].Value} for each {PaidMarker36}." : t;
    }

    /// <summary>
    /// 비용 "suspending up to 5 Tamers" — "of your"가 없으면 양쪽의 액티브 테이머 (공식 Q&A BT17-048: 자신과 상대의 테이머를 고를 수 있다). 낸 매수는 PaidCount.
    /// AI · 추천은 상대의 테이머부터
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? SuspendAnyTamers36(int n)
    {
        List<(PlayerZones Zones, CardData Tamer)> Able(EffectContext c) =>
            c.Enemy.Tamers.Distinct().Where(t => c.Enemy.HasActiveTamer(t) && !c.Engine.TamerSuspendLocked(c.Enemy, t)).Select(t => (c.Enemy, t))
                .Concat(c.Owner.Tamers.Distinct().Where(t => c.Owner.HasActiveTamer(t) && !c.Engine.TamerSuspendLocked(c.Owner, t)).Select(t => (c.Owner, t))).ToList();
        return (c => Able(c).Count > 0,
                c =>
                {
                    var able = Able(c);
                    var fakes = able.Select(x => new CardInstance(x.Tamer, 0)).ToList();   // 같은 카드 객체가 양쪽에 있을 수 있어 가짜 디지몬으로 구분한다
                    var picks = c.Engine.ChooseTargets(c.Owner, fakes, Math.Min(n, fakes.Count), $"[{c.Card.Name}] 비용으로 레스트시킬 테이머를 고르세요 ({n}명까지, 상대의 테이머도).", true, null);
                    int done = 0;
                    foreach (var f in picks)
                    {
                        var (zones, tamer) = able[fakes.IndexOf(f)];
                        if (!zones.HasActiveTamer(tamer)) continue;
                        c.Engine.SuspendTamer(zones, tamer);
                        done++;
                    }
                    c.PaidCount = done;
                },
                _ => true, $"테이머 {n}명까지를 레스트시켜");
    }

    /// <summary>
    /// 문장 (ParseStep 처음): "Until ~, this Digimon isn't affected by their ＜De-Digivolve＞ effects and it gets +1000 DP for each of its face-down digivolution cards" (BT22-060) —
    /// 두 술어를 같은 기간으로 (DP의 "for each"는 문장 단위가 읽는다)
    /// </summary>
    private static Step? TwoPredStep36(string s, CardData card, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s.Trim().TrimEnd('.'), @"^(?<dur>until [^,]+|for the turn), (?<a>this Digimon (?:isn't|is not) affected by [^,]+?) and (?:it )?(?<b>gets [+-]\d+ ?DP for (?:each|every) .+)$", I);
        if (!m.Success) return null;
        var a = ParseStep($"{m.Groups["dur"].Value}, {m.Groups["a"].Value}", card, triggers);
        var b = ParseStep($"{m.Groups["dur"].Value}, this Digimon {m.Groups["b"].Value}", card, triggers);
        if (a.Step is not { } stepA || b.Step is not { } stepB || stepA.Condition != null || stepA.Optional || stepB.Condition != null || stepB.Optional) return null;
        return new Step(null, c => { RunStep(stepA, c); RunStep(stepB, c); }, false, s);
    }

    /// <summary>v0.36.0 동작 (ParseAction35 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction36(string raw, CardData card, List<EffectTrigger> triggers)
    {
        string s = raw.Trim().TrimEnd('.');
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        if (PlayerRestriction36(s, label) is { } restriction) return restriction;
        if (PlayEachLevel36(s, label) is { } eachLevel) return eachLevel;   // BT22-028
        if (TamerAsDigimon36(s, card, label) is { } tamerDigimon) return tamerDigimon;   // 「최건우」
        // "~ may attack your opponent's Digimon ⟦unsus⟧" — 이 효과로는 액티브 상태인 상대의 디지몬에게도 어택할 수 있다 (어택하는 디지몬이 이 턴 동안 그렇게 된다)
        if (Regex.Match(s, @"^(?<pre>.+? may attack (?:your opponent's|their) Digimon) ⟦unsus⟧$", I) is { Success: true } us
            && ParseStep(us.Groups["pre"].Value, card, triggers) is { Step: { Condition: null, Optional: false } attackStep }
            && TargetSpec.Parse(Regex.Match(us.Groups["pre"].Value, @"^(?<t>.+?) may attack", I).Groups["t"].Value) is { Opponent: false, AnySide: false } attackers)
            return (c =>
            {
                foreach (var d in Pick(attackers, c, false).Where(x => c.Owner.BattleArea.Contains(x)))
                    d.TimedKeywords.Add((Keyword.CanAttackUnsuspended, c.TurnSequence));
                c.RecalculateFieldBuffs();
                RunStep(attackStep, c);
            }, null);
        return null;
    }
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 3. 비용 · 정리
    // =====================================================================

    /// <summary>
    /// 절 정리 (Normalize 끝): "By placing ~ as any of your Digimon's bottom digivolution card, or face down under any of your [X] trait Tamers, ~" (BT27-020) —
    /// 비용 안의 ", or" 쉼표를 지워 "By ~, ~"가 한 비용으로 읽히게 한다
    /// </summary>
    internal static string Normalize36(string s)
    {
        s = Regex.Replace(s, @"(?<=\bby placing [^,]+? bottom digivolution cards?), or (?=face[- ]down under )", " or ", I);
        // 두 술어 보호 (BT22-060 · BT26-057): "their ＜De-Digivolve＞ effects don't affect this Digimon" → "this Digimon isn't affected by their ＜De-Digivolve＞ effects" ·
        // "~ effects don't affect this Digimon, and it gets +3000 DP" → 쉼표 없이 (NormalizeProtection34가 "and it ~"를 읽는다)
        s = Regex.Replace(s, @"(?<=^|[,.]\s|\]\s)(?:their|your opponent's) ＜De-Digivolve＞ effects (?:don't|do not) affect this Digimon\b", "this Digimon isn't affected by their ＜De-Digivolve＞ effects", I);
        s = Regex.Replace(s, @"(?<=\beffects (?:don't|do not) affect this Digimon|\bisn't affected by their ＜De-Digivolve＞ effects), and (?=it gets [+-]\d+ ?DP\b)", " and ", I);
        // "delete 1 of your Digimon with the [Ver.3] trait and all of your opponent's Digimon with the lowest play cost" (BT26-055) — 두 대상을 각각 소멸 ("and delete ~")
        s = Regex.Replace(s, @"(?<=\[[^\]]+\]) 's\b", "'s");   // 원문 "[Kudamon] 's" (EX13-071)
        // "Then, that Digimon may attack your opponent's Digimon. Your opponent's unsuspended Digimon can also be attacked with this effect." (BT21-096) — 앞 문장에 붙인다
        s = Regex.Replace(s, @"(?<=\bmay attack (?:your opponent's|their) Digimon)\. (?:Your opponent's|Their) unsuspended Digimon can also be attacked (?:with|by) this effect\.?", " ⟦unsus⟧.", I);
        s = Regex.Replace(s, @"\b(?<v>[Dd]elete) (?<a>1 of your (?!opponent)(?:[^.,]|\.(?=\d))+?) and (?=all of your opponent's Digimon\b)", "${v} ${a} and delete ", I);
        return s;
    }

    /// <summary>
    /// 비용: "trashing 1 card in your hand or placing it face down under any of your [Keenan Crier]s" (BT26-072 — "it" = 패의 카드 1장) ·
    /// "placing 1 ~ from your hand as any of your Digimon's bottom digivolution card or face down under any of your [X] trait Tamers" (BT27-020 — 같은 카드를 둘 중 한 곳에) ·
    /// "suspending 1 of your [Rhythm]" (BT17-048 진화원 — 이름으로 고르는 자신의 테이머 · 디지몬)
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost36(string cost, CardData card)
    {
        Match m;
        cost = cost.Trim();
        if ((m = Regex.Match(cost, @"^(?<a>trashing (?<pick>1 card|\d+ cards?) (?:in|from) your hand) or placing (?:it|them) (?<rest>face[- ]down under .+)$", I)).Success)
            return ParseCost35($"{m.Groups["a"].Value} or placing {m.Groups["pick"].Value} from your hand {m.Groups["rest"].Value}", card);
        if ((m = Regex.Match(cost, @"^(?<a>placing (?<pick>.+?) from your hand) as (?<dest>(?:any|1|one) of your .+?bottom digivolution cards?) or (?<rest>face[- ]down under .+)$", I)).Success)
            return ParseCost35($"{m.Groups["a"].Value} as {m.Groups["dest"].Value} or placing {m.Groups["pick"].Value} from your hand {m.Groups["rest"].Value}", card);
        if (PlaceEachLevelCost36(cost) is { } eachLevelCost) return eachLevelCost;   // EX13-071
        if (BothCosts36(cost, card) is { } both) return both;   // "A and B"
        // "suspending 1 of your [Rhythm]" — 그 이름의 액티브 테이머 또는 액티브 디지몬 1장
        if ((m = Regex.Match(cost, @"^suspending (?:1|one) of your \[(?<name>[^\]]+)\]s?$", I)).Success)
        {
            string name = m.Groups["name"].Value;
            List<CardData> Tamers(EffectContext c) => c.Owner.Tamers.Distinct().Where(t => CardNames.Is(t, name) && c.Owner.HasActiveTamer(t) && !c.Engine.TamerSuspendLocked(c.Owner, t)).ToList();
            List<CardInstance> Digimon(EffectContext c) => c.Owner.BattleArea.Where(d => !d.IsSuspended && CardNames.Is(d.TopCard, name) && !EffectRules.IsSuspendLocked(c.State, d)).ToList();
            return (c => Tamers(c).Count > 0 || Digimon(c).Count > 0,
                    c =>
                    {
                        if (Tamers(c).FirstOrDefault() is { } t) { c.Engine.SuspendTamer(c.Owner, t); return; }
                        var d = Digimon(c).OrderBy(x => x.GetCurrentDP()).FirstOrDefault();
                        if (d == null) { c.CostFailed = true; return; }
                        c.Engine.Suspend(d);
                    },
                    _ => true, $"자신의 [{name}] 1장을 레스트시켜");
        }
        return null;
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// "Reveal ~. You may play 1 play cost 4 or lower ~ among them ~. For each of this Digimon's face-down digivolution cards, add 1 to this effect's play cost maximum. Return the rest ~." (EX9-053) —
    /// 상한 늘리기 문장을 남은 카드 문장 뒤로 옮겨 공개 문장이 하나로 합쳐지게 한다 (상한은 합친 공개 문장의 "play cost 4 or lower"를 늘린다)
    /// </summary>
    private static List<string> ReorderRevealMaxUp36(List<string> sentences)
    {
        // "This effect can't play cards with the same name as any of your Tamers." (BT22-072 · BT24-034 · EX10-047 · EX10-051) — 앞 문장(등장)에 붙인다
        for (int k = sentences.Count - 1; k > 0; k--)
            if (Regex.IsMatch(sentences[k], @"^This effect can't play cards with the same names? as (?:any of )?your Tamers\.?$", I)
                && Regex.IsMatch(sentences[k - 1], @"without paying (?:the|its) (?:memory |play )?costs?(?: ⟦no-onplay⟧)?\.?$", I))
            {
                sentences = sentences.ToList();
                sentences[k - 1] = sentences[k - 1].TrimEnd('.') + " ⟦no-tamer-name⟧.";
                sentences.RemoveAt(k);
            }
        for (int i = 0; i + 3 < sentences.Count; i++)
        {
            if (!Regex.IsMatch(sentences[i], @"^(?:Then,?\s+)?Reveal the top \d+ cards? of\b", I)
                || !Regex.IsMatch(sentences[i + 2], @"^(?:For (?:each|every) .+?|If [^,]+), add \d+ to (?:this effect's|the) (?:play cost|level|DP) maximum\.?$", I)
                || !Regex.IsMatch(sentences[i + 3], $@"^{RevealRest}\.?$", I))
                continue;
            var list = sentences.ToList();
            (list[i + 2], list[i + 3]) = (list[i + 3], list[i + 2]);
            return list;
        }
        return sentences;
    }
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 5. "1 each of level 3, level 4 and level 5 ~" 여러 장
    // =====================================================================

    /// <summary>"level 3, level 4 and level 5" · "level 4 and level 5" → [3, 4, 5]</summary>
    private static List<int>? LevelList36(string text)
    {
        var m = Regex.Match(text.Trim(), @"^level (?<a>\d+)(?:, level (?<b>\d+))*,? and level (?<z>\d+)$", I);
        if (!m.Success) return null;
        var list = new List<int> { int.Parse(m.Groups["a"].Value) };
        list.AddRange(m.Groups["b"].Captures.Select(x => int.Parse(x.Value)));
        list.Add(int.Parse(m.Groups["z"].Value));
        return list.Distinct().Count() == list.Count ? list : null;
    }

    /// <summary>"Digimon cards with [Aqua] or [Sea Animal] in any of their traits" → Lv.마다 "1 level N Digimon card with ~ in any of its traits"의 조건</summary>
    private static List<Func<CardData, bool>>? EachLevelSpecs36(List<int> levels, string rest)
    {
        string one = Regex.Replace(rest.Trim(), @"\bcards\b", "card", I);
        one = Regex.Replace(one, @"\bin any of their traits\b", "in any of its traits", I);
        one = Regex.Replace(one, @"\bin their names\b", "in its name", I);
        var specs = new List<Func<CardData, bool>>();
        foreach (int lv in levels)
        {
            if ((ParseCardPick($"1 level {lv} {one}") ?? ParseNamePick($"1 level {lv} {one}")) is not { } p || p.Count != 1) return null;
            specs.Add(p.Match);
        }
        return specs;
    }

    /// <summary>
    /// "You may play 1 each of level 3, level 4 and level 5 Digimon cards with [Aqua] or [Sea Animal] in any of their traits from this Digimon's digivolution cards without paying the cost" (BT22-028) —
    /// Lv.마다 1장씩 (공식 "Lv.3과 Lv.4와 Lv.5의 디지몬 카드 1장씩을 코스트를 지불하지 않고 등장시킬 수 있다"). 효과로 등장 (한 곳)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? PlayEachLevel36(string s, string label)
    {
        var m = Regex.Match(s, @"^play 1 each of (?<lv>level \d+(?:, level \d+)*,? and level \d+) (?<rest>.+?) from (?<from>this Digimon's digivolution cards|its digivolution cards|your trash|your hand|your hand or trash) without paying (?:the|its) (?:memory )?costs?$", I);
        if (!m.Success || LevelList36(m.Groups["lv"].Value) is not { } levels || EachLevelSpecs36(levels, m.Groups["rest"].Value) is not { } specs) return null;
        string from = m.Groups["from"].Value.ToLowerInvariant();
        bool fromSources = from.Contains("digivolution"), fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
        return (c =>
        {
            var played = new List<string>();
            for (int i = 0; i < specs.Count; i++)
            {
                if (c.Owner.BattleArea.Count >= 12) break;
                var spec = specs[i];
                var pool = new List<PoolCard>();
                if (fromSources && c.Self != null && c.Owner.BattleArea.Contains(c.Self)) pool.AddRange(c.Self.SourcesWhere(x => x.Type == CardType.Digimon && spec(x)).Select(x => new PoolCard(x, null)));
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, x => x.Type == CardType.Digimon && spec(x)));
                if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, x => x.Type == CardType.Digimon && spec(x)));
                pool.RemoveAll(x => !c.Engine.CanPlayByEffect(c.Owner, x.Card, OriginOf(c, x)));
                if (pool.Count == 0) continue;
                var zone = fromSources ? CardChoiceZone.DigivolutionCards : fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var pick = ChooseFromPool(c, zone, pool.OrderByDescending(x => x.Card.DP).ToList(), 1, $"[{c.Card.Name}] 코스트 없이 등장시킬 Lv.{levels[i]} 카드를 고르세요.").FirstOrDefault();
                if (pick.Card == null) continue;
                var p0 = pick;
                if (c.Engine.TryPlayByEffect(c.Owner, pick.Card, OriginOf(c, pick), () => TakeFromPool(c, p0))) played.Add(pick.Card.Name);
            }
            if (played.Count == 0) c.LastStepResolved = false;
            Emit(c, label, played.Count > 0 ? $"[{string.Join(", ", played)}] 코스트 없이 등장" : "등장시킬 카드가 없습니다");
        }, null);
    }

    /// <summary>
    /// 비용 "placing 1 each of level 4 and level 5 [Holy Beast] trait yellow Digimon cards from your trash as 1 of your [Kudamon]'s bottom digivolution cards" (EX13-071) —
    /// 그 디지몬이 다음 "it"(LastTargets). 모든 Lv.의 카드가 있어야 낼 수 있다
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? PlaceEachLevelCost36(string cost)
    {
        var m = Regex.Match(cost, @"^placing 1 each of (?<lv>level \d+(?:, level \d+)*,? and level \d+) (?<rest>.+?) from (?<from>your trash|your hand) (?<dest>as .+)$", I);
        if (!m.Success || LevelList36(m.Groups["lv"].Value) is not { } levels || EachLevelSpecs36(levels, m.Groups["rest"].Value) is not { } specs
            || ParsePlaceDest33(m.Groups["dest"].Value) is not { Other: { } destFilter }) return null;
        bool hand = m.Groups["from"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase);
        List<CardData> Zone(EffectContext c) => hand ? c.Owner.Hand : c.Owner.Trash;
        bool Enough(EffectContext c)
        {
            var used = new HashSet<CardData>(ReferenceEqualityComparer.Instance);
            foreach (var spec in specs)
            {
                var x = Zone(c).FirstOrDefault(y => !used.Contains(y) && !ReferenceEquals(y, c.Card) && spec(y));
                if (x == null) return false;
                used.Add(x);
            }
            return true;
        }
        List<CardInstance> Hosts(EffectContext c) => c.Owner.BattleArea.Where(destFilter).ToList();
        return (c => Hosts(c).Count > 0 && Enough(c),
                c =>
                {
                    var d = c.Engine.ChooseTargets(c.Owner, Hosts(c), 1, $"[{c.Card.Name}] 진화원 맨 아래에 카드를 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                    if (d == null || !Enough(c)) { c.CostFailed = true; return; }
                    for (int i = 0; i < specs.Count; i++)
                    {
                        var spec = specs[i];
                        var pool = PoolFrom(Zone(c), x => !ReferenceEquals(x, c.Card) && spec(x)).ToList();
                        var pick = ChooseFromPool(c, hand ? CardChoiceZone.Hand : CardChoiceZone.Trash, pool, 1, $"[{c.Card.Name}] [{d.TopCard.Name}]의 진화원 맨 아래에 놓을 Lv.{levels[i]} 카드를 고르세요.").FirstOrDefault();
                        if (pick.Card == null || !TakeFromPool(c, pick)) { c.CostFailed = true; return; }
                        d.AddBottomSource(pick.Card);
                        c.LastCard = pick.Card;
                    }
                    c.LastTargets = new List<CardInstance> { d };
                    c.RecalculateFieldBuffs();
                },
                _ => true, $"{(hand ? "패" : "트래시")}의 Lv.{string.Join("/", levels)} 카드 1장씩을 자신의 디지몬의 진화원 맨 아래에 놓아");
    }

    /// <summary>비용 "A and B" (둘 다 낸다 — 뒤 비용이 정한 디지몬이 다음 "it"): "trashing 3 bottom face-down cards from under any of your Tamers and placing ~" (EX13-071)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? BothCosts36(string cost, CardData card)
    {
        foreach (Match am in Regex.Matches(cost, @" and (?=(?:placing|trashing|returning|suspending|deleting|adding) )", I))
        {
            string a = cost[..am.Index].Trim(), b = cost[(am.Index + am.Length)..].Trim();
            if (!Regex.IsMatch(a, @"^[a-z]+ing ", I)) continue;
            if (ParseCost(a, card) is not { } ca || ParseCost(b, card) is not { } cb) continue;
            return (c => ca.CanPay(c) && cb.CanPay(c),
                    c =>
                    {
                        ca.Pay(c);
                        if (c.CostFailed) return;
                        cb.Pay(c);
                    },
                    c => ca.Recommend(c) && cb.Recommend(c), $"{ca.Describe.TrimEnd(',')} {cb.Describe}");
        }
        return null;
    }
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 2. 테이머를 디지몬으로도 취급 (GoldfishEngine.TamerAsDigimon.cs)
    // =====================================================================

    /// <summary>"is also treated as a 3000 DP Digimon" 뒤: ", can't digivolve, and gains ＜Blocker＞" · " that can't digivolve and gains ＜Rush＞" · ", gains ＜Rush＞ and can't digivolve" → (진화 불가, 얻는 키워드)</summary>
    private static (bool NoDigivolve, List<Keyword> Grants)? TamerDigimonTail36(string tail)
    {
        var grants = new List<Keyword>();
        foreach (Match k in Regex.Matches(tail, @"＜[^＞]+＞"))
        {
            if (ParseKeywordList(k.Value) is not { Count: 1 } list || list[0].Keyword is not { } kw) return null;
            grants.Add(kw);
        }
        bool noDigivolve = Regex.IsMatch(tail, @"\bcan't digivolve\b", I);
        string left = Regex.Replace(tail, @"＜[^＞]+＞|\bcan't digivolve\b|\bgains?\b|\bthat\b|\band\b|,", "", I).Trim();
        return left.Length == 0 ? (noDigivolve, grants) : null;
    }

    /// <summary>
    /// "For the turn, 1 of your [Marcus Damon]s is also treated as a 3000 DP Digimon and can't digivolve" (BT13-008) · "~ this Tamer is also treated as ~" (BT12-092) ·
    /// "~, can't digivolve, and gains ＜Blocker＞" (BT13-018 · BT17-087) · "~ gains ＜Rush＞ and ＜Alliance＞" (BT21-044) · "~ 12000 DP ~" (BT21-096) · AD1-021.
    /// 그 디지몬이 다음 "that Digimon"(LastTargets)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? TamerAsDigimon36(string s, CardData card, string label)
    {
        Duration dur = ParseDuration(s, out string rest);
        if (dur is Duration.None or Duration.ThisAttack) return null;
        var m = Regex.Match(rest.Trim().TrimEnd(','), @"^(?<subj>this Tamer|(?:1|one) of your \[(?<name>[^\]]+)\]s?) is also treated as an? (?<dp>\d+) DP Digimon(?<tail>.*)$", I);
        if (!m.Success || TamerDigimonTail36(m.Groups["tail"].Value) is not { } tail) return null;
        bool self = m.Groups["subj"].Value.Equals("this Tamer", StringComparison.OrdinalIgnoreCase);
        if (self && card.Type != CardType.Tamer) return null;
        string? name = m.Groups["name"].Success ? m.Groups["name"].Value : null;
        int dp = int.Parse(m.Groups["dp"].Value);
        return (c =>
        {
            CardData? tamer = null;
            if (self) tamer = c.Owner.Tamers.Contains(c.Card) ? c.Card : null;
            else
            {
                var cands = c.Owner.Tamers.Distinct().Where(t => CardNames.Is(t, name!)).OrderBy(t => c.Engine.TamerProxyOf(c.Owner, t) != null ? 1 : 0).ToList();
                tamer = cands.Count <= 1 ? cands.FirstOrDefault()
                    : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, cands, 1, upTo: false, $"[{c.Card.Name}] 디지몬으로도 취급할 테이머를 고르세요.").FirstOrDefault();
            }
            var inst = tamer == null ? null : c.Engine.TreatTamerAsDigimon(c.Owner, tamer, dp, Until(c, dur), tail.NoDigivolve, tail.Grants);
            if (inst == null) { c.LastStepResolved = false; Emit(c, label, "디지몬으로 취급할 테이머가 없습니다"); return; }
            c.LastTargets = new List<CardInstance> { inst };
            Emit(c, label, $"테이머 [{tamer!.Name}]을(를) DP {dp}의 디지몬으로도 취급");
        }, null);
    }

    /// <summary>지속 효과 "All of your [Marcus Damon]s are also treated as 12000 DP Digimon and gain ＜Rush＞" (BT25-104). 읽지 못하면 null, 읽으면 ""</summary>
    private static string? TamerAuraStatic36(string s, bool inherited, CardData card, Func<StaticContext, bool> cond, Pending pending)
    {
        var m = Regex.Match(s, @"^all of your \[(?<name>[^\]]+)\]s? are also treated as (?<dp>\d+) DP Digimon(?<tail>.*)$", I);
        if (!m.Success || card.Type != CardType.Digimon || TamerDigimonTail36(m.Groups["tail"].Value) is not { } tail || tail.NoDigivolve) return null;
        var src = inherited ? EffectSource.Inherited : EffectSource.Main;
        var aura = new TamerAura(src, m.Groups["name"].Value, int.Parse(m.Groups["dp"].Value), tail.Grants);
        var c1 = cond;
        pending.Registrations.Add(d => d.TamerAura(aura));
        pending.Registrations.Add(d => d.Static(StaticStat.Keyword, src, c => c1(c) ? 1 : 0, Keyword.TamerAsDigimonAura));
        return "";
    }
}
