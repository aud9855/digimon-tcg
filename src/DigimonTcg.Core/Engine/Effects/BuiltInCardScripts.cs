using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// 내장 카드(StarterDecks 120장)의 효과 스크립트 — 공식 한글판 카드 텍스트 기준.
/// 대상 선택이 필요한 효과는 UI가 넘겨준 대상(ChosenTarget)을 쓰고, 없으면 가장 유리한 대상을 자동으로 고른다.
/// </summary>
internal static class BuiltInCardScripts
{
    public static void Register(CardScriptBuilder b)
    {
        RegisterSt1(b);
        RegisterSt2(b);
        RegisterSt3(b);
        RegisterSt4(b);
        RegisterSt5(b);
        RegisterSt6(b);
        RegisterBt1(b);
        RegisterBt2(b);
        RegisterBt3To5(b);
    }

    // =====================================================================
    // 자주 쓰는 효과
    // =====================================================================

    /// <summary>≪블로커≫ 디지몬의 [어택 시] 메모리 -2</summary>
    private static void LoseTwoMemoryWhenAttacking(CardScriptDefinition card) =>
        card.WhenAttacking(c =>
        {
            c.AddMemory(-2);
            c.Log($"[어택 시] {c.Card.Name}: 메모리 -2 (현재 메모리: {c.OwnerMemory})");
        });

    /// <summary>[어택 시] 메모리 +3, 이 턴 종료 시 -3</summary>
    private static void GainThreeMemoryUntilEndOfTurn(CardScriptDefinition card) =>
        card.WhenAttacking(c =>
        {
            c.AddMemory(3);
            c.LoseMemoryAtEndOfTurn(3);
            c.Log($"[어택 시] {c.Card.Name}: 메모리 +3 (이 턴 종료 시 -3) → 현재 메모리: {c.OwnerMemory}");
        });

    /// <summary>이 턴 동안 자신의 디지몬 1마리 DP +3000 (섀도 윙 · 천국의 문 [메인])</summary>
    private static void AllyDpPlus3000(EffectContext c)
    {
        var ally = AllyTarget(c) ?? c.Owner.BattleArea.OrderByDescending(d => d.GetCurrentDP()).FirstOrDefault();
        if (ally == null) return;
        ally.TemporaryDpBonus += 3000;
        ally.ActiveSkillBuffLabels.Add($"✨{c.Card.Name} (DP +3,000)");
        c.Message = $"[{ally.TopCard.Name}]의 DP가 이번 턴 +3000 상승했습니다! (현재 DP: {ally.GetCurrentDP()})";
    }

    /// <summary>[진화원] 상대 디지몬에 어택했을 때 이 턴 DP +N</summary>
    private static void DpWhenAttackingDigimon(CardScriptDefinition card, string name, int dp) =>
        card.Inherited(EffectTrigger.WhenAttacking, c =>
        {
            c.Self!.TemporaryDpBonus += dp;
            c.Log($"[진화원 효과] {name}: 상대 디지몬에 어택 → 이 턴 DP +{dp}");
        }, when: c => c.Other != null);

    /// <summary>[진화원] ≪1 드로우≫ 후 패 1장 파기 (퍼플)</summary>
    private static void DrawThenDiscardWhenAttacking(CardScriptDefinition card) =>
        card.Inherited(EffectTrigger.WhenAttacking, c =>
        {
            var drawn = c.Draw();
            var ranked = c.Owner.Hand.Where(h => h != drawn).OrderBy(h => h.Type == CardType.Digimon ? 0 : 1).ThenBy(h => h.Level).ToList();
            if (drawn != null) ranked.Add(drawn);   // 방금 뽑은 카드는 마지막 추천
            var discard = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, ranked, 1, upTo: false, "≪1 드로우≫ 후 버릴 패 1장을 고르세요.").FirstOrDefault();
            if (discard != null)
            {
                c.Owner.Hand.Remove(discard);
                c.Owner.Trash.Add(discard);
            }
            c.Log($"[진화원 효과] ≪1 드로우≫ {(drawn != null ? $"[{drawn.Name}]" : "(덱 없음)")} 후 패의 [{discard?.Name}] 파기");
        });

    /// <summary>[상대의 턴 종료 시] 이 턴에 상대가 어택하지 않았다면 ≪1 드로우≫ (진화원)</summary>
    private static void DrawIfOpponentDidNotAttack(CardScriptDefinition card) =>
        card.Inherited(EffectTrigger.OpponentTurnEndWithoutAttack, c =>
        {
            var drawn = c.Draw();
            if (drawn == null) return;
            c.Log($"[진화원 효과] 상대가 어택하지 않아 ≪1 드로우≫: [{drawn.Name}]");
            c.Engine.AddReplayEvent("내 진화원 효과", $"상대가 어택하지 않아 1장 드로우 ({c.Self!.TopCard.Name})");
        });

    /// <summary>다음 상대의 턴 종료 시까지 어택 · 블록 불가</summary>
    private static void LockAttackAndBlock(CardInstance target, int until)
    {
        target.CannotAttackUntil = Math.Max(target.CannotAttackUntil, until);
        target.CannotBlockUntil = Math.Max(target.CannotBlockUntil, until);
    }

    /// <summary>UI가 고른 대상이 상대 디지몬이면 그것</summary>
    private static CardInstance? EnemyTarget(EffectContext c) =>
        c.ChosenTarget != null && c.Enemy.BattleArea.Contains(c.ChosenTarget) ? c.ChosenTarget : null;

    private static CardInstance? AllyTarget(EffectContext c) =>
        c.ChosenTarget != null && c.Owner.BattleArea.Contains(c.ChosenTarget) ? c.ChosenTarget : null;

    private static bool IsDigimon(CardData c) => c.Type == CardType.Digimon;

    // =====================================================================
    // ST-1 가이아 레드
    // =====================================================================

    private static void RegisterSt1(CardScriptBuilder b)
    {
        // 코로몬: [자신의 턴] 진화원 4장 이상이면 DP +1000
        b.Card("ST1-01").InheritedDpOnOwnTurn(1000, c => c.Digimon!.EvolutionSources.Count >= 4);

        LoseTwoMemoryWhenAttacking(b.Card("ST1-06"));   // 코어드라몬

        // 가루다몬: [진화 시] 이 턴 동안 자신의 디지몬 1마리 DP +3000
        b.Card("ST1-08").WhenDigivolving(c =>
        {
            c.Self!.TemporaryDpBonus += 3000;
            c.Self.ActiveSkillBuffLabels.Add("🔥가루다몬 [진화 시] (DP +3,000)");
            c.Log($"[진화 시] 가루다몬: 이 턴 동안 DP +3000 (현재 DP: {c.Self.GetCurrentDP()})");
        });

        // 메탈그레이몬: [진화원] 이 디지몬의 어택이 블록당했을 때 메모리 +3
        b.Card("ST1-09").Inherited(EffectTrigger.AttackBlocked, c =>
        {
            c.AddMemory(3);
            c.Log($"[진화원 효과] 메탈그레이몬: 블록당했을 때 메모리 +3 (현재 메모리: {c.OwnerMemory})");
        });

        // 워그레이몬: [자신의 턴] 진화원 2장마다 ≪시큐리티 어택 +1≫
        b.Card("ST1-11").Static(StaticStat.SecurityAttack, EffectSource.Main, c => c.OwnersTurn ? c.Digimon!.EvolutionSources.Count / 2 : 0);

        // 신태일: [자신의 턴] 자신의 디지몬 전부 DP +1000
        b.Card("ST1-12").Static(StaticStat.AllyDigimonDp, EffectSource.Main, c => c.OwnersTurn ? 1000 : 0);

        // 섀도 윙
        b.Card("ST1-13")
            .Main(TargetKind.OwnDigimon, AllyDpPlus3000)
            .Security(c =>   // 다음 자신의 턴 종료 시까지 자신의 디지몬 전부 ≪시큐리티 어택 +1≫
            {
                c.Engine.SetAllSecurityAttackUntil(c.Owner, c.TurnSequence + 1);
                c.SecurityResult("다음 자신의 턴 종료 시까지 디지몬 전부 ≪시큐리티 어택 +1≫", "시큐리티 어택 +1 부여");
            });

        // 스타라이트 익스플로전
        b.Card("ST1-14")
            .Main(TargetKind.None, c =>   // 다음 상대 턴 종료 시까지 자신의 시큐리티 디지몬 전부 DP +7000
            {
                c.Engine.SecurityDpBuffsOf(c.Owner).Add((7000, c.TurnSequence + 1));
                c.Message = "다음 상대 턴 종료 시까지 내 시큐리티 디지몬 전부 DP +7000!";
            })
            .Security(c =>   // 이 턴 동안 자신의 시큐리티 디지몬 전부 DP +7000
            {
                c.Engine.SecurityDpBuffsOf(c.Owner).Add((7000, c.TurnSequence));
                c.SecurityResult("이 턴 동안 시큐리티 디지몬 전부 DP +7000", "시큐리티 디지몬 DP +7000");
            });

        // 기가 디스트로이어: DP 4000 이하 상대 디지몬 2마리까지 소멸
        b.Card("ST1-15")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var chosen = EnemyTarget(c);
                var victims = c.Enemy.BattleArea.Where(d => d.GetCurrentDP() <= 4000)
                    .OrderByDescending(d => ReferenceEquals(d, chosen)).ThenByDescending(d => d.GetCurrentDP()).Take(2).ToList();
                foreach (var v in victims) c.DeleteEnemy(v);
                c.Message = victims.Count > 0
                    ? $"💥 기가 디스트로이어 발동! 상대 [{string.Join(", ", victims.Select(v => v.TopCard.Name))}] 소멸!"
                    : "DP 4000 이하인 상대 디지몬이 없어 효과가 없습니다.";
            })
            .Security(c =>
            {
                var victims = c.Enemy.BattleArea.Where(d => d.GetCurrentDP() <= 4000)
                    .OrderByDescending(d => ReferenceEquals(d, c.Other)).Take(2).ToList();
                foreach (var v in victims) c.DeleteEnemy(v);
                c.SecurityResult($"DP 4000 이하 디지몬 {victims.Count}마리 소멸", $"{victims.Count}마리 소멸", victims.Contains(c.Other!));
            });

        // 가이아 포스: 상대 디지몬 1마리 소멸
        b.Card("ST1-16")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var enemy = c.PickEnemy(preferred: EnemyTarget(c));
                if (enemy == null) return;
                c.DeleteEnemy(enemy);
                c.Message = $"🔥 가이아 포스 발동! 상대 [{enemy.TopCard.Name}] 소멸!";
            })
            .Security(c =>   // 어택 중인 디지몬
            {
                c.DeleteEnemy(c.Other!);
                c.SecurityResult($"어택하던 [{c.Other!.TopCard.Name}] 소멸!", $"[{c.Other.TopCard.Name}] 소멸", true);
            });
    }

    // =====================================================================
    // ST-2 코큐토스 블루
    // =====================================================================

    private static void RegisterSt2(CardScriptBuilder b)
    {
        // 뿔몬: [진화원] [자신의 턴] 진화원을 갖지 않은 상대의 디지몬과 배틀하고 있는 동안 DP +1000
        b.Card("ST2-01").Static(StaticStat.DpInBattle, EffectSource.Inherited, c => c.OwnersTurn && !c.BattleOpponentHasSources ? 1000 : 0);

        // 파피몬: [진화원] [어택 시] Lv.5 이하 상대 디지몬 1마리의 진화원 아래에서 1장 파기
        b.Card("ST2-03").Inherited(EffectTrigger.WhenAttacking, c => c.TrashBottomSourceOfEnemy(d => d.TopCard.Level <= 5, "파피몬"));
        // 가루몬: [진화원] [어택 시] 상대 디지몬 1마리의 진화원 아래에서 1장 파기
        b.Card("ST2-06").Inherited(EffectTrigger.WhenAttacking, c => c.TrashBottomSourceOfEnemy(_ => true, "가루몬"));

        LoseTwoMemoryWhenAttacking(b.Card("ST2-07"));   // 그리즈몬

        // 워가루몬: [진화원] [자신의 턴] 진화원을 갖지 않은 상대 디지몬이 있으면 ≪시큐리티 어택 +1≫
        b.Card("ST2-08").Static(StaticStat.SecurityAttack, EffectSource.Inherited,
            c => c.OwnersTurn && c.Enemy.BattleArea.Any(d => d.EvolutionSources.Count == 0) ? 1 : 0);

        // 쥬드몬: [진화 시] 상대 디지몬 1마리의 진화원을 아래에서 2장 파기
        b.Card("ST2-09").WhenDigivolving(c =>
        {
            var enemy = c.Enemy.BattleArea.Where(d => d.EvolutionSources.Count > 0).OrderByDescending(d => d.TopCard.Level).FirstOrDefault();
            if (enemy == null) return;
            var removed = enemy.RemoveBottomSources(2);
            c.Enemy.Trash.AddRange(removed);
            c.Log($"[진화 시] 쥬드몬: 상대 [{enemy.TopCard.Name}]의 진화원 {removed.Count}장 파기");
        });

        // 메탈가루몬: [어택 시] [턴에 1회] 이 디지몬을 액티브로
        b.Card("ST2-11").WhenAttacking(c =>
        {
            c.Engine.Unsuspend(c.Self!);
            c.Log("❄️ [어택 시] 메탈가루몬: 액티브 상태로 전환 — 이번 턴 한 번 더 어택할 수 있습니다!");
        }, oncePerTurn: true);

        // 매튜: [자신의 턴 개시 시] 진화원을 갖지 않은 상대 디지몬이 있으면 메모리 +1
        b.Card("ST2-12").StartOfTurnMemory(1, enemy => enemy.BattleArea.Any(d => d.EvolutionSources.Count == 0), "진화원 없는 상대 디지몬 있음");

        // 해머 스파크
        b.Card("ST2-13")
            .Main(TargetKind.None, c =>
            {
                c.AddMemory(1);
                c.Message = $"⚡ 해머 스파크: 메모리 +1 (현재 메모리: {c.OwnerMemory})";
            })
            .Security(c =>
            {
                c.AddMemory(2);
                c.SecurityResult("메모리 +2", "메모리 +2");
            });

        // 소로 블루: 진화원 없는 상대 디지몬 1마리, 어택 · 블록 불가
        b.Card("ST2-14")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var enemy = c.PickEnemy(d => d.EvolutionSources.Count == 0, EnemyTarget(c));
                if (enemy == null)
                {
                    c.Message = "진화원을 갖지 않은 상대 디지몬이 없어 효과가 없습니다.";
                    return;
                }
                LockAttackAndBlock(enemy, c.TurnSequence + 1);
                c.Message = $"❄️ 소로 블루: 상대 [{enemy.TopCard.Name}]은(는) 다음 상대 턴 종료 시까지 어택 · 블록 불가!";
            }, filter: d => d.EvolutionSources.Count == 0, rule: "진화원을 갖지 않은 상대 디지몬")
            .Security(c =>
            {
                var t = c.PickEnemy(d => d.EvolutionSources.Count == 0, c.Other);
                if (t == null) { c.SecurityResult("진화원을 갖지 않은 디지몬이 없어 불발", "불발"); return; }
                LockAttackAndBlock(t, c.TurnSequence + 1);
                c.SecurityResult($"[{t.TopCard.Name}]은(는) 다음 자신의 턴 종료 시까지 어택 · 블록 불가", $"[{t.TopCard.Name}] 봉인");
            });

        // 카이저 네일: 자신의 디지몬 진화원의 디지몬 카드 1장을 코스트 없이 등장
        b.Card("ST2-15")
            .Main(TargetKind.OwnDigimonWithDigimonSource, c =>
            {
                var ally = c.PickOwn(d => d.SourcesAny(IsDigimon), AllyTarget(c));
                var card = ally?.TakeSource(s => !ally.IsFaceDown(s) && IsDigimon(s) && s.Level == ally.SourcesWhere(IsDigimon).Max(x => x.Level));
                if (ally == null || card == null) return;
                c.PlayWithoutCost(card);
                c.Message = $"❄️ 카이저 네일: [{ally.TopCard.Name}]의 진화원 [{card.Name}(Lv.{card.Level})]이(가) 코스트 없이 등장!";
            })
            .Security(c =>
            {
                var host = c.Owner.BattleArea.FirstOrDefault(d => d.SourcesAny(IsDigimon));
                var card = host?.TakeSource(s => !host.IsFaceDown(s) && IsDigimon(s));
                if (card == null) { c.SecurityResult("꺼낼 진화원이 없어 불발", "불발"); return; }
                c.PlayWithoutCost(card).IsOwnersTurn = false;
                c.SecurityResult($"진화원의 [{card.Name}]이(가) 코스트 없이 등장!", $"[{card.Name}] 등장");
            });

        // 코큐토스 브레스: 상대 디지몬 1마리를 패로 (진화원은 파기)
        b.Card("ST2-16")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var enemy = c.PickEnemy(preferred: EnemyTarget(c));
                if (enemy == null) return;
                c.BounceEnemy(enemy);
                c.Message = $"🌊 코큐토스 브레스: 상대 [{enemy.TopCard.Name}]을(를) 패로 되돌렸습니다!";
            })
            .Security(c =>
            {
                c.BounceEnemy(c.Other!);
                c.SecurityResult($"어택하던 [{c.Other!.TopCard.Name}]을(를) 패로 되돌렸습니다!", $"[{c.Other.TopCard.Name}] 패로", true);
            });
    }

    // =====================================================================
    // ST-3 헤븐즈 옐로
    // =====================================================================

    private static void RegisterSt3(CardScriptBuilder b)
    {
        // 토코몬: [진화원] 상대 디지몬이 DP 0으로 소멸했을 때 [턴에 1회] 이 턴 동안 DP +1000
        b.Card("ST3-01").Inherited(EffectTrigger.OpponentDigimonDeletedByZeroDp, c =>
        {
            c.Self!.TemporaryDpBonus += 1000;
            c.Log($"[진화원 효과] 토코몬: [{c.Self.TopCard.Name}] 이 턴 동안 DP +1000");
        }, when: c => c.Self!.IsOwnersTurn, oncePerTurn: true);

        // 파닥몬: [진화원] 상대 디지몬이 DP 0으로 소멸했을 때 [턴에 1회] 메모리 +1
        b.Card("ST3-04").Inherited(EffectTrigger.OpponentDigimonDeletedByZeroDp, c =>
        {
            c.AddMemory(1);
            c.Log("[진화원 효과] 파닥몬: 상대 디지몬이 DP 0으로 소멸 → 메모리 +1");
        }, when: c => c.Self!.IsOwnersTurn, oncePerTurn: true);

        // 엔젤몬: [진화원] [어택 시] 시큐리티 4장 이상이면 메모리 +1
        b.Card("ST3-05").Inherited(EffectTrigger.WhenAttacking, c =>
        {
            c.AddMemory(1);
            c.Log("[진화원 효과] 엔젤몬: 시큐리티 4장 이상 → 메모리 +1");
        }, when: c => c.Owner.SecurityStack.Count >= 4);

        LoseTwoMemoryWhenAttacking(b.Card("ST3-07"));   // 유니몬

        // 홀리엔젤몬: [진화원] [어택 시] 상대 디지몬 1마리 DP -1000
        b.Card("ST3-08").Inherited(EffectTrigger.WhenAttacking, c =>
        {
            var t = c.Other ?? c.PickEnemy(d => d.GetCurrentDP() <= 1000) ?? EffectRules.FindBlocker(c.State, c.Enemy.BattleArea, c.Self!);
            if (t != null && c.Enemy.BattleArea.Contains(t)) c.DpMinus(t, 1000, "홀리엔젤몬 진화원");
        });

        // 엔젤우몬: [진화 시] 시큐리티 3장 이하면 ≪리커버리 +1≫
        b.Card("ST3-09").WhenDigivolving(c =>
        {
            if (c.Owner.SecurityStack.Count <= 3 && c.Recovery(1) > 0)
                c.Log("[진화 시] 엔젤우몬: ≪리커버리 +1≫ 시큐리티 1장 추가");
        });

        // 세라피몬: [어택 시] 상대 디지몬 1마리 DP -4000
        b.Card("ST3-11").WhenAttacking(c =>
        {
            var t = c.Other ?? c.PickEnemy(d => d.GetCurrentDP() <= 4000) ?? EffectRules.FindBlocker(c.State, c.Enemy.BattleArea, c.Self!);
            if (t != null) c.DpMinus(t, 4000, "세라피몬 [어택 시]");
        });

        // 리키: [상대의 턴] 자신의 시큐리티 디지몬 DP +2000
        b.Card("ST3-12").Static(StaticStat.SecurityDigimonDp, EffectSource.Main, c => c.OwnersTurn ? 0 : 2000);

        // 천국의 문
        b.Card("ST3-13")
            .Main(TargetKind.OwnDigimon, AllyDpPlus3000)
            .Security(c =>   // 이 턴 동안 자신의 디지몬과 시큐리티 디지몬 전부 DP +5000, 이 카드는 패로
            {
                foreach (var d in c.Owner.BattleArea) d.TemporaryDpBonus += 5000;
                c.Engine.SecurityDpBuffsOf(c.Owner).Add((5000, c.TurnSequence));
                c.SecurityResult("이 턴 동안 디지몬 · 시큐리티 디지몬 전부 DP +5000, 이 카드는 패로", "DP +5000 · 패로 회수", returnToHand: true);
            });

        // 세븐 헤븐즈: 이 턴 동안 상대 디지몬 1마리 DP -10000
        b.Card("ST3-16")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var enemy = c.PickEnemy(preferred: EnemyTarget(c));
                if (enemy == null) return;
                bool deleted = c.DpMinus(enemy, 10000, "세븐 헤븐즈");
                c.Message = deleted ? $"✨ 세븐 헤븐즈: 상대 [{enemy.TopCard.Name}] DP 0 → 소멸!" : $"✨ 세븐 헤븐즈: 상대 [{enemy.TopCard.Name}] DP -10000";
            })
            .Security(c =>
            {
                var attacker = c.Other!;
                bool deleted = c.DpMinus(attacker, 10000, "세븐 헤븐즈 [시큐리티]");
                c.SecurityResult(deleted ? $"[{attacker.TopCard.Name}] DP 0 → 소멸!" : $"[{attacker.TopCard.Name}] DP -10000",
                    deleted ? $"[{attacker.TopCard.Name}] 소멸" : "DP -10000", deleted);
            });
    }

    // =====================================================================
    // ST-4 기가 그린
    // =====================================================================

    private static void RegisterSt4(CardScriptBuilder b)
    {
        // 모티몬: [진화원] [자신의 턴] 이 디지몬이 Lv.6 이상이면 DP +1000
        b.Card("ST4-01").InheritedDpOnOwnTurn(1000, c => c.Digimon!.TopCard.Level >= 6);

        // 텐타몬: [등장 시] 덱 위 1장이 그린 디지몬이면 패에
        b.Card("ST4-03").OnPlay(c =>
        {
            var got = c.RevealToHand(1, x => IsDigimon(x) && x.Color == CardColor.Green, 1);
            c.Message = got.Count > 0 ? $"[등장 시] [{got[0].Name}] 패에 추가" : "[등장 시] 그린 디지몬이 아니어서 덱 아래로";
        });

        DpWhenAttackingDigimon(b.Card("ST4-04"), "팔몬", 2000);
        DpWhenAttackingDigimon(b.Card("ST4-06"), "니드몬", 2000);

        LoseTwoMemoryWhenAttacking(b.Card("ST4-08"));   // 캅테리몬

        // 릴리몬: [진화 시] 덱 위 5장 중 Lv.6 이상 디지몬 1장
        b.Card("ST4-10").WhenDigivolving(c =>
        {
            var got = c.RevealToHand(5, x => IsDigimon(x) && x.Level >= 6, 1);
            c.Log(got.Count > 0 ? $"[진화 시] 릴리몬: [{got[0].Name}] 패에 추가" : "[진화 시] 릴리몬: 덱 위 5장에 Lv.6 이상 디지몬이 없습니다");
        });

        // 아트라캅테리몬: [진화원] [자신의 턴] [턴에 1회] 배틀에서 상대 디지몬만 소멸시켰을 때 상대 시큐리티 위에서 1장 파기
        b.Card("ST4-11").Inherited(EffectTrigger.DeletedOpponentInBattle, c =>
        {
            if (c.Enemy.SecurityStack.Count == 0) return;
            c.Enemy.Trash.Add(c.Enemy.SecurityStack[0]);
            c.Enemy.SecurityStack.RemoveAt(0);
            c.Engine.NotifySecurityRemoved(c.Enemy);
            c.Log($"[진화원 효과] 아트라캅테리몬: {c.Engine.SideName(c.Enemy)} 시큐리티를 위에서부터 1장 파기!");
        }, oncePerTurn: true);

        // 로제몬: [진화 시] 다음 상대의 턴 종료 시까지 상대 디지몬 1마리 어택 · 블록 불가
        b.Card("ST4-12").WhenDigivolving(c =>
        {
            var enemy = c.PickEnemy(d => !d.IsSuspended) ?? c.PickEnemy();
            if (enemy == null) return;
            LockAttackAndBlock(enemy, c.TurnSequence + 1);
            c.Log($"[진화 시] 로제몬: 상대 [{enemy.TopCard.Name}]은(는) 다음 상대 턴 종료 시까지 어택 · 블록 불가");
        });

        // 헤라클레스캅테리몬: ≪관통≫ / [메인] ≪디지버스트 2≫ 상대 디지몬 1마리 레스트
        b.Card("ST4-13")
            .Keyword(Keyword.Piercing)
            .On(EffectTrigger.ActivateMain, c =>
            {
                if (c.Self == null || c.Self.EvolutionSources.Count < 2)
                {
                    c.Log("[메인] 헤라클레스캅테리몬: 진화원이 2장 미만이라 ≪디지버스트 2≫를 쓸 수 없습니다");
                    c.StepsRan = true;   // 아무것도 하지 않았으므로 이번 턴 사용으로 치지 않는다
                    return;
                }
                var target = c.PickEnemy(d => !d.IsSuspended);
                if (target == null || !c.Engine.AskYesNo(c.Owner, c.Self, "≪디지버스트 2≫ 진화원 2장을 파기하고 상대 디지몬 1마리를 레스트시킬까요?", recommended: true))
                {
                    c.StepsRan = true;
                    return;
                }
                c.Engine.PayDigiBurst(c.Owner, c.Self, 2);   // 트래시된 진화원 · "디지버스트를 발휘했을 때" 효과까지
                c.Engine.Suspend(target);
                c.Log($"[메인] 헤라클레스캅테리몬: ≪디지버스트 2≫ 진화원 2장 파기 → 상대 [{target.TopCard.Name}] 레스트");
            });

        // 장한솔: [자신의 턴] 상대의 디지몬이 레스트했을 때, 이 테이머를 레스트시키는 것으로 메모리 +1
        b.Card("ST4-14").On(EffectTrigger.OpponentDigimonSuspended, c =>
        {
            if (!c.Owner.HasActiveTamer(c.Card) || !c.Engine.AskYesNo(c.Owner, null, "장한솔: 이 테이머를 레스트시키고 메모리 +1 할까요?", recommended: true))
                return;
            c.Engine.SuspendTamer(c.Owner, c.Card);
            c.AddMemory(1);
            c.Log($"[테이머] 장한솔: 레스트 → 메모리 +1 (현재 {c.OwnerMemory})");
        }, when: c => c.Engine.IsTurnOf(c.Owner));

        // 전기 충격: 레스트 상태 상대 디지몬 1마리를 패로
        b.Card("ST4-16")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var enemy = c.PickEnemy(d => d.IsSuspended, EnemyTarget(c));
                if (enemy == null)
                {
                    c.Message = "레스트 상태인 상대 디지몬이 없어 효과가 없습니다.";
                    return;
                }
                c.BounceEnemy(enemy);
                c.Message = $"⚡ 전기 충격: 레스트 상태의 상대 [{enemy.TopCard.Name}]을(를) 패로 되돌렸습니다!";
            }, filter: d => d.IsSuspended, rule: "레스트 상태인 상대 디지몬")
            .Security(c =>   // 어택 중인 디지몬은 레스트 상태
            {
                var t = c.PickEnemy(d => d.IsSuspended, c.Other);
                if (t == null) { c.SecurityResult("레스트 상태 디지몬이 없어 불발", "불발"); return; }
                c.BounceEnemy(t);
                c.SecurityResult($"[{t.TopCard.Name}]을(를) 패로 되돌렸습니다!", $"[{t.TopCard.Name}] 패로", ReferenceEquals(t, c.Other));
            });
    }

    // =====================================================================
    // ST-5 마신 블랙
    // =====================================================================

    private static void RegisterSt5(CardScriptBuilder b)
    {
        // 카프리몬: [진화원] [자신의 턴] 이 디지몬이 ≪블로커≫를 가졌으면 DP +1000
        b.Card("ST5-01").InheritedDpOnOwnTurn(1000, c => c.Digimon!.HasBlocker);

        DrawIfOpponentDidNotAttack(b.Card("ST5-04"));   // 토이아구몬
        DrawIfOpponentDidNotAttack(b.Card("ST5-06"));   // 그레이몬(흑)

        // 신태일(흑): [상대의 턴] 자신의 디지몬이 ≪블로커≫를 사용하여 레스트했을 때, 이 테이머를 레스트시키는 것으로 자신의 디지몬 1마리 액티브
        b.Card("ST5-14").On(EffectTrigger.AllyDigimonSuspended, c =>
        {
            if (c.Other == null || !c.Engine.SuspendingForBlock || !c.Owner.HasActiveTamer(c.Card))   // ≪블로커≫로 블록해서 레스트했을 때만
                return;
            var target = c.PickOwn(d => d.IsSuspended, c.Other);
            if (target == null || !c.Engine.AskYesNo(c.Owner, null, $"신태일: 이 테이머를 레스트시키고 [{target.TopCard.Name}]을(를) 액티브로 할까요?", recommended: true))
                return;
            c.Engine.SuspendTamer(c.Owner, c.Card);
            c.Engine.Unsuspend(target);
            c.Log($"[테이머] 신태일: 레스트 → [{target.TopCard.Name}] 액티브");
        }, when: c => !c.Engine.IsTurnOf(c.Owner));

        LoseTwoMemoryWhenAttacking(b.Card("ST5-08"));   // 다크티라노몬

        // 메탈그레이몬(흑): [진화 시] 다음 상대의 턴 종료 시까지 자신의 디지몬 1마리 ≪블로커≫
        b.Card("ST5-09").WhenDigivolving(c =>
        {
            c.Self!.GrantedBlockerUntil = Math.Max(c.Self.GrantedBlockerUntil, c.TurnSequence + 1);
            c.Log($"[진화 시] 메탈그레이몬: [{c.Self.TopCard.Name}]이(가) 다음 상대 턴 종료 시까지 ≪블로커≫를 얻습니다");
        });

        b.Card("ST5-11").InheritedKeyword(Keyword.Blocker);   // 메가드라몬 진화원 ≪블로커≫

        // 파워드라몬: [진화 시] 다음 상대의 턴 종료 시까지 자신의 디지몬 2마리까지 ≪재기동≫
        b.Card("ST5-12").WhenDigivolving(c =>
        {
            var picks = new List<CardInstance> { c.Self! };
            var other = c.Owner.BattleArea.Where(d => !ReferenceEquals(d, c.Self)).OrderByDescending(d => d.GetCurrentDP()).FirstOrDefault();
            if (other != null) picks.Add(other);
            foreach (var p in picks) p.GrantedRebootUntil = Math.Max(p.GrantedRebootUntil, c.TurnSequence + 1);
            c.Log($"[진화 시] 파워드라몬: {string.Join(", ", picks.Select(p => p.TopCard.Name))}이(가) ≪재기동≫을 얻습니다");
        });

        // 레이저 아이: 상대 디지몬 2마리까지 ≪퇴화 1≫
        b.Card("ST5-15")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var chosen = EnemyTarget(c);
                var picks = c.Enemy.BattleArea.Where(d => d.EvolutionSources.Count > 0 && d.TopCard.Level > 3)
                    .OrderByDescending(d => ReferenceEquals(d, chosen)).ThenByDescending(d => d.TopCard.Level).Take(2).ToList();
                var names = picks.Select(p => p.TopCard.Name).ToList();
                foreach (var p in picks) c.DeDigivolveEnemy(p, 1);
                c.Message = picks.Count > 0 ? $"🔻 레이저 아이: 상대 [{string.Join(", ", names)}] ≪퇴화 1≫" : "퇴화시킬 상대 디지몬이 없습니다.";
            })
            .Security(c =>
            {
                var picks = c.Enemy.BattleArea.Where(d => d.EvolutionSources.Count > 0 && d.TopCard.Level > 3)
                    .OrderByDescending(d => ReferenceEquals(d, c.Other)).ThenByDescending(d => d.TopCard.Level).Take(2).ToList();
                foreach (var p in picks) c.DeDigivolveEnemy(p, 1);
                c.SecurityResult($"디지몬 {picks.Count}마리 ≪퇴화 1≫", $"{picks.Count}마리 퇴화");
            });

        // 제노사이드 어택: 등장 코스트 7 이하 상대 디지몬 1마리 소멸
        b.Card("ST5-16")
            .Main(TargetKind.OpponentDigimon, c =>
            {
                var enemy = c.PickEnemy(d => d.TopCard.PlayCost <= 7, EnemyTarget(c));
                if (enemy == null)
                {
                    c.Message = "등장 코스트 7 이하인 상대 디지몬이 없습니다.";
                    return;
                }
                c.DeleteEnemy(enemy);
                c.Message = $"🔥 제노사이드 어택: 상대 [{enemy.TopCard.Name}] 소멸!";
            }, filter: d => d.TopCard.PlayCost <= 7, rule: "등장 코스트 7 이하인 상대 디지몬")
            .Security(c =>
            {
                var t = c.PickEnemy(d => d.TopCard.PlayCost <= 7, c.Other);
                if (t == null) { c.SecurityResult("등장 코스트 7 이하 디지몬이 없어 불발", "불발"); return; }
                c.DeleteEnemy(t);
                c.SecurityResult($"[{t.TopCard.Name}] 소멸!", $"[{t.TopCard.Name}] 소멸", ReferenceEquals(t, c.Other));
            });
    }

    // =====================================================================
    // ST-6 베놈 바이올렛
    // =====================================================================

    private static void RegisterSt6(CardScriptBuilder b)
    {
        // 퍼그몬: [진화원] [소멸 시] 자신의 덱 위에서 2장 파기
        b.Card("ST6-01").Inherited(EffectTrigger.OnDeletion, c => c.Mill(2, "퍼그몬 진화원 [소멸 시]"));

        DrawThenDiscardWhenAttacking(b.Card("ST6-03"));   // 파피몬(퍼플)

        // 드라큐몬: [등장 시] 트래시의 퍼플 옵션(코스트 1 또는 7) 1장 패로 ("~되돌릴 수 있다" — 고르지 않아도 된다)
        b.Card("ST6-04").OnPlay(c =>
        {
            var opt = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, c.Owner.Trash.Where(x => x.Type == CardType.Option && x.Color == CardColor.Purple && x.PlayCost is 1 or 7).Reverse().ToList(),
                1, upTo: true, "드라큐몬: 트래시에서 패로 되돌릴 옵션 카드를 고르세요.").FirstOrDefault();
            if (opt == null) return;
            c.Owner.Trash.Remove(opt);
            c.Owner.Hand.Add(opt);
            c.Message = $"[등장 시] 트래시의 [{opt.Name}] 패로 회수";
        });

        DrawThenDiscardWhenAttacking(b.Card("ST6-06"));   // 가루몬(퍼플)

        LoseTwoMemoryWhenAttacking(b.Card("ST6-08"));   // 데블몬

        // 매튜(퍼플): [자신의 턴] 자신의 디지몬이 소멸했을 때, 이 테이머를 레스트시키는 것으로 메모리 +1
        b.Card("ST6-14").On(EffectTrigger.AllyDigimonDeleted, c =>
        {
            if (!c.Owner.HasActiveTamer(c.Card) || !c.Engine.AskYesNo(c.Owner, null, "매튜: 이 테이머를 레스트시키고 메모리 +1 할까요?", recommended: true))
                return;
            c.Engine.SuspendTamer(c.Owner, c.Card);
            c.AddMemory(1);
            c.Log($"[테이머] 매튜: 레스트 → 메모리 +1 (현재 {c.OwnerMemory})");
        }, when: c => c.Engine.IsTurnOf(c.Owner));

        // 스컬사탄몬: [진화 시] 트래시의 퍼플 디지몬 1장 패로 ("~되돌릴 수 있다" — 고르지 않아도 된다)
        b.Card("ST6-10").WhenDigivolving(c =>
        {
            var card = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, c.Owner.Trash.Where(x => IsDigimon(x) && x.Color == CardColor.Purple).OrderByDescending(x => x.Level).ToList(),
                1, upTo: true, "스컬사탄몬: 트래시에서 패로 되돌릴 퍼플 디지몬을 고르세요.").FirstOrDefault();
            if (card == null) return;
            c.Owner.Trash.Remove(card);
            c.Owner.Hand.Add(card);
            c.Log($"[진화 시] 스컬사탄몬: 트래시의 [{card.Name}] 패로 회수");
        });

        // 워가루몬(퍼플): [진화원] [자신의 턴] 자신의 트래시 5장 이상이면 DP +2000
        b.Card("ST6-11").InheritedDpOnOwnTurn(2000, c => c.Owner.Trash.Count >= 5);

        // 데스 클로
        b.Card("ST6-15")
            .Main(TargetKind.OpponentDigimon, c =>   // 자신의 디지몬 1마리를 소멸시키는 것으로 Lv.4 이하 상대 디지몬 1마리 소멸
            {
                var enemy = c.PickEnemy(d => d.TopCard.Level <= 4, EnemyTarget(c));
                var sacrifice = c.Owner.BattleArea.OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP()).FirstOrDefault();
                if (enemy == null || sacrifice == null)
                {
                    c.Message = "소멸시킬 내 디지몬 또는 Lv.4 이하 상대 디지몬이 없어 효과가 없습니다.";
                    return;
                }
                c.DeleteOwn(sacrifice);
                c.DeleteEnemy(enemy);
                c.Message = $"💀 데스 클로: 내 [{sacrifice.TopCard.Name}]을(를) 소멸시켜 상대 [{enemy.TopCard.Name}] 소멸!";
            }, filter: d => d.TopCard.Level <= 4, rule: "Lv.4 이하인 상대 디지몬")
            .Security(c =>   // Lv.4 이하 상대 디지몬 1마리 소멸
            {
                var t = c.PickEnemy(d => d.TopCard.Level <= 4, c.Other);
                if (t == null) { c.SecurityResult("Lv.4 이하 디지몬이 없어 불발", "불발"); return; }
                c.DeleteEnemy(t);
                c.SecurityResult($"[{t.TopCard.Name}] 소멸!", $"[{t.TopCard.Name}] 소멸", ReferenceEquals(t, c.Other));
            });

        // 어둠의 저주
        b.Card("ST6-16")
            .Main(TargetKind.None, c =>   // 트래시의 퍼플 Lv.3 1장 + 퍼플 Lv.4 1장 코스트 없이 등장
            {
                var played = c.PlayFromTrash(x => x.Color == CardColor.Purple && x.Level == 3, 1)
                    .Concat(c.PlayFromTrash(x => x.Color == CardColor.Purple && x.Level == 4, 1)).ToList();
                c.Message = played.Count > 0 ? $"🌑 어둠의 저주: 트래시에서 {string.Join(", ", played.Select(p => p.Name))} 등장!" : "트래시에 등장시킬 퍼플 디지몬이 없습니다.";
            })
            .Security(c =>   // 트래시의 퍼플 Lv.4 이하 1장 등장
            {
                var played = c.PlayFromTrash(x => x.Color == CardColor.Purple && x.Level <= 4, 1);
                if (played.Count > 0) c.SecurityResult($"트래시의 [{played[0].Name}] 등장!", $"[{played[0].Name}] 등장");
                else c.SecurityResult("트래시에 퍼플 디지몬이 없어 불발", "불발");
            });
    }

    // =====================================================================
    // BT-1
    // =====================================================================

    private static void RegisterBt1(CardScriptBuilder b)
    {
        DpWhenAttackingDigimon(b.Card("BT1-001"), "어니몬", 1000);

        // 우파몬: [진화원] [어택 시] [턴에 1회] 진화원을 갖지 않은 상대 디지몬이 있으면 ≪1 드로우≫
        b.Card("BT1-003").Inherited(EffectTrigger.WhenAttacking, c =>
        {
            var drawn = c.Draw();
            if (drawn != null) c.Log($"[진화원 효과] 우파몬: ≪1 드로우≫ [{drawn.Name}]");
        }, when: c => c.Enemy.BattleArea.Any(d => d.EvolutionSources.Count == 0), oncePerTurn: true);

        // 아구몬: [등장 시] 덱 위 5장 중 테이머 1장
        b.Card("BT1-010").OnPlay(c =>
        {
            var got = c.RevealToHand(5, x => x.Type == CardType.Tamer, 1);
            c.Message = got.Count > 0 ? $"[등장 시] 테이머 [{got[0].Name}] 패에 추가" : "[등장 시] 덱 위 5장에 테이머가 없습니다";
        });

        GainThreeMemoryUntilEndOfTurn(b.Card("BT1-021"));   // 메탈그레이몬(BT1)

        // 워그레이몬(BT1): [진화 시] 이 턴 동안 ≪시큐리티 어택 +1≫ / 체크한 옵션 카드의 [시큐리티] 효과는 발휘하지 않는다
        b.Card("BT1-025")
            .Keyword(Keyword.NegateSecurityOptions)
            .WhenDigivolving(c =>
            {
                c.Self!.TemporarySecurityAttackBonus += 1;
                c.Self.ActiveSkillBuffLabels.Add("🔥워그레이몬 [진화 시] (시큐어택 +1)");
                c.Log("[진화 시] 워그레이몬: 이 턴 동안 ≪시큐리티 어택 +1≫");
            });

        // 파피몬: [등장 시] ≪1 드로우≫
        b.Card("BT1-029").OnPlay(c =>
        {
            var drawn = c.Draw();
            if (drawn != null) c.Message = $"[등장 시] {drawn.Name} 드로우";
        });

        b.Card("BT1-034").InheritedKeyword(Keyword.UnblockableBySourceless);   // 원뿔몬

        GainThreeMemoryUntilEndOfTurn(b.Card("BT1-040"));   // 워가루몬(BT1)

        // 파닥몬: [등장 시] 덱 위 4장 중 옐로 테이머 전부
        b.Card("BT1-048").OnPlay(c =>
        {
            var got = c.RevealToHand(4, x => x.Type == CardType.Tamer && x.Color == CardColor.Yellow, 4);
            c.Message = got.Count > 0 ? $"[등장 시] 옐로 테이머 {got.Count}장 패에 추가" : "[등장 시] 덱 위 4장에 옐로 테이머가 없습니다";
        });

        // 홀리엔젤몬(BT1): [등장 시] ≪리커버리 +1 (덱)≫ / [진화원] [자신의 턴] 시큐리티 3장마다 DP +1000
        b.Card("BT1-060")
            .OnPlay(c =>
            {
                if (c.Recovery(1) > 0) c.Message = "[등장 시] ≪리커버리 +1≫ 시큐리티 1장 추가";
            })
            .Static(StaticStat.Dp, EffectSource.Inherited, c => c.OwnersTurn ? c.Owner.SecurityStack.Count / 3 * 1000 : 0);

        // 팔몬: [등장 시] 덱 위 3장 중 Lv.4 디지몬 1장
        b.Card("BT1-067").OnPlay(c =>
        {
            var got = c.RevealToHand(3, x => IsDigimon(x) && x.Level == 4, 1);
            c.Message = got.Count > 0 ? $"[등장 시] [{got[0].Name}] 패에 추가" : "[등장 시] 덱 위 3장에 Lv.4 디지몬이 없습니다";
        });

        // 니드몬(BT1): [진화 시] 덱 위 3장 중 Lv.5 이상 디지몬 1장
        b.Card("BT1-074").WhenDigivolving(c =>
        {
            var got = c.RevealToHand(3, x => IsDigimon(x) && x.Level >= 5, 1);
            c.Log(got.Count > 0 ? $"[진화 시] 니드몬: [{got[0].Name}] 패에 추가" : "[진화 시] 니드몬: 덱 위 3장에 Lv.5 이상 디지몬이 없습니다");
        });

        // 릴리몬(BT1): [진화원] [어택 시] ≪블로커≫를 갖지 않은 상대 디지몬 1마리 레스트
        b.Card("BT1-079").Inherited(EffectTrigger.WhenAttacking, c =>
        {
            var t = c.PickEnemy(d => !d.IsSuspended && !d.HasBlocker);
            if (t == null) return;
            c.Engine.Suspend(t);
            c.Log($"[진화원 효과] 릴리몬: 상대 [{t.TopCard.Name}] 레스트");
        });

        // 오메가몬: Lv.6 레드 · 블루에서 진화
        b.Card("BT1-084")
            .EvolvesFrom(t => t.TopCard.Color is CardColor.Red or CardColor.Blue)
            .WhenDigivolving(c =>   // 상대 디지몬 1마리를 골라 같은 명칭의 상대 디지몬 전부 소멸
            {
                var chosen = c.PickEnemy();
                if (chosen == null) return;
                var victims = c.Enemy.BattleArea.Where(d => CardNames.Same(d.TopCard, chosen.TopCard)).ToList();
                foreach (var v in victims) c.DeleteEnemy(v);
                c.Log($"👑 [진화 시] 오메가몬: 상대 [{chosen.TopCard.Name}] 명칭의 디지몬 {victims.Count}마리 전부 소멸!");
            })
            .WhenAttacking(c =>   // [턴에 1회] 진화원의 Lv.6 디지몬 1장을 패로 → 액티브
            {
                var lv6 = c.Self!.TakeSource(s => !c.Self.IsFaceDown(s) && IsDigimon(s) && s.Level == 6);
                if (lv6 == null) return;
                c.Owner.Hand.Add(lv6);
                c.Engine.Unsuspend(c.Self);
                c.Log($"👑 [어택 시] 오메가몬: 진화원 [{lv6.Name}]을(를) 패로 되돌리고 액티브!");
            }, oncePerTurn: true);
    }

    // =====================================================================
    // BT-2
    // =====================================================================

    private static void RegisterBt2(CardScriptBuilder b)
    {
        // 길몬: [진화원] [자신의 턴] 상대 트래시 5장 이상이면 DP +1000
        b.Card("BT2-009").InheritedDpOnOwnTurn(1000, c => c.Enemy.Trash.Count >= 5);

        // 듀크몬
        b.Card("BT2-020")
            .WhenDigivolving(c =>   // 레드 테이머가 있으면 DP 6000 이하 상대 디지몬 1마리 소멸
            {
                var enemy = c.PickEnemy(d => d.GetCurrentDP() <= 6000);
                if (enemy == null) return;
                c.DeleteEnemy(enemy);
                c.Log($"[진화 시] 듀크몬: 상대 [{enemy.TopCard.Name}] 소멸!");
            }, when: c => c.Owner.Tamers.Any(t => t.Color == CardColor.Red))
            .WhenAttacking(c =>   // 상대 트래시 10장마다 상대 시큐리티 1장 파기
            {
                int n = Math.Min(c.Enemy.Trash.Count / 10, c.Enemy.SecurityStack.Count);
                for (int i = 0; i < n; i++)
                {
                    c.Enemy.Trash.Add(c.Enemy.SecurityStack[0]);
                    c.Enemy.SecurityStack.RemoveAt(0);
                    c.Engine.NotifySecurityRemoved(c.Enemy);
                }
                if (n > 0) c.Log($"[어택 시] 듀크몬: 상대 시큐리티 {n}장 파기!");
            });

        // 홀리드라몬
        b.Card("BT2-039")
            .OnPlay(c =>   // 시큐리티 3장 이하면 ≪리커버리 +2≫
            {
                int n = c.Recovery(2);
                c.Message = $"[등장 시] ≪리커버리 +2≫ 시큐리티 {n}장 추가";
            }, when: c => c.Owner.SecurityStack.Count <= 3)
            .WhenAttacking(c =>   // 패의 옐로 Lv.3 디지몬 1장을 코스트 없이 등장
            {
                // 공식 Q&A BT2-039 Q1: 쓰지 않아도 되는 효과 ("~할 수 있다")
                var candidates = c.Owner.Hand.Where(x => IsDigimon(x) && x.Color == CardColor.Yellow && x.Level == 3).ToList();
                if (candidates.Count == 0 || c.Owner.BattleArea.Count >= 8
                    || !c.Engine.AskYesNo(c.Owner, c.Self, "홀리드라몬: 패의 옐로 Lv.3 디지몬 1장을 코스트 없이 등장시킬까요?", recommended: true))
                    return;
                var lv3 = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, candidates, 1, upTo: false, "홀리드라몬: 코스트 없이 등장시킬 옐로 Lv.3 디지몬을 고르세요.").FirstOrDefault();
                if (lv3 == null) return;
                c.Owner.Hand.Remove(lv3);
                var inst = c.PlayWithoutCost(lv3);
                c.Log($"[어택 시] 홀리드라몬: 패의 [{lv3.Name}]을(를) 코스트 없이 등장");
                c.Engine.ApplyOnPlayEffects(c.Owner, inst);
            });

        // 크리사리몬: [진화원] 같은 명칭의 다른 자신의 디지몬이 등장했을 때 메모리 +1
        b.Card("BT2-059").Inherited(EffectTrigger.AllySameNamePlayed, c =>
        {
            c.AddMemory(1);
            c.Log($"[진화원 효과] 크리사리몬: 같은 명칭 [{c.Other!.TopCard.Name}] 등장 → 메모리 +1");
        }, when: c => c.Other != null && CardNames.Same(c.Self!.TopCard, c.Other.TopCard));

        b.Card("BT2-065").Keyword(Keyword.Reboot);   // 워그레이몬(BT2) ≪재기동≫

        // 브이몬: [진화원] [자신의 턴] [턴에 1회] 이 디지몬이 메인 페이즈에 액티브가 되었을 때 ≪1 드로우≫
        b.Card("BT2-021").Inherited(EffectTrigger.ThisDigimonUnsuspendedInMain, c =>
        {
            var drawn = c.Draw();
            if (drawn != null) c.Log($"[진화원 효과] 브이몬: 메인 페이즈에 액티브 → ≪1 드로우≫ [{drawn.Name}]");
        }, when: c => c.Engine.IsTurnOf(c.Owner), oncePerTurn: true);

        // 알포스브이드라몬: [자신의 턴] 블루 테이머가 레스트했을 때 이 디지몬 액티브 / [턴에 1회] 메인 페이즈에 액티브가 되었을 때 메모리 +1
        b.Card("BT2-032")
            .On(EffectTrigger.AllyTamerSuspended, c =>
            {
                if (c.Self == null || !c.Self.IsSuspended) return;
                c.Engine.Unsuspend(c.Self);
                c.Log("[자신의 턴] 알포스브이드라몬: 블루 테이머가 레스트 → 액티브");
            }, when: c => c.Engine.IsTurnOf(c.Owner) && (c.TriggerCard?.AllColors.Contains(CardColor.Blue) ?? false))
            .On(EffectTrigger.ThisDigimonUnsuspendedInMain, c =>
            {
                c.AddMemory(1);
                c.Log($"[자신의 턴] 알포스브이드라몬: 메인 페이즈에 액티브 → 메모리 +1 (현재 {c.OwnerMemory})");
            }, when: c => c.Engine.IsTurnOf(c.Owner), oncePerTurn: true);

        // 임프몬: [소멸 시] 자신의 덱 위에서 3장 파기
        b.Card("BT2-068").OnDeletion(c => c.Mill(3, "임프몬 [소멸 시]"));

        b.Card("BT2-074").Keyword(Keyword.Retaliation).InheritedKeyword(Keyword.Retaliation);   // 데블몬 ≪길동무≫

        // 피에몬: ≪길동무≫ / [등장 시] 트래시의 퍼플 Lv.4 이하 디지몬 2장까지 등장
        b.Card("BT2-080")
            .Keyword(Keyword.Retaliation)
            .OnPlay(c =>
            {
                var played = c.PlayFromTrash(x => x.Color == CardColor.Purple && x.Level <= 4, 2);
                if (played.Count > 0) c.Message = $"[등장 시] 트래시에서 {string.Join(", ", played.Select(p => p.Name))} 등장";
            });
    }

    // =====================================================================
    // BT-3 ~ BT-5
    // =====================================================================

    private static void RegisterBt3To5(CardScriptBuilder b)
    {
        // 황제드라몬: ≪재밍≫ / [진화 시] ≪재밍≫을 가진 자신의 디지몬 전부 액티브
        b.Card("BT3-031")
            .Keyword(Keyword.Jamming)
            .WhenDigivolving(c =>
            {
                foreach (var d in c.Owner.BattleArea.Where(d => CardScripts.HasKeyword(d, Keyword.Jamming))) c.Engine.Unsuspend(d);
                c.Log("[진화 시] 황제드라몬: ≪재밍≫을 가진 자신의 디지몬 전부 액티브");
            });

        // 오메가몬 Alter-S: Lv.6 레드 · 블랙에서 진화
        b.Card("BT3-112")
            .EvolvesFrom(t => t.TopCard.Color is CardColor.Red or CardColor.Black)
            .WhenDigivolving(c =>   // 상대 전부 ≪퇴화 1≫ 후 DP 5000 이하 전부 소멸
            {
                foreach (var d in c.Enemy.BattleArea.ToList()) c.DeDigivolveEnemy(d, 1);
                c.RecalculateFieldBuffs();
                var victims = c.Enemy.BattleArea.Where(d => d.GetCurrentDP() <= 5000).ToList();
                foreach (var v in victims) c.DeleteEnemy(v);
                c.Log($"👑 [진화 시] 오메가몬 Alter-S: 상대 전부 ≪퇴화 1≫ → DP 5000 이하 {victims.Count}마리 소멸!");
            })
            .WhenAttacking(c =>   // 진화원의 Lv.6 디지몬 1장을 패로 → 이 턴 동안 블록당하지 않음 (막을 블로커가 있을 때만)
            {
                if (EffectRules.FindBlocker(c.State, c.Enemy.BattleArea.Where(d => !ReferenceEquals(d, c.Other)), c.Self!) == null) return;
                var lv6 = c.Self!.TakeSource(s => !c.Self.IsFaceDown(s) && IsDigimon(s) && s.Level == 6);
                if (lv6 == null) return;
                c.Owner.Hand.Add(lv6);
                c.Self.UnblockableUntil = c.TurnSequence;
                c.Log($"👑 [어택 시] 오메가몬 Alter-S: 진화원 [{lv6.Name}]을(를) 패로 되돌려 이 턴 동안 블록당하지 않습니다!");
            });

        // 오메가몬(BT5): Lv.6 레드 · 블루에서 진화 / [진화 시] 이 디지몬을 액티브로 → [진화 시] ≪진격≫
        // (액티브가 된 뒤에 ≪진격≫을 처리해야 어택할 수 있다. 상대 효과에서 지키는 [서로의 턴] 효과는 아직 문장만)
        b.Card("BT5-086")
            .EvolvesFrom(t => t.TopCard.Color is CardColor.Red or CardColor.Blue)
            .WhenDigivolving(c =>
            {
                c.Engine.Unsuspend(c.Self!);
                c.Log("👑 [진화 시] 오메가몬: 액티브 상태가 됩니다");
                if (c.Engine.TryGrantBlitz(c.Owner, c.Self!))
                    c.Log("👑 [진화 시] 오메가몬: ≪진격≫ — 상대 쪽 메모리가 1 이상이어서 이 디지몬으로 어택할 수 있습니다");
            });
    }
}
