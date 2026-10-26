using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// 디지크로스 (효과 엔진 7단계): 디지크로스 조건을 가진 디지몬 카드를 패에서 등장시킬 때, 조건의 카드를 패 · 배틀 에어리어에서 이 카드 아래에 놓고
/// 1장마다 등장 코스트 -N. 조건은 영어 데이터에 없어서 공식 한글 문구("디지크로스 -2:「A」×「B」")를 실행 중에만 읽는다 — 한글 데이터가 없으면 디지크로스하지 않는다.
/// - 조건 카드 1칸마다 1장 (적혀 있으면 N장 · ∞장). 「A」/「B」는 그중 1장 (공식 Q&amp;A EX6-023 ~ 026)
/// - 배틀 에어리어의 디지몬을 놓으면 그 디지몬 카드만 아래에 놓고, 그 디지몬의 진화원 · 링크 카드는 트래시 (사용자 확인 대상)
/// - 놓는 순서: 조건의 왼쪽 칸이 위 (사용자 확인 대상)
/// - 트래시 · 테이머 아래 카드는 그것을 허락하는 효과가 있을 때만
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>디지크로스 조건 1칸 — Count장까지 (Distinct면 카드 번호가 서로 달라야 한다)</summary>
    internal sealed record XrosSlot(Func<CardData, bool> Match, int Count, bool Distinct, string Text);

    internal sealed record XrosRequirement(int Reduction, List<XrosSlot> Slots);

    private static readonly ConcurrentDictionary<string, XrosRequirement?> XrosCache = new(StringComparer.Ordinal);

    /// <summary>카드의 디지크로스 조건 (공식 한글 문구가 없거나 읽지 못하면 null)</summary>
    internal static XrosRequirement? DigiXrosOf(CardData card) =>
        card.Type != CardType.Digimon ? null : XrosCache.GetOrAdd(card.CardId, _ => ParseKoreanXros(card));

    /// <summary>디지크로스 조건이 있는지 (화면 · 효과 문구 "card with DigiXros requirements")</summary>
    public static bool HasDigiXros(CardData card) => DigiXrosOf(card) != null;

    // 방금 디지크로스로 등장한 디지몬과 놓은 장수 — 그 [등장 시] 동안 "if DigiXrosing (with N cards)"
    private (CardInstance? Digimon, int Count) _xrosPlay;

    /// <summary>이 디지몬이 지금 디지크로스로 등장하는 중이면 놓은 장수, 아니면 0</summary>
    internal int XrosCountOf(CardInstance? digimon) => digimon != null && ReferenceEquals(_xrosPlay.Digimon, digimon) ? _xrosPlay.Count : 0;

    /// <summary>재료 후보 1장 — Instance가 있으면 배틀 에어리어의 디지몬, Zone이 있으면 그 영역의 카드</summary>
    private sealed record XrosCandidate(CardData Card, CardInstance? Instance, List<CardData>? Zone);

    /// <summary>
    /// 패에서 card를 등장시키기 전에 디지크로스 재료를 고르고 떼어 낸다 (아직 아래에 놓지 않음). 반환: 고른 재료(위 → 아래)
    /// 플레이어는 칸마다 고른다(추천 = 패의 카드). AI · 선택 창이 없으면 패의 카드만 쓴다 (필드의 디지몬은 남긴다).
    /// </summary>
    private List<CardData> TakeXrosMaterials(PlayerZones owner, CardData card)
    {
        var req = DigiXrosOf(card);
        var taken = new List<CardData>();
        if (req == null) return taken;
        bool ask = ShouldAskPlayer(owner);
        var pool = new List<XrosCandidate>();
        pool.AddRange(owner.Hand.Where(c => c.Type == CardType.Digimon).Select(c => new XrosCandidate(c, null, owner.Hand)));
        if (ask) pool.AddRange(owner.BattleArea.Where(d => !IsToken(d.TopCard)).Select(d => new XrosCandidate(d.TopCard, d, null)));
        if (CardScripts.For(card)?.XrosFromTrash is { } fromTrash && fromTrash(owner))
            pool.AddRange(owner.Trash.Where(c => c.Type == CardType.Digimon).Select(c => new XrosCandidate(c, null, owner.Trash)));
        // 테이머: "by suspending this Tamer, you may place cards from under your Tamers as digivolution cards for a DigiXros"
        var underTamer = owner.Tamers.Distinct().FirstOrDefault(t => owner.HasActiveTamer(t) && CardScripts.For(t)?.XrosUnderTamersFor is { } f && f(card));
        if (underTamer != null && owner.FaceUpUnderTamers.Any(c => c.Type == CardType.Digimon && req.Slots.Any(s => s.Match(c)))
            && AskYesNo(owner, null, $"[{underTamer.Name}]을(를) 레스트시켜 테이머 아래 카드도 [{card.Name}]의 디지크로스에 쓸까요?", recommended: true))
        {
            SuspendTamer(owner, underTamer);
            pool.AddRange(owner.FaceUpUnderTamers.Where(c => c.Type == CardType.Digimon).Select(c => new XrosCandidate(c, null, owner.UnderTamers)));
        }

        var used = new HashSet<XrosCandidate>();
        var picked = new List<XrosCandidate>();
        foreach (var slot in req.Slots)
        {
            var cands = pool.Where(p => !used.Contains(p) && slot.Match(p.Card)).ToList();
            if (cands.Count == 0) continue;
            int max = Math.Min(slot.Count, cands.Count);
            var chosen = new List<XrosCandidate>();
            if (ask)
            {
                var ranked = cands.OrderBy(p => p.Instance != null ? 1 : 0).ToList();   // 추천 = 패 → 트래시 · 테이머 아래 → 필드
                var idx = ChooseCardIndexes(owner, CardChoiceZone.Other, ranked.Select(p => p.Card).ToList(), max, upTo: true,
                    $"[{card.Name}] 디지크로스 ({slot.Text}) — 패 · 필드에서 이 카드 아래에 놓을 카드를 고르세요 (1장마다 코스트 -{req.Reduction}, 필드의 디지몬은 그 카드만 놓고 진화원은 트래시).");
                chosen = idx.Select(i => ranked[i]).ToList();
            }
            else
                chosen = cands.Take(max).ToList();
            foreach (var p in chosen)
            {
                if (slot.Distinct && picked.Any(x => x.Card.CardId == p.Card.CardId)) continue;
                used.Add(p);
                picked.Add(p);
            }
        }
        foreach (var p in picked)
        {
            if (p.Instance != null)
            {
                if (!owner.BattleArea.Remove(p.Instance)) continue;
                TrashLinks(owner, p.Instance);
                while (p.Instance.EvolutionSources.Count > 0) owner.Trash.Add(p.Instance.EvolutionSources.Pop());
                if (ReferenceEquals(CurrentAttacker, p.Instance)) CurrentAttacker = null;
            }
            else if (p.Zone == null || !p.Zone.Remove(p.Card)) continue;
            taken.Add(p.Card);
        }
        if (taken.Count > 0)
            EffectLog(owner, $"[디지크로스] [{card.Name}] 아래에 [{string.Join(", ", taken.Select(c => c.Name))}] — 코스트 -{req.Reduction * taken.Count}");
        return taken;
    }

    /// <summary>디지크로스 코스트 감소 (재료 장수 × N)</summary>
    private static int XrosReduction(CardData card, int materials) => (DigiXrosOf(card)?.Reduction ?? 0) * materials;

    /// <summary>디지크로스 재료를 새 디지몬의 진화원으로 놓는다 (왼쪽 칸이 위)</summary>
    private static void PlaceXrosMaterials(CardInstance digimon, List<CardData> materials)
    {
        foreach (var m in materials)
            digimon.AddBottomSource(m);
    }

    /// <summary>[등장 시]를 디지크로스 장수와 함께 처리 ("if DigiXrosing")</summary>
    private T WithXros<T>(CardInstance digimon, int count, Func<T> run)
    {
        var outer = _xrosPlay;
        _xrosPlay = count > 0 ? (digimon, count) : (null, 0);
        try { return run(); }
        finally { _xrosPlay = outer; }
    }

    // ---- 공식 한글 문구 읽기 (실행 중에만 — 저장하지 않는다) ----

    private static readonly Dictionary<string, CardColor> KoColors = new()
    {
        ["레드"] = CardColor.Red, ["블루"] = CardColor.Blue, ["옐로"] = CardColor.Yellow, ["그린"] = CardColor.Green,
        ["블랙"] = CardColor.Black, ["퍼플"] = CardColor.Purple, ["화이트"] = CardColor.White,
    };

    private static XrosRequirement? ParseKoreanXros(CardData card)
    {
        var ko = KoreanCardData.Get(card.CardId);
        if (ko?.MainText is not { Length: > 0 } text) return null;
        var m = Regex.Match(text, @"디지크로스\s*-\s*(?<n>\d+)\s*[:：]\s*(?<req>.+?)(?=\s*등장할 때|$)");
        if (!m.Success) return null;
        int reduction = int.Parse(m.Groups["n"].Value);
        var slots = new List<XrosSlot>();
        foreach (var raw in Regex.Split(m.Groups["req"].Value, @"\s*×\s*|(?<=」)\s*[xX]\s*(?=「)"))
        {
            var slot = ParseXrosSlot(raw.Trim());
            if (slot == null) return null;   // 읽지 못한 칸이 있으면 디지크로스를 지원하지 않는다
            slots.Add(slot);
        }
        return slots.Count > 0 ? new XrosRequirement(reduction, slots) : null;
    }

    /// <summary>"「샤우트몬」" · "「아구몬」/「그레이몬」" · "블루인 「그레이몬」" · "「벰몬」 4장" · "특징으로 「바그라군」을 가진 디지몬 카드 2장" · "《세이브》가 기술되어 있는 디지몬 카드 1장" · "카드 넘버가 서로 다른 ~ ∞장"</summary>
    private static XrosSlot? ParseXrosSlot(string s)
    {
        string text = s;
        var filters = new List<Func<CardData, bool>> { c => c.Type == CardType.Digimon };
        bool distinct = false;
        int count = 1;
        Match m;
        if ((m = Regex.Match(s, @"카드 넘버가 서로 다른\s*")).Success) { distinct = true; s = s.Remove(m.Index, m.Length); }
        if ((m = Regex.Match(s, @"\s*(?<n>\d+|∞)\s*장$")).Success)
        {
            count = m.Groups["n"].Value == "∞" ? 99 : int.Parse(m.Groups["n"].Value);
            s = s.Remove(m.Index, m.Length);
        }
        s = Regex.Replace(s, @"\s*(?:의\s*)?디지몬 카드$", "").Trim();
        if ((m = Regex.Match(s, @"^(?<c>레드|블루|옐로|그린|블랙|퍼플|화이트)인\s*")).Success)
        {
            var color = KoColors[m.Groups["c"].Value];
            filters.Add(c => c.AllColors.Contains(color));
            s = s[m.Length..];
        }
        // 이름만: 「A」/「B」 — 정확히 그 명칭 (디지크로스에서 그 이름으로도 취급하는 카드 포함)
        if ((m = Regex.Match(s, @"^(?<x>「[^」]+」(?:\s*/\s*「[^」]+」)*)$")).Success)
        {
            var names = Regex.Matches(m.Groups["x"].Value, @"「([^」]+)」").Select(x => x.Groups[1].Value).ToList();
            filters.Add(c => names.Contains(KoreanCardData.DisplayName(c)) || XrosAliasNames(c).Any(names.Contains));
            return new XrosSlot(c => filters.All(f => f(c)), count, distinct, text);
        }
        if ((m = Regex.Match(s, @"《세이브》가 기술되어 있는")).Success)
        {
            filters.Add(c => $"{c.EffectText}\n{c.InheritedEffectText}".Contains("＜Save＞", StringComparison.Ordinal));
            s = s.Remove(m.Index, m.Length).Trim();
        }
        if ((m = Regex.Match(s, @"특징(?:으로)?\s*(?<t>「[^」]+」(?:\s*(?:/|또는)\s*「[^」]+」)*)\s*(?:을|를)?\s*(?:가진|가지는|포함하는)")).Success)
        {
            var traits = Regex.Matches(m.Groups["t"].Value, @"「([^」]+)」").Select(x => x.Groups[1].Value).ToList();
            filters.Add(c => KoreanCardData.DisplayTraits(c).Any(t => traits.Any(x => t.Contains(x, StringComparison.Ordinal))));
            s = s.Remove(m.Index, m.Length).Trim();
        }
        if ((m = Regex.Match(s, @"명칭에\s*(?<x>「[^」]+」(?:\s*/\s*「[^」]+」)*)\s*(?:을|를)?\s*포함하는")).Success)
        {
            var names = Regex.Matches(m.Groups["x"].Value, @"「([^」]+)」").Select(x => x.Groups[1].Value).ToList();
            filters.Add(c => names.Any(x => KoreanCardData.DisplayName(c).Contains(x, StringComparison.Ordinal)));
            s = s.Remove(m.Index, m.Length).Trim();
        }
        if ((m = Regex.Match(s, @"명칭이 서로 다른")).Success) { distinct = true; s = s.Remove(m.Index, m.Length).Trim(); }   // 카드 번호보다 약하지만 같은 명칭 2장은 막는다
        if ((m = Regex.Match(s, @"Lv\.\s*(?<n>\d+)\s*(?<dir>이하|이상)?\s*(?:의)?")).Success)
        {
            int lv = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value;
            filters.Add(c => dir == "이하" ? c.Level <= lv : dir == "이상" ? c.Level >= lv : c.Level == lv);
            s = s.Remove(m.Index, m.Length).Trim();
        }
        if (s.Length > 0) return null;
        return new XrosSlot(c => filters.All(f => f(c)), count, distinct, text);
    }

    /// <summary>디지크로스에서만 쓰는 다른 이름 (한글) — 영어 "also treated as [Shoutmon] for a DigiXros"를 공식 한글 명칭으로</summary>
    private static IEnumerable<string> XrosAliasNames(CardData card) =>
        CardScripts.For(card)?.XrosAliases.Select(KoreanEffectRenderer.NameKo) ?? Enumerable.Empty<string>();
}
