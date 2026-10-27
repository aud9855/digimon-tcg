using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.46.0 "~ would leave the battle area" 묶음:
///  - 떠나려던 디지몬이 이미 다른 디지몬이 되었을 때 (공식 Q&A EX9-020 Q1 "조그레스 진화는 새로운 디지몬으로서 진화하기 때문에 벗어나는 디지몬과는 다른 디지몬이 됩니다") —
///    조그레스 진화 재료가 되었거나 다른 디지몬의 진화원 아래로 옮겨졌으면 떠나는 처리를 하지 않는다
///  - [육성] 효과 "When any of your ~ would leave the battle area" (BT18-086 · BT22-007)
///  - "That Digimon and a card in the hand may DNA digivolve into ~" (BT17-095 · EX6-072)
/// </summary>
public partial class GoldfishEngine
{
    private readonly HashSet<CardInstance> _leaveEscaped46 = new(ReferenceEqualityComparer.Instance);

    /// <summary>마지막 TryPreventLeave가 "새 디지몬이 되어 떠나지 않음"으로 끝났는지</summary>
    internal bool LastLeaveEscaped46 { get; private set; }

    internal bool LeaveEscaped46(CardInstance inst) => _leaveEscaped46.Contains(inst);

    private bool TakeLeaveEscaped46(CardInstance inst)
    {
        LastLeaveEscaped46 = _leaveEscaped46.Remove(inst);
        return LastLeaveEscaped46;
    }

    /// <summary>떠나려는 디지몬이 다른 곳으로 옮겨졌다 (진화원 아래 — 떠나는 처리는 하지 않는다)</summary>
    internal void MarkLeaveEscaped46(CardInstance inst)
    {
        if (CurrentLeave is { IsTamer: false } lv && ReferenceEquals(lv.Digimon, inst)) _leaveEscaped46.Add(inst);
    }

    /// <summary>DNA 진화 재료에 떠나려던 디지몬이 있으면 기록</summary>
    private void NoteDnaEscape46(CardInstance left, CardInstance right)
    {
        if (CurrentLeave is not { IsTamer: false } lv) return;
        if (!ReferenceEquals(lv.Digimon, left) && !ReferenceEquals(lv.Digimon, right)) return;
        _leaveEscaped46.Add(lv.Digimon);
        Diag46.Hit("dnaEscapesLeave");
    }

    /// <summary>[육성] "When any of your ~ would leave the battle area" — 육성 에어리어의 디지몬 (맨 위 · 진화원)</summary>
    private bool RunBreedingWouldLeave46(PlayerZones owner, CardInstance inst, bool deletion)
    {
        if (owner.BreedingArea is not { } b || State.IsGameOver) return false;
        var triggers = deletion ? new[] { EffectTrigger.AllyDigimonWouldLeave, EffectTrigger.AllyDigimonWouldBeDeleted } : new[] { EffectTrigger.AllyDigimonWouldLeave };
        var list = new List<(CardData Card, CardEffect Effect)>();
        if (CardScripts.For(b.TopCard) is { } top)
            foreach (var e in top.Effects.Where(e => e.Source == EffectSource.Breeding42 && triggers.Contains(e.Trigger)))
                list.Add((b.TopCard, e));
        foreach (var src in b.FaceUpSources.GroupBy(x => x.CardId).Select(g => g.First()))
            if (CardScripts.For(src) is { } ss)
                foreach (var e in ss.Effects.Where(e => e.Source == EffectSource.BreedingInherited42 && triggers.Contains(e.Trigger)))
                    list.Add((src, e));
        if (list.Count == 0) return false;
        bool prevented = false;
        _effectDepth++;
        var outerEffectOwner = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try
        {
            foreach (var (card, effect) in list)
            {
                if (prevented || LeaveEscaped46(inst) || State.IsGameOver || !owner.BattleArea.Contains(inst)) break;
                if (!ReferenceEquals(owner.BreedingArea, b)) break;   // 육성 에어리어를 떠났으면 [육성] 효과는 없다
                var ctx = new EffectContext(this, owner, b, inst, null) { Trigger = effect.Trigger };
                Resolve(ctx, card, effect);
                if (ctx.PreventLeave) { prevented = true; Diag46.Hit("breedingWouldLeave"); }
            }
        }
        finally
        {
            _effectDepth--;
            CurrentEffectOwner = outerEffectOwner;
        }
        return prevented;
    }

    /// <summary>[트래시] "When one of your ~ would be deleted, by digivolving it into this card ~, prevent that deletion" (BT17-065 · BT17-067 · BT17-073) — 트래시의 이 카드</summary>
    private bool RunTrashWouldLeave46(PlayerZones owner, CardInstance inst, bool deletion)
    {
        if (owner.Trash.Count == 0 || State.IsGameOver) return false;
        var triggers = deletion ? new[] { EffectTrigger.AllyDigimonWouldLeave, EffectTrigger.AllyDigimonWouldBeDeleted } : new[] { EffectTrigger.AllyDigimonWouldLeave };
        var cards = owner.Trash.Where(c => CardScripts.For(c)?.Effects.Any(e => e.Source == EffectSource.Trash && triggers.Contains(e.Trigger)) == true)
            .GroupBy(c => c.CardId).Select(g => g.First()).ToList();
        bool prevented = false;
        foreach (var card in cards)
        {
            if (prevented || LeaveEscaped46(inst) || State.IsGameOver || !owner.BattleArea.Contains(inst)) break;
            if (!owner.Trash.Contains(card)) continue;
            var ctx = new EffectContext(this, owner, null, inst, null);
            _effectDepth++;
            var outerEffectOwner = CurrentEffectOwner;
            CurrentEffectOwner = owner;
            try
            {
                foreach (var effect in CardScripts.For(card)!.Effects.Where(e => e.Source == EffectSource.Trash && triggers.Contains(e.Trigger)).ToList())
                {
                    if (!owner.Trash.Contains(card) || ctx.PreventLeave) break;
                    ctx.Trigger = effect.Trigger;
                    Resolve(ctx, card, effect);
                }
            }
            finally
            {
                _effectDepth--;
                CurrentEffectOwner = outerEffectOwner;
            }
            if (ctx.PreventLeave) { prevented = true; Diag46.Hit("trashWouldLeave"); }
        }
        return prevented;
    }

    /// <summary>"place them as this Digimon's bottom digivolution cards" (BT22-007 [육성] 진화원 효과) — 떠나려는 디지몬의 카드 전부를 host의 진화원 맨 아래에</summary>
    internal bool PlaceDigimonUnder46(PlayerZones owner, CardInstance moving, CardInstance host)
    {
        if (ReferenceEquals(moving, host) || !owner.BattleArea.Contains(moving)) return false;
        if (!owner.BattleArea.Contains(host) && !ReferenceEquals(owner.BreedingArea, host)) return false;
        owner.BattleArea.Remove(moving);
        TrashLinks(owner, moving);
        if (ReferenceEquals(CurrentAttacker, moving)) CurrentAttacker = null;
        var stack = moving.GetEvolutionStackBottomUp().Append(moving.TopCard).ToList();   // 아래 → 위
        for (int i = stack.Count - 1; i >= 0; i--) host.AddBottomSource(stack[i]);
        MarkLeaveEscaped46(moving);
        RecalculateFieldBuffs();
        Diag46.Hit("placeLeavingUnder");
        return true;
    }

    /// <summary>
    /// "That Digimon and a card in the hand may DNA digivolve into ~ in the hand" (BT17-095 · EX6-072 — 공식 "그 디지몬과 패의 카드로, 패의 ~로 조그레스 진화할 수 있다"):
    /// 패의 카드 1장을 조그레스 조건의 한쪽으로 쓴다 (조그레스 코스트는 지불한다). 반환: 진화한 디지몬 (안 했으면 null)
    /// </summary>
    internal CardInstance? DnaWithHandCard46(PlayerZones owner, Func<CardInstance, bool> fieldOk, Func<CardData, bool> into, string label)
    {
        var options = new List<(CardData Dna, CardInstance Field, CardData HandCard)>();
        foreach (var x in owner.Hand.Where(x => x.Type == CardType.Digimon && into(x)).Distinct().ToList())
        {
            if (DnaOf(x) is not { } req) continue;
            if (ReferenceEquals(owner, State.Player) && req.Cost > Math.Max(0, MemoryOf(owner))) continue;
            foreach (var d in owner.BattleArea.Where(d => fieldOk(d) && !IsToken(d.TopCard) && !EffectRules.IsDigivolveLocked(State, d)))
                foreach (var h in owner.Hand.Where(h => !ReferenceEquals(h, x) && h.Type == CardType.Digimon).Distinct())
                    if (DnaPairOk(req, x, d, new CardInstance(h, State.CurrentTurn))) options.Add((x, d, h));
        }
        if (options.Count == 0 || !AskYesNo(owner, null, $"[{label}] 필드의 디지몬과 패의 카드로 조그레스 진화할까요? (후보: {string.Join(", ", options.Select(o => o.Dna.Name).Distinct())})", recommended: true))
            return null;
        var dnas = options.Select(o => o.Dna).Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
        int di = ChooseCardIndexes(owner, CardChoiceZone.Hand, dnas, 1, upTo: false, $"[{label}] 조그레스 진화할 카드를 고르세요.").FirstOrDefault();
        var dna = dnas[Math.Clamp(di, 0, dnas.Count - 1)];
        var mine = options.Where(o => ReferenceEquals(o.Dna, dna)).ToList();
        var fields = mine.Select(o => o.Field).Distinct().ToList();
        var field = fields.Count == 1 ? fields[0] : ChooseTargets(owner, fields, 1, $"[{dna.Name}]으로 조그레스 진화할 디지몬을 고르세요.", optional: false, preselected: fields[0]).FirstOrDefault() ?? fields[0];
        var hands = mine.Where(o => ReferenceEquals(o.Field, field)).Select(o => o.HandCard).Distinct().ToList();
        int hi = hands.Count == 1 ? 0 : ChooseCardIndexes(owner, CardChoiceZone.Hand, hands, 1, upTo: false, $"[{dna.Name}] 함께 쓸 패의 카드를 고르세요.").FirstOrDefault();
        var hand = hands[Math.Clamp(hi, 0, hands.Count - 1)];
        // 패의 카드를 디지몬처럼 두고 조그레스 진화 (진화원이 된다)
        owner.Hand.Remove(hand);
        var tmpInst = new CardInstance(hand, State.CurrentTurn) { IsOwnersTurn = IsTurnOf(owner) };
        owner.BattleArea.Add(tmpInst);
        string msg = DnaDigivolveCore(owner, dna, field, tmpInst, GetDnaCost(dna) ?? 0, owner.Hand);
        var result = owner.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TopCard, dna));
        if (result == null)
        {
            if (owner.BattleArea.Remove(tmpInst)) owner.Hand.Add(hand);   // 진화하지 못했으면 패의 카드를 되돌린다
            EffectLog(owner, $"[{label}] {msg}");
            return null;
        }
        Diag46.Hit("dnaWithHandCard");
        return result;
    }

    /// <summary>
    /// "That Digimon and any of your other Digimon may DNA digivolve into ~ in the hand" (EX12-003 "1 of them and any of your other Digimon" — 떠나려는 디지몬과 다른 자신의 디지몬)
    /// </summary>
    internal CardInstance? DnaWithThat46(PlayerZones owner, CardInstance that, Func<CardInstance, bool> otherOk, Func<CardData, bool> into, string label)
    {
        if (!owner.BattleArea.Contains(that)) return null;
        var options = owner.Hand.Where(x => x.Type == CardType.Digimon && into(x)).Distinct()
            .Select(x => (Card: x, Pairs: DnaPairs(owner, x).Where(p => ReferenceEquals(p.Left, that) && otherOk(p.Right) || ReferenceEquals(p.Right, that) && otherOk(p.Left)).ToList()))
            .Where(o => o.Pairs.Count > 0 && (!ReferenceEquals(owner, State.Player) || (GetDnaCost(o.Card) ?? 99) <= Math.Max(0, MemoryOf(owner))))
            .OrderByDescending(o => o.Card.Level).ThenByDescending(o => o.Card.DP).ToList();
        if (options.Count == 0 || !AskYesNo(owner, that, $"[{label}] [{that.TopCard.Name}]으로 조그레스 진화할까요? (후보: {string.Join(", ", options.Select(o => o.Card.Name).Distinct())})", recommended: true))
            return null;
        var cards = options.Select(o => o.Card).ToList();
        int ci = ChooseCardIndexes(owner, CardChoiceZone.Hand, cards, 1, upTo: false, $"[{label}] 조그레스 진화할 카드를 고르세요.").FirstOrDefault();
        var chosen = options[Math.Clamp(ci, 0, options.Count - 1)];
        var partners = chosen.Pairs.Select(p => ReferenceEquals(p.Left, that) ? p.Right : p.Left).Distinct().ToList();
        var partner = partners.Count == 1 ? partners[0]
            : ChooseTargets(owner, partners, 1, $"[{that.TopCard.Name}]과(와) 함께 조그레스 진화할 디지몬을 고르세요.", optional: false, preselected: partners[0]).FirstOrDefault() ?? partners[0];
        DnaDigivolveCore(owner, chosen.Card, that, partner, GetDnaCost(chosen.Card) ?? 0, owner.Hand);
        var result = owner.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TopCard, chosen.Card));
        if (result != null) Diag46.Hit("dnaWithThat");
        return result;
    }
}

public partial class GoldfishEngine
{
    /// <summary>"can't be deleted" 지속 효과 (StaticStat.CannotBeDeleted46) — 배틀 · 효과 · DP 0 룰 처리 모두</summary>
    internal bool CannotBeDeleted46(PlayerZones owner, CardInstance d)
    {
        foreach (var (aura, actx) in DigimonAuras(owner, EnemyOf(owner), StaticStat.CannotBeDeleted46).Concat(TamerAuras(owner, EnemyOf(owner), StaticStat.CannotBeDeleted46)))
            if (aura.Value(actx) != 0 && aura.AppliesTo(actx, d))
            {
                Diag46.Hit("cannotBeDeleted");
                EffectLog(owner, $"[{d.TopCard.Name}]은(는) 효과로 소멸하지 않습니다.");
                return true;
            }
        return false;
    }

    /// <summary>테이머 "can't be deleted" (StaticStat.TamerCannotBeDeleted46)</summary>
    internal bool TamerCannotBeDeleted46(PlayerZones owner, CardData tamer)
    {
        var probe = new CardInstance(tamer, State.CurrentTurn);
        foreach (var (aura, actx) in DigimonAuras(owner, EnemyOf(owner), StaticStat.TamerCannotBeDeleted46).Concat(TamerAuras(owner, EnemyOf(owner), StaticStat.TamerCannotBeDeleted46)))
            if (aura.Value(actx) != 0 && aura.AppliesTo(actx, probe))
            {
                Diag46.Hit("tamerCannotBeDeleted");
                EffectLog(owner, $"테이머 [{tamer.Name}]은(는) 효과로 소멸하지 않습니다.");
                return true;
            }
        return false;
    }
}
