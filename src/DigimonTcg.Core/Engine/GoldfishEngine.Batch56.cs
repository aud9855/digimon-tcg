using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.56.0 다른 효과의 처리 일부로 처리하는 효과에서 유발한 효과는 바깥 효과가 끝난 뒤 (30차 묶음).
///  - 종합 룰 15-8-3-2: 유발형 효과는 룰이나 효과의 처리 중에는 발휘할 수 없다
///  - 15-4-3-2: 1개의 룰이나 효과가 해결되기까지 유발한 효과는 동시 유발 → 처리 중인 효과가 끝난 뒤 발휘 대기에서 1개씩 (15-4-4)
///  - 9-1-2 · 9-1-5: 사용한 옵션의 【메인】은 사용 절차에 이어서 끼어들기 없이 처리한다 — 효과로 사용했으면 그 효과의 처리 중이다
///  - 공식 Q&A EX8-037 Q1: 효과로 사용한 옵션의 효과를 발휘한 뒤 "이 효과로 사용했다면" 이후를 처리 (옵션의 【메인】은 바깥 효과의 한가운데)
/// v0.54.0 · v0.55.0은 효과(ResolveCore) 하나마다 모아 그 효과가 끝나면 발휘했다 — 효과로 사용한 옵션의 【메인】 · "~의 【등장 시】 효과를 발휘한다"로
/// 발휘한 효과는 안쪽 효과가 끝날 때(바깥 효과의 한가운데) 유발한 효과가 발휘되었다. 이제는 바깥 효과의 목록에 더해 바깥 효과가 끝난 뒤.
/// 안쪽 효과로 등장한 카드의 【등장 시】 · "등장했을 때"도 바깥 효과가 끝난 뒤 (안쪽 효과로 함께 등장한 카드끼리는 그대로 동시 등장).
/// 효과 밖에서 쓴 옵션 · 발휘 대기에서 발휘하는 유발 효과 · 즉시형 효과("~하려 할 때")는 전과 같다.
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>끄면 v0.55.0까지처럼 안쪽 효과가 끝날 때 발휘 (시뮬레이션 비교용)</summary>
    public static bool JoinOuterEffect56 { get; set; } = true;

    /// <summary>다음 효과 처리(ResolveCore)가 바깥 효과의 처리 일부인지 (0이면 아님)</summary>
    private int _joinOuter56;

    /// <summary>바깥 효과의 처리 일부로 효과를 처리한다 (효과 처리 중일 때만 — 효과 밖이면 전과 같이 그 효과가 끝난 뒤)</summary>
    internal T AsPartOfOuterEffect56<T>(Func<T> run)
    {
        bool join = Deferring54;
        if (join) _joinOuter56++;
        try { return run(); }
        finally { if (join) _joinOuter56--; }
    }

    /// <summary>효과 처리(ResolveCore)를 시작한다: joined = 바깥 효과의 유발 목록에 더하는지. 안쪽의 효과 처리는 다시 정한다</summary>
    internal int EnterResolve56(out bool joined)
    {
        int saved = _joinOuter56;
        joined = JoinOuterEffect56 && saved > 0 && Deferring54;
        if (joined) Diag46.Hit("joinedOuter56");
        _joinOuter56 = 0;
        return saved;
    }

    internal void ExitResolve56(int saved) => _joinOuter56 = saved;

    /// <summary>바깥 효과의 처리 일부인 효과로 등장한 카드: 【등장 시】 · "등장했을 때"는 바깥 효과가 끝난 뒤 (그 효과로 함께 등장한 카드끼리는 동시)</summary>
    internal void EndPlayBatchJoined56(PlayBatch47? b)
    {
        if (b == null) return;
        _playBatch47 = b.Outer;
        if (b.Items.Count == 0) return;
        Diag46.Hit("playJoined56");
        Defer54(() => ProcessPlayBatch47(b));
    }
}

/// <summary>
/// v0.56.0 얻은 진화원 카드의 효과 · 링크 카드의 링크 효과도 카드(객체)마다 (29차 묶음 CardIdentity55의 나머지).
///  - 종합 룰 4-3-3 (진화원 효과) · 4-3-5 "디지몬은 그 디지몬이 가진 링크 카드의 링크 효과를 얻는다" · 15-14-1-3 "[턴 X회]는 카드 1장마다"
///  - "이 디지몬은 진화원의 [X]의 효과를 모두 얻는다"로 같은 카드 2장이면 2장의 효과를 얻는다 (지속 효과는 2번, 유발은 장마다, [턴에 1회]도 장마다)
///  - 맨 위 카드와 카드 번호가 같은 진화원 카드의 얻은 효과는 맨 위 카드의 효과와 다른 효과 ([턴에 1회]를 나누지 않는다)
/// 링크 카드는 1장이 상한(4-9-5)이라 같은 카드 2장은 상한을 늘리는 효과가 있을 때만.
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>얻은 진화원 카드의 메인 효과면 [턴에 1회] 기록 이름에 그 카드(객체)를 붙인다</summary>
    internal string GainedOnceKey56(EffectContext ctx, CardData card, CardEffect effect, string key)
    {
        if (!CardIdentity55.PerCard || ctx.Self is not { } self || effect.Source != EffectSource.Main || ReferenceEquals(card, self.TopCard)) return key;
        bool gained = self.EvolutionSources.Any(s => ReferenceEquals(s, card)) || (ctx.DeletedSources44?.Any(s => ReferenceEquals(s, card)) ?? false);
        return gained ? key + "#G" + CardIdentity55.Of(card) : key;
    }

    /// <summary>얻은 진화원 카드의 [메인] 기동 효과의 사용 기록 이름 — 카드(객체)마다 (끄면 카드 번호마다)</summary>
    private static object GainedMainKey56(CardInstance d, CardData gained) =>
        CardIdentity55.PerCard ? (d, (object)gained, "gained") : (d, gained.CardId, "gained");

    private static bool SameGained56(CardData a, CardData b) =>
        CardIdentity55.PerCard ? ReferenceEquals(a, b) : a.CardId == b.CardId;
}
