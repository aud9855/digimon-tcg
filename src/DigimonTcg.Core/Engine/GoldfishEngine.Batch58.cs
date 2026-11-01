using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.58.0 미지원 문장 32차 묶음 (엔진 쪽):
///  - 지속 효과로 얻은 특징 (EX7-010 "[Your Turn] This Digimon gains the [Three Musketeers] trait") — 다른 지속 효과보다 먼저 계산 (대상 조건에 쓰인다)
///  - 어택 중에는 상대의 효과를 받지 않는다 (BT27-103 "During attacks, your opponent's effects don't affect this Digimon")
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>특징을 얻는 지속 효과를 등록한 카드 스크립트가 있는지 (SentenceParser.Batch58이 등록할 때 켠다)</summary>
    internal static volatile bool ExtraTraitScripts58;

    /// <summary>지속 효과로 얻은 특징 — 양쪽 배틀 에어리어 전부 (키워드 · DP보다 먼저: "[X] trait Digimon" 조건이 이것을 본다)</summary>
    private void ApplyExtraTraits58()
    {
        if (!ExtraTraitScripts58) return;   // 특징을 얻는 지속 효과를 등록한 카드가 없으면 (거의 늘) 할 일이 없다
        ApplyExtraTraits58(State.Player, State.DummyOpponent);
        ApplyExtraTraits58(State.DummyOpponent, State.Player);
    }

    private void ApplyExtraTraits58(PlayerZones owner, PlayerZones enemy)
    {
        foreach (var inst in owner.BattleArea)
        {
            if (inst.ExtraTraits58.Count > 0) inst.ExtraTraits58.Clear();
            StaticContext? ctx = null;
            foreach (var e in StaticEffectsOf(inst))
            {
                if (e.Stat != StaticStat.ExtraTrait58 || e.Trait58 == null) continue;
                ctx ??= new StaticContext { Engine = this, State = State, Owner = owner, Enemy = enemy, Digimon = inst, OwnersTurn = inst.IsOwnersTurn };
                if (e.Value(ctx) != 0) inst.ExtraTraits58.Add(e.Trait58);
            }
        }
    }

    /// <summary>BT27-103: 어택 중(어느 쪽의 어택이든 — 효과로 선언한 어택도)에는 상대의 효과를 받지 않는다</summary>
    private bool UnaffectedDuringAttacks58(PlayerZones effectOwner, CardInstance target) =>
        AttackInProgress57 && !ReferenceEquals(OwnerOf(target), effectOwner) && CardScripts.HasKeyword(target, Keyword.UnaffectedByOpponentDuringAttacks58)
        && Diag46.HitTrue("unaffectedDuringAttacks58");
}
