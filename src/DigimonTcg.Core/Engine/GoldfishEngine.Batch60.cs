using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>"~ 할 수 없다" 기간 효과의 종류 (CardInstance.OppLockUntil60 · OwnLockUntil60의 자리)</summary>
internal enum LockKind60 { Attack = 0, Block = 1, Digivolve = 2, Suspend = 3, Unsuspend = 4, SkipUnsuspend = 5 }

/// <summary>
/// v0.60.0 미지원 문장 34차 묶음 (엔진 쪽):
///  - 상대의 효과를 받지 않는 동안 (33차의 마무리): 상대의 효과로 받은 기간 키워드 · ≪시큐리티 어택 ±N≫ · "~ 할 수 없다" 기간 효과(어택 · 블록 · 진화 · 레스트 · 액티브)도
///    영향이 없다 — 종합 룰 15-15-5-1 "효과를 받지 않는 카드는 효과에 의해 발생한 영향을 받지 않는다" · 15-15-5-2 "효과를 받게 된 경우, 그 시점에 영향을 받는다" ·
///    15-15-5-4 "효과를 받지 않는 디지몬에게 ≪S 어택 -1≫을 부여해도 가지지 않는다". 받은 효과는 지워지지 않고, 받지 않는 상태가 끝나면 그 시점부터 다시 영향을 받는다 (DP와 같은 방식 — v0.59.0)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>v0.60.0 ≪디지버스트≫ 뒤의 효과가 트래시에서 쓰는 카드 (AI 추천: 맞는 진화원을 먼저 파기 — SentenceParser.Batch60Ai)</summary>
    internal Func<CardData, bool>? BurstUse60 { get; set; }

    // =====================================================================
    // 상대의 효과로 받은 기간 효과 — 출처 기록
    // =====================================================================

    /// <summary>기간 키워드를 준다 (상대의 효과이면 그 몫을 기록)</summary>
    internal void AddTimedKeyword60(CardInstance d, Keyword kw, int until)
    {
        d.TimedKeywords.Add((kw, until));
        if (IsOpponentEffectFor(OwnerOf(d))) d.OppTimedKeywords60.Add((kw, until));
    }

    /// <summary>기간 ≪시큐리티 어택 ±N≫을 준다 (상대의 효과이면 그 몫을 기록)</summary>
    internal void AddTimedSa60(CardInstance d, int sa, int until)
    {
        d.TimedSecurityAttackBuffs.Add((sa, until));
        if (IsOpponentEffectFor(OwnerOf(d))) d.OppTimedSa60.Add((sa, until));
    }

    /// <summary>기간 ≪시큐리티 어택 ±N≫ 하나를 지운다 (EX3-036 "대신")</summary>
    internal void RemoveTimedSaAt60(CardInstance d, int index)
    {
        var e = d.TimedSecurityAttackBuffs[index];
        d.TimedSecurityAttackBuffs.RemoveAt(index);
        int j = d.OppTimedSa60.IndexOf(e);
        if (j >= 0) d.OppTimedSa60.RemoveAt(j);
    }

    /// <summary>이 어택 동안 ≪시큐리티 어택 ±N≫ (상대의 효과이면 그 몫을 기록)</summary>
    internal void AddAttackSa60(CardInstance d, int sa)
    {
        d.AttackSecurityAttackBonus += sa;
        if (IsOpponentEffectFor(OwnerOf(d))) d.OppAttackSa60 += sa;
    }

    /// <summary>"~ 할 수 없다" 기간 효과를 건다 (상대의 효과이면 그 몫, 아니면 다른 효과의 몫으로 기록)</summary>
    internal void Lock60(CardInstance d, LockKind60 kind, int until)
    {
        switch (kind)
        {
            case LockKind60.Attack: d.CannotAttackUntil = Math.Max(d.CannotAttackUntil, until); break;
            case LockKind60.Block: d.CannotBlockUntil = Math.Max(d.CannotBlockUntil, until); break;
            case LockKind60.Digivolve: d.CannotDigivolveUntil = Math.Max(d.CannotDigivolveUntil, until); break;
            case LockKind60.Suspend: d.CannotSuspendUntil = Math.Max(d.CannotSuspendUntil, until); break;
            case LockKind60.Unsuspend: d.CannotUnsuspendUntil = Math.Max(d.CannotUnsuspendUntil, until); break;
            case LockKind60.SkipUnsuspend: d.SkipUnsuspendUntil = Math.Max(d.SkipUnsuspendUntil, until); break;
        }
        var arr = IsOpponentEffectFor(OwnerOf(d)) ? d.OppLockUntil60 : d.OwnLockUntil60;
        arr[(int)kind] = Math.Max(arr[(int)kind], until);
    }

    // =====================================================================
    // 지속 효과 계산 (RecalculateFieldBuffs)
    // =====================================================================

    /// <summary>기간 키워드 중 상대의 효과가 아닌 것을 더하고, 상대의 효과로 받은 것은 돌려준다 (받지 않는 상태를 정한 뒤에 더한다). 없으면 null</summary>
    private static List<Keyword>? AddTimedKeywordsSplit60(CardInstance inst)
    {
        if (inst.OppTimedKeywords60.Count == 0)
        {
            foreach (var (k, _) in inst.TimedKeywords) inst.ActiveKeywords.Add(k);
            return null;
        }
        var opp = inst.OppTimedKeywords60.ToList();
        var deferred = new List<Keyword>();
        foreach (var t in inst.TimedKeywords)
        {
            int i = opp.IndexOf(t);
            if (i >= 0) { opp.RemoveAt(i); deferred.Add(t.Keyword); }
            else inst.ActiveKeywords.Add(t.Keyword);
        }
        return deferred;
    }

    /// <summary>기간이 지난 상대 효과의 몫을 지운다</summary>
    private void ExpireOppTimed60(CardInstance inst)
    {
        if (inst.OppTimedKeywords60.Count > 0) inst.OppTimedKeywords60.RemoveAll(t => t.Until < State.TurnSequence);
        if (inst.OppTimedSa60.Count > 0) inst.OppTimedSa60.RemoveAll(t => t.Until < State.TurnSequence);
    }

    /// <summary>받지 않는 상태가 아니면 상대의 효과로 받은 기간 키워드를 더한다</summary>
    private void ApplyDeferredOppKeywords60(CardInstance inst, List<Keyword>? deferred)
    {
        if (deferred == null || deferred.Count == 0) return;
        if (inst.OppShield59) { Diag46.Hit("oppShieldTimedKeyword60"); return; }
        foreach (var k in deferred) inst.ActiveKeywords.Add(k);
    }

    /// <summary>받지 않는 동안 빼는 상대 효과의 기간 ≪시큐리티 어택≫</summary>
    private static int OppTimedSaShield60(CardInstance inst) =>
        inst.OppShield59 && inst.OppTimedSa60.Count > 0 && Diag46.HitTrue("oppShieldTimedSa60") ? inst.OppTimedSa60.Sum(t => t.Count) : 0;
}

public partial class GoldfishEngine
{
    // =====================================================================
    // 【시큐리티】 효과가 발휘되지 않는다 (EX6-010 · BT12-112 · BT3-097 · EX3-073)
    // =====================================================================

    /// <summary>
    /// attacker가 체크한 card의 【시큐리티】 효과가 발휘되지 않는지 — 체크로 유발한 효과를 처리하지 않고 (13-1-8-2), 공개된 카드는 트래시에 놓인다 (13-1-8-4 ·
    /// 공식 Q&amp;A BT1-025 Q1 "【시큐리티】 효과가 발휘되지 않으므로 그대로 파기"). 디지몬 카드는 효과 없이 배틀한다 (13-1-8-3).
    /// "on Option cards"는 옵션 카드만 (공식 Q&amp;A BT3-097 Q1: 디지몬 · 테이머 카드의 【시큐리티】 효과는 평소처럼 발휘한다)
    /// </summary>
    internal bool SecurityEffectBlocked60(CardInstance attacker, CardData card)
    {
        bool option = card.Type == CardType.Option;
        if (option && CardScripts.HasKeyword(attacker, Keyword.NegateSecurityOptions)) return true;   // 원래 규칙 (BT1-025 · BT3-097 얻은 것도)
        var side = OwnerOf(attacker);
        bool hit = CardScripts.HasKeyword(attacker, Keyword.NegateAllSecurity60)
                   || option && side.BattleArea.Any(d => CardScripts.HasKeyword(d, Keyword.NegateEnemyOptionSecurity60))
                   || EffectRules.IsActive(side.NoEnemySecurityUntil60, State);
        return hit && Diag46.HitTrue("securityEffectBlocked60");
    }

    // =====================================================================
    // EX6-044 자신의 효과 · 소멸 이외로 배틀 에어리어를 떠나지 않는다
    // =====================================================================

    /// <summary>소멸 이외의 방법으로 떠나려 할 때 막는지 — EX6-044 "other than by your effects or by deletion" (상대의 효과만 막는다) ·
    /// BT16-051 "other than by deletion" (자신의 효과도 막는다). 규칙 처리로 떠나는 것은 소멸뿐</summary>
    private bool LeaveLocked60(PlayerZones zones, CardInstance inst, bool deletion)
    {
        if (deletion) return false;
        bool any = CardScripts.HasKeyword(inst, Keyword.CannotLeaveExceptDeletion60);
        if (!any && !(CardScripts.HasKeyword(inst, Keyword.CannotLeaveExceptOwnOrDeletion60) && IsOpponentEffectFor(zones))) return false;
        EffectLog(zones, any ? $"[{inst.TopCard.Name}]은(는) 소멸 이외로는 배틀 에어리어를 떠나지 않습니다." : $"[{inst.TopCard.Name}]은(는) 자신의 효과 · 소멸 이외로는 배틀 에어리어를 떠나지 않습니다.");
        Diag46.Hit("leaveLocked60");
        return true;
    }
}
