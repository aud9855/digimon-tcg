using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.37.0 미지원 문장 11차 묶음: 메모리 조건 지속 효과 · DP 소멸 효과의 상한 · "can't play (or move) Digimon with N DP or less" ·
/// 색 · DNA 진화 이름 취급 · 상대의 디지몬 전부의 제한 (엔진: GoldfishEngine.Batch37.cs)
/// </summary>
internal static partial class SentenceParser
{
    // =====================================================================
    // 1. 조건
    // =====================================================================

    /// <summary>
    /// 지속 효과의 메모리 조건: "you have 0 or less memory" · "you have 3 or more memory" · "your opponent has 1 or more memory" (공식 Q&amp;A EX8-026: 자신 쪽의 1 이상) —
    /// 효과 주인 기준 (상대의 메모리 = 주인 메모리의 반대)
    /// </summary>
    private static Func<StaticContext, bool>? ParseStaticCondition37(string cond)
    {
        Match m;
        cond = cond.Trim();
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>less|fewer|more) memory$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
            return c => more ? c.OwnerMemory >= n : c.OwnerMemory <= n;
        }
        if ((m = Regex.Match(cond, @"^(?:your opponent has|they have) (?<n>\d+) or (?<dir>less|fewer|more) memory$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
            return c => more ? -c.OwnerMemory >= n : -c.OwnerMemory <= n;
        }
        return null;
    }

    // =====================================================================
    // 2. 지속 효과
    // =====================================================================

    private static readonly Dictionary<string, CardColor> ColorWords37 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = CardColor.Red, ["blue"] = CardColor.Blue, ["yellow"] = CardColor.Yellow, ["green"] = CardColor.Green,
        ["black"] = CardColor.Black, ["purple"] = CardColor.Purple, ["white"] = CardColor.White,
    };

    /// <summary>
    /// v0.37.0 지속 효과 (ParseStatic36 다음): DP 소멸 효과의 상한 · 색 추가 · 상대의 디지몬 전부의 제한 · "they can't play Digimon with N DP or less" ·
    /// "it doesn't activate [Security] effects on Option cards it checks" · DNA 진화 이름 취급. 읽지 못하면 null, 읽으면 ""
    /// </summary>
    private static string? ParseStatic37(string s, bool inherited, CardData card, Func<StaticContext, bool> cond, Pending pending)
    {
        Match m;
        s = s.Trim().TrimEnd('.');
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        var c1 = cond;
        // "add 2000 to this Digimon's DP-based deletion effects' maximums" (BT17-008 · BT19-007 · EX13-007 — 공식 "이 디지몬의 DP 소멸 효과의 상한 +2000")
        if ((m = Regex.Match(s, @"^add (?<n>\d+) to this Digimon's DP(?:-based)? deletion effects'? maximums?$", I)).Success && card.Type == CardType.Digimon)
        {
            int n = int.Parse(m.Groups["n"].Value);
            pending.Registrations.Add(d => d.Static(StaticStat.DeleteCapSelf, source, c => c1(c) ? n : 0));
            return "";
        }
        // "Add 1000 to the maximum DP you can choose with DP-based deletion effects" (BT12-001 · BT9-011 · EX2-010 · EX2-011 — 공식 "자신이 발휘하는 DP 소멸 효과의 상한 +1000")
        if ((m = Regex.Match(s, @"^add (?<n>\d+) to the maximum DP (?:you (?:can|may)|that can be) choose(?:n)? with DP-based deletion effects$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            pending.Registrations.Add(d => d.Static(StaticStat.DeleteCapAll, card.Type == CardType.Tamer ? EffectSource.Main : source, c => c1(c) ? n : 0));
            return "";
        }
        // "This Digimon is also treated as yellow" (BT3-014 · BT6-061 · EX1-005 등 — 공식 "이 디지몬의 색은 옐로로도 취급한다")
        if ((m = Regex.Match(s, @"^(?:this Digimon|it) is also treated as (?<c1>red|blue|yellow|green|black|purple|white)(?:(?:,| and| or) (?<c2>red|blue|yellow|green|black|purple|white))?$", I)).Success && card.Type == CardType.Digimon)
        {
            int mask = 1 << (int)ColorWords37[m.Groups["c1"].Value];
            if (m.Groups["c2"].Success) mask |= 1 << (int)ColorWords37[m.Groups["c2"].Value];
            pending.Registrations.Add(d => d.Static(StaticStat.ExtraColors, source, c => c1(c) ? mask : 0));
            return "";
        }
        // "This Digimon is treated as also having the color of its digivolution cards" (BT8-084 — 앞면 진화원의 색)
        if (Regex.IsMatch(s, @"^(?:this Digimon|it) is treated as also having the colou?rs? of its digivolution cards$|^(?:this Digimon|it) gains all colou?rs in its digivolution cards$", I) && card.Type == CardType.Digimon)
        {
            pending.Registrations.Add(d => d.Static(StaticStat.ExtraColors, source, c =>
            {
                if (!c1(c) || c.Digimon == null) return 0;
                int mask = 0;
                foreach (var src in c.Digimon.FaceUpSources)
                    foreach (var col in src.AllColors) mask |= 1 << (int)col;
                return mask;
            }));
            return "";
        }
        // "none of your opponent's Digimon can suspend" (EX8-026) · "~ can activate [When Digivolving] / [On Play] effects" (EX8-035 · EX8-029)
        if ((m = Regex.Match(s, @"^none of your opponent's Digimon can (?:(?<sus>suspend)|activate \[(?<when>When Digivolving|On Play|When Attacking)\] effects)$", I)).Success && card.Type == CardType.Digimon)
        {
            Keyword kw = m.Groups["sus"].Success ? Keyword.CannotSuspend
                : m.Groups["when"].Value.StartsWith("On", StringComparison.OrdinalIgnoreCase) ? Keyword.CannotActivateOnPlay
                : m.Groups["when"].Value.EndsWith("Attacking", StringComparison.OrdinalIgnoreCase) ? Keyword.CannotActivateWhenAttacking : Keyword.CannotActivateWhenDigivolving;
            pending.Registrations.Add(d => d.Static(StaticStat.EnemyKeyword, source, c => c1(c) ? 1 : 0, kw));
            return "";
        }
        // "they can't play Digimon with 6000 DP or less, and this Digimon gets +4000 DP" (BT14-017 — 공식 "DP 6000 이하의 상대의 디지몬은 등장할 수 없으며, 이 디지몬을 DP +4000")
        if ((m = Regex.Match(s, @"^(?:they|your opponent) can't play Digimon with (?<n>\d+) DP or less(?:,? and (?<tail>this Digimon gets [+-]\d+ DP))?$", I)).Success && card.Type == CardType.Digimon)
        {
            int n = int.Parse(m.Groups["n"].Value);
            if (m.Groups["tail"].Success)
            {
                var dp = Regex.Match(m.Groups["tail"].Value, @"(?<v>[+-]\d+) DP", I);
                int v = int.Parse(dp.Groups["v"].Value);
                pending.Registrations.Add(d => d.Static(StaticStat.Dp, source, c => c1(c) ? v : 0));
            }
            pending.Registrations.Add(d => d.Static(StaticStat.OpponentPlayDpLock, source, c => c1(c) ? n : 0));
            return "";
        }
        // "it doesn't activate [Security] effects on Option cards it checks" (BT7-014 · BT17-014 · ST13-05 · BT20-071 — 체크한 옵션 카드의 【시큐리티】 효과는 발휘하지 않는다)
        if (Regex.IsMatch(s, @"^(?:this Digimon|it)(?: with (?:the )?\[[^\]]+\](?: or \[[^\]]+\])* traits?)? doesn't activate \[Security\] effects on (?:option|Option) cards it checks$", I) && card.Type == CardType.Digimon)
        {
            var c2 = cond;
            var tm = Regex.Match(s, @"with (?:the )?(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) traits?", I);
            if (tm.Success)
            {
                var xs = TargetSpec.Names(tm.Groups["x"].Value);
                c2 = c => cond(c) && c.Digimon != null && xs.Any(x => TargetSpec.HasTrait(c.Digimon.TopCard, x));
            }
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, Keyword.NegateSecurityOptions));
            return "";
        }
        // "This Digimon is also treated as Lv.6 [Slayerdramon] for [Examon]'s DNA digivolution" (BT20-025 · BT20-042 · EX13-021 · EX13-041 — Q&A BT20-025: 배틀 에어리어에 있는 동안만)
        if ((m = Regex.Match(s, @"^this Digimon is also treated as Lv\.\s*(?<lv>\d+) \[(?<as>[^\]]+)\] for \[(?<for>[^\]]+)\]'s DNA digivolution$", I)).Success && card.Type == CardType.Digimon && !inherited)
        {
            string asName = m.Groups["as"].Value, forName = m.Groups["for"].Value; int lv = int.Parse(m.Groups["lv"].Value);
            pending.Registrations.Add(d => d.DnaAlias(forName, asName, lv));
            return "";
        }
        return null;
    }

    // =====================================================================
    // 3. 동작
    // =====================================================================

    /// <summary>
    /// "Your opponent can't play Digimon with 5000 DP or less until the end of their turn" (EX3-012) · "Your opponent can't play or move Digimon with 6000 DP or less until ~" (EX7-014 — 공식 "등장시킬 수 없으며, 이동시킬 수 없다")
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? PlayDpLock37(string s, string label)
    {
        Duration dur = ParseDuration(s, out string rest);
        if (dur is Duration.None or Duration.ThisAttack) return null;
        var m = Regex.Match(rest.Trim().TrimEnd(','), @"^(?:your opponent|they) can't play (?<move>or move )?Digimon with (?<n>\d+) DP or less$", I);
        if (!m.Success) return null;
        int n = int.Parse(m.Groups["n"].Value); bool move = m.Groups["move"].Success;
        return (c =>
        {
            c.Enemy.PlayDpLocks.Add((n, Until(c, dur), move));
            Emit(c, label, $"상대는 DP {n} 이하의 디지몬을 등장{(move ? " · 이동" : "")}시킬 수 없다");
        }, null);
    }

    /// <summary>v0.37.0 동작 (ParseAction36 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction37(string raw, CardData card, List<EffectTrigger> triggers)
    {
        string s = raw.Trim().TrimEnd('.');
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        if (PlayDpLock37(s, label) is { } dpLock) return dpLock;
        // [시큐리티] "Play this card without battling and without paying the cost" (BT5-112 · EX2-054 · BT24-015 · BT24-039) — 시큐리티 디지몬이 배틀하지 않고 등장
        if (card.Type == CardType.Digimon && triggers.Contains(EffectTrigger.Security) && Regex.IsMatch(s, @"^play this card without battling and without paying (?:the|its) (?:memory )?costs?$", I))
            return (c => { c.PlayWithoutBattle = true; Emit(c, label, "배틀하지 않고 이 카드를 코스트 없이 등장"); }, null);
        if (PlaceThisUnder37(s, label, triggers) is { } placeUnder) return placeUnder;   // EX7-070 · P-180 · BT9-029
        if (PlaceDigimonOnSecurity37(s, label, triggers.Contains(EffectTrigger.Security)) is { } toSecurity) return toSecurity;
        if (PlaceThisOnSecurity37(s, label, triggers) is { } thisToSecurity) return thisToSecurity;
        if (SecurityDigimonDp37(s, label, card, triggers) is { } secDp) return secDp;
        return null;
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// v0.37.0 절 정리 (Normalize 끝): "＜Security A. +1}＞" 원문 오타 (BT24-072 · BT24-075) ·
    /// 메모리 조건 뒤의 줄인 조건 "If they have 4 or more, ~" · "Then, if you have 1 or less, ~" → "~ memory" (같은 절에 앞서 메모리 조건이 있을 때 — BT15-016 · BT25-019 · LM-041 · EX8-029)
    /// </summary>
    internal static string Normalize37(string s)
    {
        s = Regex.Replace(s, @"(?<=＜Security A(?:ttack)?\.? [+-]\d+)\}(?=＞)", "");
        s = NormalizeHandPlace37(s);   // BT9-029 · BT10-025
        s = Regex.Replace(s, @"\b(?<w>their|your opponent's) Option effects (?=(?:don't|do not) affect\b)", "${w} Option card's effects ", I);   // BT25-019
        s = Regex.Replace(s, @"\b(?<w>Digimon or Tamers|Digimon|Tamers) can't be suspended\b", "${w} can't suspend", I);   // LM-041
        s = Regex.Replace(s, @"\b(?<p>has \d+ or more) colou?r\b(?!s)", "${p} colors", I);   // BT8-084 "4 or more color"
        if (Regex.IsMatch(s, @"\b\d+ or (?:more|less|fewer) memory\b", I))
            s = Regex.Replace(s, @"\b(?<p>[Ii]f|[Ww]hile) (?<who>you have|they have|your opponent has) (?<n>\d+) or (?<d>more|less|fewer)(?=,)", "${p} ${who} ${n} or ${d} memory");
        return s;
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// v0.37.0 "While ~, A. While ~, B." · "A. While ~, B." — 지속 효과 문장이 여럿 (EX8-029 · BT8-084). 문장마다 읽어 모두 읽히면 true (같은 시점 표기)
    /// </summary>
    private static bool ParseStaticMulti37(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer)
    {
        var sentences = SplitSentences(body);
        if (sentences.Count < 2 || sentences.Skip(1).Any(x => Regex.IsMatch(x.Trim(), @"^(?:They|It|Then|After)\b", I))) return false;   // 앞 문장을 받는 문장이 있으면 읽지 않는다
        var all = new Pending();
        foreach (var sentence in sentences)
        {
            var one = new Pending();
            if (ParseStatic(sentence.Trim(), inherited, card, qualifier, one, outer) != null) return false;
            all.Registrations.AddRange(one.Registrations);
        }
        pending.Registrations.AddRange(all.Registrations);
        return true;
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// v0.37.0 "Trash the top or bottom digivolution card of ~" — 위(true) · 아래(false) 중 고른다. 추천: 상대의 디지몬이면 진화원 효과 · DP가 더 큰 쪽, 자신의 디지몬이면 작은 쪽
    /// </summary>
    private static bool TopOrBottomSource37(EffectContext c, CardInstance d)
    {
        var top = d.EvolutionSources.Peek();
        var bottom = d.EvolutionSources.Last();
        if (ReferenceEquals(top, bottom)) return true;
        int Value(CardData x) => d.IsFaceDown(x) ? 0 : (x.InheritedEffectText.Trim().Length > 0 ? 3 : 0) + x.InheritedDpBonus / 1000 + x.InheritedSecurityAttackBonus * 2;
        bool enemy = !c.Owner.BattleArea.Contains(d);
        bool recommendTop = enemy ? Value(top) >= Value(bottom) : Value(top) < Value(bottom);
        return c.Engine.AskYesNo(c.Owner, d, $"[{d.TopCard.Name}]의 진화원 맨 위 [{(d.IsFaceDown(top) ? "뒷면 카드" : top.Name)}]를 파기할까요? (아니요 = 맨 아래 [{(d.IsFaceDown(bottom) ? "뒷면 카드" : bottom.Name)}])", recommendTop);
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// v0.37.0 "Then, place this card as the bottom digivolution card of 1 of your [Three Musketeers] trait Digimon" (옵션 【메인】 EX7-070 · P-180 · BT17-100 · EX7-066) ·
    /// [패] 【메인】 "If you have a Digimon with ~, by paying 1 memory, place this card as the bottom digivolution card of 1 of your Digimon with ~" (BT9-029 · BT10-025 — 공식 "1코스트 지불하는 것으로, 이 카드를 그 디지몬 1마리의 진화원 아래에 놓는다").
    /// 이 카드는 패 · 트래시(체크한 【시큐리티】) · 사용 중인 옵션에서 놓는다. 놓은 디지몬이 다음 문장의 "that Digimon"
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? PlaceThisUnder37(string s, string label, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s, @"^place this card(?: from your hand)? (?:as the bottom digivolution card of (?<t>.+)|as (?<t>.+?)'s bottom digivolution card|under (?<t>.+?) as its bottom digivolution card)$", I);
        if (!m.Success || !triggers.Any(t => t is EffectTrigger.Main or EffectTrigger.Security or EffectTrigger.HandMain)) return null;
        string t0 = m.Groups["t"].Value;
        var filter = OwnDigimonFilter(t0) ?? OwnDigimonFilter(Regex.Replace(t0, @"^(?:(?:1|one|any) of )?your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon$", "1 of your Digimon with the ${x} trait", I));
        if (filter == null) return (null, $"미지원 대상 (이 카드를 진화원에): {t0}");
        return (c =>
        {
            var hosts = c.Owner.BattleArea.Where(filter).OrderByDescending(d => d.GetCurrentDP()).ToList();
            // 사용 중인 옵션은 어느 곳에도 없다 — 같은 카드가 패 · 트래시에 더 있어도 (같은 카드 여러 장은 같은 객체) 그쪽을 빼지 않는다
            bool resolving = c.Card.Type == CardType.Option && c.Trigger == EffectTrigger.Main;
            bool inHand = !resolving && c.Trigger == EffectTrigger.HandMain && c.Owner.Hand.Contains(c.Card), inTrash = !resolving && !inHand && c.Owner.Trash.Contains(c.Card);
            if (hosts.Count == 0 || !(inHand || inTrash || resolving)) { c.LastStepResolved = false; Emit(c, label, "이 카드를 놓을 디지몬이 없습니다"); return; }
            var host = c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{c.Card.Name}] 이 카드를 진화원 맨 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
            if (host == null) { c.LastStepResolved = false; return; }
            if (inHand) c.Owner.Hand.Remove(c.Card);
            else if (inTrash) c.Owner.Trash.Remove(c.Card);
            host.AddBottomSource(c.Card);
            c.ThisCardMoved = true;
            c.LastTargets = new List<CardInstance> { host };
            c.RecalculateFieldBuffs();
            Emit(c, label, $"이 카드를 [{host.TopCard.Name}]의 진화원 맨 아래에 놓는다");
        }, null);
    }

    /// <summary>[패] 【메인】 "If you have a Digimon with ~, you may pay 1 memory to place this card under that Digimon as its bottom digivolution card" → "~, by paying 1 memory, place this card as the bottom digivolution card of 1 of your Digimon with ~"</summary>
    internal static string NormalizeHandPlace37(string s) =>
        Regex.Replace(s, @"(?<pre>If you have (?:a|an) Digimon (?:in play )?with (?<x>(?:\[[^\]]+\](?:,? (?:or )?)?)+ in its name|the \[[^\]]+\] trait)(?: in play)?, )(?:you may pay (?<n>\d+) memory to place|by paying (?<n>\d+) memory, place) this card under that Digimon as its bottom digivolution card",
            "${pre}by paying ${n} memory, place this card from your hand as the bottom digivolution card of 1 of your Digimon with ${x}", I);
}

internal static partial class SentenceParser
{
    // =====================================================================
    // 5. 시큐리티
    // =====================================================================

    /// <summary>
    /// "Place 1 of your opponent's Digimon face down at the bottom of their security stack" (RB1-019 · BT14-094 · BT14-102) · "~ as their top security card" (BT27-099 · EX12-076) ·
    /// "~ at the top or bottom of ~ security stack" · "~ as the top or bottom security card" (ST10-14 · EX11-016 — 고른다, 추천: 아래) · "~ face down on top of their security stack" (BT10-101)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? PlaceDigimonOnSecurity37(string s, string label, bool security)
    {
        var m = Regex.Match(s, @"^Place (?<t>(?:\d+|all) of (?:your|your opponent's|their) .+?)(?: face down)? (?:(?:on|at the) (?<pos>top or bottom|top|bottom) of (?:your|your opponent's|its owner's|their owners'?|their) security stacks?|as (?:the|their|your|its owner's) (?<pos>top or bottom|top|bottom) security cards?)(?: face down)?$", I);
        if (!m.Success) return null;
        var target = TargetSpec.Parse(m.Groups["t"].Value);
        if (target == null) return (null, $"미지원 대상 (시큐리티로): {m.Groups["t"].Value}");
        string pos = m.Groups["pos"].Value.ToLowerInvariant();
        return (c =>
        {
            var list = Pick(target, c, security);
            foreach (var d in list)
            {
                var zone = c.Engine.OwnerOf(d);
                bool top = pos == "top" || (pos == "top or bottom"
                    && c.Engine.AskYesNo(c.Owner, d, $"[{d.TopCard.Name}]을(를) 시큐리티 맨 위에 놓을까요? (아니요 = 맨 아래)", recommended: false));
                c.Engine.PlaceOnSecurity(zone, d, top);
            }
            Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 시큐리티로" : "대상이 없습니다", attackerRemoved: list.Contains(c.Other!));
        }, null);
    }

    /// <summary>"Place this card at the bottom of your security stack" (【소멸 시】 BT14-038 · BT14-102 · 옵션 【메인】 BT16-100 — 뒷면)</summary>
    private static (Action<EffectContext>? Act, string? Error)? PlaceThisOnSecurity37(string s, string label, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s, @"^Place this card (?:(?:on|at the) (?<pos>top|bottom) of your security stack|as (?:the|your) (?<pos>top|bottom) security card)(?: face down)?$", I);
        if (!m.Success || !triggers.Any(t => t is EffectTrigger.OnDeletion or EffectTrigger.Main)) return null;
        bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
        return (c =>
        {
            bool resolving = c.Card.Type == CardType.Option && c.Trigger == EffectTrigger.Main;   // 사용 중인 옵션 (같은 카드가 트래시에 더 있어도 빼지 않는다)
            bool inTrash = !resolving && c.Owner.Trash.Contains(c.Card);
            if (!inTrash && !resolving) { c.LastStepResolved = false; return; }
            if (inTrash) c.Owner.Trash.Remove(c.Card);
            if (top) c.Owner.SecurityStack.Insert(0, c.Card); else c.Owner.SecurityStack.Add(c.Card);
            c.ThisCardMoved = true;
            c.Engine.NotifySecurityAdded(c.Owner, c.Card);
            Emit(c, label, $"이 카드를 시큐리티 맨 {(top ? "위" : "아래")}에 놓는다");
        }, null);
    }

    /// <summary>
    /// "1 of your opponent's Digimon and all of your opponent's Security Digimon get -3000 DP for the turn" (BT7-098 · EX1-030 · EX4-009 · P-053) ·
    /// "~ and all of their Security Digimon get -4000 DP until the end of their turn" (ST17-06) — 디지몬 쪽은 보통 DP 변화, 시큐리티 디지몬 쪽은 기간 동안 상대의 시큐리티 디지몬 전부
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? SecurityDigimonDp37(string s, string label, CardData card, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s, @"^(?:(?<a>.+?) and )?all of (?:your opponent's|their) Security Digimon get (?<dp>[+-]\d+) DP (?<dur>for the turn|until the end of (?:their|your opponent's) turn|until (?:their|your opponent's) turn ends)$", I);
        if (!m.Success) return null;
        int dp = int.Parse(m.Groups["dp"].Value);
        var dur = ParseDuration(m.Groups["dur"].Value, out string left);
        if (dur is Duration.None or Duration.ThisAttack || left.Trim().Length > 0) return null;
        Step? first = null;
        if (m.Groups["a"].Success)
        {
            string a = m.Groups["a"].Value;
            bool plural = Regex.IsMatch(a, @"^all of\b", I);
            var r = ParseStep($"{a} {(plural ? "get" : "gets")} {m.Groups["dp"].Value} DP {m.Groups["dur"].Value}.", card, triggers);
            if (r.Step is not { Condition: null, Optional: false } st) return null;
            first = st;
        }
        return (c =>
        {
            if (first != null) RunStep(first, c);
            c.Engine.SecurityDpBuffsOf(c.Enemy).Add((dp, Until(c, dur)));
            Emit(c, label, $"상대의 시큐리티 디지몬 전부 DP {dp:+#;-#}");
        }, null);
    }
}

internal static partial class SentenceParser
{
    /// <summary>
    /// v0.37.0 등장 문장 뒤의 제한 (MergeRevealSentences에서 앞 문장에 붙인다):
    /// "This effect can't play [Plutomon]." (BT26-059 — 공식 "이 효과로는 「플루토몬」은 등장시킬 수 없다") → 앞 등장 문장의 고르는 카드에 "other than [Plutomon]" ·
    /// "This effect can't play cards of the same level." (P-220) → "⟦diff-level⟧" (같은 Lv.의 카드를 2장 이상 등장시키지 않는다)
    /// </summary>
    private static List<string> MergePlayLimits37(List<string> sentences)
    {
        for (int k = sentences.Count - 1; k > 0; k--)
        {
            var not = Regex.Match(sentences[k], @"^This effect can't play (?<x>\[[^\]]+\](?:(?:,| or) \[[^\]]+\])*)\.?$", I);
            if (not.Success && Regex.IsMatch(sentences[k - 1], @"\bplay (?:up to )?\d+ [^.]*?\bcards?(?= from )", I))
            {
                sentences = sentences.ToList();
                sentences[k - 1] = Regex.Replace(sentences[k - 1], @"(?<=\bplay (?:up to )?\d+ [^.]*?\bcards?)(?= from )", $" other than {not.Groups["x"].Value}", I);
                sentences.RemoveAt(k);
                continue;
            }
            if (Regex.IsMatch(sentences[k], @"^This effect can't play (?:cards|Digimon) (?:of|with) the same level\.?$", I)
                && Regex.IsMatch(sentences[k - 1], @"\bplay (?:up to )?[2-9] .*? without paying (?:the|its) (?:memory |play )?costs?\.?$", I))
            {
                sentences = sentences.ToList();
                sentences[k - 1] = sentences[k - 1].TrimEnd('.') + " ⟦diff-level⟧.";
                sentences.RemoveAt(k);
            }
        }
        return sentences;
    }
}

internal static partial class SentenceParser
{
    /// <summary>v0.37.0 "This effect can't play cards of the same level" — 1장씩 고르며 이미 고른 Lv.은 후보에서 뺀다</summary>
    private static List<PoolCard> ChooseDiffLevels37(EffectContext c, CardChoiceZone zone, List<PoolCard> ranked, int count)
    {
        var chosen = new List<PoolCard>();
        var levels = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            var rest = ranked.Where(x => !chosen.Contains(x) && !levels.Contains(x.Card.Level)).ToList();
            if (rest.Count == 0) break;
            var one = ChooseFromPool(c, zone, rest, 1, $"[{c.Card.Name}] 코스트 없이 등장시킬 카드를 고르세요 — 같은 Lv.은 1장만 ({i + 1}/{count}).").FirstOrDefault();
            if (one.Card == null) break;
            chosen.Add(one);
            levels.Add(one.Card.Level);
        }
        return chosen;
    }
}
