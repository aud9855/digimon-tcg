using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DigimonTcg.Core.Data;
using DigimonTcg.Core.DeckBuilder;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using DigimonTcg.Wpf.Themes;

namespace DigimonTcg.Wpf;

public partial class MainWindow : Window
{
    private readonly CardCatalogService _catalogService;
    private readonly GameState _gameState;
    private readonly GoldfishEngine _engine;
    private readonly Dictionary<string, BitmapImage> _bitmapCache = new();

    private List<CardData> _builderMainDeck = new();
    private List<CardData> _builderDigitamaDeck = new();
    private string _activeDeckTitle = "🔥 [추천 1] 워그레이몬 & 오메가몬 초화력 비트 (50장)";

    // 이름을 붙여 저장한 내 덱 보관함 (data/saved_decks.json) · 지금 덱 빌더에 불러온 / 저장한 덱 이름 (저장 창 기본값)
    private readonly SavedDeckStore _savedDeckStore;
    private string? _currentSavedDeckName;

    private CardData? _pendingHandCard;
    private bool _pendingIsEvolution;
    private bool _pendingTargetsOpponent;
    private CardInstance? _pendingAttackerInstance;
    private CardInstance? _pinnedFieldInstance;
    private bool _isSecurityPeekEnabled = false;
    private DispatcherTimer? _ultimateCutInTimer;

    // 드래그 앤 드롭 상태 필드
    private Point _dragStartPoint;
    private bool _isDragging;
    private CardData? _potentialDragHandCard;
    private CardInstance? _potentialDragFieldAttacker;
    private CardInstance? _potentialDragBreedingDigimon;
    private Border? _dragGhostBorder;
    private Border? _dragGhostBadgeBorder;
    private TextBlock? _dragGhostBadgeText;

    // 옵션 [메인] 효과의 대상 종류는 카드 스크립트(CardScripts)에 적혀 있다.
    // 대상 쪽에 대상 조건을 만족하는 디지몬이 하나도 없으면 대상 옵션으로 다루지 않는다 —
    // 공식 룰상 대상이 없어도 옵션은 쓸 수 있다 (예: "상대 디지몬 1마리를 소멸시킨다. 그 뒤 ≪1 드로우≫"를 드로우하려고).
    private bool IsAllyTargetedOption(CardData card) =>
        card.Type == CardType.Option &&
        CardScripts.MainTargetOf(card) is TargetKind.OwnDigimon or TargetKind.OwnDigimonWithDigimonSource &&   // 자신의 디지몬 1마리 대상
        _gameState.Player.BattleArea.Any(d => IsValidAllyOptionTarget(card, d));

    private bool IsEnemyTargetedOption(CardData card) =>
        card.Type == CardType.Option &&
        CardScripts.MainTargetOf(card) == TargetKind.OpponentDigimon &&   // 상대 디지몬 대상
        _gameState.DummyOpponent.BattleArea.Any(d => CardScripts.IsValidMainTarget(card, d));

    private static bool IsValidAllyOptionTarget(CardData option, CardInstance d) =>
        CardScripts.IsValidMainTarget(option, d)
        && (CardScripts.MainTargetOf(option) != TargetKind.OwnDigimonWithDigimonSource || d.EvolutionSources.Any(s => s.Type == CardType.Digimon));

    /// <summary>상대 디지몬 대상 옵션을 빈 칸에 놓았을 때의 대상: 대상 조건을 만족하는 상대 디지몬 중 DP가 가장 높은 것 (없으면 null)</summary>
    private CardInstance? BestEnemyOptionTarget(CardData option) =>
        _gameState.DummyOpponent.BattleArea
            .Where(d => CardScripts.IsValidMainTarget(option, d))
            .OrderByDescending(d => d.GetCurrentDP())
            .FirstOrDefault();

    public MainWindow()
    {
        InitializeComponent();
        FitRestoredBoundsToWorkArea();

        // 앱 버전 표시 (src/DigimonTcg.Version.props + 빌드한 커밋)
        TxtAppVersion.Text = DigimonTcg.Core.AppVersion.Display;
        Title = $"{Title}  {DigimonTcg.Core.AppVersion.Display}";

        _catalogService = new CardCatalogService();
        _savedDeckStore = new SavedDeckStore(Path.Combine(Path.GetDirectoryName(_catalogService.ImagesDirectoryPath)!, "saved_decks.json"));
        _gameState = new GameState();
        _engine = new GoldfishEngine(_gameState) { PlayerChoosesBlocks = true };
        // 카드 효과의 "~할 수 있다" 확인 · 대상 고르기 · 고르는 효과 (카드 그림으로 보여 준다)
        _engine.PlayerChoices = new WpfChoiceProvider(this, card => CreateCardVisualFrame(card, width: 108, height: 151));

        // 기본 추천 1번 덱(ST-1 가이아 레드 + 오메가몬) 로드 및 백그라운드 대련 상태 준비
        var (st1Main, st1Digitama) = StarterDecks.CreateSt1GaiaRedPreset();
        _builderMainDeck = st1Main.ToList();
        _builderDigitamaDeck = st1Digitama.ToList();

        _isSecurityPeekEnabled = false;
        InitOpponentDeckCombos();
        StartGameForStage(PracticeStage.Stage3_AiBattle);

        // 초기 프리뷰 카드 세팅 (워그레이몬 또는 첫 패 카드)
        var initialPreview = _gameState.Player.Hand.FirstOrDefault() ?? _catalogService.Catalog.First();
        UpdateCardInspector(initialPreview);
        UpdateFieldDigimonBreakdown(null);

        // 1. 처음 켰을 때 메인 로비 화면(실전 대련[AI], 덱빌딩, 연습)으로 시작!
        UpdateTopTabVisuals(activeTab: -1);
        RefreshAllSimulatorUi();
        InitSetFilter();
        RefreshCatalogUi();
        RefreshDeckBuilderUi();
        ApplyLobbyArtwork();
        ShowToast(true, "🏠 [디지몬 TCG : DIGITAL COLOSSEUM] 메인 로비입니다! [⚔️ 실전 대련 (AI)], [🛠️ 덱 빌딩 & 도감], [🎯 연습] 모드를 선택하세요.");

        // 신규 추가된 7색 TCG 카드 일러스트 백그라운드 프리패치
        _ = PrefetchCatalogImagesAsync();
        _ = WarmUpEffectSupportAsync();
    }

    private async System.Threading.Tasks.Task PrefetchCatalogImagesAsync()
    {
        // 미리 받는 것은 효과 구현 카드(덱 · 대전에서 주로 쓰는 카드)만. 나머지 수천 장은 바인더에 보일 때 받는다.
        foreach (var card in _catalogService.Catalog.Where(c => c.IsEffectImplemented && _catalogService.GetKoreanImageFilePath(c.CardId) == null).ToList())
        {
            string localPath = _catalogService.GetLocalImageFilePath(card);
            if (!File.Exists(localPath) || new FileInfo(localPath).Length <= 500)
            {
                await _catalogService.EnsureCardImageCachedAsync(card);
            }
        }
        Dispatcher.Invoke(() =>
        {
            ApplyLobbyArtwork();
            if (DeckBuilderView.Visibility == Visibility.Visible)
                RefreshCatalogUi();
            if (SimulatorView.Visibility == Visibility.Visible)
                RefreshAllSimulatorUi();
        });
    }

    // ==================== 카드 이미지 로더 & 인스펙터 ====================
    private BitmapImage? GetCardBitmap(CardData card)
    {
        if (_bitmapCache.TryGetValue(card.CardId, out var cached))
            return cached;

        // 공식 한글판 카드 이미지가 있으면 우선 사용 (data/images_ko · data/tmp)
        string? koreanPath = _catalogService.GetKoreanImageFilePath(card.CardId);
        if (koreanPath != null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 480;
                bmp.UriSource = new Uri(koreanPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _bitmapCache[card.CardId] = bmp;
                return bmp;
            }
            catch
            {
                // webp 코덱이 없는 PC 등 — 영문 이미지로 대체
            }
        }

        string localPath = _catalogService.GetLocalImageFilePath(card);
        if (File.Exists(localPath) && new FileInfo(localPath).Length > 500)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(localPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _bitmapCache[card.CardId] = bmp;
                return bmp;
            }
            catch
            {
                // 이미지 디코딩 실패 시 무시
            }
        }
        else
        {
            // 백그라운드로 이미지 다운로드 시도 후 캐시
            _ = DownloadAndRefreshCardImageAsync(card);
        }

        return null;
    }

    // 이미지 지연 다운로드: 같은 카드 중복 요청 방지 · 실패한 카드는 이번 실행 중 재시도하지 않음 · 화면 갱신은 0.5초 모아서 한 번
    private readonly HashSet<string> _imageDownloadsInFlight = new();
    private readonly HashSet<string> _imageDownloadsFailed = new();
    private DispatcherTimer? _imageRefreshTimer;

    private async System.Threading.Tasks.Task DownloadAndRefreshCardImageAsync(CardData card)
    {
        if (_imageDownloadsFailed.Contains(card.CardId) || !_imageDownloadsInFlight.Add(card.CardId))
            return;

        try
        {
            var path = await _catalogService.EnsureCardImageCachedAsync(card);
            if (path == null)
                _imageDownloadsFailed.Add(card.CardId);
            else
                ScheduleImageRefresh();
        }
        finally
        {
            _imageDownloadsInFlight.Remove(card.CardId);
        }
    }

    private void ScheduleImageRefresh()
    {
        if (_imageRefreshTimer == null)
        {
            _imageRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _imageRefreshTimer.Tick += (_, _) =>
            {
                _imageRefreshTimer.Stop();
                if (WorkspaceContainerGrid.Visibility != Visibility.Visible)
                    return;
                if (SimulatorView.Visibility == Visibility.Visible)
                    RefreshAllSimulatorUi();
                if (DeckBuilderView.Visibility == Visibility.Visible)
                    RefreshCatalogUi();
            };
        }
        _imageRefreshTimer.Stop();
        _imageRefreshTimer.Start();
    }

    private void UpdateCardInspector(CardData card)
    {
        TxtInspectorId.Text = card.CardId;
        InspectorCardFrame.BorderBrush = GetCardColorBrush(card.Color);
        InspectorColorBadge.Background = GetCardColorBrush(card.Color);

        TxtInspectorBadge.Text = (card.Type == CardType.Digimon || card.Type == CardType.Digitama
            ? $"Lv.{card.Level} {KoreanLabels.Colors(card)} {KoreanLabels.Type(card.Type)}"
            : $"{KoreanLabels.Colors(card)} {KoreanLabels.Type(card.Type)}") + (string.IsNullOrEmpty(card.Rarity) ? "" : $" · {card.Rarity}");

        TxtInspectorName.Text = card.DisplayName;
        TxtInspectorStats.Text = card.IsDual
            ? $"듀얼 카드 — 옵션 쪽 사용 코스트: {card.PlayCost} | 진화: {card.EvoCost} (Lv.{card.EvoRequirementLevel})\n기본 DP: {card.DP:N0}"
            : card.Type == CardType.Digimon
            ? $"등장 코스트: {card.PlayCost} | 진화: {card.EvoCost} (Lv.{card.EvoRequirementLevel})\n기본 DP: {card.DP:N0}"
            : card.Type == CardType.Digitama
                ? "육성 에어리어 전용 Lv.2 디지타마"
                : $"사용 코스트: {card.PlayCost}";
        if (card.DisplayTraits.Count > 0)
            TxtInspectorStats.Text += $"\n특징: {string.Join(" · ", card.DisplayTraits)}";

        TxtInspectorEffect.Text = string.IsNullOrWhiteSpace(card.DisplayEffectText) ? "없음 (바닐라)" : card.DisplayEffectText;
        TxtInspectorInheritedHeader.Text = card.IsDual ? "[옵션 쪽] (패에서 배틀 에어리어 빈 곳에 놓으면 사용)" : "[진화원 효과]";
        TxtInspectorInherited.Text = string.IsNullOrWhiteSpace(card.DisplayInheritedEffectText) ? "없음" : card.DisplayInheritedEffectText;

        // 대전 엔진이 효과를 구현하지 않은 카드 안내
        var support = EffectSupport.For(card);
        InspectorImplNote.Visibility = support.Level is EffectSupportLevel.Partial or EffectSupportLevel.None ? Visibility.Visible : Visibility.Collapsed;
        if (support.Level is EffectSupportLevel.Partial or EffectSupportLevel.None)
        {
            var (note, detail) = DescribeEffectSupport(support);
            TxtInspectorImplNote.Text = note;
            InspectorImplNote.ToolTip = detail;   // 영어 원문 · 엔진 쪽 사유는 마우스를 올렸을 때만
        }

        var bmp = GetCardBitmap(card);
        if (bmp != null)
        {
            ImgInspectorCard.Source = bmp;
            ImgInspectorCard.Visibility = Visibility.Visible;
            InspectorFallbackPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ImgInspectorCard.Visibility = Visibility.Collapsed;
            InspectorFallbackPanel.Visibility = Visibility.Visible;
            TxtInspectorFallback.Text = $"{card.CardId}\n{card.DisplayName}\n(Lv.{card.Level})";
        }
    }

    /// <summary>
    /// 실물 TCG 카드 디자인 아트워크 박스 생성 (코스트 오브, DP 배지, 한글 이름 오버레이, 레스트 기울임 포함)
    /// </summary>
    private FrameworkElement CreateCardVisualFrame(
        CardData card,
        double width,
        double height,
        string? customDpOrBadge = null,
        bool isRested = false)
    {
        var rootGrid = new Grid
        {
            Width = width,
            Height = height,
            ClipToBounds = true
        };

        var bmp = GetCardBitmap(card);
        if (bmp != null)
        {
            var img = new Image
            {
                Source = bmp,
                Stretch = Stretch.UniformToFill
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            rootGrid.Children.Add(img);
        }
        else
        {
            // 오프라인 폴백 프로시저럴 TCG 일러스트 배경
            rootGrid.Children.Add(new Border
            {
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#1E1E2E"),
                    GetCardColorBrush(card.Color).Color,
                    45.0),
                Child = new TextBlock
                {
                    Text = $"{card.CardId}\n{card.DisplayName}",
                    Foreground = BrushFromHex("#11111B"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        // 좌상단 등장 코스트 & 진화 코스트 배지
        if (card.Type != CardType.Digitama)
        {
            var topLeftStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4)
            };

            topLeftStack.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = BrushFromHex("#E611111B"),
                BorderBrush = GetCardColorBrush(card.Color),
                BorderThickness = new Thickness(1.5),
                ToolTip = $"등장/사용 코스트: {card.PlayCost}",
                Child = new TextBlock
                {
                    Text = card.PlayCost.ToString(),
                    Foreground = BrushFromHex("#F8FAFC"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });

            if (card.Type == CardType.Digimon)
            {
                topLeftStack.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = GetCardColorBrush(card.Color),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 2, 0, 0),
                    ToolTip = $"진화 코스트: {card.EvoCost} (Lv.{card.EvoRequirementLevel} 필요)",
                    Child = new TextBlock
                    {
                        Text = $"진화{card.EvoCost}",
                        Foreground = BrushFromHex("#11111B"),
                        FontWeight = FontWeights.ExtraBold,
                        FontSize = 9.5
                    }
                });
            }

            rootGrid.Children.Add(topLeftStack);
        }

        // 우상단 DP 또는 커스텀 배지
        string rightBadgeText = customDpOrBadge ?? (card.Type == CardType.Digimon ? $"{card.DP} DP" : KoreanLabels.Type(card.Type));
        rootGrid.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            Background = BrushFromHex("#D911111B"),
            BorderBrush = BrushFromHex("#F9E2AF"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock
            {
                Text = rightBadgeText,
                Foreground = BrushFromHex("#F9E2AF"),
                FontWeight = FontWeights.ExtraBold,
                FontSize = 10
            }
        });

        // 하단 반투명 한글 카드명 & 레벨 리본
        var bottomRibbon = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = BrushFromHex("#D911111B"),
            BorderBrush = GetCardColorBrush(card.Color),
            BorderThickness = new Thickness(0, 1.5, 0, 0),
            Padding = new Thickness(5, 3, 5, 3),
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = card.Level > 0 ? $"Lv.{card.Level} {card.DisplayName}" : card.DisplayName,
                        Foreground = BrushFromHex("#F8FAFC"),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        };
        rootGrid.Children.Add(bottomRibbon);

        // 레스트(REST) 상태 오버레이
        if (isRested)
        {
            rootGrid.Children.Add(new Border
            {
                Background = BrushFromHex("#8811111B"),
                Child = new Border
                {
                    Background = BrushFromHex("#FAB387"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "↻ REST (레스트)",
                        Foreground = BrushFromHex("#11111B"),
                        FontWeight = FontWeights.ExtraBold,
                        FontSize = 11
                    }
                }
            });
        }

        var outerBorder = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(7),
            BorderBrush = GetCardColorBrush(card.Color),
            BorderThickness = new Thickness(2),
            Background = BrushFromHex("#11111B"),
            ClipToBounds = true,
            Child = rootGrid,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        if (isRested)
        {
            outerBorder.RenderTransform = new RotateTransform(-6.0);
        }

        outerBorder.MouseEnter += (_, _) => UpdateCardInspector(card);
        return outerBorder;
    }

    /// <summary>
    /// 최대화를 풀었을 때의 창 크기 · 위치를 작업 영역(작업 표시줄 제외) 안으로 맞춘다.
    /// 최대화로 시작하면 CenterScreen이 적용되지 않아 Windows 기본 위치(계단식)에 놓였고, 1640x960 창의 아래가 작업 표시줄에 가렸다.
    /// </summary>
    private void FitRestoredBoundsToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    // ==================== 메인 로비 & 3대 메인 모드 탭 전환 (실전 대련[AI] / 덱 빌딩 / 연습[솔리테어]) ====================
    /// <summary>덱 빌더 카드에 열려 있는 툴팁 — 화면을 바꿀 때 닫는다 (카드가 숨겨져도 툴팁 창은 남아 있었다)</summary>
    private ToolTip? _openCatalogTip;

    private void UpdateTopTabVisuals(int activeTab)
    {
        if (_openCatalogTip != null)
        {
            _openCatalogTip.IsOpen = false;
            _openCatalogTip = null;
        }

        // -1: 메인 로비, 0: 실전 대련[AI], 1: 연습[솔리테어], 2: 덱 빌딩 & 도감
        // 로비(타이틀 화면)에서는 상단 헤더와 안내 바를 숨기고 전면 키 아트 + 세로 메뉴만 보여 준다
        var chrome = activeTab == -1 ? Visibility.Collapsed : Visibility.Visible;
        TopHeaderBar.Visibility = chrome;
        ToastBanner.Visibility = chrome;

        if (activeTab == -1)
        {
            MainLobbyView.Visibility = Visibility.Visible;
            WorkspaceContainerGrid.Visibility = Visibility.Collapsed;
            TxtLobbyActiveDeckName.Text = $"🎴 출전 덱: {_activeDeckTitle}";
        }
        else
        {
            MainLobbyView.Visibility = Visibility.Collapsed;
            WorkspaceContainerGrid.Visibility = Visibility.Visible;
            SimulatorView.Visibility = (activeTab == 0 || activeTab == 1) ? Visibility.Visible : Visibility.Collapsed;
            DeckBuilderView.Visibility = activeTab == 2 ? Visibility.Visible : Visibility.Collapsed;
            SoloStageButtonsPanel.Visibility = activeTab == 1 ? Visibility.Visible : Visibility.Collapsed;
            MatchOpponentPanel.Visibility = activeTab == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyInspectorMode(deckBuilder: activeTab == 2);
        }

        TxtCurrentModeSubtitle.Text = activeTab switch
        {
            -1 => string.Empty,
            0 => "⚔️ 실전 대련 [AI] — 공식 룰 플레이매트 & 카드 효과 실전 대전",
            1 => "🎯 연습 [솔리테어] — 5턴 타임어택 & 블로커 돌파 연습장",
            _ => "🛠️ 덱 빌딩 & 도감 — 7색 전체 TCG 카드 도감 & 추천 메타 덱 공방"
        };
    }

    /// <summary>
    /// 우측 사이드바 모드 전환.
    /// 덱 빌딩: 대련 전용 패널(필드 DP 내역 · 전장 현황 · 로그)을 숨기고 카드를 크게 + 효과를 아래에 보여 주는 '카드 상세'.
    /// 실전 / 연습: 작은 카드 + 효과 옆 배치에 대련 패널을 모두 표시.
    /// </summary>
    private void ApplyInspectorMode(bool deckBuilder)
    {
        var battleOnly = deckBuilder ? Visibility.Collapsed : Visibility.Visible;
        FieldStatBreakdownBorder.Visibility = battleOnly;
        InspectorHudSection.Visibility = Visibility.Collapsed;   // 전장 현황 지표는 사용하지 않음 (대련 화면에서도 숨김)
        InspectorLogSection.Visibility = battleOnly;

        TxtInspectorTitle.Text = deckBuilder ? "🔍 카드 상세" : "🔍 카드 효과 & 상세 프리뷰";
        InspectorImageColumn.Width = deckBuilder ? new GridLength(1, GridUnitType.Star) : new GridLength(136);
        InspectorTextColumn.Width = deckBuilder ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        InspectorCardFrame.Width = deckBuilder ? 300 : 130;
        InspectorCardFrame.Height = deckBuilder ? 418 : 182;
        InspectorCardFrame.CornerRadius = new CornerRadius(deckBuilder ? 12 : 8);
        InspectorCardFrame.HorizontalAlignment = deckBuilder ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        Grid.SetColumn(InspectorTextPanel, deckBuilder ? 0 : 1);
        Grid.SetRow(InspectorTextPanel, deckBuilder ? 1 : 0);
        InspectorTextPanel.Margin = deckBuilder ? new Thickness(2, 12, 2, 0) : new Thickness(8, 0, 0, 0);
        TxtInspectorName.FontSize = deckBuilder ? 19 : 15.5;
        TxtInspectorStats.FontSize = deckBuilder ? 12.5 : 11;
        TxtInspectorEffect.FontSize = deckBuilder ? 12 : 10.5;
        TxtInspectorInherited.FontSize = deckBuilder ? 12 : 10.5;
    }


    // ==================== 🏠 메인 로비 모드 카드 일러스트 배너 ====================
    private void ApplyLobbyArtwork()
    {
        // 타이틀 화면 키 아트: 초궁극체(Lv.7) 일러스트 → 없으면 레드 궁극체(Lv.6)
        Func<CardData, bool> heroPick = c => c.IsEffectImplemented && c.Type == CardType.Digimon && c.Level == 7;
        if (!_catalogService.Catalog.Any(c => heroPick(c) && GetCardBitmap(c) != null))
            heroPick = c => c.IsEffectImplemented && c.Type == CardType.Digimon && c.Level == 6 && c.Color == CardColor.Red;

        SetLobbyArt(LobbyHeroArt, heroPick, new Rect(0.17, 0.11, 0.79, 0.47));   // 코스트 아이콘 · 텍스트 박스를 피해 그림만
        SetLobbyArt(LobbyHeroBlur, heroPick);
    }

    private void SetLobbyArt(System.Windows.Shapes.Rectangle target, Func<CardData, bool> pick, Rect? viewbox = null)
    {
        var card = _catalogService.Catalog.Where(pick).FirstOrDefault(c => GetCardBitmap(c) != null);
        var bmp = card == null ? null : GetCardBitmap(card);
        if (bmp == null)
            return;

        // 카드에서 텍스트 박스를 뺀 일러스트 부분만 잘라 배너로 쓴다
        target.Fill = new ImageBrush(bmp)
        {
            Viewbox = viewbox ?? new Rect(0.04, 0.09, 0.92, 0.56),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Stretch = Stretch.UniformToFill,
            AlignmentY = AlignmentY.Top
        };
        target.ToolTip = $"{card!.DisplayName} ({card.CardId})";
    }

    private void BtnNavHome_Click(object sender, RoutedEventArgs e)
    {
        UpdateTopTabVisuals(-1);
        ShowToast(true, "🏠 메인 로비로 이동했습니다! 원하는 대련/덱빌딩/연습 모드를 선택하세요.");
    }

    private void BtnTabOfficialMatch_Click(object sender, RoutedEventArgs e)
    {
        UpdateTopTabVisuals(0);

        if (_gameState.CurrentStage != PracticeStage.Stage3_AiBattle)
        {
            _isSecurityPeekEnabled = false;
            StartGameForStage(PracticeStage.Stage3_AiBattle);
        }
        ShowToast(true, $"⚔️ [실전 대련 (AI)] 시작! 상대 덱: {_gameState.OpponentDeckName} — 상단 [👾 상대 덱] 선택 후 [🔄 재대전]으로 상대를 바꿀 수 있습니다.");
        RefreshAllSimulatorUi();
    }

    private void BtnTabSimulator_Click(object sender, RoutedEventArgs e)
    {
        UpdateTopTabVisuals(1);

        if (_gameState.CurrentStage == PracticeStage.Stage3_AiBattle)
        {
            _isSecurityPeekEnabled = true;
            StartGameForStage(PracticeStage.Stage1_Goldfish);
        }
        ShowToast(true, "🎯 [연습 (솔리테어)] 연습장 모드로 진입했습니다! 상단에서 [Stage 1: 솔리테어] 또는 [Stage 2: 블로커 돌파]를 선택할 수 있습니다.");
        RefreshAllSimulatorUi();
    }

    private void BtnTabDeckBuilder_Click(object sender, RoutedEventArgs e)
    {
        UpdateTopTabVisuals(2);
        RefreshCatalogUi();
        RefreshDeckBuilderUi();
        ShowToast(true, "🛠️ [덱 빌딩 & 도감] 7색 전체 TCG 카드와 6대 추천 메타 덱 프리셋, AI 자동 완성 기능을 사용할 수 있습니다.");
    }

    // ==================== 👾 상대 AI 덱 선택 (기초 덱 + 미러전 + 이름 붙여 저장한 내 덱들) ====================
    // 콤보 항목: 0 자동 배정 · 1~6 기초 덱 · 7 미러전 · 8~ 저장한 덱 (이름별 1개씩, 저장 / 삭제하면 다시 채운다)
    private const int MirrorChoiceIndex = 7;
    private int _opponentChoiceIndex;
    private string? _opponentSavedDeckName;   // 저장한 덱을 상대로 골랐을 때 그 덱 이름
    private bool _isSyncingOpponentCombo;

    private static readonly string[] OpponentChoiceLabels =
    {
        "🎲 자동 배정 (내 덱 카운터)",
        "🔥 [기초] ST-1 가이아 레드",
        "❄️ [기초] ST-2 코퀴토스 블루",
        "⚡ [기초] ST-3 헤븐즈 옐로우",
        "🌿 [기초] ST-4 기가 그린",
        "⚙️ [기초] ST-5 머신 블랙",
        "💜 [기초] ST-6 베놈 바이올렛",
        "🪞 미러전 (현재 내 출전 덱)"
    };

    private void InitOpponentDeckCombos()
    {
        _isSyncingOpponentCombo = true;
        CmbMatchOpponentDeck.Items.Clear();
        foreach (var label in OpponentChoiceLabels)
            CmbMatchOpponentDeck.Items.Add(new ComboBoxItem { Content = label });

        var savedDecks = _savedDeckStore.LoadAll();
        foreach (var deck in savedDecks)
            CmbMatchOpponentDeck.Items.Add(new ComboBoxItem { Content = $"🛠️ 내 덱: {deck.Name}", Tag = deck.Name });

        // 상대로 고른 저장 덱이 지워졌으면 자동 배정으로 돌아간다
        if (_opponentSavedDeckName != null)
        {
            int at = savedDecks.ToList().FindIndex(d => string.Equals(d.Name, _opponentSavedDeckName, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                _opponentChoiceIndex = OpponentChoiceLabels.Length + at;
            }
            else
            {
                _opponentSavedDeckName = null;
                _opponentChoiceIndex = 0;
            }
        }
        CmbMatchOpponentDeck.SelectedIndex = _opponentChoiceIndex;
        _isSyncingOpponentCombo = false;
    }

    private void CmbOpponentDeck_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingOpponentCombo || sender is not ComboBox combo || combo.SelectedIndex < 0)
            return;

        _opponentChoiceIndex = combo.SelectedIndex;
        _opponentSavedDeckName = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        string label = (combo.SelectedItem as ComboBoxItem)?.Content as string ?? OpponentChoiceLabels[0];
        ShowToast(true, $"👾 상대 AI 덱을 [{label}](으)로 설정했습니다. 다음 실전 대련(또는 [🔄 재대전])부터 적용됩니다.");
    }

    private void BtnRematchAi_Click(object sender, RoutedEventArgs e)
    {
        _isSecurityPeekEnabled = false;
        StartGameForStage(PracticeStage.Stage3_AiBattle);
        UpdateTopTabVisuals(0);
        RefreshAllSimulatorUi();
        ShowToast(true, $"🔄 새 실전 대련 시작! 상대 덱: {_gameState.OpponentDeckName}");
    }

    /// <summary>
    /// 현재 선택된 상대 덱 옵션을 실제 카드 리스트로 변환 (null = 자동 배정)
    /// </summary>
    private (List<CardData>? Main, List<CardData>? Digitama, string? Name) ResolveSelectedOpponentDeck()
    {
        if (_opponentSavedDeckName != null)
        {
            var saved = _savedDeckStore.Find(_opponentSavedDeckName);
            if (saved == null)
                return (null, null, null);
            var (main, digitama, _) = SavedDeckStore.Resolve(saved, CatalogById());
            if (main.Count < 10)
                return (null, null, null);   // 너무 적으면 자동 배정
            return (main, digitama, $"🛠️ 내 덱: {saved.Name} ({main.Count}장)");
        }

        switch (_opponentChoiceIndex)
        {
            case 1: { var (m, d) = StarterDecks.CreateSt1GaiaRedPreset(); return (m, d, OpponentChoiceLabels[1]); }
            case 2: { var (m, d) = StarterDecks.CreateSt2CocytusBluePreset(); return (m, d, OpponentChoiceLabels[2]); }
            case 3: { var (m, d) = StarterDecks.CreateSt3HeavensYellowPreset(); return (m, d, OpponentChoiceLabels[3]); }
            case 4: { var (m, d) = StarterDecks.CreateSt4GigaGreenPreset(); return (m, d, OpponentChoiceLabels[4]); }
            case 5: { var (m, d) = StarterDecks.CreateSt5MachineBlackPreset(); return (m, d, OpponentChoiceLabels[5]); }
            case 6: { var (m, d) = StarterDecks.CreateSt6VenomVioletPreset(); return (m, d, OpponentChoiceLabels[6]); }
            case MirrorChoiceIndex:
                return (_builderMainDeck.ToList(), _builderDigitamaDeck.ToList(), $"🪞 미러전: {_activeDeckTitle}");
            default:
                return (null, null, null);
        }
    }

    /// <summary>
    /// 모든 게임 시작은 이 메서드를 통해 진행 (AI 대전일 때만 선택한 상대 덱 적용)
    /// </summary>
    private readonly GameLogFile _gameLogFile = new();

    private void StartGameForStage(PracticeStage stage)
    {
        if (stage == PracticeStage.Stage3_AiBattle)
        {
            var (oppMain, oppDigitama, oppName) = ResolveSelectedOpponentDeck();
            _engine.StartNewGame(_builderMainDeck, _builderDigitamaDeck, stage: stage,
                opponentMainDeck: oppMain, opponentDigitamaDeck: oppDigitama, opponentDeckName: oppName);
        }
        else
        {
            _engine.StartNewGame(_builderMainDeck, _builderDigitamaDeck, stage: stage);
        }
        _gameLogFile.NewGame($"DIGIMON TCG {DigimonTcg.Core.AppVersion.Display} · {stage} · 내 덱: {_activeDeckTitle}{(stage == PracticeStage.Stage3_AiBattle ? $" · 상대 덱: {_gameState.OpponentDeckName}" : "")}");

        // 실전 AI 대전은 화면에 대련장이 처음 보일 때 코인 토스로 선공 / 후공을 정한다
        _coinTossPending = stage == PracticeStage.Stage3_AiBattle;

        // 새 게임: 이전 게임의 리플레이 / 결과 화면 정리
        _gameResultShown = false;
        _replayTimer?.Stop();
        _replayQueue.Clear();
        OpponentReplayOverlay.Visibility = Visibility.Collapsed;
        HideGameResult();
    }

    // ==================== 🎲 코인 토스 (실전 대련 선공 / 후공 결정) ====================
    private bool _coinTossPending;
    private bool _coinTossPlayerFirst;
    private bool _coinTossLanded;
    private DispatcherTimer? _coinTossTimer;

    private void TryStartCoinToss()
    {
        if (!_coinTossPending || CoinTossOverlay.Visibility == Visibility.Visible)
            return;
        if (_gameState.CurrentStage != PracticeStage.Stage3_AiBattle ||
            MainLobbyView.Visibility == Visibility.Visible ||
            WorkspaceContainerGrid.Visibility != Visibility.Visible ||
            SimulatorView.Visibility != Visibility.Visible)
            return;

        _coinTossPending = false;
        _coinTossPlayerFirst = Random.Shared.Next(2) == 0;
        _coinTossLanded = false;

        TxtCoinResult.Text = string.Empty;
        TxtCoinResultSub.Text = string.Empty;
        TxtCoinResult.BeginAnimation(OpacityProperty, null);
        TxtCoinResultSub.BeginAnimation(OpacityProperty, null);
        TxtCoinResult.Opacity = 0;
        TxtCoinResultSub.Opacity = 0;
        CoinTossOverlay.BeginAnimation(OpacityProperty, null);
        CoinTossOverlay.Opacity = 1;
        CoinTossOverlay.Visibility = Visibility.Visible;

        // 동전이 튀어 올라 회전하다가 결과 면으로 떨어진다 (앞면 ME / 뒷면 AI)
        const double flipSeconds = 1.6;
        int halfTurns = 2 * 7 + (_coinTossPlayerFirst ? 0 : 1);   // 짝수 번 뒤집히면 앞면, 홀수면 뒷면
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _coinTossTimer?.Stop();
        _coinTossTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _coinTossTimer.Tick += (_, _) =>
        {
            double t = Math.Min(1.0, clock.Elapsed.TotalSeconds / flipSeconds);
            double eased = 1 - Math.Pow(1 - t, 3);                 // 점점 느려지는 회전
            double angle = eased * halfTurns * Math.PI;
            double c = Math.Cos(angle);
            CoinFlipScale.ScaleY = Math.Max(0.04, Math.Abs(c));
            bool front = c >= 0;
            TxtCoinFace.Text = front ? "ME" : "AI";
            TxtCoinFace.Foreground = BrushFromHex(front ? "#0B4A7A" : "#7A0B2E");
            double lift = Math.Sin(t * Math.PI) * 170;               // 포물선으로 떴다가 내려옴
            CoinLift.Y = -lift;
            CoinShadow.Opacity = 1 - lift / 240;
            CoinShadow.Width = 150 - lift / 3;

            if (t >= 1.0)
            {
                _coinTossTimer!.Stop();
                LandCoin();
            }
        };
        _coinTossTimer.Start();
    }

    private void LandCoin()
    {
        if (_coinTossLanded)
            return;
        _coinTossLanded = true;

        CoinFlipScale.ScaleY = 1;
        CoinLift.Y = 0;
        TxtCoinFace.Text = _coinTossPlayerFirst ? "ME" : "AI";
        TxtCoinFace.Foreground = BrushFromHex(_coinTossPlayerFirst ? "#0B4A7A" : "#7A0B2E");

        TxtCoinResult.Text = _coinTossPlayerFirst ? "앞면! 내가 선공입니다" : "뒷면! 상대 AI 선공입니다";
        TxtCoinResult.Foreground = Theme.GetBrush(_coinTossPlayerFirst ? "Neo.Brush.CyanBright" : "Neo.Brush.RoseSoft");
        TxtCoinResultSub.Text = _coinTossPlayerFirst
            ? "선공은 메모리 0 · 드로우 없이 1턴을 시작합니다."
            : "상대가 먼저 1턴을 진행합니다. 후공인 나는 1턴째에도 1장 드로우합니다.";
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        TxtCoinResult.BeginAnimation(OpacityProperty, fadeIn);
        TxtCoinResultSub.BeginAnimation(OpacityProperty, fadeIn);

        _coinTossTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _coinTossTimer.Tick += (_, _) => FinishCoinToss();
        _coinTossTimer.Start();
    }

    private void FinishCoinToss()
    {
        _coinTossTimer?.Stop();
        if (CoinTossOverlay.Visibility != Visibility.Visible)
            return;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            CoinTossOverlay.Visibility = Visibility.Collapsed;
            CoinTossOverlay.BeginAnimation(OpacityProperty, null);
            CoinTossOverlay.Opacity = 1;
            // 코인 토스 화면이 사라진 뒤에 손패 확인 · 멀리건 화면을 띄운다
            // (아래 RefreshAllSimulatorUi 때는 코인 토스 화면이 아직 사라지는 중이라 멀리건 화면이 뜨지 않는다)
            TryShowMulliganChoice();
        };
        CoinTossOverlay.BeginAnimation(OpacityProperty, fade);

        // 공식 룰 5-2: 선공 결정 → 초기 손패 확인 · 멀리건 → 시큐리티 5장 → 게임 시작 (멀리건 화면이 이어서 뜬다)
        _engine.DecideFirstPlayer(_coinTossPlayerFirst);
        RefreshAllSimulatorUi();
        ShowToast(true, _coinTossPlayerFirst
            ? "🎲 코인 토스: 앞면 — 내가 선공! 초기 손패를 확인하고 멀리건 여부를 정하세요."
            : "🎲 코인 토스: 뒷면 — 상대 AI 선공! 초기 손패를 확인하고 멀리건 여부를 정하세요.");
    }

    // ==================== 🃏 게임 시작 전 초기 손패 확인 · 멀리건 (공식 룰 5-2) ====================
    private void TryShowMulliganChoice()
    {
        bool waiting = _gameState.CurrentPhase == Phase.Setup && !_gameState.IsGameOver;
        // 실전 대련은 코인 토스로 선공을 정한 뒤에 묻는다
        bool ready = waiting && !_coinTossPending && CoinTossOverlay.Visibility != Visibility.Visible
                     && (_gameState.CurrentStage != PracticeStage.Stage3_AiBattle || _engine.IsFirstPlayerDecided);
        if (!waiting)
        {
            MulliganOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        if (!ready || WorkspaceContainerGrid.Visibility != Visibility.Visible || SimulatorView.Visibility != Visibility.Visible)
            return;

        string first = _gameState.PlayerGoesFirst ? "내가 선공 (1턴째 드로우 없음)" : "상대 AI 선공 (나는 1턴째에도 1장 드로우)";
        TxtMulliganDetail.Text = _gameState.CanMulligan
            ? $"{first} · 마음에 들지 않으면 손패 5장을 덱에 섞고 다시 5장을 뽑을 수 있습니다 (1회). 시큐리티 5장은 손패를 확정한 뒤 덱 위에서 놓입니다."
            : $"{first} · 멀리건을 사용했습니다. 이 손패로 게임을 시작합니다.";

        MulliganHandPanel.Children.Clear();
        foreach (var card in _gameState.Player.Hand)
        {
            var frame = CreateCardVisualFrame(card, width: 120, height: 168);
            frame.Margin = new Thickness(6, 0, 6, 0);
            frame.MouseEnter += (_, _) => UpdateCardInspector(card);
            MulliganHandPanel.Children.Add(frame);
        }
        BtnRedrawHand.IsEnabled = _gameState.CanMulligan;
        BtnRedrawHand.Opacity = _gameState.CanMulligan ? 1.0 : 0.45;

        if (MulliganOverlay.Visibility != Visibility.Visible)
        {
            MulliganOverlay.Visibility = Visibility.Visible;
            MulliganOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }
    }

    private void BtnRedrawHand_Click(object sender, RoutedEventArgs e)
    {
        ApplyActionResult(_engine.ExecuteMulligan());   // 새 손패로 다시 그린다 (RefreshAllSimulatorUi → TryShowMulliganChoice)
    }

    private void BtnKeepHand_Click(object sender, RoutedEventArgs e)
    {
        MulliganOverlay.Visibility = Visibility.Collapsed;
        // 시큐리티 5장을 놓고 선공의 1턴 시작 (AI 선공이면 AI 턴을 진행한 뒤 리플레이로 보여 준다)
        ApplyActionResult(_engine.BeginGame());
    }

    private void CoinTossOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 회전 중 클릭 = 바로 착지, 착지 후 클릭 = 바로 대련 시작
        if (!_coinTossLanded)
        {
            _coinTossTimer?.Stop();
            LandCoin();
        }
        else
        {
            FinishCoinToss();
        }
    }

    private void ApplyPresetDeckAndSync(string deckTitle, (List<CardData> MainDeck, List<CardData> DigitamaDeck) preset)
    {
        _activeDeckTitle = deckTitle;
        _currentSavedDeckName = null;
        _builderMainDeck = preset.MainDeck;
        _builderDigitamaDeck = preset.DigitamaDeck;
        if (TxtLobbyActiveDeckName != null)
        {
            TxtLobbyActiveDeckName.Text = $"🎴 출전 덱: {_activeDeckTitle}";
        }
        StartGameForStage(_gameState.CurrentStage);
        RefreshCatalogUi();
        RefreshDeckBuilderUi();
        if (SimulatorView.Visibility == Visibility.Visible)
        {
            RefreshAllSimulatorUi();
        }
        ShowToast(true, $"✅ [{deckTitle}] (메인 50장 + 디지타마 4장) 장착 완료!");
    }

    private void BtnQuickDeckRed_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("🔥 [추천 1] 워그레이몬 & 오메가몬 초화력 비트 (50장)", StarterDecks.CreateSt1GaiaRedPreset());

    private void BtnQuickDeckBlue_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("❄️ [추천 2] 메탈가루몬 & 오메가몬 연타 (50장)", StarterDecks.CreateSt2CocytusBluePreset());

    private void BtnQuickDeckYellow_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("⚡ [추천 3] 세라피몬 & 홀리드라몬 천사 컨트롤 (50장)", StarterDecks.CreateSt3HeavensYellowPreset());

    private void BtnQuickDeckGreen_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("🌿 [추천 4] 헤라클레스캅테리몬 & 로제몬 (50장)", StarterDecks.CreateSt4GigaGreenPreset());

    private void BtnQuickDeckBlack_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("⚙️ [추천 5] 파워드라몬 & 워그레이몬 철벽 블로커 (50장)", StarterDecks.CreateSt5MachineBlackPreset());

    private void BtnQuickDeckPurple_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("🔮 [추천 6] 피에몬 & 데블몬 길동무 (50장)", StarterDecks.CreateSt6VenomVioletPreset());

    // ==================== 📊 우측 사이드바: 필드 디지몬 DP & 테이머 효과 증가치 인스펙터 ====================
    private void UpdateFieldDigimonBreakdown(CardInstance? instance, bool isPinned = false, bool isOpponent = false)
    {
        if (TxtFieldInspectHeader == null)
            return;

        if (instance == null)
        {
            if (_pinnedFieldInstance != null)
            {
                bool onPlayerField = _gameState.Player.BattleArea.Contains(_pinnedFieldInstance) ||
                                     ReferenceEquals(_gameState.Player.BreedingArea, _pinnedFieldInstance);
                bool onOppField = _gameState.DummyOpponent.BattleArea.Contains(_pinnedFieldInstance);
                if (onPlayerField || onOppField)
                {
                    instance = _pinnedFieldInstance;
                    isPinned = true;
                    isOpponent = onOppField;
                }
                else
                {
                    _pinnedFieldInstance = null;
                }
            }
        }

        if (instance == null)
        {
            TxtFieldInspectHeader.Text = "필드 카드를 마우스로 가리키거나 클릭하세요";
            TxtFieldInspectPinState.Text = "🖱️ 호버 / 클릭 확인";
            TxtFieldDpBreakdown.Text = "⚔️ 총 DP: - (기본 - / 진화원 +0 / 테이머 +0 / 스킬 +0)";
            TxtFieldSecBreakdown.Text = "💥 시큐리티 타격: - (기본 1장)";

            if (_gameState.Player.Tamers.Count > 0)
            {
                var tamerSummaries = _gameState.Player.Tamers
                    .GroupBy(t => t.CardId)
                    .Select(g => $"{g.First().DisplayName} x{g.Count()}");
                TxtFieldTamerAndSources.Text = $"🎖️ 현재 내 필드 활성 테이머: {string.Join(", ", tamerSummaries)}\n(필드의 디지몬에 마우스를 올리면 정확한 DP/시큐 증가치가 계산됩니다)";
            }
            else
            {
                TxtFieldTamerAndSources.Text = "🎖️ 활성 테이머 & 진화원 스택: 필드의 디지몬 또는 테이머에 마우스를 올리세요.";
            }
            return;
        }

        string sideLabel = isOpponent ? "[상대 필드]" : ReferenceEquals(_gameState.Player.BreedingArea, instance) ? "[내 육성존]" : "[내 배틀존]";
        TxtFieldInspectHeader.Text = $"{sideLabel} Lv.{instance.TopCard.Level} {instance.TopCard.DisplayName}";
        TxtFieldInspectPinState.Text = isPinned ? "📌 클릭 고정됨 (재클릭 해제)" : "🖱️ 마우스 오버 중 (클릭 시 고정)";
        TxtFieldInspectPinState.Foreground = BrushFromHex(isPinned ? "#F9E2AF" : "#A6E3A1");

        if (instance.TopCard.Type == CardType.Digimon)
        {
            int baseDp = instance.TopCard.DP;
            int inhDp = instance.GetInheritedDpBonus() + instance.EffectDpBonus;   // 진화원 · 카드 지속 효과
            int tamerDp = instance.TamerDpBonus;
            int skillDp = instance.TemporaryDpBonus;
            int totalBonusDp = instance.GetTotalBonusDP();
            int currentDp = instance.GetCurrentDP();

            TxtFieldDpBreakdown.Text =
                $"⚔️ 현재 총 DP: {currentDp:N0} (▲ +{totalBonusDp:N0} 증가)\n" +
                $"   └ 기본 {baseDp:N0} | 🧬진화원 +{inhDp:N0} | 🎖️테이머 +{tamerDp:N0} | ✨스킬/옵션 +{skillDp:N0}";

            int secCount = instance.GetSecurityAttackCount();
            int bonusSec = instance.GetTotalBonusSecurityAttack();
            TxtFieldSecBreakdown.Text =
                $"💥 시큐리티 타격: {secCount}장 (추가공격 +{bonusSec})\n" +
                $"   └ 기본 1장 | 🔥고유 +{instance.TopCard.SecurityAttackBonus} | 🧬진화원 +{instance.GetInheritedSecurityAttackBonus()} | 🎖️테이머 +{instance.TamerSecurityAttackBonus} | ✨스킬 +{instance.TemporarySecurityAttackBonus + instance.EffectSecurityAttackBonus}";
        }
        else
        {
            TxtFieldDpBreakdown.Text = "🥚 Lv.2 유년기 디지타마 (전투 DP 없음 — Lv.3 성장기로 진화시켜 배틀존으로 이동하세요)";
            TxtFieldSecBreakdown.Text = "🛡️ 안전지대 육성 중";
        }

        var detailLines = new List<string>();
        if (!isOpponent && _gameState.Player.Tamers.Count > 0)
        {
            var tamerNames = _gameState.Player.Tamers.GroupBy(t => t.CardId).Select(g => $"{g.First().DisplayName} x{g.Count()}");
            detailLines.Add($"🎖️ 테이머 오라 적용: {string.Join(", ", tamerNames)} (DP +{instance.TamerDpBonus:N0}, 시큐 +{instance.TamerSecurityAttackBonus})");
        }
        if (instance.ActiveSkillBuffLabels.Count > 0)
        {
            detailLines.Add($"✨ 활성 스킬 버프: {string.Join(" / ", instance.ActiveSkillBuffLabels)}");
        }

        var evoSources = instance.GetEvolutionStackBottomUp();
        if (evoSources.Count > 0)
        {
            var srcTexts = evoSources.Select(s =>
            {
                string bonus = s.InheritedSecurityAttackBonus > 0
                    ? $"[💥시큐+{s.InheritedSecurityAttackBonus}]"
                    : s.InheritedDpBonus > 0
                        ? $"[▲DP+{s.InheritedDpBonus:N0}]"
                        : "";
                return $"Lv.{s.Level} {s.DisplayName}{bonus}";
            });
            detailLines.Add($"🧬 진화원({evoSources.Count}장): {string.Join(" ➔ ", srcTexts)}");
        }
        else
        {
            detailLines.Add("🧬 진화원 스택: 없음 (바닥 등장)");
        }

        if (instance.LinkedCards.Count > 0)
            detailLines.Add($"🔗 링크 카드({instance.LinkedCards.Count}장): {string.Join(", ", instance.LinkedCards.Select(c => c.DisplayName))} (링크 DP는 위 DP에 포함)");

        TxtFieldTamerAndSources.Text = string.Join("\n", detailLines);
    }

    private void UpdateTamerBreakdownOnSidebar(CardData tamer, int count)
    {
        if (TxtFieldInspectHeader == null)
            return;

        TxtFieldInspectHeader.Text = $"[내 테이머 존] 🎖️ {tamer.DisplayName} x{count}장";
        TxtFieldInspectPinState.Text = "🎖️ 테이머 지속 효과";

        string mainEffect = tamer.DisplayEffectText.Split('\n')[0];
        string dpAura = tamer.CardId == "ST1-12"
            ? $"[자신의 턴] 아군 모든 디지몬 DP +{count * 1000:N0}"
            : tamer.CardId == "ST3-12"
                ? $"[상대의 턴] 내 시큐리티 디지몬 DP +{count * 2000:N0}"
                : "없음";

        TxtFieldDpBreakdown.Text = $"⚔️ 필드 DP 증가치: {dpAura}";
        TxtFieldSecBreakdown.Text = $"⚡ 테이머 효과 (x{count}): {mainEffect}";
        TxtFieldTamerAndSources.Text = $"📜 원문 효과: {tamer.DisplayEffectText}";
    }

    // ==================== ⚡ 2-3. 궁극체(Lv.6) / 초궁극체(Lv.7) 특수 소환 컷인 연출 ====================
    private void ShowUltimateCutIn(CardInstance instance, bool isEvolution)
    {
        var card = instance.TopCard;
        if (card.Type != CardType.Digimon || card.Level < 6)
            return;

        _ultimateCutInTimer?.Stop();
        StopCutInVideo();

        var colorBrush = GetCardColorBrush(card.Color);
        Color auraColor = card.Level >= 7 ? (Color)ColorConverter.ConvertFromString("#F9E2AF") : colorBrush.Color;

        UltimateCutInBanner.BorderBrush = colorBrush;
        UltimateCutInBanner.Background = new LinearGradientBrush(
            Color.FromArgb(0, auraColor.R, auraColor.G, auraColor.B),
            Color.FromArgb(0, auraColor.R, auraColor.G, auraColor.B), 0)
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, auraColor.R, auraColor.G, auraColor.B), 0),
                new GradientStop(Color.FromArgb(90, auraColor.R, auraColor.G, auraColor.B), 0.5),
                new GradientStop(Color.FromArgb(0, auraColor.R, auraColor.G, auraColor.B), 1)
            }
        };
        TxtUltimateCutInTier.Text = card.Level >= 7
            ? $"🌌 LV.{card.Level} ULTRA DIGIVOLUTION // 초궁극체 강림!"
            : isEvolution
                ? $"⚡ LV.{card.Level} ULTIMATE DIGIVOLUTION // 궁극체 진화!"
                : $"⚡ LV.{card.Level} ULTIMATE SUMMON // 궁극체 전장 강림!";
        TxtUltimateCutInTier.Foreground = card.Level >= 7 ? BrushFromHex("#F9E2AF") : colorBrush;

        TxtUltimateCutInName.Text = card.DisplayName;
        TxtUltimateCutInStats.Text =
            $"⚔️ DP {instance.GetCurrentDP():N0} (▲+{instance.GetTotalBonusDP():N0})   |   💥 시큐리티 타격 {instance.GetSecurityAttackCount()}장";
        TxtUltimateCutInEffect.Text = string.IsNullOrWhiteSpace(card.DisplayEffectText)
            ? "압도적인 궁극체 전투력으로 상대 필드를 제압합니다!"
            : card.DisplayEffectText;

        // 배경 오라 + 카드 색 집중선
        CutInAura.Background = new RadialGradientBrush(Color.FromArgb(150, auraColor.R, auraColor.G, auraColor.B), Color.FromArgb(0, 0, 0, 0))
        {
            Center = new Point(0.5, 0.42),
            GradientOrigin = new Point(0.5, 0.42),
            RadiusX = 0.6,
            RadiusY = 0.7
        };
        CutInSpeedLines.Fill = CreateCutInSpeedLinesBrush(Color.FromArgb(150, auraColor.R, auraColor.G, auraColor.B));

        // 영상이 있으면 영상, 없으면 카드 일러스트(텍스트 박스를 뺀 그림 부분만 잘라 크게)로 포효 연출
        string? videoPath = FindCutInVideo(card);
        bool playVideo = videoPath != null;
        CutInVideo.Visibility = playVideo ? Visibility.Visible : Visibility.Collapsed;
        CutInArt.Visibility = playVideo ? Visibility.Collapsed : Visibility.Visible;
        // 일러스트는 영상 재생이 실패할 때의 대체 화면으로도 쓰이므로 항상 준비해 둔다
        var bmp = GetCardBitmap(card);
        CutInArt.Fill = bmp == null
            ? new RadialGradientBrush(auraColor, Colors.Transparent)
            : new ImageBrush(bmp)
            {
                Viewbox = new Rect(0.04, 0.09, 0.92, 0.56),
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.UniformToFill
            };
        if (playVideo)
        {
            CutInVideo.Source = new Uri(videoPath!, UriKind.Absolute);
            CutInVideo.Play();
        }

        UltimateCutInOverlay.BeginAnimation(OpacityProperty, null);
        UltimateCutInOverlay.Opacity = 1;
        UltimateCutInOverlay.Visibility = Visibility.Visible;
        PlayRoarCutInAnimation(withArtZoom: !playVideo);

        // 영상은 끝날 때 닫히고(최대 12초), 일러스트 연출은 3.2초 뒤에 닫힌다
        _ultimateCutInTimer = new DispatcherTimer
        {
            Interval = playVideo ? TimeSpan.FromSeconds(12) : TimeSpan.FromMilliseconds(3200)
        };
        _ultimateCutInTimer.Tick += (_, _) => CloseUltimateCutIn();
        _ultimateCutInTimer.Start();
    }

    private void PlayRoarCutInAnimation(bool withArtZoom)
    {
        var easeOut = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };

        // 화면 플래시
        CutInFlash.BeginAnimation(OpacityProperty, new DoubleAnimation(0.95, 0, TimeSpan.FromMilliseconds(380)));

        // 줌 펀치 → 천천히 다가오는 카메라
        var zoom = new DoubleAnimationUsingKeyFrames();
        zoom.KeyFrames.Add(new DiscreteDoubleKeyFrame(withArtZoom ? 1.55 : 1.15, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        zoom.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), easeOut));
        zoom.KeyFrames.Add(new LinearDoubleKeyFrame(withArtZoom ? 1.12 : 1.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3200))));
        CutInArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        CutInArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);

        // 포효 순간의 카메라 흔들림 (점점 약해짐)
        var rng = new Random();
        var shakeX = new DoubleAnimationUsingKeyFrames();
        var shakeY = new DoubleAnimationUsingKeyFrames();
        shakeX.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        shakeY.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        for (int i = 0; i < 16; i++)
        {
            double power = 18 * (1 - i / 16.0);
            var t = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220 + i * 40));
            shakeX.KeyFrames.Add(new LinearDoubleKeyFrame((rng.NextDouble() * 2 - 1) * power, t));
            shakeY.KeyFrames.Add(new LinearDoubleKeyFrame((rng.NextDouble() * 2 - 1) * power, t));
        }
        var settle = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900));
        shakeX.KeyFrames.Add(new LinearDoubleKeyFrame(0, settle));
        shakeY.KeyFrames.Add(new LinearDoubleKeyFrame(0, settle));
        CutInShake.BeginAnimation(TranslateTransform.XProperty, shakeX);
        CutInShake.BeginAnimation(TranslateTransform.YProperty, shakeY);

        // 집중선 회전
        CutInSpeedLinesRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 24, TimeSpan.FromMilliseconds(3200)));

        // 충격파 3연발
        var waves = new[] { CutInShockwave1, CutInShockwave2, CutInShockwave3 };
        for (int i = 0; i < waves.Length; i++)
        {
            var begin = TimeSpan.FromMilliseconds(200 + i * 260);
            var scale = (ScaleTransform)waves[i].RenderTransform;
            var grow = new DoubleAnimation(0.2, 5.5, TimeSpan.FromMilliseconds(800)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            waves[i].BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 0, TimeSpan.FromMilliseconds(800)) { BeginTime = begin });
        }

        // "크아아아앙!!" 효과음 문자: 쾅 하고 커졌다가 떨리며 사라짐
        var roarScale = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(180) };
        roarScale.KeyFrames.Add(new DiscreteDoubleKeyFrame(2.4, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        roarScale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), easeOut));
        roarScale.KeyFrames.Add(new LinearDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))));
        CutInRoarScale.BeginAnimation(ScaleTransform.ScaleXProperty, roarScale);
        CutInRoarScale.BeginAnimation(ScaleTransform.ScaleYProperty, roarScale);
        var roarFade = new DoubleAnimationUsingKeyFrames();
        roarFade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        roarFade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        roarFade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
        roarFade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000))));
        TxtCutInRoar.BeginAnimation(OpacityProperty, roarFade);

        // 이름 슬라이드 인
        CutInTextShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-260, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(250), EasingFunction = easeOut });
        CutInTextPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(250) });
    }

    private static Brush CreateCutInSpeedLinesBrush(Color color)
    {
        // 중심에서 뻗어 나가는 가는 쐐기 60개 = 만화식 집중선
        var group = new GeometryGroup();
        for (int i = 0; i < 60; i++)
        {
            double angle = i * 6 * Math.PI / 180 + (i % 2 == 0 ? 0 : 0.03);
            double half = (i % 3 == 0 ? 1.1 : 0.5) * Math.PI / 180;
            var tip = new Point(0, 0);
            var a = new Point(Math.Cos(angle - half) * 1000, Math.Sin(angle - half) * 1000);
            var b = new Point(Math.Cos(angle + half) * 1000, Math.Sin(angle + half) * 1000);
            group.Children.Add(new PathGeometry(new[]
            {
                new PathFigure(tip, new PathSegment[] { new LineSegment(a, false), new LineSegment(b, false) }, true)
            }));
        }
        var brush = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(color), null, group)) { Stretch = Stretch.Uniform };
        brush.Freeze();
        return brush;
    }

    /// <summary>data/cutins/ 에서 {카드번호} 또는 {디지몬 이름} 으로 된 영상(.mp4 / .wmv / .mov)을 찾는다.</summary>
    private string? FindCutInVideo(CardData card) => FindCutInVideoFile(card.CardId, card.Name);

    /// <summary>data/cutins/{이름}.mp4|.wmv|.mov 중 처음 존재하는 파일 (예: victory, defeat, BT1-084, 워그레이몬)</summary>
    private string? FindCutInVideoFile(params string[] baseNames)
    {
        string? dataDir = Path.GetDirectoryName(_catalogService.ImagesDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (dataDir == null)
            return null;

        string cutInDir = Path.Combine(dataDir, "cutins");
        if (!Directory.Exists(cutInDir))
            return null;

        foreach (string baseName in baseNames)
        {
            foreach (string ext in new[] { ".mp4", ".wmv", ".mov" })
            {
                string path = Path.Combine(cutInDir, baseName + ext);
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    private void StopCutInVideo()
    {
        CutInVideo.Stop();
        CutInVideo.Source = null;
    }

    private void CloseUltimateCutIn()
    {
        _ultimateCutInTimer?.Stop();
        if (UltimateCutInOverlay.Visibility != Visibility.Visible)
            return;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) =>
        {
            UltimateCutInOverlay.Visibility = Visibility.Collapsed;
            UltimateCutInOverlay.BeginAnimation(OpacityProperty, null);
            UltimateCutInOverlay.Opacity = 1;
            StopCutInVideo();
        };
        UltimateCutInOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void CutInVideo_MediaEnded(object sender, RoutedEventArgs e) => CloseUltimateCutIn();

    private void CutInVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // 코덱 문제 등으로 영상 재생이 실패하면 일러스트 연출로 대체
        CutInVideo.Visibility = Visibility.Collapsed;
        CutInArt.Visibility = Visibility.Visible;
        PlayRoarCutInAnimation(withArtZoom: true);
        if (_ultimateCutInTimer != null)
        {
            _ultimateCutInTimer.Stop();
            _ultimateCutInTimer.Interval = TimeSpan.FromMilliseconds(3200);
            _ultimateCutInTimer.Start();
        }
    }

    private void UltimateCutInOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloseUltimateCutIn();

    private void ExecutePlayCardWithEffects(CardData card, CardInstance? target = null)
    {
        var res = _engine.PlayCard(card, target);
        ApplyActionResult(res);
        if (res.CanExecute && card.Type == CardType.Digimon && !card.IsDual && card.Level >= 6)
        {
            var spawned = _gameState.Player.BattleArea.LastOrDefault(d => d.TopCard.CardId == card.CardId);
            if (spawned != null)
            {
                ShowUltimateCutIn(spawned, isEvolution: false);
                UpdateFieldDigimonBreakdown(spawned, isPinned: false);
            }
        }
    }

    private void ExecuteEvolveDigimonWithEffects(CardInstance target, CardData evoCard)
    {
        var res = _engine.EvolveDigimon(target, evoCard);
        ApplyActionResult(res);
        if (res.CanExecute)
        {
            UpdateFieldDigimonBreakdown(target, isPinned: ReferenceEquals(_pinnedFieldInstance, target));
            if (evoCard.Type == CardType.Digimon && evoCard.Level >= 6)
            {
                ShowUltimateCutIn(target, isEvolution: true);
            }
        }
    }

    private void AutoPassBreedingIfNeeded()
    {
        if (_gameState.CurrentPhase == Phase.Breeding && !_gameState.IsGameOver)
        {
            _engine.PassBreedingPhase();
        }
    }

    // ==================== 토스트 가이드 배너 ====================
    private void ShowToast(bool isSuccessOrGuide, string message)
    {
        if (isSuccessOrGuide)
        {
            ToastBanner.Background = BrushFromHex("#E60E1729");
            ToastBanner.BorderBrush = BrushFromHex("#6638BDF8");
            ToastBadge.Text = "💡 가이드 / 승인";
            ToastBadge.Foreground = BrushFromHex("#38BDF8");
        }
        else
        {
            ToastBanner.Background = BrushFromHex("#E62A0F1E");
            ToastBanner.BorderBrush = BrushFromHex("#99F43F75");
            ToastBadge.Text = "⚠️ 규칙 위반 차단";
            ToastBadge.Foreground = BrushFromHex("#F38BA8");
        }
        ToastMessageText.Text = message;
    }

    private void ApplyActionResult(ActionResult result)
    {
        if (result.CanExecute)
        {
            ShowToast(true, result.GuideMessage);
        }
        else
        {
            ShowToast(false, result.Reason);
        }
        RefreshAllSimulatorUi();
    }

    private void BtnCancelPendingAction_Click(object sender, RoutedEventArgs e)
    {
        _pendingHandCard = null;
        _pendingAttackerInstance = null;
        ShowToast(true, "카드 사용/진화/공격 대상 선택을 취소했습니다.");
        RefreshAllSimulatorUi();
    }

    // ==================== 인게임 시뮬레이터 UI 렌더링 ====================
    private void RefreshAllSimulatorUi()
    {
        if (_pendingHandCard != null &&
            (!_gameState.Player.Hand.Contains(_pendingHandCard) ||
             _gameState.CurrentPhase != Phase.Main ||
             _gameState.IsGameOver))
        {
            _pendingHandCard = null;
        }

        if (_pendingAttackerInstance != null &&
            (!_gameState.Player.BattleArea.Contains(_pendingAttackerInstance) ||
             _pendingAttackerInstance.IsSuspended ||
             _gameState.CurrentPhase != Phase.Main ||
             _gameState.IsGameOver))
        {
            _pendingAttackerInstance = null;
        }

        if (MainLobbyView.Visibility != Visibility.Visible && SimulatorView.Visibility == Visibility.Visible)
        {
            UpdateTopTabVisuals(_gameState.CurrentStage == PracticeStage.Stage3_AiBattle ? 0 : 1);
        }

        RenderMemoryGauge();
        RenderTurnAndPhases();
        RenderOpponentSecurity();
        RenderOpponentBattleArea();
        RenderBreedingArea();
        RenderBattleArea();
        UpdateFieldDigimonBreakdown(null);
        RenderHand();
        RenderDuelPlates();
        RenderTimeAttackHudAndLogs();
        TryStartCoinToss();
        TryShowMulliganChoice();
        TryStartOpponentReplay();
        TryShowBlockChoice();
        TryShowGameResult();
    }

    // ==================== 🤖 상대 턴 리플레이 (상대가 한 행동을 한 장면씩 보여 줌) ====================
    private readonly Queue<OpponentActionEvent> _replayQueue = new();
    private int _replayTotal;
    private DispatcherTimer? _replayTimer;
    private bool IsReplayRunning => OpponentReplayOverlay.Visibility == Visibility.Visible;

    private void TryStartOpponentReplay()
    {
        if (IsReplayRunning || _gameState.OpponentTurnEvents.Count == 0)
            return;
        if (WorkspaceContainerGrid.Visibility != Visibility.Visible || SimulatorView.Visibility != Visibility.Visible)
            return;

        _replayQueue.Clear();
        foreach (var ev in _gameState.OpponentTurnEvents)
            _replayQueue.Enqueue(ev);
        _gameState.OpponentTurnEvents.Clear();
        _replayTotal = _replayQueue.Count;

        OpponentReplayOverlay.BeginAnimation(OpacityProperty, null);
        OpponentReplayOverlay.Opacity = 1;
        OpponentReplayOverlay.Visibility = Visibility.Visible;
        ShowNextReplayStep();
    }

    private void ShowNextReplayStep()
    {
        _replayTimer?.Stop();
        if (_replayQueue.Count == 0)
        {
            EndOpponentReplay();
            return;
        }

        var ev = _replayQueue.Dequeue();
        TxtReplayStep.Text = $"{_replayTotal - _replayQueue.Count} / {_replayTotal}";
        TxtReplayTitle.Text = ev.Title;
        TxtReplayDetail.Text = ev.Detail;

        var (kindLabel, accentKey) = ev.Kind switch
        {
            OpponentActionKind.TurnStart => ("TURN START", "Neo.Brush.RoseSoft"),
            OpponentActionKind.Breeding => ("BREEDING", "Neo.Brush.Green"),
            OpponentActionKind.Evolve => ("DIGIVOLVE", "Neo.Brush.Violet"),
            OpponentActionKind.Play => ("PLAY", "Neo.Brush.CyanBright"),
            OpponentActionKind.Tamer => ("TAMER", "Neo.Brush.Gold"),
            OpponentActionKind.Attack => ("ATTACK", "Neo.Brush.Rose"),
            OpponentActionKind.Block => ("BLOCK", "Neo.Brush.Green"),
            OpponentActionKind.DirectAttack => ("DIRECT ATTACK", "Neo.Brush.Rose"),
            OpponentActionKind.TurnEnd => ("TURN END", "Neo.Brush.Cyan"),
            OpponentActionKind.Effect => ("EFFECT", "Neo.Brush.Gold"),
            _ => ev.Title.Contains("역격파") || ev.Title.Contains("테이머") ? ("SECURITY CHECK", "Neo.Brush.Green") : ("SECURITY CHECK", "Neo.Brush.Rose")
        };
        var accent = Theme.GetBrush(accentKey);
        TxtReplayKind.Text = kindLabel;
        TxtReplayKind.Foreground = accent;
        ReplayKindChip.BorderBrush = accent;
        ReplayKindChip.Background = BrushFromHex("#40000000");
        ReplayBand.BorderBrush = accent;

        // 행동한 카드 (→ 대상 카드: 막은 블로커 / 오픈된 내 시큐리티)
        ReplayCardSlot.Content = ev.Card == null ? null : CreateReplayCard(ev.Card);
        ReplaySubCardSlot.Content = ev.SubCard == null ? null : CreateReplayCard(ev.SubCard);
        TxtReplayArrow.Visibility = ev.Card != null && ev.SubCard != null ? Visibility.Visible : Visibility.Collapsed;
        ReplayCardSlot.Visibility = ev.Card == null ? Visibility.Collapsed : Visibility.Visible;
        ReplaySubCardSlot.Visibility = ev.SubCard == null ? Visibility.Collapsed : Visibility.Visible;

        // 장면 전환: 오른쪽에서 밀려 들어오며 나타남
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ReplayContentShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(70, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        ReplayContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));

        bool quick = ev.Kind is OpponentActionKind.TurnStart or OpponentActionKind.TurnEnd;
        _replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(quick ? 1000 : 1500) };
        _replayTimer.Tick += (_, _) => ShowNextReplayStep();
        _replayTimer.Start();
    }

    private FrameworkElement CreateReplayCard(CardData card)
    {
        var frame = CreateCardVisualFrame(card, width: 150, height: 210);
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = GetCardColorBrush(card.Color),
            BorderThickness = new Thickness(2),
            Effect = Theme.GetEffect("Neo.Shadow.Float"),
            Child = frame
        };
    }

    private void EndOpponentReplay()
    {
        _replayTimer?.Stop();
        _replayQueue.Clear();
        if (!IsReplayRunning)
            return;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            OpponentReplayOverlay.Visibility = Visibility.Collapsed;
            OpponentReplayOverlay.BeginAnimation(OpacityProperty, null);
            OpponentReplayOverlay.Opacity = 1;
            // 리플레이가 끝난 뒤 블록 선택 · 패배 등 결과 화면이 필요한지 다시 확인
            TryShowBlockChoice();
            TryShowGameResult();
        };
        OpponentReplayOverlay.BeginAnimation(OpacityProperty, fade);
    }

    // ==================== 🛡️ 블록 선택 (AI 어택을 ≪블로커≫로 막을지 — 공식 룰상 방어 측의 선택) ====================
    private void TryShowBlockChoice()
    {
        var pending = _gameState.PendingBlock;
        if (pending == null)
        {
            BlockChoiceOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        // 어택 선언까지의 상대 행동 리플레이를 먼저 다 보여 준 뒤 묻는다
        if (IsReplayRunning)
            return;
        if (WorkspaceContainerGrid.Visibility != Visibility.Visible || SimulatorView.Visibility != Visibility.Visible)
            return;

        var attacker = pending.Attacker;
        BlockAttackerSlot.Content = CreateReplayCard(attacker.TopCard);
        TxtBlockTitle.Text = $"AI [{attacker.TopCard.DisplayName}]의 어택! 블록하시겠습니까?";
        TxtBlockDetail.Text = $"상대 DP {pending.AttackerDp:N0} · 시큐리티 체크 {pending.SecurityChecks}장 — 막을 ≪블로커≫를 고르거나, 블록하지 않고 시큐리티로 받으세요. (예상 결과는 DP 기준)";

        int mySecurity = _gameState.Player.SecurityStack.Count;
        TxtNoBlockOutcome.Text = mySecurity == 0
            ? "⚠️ 내 시큐리티 0장 — 다이렉트 어택으로 패배합니다!"
            : $"내 시큐리티 {Math.Min(pending.SecurityChecks, mySecurity)}장 체크 (남은 시큐리티 {mySecurity}장)";
        TxtNoBlockOutcome.Foreground = mySecurity == 0 ? BrushFromHex("#F38BA8") : BrushFromHex("#CDD6F4");

        BlockCandidatesPanel.Children.Clear();
        foreach (var blocker in pending.Candidates)
            BlockCandidatesPanel.Children.Add(CreateBlockCandidateButton(blocker, pending.AttackerDp));

        if (BlockChoiceOverlay.Visibility != Visibility.Visible)
        {
            BlockChoiceOverlay.Visibility = Visibility.Visible;
            BlockChoiceOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }
    }

    private Button CreateBlockCandidateButton(CardInstance blocker, int attackerDp)
    {
        int dp = blocker.GetCurrentDP();
        var (outcome, color) = dp > attackerDp ? ("✅ 격파 — 내 블로커 생존", "#A6E3A1")
            : dp == attackerDp ? ("💥 동귀어진 — 양쪽 모두 소멸", "#F9E2AF")
            : ("💀 희생 — 내 블로커 소멸 (시큐리티는 지켜짐)", "#F38BA8");

        var content = new StackPanel { Width = 150 };
        content.Children.Add(CreateCardVisualFrame(blocker.TopCard, width: 110, height: 154));
        content.Children.Add(new TextBlock
        {
            Text = $"{blocker.TopCard.DisplayName} · DP {dp:N0}",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 2)
        });
        content.Children.Add(new TextBlock
        {
            Text = outcome,
            Foreground = BrushFromHex(color),
            FontSize = 11.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var btn = new Button
        {
            Content = content,
            Style = (Style)FindResource("Neo.Button.Ghost"),
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(10),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = $"[{blocker.TopCard.DisplayName}]을(를) 레스트하고 이 어택을 대신 받습니다"
        };
        btn.MouseEnter += (_, _) => UpdateCardInspector(blocker.TopCard);
        btn.Click += (_, _) => ChooseBlock(blocker);
        return btn;
    }

    private void BtnNoBlock_Click(object sender, RoutedEventArgs e) => ChooseBlock(null);

    private void ChooseBlock(CardInstance? blocker)
    {
        BlockChoiceOverlay.Visibility = Visibility.Collapsed;
        // 결과(블록 배틀 / 시큐리티 체크)와 남은 상대 턴은 리플레이로 이어서 보여 주고, 다음 어택이 있으면 다시 묻는다
        ApplyActionResult(_engine.ResolvePendingBlock(blocker));
    }

    private void OpponentReplayOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowNextReplayStep();

    private void BtnReplaySkip_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        EndOpponentReplay();
    }

    // ==================== 🏁 승리 / 패배 화면 ====================
    private bool _gameResultShown;

    private void TryShowGameResult()
    {
        if (!_gameState.IsGameOver || _gameResultShown || IsReplayRunning)
            return;
        if (WorkspaceContainerGrid.Visibility != Visibility.Visible || SimulatorView.Visibility != Visibility.Visible)
            return;

        _gameResultShown = true;
        bool win = _gameState.IsVictory;

        TxtResultTitle.Text = win ? "VICTORY" : "DEFEAT";
        TxtResultTitle.Foreground = win
            ? Theme.GetBrush("Duel.Brush.LogoFill")
            : new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"), (Color)ColorConverter.ConvertFromString("#7F1D1D"), 90);
        ResultTitleGlow.Color = (Color)ColorConverter.ConvertFromString(win ? "#F9E2AF" : "#F43F75");
        TxtResultSub.Text = win ? "★ 승리했습니다! ★" : "패배했습니다...";
        TxtResultReason.Text = string.IsNullOrWhiteSpace(_gameState.GameOverReason) ? "-" : _gameState.GameOverReason;
        TxtResultStats.Text =
            $"TURN {_gameState.CurrentTurn} · 상대 시큐리티 {_gameState.DummyOpponent.SecurityStack.Count}장 남음 · 내 시큐리티 {_gameState.Player.SecurityStack.Count}장 남음 · 진화 {_gameState.TotalEvolutionsCount}회";

        Color auraColor = (Color)ColorConverter.ConvertFromString(win ? "#F9E2AF" : "#7F1D1D");
        ResultAura.Background = new RadialGradientBrush(Color.FromArgb(win ? (byte)120 : (byte)150, auraColor.R, auraColor.G, auraColor.B), Colors.Transparent)
        {
            RadiusX = 0.6,
            RadiusY = 0.7
        };
        ResultRays.Fill = CreateCutInSpeedLinesBrush(Color.FromArgb(140, auraColor.R, auraColor.G, auraColor.B));
        ResultRays.Visibility = win ? Visibility.Visible : Visibility.Collapsed;

        // data/cutins/victory.mp4 · defeat.mp4 가 있으면 배경 영상
        string? video = FindCutInVideoFile(win ? "victory" : "defeat");
        ResultVideo.Visibility = video != null ? Visibility.Visible : Visibility.Collapsed;
        if (video != null)
        {
            ResultVideo.Source = new Uri(video, UriKind.Absolute);
            ResultVideo.Play();
        }

        GameResultOverlay.Visibility = Visibility.Visible;
        GameResultOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(win ? 200 : 700)));

        // 승리: 플래시 + 쾅 하고 꽂히는 타이틀 + 회전 광선 / 패배: 천천히 가라앉는 타이틀
        var easeOut = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        var titleScale = win
            ? new DoubleAnimation(2.6, 1.0, TimeSpan.FromMilliseconds(420)) { EasingFunction = easeOut }
            : new DoubleAnimation(1.25, 1.0, TimeSpan.FromMilliseconds(1600)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ResultTitleScale.BeginAnimation(ScaleTransform.ScaleXProperty, titleScale);
        ResultTitleScale.BeginAnimation(ScaleTransform.ScaleYProperty, titleScale);
        TxtResultTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(win ? 200 : 1400)));
        if (win)
        {
            ResultFlash.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(500)));
            ResultRaysRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(40)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        ResultButtons.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(win ? 700 : 1600) });
    }

    private void HideGameResult()
    {
        ResultVideo.Stop();
        ResultVideo.Source = null;
        ResultRaysRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        GameResultOverlay.Visibility = Visibility.Collapsed;
    }

    private void BtnResultRetry_Click(object sender, RoutedEventArgs e)
    {
        HideGameResult();
        if (_gameState.CurrentStage == PracticeStage.Stage3_AiBattle)
        {
            BtnRematchAi_Click(sender, e);
            return;
        }
        StartGameForStage(_gameState.CurrentStage);
        RefreshAllSimulatorUi();
        ShowToast(true, "🔄 같은 스테이지로 새 연습 게임을 시작했습니다!");
    }

    private void BtnResultClose_Click(object sender, RoutedEventArgs e) => HideGameResult();

    private void BtnResultLobby_Click(object sender, RoutedEventArgs e)
    {
        HideGameResult();
        BtnNavHome_Click(sender, e);
    }

    private void ResultVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) => ResultVideo.Visibility = Visibility.Collapsed;

    // ==================== 🔮 중앙 네온 메모리 코어 (+10 내 쪽 ← 0 → -10 상대 쪽) ====================
    private const int MemoryCoreCells = 21;
    private const double MemoryCoreIndicatorSize = 34;
    private int? _memoryCoreShownValue;

    private void RenderMemoryGauge()
    {
        int memory = Math.Clamp(_gameState.MemoryGauge, -10, 10);
        bool playerSide = memory >= 0;

        // 공식 플레이매트의 메모리 게이지처럼 10 … 1 · 0 · 1 … 10 숫자 원형 칸 (왼쪽 = 내 쪽, 오른쪽 = 상대 쪽)
        MemoryGaugeGrid.Children.Clear();
        var monoFont = (FontFamily)FindResource("Neo.Font.Mono");
        for (int m = 10; m >= -10; m--)
        {
            bool isCurrent = memory == m;
            bool onPlayerSide = m > 0;
            // 현재 값과 0 사이(에너지가 넘어온 구간)는 밝게, 나머지는 어둡게
            bool energized = playerSide ? (m >= 0 && m <= memory) : (m <= 0 && m >= memory);
            string sideBrush = m == 0 ? "Neo.Brush.Text.Primary" : onPlayerSide ? "Neo.Brush.Cyan" : "Neo.Brush.RoseSoft";

            MemoryGaugeGrid.Children.Add(new Border
            {
                Width = m == 0 ? 26 : 22,
                Height = m == 0 ? 26 : 22,
                CornerRadius = new CornerRadius(13),
                Background = Theme.GetBrush("Neo.Brush.SurfaceInput"),
                BorderBrush = Theme.GetBrush(sideBrush),
                BorderThickness = new Thickness(m == 0 ? 2 : 1.2),
                Opacity = isCurrent ? 0 : energized ? 1.0 : 0.45,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = Math.Abs(m).ToString(),
                    FontFamily = monoFont,
                    FontSize = m == 0 ? 11 : 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.GetBrush(sideBrush),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        TxtMemoryCoreValue.Text = Math.Abs(memory).ToString();
        MemoryCoreOrb.Fill = Theme.GetBrush(playerSide ? "Neo.Brush.Button.Cyan" : "Neo.Brush.Button.Rose");
        MemoryCoreOrb.Effect = Theme.GetEffect(playerSide ? "Neo.Glow.Cyan" : "Neo.Glow.Rose");
        MemoryCoreHalo.Fill = Theme.GetBrush(playerSide ? "Neo.Brush.Cyan" : "Neo.Brush.Rose");

        PlaceMemoryCoreIndicator(memory, animate: _memoryCoreShownValue != null && _memoryCoreShownValue != memory);
        _memoryCoreShownValue = memory;

        TxtMemoryStatus.Text = _gameState.MemoryGauge >= 0
            ? $"메모리 +{_gameState.MemoryGauge} · 내 턴 유지"
            : $"메모리 {_gameState.MemoryGauge} · 상대 턴 전환";
    }

    private void PlaceMemoryCoreIndicator(int memory, bool animate)
    {
        double width = MemoryCoreHost.ActualWidth;
        if (width <= 0)
            return;

        double cell = width / MemoryCoreCells;
        int index = 10 - memory;
        double targetX = (index + 0.5) * cell - MemoryCoreIndicatorSize / 2;

        if (!animate)
        {
            MemoryCoreShift.BeginAnimation(TranslateTransform.XProperty, null);
            MemoryCoreShift.X = targetX;
            return;
        }

        MemoryCoreShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void MemoryCoreHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        PlaceMemoryCoreIndicator(Math.Clamp(_gameState.MemoryGauge, -10, 10), animate: false);

    private void RenderTurnAndPhases()
    {
        TxtTurnBadge.Text = $"TURN {_gameState.CurrentTurn}";
        PhaseStepPanel.Children.Clear();

        var phases = new (Phase Phase, string Name)[]
        {
            (Phase.Active, "1. 액티브"),
            (Phase.Draw, "2. 드로우"),
            (Phase.Breeding, "3. 육성"),
            (Phase.Main, "4. 메인"),
            (Phase.TurnEnd, "5. 턴종료")
        };

        foreach (var p in phases)
        {
            bool active = _gameState.CurrentPhase == p.Phase;
            var badge = new Border
            {
                Background = active ? Theme.GetBrush("Neo.Brush.Button.Cyan") : Theme.GetBrush("Neo.Brush.Cyan.Tint"),
                BorderBrush = active ? Brushes.Transparent : Theme.GetBrush("Neo.Brush.Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Effect = active ? Theme.GetEffect("Neo.Glow.Cyan.Soft") : null,
                Child = new TextBlock
                {
                    Text = p.Name,
                    Foreground = active ? Theme.GetBrush("Neo.Brush.Text.OnNeon") : Theme.GetBrush("Neo.Brush.Text.Muted"),
                    FontWeight = active ? FontWeights.ExtraBold : FontWeights.Normal,
                    FontSize = 11
                }
            };
            PhaseStepPanel.Children.Add(badge);
        }

        BtnPassBreeding.IsEnabled = _gameState.CurrentPhase == Phase.Breeding && !_gameState.IsGameOver;
        BtnMulligan.IsEnabled = _gameState.CanMulligan && !_gameState.IsGameOver;
        BtnMulligan.Opacity = BtnMulligan.IsEnabled ? 1.0 : 0.45;

        // 훈련 단계 버튼 하이라이트
        BtnStage1.Background = BrushFromHex(_gameState.CurrentStage == PracticeStage.Stage1_Goldfish ? "#89B4FA" : "#313244");
        BtnStage1.Foreground = BrushFromHex(_gameState.CurrentStage == PracticeStage.Stage1_Goldfish ? "#11111B" : "#CDD6F4");
        BtnStage2.Background = BrushFromHex(_gameState.CurrentStage == PracticeStage.Stage2_BlockerDefense ? "#F9E2AF" : "#313244");
        BtnStage2.Foreground = BrushFromHex(_gameState.CurrentStage == PracticeStage.Stage2_BlockerDefense ? "#11111B" : "#CDD6F4");
        BtnStage3.Background = BrushFromHex(_gameState.CurrentStage == PracticeStage.Stage3_AiBattle ? "#F38BA8" : "#313244");
        BtnStage3.Foreground = BrushFromHex(_gameState.CurrentStage == PracticeStage.Stage3_AiBattle ? "#11111B" : "#CDD6F4");
    }

    private void BtnToggleSecurityPeek_Click(object sender, RoutedEventArgs e)
    {
        _isSecurityPeekEnabled = !_isSecurityPeekEnabled;
        RenderOpponentSecurity();
    }

    private void RenderOpponentSecurity()
    {
        // 시큐리티 앞면보기는 연습 모드 전용 — 실전 AI 대전에서는 공식 룰대로 상대 시큐리티를 체크 전까지 비공개로 둔다
        bool isAiBattle = _gameState.CurrentStage == PracticeStage.Stage3_AiBattle;
        bool peek = _isSecurityPeekEnabled && !isAiBattle;
        BtnToggleSecurityPeek.Visibility = isAiBattle ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleSecurityPeek.Content = peek ? "👁️ 앞면보기: ON" : "🙈 앞면보기: OFF";
        BtnToggleSecurityPeek.Background = BrushFromHex(peek ? "#A6E3A1" : "#45475A");
        BtnToggleSecurityPeek.Foreground = BrushFromHex(peek ? "#11111B" : "#F8FAFC");

        OpponentSecurityPanel.Children.Clear();

        var clearedHistory = _gameState.SecurityCheckHistory;
        var remainingCards = _gameState.DummyOpponent.SecurityStack;
        int clearedCount = clearedHistory.Count;
        int totalCount = clearedCount + remainingCards.Count;
        if (totalCount == 0) totalCount = 5;

        int pct = (int)Math.Round((double)clearedCount / totalCount * 100.0);
        TxtSecurityProgressLabel.Text = $"{clearedCount}/{totalCount} 돌파 ({pct}%)";
        BarSecurityProgress.Maximum = totalCount;
        BarSecurityProgress.Value = clearedCount;

        if (remainingCards.Count > 0)
        {
            var nextCard = remainingCards[0];
            // 앞면으로 놓인 시큐리티는 공개 정보 — 실전에서도 보여 준다
            bool nextFaceUp = GoldfishEngine.IsFaceUp(_gameState.DummyOpponent, nextCard);
            TxtNextSecurityTarget.Text = peek || nextFaceUp
                ? $"🎯 다음 타격 (#{clearedCount + 1}{(nextFaceUp ? " · 앞면" : "")}): [{nextCard.DisplayName}] {(nextCard.Type == CardType.Digimon ? $"DP {nextCard.DP:N0}" : KoreanLabels.Type(nextCard.Type))}"
                : $"🎯 다음 타격: 시큐리티 #{clearedCount + 1} (뒷면 비공개)";
            TxtNextSecurityTarget.Foreground = BrushFromHex("#89DCEB");
        }
        else
        {
            TxtNextSecurityTarget.Text = "🏆 상대 시큐리티 0장! 지금 어택하면 다이렉트 어택 승리!";
            TxtNextSecurityTarget.Foreground = BrushFromHex("#A6E3A1");
        }

        if (!string.IsNullOrWhiteSpace(_gameState.LastSecurityBattleResult))
        {
            TxtLastSecurityBattle.Text = $"⚔️ {_gameState.LastSecurityBattleResult}";
            TxtLastSecurityBattle.Foreground = BrushFromHex(_gameState.LastSecurityBattleWon ? "#A6E3A1" : "#F38BA8");
        }
        else
        {
            TxtLastSecurityBattle.Text = "내 디지몬을 드래그해 이곳에 놓으면 시큐리티를 공격합니다!";
            TxtLastSecurityBattle.Foreground = BrushFromHex("#A6ADC8");
        }

        // 1) 이미 오픈/소모된 시큐리티 슬롯 (#1 ~ #clearedCount)
        FrameworkElement? lastClearedSlot = null;
        for (int i = 0; i < clearedHistory.Count; i++)
        {
            var item = clearedHistory[i];
            int slotNum = i + 1;
            var widget = (FrameworkElement)CreateSecuritySlotWidget(item.SecurityCard, slotNum, isCleared: true, isBattleWon: item.IsBattleWon, isNextTarget: false, showFace: true);
            OpponentSecurityPanel.Children.Add(widget);
            lastClearedSlot = widget;
        }

        // 2) 남아 있는 시큐리티 슬롯 (#clearedCount+1 ~ #5)
        FrameworkElement? nextTargetSlot = null;
        for (int i = 0; i < remainingCards.Count; i++)
        {
            var card = remainingCards[i];
            int slotNum = clearedCount + i + 1;
            bool isNext = i == 0;
            var widget = (FrameworkElement)CreateSecuritySlotWidget(card, slotNum, isCleared: false, isBattleWon: true, isNextTarget: isNext,
                showFace: peek || GoldfishEngine.IsFaceUp(_gameState.DummyOpponent, card));
            OpponentSecurityPanel.Children.Add(widget);
            if (isNext)
                nextTargetSlot = widget;
        }

        // 시큐리티가 타격/파괴될 때마다 방금 오픈된 시큐리티 카드(좌측)와 다음 타격 대상 시큐리티(우측)가 한눈에 보이도록 자동 스크롤 이동!
        double targetOffset = clearedCount <= 1 ? 0 : (clearedCount - 1) * 78.0;
        OpponentSecurityScrollViewer.UpdateLayout();
        OpponentSecurityScrollViewer.ScrollToVerticalOffset(targetOffset);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (clearedCount == 0)
            {
                OpponentSecurityScrollViewer.ScrollToVerticalOffset(0);
            }
            else if (nextTargetSlot != null && lastClearedSlot != null)
            {
                OpponentSecurityScrollViewer.ScrollToVerticalOffset(targetOffset);
                nextTargetSlot.BringIntoView();
            }
            else if (lastClearedSlot != null)
            {
                OpponentSecurityScrollViewer.ScrollToBottom();
                lastClearedSlot.BringIntoView();
            }
        }));
    }

    private UIElement CreateSecuritySlotWidget(CardData card, int slotNumber, bool isCleared, bool isBattleWon, bool isNextTarget, bool showFace)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 좌측 미니 카드 이미지 또는 뒷면
        Border thumbBorder;
        if (showFace)
        {
            var bmp = GetCardBitmap(card);
            var thumbGrid = new Grid();
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Stretch = Stretch.UniformToFill, Opacity = isCleared ? 0.45 : 1.0 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                thumbGrid.Children.Add(img);
            }
            if (isCleared)
            {
                thumbGrid.Children.Add(new Border
                {
                    Background = BrushFromHex("#AA11111B"),
                    Child = new TextBlock
                    {
                        Text = isBattleWon ? "💥" : "💀",
                        FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            thumbBorder = new Border
            {
                Width = 44,
                Height = 60,
                CornerRadius = new CornerRadius(4),
                BorderBrush = GetCardColorBrush(card.Color),
                BorderThickness = new Thickness(1.5),
                ClipToBounds = true,
                Child = thumbGrid
            };
        }
        else
        {
            // 비공개 시큐리티 = 실제 TCG 카드 뒷면
            thumbBorder = new Border
            {
                Width = 44,
                Height = 60,
                CornerRadius = new CornerRadius(4),
                Background = Theme.GetBrush("Duel.Brush.CardBackArt"),
                BorderBrush = BrushFromHex(isNextTarget ? "#F9E2AF" : "#38BDF8"),
                BorderThickness = new Thickness(1.5)
            };
        }
        Grid.SetColumn(thumbBorder, 0);
        row.Children.Add(thumbBorder);

        // 우측 시큐리티 슬롯 상태, 이름, DP 표시
        var infoStack = new StackPanel
        {
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        string badgeText = isCleared
            ? card.Type == CardType.Tamer
                ? $"#{slotNumber} 🎖️ [시큐리티] 테이머 등장!"
                : card.Type == CardType.Option
                    ? $"#{slotNumber} ✨ [시큐리티] 옵션 발동!"
                    : (isBattleWon ? $"#{slotNumber} ✅ 배틀 승리 (격파)" : $"#{slotNumber} 💀 내 디지몬 소멸 (자폭)")
            : isNextTarget
                ? $"#{slotNumber} 🎯 다음 타격 대상"
                : $"#{slotNumber} 🛡️ 대기 시큐리티";

        string badgeColor = isCleared
            ? (isBattleWon ? "#A6E3A1" : "#F38BA8")
            : isNextTarget
                ? "#F9E2AF"
                : "#A6ADC8";

        infoStack.Children.Add(new TextBlock
        {
            Text = badgeText,
            Foreground = BrushFromHex(badgeColor),
            FontWeight = FontWeights.ExtraBold,
            FontSize = 11
        });

        if (showFace)
        {
            string titlePrefix = card.Type switch
            {
                CardType.Digimon => $"Lv.{card.Level} ",
                CardType.Tamer => "[테이머] ",
                CardType.Option => "[옵션] ",
                _ => ""
            };

            infoStack.Children.Add(new TextBlock
            {
                Text = $"{titlePrefix}{card.DisplayName}",
                Foreground = BrushFromHex(isCleared ? "#9399B2" : "#F8FAFC"),
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                TextDecorations = isCleared && card.Type == CardType.Digimon ? TextDecorations.Strikethrough : null,
                Margin = new Thickness(0, 1, 0, 1)
            });

            string bottomBadgeText = card.Type switch
            {
                CardType.Digimon => $"⚔️ DP {card.DP:N0}",
                CardType.Tamer => "🎖️ [시큐리티] 코스트 0 등장",
                CardType.Option => "✨ [시큐리티] 즉시 효과 발동",
                _ => $"DP {card.DP:N0}"
            };

            infoStack.Children.Add(new Border
            {
                Background = BrushFromHex(isCleared ? "#232438" : isNextTarget ? "#3B1D2E" : "#1E293B"),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = bottomBadgeText,
                    Foreground = BrushFromHex(isCleared ? "#6C7086" : card.Type == CardType.Digimon ? "#F38BA8" : "#F9E2AF"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 11
                }
            });
        }
        else
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = "비공개 시큐리티",
                Foreground = BrushFromHex("#9399B2"),
                FontWeight = FontWeights.Bold,
                FontSize = 12
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = "공격 시 오픈 & 효과 발동",
                Foreground = BrushFromHex("#6C7086"),
                FontSize = 10.5
            });
        }

        Grid.SetColumn(infoStack, 1);
        row.Children.Add(infoStack);

        var slotBorder = new Border
        {
            Width = 212,
            Height = 72,
            Background = BrushFromHex(isCleared ? "#11111B" : isNextTarget ? "#231E32" : "#141522"),
            BorderBrush = BrushFromHex(isCleared ? "#313244" : isNextTarget ? "#F9E2AF" : "#45475A"),
            BorderThickness = new Thickness(isNextTarget ? 2.5 : 1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 5, 7, 5),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = row
        };

        if (showFace)
        {
            slotBorder.MouseEnter += (_, _) => UpdateCardInspector(card);
        }

        return slotBorder;
    }

    private void RenderOpponentBattleArea()
    {
        OpponentBattleAreaBorder.Visibility = Visibility.Visible;
        OpponentBattleAreaPanel.Children.Clear();

        if (_pendingHandCard != null && !_pendingIsEvolution && _pendingTargetsOpponent)
        {
            TxtOpponentFieldHeader.Text = $"🎯 [{_pendingHandCard.DisplayName}] 발동 대기 중 👉 제거/무력화할 상대 디지몬을 클릭하세요!";
            TxtOpponentFieldHeader.Foreground = BrushFromHex("#A6E3A1");
        }
        else if (_pendingAttackerInstance != null)
        {
            TxtOpponentFieldHeader.Text = $"🎯 [{_pendingAttackerInstance.TopCard.DisplayName}(DP {_pendingAttackerInstance.GetCurrentDP()})] 공격 대기 중 👉 타격할 상대 레스트(REST) 디지몬을 클릭하세요!";
            TxtOpponentFieldHeader.Foreground = BrushFromHex("#A6E3A1");
        }
        else
        {
            TxtOpponentFieldHeader.Text = _gameState.CurrentStage switch
            {
                PracticeStage.Stage1_Goldfish => "🎯 상대 배틀 에어리어 [1인 솔리테어 타임어택 — 방해 디지몬 없음]",
                PracticeStage.Stage2_BlockerDefense => "🛡️ 상대 배틀 에어리어 [블로커 수비진 — 옵션 또는 고DP로 돌파!]",
                _ => $"👾 상대 AI 배틀 에어리어 [VS {_gameState.OpponentDeckName}] — 레스트 디지몬 직접 타격 가능!"
            };
            TxtOpponentFieldHeader.Foreground = BrushFromHex("#FAB387");
        }

        TxtOpponentZonesSummary.Text = _gameState.CurrentStage == PracticeStage.Stage3_AiBattle
            ? $"AI 필드: {_gameState.DummyOpponent.BattleArea.Count}체 | AI 테이머: {_gameState.DummyOpponent.Tamers.Count}장"
              + (_gameState.DummyOpponent.OptionsInPlay.Count > 0
                  ? $" | ⏳ AI 딜레이 옵션: {string.Join(", ", _gameState.DummyOpponent.OptionsInPlay.Select(p => p.Card.DisplayName))}"
                  : "")
              + (_gameState.DummyOpponent.UnderTamers.Count > 0 ? $" | 📥 AI 테이머 아래 {_gameState.DummyOpponent.UnderTamers.Count}장" : "")
              + $" | AI 손패: {_gameState.DummyOpponent.Hand.Count}장 | AI 덱: {_gameState.DummyOpponent.MainDeck.Count}장"
            : $"상대 필드: {_gameState.DummyOpponent.BattleArea.Count}체 | 상대 트래시: {_gameState.DummyOpponent.Trash.Count}장";

        if (_gameState.DummyOpponent.BattleArea.Count == 0)
        {
            string emptyMsg = _gameState.CurrentStage == PracticeStage.Stage1_Goldfish
                ? "🎯 [1인 솔리테어 훈련] 상대 필드가 비어 있습니다! 진화 체인을 완성해 상대 시큐리티 5장을 빠르게 돌파하세요!"
                : "✅ 상대 배틀 존이 비어 있습니다! 지금 바로 상대 시큐리티를 총공격하세요!";
            OpponentBattleAreaBorder.ToolTip = emptyMsg;
        }
        else
        {
            OpponentBattleAreaBorder.ToolTip = null;
        }

        var oppFieldItems = new List<FieldItem>();
        foreach (var opp in _gameState.DummyOpponent.BattleArea.ToList())
        {
            // 옵션의 대상 조건(예: 전기 충격 = 레스트 상태)에 맞는 디지몬만 강조 · 클릭 대상
            bool isTargetForOption = _pendingHandCard != null && !_pendingIsEvolution && _pendingTargetsOpponent
                                     && CardScripts.IsValidMainTarget(_pendingHandCard, opp);
            bool isTargetForAttack = _pendingAttackerInstance != null && opp.IsSuspended;
            bool isHighlighted = isTargetForOption || isTargetForAttack;

            // 존에 놓인 카드 1장 (위: 카드 / 아래: 상태·이름·DP·대상 버튼)
            var row = new Grid();
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var thumb = CreateCardVisualFrame(
                opp.TopCard,
                width: OpponentCardArtWidth,
                height: OpponentCardArtHeight,
                customDpOrBadge: $"{opp.GetCurrentDP()}",
                isRested: opp.IsSuspended);
            thumb.HorizontalAlignment = HorizontalAlignment.Center;
            if (opp.IsSuspended)
            {
                // 레스트 = 가로로 눕힌 수비 표시
                thumb.LayoutTransform = new RotateTransform(90);
            }
            Grid.SetRow(thumb, 0);
            row.Children.Add(thumb);

            var infoStack = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            string statusText = opp.HasBlocker && !opp.IsSuspended
                ? "🛡️ ≪블로커≫ 방어 대기!"
                : opp.IsSuspended
                    ? "↻ REST (직접 타격 가능!)"
                    : "⚡ ACTIVE";
            string statusColor = opp.HasBlocker && !opp.IsSuspended
                ? "#F38BA8"
                : opp.IsSuspended
                    ? "#FAB387"
                    : "#89DCEB";

            infoStack.Children.Add(new TextBlock
            {
                Text = statusText,
                Foreground = BrushFromHex(statusColor),
                FontWeight = FontWeights.ExtraBold,
                FontSize = 10
            });

            infoStack.Children.Add(new TextBlock
            {
                Text = $"Lv.{opp.TopCard.Level} {opp.TopCard.DisplayName}",
                Foreground = BrushFromHex("#F8FAFC"),
                FontWeight = FontWeights.Bold,
                FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = OpponentZoneSlotWidth - 14,
                Margin = new Thickness(0, 2, 0, 2)
            });

            infoStack.Children.Add(new TextBlock
            {
                Text = $"⚔️ DP {opp.GetCurrentDP():N0}",
                Foreground = BrushFromHex("#F9E2AF"),
                FontWeight = FontWeights.ExtraBold,
                FontSize = 11.5
            });

            if (opp.LinkedCards.Count > 0)
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"🔗 링크: {string.Join(", ", opp.LinkedCards.Select(c => c.DisplayName))}",
                    Foreground = BrushFromHex("#89DCEB"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = OpponentZoneSlotWidth - 14,
                    ToolTip = string.Join("\n\n", opp.LinkedCards.Select(c => $"[{c.CardId}] {c.DisplayName}\n{c.DisplayEffectText}"))
                });
            }

            if (isTargetForOption && _pendingHandCard != null)
            {
                var btnOptTarget = new Button
                {
                    Content = $"💥 [{_pendingHandCard.DisplayName}] 적중!",
                    Background = BrushFromHex("#A6E3A1"),
                    Foreground = BrushFromHex("#11111B"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 10.5,
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 4, 0, 0),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btnOptTarget.Click += (_, e) =>
                {
                    e.Handled = true;
                    ExecutePendingHandActionOnTarget(opp);
                };
                infoStack.Children.Add(btnOptTarget);
            }
            else if (_pendingAttackerInstance != null)
            {
                if (opp.IsSuspended)
                {
                    var btnHitTarget = new Button
                    {
                        Content = $"⚔️ [{_pendingAttackerInstance.TopCard.DisplayName}]으로 격파!",
                        Background = BrushFromHex("#A6E3A1"),
                        Foreground = BrushFromHex("#11111B"),
                        FontWeight = FontWeights.ExtraBold,
                        FontSize = 10.5,
                        Padding = new Thickness(6, 3, 6, 3),
                        Margin = new Thickness(0, 4, 0, 0),
                        BorderThickness = new Thickness(0),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    btnHitTarget.Click += (_, e) =>
                    {
                        e.Handled = true;
                        var attacker = _pendingAttackerInstance;
                        _pendingAttackerInstance = null;
                        ApplyActionResult(_engine.AttackOpponentDigimon(attacker, opp));
                    };
                    infoStack.Children.Add(btnHitTarget);
                }
                else
                {
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = "액티브 상태 (직접 공격 불가)",
                        Foreground = BrushFromHex("#6C7086"),
                        FontSize = 10,
                        Margin = new Thickness(0, 3, 0, 0)
                    });
                }
            }

            Grid.SetRow(infoStack, 1);
            row.Children.Add(infoStack);

            var oppCardBorder = new Border
            {
                Width = OpponentZoneSlotWidth,
                Tag = opp,
                Background = BrushFromHex(isHighlighted ? "#1B2E28" : "#141522"),
                BorderBrush = BrushFromHex(isHighlighted ? "#A6E3A1" : opp.HasBlocker && !opp.IsSuspended ? "#F38BA8" : "#45475A"),
                BorderThickness = new Thickness(isHighlighted ? 2.5 : 1.5),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 5, 7, 5),
                Cursor = isHighlighted ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Child = row
            };

            oppCardBorder.MouseEnter += (_, _) =>
            {
                UpdateCardInspector(opp.TopCard);
                if (_pinnedFieldInstance == null)
                {
                    UpdateFieldDigimonBreakdown(opp, isPinned: false, isOpponent: true);
                }
                if (_pendingAttackerInstance != null)
                {
                    var check = ActionValidator.ValidateAttack(_gameState, _pendingAttackerInstance, opp);
                    ShowToast(check.CanExecute, check.CanExecute ? check.GuideMessage : check.Reason);
                }
            };

            oppCardBorder.MouseLeftButtonUp += (_, _) =>
            {
                if (isTargetForOption)
                {
                    ExecutePendingHandActionOnTarget(opp);
                }
                else if (_pendingAttackerInstance != null)
                {
                    var attacker = _pendingAttackerInstance;
                    _pendingAttackerInstance = null;
                    ApplyActionResult(_engine.AttackOpponentDigimon(attacker, opp));
                }
                else
                {
                    _pinnedFieldInstance = ReferenceEquals(_pinnedFieldInstance, opp) ? null : opp;
                    UpdateFieldDigimonBreakdown(opp, isPinned: _pinnedFieldInstance != null, isOpponent: true);
                }
            };

            oppCardBorder.MouseEnter += (_, _) => Panel.SetZIndex(oppCardBorder, 10);
            oppCardBorder.MouseLeave += (_, _) => Panel.SetZIndex(oppCardBorder, 0);
            oppFieldItems.Add(new FieldItem(opp, oppCardBorder, IsTamer: false));
        }

        // 상대 테이머도 카드로 한 장씩 (레스트면 가로로 눕힘) — 상대 필드는 자동 배치만 (거울 배치: 디지몬은 오른쪽부터, 테이머는 왼쪽 끝부터)
        foreach (var key in TamerKeysOf(_gameState.DummyOpponent))
            oppFieldItems.Add(new FieldItem(key, CreateFieldTamerWidget(key, _gameState.DummyOpponent, isOpponent: true), IsTamer: true));

        LayoutFieldCanvas(OpponentBattleAreaPanel, OpponentBattleAreaScroll, oppFieldItems, _oppFieldPositions,
            mirrored: true, guideHeight: OpponentZoneSlotHeight, contentDrivenHeight: false, dropCenter: null);

        RenderOpponentBreedingArea();
    }

    // 상대 육성 에어리어 (AI가 부화시킨 디지타마 / 육성 중인 디지몬)
    private void RenderOpponentBreedingArea()
    {
        var breeding = _gameState.DummyOpponent.BreedingArea;
        if (breeding == null)
        {
            OppBreedingSlot.Content = new TextBlock
            {
                Text = _gameState.DummyOpponent.DigitamaDeck.Count > 0 ? $"비어 있음\n(디지타마 {_gameState.DummyOpponent.DigitamaDeck.Count}장)" : "비어 있음",
                Foreground = Theme.GetBrush("Neo.Brush.Text.Dim"),
                FontSize = 10,
                TextAlignment = TextAlignment.Center
            };
            return;
        }

        var card = breeding.TopCard;
        var frame = CreateCardVisualFrame(card, width: 88, height: 120,
            customDpOrBadge: card.Type == CardType.Digimon ? $"{breeding.GetCurrentDP()}" : $"Lv.{card.Level}");
        var holder = new StackPanel
        {
            Cursor = System.Windows.Input.Cursors.Hand,
            Children =
            {
                frame,
                new TextBlock
                {
                    Text = $"Lv.{card.Level} {card.DisplayName}",
                    Foreground = Theme.GetBrush("Neo.Brush.Text.Secondary"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 100,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
                }
            }
        };
        holder.MouseEnter += (_, _) =>
        {
            UpdateCardInspector(card);
            if (_pinnedFieldInstance == null)
                UpdateFieldDigimonBreakdown(breeding, isPinned: false, isOpponent: true);
        };
        OppBreedingSlot.Content = holder;
    }

    // ==================== 🎴 듀얼 필드 카드 크기 (배틀 에어리어는 고정 칸 없는 자유 배치 필드) ====================
    private const double PlayerZoneSlotWidth = 160;
    private const double PlayerZoneSlotHeight = 214;
    private const double FieldCardArtWidth = 104;
    private const double FieldCardArtHeight = 142;
    private const double OpponentZoneSlotWidth = 150;
    private const double OpponentZoneSlotHeight = 184;
    private const double OpponentCardArtWidth = 80;
    private const double OpponentCardArtHeight = 110;

    // ==================== 🪪 플레이어 플레이트 & 상대 손패 (유희왕 LP 표시 자리) ====================
    private void RenderDuelPlates()
    {
        var opp = _gameState.DummyOpponent;
        TxtOppPlateName.Text = _gameState.CurrentStage switch
        {
            PracticeStage.Stage1_Goldfish => "솔리테어 훈련 (방해 없음)",
            PracticeStage.Stage2_BlockerDefense => "블로커 수비진",
            _ => $"VS {_gameState.OpponentDeckName}"
        };
        TxtOppPlateSecurity.Text = opp.SecurityStack.Count.ToString();
        TxtOppPlateInfo.Text = $"HAND {opp.Hand.Count} · DECK {opp.MainDeck.Count} · TRASH {opp.Trash.Count}";
        TxtOppDeckCount.Text = opp.MainDeck.Count.ToString();
        TxtOppTrashCount.Text = opp.Trash.Count.ToString();

        OppHandBacksPanel.Children.Clear();
        int handCount = opp.Hand.Count;
        for (int i = 0; i < Math.Min(handCount, 10); i++)
        {
            double offset = i - (Math.Min(handCount, 10) - 1) / 2.0;
            OppHandBacksPanel.Children.Add(new Border
            {
                Width = 26,
                Height = 36,
                CornerRadius = new CornerRadius(3),
                Background = Theme.GetBrush("Duel.Brush.CardBackArt"),
                BorderBrush = Theme.GetBrush("Neo.Brush.Rose.Soft"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(i == 0 ? 0 : -8, 0, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0),
                // 상대 손패는 위쪽을 축으로 뒤집힌 부채꼴
                RenderTransform = new TransformGroup
                {
                    Children =
                    {
                        new RotateTransform(-offset * 4),
                        new TranslateTransform(0, -Math.Abs(offset) * Math.Abs(offset) * 0.8)
                    }
                }
            });
        }

        var me = _gameState.Player;
        TxtPlayerPlateSecurity.Text = me.SecurityStack.Count.ToString();
        TxtPlayerPlateInfo.Text = $"DECK {me.MainDeck.Count} · TRASH {me.Trash.Count} · EGG {me.DigitamaDeck.Count}";
    }

    private void RenderBreedingArea()
    {
        TxtDigitamaCount.Text = $"디지타마 덱: {_gameState.Player.DigitamaDeck.Count}장";
        if (_gameState.Player.BreedingArea == null)
        {
            BreedingSlotControl.Content = new Border
            {
                Width = 175,
                Height = 150,
                Background = BrushFromHex("#11111B"),
                BorderBrush = BrushFromHex("#313244"),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 8, 0, 0),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "🥚 DIGITAMA ZONE",
                            Foreground = BrushFromHex("#A6E3A1"),
                            FontWeight = FontWeights.ExtraBold,
                            FontSize = 13,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 8)
                        },
                        new TextBlock
                        {
                            Text = "육성 에어리어 비어 있음\n\n상단 [🥚 부화 (Lv.2)] 버튼을 눌러 디지타마 카드를 부화시키세요!",
                            Foreground = BrushFromHex("#A6ADC8"),
                            FontSize = 11.5,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
        }
        else
        {
            BreedingSlotControl.Content = CreateFieldDigimonWidget(_gameState.Player.BreedingArea, isBreedingArea: true);
        }
    }

    private void RenderBattleArea()
    {
        // 자유 배치 필드: 디지몬 · 테이머를 한 장씩 캔버스에 놓는다 (끌어서 옮긴 자리 유지, 새 카드는 놓은 자리 · 빈 격자 자리)
        var myFieldItems = new List<FieldItem>();
        foreach (var instance in _gameState.Player.BattleArea)
            myFieldItems.Add(new FieldItem(instance, CreateFieldDigimonWidget(instance, isBreedingArea: false), IsTamer: false));
        foreach (var key in TamerKeysOf(_gameState.Player))
            myFieldItems.Add(new FieldItem(key, CreateFieldTamerWidget(key, _gameState.Player, isOpponent: false), IsTamer: true));
        if (LayoutFieldCanvas(BattleAreaPanel, BattleAreaScroll, myFieldItems, _myFieldPositions,
                mirrored: false, guideHeight: PlayerZoneSlotHeight, contentDrivenHeight: false, dropCenter: _pendingFieldDropPoint))
            _pendingFieldDropPoint = null;

        if (_pendingHandCard != null)
        {
            TxtSelectedTarget.Text = _pendingIsEvolution
                ? $"🎯 [{_pendingHandCard.DisplayName}(Lv.{_pendingHandCard.Level})] 진화 대기 중 👉 내 필드에서 Lv.{_pendingHandCard.EvoRequirementLevel} 디지몬을 클릭하세요!"
                : _pendingTargetsOpponent
                    ? $"🎯 [{_pendingHandCard.DisplayName}] 사용 대기 중 👉 상단 상대 배틀 에어리어에서 상대 디지몬을 클릭하세요!"
                    : $"🎯 [{_pendingHandCard.DisplayName}] 사용 대기 중 👉 내 배틀 에어리어에서 아군 디지몬을 클릭하세요!";
            TxtSelectedTarget.Foreground = BrushFromHex("#A6E3A1");
            BtnCancelPendingAction.Visibility = Visibility.Visible;
        }
        else if (_pendingAttackerInstance != null)
        {
            TxtSelectedTarget.Text = $"🎯 [{_pendingAttackerInstance.TopCard.DisplayName}] 공격 대기 중 👉 상단 상대 배틀 에어리어에서 레스트(REST) 디지몬을 클릭하세요!";
            TxtSelectedTarget.Foreground = BrushFromHex("#A6E3A1");
            BtnCancelPendingAction.Visibility = Visibility.Visible;
        }
        else
        {
            TxtSelectedTarget.Text = "💡 카드를 끌어 내 배틀 에어리어 안에 놓으면 자리 이동 · 상대 시큐리티/레스트 디지몬에 놓으면 공격 · 클릭하면 DP·효과 확인";
            TxtSelectedTarget.Foreground = BrushFromHex("#F9E2AF");
            BtnCancelPendingAction.Visibility = Visibility.Collapsed;
        }

        // 테이머는 배틀 에어리어에 카드로 놓인다 (위 자유 배치 필드). 이 줄에는 놓인 ≪딜레이≫ 옵션 · 테이머 아래 카드만 보여 준다
        TamerZonePanel.Children.Clear();

        // 배틀 에어리어에 놓인 내 옵션 (≪딜레이≫ — 놓은 다음 턴부터 클릭해서 파기하고 효과를 발휘)
        var readyOptions = _engine.GetMainAbilities(_gameState.Player).Where(a => a.Digimon == null && a.Card.Type == CardType.Option).Select(a => a.Card).ToList();
        foreach (var placed in _gameState.Player.OptionsInPlay)
        {
            var option = placed.Card;
            bool ready = readyOptions.Any(o => ReferenceEquals(o, option));
            var optionChip = new Border
            {
                Background = BrushFromHex("#33F9E2AF"),
                BorderBrush = BrushFromHex(ready ? "#F9E2AF" : "#6C7086"),
                BorderThickness = new Thickness(ready ? 1.5 : 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = ready ? "클릭하면 이 옵션을 파기하고 ≪딜레이≫ 효과를 발휘합니다" : "≪딜레이≫ 옵션은 놓은 다음 턴부터 쓸 수 있습니다",
                Child = new TextBlock
                {
                    Text = $"⏳ {option.DisplayName} [{(ready ? "딜레이 · 사용 가능" : "딜레이 · 다음 턴부터")}]",
                    Foreground = BrushFromHex(ready ? "#F9E2AF" : "#A6ADC8"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                }
            };
            optionChip.MouseEnter += (_, _) => UpdateCardInspector(option);
            optionChip.MouseLeftButtonUp += (_, _) =>
            {
                UpdateCardInspector(option);
                if (ready)
                    TryActivateMainAbility(null, option);
                else
                    ShowToast(false, $"⏳ [{option.DisplayName}]은(는) 놓은 턴에는 쓸 수 없습니다. 다음 내 턴부터 클릭해서 쓰세요.");
            };
            TamerZonePanel.Children.Add(optionChip);
        }

        // 테이머 아래에 놓인 카드 (≪세이브≫ · ≪머티리얼 세이브≫ 등 — 효과는 엔진이 처리, 여기서는 장수와 카드만 보여 준다)
        var underTamers = _gameState.Player.UnderTamers;
        if (underTamers.Count > 0)
        {
            string names = string.Join(", ", underTamers.Select(c => c.DisplayName));
            var underChip = new Border
            {
                Background = BrushFromHex("#33CBA6F7"),
                BorderBrush = BrushFromHex("#CBA6F7"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"테이머 아래에 놓인 카드 {underTamers.Count}장: {names}",
                Child = new TextBlock
                {
                    Text = $"📥 테이머 아래 {underTamers.Count}장",
                    Foreground = BrushFromHex("#CBA6F7"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                }
            };
            underChip.MouseEnter += (_, _) => UpdateCardInspector(underTamers[^1]);
            underChip.MouseLeftButtonUp += (_, _) => ShowToast(true, $"📥 테이머 아래에 놓인 카드 {underTamers.Count}장: {names}");
            TamerZonePanel.Children.Add(underChip);
        }
        // 놓인 옵션 · 테이머 아래 카드가 없으면 줄을 숨겨 필드를 넓게 쓴다
        TamerStripBorder.Visibility = TamerZonePanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        string myLastSecInfo = _gameState.PlayerSecurityCheckHistory.Count > 0
            ? $"최근 오픈: [{_gameState.PlayerSecurityCheckHistory[^1].SecurityCard.DisplayName}] → {_gameState.PlayerSecurityCheckHistory[^1].OutcomeSummary}"
            : "50장 메인 덱 맨 위 5장 무작위 배치됨";

        TxtPlayerZonesSummary.Text =
            $"메인 덱: {_gameState.Player.MainDeck.Count}장 | 🛡️ 내 시큐리티: {_gameState.Player.SecurityStack.Count}장 | 트래시: {_gameState.Player.Trash.Count}장";

        // 1) 플레이매트 좌측 상단: 내 시큐리티 5장 미니 카드 스택 렌더링
        int myRemainingCount = _gameState.Player.SecurityStack.Count;
        TxtMySecurityCountBadge.Text = myRemainingCount > 5 ? $"{myRemainingCount}장" : $"{myRemainingCount} / 5장";
        TxtMySecurityLastEvent.Text = myLastSecInfo;
        TxtMySecurityLastEvent.Foreground = BrushFromHex(_gameState.PlayerSecurityCheckHistory.Count > 0 ? "#F9E2AF" : "#A6ADC8");

        MySecurityVisualStack.Children.Clear();
        // ≪회복≫ · "시큐리티 맨 아래에 앞면으로" 등으로 5장을 넘으면 칸을 늘린다 (맨 아래에 놓인 앞면 카드도 보이도록)
        int securitySlots = Math.Max(5, myRemainingCount);
        MySecurityVisualStack.Columns = securitySlots;

        for (int i = 0; i < securitySlots; i++)
        {
            if (i < myRemainingCount && _gameState.Player.SecurityStack[i] is var mySec && GoldfishEngine.IsFaceUp(_gameState.Player, mySec))
            {
                // 앞면으로 놓인 시큐리티 (공개 정보) — 카드 그림으로 보여 준다
                var faceUpGrid = new Grid();
                if (GetCardBitmap(mySec) is { } secBmp)
                {
                    var secImg = new Image { Source = secBmp, Stretch = Stretch.UniformToFill };
                    RenderOptions.SetBitmapScalingMode(secImg, BitmapScalingMode.HighQuality);
                    faceUpGrid.Children.Add(secImg);
                }
                faceUpGrid.Children.Add(new Border
                {
                    Background = BrushFromHex("#CC050A18"),
                    CornerRadius = new CornerRadius(0, 0, 3, 0),
                    Padding = new Thickness(3, 0, 3, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = $"{i + 1} 앞면", Foreground = BrushFromHex("#F9E2AF"), FontWeight = FontWeights.ExtraBold, FontSize = 9 }
                });
                var faceUpBox = new Border
                {
                    BorderBrush = BrushFromHex("#F9E2AF"),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1, 0, 1, 0),
                    ClipToBounds = true,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = $"내 시큐리티 #{i + 1} (앞면 공개): {mySec.DisplayName}",
                    Child = faceUpGrid
                };
                faceUpBox.MouseEnter += (_, _) => UpdateCardInspector(mySec);
                MySecurityVisualStack.Children.Add(faceUpBox);
            }
            else if (i < myRemainingCount)
            {
                // 실제 플레이매트처럼 시큐리티는 가로로 눕힌 카드 뒷면
                var activeSecBox = new Border
                {
                    Background = Theme.GetBrush("Duel.Brush.CardBackArt.Sideways"),
                    BorderBrush = BrushFromHex("#38BDF8"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1, 0, 1, 0),
                    ToolTip = $"내 시큐리티 #{i + 1} (비공개)",
                    Child = new Border
                    {
                        Background = BrushFromHex("#CC050A18"),
                        CornerRadius = new CornerRadius(0, 0, 3, 0),
                        Padding = new Thickness(3, 0, 3, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = new TextBlock
                        {
                            Text = $"{i + 1}",
                            Foreground = BrushFromHex("#7DE3FF"),
                            FontWeight = FontWeights.ExtraBold,
                            FontSize = 9
                        }
                    }
                };
                MySecurityVisualStack.Children.Add(activeSecBox);
            }
            else
            {
                int histIdx = i - myRemainingCount;
                var histItem = histIdx >= 0 && histIdx < _gameState.PlayerSecurityCheckHistory.Count
                    ? _gameState.PlayerSecurityCheckHistory[histIdx]
                    : null;

                var clearedSecBox = new Border
                {
                    Background = BrushFromHex("#181825"),
                    BorderBrush = BrushFromHex(histItem != null && histItem.IsBattleWon ? "#A6E3A1" : "#45475A"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(1, 0, 1, 0),
                    Cursor = histItem != null ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                    Child = new TextBlock
                    {
                        Text = histItem != null ? (histItem.IsBattleWon ? "💥승" : "💔파") : "빈칸",
                        Foreground = BrushFromHex(histItem != null && histItem.IsBattleWon ? "#A6E3A1" : "#6C7086"),
                        FontWeight = FontWeights.Bold,
                        FontSize = 9.5,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                if (histItem != null)
                {
                    var capturedCard = histItem.SecurityCard;
                    clearedSecBox.MouseEnter += (_, _) => UpdateCardInspector(capturedCard);
                }
                MySecurityVisualStack.Children.Add(clearedSecBox);
            }
        }

        // 2) 플레이매트 우측: 메인 덱 장수 및 트래시(묘지) 맨 위 카드 렌더링
        TxtMainDeckVisualCount.Text = $"{_gameState.Player.MainDeck.Count}장";
        // 트래시의 카드로 쓸 수 있는 [Trash] [Main] 효과가 있으면 표시 (트래시 칸 우클릭으로 고른다)
        int trashAbilities = _gameState.CurrentPhase == Phase.Main ? _engine.GetTrashMainAbilities(_gameState.Player).Count : 0;
        TxtTrashVisualCount.Text = trashAbilities > 0 ? $"{_gameState.Player.Trash.Count}장 · ⚡{trashAbilities}" : $"{_gameState.Player.Trash.Count}장";
        PlayerTrashZoneBorder.ToolTip = trashAbilities > 0 ? $"우클릭: 트래시의 카드로 쓰는 [메인] 효과 ({trashAbilities}개)" : null;

        if (_gameState.Player.Trash.Count > 0)
        {
            var topTrash = _gameState.Player.Trash[^1];
            var trashThumb = CreateCardVisualFrame(topTrash, width: 72, height: 96, customDpOrBadge: "TRASH");
            TrashTopCardControl.Content = trashThumb;
        }
        else
        {
            TrashTopCardControl.Content = new Border
            {
                Width = 72,
                Height = 96,
                CornerRadius = new CornerRadius(6),
                Background = BrushFromHex("#0F101A"),
                BorderBrush = BrushFromHex("#313244"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "비어 있음",
                    Foreground = BrushFromHex("#6C7086"),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
    }

    // ==================== 2-1. 실제 플레이매트 감각의 슬림 & 입체형 필드 디지몬 카드 위젯 ====================
    private UIElement CreateFieldDigimonWidget(CardInstance instance, bool isBreedingArea)
    {
        bool isSummoningSick = !isBreedingArea && instance.TurnPlayed == _gameState.CurrentTurn;
        bool isPinned = ReferenceEquals(_pinnedFieldInstance, instance);

        // 현재 패에서 선택된 카드(_pendingHandCard)의 유효한 아군 필드 대상인지 판별
        bool isPendingAllyTargetMode = _pendingHandCard != null && !_pendingTargetsOpponent;
        bool isValidPendingTarget = false;
        string invalidTargetReason = string.Empty;

        if (isPendingAllyTargetMode && _pendingHandCard != null)
        {
            if (_pendingIsEvolution)
            {
                var check = ActionValidator.ValidateEvolution(_gameState, instance, _pendingHandCard);
                isValidPendingTarget = check.CanExecute;
                invalidTargetReason = check.Reason;
            }
            else
            {
                if (isBreedingArea)
                {
                    isValidPendingTarget = false;
                    invalidTargetReason = "육성 에어리어의 디지몬은 옵션 효과의 대상으로 지정할 수 없습니다.";
                }
                else if (CardScripts.MainTargetOf(_pendingHandCard) == TargetKind.OwnDigimonWithDigimonSource && !instance.EvolutionSources.Any(s => s.Type == CardType.Digimon))
                {
                    isValidPendingTarget = false;
                    invalidTargetReason = $"{_pendingHandCard.DisplayName}은(는) 진화원에 디지몬 카드가 있는 디지몬에게만 사용할 수 있습니다.";
                }
                else if (!CardScripts.IsValidMainTarget(_pendingHandCard, instance))
                {
                    isValidPendingTarget = false;
                    invalidTargetReason = $"{_pendingHandCard.DisplayName}의 대상 조건에 맞지 않는 디지몬입니다.";
                }
                else
                {
                    isValidPendingTarget = true;
                }
            }
        }

        var stack = new StackPanel();

        // 1) 상단 상태 표시줄 (액티브/레스트/소환멀미/블로커 + 고정 핀 표시)
        var statusDock = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        string stateText = isBreedingArea
            ? "🛡️ 육성존"
            : instance.IsSuspended ? "↻ REST" : isSummoningSick ? "⏳ 등장대기" : "⚡ ACTIVE";
        if (!isBreedingArea && instance.HasBlocker)
        {
            stateText += " 🛡️블로커";
        }
        if (isPinned)
        {
            stateText += " 📌";
        }

        string stateColor = isBreedingArea
            ? "#A6E3A1"
            : instance.IsSuspended ? "#FAB387" : isSummoningSick ? "#F9E2AF" : "#A6E3A1";

        statusDock.Children.Add(new TextBlock
        {
            Text = stateText,
            Foreground = BrushFromHex(stateColor),
            FontWeight = FontWeights.ExtraBold,
            FontSize = 10.5
        });

        if (instance.TopCard.Type == CardType.Digimon)
        {
            int secChecks = instance.GetSecurityAttackCount();
            statusDock.Children.Add(new Border
            {
                Background = BrushFromHex(secChecks >= 2 ? "#F38BA8" : "#313244"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = secChecks >= 2 ? $"💥 시큐 {secChecks}장" : "시큐 1장",
                    Foreground = BrushFromHex(secChecks >= 2 ? "#11111B" : "#CDD6F4"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 10
                }
            });
        }
        stack.Children.Add(statusDock);

        // 2) 실제 TCG 플레이매트처럼 메인 카드 위쪽으로 살짝 겹쳐 보이는 [진화원 피킹 탭 (Peeking Evolution Sources)]
        var evoSources = instance.GetEvolutionStackBottomUp();
        if (evoSources.Count > 0)
        {
            var evoPeekStack = new StackPanel { Margin = new Thickness(2, 0, 2, -2) };
            foreach (var src in evoSources.TakeLast(4))
            {
                string srcBonus = src.InheritedSecurityAttackBonus > 0
                    ? $"💥시큐+{src.InheritedSecurityAttackBonus}"
                    : src.InheritedDpBonus > 0
                        ? $"▲+{src.InheritedDpBonus:N0}"
                        : $"Lv.{src.Level}";

                var peekStrip = new Border
                {
                    Height = 18,
                    Background = BrushFromHex("#1D2033"),
                    BorderBrush = GetCardColorBrush(src.Color),
                    BorderThickness = new Thickness(3, 1, 1, 0),
                    CornerRadius = new CornerRadius(4, 4, 0, 0),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 0, 1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = $"진화원: [{src.CardId}] Lv.{src.Level} {src.DisplayName} — {(src.IsDual ? "(듀얼 카드: 옵션 쪽은 진화원 효과가 아님)" : src.DisplayInheritedEffectText)}",
                    Child = new DockPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"🧬 Lv.{src.Level} {src.DisplayName}",
                                Foreground = BrushFromHex("#CDD6F4"),
                                FontWeight = FontWeights.Bold,
                                FontSize = 9.5,
                                VerticalAlignment = VerticalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = srcBonus,
                                Foreground = BrushFromHex(src.InheritedSecurityAttackBonus > 0 ? "#F9E2AF" : src.InheritedDpBonus > 0 ? "#A6E3A1" : "#89DCEB"),
                                FontWeight = FontWeights.ExtraBold,
                                FontSize = 9.5,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    }
                };
                peekStrip.MouseEnter += (_, _) =>
                {
                    UpdateCardInspector(src);
                    UpdateFieldDigimonBreakdown(instance, isPinned: isPinned, isOpponent: false);
                };
                evoPeekStack.Children.Add(peekStrip);
            }
            stack.Children.Add(evoPeekStack);
        }

        // 3) 실물 최상단 카드 아트워크 프레임 (152 x 204)
        int totalBonusDp = instance.GetTotalBonusDP();
        string dpBadge = instance.TopCard.Type == CardType.Digimon
            ? totalBonusDp > 0
                ? $"{instance.GetCurrentDP():N0} DP (▲+{totalBonusDp:N0})"
                : $"{instance.GetCurrentDP():N0} DP"
            : $"Lv.{instance.TopCard.Level}";

        var cardArt = CreateCardVisualFrame(
            instance.TopCard,
            width: FieldCardArtWidth,
            height: FieldCardArtHeight,
            customDpOrBadge: dpBadge,
            isRested: instance.IsSuspended);
        cardArt.HorizontalAlignment = HorizontalAlignment.Center;
        // 유희왕 수비 표시처럼 레스트(REST) 디지몬은 카드를 가로로 눕힌다
        if (instance.IsSuspended && !isBreedingArea)
        {
            cardArt.LayoutTransform = new RotateTransform(90);
            cardArt.Margin = new Thickness(0, 4, 0, 4);
        }
        stack.Children.Add(cardArt);

        // 3-1) 링크 카드: 디지몬 옆에 가로로 끼운 카드 (≪링크 +1≫이면 2장까지) — 링크 DP는 엔진이 이미 디지몬 DP에 더해 둔다
        foreach (var link in instance.LinkedCards)
        {
            var linkStrip = new Border
            {
                Height = 18,
                Background = BrushFromHex("#14303A"),
                BorderBrush = BrushFromHex("#89DCEB"),
                BorderThickness = new Thickness(1, 1, 3, 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 2, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"링크 카드: [{link.CardId}] {link.DisplayName}\n{link.DisplayEffectText}",
                Child = new TextBlock
                {
                    Text = $"🔗 링크 · {link.DisplayName}",
                    Foreground = BrushFromHex("#89DCEB"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 9.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            linkStrip.MouseEnter += (_, _) =>
            {
                UpdateCardInspector(link);
                UpdateFieldDigimonBreakdown(instance, isPinned: isPinned, isOpponent: false);
            };
            stack.Children.Add(linkStrip);
        }

        // 4) 카드 하단 1줄 컴팩트 전투력 배지 (상세 내역은 호버/클릭 시 우측 사이드바에서 확인!)
        if (instance.TopCard.Type == CardType.Digimon)
        {
            int currentDp = instance.GetCurrentDP();
            string buffSummary = totalBonusDp > 0
                ? $"⚔️ DP {currentDp:N0} (▲+{totalBonusDp:N0}) | 🧬진화원 {evoSources.Count}장"
                : $"⚔️ DP {currentDp:N0} (기본) | 🧬진화원 {evoSources.Count}장";

            stack.Children.Add(new Border
            {
                Background = BrushFromHex(totalBonusDp > 0 ? "#162C26" : "#181926"),
                BorderBrush = BrushFromHex(totalBonusDp > 0 ? "#A6E3A1" : "#313244"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 3, 5, 3),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = buffSummary,
                    Foreground = BrushFromHex(totalBonusDp > 0 ? "#A6E3A1" : "#CDD6F4"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            });
        }

        // 5) 패에서 카드를 먼저 선택한 상태(_pendingHandCard != null)일 때 필드 대상 선택 버튼 표시
        if (isPendingAllyTargetMode && _pendingHandCard != null)
        {
            if (isValidPendingTarget)
            {
                var btnConfirmTarget = new Button
                {
                    Content = _pendingIsEvolution
                        ? $"🧬 여기에 [{_pendingHandCard.DisplayName}] 진화!"
                        : $"✨ 여기에 [{_pendingHandCard.DisplayName}] 적용!",
                    Background = BrushFromHex("#A6E3A1"),
                    Foreground = BrushFromHex("#11111B"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 11,
                    Padding = new Thickness(5, 5, 5, 5),
                    Margin = new Thickness(0, 4, 0, 2),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btnConfirmTarget.Click += (_, e) =>
                {
                    e.Handled = true;
                    ExecutePendingHandActionOnTarget(instance);
                };
                stack.Children.Add(btnConfirmTarget);
            }
            else
            {
                stack.Children.Add(new Border
                {
                    Background = BrushFromHex("#232438"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(0, 4, 0, 2),
                    Child = new TextBlock
                    {
                        Text = _pendingIsEvolution
                            ? $"대상 불가 (Lv.{_pendingHandCard.EvoRequirementLevel} 필요)"
                            : "대상 불가",
                        Foreground = BrushFromHex("#6C7086"),
                        FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                });
            }
        }

        // 6) 일반 상태에서 배틀 에어리어 디지몬의 공격 버튼 (시큐리티 어택 + 상대 레스트 디지몬 타격)
        if (!isBreedingArea && _pendingHandCard == null)
        {
            int checks = instance.GetSecurityAttackCount();
            var btnAttack = new Button
            {
                Content = _gameState.DummyOpponent.SecurityStack.Count > 0
                    ? $"⚔️ 시큐리티 어택 ({checks}장 타격!)"
                    : "🏆 다이렉트 어택 (게임 승리!)",
                Background = BrushFromHex("#F38BA8"),
                Foreground = BrushFromHex("#11111B"),
                FontWeight = FontWeights.ExtraBold,
                FontSize = 10.5,
                Padding = new Thickness(4, 4, 4, 4),
                Margin = new Thickness(0, 4, 0, 0),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnAttack.MouseEnter += (_, _) =>
            {
                UpdateCardInspector(instance.TopCard);
                UpdateFieldDigimonBreakdown(instance, isPinned: isPinned, isOpponent: false);
                var check = ActionValidator.ValidateAttack(_gameState, instance, null);
                ShowToast(check.CanExecute, check.CanExecute ? check.GuideMessage : check.Reason);
            };
            btnAttack.Click += (_, e) =>
            {
                e.Handled = true;
                AutoPassBreedingIfNeeded();
                _pendingAttackerInstance = null;
                var res = _engine.AttackSecurityOrDirect(instance);
                if (!res.CanExecute)
                {
                    ApplyActionResult(res);
                }
                else
                {
                    if (_gameState.SecurityCheckHistory.Count > 0)
                    {
                        UpdateCardInspector(_gameState.SecurityCheckHistory[^1].SecurityCard);
                    }
                    ShowToast(_gameState.LastSecurityBattleWon || _gameState.IsVictory, res.GuideMessage);
                    if (!_gameState.LastSecurityBattleWon)
                    {
                        ToastBadge.Text = "💀 배틀 패배 (소멸)";
                    }
                    RefreshAllSimulatorUi();
                }
            };
            stack.Children.Add(btnAttack);

            if (_gameState.DummyOpponent.BattleArea.Any(d => d.IsSuspended))
            {
                bool isThisAttackerSelected = ReferenceEquals(_pendingAttackerInstance, instance);
                var btnAttackDigimon = new Button
                {
                    Content = isThisAttackerSelected ? "❌ 타격 선택 취소" : "🎯 상대 디지몬 타격",
                    Background = BrushFromHex(isThisAttackerSelected ? "#FAB387" : "#89DCEB"),
                    Foreground = BrushFromHex("#11111B"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 10,
                    Padding = new Thickness(4, 3, 4, 3),
                    Margin = new Thickness(0, 3, 0, 0),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btnAttackDigimon.Click += (_, e) =>
                {
                    e.Handled = true;
                    if (isThisAttackerSelected)
                    {
                        _pendingAttackerInstance = null;
                        ShowToast(true, "상대 디지몬 타격 대상 선택을 취소했습니다.");
                        RefreshAllSimulatorUi();
                        return;
                    }

                    AutoPassBreedingIfNeeded();
                    var firstRested = _gameState.DummyOpponent.BattleArea.FirstOrDefault(d => d.IsSuspended);
                    var check = ActionValidator.ValidateAttack(_gameState, instance, firstRested);
                    if (!check.CanExecute)
                    {
                        ShowToast(false, check.Reason);
                        return;
                    }

                    _pendingAttackerInstance = instance;
                    ShowToast(true, $"🎯 [{instance.TopCard.DisplayName}(DP {instance.GetCurrentDP()})] 공격 준비 완료! 상단 상대 배틀 에어리어에서 타격할 레스트 디지몬을 클릭하세요.");
                    RefreshAllSimulatorUi();
                };
                stack.Children.Add(btnAttackDigimon);
            }
        }

        var scaleTrans = new ScaleTransform(1.0, 1.0);
        var fieldCardBorder = new Border
        {
            Width = PlayerZoneSlotWidth,
            Tag = instance,
            Background = BrushFromHex(isValidPendingTarget ? "#1B2E28" : isPinned ? "#1E2238" : "#C00B1020"),
            BorderBrush = BrushFromHex(isValidPendingTarget ? "#A6E3A1" : isPinned ? "#F9E2AF" : GetCardColorBrush(instance.TopCard.Color).Color.ToString()),
            BorderThickness = new Thickness(isValidPendingTarget ? 3 : isPinned ? 2.5 : 1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 6, 7, 6),
            Margin = isBreedingArea ? new Thickness(0, 0, 10, 6) : new Thickness(0),
            Opacity = isPendingAllyTargetMode && !isValidPendingTarget ? 0.55 : 1.0,
            Cursor = System.Windows.Input.Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scaleTrans,
            Child = stack
        };

        // 2-4. 필드 카드에 마우스를 대거나 클릭하면 우측 사이드바에서 DP 증가치 및 테이머/진화원 효과를 즉시 확인!
        fieldCardBorder.MouseEnter += (_, _) =>
        {
            UpdateCardInspector(instance.TopCard);
            if (_pinnedFieldInstance == null || ReferenceEquals(_pinnedFieldInstance, instance))
            {
                UpdateFieldDigimonBreakdown(instance, isPinned: isPinned, isOpponent: false);
            }
            var anim = new DoubleAnimation(1.03, TimeSpan.FromMilliseconds(120));
            scaleTrans.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scaleTrans.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            Panel.SetZIndex(fieldCardBorder, 10);
        };

        fieldCardBorder.MouseLeave += (_, _) =>
        {
            Panel.SetZIndex(fieldCardBorder, 0);
            var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
            scaleTrans.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scaleTrans.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            if (_pinnedFieldInstance != null)
            {
                UpdateFieldDigimonBreakdown(_pinnedFieldInstance, isPinned: true);
            }
        };

        // 배틀 에어리어 디지몬 드래그 공격 & 육성 에어리어 디지몬 드래그 이동
        fieldCardBorder.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_gameState.IsGameOver && !isPendingAllyTargetMode)
            {
                _dragStartPoint = e.GetPosition(this);
                _potentialDragHandCard = null;
                if (isBreedingArea)
                {
                    _potentialDragBreedingDigimon = instance;
                    _potentialDragFieldAttacker = null;
                }
                else
                {
                    // 배틀 에어리어 디지몬: 상대 쪽에 놓으면 공격, 내 배틀 에어리어 안에 놓으면 자리 이동
                    _potentialDragFieldAttacker = instance;
                    _potentialDragBreedingDigimon = null;
                    BeginPotentialFieldMove(fieldCardBorder, e);
                }
                _potentialDragFieldTamer = null;
            }
        };

        // 진화원의 [메인] 효과를 지금 쓸 수 있으면 안내 (v0.29.0 — 우클릭으로 쓴다)
        if (!isBreedingArea && _gameState.CurrentPhase == Phase.Main
            && _engine.GetMainAbilities(_gameState.Player).Where(a => ReferenceEquals(a.Digimon, instance)).ToList() is { Count: > 0 } usable
            && usable.Any(a => a.Inherited))
            fieldCardBorder.ToolTip = "우클릭: [메인] 효과 쓰기 — " + string.Join(", ", usable.Select(a => a.Inherited ? $"진화원 [{a.Card.DisplayName}]" : $"[{a.Card.DisplayName}]"));

        // 우클릭: 이 디지몬의 [메인] 기동 효과 사용 (≪디지버스트≫ 등, 육성 에어리어에서는 ≪트레이닝≫ — 쓸 수 있는지는 엔진이 확인)
        fieldCardBorder.MouseRightButtonUp += (_, e) =>
        {
            if (isPendingAllyTargetMode)
                return;
            e.Handled = true;
            TryActivateDigimonMainAbility(instance);
        };

        // 클릭 시: 대상 지정 모드면 즉시 진화/버프 실행, 일반 모드면 우측 DP 증가치 인스펙터 고정/해제!
        fieldCardBorder.MouseLeftButtonUp += (_, _) =>
        {
            if (isPendingAllyTargetMode && _pendingHandCard != null)
            {
                if (isValidPendingTarget)
                {
                    ExecutePendingHandActionOnTarget(instance);
                }
                else
                {
                    ShowToast(false, invalidTargetReason);
                }
            }
            else
            {
                _pinnedFieldInstance = ReferenceEquals(_pinnedFieldInstance, instance) ? null : instance;
                UpdateFieldDigimonBreakdown(instance, isPinned: _pinnedFieldInstance != null, isOpponent: false);
                RefreshAllSimulatorUi();
            }
        };

        return fieldCardBorder;
    }

    private void ExecutePendingHandActionOnTarget(CardInstance target)
    {
        if (_pendingHandCard == null)
            return;

        AutoPassBreedingIfNeeded();
        var cardToUse = _pendingHandCard;
        bool isEvo = _pendingIsEvolution;
        _pendingHandCard = null;
        _pendingTargetsOpponent = false;

        if (isEvo)
        {
            ExecuteEvolveDigimonWithEffects(target, cardToUse);
        }
        else
        {
            ExecutePlayCardWithEffects(cardToUse, target);
        }
    }

    /// <summary>패 카드 위에 붙는 한 줄 효과 요약 — 카드의 실제 효과 문장(첫 줄)에서 설명 괄호를 빼고 줄여서 만든다</summary>
    private static (string Text, string ColorHex) GetHandCardQuickEffectSummary(CardData card)
    {
        static string Shorten(string text)
        {
            string line = text.Split('\n')[0];
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\s*\([^)]*\)", string.Empty).Trim();
            return line.Length > 26 ? line[..26] + "…" : line;
        }

        string color = card.Type switch
        {
            CardType.Tamer => "#F9E2AF",
            CardType.Option => "#89DCEB",
            _ => card.IsBlocker ? "#FAB387" : "#A6E3A1"
        };

        if (!string.IsNullOrWhiteSpace(card.DisplayEffectText))
            return ((card.Type == CardType.Tamer ? "🎖️ " : card.Type == CardType.Option ? "✨ " : "⚡ ") + Shorten(card.DisplayEffectText), color);
        if (!string.IsNullOrWhiteSpace(card.DisplayInheritedEffectText))
            return card.IsDual ? ("⚡ 옵션 쪽: " + Shorten(card.DisplayInheritedEffectText), "#89DCEB") : ("🧬 " + Shorten(card.DisplayInheritedEffectText), "#A6E3A1");
        return ("⚔️ 효과 없음 (바닐라)", "#9399B2");
    }

    private void RenderHand()
    {
        HandPanel.Children.Clear();
        var handAbilities = _gameState.CurrentPhase == Phase.Main ? _engine.GetHandMainAbilities(_gameState.Player) : Array.Empty<GoldfishEngine.MainAbility>();

        int handCount = _gameState.Player.Hand.Count;
        int handIndex = 0;
        foreach (var card in _gameState.Player.Hand.ToList())
        {
            bool isThisCardPending = ReferenceEquals(_pendingHandCard, card);
            var cardStack = new StackPanel();

            // 실물 카드 디자인 프레임 (132 x 162) — 좌상단에 숫자 코스트 배지 표시
            var visualCard = CreateCardVisualFrame(card, width: 114, height: 150);
            cardStack.Children.Add(visualCard);

            // 패 카드 하단 핵심 효과(DP증가/시큐리티 추가공격/테이머/옵션) 한줄 요약 배지
            var (effText, effColor) = GetHandCardQuickEffectSummary(card);
            cardStack.Children.Add(new Border
            {
                Background = BrushFromHex("#1E1E2E"),
                BorderBrush = BrushFromHex("#313244"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = effText,
                    Foreground = BrushFromHex(effColor),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 9.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            });

            // 등장 / 진화 / 사용은 버튼 대신 드래그 앤 드롭으로만 한다 (필드 디지몬 위 = 진화, 배틀 에어리어 빈 곳 = 등장·옵션 발동)

            // 2-2. 실제 카드를 손에서 집어드는 느낌의 호버 리프트 & 스케일 트랜스폼 애니메이션
            // 유희왕식 부채꼴 손패: 가운데 카드를 기준으로 양옆으로 기울이고 바깥쪽일수록 살짝 내려간다
            double fanOffset = handIndex - (handCount - 1) / 2.0;
            double fanAngle = fanOffset * 2.5;
            double restY = (isThisCardPending ? -14 : 0) + fanOffset * fanOffset * 2.2;
            handIndex++;

            var translateTrans = new TranslateTransform(0, restY);
            var scaleTrans = new ScaleTransform(1.0, 1.0);
            var rotateTrans = new RotateTransform(isThisCardPending ? 0 : fanAngle);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTrans);
            transformGroup.Children.Add(rotateTrans);
            transformGroup.Children.Add(translateTrans);

            var wrapper = new Border
            {
                Width = 128,
                Background = BrushFromHex(isThisCardPending ? "#E626283E" : "#E00E1222"),
                BorderBrush = BrushFromHex(isThisCardPending ? "#F9E2AF" : "#40FFFFFF"),
                BorderThickness = new Thickness(isThisCardPending ? 2.5 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 6, -4, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 1.0),
                RenderTransform = transformGroup,
                Effect = Theme.GetEffect("Neo.Shadow.Float"),
                Child = cardStack
            };

            wrapper.MouseEnter += (_, _) =>
            {
                UpdateCardInspector(card);
                Panel.SetZIndex(wrapper, 100);
                wrapper.BorderBrush = GetCardColorBrush(card.Color);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var liftAnim = new DoubleAnimation(-22, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
                var zoomAnim = new DoubleAnimation(1.07, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
                var straightenAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
                translateTrans.BeginAnimation(TranslateTransform.YProperty, liftAnim);
                rotateTrans.BeginAnimation(RotateTransform.AngleProperty, straightenAnim);
                scaleTrans.BeginAnimation(ScaleTransform.ScaleXProperty, zoomAnim);
                scaleTrans.BeginAnimation(ScaleTransform.ScaleYProperty, zoomAnim);
            };

            wrapper.MouseLeave += (_, _) =>
            {
                Panel.SetZIndex(wrapper, 0);
                wrapper.BorderBrush = BrushFromHex(isThisCardPending ? "#F9E2AF" : "#40FFFFFF");
                var dropAnim = new DoubleAnimation(restY, TimeSpan.FromMilliseconds(140));
                var tiltAnim = new DoubleAnimation(isThisCardPending ? 0 : fanAngle, TimeSpan.FromMilliseconds(140));
                var zoomOutAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140));
                translateTrans.BeginAnimation(TranslateTransform.YProperty, dropAnim);
                rotateTrans.BeginAnimation(RotateTransform.AngleProperty, tiltAnim);
                scaleTrans.BeginAnimation(ScaleTransform.ScaleXProperty, zoomOutAnim);
                scaleTrans.BeginAnimation(ScaleTransform.ScaleYProperty, zoomOutAnim);
            };

            wrapper.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (!_gameState.IsGameOver)
                {
                    _dragStartPoint = e.GetPosition(this);
                    _potentialDragHandCard = card;
                    _potentialDragFieldAttacker = null;
                    _potentialDragBreedingDigimon = null;
                    _potentialDragFieldTamer = null;
                }
            };

            // 우클릭: 패의 이 카드로 쓰는 [Hand] [Main] 효과
            if (handAbilities.Any(a => ReferenceEquals(a.Card, card)))
                wrapper.ToolTip = "우클릭: 이 카드의 패 [메인] 효과 쓰기";
            else if (card.IsDual)
                wrapper.ToolTip = CardScripts.IsUsableAsOption(card)
                    ? "듀얼 카드 — 내 디지몬 위에 놓으면 진화, 배틀 에어리어 빈 곳에 놓으면 옵션 쪽 사용"
                    : "듀얼 카드 — 내 디지몬 위에 놓으면 진화 (옵션 쪽 사용은 아직 지원하지 않음)";
            wrapper.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                TryActivateZoneAbility(card, MainAbilityZone.Hand);
            };

            HandPanel.Children.Add(wrapper);
        }
    }

    // ==================== 🖱️ 드래그 앤 드롭 (패 -> 진화/등장/옵션 & 필드 디지몬 -> 배틀이동/공격) ====================
    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {

        if (SimulatorView.Visibility != Visibility.Visible)
            return;

        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            if (_isDragging)
            {
                CancelDragVisuals();
                RefreshAllSimulatorUi();
            }
            return;
        }

        if (_potentialDragHandCard == null && _potentialDragFieldAttacker == null && _potentialDragBreedingDigimon == null && _potentialDragFieldTamer == null)
            return;

        Point pos = e.GetPosition(this);
        if (!_isDragging)
        {
            Vector diff = pos - _dragStartPoint;
            if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
            {
                StartCardDrag(pos);
            }
        }

        if (_isDragging && _dragGhostBorder != null)
        {
            Canvas.SetLeft(_dragGhostBorder, pos.X - 68);
            Canvas.SetTop(_dragGhostBorder, pos.Y - 96);
            UpdateDragHoverFeedback(pos);
        }
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            _potentialDragHandCard = null;
            _potentialDragFieldAttacker = null;
            _potentialDragBreedingDigimon = null;
            _potentialDragFieldTamer = null;
            return;
        }

        e.Handled = true;
        Point dropPos = e.GetPosition(this);
        CompleteCardDrop(dropPos);
    }

    private void StartCardDrag(Point pos)
    {
        _isDragging = true;
        // 손패를 끌기 시작하면 육성 페이즈를 넘긴다. 필드 카드는 자리만 옮길 수도 있으니
        // 상대 쪽(공격할 곳)으로 가져갈 때 넘긴다 (UpdateDragHoverFeedback · CompleteCardDrop)
        if (_potentialDragHandCard != null)
        {
            AutoPassBreedingIfNeeded();
        }

        CardData? cardToPreview = _potentialDragHandCard
            ?? _potentialDragFieldAttacker?.TopCard
            ?? _potentialDragBreedingDigimon?.TopCard
            ?? _potentialDragFieldTamer?.Card;
        if (cardToPreview == null)
            return;

        if (_potentialDragHandCard != null)
        {
            var hc = _potentialDragHandCard;
            _pendingAttackerInstance = null;
            _pendingHandCard = hc;
            _pendingIsEvolution = hc.Type == CardType.Digimon;
            _pendingTargetsOpponent = IsEnemyTargetedOption(hc);
            RefreshAllSimulatorUi();
        }
        else if (_potentialDragFieldAttacker != null)
        {
            _pendingHandCard = null;
            _pendingAttackerInstance = _potentialDragFieldAttacker;
            RefreshAllSimulatorUi();
        }
        else if (_potentialDragBreedingDigimon != null || _potentialDragFieldTamer != null)
        {
            _pendingHandCard = null;
            _pendingAttackerInstance = null;
            RefreshAllSimulatorUi();
        }

        // UI 갱신 직후 즉시 레이아웃을 확정하여 모든 카드/존의 ActualWidth, ActualHeight, 좌표를 최신화
        UpdateLayout();
        System.Windows.Input.Mouse.Capture(this, System.Windows.Input.CaptureMode.SubTree);

        // 반투명 플로팅 카드 고스트 생성
        DragOverlayCanvas.Children.Clear();
        DragOverlayCanvas.Visibility = Visibility.Visible;

        var ghostStack = new StackPanel { IsHitTestVisible = false };
        _dragGhostBadgeText = new TextBlock
        {
            Text = "🖱️ 드래그하여 대상 위에 놓으세요",
            Foreground = BrushFromHex("#11111B"),
            FontWeight = FontWeights.ExtraBold,
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };

        _dragGhostBadgeBorder = new Border
        {
            Background = BrushFromHex("#F9E2AF"),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _dragGhostBadgeText,
            IsHitTestVisible = false
        };
        ghostStack.Children.Add(_dragGhostBadgeBorder);

        var ghostCardFrame = CreateCardVisualFrame(cardToPreview, width: 126, height: 168);
        ghostCardFrame.Opacity = 0.88;
        ghostCardFrame.HorizontalAlignment = HorizontalAlignment.Center;
        ghostCardFrame.IsHitTestVisible = false;
        ghostStack.Children.Add(ghostCardFrame);

        _dragGhostBorder = new Border
        {
            Child = ghostStack,
            IsHitTestVisible = false
        };

        DragOverlayCanvas.Children.Add(_dragGhostBorder);
        Canvas.SetLeft(_dragGhostBorder, pos.X - 68);
        Canvas.SetTop(_dragGhostBorder, pos.Y - 96);
    }

    private bool IsPointOverElement(FrameworkElement? element, Point windowPos, double expandMargin = 0)
    {
        if (element == null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        try
        {
            Point topLeft = element.TranslatePoint(new Point(0, 0), this);
            Rect rect = new Rect(
                topLeft.X - expandMargin,
                topLeft.Y - expandMargin,
                element.ActualWidth + expandMargin * 2,
                element.ActualHeight + expandMargin * 2);
            return rect.Contains(windowPos);
        }
        catch
        {
            return false;
        }
    }

    private CardInstance? FindClosestCardInPanel(Panel? panel, Point windowPos, double expandMargin = 10)
    {
        if (panel == null || !panel.IsVisible)
            return null;

        CardInstance? bestCard = null;
        double bestDistSq = double.MaxValue;

        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not CardInstance ci || !child.IsVisible || child.ActualWidth <= 0 || child.ActualHeight <= 0)
                continue;

            try
            {
                Point topLeft = child.TranslatePoint(new Point(0, 0), this);
                Rect rect = new Rect(
                    topLeft.X - expandMargin,
                    topLeft.Y - expandMargin,
                    child.ActualWidth + expandMargin * 2,
                    child.ActualHeight + expandMargin * 2);

                if (rect.Contains(windowPos))
                {
                    Point center = new Point(topLeft.X + child.ActualWidth * 0.5, topLeft.Y + child.ActualHeight * 0.5);
                    double dx = windowPos.X - center.X;
                    double dy = windowPos.Y - center.Y;
                    double distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestCard = ci;
                    }
                }
            }
            catch
            {
                // ignore coordinate transform errors
            }
        }

        return bestCard;
    }

    private (CardInstance? AllyDigimon, CardInstance? OpponentDigimon, bool OverBattleZone, bool OverBreedingZone, bool OverOpponentSecurityZone, bool OverOpponentBattleZone)
        FindDropTargetInfo(Point windowPos)
    {
        bool overBattleZone = IsPointOverElement(BattleAreaZoneBorder, windowPos, expandMargin: 4);
        bool overBreedingZone = IsPointOverElement(BreedingAreaZoneBorder, windowPos, expandMargin: 4);
        bool overOpponentSecurity = IsPointOverElement(OpponentSecurityZoneBorder, windowPos, expandMargin: 4);
        bool overOpponentBattleZone = IsPointOverElement(OpponentBattleAreaBorder, windowPos, expandMargin: 4);

        CardInstance? opponentDigimon = overOpponentBattleZone
            ? FindClosestCardInPanel(OpponentBattleAreaPanel, windowPos, expandMargin: 12)
            : null;

        CardInstance? allyDigimon = null;
        if (overBreedingZone && _gameState.Player.BreedingArea != null)
        {
            allyDigimon = _gameState.Player.BreedingArea;
        }
        else if (overBattleZone)
        {
            allyDigimon = FindClosestCardInPanel(BattleAreaPanel, windowPos, expandMargin: 10);
        }

        return (allyDigimon, opponentDigimon, overBattleZone, overBreedingZone, overOpponentSecurity, overOpponentBattleZone);
    }

    private (bool IsEvolve, CardInstance? EvoTarget, bool IsPlay, bool IsInvalidBreeding)
        ResolveHandDigimonDropIntent(CardData card, (CardInstance? AllyDigimon, CardInstance? OpponentDigimon, bool OverBattleZone, bool OverBreedingZone, bool OverOpponentSecurityZone, bool OverOpponentBattleZone) info)
    {
        // 1) 육성 에어리어(Breeding Area) 위에 놓았을 때
        if (info.OverBreedingZone)
        {
            if (_gameState.Player.BreedingArea != null)
                return (true, _gameState.Player.BreedingArea, false, false);
            return (false, null, false, true);
        }

        // 2) 배틀 에어리어(Battle Area)의 특정 아군 디지몬 카드 위에 놓았을 때
        if (info.AllyDigimon != null)
        {
            // Lv.3 성장기 디지몬은 진화 조건이 Lv.2(유년기)이므로 배틀 에어리어 디지몬 위에는 절대 진화할 수 없음 -> 무조건 [배틀 에어리어 일반 등장]으로 처리!
            if (card.Level == 3)
            {
                return (false, null, true, false);
            }

            // 진화 가능 조건이 일치하는 디지몬 위에 올렸을 때 -> [진화]!
            if (ActionValidator.ValidateEvolution(_gameState, info.AllyDigimon, card).CanExecute ||
                info.AllyDigimon.TopCard.Level == card.EvoRequirementLevel)
            {
                return (true, info.AllyDigimon, false, false);
            }

            // 만약 배틀 에어리어에 진화 가능한 레벨의 디지몬이 아예 없는데 다른 레벨 디지몬 카드 위로 드래그했다면 -> [일반 등장]으로 스마트 처리!
            bool anyValidEvoInBattle = _gameState.Player.BattleArea.Any(d => d.TopCard.Level == card.EvoRequirementLevel);
            if (!anyValidEvoInBattle)
            {
                return (false, null, true, false);
            }

            return (true, info.AllyDigimon, false, false);
        }

        // 3) 배틀 에어리어 빈 공간에 놓았을 때 -> [일반 등장]!
        if (info.OverBattleZone)
        {
            return (false, null, true, false);
        }

        return (false, null, false, false);
    }

    private void ResetDropZoneHighlights()
    {
        BreedingAreaZoneBorder.BorderBrush = Theme.GetBrush("Neo.Brush.Green.Soft");
        BreedingAreaZoneBorder.BorderThickness = new Thickness(1);

        BattleAreaZoneBorder.BorderBrush = Theme.GetBrush("Neo.Brush.Cyan.Soft");
        BattleAreaZoneBorder.BorderThickness = new Thickness(1);

        OpponentSecurityZoneBorder.BorderBrush = Theme.GetBrush("Neo.Brush.Rose.Soft");
        OpponentSecurityZoneBorder.BorderThickness = new Thickness(1);

        OpponentBattleAreaBorder.BorderBrush = Theme.GetBrush("Neo.Brush.Rose.Soft");
        OpponentBattleAreaBorder.BorderThickness = new Thickness(1);
    }

    private void HighlightDropZone(Border? targetZone, string hexColor)
    {
        ResetDropZoneHighlights();
        if (targetZone != null)
        {
            targetZone.BorderBrush = BrushFromHex(hexColor);
            targetZone.BorderThickness = new Thickness(3.0);
        }
    }

    private void UpdateDragHoverFeedback(Point pos)
    {
        if (_dragGhostBadgeText == null || _dragGhostBadgeBorder == null)
            return;

        var info = FindDropTargetInfo(pos);

        if (_potentialDragHandCard != null)
        {
            var card = _potentialDragHandCard;
            if (SpecialHandDropActions(card, info) is { Count: > 0 } specials)
            {
                bool choose = specials.Count > 1 || DescribeOtherHandDrop(card, info) != null;
                HighlightDropZone(BattleAreaZoneBorder, "#89DCEB");
                _dragGhostBadgeText.Text = choose
                    ? $"{specials[0].Label} 가능 — 놓으면 할 일을 고르기"
                    : $"{specials[0].Label}!";
                _dragGhostBadgeBorder.Background = BrushFromHex("#89DCEB");
                return;
            }
            if (card.Type == CardType.Digimon)
            {
                var intent = ResolveHandDigimonDropIntent(card, info);

                if (intent.IsInvalidBreeding)
                {
                    HighlightDropZone(BreedingAreaZoneBorder, "#F38BA8");
                    _dragGhostBadgeText.Text = "⚠️ 육성존 비어있음 ([🥚부화] 먼저 실행 또는 배틀존에 등장!)";
                    _dragGhostBadgeBorder.Background = BrushFromHex("#F38BA8");
                    return;
                }

                if (intent.IsEvolve && intent.EvoTarget != null)
                {
                    var check = ActionValidator.ValidateEvolution(_gameState, intent.EvoTarget, card);
                    HighlightDropZone(info.OverBreedingZone ? BreedingAreaZoneBorder : BattleAreaZoneBorder, check.CanExecute ? "#A6E3A1" : "#F38BA8");
                    if (check.CanExecute)
                    {
                        _dragGhostBadgeText.Text = $"🧬 [{intent.EvoTarget.TopCard.DisplayName}] 위에 진화! (코스트 {EvoCostNow(intent.EvoTarget, card)}, 1드로우)";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#A6E3A1");
                        ShowToast(true, check.GuideMessage);
                    }
                    else
                    {
                        _dragGhostBadgeText.Text = $"⚠️ 진화 불가 (Lv.{card.EvoRequirementLevel} 필요) — 빈 공간에 놓으면 등장";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#F38BA8");
                        ShowToast(false, check.Reason);
                    }
                    return;
                }

                if (intent.IsPlay)
                {
                    var playCheck = ActionValidator.ValidatePlay(_gameState, card);
                    HighlightDropZone(BattleAreaZoneBorder, playCheck.CanExecute ? "#89B4FA" : "#F38BA8");
                    if (playCheck.CanExecute)
                    {
                        _dragGhostBadgeText.Text = card.IsDual
                            ? $"⚡ 듀얼 카드 옵션 쪽 사용! (사용 코스트 {DigimonPlayCostLabel(card)})"
                            : $"✨ 배틀 에어리어에 바로 등장! (코스트 {DigimonPlayCostLabel(card)})";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#89B4FA");
                        ShowToast(true, playCheck.GuideMessage);
                    }
                    else
                    {
                        _dragGhostBadgeText.Text = card.IsDual ? "⚠️ 옵션 쪽 사용 불가" : "⚠️ 등장 불가";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#F38BA8");
                        ShowToast(false, playCheck.Reason);
                    }
                    return;
                }

                ResetDropZoneHighlights();
                _dragGhostBadgeText.Text = card.IsDual
                    ? $"🖱️ 디지몬 위에 놓으면 [진화({card.EvoCost})] / 배틀존 빈 곳에 놓으면 [옵션 쪽 사용({card.PlayCost})]"
                    : $"🖱️ 디지몬 위에 놓으면 [진화({card.EvoCost})] / 배틀존에 놓으면 [등장({card.PlayCost})]";
                _dragGhostBadgeBorder.Background = BrushFromHex("#F9E2AF");
            }
            else if (card.Type == CardType.Option)
            {
                if (_pendingTargetsOpponent)
                {
                    var oppTarget = info.OpponentDigimon
                        ?? (info.OverOpponentBattleZone ? BestEnemyOptionTarget(card) : null);

                    if (oppTarget != null && !CardScripts.IsValidMainTarget(card, oppTarget))
                    {
                        ResetDropZoneHighlights();
                        _dragGhostBadgeText.Text = $"❌ [{oppTarget.TopCard.DisplayName}]에게는 [{card.DisplayName}]을(를) 쓸 수 없습니다 (대상 조건)";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#F38BA8");
                    }
                    else if (oppTarget != null)
                    {
                        HighlightDropZone(OpponentBattleAreaBorder, "#A6E3A1");
                        _dragGhostBadgeText.Text = $"🔥 상대 [{oppTarget.TopCard.DisplayName}]에게 [{card.DisplayName}] 발동!";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#A6E3A1");
                    }
                    else
                    {
                        ResetDropZoneHighlights();
                        _dragGhostBadgeText.Text = "🖱️ 상단 [상대 배틀 에어리어]의 디지몬 위에 드롭하세요!";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#F9E2AF");
                    }
                }
                else if (IsAllyTargetedOption(card))
                {
                    var allyTarget = info.AllyDigimon
                        ?? (info.OverBattleZone ? _gameState.Player.BattleArea.Where(d => IsValidAllyOptionTarget(card, d)).OrderByDescending(d => d.GetCurrentDP()).FirstOrDefault() : null);

                    if (allyTarget != null && !_gameState.Player.BattleArea.Contains(allyTarget))
                        allyTarget = null;

                    if (allyTarget != null)
                    {
                        HighlightDropZone(BattleAreaZoneBorder, "#A6E3A1");
                        _dragGhostBadgeText.Text = $"✨ 아군 [{allyTarget.TopCard.DisplayName}]에게 [{card.DisplayName}] 적용!";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#A6E3A1");
                    }
                    else
                    {
                        ResetDropZoneHighlights();
                        _dragGhostBadgeText.Text = "🖱️ [내 배틀 에어리어]의 아군 디지몬 위에 드롭하세요!";
                        _dragGhostBadgeBorder.Background = BrushFromHex("#F9E2AF");
                    }
                }
                else
                {
                    // 대상이 필요 없는 옵션, 또는 대상 쪽에 조건에 맞는 디지몬이 없어 대상 없이 쓰는 옵션
                    bool overPlaymat = info.OverBattleZone || info.OverBreedingZone || info.AllyDigimon != null
                                       || info.OverOpponentBattleZone || info.OverOpponentSecurityZone || info.OpponentDigimon != null;
                    string noTarget = CardScripts.MainTargetOf(card) is TargetKind.OpponentDigimon or TargetKind.OwnDigimon or TargetKind.OwnDigimonWithDigimonSource
                        ? " — 대상으로 고를 디지몬이 없어 대상 없이 씁니다" : "";
                    HighlightDropZone(overPlaymat ? BattleAreaZoneBorder : null, "#89B4FA");
                    _dragGhostBadgeText.Text = overPlaymat
                        ? $"⚡ [{card.DisplayName}] 즉시 발동! (코스트 {PlayCostNow(card)}){noTarget}"
                        : $"🖱️ 배틀 에어리어에 드롭하여 [{card.DisplayName}] 발동!";
                    _dragGhostBadgeBorder.Background = BrushFromHex(overPlaymat ? "#A6E3A1" : "#F9E2AF");
                }
            }
            else if (card.Type == CardType.Tamer)
            {
                bool overPlaymat = info.OverBattleZone || info.OverBreedingZone || info.AllyDigimon != null;
                HighlightDropZone(overPlaymat ? BattleAreaZoneBorder : null, "#A6E3A1");
                _dragGhostBadgeText.Text = overPlaymat
                    ? $"🎖️ 테이머 [{card.DisplayName}] 필드에 등장! (코스트 {PlayCostNow(card)})"
                    : "🖱️ 배틀 에어리어에 드롭하여 테이머를 등장시키세요!";
                _dragGhostBadgeBorder.Background = BrushFromHex(overPlaymat ? "#A6E3A1" : "#F9E2AF");
            }
        }
        else if (_potentialDragBreedingDigimon != null)
        {
            if (info.OverBattleZone)
            {
                var moveCheck = ActionValidator.ValidateMoveFromBreeding(_gameState);
                HighlightDropZone(BattleAreaZoneBorder, moveCheck.CanExecute ? "#89DCEB" : "#F38BA8");
                _dragGhostBadgeText.Text = moveCheck.CanExecute
                    ? $"🚀 [{_potentialDragBreedingDigimon.TopCard.DisplayName}] 배틀 에어리어로 이동!"
                    : $"⚠️ {moveCheck.Reason}";
                _dragGhostBadgeBorder.Background = BrushFromHex(moveCheck.CanExecute ? "#A6E3A1" : "#F38BA8");
            }
            else
            {
                ResetDropZoneHighlights();
                _dragGhostBadgeText.Text = "🖱️ 우측 [내 배틀 에어리어]로 드롭하여 배틀 필드로 이동!";
                _dragGhostBadgeBorder.Background = BrushFromHex("#89DCEB");
            }
        }
        else if (_potentialDragFieldTamer != null)
        {
            // 테이머는 공격하지 않으니 끌기 = 자리 이동뿐
            if (info.OverBattleZone)
            {
                HighlightDropZone(BattleAreaZoneBorder, "#89DCEB");
                _dragGhostBadgeText.Text = $"📍 [{_potentialDragFieldTamer.Card.DisplayName}] 여기로 옮기기";
                _dragGhostBadgeBorder.Background = BrushFromHex("#89DCEB");
                ShowFieldMovePreview(FieldMoveTarget(pos));
            }
            else
            {
                ResetDropZoneHighlights();
                HideFieldMovePreview();
                _dragGhostBadgeText.Text = "🖱️ 내 배틀 에어리어 안에 놓으면 테이머 자리를 옮깁니다";
                _dragGhostBadgeBorder.Background = BrushFromHex("#F9E2AF");
            }
        }
        else if (_potentialDragFieldAttacker != null)
        {
            var attacker = _potentialDragFieldAttacker;
            bool overOpponentSide = info.OpponentDigimon != null || info.OverOpponentSecurityZone || info.OverOpponentBattleZone;
            if (!overOpponentSide)
            {
                if (info.OverBattleZone)
                    ShowFieldMovePreview(FieldMoveTarget(pos));
                else
                    HideFieldMovePreview();
            }
            else
            {
                HideFieldMovePreview();
                // 공격하러 상대 쪽으로 가져가면 그때 육성 페이즈를 넘기고 공격 대기 표시 (상대 레스트 디지몬 강조)
                if (_gameState.CurrentPhase == Phase.Breeding)
                {
                    AutoPassBreedingIfNeeded();
                    _pendingAttackerInstance = attacker;
                    RefreshAllSimulatorUi();
                    UpdateLayout();
                    info = FindDropTargetInfo(pos);
                }
            }

            if (!overOpponentSide && info.OverBattleZone)
            {
                HighlightDropZone(BattleAreaZoneBorder, "#89DCEB");
                _dragGhostBadgeText.Text = $"📍 [{attacker.TopCard.DisplayName}] 여기로 옮기기 (공격은 상대 쪽에 놓기)";
                _dragGhostBadgeBorder.Background = BrushFromHex("#89DCEB");
            }
            else if (info.OpponentDigimon != null)
            {
                var check = ActionValidator.ValidateAttack(_gameState, attacker, info.OpponentDigimon);
                HighlightDropZone(OpponentBattleAreaBorder, check.CanExecute ? "#A6E3A1" : "#F38BA8");
                _dragGhostBadgeText.Text = check.CanExecute
                    ? $"🎯 상대 [{info.OpponentDigimon.TopCard.DisplayName}] 직접 타격!"
                    : "⚠️ 액티브 디지몬은 직접 공격 불가 (시큐리티 존에 드롭하세요)";
                _dragGhostBadgeBorder.Background = BrushFromHex(check.CanExecute ? "#A6E3A1" : "#F38BA8");
            }
            else if (info.OverOpponentSecurityZone || info.OverOpponentBattleZone)
            {
                var check = ActionValidator.ValidateAttack(_gameState, attacker, null);
                var targetBorder = info.OverOpponentSecurityZone ? OpponentSecurityZoneBorder : OpponentBattleAreaBorder;
                HighlightDropZone(targetBorder, check.CanExecute ? "#F38BA8" : "#FAB387");
                _dragGhostBadgeText.Text = check.CanExecute
                    ? (_gameState.DummyOpponent.SecurityStack.Count > 0
                        ? $"⚔️ 시큐리티 어택! ({attacker.GetSecurityAttackCount()}장 타격)"
                        : "🏆 다이렉트 어택 (게임 승리!)")
                    : $"⚠️ {check.Reason}";
                _dragGhostBadgeBorder.Background = BrushFromHex(check.CanExecute ? "#F38BA8" : "#FAB387");
            }
            else
            {
                ResetDropZoneHighlights();
                _dragGhostBadgeText.Text = "🖱️ 상단 [상대 시큐리티 존] 또는 [상대 레스트 디지몬] 위에 드롭하여 공격!";
                _dragGhostBadgeBorder.Background = BrushFromHex("#F9E2AF");
            }
        }
    }

    // ==================== 🔗 링크 (패의 카드를 내 디지몬 옆에 끼운다 — 조건 · 코스트 · 효과는 엔진이 처리) ====================

    /// <summary>놓은 자리가 card를 링크할 수 있는 내 디지몬이면 그 디지몬</summary>
    private CardInstance? LinkTargetOf(CardData card, (CardInstance? AllyDigimon, CardInstance? OpponentDigimon, bool OverBattleZone, bool OverBreedingZone, bool OverOpponentSecurityZone, bool OverOpponentBattleZone) info) =>
        info.AllyDigimon is { } ally && _engine.GetLinkTargets(card).Any(t => ReferenceEquals(t, ally)) ? ally : null;

    private string LinkLabel(CardData card, CardInstance target) =>
        $"🔗 [{target.TopCard.DisplayName}]에 링크" + (_engine.GetLinkCost(card) is int cost ? $" (코스트 {cost})" : "");

    /// <summary>지금 내면 실제로 낼 등장 · 사용 코스트 (코스트 감소 반영 — 카드에 인쇄된 값이 아니라)</summary>
    private int PlayCostNow(CardData card) => CardScripts.EffectivePlayCost(_gameState.Player, card, _gameState.DummyOpponent);

    /// <summary>
    /// 디지몬 등장 코스트 안내: "5", 또는 "비용을 내면 -N" 선택형 감소(예: 자신의 디지몬 1마리를 소멸시켜 -4)를 지금 낼 수 있으면 "5, 비용을 내면 1".
    /// 실제로 낼지는 등장할 때 묻는다.
    /// </summary>
    private string DigimonPlayCostLabel(CardData card)
    {
        int now = PlayCostNow(card);
        int withOption = _engine.PreviewPlayCostWithOptions(_gameState.Player, card);
        return withOption < now ? $"{now}, 비용을 내면 {withOption}" : $"{now}";
    }

    /// <summary>지금 진화하면 실제로 낼 진화 코스트 (진화 코스트 감소 반영, 비용을 내는 감소는 빼고)</summary>
    private int EvoCostNow(CardInstance target, CardData card) => _engine.PreviewEvoCost(_gameState.Player, target, card);

    /// <summary>같은 자리에 놓았을 때 링크 말고 할 수 있는 동작 (없으면 null) — 진화 · 등장 · 옵션 · 테이머</summary>
    private string? DescribeOtherHandDrop(CardData card, (CardInstance? AllyDigimon, CardInstance? OpponentDigimon, bool OverBattleZone, bool OverBreedingZone, bool OverOpponentSecurityZone, bool OverOpponentBattleZone) info)
    {
        switch (card.Type)
        {
            case CardType.Digimon:
                var intent = ResolveHandDigimonDropIntent(card, info);
                if (intent.IsEvolve && intent.EvoTarget != null && ActionValidator.ValidateEvolution(_gameState, intent.EvoTarget, card).CanExecute)
                    return $"🧬 [{intent.EvoTarget.TopCard.DisplayName}] 위에 진화 (코스트 {EvoCostNow(intent.EvoTarget, card)})";
                if (intent.IsPlay && ActionValidator.ValidatePlay(_gameState, card).CanExecute)
                    return card.IsDual
                        ? $"⚡ [{card.DisplayName}] 옵션 쪽 사용 (코스트 {DigimonPlayCostLabel(card)})"
                        : $"✨ 배틀 에어리어에 등장 (코스트 {DigimonPlayCostLabel(card)})";
                return null;
            case CardType.Option:
                return ActionValidator.ValidatePlay(_gameState, card).CanExecute ? $"⚡ [{card.DisplayName}] 사용 (코스트 {PlayCostNow(card)})" : null;
            case CardType.Tamer:
                return ActionValidator.ValidatePlay(_gameState, card).CanExecute ? $"🎖️ 테이머 등장 (코스트 {PlayCostNow(card)})" : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// 패의 카드를 내 디지몬 위에 놓았을 때 할 수 있는 특별 동작 (DNA 진화 · 링크) — 표시 문구와 실행.
    /// 할 수 있는지 · 코스트 · 효과 · 턴 종료는 엔진이 정한다 (GetDnaPairs · GetLinkTargets).
    /// </summary>
    private List<(string Label, Action Run)> SpecialHandDropActions(CardData card, (CardInstance? AllyDigimon, CardInstance? OpponentDigimon, bool OverBattleZone, bool OverBreedingZone, bool OverOpponentSecurityZone, bool OverOpponentBattleZone) info)
    {
        var actions = new List<(string Label, Action Run)>();
        if (info.AllyDigimon is { } ally && GoldfishEngine.HasDna(card))
        {
            var partners = _engine.GetDnaPairs(card)
                .Where(p => ReferenceEquals(p.Left, ally) || ReferenceEquals(p.Right, ally))
                .Select(p => ReferenceEquals(p.Left, ally) ? p.Right : p.Left)
                .Distinct()
                .ToList();
            if (partners.Count > 0)
                actions.Add((DnaLabel(card, ally, partners), () => RunDnaDigivolve(card, ally, partners)));
        }
        if (LinkTargetOf(card, info) is { } linkTarget)
            actions.Add((LinkLabel(card, linkTarget), () => ApplyActionResult(_engine.LinkFromHand(card, linkTarget))));
        return actions;
    }

    private static string DnaLabel(CardData card, CardInstance ally, IReadOnlyList<CardInstance> partners) =>
        $"🧬🧬 DNA 진화: [{ally.TopCard.DisplayName}] + "
        + (partners.Count == 1 ? $"[{partners[0].TopCard.DisplayName}]" : "짝 고르기")
        + (GoldfishEngine.GetDnaCost(card) is int cost ? $" (코스트 {cost})" : "");

    /// <summary>DNA 진화: 놓은 디지몬과 짝이 될 수 있는 디지몬이 여럿이면 짝을 고른다 (고르지 않으면 취소)</summary>
    private void RunDnaDigivolve(CardData card, CardInstance ally, IReadOnlyList<CardInstance> partners)
    {
        var partner = partners.Count == 1
            ? partners[0]
            : _engine.PlayerChoices is { } choices
                ? choices.ChooseDigimon(_gameState, $"[{card.DisplayName}] DNA 진화 — [{ally.TopCard.DisplayName}]과(와) 함께 진화할 디지몬을 고르세요.", partners, partners[0], optional: true)
                : partners[0];
        if (partner == null || !partners.Contains(partner))
        {
            ShowToast(false, "DNA 진화를 취소했습니다.");
            RefreshAllSimulatorUi();
            return;
        }
        ApplyActionResult(_engine.DnaDigivolve(card, ally, partner));
    }

    private void CompleteCardDrop(Point dropPos)
    {
        var draggedHand = _potentialDragHandCard;
        var draggedAttacker = _potentialDragFieldAttacker;
        var draggedBreeding = _potentialDragBreedingDigimon;
        var draggedTamer = _potentialDragFieldTamer;

        var info = FindDropTargetInfo(dropPos);
        // 필드 카드를 내 배틀 에어리어 안에 놓았으면 자리 이동 (공격 · 효과 없음 — 상대 쪽에 놓아야 공격)
        bool overOpponentSide = info.OpponentDigimon != null || info.OverOpponentSecurityZone || info.OverOpponentBattleZone;
        object? movedKey = draggedTamer ?? (object?)draggedAttacker;
        bool moved = movedKey != null && info.OverBattleZone && !overOpponentSide && TryCompleteFieldMove(movedKey, dropPos);
        CancelDragVisuals();

        if (moved)
        {
            RefreshAllSimulatorUi();
            return;
        }

        if (draggedHand != null)
        {
            // 배틀 에어리어 빈 곳에 놓아 새로 나오는 카드(디지몬 · 테이머)는 놓은 자리에 놓인다
            RememberFieldDropPoint(dropPos, info.OverBattleZone, onCard: info.AllyDigimon != null);
            try
            {
                CompleteHandCardDrop(draggedHand, info);
            }
            finally
            {
                _pendingFieldDropPoint = null;
            }
            return;
        }
        else if (draggedBreeding != null)
        {
            if (info.OverBattleZone)
            {
                RememberFieldDropPoint(dropPos, overBattleZone: true, onCard: false);
                try
                {
                    ApplyActionResult(_engine.MoveFromBreedingArea());
                }
                finally
                {
                    _pendingFieldDropPoint = null;
                }
                return;
            }
        }
        else if (draggedAttacker != null && overOpponentSide)
        {
            AutoPassBreedingIfNeeded();
            if (info.OpponentDigimon != null)
            {
                ApplyActionResult(_engine.AttackOpponentDigimon(draggedAttacker, info.OpponentDigimon));
                return;
            }
            var res = _engine.AttackSecurityOrDirect(draggedAttacker);
            if (!res.CanExecute)
            {
                ApplyActionResult(res);
            }
            else
            {
                if (_gameState.SecurityCheckHistory.Count > 0)
                {
                    UpdateCardInspector(_gameState.SecurityCheckHistory[^1].SecurityCard);
                }
                ShowToast(_gameState.LastSecurityBattleWon || _gameState.IsVictory, res.GuideMessage);
                RefreshAllSimulatorUi();
            }
            return;
        }

        RefreshAllSimulatorUi();
    }

    /// <summary>손패 카드를 놓았을 때: DNA · 링크 · 진화 · 등장 · 옵션 · 테이머 (놓은 자리로 판단)</summary>
    private void CompleteHandCardDrop(CardData draggedHand, (CardInstance? AllyDigimon, CardInstance? OpponentDigimon, bool OverBattleZone, bool OverBreedingZone, bool OverOpponentSecurityZone, bool OverOpponentBattleZone) info)
    {
        {
            // DNA 진화 · 링크: 내 디지몬 위에 놓았고 할 수 있으면 (진화 · 등장 · 옵션도 가능하면 무엇을 할지 고르게 한다)
            var specials = SpecialHandDropActions(draggedHand, info);
            if (specials.Count > 0)
            {
                string? other = DescribeOtherHandDrop(draggedHand, info);
                var labels = new List<string>();
                if (other != null)
                    labels.Add(other);
                labels.AddRange(specials.Select(s => s.Label));
                int pick = labels.Count == 1
                    ? 0
                    : _engine.PlayerChoices?.ChooseOption(_gameState, $"[{draggedHand.DisplayName}]을(를) 어떻게 쓸까요?", labels, 0) ?? 0;
                int specialIndex = other != null ? pick - 1 : pick;
                if (specialIndex >= 0 && specialIndex < specials.Count)
                {
                    specials[specialIndex].Run();
                    return;
                }
            }

            if (draggedHand.Type == CardType.Digimon)
            {
                var intent = ResolveHandDigimonDropIntent(draggedHand, info);
                if (intent.IsInvalidBreeding)
                {
                    ShowToast(false, "육성 에어리어에는 디지몬을 바로 등장시킬 수 없습니다. 먼저 [🥚 부화 (Lv.2)]를 누르거나 우측 [배틀 에어리어]에 드롭하세요!");
                    RefreshAllSimulatorUi();
                    return;
                }

                if (intent.IsEvolve && intent.EvoTarget != null)
                {
                    ExecuteEvolveDigimonWithEffects(intent.EvoTarget, draggedHand);
                    return;
                }

                if (intent.IsPlay)
                {
                    ExecutePlayCardWithEffects(draggedHand);
                    return;
                }
            }
            else if (draggedHand.Type == CardType.Option)
            {
                bool isEnemyTargeted = IsEnemyTargetedOption(draggedHand);
                bool isAllyTargeted = IsAllyTargetedOption(draggedHand);

                if (isEnemyTargeted)
                {
                    // 디지몬 위에 놓았으면 그 디지몬 (대상 조건에 안 맞으면 엔진이 메모리를 쓰기 전에 거절하고 안내한다),
                    // 빈 칸이면 대상 조건(예: 전기 충격 = 레스트 상태)을 만족하는 상대 디지몬 중 DP가 가장 높은 것
                    bool overZone = info.OverOpponentBattleZone || info.OverOpponentSecurityZone || info.OverBattleZone;
                    var oppTarget = info.OpponentDigimon ?? (overZone ? BestEnemyOptionTarget(draggedHand) : null);
                    if (oppTarget != null)
                    {
                        ExecutePlayCardWithEffects(draggedHand, oppTarget);
                        return;
                    }
                }
                else if (isAllyTargeted)
                {
                    var allyTarget = (info.AllyDigimon != null && _gameState.Player.BattleArea.Contains(info.AllyDigimon))
                        ? info.AllyDigimon
                        : (info.OverBattleZone
                            ? _gameState.Player.BattleArea.Where(d => IsValidAllyOptionTarget(draggedHand, d)).OrderByDescending(d => d.GetCurrentDP()).FirstOrDefault()
                            : null);
                    if (allyTarget != null)
                    {
                        ExecutePlayCardWithEffects(draggedHand, allyTarget);
                        return;
                    }
                }
                // 대상이 필요 없는 옵션 · 대상 쪽에 조건에 맞는 디지몬이 없는 옵션: 필드 어디에 놓아도 대상 없이 쓴다
                else if (info.OverBattleZone || info.OverBreedingZone || info.AllyDigimon != null
                         || info.OverOpponentBattleZone || info.OverOpponentSecurityZone || info.OpponentDigimon != null)
                {
                    ExecutePlayCardWithEffects(draggedHand);
                    return;
                }
            }
            else if (draggedHand.Type == CardType.Tamer)
            {
                if (info.OverBattleZone || info.OverBreedingZone || info.AllyDigimon != null)
                {
                    ExecutePlayCardWithEffects(draggedHand);
                    return;
                }
            }
        }

        RefreshAllSimulatorUi();
    }

    private void CancelDragVisuals()
    {
        if (System.Windows.Input.Mouse.Captured != null)
        {
            System.Windows.Input.Mouse.Capture(null);
        }

        ResetDropZoneHighlights();
        _isDragging = false;
        _potentialDragHandCard = null;
        _potentialDragFieldAttacker = null;
        _potentialDragBreedingDigimon = null;
        _potentialDragFieldTamer = null;
        HideFieldMovePreview();
        _pendingHandCard = null;
        _pendingAttackerInstance = null;
        _dragGhostBorder = null;
        _dragGhostBadgeBorder = null;
        _dragGhostBadgeText = null;
        DragOverlayCanvas.Children.Clear();
        DragOverlayCanvas.Visibility = Visibility.Collapsed;
    }

    private void RenderTimeAttackHudAndLogs()
    {
        string stageLabel = _gameState.CurrentStage switch
        {
            PracticeStage.Stage1_Goldfish => "Stage 1 (기본 5턴 타임어택)",
            PracticeStage.Stage2_BlockerDefense => "Stage 2 (블로커 & 필드 돌파 챌린지)",
            PracticeStage.Stage3_AiBattle => "Stage 3 (AI 듀얼리스트 실전 배틀)",
            _ => "타임어택"
        };

        TxtHudTurn.Text = _gameState.CurrentStage == PracticeStage.Stage3_AiBattle
            ? $"• 모드: {stageLabel} | 현재 {_gameState.CurrentTurn}턴 (턴 제한 없음)"
            : $"• 모드: {stageLabel} | 현재 {_gameState.CurrentTurn} / {_gameState.TargetTurns}턴";
        TxtHudSecurity.Text = $"• 상대 시큐리티: {_gameState.DummyOpponent.SecurityStack.Count}/5장 | 🛡️ 내 시큐리티: {_gameState.Player.SecurityStack.Count}/5장";
        TxtHudEvoRate.Text = $"• 진화 체인 성공률 (Lv.3→Lv.6): {_gameState.EvolutionChainSuccessRate:F1}% ({_gameState.Lv6CompletedChains}/{_gameState.Lv3StartedChains})";
        TxtHudMemoryEff.Text = $"• 턴당 메모리 사용 효율: {_gameState.MemoryEfficiencyPerTurn:F1} 메모리/턴";

        if (_gameState.IsGameOver)
        {
            TxtGameResultBanner.Text = _gameState.GameOverReason;
            TxtGameResultBanner.Foreground = BrushFromHex(_gameState.IsVictory ? "#A6E3A1" : "#F38BA8");
        }
        else
        {
            TxtGameResultBanner.Text = _gameState.CurrentStage switch
            {
                PracticeStage.Stage1_Goldfish => "Stage 1 진행 중: 5턴 이내에 진화 체인을 완성해 시큐리티 5장을 돌파하세요!",
                PracticeStage.Stage2_BlockerDefense => "Stage 2 진행 중: 상대 필드의 ≪블로커≫를 옵션/고DP로 돌파하고 6턴 내에 승리하세요!",
                PracticeStage.Stage3_AiBattle => "Stage 3 진행 중: 메모리를 아껴 AI에게 역공 각을 주지 말고, AI보다 먼저 다이렉트 어택을 성공시키세요!",
                _ => "진행 중"
            };
            TxtGameResultBanner.Foreground = BrushFromHex("#CDD6F4");
        }

        // 엔진의 ActionLogs(List<string>)는 변경 알림이 없으므로 직접 바인딩하면 안 된다.
        // 바인딩 후 엔진이 로그를 추가한 상태에서 레이아웃이 돌면(예: 시큐리티 어택 중 UpdateLayout)
        // ItemsControl 불일치 예외로 앱이 종료된다 → 매번 스냅샷을 바인딩한다.
        var logSnapshot = _gameState.ActionLogs.ToList();
        LstActionLogs.ItemsSource = logSnapshot;
        // 경기 로그 파일 (앱을 켜기만 한 배경 대련 준비 단계는 남기지 않는다)
        if (_gameState.CurrentPhase != Phase.Setup)
            _gameLogFile.Save(logSnapshot);
        if (logSnapshot.Count > 0)
        {
            LstActionLogs.ScrollIntoView(logSnapshot[^1]);
        }
    }

    // ==================== 인게임 버튼 이벤트 ====================
    private void BtnStage1_Click(object sender, RoutedEventArgs e)
    {
        _isSecurityPeekEnabled = true;
        StartGameForStage(PracticeStage.Stage1_Goldfish);
        RefreshAllSimulatorUi();
        ShowToast(true, "🥇 [STAGE 1: 5턴 타임어택] 시작! 상대 방해 없이 5턴 안에 시큐리티 5장을 깨고 다이렉트 어택을 성공시키세요.");
    }

    private void BtnStage2_Click(object sender, RoutedEventArgs e)
    {
        _isSecurityPeekEnabled = true;
        StartGameForStage(PracticeStage.Stage2_BlockerDefense);
        RefreshAllSimulatorUi();
        ShowToast(true, "🥈 [STAGE 2: 블로커 돌파] 시작! 상대 필드의 ≪블로커≫ 디지몬을 옵션 카드(가이아 포스/코큐토스 브레스)나 고DP 진화로 돌파하세요!");
    }

    private void BtnStage3_Click(object sender, RoutedEventArgs e)
    {
        _isSecurityPeekEnabled = false; // 공식 대인전 룰: 상대 시큐리티는 뒷면 비공개로 시작하며 공격 시 오픈됩니다!
        StartGameForStage(PracticeStage.Stage3_AiBattle);
        RefreshAllSimulatorUi();
        ShowToast(true, "🥉 [STAGE 3: 공식 룰 AI 실전 배틀] 시작! 양쪽 모두 50장 덱 맨 위 5장이 시큐리티(비공개)로 세팅되며, 테이머/옵션의 [시큐리티] 효과가 100% 발동됩니다!");
    }

    private void BtnMulligan_Click(object sender, RoutedEventArgs e)
    {
        ApplyActionResult(_engine.ExecuteMulligan());
    }

    private void BtnHatch_Click(object sender, RoutedEventArgs e)
    {
        ApplyActionResult(_engine.HatchDigitama());
    }

    private void BtnMoveBreeding_Click(object sender, RoutedEventArgs e)
    {
        ApplyActionResult(_engine.MoveFromBreedingArea());
    }

    private void BtnPassBreeding_Click(object sender, RoutedEventArgs e)
    {
        ApplyActionResult(_engine.PassBreedingPhase());
    }

    private void BtnPassTurn_Click(object sender, RoutedEventArgs e)
    {
        ApplyActionResult(_engine.PassTurn());
    }

    /// <summary>
    /// 테이머 · 디지몬의 [메인] 기동 효과 사용 (테이머 칩 클릭 · 필드 디지몬 우클릭). 내 메인 페이즈이고
    /// 블록 선택 · 멀리건 · 상대 턴 리플레이 화면이 없을 때만 열린다.
    /// </summary>
    /// <summary>
    /// 필드 디지몬 우클릭: 이 디지몬이 지금 쓸 수 있는 [메인] 기동 효과 — 맨 위 카드 · 진화원 카드의 [메인] (v0.29.0).
    /// 1개면 바로 확인 창, 여러 개면 고른다 (엔진 GoldfishEngine.GetMainAbilities)
    /// </summary>
    private void TryActivateDigimonMainAbility(CardInstance instance)
    {
        var mine = _gameState.CurrentPhase == Phase.Main
            ? _engine.GetMainAbilities(_gameState.Player).Where(a => ReferenceEquals(a.Digimon, instance)).ToList()
            : new List<GoldfishEngine.MainAbility>();
        if (mine.Count <= 1)
        {
            TryActivateMainAbility(instance, mine.Count == 1 ? mine[0].Card : instance.TopCard);
            return;
        }
        var labels = mine.Select(a => a.Inherited ? $"🧬 진화원 [{a.Card.DisplayName}]의 [메인] 효과" : $"⚡ [{a.Card.DisplayName}]의 [메인] 효과").ToList();
        int pick = _engine.PlayerChoices?.ChooseOption(_gameState, $"[{instance.TopCard.DisplayName}] 어느 [메인] 효과를 쓸까요?", labels, 0) ?? 0;
        if (pick >= 0 && pick < mine.Count)
            TryActivateMainAbility(instance, mine[pick].Card);
    }

    private void TryActivateMainAbility(CardInstance? digimon, CardData card)
    {
        if (_gameState.IsGameOver || _gameState.CurrentPhase != Phase.Main || _gameState.PendingBlock != null || IsReplayRunning)
        {
            ShowToast(false, "[메인] 효과는 내 메인 페이즈에만 쓸 수 있습니다.");
            return;
        }
        if (!_engine.GetMainAbilities(_gameState.Player).Any(a => ReferenceEquals(a.Digimon, digimon) && ReferenceEquals(a.Card, card)))
        {
            ShowToast(false, $"[{card.DisplayName}]은(는) 지금 쓸 수 있는 [메인] 효과가 없습니다. (효과가 없거나 이번 턴에 이미 썼습니다)");
            return;
        }
        bool inherited = digimon != null && _engine.GetMainAbilities(_gameState.Player).Any(a => a.Inherited && ReferenceEquals(a.Digimon, digimon) && ReferenceEquals(a.Card, card));
        string effectText = inherited
            ? $"(진화원 [{card.DisplayName}]의 효과)\n{card.DisplayInheritedEffectText}"
            : string.Join("\n", card.EffectText.Split('\n').Where(l => l.Contains("[Main]") || l.Contains("[메인]") || l.Contains("Digi-Burst")));
        bool go = _engine.PlayerChoices?.Confirm(_gameState, digimon, $"[{card.DisplayName}]의 [메인] 효과를 쓸까요?\n\n{effectText}", true) ?? true;
        if (go)
            ApplyActionResult(_engine.ActivateMainAbility(digimon, card));
    }

    /// <summary>
    /// 패 · 트래시의 카드로 쓰는 [메인] 효과 ([Hand] [Main] · [Trash] [Main]) — 패 카드 우클릭 · 트래시 칸 우클릭.
    /// 필드 [메인] 효과(TryActivateMainAbility)와 같은 조건 · 확인 창.
    /// </summary>
    private void TryActivateZoneAbility(CardData card, MainAbilityZone zone)
    {
        string zoneName = zone == MainAbilityZone.Hand ? "패" : "트래시";
        if (_gameState.IsGameOver || _gameState.CurrentPhase != Phase.Main || _gameState.PendingBlock != null || IsReplayRunning)
        {
            ShowToast(false, $"{zoneName}의 [메인] 효과는 내 메인 페이즈에만 쓸 수 있습니다.");
            return;
        }
        var abilities = zone == MainAbilityZone.Hand ? _engine.GetHandMainAbilities(_gameState.Player) : _engine.GetTrashMainAbilities(_gameState.Player);
        if (!abilities.Any(a => ReferenceEquals(a.Card, card)))
        {
            ShowToast(false, $"[{card.DisplayName}]은(는) 지금 {zoneName}에서 쓸 수 있는 [메인] 효과가 없습니다. (효과가 없거나 이번 턴에 이미 썼습니다)");
            return;
        }
        string tag = zone == MainAbilityZone.Hand ? @"\[Hand\]|\[패\]|【패】" : @"\[Trash\]|\[트래시\]|【트래시】";
        string effectText = string.Join("\n", card.DisplayEffectText.Split('\n')
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, tag, System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
        if (effectText.Length == 0)
            effectText = card.DisplayEffectText;
        bool go = _engine.PlayerChoices?.Confirm(_gameState, null, $"{zoneName}의 [{card.DisplayName}]로 [메인] 효과를 쓸까요?\n\n{effectText}", true) ?? true;
        if (go)
            ApplyActionResult(zone == MainAbilityZone.Hand ? _engine.ActivateHandEffect(card) : _engine.ActivateTrashEffect(card));
    }

    /// <summary>트래시 칸 우클릭: 쓸 수 있는 [Trash] [Main] 효과가 여러 장이면 고르고, 1장이면 바로 확인</summary>
    private void PlayerTrashZone_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        var abilities = _gameState.CurrentPhase == Phase.Main ? _engine.GetTrashMainAbilities(_gameState.Player) : Array.Empty<GoldfishEngine.MainAbility>();
        if (abilities.Count == 0)
        {
            ShowToast(false, "트래시에서 지금 쓸 수 있는 [메인] 효과가 없습니다.");
            return;
        }
        var card = abilities[0].Card;
        if (abilities.Count > 1)
        {
            int pick = _engine.PlayerChoices?.ChooseOption(_gameState, "트래시의 어느 카드로 [메인] 효과를 쓸까요?",
                abilities.Select(a => $"[{a.Card.CardId}] {a.Card.DisplayName}").Append("쓰지 않음").ToList(), 0) ?? 0;
            if (pick < 0 || pick >= abilities.Count)
                return;
            card = abilities[pick].Card;
        }
        TryActivateZoneAbility(card, MainAbilityZone.Trash);
    }

    private void BtnStartSt1_Click(object sender, RoutedEventArgs e)
    {
        var (main, digitama) = StarterDecks.CreateSt1GaiaRedPreset();
        _builderMainDeck = main;
        _builderDigitamaDeck = digitama;
        StartGameForStage(_gameState.CurrentStage);
        RefreshAllSimulatorUi();
        ShowToast(true, "🔥 ST-1 가이아 레드 프리셋으로 새 연습 게임을 시작했습니다!");
    }

    private void BtnStartSt2_Click(object sender, RoutedEventArgs e)
    {
        var (main, digitama) = StarterDecks.CreateSt2CocytusBluePreset();
        _builderMainDeck = main;
        _builderDigitamaDeck = digitama;
        StartGameForStage(_gameState.CurrentStage);
        RefreshAllSimulatorUi();
        ShowToast(true, "❄️ ST-2 코큐토스 블루 프리셋으로 새 연습 게임을 시작했습니다!");
    }

    // ==================== 덱 빌더 & 카드 도감 UI (실물 카드 디자인 그리드) ====================
    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        _catalogPage = 0;
        RefreshCatalogUi();
    }

    // ==================== 바인더 페이지 / 세트 필터 ====================
    private const int CatalogPageSize = 60;
    private int _catalogPage;

    private void BtnCatalogPrev_Click(object sender, RoutedEventArgs e)
    {
        _catalogPage--;
        RefreshCatalogUi();
        CatalogScrollViewer.ScrollToTop();
    }

    private void BtnCatalogNext_Click(object sender, RoutedEventArgs e)
    {
        _catalogPage++;
        RefreshCatalogUi();
        CatalogScrollViewer.ScrollToTop();
    }

    private static string SetPrefixOf(string cardId) => cardId.Contains('-') ? cardId[..cardId.LastIndexOf('-')] : cardId;

    private static int SetGroupOrder(string cardId)
    {
        string p = SetPrefixOf(cardId);
        return p.StartsWith("ST") ? 0 : p.StartsWith("BT") ? 1 : p.StartsWith("EX") ? 2 : 3;
    }

    private static int SetNumber(string cardId) =>
        int.TryParse(new string(SetPrefixOf(cardId).SkipWhile(char.IsLetter).ToArray()), out int n) ? n : 0;

    /// <summary>SET 필터 목록을 도감에 있는 세트 접두어(ST1, BT10, EX3, P ...)로 채운다</summary>
    private void InitSetFilter()
    {
        string? selected = (CmbFilterSet.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var prefixes = _catalogService.Catalog
            .Select(c => c.CardId.Contains('-') ? c.CardId[..c.CardId.LastIndexOf('-')] : c.CardId)
            .Distinct()
            .OrderBy(p => p.StartsWith("ST") ? 0 : p.StartsWith("BT") ? 1 : p.StartsWith("EX") ? 2 : 3)
            .ThenBy(p => int.TryParse(new string(p.SkipWhile(char.IsLetter).ToArray()), out int n) ? n : 0)
            .ThenBy(p => p)
            .ToList();

        CmbFilterSet.Items.Clear();
        CmbFilterSet.Items.Add(new ComboBoxItem { Content = "전체" });
        foreach (var p in prefixes)
            CmbFilterSet.Items.Add(new ComboBoxItem { Content = p });
        CmbFilterSet.SelectedIndex = Math.Max(0, prefixes.IndexOf(selected ?? "") + 1);
    }

    // ==================== 🧩 카드 효과 지원 상태 (EffectSupport: 전부 동작 / 일부 N/M절 / 미지원) ====================
    // 도감 전체(약 4,500장) 문장 분석은 몇 초 걸리므로 앱 시작 · 도감 업데이트 때 백그라운드로 미리 계산한다
    private EffectSupportSummary? _catalogEffectSummary;

    private async System.Threading.Tasks.Task WarmUpEffectSupportAsync()
    {
        var cards = _catalogService.Catalog.ToList();
        try
        {
            var summary = await System.Threading.Tasks.Task.Run(() => EffectSupport.Summarize(cards));
            _catalogEffectSummary = summary;
            RefreshCatalogUi();
        }
        catch (Exception ex)
        {
            ShowToast(false, $"효과 지원 상태를 계산하지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>EFFECT 필터 (1 전부 동작 — 효과 없는 카드 포함 · 2 일부 동작 · 3 미지원)</summary>
    private static bool MatchesEffectFilter(EffectSupportLevel level, int filter) => filter switch
    {
        1 => level is EffectSupportLevel.Full or EffectSupportLevel.NoEffect,
        2 => level == EffectSupportLevel.Partial,
        3 => level == EffectSupportLevel.None,
        _ => true
    };

    /// <summary>카드 상세 안내: 어느 절이 왜 동작하지 않는지 (최대 4절, 영어 원문 절 기준)</summary>
    /// <summary>
    /// 카드 상세의 효과 지원 안내. Note = 화면에 보이는 한글 (동작하지 않는 효과를 공식 문체 한글로),
    /// Detail = 마우스를 올렸을 때 보이는 영어 원문 · 엔진 쪽 사유 (없으면 null)
    /// </summary>
    private static (string Note, string? Detail) DescribeEffectSupport(EffectSupportInfo info)
    {
        if (info.IsBuiltIn)
            return ("⚠️ 일부 효과만 동작 — 이 카드 효과의 일부는 아직 대전에서 동작하지 않습니다. (DP · 코스트 · 진화 조건은 정상 적용)", null);

        var unsupported = info.UnsupportedClauses.ToList();
        string head = info.Level == EffectSupportLevel.None
            ? "⚠️ 이 카드의 효과는 아직 대전에서 동작하지 않습니다 — DP · 코스트 · 진화 조건만 적용됩니다."
            : $"⚠️ 효과 {info.TotalClauses}개 중 {unsupported.Count}개는 아직 대전에서 동작하지 않습니다.";
        var lines = unsupported
            .Take(4)
            .Select(c => KoreanEffectRenderer.RenderClause(c.Text) is { } ko
                ? $"• {(c.Inherited ? "【진화원】 " : "")}{Shorten(ko, 70)}"
                : $"• {(c.Inherited ? "아래 [진화원 효과]" : "아래 [메인 효과]")} 중 1개");   // 한글로 옮기지 못한 절 — 공식 한글 효과 문장과 맞춰 보게
        int more = unsupported.Count - 4;
        string note = head + "\n" + string.Join("\n", lines) + (more > 0 ? $"\n… 외 {more}개" : "") + "\n(마우스를 올리면 영어 원문)";

        string detail = string.Join("\n\n", unsupported.Select(c =>
            $"{(c.Inherited ? "[진화원] " : "")}{c.Text.Trim()}{(string.IsNullOrWhiteSpace(c.Note) ? "" : $"\n— {c.Note}")}"));
        return (note, detail);
    }

    private static string Shorten(string text, int max)
    {
        string oneLine = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 1)] + "…";
    }

    private void RefreshCatalogUi()
    {
        if (CatalogWrapPanel == null)
            return;

        string? keyword = TxtSearchKeyword.Text;
        // 목록에는 한글을 보이고 Tag에 영어 값을 둔다
        CardColor? color = (CmbFilterColor.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "Red" => CardColor.Red,
            "Blue" => CardColor.Blue,
            "Yellow" => CardColor.Yellow,
            "Green" => CardColor.Green,
            "Black" => CardColor.Black,
            "Purple" => CardColor.Purple,
            "White" => CardColor.White,
            _ => null
        };

        CardType? type = (CmbFilterType.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "Digitama" => CardType.Digitama,
            "Digimon" => CardType.Digimon,
            "Tamer" => CardType.Tamer,
            "Option" => CardType.Option,
            _ => null
        };

        int? level = (CmbFilterLevel.SelectedItem as ComboBoxItem)?.Content?.ToString() switch
        {
            "Lv.2" => 2,
            "Lv.3" => 3,
            "Lv.4" => 4,
            "Lv.5" => 5,
            "Lv.6" => 6,
            "Lv.7" => 7,
            _ => null
        };

        string? setPrefix = (CmbFilterSet.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (setPrefix == "전체")
            setPrefix = null;
        int effectFilter = CmbFilterImpl.SelectedIndex;   // 0 전체 · 1 전부 동작 · 2 일부 동작 · 3 미지원
        string? rarity = (CmbFilterRarity.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (rarity == "전체")
            rarity = null;

        // 정렬: 내장 카드 먼저 → 세트(ST → BT → EX → 그 외, 번호순) → 카드 번호
        var results = _catalogService.SearchCards(keyword, color, type, level)
            .Where(c => setPrefix == null || c.CardId.StartsWith(setPrefix + "-", StringComparison.Ordinal))
            .Where(c => effectFilter == 0 || MatchesEffectFilter(EffectSupport.For(c).Level, effectFilter))
            .Where(c => rarity == null || string.Equals(c.Rarity, rarity, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.IsEffectImplemented)
            .ThenBy(c => SetGroupOrder(c.CardId))
            .ThenBy(c => SetNumber(c.CardId))
            .ThenBy(c => c.CardId, StringComparer.Ordinal)
            .ToList();

        // 도감 전체 지원률은 앱 시작 때 백그라운드로 미리 계산해 둔 값 (계산 전이면 안내만)
        string supportText = _catalogEffectSummary is { } cs
            ? $"전부 동작 {cs.Full + cs.NoEffect} · 일부 {cs.Partial} · 미지원 {cs.None} · 효과 절 {cs.ClauseRate:F1}% 동작"
            : "효과 지원 상태 계산 중…";
        TxtCatalogCount.Text = $"검색된 카드: {results.Count}종 · 도감 전체 {_catalogService.Catalog.Count}종 ({supportText})";

        // 수천 장을 한 번에 그리면 느려지므로 한 페이지(60장)씩
        int pageCount = Math.Max(1, (results.Count + CatalogPageSize - 1) / CatalogPageSize);
        _catalogPage = Math.Clamp(_catalogPage, 0, pageCount - 1);
        TxtCatalogPage.Text = $"{_catalogPage + 1} / {pageCount}";
        BtnCatalogPrev.IsEnabled = _catalogPage > 0;
        BtnCatalogNext.IsEnabled = _catalogPage < pageCount - 1;

        CatalogWrapPanel.Children.Clear();
        foreach (var card in results.Skip(_catalogPage * CatalogPageSize).Take(CatalogPageSize))
        {
            int countInDeck = _builderMainDeck.Count(c => c.CardId == card.CardId) +
                              _builderDigitamaDeck.Count(c => c.CardId == card.CardId);

            CatalogWrapPanel.Children.Add(CreateBinderPocket(card, countInDeck));
        }
    }

    /// <summary>
    /// 바인더 앨범의 카드 포켓 1칸: 슬리브에 넣은 카드 + 투입 매수 배지.
    /// 클릭 = 덱에 넣기 / 우클릭 = 빼기 (덱에 들어간 카드는 금색 테두리 + 매수 칩으로 표시)
    /// </summary>
    private FrameworkElement CreateBinderPocket(CardData card, int countInDeck)
    {
        const double cardW = 142, cardH = 198;
        bool inDeck = countInDeck > 0;

        var cardGrid = new Grid { Width = cardW, Height = cardH };
        cardGrid.Children.Add(CreateCardVisualFrame(card, width: cardW, height: cardH));
        // 슬리브 비닐 광택
        cardGrid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Theme.GetBrush("Duel.Brush.SleeveSheen"),
            IsHitTestVisible = false
        });

        if (EffectSupport.For(card) is { BadgeText: { } badge } badgeInfo)
        {
            // 효과의 일부 또는 전부가 대전에서 동작하지 않는 카드 (일부 동작 N/M절 · 효과 미지원)
            string badgeColor = badgeInfo.Level == EffectSupportLevel.None ? "#F38BA8" : "#FAB387";
            cardGrid.Children.Add(new Border
            {
                Background = BrushFromHex("#E6301A0A"),
                BorderBrush = BrushFromHex(badgeColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(7, 1, 7, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(5, 0, 0, 30),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = badge,
                    Foreground = BrushFromHex(badgeColor),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10
                }
            });
        }

        if (inDeck)
        {
            // 덱 투입 매수: 카드 오른쪽 위에 겹쳐 놓은 칩 (최대 4장)
            var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 30, 4, 0) };
            chips.Children.Add(new Border
            {
                Background = Theme.GetBrush("Neo.Brush.Button.Gold"),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 1, 8, 1),
                Effect = Theme.GetEffect("Neo.Shadow.Float"),
                Child = new TextBlock
                {
                    Text = $"×{countInDeck}",
                    Foreground = Theme.GetBrush("Neo.Brush.Text.OnNeon"),
                    FontFamily = (FontFamily)FindResource("Neo.Font.Mono"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 12
                }
            });
            cardGrid.Children.Add(chips);
        }

        // 포켓 아래 칸: 카드 번호 + 매수 표시 + 빼기/넣기 소형 버튼
        var footer = new DockPanel { Margin = new Thickness(2, 5, 2, 0) };
        var btnRem = new Button
        {
            Content = "−",
            Width = 24,
            Height = 20,
            Padding = new Thickness(0),
            FontWeight = FontWeights.ExtraBold,
            IsEnabled = inDeck,
            ToolTip = "덱에서 1장 빼기 (우클릭과 동일)"
        };
        ThemeAssist.SetCornerRadius(btnRem, new CornerRadius(999));
        btnRem.Click += (_, _) => RemoveCardFromBuilderDeck(card);
        var btnAdd = new Button
        {
            Content = "+",
            Width = 24,
            Height = 20,
            Padding = new Thickness(0),
            FontWeight = FontWeights.ExtraBold,
            Background = Theme.GetBrush("Neo.Brush.Green"),
            Foreground = Theme.GetBrush("Neo.Brush.Text.OnNeon"),
            BorderThickness = new Thickness(0),
            ToolTip = "덱에 1장 넣기 (클릭과 동일)"
        };
        ThemeAssist.SetCornerRadius(btnAdd, new CornerRadius(999));
        btnAdd.Click += (_, _) => AddCardToBuilderDeck(card);
        DockPanel.SetDock(btnRem, Dock.Left);
        DockPanel.SetDock(btnAdd, Dock.Right);
        footer.Children.Add(btnRem);
        footer.Children.Add(btnAdd);
        footer.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(card.Rarity) ? $"{card.CardId} · {countInDeck}/4" : $"{card.CardId} · {card.Rarity} · {countInDeck}/4",
            Foreground = Theme.GetBrush(inDeck ? "Neo.Brush.Gold" : "Neo.Brush.Text.Dim"),
            FontFamily = (FontFamily)FindResource("Neo.Font.Mono"),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var scale = new ScaleTransform(1, 1);
        var pocket = new Border
        {
            Width = cardW + 14,
            Padding = new Thickness(6, 6, 6, 6),
            Margin = new Thickness(0, 0, 10, 12),
            CornerRadius = new CornerRadius(10),
            Background = BrushFromHex("#8C050811"),
            BorderBrush = inDeck ? Theme.GetBrush("Duel.Brush.GoldEdge") : Theme.GetBrush("Duel.Brush.ZoneEdge.Neutral"),
            BorderThickness = new Thickness(inDeck ? 1.6 : 1),
            Cursor = System.Windows.Input.Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            Child = new StackPanel { Children = { cardGrid, footer } }
        };
        var tip = new ToolTip { Content = "클릭: 덱에 넣기 · 우클릭: 빼기" };
        tip.Opened += (_, _) => _openCatalogTip = tip;
        pocket.ToolTip = tip;

        pocket.MouseEnter += (_, _) =>
        {
            UpdateCardInspector(card);
            var up = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(120));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, up);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
        };
        pocket.MouseLeave += (_, _) =>
        {
            var down = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, down);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
        };
        // 카드 그림 부분 클릭/우클릭으로 넣고 빼기 (아래 소형 버튼 클릭은 버튼이 처리)
        cardGrid.MouseLeftButtonUp += (_, e) => { e.Handled = true; AddCardToBuilderDeck(card); };
        cardGrid.MouseRightButtonUp += (_, e) => { e.Handled = true; RemoveCardFromBuilderDeck(card); };
        return pocket;
    }

    private void AddCardToBuilderDeck(CardData card)
    {
        int currentCopies = _builderMainDeck.Count(c => c.CardId == card.CardId) +
                            _builderDigitamaDeck.Count(c => c.CardId == card.CardId);

        if (currentCopies >= DeckValidator.MaxCopiesPerCard)
        {
            ShowToast(false, $"동일 카드 번호({card.CardId})는 최대 4장까지만 넣을 수 있습니다.");
            return;
        }

        if (card.Type == CardType.Digitama)
        {
            if (_builderDigitamaDeck.Count >= DeckValidator.MaxDigitamaDeckSize)
            {
                ShowToast(false, "디지타마 덱은 최대 5장까지만 구성할 수 있습니다.");
                return;
            }
            _builderDigitamaDeck.Add(card);
        }
        else
        {
            if (_builderMainDeck.Count >= DeckValidator.RequiredMainDeckSize)
            {
                ShowToast(false, "메인 덱은 최대 50장까지만 구성할 수 있습니다.");
                return;
            }
            _builderMainDeck.Add(card);
        }

        var addedSupport = EffectSupport.For(card);
        if (addedSupport.Level == EffectSupportLevel.Partial)
            ShowToast(false, $"⚠️ [{card.CardId}] {card.DisplayName}은(는) 효과가 일부만 동작합니다"
                + (addedSupport.TotalClauses > 0 ? $" ({addedSupport.SupportedClauses}/{addedSupport.TotalClauses}절)" : "")
                + ". 동작하지 않는 절은 카드 상세에서 확인하세요.");
        else if (addedSupport.Level == EffectSupportLevel.None)
            ShowToast(false, $"⚠️ [{card.CardId}] {card.DisplayName}은(는) 효과가 대전에서 동작하지 않습니다. 덱에 넣을 수는 있지만 DP · 코스트 · 진화 조건만 적용됩니다.");

        RefreshCatalogUi();
        RefreshDeckBuilderUi();
    }

    private void RemoveCardFromBuilderDeck(CardData card)
    {
        if (card.Type == CardType.Digitama)
        {
            var found = _builderDigitamaDeck.FirstOrDefault(c => c.CardId == card.CardId);
            if (found != null) _builderDigitamaDeck.Remove(found);
        }
        else
        {
            var found = _builderMainDeck.FirstOrDefault(c => c.CardId == card.CardId);
            if (found != null) _builderMainDeck.Remove(found);
        }

        RefreshCatalogUi();
        RefreshDeckBuilderUi();
    }

    private void RefreshDeckBuilderUi()
    {
        if (GoldenRatioHudPanel == null)
            return;

        var ratio = DeckValidator.CalculateRatios(_builderMainDeck, _builderDigitamaDeck);
        GoldenRatioHudPanel.Children.Clear();

        foreach (var item in ratio.Items)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });

            var lbl = new TextBlock
            {
                Text = item.Label,
                Foreground = BrushFromHex("#CDD6F4"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = Math.Max(item.RecommendedMax + 4, 16),
                Value = item.CurrentCount,
                Height = 8,
                Margin = new Thickness(5, 0, 5, 0),
                Style = (Style)FindResource("Neo.ProgressBar"),
                Background = BrushFromHex("#313244"),
                Foreground = BrushFromHex(item.StatusText == "적정" ? "#A6E3A1" : item.StatusText == "부족" ? "#F9E2AF" : "#F38BA8"),
                BorderThickness = new Thickness(0)
            };
            Grid.SetColumn(bar, 1);

            var statusTxt = new TextBlock
            {
                Text = $"{item.CurrentCount,2}/{item.RecommendedMin}~{item.RecommendedMax} ({item.StatusText})",
                Foreground = BrushFromHex(item.StatusText == "적정" ? "#A6E3A1" : item.StatusText == "부족" ? "#F9E2AF" : "#F38BA8"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusTxt, 2);

            row.Children.Add(lbl);
            row.Children.Add(bar);
            row.Children.Add(statusTxt);
            GoldenRatioHudPanel.Children.Add(row);
        }

        var (isValid, errors) = DeckValidator.ValidateDeck(_builderMainDeck, _builderDigitamaDeck);
        if (isValid)
        {
            TxtDeckValidationStatus.Text = $"✅ 덱 검증 통과 (메인 {_builderMainDeck.Count}장 / 디지타마 {_builderDigitamaDeck.Count}장)";
            TxtDeckValidationStatus.Foreground = BrushFromHex("#A6E3A1");
        }
        else
        {
            TxtDeckValidationStatus.Text = "⚠️ " + string.Join(" / ", errors);
            TxtDeckValidationStatus.Foreground = BrushFromHex("#F38BA8");
        }

        // 덱 효과 지원률 (전부 동작 / 일부 동작 / 미지원 장수)
        var deckCards = _builderDigitamaDeck.Concat(_builderMainDeck).ToList();
        if (deckCards.Count == 0)
        {
            TxtDeckEffectSupport.Text = " ";
        }
        else
        {
            var ds = EffectSupport.Summarize(deckCards);
            TxtDeckEffectSupport.Text = ds.Partial + ds.None == 0
                ? $"🧩 효과 지원: {ds.Cards}장 모두 대전에서 효과가 동작합니다"
                : $"🧩 효과 지원: 전부 동작 {ds.Full + ds.NoEffect}장 · ⚠ 일부 동작 {ds.Partial}장 · ⚠ 미지원 {ds.None}장";
            TxtDeckEffectSupport.Foreground = BrushFromHex(ds.None > 0 ? "#F38BA8" : ds.Partial > 0 ? "#FAB387" : "#A6E3A1");
        }

        TxtDeckListHeader.Text =$"📋 현재 덱 구성 (메인 {_builderMainDeck.Count}/50장, 디지타마 {_builderDigitamaDeck.Count}/5장)";
        CurrentDeckListPanel.Children.Clear();

        var grouped = _builderDigitamaDeck.Concat(_builderMainDeck)
            .GroupBy(c => c.CardId)
            .OrderBy(g => g.First().Type == CardType.Digitama ? 0 : 1)
            .ThenBy(g => g.First().Level)
            .ThenBy(g => g.Key);

        foreach (var g in grouped)
        {
            var card = g.First();
            int count = g.Count();

            var itemDock = new DockPanel();
            var bmp = GetCardBitmap(card);
            if (bmp != null)
            {
                itemDock.Children.Add(new Border
                {
                    Width = 22,
                    Height = 30,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 6, 0),
                    ClipToBounds = true,
                    Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill }
                });
            }

            itemDock.Children.Add(new TextBlock
            {
                Text = $"[{card.CardId}] {card.DisplayName} {(card.Level > 0 ? $"(Lv.{card.Level})" : $"({KoreanLabels.Type(card.Type)})")}",
                Foreground = BrushFromHex("#F8FAFC"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });

            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (EffectSupport.For(card) is { BadgeText: not null } deckItemSupport)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = "⚠",
                    Foreground = BrushFromHex(deckItemSupport.Level == EffectSupportLevel.None ? "#F38BA8" : "#FAB387"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = deckItemSupport.Level == EffectSupportLevel.None ? "효과 미지원" : deckItemSupport.BadgeText!.TrimStart('⚠', ' ')
                });
            }
            rightStack.Children.Add(new TextBlock
            {
                Text = $"x{count}",
                Foreground = BrushFromHex("#F9E2AF"),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var btnMinus = new Button
            {
                Content = "−",
                Width = 22, Padding = new Thickness(0), FontWeight = FontWeights.ExtraBold,
                Height = 20,
                Background = BrushFromHex("#45475A"),
                Foreground = BrushFromHex("#F8FAFC"),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 3, 0)
            };
            btnMinus.Click += (_, _) => RemoveCardFromBuilderDeck(card);

            var btnPlus = new Button
            {
                Content = "+",
                Width = 22, Padding = new Thickness(0),
                Height = 20,
                Background = BrushFromHex("#A6E3A1"),
                Foreground = BrushFromHex("#11111B"),
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            btnPlus.Click += (_, _) => AddCardToBuilderDeck(card);

            rightStack.Children.Add(btnMinus);
            rightStack.Children.Add(btnPlus);
            itemDock.Children.Add(rightStack);

            var rowBorder = new Border
            {
                Background = BrushFromHex("#B30B1020"),
                BorderBrush = GetCardColorBrush(card.Color),
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 3),
                Child = itemDock
            };
            rowBorder.MouseEnter += (_, _) => UpdateCardInspector(card);
            CurrentDeckListPanel.Children.Add(rowBorder);
        }
    }

    private async void BtnSyncApi_Click(object sender, RoutedEventArgs e)
    {
        BtnSyncApi.IsEnabled = false;
        var progress = new Progress<string>(text => ShowToast(true, text));
        var (success, msg) = await _catalogService.SyncAllSetsFromApiAsync(progress);
        ShowToast(success, msg);
        InitSetFilter();
        _catalogPage = 0;
        // 카드 문장이 바뀌었을 수 있으므로 효과 지원 상태를 다시 계산한다
        EffectSupport.ClearCache();
        _catalogEffectSummary = null;
        RefreshCatalogUi();
        _ = WarmUpEffectSupportAsync();
        BtnSyncApi.IsEnabled = true;
    }

    private void BtnLoadPresetSt1_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("🔥 [추천 1] 워그레이몬 & 오메가몬 초화력 비트 (50장)", StarterDecks.CreateSt1GaiaRedPreset());

    private void BtnLoadPresetSt2_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("❄️ [추천 2] 메탈가루몬 & 오메가몬 연타 (50장)", StarterDecks.CreateSt2CocytusBluePreset());

    private void BtnLoadPresetSt3_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("⚡ [추천 3] 세라피몬 & 홀리드라몬 천사 컨트롤 (50장)", StarterDecks.CreateSt3HeavensYellowPreset());

    private void BtnLoadPresetSt4_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("🌿 [추천 4] 헤라클레스캅테리몬 & 로제몬 (50장)", StarterDecks.CreateSt4GigaGreenPreset());

    private void BtnLoadPresetSt5_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("⚙️ [추천 5] 파워드라몬 & 워그레이몬 철벽 블로커 (50장)", StarterDecks.CreateSt5MachineBlackPreset());

    private void BtnLoadPresetSt6_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetDeckAndSync("🔮 [추천 6] 피에몬 & 데블몬 길동무 (50장)", StarterDecks.CreateSt6VenomVioletPreset());

    private void BtnAutoCompleteDeck_Click(object sender, RoutedEventArgs e)
    {
        // AI 자동 완성은 대전에서 효과가 동작하는 카드만 사용
        var (completedMain, completedDigitama, guideMsg) = StarterDecks.AutoCompleteRecommendedDeck(
            _builderMainDeck,
            _builderDigitamaDeck,
            _catalogService.Catalog.Where(c => c.IsEffectImplemented).ToList());

        _builderMainDeck = completedMain;
        _builderDigitamaDeck = completedDigitama;
        _activeDeckTitle = $"🤖 AI 추천 황금비율 완성 덱 ({_builderMainDeck.Count}장)";
        if (TxtLobbyActiveDeckName != null)
        {
            TxtLobbyActiveDeckName.Text = $"🎴 출전 덱: {_activeDeckTitle}";
        }
        RefreshCatalogUi();
        RefreshDeckBuilderUi();
        ShowToast(true, guideMsg);
    }

    // ==================== 💾 내 덱 저장 · 불러오기 (이름을 붙여 여러 개 저장, data/saved_decks.json) ====================
    private Dictionary<string, CardData> CatalogById() =>
        _catalogService.Catalog.GroupBy(c => c.CardId).ToDictionary(g => g.Key, g => g.First());

    private void BtnSaveCustomDeck_Click(object sender, RoutedEventArgs e)
    {
        if (_builderMainDeck.Count + _builderDigitamaDeck.Count == 0)
        {
            ShowToast(false, "덱이 비어 있습니다. 카드를 담은 뒤 저장하세요.");
            return;
        }

        var (isValid, _) = DeckValidator.ValidateDeck(_builderMainDeck, _builderDigitamaDeck);
        TxtDeckSaveSummary.Text = $"메인 덱 {_builderMainDeck.Count}장 · 디지타마 {_builderDigitamaDeck.Count}장"
            + (isValid ? "" : " · 아직 대전 규격(메인 50장)을 채우지 않은 덱도 저장할 수 있습니다.");
        TxtDeckSaveName.Text = _currentSavedDeckName ?? $"내 덱 {_savedDeckStore.LoadAll().Count + 1}";
        UpdateDeckSaveHint();
        DeckSaveOverlay.Visibility = Visibility.Visible;
        TxtDeckSaveName.Focus();
        TxtDeckSaveName.SelectAll();
    }

    private void UpdateDeckSaveHint()
    {
        string? error = SavedDeckStore.ValidateName(TxtDeckSaveName.Text);
        bool exists = error == null && _savedDeckStore.Exists(TxtDeckSaveName.Text);
        TxtDeckSaveHint.Text = error ?? (exists ? "⚠️ 같은 이름의 덱이 있습니다. 저장하면 그 덱을 덮어씁니다." : " ");
        TxtDeckSaveHint.Foreground = BrushFromHex(error != null ? "#F38BA8" : "#F9E2AF");
        BtnDeckSaveConfirm.IsEnabled = error == null;
        BtnDeckSaveConfirm.Content = exists ? "덮어쓰기" : "저장";
    }

    private void TxtDeckSaveName_TextChanged(object sender, TextChangedEventArgs e) => UpdateDeckSaveHint();

    private void TxtDeckSaveName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && BtnDeckSaveConfirm.IsEnabled)
            BtnDeckSaveConfirm_Click(sender, e);
        else if (e.Key == System.Windows.Input.Key.Escape)
            DeckSaveOverlay.Visibility = Visibility.Collapsed;
    }

    private void BtnDeckSaveConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (SavedDeckStore.ValidateName(TxtDeckSaveName.Text) is { } error)
        {
            ShowToast(false, error);
            return;
        }

        bool overwrote = _savedDeckStore.Exists(TxtDeckSaveName.Text);
        SavedDeck saved;
        try
        {
            saved = _savedDeckStore.Save(TxtDeckSaveName.Text, _builderMainDeck, _builderDigitamaDeck);
        }
        catch (IOException ex)
        {
            ShowToast(false, $"덱을 저장하지 못했습니다: {ex.Message}");
            return;
        }

        DeckSaveOverlay.Visibility = Visibility.Collapsed;
        _currentSavedDeckName = saved.Name;
        SetActiveDeckTitle($"📂 {saved.Name} ({_builderMainDeck.Count}장)");
        InitOpponentDeckCombos();
        ShowToast(true, overwrote ? $"💾 [{saved.Name}] 덱을 새 구성으로 덮어썼습니다." : $"💾 [{saved.Name}] 덱을 저장했습니다.");
    }

    private void BtnDeckSaveCancel_Click(object sender, RoutedEventArgs e) => DeckSaveOverlay.Visibility = Visibility.Collapsed;

    private void BtnLoadCustomDeck_Click(object sender, RoutedEventArgs e)
    {
        RenderDeckLoadList();
        DeckLoadOverlay.Visibility = Visibility.Visible;
    }

    private void RenderDeckLoadList()
    {
        var decks = _savedDeckStore.LoadAll();
        TxtDeckLoadSummary.Text = decks.Count == 0
            ? "저장한 덱이 없습니다. 덱 빌더에서 [💾 저장]을 눌러 이름을 붙여 저장하세요."
            : $"{decks.Count}개 · 최근 저장한 순서";
        DeckLoadListPanel.Children.Clear();
        foreach (var deck in decks)
            DeckLoadListPanel.Children.Add(CreateSavedDeckRow(deck));
    }

    private FrameworkElement CreateSavedDeckRow(SavedDeck deck)
    {
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = deck.Name, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock
        {
            Text = $"메인 {deck.MainDeckCardIds.Count}장 · 디지타마 {deck.DigitamaCardIds.Count}장 · {deck.SavedAt:yyyy-MM-dd HH:mm} 저장",
            Foreground = Theme.GetBrush("Neo.Brush.Text.Muted"),
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var btnLoad = new Button { Content = "불러오기", Style = (Style)FindResource("Neo.Button.Primary"), FontWeight = FontWeights.Bold, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        btnLoad.Click += (_, _) => LoadSavedDeck(deck);

        // 삭제는 두 번 눌러야 한다 (실수로 지우지 않도록)
        var btnDelete = new Button { Content = "삭제", Style = (Style)FindResource("Neo.Button.Ghost"), FontWeight = FontWeights.Bold, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        bool armed = false;
        btnDelete.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                btnDelete.Content = "정말 삭제?";
                btnDelete.Foreground = BrushFromHex("#F38BA8");
                return;
            }
            _savedDeckStore.Delete(deck.Name);
            if (string.Equals(_currentSavedDeckName, deck.Name, StringComparison.OrdinalIgnoreCase))
                _currentSavedDeckName = null;
            InitOpponentDeckCombos();
            RenderDeckLoadList();
            ShowToast(true, $"🗑️ [{deck.Name}] 덱을 삭제했습니다.");
        };

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(btnDelete, Dock.Right);
        DockPanel.SetDock(btnLoad, Dock.Right);
        row.Children.Add(btnDelete);
        row.Children.Add(btnLoad);
        row.Children.Add(info);

        return new Border
        {
            Child = row,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(6),
            Background = Theme.GetBrush("Neo.Brush.SurfaceInput"),
            BorderBrush = BrushFromHex("#33FFFFFF"),
            BorderThickness = new Thickness(1)
        };
    }

    private void LoadSavedDeck(SavedDeck deck)
    {
        var (main, digitama, missing) = SavedDeckStore.Resolve(deck, CatalogById());
        _builderMainDeck = main;
        _builderDigitamaDeck = digitama;
        _currentSavedDeckName = deck.Name;
        SetActiveDeckTitle($"📂 {deck.Name} ({main.Count}장)");
        DeckLoadOverlay.Visibility = Visibility.Collapsed;
        RefreshCatalogUi();
        RefreshDeckBuilderUi();
        ShowToast(missing == 0, missing == 0
            ? $"📂 [{deck.Name}] 덱을 불러왔습니다."
            : $"📂 [{deck.Name}] 덱을 불러왔습니다. 도감에 없는 카드 {missing}장은 빠졌습니다.");
    }

    private void BtnDeckLoadClose_Click(object sender, RoutedEventArgs e) => DeckLoadOverlay.Visibility = Visibility.Collapsed;

    private void SetActiveDeckTitle(string title)
    {
        _activeDeckTitle = title;
        if (TxtLobbyActiveDeckName != null)
            TxtLobbyActiveDeckName.Text = $"🎴 출전 덱: {_activeDeckTitle}";
    }

    // 창 바깥(어두운 배경)을 누르면 닫고, 창 안을 누르면 닫히지 않게 한다
    private void DeckOverlayBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DeckSaveOverlay.Visibility = Visibility.Collapsed;
        DeckLoadOverlay.Visibility = Visibility.Collapsed;
    }

    private void DeckOverlayPanel_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

    private void BtnClearDeck_Click(object sender, RoutedEventArgs e)
    {
        _builderMainDeck.Clear();
        _builderDigitamaDeck.Clear();
        RefreshCatalogUi();
        RefreshDeckBuilderUi();
        ShowToast(true, "덱 구성을 초기화했습니다. 원하는 카드를 고른 뒤 [🤖 AI 추천 덱 자동 완성]을 누르면 남은 자리를 황금 비율로 채워줍니다!");
    }

    private void BtnPlayWithCurrentDeck_Click(object sender, RoutedEventArgs e)
    {
        var (isValid, errors) = DeckValidator.ValidateDeck(_builderMainDeck, _builderDigitamaDeck);
        if (!isValid)
        {
            ShowToast(false, "덱 규칙을 만족하지 않아 게임을 시작할 수 없습니다 (먼저 [🤖 AI 추천 덱 자동 완성]을 눌러보세요!): " + string.Join(" ", errors));
            return;
        }

        _isSecurityPeekEnabled = false;
        StartGameForStage(PracticeStage.Stage3_AiBattle);
        BtnTabOfficialMatch_Click(sender, e);
        ShowToast(true, "⚔️ 현재 빌딩된 50장 덱으로 [실전 대련 (AI)] 새 게임을 시작했습니다!");
    }

    private static SolidColorBrush BrushFromHex(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private static SolidColorBrush GetCardColorBrush(CardColor color) => Theme.GetCardColorBrush(color);
}
