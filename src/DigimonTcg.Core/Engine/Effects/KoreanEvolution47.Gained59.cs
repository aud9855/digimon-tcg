using System;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.59.0 진화 조건의 특징: 필드의 디지몬이 지속 효과로 얻은 특징도 그 디지몬의 특징이다 (EX7-010 "[Your Turn] This Digimon gains the [Three Musketeers] trait").
/// 종합 룰 3-4-4 · 3-4-5 — 에어리어의 카드는 1개의 존재(그 디지몬)로 취급하고, 진화는 "에어리어의 자신의 카드"를 진화시킨다(6-5-1-2).
/// 효과가 그 디지몬에게 준 특징은 그 디지몬을 참조하는 모든 판단(진화 조건 포함)에 쓰인다. 패 · 트래시의 카드는 얻지 않는다
/// </summary>
public static partial class KoreanEvolution47
{
    /// <summary>그 디지몬(맨 위 카드가 c)이 얻은 특징 x — 영어 이름 일치 또는 한글 표기 일치</summary>
    private static bool Gained59(CardInstance? d, CardData c, string x, bool exact = false)
    {
        if (d == null || d.ExtraTraits58.Count == 0 || !ReferenceEquals(d.TopCard, c)) return false;
        string nx = Norm(x);
        foreach (var t in d.ExtraTraits58)
        {
            if (exact ? t.Equals(x, StringComparison.OrdinalIgnoreCase) : t.Contains(x, StringComparison.OrdinalIgnoreCase)) return Diag46.HitTrue("gainedTraitEvo59");
            if (Norm(KoreanEffectRenderer.TraitKo(t)) == nx) return Diag46.HitTrue("gainedTraitEvo59");
        }
        return false;
    }

    private static bool TraitIs59(CardData c, string x, CardInstance? d) => TraitIs(c, x) || Gained59(d, c, x, exact: true);

    private static bool TraitIsEn59(CardData c, string x, CardInstance? d) => TraitIsEn(c, x) || Gained59(d, c, x, exact: true);
}
