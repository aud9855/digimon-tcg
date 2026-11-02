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
/// v0.84.0 어셈블리 (종합 룰 2-3-10 · 6-5-1-1-3 · 7-3): 「어셈블리 조건」을 가진 디지몬 카드를 등장시킬 때, 조건에서 지정된 자신의 트래시의 카드를
/// 지정된 장수만큼 이 카드 아래에 놓아 조건의 수치만큼 등장 코스트를 마이너스한다.
/// 조건은 영어 API 데이터에 없어서 공식 영어 카드 리스트의 [Special Play Condition] "Assembly -6: 4 [Negamon]" 줄을 읽는다
/// (로컬 data/en/card_rules.json — tools/ensite/collect_en.ps1, 공식 일본어 카드 리스트 [特殊登場]과 같다). 파일이 없으면 어셈블리하지 않는다.
///  - 7-3-2-1 · 7-3-2-4: 지정된 장수를 전부 놓아야 하고 (일부만은 안 된다) 코스트는 놓은 장수와 관계없이 수치만큼 (디지크로스처럼 1장마다가 아니다)
///  - 7-3-2-2 · 7-3-3-1: 선언은 등장 코스트 지불 직전 — 등장할 때 유발한 효과("by ~, reduce the play cost")가 먼저
///  - 7-3-2-5: 조건에 적힌 카드는 각각 1장 (장수가 적혀 있으면 그 장수) · 7-3-2-6: 왼쪽에 적힌 카드가 위 (같은 칸 · 장수 지정은 플레이어가 고른 순서)
///  - 7-3-2-8: 등장할 수 없게 되면 아래에 놓인 카드는 파기 (트래시) · 7-3-2-9: 강제가 아니다 · 7-3-2-10: 효과로 등장시킬 때도 (Q&amp;A P-205 Q5397 · BT26-079 Q7110)
///  - "Lv.5 × Lv.4 × Lv.3, all ~": 3장 모두 그 조건 (공식 Q&amp;A EX12-017 Q6743 · EX13-014 Q7246 등) · Lv.이 없는 카드는 Lv. 조건에 맞지 않는다 (Q&amp;A BT26-085 Q7210)
///  - 공개 중인 카드는 아직 트래시에 없으므로 쓸 수 없다 (Q&amp;A EX12-058 Q7214) — 트래시의 카드만 후보
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>끄면 v0.83.0까지처럼 어셈블리하지 않는다 — 시뮬레이션 비교용</summary>
    public static bool Assembly84 { get; set; } = true;

    /// <summary>서로 달라야 하는 것 ("w/different names · levels · colors")</summary>
    internal enum AsmDistinct84 { None, Name, Level, Color }

    /// <summary>어셈블리 조건 1칸 — Count장 (Distinct면 고른 카드끼리 서로 달라야 한다). Ko = 화면 한글</summary>
    internal sealed record AsmSlot84(Func<CardData, bool> Match, int Count, AsmDistinct84 Distinct, string Ko);

    /// <summary>어셈블리 조건: 코스트 -Reduction · 칸 (여러 칸이면 칸마다 1장 — "A×B", "Lv.5×Lv.4×Lv.3") · 공식 영어 줄 · 화면 한글</summary>
    internal sealed record AsmRequirement84(int Reduction, List<AsmSlot84> Slots, string English, string Ko)
    {
        public int Total => Slots.Sum(s => s.Count);
    }

    private static readonly ConcurrentDictionary<string, AsmRequirement84?> AsmCache84 = new(StringComparer.Ordinal);

    /// <summary>카드의 어셈블리 조건 (공식 영어 줄이 없거나 읽지 못하면 null)</summary>
    internal static AsmRequirement84? AssemblyOf84(CardData card)
    {
        if (!Assembly84 || card.Type != CardType.Digimon || card.IsDual || card.IsTamerProxy) return null;
        if (EnglishCardRules.Get(card.CardId)?.Assembly is not { Length: > 0 } line) return null;
        return AsmCache84.GetOrAdd(card.CardId + "\n" + card.Name + "\n" + line, _ => ParseAssembly84(line, card));
    }

    /// <summary>어셈블리 조건이 있는지 (화면 · 덱 빌더)</summary>
    public static bool HasAssembly84(CardData card) => AssemblyOf84(card) != null;

    /// <summary>화면 한글: "어셈블리 -6: 「네가몬」 4장" + 주석 (종합 룰 2-3-10-2). 조건이 없으면 null</summary>
    public static string? AssemblyDisplayKo84(CardData card) =>
        AssemblyOf84(card) is { } req ? $"어셈블리 -{req.Reduction}: {req.Ko} (등장할 때, 트래시에서 지정된 장수를 이 카드 아래에 놓는 것으로, 등장 코스트를 마이너스한다)" : null;

    // 방금 어셈블리로 등장한 디지몬 (효과 등장에서 놓을 카드)
    private List<CardData>? _pendingAsm84;

    /// <summary>
    /// 등장 코스트 지불 직전 (7-3-3-2): 어셈블리할지 정하고, 하면 트래시에서 조건의 카드를 떼어 낸다 (아직 아래에 놓지 않음). 반환: 고른 카드 (위 → 아래) · 줄일 코스트.
    /// paying: 코스트를 내는 등장인지 (코스트 없이 등장하면 코스트 감소는 의미가 없다 — AI는 하지 않고, 플레이어에게는 묻는다), cost: 지금까지 줄인 코스트
    /// </summary>
    private (List<CardData> Taken, int Reduce) TakeAssembly84(PlayerZones owner, CardData card, bool paying, int cost)
    {
        var none = (new List<CardData>(), 0);
        if (AssemblyOf84(card) is not { } req) return none;
        if (paying && PlayCostReductionBlocked(owner, card)) return none;   // "Players can't reduce play costs" — 어셈블리는 등장 코스트를 마이너스하는 룰 (7-3-1)
        var pool = owner.Trash.ToList();
        var auto = AutoPickAsm84(req, pool);
        if (auto == null) return none;   // 조건을 다 채울 수 없으면 선언할 수 없다 (7-3-2-4)
        bool ask = ShouldAskPlayer(owner);
        if (!ask && (!paying || cost <= 0)) return none;   // AI: 코스트를 실제로 줄일 때만 (트래시의 카드를 남긴다)
        int reduce = paying ? Math.Min(req.Reduction, Math.Max(0, cost)) : 0;
        if (ask && !AskYesNo(owner, null,
                $"[{card.Name}]을(를) 어셈블리할까요? (어셈블리 -{req.Reduction}: {req.Ko} — 트래시에서 {req.Total}장을 이 카드 아래에 놓는다{(paying ? $", 등장 코스트 -{req.Reduction}" : ", 코스트 없이 등장하므로 코스트 감소는 없음")}, 종합 룰 7-3)",
                recommended: true))
            return none;
        var picked = ask ? AskAsmPicks84(owner, card, req, pool, auto) : auto;
        var taken = new List<CardData>();
        foreach (var x in picked)
            if (owner.Trash.Remove(x)) taken.Add(x);
        if (taken.Count != picked.Count)   // 고르는 사이 트래시가 바뀌었다 — 하지 않은 것으로 (7-3-2-7)
        {
            owner.Trash.AddRange(taken);
            return none;
        }
        Diag46.Hit(paying ? "assemblyPaid84" : "assemblyFree84");
        EffectLog(owner, $"[어셈블리] [{card.Name}] 아래에 트래시의 [{string.Join(", ", taken.Select(c => c.Name))}] — {(paying ? $"등장 코스트 -{req.Reduction}" : "코스트 없이 등장")}");
        return (taken, reduce);
    }

    /// <summary>플레이어: 칸마다 고른다 (여러 칸 — 칸마다 1장, 한 칸 N장 — 한 번에 N장). 조건에 맞지 않게 고르면 추천(auto)</summary>
    private List<CardData> AskAsmPicks84(PlayerZones owner, CardData card, AsmRequirement84 req, List<CardData> pool, List<CardData> auto)
    {
        if (req.Slots.Count == 1)
        {
            var slot = req.Slots[0];
            var cands = pool.Where(slot.Match).ToList();
            var ranked = auto.Concat(cands.Where(c => !auto.Any(a => ReferenceEquals(a, c)))).ToList();   // 추천 = 앞의 N장
            string rule = slot.Distinct switch { AsmDistinct84.Name => " · 명칭이 서로 달라야 한다", AsmDistinct84.Level => " · Lv.이 서로 달라야 한다", AsmDistinct84.Color => " · 색이 서로 달라야 한다", _ => "" };
            var idx = ChooseCardIndexes(owner, CardChoiceZone.Trash, ranked, slot.Count, upTo: false,
                $"[{card.Name}] 어셈블리 -{req.Reduction}: {slot.Ko} — 트래시에서 이 카드 아래에 놓을 카드 {slot.Count}장을 고르세요{rule} (고른 순서대로 위에서부터).");
            var pick = idx.Select(i => ranked[i]).ToList();
            return ValidAsm84(req, pick) ? pick : auto;
        }
        var chosen = new List<CardData>();
        for (int k = 0; k < req.Slots.Count; k++)
        {
            var slot = req.Slots[k];
            var rest = req.Slots.Skip(k + 1).ToList();
            var cands = pool.Where(c => !chosen.Any(x => ReferenceEquals(x, c)) && slot.Match(c)
                && AssignAsm84(rest, pool.Where(p => !ReferenceEquals(p, c) && !chosen.Any(x => ReferenceEquals(x, p))).ToList()) != null).ToList();
            if (cands.Count == 0) return auto;
            var rec = auto.Count > k && cands.Any(c => ReferenceEquals(c, auto[k])) ? auto[k] : cands[0];
            var ranked = new[] { rec }.Concat(cands.Where(c => !ReferenceEquals(c, rec))).ToList();
            var idx = ChooseCardIndexes(owner, CardChoiceZone.Trash, ranked, 1, upTo: false,
                $"[{card.Name}] 어셈블리 -{req.Reduction}: {req.Ko} — {k + 1}번째 칸 ({slot.Ko}) 카드를 트래시에서 고르세요.");
            chosen.Add(idx.Count == 1 ? ranked[idx[0]] : rec);
        }
        return ValidAsm84(req, chosen) ? chosen : auto;
    }

    /// <summary>조건을 다 채우는 카드 (위 → 아래). 채울 수 없으면 null. 추천: 트래시 위(나중에 놓인 카드)부터</summary>
    internal static List<CardData>? AutoPickAsm84(AsmRequirement84 req, IReadOnlyList<CardData> trash)
    {
        var pool = trash.Reverse().ToList();
        if (req.Slots.Count == 1) return PickDistinct84(req.Slots[0], pool);
        return AssignAsm84(req.Slots, pool);
    }

    /// <summary>여러 칸(칸마다 1장): 칸 순서대로 서로 다른 카드를 하나씩 (백트래킹 — 칸은 4개까지)</summary>
    private static List<CardData>? AssignAsm84(IReadOnlyList<AsmSlot84> slots, IReadOnlyList<CardData> pool)
    {
        var result = new CardData[slots.Count];
        var used = new HashSet<CardData>(ReferenceEqualityComparer.Instance);
        bool Go(int k)
        {
            if (k == slots.Count) return true;
            foreach (var c in pool)
            {
                if (used.Contains(c) || !slots[k].Match(c)) continue;
                used.Add(c); result[k] = c;
                if (Go(k + 1)) return true;
                used.Remove(c);
            }
            return false;
        }
        return Go(0) ? result.ToList() : null;
    }

    /// <summary>한 칸 N장: 조건에 맞는 카드 중 서로 다른(명칭 · Lv. · 색) N장</summary>
    private static List<CardData>? PickDistinct84(AsmSlot84 slot, IReadOnlyList<CardData> pool)
    {
        var cands = pool.Where(slot.Match).ToList();
        if (slot.Distinct == AsmDistinct84.None) return cands.Count >= slot.Count ? cands.Take(slot.Count).ToList() : null;
        if (slot.Distinct is AsmDistinct84.Name or AsmDistinct84.Level)
        {
            var pick = cands.GroupBy(c => DistinctKey84(slot.Distinct, c)).Select(g => g.First()).Take(slot.Count).ToList();
            return pick.Count == slot.Count ? pick : null;
        }
        // 색: 카드마다 서로 다른 색 하나씩 (여러 색 카드는 그중 하나 — 공식 Q&A EX9-074 Q5004 · Q5005의 "색이 서로 다른"과 같은 셈). 7색이라 최대 매칭
        var chosen = new List<CardData>();
        var colorOf = new Dictionary<CardColor, CardData>();
        foreach (var c in cands.OrderBy(c => c.AllColors.Count))
        {
            if (chosen.Count == slot.Count) break;
            if (TryAugment84(c, colorOf, new HashSet<CardColor>())) chosen.Add(c);
        }
        return chosen.Count == slot.Count ? chosen : null;
    }

    private static bool TryAugment84(CardData c, Dictionary<CardColor, CardData> colorOf, HashSet<CardColor> seen)
    {
        foreach (var col in c.AllColors.Distinct())
        {
            if (!seen.Add(col)) continue;
            if (!colorOf.TryGetValue(col, out var other) || TryAugment84(other, colorOf, seen))
            {
                colorOf[col] = c;
                return true;
            }
        }
        return false;
    }

    private static string DistinctKey84(AsmDistinct84 d, CardData c) => d == AsmDistinct84.Level ? c.Level.ToString() : CardNames.MatchName(c);

    /// <summary>고른 카드가 조건을 정확히 채우는지 (서로 다른 카드 · 장수 · 칸 · 서로 다름)</summary>
    internal static bool ValidAsm84(AsmRequirement84 req, IReadOnlyList<CardData> pick)
    {
        if (pick.Count != req.Total || pick.Distinct(ReferenceEqualityComparer.Instance).Count() != pick.Count) return false;
        if (req.Slots.Count > 1) return pick.Select((c, i) => req.Slots[i].Match(c)).All(x => x);
        var slot = req.Slots[0];
        if (!pick.All(slot.Match)) return false;
        return slot.Distinct switch
        {
            AsmDistinct84.Name or AsmDistinct84.Level => pick.Select(c => DistinctKey84(slot.Distinct, c)).Distinct().Count() == pick.Count,
            AsmDistinct84.Color => PickDistinct84(slot with { Count = pick.Count }, pick) != null,
            _ => true,
        };
    }

    /// <summary>AI 미리보기: 지금 패에서 등장시키면 어셈블리로 줄일 코스트 (할 수 없으면 0)</summary>
    public int PreviewAssemblyReduction84(PlayerZones owner, CardData card) =>
        AssemblyOf84(card) is { } req && !PlayCostReductionBlocked(owner, card) && AutoPickAsm84(req, owner.Trash) != null ? req.Reduction : 0;

    /// <summary>어셈블리한 카드를 새 디지몬 아래에 놓는다 (왼쪽 칸 · 고른 순서가 위 — 7-3-2-6)</summary>
    private static void PlaceAsm84(CardInstance digimon, List<CardData> taken)
    {
        foreach (var c in taken) digimon.AddBottomSource(c);
    }

    /// <summary>효과 등장 (TryPlayByEffect): 어셈블리하면 코스트를 줄이고 놓을 카드를 PlayWithoutCostCore에 넘긴다. 반환: 줄인 뒤의 코스트 (코스트 없이면 null)</summary>
    private int? EffectPlayAssembly84(PlayerZones owner, CardData card, int? payCost)
    {
        var (taken, reduce) = TakeAssembly84(owner, card, payCost != null, payCost ?? 0);
        if (taken.Count == 0) return payCost;
        _pendingAsm84 = taken;
        return payCost is int cost ? Math.Max(0, cost - reduce) : null;
    }

    /// <summary>놓지 못한 어셈블리 카드 (등장하지 않았다 — 7-3-2-8 파기)</summary>
    private void DropPendingAsm84(PlayerZones owner)
    {
        if (_pendingAsm84 is { } xs) owner.Trash.AddRange(xs);
        _pendingAsm84 = null;
    }

    // ---- 공식 영어 조건 읽기 ----

    private const RegexOptions I84 = RegexOptions.IgnoreCase;
    private static readonly string[] AsmColors84 = { "red", "blue", "yellow", "green", "black", "purple", "white" };
    private static readonly Dictionary<string, string> AsmColorKo84 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "레드", ["blue"] = "블루", ["yellow"] = "옐로", ["green"] = "그린", ["black"] = "블랙", ["purple"] = "퍼플", ["white"] = "화이트",
    };

    /// <summary>"Assembly -6: 4 [Negamon]" → 조건. 읽지 못한 부분이 있으면 null (어셈블리하지 않는다)</summary>
    internal static AsmRequirement84? ParseAssembly84(string line, CardData card)
    {
        var m = Regex.Match(line.Trim(), @"^Assembly\s*-\s*(?<n>\d+)\s*[:：]\s*(?<req>.+?)\s*$", I84);
        if (!m.Success) return null;
        int reduction = int.Parse(m.Groups["n"].Value);
        string req = m.Groups["req"].Value.Trim();
        var slots = new List<AsmSlot84>();
        string ko;
        Match lm;
        if ((lm = Regex.Match(req, @"^(?<lv>Lv\.\s*\d+(?:\s*×\s*Lv\.\s*\d+)+)\s*,\s*all\s+(?<common>.+)$", I84)).Success)
        {
            // "Lv.5 × Lv.4 × Lv.3, all w/[Agumon]/[Greymon] in name or w/[ME]/[VB] trait" — 3칸 모두 공통 조건
            var common = ParseAsmDesc84(lm.Groups["common"].Value, card);
            if (common == null) return null;
            var levels = Regex.Matches(lm.Groups["lv"].Value, @"\d+").Select(x => int.Parse(x.Value)).ToList();
            foreach (int lv in levels)
            {
                var f = common.Value.Match;
                slots.Add(new AsmSlot84(c => LevelIn84(c, card, l => l == lv) && f(c), 1, AsmDistinct84.None, $"Lv.{lv}"));
            }
            ko = $"{common.Value.Ko}, {string.Join("×", levels.Select(l => $"Lv.{l}"))}";
        }
        else if (req.Contains('×'))
        {
            // "[WarGreymon]×[MetalGarurumon]×[Agumon]×[Gabumon]" — 칸마다 그 명칭 1장
            var kos = new List<string>();
            foreach (var part in req.Split('×').Select(p => p.Trim()))
            {
                if (!Regex.IsMatch(part, @"^\[[^\]]+\](?:/\[[^\]]+\])*$")) return null;
                var names = Brackets84(part);
                slots.Add(new AsmSlot84(c => names.Any(n => CardNames.Is(c, n)), 1, AsmDistinct84.None, NamesKo84(names)));
                kos.Add(NamesKo84(names));
            }
            ko = string.Join("×", kos);
        }
        else
        {
            string s = req;
            int count = 1;
            var distinct = AsmDistinct84.None;
            if ((lm = Regex.Match(s, @"^(?<n>\d+)\s+")).Success) { count = int.Parse(lm.Groups["n"].Value); s = s[lm.Length..]; }
            if ((lm = Regex.Match(s, @"\s*w/different (?<k>names|levels|colors)$", I84)).Success)
            {
                distinct = lm.Groups["k"].Value.ToLowerInvariant() switch { "names" => AsmDistinct84.Name, "levels" => AsmDistinct84.Level, _ => AsmDistinct84.Color };
                s = s[..lm.Index];
            }
            if ((lm = Regex.Match(s, @"^different-level\s+", I84)).Success) { distinct = AsmDistinct84.Level; s = s[lm.Length..]; }
            (Func<CardData, bool> Match, string Ko)? desc;
            if ((lm = Regex.Match(s, @"^(?<n>\[[^\]]+\])/(?<rest>(?:Digimon|Tamer|Option) cards?\b.*)$", I84)).Success)
            {
                // "[Blimpmon]/Tamer card w/[TS] trait" — 그 명칭 또는 다른 조건
                var names = Brackets84(lm.Groups["n"].Value);
                var other = ParseAsmDesc84(lm.Groups["rest"].Value, card);
                if (other == null) return null;
                var f = other.Value.Match;
                desc = (c => names.Any(n => CardNames.Is(c, n)) || f(c), $"{NamesKo84(names)}/{other.Value.Ko}");
            }
            else desc = ParseAsmDesc84(s, card);
            if (desc == null) return null;
            var match = desc.Value.Match;
            Func<CardData, bool> m2 = distinct == AsmDistinct84.Level ? c => c.Level > 0 && match(c) : match;   // Lv.이 없는 카드는 "Lv.이 서로 다른"에 들지 않는다 (Q&A BT26-085 Q7210)
            string pre = distinct switch { AsmDistinct84.Name => "명칭이 서로 다른 ", AsmDistinct84.Level => "Lv.이 서로 다른 ", AsmDistinct84.Color => "색이 서로 다른 ", _ => "" };
            string slotKo = $"{pre}{desc.Value.Ko}{(count > 1 ? $" {count}장" : "")}";
            slots.Add(new AsmSlot84(m2, count, distinct, slotKo));
            ko = slotKo;
        }
        return slots.Count > 0 ? new AsmRequirement84(reduction, slots, line.Trim(), ko) : null;
    }

    /// <summary>
    /// 카드 조건 한 덩어리: "level 5 [Cyborg] trait Digimon cards" · "Lv.4 or lower w/[Agumon]/[Greymon] in name or w/[ME]/[VB] trait" · "green Digimon cards w/[Maquinamon] in text" ·
    /// "[Huckmon] text Digimon cards" · "yellow w/[Holy Beast] trait" · "black w/＜Blocker＞" · "[Plutomon]" · "[Gokuumon]/[Sagomon]". 읽지 못하면 null
    /// </summary>
    private static (Func<CardData, bool> Match, string Ko)? ParseAsmDesc84(string desc, CardData card)
    {
        string s = desc.Trim();
        var filters = new List<Func<CardData, bool>>();
        string lvKo = "", colorKo = "", typeKo = "카드", condKo = "", namesKo = "";
        Match m;
        bool any = false;
        for (int guard = 0; s.Length > 0 && guard < 12; guard++)
        {
            if ((m = Regex.Match(s, @"^(?:level|Lv\.)\s*(?<n>\d+)(?<dir> or (?:lower|less|higher|more))?\s*", I84)).Success)
            {
                int lv = int.Parse(m.Groups["n"].Value);
                string dir = m.Groups["dir"].Value.Trim().ToLowerInvariant();
                bool lower = dir.EndsWith("lower") || dir.EndsWith("less"), higher = dir.EndsWith("higher") || dir.EndsWith("more");
                filters.Add(c => LevelIn84(c, card, l => lower ? l <= lv : higher ? l >= lv : l == lv));
                lvKo = $"Lv.{lv}{(lower ? " 이하" : higher ? " 이상" : "")}";
            }
            else if ((m = Regex.Match(s, $@"^(?<c>{string.Join("|", AsmColors84)})\s+", I84)).Success)
            {
                var col = Enum.Parse<CardColor>(m.Groups["c"].Value, ignoreCase: true);
                filters.Add(c => c.AllColors.Contains(col));
                colorKo = AsmColorKo84[m.Groups["c"].Value];
            }
            else if ((m = Regex.Match(s, @"^(?<t>Digimon|Tamer|Option) cards?\s*", I84)).Success)
            {
                var type = Enum.Parse<CardType>(m.Groups["t"].Value, ignoreCase: true);
                filters.Add(c => c.Type == type && !c.IsDual);
                typeKo = type switch { CardType.Digimon => "디지몬 카드", CardType.Tamer => "테이머 카드", _ => "옵션 카드" };
            }
            else if ((m = Regex.Match(s, @"^cards?\s*", I84)).Success) { typeKo = "카드"; }
            else if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?:/\[[^\]]+\])*) trait\s*", I84)).Success)
            {
                var traits = Brackets84(m.Groups["x"].Value);
                filters.Add(c => traits.Any(t => TargetSpec.HasTrait(c, t + CardNames.TraitExactMark72)));   // 「X」 특징을 가진 = 일치 (종합 룰 2-3-2-3)
                condKo = Join84(condKo, $"특징 {P84(TraitsKo84(traits), "을", "를")} 가진");
            }
            else if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?:/\[[^\]]+\])*) text\s*", I84)).Success)
            {
                var xs = Brackets84(m.Groups["x"].Value);
                filters.Add(c => xs.Any(x => TextHas84(c, x)));
                condKo = Join84(condKo, $"{P84(NamesKo84(xs), "이", "가")} 기술되어 있는");
            }
            else if ((m = Regex.Match(s, @"^w/(?<w>.+)$", I84)).Success)
            {
                // "w/[A]/[B] in name or w/[C]/[D] trait" — 어느 하나 (공식 Q&A EX12-048 Q6823: 앞의 Lv. · 종류는 둘 다에 걸린다)
                var alts = new List<Func<CardData, bool>>();
                var kos = new List<string>();
                foreach (var alt in Regex.Split(m.Groups["w"].Value, @"\s+or\s+w/", I84))
                {
                    var one = ParseAsmWith84(alt.Trim());
                    if (one == null) return null;
                    alts.Add(one.Value.Match);
                    kos.Add(one.Value.Ko);
                }
                filters.Add(c => alts.Any(f => f(c)));
                condKo = Join84(condKo, kos.Count == 1 ? kos[0] : string.Join("", kos.Take(kos.Count - 1).Select(k => KoOr84(k))) + kos[^1]);
                m = Match.Empty;
                s = "";
                any = true;
                continue;
            }
            else if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?:/\[[^\]]+\])*)\s*", I84)).Success)
            {
                var names = Brackets84(m.Groups["x"].Value);
                filters.Add(c => names.Any(n => CardNames.Is(c, n)));
                namesKo = NamesKo84(names);
            }
            else return null;
            s = s[m.Length..].TrimStart();
            any = true;
        }
        if (!any || s.Length > 0) return null;
        // 한글: "명칭에 「아구몬」을 포함하거나 특징 「ME」를 가진 옐로 Lv.4 이하의 디지몬 카드" / "「네가몬」"
        string ko;
        if (namesKo.Length > 0 && condKo.Length == 0 && lvKo.Length == 0 && colorKo.Length == 0 && typeKo == "카드") ko = namesKo;
        else
        {
            var parts = new List<string>();
            if (condKo.Length > 0) parts.Add(condKo);
            if (namesKo.Length > 0) parts.Add(namesKo);
            if (colorKo.Length > 0) parts.Add(colorKo + (lvKo.Length > 0 || typeKo.Length > 0 ? "인" : ""));
            string tail = lvKo.Length > 0 ? $"{lvKo}의 {typeKo}" : typeKo;
            parts.Add(tail);
            ko = string.Join(" ", parts.Where(p => p.Length > 0));
        }
        var all = filters.ToList();
        return (c => all.All(f => f(c)), ko);
    }

    /// <summary>"w/" 뒤 하나: "[A]/[B] in name" · "[A] in text" · "[A]/[B] in any trait" · "[A]/[B] trait" · "＜Blocker＞"</summary>
    private static (Func<CardData, bool> Match, string Ko)? ParseAsmWith84(string s)
    {
        Match m;
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?:/\[[^\]]+\])*) in (?:its |their )?names?$", I84)).Success)
        {
            var xs = Brackets84(m.Groups["x"].Value);
            return (c => xs.Any(x => CardNames.Contains(c, x)), $"명칭에 {P84(NamesKo84(xs), "을", "를")} 포함하는");
        }
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?:/\[[^\]]+\])*) in (?:its |their )?text$", I84)).Success)
        {
            var xs = Brackets84(m.Groups["x"].Value);
            return (c => xs.Any(x => TextHas84(c, x)), $"{P84(NamesKo84(xs), "이", "가")} 기술되어 있는");
        }
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?:/\[[^\]]+\])*) in any (?:of its )?traits?$", I84)).Success)
        {
            var xs = Brackets84(m.Groups["x"].Value);
            return (c => xs.Any(x => TargetSpec.HasTrait(c, x)), $"특징에 {P84(TraitsKo84(xs), "을", "를")} 포함하는");   // 포함 (종합 룰 2-3-2-4)
        }
        if ((m = Regex.Match(s, @"^(?<x>\[[^\]]+\](?:/\[[^\]]+\])*) traits?$", I84)).Success)
        {
            var xs = Brackets84(m.Groups["x"].Value);
            return (c => xs.Any(x => TargetSpec.HasTrait(c, x + CardNames.TraitExactMark72)), $"특징 {P84(TraitsKo84(xs), "을", "를")} 가진");
        }
        if (Regex.IsMatch(s, @"^＜Blocker＞$|^<Blocker>$", I84))
            return (c => c.IsBlocker || c.EffectText.Contains("＜Blocker＞", StringComparison.Ordinal), "≪블로커≫를 가진");
        return null;
    }

    /// <summary>「X」이(가) 기술되어 있는 카드 (공식 Q&amp;A Q4281: 명칭 · 특징 · 효과 · 진화원 효과 · 〈룰〉 · 조건 — 효과 문구는 [X]로 적힌 것)</summary>
    private static bool TextHas84(CardData c, string x) =>
        TargetSpec.TextHas(c, x) || c.Traits.Any(t => t.Equals(x, StringComparison.OrdinalIgnoreCase))
        || (EnglishCardRules.Get(c.CardId)?.Assembly?.Contains($"[{x}]", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>어셈블리할 때의 Lv. — 카드의 Lv. 또는 "~'s assembly에서 Lv.N으로도 취급" (EX9-062). Lv.이 없는 카드(0)는 맞지 않는다</summary>
    private static bool LevelIn84(CardData c, CardData assembling, Func<int, bool> ok)
    {
        if (c.Level > 0 && ok(c.Level)) return true;
        var aliases = CardScripts.For(c)?.AsmLevelAliases84;
        return aliases is { Count: > 0 } && aliases.Any(a => CardNames.Is(assembling, a.Name) && ok(a.Level));
    }

    private static List<string> Brackets84(string s) => Regex.Matches(s, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim()).ToList();
    private static string NamesKo84(IEnumerable<string> names) => string.Join("/", names.Select(n => $"「{KoreanEffectRenderer.NameKo(n)}」"));
    private static string TraitsKo84(IEnumerable<string> traits) => string.Join("/", traits.Select(n => $"「{KoreanEffectRenderer.TraitKo(n)}」"));
    private static string P84(string w, string a, string b) => w + KoreanEffectRenderer.J84(w, a, b);
    private static string Join84(string a, string b) => a.Length == 0 ? b : $"{a} {b}";

    /// <summary>"~을(를) 포함하는" → "~을(를) 포함하거나 " (공식 한글 "명칭에 「아구몬」을 포함하거나 특징 「ME」를 가진")</summary>
    private static string KoOr84(string k) =>
        k.EndsWith("포함하는") ? k[..^"포함하는".Length] + "포함하거나 "
        : k.EndsWith("가진") ? k[..^"가진".Length] + "가지거나 "
        : k.EndsWith("기술되어 있는") ? k[..^"기술되어 있는".Length] + "기술되어 있거나 "
        : k + " 또는 ";
}
