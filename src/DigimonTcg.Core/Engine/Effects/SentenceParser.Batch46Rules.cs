using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.46.0 20차 묶음 — 표기 규칙 · 절 · 문장 · 동작 · 비용 · 조건 · 셈 (원래 규칙이 읽지 못한 것만)</summary>
internal static partial class SentenceParser
{
    /// <summary>절 전체 규칙 (문장 경계를 넘는 표기)</summary>
    private static readonly Func<string, string>[] ClauseRules46 =
    {
        // "would leave the battle area outside of a battle" (BT17-095 — 공식 "배틀 이외로 배틀 에어리어를 벗어날 때")
        t => Regex.Replace(t, @"\boutside of (?:a )?battles?\b", "other than in battle", I),
        // "Security Effect You may play ~" (BT15-091 — 시큐리티 효과 표시 [Security]가 빠졌다)
        t => Regex.Replace(t, @"^Security Effect\s+(?=[A-Z][a-z])", "Security Effect [Security] "),
        // 마침표 둘 (BT9-106)
        t => Regex.Replace(t, @"\.\.\s*$", "."),
        // "This Digimon's DP can't be reduced" (EX1-073 — 공식 "이 디지몬의 DP는 마이너스되지 않는다")
        t => Regex.Replace(t, @"\bThis Digimon's DP can't be reduced\b", "This Digimon can't have its DP reduced", I),
        // "[Dracomon] or [Examon] text Digimon" (EX13-024 — 공식 "「드라코몬」/「엑자몬」이 기술되어 있는 디지몬") · 뒤의 "1 of your such Digimon"
        t => Regex.Replace(t, @"(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) text Digimon\b(?<rest>.*?)\b1 of your such Digimon\b",
            m => $"Digimon with {m.Groups["x"].Value} in their texts{m.Groups["rest"].Value}1 of your Digimon with {m.Groups["x"].Value} in their texts", I),
    };

    private static readonly Func<string, string>[] RewriteRules46 =
    {
        // "When played from digivolution cards, ~" (BT7-018 · EX3-015 · EX3-017 — 공식 "진화원에서 등장했다면, ~") — 【등장 시】 안의 조건
        t => Regex.Replace(t, @"^When played from digivolution cards, ", "If played from digivolution cards, ", I),
        // 보호 두 술어의 다른 표기 (기간 효과):
        //  "Until your opponent's turn ends, their ＜De-Digivolve＞ effects don't affect 1 of your Digimon, and it gets +3000 DP" (EX10-031 — 공식 "자신의 디지몬 1마리는 상대의 《퇴화》의 효과를 받지 않고, DP +3000")
        t => Regex.Replace(t, @"^(?<dur>Until your opponent's turn ends|Until the end of your opponent's turn), (?:their|your opponent's) ＜De-Digivolve＞ effects don't affect (?<t>1 of your [^,]*?Digimon), and it gets (?<dp>[+-]\d+ DP)$",
            "${dur}, ${t} isn't affected by your opponent's ＜De-Digivolve＞ effects and gets ${dp}", I),
        //  "1 of your [DS] trait Digimon can't have its DP reduced or be affected by your opponent's ＜De-Digivolve＞ effects until the end of their turn" (P-162 — 공식 "상대의 효과로 DP가 마이너스되지 않고, 《퇴화》의 효과를 받지 않는다")
        t => Regex.Replace(t, @"^(?<t>1 of your [^,]*?Digimon) can't have its DP reduced or be affected by your opponent's ＜De-Digivolve＞ effects until the end of (?:their|your opponent's) turn$",
            "Until the end of your opponent's turn, ${t} can't have its DP reduced by your opponent's effects and isn't affected by your opponent's ＜De-Digivolve＞ effects", I),
        //  "~ isn't affected by the effects of your opponent's Option cards and it can't have its DP reduced" (BT19-089)
        t => Regex.Replace(t, @"\bisn't affected by the effects of your opponent's Option cards and it can't have its DP reduced$", "isn't affected by the effects of your opponent's Option cards and can't have its DP reduced", I),
        //  "Until your opponent's turn ends, their effects can't reduce the DP of 1 of your [Hudie] trait Digimon, and it gains ~" (BT23-085 — 공식 "~는 상대의 효과로 DP가 마이너스되지 않고, ~를 얻는다")
        t => Regex.Replace(t, @"^(?<dur>Until your opponent's turn ends|Until the end of your opponent's turn), (?:their|your opponent's) effects can't reduce the DP of (?<t>1 of your [^,]*?Digimon), and it gains ",
            "${dur}, ${t} can't have its DP reduced by your opponent's effects and gains ", I),
        //  "Neither player's Digimon can be deleted in battle for the turn" (BT3-099 — 공식 "이 턴 동안, 양 측의 디지몬은 배틀로 소멸하지 않는다")
        t => Regex.Replace(t, @"^Neither player's Digimon can be deleted in battle for the turn$", "For the turn, all Digimon can't be deleted in battle", I),
        // "You may place up to 5 ~ in this card's digivolution cards to gain 1 memory for each card placed" (EX1-073 — 공식 "~ 이 디지몬의 진화원에 놓는 것으로, 놓은 1장마다 메모리 +1")
        t => Regex.Replace(t, @"^You may place (?<x>.+?) (?<in>in this card's digivolution cards) to (?<y>.+?) for each card placed$", "By placing ${x} ${in}, ${y} for each card placed by this effect", I),
        // "1 of them and any of your other Digimon may DNA digivolve into ~" (EX12-003 — 떠나려는 디지몬)
        t => Regex.Replace(t, @"^1 of them and (?=(?:any of )?your other .*Digimon may DNA digivolve into )", "That Digimon and ", I),
        // 이름 주어 + 두 술어 지속 효과: "All of your [Marcus Damon] (in play) get +3000 DP and gain ＜Security A. +1＞" (BT12-043 — 공식 "자신의 「최건우」 전부는 《S 어택 +1》을 얻고, DP +3000")
        t => Regex.Replace(t, @"^All of your \[(?<x>[^\]]+)\](?: in play)? (?=gets? [+-]\d+ DP and gains? )", "All of your Digimon with [${x}] in their names ", I),
    };

    private static string HeaderRewrite46(string header) => ClauseRules46.Aggregate(header, (acc, f) => f(acc));

    private static string OptionRewrite46(string option) => option;

    /// <summary>"that Digimon" — 사건의 디지몬 (떠나려는 디지몬 등), 없으면 앞 문장의 대상</summary>
    private static CardInstance? That46(EffectContext c) =>
        c.Other ?? (c.Engine.CurrentLeave is { IsTamer: false } lv && ReferenceEquals(lv.Digimon, c.Self) ? c.Self : null) ?? c.LastTargets.FirstOrDefault();   // "When this Digimon would leave" — 떠나려는 이 디지몬

    // =====================================================================
    // 절
    // =====================================================================

    private static bool ExtraClause46(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string c0 = Regex.Replace(rawClause.Trim(), @"^(?:Inherited Effect|Security Effect)\s*", "", I);
        if (BreedingClause46(rawClause, inherited, card, def)) return true;
        if (CardTextFix46(rawClause, inherited, card) is { } fixedClause)
        {
            if (TryParse45(fixedClause, inherited, card, null) != null) return false;
            if (def != null) TryParse45(fixedClause, inherited, card, def);
            Diag46.Register("textFix:" + card.CardId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 영어 데이터가 깨진 카드 (카드 번호 · 칸 · 원문이 정확히 같을 때만 — 공식 카드 문구의 구조에 맞춘 영어):
    ///  - EX10-029 링크 효과: 키워드가 빠졌다 (공식 "【링크 시】이 디지몬의 링크 카드 1장을 파기하는 것으로, 상대의 턴 종료까지, 자신의 디지몬 1마리는 《퇴화》의 효과를 받지 않는다")
    /// </summary>
    internal static string? CardTextFix46(string clause, bool inherited, CardData card)
    {
        string t = clause.Trim();
        return card.CardId switch
        {
            "EX10-029" when inherited && t == "By trashing 1 of this Digimon's link cards, effects don't affect 1 of your Digimon until your opponent's turn ends."
                => "By trashing 1 of this Digimon's link cards, 1 of your Digimon isn't affected by ＜De-Digivolve＞ effects until your opponent's turn ends.",
            _ => null,
        };
    }

    /// <summary>[육성] "When any of your ~ would leave the battle area" (BT18-086 · BT22-007 진화원 효과) — 엔진: GoldfishEngine.Batch46Leave (RunBreedingWouldLeave46)</summary>
    private static bool BreedingClause46(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (card.Type is not (CardType.Digimon or CardType.Digitama)) return false;
        string c0 = Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", I);
        var m = Regex.Match(c0, @"^\[Breeding\]\s*(?<rest>\[(?:All Turns|Your Turn|Opponent's Turn)\].*\bwould (?:leave the battle area|be deleted)\b.+)$", I | RegexOptions.Singleline);
        if (!m.Success) return false;
        string rest = m.Groups["rest"].Value.Trim();
        var tmpCard = new CardData
        {
            CardId = card.CardId + "#breed46", Name = card.Name, Type = card.Type, Level = card.Level, Color = card.Color, SecondaryColor = card.SecondaryColor,
            Traits = card.Traits, EffectText = card.EffectText, InheritedEffectText = card.InheritedEffectText, PlayCost = card.PlayCost, DP = card.DP,
        };
        var tdef = new CardScriptBuilder().Card(tmpCard.CardId);
        _breedingParse46++;
        try
        {
            if (TryParse45(rest, inherited, tmpCard, null) != null) return false;
            TryParse45(rest, inherited, tmpCard, tdef);
        }
        finally { _breedingParse46--; }
        var s = tdef.Script41;
        if (s.Statics.Count > 0 || s.Effects.Count == 0 || s.Effects.Any(e => e.Trigger is not (EffectTrigger.AllyDigimonWouldLeave or EffectTrigger.AllyDigimonWouldBeDeleted or EffectTrigger.ThisDigimonWouldLeave or EffectTrigger.ThisDigimonWouldBeDeleted))) return false;
        if (def != null)
            foreach (var e in s.Effects)
            {
                var inner = e.Resolve;
                def.Script41.Effects.Add(e with
                {
                    Source = e.Source == EffectSource.Inherited ? EffectSource.BreedingInherited42 : EffectSource.Breeding42,
                    Resolve = c => { _gate46++; _gate45++; _gate44++; try { inner(c); } finally { _gate46--; _gate45--; _gate44--; } },
                });
            }
        return true;
    }

    /// <summary>[육성] 효과를 읽는 중 ("this Digimon" = 육성 에어리어의 이 디지몬)</summary>
    [ThreadStatic] private static int _breedingParse46;

    // =====================================================================
    // 문장 · 동작
    // =====================================================================

    private static Step? StepExtra46(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        string s = sentence.Trim().TrimEnd('.');
        Match m;
        // "1 of your Digimon may get +3000 DP until your opponent's turn ends and attack" (BT26-015 — 공식 "자신의 디지몬 1마리는 상대의 턴 종료까지 DP +3000 하고 어택할 수 있다")
        if ((m = Regex.Match(s, @"^(?<t>1 of your [^,]*?Digimon(?: with [^,]+?)?) may get (?<dp>[+-]\d+ DP) (?<dur>until [^,]+?|for the turn) and attack$", I)).Success)
        {
            var a = ParseStep($"{m.Groups["t"].Value} gets {m.Groups["dp"].Value} {m.Groups["dur"].Value}.", card, triggers);
            var b = ParseStep("That Digimon may attack.", card, triggers);
            if (a.Error != null || a.Step == null || b.Error != null || b.Step == null) return null;
            var (sa, sb) = (a.Step, b.Step);
            return new Step(null, c =>
            {
                if (!c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 자신의 디지몬 1마리를 {m.Groups["dp"].Value} 하고 어택할까요?", recommended: true)) { c.LastStepResolved = false; return; }
                RunStep(sa, c);
                if (c.LastTargets.Count == 0) return;
                Diag46.Hit("dpAndAttack");
                RunStep(sb, c);
            }, Text: sentence.Trim());
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ActionExtra46(string s, CardData card, List<EffectTrigger> triggers)
    {
        Match m;
        if (PlaceAction46(s) is { } place) return place;   // 진화원 아래에 놓기 (SentenceParser.Batch46Src)
        // "Place 1 of your [ADR-02 Searcher]s from in play or from your hand under 1 of your [Mother D-Reaper]s as its bottom digivolution card" (EX2-048 —
        // 공식 "자신의 「ADR-02=서쳐」 1마리 또는 패의 「ADR-02=서쳐」 1장을 자신의 「마더 디・리퍼」 1마리의 진화원의 가장 아래에 놓을 수 있다")
        if ((m = Regex.Match(s, @"^Place 1 of your (?<x>\[[^\]]+\])s? from in play or from your hand under 1 of your (?<y>\[[^\]]+\])s? as its bottom digivolution card$", I)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            var ys = TargetSpec.Names(m.Groups["y"].Value);
            bool IsX(CardData cd) => xs.Any(n => cd.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            bool IsY(CardData cd) => ys.Any(n => cd.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            return (c =>
            {
                var hosts = c.Owner.BattleArea.Where(d => IsY(d.TopCard)).ToList();
                if (c.Owner.BreedingArea is { } b46 && IsY(b46.TopCard)) hosts.Add(b46);   // 육성 에어리어의 「마더 디・리퍼」(디지타마)도
                if (hosts.Count == 0) { c.LastStepResolved = false; return; }
                var host = hosts.Count == 1 ? hosts[0] : c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{c.Card.Name}] 진화원 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: hosts[0]).FirstOrDefault() ?? hosts[0];
                var field = c.Owner.BattleArea.Where(d => !ReferenceEquals(d, host) && IsX(d.TopCard)).ToList();
                var handCard = c.Owner.Hand.FirstOrDefault(IsX);
                if ((handCard != null || field.Count > 0) && !c.Engine.AskYesNo(c.Owner, host, $"[{c.Card.Name}] [{host.TopCard.Name}]의 진화원 아래에 [{m.Groups["x"].Value}]을(를) 놓을까요? (공식 \"놓을 수 있다\")", recommended: true)) { c.LastStepResolved = false; return; }
                if (handCard != null && (field.Count == 0 || !c.Engine.AskYesNo(c.Owner, host, $"[{c.Card.Name}] 필드의 [{field[0].TopCard.Name}] 대신 패의 [{handCard.Name}]을(를) 놓을까요?", recommended: true)))
                {
                    c.Owner.Hand.Remove(handCard);
                    host.AddBottomSource(handCard);
                    c.RecalculateFieldBuffs();
                    Diag46.Hit("placeNamedUnder");
                    c.Log($"[{c.Card.Name}] 패의 [{handCard.Name}]을(를) [{host.TopCard.Name}]의 진화원 아래에 놓았습니다.");
                    return;
                }
                if (field.Count == 0) { c.LastStepResolved = false; return; }
                var mv = field.Count == 1 ? field[0] : c.Engine.ChooseTargets(c.Owner, field, 1, $"[{c.Card.Name}] 진화원 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: field[0]).FirstOrDefault() ?? field[0];
                if (c.Engine.PlaceDigimonUnder46(c.Owner, mv, host)) { Diag46.Hit("placeNamedUnder"); c.Log($"[{c.Card.Name}] [{mv.TopCard.Name}]을(를) [{host.TopCard.Name}]의 진화원 아래에 놓았습니다."); }
                else c.LastStepResolved = false;
            }, null);
        }
        // "That Digimon and a card in the hand may DNA digivolve into a Digimon card with [Omnimon] in its name in the hand" (BT17-095) ·
        // "1 of your level 6 Digimon and 1 card in the hand may DNA digivolve into a level 7 Digimon card in your hand" (EX6-072) — 공식 "~와 패의 카드로, 패의 ~로 조그레스 진화할 수 있다"
        if ((m = Regex.Match(s, @"^(?<who>that Digimon|1 of your (?<spec>[^,]*?)Digimon(?<with> with [^,]+?)?) and (?:a|1) card in (?:the|your) hand may DNA digivolve into (?<pick>.+?) in (?:the|your) hand$", I)).Success
            && RevealPick45(m.Groups["pick"].Value) is { } dp)
        {
            bool that = m.Groups["who"].Value.Equals("that Digimon", StringComparison.OrdinalIgnoreCase);
            var spec = that ? null : TargetSpec.Parse("all of your " + m.Groups["spec"].Value + "Digimon" + m.Groups["with"].Value);
            if (!that && (spec == null || spec.Opponent || spec.ContextFilters.Count > 0)) return null;
            var into = dp.Match;
            return (c =>
            {
                var t = that ? That46(c) : null;
                Func<CardInstance, bool> ok = that ? d => ReferenceEquals(d, t) : d => spec!.Filters.All(f => f(d));
                if (that && (t == null || !c.Owner.BattleArea.Contains(t))) { c.LastStepResolved = false; return; }
                var r = c.Engine.DnaWithHandCard46(c.Owner, ok, into, c.Card.Name);
                c.LastStepResolved = r != null;
                c.LastTargets = r != null ? new List<CardInstance> { r } : new List<CardInstance>();
            }, null);
        }
        // "That Digimon and any of your other Digimon may DNA digivolve into an [ME] trait Digimon card in the hand" (EX12-003 "1 of them and ~")
        if ((m = Regex.Match(s, @"^that Digimon and (?:any of )?your other (?<spec>[^,]*?)Digimon(?<with> with [^,]+?)? may DNA digivolve into (?<pick>.+?) in (?:the|your) hand$", I)).Success
            && RevealPick45(m.Groups["pick"].Value) is { } tp && TargetSpec.Parse("all of your " + m.Groups["spec"].Value + "Digimon" + m.Groups["with"].Value) is { Opponent: false } os && os.ContextFilters.Count == 0)
        {
            var into = tp.Match;
            return (c =>
            {
                if (That46(c) is not { } t || !c.Owner.BattleArea.Contains(t)) { c.LastStepResolved = false; return; }
                var r = c.Engine.DnaWithThat46(c.Owner, t, d => !ReferenceEquals(d, t) && os.Filters.All(f => f(d)), into, c.Card.Name);
                c.LastStepResolved = r != null;
                c.LastTargets = r != null ? new List<CardInstance> { r } : new List<CardInstance>();
            }, null);
        }
        // "You may play 1 [Wicked God] trait Digimon card with a play cost 1 higher than that Digimon from your hand or trash without paying the cost" (BT19-099 —
        // 공식 "그 디지몬보다 등장 코스트가 1 높은 ~") — 처리할 때 그 디지몬의 등장 코스트로 바꿔 읽는다
        if ((m = Regex.Match(s, @"^(?<a>(?:you may )?play 1 .+?) with a play cost (?<n>\d+) higher than that Digimon(?<b> from .+)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            string Fill(int v) => Cap44(m.Groups["a"].Value) + $" with a play cost of {v}" + m.Groups["b"].Value + ".";
            if (RuntimeStep45(Fill(5), card, triggers) == null) return null;
            return (c =>
            {
                if (That46(c) is not { } t) { c.LastStepResolved = false; return; }
                int v = t.TopCard.PlayCost + n;
                Diag46.Hit("playCostHigherThanThat");
                if (RuntimeStep45(Fill(v), c.Card, triggers) is { } st) RunStep(st, c);
                else c.LastStepResolved = false;
            }, null);
        }
        // "you may place them as this Digimon's bottom digivolution cards" (BT22-007 [육성] 진화원 효과 — 공식 "그 디지몬을 이 디지몬의 진화원 아래에 놓을 수 있다")
        if ((m = Regex.Match(s, @"^(?<may>you may )?place (?:them|those Digimon|that Digimon|it) as this Digimon's bottom digivolution cards?$", I)).Success)
        {
            bool may = m.Groups["may"].Success;
            return (c =>
            {
                if (That46(c) is not { } t || c.Self == null || ReferenceEquals(t, c.Self) || !c.Owner.BattleArea.Contains(t)) { c.LastStepResolved = false; return; }
                if (may && !c.Engine.AskYesNo(c.Owner, t, $"[{c.Card.Name}] [{t.TopCard.Name}]을(를) 이 디지몬의 진화원 아래에 놓을까요?", recommended: true)) { c.LastStepResolved = false; return; }
                if (c.Engine.PlaceDigimonUnder46(c.Owner, t, c.Self)) c.Log($"[{c.Card.Name}] [{t.TopCard.Name}]을(를) [{c.Self.TopCard.Name}]의 진화원 아래에 놓았습니다.");
                else c.LastStepResolved = false;
            }, null);
        }
        // "you may place those Digimon face-up at the bottom of your security stack" (BT19-053) · "place 1 of those Digimon as the top security card" (BT23-080) — 떠나려는 디지몬을 대신 시큐리티에
        if ((m = Regex.Match(s, @"^(?<may>you may )?place (?:those Digimon|that Digimon|1 of those Digimon|it|them) (?:(?<up>face[- ]up )?(?:at the (?<pos>bottom|top) of your security stack|as the (?<pos2>top|bottom) security card)(?: face[- ]up)?)$", I)).Success)
        {
            bool may = m.Groups["may"].Success, faceUp = m.Groups["up"].Success || s.Contains("face-up", StringComparison.OrdinalIgnoreCase) || s.Contains("face up", StringComparison.OrdinalIgnoreCase);
            bool top = (m.Groups["pos"].Success ? m.Groups["pos"].Value : m.Groups["pos2"].Value).Equals("top", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                if (That46(c) is not { } t || !c.Owner.BattleArea.Contains(t)) { c.LastStepResolved = false; return; }
                if (may && !c.Engine.AskYesNo(c.Owner, t, $"[{c.Card.Name}] [{t.TopCard.Name}]을(를) 시큐리티 {(top ? "위" : "아래")}에 놓을까요?", recommended: true)) { c.LastStepResolved = false; return; }
                bool leaving = c.Engine.CurrentLeave is { } lv && ReferenceEquals(lv.Digimon, t);
                if (c.Engine.PlaceDigimonInSecurity(c.Owner, t, top, faceUp, canPrevent: true, skipLeave: leaving))
                {
                    if (leaving) c.Engine.MarkLeaveEscaped46(t);
                    Diag46.Hit("leavingToSecurity");
                    c.Log($"[{c.Card.Name}] [{t.TopCard.Name}]을(를) 시큐리티 {(top ? "위" : "아래")}에 {(faceUp ? "앞면으로 " : "")}놓았습니다.");
                }
                else c.LastStepResolved = false;
            }, null);
        }
        return null;
    }

    // =====================================================================
    // 사건 · 셈 · 비용 · 조건 · 지속 효과
    // =====================================================================

    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase46(string ev)
    {
        if (!Gate46) return null;
        // "When your effects add to decks" (BT26-015 — 공식 "자신의 효과로 덱에 카드가 추가되었을 때") — 엔진: GoldfishEngine.Batch46 (BeginDeckAddWatch46)
        if (Regex.IsMatch(ev.Trim(), @"^(?:your effects|one of your effects|an effect of yours) adds? (?:cards )?to (?:decks|a deck|the decks?|your deck)$", I))
            return (new List<EffectTrigger> { EffectTrigger.AllyEffectAddedToDeck46 }, null);
        return null;
    }

    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter46(string counter)
    {
        if (!Gate46) return null;
        return null;
    }

    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost46(string cost, CardData card)
    {
        if (!Gate46) return null;
        if (PlaceCost46(cost) is { } placeCost) return placeCost;   // 진화원 아래에 놓기 (SentenceParser.Batch46Src)
        Match m;
        // "digivolving that Digimon into a Digimon card with [Imperialdramon] in its name in your hand without paying the cost" (BT17-097) ·
        // "digivolving it into [Chronomon: Destroy Mode] in the hand or trash without paying the cost" (BT26-085) · [트래시] "digivolving it into this card without paying the cost" (BT17-065)
        if ((m = Regex.Match(cost.Trim(), @"^digivolving (?<who>that Digimon|it|this Digimon) into (?<p>.+?)(?: in (?:the|your) (?<z>hand or trash|hand|trash))?(?<free> without paying (?:the|its) cost)?$", I)).Success)
        {
            bool thisCard = Regex.IsMatch(m.Groups["p"].Value, @"^this card$", I);
            var pick = thisCard ? null : RevealPick45(m.Groups["p"].Value);
            if (!thisCard && pick == null) return null;
            if (thisCard && !_trashZoneParse) return null;
            string z = m.Groups["z"].Success ? m.Groups["z"].Value.ToLowerInvariant() : "hand";
            bool hand = z.Contains("hand"), trash = z.Contains("trash");
            bool free = m.Groups["free"].Success;
            bool self = m.Groups["who"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
            CardInstance? Target(EffectContext c) => self ? c.Self : c.Other ?? c.Self;
            List<(CardData X, List<CardData> From, int Cost)> Options(EffectContext c)
            {
                var d = Target(c);
                var list = new List<(CardData, List<CardData>, int)>();
                if (d == null || !c.Owner.BattleArea.Contains(d) || EffectRules.IsDigivolveLocked(c.Engine.State, d)) return list;
                IEnumerable<(CardData X, List<CardData> From)> pool = thisCard
                    ? (c.Owner.Trash.Contains(c.Card) ? new[] { (c.Card, c.Owner.Trash) } : Array.Empty<(CardData, List<CardData>)>())
                    : (hand ? c.Owner.Hand.Distinct().Select(x => (x, c.Owner.Hand)) : Enumerable.Empty<(CardData, List<CardData>)>())
                        .Concat(trash ? c.Owner.Trash.Distinct().Select(x => (x, c.Owner.Trash)) : Enumerable.Empty<(CardData, List<CardData>)>());
                foreach (var (x, from) in pool)
                {
                    if (x.Type != CardType.Digimon || (!thisCard && !pick!.Value.Match(x))) continue;
                    if (!(ActionValidator.ValidateEvolution(d, x).CanExecute || KoreanNameEvo46(x, d)) || !CardScripts.EvoAllowed(d, x)) continue;   // 공식 한글 〔진화〕「이름」 조건도 (BT17-065)
                    list.Add((x, from, free ? 0 : ActionValidator.EvolutionCost(d, x)));
                }
                return list.OrderByDescending(o => o.Item1.Level).ThenBy(o => o.Item3).ToList();
            }
            return (c => Options(c).Count > 0,
                c =>
                {
                    var opts = Options(c);
                    var d = Target(c);
                    if (opts.Count == 0 || d == null) { c.CostFailed = true; return; }
                    var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, opts.Select(o => o.X).ToList(), 1, false, $"[{c.Card.Name}] [{d.TopCard.Name}]을(를) 진화시킬 카드를 고르세요.");
                    var pickO = opts[k.Count > 0 ? Math.Clamp(k[0], 0, opts.Count - 1) : 0];
                    c.Engine.EvolveByEffect(c.Owner, d, pickO.X, pickO.Cost, pickO.From);
                    if (!ReferenceEquals(d.TopCard, pickO.X)) { c.CostFailed = true; return; }
                    c.LastTargets = new List<CardInstance> { d };
                    Diag46.Hit("digivolveInsteadOfLeave");
                },
                c => Options(c).Any(o => o.Cost <= Math.Max(0, c.OwnerMemory) || !c.OwnerIsPlayer),
                "진화시켜");
        }
        // [육성] "moving this Digimon to (the) battle area" (BT18-086 — 공식 "이 디지몬을 배틀 에어리어로 이동시키는 것으로")
        if (_breedingParse46 > 0 && Regex.IsMatch(cost.Trim(), @"^moving this Digimon to (?:the )?battle area$", I))
            return (c => c.Self != null && ReferenceEquals(c.Owner.BreedingArea, c.Self) && c.Self.TopCard.Type == CardType.Digimon && c.Owner.BattleArea.Count < 12,
                c =>
                {
                    if (c.Self == null || !ReferenceEquals(c.Owner.BreedingArea, c.Self) || c.Engine.MoveBreedingToBattle(c.Owner) is null) { c.CostFailed = true; return; }
                    Diag46.Hit("breedingMoveCost");
                },
                _ => true,
                "이 디지몬을 배틀 에어리어로 이동시켜");
        return null;
    }

    private static Func<PlayerZones, PlayerZones, bool>? ParseSideCondition46(string cond)
    {
        if (!Gate46) return null;
        Match m;
        // "you have a non-white Digimon with [Lucemon] in its name" (BT18-086 — 공식 "화이트 이외의 명칭에 「루체몬」을 포함하는 자신의 디지몬이 있는")
        if ((m = Regex.Match(cond.Trim(), @"^you have an? non-(?<c>red|blue|yellow|green|black|purple|white) Digimon with (?<x>\[[^\]]+\]) in its name$", I)).Success)
        {
            var color = Enum.Parse<CardColor>(m.Groups["c"].Value, true);
            var names = TargetSpec.Names(m.Groups["x"].Value);
            return (o, _) => o.BattleArea.Any(d => d.TamerProxyOf == null && !d.Colors.Contains(color) && names.Any(n => TargetSpec.NameHas(d.TopCard, n)));
        }
        return null;
    }

    private static bool ParseStatic46(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer)
    {
        if (!Gate46) return false;
        var sentences = SplitSentences(body);
        if (sentences.Count != 1) return false;
        string s = sentences[0].Trim().TrimEnd('.');
        Func<StaticContext, bool> turn = qualifier switch
        {
            Qualifier.YourTurn => c => c.OwnersTurn,
            Qualifier.OpponentsTurn => c => !c.OwnersTurn,
            _ => outer ?? (_ => true),
        };
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        Match m;
        Func<StaticContext, bool>? pre = null;
        if ((m = Regex.Match(s, @"^While (?<c>[^,]+), (?<rest>.+)$", I)).Success)
        {
            if (ParseStaticCondition(m.Groups["c"].Value) is not { } pc) return false;
            pre = pc;
            s = m.Groups["rest"].Value;
        }
        var p2 = pre;
        // "This Digimon and all of your Digimon with [Knightmon] in their names or [Knightsmon] in their names are unblockable" (EX4-042 — 공식 "이 디지몬과 명칭에 「나이트몬」/「나이츠몬」을 포함하는 디지몬 전부는 블록당하지 않는다")
        if (pre == null && (m = Regex.Match(s, @"^This Digimon and all of your Digimon with (?<x>\[[^\]]+\]) in their names or (?<y>\[[^\]]+\]) in their names are unblockable$", I)).Success)
        {
            var a = new Pending();
            var b = new Pending();
            if (ParseStatic("This Digimon can't be blocked.", inherited, card, qualifier, a, outer) != null) return false;
            if (ParseStatic($"All of your Digimon with {m.Groups["x"].Value} or {m.Groups["y"].Value} in their names can't be blocked.", inherited, card, qualifier, b, outer) != null) return false;
            pending.Registrations.AddRange(a.Registrations);
            pending.Registrations.AddRange(b.Registrations);
            return true;
        }
        // "all of your 0 DP Digimon can't be deleted" (BT18-086 — 공식 "DP 0의 자신의 디지몬 전부는 소멸하지 않는다": DP 0 룰 처리도)
        if ((m = Regex.Match(s, @"^all of your (?<dp>\d+) DP Digimon can't be deleted$", I)).Success)
        {
            int dp = int.Parse(m.Groups["dp"].Value);
            pending.Registrations.Add(d => d.Static(StaticStat.CannotBeDeleted46, source, c => turn(c) && (p2 == null || p2(c)) ? 1 : 0,
                subject: x => x.GetCurrentDP() <= dp));
            return true;
        }
        // "All of your [Yuu Amano] can't be deleted" (BT11-082 — 공식 "자신의 「노유빈」 전부는 소멸하지 않는다": 테이머)
        if ((m = Regex.Match(s, @"^All of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*)s? can't be deleted$", I)).Success)
        {
            var names = TargetSpec.Names(m.Groups["x"].Value);
            Func<CardInstance, bool> named = x => names.Any(n => x.TopCard.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            pending.Registrations.Add(d => d.Static(StaticStat.TamerCannotBeDeleted46, source, c => turn(c) && (p2 == null || p2(c)) ? 1 : 0, subject: named));
            pending.Registrations.Add(d => d.Static(StaticStat.CannotBeDeleted46, source, c => turn(c) && (p2 == null || p2(c)) ? 1 : 0, subject: named));
            return true;
        }
        return false;
    }
}
