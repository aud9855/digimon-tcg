using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.49.0 미지원 문장 23차 묶음: 원래 규칙(1~22차)이 읽지 못한 절 · 문장 · 동작 · 조건만 23차 규칙으로 다시 읽는다.
///  - 같은 뜻 다른 표기 (ClauseRules49): "a play or use cost of N or less" (EX13-058 · ST24-06) · "1 play or use cost 8 or lower Digimon or Option card with [X] in their texts" (EX13-064) ·
///    "1 of your opponent's Digimon of the same level" (EX3-023 — 등장한 디지몬과 같은 Lv.) · "By suspending 1 of your other Digimon, ~ as the Digimon this effect suspended" (BT16-048)
///  - 동작 (ActionExtra49): 양쪽 트래시를 덱 아래로 (BT17-077 · BT7-015) · 곳이 다른 두 고르기 중 하나 등장 (BT13-019) · 색이 다른 N장 등장 (EX7-037) ·
///    진화한 디지몬의 이름에 따라 등장 (EX13-067 · EX4-061)
///  - 조건 (ParseEventCondition49): "If this effect returned a white level 7 card" · "If 7 or more cards were returned using this effect"
///  - 덱 위 공개 "play or use 1 ~ among them" (LM-067 · BT26-084 — SentenceParser.cs 공개 처리의 PlayOrUse)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch49.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth49;
    [ThreadStatic] private static int _fallbackDepth49;
    [ThreadStatic] private static int _gate49;
    internal static bool Gate49 => _gate49 > 0;

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules49 =
    {
        // "1 play or use cost 8 or lower Digimon or Option card with [Knightmon] in their texts" (EX13-064) — 조건이 디지몬 · 옵션 양쪽에 붙도록 두 고르기로 ("1 Digimon or Option card with ~"은 원래 규칙이 "디지몬 카드 아무거나"로 나눈다)
        s => Regex.Replace(s, @"\b1 play or use cost (?<n>\d+) or (?:lower|less) Digimon or Option card with (?<x>\[[^\]]+\]) in (?:its|their) texts?", "1 Digimon card with ${x} in its text and a play cost of ${n} or less or 1 Option card with ${x} in its text and a play cost of ${n} or less", RegexOptions.IgnoreCase),
        // "play or use ~ from your hand or trash without paying the cost" (EX13-064) — 곳 목록의 표기 "your hand or your trash"
        s => Regex.Replace(s, @"(?<a>\bplay or use [^.]*? from your hand) or trash\b", "${a} or your trash", RegexOptions.IgnoreCase),
        // "1 card with [Knightmon] in its text and a play or use cost of 4 or less" (EX13-058) · "1 [DATA SQUAD] trait card with a play or use cost of 5 or less" (ST24-06) — 옵션의 사용 코스트도 카드의 코스트 칸
        s => Regex.Replace(s, @"\b(?<w>with|and) a play or use cost of (?<n>\d+) or (?:less|lower)\b", "${w} a cost of ${n} or less", RegexOptions.IgnoreCase),
        // "When you play a Digimon from digivolution cards, you may return 1 of your opponent's Digimon of the same level to ~" (EX3-023 — 공식 "등장한 디지몬과 같은 Lv.의")
        s => Regex.IsMatch(s, @"When you play a Digimon from (?:its |their )?digivolution cards", RegexOptions.IgnoreCase)
            ? Regex.Replace(s, @"\b(?<t>1 of your opponent's Digimon) of the same level\b", "${t} with the same level as the played Digimon", RegexOptions.IgnoreCase) : s,
        // "By suspending 1 of your other Digimon, return 1 of your opponent's Digimon with as much or less DP as the Digimon this effect suspended to the bottom of the deck." (BT16-048)
        //  — "~하는 것으로, ~" = 비용을 낼 수 있다 → 냈다면 (비용으로 레스트한 그 디지몬)
        // "~ from the digivolution cards of your Digimon in the breeding area without paying its cost. You can't play [Gankoomon] or [Omnimon] with this effect." (BT13-019) — 등장시킬 수 없는 이름을 앞 문장에
        s => Regex.Replace(s, @"(?<a>from the digivolution cards of your Digimon in the breeding area without paying (?:its|the) costs?)\. You can't play (?<n>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) with this effect\.", "${a} ⟦not:${n}⟧.", RegexOptions.IgnoreCase),
        // ---- 시큐리티 (같은 뜻 다른 표기) ----
        // "place this Digimon as your top security card" (EX9-021) → "on top of your security stack"
        s => Regex.Replace(s, @"\bplace this Digimon as your top security card\b", "place this Digimon on top of your security stack", RegexOptions.IgnoreCase),
        // "place 1 [Witchelny] text card from your hand as the bottom security card" (EX13-033)
        s => Regex.Replace(s, @"\bplace 1 (?<x>\[[^\]]+\]) text card from your hand as the bottom security card\b", "place 1 card with ${x} in its text from your hand at the bottom of your security stack", RegexOptions.IgnoreCase),
        // "place 1 of your [Patamon] at the bottom of your security stack face down" (P-023) — 이름 [Patamon]의 디지몬
        s => Regex.Replace(s, @"\bplace 1 of your (?<x>\[[^\]]+\]) at the bottom of your security stack face down\b", "place 1 of your ${x} Digimon at the bottom of your security stack face down", RegexOptions.IgnoreCase),
        // P-023 "~ at the bottom of your security stack face down. Trash that Digimon's digivolution cards." — 시큐리티로 옮길 때 겹쳐진 카드는 이미 파기된다 (덱으로 옮긴 P-024는 아님)
        s => Regex.Replace(s, @"(?<a>at the bottom of your security stack face down)\. Trash that Digimon's digivolution cards\.","${a}. Trash that Digimon's digivolution cards ⟦after-security⟧.", RegexOptions.IgnoreCase),
        // "Reveal all of your opponent's security cards, and trash 1 of them. Place the rest in your opponent's security stack face down. Then, your opponent shuffles their security stack." (BT10-086)
        s => Regex.Replace(s, @"\band trash 1 of them\. Place the rest in your opponent's security stack face down\. Then, your opponent shuffles their security stack\.", "and trash 1 of them, then shuffle the rest into their security stack.", RegexOptions.IgnoreCase),
        // "~, and place 1 card among them on top of your opponent's deck. Shuffle the rest and return them to the security stack." (LM-020)
        s => Regex.Replace(s, @"\band place 1 card among them on top of your opponent's deck\. Shuffle the rest and return them to the security stack\.", "and place 1 card among them on top of your opponent's deck, then shuffle the rest into their security stack.", RegexOptions.IgnoreCase),
        // ---- 진화원 (같은 뜻 다른 표기) ----
        // "trash 1 digivolution card from the bottom of each of your opponent's Digimon" (BT7-030) → "the bottom digivolution card of all of your opponent's Digimon"
        s => Regex.Replace(s, @"\btrash 1 digivolution card from the bottom of each of your opponent's Digimon\b", "trash the bottom digivolution card of all of your opponent's Digimon", RegexOptions.IgnoreCase),
        // "If you placed 4 or more cards with this effect" (EX6-073) — 앞 문장이 진화원에 놓은 장수
        s => Regex.Replace(s, @"\bIf you placed (?<n>\d+) or more cards with this effect\b", "If you place ${n} or more cards with this effect", RegexOptions.IgnoreCase),
        // "place 1 blue [Greymon] and 1 blue [MailBirdramon] from your trash under 1 of your Tamers with the [General] trait." (BT11-031) — 1장씩 (테이머 아래 카드는 한 곳)
        s => Regex.Replace(s, @"\b(?<v>[Pp]lace) 1 (?<a>[^.]+?) and 1 (?<b>[^.]+?) from your trash under (?<w>1 of your Tamers[^.]*)\.", "${v} 1 ${a} from your trash under ${w}. Place 1 ${b} from your trash under ${w}.", RegexOptions.None),
        s => Regex.Replace(s, @"\bBy suspending 1 of your other Digimon, (?<a>[^.]*?) with as much or less DP as the Digimon this effect suspended(?<b>[^.]*)\.", "You may suspend 1 of your other Digimon. If you did, ${a} with as much or less DP as that Digimon${b}.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite49(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules49) s = f(s);
        return s;
    }

    private static string? TryParse49(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse48(rawClause, inherited, card, def);
        if (err == null || _clauseDepth49 > 0) return err;
        _clauseDepth49++;
        _gate49++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (TryParse48(rawClause, inherited, card, null) == null)
            {
                if (def != null) TryParse48(rawClause, inherited, card, def);
                return null;
            }
            string alt = ClauseRewrite49(rawClause);
            if (alt == rawClause || TryParse48(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse48(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth49--;
            _gate49--;
            if (def != null) WrapGate49(def, before);
        }
    }

    /// <summary>23차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate49(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate49++; try { inner(c); } finally { _gate49--; } } };
        }
    }

    // =====================================================================
    // 조건 — 앞 동작의 결과 (EffectContext.Returned49)
    // =====================================================================

    private static Func<EffectContext, bool>? ParseEventCondition49(string cond)
    {
        if (!Gate49) return null;
        string s = cond.Trim();
        Match m;
        // "this effect returned a white level 7 card" (BT17-077)
        if ((m = Regex.Match(s, @"^this effect returned an? (?<c>red|blue|yellow|green|black|purple|white) level (?<lv>\d+) card$", I)).Success)
        {
            var col = Enum.Parse<CardColor>(m.Groups["c"].Value, true); int lv = int.Parse(m.Groups["lv"].Value);
            return c => c.Returned49.Any(x => x.Level == lv && x.AllColors.Contains(col)) && Diag46.HitTrue("condReturnedCard49");
        }
        // "7 or more cards were returned using this effect" (BT7-015)
        if ((m = Regex.Match(s, @"^(?<n>\d+) or more cards were returned (?:using|by|with) this effect$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return c => c.Returned49.Count >= n && Diag46.HitTrue("condReturnedCount49");
        }
        // "your opponent has as many or fewer total Digimon and Tamers as you" (EX6-061 — 디지몬과 테이머의 합계 수)
        if (Regex.IsMatch(s, @"^your opponent has as many or fewer total Digimon and Tamers as you$", I))
        {
            static int Total(PlayerZones z) => z.BattleArea.Count(d => d.TamerProxyOf == null) + z.Tamers.Count;
            return c => Total(c.Enemy) <= Total(c.Owner) && Diag46.HitTrue("condTotalDigimonTamers49");
        }
        return null;
    }

    // =====================================================================
    // 문장 · 동작 — 원래 규칙이 읽지 못한 것만 (문이 열려 있을 때)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback49(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth49 >= 2 || !Gate49) return null;
        _fallbackDepth49++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            bool optional = false;
            var may = Regex.Match(s, @"^You may (?<b>.+)$", I);
            if (may.Success) { optional = true; s = may.Groups["b"].Value; }
            if (ActionExtra49(s, card) is { Act: { } act }) return (new Step(null, act, optional, sentence.Trim()), null);
            return null;
        }
        finally { _fallbackDepth49--; }
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction49(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth49 >= 2 || !Gate49) return null;
        _fallbackDepth49++;
        try { return ActionExtra49(Regex.Replace(raw.Trim().TrimEnd('.'), @"^you may ", "", I), card); }
        finally { _fallbackDepth49--; }
    }

    private static (Action<EffectContext>? Act, string? Error)? ActionExtra49(string s, CardData card)
    {
        string label = card.Name;
        Match m;
        // "return all cards from your or your opponent's trash to the bottom of the deck" (BT17-077 — 공식 "자신 또는 상대의 트래시의 카드를 전부": 한쪽을 고른다)
        if (Regex.IsMatch(s, @"^return all cards from your or your opponent's trash to the bottom of (?:the|their owners'?|its owner's) decks?$", I))
        {
            return (c =>
            {
                // 추천: 자신의 트래시에 화이트 Lv.7이 있으면 자신 (뒤 문장의 메모리) · 아니면 상대 (상대가 다시 쓰지 못하게)
                bool ownBetter = c.Owner.Trash.Any(x => x.Level == 7 && x.AllColors.Contains(CardColor.White)) || c.Enemy.Trash.Count == 0;
                int pick = c.Engine.ChooseOption(c.Owner, $"[{label}] 어느 쪽 트래시의 카드를 전부 덱 아래로 되돌릴까요?",
                    new[] { $"자신의 트래시 ({c.Owner.Trash.Count}장)", $"상대의 트래시 ({c.Enemy.Trash.Count}장)" }, ownBetter ? 0 : 1);
                var who = pick == 0 ? c.Owner : c.Enemy;
                var moved = who.Trash.ToList();
                who.Trash.Clear();
                who.MainDeck.AddRange(moved);
                c.Returned49 = moved;
                if (moved.Count == 0) c.LastStepResolved = false;
                Diag46.Hit("trashAllToDeck49");
                Emit(c, label, $"{(pick == 0 ? "자신" : "상대")}의 트래시 {moved.Count}장을 덱 아래로");
            }, null);
        }
        // "Return all cards with the [Three Musketeers] trait and all Option cards from both players' trashes to the bottom of their owners' decks" (BT7-015)
        if ((m = Regex.Match(s, @"^return all cards with the (?<t>\[[^\]]+\](?: or \[[^\]]+\])*) traits? and all Option cards from both players' trash(?:es)? to the bottom of (?:their owners'?|its owner's|the) decks?$", I)).Success)
        {
            var ts = TargetSpec.Names(m.Groups["t"].Value);
            bool Match49(CardData x) => x.Type == CardType.Option || ts.Any(t => TargetSpec.HasTrait(x, t));
            return (c =>
            {
                var moved = new List<CardData>();
                foreach (var who in new[] { c.Owner, c.Enemy })
                {
                    var xs = who.Trash.Where(Match49).ToList();
                    foreach (var x in xs) who.Trash.Remove(x);
                    who.MainDeck.AddRange(xs);
                    moved.AddRange(xs);
                }
                c.Returned49 = moved;
                if (moved.Count == 0) c.LastStepResolved = false;
                Diag46.Hit("bothTrashToDeck49");
                Emit(c, label, $"양쪽 트래시에서 {moved.Count}장을 덱 아래로");
            }, null);
        }
        // "play 1 Digimon card with [Sistermon] in its name from your trash or 1 Digimon card with the [Royal Knight] trait from the digivolution cards of your Digimon in the breeding area without paying its cost" (BT13-019)
        if ((m = Regex.Match(s, @"^play (?<a>1 .+?) from your trash or (?<b>1 .+?) from the digivolution cards of your Digimon in the breeding area without paying (?:its|the) costs?(?<ex> ⟦not:(?<names>[^⟧]+)⟧)?$", I)).Success
            && ParseCardPick(m.Groups["a"].Value) is { } pa && ParseCardPick(m.Groups["b"].Value) is { } pb)
        {
            var not = m.Groups["ex"].Success ? TargetSpec.Names(m.Groups["names"].Value) : new List<string>();
            bool Allowed(CardData x) => !not.Any(n => CardNames.Is(x, n));
            return (c =>
            {
                var breed = c.Owner.BreedingArea;
                var pool = c.Owner.Trash.Where(x => pa.Match(x) && Allowed(x)).Select(x => (Card: x, Breed: false))
                    .Concat(breed == null ? Enumerable.Empty<(CardData Card, bool Breed)>() : breed.SourcesWhere(x => !breed.IsFaceDown(x) && pb.Match(x) && Allowed(x)).Select(x => (Card: x, Breed: true)))
                    .Where(p => c.Engine.CanPlayByEffect(c.Owner, p.Card, p.Breed ? PlayOrigin.Sources : PlayOrigin.Trash))
                    .OrderByDescending(p => p.Card.Level).ThenByDescending(p => p.Card.PlayCost).ToList();
                if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, "등장시킬 카드가 없습니다"); return; }
                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool.Select(p => p.Card).ToList(), 1, false, $"[{label}] 등장시킬 카드를 고르세요 (트래시 / 육성 에어리어의 진화원).");
                if (idx.Count == 0) { c.LastStepResolved = false; return; }
                var (x, fromBreed) = pool[idx[0]];
                Func<bool> take = fromBreed ? () => breed!.TakeSource(y => ReferenceEquals(y, x)) != null : () => c.Owner.Trash.Remove(x);
                if (!c.Engine.TryPlayByEffect(c.Owner, x, fromBreed ? PlayOrigin.Sources : PlayOrigin.Trash, take)) { c.LastStepResolved = false; return; }
                Diag46.Hit("playTrashOrBreedSource49");
                Emit(c, label, $"[{x.Name}] 등장 ({(fromBreed ? "육성 에어리어의 진화원" : "트래시")})");
                c.RecalculateFieldBuffs();
            }, null);
        }
        // "play 2 Digimon cards with different color and the [NSp] trait and a play cost of 7 or less from your hand without paying the cost" (EX7-037 — 서로 색이 다른 카드, 같은 색을 갖지 않는다)
        if ((m = Regex.Match(s, @"^play (?<n>\d+) Digimon cards with different colou?rs? and (?<rest>.+?) from your hand without paying (?:the|its|their) costs?$", I)).Success
            && ParseCardPick($"1 Digimon card with {m.Groups["rest"].Value}") is { } pd)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                var chosen = new List<CardData>();
                for (int k = 0; k < n; k++)
                {
                    var pool = c.Owner.Hand.Where(x => pd.Match(x) && !chosen.Contains(x) && !chosen.Any(y => y.AllColors.Intersect(x.AllColors).Any()) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand))
                        .Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.PlayCost).ToList();
                    if (pool.Count == 0) break;
                    var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, pool, 1, true, $"[{label}] 등장시킬 카드를 고르세요 ({k + 1}/{n} — 앞에서 고른 카드와 색이 달라야 합니다).");
                    if (idx.Count == 0) break;
                    chosen.Add(pool[idx[0]]);
                }
                int played = 0;
                foreach (var x in chosen)
                {
                    if (c.Owner.BattleArea.Count >= 12) break;
                    if (c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x))) played++;
                }
                if (played == 0) c.LastStepResolved = false;
                Diag46.Hit("playDifferentColors49");
                Emit(c, label, played > 0 ? $"색이 다른 디지몬 {played}장 등장" : "등장시킬 카드가 없습니다");
                c.RecalculateFieldBuffs();
            }, null);
        }
        // "play 1 [Gabumon] if that Digimon has [Greymon] in its name and/or 1 [Agumon] if it has [Garurumon] in its name from your hand or trash without paying the cost" (EX13-067 "and" — 맞는 것 전부 · EX4-061 "or" — 1장)
        if ((m = Regex.Match(s, @"^play 1 (?<a>\[[^\]]+\]) if (?:that Digimon|it) has (?<x>\[[^\]]+\]) in its name (?<j>and|or) 1 (?<b>\[[^\]]+\]) if (?:that Digimon|it) has (?<y>\[[^\]]+\]) in its name from your hand or trash without paying (?:the|its) costs?$", I)).Success)
        {
            string a = TargetSpec.Names(m.Groups["a"].Value)[0], b = TargetSpec.Names(m.Groups["b"].Value)[0];
            string x = TargetSpec.Names(m.Groups["x"].Value)[0], y = TargetSpec.Names(m.Groups["y"].Value)[0];
            bool both = m.Groups["j"].Value.Equals("and", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                var that = c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d)) ?? c.Other;   // 진화한 디지몬
                var wants = new List<string>();
                if (that != null && TargetSpec.NameHas(that.TopCard, x)) wants.Add(a);
                if (that != null && TargetSpec.NameHas(that.TopCard, y)) wants.Add(b);
                if (wants.Count == 0) { c.LastStepResolved = false; Emit(c, label, "진화한 디지몬의 이름이 맞지 않습니다"); return; }
                if (!both && wants.Count > 1)
                {
                    int k = c.Engine.ChooseOption(c.Owner, $"[{label}] 어느 카드를 등장시킬까요?", wants.Select(w => $"[{w}]").ToList(), 0);
                    wants = new List<string> { wants[Math.Clamp(k, 0, wants.Count - 1)] };
                }
                var done = new List<string>();
                foreach (var w in wants)
                {
                    if (c.Owner.BattleArea.Count >= 12) break;
                    var pool = c.Owner.Hand.Where(z => z.Type == CardType.Digimon && CardNames.Is(z, w)).Select(z => (Card: z, Hand: true))
                        .Concat(c.Owner.Trash.Where(z => z.Type == CardType.Digimon && CardNames.Is(z, w)).Select(z => (Card: z, Hand: false)))
                        .Where(p => c.Engine.CanPlayByEffect(c.Owner, p.Card, p.Hand ? PlayOrigin.Hand : PlayOrigin.Trash))
                        .OrderByDescending(p => p.Card.Level).ToList();
                    if (pool.Count == 0) continue;
                    var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool.Select(p => p.Card).ToList(), 1, true, $"[{label}] 등장시킬 [{w}]을(를) 고르세요 (패 / 트래시).");
                    if (idx.Count == 0) continue;
                    var (card0, hand) = pool[idx[0]];
                    if (c.Engine.TryPlayByEffect(c.Owner, card0, hand ? PlayOrigin.Hand : PlayOrigin.Trash, hand ? () => c.Owner.Hand.Remove(card0) : () => c.Owner.Trash.Remove(card0)))
                        done.Add(card0.Name);
                }
                if (done.Count == 0) c.LastStepResolved = false;
                Diag46.Hit("playByEvolvedName49");
                Emit(c, label, done.Count > 0 ? $"[{string.Join(", ", done)}] 등장" : "등장시킬 카드가 없습니다");
                c.RecalculateFieldBuffs();
            }, null);
        }
        return SecurityAndSources49(s, label);
    }

    /// <summary>시큐리티 · 진화원을 다루는 동작 (원래 규칙이 읽지 못한 것만)</summary>
    private static (Action<EffectContext>? Act, string? Error)? SecurityAndSources49(string s, string label)
    {
        Match m;
        // "place this Digimon on top of your security stack" — 조건 뒤 ("If this effect played, ~" EX9-021)
        if ((m = Regex.Match(s, @"^place this Digimon (?:on top of your security stack|as your top security card)(?: face down)?$", I)).Success)
        {
            return (c =>
            {
                if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) { c.LastStepResolved = false; return; }
                string name = c.Self.TopCard.Name;
                c.Engine.PlaceOnSecurity(c.Owner, c.Self, top: true);
                if (c.Owner.BattleArea.Contains(c.Self)) { c.LastStepResolved = false; return; }
                Diag46.Hit("thisDigimonToSecurity49");
                Emit(c, label, $"[{name}] 시큐리티 맨 위로", attackerRemoved: ReferenceEquals(c.Other, c.Self));
            }, null);
        }
        // "Trash that Digimon's digivolution cards" (P-023 — 시큐리티로 옮긴 디지몬은 이미 겹쳐진 카드를 파기했다: 필드에 남은 경우만)
        if (Regex.IsMatch(s, @"^trash that Digimon's digivolution cards ⟦after-security⟧$", I))
        {
            return (c =>
            {
                var d = c.LastTargets.FirstOrDefault();
                var zone = d == null ? null : c.Owner.BattleArea.Contains(d) ? c.Owner : c.Enemy.BattleArea.Contains(d) ? c.Enemy : null;
                if (d == null || zone == null) return;   // 필드를 떠났다 (진화원은 떠날 때 파기)
                var xs = c.Engine.TrashTopSources(zone, d, d.EvolutionSources.Count);
                if (xs.Count == 0) { c.LastStepResolved = false; return; }
                c.RecalculateFieldBuffs();
                Emit(c, label, $"[{d.TopCard.Name}]의 진화원 {xs.Count}장 파기");
            }, null);
        }
        // "place this card as the top security card" ([패] 【메인】 BT23-025 — 패의 이 카드를 시큐리티 맨 위에)
        if ((m = Regex.Match(s, @"^place this card as (?:the|your) (?<pos>top|bottom) security card$", I)).Success)
        {
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                var from = c.Owner.Hand.Contains(c.Card) ? c.Owner.Hand : c.Owner.Trash.Contains(c.Card) ? c.Owner.Trash : null;
                if (from == null) { c.LastStepResolved = false; Emit(c, label, "이 카드가 패 · 트래시에 없습니다"); return; }
                if (c.Engine.SecurityAddBlocked65(c.Owner)) { c.LastStepResolved = false; Emit(c, label, "시큐리티에 카드를 추가할 수 없습니다"); return; }   // v0.65.0 BT9-103
                from.Remove(c.Card);
                if (top) c.Owner.SecurityStack.Insert(0, c.Card); else c.Owner.SecurityStack.Add(c.Card);
                c.ThisCardMoved = true;
                c.Engine.NotifySecurityAdded(c.Owner, c.Card);
                Diag46.Hit("thisCardToSecurity49");
                Emit(c, label, $"이 카드를 시큐리티 맨 {(top ? "위" : "아래")}에 놓는다");
            }, null);
        }
        // "place 1 of each player's Digimon as the top security cards" (EX13-036 — 양쪽 플레이어의 디지몬 1마리씩 각자의 시큐리티 맨 위에)
        if (Regex.IsMatch(s, @"^place 1 of each player's Digimon as the top security cards?$", I))
        {
            return (c =>
            {
                var done = new List<string>();
                foreach (var side in new[] { c.Owner, c.Enemy })
                {
                    bool mine = ReferenceEquals(side, c.Owner);
                    var cands = side.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)   /* v0.51.0 자신의 디지몬도 (LM-020) */)
                        .OrderBy(d => mine ? d.GetCurrentDP() : -d.GetCurrentDP()).ToList();   // 추천: 자신은 약한 디지몬 · 상대는 강한 디지몬
                    if (cands.Count == 0) continue;
                    var pick = c.Engine.ChooseTargets(c.Owner, cands, 1, $"[{label}] 시큐리티 맨 위에 놓을 {(mine ? "자신" : "상대")}의 디지몬을 고르세요.", optional: false, preselected: cands[0]).FirstOrDefault() ?? cands[0];
                    string name = pick.TopCard.Name;
                    c.Engine.PlaceOnSecurity(side, pick, top: true);
                    if (!side.BattleArea.Contains(pick)) done.Add(name);
                }
                if (done.Count == 0) c.LastStepResolved = false;
                Diag46.Hit("eachPlayerToSecurity49");
                Emit(c, label, done.Count > 0 ? $"[{string.Join(", ", done)}] 각자의 시큐리티 맨 위로" : "대상이 없습니다");
            }, null);
        }
        // "Place all level 3 Digimon face down on top of their owners' security stacks in any order" (RB1-019 — 양쪽의 그 레벨 디지몬 전부)
        if ((m = Regex.Match(s, @"^place all level (?<lv>\d+) Digimon(?: face down)? on top of their owners'? security stacks?(?: face down)?(?: in any order)?$", I)).Success)
        {
            int lv = int.Parse(m.Groups["lv"].Value);
            return (c =>
            {
                int n = 0;
                foreach (var side in new[] { c.Owner, c.Enemy })
                    foreach (var d in side.BattleArea.Where(d => d.TamerProxyOf == null && d.TopCard.Level == lv && !c.Engine.IsUnaffectedBy(c.Owner, d)   /* v0.51.0 자신의 디지몬도 (LM-020) */).ToList())
                    {
                        c.Engine.PlaceOnSecurity(side, d, top: true);
                        if (!side.BattleArea.Contains(d)) n++;
                    }
                if (n == 0) c.LastStepResolved = false;
                Diag46.Hit("allLevelToSecurity49");
                Emit(c, label, n > 0 ? $"Lv.{lv} 디지몬 {n}마리를 각자의 시큐리티 맨 위로" : "대상이 없습니다");
            }, null);
        }
        // "reveal all of your opponent's security cards, and trash 1 of them, then shuffle the rest into their security stack" (BT10-086) ·
        // "~, and place 1 card among them on top of your opponent's deck, then shuffle the rest into their security stack" (LM-020)
        if ((m = Regex.Match(s, @"^reveal all of your opponent's security cards, and (?:(?<trash>trash 1 of them)|place 1 card among them on top of your opponent's deck), then shuffle the rest into their security stack$", I)).Success)
        {
            bool trash = m.Groups["trash"].Success;
            return (c =>
            {
                var sec = c.Enemy.SecurityStack;
                if (sec.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대의 시큐리티가 없습니다"); return; }
                // 추천: 상대에게 가장 쓸모 있는 카드 (옵션 · 시큐리티 효과 · 높은 DP)
                var ranked = sec.OrderByDescending(x => x.Type == CardType.Option ? 1 : 0).ThenByDescending(x => (x.EffectText ?? "").Contains("[Security]") ? 1 : 0).ThenByDescending(x => x.DP).ToList();
                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, ranked, 1, false,
                    $"[{label}] 공개한 상대의 시큐리티 중 {(trash ? "파기할" : "상대의 덱 위에 놓을")} 카드 1장을 고르세요.");
                var x = ranked[idx.Count > 0 ? idx[0] : 0];
                sec.Remove(x);
                c.Enemy.FaceUpSecurity.Remove(x);
                if (trash) c.Enemy.Trash.Add(x); else c.Enemy.MainDeck.Insert(0, x);
                foreach (var y in sec) c.Enemy.FaceUpSecurity.Remove(y);   // 남은 카드는 뒷면으로 섞는다
                c.Engine.ShuffleSecurity(c.Enemy);
                Diag46.Hit("revealOppSecurity49");
                Emit(c, label, $"상대의 시큐리티 {ranked.Count}장 공개 → [{x.Name}] {(trash ? "파기" : "상대의 덱 위로")} · 나머지를 섞어 시큐리티로");
            }, null);
        }
        // "reveal 1 card of the same level in your hand, then place that card on top of your security stack face down" (EX4-023 — 상대가 등장시킨 디지몬과 같은 Lv.)
        if (Regex.IsMatch(s, @"^reveal 1 card of the same level in your hand, then place that card on top of your security stack(?: face down)?$", I))
        {
            return (c =>
            {
                int lv = c.Other?.TopCard.Level ?? c.TriggerCard?.Level ?? 0;
                var pool = c.Owner.Hand.Where(x => lv > 0 && x.Level == lv && !c.Engine.SecurityAddBlocked65(c.Owner)).Distinct().OrderBy(x => x.PlayCost).ToList();   // v0.65.0 BT9-103
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"패에 Lv.{lv} 카드가 없습니다"); return; }
                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, pool, 1, false, $"[{label}] 공개하고 시큐리티 맨 위에 놓을 Lv.{lv} 카드를 고르세요.");
                var x = pool[idx.Count > 0 ? idx[0] : 0];
                c.Owner.Hand.Remove(x);
                c.Owner.SecurityStack.Insert(0, x);
                c.Engine.NotifySecurityAdded(c.Owner, x);
                Diag46.Hit("sameLevelHandToSecurity49");
                Emit(c, label, $"패의 [{x.Name}] 공개 → 시큐리티 맨 위에");
            }, null);
        }
        // "place up to 5 differently named Digimon cards with [Gammamon] in their names from your trash as this Digimon's bottom digivolution cards" (EX10-053) ·
        // "place up to 7 cards with different names and the [Seven Great Demon Lords] trait from your trash as this Digimon's bottom digivolution cards" (EX6-073)
        if ((m = Regex.Match(s, @"^place up to (?<n>\d+) (?:differently named (?<p1>.+?)|(?<p2>cards|Digimon cards) with different names and (?<rest>.+?)) from your trash (?:as this Digimon's bottom digivolution cards?|under this Digimon as its bottom digivolution cards?)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            string pickText = m.Groups["p1"].Success ? "1 " + Regex.Replace(m.Groups["p1"].Value, @"\bcards\b", "card", RegexOptions.IgnoreCase).Replace(" in their names", " in its name")
                : $"1 {(m.Groups["p2"].Value.StartsWith("Digimon", StringComparison.OrdinalIgnoreCase) ? "Digimon card" : "card")} with {m.Groups["rest"].Value}";
            if (ParseCardPick(pickText) is not { } pk) return null;
            return (c =>
            {
                if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) { c.LastStepResolved = false; return; }
                var chosen = new List<CardData>();
                while (chosen.Count < n)
                {
                    var pool = c.Owner.Trash.Where(x => pk.Match(x) && !chosen.Contains(x) && !chosen.Any(y => y.Name == x.Name))
                        .GroupBy(x => x.Name).Select(g => g.First()).OrderByDescending(x => x.Level).ToList();
                    if (pool.Count == 0) break;
                    var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Trash, pool, 1, true, $"[{label}] 진화원 아래에 놓을 카드를 고르세요 ({chosen.Count + 1}/{n}, 이름이 달라야 합니다 — 고르지 않으면 끝).");
                    if (idx.Count == 0) break;
                    chosen.Add(pool[idx[0]]);
                }
                foreach (var x in chosen) { c.Owner.Trash.Remove(x); c.Self.AddBottomSource(x); }
                if (chosen.Count == 0) c.LastStepResolved = false;
                c.RecalculateFieldBuffs();
                Diag46.Hit("differentNamesToSources49");
                Emit(c, label, chosen.Count > 0 ? $"트래시의 [{string.Join(", ", chosen.Select(x => x.Name))}] 진화원 아래에" : "놓을 카드가 없습니다");
            }, null);
        }
        // "return up to 7 non-Digi-Egg cards with the [X Antibody] trait from this Digimon's digivolution cards to the bottom of your deck in any order, if you do, gain 1 memory for each card returned" (BT9-111)
        if ((m = Regex.Match(s, @"^return up to (?<n>\d+) (?<nde>non-Digi-Egg )?cards with the (?<t>\[[^\]]+\]) trait from this Digimon's digivolution cards to the bottom of (?:your|the) deck(?: in any order)?, if you do, gain 1 memory for each card returned$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool nonEgg = m.Groups["nde"].Success;
            var t = TargetSpec.Names(m.Groups["t"].Value)[0];
            return (c =>
            {
                if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) { c.LastStepResolved = false; return; }
                var pool = c.Self.SourcesWhere(x => !c.Self.IsFaceDown(x) && TargetSpec.HasTrait(x, t) && (!nonEgg || x.Type != CardType.Digitama)).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "되돌릴 진화원이 없습니다"); return; }
                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, pool, Math.Min(n, pool.Count), true, $"[{label}] 덱 아래로 되돌릴 진화원을 {n}장까지 고르세요 (1장마다 메모리 +1).");
                var moved = new List<CardData>();
                foreach (int i in idx) { var x = pool[i]; if (c.Self.TakeSource(y => ReferenceEquals(y, x)) != null) { c.Owner.MainDeck.Add(x); moved.Add(x); } }
                if (moved.Count == 0) { c.LastStepResolved = false; return; }
                c.AddMemory(moved.Count);
                c.RecalculateFieldBuffs();
                Diag46.Hit("sourcesToDeckMemory49");
                Emit(c, label, $"진화원 {moved.Count}장을 덱 아래로 · 메모리 +{moved.Count}");
            }, null);
        }
        // "return the bottom 3 digivolution cards of 1 of your opponent's Digimon to the bottom of the deck" (EX6-061)
        if ((m = Regex.Match(s, @"^return the bottom (?<n>\d+) digivolution cards of 1 of your opponent's Digimon to the bottom of (?:the|its owner's|their) deck$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                var cands = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && d.EvolutionSources.Count > 0 && !c.Engine.IsUnaffectedBy(c.Owner, d))
                    .OrderByDescending(d => d.EvolutionSources.Count >= n ? 1 : 0).ThenByDescending(d => d.GetCurrentDP()).ToList();
                if (cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "진화원이 있는 상대의 디지몬이 없습니다"); return; }
                var d = c.Engine.ChooseTargets(c.Owner, cands, 1, $"[{label}] 진화원 아래 {n}장을 덱 아래로 되돌릴 상대의 디지몬을 고르세요.", optional: false, preselected: cands[0]).FirstOrDefault() ?? cands[0];
                if (c.Engine.StackProtected42(c.Enemy, d)) { c.LastStepResolved = false; Emit(c, label, "진화원을 옮길 수 없습니다"); return; }
                var moved = d.RemoveBottomSources(n);
                foreach (var x in moved) d.FaceDownSources.Remove(x);
                c.Enemy.MainDeck.AddRange(moved);
                c.LastTargets = new List<CardInstance> { d };
                if (moved.Count == 0) c.LastStepResolved = false;
                c.RecalculateFieldBuffs();
                Diag46.Hit("oppBottomSourcesToDeck49");
                Emit(c, label, $"[{d.TopCard.Name}]의 진화원 아래 {moved.Count}장을 덱 아래로");
            }, null);
        }
        return null;
    }
}
