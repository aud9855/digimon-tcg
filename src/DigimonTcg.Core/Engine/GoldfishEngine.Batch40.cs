using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.40.0 미지원 문장 14차 묶음의 엔진 쪽 (SentenceParser.Batch40.cs) — 시뮬레이션에서 새 동작이 실제로 돈 횟수</summary>
public static class Diag40
{
    public static int AllianceSuspend, WouldDigivolve, TopPlacedUnder, SourcesToDeck, UnderTamerPlaced, OwnerAura, MultiLevelDelete, SameLevelEvo, MainPhaseUnsuspend,
        SetCost, OpponentEvoCost, Detach, Decode, SecuritySearch, RevealVariant, KeywordGrant, EventMisc;
    public static void Hit(ref int counter) => System.Threading.Interlocked.Increment(ref counter);
    public static void Reset() => AllianceSuspend = WouldDigivolve = TopPlacedUnder = SourcesToDeck = UnderTamerPlaced = OwnerAura = MultiLevelDelete = SameLevelEvo = MainPhaseUnsuspend
        = SetCost = OpponentEvoCost = Detach = Decode = SecuritySearch = RevealVariant = KeywordGrant = EventMisc = 0;
}

public partial class GoldfishEngine
{
    // =====================================================================
    // ≪연계≫로 레스트 ("When one of your Digimon is suspended by an ＜Alliance＞ effect" — EX4-032 · EX4-033 · EX4-034)
    // =====================================================================

    /// <summary>지금 레스트하는 디지몬이 ≪연계≫의 효과로 레스트하는 중인지</summary>
    internal bool SuspendingForAlliance40 { get; private set; }

    /// <summary>효과로 진화한 횟수 (EvolveByEffect) — "If digivolved by this effect"</summary>
    internal int EffectEvolveSerial40 { get; private set; }

    private void SuspendForAlliance40(CardInstance ally)
    {
        SuspendingForAlliance40 = true;
        try { Suspend(ally); }
        finally { SuspendingForAlliance40 = false; }
    }

    // =====================================================================
    // 액티브 페이즈 · 메인 페이즈 ("When an opponent's Digimon becomes unsuspended during a main phase" — BT9-089)
    // =====================================================================

    private int _unsuspendPhase40;

    /// <summary>지금 액티브가 되는 것이 메인 페이즈의 일인지 (액티브 페이즈에 액티브가 된 것은 아님)</summary>
    internal bool UnsuspendingInMainPhase40 => _unsuspendPhase40 == 0 && (_aiTurn != null || State.CurrentPhase == Phase.Main);

    // =====================================================================
    // 진화하려 할 때 ("When this Digimon would digivolve, ~" — BT8-024 · BT14-018)
    // =====================================================================

    private int _wouldDigivolveDepth40;

    /// <summary>진화하기 직전 (진화할 카드가 패를 떠난 뒤 · 코스트를 내기 전): "이 디지몬이 진화하려 할 때" 효과</summary>
    private void NotifyWouldDigivolve40(PlayerZones owner, CardInstance target, CardData evoCard)
    {
        if (_wouldDigivolveDepth40 > 0 || State.IsGameOver || !owner.BattleArea.Contains(target) || target.TamerProxyOf != null)
            return;
        if (!AnyScriptHas40(target, EffectTrigger.ThisDigimonWouldDigivolve))
            return;
        _wouldDigivolveDepth40++;
        try { RunEffects(EffectTrigger.ThisDigimonWouldDigivolve, owner, target, target.TopCard, triggerCard: evoCard); }
        finally { _wouldDigivolveDepth40--; }
    }

    private static bool AnyScriptHas40(CardInstance d, EffectTrigger t) =>
        (CardScripts.For(d.TopCard)?.Effects.Any(e => e.Trigger == t) ?? false)
        || d.EvolutionSources.Any(s => CardScripts.For(s)?.Effects.Any(e => e.Trigger == t) ?? false);

    // =====================================================================
    // 효과 하나가 끝난 뒤 비교 (GoldfishEngine.SourceEvents.cs의 BeginSourceWatch · EndSourceWatch에 이어서)
    //  - 맨 위 카드가 효과로 진화원에 놓였을 때 (공식 Q&A EX5-001 · EX5-065: 진화로 진화원이 된 것은 아님)
    //  - 진화원에서 덱으로 되돌아갔을 때 (BT11-065 · BT18-065 · BT21-058 "[Vemmon]")
    //  - 테이머 아래에 카드가 놓였을 때 (BT10-093 — 엔진은 테이머 아래를 쪽마다 한 곳에 모은다)
    // =====================================================================

    private Dictionary<PlayerZones, CardData[]>? _watchDeck40;
    private Dictionary<PlayerZones, CardData[]>? _watchUnder40;

    private void BeginWatch40()
    {
        _watchDeck40 = new Dictionary<PlayerZones, CardData[]>(ReferenceEqualityComparer.Instance);
        _watchUnder40 = new Dictionary<PlayerZones, CardData[]>(ReferenceEqualityComparer.Instance);
        foreach (var zones in new[] { State.Player, State.DummyOpponent })
        {
            _watchDeck40[zones] = zones.MainDeck.ToArray();
            _watchUnder40[zones] = zones.UnderTamers.ToArray();
        }
    }

    private void EndWatch40(List<SourceSnapshot> snaps, PlayerZones effectOwner)
    {
        var deckBefore = _watchDeck40;
        var underBefore = _watchUnder40;
        _watchDeck40 = null;
        _watchUnder40 = null;
        if (deckBefore == null || underBefore == null)
            return;
        var deckGain = new Dictionary<PlayerZones, Dictionary<CardData, int>>(ReferenceEqualityComparer.Instance);
        foreach (var (zones, before) in deckBefore)
            deckGain[zones] = Count(Diff(zones.MainDeck, before));
        foreach (var snap in snaps)
        {
            if (!snap.Zones.BattleArea.Contains(snap.Digimon))
                continue;
            var digimon = snap.Digimon;
            var zonesOf = snap.Zones;
            // 맨 위 카드가 진화원으로 (새 맨 위 카드가 원래 진화원 — 진화가 아님)
            if (!ReferenceEquals(digimon.TopCard, snap.Top) && snap.Sources.Contains(digimon.TopCard)
                && zonesOf.BattleArea.Any(d => d.EvolutionSources.Contains(snap.Top)))
            {
                bool bottom = ReferenceEquals(digimon.EvolutionSources.LastOrDefault(), snap.Top);
                var placedTop = snap.Top;
                RunOrDefer(() =>
                {
                    if (State.IsGameOver || !zonesOf.BattleArea.Contains(digimon)) return;
                    Diag40.Hit(ref Diag40.TopPlacedUnder);
                    var outerBottom = TopPlacedAsBottom40;
                    var outerCards = CurrentSourceCards;
                    TopPlacedAsBottom40 = bottom;
                    CurrentSourceCards = new List<CardData> { placedTop };
                    try { FireForSide(EffectTrigger.AllyTopPlacedUnder, zonesOf, digimon, triggerCard: placedTop); }
                    finally { TopPlacedAsBottom40 = outerBottom; CurrentSourceCards = outerCards; }
                });
            }
            // 진화원에서 덱으로
            var removed = Diff(snap.Sources, digimon.EvolutionSources.ToList());
            if (removed.Count > 0 && deckGain.TryGetValue(zonesOf, out var gain))
            {
                var toDeck = new List<CardData>();
                foreach (var x in removed)
                    if (gain.TryGetValue(x, out int n) && n > 0) { gain[x] = n - 1; toDeck.Add(x); }
                if (toDeck.Count > 0)
                    RunOrDefer(() =>
                    {
                        if (State.IsGameOver || !zonesOf.BattleArea.Contains(digimon)) return;
                        Diag40.Hit(ref Diag40.SourcesToDeck);
                        var outerCards = CurrentSourceCards;
                        CurrentSourceCards = toDeck;
                        try { RunEffects(EffectTrigger.ThisSourcesToDeck, zonesOf, digimon, digimon.TopCard); }
                        finally { CurrentSourceCards = outerCards; }
                    });
            }
        }
        foreach (var (zones, before) in underBefore)
        {
            var placed = Diff(zones.UnderTamers, before);
            if (placed.Count == 0 || zones.Tamers.Count == 0)
                continue;
            var zonesOf = zones;
            RunOrDefer(() =>
            {
                if (State.IsGameOver) return;
                Diag40.Hit(ref Diag40.UnderTamerPlaced);
                var outerCards = CurrentSourceCards;
                CurrentSourceCards = placed;
                try { FireForSide(EffectTrigger.AllyUnderTamerPlaced, zonesOf, null, triggerCard: placed[0]); }
                finally { CurrentSourceCards = outerCards; }
            });
        }
    }

    /// <summary>지금 알리는 "맨 위 카드가 진화원에 놓였다"가 맨 아래에 놓인 것인지 (BT22-006 "as its bottom digivolution card")</summary>
    internal bool TopPlacedAsBottom40 { get; private set; }
}

public partial class GoldfishEngine
{
    /// <summary>v0.40.0 상대(enemy)의 디지몬이 가진 "상대의 디지몬이 진화할 때 진화 코스트 +N" (EX3-016 · EX3-019) — target = 진화하는 owner의 디지몬</summary>
    private int OpponentEvoCostIncrease40(PlayerZones owner, CardInstance target)
    {
        var enemy = EnemyOf(owner);
        int total = 0;
        foreach (var d in enemy.BattleArea)
        {
            IEnumerable<(EffectSource Source, System.Func<CardInstance, bool> Who, int Amount, bool OpponentsTurnOnly)> mods =
                (CardScripts.For(d.TopCard)?.OpponentEvoCostIncreases40.Where(x => x.Source == EffectSource.Main) ?? Enumerable.Empty<(EffectSource, System.Func<CardInstance, bool>, int, bool)>())
                .Concat(d.FaceUpSources.GroupBy(s => s.CardId).Select(g => g.First())
                    .SelectMany(s => CardScripts.For(s)?.OpponentEvoCostIncreases40.Where(x => x.Source == EffectSource.Inherited) ?? Enumerable.Empty<(EffectSource, System.Func<CardInstance, bool>, int, bool)>()));
            foreach (var (_, who, amount, oppTurn) in mods)
                if ((!oppTurn || IsTurnOf(owner)) && who(target)) { total += amount; Diag40.Hit(ref Diag40.OpponentEvoCost); }
        }
        return total;
    }
}
