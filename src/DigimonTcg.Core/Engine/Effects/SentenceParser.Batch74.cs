using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.74.0 미지원 문장 48차 묶음 (원래 규칙이 읽지 못한 처리만):
///  - BT5-109 "At the end of the turn, return the Digimon that digivolved with this effect to the bottom of its owner's deck" —
///    이 효과의 "다음 진화" 감소로 진화한 디지몬을 이 턴 종료 시 덱 아래로, 진화원은 파기 (공식 한글 BT5-109)
///  - EX1-071 "you may trash 1 Digimon card in your hand of the same color as the digivolving Digimon to reduce ~ by 4" —
///    배틀 에어리어에 있는 진화하는 디지몬과 같은 색 (공식 Q&amp;A EX1-071 Q1) · 색을 1색이라도 같이 가지면 같은 색 (공식 Q&amp;A P-206 Q1)
///  - BT23-060 "Activate 1 [On Play] effect on a face-up [Zaxon] trait Digimon card in your security stack as an effect of this Digimon" —
///    시큐리티의 앞면 카드의 【등장 시】 효과 1개를 이 디지몬의 효과로서 (공식 한글 BT23-060) · 없어도 [턴에 1회]는 쓴다 (공식 Q&amp;A BT23-060 Q1)
///  - 같은 뜻 다른 표기 (ClauseRewrite74): BT11-097 · AD1-009 · EX8-043 · BT26-047 · EX1-071
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I74 = RegexOptions.IgnoreCase;

    /// <summary>
    /// 영어 API 문장이 공식 영어 카드 리스트(world.digimoncard.com)와 다른 절 → 공식 문장 (카드 번호 · API 문장이 정확히 같을 때만 — OfficialClause72 다음).
    /// BT23-033 (링크 효과): API "~ or affect it with effects." — 공식 "~ or affect it with ＜De-Digivolve＞ effects." (공식 한글 BT23-033 〈링크〉 【링크 시】도 《퇴화》) ·
    /// BT24-056: API 끝에 다른 문장 조각 " with the [Appmon] trait from your trash without paying the cost."이 붙어 있다 — 공식은 "~ to hands or decks."에서 끝난다
    /// </summary>
    private static readonly Dictionary<(string Id, string Api), string> OfficialClauses74 = new()
    {
        [("BT23-033", "Until your opponent's turn ends, their effects can't return this Digimon to hands or decks or affect it with effects.")] =
            "Until your opponent's turn ends, their effects can't return this Digimon to hands or decks or affect it with ＜De-Digivolve＞ effects.",
        [("BT24-056", "[On Play] [When Digivolving] Until your opponent's turn ends, their effects can't return 1 of your Digimon with the [System], [Life] or [Transmutation (App Name)] trait to hands or decks. with the [Appmon] trait from your trash without paying the cost.")] =
            "[On Play] [When Digivolving] Until your opponent's turn ends, their effects can't return 1 of your Digimon with the [System], [Life] or [Transmutation (App Name)] trait to hands or decks.",
    };

    internal static string OfficialClause74(CardData card, string clause) =>
        OfficialClauses74.TryGetValue((card.CardId, clause.Trim()), out var official) ? official : OfficialClause75(card, clause);   // v0.75.0 (SentenceParser.Batch75)

    private static string? TryParse74(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse73(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만: 같은 뜻의 다른 표기로 다시 읽는다
        string alt = ClauseRewrite74(rawClause);
        if (alt == rawClause || TryParse73(alt, inherited, card, null) != null) return err;
        if (def != null) TryParse73(alt, inherited, card, def);
        return null;
    }

    /// <summary>같은 뜻 다른 표기 (렌더러도 쓴다)</summary>
    internal static string ClauseRewrite74(string clause)
    {
        string t = clause;
        // BT11-097 "activate 1 of the [On Deletion] effects of 1 of your red Digimon with the [Vaccine] trait" — 공식 한글 BT11-097과 같은 뜻 (그 디지몬의 【소멸 시】 효과 1개)
        t = Regex.Replace(t, @"\bactivate 1 of the (?<x>\[[^\]]+\]) effects of (?=1 of your )", "activate 1 ${x} effect of ", I74);
        // AD1-009 "Then, until your opponent's turn ends, their Digimon's effects don't affect this Digimon and 1 of your Digimon with [Garurumon] in its name."
        //  — 이 디지몬과 다른 디지몬 1마리 (여러 대상을 동시에 고를 때 같은 대상은 고를 수 없다 — 종합 룰 15-10-2-3)
        t = Regex.Replace(t, @"(?<then>Then, )(?<d>until your opponent's turn ends), their Digimon's effects don't affect this Digimon and 1 of your Digimon (?<w>with [^.]+?)\.?$",
            "${then}${d}, their Digimon's effects don't affect this Digimon. Then, ${d}, their Digimon's effects don't affect 1 of your other Digimon ${w}.", I74);
        // EX8-043 "Then, if this Digimon is suspended, ＜De-Digivolve 1＞ 1 of your opponent's Digimon and, until the end of your opponent's turn,
        //  their effects can't return this Digimon to hands or decks or affect it with ＜De-Digivolve＞ effects." — 공식 한글 EX8-043 (조건은 둘 다에)
        t = Regex.Replace(t, @"(?<c>Then, if this Digimon is suspended, )(?<dd>＜De-Digivolve \d+＞ 1 of your opponent's Digimon) and, (?<d>until the end of your opponent's turn), their effects can't return this Digimon to hands or decks or affect it with ＜De-Digivolve＞ effects\.?$",
            "${c}${dd}. ${c}${d}, this Digimon can't be returned to hands or decks by your opponent's effects and isn't affected by your opponent's ＜De-Digivolve＞ effects.", I74);
        // BT23-033 (링크 효과 — 공식 영어로 고친 문장) "Until your opponent's turn ends, their effects can't return this Digimon to hands or decks or affect it with ＜De-Digivolve＞ effects."
        //  — BT21-074와 같은 뜻, 대상은 이 디지몬 (링크한 디지몬)
        t = Regex.Replace(t, @"^(?<d>Until [^,]+, )their effects can't return this Digimon to hands or decks or affect it with ＜De-Digivolve＞ effects\.?$",
            "${d}this Digimon can't be returned to hands or decks by your opponent's effects and isn't affected by your opponent's ＜De-Digivolve＞ effects.", I74);
        // BT26-047 "By suspending 1 Digimon, until your opponent's turn ends, none of your suspended [Insectoid] or [Titan] trait Digimon are affected by your opponent's Option effects, and they get +3000 DP."
        //  — 효과를 처리할 때 레스트 상태인 그 디지몬 전부 (기간이 있는 효과의 대상은 처리할 때 정한다)
        t = Regex.Replace(t, @"(?<pre>By [^,]+, )(?<d>until your opponent's turn ends), none of your suspended (?<x>.+? trait) Digimon are affected by your opponent's Option effects, and they get \+(?<n>\d+) DP\.?$",
            "${pre}${d}, their Option effects don't affect all of your suspended ${x} Digimon. Then, all of your suspended ${x} Digimon get +${n} DP until your opponent's turn ends.", I74);
        // EX1-071 "you may trash 1 Digimon card in your hand of the same color as the digivolving Digimon to reduce the memory cost of the digivolution by 4"
        t = Regex.Replace(t, @"(?<head>the next time one of your Digimon would digivolve this turn), you may trash (?<p>1 Digimon card in your hand of the same colou?r as the digivolving Digimon) to (?<r>reduce the (?:memory |digivolution )?cost of the digivolution by \d+)\.?$",
            "${head}, by trashing ${p}, ${r}.", I74);
        return t;
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction74(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I74);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;
        // BT5-109 "At the end of the turn, return the Digimon that digivolved with this effect to the bottom of its owner's deck"
        if (Regex.IsMatch(s, @"^At the end of (?:the|this) turn, return the Digimon that digivolved (?:with|by) this effect to the bottom of (?:its owner's|your|the) deck$", I74))
            return (c =>
            {
                if (!c.Engine.MarkTurnEvoReturn74(c.Owner, c.Card)) { c.LastStepResolved = false; return; }
                Emit(c, label, "이 턴 종료 시 이 효과로 진화한 디지몬을 덱 아래로 (진화원은 파기)");
            }, null);
        // BT23-060 "Activate 1 [On Play] effect on a face-up [Zaxon] trait Digimon card in your security stack as an effect of this Digimon"
        if ((m = Regex.Match(s, @"^(?<may>you may )?activate 1 (?<trig>\[[^\]]+\]) effect on a face[- ]up (?<p>.+?) in your security(?: stack)? as an effect of this Digimon$", I74)).Success
            && ActivateTriggers41.TryGetValue(m.Groups["trig"].Value.Trim('[', ']'), out var trig)
            && ParseCardPick("1 " + m.Groups["p"].Value) is { Count: 1 } pk)
        {
            var match = pk.Match;
            bool may = m.Groups["may"].Success;
            var trigs = new HashSet<EffectTrigger> { trig };
            return (c => ActivateFaceUpSecurity74(c, match, trigs, may, label), null);
        }
        return null;
    }

    /// <summary>EX1-071 비용: "trashing 1 Digimon card in your hand of the same color as the digivolving Digimon" — 진화하는 디지몬 = 진화 코스트 감소의 Other</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost74(string cost, CardData card)
    {
        if (!Regex.IsMatch(cost.Trim(), @"^trashing 1 Digimon card in your hand of the same colou?r as the digivolving Digimon$", I74)) return null;
        static List<CardData> Pool(EffectContext c)
        {
            if (c.Other == null) return new List<CardData>();
            var colors = c.Other.Colors;   // 배틀 에어리어에 있는 진화하는 디지몬의 지금 색 (공식 Q&A EX1-071 Q1)
            return c.Owner.Hand.Where(x => x.Type == CardType.Digimon && !ReferenceEquals(x, c.TriggerCard) && x.AllColors.Any(colors.Contains)).ToList();   // 진화할 카드 자신은 빼고   // 1색이라도 같으면 (공식 Q&A P-206 Q1)
        }
        return (c => Pool(c).Count > 0,
                c =>
                {
                    var pool = Pool(c).OrderBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
                    var x = pool.Count == 1 ? pool[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: false, $"[{c.Card.Name}] 비용으로 버릴, 진화하는 디지몬과 같은 색의 디지몬 카드를 고르세요.").FirstOrDefault() ?? pool[0];
                    c.Owner.Hand.Remove(x);
                    c.Owner.Trash.Add(x);
                    c.PaidCount = 1;
                    c.Engine.NotifyHandTrashed(c.Owner, new List<CardData> { x });
                    Diag46.Hit("sameColorHandTrash74");
                },
                c => c.Owner.Hand.Count >= 2,
                "패에서 진화하는 디지몬과 같은 색의 디지몬 카드 1장을 버려");
    }

    private static void ActivateFaceUpSecurity74(EffectContext c, Func<CardData, bool> match, IReadOnlySet<EffectTrigger> trigs, bool may, string label)
    {
        var self = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
        var cands = GoldfishEngine.FaceUpCards(c.Owner).Where(x => x.Type == CardType.Digimon && match(x) && GoldfishEngine.HasEffectsFor41(x, trigs)).Distinct().ToList();
        if (self == null || cands.Count == 0) { c.LastStepResolved = false; Emit(c, label, "발휘할 앞면 시큐리티의 효과가 없습니다"); return; }
        if (may && !c.Engine.AskYesNo(c.Owner, self, $"[{c.Card.Name}] 효과를 발휘할까요?", recommended: true)) { c.LastStepResolved = false; return; }
        int pick = cands.Count == 1 ? 0 : c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 효과를 발휘할 앞면 시큐리티 카드를 고르세요.", cands.Select(x => x.Name).ToList(), 0);
        var card = cands[Math.Clamp(pick, 0, cands.Count - 1)];
        int ran = c.Engine.ActivateEffectsAs41(c.Owner, self, card, trigs, true);
        if (ran > 0) Diag46.Hit("faceUpSecurityActivate74");
        else c.LastStepResolved = false;
        Emit(c, label, ran > 0 ? $"앞면 시큐리티 [{card.Name}]의 효과를 이 디지몬의 효과로서 발휘했습니다" : "발휘할 효과가 없습니다");
    }
}
