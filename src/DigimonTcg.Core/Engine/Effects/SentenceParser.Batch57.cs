using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.57.0 미지원 문장 31차 묶음: 원래 규칙(1~30차)이 읽지 못한 절만 31차 규칙으로 다시 읽는다.
///  - 셈: "for every 5000 DP this Digimon has" (EX13-020) · "of this Digimon's link cards" (EX11-073) · "2 same-level cards this Digimon's stack has" (BT22-015)
///  - 낸 만큼: "for every Tamer this effect suspended" (BT17-041) · "pay up to 5 memory ~ for each memory paid" (BT6-111)
///  - 지속 효과 둘이 한 절 (AD1-004 · BT12-031 · EX8-045) · 조건 "N or more colors in its digivolution cards" · "your opponent has no Digimon with equal or higher DP"
///  - 트래시에서 코스트를 내고 등장 + 트래시의 카드로 디지크로스 (BT10-104)
///  - "[Opponent's Turn] All of your opponent's Tamers with play costs of 2 or less can't suspend." (EX5-052)
///  - 아래 효과를 여러 번: "Activate the effect below. For each ~, activate it again." (EX4-068) · "Suspend all of your yellow Tamers. For each yellow Tamer you suspend by this effect, ~" (BT2-041) ·
///    "~. Then, for every 5 of this Digimon's digivolution cards, activate 1 of the effects below:" (EX12-037)
/// 렌더러도 같은 표 (KoreanEffectRenderer.Batch57.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth57;
    [ThreadStatic] private static int _gate57;
    internal static bool Gate57 => _gate57 > 0;

    private static string? TryParse57(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse56(rawClause, inherited, card, def);
        if (err == null || _clauseDepth57 > 0) return err;
        _clauseDepth57++;
        _gate57++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (ClauseSpecial57(rawClause, inherited, card, def) is { } special) return special.Length == 0 ? null : err;
            if (SplitTwoEffects57(rawClause) is { } two)
            {
                if (TryParse56(two.First, inherited, card, null) != null || TryParse56(two.Second, inherited, card, null) != null) return err;
                if (def != null) { TryParse56(two.First, inherited, card, def); TryParse56(two.Second, inherited, card, def); }
                return null;
            }
            // 문이 열린 채로 다시 읽는다 (31차의 셈 · 동작 · 조건은 문이 열려 있을 때만) — 바꿔 쓸 것이 없어도
            string alt = ClauseRewrite57(rawClause);
            // v0.58.0 바꿔 쓴 것도 31차 셈 · 동작 · 조건 · 비용 표현(HintParse57)도 없으면 다시 읽지 않는다 — 결과는 같고, 읽지 못하는 절마다 원래 규칙 전체를 한 번 더 읽어 도감 계산이 두 배 넘게 걸렸다
            if (alt == rawClause && !HintParse57.IsMatch(rawClause)) return err;
            if (TryParse56(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse56(alt, inherited, card, def);
            return null;
        }
        finally
        {
            _clauseDepth57--;
            _gate57--;
            if (def != null) WrapGate57(def, before);
        }
    }

    /// <summary>
    /// v0.58.0 문이 열려 있을 때만 읽는 31차 표현 (ParseCounter57 · ParseEventCondition57 · ParseStaticCondition57 · ParseCost57 · ParseAction57)이 절에 있는지.
    /// 없으면 문을 열고 다시 읽어도 결과가 같다 (도감 결과가 바뀌지 않는 것을 전 카드로 확인)
    /// </summary>
    private static readonly Regex HintParse57 = new(@"DP this Digimon has|of this Digimon's link cards|same-level cards|colou?r your Digimon have|or more (?:suspended )?Digimon with|or more colou?rs in its digivolution|no Digimon with (?:equal|DP equal)|up to \d+ memory|Tamers? (?:this effect )?suspended|memory paid|from your trash for its memory cost|DigiXros from your trash", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>31차 규칙으로 등록한 시점 효과는 처리할 때도 문을 연다</summary>
    private static void WrapGate57(CardScriptDefinition def, int from)
    {
        var effects = def.Script41.Effects;
        for (int i = from; i < effects.Count; i++)
        {
            var inner = effects[i].Resolve;
            effects[i] = effects[i] with { Resolve = c => { _gate57++; try { inner(c); } finally { _gate57--; } } };
        }
    }

    internal static readonly Regex TamerSuspendLockRe57 = new(@"^\[Opponent's Turn\]\s*All (?:of )?your opponent's Tamers with (?:a )?play costs? of (?<n>\d+) or less can't suspend\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 "" (등록함), 해당 없으면 null</summary>
    private static string? ClauseSpecial57(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string t = Normalize(rawClause);
        // EX5-052 "[Opponent's Turn] All of your opponent's Tamers with play costs of 2 or less can't suspend." — 상대의 턴(테이머 주인의 턴) 동안 레스트할 수 없다 (비용 · 효과 모두)
        if (!inherited && card.Type == CardType.Digimon && TamerSuspendLockRe57.Match(t) is { Success: true } m)
        {
            int n = int.Parse(m.Groups["n"].Value);
            if (def != null) def.Script41.OpponentTamerSuspendLock57 = tamer => tamer.PlayCost <= n;
            return "";
        }
        return null;
    }

    /// <summary>절 하나에 지속 효과가 둘 (쉼표 · 문장으로 이어 씀) — 나눈 두 절 (렌더러도 쓴다)</summary>
    internal static (string First, string Second)? SplitTwoEffects57(string rawClause)
    {
        string t = Regex.Replace(rawClause.Replace(' ', ' ').Trim(), @"^Inherited Effect\s*", "", I);
        const string Paren = @"(?:\s*\((?:[^()]|\([^)]*\))*\))?";
        // AD1-004 "[All Turns] This Digimon gets +1000 DP for each of your Tamers' colors, and gains ＜Security A. +1＞ (~) for every 3 of their colors."
        var m = Regex.Match(t, $@"^\[All Turns\]\s*This Digimon gets \+(?<dp>\d+) DP for each of your Tamers' colou?rs, and gains (?<k>＜Security A\. \+\d+＞){Paren} for every (?<n>\d+) of their colou?rs\.?$", I);
        if (m.Success)
            return ($"[All Turns] This Digimon gets +{m.Groups["dp"].Value} DP for each of your Tamers' colors.",
                    $"[All Turns] This Digimon gains {m.Groups["k"].Value} for every {m.Groups["n"].Value} of your Tamers' colors.");
        // BT12-031 "[All Turns] This Digimon gets +1000 DP for each color in its digivolution cards. While there are 2 or more colors in its digivolution cards, it gains ＜Security A. +1＞ (~) and ＜Blocker＞ (~)."
        m = Regex.Match(t, $@"^(?<q>\[(?:All Turns|Your Turn)\])\s*This Digimon gets \+(?<dp>\d+) DP for each colou?r in its digivolution cards\. While there are (?<n>\d+) or more colou?rs in its digivolution cards, it gains (?<k1>＜[^＞]+＞){Paren} and (?<k2>＜[^＞]+＞){Paren}\.?$", I);
        if (m.Success)
            return ($"{m.Groups["q"].Value} This Digimon gets +{m.Groups["dp"].Value} DP for each color in its digivolution cards.",
                    $"{m.Groups["q"].Value} While this Digimon has {m.Groups["n"].Value} or more colors in its digivolution cards, this Digimon gains {m.Groups["k1"].Value} and {m.Groups["k2"].Value}.");
        // EX8-045 "[Your Turn] For each color in this Digimon's digivolution cards, it gets +1000 DP. While your opponent has no Digimon with equal or higher DP, this Digimon gets ＜Piercing＞ (~) and ＜Security A. +1＞ (~)"
        m = Regex.Match(t, $@"^(?<q>\[(?:All Turns|Your Turn)\])\s*For each colou?r in this Digimon's digivolution cards, it gets \+(?<dp>\d+) DP\. While your opponent has no Digimon with equal or higher DP, this Digimon (?:gets|gains) (?<k1>＜[^＞]+＞){Paren} and (?<k2>＜[^＞]+＞){Paren}\.?$", I);
        if (m.Success)
            return ($"{m.Groups["q"].Value} This Digimon gets +{m.Groups["dp"].Value} DP for each color in its digivolution cards.",
                    $"{m.Groups["q"].Value} While your opponent has no Digimon with DP equal to or higher than this Digimon, this Digimon gains {m.Groups["k1"].Value} and {m.Groups["k2"].Value}.");
        return null;
    }

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules57 =
    {
        // BT17-041 "By suspending up to 2 of your yellow Tamers, for every Tamer this effect suspended, this Digimon gains ＜Security A. +1＞ for the turn."
        s => Regex.Replace(s, @"\bBy suspending (?<c>[^,]+? Tamers), for (?:every|each) Tamer this effect suspended, (?<a>this Digimon gains ＜Security A\. \+\d+＞ for the turn)\.", "By suspending ${c}, ${a} for each Tamer suspended.", RegexOptions.IgnoreCase),
        // BT6-111 "You may pay up to 5 memory. If you do, this Digimon gets +1000 DP for the turn for each memory paid."
        s => Regex.Replace(s, @"\bYou may pay up to (?<n>\d+) memory\. If you do, (?<a>[^.]+? for each memory paid)\.", "By paying up to ${n} memory, ${a}.", RegexOptions.IgnoreCase),
        // EX11-073 "For each of this Digimon's link cards, trash your opponent's top security card and return 1 of their Digimon to the bottom of the deck." — 1장마다 둘 다
        s => Regex.Replace(s, @"\bFor each of this Digimon's link cards, trash your opponent's top security card and return 1 of their Digimon to the bottom of the deck\.",
            "For each of this Digimon's link cards, trash your opponent's top security card. Then, for each of this Digimon's link cards, return 1 of your opponent's Digimon to the bottom of the deck.", RegexOptions.IgnoreCase),
        // BT10-104 "you may play 1 [DarkKnightmon] from your trash for its memory cost. If you play a Digimon card with DigiXros requirements by this effect, you may DigiXros by placing any of the specified Digimon from your trash at the bottom of this card as digivolution cards."
        s => Regex.Replace(s, @"\bplay 1 (?<x>\[[^\]]+\]) from your trash for its memory cost\. If you play a Digimon card with DigiXros requirements by this effect, you may DigiXros by placing any of the specified Digimon from your trash at the bottom of this card as digivolution cards\.",
            "play 1 ${x} from your trash paying its play cost with DigiXros from your trash.", RegexOptions.IgnoreCase),
    };

    /// <summary>같은 뜻 다른 표기 (계속) — 셈 · 조건의 순서만 바꾼 것</summary>
    internal static readonly Func<string, string>[] ClauseRules57b =
    {
        // EX3-045 "When ~, for each other suspended Digimon with [A], [B] or [C] in any of its traits you have in play, gain 1 memory."
        s => Regex.Replace(s, @", for each other suspended Digimon with (?<x>\[[^\]]+\], \[[^\]]+\] or \[[^\]]+\]) in any of its traits you have in play, gain (?<n>\d+) memory\.", ", gain ${n} memory for each other suspended Digimon with ${x} in one of their traits you have.", RegexOptions.IgnoreCase),
        // EX3-045 "If you have 2 or more suspended Digimon with [A], [B] or [C] in one of their traits, return 1 of your opponent's suspended Digimon at the bottom of its owner's deck."
        s => Regex.Replace(s, @"\bIf you have (?<n>\d+) or more suspended Digimon with \[(?<a>[^\]]+)\], \[(?<b>[^\]]+)\] or \[(?<c>[^\]]+)\] in one of their traits, return 1 of your opponent's suspended Digimon at the bottom of its owner's deck\.",
            "If you have ${n} or more suspended Digimon with [${a}] or [${b}] or [${c}] in one of their traits, return 1 of your opponent's suspended Digimon to the bottom of the deck.", RegexOptions.IgnoreCase),
        // BT10-057 "Then, gain 1 memory for each suspended Digimon with [A], [B] or [C] in one of their traits you have in play. If you gain 2 or more memory by this effect, unsuspend this Digimon and it gains ＜Piercing＞ for the turn."
        //  — 얻은 메모리 = 그 디지몬의 수 (1마리마다 1) → "그 디지몬이 2마리 이상이면"
        s => Regex.Replace(s, @"\bThen, gain 1 memory for each suspended Digimon with \[(?<a>[^\]]+)\], \[(?<b>[^\]]+)\] or \[(?<c>[^\]]+)\] in one of their traits you have in play\. If you gain (?<n>\d+) or more memory by this effect, (?<r>[^.]+)\.",
            "Then, gain 1 memory for each suspended Digimon with [${a}], [${b}] or [${c}] in one of their traits you have. Then, if you have ${n} or more suspended Digimon with [${a}] or [${b}] or [${c}] in one of their traits, ${r}.", RegexOptions.IgnoreCase),
        // EX2-018 "For each ~, ＜Recovery +1 (Deck)＞. This effect can't increase the number of cards in your security stack to 6 or more." — 1장마다 시큐리티가 5장 미만일 때만
        s => Regex.Replace(s, @"^(?<a>.*\bFor each [^,]+), (?<r>＜Recovery \+1 \(Deck\)＞)(?: \([^)]*\))?\. This effect can't increase the number of cards in your security stack to (?<n>\d+) or more\.?$",
            m => $"{m.Groups["a"].Value}, if you have {int.Parse(m.Groups["n"].Value) - 2} or fewer security cards, {m.Groups["r"].Value}.", RegexOptions.IgnoreCase),
        // ST20-11 "Until your opponent's turn ends, for every 2 colors your Tamers have, their Digimon's effects don't affect 1 of your Digimon."
        s => Regex.Replace(s, @"\bUntil your opponent's turn ends, for every (?<n>\d+) colou?rs your Tamers have, their Digimon's effects don't affect 1 of your Digimon\.",
            "For every ${n} of your Tamers' colors, 1 of your Digimon isn't affected by your opponent's Digimon effects until your opponent's turn ends.", RegexOptions.IgnoreCase),
        // P-112 "this Digimon may digivolve into an [Eosmon] in your hand, reducing the digivolution cost reduced by 3." (원문 오타)
        s => Regex.Replace(s, @", reducing the digivolution cost reduced by (?<n>\d+)\.", " with the digivolution cost reduced by ${n}.", RegexOptions.IgnoreCase),
        // P-085 "If you have a purple Tamer in play, if its your turn, you may digivolve this Digimon into ~ from your trash by paying its digivolution cost."
        s => Regex.Replace(s, @"\bIf you have (?<t>a (?:Red|Blue|Yellow|Green|Black|Purple|White) Tamer) in play, if it'?s your turn, you may digivolve this Digimon into (?<x>.+?) from your trash by paying its digivolution cost\.",
            "If it's your turn and you have ${t} in play, you may digivolve this Digimon into ${x} in your trash for its digivolution cost.", RegexOptions.IgnoreCase),
        // BT9-029 "You may trash 1 Digimon card with ~ in your hand to place 1 of your opponent's level 4 or lower Digimon to the bottom of its owner's deck."
        s => Regex.Replace(s, @"\bYou may trash (?<c>1 Digimon card with .+? in your hand) to place (?<t>1 of your opponent's [^.]+?) to the bottom of its owner's deck\.",
            "By trashing ${c}, return ${t} to the bottom of the deck.", RegexOptions.IgnoreCase),
        // BT12-016 "If no opponent's Digimon was deleted by this effect, you may digivolve ~ in your hand for the digivolution cost. When this Digimon would digivolve with this effect, reduce the digivolution cost by 1."
        s => Regex.Replace(s, @"\bIf no opponent's Digimon was deleted by this effect, you may digivolve this Digimon into (?<x>.+?) in your hand for the digivolution cost\. When this Digimon would digivolve with this effect, reduce the digivolution cost by (?<n>\d+)\.",
            "If no Digimon was deleted by this effect, you may digivolve this Digimon into ${x} in your hand with the digivolution cost reduced by ${n}.", RegexOptions.IgnoreCase),
        // ST13-06 "＜Blitz＞ (~) When DNA digivolving, for every 4 cards in this Digimon's digivolution cards, delete 1 of ~ and trash the top card of your opponent's security stack."
        s => Regex.Replace(s, @"(?<b>＜Blitz＞)(?: \((?:[^()]|\([^)]*\))*\))? When DNA digivolving, for every (?<n>\d+) cards in this Digimon's digivolution cards, (?<d>delete 1 of your opponent's Digimon with a play cost of \d+ or less) and trash the top card of your opponent's security stack\.",
            "${b}. If DNA digivolving, for every ${n} cards in this Digimon's digivolution cards, ${d}. Then, if DNA digivolving, for every ${n} cards in this Digimon's digivolution cards, trash the top card of your opponent's security stack.", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite57(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules57) s = f(s);
        foreach (var f in ClauseRules57b) s = f(s);
        return s;
    }

    // =====================================================================
    // 셈 · 조건 · 비용 · 동작 (문이 열려 있을 때만)
    // =====================================================================

    /// <summary>"N same-level cards this Digimon's stack has" — 겹쳐진 카드(맨 위 + 진화원) 중 같은 Lv.의 카드 N장마다 (Lv.마다 N장씩 묶는다)</summary>
    internal static int SameLevelGroups57(CardInstance? self, int per) =>
        self == null || per <= 0 ? 0
        : new[] { self.TopCard }.Concat(self.EvolutionSources).Where(c => c.Level > 0).GroupBy(c => c.Level).Sum(g => g.Count() / per);

    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter57(string counter)
    {
        if (!Gate57) return null;
        string s = counter.Trim().TrimEnd('.');
        Match m;
        // EX13-020 "5000 DP this Digimon has" — 이 디지몬의 지금 DP를 5000으로 나눈 몫
        if ((m = Regex.Match(s, @"^(?<n>\d+) DP this Digimon has$", I)).Success)
        {
            int per = int.Parse(m.Groups["n"].Value);
            return (_, _, self) => per > 0 && self != null ? Math.Max(0, self.GetCurrentDP()) / per : 0;
        }
        // EX11-073 "of this Digimon's link cards"
        if (Regex.IsMatch(s, @"^of this Digimon's link cards$", I))
            return (_, _, self) => self?.LinkedCards.Count ?? 0;
        // BT22-015 "2 same-level cards this Digimon's stack has"
        if ((m = Regex.Match(s, @"^(?<n>\d+) same-level cards this Digimon's stack has$", I)).Success)
        {
            int per = int.Parse(m.Groups["n"].Value);
            return (_, _, self) => SameLevelGroups57(self, per);
        }
        // EX4-068 "color your Digimon have" — 자신의 디지몬이 가진 색
        if (Regex.IsMatch(s, @"^colou?r your Digimon have$", I))
            return (o, _, _) => o.BattleArea.Where(d => d.TamerProxyOf == null).SelectMany(d => d.Colors).Distinct().Count();
        return null;
    }

    /// <summary>조건 (If ~): "you have 2 or more suspended Digimon with [A] or [B] or [C] in one of their traits" (EX3-045 · BT10-057)</summary>
    private static Func<EffectContext, bool>? ParseEventCondition57(string cond)
    {
        if (!Gate57) return null;
        var m = Regex.Match(cond.Trim(), @"^you have (?<n>\d+) or more (?<x>(?:suspended )?Digimon with .+)$", I);
        if (!m.Success || TargetSpec.Parse("all of your " + m.Groups["x"].Value) is not { ContextFilters.Count: 0, Opponent: false, AnySide: false } spec) return null;
        int n = int.Parse(m.Groups["n"].Value);
        return c => c.Owner.BattleArea.Count(d => d.TamerProxyOf == null && spec.Filters.All(f => f(d))) >= n;
    }

    /// <summary>지속 효과의 조건 (While ~)</summary>
    private static Func<StaticContext, bool>? ParseStaticCondition57(string cond)
    {
        if (!Gate57) return null;
        string s = cond.Trim();
        Match m;
        // BT12-031 "this Digimon has 2 or more colors in its digivolution cards"
        if ((m = Regex.Match(s, @"^this Digimon has (?<n>\d+) or more colou?rs in its digivolution cards$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return c => c.Digimon != null && c.Digimon.FaceUpSources.SelectMany(x => x.AllColors).Distinct().Count() >= n;
        }
        // EX8-045 "your opponent has no Digimon with DP equal to or higher than this Digimon"
        if (Regex.IsMatch(s, @"^your opponent has no Digimon with DP equal to or higher than this Digimon$", I))
            return c => c.Digimon != null && !c.Enemy.BattleArea.Any(d => d.TamerProxyOf == null && d.GetCurrentDP() >= c.Digimon.GetCurrentDP());
        return null;
    }

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost57(string cost, CardData card)
    {
        if (!Gate57) return null;
        Match m;
        // BT6-111 "paying up to 5 memory" — 0 ~ N 중 고른다 (AI: 메모리를 상대 쪽으로 넘기지 않는 만큼). 낸 양은 PaidCount, 0이면 내지 않은 것
        if ((m = Regex.Match(cost.Trim(), @"^paying up to (?<n>\d+) memory$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (_ => true,
                    c =>
                    {
                        int mem = c.Engine.MemoryOf(c.Owner);
                        int rec = Math.Clamp(mem, 0, n);
                        int k = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 지불할 메모리를 고르세요 (최대 {n}).", Enumerable.Range(0, n + 1).Select(i => $"{i}").ToList(), rec);
                        k = Math.Clamp(k, 0, n);
                        c.PaidCount = k;
                        if (k == 0) { c.CostFailed = true; return; }
                        c.Engine.AddMemory(c.Owner, -k);
                        if (ReferenceEquals(c.Owner, c.Engine.State.Player)) c.Engine.State.TotalMemorySpent += k;
                        c.Log($"[{c.Card.Name}] 메모리 {k} 지불");
                        Diag46.Hit("payUpToMemory57");
                    },
                    c => c.Engine.MemoryOf(c.Owner) > 0,
                    $"메모리를 {n}까지 지불하고");
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction57(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (!Gate57) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;
        // BT17-041 "this Digimon gains ＜Security A. +1＞ for the turn for each Tamer suspended" — 비용으로 레스트시킨 수(PaidCount)만큼
        if ((m = Regex.Match(s, @"^(?<t>this Digimon) gains ＜Security A\. \+(?<n>\d+)＞ for the turn for each Tamer suspended$", I)).Success)
        {
            int unit = int.Parse(m.Groups["n"].Value);
            string who = m.Groups["t"].Value;
            if (ParseAction($"{who} gains ＜Security A. +{unit}＞ for the turn", card, triggers).Error != null) return null;
            var byK = new Dictionary<int, Action<EffectContext>?>();
            return (c =>
            {
                int k = Math.Min(c.PaidCount, 20);
                if (k <= 0) { c.LastStepResolved = false; return; }
                if (!byK.TryGetValue(k, out var a)) byK[k] = a = ParseAction($"{who} gains ＜Security A. +{unit * k}＞ for the turn", card, triggers).Act;
                a?.Invoke(c);
                Diag46.Hit("saPerSuspended57");
            }, null);
        }
        // BT6-111 "this Digimon gets +1000 DP for the turn for each memory paid"
        if ((m = Regex.Match(s, @"^(?<t>.+?) gets (?<sign>[+-])(?<n>\d+) DP(?<dur> for the turn| until [^,]+?)? for each memory paid$", I)).Success)
        {
            int unit = int.Parse(m.Groups["n"].Value);
            string head = $"{m.Groups["t"].Value} gets {m.Groups["sign"].Value}", dur = m.Groups["dur"].Value;
            if (ParseAction($"{head}{unit} DP{dur}", card, triggers).Error != null) return null;
            var byK = new Dictionary<int, Action<EffectContext>?>();
            return (c =>
            {
                int k = Math.Min(c.PaidCount, 20);
                if (k <= 0) { c.LastStepResolved = false; return; }
                if (!byK.TryGetValue(k, out var a)) byK[k] = a = ParseAction($"{head}{unit * k} DP{dur}", card, triggers).Act;
                a?.Invoke(c);
            }, null);
        }
        // BT10-104 "play 1 [DarkKnightmon] from your trash paying its play cost with DigiXros from your trash" — 코스트를 내고 등장, 디지크로스 조건이 있으면 트래시의 카드로 디지크로스할 수 있다
        if ((m = Regex.Match(s, @"^(?<may>you may )?play 1 (?<x>\[[^\]]+\]) from your trash paying its play cost with DigiXros from your trash$", I)).Success)
        {
            var inner = ParseAction($"{m.Groups["may"].Value}play 1 {m.Groups["x"].Value} from your trash with the play cost reduced by 0", card, triggers);
            if (inner.Error != null || inner.Act == null) return null;
            var act = inner.Act;
            return (c =>
            {
                c.Engine.EffectXrosFromTrash57++;
                try { act(c); }
                finally { c.Engine.EffectXrosFromTrash57--; }
            }, null);
        }
        return null;
    }

    // =====================================================================
    // 아래 효과를 여러 번 (고르는 효과)
    // =====================================================================

    /// <summary>고르는 효과: 원래 규칙이 읽지 못했을 때만 31차 규칙으로</summary>
    private static string? TryParseChoice57(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoice50(rawHeader, rawOptions, inherited, card, def);
        if (err == null || _gate57 > 0) return err;
        _gate57++;
        int before = def?.Script41.Effects.Count ?? 0;
        try
        {
            if (RepeatChoice57(rawHeader, rawOptions, inherited, card, null) != null) return err;
            if (def != null) RepeatChoice57(rawHeader, rawOptions, inherited, card, def);
            return null;
        }
        finally
        {
            _gate57--;
            if (def != null) WrapGate57(def, before);
        }
    }

    internal static readonly Regex AgainRe57 = new(@"^Activate the effect below\. For each (?<cnt>[^,]+), activate it again[:.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex SuspendEachRe57 = new(@"^(?<pre>Suspend all of your (?<c>(?:Red|Blue|Yellow|Green|Black|Purple|White) )?Tamers)\. For each (?:(?:Red|Blue|Yellow|Green|Black|Purple|White) )?Tamer you suspend(?:ed)? by this effect, activate the effect below[:.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex ThenEveryChooseRe57 = new(@"^(?<pre>[^.]+)\. Then, for (?:each|every) (?<cnt>[^,]+), activate (?:1|one) of the effects below[:.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? RepeatChoice57(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string header = Normalize(rawHeader);
        var prefix = Regex.Match(header, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        var labels = Regex.Matches(prefix.Groups[1].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim()).ToList();
        var triggers = new List<EffectTrigger>();
        bool oncePerTurn = false;
        foreach (var label in labels)
        {
            if (EventTriggers.TryGetValue(label, out var tr)) triggers.Add(tr);
            else if (label.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase)) oncePerTurn = true;
            else return $"미지원 시점 [{label}]";
        }
        if (triggers.Count == 0) return "미지원: 시점 없는 고르는 효과";
        if (!MapMainTrigger(triggers, card, inherited)) return "미지원: 진화원의 [메인] 효과";
        string head = prefix.Groups[2].Value.Trim();

        Step? pre = null;
        Func<EffectContext, int> times;
        bool chooseEach = false;
        bool suspendCount = false;
        CardColor? suspendColor = null;
        Match m;
        if ((m = AgainRe57.Match(head)).Success)
        {
            if (ParseCounter(m.Groups["cnt"].Value) is not { } counter) return $"미지원 셈 (For each {m.Groups["cnt"].Value})";
            times = c => 1 + Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
        }
        else if ((m = SuspendEachRe57.Match(head)).Success)
        {
            // "Suspend all of your yellow Tamers" — 레스트할 수 있는 그 색의 자신의 테이머를 전부 (레스트시킨 수만큼)
            suspendColor = m.Groups["c"].Success ? Enum.Parse<CardColor>(m.Groups["c"].Value.Trim(), true) : null;
            suspendCount = true;
            times = _ => 0;
        }
        else if ((m = ThenEveryChooseRe57.Match(head)).Success)
        {
            var st = ParseStep(m.Groups["pre"].Value, card, triggers);
            if (st.Error != null || st.Step == null) return st.Error ?? "미지원 동작";
            pre = st.Step;
            if (ParseCounter(m.Groups["cnt"].Value) is not { } counter) return $"미지원 셈 (For every {m.Groups["cnt"].Value})";
            times = c => Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
            chooseEach = true;
        }
        else return "문장 파서(3단계) 대상: 고르는 효과";

        var options = new List<Step[]>();
        foreach (var raw in rawOptions)
        {
            var steps = new List<Step>();
            foreach (var sentence in MergeRevealSentences(SplitSentences(Normalize(raw))))
            {
                var step = ParseStep(sentence, card, triggers);
                if (step.Error != null) return step.Error;
                if (step.Step != null) steps.Add(step.Step);
            }
            options.Add(steps.ToArray());
        }
        if (options.Count == 0 || (!chooseEach && options.Count != 1)) return "미지원: 선택지 수";
        var optionTexts = rawOptions.Select(o => Normalize(o).TrimStart('・').Trim()).ToArray();
        var values = optionTexts.Select(t => SplitSentences(t).Sum(AiEvaluator.EstimateSentenceValue)).ToArray();
        int recommended = Array.IndexOf(values, values.Max());
        var recommend58 = Recommender58(optionTexts, values);   // v0.58.0 고를 때마다 지금 할 수 있는 선택지를 추천 (SentenceParser.Batch58.cs)

        Action<EffectContext> resolve = c =>
        {
            int k;
            if (suspendCount)
            {
                int before = c.Owner.SuspendedTamers.Count;
                for (int guard = 0; guard < 20; guard++)
                {
                    var t = c.Owner.Tamers.FirstOrDefault(x => (suspendColor == null || c.Owner.TamerColorsOf66(x).Contains(suspendColor.Value)) && c.Owner.HasActiveTamer(x) && !c.Engine.TamerSuspendLocked(c.Owner, x));
                    if (t == null) break;
                    int b = c.Owner.SuspendedTamers.Count;
                    c.Engine.SuspendTamer(c.Owner, t);
                    if (c.Owner.SuspendedTamers.Count == b) break;
                }
                k = Math.Max(0, c.Owner.SuspendedTamers.Count - before);
                c.Log($"[{c.Card.Name}] 테이머 {k}명 레스트");
            }
            else if (pre != null)
            {
                RunStep(pre, c);
                k = times(c);
            }
            else k = times(c);
            for (int i = 0; i < k && !c.Engine.State.IsGameOver; i++)
            {
                if (chooseEach)
                {
                    int pick = options.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 발휘할 효과를 1개 고르세요. ({i + 1}/{k})", optionTexts, recommend58(c));
                    foreach (var s in options[Math.Clamp(pick, 0, options.Count - 1)]) RunStep(s, c);
                }
                else
                    foreach (var s in options[0]) RunStep(s, c);
            }
            if (k > 0) Diag46.Hit("repeatChoice57");
        };
        if (def == null) return null;
        foreach (var t in triggers)
        {
            if (t == EffectTrigger.Main) def.Main(TargetKind.None, resolve);
            else if (inherited) def.Inherited(t, resolve, null, oncePerTurn, false);
            else def.On(t, resolve, null, oncePerTurn, false);
        }
        return null;
    }
}
