using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.85.0 테이머 아래 카드를 테이머마다 따로 둔다 (PlayerZones.UnderTamerHosts85).
/// 근거:
///  - 종합 룰 4-4-2: 아래에 겹쳐진 카드가 있는 테이머 아래에 카드를 놓을 때는 맨 아래에 놓는다 (공식 Q&A BT21-013 · EX10-039 · BT25-090 Q6413 — 그 테이머 아래)
///  - 종합 룰 4-7-8: 아래에 카드가 겹쳐진 카드 1장이 에어리어를 떠나면 그 카드 아래 카드는 동시에 파기된다 (테이머 1명이 떠나면 그 테이머 아래 카드만)
///  - 종합 룰 4-7-7 · 16-28 ≪마인드 링크≫: 테이머가 디지몬의 진화원 아래로 가면 에어리어를 떠난다 → 그 테이머 아래 카드는 파기
///  - 종합 룰 16-20 ≪세이브≫ "자신의 테이머 1명의 밑에" · 16-21-4 ≪머티리얼 세이브≫ "테이머 1명 밑에" — 놓을 때는 테이머 1명을 고른다
///  - 종합 룰 3-4-5 · 4-3-2: 테이머에서 진화하면 그 테이머 아래 카드는 그 디지몬의 진화원 (그 테이머 아래 카드만)
///  - 종합 룰 4-7-9 · 4-7-10: 뒷면 카드는 정보가 없다 · 주인만 볼 수 있다 (카드마다 · 테이머마다)
///  - 공식 Q&A BT10-093 Q2024 · P-096 Q4183 · ST24-06 Q6212 · BT25-035 Q6301: "테이머 아래에서" 고르거나 파기하는 효과는 여러 테이머 아래에서 함께 고를 수 있다
///  - 공식 Q&A BT10-087 Q2126 · BT19-079 Q3139: 디지크로스는 다른 테이머 아래 카드도
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>테이머가 떠나 그 아래 카드를 트래시로 보낸 장 수 (시뮬레이션 · 진단용)</summary>
    public int UnderTamerOrphansTrashed85 { get; private set; }

    /// <summary>룰 처리로만 맞춘 경우 (테이머가 떠난 곳에서 부르지 않았는데 RecalculateFieldBuffs가 찾았다 — 0이어야 한다)</summary>
    public int UnderTamerLateSweeps85 { get; private set; }

    /// <summary>지금 알리는 "테이머 아래 카드가 파기 · 놓였다"의 테이머들 ("this Tamer" 유발 조건)</summary>
    internal IReadOnlyList<CardData> UnderTamerEventHosts85 { get; private set; } = Array.Empty<CardData>();

    /// <summary>
    /// 카드를 zones의 테이머 tamer 아래 맨 아래에 놓는다 (원래 있던 곳에서 이미 뺀 카드). faceDown이면 뒷면 (복사본). 놓인 카드 반환
    /// </summary>
    internal CardData PlaceUnderTamer85(PlayerZones zones, CardData tamer, CardData card, bool faceDown = false)
    {
        if (faceDown)
        {
            var copy = zones.AddUnderTamerFaceDown(tamer, card);
            FaceDownPlacedTotal++;
            EffectLog(zones, $"[테이머 아래 뒷면] 카드 1장을 [{tamer.Name}] 아래에 뒷면으로 놓았습니다 (뒷면 {zones.FaceDownUnderCountOf(tamer)}장)");
            return copy;
        }
        zones.PlaceUnderTamer85(tamer, card);
        return card;
    }

    /// <summary>
    /// 카드를 놓을 zones의 테이머 1명을 고른다 (chooser = 효과를 처리하는 쪽 — 상대의 테이머 아래에 놓는 효과는 상대 테이머 중에서 자신이 고른다).
    /// 후보가 1명이면 그 테이머, 없으면 null. AI · 테스트는 추천 (<see cref="RankUnderTamerHosts85"/>)
    /// </summary>
    internal CardData? ChooseUnderTamerHost85(PlayerZones zones, PlayerZones chooser, IReadOnlyCollection<CardData> cards, string question, Func<CardData, bool>? filter = null, bool faceDown = false)
    {
        var ranked = RankUnderTamerHosts85(zones, chooser, cards, filter, faceDown);
        if (ranked.Count <= 1) return ranked.FirstOrDefault();
        return ChooseTamer85(chooser, zones, ranked, question);
    }

    /// <summary>zones의 테이머 후보 ranked(추천 순서) 중 1명을 chooser가 고른다 (플레이어면 창, 그 밖엔 첫째)</summary>
    internal CardData ChooseTamer85(PlayerZones chooser, PlayerZones zones, IReadOnlyList<CardData> ranked, string question)
    {
        if (ranked.Count == 1 || !ShouldAskPlayer(chooser)) return ranked[0];
        var fakes = ranked.Select(t => new CardInstance(t, 0) { IsSuspended = !zones.HasActiveTamer(t) }).ToList();
        var pick = ChooseTargets(chooser, fakes, 1, KoreanNameLocalizer.Localize(question), optional: false, preselected: null).FirstOrDefault();   // 질문의 영어 카드 이름 → 공식 한글 이름
        int i = pick == null ? 0 : fakes.FindIndex(f => ReferenceEquals(f, pick));
        return ranked[Math.Max(0, i)];
    }

    /// <summary>
    /// 카드를 놓을 테이머의 추천 순서 (AI 판단). 자신의 테이머: 테이머 아래 카드를 쓰는 테이머 ("under this Tamer" · 진화원 효과가 있는 하이브리드 테이머) ·
    /// 이미 아래에 카드가 많은 테이머 (〔진화〕 · "아래에 카드가 N장 이상" 조건을 채우기 쉽다) 먼저. 상대의 테이머: 그 반대 (상대에게 덜 쓸모 있는 곳)
    /// </summary>
    internal List<CardData> RankUnderTamerHosts85(PlayerZones zones, PlayerZones chooser, IReadOnlyCollection<CardData> cards, Func<CardData, bool>? filter = null, bool faceDown = false)
    {
        var tamers = zones.Tamers.Distinct().Where(t => filter == null || filter(t)).ToList();
        if (tamers.Count <= 1) return tamers;
        bool own = ReferenceEquals(zones, chooser);
        int Score(CardData t)
        {
            int s = 0;
            string text = (t.EffectText ?? "") + " " + (t.InheritedEffectText ?? "");
            if (text.Contains("under this Tamer", StringComparison.OrdinalIgnoreCase) || text.Contains("cards under it", StringComparison.OrdinalIgnoreCase)) s += 100;
            if (text.Contains("placed under this Tamer", StringComparison.OrdinalIgnoreCase)) s += 80;   // "이 테이머 아래에 카드가 놓였을 때" (BT10-093)
            if (!string.IsNullOrWhiteSpace(t.InheritedEffectText)) s += 50;   // 진화원 효과가 있는 테이머 (하이브리드체 — 테이머에서 진화하면 아래 카드가 진화원)
            if (faceDown && text.Contains("face down", StringComparison.OrdinalIgnoreCase)) s += 30;
            s += 5 * Math.Min(10, zones.UnderCountOf(t));
            if (TamerEvolvableSoon85(zones, t)) s += 40;
            return own ? s : -s;
        }
        return tamers.Select((t, i) => (t, i, s: Score(t))).OrderByDescending(x => x.s).ThenBy(x => x.i).Select(x => x.t).ToList();
    }

    /// <summary>패에 이 테이머에서 진화할 수 있는 디지몬 카드가 있는지 (〔진화〕 · 하이브리드체) — 추천용</summary>
    private bool TamerEvolvableSoon85(PlayerZones zones, CardData tamer)
    {
        try
        {
            foreach (var h in zones.Hand)
                if (h.Type == CardType.Digimon && CardScripts.TamerEvolutionCost(h, tamer, new StaticContext { Engine = this, State = State, Owner = zones, Enemy = EnemyOf(zones), OwnersTurn = IsTurnOf(zones) }) != null)
                    return true;
        }
        catch (InvalidOperationException) { }
        return false;
    }

    /// <summary>
    /// 테이머 아래 카드 중 count장을 아래에서부터 파기한다 (faceDownOnly면 뒷면 카드만). 여러 테이머 아래에서 함께 골라도 된다 (공식 Q&A ST24-06 Q6212 · BT25-035 Q6301):
    /// 1장마다 그 카드를 가진 테이머를 고르고 그 테이머 아래의 맨 아래 (뒷면) 카드를 파기한다. tamerFilter가 있으면 그 테이머 아래만 ("this Tamer").
    /// 파기한 카드 반환 (효과로 파기 — "When effects trash cards from under ~"를 알린다)
    /// </summary>
    internal List<CardData> TrashBottomUnderTamers85(PlayerZones owner, int count, bool faceDownOnly, Func<CardData, bool>? tamerFilter, string question)
    {
        var trashed = new List<CardData>();
        var hosts = new List<CardData>();
        for (int k = 0; k < count; k++)
        {
            var candidates = owner.Tamers.Distinct().Where(t => (tamerFilter == null || tamerFilter(t)) && BottomUnder85(owner, t, faceDownOnly) != null)
                .OrderByDescending(t => faceDownOnly ? owner.FaceDownUnderCountOf(t) : owner.UnderCountOf(t)).ToList();
            if (candidates.Count == 0) break;
            var host = ChooseTamer85(owner, owner, candidates, $"{question} ({k + 1}/{count}) — 어느 테이머 아래에서 파기할까요?");
            var x = BottomUnder85(owner, host, faceDownOnly)!;
            owner.RemoveUnderTamer85(x);
            owner.Trash.Add(x);
            trashed.Add(x);
            if (!hosts.Any(h => ReferenceEquals(h, host))) hosts.Add(host);
        }
        if (trashed.Count > 0)
        {
            EffectLog(owner, $"테이머 아래 {(faceDownOnly ? "뒷면 " : "")}카드 {trashed.Count}장을 아래에서부터 파기했습니다 ({string.Join(", ", hosts.Select(h => h.Name))}).");
            NotifyUnderTamersTrashed(owner, trashed, hosts);
        }
        return trashed;
    }

    /// <summary>테이머 tamer 아래의 맨 아래 카드 (faceDownOnly면 맨 아래 뒷면 카드)</summary>
    private static CardData? BottomUnder85(PlayerZones owner, CardData tamer, bool faceDownOnly)
    {
        for (int i = owner.UnderTamers.Count - 1; i >= 0; i--)
        {
            var x = owner.UnderTamers[i];
            if (!ReferenceEquals(owner.HostOfUnderTamer(x), tamer)) continue;
            if (faceDownOnly && !owner.IsFaceDownUnderTamer(x)) continue;
            return x;
        }
        return null;
    }

    /// <summary>
    /// 종합 룰 4-7-8: 필드를 떠난 테이머 아래에 남은 카드를 트래시로 보낸다 (테이머가 떠난 곳마다 부른다).
    /// late = RecalculateFieldBuffs의 안전망에서 부른 것 (진단 — 0이어야 한다)
    /// </summary>
    internal void TrashOrphanUnderTamers85(PlayerZones zones, bool late = false)
    {
        if (zones.UnderTamers.Count == 0)
        {
            if (zones.UnderTamerHosts85.Count > 0) zones.UnderTamerHosts85.Clear();
            return;
        }
        if (zones.UnderTamerHosts85.Count > zones.UnderTamers.Count)   // 효과가 목록에서 바로 뺀 카드 (등장 · 디지크로스 등)의 기록
            foreach (var k in zones.UnderTamerHosts85.Keys.Where(k => !zones.UnderTamers.Any(x => ReferenceEquals(x, k))).ToList()) zones.UnderTamerHosts85.Remove(k);
        var orphans = zones.UnderTamers.Where(x => zones.HostOfUnderTamer(x) == null).ToList();
        if (orphans.Count == 0) return;
        foreach (var x in orphans)
        {
            zones.RemoveUnderTamer85(x);
            zones.Trash.Add(x);
        }
        UnderTamerOrphansTrashed85 += orphans.Count;
        if (late) { UnderTamerLateSweeps85++; Diag46.Hit("underTamerLateSweep85"); }
        EffectLog(zones, $"테이머가 에어리어를 떠나 그 테이머 아래 카드 {orphans.Count}장을 트래시로 보냅니다 (종합 룰 4-7-8).");
    }

    /// <summary>
    /// 테이머 tamer 아래 카드를 전부 꺼낸다 (위 → 아래 순서) — 테이머에서 진화할 때 진화원으로 (종합 룰 3-4-5 · 4-3-2). 뒷면 여부를 함께 돌려준다
    /// </summary>
    internal List<(CardData Card, bool FaceDown)> TakeAllUnder85(PlayerZones zones, CardData tamer)
    {
        var list = zones.UnderOf(tamer).Select(x => (x, zones.IsFaceDownUnderTamer(x))).ToList();
        foreach (var (x, _) in list) zones.RemoveUnderTamer85(x);
        return list;
    }
}
