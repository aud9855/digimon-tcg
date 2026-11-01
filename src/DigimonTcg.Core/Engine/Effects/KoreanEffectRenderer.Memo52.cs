using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.52.0 한글 생성 속도 — 원래 규칙이 읽지 못한 절은 25차까지 쌓인 단계(Batch38 ~ Batch51)가 문을 연 상태마다 같은 절 · 문장을 다시 읽어
/// 읽지 못하는 절 하나에 몇 초씩 걸렸다. 결과는 (입력, 모든 [ThreadStatic] 상태)만으로 정해지므로 그 둘이 같은 호출은 한 번만 계산하고
/// 결과와 호출이 끝난 뒤의 상태를 그대로 돌려준다 — 결과 · 부수 상태가 캐시 없이 계산한 것과 똑같다.
/// </summary>
public static partial class KoreanEffectRenderer
{
    /// <summary>시험용: false면 캐시를 쓰지 않는다 (캐시 있음 · 없음 결과 비교)</summary>
    internal static bool MemoEnabled52 = true;

    private sealed class StateSlot52
    {
        public required Func<object?> Get;
        public required Action<object?> Set;
    }

    private static readonly StateSlot52[]? Slots52 = BuildSlots52();

    /// <summary>이 어셈블리의 모든 [ThreadStatic] 정적 필드 — 값 형식 · 문자열만 (다른 형식이 있으면 캐시를 쓰지 않는다)</summary>
    private static StateSlot52[]? BuildSlots52()
    {
        try
        {
            var slots = new List<StateSlot52>();
            foreach (var t in typeof(KoreanEffectRenderer).Assembly.GetTypes())
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.GetCustomAttribute<ThreadStaticAttribute>() == null) continue;
                    if (f.Name.StartsWith("_memo52", StringComparison.Ordinal)) continue;
                    var ft = f.FieldType;
                    if (!(ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || Nullable.GetUnderlyingType(ft) is { } u && (u.IsPrimitive || u.IsEnum)) || f.IsInitOnly || f.IsLiteral)
                        return null;
                    var get = Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Field(null, f), typeof(object))).Compile();
                    var p = Expression.Parameter(typeof(object));
                    var set = Expression.Lambda<Action<object?>>(Expression.Assign(Expression.Field(null, f), Expression.Convert(p, ft)), p).Compile();
                    slots.Add(new StateSlot52 { Get = get, Set = set });
                }
            return slots.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly struct MemoKey52 : IEquatable<MemoKey52>
    {
        private readonly int _fn;
        private readonly string _text;
        private readonly int _flag;
        private readonly object?[] _state;
        private readonly int _hash;

        public MemoKey52(int fn, string text, int flag, object?[] state)
        {
            _fn = fn; _text = text; _flag = flag; _state = state;
            var h = new HashCode();
            h.Add(fn); h.Add(text, StringComparer.Ordinal); h.Add(flag);
            foreach (var v in state) h.Add(v);
            _hash = h.ToHashCode();
        }

        public bool Equals(MemoKey52 o)
        {
            if (_hash != o._hash || _fn != o._fn || _flag != o._flag || !string.Equals(_text, o._text, StringComparison.Ordinal)) return false;
            for (int i = 0; i < _state.Length; i++)
                if (!Equals(_state[i], o._state[i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is MemoKey52 k && Equals(k);
        public override int GetHashCode() => _hash;
    }

    [ThreadStatic] private static Dictionary<MemoKey52, (object? Result, object?[] After)>? _memo52;

    private const int MemoLimit52 = 200_000;

    private static object?[] Snapshot52(StateSlot52[] slots)
    {
        var a = new object?[slots.Length];
        for (int i = 0; i < slots.Length; i++) a[i] = slots[i].Get();
        return a;
    }
    /// <summary>fn = 함수 번호 (0 = RenderClause, 1 = Sentence, 2 = Action, 3 = CostKo, 4 = CondKo, 5 = EventKo, 6 = TargetKo, 7 = PickKo, 8 = RenderClauseCore) · flag = 그 함수의 둘째 인자</summary>
    private static T Memo52<T>(int fn, string text, int flag, Func<string, int, T> compute)
    {
        if (!MemoEnabled52 || Slots52 is not { } slots || text == null) return compute(text!, flag);
        var key = new MemoKey52(fn, text, flag, Snapshot52(slots));
        var memo = _memo52 ??= new Dictionary<MemoKey52, (object?, object?[])>();
        if (memo.TryGetValue(key, out var hit))
        {
            for (int i = 0; i < slots.Length; i++) slots[i].Set(hit.After[i]);
            return (T)hit.Result!;
        }
        T result = compute(text, flag);   // 예외는 캐시하지 않는다
        if (memo.Count >= MemoLimit52) memo.Clear();
        memo[key] = (result, Snapshot52(slots));
        return result;
    }
}
