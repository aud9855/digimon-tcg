using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 블록 가능 여부처럼 엔진과 ActionValidator(공격 미리보기)가 함께 쓰는 판정
/// </summary>
public static partial class EffectRules
{
    /// <summary>"~ 턴 종료 시까지" 기간 효과가 아직 유효한지 (0 = 효과 없음)</summary>
    public static bool IsActive(int until, GameState state) => until > 0 && until >= state.TurnSequence;

    public static bool CanBlock(GameState state, CardInstance blocker) =>
        blocker.HasBlocker && !CardScripts.HasKeyword(blocker, Keyword.CannotBlock) && !blocker.IsSuspended && !IsActive(blocker.CannotBlockUntil, state) && !IsSuspendLocked(state, blocker);

    /// <summary>attacker가 blocker에게 블록당하지 않는지</summary>
    public static bool IsUnblockableBy(GameState state, CardInstance attacker, CardInstance blocker) =>
        IsActive(attacker.UnblockableUntil, state)
        || CardScripts.HasKeyword(attacker, Keyword.Unblockable)
        || (CardScripts.HasKeyword(attacker, Keyword.UnblockableBySourceless) && blocker.EvolutionSources.Count == 0);

    /// <summary>attacker의 어택을 막을 블로커 (가장 DP가 높은 블로커가 막는다)</summary>
    public static CardInstance? FindBlocker(GameState state, IEnumerable<CardInstance> defenders, CardInstance attacker) =>
        BlockCandidates(state, defenders, attacker).FirstOrDefault();

    /// <summary>attacker의 어택을 블록할 수 있는 블로커 전부 (DP 높은 순) — 이 중 어느 것으로 막을지(막지 않을지)는 방어 측이 고른다</summary>
    public static List<CardInstance> BlockCandidates(GameState state, IEnumerable<CardInstance> defenders, CardInstance attacker) =>
        defenders
            .Where(d => CanBlockAgainst(state, d, attacker) && !IsUnblockableBy(state, attacker, d))
            .OrderByDescending(d => d.GetCurrentDP())
            .ToList();

    /// <summary>≪충돌≫: 이 디지몬이 어택하는 동안 상대 디지몬 전부가 ≪블로커≫를 얻고, 상대는 가능하면 블록해야 한다</summary>
    public static bool ForcesBlock(CardInstance attacker) => CardScripts.HasKeyword(attacker, Keyword.Collision);

    /// <summary>blocker가 attacker의 어택을 블록할 수 있는지 (≪충돌≫이면 ≪블로커≫가 없어도)</summary>
    public static bool CanBlockAgainst(GameState state, CardInstance blocker, CardInstance attacker) =>
        (blocker.HasBlocker || ForcesBlock(attacker)) && !CardScripts.HasKeyword(blocker, Keyword.CannotBlock) && !blocker.IsSuspended && !IsActive(blocker.CannotBlockUntil, state) && !IsSuspendLocked(state, blocker);

    /// <summary>
    /// 상대(AI · 연습 더미)가 내 어택을 블록할 블로커. null = 블록하지 않음.
    /// 연습 모드의 더미 수비대는 블로커 돌파 훈련용이므로 항상 막는다. 실전 AI는 공식 룰대로 막을지 고른다:
    /// 이기는 블로커가 있으면 (그중 DP가 가장 낮은 것으로) 막고, 다이렉트 어택은 희생해서라도 막고,
    /// 시큐리티가 1장 남았을 때는 상대 쪽이 더 높은 레벨일 때만 동귀어진으로 교환한다. 그 밖에는 블로커를 아낀다.
    /// </summary>
    public static CardInstance? ChooseOpponentBlocker(GameState state, CardInstance attacker, CardInstance? attackTarget)
    {
        var candidates = BlockCandidates(state, state.DummyOpponent.BattleArea.Where(d => !ReferenceEquals(d, attackTarget)), attacker);
        if (candidates.Count == 0)
            return null;
        if (state.CurrentStage != PracticeStage.Stage3_AiBattle)
            return candidates[0];

        int atkDp = attacker.GetCurrentDP();
        var winner = candidates.Where(b => b.GetCurrentDP() > atkDp).OrderBy(b => b.GetCurrentDP()).FirstOrDefault();
        if (winner != null)
            return winner;

        if (attackTarget == null)
        {
            int security = state.DummyOpponent.SecurityStack.Count;
            if (security == 0)
                return candidates.OrderBy(b => b.GetCurrentDP()).First();

            if (security <= 1)
            {
                var trade = candidates.FirstOrDefault(b => b.GetCurrentDP() == atkDp && b.TopCard.Level < attacker.TopCard.Level);
                if (trade != null)
                    return trade;
            }
        }
        // ≪충돌≫: 막을 수 있으면 반드시 막는다 (가장 DP가 낮은 것으로)
        return ForcesBlock(attacker) ? candidates.OrderBy(b => b.GetCurrentDP()).First() : null;
    }

    /// <summary>
    /// 시큐리티 체크로 오픈된 카드에 [시큐리티] 효과가 있는지 (공식 룰 13-1-8-2: 시큐리티 체크로 발동하는 효과가 있을 때만 처리하고,
    /// 없으면 13-1-8-4에 따라 그냥 트래시). 테이머의 [시큐리티] 효과는 "코스트 없이 등장".
    /// </summary>
    public static bool HasSecurityEffect(CardData card) =>
        card.EffectText.Contains("[시큐리티]") || card.EffectText.Contains("[Security]", StringComparison.OrdinalIgnoreCase)
        // API 카드는 옵션 · 테이머의 [시큐리티] 효과가 진화원 효과 칸("Security Effect [Security] ~")에 들어 있다
        || card.InheritedEffectText.Contains("[Security]", StringComparison.OrdinalIgnoreCase);

    /// <summary>디지몬 어택의 대상이 될 수 있는지 (레스트 상태 · "어택당하지 않는다" 아님)</summary>
    public static bool CanBeAttackTarget(CardInstance digimon) =>
        digimon.IsSuspended && !CardScripts.HasKeyword(digimon, Keyword.CannotBeAttacked);

    /// <summary>
    /// v0.34.0 attacker가 이 디지몬에게 어택할 수 있는지 — "can't attack your opponent's Digimon" · "can also attack your opponent's unsuspended Digimon (with no digivolution cards)"
    /// </summary>
    public static bool CanBeAttackTargetFor(CardInstance attacker, CardInstance target) =>
        !CardScripts.HasKeyword(attacker, Keyword.CannotAttackDigimon) && !CardScripts.HasKeyword(target, Keyword.CannotBeAttacked)
        && (target.IsSuspended || CardScripts.HasKeyword(attacker, Keyword.CanAttackUnsuspended)
            || (CardScripts.HasKeyword(attacker, Keyword.CanAttackUnsuspendedSourceless) && target.EvolutionSources.Count == 0));

    /// <summary>v0.34.0 attacker가 플레이어(시큐리티)에게 어택할 수 있는지 — "can't attack players"</summary>
    public static bool CanAttackPlayer(CardInstance attacker) => !CardScripts.HasKeyword(attacker, Keyword.CannotAttackPlayers);

    public static bool IsAttackLocked(GameState state, CardInstance digimon) =>
        IsActive(digimon.CannotAttackUntil, state) || IsSuspendLocked(state, digimon) || CardScripts.HasKeyword(digimon, Keyword.CannotAttack);   // 어택하면 레스트가 되므로 레스트 불가 = 어택 불가

    /// <summary>"This Digimon can't digivolve" (지속) · "~ can't digivolve until their turn ends" (기간) — 패 · 효과 · DNA 진화 모두 (v0.29.0)</summary>
    public static bool IsDigivolveLocked(GameState? state, CardInstance digimon) =>
        CardScripts.HasKeyword(digimon, Keyword.CannotDigivolve) || state != null && IsActive(digimon.CannotDigivolveUntil, state);

    /// <summary>"can't suspend" — 레스트가 될 수 없으면 블록(레스트해서 막기)도 할 수 없다</summary>
    public static bool IsSuspendLocked(GameState state, CardInstance digimon) => IsActive(digimon.CannotSuspendUntil, state) || CardScripts.HasKeyword(digimon, Keyword.CannotSuspend);   // v0.37.0 지속 효과 (EX8-026)

    /// <summary>"can't unsuspend" — 액티브 페이즈 · 효과로 액티브가 되지 않는다</summary>
    public static bool IsUnsuspendLocked(GameState state, CardInstance digimon) => IsActive(digimon.CannotUnsuspendUntil, state);
}

/// <summary>
/// 카드 효과 처리 — 효과 내용은 카드 스크립트(Engine/Effects)에 있고, 여기서는 시점마다 해당 효과를 찾아 처리한다.
/// 스크립트가 쓰는 공용 동작(드로우 · 소멸 · 리커버리 · 메모리 …)도 여기에 있다.
/// </summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // 효과 발동
    // =====================================================================

    /// <summary>
    /// trigger 시점의 효과를 처리한다: mainCard의 메인 효과 → self의 진화원 효과(아래에서 위 순서, 같은 카드는 1번).
    /// 진화원 효과는 처리 직전에 그 진화원이 아직 남아 있을 때만 발휘한다 (앞의 효과로 진화원이 빠질 수 있음).
    /// sources를 넘기면 그 목록을 진화원으로 쓴다 ([소멸 시]: 이미 트래시로 간 진화원).
    /// </summary>
    internal EffectContext RunEffects(
        EffectTrigger trigger,
        PlayerZones owner,
        CardInstance? self,
        CardData? mainCard,
        CardInstance? other = null,
        CardInstance? chosenTarget = null,
        bool includeInherited = true,
        IReadOnlyList<CardData>? sources = null,
        CardData? triggerCard = null,
        bool optionSide = false)
    {
        var ctx = new EffectContext(this, owner, self, other, chosenTarget) { TriggerCard = triggerCard, Trigger = trigger };

        _effectDepth++;
        var outerEffectOwner = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try
        {
            if (mainCard != null && (optionSide ? CardScripts.OptionSideFor(mainCard) : CardScripts.For(mainCard)) is { } main)
            {
                foreach (var effect in main.Effects.Where(e => e.Trigger == trigger && e.Source == EffectSource.Main).ToList())
                    Resolve(ctx, mainCard, effect);
            }

            // v0.32.0 효과로 받은 효과 (기간 안 · 효과를 받을 수 있을 때 — 공식 Q&A EX10-034)
            if (self != null && mainCard != null && !optionSide && ReferenceEquals(mainCard, self.TopCard) && self.GrantedEffects.Count > 0)
            {
                foreach (var granted in ActiveGrantedEffects(self))
                {
                    if (CardScripts.For(granted) is not { } grantedScript) continue;
                    foreach (var effect in grantedScript.Effects.Where(e => e.Trigger == trigger && e.Source == EffectSource.Main).ToList())
                        Resolve(ctx, granted, effect);
                }
            }

            // v0.31.0 "This Digimon gains all effects of ~ in this Digimon's digivolution cards" — 얻은 진화원 카드의 메인 효과 (맨 위 카드의 시점으로)
            if (self != null && mainCard != null && !optionSide && ReferenceEquals(mainCard, self.TopCard) && trigger != EffectTrigger.ActivateMain)
            {
                foreach (var gained in CardScripts.GainedSources(self, sources).ToList())
                {
                    if (CardScripts.For(gained) is not { } gainedScript) continue;
                    foreach (var effect in gainedScript.Effects.Where(e => e.Trigger == trigger && e.Source == EffectSource.Main).ToList())
                    {
                        if (sources == null && !self.HasSource(gained.CardId)) break;
                        Resolve(ctx, gained, effect);
                    }
                }
            }

            if (includeInherited && self != null)
            {
                var sourceCards = (sources ?? self.GetEvolutionStackBottomUp()).Where(s => !self.IsFaceDown(s)).GroupBy(s => s.CardId).Select(g => g.First()).ToList();
                foreach (var source in sourceCards)
                {
                    if (CardScripts.For(source) is not { } script)
                        continue;
                    foreach (var effect in script.Effects.Where(e => e.Trigger == trigger && e.Source == EffectSource.Inherited).ToList())
                    {
                        if (sources == null && !self.HasSource(source.CardId))
                            break;
                        Resolve(ctx, source, effect);
                    }
                }
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

    // 디지몬이 아닌 카드의 [턴에 1회] 사용 기록: (카드, 효과) → (턴 번호, 횟수)
    private readonly Dictionary<(CardData Card, string Key), (int Turn, int Count)> _cardOncePerTurn = new();

    /// <summary>테이머 · 옵션 · 트래시 카드의 [턴에 1회] (v0.31.0): 이번 턴에 limit번 썼으면 false, 아니면 사용으로 기록하고 true</summary>
    internal bool TryUseCardOncePerTurn(CardData card, string key, int limit)
    {
        var k = (card, key);
        if (_cardOncePerTurn.TryGetValue(k, out var used) && used.Turn == State.TurnSequence)
        {
            if (used.Count >= limit) return false;
            _cardOncePerTurn[k] = (used.Turn, used.Count + 1);
            return true;
        }
        _cardOncePerTurn[k] = (State.TurnSequence, 1);
        return true;
    }

    /// <summary>지금 처리 중인 효과의 주인 (효과 밖이면 null) — "자신의 효과 이외로" 같은 판단</summary>
    internal PlayerZones? CurrentEffectOwner { get; private set; }

    /// <summary>지금 배틀 에어리어를 떠나려는 디지몬의 사정 ("would leave the battle area / be deleted" 효과의 조건이 본다)</summary>
    internal sealed record LeaveInfo(CardInstance Digimon, bool Deletion, bool InBattle, bool ByOwnEffect, bool CanPrevent = true, LeaveDestination Destination = LeaveDestination.Other)
    {
        /// <summary>v0.33.0 떠나려는 것이 테이머 (Digimon = 그 테이머 카드만 든 가짜 디지몬)</summary>
        public bool IsTamer { get; init; }
    }

    /// <summary>v0.32.0 떠나는 곳 ("would be deleted or returned to your hand or deck" 같은 조건이 본다)</summary>
    internal enum LeaveDestination { Other, Deleted, Hand, Deck, Security }
    internal LeaveInfo? CurrentLeave { get; private set; }

    /// <summary>
    /// "When ~ would leave the battle area / would be deleted" 효과를 발휘하고, 그중 하나라도 떠나는 것을 막았으면 true.
    /// deletion = 소멸로 떠나는지, inBattle = 배틀로 소멸하는지. 자신의 효과로 떠나는지는 지금 처리 중인 효과의 주인으로 판단한다.
    /// </summary>
    internal bool TryPreventLeave(PlayerZones zones, CardInstance inst, bool deletion, bool inBattle, bool canPrevent = true, LeaveDestination destination = LeaveDestination.Other)
    {
        if (!zones.BattleArea.Contains(inst))
            return false;
        var outer = CurrentLeave;
        CurrentLeave = new LeaveInfo(inst, deletion, inBattle, !inBattle && ReferenceEquals(CurrentEffectOwner, zones), canPrevent,
            deletion ? LeaveDestination.Deleted : destination);
        bool prevented = false;
        try
        {
            prevented |= RunEffects(EffectTrigger.ThisDigimonWouldLeave, zones, inst, inst.TopCard).PreventLeave;
            if (deletion && !prevented)
                prevented |= RunEffects(EffectTrigger.ThisDigimonWouldBeDeleted, zones, inst, inst.TopCard).PreventLeave;
            foreach (var d in zones.BattleArea.Where(d => !ReferenceEquals(d, inst)).ToList())
            {
                if (prevented) break;
                prevented |= RunEffects(EffectTrigger.AllyDigimonWouldLeave, zones, d, d.TopCard, other: inst).PreventLeave;
                if (deletion && !prevented)
                    prevented |= RunEffects(EffectTrigger.AllyDigimonWouldBeDeleted, zones, d, d.TopCard, other: inst).PreventLeave;
            }
            foreach (var card in zones.Tamers.Concat(zones.OptionsInPlay.Select(p => p.Card)).Distinct().ToList())
            {
                if (prevented) break;
                prevented |= RunEffects(EffectTrigger.AllyDigimonWouldLeave, zones, null, card, other: inst, includeInherited: false).PreventLeave;
                if (deletion && !prevented)
                    prevented |= RunEffects(EffectTrigger.AllyDigimonWouldBeDeleted, zones, null, card, other: inst, includeInherited: false).PreventLeave;
            }
        }
        finally
        {
            CurrentLeave = outer;
        }
        if (!canPrevent)
            return false;   // DP 0 소멸 · 코스트 소멸: "떠나려 할 때" 효과는 발휘하지만 막을 수는 없다
        if (prevented)
            EffectLog(zones, $"[{inst.TopCard.Name}]은(는) 효과로 배틀 에어리어를 떠나지 않습니다.");
        return prevented && zones.BattleArea.Contains(inst);
    }

    // 효과 처리 중에 일어난 [소멸 시] · "소멸했을 때"는 그 효과를 모두 처리한 뒤에 발휘한다 (공식 Q&A ST6-15 Q1)
    private int _effectDepth;
    private readonly Queue<Action> _pendingTriggers = new();
    private bool _flushingTriggers;

    /// <summary>효과 처리 중이면 그 효과가 끝날 때까지 미루고, 아니면 바로 처리</summary>
    private void RunOrDefer(Action trigger)
    {
        if (_effectDepth > 0) _pendingTriggers.Enqueue(trigger);
        else trigger();
    }

    private void FlushPendingTriggers()
    {
        if (_flushingTriggers)
            return;
        _flushingTriggers = true;
        try
        {
            while (_pendingTriggers.Count > 0)
                _pendingTriggers.Dequeue()();
        }
        finally
        {
            _flushingTriggers = false;
        }
    }

    private static void Resolve(EffectContext ctx, CardData card, CardEffect effect)
    {
        var outerEffectCard = ctx.Engine.CurrentEffectCard;
        try { ResolveCore(ctx, card, effect); }
        finally { ctx.Engine.CurrentEffectCard = outerEffectCard; }
    }

    private static void ResolveCore(EffectContext ctx, CardData card, CardEffect effect)
    {
        ctx.Card = card;
        ctx.Engine.CurrentEffectCard = card;   // v0.34.0 "상대의 디지몬 · 옵션 카드의 효과를 받지 않는다"
        // "When one of your Digimon attacks, ~ give that Digimon +1000 DP" — 사건의 주인공 디지몬이 "that Digimon"
        ctx.LastTargets = ctx.Other != null && effect.Trigger is EffectTrigger.AllyDigimonAttacks or EffectTrigger.AllyDigimonPlayed
            or EffectTrigger.AllyDigimonDigivolved or EffectTrigger.AllyDigimonSuspended or EffectTrigger.OpponentDigimonAttacks
            or EffectTrigger.OpponentDigimonSuspended or EffectTrigger.OpponentDigimonPlayed
            or EffectTrigger.AllyDigimonUnsuspended or EffectTrigger.OpponentDigimonUnsuspended or EffectTrigger.AllyDeletedOpponentInBattle
            or EffectTrigger.AllySourcesAdded   // v0.35.0 "When face-down cards are placed in any of your Digimon's digivolution cards, any of those Digimon may digivolve ~"
            ? new List<CardInstance> { ctx.Other } : new List<CardInstance>();
        if (effect.Condition != null && !effect.Condition(ctx))
            return;
        // [턴에 1회] 기록: 같은 카드의 메인 효과와 진화원 효과는 다른 효과다 (v0.29.0 — 진화원 [메인]과 맨 위가 된 그 카드의 메인 효과, BT22-043)
        string onceKey = effect.Source == EffectSource.Inherited ? card.CardId + "#I" : card.CardId;
        if (effect.OncePerTurn && ctx.Self != null && !ctx.Self.UsedOncePerTurn.Add(onceKey))
            return;
        if (effect.TwicePerTurn && ctx.Self != null && !ctx.Self.UsedOncePerTurn.Add(onceKey) && !ctx.Self.UsedOncePerTurn.Add(onceKey + "#2"))
            return;
        // v0.31.0: 디지몬이 아닌 카드(테이머 · 배틀 에어리어의 옵션 · 트래시의 카드)의 [턴에 1회] · [턴에 2회] — 그 카드마다 이번 턴 사용 횟수
        if ((effect.OncePerTurn || effect.TwicePerTurn) && ctx.Self == null && !ctx.Engine.TryUseCardOncePerTurn(card, onceKey, effect.TwicePerTurn ? 2 : 1))
            return;
        int logsBefore = ctx.State.ActionLogs.Count;
        string messageBefore = ctx.Message;
        ctx.AnyEffectRan = true;
        bool watching = ctx.Engine.BeginSourceWatch();
        // "the Digimon this effect played" — 이 효과가 직접 등장시킨 디지몬 (안쪽 효과는 자기 목록)
        var outerCollector = ctx.Engine.PlayCollector;
        ctx.PlayedThisEffect = new List<CardInstance>();
        ctx.Engine.PlayCollector = ctx.PlayedThisEffect;
        var handBefore = ctx.Self != null ? ctx.Owner.Hand.ToArray() : null;   // "one of your Digimon's effects adds cards to your hand" (v0.30.0)
        int drawsBefore = ctx.Engine.DrawSerial.GetValueOrDefault(ctx.Owner);
        try
        {
            effect.Resolve(ctx);
            if (handBefore != null) ctx.Engine.NoteDigimonHandAdd(ctx.Owner, handBefore, drawsBefore);
        }
        finally { ctx.Engine.PlayCollector = outerCollector; if (watching) ctx.Engine.EndSourceWatch(ctx.Owner); }
        // 카드에 적힌 시점([등장 시] · [진화 시] · [어택 시] · [소멸 시])의 효과가 대상 · 조건이 없어 아무것도 안 했으면 그렇다고 남긴다.
        // 조용히 넘어가면 "효과가 안 탔다"로 보인다 (사용자 신고: 로제몬 · 쥬드몬 · 엔젤우몬 등 내장 스크립트의 return)
        if (ctx.State.ActionLogs.Count == logsBefore && ctx.Message == messageBefore && AnnouncedLabel(effect) is { } label)
            ctx.Log($"[{label}] {card.Name}: 대상 · 조건에 맞는 것이 없어 효과가 일어나지 않았습니다");
    }

    /// <summary>
    /// "효과가 일어나지 않았다"를 남길 시점 — 카드 본체의 효과만. 진화원 효과는 어택마다 여러 장이 함께 발동해
    /// 조건이 안 맞을 때마다 남기면 로그가 묻힌다 (대량 대전에서 디지타마 진화원 [어택 시]가 한 장에 수백 줄)
    /// </summary>
    private static string? AnnouncedLabel(CardEffect effect) => effect.Source != EffectSource.Main ? null : effect.Trigger switch
    {
        EffectTrigger.OnPlay => "등장 시",
        EffectTrigger.WhenDigivolving => "진화 시",
        EffectTrigger.WhenAttacking => "어택 시",
        EffectTrigger.OnDeletion => "소멸 시",
        _ => null,
    };

    // =====================================================================
    // 키워드
    // =====================================================================

    private static bool HasRetaliation(CardInstance d) => CardScripts.HasKeyword(d, Keyword.Retaliation);

    private static bool HasJamming(CardInstance d) => CardScripts.HasKeyword(d, Keyword.Jamming);

    private static bool HasPiercing(CardInstance d) => CardScripts.HasKeyword(d, Keyword.Piercing);

    private bool HasReboot(CardInstance d) =>
        CardScripts.HasKeyword(d, Keyword.Reboot) || EffectRules.IsActive(d.GrantedRebootUntil, State);

    // =====================================================================
    // 지속 효과 (테이머 · 진화원 조건부 DP / 시큐리티 어택 · 얻은 블로커)
    // =====================================================================

    /// <summary>
    /// 필드의 지속 효과 수치를 다시 계산한다 (양쪽 모두). [자신의 턴] 효과는 주인의 턴에만 적용된다.
    /// </summary>
    public void RecalculateFieldBuffs()
    {
        PruneFaceUp(State.Player);
        PruneFaceUp(State.DummyOpponent);
        SyncTamerProxies();   // v0.36.0 디지몬으로 취급하는 테이머 (GoldfishEngine.TamerAsDigimon.cs)
        ApplyContinuousEffects(State.Player, State.DummyOpponent, State.PlayerAllSecurityAttackUntil);
        ApplyContinuousEffects(State.DummyOpponent, State.Player, State.OpponentAllSecurityAttackUntil);
        if (_tamerProxies.Count > 0 || AnyTamerAura())
        {
            int before = _tamerProxies.Count;
            SyncTamerProxies();   // 지속 효과(키워드)가 막 켜지거나 꺼진 것
            if (_tamerProxies.Count != before)
            {
                ApplyContinuousEffects(State.Player, State.DummyOpponent, State.PlayerAllSecurityAttackUntil);
                ApplyContinuousEffects(State.DummyOpponent, State.Player, State.OpponentAllSecurityAttackUntil);
            }
        }
    }

    private void ApplyContinuousEffects(PlayerZones owner, PlayerZones enemy, int allSecurityAttackUntil)
    {
        bool shadowWing = EffectRules.IsActive(allSecurityAttackUntil, State);

        foreach (var inst in owner.BattleArea)
        {
            bool ownTurn = inst.IsOwnersTurn;
            var ctx = new StaticContext { Engine = this, State = State, Owner = owner, Enemy = enemy, Digimon = inst, OwnersTurn = ownTurn };

            // 효과로 얻은 키워드: 기간 효과 + 조건부 지속 효과 (먼저 계산해야 ≪블로커≫ 판정에 들어간다)
            inst.TimedKeywords.RemoveAll(t => t.Until < State.TurnSequence);
            inst.TimedDpBuffs.RemoveAll(t => t.Until < State.TurnSequence);
            inst.TimedSecurityAttackBuffs.RemoveAll(t => t.Until < State.TurnSequence);
            inst.ActiveKeywords.Clear();
            foreach (var (k, _) in inst.TimedKeywords) inst.ActiveKeywords.Add(k);
            var statics = StaticEffectsOf(inst).ToList();
            ApplyExtraColors37(inst, statics, ctx);   // v0.37.0 "This Digimon is also treated as yellow" (색 조건보다 먼저)
            ApplyEnemyKeywordAuras37(inst, owner, enemy);   // v0.37.0 "none of your opponent's Digimon can suspend"
            foreach (var effect in statics.Where(e => e.Stat == StaticStat.Keyword && e.Keyword != null))
                if (effect.Value(ctx) != 0) inst.ActiveKeywords.Add(effect.Keyword!.Value);
            // 다른 카드가 주는 키워드 ("All of your level 4 or lower Digimon gain ＜Jamming＞") — 효과를 가진 카드 기준으로 조건 판단
            foreach (var (aura, auraCtx) in DigimonAuras(owner, enemy, StaticStat.AllyKeyword).Concat(TamerAuras(owner, enemy, StaticStat.AllyKeyword)))
                if (aura.Keyword != null && (aura.Subject == null || aura.Subject(inst)) && !(aura.ExcludeSource && ReferenceEquals(inst, auraCtx.Digimon)) && aura.Value(auraCtx) != 0)
                    inst.ActiveKeywords.Add(aura.Keyword.Value);

            inst.HasGrantedBlocker = EffectRules.IsActive(inst.GrantedBlockerUntil, State) || CardScripts.HasKeyword(inst, Keyword.Blocker);

            int dp = inst.TimedDpBuffs.Sum(t => t.Dp), sec = inst.TimedSecurityAttackBuffs.Sum(t => t.Count);
            foreach (var effect in statics)
            {
                if (effect.Stat == StaticStat.Dp) dp += effect.Value(ctx);
                else if (effect.Stat == StaticStat.SecurityAttack) sec += effect.Value(ctx);
            }
            if (ownTurn && shadowWing) sec += 1;   // 섀도 윙 [시큐리티]

            // 다른 디지몬이 주는 효과 ("All of your black Digimon get +1000 DP") — 효과를 가진 디지몬 기준으로 조건 판단
            foreach (var (aura, auraCtx) in DigimonAuras(owner, enemy, StaticStat.AllyDigimonDp))
                if ((aura.Subject == null || aura.Subject(inst)) && !(aura.ExcludeSource && ReferenceEquals(inst, auraCtx.Digimon))) dp += aura.Value(auraCtx);
            foreach (var link in inst.LinkedCards)   // 〈링크 중〉 이 디지몬의 DP +N
                dp += LinkDp(link);
            foreach (var (aura, auraCtx) in DigimonAuras(owner, enemy, StaticStat.AllySecurityAttack).Concat(TamerAuras(owner, enemy, StaticStat.AllySecurityAttack)))
                if ((aura.Subject == null || aura.Subject(inst)) && !(aura.ExcludeSource && ReferenceEquals(inst, auraCtx.Digimon))) sec += aura.Value(auraCtx);
            // 상대 카드가 주는 효과 ("All of your opponent's Digimon with no digivolution cards gain ＜Security A. -1＞")
            foreach (var (aura, auraCtx) in DigimonAuras(enemy, owner, StaticStat.EnemySecurityAttack).Concat(TamerAuras(enemy, owner, StaticStat.EnemySecurityAttack)))
                if ((aura.Subject == null || aura.Subject(inst)) && !(aura.ExcludeSource && ReferenceEquals(inst, auraCtx.Digimon))) sec += aura.Value(auraCtx);

            inst.EffectDpBonus = dp;
            inst.EffectSecurityAttackBonus = sec;
            inst.TamerDpBonus = TamerStatics(owner, StaticStat.AllyDigimonDp).Where(e => e.Subject == null || e.Subject(inst)).Sum(e => e.Value(ctx));
            inst.TamerSecurityAttackBonus = 0;
        }

        enemy.StaticPlayDpLock = StaticPlayDpLock37(owner, enemy);   // v0.37.0 "they can't play Digimon with 6000 DP or less" (BT14-017)

        if (owner.BreedingArea != null)
        {
            owner.BreedingArea.EffectDpBonus = 0;
            owner.BreedingArea.EffectSecurityAttackBonus = 0;
            owner.BreedingArea.TamerDpBonus = 0;
            owner.BreedingArea.TamerSecurityAttackBonus = 0;
            owner.BreedingArea.HasGrantedBlocker = false;
        }
    }

    /// <summary>디지몬 자신에게 적용되는 지속 효과: 맨 위 카드의 메인 효과 + 진화원 효과(같은 카드는 1번)</summary>
    private static IEnumerable<StaticEffect> StaticEffectsOf(CardInstance inst)
    {
        if (CardScripts.For(inst.TopCard) is { } top)
        {
            foreach (var e in top.Statics.Where(e => e.Source == EffectSource.Main))
                yield return e;
        }
        foreach (var gained in CardScripts.GainedSources(inst))   // v0.31.0 진화원 카드의 효과 전부를 얻는다 (지속 효과)
            if (CardScripts.For(gained) is { } gs)
                foreach (var e in gs.Statics.Where(e => e.Source == EffectSource.Main))
                    yield return e;
        foreach (var source in inst.FaceUpSources.GroupBy(s => s.CardId).Select(g => g.First()))
        {
            if (CardScripts.For(source) is not { } script) continue;
            foreach (var e in script.Statics.Where(e => e.Source == EffectSource.Inherited))
                yield return e;
        }
    }

    /// <summary>배틀하고 있는 동안만 더하는 DP (opponentHasSources = 배틀 상대가 진화원을 가졌는지, 시큐리티 디지몬이면 false)</summary>
    private int BattleDpBonus(CardInstance digimon, bool opponentHasSources)
    {
        var owner = OwnerOf(digimon);
        var ctx = new StaticContext
        {
            Engine = this, State = State, Owner = owner, Enemy = EnemyOf(owner), Digimon = digimon,
            OwnersTurn = digimon.IsOwnersTurn, BattleOpponentHasSources = opponentHasSources,
        };
        return StaticEffectsOf(digimon).Where(e => e.Stat == StaticStat.DpInBattle).Sum(e => e.Value(ctx));
    }

    /// <summary>필드의 테이머 효과 (같은 테이머가 여러 장이면 장마다)</summary>
    private static IEnumerable<StaticEffect> TamerStatics(PlayerZones owner, StaticStat stat) =>
        owner.Tamers.SelectMany(t => CardScripts.For(t)?.Statics.Where(e => e.Stat == stat && e.Source == EffectSource.Main) ?? Enumerable.Empty<StaticEffect>());

    private bool _playerTurnActive = true;

    /// <summary>누구의 턴인지 표시한다 ([자신의 턴] 진화원 DP는 주인의 턴에만 적용)</summary>
    private void SetTurnOwner(bool playerTurn)
    {
        _playerTurnActive = playerTurn;
        foreach (var d in AllInstances(State.Player)) d.IsOwnersTurn = playerTurn;
        foreach (var d in AllInstances(State.DummyOpponent)) d.IsOwnersTurn = !playerTurn;
        RecalculateFieldBuffs();
    }

    /// <summary>"이 턴 동안" 효과와 [턴에 1회] 사용 기록을 지운다 (각 턴 시작 시)</summary>
    private static void ResetTurnScopedState(CardInstance d)
    {
        d.TemporaryDpBonus = 0;
        d.TemporarySecurityAttackBonus = 0;
        d.HasUsedOncePerTurnEffect = false;
        d.UsedOncePerTurn.Clear();
        d.ActiveSkillBuffLabels.Clear();
    }

    private void PruneExpiredBuffs()
    {
        State.PlayerSecurityDpBuffs.RemoveAll(b => b.Until < State.TurnSequence);
        State.OpponentSecurityDpBuffs.RemoveAll(b => b.Until < State.TurnSequence);
    }

    private static IEnumerable<CardInstance> AllInstances(PlayerZones zones) =>
        zones.BreedingArea != null ? zones.BattleArea.Append(zones.BreedingArea) : zones.BattleArea;

    /// <summary>시큐리티 디지몬 DP 보정 (스타라이트 익스플로전 · 천국의 문 등 기간 효과 + 리키 같은 테이머 지속 효과)</summary>
    private int SecurityDigimonDpBonus(PlayerZones owner, bool ownersTurn)
    {
        int bonus = SecurityDpBuffsOf(owner).Where(b => b.Until >= State.TurnSequence).Sum(b => b.Dp);
        var ctx = new StaticContext { Engine = this, State = State, Owner = owner, Enemy = EnemyOf(owner), OwnersTurn = ownersTurn };
        bonus += TamerStatics(owner, StaticStat.SecurityDigimonDp).Sum(e => e.Value(ctx));
        bonus += DigimonAuras(owner, EnemyOf(owner), StaticStat.SecurityDigimonDp).Sum(a => a.Effect.Value(a.Context));   // "all of your Security Digimon get +1000 DP" (디지몬 효과)

        // 상대 쪽 효과: "All of your opponent's Security Digimon get -1000 DP"
        var enemy = EnemyOf(owner);
        var enemyCtx = new StaticContext { Engine = this, State = State, Owner = enemy, Enemy = owner, OwnersTurn = !ownersTurn };
        bonus += TamerStatics(enemy, StaticStat.EnemySecurityDigimonDp).Sum(e => e.Value(enemyCtx));
        bonus += DigimonAuras(enemy, owner, StaticStat.EnemySecurityDigimonDp).Sum(a => a.Effect.Value(a.Context));
        return bonus;
    }

    /// <summary>side의 테이머가 가진 stat 지속 효과 (장마다)와 계산 정보</summary>
    private IEnumerable<(StaticEffect Effect, StaticContext Context)> TamerAuras(PlayerZones side, PlayerZones enemy, StaticStat stat)
    {
        bool ownersTurn = IsTurnOf(side);
        var ctx = new StaticContext { Engine = this, State = State, Owner = side, Enemy = enemy, OwnersTurn = ownersTurn };
        foreach (var e in TamerStatics(side, stat))
            yield return (e, ctx);
    }

    /// <summary>side의 배틀 에어리어 디지몬(맨 위 카드 · 진화원)이 가진 stat 지속 효과와, 그 디지몬 기준 계산 정보</summary>
    private IEnumerable<(StaticEffect Effect, StaticContext Context)> DigimonAuras(PlayerZones side, PlayerZones enemy, StaticStat stat)
    {
        foreach (var source in side.BattleArea)
        {
            var ctx = new StaticContext { Engine = this, State = State, Owner = side, Enemy = enemy, Digimon = source, OwnersTurn = source.IsOwnersTurn };
            foreach (var e in StaticEffectsOf(source).Where(e => e.Stat == stat))
                yield return (e, ctx);
        }
    }

    internal List<(int Dp, int Until)> SecurityDpBuffsOf(PlayerZones owner) =>
        ReferenceEquals(owner, State.Player) ? State.PlayerSecurityDpBuffs : State.OpponentSecurityDpBuffs;

    /// <summary>자신의 디지몬 전부 ≪시큐리티 어택 +1≫ (until 턴 종료 시까지)</summary>
    internal void SetAllSecurityAttackUntil(PlayerZones owner, int until)
    {
        if (ReferenceEquals(owner, State.Player)) State.PlayerAllSecurityAttackUntil = until;
        else State.OpponentAllSecurityAttackUntil = until;
    }

    // =====================================================================
    // 메모리 — 효과 주인 기준 (AI 턴에는 AI 메모리 = AiTurnContext.Memory)
    // =====================================================================

    /// <summary>side 기준 메모리 (플레이어면 +가 플레이어 쪽, AI면 +가 AI 쪽)</summary>
    internal int MemoryOf(PlayerZones side)
    {
        bool player = ReferenceEquals(side, State.Player);
        if (_aiTurn != null)
            return player ? -_aiTurn.Memory : _aiTurn.Memory;
        return player ? State.MemoryGauge : -State.MemoryGauge;
    }

    /// <summary>side의 메모리 +amount (상대 쪽에서 보면 -amount)</summary>
    internal void AddMemory(PlayerZones side, int amount)
    {
        if (amount > 0 && MemoryGainBlocked34(side)) return;   // v0.34.0 "Your opponent can't gain memory other than by Tamer effects"
        bool player = ReferenceEquals(side, State.Player);
        if (_aiTurn != null)
            _aiTurn.Memory += player ? -amount : amount;
        else
            State.MemoryGauge += player ? amount : -amount;
        if (amount != 0) RecalculateFieldBuffs();   // v0.37.0 메모리 조건 지속 효과 ("While you have 0 or less memory")
    }

    /// <summary>[이 턴 종료 시] 메모리 -amount — 턴 플레이어의 효과만 의미가 있다</summary>
    internal void AddEndOfTurnMemoryLoss(PlayerZones side, int amount)
    {
        bool player = ReferenceEquals(side, State.Player);
        if (_aiTurn != null)
        {
            if (!player) _aiTurn.EndOfTurnMemoryLoss += amount;
        }
        else if (player)
        {
            State.PendingEndOfTurnMemoryLoss += amount;
        }
    }

    // =====================================================================
    // 공통 동작
    // =====================================================================

    internal PlayerZones OwnerOf(CardInstance inst) =>
        State.DummyOpponent.BattleArea.Contains(inst) || ReferenceEquals(State.DummyOpponent.BreedingArea, inst)
            ? State.DummyOpponent
            : State.Player;

    internal PlayerZones EnemyOf(PlayerZones zones) =>
        ReferenceEquals(zones, State.Player) ? State.DummyOpponent : State.Player;

    internal string SideName(PlayerZones zones) => ReferenceEquals(zones, State.Player) ? "내" : "상대";

    /// <summary>소멸 원인 — ≪방벽≫은 배틀, ≪회피≫는 배틀 · 효과에서만 소멸을 막는다</summary>
    internal enum DeletionCause { Battle, Effect, ZeroDp, Cost }

    /// <summary>지금 처리 중인 소멸의 원인 (소멸 효과 처리 중에만 의미가 있다)</summary>
    internal DeletionCause CurrentDeletionCause { get; private set; }

    /// <summary>
    /// 디지몬을 소멸시킨다 (진화원 포함 트래시) + [소멸 시] 효과. ≪방벽≫ · ≪회피≫로 막으면 false.
    /// DP 0이 되어 소멸하는 것(룰 처리)과 코스트로 소멸시키는 것은 막을 수 없다.
    /// </summary>
    internal bool DeleteFrom(PlayerZones zones, CardInstance instance, DeletionCause cause = DeletionCause.Effect)
    {
        if (!zones.BattleArea.Contains(instance))
            return false;
        if (TryPreventDeletion(zones, instance, cause))
            return false;
        // DP 0 소멸 · 코스트 소멸은 "would be deleted / leave" 효과로도 막지 않는다 (≪회피≫와 같은 규칙).
        // 다만 막지 않고 발휘만 하는 효과(≪디코드≫의 진화원 등장 등 — "배틀 이외로 떠날 때")는 발휘한다
        bool canPrevent = cause is not (DeletionCause.ZeroDp or DeletionCause.Cost);
        if (TryPreventLeave(zones, instance, deletion: true, inBattle: cause == DeletionCause.Battle, canPrevent) && canPrevent)
            return false;
        RemoveDeleted(zones, instance, cause);
        return true;
    }

    /// <summary>≪방벽≫ · ≪회피≫ — 소멸을 막았으면 true</summary>
    private bool TryPreventDeletion(PlayerZones zones, CardInstance inst, DeletionCause cause)
    {
        if (cause is DeletionCause.ZeroDp or DeletionCause.Cost)
            return false;
        if (DeletionBlocked34(zones, inst, cause))   // v0.34.0 "can't be deleted in battle / by your opponent's effects"
            return true;
        string name = inst.TopCard.Name;

        if (cause == DeletionCause.Battle && CardScripts.HasKeyword(inst, Keyword.Barrier) && zones.SecurityStack.Count > 0
            && AskYesNo(zones, inst, $"≪방벽≫ [{name}]이(가) 배틀에서 소멸합니다. 시큐리티를 위에서 1장 파기하고 소멸을 막을까요?", recommended: zones.SecurityStack.Count >= 2))
        {
            var barrierCard = zones.SecurityStack[0];
            zones.Trash.Add(barrierCard);
            zones.SecurityStack.RemoveAt(0);
            NotifySecurityRemoved(zones, barrierCard, trashed: true);   // ≪방벽≫도 효과 — "효과로 시큐리티에서 파기되었을 때"
            EffectLog(zones, $"[≪방벽≫] [{name}]: 시큐리티를 위에서 1장 파기하고 소멸하지 않습니다. (남은 시큐리티 {zones.SecurityStack.Count}장)");
            return true;
        }

        if (CardScripts.HasKeyword(inst, Keyword.Evade) && !inst.IsSuspended
            && AskYesNo(zones, inst, $"≪회피≫ [{name}]이(가) 소멸합니다. 레스트시키고 소멸을 막을까요?", recommended: true))
        {
            Suspend(inst);
            EffectLog(zones, $"[≪회피≫] [{name}]: 레스트 상태가 되어 소멸하지 않습니다.");
            return true;
        }

        // ≪아머 퍼지≫: 이 디지몬에 겹쳐진 카드를 위에서부터 1장 파기하는 것으로 소멸하지 않는다
        if (CardScripts.HasKeyword(inst, Keyword.ArmorPurge) && inst.EvolutionSources.Count > 0
            && AskYesNo(zones, inst, $"≪아머 퍼지≫ [{name}]이(가) 소멸합니다. 맨 위 카드를 파기하고 소멸을 막을까요?", recommended: true))
        {
            var top = inst.TrashTopCard()!;
            ApplyOverflow(zones, new[] { top });
            zones.Trash.Add(top);
            RecalculateFieldBuffs();
            EffectLog(zones, $"[≪아머 퍼지≫] [{top.Name}]을(를) 파기하고 [{inst.TopCard.Name}](으)로 남습니다.");
            TrashExposedFaceDownTops();   // v0.35.0
            return true;
        }

        // ≪스케이프고트≫: 자신의 효과 이외로 소멸할 때, 다른 자신의 디지몬 1마리를 소멸시키는 것으로 소멸하지 않는다
        if ((cause == DeletionCause.Battle || _deletingByOpponentEffect) && CardScripts.HasKeyword(inst, Keyword.Scapegoat))
        {
            var others = zones.BattleArea.Where(d => !ReferenceEquals(d, inst)).OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).ToList();
            if (others.Count > 0)
            {
                var victim = AskDigimon(zones, $"≪스케이프고트≫ [{name}]의 소멸을 막으려면 대신 소멸시킬 자신의 디지몬을 고르세요.", others,
                    others[0].TopCard.Level < inst.TopCard.Level ? others[0] : null, optional: true);
                if (victim != null)
                {
                    EffectLog(zones, $"[≪스케이프고트≫] [{victim.TopCard.Name}]을(를) 소멸시키고 [{name}]은(는) 소멸하지 않습니다.");
                    DeleteFrom(zones, victim, DeletionCause.Cost);
                    return true;
                }
            }
        }
        return false;
    }

    // 지금 처리 중인 소멸이 상대 효과에 의한 것인지 (≪스케이프고트≫ — 자신의 효과 이외)
    private bool _deletingByOpponentEffect;

    /// <summary>상대의 효과로 소멸 (EffectContext.DeleteEnemy)</summary>
    internal bool DeleteByOpponentEffect(PlayerZones zones, CardInstance instance)
    {
        bool outer = _deletingByOpponentEffect;
        _deletingByOpponentEffect = true;
        try { return DeleteFrom(zones, instance); }
        finally { _deletingByOpponentEffect = outer; }
    }

    /// <summary>소멸 처리 (막을 수 있는지는 이미 정해졌을 때)</summary>
    private void RemoveDeleted(PlayerZones zones, CardInstance instance, DeletionCause cause = DeletionCause.Battle)
    {
        if (!zones.BattleArea.Remove(instance))
            return;

        var stack = instance.GetEvolutionStackBottomUp();
        // ≪불굴≫: 진화원을 가진 이 디지몬이 소멸했을 때, 이 카드(≪불굴≫이 적힌 카드)를 코스트 없이 등장
        CardData? fortitude = null;
        if (stack.Count > 0 && CardScripts.HasKeyword(instance, Keyword.Fortitude))
            fortitude = stack.LastOrDefault(s => CardScripts.For(s)?.InheritedKeywords.Contains(Keyword.Fortitude) == true
                                              && CardScripts.For(instance.TopCard)?.Keywords.Contains(Keyword.Fortitude) != true) ?? instance.TopCard;
        ApplyOverflow(zones, stack.Append(instance.TopCard));
        // ≪머티리얼 세이브 N≫: 진화원 N장(디지몬 카드)을 자신의 테이머 아래에 놓을 수 있다
        int materialSave = CardScripts.For(instance.TopCard)?.MaterialSave ?? 0;
        var xrosNames = DigiXrosRequirementNames(instance.TopCard);
        bool IsXrosMaterial(CardData s) => s.Type == CardType.Digimon
            && (xrosNames == null || xrosNames.Any(n => KoreanCardData.DisplayName(s).Contains(n, StringComparison.Ordinal) || s.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));
        if (materialSave > 0 && zones.Tamers.Count > 0 && instance.SourcesAny(IsXrosMaterial)
            && AskYesNo(zones, instance, $"≪머티리얼 세이브 {materialSave}≫ [{instance.TopCard.Name}]의 진화원 {materialSave}장을 테이머 아래에 놓을까요?", recommended: true))
        {
            var saved = ChooseCards(zones, CardChoiceZone.DigivolutionCards, instance.SourcesWhere(IsXrosMaterial).OrderByDescending(s => s.Level).ToList(),
                materialSave, upTo: true, $"≪머티리얼 세이브 {materialSave}≫ 테이머 아래에 놓을 진화원을 고르세요.");
            foreach (var s in saved)
                if (instance.TakeSource(x => ReferenceEquals(x, s)) != null) zones.UnderTamers.Add(s);
            if (saved.Count > 0) EffectLog(zones, $"[≪머티리얼 세이브≫] [{string.Join(", ", saved.Select(s => s.Name))}]을(를) 테이머 아래에 놓았습니다.");
        }
        zones.Trash.Add(instance.TopCard);
        TrashLinks(zones, instance);
        while (instance.EvolutionSources.Count > 0)
            zones.Trash.Add(instance.EvolutionSources.Pop());
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, instance);   // v0.33.0 "When any of your opponent's Digimon leave the battle area"

        // [소멸 시] · "소멸했을 때" · ≪불굴≫ — 효과 처리 중이면 그 효과가 끝난 뒤에
        RunOrDefer(() =>
        {
            var outerCause = CurrentDeletionCause;
            CurrentDeletionCause = cause;   // 이 효과들이 볼 수 있게 ("deleted by an effect" · "by dropping to 0 DP")
            RunEffects(EffectTrigger.OnDeletion, zones, instance, instance.TopCard, sources: stack);
            FireForSide(EffectTrigger.AllyDigimonDeleted, zones, instance);
            FireForSide(EffectTrigger.OpponentDigimonDeleted, EnemyOf(zones), instance);
            CurrentDeletionCause = outerCause;

            // ≪천승≫ (종합 룰 16-43, 임의): 이 디지몬이 소멸했을 때, 이 카드를 시큐리티 위에 놓을 수 있다 (트래시에 있을 때만 — 다른 효과로 옮겨졌으면 못 한다)
            var top = instance.TopCard;
            if (!IsToken(top) && CardScripts.For(top)?.Keywords.Contains(Keyword.Ascension) == true && zones.Trash.Contains(top) && !State.IsGameOver
                && AskYesNo(zones, null, $"≪천승≫ [{top.Name}]을(를) 시큐리티 맨 위에 놓을까요?", recommended: true))
            {
                zones.Trash.Remove(top);
                zones.SecurityStack.Insert(0, top);
                EffectLog(zones, $"[≪천승≫] [{top.Name}]을(를) 시큐리티 맨 위에 놓았습니다.");
                NotifySecurityAdded(zones, top);
            }

            if (fortitude != null && zones.Trash.Contains(fortitude) && CanPlayByEffect(zones, fortitude, PlayOrigin.Trash))   // v0.36.0 효과로 등장 (막혀 있으면 트래시에 남는다)
            {
                EffectLog(zones, $"[≪불굴≫] [{fortitude.Name}]이(가) 코스트 없이 다시 등장합니다.");
                TryPlayByEffect(zones, fortitude, PlayOrigin.Trash, () => zones.Trash.Remove(fortitude));
            }
        });
    }

    // =====================================================================
    // 플레이어에게 묻는 선택 (AI · 테스트는 추천 답)
    // =====================================================================

    /// <summary>플레이어 쪽 선택을 물을 곳 (UI가 등록). null이면 추천 답을 쓴다</summary>
    public IChoiceProvider? PlayerChoices { get; set; }

    internal bool AskYesNo(PlayerZones owner, CardInstance? subject, string question, bool recommended) =>
        ReferenceEquals(owner, State.Player) && PlayerChoices != null
            ? PlayerChoices.Confirm(State, subject, KoreanNameLocalizer.Localize(question), recommended)   // 질문의 영어 카드 이름 → 공식 한글 이름
            : recommended;

    internal CardInstance? AskDigimon(PlayerZones owner, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional)
    {
        if (!ReferenceEquals(owner, State.Player) || PlayerChoices == null)
            return recommended;
        var picked = PlayerChoices.ChooseDigimon(State, KoreanNameLocalizer.Localize(question), options, recommended, optional);
        return picked != null && options.Contains(picked) ? picked : optional ? null : recommended;
    }

    /// <summary>
    /// 효과 대상 고르기 (count마리, 0 = 전부). ranked = 추천 순서의 후보.
    /// 플레이어의 효과이고 정말 고를 것이 있을 때만 묻는다 — 후보가 count 이하면 전부, UI가 미리 고른 대상(preselected)이 맞으면 그것.
    /// AI · 테스트는 추천 순서대로.
    /// </summary>
    internal List<CardInstance> ChooseTargets(PlayerZones owner, IReadOnlyList<CardInstance> ranked, int count, string question, bool optional, CardInstance? preselected)
    {
        if (count <= 0)
            return ranked.ToList();
        bool ask = ReferenceEquals(owner, State.Player) && PlayerChoices != null;
        if (!ask || (ranked.Count <= count && !optional) || ranked.Count == 0)
            return ranked.Take(count).ToList();
        if (count == 1 && preselected != null && ranked.Contains(preselected))
            return new List<CardInstance> { preselected };

        if (count > 1)
            return AskDigimons(owner, ranked, count, upTo: optional, question);   // 여러 마리는 한 번에 고르는 창
        var p = PlayerChoices!.ChooseDigimon(State, question, ranked, ranked[0], optional);
        if (p == null || !ranked.Contains(p))
            return optional ? new List<CardInstance>() : new List<CardInstance> { ranked[0] };
        return new List<CardInstance> { p };
    }

    /// <summary>고르는 효과의 선택지 — 플레이어면 묻고, 아니면 추천(AI 평가 점수가 가장 높은 것)</summary>
    internal int ChooseOption(PlayerZones owner, string question, IReadOnlyList<string> options, int recommended)
    {
        if (!ReferenceEquals(owner, State.Player) || PlayerChoices == null || options.Count <= 1)
            return recommended;
        // 질문 · 선택지의 영어 카드 이름 → 공식 한글 이름
        int pick = PlayerChoices.ChooseOption(State, KoreanNameLocalizer.Localize(question), options.Select(KoreanNameLocalizer.Localize).ToList(), recommended);
        return pick >= 0 && pick < options.Count ? pick : recommended;
    }

    // =====================================================================
    // 어택 키워드 — ≪연계≫ · ≪돌진≫ · ≪속공≫ · ≪진격≫
    // =====================================================================

    /// <summary>이 턴에 어택할 수 있는 상태인지 (액티브 · 등장한 턴이 아니거나 ≪속공≫ · 어택 불가 효과 없음)</summary>
    internal bool CanAttackThisTurn(CardInstance d) =>
        !d.IsSuspended
        && (d.TurnPlayed < State.CurrentTurn || CardScripts.HasKeyword(d, Keyword.Rush))
        && !EffectRules.IsAttackLocked(State, d);

    /// <summary>
    /// "어택했을 때" 키워드 처리 ([어택 시] 효과 다음): ≪연계≫ → ≪돌진≫.
    /// 반환: ≪돌진≫으로 바꾼 어택 대상 (바꾸지 않았으면 null). target = 원래 어택 대상 (시큐리티 어택이면 null)
    /// </summary>
    private CardInstance? ApplyAttackKeywords(CardInstance attacker, CardInstance? target)
    {
        var owner = OwnerOf(attacker);
        var enemy = EnemyOf(owner);
        if (!owner.BattleArea.Contains(attacker))
            return null;

        if (CardScripts.HasKeyword(attacker, Keyword.Alliance))
        {
            var options = owner.BattleArea.Where(d => !ReferenceEquals(d, attacker) && !d.IsSuspended).ToList();
            if (options.Count > 0)
            {
                // 추천: 이 턴에 어택하지 못하고 ≪블로커≫도 아닌 디지몬 중 DP가 가장 높은 것
                var recommended = options.Where(d => !d.HasBlocker && !CanAttackThisTurn(d)).OrderByDescending(d => d.GetCurrentDP()).FirstOrDefault();
                var ally = AskDigimon(owner, $"≪연계≫ [{attacker.TopCard.Name}]: 레스트시켜 DP를 더할 다른 디지몬을 고르세요 (고르지 않아도 됩니다).", options, recommended, optional: true);
                if (ally != null)
                {
                    Suspend(ally);
                    int dp = ally.GetCurrentDP();
                    attacker.AttackDpBonus += dp;
                    attacker.AttackSecurityAttackBonus += 1;
                    EffectLog(owner, $"[≪연계≫] [{ally.TopCard.Name}]을(를) 레스트시켜 [{attacker.TopCard.Name}]이(가) 이 어택 동안 DP +{dp:N0} · ≪시큐리티 어택 +1≫");
                }
            }
        }

        if (CardScripts.HasKeyword(attacker, Keyword.Raid) && !IsAttackTargetLocked(attacker))
        {
            var best = enemy.BattleArea.Where(d => !d.IsSuspended && !CardScripts.HasKeyword(d, Keyword.CannotBeAttacked)).OrderByDescending(d => d.GetCurrentDP()).FirstOrDefault();
            if (best != null && !ReferenceEquals(best, target))
            {
                // 추천: 이기는 배틀일 때만 — 디지몬을 어택 중이었거나, 시큐리티 어택이면 상대 ≪블로커≫ / Lv.5 이상을 잡을 때 (시큐리티 0장이면 다이렉트 어택 우선)
                bool wins = attacker.GetCurrentDP() > best.GetCurrentDP();
                bool recommended = wins && (target != null || (enemy.SecurityStack.Count > 0 && (best.HasBlocker || best.TopCard.Level >= 5)));
                if (AskYesNo(owner, attacker, $"≪돌진≫ [{attacker.TopCard.Name}]: 어택 대상을 상대 [{best.TopCard.Name}(DP {best.GetCurrentDP():N0})]로 바꿀까요?", recommended))
                {
                    EffectLog(owner, $"[≪돌진≫] [{attacker.TopCard.Name}]의 어택 대상을 액티브 상태의 [{best.TopCard.Name}(DP {best.GetCurrentDP():N0})]로 바꿉니다!");
                    NotifyAttackTargetSwitched(attacker);
                    return best;
                }
            }
        }
        return null;
    }

    /// <summary>어택이 끝났을 때: "이 어택 동안" 효과(≪연계≫)를 지우고 [어택 종료 시] 효과</summary>
    private void EndAttack(CardInstance attacker)
    {
        if (ReferenceEquals(CurrentAttacker, attacker)) CurrentAttacker = null;
        attacker.AttackTargetLocked = false;
        attacker.AttackDpBonus = 0;
        attacker.AttackSecurityAttackBonus = 0;
        RecalculateFieldBuffs();
        var owner = OwnerOf(attacker);
        if (owner.BattleArea.Contains(attacker) && !State.IsGameOver)
        {
            RunEffects(EffectTrigger.EndOfAttack, owner, attacker, attacker.TopCard);
            RecalculateFieldBuffs();
        }
        FinishExecuteAttack(attacker);   // ≪에그제큐트≫: 어택 종료 시 소멸
    }

    /// <summary>[자신의 턴 개시 시] 효과 — 자신의 디지몬(메인 · 진화원)과 테이머</summary>
    private void TriggerStartOfTurn(PlayerZones owner)
    {
        foreach (var d in owner.BattleArea.ToList())
            RunEffects(EffectTrigger.StartOfYourTurn, owner, d, d.TopCard);
        foreach (var tamer in owner.Tamers.ToList())
            RunEffects(EffectTrigger.StartOfYourTurn, owner, null, tamer, includeInherited: false);
        RunPlacedOptions(EffectTrigger.StartOfYourTurn, owner);
        RunTrashEffects(EffectTrigger.StartOfYourTurn, owner);   // v0.31.0 [트래시] [~ 시] 효과
        RecalculateFieldBuffs();
    }

    /// <summary>배틀 에어리어의 옵션(≪딜레이≫)에도 시점을 알린다 — 놓인 장마다 (같은 카드 2장이면 2번, 발휘할 때마다 1장씩 파기)</summary>
    private void RunPlacedOptions(EffectTrigger trigger, PlayerZones owner, CardInstance? other = null, CardData? triggerCard = null)
    {
        foreach (var option in owner.OptionsInPlay.Select(p => p.Card).ToList())
            RunEffects(trigger, owner, null, option, other: other, includeInherited: false, triggerCard: triggerCard);
    }

    /// <summary>
    /// 시큐리티 체크로 오픈된 디지몬 카드의 [시큐리티] 효과 (공식 룰: 효과를 발휘한 뒤 배틀). 반환: 결과 메시지 (효과 없으면 빈 문자열)
    /// </summary>
    private (string Message, bool PlayAfterBattle) ResolveSecurityDigimonEffect(CardData securityCard, PlayerZones owner, CardInstance attacker)
    {
        if (!(CardScripts.For(securityCard)?.Effects.Any(e => e.Trigger == EffectTrigger.Security) ?? false))
            { SecurityPlayWithoutBattle = false; return (string.Empty, false); }
        var ctx = RunEffects(EffectTrigger.Security, owner, null, securityCard, other: attacker, chosenTarget: attacker, includeInherited: false);
        RecalculateFieldBuffs();
        SecurityPlayWithoutBattle = ctx.PlayWithoutBattle;   // v0.37.0
        string msg = $"[{SideName(owner)} 시큐리티 디지몬 {securityCard.Name}의 [시큐리티] 효과] {ctx.Message}";
        Log(msg);
        return (msg, ctx.PlayAfterBattle);
    }

    /// <summary>배틀이 끝난 시큐리티 디지몬을 트래시에서 코스트 없이 등장 ("At the end of the battle, play this card")</summary>
    private void PlaySecurityDigimonAfterBattle(PlayerZones owner, CardData card)
    {
        if (EffectPlayBlock(owner, card, PlayOrigin.Security) is { } why) { EffectLog(owner, $"[{card.Name}] {why}."); return; }   // v0.36.0 효과로 등장 — 트래시에 남는다
        if (!owner.Trash.Remove(card))
            return;
        owner.BattleArea.Add(new CardInstance(card, State.CurrentTurn) { IsOwnersTurn = IsTurnOf(owner) });
        RecalculateFieldBuffs();
        Log($"[{SideName(owner)} 시큐리티 디지몬 {card.Name}] 배틀이 끝나 코스트 없이 등장했습니다.");
    }

    /// <summary>
    /// ≪진격≫: 상대 쪽 메모리가 1 이상이면 이 디지몬으로 어택할 수 있다.
    /// 플레이어는 턴이 끝나지 않고 이 디지몬의 어택 1번만 할 수 있는 상태(State.BlitzAttacker)가 되고, AI는 곧바로 어택한다.
    /// </summary>
    internal bool TryGrantBlitz(PlayerZones owner, CardInstance digimon)
    {
        if (MemoryOf(owner) > -1 || !owner.BattleArea.Contains(digimon) || !CanAttackThisTurn(digimon))
            return false;
        if (ReferenceEquals(owner, State.Player))
            State.BlitzAttacker = digimon;
        else if (_aiTurn != null)
            _aiTurn.BlitzAttacker = digimon;
        else
            return false;
        return true;
    }

    private bool DeleteDigimon(CardInstance instance) => DeleteFrom(State.Player, instance, DeletionCause.Battle);

    private bool DeleteOpponentDigimon(CardInstance instance) => DeleteFrom(State.DummyOpponent, instance, DeletionCause.Battle);

    internal void Mill(PlayerZones zones, int count, string reason)
    {
        int n = 0;
        CardData? first = null;
        while (n < count && zones.MainDeck.Count > 0)
        {
            first ??= zones.MainDeck[0];
            zones.Trash.Add(zones.MainDeck[0]);
            zones.MainDeck.RemoveAt(0);
            n++;
        }
        if (n > 0)
        {
            Log($"[효과] {reason}: {SideName(zones)} 덱 위에서 {n}장 파기");
            FireForSide(EffectTrigger.AllyDeckTrashed, zones, null, triggerCard: first);   // "When a card is trashed from your deck"
            FireForSide(EffectTrigger.OpponentDeckTrashed, EnemyOf(zones), null, triggerCard: first);   // v0.31.0 "When your opponent's deck is trashed from"
            FireThisCardTrashed(zones, zones.Trash.Skip(zones.Trash.Count - n).ToList(), EffectTrigger.ThisTrashedFromDeck);   // "When this card is trashed from your deck" (덱에서 직접)
        }
    }

    internal CardData? DrawFor(PlayerZones zones)
    {
        if (zones.MainDeck.Count == 0)
            return null;
        var card = zones.MainDeck[0];
        zones.MainDeck.RemoveAt(0);
        zones.Hand.Add(card);
        DrawSerial[zones] = DrawSerial.GetValueOrDefault(zones) + 1;   // "효과로 패가 늘어났다" — 뽑은 카드를 곧 버려 장수가 같아도 (공식 Q&A BT15-002)
        return card;
    }

    /// <summary>쪽마다 드로우한 횟수 (늘기만 한다) — 효과 처리 전후를 비교해 "효과로 패가 늘어났다"를 안다 (v0.30.0)</summary>
    internal readonly Dictionary<PlayerZones, int> DrawSerial = new(ReferenceEqualityComparer.Instance);

    /// <summary>≪리커버리 +N (덱)≫: 덱 위에서 N장을 시큐리티 위에 놓는다</summary>
    internal int Recovery(PlayerZones zones, int count)
    {
        int n = 0;
        while (n < count && zones.MainDeck.Count > 0)
        {
            var recovered = zones.MainDeck[0];
            zones.SecurityStack.Insert(0, zones.MainDeck[0]);
            zones.MainDeck.RemoveAt(0);
            NotifySecurityAdded(zones, recovered);
            n++;
        }
        return n;
    }

    /// <summary>덱 위에서 count장을 오픈해 조건에 맞는 카드를 최대 maxTake장 패에 넣고, 나머지는 덱 아래로 되돌린다</summary>
    internal List<CardData> RevealAndTake(PlayerZones zones, int count, Func<CardData, bool> match, int maxTake)
    {
        var revealed = zones.MainDeck.Take(count).ToList();
        zones.MainDeck.RemoveRange(0, revealed.Count);
        var taken = revealed.Where(match).OrderByDescending(c => c.Level).ThenByDescending(c => c.DP).Take(maxTake).ToList();
        foreach (var c in taken)
        {
            revealed.Remove(c);
            zones.Hand.Add(c);
        }
        zones.MainDeck.AddRange(revealed);
        return taken;
    }

    internal void ReturnToHand(PlayerZones zones, CardInstance inst)
    {
        if (ReturnBlocked34(zones, inst))   // v0.34.0 "can't be returned to the hand or deck by your opponent's effects"
            return;
        if (TryPreventLeave(zones, inst, deletion: false, inBattle: false, destination: LeaveDestination.Hand))
            return;
        if (!zones.BattleArea.Remove(inst))
            return;
        ApplyOverflow(zones, inst.GetFaceUpStackBottomUp().Append(inst.TopCard));
        TrashLinks(zones, inst);
        while (inst.EvolutionSources.Count > 0)
            zones.Trash.Add(inst.EvolutionSources.Pop());
        zones.Hand.Add(inst.TopCard);
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, inst);   // v0.33.0
        if (!IsToken(inst.TopCard)) NotifyReturnedToHand(zones, inst);   // v0.33.0 "When another Digimon is returned to the hand"
        FireForSide(EffectTrigger.OpponentHandAddedByEffect, EnemyOf(zones), null, triggerCard: inst.TopCard);   // "When an effect adds cards to your opponent's hand"
    }

    /// <summary>≪퇴화 N≫ — 버린 매수 반환</summary>
    internal int DeDigivolve(PlayerZones zones, CardInstance inst, int count)
    {
        if (CardScripts.HasKeyword(inst, Keyword.UnaffectedByOpponentDeDigivolve) && IsOpponentEffectFor(zones))   // v0.36.0 "their ＜De-Digivolve＞ effects don't affect this Digimon" (BT22-060)
        {
            EffectLog(zones, $"[{inst.TopCard.Name}]은(는) 상대의 ≪퇴화≫ 효과를 받지 않습니다.");
            return 0;
        }
        int n = 0;
        while (n < count)
        {
            var removed = inst.DeDigivolveOnce();
            if (removed == null) break;
            zones.Trash.Add(removed);
            ApplyOverflow(zones, new[] { removed });
            n++;
        }
        if (n > 0) TrashExposedFaceDownTops();   // v0.35.0 뒷면 진화원이 맨 위가 되면 룰 처리로 파기 (17-1-3-2-4)
        return n;
    }

    /// <summary>DP 마이너스 — DP가 0 이하가 되면 소멸 (반환: 소멸 여부)</summary>
    internal bool ApplyDpMinus(CardInstance target, int amount, string source)
    {
        var zones = OwnerOf(target);
        if (DpReductionBlocked34(target))   // v0.34.0 "can't have its DP reduced"
            return false;
        target.TemporaryDpBonus -= amount;
        target.ActiveSkillBuffLabels.Add($"🔻{source} (DP -{amount:N0})");
        Log($"[효과] {source}: {SideName(zones)} [{target.TopCard.Name}] DP -{amount:N0}");

        if (target.TopCard.DP + target.GetTotalBonusDP() > 0)
            return false;

        if (!DeleteFrom(zones, target, DeletionCause.ZeroDp))
            return false;
        Log($"[효과] {SideName(zones)} [{target.TopCard.Name}]의 DP가 0이 되어 소멸했습니다.");

        // 상대 디지몬이 DP 0으로 소멸했을 때 (토코몬 · 파닥몬 진화원 등)
        var other = EnemyOf(zones);
        foreach (var d in other.BattleArea.ToList())
            RunEffects(EffectTrigger.OpponentDigimonDeletedByZeroDp, other, d, null);
        return true;
    }

    /// <summary>
    /// DP ± (until 턴 종료 시까지). 이 턴 동안이면 기존 임시 DP(마이너스는 ApplyDpMinus — 0이 되면 소멸), 더 길면 기간 DP.
    /// 반환: DP 0이 되어 소멸했는지
    /// </summary>
    internal bool ApplyDpChange(CardInstance target, int dp, int until, string source)
    {
        if (until <= State.TurnSequence)
        {
            if (dp < 0)
                return ApplyDpMinus(target, -dp, source);
            target.TemporaryDpBonus += dp;
            target.ActiveSkillBuffLabels.Add($"✨{source} (DP +{dp:N0})");
            return false;
        }

        var zones = OwnerOf(target);
        if (dp < 0 && DpReductionBlocked34(target))   // v0.34.0
            return false;
        target.TimedDpBuffs.Add((dp, until));
        RecalculateFieldBuffs();
        if (dp >= 0 || target.TopCard.DP + target.GetTotalBonusDP() > 0)
            return false;
        if (!DeleteFrom(zones, target, DeletionCause.ZeroDp))
            return false;
        Log($"[효과] {SideName(zones)} [{target.TopCard.Name}]의 DP가 0이 되어 소멸했습니다.");
        var other = EnemyOf(zones);
        foreach (var d in other.BattleArea.ToList())
            RunEffects(EffectTrigger.OpponentDigimonDeletedByZeroDp, other, d, null);
        return true;
    }

    /// <summary>디지몬을 덱 아래로 (맨 위 카드와 진화원 모두)</summary>
    internal void BottomDeck(PlayerZones zones, CardInstance inst)
    {
        if (ReturnBlocked34(zones, inst))   // v0.34.0
            return;
        if (TryPreventLeave(zones, inst, deletion: false, inBattle: false, destination: LeaveDestination.Deck))
            return;
        if (!zones.BattleArea.Remove(inst))
            return;
        ApplyOverflow(zones, inst.GetFaceUpStackBottomUp().Append(inst.TopCard));
        TrashLinks(zones, inst);
        zones.MainDeck.Add(inst.TopCard);
        foreach (var s in inst.GetEvolutionStackBottomUp().Reverse())
            zones.MainDeck.Add(s);
        inst.EvolutionSources.Clear();
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, inst);   // v0.33.0
    }

    /// <summary>디지몬을 주인의 시큐리티 맨 위에 뒷면으로 (맨 위 카드만 — 진화원은 트래시)</summary>
    internal void PlaceOnSecurity(PlayerZones zones, CardInstance inst, bool top = true)   // v0.37.0 top = false: 맨 아래
    {
        if (TryPreventLeave(zones, inst, deletion: false, inBattle: false, destination: LeaveDestination.Security))
            return;
        if (!zones.BattleArea.Remove(inst))
            return;
        ApplyOverflow(zones, inst.GetFaceUpStackBottomUp().Append(inst.TopCard));
        TrashLinks(zones, inst);
        while (inst.EvolutionSources.Count > 0)
            zones.Trash.Add(inst.EvolutionSources.Pop());
        if (!IsToken(inst.TopCard))
        {
            if (top) zones.SecurityStack.Insert(0, inst.TopCard); else zones.SecurityStack.Add(inst.TopCard);
            NotifySecurityAdded(zones, inst.TopCard);
        }
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, inst);   // v0.33.0
    }

    /// <summary>진화원을 위에서부터 count장 파기. 반환: 파기한 카드</summary>
    internal List<CardData> TrashTopSources(PlayerZones zones, CardInstance inst, int count)
    {
        var removed = new List<CardData>();
        while (removed.Count < count && inst.EvolutionSources.Count > 0)
            removed.Add(inst.EvolutionSources.Pop());
        zones.Trash.AddRange(removed);
        ApplyOverflow(zones, removed);
        return removed;
    }

    /// <summary>
    /// 카드를 코스트 없이 등장시킨다 (효과로 등장 — [등장 시] 효과는 발휘한다). 디지몬은 배틀 에어리어, 테이머는 테이머 존.
    /// </summary>
    internal void PlayWithoutCost(PlayerZones owner, CardData card, bool triggerOnPlay = true, bool suspended = false)
    {
        _playingByEffect++;
        try { PlayWithoutCostCore(owner, card, triggerOnPlay, suspended); }
        finally { _playingByEffect--; }
    }

    private void PlayWithoutCostCore(PlayerZones owner, CardData card, bool triggerOnPlay, bool suspended)
    {
        if (card.Type == CardType.Tamer)
        {
            AddTamer(owner, card);
            EffectLog(owner, $"[효과] 테이머 [{card.Name}] 코스트 없이 등장");
            if (triggerOnPlay) RunEffects(EffectTrigger.OnPlay, owner, null, card, includeInherited: false);
            FireForSide(EffectTrigger.AllyTamerPlayed, owner, null, triggerCard: card);
            FireForSide(EffectTrigger.OpponentTamerPlayed, EnemyOf(owner), null, triggerCard: card);
            RecalculateFieldBuffs();
            return;
        }
        var inst = new CardInstance(card, State.CurrentTurn) { IsOwnersTurn = IsTurnOf(owner), IsSuspended = suspended };
        owner.BattleArea.Add(inst);
        PlayCollector?.Add(inst);
        RecalculateFieldBuffs();
        EffectLog(owner, $"[효과] [{card.Name}(Lv.{card.Level})] 코스트 없이 {(suspended ? "레스트 상태로 " : "")}등장");
        if (triggerOnPlay)
            ApplyOnPlayEffects(owner, inst);
    }

    /// <summary>플레이어의 어택이 상대 효과로 바뀐 대상 (플레이어 어택 처리 중에만 쓰인다)</summary>
    private CardInstance? _playerAttackRedirect;

    /// <summary>
    /// "어택 대상을 이 디지몬으로 바꾼다" — defender = 효과 주인(어택당하는 쪽). AI 어택이면 AI 턴의 어택 대상을,
    /// 플레이어 어택이면 진행 중인 어택의 대상을 바꾼다. 반환: 바꿨는지
    /// </summary>
    internal bool RedirectAttack(PlayerZones defender, CardInstance newTarget)
    {
        if (!defender.BattleArea.Contains(newTarget) || (CurrentAttacker is { } locked && IsAttackTargetLocked(locked)))
            return false;
        if (ReferenceEquals(defender, State.Player) && _aiTurn != null)
        {
            _aiTurn.AttackTarget = newTarget;
            NotifyAttackTargetSwitched(CurrentAttacker);
            return true;
        }
        if (ReferenceEquals(defender, State.DummyOpponent) && _aiTurn == null)
        {
            _playerAttackRedirect = newTarget;
            NotifyAttackTargetSwitched(CurrentAttacker);
            return true;
        }
        return false;
    }

    /// <summary>이 디지몬의 어택 대상은 바뀌지 않는다 — 이 어택 동안("can't be switched for the turn") 또는 지속 효과("[Your Turn] ~ can't change")</summary>
    private static bool IsAttackTargetLocked(CardInstance attacker) =>
        attacker.AttackTargetLocked || CardScripts.HasKeyword(attacker, Keyword.AttackTargetLocked);

    // 어택 대상이 바뀌었을 때 효과를 처리하는 중 (그 효과가 다시 대상을 바꿔도 거듭 발동하지 않는다)
    private bool _notifyingTargetSwitch;

    /// <summary>"When an attack target is switched" · "When this Digimon's attack target is switched" — 효과 · ≪돌진≫으로 어택 대상이 바뀐 뒤</summary>
    private void NotifyAttackTargetSwitched(CardInstance? attacker)
    {
        if (attacker == null || _notifyingTargetSwitch || State.IsGameOver)
            return;
        var owner = OwnerOf(attacker);
        _notifyingTargetSwitch = true;
        try
        {
            FireForSide(EffectTrigger.AttackTargetSwitched, owner, attacker);
            FireForSide(EffectTrigger.AttackTargetSwitched, EnemyOf(owner), attacker);
            FireForSide(EffectTrigger.AllyAttackTargetSwitched, owner, attacker);
        }
        finally { _notifyingTargetSwitch = false; }
    }

    /// <summary>
    /// 효과로 진화: 패(from이 있으면 그곳 — 트래시 등)의 evoCard로 target을 진화시킨다 (cost만큼 메모리 지불) → 진화 보너스 드로우 → [진화 시] 효과.
    /// 효과 처리 도중에는 메모리가 0 미만이 되어도 턴이 바로 끝나지 않는다 (효과가 끝난 뒤 엔진이 판단).
    /// </summary>
    internal void EvolveByEffect(PlayerZones owner, CardInstance target, CardData evoCard, int cost, List<CardData>? from = null)
    {
        if (EffectRules.IsDigivolveLocked(State, target) || !CardScripts.EvoAllowed(target, evoCard))   // v0.34.0 "can only digivolve into ~"
        {
            EffectLog(owner, $"[{target.TopCard.Name}]은(는) 진화할 수 없습니다 (카드 효과).");
            return;
        }
        if (!(from ?? owner.Hand).Remove(evoCard))
            return;
        cost = ApplyEvoCostModifiers(owner, target, evoCard, cost);   // "When this Digimon would digivolve, reduce the digivolution cost" (코스트 없는 진화는 그대로 0)
        target.Evolve(evoCard);
        AddMemory(owner, -cost);
        if (ReferenceEquals(owner, State.Player))
        {
            State.TotalMemorySpent += cost;
            State.TotalEvolutionsCount++;
        }
        DrawFor(owner);
        RecalculateFieldBuffs();
        EffectLog(owner, $"[효과로 진화] [{evoCard.Name}(Lv.{evoCard.Level})] (코스트 {cost})");
        if (owner.BattleArea.Contains(target))
        {
            bool fromTrash = ReferenceEquals(from, owner.Trash);   // v0.33.0 "When any of your Digimon digivolve from the trash" · "this card is digivolving from the trash"
            if (fromTrash) _digivolvingFromTrash++;
            try
            {
                _whenDigivolvingByEffect++;   // v0.33.0 [진화 시] "If played or digivolved by effects"
                try { ApplyWhenDigivolvingEffects(owner, target, evoCard); }
                finally { _whenDigivolvingByEffect--; }
                _digivolvingByEffect++;   // v0.31.0 "When effects digivolve your opponent's Digimon"
                try { FireForSide(EffectTrigger.AllyDigimonDigivolved, owner, target); }
                finally { _digivolvingByEffect--; }
            }
            finally { if (fromTrash) _digivolvingFromTrash--; }
        }
    }

    private int _digivolvingByEffect;
    private int _whenDigivolvingByEffect;
    private int _digivolvingFromTrash;

    /// <summary>v0.33.0 지금 알리는 진화([진화 시] · 진화했을 때)가 트래시의 카드로 진화한 것인지</summary>
    internal bool DigivolvingFromTrash => _digivolvingFromTrash > 0;

    /// <summary>v0.33.0 지금 발휘하는 [진화 시]가 효과로 진화한 것인지 ("If played or digivolved by effects")</summary>
    internal bool WhenDigivolvingByEffect => _whenDigivolvingByEffect > 0;

    /// <summary>지금 알리는 진화가 효과로 진화한 것인지 (v0.31.0)</summary>
    internal bool DigivolvingByEffect => _digivolvingByEffect > 0;

    /// <summary>토큰 카드 아이디 ("TOKEN-" 접두사) — 필드를 떠나면 사라진다</summary>
    internal static bool IsToken(CardData card) => card.CardId.StartsWith("TOKEN-", StringComparison.Ordinal);

    /// <summary>필드를 떠난 토큰은 트래시 · 패 · 덱에 남지 않는다</summary>
    private static void PurgeTokens(PlayerZones zones)
    {
        zones.Trash.RemoveAll(IsToken);
        zones.Hand.RemoveAll(IsToken);
        zones.MainDeck.RemoveAll(IsToken);
    }

    /// <summary>ACE ≪오버플로우 -N≫: 배틀 에어리어 · 진화원에서 다른 곳으로 간 카드마다 주인의 메모리 -N</summary>
    private void ApplyOverflow(PlayerZones owner, IEnumerable<CardData> leavingCards)
    {
        foreach (var card in leavingCards)
        {
            int n = CardScripts.For(card)?.OverflowMemory ?? 0;
            if (n <= 0) continue;
            AddMemory(owner, -n);
            EffectLog(owner, $"[≪오버플로우 -{n}≫] [{card.Name}]이(가) 필드를 떠나 메모리 -{n} (현재 {MemoryOf(owner)})");
        }
    }

    /// <summary>패에서 count장 파기 (낮은 레벨 · 옵션 아닌 카드부터 — 사람에게 묻는 화면은 4단계). 반환: 버린 매수</summary>
    internal int DiscardFromHand(PlayerZones zones, int count)
    {
        var ranked = zones.Hand.OrderBy(c => c.Type == CardType.Option ? 1 : 0).ThenBy(c => c.Level).ThenBy(c => c.PlayCost).ToList();
        var picks = ChooseCards(zones, CardChoiceZone.Hand, ranked, count, upTo: false, $"버릴 패 {count}장을 고르세요.");
        foreach (var c in picks)
        {
            zones.Hand.Remove(c);
            zones.Trash.Add(c);
        }
        NotifyHandTrashed(zones, picks);
        return picks.Count;
    }

    /// <summary>"When your hand is trashed from" · "When your opponent's hand is trashed from" — 패를 버린 뒤 (1번에 1회)</summary>
    internal void NotifyHandTrashed(PlayerZones zones, IReadOnlyCollection<CardData> trashed)
    {
        if (trashed.Count == 0)
            return;
        var first = trashed.First();
        var outerCards = CurrentHandTrashed;
        var outerBy = HandTrashedBy;
        CurrentHandTrashed = trashed.ToList();   // v0.31.0 "When a Digimon card with the [X] trait is trashed from your hand" — 버린 카드 조건
        HandTrashedBy = CurrentEffectOwner;      // v0.31.0 "~ by your effect"
        try
        {
            FireForSide(EffectTrigger.AllyHandTrashed, zones, null, triggerCard: first);
            FireForSide(EffectTrigger.OpponentHandTrashed, EnemyOf(zones), null, triggerCard: first);
        }
        finally { CurrentHandTrashed = outerCards; HandTrashedBy = outerBy; }
        FireThisCardTrashed(zones, trashed, EffectTrigger.ThisTrashedFromHand);   // "When this card is trashed from your hand"
    }

    /// <summary>지금 알리는 "패가 파기되었을 때"의 버린 카드 전부 (밖이면 빈 목록)</summary>
    internal IReadOnlyList<CardData> CurrentHandTrashed { get; private set; } = Array.Empty<CardData>();

    /// <summary>지금 알리는 "패가 파기되었을 때"의 파기한 효과의 주인 (효과 밖이면 null)</summary>
    internal PlayerZones? HandTrashedBy { get; private set; }

    // 효과로 등장시키는 중 ("When an effect plays one of your Digimon")
    private int _playingByEffect;

    /// <summary>지금 등장하는 카드가 효과로 등장하는지 (PlayWithoutCost 안)</summary>
    internal bool PlayingByEffect => _playingByEffect > 0;

    /// <summary>어택하면서 레스트시키는 중 ("When an effect suspends ~"는 어택 · 블록으로 레스트한 것을 빼고)</summary>
    internal bool SuspendingForAttack { get; private set; }

    private void SuspendAsAttacker(CardInstance attacker)
    {
        SuspendingForAttack = true;
        try { Suspend(attacker); }
        finally { SuspendingForAttack = false; }
    }

    /// <summary>지금이 owner의 턴인지</summary>
    internal bool IsTurnOf(PlayerZones owner) => ReferenceEquals(owner, State.Player) == _playerTurnActive;

    /// <summary>[자신의 메인 페이즈 개시 시] 효과 — 자신의 디지몬(메인 · 진화원)과 테이머</summary>
    private void TriggerStartOfMainPhase(PlayerZones owner)
    {
        foreach (var d in owner.BattleArea.ToList())
            RunEffects(EffectTrigger.StartOfYourMainPhase, owner, d, d.TopCard);
        foreach (var tamer in owner.Tamers.ToList())
            RunEffects(EffectTrigger.StartOfYourMainPhase, owner, null, tamer, includeInherited: false);
        RunPlacedOptions(EffectTrigger.StartOfYourMainPhase, owner);
        RunTrashEffects(EffectTrigger.StartOfYourMainPhase, owner);   // v0.31.0 [트래시] [~ 시] 효과
        RecalculateFieldBuffs();
    }

    // =====================================================================
    // [메인] 기동 효과 — 테이머 · 디지몬 (≪디지버스트≫ 포함)
    // =====================================================================

    /// <summary>쓸 수 있는 [메인] 기동 효과 1건 (Digimon = 효과를 가진 디지몬, 테이머 · 패 · 트래시면 null, Zone = 효과를 쓰는 곳)</summary>
    public sealed record MainAbility(CardInstance? Digimon, CardData Card, MainAbilityZone Zone = MainAbilityZone.Field, bool Inherited = false, bool Gained = false);

    // 이번 턴에 쓴 기동 효과 (같은 효과를 한 턴에 여러 번 쓰지 않도록 — 비용이 없는 효과의 무한 반복 방지)
    // 테이머는 같은 카드가 여러 장일 수 있어(같은 CardData 참조) 쓴 횟수를 센다 — 장수만큼 쓸 수 있다
    private readonly Dictionary<object, int> _usedMainAbilities = new();

    /// <summary>owner가 지금 쓸 수 있는 [메인] 기동 효과 목록 (이번 턴에 이미 쓴 것 제외)</summary>
    public IReadOnlyList<MainAbility> GetMainAbilities(PlayerZones owner)
    {
        var list = new List<MainAbility>();
        foreach (var d in owner.BattleArea)
        {
            if (HasMainAbility(d.TopCard) && !_usedMainAbilities.ContainsKey(d))
                list.Add(new MainAbility(d, d.TopCard));
            // v0.31.0 얻은 진화원 카드의 [메인] ("gains all effects of ~ in this Digimon's digivolution cards") — 그 카드마다
            foreach (var gained in CardScripts.GainedSources(d))
                if (HasMainAbility(gained) && !_usedMainAbilities.ContainsKey((d, gained.CardId, "gained")))
                    list.Add(new MainAbility(d, gained, Gained: true));
            // 진화원의 [메인] 기동 효과 (v0.29.0 — "[Main] [Once Per Turn] By placing this Digimon's top stacked card ~"): 진화원 카드마다
            foreach (var source in d.GetFaceUpStackBottomUp().GroupBy(s => s.CardId).Select(g => g.First()))
                if (HasInheritedMainAbility(source) && !_usedMainAbilities.ContainsKey((d, source.CardId)))
                    list.Add(new MainAbility(d, source, Inherited: true));
        }
        // ≪트레이닝≫처럼 육성 에어리어에서도 쓸 수 있는 기동 효과
        if (owner.BreedingArea is { } breeding && CardScripts.For(breeding.TopCard)?.ActivateInBreeding == true
            && HasMainAbility(breeding.TopCard) && !_usedMainAbilities.ContainsKey(breeding))
            list.Add(new MainAbility(breeding, breeding.TopCard));
        foreach (var t in owner.Tamers.Distinct())
            if (HasMainAbility(t) && _usedMainAbilities.GetValueOrDefault(t) < owner.Tamers.Count(x => ReferenceEquals(x, t)))
                list.Add(new MainAbility(null, t));
        // 배틀 에어리어의 옵션 (≪딜레이≫ — 놓은 다음 턴부터)
        foreach (var o in owner.OptionsInPlay.Select(p => p.Card).Distinct())
            if (HasMainAbility(o) && ReadyPlacedOptions(owner, o) > 0)
                list.Add(new MainAbility(null, o));
        return list;
    }

    private static bool HasInheritedMainAbility(CardData card) =>
        CardScripts.For(card)?.Effects.Any(e => e.Trigger == EffectTrigger.ActivateMain && e.Source == EffectSource.Inherited) ?? false;

    private static bool HasMainAbility(CardData card) =>
        CardScripts.For(card)?.Effects.Any(e => e.Trigger == EffectTrigger.ActivateMain && e.Source == EffectSource.Main) ?? false;

    /// <summary>플레이어가 [메인] 기동 효과를 쓴다 (메인 페이즈, 턴에 1번). 메모리가 0 미만이 되면 턴이 끝난다</summary>
    public ActionResult ActivateMainAbility(CardInstance? digimon, CardData card)
    {
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null)
            return ActionResult.Fail("지금은 [메인] 효과를 쓸 수 없습니다.");
        var ability = GetMainAbilities(State.Player).FirstOrDefault(a => ReferenceEquals(a.Digimon, digimon) && ReferenceEquals(a.Card, card));
        if (ability == null)
            return ActionResult.Fail($"[{card.Name}]의 [메인] 효과는 지금 쓸 수 없습니다 (이번 턴에 이미 썼거나 효과가 없음).");
        bool used = UseMainAbility(State.Player, digimon, card, ability.Inherited, ability.Gained);
        CheckTurnEndAndAutoReset();
        // 패 · 트래시 [메인] 효과(ActivateZoneEffect)와 같이: 조건 · 비용이 맞지 않아 아무것도 안 했으면 "썼다"가 아니라 실패로 알린다
        if (!used)
            return ActionResult.Fail($"[{card.Name}]의 [메인] 효과: 조건 미충족 — 조건이 맞지 않거나 비용을 낼 수 없어 효과를 발휘하지 않았습니다.");
        return ActionResult.Success($"[{card.Name}]의 [메인] 효과를 썼습니다.");
    }

    /// <summary>[메인] 기동 효과를 쓴다. 반환: 실제로 효과를 발휘했는지 (아니면 이번 턴에 쓴 것으로 치지 않는다)</summary>
    private bool UseMainAbility(PlayerZones owner, CardInstance? digimon, CardData card, bool inherited = false, bool gained = false)
    {
        EffectLog(owner, inherited ? $"[메인] {digimon?.TopCard.Name}의 진화원 {card.Name}의 효과 사용" : $"[메인] {card.Name}의 효과 사용");
        // 진화원의 [메인]: 그 진화원 카드의 진화원 효과만 (디지몬 = Self)
        var ctx = inherited && digimon != null
            ? RunEffects(EffectTrigger.ActivateMain, owner, digimon, null, includeInherited: true, sources: new[] { card })
            : RunEffects(EffectTrigger.ActivateMain, owner, digimon, card, includeInherited: false);
        // 발동 조건("If ~")이 맞지 않았거나, 비용을 내지 않았거나 조건이 맞지 않아 아무 문장도 처리하지 않았으면 사용으로 치지 않는다
        bool used = ctx.AnyEffectRan && !(ctx.StepsRan && !ctx.AnyStepResolved);
        if (used)
        {
            object key = gained && digimon != null ? (digimon, card.CardId, "gained") : inherited && digimon != null ? (digimon, card.CardId) : (object?)digimon ?? card;
            _usedMainAbilities[key] = _usedMainAbilities.GetValueOrDefault(key) + 1;
        }
        else
            EffectLog(owner, $"[메인] {card.Name}: 조건 미충족 — 효과를 발휘하지 않았습니다");
        RecalculateFieldBuffs();
        return used;
    }

    /// <summary>AI: 쓸 만한 [메인] 기동 효과를 쓴다 (효과 문장 점수가 양수인 것)</summary>
    private void AiUseMainAbilities()
    {
        var abilities = GetMainAbilities(State.DummyOpponent)
            .Concat(GetHandMainAbilities(State.DummyOpponent))
            .Concat(GetTrashMainAbilities(State.DummyOpponent)).ToList();
        foreach (var ability in abilities)
        {
            if (State.IsGameOver || (_aiTurn?.Memory ?? 0) < 0) break;
            if (ability.Zone != MainAbilityZone.Field && !GetZoneAbilities(State.DummyOpponent, ability.Zone).Any(a => ReferenceEquals(a.Card, ability.Card)))
                continue;   // 앞의 효과로 패 · 트래시를 떠났거나 이미 썼다
            if (ability.Inherited && (ability.Digimon == null || !State.DummyOpponent.BattleArea.Contains(ability.Digimon) || !ability.Digimon.HasSource(ability.Card.CardId)))
                continue;   // 앞의 효과로 그 디지몬이 떠났거나 그 카드가 진화원이 아니게 되었다
            if (!AiPolicy.ShouldUseMainAbility(this, ability, _aiTurn?.Memory ?? 0)) continue;
            AddEffectEvent(ability.Card, CaptureEffectLogs(() =>
            {
                if (ability.Zone == MainAbilityZone.Field) UseMainAbility(State.DummyOpponent, ability.Digimon, ability.Card, ability.Inherited, ability.Gained);
                else UseZoneAbility(State.DummyOpponent, ability.Card, ability.Zone);
            }));
        }
    }

    /// <summary>지금 레스트가 ≪블로커≫ 블록 때문인지 (레스트 효과 처리 중에만 의미가 있다 — 신태일 ST5-14)</summary>
    internal bool SuspendingForBlock { get; private set; }

    /// <summary>≪블로커≫로 블록하며 레스트</summary>
    internal void SuspendForBlock(CardInstance blocker)
    {
        SuspendingForBlock = true;
        try { Suspend(blocker); }
        finally { SuspendingForBlock = false; }
    }

    /// <summary>디지몬을 레스트시킨다 + "이 디지몬이 레스트 상태가 되었을 때" 효과</summary>
    internal void Suspend(CardInstance inst)
    {
        if (inst.IsSuspended || EffectRules.IsSuspendLocked(State, inst))
            return;
        inst.IsSuspended = true;
        var owner = OwnerOf(inst);
        if (!owner.BattleArea.Contains(inst))
            return;
        bool tamerToo = inst.TamerProxyOf != null && SuspendProxyTamer(owner, inst);   // v0.36.0 디지몬으로 취급하는 테이머 — 테이머도 레스트
        WithSuspendCause(() =>   // v0.32.0 "When an effect suspends ~"
        {
            RunEffects(EffectTrigger.ThisDigimonSuspends, owner, inst, inst.TopCard);
            FireForSide(EffectTrigger.AllyDigimonSuspended, owner, inst);
            FireForSide(EffectTrigger.OpponentDigimonSuspended, EnemyOf(owner), inst);
            if (tamerToo && inst.TamerProxyOf is { } tamer)
            {
                FireForSide(EffectTrigger.AllyTamerSuspended, owner, null, triggerCard: tamer);
                FireForSide(EffectTrigger.OpponentTamerSuspended, EnemyOf(owner), null, triggerCard: tamer);
            }
            return true;
        });
    }

    /// <summary>
    /// zones의 시큐리티에서 카드가 빠졌다 (체크 · 효과로 파기 · 패에 추가 · 등장 등) —
    /// 주인 쪽 "When your security stack is removed from", 상대 쪽 "When your opponent's security stack is removed from".
    /// 시큐리티를 빼는 곳에서 빼고 난 뒤 부른다.
    /// </summary>
    internal void NotifySecurityRemoved(PlayerZones zones, CardData? card = null, bool trashed = false)
    {
        var (outerEffect, outerTrashed) = (SecurityRemovedByEffect, SecurityRemovedTrashed);
        SecurityRemovedByEffect = !_securityCheckRemoval;   // v0.33.0 "When an effect removes cards from your security stack" · "When a card is trashed from your security stack by an effect"
        SecurityRemovedTrashed = trashed;
        try { NotifySecurityRemovedCore(zones, card, trashed); }
        finally { (SecurityRemovedByEffect, SecurityRemovedTrashed) = (outerEffect, outerTrashed); }
    }

    /// <summary>v0.33.0 지금 알리는 "시큐리티가 빠졌다"가 효과로 빠진 것인지 (체크가 아님) · 파기된 것인지</summary>
    internal bool SecurityRemovedByEffect { get; private set; }
    internal bool SecurityRemovedTrashed { get; private set; }
    private bool _securityCheckRemoval;

    private void NotifySecurityRemovedCore(PlayerZones zones, CardData? card, bool trashed)
    {
        FireForSide(EffectTrigger.AllySecurityRemoved, zones, null, triggerCard: card);
        FireForSide(EffectTrigger.OpponentSecurityRemoved, EnemyOf(zones), null, triggerCard: card);
        // 효과가 시큐리티에서 직접 파기한 카드 자신의 "When an effect trashes this card from the security stack" (v0.29.0)
        if (trashed && card != null)
            FireThisCardTrashed(zones, new[] { card }, EffectTrigger.ThisTrashedFromSecurity);
    }

    /// <summary>
    /// 이 카드가 시큐리티 · 덱 · 패에서 (직접) 파기되었을 때 — 트래시에 있는 그 카드 자신의 효과 (v0.29.0).
    /// 효과 처리 중이면 그 효과가 끝난 뒤에 (공식 Q&amp;A ST22-10: 같은 때 유발한 효과는 턴 플레이어부터)
    /// </summary>
    internal void FireThisCardTrashed(PlayerZones zones, IEnumerable<CardData> cards, EffectTrigger trigger)
    {
        var owner = CurrentEffectOwner;
        foreach (var card in cards.Distinct().ToList())
        {
            if (CardScripts.For(card)?.Effects.Any(e => e.Trigger == trigger) != true)
                continue;
            RunOrDefer(() =>
            {
                if (State.IsGameOver || !zones.Trash.Contains(card)) return;
                var outer = ThisTrashedByOwner;
                ThisTrashedByOwner = owner;
                try
                {
                    var ctx = RunEffects(trigger, zones, null, card, includeInherited: false, triggerCard: card);
                    if (ctx.PlaceInBattleArea && zones.Trash.Remove(card)) PlaceOption(zones, card);   // "place this card in the battle area" (옵션)
                }
                finally { ThisTrashedByOwner = outer; }
            });
        }
    }

    /// <summary>이 카드를 파기한 효과의 주인 ("When this card is trashed from your hand by one of your effects") — 효과 밖이면 null</summary>
    internal PlayerZones? ThisTrashedByOwner { get; private set; }

    /// <summary>zones의 시큐리티에 카드가 놓였다 (≪리커버리≫ 등) — "When a card is added to your security stack"</summary>
    internal void NotifySecurityAdded(PlayerZones zones, CardData? card = null)
    {
        FireForSide(EffectTrigger.AllySecurityAdded, zones, null, triggerCard: card);
        FireForSide(EffectTrigger.OpponentSecurityAdded, EnemyOf(zones), null, triggerCard: card);   // v0.33.0 "When a card is added to your opponent's security stack"
    }

    /// <summary>owner 쪽 디지몬(메인 · 진화원)과 테이머 전부에 trigger 시점을 알린다 (other = 그 일을 일으킨 디지몬)</summary>
    internal void FireForSide(EffectTrigger trigger, PlayerZones owner, CardInstance? other, CardData? triggerCard = null)
    {
        foreach (var d in owner.BattleArea.ToList())
            RunEffects(trigger, owner, d, d.TopCard, other: other, triggerCard: triggerCard);
        foreach (var tamer in owner.Tamers.ToList())
            RunEffects(trigger, owner, null, tamer, other: other, includeInherited: false, triggerCard: triggerCard);
        RunPlacedOptions(trigger, owner, other, triggerCard);   // ≪딜레이≫ 옵션의 "When ~, ＜Delay＞"
        RunTrashEffects(trigger, owner, other, triggerCard);    // v0.31.0 [트래시] 시점 효과 (트래시의 이 카드)
        RecalculateFieldBuffs();
        // v0.31.0 "When an opponent's Digimon digivolves" — 자신의 디지몬이 진화했을 때를 상대 쪽에도 알린다
        if (trigger == EffectTrigger.AllyDigimonDigivolved && !State.IsGameOver)
            FireForSide(EffectTrigger.OpponentDigimonDigivolved, EnemyOf(owner), other, triggerCard);
    }

    /// <summary>
    /// [트래시] 시점 효과 (v0.31.0): 트래시에 있는 이 카드의 "[Trash] [Your Turn] When ~, ~" — 같은 카드가 여러 장이면 1장만 (같은 효과).
    /// 처리하는 동안 이 카드가 트래시에 그대로 있어야 한다 (앞의 효과로 트래시를 떠났으면 발휘하지 않는다)
    /// </summary>
    internal void RunTrashEffects(EffectTrigger trigger, PlayerZones owner, CardInstance? other = null, CardData? triggerCard = null)
    {
        if (owner.Trash.Count == 0)
            return;
        var cards = owner.Trash.Where(c => CardScripts.For(c)?.Effects.Any(e => e.Source == EffectSource.Trash && e.Trigger == trigger) == true)
            .GroupBy(c => c.CardId).Select(g => g.First()).ToList();
        foreach (var card in cards)
        {
            if (State.IsGameOver || !owner.Trash.Contains(card)) continue;
            var ctx = new EffectContext(this, owner, null, other, null) { TriggerCard = triggerCard, Trigger = trigger };
            _effectDepth++;
            var outerEffectOwner = CurrentEffectOwner;
            CurrentEffectOwner = owner;
            try
            {
                foreach (var effect in CardScripts.For(card)!.Effects.Where(e => e.Source == EffectSource.Trash && e.Trigger == trigger).ToList())
                {
                    if (!owner.Trash.Contains(card)) break;
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
    }

    /// <summary>테이머를 레스트시킨다 (비용 "이 테이머를 레스트시켜") + "자신의 테이머가 레스트했을 때" 효과</summary>
    internal void SuspendTamer(PlayerZones owner, CardData tamer)
    {
        if (!owner.HasActiveTamer(tamer) || TamerSuspendLocked(owner, tamer))   // v0.35.0 "can't suspend until ~"
            return;
        owner.SuspendedTamers.Add(tamer);
        if (TamerProxyOf(owner, tamer) is { } proxy36) { proxy36.IsSuspended = true; SyncTamerProxies(); }   // v0.36.0 디지몬으로도 취급 중이면 그 디지몬도
        WithSuspendCause(() =>   // v0.32.0 "When one of your effects suspends a Tamer"
        {
            FireForSide(EffectTrigger.AllyTamerSuspended, owner, null, triggerCard: tamer);
            FireForSide(EffectTrigger.OpponentTamerSuspended, EnemyOf(owner), null, triggerCard: tamer);
            return true;
        });
    }

    /// <summary>
    /// v0.31.0 "activate 1 of this Digimon's [When Digivolving] effects": 디지몬의 맨 위 카드(와 얻은 진화원 카드)의 그 시점 효과 1개를 이 효과로 발휘한다
    /// (여럿이면 고른다). 효과가 없으면 false
    /// </summary>
    internal bool ActivateDigimonEffect(PlayerZones owner, CardInstance digimon, EffectTrigger trigger)
    {
        if (!owner.BattleArea.Contains(digimon))
            return false;
        var list = new List<(CardData Card, CardEffect Effect)>();
        if (CardScripts.For(digimon.TopCard) is { } top)
            list.AddRange(top.Effects.Where(e => e.Trigger == trigger && e.Source == EffectSource.Main).Select(e => (digimon.TopCard, e)));
        foreach (var gained in CardScripts.GainedSources(digimon))
            if (CardScripts.For(gained) is { } gs)
                list.AddRange(gs.Effects.Where(e => e.Trigger == trigger && e.Source == EffectSource.Main).Select(e => (gained, e)));
        if (list.Count == 0)
            return false;
        int pick = list.Count == 1 ? 0
            : Math.Clamp(ChooseOption(owner, $"[{digimon.TopCard.Name}] 발휘할 효과를 고르세요.", list.Select((x, i) => $"[{x.Card.Name}] 효과 {i + 1}").ToList(), 0), 0, list.Count - 1);
        var ctx = new EffectContext(this, owner, digimon, null, null) { Trigger = trigger };
        _effectDepth++;
        var outerEffectOwner = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try { Resolve(ctx, list[pick].Card, list[pick].Effect); }
        finally
        {
            _effectDepth--;
            CurrentEffectOwner = outerEffectOwner;
        }
        if (_effectDepth == 0)
            FlushPendingTriggers();
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>테이머를 소멸시킨다 (효과 · 비용) + "자신의 테이머가 소멸했을 때" (v0.31.0) — 디지몬 소멸처럼 효과 처리 중이면 끝난 뒤에</summary>
    internal void DeleteTamer(PlayerZones owner, CardData tamer, bool canPrevent = true)
    {
        // v0.33.0 "When any of your Digimon or Tamers would leave the battle area" — 비용으로 소멸시키는 것은 막을 수 없다
        if (TryPreventTamerLeave(owner, tamer, canPrevent, LeaveDestination.Deleted) && canPrevent)
            return;
        if (!owner.Tamers.Remove(tamer))
            return;
        owner.SuspendedTamers.Remove(tamer);
        owner.Trash.Add(tamer);
        ClearUnderTamersIfNoTamer(owner);
        RunOrDefer(() =>
        {
            if (State.IsGameOver) return;
            FireForSide(EffectTrigger.AllyTamerDeleted, owner, null, triggerCard: tamer);
            FireForSide(EffectTrigger.OpponentTamerDeleted, EnemyOf(owner), null, triggerCard: tamer);
        });
    }

    /// <summary>"When this Tamer becomes unsuspended" (v0.31.0) — 액티브가 된 테이머마다</summary>
    internal void NotifyTamersUnsuspended(PlayerZones owner, IEnumerable<CardData> tamers)
    {
        foreach (var t in tamers.Distinct().ToList())
            if (owner.Tamers.Contains(t) && !State.IsGameOver)
                FireForSide(EffectTrigger.AllyTamerUnsuspended, owner, null, triggerCard: t);
    }

    /// <summary>효과로 테이머 아래 카드를 파기한 뒤 (v0.31.0) — "When effects trash cards from under your Tamers / this Tamer"</summary>
    internal void NotifyUnderTamersTrashed(PlayerZones owner, IReadOnlyCollection<CardData> trashed)
    {
        if (trashed.Count == 0 || State.IsGameOver)
            return;
        RunOrDefer(() => FireForSide(EffectTrigger.UnderTamersTrashed, owner, null, triggerCard: trashed.First()));
    }

    /// <summary>효과로 디지몬의 링크 카드를 파기한 뒤 (v0.31.0) — "When effects trash any of this Digimon's / your Digimon's link cards"</summary>
    internal void NotifyLinkCardsTrashed(PlayerZones owner, CardInstance digimon, int count)
    {
        if (count == 0 || State.IsGameOver)
            return;
        RunOrDefer(() => FireForSide(EffectTrigger.AllyLinkCardsTrashed, owner, digimon));
    }

    /// <summary>상대 효과("Your opponent can't reduce digivolution costs")로 진화 코스트 감소가 막혀 있지 않은지</summary>
    internal bool CanReduceEvoCost(PlayerZones owner) =>
        !EnemyOf(owner).BattleArea.Any(d => CardScripts.HasKeyword(d, Keyword.BlockOpponentEvoCostReduction));

    /// <summary>
    /// ≪디지섭션 -N≫: 패에서 이 카드로 진화할 때 자신의 다른 액티브 디지몬 1마리를 레스트시켜 진화 코스트 -N.
    /// 추천(AI)은 그대로 내면 메모리가 상대 쪽으로 넘어갈 때만 쓴다. 반환: 줄인 코스트
    /// </summary>
    private int TryDigisorption(PlayerZones owner, CardInstance target, CardData evoCard, int baseCost, int memoryBefore)
    {
        int n = CardScripts.For(evoCard)?.DigisorptionReduction ?? 0;
        if (n <= 0 || baseCost <= 0 || !CanReduceEvoCost(owner))
            return 0;
        var pool = owner.BattleArea.Where(d => !ReferenceEquals(d, target) && !d.IsSuspended).OrderBy(d => d.GetCurrentDP()).ToList();
        if (pool.Count == 0)
            return 0;
        bool recommended = memoryBefore - baseCost < 0;
        var pick = AskDigimon(owner, $"≪디지섭션 -{n}≫ [{evoCard.Name}] 진화 코스트를 줄이려면 레스트시킬 디지몬을 고르세요.", pool, recommended ? pool[0] : null, optional: true);
        if (pick == null)
            return 0;
        Suspend(pick);
        int reduced = Math.Min(n, baseCost);   // 실제 진화 코스트(카드 효과 진화 포함)보다 많이 줄이지 않는다
        EffectLog(owner, $"[≪디지섭션 -{n}≫] [{pick.TopCard.Name}]을(를) 레스트시켜 [{evoCard.Name}] 진화 코스트 -{reduced}");
        return reduced;
    }

    /// <summary>
    /// 【카운터】 타이밍: 상대 디지몬이 어택했을 때 (어택 시 효과 뒤, 블록 타이밍 전) — 방어 측 패의 ≪블래스트 진화≫.
    /// 진화 조건을 만족하는 자신의 디지몬을 코스트 없이 진화시킨다 (진화 보너스 드로우 · [진화 시] 효과 포함). 어택 1번에 1장.
    /// AI · 테스트는 블록할 수 있는 액티브 디지몬 → DP가 높은 디지몬 순으로 추천해 쓴다.
    /// </summary>
    internal void CounterTiming(PlayerZones defender, CardInstance attacker)
    {
        if (!EnemyOf(defender).BattleArea.Contains(attacker))
            return;
        foreach (var card in defender.Hand.Where(c => CardScripts.For(c)?.BlastDigivolve == true).Distinct().OrderByDescending(c => c.Level).ThenByDescending(c => c.DP).ToList())
        {
            var bases = defender.BattleArea.Where(d => ActionValidator.ValidateEvolution(d, card).CanExecute)
                .OrderByDescending(d => d.HasBlocker && !d.IsSuspended).ThenByDescending(d => d.GetCurrentDP()).ToList();
            if (bases.Count == 0) continue;
            var target = AskDigimon(defender, $"【카운터】 ≪블래스트 진화≫ [{attacker.TopCard.Name}]의 어택: 패의 [{card.Name}](으)로 코스트 없이 진화시킬 디지몬을 고르세요 (고르지 않으면 쓰지 않음)",
                bases, bases[0], optional: true);
            if (target == null) continue;
            EffectLog(defender, $"【카운터】 ≪블래스트 진화≫ [{target.TopCard.Name}] → [{card.Name}]");
            EvolveByEffect(defender, target, card, 0);
            return;
        }
        // v0.31.0 배틀 에어리어 디지몬의 【카운터】 효과 (종합 룰 11-3: 어택받는 쪽이 어택 1번에 1개) — "[When Digivolving] [Counter] [Once Per Turn] ~"
        foreach (var d in defender.BattleArea.Where(HasCounterEffect).ToList())
        {
            if (State.IsGameOver || !EnemyOf(defender).BattleArea.Contains(attacker)) return;
            if (!defender.BattleArea.Contains(d)
                || !AskYesNo(defender, d, $"【카운터】 [{attacker.TopCard.Name}]의 어택: [{d.TopCard.Name}]의 【카운터】 효과를 발휘할까요?", recommended: true))
                continue;
            var ctx = RunEffects(EffectTrigger.Counter, defender, d, d.TopCard, other: attacker);
            RecalculateFieldBuffs();
            if (ctx.AnyEffectRan)
                return;   // 1번 발휘했으면 이 어택에서는 끝 (조건 · [턴에 1회]로 아무것도 안 했으면 다음 디지몬)
        }
    }

    /// <summary>배틀 에어리어 디지몬이 【카운터】 효과(맨 위 카드 · 진화원)를 가졌는지</summary>
    private static bool HasCounterEffect(CardInstance d) =>
        CardScripts.For(d.TopCard)?.Effects.Any(e => e.Trigger == EffectTrigger.Counter && e.Source == EffectSource.Main
                                                    && !(e.OncePerTurn && d.UsedOncePerTurn.Contains(d.TopCard.CardId))) == true
        || d.SourcesAny(s => CardScripts.For(s)?.Effects.Any(e => e.Trigger == EffectTrigger.Counter && e.Source == EffectSource.Inherited
                                                                      && !(e.OncePerTurn && d.UsedOncePerTurn.Contains(s.CardId + "#I"))) == true);

    /// <summary>≪디지버스트 N≫ 비용: 진화원 아래에서 N장 파기 → 그 카드들의 "디지버스트로 트래시되었을 때" 효과 → "디지버스트를 발휘했을 때"</summary>
    internal void PayDigiBurst(PlayerZones owner, CardInstance self, int n)
    {
        var removed = self.RemoveBottomSources(n);
        owner.Trash.AddRange(removed);
        RunEffects(EffectTrigger.DigiBurstTrashedThis, owner, self, null, sources: removed);
        FireForSide(EffectTrigger.AllyDigiBurst, owner, self);
    }

    /// <summary>"When this Digimon becomes unsuspended during your unsuspend phase" — 액티브 페이즈에 액티브가 된 배틀 에어리어 디지몬</summary>
    private void FireUnsuspendPhase(PlayerZones owner, List<CardInstance> woke)
    {
        foreach (var d in woke.Where(owner.BattleArea.Contains).ToList())
            RunEffects(EffectTrigger.ThisDigimonUnsuspendedInUnsuspendPhase, owner, d, d.TopCard);
        foreach (var d in woke.Where(owner.BattleArea.Contains).ToList())
            FireUnsuspended(owner, d);
        RecalculateFieldBuffs();
    }

    /// <summary>디지몬을 액티브로 (효과) + 자신의 메인 페이즈라면 "메인 페이즈에 액티브가 되었을 때" 효과</summary>
    internal void Unsuspend(CardInstance inst)
    {
        if (!inst.IsSuspended || EffectRules.IsUnsuspendLocked(State, inst))
            return;
        inst.IsSuspended = false;
        var owner = OwnerOf(inst);
        if (inst.TamerProxyOf != null) UnsuspendProxyTamer(owner, inst);   // v0.36.0
        bool mainPhase = ReferenceEquals(owner, State.Player) ? State.CurrentPhase == Phase.Main && _aiTurn == null : _aiTurn != null;
        if (owner.BattleArea.Contains(inst) && IsTurnOf(owner) && mainPhase)
            RunEffects(EffectTrigger.ThisDigimonUnsuspendedInMain, owner, inst, inst.TopCard);
        if (owner.BattleArea.Contains(inst))
            FireUnsuspended(owner, inst);
    }

    /// <summary>"When this Digimon becomes unsuspended" · "When a Digimon becomes unsuspended" — 액티브 페이즈 · 효과 모두 (Other = 액티브가 된 디지몬)</summary>
    private void FireUnsuspended(PlayerZones owner, CardInstance inst)
    {
        FireForSide(EffectTrigger.AllyDigimonUnsuspended, owner, inst);
        FireForSide(EffectTrigger.OpponentDigimonUnsuspended, EnemyOf(owner), inst);
    }

    /// <summary>[상대의 턴 종료 시] 효과 — owner = 턴을 마친 쪽의 상대</summary>
    private void TriggerEndOfOpponentTurn(PlayerZones owner)
    {
        foreach (var d in owner.BattleArea.ToList())
            RunEffects(EffectTrigger.EndOfOpponentsTurn, owner, d, d.TopCard);
        foreach (var tamer in owner.Tamers.ToList())
            RunEffects(EffectTrigger.EndOfOpponentsTurn, owner, null, tamer, includeInherited: false);
        RunPlacedOptions(EffectTrigger.EndOfOpponentsTurn, owner);
        RunTrashEffects(EffectTrigger.EndOfOpponentsTurn, owner);   // v0.31.0 [트래시] [~ 시] 효과
        RunScheduledDeletions();   // "~ is deleted at the end of the turn" (이 효과로 등장한 디지몬) — 턴 종료 시의 마지막
        ExpireTamerProxies();   // v0.36.0 테이머의 디지몬 취급 기간 끝
        RecalculateFieldBuffs();
    }

    /// <summary>[자신의 턴 종료 시] 효과 — 자신의 디지몬(메인 · 진화원)과 테이머</summary>
    private void TriggerEndOfTurn(PlayerZones owner)
    {
        foreach (var d in owner.BattleArea.ToList())
            RunEffects(EffectTrigger.EndOfYourTurn, owner, d, d.TopCard);
        foreach (var tamer in owner.Tamers.ToList())
            RunEffects(EffectTrigger.EndOfYourTurn, owner, null, tamer, includeInherited: false);
        RunPlacedOptions(EffectTrigger.EndOfYourTurn, owner);
        RunTrashEffects(EffectTrigger.EndOfYourTurn, owner);   // v0.31.0 [트래시] [~ 시] 효과
        RecalculateFieldBuffs();
    }

    /// <summary>트래시에서 조건에 맞는 디지몬을 코스트 없이 등장시킨다 ([등장 시] 효과는 발휘하지 않음)</summary>
    internal List<CardData> PlayFromTrash(PlayerZones zones, Func<CardData, bool> match, int maxCount)
    {
        var ranked = zones.Trash.Where(c => c.Type == CardType.Digimon && match(c) && CanPlayByEffect(zones, c, PlayOrigin.Trash))   // v0.36.0
            .OrderByDescending(c => c.Level).ThenByDescending(c => c.DP).ToList();
        var picks = ChooseCards(zones, CardChoiceZone.Trash, ranked, maxCount, upTo: false, $"트래시에서 등장시킬 디지몬 {maxCount}장을 고르세요.");
        foreach (var c in picks)
        {
            zones.Trash.Remove(c);
            zones.BattleArea.Add(new CardInstance(c, State.CurrentTurn) { IsOwnersTurn = ReferenceEquals(zones, State.Player) == _playerTurnActive });
        }
        return picks;
    }

    /// <summary>디지몬 중 효과 대상 고르기: UI가 넘겨준 대상이 조건에 맞으면 그것, 아니면 DP가 가장 높은 것</summary>
    internal static CardInstance? PickTarget(IEnumerable<CardInstance> candidates, CardInstance? preferred, Func<CardInstance, bool>? match = null)
    {
        var list = candidates.Where(c => match == null || match(c)).ToList();
        if (preferred != null && list.Contains(preferred))
            return preferred;
        return list.OrderByDescending(c => c.GetCurrentDP()).ThenByDescending(c => c.TopCard.Level).FirstOrDefault();
    }

    /// <summary>상대 디지몬 1마리(진화원이 있고 조건에 맞는 것 중 레벨이 가장 높은 것)의 진화원을 아래에서 1장 파기</summary>
    internal void TrashBottomSourceOfEnemy(PlayerZones enemyZones, Func<CardInstance, bool> match, string source)
    {
        var target = enemyZones.BattleArea.Where(d => d.EvolutionSources.Count > 0 && match(d))
            .OrderByDescending(d => d.TopCard.Level).FirstOrDefault();
        if (target == null) return;
        var removed = target.RemoveBottomSources(1);
        enemyZones.Trash.AddRange(removed);
        Log($"[진화원 효과] {source}: 상대 [{target.TopCard.Name}]의 진화원 [{removed[0].Name}] 파기");
    }

    /// <summary>카드 효과 로그 — AI 쪽 효과는 🤖를 붙여 구분한다 (효과 문장의 "상대"는 효과 주인 기준)</summary>
    internal void EffectLog(PlayerZones owner, string message) =>
        Log(ReferenceEquals(owner, State.DummyOpponent) ? "🤖 " + message : message);

    /// <summary>상대 턴 리플레이에 장면을 남긴다 (상대 턴 중 내 효과가 발휘됐을 때)</summary>
    internal void AddReplayEvent(string title, string detail) =>
        AddOpponentEvent(OpponentActionKind.TurnEnd, title, detail);

    // =====================================================================
    // 배틀 (블록 · 디지몬 어택) — ≪길동무≫ 포함
    // =====================================================================

    /// <summary>
    /// 두 디지몬의 배틀: DP가 낮은 쪽이 소멸, 같으면 둘 다 소멸. 혼자 진 쪽이 ≪길동무≫면 상대도 소멸.
    /// 반환하는 DP는 배틀 중에만 더하는 DP(뿔몬 진화원 등)까지 포함한 값.
    /// </summary>
    private (bool AttackerDeleted, bool DefenderDeleted, int AttackerDp, int DefenderDp) ResolveDigimonBattle(CardInstance attacker, CardInstance defender)
    {
        var atkZones = OwnerOf(attacker);
        var defZones = OwnerOf(defender);
        RecalculateFieldBuffs();
        int atkDp = attacker.GetCurrentDP() + BattleDpBonus(attacker, defender.EvolutionSources.Count > 0);
        int defDp = defender.GetCurrentDP() + BattleDpBonus(defender, attacker.EvolutionSources.Count > 0);

        // ≪빙장≫: 시큐리티 디지몬 이외와의 배틀에서는 DP가 아니라 진화원 매수를 비교한다
        bool iceclad = CardScripts.HasKeyword(attacker, Keyword.Iceclad) || CardScripts.HasKeyword(defender, Keyword.Iceclad);
        int atkValue = iceclad ? attacker.EvolutionSources.Count : atkDp;
        int defValue = iceclad ? defender.EvolutionSources.Count : defDp;
        if (iceclad)
            Log($"[≪빙장≫] 진화원 매수로 비교: [{attacker.TopCard.Name}] {atkValue}장 vs [{defender.TopCard.Name}] {defValue}장");
        bool attackerLoses = atkValue <= defValue;
        bool defenderLoses = atkValue >= defValue;

        // 진 쪽이 ≪방벽≫ · ≪회피≫로 소멸을 막는지 먼저 정한다 (막으면 ≪길동무≫도 발동하지 않는다)
        bool attackerDeleted = attackerLoses && !TryPreventDeletion(atkZones, attacker, DeletionCause.Battle) && !TryPreventLeave(atkZones, attacker, deletion: true, inBattle: true);
        bool defenderDeleted = defenderLoses && !TryPreventDeletion(defZones, defender, DeletionCause.Battle) && !TryPreventLeave(defZones, defender, deletion: true, inBattle: true);

        if (attackerDeleted && !defenderLoses && HasRetaliation(attacker))
        {
            Log($"[≪길동무≫] 배틀에서 진 [{attacker.TopCard.Name}]이(가) [{defender.TopCard.Name}]을(를) 함께 소멸시킵니다!");
            bool outer = _deletingByOpponentEffect;
            _deletingByOpponentEffect = true;   // ≪길동무≫는 상대 디지몬의 효과 (≪스케이프고트≫ 판단)
            defenderDeleted = !TryPreventDeletion(defZones, defender, DeletionCause.Effect);
            _deletingByOpponentEffect = outer;
        }
        else if (defenderDeleted && !attackerLoses && HasRetaliation(defender))
        {
            Log($"[≪길동무≫] 배틀에서 진 [{defender.TopCard.Name}]이(가) [{attacker.TopCard.Name}]을(를) 함께 소멸시킵니다!");
            bool outer = _deletingByOpponentEffect;
            _deletingByOpponentEffect = true;
            attackerDeleted = !TryPreventDeletion(atkZones, attacker, DeletionCause.Effect);
            _deletingByOpponentEffect = outer;
        }

        if (defenderDeleted) RemoveDeleted(defZones, defender);
        if (attackerDeleted) RemoveDeleted(atkZones, attacker);

        if (defenderDeleted && !attackerDeleted)
            RunEffects(EffectTrigger.DeletedOpponentInBattle, atkZones, attacker, null);
        // "When one of your Digimon deletes an opponent's Digimon in battle (and survives)" — 어택 · 블록 쪽 모두 (함께 소멸했으면 "and survives"가 아닌 효과만)
        if (defenderDeleted)
            FireForSide(EffectTrigger.AllyDeletedOpponentInBattle, atkZones, attacker, triggerCard: defender.TopCard);
        if (attackerDeleted)
            FireForSide(EffectTrigger.AllyDeletedOpponentInBattle, defZones, defender, triggerCard: attacker.TopCard);
        if (defenderDeleted && !attackerDeleted)
            RunEffects(EffectTrigger.WonBattle, atkZones, attacker, attacker.TopCard, other: defender);
        else if (attackerDeleted && !defenderDeleted)
            RunEffects(EffectTrigger.WonBattle, defZones, defender, defender.TopCard, other: attacker);

        return (attackerDeleted, defenderDeleted, atkDp, defDp);
    }

    // =====================================================================
    // 시점별 효과 처리
    // =====================================================================

    /// <summary>[등장 시] 효과 + 같은 명칭 등장에 반응하는 진화원 효과. 반환: 결과 요약</summary>
    internal string ApplyOnPlayEffects(PlayerZones owner, CardInstance instance)
    {
        var card = instance.TopCard;
        string summary;
        if (OnPlayBlocked37(owner, instance))   // v0.37.0 "none of your opponent's Digimon can activate [On Play] effects" (EX8-029)
        {
            EffectLog(owner, $"[{card.Name}]의 【등장 시】 효과는 상대의 효과로 발휘하지 않습니다.");
            summary = string.Empty;
        }
        else
            summary = RunEffects(EffectTrigger.OnPlay, owner, instance, card, includeInherited: false).Message;

        foreach (var other in owner.BattleArea.Where(d => !ReferenceEquals(d, instance)).ToList())
            RunEffects(EffectTrigger.AllySameNamePlayed, owner, other, null, other: instance);
        FireForSide(EffectTrigger.AllyDigimonPlayed, owner, instance);
        FireForSide(EffectTrigger.OpponentDigimonPlayed, EnemyOf(owner), instance);

        if (summary.Length > 0)
            EffectLog(owner, $"[효과] {card.Name} {summary}");
        RecalculateFieldBuffs();
        return summary;
    }

    /// <summary>[진화 시] 효과 (진화한 카드의 메인 효과)</summary>
    private void ApplyWhenDigivolvingEffects(PlayerZones owner, CardInstance target, CardData evoCard)
    {
        if (CardScripts.HasKeyword(target, Keyword.CannotActivateWhenDigivolving))   // v0.34.0 "can't activate [When Digivolving] effects"
        {
            EffectLog(owner, $"[{evoCard.Name}]의 【진화 시】 효과는 효과로 발휘하지 않습니다.");
            return;
        }
        RunEffects(EffectTrigger.WhenDigivolving, owner, target, evoCard, includeInherited: false);
        RecalculateFieldBuffs();
    }

    /// <summary>[어택 시] 효과 (자신 + 진화원). defender = 어택 대상 디지몬 (시큐리티 어택이면 null)</summary>
    /// <summary>지금 어택 중인 디지몬 (어택 선언부터 어택 종료까지)</summary>
    internal CardInstance? CurrentAttacker { get; private set; }

    /// <summary>≪프로그레스≫: 어택 중인 이 디지몬은 상대의 효과를 받지 않는다 (effectOwner = 효과 주인)</summary>
    internal bool IsUnaffectedBy(PlayerZones effectOwner, CardInstance target) =>
        (ReferenceEquals(CurrentAttacker, target) && !OwnerOf(target).Equals(effectOwner) && CardScripts.HasKeyword(target, Keyword.Progress))
        || UnaffectedByKeyword34(effectOwner, target);   // v0.34.0 "isn't affected by your opponent's (Digimon's) effects"

    private void TriggerWhenAttackingEffects(CardInstance attacker, CardInstance? defender)
    {
        CurrentAttacker = attacker;   // 어택 종료(EndAttack)까지 — ≪프로그레스≫ 판단
        _attackEndRequested = null;   // v0.34.0 "End the attack"
        if (CardScripts.HasKeyword(attacker, Keyword.CannotActivateWhenAttacking))   // v0.34.0 "can't activate [When Attacking] effects"
            EffectLog(OwnerOf(attacker), $"[{attacker.TopCard.Name}]의 【어택 시】 효과는 효과로 발휘하지 않습니다.");
        else
        RunEffects(EffectTrigger.WhenAttacking, OwnerOf(attacker), attacker, attacker.TopCard, other: defender);
        RecalculateFieldBuffs();
    }

    /// <summary>내 디지몬의 어택이 블록당했을 때 (메탈그레이몬 ST1-09 진화원 등)</summary>
    private void OnAttackBlocked(CardInstance attacker) =>
        RunEffects(EffectTrigger.AttackBlocked, OwnerOf(attacker), attacker, null);

    /// <summary>
    /// 옵션 [메인] 효과 + 사용 뒤 처리 (트래시, ≪딜레이≫ 옵션은 "place this card in your battle area"면 배틀 에어리어).
    /// target = UI가 고른 대상. 반환: 결과 메시지
    /// </summary>
    private string ApplyOptionEffect(PlayerZones owner, CardData optionCard, CardInstance? target)
    {
        // 옵션 효과를 모두 처리하고 그 결과를 남긴 뒤에, 처리 중에 일어난 [소멸 시] 등을 발휘한다
        EffectContext optionCtx;
        _effectDepth++;
        try { optionCtx = RunEffects(EffectTrigger.Main, owner, null, optionCard, chosenTarget: target, includeInherited: false, optionSide: optionCard.IsDual); }
        finally { _effectDepth--; }
        string msg = optionCtx.Message;
        if (msg.Length > 0)
            Log("[효과] " + msg);
        // 〈아츠 진화〉 (종합 룰 4-20): 사용 뒤 트래시하는 대신 자신의 카드를 이 듀얼 카드로 코스트 없이 진화 (치환 처리).
        // 효과 처리 중에 일어난 [소멸 시] 등은 그 뒤에 — 공식 Q&A BT25-043 (Q6313): 아츠 진화 뒤 룰 처리로 소멸하고, 그 [소멸 시]는 이 카드의 [진화 시]와 함께 유발
        LastOptionArtsDigivolved = !optionCtx.LinkedThisCard && optionCtx.ThisCardToSecurityTop == null && !optionCtx.PlaceInBattleArea && !optionCtx.ThisCardMoved
            && TryArtsDigivolve(owner, optionCard);
        if (_effectDepth == 0)
            FlushPendingTriggers();
        if (LastOptionArtsDigivolved) { }
        else if (optionCtx.LinkedThisCard) { }   // "link this card to ~" — 링크 카드가 되었다
        else if (optionCtx.ThisCardMoved) { }   // v0.37.0 "place this card as the bottom digivolution card of ~" — 진화원이 되었다
        else if (optionCtx.ThisCardToSecurityTop is bool secTop) PlaceSecurityFaceUp(owner, optionCard, secTop);   // "place this card face up as the bottom security card"
        else if (optionCtx.PlaceInBattleArea) PlaceOption(owner, optionCard);
        else owner.Trash.Add(optionCard);
        // "When you use an Option card" / "When a player uses an Option card"
        FireForSide(EffectTrigger.AllyOptionUsed, owner, null, triggerCard: optionCard);
        FireForSide(EffectTrigger.OpponentOptionUsed, EnemyOf(owner), null, triggerCard: optionCard);
        RecalculateFieldBuffs();
        return msg;
    }

    /// <summary>마지막으로 쓴 옵션이 〈아츠 진화〉로 진화했는지 (사용 로그 · 테스트용)</summary>
    internal bool LastOptionArtsDigivolved { get; private set; }

    /// <summary>
    /// 〈아츠 진화〉 (종합 룰 2-11 · 4-20, 공식 카드 리스트의 [DUAL Rule] — <see cref="DualCardRules"/>): 옵션 쪽으로 사용한 듀얼 카드를
    /// 트래시하는 대신, 에어리어(배틀 · 육성)의 자신의 디지몬을 그 카드로 코스트 없이 진화시킬 수 있다 (임의). 진화 조건은 보통 진화와 같다.
    /// 진화했으면 보통 진화처럼 1장 드로우 · [진화 시] (공식 Q&A Q6313: 아츠 진화로 진화하면 이 카드의 [진화 시]가 유발).
    /// </summary>
    private bool TryArtsDigivolve(PlayerZones owner, CardData dual)
    {
        if (!DualCardRules.HasArtsDigivolve(dual) || State.IsGameOver)
            return false;
        var bases = owner.BattleArea.ToList();
        if (owner.BreedingArea != null) bases.Add(owner.BreedingArea);
        var candidates = bases
            .Where(d => !EffectRules.IsDigivolveLocked(State, d) && ActionValidator.ValidateEvolution(d, dual).CanExecute)
            .OrderBy(d => owner.BattleArea.Contains(d) ? 0 : 1)
            .ThenByDescending(d => d.EvolutionSources.Count)
            .ThenByDescending(d => d.GetCurrentDP())
            .ToList();
        if (candidates.Count == 0)
            return false;
        if (!AskYesNo(owner, null, $"〈아츠 진화〉 [{dual.Name}]을(를) 트래시하는 대신, 자신의 디지몬을 이 카드로 코스트 없이 진화시킬까요?", recommended: true))
            return false;
        var target = AskDigimon(owner, $"〈아츠 진화〉 [{dual.Name}]으로 진화시킬 디지몬을 고르세요.", candidates, candidates[0], optional: true);
        if (target == null)
            return false;
        target.Evolve(dual);
        if (ReferenceEquals(owner, State.Player))
            State.TotalEvolutionsCount++;
        EffectLog(owner, $"[〈아츠 진화〉] [{dual.Name}(Lv.{dual.Level})]을(를) 트래시하는 대신 진화 (코스트 0)");
        DrawFor(owner);
        RecalculateFieldBuffs();
        if (owner.BattleArea.Contains(target))
        {
            ApplyWhenDigivolvingEffects(owner, target, dual);
            FireForSide(EffectTrigger.AllyDigimonDigivolved, owner, target);
        }
        return true;
    }

    /// <summary>[상대의 턴 종료 시] 이 턴에 상대가 어택하지 않았을 때 (owner = 턴을 마친 쪽의 상대)</summary>
    private void TriggerOpponentTurnEndWithoutAttack(PlayerZones owner)
    {
        foreach (var d in owner.BattleArea.ToList())
            RunEffects(EffectTrigger.OpponentTurnEndWithoutAttack, owner, d, null);
    }

    // =====================================================================
    // 옵션 [시큐리티] — 양쪽 공통 (owner = 시큐리티 주인, attacker = 어택 중인 상대 디지몬)
    // =====================================================================

    private record SecurityOptionOutcome(string Log, string Summary, bool AttackerRemoved, bool ReturnToHand);

    private SecurityOptionOutcome ResolveSecurityOption(CardData option, PlayerZones owner, CardInstance attacker)
    {
        string head = $"[{SideName(owner)} 시큐리티 {option.Name} 발동!]";
        bool hasEffect = CardScripts.For(option)?.Effects.Any(e => e.Trigger == EffectTrigger.Security) ?? false;
        if (!hasEffect)
            return new($"{head} [시큐리티] 효과가 없는 카드입니다.", $"[시큐리티 {option.Name}] 효과 없음", false, false);

        var ctx = RunEffects(EffectTrigger.Security, owner, null, option, other: attacker, chosenTarget: attacker, includeInherited: false);
        if (ctx.PlaceInBattleArea && owner.Trash.Remove(option))   // 시큐리티 체크 처리에서 이미 트래시로 갔다
            PlaceOption(owner, option);
        if (ctx.LinkedThisCard)   // "link this card to ~" — 링크 카드가 되었으니 트래시에서 뺀다 (안 빼면 링크 · 트래시 두 곳에 남는다)
            owner.Trash.Remove(option);
        return new($"{head} {ctx.Message}", $"[시큐리티 {option.Name}] {ctx.Summary}", ctx.AttackerRemoved, ctx.ReturnToHand);
    }

    /// <summary>
    /// 디지크로스 조건의 카드 이름 (공식 한글 문구 "디지크로스-2:「A」×「B」"에서 — 영어 카드 데이터에는 없다).
    /// 공식 데이터가 없으면 null (조건을 모름 → 디지몬 카드 전부를 조건 카드로 본다)
    /// </summary>
    private static List<string>? DigiXrosRequirementNames(CardData card)
    {
        var ko = KoreanCardData.Get(card.CardId);
        if (ko?.MainText is not { Length: > 0 } text) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"디지크로스\s*-?\s*\d*\s*[:：]\s*(?<names>(?:「[^」]+」\s*[×xX]?\s*)+)");
        if (!m.Success) return null;
        var names = System.Text.RegularExpressions.Regex.Matches(m.Groups["names"].Value, @"「([^」]+)」").Select(x => x.Groups[1].Value).ToList();
        return names.Count > 0 ? names : null;
    }

    /// <summary>
    /// 테이머가 배틀 에어리어를 떠난 뒤: 테이머 아래 카드는 어느 테이머 아래인지 구분하지 않으므로,
    /// 테이머가 한 장도 남지 않았을 때 전부 트래시로 보낸다 (테이머가 떠나면 그 아래 카드는 트래시)
    /// </summary>
    internal void ClearUnderTamersIfNoTamer(PlayerZones zones)
    {
        if (zones.Tamers.Count > 0 || zones.UnderTamers.Count == 0)
            return;
        zones.Trash.AddRange(zones.UnderTamers);
        EffectLog(zones, $"테이머가 없어져 테이머 아래 카드 {zones.UnderTamers.Count}장을 트래시로 보냅니다.");
        zones.UnderTamers.Clear();
        zones.FaceDownUnderTamers.Clear();
    }

    /// <summary>옵션 카드를 배틀 에어리어에 놓는다 (≪딜레이≫: 놓은 다음 턴부터 [메인]으로 파기해 효과를 발휘)</summary>
    /// <summary>
    /// 효과로 옵션을 코스트 없이 사용 ("You may use 1 Option card ~ in your hand without paying the cost") — 원래 있던 곳에서 이미 뺀 카드.
    /// [메인] 효과 → 사용 뒤 처리(트래시 · 링크 · 배틀 에어리어) → "옵션을 사용했을 때". 대상은 엔진이 고른다 (플레이어는 묻는다)
    /// </summary>
    internal void UseOptionByEffect(PlayerZones owner, CardData option)
    {
        EffectLog(owner, $"[효과] 옵션 [{option.Name}]을(를) 코스트 없이 사용");
        ApplyOptionEffect(owner, option, null);
    }

    internal void PlaceOption(PlayerZones owner, CardData option)
    {
        owner.OptionsInPlay.Add(new PlacedOption { Card = option, PlacedTurn = State.TurnSequence });
        EffectLog(owner, $"[{option.Name}]을(를) 배틀 에어리어에 놓았습니다.");
        RunOrDefer(() => { if (!State.IsGameOver) FireForSide(EffectTrigger.AllyOptionPlaced, owner, null, triggerCard: option); });   // "When any of your Option cards are placed in the battle area"
    }

    /// <summary>배틀 에어리어의 옵션을 트래시로 (≪딜레이≫ 비용) — 놓은 턴에는 쓸 수 없다. 성공하면 true</summary>
    internal bool TrashPlacedOption(PlayerZones owner, CardData option)
    {
        var placed = owner.OptionsInPlay.FirstOrDefault(p => ReferenceEquals(p.Card, option) && p.PlacedTurn < State.TurnSequence);
        if (placed == null) return false;
        TrashOptionFromBattleArea(owner, placed);
        return true;
    }

    /// <summary>
    /// 배틀 에어리어에 놓인 옵션 카드를 트래시로 (≪딜레이≫ · 비용 "by trashing 1 Option card in the battle area" · 효과) — v0.30.0:
    /// 그 카드의 "When this card is trashed from the battle area"와 "When Option cards in the battle area are trashed"를 효과가 끝난 뒤 발휘
    /// </summary>
    internal void TrashOptionFromBattleArea(PlayerZones owner, PlacedOption placed)
    {
        if (!owner.OptionsInPlay.Remove(placed)) return;
        var option = placed.Card;
        owner.Trash.Add(option);
        EffectLog(owner, $"[배틀 에어리어의 옵션] [{option.Name}]을(를) 파기했습니다.");
        FireThisCardTrashed(owner, new[] { option }, EffectTrigger.ThisTrashedFromBattleArea);
        RunOrDefer(() =>
        {
            if (State.IsGameOver) return;
            FireForSide(EffectTrigger.OptionTrashedFromBattleArea, owner, null, triggerCard: option);
            FireForSide(EffectTrigger.OptionTrashedFromBattleArea, EnemyOf(owner), null, triggerCard: option);
        });
    }

    /// <summary>≪딜레이≫를 쓸 수 있는 장수 (놓은 다음 턴 이후인 것)</summary>
    internal int ReadyPlacedOptions(PlayerZones owner, CardData option) =>
        owner.OptionsInPlay.Count(p => ReferenceEquals(p.Card, option) && p.PlacedTurn < State.TurnSequence);

    /// <summary>
    /// [자신의 턴 개시 시] 테이머 효과로 메모리를 보정한다 (플레이어 · AI 공통, 카드 스크립트의 StartOfTurnMemory).
    /// </summary>
    private static (int Memory, string? Note) ApplyStartOfTurnTamerMemory(int handedOver, IReadOnlyCollection<CardData> tamers, PlayerZones enemy)
    {
        int memory = handedOver;
        var notes = new List<string>();

        foreach (var group in tamers.GroupBy(t => t.CardId))
        {
            var effect = CardScripts.For(group.First())?.StartOfTurnMemory;
            if (effect == null || !effect.Condition(enemy))
                continue;
            int count = group.Count();
            int gain = effect.Amount * count;
            string name = group.First().Name;
            memory += gain;
            notes.Add($"{name} 효과 +{gain} ({effect.Reason})" + (count > 1 ? $" ({name} {count}장)" : ""));
        }

        return (memory, notes.Count > 0 ? string.Join(", ", notes) : null);
    }
}
