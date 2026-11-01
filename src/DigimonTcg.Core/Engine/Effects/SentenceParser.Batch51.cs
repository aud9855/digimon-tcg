using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.51.0 미지원 문장 25차 묶음: 원래 규칙(1~24차)이 읽지 못한 절 · 문장 · 동작 · 조건 · 비용만 25차 규칙으로 다시 읽는다.
///  - 같은 뜻 다른 표기 (ClauseRules51): "and"로 이은 두 대상 · 효과 (BT20-085 · BT8-101 · EX8-064 · BT20-055 · BT16-062 · BT11-106 · BT12-100 · BT21-089 · EX4-074) ·
///    "You may ~ to ~" = 비용 (BT13-050 · BT13-084 · BT7-045 · EX2-055) · AD1-004 · ST22-13 · BT17-060 · BT6-065 · LM-018 · EX13-074 · EX9-068 · P-087 · AD1-020
///  - 비용 (ParseCost51): 진화원 / 트래시에서 등장 (BT24-060 · LM-026 · EX5-065) · 트래시의 이름 3장을 덱 아래로 (EX3-035) · 패 / 트래시의 옵션을 배틀 에어리어에 (P-179) ·
///    특징을 가진 배틀 에어리어의 옵션 파기 (BT19-086) · 테이머 아래에서 등장 (BT20-092) · 패의 테이머 공개 → 덱 아래 (BT12-048) · 패의 카드 공개 → 덱 위 (BT7-045) ·
///    트래시의 카드를 진화원 아래에 (EX2-055)
///  - 조건 (ParseEventCondition51): 이번 턴에 진화했다 (BT1-007) · Lv.6 진화원의 색 수 (BT9-067) · 이 효과로 DP N 이상이 소멸 (EX4-065) · 서로의 트래시 합계 (BT21-089) ·
///    "A, or if B" (EX4-027) · 「종말의 시계」 4장 (BT17-100) · 이 카드를 등장시킨 효과의 카드 (EX3-025 · EX3-036)
///  - 지속 조건 (ParseStaticCondition51): "~ in play, or [X] is in your battle area" (EX3-033)
///  - 동작 (ActionExtra51): 이 디지몬으로 어택 + 그 어택 동안 ≪시큐리티 어택 +N≫ (AD1-020) · 시큐리티 장수만큼 남기고 소멸 (BT19-094) · 모든 디지몬과 테이머 소멸 (BT5-110) ·
///    테이머 아래의 카드를 패로 (BT12-075) · 이 디지몬을 등장한 디지몬의 진화원 아래에 (LM-026) · 게임에 승리 (BT17-100)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch51.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth51;
    [ThreadStatic] private static int _fallbackDepth51;
    [ThreadStatic] private static int _gate51;
    internal static bool Gate51 => _gate51 > 0;

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules51 =
    {
        // BT20-085 "suspend 1 of your opponent's Digimon and, until the end of their turn, 1 of your Digimon with the [Vortex Warriors] trait gets +2000 DP."
        s => Regex.Replace(s, @"\b(?<a>suspend 1 of your opponent's Digimon) and, until the end of their turn, (?<b>1 of your Digimon[^.]*?) gets \+(?<n>\d+) DP\.",
            "${a}. ${b} gets +${n} DP until the end of your opponent's turn.", RegexOptions.IgnoreCase),
        // BT8-101 "1 of your opponent's Digimon gets -4000 DP for the turn, and all of your opponent's Digimon get -1000 DP for the turn for each ~."
        s => Regex.Replace(s, @"\b(?<a>1 of your opponent's Digimon gets -\d+ DP for the turn), and (?<b>all of your opponent's Digimon get -\d+ DP for the turn for each [^.]+)\.",
            m => $"{m.Groups["a"].Value}. {char.ToUpperInvariant(m.Groups["b"].Value[0])}{m.Groups["b"].Value[1..]}.", RegexOptions.IgnoreCase),
        // EX8-064 "＜De-Digivolve 3＞ 1 of your opponent's Digimon and, for the turn, all of their Digimon get -6000 DP." (보충 설명이 사이에 올 수도 있다)
        s => Regex.Replace(s, @"(?<a>＜De-Digivolve \d+＞ 1 of your opponent's Digimon)(?<ex> \([^)]*\))? and, for the turn, all of their Digimon get -(?<n>\d+) DP\.",
            "${a}.${ex} All of your opponent's Digimon get -${n} DP for the turn.", RegexOptions.IgnoreCase),
        // BT20-055 "＜De-Digivolve 2＞ 1 of your opponent's Digimon. (~) and flip your opponent's top face-down security card face up." — 보충 설명이 문장을 끊었다
        s => Regex.Replace(s, @"(?<a>＜De-Digivolve \d+＞ 1 of your opponent's Digimon\. \([^)]*\)) and flip your opponent's top face-down security card face up\.",
            "${a} Flip your opponent's top face-down security card face up.", RegexOptions.IgnoreCase),
        // BT16-062 "＜De-Digivolve 1＞ 1 of your opponent's Digimon. (~) with as much or less DP as this Digimon." — 보충 설명이 대상을 끊었다
        s => Regex.Replace(s, @"(?<a>＜De-Digivolve \d+＞ 1 of your opponent's Digimon)\.(?<ex> \([^)]*\)) (?<w>with as much or less DP as this Digimon)\.",
            "${a} ${w}.${ex}", RegexOptions.IgnoreCase),
        // AD1-004 (진화원) "Delete 1 of your opponent's Digimon with as much or less DP as this Digimon with [Greymon] or [Omnimon] in its name in its name." — 이름이 붙은 것은 이 디지몬
        s => Regex.Replace(s, @"\bDelete 1 of your opponent's Digimon with as much or less DP as this Digimon with (?<x>\[[^\]]+\](?:(?:,|,? or) \[[^\]]+\])*) in its name in its name\.",
            "If this Digimon has ${x} in its name, delete 1 of your opponent's Digimon with as much or less DP as this Digimon.", RegexOptions.IgnoreCase),
        // BT11-106 "Until the end of your opponent's turn, 1 of your Digimon with ~ in its name can't be blocked and gains "~"."
        s => Regex.Replace(s, @"\bUntil the end of your opponent's turn, (?<t>1 of your Digimon with [^.]*?) can't be blocked and gains (?<g>""[^""]+"")\.?",
            "${t} can't be blocked until the end of your opponent's turn. That Digimon gains ${g} until the end of your opponent's turn.", RegexOptions.IgnoreCase),
        // BT12-100 "unsuspend 1 of your [Shoutmon X7: Superior Mode] cards, and it may attack a player."
        s => Regex.Replace(s, @"\bunsuspend 1 of your (?<x>\[[^\]]+\]) cards, and it may attack a player\.", "unsuspend 1 of your ${x}. That Digimon may attack a player.", RegexOptions.IgnoreCase),
        // BT21-089 "until your opponent's turn ends, 1 of your Digimon with ~ gains ＜Blocker＞ (~) and, if there are 10 or more total cards in both players' trashes, gets +2000 DP."
        s => Regex.Replace(s, @"\buntil your opponent's turn ends, (?<t>1 of your Digimon with [^.]*?) gains (?<k>＜Blocker＞)(?<ex> \([^)]*\))? and, if (?<c>[^,]+), gets \+(?<n>\d+) DP\.",
            "${t} gains ${k}${ex} until your opponent's turn ends. If ${c}, that Digimon gets +${n} DP until your opponent's turn ends.", RegexOptions.IgnoreCase),
        // EX4-074 "Delete this Digimon and 1 of your opponent's Digimon, and ＜Recovery +1 (Deck)＞." — 두 소멸 + 시큐리티 회복
        s => Regex.Replace(s, @"\bDelete this Digimon and 1 of your opponent's Digimon, and (?<r>＜Recovery \+\d+ \(Deck\)＞)", "Delete this Digimon and delete 1 of your opponent's Digimon. ${r}", RegexOptions.IgnoreCase),
        // ST22-13 (진화원) "If your opponent has no unsuspended Digimon, this Digimon with the [Vortex Warriors] trait may unsuspend."
        s => Regex.Replace(s, @"\bIf (?<c>[^,]+), this Digimon with the (?<x>\[[^\]]+\]) trait may unsuspend\.", "If ${c} and this Digimon has the ${x} trait, this Digimon may unsuspend.", RegexOptions.IgnoreCase),
        // BT13-050 "You may suspend this Digimon to digivolve 1 of your Digimon into a Digimon card with the [Fairy] trait trait in your hand. When it would digivolve by this effect, reduce the digivolution cost by 2."
        s => Regex.Replace(s, @"\bYou may suspend this Digimon to digivolve 1 of your Digimon into (?<p>a Digimon card with the \[[^\]]+\] trait)(?: trait)? in your hand\. When it would digivolve by this effect, reduce the digivolution cost by (?<n>\d+)\.",
            "By suspending this Digimon, 1 of your Digimon may digivolve into ${p} in the hand with the cost reduced by ${n}.", RegexOptions.IgnoreCase),
        // BT13-084 "You may delete 1 of your other purple Digimon to digivolve this Digimon into a Digimon card with [Belphemon] in its name in your hand without paying its digivolution cost."
        s => Regex.Replace(s, @"\bYou may delete (?<a>1 of your other [^.]*?Digimon) to digivolve this Digimon into (?<p>a Digimon card with [^.]+?) in your hand without paying its digivolution cost\.",
            "By deleting ${a}, this Digimon may digivolve into ${p} in the hand without paying the cost.", RegexOptions.IgnoreCase),
        // BT7-045 (진화원) "You may reveal 1 green Digimon card from your hand and place it on top of your deck to have this Digimon get +3000 DP for the turn."
        s => Regex.Replace(s, @"\bYou may reveal (?<p>1 [^.]*?card) from your hand and place it on top of your deck to have this Digimon get \+(?<n>\d+) DP for the turn\.",
            "By revealing ${p} from your hand and placing it on top of your deck, this Digimon gets +${n} DP for the turn.", RegexOptions.IgnoreCase),
        // EX2-055 "You may place 2 [ADR-02 Searcher]s from your trash under this Digimon in any order as its bottom digivolution cards to unsuspend this Digimon."
        s => Regex.Replace(s, @"\bYou may place (?<n>\d+) (?<x>\[[^\]]+\])s? from your trash under this Digimon in any order as its bottom digivolution cards to unsuspend this Digimon\.",
            "By placing ${n} ${x} from your trash as this Digimon's bottom digivolution cards, unsuspend this Digimon.", RegexOptions.IgnoreCase),
        // BT17-060 "Delete any of your opponent's Digimon with a total play cost up to 15."
        s => Regex.Replace(s, @"\bDelete any of your opponent's Digimon with a total play cost up to (?<n>\d+)\.", "Delete up to ${n} play cost's total worth of your opponent's Digimon.", RegexOptions.IgnoreCase),
        // BT6-065 "If you don't use an Option card with this effect, ~" — 앞 문장(공개한 카드의 옵션 사용)을 하지 않았다
        s => Regex.Replace(s, @"\bIf you don't use an Option card with this effect, ", "If you didn't, ", RegexOptions.IgnoreCase),
        // LM-018 "Delete 1 level 4 or lower Digimon. If you did, ~" — 앞 문장의 소멸
        s => Regex.Replace(s, @"(?<a>\bDelete 1 [^.]*?Digimon\.) If you did, ", "${a} If this effect deleted, ", RegexOptions.IgnoreCase),
        // EX13-074 "When any of your [Knightmon] text Digimon are played or deleted, by placing 1 such card from your hand or trash under this Tamer" — "such card" = 그 이름이 텍스트에 있는 카드
        s => Regex.Replace(s, @"\bWhen any of your (?<x>\[[^\]]+\]) text Digimon (?<ev>are [^,]+), by placing 1 such card from your hand or trash under this Tamer",
            "When any of your Digimon with ${x} in their texts ${ev}, by placing 1 card with ${x} in its text from your hand or trash under this Tamer", RegexOptions.IgnoreCase),
        // EX9-068 "When any of your play cost 7 or higher [Cyborg], [Machine] or [DM] trait Digimon are played" · "place 1 card from your hand face down as any of those Digimon's bottom digivolution card"
        s => Regex.Replace(s, @"\bWhen any of your play cost (?<n>\d+) or higher (?<x>\[[^\]]+\](?:(?:,|,? or) \[[^\]]+\])*) trait Digimon are played\b",
            "When any of your ${x} trait Digimon with a play cost of ${n} or more are played", RegexOptions.IgnoreCase),
        // P-087 "When you would play [Pulsemon], by suspending this Tamer, ~" — 등장 직전 (효과는 드로우 · 메모리라 등장 전후가 같다)
        s => Regex.Replace(s, @"\bWhen you would play (?<x>\[[^\]]+\]), by suspending this Tamer, ", "When you play ${x}, by suspending this Tamer, ", RegexOptions.IgnoreCase),
        // AD1-020 (진화원) "By attacking with this Digimon with the [Hybrid] or [Ten Warriors] trait, it gains ＜Security A. +1＞ (~) for the attack."
        s => Regex.Replace(s, @"\bBy attacking with this Digimon with the (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait, it gains ＜Security A\. \+(?<n>\d+)＞(?: \([^)]*\))? for the attack\.",
            "If this Digimon has the ${x} trait, this Digimon may attack with ＜Security A. +${n}＞ for the attack.", RegexOptions.IgnoreCase),
        // EX2-055 "When you would play this Digimon, you may trash 7 or more digivolution cards from the bottom of 1 of your [Mother D-Reaper]s to set this Digimon's play cost to 0."
        s => Regex.Replace(s, @"\bWhen you would play this Digimon, you may trash (?<n>\d+) or more digivolution cards from the bottom of 1 of your (?<x>\[[^\]]+\])s? to set this Digimon's play cost to 0\.",
            "When you would play this Digimon, by trashing ${n} digivolution cards from the bottom of 1 of your ${x}, reduce the play cost by 99.", RegexOptions.IgnoreCase),
        // EX3-036 "If this card was played by [X]'s effect, all of your opponent's Digimon gain ＜Security A. -2＞ (~) until the end of your opponent's turn instead." — 앞 문장 대신
        s => Regex.Replace(s, @"(?<a>All of your opponent's Digimon gain ＜Security A\. -(?<n1>\d+)＞(?: \([^)]*\))? until the end of your opponent's turn)\. If this card was played by (?<x>\[[^\]]+\])'s effect, all of your opponent's Digimon gain ＜Security A\. -(?<n2>\d+)＞(?: \([^)]*\))? until the end of your opponent's turn instead\.",
            "All of your opponent's Digimon gain ＜Security A. -${n1}＞ until the end of your opponent's turn. If this card was played by ${x}'s effect, all of your opponent's Digimon gain ＜Security A. -${n2}＞ ⟦instead-of:${n1}⟧ until the end of your opponent's turn.", RegexOptions.IgnoreCase),
        // BT9-067 "this Digimon gain +3000 DP until the end of your opponent's next turn" — 동사 수
        s => Regex.Replace(s, @"\bthis Digimon gain \+(?<n>\d+) DP\b", "this Digimon gets +${n} DP", RegexOptions.IgnoreCase),
        // EX3-035 "by returning 1 [Magnadramon], 1 [Azulongmon], and 1 [Megidramon] from your trash ~" — 쉼표가 비용을 끊었다
        s => Regex.Replace(s, @"\bby returning (?<a>1 \[[^\]]+\]), (?<b>1 \[[^\]]+\]),? and (?<c>1 \[[^\]]+\]) from your trash", "by returning ${a} plus ${b} plus ${c} from your trash", RegexOptions.IgnoreCase),
        // EX4-027 "If you have a blue or yellow Tamer in play, or if you have a card with the [Armor Form] trait in your trash, ~" — 쉼표가 조건을 끊었다
        s => Regex.Replace(s, @"\bIf (?<a>you have [^,]+), or if (?<b>you have [^,]+), ", "If ${a} ⟦or⟧ ${b}, ", RegexOptions.IgnoreCase),
        // BT5-110 "You may return 1 of your Digimon with [Omnimon] in its name to its owner's hand to delete all Digimon and Tamers. Trash all of the digivolution cards of the Digimon you returned with this effect."
        //  — 비용 (패로 되돌린 디지몬의 진화원은 원래 트래시로 간다)
        s => Regex.Replace(s, @"\bYou may return (?<t>1 of your Digimon with [^.]+?) to its owner's hand to delete all Digimon and Tamers\. Trash all of the digivolution cards of the Digimon you returned with this effect\.",
            "By returning ${t} to the hand, delete all Digimon and Tamers.", RegexOptions.IgnoreCase),
    };

    /// <summary>25차 규칙(비용 · 조건 · 동작)이 읽을 수 있는 말이 있는 절만 문을 열고 다시 읽는다 (문을 연 채로 모든 층을 다시 도는 비용을 줄인다)</summary>
    internal static readonly Regex Hint51 = new(string.Join("|", new[]
    {
        @"digivolved this turn", @"colou?rs among them", @"DP or more (?:is|was) deleted by this effect", @"both players' trash", @"placed in your battle area", @"was played by",
        @", or if", @"is in your battle area", @"from this Digimon's digivolution cards", @"from that Digimon's digivolution cards", @"from under (?:this Tamer|your Tamers|one of your Tamers)",
        @"from your hand or trash in(?:to)? the battle area", @"Option cards with", @"\brevealing", @"digivolution cards from the bottom of", @"from your trash as this Digimon's bottom",
        @"win the game", @"until they have as many", @"all Digimon and Tamers", @"played by this effect to", @"face down as", @"place this Digimon as that Digimon's",
        @"Token", @"attack with ＜Security",
    }), RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string ClauseRewrite51(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules51) s = f(s);
        return s;
    }

    private static string? TryParse51(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse50(rawClause, inherited, card, def);
        if (err == null || _clauseDepth51 > 0) return err;
        _clauseDepth51++;
        _gate51++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (Hint51.IsMatch(rawClause) && TryParse50(rawClause, inherited, card, null) == null)
            {
                if (def != null) TryParse50(rawClause, inherited, card, def);
                return null;
            }
            string alt = ClauseRewrite51(rawClause);
            if (alt == rawClause || TryParse50(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse50(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth51--;
            _gate51--;
            if (def != null) WrapGate51(def, before);
        }
    }

    /// <summary>25차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate51(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate51++; try { inner(c); } finally { _gate51--; } } };
        }
    }

    // =====================================================================
    // 조건
    // =====================================================================

    private static Func<EffectContext, bool>? ParseEventCondition51(string cond)
    {
        if (!Gate51) return null;
        string s = cond.Trim();
        Match m;
        // "you've digivolved this turn" (BT1-007 진화원) — 이번 턴에 자신이 진화했다 (효과로 진화 · 육성 에어리어 포함)
        if (Regex.IsMatch(s, @"^you(?:'ve| have) digivolved (?:a Digimon )?this turn$", I))
            return c => c.Engine.DigivolvedThisTurn51(c.Owner) && Diag46.HitTrue("condDigivolvedThisTurn51");
        // "the level 6 cards in this Digimon's digivolution cards have 3 or more colors among them" · "they have 4 or more colors" (BT9-067)
        if ((m = Regex.Match(s, @"^(?:the level (?<lv>\d+) cards in this Digimon's digivolution cards|they) have (?<n>\d+) or more colou?rs(?: among them)?$", I)).Success)
        {
            int lv = m.Groups["lv"].Success ? int.Parse(m.Groups["lv"].Value) : 6, n = int.Parse(m.Groups["n"].Value);
            return c => c.Self != null && c.Self.SourcesWhere(x => x.Level == lv && !c.Self.IsFaceDown(x)).SelectMany(x => x.AllColors).Distinct().Count() >= n && Diag46.HitTrue("condSourceColors51");
        }
        // "a Digimon with 13000 DP or more is deleted by this effect" (EX4-065) — 이 효과로 소멸한 디지몬의 소멸 직전 DP
        if ((m = Regex.Match(s, @"^a Digimon with (?<n>\d+) DP or more (?:is|was) deleted by this effect$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return c => c.LastTargets.Any(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d) && (c.Enemy.Trash.Contains(d.TopCard) || c.Owner.Trash.Contains(d.TopCard)) && d.GetCurrentDP() >= n)
                        && Diag46.HitTrue("condDeletedDpAtLeast51");
        }
        // "there are 10 or more total cards in both players' trashes" (BT21-089)
        if ((m = Regex.Match(s, @"^there are (?<n>\d+) or more (?:total )?cards in both players' trash(?:es)?$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return c => c.Owner.Trash.Count + c.Enemy.Trash.Count >= n;
        }
        // "4 [Doomsday Clock]s are placed in your battle area" (BT17-100 — 배틀 에어리어에 놓인 옵션 카드)
        if ((m = Regex.Match(s, @"^(?<n>\d+) (?<x>\[[^\]]+\])s? (?:are|is) placed in your battle area$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); string x = TargetSpec.Names(m.Groups["x"].Value)[0];
            return c => c.Owner.OptionsInPlay.Count(p => CardNames.Is(p.Card, x)) + c.Owner.BattleArea.Count(d => CardNames.Is(d, x)) >= n && Diag46.HitTrue("condPlacedCount51");
        }
        // "this card was played by [Trial of the Four Great Dragons]'s effect" (EX3-025 · EX3-036)
        if ((m = Regex.Match(s, @"^this card was played by (?<x>\[[^\]]+\])'s effect$", I)).Success)
        {
            string x = TargetSpec.Names(m.Groups["x"].Value)[0];
            return c => c.Self?.PlayedByEffectCard51 is { } by && CardNames.Is(by, x) && Diag46.HitTrue("condPlayedByCard51");
        }
        // "you have a card with the [Armor Form] trait in your trash" (EX4-027)
        if ((m = Regex.Match(s, @"^you have (?:a|an|1 or more) (?<pick>(?:Digimon |Tamer |Option )?card with .+?) in your trash$", I)).Success
            && ParseCardPick("1 " + m.Groups["pick"].Value) is { } tp)
        {
            var match = tp.Match;
            return c => c.Owner.Trash.Any(match);
        }
        if ((m = Regex.Match(s, @"^(?<a>.+?) ⟦or⟧ (?<b>.+)$", I)).Success
            && ParseEventConditionChain(m.Groups["a"].Value) is { } oa && ParseEventConditionChain(m.Groups["b"].Value) is { } ob)
            return c => oa(c) || ob(c);
        // "A, or if B" (EX4-027 "If you have a blue or yellow Tamer in play, or if you have a card with the [Armor Form] trait in your trash")
        if ((m = Regex.Match(s, @"^(?<a>.+?), or if (?<b>.+)$", I)).Success
            && ParseEventConditionChain(m.Groups["a"].Value) is { } ca && ParseEventConditionChain(m.Groups["b"].Value) is { } cb)
            return c => ca(c) || cb(c);
        return null;
    }

    /// <summary>지속 조건: "you have a Digimon with the [Four Great Dragons] trait in play, or [Trial of the Four Great Dragons] is in your battle area" (EX3-033)</summary>
    private static Func<StaticContext, bool>? ParseStaticCondition51(string cond)
    {
        if (!Gate51) return null;
        Match m;
        if ((m = Regex.Match(cond.Trim(), @"^(?<a>.+?), or (?<x>\[[^\]]+\]) is in your battle area$", I)).Success && ParseStaticCondition(m.Groups["a"].Value) is { } ca)
        {
            string x = TargetSpec.Names(m.Groups["x"].Value)[0];
            return c => ca(c) || c.Owner.OptionsInPlay.Any(p => CardNames.Is(p.Card, x)) || c.Owner.BattleArea.Any(d => CardNames.Is(d, x));
        }
        return null;
    }

    // =====================================================================
    // 비용
    // =====================================================================

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost51(string cost, CardData card)
    {
        if (!Gate51) return null;
        Match m;
        // "playing 1 [DigiPolice] or [SEEKERS] trait Tamer card from this Digimon's digivolution cards without paying the cost" (BT24-060)
        //  · "playing 1 [Guilmon] from this Digimon's digivolution cards or from your trash" (LM-026 — 공식 한글 "코스트를 지불하지 않고 등장시킨다")
        if ((m = Regex.Match(cost, @"^playing (?<pick>1 .+?) from this Digimon's digivolution cards(?<trash> or from your trash)?(?: without paying (?:the|its) cost)?$", I)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } pk)
        {
            var match = pk.Match; bool trash = m.Groups["trash"].Success;
            List<(CardData Card, bool FromSources)> Able(EffectContext c)
            {
                var list = new List<(CardData, bool)>();
                if (c.Self != null && (c.Owner.BattleArea.Contains(c.Self) || c.Engine.CurrentLeave != null))
                    list.AddRange(c.Self.SourcesWhere(x => !c.Self.IsFaceDown(x) && match(x) && x.Type is CardType.Digimon or CardType.Tamer && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Sources)).Select(x => (x, true)));
                if (trash) list.AddRange(c.Owner.Trash.Where(x => match(x) && x.Type is CardType.Digimon or CardType.Tamer && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Trash)).Select(x => (x, false)));
                return list;
            }
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var pool = Able(c);
                        int k = pool.Count == 1 ? 0 : c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, pool.Select(p => p.Card).ToList(), 1, false, $"[{c.Card.Name}] 비용으로 코스트 없이 등장시킬 카드를 고르세요.").DefaultIfEmpty(0).First();
                        var (x, fromSources) = pool[Math.Clamp(k, 0, pool.Count - 1)];
                        var self = c.Self;
                        bool ok = fromSources
                            ? c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Sources, () => self?.TakeSource(y => ReferenceEquals(y, x)) != null)
                            : c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Trash, () => c.Owner.Trash.Remove(x));
                        if (!ok) { c.CostFailed = true; return; }
                        c.RecalculateFieldBuffs();
                        Diag46.Hit(fromSources ? "playFromSourcesCost51" : "playFromTrashCost51");
                    },
                    _ => true, "카드 1장을 코스트 없이 등장시켜");
        }
        // "playing 1 card with the same level as one of your [Night Claw] or [Light Fang] trait Digimon from that Digimon's digivolution cards without paying the cost" (EX5-065)
        if ((m = Regex.Match(cost, @"^playing 1 card with the same level as one of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon from that Digimon's digivolution cards without paying (?:the|its) cost$", I)).Success)
        {
            var ts = TargetSpec.Names(m.Groups["x"].Value);
            List<(CardInstance Host, CardData Card)> Able(EffectContext c) =>
                c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && ts.Any(t => TargetSpec.HasTrait(d, t)))
                    .SelectMany(d => d.SourcesWhere(x => !d.IsFaceDown(x) && x.Type == CardType.Digimon && x.Level == d.TopCard.Level && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Sources)).Select(x => (d, x))).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var pool = Able(c).OrderByDescending(p => p.Card.DP).ToList();
                        int k = pool.Count == 1 ? 0 : c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, pool.Select(p => p.Card).ToList(), 1, false, $"[{c.Card.Name}] 비용으로 등장시킬 진화원 (그 디지몬과 같은 Lv.)을 고르세요.").DefaultIfEmpty(0).First();
                        var (host, x) = pool[Math.Clamp(k, 0, pool.Count - 1)];
                        if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Sources, () => host.TakeSource(y => ReferenceEquals(y, x)) != null)) { c.CostFailed = true; return; }
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("playSameLevelSourceCost51");
                    },
                    _ => true, "같은 Lv.의 진화원 1장을 코스트 없이 등장시켜");
        }
        // "playing 1 play cost 3 or lower Digimon card from under this Tamer without paying the cost" (BT20-092 — 테이머 아래 카드는 한 묶음)
        if ((m = Regex.Match(cost, @"^playing 1 play cost (?<n>\d+) or (?:lower|less) Digimon card from under (?:this Tamer|your Tamers) without paying (?:the|its) cost$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            List<CardData> Able(EffectContext c) => c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x) && x.Type == CardType.Digimon && x.PlayCost <= n && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.UnderTamers)).ToList();
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var pool = Able(c).OrderByDescending(x => x.PlayCost).ThenByDescending(x => x.DP).ToList();
                        var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: false, $"[{c.Card.Name}] 비용으로 테이머 아래에서 등장시킬 디지몬 카드를 고르세요.").FirstOrDefault() ?? pool[0];
                        if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.UnderTamers, () => { bool r = c.Owner.UnderTamers.Remove(x); c.Owner.FaceDownUnderTamers.Remove(x); return r; })) { c.CostFailed = true; return; }
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("playUnderTamerCost51");
                    },
                    _ => true, $"테이머 아래에서 등장 코스트 {n} 이하의 디지몬 카드 1장을 코스트 없이 등장시켜");
        }
        // "returning 1 [Magnadramon], 1 [Azulongmon], and 1 [Megidramon] from your trash to the bottom of your deck in any order" (EX3-035 — 이름마다 1장)
        if ((m = Regex.Match(cost, @"^returning (?<list>1 \[[^\]]+\](?: plus 1 \[[^\]]+\])+) from your trash to the bottom of (?:your|the) deck(?: in any order)?$", I)).Success)
        {
            var names = Regex.Matches(m.Groups["list"].Value, @"\[(?<n>[^\]]+)\]").Select(x => x.Groups["n"].Value).ToList();
            List<CardData>? Pick(EffectContext c)
            {
                var picked = new List<CardData>();
                foreach (var n in names)
                {
                    var x = c.Owner.Trash.FirstOrDefault(y => CardNames.Is(y, n) && !picked.Any(p => ReferenceEquals(p, y)));
                    if (x == null) return null;
                    picked.Add(x);
                }
                return picked;
            }
            return (c => Pick(c) != null,
                    c =>
                    {
                        var picked = Pick(c);
                        if (picked == null) { c.CostFailed = true; return; }
                        foreach (var x in picked) if (c.Owner.Trash.Remove(x)) c.Owner.MainDeck.Add(x);
                        c.Engine.NotifyTrashMoved(c.Owner, picked, toHand: false);
                        Diag46.Hit("namedTrashToDeckCost51");
                    },
                    _ => true, $"트래시의 {string.Join(" · ", names.Select(n => $"[{n}]"))}를 1장씩 덱 아래로 되돌려");
        }
        // "placing 1 Option card with the [Device] trait from your hand or trash into the battle area" (P-179)
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from your hand or trash (?:in|into) (?:the|your) battle area$", I)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } op)
        {
            var (n, match) = op;
            Func<CardData, bool> ok = x => x.Type == CardType.Option && match(x);
            return (c => c.Owner.Hand.Count(ok) + c.Owner.Trash.Count(ok) >= n,
                    c =>
                    {
                        var pool = c.Owner.Trash.Where(ok).Concat(c.Owner.Hand.Where(ok)).ToList();   // 추천: 트래시 먼저
                        var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, n, upTo: false, $"[{c.Card.Name}] 비용으로 배틀 에어리어에 놓을 옵션 카드를 고르세요 (패 · 트래시).");
                        int placed = 0;
                        foreach (var x in picked.Take(n))
                            if (c.Owner.Trash.Remove(x) || c.Owner.Hand.Remove(x)) { c.Engine.PlaceOption(c.Owner, x); placed++; }
                        if (placed < n) c.CostFailed = true;
                        Diag46.Hit("placeOptionHandOrTrashCost51");
                    },
                    _ => true, $"패 · 트래시의 옵션 카드 {n}장을 배틀 에어리어에 놓아");
        }
        // "trashing 4 of your Option cards with the [Device] trait in the battle area" (BT19-086)
        if ((m = Regex.Match(cost, @"^trashing (?<n>\d+) of your Option cards (?<w>with .+?) in the battle area$", I)).Success
            && ParseCardPick("1 Option card " + m.Groups["w"].Value) is { } ow)
        {
            int n = int.Parse(m.Groups["n"].Value); var match = ow.Match;
            List<PlacedOption> Able(EffectContext c) => c.Owner.OptionsInPlay.Where(p => match(p.Card)).ToList();
            return (c => Able(c).Count >= n,
                    c =>
                    {
                        var pool = Able(c);
                        var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool.Select(p => p.Card).ToList(), n, upTo: false, $"[{c.Card.Name}] 비용으로 파기할 배틀 에어리어의 옵션 카드 {n}장을 고르세요.");
                        var picked = idx.Select(i => pool[i]).Distinct().ToList();
                        foreach (var p in pool) { if (picked.Count >= n) break; if (!picked.Contains(p)) picked.Add(p); }
                        foreach (var p in picked.Take(n)) c.Engine.TrashOptionFromBattleArea(c.Owner, p);
                        Diag46.Hit("trashPlacedOptionsWithCost51");
                    },
                    _ => true, $"배틀 에어리어의 조건에 맞는 옵션 카드 {n}장을 파기하여");
        }
        // "revealing up to 3 Tamer cards in your hand and placing them at the bottom of your deck in any order" (BT12-048 — 놓은 장수 = PaidCount)
        if ((m = Regex.Match(cost, @"^revealing up to (?<n>\d+) (?<pick>.+?) in your hand and placing them at the bottom of your deck(?: in any order)?$", I)).Success
            && (Regex.Match(m.Groups["pick"].Value, @"^(?<t>Tamer|Digimon|Option) cards?$", I) is { Success: true } kind
                    ? ((int, Func<CardData, bool>)?)(1, x => x.Type == Enum.Parse<CardType>(kind.Groups["t"].Value, ignoreCase: true))
                    : ParseCardPick("1 " + Regex.Replace(m.Groups["pick"].Value, @"\bcards\b", "card", I))) is { } rv)
        {
            int n = int.Parse(m.Groups["n"].Value); var match = rv.Item2;
            return (c => c.Owner.Hand.Any(match),
                    c =>
                    {
                        var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, c.Owner.Hand.Where(match).ToList(), n, upTo: true, $"[{c.Card.Name}] 공개하고 덱 아래에 놓을 카드를 {n}장까지 고르세요.");
                        int placed = 0;
                        foreach (var x in picked.Take(n)) if (c.Owner.Hand.Remove(x)) { c.Owner.MainDeck.Add(x); placed++; }
                        if (placed == 0) { c.CostFailed = true; return; }
                        c.PaidCount = placed;
                        c.Log($"[{c.Card.Name}] 패의 {string.Join(", ", picked.Take(placed).Select(x => $"[{x.Name}]"))}을(를) 공개하고 덱 아래에 놓았습니다.");
                        Diag46.Hit("revealHandToBottomCost51");
                    },
                    _ => true, $"패의 카드를 {n}장까지 공개하고 덱 아래에 놓아");
        }
        // "revealing 1 green Digimon card from your hand and placing it on top of your deck" (BT7-045 진화원)
        if ((m = Regex.Match(cost, @"^revealing (?<pick>1 .+?) from your hand and placing it on top of your deck$", I)).Success
            && ParseCardPick(m.Groups["pick"].Value) is { } rt)
        {
            var match = rt.Match;
            return (c => c.Owner.Hand.Any(match),
                    c =>
                    {
                        var pool = c.Owner.Hand.Where(match).OrderBy(x => x.Level).ToList();
                        var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] 공개하고 덱 위에 놓을 카드를 고르세요.").FirstOrDefault() ?? pool[0];
                        if (!c.Owner.Hand.Remove(x)) { c.CostFailed = true; return; }
                        c.Owner.MainDeck.Insert(0, x);
                        c.Log($"[{c.Card.Name}] 패의 [{x.Name}]을(를) 공개하고 덱 위에 놓았습니다.");
                        Diag46.Hit("revealHandToTopCost51");
                    },
                    c => c.Owner.Hand.Count >= 4, "패의 카드 1장을 공개하고 덱 위에 놓아");
        }
        // "placing 2 [ADR-02 Searcher] from your trash as this Digimon's bottom digivolution cards" (EX2-055)
        if ((m = Regex.Match(cost, @"^placing (?<n>\d+) (?<x>\[[^\]]+\])s? from your trash as this Digimon's bottom digivolution cards?$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); string x = TargetSpec.Names(m.Groups["x"].Value)[0];
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && c.Owner.Trash.Count(y => CardNames.Is(y, x)) >= n,
                    c =>
                    {
                        var pool = c.Owner.Trash.Where(y => CardNames.Is(y, x)).Take(n).ToList();
                        foreach (var y in pool) if (c.Owner.Trash.Remove(y)) c.Self!.AddBottomSource(y);
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("trashToBottomSourcesCost51");
                    },
                    _ => true, $"트래시의 [{x}] {n}장을 이 디지몬의 진화원 아래에 놓아");
        }
        // "trashing 7 digivolution cards from the bottom of 1 of your [Mother D-Reaper]" (EX2-055 — 등장 코스트를 0으로)
        if ((m = Regex.Match(cost, @"^trashing (?<n>\d+) digivolution cards from the bottom of 1 of your (?<x>\[[^\]]+\])$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); string x = TargetSpec.Names(m.Groups["x"].Value)[0];
            List<CardInstance> Able(EffectContext c) => c.Owner.BattleArea.Where(d => CardNames.Is(d, x) && c.Engine.TrashableSourceCount61(d) >= n).ToList();   // v0.61.0 파기할 수 없는 카드를 빼고 셈 (공식 Q&A EX2-055 Q1)
            return (c => Able(c).Count > 0,
                    c =>
                    {
                        var pool = Able(c);
                        var host = pool.Count == 1 ? pool[0] : c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 진화원 아래에서 {n}장을 파기할 [{x}]을(를) 고르세요.", optional: false, preselected: pool[0]).FirstOrDefault() ?? pool[0];
                        var bottom = c.Engine.TrashableSources61(host, host.GetEvolutionStackBottomUp()).Take(n).ToList();   // v0.61.0 파기할 수 없는 카드를 제외하고 아래에서부터 (공식 Q&A EX2-055 Q1)
                        int done = c.Engine.TrashChosenSources61(c.Owner, host, bottom, cost: true).Count;
                        if (done < n) c.CostFailed = true;
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("trashBottomSourcesCost51");
                    },
                    _ => true, $"[{x}]의 진화원 아래에서 {n}장을 파기하여");
        }
        return null;
    }

    // =====================================================================
    // 문장 · 동작 — 원래 규칙이 읽지 못한 것만 (문이 열려 있을 때)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback51(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth51 >= 2 || !Gate51) return null;
        _fallbackDepth51++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            bool optional = false;
            var may = Regex.Match(s, @"^You may (?<b>.+)$", I);
            if (may.Success) { optional = true; s = may.Groups["b"].Value; }
            if (ActionExtra51(s, card, triggers) is { Act: { } act }) return (new Step(null, act, optional, sentence.Trim()), null);
            return null;
        }
        finally { _fallbackDepth51--; }
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction51(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth51 >= 2 || !Gate51) return null;
        _fallbackDepth51++;
        try { return ActionExtra51(Regex.Replace(raw.Trim().TrimEnd('.'), @"^you may ", "", I), card, triggers); }
        finally { _fallbackDepth51--; }
    }

    private static (Action<EffectContext>? Act, string? Error)? ActionExtra51(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = card.Name;
        Match m;
        // "this Digimon may attack with ＜Security A. +1＞ for the attack" (AD1-020 진화원 — 원문 "By attacking with this Digimon ~, it gains ＜Security A. +1＞ for the attack")
        if ((m = Regex.Match(s, @"^this Digimon may attack with ＜Security A\. \+(?<n>\d+)＞ for the attack$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                var d = c.Self;
                if (d == null || !c.Engine.CanEffectAttack(c.Owner, d, withoutSuspending: false)) { c.LastStepResolved = false; Emit(c, label, "어택할 수 없습니다"); return; }
                var buff = (n, c.Engine.State.TurnSequence);
                d.TimedSecurityAttackBuffs.Add(buff);   // 그 어택 동안 (자신의 턴 종료 시의 어택 — 턴이 끝나면 사라진다)
                c.RecalculateFieldBuffs();
                if (!c.Engine.EffectAttack(c.Owner, d, EffectAttackTarget.Any, withoutSuspending: false, label))
                {
                    d.TimedSecurityAttackBuffs.Remove(buff);
                    c.RecalculateFieldBuffs();
                    c.LastStepResolved = false;
                    return;
                }
                Diag46.Hit("attackWithSecurityPlus51");
                Emit(c, label, $"이 디지몬으로 어택 — 그 어택 동안 ≪시큐리티 어택 +{n}≫");
            }, null);
        }
        // "Delete your opponent's Digimon until they have as many as the number of your security cards" (BT19-094 — 고르는 쪽은 효과의 주인)
        if (Regex.IsMatch(s, @"^Delete your opponent's Digimon until they have as many as the number of your security cards$", I))
        {
            return (c =>
            {
                var deleted = new List<CardInstance>();
                int guard = 0;
                while (guard++ < 30)
                {
                    var pool = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
                    if (c.Enemy.BattleArea.Count(d => d.TamerProxyOf == null) <= c.Owner.SecurityStack.Count || pool.Count == 0) break;
                    var ranked = pool.OrderByDescending(d => d.GetCurrentDP()).ToList();
                    var v = c.Engine.ChooseTargets(c.Owner, ranked, 1, $"[{label}] 소멸시킬 상대의 디지몬을 고르세요 (상대의 디지몬이 자신의 시큐리티 {c.Owner.SecurityStack.Count}장과 같은 수가 될 때까지).", optional: false, preselected: ranked[0]).FirstOrDefault() ?? ranked[0];
                    if (!c.DeleteEnemy(v)) break;
                    deleted.Add(v);
                }
                c.LastTargets = deleted;
                c.LastDidDelete34 = deleted.Count > 0;
                if (deleted.Count == 0) c.LastStepResolved = false;
                Diag46.Hit("deleteUntilSecurityCount51");
                Emit(c, label, deleted.Count > 0 ? $"[{string.Join(", ", deleted.Select(d => d.TopCard.Name))}] 소멸" : "소멸시킬 디지몬이 없습니다");
            }, null);
        }
        // "delete all Digimon and Tamers" (BT5-110 — 서로의 디지몬과 테이머 전부)
        if (Regex.IsMatch(s, @"^delete all Digimon and Tamers$", I))
        {
            return (c =>
            {
                int n = 0;
                foreach (var d in c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList()) if (c.DeleteEnemy(d)) n++;
                foreach (var d in c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList()) if (c.Engine.DeleteFrom(c.Owner, d)) n++;
                foreach (var (z, t) in c.Enemy.Tamers.Select(t => (c.Enemy, t)).Concat(c.Owner.Tamers.Select(t => (c.Owner, t))).ToList())
                    if (z.Tamers.Contains(t)) { c.Engine.DeleteTamer(z, t); if (!z.Tamers.Contains(t)) n++; }
                if (n == 0) c.LastStepResolved = false;
                c.RecalculateFieldBuffs();
                Diag46.Hit("deleteAllDigimonAndTamers51");
                Emit(c, label, $"모든 디지몬과 테이머 소멸 ({n})");
            }, null);
        }
        // "return 1 Digimon card with ＜Save＞ in its text from under one of your Tamers to your hand" (BT12-075 — 테이머 아래 카드는 한 묶음)
        if ((m = Regex.Match(s, @"^return (?<pick>1 .+?) from under (?:one of your Tamers|your Tamers|1 of your Tamers) to (?:your|the) hand$", I)).Success
            && ParseCardPick(m.Groups["pick"].Value) is { } up)
        {
            var match = up.Match;
            return (c =>
            {
                var pool = c.Owner.UnderTamers.Where(x => !c.Owner.IsFaceDownUnderTamer(x) && match(x)).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "테이머 아래에 패로 되돌릴 카드가 없습니다"); return; }
                var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: false, $"[{label}] 테이머 아래에서 패로 되돌릴 카드를 고르세요.").FirstOrDefault() ?? pool[0];
                if (!c.Owner.UnderTamers.Remove(x)) { c.LastStepResolved = false; return; }
                c.Owner.FaceDownUnderTamers.Remove(x);
                c.Owner.Hand.Add(x);
                Diag46.Hit("underTamerToHand51");
                Emit(c, label, $"테이머 아래의 [{x.Name}]을(를) 패로");
            }, null);
        }
        // "place this Digimon as that Digimon's bottom digivolution card" (LM-026 — 방금 등장한 디지몬의 진화원 아래에. 떠나려던 디지몬이면 떠나지 않는다)
        if (Regex.IsMatch(s, @"^place this Digimon as that Digimon's bottom digivolution cards?$", I))
        {
            return (c =>
            {
                var host = c.PlayedThisEffect.LastOrDefault(d => c.Owner.BattleArea.Contains(d)) ?? c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d));
                if (c.Self == null || host == null || ReferenceEquals(host, c.Self) || !c.Engine.PlaceDigimonUnder46(c.Owner, c.Self, host)) { c.LastStepResolved = false; Emit(c, label, "놓을 디지몬이 없습니다"); return; }
                Diag46.Hit("placeSelfUnderPlayed51");
                Emit(c, label, $"이 디지몬을 [{host.TopCard.Name}]의 진화원 아래에");
            }, null);
        }
        // "place 1 card from your hand face down as any of those Digimon's bottom digivolution card" (EX9-068 — 등장한 디지몬 중 1마리)
        if (Regex.IsMatch(s, @"^place 1 card from your hand face down as (?:any of those Digimon's|that Digimon's) bottom digivolution card$", I))
        {
            return (c =>
            {
                var hosts = c.LastTargets.Concat(c.Other != null ? new[] { c.Other } : Array.Empty<CardInstance>()).Where(d => c.Owner.BattleArea.Contains(d)).Distinct().ToList();
                if (hosts.Count == 0 || c.Owner.Hand.Count == 0) { c.LastStepResolved = false; return; }
                var host = hosts.Count == 1 ? hosts[0] : c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{label}] 진화원 아래에 뒷면으로 카드를 놓을 디지몬을 고르세요.", optional: false, preselected: hosts[0]).FirstOrDefault() ?? hosts[0];
                var pool = c.Owner.Hand.OrderBy(x => x.Type == CardType.Digimon ? 1 : 0).ThenBy(x => x.Level).ToList();
                var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: false, $"[{label}] [{host.TopCard.Name}]의 진화원 아래에 뒷면으로 놓을 패의 카드를 고르세요.").FirstOrDefault() ?? pool[0];
                if (!c.Owner.Hand.Remove(x)) { c.LastStepResolved = false; return; }
                var copy = CardInstance.CopyCard(x);   // 뒷면 카드는 위치마다 구분한다
                host.AddBottomSource(copy);
                host.FaceDownSources.Add(copy);
                c.RecalculateFieldBuffs();
                Diag46.Hit("handFaceDownUnder51");
                Emit(c, label, $"패의 카드 1장을 [{host.TopCard.Name}]의 진화원 아래에 뒷면으로");
            }, null);
        }
        // "At the end of the turn, return the Digimon played by this effect to the hand" (EX5-065 — 이 효과(비용 포함)로 등장한 디지몬. 조그레스 재료가 되었으면 이미 다른 디지몬)
        if (Regex.IsMatch(s, @"^At the end of (?:the|this) turn, return the Digimon played by this effect to (?:the|your|its owner's) hand$", I))
        {
            return (c =>
            {
                var list = c.PlayedThisEffect.Where(c.Owner.BattleArea.Contains).ToList();
                if (list.Count == 0) { c.LastStepResolved = false; return; }
                foreach (var x in list) c.Engine.ScheduleReturnAtTurnEnd42(x, c.Owner, c.State.TurnSequence, toHand: true);
                Diag46.Hit("returnPlayedAtTurnEnd51");
                Emit(c, label, $"[{string.Join(", ", list.Select(x => x.TopCard.Name))}] 턴 종료 시 패로");
            }, null);
        }
        // "＜Draw 1＞ for each card placed at the bottom of your deck" (BT12-048 — 비용으로 덱 아래에 놓은 장수 = PaidCount)
        if ((m = Regex.Match(s, @"^＜Draw (?<n>\d+)＞ for each card (?:placed at the bottom of your deck|returned by this effect)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int k = Math.Max(0, c.PaidCount) * n, drawn = 0;
                for (int i = 0; i < k; i++) if (c.Draw() != null) drawn++;
                if (drawn == 0) c.LastStepResolved = false;
                Diag46.Hit("drawPerPaid51");
                Emit(c, label, $"≪{n} 드로우≫ × {Math.Max(0, c.PaidCount)} ({drawn}장)");
            }, null);
        }
        // "delete this Tamer" (BT20-092 — 비용을 낸 뒤 이 테이머를 소멸)
        if (Regex.IsMatch(s, @"^delete this Tamer$", I) && card.Type == CardType.Tamer)
        {
            return (c =>
            {
                if (!c.Owner.Tamers.Contains(c.Card)) { c.LastStepResolved = false; return; }
                c.Engine.DeleteTamer(c.Owner, c.Card);
                Diag46.Hit("deleteThisTamer51");
                Emit(c, label, "이 테이머를 소멸");
            }, null);
        }
        // "you win the game" (BT17-100)
        if (Regex.IsMatch(s, @"^you win the game$", I))
        {
            return (c =>
            {
                var st = c.Engine.State;
                if (st.IsGameOver) return;
                bool player = ReferenceEquals(c.Owner, st.Player);
                st.IsGameOver = true;
                st.IsVictory = player;
                st.GameOverReason = $"[{label}] 효과로 {(player ? "승리" : "패배")}했습니다.";
                Diag46.Hit("winTheGame51");
                Emit(c, label, "게임에 승리");
            }, null);
        }
        // "all of your opponent's Digimon gain ＜Security A. -2＞ ⟦instead-of:1⟧ until the end of your opponent's turn" (EX3-036 — 앞 문장의 ≪시큐리티 어택 -1≫ 대신)
        if ((m = Regex.Match(s, @"^all of your opponent's Digimon gain ＜Security A\. -(?<n>\d+)＞ ⟦instead-of:(?<o>\d+)⟧ until the end of your opponent's turn$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value), o = int.Parse(m.Groups["o"].Value);
            return (c =>
            {
                int until = Until(c, Duration.OpponentsTurnEnd);
                c.Engine.WholeDrop63(c, $"sa:{-o}");   // v0.63.0 앞 문장의 전체 처리 기록 대신 (SentenceParser.Batch63)
                var all63 = TargetSpec.Parse("all of your opponent's Digimon")!;
                var whole = WholeBegin63(c, all63, Duration.OpponentsTurnEnd);
                var first = c.Enemy.BattleArea.Where(d => !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
                foreach (var d in first)
                {
                    int i = d.TimedSecurityAttackBuffs.FindLastIndex(b => b.Count == -o);
                    if (i >= 0) c.Engine.RemoveTimedSaAt60(d, i);
                    c.Engine.AddTimedSa60(d, -n, until);
                }
                WholeEnd63(c, all63, whole, first, until, d => c.Engine.AddTimedSa60(d, -n, until));
                c.RecalculateFieldBuffs();
                Diag46.Hit("securityMinusInstead51");
                Emit(c, label, $"대신 상대의 디지몬 전부 ≪시큐리티 어택 -{n}≫");
            }, null);
        }
        return null;
    }
}
