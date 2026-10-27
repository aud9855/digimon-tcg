using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.44.0 18차 묶음 — 절 · 문장 · 동작 · 비용 · 조건 · 셈 (원래 규칙이 읽지 못한 것만)</summary>
internal static partial class SentenceParser
{
    // =====================================================================
    // 절
    // =====================================================================

    /// <summary>
    /// 【소멸 시】: 소멸하기 전의 진화원을 가진 이 디지몬 (조건 판단용 복사본) — 엔진은 진화원을 트래시로 보낸 뒤 【소멸 시】를 처리하므로,
    /// "If [X] was in this Digimon's digivolution cards"(P-145) 같은 진화원 조건은 이것으로 다시 본다. 【소멸 시】가 아니거나 진화원이 없었으면 null
    /// </summary>
    private static CardInstance? DeletedSnapshot44(EffectContext c)
    {
        if (c.DeletedSources44 is not { Count: > 0 } srcs || c.Self == null) return null;
        var snap = new CardInstance(c.Self.TopCard, c.Self.TurnPlayed);
        for (int i = srcs.Count - 1; i >= 0; i--) snap.AddBottomSource(srcs[i]);
        return snap;
    }

    internal static readonly Regex WhileGainsOnDeletionRe44 = new(
        $@"^(?:\[All Turns\]\s*)?While (?<x>{TargetSpec.NameList}) (?:is|are) in this Digimon's digivolution cards, (?:it|this Digimon) gains ""\[On Deletion\]\s*(?<body>[^""]+?)\.?""\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool ExtraClause44(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string c0 = Regex.Replace(rawClause.Trim(), @"^(?:Inherited Effect|Security Effect)\s*", "", I);
        // 옵션 카드의 색 조건 줄 "Option Card Colour Requirements Purple" (BT26-080 · BT27-085) — 카드 정보 (효과 없음)
        if (Regex.IsMatch(c0, @"^Option Card Colou?r Requirements? (?:(?:Red|Blue|Yellow|Green|Black|Purple|White)[ /,]*)+\.?$", I)) return true;
        // 영어 데이터의 깨진 중복 문장 (디지타마 EX12-001): 진화원 칸 문장이 메인 칸 "Inherited Effect [시점] ~"의 본문과 똑같다 — 메인 칸 문장이 진화원 효과를 등록한다 (등록 없음)
        if (inherited && card.Type == CardType.Digitama && !Regex.IsMatch(c0, @"^\[", I)
            && Regex.Matches(card.EffectText ?? "", @"Inherited Effect\s*(?:\[[^\]]+\]\s*)+(?<body>[^\r\n]+)", I).Any(g => string.Equals(g.Groups["body"].Value.Trim(), c0, StringComparison.Ordinal)))
            return true;
        // "[All Turns] While [Numemon] or [X Antibody] is in this Digimon's digivolution cards, it gains "[On Deletion] ~"" (BT15-057 — 공식
        //  "【서로의 턴】이 디지몬의 진화원에 「누메몬」/「X항체」가 있는 동안, 이 디지몬은 「【소멸 시】~」를 얻는다") — 소멸할 때의 진화원으로 판단하는 이 카드의 【소멸 시】
        if (card.Type == CardType.Digimon && WhileGainsOnDeletionRe44.Match(c0) is { Success: true } wg)
        {
            string bodyQ = wg.Groups["body"].Value.Trim();
            string clause = $"{(inherited ? "Inherited Effect " : "")}[On Deletion] If {wg.Groups["x"].Value} was in this Digimon's digivolution cards when it was deleted, {char.ToLowerInvariant(bodyQ[0])}{bodyQ[1..]}";
            return TryParse43(clause, inherited, card, def) == null;
        }
        return false;
    }

    // =====================================================================
    // 문장
    // =====================================================================

    private static Step? StepExtra44(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        string s = sentence.Trim();
        Match m;
        if (s.Contains(IfNotMark44) && !Regex.IsMatch(s, @"^(?:Then,?\s+)?By ", I)) return null;   // 비용 뒤 문장만 따로 읽지 않는다
        // 앞의 조건 "If ~, " — 뒤 문장을 이 묶음 규칙으로 읽을 수 있으면 조건을 붙인다
        if ((m = Regex.Match(s, @"^(?<then>Then,?\s+)?If (?<c>[^,]+), (?<rest>.+)$", I)).Success && !s.Contains(IfNotMark44)
            && ParseEventCondition(m.Groups["c"].Value) is { } pre
            && StepExtra44(char.ToUpperInvariant(m.Groups["rest"].Value[0]) + m.Groups["rest"].Value[1..], card, triggers) is { } condInner)
        {
            var innerCond = condInner.Condition;
            return condInner with { Condition = c => pre(c) && (innerCond == null || innerCond(c)), Text = s };
        }
        if (TrashUnderForEach44(s, card, triggers) is { } underEach) return underEach;
        // "For every 2 cards trashed by this effect, delete 1 of your opponent's Tamers" (BT19-075 — 공식 "이 효과로 파기한 2장마다") — 앞 문장이 옮긴 매수 (PaidCount)
        if ((m = Regex.Match(s.TrimEnd('.'), @"^(?:Then, )?For (?:each|every) (?<n>\d+) cards? (?:trashed by this effect|this effect trashed), (?<core>.+)$", I)).Success)
        {
            int per = Math.Max(1, int.Parse(m.Groups["n"].Value));
            var inner = ParseStep(char.ToUpperInvariant(m.Groups["core"].Value[0]) + m.Groups["core"].Value[1..] + ".", card, triggers);
            if (inner.Error == null && inner.Step != null)
            {
                var st = inner.Step;
                return new Step(null, c =>
                {
                    int k = Math.Min(c.PaidCount / per, 20);
                    if (k > 0) Diag44.Hit("forEveryNTrashed");
                    for (int i = 0; i < k; i++) RunStep(st, c);
                }, false, s);
            }
        }
        // 시큐리티 디지몬 DP에 셈이 붙은 문장: "For each yellow and/or red Tamer you have in play, 1 of your opponent's Digimon and all of your opponent's Security Digimon get -3000 DP for the turn" (BT12-043) ·
        //  "all of your opponent's Digimon and Security Digimon get -1000 DP for the turn for each card in your security stack" (BT9-043 — 공식 "자신의 시큐리티 1장마다 ~ DP-1000")
        if (((m = Regex.Match(s.TrimEnd('.'), @"^(?:Then, )?For (?:each|every) (?<cnt>[^,]+), (?<core>.+)$", I)).Success
             || (m = Regex.Match(s.TrimEnd('.'), @"^(?:Then, )?(?<core>.+? (?:for the turn|until [^.]+?)) for (?:each|every) (?<cnt>.+)$", I)).Success)
            && SecDpRe44.Match(Regex.Replace(m.Groups["core"].Value, @"^(?:Then, )", "", I)) is { Success: true } sm
            && ParseCounter(m.Groups["cnt"].Value) is { } secCnt)
        {
            string core = Regex.Replace(m.Groups["core"].Value, @"^(?:Then, )", "", I);
            int unit = int.Parse(sm.Groups["dp"].Value);
            if (ActionExtra44(core, card, triggers, "") is not { Error: null, Act: not null }) return null;
            string Scaled(int k) => core[..sm.Groups["dp"].Index] + (unit * k).ToString("+#;-#") + core[(sm.Groups["dp"].Index + sm.Groups["dp"].Length)..];
            var byK = new Dictionary<int, Step?>();
            return new Step(null, c =>
            {
                int k = Math.Min(secCnt(c.Owner, c.Enemy, c.Self), 20);
                if (k <= 0) return;
                if (!byK.TryGetValue(k, out var st)) byK[k] = st = ParseStep(Scaled(k) + ".", card, triggers).Step;
                if (st != null) RunStep(st, c);
            }, false, s);
        }
        // "For each of your opponent's Digimon suspended by this effect, gain 1 memory" (EX8-044 — 공식 "이 효과로 레스트한 상대의 디지몬 1마리마다") — 앞 문장의 대상
        if ((m = Regex.Match(s.TrimEnd('.'), @"^(?:Then, )?For each of your opponent's Digimon suspended by this effect, (?<core>.+)$", I)).Success)
        {
            var inner = ParseStep(char.ToUpperInvariant(m.Groups["core"].Value[0]) + m.Groups["core"].Value[1..] + ".", card, triggers);
            if (inner.Error == null && inner.Step != null)
            {
                var st = inner.Step;
                return new Step(null, c =>
                {
                    int k = Math.Min(c.LastTargets.Count(d => c.Enemy.BattleArea.Contains(d) && d.IsSuspended), 20);
                    if (k > 0) Diag44.Hit("forEachSuspendedByThis");
                    for (int i = 0; i < k; i++) RunStep(st, c);
                }, false, s);
            }
        }
        // "For each Digimon deleted by this effect, gain 1 memory" (BT8-019 — 공식 "이 효과로 소멸한 디지몬 1마리마다 메모리+1") — 앞 문장의 대상 중 필드를 떠난 수
        if ((m = Regex.Match(s.TrimEnd('.'), @"^(?:Then, )?For (?:each|every) Digimon deleted by this effect, (?<core>.+)$", I)).Success)
        {
            var inner = ParseStep(char.ToUpperInvariant(m.Groups["core"].Value[0]) + m.Groups["core"].Value[1..] + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            return new Step(null, c =>
            {
                int k = Math.Min(c.LastTargets.Count(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d)), 20);
                if (k > 0) Diag44.Hit("forEachDeletedByThis");
                for (int i = 0; i < k; i++) RunStep(st, c);
            }, false, s);
        }
        // "By X, A ⟦ifnot44⟧ If this effect didn't ~, B" (BT16-076 · BT22-074 · BT25-014 · EX10-052 · BT18-034 …) —
        // 공식 "~하는 것으로, A. 이 효과로 ~하지 않았다면, B": 비용을 내지 않았으면 A도 B도 처리하지 않는다 (비용이 효과 전체의 조건)
        if ((m = Regex.Match(s, $@"^(?<a>(?:Then,?\s+)?By .+?) {Regex.Escape(IfNotMark44)} (?<b>.+)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)).Success)
        {
            var ra = ParseStep(m.Groups["a"].Value.Trim() + ".", card, triggers);
            var rb = ParseStep(m.Groups["b"].Value.Trim(), card, triggers);
            if (ra.Error != null || ra.Step == null || rb.Error != null || rb.Step == null) return null;
            var (sa, sb) = (ra.Step, rb.Step);
            return new Step(null, c =>
            {
                int before = c.CostsPaid44;
                RunStep(sa, c);
                if (c.CostsPaid44 <= before) return;
                Diag44.Hit("ifNotAfterCost");
                RunStep(sb, c);
            }, false, s.Replace(" " + IfNotMark44 + " ", ". "));
        }
        return MoreStep44(sentence, card, triggers);
    }

    // =====================================================================
    // 동작
    // =====================================================================

    private static (Action<EffectContext>? Act, string? Error)? ActionExtra44(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        // "＜Blitz＞" (문장 안 — 조건 · "Then," 뒤: BT10-070 · BT9-068 · EX3-063 — 공식 "《진격》(상대의 메모리가 1 이상이라면, 이 디지몬으로 어택할 수 있다)")
        if (Regex.IsMatch(s, @"^＜Blitz＞$", I) && card.Type == CardType.Digimon)
            return (c =>
            {
                if (c.Self != null && c.Engine.TryGrantBlitz(c.Owner, c.Self))
                {
                    Diag44.Hit("blitzStep");
                    c.Log($"[{label}] {c.Card.Name}: ≪진격≫ — 상대 쪽 메모리가 1 이상이어서 이 디지몬으로 어택할 수 있습니다");
                }
                else c.LastStepResolved = false;
            }, null);
        // 【소멸 시】 "Play this card (from your trash) without paying the cost" (BT2-083 · 준 효과 ST16-15 · EX4-059 · BT3-109 — 소멸한 디지몬의 맨 위 카드를 트래시에서)
        if (triggers.Contains(EffectTrigger.OnDeletion)
            && Regex.Match(s, @"^Play this card(?: from (?:your|the) trash)? without paying (?:the|its)(?: memory| play)? cost(?<no> ⟦no-onplay⟧)?$", I) is { Success: true } pt)
        {
            bool noOnPlay = pt.Groups["no"].Success;
            return (c =>
            {
                var x = c.Self?.TopCard ?? c.Card;
                if (GoldfishEngine.IsToken(x) || !c.Owner.Trash.Contains(x) || x.Type != CardType.Digimon) { c.LastStepResolved = false; Emit(c, label, "트래시에 이 카드가 없습니다"); return; }
                bool ok = c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Trash, () => c.Owner.Trash.Remove(x), triggerOnPlay: !noOnPlay);
                c.LastStepResolved = ok;
                if (ok) Diag44.Hit("onDeletionPlayThisCard");
                Emit(c, label, ok ? $"[{x.Name}]을(를) 트래시에서 코스트 없이 등장" : "등장시키지 못했습니다");
            }, null);
        }
        // "Until ~, this Digimon and 1 of your level 5 or lower Digimon gain "~"" (EX4-059 — 공식 "이 디지몬과 Lv.5 이하의 자신의 디지몬 1마리는 「~」의 효과를 얻는다") — 두 대상에 같은 효과
        if (Regex.Match(s, "^(?<pre>Until [^,\"]+, )?this Digimon and (?<t>1 of your [^\"]+?) gain \"(?<q>[^\"]+)\"(?<post> (?:until|for) [^\"]+)?$", I) is { Success: true } two)
        {
            string pre = two.Groups["pre"].Value, post = two.Groups["post"].Value, q = two.Groups["q"].Value;
            var a = ParseAction($"{pre}this Digimon gains \"{q}\"{post}", card, triggers);
            var b = ParseAction($"{pre}{two.Groups["t"].Value} gains \"{q}\"{post}", card, triggers);
            if (a.Error == null && a.Act != null && b.Error == null && b.Act != null)
            {
                var (actA, actB) = (a.Act, b.Act);
                return (c => { actA(c); actB(c); Diag44.Hit("grantTwoTargets"); }, null);
            }
        }
        if (SecurityDigimonDp44(s, card, triggers, label) is { } sec) return sec;
        if (TrashUnderAction44(s, label) is { } under) return under;
        if (TamerAction44(s, card, triggers, label) is { } tamer) return tamer;
        if (ChooseAndOpponentAction44(s, card, triggers, label) is { } choose) return choose;
        if (MoreAction44(s, card, triggers, label) is { } more) return more;
        if (CompoundAction44(s, card, triggers) is { } compound) return compound;
        return null;
    }

    internal static readonly Regex SecDpRe44 = new(
        @"^(?:(?<a>.+?) and )?(?:all of )?(?<who>your opponent's |their |your )?Security Digimon get (?<dp>[+-]\d+) DP (?<dur>for the turn|until .+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 시큐리티 디지몬의 DP (기간): "all of your opponent's Digimon and Security Digimon get -5000 DP until the end of your turn" (BT15-092 — 공식 "상대의 디지몬과 시큐리티 디지몬 전부를 DP -5000") ·
    /// "all of your Security Digimon get +3000 DP until the end of your opponent's next turn" (BT7-036) · "all of your opponent's Security Digimon get -2000 DP for the turn" (BT9-084)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? SecurityDigimonDp44(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        var m = SecDpRe44.Match(s);
        if (!m.Success) return null;
        string who = m.Groups["who"].Value.Trim();
        bool own = who.Equals("your", StringComparison.OrdinalIgnoreCase);
        // "all of your opponent's Digimon and Security Digimon" — 앞쪽이 주인을 정한다
        if (who.Length == 0)
        {
            if (!m.Groups["a"].Success || !Regex.IsMatch(m.Groups["a"].Value, @"\b(?:your opponent's|their)\b", I)) return null;
            own = false;
        }
        int dp = int.Parse(m.Groups["dp"].Value);
        var dur = ParseDuration(m.Groups["dur"].Value, out string left);
        if (dur is Duration.None or Duration.ThisAttack || left.Trim().Length > 0) return null;
        Step? first = null;
        if (m.Groups["a"].Success)
        {
            string a = m.Groups["a"].Value;
            bool plural = Regex.IsMatch(a, @"^all of\b", I);
            var r = ParseStep($"{char.ToUpperInvariant(a[0])}{a[1..]} {(plural ? "get" : "gets")} {m.Groups["dp"].Value} DP {m.Groups["dur"].Value}.", card, triggers);
            if (r.Error != null || r.Step is not { Condition: null, Optional: false } st) return null;
            first = st;
        }
        return (c =>
        {
            if (first != null) RunStep(first, c);
            c.Engine.SecurityDpBuffsOf(own ? c.Owner : c.Enemy).Add((dp, Until(c, dur)));
            c.RecalculateFieldBuffs();
            Diag44.Hit("securityDigimonDp");
            Emit(c, label, $"{(own ? "자신" : "상대")}의 시큐리티 디지몬 전부 DP {dp:+#;-#}");
        }, null);
    }

    [ThreadStatic] private static int _compoundDepth44;

    /// <summary>
    /// 두 동작을 잇는 문장 — 앞뒤를 각각 문장으로 읽을 수 있을 때만: "delete 1 Tamer, and unsuspend this Digimon" (BT10-069) ·
    /// "suspend this Tamer and ＜Draw 1＞" (BT24-102) · "Gain 1 memory, then hatch 1 Digi-Egg card ~" (EX1-066)
    /// </summary>
    private static (Action<EffectContext>? Act, string? Error)? CompoundAction44(string s, CardData card, List<EffectTrigger> triggers)
    {
        if (_compoundDepth44 > 1) return null;
        _compoundDepth44++;
        try
        {
            foreach (Match sep in Regex.Matches(s, @"(?:,? and then|, then|,? and|,) (?=(?:gain|draw|unsuspend|suspend|delete|return|trash|hatch|place|play|add|lose|reveal|have)\b|＜)", I))
            {
                string left = s[..sep.Index].Trim(), right = s[(sep.Index + sep.Length)..].Trim();
                if (left.Length == 0 || right.Length == 0) continue;
                // 대상 목록 안의 "and"는 나누지 않는다 ("Delete 1 of ~ and 1 of ~")
                if (Regex.IsMatch(right, @"^(?:\d+|all|up to \d+) of\b", I)) continue;
                var a = ParseStep(char.ToUpperInvariant(left[0]) + left[1..] + ".", card, triggers);
                if (a.Error != null || a.Step == null) continue;
                var b = ParseStep(char.ToUpperInvariant(right[0]) + right[1..] + ".", card, triggers);
                if (b.Error != null || b.Step == null) continue;
                var (sa, sb) = (a.Step, b.Step);
                return (c => { RunStep(sa, c); RunStep(sb, c); Diag44.Hit("compound"); }, null);
            }
            return null;
        }
        finally { _compoundDepth44--; }
    }

    // =====================================================================
    // 비용 · 조건 · 셈 · 사건 (각 체인 끝)
    // =====================================================================

    [ThreadStatic] private static int _costDepth44;

    /// <summary>
    /// 비용 (원래 규칙이 읽지 못한 것만): "deleting 1 of your level 2 Digimon in the breeding area" (BT13-080 · BT27-075 — 공식 "육성 에어리어의 Lv.2의 자신의 디지몬 1마리를 소멸시키는 것으로") ·
    /// "trashing 1 of your Digimon's link cards" (BT25-073) · "trashing 1 card with the [Rock Dragon] or [Earth Dragon] trait" (EX7-042 — 공식 "자신의 패에서") ·
    /// "placing 1 of your Digimon cards with ＜Save＞ in its text from your hand under this Tamer" (BT12-087) ·
    /// "returning 1 [Magnadramon], 1 [Azulongmon], and 1 [Megidramon] from your trash to the bottom of your deck in any order" (EX3-035 — 1장씩) ·
    /// "returning 1 of your opponent's Digimon with no digivolution cards to the bottom of the deck" (BT17-077)
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost44(string cost, CardData card)
    {
        if (!Gate44) return null;   // 18차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만
        if (_costDepth44 > 0) return null;
        _costDepth44++;
        try
        {
            string s = cost.Trim();
            Match m;
            if ((m = Regex.Match(s, @"^deleting (?:1|one) of your level (?<lv>\d+) Digimon (?:in|from) (?:the|your) breeding area$", I)).Success)
            {
                int lv = int.Parse(m.Groups["lv"].Value);
                return (c => c.Owner.BreedingArea is { } b && b.TopCard.Type == CardType.Digimon && b.TopCard.Level == lv,
                        c => { if (!c.Engine.DeleteBreeding44(c.Owner)) c.CostFailed = true; else Diag44.Hit("deleteBreedingCost"); },
                        _ => true, $"육성 에어리어의 Lv.{lv} 디지몬을 소멸시켜");
            }
            string? alt = null;
            if ((m = Regex.Match(s, @"^trashing (?<n>\d+) of your Digimon's link cards?$", I)).Success)
                alt = $"trashing {m.Groups["n"].Value} card from any of your link cards";
            else if ((m = Regex.Match(s, @"^trashing (?<p>\d+ (?:Digimon |Option |Tamer )?cards? with (?:the )?\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])* traits?)$", I)).Success)
                alt = $"trashing {m.Groups["p"].Value} in your hand";
            else if (Regex.IsMatch(s, @"^placing (?:1|one) of your Digimon cards with ", I))
                alt = Regex.Replace(s, @"^placing (?:1|one) of your Digimon cards with ", "placing 1 Digimon card with ", I);
            if (alt != null && ParseCost(alt, card) is { } r) return r;
            // 이름 1장씩: "returning 1 [A], 1 [B], and 1 [C] from your trash to the bottom of your deck (in any order)"
            if ((m = Regex.Match(s, @"^returning (?<list>1 \[[^\]]+\](?:,? (?:and )?1 \[[^\]]+\])+) from your trash to the bottom of (?:your|the) deck(?: in any order)?$", I)).Success)
            {
                var names = Regex.Matches(m.Groups["list"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
                List<CardData>? Picks(EffectContext c)
                {
                    var used = new List<CardData>();
                    foreach (var n in names)
                    {
                        var x = c.Owner.Trash.FirstOrDefault(y => CardNames.Is(y, n) && !used.Any(u => ReferenceEquals(u, y)));
                        if (x == null) return null;
                        used.Add(x);
                    }
                    return used;
                }
                return (c => Picks(c) != null,
                        c =>
                        {
                            var picks = Picks(c);
                            if (picks == null) { c.CostFailed = true; return; }
                            foreach (var x in picks) { c.Owner.Trash.Remove(x); c.Owner.MainDeck.Add(x); }
                            c.PaidCount = picks.Count;
                            Diag44.Hit("namedReturnCost");
                        },
                        _ => true, $"트래시의 {string.Join(" · ", names)} 1장씩을 덱 아래로 되돌려");
            }
            // 상대의 디지몬을 덱 아래로: "returning 1 of your opponent's Digimon with no digivolution cards to the bottom of the deck"
            if ((m = Regex.Match(s, @"^returning (?<t>1 of your opponent's .+?) to the bottom of (?:the|its owner's|their) deck$", I)).Success
                && TargetSpec.Parse(m.Groups["t"].Value) is { Opponent: true, AnySide: false } ts)
            {
                List<CardInstance> Pool(EffectContext c) => c.Enemy.BattleArea.Where(d => ts.Filters.All(f => f(d)) && ts.ContextFilters.All(f => f(c, d)) && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
                return (c => Pool(c).Count > 0,
                        c =>
                        {
                            var pool = Pool(c).OrderByDescending(d => d.GetCurrentDP()).ToList();
                            var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 덱 아래로 되돌릴 상대의 디지몬을 고르세요.", optional: false, preselected: pool.FirstOrDefault()).FirstOrDefault();
                            if (d == null) { c.CostFailed = true; return; }
                            c.Engine.BottomDeck(c.Enemy, d);
                            if (c.Enemy.BattleArea.Contains(d)) c.CostFailed = true;
                            else Diag44.Hit("opponentBottomDeckCost");
                        },
                        _ => true, "상대의 디지몬 1마리를 덱 아래로 되돌려");
            }
            return null;
        }
        finally { _costDepth44--; }
    }

    /// <summary>
    /// 조건: 【소멸 시】 소멸하기 전의 진화원 — "[Numemon] or [X Antibody] was in this Digimon's digivolution cards" (BT15-057 지속 효과로 얻는 【소멸 시】) ·
    /// "this card had digivolution cards" (BT2-083 — 공식 "이 카드가 진화원을 가지고 있었다면")
    /// </summary>
    private static Func<EffectContext, bool>? ParseEventCondition44(string cond)
    {
        if (!Gate44) return null;   // 18차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만
        string c0 = cond.Trim();
        Match m;
        IEnumerable<CardData> Sources(EffectContext c) => c.DeletedSources44 ?? (IEnumerable<CardData>?)c.Self?.FaceUpSources ?? Array.Empty<CardData>();
        if ((m = Regex.Match(c0, $@"^(?<x>{TargetSpec.NameList})(?<nm> in (?:its|their) names?)? (?:was|were) in this Digimon's digivolution cards when it was deleted$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return c =>
            {
                bool ok = Sources(c).Any(s => xs.Any(x => TargetSpec.NameHas(s, x)));   // "is in this Digimon's digivolution cards"와 같게 (이름에 포함)
                if (ok) Diag44.Hit("wasInSources");
                return ok;
            };
        }
        if (Regex.IsMatch(c0, @"^this (?:card|Digimon) had (?:any )?digivolution cards$", I))
            return c => Sources(c).Any();
        return null;
    }

    /// <summary>
    /// 셈: "suspended Tamer" (LM-010 — 양쪽) · "of your opponent's unsuspended Digimon and Tamers" (BT9-069) ·
    /// "2 unsuspended Digimon and/or Tamers your opponent has in play" (BT9-069)
    /// </summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter44(string counter)
    {
        if (!Gate44) return null;   // 18차 규칙은 원래 규칙이 읽지 못한 절을 다시 읽을 때만
        string s = counter.Trim().TrimEnd('.');
        Match m;
        static int Susp(PlayerZones z) => z.Tamers.Count(t => z.SuspendedTamers.Contains(t));
        static int Active(PlayerZones z) => z.BattleArea.Count(d => d.TamerProxyOf == null && !d.IsSuspended) + z.Tamers.Count(t => !z.SuspendedTamers.Contains(t));
        if (Regex.IsMatch(s, @"^suspended Tamers?(?: in play)?$", I))
            return (o, e, _) => Susp(o) + Susp(e);
        // "of your [Mother Eater]'s digivolution cards in the breeding area" (BT23-075 — 육성 에어리어의 그 디지몬의 진화원 수)
        if ((m = Regex.Match(s, $@"^of your (?<x>{TargetSpec.NameList})(?:'s|’s) digivolution cards in the breeding area$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return (o, _, _) => o.BreedingArea is { } b && xs.Any(x => CardNames.Is(b.TopCard, x)) ? b.EvolutionSources.Count : 0;
        }
        // "2 [Negamon] in your trash or your Digimon's digivolution cards" (EX9-054 · EX9-005 — 트래시와 디지몬의 진화원의 합계)
        if ((m = Regex.Match(s, $@"^(?:(?<n>\d+) )?(?<x>{TargetSpec.NameList}) in your trash (?:or|and) your Digimon's digivolution cards$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            int per = m.Groups["n"].Success ? Math.Max(1, int.Parse(m.Groups["n"].Value)) : 1;
            bool Is(CardData x) => xs.Any(y => CardNames.Is(x, y));
            return (o, _, _) => (o.Trash.Count(Is) + o.BattleArea.Sum(d => d.FaceUpSources.Count(Is))) / per;
        }
        // "yellow and/or red Tamers you have in play" (BT9-099 — 복수형)
        if ((m = Regex.Match(s, @"^(?<c>(?:red|blue|yellow|green|black|purple|white)(?: (?:or|and/or) (?:red|blue|yellow|green|black|purple|white))*) Tamers you have(?: in play)?$", I)).Success)
            return ParseCounter($"{m.Groups["c"].Value} Tamer you have in play");
        // "2 colors your opponent's Digimon and Tamers have" (EX10-068) · "2 colors your Tamers with the [ADVENTURE] trait have" (ST20-09) · "color Digimon and Tamers have" (P-182 — 양쪽)
        if ((m = Regex.Match(s, @"^(?:(?<n>\d+) )?colou?rs? (?<who>your opponent's Digimon and Tamers|your Digimon and Tamers|Digimon and Tamers|your Tamers with the (?<x>\[[^\]]+\]) trait) have$", I)).Success)
        {
            int per = m.Groups["n"].Success ? Math.Max(1, int.Parse(m.Groups["n"].Value)) : 1;
            string who = m.Groups["who"].Value.ToLowerInvariant();
            var xs = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            return (o, e, _) =>
            {
                IEnumerable<CardData> cards = who.StartsWith("your opponent's") ? e.BattleArea.Where(d => d.TamerProxyOf == null).Select(d => d.TopCard).Concat(e.Tamers)
                    : who.StartsWith("your digimon") ? o.BattleArea.Where(d => d.TamerProxyOf == null).Select(d => d.TopCard).Concat(o.Tamers)
                    : who.StartsWith("your tamers") ? o.Tamers.Where(t => xs!.Any(x => TargetSpec.HasTrait(t, x)))
                    : o.BattleArea.Concat(e.BattleArea).Where(d => d.TamerProxyOf == null).Select(d => d.TopCard).Concat(o.Tamers).Concat(e.Tamers);
                return cards.SelectMany(x => x.AllColors).Distinct().Count() / per;
            };
        }
        // "color in trashes" (EX13-020 — 서로의 트래시의 색 수)
        if (Regex.IsMatch(s, @"^colou?r in (?:both players' )?trashes$", I))
            return (o, e, _) => o.Trash.Concat(e.Trash).SelectMany(x => x.AllColors).Distinct().Count();
        // "Tamer your opponent has in play" (BT9-018)
        if (Regex.IsMatch(s, @"^Tamers? your opponent has(?: in play)?$", I))
            return (_, e, _) => e.Tamers.Count;
        // "suspended Digimon your opponent has in play" (BT11-057)
        if (Regex.IsMatch(s, @"^suspended Digimon your opponent has(?: in play)?$", I))
            return (_, e, _) => e.BattleArea.Count(d => d.IsSuspended && d.TamerProxyOf == null);
        // "(other) suspended Digimon with [Vegetation], [Plant] or [Fairy] in one of their traits you have in play" (BT10-057 · EX3-045 — 특징에 포함)
        if ((m = Regex.Match(s, $@"^(?<other>other )?suspended Digimon with (?<x>{TargetSpec.NameList}) in (?:one|any) of (?:their|its) traits you have(?: in play)?$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            bool other = m.Groups["other"].Success;
            return (o, _, self) => o.BattleArea.Count(d => d.IsSuspended && d.TamerProxyOf == null && (!other || !ReferenceEquals(d, self))
                && d.TopCard.Traits.Any(t => xs.Any(x => t.Contains(x, StringComparison.OrdinalIgnoreCase))));
        }
        // "Tamer you have in play with a different color" (BT12-083 — 색이 다른 테이머 수)
        if (Regex.IsMatch(s, @"^Tamer you have in play with a different colou?r$", I))
            return (o, _, _) => o.Tamers.SelectMany(t => t.AllColors).Distinct().Count();
        // "for every 2 Tamers" (BT17-075 — 공식 "테이머 2명마다", 서로의 테이머)
        if ((m = Regex.Match(s, @"^(?<n>\d+) Tamers$", I)).Success)
        {
            int per = Math.Max(1, int.Parse(m.Groups["n"].Value));
            return (o, e, _) => (o.Tamers.Count + e.Tamers.Count) / per;
        }
        if ((m = Regex.Match(s, @"^(?:of )?(?<w>your opponent's|their|your) unsuspended Digimon and Tamers$", I)).Success)
        {
            bool mine = m.Groups["w"].Value.Equals("your", StringComparison.OrdinalIgnoreCase);
            return (o, e, _) => Active(mine ? o : e);
        }
        if ((m = Regex.Match(s, @"^(?<n>\d+) unsuspended Digimon and/or Tamers your opponent has(?: in play)?$", I)).Success)
        {
            int per = Math.Max(1, int.Parse(m.Groups["n"].Value));
            return (_, e, _) => Active(e) / per;
        }
        return null;
    }

    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase44(string ev) => null;
}
