using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.46.0 사건이 일어난 시점에 그 자리에 있던 카드만 유발한다 (종합 룰 15-5-1 "유발 조건을 만족하는 상황이 발생한 시점에서, 그 효과가 유발합니다" ·
/// 15-4-4-3 · 공식 Q&A BT14-073 Q1 "파기된 타이밍에는 이 카드는 배틀 에어리어에 존재하지 않기 때문에 유발하지 않으며" · BT15-052 Q1).
/// 엔진은 사건의 주인공 효과(【소멸 시】 · 【등장 시】 · 【진화 시】 · 옵션의 【메인】)를 먼저 처리한 뒤 "~했을 때" 효과를 알리므로,
/// 그 사이에 등장한 디지몬 · 테이머 · 놓인 옵션 · 트래시에 간 카드가 앞의 사건에 반응하던 것을 막는다 (예: P-145 【소멸 시】로 등장한 BT16-081).
/// 사건과 동시에 그 자리에 놓인 카드(종합 룰 15-5-3 — 등장한 디지몬 자신 · 소멸해 트래시에 간 카드)는 그대로 유발한다
/// </summary>
internal sealed class EventWitness46
{
    private readonly HashSet<CardInstance> _digimon = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PlayerZones, List<CardData>> _tamers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PlayerZones, List<CardData>>? _options;
    private readonly Dictionary<PlayerZones, HashSet<CardData>> _trash = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PlayerZones, CardInstance?> _breeding = new(ReferenceEqualityComparer.Instance);

    internal EventWitness46(bool options, params PlayerZones[] sides)
    {
        if (options) _options = new(ReferenceEqualityComparer.Instance);
        foreach (var z in sides)
        {
            foreach (var d in z.BattleArea) _digimon.Add(d);
            _tamers[z] = z.Tamers.ToList();
            _options?.Add(z, z.OptionsInPlay.Select(p => p.Card).ToList());
            var t = new HashSet<CardData>(ReferenceEqualityComparer.Instance);
            foreach (var c in z.Trash) t.Add(c);
            _trash[z] = t;
            _breeding[z] = z.BreedingArea;
        }
    }

    internal bool Saw(CardInstance d) => _digimon.Contains(d);

    internal bool SawBreeding(PlayerZones z, CardInstance b) => _breeding.TryGetValue(z, out var x) && ReferenceEquals(x, b);

    internal bool SawTrash(PlayerZones z, CardData c) => _trash.TryGetValue(z, out var t) && t.Contains(c);

    /// <summary>그 시점의 테이머 (같은 카드 여러 장 — 앞에서부터 셈)</summary>
    internal List<CardData> TamerBudget(PlayerZones z) => _tamers.TryGetValue(z, out var l) ? l.ToList() : new List<CardData>();

    /// <summary>그 시점에 놓여 있던 옵션 (null = 옵션은 거르지 않는다)</summary>
    internal List<CardData>? OptionBudget(PlayerZones z) => _options == null ? null : _options.TryGetValue(z, out var l) ? l.ToList() : new List<CardData>();

    internal static bool Consume(List<CardData> budget, CardData c)
    {
        int i = budget.FindIndex(x => ReferenceEquals(x, c));
        if (i < 0) return false;
        budget.RemoveAt(i);
        return true;
    }
}

public partial class GoldfishEngine
{
    /// <summary>사건 시점의 카드만 유발 (감사용 스위치 — 끄면 v0.45.0까지처럼 알릴 때 있는 카드 전부)</summary>
    public static bool EventWitnessEnabled46 { get; set; } = true;

    /// <summary>지금 양쪽 필드 · 트래시 · 육성의 카드를 기록한다 (options = 놓인 옵션도 거를지)</summary>
    internal EventWitness46? CaptureWitness46(bool options = true) =>
        EventWitnessEnabled46 ? new EventWitness46(options, State.Player, State.DummyOpponent) : null;

    /// <summary>【진화 시】를 처리하기 직전의 기록 — 그 디지몬의 "진화했을 때" 알림에서 쓴다</summary>
    private readonly Dictionary<CardInstance, EventWitness46> _digivolveWitness46 = new(ReferenceEqualityComparer.Instance);

    private void NoteDigivolveWitness46(CardInstance target)
    {
        if (CaptureWitness46() is { } w) _digivolveWitness46[target] = w;
    }

    private EventWitness46? TakeDigivolveWitness46(EffectTrigger trigger, CardInstance? other)
    {
        if (trigger != EffectTrigger.AllyDigimonDigivolved || other == null) return null;
        if (!_digivolveWitness46.Remove(other, out var w)) return null;
        return w;
    }

    /// <summary>사건 뒤에 그 자리에 온 카드라 알리지 않았다 (그 시점 효과가 있을 때만 센다)</summary>
    private static void NoteWitnessSkip46(EffectTrigger trigger, CardData? card, CardInstance? d = null)
    {
        var cards = d != null ? d.GetEvolutionStackBottomUp().Append(d.TopCard) : card != null ? new[] { card } : Enumerable.Empty<CardData>();
        foreach (var c in cards)
        {
            if (CardScripts.For(c)?.Effects.Any(e => e.Trigger == trigger) != true) continue;
            Diag46.Hit("witnessSkip");
            Diag46.Hit("witness:" + trigger + ":" + c.CardId);
            return;
        }
    }

    private static bool NoteSkipFalse46(EffectTrigger trigger, CardData c) { NoteWitnessSkip46(trigger, c); return false; }

    /// <summary>[시작 시] · [종료 시] — 그 시점의 테이머 · 옵션 · 트래시 · 육성만 (디지몬은 원래 목록을 먼저 만든다)</summary>
    private void RunPhaseTamers46(EffectTrigger trigger, PlayerZones owner, EventWitness46? w)
    {
        var budget = w?.TamerBudget(owner);
        foreach (var tamer in owner.Tamers.ToList())
        {
            if (budget != null && !EventWitness46.Consume(budget, tamer)) { NoteWitnessSkip46(trigger, tamer); continue; }
            RunEffects(trigger, owner, null, tamer, includeInherited: false);
        }
    }
}

/// <summary>v0.46.0 시뮬레이션 집계 — 새 동작이 실제로 처리된 횟수 (이름별)</summary>
public static class Diag46
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
        if (name.StartsWith("witness:", StringComparison.Ordinal)) return;
        lock (Names) if (!Names.Contains(name)) Names.Add(name);
    }

    public static bool HitTrue(string name) { Hit(name); return true; }

    public static int Get(string name) => Counts.TryGetValue(name, out var v) ? v : 0;

    public static void Reset() => Counts.Clear();

    public static string Report()
    {
        lock (Names) return string.Join(" ", Names.Select(n => $"{n}={Get(n)}"));
    }

    /// <summary>사건 뒤에 와서 알리지 않은 카드 (시점:카드 번호)</summary>
    public static string WitnessReport() =>
        string.Join(" ", Counts.Where(kv => kv.Key.StartsWith("witness:", StringComparison.Ordinal)).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key["witness:".Length..]}={kv.Value}"));
}

public partial class GoldfishEngine
{
    // =====================================================================
    // "When your effects add to decks" (BT26-015 — 공식 "자신의 효과로 덱에 카드가 추가되었을 때"): 효과 1개를 처리하는 동안 어느 쪽 덱이든
    // 처리 전에 없던 카드가 들어왔으면 그 효과가 끝난 뒤 알린다 (공개했다가 되돌린 카드는 원래 덱에 있던 카드라 셈하지 않는다)
    // =====================================================================

    private readonly Dictionary<PlayerZones, int> _deckWatchDepth46 = new(ReferenceEqualityComparer.Instance);

    internal sealed record DeckWatch46(HashSet<CardData> Mine, HashSet<CardData> Theirs);

    private static bool ListensDeckAdd46(CardData c) => CardScripts.For(c)?.Effects.Any(e => e.Trigger == EffectTrigger.AllyEffectAddedToDeck46) == true;

    private bool HasDeckAddListener46(PlayerZones owner) =>
        owner.BattleArea.Any(d => ListensDeckAdd46(d.TopCard) || d.FaceUpSources.Any(ListensDeckAdd46))
        || owner.Tamers.Any(ListensDeckAdd46) || owner.OptionsInPlay.Any(p => ListensDeckAdd46(p.Card))
        || owner.BreedingArea is { } b && (ListensDeckAdd46(b.TopCard) || b.FaceUpSources.Any(ListensDeckAdd46));

    internal DeckWatch46? BeginDeckAddWatch46(PlayerZones owner)
    {
        int depth = _deckWatchDepth46.GetValueOrDefault(owner) + 1;
        _deckWatchDepth46[owner] = depth;   // 같은 쪽 효과 안의 효과는 바깥 효과가 함께 본다
        if (depth > 1 || !HasDeckAddListener46(owner)) return null;
        var mine = new HashSet<CardData>(owner.MainDeck, ReferenceEqualityComparer.Instance);
        var theirs = new HashSet<CardData>(EnemyOf(owner).MainDeck, ReferenceEqualityComparer.Instance);
        return new DeckWatch46(mine, theirs);
    }

    internal void EndDeckAddWatch46(DeckWatch46? w, PlayerZones owner)
    {
        _deckWatchDepth46[owner] = Math.Max(0, _deckWatchDepth46.GetValueOrDefault(owner) - 1);
        if (w == null || State.IsGameOver) return;
        bool added = owner.MainDeck.Any(c => !w.Mine.Contains(c)) || EnemyOf(owner).MainDeck.Any(c => !w.Theirs.Contains(c));
        if (!added) return;
        Diag46.Hit("effectAddedToDeck");
        RunOrDefer(() => { if (!State.IsGameOver) FireForSide(EffectTrigger.AllyEffectAddedToDeck46, owner, null); });
    }
}
