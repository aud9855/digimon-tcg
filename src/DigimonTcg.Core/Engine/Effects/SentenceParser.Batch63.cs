using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.63.0 미지원 문장 37차 묶음 (파서 쪽 공통):
///  - 기간 동안의 전체 처리 (종합 룰 15-10-2-4-1 · 15-11-2-2 · 15-11-2-3): 대상을 고르지 않는 "All of ~ / Your opponent's Digimon ~"의
///    DP · 키워드 · ≪시큐리티 어택≫ · 효과 문장 · "~ 할 수 없다"는 기간 동안 나중에 나온 디지몬 · 나중에 조건을 만족한 디지몬도 받고,
///    조건을 잃은 디지몬은 대상이 아니게 된다 (공식 Q&amp;A ST1-13 Q1 · BT3-105 Q1 · BT9-102 Q1 · BT12-106 Q1 · BT1-100 Q1 · BT18-028 Q1).
///    읽기(지원 여부 · 한글)는 바뀌지 않는다 — 처리(엔진)만. 엔진은 GoldfishEngine.Batch63
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>기간 동안의 전체 처리를 가진 카드 (보고용 — 읽을 때 채운다)</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> WholeCards63 = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>대상을 고르지 않는 처리인지 (전부 · 수를 말하지 않은 "your opponent's Digimon") — 고르기 기준(가장 DP가 낮은 등)이 있으면 아니다</summary>
    internal static bool IsWhole63(TargetSpec t) => t.Count == 0 && !t.IsThis && !t.IsThat && t.Selector == null;

    /// <summary>파서가 읽을 때: 전체 처리면 표에 적는다</summary>
    private static bool NoteWhole63(TargetSpec t, CardData card)
    {
        if (!IsWhole63(t)) return false;
        WholeCards63[card.CardId] = true;
        return true;
    }

    private static PlayerZones[] Sides63(EffectContext c, TargetSpec t) =>
        t.AnySide ? new[] { c.Owner, c.Enemy } : new[] { t.Opponent ? c.Enemy : c.Owner };

    /// <summary>
    /// 조건을 잃으면 되돌릴지 — 대상 조건이 DP를 보거나 이 효과의 디지몬과 비교하면 처리 자체로 조건이 바뀔 수 있어 처음 판단 그대로 (나중에 나온 디지몬은 받는다)
    /// </summary>
    private static bool Dynamic63(TargetSpec t) => (t.ContextFilters.Count == 0 || t.Dynamic80) &&   // v0.80.0 Dynamic80 (BT23-024 가장 등장 코스트가 높은 디지몬 이외)
        !Regex.IsMatch(t.Text, @"\bDP\b|\bas (?:much|many|high)\b", RegexOptions.IgnoreCase);

    /// <summary>처리 전: 전체 처리면 대상 쪽 상태를 적어 둔다 (아니면 null)</summary>
    private static Dictionary<CardInstance, GoldfishEngine.Before63>? WholeBegin63(EffectContext c, TargetSpec t, Duration dur) =>
        dur != Duration.ThisAttack && dur != Duration.None && IsWhole63(t) ? c.Engine.WholeBegin63(Sides63(c, t)) : null;

    /// <summary>처리 뒤: 기간 동안 나중의 대상에게도 one을 하도록 기록 (WholeBegin63이 null이면 아무것도 하지 않는다)</summary>
    private static void WholeEnd63(EffectContext c, TargetSpec t, Dictionary<CardInstance, GoldfishEngine.Before63>? before, IEnumerable<CardInstance> first, int until, Action<CardInstance> one, string tag = "")
    {
        if (before == null) return;
        var filters = t.Filters.ToList();
        var ctxFilters = t.ContextFilters.ToList();
        c.Engine.WholeEnd63(c, Sides63(c, t), d => filters.All(f => f(d)) && ctxFilters.All(f => f(c, d)), before, first, until, one, Dynamic63(t), tag);
    }

    // =====================================================================
    // 37차 묶음 — 원래 규칙(36차까지)이 읽지 못한 절만
    // =====================================================================

    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth63;

    private static string? TryParse63(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse62(rawClause, inherited, card, def);
        if (err == null || _depth63 > 0) return err;
        string t = Normalize58(rawClause);
        if (ClauseSpecialRe63.IsMatch(t)) return ClauseSpecial63(t, inherited, card, def) ? null : err;
        string alt = ClauseRewrite63(t);
        if (alt == t) return err;
        _depth63++;
        try
        {
            if (TryParse62(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse62(alt, inherited, card, def);
            return null;
        }
        finally { _depth63--; }
    }

    /// <summary>같은 뜻 다른 표기 · 이 묶음의 동작 표시 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules63 =
    {
        // BT17-101 "Then, if this Digimon has a Tamer in its digivolution cards, ~" — 진화원에 테이머 카드가 있다면
        s => Regex.Replace(s, @"\bif this Digimon has a Tamer in its digivolution cards,", "if this Digimon has a Tamer card in its digivolution cards,", RegexOptions.IgnoreCase),
        // BT17-101 "you may set the memory to 3 on the opponent's side" — 메모리를 상대 쪽의 3으로 할 수 있다 (할지는 효과 주인 — 추천: 지금 상대 쪽 메모리가 더 많을 때)
        s => Regex.Replace(s, @"\byou may set the memory to (?<n>\d+) on (?:the|your) opponent's side\b", "⟦oppmem63:${n}⟧", RegexOptions.IgnoreCase),
        // BT4-075 "Your opponent may choose 1 of their unsuspended Digimon. If they do, switch the target of attack to that Digimon." — 고르는 것은 상대 (공식 Q&A BT4-075 Q1)
        s => Regex.Replace(s, @"\bYour opponent may choose 1 of their unsuspended Digimon\. If they do, switch the target of (?:the )?attack to that Digimon\.?", "⟦oppswitch63⟧.", RegexOptions.IgnoreCase),
        // BT15-078 "Then, you may switch the target of attack to that Digimon." — 그 디지몬 = 이 효과로 상대가 등장시킨 디지몬 (등장하지 않았으면 그대로 — 공식 Q&A BT15-078 Q1)
        s => Regex.Replace(s, @"\bThen, you may switch the target of (?:the )?attack to that Digimon\.?$", "Then, ⟦switchthat63⟧.", RegexOptions.IgnoreCase),
        // BT16-092 "the Digimon this effect DNA digivolved" — 이 효과로 진화한 디지몬 (조그레스 진화도 진화)
        s => Regex.Replace(s, @"\bthe Digimon this effect DNA digivolved\b", "the Digimon this effect digivolved", RegexOptions.IgnoreCase),
    };

    internal static string ClauseRewrite63(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules63) s = f(s);
        return s;
    }

    // BT5-017 진화원 "[Your Turn] When this Digimon attacks with ＜Blitz＞, it can also attack your opponent's unsuspended Digimon."
    internal static readonly Regex BlitzUnsuspendedRe63 = new(@"^\[Your Turn\]\s*When this Digimon attacks with ＜Blitz＞, it can also attack your opponent's unsuspended Digimon\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClauseSpecialRe63 = new($"{BlitzUnsuspendedRe63}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool ClauseSpecial63(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        // BT5-017 — ≪진격≫으로 어택하는 동안 (어택 대상을 고를 때) 액티브 상태인 상대의 디지몬에게도 어택할 수 있다
        if (BlitzUnsuspendedRe63.IsMatch(t))
        {
            if (card.Type != CardType.Digimon) return false;
            def?.Static(StaticStat.Keyword, inherited ? EffectSource.Inherited : EffectSource.Main,
                c => c.OwnersTurn && c.Digimon is { } d && c.Engine is { } e && e.IsBlitzAttacker63(d) ? 1 : 0, Keyword.CanAttackUnsuspended);
            return true;
        }
        return false;
    }

    /// <summary>이 묶음의 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep에서 앞의 묶음 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction63(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim(), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        Match m;
        // BT17-101 "you may set the memory to 3 on the opponent's side"
        if ((m = Regex.Match(s, @"^⟦oppmem63:(?<n>\d+)⟧$")).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                bool rec = c.OwnerMemory < -n;   // 지금 상대 쪽 메모리가 n보다 많을 때만 이득
                if (!c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 메모리를 상대 쪽의 {n}으로 할까요? (지금 {(c.OwnerMemory >= 0 ? $"자신 쪽 {c.OwnerMemory}" : $"상대 쪽 {-c.OwnerMemory}")})", rec))
                { c.LastStepResolved = false; return; }
                c.AddMemory(-n - c.OwnerMemory);
                Diag46.Hit("oppMemory63");
                Emit(c, label, $"메모리를 상대 쪽의 {n}으로 했습니다");
            }, null);
        }
        // BT15-078 "Then, you may switch the target of attack to that Digimon." — 이 효과로 상대가 등장시킨 디지몬
        if (s == "⟦switchthat63⟧")
            return (c =>
            {
                var d = c.PlayedThisEffect.LastOrDefault(x => c.Enemy.BattleArea.Contains(x))
                        ?? c.LastTargets.LastOrDefault(x => c.Enemy.BattleArea.Contains(x));
                var atk = c.Engine.CurrentAttacker;
                if (d == null || atk == null || !c.Owner.BattleArea.Contains(atk) || ReferenceEquals(c.Engine.CurrentAttackTarget63, d)) { c.LastStepResolved = false; return; }
                bool rec = OwnSwitchGood63(c, atk, c.Engine.CurrentAttackTarget63, d);
                if (!c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 어택 대상을 [{d.TopCard.Name}]으로 바꿀까요?", rec)) { c.LastStepResolved = false; return; }
                if (c.Engine.RedirectAttack(c.Enemy, d)) { Diag46.Hit("switchThat63"); Emit(c, label, $"어택 대상을 [{d.TopCard.Name}]으로 바꿨습니다"); }
                else c.LastStepResolved = false;
            }, null);
        // BT4-075 상대는 액티브 상태인 자신의 디지몬 1마리를 고를 수 있다 — 고르면 어택 대상을 그 디지몬으로 (공식 Q&A BT4-075 Q1: 원래 대상이 무엇이든)
        if (s == "⟦oppswitch63⟧")
            return (c =>
            {
                var atk = c.Engine.CurrentAttacker;
                if (atk == null || !c.Owner.BattleArea.Contains(atk)) { c.LastStepResolved = false; return; }
                var pool = c.Enemy.BattleArea.Where(d => !d.IsSuspended && d.TamerProxyOf == null && !ReferenceEquals(d, c.Engine.CurrentAttackTarget63)).OrderByDescending(d => d.GetCurrentDP()).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; return; }
                // 상대의 추천: 어택한 디지몬보다 DP가 높은 디지몬이 있으면 그 디지몬으로 (배틀에서 이긴다)
                var rec = pool[0].GetCurrentDP() > atk.GetCurrentDP() ? pool[0] : null;
                var d = c.Engine.AskDigimon(c.Enemy, $"[{c.Card.Name}] 어택 대상으로 할 액티브 상태인 자신의 디지몬 1마리를 고를 수 있습니다 (고르지 않으면 그대로).", pool, rec, optional: true);
                if (d == null) { c.LastStepResolved = false; Emit(c, label, "상대는 디지몬을 고르지 않았습니다"); return; }
                if (c.Engine.RedirectAttack(c.Enemy, d)) { Diag46.Hit("oppSwitch63"); Emit(c, label, $"상대가 고른 [{d.TopCard.Name}]으로 어택 대상을 바꿨습니다"); }
                else c.LastStepResolved = false;
            }, null);
        // EX5-054 "switch the target of attack to this Digimon or the player" — 상대의 어택 (공식 Q&A EX5-054 Q1: 바꾸지 않을 수도 있다)
        if (Regex.IsMatch(s, @"^(?:you may )?(?:Switch|Change) the (?:target of (?:the )?attack|attack target) to this Digimon or the player$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var atk = c.Engine.CurrentAttacker;
                if (atk == null || !c.Enemy.BattleArea.Contains(atk)) { c.LastStepResolved = false; return; }
                var cur = c.Engine.CurrentAttackTarget63;
                var self = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
                var opts = new List<string>(); var acts = new List<int>();
                if (self != null && !ReferenceEquals(cur, self)) { opts.Add($"이 디지몬 [{self.TopCard.Name}]으로"); acts.Add(1); }
                if (cur != null) { opts.Add("플레이어로"); acts.Add(2); }
                opts.Add("바꾸지 않는다"); acts.Add(0);
                // 추천: 이 디지몬이 배틀에서 이기면 이 디지몬으로, 지키는 디지몬이 지고 시큐리티가 3장 이상이면 플레이어로
                int recAct = self != null && !ReferenceEquals(cur, self) && self.GetCurrentDP() > atk.GetCurrentDP() ? 1
                    : cur != null && cur.GetCurrentDP() <= atk.GetCurrentDP() && c.Owner.SecurityStack.Count >= 3 ? 2 : 0;
                int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 어택 대상을 바꿀까요?", opts, Math.Max(0, acts.IndexOf(recAct)));
                int act = acts[Math.Clamp(pick, 0, acts.Count - 1)];
                bool ok = act == 1 ? c.Engine.RedirectAttack(c.Owner, self!) : act == 2 && c.Engine.RedirectAttackToPlayer63(c.Owner);
                if (ok) { Diag46.Hit("switchSelfOrPlayer63"); Emit(c, label, act == 1 ? $"어택 대상을 [{self!.TopCard.Name}]으로 바꿨습니다" : "어택 대상을 플레이어로 바꿨습니다"); }
                else c.LastStepResolved = false;
            }, null);
        // ST18-14 "change the attack target to another of your opponent's Digimon or the player" — 자신의 디지몬의 어택 (공식 Q&A ST18-14 Q1: 액티브 · 어택당하지 않는 디지몬으로도)
        if (Regex.IsMatch(s, @"^(?:you may )?(?:Switch|Change) the (?:target of (?:the )?attack|attack target) to another of your opponent's Digimon or the player$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var atk = c.Engine.CurrentAttacker;
                if (atk == null || !c.Owner.BattleArea.Contains(atk)) { c.LastStepResolved = false; return; }
                var cur = c.Engine.CurrentAttackTarget63;
                var pool = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !ReferenceEquals(d, cur)).OrderBy(d => d.GetCurrentDP() < atk.GetCurrentDP() ? 0 : 1).ThenByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP()).ToList();
                var best = pool.FirstOrDefault(d => OwnSwitchGood63(c, atk, cur, d));
                bool toPlayer = best == null && cur != null && OwnSwitchGood63(c, atk, cur, null);
                var d = c.Engine.AskDigimon(c.Owner, $"[{c.Card.Name}] 어택 대상으로 할 다른 상대의 디지몬을 고르세요 (고르지 않으면 플레이어로 바꿀지 묻습니다).", pool, best, optional: true);
                bool ok;
                if (d != null) ok = c.Engine.RedirectAttack(c.Enemy, d);
                else ok = cur != null && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 어택 대상을 플레이어로 바꿀까요?", toPlayer) && c.Engine.RedirectAttackToPlayer63(c.Enemy);
                if (ok) { Diag46.Hit("switchOtherOrPlayer63"); Emit(c, label, d != null ? $"어택 대상을 [{d.TopCard.Name}]으로 바꿨습니다" : "어택 대상을 플레이어로 바꿨습니다"); }
                else c.LastStepResolved = false;
            }, null);
        return null;
    }

    /// <summary>
    /// 자신의 어택 대상을 cand(null = 플레이어)로 바꾸는 것이 나은지 (AI 추천): 디지몬이면 배틀에서 이기고 — 지금 대상에게 지거나, 지금 대상이 플레이어인데 상대 시큐리티가 3장 이상일 때.
    /// 플레이어면 지금 대상에게 지고 시큐리티를 체크할 수 있을 때
    /// </summary>
    private static bool OwnSwitchGood63(EffectContext c, CardInstance atk, CardInstance? cur, CardInstance? cand)
    {
        int a = atk.GetCurrentDP();
        bool losesNow = cur != null && cur.GetCurrentDP() >= a;
        if (cand == null) return losesNow && atk.GetSecurityAttackCount() >= 1;
        if (cand.GetCurrentDP() >= a) return false;
        return cur == null ? c.Enemy.SecurityStack.Count >= 3 : losesNow || cand.TopCard.Level > cur.TopCard.Level;
    }

    // =====================================================================
    // 고르는 효과 — "~. Then, activate 1 of the effects below:" (BT26-101)
    // =====================================================================

    internal static readonly Regex ThenChooseRe63 = new(@"^(?<pre>.+?)\. Then, activate (?:1|one) of the effects below[:.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? TryParseChoice63(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoice58(rawHeader, rawOptions, inherited, card, def);
        if (err == null) return err;
        string header = Normalize(rawHeader);
        var prefix = Regex.Match(header, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        var m = ThenChooseRe63.Match(prefix.Groups[2].Value.Trim());
        if (!m.Success) return err;
        var labels = Regex.Matches(prefix.Groups[1].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim()).ToList();
        var triggers = new List<EffectTrigger>();
        bool oncePerTurn = false;
        foreach (var lab in labels)
        {
            if (EventTriggers.TryGetValue(lab, out var tr)) triggers.Add(tr);
            else if (lab.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase)) oncePerTurn = true;
            else return err;
        }
        if (triggers.Count == 0 || !MapMainTrigger(triggers, card, inherited)) return err;
        // 앞 문장은 같은 시점의 절 하나로 읽는다 (조건이 있으면 그 문장에만 — "그 후"의 고르는 효과는 따로)
        string preClause = prefix.Groups[1].Value.Trim() + " " + m.Groups["pre"].Value.Trim() + ".";
        if (TryParse(preClause, inherited, card, null) != null) return err;
        // 선택지도 같은 시점의 절 하나로 읽는다
        var optionTexts = rawOptions.Select(o => Normalize(o).TrimStart('・', '•', '·').Trim()).ToArray();
        var optionClauses = optionTexts.Select(o => prefix.Groups[1].Value.Trim() + " " + o).ToArray();
        if (optionClauses.Length < 2 || optionClauses.Any(o => TryParse(o, inherited, card, null) != null)) return err;
        var values = optionTexts.Select(t => SplitSentences(t).Sum(AiEvaluator.EstimateSentenceValue)).ToArray();
        var recommend58 = Recommender58(optionTexts, values);
        if (def == null) return null;
        var tmp = new CardScriptDefinition(new CardScript(card.CardId));
        if (TryParse(preClause, inherited, card, tmp) != null || tmp.Script41.Effects.Count != triggers.Count) return err;
        var pres = tmp.Script41.Effects.ToList();
        var options = new List<List<CardEffect>>();
        foreach (var o in optionClauses)
        {
            var od = new CardScriptDefinition(new CardScript(card.CardId));
            if (TryParse(o, inherited, card, od) != null || od.Script41.Effects.Count != triggers.Count) return err;
            options.Add(od.Script41.Effects.ToList());
        }
        Action<EffectContext> resolve = c =>
        {
            (pres.FirstOrDefault(e => e.Trigger == c.Trigger) ?? pres[0]).Resolve(c);
            if (c.Engine.State.IsGameOver) return;
            int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 발휘할 효과를 1개 고르세요.", optionTexts, recommend58(c));
            var opt = options[Math.Clamp(pick, 0, options.Count - 1)];
            (opt.FirstOrDefault(e => e.Trigger == c.Trigger) ?? opt[0]).Resolve(c);
            Diag46.Hit("thenChoose63");
        };
        foreach (var t in triggers)
        {
            if (t == EffectTrigger.Main) def.Main(TargetKind.None, resolve);
            else if (inherited) def.Inherited(t, resolve, null, oncePerTurn, false);
            else def.On(t, resolve, null, oncePerTurn, false);
        }
        return null;
    }
}
