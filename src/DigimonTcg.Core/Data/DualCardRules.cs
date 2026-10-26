using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

/// <summary>
/// 듀얼 카드의 〈아츠 진화〉 (종합 룰 2-11 · 4-20) — 카드에 아이콘으로만 있고 영어 API 데이터에는 없다.
/// 공식 카드 리스트(world.digimoncard.com · digimoncard.com)의 [DUAL Rule] 칸에서 받은 data/kr/dual_rules.json (개인 로컬 사용 전용, git 제외).
/// 파일이 없거나 카드가 목록에 없으면 〈아츠 진화〉가 없는 것으로 본다 (추측하지 않는다).
/// </summary>
public static class DualCardRules
{
    private sealed class Entry
    {
        public string CardId { get; set; } = string.Empty;
        public bool ArtsDigivolve { get; set; }
    }

    private sealed class Document
    {
        public List<Entry> Cards { get; set; } = new();
    }

    private static Lazy<HashSet<string>> _arts = new(() => Load(DefaultPath()));

    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(CardCatalogService.ResolveImagesDirectory()) ?? ".", "kr", "dual_rules.json");

    /// <summary>이 카드가 〈아츠 진화〉를 가진 듀얼 카드인지 (공식 카드 리스트 기준)</summary>
    public static bool HasArtsDigivolve(CardData card) => card.IsDual && _arts.Value.Contains(card.CardId);

    public static int ArtsCount => _arts.Value.Count;

    /// <summary>다른 파일로 다시 읽는다 (테스트용)</summary>
    public static void Reload(string? path = null)
    {
        var p = path ?? DefaultPath();
        _arts = new Lazy<HashSet<string>>(() => Load(p));
    }

    private static HashSet<string> Load(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return set;
        try
        {
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var e in doc?.Cards ?? new())
                if (e.ArtsDigivolve && !string.IsNullOrWhiteSpace(e.CardId))
                    set.Add(e.CardId.Trim());
        }
        catch
        {
            // 파일이 깨졌으면 〈아츠 진화〉 없이 진행
        }
        return set;
    }
}
