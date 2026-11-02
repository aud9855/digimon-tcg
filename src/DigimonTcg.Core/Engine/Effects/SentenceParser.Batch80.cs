using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.80.0 미지원 문장 54차 묶음 (원래 규칙이 읽지 못한 절만):
///  - BT26-078 [트래시] "~ When any of your ~ Digimon are played, ~, 1 of them gains ＜Rush＞ and ＜Execute＞ for the turn" — "1 of them" = 등장한 디지몬 중 1마리
///    (BT23-072 "1 of the played Digimon gains ~"와 같은 처리 — ≪에그제큐트≫ 종합 룰 16-38)
///  - BT23-024 "other than their highest play cost Digimon, none of your opponent's Digimon can suspend until their turn ends" — 대상을 고르지 않는 전체 처리
///    (종합 룰 15-11-2-2 나중에 늘어난 대상에게도 · 15-11-2-3-2 · 15-11-2-3-3 조건은 그때그때): "가장 등장 코스트가 높은 상대의 디지몬"은 지금 상대의 배틀 에어리어에서
///    가장 높은 것 (같으면 모두) — 그 디지몬이 떠나거나 더 높은 디지몬이 나오면 그때 대상이 바뀐다
///  - BT16-015 "[Your Turn] While [Phoenixmon] or [X Antibody] is in this Digimon's digivolution cards, attach [End of Attack] to all of this Digimon's [On Deletion] effects"
///    — 이 디지몬의 【소멸 시】 효과(진화원 효과 포함)가 【어택 종료 시】에도 유발한다. 조건이 있는 효과는 조건을 만족해야 발휘 (공식 Q&amp;A BT16-015 Q1)
///  - BT7-112 "By placing 10 Tamer cards and/or cards with the [Hybrid] trait from your hand and/or trash at the bottom of your deck in any order, you may digivolve this card
///    from your hand onto one of your Tamers as if the Tamer is a level 6 Digimon" — 진화를 선언하고 진화시킬 카드를 고르기 전에 비용 (공식 Q&amp;A BT7-112 Q1),
///    진화 코스트는 Lv.6에서의 진화 코스트 (공식 영어 카드 리스트 "7 from Lv.6")
///  - BT10-077 "[Opponent's Turn] [Once Per Turn] When an effect adds cards to your opponent's hand, by choosing and trashing 1 of this Digimon's digivolution cards,
///    your opponent trashes cards from their hand equal to the number of cards added to their hand by the effect" — 효과 하나마다 늘어난 장수
///    (공식 Q&amp;A BT10-077 Q1 · EX7-013 Q1 — 드로우도, 동시에 유발한 다른 효과의 몫은 따로). 이 카드만 새 사건으로 — 같은 사건의 다른 카드는 그대로
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I80 = RegexOptions.IgnoreCase;

    internal const string NotHighestCostMarker80 = "other than their highest play cost Digimon";
    internal const string OppHandAddEventMarker80 = "⟦opphandcount80⟧";
    internal const string TrashAddedCountMarker80 = "⟦trashaddedcount80⟧";
    internal const string AttachEndOfAttackKeyword80 = "AttachEndOfAttackInternal";   // 숫자 없이 (ParseKeywordList)

    /// <summary>BT16-015 "[Your Turn] While ~, attach [End of Attack] to all of this Digimon's [On Deletion] effects."</summary>
    internal static readonly Regex AttachEndOfAttackRe80 = new(@"^(?<h>\[Your Turn\]\s*)While (?<cond>.+?), attach \[End of Attack\] to all of this Digimon's \[On Deletion\] effects\.?$", I80 | RegexOptions.Compiled);

    /// <summary>BT7-112 "By placing 10 Tamer cards and/or cards with the [Hybrid] trait from your hand and/or trash at the bottom of your deck in any order, you may digivolve ~ as if the Tamer is a level 6 Digimon."</summary>
    internal static readonly Regex TamerEvoPlaceRe80 = new(@"^By placing (?<n>\d+) Tamer cards and/or cards with the \[(?<t>[^\]]+)\] trait from your hand and/or trash at the bottom of your deck in any order, you may digivolve this card from your hand onto one of your Tamers as if the Tamer is a level (?<lv>\d+) Digimon\.?$", I80 | RegexOptions.Compiled);

    /// <summary>원래 규칙이 읽지 못한 절 → 같은 뜻의 다른 표기 (바꾸지 못하면 null)</summary>
    internal static string? Rewrite80(string rawClause)
    {
        string t = Normalize58(rawClause);
        string a = t;
        // BT26-078 "When any of your ~ Digimon are played, ~, 1 of them gains ~" — 등장한 디지몬 중 1마리
        if (Regex.IsMatch(a, @"\bWhen any of your [^.]*?Digimon are played,", I80))
            a = Regex.Replace(a, @"(?<=, )1 of them gains\b", "1 of the played Digimon gains", I80);
        // BT23-024 "other than their highest play cost Digimon, none of your opponent's Digimon can suspend ~" — 대상 뒤로
        string b24 = Regex.Replace(a, @"(?<=, )other than their highest play cost Digimon, none of (?<t>your opponent's Digimon) (?<v>can(?:'t|not)? (?:un)?suspend)\b",
            m => $"none of {m.Groups["t"].Value} {NotHighestCostMarker80} {m.Groups["v"].Value}", I80);
        // 같은 문장의 "When this Digimon gets linked, by unsuspending it," — "it" = 이 디지몬 (사건의 디지몬)
        if (b24 != a) a = Regex.Replace(b24, @"(?<=\bWhen this Digimon [^,]+, by )unsuspending it,", "unsuspending this Digimon,", I80);
        // BT16-015 → "this Digimon gains ＜AttachEndOfAttackInternal＞" (내부 키워드 — 엔진이 어택 종료 시에 【소멸 시】 효과도 발휘)
        a = AttachEndOfAttackRe80.Replace(a, m => $"{m.Groups["h"].Value}While {m.Groups["cond"].Value}, this Digimon gains ＜{AttachEndOfAttackKeyword80}＞.");
        // BT10-077 "When an effect adds cards to your opponent's hand, ~, your opponent trashes cards from their hand equal to the number of cards added to their hand by the effect."
        var h = Regex.Match(a, @"^(?<h>(?:\[[^\]]+\]\s*)*)When an effect adds cards to your opponent's hand, (?<cost>by [^,]+, )?your opponent trashes cards from their hand equal to the number of cards added to their hand by the effect\.?$", I80);
        if (h.Success)
            a = $"{h.Groups["h"].Value}When {OppHandAddEventMarker80}, {h.Groups["cost"].Value}{TrashAddedCountMarker80}.";
        return a == t ? null : a;
    }

    private static string? TryParse80(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse79(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만
        string t = Normalize58(rawClause);
        // BT7-112 테이머에서 진화 + 비용 (패 · 트래시의 카드 N장을 덱 아래로)
        if (!inherited && card.Type == CardType.Digimon && TamerEvoPlaceRe80.Match(t) is { Success: true } te)
        {
            int n = int.Parse(te.Groups["n"].Value), lv = int.Parse(te.Groups["lv"].Value);
            string trait = te.Groups["t"].Value;
            if (def != null)
            {
                // "as if the Tamer is a level 6 Digimon" — 색은 말하지 않는다: 이 카드의 Lv.6에서의 진화 조건 (TamerEvolutionCost는 진화 조건의 색과 같아야 한다)
                def.TamerEvolution(_ => true, lv, card.EvoRequirementColor, null);
                def.Script41.TamerEvoPlaceToDeck80 = (n, c => c.Type == CardType.Tamer || TargetSpec.HasTrait(c, trait));
                Diag46.Hit("tamerEvoPlace80");
            }
            return null;
        }
        if (Rewrite80(rawClause) is not { } alt || TryParse79(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse79(alt, inherited, card, def);
            Diag46.Hit("rewrite80");
        }
        return null;
    }

    /// <summary>v0.80.0 사건: "⟦opphandcount80⟧" = 효과 하나로 상대의 패가 늘어났을 때 (BT10-077)</summary>
    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase80(string ev) =>
        ev.Trim() == OppHandAddEventMarker80 ? (new List<EffectTrigger> { EffectTrigger.OpponentHandAddedCount80 }, null) : null;

    private static (Action<EffectContext>? Act, string? Error)? ParseAction80(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I80);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        // BT10-077 "⟦trashaddedcount80⟧" — 상대는 그 효과로 늘어난 장수만큼 패를 파기 (상대가 고른다)
        if (s == TrashAddedCountMarker80 && triggers.Contains(EffectTrigger.OpponentHandAddedCount80))
            return (c =>
            {
                int n = c.Engine.OppHandAddedCount80;
                int done = n > 0 ? c.Engine.DiscardFromHand(c.Enemy, n) : 0;
                if (done == 0) c.LastStepResolved = false;
                Diag46.Hit("trashHandAddedCount80");
                Emit(c, label, $"효과로 늘어난 {n}장 — 상대 패 {done}장 파기");
            }, null);
        return null;
    }
}
