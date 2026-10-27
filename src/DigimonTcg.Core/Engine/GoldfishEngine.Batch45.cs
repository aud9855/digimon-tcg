using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.45.0 미지원 문장 19차 묶음의 엔진 처리</summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // 【소멸 시】 · "소멸했을 때"의 조건은 소멸하기 직전의 모습으로 (종합 룰 15-8-3-9-4)
    //  - 엔진은 진화원을 트래시로 보낸 뒤 【소멸 시】를 처리한다 (공식 Q&A EX10-050: 트래시 매수에는 진화원이 들어간다)
    //  - 그래서 조건만 소멸 직전의 복사본(CardInstance.DeletionView45)으로 본다 — 진화원 · DP · 색 · 키워드.
    //    효과가 옮기는 카드는 원래 디지몬(트래시에 간 카드)의 것이라 카드가 둘이 되지 않는다
    // =====================================================================

    /// <summary>소멸 직전 모습으로 조건을 볼지 (감사용 스위치 — 끄면 v0.44.0까지처럼 소멸 뒤 모습)</summary>
    public static bool DeletionViewEnabled45 { get; set; } = true;

    /// <summary>감사용: 두 모습으로 모두 판단해 결과가 다르면 Diag45에 카드 번호로 센다 (시뮬레이션 전용)</summary>
    public static bool AuditDeletionView45 { get; set; }

    private static CardInstance? DeletionView45(EffectContext c, CardInstance? x)
    {
        if (x?.DeletionView45 is not { } v) return null;
        if (c.Owner.BattleArea.Contains(x) || c.Enemy.BattleArea.Contains(x)) return null;   // 다시 필드에 있으면 지금 모습
        return v;
    }

    /// <summary>조건을 소멸 직전 모습으로 판단 (이 효과의 디지몬 · 사건의 디지몬이 소멸한 디지몬일 때만)</summary>
    internal static bool EvalWithDeletionView45(EffectContext c, Func<EffectContext, bool> f)
    {
        var self = c.Self;
        var other = c.Other;
        var vs = DeletionView45(c, self);
        var vo = DeletionView45(c, other);
        if (vs == null && vo == null) return f(c);
        if (!DeletionViewEnabled45) return f(c);
        bool withView;
        c.Self = vs ?? self;
        c.Other = vo ?? other;
        try { withView = f(c); }
        finally { c.Self = self; c.Other = other; }
        if (AuditDeletionView45 && f(c) != withView)
            Diag45.Hit("deletionView:" + (c.Card?.CardId ?? "?"));
        return withView;
    }
}

public partial class GoldfishEngine
{
    // =====================================================================
    // 지속 "액티브가 되지 않는다" (StaticStat.UnsuspendLock45)
    //  - "All other Digimon and Tamers don't unsuspend" (BT12-057 — 공식 "이 디지몬 이외의 디지몬과 테이머 전부는 액티브가 되지 않는다": 효과로도)
    //  - "Other than this Digimon, no Digimon or Tamers can unsuspend in the unsuspend phase" (EX10-023 — 공식 "액티브 페이즈에서는")
    //  - "your opponent's Tamers don't unsuspend during their unsuspend phase" (BT6-092)
    // =====================================================================

    /// <summary>owner의 디지몬 d (또는 테이머 tamer)가 지속 효과로 액티브가 되지 않는지. phase = 액티브 페이즈의 판단 (false면 "액티브 페이즈에서는" 효과는 보지 않는다)</summary>
    internal bool UnsuspendLock45(PlayerZones owner, CardInstance? d, CardData? tamer, bool phase)
    {
        var enemy = EnemyOf(owner);
        foreach (var side in new[] { owner, enemy })
        {
            var other = ReferenceEquals(side, owner) ? enemy : owner;
            bool mine = ReferenceEquals(side, owner);
            foreach (var (aura, actx) in DigimonAuras(side, other, StaticStat.UnsuspendLock45).Concat(TamerAuras(side, other, StaticStat.UnsuspendLock45)))
            {
                int bit = d != null ? (mine ? 1 : 2) : (mine ? 4 : 8);
                int v = aura.Value(actx);
                if ((v & bit) == 0) continue;
                if ((v & 16) != 0 && !phase) continue;
                if (d != null && !aura.AppliesTo(actx, d)) continue;
                if (phase) Diag45.Hit((v & 16) != 0 ? "unsuspendPhaseLock" : "unsuspendLock");
                return true;
            }
        }
        return false;
    }
}

public partial class GoldfishEngine
{
    // =====================================================================
    // [육성] [메인] 기동 효과 (EX9-005) · [육성] "treated as having 16000 DP" (BT22-007)
    // =====================================================================

    private static bool HasBreedingMain45(CardData card) =>
        CardScripts.For(card)?.Effects.Any(e => e.Trigger == EffectTrigger.ActivateMain && e.Source == EffectSource.Breeding42) ?? false;

    /// <summary>육성 에어리어의 디지몬 b의 [육성] [메인] 효과를 처리한다</summary>
    private EffectContext RunBreedingMain45(PlayerZones owner, CardInstance b, CardData card)
    {
        var ctx = new EffectContext(this, owner, b, null, null) { Trigger = EffectTrigger.ActivateMain };
        _effectDepth++;
        var outerEffectOwner = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try
        {
            foreach (var e in CardScripts.For(card)?.Effects.Where(e => e.Trigger == EffectTrigger.ActivateMain && e.Source == EffectSource.Breeding42).ToList() ?? new())
            {
                if (State.IsGameOver || !ReferenceEquals(owner.BreedingArea, b)) break;
                Resolve(ctx, card, e);
            }
        }
        finally
        {
            _effectDepth--;
            CurrentEffectOwner = outerEffectOwner;
        }
        if (_effectDepth == 0)
            FlushPendingTriggers();
        return ctx;
    }

    /// <summary>육성 에어리어의 디지몬(카드 전부)을 target의 진화원 맨 아래에 놓는다 (위 카드가 위로 — 공식 EX9-005 "이 디지몬을 등장한 디지몬의 진화원 아래에 놓는다")</summary>
    internal bool PlaceBreedingUnder45(PlayerZones owner, CardInstance target)
    {
        if (owner.BreedingArea is not { } b || !owner.BattleArea.Contains(target)) return false;
        owner.BreedingArea = null;
        var stack = b.GetEvolutionStackBottomUp().Append(b.TopCard).ToList();   // 아래 → 위
        for (int i = stack.Count - 1; i >= 0; i--) target.AddBottomSource(stack[i]);
        RecalculateFieldBuffs();
        Diag45.Hit("breedingPlacedUnder");
        return true;
    }

    /// <summary>"treated as having N DP" — 맨 위 카드의 DP 대신 N (StaticStat.AllyDigimonDpSet45)</summary>
    private int DpSetDelta45(PlayerZones owner, PlayerZones enemy, CardInstance inst)
    {
        int? set = null;
        foreach (var (aura, actx) in DigimonAuras(owner, enemy, StaticStat.AllyDigimonDpSet45))
            if (aura.AppliesTo(actx, inst)) set = Math.Max(set ?? 0, aura.Value(actx));
        if (set is not int v) return 0;
        Diag45.Hit("dpSet");
        return v - inst.TopCard.DP;
    }
}

/// <summary>v0.45.0 시뮬레이션 집계 — 새 동작이 실제로 처리된 횟수 (이름별)</summary>
public static class Diag45
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);

    /// <summary>집계 이름 (한 번도 처리되지 않은 것도 보고하려고 미리 둔다)</summary>
    public static readonly List<string> Names = new();

    public static void Register(params string[] names)
    {
        lock (Names) foreach (var n in names) if (!Names.Contains(n)) Names.Add(n);
    }

    public static void Hit(string name)
    {
        Counts.AddOrUpdate(name, 1, (_, v) => v + 1);
        if (name.StartsWith("deletionView:", StringComparison.Ordinal)) return;
        lock (Names) if (!Names.Contains(name)) Names.Add(name);
    }

    /// <summary>집계하고 true (조건식 안에서 쓰기 좋게)</summary>
    public static bool HitTrue(string name) { Hit(name); return true; }

    public static int Get(string name) => Counts.TryGetValue(name, out var v) ? v : 0;

    public static void Reset() => Counts.Clear();

    public static string Report()
    {
        lock (Names) return string.Join(" ", Names.Select(n => $"{n}={Get(n)}"));
    }

    /// <summary>소멸 직전 모습으로 결과가 바뀐 카드 (감사)</summary>
    public static string DeletionViewReport() =>
        string.Join(" ", Counts.Where(kv => kv.Key.StartsWith("deletionView:", StringComparison.Ordinal)).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key["deletionView:".Length..]}={kv.Value}"));
}
