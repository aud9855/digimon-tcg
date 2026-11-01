using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.77.0 미지원 문장 51차 묶음 (원래 규칙이 읽지 못한 절만):
///  - BT12-112 · BT21-030 "When you would play this card, by placing 1 [Shoutmon] ~ under this Digimon, reduce the cost by 1 and ~ cards in your trash ~ for a DigiXros" —
///    배틀 에어리어의 「샤우트몬」 1마리만 (공식 Q&amp;A BT12-112 Q1). 등장할 때 효과를 먼저 해결하고 디지크로스를 선언한다 (종합 룰 7-2-2-2 · 7-2-3-1).
///    놓는 것은 맨 위 카드뿐, 그 진화원 · 링크 카드는 트래시 (4-7-7 · 4-7-8)
///  - BT10-111 "When you would DigiXros this turn, this Digimon may replace 1 of the DigiXros requirements" — 이 턴 동안 이 디지몬이 디지크로스 조건 1장 대신
///    (공식 Q&amp;A BT10-111 Q1 · 종합 룰 15-15-6-1. 카드 넘버가 서로 달라야 하는 칸은 대신할 수 없다 — 15-15-6-2)
///  - EX6-006 [육성]【상대의 턴 종료 시】 "By deleting this Digimon with 7 or more cards with different names in its digivolution cards, ~" — 육성 에어리어의 이 디지몬 (디지타마 카드도 디지몬 — 4-3-1)
///  - BT13-055 "digivolve that Digimon into this card for a digivolution cost of 3, ignoring this card's digivolution requirements" (같은 뜻 다른 표기)
///  - BT26-097 "After, you may place 1 card with [Aegiochusmon] in its name in your trash as any of your [Jupitermon]'s top digivolution card" (같은 뜻 다른 표기)
///  - BT12-089 "The Digimon digivolved by this effect gets +2000 DP for the turn" — 진화했을 때만 (공식 Q&amp;A BT12-089 Q1: 진화하지 않을 수도 있다)
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I77 = RegexOptions.IgnoreCase;

    /// <summary>BT10-111 표식 — "이 턴 동안 이 디지몬이 디지크로스 조건 1장 대신"</summary>
    internal const string XrosSubMarker77 = "⟦xrossub77⟧";

    /// <summary>BT12-112 · BT21-030 절 전체</summary>
    internal static readonly Regex XrosFieldCostRe77 = new(
        @"^When (?:you would play this card|this card would be played), by placing (?:1|one) (?:of your )?(?<x>\[[^\]]+\](?: or \[[^\]]+\])*) (?:as a digivolution card )?under (?:this Digimon|it|this card), reduce the (?:play |memory )?cost by (?<n>\d+) and (?:you may place cards (?:in|from) your trash as digivolution cards for a DigiXros|cards (?:in|from) your trash can also be placed for (?:a )?DigiXros)\.?$",
        I77 | RegexOptions.Compiled);

    /// <summary>원래 규칙이 읽지 못한 절 → 같은 뜻의 다른 표기 (바꾸지 못하면 null)</summary>
    internal static string? Rewrite77(string rawClause)
    {
        string t = rawClause.Trim();
        // BT13-055 "~, digivolve that Digimon into this card for a digivolution cost of 3, ignoring this card's digivolution requirements."
        string a = Regex.Replace(t, @", digivolve (?<w>that Digimon|it) into this card for a digivolution cost of (?<n>\d+), ignoring this card's digivolution requirements\.?$",
            ", ${w} digivolves into this card for a digivolution cost of ${n}, ignoring its digivolution requirements.", I77);
        // BT26-097 "~. After, you may place 1 card with [Aegiochusmon] in its name in your trash as any of your [Jupitermon]'s top digivolution card."
        a = Regex.Replace(a, @"\. After, you may place (?<p>1 card with \[[^\]]+\] in its name) (?:in|from) your trash as (?:any|1|one) of your (?<y>\[[^\]]+\])'s (?<pos>top|bottom) digivolution card\.?$",
            ". Then, you may place ${p} from your trash as 1 of your ${y} Digimon's ${pos} digivolution card.", I77);
        // BT12-089 "~ that Digimon may digivolve into ~. The Digimon digivolved by this effect gets +2000 DP for the turn."
        a = Regex.Replace(a, @"(?<=may digivolve into [^.]+)\. The Digimon digivolved by this effect gets (?<dp>[+-]\d+) DP for the turn\.?$",
            ". If digivolved by this effect, that Digimon gets ${dp} DP for the turn.", I77);
        // BT10-111 "~. When you would DigiXros this turn, this Digimon may replace 1 of the DigiXros requirements."
        a = Regex.Replace(a, @"^(?<h>\[On Play\][^.]+)\. When you would DigiXros this turn, this Digimon may replace 1 of the DigiXros requirements\.?$",
            "${h}. Then, " + XrosSubMarker77 + ".", I77);
        // ST10-06 "~. When DNA digivolving, you may search your security stack for 1 ~ and play it without paying its memory cost. Then, shuffle your security stack."
        //  — 찾기만 조그레스 진화했을 때, 셔플은 어느 진화에서도 (공식 Q&amp;A ST10-06 Q1)
        a = Regex.Replace(a, @"\. When DNA digivolving, you may search your security stack for (?<p>1 [^.]+?) and play it without paying its (?:memory )?cost\. Then, shuffle your security stack\.?$",
            ". Then, if DNA digivolving, search your security stack, and you may play ${p} among it without paying its cost. Then, shuffle your security stack.", I77);
        // EX6-029 "~. Then, if DNA digivolving, place 1 other Digimon at the bottom of its owner's security stack, and trash cards from the top of your opponent's security stack until it has 4 left."
        //  — 그 디지몬 주인의 시큐리티 아래 (맨 위 카드만 — 4-7-8), 그 뒤 상대의 시큐리티가 4장이 되도록
        a = Regex.Replace(a, @"\. Then, if DNA digivolving, place 1 other Digimon at the bottom of its owner's security stack, and (?<b>trash cards from the top of your opponent's security stack until it has \d+ left)\.?$",
            ". Then, if DNA digivolving, place 1 other Digimon as the bottom security card. Then, if DNA digivolving, ${b}.", I77);
        return a == t ? null : a;
    }

    private static string? TryParse77(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse76(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만
        if (!inherited && card.Type == CardType.Digimon && XrosFieldCostRe77.Match(rawClause.Trim()) is { Success: true } xm)
        {
            if (def != null)
            {
                def.Script41.XrosFieldCost77 = new XrosFieldCost77(TargetSpec.Names(xm.Groups["x"].Value), int.Parse(xm.Groups["n"].Value));
                Diag46.Hit("xrosFieldCost77");
            }
            return null;
        }
        if (Rewrite77(rawClause) is not { } alt || TryParse76(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse76(alt, inherited, card, def);
            Diag46.Hit("rewrite77");
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction77(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I77);
        // BT10-111 ⟦xrossub77⟧ — 이 턴 동안 이 디지몬이 디지크로스 조건 1장 대신
        if (s == XrosSubMarker77 && card.Type == CardType.Digimon)
        {
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)) { c.LastStepResolved = false; return; }
                c.Engine.AllowXrosSubstitute77(c.Self);
                Diag46.Hit("xrosSubstitute77");
                Emit(c, label, "이 턴 동안 이 디지몬은 디지크로스 조건 1장을 대신할 수 있습니다");
            }, null);
        }
        // EX6-029 "trash cards from the top of your opponent's security stack until it has 4 left" — 이미 그 장수 이하면 파기하지 않는다
        Match m;
        if ((m = Regex.Match(s, @"^trash cards from the top of your opponent's security stack until it has (?<n>\d+) left$", I77)).Success)
        {
            int keep = int.Parse(m.Groups["n"].Value);
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                int k = 0;
                while (c.Enemy.SecurityStack.Count > keep) { var x = c.Enemy.SecurityStack[0]; c.Enemy.Trash.Add(x); c.Enemy.SecurityStack.RemoveAt(0); c.Engine.NotifySecurityRemoved(c.Enemy, x, trashed: true); k++; }
                if (k == 0) c.LastStepResolved = false;
                else Diag46.Hit("trashSecurityUntil77");
                Emit(c, label, $"상대 시큐리티 위에서 {k}장 파기 (남은 {c.Enemy.SecurityStack.Count}장)");
            }, null);
        }
        return null;
    }

    /// <summary>
    /// 비용 (원래 규칙이 읽지 못한 것만 — 체인 맨 끝):
    ///  - EX6-006 "deleting this Digimon with 7 or more cards with different names in its digivolution cards" — 배틀 에어리어 또는 육성 에어리어의 이 디지몬.
    ///    명칭은 앞면 진화원만 센다 (뒷면 카드는 카드 정보를 가지지 않는다 — 종합 룰 4-7-9)
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost77(string cost, CardData card)
    {
        Match m;
        if ((m = Regex.Match(cost.Trim(), @"^deleting this Digimon with (?<n>\d+) or more cards with different names in its digivolution cards$", I77)).Success)
        {
            int need = int.Parse(m.Groups["n"].Value);
            bool Enough(EffectContext c) => c.Self != null && c.Self.FaceUpSources.Select(CardNames.MatchName).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= need;
            bool InBreeding(EffectContext c) => c.Self != null && ReferenceEquals(c.Owner.BreedingArea, c.Self);
            return (c => Enough(c) && (InBreeding(c) || c.Self != null && c.Owner.BattleArea.Contains(c.Self)),
                    c =>
                    {
                        if (InBreeding(c))
                        {
                            if (!c.Engine.DeleteBreeding44(c.Owner)) { c.CostFailed = true; return; }
                        }
                        else c.DeleteOwn(c.Self!);
                        Diag46.Hit("deleteSelfDistinctNames77");
                    },
                    _ => true, $"진화원에 명칭이 서로 다른 카드가 {need}장 이상 있는 이 디지몬을 소멸시켜");
        }
        return null;
    }
}
