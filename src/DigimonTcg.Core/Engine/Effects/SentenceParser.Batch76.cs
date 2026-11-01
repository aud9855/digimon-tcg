using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.76.0 미지원 문장 50차 묶음 (원래 규칙이 읽지 못한 절만):
///  - BT13-020 "For the turn, treat the Tamer played with this effect as a 12000 DP Digimon with ＜Rush＞, and it cannot digivolve" — 앞 문장이 등장시킨 테이머를
///    디지몬으로도 취급 (v0.36.0 테이머를 디지몬으로 — 공식 Q&amp;A BT13-020 Q1 "「DP X000인 디지몬」으로도 취급"). ≪속공≫이 있으므로 등장한 턴에도 어택할 수 있다
///  - BT15-082 "remove 2000 from this effect's playable card's DP maximum" = "this effect's DP maximum" (공식 Q&amp;A BT15-082 Q1 — 상대의 시큐리티 1장마다 등장시킬 수 있는 DP 상한 -2000)
///  - BT11-086 "If DigiXrosing, add 1 to the number of cards you play with this effect" — 디지크로스해 등장했으면 같은 조건의 카드를 1장 더 (한 효과 — 함께 등장한 카드의 【등장 시】는 효과가 끝난 뒤)
///  - BT11-105 "you may digivolve 1 of your Digimon into 1 [A] or [B] from your trash for its digivolution cost" = "1 of your Digimon may digivolve into [A] or [B] in the trash"
///    (진화 코스트를 지불 — 공식 Q&amp;A BT11-105 Q1: 트래시에 진화할 카드가 없어도 「벰몬」을 놓을 수 있다)
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I76 = RegexOptions.IgnoreCase;

    /// <summary>
    /// 영어 API 문장이 공식 영어 카드 리스트(world.digimoncard.com)와 다른 절 → 공식 문장 (카드 번호 · API 문장이 정확히 같을 때만 — OfficialClause75 다음):
    ///  - P-158: API "By returning this Tamer to the bottom of the deck. You may play ~ For each digivolution card under 1 of your [Mother D-Reaper], add 1 to the maximum play cost you may play with this effect."
    ///    (마침표가 비용을 끊었다) — 공식 영어 "By returning this Tamer to the bottom of the deck, you may play 1 play cost 3 or lower Digimon card with the [D-Reaper] trait from your hand without paying the cost.
    ///    For each digivolution card of 1 of your [Mother D-Reaper]s, add 1 to this effect's play cost maximum." (공식 한글 P-158도 같은 뜻)
    /// </summary>
    private static readonly Dictionary<(string Id, string Api), string> OfficialClauses76 = new()
    {
        [("P-158", "[Main] By returning this Tamer to the bottom of the deck. You may play 1 Digimon card with the [D-Reaper] trait and play cost of 3 or less from your hand without paying the cost. For each digivolution card under 1 of your [Mother D-Reaper], add 1 to the maximum play cost you may play with this effect.")] =
            "[Main] By returning this Tamer to the bottom of the deck, you may play 1 play cost 3 or lower Digimon card with the [D-Reaper] trait from your hand without paying the cost. For each digivolution card of 1 of your [Mother D-Reaper]s, add 1 to this effect's play cost maximum.",
    };

    internal static string OfficialClause76(CardData card, string clause) =>
        OfficialClauses76.TryGetValue((card.CardId, clause.Trim()), out var official) ? official : clause;

    /// <summary>BT11-086 "~ You may play 1 X from your trash without paying the cost. If DigiXrosing, add 1 to the number of cards you play with this effect."</summary>
    private static readonly Regex XrosPlusOne76 = new(@"^(?<h>(?:\s*\[[^\]]+\])+\s*)(?<play>You may play 1 (?<pick>.+?) from your trash without paying the cost)\. If DigiXrosing, add 1 to the number of cards you play with this effect\.?$", I76 | RegexOptions.Compiled);

    /// <summary>원래 규칙이 읽지 못한 절 → 같은 뜻의 다른 표기 (바꾸지 못하면 null)</summary>
    internal static string? Rewrite76(string rawClause)
    {
        string t = rawClause.Trim();
        // BT15-082 "remove 2000 from this effect's playable card's DP maximum"
        string a = Regex.Replace(t, @"\bthis effect's playable card's (?<w>DP|level|play cost) maximum\b", "this effect's ${w} maximum", I76);
        // BT11-105 "you may digivolve 1 of your Digimon into 1 [A] or [B] from your trash for its digivolution cost"
        a = Regex.Replace(a, @", you may digivolve 1 of your Digimon into 1 (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) from your trash for its digivolution cost\.?$", ", 1 of your Digimon may digivolve into ${x} in the trash.", I76);
        // BT11-086 — 디지크로스했으면 같은 조건의 카드를 1장 더
        if (XrosPlusOne76.Match(a) is { Success: true } x)
            a = $"{x.Groups["h"].Value}{x.Groups["play"].Value}. Then, if DigiXrosing, you may play 1 {x.Groups["pick"].Value} from your trash without paying the cost.";
        // EX6-060 "place 1 card with the [Seven Great Demon Lords] trait card from your trash ~" — "trait card"의 "card"가 겹쳤다 (공식 한글도 카드 1장)
        a = Regex.Replace(a, @"\b(?<p>[Pp]lace 1 card with the \[[^\]]+\] trait) card (?=from your trash as the bottom digivolution card of (?:your )?\[[^\]]+\] in the breeding area)", "${p} ", I76);
        // BT8-070 "If this Digimon has a red digivolution card, choose any number of your opponent's Digimon. If this Digimon has a black digivolution card, choose any number of your opponent's Tamers.
        //  The chosen cards' play costs must add up to 6 or less. Delete the chosen cards." — 한 번에 고른다 (공식 Q&A BT8-070 Q1: 둘 다 있으면 디지몬과 테이머를 함께)
        a = Regex.Replace(a, @"\bIf this Digimon has an? (?<c1>red|blue|yellow|green|black|purple|white) digivolution card, choose any number of your opponent's Digimon\. If this Digimon has an? (?<c2>red|blue|yellow|green|black|purple|white) digivolution card, choose any number of your opponent's Tamers\. The chosen cards' play costs must add up to (?<n>\d+) or less\. Delete the chosen cards\.?$",
            "⟦srcdel76:${c1}:${c2}:${n}⟧.", I76);
        // BT13-112 "Delete 1 of your opponent's Digimon, or you may play 1 of each ~ from the digivolution cards of your Digimon in the breeding area without paying their costs.
        //  When a Digimon is played by this effect, trash 1 of your Digimon in the breeding area, and all of your Digimon gain ＜Rush＞ for the turn." — 둘 중 하나 (한 문장으로 묶는다)
        a = Regex.Replace(a, @"\bDelete 1 of your opponent's Digimon, or (?<p>you may play 1 of each .+? from the digivolution cards of your Digimon in (?:the|your) breeding area without paying (?:the|their|its) costs?)\. When a Digimon is played by this effect, trash 1 of your Digimon in (?:the|your) breeding area, and all of your Digimon gain ＜Rush＞ for the turn\.?(?: \([^)]*\))?$",
            "⟦delorplay76⟧ ${p}.", I76);
        return a == t ? null : a;
    }

    /// <summary>
    /// 상대의 디지몬 · 테이머를 등장 코스트 합계 budget 이하가 되도록 원하는 수만큼 골라 소멸 (P-094 규칙과 같은 고르기 — 디지몬만 · 테이머만도).
    /// 효과를 받지 않는 디지몬은 고르지 않는다. LastTargets = 고른 디지몬 · LastTamers35 = 고른 테이머
    /// </summary>
    private static void DeleteCostTotal76(EffectContext c, string label, int budget, bool digimon, bool tamers)
    {
        var tamerOf = new Dictionary<CardInstance, CardData>(ReferenceEqualityComparer.Instance);
        var all = digimon ? c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).ToList() : new List<CardInstance>();
        if (tamers)
            foreach (var t in c.Enemy.Tamers.Distinct())
            {
                var fake = new CardInstance(t, 0);
                tamerOf[fake] = t;
                all.Add(fake);
            }
        string what = digimon && tamers ? "디지몬/테이머" : digimon ? "디지몬" : "테이머";
        var picked = new List<CardInstance>();
        while (true)
        {
            var pool = all.Where(d => !picked.Contains(d) && d.PlayCost45 <= budget)
                .OrderByDescending(d => d.PlayCost45).ThenByDescending(d => tamerOf.ContainsKey(d) ? 0 : 1).ThenByDescending(d => d.GetCurrentDP()).ToList();
            if (pool.Count == 0) break;
            var d = c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 등장 코스트 합계 {budget} 이하가 되도록 상대의 {what}을(를) 고르세요 (남은 {budget}, 그만 고르려면 취소).", optional: true, preselected: null).FirstOrDefault();
            if (d == null) break;
            picked.Add(d);
            budget -= Math.Max(0, d.PlayCost45);
        }
        var names = new List<string>();
        foreach (var d in picked)
        {
            if (tamerOf.TryGetValue(d, out var t)) { if (c.Enemy.Tamers.Contains(t)) { c.Engine.DeleteTamer(c.Enemy, t); names.Add(t.Name); } continue; }
            if (c.Enemy.BattleArea.Contains(d) && c.DeleteEnemy(d)) names.Add(d.TopCard.Name);
        }
        c.LastTargets = picked.Where(d => !tamerOf.ContainsKey(d)).ToList();
        c.LastTamers35 = picked.Where(tamerOf.ContainsKey).Select(d => tamerOf[d]).ToList();
        if (picked.Count == 0) c.LastStepResolved = false; else Diag46.Hit("deleteCostTotal76");
        Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸 (등장 코스트 합계)" : $"고를 상대의 {what}이(가) 없습니다");
    }

    /// <summary>
    /// 비용 (원래 규칙이 읽지 못한 것만 — 체인 맨 끝):
    ///  - BT23-072 "paying 3 cost and placing this card as the bottom digivolution card of your [King Drasil_7D6] or [Mother Eater] in the breeding area" —
    ///    육성 에어리어를 지정한 효과 (종합 룰 3-4-7-5). 육성 에어리어에 그 카드가 없으면 발휘를 선언할 수 없다 (공식 Q&amp;A BT23-072 Q1)
    ///  - BT23-073 "deleting this Digimon or placing it as the bottom digivolution card of your [Mother Eater] in the breeding area" — 둘 중 하나.
    ///    놓는 것은 맨 위 카드뿐, 그 진화원 · 링크 카드는 트래시 (종합 룰 4-7-7 · 4-7-8)
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost76(string cost, CardData card)
    {
        string s = cost.Trim();
        Match m;
        if ((m = Regex.Match(s, $@"^paying (?<n>\d+) (?:memory|cost) and placing this card as the bottom digivolution card of (?:1 of |one of )?your (?<x>{TargetSpec.NameList}) in (?:the|your) breeding area$", I76)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            CardInstance? Host(EffectContext c) => c.Owner.BreedingArea is { } b && xs.Any(x => TargetSpec.NameHas(b, x)) ? b : null;
            return (c => c.Owner.Hand.Contains(c.Card) && Host(c) != null,
                    c =>
                    {
                        if (Host(c) is not { } b || !c.Owner.Hand.Remove(c.Card)) { c.CostFailed = true; return; }
                        c.AddMemory(-n);
                        b.AddBottomSource(c.Card);
                        c.LastTargets = new List<CardInstance> { b };
                        c.RecalculateFieldBuffs();
                        Diag46.Hit("payPlaceUnderBreeding76");
                    },
                    c => c.OwnerMemory - n >= 0, $"메모리 {n}을(를) 지불하고 이 카드를 육성 에어리어의 진화원 맨 아래에 놓아");
        }
        if ((m = Regex.Match(s, $@"^deleting this Digimon or placing it as the bottom digivolution card of (?:1 of |one of )?your (?<x>{TargetSpec.NameList}) in (?:the|your) breeding area$", I76)).Success
            && ParseCost("deleting this Digimon", card) is { } del)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            CardInstance? Host(EffectContext c) => c.Owner.BreedingArea is { } b && xs.Any(x => TargetSpec.NameHas(b, x)) ? b : null;
            bool CanPlace(EffectContext c) => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && Host(c) != null;
            return (c => del.CanPay(c) || CanPlace(c),
                    c =>
                    {
                        bool place = CanPlace(c);
                        if (place && del.CanPay(c))
                            place = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 비용을 고르세요.", new List<string> { $"이 디지몬을 육성 에어리어 [{Host(c)!.TopCard.Name}]의 진화원 맨 아래에 놓는다", "이 디지몬을 소멸시킨다" }, 0) == 0;
                        if (!place) { del.Pay(c); return; }
                        if (!c.Engine.PlaceDigimonUnder(c.Owner, c.Self!, Host(c)!, canPrevent: false)) { c.CostFailed = true; return; }
                        Diag46.Hit("placeSelfUnderBreedingCost76");
                    },
                    _ => true, "이 디지몬을 소멸시키거나 육성 에어리어의 진화원 맨 아래에 놓아");
        }
        return null;
    }

    private static string? TryParse76(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse75(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만
        if (Rewrite76(rawClause) is not { } alt || TryParse75(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse75(alt, inherited, card, def);
            Diag46.Hit("rewrite76");
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction76(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I76);
        Match m;
        // BT8-070 ⟦srcdel76:red:black:6⟧ — 진화원에 그 색 카드가 있으면 상대의 디지몬 / 테이머를, 합쳐서 등장 코스트 합계 N 이하가 되도록 골라 소멸
        if ((m = Regex.Match(s, @"^⟦srcdel76:(?<c1>\w+):(?<c2>\w+):(?<n>\d+)⟧$")).Success)
        {
            var c1 = Enum.Parse<CardColor>(m.Groups["c1"].Value, true);
            var c2 = Enum.Parse<CardColor>(m.Groups["c2"].Value, true);
            int n = int.Parse(m.Groups["n"].Value);
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                var srcs = c.Self?.FaceUpSources.ToList() ?? new List<CardData>();
                bool dg = srcs.Any(x => x.AllColors.Contains(c1)), tm = srcs.Any(x => x.AllColors.Contains(c2));
                if (!dg && !tm) { c.LastStepResolved = false; Emit(c, label, "진화원에 그 색 카드가 없습니다"); return; }
                DeleteCostTotal76(c, label, n, dg, tm);
            }, null);
        }
        // BT11-107 "Choose any number of your opponent's Digimon and/or Tamers whose combined play costs are less than or equal to the play cost of 1 of your Digimon with [Greymon] in its name,
        //  and delete all of the chosen cards" — 그 자신의 디지몬의 등장 코스트까지 (여러 마리면 가장 높은 것 — 적게 고를 수도 있다: 공식 Q&amp;A BT11-107 Q1)
        if ((m = Regex.Match(s, @"^choose any number of your opponent's (?<w>Digimon and/or Tamers|Digimon and Tamers|Digimon|Tamers) whose combined play costs are less than or equal to the play cost of (?<own>(?:1|one) of your .+?), and delete (?:all of )?(?:the chosen cards|them)$", I76)).Success
            && OwnDigimonFilter(m.Groups["own"].Value) is { } ownOk)
        {
            string w = m.Groups["w"].Value.ToLowerInvariant();
            bool dg = w.Contains("digimon"), tm = w.Contains("tamers");
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                var owns = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && ownOk(d)).OrderByDescending(d => d.PlayCost45).ToList();
                var own = owns.FirstOrDefault();
                if (own == null) { c.LastStepResolved = false; Emit(c, label, "등장 코스트를 볼 자신의 디지몬이 없습니다"); return; }
                // v0.77.0 여러 마리면 기준으로 할 1마리를 고른다 (추천 = 등장 코스트가 가장 높은 것 — "1 of your Digimon with ~": 고르는 것은 플레이어)
                if (owns.Select(d => d.PlayCost45).Distinct().Count() > 1)
                {
                    own = c.Engine.ChooseTargets(c.Owner, owns, 1, $"[{c.Card.Name}] 등장 코스트를 기준으로 할 자신의 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault() ?? owns[0];
                    Diag46.Hit("costTotalRefChooser77");
                }
                DeleteCostTotal76(c, label, Math.Max(0, own.PlayCost45), dg, tm);
            }, null);
        }
        // EX10-061 "You may play 1 of each [Dark Masters] trait card with different names from this Digimon's digivolution cards without paying the costs" ·
        // BT13-112 "~ play 1 of each Digimon with the [Royal Knight] trait and a different name from the digivolution cards of your Digimon in the breeding area without paying their costs"
        //  — 명칭이 서로 다른 카드를 1장씩 (같은 명칭은 1장만 — 공식 Q&amp;A BT13-112 Q1). 한 효과로 함께 등장 (【등장 시】는 효과가 끝난 뒤)
        if ((m = Regex.Match(s, @"^(?<may>you may )?play 1 of each (?<pick>.+?)(?: with (?:a )?different names?| and a different name) from (?<z>this Digimon's digivolution cards|the digivolution cards of your Digimon in (?:the|your) breeding area) without paying (?:the|their|its) costs?$", I76)).Success
            && ParseCardPick("1 " + Regex.Replace(m.Groups["pick"].Value, @"^Digimon with\b", "Digimon card with", I76)) is { } each)
        {
            bool may = m.Groups["may"].Success, breeding = m.Groups["z"].Value.Contains("breeding", StringComparison.OrdinalIgnoreCase);
            var match = each.Match;
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                var host = breeding ? c.Owner.BreedingArea : c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
                if (host == null) { c.LastStepResolved = false; Emit(c, label, breeding ? "육성 에어리어에 디지몬이 없습니다" : "이 디지몬이 배틀 에어리어에 없습니다"); return; }
                var pool = host.FaceUpSources.Where(x => x.Type is CardType.Digimon or CardType.Tamer && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Sources))
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 카드가 없습니다"); return; }
                if (may && !c.Engine.AskYesNo(c.Owner, host, $"[{c.Card.Name}] 진화원에서 명칭이 서로 다른 카드 {pool.Count}장 ({string.Join(", ", pool.Select(x => x.Name))})을(를) 코스트 없이 등장시킬까요?", recommended: true)) { c.LastStepResolved = false; return; }
                int played = 0;
                foreach (var x in pool)
                {
                    if (c.Engine.State.IsGameOver) break;
                    if (c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Sources, () => host.TakeSource(y => ReferenceEquals(y, x)) != null)) played++;
                }
                c.RecalculateFieldBuffs();
                if (played == 0) c.LastStepResolved = false; else Diag46.Hit("playEachDifferentName76");
                Emit(c, label, played > 0 ? $"진화원에서 명칭이 서로 다른 카드 {played}장 등장" : "등장시키지 못했습니다");
            }, null);
        }
        // BT13-112 ⟦delorplay76⟧ "Delete 1 of your opponent's Digimon, or you may play 1 of each ~ (breeding area). When a Digimon is played by this effect, trash 1 of your Digimon in the breeding area,
        //  and all of your Digimon gain ＜Rush＞ for the turn." — 소멸 또는 등장 중 하나. 등장했으면 육성 에어리어의 디지몬을 파기 (육성 에어리어를 지정 — 종합 룰 3-4-7-5)하고
        //  그때 자신의 디지몬 전부가 이 턴 동안 ≪속공≫
        if ((m = Regex.Match(s, @"^⟦delorplay76⟧ (?<p>.+)$", I76)).Success && ParseAction76(m.Groups["p"].Value, card, triggers) is { Act: { } playAct })
        {
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                var enemies = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).OrderByDescending(d => d.GetCurrentDP()).ToList();
                int playable = c.Owner.BreedingArea?.FaceUpSources.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0;
                var opts = new List<string> { "상대의 디지몬 1마리를 소멸시킨다", "육성 에어리어의 디지몬의 진화원에서 명칭이 서로 다른 카드를 1장씩 등장시킨다" };
                int rec = enemies.Count == 0 || playable >= 2 ? 1 : 0;
                int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 효과를 고르세요.", opts, rec);
                if (pick == 0)
                {
                    if (enemies.Count == 0) { c.LastStepResolved = false; Emit(c, label, "소멸시킬 상대의 디지몬이 없습니다"); return; }
                    var d = c.Engine.ChooseTargets(c.Owner, enemies, 1, $"[{c.Card.Name}] 소멸시킬 상대의 디지몬을 고르세요.", optional: false, preselected: enemies[0]).FirstOrDefault() ?? enemies[0];
                    bool ok = c.Enemy.BattleArea.Contains(d) && c.DeleteEnemy(d);
                    c.LastTargets = new List<CardInstance> { d };
                    if (!ok) c.LastStepResolved = false;
                    Emit(c, label, ok ? $"[{d.TopCard.Name}] 소멸" : "소멸시키지 못했습니다", attackerRemoved: ok && ReferenceEquals(d, c.Other));
                    return;
                }
                int before = c.PlayedThisEffect.Count;
                playAct(c);
                if (c.PlayedThisEffect.Count <= before) return;
                if (c.Engine.TrashBreedingAny76(c.Owner)) Emit(c, label, "육성 에어리어의 디지몬을 파기");
                foreach (var d in c.Owner.BattleArea.Where(d => d.TamerProxyOf == null))
                    d.TimedKeywords.Add((Keyword.Rush, c.TurnSequence));
                c.RecalculateFieldBuffs();
                Diag46.Hit("deleteOrPlayEach76");
                Emit(c, label, "이 턴 동안 자신의 디지몬 전부 ≪속공≫");
            }, null);
        }
        // BT13-020 "For the turn, treat the Tamer played with this effect as a 12000 DP Digimon with ＜Rush＞, and it cannot digivolve"
        //  — 앞 문장이 등장시킨 테이머 (등장시키지 않았으면 아무것도 하지 않는다)
        Duration dur = ParseDuration(s, out string rest);
        if (dur is not (Duration.None or Duration.ThisAttack)
            && (m = Regex.Match(rest.Trim().TrimEnd(','), @"^treat the Tamer (?:played|that was played) (?:with|by) this effect as an? (?<dp>\d+) DP Digimon(?: with (?<kw>＜[^＞]+＞(?:(?:,| and|, and) ＜[^＞]+＞)*))?(?<noevo>,? and it (?:cannot|can't|can not) digivolve)?$", I76)).Success)
        {
            var grants = new List<Keyword>();
            foreach (Match k in Regex.Matches(m.Groups["kw"].Value, @"＜[^＞]+＞"))
            {
                if (ParseKeywordList(k.Value) is not { Count: 1 } list || list[0].Keyword is not { } kw) return null;
                grants.Add(kw);
            }
            int dp = int.Parse(m.Groups["dp"].Value);
            bool noEvo = m.Groups["noevo"].Success;
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                var tamer = c.Engine.LastTamerPlayedByEffect76;
                if (!c.LastStepResolved || tamer == null || !c.Owner.Tamers.Contains(tamer)) { c.LastStepResolved = false; Emit(c, label, "이 효과로 등장한 테이머가 없습니다"); return; }
                var inst = c.Engine.TreatTamerAsDigimon(c.Owner, tamer, dp, Until(c, dur), noEvo, grants);
                if (inst == null) { c.LastStepResolved = false; return; }
                c.LastTargets = new List<CardInstance> { inst };
                Diag46.Hit("playedTamerAsDigimon76");
                Emit(c, label, $"이 효과로 등장한 테이머 [{tamer.Name}]을(를) DP {dp}의 디지몬으로도 취급");
            }, null);
        }
        return null;
    }
}
