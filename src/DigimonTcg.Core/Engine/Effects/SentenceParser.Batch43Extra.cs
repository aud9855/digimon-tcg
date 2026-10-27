using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.43.0 17차 묶음 — 원래 규칙이 읽지 못한 것만:
///  - 겹쳐진 맨 위 카드: 비용 "trashing the top stacked card of 1 of your [Armor Form] trait Digimon" (BT21-085) · 사건 "When the top stacked card of any of your [Armor Form] trait Digimon is trashed" (BT21-094)
///  - 트래시의 카드를 시큐리티 아래에: "Place 1 [Etemon] from your trash at the bottom of your security stack" (BT14-038 — 공식 "자신의 트래시에서 「에테몬」 1장을 시큐리티 아래에 놓는다")
///  - "for each" 지속 효과: "For each ~, all of your ~ get +1000 DP" (BT13-017 · BT17-076) · "For each color in this ~ Digimon's digivolution cards, it gets +1000 DP" (AD1-013)
///  - 셈: 상대의 메모리 (BT25-086 · BT26-090) · "for each of that Digimon's digivolution cards" (BT7-104 — 앞에서 고른 디지몬)
///  - "대신": "If no Digimon was deleted by this effect, ~ instead" (BT13-111 — 이 효과의 결과를 본 뒤) · 쉼표가 든 조건 "If you have a Digimon in play with 2 or more colors, or ~" (BT8-096 · BT8-100)
/// </summary>
internal static partial class SentenceParser
{
    // =====================================================================
    // 절: "for each" 지속 효과
    // =====================================================================

    internal static readonly Regex ForEachStaticRe43 = new(
        @"^(?<labels>(?:\[(?:All Turns|Your Turn|Opponent's Turn)\]\s*)*)For (?:each|every) (?<cnt>[^,]+), (?<subj>all of your .+?|this Digimon|it) gets? \+(?<n>\d+) DP\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool ExtraClause43(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string c0 = Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", I);
        var m = ForEachStaticRe43.Match(c0);
        if (m.Success && card.Type is CardType.Digimon or CardType.Tamer)
        {
            string labels = m.Groups["labels"].Value;
            bool yourTurn = labels.Contains("Your Turn", StringComparison.OrdinalIgnoreCase);
            bool oppTurn = labels.Contains("Opponent's Turn", StringComparison.OrdinalIgnoreCase);
            int n = int.Parse(m.Groups["n"].Value);
            if (ParseCounter(m.Groups["cnt"].Value) is not { } cnt) return false;
            var src = inherited ? EffectSource.Inherited : EffectSource.Main;
            string subj = m.Groups["subj"].Value.Trim();
            Func<StaticContext, int> value = c =>
            {
                if (yourTurn && !c.OwnersTurn || oppTurn && c.OwnersTurn) return 0;
                int k = Math.Max(0, cnt(c.Owner, c.Enemy, c.Digimon));
                if (k > 0) Diag43.Hit(ref Diag43.ForEachStatic);
                return n * k;
            };
            if (Regex.IsMatch(subj, @"^(?:this Digimon|it)$", I))
            {
                if (card.Type != CardType.Digimon) return false;
                def?.Static(StaticStat.Dp, src, value);
                return true;
            }
            string spec = Regex.Replace(subj, @"^all of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*)s?$", "all of your ${x} Digimon", I);
            if (TargetSpec.Parse(spec) is not { Opponent: false, AnySide: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } ts) return false;
            var fs = ts.Filters.ToList();
            if (card.Type == CardType.Tamer) return false;   // 테이머의 지속 효과는 TamerStatics (이 묶음은 디지몬만)
            def?.Static(StaticStat.AllyDigimonDp, src, value, subject: d => fs.All(f => f(d)));
            return true;
        }
        return false;
    }

    // =====================================================================
    // 셈 (ParseCounter 끝)
    // =====================================================================

    /// <summary>
    /// "memory your opponent has" · "point of memory your opponent has" (BT25-086 · BT26-090 — 상대 쪽 메모리, 0 미만이면 0) ·
    /// "color in this [Blue Flare] or [Xros Heart] trait Digimon's digivolution cards" (AD1-013 — 그 특징을 가진 이 디지몬의 진화원의 색 수)
    /// </summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter43(string counter)
    {
        string s = counter.Trim().TrimEnd('.');
        Match m;
        if (Regex.IsMatch(s, @"^(?:point of )?memory (?:your opponent has|on your opponent's side)$", I))
            return (_, e, _) =>
            {
                int k = Math.Max(0, e.MemoryProbe43?.Invoke() ?? 0);
                if (k > 0) Diag43.Hit(ref Diag43.OppMemoryCount);
                return k;
            };
        // "other Digimon with the [Royal Knight] trait or [Sistermon] in their names you have in play" (BT13-017 — "other"는 이 디지몬을 뺀다: 대상 조건은 "other" 없이 읽는다)
        if ((m = Regex.Match(s, @"^other (?<x>(?:[^,]*? )?Digimon with .+?) you have(?: in play)?$", I)).Success
            && TargetSpec.Parse("all of your " + m.Groups["x"].Value) is { ContextFilters.Count: 0, Opponent: false, AnySide: false } spec)
            return (o, _, self) => o.BattleArea.Count(d => !ReferenceEquals(d, self) && d.TamerProxyOf == null && spec.Filters.All(f => f(d)));
        if ((m = Regex.Match(s, @"^colou?r in (?:this (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon's|this Digimon's|its) digivolution cards$", I)).Success)
        {
            var traits = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            return (_, _, self) => self == null || traits != null && !traits.Any(t => TargetSpec.HasTrait(self.TopCard, t)) ? 0
                : self.FaceUpSources.SelectMany(x => x.AllColors).Distinct().Count();
        }
        return null;
    }

    // =====================================================================
    // 비용 (ParseCost 끝)
    // =====================================================================

    /// <summary>"trashing the top stacked card of 1 of your [Armor Form] trait Digimon" (BT21-085 — 공식 "특징 「아머체」를 가진 자신의 디지몬 1마리에 겹쳐져 있는 카드를 위에서부터 1장 파기하는 것으로")</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost43(string cost, CardData card)
    {
        // "returning 4 [Negamon] from your trash or your Digimon's digivolution cards to the bottom of the Digi-Egg deck" ([육성] EX9-057 — 트래시 → 진화원 순으로 고른다)
        var rm = Regex.Match(cost.Trim(), $@"^returning (?<n>\d+) (?<x>{TargetSpec.NameList}) from your trash or your Digimon's digivolution cards to the bottom of (?:the|your) Digi-Egg deck$", I);
        if (rm.Success)
        {
            int n = int.Parse(rm.Groups["n"].Value);
            var xs = TargetSpec.Names(rm.Groups["x"].Value);
            bool Is(CardData x) => xs.Any(y => CardNames.Is(x, y));
            List<(CardData Card, CardInstance? From)> NegaPool(EffectContext c) =>
                c.Owner.Trash.Where(Is).Select(x => (x, (CardInstance?)null))
                    .Concat(c.Owner.BattleArea.SelectMany(d => d.FaceUpSources.Where(Is).Select(x => (x, (CardInstance?)d)))).ToList();
            return (c => NegaPool(c).Count >= n,
                    c =>
                    {
                        var pool = NegaPool(c);
                        if (pool.Count < n) { c.CostFailed = true; return; }
                        var list = c.Owner.DigitamaDeck.Reverse().ToList();   // 아래 → 위
                        foreach (var (x, from) in pool.Take(n))
                        {
                            bool removed = from == null ? c.Owner.Trash.Remove(x) : from.TakeSource(y => ReferenceEquals(y, x)) != null;
                            if (!removed) { c.CostFailed = true; return; }
                            list.Insert(0, x);
                        }
                        c.Owner.DigitamaDeck = new Stack<CardData>(list);
                        c.RecalculateFieldBuffs();
                        Diag43.Hit(ref Diag43.Breeding);
                    },
                    _ => true, $"{n}장을 디지타마 덱 아래로 되돌려");
        }
        var m = Regex.Match(cost.Trim(), @"^trashing the top stacked card of (?:1|one) of your (?<t>.+)$", I);
        if (!m.Success || OwnDigimonFilter42(m.Groups["t"].Value) is not { } filter) return null;
        // 진화원이 있는 디지몬만 (맨 위 카드를 파기해도 디지몬이 남는다)
        List<CardInstance> Pool(EffectContext c) => c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && d.EvolutionSources.Count > 0 && filter(d)).ToList();
        return (c => Pool(c).Count > 0,
                c =>
                {
                    var pool = Pool(c).OrderByDescending(d => d.EvolutionSources.Count).ThenBy(d => d.TopCard.Level).ToList();
                    var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 맨 위 카드를 파기할 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                    if (d == null || c.Engine.TrashTopStackedCards42(c.Owner, d, 1) == 0) { c.CostFailed = true; return; }
                    Diag43.Hit(ref Diag43.TopStackedCost);
                },
                c => Pool(c).Any(d => d.EvolutionSources.Count >= 2),
                "디지몬 1마리의 맨 위 카드를 파기하여");
    }

    // =====================================================================
    // 사건 (ParseEventPhrase 끝)
    // =====================================================================

    /// <summary>"the top stacked card of any of your [Armor Form] trait Digimon is trashed" (BT21-094 — 공식 "특징 「아머체」를 가진 자신의 디지몬에 겹쳐져 있는 카드가 위에서부터 파기되었을 때")</summary>
    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase43(string ev)
    {
        var m = Regex.Match(ev.Trim(), @"^the top stacked card of (?:any of |one of |1 of )?your (?<t>.+?) (?:is|gets) trashed$", I);
        // "When an Option card with the [Royal Knight] trait is placed in the battle area" ([육성] BT13-007 — 공식 "특징 「로얄 나이츠」를 가진 옵션 카드가 배틀 에어리어에 놓였을 때")
        var om = Regex.Match(ev.Trim(), $@"^an? Option card (?:with the (?<x>{TargetSpec.NameList}) traits? )?(?:is|are) placed in (?:the|your) battle area$", I);
        if (om.Success)
        {
            var xs = om.Groups["x"].Success ? TargetSpec.Names(om.Groups["x"].Value) : null;
            return (new List<EffectTrigger> { EffectTrigger.AllyOptionPlaced }, xs == null ? null : c => c.TriggerCard != null && xs.Any(x => TargetSpec.HasTrait(c.TriggerCard, x)));
        }
        if (m.Success && OwnDigimonFilter42(m.Groups["t"].Value) is { } who)
            return (new List<EffectTrigger> { EffectTrigger.AllyTopStackedTrashed43 },
                c => c.Other != null && (who(c.Other) || c.TriggerCard != null && who(new CardInstance(c.TriggerCard, 0))));   // 파기되기 전의 맨 위 카드로도 본다
        return null;
    }

    // =====================================================================
    // 조건 (ParseEventCondition 끝)
    // =====================================================================

    /// <summary>"you have a Digimon with 2 or more colors or with 2 or more colors in its digivolution cards" (BT8-096 · BT8-100 — 공식 "2색 이상인 자신의 디지몬 또는 진화원에 2색 이상인 카드를 가진 자신의 디지몬이 있다면")</summary>
    private static Func<EffectContext, bool>? ParseEventCondition43(string cond)
    {
        if (Regex.IsMatch(cond.Trim(), @"^you have a Digimon with 2 or more colou?rs or with 2 or more colou?rs in its digivolution cards$", I))
            return c =>
            {
                bool ok = c.Owner.BattleArea.Any(d => d.TamerProxyOf == null && (d.Colors.Count >= 2 || d.FaceUpSources.Any(s => s.AllColors.Count >= 2)));
                if (ok) Diag43.Hit(ref Diag43.InsteadCompound);
                return ok;
            };
        // "If no Digimon was deleted by this effect⟦r43⟧, B." (MergeSentences43이 "대신"을 뗀 문장 — 원래 조건 + 집계)
        if (cond.EndsWith(ResultMark43, StringComparison.Ordinal) && ParseEventCondition(cond[..^ResultMark43.Length].Trim()) is { } baseCond)
            return c =>
            {
                bool ok = baseCond(c);
                if (ok) Diag43.Hit(ref Diag43.InsteadResult);
                return ok;
            };
        return null;
    }

    /// <summary>MergeSentences43이 "대신"을 뗀 조건 표시 (집계용 — 렌더러는 이 합친 문장을 쓰지 않는다)</summary>
    internal const string ResultMark43 = " ⟦r43⟧";

    // =====================================================================
    // 문장 목록 (MergeSentences 끝)
    // =====================================================================

    internal static readonly Regex InsteadResultRe43 = new(@"^If (?<c>no (?:opponent's )?Digimon (?:was|were) deleted by this effect|this effect didn't delete), (?<b>.+?),? instead\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// "A. If no Digimon was deleted by this effect, B instead." (BT13-111 — 공식 "이 효과로 소멸하지 않았다면, ~") — A를 처리한 뒤 그 결과로 B ("대신"을 뗀 조건 문장) ·
    /// "If you have a Digimon in play with 2 or more colors, or with 2 or more colors in its digivolution cards, B instead." — 조건의 쉼표를 뗀다 (BT8-096 · BT8-100)
    /// </summary>
    private static List<string> MergeSentences43(List<string> sentences)
    {
        if (!sentences.Any(s => Regex.IsMatch(s, @"\binstead\b", I))) return sentences;
        var result = new List<string>();
        for (int i = 0; i < sentences.Count; i++)
        {
            string s = sentences[i];
            if (i > 0 && InsteadResultRe43.Match(s.Trim()) is { Success: true } rm)
            {
                result.Add($"If {rm.Groups["c"].Value}{ResultMark43}, {rm.Groups["b"].Value.Trim().TrimEnd('.')}.");
                continue;
            }
            // "Until ~, 1 of your opponent's Digimon gains ＜Security A. -1＞. If ~, 3 of your opponent's Digimon gain it instead." (BT10-099) — "it"을 앞 문장의 키워드 · 기간으로
            if (i > 0 && Regex.Match(s.Trim(), @"^If (?<c>[^,]+), (?<n>\d+) of your opponent's Digimon gain it instead\.?$", I) is { Success: true } gm
                && Regex.Match(result[^1].Trim(), @"^(?<dur>Until [^,]+, )?1 of your opponent's Digimon gains (?<k>＜[^＞]+＞)(?:\s*\([^)]*\))?(?<dur2> until [^.]+| for the turn)?\.?$", I) is { Success: true } pm)
            {
                result.Add($"If {gm.Groups["c"].Value}, {pm.Groups["dur"].Value}{gm.Groups["n"].Value} of your opponent's Digimon gain {pm.Groups["k"].Value}{pm.Groups["dur2"].Value} instead.");
                continue;
            }
            if (i > 0)
                s = Regex.Replace(s, @"^If you have a Digimon in play with 2 or more colou?rs, or with 2 or more colou?rs in its digivolution cards, ",
                    "If you have a Digimon with 2 or more colors or with 2 or more colors in its digivolution cards, ", I);
            result.Add(s);
        }
        return result;
    }

    // =====================================================================
    // 문장 (StepFallback43)
    // =====================================================================

    private static Step? StepExtra43(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
        Match m;
        // "＜Draw 1＞ for each of that Digimon's digivolution cards" (BT7-104 — 공식 "그 디지몬이 갖는 진화원 1장마다 《1 드로우》") — 앞 문장에서 고른 디지몬
        if ((m = Regex.Match(s, @"^(?<core>.+?) for (?:each|every) of that Digimon's digivolution cards$", I)).Success)
        {
            var inner = ParseAction(m.Groups["core"].Value, card, triggers);
            if (inner.Error != null || inner.Act == null) return null;
            var act = inner.Act;
            return new Step(null, c =>
            {
                var d = c.LastTargets.FirstOrDefault();
                int k = d == null ? 0 : Math.Min(d.EvolutionSources.Count, 20);
                if (k > 0) Diag43.Hit(ref Diag43.Misc);
                for (int i = 0; i < k; i++) act(c);
            }, false, sentence.Trim());
        }
        // "Place the top card of your Digi-Egg deck as this Digimon's bottom digivolution card and delete all of your Digimon" ([육성] EX6-006) — 두 동작
        if ((m = Regex.Match(s, @"^(?<a>Place the top card of your Digi-Egg deck .+?) and (?<b>delete .+)$", I)).Success)
        {
            var ra = ParseStep(m.Groups["a"].Value, card, triggers);
            var rb = ParseStep(char.ToUpperInvariant(m.Groups["b"].Value[0]) + m.Groups["b"].Value[1..], card, triggers);
            if (ra.Error == null && ra.Step != null && rb.Error == null && rb.Step != null)
            {
                var (sa, sb) = (ra.Step, rb.Step);
                return new Step(null, c => { RunStep(sa, c); RunStep(sb, c); }, false, sentence.Trim());
            }
        }
        // "1 of your [TS] trait Digimon gains +1000 DP for the turn for each memory your opponent has" (BT25-086 — "gains ~ DP")
        if ((m = Regex.Match(s, @"^(?<t>.+?) gains (?<dp>[+-]\d+ DP .+)$", I)).Success)
        {
            var r = ParseStep($"{m.Groups["t"].Value} gets {m.Groups["dp"].Value}", card, triggers);
            if (r.Error == null && r.Step != null) return r.Step with { Text = sentence.Trim() };
        }
        // "with the 13000 DP or more" (BT13-111 원문의 "the")
        if (Regex.IsMatch(s, @"\bwith the \d+ DP or (?:less|more|lower|higher)\b", I))
        {
            var r = ParseStep(Regex.Replace(s, @"\bwith the (?=\d+ DP or )", "with ", I), card, triggers);
            if (r.Error == null && r.Step != null) return r.Step with { Text = sentence.Trim() };
        }
        return null;
    }

    /// <summary>앞 문장 a를 코스트 k만큼 더 줄인 문장 — "use 1 Option card ~ from your hand" (BT26-090)</summary>
    internal static string? ReducedSentence43(string a, int k)
    {
        a = a.Trim().TrimEnd('.');
        if (Regex.IsMatch(a, @"\bwithout paying\b", I)) return null;
        if (Regex.IsMatch(a, @"\buse \d+ .+? from your hand$", I)) return $"{a} with the use cost reduced by {k}";
        return null;
    }

    // =====================================================================
    // 동작 (ParseAction43)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ActionExtra43(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        Match m;
        // "Place 1 [Etemon] from your trash at the bottom of your security stack" (BT14-038 — 공식 "자신의 트래시에서 「에테몬」 1장을 시큐리티 아래에 놓는다")
        if ((m = Regex.Match(s, @"^(?:You may )?[Pp]lace (?<pick>\d+ .+?) from your trash (?:at the (?<pos>bottom|top) of|on (?<pos>top) of|under) your security stack(?: face (?<fd>up|down))?$", I)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } pick)
        {
            var (count, match) = pick;
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                var pool = c.Owner.Trash.Where(match).Distinct().OrderByDescending(x => x.Level).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "트래시에 놓을 카드가 없습니다"); return; }
                var picks = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Trash, pool, Math.Min(count, pool.Count), upTo: false, $"[{c.Card.Name}] 시큐리티 {(top ? "위" : "아래")}에 놓을 카드를 고르세요.").Select(i => pool[i]).ToList();
                foreach (var x in picks)
                {
                    if (!c.Owner.Trash.Remove(x)) continue;
                    if (top) c.Owner.SecurityStack.Insert(0, x); else c.Owner.SecurityStack.Add(x);
                    c.Engine.NotifySecurityAdded(c.Owner, x);
                    Diag43.Hit(ref Diag43.TrashToSecurityBottom);
                    Emit(c, label, $"트래시의 [{x.Name}]을(를) 시큐리티 {(top ? "위" : "아래")}에 놓았습니다");
                }
            }, null);
        }
        // "Place the top card of your Digi-Egg deck as this Digimon's bottom digivolution card" ([육성] EX6-006 — 공식 "자신의 디지타마 덱 위에서부터 1장을 이 디지몬의 진화원 아래에 놓는다")
        if (Regex.IsMatch(s, @"^Place the top card of your Digi-Egg deck (?:as this Digimon's bottom digivolution card|under this Digimon as its bottom digivolution card)$", I))
            return (c =>
            {
                if (c.Self == null || c.Owner.DigitamaDeck.Count == 0 || !(c.Owner.BattleArea.Contains(c.Self) || ReferenceEquals(c.Owner.BreedingArea, c.Self)))
                { c.LastStepResolved = false; Emit(c, label, "놓을 카드가 없습니다"); return; }
                var egg = c.Owner.DigitamaDeck.Pop();
                c.Self.AddBottomSource(egg);
                c.RecalculateFieldBuffs();
                Diag43.Hit(ref Diag43.Breeding);
                Emit(c, label, $"디지타마 덱 위의 [{egg.Name}]을(를) 진화원 아래에 놓았습니다");
            }, null);
        // "use 1 Option card with the [TS] trait from your hand (with the use cost reduced by 2)" (BT26-090 — 코스트를 내고 사용, 메모리를 상대 쪽으로 넘기지 않는 것만)
        if ((m = Regex.Match(s, @"^(?:you may )?use (?<pick>\d+ .*?Option cards?.*?) from your hand(?: with the (?:use |memory )?cost reduced by (?<red>\d+))?$", I)).Success
            && ParseCardPick(m.Groups["pick"].Value) is { } opick)
        {
            var (count, match) = opick;
            int red = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : 0;
            return (c =>
            {
                int mem = Math.Max(0, c.OwnerMemory);
                bool Usable(CardData x) => x.Type == CardType.Option && match(x) && !ReferenceEquals(x, c.Card) && ActionValidator.MissingOptionColors(c.Owner, x).Count == 0
                                           && !EffectRules.OptionUseBlocked(c.State, c.Owner) && Math.Max(0, x.PlayCost - red) <= mem;
                var pool = c.Owner.Hand.Where(Usable).Distinct().OrderByDescending(x => x.PlayCost).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "코스트를 내고 사용할 수 있는 옵션 카드가 없습니다"); return; }
                var picks = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, pool, Math.Min(count, pool.Count), upTo: false, $"[{c.Card.Name}] 사용할 옵션 카드를 고르세요 (코스트 -{red}).").Select(i => pool[i]).ToList();
                foreach (var x in picks)
                {
                    if (!c.Owner.Hand.Remove(x)) continue;
                    int cost = Math.Max(0, x.PlayCost - red);
                    c.Engine.UseOptionPaid43(c.Owner, x, cost);
                    Diag43.Hit(ref Diag43.Misc);
                }
            }, null);
        }
        return null;
    }
}
