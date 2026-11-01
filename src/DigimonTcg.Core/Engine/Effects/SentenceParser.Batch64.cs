using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.64.0 미지원 문장 38차 묶음 (파서 쪽):
///  - 턴 종료 조건 (종합 룰 6-1-4-1 · 6-6-1): "[Your Turn] Your turn end condition is the opponent having 3 or more memory." (BT14-081) ·
///    진화원 "While this Digimon has [Fenriloogamon] in its name, the turn end condition is ~" (BT17-069) — 메모리가 상대 쪽의 3 이상이 되지 않으면
///    턴 종료 시가 되지 않는다 (공식 Q&amp;A BT14-081 Q1). 엔진은 GoldfishEngine.Batch64
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth64;

    private static string? TryParse64(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse63(rawClause, inherited, card, def);
        if (err == null || _depth64 > 0) return err;
        string t = Normalize58(rawClause);
        if (ClauseSpecialRe64.IsMatch(t)) return ClauseSpecial64(t, inherited, card, def) ? null : err;
        string alt = ClauseRewrite64(t);
        if (alt == t) return err;
        _depth64++;
        try
        {
            if (TryParse63(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse63(alt, inherited, card, def);
            return null;
        }
        finally { _depth64--; }
    }

    internal static readonly Regex PlayCountUpRe64 = new(@"\bYou may play 1 (?<p>[^.]+?) card (?<rest>with [^.]+? from your trash without paying (?:its|the) cost)\. If (?<x>\[[^\]]+\]) is in this Digimon's digivolution cards, increase the number of Digimon you can play with this effect by (?<k>\d+)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string Reminder64 = @"(?:\s*\([^()]*\))?";
    internal static readonly Regex SuchKeysDpRe64 = new($@"(?<pre>\bBy trashing any 1 (?<tr>\[[^\]]+\](?: or \[[^\]]+\])*) trait card from your Digimon's digivolution cards, )1 of your such Digimon gains (?<k1>＜[^＞]+＞){Reminder64}, (?<k2>＜[^＞]+＞){Reminder64} and \+(?<dp>\d+) DP until your opponent's turn ends\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex KeysReturnDpRe64 = new($@"(?<pre>\bBy trashing [^,]+ of 1 of your Digimon[^,]*, )until the end of your opponent's turn, that Digimon gains (?<k1>＜[^＞]+＞){Reminder64}, (?<k2>＜[^＞]+＞){Reminder64}, (?<k3>＜[^＞]+＞){Reminder64},? and your opponent's effects can't return it to hands or decks, and it gets (?<dp>[+-]\d+ DP)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 · 이 묶음의 동작 표시 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules64 =
    {
        // BT14-081 "You may play 1 ~ card ~ from your trash without paying its cost. If [Eiji Nagasumi] is in this Digimon's digivolution cards, increase the number of Digimon you can play with this effect by 2."
        //  — 진화원에 그 카드가 있으면 최대 1+2장을 함께 등장 (매수 상한 +2), 없으면 1장
        s => PlayCountUpRe64.Replace(s, m =>
        {
            int k = 1 + int.Parse(m.Groups["k"].Value);
            string x = m.Groups["x"].Value, p = m.Groups["p"].Value, rest = m.Groups["rest"].Value;
            return $"If a {x} card isn't in this Digimon's digivolution cards, you may play 1 {p} card {rest}. If {x} is in this Digimon's digivolution cards, you may play up to {k} {p} cards {rest}.";
        }),
        // EX8-070 "By trashing ~ of 1 of your Digimon ~, until the end of your opponent's turn, that Digimon gains ＜A＞, ＜B＞, ＜C＞, and your opponent's effects can't return it to hands or decks, and it gets +3000 DP."
        //  — 그 디지몬 = 비용으로 진화원을 버린 디지몬. 키워드 · DP와 "패 · 덱으로 되돌릴 수 없다"를 두 문장으로 (같은 기간)
        // BT19-078 진화원 "If you played, you may change the attack target to the Digimon played by this effect." — 상대의 어택 대상을 이 효과로 등장한 자신의 디지몬으로 (바꾸지 않을 수도 있다 — 공식 Q&A BT19-078 Q1)
        s => Regex.Replace(s, @"\bIf you played, you may (?:change|switch) the (?:attack target|target of (?:the )?attack) to the Digimon played by this effect\.?$", "Then, ⟦switchplayed64⟧.", RegexOptions.IgnoreCase),
        // EX12-047 "Then, by returning 2 cards from their trash to the bottom of the deck, for the turn, this Digimon gets +6000 DP and, to 1 of their Digimon, give -5000 DP for each of those returned cards' colors."
        //  — 되돌린 카드의 색 1색마다 (되돌린 카드들이 가진 색의 수)
        s => Regex.Replace(s, @"\bThen, by returning (?<n>\d+) cards from (?:their|your opponent's) trash to the bottom of the deck, for the turn, this Digimon gets \+(?<a>\d+) DP and, to 1 of their Digimon, give -(?<b>\d+) DP for each of those returned cards' colou?rs\.?$",
            "Then, by returning ${n} cards from their trash to the bottom of the deck, ⟦retcolor64:${a}:${b}⟧.", RegexOptions.IgnoreCase),
        // EX10-032 "By trashing any 1 [Mineral] or [Rock] trait card from your Digimon's digivolution cards, 1 of your such Digimon gains ＜A＞, ＜B＞ and +3000 DP until your opponent's turn ends."
        //  — "such" = 비용과 같은 특징을 가진 자신의 디지몬
        s => SuchKeysDpRe64.Replace(s, m =>
            $"{m.Groups["pre"].Value}1 of your {m.Groups["tr"].Value} trait Digimon gains {m.Groups["k1"].Value} and {m.Groups["k2"].Value} and gets +{m.Groups["dp"].Value} DP until your opponent's turn ends."),
        // EX10-033 "~, to 1 of your opponent's Digimon, reduce the play cost by 2 until their turn ends for each card trashed." — 한 마리에 (버린 장 수 × 2)
        s => Regex.Replace(s, @", to 1 of your opponent's Digimon, reduce the play cost by (?<n>\d+) until their turn ends for each card trashed\.?$", ", ⟦oppcostdown64:${n}⟧.", RegexOptions.IgnoreCase),
        // BT7-064 "You may place 1 ~ from your hand at the bottom of this Digimon's digivolution cards to prevent effects from deleting it or reducing its DP until ~" — 놓는 것으로 (비용), 효과로 소멸하지 않고 DP가 마이너스되지 않는다
        s => Regex.Replace(s, @"\bYou may place (?<p>1 [^.]+?) from your hand at the bottom of this Digimon's digivolution cards to prevent effects from deleting it or reducing its DP (?<dur>until [^.]+?)\.?$",
            "By placing ${p} from your hand as this Digimon's bottom digivolution card, this Digimon can't be deleted by effects and can't have its DP reduced ${dur}.", RegexOptions.IgnoreCase),
        // BT4-098 "1 of your ~ Digimon gets +3000 DP, ＜Security A. +1＞ and "[Your Turn] ~ gain +3 memory." for the turn." — 그 디지몬이 효과 문장도 얻는다
        s => Regex.Replace(s, $@"\b(?<t>1 of your [^,]+?) gets \+(?<dp>\d+) DP, (?<k>＜Security A\. \+\d+＞){Reminder64} and ""(?<g>[^""]+)"" for the turn\.?$",
            m => $"{m.Groups["t"].Value} gets +{m.Groups["dp"].Value} DP and gains {m.Groups["k"].Value} for the turn. That Digimon gains \"{Regex.Replace(m.Groups["g"].Value, @"\bgain \+(\d+) memory", "gain $1 memory", RegexOptions.IgnoreCase)}\" for the turn.", RegexOptions.IgnoreCase),
        s => KeysReturnDpRe64.Replace(s, m =>
            $"{m.Groups["pre"].Value}until the end of your opponent's turn, that Digimon gains {m.Groups["k1"].Value}, {m.Groups["k2"].Value} and {m.Groups["k3"].Value} and gets {m.Groups["dp"].Value}. Until the end of your opponent's turn, your opponent's effects can't return that Digimon to hands or decks."),
    };

    internal static string ClauseRewrite64(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules64) s = f(s);
        return s;
    }

    // BT14-081 "[Your Turn] Your turn end condition is the opponent having 3 or more memory." ·
    // BT17-069 진화원 "[Your Turn] While this Digimon has [Fenriloogamon] in its name, the turn end condition is the opponent having 3 or more memory."
    internal static readonly Regex TurnEndRe64 = new(@"^(?:Inherited Effect\s*)?\[Your Turn\]\s*(?:While (?<cond>[^,]+), )?(?:your|the) turn end condition is (?:the|your) opponent having (?<n>\d+) or more memory\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClauseSpecialRe64 = new($"{TurnEndRe64}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool ClauseSpecial64(string t, bool inherited, CardData card, CardScriptDefinition? def)
    {
        Match m;
        // 턴 종료 조건 — 이 효과가 발휘하고 있는 동안은 메모리가 상대 쪽의 N 이상이 되지 않으면 턴 종료 시가 되지 않는다 (공식 Q&A BT14-081 Q1)
        if ((m = TurnEndRe64.Match(t)).Success)
        {
            if (card.Type != CardType.Digimon) return false;
            int n = int.Parse(m.Groups["n"].Value);
            Func<StaticContext, bool> cond = _ => true;
            if (m.Groups["cond"].Success)
            {
                if (ParseStaticCondition(m.Groups["cond"].Value) is not { } sc) return false;
                cond = sc;
            }
            def?.Static(StaticStat.TurnEndMemory64, inherited ? EffectSource.Inherited : EffectSource.Main, c => c.OwnersTurn && cond(c) ? n : 0);
            return true;
        }
        return false;
    }

    // =====================================================================
    // 수를 센 고르는 효과 — 종합 룰 15-15-7-2: 발휘할 효과 1개를 고르고 발휘한 뒤 다음 1개를 고른다. 지정된 횟수만큼 반복
    //  - EX13-077 "~. Then, for every 2 of your Digimon and Tamers' colors, activate 1 effect below:" (같은 효과를 다시 골라도 된다)
    //  - EX4-073 "By trashing up to 3 ~, for each card trashed, activate the effect below. Then, if you trashed 3 cards, ~" (선택지 1개 — 버린 장 수만큼)
    // =====================================================================

    internal static readonly Regex ForEveryChooseRe64 = new(@"^(?<pre>.+?)\. Then, for (?:every|each) (?<cnt>.+?), activate (?:1|one) (?:effect|of the effects) below[:.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex ForEachTrashedBelowRe64 = new(@"^(?<cost>By trashing [^,]+), for each (?:card|one) trashed, activate the effect below\.(?:\s+(?<post>Then, .+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"2 of your Digimon and Tamers' colors" → 세는 식 (원래 표기가 안 되면 같은 뜻 다른 표기로)</summary>
    internal static Func<PlayerZones, PlayerZones, CardInstance?, int>? ChoiceCounter64(string cnt)
    {
        var m = Regex.Match(cnt.Trim(), @"^(?:(?<n>\d+) of )?your Digimon and Tamers' colou?rs$", RegexOptions.IgnoreCase);
        if (!m.Success) return ParseCounter(cnt.Trim());
        int per = m.Groups["n"].Success ? Math.Max(1, int.Parse(m.Groups["n"].Value)) : 1;
        // 자신의 디지몬(효과로 얻은 색도 — EX13-077 "진화원의 색을 전부 얻는다")과 테이머의 색 수 (Batch44Extra "colors your Digimon and Tamers have"와 같은 셈)
        return (o, _, _) => o.BattleArea.Where(d => d.TamerProxyOf == null).SelectMany(d => d.BaseColors66.Concat(d.ExtraColors))
            .Concat(o.Tamers.SelectMany(t => o.TamerColorsOf66(t))).Distinct().Count() / per;
    }

    private static string? TryParseChoice64(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParseChoice63(rawHeader, rawOptions, inherited, card, def);
        if (err == null) return err;
        string header = Normalize(rawHeader);
        var prefix = Regex.Match(header, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        string labelsText = prefix.Groups[1].Value.Trim();
        string body = prefix.Groups[2].Value.Trim();
        var optionTexts = rawOptions.Select(o => Normalize(o).TrimStart('・', '•', '·').Trim()).ToArray();
        Match m;
        // EX4-073 — 선택지 1개를 버린 장 수만큼: 같은 시점의 절 하나 "By trashing ~, <선택지> for each card trashed. Then, ~"로 읽는다
        if ((m = ForEachTrashedBelowRe64.Match(body)).Success && optionTexts.Length == 1)
        {
            string opt = optionTexts[0].TrimEnd('.');
            if (opt.Length == 0) return err;
            string clause = $"{labelsText} {m.Groups["cost"].Value}, {char.ToLowerInvariant(opt[0])}{opt[1..]} for each card trashed.{(m.Groups["post"].Success ? " " + m.Groups["post"].Value : "")}";
            if (TryParse(clause, inherited, card, null) != null) return err;
            if (def != null) TryParse(clause, inherited, card, def);
            return null;
        }
        if (!(m = ForEveryChooseRe64.Match(body)).Success || optionTexts.Length < 2 || ChoiceCounter64(m.Groups["cnt"].Value) is not { } counter) return err;
        var labels = Regex.Matches(labelsText, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim()).ToList();
        var triggers = new List<EffectTrigger>();
        bool oncePerTurn = false;
        foreach (var lab in labels)
        {
            if (EventTriggers.TryGetValue(lab, out var tr)) triggers.Add(tr);
            else if (lab.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase)) oncePerTurn = true;
            else return err;
        }
        if (triggers.Count == 0 || !MapMainTrigger(triggers, card, inherited)) return err;
        string preClause = labelsText + " " + m.Groups["pre"].Value.Trim() + ".";
        if (TryParse(preClause, inherited, card, null) != null) return err;
        var optionClauses = optionTexts.Select(o => labelsText + " " + o).ToArray();
        if (optionClauses.Any(o => TryParse(o, inherited, card, null) != null)) return err;
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
            int times = Math.Max(0, counter(c.Owner, c.Enemy, c.Self));   // 처리할 때 센다
            if (times == 0) { Emit(c, string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString()))), "발휘할 효과가 없습니다 (센 수 0)"); return; }
            for (int i = 0; i < times && !c.Engine.State.IsGameOver; i++)
            {
                int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 발휘할 효과를 1개 고르세요 ({i + 1}/{times}번째).", optionTexts, recommend58(c));
                var opt = options[Math.Clamp(pick, 0, options.Count - 1)];
                (opt.FirstOrDefault(e => e.Trigger == c.Trigger) ?? opt[0]).Resolve(c);
                Diag46.Hit("countedChoose64");
            }
        };
        foreach (var t in triggers)
        {
            if (t == EffectTrigger.Main) def.Main(TargetKind.None, resolve);
            else if (inherited) def.Inherited(t, resolve, null, oncePerTurn, false);
            else def.On(t, resolve, null, oncePerTurn, false);
        }
        return null;
    }

    /// <summary>
    /// 조건 (원래 규칙이 읽지 못한 것만): "if you trashed 3 cards" — 이 효과의 비용("By trashing up to 3 ~")으로 버린 장 수 (EX4-073)
    /// </summary>
    private static Func<EffectContext, bool>? ParseEventCondition64(string cond)
    {
        Match m;
        if ((m = Regex.Match(cond.Trim(), @"^you trashed (?<n>\d+)(?<more> or more)? cards$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return c => c.PaidCount >= n && Diag46.HitTrue("condPaid64");
        }
        return null;
    }

    /// <summary>이 묶음의 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep에서 앞의 묶음 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction64(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim(), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        Match m;
        // EX10-033 상대의 디지몬 1마리의 등장 코스트를 상대의 턴 종료까지 -(n × 비용으로 버린 장 수)
        if ((m = Regex.Match(s, @"^⟦oppcostdown64:(?<n>\d+)⟧$")).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                int k = Math.Min(c.PaidCount, 20);
                if (k <= 0) { c.LastStepResolved = false; return; }
                // 추천: 등장 코스트가 가장 높은 상대의 디지몬 (낮춰서 쓰는 효과 — 자신의 "가장 등장 코스트가 낮은" 효과 · 소멸 상한과 함께)
                var pool = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).OrderByDescending(d => d.PlayCost45).ToList();
                var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 등장 코스트를 -{n * k} 할 상대의 디지몬을 고르세요.", false, null).FirstOrDefault();
                if (d == null) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
                d.TimedPlayCostMods45.Add((-n * k, Until(c, Duration.OpponentsTurnEnd)));
                c.RecalculateFieldBuffs();
                c.LastTargets = new List<CardInstance> { d };
                Diag46.Hit("oppCostDown64");
                Emit(c, label, $"[{d.TopCard.Name}] 상대의 턴 종료까지 등장 코스트 -{n * k}");
            }, null);
        }
        // EX12-047 이 턴 동안 이 디지몬 DP +a, 상대의 디지몬 1마리 DP -b × (비용으로 되돌린 카드들의 색 수)
        if ((m = Regex.Match(s, @"^⟦retcolor64:(?<a>\d+):(?<b>\d+)⟧$")).Success)
        {
            int a = int.Parse(m.Groups["a"].Value), b = int.Parse(m.Groups["b"].Value);
            var self = ParseAction($"This Digimon gets +{a} DP for the turn", card, triggers);
            if (self.Error != null) return (null, self.Error);
            var byK = new Dictionary<int, Action<EffectContext>?>();
            return (c =>
            {
                self.Act!(c);
                int k = c.PaidCards64.SelectMany(x => x.AllColors).Distinct().Count();
                if (k <= 0) { Emit(c, label, "되돌린 카드에 색이 없습니다"); return; }
                if (!byK.TryGetValue(k, out var act))
                    byK[k] = act = ParseAction($"1 of your opponent's Digimon gets -{b * k} DP for the turn", card, triggers).Act;
                act?.Invoke(c);
                Diag46.Hit("returnedColors64");
            }, null);
        }
        // BT19-078 상대의 디지몬이 어택했을 때 — 이 효과로 등장한 자신의 디지몬으로 어택 대상을 바꿀 수 있다 (등장하지 않았으면 그대로)
        if (s == "⟦switchplayed64⟧")
            return (c =>
            {
                var d = c.PlayedThisEffect.LastOrDefault(x => c.Owner.BattleArea.Contains(x));
                var atk = c.Engine.CurrentAttacker;
                if (d == null || atk == null || !c.Enemy.BattleArea.Contains(atk) || ReferenceEquals(c.Engine.CurrentAttackTarget63, d)) { c.LastStepResolved = false; return; }
                var cur = c.Engine.CurrentAttackTarget63;
                int a = atk.GetCurrentDP();
                // 추천: 등장한 디지몬이 배틀에서 이기거나, 지키던 디지몬이 지고 그 디지몬이 더 귀하거나, 플레이어로의 어택이고 시큐리티가 2장 이하일 때
                bool rec = d.GetCurrentDP() > a
                    || cur != null && cur.GetCurrentDP() <= a && cur.TopCard.Level > d.TopCard.Level
                    || cur == null && c.Owner.SecurityStack.Count <= 2;
                if (!c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 어택 대상을 이 효과로 등장한 [{d.TopCard.Name}]으로 바꿀까요?", rec)) { c.LastStepResolved = false; return; }
                if (c.Engine.RedirectAttack(c.Owner, d)) { Diag46.Hit("switchPlayed64"); Emit(c, label, $"어택 대상을 [{d.TopCard.Name}]으로 바꿨습니다"); }
                else c.LastStepResolved = false;
            }, null);
        return null;
    }
}
