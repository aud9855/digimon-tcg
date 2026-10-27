using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.42.0 미지원 문장 16차 묶음의 엔진 처리 (SentenceParser.Batch42*):
///  - 조건이 붙은 진화 조건 무시 진화 — 지속 효과의 조건을 지금 필드로 판단하는 문맥 (CardInstance.LiveContext42)
///  - 겹쳐진 카드 보호 ("their effects can't trash this Digimon's stacked cards" — Keyword.StackProtectedFromOpponent) · 겹쳐진 카드를 위에서부터 N장 파기 (BT21-030)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>inst의 지속 효과 조건을 지금 필드로 판단하는 문맥을 채운다 (조건이 붙은 진화 조건 무시 진화 — CardScripts.SpecialEvolutionCost42)</summary>
    private void SetLiveContext42(CardInstance inst, PlayerZones owner, PlayerZones enemy)
    {
        if (inst.LiveContext42 != null && ReferenceEquals(inst.LiveOwner42, owner)) return;
        inst.LiveOwner42 = owner;
        inst.LiveContext42 = () => new StaticContext { Engine = this, State = State, Owner = owner, Enemy = enemy, Digimon = inst, OwnersTurn = inst.IsOwnersTurn };
    }

    /// <summary>집계: 조건이 붙은 진화 조건 무시 진화로 진화했는지 (진화할 카드 쪽 · 진화하는 디지몬 쪽)</summary>
    private static void NoteSpecialEvo42(CardInstance target, CardData evoCard, int cost)
    {
        if (target.TopCard.Level == evoCard.EvoRequirementLevel && target.Colors.Contains(evoCard.EvoRequirementColor)) return;   // 보통 진화
        if (KoreanEvolution47.Cost(evoCard, target) == cost) { Diag46.Hit("koreanEvo47"); return; }   // v0.47.0 공식 한글 〔진화〕 조건으로 진화
        if (CardScripts.SpecialEvolutionCost42(evoCard, target) != cost) return;
        if (CardScripts.For(evoCard)?.SpecialEvolutions42.Count > 0) Diag42.Hit(ref Diag42.SpecialEvo);
        else Diag42.Hit(ref Diag42.HolderEvo);
    }

    /// <summary>
    /// 지금 처리 중인 효과로 inst의 겹쳐진 카드를 파기할 수 없는지 — 상대의 효과이고 inst가 "겹쳐진 카드는 상대의 효과로 파기되지 않는다"를 가졌을 때
    /// (공식 BT21-060 "상대의 턴 종료까지 이 디지몬에 겹쳐져 있는 카드는 상대의 효과로 파기되지 않는다" — ≪퇴화≫ · 진화원 파기 · 맨 위 카드 파기)
    /// </summary>
    internal bool StackProtected42(PlayerZones zones, CardInstance inst)
    {
        if (!CardScripts.HasKeyword(inst, Keyword.StackProtectedFromOpponent) || !IsOpponentEffectFor(zones)) return false;
        Diag42.Hit(ref Diag42.StackProtect);
        EffectLog(zones, $"[{inst.TopCard.Name}]에 겹쳐진 카드는 상대의 효과로 파기되지 않습니다.");
        return true;
    }

    /// <summary>
    /// 겹쳐진 카드를 위에서부터 n장 파기 (BT21-030 — 공식 "상대의 디지몬 1마리에 겹쳐져 있는 카드를 위에서부터 10장 파기한다"):
    /// 맨 위 카드를 파기하면 바로 아래 진화원이 맨 위 카드가 된다. 마지막 1장까지 파기하면 그 디지몬은 트래시로 간다 (배틀 에어리어를 떠난다 — 소멸은 아님). 파기한 매수
    /// </summary>
    internal int TrashTopStackedCards42(PlayerZones zones, CardInstance inst, int n)
    {
        if (!zones.BattleArea.Contains(inst) || n <= 0 || StackProtected42(zones, inst)) return 0;
        int done = 0;
        CardData? firstTop = null;
        while (done < n && inst.EvolutionSources.Count > 0 && inst.TrashTopCard() is { } t)
        {
            firstTop ??= t;
            ApplyOverflow(zones, new[] { t });
            if (!IsToken(t)) zones.Trash.Add(t);
            done++;
        }
        if (done < n)
        {
            // 마지막 카드 — 디지몬 자체를 트래시로 ("떠나려 할 때" 효과로 막을 수 있다)
            if (TryPreventLeave(zones, inst, deletion: false, inBattle: false, canPrevent: true, LeaveDestination.Other) || !zones.BattleArea.Remove(inst))
            {
                RecalculateFieldBuffs();
                return done;
            }
            TrashLinks(zones, inst);
            ApplyOverflow(zones, new[] { inst.TopCard });
            if (!IsToken(inst.TopCard)) zones.Trash.Add(inst.TopCard);
            PurgeTokens(zones);
            NotifyLeftBattleArea(zones, inst);
            Diag42.Hit(ref Diag42.StackTrashAll);
            done++;
        }
        RecalculateFieldBuffs();
        TrashExposedFaceDownTops();
        if (done > 0) NotifyTopStackedTrashed43(zones, inst, firstTop);   // v0.43.0 겹쳐진 카드가 위에서부터 파기되었을 때 (BT21-094)
        return done;
    }
}

public partial class GoldfishEngine
{
    private readonly List<(CardInstance Digimon, PlayerZones EffectOwner, int TurnSequence, bool ToHand)> _scheduledReturns42 = new();
    private bool _securityReturnAtEnd42;

    /// <summary>이 턴 종료 시 그 디지몬을 패 (toHand) · 덱 아래로 되돌린다 (EX4-013 — 공식 "이 턴 종료 시, 이 디지몬을 패로 되돌린다" · BT5-109)</summary>
    internal void ScheduleReturnAtTurnEnd42(CardInstance digimon, PlayerZones effectOwner, int turnSequence, bool toHand) =>
        _scheduledReturns42.Add((digimon, effectOwner, turnSequence, toHand));

    private void RunScheduledReturns42()
    {
        if (_scheduledReturns42.Count == 0) return;
        var due = _scheduledReturns42.Where(x => x.TurnSequence <= State.TurnSequence).ToList();
        _scheduledReturns42.RemoveAll(x => x.TurnSequence <= State.TurnSequence);
        foreach (var (digimon, effectOwner, _, toHand) in due)
        {
            if (State.IsGameOver) return;
            var zones = State.Player.BattleArea.Contains(digimon) ? State.Player : State.DummyOpponent.BattleArea.Contains(digimon) ? State.DummyOpponent : null;
            if (zones == null) continue;
            EffectLog(zones, $"[턴 종료 시] [{digimon.TopCard.Name}]을(를) {(toHand ? "패로" : "덱 아래로")} 되돌립니다.");
            var outer = CurrentEffectOwner;
            CurrentEffectOwner = effectOwner;
            _effectDepth++;
            try { if (toHand) ReturnToHand(zones, digimon); else BottomDeck(zones, digimon); }
            finally { _effectDepth--; CurrentEffectOwner = outer; }
            Diag42.Hit(ref Diag42.ReturnAtEnd);
            FlushPendingTriggers();
        }
        RecalculateFieldBuffs();
    }
}

public partial class GoldfishEngine
{
    /// <summary>
    /// [육성] 시점 효과 (SentenceParser.Batch42Breeding): 육성 에어리어의 디지몬 — 맨 위 카드의 Breeding42 효과 · 앞면 진화원의 BreedingInherited42 효과 (같은 카드는 1번).
    /// 처리하는 동안 그 디지몬이 육성 에어리어에 그대로 있어야 한다
    /// </summary>
    internal void RunBreedingEffects42(EffectTrigger trigger, PlayerZones owner, CardInstance? other, CardData? triggerCard)
    {
        if (owner.BreedingArea is not { } b || State.IsGameOver) return;
        var list = new List<(CardData Card, CardEffect Effect)>();
        if (CardScripts.For(b.TopCard) is { } top)
            foreach (var e in top.Effects.Where(e => e.Source == EffectSource.Breeding42 && e.Trigger == trigger))
                list.Add((b.TopCard, e));
        foreach (var src in b.FaceUpSources.GroupBy(x => x.CardId).Select(g => g.First()))
            if (CardScripts.For(src) is { } ss)
                foreach (var e in ss.Effects.Where(e => e.Source == EffectSource.BreedingInherited42 && e.Trigger == trigger))
                    list.Add((src, e));
        if (list.Count == 0) return;
        _effectDepth++;
        var outerEffectOwner = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try
        {
            foreach (var (card, effect) in list)
            {
                if (State.IsGameOver || !ReferenceEquals(owner.BreedingArea, b)) break;
                var ctx = new EffectContext(this, owner, b, other, null) { TriggerCard = triggerCard, Trigger = trigger };
                Diag42.Hit(ref Diag42.Breeding);
                Resolve(ctx, card, effect);
            }
        }
        finally
        {
            _effectDepth--;
            CurrentEffectOwner = outerEffectOwner;
        }
        if (_effectDepth == 0)
            FlushPendingTriggers();
    }

    /// <summary>[육성] 지속 효과 중 다른 디지몬에게 주는 것 (DigimonAuras에 더한다)</summary>
    private IEnumerable<(StaticEffect Effect, StaticContext Context)> BreedingAuras42(PlayerZones side, PlayerZones enemy, StaticStat stat)
    {
        if (side.BreedingArea is not { } b) yield break;
        var ctx = new StaticContext { Engine = this, State = State, Owner = side, Enemy = enemy, Digimon = b, OwnersTurn = b.IsOwnersTurn };
        if (CardScripts.For(b.TopCard) is { } top)
            foreach (var e in top.Statics.Where(e => e.Source == EffectSource.Breeding42 && e.Stat == stat))
                yield return (e, ctx);
        foreach (var src in b.FaceUpSources.GroupBy(x => x.CardId).Select(g => g.First()))
            if (CardScripts.For(src) is { } ss)
                foreach (var e in ss.Statics.Where(e => e.Source == EffectSource.BreedingInherited42 && e.Stat == stat))
                    yield return (e, ctx);
    }
}

/// <summary>v0.42.0 시뮬레이션 집계 — 새 동작이 실제로 일어난 횟수 (sim 하네스가 읽는다)</summary>
public static class Diag42
{
    public static int Link, SpecialEvo, HolderEvo, StackProtect, StackTrash, StackTrashAll, SecurityTrashN, SecurityLook, ReturnAtEnd, Instead, InsteadAlt, Breeding, DeletedCost, CountNew;

    public static void Hit(ref int counter) => System.Threading.Interlocked.Increment(ref counter);

    public static void Reset() =>
        Link = SpecialEvo = HolderEvo = StackProtect = StackTrash = StackTrashAll = SecurityTrashN = SecurityLook = ReturnAtEnd = Instead = InsteadAlt = Breeding = DeletedCost = CountNew = 0;

    public static string Report() =>
        $"link={Link} specialEvo={SpecialEvo} holderEvo={HolderEvo} stackProtect={StackProtect} stackTrash={StackTrash} stackTrashAll={StackTrashAll} secTrashN={SecurityTrashN} secLook={SecurityLook} " +
        $"returnAtEnd={ReturnAtEnd} instead={Instead} insteadAlt={InsteadAlt} breeding={Breeding} deletedCost={DeletedCost} count={CountNew}";
}
