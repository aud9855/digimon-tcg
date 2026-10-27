using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.44.0 미지원 문장 18차 묶음의 엔진 처리</summary>
public partial class GoldfishEngine
{
    /// <summary>육성 에어리어의 디지몬을 소멸시킨다 (비용 — 진화원과 함께 트래시로, ≪오버플로우≫). 소멸시켰으면 true</summary>
    internal bool DeleteBreeding44(PlayerZones owner)
    {
        if (owner.BreedingArea is not { } b || b.TopCard.Type != CardType.Digimon) return false;
        owner.BreedingArea = null;
        var stack = b.GetEvolutionStackBottomUp();
        ApplyOverflow(owner, stack.Append(b.TopCard));
        owner.Trash.Add(b.TopCard);
        while (b.EvolutionSources.Count > 0) owner.Trash.Add(b.EvolutionSources.Pop());
        EffectLog(owner, $"육성 에어리어의 [{b.TopCard.Name}]을(를) 소멸시켰습니다.");
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>테이머를 레스트시키고 실제로 레스트했는지 (액티브가 아니거나 "can't suspend"면 false)</summary>
    internal bool SuspendTamerReturning44(PlayerZones owner, CardData tamer)
    {
        if (!owner.HasActiveTamer(tamer) || TamerSuspendLocked(owner, tamer)) return false;
        SuspendTamer(owner, tamer);
        return true;
    }
}

/// <summary>v0.44.0 시뮬레이션 집계 — 새 동작이 실제로 처리된 횟수 (이름별)</summary>
public static class Diag44
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);

    /// <summary>집계 이름 (한 번도 처리되지 않은 것도 보고하려고 미리 둔다)</summary>
    public static readonly List<string> Names = new()
    {
        "ifNotAfterCost", "suspendThisTamer", "bothSideTamers", "digimonAndOrTamers", "tamerLock", "mindLink", "compound", "blitzStep",
        "chooseEachDelete", "deleteOthersChosen", "forEachDeletedByThis", "opponentAttacksChosen", "opponentPlaysTamer", "eitherTarget",
        "enemyDigimonDpStatic", "wasInSources", "onDeletionPlayThisCard", "grantTwoTargets", "securityDigimonDp", "trashUnder",
        "digivolveIntoThisCard", "dnaIntoThisCard", "handTrim", "forEveryNTrashed", "enemyCardsSkipUnsuspend", "suspendedCardsSkipUnsuspend",
        "deleteBreedingCost", "namedReturnCost", "opponentBottomDeckCost", "changeOrientation", "handToDeckTop", "drawUntil",
        "trashTopOrBottomSecurity", "memoryOrDrawByLevel", "multiTargetVerb", "trashChosenSources", "twoConditions", "forEachRepeat",
        "forEachSuspendedByThis", "twoPlays",
    };

    public static void Register(params string[] names)
    {
        lock (Names) foreach (var n in names) if (!Names.Contains(n)) Names.Add(n);
    }

    public static void Hit(string name)
    {
        Counts.AddOrUpdate(name, 1, (_, v) => v + 1);
        lock (Names) if (!Names.Contains(name)) Names.Add(name);
    }

    public static int Get(string name) => Counts.TryGetValue(name, out var v) ? v : 0;

    public static void Reset() => Counts.Clear();

    public static string Report()
    {
        lock (Names) return string.Join(" ", Names.Select(n => $"{n}={Get(n)}"));
    }
}
