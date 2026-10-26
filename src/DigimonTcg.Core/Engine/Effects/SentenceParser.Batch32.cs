using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.32.0 미지원 문장 6차 묶음 — "떠나려 할 때"의 다른 표기 (색 · 쉼표 목록 주어 · "소멸하거나 패/덱으로 되돌아갈 때" · "상대의 효과가 소멸시킬 때") ·
/// 막기 동작 · 비용 (Lv.이 같은 진화원 2장 등) 외. 앞의 규칙에 걸리지 않은 것만 본다 (기존 판정은 그대로)
/// </summary>
internal static partial class SentenceParser
{
    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase32(string ev)
    {
        Match m;
        (List<EffectTrigger>, Func<EffectContext, bool>?) One(EffectTrigger t, Func<EffectContext, bool>? cond = null) => (new List<EffectTrigger> { t }, cond);

        // ---- 떠나려 할 때 · 소멸할 때 ----
        // 원문 표기 정리: "would leave battle area" (EX7-014 · EX7-049) · "When this would leave" (BT25-101) — 고친 뒤 원래 규칙으로
        string fixedEv = Regex.Replace(ev, @"\bwould leave battle area\b", "would leave the battle area", RegexOptions.IgnoreCase);
        fixedEv = Regex.Replace(fixedEv, @"^this would\b", "this Digimon would", RegexOptions.IgnoreCase);
        // "[Dracomon] or [Examon] text Digimon" → "Digimon with [Dracomon] or [Examon] in their texts"
        fixedEv = Regex.Replace(fixedEv, $@"\byour (?<x>{TargetSpec.NameList}) text Digimon\b", "your Digimon with ${x} in their texts", RegexOptions.IgnoreCase);
        if (fixedEv != ev) return ParseEventPhrase(fixedEv);

        // "this yellow Digimon with the [Data] or [Witchelny] trait would leave ~" (BT18-030 · BT19-029) — 색 조건을 더한다
        if ((m = Regex.Match(ev, $@"^this (?<col>(?:{Colors})(?: or (?:{Colors}))*) Digimon(?<rest> (?:with .+? )?would .+)$", RegexOptions.IgnoreCase)).Success
            && ParseEventPhrase("this Digimon" + m.Groups["rest"].Value) is { } colored)
        {
            var cols = Regex.Matches(m.Groups["col"].Value, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
            var inner = colored.Condition;
            return (colored.Triggers, c => c.Self != null && c.Self.TopCard.AllColors.Any(cols.Contains) && (inner == null || inner(c)));
        }

        // "this Digimon with the [Night Claw], [Light Fang] or [Galaxy] trait would be deleted" — 주어 안의 쉼표 목록 (원래 규칙은 쉼표 앞까지만 본다)
        if ((m = Regex.Match(ev, @"^this Digimon (?<with>with .+?) would (?<what>leave the battle area|be deleted)(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && m.Groups["with"].Value.Contains(',')
            && TargetSpec.Parse("all of your Digimon " + m.Groups["with"].Value) is { Opponent: false, AnySide: false } selfWith
            && ParseEventPhrase($"this Digimon would {m.Groups["what"].Value}{m.Groups["q"].Value}") is { } bare)
        {
            var inner = bare.Condition;
            return (bare.Triggers, c => c.Self != null && selfWith.Filters.All(f => f(c.Self)) && (inner == null || inner(c)));
        }
        // "one of your Digimon with the [Angel], [Archangel] or [Three Great Angels] trait would leave ~" · "one of your [Apocalymon] or Digimon with the [Dark Masters] trait would leave ~"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?!opponent)(?<subj>(?:other )?.+?) would (?<what>leave the battle area|be deleted)(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && !Regex.IsMatch(m.Groups["subj"].Value, @"\bTamers?\b", RegexOptions.IgnoreCase)
            && ParseLeaveQualifier(m.Groups["q"].Value) is { } listOk
            && LeaveSubject32(m.Groups["subj"].Value) is { } listWho)
            return AllyLeave32(m.Groups["what"].Value.StartsWith("be deleted", StringComparison.OrdinalIgnoreCase), listOk, listWho);

        // "this Digimon would be deleted or returned to your hand or deck (by an effect)" (BT11-062 · EX3-013 · EX4-021 · EX6-031) —
        // 공식 한글 (BT11-062): "효과로 소멸할 때, 또는 패/덱으로 되돌아갈 때" — "by an effect"는 소멸에 붙는다 (패 · 덱으로 되돌리는 것은 언제나 효과)
        if ((m = Regex.Match(ev, @"^this Digimon(?<with> with .+?)? would be deleted or returned to (?:your|the|its owner's) hands? or decks?(?<eff> by (?:an )?effects?)?$", RegexOptions.IgnoreCase)).Success)
        {
            TargetSpec? with = m.Groups["with"].Success ? TargetSpec.Parse("all of your Digimon" + m.Groups["with"].Value) : null;
            if (m.Groups["with"].Success && with == null) return null;
            bool byEffect = m.Groups["eff"].Success;
            return One(EffectTrigger.ThisDigimonWouldLeave, c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self)
                && (l.Destination == GoldfishEngine.LeaveDestination.Deleted ? !byEffect || !l.InBattle : l.Destination is GoldfishEngine.LeaveDestination.Hand or GoldfishEngine.LeaveDestination.Deck)
                && (with == null || with.Filters.All(f => f(c.Self!))));
        }
        // "this Digimon has [Greymon] or [Omnimon] in its name and an effect would delete it or return it to your hand or deck" (BT9-012 · P-072)
        //  · "this Digimon has [Garurumon] or [Omnimon] in its name and would be deleted in battle" (BT9-024 · P-073) — 공식 한글: "명칭에 ~을 포함하는 이 디지몬이 ~할 때"
        if ((m = Regex.Match(ev, $@"^this Digimon has (?<x>{TargetSpec.NameList}) in its name and (?:(?<eff>an effect would delete it or return it to (?:your|the|its owner's) hand or deck)|(?<battle>would be deleted in battle))$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            bool Named(EffectContext c) => c.Self != null && xs.Any(x => TargetSpec.NameHas(c.Self.TopCard, x));
            if (m.Groups["battle"].Success)
                return One(EffectTrigger.ThisDigimonWouldBeDeleted, c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && l.InBattle && Named(c));
            return One(EffectTrigger.ThisDigimonWouldLeave, c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && Named(c)
                && (l.Destination == GoldfishEngine.LeaveDestination.Deleted ? !l.InBattle : l.Destination is GoldfishEngine.LeaveDestination.Hand or GoldfishEngine.LeaveDestination.Deck));
        }
        // "an opponent's effect would delete this Digimon" (BT17-050) — 공식 한글: "이 디지몬이 상대의 효과로 소멸할 때"
        if (Regex.IsMatch(ev, @"^(?:an|your) opponent's effects? would delete this Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.ThisDigimonWouldBeDeleted, c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && !l.InBattle && !l.ByOwnEffect);

        // ---- 등장한 곳: "When you play a Digimon from your trash" · "~ from one of your Digimon's digivolution cards" · "When one of your Digimon is played from digivolution cards" ----
        if ((m = Regex.Match(ev, @"^(?:you play (?:an? |1 )?Digimon(?: cards?)? from (?<z>your trash|(?:one of your Digimon's |your Digimon's |their |its )?digivolution cards)|(?:one of |any of )?your Digimon (?:is|are) played from (?<z>your trash|(?:their |its )?digivolution cards))$", RegexOptions.IgnoreCase)).Success)
        {
            var origin = m.Groups["z"].Value.Contains("trash", StringComparison.OrdinalIgnoreCase) ? PlayOrigin.Trash : PlayOrigin.Sources;
            return One(EffectTrigger.AllyDigimonPlayed, c => c.Other != null && c.Engine.CurrentPlayOrigin == origin);
        }

        // ---- 상대 디지몬의 진화원: "When a digivolution card of an opponent's Digimon is trashed" · "When a card is trashed from under one of your opponent's Digimon" ----
        if (Regex.IsMatch(ev, @"^(?:a digivolution card of (?:an|one of your) opponent's Digimon is trashed|(?:a card is|cards are) trashed from under (?:one of your|an|your) opponent's Digimon|(?:an|your) opponent's Digimon's digivolution cards? (?:is|are) trashed)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentSourcesTrashed);

        // ---- 레스트 ----
        // "When any of your yellow or red Tamers suspend" (BT21-004)
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<color>(?:{Colors})(?: or (?:{Colors}))*) Tamers? {SuspendVerb}$", RegexOptions.IgnoreCase)).Success)
        {
            var cols = Regex.Matches(m.Groups["color"].Value, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
            return One(EffectTrigger.AllyTamerSuspended, c => c.TriggerCard != null && c.TriggerCard.AllColors.Any(cols.Contains));
        }
        // 효과로 레스트: "When an effect suspends a Digimon or Tamer" · "~ another Digimon or Tamer" · "When a Digimon is suspended by an effect" ·
        //  "When one of your effects suspends a Tamer" · "When effects suspend any of your [WG] trait Digimon" (효과의 비용으로 레스트한 것도 효과 — 어택 · 블록은 아님)
        if ((m = Regex.Match(ev, @"^(?:(?<mine>one of your effects) suspends|(?:an effect|effects) suspends?) (?<other>another |other )?(?:an? |any |any of )?(?<who>your (?<spec>[^,]*?)Digimon|Digimon or Tamers?|Digimon|Tamers?)$|^(?:an? |any )?(?<who>Digimon|Tamer) (?:is|are|becomes?) suspended by (?:an )?effects?$", RegexOptions.IgnoreCase)).Success)
        {
            bool mine = m.Groups["mine"].Success, other = m.Groups["other"].Success;
            string who = m.Groups["who"].Value.ToLowerInvariant();
            Func<CardInstance, bool>? spec = null;
            if (m.Groups["spec"].Success && m.Groups["spec"].Value.Trim().Length > 0)
            {
                if (TargetSpec.Parse("all of your " + m.Groups["spec"].Value + "Digimon") is not { Opponent: false, AnySide: false } ts) return null;
                spec = d => ts.Filters.All(f => f(d));
            }
            bool ownOnly = who.StartsWith("your");
            bool digimon = who.Contains("digimon"), tamer = who.Contains("tamer");
            Func<EffectContext, bool> cause = c => c.Engine.SuspendCause is { } by && (!mine || ReferenceEquals(by, c.Owner));
            var ts2 = new List<EffectTrigger>();
            if (digimon) { ts2.Add(EffectTrigger.AllyDigimonSuspended); if (!ownOnly) ts2.Add(EffectTrigger.OpponentDigimonSuspended); }
            if (tamer) { ts2.Add(EffectTrigger.AllyTamerSuspended); ts2.Add(EffectTrigger.OpponentTamerSuspended); }
            return (ts2, c => cause(c) && (c.Other != null
                ? (!other || !ReferenceEquals(c.Other, c.Self)) && (spec == null || spec(c.Other))
                : c.TriggerCard != null && (!other || !ReferenceEquals(c.TriggerCard, c.Card))));
        }

        // ---- 등장 (카드 조건) ----
        // "When your card with the [Dark Animal] or [SoC] trait is played" = "you play a card with ~"
        if ((m = Regex.Match(ev, @"^your (?<s>card with .+?) is played$", RegexOptions.IgnoreCase)).Success)
            return ParseEventPhrase("you play a " + m.Groups["s"].Value);
        // "When you play a 2-color black and yellow Digimon" (BT13-101 · BT9-091)
        if ((m = Regex.Match(ev, @"^you play (?<subj>(?:an?|1) \d+-colou?r .+? Digimon)$", RegexOptions.IgnoreCase)).Success && AttackerSubject(m.Groups["subj"].Value) is { } twoColor)
            return One(EffectTrigger.AllyDigimonPlayed, c => c.Other != null && twoColor(c, c.Other));
        // "When you play a Digimon with ＜Save＞ in its text" (BT12-005)
        if ((m = Regex.Match(ev, @"^you play (?:an?|1) Digimon with ＜(?<k>[^＞]+)＞ in (?:its|their) texts?$", RegexOptions.IgnoreCase)).Success)
        {
            string k = "＜" + m.Groups["k"].Value;
            return One(EffectTrigger.AllyDigimonPlayed, c => c.Other != null && (c.Other.TopCard.EffectText + " " + c.Other.TopCard.InheritedEffectText).Contains(k, StringComparison.OrdinalIgnoreCase));
        }
        // "When you play a Digimon with the [Royal Knight] trait or a blue Tamer" (BT13-030)
        if ((m = Regex.Match(ev, $@"^you play (?:an?|1) (?<d>Digimon with .+?) or (?:an?|1) (?<col>{Colors}) Tamer$", RegexOptions.IgnoreCase)).Success
            && EventCardSubject(m.Groups["d"].Value) is { } dWith)
        {
            var col = Enum.Parse<CardColor>(m.Groups["col"].Value, ignoreCase: true);
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.AllyTamerPlayed },
                c => c.Other != null ? dWith(c.Other.TopCard) : c.TriggerCard != null && c.TriggerCard.AllColors.Contains(col));
        }
        // "When another [Eosmon] is played" (어느 쪽이든 — 공식 한글 "다른 「이오스몬」이 등장했을 때")
        if ((m = Regex.Match(ev, $@"^another (?<x>{TargetSpec.NameList}) is played$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.OpponentDigimonPlayed },
                c => c.Other != null && !ReferenceEquals(c.Other, c.Self) && xs.Any(x => CardNames.Is(c.Other.TopCard, x)));
        }
        // "When one of your Digimon or Tamers that have the [SoC] trait or [Pulsemon] in its text is played" (BT17-069)
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your Digimon or Tamers (?:that|which) (?:have|has) the (?<t>{TargetSpec.NameList}) trait or (?<x>{TargetSpec.NameList}) in (?:its|their) texts? (?:is|are) played$", RegexOptions.IgnoreCase)).Success)
        {
            var traits = TargetSpec.Names(m.Groups["t"].Value); var texts = TargetSpec.Names(m.Groups["x"].Value);
            bool Ok(CardData x) => traits.Any(t => TargetSpec.HasTrait(x, t)) || texts.Any(t => (x.EffectText + " " + x.InheritedEffectText + " " + x.Name).Contains(t, StringComparison.OrdinalIgnoreCase));
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.AllyTamerPlayed }, c => (c.Other?.TopCard ?? c.TriggerCard) is { } p && Ok(p));
        }
        // "When any of your [Marcus Damon]s are played or suspend" (AD1-016 — 테이머)
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<x>{TargetSpec.NameList})s? (?:is|are) played or (?:suspends?|becomes? suspended)$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (new List<EffectTrigger> { EffectTrigger.AllyTamerPlayed, EffectTrigger.AllyTamerSuspended }, c => c.TriggerCard != null && xs.Any(x => CardNames.Is(c.TriggerCard, x)));
        }
        // "When an effect plays one of your [Terriermon] or [Lopmon] or green Tamers" (BT17-043)
        if ((m = Regex.Match(ev, $@"^(?:an effect plays|effects play) (?:one of |any of )?your (?<x>{TargetSpec.NameList}) or (?<col>{Colors}) Tamers?$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); var col = Enum.Parse<CardColor>(m.Groups["col"].Value, ignoreCase: true);
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.AllyTamerPlayed },
                c => c.Engine.PlayingByEffect && (c.Other != null ? xs.Any(x => CardNames.Is(c.Other.TopCard, x)) : c.TriggerCard != null && (c.TriggerCard.AllColors.Contains(col) || xs.Any(x => CardNames.Is(c.TriggerCard, x)))));
        }
        // "When effects play one of your Tokens or a Digimon with the [Puppet] trait" (ST19-14)
        if ((m = Regex.Match(ev, @"^(?:an effect plays|effects play) (?:one of |any of )?your Tokens? or (?:an? )?(?<d>Digimon with .+)$", RegexOptions.IgnoreCase)).Success
            && EventCardSubject(m.Groups["d"].Value) is { } tokOr)
            return One(EffectTrigger.AllyDigimonPlayed, c => c.Engine.PlayingByEffect && c.Other != null && (GoldfishEngine.IsToken(c.Other.TopCard) || tokOr(c.Other.TopCard)));
        // "When an opponent's Digimon or one of your Digimon with the [Seven Great Demon Lords] trait is played" (EX6-061)
        if ((m = Regex.Match(ev, @"^(?:an|one of your) opponent's Digimon or (?:one of |any of )?your (?<d>Digimon with .+?) (?:is|are) played$", RegexOptions.IgnoreCase)).Success
            && EventCardSubject(m.Groups["d"].Value) is { } mineWith)
            return (new List<EffectTrigger> { EffectTrigger.OpponentDigimonPlayed, EffectTrigger.AllyDigimonPlayed },
                c => c.Other != null && (c.Trigger == EffectTrigger.OpponentDigimonPlayed || mineWith(c.Other.TopCard)));

        // ---- 진화 ----
        // "When any of your Digimon or Tamers digivolve into Digimon with the [Hybrid] or [Ten Warriors] trait" · "When any of your Digimon digivolve into a Digimon card with [Lucemon] in its name"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your Digimon(?: or Tamers)? digivolves? into (?:an? |1 )?(?<into>Digimon(?: card)? with .+)$", RegexOptions.IgnoreCase)).Success
            && EventCardSubject(Regex.Replace(m.Groups["into"].Value, @"^Digimon card\b", "Digimon", RegexOptions.IgnoreCase)) is { } intoWith)
            return One(EffectTrigger.AllyDigimonDigivolved, c => c.Other != null && intoWith(c.Other.TopCard));

        // ---- 어택 · 배틀 ----
        // "When an opponent's Digimon with as many or fewer digivolution cards as this Digimon attacks" (BT15-032)
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon with as many or fewer digivolution cards as this Digimon attacks$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonAttacks, c => c.Other != null && c.Self != null && c.Other.EvolutionSources.Count <= c.Self.EvolutionSources.Count);
        // "When this Digimon deletes an opponent's Digimon by battle" = "in battle"
        if (Regex.IsMatch(ev, @"\bby battle$", RegexOptions.IgnoreCase) && Regex.IsMatch(ev, @"^this Digimon deletes\b", RegexOptions.IgnoreCase))
            return ParseEventPhrase(Regex.Replace(ev, @"\bby battle$", "in battle", RegexOptions.IgnoreCase));
        // "When any of your [TS] trait Digimon win a battle" (BT25-020) — 배틀에서 상대만 소멸 (자신의 디지몬은 남는다)
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>[^,]*?Digimon(?: with [^,]+?)?) wins? (?:a|the) battle$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["subj"].Value) is { } winner)
            return One(EffectTrigger.AllyDeletedOpponentInBattle, c => c.Other != null && c.Owner.BattleArea.Contains(c.Other) && winner(c, c.Other));
        // "When this Digimon with [Angoramon] in its text or the [NSp] trait wins a battle" (EX12-051)
        if ((m = Regex.Match(ev, $@"^this Digimon with (?<x>{TargetSpec.NameList}) in its text or the (?<t>{TargetSpec.NameList}) trait wins (?:a|the) battle$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); var ts = TargetSpec.Names(m.Groups["t"].Value);
            return One(EffectTrigger.WonBattle, c => c.Self != null && (xs.Any(x => (c.Self.TopCard.EffectText + " " + c.Self.TopCard.InheritedEffectText + " " + c.Self.TopCard.Name).Contains(x, StringComparison.OrdinalIgnoreCase)) || ts.Any(t => TargetSpec.HasTrait(c.Self.TopCard, t))));
        }
        return null;
    }

    /// <summary>
    /// v0.32.0 진화 코스트 감소 문장의 다른 표기 → 원래 규칙이 읽는 표기 (BT2-088 · BT4-095 · BT5-092 · BT8-091 · BT9-090 · RB1-034):
    /// "When digivolving one of your Digimon into ~" · "into a Digimon card in your hand with ~" · ", you may suspend this Tamer to reduce ~" · "in their name"
    /// </summary>
    internal static string EvoReductionText32(string t)
    {
        if (!t.Contains("digivolv", StringComparison.OrdinalIgnoreCase)) return t;
        t = Regex.Replace(t, @"\bWhen digivolving (one of your Digimon|this Digimon) into\b", "When $1 would digivolve into", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"(?<=would digivolve into [^,]*?\bcard) in (?:your|the) hand(?= with\b)", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"(?<=would digivolve into [^,]*?)\bin their names?\b", "in its name", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"(?<=would digivolve[^.]*?), you may (?<v>suspend|delete|trash|return|place) (?<obj>[^,]+?) to reduce\b",
            m => $", by {Gerund(m.Groups["v"].Value)} {m.Groups["obj"].Value}, reduce", RegexOptions.IgnoreCase);
        return t;
    }

    /// <summary>자신의 다른 디지몬이 떠나려 할 때의 시점 (원래 규칙과 같은 판단 — 이 디지몬 자신이 떠날 때도 본다)</summary>
    private static (List<EffectTrigger>, Func<EffectContext, bool>?) AllyLeave32(bool deletion, Func<GoldfishEngine.LeaveInfo, bool> ok, Func<EffectContext, CardInstance, bool> who) =>
        (new List<EffectTrigger> { deletion ? EffectTrigger.AllyDigimonWouldBeDeleted : EffectTrigger.AllyDigimonWouldLeave,
                                   deletion ? EffectTrigger.ThisDigimonWouldBeDeleted : EffectTrigger.ThisDigimonWouldLeave },
         c => c.Engine.CurrentLeave is { } l && ok(l) && (ReferenceEquals(l.Digimon, c.Other) || ReferenceEquals(l.Digimon, c.Self)) && who(c, l.Digimon));

    /// <summary>
    /// 떠나려 하는 자신의 디지몬 (쉼표 목록 · 이름 또는 조건): "Digimon with the [Angel], [Archangel] or [Three Great Angels] trait" ·
    /// "[Apocalymon] or Digimon with the [Dark Masters] trait" · "other Digimon with [Knightmon] in its text"
    /// </summary>
    private static Func<EffectContext, CardInstance, bool>? LeaveSubject32(string subj)
    {
        Match m;
        subj = Regex.Replace(subj.Trim(), @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase);
        if ((m = Regex.Match(subj, $@"^(?<x>{TargetSpec.NameList}) or (?<rest>(?:[^,]*? )?Digimon with .+)$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["rest"].Value) is { } restWho)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (c, d) => xs.Any(x => CardNames.Is(d.TopCard, x)) || restWho(c, d);
        }
        if ((m = Regex.Match(subj, $@"^(?<other>other )?(?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); bool other = m.Groups["other"].Success;
            return (c, d) => (!other || !ReferenceEquals(d, c.Self)) && xs.Any(x => TargetSpec.NameHas(d.TopCard, x));
        }
        return ParseAllySubject(subj);
    }

    /// <summary>v0.32.0 비용 (앞의 ParseCost · ParseCost31이 읽지 못하는 것)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost32(string cost, CardData card)
    {
        Match m;
        // "trashing 2 same-level cards from its digivolution cards" · "trashing 2 cards of the same level in this Digimon's digivolution cards"
        //  — 공식 Q&A BT9-012 · BT9-024: 이 카드와 같은 Lv.이 아니라, 진화원 중 서로 Lv.이 같은 카드 2장
        if (Regex.IsMatch(cost, @"^trashing 2 (?:same-level cards|cards of the same level|cards with the same level) (?:from|in) (?:its|this Digimon's) digivolution cards$", RegexOptions.IgnoreCase))
        {
            // 후보: Lv.마다 2장 이상인 묶음 (추천 = 이 효과의 카드를 남기는 묶음 · 낮은 Lv.부터). 묶음 안에서는 이 효과의 카드를 뒤로
            static List<List<CardData>> Pairs(EffectContext c) => c.Self == null ? new List<List<CardData>>()
                : c.Self.SourcesWhere(x => x.Level > 0).GroupBy(x => x.Level).Where(g => g.Count() >= 2)
                    .Select(g => g.OrderBy(x => ReferenceEquals(x, c.Card) ? 1 : 0).Take(2).ToList())
                    .OrderBy(p => p.Any(x => ReferenceEquals(x, c.Card)) ? 1 : 0).ThenBy(p => p[0].Level).ToList();
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && Pairs(c).Count > 0,
                    c =>
                    {
                        var pairs = Pairs(c);
                        var pair = pairs[0];
                        if (pairs.Count > 1)   // 어느 Lv.의 2장을 파기할지 고른다 (각 묶음의 첫 카드로 묻는다)
                        {
                            var pick = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, pairs.Select(p => p[0]).ToList(), 1, upTo: false,
                                $"[{c.Card.Name}] 비용으로 파기할 같은 Lv.의 진화원 2장 — Lv.을 고르세요.").FirstOrDefault();
                            pair = pairs.FirstOrDefault(p => ReferenceEquals(p[0], pick)) ?? pairs[0];
                        }
                        foreach (var x in pair)
                            if (c.Self!.TakeSource(y => ReferenceEquals(y, x)) != null) c.Owner.Trash.Add(x);
                        c.Log($"[{c.Card.Name}] 진화원 [{string.Join(", ", pair.Select(x => x.Name))}] (Lv.{pair[0].Level})을(를) 파기했습니다 (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, "이 디지몬의 진화원에서 Lv.이 같은 카드 2장을 파기하여");
        }
        // "returning 4 [Vemmon] from its digivolution cards to the bottom of the deck" · "placing 4 [Vemmon] from this Digimon's digivolution cards at the bottom of their owners' decks"
        //  · "trashing 3 Digimon cards from its digivolution cards" · "returning 1 Digimon card with the [X Antibody] or [Royal Knight] trait from this Digimon's digivolution cards to the bottom of your deck"
        if ((m = Regex.Match(cost, @"^(?<v>returning|placing|trashing) (?<pick>.+?) (?:from|in) (?:its|this Digimon's) digivolution cards(?<deck> (?:to|at) the bottom of (?:the|your|its owner's|their owners'?) decks?)?$", RegexOptions.IgnoreCase)).Success
            && (m.Groups["v"].Value.Equals("trashing", StringComparison.OrdinalIgnoreCase) != m.Groups["deck"].Success)
            && (ParseNamePick(m.Groups["pick"].Value) ?? ParseCardPick(m.Groups["pick"].Value)) is { } srcPick)
        {
            var (n, match) = srcPick;
            bool toDeck = m.Groups["deck"].Success;
            List<CardData> Able(EffectContext c) => c.Self == null || !c.Owner.BattleArea.Contains(c.Self) ? new List<CardData>() : c.Self.SourcesWhere(match).ToList();
            return (c => Able(c).Count >= n,
                    c =>
                    {
                        var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, Able(c).OrderBy(x => x.Level).ToList(), n, upTo: false,
                            $"[{c.Card.Name}] 비용으로 {(toDeck ? "덱 아래로 되돌릴" : "파기할")} 진화원 {n}장을 고르세요.");
                        foreach (var x in picked)
                        {
                            if (c.Self!.TakeSource(y => ReferenceEquals(y, x)) == null) continue;
                            if (toDeck) c.Owner.MainDeck.Add(x); else c.Owner.Trash.Add(x);
                        }
                        c.Log($"[{c.Card.Name}] 진화원 [{string.Join(", ", picked.Select(x => x.Name))}]을(를) {(toDeck ? "덱 아래로 되돌렸습니다" : "파기했습니다")} (비용)");
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, toDeck ? $"이 디지몬의 진화원 {n}장을 덱 아래로 되돌려" : $"이 디지몬의 진화원 {n}장을 파기하여");
        }
        // "trashing the bottom card of your security stack" · "trashing your bottom security card"
        if (Regex.IsMatch(cost, @"^trashing (?:the bottom card of your security stack|your bottom security card)$", RegexOptions.IgnoreCase))
            return (c => c.Owner.SecurityStack.Count > 0,
                    c => { var x = c.Owner.SecurityStack[^1]; c.Owner.SecurityStack.RemoveAt(c.Owner.SecurityStack.Count - 1); c.Owner.Trash.Add(x); c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: true); },
                    c => c.Owner.SecurityStack.Count > 1, "자신의 시큐리티를 아래에서부터 1장 파기하여");
        // "returning your top security card to the bottom of the deck"
        if (Regex.IsMatch(cost, @"^returning (?:your top security card|the top card of your security stack) to the bottom of (?:the|your) deck$", RegexOptions.IgnoreCase))
            return (c => c.Owner.SecurityStack.Count > 0,
                    c => { var x = c.Owner.SecurityStack[0]; c.Owner.SecurityStack.RemoveAt(0); c.Owner.MainDeck.Add(x); c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: false); },
                    c => c.Owner.SecurityStack.Count > 1, "자신의 시큐리티 맨 위 카드를 덱 아래로 되돌려");
        // "trashing both players' top security cards" — 양쪽 모두 있어야 낼 수 있다
        if (Regex.IsMatch(cost, @"^trashing both players' top security cards?$", RegexOptions.IgnoreCase))
            return (c => c.Owner.SecurityStack.Count > 0 && c.Enemy.SecurityStack.Count > 0,
                    c =>
                    {
                        foreach (var z in new[] { c.Owner, c.Enemy })
                        { var x = z.SecurityStack[0]; z.SecurityStack.RemoveAt(0); z.Trash.Add(x); c.Engine.NotifySecurityRemoved(z, x, trashed: true); }
                    },
                    c => c.Owner.SecurityStack.Count > 1, "서로의 시큐리티를 위에서부터 1장 파기하여");
        return ParseCost33(cost, card) ?? ParseCost34(cost, card) ?? ParseCost35(cost, card) ?? ParseCost36(cost, card);   // v0.35.0 (SentenceParser.Batch35.cs)   // v0.33.0 (SentenceParser.Batch33.cs) · v0.34.0
    }

    /// <summary>v0.32.0 동작 (앞의 ParseAction보다 먼저 본다 — 지금까지 읽지 못하던 표기만)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction32(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        // ---- 막기 (떠나려 할 때 · 소멸할 때): "Prevent it" · "Prevent it from leaving play" · "Prevent its deletion" · "1 of those Digimon doesn't leave" · "prevent 1 of those Digimon from leaving" ----
        if (Regex.IsMatch(s, @"^(?:prevent (?:it|that|its deletion|it from leaving play|(?:1|one) of (?:those|them|the) Digimon from (?:leaving(?: the battle area| play)?|being deleted))|(?:1|one) of (?:those|them|the) Digimon (?:doesn't|does not) leave(?: the battle area)?)$", RegexOptions.IgnoreCase)
            && triggers.Any(t => t is EffectTrigger.ThisDigimonWouldLeave or EffectTrigger.ThisDigimonWouldBeDeleted or EffectTrigger.AllyDigimonWouldLeave or EffectTrigger.AllyDigimonWouldBeDeleted))
            return (c => { c.PreventLeave = true; Emit(c, label, "배틀 에어리어를 떠나지 않는다"); }, null);

        // ---- 강제 어택: "This Digimon attacks" · "Attack with this Digimon" (준 효과 "[Start of Your Main Phase] This Digimon attacks." 등) ----
        if (Regex.IsMatch(s, @"^(?:This Digimon attacks|Attack with this Digimon)$", RegexOptions.IgnoreCase) && card.Type == CardType.Digimon)
            return (c =>
            {
                if (c.Self == null || !c.Engine.ForcedAttack(c.Owner, c.Self, label)) { c.LastStepResolved = false; Emit(c, label, "어택할 수 없습니다"); }
            }, null);

        // ---- 효과를 준다: "1 of your opponent's Digimon gains "[On Deletion] Lose 1 memory."" · "Give 1 of their Digimon "[Start of Your Main Phase] This Digimon attacks." until their turn ends"
        //      · "~ gets -3000 DP and gains "~"" · "Give 1 of your opponent's Digimon ＜Collision＞ and "~"" (기간 앞뒤, 없으면 이 턴 동안) ----
        if (s.Contains('"'))
        {
            var dur = ParseDuration(s, out string core);
            Match g;
            if ((g = Regex.Match(core, "^(?:give (?<t>.+?)(?: (?<kws>＜[^＞]+＞(?:\\s*,?\\s*(?:and\\s*)?＜[^＞]+＞)*) and)? \"(?<q>[^\"]+)\"|(?<t>.+?) (?:(?:gets? (?<sign>[+-])(?<n>\\d+) DP|gains? (?<kws>＜[^＞]+＞(?:\\s*,?\\s*(?:and\\s*)?＜[^＞]+＞)*)) and )?(?:gains?|gets?) \"(?<q>[^\"]+)\")$", RegexOptions.IgnoreCase)).Success)
            {
                var target = TargetSpec.Parse(g.Groups["t"].Value);
                if (target == null || target.AnySide || Regex.IsMatch(g.Groups["t"].Value, @"\bTamers?\b", RegexOptions.IgnoreCase)) return (null, $"미지원 대상 (효과를 준다): {g.Groups["t"].Value}");
                if (GrantedCard32(card, g.Groups["q"].Value) is not { } granted) return (null, $"미지원 준 효과: {g.Groups["q"].Value}");
                List<(Keyword? Keyword, int SecurityAttack)>? kws = null;
                if (g.Groups["kws"].Success && (kws = ParseKeywordList(g.Groups["kws"].Value)) == null) return (null, "미지원 키워드 부여 (효과를 준다)");
                int dp = g.Groups["n"].Success ? int.Parse(g.Groups["n"].Value) * (g.Groups["sign"].Value == "-" ? -1 : 1) : 0;
                bool security = triggers.Contains(EffectTrigger.Security);
                var d2 = dur == Duration.None ? Duration.ThisTurn : dur;
                return (c =>
                {
                    int until = Until(c, d2);
                    var list = Pick(target, c, security);
                    foreach (var d in list)
                    {
                        d.GrantedEffects.Add((granted, until, c.Owner));
                        if (kws != null)
                            foreach (var (k, sa) in kws)
                            {
                                if (k is { } kw) d.TimedKeywords.Add((kw, until));
                                else d.TimedSecurityAttackBuffs.Add((sa, until));
                            }
                        if (dp != 0) c.Engine.ApplyDpChange(d, dp, until, $"{c.Card.Name} [{label}]");
                    }
                    c.LastTargets = list;
                    c.RecalculateFieldBuffs();
                    if (list.Count == 0) c.LastStepResolved = false;
                    Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}]에게 효과 「{granted.EffectText}」를 준다" : "대상이 없습니다");
                }, null);
            }
        }
        return null;
    }

    /// <summary>
    /// 따옴표 안의 효과 문장 → 그 효과만 가진 카드 (엔진이 받은 디지몬의 맨 위 카드 효과처럼 발휘). 시점 효과만 (지속 효과 · 키워드 · 코스트 감소는 아직 아님),
    /// 모든 절을 읽을 수 있을 때만. 같은 문장은 같은 카드 (CardScripts 캐시)
    /// </summary>
    private static CardData? GrantedCard32(CardData card, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        var synth = new CardData
        {
            CardId = $"GRANT:{card.CardId}:{text}", Name = card.Name, Type = CardType.Digimon, Color = card.Color, Level = 3, EffectText = text,
        };
        var info = KeywordDictionary.Analyze(synth);
        if (info.Count == 0 || info.Any(x => x.Support != ClauseSupport.Supported)) return null;
        if (CardScripts.For(synth) is not { } script || script.Effects.Count == 0 || script.Statics.Count > 0 || script.Keywords.Count > 0
            || script.EvoCostModifiers.Count > 0 || script.PlayCostOptions.Count > 0 || script.FieldPlayCostModifiers.Count > 0)
            return null;
        return synth;
    }

    /// <summary>
    /// v0.32.0 두 동작을 한 문장에 쓴 것 (원래 규칙이 읽지 못할 때만): "Suspend 1 of your opponent's Digimon, and 1 of your Digimon gains ＜Piercing＞ for the turn" ·
    /// "Unsuspend this Digimon, and gain 1 memory" · "Suspend 2 of your opponent's Digimon. 2 of their Digimon or Tamers can't unsuspend ~" · "Suspend 1 Digimon, then unsuspend 1 Digimon"
    /// — 앞은 대상 동작, 뒤는 대상 · 주어로 시작하는 동작. 둘 다 읽을 수 있을 때만 (앞 → 뒤 순서로 처리)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? Compound32(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (!Regex.IsMatch(s, @"^(?:Suspend|Unsuspend|Delete|Return|Trash|＜De-Digivolve)\b", RegexOptions.IgnoreCase)) return null;
        foreach (Match sep in Regex.Matches(s, @"(?:,? and then|, then|,? and|\.) "))
        {
            string left = s[..sep.Index].Trim(), right = s[(sep.Index + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            if (!Regex.IsMatch(right, @"^(?:(?:up to )?\d+|all) of |^(?:this Digimon|it|that Digimon|they|those Digimon|this Tamer) |^(?:gain|suspend|unsuspend|delete|return|trash|＜Draw)\b", RegexOptions.IgnoreCase)) continue;
            var a = ParseAction(left, card, triggers);
            if (a.Error != null) continue;
            string r = char.ToUpperInvariant(right[0]) + right[1..];
            var b = ParseAction(r, card, triggers);
            Action<EffectContext>? actB = b.Error == null ? b.Act : null;
            if (actB == null && ParseStep(r, card, triggers) is { Error: null, Step: { } stepB }) actB = c => RunStep(stepB, c);
            if (actB == null) continue;
            var actA = a.Act!;
            return (c => { actA(c); actB(c); }, null);
        }
        return null;
    }

    /// <summary>v0.32.0 셈 (ParseCounter가 읽지 못하는 것)</summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter32(string counter)
    {
        Match m;
        // "2 suspended Digimon you have in play" (BT10-057)
        if ((m = Regex.Match(counter, @"^(?<n>\d+) (?<other>other )?(?<sus>suspended )?Digimon you have(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            int per = Math.Max(1, int.Parse(m.Groups["n"].Value)); bool other = m.Groups["other"].Success, sus = m.Groups["sus"].Success;
            return (o, _, self) => o.BattleArea.Count(d => (!other || !ReferenceEquals(d, self)) && (!sus || d.IsSuspended)) / per;
        }
        // "Digimon with no digivolution cards your opponent has in play" (BT6-029)
        if (Regex.IsMatch(counter, @"^Digimon with no digivolution cards your opponent has in play$", RegexOptions.IgnoreCase))
            return (_, e, _) => e.BattleArea.Count(d => d.EvolutionSources.Count == 0);
        // "other Digimon with [Sukamon] in its name in play" (BT11-043 — 어느 쪽이든)
        if ((m = Regex.Match(counter, $@"^(?<other>other )?Digimon with (?<x>{TargetSpec.NameList}) in (?:its|their) names? in play$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); bool other = m.Groups["other"].Success;
            return (o, e, self) => o.BattleArea.Concat(e.BattleArea).Count(d => (!other || !ReferenceEquals(d, self)) && xs.Any(x => TargetSpec.NameHas(d.TopCard, x)));
        }
        return null;
    }

    /// <summary>
    /// v0.32.0 지속 효과 (ParseStatic 앞에서 본다 — 지금까지 읽지 못하던 표기만): "this Digimon gets +2000 DP and gains ＜Security A. +1＞ for every 2 ~" ·
    /// "this Digimon gains ＜Security A. +1＞ and +2000 DP for every 2 colors ~" (BT10-057 · BT18-085). 반환: null = 해당 없음, "" = 읽음, 그 밖 = 오류
    /// </summary>
    private static string? ParseStatic32(string s, bool inherited, CardData card, Pending pending, Func<StaticContext, bool> cond)
    {
        Match m;
        if ((m = Regex.Match(s, @"^(?:this Digimon|it) (?:gets? (?<dp>[+-]\d+ DP) and gains? (?<sa>＜Security A(?:ttack)?\.? [+-]\d+\s*＞)|gains? (?<sa>＜Security A(?:ttack)?\.? [+-]\d+\s*＞) and (?:gets? )?(?<dp>[+-]\d+ DP)) for (?:every|each) (?<cnt>.+)$", RegexOptions.IgnoreCase)).Success
            && ParseCounter(m.Groups["cnt"].Value) != null)
        {
            string? e1 = ParseStatic($"This Digimon gets {m.Groups["dp"].Value} for each {m.Groups["cnt"].Value}", inherited, card, Qualifier.None, pending, cond);
            if (e1 != null) return e1;
            return ParseStatic($"This Digimon gains {m.Groups["sa"].Value} for each {m.Groups["cnt"].Value}", inherited, card, Qualifier.None, pending, cond) ?? "";
        }
        return null;
    }

    /// <summary>
    /// v0.32.0 다른 카드의 등장 코스트 감소 + 두 번째 문장 ([육성] — BT13-007 · EX6-006):
    /// "~ you may reduce the play cost by 4. Further reduce it by 1 for each of this Digimon's digivolution cards." — 공식 한글: "이 디지몬의 진화원 1장마다 이 코스트 경감 효과의 수치 -1" (줄이는 양이 준다)
    /// "~ you may reduce the play cost by 3. If this Digimon has 5 or more cards with different names in its digivolution cards, you may reduce the play cost by 4 instead."
    /// 반환: null = 해당 없음, "" = 읽음, 그 밖 = 오류
    /// </summary>
    private static string? FieldPlayCost32(string text, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var two = Regex.Match(text, @"^(?<first>.+?reduce (?:the|its|their) (?:play |memory )?costs? by (?<n>\d+))\.\s+(?:Further reduce it by (?<k>\d+) for (?:each|every) (?<cnt>[^.]+?)|If (?<cond>[^,]+), you may reduce (?:the|its|their) (?:play |memory )?costs? by (?<m>\d+) instead)\.?$", RegexOptions.IgnoreCase);
        if (!two.Success || FieldPlayCostClause(two.Groups["first"].Value + ".") is not { } fpc) return null;
        string labels = fpc.Groups["labels"].Value;
        bool breeding = labels.Contains("[Breeding]", StringComparison.OrdinalIgnoreCase);
        if (breeding && card.Type is not (CardType.Digimon or CardType.Digitama)) return "미지원 시점 [Breeding]: 디지몬 · 디지타마가 아닌 카드";   // v0.32.0 디지타마(육성 에어리어)도
        if (fpc.Groups["cost"].Success || fpc.Groups["cnt"].Success) return "미지원 등장 코스트 감소 (두 문장 + 비용 · 셈)";
        if (FieldPlayCostTarget(fpc.Groups["p"].Value) is not { } which) return $"미지원 등장 코스트 감소 카드: {fpc.Groups["p"].Value}";
        int n = int.Parse(fpc.Groups["n"].Value);
        Func<EffectContext, int> amount;
        if (two.Groups["cnt"].Success)
        {
            if (ParseCounter(two.Groups["cnt"].Value) is not { } counter) return $"미지원 등장 코스트 감소 셈: for each {two.Groups["cnt"].Value}";
            int k = int.Parse(two.Groups["k"].Value);
            amount = c => n - k * Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
        }
        else
        {
            var cm = Regex.Match(two.Groups["cond"].Value, @"^this Digimon has (?<c>\d+) or more cards with different names in its digivolution cards$", RegexOptions.IgnoreCase);
            if (!cm.Success) return $"미지원 조건 (등장 코스트 감소: {two.Groups["cond"].Value})";
            int need = int.Parse(cm.Groups["c"].Value), alt = int.Parse(two.Groups["m"].Value);
            amount = c => c.Self != null && c.Self.FaceUpSources.Select(CardNames.MatchName).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= need ? alt : n;
        }
        def?.FieldPlayCostModifier(new FieldPlayCostModifier(inherited ? EffectSource.Inherited : EffectSource.Main, which, n, null,
            Optional: fpc.Groups["may"].Success,
            OncePerTurn: labels.Contains("[Once Per Turn]", StringComparison.OrdinalIgnoreCase),
            YourTurnOnly: labels.Contains("[Your Turn]", StringComparison.OrdinalIgnoreCase),
            Breeding: breeding) { Amount = amount });
        return "";
    }

    /// <summary>v0.32.0 자신의 필드 조건 (ParseZoneCondition이 읽지 못하는 것) — 코스트 감소 등의 "if you have ~"</summary>
    private static Func<PlayerZones, bool>? ParseZoneCondition32(string cond)
    {
        Match m;
        // "you have a Digimon with [X Antibody] in its digivolution cards (in play)" (BT9-095 · BT9-097 · BT11-107)
        if ((m = Regex.Match(cond, $@"^you have (?:a|an|1) Digimon with (?<x>{TargetSpec.NameList}) in (?:its|their) digivolution cards(?: in play| on the field)?$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return z => z.BattleArea.Any(d => d.SourcesAny(s => xs.Any(x => TargetSpec.NameHas(s, x))));
        }
        // "you have 2 or more suspended green Digimon in play" (BT10-103)
        if ((m = Regex.Match(cond, $@"^you have (?<n>\d+) or more (?<sus>suspended )?(?:(?<color>{Colors}) )?Digimon(?: in play| on the field)?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool sus = m.Groups["sus"].Success;
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            return z => z.BattleArea.Count(d => (!sus || d.IsSuspended) && (color == null || d.TopCard.AllColors.Contains(color.Value))) >= n;
        }
        // "you have a Digimon with [Huckmon], [Jesmon] or [Sistermon] in its name" (BT20-057 — 이름 목록)
        if ((m = Regex.Match(cond, $@"^you have (?:a|an) (?<kind>Digimon|Tamer) with (?<x>{TargetSpec.NameList}) in (?:its|their) names?(?: on the field| in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); bool tamer = m.Groups["kind"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase);
            return z => tamer ? z.Tamers.Any(t => xs.Any(x => TargetSpec.NameHas(t, x))) : z.BattleArea.Any(d => xs.Any(x => TargetSpec.NameHas(d.TopCard, x)));
        }
        // "you have [Yuuko Kamishiro] or a [CS] trait Digimon" (BT23-044)
        if ((m = Regex.Match(cond, $@"^you have (?<x>{TargetSpec.NameList}) or (?:a|an) (?<t>{TargetSpec.NameList}) trait Digimon$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); var ts = TargetSpec.Names(m.Groups["t"].Value);
            return z => z.Tamers.Any(t => xs.Any(x => CardNames.Is(t, x))) || z.BattleArea.Any(d => xs.Any(x => CardNames.Is(d.TopCard, x)) || ts.Any(t => TargetSpec.HasTrait(d.TopCard, t)));
        }
        return null;
    }

    /// <summary>v0.32.0 자신 · 상대 필드 조건 (ParseSideCondition이 읽지 못하는 것)</summary>
    private static Func<PlayerZones, PlayerZones, bool>? ParseSideCondition32(string cond)
    {
        Match m;
        // "your opponent has 3 or fewer security cards" (BT21-093)
        if ((m = Regex.Match(cond, @"^(?:your opponent|they) (?:has|have) (?<n>\d+) or (?<dir>more|fewer|less) security cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase); return (_, e) => more ? e.SecurityStack.Count >= n : e.SecurityStack.Count <= n; }
        // "your opponent has 10 or more cards in either their hand or trash" (ST22-14)
        if ((m = Regex.Match(cond, @"^(?:your opponent|they) (?:has|have) (?<n>\d+) or more cards in either their hand or (?:their )?trash$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (_, e) => e.Hand.Count >= n || e.Trash.Count >= n; }
        return null;
    }

    /// <summary>v0.32.0 공개한 카드 x로 진화할 수 있는 자신의 디지몬 (이 디지몬 또는 자신의 디지몬 전부 — 진화 조건 · 진화 불가 확인)</summary>
    private static List<CardInstance> RevealEvoTargets32(EffectContext c, bool anyOwn, CardData x) =>
        (anyOwn ? c.Owner.BattleArea.ToList() : c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new List<CardInstance> { c.Self } : new List<CardInstance>())
        .Where(d => !EffectRules.IsDigivolveLocked(c.Engine.State, d) && ActionValidator.ValidateEvolution(d, x).CanExecute).ToList();
}
