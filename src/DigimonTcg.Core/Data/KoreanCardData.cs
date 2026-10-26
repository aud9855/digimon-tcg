using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

/// <summary>한국 공식 사이트(digimoncard.co.kr)에서 받은 카드 1장 — tools/krsite/collect_kr.ps1 출력 형식</summary>
public sealed class KoreanCard
{
    public string CardId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;          // 디지타마 · 디지몬 · 테이머 · 옵션
    public string Level { get; set; } = string.Empty;
    public List<string> Colors { get; set; } = new();
    public List<string> Traits { get; set; } = new();
    public string? MainText { get; set; }                       // 상단 텍스트 (〔진화〕 특수 진화 조건 + 메인 효과)
    public string? SourceText { get; set; }                     // 하단 텍스트 (디지몬: 진화원 효과 / 테이머 · 옵션: [시큐리티] 효과)
    public string Image { get; set; } = string.Empty;
    public List<string> ParallelImages { get; set; } = new();
    public string Set { get; set; } = string.Empty;
}

/// <summary>
/// 화면 표시용 공식 한글 카드 데이터 (data/kr/cards_ko.json, 카드 번호 기준).
/// 대전 엔진은 계속 영어 API 데이터(Name · EffectText)로 판정하고, 이 데이터는 이름 · 특징 · 효과 문구 표시에만 쓴다.
/// 파일이 없으면 아무 카드도 찾지 못하고, 화면은 지금처럼 원래 데이터를 보여 준다.
/// </summary>
public static class KoreanCardData
{
    private static Lazy<Dictionary<string, KoreanCard>> _cards = new(() => Load(DefaultPath()));

    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(CardCatalogService.ResolveImagesDirectory()) ?? ".", "kr", "cards_ko.json");

    public static int Count => _cards.Value.Count;

    public static KoreanCard? Get(string cardId) =>
        _cards.Value.TryGetValue(cardId, out var card) ? card : null;

    public static IReadOnlyCollection<KoreanCard> All => _cards.Value.Values;

    /// <summary>다른 파일로 다시 읽는다 (테스트 · 수집 후 새로 고침용)</summary>
    public static void Reload(string? path = null)
    {
        var p = path ?? DefaultPath();
        _cards = new Lazy<Dictionary<string, KoreanCard>>(() => Load(p));
    }

    private static Dictionary<string, KoreanCard> Load(string path)
    {
        var map = new Dictionary<string, KoreanCard>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return map;
        try
        {
            var list = JsonSerializer.Deserialize<List<KoreanCard>>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var c in list ?? new())
            {
                if (!string.IsNullOrWhiteSpace(c.CardId))
                    map[c.CardId.Trim()] = c;
            }
        }
        catch
        {
            // 파일이 깨졌으면 한글 표시 없이 진행
        }
        return map;
    }

    // ---- CardData 표시용 ----

    /// <summary>이름 표시: 공식 한글 → (공식 한글이 없는 카드) 같은 영어 이름의 공식 한글 이름 → 영어 이름</summary>
    public static string DisplayName(CardData card) =>
        Get(card.CardId) is { Name.Length: > 0 } ko ? ko.Name
        : StarterDecks.IsBuiltInCard(card.CardId) ? card.Name
        : Engine.Effects.KoreanNameLocalizer.KoreanNameFor(card.Name) ?? card.Name;

    /// <summary>
    /// 메인 효과 표시 — 테이머 · 옵션은 [시큐리티] 효과(하단 텍스트)까지 붙인다 (내장 카드와 같은 형식).
    /// 순서: ① 공식 한글(cards_ko.json) ② 내장 카드는 원래 문구(이미 한글판에서 옮긴 한글) ③ 생성한 한글(KoreanEffectRenderer) ④ 영어 원문
    /// </summary>
    public static string DisplayEffectText(CardData card)
    {
        var ko = Get(card.CardId);
        if (ko == null)
        {
            if (StarterDecks.IsBuiltInCard(card.CardId))
                return card.EffectText;
            if (card.Type is CardType.Tamer or CardType.Option)
            {
                string genMain = Generated(card, inherited: false);
                string genSec = Generated(card, inherited: true);
                return genSec.Length == 0 ? genMain : genMain.Length == 0 ? genSec : genMain + "\n" + genSec;
            }
            return Generated(card, inherited: false);
        }
        string main = Clean(ko.MainText);
        if (card.Type is CardType.Tamer or CardType.Option)
        {
            string sec = Clean(ko.SourceText);
            // 공식 한글 데이터에 [시큐리티] 효과가 빠진 카드(BT11 옵션 등): 영어 [시큐리티] 효과의 생성 한글로 채운다
            if (sec.Length == 0 && !main.Contains("시큐리티】") && !string.IsNullOrWhiteSpace(card.InheritedEffectText))
                sec = Generated(card, inherited: true);
            return sec.Length == 0 ? main : main.Length == 0 ? sec : main + "\n" + sec;
        }
        return main;
    }

    public static string DisplayInheritedEffectText(CardData card)
    {
        var ko = Get(card.CardId);
        if (ko == null)
        {
            if (StarterDecks.IsBuiltInCard(card.CardId))
                return card.InheritedEffectText;
            if (card.Type is CardType.Tamer or CardType.Option) return string.Empty;
            string gen = Generated(card, inherited: true);
            // 듀얼 카드 옵션 쪽의 〈아츠 진화〉 (공식 카드 리스트의 [DUAL Rule] — 영어 API 문장에는 없다). 설명은 종합 룰 4-20-1 문구
            return DualCardRules.HasArtsDigivolve(card) ? (gen.Length == 0 ? ArtsDigivolveText : gen + "\n" + ArtsDigivolveText) : gen;
        }
        return card.Type is CardType.Tamer or CardType.Option ? string.Empty : Clean(ko.SourceText);
    }

    /// <summary>〈아츠 진화〉 표시 문구 (종합 룰 2-11 · 4-20-1)</summary>
    public const string ArtsDigivolveText = "〈아츠 진화〉 (사용한 뒤 트래시하는 대신, 자신의 카드를 이 카드로 코스트를 지불하지 않고 진화시킬 수 있다)";

    /// <summary>공식 한글이 없는 카드: 영어 효과 문장으로 만든 한글 (절 하나도 번역하지 못하면 영어 원문)</summary>
    private static string Generated(CardData card, bool inherited)
    {
        string english = inherited ? card.InheritedEffectText : card.EffectText;
        if (string.IsNullOrWhiteSpace(english))
            return string.Empty;
        return GeneratedCache.GetOrAdd((card.CardId, inherited), _ =>
        {
            try { return Engine.Effects.KoreanEffectRenderer.Render(card, inherited) ?? english; }
            catch { return english; }   // 번역 오류로 화면이 깨지지 않게 — 영어 원문
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string CardId, bool Inherited), string> GeneratedCache = new();

    /// <summary>특징 표시: 공식 한글 → (공식 한글이 없는 카드) 특징 이름표로 하나씩 (모르는 특징은 영어 그대로)</summary>
    public static IReadOnlyList<string> DisplayTraits(CardData card) =>
        Get(card.CardId) is { Traits.Count: > 0 } ko ? ko.Traits
        : StarterDecks.IsBuiltInCard(card.CardId) || Count == 0 ? card.Traits
        : card.Traits.Select(Engine.Effects.KoreanEffectRenderer.TraitKo).ToList();

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-")
            return string.Empty;
        // 사이트 문구는 효과가 한 줄로 이어져 있다 → 새 효과의 발동 시점(【…】) 앞에서 줄을 바꾼다
        // (문장이 끝난 뒤의 【…】만 — "이 카드의 【메인】 효과"처럼 문장 속 표기는 그대로)
        string t = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"(?<=[.。)）》」])\s*(?=【|〔)", "\n");
        return t.Trim();
    }
}
