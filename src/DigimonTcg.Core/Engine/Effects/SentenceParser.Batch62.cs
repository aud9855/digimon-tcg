using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.62.0 미지원 문장 36차 묶음: 원래 규칙(1~35차)이 읽지 못한 절만 36차 규칙으로 다시 읽는다.
///  - 절 하나로 읽는 것 (ClauseSpecial62):
///    BT7-055가 주는 효과 "[Your Turn] When this Digimon would unsuspend, you must trash 1 card in your hand or prevent this Digimon from unsuspending."
///      (액티브가 되려 할 때 — 공식 Q&amp;A BT7-055 Q1: 액티브가 될 디지몬 1마리마다 패 1장) ·
///    EX6-031 "[Your Turn] Change ＜Security A. -＞ to ＜Security A. +＞ on all of your Digimon." (효과마다 부호를 따로 센다 — CardInstance.SaNeg62) ·
///    BT8-059 "[All Turns] Players can't ignore digivolution requirements." (공식 Q&amp;A BT8-059 Q1 — 서로의 플레이어) ·
///    옵션 [메인] "All of your ~ Digimon gain (＜X＞ and) "~" for the turn" (BT4-101 · BT9-102 — 이 턴 동안 조건에 맞는 자신의 디지몬 전부: 나중에 나온 디지몬도, 공식 Q&amp;A BT9-102 Q1) ·
///    옵션 [메인] "Until ~, 1 of your Digimon gains "~" and "~"" (BT13-108 — 키워드 지속 효과 문장도 줄 수 있다)
///    BT4-101이 주는 효과 "[Your Turn] When attacking an opponent's Digimon with no digivolution cards, delete that Digimon." (그 디지몬 = 어택 대상 — 효과로 소멸, 공식 Q&amp;A BT4-101 Q1)
///  - 같은 뜻 다른 표기 (ClauseRewrite62): "If this Digimon has a digivolution card" (BT9-102가 주는 효과) ·
///    "isn't affected by your opponent's Option cards" (BT13-108이 주는 효과 — 공식 Q&amp;A BT13-108 Q1: 【시큐리티】 효과도)
/// 엔진은 GoldfishEngine.Batch62, 렌더러도 같은 표 (KoreanEffectRenderer.Batch62.cs)
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _rewriteDepth62;

    /// <summary>이 묶음 규칙을 끄고 앞의 규칙(35차까지)만으로 읽는 중</summary>
    [ThreadStatic] private static int _off62;

    private static string? TryParse62(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (_off62 > 0) return TryParse61(rawClause, inherited, card, def);
        string t = Normalize58(rawClause);
        // "All of your ~ gain "~" for the turn" (BT4-101): 앞의 규칙만으로 (안의 효과 문장도 35차까지로) 읽는 문장은 앞의 규칙 그대로 — 이전에 지원하던 문장은 그대로.
        //  안의 효과 문장을 이 묶음에서 처음 읽게 된 것은 이 묶음 규칙 (이 턴 동안 나중에 나온 디지몬도 얻는다 — 공식 Q&A BT9-102 Q1)
        if (SideGrantRe62.IsMatch(t))
        {
            string? old;
            _off62++;
            try { old = TryParse61(rawClause, inherited, card, null); }
            finally { _off62--; }
            if (old == null) return TryParse61(rawClause, inherited, card, def);
            return ClauseSpecial62(t, inherited, card, def) ? null : old;
        }
        string? err = TryParse61(rawClause, inherited, card, def);
        if (err == null) return err;
        if (ClauseSpecialRe62.IsMatch(t)) return ClauseSpecial62(t, inherited, card, def) ? null : err;
        if (_rewriteDepth62 > 0) return err;
        string alt = ClauseRewrite62(t);
        if (alt == t) return err;
        _rewriteDepth62++;
        try
        {
            if (TryParse61(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse61(alt, inherited, card, def);
            return null;
        }
        finally { _rewriteDepth62--; }
    }

    /// <summary>같은 뜻 다른 표기 — 절 전체에 차례로 (따옴표 안의 효과 문장에도 · 렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules62 =
    {
        // BT9-102가 주는 효과 "[On Play] If this Digimon has a digivolution card, ＜Blitz＞" — 진화원이 1장 이상
        s => Regex.Replace(s, @"\bIf this Digimon has a digivolution card,", "If this Digimon has 1 or more digivolution cards,", RegexOptions.IgnoreCase),
        // BT13-108이 주는 효과 "[Opponent's Turn] This Digimon isn't affected by your opponent's Option cards." — 상대의 옵션 카드의 효과 (【시큐리티】 효과도 — 공식 Q&A BT13-108 Q1)
        s => Regex.Replace(s, @"\bisn't affected by your opponent's Option cards(?=\.?(?:""|$))", "isn't affected by your opponent's Option cards' effects", RegexOptions.IgnoreCase),
        // BT4-090 "Then, it can attack your opponent's Digimon. This effect allows you to attack unsuspended Digimon as well." — BT21-096과 같은 표기로 (액티브 상태인 디지몬에게도)
        s => Regex.Replace(s, @"\bThen, it can attack your opponent's Digimon\. This effect allows you to attack unsuspended Digimon as well\.?$", "Then, it may attack your opponent's Digimon. Your opponent's unsuspended Digimon can also be attacked with this effect.", RegexOptions.IgnoreCase),
        // EX13-035 "give all of your opponent's Digimon ＜Security A. -1＞ (~) and -3000 DP" — 상대의 디지몬 전부 DP -3000 · ≪시큐리티 어택 -1≫ (조건 "there are ~"는 ParseStaticCondition62)
        s => Regex.Replace(s, @"\bgive all of your opponent's Digimon ＜Security A\. -(?<n>\d+)＞(?:\s*\([^)]*\))? and -(?<dp>\d+) DP\.?$", "all of your opponent's Digimon gain ＜Security A. -${n}＞ and get -${dp} DP.", RegexOptions.IgnoreCase),
    };

    /// <summary>한글로 옮길 때만 쓰는 같은 뜻 다른 표기 (엔진은 ClauseSpecial62가 따로 읽는다)</summary>
    internal static string KoRewrite62(string rawClause) =>
        ClauseRewrite62(Regex.Replace(rawClause, @"\bWhen attacking (?=an opponent's Digimon\b)", "When this Digimon attacks ", RegexOptions.IgnoreCase));

    internal static string ClauseRewrite62(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules62) s = f(s);
        return s;
    }

    // =====================================================================
    // 절 하나로 읽는 것
    // =====================================================================

    internal static readonly Regex WouldUnsuspendRe62 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*When this Digimon would unsuspend, you must trash 1 card in your hand or prevent this Digimon from unsuspending\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex SaFlipRe62 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*Change ＜Security A(?:ttack)?\. ?-＞ to ＜Security A(?:ttack)?\. ?\+＞ on all of your Digimon\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex NoIgnoreReqRe62 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*Players can't ignore digivolution requirements\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex SideGrantRe62 = new("^\\[Main\\]\\s*(?:You may trash 1 (?<cost>card with [^\"]+?) from your hand to have a|A)ll of your (?<t>[^\"]*?Digimon(?: with [^\"]+?)?) gain (?:(?<kws>＜[^＞]+＞)(?:\\s*\\([^)]*\\))? and )?\"(?<g>[^\"]+)\"\"? for the turn\\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex TwoGrantsRe62 = new("^\\[Main\\]\\s*(?<d>Until the end of your opponent's turn|For the turn), (?<t>1 of your [^\"]*?Digimon(?: with [^\"]+?)?) gains \"(?<a>[^\"]+)\" and \"(?<b>[^\"]+)\"\\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex UnsuspendedDeDigivolveRe62 = new(@"^(?<q>\[Opponent's Turn\])\s*\[Once Per Turn\]\s*When an opponent's Digimon becomes unsuspended, ＜De-Digivolve (?<n>\d+)＞ 1 of those Digimon\.?(?:\s*\([^)]*\))?\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex AttackingDeleteRe62 = new(@"^(?<q>\[(?:Your Turn|All Turns|Opponent's Turn)\])\s*When attacking an opponent's (?<t>Digimon(?: with [^,]+)?), delete that Digimon\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClauseSpecialRe62 = new($"{WouldUnsuspendRe62}|{SaFlipRe62}|{NoIgnoreReqRe62}|{SideGrantRe62}|{TwoGrantsRe62}|{AttackingDeleteRe62}|{UnsuspendedDeDigivolveRe62}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>지속 효과의 조건 (While ~) — 이 묶음에서 바꿔 쓴 절을 다시 읽을 때만: "there are 3 or more Digimon with [Sukamon] or [Etemon] in their names" (EX13-035 — 서로의 디지몬)</summary>
    private static Func<StaticContext, bool>? ParseStaticCondition62(string cond)
    {
        if (_rewriteDepth62 == 0) return null;
        var m = Regex.Match(cond.Trim(), @"^there are (?<n>\d+) or more (?<x>Digimon with .+)$", I);
        if (!m.Success || TargetSpec.Parse("all of your " + m.Groups["x"].Value) is not { ContextFilters.Count: 0, Opponent: false, AnySide: false, Selector: null } spec) return null;
        int n = int.Parse(m.Groups["n"].Value);
        return c => c.Owner.BattleArea.Concat(c.Enemy.BattleArea).Count(d => d.TamerProxyOf == null && spec.Filters.All(f => f(d))) >= n;
    }

    /// <summary>"When this Digimon would unsuspend" 효과를 가진 카드 (주는 효과만 든 카드 포함 — 엔진의 빠른 확인)</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> WouldUnsuspendCards62 = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"All of your level 6 Digimon with the [Machine] trait" 등 — 자신의 디지몬 조건 (읽지 못하면 null)</summary>
    internal static TargetSpec? SideGrantTarget62(string t)
    {
        if (TargetSpec.Parse("all of your " + t.Trim()) is not { } spec || spec.Opponent || spec.AnySide || spec.IsThis || spec.IsThat || spec.Selector != null || spec.ContextFilters.Count > 0) return null;
        return spec;
    }

    /// <summary>절 하나로 읽는 규칙 — 읽었으면 true (def가 있으면 등록)</summary>
    private static bool ClauseSpecial62(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        Match m;
        // BT7-055가 주는 효과 "[Your Turn] When this Digimon would unsuspend, you must trash 1 card in your hand or prevent this Digimon from unsuspending."
        //  — 액티브 페이즈 · 효과로 액티브가 되려 할 때 그 디지몬 1마리마다 (공식 Q&A BT7-055 Q1). "you" = 이 효과를 가진 디지몬의 주인
        if ((m = WouldUnsuspendRe62.Match(t)).Success)
        {
            if (inherited || card.Type != CardType.Digimon) return false;
            WouldUnsuspendCards62[card.CardId] = true;
            if (def == null) return true;
            string q = m.Groups["q"].Value.ToLowerInvariant();
            Func<EffectContext, bool>? when = q == "[your turn]" ? c => c.Engine.IsTurnOf(c.Owner) : q == "[opponent's turn]" ? c => !c.Engine.IsTurnOf(c.Owner) : null;
            def.On(EffectTrigger.ThisDigimonWouldUnsuspend62, c =>
            {
                if (c.Self is not { } self) return;
                bool pay = c.Owner.Hand.Count > 0 && c.Engine.AskYesNo(c.Owner, self,
                    $"[{self.TopCard.Name}]을(를) 액티브로 하려면 패 1장을 파기해야 합니다. 파기할까요? (아니요 = 액티브가 되지 않습니다)", c.Engine.RecommendUnsuspendDiscard62(c.Owner, self));
                if (pay && c.Engine.DiscardFromHand(c.Owner, 1) == 1)
                {
                    c.Log($"[{self.TopCard.Name}] 패 1장을 파기하고 액티브가 됩니다.");
                    return;
                }
                c.PreventUnsuspend62 = true;
                c.Log($"[{self.TopCard.Name}]은(는) 액티브가 되지 않습니다{(c.Owner.Hand.Count == 0 ? " (패가 없다)" : "")}.");
            }, when);
            return true;
        }
        // BT4-101이 주는 효과 "[Your Turn] When attacking an opponent's Digimon with no digivolution cards, delete that Digimon." — 그 디지몬 = 이 디지몬의 어택 대상
        //  (배틀이 아니라 효과로 소멸 — 공식 Q&A BT4-101 Q1)
        if ((m = AttackingDeleteRe62.Match(t)).Success)
        {
            if (inherited || card.Type != CardType.Digimon) return false;
            if (TargetSpec.Parse("all of your opponent's " + m.Groups["t"].Value) is not { Opponent: true, IsThis: false, IsThat: false, Selector: null } spec || spec.ContextFilters.Count > 0) return false;
            if (def == null) return true;
            string q = m.Groups["q"].Value.ToLowerInvariant();
            Func<EffectContext, bool> turn = q == "[your turn]" ? c => c.Engine.IsTurnOf(c.Owner) : q == "[opponent's turn]" ? c => !c.Engine.IsTurnOf(c.Owner) : _ => true;
            def.On(EffectTrigger.WhenAttacking, c =>
            {
                if (c.Other is not { } d || !c.Enemy.BattleArea.Contains(d) || c.Engine.IsUnaffectedBy(c.Owner, d)) { c.LastStepResolved = false; return; }
                c.LastTargets = new List<CardInstance> { d };
                bool gone = c.DeleteEnemy(d);
                c.Log($"[어택 시] {c.Card.Name}: 어택 대상 [{d.TopCard.Name}] {(gone ? "소멸" : "소멸하지 않았습니다")}");
            }, c => turn(c) && c.Other is { } o && spec.Filters.All(x => x(o)));
            return true;
        }
        // EX2-037 "[Opponent's Turn] [Once Per Turn] When an opponent's Digimon becomes unsuspended, ＜De-Digivolve 1＞ 1 of those Digimon."
        //  — 액티브가 된 그 상대의 디지몬 (액티브 페이즈도 — 공식 Q&A EX2-037 Q1)
        if ((m = UnsuspendedDeDigivolveRe62.Match(t)).Success)
        {
            if (inherited || card.Type != CardType.Digimon) return false;
            if (def == null) return true;
            int n = int.Parse(m.Groups["n"].Value);
            def.On(EffectTrigger.OpponentDigimonUnsuspended, c =>
            {
                if (c.Other is not { } d || !c.Enemy.BattleArea.Contains(d) || c.Engine.IsUnaffectedBy(c.Owner, d)) { c.LastStepResolved = false; return; }
                c.LastTargets = new List<CardInstance> { d };
                string name = d.TopCard.Name;
                int k = c.Engine.DeDigivolve(c.Enemy, d, n);
                c.Log($"[상대의 턴] {c.Card.Name}: 액티브가 된 [{name}] ≪퇴화 {n}≫ ({k}장)");
            }, c => !c.Engine.IsTurnOf(c.Owner) && c.Other != null, oncePerTurn: true);
            return true;
        }
        // EX6-031 "[Your Turn] Change ＜Security A. -＞ to ＜Security A. +＞ on all of your Digimon." — 자신의 디지몬 전부 (이 디지몬 자신도)
        if ((m = SaFlipRe62.Match(t)).Success)
        {
            if (inherited || card.Type != CardType.Digimon) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            def?.Static(StaticStat.AllySaFlip62, EffectSource.Main, c => q(c) ? 1 : 0);
            return true;
        }
        // BT8-059 "[All Turns] Players can't ignore digivolution requirements." — 서로 (공식 Q&A BT8-059 Q1)
        if ((m = NoIgnoreReqRe62.Match(t)).Success)
        {
            if (card.Type != CardType.Digimon) return false;
            var q = Qualifier58(m.Groups["q"].Value);
            def?.Static(StaticStat.Keyword, source, c => q(c) ? 1 : 0, Keyword.NoIgnoreEvoRequirements62);
            return true;
        }
        if (card.Type != CardType.Option || inherited) return false;
        // BT4-101 "[Main] All of your Digimon gain "[Your Turn] When attacking ~, delete that Digimon." for the turn." ·
        // BT9-102 "[Main] You may trash 1 card with the [Cyborg] or [Machine] trait from your hand to have all of your level 6 Digimon with the [Machine] trait gain ＜Rush＞ (~) and "[On Play] ~" for the turn."
        //  — 이 턴 동안 조건에 맞는 자신의 디지몬 전부 (나중에 등장 · 진화한 디지몬도 — 공식 Q&A BT9-102 Q1)
        if ((m = SideGrantRe62.Match(t)).Success)
        {
            if (SideGrantTarget62(m.Groups["t"].Value) is not { } spec) return false;
            var kws = new List<Keyword>();
            if (m.Groups["kws"].Success)
            {
                if (ParseKeywordList(m.Groups["kws"].Value) is not { } list || list.Any(k => k.Keyword == null)) return false;
                kws.AddRange(list.Select(k => k.Keyword!.Value));
            }
            if (GrantedCard62(card, m.Groups["g"].Value) is not { } granted) return false;
            Func<CardData, bool>? costMatch = null;
            if (m.Groups["cost"].Success)
            {
                if (ParseCardPick("1 " + m.Groups["cost"].Value) is not { } pick) return false;
                costMatch = pick.Match;
            }
            def?.Main(TargetKind.None, c =>
            {
                if (costMatch != null)
                {
                    var pool = c.Owner.Hand.Where(costMatch).OrderBy(x => x.Type == CardType.Digimon && spec.Filters.Count > 0 ? 1 : 0).ThenBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
                    if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, "메인", "패에 파기할 카드가 없어 효과를 발휘하지 않습니다"); return; }
                    if (!c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 패의 카드 1장을 파기하여 효과를 발휘할까요?", true))
                    { c.LastStepResolved = false; Emit(c, "메인", "효과를 발휘하지 않았습니다"); return; }
                    var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] 파기할 패 1장을 고르세요.");
                    if (picked.Count == 0 || !c.Owner.Hand.Remove(picked[0])) { c.LastStepResolved = false; return; }
                    c.Owner.Trash.Add(picked[0]);
                    c.Engine.NotifyHandTrashed(c.Owner, picked);
                }
                c.Engine.AddSideAura62(c.Owner, d => spec.Filters.All(f => f(d)), kws, granted, Until(c, Duration.ThisTurn));
                c.RecalculateFieldBuffs();
                Emit(c, "메인", $"이 턴 동안 조건에 맞는 자신의 디지몬 전부가 {(kws.Count > 0 ? string.Join(" · ", kws) + "와 " : "")}효과 「{granted.EffectText}」를 얻는다");
            });
            return true;
        }
        // BT13-108 "[Main] Until the end of your opponent's turn, 1 of your Digimon gains "~" and "[Opponent's Turn] This Digimon isn't affected by your opponent's Option cards.""
        if ((m = TwoGrantsRe62.Match(t)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null || target.Opponent || target.AnySide || Regex.IsMatch(m.Groups["t"].Value, @"\bTamers?\b", I)) return false;
            if (GrantedCard62(card, m.Groups["a"].Value) is not { } a || GrantedCard62(card, m.Groups["b"].Value) is not { } b) return false;
            var dur = m.Groups["d"].Value.StartsWith("for", StringComparison.OrdinalIgnoreCase) ? Duration.ThisTurn : Duration.OpponentsTurnEnd;
            def?.Main(TargetKind.None, c =>
            {
                int until = Until(c, dur);
                var list = Pick(target, c, security: false);
                foreach (var d in list)
                    foreach (var g in new[] { a, b })
                    {
                        d.GrantedEffects.Add((g, until, c.Owner));
                        c.Engine.NoteGrantedCat61(d, g);
                    }
                c.LastTargets = list;
                c.RecalculateFieldBuffs();
                if (list.Count == 0) c.LastStepResolved = false;
                Emit(c, "메인", list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}]에게 효과 「{a.EffectText}」 · 「{b.EffectText}」를 준다" : "대상이 없습니다");
            });
            return true;
        }
        return false;
    }

    /// <summary>
    /// 따옴표 안의 효과 문장 → 그 효과만 가진 카드 (GrantedCard32와 같고, 키워드 지속 효과도 된다 — "[Opponent's Turn] This Digimon isn't affected by ~" BT13-108).
    /// 엔진은 효과로 받은 키워드 지속 효과를 받은 디지몬의 것으로 계산한다 (GoldfishEngine.Batch62)
    /// </summary>
    private static CardData? GrantedCard62(CardData card, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        var synth = new CardData
        {
            CardId = $"GRANT:{card.CardId}:{text}", Name = card.Name, Type = CardType.Digimon, Color = card.Color, Level = 3, EffectText = text,
        };
        var info = KeywordDictionary.Analyze(synth);
        if (info.Count == 0 || info.Any(x => x.Support != ClauseSupport.Supported)) return null;
        if (CardScripts.For(synth) is not { } script || script.Effects.Count + script.Statics.Count == 0 || script.Statics.Any(s => s.Stat != StaticStat.Keyword || s.Keyword == null)
            || script.Keywords.Count > 0 || script.EvoCostModifiers.Count > 0 || script.PlayCostOptions.Count > 0 || script.FieldPlayCostModifiers.Count > 0)
            return null;
        return synth;
    }
}
