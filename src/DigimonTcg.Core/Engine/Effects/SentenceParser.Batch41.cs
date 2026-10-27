using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.41.0 미지원 문장 15차 묶음: 링크 조건이 빠진 어플몬의 【링크 시】 문장 · 같은 뜻 다른 표기 ("Until their turn ends" · "To 1 of ~, give ~ for each ~" · "Draw 1") ·
/// "The next time ~ would digivolve this turn" 진화 코스트 감소 · 진화원 · 시큐리티 표기 등.
/// 원래 규칙(1~14차)이 읽지 못한 절 · 문장 · 동작만 15차 규칙으로 다시 읽는다 (엔진: GoldfishEngine.Batch41.cs)
/// </summary>
internal static partial class SentenceParser
{
    [ThreadStatic] private static int _clauseDepth41;
    [ThreadStatic] private static int _fallbackDepth41;

    /// <summary>켜져 있으면 진화원 칸의 라벨 없는 문장을 【링크 시】 효과로 읽는다 (SentenceParser.cs — 링크 카드 처리)</summary>
    [ThreadStatic] private static bool _forceLinked41;

    // =====================================================================
    // 1. 절 — 원래 규칙이 읽지 못한 절만 (TryParse)
    // =====================================================================

    private static string? TryParse41(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse40(rawClause, inherited, card, def);
        if (err == null || _clauseDepth41 > 0) return err;
        _clauseDepth41++;
        try
        {
            // 영어 데이터에 링크 조건이 빠진 어플몬 카드: 진화원 칸의 라벨 없는 문장 = 【링크 시】 효과 (공식 BT21-043 "【링크 시】상대의 턴 종료까지 ~")
            if (inherited && IsUnlabeledAppLinkEffect41(rawClause, card))
            {
                _forceLinked41 = true;
                try
                {
                    foreach (var alt in new[] { rawClause }.Concat(ClauseRewrites41(rawClause, card)))
                        if (TryParse40(alt, inherited, card, null) == null)
                        {
                            if (def != null) TryParse40(alt, inherited, card, def);
                            return null;
                        }
                }
                finally { _forceLinked41 = false; }
            }
            if (LinkCostClause41(rawClause, inherited, card, def)) return null;
            if (SuccessionClause41(rawClause, inherited, card, def)) return null;
            foreach (var alt in ClauseRewrites41(rawClause, card))
            {
                if (alt == rawClause) continue;
                if (TryParse40(alt, inherited, card, null) != null) continue;
                if (def != null) TryParse40(alt, inherited, card, def);
                return null;
            }
            return err;
        }
        finally { _clauseDepth41--; }
    }

    /// <summary>
    /// 어플몬(특징 "~ (App Name)")의 진화원 칸 문장 중 라벨이 없는 것 — 영어 데이터에 "Link Requirements"가 빠진 카드 (BT21-043 · BT22-009 · BT26-019 등).
    /// 공식 카드에는 〈링크〉 조건과 【링크 시】 라벨이 있다
    /// </summary>
    internal static bool IsUnlabeledAppLinkEffect41(string rawClause, CardData card) =>
        card.Type == CardType.Digimon && card.Level >= 3
        && card.Traits.Any(t => t.Contains("(App Name)", StringComparison.OrdinalIgnoreCase))
        && !card.InheritedEffectText.Contains("Link Requirements", StringComparison.OrdinalIgnoreCase)
        && Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", I) is var c
        && c.Length > 0 && !c.StartsWith("[") && !c.StartsWith("＜") && !c.StartsWith("(") && !c.StartsWith("<");

    /// <summary>절 전체의 같은 뜻 다른 표기 (렌더러도 쓴다) — 원래 규칙으로 읽혀야 쓴다</summary>
    internal static IEnumerable<string> ClauseRewrites41(string rawClause, CardData card)
    {
        string c = rawClause.Trim();
        // 문장마다 다른 표기 (Rewrites41)를 절 전체에 — 라벨 뒤 본문
        var lead = Regex.Match(c, @"^(?<labels>(?:Inherited Effect\s*|Security Effect\s*)?(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
        string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value.Trim();
        // "Then, add your top security card to the hand and place this card as the top security card" (BT27-099 — 두 동작) → 두 문장
        const string addPlace = @"\badd your top security card to the hand and place this card as the top security card\.?$";
        if (Regex.IsMatch(body, addPlace, I))
            yield return labels + Regex.Replace(body, addPlace, "add your top security card to the hand. Then, place this card as the top security card.", I);
        var sentences = Regex.Split(body, @"(?<=\.)\s+(?=[A-Z＜])");
        if (sentences.Length >= 1)
        {
            bool changed = false;
            var outs = new List<string>();
            foreach (var s in sentences)
            {
                string t = s.Trim();
                string end = t.EndsWith(".") ? "." : "";
                string core = t.TrimEnd('.');
                string? alt = Rewrites41(core).FirstOrDefault(a => a != core);
                if (alt != null) { changed = true; outs.Add(alt + end); }
                else outs.Add(t);
            }
            if (changed) yield return labels + string.Join(" ", outs);
        }
    }

    // =====================================================================
    // 2. 문장 — 원래 규칙이 읽지 못한 문장만 (ParseStep 끝)
    // =====================================================================

    private static (Step? Step, string? Error)? StepFallback41(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth41 >= 3) return null;
        _fallbackDepth41++;
        try
        {
            string s = Regex.Replace(sentence.Trim().TrimEnd('.').Trim(), @"^Then,?\s+", "", I);
            foreach (var alt in Rewrites41(s))
            {
                if (alt == s) continue;
                var r = ParseStep(alt, card, triggers);
                if (r.Error == null && r.Step != null) return (r.Step with { Text = sentence.Trim() }, null);
            }
            return null;
        }
        finally { _fallbackDepth41--; }
    }

    /// <summary>같은 뜻의 다른 표기 (렌더러도 쓴다). 여러 규칙을 차례로 겹쳐 적용한 결과도 낸다</summary>
    internal static IEnumerable<string> Rewrites41(string s)
    {
        string acc = s;
        foreach (var f in RewriteRules41)
        {
            string next = f(acc);
            if (next != acc)
            {
                yield return next;
                acc = next;
            }
        }
    }

    private static readonly Func<string, string>[] RewriteRules41 =
    {
        // "[System|Seven Code]" (영어 데이터의 묶은 표기 — BT26-084) → "[System] or [Seven Code]"
        t => PipeList41(t),
        // "Draw 1" (괄호 없는 키워드 — BT23-016 · P-190 · BT25-036 · BT21-071) → "＜Draw 1＞"
        t => Regex.Replace(t, @"(?<![＜\w])Draw (?<n>\d+)(?![＞\w])", "＜Draw ${n}＞"),
        // "Until their turn ends" · "until their turn ends" (상대의 턴) — 앞에 상대가 나온 문장만 (공식 "상대의 턴 종료까지")
        t => Regex.Replace(t, @"^Until their turn ends,", "Until your opponent's turn ends,", I),
        t => Regex.Replace(t, @"^Until the end of opponent's turn,", "Until the end of your opponent's turn,", I),
        // "Until your opponent's turn ends, 1 of their Digimon ~" (BT21-053 · BT23-028 · BT26-028) — "their" = 상대
        t => Regex.IsMatch(t, @"\byour opponent'?s?\b|\bopponent's\b", I)
            ? Regex.Replace(t, @"(?<=^Until (?:the end of )?your opponent's turn(?: ends)?, )(?<n>\d+|all) of their (?=Digimon|Tamers)", "${n} of your opponent's ", I) : t,
        // "To 1 of your opponent's Digimon, give -4000 DP for the turn for each ~" (BT22-035 · EX12-030 · EX13-065 · BT26-081) → "1 of your opponent's Digimon gets -4000 DP for the turn for each ~"
        t => Regex.Replace(t, @"^To (?<t>\d+ of (?:your opponent's|their) Digimon), give (?<dp>[+-]\d+ DP)(?<rest>.*)$", "${t} gets ${dp}${rest}", I),
        // "To 1 of your opponent's Digimon, ＜De-Digivolve 1＞ (~) for each of your Digimon" (BT24-041 · BT24-065 · BT21-061 · BT21-060 · EX9-043) → "For each ~, ＜De-Digivolve 1＞ 1 of your opponent's Digimon"
        t => Regex.Replace(t, @"^To (?<t>\d+ of (?:your opponent's|their) Digimon), (?<k>＜De-Digivolve \d+＞)(?:\s*\([^)]*\))? (?<each>for (?:each|every) .+)$",
            m => $"{char.ToUpperInvariant(m.Groups["each"].Value[0])}{m.Groups["each"].Value[1..]}, {m.Groups["k"].Value} {m.Groups["t"].Value}", I),
        // "~ can't have its DP reduced by your opponent's effects, and isn't affected by ＜De-Digivolve＞ effects" (BT11-069 · BT16-055) — 공식 BT11-069 "상대의 효과로 DP가 마이너스되지 않고, 《퇴화》 효과를 받지 않는다"
        t => Regex.Replace(t, @"\bisn't affected by ＜De-Digivolve＞ effects\b", "isn't affected by your opponent's ＜De-Digivolve＞ effects", I),
        // "their ＜De-Digivolve＞ effects don't affect 1 of your Digimon" (EX10-031) → "1 of your Digimon isn't affected by your opponent's ＜De-Digivolve＞ effects"
        t => Regex.Replace(t, @"\b(?:their|your opponent's) ＜De-Digivolve＞ effects don't affect (?<t>\d+ of your [^,]+?Digimon)(?=,| until|$)", "${t} isn't affected by your opponent's ＜De-Digivolve＞ effects", I),
        // "From 1 of your opponent's Digimon, trash any 1 digivolution card" (EX9-018 · ST21-04) · "trash any 1 digivolution card from your opponent's Digimon" (BT25-103)
        t => Regex.Replace(t, @"^From (?<t>1 of your opponent's Digimon), trash (?<n>any \d+|\d+) digivolution cards?$", "Trash ${n} digivolution card of ${t}", I),
        t => Regex.Replace(t, @"^(?<may>You may )?trash any (?<n>\d+) digivolution cards? from (?:1 of )?your opponent's Digimon$", m => $"{m.Groups["may"].Value}{(m.Groups["may"].Success ? "trash" : "Trash")} any {m.Groups["n"].Value} digivolution card of 1 of your opponent's Digimon", I),
        // "Trash the top 10 stacked cards of 1 of your opponent's Digimon" (BT21-030) — 공식 "겹쳐진 카드를 위에서부터 10장" — 겹쳐진 카드 = 진화원 + 맨 위 (맨 위만 남지 않으면 그 디지몬은 트래시)
        // (엔진이 맨 위까지 파기하는 처리를 갖지 않아 다루지 않는다)
        // "From 1 of your opponent's Digimon, trash any 1 digivolution card for each ~" (EX9-018 · ST21-04) → "For each ~, trash any 1 digivolution card of 1 of your opponent's Digimon"
        t => Regex.Replace(t, @"^From (?<t>1 of your opponent's Digimon), trash (?<n>any \d+|\d+) digivolution cards? (?<each>for (?:each|every) .+)$",
            m => $"{char.ToUpperInvariant(m.Groups["each"].Value[0])}{m.Groups["each"].Value[1..]}, trash {m.Groups["n"].Value} digivolution card of {m.Groups["t"].Value}", I),
        // "~, you may trash any 1 digivolution card from your opponent's Digimon" (BT25-103 — 문장 가운데)
        t => Regex.Replace(t, @"\btrash any (?<n>\d+) digivolution cards? from (?:1 of )?your opponent's Digimon$", "trash any ${n} digivolution card of 1 of your opponent's Digimon", I),
        // "gets ＜Piercing＞ and ＜Blocker＞" · "gets ＜Security A. -2＞" (EX7-064 · BT8-043) → "gains ~"
        t => Regex.Replace(t, @"\b(?<v>get)(?<s>s?) (?=＜)", "gain${s} ", I),
        // "-5000DP" (EX4-074 원문) → "-5000 DP"
        t => Regex.Replace(t, @"(?<n>[+-]\d+)DP\b", "${n} DP"),
        // "to their owner's hands" · "to their owner's hand" (BT14-027 · P-073 · BT9-031 — 복수) → "to their owners' hands"
        t => Regex.Replace(t, @"\bto their owner's hands?\b", "to their owners' hands", I),
        // "~ that has an [On Deletion] effect" (EX1-021) → "~ with an [On Deletion] effect" · "its owners deck" (원문) → "the deck"
        t => Regex.Replace(Regex.Replace(t, @"\bDigimon that has an? (?=\[)", "Digimon with an ", I), @"\bto the bottom of its owners deck\b", "to the bottom of the deck", I),
        // "without paying for its memory cost" (BT9-099) → "without paying its memory cost"
        t => Regex.Replace(t, @"\bwithout paying for its (?=(?:memory |play )?cost\b)", "without paying its ", I),
        // "Gain 1 memory if you have a Digimon with [Greymon] in its name" (EX9-066) → "If you have ~, gain 1 memory"
        t => Regex.Replace(t, @"^Gain (?<n>\d+) memory if (?<c>you have .+)$", "If ${c}, gain ${n} memory", I),
        // "None of your opponent's Digimon can unsuspend in their next unsuspend phase" (BT23-047) → "Your opponent's Digimon can't unsuspend during their next unsuspend phase"
        t => Regex.Replace(t, @"^None of your opponent's Digimon can unsuspend (?:in|during) their next unsuspend phase$", "Your opponent's Digimon can't unsuspend during their next unsuspend phase", I),
        // "For each card trashed (by this effect), suspend 1 of your opponent's Digimon" (BT11-057 · BT9-107) · "For each one, ~" (앞 문장에서 파기한 매수 — EX6-060) → "~ for each card trashed by this effect"
        t => Regex.Replace(t, @"^(?<pre>If you do, )?For each (?:card trashed(?: by this (?:card's )?effect)?|one(?: trashed)?), (?<a>(?:suspend|delete|＜De-Digivolve \d+＞) .+?)(?:\s*\([^)]*\))?$", "${pre}${a} for each card trashed by this effect", I),
        // "[On Deletion] When this Digimon had 2 or more colors, gain 2 memory" (BT9-074 · BT9-076 원문 "colurs") — 공식 "이 디지몬이 2색 이상이었다면" (조건)
        t => Regex.Replace(t, @"^When this Digimon had (?<n>\d+) or more colo?u?rs,", "If this Digimon had ${n} or more colors,", I),
        // "give 3 of your opponent's Digimon ＜Security A. -1＞ (~) until their turn ends" (BT23-025) → "3 of your opponent's Digimon gain ＜Security A. -1＞ until their turn ends"
        t => Regex.Replace(t, @"\bgive (?<t>\d+ of your opponent's Digimon) (?<k>＜[^＞]+＞)(?:\s*\([^)]*\))? (?<d>until (?:their|your opponent's) turn ends|until the end of your opponent's turn|for the turn)$", "${t} gain ${k} ${d}", I),
        // "At turn end, delete this Digimon" · "At the end of the turn, delete this Digimon" (BT23-025) → "Delete this Digimon at the end of the turn" (v0.40.0)
        t => Regex.Replace(t, @"^At (?:turn end|the end of (?:the|this) turn), delete this Digimon$", "Delete this Digimon at the end of the turn", I),
        // 시큐리티 위치의 다른 표기 — "Place this Digimon at the bottom of your security stack (face down)" (EX4-060) · "as your top security card" (EX9-021)
        t => Regex.Replace(t, @"^Place this Digimon (?:at the bottom of your security stack(?: face down)?|as your bottom security card)$", "Place this Digimon as the bottom security card", I),
        t => Regex.Replace(t, @"^Place this Digimon as your top security card$", "Place this Digimon as the top security card", I),
        // 원문 오타 "add it to you hand" (BT9-034)
        t => Regex.Replace(t, @"\bto you hand\b", "to your hand", I),
        // 진화원 위치의 다른 표기 — "at the bottom of one of your Digimon's digivolution cards" (BT7-027 — 공식 "자신의 디지몬 1마리의 진화원 가장 아래에")
        t => Regex.Replace(t, @"\bat the bottom of (?:one|1) of your (?<x>(?:[^,]*? )?Digimon)'s digivolution cards\b", "under 1 of your ${x} as its bottom digivolution card", I),
        // "at the bottom of this Digimon's digivolution cards (in any order)" (BT7-039 · BT7-083) · "at the top of ~" (BT6-086 — 공식 "이 디지몬의 진화원 위에 원하는 순서대로")
        t => Regex.Replace(t, @"\bat the (?<pos>bottom|top) of this Digimon's digivolution cards(?: in any order)?", "as this Digimon's ${pos} digivolution cards", I),
        // "~ from your trash as digivolution cards of this Digimon" (BT12-111 — 공식 "이 디지몬의 진화원에 놓을 수 있다" — 맨 아래)
        t => Regex.Replace(t, @"\bas digivolution cards of this Digimon\b", "as this Digimon's bottom digivolution cards", I),
        // "trash this Digimon's top stacked card" (EX10-022 — 공식 "이 디지몬의 맨 위 카드를 파기") → "trash the top card of this Digimon"
        t => Regex.Replace(t, @"\btrash this Digimon's top stacked card\b", "trash the top card of this Digimon", I),
    };

    // =====================================================================
    // 3. 동작 — 원래 규칙이 읽지 못한 동작만 (ParseAction 끝)
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ParseAction41(string raw, CardData card, List<EffectTrigger> triggers)
    {
        if (_fallbackDepth41 >= 3) return null;
        _fallbackDepth41++;
        try
        {
            string s = raw.Trim().TrimEnd('.');
            if (NextEvoReduction41(s, card, triggers) is { } ner) return (ner, null);
            if (SwitchOwnAttack41(s, triggers) is { } swa) return (swa, null);
            if (OpponentUnderOpponent41(s, triggers) is { } ouo) return (ouo, null);
            if (TrashTopCard41(s, triggers) is { } ttc) return (ttc, null);
            if (ActivateEffects41(s, triggers) is { } act) return (act, null);
            foreach (var alt in Rewrites41(s))
            {
                if (alt == s) continue;
                var r = ParseAction(alt, card, triggers);
                if (r.Error == null && r.Act != null) return r;
            }
            return null;
        }
        finally { _fallbackDepth41--; }
    }

    // ---- "The next time one of your Digimon would digivolve this turn, reduce ~" ----

    /// <summary>
    /// "다음에 ~ 진화할 때" 문장 → (지속 효과로 읽을 문장, 한 번만인지). 아니면 null (렌더러도 쓴다)
    /// BT1-109 · BT5-109 · EX1-033 · ST12-15 · EX5-029 · BT3-103 (한 번만) · BT14-013 (이 디지몬, 턴 종료까지 계속)
    /// </summary>
    internal static (string Static, bool OneShot)? NextEvoStatic41(string s)
    {
        s = s.Trim().TrimEnd('.');
        Match m;
        string Reduce(string r) => Regex.Replace(r, @"^(?:reduce|decrease) the (?:memory |digivolution )?cost(?: of the digivolution)? by (?<n>\d+)$", "reduce the digivolution cost by ${n}", I);
        string Who(string color, string from, string to, string into)
        {
            string lv = from.Length > 0 ? $"level {from} " : "";
            string col = color.Length > 0 ? color + " " : "";
            string w = $"one of your {col}{lv}Digimon would digivolve";
            if (to.Length > 0) w += $" into a level {to} Digimon card";
            else if (into.Length > 0) w += " into " + into;
            return w;
        }
        // "The next time one of your Digimon would digivolve (from level 6 to level 7 | into ~) this turn, reduce ~"
        if ((m = Regex.Match(s, @"^(?:For the turn, )?the next time (?:one of your (?<col>\w+ )?Digimon would digivolve|you would digivolve one of your (?<col>\w+ )?Digimon)(?: from level (?<a>\d+) to level (?<b>\d+)| into (?<into>.+?))?(?: this turn)?, (?<cost>by [^,]+, )?(?<r>(?:reduce|decrease) the (?:memory |digivolution )?cost(?: of the digivolution)? by \d+)$", I)).Success)
            return ($"When {Who(m.Groups["col"].Value.Trim(), m.Groups["a"].Value, m.Groups["b"].Value, m.Groups["into"].Value)}, {m.Groups["cost"].Value}{Reduce(m.Groups["r"].Value)}.", true);
        // "For the turn, when one of your (green) Digimon would next digivolve, (by ~,) reduce ~" (EX5-029 · BT3-103)
        if ((m = Regex.Match(s, @"^For the turn, when one of your (?<col>\w+ )?Digimon would next digivolve, (?<cost>by [^,]+, )?(?<r>(?:reduce|decrease) the (?:memory |digivolution )?cost(?: of the digivolution)? by \d+)$", I)).Success)
            return ($"When {Who(m.Groups["col"].Value.Trim(), "", "", "")}, {m.Groups["cost"].Value}{Reduce(m.Groups["r"].Value)}.", true);
        // "For the turn, when this Digimon would digivolve into ~, reduce the digivolution cost by 1" (BT14-013 — 턴 종료까지 계속)
        if ((m = Regex.Match(s, @"^For the turn, (?<w>when this Digimon would digivolve into .+?), (?<r>(?:reduce|decrease) the (?:memory |digivolution )?cost(?: of the digivolution)? by \d+)$", I)).Success)
            return ($"W{m.Groups["w"].Value[1..]}, {Reduce(m.Groups["r"].Value)}.", false);
        return null;
    }

    private static Action<EffectContext>? NextEvoReduction41(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (NextEvoStatic41(s) is not { } ns) return null;
        // 지속 효과(진화 코스트 감소) 문장을 임시 카드로 읽어 그 감소를 얻는다
        var tmpCard = new CardData { CardId = card.CardId + "#next41", Name = card.Name, Type = CardType.Digimon, Level = card.Level, Color = card.Color, Traits = card.Traits };
        var builder = new CardScriptBuilder();
        var def = builder.Card(tmpCard.CardId);
        if (TryParse40(ns.Static, false, tmpCard, null) != null) return null;
        TryParse40(ns.Static, false, tmpCard, def);
        var mods = def.Script41.EvoCostModifiers.ToList();
        if (mods.Count == 0) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        bool oneShot = ns.OneShot;
        return c =>
        {
            foreach (var mod in mods)
                c.Engine.AddTurnEvoCostModifier41(c.Owner, mod, c.Card, mod.Subject == EvoCostSubject.ThisDigimon ? c.Self : null, oneShot);
            Emit(c, label, oneShot ? "이 턴 동안 다음 진화의 진화 코스트를 줄입니다" : "이 턴 동안 진화 코스트를 줄입니다");
        };
    }

    // ---- 자신의 어택 대상 변경 ----

    /// <summary>"(you may) switch the target of (the) attack to 1 of your opponent's suspended Digimon" (BT9-052 · BT9-055 — 공식 "어택의 대상을 레스트 상태인 상대 디지몬 1마리로 변경할 수 있다") — 이 디지몬의 어택</summary>
    private static Action<EffectContext>? SwitchOwnAttack41(string s, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s, @"^(?:Switch|Change) the (?:target of (?:the |this )?attack|attack target) to (?<t>\d+ of your opponent's .+)$", I);
        if (!m.Success) return null;
        var target = TargetSpec.Parse(m.Groups["t"].Value);
        if (target == null || !target.Opponent || target.IsThis || target.IsThat) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return c =>
        {
            var attacker = c.Engine.CurrentAttacker;
            if (attacker == null || !c.Owner.BattleArea.Contains(attacker)) { c.LastStepResolved = false; return; }
            var d = Pick(target, c, false).FirstOrDefault();
            if (d != null && c.Engine.RedirectAttack(c.Enemy, d))
            {
                Diag41.Hit(ref Diag41.OwnAttackSwitch);
                Emit(c, label, $"어택 대상을 [{d.TopCard.Name}]으로 바꿨습니다");
            }
            else c.LastStepResolved = false;
        };
    }
}
