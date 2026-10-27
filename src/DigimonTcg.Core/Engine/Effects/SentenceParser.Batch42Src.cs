using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.42.0 시큐리티 · 겹쳐진 카드 동작 — 원래 규칙이 읽지 못한 것만 (ParseAction42)</summary>
internal static partial class SentenceParser
{
    // ---- 시큐리티 ----

    private static (Action<EffectContext>? Act, string? Error)? SecurityAction42(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        Match m;
        // "Trash the top 2 cards of your security stack" · "Trash your top 2 security cards" (BT6-087 · BT6-088 — 공식 "자신의 시큐리티를 위에서부터 2장 파기한다")
        if ((m = Regex.Match(s, @"^Trash (?:the top (?<n>\d+) cards of your security stack|your top (?<n>\d+) security cards)$", I)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            if (n < 2) return null;
            return (c =>
            {
                int k = 0;
                while (k < n && c.Owner.SecurityStack.Count > 0)
                {
                    var x = c.Owner.SecurityStack[0];
                    c.Owner.SecurityStack.RemoveAt(0);
                    c.Owner.Trash.Add(x);
                    c.Engine.NotifySecurityRemoved(c.Owner, x, trashed: true);
                    k++;
                }
                if (k > 0) Diag42.Hit(ref Diag42.SecurityTrashN);
                c.PaidCount = k;
                if (k == 0) c.LastStepResolved = false;
                Emit(c, label, $"자신의 시큐리티 위에서 {k}장 파기");
            }, null);
        }
        // "Look at the top card of your security stack" (BT3-034 — 공식 "자신의 시큐리티의 내용을 위에서부터 1장 확인한다") — 보기만 한다 (뒤 문장이 그 카드를 쓴다)
        if (Regex.IsMatch(s, @"^Look at the top card of your security stack$", I))
            return (c =>
            {
                if (c.Owner.SecurityStack.Count == 0) { c.LastStepResolved = false; return; }
                Diag42.Hit(ref Diag42.SecurityLook);
                Emit(c, label, "자신의 시큐리티 맨 위 카드를 확인했습니다");
            }, null);
        // [시큐리티] "Return this Digimon to your hand at the end of the turn" (EX4-013 — 공식 "이 턴 종료 시, 이 디지몬을 패로 되돌린다") · 디지몬 효과면 이 디지몬
        if ((m = Regex.Match(s, @"^(?:Return (?<t>this Digimon|it|that Digimon) to (?:your|its owner's|the) hand at the end of (?:the|this) turn|At the end of (?:the|this) turn, return (?<t>this Digimon|it|that Digimon) to (?:your|its owner's|the) hand)$", I)).Success)
        {
            bool self = m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
            bool security = triggers.Contains(EffectTrigger.Security);
            return (c =>
            {
                if (security && self && c.Self == null) { c.ReturnPlayedAtTurnEnd42 = true; Emit(c, label, "이 턴 종료 시 이 디지몬을 패로"); return; }
                var list = (self ? (c.Self != null ? new List<CardInstance> { c.Self } : new()) : c.LastTargets.ToList()).Where(c.Owner.BattleArea.Contains).ToList();
                if (list.Count == 0) { c.LastStepResolved = false; return; }
                foreach (var x in list) c.Engine.ScheduleReturnAtTurnEnd42(x, c.Owner, c.State.TurnSequence, toHand: true);
                Emit(c, label, $"[{string.Join(", ", list.Select(x => x.TopCard.Name))}] 턴 종료 시 패로");
            }, null);
        }
        // "At the end of the turn, return the Digimon that digivolved with this effect to the bottom of its owner's deck" (BT5-109 — 공식 "이 턴 종료 시, 이 효과로 진화한 디지몬을 덱 아래로 되돌린다")
        //  — 이 턴 동안의 "다음 진화" 감소로 진화한 디지몬 (엔진: TurnEvoCostModifier41이 진화한 디지몬을 기억한다)
        return null;
    }

    // ---- 육성 에어리어에서 이동 ----

    /// <summary>"[Breeding] [When Digivolving] This Digimon may move." (EX10-013) — 육성 에어리어의 이 디지몬을 배틀 에어리어로 이동할 수 있다</summary>
    private static (Action<EffectContext>? Act, string? Error)? MoveAction42(string s, string label)
    {
        var m = Regex.Match(s, @"^This Digimon (?<may>may )?moves?(?: to (?:your|the) battle area)?$", I);
        if (!m.Success) return null;
        bool may = m.Groups["may"].Success;
        return (c =>
        {
            var b = c.Owner.BreedingArea;
            bool ok = b != null && ReferenceEquals(b, c.Self) && b.TopCard.Type == CardType.Digimon && b.TopCard.Level >= 3
                      && (!may || c.Engine.AskYesNo(c.Owner, b, $"[{b.TopCard.Name}]을(를) 배틀 에어리어로 이동할까요?", recommended: true));
            var moved = ok ? c.Engine.MoveBreedingToBattle(c.Owner) : null;
            if (moved == null) { c.LastStepResolved = false; Emit(c, label, "이동하지 않았습니다"); return; }
            c.LastTargets = new List<CardInstance> { moved };
            Emit(c, label, $"[{moved.TopCard.Name}] 육성 에어리어 → 배틀 에어리어");
        }, null);
    }

    // ---- 겹쳐진 카드 ----

    private static (Action<EffectContext>? Act, string? Error)? StackAction42(string s, CardData card, List<EffectTrigger> triggers, string label)
    {
        Match m;
        // "Trash the top 10 stacked cards of 1 of your opponent's Digimon" (BT21-030 — 공식 "상대의 디지몬 1마리에 겹쳐져 있는 카드를 위에서부터 10장 파기한다")
        if ((m = Regex.Match(s, @"^Trash the top (?<n>\d+) stacked cards of (?<t>.+)$", I)).Success && TargetSpec.Parse(m.Groups["t"].Value) is { } target)
        {
            int n = int.Parse(m.Groups["n"].Value);
            bool security = triggers.Contains(EffectTrigger.Security);
            return (c =>
            {
                var parts = new List<string>();
                foreach (var d in Pick(target, c, security).ToList())
                {
                    var zone = c.Engine.OwnerOf(d);
                    string name = d.TopCard.Name;
                    int k = c.Engine.TrashTopStackedCards42(zone, d, n);
                    if (k > 0) { Diag42.Hit(ref Diag42.StackTrash); parts.Add($"[{name}] {k}장"); }
                }
                if (parts.Count == 0) c.LastStepResolved = false;
                Emit(c, label, parts.Count > 0 ? $"겹쳐진 카드 파기: {string.Join(", ", parts)}" : "파기할 카드가 없습니다");
            }, null);
        }
        return null;
    }

    // ---- 셈 (SentenceParser.Batch42Count) ----
    private static (Action<EffectContext>? Act, string? Error)? CountAction42(string s, CardData card, List<EffectTrigger> triggers, string label) => CountAction42Impl(s, card, triggers, label);

    private static bool BreedingClause42(string rawClause, bool inherited, CardData card, CardScriptDefinition? def) => BreedingClause42Impl(rawClause, inherited, card, def);

    private static bool DeletedCostClause42(string rawClause, bool inherited, CardData card, CardScriptDefinition? def) => DeletedCostClause42Impl(rawClause, inherited, card, def);
}
