using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.79.0 미지원 문장 53차 묶음 — 엔진 쪽.
/// 효과 등장의 디지크로스 (모든 디지크로스 카드): 종합 룰 7-2-2-13 "효과로 디지크로스 조건을 가진 디지몬 카드를 등장시킬 때도, 디지크로스할 수 있습니다".
///  - 7-2-3-1 · 7-2-3-2: 등장할 때 유발한 효과를 해결한 뒤, 등장 코스트를 지불하기 직전에 디지크로스를 선언 — 패 및 배틀 에어리어에서 (효과가 허락하면 트래시 · 테이머 아래도)
///  - 7-2-3-3: "등장 코스트를 지불하는 경우, 증감된 후의 등장 코스트를 지불합니다" — 코스트를 지불하지 않는 등장에서도 디지크로스는 할 수 있다 (코스트 감소만 의미가 없다)
///  - 7-2-2-12: 디지크로스는 강제가 아니다 · 7-2-2-11: 등장할 수 없게 되면 아래의 카드는 파기
///  - 공식 Q&amp;A P-098 Q1 · BT11-076 Q1: 효과로 등장할 때 디지크로스했다면 "디지크로스했을 때" 효과 · 진화원 효과가 발휘된다
/// 전에는 BT10-104 (트래시의 카드만) · BT12-112 · BT21-030 (코스트를 내는 효과 등장)만 디지크로스했다.
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>끄면 v0.78.0까지처럼 (BT10-104 · BT12-112 · BT21-030만) — 시뮬레이션 비교용</summary>
    public static bool EffectPlayXros79 { get; set; } = true;

    /// <summary>효과 등장의 디지크로스 — Paying: 코스트를 내는 등장인지, Trash: 그 효과가 트래시의 카드도 허락하는지 (BT10-104)</summary>
    internal sealed record XrosEffectPlay79(bool Paying, bool Trash);

    /// <summary>
    /// 효과 등장의 디지크로스: 재료를 고르고 (등장할 때 효과 BT12-112 · BT21-030을 먼저) 코스트를 줄인다. 반환: 줄인 뒤의 코스트 (코스트 없이면 null) · 재료
    /// </summary>
    private (int? Cost, List<CardData> Xros) EffectPlayDigiXros79(PlayerZones owner, CardData card, int? payCost)
    {
        var xros = TakeXrosMaterials(owner, card, new XrosEffectPlay79(payCost != null, EffectXrosFromTrash57 > 0));
        int field = TakeXrosFieldCostReduce77();
        if (PendingUnderPlayed.Count > 0)   // BT12-112 · BT21-030 등장할 때 효과로 놓은 「샤우트몬」 (디지크로스 재료 아래)
        {
            _pendingUnderByEffect78 = PendingUnderPlayed.ToList();
            PendingUnderPlayed.Clear();
        }
        if (xros.Count > 0)
        {
            Diag46.Hit(payCost != null ? "xrosOnEffectPlayPaid79" : "xrosOnEffectPlayFree79");
            if (EffectXrosFromTrash57 > 0) Diag46.Hit("xrosFromTrashByEffect57");
        }
        if (payCost is int cost && !PlayCostReductionBlocked(owner, card))
            payCost = Math.Max(0, cost - XrosReduction(card, xros.Count) - field);
        return (payCost, xros);
    }

    /// <summary>
    /// 효과 등장의 디지크로스 후보 정리 · 확인 (TakeXrosMaterials 안 — 등장할 때 효과 · 테이머 효과 다음, 선언 직전 7-2-3-2). false면 디지크로스하지 않는다.
    /// AI: 코스트를 내면 보통 등장처럼 (패의 카드로 코스트를 줄인다). 코스트 없이면 패 · 필드의 카드는 "디지크로스했다면" 효과가 있을 때만 쓴다 (트래시 · 테이머 아래는 언제나).
    /// AI는 트래시 · 테이머 아래 → 패 → 필드 순으로 쓴다. 플레이어: 후보가 있으면 한 번 묻는다.
    /// </summary>
    private bool GateEffectPlayXros79(PlayerZones owner, CardData card, XrosRequirement req, List<XrosCandidate> pool, XrosEffectPlay79 ep, bool ask)
    {
        if (!ask)
        {
            if (!ep.Paying && !MentionsDigiXrosing79(card))
                pool.RemoveAll(p => p.Instance != null || ReferenceEquals(p.Zone, owner.Hand));
            var ordered = pool.OrderBy(p => p.Instance != null ? 2 : ReferenceEquals(p.Zone, owner.Hand) ? 1 : 0).ToList();
            pool.Clear();
            pool.AddRange(ordered);
        }
        bool any = req.Slots.Any(s => pool.Any(p => s.Match(p.Card) || !s.Distinct && IsXrosSubstitute77(owner, p.Instance)));
        if (!any) return false;
        if (ask && !AskYesNo(owner, null,
                $"효과로 등장하는 [{card.Name}]을(를) 디지크로스할까요? (종합 룰 7-2-2-13 — 조건의 카드를 이 카드 아래에 놓는다{(ep.Paying ? $", 1장마다 코스트 -{req.Reduction}" : ", 코스트 없이 등장하므로 코스트 감소는 없음")})",
                recommended: true))
            return false;
        Diag46.Hit("xrosEffectPlayOffered79");
        return true;
    }

    // ---- BT19-091 "gains ＜Alliance＞ twice for the turn" — 이 턴 동안 ≪연계≫ 1개 더 (어택했을 때 ≪연계≫를 한 번 더) ----

    private readonly Dictionary<CardInstance, int> _extraAlliance79 = new(ReferenceEqualityComparer.Instance);

    /// <summary>이 턴 동안 d의 ≪연계≫를 1개 더한다</summary>
    internal void AddExtraAlliance79(CardInstance d) => _extraAlliance79[d] = State.TurnSequence;

    /// <summary>어택했을 때 ≪연계≫를 처리할 횟수 (≪연계≫가 없으면 0)</summary>
    private int AllianceTimes79(CardInstance attacker) =>
        !CardScripts.HasKeyword(attacker, Keyword.Alliance) ? 0
        : 1 + (_extraAlliance79.TryGetValue(attacker, out int turn) && turn == State.TurnSequence ? 1 : 0);

    // ---- BT10-041 "Place the Option card used with this effect on top of your security stack face down instead of placing it in your trash" ----

    // 사용 뒤 트래시 대신 시큐리티 맨 위에 뒷면으로 놓을 옵션 (ApplyOptionEffect가 본다)
    private CardData? _optionToSecurity79;

    /// <summary>효과로 옵션을 코스트 없이 사용하고, 사용 뒤 트래시 대신 시큐리티 맨 위에 뒷면으로 (BT9-103 등으로 놓을 수 없으면 트래시 — 공식 Q&amp;A BT10-041 Q1)</summary>
    internal void UseOptionByEffectToSecurity79(PlayerZones owner, CardData option)
    {
        var outer = _optionToSecurity79;
        _optionToSecurity79 = option;
        try { UseOptionByEffect(owner, option); }
        finally { _optionToSecurity79 = outer; }
    }

    /// <summary>ApplyOptionEffect: 이 옵션을 트래시 대신 시큐리티 맨 위에 뒷면으로 놓았는지 (놓았으면 true)</summary>
    private bool PlaceUsedOptionOnSecurity79(PlayerZones owner, CardData option)
    {
        if (!ReferenceEquals(_optionToSecurity79, option) || SecurityAddBlocked65(owner)) return false;
        owner.SecurityStack.Insert(0, option);
        EffectLog(owner, $"[효과] 사용한 옵션 [{option.Name}]을(를) 트래시 대신 시큐리티 맨 위에 뒷면으로 놓았습니다.");
        NotifySecurityAdded(owner, option);
        return true;
    }

    /// <summary>owner에게 선택 창을 띄우는지 (플레이어 · 선택 창이 있을 때)</summary>
    internal bool IsAskingPlayer79(PlayerZones owner) => ShouldAskPlayer(owner);

    /// <summary>카드 효과에 "디지크로스했다면"이 있는지 (if DigiXrosing) — AI가 코스트 없는 등장에서도 패의 카드로 디지크로스할지</summary>
    private static bool MentionsDigiXrosing79(CardData card) =>
        $"{card.EffectText}\n{card.InheritedEffectText}".Contains("DigiXrosing", StringComparison.OrdinalIgnoreCase);
}
