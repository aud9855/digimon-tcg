using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.63.0 미지원 문장 37차 묶음 (엔진 쪽):
///  - 기간 동안 대상을 고르지 않는 "전체 처리" (종합 룰 15-10-2-4-1 · 15-11-2): "All of your opponent's Digimon get -5000 DP for the turn" ·
///    "Your opponent's Digimon can't attack players for the turn" · "All of your Digimon gain ＜Security A. +1＞ until ~" 등.
///    15-11-2-2 "지속하는 전체 처리는 나중에 늘어난 대상에게도 영향을 끼친다" (공식 Q&amp;A ST1-13 Q1 · BT3-105 Q1 · BT9-102 Q1 · BT12-106 Q1) ·
///    15-11-2-3-2 "조건을 만족하지 않게 되면 그 시점에서 대상이 아니게 된다" (공식 Q&amp;A BT1-100 Q1 · BT18-028 Q1) ·
///    15-11-2-3-3 "나중에 조건을 만족하면 그 시점에서 대상이 된다".
///    대상을 고르는 단일 처리("1 of ~" · "up to N" · "it")는 그대로 — 15-11-1-3-2 · 공식 Q&amp;A ST21-03 Q1.
///    처음 대상은 지금처럼 그 자리에서 처리하고 (결과 그대로), 이 기록이 나중에 나온 · 나중에 조건을 만족한 디지몬에게 같은 처리를 하고,
///    조건을 잃은 디지몬에게서는 그 몫을 되돌린다. 나중에 DP 0이 된 디지몬은 효과 처리가 끝난 때 룰 처리로 소멸
///  - 지속 효과로 효과 문장을 주는 카드 · "액티브가 되려 할 때" 카드: 효과를 처리하지 않고 필드에 놓인 카드도 스크립트를 먼저 만들어 본다 (v0.61.0 · v0.62.0의 빠른 확인)
/// </summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // 전체 처리 — 기간 동안 나중에 늘어난 대상에게도
    // =====================================================================

    /// <summary>한 디지몬이 이 처리로 받은 것 (되돌리기용 — 처리 전후의 차이)</summary>
    internal sealed class Delta63
    {
        public int Turn, Temp, OppTemp;
        public List<(int, int)> TimedDp = new(), OppTimedDp = new(), Sa = new(), OppSa = new();
        public List<(Keyword, int)> Kw = new(), OppKw = new();
        public List<(CardType?, bool, int, int, int)> Cat = new();
        public List<(CardData, int, PlayerZones?)> Grant = new();
        public List<(CardData, CardType)> GrantCat = new();
        public List<string> Labels = new();
        public int[] LockBefore = new int[6], LockAfter = new int[6], OppBefore = new int[6], OppAfter = new int[6], OwnBefore = new int[6], OwnAfter = new int[6];
    }

    /// <summary>처리 전의 상태 (목록은 길이만)</summary>
    internal sealed class Before63
    {
        public int Turn, Temp, OppTemp, nTimedDp, nOppTimedDp, nSa, nOppSa, nKw, nOppKw, nCat, nGrant, nGrantCat, nLabels;
        public int[] Lock = new int[6], Opp = new int[6], Own = new int[6];
    }

    private static int LockOf63(CardInstance d, int k) => k switch
    {
        0 => d.CannotAttackUntil, 1 => d.CannotBlockUntil, 2 => d.CannotDigivolveUntil, 3 => d.CannotSuspendUntil, 4 => d.CannotUnsuspendUntil, _ => d.SkipUnsuspendUntil,
    };

    private static void SetLock63(CardInstance d, int k, int v)
    {
        switch (k)
        {
            case 0: d.CannotAttackUntil = v; break;
            case 1: d.CannotBlockUntil = v; break;
            case 2: d.CannotDigivolveUntil = v; break;
            case 3: d.CannotSuspendUntil = v; break;
            case 4: d.CannotUnsuspendUntil = v; break;
            default: d.SkipUnsuspendUntil = v; break;
        }
    }

    internal Before63 Capture63(CardInstance d)
    {
        var b = new Before63
        {
            Turn = State.TurnSequence, Temp = d.TemporaryDpBonus, OppTemp = d.OppTempDp59,
            nTimedDp = d.TimedDpBuffs.Count, nOppTimedDp = d.OppTimedDp59.Count, nSa = d.TimedSecurityAttackBuffs.Count, nOppSa = d.OppTimedSa60.Count,
            nKw = d.TimedKeywords.Count, nOppKw = d.OppTimedKeywords60.Count, nCat = d.CatGiven61.Count, nGrant = d.GrantedEffects.Count, nGrantCat = d.GrantedCat61.Count,
            nLabels = d.ActiveSkillBuffLabels.Count,
        };
        for (int k = 0; k < 6; k++) { b.Lock[k] = LockOf63(d, k); b.Opp[k] = d.OppLockUntil60[k]; b.Own[k] = d.OwnLockUntil60[k]; }
        return b;
    }

    internal static Delta63 Diff63(CardInstance d, Before63 b)
    {
        static List<T> Tail<T>(List<T> l, int n) => n >= 0 && n <= l.Count ? l.Skip(n).ToList() : new List<T>();
        var x = new Delta63
        {
            Turn = b.Turn, Temp = d.TemporaryDpBonus - b.Temp, OppTemp = d.OppTempDp59 - b.OppTemp,
            TimedDp = Tail(d.TimedDpBuffs, b.nTimedDp), OppTimedDp = Tail(d.OppTimedDp59, b.nOppTimedDp),
            Sa = Tail(d.TimedSecurityAttackBuffs, b.nSa), OppSa = Tail(d.OppTimedSa60, b.nOppSa),
            Kw = Tail(d.TimedKeywords, b.nKw).Select(t => (t.Keyword, t.Until)).ToList(), OppKw = Tail(d.OppTimedKeywords60, b.nOppKw).Select(t => (t.Keyword, t.Until)).ToList(),
            Cat = Tail(d.CatGiven61, b.nCat).Select(t => (t.Type, t.Opp, t.Kind, t.Value, t.Until)).ToList(),
            Grant = Tail(d.GrantedEffects, b.nGrant).Select(t => (t.Effect, t.Until, t.Granter)).ToList(),
            GrantCat = Tail(d.GrantedCat61, b.nGrantCat).Select(t => (t.Effect, t.Type)).ToList(),
            Labels = Tail(d.ActiveSkillBuffLabels, b.nLabels),
        };
        x.TimedDp = x.TimedDp.Select(t => (t.Item1, t.Item2)).ToList();
        for (int k = 0; k < 6; k++)
        {
            x.LockBefore[k] = b.Lock[k]; x.LockAfter[k] = LockOf63(d, k);
            x.OppBefore[k] = b.Opp[k]; x.OppAfter[k] = d.OppLockUntil60[k];
            x.OwnBefore[k] = b.Own[k]; x.OwnAfter[k] = d.OwnLockUntil60[k];
        }
        return x;
    }

    /// <summary>받은 몫을 되돌린다 (이미 사라진 것은 건너뛴다)</summary>
    private void Undo63(CardInstance d, Delta63 x)
    {
        static void RemoveOne<T>(List<T> l, T v) { int i = l.LastIndexOf(v); if (i >= 0) l.RemoveAt(i); }
        if (x.Turn == State.TurnSequence) { d.TemporaryDpBonus -= x.Temp; d.OppTempDp59 -= x.OppTemp; }
        foreach (var t in x.TimedDp) RemoveOne(d.TimedDpBuffs, (t.Item1, t.Item2));
        foreach (var t in x.OppTimedDp) RemoveOne(d.OppTimedDp59, (t.Item1, t.Item2));
        foreach (var t in x.Sa) RemoveOne(d.TimedSecurityAttackBuffs, (t.Item1, t.Item2));
        foreach (var t in x.OppSa) RemoveOne(d.OppTimedSa60, (t.Item1, t.Item2));
        foreach (var t in x.Kw) RemoveOne(d.TimedKeywords, (t.Item1, t.Item2));
        foreach (var t in x.OppKw) RemoveOne(d.OppTimedKeywords60, (t.Item1, t.Item2));
        foreach (var t in x.Cat) RemoveOne(d.CatGiven61, (t.Item1, t.Item2, t.Item3, t.Item4, t.Item5));
        foreach (var t in x.Grant) RemoveOne(d.GrantedEffects, (t.Item1, t.Item2, t.Item3));
        foreach (var t in x.GrantCat) RemoveOne(d.GrantedCat61, (t.Item1, t.Item2));
        foreach (var t in x.Labels) RemoveOne(d.ActiveSkillBuffLabels, t);
        for (int k = 0; k < 6; k++)
        {
            if (x.LockAfter[k] != x.LockBefore[k] && LockOf63(d, k) == x.LockAfter[k]) SetLock63(d, k, x.LockBefore[k]);
            if (x.OppAfter[k] != x.OppBefore[k] && d.OppLockUntil60[k] == x.OppAfter[k]) d.OppLockUntil60[k] = x.OppBefore[k];
            if (x.OwnAfter[k] != x.OwnBefore[k] && d.OwnLockUntil60[k] == x.OwnAfter[k]) d.OwnLockUntil60[k] = x.OwnBefore[k];
        }
    }

    /// <summary>전체 처리 하나의 기록</summary>
    private sealed class Whole63
    {
        public required PlayerZones[] Sides;
        public required PlayerZones EffectOwner;
        public CardData? EffectCard;
        public required Func<CardInstance, bool> Match;
        public required Action<CardInstance> Apply;
        public required int Until;
        public bool Dynamic = true;
        public string Name = "";
        public string Tag = "";
        public object? Ctx;
        public readonly Dictionary<CardInstance, Delta63> Given = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<CardInstance, int> Flips = new(ReferenceEqualityComparer.Instance);
        /// <summary>BT12-106 "상대의 카드 전부" — 나중에 등장한 테이머도 (0이면 없음)</summary>
        public int TamerSkipUntil;
        public readonly HashSet<CardData> TamersSeen = new(ReferenceEqualityComparer.Instance);
    }

    private readonly List<Whole63> _whole63 = new();
    private bool _sweeping63;
    private readonly List<CardInstance> _zeroPending63 = new();

    /// <summary>처리 시작: 대상 쪽 디지몬 전부의 상태를 적어 둔다 (나중에 처리 결과와 비교)</summary>
    internal Dictionary<CardInstance, Before63> WholeBegin63(IEnumerable<PlayerZones> sides)
    {
        var map = new Dictionary<CardInstance, Before63>(ReferenceEqualityComparer.Instance);
        foreach (var z in sides)
            foreach (var d in z.BattleArea)
                map[d] = Capture63(d);
        return map;
    }

    /// <summary>
    /// 처리 끝: 기간 동안의 전체 처리로 기록한다. first = 처음 처리한 대상 (그 자리에서 받은 몫), apply = 나중의 대상 1마리에게 같은 처리
    /// </summary>
    internal void WholeEnd63(EffectContext c, IReadOnlyList<PlayerZones> sides, Func<CardInstance, bool> match, Dictionary<CardInstance, Before63> before,
        IEnumerable<CardInstance> first, int until, Action<CardInstance> apply, bool dynamic, string tag = "")
    {
        if (until < State.TurnSequence || State.IsGameOver) return;
        var rec = new Whole63
        {
            Sides = sides.ToArray(), EffectOwner = c.Owner, EffectCard = CurrentEffectCard ?? c.Card, Match = match, Apply = apply, Until = until, Dynamic = dynamic,
            Name = c.Card?.Name ?? "", Tag = tag, Ctx = c,
        };
        foreach (var d in first)
            if (before.TryGetValue(d, out var b) && rec.Sides.Any(z => z.BattleArea.Contains(d)))
                rec.Given[d] = Diff63(d, b);
        _whole63.Add(rec);
        Diag46.Hit("whole63");
    }

    /// <summary>BT12-106: 나중에 나온 테이머도 다음 액티브 페이즈에 액티브가 되지 않는다</summary>
    internal void WholeTamers63(EffectContext c, PlayerZones side, int until, string tag)
    {
        var rec = _whole63.LastOrDefault(r => ReferenceEquals(r.Ctx, c) && r.Tag == tag);
        if (rec == null) return;
        rec.TamerSkipUntil = until;
        foreach (var t in side.Tamers) rec.TamersSeen.Add(t);
    }

    /// <summary>이 효과(c)가 남긴 기록을 지운다 (EX3-036 "대신" — 앞 문장의 ≪시큐리티 어택 -1≫ 기록을 -2로 바꿀 때)</summary>
    internal void WholeDrop63(EffectContext c, string tag) => _whole63.RemoveAll(r => ReferenceEquals(r.Ctx, c) && r.Tag == tag);

    internal bool AnyWhole63 => _whole63.Count > 0;

    /// <summary>새 게임: 앞 게임의 기간 처리 기록을 지운다 (v0.62.0 이 턴 동안 얻는 것도)</summary>
    private void ResetWhole63()
    {
        _whole63.Clear();
        _zeroPending63.Clear();
        _sideAuras62.Clear();
    }

    /// <summary>
    /// RecalculateFieldBuffs 끝: 나중에 늘어난 대상 · 조건을 잃은 대상을 맞춘다. 이 안에서는 소멸시키지 않는다 (DP 0은 FlushZero63)
    /// </summary>
    private bool Sweep63()
    {
        if (_whole63.Count == 0 || _sweeping63) return false;
        _whole63.RemoveAll(r => r.Until < State.TurnSequence);
        if (_whole63.Count == 0) return false;
        bool changed = false;
        _sweeping63 = true;
        var outerOwner = CurrentEffectOwner;
        var outerCard = CurrentEffectCard;
        bool outerDel = _deletingByOpponentEffect;
        try
        {
            foreach (var rec in _whole63.ToList())
            {
                if (State.IsGameOver) break;
                CurrentEffectOwner = rec.EffectOwner;
                CurrentEffectCard = rec.EffectCard;
                _deletingByOpponentEffect = false;
                // 필드를 떠난 디지몬 (새 카드가 된다 — 종합 룰 3-1-3-1)
                foreach (var gone in rec.Given.Keys.Where(d => !rec.Sides.Any(z => z.BattleArea.Contains(d))).ToList())
                {
                    rec.Given.Remove(gone);
                    rec.Flips.Remove(gone);
                }
                foreach (var z in rec.Sides)
                {
                    foreach (var d in z.BattleArea.ToList())
                    {
                        if (d.TamerProxyOf != null) continue;
                        bool has = rec.Given.TryGetValue(d, out var delta);
                        if (rec.Flips.GetValueOrDefault(d) >= 4) continue;   // 조건이 계속 바뀌는 것 (자기 처리로 조건이 바뀜) — 멈춘다
                        bool fits;
                        try { fits = rec.Match(d); }
                        catch { fits = false; }
                        if (!has && fits && !IsUnaffectedBy(rec.EffectOwner, d))
                        {
                            var b = Capture63(d);
                            rec.Apply(d);
                            rec.Given[d] = Diff63(d, b);
                            rec.Flips[d] = rec.Flips.GetValueOrDefault(d) + 1;
                            changed = true;
                            Diag46.Hit("whole63Late");
                            Log($"[효과] {rec.Name} (전체 효과): {SideName(z)} [{d.TopCard.Name}]도 대상이 되었습니다.");
                        }
                        else if (has && !fits && rec.Dynamic)
                        {
                            Undo63(d, delta!);
                            rec.Given.Remove(d);
                            rec.Flips[d] = rec.Flips.GetValueOrDefault(d) + 1;
                            changed = true;
                            Diag46.Hit("whole63Undo");
                            Log($"[효과] {rec.Name} (전체 효과): {SideName(z)} [{d.TopCard.Name}]은(는) 조건을 만족하지 않게 되어 대상이 아니게 되었습니다.");
                        }
                    }
                    if (rec.TamerSkipUntil > 0)
                        foreach (var t in z.Tamers.ToList())
                            if (rec.TamersSeen.Add(t))
                            {
                                z.TamerCannotUnsuspendUntil[t] = Math.Max(z.TamerCannotUnsuspendUntil.GetValueOrDefault(t), rec.TamerSkipUntil);
                                Diag46.Hit("whole63Tamer");
                            }
                }
            }
        }
        finally
        {
            CurrentEffectOwner = outerOwner;
            CurrentEffectCard = outerCard;
            _deletingByOpponentEffect = outerDel;
            _sweeping63 = false;
        }
        return changed;
    }

    /// <summary>나중에 받은 DP 마이너스로 DP가 0 이하 — 효과 처리가 끝난 때 소멸시킨다 (룰 처리)</summary>
    private bool DeferZero63(CardInstance target)
    {
        if (!_sweeping63) return false;
        if (!_zeroPending63.Contains(target)) _zeroPending63.Add(target);
        return true;
    }

    /// <summary>DP 0 이하가 된 디지몬을 소멸시킨다 (효과 처리가 끝난 때 · 등장 · 진화 · 이동 뒤)</summary>
    private void FlushZero63()
    {
        if (_zeroPending63.Count == 0 || _sweeping63) return;
        var list = _zeroPending63.ToList();
        _zeroPending63.Clear();
        foreach (var d in list)
        {
            if (State.IsGameOver) return;
            var zones = State.Player.BattleArea.Contains(d) ? State.Player : State.DummyOpponent.BattleArea.Contains(d) ? State.DummyOpponent : null;
            if (zones == null || d.GetCurrentDP() > 0) continue;
            if (!DeleteFrom(zones, d, DeletionCause.ZeroDp)) continue;
            Diag46.Hit("whole63Zero");
            Log($"[효과] {SideName(zones)} [{d.TopCard.Name}]의 DP가 0이 되어 소멸했습니다.");
            var other = EnemyOf(zones);
            foreach (var e in other.BattleArea.ToList())
                RunEffects(EffectTrigger.OpponentDigimonDeletedByZeroDp, other, e, null);
        }
    }

    // =====================================================================
    // 스크립트를 먼저 만들어 보는 빠른 확인 (v0.61.0 효과 문장을 주는 지속 효과 · v0.62.0 "액티브가 되려 할 때")
    // =====================================================================

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Built63 = new(StringComparer.Ordinal);

    /// <summary>카드의 스크립트를 한 번 만들어 둔다 (만들 때 등록되는 표 — GrantAuraCards61 · WouldUnsuspendCards62)</summary>
    internal static void EnsureScript63(CardData c)
    {
        if (c.IsTamerProxy || Built63.ContainsKey(c.CardId)) return;
        CardScripts.For(c);
        Built63[c.CardId] = true;
    }

    // =====================================================================
    // 어택 대상 변경 — 플레이어로 · 지금의 어택 대상 (BT15-078 · EX5-054 · ST18-14 · BT4-075)
    // =====================================================================

    /// <summary>플레이어의 어택이 처음 고른 상대의 디지몬 (시큐리티 어택이면 null)</summary>
    private CardInstance? _playerAttackTarget63;

    /// <summary>플레이어의 디지몬 어택을 효과로 상대 플레이어에게 바꿨다 (【어택 시】 · "어택했을 때" 효과 뒤에 시큐리티 어택으로)</summary>
    private bool _playerAttackToPlayer63;

    /// <summary>지금 어택의 대상 (플레이어면 null) — 효과로 바뀐 것까지</summary>
    internal CardInstance? CurrentAttackTarget63
    {
        get
        {
            if (CurrentAttacker is not { } a) return null;
            if (_aiTurn != null && State.DummyOpponent.BattleArea.Contains(a)) return _aiTurn.AttackTarget;
            if (_playerAttackToPlayer63) return null;
            if (_playerAttackRedirect is { } r && State.DummyOpponent.BattleArea.Contains(r)) return r;
            return _playerAttackTarget63 is { } t && State.DummyOpponent.BattleArea.Contains(t) ? t : null;
        }
    }

    /// <summary>
    /// "어택 대상을 플레이어로 바꾼다" — defender = 어택당하는 쪽. 디지몬을 어택하는 중일 때만 (이미 플레이어면 false).
    /// 효과로 어택 대상을 바꾸는 것은 어택 선언이 아니다 (공식 Q&amp;A ST18-14 Q1 — 어택당하지 않는 디지몬 · 액티브 디지몬으로도 바꿀 수 있다)
    /// </summary>
    internal bool RedirectAttackToPlayer63(PlayerZones defender)
    {
        if (CurrentAttacker is not { } atk || IsAttackTargetLocked(atk)) return false;
        if (ReferenceEquals(defender, State.Player) && _aiTurn != null)
        {
            if (_aiTurn.AttackTarget == null) return false;
            _aiTurn.AttackTarget = null;
        }
        else if (ReferenceEquals(defender, State.DummyOpponent) && _aiTurn == null)
        {
            if (CurrentAttackTarget63 == null) return false;
            _playerAttackRedirect = null;
            _playerAttackToPlayer63 = true;
        }
        else return false;
        Diag46.Hit("attackToPlayer63");
        NotifyAttackTargetSwitched(atk);
        return true;
    }

    /// <summary>플레이어 어택의 처음 대상을 적어 둔다 (어택 선언 · 【어택 시】 전)</summary>
    private void BeginPlayerAttack63(CardInstance? target)
    {
        _playerAttackTarget63 = target;
        _playerAttackToPlayer63 = false;
    }

    /// <summary>【어택 시】 뒤: 효과로 플레이어로 바꿨는지 (읽으면 지운다)</summary>
    private bool ConsumeToPlayer63()
    {
        bool r = _playerAttackToPlayer63;
        _playerAttackToPlayer63 = false;
        _playerAttackTarget63 = null;
        return r;
    }

    /// <summary>≪진격≫으로 어택하는 디지몬인지 (BT5-017 진화원 "이 디지몬이 ≪진격≫으로 어택할 때, 액티브 상태인 상대의 디지몬에게도 어택할 수 있다")</summary>
    internal bool IsBlitzAttacker63(CardInstance d) =>
        ReferenceEquals(State.BlitzAttacker, d) || (_aiTurn != null && ReferenceEquals(_aiTurn.BlitzAttacker, d));

    /// <summary>v0.63.0 AI: inst가 "액티브가 되려 할 때 패 1장을 파기하거나 액티브가 되지 않는다"를 가졌는지 (BT7-055가 주는 효과)</summary>
    internal bool HasWouldUnsuspend63(CardInstance inst) => HasWouldUnsuspend62(this, inst);
}
