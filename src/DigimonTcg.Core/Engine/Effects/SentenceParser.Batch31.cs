using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.31.0 미지원 문장 5차 묶음 — "When ~," 사건 표기 보강: 테이머(레스트 · 액티브 · 소멸 · 아래 카드 파기) · 링크 카드 파기 ·
/// 패 · 덱 파기의 카드 조건 · "you attack with ~" · 상대 디지몬의 진화 · 효과로 진화 · 양쪽 디지몬의 등장 · 소멸 · "A or B" 사건.
/// 앞의 규칙(ParseEventPhrase · ParseEventPhraseMore · ParseSourceEvent)에 걸리지 않은 것만 본다 (기존 판정은 그대로)
/// </summary>
internal static partial class SentenceParser
{
    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase31(string ev)
    {
        Match m;
        (List<EffectTrigger>, Func<EffectContext, bool>?) One(EffectTrigger t, Func<EffectContext, bool>? cond = null) => (new List<EffectTrigger> { t }, cond);
        (List<EffectTrigger>, Func<EffectContext, bool>?) Many(Func<EffectContext, bool>? cond, params EffectTrigger[] ts) => (ts.ToList(), cond);
        List<CardColor> ColorList(string text) => Regex.Matches(text, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();

        // ---- 테이머 ----
        // "When this Tamer becomes suspended" · "When this Tamer suspends" (이 테이머 자신)
        if (Regex.IsMatch(ev, $@"^this Tamer {SuspendVerb}$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyTamerSuspended, c => ReferenceEquals(c.TriggerCard, c.Card));
        // "When this Tamer becomes unsuspended" · "When this Tamer unsuspends" (액티브 페이즈 · 효과)
        if (Regex.IsMatch(ev, $@"^this Tamer {UnsuspendVerb}$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyTamerUnsuspended, c => ReferenceEquals(c.TriggerCard, c.Card));
        // "When one of your Tamers is deleted" · "When one of your red or yellow Tamers is deleted" · "When any of your yellow or red Tamers are deleted"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers? (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success)
        {
            var colors = ColorList(m.Groups["color"].Value);
            return One(EffectTrigger.AllyTamerDeleted, c => c.TriggerCard != null && (colors.Count == 0 || c.TriggerCard.AllColors.Any(colors.Contains)));
        }
        // "When effects trash cards from under your Tamers" · "~ from under this Tamer" (테이머 아래 카드 — 엔진은 테이머 아래 카드를 한 묶음으로 둔다)
        if (Regex.IsMatch(ev, @"^(?:an effect trashes|effects trash) (?:a card|cards) from under (?:your Tamers|this Tamer|(?:one|any) of your Tamers)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.UnderTamersTrashed);

        // ---- 링크 카드 ----
        // "When effects trash any of this Digimon's link cards" · "When effects trash any of your Digimon's link cards"
        if ((m = Regex.Match(ev, @"^(?:an effect trashes|effects trash) (?:any of |one of )?(?:(?<self>this Digimon's)|your Digimon's) link cards?$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["self"].Success
                ? One(EffectTrigger.AllyLinkCardsTrashed, c => c.Self != null && ReferenceEquals(c.Other, c.Self))
                : One(EffectTrigger.AllyLinkCardsTrashed);
        // "When any of your Digimon get linked to a [Social], [Creation], [Navi] or [Tool] trait card"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your Digimon (?:gets?|are|is) linked (?:to|with) (?:an? |1 )?(?<x>{TargetSpec.NameList}) trait card$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return One(EffectTrigger.AllyDigimonLinked, c => c.TriggerCard != null && xs.Any(x => TargetSpec.HasTrait(c.TriggerCard, x)));
        }

        // ---- 패 · 덱 ----
        // "When an effect trashes a card in your hand" · "When a card is trashed from your hand (by your effect)" · "When cards are trashed from your hand"
        if ((m = Regex.Match(ev, @"^(?:an effect trashes (?:a card|cards) in your hand|(?:a card is|cards are) trashed from your hand(?<mine> by (?:your|one of your) effects?)?)$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["mine"].Success
                ? One(EffectTrigger.AllyHandTrashed, c => ReferenceEquals(c.Engine.HandTrashedBy, c.Owner))
                : One(EffectTrigger.AllyHandTrashed);
        // "When hands are trashed from" (어느 쪽이든) · "When cards are trashed from your opponent's hand"
        if (Regex.IsMatch(ev, @"^hands are trashed from$|^(?:a player's|either player's|any player's) hand is trashed from$", RegexOptions.IgnoreCase))
            return Many(null, EffectTrigger.AllyHandTrashed, EffectTrigger.OpponentHandTrashed);
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your opponent's hand$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentHandTrashed);
        // 버린 카드 조건: "When a Digimon card with the [Dark Animal] or [SoC] trait is trashed from your hand" · "When a card with [Jellymon] in its text is trashed from your hand"
        //  · "When level 4 or higher Digimon cards with the [Demon] or [Titan] trait are trashed from your hand"
        if ((m = Regex.Match(ev, @"^(?:an? )?(?<pick>[^,]+?cards?(?: with [^,]+?)?) (?:is|are) trashed from your hand$", RegexOptions.IgnoreCase)).Success
            && !Regex.IsMatch(m.Groups["pick"].Value, @"^(?:a )?cards?$", RegexOptions.IgnoreCase)
            && ParseCardPick("1 " + Regex.Replace(Regex.Replace(m.Groups["pick"].Value, @"\bcards\b", "card", RegexOptions.IgnoreCase), @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase)) is { } handPick)
        {
            var match = handPick.Match;
            return One(EffectTrigger.AllyHandTrashed, c => c.Engine.CurrentHandTrashed.Any(match));
        }
        // "When you trash this card in your hand using one of your effects" · "When one of your effects trashes this card in your hand" (공식 Q&A BT10-080 — 이 카드 자신)
        if (Regex.IsMatch(ev, @"^you trash this card in your hand (?:using|with|by) (?:one of your|your) effects?$|^one of your effects trashes this card in your hand$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.ThisTrashedFromHand, c => ReferenceEquals(c.Engine.ThisTrashedByOwner, c.Owner));
        // "When your opponent's deck is trashed from" · "When a card in your opponent's deck is trashed" · "When effects trash cards from your deck"
        if (Regex.IsMatch(ev, @"^your opponent's deck is trashed from$|^(?:a card|cards) in your opponent's deck (?:is|are) trashed$|^(?:a card is|cards are) trashed from your opponent's deck$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDeckTrashed);
        if (Regex.IsMatch(ev, @"^(?:an effect trashes|effects trash(?:es)?) (?:a card|cards) from your deck$|^your deck is trashed from$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDeckTrashed);
        // "When effects add (cards) to your opponent's hand" · "When an effect adds cards to your or your opponent's hand"
        if (Regex.IsMatch(ev, @"^(?:an effect adds|effects add) (?:(?:a card|cards) )?to your opponent's hand$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentHandAddedByEffect);
        if (Regex.IsMatch(ev, @"^(?:an effect adds|effects add) (?:(?:a card|cards) )?to (?:your or your opponent's|either player's|a player's) hand$", RegexOptions.IgnoreCase))
            return Many(null, EffectTrigger.AllyHandAddedByEffect, EffectTrigger.OpponentHandAddedByEffect);

        // ---- 어택 ----
        // "When you attack with a Digimon" · "~ with a black Digimon" · "~ with a level 5 green Digimon" · "~ with a Digimon with [Gargomon] or [Rapidmon] in its name"
        //  · "~ with a Digimon that has [Angoramon] in its name or is level 5 or higher" · "~ with a Digimon with [Jellymon] in its name or is level 5 or higher"
        if ((m = Regex.Match(ev, @"^you attack with (?<subj>(?:an?|1) [^,]*?Digimon(?: (?:with|that has) [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
            && AttackerSubject(m.Groups["subj"].Value) is { } attacker)
            return One(EffectTrigger.AllyDigimonAttacks, c => c.Other != null && attacker(c, c.Other));
        // "When one of your Digimon with [Gammamon] in its digivolution cards attacks" · "When one of your Digimon with 2 or more colors attacks"
        //  · "When one of your 2-color blue and green Digimon attacks" · "When any of your Digimon with [Aqua] or [Sea Animal] in any of its traits attack"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?!opponent)[^,]*?Digimon(?: with [^,]+?)?) attacks?$", RegexOptions.IgnoreCase)).Success
            && AttackerSubject(m.Groups["subj"].Value) is { } allyAttacker)
            return One(EffectTrigger.AllyDigimonAttacks, c => c.Other != null && allyAttacker(c, c.Other));
        // "When Digimon attack" · "When Digimon attack players" (양쪽)
        if ((m = Regex.Match(ev, @"^(?:any )?Digimon attack(?<p> (?:players|a player))?$", RegexOptions.IgnoreCase)).Success)
        {
            bool player = m.Groups["p"].Success;
            return Many(c => c.Other != null && (!player || c.TriggerCard == null), EffectTrigger.AllyDigimonAttacks, EffectTrigger.OpponentDigimonAttacks);
        }
        // "When one of your Digimon attacks your opponent's Digimon" (상대 디지몬을 어택)
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon attacks? (?:your opponent's|an opponent's|a) Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDigimonAttacks, c => c.Other != null && c.TriggerCard != null);
        // "When one of your opponent's Digimon attacks one of your blue Digimon"
        if ((m = Regex.Match(ev, @"^(?:one of your|an|any of your) opponent's Digimon attacks (?:one of |any of )?your (?<spec>[^,]*?)Digimon$", RegexOptions.IgnoreCase)).Success
            && (m.Groups["spec"].Value.Trim().Length == 0 ? (Func<CardData, bool>)(_ => true) : EventCardSubject(m.Groups["spec"].Value + "Digimon")) is { } defended)
            return One(EffectTrigger.OpponentDigimonAttacks, c => c.TriggerCard != null && c.TriggerCard.Type != CardType.Tamer && defended(c.TriggerCard));
        // "When your opponent's Digimon with the highest DP attacks"
        if (Regex.IsMatch(ev, @"^(?:your|an) opponent's Digimon with the highest DP attacks$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonAttacks, c => c.Other != null && c.Enemy.BattleArea.Count > 0 && c.Other.GetCurrentDP() >= c.Enemy.BattleArea.Max(d => d.GetCurrentDP()));

        // ---- 진화 (상대 · 효과) ----
        // "When an opponent's Digimon digivolves" · "When any of your opponent's Digimon digivolve into a level 6 or higher Digimon"
        if ((m = Regex.Match(ev, @"^(?:an|one of your|any of your) opponent's Digimon digivolves?(?: into (?:an? )?(?<into>[^,]+))?$", RegexOptions.IgnoreCase)).Success)
        {
            Func<CardData, bool>? into = null;
            if (m.Groups["into"].Success && (into = EventCardSubject(m.Groups["into"].Value)) == null) return null;
            return One(EffectTrigger.OpponentDigimonDigivolved, c => c.Other != null && (into == null || into(c.Other.TopCard)));
        }
        // "When an opponent's Digimon is played or digivolves" · "When any of your opponent's Digimon are played or digivolve" · "~ are played or deleted"
        if ((m = Regex.Match(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:is|are) played or (?<w>digivolves?|deleted)$", RegexOptions.IgnoreCase)).Success)
            return Many(c => c.Other != null, EffectTrigger.OpponentDigimonPlayed,
                m.Groups["w"].Value.StartsWith("digivolv", StringComparison.OrdinalIgnoreCase) ? EffectTrigger.OpponentDigimonDigivolved : EffectTrigger.OpponentDigimonDeleted);
        // "When effects digivolve your opponent's Digimon" · "When an effect plays or digivolves an opponent's Digimon" · "When effects play or digivolve any Digimon"
        if (Regex.IsMatch(ev, @"^(?:an effect digivolves|effects digivolve) (?:your|an|one of your) opponent's Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonDigivolved, c => c.Engine.DigivolvingByEffect);
        if (Regex.IsMatch(ev, @"^(?:an effect plays or digivolves|effects play or digivolve) (?:your|an|one of your) opponent's Digimon$", RegexOptions.IgnoreCase))
            return Many(c => c.Trigger == EffectTrigger.OpponentDigimonPlayed ? c.Engine.PlayingByEffect : c.Engine.DigivolvingByEffect,
                EffectTrigger.OpponentDigimonPlayed, EffectTrigger.OpponentDigimonDigivolved);
        if (Regex.IsMatch(ev, @"^(?:an effect plays or digivolves|effects play or digivolve) (?:any|a) Digimon$", RegexOptions.IgnoreCase))
            return Many(c => c.Trigger is EffectTrigger.AllyDigimonPlayed or EffectTrigger.OpponentDigimonPlayed ? c.Engine.PlayingByEffect : c.Engine.DigivolvingByEffect,
                EffectTrigger.AllyDigimonPlayed, EffectTrigger.OpponentDigimonPlayed, EffectTrigger.AllyDigimonDigivolved, EffectTrigger.OpponentDigimonDigivolved);

        // ---- 등장 (양쪽 · 상대 · 효과) ----
        // "When any Digimon are played or digivolve" · "When Digimon are played (or digivolve)" · "When other Digimon are played or digivolve"
        if ((m = Regex.Match(ev, @"^(?<other>other |another )?(?:any )?Digimon (?:is |are )?played(?<evo> or digivolves?)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool other = m.Groups["other"].Success;
            var ts = new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.OpponentDigimonPlayed };
            if (m.Groups["evo"].Success) { ts.Add(EffectTrigger.AllyDigimonDigivolved); ts.Add(EffectTrigger.OpponentDigimonDigivolved); }
            return (ts, c => c.Other != null && (!other || !ReferenceEquals(c.Other, c.Self)));
        }
        // "When an opponent plays a Digimon" · "When your opponent plays a Digimon card" · "When an opponent plays a Digimon or Tamer"
        if ((m = Regex.Match(ev, @"^(?:an|your) opponent plays (?:a|an|1) Digimon(?: card)?(?<tamer> or Tamer)?$", RegexOptions.IgnoreCase)).Success)
            return m.Groups["tamer"].Success ? Many(null, EffectTrigger.OpponentDigimonPlayed, EffectTrigger.OpponentTamerPlayed) : One(EffectTrigger.OpponentDigimonPlayed);
        // "When any of your opponent's Digimon are played" (색 · 조건 없이)
        if (Regex.IsMatch(ev, @"^any of your opponent's Digimon (?:is|are) played$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonPlayed);
        // "When an effect plays another Digimon" (양쪽) · "When one of your effects plays another Digimon" (자신의 효과)
        if ((m = Regex.Match(ev, @"^(?<mine>one of your effects|an effect) plays another Digimon$", RegexOptions.IgnoreCase)).Success)
        {
            bool mine = m.Groups["mine"].Value.StartsWith("one of your", StringComparison.OrdinalIgnoreCase);
            Func<EffectContext, bool> ok = c => c.Engine.PlayingByEffect && c.Other != null && !ReferenceEquals(c.Other, c.Self);
            // 자신의 효과가 등장시키는 것은 자신의 디지몬 (효과로 상대 디지몬을 등장시키는 문장은 따로 없다)
            return mine ? One(EffectTrigger.AllyDigimonPlayed, ok) : Many(ok, EffectTrigger.AllyDigimonPlayed, EffectTrigger.OpponentDigimonPlayed);
        }
        // "When a card with the [X] trait is played" · "When you play a card with the [Royal Knight] or [CS] trait" (디지몬 · 테이머)
        if ((m = Regex.Match(ev, @"^(?:you play (?:a|an|1) (?<s>card[^,]*)|(?:a|an) (?<s>card[^,]*?) is played)$", RegexOptions.IgnoreCase)).Success
            && EventCardSubject(m.Groups["s"].Value) is { } cardPlayed)
        {
            bool mineOnly = ev.StartsWith("you play", StringComparison.OrdinalIgnoreCase);
            var ts = mineOnly ? new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.AllyTamerPlayed }
                              : new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.AllyTamerPlayed, EffectTrigger.OpponentDigimonPlayed, EffectTrigger.OpponentTamerPlayed };
            return (ts, c => (c.Other?.TopCard ?? c.TriggerCard) is { } played && cardPlayed(played));
        }

        // ---- 소멸 (양쪽 · 원인) ----
        // "When any Digimon are deleted" · "When other Digimon or Tamers are deleted" · "When any of your other Digimon or Tamers are deleted"
        if ((m = Regex.Match(ev, @"^(?<other>other |another )?(?:any )?Digimon(?<tam> or Tamers?)? (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success)
        {
            bool other = m.Groups["other"].Success;
            var ts = new List<EffectTrigger> { EffectTrigger.AllyDigimonDeleted, EffectTrigger.OpponentDigimonDeleted };
            if (m.Groups["tam"].Success) { ts.Add(EffectTrigger.AllyTamerDeleted); ts.Add(EffectTrigger.OpponentTamerDeleted); }
            return (ts, c => c.Other != null ? !other || !ReferenceEquals(c.Other, c.Self) : c.TriggerCard != null && !ReferenceEquals(c.TriggerCard, c.Card));
        }
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your other Digimon or Tamers (?:is|are) deleted$", RegexOptions.IgnoreCase))
            return Many(c => c.Other != null ? !ReferenceEquals(c.Other, c.Self) : c.TriggerCard != null && !ReferenceEquals(c.TriggerCard, c.Card),
                EffectTrigger.AllyDigimonDeleted, EffectTrigger.AllyTamerDeleted);
        // "When an opponent's Digimon is deleted in battle" · "~ by having 0 DP" · "~ in battle or by having 0 DP" · "When your opponent's Digimon are deleted in battle"
        if ((m = Regex.Match(ev, @"^(?:an|your|one of your|any of your) opponent's Digimon (?:is|are) deleted (?<how>in battle(?: or by having 0 DP)?|by having 0 DP)$", RegexOptions.IgnoreCase)).Success)
        {
            string how = m.Groups["how"].Value.ToLowerInvariant();
            bool battle = how.Contains("battle"), zero = how.Contains("0 dp");
            return One(EffectTrigger.OpponentDigimonDeleted, c => (battle && c.Engine.CurrentDeletionCause == GoldfishEngine.DeletionCause.Battle)
                                                                 || (zero && c.Engine.CurrentDeletionCause == GoldfishEngine.DeletionCause.ZeroDp));
        }
        // "When an effect deletes another Digimon" · "When an effect deletes an opponent's Digimon" · "When an effect deletes one of your other Digimon (or Tamers)"
        if ((m = Regex.Match(ev, @"^an effect deletes (?:(?<another>another Digimon)|(?<opp>an opponent's Digimon|one of your opponent's Digimon)|one of your other Digimon(?<tam> or Tamers)?)$", RegexOptions.IgnoreCase)).Success)
        {
            Func<EffectContext, bool> byEffect = c => c.Engine.CurrentDeletionCause == GoldfishEngine.DeletionCause.Effect;
            if (m.Groups["opp"].Success) return One(EffectTrigger.OpponentDigimonDeleted, byEffect);
            if (m.Groups["another"].Success) return Many(c => byEffect(c) && c.Other != null && !ReferenceEquals(c.Other, c.Self), EffectTrigger.AllyDigimonDeleted, EffectTrigger.OpponentDigimonDeleted);
            if (m.Groups["tam"].Success) return null;   // 효과로 테이머가 소멸한 원인은 아직 구분하지 않는다
            return One(EffectTrigger.AllyDigimonDeleted, c => byEffect(c) && c.Other != null && !ReferenceEquals(c.Other, c.Self));
        }
        // "When another Digimon with [Angemon] or [Angewomon] in its name is deleted" · "When any other Digimon with [Greymon] in their names are deleted" (양쪽)
        if ((m = Regex.Match(ev, @"^(?:another|any other) (?<spec>[^,]*?Digimon(?: with [^,]+?)?) (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your " + Regex.Replace(m.Groups["spec"].Value, @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase)) is { Opponent: false, AnySide: false } otherDel)
            return Many(c => c.Other != null && !ReferenceEquals(c.Other, c.Self) && otherDel.Filters.All(f => f(c.Other)), EffectTrigger.AllyDigimonDeleted, EffectTrigger.OpponentDigimonDeleted);
        // "When one of your other Digimon with the same name as this Digimon is deleted"
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your other Digimon with the same name as this Digimon (?:is|are) deleted$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDigimonDeleted, c => c.Other != null && c.Self != null && !ReferenceEquals(c.Other, c.Self) && CardNames.Same(c.Self.TopCard, c.Other.TopCard));
        // "When one of your level 6 or higher Digimon with digivolution cards is deleted"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<spec>[^,]*?Digimon) with digivolution cards (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["spec"].Value) is { } withSources)
            return One(EffectTrigger.AllyDigimonDeleted, c => c.Other != null && c.Other.EvolutionSources.Count > 0 && withSources(c, c.Other));

        // ---- 옵션 · 시큐리티 ----
        // "When an Option card is used" (어느 쪽이든)
        if (Regex.IsMatch(ev, @"^(?:an|any) Option cards? (?:is|are) used$", RegexOptions.IgnoreCase))
            return Many(null, EffectTrigger.AllyOptionUsed, EffectTrigger.OpponentOptionUsed);
        // "When your security stack is added to" · "When your opponent's security is removed from" · "When your security is reduced"
        if (Regex.IsMatch(ev, @"^your security stack is added to$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllySecurityAdded);
        if (Regex.IsMatch(ev, @"^your opponent's security (?:stack )?is removed from$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentSecurityRemoved);

        // ---- "A or B" 사건 (각각 읽을 수 있을 때만) ----
        // "When attack targets change or effects trash cards from under your Tamers" · "When this Digimon gets linked or unsuspends"
        //  · "When your opponent's hand is trashed from or effects trash cards from under this Tamer" · "When you use Option cards or your security stack is removed from"
        foreach (Match or in Regex.Matches(ev, @"\s+or\s+"))
        {
            string left = ev[..or.Index].Trim(), right = ev[(or.Index + or.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            var a = ParseEventPhrase(left);
            if (a == null) continue;
            var b = ParseEventPhrase(right);
            // 주어를 함께 쓰는 "this Digimon gets linked or unsuspends" — 뒤에 앞의 주어를 붙인다
            if (b == null && Regex.Match(left, @"^(?<subj>this Digimon|this Tamer)\b", RegexOptions.IgnoreCase) is { Success: true } subj)
                b = ParseEventPhrase(subj.Groups["subj"].Value + " " + right);
            if (b == null) continue;
            var (ta, ca) = a.Value;
            var (tb, cb) = b.Value;
            var all = ta.Concat(tb).Distinct().ToList();
            return (all, c => (ta.Contains(c.Trigger) && (ca == null || ca(c))) || (tb.Contains(c.Trigger) && (cb == null || cb(c))));
        }
        return null;
    }

    /// <summary>[트래시] 시점 효과를 읽는 중 ("this card" = 트래시의 이 카드) — TryParse가 정한다</summary>
    [ThreadStatic] private static bool _trashZoneParse;

    /// <summary>v0.31.0 비용 (앞의 ParseCost가 읽지 못하는 것)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost31(string cost, CardData card)
    {
        // [트래시] "by returning this card to the bottom of the deck" — 트래시의 이 카드를 덱 맨 아래로
        if (_trashZoneParse && Regex.IsMatch(cost, @"^returning this card to the bottom of (?:the|your|its owner's) deck$", RegexOptions.IgnoreCase))
            return (c => c.Owner.Trash.Contains(c.Card),
                    c => { if (c.Owner.Trash.Remove(c.Card)) c.Owner.MainDeck.Add(c.Card); },
                    _ => true, "트래시의 이 카드를 덱 아래로 되돌려");
        return null;
    }

    /// <summary>v0.31.0 동작 (앞의 ParseAction보다 먼저 본다 — 지금까지 읽지 못하던 표기만)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction31(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        Match m;
        // ---- 합계로 고르기: "Choose any number of your opponent's Digimon whose total DP adds up to 6000 or less and delete them" ·
        //      "~ whose play costs add up to 6 or less and return them to the bottom of their owner's deck" · "Delete up to 10000 DP total worth of your opponent's Digimon" ·
        //      "Delete up to 7 play cost's total worth of your opponent's Digimon" · "delete up to 4 levels' total worth of your opponent's Digimon" ·
        //      "Delete any number of your opponent's Digimon whose total DP adds up to equal or less than this Digimon's DP"
        if ((m = Regex.Match(s, @"^(?:you may )?(?:choose|delete) any number of (?:your opponent's|their) Digimon whose (?:total )?(?<k>DP|play costs?|levels?) adds? up to (?:(?<n>\d+) or (?:less|lower)|(?<self>equal (?:to )?or less than this Digimon's DP))(?: and (?<v>delete|return) them(?<bottom> to the bottom of (?:their owners?'s?|the|its owner's) decks?)?)?$", RegexOptions.IgnoreCase)).Success
            || (m = Regex.Match(s, @"^(?:you may )?(?<v>delete|return) up to (?<n>\d+) (?<k>DP|play cost's|play costs'|levels') total worth of (?:your opponent's|their) Digimon(?<bottom> to the bottom of (?:their owners?'s?|the|its owner's) decks?)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool choosePhrase = s.StartsWith("choose", StringComparison.OrdinalIgnoreCase) || s.StartsWith("you may choose", StringComparison.OrdinalIgnoreCase);
            string verb = m.Groups["v"].Success ? m.Groups["v"].Value.ToLowerInvariant() : choosePhrase ? "" : "delete";
            if (verb.Length == 0) return (null, "미지원: 합계로 고른 뒤의 동작");
            bool toBottom = verb == "return";
            if (toBottom && !m.Groups["bottom"].Success) return (null, "미지원: 합계로 고른 디지몬을 되돌리는 곳");
            string k = m.Groups["k"].Value.ToLowerInvariant();
            Func<CardInstance, int> value = k.StartsWith("dp") ? d => d.GetCurrentDP() : k.StartsWith("play") ? d => d.TopCard.PlayCost : d => d.TopCard.Level;
            bool selfDp = m.Groups["self"].Success;
            int fixedBudget = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 0;
            string unit = k.StartsWith("dp") ? "DP" : k.StartsWith("play") ? "등장 코스트" : "Lv.";
            return (c =>
            {
                int budget = selfDp ? c.Self?.GetCurrentDP() ?? 0 : fixedBudget;
                var picked = new List<CardInstance>();
                while (true)
                {
                    var pool = c.Enemy.BattleArea.Where(d => !picked.Contains(d) && value(d) <= budget && !c.Engine.IsUnaffectedBy(c.Owner, d))
                        .OrderByDescending(value).ThenByDescending(d => d.TopCard.Level).ToList();
                    if (pool.Count == 0) break;
                    var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] {unit} 합계 {budget} 이하가 되도록 상대의 디지몬을 고르세요 (남은 {budget}, 그만 고르려면 취소).", optional: true, preselected: null).FirstOrDefault();
                    if (d == null) break;
                    picked.Add(d);
                    budget -= value(d);
                }
                foreach (var d in picked)
                {
                    if (!c.Enemy.BattleArea.Contains(d)) continue;
                    if (toBottom) c.Engine.BottomDeck(c.Enemy, d); else c.DeleteEnemy(d);
                }
                if (picked.Count == 0) c.LastStepResolved = false;
                Emit(c, label, picked.Count > 0 ? $"상대 디지몬 [{string.Join(", ", picked.Select(d => d.TopCard.Name))}] {(toBottom ? "덱 아래로" : "소멸")} ({unit} 합계)" : "고를 디지몬이 없습니다");
            }, null);
        }

        // ---- "activate 1 of this Digimon's [When Digivolving] effects" · "you may activate ~" · "activate 1 of that Digimon's ~" ----
        if ((m = Regex.Match(s, @"^(?<may>you may )?activate (?:1|one) of (?<who>this|that) Digimon's \[When Digivolving\] effects?$", RegexOptions.IgnoreCase)).Success)
        {
            bool that = m.Groups["who"].Value.Equals("that", StringComparison.OrdinalIgnoreCase), may = m.Groups["may"].Success;
            if (!that && card.Type != CardType.Digimon) return (null, "미지원: 디지몬이 아닌 카드의 \"this Digimon's [When Digivolving]\"");
            return (c =>
            {
                var d = that ? c.LastTargets.FirstOrDefault() ?? c.Other : c.Self;
                if (d == null || !c.Owner.BattleArea.Contains(d)) { c.LastStepResolved = false; return; }
                if (may && !c.Engine.AskYesNo(c.Owner, d, $"[{d.TopCard.Name}]의 【진화 시】 효과를 발휘할까요?", recommended: true)) { c.LastStepResolved = false; return; }
                Emit(c, label, $"[{d.TopCard.Name}]의 【진화 시】 효과를 발휘");
                if (!c.Engine.ActivateDigimonEffect(c.Owner, d, EffectTrigger.WhenDigivolving)) c.LastStepResolved = false;
            }, null);
        }

        // ---- 양쪽 드로우: "Both players ＜Draw 1＞" · "Both players draw 1 card from their decks" · "Both players draw the top card of their decks" ----
        if ((m = Regex.Match(s, @"^Both players (?:＜Draw (?<n>\d+)＞|draw (?<n>\d+) cards? from their decks?|draw the top card of their decks?)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            return (c =>
            {
                for (int i = 0; i < n; i++) { c.Engine.DrawFor(c.Owner); c.Engine.DrawFor(c.Enemy); }
                Emit(c, label, $"서로 {n}장 드로우");
            }, null);
        }

        // ---- 테이머 아래: "Place the top 2 cards of your deck face down under this Tamer" ----
        if ((m = Regex.Match(s, @"^(?:you may )?place the top (?<n>\d+) cards of your deck(?: face down)? under (?:this Tamer|(?:1|one) of your Tamers|your Tamers)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                if (c.Owner.Tamers.Count == 0 || c.Owner.MainDeck.Count == 0) { c.LastStepResolved = false; return; }
                int placed = 0;
                for (; placed < n && c.Owner.MainDeck.Count > 0; placed++) { c.Owner.UnderTamers.Add(c.Owner.MainDeck[0]); c.Owner.MainDeck.RemoveAt(0); }
                Emit(c, label, $"덱 위 {placed}장을 테이머 아래에 놓는다");
            }, null);
        }

        // ---- [트래시] 이 카드: "activate this card's [Main] effects" (옵션) ----
        if (_trashZoneParse && Regex.IsMatch(s, @"^Activate (?:this card'?s|its) \[Main\] effects?$", RegexOptions.IgnoreCase) && card.Type == CardType.Option)
            return (c => { Emit(c, label, "트래시의 이 카드의 [메인] 효과를 발휘"); ActivateMainEffects(c, label); }, null);
        return null;
    }

    /// <summary>
    /// v0.31.0 "[All Turns] This Digimon gains all (of the) effects (of|on) cards with [X] in their names in this Digimon's digivolution cards" ·
    /// "~ of [A] and [B] cards in ~" · "~ of [A] in ~" → 진화원 카드 조건 (이름 포함 / 같은 이름)
    /// </summary>
    internal static Func<CardData, bool>? GainAllEffectsClause(string text)
    {
        var m = Regex.Match(text, $@"^(?:\[All Turns\]\s*)?This Digimon gains all (?:of the )?effects (?:of|on) (?:cards with (?<inname>{TargetSpec.NameList}) in (?:their|its) names?|(?<exact>{TargetSpec.NameList})(?: cards?)?) in this Digimon's digivolution cards\.?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        if (m.Groups["inname"].Success)
        {
            var xs = TargetSpec.Names(m.Groups["inname"].Value);
            return c => xs.Any(x => TargetSpec.NameHas(c, x));
        }
        var exact = TargetSpec.Names(m.Groups["exact"].Value);
        return c => exact.Any(x => CardNames.Is(c, x));
    }

    /// <summary>
    /// v0.31.0 다른 카드의 등장 코스트 감소 문장 (그룹: labels · p(등장하는 카드) · cost · may · n · cnt). 라벨은 [Your Turn] · [All Turns] · [Once Per Turn] · [Breeding]만,
    /// "this card" · "this Digimon"(이 카드 자신의 감소)은 아니다
    /// </summary>
    internal static Match? FieldPlayCostClause(string text)
    {
        var m = Regex.Match(text, @"^(?<labels>(?:\[(?:Your Turn|All Turns|Once Per Turn|Breeding)\]\s*)*)When (?:you would play (?<p>[^,]+?)|(?<p>[^,]+?) would be played)(?: from (?:your|the) hand)?, (?:by (?<cost>[^,]+?), )?(?<may>you may )?reduce (?:the|its|their) (?:play |memory )?costs? by (?<n>\d+)(?: for (?:each|every) (?<cnt>[^,]+?))?\.?$", RegexOptions.IgnoreCase);
        if (!m.Success || Regex.IsMatch(m.Groups["p"].Value, @"^this\b", RegexOptions.IgnoreCase)) return null;
        return m;
    }

    /// <summary>등장 코스트 감소를 받는 카드: "[MarineAngemon]" · "[Arukenimon] or [Mummymon]" · "a green Tamer" · "any Digimon cards with the [ADVENTURE] trait" · "any of your Digimon or Tamers with the [CS] trait"</summary>
    internal static Func<CardData, bool>? FieldPlayCostTarget(string p)
    {
        p = Regex.Replace(p.Trim(), @"^(?:any of your|one of your|any|an?|1)\s+", "", RegexOptions.IgnoreCase);
        if (Regex.Match(p, $@"^(?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase) is { Success: true } named)
        {
            var xs = TargetSpec.Names(named.Groups["x"].Value);
            return c => xs.Any(x => CardNames.Is(c, x));
        }
        p = Regex.Replace(p, @"\b(Digimon|Tamer|Option) cards?\b", "$1", RegexOptions.IgnoreCase);
        p = Regex.Replace(p, @"\bwith (\[[^\]]+\]) trait\b", "with the $1 trait", RegexOptions.IgnoreCase);
        return EventCardSubject(p);
    }

    /// <summary>
    /// "you attack with ~" · "your ~ attacks"의 디지몬 조건 (v0.31.0): 보통 대상 조건(ParseAllySubject)에 더해
    /// "with [X] in its digivolution cards" · "with 2 or more colors" · "2-color blue and green" · "that has [X] in its name or is level 5 or higher" ·
    /// "with [Aqua] or [Sea Animal] in any of its traits"
    /// </summary>
    private static Func<EffectContext, CardInstance, bool>? AttackerSubject(string subj)
    {
        string s = Regex.Replace(subj.Trim(), @"^(?:an?|1|one of your|any of your)\s+", "", RegexOptions.IgnoreCase);
        Match m;
        // "Digimon that has [Angoramon] in its name or is level 5 or higher" · "Digimon with [Jellymon] in its name or is level 5 or higher"
        if ((m = Regex.Match(s, $@"^Digimon (?:that has|with) (?<x>{TargetSpec.NameList}) in (?:its|their) names? or is level (?<lv>\d+) or (?<dir>higher|lower)$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); int lv = int.Parse(m.Groups["lv"].Value); bool higher = m.Groups["dir"].Value.Equals("higher", StringComparison.OrdinalIgnoreCase);
            return (_, d) => xs.Any(x => TargetSpec.NameHas(d.TopCard, x)) || (higher ? d.TopCard.Level >= lv : d.TopCard.Level <= lv);
        }
        // "Digimon with [Gammamon] in its digivolution cards"
        if ((m = Regex.Match(s, $@"^Digimon with (?<x>{TargetSpec.NameList}) in (?:its|their) digivolution cards$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (_, d) => d.EvolutionSources.Any(src => xs.Any(x => TargetSpec.NameHas(src, x)));
        }
        // "Digimon with 2 or more colors"
        if ((m = Regex.Match(s, @"^Digimon with (?<n>\d+) or more colors$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (_, d) => d.TopCard.AllColors.Distinct().Count() >= n;
        }
        // "2-color blue and green Digimon"
        if ((m = Regex.Match(s, $@"^(?<n>\d+)-colou?r (?<a>{Colors}) and (?<b>{Colors}) Digimon$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var a = Enum.Parse<CardColor>(m.Groups["a"].Value, ignoreCase: true); var b = Enum.Parse<CardColor>(m.Groups["b"].Value, ignoreCase: true);
            return (_, d) => d.TopCard.AllColors.Distinct().Count() == n && d.TopCard.AllColors.Contains(a) && d.TopCard.AllColors.Contains(b);
        }
        // "Digimon with [Aqua] or [Sea Animal] in any of its traits"
        s = Regex.Replace(s, @"\bin any of their traits\b", "in any of its traits", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bin their (names?|texts?)\b", x => $"in its {x.Groups[1].Value.TrimEnd('s')}", RegexOptions.IgnoreCase);
        return ParseAllySubject(s);
    }
}
