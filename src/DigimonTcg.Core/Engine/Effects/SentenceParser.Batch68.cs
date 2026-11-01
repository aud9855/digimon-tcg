using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.68.0 미지원 문장 42차 묶음 — 원래 규칙(41차까지)이 읽지 못한 절만:
///  - EX12-069 · EX12-074 [시큐리티]【자신의 턴】 "When one of your ~ Digimon attacks, ~" — 시큐리티에 앞면으로 있는 동안 유발 (종합 룰 15-14-5-1, EffectSource.FaceUpSecurity68)
///  - LM-068 (옵션 쪽) "When one of your ~ Digimon attacks, if ~, you may use this card with the cost reduced by 3" — 패의 이 카드 (EffectSource.Hand68)
///  - BT19-053 앞면 시큐리티에서 등장 코스트 -8 하여 등장 · EX2-070 진화 코스트 3 이하로 진화할 수 있는 카드로 코스트 없이 진화
///  - BT14-078 한 절에 이어 적힌 두 효과 (【자신의 턴 종료 시】 ~. 【소멸 시】 ~)와 "이 효과로 파기한 카드 1장마다 Lv. 상한 +1" (⟦trashlv68⟧)
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth68;

    /// <summary>지금 읽는 [트래시] 형식 시점 효과가 실제로 있는 곳 (0 = 트래시, 1 = 앞면 시큐리티, 2 = 패)</summary>
    [ThreadStatic] private static int _zone68;

    /// <summary>core 파서가 [트래시] 시점 효과를 등록할 곳</summary>
    private static EffectSource ZoneSource68 => _zone68 switch { 1 => EffectSource.FaceUpSecurity68, 2 => EffectSource.Hand68, _ => EffectSource.Trash };

    private static string? TryParse68(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse67(rawClause, inherited, card, def);
        if (err == null || _depth68 > 0) return err;
        string t = Normalize58(rawClause);
        _depth68++;
        try
        {
            // 한 절에 이어 적힌 두 효과 (BT14-078 "[End of Your Turn] ~. [On Deletion] ~") — 둘 다 읽을 수 있을 때만
            if (SplitTwoEffects68(t) is { } parts)
            {
                if (parts.Any(p => TryParse68Inner(p, inherited, card, null) != null)) return err;
                if (def != null) foreach (var p in parts) TryParse68Inner(p, inherited, card, def);
                return null;
            }
            var (alt, zone) = ClauseRewrite68(t, card);
            if (alt == t) return err;
            if (TryParse68Inner(alt, inherited, card, null, zone) != null) return err;
            if (def != null) TryParse68Inner(alt, inherited, card, def, zone);
            return null;
        }
        finally { _depth68--; }
    }

    private static string? TryParse68Inner(string clause, bool inherited, CardData card, CardScriptDefinition? def, int zone = 0)
    {
        if (zone == 0)
        {
            var (alt, z) = ClauseRewrite68(clause, card);
            if (alt != clause) { clause = alt; zone = z; }
        }
        int outer = _zone68;
        _zone68 = zone;
        try { return TryParse67(clause, inherited, card, def); }
        finally { _zone68 = outer; }
    }

    // ---- 한 절의 두 효과 ----

    private static readonly Regex TwoEffects68 = new(@"^(?<a>\[(?:End of Your Turn|Start of Your Turn|On Play|When Digivolving|When Attacking)\].+?[.)])\s+(?<b>\[(?:On Deletion|On Play|When Digivolving|When Attacking|End of Your Turn)\]\s*.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string[]? SplitTwoEffects68(string clause)
    {
        var m = TwoEffects68.Match(clause.Trim());
        return m.Success ? new[] { m.Groups["a"].Value.Trim(), m.Groups["b"].Value.Trim() } : null;
    }

    // ---- 표시 (값 묶음은 번호로) ----

    private static readonly ConcurrentDictionary<string, int> SpecIds68 = new();
    private static readonly ConcurrentDictionary<int, string[]> Specs68 = new();

    internal static string[]? Spec68(int id) => Specs68.TryGetValue(id, out var s) ? s : null;

    private static string Mark68(string kind, params string[] values)
    {
        string key = kind + "\u0001" + string.Join("\u0001", values);
        int id = SpecIds68.GetOrAdd(key, _ => { int k; lock (Specs68) { k = Specs68.Count + 1; Specs68[k] = values; } return k; });
        return $"⟦{kind}68:{id}⟧";
    }

    // EX12-069 · EX12-074 "[Security] [Your Turn] (Once Per Turn) When one of your ~ Digimon attacks, ~"
    internal static readonly Regex SecurityEventRe68 = new(@"^\[Security\]\s*(?<lab>\[Your Turn\]\s*(?:\[Once Per Turn\]\s*)?)When (?<ev>(?:one|any) of your [^,]+? Digimon attacks?), (?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // LM-068 (옵션 쪽) "When one of your [Evil|Dark Dragon|Evil Dragon] trait Digimon attacks, if your hand has 4 or fewer cards, you may use this card with the cost reduced by 3."
    internal static readonly Regex HandUseRe68 = new(@"^When (?<ev>(?:one|any) of your [^,]+? Digimon attacks?), (?<rest>(?:if [^,]+, )?you may use this card with the (?:use |memory )?cost reduced by \d+\.?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT14-078 "[On Deletion] You may trash up to 3 [Dark Animal] or [SoC] trait cards from your hand. Then, delete 1 of your opponent's level 3 or lower Digimon. For each card you trashed with this effect, increase this level by 1."
    internal static readonly Regex TrashLevelRe68 = new(@"\bYou may trash up to (?<n>\d+) (?<pick>[^.]+?) from your hand\. Then, delete 1 of your opponent's level (?<lv>\d+) or lower Digimon\. For each card you trashed with this effect, increase this level by 1\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // BT25-039 "[Security] [End of Your Turn] ~" — 시큐리티에 앞면으로 있는 동안 (15-14-5-1)
    internal static readonly Regex SecurityEotRe68 = new(@"^\[Security\]\s*(?<lab>\[End of Your Turn\]\s*(?:\[Once Per Turn\]\s*)?)(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT20-077 "Trash cards in your hand until it has 4 left. Then, play 1 8000 DP or lower Digimon card from your trash without paying the cost. For each card this effect trashed, remove 2000 from this effect's DP maximum."
    internal static readonly Regex HandToRe68 = new(@"\bTrash cards in your hand until it has (?<n>\d+) left\. Then, play 1 (?<dp>\d+) DP or lower Digimon card from your trash without paying the cost\. For each card this effect trashed, remove (?<r>\d+) from this effect's DP maximum\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT15-099 "~. When a card with [Myotismon] in its text is trashed by this effect, ＜Draw 2＞ (Draw 2 cards from your deck.)"
    internal static readonly Regex TrashedTextRe68 = new(@"\. When a card with (?<x>\[[^\]]+\]) in its text is trashed by this effect, (?<d>＜Draw \d+＞)(?: \([^)]*\))?\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT17-102 "You may play 1 Tamer card with [Tai Kamiya] or [Kari Kamiya] in its name from your hand without paying the cost, or hatch in your breeding area."
    internal static readonly Regex TamerOrHatchRe68 = new(@"\bYou may play 1 Tamer card with (?<x>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) in its name from your hand without paying the cost, or hatch in your breeding area\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"[Evil|Dark Dragon|Evil Dragon] trait" (LM-068 영어 데이터) → "[Evil], [Dark Dragon] or [Evil Dragon] trait"</summary>
    private static string PipeTraits68(string s) =>
        Regex.Replace(s, @"\[(?<x>[^\]|]+(?:\|[^\]|]+)+)\]", m =>
        {
            var xs = m.Groups["x"].Value.Split('|').Select(x => $"[{x.Trim()}]").ToList();
            return xs.Count == 2 ? $"{xs[0]} or {xs[1]}" : string.Join(", ", xs.Take(xs.Count - 1)) + " or " + xs[^1];
        });

    /// <summary>같은 뜻 다른 표기 · 이 묶음의 동작 표시 (zone: [트래시] 형식으로 읽은 시점 효과가 실제로 있는 곳 — 1 앞면 시큐리티 · 2 패)</summary>
    internal static (string Text, int Zone) ClauseRewrite68(string rawClause, CardData? card)
    {
        string s = rawClause.Trim();
        Match m;
        if ((m = SecurityEventRe68.Match(s)).Success && card is not { Type: CardType.Tamer })
            return ($"[Trash] {m.Groups["lab"].Value}When {PipeTraits68(m.Groups["ev"].Value)}, {m.Groups["rest"].Value}", 1);
        if (card is null or { Type: CardType.Option } && (m = HandUseRe68.Match(s)).Success)
            return ($"[Trash] When {PipeTraits68(m.Groups["ev"].Value)}, {m.Groups["rest"].Value}", 2);
        if ((m = SecurityEotRe68.Match(s)).Success && card is not { Type: CardType.Tamer })
        {
            // 턴 종료 시의 "play ~ from your hand with the cost reduced by N" — 메모리가 이미 상대 쪽이어도 낼 수 있다 (⟦eotplay68⟧)
            string rest = Regex.Replace(m.Groups["rest"].Value, @"^You may play (?<p>1 [^.]+?) from your hand with the (?:play |memory )?cost reduced by (?<r>\d+)\.", x => Mark68("eotplay", x.Groups["p"].Value, x.Groups["r"].Value) + ".", RegexOptions.IgnoreCase);
            return ($"[Trash] {m.Groups["lab"].Value}{rest}", 1);
        }
        s = TrashLevelRe68.Replace(s, x => Mark68("trashlv", x.Groups["n"].Value, x.Groups["pick"].Value, x.Groups["lv"].Value) + ".");
        s = HandToRe68.Replace(s, x => Mark68("handto", x.Groups["n"].Value, x.Groups["dp"].Value, x.Groups["r"].Value) + ".");
        s = TrashedTextRe68.Replace(s, x => ". Then, " + Mark68("trashedtext", x.Groups["x"].Value, x.Groups["d"].Value) + ".");
        s = TamerOrHatchRe68.Replace(s, x => Mark68("tamerhatch", x.Groups["x"].Value) + ".");
        return (s, 0);
    }

    /// <summary>렌더러용: 바꿔 쓴 절 (zone 없이)</summary>
    internal static string ClauseRewriteText68(string rawClause, CardData? card = null) => ClauseRewrite68(rawClause, card).Text;

    /// <summary>이 묶음의 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep에서 앞의 묶음 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction68(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = _zone68 == 1 ? "시큐리티 (앞면)" : _zone68 == 2 ? "패의 이 카드"
            : string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        Match m;
        // EX12-069 "you may play 1 [VB] trait Digimon card of the same level from your hand with the cost reduced by 3" — 어택한 디지몬과 같은 Lv.
        if ((m = Regex.Match(s, @"^(?:you may )?play (?<pick>1 .+?) of the same level from your hand with the (?:play |memory )?cost reduced by (?<red>\d+)$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } sl)
        {
            var match = sl.Match;
            int red = int.Parse(m.Groups["red"].Value);
            return (c => PlayPaid68(c, label, c.Owner.Hand, PlayOrigin.Hand, x => c.Other != null && x.Level == c.Other.TopCard.Level && match(x), red, "패"), null);
        }
        // BT19-053 "You may play 1 [Royal Base] trait Digimon card from your face-up security cards with the play cost reduced by 8"
        if ((m = Regex.Match(s, @"^(?:you may )?play (?<pick>1 .+?) from your face[- ]up security (?:cards|stack) with the (?:play |memory )?cost reduced by (?<red>\d+)$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } fu)
        {
            var match = fu.Match;
            int red = int.Parse(m.Groups["red"].Value);
            return (c => PlayPaid68(c, label, null, PlayOrigin.Security, match, red, "앞면 시큐리티"), null);
        }
        // LM-068 (옵션 쪽, 패의 이 카드) "you may use this card with the cost reduced by 3"
        if ((m = Regex.Match(s, @"^(?:you may )?use this card with the (?:use |memory )?cost reduced by (?<red>\d+)$", RegexOptions.IgnoreCase)).Success)
        {
            if (_zone68 != 2) return (null, "미지원: 패에 있는 이 카드의 시점 효과로만 사용");   // 필드의 효과로 읽지 않는다 (LM-068 옵션 쪽 — 패의 이 카드)
            int red = int.Parse(m.Groups["red"].Value);
            return (c => UseThisFromHand68(c, label, red), null);
        }
        // EX2-070 "1 of your Digimon may digivolve into a Digimon card in your hand that can digivolve for a digivolution cost of 3 or less without paying its digivolution cost"
        if ((m = Regex.Match(s, @"^(?:1|one) of your Digimon may digivolve into a Digimon card in your hand that can digivolve for a digivolution cost of (?<n>\d+) or less without paying (?:its|the) (?:digivolution |memory )?cost$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => EvoCostAtMost68(c, label, n), null);
        }
        // BT18-033 [패] [메인] "play this card in your breeding area without paying the cost" — 비어 있는 육성 에어리어에 (【등장 시】 없음 — 공식 Q&A BT15-068 Q1)
        if (Regex.IsMatch(s, @"^play this card in your breeding area without paying (?:the|its) cost$", RegexOptions.IgnoreCase) && card.Type == CardType.Digimon)
            return (c => PlayThisToBreeding68(c, label), null);
        // BT22-075 "you may play 1 of this Digimon's link cards without paying the cost" (떠나려 할 때 — 링크 카드는 아직 이 디지몬 옆에)
        if (Regex.IsMatch(s, @"^(?:you may )?play 1 of this Digimon's link cards without paying (?:the|its) cost$", RegexOptions.IgnoreCase))
            return (c => PlayLinkCard68(c, label), null);
        // BT25-039 "you may place this card as the played Digimon's bottom digivolution card" (앞면 시큐리티의 이 카드)
        if (Regex.IsMatch(s, @"^(?:you may )?place this card as the played Digimon's bottom digivolution card$", RegexOptions.IgnoreCase))
            return _zone68 == 1 ? (c => ThisUnderPlayed68(c, label), null) : (null, "미지원: 앞면 시큐리티의 이 카드만");
        var mk = Regex.Match(s, @"^⟦(?<k>[a-z]+)68:(?<id>\d+)⟧$");
        if (!mk.Success || Spec68(int.Parse(mk.Groups["id"].Value)) is not { } v) return null;
        switch (mk.Groups["k"].Value)
        {
            case "eotplay":
            {
                var pick = ParseCardPick(v[0]) ?? ParseNamePick(v[0]);
                if (pick == null) return (null, $"미지원 등장 조건 ({v[0]})");
                var match = pick.Value.Match;
                int red = int.Parse(v[1]);
                return (c => EotPlay68(c, label, match, red), null);
            }
            case "handto":
            {
                int n = int.Parse(v[0]), dp = int.Parse(v[1]), r = int.Parse(v[2]);
                return (c => HandToPlay68(c, label, n, dp, r), null);
            }
            case "trashedtext":
            {
                var xs = TargetSpec.Names(v[0]);
                int draw = int.Parse(Regex.Match(v[1], @"\d+").Value);
                return (c =>
                {
                    if (c.RefCard38 is not { } x || !xs.Any(name => TargetSpec.TextHas(x, name))) { c.LastStepResolved = false; return; }
                    int drawn = 0;
                    for (int i = 0; i < draw; i++) if (c.Draw() != null) drawn++;
                    Diag46.Hit("trashedText68");
                    Emit(c, label, $"파기한 [{x.Name}]에 {v[0]}이(가) 기술되어 있어 ≪{draw} 드로우≫ ({drawn}장)");
                }, null);
            }
            case "tamerhatch":
            {
                var xs = TargetSpec.Names(v[0]);
                return (c => TamerOrHatch68(c, label, xs), null);
            }
            case "trashlv":
            {
                var pick = ParseCardPick("1 " + Regex.Replace(v[1], @"\bcards$", "card", RegexOptions.IgnoreCase)) ?? ParseNamePick("1 " + v[1]);
                if (pick == null) return (null, $"미지원 파기 조건 ({v[1]})");
                int n = int.Parse(v[0]), lv = int.Parse(v[2]);
                var match = pick.Value.Match;
                return (c => TrashForLevel68(c, label, n, match, lv), null);
            }
        }
        return null;
    }

    // ---- 코스트를 내고 등장 (패 · 앞면 시큐리티) ----

    /// <summary>
    /// 줄인 코스트를 내고 등장 (메모리가 상대 쪽으로 넘어가지 않는 카드만 후보 — "~ from your hand with the play cost reduced by 3"과 같은 기준).
    /// zone = null이면 자신의 앞면 시큐리티
    /// </summary>
    private static void PlayPaid68(EffectContext c, string label, List<CardData>? zone, PlayOrigin origin, Func<CardData, bool> match, int red, string where)
    {
        int CostOf(CardData x) => c.Engine.ReducedPlayCost(c.Owner, x, Math.Max(0, x.PlayCost - red), x.PlayCost);
        var src = zone ?? GoldfishEngine.FaceUpCards(c.Owner);
        var pool = src.Where(x => x.Type == CardType.Digimon && match(x) && CostOf(x) <= Math.Max(0, c.OwnerMemory) && c.Engine.CanPlayByEffect(c.Owner, x, origin))
            .Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.PlayCost).ToList();
        if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, $"{where}에서 코스트 -{red}로 등장시킬 수 있는 카드가 없습니다"); return; }
        var x = c.Engine.ChooseCards(c.Owner, zone == null ? CardChoiceZone.Other : CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] {where}에서 등장시킬 카드를 고르세요 (등장 코스트 -{red}).").FirstOrDefault() ?? pool[0];
        int pc = CostOf(x);
        bool ok = c.Engine.TryPlayByEffect(c.Owner, x, origin, () =>
        {
            if (zone != null) return zone.Remove(x);
            int at = c.Owner.SecurityStack.IndexOf(x);
            if (at < 0 || !GoldfishEngine.IsFaceUp(c.Owner, x)) return false;
            c.Owner.SecurityStack.RemoveAt(at);
            c.Owner.FaceUpSecurity.Remove(x);
            c.Engine.NotifySecurityRemoved(c.Owner, x);
            return true;
        }, payCost: pc);
        if (!ok) { c.LastStepResolved = false; return; }
        Diag46.Hit(zone == null ? "faceUpSecPaidPlay68" : "sameLevelPaidPlay68");
        Emit(c, label, $"{where}의 [{x.Name}] 등장 (코스트 {pc})");
    }

    // ---- LM-068 패의 이 카드를 사용 ----

    private static void UseThisFromHand68(EffectContext c, string label, int red)
    {
        var card = c.Card;
        if (card == null || !c.Owner.Hand.Contains(card)) { c.LastStepResolved = false; return; }
        int cost = Math.Max(0, CardScripts.EffectivePlayCost(c.Owner, card, c.Enemy) - red);
        if (EffectRules.OptionUseBlocked(c.State, c.Owner) || ActionValidator.MissingOptionColors(c.Owner, card, c.Enemy).Count > 0 || cost > Math.Max(0, c.OwnerMemory))
        { c.LastStepResolved = false; Emit(c, label, $"패의 [{card.Name}]을(를) 지금 사용할 수 없습니다 (색 조건 · 메모리)"); return; }
        // 추천: 옵션 쪽 [메인]으로 소멸시킬 상대의 디지몬이 있을 때 (그 밖엔 묻는 대로)
        bool rec = c.Enemy.BattleArea.Any(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d));
        if (!(c.OwnerIsPlayer && c.Engine.PlayerChoices != null) && !rec) { c.LastStepResolved = false; return; }   // 플레이어는 "~할 수 있다"에서 이미 물었다
        if (!c.Owner.Hand.Remove(card)) { c.LastStepResolved = false; return; }
        Diag46.Hit("handUse68");
        c.Engine.UseOptionPaid43(c.Owner, card, cost);
        c.RecalculateFieldBuffs();
    }

    // ---- EX2-070 진화 코스트 N 이하로 진화할 수 있는 카드 ----

    /// <summary>
    /// 자신의 디지몬 1마리를 패의, 그 디지몬에서 진화 코스트 N 이하로 진화할 수 있는 디지몬 카드로 코스트 없이 진화 (공식 Q&amp;A EX2-070 Q1: 특수 진화의 코스트도 — ActionValidator.EvolutionCost).
    /// 추천: Lv. · DP가 가장 높은 카드
    /// </summary>
    private static void EvoCostAtMost68(EffectContext c, string label, int n)
    {
        var pairs = new List<(CardInstance D, CardData X)>();
        foreach (var d in c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && !EffectRules.IsDigivolveLocked(c.Engine.State, d)))
            foreach (var x in c.Owner.Hand.Where(x => x.Type == CardType.Digimon).Distinct())
                if (ActionValidator.ValidateEvolution(d, x).CanExecute && ActionValidator.EvolutionCost(d, x) <= n)
                    pairs.Add((d, x));
        if (pairs.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"진화 코스트 {n} 이하로 진화할 수 있는 카드가 없습니다"); return; }
        var ranked = pairs.OrderByDescending(p => p.X.Level).ThenByDescending(p => p.X.DP).ThenByDescending(p => p.D.EvolutionSources.Count).ToList();
        var bases = ranked.Select(p => p.D).Distinct().ToList();
        var d0 = bases.Count == 1 ? bases[0] : c.Engine.ChooseTargets(c.Owner, bases, 1, $"[{c.Card.Name}] 진화시킬 자신의 디지몬을 고르세요.", optional: false, preselected: ranked[0].D).FirstOrDefault() ?? ranked[0].D;
        var cards = ranked.Where(p => ReferenceEquals(p.D, d0)).Select(p => p.X).ToList();
        if (cards.Count == 0) { c.LastStepResolved = false; return; }
        var x0 = cards.Count == 1 ? cards[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, cards, 1, upTo: false, $"[{c.Card.Name}] 진화할 카드를 고르세요 (진화 코스트 {n} 이하).").FirstOrDefault() ?? cards[0];
        Diag46.Hit("evoCostAtMost68");
        string before = d0.TopCard.Name;
        c.Engine.EvolveByEffect(c.Owner, d0, x0, 0, c.Owner.Hand);
        c.LastTargets = new List<CardInstance> { d0 };
        Emit(c, label, $"[{before}] → [{x0.Name}] 코스트 없이 진화");
    }

    // ---- BT14-078 파기한 카드 1장마다 Lv. 상한 +1 ----

    /// <summary>
    /// 패의 조건에 맞는 카드 n장까지를 파기할 수 있다. 그 후, Lv. lv + (파기한 장수) 이하의 상대의 디지몬 1마리를 소멸 (공식 한글 카드 문장: 이 효과로 선택할 수 있는 Lv. 상한 +1).
    /// 추천: 소멸시킬 수 있는 가장 Lv. · DP가 높은 상대의 디지몬에 필요한 만큼만 파기
    /// </summary>
    private static void TrashForLevel68(EffectContext c, string label, int n, Func<CardData, bool> match, int lv)
    {
        var hand = c.Owner.Hand.Where(x => match(x) && !ReferenceEquals(x, c.Card)).OrderBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
        int max = Math.Min(n, hand.Count);
        var foes = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
        var best = foes.Where(d => d.TopCard.Level <= lv + max).OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).FirstOrDefault();
        int want = best == null ? 0 : Math.Max(0, best.TopCard.Level - lv);
        var picks = new List<CardData>();
        if (max > 0 && (c.OwnerIsPlayer && c.Engine.PlayerChoices != null || want > 0))
            picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, hand, c.OwnerIsPlayer && c.Engine.PlayerChoices != null ? max : Math.Min(want, max), upTo: true, $"[{c.Card.Name}] 파기할 패를 고르세요 ({n}장까지 — 1장마다 소멸시킬 수 있는 Lv. 상한 +1).");
        foreach (var x in picks) { c.Owner.Hand.Remove(x); c.Owner.Trash.Add(x); }
        if (picks.Count > 0) c.Engine.NotifyHandTrashed(c.Owner, picks);
        int cap = lv + picks.Count;
        var pool = foes.Where(d => c.Enemy.BattleArea.Contains(d) && d.TopCard.Level <= cap).OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).ToList();
        Diag46.Hit("trashForLevel68");
        if (pool.Count == 0) { Emit(c, label, $"패 {picks.Count}장 파기 · Lv.{cap} 이하의 상대의 디지몬이 없습니다"); if (picks.Count == 0) c.LastStepResolved = false; return; }
        var d0 = pool.Count == 1 ? pool[0] : c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 소멸시킬 Lv.{cap} 이하의 상대의 디지몬을 고르세요.", optional: false, preselected: pool[0]).FirstOrDefault() ?? pool[0];
        string name = d0.TopCard.Name;
        c.DeleteEnemy(d0);
        c.LastTargets = new List<CardInstance> { d0 };
        Emit(c, label, $"패 {picks.Count}장 파기 → Lv.{cap} 이하의 상대의 [{name}] 소멸");
    }

    // ---- BT25-039 턴 종료 시 패에서 코스트를 내고 등장 ----

    /// <summary>
    /// 【자신의 턴 종료 시】 패의 카드를 줄인 코스트로 등장 — 턴 종료 시에는 메모리가 이미 상대 쪽이므로, 낼 수 있는 만큼 낸다 (메모리는 상대 쪽 10에서 멈춘다 — 종합 룰 · ActionValidator와 같다).
    /// 추천: 상대에게 넘어갈 메모리가 7 이하일 때
    /// </summary>
    private static void EotPlay68(EffectContext c, string label, Func<CardData, bool> match, int red)
    {
        int CostOf(CardData x) => c.Engine.ReducedPlayCost(c.Owner, x, Math.Max(0, x.PlayCost - red), x.PlayCost);
        var pool = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand)).Distinct().OrderByDescending(x => x.Level).ToList();
        if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, "패에서 등장시킬 카드가 없습니다"); return; }
        var x = pool[0];
        int cost = CostOf(x);
        int after = Math.Max(-10, c.OwnerMemory - cost);
        if (!c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 패의 [{x.Name}]을(를) 코스트 {cost}로 등장시킬까요? (메모리 {c.OwnerMemory} → {after})", after >= -7)) { c.LastStepResolved = false; return; }
        if (pool.Count > 1) x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] 등장시킬 카드를 고르세요 (코스트 -{red}).").FirstOrDefault() ?? x;
        cost = CostOf(x);
        int pay = Math.Min(cost, Math.Max(0, c.OwnerMemory + 10));
        if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x), payCost: pay)) { c.LastStepResolved = false; return; }
        Diag46.Hit("eotPlay68");
        Emit(c, label, $"패의 [{x.Name}] 등장 (코스트 {cost})");
    }

    // ---- BT18-033 패의 이 카드를 육성 에어리어에 ----

    private static void PlayThisToBreeding68(EffectContext c, string label)
    {
        var card = c.Card;
        if (card == null || !c.Owner.Hand.Contains(card) || c.Owner.BreedingArea != null) { c.LastStepResolved = false; Emit(c, label, "육성 에어리어에 등장시킬 수 없습니다"); return; }
        if (!c.Engine.TryPlayByEffect(c.Owner, card, PlayOrigin.Hand, () => c.Owner.Hand.Remove(card), toBreeding: true)) { c.LastStepResolved = false; return; }
        Diag46.Hit("playThisToBreeding68");
        Emit(c, label, $"패의 [{card.Name}]을(를) 육성 에어리어에 코스트 없이 등장");
    }

    // ---- BT22-075 이 디지몬의 링크 카드를 등장 ----

    private static void PlayLinkCard68(EffectContext c, string label)
    {
        var self = c.Self;
        if (self == null || self.LinkedCards.Count == 0 || c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; Emit(c, label, "등장시킬 링크 카드가 없습니다"); return; }
        var pool = self.LinkedCards.Where(x => x.Type == CardType.Digimon && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Unknown)).Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 수 있는 링크 카드가 없습니다"); return; }
        var x = pool.Count == 1 ? pool[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: false, $"[{c.Card.Name}] 등장시킬 링크 카드를 고르세요.").FirstOrDefault() ?? pool[0];
        if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Unknown, () => self.LinkedCards.Remove(x))) { c.LastStepResolved = false; return; }
        c.RecalculateFieldBuffs();
        Diag46.Hit("playLinkCard68");
        Emit(c, label, $"링크 카드 [{x.Name}] 코스트 없이 등장");
    }

    // ---- BT25-039 앞면 시큐리티의 이 카드를 등장한 디지몬의 진화원 아래에 ----

    private static void ThisUnderPlayed68(EffectContext c, string label)
    {
        var card = c.Card;
        var played = c.PlayedThisEffect.LastOrDefault(d => c.Owner.BattleArea.Contains(d));
        int at = card == null ? -1 : c.Owner.SecurityStack.IndexOf(card);
        if (played == null || card == null || at < 0) { c.LastStepResolved = false; return; }
        c.Owner.SecurityStack.RemoveAt(at);
        c.Owner.FaceUpSecurity.Remove(card);
        c.Engine.NotifySecurityRemoved(c.Owner, card);
        played.AddBottomSource(card);
        c.RecalculateFieldBuffs();
        Diag46.Hit("thisUnderPlayed68");
        Emit(c, label, $"이 카드를 [{played.TopCard.Name}]의 진화원 맨 아래에");
    }

    // ---- BT20-077 패가 N장이 될 때까지 파기 · 파기한 1장마다 DP 상한 -R ----

    private static void HandToPlay68(EffectContext c, string label, int n, int dp, int r)
    {
        int k = Math.Max(0, c.Owner.Hand.Count - n);
        var trashed = new List<CardData>();
        if (k > 0)
        {
            var ranked = c.Owner.Hand.OrderBy(x => x.Type == CardType.Option ? 0 : 1).ThenBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
            trashed = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, ranked, k, upTo: false, $"[{c.Card.Name}] 패가 {n}장이 될 때까지 파기할 카드 {k}장을 고르세요.");
            foreach (var x in trashed) { c.Owner.Hand.Remove(x); c.Owner.Trash.Add(x); }
            if (trashed.Count > 0) c.Engine.NotifyHandTrashed(c.Owner, trashed);
        }
        int max = dp - r * trashed.Count;
        var pool = c.Owner.Trash.Where(x => x.Type == CardType.Digimon && x.DP <= max && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Trash))
            .Distinct().OrderByDescending(x => x.DP).ThenByDescending(x => x.Level).ToList();
        Diag46.Hit("handToPlay68");
        if (pool.Count == 0 || max < 0 || c.Owner.BattleArea.Count >= 12) { Emit(c, label, $"패 {trashed.Count}장 파기 · DP {Math.Max(0, max)} 이하로 등장시킬 카드가 없습니다"); return; }
        var p = pool.Count == 1 ? pool[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, pool, 1, upTo: false, $"[{c.Card.Name}] 트래시에서 등장시킬 DP {max} 이하의 디지몬 카드를 고르세요.").FirstOrDefault() ?? pool[0];
        if (!c.Engine.TryPlayByEffect(c.Owner, p, PlayOrigin.Trash, () => c.Owner.Trash.Remove(p))) { c.LastStepResolved = false; return; }
        Emit(c, label, $"패 {trashed.Count}장 파기 → 트래시의 [{p.Name}] (DP {p.DP}) 코스트 없이 등장 (DP 상한 {max})");
    }

    // ---- BT17-102 테이머 등장 또는 부화 ----

    private static void TamerOrHatch68(EffectContext c, string label, List<string> names)
    {
        var tamers = c.Owner.Hand.Where(x => x.Type == CardType.Tamer && names.Any(n => TargetSpec.NameHas(x, n)) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand)).Distinct().ToList();
        bool canHatch = c.Owner.BreedingArea == null && c.Owner.DigitamaDeck.Count > 0;
        if (tamers.Count == 0 && !canHatch) { c.LastStepResolved = false; Emit(c, label, "등장시킬 테이머가 없고 부화할 수 없습니다"); return; }
        var options = new List<string>();
        if (tamers.Count > 0) options.Add("패의 테이머를 코스트 없이 등장");
        if (canHatch) options.Add("육성 에어리어에 부화");
        options.Add("하지 않는다");
        int pick = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 어느 쪽을 할까요?", options, 0), 0, options.Count - 1);
        string chosen = options[pick];
        if (chosen.StartsWith("패의", StringComparison.Ordinal))
        {
            var t = tamers.Count == 1 ? tamers[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, tamers, 1, upTo: false, $"[{c.Card.Name}] 등장시킬 테이머를 고르세요.").FirstOrDefault() ?? tamers[0];
            if (!c.Engine.TryPlayByEffect(c.Owner, t, PlayOrigin.Hand, () => c.Owner.Hand.Remove(t))) { c.LastStepResolved = false; return; }
            Diag46.Hit("tamerOrHatchTamer68");
            Emit(c, label, $"패의 [{t.Name}] 코스트 없이 등장");
        }
        else if (chosen.StartsWith("육성", StringComparison.Ordinal))
        {
            var egg = c.Engine.Hatch(c.Owner);
            if (egg == null) { c.LastStepResolved = false; return; }
            Diag46.Hit("tamerOrHatchHatch68");
            Emit(c, label, $"[{egg.Name}] 부화");
        }
        else c.LastStepResolved = false;
    }
}
