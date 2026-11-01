using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.71.0 미지원 문장 45차 묶음 — 엔진 쪽</summary>
public partial class GoldfishEngine
{
    /// <summary>v0.71.0 조그레스 진화 횟수 · 마지막으로 조그레스 진화한 디지몬 — "the Digimon this effect DNA digivolved" (BT16-091 — EffectContext.DnaSerialAtStart71과 비교)</summary>
    internal int DnaSerial71 { get; private set; }
    internal CardInstance? LastDna71 { get; private set; }

    /// <summary>
    /// "DNA digivolve 1 of your [Machinedramon] in play and 1 [Kimeramon] in the trash into [Millenniummon] in the hand" (BT18-015 · BT18-073) ·
    /// "1 of your [NSo] trait Digimon and 1 [NSo] trait Digimon card in the trash may DNA digivolve into ~ in the hand" (EX11-059):
    /// 필드의 디지몬 1마리와 트래시의 디지몬 카드 1장을 조그레스 조건의 두 쪽으로 쓴다 (DnaWithHandCard46과 같은 방식 — 트래시의 카드를 디지몬처럼 두고 조그레스).
    /// 조그레스 조건은 만족해야 하고 조그레스가 없는 카드로는 할 수 없다 (공식 Q&amp;A BT18-015 Q1) · 조그레스 코스트는 지불한다 (지불하지 않는다고 적혀 있지 않다 — 종합 룰 5-3-3).
    /// 【소멸 시】의 이 카드도 트래시에 있으면 쓸 수 있다. 반환: 진화한 디지몬 (안 했으면 null)
    /// </summary>
    internal CardInstance? DnaWithZoneCard71(PlayerZones owner, Func<CardInstance, bool> fieldOk, Func<CardData, bool> partnerOk, Func<CardData, bool> into, string label)
    {
        var options = new List<(CardData Dna, CardInstance Field, CardData Partner)>();
        foreach (var x in owner.Hand.Where(x => x.Type == CardType.Digimon && into(x)).Distinct().ToList())
        {
            if (DnaOf(x) is not { } req) continue;
            if (ReferenceEquals(owner, State.Player) && req.Cost > Math.Max(0, MemoryOf(owner))) continue;
            foreach (var d in owner.BattleArea.Where(d => d.TamerProxyOf == null && fieldOk(d) && !IsToken(d.TopCard) && !EffectRules.IsDigivolveLocked(State, d)))
                foreach (var t in owner.Trash.Where(partnerOk).Distinct())
                    if (DnaPairOk(req, x, d, new CardInstance(t, State.CurrentTurn))) options.Add((x, d, t));
        }
        if (options.Count == 0 || !AskYesNo(owner, null, $"[{label}] 필드의 디지몬과 트래시의 카드로 조그레스 진화할까요? (후보: {string.Join(", ", options.Select(o => o.Dna.Name).Distinct())})", recommended: true))
            return null;
        var dnas = options.Select(o => o.Dna).Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
        int di = dnas.Count == 1 ? 0 : ChooseCardIndexes(owner, CardChoiceZone.Hand, dnas, 1, upTo: false, $"[{label}] 조그레스 진화할 카드를 고르세요.").FirstOrDefault();
        var dna = dnas[Math.Clamp(di, 0, dnas.Count - 1)];
        var mine = options.Where(o => ReferenceEquals(o.Dna, dna)).ToList();
        var fields = mine.Select(o => o.Field).Distinct().ToList();
        var field = fields.Count == 1 ? fields[0] : ChooseTargets(owner, fields, 1, $"[{dna.Name}]으로 조그레스 진화할 디지몬을 고르세요.", optional: false, preselected: fields[0]).FirstOrDefault() ?? fields[0];
        var partners = mine.Where(o => ReferenceEquals(o.Field, field)).Select(o => o.Partner).Distinct().ToList();
        int pi = partners.Count == 1 ? 0 : ChooseCardIndexes(owner, CardChoiceZone.Trash, partners, 1, upTo: false, $"[{dna.Name}] 함께 쓸 트래시의 카드를 고르세요.").FirstOrDefault();
        var partner = partners[Math.Clamp(pi, 0, partners.Count - 1)];
        // 트래시의 카드를 디지몬처럼 두고 조그레스 진화 (진화원이 된다)
        int at = owner.Trash.LastIndexOf(partner);
        if (at < 0) return null;
        owner.Trash.RemoveAt(at);
        var tmpInst = new CardInstance(partner, State.CurrentTurn) { IsOwnersTurn = IsTurnOf(owner) };
        owner.BattleArea.Add(tmpInst);
        string msg = DnaDigivolveCore(owner, dna, field, tmpInst, GetDnaCost(dna) ?? 0, owner.Hand);
        var result = owner.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TopCard, dna));
        if (result == null)
        {
            if (owner.BattleArea.Remove(tmpInst)) owner.Trash.Insert(Math.Min(at, owner.Trash.Count), partner);   // 진화하지 못했으면 트래시로 되돌린다
            EffectLog(owner, $"[{label}] {msg}");
            return null;
        }
        Diag46.Hit("dnaWithTrashCard71");
        return result;
    }
}
