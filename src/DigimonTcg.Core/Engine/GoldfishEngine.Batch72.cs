using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.72.0 미지원 문장 46차 묶음 (엔진):
///  - "If played by ＜Decode＞" (EX11-058) — ≪디코드≫로 진화원에서 등장한 디지몬 (종합 룰 16-? ≪디코드≫: 배틀 이외로 배틀 에어리어를 벗어날 때 진화원의 지정 카드를 코스트 없이 등장)
///  - "If deleted by ＜Overclock＞" (EX11-060) — ≪오버클럭≫의 비용으로 소멸한 디지몬 (종합 룰 16-34)
///  - "activate 1 of those Digimon's [Main] effects" (BT22-092 — 공식 Q&amp;A BT22-092 Q1: 진화원 효과의 【메인】도 발휘할 수 있다)
/// </summary>
public partial class GoldfishEngine
{
    // ≪디코드≫로 등장시키는 중인 카드 (등장 처리 안에서 바로 유발하는 효과) · 등장한 디지몬 (나중에 처리하는 효과)
    private readonly HashSet<CardData> _decodePlaying72 = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CardInstance> _decodePlayed72 = new(ReferenceEqualityComparer.Instance);
    // ≪오버클럭≫의 비용으로 소멸한 디지몬
    private readonly HashSet<CardInstance> _overclockDeleted72 = new(ReferenceEqualityComparer.Instance);

    /// <summary>d가 ≪디코드≫로 등장한 디지몬인지</summary>
    internal bool PlayedByDecode72(CardInstance? d) =>
        d != null && (_decodePlayed72.Contains(d) || _decodePlaying72.Contains(d.TopCard) && d.EvolutionSources.Count == 0);

    /// <summary>d가 ≪오버클럭≫의 비용으로 소멸한 디지몬인지</summary>
    internal bool DeletedByOverclock72(CardInstance? d) => d != null && _overclockDeleted72.Contains(d);

    internal void NoteOverclockFodder72(CardInstance fodder) => _overclockDeleted72.Add(fodder);

    /// <summary>≪디코드≫로 진화원의 pick을 등장시킨다 (play = 실제 등장 처리) — 등장한 디지몬을 기억한다</summary>
    internal bool PlayByDecode72(PlayerZones owner, CardData pick, Func<bool> play)
    {
        _decodePlaying72.Add(pick);
        bool ok;
        try { ok = play(); }
        finally { _decodePlaying72.Remove(pick); }
        if (ok && owner.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TopCard, pick)) is { } played) _decodePlayed72.Add(played);
        return ok;
    }

    /// <summary>
    /// 필드의 디지몬 d의 【메인】 효과 1개를 발휘한다 (맨 위 카드의 【메인】 · 앞면 진화원의 진화원 효과 【메인】 — 공식 Q&amp;A BT22-092 Q1).
    /// 효과로 발휘하는 것이라 플레이어의 【메인】 사용 횟수에는 넣지 않는다. 반환: 효과를 발휘했는지 (조건 · 비용이 맞지 않으면 false)
    /// </summary>
    internal bool ActivateMainOf72(PlayerZones owner, CardInstance d)
    {
        if (!owner.BattleArea.Contains(d)) return false;
        var cands = new List<(CardData Card, bool Inherited)>();
        if (HasMainAbility(d.TopCard)) cands.Add((d.TopCard, false));
        foreach (var s in CardIdentity55.Each(d.FaceUpSources.Where(x => !ReferenceEquals(x, d.TopCard))))
            if (HasInheritedMainAbility(s)) cands.Add((s, true));
        if (cands.Count == 0) return false;
        int i = cands.Count == 1 ? 0 : ChooseOption(owner, $"[{d.TopCard.Name}]의 어느 【메인】 효과를 발휘할까요?", cands.Select(x => x.Inherited ? $"진화원 [{x.Card.Name}]" : $"[{x.Card.Name}]").ToList(), 0);
        var (card, inherited) = cands[Math.Clamp(i, 0, cands.Count - 1)];
        var ctx = AsPartOfOuterEffect56(() => inherited
            ? RunEffects(EffectTrigger.ActivateMain, owner, d, null, includeInherited: true, sources: new[] { card })
            : RunEffects(EffectTrigger.ActivateMain, owner, d, card, includeInherited: false));
        RecalculateFieldBuffs();
        return ctx.AnyEffectRan && !(ctx.StepsRan && !ctx.AnyStepResolved);
    }
}
