using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.39.0 여러 곳에서 등장 · 사용 (자신의 디지몬 여럿의 진화원 · 패 또는 진화원 · 트래시 또는 진화원 · "패로 되돌리거나 등장")</summary>
internal static partial class SentenceParser
{
    /// <summary>카드를 꺼낼 곳 하나 — Kind: hand · trash · under · self · that · hosts</summary>
    internal sealed record Zone39(string Kind, Func<CardInstance, bool>? HostOk, bool AnyHost);

    private sealed record Src39(CardData Card, List<CardData>? List, CardInstance? Host, PlayOrigin Origin);

    internal const string ColorAdj39 = @"(?:(?:red|blue|yellow|green|black|purple|white)(?:(?:,| or| and/or|/) (?:red|blue|yellow|green|black|purple|white))* )";

    /// <summary>"your hand or this Digimon's digivolution cards" · "1 of your purple or blue Digimon's digivolution cards" · "your trash or from one of your Digimon's digivolution cards" · "any of your [Imperialdramon: Fighter Mode]'s digivolution cards"</summary>
    internal static List<(Zone39 Zone, Match M)>? ParseZones39(string from)
    {
        var list = new List<(Zone39, Match)>();
        string rest = from.Trim();
        while (rest.Length > 0)
        {
            Match m;
            Zone39 z;
            if ((m = Regex.Match(rest, @"^(?:from )?your hand", I)).Success) z = new("hand", null, false);
            else if ((m = Regex.Match(rest, @"^(?:from )?your trash", I)).Success) z = new("trash", null, false);
            else if ((m = Regex.Match(rest, @"^(?:from )?under (?:one of |1 of |any of )?your Tamers", I)).Success) z = new("under", null, false);
            else if ((m = Regex.Match(rest, @"^(?:from )?(?:this Digimon's|its) digivolution cards", I)).Success) z = new("self", null, false);
            else if ((m = Regex.Match(rest, @"^(?:from )?(?:that|those) Digimon's digivolution cards", I)).Success) z = new("that", null, false);
            else if ((m = Regex.Match(rest, $@"^(?:from )?(?<one>(?:1|one|any) of )?your (?<c>{ColorAdj39})?Digimon's digivolution cards", I)).Success)
            {
                var colors = Regex.Matches(m.Groups["c"].Value, Colors, I).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
                z = new("hosts", colors.Count == 0 ? null : d => d.Colors.Any(colors.Contains), !Regex.IsMatch(m.Groups["one"].Value, @"^(?:1|one) ", I));
            }
            else if ((m = Regex.Match(rest, $@"^(?:from )?(?<one>(?:1|one|any) of )?your (?<x>{TargetSpec.NameList})(?:'s|s') digivolution cards", I)).Success)
            {
                var xs = TargetSpec.Names(m.Groups["x"].Value);
                z = new("hosts", d => xs.Any(x => CardNames.Is(d.TopCard, x)), true);
            }
            else return null;
            list.Add((z, m));
            rest = rest[m.Length..].Trim();
            if (rest.Length == 0) break;
            var sep = Regex.Match(rest, @"^,?\s*or\s+", I);
            if (!sep.Success) return null;
            rest = rest[sep.Length..];
        }
        return list.Count == 0 ? null : list;
    }

    internal static readonly Regex PlayFromZonesRe39 = new(@"^(?<v>Return to the hand or play|Play or use|Play|Use) (?<mid>.+?)(?<as> as (?:another|other) Digimon| as a Tamer or another Digimon)? (?:without paying (?:the|its|their) (?:memory |play |use )?costs?|with the (?:play |use )?cost reduced by (?<red>\d+))$", RegexOptions.IgnoreCase);

    /// <summary>"1 play or use cost 10 or lower card ~" · "1 use cost 5 or lower [Huckmon] text Option card" · "[Aquatic|DATA SQUAD]" — 고르기 조건의 다른 표기 (렌더러도 쓴다)</summary>
    /// <summary>
    /// "1 card with [Gammamon] in its text or the [VB] trait from your hand" — 고르기 조건 안의 "in"과 곳의 "from"을 나눈다: 왼쪽부터 나눠 보고 곳 · 고르기가 모두 읽히는 첫 자리
    /// </summary>
    internal static (string Pick, List<(Zone39 Zone, Match M)> Zones, bool Suspended)? SplitMid39(string mid)
    {
        foreach (Match sep in Regex.Matches(mid, @"(?<sus> suspended)? (?:in|from) (?=(?:your|this|its|that|those|under|(?:1|one|any) of your)\b)", I))
        {
            string pick = mid[..sep.Index].Trim(), from = mid[(sep.Index + sep.Length)..].Trim();
            bool sus = sep.Groups["sus"].Success;
            var ss = Regex.Match(from, @" suspended$", I);
            if (ss.Success) { sus = true; from = from[..ss.Index]; }
            if (pick.Length == 0 || ParseZones39(from) is not { } zones) continue;
            if (SplitPicks39(pick).All(p => ParseCardPick(NormalizePick39(p)) != null || ParseNamePick(NormalizePick39(p)) != null))
                return (pick, zones, sus);
        }
        return null;
    }

    /// <summary>"1 A and 1 B" → 1장씩 (전체가 한 고르기로 읽히면 나누지 않는다 — "1 ~ card with the [X] trait and a play cost of 7 or less")</summary>
    internal static string[] SplitPicks39(string pick)
    {
        string whole = NormalizePick39(pick);
        if (ParseCardPick(whole) != null || ParseNamePick(whole) != null) return new[] { pick.Trim() };
        return Regex.Split(pick.Trim(), @"\s+and\s+(?=(?:1|a|an) )", I);
    }

    internal static string NormalizePick39(string part)
    {
        string pt = Regex.Replace(part.Trim(), @"^(?:an?|one) ", "1 ", I);
        pt = Regex.Replace(pt, @"^(?<n>\d+) (?:(?:play or use|use|play|memory) )?cost (?<c>\d+) or (?:lower|less) (?<rest>.+)$", "${n} ${rest} with a cost of ${c} or less", I);
        pt = Regex.Replace(pt, @"(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) text (?<k>Option card|Digimon card|card)", "${k} with ${x} in its text", I);
        pt = Regex.Replace(pt, @"\[(?<a>[^\]|]+)\|(?<b>[^\]]+)\]", "[${a}] or [${b}]");
        return pt;
    }

    /// <summary>
    /// "Play 1 blue level 3 Digimon card from 1 of your blue Digimon's digivolution cards without paying the cost" (EX3-022 — 공식 "블루인 자신의 디지몬 1마리의 진화원에서 ~ 등장시킬 수 있다") ·
    /// "Play 1 [Diaboromon] from your hand or this Digimon's digivolution cards ~" (BT24-065) · "~ from your trash or from one of your Digimon's digivolution cards ~" (BT17-025) ·
    /// "Play 1 level 3 Digimon card and 1 level 4 Digimon card from this Digimon's digivolution cards ~" (BT10-027 — "1장씩") · "~ suspended ~" (BT7-063) ·
    /// "Return to the hand or play 1 ~ from this Digimon's digivolution cards ~" (BT18-017 — 공식 "~ 패로 되돌리거나, 코스트를 지불하지 않고 등장시킬 수 있다") ·
    /// "Use 1 ~ Option card from your hand or this Digimon's digivolution cards without paying the cost" (BT25-085) · "Play or use 1 ~ card from ~" (EX12-077) ·
    /// "Play or use 1 card with ~ from your hand with the cost reduced by 2" (EX12-013 — 옵션은 코스트를 내고 사용)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? PlayFromZones39(string s, string label)
    {
        var m = PlayFromZonesRe39.Match(s);
        if (!m.Success || SplitMid39(m.Groups["mid"].Value) is not { } mid) return null;
        var zones = mid.Zones;
        string v = m.Groups["v"].Value.ToLowerInvariant();
        bool play = v.Contains("play"), use = v.Contains("use"), returnOr = v.StartsWith("return");
        bool suspended = mid.Suspended;
        int? red = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : null;
        // "1 A and 1 B" — 1장씩
        if (Regex.IsMatch(mid.Pick, @"\bor up to \d+", I)) return null;   // "1 [Rosemon] or up to 2 ~" — 수가 다른 두 고르기는 읽지 않는다
        var parts = SplitPicks39(mid.Pick);
        var picks = new List<(int Count, Func<CardData, bool> Match)>();
        foreach (var part in parts)
        {
            string pt = NormalizePick39(part);
            var p = ParseCardPick(pt) ?? ParseNamePick(pt);
            if (p == null) return null;   // 읽지 못하면 다른 13차 규칙에 맡긴다
            picks.Add(p.Value);
        }
        bool fromSourcesOnly = zones.All(z => z.Zone.Kind is "self" or "that" or "hosts");
        return (c =>
        {
            List<Src39> Pool(Func<CardData, bool> match)
            {
                var pool = new List<Src39>();
                foreach (var (z, _) in zones)
                {
                    switch (z.Kind)
                    {
                        case "hand": pool.AddRange(c.Owner.Hand.Where(match).Select(x => new Src39(x, c.Owner.Hand, null, PlayOrigin.Hand))); break;
                        case "trash": pool.AddRange(c.Owner.Trash.Where(match).Select(x => new Src39(x, c.Owner.Trash, null, PlayOrigin.Trash))); break;
                        case "under": pool.AddRange(c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x) && match(x)).Select(x => new Src39(x, c.Owner.UnderTamers, null, PlayOrigin.UnderTamers))); break;
                        default:
                            IEnumerable<CardInstance> hs = z.Kind switch
                            {
                                "self" => c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new[] { c.Self } : Array.Empty<CardInstance>(),
                                "that" => (c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d)) ?? (c.Other != null && c.Owner.BattleArea.Contains(c.Other) ? c.Other : null)) is { } t ? new[] { t } : Array.Empty<CardInstance>(),
                                _ => c.Owner.BattleArea.Where(d => z.HostOk == null || z.HostOk(d)),
                            };
                            foreach (var h in hs.ToList())
                                pool.AddRange(h.SourcesWhere(x => !h.IsFaceDown(x) && match(x)).Select(x => new Src39(x, null, h, PlayOrigin.Sources)));
                            break;
                    }
                }
                return pool;
            }
            int CostOf(CardData x) => red is int r ? (x.Type == CardType.Option ? Math.Max(0, x.PlayCost - r) : c.Engine.ReducedPlayCost(c.Owner, x, Math.Max(0, x.PlayCost - r), x.PlayCost)) : 0;
            bool Usable(CardData x) => x.Type == CardType.Option && ActionValidator.MissingOptionColors(c.Owner, x).Count == 0 && !EffectRules.OptionUseBlocked(c.State, c.Owner);
            bool Ok(Src39 src)
            {
                var x = src.Card;
                if (red != null && CostOf(x) > Math.Max(0, c.OwnerMemory)) return false;
                if (x.Type is CardType.Digimon or CardType.Tamer) return (play || returnOr) && (returnOr || c.Engine.CanPlayByEffect(c.Owner, x, src.Origin));
                return use && Usable(x);
            }
            var done = new List<string>();
            var taken = new List<CardData>();
            foreach (var (count, match) in picks)
            {
                var ranked = Pool(x => match(x) && !taken.Contains(x)).Where(Ok)
                    .OrderByDescending(p => p.Card.Type == CardType.Digimon).ThenByDescending(p => p.Card.Level).ThenByDescending(p => p.Card.PlayCost).ToList();
                if (ranked.Count == 0) continue;
                var zone = fromSourcesOnly ? CardChoiceZone.DigivolutionCards : CardChoiceZone.Other;
                var idx = c.Engine.ChooseCardIndexes(c.Owner, zone, ranked.Select(p => p.Card).ToList(), Math.Min(count, ranked.Count), upTo: false,
                    $"[{c.Card.Name}] {(returnOr ? "패로 되돌리거나 등장시킬" : use && !play ? "사용할" : "등장시킬")} 카드를 고르세요{(red != null ? $" (코스트 -{red})" : "")}.");
                foreach (var i in idx)
                {
                    var src = ranked[i];
                    var x = src.Card;
                    Func<bool> take = () => src.Host != null ? src.Host.TakeSource(y => ReferenceEquals(y, x)) != null : src.List!.Remove(x);
                    taken.Add(x);
                    if (returnOr)
                    {
                        bool canPlay = x.Type is CardType.Digimon or CardType.Tamer && c.Owner.BattleArea.Count < 12 && c.Engine.CanPlayByEffect(c.Owner, x, src.Origin);
                        int opt = canPlay ? c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] [{x.Name}]을(를)", new[] { "패로 되돌린다", "코스트를 지불하지 않고 등장시킨다" }, 1) : 0;
                        Diag39.Hit(ref Diag39.ReturnOrPlay);
                        if (opt == 1 && c.Engine.TryPlayByEffect(c.Owner, x, src.Origin, take, suspended: suspended)) { done.Add($"{x.Name} 등장"); continue; }
                        if (!take()) continue;
                        c.Owner.Hand.Add(x);
                        done.Add($"{x.Name} 패로");
                        continue;
                    }
                    if (x.Type == CardType.Option)
                    {
                        if (!take()) continue;
                        int oc = CostOf(x);
                        if (oc > 0) c.AddMemory(-oc);
                        c.Engine.UseOptionByEffect(c.Owner, x);
                        Diag39.Hit(ref Diag39.PlayFromSources);
                        done.Add($"{x.Name} 사용{(red != null ? $"(코스트 {oc})" : "")}");
                        continue;
                    }
                    if (c.Owner.BattleArea.Count >= 12) break;
                    int? pay = red != null ? CostOf(x) : null;
                    if (!c.Engine.TryPlayByEffect(c.Owner, x, src.Origin, take, suspended: suspended, payCost: pay)) continue;
                    Diag39.Hit(ref Diag39.PlayFromSources);
                    done.Add($"{x.Name} 등장{(pay != null ? $"(코스트 {pay})" : "")}");
                }
            }
            if (!fromSourcesOnly || zones.Count > 0) c.RecalculateFieldBuffs();
            if (done.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시키거나 사용할 카드가 없습니다"); return; }
            Emit(c, label, string.Join(", ", done.Select(x => $"[{x}]")));
        }, null);
    }
}

/// <summary>v0.39.0 테이머 아래에서 진화: "This Digimon may digivolve into [OmniShoutmon] under your Tamers without paying the cost" (BT19-008 — 공식 "이 디지몬을 자신의 테이머 아래의 「오메가샤우트몬」으로 코스트를 지불하지 않고 진화시킬 수 있다")</summary>
internal static partial class SentenceParser
{
    internal static readonly Regex UnderTamerEvoRe39 = new(@"^(?<t>this Digimon|it|that Digimon|\d+ of your (?:other )?(?:[^,]*? )?Digimon(?: with [^,]+?)?) (?:may )?digivolves? into (?<pick>.+?) (?:from )?under (?:one of |1 of |any of )?your Tamers(?: for (?:a digivolution cost of (?<cost>\d+)|its digivolution cost))?,? ?(?<free>without paying (?:the|its) (?:memory |digivolution )?cost|with the (?:digivolution )?cost reduced by (?<red>\d+))?$", RegexOptions.IgnoreCase);

    private static (Action<EffectContext>? Act, string? Error)? UnderTamerEvo39(string s, string label, bool security)
    {
        var m = UnderTamerEvoRe39.Match(s);
        if (!m.Success) return null;
        var target = TargetSpec.Parse(Regex.IsMatch(m.Groups["t"].Value, "^(?:it|that Digimon)$", I) ? "that Digimon" : m.Groups["t"].Value);
        var pick = ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value);
        if (target == null || target.Opponent || target.AnySide || pick == null) return null;
        var match = pick.Value.Match;
        bool free = m.Groups["free"].Value.StartsWith("without", StringComparison.OrdinalIgnoreCase);
        int reduce = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : 0;
        int? fixedCost = m.Groups["cost"].Success ? int.Parse(m.Groups["cost"].Value) : null;
        bool may = Regex.IsMatch(s, @" may digivolve", I);
        return (c =>
        {
            foreach (var d in Pick(target, c, security).Where(x => c.Owner.BattleArea.Contains(x)).ToList())
            {
                var pool = c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x) && x.Type == CardType.Digimon && match(x) && ActionValidator.ValidateEvolution(d, x).CanExecute)
                    .OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
                if (pool.Count == 0) continue;
                var evo = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, false, $"[{c.Card.Name}] 테이머 아래에서 진화할 카드를 고르세요.").FirstOrDefault() ?? pool[0];
                int cost = free ? 0 : fixedCost ?? Math.Max(0, evo.EvoCost - (c.Engine.CanReduceEvoCost(c.Owner) ? reduce : 0));
                if (may && !c.Engine.AskYesNo(c.Owner, d, $"[{c.Card.Name}] [{d.TopCard.Name}]을(를) 테이머 아래의 [{evo.Name}]으로 진화시킬까요? (코스트 {cost})", true)) { c.LastStepResolved = false; return; }
                c.Engine.EvolveByEffect(c.Owner, d, evo, cost, c.Owner.UnderTamers);
                c.LastTargets = new List<CardInstance> { d };
                Diag39.Hit(ref Diag39.SourcePlace);
                Emit(c, label, $"테이머 아래의 [{evo.Name}]으로 진화 (코스트 {cost})");
                return;
            }
            Emit(c, label, "진화할 수 있는 카드가 없습니다");
            c.LastStepResolved = false;
        }, null);
    }
}

/// <summary>v0.39.0 "1 of your Digimon or Tamers may digivolve into ~" (BT18-022 — 공식 "자신의 디지몬/테이머 1마리(명)를 패의 ~ 디지몬 카드로 지불하는 진화 코스트 -1 하여 진화시킬 수 있다")</summary>
internal static partial class SentenceParser
{
    internal static readonly Regex DigimonOrTamerEvoRe39 = new(@"^1 of your Digimon or Tamers (?:may )?digivolve into (?<pick>.+?) in (?:the|your) (?<from>hand or (?:the |your )?trash|hand|trash)(?: with the (?:digivolution )?cost reduced by (?<red>\d+)| (?<free>without paying (?:the|its) (?:digivolution )?cost))?$", RegexOptions.IgnoreCase);

    private static (Action<EffectContext>? Act, string? Error)? DigimonOrTamerEvo39(string s, string label)
    {
        var m = DigimonOrTamerEvoRe39.Match(s);
        if (!m.Success) return null;
        var pick = ParseCardPick(Regex.Replace(m.Groups["pick"].Value, @"^(?:an?|1) ", "1 ", I)) ?? ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value);
        if (pick == null) return null;
        var match = pick.Value.Match;
        string from = m.Groups["from"].Value.ToLowerInvariant();
        bool hand = from.Contains("hand"), trash = from.Contains("trash"), free = m.Groups["free"].Success;
        int red = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : 0;
        return (c =>
        {
            var zones = new List<List<CardData>>();
            if (hand) zones.Add(c.Owner.Hand);
            if (trash) zones.Add(c.Owner.Trash);
            int Cost(int baseCost) => free ? 0 : Math.Max(0, baseCost - (red > 0 && c.Engine.CanReduceEvoCost(c.Owner) ? red : 0));
            // 후보: (디지몬 또는 테이머, 진화할 카드, 있던 곳, 코스트)
            var options = new List<(CardInstance? Digimon, CardData? Tamer, CardData Evo, List<CardData> Zone, int Cost)>();
            foreach (var z in zones)
                foreach (var x in z.Where(x => x.Type == CardType.Digimon && match(x)).Distinct().ToList())
                {
                    foreach (var d in c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && ActionValidator.ValidateEvolution(d, x).CanExecute))
                        options.Add((d, null, x, z, Cost(x.EvoCost)));
                    foreach (var t in c.Owner.Tamers.Distinct().ToList())
                        if (CardScripts.TamerEvolutionCost(x, t) is int tc) options.Add((null, t, x, z, Cost(tc)));
                }
            if (options.Count == 0) { c.LastStepResolved = false; Emit(c, label, "진화할 수 있는 디지몬 · 테이머가 없습니다"); return; }
            options = options.OrderByDescending(o => o.Evo.Level).ThenByDescending(o => o.Evo.DP).ThenBy(o => o.Cost).ToList();
            var texts = options.Select(o => $"[{(o.Digimon != null ? o.Digimon.TopCard.Name : o.Tamer!.Name)}] → [{o.Evo.Name}] ({(ReferenceEquals(o.Zone, c.Owner.Trash) ? "트래시" : "패")} · 코스트 {o.Cost})").ToList();
            texts.Add("진화하지 않는다");
            int pickIdx = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 진화시킬 디지몬/테이머와 카드를 고르세요.", texts, 0);
            if (pickIdx < 0 || pickIdx >= options.Count) { c.LastStepResolved = false; Emit(c, label, "진화하지 않았습니다"); return; }
            var o = options[pickIdx];
            if (o.Digimon != null)
            {
                c.Engine.EvolveByEffect(c.Owner, o.Digimon, o.Evo, o.Cost, o.Zone);
                c.LastTargets = new List<CardInstance> { o.Digimon };
            }
            else if (c.Engine.EvolveTamerByEffect(c.Owner, o.Tamer!, o.Evo, o.Cost, o.Zone) is { } ti) c.LastTargets = new List<CardInstance> { ti };
            else { c.LastStepResolved = false; return; }
            Diag39.Hit(ref Diag39.SourcePlace);
            Emit(c, label, $"[{o.Evo.Name}]으로 진화 (코스트 {o.Cost})");
        }, null);
    }
}
