using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.72.0 미지원 문장 46차 묶음:
///  - 특징 "가진"(일치) · "포함하는"(포함) 구분 (종합 룰 2-3-2-3 · 2-3-2-4): "[X] trait" · "with the [X] trait"는 특징이 X와 일치하는 카드,
///    "with [X] in any of its traits"는 X를 포함하는 특징을 가진 카드. 전에는 둘 다 포함으로 봐서 [Machine] trait가 Machine Dragon에,
///    [Angel] trait가 Archangel · Fallen Angel에, [Rock] trait가 Rock Dragon에, [Demon] trait가 Demon Lord에 맞았다.
///    문장을 읽기 전에 "[X] trait"의 X 끝에 일치 표식(CardNames.TraitExactMark72)을 붙인다 — 약칭 특징(71차에서 이미 일치)은 그대로.
///    카드마다 공식 한글 문구가 다른 것: BT13-050 (영어 "[Fairy] trait trait" · 공식 한글 "특징으로 「요정형」을 포함하는") — 포함
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>영어 "[X] trait"이지만 공식 한글이 "특징으로 「X」를 포함하는"인 카드 (카드 번호 → 포함으로 읽을 특징)</summary>
    private static readonly Dictionary<string, string[]> TraitContainsKo72 = new(StringComparer.Ordinal)
    {
        ["BT13-050"] = new[] { "Fairy" },
    };

    // "[A] trait" · "[A] or [B] trait" · "[A], [B] or [C] traits" · "[A]/[B] trait" — 목록 바로 뒤에 trait (사이에 "in any of its"가 없는 것)
    private static readonly Regex TraitList72 = new(@"(?<l>\[[^\[\]]+\](?:(?:,? (?:or|and|and/or) |, |/)\[[^\[\]]+\])*)(?=\s*traits?\b)", RegexOptions.IgnoreCase);

    /// <summary>
    /// 영어 API 문장이 공식 영어 카드 리스트(world.digimoncard.com)와 다른 절 → 공식 문장 (카드 번호 · API 문장이 정확히 같을 때만).
    /// EX11-033 · EX11-042: API "~ from your hand or this Digimon's digivolution cards to 1 of your Digimon ~" (뜻이 통하지 않는 문장) —
    /// 공식 "You may play 1 [Maquinamon] from your hand or this Digimon's link cards without paying the cost." (EX11-033의 시점은 공식 [When Moving] [When Digivolving])
    /// </summary>
    private static readonly Dictionary<string, (string Api, string Official)> OfficialClauses72 = new(StringComparer.Ordinal)
    {
        ["EX11-033"] = ("[On Play] [When Digivolving] You may play 1 [Maquinamon] from your hand or this Digimon's digivolution cards to 1 of your Digimon without paying the cost.",
                        "[When Moving] [When Digivolving] You may play 1 [Maquinamon] from your hand or this Digimon's link cards without paying the cost."),
        ["EX11-042"] = ("[On Play] [When Digivolving] You may play 1 [Maquinamon] from your hand or this Digimon's digivolution cards to 1 of your Digimon without paying the cost.",
                        "[On Play] [When Digivolving] You may play 1 [Maquinamon] from your hand or this Digimon's link cards without paying the cost."),
    };

    internal static string OfficialClause72(CardData card, string clause) =>
        OfficialClauses72.TryGetValue(card.CardId, out var f) && clause.Trim() == f.Api ? f.Official : OfficialClause74(card, clause);   // v0.74.0 (SentenceParser.Batch74)

    // "(other than [X] trait)" · "other than [A] or [B] trait" — 제외할 특징은 원래부터 일치로 비교한다 (표식을 붙이지 않는다)
    private static readonly Regex OtherThan72 = new(@"other than (?:the |cards? with (?:the )?)?$", RegexOptions.IgnoreCase);

    /// <summary>"[X] trait"의 X에 특징 일치 표식을 붙인다 (약칭 · 표식이 이미 있는 것 · 카드별 예외는 그대로)</summary>
    internal static string MarkExactTraits72(string clause, CardData card)
    {
        if (clause.IndexOf("trait", StringComparison.OrdinalIgnoreCase) < 0) return clause;
        TraitContainsKo72.TryGetValue(card.CardId, out var keep);
        return TraitList72.Replace(clause, m => OtherThan72.IsMatch(clause[..m.Index]) ? m.Value : Regex.Replace(m.Groups["l"].Value, @"\[(?<x>[^\[\]]+)\]", x =>
        {
            string name = x.Groups["x"].Value;
            if (name.Length == 0 || name[^1] == CardNames.TraitExactMark72 || TargetSpec.IsAbbrev72(name)) return x.Value;
            if (keep != null && keep.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))) return x.Value;
            return "[" + name + CardNames.TraitExactMark72 + "]";
        }));
    }

    private static string? TryParse72(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string marked = MarkExactTraits72(rawClause, card);
        string? err = TryParse71(marked, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만: 설명문 괄호 뒤에 마침표가 빠진 문장 "＜Draw 1＞ (Draw 1 card from your deck.) If deleted by ~" (EX11-060)
        string alt = ClauseRewrite72(marked);
        if (alt == marked || TryParse71(alt, inherited, card, null) != null) return err;
        if (def != null) TryParse71(alt, inherited, card, def);
        return null;
    }

    /// <summary>같은 뜻 다른 표기 (렌더러도 쓴다): 설명문 괄호 "(~.)" 바로 뒤에 "If ~"가 이어지면 문장을 나눈다</summary>
    internal static string ClauseRewrite72(string clause) =>
        Regex.Replace(
            Regex.Replace(clause, @"(?<=\.\))\s+(?=If\b)", ". "),
            // EX11-006 "This Digimon linked with [Maquinamon] may digivolve into ~" — 링크 카드에 그 명칭이 있는 이 디지몬
            @"(?<=^(?:Inherited Effect\s*)?(?:\[[^\]]+\]\s*)*)This Digimon linked with (?<x>\[[^\]]+\]) may ", "If this Digimon is linked with ${x}, this Digimon may ", RegexOptions.IgnoreCase);

    // ---- 조건 (원래 규칙이 읽지 못한 것만 — ParseEventConditionChain 끝) ----

    private static Func<EffectContext, bool>? ParseEventCondition72(string cond)
    {
        cond = cond.Trim();
        // EX11-058 "If played by ＜Decode＞" — 그 디지몬이 ≪디코드≫로 진화원에서 등장했는지
        if (Regex.IsMatch(cond, @"^(?:it was |they were |that Digimon was )?played by ＜Decode＞$", RegexOptions.IgnoreCase))
            return c => c.Engine.PlayedByDecode72(c.Other);
        // EX11-060 "If deleted by ＜Overclock＞" — 그 디지몬이 ≪오버클럭≫의 비용으로 소멸했는지
        if (Regex.IsMatch(cond, @"^(?:it was |they were |that Digimon was )?deleted by ＜Overclock＞$", RegexOptions.IgnoreCase))
            return c => c.Engine.DeletedByOverclock72(c.Other);
        // EX11-006 "this Digimon is linked with [Maquinamon]" — 이 디지몬의 링크 카드에 명칭이 그 카드인 것이 있다 (명칭 일치 — 종합 룰 2-3-1-2)
        if (Regex.Match(cond, $@"^this Digimon is linked with (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase) is { Success: true } lk)
        {
            var xs = TargetSpec.Names(lk.Groups["x"].Value);
            return c => c.Self != null && c.Self.LinkedCards.Any(l => xs.Any(x => CardNames.Is(l, x)));
        }
        // BT22-092 "If this activated any effect" — 공식 "이 효과로 발휘했다면" (앞 문장이 효과를 발휘했는지)
        if (Regex.IsMatch(cond, @"^this activated any effects?$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved;
        return null;
    }

    // ---- 수 (원래 규칙이 읽지 못한 것만 — ParseCounter 끝) ----

    /// <summary>"your opponent's face-up security cards" · "your face-up security cards" — 시큐리티에 앞면으로 있는 카드 (EX11-064)</summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter72(string counter)
    {
        var m = Regex.Match(counter.Trim(), @"^(?:of )?your (?<opp>opponent's )?face-up security cards?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        bool opp = m.Groups["opp"].Success;
        return (o, e, _) => { var z = opp ? e : o; return z.SecurityStack.Count(z.FaceUpSecurity.Contains); };
    }

    // ---- 동작 (원래 규칙이 읽지 못한 것만) ----

    private static (Action<EffectContext>? Act, string? Error)? ParseAction72(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        // BT22-092 "activate 1 of those Digimon's [Main] effects" — 공식 "그 디지몬의 【메인】 효과 1개를 발휘한다" (진화원 효과의 【메인】도 — 공식 Q&A BT22-092 Q1)
        if (Regex.IsMatch(s, @"^activate 1 of (?:those|that|these) Digimon's \[Main\] effects?$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var d = c.Other != null && c.Owner.BattleArea.Contains(c.Other) ? c.Other : null;
                bool ran = d != null && c.Engine.ActivateMainOf72(c.Owner, d);
                c.LastStepResolved = ran;
                if (!ran) c.Log($"[{c.Card.Name}] 발휘할 【메인】 효과가 없습니다.");
            }, null);
        // EX11-033 · EX11-042 (공식 영어) "play 1 [Maquinamon] from your hand or this Digimon's link cards without paying the cost" — 패 또는 이 디지몬의 링크 카드에서 1장
        Match m;
        if ((m = Regex.Match(s, @"^play (?<p>1 .+?) from your hand or this Digimon's link cards without paying (?:the|its) (?:play |memory )?costs?$", RegexOptions.IgnoreCase)).Success
            && (ParseNamePick(m.Groups["p"].Value) ?? ParseCardPick(m.Groups["p"].Value)) is { Count: 1 } pick)
        {
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            var match = pick.Match;
            return (c => PlayHandOrLink72(c, match, label), null);
        }
        return null;
    }

    private static void PlayHandOrLink72(EffectContext c, Func<CardData, bool> match, string label)
    {
        var self = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
        if (c.Owner.BattleArea.Count >= 12) { c.LastStepResolved = false; return; }
        var pool = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand))
            .Concat(self == null ? Enumerable.Empty<CardData>() : self.LinkedCards.Where(x => x.Type == CardType.Digimon && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Unknown)))
            .Distinct(ReferenceEqualityComparer.Instance).Cast<CardData>().ToList();
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 카드가 없습니다"); return; }
        var x = pool.Count == 1 ? pool[0] : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: false, $"[{c.Card.Name}] 패 또는 링크 카드에서 등장시킬 카드를 고르세요.").FirstOrDefault() ?? pool[0];
        bool fromLink = self != null && self.LinkedCards.Any(y => ReferenceEquals(y, x));
        bool ok = fromLink
            ? c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Unknown, () => self!.LinkedCards.Remove(x))
            : c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Hand, () => c.Owner.Hand.Remove(x));
        if (!ok) { c.LastStepResolved = false; return; }
        c.RecalculateFieldBuffs();
        Emit(c, label, $"{(fromLink ? "링크 카드" : "패")}의 [{x.Name}] 코스트 없이 등장");
    }

    /// <summary>공개한 카드를 놓는 곳 — "as any of those Digimon's bottom digivolution cards" (EX11-066 — 사건의 그 디지몬)</summary>
    private static Func<EffectContext, CardData, bool>? RevealPlace72(string where)
    {
        if (!Regex.IsMatch(where.Trim(), @"^as (?:any of |1 of |one of )?(?:those|these|that) Digimon's bottom digivolution cards?$", RegexOptions.IgnoreCase)) return null;
        return (c, x) =>
        {
            if (c.Other == null || !c.Owner.BattleArea.Contains(c.Other)) return false;
            c.Other.AddBottomSource(x);
            return true;
        };
    }

    private static string? TryParseChoice72(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def) =>
        TryParseChoice64(MarkExactTraits72(rawHeader, card), rawOptions.Select(o => MarkExactTraits72(o, card)).ToList(), inherited, card, def);
}
