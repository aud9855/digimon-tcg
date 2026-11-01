using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.50.0 미지원 문장 24차 묶음: 원래 규칙(1~23차)이 읽지 못한 절 · 문장 · 동작 · 조건만 24차 규칙으로 다시 읽는다.
///  - 같은 뜻 다른 표기 (ClauseRules50): "When ~ suspend, or effects trash cards from under your Tamers, ~" (BT26-044 · BT26-049 · BT26-091 · ST24-11 — 쉼표가 사건을 끊었다) ·
///    "all of your opponent's Digimon can only attack suspended Digimon" (EX11-011 · EX8-016) · P-024 (덱 아래로 · 진화원은 파기) ·
///    BT26-049 "1 play cost or use cost 3 or lower ~ card ~. For each suspended Digimon or Tamer, add 1 to the cost maximum" · EX6-073 · EX6-015 · BT18-088
///  - 동작 (ActionExtra50): 상대가 토큰을 등장 ("they play 1 [Petrification] Token" BT21-029 · BT24-017 · EX11-012) · 색 / 명칭이 서로 다른 카드를 테이머 아래에 (AD1-020 · AD1-023 · BT18-088 · BT18-095 · BT18-097) ·
///    아래에 카드가 N장 이상 있는 테이머를 진화 조건을 무시하고 진화 (BT18-095 · BT18-097) · 이 디지몬의 【시큐리티】 효과 1개를 발휘 (EX13-036) ·
///    소멸시킨 수만큼 줄여 시큐리티 파기 (EX6-073) · 다른 디지몬을 진화원 아래에 놓은 만큼 Lv. 상한을 늘려 패로 (EX6-015)
///  - 고르는 효과: "· ~" 선택지 · "When ~, by ~, activate 1 of the effects below:" (EX12-066 ~ EX12-068)
///  - 비용 (ParseCost50): P-024 · EX6-073 / 조건 (ParseEventCondition50) · 셈 (ParseCounter50) · 지속 (ParseStatic50 — 토큰의 "This Digimon can't suspend")
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch50.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth50;
    [ThreadStatic] private static int _fallbackDepth50;
    [ThreadStatic] private static int _gate50;
    internal static bool Gate50 => _gate50 > 0;

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules50 =
    {
        // "When any of your opponent's Digimon or Tamers suspend, or effects trash cards from under your Tamers, ~" (BT26-044 · BT26-049 · BT26-091 · ST24-11) — 사건 둘 사이의 쉼표
        s => Regex.Replace(s, @"\b(?<a>Digimon or Tamers suspend), or (?<b>effects trash cards from under (?:your Tamers|this Tamer)),", "${a} or ${b},", RegexOptions.IgnoreCase),
        // "you may play or use 1 play cost or use cost 3 or lower [DATA SQUAD] trait card from your hand without paying the cost. For each suspended Digimon or Tamer, add 1 to the cost maximum." (BT26-049)
        s => Regex.Replace(s, @"\bplay or use 1 play cost or use cost (?<n>\d+) or (?:lower|less) (?<x>\[[^\]]+\]) trait card from your hand without paying the cost\. For each (?<cnt>suspended Digimon or Tamer), add (?<k>\d+) to the cost maximum\.",
            "play or use 1 ${x} trait card with a play cost of ${n} or less from your hand without paying the cost. For each ${cnt}, add ${k} to this effect's play cost maximum.", RegexOptions.IgnoreCase),
        // "[Opponent's Turn] While this Digimon is suspended, all of your opponent's Digimon can only attack suspended Digimon." (EX11-011 · EX8-016) —
        //  어택 대상은 원래 플레이어 또는 레스트 상태의 디지몬뿐 (종합 룰 6-3-1) → 플레이어에게 어택할 수 없다
        s => Regex.Replace(s, @"\ball of your opponent's Digimon can only attack suspended Digimon\b", "your opponent's Digimon can't attack players", RegexOptions.IgnoreCase),
        // P-024 "place 1 of your [Agumon] at the bottom of its owner's deck to ＜Draw 3＞ (~) Trash that Digimon's digivolution cards." — 비용 (~하는 것으로): 맨 위 카드는 덱 아래 · 진화원은 트래시
        s => Regex.Replace(s, @"\bplace 1 of your (?<n>\[[^\]]+\]) at the bottom of its owner's deck to (?<d>＜Draw \d+＞)(?<ex> \([^)]*\))?\s*Trash that Digimon's digivolution cards\.",
            "by returning 1 of your ${n} to the bottom of the deck and trashing its digivolution cards, ${d}${ex}.", RegexOptions.IgnoreCase),
        // EX6-073 "~ delete 7 of your opponent's Digimon or Tamers. Then, trash the top 7 cards of your opponent's security stack. For each card deleted by this effect, reduce the cards trashed by 1."
        s => Regex.Replace(s, @"\bdelete (?<n>\d+) of your opponent's Digimon or Tamers\. Then, trash the top (?<k>\d+) cards of your opponent's security stack\. For each card deleted by this effect, reduce the cards trashed by 1\.",
            "delete ${n} of your opponent's Digimon or Tamers, then trash the top ${k} cards of your opponent's security stack minus 1 for each card deleted.", RegexOptions.IgnoreCase),
        // EX6-015 "You may place up to 3 of your other blue Digimon as this Digimon's bottom digivolution cards. Then, return all other level 4 or lower Digimon to the hand. For each card placed in this Digimon's digivolution cards, add 1 to the level this effect may return."
        s => Regex.Replace(s, @"\bYou may place up to (?<n>\d+) of your other (?<spec>[^.]*?)Digimon as this Digimon's bottom digivolution cards\. Then, return all other level (?<lv>\d+) or lower Digimon to the hand\. For each card placed in this Digimon's digivolution cards, add 1 to the level this effect may return\.",
            "Place up to ${n} of your other ${spec}Digimon as this Digimon's bottom digivolution cards ⟦then-return-other-level:${lv}⟧.", RegexOptions.IgnoreCase),
        // BT18-088 "You may place up to 1 [Hybrid] trait card with different names from your trash under this Tamer. For each of your other Tamers, add 2 to the maximum number this effect may place."
        s => Regex.Replace(s, @"\b(?<a>place up to \d+ [^.]+? with different (?:names|colou?rs) from [^.]+? under (?:this Tamer|1 of your Tamers))\. For each of your other Tamers, add (?<k>\d+) to the maximum number this effect may place\.",
            "${a} ⟦plus-other-tamers:${k}⟧.", RegexOptions.IgnoreCase),
        // LM-020 "[Start of Opponent's Turn] Declare 1 card category. Then, reveal the top card of your opponent's deck. If it's of that category, this Digimon isn't affected by that category's effects,
        //  other than this Digimon's effects, for the turn. Return the revealed cards to the top or bottom of your opponent's deck." — 한 동작으로
        s => Regex.Replace(s, @"\bDeclare 1 card category\. Then, reveal the top card of your opponent's deck\. If it's of that category, this Digimon isn't affected by that category's effects, other than this Digimon's effects, for the turn\. Return the revealed cards? to the top or bottom of your opponent's deck\.",
            "Declare 1 card category and reveal the top card of your opponent's deck for category immunity.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite50(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules50) s = f(s);
        return s;
    }

    private static string? TryParse50(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse49(rawClause, inherited, card, def);
        if (err == null || _clauseDepth50 > 0) return err;
        _clauseDepth50++;
        _gate50++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (TryParse49(rawClause, inherited, card, null) == null)
            {
                if (def != null) TryParse49(rawClause, inherited, card, def);
                return null;
            }
            string alt = ClauseRewrite50(rawClause);
            if (alt == rawClause || TryParse49(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse49(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth50--;
            _gate50--;
            if (def != null) WrapGate50(def, before);
        }
    }

    /// <summary>24차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate50(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate50++; try { inner(c); } finally { _gate50--; } } };
        }
    }

    // =====================================================================
    // 고르는 효과 — "· ~" 선택지 · 사건 시점 "When ~, by ~, activate 1 of the effects below:" (EX12-066 ~ EX12-068)
    // =====================================================================

    /// <summary>고르는 효과: 원래 규칙이 읽지 못했을 때만 24차 규칙으로</summary>
    private static string? TryParseChoice50(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoice48(rawHeader, rawOptions, inherited, card, def);
        if (err == null || _gate50 > 0) return err;
        _gate50++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            // "· ~" (EX12-066 ~ EX12-068 영어 데이터의 다른 기호) → "・~"
            var opts = rawOptions.Select(o => Regex.Replace(o, @"^\s*·\s*", "・")).ToList();
            if (!opts.SequenceEqual(rawOptions) && TryParseChoice48(rawHeader, opts, inherited, card, null) == null)
            {
                if (def != null) TryParseChoice48(rawHeader, opts, inherited, card, def);
                return null;
            }
            string? evErr = EventChoice50(rawHeader, opts, inherited, card, null);
            if (evErr != null) return err;
            if (def != null) EventChoice50(rawHeader, opts, inherited, card, def);
            return null;
        }
        finally
        {
            _gate50--;
            if (def != null) WrapGate50(def, before);
        }
    }

    /// <summary>
    /// "[Your Turn] When one of your Digimon with [Gammamon] in its text or the [VB] trait attacks, by suspending this Tamer, activate 1 of the effects below:" + 선택지 —
    /// 사건 → (비용) → 선택지 1개. 선택지의 "It" = 사건의 디지몬 (원래 규칙이 "When ~, it may digivolve ~"를 읽는 것과 같다)
    /// </summary>
    private static string? EventChoice50(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string header = Normalize(rawHeader);
        var prefix = Regex.Match(header, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        bool once = false, twice = false;
        var qualifier = Qualifier.None;
        foreach (Match l in Regex.Matches(prefix.Groups[1].Value, @"\[([^\]]+)\]"))
        {
            string label = l.Groups[1].Value.Trim();
            if (label.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase)) once = true;
            else if (label.Equals("Twice Per Turn", StringComparison.OrdinalIgnoreCase)) twice = true;
            else if (label.Equals("Your Turn", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.YourTurn;
            else if (label.Equals("Opponent's Turn", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.OpponentsTurn;
            else if (label.Equals("All Turns", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.AllTurns;
            else return $"미지원 시점 [{label}]";
        }
        var body = Regex.Match(prefix.Groups[2].Value, @"^When (?<ev>[^,]+), (?:by (?<cost>[^,]+), )?(?<may>you may )?activate (?:1|one) of the (?:effects below|following effects)[:.]?$", RegexOptions.IgnoreCase);
        if (!body.Success) return "문장 파서(3단계) 대상: 고르는 효과";
        bool may = body.Groups["may"].Success;   // "you may activate ~" (BT13-059) — 발휘하지 않을 수 있다
        if (ParseEventPhrase(body.Groups["ev"].Value) is not { } ev) return $"미지원 발동 조건 (When {body.Groups["ev"].Value})";
        var triggers = ev.Triggers.ToList();
        (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? cost = null;
        if (body.Groups["cost"].Success && (cost = ParseCost(body.Groups["cost"].Value, card)) == null) return $"미지원 비용 (By {body.Groups["cost"].Value})";
        var options = new List<Step[]>();
        foreach (var raw in rawOptions)
        {
            var steps = new List<Step>();
            foreach (var sentence in MergeRevealSentences(SplitSentences(Normalize(raw))))
            {
                var step = ParseStep(sentence, card, triggers);
                if (step.Error != null) return step.Error;
                if (step.Step != null) steps.Add(step.Step);
            }
            if (steps.Count == 0) return "빈 선택지";
            options.Add(steps.ToArray());
        }
        var optionTexts = rawOptions.Select(o => Normalize(o).TrimStart('・').Trim()).ToArray();
        var values = optionTexts.Select(t => SplitSentences(t).Sum(AiEvaluator.EstimateSentenceValue)).ToArray();
        int recommended = Array.IndexOf(values, values.Max());
        var feasible = optionTexts.Select(OptionFeasible51).ToArray();   // v0.51.0 AI가 할 수 없는 선택지(진화할 카드가 없는 진화 등)를 고르지 않게
        int Recommend51(EffectContext c)
        {
            var able = Enumerable.Range(0, values.Length).Where(i => feasible[i] == null || feasible[i]!(c)).ToList();
            if (able.Count == 0) return recommended;
            Diag46.Hit("eventChoiceFeasible51");
            return able.OrderByDescending(i => values[i]).First();
        }
        bool AnyFeasible51(EffectContext c) => Enumerable.Range(0, values.Length).Any(i => feasible[i] == null || feasible[i]!(c));
        Action<EffectContext> resolve = c =>
        {
            if (cost is { } k)
            {
                if (!PayOrSkip(c, k.CanPay, x => k.Recommend(x) && AnyFeasible51(x), k.Describe)) return;   // v0.51.0 할 수 있는 선택지가 없으면 비용을 내지 않는 쪽을 추천
                k.Pay(c);
                if (c.CostFailed) return;
            }
            else if (may && !c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 효과를 발휘할까요? (아래 효과 중 1개)", recommended: true)) return;
            int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 발휘할 효과를 1개 고르세요.", optionTexts, Recommend51(c));
            Diag46.Hit("eventChoice50");
            foreach (var s in options[Math.Clamp(pick, 0, options.Count - 1)])
                RunStep(s, c);
        };
        var evCond = ev.Condition;
        Func<EffectContext, bool>? when = qualifier switch
        {
            Qualifier.YourTurn => c => c.Engine.IsTurnOf(c.Owner) && (evCond == null || evCond(c)),
            Qualifier.OpponentsTurn => c => !c.Engine.IsTurnOf(c.Owner) && (evCond == null || evCond(c)),
            _ => evCond,
        };
        if (def == null) return null;
        foreach (var t in triggers)
        {
            var tr = t;
            if (inherited) def.Inherited(tr, resolve, when, once, twice);
            else def.On(tr, resolve, when, once, twice);
        }
        return null;
    }

    /// <summary>
    /// v0.51.0 고르는 효과의 선택지를 지금 할 수 있는지 (AI 추천용 — null이면 늘 할 수 있다고 본다):
    /// "It may digivolve into ~ in the hand ~" = 사건의 디지몬이 패의 그 카드로 진화할 수 있다 · "use 1 Option card ~ from your hand" = 패에 그 옵션이 있다
    /// </summary>
    private static Func<EffectContext, bool>? OptionFeasible51(string text)
    {
        Match m;
        if ((m = Regex.Match(text, @"\bdigivolve into (?<pick>.+?) in (?:the|your) hand\b", I)).Success)
        {
            string p0 = Regex.Replace(m.Groups["pick"].Value, @"^an? ", "", I);
            Func<CardData, bool> match = (ParseCardPick("1 " + p0) ?? ParseCardPick(m.Groups["pick"].Value))?.Match ?? (x => x.Type == CardType.Digimon);   // 읽지 못한 조건이면 패의 디지몬 카드 아무거나 (추천용)
            return c =>
            {
                var bases = c.LastTargets.Concat(c.Other != null ? new[] { c.Other } : Array.Empty<CardInstance>()).Where(d => c.Owner.BattleArea.Contains(d)).ToList();
                if (bases.Count == 0) bases = c.Owner.BattleArea.ToList();
                return bases.Any(d => c.Owner.Hand.Any(x => match(x) && ActionValidator.ValidateEvolution(d, x).CanExecute));
            };
        }
        if ((m = Regex.Match(text, @"\buse (?:1|an?|one) Option card (?<pick>.*?)\s*from your hand\b", I)).Success
            && ParseCardPick(("1 Option card " + m.Groups["pick"].Value).Trim()) is { } op)
        {
            var match = op.Match;
            return c => c.Owner.Hand.Any(x => x.Type == CardType.Option && match(x));
        }
        return null;
    }

    // =====================================================================
    // 조건 · 셈 · 비용 · 지속
    // =====================================================================

    private static Func<EffectContext, bool>? ParseEventCondition50(string cond)
    {
        if (!Gate50) return null;
        string s = cond.Trim();
        Match m;
        // "there are 4 or more [Hybrid] trait cards under this Tamer" (AD1-020 · AD1-023 — 테이머 아래 카드는 한 묶음: "this Tamer has N cards under it"과 같다)
        if ((m = Regex.Match(s, @"^there are (?<n>\d+) or more (?<x>\[[^\]]+\]) trait cards under this Tamer$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var ts = TargetSpec.Names(m.Groups["x"].Value);
            return c => c.Owner.UnderTamers.Count(x => !c.Owner.IsFaceDownUnderTamer(x) && ts.Any(t => TargetSpec.HasTrait(x, t))) >= n && Diag46.HitTrue("condUnderTamerTrait50");
        }
        // "attacking a Digimon with DP higher than this Digimon" (BT14-048 — 【어택 시】 어택 대상 디지몬의 DP가 이 디지몬보다 높다)
        if (Regex.IsMatch(s, @"^attacking a Digimon with (?:DP higher than|more DP than|higher DP than) this Digimon$", I))
            return c => c.Self != null && c.Other != null && !ReferenceEquals(c.Other, c.Self) && c.Enemy.BattleArea.Contains(c.Other) && c.Other.GetCurrentDP() > c.Self.GetCurrentDP() && Diag46.HitTrue("condAttackingHigherDp50");
        return null;
    }

    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter50(string counter)
    {
        if (!Gate50) return null;
        // "each suspended Digimon or Tamer" (BT26-049 — 양쪽의 레스트 상태 디지몬과 테이머)
        if (Regex.IsMatch(counter.Trim(), @"^suspended Digimon or Tamers?$", I))
        {
            static int Side(PlayerZones z) => z.BattleArea.Count(d => d.TamerProxyOf == null && d.IsSuspended) + z.Tamers.Count(t => !z.HasActiveTamer(t));
            return (o, e, _) => Side(o) + Side(e);
        }
        return null;
    }

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost50(string cost, CardData card)
    {
        if (!Gate50) return null;
        Match m;
        // "returning 1 of your [Agumon] to the bottom of the deck and trashing its digivolution cards" (P-024 — 맨 위 카드는 덱 아래 · 진화원은 파기)
        if ((m = Regex.Match(cost, @"^returning 1 of your (?<n>\[[^\]]+\]) to the bottom of the deck and trashing its digivolution cards$", I)).Success)
        {
            string name = TargetSpec.Names(m.Groups["n"].Value)[0];
            IEnumerable<CardInstance> Able(EffectContext c) => c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && CardNames.Is(d.TopCard, name));
            return (c => Able(c).Any(),
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Able(c).OrderBy(x => x.TopCard.Level).ThenBy(x => x.EvolutionSources.Count).ToList(), 1, $"[{c.Card.Name}] 비용으로 덱 아래로 되돌릴 [{name}]을(를) 고르세요 (진화원은 파기).", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) { c.CostFailed = true; return; }
                        c.Engine.BottomDeckAsCost(c.Owner, d, trashSources50: true);
                        Diag46.Hit("bottomDeckTrashSources50");
                    },
                    _ => true, $"[{name}] 1마리를 덱 아래로 되돌리고 (진화원은 파기)");
        }
        // "returning 7 cards with different names and the [Seven Great Demon Lords] trait from this Digimon's digivolution cards to the bottom of the deck" (EX6-073)
        if ((m = Regex.Match(cost, @"^returning (?<n>\d+) cards with different names and (?<rest>.+?) from this Digimon's digivolution cards to the bottom of the deck$", I)).Success
            && ParseCardPick("1 card with " + m.Groups["rest"].Value) is { } pk)
        {
            int n = int.Parse(m.Groups["n"].Value);
            List<CardData> Distinct(EffectContext c) => c.Self == null ? new() : c.Self.SourcesWhere(x => !c.Self.IsFaceDown(x) && pk.Match(x)).GroupBy(x => x.Name).Select(g => g.First()).ToList();
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && Distinct(c).Count >= n,
                    c =>
                    {
                        var pool = Distinct(c);
                        var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, pool, n, false, $"[{c.Card.Name}] 덱 아래로 되돌릴 명칭이 서로 다른 진화원 {n}장을 고르세요.");
                        var picked = idx.Select(i => pool[i]).Distinct().ToList();
                        foreach (var x in pool) { if (picked.Count >= n) break; if (!picked.Contains(x)) picked.Add(x); }
                        int moved = 0;
                        foreach (var x in picked.Take(n))
                            if (c.Self!.TakeSource(y => ReferenceEquals(y, x)) != null) { c.Owner.MainDeck.Add(x); moved++; }
                        if (moved < n) c.CostFailed = true;
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("differentNamesSourcesToDeck50");
                    },
                    _ => true, $"명칭이 서로 다른 진화원 {n}장을 덱 아래로 되돌려");
        }
        return null;
    }

    /// <summary>지속 (원래 규칙이 읽지 못한 것만): "[Your Turn] This Digimon can't suspend." (「석화」 토큰 — BT21-029 · BT24-017 · EX11-012)</summary>
    private static bool ParseStatic50(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer)
    {
        if (!Gate50) return false;
        var sentences = SplitSentences(body);
        if (sentences.Count != 1 || card.Type != CardType.Digimon) return false;
        if (!Regex.IsMatch(sentences[0].Trim().TrimEnd('.'), @"^this Digimon can't suspend$", I)) return false;
        Func<StaticContext, bool> turn = qualifier switch
        {
            Qualifier.YourTurn => c => c.OwnersTurn,
            Qualifier.OpponentsTurn => c => !c.OwnersTurn,
            _ => outer ?? (_ => true),
        };
        var src = inherited ? EffectSource.Inherited : EffectSource.Main;
        pending.Registrations.Add(d => d.Static(StaticStat.Keyword, src, c => turn(c) ? 1 : 0, Keyword.CannotSuspend));
        return true;
    }

    // =====================================================================
    // 문장 · 동작 — 원래 규칙이 읽지 못한 것만 (문이 열려 있을 때)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback50(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth50 >= 2 || !Gate50) return null;
        _fallbackDepth50++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            bool optional = false;
            var may = Regex.Match(s, @"^You may (?<b>.+)$", I);
            if (may.Success) { optional = true; s = may.Groups["b"].Value; }
            if (ActionExtra50(s, card, triggers) is { Act: { } act }) return (new Step(null, act, optional, sentence.Trim()), null);
            return null;
        }
        finally { _fallbackDepth50--; }
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction50(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth50 >= 2 || !Gate50) return null;
        _fallbackDepth50++;
        try { return ActionExtra50(Regex.Replace(raw.Trim().TrimEnd('.'), @"^you may ", "", I), card, triggers); }
        finally { _fallbackDepth50--; }
    }

    private static (Action<EffectContext>? Act, string? Error)? ActionExtra50(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = card.Name;
        Match m;
        // "they play 1 [Petrification] Token⦃Digimon/White/3000 DP/[Your Turn] This Digimon can't suspend. [On Deletion] Trash your top security card⦄" (BT21-029 · BT24-017 · EX11-012) — 상대의 배틀 에어리어에
        if ((m = Regex.Match(s, @"^(?:they|your opponent) plays? (?<n>\d+|a|an) \[(?<name>[^\]]+)\] Tokens?⦃(?<spec>[^⦄]*)⦄(?: without paying (?:the|its) (?:memory )?costs?)?$", I)).Success)
        {
            string name = m.Groups["name"].Value, spec = m.Groups["spec"].Value;
            int n = int.TryParse(m.Groups["n"].Value, out int k) ? k : 1;
            if (MakeToken(name, spec) is not { } proto) return (null, $"미지원 토큰 정보: {DecodeTokenSpec(spec)}");
            if (KeywordDictionary.Analyze(proto).FirstOrDefault(x => x.Support != ClauseSupport.Supported) is { } bad) return (null, $"미지원 토큰 효과: {bad.Text}");
            return (c =>
            {
                int played = 0;
                for (int i = 0; i < n && c.Enemy.BattleArea.Count < 12; i++, played++)
                    if (!c.Engine.TryPlayByEffect(c.Enemy, MakeToken(name, spec)!, PlayOrigin.Token, null)) break;
                if (played == 0) c.LastStepResolved = false;
                Diag46.Hit("opponentPlaysToken50");
                Emit(c, label, played > 0 ? $"상대가 [{name} Token] {played}장 등장" : "상대가 토큰을 등장시킬 수 없습니다");
                c.RecalculateFieldBuffs();
            }, null);
        }
        // "place up to 2 [Hybrid] trait cards with different colors from your hand or trash under this Tamer" (AD1-020 · AD1-023 — 서로 같은 색을 갖지 않는 카드: 23차 EX7-037과 같은 판단) ·
        // "place up to 1 [Hybrid] trait card with different names from your trash under this Tamer ⟦plus-other-tamers:2⟧" (BT18-088 — 다른 자신의 테이머 1명마다 상한 +2) ·
        // "place up to 5 [Hybrid] trait cards with different names from your hand or trash under 1 of your Tamers" (BT18-095 · BT18-097)
        if ((m = Regex.Match(s, @"^place up to (?<n>\d+) (?<pick>.+?) cards? with different (?<what>names|colou?rs) from (?<from>your hand or (?:your )?trash|your trash|your hand) under (?:this Tamer|1 of your Tamers)(?: ⟦plus-other-tamers:(?<plus>\d+)⟧)?$", I)).Success
            && ParseCardPick("1 " + m.Groups["pick"].Value + " card") is { } pk)
        {
            int n0 = int.Parse(m.Groups["n"].Value);
            int plus = m.Groups["plus"].Success ? int.Parse(m.Groups["plus"].Value) : 0;
            bool names = m.Groups["what"].Value.StartsWith("name", StringComparison.OrdinalIgnoreCase);
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
            bool thisTamer = Regex.IsMatch(s, @"under this Tamer", I);
            return (c =>
            {
                if (c.Owner.Tamers.Count == 0 || thisTamer && c.Card.Type == CardType.Tamer && !c.Owner.Tamers.Contains(c.Card)) { c.LastStepResolved = false; Emit(c, label, "테이머가 없습니다"); return; }
                int others = c.Owner.Tamers.Count(t => !ReferenceEquals(t, c.Card));
                int n = n0 + plus * others;
                var chosen = new List<(CardData Card, bool Hand)>();
                bool Differs(CardData x) => names ? !chosen.Any(y => y.Card.Name == x.Name) : !chosen.Any(y => y.Card.AllColors.Intersect(x.AllColors).Any());
                while (chosen.Count < n)
                {
                    var pool = new List<(CardData Card, bool Hand)>();
                    if (fromHand) pool.AddRange(c.Owner.Hand.Where(x => pk.Match(x) && !chosen.Any(y => ReferenceEquals(y.Card, x)) && Differs(x)).Distinct().Select(x => (x, true)));
                    if (fromTrash) pool.AddRange(c.Owner.Trash.Where(x => pk.Match(x) && !chosen.Any(y => ReferenceEquals(y.Card, x)) && Differs(x)).Distinct().Select(x => (x, false)));
                    // 추천: 트래시 먼저 · 낮은 Lv.
                    pool = pool.OrderBy(p => p.Hand ? 1 : 0).ThenBy(p => p.Card.Level).ToList();
                    if (pool.Count == 0) break;
                    var idx = c.Engine.ChooseCardIndexes(c.Owner, fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash, pool.Select(p => p.Card).ToList(), 1, true,
                        $"[{label}] 테이머 아래에 놓을 카드를 고르세요 ({chosen.Count + 1}/{n}, {(names ? "명칭" : "색")}이 서로 달라야 합니다 — 고르지 않으면 끝).");
                    if (idx.Count == 0) break;
                    chosen.Add(pool[idx[0]]);
                }
                var placed = new List<string>();
                foreach (var (x, hand) in chosen)
                    if (hand ? c.Owner.Hand.Remove(x) : c.Owner.Trash.Remove(x)) { c.Owner.UnderTamers.Add(x); placed.Add(x.Name); }
                if (placed.Count == 0) c.LastStepResolved = false;
                Diag46.Hit(names ? "differentNamesUnderTamer50" : "differentColorsUnderTamer50");
                Emit(c, label, placed.Count > 0 ? $"[{string.Join(", ", placed)}]을(를) 테이머 아래에" : "놓을 카드가 없습니다");
            }, null);
        }
        // "1 of your Tamers with 5 or more cards under it may digivolve into [EmperorGreymon] in the hand or trash, ignoring digivolution requirements and without paying the cost" (BT18-095 · BT18-097)
        //  — 테이머 아래 카드는 한 묶음 ("this Tamer has N cards under it"과 같은 판단)
        if ((m = Regex.Match(s, @"^1 of your Tamers with (?<n>\d+) or more cards under it may digivolve into (?<x>\[[^\]]+\]) in the (?<from>hand or trash|hand|trash), ignoring (?:its )?digivolution requirements and without paying (?:the|its) costs?$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            string name = TargetSpec.Names(m.Groups["x"].Value)[0];
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool hand = from.Contains("hand"), trash = from.Contains("trash");
            return (c =>
            {
                if (c.Owner.UnderTamers.Count < n || c.Owner.Tamers.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, $"아래에 카드가 {n}장 이상 있는 테이머가 없습니다"); return; }
                var options = new List<(CardData Tamer, CardData Evo, List<CardData> Zone)>();
                foreach (var z in new[] { hand ? c.Owner.Hand : null, trash ? c.Owner.Trash : null })
                {
                    if (z == null) continue;
                    var evo = z.Where(x => x.Type == CardType.Digimon && CardNames.Is(x, name)).OrderByDescending(x => x.DP).FirstOrDefault();
                    if (evo == null) continue;
                    foreach (var t in c.Owner.Tamers.Distinct()) options.Add((t, evo, z));
                }
                if (options.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"진화할 [{name}]이(가) 없습니다"); return; }
                var texts = options.Select(o => $"[{o.Tamer.Name}] → [{o.Evo.Name}] ({(ReferenceEquals(o.Zone, c.Owner.Trash) ? "트래시" : "패")})").ToList();
                texts.Add("진화하지 않는다");
                int idx = c.Engine.ChooseOption(c.Owner, $"[{label}] 진화시킬 테이머를 고르세요 (진화 조건 무시 · 코스트 없이).", texts, 0);
                if (idx < 0 || idx >= options.Count) { c.LastStepResolved = false; return; }
                var o = options[idx];
                if (c.Engine.EvolveTamerByEffect(c.Owner, o.Tamer, o.Evo, 0, o.Zone) is { } ti) c.LastTargets = new List<CardInstance> { ti };
                else { c.LastStepResolved = false; return; }
                Diag46.Hit("tamerUnderCountDigivolve50");
                Emit(c, label, $"테이머 [{o.Tamer.Name}] → [{o.Evo.Name}] 진화 (진화 조건 무시 · 코스트 0)");
            }, null);
        }
        // "return this Tamer to the bottom of the deck" (BT21-102 — 【메인】 효과의 끝)
        if (Regex.IsMatch(s, @"^return this Tamer to the bottom of (?:the|your|its owner's) deck$", I) && card.Type == CardType.Tamer)
        {
            return (c =>
            {
                if (!c.Owner.Tamers.Contains(c.Card)) { c.LastStepResolved = false; return; }
                c.Engine.MoveTamerToDeckBottom34(c.Owner, c.Card);
                if (c.Owner.Tamers.Contains(c.Card)) { c.LastStepResolved = false; return; }
                Diag46.Hit("thisTamerToDeckBottom50");
                Emit(c, label, "이 테이머를 덱 아래로");
            }, null);
        }
        // "delete 1 of your opponent's Tamers or level 6 or lower Digimon" (BT7-111 — 테이머 또는 조건에 맞는 디지몬 중 1장)
        if ((m = Regex.Match(s, @"^delete 1 of your opponent's Tamers or (?<spec>[^,]+?) Digimon$", I)).Success
            && TargetSpec.Parse("all of your opponent's " + m.Groups["spec"].Value + " Digimon") is { Opponent: true, AnySide: false, IsThis: false, IsThat: false, ContextFilters.Count: 0 } ds)
        {
            var fs = ds.Filters.ToList();
            return (c =>
            {
                var digis = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && fs.All(f => f(d)) && !c.Engine.IsUnaffectedBy(c.Owner, d)).OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).ToList();
                var tamers = c.Enemy.Tamers.Distinct().ToList();
                var texts = digis.Select(d => $"디지몬 [{d.TopCard.Name}] (Lv.{d.TopCard.Level} · DP {d.GetCurrentDP()})").Concat(tamers.Select(t => $"테이머 [{t.Name}]")).ToList();
                if (texts.Count == 0) { c.LastStepResolved = false; Emit(c, label, "소멸시킬 대상이 없습니다"); return; }
                int k = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{label}] 소멸시킬 상대의 테이머 또는 디지몬을 고르세요.", texts, 0), 0, texts.Count - 1);
                Diag46.Hit("deleteTamerOrDigimon50");
                if (k < digis.Count)
                {
                    var d = digis[k]; string nm = d.TopCard.Name;
                    if (c.DeleteEnemy(d)) Emit(c, label, $"[{nm}] 소멸"); else c.LastStepResolved = false;
                    c.LastTargets = new List<CardInstance> { d };
                }
                else
                {
                    var t = tamers[k - digis.Count];
                    c.Engine.DeleteTamer(c.Enemy, t);
                    Emit(c, label, $"테이머 [{t.Name}] 소멸");
                }
            }, null);
        }
        // "activate 1 of this Digimon's [Security] effects" (EX13-036 — 이 디지몬의 맨 위 카드에 적힌 【시큐리티】 효과)
        if (Regex.IsMatch(s, @"^activate (?:1|one) of this Digimon's \[Security\] effects$", I))
        {
            return (c =>
            {
                var top = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self.TopCard : null;
                var effs = top == null ? new List<CardEffect>() : CardScripts.For(top)?.Effects.Where(e => e.Trigger == EffectTrigger.Security && e.Source == EffectSource.Main).ToList() ?? new();
                if (effs.Count == 0) { c.LastStepResolved = false; Emit(c, label, "발휘할 【시큐리티】 효과가 없습니다"); return; }
                int k = effs.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{label}] 발휘할 【시큐리티】 효과를 고르세요.", effs.Select((_, i) => $"【시큐리티】 효과 {i + 1}").ToList(), 0);
                Diag46.Hit("activateOwnSecurityEffect50");
                Emit(c, label, "이 디지몬의 【시큐리티】 효과를 발휘");
                effs[Math.Clamp(k, 0, effs.Count - 1)].Resolve(c);
            }, null);
        }
        // "declare 1 card category and reveal the top card of your opponent's deck for category immunity" (LM-020 — 카드 종류: 디지몬 · 디지타마 · 테이머 · 옵션)
        if (Regex.IsMatch(s, @"^declare 1 card category and reveal the top card of your opponent's deck for category immunity$", I))
        {
            return (c =>
            {
                var kinds = new[] { CardType.Digimon, CardType.Digitama, CardType.Tamer, CardType.Option };
                var names = new[] { "디지몬", "디지타마", "테이머", "옵션" };
                // 추천: 상대의 트래시 · 배틀 에어리어에서 가장 많이 보인 종류 (상대의 덱은 보지 않는다)
                var seen = c.Enemy.Trash.Select(x => x.Type).Concat(c.Enemy.BattleArea.Select(d => d.TopCard.Type)).Concat(c.Enemy.Tamers.Select(t => t.Type)).ToList();
                int rec = seen.Count == 0 ? 0 : Array.IndexOf(kinds, kinds.OrderByDescending(k => seen.Count(t => t == k)).First());
                int pick = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{label}] 카드 종류 1개를 선언하세요.", names, Math.Max(0, rec)), 0, kinds.Length - 1);
                var kind = kinds[pick];
                if (c.Enemy.MainDeck.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"{names[pick]} 선언 — 상대의 덱이 없습니다"); return; }
                var top = c.Enemy.MainDeck[0];
                bool hit = (top.IsDual ? CardType.Option : top.Type) == kind || top.IsDual && kind == top.Type;
                if (hit && c.Self != null && c.Owner.BattleArea.Contains(c.Self))
                {
                    c.Self.UnaffectedCategory50 = kind;
                    c.Self.UnaffectedCategory50Until = c.Engine.State.TurnSequence;
                }
                // 공개한 카드는 상대의 덱 위 또는 아래로 — 추천: 아래
                int where = c.Engine.ChooseOption(c.Owner, $"[{label}] 공개한 [{top.Name}]을(를) 상대의 덱 어디로 되돌릴까요?", new[] { "덱 아래", "덱 위" }, 0);
                if (where == 0) { c.Enemy.MainDeck.RemoveAt(0); c.Enemy.MainDeck.Add(top); }
                if (!hit) c.LastStepResolved = false;
                Diag46.Hit(hit ? "declareCategoryHit50" : "declareCategoryMiss50");
                Emit(c, label, $"{names[pick]} 선언 → 상대의 덱 위 [{top.Name}] 공개 — {(hit ? $"이 턴 동안 {names[pick]}의 효과를 받지 않는다" : "다른 종류")} · {(where == 0 ? "덱 아래로" : "덱 위로")}");
            }, null);
        }
        // "delete 7 of your opponent's Digimon or Tamers, then trash the top 7 cards of your opponent's security stack minus 1 for each card deleted" (EX6-073)
        if ((m = Regex.Match(s, @"^delete (?<n>\d+) of your opponent's Digimon or Tamers, then trash the top (?<k>\d+) cards of your opponent's security stack minus 1 for each card deleted$", I)).Success)
        {
            int k = int.Parse(m.Groups["k"].Value);
            // 디지몬과 테이머를 함께 후보로 (TamerTargetAction34 — "Delete N of your opponent's Digimon or Tamers")
            var del = TamerTargetAction34($"Delete {m.Groups["n"].Value} of your opponent's Digimon or Tamers", label, false) ?? ParseAction($"Delete {m.Groups["n"].Value} of your opponent's Digimon or Tamers", card, triggers);
            if (del.Error != null || del.Act == null) return null;
            var delAct = del.Act;
            var trashActs = new Dictionary<int, Action<EffectContext>?>();
            Action<EffectContext>? TrashAct(int x)
            {
                if (!trashActs.TryGetValue(x, out var a))
                    trashActs[x] = a = ParseAction(x == 1 ? "Trash the top card of your opponent's security stack" : $"Trash the top {x} cards of your opponent's security stack", card, triggers) is { Error: null, Act: { } ta } ? ta : null;
                return a;
            }
            if (TrashAct(k) == null) return null;
            return (c =>
            {
                var beforeD = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null).ToList();
                var beforeT = c.Enemy.Tamers.ToList();
                delAct(c);
                int deleted = beforeD.Count(d => !c.Enemy.BattleArea.Contains(d)) + beforeT.Count(t => !c.Enemy.Tamers.Contains(t));
                int n = Math.Max(0, k - deleted);
                Diag46.Hit("trashSecurityMinusDeleted50");
                Emit(c, label, $"소멸 {deleted} → 상대의 시큐리티 {n}장 파기");
                if (n > 0 && TrashAct(n) is { } t) t(c);
            }, null);
        }
        // "place up to 3 of your other blue Digimon as this Digimon's bottom digivolution cards ⟦then-return-other-level:4⟧" (EX6-015 — 놓을 수 있다 · 그 후 되돌리기는 반드시)
        if ((m = Regex.Match(s, @"^place up to (?<n>\d+) of your other (?<spec>[^,]*?)Digimon as this Digimon's bottom digivolution cards ⟦then-return-other-level:(?<lv>\d+)⟧$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value), lv = int.Parse(m.Groups["lv"].Value);
            var one = ParseStep($"Place 1 of your other {m.Groups["spec"].Value}Digimon as this Digimon's bottom digivolution card.", card, triggers);
            if (one.Error != null || one.Step == null) return null;
            var placeOne = one.Step;
            var returns = new Dictionary<int, Step?>();
            Step? ReturnAct(int level)
            {
                if (!returns.TryGetValue(level, out var a))
                    returns[level] = a = ParseStep($"Return all other level {level} or lower Digimon to the hand.", card, triggers) is { Error: null, Step: { } ra } ? ra : null;
                return a;
            }
            if (ReturnAct(lv) == null) return null;
            return (c =>
            {
                int placed = 0;
                for (int i = 0; i < n; i++)
                {
                    if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) break;
                    int before = c.Self.EvolutionSources.Count;
                    // 놓을 수 있다 (1장마다 묻는다) — 추천: 어차피 패로 되돌아갈 Lv.의 디지몬이 있을 때
                    bool rec = c.Owner.BattleArea.Any(d => !ReferenceEquals(d, c.Self) && d.TamerProxyOf == null && d.TopCard.Level <= lv + placed);
                    if (!c.Engine.AskYesNo(c.Owner, null, $"[{label}] 다른 자신의 디지몬을 이 디지몬의 진화원 아래에 놓을까요? ({i + 1}/{n} — 놓은 1장마다 되돌리는 Lv. 상한 +1)", recommended: rec)) break;
                    c.LastStepResolved = true;
                    RunStep(placeOne, c);
                    if (c.Self.EvolutionSources.Count <= before) break;
                    placed++;
                }
                c.LastStepResolved = true;
                Diag46.Hit("placeOthersThenReturn50");
                if (ReturnAct(lv + placed) is { } ret) RunStep(ret, c);
                Emit(c, label, $"진화원 아래에 {placed}장 · Lv.{lv + placed} 이하의 다른 디지몬을 패로");
            }, null);
        }
        return null;
    }
}
