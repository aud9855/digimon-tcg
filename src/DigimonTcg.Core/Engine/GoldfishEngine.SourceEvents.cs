using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 진화원이 효과로 파기되었을 때 · 효과로 진화원에 카드가 놓였을 때 (v0.28.0).
/// 효과 하나(가장 바깥 효과)를 처리하기 전후의 배틀 에어리어 디지몬 진화원을 비교해, 그 효과가 끝난 뒤에 시점을 알린다.
/// 진화원을 바꾸는 곳(파기 · 아래에 놓기 · ≪마인드 링크≫ · ≪디지버스트≫ 비용 …)이 여러 군데라 한 곳에서 비교한다.
///  - 공식 Q&A BT7-005: 진화(효과로 진화해도)로 겹친 카드는 "효과로 진화원이 늘어났을 때"가 아니다 → 진화 전 맨 위 카드는 빼고 센다
///  - 공식 Q&A BT2-085 · P-004: 패로 되돌린 디지몬의 진화원이 트래시로 가는 것은 "진화원을 파기했을 때"가 아니다 → 필드를 떠난 디지몬은 보지 않는다
///  - 공식 Q&A BT10-006: 자신의 효과로 파기해도 "효과로 진화원에서 파기되었을 때"
///  - ≪퇴화≫는 맨 위 카드를 파기하는 것이므로 진화원 파기가 아니다 (맨 위가 된 진화원은 트래시로 가지 않았다)
/// </summary>
public partial class GoldfishEngine
{
    private sealed record SourceSnapshot(CardInstance Digimon, PlayerZones Zones, CardData Top, CardData[] Sources);

    private List<SourceSnapshot>? _sourceWatch;
    private Dictionary<PlayerZones, CardData[]>? _sourceWatchTrash;
    private Dictionary<PlayerZones, CardData[]>? _sourceWatchHand;   // "When an effect adds a card to your hand" (v0.29.0)
    private readonly Dictionary<PlayerZones, int> _sourceWatchDraws = new(ReferenceEqualityComparer.Instance);   // 그동안 드로우했는지 (v0.30.0)
    private int _sourceNotifyTurn = -1, _sourceNotifyCount;
    private int _handAddNotifyTurn = -1, _handAddNotifyCount;

    /// <summary>진화원 시점을 알리는 동안: 파기되었거나 놓인 카드 (조건 "Tamer cards are placed ~" 등이 본다)</summary>
    internal IReadOnlyList<CardData> CurrentSourceCards { get; private set; } = new List<CardData>();

    /// <summary>
    /// "When one of your Digimon's effects adds cards to your hand" (v0.30.0): 지금 알리는 "효과로 패가 늘어났다"가 자신의 디지몬의 효과(배틀 에어리어 디지몬의
    /// 메인 · 진화원 효과, [소멸 시] 포함 — 가장 바깥 효과 기준)였는지. 공식 Q&A BT15-002 · BT15-026 · BT15-083: 1장 드로우 뒤 1장 버려 장수가 같아도 발휘
    /// </summary>
    internal bool HandAddedByDigimonEffect { get; private set; }

    private readonly HashSet<PlayerZones> _digimonHandAdds = new(ReferenceEqualityComparer.Instance);

    /// <summary>디지몬의 효과(Self가 있는 효과) 하나를 처리한 뒤: 그 효과(안쪽 효과 포함)로 패가 늘었으면 적어 둔다</summary>
    internal void NoteDigimonHandAdd(PlayerZones zones, CardData[] handBefore, int drawsBefore)
    {
        if (_sourceWatch != null && (Diff(zones.Hand, handBefore).Count > 0 || DrawSerial.GetValueOrDefault(zones) != drawsBefore))
            _digimonHandAdds.Add(zones);
    }

    /// <summary>진화원 시점을 일으킨 효과의 주인 ("one of your effects places ~")</summary>
    internal PlayerZones? CurrentSourceEffectOwner { get; private set; }

    /// <summary>가장 바깥 효과를 처리하기 전: 양쪽 배틀 에어리어 디지몬의 진화원과 트래시를 적어 둔다. 반환: 이 효과가 가장 바깥인지</summary>
    internal bool BeginSourceWatch()
    {
        if (_sourceWatch != null)
            return false;
        _sourceWatch = new List<SourceSnapshot>();
        _sourceWatchTrash = new Dictionary<PlayerZones, CardData[]>();
        _sourceWatchHand = new Dictionary<PlayerZones, CardData[]>();
        foreach (var zones in new[] { State.Player, State.DummyOpponent })
        {
            foreach (var d in zones.BattleArea)
                _sourceWatch.Add(new SourceSnapshot(d, zones, d.TopCard, d.EvolutionSources.ToArray()));
            _sourceWatchTrash[zones] = zones.Trash.ToArray();
            _sourceWatchHand[zones] = zones.Hand.ToArray();
            _sourceWatchDraws[zones] = DrawSerial.GetValueOrDefault(zones);
        }
        BeginWatch40();   // v0.40.0 덱 · 테이머 아래 (GoldfishEngine.Batch40.cs)
        return true;
    }

    /// <summary>가장 바깥 효과를 처리한 뒤: 바뀐 진화원을 찾아 시점을 알린다 (처리 중인 효과가 끝난 뒤에)</summary>
    internal void EndSourceWatch(PlayerZones effectOwner)
    {
        var snaps = _sourceWatch;
        var trashBefore = _sourceWatchTrash;
        var handBefore = _sourceWatchHand;
        _sourceWatch = null;
        _sourceWatchTrash = null;
        _sourceWatchHand = null;
        var digimonAdds = _digimonHandAdds.ToList();
        _digimonHandAdds.Clear();
        if (snaps == null || trashBefore == null)
            return;
        EndWatch40(snaps, effectOwner);   // v0.40.0 맨 위 카드가 진화원으로 · 진화원이 덱으로 · 테이머 아래에 놓였을 때 (GoldfishEngine.Batch40.cs)

        // 효과로 패에 카드가 들어갔다 (드로우 포함 — 종합 룰 4-14-1 "드로우 = 덱에서 뽑아 패에 더하는 것"). 효과 하나가 끝난 뒤 쪽마다 1번
        if (handBefore != null)
            foreach (var (zones, before) in handBefore)
            {
                var addedToHand = Diff(zones.Hand, before);
                // 뽑은 카드를 같은 효과로 곧 버려 패가 그대로여도 "패가 늘어났다" (공식 Q&A BT15-002 · BT15-026 · BT15-083) — v0.30.0
                bool drew = DrawSerial.GetValueOrDefault(zones) != _sourceWatchDraws.GetValueOrDefault(zones);
                if (addedToHand.Count == 0 && !drew) continue;
                var zonesOfHand = zones;
                var firstAdded = addedToHand.Count > 0 ? addedToHand[0] : zones.Trash.LastOrDefault();
                bool byDigimon = digimonAdds.Contains(zones);
                RunOrDefer(() =>
                {
                    if (State.IsGameOver) return;
                    if (_handAddNotifyTurn != State.TurnSequence) { _handAddNotifyTurn = State.TurnSequence; _handAddNotifyCount = 0; }
                    if (++_handAddNotifyCount > 100) return;   // 서로 부르며 끝없이 이어지는 것을 막는다
                    var outerKind = HandAddedByDigimonEffect;
                    HandAddedByDigimonEffect = byDigimon;
                    try { FireForSide(EffectTrigger.AllyHandAddedByEffect, zonesOfHand, null, triggerCard: firstAdded); }
                    finally { HandAddedByDigimonEffect = outerKind; }
                });
            }

        // 트래시에 새로 들어간 카드 (같은 카드 데이터가 여러 장일 수 있어 장수로 센다)
        var trashGain = new Dictionary<PlayerZones, Dictionary<CardData, int>>();
        foreach (var (zones, before) in trashBefore)
        {
            var gain = Count(zones.Trash);
            foreach (var x in before)
                if (gain.TryGetValue(x, out int n)) gain[x] = n - 1;
            trashGain[zones] = gain;
        }

        foreach (var snap in snaps)
        {
            if (!snap.Zones.BattleArea.Contains(snap.Digimon))
                continue;   // 소멸 · 패 · 덱으로 떠난 디지몬의 진화원은 "진화원 파기"가 아니다
            var after = snap.Digimon.EvolutionSources.ToList();
            var removed = Diff(snap.Sources, after);
            var added = Diff(after, snap.Sources);
            // 진화로 겹친 카드 (Q&A BT7-005) — 새 맨 위 카드가 원래 진화원이었다면 진화가 아니라
            // "겹쳐져 있는 카드를 위에서부터 1장 진화원 아래에 놓았다" (Q&A EX5-007, v0.29.0) → 효과로 진화원에 놓인 것
            if (!ReferenceEquals(snap.Digimon.TopCard, snap.Top) && !snap.Sources.Contains(snap.Digimon.TopCard))
                added.Remove(snap.Top);
            var gain = trashGain[snap.Zones];
            var trashed = new List<CardData>();
            foreach (var x in removed)
            {
                if (gain.TryGetValue(x, out int n) && n > 0)
                {
                    gain[x] = n - 1;
                    trashed.Add(x);
                }
            }
            if (trashed.Count == 0 && added.Count == 0)
                continue;
            var digimon = snap.Digimon;
            var zonesOf = snap.Zones;
            RunOrDefer(() => NotifySourcesChanged(zonesOf, digimon, trashed, added, effectOwner));
        }
    }

    private static Dictionary<CardData, int> Count(IEnumerable<CardData> cards)
    {
        var d = new Dictionary<CardData, int>(ReferenceEqualityComparer.Instance);
        foreach (var c in cards) d[c] = d.GetValueOrDefault(c) + 1;
        return d;
    }

    /// <summary>a에는 있고 b에는 없는 카드 (장수 기준)</summary>
    private static List<CardData> Diff(IEnumerable<CardData> a, IEnumerable<CardData> b)
    {
        var rest = Count(b);
        var result = new List<CardData>();
        foreach (var x in a)
        {
            if (rest.TryGetValue(x, out int n) && n > 0) rest[x] = n - 1;
            else result.Add(x);
        }
        return result;
    }

    private void NotifySourcesChanged(PlayerZones zones, CardInstance digimon, List<CardData> trashed, List<CardData> added, PlayerZones effectOwner)
    {
        if (State.IsGameOver)
            return;
        // 서로 부르며 끝없이 이어지는 것을 막는다 (턴마다 넉넉히)
        if (_sourceNotifyTurn != State.TurnSequence) { _sourceNotifyTurn = State.TurnSequence; _sourceNotifyCount = 0; }
        if (++_sourceNotifyCount > 200)
            return;
        var outerCards = CurrentSourceCards;
        var outerOwner = CurrentSourceEffectOwner;
        CurrentSourceEffectOwner = effectOwner;
        try
        {
            if (trashed.Count > 0)
            {
                CurrentSourceCards = trashed;
                RunEffects(EffectTrigger.SourceTrashedThis, zones, digimon, null, sources: trashed);
                FireForSide(EffectTrigger.AllySourcesTrashed, zones, digimon, triggerCard: trashed[0]);
                FireForSide(EffectTrigger.OpponentSourcesTrashed, EnemyOf(zones), digimon, triggerCard: trashed[0]);
            }
            if (added.Count > 0 && zones.BattleArea.Contains(digimon))
            {
                CurrentSourceCards = added;
                FireForSide(EffectTrigger.AllySourcesAdded, zones, digimon, triggerCard: added[0]);
                FireForSide(EffectTrigger.OpponentSourcesAdded, EnemyOf(zones), digimon, triggerCard: added[0]);   // v0.33.0
            }
        }
        finally
        {
            CurrentSourceCards = outerCards;
            CurrentSourceEffectOwner = outerOwner;
        }
    }
}

public partial class GoldfishEngine
{
    /// <summary>시큐리티를 섞는다 ("Then, shuffle your security stack" — 대전 준비와 같은 난수)</summary>
    internal void ShuffleSecurity(PlayerZones zones)
    {
        var shuffled = zones.SecurityStack.OrderBy(_ => _setupRng.Next()).ToList();
        zones.SecurityStack.Clear();
        zones.SecurityStack.AddRange(shuffled);
    }
}
