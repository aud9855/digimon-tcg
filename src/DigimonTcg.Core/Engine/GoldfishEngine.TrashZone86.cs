using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.86.0 트래시 목록의 카드 한 장 — Position 0 = 맨 위 (가장 나중에 트래시에 간 카드)</summary>
/// <param name="HasTrashMain">트래시의 이 카드로 쓰는 [트래시]【메인】 효과가 있다</param>
/// <param name="HasTrashTiming">트래시에 있으면 그 시점에 발휘하는 [트래시] 시점 효과가 있다 (자동 — 조건이 맞으면 엔진이 묻는다)</param>
/// <param name="CanUse">[트래시]【메인】 효과를 지금 쓸 수 있다 (미리 볼 수 있는 것만 — 써 보면 대상이 없어 실패할 수도 있다)</param>
/// <param name="Reason">쓸 수 없는 이유 (CanUse = false이고 [트래시]【메인】 효과가 있을 때)</param>
public sealed record TrashCardInfo(CardData Card, int Position, bool HasTrashMain, bool HasTrashTiming, bool CanUse, string? Reason);

/// <summary>
/// v0.86.0 트래시 목록 (트래시 칸을 누르면 여는 창)과 [트래시]【메인】 효과를 쓸 수 있는지 미리 보기.
/// 트래시는 공개 정보라 양쪽 트래시를 모두 본다. [트래시] 효과는 트래시에 있는 이 카드로만 쓴다 (GoldfishEngine.ZoneAbilities · RunTrashEffects).
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>카드에 [트래시]【메인】 효과가 있는지</summary>
    public static bool HasTrashMainEffect(CardData card) =>
        CardScripts.For(card)?.Effects.Any(e => e.Trigger == EffectTrigger.TrashMain) == true;

    /// <summary>카드에 [트래시] 시점 효과(자동)가 있는지</summary>
    public static bool HasTrashTimingEffect(CardData card) =>
        CardScripts.For(card)?.Effects.Any(e => e.Source == EffectSource.Trash) == true;

    /// <summary>owner의 트래시 — 맨 위(가장 나중에 간 카드)부터</summary>
    public IReadOnlyList<TrashCardInfo> DescribeTrash(PlayerZones owner)
    {
        var list = new List<TrashCardInfo>(owner.Trash.Count);
        for (int i = owner.Trash.Count - 1, pos = 0; i >= 0; i--, pos++)
        {
            var card = owner.Trash[i];
            bool main = HasTrashMainEffect(card);
            var (ok, why) = main ? TrashMainStatus(owner, card) : (false, null);
            list.Add(new TrashCardInfo(card, pos, main, HasTrashTimingEffect(card), ok, why));
        }
        return list;
    }

    /// <summary>
    /// 트래시의 이 카드로 [트래시]【메인】 효과를 지금 쓸 수 있는지와 쓸 수 없는 이유 (효과를 처리하지 않고 본다):
    /// 자신의 메인 페이즈 → 트래시에 있음 → 이번 턴에 쓴 횟수 → 효과 앞머리의 발동 조건 "If ~," → 비용 "By ~,"
    /// </summary>
    public (bool CanUse, string? Reason) TrashMainStatus(PlayerZones owner, CardData card)
    {
        if (!HasTrashMainEffect(card))
            return (false, "트래시에서 쓰는 【메인】 효과가 없습니다.");
        if (!owner.Trash.Any(c => ReferenceEquals(c, card)))
            return (false, "트래시에 없는 카드입니다.");
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null || !IsTurnOf(owner))
            return (false, "자신의 메인 페이즈에만 쓸 수 있습니다.");
        if (_usedMainAbilities.GetValueOrDefault((MainAbilityZone.Trash, card)) >= owner.Trash.Count(c => ReferenceEquals(c, card)))
            return (false, "이번 턴에 이미 썼습니다.");
        var checks = SentenceParser.TrashMainChecks86(card);
        if (checks.Count == 0)
            return (true, null);
        var ctx = new EffectContext(this, owner, null, null, null) { Card = card, Trigger = EffectTrigger.TrashMain };
        var outer = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try
        {
            foreach (var check in checks)
            {
                bool ok;
                try { ok = check.Ok(ctx); }
                catch (Exception) { continue; }   // 미리 볼 수 없는 조건 — 써 볼 때 엔진이 본다
                if (ok) continue;
                if (check.IsCost)
                    return (false, KoreanEffectRenderer.CostKo86(check.Text) is { } k ? $"비용을 낼 수 없습니다 ({k})." : $"비용을 낼 수 없습니다 (by {check.Text}).");
                return (false, KoreanEffectRenderer.CondIfKo86(check.Text) is { } c ? $"조건이 맞지 않습니다 ({c})." : $"조건이 맞지 않습니다 (If {check.Text}).");
            }
        }
        finally { CurrentEffectOwner = outer; }
        return (true, null);
    }
}
