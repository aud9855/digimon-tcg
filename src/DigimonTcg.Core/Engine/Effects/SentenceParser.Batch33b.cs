using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.33.0 미지원 문장 7차 묶음 — 조건 ("If this ~" · "If you ~" · "While ~"). 앞의 규칙이 읽지 못한 조건만 본다 (기존 판정은 그대로)
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>"If ~, ~" · "While ~, ~"의 조건에 쉼표 목록이 든 것 ("If this Digimon has the [Angel], [Archangel] or [Three Great Angels] trait, ~") — 쉼표 뒤가 "["이면 조건의 일부</summary>
    internal const string IfWithListPattern = @"^(?<w>If|While) (?<c>(?:[^,\[]|\[[^\]]*\]|,\s*(?=\[))+), (?<rest>.+)$";

    /// <summary>
    /// 진화원 안의 카드: "a Tamer card with the [SoC] trait" · "a card with the [Hybrid] trait or [Zoe Orimoto]" · "[Magnamon (X Antibody)] or an [Armor Form] trait card" ·
    /// "a level 3 Digimon card" · "a blue card" — 이름만 쓴 것(「X항체」 등)은 카드명으로 본다
    /// </summary>
    private static Func<CardData, bool>? SourceCardPick33(string pick)
    {
        string p = pick.Trim();
        if (Regex.IsMatch(p, $@"^{TargetSpec.NameList}$"))
        {
            var xs = TargetSpec.Names(p);
            return x => xs.Any(n => TargetSpec.NameHas(x, n));
        }
        // "A or [B]" · "[A] or B" — 이름만 쓴 쪽은 카드명
        foreach (Match or in Regex.Matches(p, @"\s+or\s+"))
        {
            string left = p[..or.Index].Trim(), right = p[(or.Index + or.Length)..].Trim();
            if (Regex.IsMatch(left, @"\]$") && Regex.IsMatch(right, @"^\[[^\]]+\]$") && !left.Contains(" ")) continue;   // "[A] or [B]" — 위에서 이미
            bool rightName = Regex.IsMatch(right, $@"^{TargetSpec.NameList}$"), leftName = Regex.IsMatch(left, $@"^{TargetSpec.NameList}$");
            if (!rightName && !leftName) continue;
            var a = leftName ? SourceCardPick33(left) : SourceCardPick33(left);
            var b = rightName ? SourceCardPick33(right) : SourceCardPick33(right);
            if (a != null && b != null) return x => a(x) || b(x);
        }
        string single = Regex.Replace(p, @"^(?:an?|1) ", "", RegexOptions.IgnoreCase);
        single = Regex.Replace(single, @"\bcards\b", "card", RegexOptions.IgnoreCase);
        if (!Regex.IsMatch(single, @"\bcard\b", RegexOptions.IgnoreCase)) return null;
        return (ParseCardPick("1 " + single) ?? ParseNamePick("1 " + single))?.Match;
    }

    /// <summary>이 디지몬에 대한 조건 (ParseDigimonCondition 끝에서 본다)</summary>
    private static Func<CardInstance?, bool>? ParseDigimonCondition33(string cond)
    {
        Match m;
        string c0 = Regex.Replace(cond.Trim(), @"^(?:this Digimon|it) had\b", "this Digimon has", RegexOptions.IgnoreCase);   // [소멸 시] "If this Digimon had the [Unidentified] trait"
        c0 = Regex.Replace(c0, @"\bin its name in its name$", "in its name", RegexOptions.IgnoreCase);   // 원문 반복
        c0 = Regex.Replace(c0, @"\bIs in\b", "is in");
        if (c0 != cond.Trim() && ParseDigimonCondition(c0) is { } re) return re;
        // "this Digimon's stack has 2 or more same-level cards" — 맨 위 카드와 진화원 중 Lv.이 같은 카드 (BT22 · BT23 · EX5 「빛의 송곳니 · 밤의 발톱」)
        if ((m = Regex.Match(c0, @"^this Digimon's stack has (?<n>\d+) or more same-level cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return d => d != null && SameLevelMax(d) >= n; }
        // 진화원 안의 카드: "a Tamer card with the [SoC] trait is in this Digimon's digivolution cards" · "[Lilithmon] or [X Antibody] in its digivolution cards" ·
        // "[Myotismon] or [X Antibody] was in this Digimon's digivolution cards" ([소멸 시])
        if ((m = Regex.Match(c0, @"^(?<p>.+?) (?:is |are |was |were )?in (?:this Digimon's|its) digivolution cards?$", RegexOptions.IgnoreCase)).Success
            && SourceCardPick33(m.Groups["p"].Value) is { } inSrc)
            return d => d != null && d.SourcesAny(inSrc);
        // "this Digimon has a red digivolution card" · "~ a purple card in its digivolution cards" · "~ an [Agumon] digivolution card" · "~ a non-red card in its digivolution cards" ·
        // "~ 4 or more level 6 cards in its digivolution cards"
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?:an? |(?<n>\d+) or more )(?<p>.+?) (?:digivolution cards?|cards? in its digivolution cards)$", RegexOptions.IgnoreCase)).Success
            && SourceCardPick33(m.Groups["p"].Value + " card") is { } hasSrc)
        { int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1; return d => d != null && d.SourcesCount(hasSrc) >= n; }
        // "this Digimon has 1 digivolution card" · "this Digimon has 3 digivolution cards" (정확히)
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?<n>\d+) digivolution cards?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return d => d != null && d.EvolutionSources.Count == n; }
        if ((m = Regex.Match(c0, @"^there are (?<n>\d+) or more digivolution cards under this Digimon$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return d => d != null && d.EvolutionSources.Count >= n; }
        // "there are 5 or more [Vemmon] in this Digimon's digivolution cards"
        if ((m = Regex.Match(c0, $@"^there are (?<n>\d+) or more (?<x>{TargetSpec.NameList}) in this Digimon's digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && d.SourcesCount(s => xs.Any(x => CardNames.Is(s, x))) >= n; }
        // "this Digimon has 7 link cards"
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?<n>\d+)(?<more> or more)? link cards?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["more"].Success; return d => d != null && (more ? d.LinkedCards.Count >= n : d.LinkedCards.Count == n); }
        // "this Digimon's name is [Koromon]"
        if ((m = Regex.Match(c0, $@"^this Digimon's name is (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && xs.Any(x => CardNames.Is(d.TopCard, x)); }
        // "this Digimon has an [On Play] effect"
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has an? \[(?<t>On Play|On Deletion|When Digivolving|When Attacking|Main)\] effect$", RegexOptions.IgnoreCase)).Success)
        { string t = m.Groups["t"].Value; return d => d != null && d.TopCard.EffectText.Contains($"[{t}]", StringComparison.OrdinalIgnoreCase); }
        // "this Digimon is black and has 13000 DP or more"
        if ((m = Regex.Match(c0, $@"^this Digimon is (?<c>{Colors}) and has (?<n>\d+) DP or (?<dir>more|less)$", RegexOptions.IgnoreCase)).Success)
        {
            var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
            return d => d != null && d.TopCard.AllColors.Contains(col) && (more ? d.GetCurrentDP() >= n : d.GetCurrentDP() <= n);
        }
        // "this Digimon has the [Bagra Army] or [Twilight] trait and is level 5 or higher"
        if ((m = Regex.Match(c0, $@"^this Digimon has (?:the )?(?<x>{TargetSpec.NameList}) traits? and is level (?<n>\d+) or (?<dir>higher|lower)$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); int n = int.Parse(m.Groups["n"].Value); bool hi = m.Groups["dir"].Value.Equals("higher", StringComparison.OrdinalIgnoreCase);
            return d => d != null && xs.Any(x => TargetSpec.HasTrait(d.TopCard, x)) && (hi ? d.TopCard.Level >= n : d.TopCard.Level <= n);
        }
        // "this Digimon has [Imperialdramon] in its name or the [Free] trait" · "[Gammamon] in its text or the [Hero] trait" · "[Dragon], [saur] or [Ceratopsian] in any of its traits" ·
        // "[Beast], [Animal] or [Sovereign] in any of its traits other than [Sea Animal] or the [Royal Knight] trait" — 대상 조건과 같은 읽기
        if ((m = Regex.Match(c0, @"^(?:this Digimon|it) has (?<rest>(?:the )?\[.+)$", RegexOptions.IgnoreCase)).Success
            && Regex.IsMatch(m.Groups["rest"].Value, @"\b(?:in (?:its|any of its) (?:names?|texts?|traits)|traits?)\b", RegexOptions.IgnoreCase)
            && TargetSpec.Parse("all of your Digimon with " + Regex.Replace(m.Groups["rest"].Value, @"\bor \[(?=[^\]]+\] traits?$)", "or the [", RegexOptions.IgnoreCase)) is { } hasSpec)
            return d => d != null && hasSpec.Filters.All(f => f(d));
        return null;
    }

    // =====================================================================
    // 사건 ("When ~,") — ParseEventPhrase31 · 32가 읽지 못한 것
    // =====================================================================

    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase33(string ev)
    {
        Match m;
        (List<EffectTrigger>, Func<EffectContext, bool>?) T(Func<EffectContext, bool>? cond, params EffectTrigger[] ts) => (ts.ToList(), cond);
        // 표기 정리 → 원래 규칙
        string fx = Regex.Replace(ev, @"^another of your Digimon\b", "one of your other Digimon", RegexOptions.IgnoreCase);   // P-154
        fx = Regex.Replace(fx, @"^your security is reduced$", "your security stack is removed from", RegexOptions.IgnoreCase);   // BT19-039
        if (fx != ev) return ParseEventPhrase(fx);
        // 사건 안의 쉼표 목록 "[Angel], [Archangel] or [Fallen Angel]" → "[Angel] or [Archangel] or [Fallen Angel]" (원래 규칙들이 쉼표에서 끊는다 — 뜻은 같다)
        string listed = Regex.Replace(ev, @"\],\s+(?=\[)", "] or [");
        // "When any of your cards with the [Beast] ~ trait are played" = "When you play a card with ~" (BT23-029)
        listed = Regex.Replace(listed, @"^(?:one of |any of )?your cards (?<w>with .+?) (?:is|are) played$", "you play a card ${w}", RegexOptions.IgnoreCase);
        if (listed != ev && ParseEventPhrase(listed) is { } fromList) return fromList;
        // "When a card with [Jellymon] in its text is trashed from your hand" (LM-004 — 원래 규칙은 "card" 앞에 말이 있어야 읽었다)
        if ((m = Regex.Match(ev, @"^(?:an? )?(?<pick>cards? with .+?) (?:is|are) trashed from your hand$", RegexOptions.IgnoreCase)).Success
            && ParseCardPick("1 " + Regex.Replace(m.Groups["pick"].Value, @"^cards", "card", RegexOptions.IgnoreCase)) is { } htPick)
        { var match = htPick.Match; return T(c => c.Engine.CurrentHandTrashed.Any(match), EffectTrigger.AllyHandTrashed); }

        // ---- 디지몬 · 테이머가 떠나려 할 때 (v0.33.0 테이머도 알린다): "When any of your Digimon or Tamers would leave the battle area by your opponent's effects" ·
        //      "When any of your [TS] trait Digimon or Tamers would leave the battle area" · "When any of your other [Shaman] or [Iliad] trait Digimon or Tamers would leave ~ other than by your effects"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<other>other )?(?:(?<x>{TargetSpec.NameList}) trait )?Digimon or Tamers would leave the battle area(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && ParseLeaveQualifier(m.Groups["q"].Value) is { } dtOk)
        {
            var xs = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            bool other = m.Groups["other"].Success;
            bool Fits(CardData top) => xs == null || xs.Any(x => TargetSpec.HasTrait(top, x));
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonWouldLeave, EffectTrigger.ThisDigimonWouldLeave, EffectTrigger.AllyTamerWouldLeave },
                c => c.Engine.CurrentLeave is { } l && dtOk(l) && Fits(l.Digimon.TopCard)
                     && (l.IsTamer ? ReferenceEquals(l.Digimon, c.Other) && (!other || c.Self != null || !ReferenceEquals(l.Digimon.TopCard, c.Card))
                                   : (ReferenceEquals(l.Digimon, c.Other) || ReferenceEquals(l.Digimon, c.Self)) && (!other || !ReferenceEquals(l.Digimon, c.Self))));
        }
        // "When any of your [Dinobeemon] or [Paildramon] would be returned to hands or decks" (BT20-074)
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<x>{TargetSpec.NameList})s? would be returned to (?:hands|your hand|the hand) or (?:decks|your deck|the deck)$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonWouldLeave, EffectTrigger.ThisDigimonWouldLeave },
                c => c.Engine.CurrentLeave is { } l && !l.IsTamer && l.Destination is GoldfishEngine.LeaveDestination.Hand or GoldfishEngine.LeaveDestination.Deck
                     && (ReferenceEquals(l.Digimon, c.Other) || ReferenceEquals(l.Digimon, c.Self)) && xs.Any(x => CardNames.Is(l.Digimon.TopCard, x)));
        }

        // ---- 패로 되돌아갔을 때 · 떠났을 때 ----
        if (Regex.IsMatch(ev, @"^another Digimon is returned to (?:the|its owner's|a) hand$", RegexOptions.IgnoreCase))   // BT14-030
            return T(c => c.Other != null && !ReferenceEquals(c.Other, c.Self), EffectTrigger.DigimonReturnedToHand);
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Tamers (?:is|are) deleted or returned to (?:the|your) hand$", RegexOptions.IgnoreCase))   // BT17-099
            return T(null, EffectTrigger.AllyTamerDeleted, EffectTrigger.AllyTamerReturnedToHand);
        if (Regex.IsMatch(ev, @"^(?:any of |one of )?your opponent's Digimon leaves? the battle area$", RegexOptions.IgnoreCase))   // AD1-025
            return T(null, EffectTrigger.OpponentDigimonLeft);

        // ---- 효과로 소멸 ----
        // "When an effect deletes one of your other Digimon or Tamers" (BT16-081) · "When effects delete Digimon" (BT21-100)
        if (Regex.IsMatch(ev, @"^(?:an effect deletes|effects delete) (?:one of |any of )?your other Digimon or Tamers$", RegexOptions.IgnoreCase))
            return T(c => c.Trigger == EffectTrigger.AllyTamerDeleted ? c.TriggerCard != null && !ReferenceEquals(c.TriggerCard, c.Card)
                          : c.Other != null && !ReferenceEquals(c.Other, c.Self) && c.Engine.CurrentDeletionCause is GoldfishEngine.DeletionCause.Effect or GoldfishEngine.DeletionCause.Cost,
                EffectTrigger.AllyDigimonDeleted, EffectTrigger.AllyTamerDeleted);
        if (Regex.IsMatch(ev, @"^(?:effects delete|an effect deletes) (?:a )?Digimon$", RegexOptions.IgnoreCase))
            return T(c => c.Engine.CurrentDeletionCause is GoldfishEngine.DeletionCause.Effect or GoldfishEngine.DeletionCause.Cost, EffectTrigger.AllyDigimonDeleted, EffectTrigger.OpponentDigimonDeleted);
        // "When any of your opponent's Digimon are deleted or their security stack is removed from" (BT21-029)
        if (Regex.IsMatch(ev, @"^(?:any of |one of )?your opponent's Digimon (?:is|are) deleted or their security stack is removed from$", RegexOptions.IgnoreCase))
            return T(null, EffectTrigger.OpponentDigimonDeleted, EffectTrigger.OpponentSecurityRemoved);

        // ---- 시큐리티 ----
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) added to your opponent's security stack$", RegexOptions.IgnoreCase))   // BT16-056
            return T(null, EffectTrigger.OpponentSecurityAdded);
        if (Regex.IsMatch(ev, @"^an effect removes cards? from your security stack$|^effects remove cards from your security stack$", RegexOptions.IgnoreCase))   // BT15-084
            return T(c => c.Engine.SecurityRemovedByEffect, EffectTrigger.AllySecurityRemoved);
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your security stack by (?:an )?effects?$", RegexOptions.IgnoreCase))   // BT17-036
            return T(c => c.Engine.SecurityRemovedByEffect && c.Engine.SecurityRemovedTrashed, EffectTrigger.AllySecurityRemoved);
        if (Regex.IsMatch(ev, @"^this Digimon checks (?:your opponent's|their) security stack$", RegexOptions.IgnoreCase))   // BT16-033 · BT22-080
            return T(c => c.Self != null && ReferenceEquals(c.Other, c.Self), EffectTrigger.AllyCheckedSecurity);

        // ---- 진화 · 어택 ----
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon digivolves or attacks$", RegexOptions.IgnoreCase))   // BT12-111
            return T(null, EffectTrigger.OpponentDigimonDigivolved, EffectTrigger.OpponentDigimonAttacks);
        // "When an opponent's Digimon digivolves or an effect places digivolution cards under an opponent's Digimon" (BT11-088)
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Digimon digivolves or (?:an effect places|effects place) digivolution cards under (?:an|one of your) opponent's Digimon$", RegexOptions.IgnoreCase))
            return T(null, EffectTrigger.OpponentDigimonDigivolved, EffectTrigger.OpponentSourcesAdded);
        // "When one of your Tamers digivolves" (BT7-081) — 바로 아래 카드가 테이머인 진화 (테이머에서 진화)
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Tamers digivolves?$", RegexOptions.IgnoreCase))
            return T(c => c.Other != null && c.Other.EvolutionSources.Count > 0 && c.Other.EvolutionSources.Peek().Type == CardType.Tamer, EffectTrigger.AllyDigimonDigivolved);
        // "When any of your Digimon digivolve from the trash" (BT23-068)
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon digivolves? from (?:the|your) trash$", RegexOptions.IgnoreCase))
            return T(c => c.Engine.DigivolvingFromTrash, EffectTrigger.AllyDigimonDigivolved);
        // "When one of your Digimon with 2 or more same-level cards in its stack attacks" (BT22-102)
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your Digimon with (?<n>\d+) or more same-level cards in its stack attacks$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return T(c => c.Other != null && SameLevelMax(c.Other) >= n, EffectTrigger.AllyDigimonAttacks); }
        // "When your Digimon with [Jellymon] in its text or when your opponent's level 5 or higher Digimon attacks" (RB1-033)
        if ((m = Regex.Match(ev, @"^your (?<a>Digimon with .+?) or when your opponent's (?<b>.+?) attacks$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your " + m.Groups["a"].Value) is { } aSpec && TargetSpec.Parse("all of your opponent's " + m.Groups["b"].Value) is { } bSpec)
            return T(c => c.Other != null && (c.Trigger == EffectTrigger.AllyDigimonAttacks ? aSpec.Filters.All(f => f(c.Other)) : bSpec.Filters.All(f => f(c.Other))),
                EffectTrigger.AllyDigimonAttacks, EffectTrigger.OpponentDigimonAttacks);
        // "When any of your [Reptile] or [Dragonkin] trait Digimon's attack targets change" (BT21-025)
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<spec>.+?Digimon)'s attack targets? (?:changes?|is switched|are switched)$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your " + m.Groups["spec"].Value) is { Opponent: false } swSpec)
            return T(c => c.Other != null && swSpec.Filters.All(f => f(c.Other)), EffectTrigger.AllyAttackTargetSwitched);

        // ---- 등장 ----
        // "When any other Digimon with the [Angel], [Archangel] or [Three Great Angels] trait is played" (BT18-032 — 어느 쪽이든)
        if ((m = Regex.Match(ev, @"^(?:any )?other (?<spec>Digimon with .+?) (?:is|are) played$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your " + m.Groups["spec"].Value) is { } otherSpec)
            return T(c => c.Other != null && !ReferenceEquals(c.Other, c.Self) && otherSpec.Filters.All(f => f(c.Other)), EffectTrigger.AllyDigimonPlayed, EffectTrigger.OpponentDigimonPlayed);
        // "When an effect plays an opponent's Digimon or Tamer" (BT15-081)
        if (Regex.IsMatch(ev, @"^(?:an effect plays|effects play) (?:an|one of your) opponent's Digimon or Tamers?$", RegexOptions.IgnoreCase))
            return T(c => c.Engine.PlayingByEffect, EffectTrigger.OpponentDigimonPlayed, EffectTrigger.OpponentTamerPlayed);
        // "When any of your [Knightmon] text Digimon are played or deleted" (EX13-074) · "When any of your Digimon with ~ are played or deleted"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?:(?<x>{TargetSpec.NameList}) text Digimon|(?<spec>Digimon with .+?)) (?:is|are) played or deleted$", RegexOptions.IgnoreCase)).Success)
        {
            Func<CardInstance, bool>? who = null;
            if (m.Groups["x"].Success) { var xs = TargetSpec.Names(m.Groups["x"].Value); who = d => xs.Any(x => TargetSpec.TextHas(d.TopCard, x)); }
            else if (TargetSpec.Parse("all of your " + m.Groups["spec"].Value) is { } pdSpec) who = d => pdSpec.Filters.All(f => f(d));
            if (who != null) return T(c => c.Other != null && who(c.Other), EffectTrigger.AllyDigimonPlayed, EffectTrigger.AllyDigimonDeleted);
        }
        // "When any of your [Chronomon] text or [Titan] trait Digimon are played" (BT26-078)
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<x>{TargetSpec.NameList}) text or (?<t>{TargetSpec.NameList}) trait Digimon (?:is|are) played$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); var ts = TargetSpec.Names(m.Groups["t"].Value);
            return T(c => c.Other != null && (xs.Any(x => TargetSpec.TextHas(c.Other.TopCard, x)) || ts.Any(t => TargetSpec.HasTrait(c.Other.TopCard, t))), EffectTrigger.AllyDigimonPlayed);
        }

        // ---- 트래시에서 패 · 덱으로 ----
        // "When a red card returns from your trash to the hand" (BT16-011) · "When a red Digimon card returns ~" (BT15-082)
        if ((m = Regex.Match(ev, @"^(?:an? )?(?<pick>.+?cards?) (?:returns?|is returned|are returned) from your trash to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success
            && ParseCardPick("1 " + Regex.Replace(m.Groups["pick"].Value, @"\bcards\b", "card", RegexOptions.IgnoreCase)) is { } thPick)
        { var match = thPick.Match; return T(c => c.Engine.CurrentMovedCards.Any(match), EffectTrigger.AllyTrashToHand); }
        // "When a card is returned to the deck from your trash" (P-048)
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) returned to (?:the|your) deck from your trash$", RegexOptions.IgnoreCase))
            return T(null, EffectTrigger.AllyTrashToDeck);
        return null;
    }

    /// <summary>스택(맨 위 카드 + 진화원) 안에서 Lv.이 같은 카드가 가장 많은 수</summary>
    internal static int SameLevelMax(CardInstance d) =>
        d.EvolutionSources.Append(d.TopCard).Where(x => x.Level > 0).GroupBy(x => x.Level).Select(g => g.Count()).DefaultIfEmpty(0).Max();

    /// <summary>자신 · 상대 필드 조건 (ParseSideCondition32 끝에서 본다 — "If ~" · "While ~" 양쪽)</summary>
    private static Func<PlayerZones, PlayerZones, bool>? ParseSideCondition33(string cond)
    {
        Match m;
        string c0 = cond.Trim();
        static bool Cmp(int value, int n, string dir) => dir.ToLowerInvariant() is "more" or "higher" ? value >= n : value <= n;
        // "they have 7 or more cards in their hand" · "their hand has 8 or more cards" · "your opponent's hand has 8 or more cards" · "your opponent has 8 or more cards in hand"
        if ((m = Regex.Match(c0, @"^(?:they have|your opponent has) (?<n>\d+) or (?<dir>more|fewer|less) cards in (?:their )?hand$|^(?:their|your opponent's) hand has (?<n>\d+) or (?<dir>more|fewer|less) cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value; return (_, e) => Cmp(e.Hand.Count, n, dir); }
        if ((m = Regex.Match(c0, @"^they have (?<n>\d+) or (?<dir>more|fewer|less) cards in their trash$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value; return (_, e) => Cmp(e.Trash.Count, n, dir); }
        if ((m = Regex.Match(c0, @"^they have (?<n>\d+) or (?<dir>more|fewer|less) security cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value; return (_, e) => Cmp(e.SecurityStack.Count, n, dir); }
        // "either player has 5 or more cards in their trash" · "the total trashes of both players' add up to 20 or more cards"
        if ((m = Regex.Match(c0, @"^either player has (?<n>\d+) or more cards in their trash$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (o, e) => o.Trash.Count >= n || e.Trash.Count >= n; }
        if ((m = Regex.Match(c0, @"^the total trashes of both players'? add up to (?<n>\d+) or more cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (o, e) => o.Trash.Count + e.Trash.Count >= n; }
        // 시큐리티 비교: "you have fewer / more security cards than your opponent" · "the number of cards in your security stack is less than or equal to your opponent's"
        if ((m = Regex.Match(c0, @"^you have (?<dir>fewer|more) security cards than your opponent(?: does)?$", RegexOptions.IgnoreCase)).Success)
        { bool fewer = m.Groups["dir"].Value.Equals("fewer", StringComparison.OrdinalIgnoreCase); return (o, e) => fewer ? o.SecurityStack.Count < e.SecurityStack.Count : o.SecurityStack.Count > e.SecurityStack.Count; }
        if (Regex.IsMatch(c0, @"^the number of cards in your security stack is less than or equal to your opponent's$", RegexOptions.IgnoreCase))
            return (o, e) => o.SecurityStack.Count <= e.SecurityStack.Count;
        // "your opponent has as many or more total Digimon and Tamers as you"
        if (Regex.IsMatch(c0, @"^your opponent has as many or more total Digimon and Tamers as you(?: do)?$", RegexOptions.IgnoreCase))
            return (o, e) => e.BattleArea.Count + e.Tamers.Count >= o.BattleArea.Count + o.Tamers.Count;
        // "your opponent has a Tamer"
        if (Regex.IsMatch(c0, @"^(?:your opponent|they) (?:has|have) a Tamer(?: in play)?$", RegexOptions.IgnoreCase))
            return (_, e) => e.Tamers.Count > 0;
        // "there's a Tamer" (양쪽 — 공식 한글 RB1-026 "테이머가 있는 동안") · "there's a suspended Digimon" · "there are 4 or more total Digimon"
        if (Regex.IsMatch(c0, @"^there(?:'s| is) a Tamer(?: in play)?$", RegexOptions.IgnoreCase)) return (o, e) => o.Tamers.Count + e.Tamers.Count > 0;
        if (Regex.IsMatch(c0, @"^there(?:'s| is) a suspended Digimon(?: in play)?$", RegexOptions.IgnoreCase)) return (o, e) => o.BattleArea.Concat(e.BattleArea).Any(d => d.IsSuspended);
        if ((m = Regex.Match(c0, @"^there are (?<n>\d+) or more total Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (o, e) => o.BattleArea.Count + e.BattleArea.Count >= n; }
        // "there's a card with [Myotismon] in its name in your trash" · "[Eyesmon: Scatter Mode] is in your trash"
        if ((m = Regex.Match(c0, @"^there(?:'s| is) (?<p>an? .+?) in your trash$", RegexOptions.IgnoreCase)).Success
            && ParseCardPick(Regex.Replace(m.Groups["p"].Value, @"^an? ", "1 ", RegexOptions.IgnoreCase)) is { } trashPick)
        { var match = trashPick.Match; return (o, _) => o.Trash.Any(match); }
        if ((m = Regex.Match(c0, $@"^(?<x>{TargetSpec.NameList}) is in your trash$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return (o, _) => o.Trash.Any(t => xs.Any(x => CardNames.Is(t, x))); }
        // 자신의 테이머: "you have a red or yellow Tamer (in play)" · "you don't have a Tamer (in play)" · "you have no Tamers in play" ·
        // "you have a Tamer with the [Light Fang] or [Night Claw] trait" · "you don't have a Tamer with [Tai Kamiya] in its name (in play)"
        if ((m = Regex.Match(c0, $@"^you (?<neg>don't have|do not have|have no|have) (?:an? |any )?(?:(?<col>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers?(?: with (?<with>.+?))?(?: in play| on the field)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool neg = !m.Groups["neg"].Value.Equals("have", StringComparison.OrdinalIgnoreCase);
            var cols = Regex.Matches(m.Groups["col"].Value, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
            Func<CardData, bool>? with = m.Groups["with"].Success ? TamerFilter33("", m.Groups["with"].Value) : _ => true;
            if (with == null) return null;
            return (o, _) => o.Tamers.Any(t => (cols.Count == 0 || t.AllColors.Any(cols.Contains)) && with(t)) != neg;
        }
        // "you have a purple Digimon or (purple) Tamer (in play)" · "a yellow Digimon or yellow Tamer" · "a green Digimon or Tamer"
        if ((m = Regex.Match(c0, $@"^you have an? (?<c>{Colors}) Digimon or (?:(?<c2>{Colors}) )?Tamer(?: in play| on the field)?$", RegexOptions.IgnoreCase)).Success
            && (!m.Groups["c2"].Success || m.Groups["c2"].Value.Equals(m.Groups["c"].Value, StringComparison.OrdinalIgnoreCase)))
        { var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); return (o, _) => o.BattleArea.Any(d => d.TopCard.AllColors.Contains(col)) || o.Tamers.Any(t => t.AllColors.Contains(col)); }
        // "you have a Digimon that's black or has the [Legend-Arms] trait (in play)" · "you have a Digimon with the [Legend-Arms] trait or a black Digimon in play"
        if ((m = Regex.Match(c0, $@"^you have an? Digimon that's (?<c>{Colors}) or has the (?<x>{TargetSpec.NameList}) traits?(?: in play)?$|^you have an? Digimon with the (?<x>{TargetSpec.NameList}) traits? or an? (?<c>{Colors}) Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (o, _) => o.BattleArea.Any(d => d.TopCard.AllColors.Contains(col) || xs.Any(x => TargetSpec.HasTrait(d.TopCard, x)));
        }
        // "you have a Digimon in play with 2 or more colors"
        if ((m = Regex.Match(c0, @"^you have an? Digimon(?: in play)? with (?<n>\d+) or more colou?rs(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (o, _) => o.BattleArea.Any(d => d.TopCard.AllColors.Count >= n); }
        // "you have a Digimon in play with [Jellymon] in its name or with [Jellymon] in its digivolution cards"
        if ((m = Regex.Match(c0, $@"^you have an? Digimon(?: in play)? with (?<x>{TargetSpec.NameList}) in its name or (?:with )?(?<y>{TargetSpec.NameList}) in its digivolution cards(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); var ys = TargetSpec.Names(m.Groups["y"].Value);
            return (o, _) => o.BattleArea.Any(d => xs.Any(x => TargetSpec.NameHas(d.TopCard, x)) || d.SourcesAny(s => ys.Any(y => TargetSpec.NameHas(s, y))));
        }
        // "you have a Digimon card with [Sistermon] in its name in play"
        if ((m = Regex.Match(c0, @"^you have an? Digimon card (?<with>with .+?) in play$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your Digimon " + m.Groups["with"].Value) is { } dcard)
            return (o, _) => o.BattleArea.Any(d => dcard.Filters.All(f => f(d)));
        // "you have [Tai Kamiya] or a Tamer with the [ADVENTURE] trait" · "you have [Argomon] or a yellow Digimon with [Agumon] or [Greymon] (in its name)"
        if ((m = Regex.Match(c0, $@"^you have (?<x>{TargetSpec.NameList}) or an? (?<rest>.+)$", RegexOptions.IgnoreCase)).Success
            && ParseSideCondition("you have a " + m.Groups["rest"].Value) is { } orRest)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (o, e) => xs.Any(x => o.BattleArea.Any(d => CardNames.Is(d.TopCard, x)) || o.Tamers.Any(t => CardNames.Is(t, x))) || orRest(o, e);
        }
        if ((m = Regex.Match(c0, $@"^you have an? yellow Digimon with (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)   // BT17-089 "a yellow Digimon with [Agumon] or [Greymon]" (명칭)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return (o, _) => o.BattleArea.Any(d => d.TopCard.AllColors.Contains(CardColor.Yellow) && xs.Any(x => TargetSpec.NameHas(d.TopCard, x))); }
        // "you have a Digimon with the [Free] trait or a Tamer with [Ken Ichijoji] in its name in play"
        if ((m = Regex.Match(c0, @"^you have an? (?<a>Digimon with .+?) or an? (?<b>Tamer with .+?)(?: in play)?$", RegexOptions.IgnoreCase)).Success
            && ParseSideCondition("you have a " + m.Groups["a"].Value) is { } dA && ParseSideCondition33("you have a " + m.Groups["b"].Value) is { } tB)
            return (o, e) => dA(o, e) || tB(o, e);
        // "your Tamers with the [ADVENTURE] trait have 3 or more total colors"
        if ((m = Regex.Match(c0, $@"^your Tamers with the (?<x>{TargetSpec.NameList}) traits? have (?<n>\d+) or more (?:total )?colou?rs$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); int n = int.Parse(m.Groups["n"].Value); return (o, _) => o.Tamers.Where(t => xs.Any(x => TargetSpec.HasTrait(t, x))).SelectMany(t => t.AllColors).Distinct().Count() >= n; }
        // "all of your Digimon are suspended"
        if (Regex.IsMatch(c0, @"^all of your Digimon are suspended$", RegexOptions.IgnoreCase)) return (o, _) => o.BattleArea.Count > 0 && o.BattleArea.All(d => d.IsSuspended);
        // "you have 2 or more suspended Digimon with [Vegetation] (in any of their traits)"
        // "you have a level 6 [Machine] trait Digimon" · "you have [Deva] or [Four Sovereigns] trait Digimon" · "you have a level 5 [WarGrowlmon], [Taomon] or [Rapidmon]"
        if ((m = Regex.Match(c0, $@"^you have (?:an? )?(?:level (?<lv>\d+) )?(?<x>{TargetSpec.NameList})(?<trait> traits?)?(?: Digimon)?$", RegexOptions.IgnoreCase)).Success
            && (m.Groups["lv"].Success || m.Groups["trait"].Success))
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); bool trait = m.Groups["trait"].Success; int lv = m.Groups["lv"].Success ? int.Parse(m.Groups["lv"].Value) : 0;
            return (o, _) => o.BattleArea.Any(d => (lv == 0 || d.TopCard.Level == lv) && xs.Any(x => trait ? TargetSpec.HasTrait(d.TopCard, x) : CardNames.Is(d.TopCard, x)));
        }
        // "you have a yellow [Data] or [Witchelny] trait Digimon"
        if ((m = Regex.Match(c0, $@"^you have an? (?<c>{Colors}) (?<x>{TargetSpec.NameList}) traits? Digimon$", RegexOptions.IgnoreCase)).Success)
        { var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); var xs = TargetSpec.Names(m.Groups["x"].Value); return (o, _) => o.BattleArea.Any(d => d.TopCard.AllColors.Contains(col) && xs.Any(x => TargetSpec.HasTrait(d.TopCard, x))); }
        // "you have a Digimon or Tamer with [Pulsemon] in its text (on the field)" · "a Tamer or [Hybrid] trait Digimon" · "a Tamer with inherited effects"
        if ((m = Regex.Match(c0, $@"^you have an? Digimon or Tamer with (?<x>{TargetSpec.NameList}) in its text(?: on the field| in play)?$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return (o, _) => o.BattleArea.Any(d => xs.Any(x => TargetSpec.TextHas(d.TopCard, x))) || o.Tamers.Any(t => xs.Any(x => TargetSpec.TextHas(t, x))); }
        if ((m = Regex.Match(c0, $@"^you have an? Tamer or (?<x>{TargetSpec.NameList}) trait Digimon$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return (o, _) => o.Tamers.Count > 0 || o.BattleArea.Any(d => xs.Any(x => TargetSpec.HasTrait(d.TopCard, x))); }
        if (Regex.IsMatch(c0, @"^you have an? Tamer with inherited effects$", RegexOptions.IgnoreCase))
            return (o, _) => o.Tamers.Any(t => t.InheritedEffectText.Trim().Length > 0);
        // "your opponent has a white Digimon or Tamer" · "your opponent has a level 6 or higher Digimon"
        if ((m = Regex.Match(c0, $@"^your opponent has an? (?<c>{Colors}) Digimon or Tamer$", RegexOptions.IgnoreCase)).Success)
        { var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); return (_, e) => e.BattleArea.Any(d => d.TopCard.AllColors.Contains(col)) || e.Tamers.Any(t => t.AllColors.Contains(col)); }
        return null;
    }

    /// <summary>이 디지몬과 자신 필드 조건 ("another ~" — 이 디지몬이 아닌): "you have another suspended Digimon" · "you have another green Digimon" · "~ Digimon or Tamer with the [Xros Heart] trait"</summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, bool>? ParseSelfSideCondition33(string cond)
    {
        Match m;
        string c0 = Regex.Replace(cond.Trim(), @"\s+in play$", "", RegexOptions.IgnoreCase);
        if ((m = Regex.Match(c0, @"^you have another (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            string rest = m.Groups["rest"].Value;
            // "Digimon or Tamer with the [Xros Heart] (or [Twilight]) trait"
            if ((m = Regex.Match(rest, $@"^Digimon or Tamer with the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
            {
                var xs = TargetSpec.Names(m.Groups["x"].Value);
                return (o, _, self) => o.BattleArea.Any(d => !ReferenceEquals(d, self) && xs.Any(x => TargetSpec.HasTrait(d.TopCard, x))) || o.Tamers.Any(t => xs.Any(x => TargetSpec.HasTrait(t, x)));
            }
            if (Regex.IsMatch(rest, @"^Digimon or Tamer$", RegexOptions.IgnoreCase))
                return (o, _, self) => o.BattleArea.Any(d => !ReferenceEquals(d, self)) || o.Tamers.Count > 0;
            // "suspended Digimon" · "green Digimon" · "level 6 Digimon" · "Digimon with the [D-Brigade] trait" · "Digimon with [Mamemon] in its name" · "Digimon"
            if (TargetSpec.Parse("all of your " + rest) is { Opponent: false, AnySide: false, IsThis: false, IsThat: false } spec && spec.ContextFilters.Count == 0)
                return (o, _, self) => o.BattleArea.Any(d => !ReferenceEquals(d, self) && spec.Filters.All(f => f(d)));
        }
        // "your opponent has no Digimon with as many or more digivolution cards as this Digimon" · "~ with with as many or more digivolution cards than this Digimon"(원문)
        if (Regex.IsMatch(c0, @"^your opponent has no Digimon with (?:with )?as many or more digivolution cards (?:as|than) this Digimon$", RegexOptions.IgnoreCase))
            return (_, e, self) => self != null && !e.BattleArea.Any(d => d.EvolutionSources.Count >= self.EvolutionSources.Count);
        return null;
    }

    /// <summary>효과 상황 조건 (ParseEventCondition 끝에서 본다)</summary>
    private static Func<EffectContext, bool>? ParseEventCondition33(string cond)
    {
        Match m;
        string c0 = cond.Trim();
        if (ParseDigimonCondition33(c0) is { } dc) return c => dc(c.Self);
        if (ParseSideCondition33(c0) is { } sc) return c => sc(c.Owner, c.Enemy);
        if (ParseSelfSideCondition33(c0) is { } ss) return c => ss(c.Owner, c.Enemy, c.Self);
        // [소멸 시]: "not deleted in battle" · "not deleted by battle" · "deleted other than in/by battle" · "deleted by an effect" · "that Digimon was deleted by an effect"
        if (Regex.IsMatch(c0, @"^(?:not deleted (?:in|by) battle|deleted other than (?:in|by) battle|this Digimon wasn't deleted (?:in|by) battle)$", RegexOptions.IgnoreCase))
            return c => c.Engine.CurrentDeletionCause != GoldfishEngine.DeletionCause.Battle;
        if (Regex.IsMatch(c0, @"^(?:deleted by (?:an )?effects?|(?:that Digimon|it) was deleted by (?:an )?effects?)$", RegexOptions.IgnoreCase))
            return c => c.Engine.CurrentDeletionCause is GoldfishEngine.DeletionCause.Effect or GoldfishEngine.DeletionCause.Cost;
        // 등장한 방법: "played by effects" · "played or digivolved by (an) effect(s)" · "played from digivolution cards"
        if (Regex.IsMatch(c0, @"^played by (?:an )?effects?$", RegexOptions.IgnoreCase))
            return c => c.Trigger is EffectTrigger.OnPlay or EffectTrigger.AllyDigimonPlayed or EffectTrigger.OpponentDigimonPlayed && c.Engine.PlayingByEffect;
        if (Regex.IsMatch(c0, @"^played or digivolved by (?:an )?effects?$", RegexOptions.IgnoreCase))
            return c => c.Trigger switch
            {
                EffectTrigger.OnPlay or EffectTrigger.AllyDigimonPlayed or EffectTrigger.OpponentDigimonPlayed => c.Engine.PlayingByEffect,   // "When Digimon are played or digivolve, ~ if played or digivolved by effects" (AD1-024) — 그 디지몬
                EffectTrigger.WhenDigivolving => c.Engine.WhenDigivolvingByEffect,
                EffectTrigger.AllyDigimonDigivolved or EffectTrigger.OpponentDigimonDigivolved => c.Engine.DigivolvingByEffect,
                _ => false,
            };
        if (Regex.IsMatch(c0, @"^played from digivolution cards$", RegexOptions.IgnoreCase))
            return c => c.Trigger == EffectTrigger.OnPlay && c.Engine.CurrentPlayOrigin == PlayOrigin.Sources;
        // 테이머: "this Tamer is suspended" · "this Tamer has 2 or fewer cards under it" · "this Tamer has 3 or more [Knightmon] text cards under it" (테이머 아래 카드는 한 묶음)
        if (Regex.IsMatch(c0, @"^this Tamer is suspended$", RegexOptions.IgnoreCase))
            return c => c.Card.Type == CardType.Tamer && c.Owner.Tamers.Contains(c.Card) && !c.Owner.HasActiveTamer(c.Card);
        if ((m = Regex.Match(c0, $@"^this Tamer has (?<n>\d+) or (?<dir>more|fewer|less) (?:(?<x>{TargetSpec.NameList}) text )?cards under it$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
            var xs = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            return c => { int k = c.Owner.UnderTamers.Count(x => xs == null || !c.Owner.IsFaceDownUnderTamer(x) && xs.Any(v => TargetSpec.TextHas(x, v))); return more ? k >= n : k <= n; };
        }
        // 어택: "this Digimon is attacking" · "one of their Digimon is attacking"
        if (Regex.IsMatch(c0, @"^this Digimon is attacking$", RegexOptions.IgnoreCase))
            return c => c.Self != null && ReferenceEquals(c.Engine.CurrentAttacker, c.Self);
        if (Regex.IsMatch(c0, @"^one of (?:their|your opponent's) Digimon is attacking$", RegexOptions.IgnoreCase))
            return c => c.Engine.CurrentAttacker is { } a && c.Enemy.BattleArea.Contains(a);
        // 메모리: "you have 0 memory"
        if ((m = Regex.Match(c0, @"^you have (?<n>\d+) memory$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.OwnerMemory == n; }
        if ((m = Regex.Match(c0, @"^they have (?<n>\d+) or (?<dir>more|fewer|less) memory$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase); return c => more ? -c.OwnerMemory >= n : -c.OwnerMemory <= n; }
        // 사건의 디지몬: "any of them are red" · "any of them have [Vemmon] in their texts" · "any of them have [Aqua] or [Sea Animal] in any of their traits" ·
        // "it has the [Free] trait or is yellow" · "that Digimon is level 5 or higher" · "that Digimon is [Jesmon GX]" · "the attacking Digimon is [Beelzemon]" · "the Digimon that played is [MarineAngemon]"
        if ((m = Regex.Match(c0, $@"^(?:any of them|it|that Digimon) (?:are|is) (?<c>{Colors})$", RegexOptions.IgnoreCase)).Success)
        { var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true); return c => c.Other != null && c.Other.TopCard.AllColors.Contains(col); }
        if ((m = Regex.Match(c0, @"^(?:any of them|it|that Digimon) (?:have|has) (?<rest>(?:the )?\[.+)$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your Digimon with " + Regex.Replace(Regex.Replace(m.Groups["rest"].Value, @"\btheir\b", "its", RegexOptions.IgnoreCase), @"\bor is (?=" + Colors + ")", "or ", RegexOptions.IgnoreCase)) is { } themSpec)
            return c => c.Other != null && themSpec.Filters.All(f => f(c.Other));
        if ((m = Regex.Match(c0, $@"^(?:it|that Digimon) has the (?<x>{TargetSpec.NameList}) trait or is (?<c>{Colors})$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true);
            return c => (c.Other ?? c.LastTargets.FirstOrDefault()) is { } d && (xs.Any(x => TargetSpec.HasTrait(d.TopCard, x)) || d.TopCard.AllColors.Contains(col));
        }
        if ((m = Regex.Match(c0, @"^(?:that Digimon|it) is level (?<n>\d+) or (?<dir>higher|lower)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool hi = m.Groups["dir"].Value.Equals("higher", StringComparison.OrdinalIgnoreCase);
            return c => (c.LastTargets.FirstOrDefault() ?? c.Other) is { } d && (hi ? d.TopCard.Level >= n : d.TopCard.Level <= n);
        }
        if ((m = Regex.Match(c0, $@"^(?:that Digimon|the attacking Digimon|the Digimon that played) is (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            bool attacking = c0.StartsWith("the attacking", StringComparison.OrdinalIgnoreCase);
            return c => (attacking ? c.Engine.CurrentAttacker : c.Other ?? c.LastTargets.FirstOrDefault()) is { } d && xs.Any(x => CardNames.Is(d.TopCard, x));
        }
        // 앞 문장의 결과: "this effect played / returned / digivolved" · "you played" · "you didn't" · "you don't" · "this effect didn't trash / return / flip"
        if (Regex.IsMatch(c0, @"^(?:this effect (?:played|returned|digivolved|digivolve)|you played)$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved;
        if (Regex.IsMatch(c0, @"^(?:you didn't|you don't|you did not|you do not|this effect didn't (?:trash|return|flip))$", RegexOptions.IgnoreCase))
            return c => !c.LastStepResolved;
        if (Regex.IsMatch(c0, @"^this didn't delete$", RegexOptions.IgnoreCase))   // = "this effect didn't delete" (EX8-073)
            return c => !c.LastTargets.Any(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d));
        // "no opponent's Digimon was deleted by this effect" · "an opponent's Digimon wasn't deleted by this effect" · "no Digimon was returned by this effect"
        if (Regex.IsMatch(c0, @"^(?:no opponent's Digimon was deleted by this effect|an opponent's Digimon wasn't deleted by this effect)$", RegexOptions.IgnoreCase))
            return c => !c.LastTargets.Any(d => !c.Enemy.BattleArea.Contains(d) && !c.Owner.BattleArea.Contains(d));
        if (Regex.IsMatch(c0, @"^no Digimon was returned by this effect$", RegexOptions.IgnoreCase))
            return c => !c.LastTargets.Any(d => !c.Enemy.BattleArea.Contains(d) && !c.Owner.BattleArea.Contains(d));
        if (Regex.IsMatch(c0, @"^this effect deleted one of your Digimon$", RegexOptions.IgnoreCase))
            return c => c.LastTargets.Any(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d) && c.Owner.Trash.Contains(d.TopCard));
        // 두 조건: "A or B" · "A and B" — 양쪽을 읽을 수 있을 때 (뒤쪽에 주어가 없으면 앞쪽 주어 "you have"를 붙인다)
        return CombineCondition33(c0, ParseEventCondition, (a, b, and) => and ? c => a(c) && b(c) : c => a(c) || b(c));
    }

    /// <summary>"A or B" · "A and B" → 두 조건 (ParseEventCondition · ParseStaticCondition 공통)</summary>
    private static Func<T, bool>? CombineCondition33<T>(string cond, Func<string, Func<T, bool>?> parse, Func<Func<T, bool>, Func<T, bool>, bool, Func<T, bool>> join)
    {
        foreach (Match sep in Regex.Matches(cond, @",? (?<w>or|and) (?:if )?"))
        {
            string left = cond[..sep.Index].Trim(), right = cond[(sep.Index + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            // 이름 · 특징 목록 안의 "or" ("[A] or [B]")는 나누지 않는다
            if (left.EndsWith("]") && right.StartsWith("[") && !Regex.IsMatch(right, @"^\[[^\]]+\] (?:is|are|was) in\b", RegexOptions.IgnoreCase)) continue;
            bool and = sep.Groups["w"].Value.Equals("and", StringComparison.OrdinalIgnoreCase);
            if (parse(left) is not { } a) continue;
            var b = parse(right);
            if (b == null && Regex.IsMatch(left, @"^you have\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(right, @"^(?:you|your|this|it|there|they|their)\b", RegexOptions.IgnoreCase))
                b = parse("you have " + right);
            if (b == null) continue;
            return join(a, b, and);
        }
        return null;
    }

    /// <summary>지속 효과의 조건 (ParseStaticCondition 끝에서 본다)</summary>
    private static Func<StaticContext, bool>? ParseStaticCondition33(string cond)
    {
        string c0 = cond.Trim();
        if (ParseDigimonCondition33(c0) is { } dc) return c => dc(c.Digimon);
        if (ParseSideCondition33(c0) is { } sc) return c => sc(c.Owner, c.Enemy);
        if (ParseSelfSideCondition33(c0) is { } ss) return c => ss(c.Owner, c.Enemy, c.Digimon);
        if (Regex.IsMatch(c0, @"^you have a suspended Tamer(?: in play)?$", RegexOptions.IgnoreCase))
            return c => c.Owner.SuspendedTamers.Count > 0;
        return CombineCondition33(c0, ParseStaticCondition, (a, b, and) => and ? c => a(c) && b(c) : c => a(c) || b(c));
    }

    /// <summary>사건의 디지몬이 있는 시점("When one of your Digimon is played or digivolves, if it has ~")의 "it" = 그 디지몬 (이 디지몬이 아니다)</summary>
    private static string IfSubject33(string cond, List<EffectTrigger> triggers) =>
        Regex.IsMatch(cond, @"^it (?:has|have|is)\b", RegexOptions.IgnoreCase)
        && triggers.Any(t => t is EffectTrigger.AllyDigimonPlayed or EffectTrigger.AllyDigimonDigivolved or EffectTrigger.OpponentDigimonPlayed or EffectTrigger.OpponentDigimonDigivolved
                              or EffectTrigger.AllyDigimonAttacks or EffectTrigger.OpponentDigimonAttacks)
            ? "that Digimon" + cond[2..] : cond;

    /// <summary>색 조건 무시 "While ~, you can ignore this card's color requirements"의 조건 (자신 필드만 — 상대를 보는 조건은 아직)</summary>
    private static Func<PlayerZones, bool>? IgnoreColorCondition33(string cond)
    {
        string c0 = Regex.Replace(cond.Trim(), @"\bin its name the field$", "in its name on the field", RegexOptions.IgnoreCase);   // BT20-057 원문
        if (Regex.IsMatch(c0, @"\b(?:opponent|they|their)\b", RegexOptions.IgnoreCase)) return null;
        if (c0 != cond.Trim() && ParseZoneCondition(c0) is { } zc) return zc;
        var sc = ParseSideCondition(c0) ?? ParseSideCondition33(c0);
        return sc == null ? null : z => sc(z, z);
    }
}
