using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.75.0 미지원 문장 49차 묶음 (원래 규칙이 읽지 못한 처리만):
///  - EX4-014 "[Your Turn] [Once Per Turn] When ~ is played, ＜Draw 1＞. When ~ is played, return ~." —
///    한 줄(한 효과)에 발동 조건이 둘 — 머리말([Your Turn] [Once Per Turn])은 둘 다에 붙고, [턴에 1회]는 한 효과로서 함께 센다
///    (종합 룰 15-14-1-1 효과마다 · 한 줄의 여러 시점은 한 효과 — 공식 Q&amp;A BT13-056 Q1 · EX5-025 Q1, 공식 한글 EX4-014도 【자신의 턴】[턴에 1회] 아래 한 줄)
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I75 = RegexOptions.IgnoreCase;

    /// <summary>"[머리말] When A, X. When B, Y." — 한 줄에 "When ~" 문장 둘</summary>
    private static readonly Regex TwoWhen75 = new(@"^(?<h>(?:\s*\[[^\]]+\])+)\s*(?<a>When [^.]+?, .+?\.)\s+(?<b>When [^.]+?, .+?)$", I75 | RegexOptions.Compiled);

    internal static (string First, string Second, string Header)? SplitTwoWhen75(string clause)
    {
        var m = TwoWhen75.Match(clause.Trim());
        if (!m.Success) return null;
        string h = m.Groups["h"].Value.Trim();
        return (h + " " + m.Groups["a"].Value.Trim(), h + " " + m.Groups["b"].Value.Trim(), h);
    }

    /// <summary>
    /// "you have no other [X] in play" — 이 디지몬 말고 배틀 에어리어의 디지몬 · 테이머 중 [X]가 없다 (육성 에어리어는 참조하지 않는다 — 종합 룰 3-4-7-8).
    /// EX2-007 "If you don't have another [Mother D-Reaper] in play" (공식 한글도 "자신의 다른 ~") — 원래 이름 조건은 "another"를 보지 않아 이 디지몬 자신도 세므로
    /// TryParse75가 이 꼴로 바꿔 읽는다
    /// </summary>
    private static Func<EffectContext, bool>? ParseEventCondition75(string cond)
    {
        var m = Regex.Match(cond.Trim(), $@"^you have no other (?<x>{TargetSpec.NameList})(?: in play)?$", I75);
        if (!m.Success) return null;
        var xs = TargetSpec.Names(m.Groups["x"].Value);
        return c => !c.Owner.BattleArea.Any(d => !ReferenceEquals(d, c.Self) && xs.Any(x => TargetSpec.NameHas(d, x)))
                    && !c.Owner.Tamers.Any(t => xs.Any(x => TargetSpec.NameHas(t, x)));
    }

    /// <summary>
    /// 영어 API 문장이 공식 영어 카드 리스트(world.digimoncard.com)와 다른 절 → 공식 문장 (카드 번호 · API 문장이 정확히 같을 때만 — OfficialClause74 다음). 특징 · 이름을 잘못 적은 API 문장:
    ///  - BT11-083: API "1 [Mirei Mikagura] trait" — 공식 영어 "1 [Mirei Mikagura]" (테이머 카드 이름 — 공식 한글도 이름. 천사 3종은 공식 한글도 일치 특징이므로 그대로)
    ///  - BT23-090: API "All of your [Hudie] Digimon" — 공식 영어 "All of your [Hudie] trait Digimon" (공식 한글도 특징)
    ///  - P-209: API "1 level 4 or lower [Demon] card" — 공식 영어 "1 level 4 or lower [Demon] or [Titan] trait card" (공식 한글도 특징 둘)
    /// </summary>
    private static readonly Dictionary<(string Id, string Api), string> OfficialClauses75 = new()
    {
        [("BT11-083", "[When Digivolving] You may trash 1 card in your hand. If you do, you may return 1 [Mirei Mikagura] trait or 1 card with the [Angel], [Archangel] or [Fallen Angel] trait from your trash to your hand.")] =
            "[When Digivolving] You may trash 1 card in your hand. If you do, you may return 1 [Mirei Mikagura] or 1 card with the [Angel], [Archangel] or [Fallen Angel] trait from your trash to your hand.",
        [("BT23-090", "[All Turns] All of your [Hudie] Digimon get +1000 DP.")] =
            "[All Turns] All of your [Hudie] trait Digimon get +1000 DP.",
        [("P-209", "[All Turns] [Once Per Turn] When your hand is trashed from, you may play 1 level 4 or lower [Demon] card from your trash without paying the cost.")] =
            "[All Turns] [Once Per Turn] When your hand is trashed from, you may play 1 level 4 or lower [Demon] or [Titan] trait card from your trash without paying the cost.",
    };

    internal static string OfficialClause75(CardData card, string clause) =>
        OfficialClauses75.TryGetValue((card.CardId, clause.Trim()), out var official) ? official : clause;

    private static (Action<EffectContext>? Act, string? Error)? ParseAction75(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I75);
        Match m;
        // BT1-089 "hatch 1 Digi-Egg card to an empty space in your breeding area, or move 1 level 3 or higher Digimon from your breeding area to your battle area"
        //  — 공식 한글 BT1-089와 같은 뜻 (부화시키거나 이동한다).
        //  부화는 육성 에어리어가 비어 있고 디지타마 덱이 있을 때 (종합 룰 4-18-2 · 4-18-3), 이동은 육성 에어리어의 그 디지몬 (4-17) — 육성 에어리어에는 카드가 1장뿐이라 (3-4-7-2) 둘 중 하나만 할 수 있다
        if ((m = Regex.Match(s, @"^hatch (?:1|a) Digi-Egg(?: card)? (?:to|in|into) (?:an empty space in )?your breeding area, or move 1 level (?<lv>\d+) or higher Digimon from your breeding area to your battle area$", I75)).Success)
        {
            int lv = int.Parse(m.Groups["lv"].Value);
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return (c =>
            {
                var b = c.Owner.BreedingArea;
                if (b == null)
                {
                    var egg = c.Engine.Hatch(c.Owner);
                    if (egg == null) c.LastStepResolved = false;
                    Emit(c, label, egg != null ? $"[{egg.Name}] 육성 에어리어에 부화" : "부화할 수 없습니다 (디지타마 덱이 없음)");
                    return;
                }
                var moved = b.TopCard.Type == CardType.Digimon && b.TopCard.Level >= lv ? c.Engine.MoveBreedingToBattle(c.Owner) : null;
                if (moved != null) { c.LastTargets = new List<CardInstance> { moved }; Diag46.Hit("hatchOrMove75"); }
                else c.LastStepResolved = false;
                Emit(c, label, moved != null ? $"[{moved.TopCard.Name}] 육성 에어리어 → 배틀 에어리어" : $"육성 에어리어가 비어 있지 않고 Lv.{lv} 이상인 디지몬도 없어 부화 · 이동할 수 없습니다");
            }, null);
        }
        // EX2-007 "place 1 of your [ADR-02 Searcher]s from in play or your hand under this Digimon as its bottom digivolution card"
        //  — 공식 한글 EX2-007과 같은 뜻 (강제 — "놓을 수 있다"가 아니다).
        //  필드의 것은 배틀 에어리어의 디지몬 (육성 에어리어를 지정하지 않은 효과는 육성 에어리어의 카드를 고를 수 없다 — 종합 룰 3-4-7-5),
        //  놓는 것은 맨 위 카드 1장이고 그 디지몬의 진화원 · 링크 카드는 트래시 (종합 룰 4-7-7 · 4-7-8 — PlaceDigimonUnder)
        if ((m = Regex.Match(s, @"^place 1 of your (?<x>\[[^\]]+\])s? from in play or (?:from )?your hand under this Digimon as its bottom digivolution card$", I75)).Success
            && card.Type is CardType.Digimon or CardType.Digitama)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            bool IsX(CardData cd) => xs.Any(n => cd.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            return (c =>
            {
                var host = c.Self;
                if (host == null || !c.Owner.BattleArea.Contains(host)) { c.LastStepResolved = false; return; }
                var field = c.Owner.BattleArea.Where(d => !ReferenceEquals(d, host) && IsX(d.TopCard)).ToList();
                var hand = c.Owner.Hand.Where(IsX).ToList();
                if (field.Count + hand.Count == 0) { c.LastStepResolved = false; c.Log($"[{c.Card.Name}] 놓을 [{m.Groups["x"].Value}]이(가) 없습니다."); return; }
                // 고르기: 필드의 디지몬 · 패의 카드 (패의 카드를 먼저 추천 — 필드의 디지몬을 남긴다)
                var labels = hand.Select(x => $"패의 [{x.Name}]").Concat(field.Select(d => $"필드의 [{d.TopCard.Name}]")).ToList();
                int pick = labels.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 이 디지몬의 진화원 아래에 놓을 카드를 고르세요.", labels, 0);
                pick = Math.Clamp(pick, 0, labels.Count - 1);
                if (pick < hand.Count)
                {
                    var x = hand[pick];
                    c.Owner.Hand.Remove(x);
                    host.AddBottomSource(x);
                    c.RecalculateFieldBuffs();
                    Diag46.Hit("placeUnderSelf75");
                    c.Log($"[{c.Card.Name}] 패의 [{x.Name}]을(를) [{host.TopCard.Name}]의 진화원 아래에 놓았습니다.");
                    return;
                }
                var mv = field[pick - hand.Count];
                if (c.Engine.PlaceDigimonUnder(c.Owner, mv, host, canPrevent: true)) { Diag46.Hit("placeUnderSelf75"); c.Log($"[{c.Card.Name}] [{mv.TopCard.Name}]을(를) [{host.TopCard.Name}]의 진화원 아래에 놓았습니다."); }
                else c.LastStepResolved = false;
            }, null);
        }
        return null;
    }

    private static string? TryParse75(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        // EX2-007 "[Main] [Once Per Turn] If you don't have another [Mother D-Reaper] in play, place ~ under this Digimon ~" — 이 동작은 49차 규칙(ParseAction75)으로만 읽히므로
        // 원래 규칙이 읽던 절이 아니다. 원래 이름 조건은 "another"를 보지 않아 이 디지몬 자신도 세므로, 이름이 이 카드인 "another"는 "no other"(이 디지몬 말고)로 읽는다
        var self75 = Regex.Match(rawClause.Trim(), @"^(?<h>(?:\[[^\]]+\]\s*)+)If you don't have another \[(?<n>[^\]]+)\] in play, (?<rest>place 1 of your \[[^\]]+\]s? from in play or (?:from )?your hand under this Digimon as its bottom digivolution card\.?)$", I75);
        if (self75.Success && self75.Groups["n"].Value.Equals(card.Name, StringComparison.OrdinalIgnoreCase))
            return TryParse74($"{self75.Groups["h"].Value}If you have no other [{self75.Groups["n"].Value}] in play, {self75.Groups["rest"].Value}", inherited, card, def);
        string? err = TryParse74(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만: 한 줄의 "When ~" 문장 둘을 같은 머리말로 나눠 읽는다 (둘 다 읽혀야)
        if (SplitTwoWhen75(rawClause) is not { } sp) return err;
        if (TryParse74(sp.First, inherited, card, null) != null || TryParse74(sp.Second, inherited, card, null) != null) return err;
        if (def != null)
        {
            int before = def.Script41.Effects.Count;
            TryParse74(sp.First, inherited, card, def);
            TryParse74(sp.Second, inherited, card, def);
            // 한 효과 — [턴에 1회] 기록 이름을 하나로 (27차 규칙은 절마다 이름을 붙이므로, 나눈 두 문장 모두 줄 이름으로 덮는다)
            var effects = def.Script41.Effects;
            for (int i = before; i < effects.Count; i++)
                effects[i] = effects[i] with { OnceKey53 = "#75:" + rawClause.Trim() };
            Diag46.Hit("twoWhenOneEffect75");
        }
        return null;
    }
}
