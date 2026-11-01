using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.59.0 미지원 문장 33차 묶음: 원래 규칙(1~32차)이 읽지 못한 절만 33차 규칙으로 다시 읽는다 (33차 표현 Hint59가 있거나 바꿔 쓸 것이 있을 때만).
///  - ST17-13 [시큐리티] ≪퇴화 1≫ 디지몬 1마리 (어느 쪽이든) · 배틀 종료 시 자신의 디지몬 1마리를 이 카드로 진화 (코스트 없이, 진화 조건은 지킨다 — 공식 Q&amp;A ST17-13 Q1)
///  - BT20-037 그 턴 종료 시까지 상대의 디지몬 · 테이머는 【등장 시】를 발휘할 수 없고 액티브가 되지 않는다
///  - BT5-018 패의 카드를 파기하여 파기한 카드의 DP만큼 DP + (공식 Q&amp;A BT5-018 Q1: 같은 턴에 다시 하면 더해진다)
///  - BT21-086 자신의 [Marcus Damon] 1명을 레스트시킬 수 있다
///  - EX4-011 [트래시] [자신의 턴 종료 시] 이 카드를 코스트 없이 등장 (공식 Q&amp;A EX4-011 Q1: 그 타이밍에 트래시에 있으면 발휘) · LM-006 [트래시] [메인] 되돌린 테이머의 등장 코스트만큼 줄여 등장
///  - BT14-006 · BT24-007 패에서 파기된 그 카드로 진화 / 그중 1장을 등장 (BT14-006 Q1: 육성 에어리어의 디지몬은 안 된다 — 진화원 효과는 배틀 에어리어에서만)
///  - BT13-033 · BT27-035 비용: 상대의 패를 보지 않고 무작위로 덱 아래에 N장이 될 때까지 · BT27-035 상대의 시큐리티 맨 위 카드를 덱 아래로
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch59.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth59;
    [ThreadStatic] private static int _gate59;
    internal static bool Gate59 => _gate59 > 0;

    private static string? TryParse59(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse58(rawClause, inherited, card, def);
        if (err == null || _clauseDepth59 > 0) return err;
        string t = Normalize58(rawClause);
        string alt = ClauseRewrite59(t);
        if (alt == t && !Hint59.IsMatch(t)) return err;
        _clauseDepth59++;
        _gate59++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (TryParse58(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse58(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth59--;
            _gate59--;
            if (def != null) WrapGate59(def, before);
        }
    }

    /// <summary>33차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate59(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate59++; try { inner(c); } finally { _gate59--; } } };
        }
    }

    /// <summary>33차의 동작 · 문장 · 비용이 쓰이는 절 (바꿔 쓰지 않아도 문을 열고 다시 읽는다)</summary>
    internal static readonly Regex Hint59 = new(@"＜De-Digivolve \d+＞ 1 Digimon\.|At the end of the battle, 1 of your Digimon may digivolve into this card|to add the trashed card's DP to this Digimon|1 of your \[[^\]]+\]s may suspend|^\[Trash\][^.]*play this card without paying|random cards from their hand|returning cards without looking|return their top security card to the bottom of the deck|⟦[a-z]+59", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules59 =
    {
        // BT20-037 "Then none of their Digimon or Tamers can activate [On Play] effects or unsuspend until the end of their turn." — 두 제한으로
        s => Regex.Replace(s, @"\bThen,? none of their Digimon or Tamers can activate \[On Play\] effects or unsuspend until the end of their turn\.",
            "Then, none of their Digimon or Tamers can activate [On Play] effects until the end of their turn. None of their Digimon or Tamers can unsuspend until the end of their turn.", RegexOptions.IgnoreCase),
        // LM-006 "By returning 1 of your Tamers to the bottom of the deck, play this card with the play cost reduced by the play cost of the returned Tamer." — 되돌린 테이머를 알아야 한다
        s => Regex.Replace(s, @"\bBy returning 1 of your Tamers to the bottom of the deck, play this card with the play cost reduced by the play cost of the returned Tamer\.",
            "By returning 1 of your Tamers to the bottom of the deck ⟦pc59⟧, play this card with the play cost reduced by the play cost of the returned Tamer.", RegexOptions.IgnoreCase),
        // BT5-018 "You may trash 1 red Digimon card in your hand to add the trashed card's DP to this Digimon for the turn." — 원래 규칙은 "to"에서 두 문장으로 나눠 읽지 못한다
        s => Regex.Replace(s, @"\bYou may trash (?<p>1 [^.]+?) in your hand to add the trashed card's DP to this Digimon for the turn\.", "You may trash ${p} in your hand ⟦dp59⟧.", RegexOptions.IgnoreCase),
        // BT14-006 "When a Digimon card with the [Dark Animal] or [SoC] trait is trashed from your hand, this Digimon may digivolve into that card." — "그 카드" = 버린 카드 중 그 조건의 카드
        s => Regex.Replace(s, @"\bWhen (?:an? )?(?<p>Digimon card with the \[[^\]]+\](?: or \[[^\]]+\])* trait) is trashed from your hand, this Digimon may digivolve into that card\.",
            "When a ${p} is trashed from your hand, this Digimon may digivolve into that card ⟦ht59:1 ${p}⟧.", RegexOptions.IgnoreCase),
        // BT24-007 "When level 4 or higher Digimon cards with the [Demon] or [Titan] trait are trashed from your hand, you may play 1 of them with the play cost reduced by 2."
        s => Regex.Replace(s, @"\bWhen (?<p>level \d+ or (?:higher|lower) Digimon) cards (?<w>with the \[[^\]]+\](?: or \[[^\]]+\])* trait) are trashed from your hand, you may play 1 of them with the play cost reduced by (?<n>\d+)\.",
            "When ${p} cards ${w} are trashed from your hand, you may play 1 of them with the play cost reduced by ${n} ⟦ht59:1 ${p} card ${w}⟧.", RegexOptions.IgnoreCase),
        // EX11-067 "1 of your Digimon with [Lucemon] in its text on the field may digivolve into ~" — "on the field"는 "자신의 디지몬"과 같다
        s => Regex.Replace(s, @"\b(?<t>1 of your Digimon with \[[^\]]+\] in its text) on the field may digivolve into\b", "${t} may digivolve into", RegexOptions.IgnoreCase),
        // BT16-031 "return 1 level 6 or lower 2-color Digimon card that's red or purple from your trash to the hand" — 색을 앞으로
        s => Regex.Replace(s, @"\breturn 1 level (?<lv>\d+) or lower (?<k>\d+)-colou?r Digimon card that's (?<a>red|blue|yellow|green|black|purple|white) or (?<b>red|blue|yellow|green|black|purple|white) from your trash\b",
            "return 1 ${a} or ${b} level ${lv} or lower ${k}-color Digimon card from your trash", RegexOptions.IgnoreCase),
        // BT20-059 "While this Digimon is [Jesmon GX] all of your Digimon gain ~" — 쉼표가 빠졌다
        s => Regex.Replace(s, @"\bWhile this Digimon is (?<x>\[[^\]]+\]) all of your\b", "While this Digimon is ${x}, all of your", RegexOptions.IgnoreCase),
        // BT16-072 · BT16-073 "[On Deletion] From your trash and without paying the cost, you may play 1 Tamer card with [Myotismon] in its text and without the same name as any of your Tamers."
        s => Regex.Replace(s, @"\bFrom your trash and without paying the cost, you may play (?<p>1 Tamer card with \[[^\]]+\] in its text) and without the same name as any of your Tamers\.",
            "You may play ${p} ⟦nosame59⟧ from your trash without paying the cost.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite59(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules59) s = f(s);
        return s;
    }

    /// <summary>그 턴 종료 시까지 — 상대의 턴 (지금이 상대의 턴이면 이 턴, 아니면 다음 턴)</summary>
    private static int EndOfTheirTurn59(EffectContext c) => c.Engine.IsTurnOf(c.Enemy) ? c.TurnSequence : c.TurnSequence + 1;

    /// <summary>방금 패에서 파기된 카드 중 pick에 맞고 아직 트래시에 있는 카드 ("that card" · "1 of them")</summary>
    private static List<CardData> HandTrashed59(EffectContext c, Func<CardData, bool> match)
    {
        IEnumerable<CardData> src = c.Engine.CurrentHandTrashed.Count > 0 ? c.Engine.CurrentHandTrashed : c.TriggerCard != null ? new[] { c.TriggerCard } : Array.Empty<CardData>();
        return src.Where(x => match(x) && c.Owner.Trash.Any(t => ReferenceEquals(t, x))).Distinct().ToList();
    }

    // =====================================================================
    // 동작 (문이 열려 있을 때만)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction59(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate59) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        bool security = triggers.Contains(EffectTrigger.Security);
        s = Regex.Replace(s.Trim(), @"^Then,?\s+", "", I);
        Match m;

        // ST17-13 [시큐리티] "＜De-Digivolve 1＞ 1 Digimon" — 어느 쪽의 디지몬이든 1마리 (≪퇴화≫는 강제 · Lv.3 이하는 파기하지 않는다: 16-12-3 · 16-12-4.
        //  고르는 대상에 조건이 없으므로 파기할 것이 없는 디지몬도 고를 수 있다: 15-11-1-3-1)
        if ((m = Regex.Match(s, @"^＜De-Digivolve (?<n>\d+)＞ 1 Digimon$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                bool Hits(CardInstance d) => d.TamerProxyOf == null && d.EvolutionSources.Count > 0 && d.TopCard.Level > 3 && !c.Engine.IsUnaffectedBy(c.Owner, d);
                var cands = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null).OrderByDescending(d => Hits(d) ? 1 : 0).ThenByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP())
                    .Concat(c.Owner.BattleArea.Where(d => d.TamerProxyOf == null).OrderBy(d => Hits(d) ? 1 : 0).ThenBy(d => d.TopCard.Level)).ToList();
                if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "디지몬이 없습니다"); return; }
                var d = c.Engine.ChooseTargets(c.Owner, cands, 1, $"[{c.Card.Name}] ≪퇴화 {n}≫할 디지몬 1마리를 고르세요 (어느 쪽이든).", optional: false, preselected: null).FirstOrDefault() ?? cands[0];
                string before = d.TopCard.Name;
                int k = Hits(d) ? c.Engine.DeDigivolve(c.Engine.OwnerOf(d), d, n) : 0;
                c.LastTargets = new List<CardInstance> { d };
                Diag46.Hit("deDigivolveAnySide59");
                Emit(c, label, k > 0 ? $"≪퇴화 {n}≫ [{before}] → [{d.TopCard.Name}]" : $"≪퇴화 {n}≫ [{before}] — 파기할 카드가 없습니다");
            }, null);
        }
        // ST17-13 [시큐리티] "At the end of the battle, 1 of your Digimon may digivolve into this card without paying the digivolution cost" — 배틀 종료 시 트래시의 이 카드로 (진화 조건은 지킨다)
        if (security && card.Type == CardType.Digimon
            && Regex.IsMatch(s, @"^At the end of the battle, 1 of your Digimon may digivolve into this card without paying (?:the|its) (?:digivolution |memory )?cost$", I))
            return (c =>
            {
                var owner = c.Owner;
                var me = c.Card;
                c.Engine.QueueAfterBattle59(owner, () =>
                {
                    if (!owner.Trash.Any(t => ReferenceEquals(t, me))) return;
                    var bases = owner.BattleArea.Where(d => d.TamerProxyOf == null && ActionValidator.ValidateEvolution(d, me).CanExecute)
                        .OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.EvolutionSources.Count).ToList();
                    if (bases.Count == 0) { c.Log($"[{me.Name}] 배틀 종료 시 — 이 카드로 진화할 수 있는 디지몬이 없습니다"); return; }
                    if (!c.Engine.AskYesNo(owner, null, $"[{me.Name}] 배틀이 끝났습니다 — 자신의 디지몬 1마리를 이 카드로 코스트 없이 진화시킬까요?", recommended: true))
                    { c.Log($"[{me.Name}] 배틀 종료 시 — 진화하지 않았습니다"); return; }
                    var d = c.Engine.ChooseTargets(owner, bases, 1, $"[{me.Name}](으)로 진화할 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault() ?? bases[0];
                    string before = d.TopCard.Name;
                    c.Engine.EvolveByEffect(owner, d, me, 0, owner.Trash);
                    Diag46.Hit("evolveAfterSecurityBattle59");
                    c.Log($"[{me.Name}] 배틀 종료 시 — [{before}]이(가) 이 카드로 코스트 없이 진화");
                });
                Emit(c, label, "배틀이 끝나면 자신의 디지몬 1마리를 이 카드로 진화시킬 수 있다");
            }, null);

        // BT20-037 "none of their Digimon or Tamers can activate [On Play] effects until the end of their turn" — 그 기간에 등장하는 것도 (전체 처리: 공식 Q&A LM-006 Q1과 같은 방식)
        if (Regex.IsMatch(s, @"^none of (?:their|your opponent's) Digimon or Tamers can activate \[On Play\] effects until the end of their turn$", I))
            return (c =>
            {
                int until = EndOfTheirTurn59(c);
                c.Enemy.OnPlayBlockedUntil59 = Math.Max(c.Enemy.OnPlayBlockedUntil59, until);
                Diag46.Hit("onPlayPeriodBlock59");
                Emit(c, label, "상대의 턴 종료 시까지 상대의 디지몬 · 테이머는 【등장 시】 효과를 발휘할 수 없다");
            }, null);

        // BT5-018 "trash 1 red Digimon card in your hand to add the trashed card's DP to this Digimon for the turn" ("You may"는 앞에서 묻는다)
        if ((m = Regex.Match(s, @"^trash (?<p>1 .+?) (?:in|from) your hand ⟦dp59⟧$", I)).Success
            && card.Type == CardType.Digimon && ParseCardPick(m.Groups["p"].Value) is { Count: 1 } dpPick)
        {
            var match = dpPick.Match;
            return (c =>
            {
                var self = c.Self;
                var cands = c.Owner.Hand.Where(match).Distinct().OrderByDescending(x => x.DP).ThenBy(x => x.PlayCost).ToList();
                if (self == null || !c.Owner.BattleArea.Contains(self) || cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "파기할 카드가 패에 없습니다"); return; }
                int i = cands.Count == 1 ? 0 : c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, cands, 1, upTo: false, $"[{c.Card.Name}] 파기할 패 1장을 고르세요 (그 카드의 DP만큼 DP +).").FirstOrDefault();
                var x = cands[Math.Clamp(i, 0, cands.Count - 1)];
                if (!c.Owner.Hand.Remove(x)) { c.LastStepResolved = false; return; }
                c.Owner.Trash.Add(x);
                c.Engine.NotifyHandTrashed(c.Owner, new[] { x });
                int dp = Math.Max(0, x.DP);
                c.Engine.ApplyDpChange(self, dp, c.TurnSequence, c.Card.Name);
                c.Engine.RecalculateFieldBuffs();
                c.LastCard = x;
                Diag46.Hit("trashedCardDp59");
                Emit(c, label, $"패의 [{x.Name}]을(를) 파기 — 이 턴 동안 DP +{dp:N0}");
            }, null);
        }

        // BT21-086 "1 of your [Marcus Damon]s may suspend" — 자신의 그 이름의 테이머(또는 디지몬) 1장을 레스트시킬 수 있다
        if ((m = Regex.Match(s, @"^1 of your (?<x>\[[^\]]+\])s may suspend$", I)).Success)
        {
            var names = TargetSpec.Names(m.Groups["x"].Value);
            return (c =>
            {
                var tamers = c.Owner.Tamers.Distinct().Where(t => names.Any(n => CardNames.Is(t, n)) && c.Owner.HasActiveTamer(t) && !c.Engine.TamerSuspendLocked(c.Owner, t)).ToList();
                var digis = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && !d.IsSuspended && names.Any(n => CardNames.Is(d.TopCard, n)) && !EffectRules.IsSuspendLocked(c.State, d)).ToList();
                if (tamers.Count + digis.Count == 0) { c.LastStepResolved = false; Emit(c, label, "레스트시킬 카드가 없습니다"); return; }
                if (!c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 자신의 {string.Join("/", names)} 1장을 레스트시킬까요?", recommended: true))
                { c.LastStepResolved = false; Emit(c, label, "레스트시키지 않았습니다"); return; }
                // 이 카드 자신을 먼저 (【서로의 턴】 "이 테이머가 레스트 상태가 되었을 때"가 유발한다)
                var t0 = tamers.OrderBy(t => ReferenceEquals(t, c.Card) ? 0 : 1).ToList();
                if (t0.Count > 0 && (digis.Count == 0 || t0.Count == 1))
                {
                    var t = t0.Count == 1 ? t0[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, t0, 1, upTo: false, $"[{c.Card.Name}] 레스트시킬 테이머를 고르세요.").FirstOrDefault() ?? t0[0];
                    c.Engine.SuspendTamer(c.Owner, t);
                    Diag46.Hit("suspendNamedTamer59");
                    Emit(c, label, $"테이머 [{t.Name}]을(를) 레스트");
                    return;
                }
                var d = c.Engine.ChooseTargets(c.Owner, digis, 1, $"[{c.Card.Name}] 레스트시킬 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault() ?? digis[0];
                c.Engine.Suspend(d);
                Emit(c, label, $"[{d.TopCard.Name}]을(를) 레스트");
            }, null);
        }

        // EX4-011 [트래시] "play this card without paying its cost" — 트래시의 이 카드 (그 타이밍에 트래시에 있으면 발휘: 공식 Q&A EX4-011 Q1)
        if ((_trashZoneParse || triggers.Contains(EffectTrigger.TrashMain)) && card.Type == CardType.Digimon
            && Regex.IsMatch(s, @"^play this card(?: from (?:your|the) trash)? without paying (?:the|its)(?: memory| play)? cost$", I))
            return (c =>
            {
                if (!c.Owner.Trash.Any(t => ReferenceEquals(t, c.Card))) { c.LastStepResolved = false; Emit(c, label, "트래시에 이 카드가 없습니다"); return; }
                bool ok = c.Engine.TryPlayByEffect(c.Owner, c.Card, PlayOrigin.Trash, () => c.Owner.Trash.Remove(c.Card));
                c.LastStepResolved = ok;
                if (ok) Diag46.Hit("trashZonePlayThis59");
                Emit(c, label, ok ? $"[{c.Card.Name}]을(를) 트래시에서 코스트 없이 등장" : "등장시키지 못했습니다");
            }, null);
        // LM-006 [트래시] [메인] "play this card with the play cost reduced by the play cost of the returned Tamer" (비용 ⟦pc59⟧가 되돌린 테이머의 등장 코스트를 PaidCount에)
        if ((_trashZoneParse || triggers.Contains(EffectTrigger.TrashMain)) && card.Type == CardType.Digimon
            && Regex.IsMatch(s, @"^play this card with the play cost reduced by the play cost of the returned Tamer$", I))
            return (c =>
            {
                if (!c.Owner.Trash.Any(t => ReferenceEquals(t, c.Card))) { c.LastStepResolved = false; Emit(c, label, "트래시에 이 카드가 없습니다"); return; }
                int full = CardScripts.EffectivePlayCost(c.Owner, c.Card, c.Enemy);
                int red = Math.Max(0, c.PaidCount);
                bool ok = c.Engine.TryPlayByEffect(c.Owner, c.Card, PlayOrigin.Trash, () => c.Owner.Trash.Remove(c.Card),
                    payCost: c.Engine.ReducedPlayCost(c.Owner, c.Card, Math.Max(0, full - red), full));
                c.LastStepResolved = ok;
                if (ok) Diag46.Hit("trashZonePlayReducedByTamer59");
                Emit(c, label, ok ? $"[{c.Card.Name}]을(를) 트래시에서 등장 (등장 코스트 -{red})" : "등장시키지 못했습니다");
            }, null);

        // BT14-006 "this Digimon may digivolve into that card ⟦ht59:1 Digimon card with ~⟧" — 패에서 파기된 그 카드로 (진화 조건 · 코스트는 보통대로: 종합 룰 8-1-3-2)
        if ((m = Regex.Match(s, @"^this Digimon may digivolve into that card ⟦ht59:(?<p>[^⟧]+)⟧$", I)).Success && ParseCardPick(m.Groups["p"].Value) is { } evPick)
        {
            var match = evPick.Match;
            return (c =>
            {
                var self = c.Self;
                var cards = self == null || !c.Owner.BattleArea.Contains(self) ? new List<CardData>()
                    : HandTrashed59(c, match).Where(x => x.Type == CardType.Digimon && ActionValidator.ValidateEvolution(self, x).CanExecute).OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
                if (cards.Count == 0) { c.LastStepResolved = false; Emit(c, label, "그 카드로 진화할 수 없습니다"); return; }
                var x = cards.Count == 1 ? cards[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, cards, 1, upTo: false, $"[{c.Card.Name}] 진화할 카드를 고르세요.").FirstOrDefault() ?? cards[0];
                int cost = Math.Max(0, x.EvoCost);
                if (!c.Engine.AskYesNo(c.Owner, self, $"[{c.Card.Name}] [{self!.TopCard.Name}]을(를) 트래시의 [{x.Name}](으)로 진화시킬까요? (진화 코스트 {cost})", recommended: cost <= Math.Max(0, c.OwnerMemory) + 2))
                { c.LastStepResolved = false; Emit(c, label, "진화하지 않았습니다"); return; }
                c.Engine.EvolveByEffect(c.Owner, self, x, cost, c.Owner.Trash);
                c.LastTargets = new List<CardInstance> { self };
                Diag46.Hit("digivolveIntoTrashedFromHand59");
                Emit(c, label, $"패에서 파기된 [{x.Name}](으)로 진화 (코스트 {cost})");
            }, null);
        }
        // BT24-007 "play 1 of them with the play cost reduced by 2 ⟦ht59:1 level 4 or higher Digimon card with ~⟧" ("You may"는 앞에서 묻는다)
        if ((m = Regex.Match(s, @"^play 1 of them with the play cost reduced by (?<n>\d+) ⟦ht59:(?<p>[^⟧]+)⟧$", I)).Success && ParseCardPick(m.Groups["p"].Value) is { } plPick)
        {
            var match = plPick.Match;
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                var cards = HandTrashed59(c, match).Where(x => x.Type == CardType.Digimon && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Trash))
                    .OrderByDescending(x => x.Level).ThenByDescending(x => x.PlayCost).ToList();
                if (cards.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 카드가 없습니다"); return; }
                var x = cards.Count == 1 ? cards[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, cards, 1, upTo: false, $"[{c.Card.Name}] 등장시킬 카드를 고르세요.").FirstOrDefault() ?? cards[0];
                int full = CardScripts.EffectivePlayCost(c.Owner, x, c.Enemy);
                bool ok = c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Trash, () => c.Owner.Trash.Remove(x),
                    payCost: c.Engine.ReducedPlayCost(c.Owner, x, Math.Max(0, full - n), full));
                c.LastStepResolved = ok;
                if (ok) Diag46.Hit("playTrashedFromHand59");
                Emit(c, label, ok ? $"패에서 파기된 [{x.Name}]을(를) 등장 (등장 코스트 -{n})" : "등장시키지 못했습니다");
            }, null);
        }

        // BT16-072 · BT16-073 "play 1 Tamer card with [Myotismon] in its text ⟦nosame59⟧ from your trash without paying the cost" — 자신의 테이머와 같은 이름이 아닌 것 ("You may"는 앞에서 묻는다)
        if ((m = Regex.Match(s, @"^play (?<p>1 Tamer card with \[[^\]]+\] in its text) ⟦nosame59⟧ from your trash without paying (?:the|its) cost$", I)).Success && ParseCardPick(m.Groups["p"].Value) is { } tPick)
        {
            var match = tPick.Match;
            return (c =>
            {
                var cands = c.Owner.Trash.Where(x => x.Type == CardType.Tamer && match(x) && !c.Owner.Tamers.Any(t => CardNames.Same(t, x)) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Trash))
                    .Distinct().OrderByDescending(x => x.PlayCost).ToList();
                if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 테이머가 없습니다"); return; }
                var x = cands.Count == 1 ? cands[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, cands, 1, upTo: false, $"[{c.Card.Name}] 등장시킬 테이머를 고르세요.").FirstOrDefault() ?? cands[0];
                bool ok = c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Trash, () => c.Owner.Trash.Remove(x));
                c.LastStepResolved = ok;
                if (ok) Diag46.Hit("playTamerNoSameName59");
                Emit(c, label, ok ? $"트래시의 테이머 [{x.Name}]을(를) 코스트 없이 등장" : "등장시키지 못했습니다");
            }, null);
        }
        // BT27-035 "return their top security card to the bottom of the deck" — 상대의 시큐리티 맨 위 카드를 상대의 덱 아래로
        if (Regex.IsMatch(s, @"^return (?:their|your opponent's) top security card to the bottom of (?:the|their|its owner's) deck$", I))
            return (c =>
            {
                if (c.Enemy.SecurityStack.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 시큐리티가 없습니다"); return; }
                var x = c.Enemy.SecurityStack[0];
                c.Enemy.SecurityStack.RemoveAt(0);
                c.Enemy.MainDeck.Add(x);
                c.Engine.NotifySecurityRemoved(c.Enemy, x, trashed: false);
                Diag46.Hit("enemySecurityToDeckBottom59");
                Emit(c, label, "상대의 시큐리티 맨 위 카드를 덱 아래로 되돌렸습니다");
            }, null);
        return null;
    }

    // =====================================================================
    // 비용
    // =====================================================================

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost59(string cost, CardData card)
    {
        cost = cost.Trim();
        Match m;
        // BT13-033 "returning random cards from their hand to the bottom of their owner's deck without looking at them until they have only 8 cards left"
        // BT27-035 "returning cards without looking to the bottom of the deck so that it has 7 left" — 상대의 패를 보지 않고(무작위) N장이 될 때까지 덱 아래로
        if ((m = Regex.Match(cost, @"^returning (?:random )?cards (?:from their hand )?(?:to the bottom of (?:their owner's|their|the) deck )?without looking(?: at them)?(?: to the bottom of (?:the|their|their owner's) deck)? (?:until they have only|so that it has) (?<n>\d+)(?: cards)? left$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => c.Enemy.Hand.Count > n,
                    c =>
                    {
                        var rng = new Random(c.State.TurnSequence * 7919 + c.Enemy.Hand.Count * 31 + c.Enemy.MainDeck.Count);
                        int k = 0;
                        while (c.Enemy.Hand.Count > n)
                        {
                            int i = rng.Next(c.Enemy.Hand.Count);
                            var x = c.Enemy.Hand[i];
                            c.Enemy.Hand.RemoveAt(i);
                            c.Enemy.MainDeck.Add(x);
                            k++;
                        }
                        c.PaidCount = k;
                        Diag46.Hit("enemyHandRandomToDeck59");
                        c.Log($"[{c.Card.Name}] 상대의 패를 보지 않고 {k}장 덱 아래로 되돌렸습니다 (상대의 패 {n}장)");
                    },
                    _ => true, $"상대의 패를 보지 않고 {n}장이 되도록 덱 아래로 되돌려");
        }
        // LM-006 "returning 1 of your Tamers to the bottom of the deck ⟦pc59⟧" — 고른 테이머의 등장 코스트를 PaidCount에 (등장 코스트를 그만큼 줄인다)
        if (Regex.IsMatch(cost, @"^returning 1 of your Tamers to the bottom of the deck ⟦pc59⟧$", I))
            return (c => c.Owner.Tamers.Any(t => t.Type == CardType.Tamer),
                    c =>
                    {
                        var ranked = c.Owner.Tamers.Distinct().Where(t => t.Type == CardType.Tamer).OrderByDescending(t => t.PlayCost).ThenBy(t => c.Owner.HasActiveTamer(t) ? 1 : 0).ToList();
                        var t = ranked.Count == 1 ? ranked[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, ranked, 1, upTo: false, $"[{c.Card.Name}] 덱 아래로 되돌릴 테이머를 고르세요 (그 등장 코스트만큼 줄인다).").FirstOrDefault() ?? ranked[0];
                        c.Engine.MoveTamerAsCost34(c.Owner, t, false);
                        c.PaidCount = t.PlayCost;
                        Diag46.Hit("returnTamerForPlayCost59");
                    },
                    _ => true, "자신의 테이머 1명을 덱 아래로 되돌려");
        return null;
    }
}
