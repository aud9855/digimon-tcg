using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.34.0 미지원 문장 8차 묶음 — 제한 · 보호 ("can't attack players" · "can't be deleted in battle" · "isn't affected by your opponent's effects" ·
/// "can't be returned to the hand or deck" · "can't have its DP reduced" · "can also attack your opponent's unsuspended Digimon") ·
/// "This Digimon with ~" 조건부 주어 · "End the attack" · 상대가 고르는 동작 ("Your opponent may trash 1 card in their hand. If they don't, ~") ·
/// 비용 ("trashing any 2 of this Digimon's digivolution cards" · "returning 2 cards from your opponent's trash to the bottom of the deck" 등) ·
/// "~ for each card trashed". 앞의 규칙에 걸리지 않은 것만 본다
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I = RegexOptions.IgnoreCase;

    // =====================================================================
    // 제한 · 보호 (키워드처럼 얻는다)
    // =====================================================================

    /// <summary>제한 · 보호 술어 → 키워드 (Good = 받는 디지몬에게 이로운지 — 양쪽 대상의 고르기 순서)</summary>
    internal static readonly (Regex Pattern, Keyword[] Keywords, bool Good)[] Restrictions34 =
    {
        (new Regex(@"^can't attack (?:players|(?:your |the )?opponents?|the player|a player)$", I), new[] { Keyword.CannotAttackPlayers }, false),
        (new Regex(@"^can't attack (?:your opponent's |their |an opponent's |opposing )?Digimon$", I), new[] { Keyword.CannotAttackDigimon }, false),
        (new Regex(@"^can't be deleted (?:in|by) battles?$", I), new[] { Keyword.CannotBeDeletedInBattle }, true),
        (new Regex(@"^can't be deleted by (?:your |the )?opponent's(?: card)? effects?$", I), new[] { Keyword.CannotBeDeletedByOpponentEffects }, true),
        (new Regex(@"^can't be deleted by effects$", I), new[] { Keyword.CannotBeDeletedByEffects }, true),
        (new Regex(@"^can't be returned to (?:the |its owner's |their owners'? |your |their )?hands? or (?:the )?decks? by (?:your |the )?opponent's effects$", I), new[] { Keyword.CannotBeReturnedByOpponentEffects }, true),
        (new Regex(@"^can't be returned to (?:the |its owner's |their owners'? |your |their )?hands? or (?:the )?decks?$", I), new[] { Keyword.CannotBeReturnedByEffects }, true),
        (new Regex(@"^(?:isn't|is not|aren't|are not) affected by (?:the effects of (?:your opponent's|their) Digimon|(?:your opponent's|their) Digimon(?:'s)? effects)$", I), new[] { Keyword.UnaffectedByOpponentDigimonEffects }, true),
        (new Regex(@"^(?:isn't|is not|aren't|are not) affected by (?:the effects of (?:your opponent's|their) Option cards|(?:your opponent's|their) Option(?: card)?(?:'s|s')? effects)$", I), new[] { Keyword.UnaffectedByOpponentOptionEffects }, true),
        (new Regex(@"^(?:isn't|is not|aren't|are not) affected by (?:the effects of )?(?:your opponent's|their|the opponent's) effects$|^(?:isn't|is not|aren't|are not) affected by (?:your opponent's|their) effects$", I), new[] { Keyword.UnaffectedByOpponentEffects }, true),
        (new Regex(@"^(?:isn't|is not) affected by (?:your opponent's|their) ＜De-Digivolve＞ effects$", I), new[] { Keyword.UnaffectedByOpponentDeDigivolve }, true),   // v0.36.0 (BT22-060)
        (new Regex(@"^can't have (?:its|their) DP reduced by (?:your |the )?opponent's(?: card)? effects$", I), new[] { Keyword.DpCannotBeReducedByOpponent }, true),
        (new Regex(@"^can't have (?:its|their) DP reduced$", I), new[] { Keyword.DpCannotBeReduced }, true),
        (new Regex(@"^can't be blocked(?: by (?:your opponent's|their) Digimon)?$|^(?:is|are) unblockable$", I), new[] { Keyword.Unblockable }, true),
        (new Regex(@"^can't be attacked$", I), new[] { Keyword.CannotBeAttacked }, true),
        (new Regex(@"^(?:can|may) (?:also )?attack (?:your opponent's|their|an opponent's) unsuspended Digimon with no digivolution cards$", I), new[] { Keyword.CanAttackUnsuspendedSourceless }, true),
        (new Regex(@"^(?:can|may) (?:also )?attack (?:your opponent's|their|an opponent's) unsuspended Digimon$", I), new[] { Keyword.CanAttackUnsuspended }, true),
        (new Regex(@"^can't activate \[When Digivolving\] effects$", I), new[] { Keyword.CannotActivateWhenDigivolving }, false),
        (new Regex(@"^can't activate \[When Attacking\] effects$", I), new[] { Keyword.CannotActivateWhenAttacking }, false),
        (new Regex(@"^can't activate \[When (?:Digivolving|Attacking)\] (?:effects )?(?:or|and) \[When (?:Digivolving|Attacking)\] effects$", I), new[] { Keyword.CannotActivateWhenDigivolving, Keyword.CannotActivateWhenAttacking }, false),
    };

    /// <summary>술어 하나: 키워드 · 기간 필드(어택 · 블록 · 진화 · 레스트 · 액티브 불가) · DP · 얻는 키워드</summary>
    internal sealed record Pred34(string Text, Keyword[] Keywords, string? Field, int Dp, List<(Keyword? Keyword, int SecurityAttack)>? Grants, bool Good);

    private static readonly Regex PredStart34 = new(@"(?:gets?|gains?|can't|cannot|can (?:also )?attack|may also attack|isn't|is not|aren't|are not|(?:is|are) unblockable)\b", I | RegexOptions.Compiled);

    /// <summary>술어 표기 정리</summary>
    private static string NormalizePred34(string p)
    {
        p = p.Trim().TrimEnd('.', ',').Trim();
        p = Regex.Replace(p, @"\bcannot\b", "can't", I);
        p = Regex.Replace(p, @"\bgains? (?=[+-]\d+ ?DP\b)", "gets ", I);
        p = Regex.Replace(p, @"\b(?<n>[+-]\d+)DP\b", "${n} DP", I);
        return p;
    }

    /// <summary>"gets +2000 DP and can't be deleted by your opponent's effects" → 술어 목록 (읽지 못하면 null)</summary>
    internal static List<Pred34>? ParsePreds34(string preds)
    {
        var parts = new List<string>();
        string rest = preds.Trim();
        // "can't activate [When Digivolving] effects or suspend" · "can't digivolve or attack players" — 두 제한 ("can't attack or block"은 그대로)
        rest = Regex.Replace(rest, @"\bcan't (?<a>digivolve|suspend|unsuspend|activate \[When (?:Digivolving|Attacking)\] effects) or (?<b>suspend|unsuspend|digivolve|attack players|activate \[When (?:Digivolving|Attacking)\] effects)\b", "can't ${a} and can't ${b}", I);
        // 술어 사이: ", and " · " and " · ", " 다음에 술어 동사가 오는 곳에서 나눈다 (키워드 목록 "＜A＞, ＜B＞ and ＜C＞"는 그대로)
        var cuts = Regex.Matches(rest, @"(?:,\s*and\s+|\s+and\s+|,\s+)(?=(?:gets?|gains?|can't|cannot|can (?:also )?attack|may also attack|isn't|is not|aren't|are not|(?:is|are) unblockable)\b)", I);
        int at = 0;
        foreach (Match cut in cuts)
        {
            parts.Add(rest[at..cut.Index]);
            at = cut.Index + cut.Length;
        }
        parts.Add(rest[at..]);
        var list = new List<Pred34>();
        foreach (var raw in parts)
        {
            string p = NormalizePred34(raw);
            Match m;
            if ((m = Regex.Match(p, @"^gets? (?<sign>[+-])(?<n>\d+) DP$", I)).Success)
            {
                int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
                list.Add(new Pred34(p, Array.Empty<Keyword>(), null, dp, null, dp > 0));
                continue;
            }
            if ((m = Regex.Match(p, @"^(?:gains?|gets?) (?<kws>＜.+)$", I)).Success)
            {
                var grants = ParseKeywordList(m.Groups["kws"].Value);
                if (grants == null) return null;
                list.Add(new Pred34(p, Array.Empty<Keyword>(), null, 0, grants, grants.All(g => g.Keyword != null || g.SecurityAttack > 0)));
                continue;
            }
            if ((m = Regex.Match(p, @"^can't (?<what>attack or block|attack and block|block or attack|attack|block|digivolve|suspend|unsuspend|be suspended|become suspended)$", I)).Success)
            {
                string what = m.Groups["what"].Value.ToLowerInvariant();
                string field = what.Contains("attack") && what.Contains("block") ? "attack+block" : what.Contains("suspend") ? (what.StartsWith("un") ? "unsuspend" : "suspend") : what;
                list.Add(new Pred34(p, Array.Empty<Keyword>(), field, 0, null, false));
                continue;
            }
            var r = Restrictions34.FirstOrDefault(x => x.Pattern.IsMatch(p));
            if (r.Pattern == null) return null;
            list.Add(new Pred34(p, r.Keywords, null, 0, null, r.Good));
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>"Your opponent's effects don't affect this Digimon" 같은 뒤집힌 표기 → "this Digimon isn't affected by your opponent's effects" (주어 + 술어)</summary>
    internal static string NormalizeProtection34(string s)
    {
        string t = s.Trim().TrimEnd('.');
        Match m;
        // "give 1 of their Digimon ＜Security A. -1＞ and it can't activate ~" → "1 of their Digimon gains ＜Security A. -1＞ and can't activate ~"
        if ((m = Regex.Match(t, @"^give (?<t>.+?) (?<kws>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*) and (?:it|they) (?<rest>.+)$", I)).Success)
            t = $"{m.Groups["t"].Value} gains {m.Groups["kws"].Value} and {m.Groups["rest"].Value}";
        if ((m = Regex.Match(t, @"^(?:your opponent's|their|the opponent's) (?<dig>Digimon(?:'s)? )?effects (?:don't|do not|can't|cannot) affect (?<t>.+?)(?<and> and (?:it|they) .+)?$", I)).Success)
            return $"{m.Groups["t"].Value} isn't affected by {(m.Groups["dig"].Success ? "the effects of your opponent's Digimon" : "your opponent's effects")}{AndIt34(m.Groups["and"].Value)}";
        if ((m = Regex.Match(t, @"^(?:your opponent's|their) Option cards?(?:'s)? effects (?:don't|do not) affect (?<t>.+)$", I)).Success)
            return $"{m.Groups["t"].Value} isn't affected by the effects of your opponent's Option cards";
        if ((m = Regex.Match(t, @"^(?:your opponent's|their) effects can't delete (?<t>.+?)(?<ret> or return (?:it|them) to (?:the |its owner's |their owners'? |their )?hands? or (?:the )?decks?)?$", I)).Success)
            return $"{m.Groups["t"].Value} can't be deleted by your opponent's effects{(m.Groups["ret"].Success ? " and can't be returned to the hand or deck by your opponent's effects" : "")}";
        if ((m = Regex.Match(t, @"^(?:your opponent's|their) effects can't return (?<t>.+?) to (?:the |their |its owner's |their owners'? )?hands? or (?:the )?decks?$", I)).Success)
            return $"{m.Groups["t"].Value} can't be returned to the hand or deck by your opponent's effects";
        if ((m = Regex.Match(t, @"^(?:your opponent's|their) effects can't reduce (?:the DP of (?<t>.+)|(?<t2>.+?)'s DP)(?<ret> or return (?:it|them) to (?:the |its owner's |their owners'? )?hands? or (?:the )?decks?)?$", I)).Success)
        {
            string who = m.Groups["t"].Success ? m.Groups["t"].Value : m.Groups["t2"].Value;
            return $"{who} can't have its DP reduced by your opponent's effects{(m.Groups["ret"].Success ? " and can't be returned to the hand or deck by your opponent's effects" : "")}";
        }
        return t;
    }

    private static string AndIt34(string and) =>
        and.Length == 0 ? "" : Regex.Replace(and, @"^ and (?:it|they) ", " and ", I);

    /// <summary>주어 + 술어로 나눈다 ("1 of your Digimon with [X] in its name can't be deleted in battle" → 주어, 술어). 없으면 null</summary>
    internal static (string Subject, string Preds)? SplitSubject34(string core)
    {
        foreach (Match v in PredStart34.Matches(core))
        {
            if (v.Index == 0 || core[v.Index - 1] != ' ') continue;
            string subj = core[..(v.Index - 1)].Trim();
            if (subj.Length == 0) continue;
            return (subj, core[v.Index..]);
        }
        return null;
    }

    /// <summary>"None of their Digimon can unsuspend" → "All of their Digimon can't unsuspend" · "Your opponent's Digimon ~" → "All of your opponent's Digimon ~"</summary>
    internal static string NormalizeSubjectForms34(string t)
    {
        Match m;
        if ((m = Regex.Match(t, @"^None of (?<x>.+?) (?:can|could) (?<rest>.+)$", I)).Success && !m.Groups["x"].Value.Contains(" can ", StringComparison.OrdinalIgnoreCase))
            return $"All of {m.Groups["x"].Value} can't {m.Groups["rest"].Value}";
        if ((m = Regex.Match(t, @"^None of (?<x>.+?) (?:are|is) (?<rest>affected .+)$", I)).Success)
            return $"All of {m.Groups["x"].Value} aren't {m.Groups["rest"].Value}";
        if ((m = Regex.Match(t, @"^(?<x>Your opponent's (?:[^,]*? )?Digimon(?: with [^,]+?)?) (?<rest>can't .+)$", I)).Success)
            return $"All of {Regex.Replace(m.Groups["x"].Value, @"^Your ", "your ")} {m.Groups["rest"].Value}";
        return t;
    }

    // =====================================================================
    // 동작
    // =====================================================================

    /// <summary>동작 표기 정리 → 원래 규칙 (바뀌지 않으면 그대로)</summary>
    internal static string NormalizeAction34(string s)
    {
        string t = s.Trim().TrimEnd('.').Trim();
        t = Regex.Replace(t, @"\buntil the end your opponent's next turn$", "until the end of your opponent's next turn", I);
        t = Regex.Replace(t, @"\buntil the end of your opponents turn$", "until the end of your opponent's turn", I);
        t = Regex.Replace(t, @"\buntil (?:your opponent's|their) next end of turn$", "until the end of your opponent's next turn", I);
        t = Regex.Replace(t, @"\buntil the next time your opponent's turn ends$", "until your opponent's turn ends", I);
        t = Regex.Replace(t, @"^Until the end of opponent's turn, ", "Until the end of your opponent's turn, ", I);
        t = Regex.Replace(t, @"\b(?<n>[+-]\d+)DP\b", "${n} DP", I);
        t = Regex.Replace(t, @"\bDiigmon\b", "Digimon", I);
        t = Regex.Replace(t, @"^Unsuspended\b", "Unsuspend", I);
        t = Regex.Replace(t, @"^(?<t>this Digimon|it|1 of your [^,]+?) gains (?=[+-]\d+ DP\b)", "${t} gets ", I);
        // 상대가 패를 버린다: "They trash 1 card in their hand" · "Trash 1 card in their hand" · "they must trash 1 card in their hand" · "they trash 1 of them"(패)
        t = Regex.Replace(t, @"^(?:They|Your opponent) (?:must )?trash(?:es)? (?<n>\d+) cards? in their hand$", "Your opponent trashes ${n} cards in their hand", I);
        t = Regex.Replace(t, @"^Trash (?<n>\d+) cards? in their hand$", "Your opponent trashes ${n} cards in their hand", I);
        t = Regex.Replace(t, @"^They trash (?<n>\d+) of them$", "Your opponent trashes ${n} cards in their hand", I);
        return t;
    }

    internal static readonly Regex PlaceEach34 = new(
        @"^Place (?:1 )?(?<names>\[[^\]]+\](?:,? (?:and )?(?:1 )?\[[^\]]+\])+) from your (?<from>trash|hand and/or trash|hand or trash|hand) (?:under (?<who>this Digimon|that Digimon|it) (?:in any order )?as its bottom digivolution cards|in (?:this card's|this Digimon's) digivolution cards|as (?<who>that Digimon's|this Digimon's|its) bottom digivolution cards)(?: in any order)?$",
        I | RegexOptions.Compiled);

    /// <summary>
    /// 7차 규칙보다 먼저 읽는 문장: "Place 1 [Raijinmon], [Fujinmon], and [Suijinmon] from your trash under this Digimon in any order as its bottom digivolution cards" —
    /// 이름마다 1장씩 (공식 BT9-067 "「뇌신몬」과 「풍신몬」과 「수신몬」 1장씩을 ~ 놓는다. 놓은 1장마다 메모리+1")
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? PreAction34(string raw, CardData card, List<EffectTrigger> triggers)
    {
        var m = PlaceEach34.Match(raw.Trim().TrimEnd('.'));
        if (!m.Success) return null;
        string label = triggers.Count > 0 && Labels.TryGetValue(triggers[0], out var l) ? l : "효과";
        var names = TargetSpec.Names(m.Groups["names"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string from = m.Groups["from"].Value.ToLowerInvariant();
        bool hand = from.Contains("hand"), trash = from.Contains("trash");
        bool that = m.Groups["who"].Value.StartsWith("that", StringComparison.OrdinalIgnoreCase) || m.Groups["who"].Value.Equals("it", StringComparison.OrdinalIgnoreCase);
        return (c =>
        {
            var host = that ? c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d)) ?? c.PlayedThisEffect.FirstOrDefault(d => c.Owner.BattleArea.Contains(d)) ?? c.Self : c.Self;
            if (host == null || !c.Owner.BattleArea.Contains(host)) { c.LastStepResolved = false; c.PaidCount = 0; Emit(c, label, "놓을 디지몬이 없습니다"); return; }
            int placed = 0;
            foreach (var name in names)
            {
                var pool = new List<PoolCard>();
                if (trash) pool.AddRange(PoolFrom(c.Owner.Trash, x => CardNames.Is(x, name)));
                if (hand) pool.AddRange(PoolFrom(c.Owner.Hand, x => CardNames.Is(x, name) && !ReferenceEquals(x, c.Card)));
                var p = pool.FirstOrDefault();
                if (p.Card == null || !TakeFromPool(c, p)) continue;
                host.AddBottomSource(p.Card);
                placed++;
            }
            c.PaidCount = placed;
            c.LastTargets = new List<CardInstance> { host };
            if (placed == 0) c.LastStepResolved = false;
            c.RecalculateFieldBuffs();
            Emit(c, label, $"진화원 아래에 {placed}장 놓았습니다");
        }, null);
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction34(string raw, CardData card, List<EffectTrigger> triggers)
    {
        string s = NormalizeAction34(raw);
        if (s != raw.Trim().TrimEnd('.') && ParseAction(s, card, triggers) is { Error: null } re) return re;
        string label = triggers.Count > 0 && Labels.TryGetValue(triggers[0], out var l) ? l : "효과";
        bool security = triggers.Contains(EffectTrigger.Security);
        Match m;

        // ---- 상대가 고르는 동작 ----
        // "Your opponent may trash 1 card in their hand" · "~ 1 Digimon card ~" · "~ 1 Tamer card or Option card ~" (공식 BT15-074: "상대는 자신의 패의 디지몬 카드 1장을 파기할 수 있다")
        if ((m = Regex.Match(s, @"^Your opponent may trash (?<pick>\d+ .*?cards?) in their hand$", I)).Success
            && ParseCardPick(m.Groups["pick"].Value) is { } opick)
        {
            var (n, match) = opick;
            return (c =>
            {
                int k = c.Engine.OpponentTrashesFromHand(c.Enemy, n, match, optional: true, $"{c.Card.Name} {label}");
                c.PaidCount = k;
                c.LastStepResolved = k > 0;
                Emit(c, label, k > 0 ? $"상대가 패 {k}장을 파기했습니다" : "상대가 패를 파기하지 않았습니다");
            }, null);
        }
        // "Trash 1 card in your opponent's hand without looking" (무작위) · "Look at your opponent's hand and trash 1 card in it" · "Search your opponent's hand, choose 1 card, and trash it" (효과 주인이 고른다)
        if ((m = Regex.Match(s, @"^(?:Trash (?<n>\d+) cards? (?:in|from) your opponent's hand without looking|Look at your opponent's hand and trash (?<n>\d+) cards? (?:in|from) it|Search your opponent's hand, choose (?<n>\d+) cards?, and trash (?:it|them))$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            bool random = s.Contains("without looking", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                int k = c.Engine.OpponentTrashesFromHand(c.Enemy, n, _ => true, optional: false, $"{c.Card.Name} {label}", random, chooser: c.Owner);
                c.PaidCount = k;
                c.LastStepResolved = k > 0;
                Emit(c, label, k > 0 ? $"상대의 패 {k}장을 파기했습니다" : "상대의 패가 없습니다");
            }, null);
        }
        if (Regex.IsMatch(s, @"^Your opponent may trash their top security card$", I))
            return (c =>
            {
                bool did = c.Engine.OpponentMayTrashTopSecurity(c.Enemy, $"{c.Card.Name} {label}");
                c.LastStepResolved = did;
                Emit(c, label, did ? "상대가 시큐리티 위 1장을 파기했습니다" : "상대가 시큐리티를 파기하지 않았습니다");
            }, null);
        if (Regex.IsMatch(s, @"^Your opponent may delete (?:1|one) of their Digimon or Tamers$", I))
            return (c =>
            {
                bool did = c.Engine.OpponentMayDeleteOwnPiece(c.Enemy, $"{c.Card.Name} {label}");
                c.LastStepResolved = did;
                c.LastDidDelete34 = did;
                // 원래 규칙의 "If this effect didn't delete"(대상이 떠났는지)도 같게 — 소멸했으면 필드에 없는 표시 1개
                c.LastTargets = did ? new List<CardInstance> { new(new CardData { CardId = "DELETED", Name = "DELETED" }, 0) } : new List<CardInstance>();
                Emit(c, label, did ? "상대가 자신의 디지몬 · 테이머 1장을 소멸시켰습니다" : "상대가 소멸시키지 않았습니다");
            }, null);
        if (Regex.IsMatch(s, @"^(?:Your opponent|They) adds? (?:the top card of their security stack|their top security card) to (?:the|their) hand$", I))
            return (c =>
            {
                var x = c.Engine.AddTopSecurityToHand(c.Enemy);
                if (x == null) { c.LastStepResolved = false; Emit(c, label, "상대의 시큐리티가 없습니다"); return; }
                Emit(c, label, "상대는 시큐리티 위 1장을 패에 추가했습니다");
            }, null);

        // ---- 어택 종료 ----
        if (Regex.IsMatch(s, @"^End (?:the|that|this) attack$", I))
            return (c =>
            {
                if (c.Engine.EndCurrentAttack()) Emit(c, label, "어택을 종료합니다");
                else { c.LastStepResolved = false; Emit(c, label, "종료할 어택이 없습니다"); }
            }, null);

        // ---- 배틀 (어택이 아닌) ----
        // "This Digimon may battle 1 of your opponent's Digimon" · "1 of your Digimon with [Dracomon] in its text may battle 1 of your opponent's Digimon"
        if ((m = Regex.Match(s, @"^(?<a>this Digimon|it|(?:1|one) of your .+?) (?<may>may )?battle (?<t>(?:1|one) of (?:your opponent's|their) .+)$", I)).Success)
        {
            string aText = m.Groups["a"].Value;
            var mine = Regex.IsMatch(aText, @"^(?:this Digimon|it)$", I) ? null : TargetSpec.Parse(aText);
            if (mine == null && !Regex.IsMatch(aText, @"^(?:this Digimon|it)$", I)) return null;
            if (mine != null && (mine.Opponent || mine.AnySide)) return null;
            var theirs = TargetSpec.Parse(m.Groups["t"].Value);
            if (theirs == null || !theirs.Opponent) return null;
            bool may = m.Groups["may"].Success;
            bool itRef = aText.Equals("it", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                CardInstance? a = mine != null ? Pick(mine, c, security).FirstOrDefault()
                    : itRef && c.LastTargets.FirstOrDefault() is { } lt && c.Owner.BattleArea.Contains(lt) ? lt : c.Self;
                if (a == null || !c.Owner.BattleArea.Contains(a)) { c.LastStepResolved = false; Emit(c, label, "배틀할 디지몬이 없습니다"); return; }
                var pool = theirs.Resolve(c, null);
                var winning = pool.Where(d => d.GetCurrentDP() < a.GetCurrentDP()).OrderByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).FirstOrDefault();
                if (pool.Count == 0 || (may && !c.Engine.AskYesNo(c.Owner, a, $"[{c.Card.Name}] [{a.TopCard.Name}]으로 상대의 디지몬과 배틀할까요?", winning != null)))
                { c.LastStepResolved = false; Emit(c, label, "배틀하지 않았습니다"); return; }
                var d = c.Engine.ChooseTargets(c.Owner, pool.OrderByDescending(x => ReferenceEquals(x, winning)).ToList(), 1, $"[{c.Card.Name}] [{a.TopCard.Name}]과(와) 배틀할 상대의 디지몬을 고르세요.", optional: false, preselected: winning).FirstOrDefault();
                if (d == null) { c.LastStepResolved = false; return; }
                c.LastTargets = new List<CardInstance> { d };
                Emit(c, label, c.Engine.EffectBattle(a, d));
            }, null);
        }

        // ---- 패 · 덱 ----
        // "Trash up to 3 cards in your hand" · "Trash up to 2 blue cards in your hand" · "Trash 1 [Hybrid] or [Ten Warriors] trait card from your hand"
        if ((m = Regex.Match(s, @"^Trash (?<up>up to )?(?<pick>\d+ .+?) (?:in|from) your hand$", I)).Success
            && ParseCardPick(m.Groups["pick"].Value) is { } hpick)
        {
            var (n, match) = hpick;
            bool upTo = m.Groups["up"].Success;
            return (c =>
            {
                var pool = c.Owner.Hand.Where(x => match(x) && !ReferenceEquals(x, c.Card)).OrderBy(x => x.Type == CardType.Option ? 1 : 0).ThenBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
                // AI · 테스트: "up to"면 패를 2장은 남긴다
                int want = upTo && !c.OwnerIsPlayer ? Math.Min(n, Math.Max(0, c.Owner.Hand.Count - 2)) : n;
                var picks = pool.Count == 0 || want <= 0 ? new List<CardData>()
                    : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, Math.Min(c.OwnerIsPlayer ? n : want, pool.Count), upTo, $"[{c.Card.Name}] 파기할 패를 고르세요{(upTo ? $" ({n}장까지)" : "")}.");
                foreach (var x in picks) { c.Owner.Hand.Remove(x); c.Owner.Trash.Add(x); }
                c.Engine.NotifyHandTrashed(c.Owner, picks);
                c.PaidCount = picks.Count;
                if (picks.Count == 0) c.LastStepResolved = false;
                Emit(c, label, picks.Count > 0 ? $"패 {picks.Count}장을 파기했습니다" : "파기한 패가 없습니다");
            }, null);
        }
        // "Trash up to 3 cards from the top of your deck" · "Trash 3 cards from the top of your deck" · "Trash their deck's top 5 cards"
        if ((m = Regex.Match(s, @"^Trash (?<up>up to )?(?<n>\d+) cards? from the top of (?<who>your|your opponent's) deck$|^Trash (?<who>their) deck's top (?<n>\d+) cards?$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            bool upTo = m.Groups["up"].Success, enemy = !m.Groups["who"].Value.Equals("your", StringComparison.OrdinalIgnoreCase);
            if (!upTo) return ParseAction(enemy ? $"Trash the top {n} cards of your opponent's deck" : $"Trash the top {n} cards of your deck", card, triggers);
            return (c =>
            {
                var deck = c.Owner.MainDeck;
                int k = Math.Min(n, deck.Count);
                if (c.OwnerIsPlayer && k > 0)
                    k = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, deck.Take(k).ToList(), k, upTo: true, $"[{c.Card.Name}] 덱 위에서부터 파기할 카드를 고르세요 (위에서부터 고른 수만큼 파기합니다).").Count;
                c.Engine.Mill(c.Owner, k, $"{c.Card.Name} {label}");
                c.PaidCount = k;
                if (k == 0) c.LastStepResolved = false;
                Emit(c, label, $"덱 위에서부터 {k}장 파기");
            }, null);
        }
        // "Return 1 red Digimon card from your trash to the top of the deck" · "Return up to 3 Digimon cards with [Mamemon] in their texts from your trash to the top of the deck"
        if ((m = Regex.Match(s, @"^Return (?<up>up to )?(?<pick>\d+ .+?) from your (?<from>hand and/or trash|hand or trash|trash) to the top of (?:the|your) deck(?: in any order)?$", I)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } tpick)
        {
            var (n, match) = tpick;
            bool upTo = m.Groups["up"].Success, fromHand = m.Groups["from"].Value.Contains("hand", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                var pool = PoolFrom(c.Owner.Trash, x => match(x) && x.Type != CardType.Digitama).ToList();
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, x => match(x) && x.Type != CardType.Digitama && !ReferenceEquals(x, c.Card)));
                if (pool.Count == 0 || (!upTo && pool.Count < 1)) { c.LastStepResolved = false; Emit(c, label, "되돌릴 카드가 없습니다"); return; }
                var picks = ChooseFromPool(c, fromHand ? CardChoiceZone.HandOrTrash : CardChoiceZone.Trash, pool.OrderByDescending(x => x.Card.Level).ToList(), Math.Min(n, pool.Count), $"[{c.Card.Name}] 덱 위로 되돌릴 카드를 고르세요.");
                var moved = new List<CardData>();
                foreach (var p in picks) if (TakeFromPool(c, p)) moved.Add(p.Card);
                c.Owner.MainDeck.InsertRange(0, moved);
                c.Engine.NotifyTrashMoved(c.Owner, moved.Where(x => !c.Owner.Hand.Contains(x)).ToList(), toHand: false);
                c.PaidCount = moved.Count;
                if (moved.Count == 0) c.LastStepResolved = false;
                Emit(c, label, $"카드 {moved.Count}장을 덱 위로 되돌렸습니다");
            }, null);
        }

        // ---- "~ for each card trashed / placed / returned" — 앞의 비용 · 동작이 옮긴 매수 ----
        if ((m = Regex.Match(s, @"^(?<core>.+?) for each (?:card|security card|one) (?:(?:this effect )?(?:trashed|placed|returned)(?: (?:this way|by this effect|with this effect))?|you return this way)(?<dur> for the turn| until .+)?$", I)).Success
            || (m = Regex.Match(s, @"^(?<core>.+?) for each card this effect (?:trashed|placed|returned)(?<dur> for the turn| until .+)?$", I)).Success)
        {
            string core = m.Groups["core"].Value + m.Groups["dur"].Value;
            core = char.ToUpperInvariant(core[0]) + core[1..];
            var dpm = Regex.Match(core, @"^(?<t>.+?) gets? (?<sign>[+-])(?<n>\d+) DP(?<rest>.*)$", I);
            if (dpm.Success)
            {
                if (ParseAction(core, card, triggers).Error != null) return null;
                int unit = int.Parse(dpm.Groups["n"].Value);
                var byK = new Dictionary<int, Action<EffectContext>?>();
                return (c =>
                {
                    int k = Math.Min(c.PaidCount, 20);
                    if (k <= 0) { c.LastStepResolved = false; return; }
                    if (!byK.TryGetValue(k, out var a))
                        byK[k] = a = ParseAction($"{dpm.Groups["t"].Value} gets {dpm.Groups["sign"].Value}{unit * k} DP{dpm.Groups["rest"].Value}", card, triggers).Act;
                    a?.Invoke(c);
                }, null);
            }
            var inner = ParseAction(core, card, triggers);
            if (inner.Error != null) return null;
            var innerAct = inner.Act!;
            return (c =>
            {
                int k = Math.Min(c.PaidCount, 20);
                if (k <= 0) { c.LastStepResolved = false; return; }
                for (int i = 0; i < k; i++) innerAct(c);
            }, null);
        }

        if (MoreAction34(s, card, triggers, label, security) is { } more) return more;   // SentenceParser.Batch34b.cs
        // ---- 제한 · 보호 · DP · 키워드 (기간) ----
        if (TimedPreds34(s, card, label, security) is { } timed) return timed;
        return null;
    }

    /// <summary>
    /// "Until your opponent's turn ends, this Digimon can't be deleted in battle" · "1 of your Digimon gains ＜Blocker＞ and can't be deleted by your opponent's effects until ~" ·
    /// "Your opponent's effects don't affect this Digimon until their turn ends" · "1 of your opponent's Digimon can't attack players until the end of their turn" 등
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? TimedPreds34(string s, CardData card, string label, bool security)
    {
        var dur = ParseDuration(s, out string core);
        string before = core;
        core = NormalizeSubjectForms34(NormalizeProtection34(core));
        if (SplitSubject34(core) is not { } split) return null;
        var preds = ParsePreds34(split.Preds);
        if (preds == null) return null;
        // 새로 읽는 것은 키워드 · 기간 필드가 하나 이상 있거나 주어 표기를 바꾼 문장만 (DP · 키워드뿐인 것은 원래 규칙)
        if (!preds.Any(p => p.Keywords.Length > 0) && !(preds.Count > 1 && preds.Any(p => p.Field != null)) && !(core != before && preds.Any(p => p.Field != null))) return null;
        if (dur == Duration.None)
        {
            // 기간 없는 제한 · 보호는 이 턴 동안 ("This Digimon can't be blocked" 같은 [어택 시])
            if (preds.Any(p => p.Field != null)) return null;
            dur = Duration.ThisTurn;
        }
        if (dur == Duration.ThisAttack) return null;
        string subj = split.Subject;
        bool self = Regex.IsMatch(subj, @"^(?:this Digimon|it|that Digimon|they|them|the Digimon this effect digivolved|the Digimon that digivolved with this effect)$", I);
        TargetSpec? target = self ? TargetSpec.Parse(Regex.IsMatch(subj, @"^this Digimon$", I) ? "this Digimon" : "it") : TargetSpec.Parse(subj);
        if (target == null) return null;
        bool good = preds.All(p => p.Good);
        target.PreferOwnSide = good;
        var list = preds;
        return (c =>
        {
            int until = Until(c, dur);
            var targets = Pick(target, c, security);
            foreach (var d in targets)
            {
                foreach (var p in list)
                {
                    foreach (var kw in p.Keywords) d.TimedKeywords.Add((kw, until));
                    if (p.Grants != null)
                        foreach (var (k, sa) in p.Grants)
                        {
                            if (k is { } kw2) d.TimedKeywords.Add((kw2, until));
                            else d.TimedSecurityAttackBuffs.Add((sa, until));
                        }
                    switch (p.Field)
                    {
                        case "attack": d.CannotAttackUntil = Math.Max(d.CannotAttackUntil, until); break;
                        case "block": d.CannotBlockUntil = Math.Max(d.CannotBlockUntil, until); break;
                        case "attack+block": d.CannotAttackUntil = Math.Max(d.CannotAttackUntil, until); d.CannotBlockUntil = Math.Max(d.CannotBlockUntil, until); break;
                        case "digivolve": d.CannotDigivolveUntil = Math.Max(d.CannotDigivolveUntil, until); break;
                        case "suspend": d.CannotSuspendUntil = Math.Max(d.CannotSuspendUntil, until); break;
                        case "unsuspend": d.CannotUnsuspendUntil = Math.Max(d.CannotUnsuspendUntil, until); break;
                    }
                }
            }
            c.RecalculateFieldBuffs();
            foreach (var d in targets)
                foreach (var p in list.Where(p => p.Dp != 0))
                    if (c.Owner.BattleArea.Contains(d) || c.Enemy.BattleArea.Contains(d))
                        c.Engine.ApplyDpChange(d, p.Dp, until, $"{c.Card.Name} [{label}]");
            if (targets.Count == 0) c.LastStepResolved = false;
            Emit(c, label, targets.Count > 0 ? $"[{string.Join(", ", targets.Select(d => d.TopCard.Name))}] {string.Join(" · ", list.Select(p => p.Text))}" : "대상이 없습니다");
        }, null);
    }

    // =====================================================================
    // 지속 효과
    // =====================================================================

    /// <summary>
    /// 지속 효과 (원래 규칙이 읽지 못한 것만): "[Your Turn] This Digimon can't attack players." · "[All Turns] This Digimon can't be deleted by your opponent's effects." ·
    /// "[Your Turn] This Digimon with the [Xros Heart] trait gains ＜Rush＞." · "[All Turns] This suspended Digimon isn't affected by your opponent's Digimon's effects." ·
    /// "[Your Turn] This Digimon with 4 or more digivolution cards gets +2000 DP." · "[Opponent's Turn] All of your Digimon with [Etemon] in their names gain ＜Blocker＞, and can't be returned to the hand or deck."
    /// </summary>
    private static string? ParseStatic34(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer)
    {
        var sentences = SplitSentences(body);
        if (sentences.Count != 1) return "여러 문장";
        string s = NormalizeAction34(sentences[0]);
        Func<StaticContext, bool> turn = qualifier switch
        {
            Qualifier.YourTurn => c => c.OwnersTurn,
            Qualifier.OpponentsTurn => c => !c.OwnersTurn,
            _ => outer ?? (_ => true),
        };
        Func<StaticContext, bool> cond = turn;
        Match m0;
        var w = Regex.Match(s, @"^(?:While|If) (?<cond>.+?), (?<rest>.+)$", I);
        if (w.Success)
        {
            var parsed = ParseStaticCondition(w.Groups["cond"].Value);
            if (parsed == null) return "조건";
            cond = c => turn(c) && parsed(c);
            s = w.Groups["rest"].Value;
        }
        // "This Digimon can only digivolve into white Digimon" · "~ into [Apocalymon]" · "~ into Digimon cards with [Lucemon] in their name" (공식 BT15-031: "이 디지몬은 화이트인 디지몬으로만 진화할 수 있다")
        if (!w.Success && (m0 = Regex.Match(s, @"^this Digimon can only digivolve into (?<what>.+)$", I)).Success && card.Type == CardType.Digimon
            && OnlyIntoFilter34(m0.Groups["what"].Value) is { } onlyInto)
        {
            bool yourTurn = qualifier == Qualifier.YourTurn;
            if (qualifier == Qualifier.OpponentsTurn) return "상대의 턴 진화 제한";
            pending.Registrations.Add(d => d.EvoOnlyInto(inherited ? EffectSource.Inherited : EffectSource.Main, yourTurn, onlyInto));
            return null;
        }
        // "Your opponent can't gain memory other than by Tamer effects" (이 디지몬이 있는 동안)
        if (Regex.IsMatch(s, @"^your opponent can't gain memory (?:other than|except) (?:by|with|through) Tamer(?:s'|'s)? effects$", I) && card.Type == CardType.Digimon)
        {
            var c1 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, inherited ? EffectSource.Inherited : EffectSource.Main, c => c1(c) ? 1 : 0, Keyword.OpponentMemoryTamerOnly));
            return null;
        }
        // v0.35.0 "Players can't gain memory other than by Tamer effects" (BT25-079 · EX13-050) — 양쪽 플레이어 모두 (이 디지몬이 있는 동안)
        if (Regex.IsMatch(s, @"^players can't gain memory (?:other than|except) (?:by|with|through) Tamer(?:s'|'s)? effects$", I) && card.Type == CardType.Digimon)
        {
            var cPlayers = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, inherited ? EffectSource.Inherited : EffectSource.Main, c => cPlayers(c) ? 1 : 0, Keyword.PlayersMemoryTamerOnly));
            return null;
        }
        if (ParseStatic36(s, inherited, card, cond, pending) is { } static36) return static36.Length == 0 ? null : static36;   // v0.36.0 플레이어 제한 (SentenceParser.Batch36.cs)
        s = NormalizeSubjectForms34(NormalizeProtection34(s));
        if (SplitSubject34(s) is not { } split) return "주어";
        var preds = ParsePreds34(split.Preds);
        if (preds == null) return "술어";
        if (preds.Any(p => p.Field is "suspend" or "unsuspend")) return "기간 없는 레스트 불가";
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        string subj = split.Subject;
        Match m;
        Func<CardInstance, bool>? selfFilter = null;
        Func<CardInstance, bool>? aura = null;
        bool excludeSource = false;
        if (Regex.IsMatch(subj, @"^(?:this Digimon|it|this card)$", I))
            selfFilter = _ => true;
        else if ((m = Regex.Match(subj, @"^this (?<adj>(?:(?:suspended|unsuspended|level \d+(?: or (?:lower|higher))?|\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*(?: trait)?|(?:" + Colors + @")(?: or (?:" + Colors + @"))*) )*)Digimon(?<with> (?:with|without|that has) .+)?$", I)).Success
                 && (m.Groups["adj"].Value.Length > 0 || m.Groups["with"].Success))
        {
            string adj = m.Groups["adj"].Value;
            string with = m.Groups["with"].Value;
            // "this [Glowing Dawn] trait Digimon" · "this [X], [Y] or [Z] trait Digimon" — 이름 앞의 특징 목록은 뒤로
            if (Regex.Match(adj, $@"^(?<x>{TargetSpec.NameList}) traits? $", I) is { Success: true } tx)
            { adj = ""; with = $" with the {tx.Groups["x"].Value} trait" + (with.Length > 0 ? " and" + Regex.Replace(with, @"^ with", "", I) : ""); }
            var spec = TargetSpec.Parse($"all of your {adj}Digimon{with}");
            if (spec == null || spec.ContextFilters.Count > 0) return "주어 조건";
            selfFilter = d => spec.Filters.All(f => f(d));
        }
        else if ((m = Regex.Match(subj, @"^(?:all of )?your (?<other>other )?(?<who>(?!opponent)(?:[^,]*? )?Digimon(?: (?:with|without) .+?)?)(?: in play)?$", I)).Success)
        {
            var spec = TargetSpec.Parse("all of your " + m.Groups["who"].Value);
            if (spec == null || spec.ContextFilters.Count > 0) return "주어 조건";
            aura = d => spec.Filters.All(f => f(d));
            excludeSource = m.Groups["other"].Success;
        }
        // "All of your [Diaboromon]" · "all of your other [Eosmon]" — 그 이름의 디지몬
        else if ((m = Regex.Match(subj, $@"^(?:all of )?your (?<other>other )?(?<x>{TargetSpec.NameList})s?(?: in play)?$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            if (xs.Any(KoreanEffectRenderer.IsTamerName33)) return "테이머를 디지몬으로 취급";   // 「마사루」 등 — 테이머를 디지몬으로 취급하는 모델이 없다
            aura = d => xs.Any(x => CardNames.Is(d.TopCard, x));
            excludeSource = m.Groups["other"].Success;
        }
        else return "주어";
        bool exclude = excludeSource;

        var c2 = cond;
        var auraSource = card.Type == CardType.Tamer ? EffectSource.Main : source;
        foreach (var p in preds)
        {
            var kws = new List<Keyword>(p.Keywords);
            var sas = new List<int>();
            if (p.Grants != null)
                foreach (var (k, sa) in p.Grants) { if (k is { } kk) kws.Add(kk); else sas.Add(sa); }
            if (p.Field is "attack" or "attack+block") kws.Add(Keyword.CannotAttack);
            if (p.Field is "block" or "attack+block") kws.Add(Keyword.CannotBlock);
            if (p.Field == "digivolve") kws.Add(Keyword.CannotDigivolve);
            if (selfFilter != null)
            {
                var f = selfFilter;
                foreach (var kw in kws) pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) && c.Digimon != null && f(c.Digimon) ? 1 : 0, kw));
                foreach (var sa in sas) pending.Registrations.Add(d => d.Static(StaticStat.SecurityAttack, source, c => c2(c) && c.Digimon != null && f(c.Digimon) ? sa : 0));
                if (p.Dp != 0) { int dp = p.Dp; pending.Registrations.Add(d => d.Static(StaticStat.Dp, source, c => c2(c) && c.Digimon != null && f(c.Digimon) ? dp : 0)); }
            }
            else
            {
                var subject = aura!;
                foreach (var kw in kws) pending.Registrations.Add(d => d.Static(StaticStat.AllyKeyword, auraSource, c => c2(c) ? 1 : 0, kw, subject, exclude));
                foreach (var sa in sas) pending.Registrations.Add(d => d.Static(StaticStat.AllySecurityAttack, auraSource, c => c2(c) ? sa : 0, subject: subject, excludeSource: exclude));
                if (p.Dp != 0) { int dp = p.Dp; pending.Registrations.Add(d => d.Static(StaticStat.AllyDigimonDp, auraSource, c => c2(c) ? dp : 0, subject: subject, excludeSource: exclude)); }
            }
        }
        return null;
    }

    // =====================================================================
    // 비용
    // =====================================================================

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost34(string cost, CardData card)
    {
        Match m;
        cost = cost.Trim();
        // "trashing any 2 of this Digimon's digivolution cards" · "choosing and trashing 1 of this Digimon's digivolution cards" — 진화원 중에서 고른다
        if ((m = Regex.Match(cost, @"^(?:trashing any (?<n>\d+) of this Digimon's digivolution cards|choosing and trashing (?<n>\d+) of this Digimon's digivolution cards)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && c.Self.EvolutionSources.Count >= n,
                    c => { c.PaidCount = TrashChosenSources34(c, new[] { c.Self! }, _ => true, n, upTo: false); if (c.PaidCount < n) c.CostFailed = true; },
                    c => c.Self != null && c.Self.EvolutionSources.Count > n,
                    $"이 디지몬의 진화원을 선택하여 {n}장 파기하여");
        }
        // "trashing any 1 [Mineral] or [Rock] trait card from your Digimon's digivolution cards" · "trashing 1 Option card from any of your Digimon's digivolution cards" ·
        // "trashing up to 3 [Mineral] or [Rock] trait cards from any of your Digimon's digivolution cards" · "trashing any 2 digivolution cards from your [Bagra Army] trait Digimon" ·
        // "trashing any 1 digivolution card of (1 of )?your [Bagra Army] trait Digimon"
        if ((m = Regex.Match(cost, @"^trashing (?:(?<up>up to )|any )?(?<pick>\d+ .+?) (?:from|in) (?:any of |1 of |one of )?(?<who>your Digimon's|your Digimon|this Digimon's) digivolution cards?$", I)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } spick)
        {
            var (n, match) = spick;
            bool upTo = m.Groups["up"].Success, self = m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase);
            IEnumerable<CardInstance> Hosts(EffectContext c) => self ? (c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? new[] { c.Self } : Array.Empty<CardInstance>()) : c.Owner.BattleArea;
            int Avail(EffectContext c) => Hosts(c).Sum(d => d.SourcesCount(match));
            return (c => Avail(c) >= (upTo ? 1 : n),
                    c => { c.PaidCount = TrashChosenSources34(c, Hosts(c).ToList(), match, n, upTo); if (!upTo && c.PaidCount < n) c.CostFailed = true; },
                    c => Avail(c) >= n,
                    $"진화원에서 카드 {(upTo ? $"{n}장까지" : $"{n}장")}을 파기하여");
        }
        if ((m = Regex.Match(cost, @"^trashing (?:any )?(?<n>\d+) digivolution cards? (?:from|of) (?:(?:1|one) of )?(?<t>your .+)$", I)).Success
            && TargetSpec.Parse("all of " + Regex.Replace(m.Groups["t"].Value, @"^your ", "your ", I)) is { Opponent: false, AnySide: false } hostSpec)
        {
            int n = int.Parse(m.Groups["n"].Value);
            List<CardInstance> Hosts(EffectContext c) => c.Owner.BattleArea.Where(d => hostSpec.Filters.All(f => f(d))).ToList();
            return (c => Hosts(c).Sum(d => d.EvolutionSources.Count) >= n,
                    c => { c.PaidCount = TrashChosenSources34(c, Hosts(c), _ => true, n, upTo: false); if (c.PaidCount < n) c.CostFailed = true; },
                    c => Hosts(c).Sum(d => d.EvolutionSources.Count) > n,
                    $"진화원을 선택하여 {n}장 파기하여");
        }
        // "returning 2 cards from your opponent's trash to the bottom of the deck" · "returning 1 Digimon card from your opponent's trash to the bottom of their deck" · "~ to the top of the deck"
        if ((m = Regex.Match(cost, @"^returning (?<pick>\d+ .+?) from (?:your opponent's|their) trash to the (?<pos>top|bottom) of (?:the|their|its owner's) deck$", I)).Success
            && ParseCardPick(m.Groups["pick"].Value) is { } opick)
        {
            var (n, match) = opick;
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            Func<CardData, bool> ok = x => match(x) && x.Type != CardType.Digitama;
            return (c => c.Enemy.Trash.Count(ok) >= n,
                    c =>
                    {
                        var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, c.Enemy.Trash.Where(ok).OrderByDescending(x => x.Level).ToList(), n, upTo: false,
                            $"[{c.Card.Name}] 비용으로 상대의 트래시에서 덱 {(top ? "위" : "아래")}로 되돌릴 카드 {n}장을 고르세요.").ToList();
                        foreach (var x in picked) c.Enemy.Trash.Remove(x);
                        if (top) c.Enemy.MainDeck.InsertRange(0, picked); else c.Enemy.MainDeck.AddRange(picked);
                        c.Engine.NotifyTrashMoved(c.Enemy, picked, toHand: false);
                        c.PaidCount = picked.Count;
                    },
                    _ => true, $"상대의 트래시의 카드 {n}장을 덱 {(top ? "위" : "아래")}로 되돌려");
        }
        // "returning 1 card with the [Hybrid] trait from this Digimon's digivolution cards to the hand" · "returning 1 [Imperialdramon: Dragon Mode] from this Digimon's digivolution cards to its owner's hand"
        if ((m = Regex.Match(cost, @"^returning (?<pick>\d+ .+?) (?:from|in) this Digimon's digivolution cards to (?:the|your|its owner's) hand$", I)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } rpick)
        {
            var (n, match) = rpick;
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && c.Self.SourcesCount(match) >= n,
                    c =>
                    {
                        var picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, c.Self!.SourcesWhere(match).OrderByDescending(x => x.Level).ToList(), n, upTo: false,
                            $"[{c.Card.Name}] 비용으로 패로 되돌릴 진화원 {n}장을 고르세요.");
                        foreach (var x in picks) { if (c.Self.TakeSource(s => ReferenceEquals(s, x)) is { } took) c.Owner.Hand.Add(took); }
                        c.PaidCount = picks.Count;
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"이 디지몬의 진화원에서 카드 {n}장을 패로 되돌려");
        }
        // "suspending 1 of your opponent's Digimon or Tamers" · "suspending 1 Digimon or Tamer"
        if ((m = Regex.Match(cost, @"^suspending (?:1|one) (?<who>of your opponent's |of their )?Digimon or Tamers?$", I)).Success)
        {
            bool enemyOnly = m.Groups["who"].Success;
            IEnumerable<CardInstance> Digis(EffectContext c) => (enemyOnly ? c.Enemy.BattleArea : c.Enemy.BattleArea.Concat(c.Owner.BattleArea))
                .Where(d => !d.IsSuspended && !EffectRules.IsSuspendLocked(c.Engine.State, d) && !c.Engine.IsUnaffectedBy(c.Owner, d));
            IEnumerable<(PlayerZones Z, CardData T)> Tams(EffectContext c) => (enemyOnly ? new[] { c.Enemy } : new[] { c.Enemy, c.Owner })
                .SelectMany(z => z.Tamers.Distinct().Where(z.HasActiveTamer).Select(t => (z, t)));
            return (c => Digis(c).Any() || Tams(c).Any(),
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Digis(c).OrderByDescending(x => c.Enemy.BattleArea.Contains(x)).ThenByDescending(x => x.GetCurrentDP()).ToList(), 1,
                            $"[{c.Card.Name}] 비용으로 레스트시킬 디지몬을 고르세요{(Tams(c).Any() ? " (고르지 않으면 테이머)" : "")}.", optional: Tams(c).Any(), preselected: null).FirstOrDefault();
                        if (d != null) { c.Engine.Suspend(d); return; }
                        var t = Tams(c).OrderByDescending(x => ReferenceEquals(x.Z, c.Enemy)).First();
                        c.Engine.SuspendTamer(t.Z, t.T);
                    },
                    _ => true, enemyOnly ? "상대의 디지몬 또는 테이머 1장을 레스트시켜" : "디지몬 또는 테이머 1장을 레스트시켜");
        }
        // "suspending it" · "unsuspending it" (앞의 그 디지몬) · "unsuspending 1 of your Digimon"
        if ((m = Regex.Match(cost, @"^(?<un>un)?suspending it$", I)).Success)
        {
            bool un = m.Groups["un"].Success;
            CardInstance? It(EffectContext c) => (c.LastTargets.FirstOrDefault() ?? c.Other) is { } d && (c.Owner.BattleArea.Contains(d) || c.Enemy.BattleArea.Contains(d)) ? d : null;
            return (c => It(c) is { } d && d.IsSuspended == un && (un ? !EffectRules.IsUnsuspendLocked(c.Engine.State, d) : !EffectRules.IsSuspendLocked(c.Engine.State, d)),
                    c => { if (un) c.Engine.Unsuspend(It(c)!); else c.Engine.Suspend(It(c)!); },
                    _ => true, un ? "그 디지몬을 액티브로 하여" : "그 디지몬을 레스트시켜");
        }
        if (Regex.IsMatch(cost, @"^unsuspending (?:1|one) of your Digimon$", I))
        {
            IEnumerable<CardInstance> Able(EffectContext c) => c.Owner.BattleArea.Where(d => d.IsSuspended && !EffectRules.IsUnsuspendLocked(c.Engine.State, d));
            return (c => Able(c).Any(),
                    c => { var d = c.Engine.ChooseTargets(c.Owner, Able(c).OrderByDescending(x => x.GetCurrentDP()).ToList(), 1, $"[{c.Card.Name}] 비용으로 액티브로 할 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault(); if (d != null) c.Engine.Unsuspend(d); },
                    _ => true, "자신의 디지몬 1마리를 액티브로 하여");
        }
        // "returning this Digimon to the hand" · "returning this Digimon to the bottom of the deck" (비용이라 막을 수 없다)
        if ((m = Regex.Match(cost, @"^returning this Digimon(?: with (?<x>.+?))? to (?:(?<hand>the|your|its owner's) hand|the bottom of (?:the|your|its owner's) deck)$", I)).Success)
        {
            bool hand = m.Groups["hand"].Success;
            Func<CardInstance, bool> ok = _ => true;
            if (m.Groups["x"].Success)
            {
                if (TargetSpec.Parse("all of your Digimon with " + m.Groups["x"].Value) is not { } xs || xs.ContextFilters.Count > 0) return null;
                ok = d => xs.Filters.All(f => f(d));
            }
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && ok(c.Self),
                    c => { if (hand) c.Engine.ReturnToHandAsCost34(c.Owner, c.Self!); else c.Engine.BottomDeckAsCost(c.Owner, c.Self!); },
                    _ => false, hand ? "이 디지몬을 패로 되돌려" : "이 디지몬을 덱 아래로 되돌려");
        }
        // "returning 1 of your Digimon to the bottom of the deck" · "returning 1 of your blue Digimon ~" · "returning 1 other suspended Digimon to the bottom of the deck"
        if ((m = Regex.Match(cost, @"^returning (?:1|one) (?:of your |(?<other>other ))?(?<spec>(?:[^,]*? )?Digimon(?: with [^,]+?)?) to the bottom of (?:the|your|its owner's) deck$", I)).Success
            && TargetSpec.Parse("all of your " + (m.Groups["other"].Success ? "other " : "") + m.Groups["spec"].Value) is { Opponent: false } bspec)
        {
            bool other = m.Groups["other"].Success;
            IEnumerable<CardInstance> Able(EffectContext c) => c.Owner.BattleArea.Where(d => bspec.Filters.All(f => f(d)) && (!other || !ReferenceEquals(d, c.Self)));
            return (c => Able(c).Any(),
                    c => { var d = c.Engine.ChooseTargets(c.Owner, Able(c).OrderBy(x => x.TopCard.Level).ThenBy(x => x.GetCurrentDP()).ToList(), 1, $"[{c.Card.Name}] 비용으로 덱 아래로 되돌릴 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault(); if (d != null) c.Engine.BottomDeckAsCost(c.Owner, d); },
                    c => Able(c).Any(d => d.TopCard.Level <= 3), "자신의 디지몬 1마리를 덱 아래로 되돌려");
        }
        // "returning 1 of your yellow Tamers to the hand" · "returning 1 of your [TS] trait Tamers to the bottom of the deck" · "returning 1 of your [Kristy Damon]s to the hand"
        if ((m = Regex.Match(cost, $@"^returning (?:1|one) of your (?:(?<color>{Colors}) |(?<x>{TargetSpec.NameList}) traits? )?Tamers to (?:(?<hand>the|your|its owner's) hand|the bottom of (?:the|your|its owner's) deck)$|^returning (?:1|one) of your (?<name>\[[^\]]+\])s? to (?:(?<hand>the|your|its owner's) hand|the bottom of (?:the|your|its owner's) deck)$", I)).Success)
        {
            bool hand = m.Groups["hand"].Success;
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            var traits = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            var names = m.Groups["name"].Success ? TargetSpec.Names(m.Groups["name"].Value) : null;
            bool Ok(CardData t) => t.Type == CardType.Tamer && (color == null || t.AllColors.Contains(color.Value)) && (traits == null || traits.Any(x => TargetSpec.HasTrait(t, x))) && (names == null || names.Any(x => CardNames.Is(t, x)));
            return (c => c.Owner.Tamers.Any(Ok),
                    c =>
                    {
                        var t = c.Owner.Tamers.Where(Ok).OrderBy(x => c.Owner.HasActiveTamer(x) ? 1 : 0).First();
                        c.Engine.MoveTamerAsCost34(c.Owner, t, hand);
                    },
                    _ => true, hand ? "자신의 테이머 1장을 패로 되돌려" : "자신의 테이머 1장을 덱 아래로 되돌려");
        }
        // "suspending up to 2 of your yellow Tamers" · "suspending up to 5 Tamers" (낸 매수는 PaidCount)
        if ((m = Regex.Match(cost, @"^suspending up to (?<n>\d+) Tamers$", I)).Success && SuspendAnyTamers36(int.Parse(m.Groups["n"].Value)) is { } anyTamers)
            return anyTamers;   // v0.36.0 "of your"가 없으면 양쪽의 테이머 (공식 Q&A BT17-048)
        if ((m = Regex.Match(cost, $@"^suspending up to (?<n>\d+) (?:of your )?(?:(?<color>{Colors}) )?Tamers$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            List<CardData> Able(EffectContext c) => c.Owner.Tamers.Distinct().Where(t => c.Owner.HasActiveTamer(t) && (color == null || t.AllColors.Contains(color.Value))).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var able = Able(c);
                        var picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, able, Math.Min(n, able.Count), upTo: true, $"[{c.Card.Name}] 비용으로 레스트시킬 테이머를 고르세요 ({n}명까지).");
                        foreach (var t in picks) c.Engine.SuspendTamer(c.Owner, t);
                        c.PaidCount = picks.Count;
                    },
                    _ => true, $"테이머 {n}명까지를 레스트시켜");
        }
        // "trashing the top 3 cards of your deck"
        if ((m = Regex.Match(cost, @"^trashing the top (?<n>\d+) cards of your deck$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => c.Owner.MainDeck.Count >= n,
                    c => { int before = c.Owner.MainDeck.Count; c.Engine.Mill(c.Owner, n, c.Card.Name); c.PaidCount = before - c.Owner.MainDeck.Count; },
                    c => c.Owner.MainDeck.Count >= n + 10, $"덱 위에서부터 {n}장 파기하여");
        }
        // "returning the top card of your security stack to your hand" (= 패에 추가) · "adding the top or bottom card of your security stack to the hand"
        if (Regex.IsMatch(cost, @"^returning (?:the top card of your security stack|your top security card) to (?:the|your) hand$", I))
            return ParseCost("adding the top card of your security stack to the hand", card);
        if (Regex.IsMatch(cost, @"^adding the top or bottom card of your security stack to (?:the|your) hand$", I))
            return (c => c.Owner.SecurityStack.Count > 0,
                    c =>
                    {
                        var sec = c.Owner.SecurityStack;
                        int i = sec.Count > 1 && !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 시큐리티 맨 위 카드를 패에 추가할까요? (아니오 = 맨 아래)", true) ? sec.Count - 1 : 0;
                        var x = sec[i]; sec.RemoveAt(i); c.Owner.Hand.Add(x);
                        c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: false);
                    },
                    c => c.Owner.SecurityStack.Count >= 3, "시큐리티 위 또는 아래 1장을 패에 추가하여");
        return null;
    }

    /// <summary>호스트 디지몬들의 진화원에서 match에 맞는 카드를 count장 골라 파기 (반환: 파기한 매수)</summary>
    private static int TrashChosenSources34(EffectContext c, IReadOnlyList<CardInstance> hosts, Func<CardData, bool> match, int count, bool upTo)
    {
        var pool = hosts.SelectMany(d => d.SourcesWhere(match).Select(x => (Host: d, Card: x))).ToList();
        if (pool.Count == 0) return 0;
        // 추천: 진화원 효과가 없는 카드 · Lv.이 낮은 카드부터
        var ranked = pool.OrderBy(p => p.Card.InheritedEffectText.Length > 0 ? 1 : 0).ThenBy(p => p.Card.Level).ToList();
        int want = Math.Min(count, ranked.Count);
        var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, ranked.Select(p => p.Card).ToList(), want, upTo, $"[{c.Card.Name}] 파기할 진화원을 고르세요{(upTo ? $" ({count}장까지)" : $" ({count}장)")}.");
        var trashed = new List<CardData>();
        foreach (var i in idx)
        {
            var (host, x) = ranked[i];
            if (host.TakeSource(s => ReferenceEquals(s, x)) is { } took)
            {
                c.Owner.Trash.Add(took);
                trashed.Add(took);
            }
        }
        if (trashed.Count > 0)
        {
            c.Engine.ApplyOverflowPublic34(c.Owner, trashed);
            c.RecalculateFieldBuffs();
        }
        return trashed.Count;
    }

    // =====================================================================
    // 등장 · 사용할 때 비용을 내면 코스트 감소 (절)
    // =====================================================================

    internal static readonly Regex PlayCostClause34 = new(
        @"^(?:\[[^\]]+\]\s*)*When (?:this card would be (?:(?<use>used)|played)(?: from (?:the|your) hand)?|you would (?:(?<use>use)|play) this (?:card|Digimon)(?: from (?:the|your) hand)?), by (?<cost>.+?), reduce (?:the|its|this card's) (?:play |use |memory )?cost by (?<n>\d+)(?<each> for each (?:one|card(?: placed| trashed| returned)?|returned card))?\.?$",
        I | RegexOptions.Compiled);

    /// <summary>
    /// "When this card would be played, by placing up to 3 [Dark Masters] trait cards with different names from your battle area or trash under it, reduce the play cost by 4 for each one." (BT15-102) ·
    /// "~ by placing 1 of each face-up [Dark Masters] trait card with different names from your security stack under this card, ~ for each card placed" (EX10-061) ·
    /// "When you would play this card from the hand, by placing up to 13 cards with ~ from your trash at the bottom of your deck, reduce the cost by 1 for each card" (BT17-060) ·
    /// "When this card would be used, by trashing cards from the top of your security stack, up to having 3 left, reduce the cost by 2 for each card trashed" (BT16-100) ·
    /// "When you would use this card, by placing 1 of your blue Digimon as the bottom digivolution card of 1 of your other blue Digimon, reduce the cost by 3" (BT12-102).
    /// 반환: null = 이 규칙이 아님, "" = 읽음, 그 밖 = 미지원 이유
    /// </summary>
    private static string? ClausePlayCost34(string clause, CardData card, CardScriptDefinition? def)
    {
        var m = PlayCostClause34.Match(clause);
        if (!m.Success) return null;
        int n = int.Parse(m.Groups["n"].Value);
        bool each = m.Groups["each"].Success;
        string costText = m.Groups["cost"].Value;
        var cost = PlayCostPay34(costText, card) ?? (each ? null : ParseCost(costText, card) is { } pc ? (pc.CanPay, pc.Pay, pc.Recommend, pc.Describe, (Func<EffectContext, int>?)null) : null);
        if (cost == null) return $"미지원 비용 (코스트 감소: by {costText})";
        var (canPay, pay, recommend, describe, preview) = cost.Value;
        def?.OptionalPlayCostReduction(new PlayCostOption(n, canPay, pay, recommend, describe, costText) { PerCount = each, PreviewCount = preview });
        return "";
    }

    /// <summary>등장 코스트 비용 (낸 매수를 EffectContext.PaidCount에) — PreviewCount = 미리보기용 낼 수 있는 매수</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe, Func<EffectContext, int>? Preview)? PlayCostPay34(string cost, CardData card)
    {
        Match m;
        // 배틀 에어리어 / 트래시 → 이 카드 아래 (명칭이 서로 다른)
        if ((m = Regex.Match(cost, @"^placing (?<up>up to )?(?<n>\d+) (?<spec>.+?) cards? with different names from your (?<from>battle area or trash|trash or battle area|trash|battle area) under (?:it|this card)$", I)).Success
            && ParseCardPick("1 " + m.Groups["spec"].Value + " card") is { } bpick)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var match = bpick.Match;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromTrash = from.Contains("trash"), fromField = from.Contains("battle");
            List<(CardData Card, CardInstance? Inst)> Pool(EffectContext c)
            {
                var pool = new List<(CardData, CardInstance?)>();
                if (fromTrash) pool.AddRange(c.Owner.Trash.Where(x => match(x) && !ReferenceEquals(x, c.Card)).Select(x => (x, (CardInstance?)null)));
                if (fromField) pool.AddRange(c.Owner.BattleArea.Where(d => match(d.TopCard) && !GoldfishEngine.IsToken(d.TopCard)).Select(d => (d.TopCard, (CardInstance?)d)));
                return pool;
            }
            int Distinct(EffectContext c) => Pool(c).Select(p => p.Card.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return (c => Distinct(c) >= 1,
                    c =>
                    {
                        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        int placed = 0;
                        // 트래시 먼저 (필드의 디지몬은 아낀다), 플레이어는 1장씩 고른다
                        for (int i = 0; i < n; i++)
                        {
                            var opts = Pool(c).Where(p => !used.Contains(p.Card.Name)).OrderBy(p => p.Inst != null ? 1 : 0).ThenBy(p => p.Card.Level).ToList();
                            if (opts.Count == 0) break;
                            var idx = c.Engine.ChooseCardIndexes(c.Owner, fromTrash && fromField ? CardChoiceZone.Other : fromTrash ? CardChoiceZone.Trash : CardChoiceZone.Other,
                                opts.Select(p => p.Card).ToList(), 1, upTo: true, $"[{c.Card.Name}] 이 카드 아래에 놓을 카드를 고르세요 ({placed + 1}/{n}, 고르지 않으면 그만).");
                            if (idx.Count == 0) break;
                            var (x, inst) = opts[idx[0]];
                            if (inst != null) { if (c.Engine.DetachDigimonAsCost(c.Owner, inst) is not { } top) continue; x = top; }
                            else if (!c.Owner.Trash.Remove(x)) continue;
                            c.Engine.PendingUnderPlayed.Add(x);
                            used.Add(x.Name);
                            placed++;
                        }
                        c.PaidCount = placed;
                    },
                    c => Pool(c).Any(p => p.Inst == null),
                    $"명칭이 서로 다른 카드를 {n}장까지 이 카드 아래에 놓아",
                    c => Math.Min(n, Pool(c).Where(p => p.Inst == null).Select(p => p.Card.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        }
        // 시큐리티의 앞면 카드 → 이 카드 아래 (1장씩 · 명칭이 서로 다른)
        if ((m = Regex.Match(cost, @"^placing 1 of each face-up (?<spec>.+?) cards? with different names from your security stack under (?:it|this card)$", I)).Success
            && ParseCardPick("1 " + m.Groups["spec"].Value + " card") is { } fpick)
        {
            var match = fpick.Match;
            List<CardData> Pool(EffectContext c) => GoldfishEngine.FaceUpCards(c.Owner).Where(match).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            return (c => Pool(c).Count > 0,
                    c =>
                    {
                        var picks = Pool(c);
                        foreach (var x in picks)
                        {
                            c.Owner.SecurityStack.Remove(x);
                            c.Owner.FaceUpSecurity.Remove(x);
                            c.Engine.PendingUnderPlayed.Add(x);
                            c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: false);
                        }
                        c.PaidCount = picks.Count;
                    },
                    _ => true, "시큐리티의 앞면 카드를 1장씩 이 카드 아래에 놓아", c => Pool(c).Count);
        }
        // 트래시 → 덱 아래 (up to N)
        if ((m = Regex.Match(cost, @"^placing up to (?<n>\d+) (?<pick>.+?) from your trash (?:at|on|to) the bottom of (?:the|your) deck$", I)).Success
            && (ParseCardPick("1 " + Regex.Replace(m.Groups["pick"].Value, @"^cards\b", "card", I)) ?? ParseCardPick("1 " + m.Groups["pick"].Value)) is { } dpick)
        {
            int n = int.Parse(m.Groups["n"].Value);
            Func<CardData, bool> match = x => dpick.Match(x) && x.Type != CardType.Digitama;
            return (c => c.Owner.Trash.Any(match),
                    c =>
                    {
                        var pool = c.Owner.Trash.Where(match).OrderBy(x => x.Level).ToList();
                        var picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, pool, Math.Min(n, pool.Count), upTo: true, $"[{c.Card.Name}] 덱 아래에 놓을 트래시의 카드를 고르세요 ({n}장까지).");
                        foreach (var x in picks) c.Owner.Trash.Remove(x);
                        c.Owner.MainDeck.AddRange(picks);
                        c.Engine.NotifyTrashMoved(c.Owner, picks, toHand: false);
                        c.PaidCount = picks.Count;
                    },
                    _ => true, $"트래시의 카드를 {n}장까지 덱 아래에 놓아", c => Math.Min(n, c.Owner.Trash.Count(match)));
        }
        // 시큐리티 위에서부터 파기 (N장 남을 때까지)
        if ((m = Regex.Match(cost, @"^trashing cards from the top of your security stack, up to having (?<n>\d+) left$", I)).Success)
        {
            int keep = int.Parse(m.Groups["n"].Value);
            return (c => c.Owner.SecurityStack.Count > keep,
                    c =>
                    {
                        int max = Math.Max(0, c.Owner.SecurityStack.Count - keep);
                        int want = c.OwnerIsPlayer ? max : Math.Min(max, Math.Max(1, (c.Card.PlayCost + 1) / 2));
                        if (c.OwnerIsPlayer && max > 0)
                            want = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, Enumerable.Range(1, max).Select(i => new CardData { CardId = "SEC-" + i, Name = $"시큐리티 {i}장 파기" }).ToList(), 1, upTo: true,
                                $"[{c.Card.Name}] 시큐리티를 위에서부터 몇 장 파기할지 고르세요 (1장마다 코스트 감소).") is { Count: > 0 } ix ? ix[0] + 1 : 0;
                        int done = 0;
                        for (int i = 0; i < want && c.Owner.SecurityStack.Count > keep; i++)
                        {
                            var x = c.Owner.SecurityStack[0];
                            c.Owner.SecurityStack.RemoveAt(0);
                            c.Owner.Trash.Add(x);
                            c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: true);
                            done++;
                        }
                        c.PaidCount = done;
                    },
                    c => c.Owner.SecurityStack.Count >= keep + 2, $"시큐리티를 {keep}장 남을 때까지 위에서부터 파기하여", c => Math.Max(0, c.Owner.SecurityStack.Count - keep));
        }
        // 트래시 → 덱 위 또는 아래 (N장, 고정)
        if ((m = Regex.Match(cost, @"^returning (?<n>\d+) (?<pick>.+?) from your trash to the top or bottom of (?:the|your) deck$", I)).Success
            && (ParseCardPick("1 " + Regex.Replace(m.Groups["pick"].Value, @"^cards\b", "card", I)) ?? ParseCardPick(m.Groups["n"].Value + " " + m.Groups["pick"].Value)) is { } tpick)
        {
            int n = int.Parse(m.Groups["n"].Value);
            Func<CardData, bool> match = x => tpick.Match(x) && x.Type != CardType.Digitama;
            return (c => c.Owner.Trash.Count(match) >= n,
                    c =>
                    {
                        var picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, c.Owner.Trash.Where(match).OrderBy(x => x.Level).ToList(), n, upTo: false, $"[{c.Card.Name}] 덱 위 또는 아래로 되돌릴 트래시의 카드 {n}장을 고르세요.");
                        bool top = c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 덱 위로 되돌릴까요? (아니오 = 덱 아래)", recommended: false);
                        foreach (var x in picks) c.Owner.Trash.Remove(x);
                        if (top) c.Owner.MainDeck.InsertRange(0, picks); else c.Owner.MainDeck.AddRange(picks);
                        c.Engine.NotifyTrashMoved(c.Owner, picks, toHand: false);
                        c.PaidCount = picks.Count;
                    },
                    c => c.Owner.Trash.Count(match) >= n, $"트래시의 카드 {n}장을 덱 위 또는 아래로 되돌려", null);
        }
        return null;
    }

    /// <summary>"Black also meets this card's color requirements." (LM-033 등 옵션) — 그 색의 자신의 디지몬 · 테이머로도 색 조건을 충족한다</summary>
    private static string? ClauseMisc34(string clause, CardData card, CardScriptDefinition? def)
    {
        var m = Regex.Match(clause, $@"^(?<c>{Colors}) also meets this card's colou?r requirements?\.?$", I);
        if (!m.Success) return null;
        if (card.Type != CardType.Option) return "미지원: 옵션이 아닌 카드의 색 조건";
        var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true);
        def?.IgnoreColorRequirementWhen(z => z.BattleArea.Any(d => d.TopCard.AllColors.Contains(col)) || (z.BreedingArea is { } b && b.TopCard.AllColors.Contains(col)) || z.Tamers.Any(t => t.AllColors.Contains(col)));
        return "";
    }

    // =====================================================================
    // 조건
    // =====================================================================

    private static Func<EffectContext, bool>? ParseEventCondition34(string cond)
    {
        string c0 = cond.Trim();
        // 앞 문장(상대가 고르는 동작)의 결과: "If they don't" · "If they didn't" · "If they do not trash a card" · "If your opponent didn't trash with this effect" · "If this effect didn't delete"
        if (Regex.IsMatch(c0, @"^this effect didn't delete$", I))
            return c => { var did = c.LastDidDelete34; c.LastDidDelete34 = null; return did is bool b ? !b : !c.LastTargets.Any(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d)); };
        if (Regex.IsMatch(c0, @"^(?:they don't|they didn't|they do not|they did not|they do not trash a card|your opponent didn't trash with this effect|your opponent didn't|this effect didn't (?:trash|play|add))$", I))
            return c => !c.LastStepResolved;
        if (Regex.IsMatch(c0, @"^(?:they do|they did|your opponent did|this effect (?:deleted|trashed))$", I))
            return c => c.LastStepResolved;
        return null;
    }
}
