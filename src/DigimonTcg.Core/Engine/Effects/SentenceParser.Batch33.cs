using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.33.0 미지원 문장 7차 묶음 — "~를 놓는 것으로" 비용 (다른 디지몬 · 이 디지몬 · 테이머를 진화원으로, 테이머 아래, 시큐리티 · 덱으로) ·
/// 사건 (테이머가 떠나려 할 때 · 패로 되돌아갔을 때 · 배틀 에어리어를 떠났을 때 등) · 조건 ("If this ~" · "If you ~"). 앞의 규칙에 걸리지 않은 것만 본다
/// </summary>
internal static partial class SentenceParser
{
    // =====================================================================
    // 비용
    // =====================================================================

    /// <summary>비용 표기 정리 (원래 규칙이 읽는 표기로) — 바뀌지 않으면 그대로</summary>
    internal static string NormalizeCost33(string cost)
    {
        string t = cost.Trim();
        t = Regex.Replace(t, @"\brom your hand\b", "from your hand", RegexOptions.IgnoreCase);   // BT19-001 원문 오타
        t = Regex.Replace(t, @"\bin text\b", "in its text", RegexOptions.IgnoreCase);              // RB1-10 "with [Gammamon] in text"
        t = Regex.Replace(t, @",\s+from your\b", " from your", RegexOptions.IgnoreCase);            // BT14-039 "in its name, from your trash"
        t = Regex.Replace(t, @"\bor with (?=\[|the \[)", "or ", RegexOptions.IgnoreCase);           // ST19-13 "with [Puppet] trait or with [Numemon] in its name"
        t = Regex.Replace(t, @"\btrait or the (?=\[)", "trait or ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\[(?<a>[^\]]+)\] trait or \[(?<b>[^\]]+)\] trait\b", "[${a}] or [${b}] trait");   // BT13-075
        // "1 [X Antibody] or level 6 card" → "1 [X Antibody] or 1 level 6 card" (BT10-086)
        t = Regex.Replace(t, @"^(?<v>placing|trashing|returning) (?<n>\d+) (?<x>\[[^\]]+\]) or level (?<lv>\d+) card\b", "${v} ${n} ${x} or ${n} level ${lv} card", RegexOptions.IgnoreCase);
        // 이 디지몬의 진화원 맨 아래 — "at the bottom of this Digimon's digivolution cards" · "under this Digimon (in any order) as its bottom digivolution card(s)"
        t = Regex.Replace(t, @"^(?<head>placing .+?) (?:at the bottom of this (?:Digimon|card)'s digivolution cards|under this Digimon(?: in any order)? as its bottom digivolution cards?)(?: in any order)?$", "${head} as this Digimon's bottom digivolution card", RegexOptions.IgnoreCase);
        // 트래시 → 덱 아래: "placing 3 cards ~ from your trash at the bottom of your deck in any order" = "returning ~ to the bottom of the deck"
        t = Regex.Replace(t, @"^placing (?<p>.+?) from your trash at the bottom of (?:your|the) deck(?: in any order)?$", "returning ${p} from your trash to the bottom of the deck", RegexOptions.IgnoreCase);
        // 진화원 → 시큐리티: "~ from this Digimon's digivolution cards at the bottom of your security stack" (P-146)
        t = Regex.Replace(t, @"^(?<head>placing .+? from this Digimon's digivolution cards) (?:at the bottom of your security stack|as your bottom security card)$", "${head} as the bottom security card", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"^(?<head>placing .+? from this Digimon's digivolution cards) (?:on top of your security stack|as your top security card)$", "${head} as the top security card", RegexOptions.IgnoreCase);
        return t;
    }

    /// <summary>"~ as this Digimon's bottom digivolution card" 같은 놓는 곳 → (이 디지몬인지, 다른 자신의 디지몬 조건, 테이머 아래인지 + 테이머 조건, 덱 아래인지)</summary>
    private sealed record PlaceDest(bool Self, Func<CardInstance, bool>? Other, Func<CardData, bool>? Tamer, bool Deck, bool Top);

    private static PlaceDest? ParsePlaceDest33(string dest)
    {
        dest = dest.Trim();
        Match m;
        if (Regex.IsMatch(dest, @"^(?:as (?:this Digimon's|its) bottom digivolution cards?|as the bottom digivolution cards? of this Digimon|at the bottom of this (?:Digimon|card)'s digivolution cards|under this Digimon as its bottom digivolution cards?)$", RegexOptions.IgnoreCase))
            return new(true, null, null, false, false);
        if (Regex.IsMatch(dest, @"^(?:as this Digimon's top digivolution cards?|on top of this (?:Digimon|card)'s digivolution cards)$", RegexOptions.IgnoreCase))
            return new(true, null, null, false, true);
        if (Regex.IsMatch(dest, @"^at the bottom of (?:your|the|its owner's) deck$", RegexOptions.IgnoreCase))
            return new(false, null, null, true, false);
        // 테이머 아래: "under this Tamer" · "under any of your [Glowing Dawn] trait Tamers" · "under 1 of your [Takuya Kanbara]s" · "under 1 of your blue or red Tamers" · "~ Tamers with the [DATA SQUAD] trait"
        if ((m = Regex.Match(dest, $@"^under (?:(?<this>this Tamer)|(?:any|1|one) of your (?:(?<names>{TargetSpec.NameList})s?|(?<spec>[^,]*?)Tamers?(?: with (?<w>.+))?))$", RegexOptions.IgnoreCase)).Success)
        {
            if (m.Groups["this"].Success) return new(false, null, _ => true, false, false) { };   // "this Tamer" — 부르는 쪽이 이 카드가 테이머인지 본다
            if (m.Groups["names"].Success)
            {
                var xs = TargetSpec.Names(m.Groups["names"].Value);
                return new(false, null, t => xs.Any(x => CardNames.Is(t, x)), false, false);
            }
            if (TamerFilter33(m.Groups["spec"].Value, m.Groups["w"].Success ? m.Groups["w"].Value : null) is { } tf)
                return new(false, null, tf, false, false);
            return null;
        }
        // 다른 자신의 디지몬: "as 1 of your [Gabumon]'s bottom digivolution cards" · "as the bottom digivolution cards of 1 of your [Agumon]" · "under 1 of your ~ as its bottom digivolution card"
        if ((m = Regex.Match(dest, @"^(?:as (?<t>(?:any|1|one) of your .+?)'s? bottom digivolution cards?|as the bottom digivolution cards? of (?<t>(?:any|1|one) of your .+?)|under (?<t>(?:any|1|one) of your .+?) as (?:its|their) bottom digivolution cards?)$", RegexOptions.IgnoreCase)).Success
            && OwnDigimonFilter33(m.Groups["t"].Value) is { } f)
            return new(false, f, null, false, false);
        return null;
    }

    /// <summary>자신의 디지몬 조건 (OwnDigimonFilter + "that's black or has the [X] trait" · "in play" · "on the field" · "[X]s")</summary>
    private static Func<CardInstance, bool>? OwnDigimonFilter33(string phrase)
    {
        string p = phrase.Trim();
        p = Regex.Replace(p, @"\s+(?:in play|on the field)$", "", RegexOptions.IgnoreCase);
        p = Regex.Replace(p, @"\]s\b", "]");
        p = Regex.Replace(p, @"\bin any of their traits\b", "in any of its traits", RegexOptions.IgnoreCase);
        Match m;
        // "1 of your other Digimon that's black or has the [Legend-Arms] trait" (ST13-02 · ST13-09)
        if ((m = Regex.Match(p, $@"^(?:any|1|one) of your (?<other>other )?Digimon that's (?<c>{Colors}) or has the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        {
            var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); var xs = TargetSpec.Names(m.Groups["x"].Value);
            return d => d.TopCard.AllColors.Contains(col) || xs.Any(x => TargetSpec.HasTrait(d.TopCard, x));
        }
        // "1 of your [Eosmon]" — 같은 명칭 (OwnDigimonFilter는 이름을 포함하는 디지몬)
        p = Regex.Replace(p, @"^(?:any|one) of your\b", "1 of your", RegexOptions.IgnoreCase);
        return OwnDigimonFilter(Regex.Replace(p, @"^1 of your other\b", "1 of your", RegexOptions.IgnoreCase));
    }

    /// <summary>테이머 조건: "[Glowing Dawn] trait " · "blue or red " · "" + "with the [DATA SQUAD] trait" · "with inherited effects"</summary>
    private static Func<CardData, bool>? TamerFilter33(string spec, string? with)
    {
        var fs = new List<Func<CardData, bool>>();
        string s = spec.Trim();
        Match m;
        if (s.Length > 0)
        {
            if ((m = Regex.Match(s, $@"^(?<x>{TargetSpec.NameList}) trait$", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(m.Groups["x"].Value); fs.Add(t => xs.Any(x => TargetSpec.HasTrait(t, x))); }
            else if ((m = Regex.Match(s, $@"^(?<c>(?:{Colors})(?: or (?:{Colors}))*)$", RegexOptions.IgnoreCase)).Success)
            {
                var cols = Regex.Matches(m.Groups["c"].Value, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
                fs.Add(t => t.AllColors.Any(cols.Contains));
            }
            else return null;
        }
        if (with != null)
        {
            if ((m = Regex.Match(with.Trim(), $@"^the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(m.Groups["x"].Value); fs.Add(t => xs.Any(x => TargetSpec.HasTrait(t, x))); }
            else if ((m = Regex.Match(with.Trim(), $@"^(?<x>{TargetSpec.NameList}) in (?:its|their) names?$", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(m.Groups["x"].Value); fs.Add(t => xs.Any(x => TargetSpec.NameHas(t, x))); }
            else return null;
        }
        return t => fs.All(f => f(t));
    }

    /// <summary>"1 of your [Erika Mishima]" · "1 of your Tamers with [Dan Yuki] or [Kanan Yuki] in their names" · "1 of your other blue Digimon" · "1 of your other Digimon or Tamers" → 놓을 자신의 디지몬 · 테이머</summary>
    private static (Func<EffectContext, CardInstance, bool>? Digimon, Func<CardData, bool>? Tamer)? OwnPiece33(string phrase)
    {
        string p = Regex.Replace(phrase.Trim(), @"^(?:one|any) of your\b", "1 of your", RegexOptions.IgnoreCase);
        p = Regex.Replace(p, @"\bin play (?=\[)", "", RegexOptions.IgnoreCase);   // BT11-072 "1 of your in play [Analogman]s"
        p = Regex.Replace(p, @"\]s$", "]");
        Match m;
        if ((m = Regex.Match(p, $@"^1 of your (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return ((c, d) => !ReferenceEquals(d, c.Self) && xs.Any(x => CardNames.Is(d.TopCard, x)), t => xs.Any(x => CardNames.Is(t, x)));
        }
        if ((m = Regex.Match(p, $@"^1 of your (?<spec>[^,]*?)Tamers?(?: with (?<x>{TargetSpec.NameList}) in (?:its|their) names?)?$", RegexOptions.IgnoreCase)).Success
            && TamerFilter33(m.Groups["spec"].Value, null) is { } tf)
        {
            var xs = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            return (null, t => tf(t) && (xs == null || xs.Any(x => TargetSpec.NameHas(t, x))));
        }
        if ((m = Regex.Match(p, @"^1 of your (?<other>other )?Digimon or Tamers?$", RegexOptions.IgnoreCase)).Success)
            return ((c, d) => !ReferenceEquals(d, c.Self), _ => true);
        if (TargetSpec.Parse(p) is { Opponent: false, AnySide: false, IsThis: false, IsThat: false, Count: 1 } spec)
            return ((c, d) => !ReferenceEquals(d, c.Self) && spec.Filters.All(f => f(d)) && spec.ContextFilters.All(f => f(c, d)), null);
        return null;
    }

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost33(string cost, CardData card)
    {
        Match m;
        string fixedCost = NormalizeCost33(cost);
        if (fixedCost != cost) return ParseCost(fixedCost, card);
        bool tamerCard = card.Type == CardType.Tamer;
        bool prevent = _placeAsAction;   // 동작("Place ~")으로 놓을 때는 "떠나려 할 때" 효과로 막을 수 있다 — 비용은 막을 수 없다

        // ---- 이 디지몬 · 이 테이머를 다른 자신의 디지몬의 진화원 아래에 ----
        if ((m = Regex.Match(cost, @"^placing this (?<what>Digimon(?: card)?|Tamer|card) (?<dest>(?:as|under) .+)$", RegexOptions.IgnoreCase)).Success
            && ParsePlaceDest33(m.Groups["dest"].Value) is { Other: { } toFilter, Top: false })
        {
            bool tamer = m.Groups["what"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase);
            if (tamer != tamerCard || (!tamer && card.Type != CardType.Digimon)) return null;
            // [소멸 시] "placing this Digimon card as ~" — 트래시의 이 카드 (P-239)
            bool FromTrash(EffectContext c) => !tamer && (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) && c.Owner.Trash.Contains(c.Card);
            List<CardInstance> Targets(EffectContext c) => c.Owner.BattleArea.Where(d => !ReferenceEquals(d, c.Self) && toFilter(d)).OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).ToList();
            bool Present(EffectContext c) => tamer ? c.Owner.Tamers.Contains(c.Card) : (c.Self != null && c.Owner.BattleArea.Contains(c.Self)) || FromTrash(c);
            return (c => Present(c) && Targets(c).Count > 0,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Targets(c), 1, $"[{c.Card.Name}] {(tamer ? "이 테이머" : "이 디지몬")}를 진화원 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) { c.CostFailed = true; return; }
                        bool placed = tamer ? c.Engine.PlaceTamerUnder(c.Owner, c.Card, d, canPrevent: prevent)
                            : FromTrash(c) ? c.Owner.Trash.Remove(c.Card) && Do(() => d.AddBottomSource(c.Card))
                            : c.Engine.PlaceDigimonUnder(c.Owner, c.Self!, d, canPrevent: prevent);
                        if (!placed) { c.CostFailed = true; return; }
                        c.LastTargets = new List<CardInstance> { d };
                        c.Log($"[{c.Card.Name}] {(tamer ? "이 테이머" : "이 디지몬")}를 [{d.TopCard.Name}]의 진화원 맨 아래에 놓았습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"{(tamer ? "이 테이머" : "이 디지몬")}를 자신의 디지몬의 진화원 아래에 놓아");
        }

        // "returning this Tamer to the hand" (BT15-082) — 테이머가 떠나려 할 때를 알린다 (비용이라 막을 수 없다)
        if (Regex.IsMatch(cost, @"^returning this Tamer to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase) && tamerCard)
            return (c => c.Owner.Tamers.Contains(c.Card), c => { if (!c.Engine.ReturnTamerToHand(c.Owner, c.Card, canPrevent: false)) c.CostFailed = true; }, _ => true, "이 테이머를 패로 되돌려");
        // "placing this Digimon as its bottom digivolution card" (P-154 — "떠나려 할 때"의 그 디지몬 아래에)
        if (Regex.IsMatch(cost, @"^placing this Digimon as its bottom digivolution card$", RegexOptions.IgnoreCase) && card.Type == CardType.Digimon)
        {
            CardInstance? Host(EffectContext c) => (c.Engine.CurrentLeave is { IsTamer: false } l ? l.Digimon : c.Other ?? c.LastTargets.FirstOrDefault()) is { } h && !ReferenceEquals(h, c.Self) && c.Owner.BattleArea.Contains(h) ? h : null;
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && Host(c) != null,
                    c => { var h = Host(c); if (h == null || !c.Engine.PlaceDigimonUnder(c.Owner, c.Self!, h, canPrevent: prevent)) c.CostFailed = true; else c.LastTargets = new List<CardInstance> { h }; },
                    _ => true, "이 디지몬을 그 디지몬의 진화원 아래에 놓아");
        }
        // ---- 이 테이머와 트래시의 [X] · [Y] 1장씩을 자신의 [Z]의 진화원 아래에 (ST17-10 · BT12-089 · BT17-080 · BT17-085) ----
        if ((m = Regex.Match(cost, $@"^placing this Tamer and (?:1 )?(?<a>\[[^\]]+\]) and (?:1 )?(?<b>\[[^\]]+\]) from your trash(?: in any order)? (?<dest>.+?)(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && tamerCard && ParsePlaceDest33(Regex.Replace(m.Groups["dest"].Value, @"\bdigivolution cards\b", "digivolution card", RegexOptions.IgnoreCase)) is { Other: { } duoFilter, Top: false })
        {
            string a = TargetSpec.Names(m.Groups["a"].Value)[0], b = TargetSpec.Names(m.Groups["b"].Value)[0];
            List<CardInstance> Targets(EffectContext c) => c.Owner.BattleArea.Where(duoFilter).ToList();
            CardData? Find(EffectContext c, string x, CardData? not) => c.Owner.Trash.FirstOrDefault(t => !ReferenceEquals(t, not) && CardNames.Is(t, x));
            return (c => c.Owner.Tamers.Contains(c.Card) && Targets(c).Count > 0 && Find(c, a, null) is { } fa && Find(c, b, fa) != null,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Targets(c), 1, $"[{c.Card.Name}] 진화원 아래에 카드를 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        var ca = Find(c, a, null); var cb = ca == null ? null : Find(c, b, ca);
                        if (d == null || ca == null || cb == null || !c.Engine.PlaceTamerUnder(c.Owner, c.Card, d, canPrevent: prevent)) { c.CostFailed = true; return; }
                        c.Owner.Trash.Remove(ca); c.Owner.Trash.Remove(cb);
                        d.AddBottomSource(cb); d.AddBottomSource(ca);
                        c.LastTargets = new List<CardInstance> { d };
                        c.Log($"[{c.Card.Name}] 이 테이머와 [{ca.Name}] · [{cb.Name}]을(를) [{d.TopCard.Name}]의 진화원 아래에 놓았습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, "이 테이머와 트래시의 카드 2장을 진화원 아래에 놓아");
        }

        // ---- 패 · 트래시의 [X] · [Y] 1장씩을 자신의 [Z]의 진화원 아래 / 테이머 아래 / 덱 아래에 ----
        if ((m = Regex.Match(cost, @"^placing (?:1 )?(?<a>\[[^\]]+\]) and (?:1 )?(?<b>\[[^\]]+\]) from your (?<z>trash|hand)(?: in any order)? (?<dest>.+?)(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && ParsePlaceDest33(Regex.Replace(m.Groups["dest"].Value, @"\bdigivolution cards\b", "digivolution card", RegexOptions.IgnoreCase)) is { Self: false, Top: false } pairDest)
        {
            string a = TargetSpec.Names(m.Groups["a"].Value)[0], b = TargetSpec.Names(m.Groups["b"].Value)[0];
            bool hand = m.Groups["z"].Value.Equals("hand", StringComparison.OrdinalIgnoreCase);
            List<CardData> Zone(EffectContext c) => hand ? c.Owner.Hand : c.Owner.Trash;
            CardData? Find(EffectContext c, string x, CardData? not) => Zone(c).FirstOrDefault(t => !ReferenceEquals(t, not) && CardNames.Is(t, x));
            List<CardInstance> Targets(EffectContext c) => pairDest.Other == null ? new List<CardInstance>() : c.Owner.BattleArea.Where(pairDest.Other).ToList();
            bool DestOk(EffectContext c) => pairDest.Deck || (pairDest.Tamer != null ? c.Owner.Tamers.Any(pairDest.Tamer) : Targets(c).Count > 0);
            string where = pairDest.Deck ? "덱 아래" : pairDest.Tamer != null ? "테이머 아래" : "자신의 디지몬의 진화원 아래";
            return (c => DestOk(c) && Find(c, a, null) is { } fa && Find(c, b, fa) != null,
                    c =>
                    {
                        CardInstance? d = null;
                        if (pairDest.Other != null)
                        {
                            d = c.Engine.ChooseTargets(c.Owner, Targets(c), 1, $"[{c.Card.Name}] 진화원 아래에 카드를 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                            if (d == null) { c.CostFailed = true; return; }
                        }
                        var ca = Find(c, a, null); var cb = ca == null ? null : Find(c, b, ca);
                        if (ca == null || cb == null) { c.CostFailed = true; return; }
                        Zone(c).Remove(ca); Zone(c).Remove(cb);
                        if (pairDest.Deck) { c.Owner.MainDeck.Add(ca); c.Owner.MainDeck.Add(cb); }
                        else if (pairDest.Tamer != null) { c.Owner.UnderTamers.Add(ca); c.Owner.UnderTamers.Add(cb); c.LastCard = c.Owner.Tamers.FirstOrDefault(pairDest.Tamer); }   // "that Tamer" = 그 테이머
                        else { d!.AddBottomSource(cb); d.AddBottomSource(ca); c.LastTargets = new List<CardInstance> { d }; }
                        c.Log($"[{c.Card.Name}] [{ca.Name}] · [{cb.Name}]을(를) {where}에 놓았습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"카드 2장을 {where}에 놓아");
        }

        // ---- 다른 자신의 디지몬 · 테이머를 이 디지몬(또는 다른 자신의 디지몬)의 진화원 아래 / 덱 아래에 ----
        if ((m = Regex.Match(cost, @"^placing (?<t>(?:1|one) of your .+?) (?<dest>(?:as|under|at) .+)$", RegexOptions.IgnoreCase)).Success
            && ParsePlaceDest33(m.Groups["dest"].Value) is { Tamer: null, Top: false } pieceDest
            && OwnPiece33(m.Groups["t"].Value) is { } piece)
        {
            var (digi, tam) = piece;
            List<CardInstance> Dests(EffectContext c) => pieceDest.Self ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new List<CardInstance> { c.Self } : new List<CardInstance>())
                : pieceDest.Other != null ? c.Owner.BattleArea.Where(pieceDest.Other).ToList() : new List<CardInstance>();
            List<CardInstance> Digis(EffectContext c) => digi == null ? new List<CardInstance>() : c.Owner.BattleArea.Where(d => digi(c, d) && !GoldfishEngine.IsToken(d.TopCard) || digi(c, d) && pieceDest.Deck)
                .OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).ToList();
            List<CardData> Tams(EffectContext c) => tam == null ? new List<CardData>() : c.Owner.Tamers.Where(tam).Distinct().ToList();
            bool Able(EffectContext c) => (pieceDest.Deck || Dests(c).Count > 0) && (Digis(c).Any(d => pieceDest.Deck || Dests(c).Any(t => !ReferenceEquals(t, d))) || Tams(c).Count > 0);
            string what = digi != null && tam != null ? "디지몬/테이머" : tam != null ? "테이머" : "디지몬";
            return ((Func<EffectContext, bool>)Able,
                    c =>
                    {
                        // 디지몬을 먼저 고르고(없으면 테이머), 놓을 곳을 고른다
                        var digis = Digis(c);
                        var tams = Tams(c);
                        CardInstance? pickD = null; CardData? pickT = null;
                        if (digis.Count > 0 && (tams.Count == 0 || !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 테이머를 놓을까요? (아니오 = 디지몬)", recommended: true)))
                            pickD = c.Engine.ChooseTargets(c.Owner, digis, 1, $"[{c.Card.Name}] 비용으로 놓을 자신의 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        else if (tams.Count > 0)
                            pickT = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, tams, 1, upTo: false, $"[{c.Card.Name}] 비용으로 놓을 자신의 테이머를 고르세요.").FirstOrDefault();
                        if (pickD == null && pickT == null) { c.CostFailed = true; return; }
                        if (pieceDest.Deck)
                        {
                            if (pickD != null) { if (prevent) c.Engine.BottomDeck(c.Owner, pickD); else c.Engine.BottomDeckAsCost(c.Owner, pickD); }
                            else
                            {
                                c.Engine.TryPreventTamerLeave(c.Owner, pickT!, canPrevent: false, GoldfishEngine.LeaveDestination.Deck);
                                if (c.Owner.Tamers.Remove(pickT!)) { c.Owner.SuspendedTamers.Remove(pickT!); c.Owner.MainDeck.Add(pickT!); c.Engine.ClearUnderTamersIfNoTamer(c.Owner); }
                            }
                            c.LastTargets = pickD != null ? new List<CardInstance> { pickD } : new List<CardInstance>();
                            c.Log($"[{c.Card.Name}] [{pickD?.TopCard.Name ?? pickT!.Name}]을(를) 덱 아래로 되돌렸습니다 (비용)");
                            return;
                        }
                        var dests = Dests(c).Where(t => !ReferenceEquals(t, pickD)).ToList();
                        var to = dests.Count == 1 ? dests[0] : c.Engine.ChooseTargets(c.Owner, dests, 1, $"[{c.Card.Name}] 진화원 아래에 놓을 곳(디지몬)을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (to == null) { c.CostFailed = true; return; }
                        string name = pickD?.TopCard.Name ?? pickT!.Name;
                        c.LastCard = pickD?.TopCard ?? pickT;
                        bool ok = pickD != null ? c.Engine.PlaceDigimonUnder(c.Owner, pickD, to, canPrevent: prevent) : c.Engine.PlaceTamerUnder(c.Owner, pickT!, to, canPrevent: prevent);
                        if (!ok) { c.CostFailed = true; return; }
                        if (!pieceDest.Self) c.LastTargets = new List<CardInstance> { to };
                        c.Log($"[{c.Card.Name}] [{name}]을(를) [{to.TopCard.Name}]의 진화원 맨 아래에 놓았습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    c => Digis(c).Any(d => d.TopCard.Level <= 4) || Tams(c).Count > 0 || pieceDest.Deck, $"자신의 {what} 1{(tam != null && digi == null ? "명" : "마리")}을(를) {(pieceDest.Deck ? "덱 아래로 되돌려" : "진화원 아래에 놓아")}");
        }

        // ---- 패 · 트래시의 카드를 테이머 아래에 ----
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:from|in) (?<from>your hand or (?:your )?trash|your trash or (?:your )?hand|your hand|your trash) (?:face down )?(?<dest>under .+?)(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && ParsePlaceDest33(m.Groups["dest"].Value) is { Tamer: { } underTamer } underDest
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } underPick)
        {
            var (n, match) = underPick;
            bool thisTamer = Regex.IsMatch(m.Groups["dest"].Value, @"^under this Tamer$", RegexOptions.IgnoreCase);
            if (thisTamer && !tamerCard) return null;
            string fromText = m.Groups["from"].Value.ToLowerInvariant();
            bool hand = fromText.Contains("hand"), trash = fromText.Contains("trash");
            List<PoolCard> Pool(EffectContext c) => (hand ? PoolFrom(c.Owner.Hand, x => match(x) && !ReferenceEquals(x, c.Card)) : Enumerable.Empty<PoolCard>())
                .Concat(trash ? PoolFrom(c.Owner.Trash, x => match(x) && !ReferenceEquals(x, c.Card)) : Enumerable.Empty<PoolCard>()).ToList();
            bool TamerOk(EffectContext c) => thisTamer ? c.Owner.Tamers.Contains(c.Card) : c.Owner.Tamers.Any(underTamer);
            var zoneKind = hand && trash ? CardChoiceZone.HandOrTrash : hand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
            return (c => TamerOk(c) && Pool(c).Count >= n,
                    c =>
                    {
                        var ranked = Pool(c).OrderBy(x => ReferenceEquals(x.Zone, c.Owner.Hand) ? 1 : 0).ThenBy(x => x.Card.Level).ToList();
                        var placed = new List<string>();
                        foreach (var x in ChooseFromPool(c, zoneKind, ranked, n, $"[{c.Card.Name}] 테이머 아래에 놓을 카드 {n}장을 고르세요."))
                            if (TakeFromPool(c, x)) { c.Owner.UnderTamers.Add(x.Card); placed.Add(x.Card.Name); }
                        c.Log($"[{c.Card.Name}] [{string.Join(", ", placed)}]을(를) 테이머 아래에 놓았습니다 (비용)");
                    },
                    c => trash ? Pool(c).Any(x => !ReferenceEquals(x.Zone, c.Owner.Hand)) || c.Owner.Hand.Count >= n + 2 : c.Owner.Hand.Count >= n + 2,
                    $"{(hand && trash ? "패 또는 트래시" : hand ? "패" : "트래시")}의 카드 {n}장을 테이머 아래에 놓아");
        }
        // "placing this card from the battle area face down under any of your [BEATBREAK] trait Tamers" (ST23-15 · ST24-15 — 배틀 에어리어에 놓인 이 옵션)
        if ((m = Regex.Match(cost, @"^placing this card from the battle area (?:face down )?(?<dest>under .+)$", RegexOptions.IgnoreCase)).Success
            && card.Type == CardType.Option && ParsePlaceDest33(m.Groups["dest"].Value) is { Tamer: { } optTamer })
        {
            PlacedOption? Placed(EffectContext c) => c.Owner.OptionsInPlay.FirstOrDefault(p => ReferenceEquals(p.Card, c.Card));
            return (c => Placed(c) != null && c.Owner.Tamers.Any(optTamer),
                    c =>
                    {
                        if (Placed(c) is not { } p || !c.Owner.OptionsInPlay.Remove(p)) { c.CostFailed = true; return; }
                        c.Owner.UnderTamers.Add(c.Card);
                        c.Log($"[{c.Card.Name}] 배틀 에어리어의 이 카드를 테이머 아래에 놓았습니다 (비용)");
                    },
                    _ => true, "배틀 에어리어의 이 카드를 테이머 아래에 놓아");
        }
        // "placing your top security card face down under any of your [Glowing Dawn] trait Tamers" (BT26-025)
        if ((m = Regex.Match(cost, @"^placing your top security card (?:face down )?(?<dest>under .+)$", RegexOptions.IgnoreCase)).Success
            && ParsePlaceDest33(m.Groups["dest"].Value) is { Tamer: { } secTamer })
            return (c => c.Owner.SecurityStack.Count > 0 && c.Owner.Tamers.Any(secTamer),
                    c =>
                    {
                        var x = c.Owner.SecurityStack[0];
                        c.Owner.SecurityStack.RemoveAt(0);
                        c.Owner.UnderTamers.Add(x);
                        c.Engine.NotifySecurityRemoved(c.Owner, x);
                        c.Log($"[{c.Card.Name}] 시큐리티 맨 위 카드를 테이머 아래에 놓았습니다 (비용)");
                    },
                    c => c.Owner.SecurityStack.Count >= 3, "시큐리티 맨 위 카드를 테이머 아래에 놓아");
        // 이 디지몬의 진화원 → 테이머 아래: "placing 1 Digimon card with the [Xros Heart] trait in this Digimon's digivolution cards under 1 of your Tamers" (P-152) ·
        // "placing all digivolution cards from under this Digimon under 1 of your Tamers" (BT10-009)
        if ((m = Regex.Match(cost, @"^placing (?:(?<all>all digivolution cards) from under this Digimon|(?<pick>.+?) (?:in|from) this Digimon's digivolution cards) (?<dest>under (?:any|1|one) of your .*?Tamers?.*)$", RegexOptions.IgnoreCase)).Success
            && ParsePlaceDest33(m.Groups["dest"].Value) is { Tamer: { } srcTamer }
            && (m.Groups["all"].Success || (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) != null))
        {
            bool all = m.Groups["all"].Success;
            var (n, match) = all ? (99, (Func<CardData, bool>)(_ => true)) : (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value))!.Value;
            List<CardData> Able(EffectContext c) => c.Self == null || !c.Owner.BattleArea.Contains(c.Self) ? new List<CardData>() : c.Self.EvolutionSources.Where(match).ToList();
            return (c => c.Owner.Tamers.Any(srcTamer) && (all ? Able(c).Count > 0 : Able(c).Count >= n),
                    c =>
                    {
                        var picked = all ? Able(c) : c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, Able(c).OrderBy(x => x.Level).ToList(), n, upTo: false, $"[{c.Card.Name}] 테이머 아래에 놓을 진화원 {n}장을 고르세요.");
                        foreach (var x in picked)
                            if (c.Self!.TakeSource(y => ReferenceEquals(y, x)) != null) c.Owner.UnderTamers.Add(x);
                        c.Log($"[{c.Card.Name}] 진화원 [{string.Join(", ", picked.Select(x => x.Name))}]을(를) 테이머 아래에 놓았습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, all ? "이 디지몬의 진화원 전부를 테이머 아래에 놓아" : $"이 디지몬의 진화원 {n}장을 테이머 아래에 놓아");
        }

        // ---- 패 · 트래시의 카드를 다른 자신의 디지몬의 진화원 아래에 ("from your hand or trash as 1 of your Digimon's bottom digivolution card" · "as the bottom digivolution card of 1 of your ~") ----
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:from|in) (?<from>your hand or (?:your )?trash|your trash or (?:your )?hand|your hand|your trash) (?:face down )?(?<dest>(?:as|under) .+)$", RegexOptions.IgnoreCase)).Success
            && ParsePlaceDest33(m.Groups["dest"].Value) is { Other: { } otherDest }
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } otherPick)
        {
            var (n, match) = otherPick;
            string fromText = m.Groups["from"].Value.ToLowerInvariant();
            bool hand = fromText.Contains("hand"), trash = fromText.Contains("trash");
            List<PoolCard> Pool(EffectContext c) => (hand ? PoolFrom(c.Owner.Hand, x => match(x) && !ReferenceEquals(x, c.Card)) : Enumerable.Empty<PoolCard>())
                .Concat(trash ? PoolFrom(c.Owner.Trash, x => match(x) && !ReferenceEquals(x, c.Card)) : Enumerable.Empty<PoolCard>()).ToList();
            List<CardInstance> Targets(EffectContext c) => c.Owner.BattleArea.Where(otherDest).OrderByDescending(d => d.TopCard.Level).ToList();
            var zoneKind = hand && trash ? CardChoiceZone.HandOrTrash : hand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
            return (c => Targets(c).Count > 0 && Pool(c).Count >= n,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Targets(c), 1, $"[{c.Card.Name}] 진화원 맨 아래에 카드를 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) { c.CostFailed = true; return; }
                        var ranked = Pool(c).OrderBy(x => ReferenceEquals(x.Zone, c.Owner.Hand) ? 1 : 0).ThenBy(x => x.Card.Level).ToList();
                        foreach (var x in ChooseFromPool(c, zoneKind, ranked, n, $"[{c.Card.Name}] [{d.TopCard.Name}]의 진화원 맨 아래에 놓을 카드를 고르세요."))
                            if (TakeFromPool(c, x)) { d.AddBottomSource(x.Card); c.LastCard = x.Card; }
                        c.LastTargets = new List<CardInstance> { d };
                        c.RecalculateFieldBuffs();
                    },
                    c => trash ? Pool(c).Any(x => !ReferenceEquals(x.Zone, c.Owner.Hand)) || c.Owner.Hand.Count >= n + 2 : c.Owner.Hand.Count >= n + 2,
                    $"{(hand && trash ? "패 또는 트래시" : hand ? "패" : "트래시")}의 카드를 자신의 디지몬 진화원 맨 아래에 놓아");
        }
        // ---- 트래시 · 패의 카드를 이 디지몬의 진화원 위에 ("as this Digimon's top digivolution card" · "on top of this Digimon's digivolution cards" — EX10-021 · BT13-088 · EX9-057) ----
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from (?<from>your trash|your hand or (?:your )?trash|your hand) (?<dest>as this Digimon's top digivolution cards?|on top of this (?:Digimon|card)'s digivolution cards)$", RegexOptions.IgnoreCase)).Success
            && !m.Groups["from"].Value.Equals("your hand", StringComparison.OrdinalIgnoreCase) | m.Groups["dest"].Value.StartsWith("as", StringComparison.OrdinalIgnoreCase)
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } topPick)
        {
            var (n, match) = topPick;
            string fromText = m.Groups["from"].Value.ToLowerInvariant();
            bool hand = fromText.Contains("hand"), trash = fromText.Contains("trash");
            List<PoolCard> Pool(EffectContext c) => (hand ? PoolFrom(c.Owner.Hand, x => match(x) && !ReferenceEquals(x, c.Card)) : Enumerable.Empty<PoolCard>())
                .Concat(trash ? PoolFrom(c.Owner.Trash, x => match(x) && !ReferenceEquals(x, c.Card)) : Enumerable.Empty<PoolCard>()).ToList();
            var zoneKind = hand && trash ? CardChoiceZone.HandOrTrash : hand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && Pool(c).Count >= n,
                    c =>
                    {
                        foreach (var x in ChooseFromPool(c, zoneKind, Pool(c).OrderBy(x => x.Card.Level).ToList(), n, $"[{c.Card.Name}] 진화원 위에 놓을 카드 {n}장을 고르세요."))
                            if (TakeFromPool(c, x)) { c.Self!.EvolutionSources.Push(x.Card); c.LastCard = x.Card; }   // 진화원 맨 위 (맨 위 카드 바로 아래)
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"{(hand && trash ? "패 또는 트래시" : hand ? "패" : "트래시")}의 카드 {n}장을 이 디지몬의 진화원 위에 놓아");
        }

        // ---- 시큐리티: 이 디지몬 · 다른 디지몬을 시큐리티 위 / 아래에 ----
        // "placing this Digimon as the bottom security card" · "~ as the face-up bottom security card" (BT26-022 · BT19-048)
        if ((m = Regex.Match(cost, @"^placing this Digimon as the (?<fu>face-up )?(?<pos>top|bottom) security card$", RegexOptions.IgnoreCase)).Success && card.Type == CardType.Digimon)
        {
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase), fu = m.Groups["fu"].Success;
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self),
                    c => { if (!c.Engine.PlaceDigimonInSecurity(c.Owner, c.Self!, top, fu, canPrevent: prevent)) c.CostFailed = true; },
                    _ => true, $"이 디지몬을 시큐리티 {(top ? "위" : "아래")}에 {(fu ? "앞면으로 " : "")}놓아");
        }
        // "placing 1 other Digimon (with no digivolution cards) as the bottom security card" · "1 8000 DP or lower Digimon" · "1 of your opponent's Digimon with the lowest DP" ·
        // "1 of your level 6 Digimon on top of your security stack" · "1 Digimon on top of its owner's security stack" — 그 디지몬 주인의 시큐리티
        if ((m = Regex.Match(cost, @"^placing (?<t>(?:1|one) .+?) (?:as the (?<pos>top|bottom) security card|on (?<pos>top) of (?:its owner's|your|their owner's) security stack)$", RegexOptions.IgnoreCase)).Success
            && !Regex.IsMatch(m.Groups["t"].Value, @"\bTamers?\b|(?<!digivolution )\bcards?\b|\bfrom\b", RegexOptions.IgnoreCase)
            && TargetSpec.Parse(Regex.Replace(m.Groups["t"].Value, @"^one\b", "1", RegexOptions.IgnoreCase)) is { IsThis: false, IsThat: false, Count: 1 } secSpec)
        {
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            if (Regex.IsMatch(m.Groups["t"].Value, @"^1 other\b", RegexOptions.IgnoreCase))
                secSpec.ContextFilters.Add((c, d) => !ReferenceEquals(d, c.Self));
            List<CardInstance> Pool(EffectContext c)
            {
                var all = (secSpec.AnySide ? c.Enemy.BattleArea.Concat(c.Owner.BattleArea) : (secSpec.Opponent ? c.Enemy : c.Owner).BattleArea)
                    .Where(d => secSpec.Filters.All(f => f(d)) && secSpec.ContextFilters.All(f => f(c, d)));
                all = all.Where(d => !c.Engine.IsUnaffectedBy(c.Owner, d));   // 효과를 받지 않는 디지몬은 고를 수 없다
                if (secSpec.Selector != null) all = secSpec.Selector(all);
                // 추천: 상대의 디지몬 (DP 높은 순) → 자신의 낮은 Lv.
                return all.OrderBy(d => c.Owner.BattleArea.Contains(d) ? 1 : 0).ThenByDescending(d => c.Enemy.BattleArea.Contains(d) ? d.GetCurrentDP() : -d.TopCard.Level).ToList();
            }
            return (c => Pool(c).Count > 0,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Pool(c), 1, $"[{c.Card.Name}] 비용으로 시큐리티 {(top ? "위" : "아래")}에 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) { c.CostFailed = true; return; }
                        var zones = c.Owner.BattleArea.Contains(d) ? c.Owner : c.Enemy;
                        string name = d.TopCard.Name;
                        if (!c.Engine.PlaceDigimonInSecurity(zones, d, top, faceUp: false, canPrevent: prevent)) { c.CostFailed = true; return; }
                        c.Log($"[{c.Card.Name}] [{name}]을(를) {(ReferenceEquals(zones, c.Owner) ? "자신" : "상대")}의 시큐리티 {(top ? "위" : "아래")}에 놓았습니다 (비용)");
                    },
                    c => Pool(c).FirstOrDefault() is { } first && (c.Enemy.BattleArea.Contains(first) || first.TopCard.Level <= 4), $"디지몬 1마리를 시큐리티 {(top ? "위" : "아래")}에 놓아");
        }
        // 맨 위 카드 → 시큐리티: "placing its top stacked card as the top security card" (EX13-032) · "this Digimon's top card as your top security card" (P-153) ·
        // "the top card of this Digimon on top of your security stack face down" (BT9-044) · "the top card of 1 of your level 4 or higher Digimon with [Pulsemon] in its text on top of your security stack" (BT17-098)
        if ((m = Regex.Match(cost, @"^placing (?:(?<self>its top stacked card|this Digimon's top (?:stacked )?card|the top (?:stacked )?card of this Digimon)|the top (?:stacked )?card of (?<t>(?:1|one|any) of your .+?)) (?:as (?:the|your) (?<pos>top|bottom) security card|on (?<pos>top) of your security stack)(?: face down)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            Func<CardInstance, bool>? who = m.Groups["t"].Success ? OwnDigimonFilter33(m.Groups["t"].Value) : null;
            if (m.Groups["t"].Success && who == null) return null;
            List<CardInstance> Able(EffectContext c) => (who == null ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new List<CardInstance> { c.Self } : new List<CardInstance>())
                : c.Owner.BattleArea.Where(who).ToList()).Where(d => d.EvolutionSources.Count > 0).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var d = Able(c).Count == 1 ? Able(c)[0] : c.Engine.ChooseTargets(c.Owner, Able(c), 1, $"[{c.Card.Name}] 맨 위 카드를 시큐리티에 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        string name = d?.TopCard.Name ?? "";
                        if (d == null || !c.Engine.PlaceTopCardInSecurity(c.Owner, d, top)) { c.CostFailed = true; return; }
                        c.Log($"[{c.Card.Name}] [{name}]을(를) 시큐리티 {(top ? "위" : "아래")}에 놓았습니다 (맨 위: [{d.TopCard.Name}], 비용)");
                    },
                    _ => true, $"겹쳐져 있는 카드를 위에서부터 1장 시큐리티 {(top ? "위" : "아래")}에 놓아");
        }
        // 패의 카드 → 시큐리티: "placing 1 card with [Etemon] or [Sukamon] in its name from your hand on top of your security stack" (EX5-054) ·
        // "~ at the bottom of your security stack" · "~ as your top security card" · "~ at the top or bottom of your security stack" (뒷면 — 앞면은 원래 규칙)
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from your hand (?:on (?<pos>top) of your security stack|at the (?<pos>top|bottom|top or bottom) of your security stack|as (?:the|your) (?<pos>top|bottom|top or bottom) security card)$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } secPick)
        {
            var (n, match) = secPick;
            string pos = m.Groups["pos"].Value.ToLowerInvariant();
            return (c => c.Owner.Hand.Count(match) >= n,
                    c =>
                    {
                        foreach (var x in c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, c.Owner.Hand.Where(match).OrderBy(x => x.Level).ToList(), n, upTo: false, $"[{c.Card.Name}] 시큐리티에 놓을 패 {n}장을 고르세요."))
                        {
                            if (!c.Owner.Hand.Remove(x)) continue;
                            bool top = pos == "top" || (pos != "bottom" && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] [{x.Name}]을(를) 시큐리티 맨 위에 놓을까요? (아니오 = 맨 아래)", recommended: true));
                            if (top) c.Owner.SecurityStack.Insert(0, x); else c.Owner.SecurityStack.Add(x);
                            c.Engine.NotifySecurityAdded(c.Owner, x);
                            c.LastCard = x;
                        }
                    },
                    c => c.Owner.Hand.Count >= n + 2, $"패의 카드 {n}장을 시큐리티 {(pos == "top" ? "위" : pos == "bottom" ? "아래" : "위 또는 아래")}에 놓아");
        }
        // 다른 디지몬의 맨 위 카드를 그 진화원 아래에: "placing the top stacked card of any of your level 4 or higher [CS] Digimon as its bottom digivolution card" (P-225)
        if ((m = Regex.Match(cost, @"^placing the top (?:stacked )?card of (?<t>(?:any|1|one) of your .+?) as its bottom digivolution card$", RegexOptions.IgnoreCase)).Success
            && OwnDigimonFilter33(m.Groups["t"].Value) is { } topWho)
        {
            List<CardInstance> Able(EffectContext c) => c.Owner.BattleArea.Where(d => topWho(d) && d.EvolutionSources.Count > 0).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Able(c), 1, $"[{c.Card.Name}] 맨 위 카드를 진화원 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) { c.CostFailed = true; return; }
                        string before = d.TopCard.Name;
                        d.MoveTopToBottom();
                        c.LastTargets = new List<CardInstance> { d };
                        c.Log($"[{c.Card.Name}] [{before}]을(를) 진화원 맨 아래에 놓았습니다 (맨 위: [{d.TopCard.Name}])");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, "겹쳐져 있는 카드를 위에서부터 1장 그 디지몬의 진화원 아래에 놓아");
        }
        // 다른 디지몬의 진화원 → 덱 아래: "placing 2 [Vemmon] from 1 of your [Galacticmon]'s digivolution cards at the bottom of their owners' decks" (BT11-070 · P-094)
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from (?<t>(?:1|one|any) of your .+?)'s digivolution cards (?:at|to) the bottom of (?:the|your|its owner's|their owners'?) decks?$", RegexOptions.IgnoreCase)).Success
            && OwnDigimonFilter33(m.Groups["t"].Value) is { } srcOwner
            && (ParseNamePick(m.Groups["pick"].Value) ?? ParseCardPick(m.Groups["pick"].Value)) is { } vem)
        {
            var (n, match) = vem;
            List<CardInstance> Able(EffectContext c) => c.Owner.BattleArea.Where(d => srcOwner(d) && d.EvolutionSources.Count(match) >= n).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Able(c), 1, $"[{c.Card.Name}] 진화원을 덱 아래로 되돌릴 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) { c.CostFailed = true; return; }
                        var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, d.EvolutionSources.Where(match).ToList(), n, upTo: false, $"[{c.Card.Name}] 덱 아래로 되돌릴 진화원 {n}장을 고르세요.");
                        foreach (var x in picked)
                            if (d.TakeSource(y => ReferenceEquals(y, x)) != null) c.Owner.MainDeck.Add(x);
                        c.Log($"[{c.Card.Name}] [{d.TopCard.Name}]의 진화원 [{string.Join(", ", picked.Select(x => x.Name))}]을(를) 덱 아래로 되돌렸습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"자신의 디지몬의 진화원 {n}장을 덱 아래로 되돌려");
        }
        return null;
    }

    // =====================================================================
    // 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep이 마지막에 본다)
    // =====================================================================

    /// <summary>동작 표기 정리 → 원래 규칙 (바뀌지 않으면 그대로)</summary>
    internal static string NormalizeAction33(string s)
    {
        string t = s.Trim().TrimEnd('.');
        t = Regex.Replace(t, @"^Switch the target of attack to\b", "Switch the attack target to", RegexOptions.IgnoreCase);   // BT11-070 · P-094
        t = Regex.Replace(t, @"^Prevent this Digimon from leaving (?:play|the battle area)$", "Prevent it from leaving play", RegexOptions.IgnoreCase);   // BT11-062 · BT11-064
        t = Regex.Replace(t, @"^(?<n>1|one) of (?<w>those|them|the) Digimon or Tamers (?<v>doesn't|does not) leave", "${n} of ${w} Digimon ${v} leave", RegexOptions.IgnoreCase);   // BT23-058 (테이머도)
        t = Regex.Replace(t, @"\bdigivolve in to\b", "digivolve into", RegexOptions.IgnoreCase);   // BT14-090 원문 오타
        // "1 of those Digimon gains ＜Rush＞ for the turn" · "1 of the played Digimon gains ~" (EX3-030 · EX10-065 · ST19-14) — 사건의 그 디지몬
        t = Regex.Replace(t, @"^(?:1|one) of (?:those|the played) Digimon (?=gains? ＜)", "That Digimon ", RegexOptions.IgnoreCase);
        return t;
    }

    /// <summary>진화 조건을 무시하는 진화: "That Digimon may digivolve into [MetalGarurumon] in your hand, ignoring its digivolution requirements and without paying the cost" 등</summary>
    private static readonly Regex IgnoreEvo33 = new(
        @"^(?<t>that Digimon|it|this Digimon|(?:1|one) of your .+?) may digivolve into (?:an? |1 )?(?<pick>.+?) (?:in|from) (?:the |your )?(?<from>hand or (?:the |your )?trash|hand|trash)(?<tail>(?:,? ?(?:ignoring (?:its |this card's |the )?(?:digivolution requirements|level)|for (?:a digivolution cost of (?<cost>\d+)|its digivolution cost)|without paying (?:the|its) (?:memory |digivolution )?cost|and))+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (Action<EffectContext>? Act, string? Error)? ParseAction33(string raw, CardData card, List<EffectTrigger> triggers)
    {
        string s = NormalizeAction33(raw);
        if (s != raw.Trim().TrimEnd('.') && ParseAction(s, card, triggers) is { Error: null } re) return re;
        string label = triggers.Count > 0 && Labels.TryGetValue(triggers[0], out var l) ? l : "효과";
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;

        // ---- 진화 조건 무시 진화 ----
        if ((m = IgnoreEvo33.Match(s)).Success && Regex.IsMatch(m.Groups["tail"].Value, @"\bignoring\b", RegexOptions.IgnoreCase))
        {
            string tail = m.Groups["tail"].Value;
            bool free = Regex.IsMatch(tail, @"without paying", RegexOptions.IgnoreCase);
            int? fixedCost = m.Groups["cost"].Success ? int.Parse(m.Groups["cost"].Value) : null;
            bool levelOnly = Regex.IsMatch(tail, @"ignoring (?:its )?level", RegexOptions.IgnoreCase);
            // 테이머의 "it may digivolve into ~" (EX13-074) — 이 테이머를 진화 (테이머에서 진화와 같은 처리)
            if (card.Type == CardType.Tamer && Regex.IsMatch(m.Groups["t"].Value, @"^(?:it|this Tamer)$", RegexOptions.IgnoreCase))
            {
                var tpick = ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value);
                if (tpick == null) return null;
                var tmatch = tpick.Value.Match;
                string tfrom = m.Groups["from"].Value.ToLowerInvariant();
                bool tHand = tfrom.Contains("hand"), tTrash = tfrom.Contains("trash");
                bool tfree = Regex.IsMatch(m.Groups["tail"].Value, @"without paying", RegexOptions.IgnoreCase);
                int? tcost = m.Groups["cost"].Success ? int.Parse(m.Groups["cost"].Value) : null;
                return (c =>
                {
                    if (!c.Owner.Tamers.Contains(c.Card)) { c.LastStepResolved = false; return; }
                    var pool = new List<PoolCard>();
                    if (tHand) pool.AddRange(PoolFrom(c.Owner.Hand, x => x.Type == CardType.Digimon && tmatch(x)));
                    if (tTrash) pool.AddRange(PoolFrom(c.Owner.Trash, x => x.Type == CardType.Digimon && tmatch(x)));
                    var evo = pool.OrderByDescending(x => x.Card.Level).FirstOrDefault();
                    if (evo.Card == null || c.Engine.EvolveTamerByEffect(c.Owner, c.Card, evo.Card, tfree ? 0 : tcost ?? evo.Card.EvoCost, evo.Zone) is not { } ti) { c.LastStepResolved = false; Emit(c, label, "진화할 수 있는 카드가 없습니다"); return; }
                    c.LastTargets = new List<CardInstance> { ti };
                    Emit(c, label, $"이 테이머를 [{evo.Card.Name}]으로 진화");
                }, null);
            }
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            Func<CardInstance, bool>? nameFilter = target == null ? OwnDigimonFilter33(m.Groups["t"].Value) : null;
            if (target == null && nameFilter == null) return null;
            if (target != null && (target.Opponent || target.AnySide)) return null;
            var pick = ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value) ?? ParseCardPick(m.Groups["pick"].Value);
            if (pick == null) return null;
            var match = pick.Value.Match;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
            // 진화 조건을 무시하면 Lv. · 색을 보지 않는다. "ignoring its level"(BT12-089)은 Lv.만 — 색 조건은 본다
            bool Can(CardInstance d, CardData x) => x.Type == CardType.Digimon && !EffectRules.IsDigivolveLocked(null, d)
                && (!levelOnly || d.TopCard.AllColors.Contains(x.EvoRequirementColor) || CardScripts.MeetsAlternativeEvolution(x, d));
            return (c =>
            {
                var bases = target != null ? Pick(target, c, security) : c.Owner.BattleArea.Where(nameFilter!).ToList();
                if (target == null && bases.Count > 1)
                    bases = c.Engine.ChooseTargets(c.Owner, bases, 1, $"[{c.Card.Name}] 진화시킬 디지몬을 고르세요.", optional: false, preselected: null);
                foreach (var d in bases.Where(x => c.Owner.BattleArea.Contains(x)).ToList())
                {
                    if (EffectRules.IsDigivolveLocked(c.Engine.State, d)) continue;
                    var pool = new List<PoolCard>();
                    if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, x => match(x) && Can(d, x)));
                    if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, x => match(x) && Can(d, x)));
                    var evo = pool.OrderByDescending(x => x.Card.Level).ThenByDescending(x => x.Card.DP).FirstOrDefault();
                    if (evo.Card == null) continue;
                    int cost = free ? 0 : fixedCost ?? evo.Card.EvoCost;
                    c.Engine.EvolveByEffect(c.Owner, d, evo.Card, cost, evo.Zone);
                    c.LastTargets = new List<CardInstance> { d };
                    Emit(c, label, $"[{d.TopCard.Name}]으로 진화 조건을 무시하고 진화 (코스트 {cost})");
                    return;
                }
                Emit(c, label, "진화할 수 있는 카드가 없습니다");
                c.LastStepResolved = false;
            }, null);
        }

        // ---- 테이머를 이 카드로 진화 (하이브리드체 [패] [메인]) ----
        //  "That Tamer digivolves into this card for digivolution cost of 3, ignoring its digivolution requirements" (BT18-026 · BT18-053 · BT18-070 · BT18-081)
        //  "Digivolve it into this card as if that card is a level 4 red Digimon for a digivolution cost of 3" (BT17-014 · BT17-026)
        //  "Digivolve into this card as if the Tamer is a level 4 red Digimon for the digivolution cost" (BT12-015)
        if (card.Type == CardType.Digimon
            && ((m = Regex.Match(s, @"^(?:that Tamer|it) digivolves into this card for (?:a )?digivolution cost (?:of )?(?<n>\d+),? ignoring (?:its |this card's |the )?digivolution requirements$", RegexOptions.IgnoreCase)).Success
                || (m = Regex.Match(s, @"^Digivolve (?:it |that Tamer )?into this card as if (?:that card|the Tamer|it) is a level \d+ (?:" + Colors + @") Digimon for (?:a digivolution cost of (?<n>\d+)|the digivolution cost|its digivolution cost)$", RegexOptions.IgnoreCase)).Success))
        {
            int? n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : null;
            return (c =>
            {
                var tamer = c.LastCard is { Type: CardType.Tamer } lt && c.Owner.Tamers.Contains(lt) ? lt : null;
                if (tamer == null || !c.Owner.Hand.Contains(c.Card)) { c.LastStepResolved = false; Emit(c, label, "진화할 테이머가 없습니다"); return; }
                var inst = c.Engine.EvolveTamerByEffect(c.Owner, tamer, c.Card, n ?? c.Card.EvoCost);
                if (inst == null) { c.LastStepResolved = false; return; }
                c.LastTargets = new List<CardInstance> { inst };
                Emit(c, label, $"테이머 [{tamer.Name}]을(를) [{c.Card.Name}]으로 진화");
            }, null);
        }

        // ---- 놓는다: 비용 규칙을 그대로 (낼 수 있으면 한다) — "Place 1 Digimon card with the [Blue Flare] trait from your trash under any of your Tamers" ·
        //      "Place 1 yellow card from your hand on top of your security stack" · "Place this Digimon as the bottom digivolution card of 1 of your other Digimon" · "Place 1 Digimon as the bottom security card"
        if ((m = Regex.Match(s, @"^Place (?<rest>(?!up to\b).+)$", RegexOptions.IgnoreCase)).Success
            && !Regex.IsMatch(s, @"\bface[- ]down\b", RegexOptions.IgnoreCase)
            && PlaceAsAction33(m.Groups["rest"].Value, card) is { } place)
        {
            var (canPay, pay, _, _) = place;
            return (c =>
            {
                if (!canPay(c)) { c.LastStepResolved = false; Emit(c, label, "놓을 카드가 없습니다"); return; }
                c.CostFailed = false;
                pay(c);
                if (c.CostFailed) { c.CostFailed = false; c.LastStepResolved = false; }
            }, null);
        }
        // "The Digimon played by this effect gains ＜Rush＞ for the turn" · "Digimon played by this effect gain ＜Rush＞ ~" (EX5-033 · ST14-09 · P-219) — 이 효과(절)가 등장시킨 디지몬
        if ((m = Regex.Match(s, @"^(?:the )?(?:Digimon played by this effect|Digimon this effect played) gains? (?<kws>＜[^＞]+＞(?:\s*,?\s*(?:and\s*)?＜[^＞]+＞)*)(?<dur> for the turn| until (?:the end of )?(?:your opponent's|their) turn(?: ends)?)?$", RegexOptions.IgnoreCase)).Success
            && ParseKeywordList(m.Groups["kws"].Value) is { } pgrants)
        {
            var pdur = ParseDuration(s, out _);
            if (pdur == Duration.None) pdur = Duration.ThisTurn;
            return (c =>
            {
                int until = Until(c, pdur);
                var list = c.PlayedThisEffect.Where(d => c.Owner.BattleArea.Contains(d)).ToList();
                foreach (var d in list)
                    foreach (var (k, sa) in pgrants)
                    {
                        if (k is { } kw) d.TimedKeywords.Add((kw, until));
                        else d.TimedSecurityAttackBuffs.Add((sa, until));
                    }
                c.RecalculateFieldBuffs();
                if (list.Count == 0) c.LastStepResolved = false;
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 키워드 획득" : "이 효과로 등장한 디지몬이 없습니다");
            }, null);
        }
        // "Trash both players' top security cards" (BT25-044)
        if (Regex.IsMatch(s, @"^Trash both players' top security cards?$", RegexOptions.IgnoreCase))
            return (c =>
            {
                foreach (var z in new[] { c.Owner, c.Enemy })
                {
                    if (z.SecurityStack.Count == 0) continue;
                    var x = z.SecurityStack[0]; z.SecurityStack.RemoveAt(0); z.Trash.Add(x);
                    c.Engine.NotifySecurityRemoved(z, x, trashed: true);
                }
                Emit(c, label, "서로의 시큐리티를 위에서부터 1장 파기");
            }, null);
        return null;
    }

    private static bool Do(Action a) { a(); return true; }

    [ThreadStatic] private static bool _placeAsAction;

    /// <summary>"Place ~" 동작 = 비용 규칙 "placing ~" (동작이므로 "떠나려 할 때" 효과로 막을 수 있다)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? PlaceAsAction33(string rest, CardData card)
    {
        bool outer = _placeAsAction;
        _placeAsAction = true;
        try { return ParseCost("placing " + rest, card); }
        finally { _placeAsAction = outer; }
    }
}
