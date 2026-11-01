using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.65.0 미지원 문장 39차 묶음 (파서 쪽) — 원래 규칙(38차까지)이 읽지 못한 절만:
///  - ST13-02 · ST13-09 "By placing this Digimon under ~ as its bottom digivolution card, you may reveal the top card of your deck. If that card is ~, you may play it
///    without paying its memory cost. Add the rest to your hand." — 놓는 것(비용)으로 덱 위 1장 공개 → 조건에 맞으면 등장시킬 수 있다 (등장시키지 않으면 패에)
///  - BT9-103 "~ can't attack players, and cards can't be added to security stacks by your opponent's effects." — 상대의 효과로는 시큐리티에 카드를 추가할 수 없다
///    (기간 — 엔진 GoldfishEngine.SecurityAddBlocked65. 공식 Q&amp;A BT10-041 Q1: 시큐리티에 놓을 옵션은 트래시)
///  - EX10-058 "give 1 of their Digimon or Tamers "~"" — 테이머에게도 효과 문장을 준다 (그 테이머의 주인이 발휘)
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth65;

    private static string? TryParse65(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse64(rawClause, inherited, card, def);
        if (err == null || _depth65 > 0) return err;
        string t = Normalize58(rawClause);
        string alt = ClauseRewrite65(t);
        if (alt == t) return err;
        _depth65++;
        try
        {
            if (TryParse64(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse64(alt, inherited, card, def);
            return null;
        }
        finally { _depth65--; }
    }

    // ST13-02 · ST13-09
    internal static readonly Regex RevealMayPlayRe65 = new(@"\b(?<pre>By placing this Digimon under [^.]+? as its bottom digivolution card), you may reveal the top card of your deck\. If that card is (?<pick>an? [^,.]+?), you may play it without paying (?:the|its) (?:memory )?cost\. Add the rest to (?:the|your) hand\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT9-103
    internal static readonly Regex NoSecAddRe65 = new(@"\b(?<a>Until the end of your opponent's turn, your opponent's Digimon [^.]+? can't attack players), and cards can't be added to security stacks by your opponent's effects\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // P-116
    internal static readonly Regex CostsZeroRe65 = new(@"^While you have (?<x>\[[^\]]+\](?:, \[[^\]]+\])*,? and \[[^\]]+\]), this card costs 0\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT13-092
    internal static readonly Regex SameNameDeleteRe65 = new(@"\b(?<pre>By returning 1 Digimon card from your opponent's trash to the bottom of (?:their|the) deck), delete all of your opponent's Digimon with the same name as that card\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 · 이 묶음의 동작 표시 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules65 =
    {
        s => RevealMayPlayRe65.Replace(s, m => $"{m.Groups["pre"].Value}, ⟦revealplay65:{m.Groups["pick"].Value}⟧."),
        s => NoSecAddRe65.Replace(s, m => $"{m.Groups["a"].Value}. Then, ⟦nosecadd65⟧."),
        // P-116 "While you have [Agumon], [Pulsemon] and [Gammamon], this card costs 0." — 그 셋이 모두 있으면 사용 코스트 0 (줄이는 양 = 원래 코스트 이상)
        s => CostsZeroRe65.Replace(s, "When this card would be used, if you have ${x}, reduce its cost by 99."),
        // BT6-078 "~ you may place it under 1 of your purple Digimon at the bottom of its digivolution cards." — 이 카드를 그 디지몬의 진화원 맨 아래에
        s => Regex.Replace(s, @"\byou may place it under 1 of your (?<w>[^.]+?) Digimon at the bottom of its digivolution cards\.?$", "you may place this card as the bottom digivolution card of 1 of your ${w} Digimon.", RegexOptions.IgnoreCase),
        // BT13-092 "By returning 1 Digimon card from your opponent's trash to the bottom of their deck, delete all of your opponent's Digimon with the same name as that card." — 그 카드 = 비용으로 되돌린 카드
        s => SameNameDeleteRe65.Replace(s, "${pre}, ⟦delsamename65⟧."),
    };

    internal static string ClauseRewrite65(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules65) s = f(s);
        return s;
    }

    /// <summary>EX10-058 "Until your opponent's turn ends, give 1 of their Digimon or Tamers "~"" (기간은 앞 또는 뒤)</summary>
    internal static readonly Regex GiveDigimonOrTamerRe65 = new("^give (?<t>1 of (?:their|your opponent's) Digimon or Tamers) \"(?<q>[^\"]+)\"$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>이 묶음의 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep에서 앞의 묶음 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction65(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim(), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        Match m;
        // ST13-02 · ST13-09 덱 위 1장 공개 → 조건에 맞으면 코스트 없이 등장시킬 수 있다 (등장시키지 않으면 패에 — "Add the rest to your hand")
        if ((m = Regex.Match(s, @"^⟦revealplay65:(?<pick>[^⟧]+)⟧$")).Success)
        {
            string pickText = Regex.Replace(m.Groups["pick"].Value.Trim(), @"^an? ", "1 ", RegexOptions.IgnoreCase);
            if ((ParseCardPick(pickText) ?? ParseNamePick(pickText)) is not { } pick) return (null, $"미지원 공개 조건: {pickText}");
            var match = pick.Match;
            return (c =>
            {
                if (c.Owner.MainDeck.Count == 0) { c.LastStepResolved = false; Emit(c, label, "덱이 없습니다"); return; }
                var top = c.Owner.MainDeck[0];
                c.Owner.MainDeck.RemoveAt(0);
                c.LastCard = top;
                c.Log($"[{c.Card.Name}] 덱 위 카드를 공개합니다: [{top.Name}]");
                Diag46.Hit("revealMayPlay65");
                if (match(top) && top.Type == CardType.Digimon
                    && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 공개한 [{top.Name}]을(를) 코스트 없이 등장시킬까요? (아니요 = 패에 추가)", recommended: true)
                    && c.Engine.TryPlayByEffect(c.Owner, top, PlayOrigin.Revealed, null))
                {
                    Emit(c, label, $"덱 위 [{top.Name}] 공개 → 코스트 없이 등장");
                    return;
                }
                c.Owner.Hand.Add(top);
                Emit(c, label, $"덱 위 [{top.Name}] 공개 → 패에 추가");
            }, null);
        }
        // BT13-092 비용으로 되돌린 카드와 같은 명칭을 가진 상대의 디지몬을 전부 소멸
        if (s == "⟦delsamename65⟧")
            return (c =>
            {
                if (c.PaidCards64.FirstOrDefault() is not { } x) { c.LastStepResolved = false; Emit(c, label, "되돌린 카드가 없습니다"); return; }
                var victims = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && string.Equals(d.TopCard.Name, x.Name, StringComparison.OrdinalIgnoreCase) && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList();
                var names = new List<string>();
                foreach (var v in victims)
                    if (c.Enemy.BattleArea.Contains(v) && c.DeleteEnemy(v)) names.Add(v.TopCard.Name);
                if (names.Count > 0) Diag46.Hit("deleteSameName65");
                else c.LastStepResolved = false;
                Emit(c, label, names.Count > 0 ? $"[{x.Name}]과(와) 같은 명칭의 상대의 디지몬 [{string.Join(", ", names)}] 소멸" : $"[{x.Name}]과(와) 같은 명칭의 상대의 디지몬이 없습니다", attackerRemoved: victims.Contains(c.Other!) && names.Count > 0);
            }, null);
        // BT9-103 상대의 턴 종료까지 상대의 효과로는 시큐리티에 카드를 추가할 수 없다
        if (s == "⟦nosecadd65⟧")
            return (c =>
            {
                c.Enemy.NoSecurityAdd65 = Math.Max(c.Enemy.NoSecurityAdd65, Until(c, Duration.OpponentsTurnEnd));
                Diag46.Hit("noSecurityAddSet65");
                Emit(c, label, "상대의 턴 종료 시까지 상대의 효과로 시큐리티에 카드를 추가할 수 없다");
            }, null);
        // EX10-058 상대의 디지몬 또는 테이머 1명에게 효과 문장을 준다 (테이머면 그 테이머의 주인이 발휘 — 엔진 RunGrantedTamer65)
        if (s.Contains('"'))
        {
            var dur = ParseDuration(s, out string core);
            if ((m = GiveDigimonOrTamerRe65.Match(core.Trim())).Success && GrantedCard32(card, m.Groups["q"].Value) is { } granted)
            {
                var d2 = dur == Duration.None ? Duration.ThisTurn : dur;
                if (d2 == Duration.ThisAttack) return null;
                return (c =>
                {
                    int until = Until(c, d2);
                    var digimon = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null).ToList();
                    var tamers = c.Enemy.Tamers.Distinct().ToList();
                    if (digimon.Count + tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
                    // 추천: 테이머 (필드를 잘 떠나지 않는다) → 상대의 효과를 받는 디지몬 중 DP가 가장 높은 것
                    var labels = tamers.Select(t => $"테이머 [{t.Name}]").Concat(digimon.Select(d => $"디지몬 [{d.TopCard.Name}] (DP {d.GetCurrentDP():N0})")).ToList();
                    int rec = tamers.Count > 0 ? 0 : tamers.Count + digimon.Select((d, i) => (d, i)).OrderBy(x => c.Engine.IsUnaffectedBy(c.Owner, x.d) ? 1 : 0).ThenByDescending(x => x.d.GetCurrentDP()).First().i;
                    int pick = Math.Clamp(c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 효과 「{granted.EffectText}」를 줄 상대의 디지몬 또는 테이머를 고르세요.", labels, rec), 0, labels.Count - 1);
                    if (pick < tamers.Count)
                    {
                        var t = tamers[pick];
                        c.Enemy.TamerGrants65.Add((t, granted, until, c.Owner));
                        Diag46.Hit("grantTamer65");
                        Emit(c, label, $"테이머 [{t.Name}]에게 효과 「{granted.EffectText}」를 준다");
                        return;
                    }
                    var dg = digimon[pick - tamers.Count];
                    dg.GrantedEffects.Add((granted, until, c.Owner));
                    c.Engine.NoteGrantedCat61(dg, granted);
                    c.LastTargets = new List<CardInstance> { dg };
                    c.RecalculateFieldBuffs();
                    Emit(c, label, $"[{dg.TopCard.Name}]에게 효과 「{granted.EffectText}」를 준다");
                }, null);
            }
        }
        return null;
    }

    /// <summary>
    /// v0.65.0 패의 카드를 진화원 아래에 놓는 비용 (BT7-064 등) 추천: 패가 (놓을 매수 + 1)장 이하면 보통은 내지 않지만,
    /// 이 효과를 받는 디지몬이 지킬 만하면 (Lv.6 이상이거나 진화원이 4장 이상) 낸다. 패에 남는 카드가 없어지면 내지 않는다
    /// </summary>
    internal static bool HandSourceWorth65(EffectContext c) =>
        c.Self is { } d && c.Owner.BattleArea.Contains(d) && c.Owner.Hand.Count >= 2 && (d.TopCard.Level >= 6 || d.EvolutionSources.Count >= 4) && Diag46.HitTrue("handSourceWorth65");

    /// <summary>
    /// v0.65.0 EX12-047 "those returned cards' colors" — 비용으로 되돌릴 카드 추천: 색이 많아지는 카드부터 (되돌린 카드들이 가진 색의 수를 크게), 같으면 Lv.가 높은 것
    /// </summary>
    /// <summary>v0.65.0 BT13-092 비용 추천: 상대의 배틀 에어리어에 같은 명칭의 디지몬이 있는 카드부터 (그 디지몬들의 Lv. 합이 큰 것), 같으면 Lv.가 높은 것</summary>
    internal static List<CardData> SameNameOrder65(EffectContext c, IEnumerable<CardData> pool) =>
        pool.OrderByDescending(x => c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && string.Equals(d.TopCard.Name, x.Name, StringComparison.OrdinalIgnoreCase)).Sum(d => d.TopCard.Level + 1))
            .ThenByDescending(x => x.Level).ToList();

    internal static List<CardData> ColorGreedyOrder65(IEnumerable<CardData> pool)
    {
        var rest = pool.ToList();
        var order = new List<CardData>();
        var have = new HashSet<CardColor>();
        while (rest.Count > 0)
        {
            var best = rest.OrderByDescending(x => x.AllColors.Count(col => !have.Contains(col))).ThenByDescending(x => x.AllColors.Count).ThenByDescending(x => x.Level).First();
            order.Add(best);
            rest.Remove(best);
            foreach (var col in best.AllColors) have.Add(col);
        }
        return order;
    }
}
