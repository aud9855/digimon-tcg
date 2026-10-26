using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Data;

public class CardCatalogService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _cacheFilePath;
    private readonly string _imagesDirPath;
    private List<CardData> _catalog = new();

    public IReadOnlyList<CardData> Catalog => _catalog;
    public string ImagesDirectoryPath => _imagesDirPath;

    public CardCatalogService(string? dataDirectory = null)
    {
        string baseDir = dataDirectory ?? FindDataDirectory();
        Directory.CreateDirectory(baseDir);
        _imagesDirPath = Path.Combine(baseDir, "images");
        Directory.CreateDirectory(_imagesDirPath);
        _cacheFilePath = Path.Combine(baseDir, "cards_cache.json");
        LoadOrInitializeCache();
    }

    private static string FindDataDirectory()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(Path.Combine(candidate, "images")) || File.Exists(Path.Combine(dir.FullName, "DigimonTcg.sln")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        const string projectDataPath = @"d:\Project\DigimonTcg\data";
        if (Directory.Exists(projectDataPath))
        {
            return projectDataPath;
        }

        return Path.Combine(Environment.CurrentDirectory, "data");
    }

    public void LoadOrInitializeCache()
    {
        var defaultCards = StarterDecks.GetAllCatalogCards();
        if (File.Exists(_cacheFilePath))
        {
            bool loadedFromCache = false;
            try
            {
                string json = File.ReadAllText(_cacheFilePath);
                var loaded = JsonSerializer.Deserialize<List<CardData>>(json, JsonOptions);
                if (loaded != null && loaded.Count > 0)
                {
                    // 구버전 임시 ID 제거 및 최신 기본 카드/이미지 URL 병합
                    var merged = loaded
                        .Where(c => !c.CardId.EndsWith("B", StringComparison.Ordinal))
                        .ToDictionary(c => c.CardId);

                    // 저장된 API 카드의 키워드 수치를 현재 키워드 사전으로 다시 계산 (사전이 바뀌어도 다시 받지 않아도 되도록)
                    foreach (var c in merged.Values)
                        KeywordDictionary.ApplyDataKeywords(c);

                    foreach (var def in defaultCards)
                    {
                        // 내장 카드는 코드 정의를 우선하되, 이전 동기화로 저장해 둔 등급 · 특징(trait)은 이어받는다
                        if (merged.TryGetValue(def.CardId, out var cached))
                        {
                            if (string.IsNullOrEmpty(def.Rarity))
                                def.Rarity = cached.Rarity;
                            if (def.Traits.Count == 0 && cached.Traits.Count > 0)
                                def.Traits = cached.Traits.ToList();
                        }
                        merged[def.CardId] = def;
                    }
                    _catalog = merged.Values.OrderBy(c => c.CardId).ToList();
                    loadedFromCache = true;
                }
            }
            catch (Exception ex)
            {
                // 캐시를 읽지 못하면 내장 카드만으로 실행하되, 기존 캐시(수천 장)는 덮어쓰지 않는다.
                // (예전에는 여기서 120장짜리 캐시로 덮어써서, 동시에 실행된 다른 프로세스가 전체 도감을 지운 적이 있다)
                LastCacheLoadError = ex.Message;
                _cacheWriteBlocked = true;
                try { File.Copy(_cacheFilePath, _cacheFilePath + ".unreadable", overwrite: true); } catch { }
                _catalog = defaultCards.OrderBy(c => c.CardId).ToList();
                return;
            }

            if (loadedFromCache)
            {
                // 읽기는 성공했으므로, 다시 저장하다 실패해도(다른 프로세스가 파일을 열고 있는 등) 읽은 도감은 그대로 쓴다
                try { SaveCache(); } catch { }
                return;
            }
        }

        _catalog = defaultCards.OrderBy(c => c.CardId).ToList();
        SaveCache();
    }

    /// <summary>마지막으로 캐시를 읽다가 난 오류 (없으면 null) — 이때는 캐시를 덮어쓰지 않고 내장 카드만 쓴다</summary>
    public string? LastCacheLoadError { get; private set; }

    // 캐시를 읽지 못해 내장 카드로 실행 중이면, 이번 실행 동안 기존 캐시 파일에 쓰지 않는다
    // (내장 120장이나 일부 세트만 받은 도감으로 수천 장짜리 캐시를 덮어쓰지 않도록. 앱을 다시 시작하면 다시 읽는다)
    private bool _cacheWriteBlocked;

    /// <summary>
    /// API에서 받은 카드의 특징(trait) · 등급 · 두 번째 색을 카탈로그 카드에 채운다 (효과 문장 · 이름 · 수치는 그대로). 반환: 바뀐 카드 수
    /// </summary>
    public int ApplyApiDetails(IEnumerable<CardData> apiCards)
    {
        var byId = _catalog.ToDictionary(c => c.CardId);
        int changed = 0;
        foreach (var api in apiCards)
        {
            if (!byId.TryGetValue(api.CardId, out var card))
                continue;
            bool touched = false;
            if (api.Traits.Count > 0 && !api.Traits.SequenceEqual(card.Traits)) { card.Traits = api.Traits.ToList(); touched = true; }
            if (string.IsNullOrEmpty(card.Rarity) && !string.IsNullOrEmpty(api.Rarity)) { card.Rarity = api.Rarity; touched = true; }
            if (card.SecondaryColor == null && api.SecondaryColor != null) { card.SecondaryColor = api.SecondaryColor; touched = true; }
            if (touched) changed++;
        }
        if (changed > 0)
            SaveCache();
        return changed;
    }

    /// <summary>도감을 캐시 파일에 저장한다. 캐시를 읽지 못한 채 실행 중이라 저장하지 않았으면 false</summary>
    public bool SaveCache()
    {
        if (_cacheWriteBlocked)
            return false;
        // 다른 프로세스(앱 · 테스트)가 동시에 읽어도 반쯤 쓴 파일을 보지 않도록 임시 파일에 쓴 뒤 한 번에 바꾼다
        string json = JsonSerializer.Serialize(_catalog, JsonOptions);
        string temp = _cacheFilePath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _cacheFilePath, overwrite: true);
        return true;
    }

    public string GetLocalImageFilePath(CardData card)
    {
        return Path.Combine(_imagesDirPath, $"{card.CardId}.jpg");
    }

    // 공식 한글판 카드 이미지 폴더 (저작권 때문에 git에 넣지 않음 — 사용자가 직접 넣는다). 앞의 폴더가 우선.
    private static readonly string[] KoreanImageFolders = { "images_ko", "tmp", "images_kr" };   // images_kr = tools/krsite 수집 이미지
    private Dictionary<string, string>? _koreanImageIndex;

    /// <summary>
    /// 한글판 카드 이미지 경로 (data/images_ko · data/tmp 아래 "{카드번호}.png" 파일). 없으면 null.
    /// 같은 번호의 패러렐(_P1 · -P1) 이미지보다 기본 이미지를 우선한다.
    /// </summary>
    public string? GetKoreanImageFilePath(string cardId)
    {
        _koreanImageIndex ??= BuildKoreanImageIndex();
        return _koreanImageIndex.TryGetValue(cardId, out var path) ? path : null;
    }

    public int KoreanImageCount => (_koreanImageIndex ??= BuildKoreanImageIndex()).Count;

    private Dictionary<string, string> BuildKoreanImageIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var isVariant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string dataDir = Path.GetDirectoryName(_imagesDirPath) ?? _imagesDirPath;

        foreach (var folder in KoreanImageFolders)
        {
            string dir = Path.Combine(dataDir, folder);
            if (!Directory.Exists(dir))
                continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).ToList(); }
            catch { continue; }

            // png/jpg를 webp보다 먼저 (WPF가 webp를 못 여는 PC가 있다)
            foreach (var file in files.OrderBy(f => Path.GetExtension(f).Equals(".webp", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                    continue;

                var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(file), @"^([A-Za-z]+\d*-\d+)(.*)$");
                if (!m.Success)
                    continue;

                string id = m.Groups[1].Value.ToUpperInvariant();
                bool variant = m.Groups[2].Value.Length > 0;
                if (index.ContainsKey(id) && (variant || !isVariant.Contains(id)))
                    continue;   // 이미 기본 이미지가 있음 (또는 같은 패러렐)

                index[id] = file;
                if (variant) isVariant.Add(id); else isVariant.Remove(id);
            }
        }

        return index;
    }

    /// <summary>
    /// 카드 이미지를 로컬 data/images/{CardId}.jpg 에 캐싱하고 경로 반환
    /// </summary>
    public async Task<string?> EnsureCardImageCachedAsync(CardData card)
    {
        string localPath = GetLocalImageFilePath(card);
        if (File.Exists(localPath) && new FileInfo(localPath).Length > 500)
        {
            return localPath;
        }

        string url = !string.IsNullOrWhiteSpace(card.ImageUrl)
            ? card.ImageUrl
            : $"https://images.digimoncard.io/images/cards/{card.CardId}.jpg";

        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url);
            if (bytes.Length > 500)
            {
                await File.WriteAllBytesAsync(localPath, bytes);
                return localPath;
            }
        }
        catch
        {
            // 오프라인 시 프로시저럴 카드 일러스트 프레임으로 대체 렌더링
        }
        return null;
    }

    // ==================== DigimonCard.io 공개 API 전체 카드 동기화 ====================
    // getAllCards  : 전체 카드 번호 목록 (이름 + 번호) → 세트 접두어(BT1, EX3, ST12, P ...) 추출
    // search?card= : 세트별 상세 정보 ("BT1-" 처럼 하이픈까지 넣어야 BT10 ~ BT19 가 섞이지 않음)
    private const string ApiBase = "https://digimoncard.io/api-public";
    private const string ApiSeries = "Digimon%20Card%20Game";

    /// <summary>
    /// 전 세트 카드를 받아 도감에 추가한다. 내장 한글 카드(효과 구현)는 덮어쓰지 않는다.
    /// API로 받은 카드는 '효과 미구현' (DP · 코스트 · 진화 조건 + 자동 인식 키워드만 적용).
    /// </summary>
    public async Task<(bool Success, string Message)> SyncAllSetsFromApiAsync(IProgress<string>? progress = null)
    {
        var coreIds = StarterDecks.GetAllCatalogCards().Select(c => c.CardId).ToHashSet();
        var map = _catalog.ToDictionary(c => c.CardId);
        int before = map.Count;
        var failedSets = new List<string>();
        string? limitNote = null;
        int fetchedSets = 0;

        try
        {
            // 1) 세트 목록: 전체 카드 번호 목록을 받는다 (요청 1회). 받지 못하면 도감에 있는 카드 번호로 대신한다.
            //    v0.28.0: 이미 받은 세트라도 API에 새 카드 번호가 생겼으면(예: BT27 추가분) 다시 받도록 번호 목록을 늘 본다
            List<string> prefixes;
            var apiIds = new HashSet<string>(StringComparer.Ordinal);
            string? listJson = null;
            try
            {
                progress?.Report("🌐 전체 카드 목록을 받는 중...");
                listJson = await GetWithRateLimitAsync($"{ApiBase}/getAllCards?series={ApiSeries}",
                    wait => progress?.Report($"⏳ API 요청 제한으로 {wait}초 기다리는 중..."));
            }
            catch (HttpRequestException) when (map.Count > coreIds.Count)
            {
                listJson = null;
            }
            if (listJson != null)
            {
                using var listDoc = JsonDocument.Parse(listJson);
                foreach (var e in listDoc.RootElement.EnumerateArray())
                    if (e.TryGetProperty("cardnumber", out var n) && n.GetString() is { } num && !string.IsNullOrWhiteSpace(num) && num.Contains('-'))
                        apiIds.Add(num.Trim());
                prefixes = apiIds.Select(SetPrefixOf).Distinct().OrderBy(SetSortKey).ToList();
            }
            else if (map.Count > coreIds.Count)
            {
                prefixes = map.Keys.Select(SetPrefixOf).Distinct().OrderBy(SetSortKey).ToList();
            }
            else
            {
                throw new InvalidOperationException("카드 목록 응답 없음");
            }

            // 2) 이어받기: 이미 등급까지 받아 두었고 API의 카드 번호가 모두 도감에 있는 세트는 건너뛰고, 아직 안 받았거나 덜 받은 세트만 요청
            var completeSets = map.Values
                .Where(c => !coreIds.Contains(c.CardId))
                .GroupBy(c => SetPrefixOf(c.CardId))
                .Where(g => g.All(c => !string.IsNullOrEmpty(c.Rarity)))
                .Select(g => g.Key)
                .ToHashSet();
            foreach (var missing in apiIds.Where(id => !map.ContainsKey(id)).Select(SetPrefixOf).Distinct().ToList())
                completeSets.Remove(missing);
            var pending = prefixes.Where(p => !completeSets.Contains(p)).ToList();

            for (int i = 0; i < pending.Count; i++)
            {
                string prefix = pending[i];
                progress?.Report($"🌐 [{i + 1}/{pending.Count}] {prefix} 세트 받는 중... (현재 도감 {map.Count}종)");
                string? json;
                try
                {
                    json = await GetWithRateLimitAsync(
                        $"{ApiBase}/search?card={Uri.EscapeDataString(prefix + "-")}&series={ApiSeries}",
                        wait => progress?.Report($"⏳ [{i + 1}/{pending.Count}] {prefix} — API 요청 제한으로 {wait}초 기다리는 중... (현재 도감 {map.Count}종)"));
                }
                catch (HttpRequestException)
                {
                    json = null;
                }

                if (json == null)
                {
                    failedSets.Add(prefix);
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    failedSets.Add(prefix);
                    continue;
                }

                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var card = ParseApiCard(elem);
                    if (card == null || !card.CardId.StartsWith(prefix + "-", StringComparison.Ordinal))
                        continue;
                    if (coreIds.Contains(card.CardId))
                    {
                        // 한글 번역 · 효과 구현 카드는 유지하고, API에만 있는 등급 정보만 채운다
                        if (map.TryGetValue(card.CardId, out var core) && string.IsNullOrEmpty(core.Rarity))
                            core.Rarity = card.Rarity;
                        continue;
                    }
                    map[card.CardId] = card;
                }
                fetchedSets++;
            }
        }
        catch (ApiRateLimitedException ex)
        {
            // 장시간 차단: 더 요청하지 않고 지금까지 받은 카드만 저장한다
            int minutes = (int)Math.Ceiling(ex.RetryAfter.TotalMinutes);
            limitNote = $"API 요청 한도를 넘어 잠시 차단되었습니다. 약 {minutes}분 뒤에 다시 누르면 남은 세트부터 이어서 받습니다.";
        }
        catch (Exception ex) when (map.Count == before && fetchedSets == 0)
        {
            return (false, $"카드를 받지 못했습니다 ({ex.Message}). 기존 도감 {_catalog.Count}종을 그대로 사용합니다.");
        }

        _catalog = map.Values.OrderBy(c => c.CardId).ToList();
        bool saved = SaveCache();

        int implemented = _catalog.Count(c => c.IsEffectImplemented);
        string summary = $"도감 {_catalog.Count}종 (새로 추가 {_catalog.Count - before}종 · 효과 구현 {implemented}종 · 이번에 받은 세트 {fetchedSets}개)";
        if (!saved)
            summary += $" — 시작할 때 기존 카드 캐시를 읽지 못해({LastCacheLoadError}) 이번에 받은 카드는 파일에 저장하지 않았습니다. 앱을 다시 시작하면 기존 캐시를 다시 읽습니다.";
        if (limitNote != null)
            return (fetchedSets > 0, $"⏳ {limitNote} {summary}");
        if (failedSets.Count > 0)
            return (fetchedSets > 0, $"⚠️ 일부 세트를 받지 못했습니다({string.Join(", ", failedSets)}). 다시 누르면 이어서 받습니다. {summary}");
        return (true, $"🌐 전체 카드 업데이트 완료! {summary}");
    }

    /// <summary>요청 한도 초과로 오래(1분 초과) 기다려야 할 때 — 재시도하지 않고 동기화를 멈춘다</summary>
    private sealed class ApiRateLimitedException(TimeSpan retryAfter) : Exception("API 요청 한도 초과")
    {
        public TimeSpan RetryAfter { get; } = retryAfter;
    }

    private static string SetPrefixOf(string cardId) => cardId.Contains('-') ? cardId[..cardId.LastIndexOf('-')] : cardId;

    /// <summary>
    /// 공개 API 요청 제한(x-ratelimit-limit: 15)을 지키며 GET 한다.
    /// 남은 횟수가 거의 없으면 미리 쉬고, 429 를 받으면 Retry-After 가 1분 이하일 때만 기다렸다가 다시 시도한다.
    /// 그보다 길게 막히면(장시간 차단) ApiRateLimitedException 으로 즉시 멈춘다 — 계속 두드리면 차단이 길어진다.
    /// </summary>
    private static async Task<string?> GetWithRateLimitAsync(string url, Action<int>? onWait = null)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using var response = await Http.GetAsync(url);
            if ((int)response.StatusCode == 429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(15 * (attempt + 1));
                if (retryAfter > TimeSpan.FromSeconds(60))
                    throw new ApiRateLimitedException(retryAfter);
                int wait = Math.Max(5, (int)Math.Ceiling(retryAfter.TotalSeconds));
                onWait?.Invoke(wait);
                await Task.Delay(TimeSpan.FromSeconds(wait));
                continue;
            }
            if (!response.IsSuccessStatusCode)
                return null;

            string body = await response.Content.ReadAsStringAsync();

            // 남은 요청 횟수가 거의 없으면 다음 요청 전에 창이 풀릴 때까지 쉰다
            if (response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
                && int.TryParse(values.FirstOrDefault(), out int remaining) && remaining <= 2)
            {
                onWait?.Invoke(20);
                await Task.Delay(TimeSpan.FromSeconds(20));
            }
            else
            {
                await Task.Delay(1200);   // 1분에 약 15회 이하로 유지
            }
            return body;
        }
        return null;
    }

    /// <summary>세트 정렬: ST → BT → EX → 그 외, 같은 계열은 번호순</summary>
    private static (int Group, int Number, string Raw) SetSortKey(string prefix)
    {
        string letters = new string(prefix.TakeWhile(char.IsLetter).ToArray());
        int.TryParse(new string(prefix.SkipWhile(char.IsLetter).ToArray()), out int number);
        int group = letters switch { "ST" => 0, "BT" => 1, "EX" => 2, _ => 3 };
        return (group, number, prefix);
    }

    public static CardData? ParseApiCard(JsonElement e)
    {
        string? id = Str(e, "id") ?? Str(e, "cardnumber");
        string? name = Str(e, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            return null;

        var type = ParseCardType(Str(e, "type"));
        int level = Int(e, "level") ?? (type == CardType.Digitama ? 2 : 0);
        string mainEffect = Str(e, "main_effect") ?? Str(e, "maineffect") ?? string.Empty;
        string sourceEffect = Str(e, "source_effect") ?? Str(e, "soureeffect") ?? string.Empty;
        var color = ParseCardColor(Str(e, "color"));

        var card = new CardData
        {
            CardId = id,
            Name = name,
            Type = type,
            Color = color,
            Level = level,
            PlayCost = Int(e, "play_cost") ?? 0,
            EvoCost = Int(e, "evolution_cost") ?? 0,
            EvoRequirementLevel = Int(e, "evolution_level") ?? Math.Max(2, level - 1),
            EvoRequirementColor = Str(e, "evolution_color") is { } evoColor ? ParseCardColor(evoColor) : color,
            SecondaryColor = Str(e, "color2") is { } color2 && !string.IsNullOrWhiteSpace(color2) ? ParseCardColor(color2) : null,
            DP = Int(e, "dp") ?? 0,
            EffectText = mainEffect,
            InheritedEffectText = sourceEffect,
            IsDual = string.Equals(Str(e, "type"), "Dual", StringComparison.OrdinalIgnoreCase),   // 듀얼 카드 (옵션 쪽 = source_effect)
            ImageUrl = $"https://images.digimoncard.io/images/cards/{id}.jpg",
            Rarity = NormalizeRarity(Str(e, "rarity")),
            // 특징: 유형(digi_type ~ digi_type5) · 세대(form) · 속성(attribute) — 효과 문장의 "[X] trait" 판정에 쓴다
            Traits = new[] { "digi_type", "digi_type2", "digi_type3", "digi_type4", "digi_type5", "form", "attribute" }
                .Select(k => Str(e, k)?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        // 키워드만 있는 절의 ≪블로커≫ · ≪시큐리티 어택 ±N≫ (조건부로 얻는 키워드는 제외) — 효과 엔진의 키워드 사전 기준
        KeywordDictionary.ApplyDataKeywords(card);
        return card;
    }

    /// <summary>API 등급 표기(c, u, r, sr, sec, p, 대소문자 혼용)를 C / U / R / SR / SEC / P 로 통일</summary>
    public static string NormalizeRarity(string? raw) => (raw ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>등급 정렬 순서 (낮은 등급 → 높은 등급)</summary>
    public static readonly string[] RarityOrder = { "C", "U", "R", "SR", "SEC", "P" };

    private static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;



    /// <summary>앱 시작 시 카드 뒷면 이미지 등을 찾기 위한 data/images 경로 (카탈로그를 읽지 않음)</summary>
    public static string ResolveImagesDirectory() => Path.Combine(FindDataDirectory(), "images");

    /// <summary>화면에 보이는 공식 한글(data/kr/cards_ko.json)의 이름 · 효과 문구 · 특징으로도 찾는다</summary>
    private static bool MatchesKoreanDisplay(CardData card, string keyword)
    {
        var ko = KoreanCardData.Get(card.CardId);
        if (ko == null)
            return false;
        return ko.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (ko.MainText?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
            || (ko.SourceText?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
            || ko.Traits.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public List<CardData> SearchCards(
        string? keyword = null,
        CardColor? color = null,
        CardType? type = null,
        int? level = null,
        int? maxPlayCost = null)
    {
        IEnumerable<CardData> query = _catalog;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            string kw = keyword.Trim();
            query = query.Where(c =>
                c.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                c.CardId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                c.EffectText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                c.InheritedEffectText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                MatchesKoreanDisplay(c, kw));
        }

        if (color.HasValue)
            query = query.Where(c => c.Color == color.Value);

        if (type.HasValue)
            query = query.Where(c => c.Type == type.Value);

        if (level.HasValue)
            query = query.Where(c => c.Level == level.Value);

        if (maxPlayCost.HasValue)
            query = query.Where(c => c.PlayCost <= maxPlayCost.Value);

        return query.OrderBy(c => c.CardId).ToList();
    }

    private static CardType ParseCardType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "digi-egg" or "digitama" => CardType.Digitama,
        "tamer" => CardType.Tamer,
        "option" => CardType.Option,
        _ => CardType.Digimon
    };

    private static CardColor ParseCardColor(string? raw) => raw?.ToLowerInvariant() switch
    {
        "blue" => CardColor.Blue,
        "yellow" => CardColor.Yellow,
        "green" => CardColor.Green,
        "black" => CardColor.Black,
        "purple" => CardColor.Purple,
        "white" => CardColor.White,
        _ => CardColor.Red
    };
}
