using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.47.0 효과 하나로 등장한 카드는 동시에 등장한다 — 등장 뒤의 유발(【등장 시】 · "등장했을 때")은 그 효과의 처리가 끝난 뒤에 한꺼번에:
///  - 종합 룰 3-1-3-3: 여러 장이 한 곳을 동시에 떠나 다른 곳에 놓이면 1장씩이 아니라 동시에 놓인다
///  - 15-5-3 유발할 수 있는 장소에 놓임과 동시에 조건을 만족하면 유발 → 같은 효과로 함께 등장한 카드도 서로의 "등장했을 때"에 반응한다
///    (전에는 1장씩 처리해 뒤에 등장한 카드가 앞 카드의 등장에 반응하지 못하고, 앞 카드는 뒤 카드의 등장마다 반응했다)
///  - 15-5-2 · 공식 Q&A ST12-04 Q1 · ST13-01 Q1 · BT17-002 Q1: 효과 하나로 여러 마리가 동시에 등장해도 그 유발은 1번 — 같은 효과의 "등장했을 때"는 1번만
///  - 15-4-4 유발한 효과는 처리 중인 효과가 끝난 뒤 발휘 대기에서 1개씩 → 【등장 시】도 효과가 끝난 뒤
/// 효과의 처리(ResolveCore) 안에서 효과로 등장한 카드만 모은다 (패에서 등장 · 규칙 처리의 등장은 전과 같다)
/// </summary>
public partial class GoldfishEngine
{
    public static bool SimultaneousPlayEnabled47 { get; set; } = true;

    internal sealed class PlayBatch47
    {
        internal PlayBatch47? Outer;
        internal readonly List<(PlayerZones Owner, CardInstance? Inst, CardData? Tamer, bool OnPlay, PlayOrigin Origin)> Items = new();
    }

    private PlayBatch47? _playBatch47;

    /// <summary>지금 처리 중인 동시 등장의 "등장했을 때" 유발 기록 (같은 효과는 1번만)</summary>
    private HashSet<(object Holder, CardEffect Effect)>? _playedFired47;

    internal PlayBatch47? BeginPlayBatch47()
    {
        if (!SimultaneousPlayEnabled47) return null;
        var b = new PlayBatch47 { Outer = _playBatch47 };
        _playBatch47 = b;
        return b;
    }

    /// <summary>효과로 등장시킨 카드를 모은다 (모으는 중이 아니면 false — 바로 처리)</summary>
    private bool DeferPlayed47(PlayerZones owner, CardInstance? inst, CardData? tamer, bool onPlay)
    {
        if (_playBatch47 is not { } b) return false;
        b.Items.Add((owner, inst, tamer, onPlay, CurrentPlayOrigin));
        return true;
    }

    internal void EndPlayBatch47(PlayBatch47? b)
    {
        if (b == null) return;
        _playBatch47 = b.Outer;
        if (b.Items.Count == 0 || State.IsGameOver) return;
        if (b.Items.Count > 1) Diag46.Hit("simultaneousPlay47");
        var witness = CaptureWitness46();
        // 1) 【등장 시】 (놓인 순서대로 — 발휘 전에 떠난 카드는 발휘하지 않는다: 종합 룰 15-4-4-3)
        foreach (var it in b.Items)
        {
            if (State.IsGameOver) return;
            if (!it.OnPlay) continue;
            WithOrigin47(it.Origin, () =>
            {
                if (it.Inst is { } inst)
                {
                    if (!it.Owner.BattleArea.Contains(inst)) return;
                    if (OnPlayBlocked37(it.Owner, inst)) { EffectLog(it.Owner, $"[{inst.TopCard.Name}]의 【등장 시】 효과는 상대의 효과로 발휘하지 않습니다."); return; }
                    string summary = RunEffects(EffectTrigger.OnPlay, it.Owner, inst, inst.TopCard, includeInherited: false).Message;
                    if (summary.Length > 0) EffectLog(it.Owner, $"[효과] {inst.TopCard.Name} {summary}");
                }
                else if (it.Tamer is { } t && it.Owner.Tamers.Contains(t))
                    RunEffects(EffectTrigger.OnPlay, it.Owner, null, t, includeInherited: false);
            });
        }
        // 2) "등장했을 때" — 함께 등장한 카드 전부를 본 기록으로, 같은 효과는 1번만
        var outerFired = _playedFired47;
        _playedFired47 = new HashSet<(object, CardEffect)>();
        try
        {
            foreach (var it in b.Items)
            {
                if (State.IsGameOver) return;
                WithOrigin47(it.Origin, () =>
                {
                    if (it.Inst is { } inst)
                    {
                        foreach (var other in it.Owner.BattleArea.Where(d => !ReferenceEquals(d, inst) && (witness == null || witness.Saw(d))).ToList())
                            RunEffects(EffectTrigger.AllySameNamePlayed, it.Owner, other, null, other: inst);
                        FireForSide(EffectTrigger.AllyDigimonPlayed, it.Owner, inst, witness: witness);
                        FireForSide(EffectTrigger.OpponentDigimonPlayed, EnemyOf(it.Owner), inst, witness: witness);
                    }
                    else if (it.Tamer is { } t)
                    {
                        FireForSide(EffectTrigger.AllyTamerPlayed, it.Owner, null, triggerCard: t, witness: witness);
                        FireForSide(EffectTrigger.OpponentTamerPlayed, EnemyOf(it.Owner), null, triggerCard: t, witness: witness);
                    }
                });
            }
        }
        finally { _playedFired47 = outerFired; }
        RecalculateFieldBuffs();
    }

    private void WithOrigin47(PlayOrigin origin, Action run)
    {
        var outer = _playOrigin;
        _playingByEffect++;
        _playOrigin = origin;
        try { run(); }
        finally { _playOrigin = outer; _playingByEffect--; }
    }

    /// <summary>동시 등장의 "등장했을 때" — 이 효과가 이미 유발했으면 false (같은 효과는 1번만)</summary>
    internal bool FirstPlayedTrigger47(EffectContext ctx, CardData card, CardEffect effect)
    {
        if (_playedFired47 == null || effect.Trigger is not (EffectTrigger.AllyDigimonPlayed or EffectTrigger.OpponentDigimonPlayed
            or EffectTrigger.AllyTamerPlayed or EffectTrigger.OpponentTamerPlayed or EffectTrigger.AllySameNamePlayed)) return true;
        object holder = (object?)ctx.Self ?? card;
        if (_playedFired47.Add((holder, effect))) return true;
        Diag46.Hit("playedOnce47");
        return false;
    }
}
