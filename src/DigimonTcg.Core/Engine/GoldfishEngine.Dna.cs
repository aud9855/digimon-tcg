using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.Engine.Ai;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// DNA 진화 (조그레스 · 효과 엔진 7단계): 지정된 자신의 디지몬 2마리를 겹쳐 패의 카드로 진화시키고 액티브로 한다.
/// 조건은 영어 데이터에 없어서 공식 한글 "〔조그레스〕블루 Lv.4+그린 Lv.4 : 코스트 0"을 실행 중에만 읽는다 — 한글 데이터가 없으면 DNA 진화하지 않는다.
/// - 메인 페이즈 행동(DnaDigivolve) · 효과("2 of your Digimon may DNA digivolve into ~ in the hand") 모두 조그레스 조건을 만족해야 한다 (공식 Q&amp;A)
/// - 겹치는 순서: 조건 왼쪽 디지몬의 카드 묶음이 위, 오른쪽 디지몬의 묶음이 아래 (사용자 확인 대상)
/// - 진화 보너스 1장 드로우 · [진화 시] 발휘 ("if DNA digivolving") · 등장한 턴 제한은 두 마리 중 먼저 나온 쪽
/// </summary>
public partial class GoldfishEngine
{
    internal sealed record DnaRequirement(Func<CardData, bool> Left, Func<CardData, bool> Right, int Cost, string Text);

    private static readonly ConcurrentDictionary<string, DnaRequirement?> DnaCache = new(StringComparer.Ordinal);

    /// <summary>카드의 조그레스 조건 (공식 한글 문구가 없거나 읽지 못하면 null)</summary>
    internal static DnaRequirement? DnaOf(CardData card) =>
        card.Type != CardType.Digimon ? null : DnaCache.GetOrAdd(card.CardId, _ => ParseKoreanDna(card));

    /// <summary>조그레스 조건이 있는지</summary>
    public static bool HasDna(CardData card) => DnaOf(card) != null;

    // 방금 DNA 진화한 디지몬 — 그 [진화 시] 동안 "if DNA digivolving"
    private CardInstance? _dnaDigivolving;

    internal bool IsDnaDigivolving(CardInstance? d) => d != null && ReferenceEquals(_dnaDigivolving, d);

    /// <summary>a · b로 card에 DNA 진화할 수 있는지 (순서는 상관없다 — 왼쪽 · 오른쪽 조건에 하나씩)</summary>
    private static bool DnaPairOk(DnaRequirement req, CardData card, CardInstance a, CardInstance b) =>
        !ReferenceEquals(a, b) && ((DnaLeft(req, card, a) && DnaRight(req, card, b)) || (DnaLeft(req, card, b) && DnaRight(req, card, a)));

    /// <summary>v0.37.0 조건 왼쪽 / 오른쪽에 맞는지 — "also treated as Lv.6 [X] for [Y]'s DNA digivolution"의 모습도 본다 (GoldfishEngine.Batch37.cs)</summary>
    private static bool DnaLeft(DnaRequirement req, CardData card, CardInstance d) => DnaFacets(d, card).Any(req.Left);
    private static bool DnaRight(DnaRequirement req, CardData card, CardInstance d) => DnaFacets(d, card).Any(req.Right);

    /// <summary>owner의 배틀 에어리어에서 card로 DNA 진화할 수 있는 두 마리 조합 (left = 조건 왼쪽)</summary>
    internal List<(CardInstance Left, CardInstance Right)> DnaPairs(PlayerZones owner, CardData card, Func<CardInstance, bool>? must = null)
    {
        var list = new List<(CardInstance, CardInstance)>();
        if (DnaOf(card) is not { } req) return list;
        var field = owner.BattleArea.Where(d => !IsToken(d.TopCard)).ToList();
        foreach (var l in field.Where(d => DnaLeft(req, card, d)))
            foreach (var r in field.Where(d => !ReferenceEquals(d, l) && DnaRight(req, card, d)))
                if (must == null || must(l) || must(r))
                    list.Add((l, r));
        return list;
    }

    /// <summary>패의 card로 DNA 진화할 수 있는 내 디지몬 조합 (메인 페이즈 — 화면이 쓴다)</summary>
    public IReadOnlyList<(CardInstance Left, CardInstance Right)> GetDnaPairs(CardData card) =>
        State.Player.Hand.Contains(card) ? DnaPairs(State.Player, card) : Array.Empty<(CardInstance, CardInstance)>();

    /// <summary>owner(플레이어 · AI)가 card로 DNA 진화할 수 있는 두 마리 조합 — AI 정책(ChooseDnaEvolution)이 쓴다</summary>
    public IReadOnlyList<(CardInstance Left, CardInstance Right)> GetDnaPairsFor(PlayerZones owner, CardData card) =>
        owner.Hand.Contains(card) ? DnaPairs(owner, card) : Array.Empty<(CardInstance, CardInstance)>();

    /// <summary>AI: 정책이 고른 DNA 진화를 검사하고 처리한다 (코스트는 AI 메모리에서). 반환: 처리했는지</summary>
    private bool AiDnaEvolve(AiDnaEvolution dna)
    {
        var opp = State.DummyOpponent;
        if (DnaOf(dna.Card) is not { } req || !opp.Hand.Contains(dna.Card) || !opp.BattleArea.Contains(dna.A) || !opp.BattleArea.Contains(dna.B) || !DnaPairOk(req, dna.Card, dna.A, dna.B))
            return false;
        // v0.42.0 진화할 수 없는 디지몬(지속 효과 "can't digivolve")이면 하지 않는다 — DnaDigivolveCore가 카드를 쓰지 않고 돌아오면 AI가 같은 DNA 진화를 끝없이 다시 고른다
        if (EffectRules.IsDigivolveLocked(State, dna.A) || EffectRules.IsDigivolveLocked(State, dna.B))
            return false;
        string names = $"{dna.A.TopCard.Name} + {dna.B.TopCard.Name}";
        int before = _aiTurn?.Memory ?? 0;
        int copiesBefore = opp.Hand.Count(c => ReferenceEquals(c, dna.Card));
        var logs = CaptureEffectLogs(() => DnaDigivolveCore(opp, dna.Card, dna.A, dna.B, req.Cost, opp.Hand));
        if (opp.Hand.Count(c => ReferenceEquals(c, dna.Card)) == copiesBefore)
            return false;   // 처리되지 않았다 (DNA 진화하려 할 때 효과로 디지몬이 떠났다 등)
        Log($"🤖 [AI DNA 진화] [{names}] -> [{dna.Card.Name}(Lv.{dna.Card.Level}, 코스트 {req.Cost})] (AI 메모리: +{before} → {_aiTurn?.Memory})");
        AddOpponentEvent(OpponentActionKind.Evolve, "DNA 진화", $"{names} → {dna.Card.Name} (Lv.{dna.Card.Level}) · 메모리 {before} → {_aiTurn?.Memory}", dna.Card);
        AddEffectEvent(dna.Card, logs);
        return true;
    }

    /// <summary>DNA 진화 코스트 (조건을 모르면 null)</summary>
    public static int? GetDnaCost(CardData card) => DnaOf(card)?.Cost;

    /// <summary>플레이어: 패의 card로 내 디지몬 a · b를 DNA 진화 (메인 페이즈, 코스트 지불). 메모리가 0 미만이 되면 턴이 끝난다</summary>
    public ActionResult DnaDigivolve(CardData card, CardInstance a, CardInstance b)
    {
        if (State.IsGameOver || State.CurrentPhase != Phase.Main || State.PendingBlock != null || State.BlitzAttacker != null)
            return ActionResult.Fail("지금은 DNA 진화할 수 없습니다.");
        if (!State.Player.Hand.Contains(card) || !State.Player.BattleArea.Contains(a) || !State.Player.BattleArea.Contains(b))
            return ActionResult.Fail("DNA 진화할 카드나 디지몬이 없습니다.");
        if (DnaOf(card) is not { } req)
            return ActionResult.Fail($"[{card.Name}]의 조그레스 조건을 알 수 없습니다 (공식 한글 데이터가 필요합니다).");
        if (!DnaPairOk(req, card, a, b))
            return ActionResult.Fail($"[{a.TopCard.Name}] · [{b.TopCard.Name}]은(는) [{card.Name}]의 조그레스 조건({req.Text})에 맞지 않습니다.");
        State.CanMulligan = false;
        var result = DnaDigivolveCore(State.Player, card, a, b, req.Cost, State.Player.Hand);
        CheckTurnEndAndAutoReset();
        return ActionResult.Success(result);
    }

    /// <summary>DNA 진화 처리 (검사 뒤): from에서 card를 빼고 코스트 지불 → 겹치기 → 액티브 → 드로우 → [진화 시]. 반환: 결과 메시지</summary>
    internal string DnaDigivolveCore(PlayerZones owner, CardData card, CardInstance a, CardInstance b, int cost, List<CardData> from)
    {
        var req = DnaOf(card)!;
        var (left, right) = DnaLeft(req, card, a) && DnaRight(req, card, b) ? (a, b) : (b, a);
        // "When this Digimon would DNA digivolve into ~" (두 마리 모두)
        RunEffects(EffectTrigger.ThisDigimonWouldDnaDigivolve, owner, left, left.TopCard, triggerCard: card);
        if (owner.BattleArea.Contains(right)) RunEffects(EffectTrigger.ThisDigimonWouldDnaDigivolve, owner, right, right.TopCard, triggerCard: card);
        if (!owner.BattleArea.Contains(left) || !owner.BattleArea.Contains(right)) return "DNA 진화할 디지몬이 없습니다.";
        if (EffectRules.IsDigivolveLocked(State, left) || EffectRules.IsDigivolveLocked(State, right)) return "진화할 수 없는 디지몬이 있어 DNA 진화할 수 없습니다.";
        if (!from.Remove(card)) return "DNA 진화할 카드가 없습니다.";
        NoteDnaEscape46(left, right);   // v0.46.0 떠나려던 디지몬으로 조그레스 진화 — 새 디지몬이라 떠나지 않는다 (공식 Q&A EX9-020 Q1)
        AddMemory(owner, -cost);
        if (ReferenceEquals(owner, State.Player)) { State.TotalMemorySpent += cost; State.TotalEvolutionsCount++; }
        // 오른쪽 디지몬의 카드 묶음(맨 위 → 아래)을 왼쪽 디지몬의 진화원 아래에 → 그 위에 DNA 카드
        owner.BattleArea.Remove(right);
        TrashLinks(owner, right);
        left.AddBottomSource(right.TopCard);
        foreach (var s in right.EvolutionSources.ToList()) { left.AddBottomSource(s); if (right.IsFaceDown(s)) left.FaceDownSources.Add(s); }   // v0.35.0 뒷면 진화원은 뒷면 그대로
        if (ReferenceEquals(CurrentAttacker, right)) CurrentAttacker = null;
        left.Evolve(card);
        left.IsSuspended = false;   // 액티브로 진화
        left.TurnPlayed = Math.Min(left.TurnPlayed, right.TurnPlayed);
        string msg = $"[DNA 진화] [{left.EvolutionSources.ElementAt(0).Name}] + [{right.TopCard.Name}] → [{card.Name}] (코스트 {cost})";
        EffectLog(owner, msg);
        DrawFor(owner);
        RecalculateFieldBuffs();
        if (owner.BattleArea.Contains(left))
        {
            var outer = _dnaDigivolving;
            _dnaDigivolving = left;
            try
            {
                ApplyWhenDigivolvingEffects(owner, left, card);
                FireForSide(EffectTrigger.AllyDigimonDigivolved, owner, left);
            }
            finally { _dnaDigivolving = outer; }
        }
        return msg;
    }

    // ---- 공식 한글 문구 읽기 (실행 중에만 — 저장하지 않는다) ----

    private static DnaRequirement? ParseKoreanDna(CardData card)
    {
        var ko = KoreanCardData.Get(card.CardId);
        if (ko?.MainText is not { Length: > 0 } text) return null;
        var m = Regex.Match(text, @"〔조그레스〕\s*(?<l>[^+＋:：]+?)\s*[+＋]\s*(?<r>[^:：]+?)\s*[:：]\s*코스트\s*(?<n>\d+)");
        if (!m.Success) return null;
        var left = ParseDnaSide(m.Groups["l"].Value.Trim());
        var right = ParseDnaSide(m.Groups["r"].Value.Trim());
        if (left == null || right == null) return null;
        return new DnaRequirement(left, right, int.Parse(m.Groups["n"].Value), $"{m.Groups["l"].Value.Trim()} + {m.Groups["r"].Value.Trim()}");
    }

    /// <summary>"블루/옐로 Lv.5" · "「키메라몬」" · "명칭에 「그레이몬」을 포함하는 Lv.6" · "「펜리루가몬」이 기술되어 있는 옐로인 Lv.6"</summary>
    private static Func<CardData, bool>? ParseDnaSide(string s)
    {
        var filters = new List<Func<CardData, bool>>();
        Match m;
        if ((m = Regex.Match(s, @"^「(?<x>[^」]+)」$")).Success)
        {
            string x = m.Groups["x"].Value;
            return c => KoreanCardData.DisplayName(c) == x;
        }
        if ((m = Regex.Match(s, @"명칭에\s*(?<x>「[^」]+」(?:\s*/\s*「[^」]+」)*)\s*(?:을|를)?\s*포함하는\s*")).Success)
        {
            var xs = Regex.Matches(m.Groups["x"].Value, @"「([^」]+)」").Select(x => x.Groups[1].Value).ToList();
            filters.Add(c => xs.Any(x => KoreanCardData.DisplayName(c).Contains(x, StringComparison.Ordinal)));
            s = s.Remove(m.Index, m.Length).Trim();
        }
        if ((m = Regex.Match(s, @"「(?<x>[^」]+)」\s*(?:이|가)\s*기술되어 있는\s*")).Success)
        {
            string x = m.Groups["x"].Value;
            filters.Add(c => KoreanCardData.DisplayName(c).Contains(x, StringComparison.Ordinal)
                             || (KoreanCardData.Get(c.CardId) is { } k && $"{k.MainText}{k.SourceText}".Contains(x, StringComparison.Ordinal)));
            s = s.Remove(m.Index, m.Length).Trim();
        }
        if ((m = Regex.Match(s, @"^(?<c>(?:레드|블루|옐로|그린|블랙|퍼플|화이트)(?:\s*/\s*(?:레드|블루|옐로|그린|블랙|퍼플|화이트))*)(?:인)?\s*")).Success)
        {
            var colors = Regex.Matches(m.Groups["c"].Value, "레드|블루|옐로|그린|블랙|퍼플|화이트").Select(x => KoColors[x.Value]).ToList();
            filters.Add(c => c.AllColors.Any(colors.Contains));
            s = s[m.Length..].Trim();
        }
        if ((m = Regex.Match(s, @"^Lv\.\s*(?<n>\d+)$")).Success)
        {
            int lv = int.Parse(m.Groups["n"].Value);
            filters.Add(c => c.Level == lv);
            s = "";
        }
        if (s.Length > 0 || filters.Count == 0) return null;
        return c => c.Type == CardType.Digimon && filters.All(f => f(c));
    }
}
