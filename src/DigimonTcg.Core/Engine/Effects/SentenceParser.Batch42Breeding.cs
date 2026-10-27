using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.42.0 [육성] 효과 (종합 룰: 육성 에어리어에 있는 동안만 발휘하는 효과 — 공식 BT13-007 "[육성]【자신의 턴】자신의 디지몬 전부는 진화할 수 없다"):
/// "[Breeding]"을 뗀 문장을 원래 규칙으로 읽고, 등록한 효과의 출처를 EffectSource.Breeding42 (맨 위 카드) · BreedingInherited42 (진화원)로 바꾼다.
///  - 시점 효과: 엔진이 육성 에어리어의 디지몬으로 처리하는 시점만 ([자신의 턴 개시 시] · [자신의 메인 페이즈 개시 시] · [턴 종료 시] · [진화 시](육성 에어리어에서 진화) · FireForSide 사건)
///  - 지속 효과: 다른 디지몬에게 주는 효과만 (AllyKeyword · AllyDigimonDp …) — 이 디지몬 자신의 DP · 키워드는 육성 에어리어에서 쓰지 않는다
/// 엔진: GoldfishEngine.Batch42 (RunBreedingEffects42 · BreedingAuras42)
/// </summary>
internal static partial class SentenceParser
{
    private static readonly HashSet<EffectTrigger> BreedingTimed42 = new()
    {
        EffectTrigger.StartOfYourTurn, EffectTrigger.StartOfYourMainPhase, EffectTrigger.EndOfYourTurn, EffectTrigger.EndOfOpponentsTurn, EffectTrigger.WhenDigivolving,
    };

    /// <summary>엔진이 FireForSide로 알리는 사건 (육성 에어리어의 [육성] 효과도 처리한다)</summary>
    private static readonly HashSet<EffectTrigger> BreedingEvents42 = new()
    {
        EffectTrigger.AllyDigimonDigivolved, EffectTrigger.OpponentTamerPlayed, EffectTrigger.OpponentDigimonAttacks, EffectTrigger.AllyTamerPlayed, EffectTrigger.AllyDigimonAttacks,
        EffectTrigger.OpponentDigimonPlayed, EffectTrigger.AllyDigimonPlayed, EffectTrigger.OpponentDigimonDeleted, EffectTrigger.AllyDigimonDeleted,
        EffectTrigger.OpponentSecurityRemoved, EffectTrigger.AllySecurityRemoved, EffectTrigger.AllySecurityAdded, EffectTrigger.OpponentSecurityAdded,
        EffectTrigger.AllyOptionUsed, EffectTrigger.OpponentOptionUsed, EffectTrigger.AllyOptionPlaced, EffectTrigger.OpponentDigimonDigivolved,
        EffectTrigger.AllyDigimonSuspended, EffectTrigger.OpponentDigimonSuspended, EffectTrigger.AllyDigimonUnsuspended, EffectTrigger.OpponentDigimonUnsuspended,
        EffectTrigger.AllyDigimonLeft, EffectTrigger.OpponentDigimonLeft, EffectTrigger.AllyHatched, EffectTrigger.AllyDigimonMovedFromBreeding,
    };

    private static readonly HashSet<StaticStat> BreedingAuraStats42 = new()
    {
        StaticStat.AllyKeyword, StaticStat.AllyDigimonDp, StaticStat.AllySecurityAttack, StaticStat.EnemySecurityAttack, StaticStat.SecurityDigimonDp, StaticStat.EnemySecurityDigimonDp, StaticStat.EnemyKeyword,
    };

    private static bool BreedingClause42Impl(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        if (card.Type is not (CardType.Digimon or CardType.Digitama)) return false;
        string c0 = Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", I);
        var m = Regex.Match(c0, @"^\[Breeding\]\s*(?<rest>.+)$", I | RegexOptions.Singleline);
        if (!m.Success) return false;
        string rest = m.Groups["rest"].Value.Trim();
        if (rest.Length == 0 || !rest.StartsWith("[")) return false;
        // 임시 스크립트로 읽어 등록한 것을 본다
        var tmpCard = new CardData
        {
            CardId = card.CardId + "#breed42", Name = card.Name, Type = card.Type, Level = card.Level, Color = card.Color, SecondaryColor = card.SecondaryColor,
            Traits = card.Traits, EffectText = card.EffectText, InheritedEffectText = card.InheritedEffectText, PlayCost = card.PlayCost, DP = card.DP,
        };
        var tdef = new CardScriptBuilder().Card(tmpCard.CardId);
        if (TryParse41(rest, inherited, tmpCard, null) != null) return false;
        TryParse41(rest, inherited, tmpCard, tdef);
        var s = tdef.Script41;
        bool onlyEffectsAndStatics = s.Keywords.Count + s.InheritedKeywords.Count == 0 && s.SpecialEvolutions.Count == 0 && s.EvoCostModifiers.Count == 0 && s.FieldPlayCostModifiers.Count == 0
            && s.PlayCostOptions.Count == 0 && s.PlayCostReduction == null && s.TamerAuras.Count == 0 && s.GainedSourceEffects.Count == 0 && s.EvoOnlyInto.Count == 0;
        if (!onlyEffectsAndStatics || s.Effects.Count + s.Statics.Count == 0) return false;
        if (s.Effects.Any(e => !(BreedingTimed42.Contains(e.Trigger) || BreedingEvents42.Contains(e.Trigger)))) return false;
        if (s.Statics.Any(e => !BreedingAuraStats42.Contains(e.Stat))) return false;
        if (def != null)
        {
            var target = def.Script41;
            foreach (var e in s.Effects)
                target.Effects.Add(e with { Source = e.Source == EffectSource.Inherited ? EffectSource.BreedingInherited42 : EffectSource.Breeding42 });
            foreach (var st in s.Statics)
                target.Statics.Add(st with { Source = st.Source == EffectSource.Inherited ? EffectSource.BreedingInherited42 : EffectSource.Breeding42 });
        }
        return true;
    }
}
