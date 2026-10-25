using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DigimonTcg.Core.Layout;
using DigimonTcg.Core.Models;
using DigimonTcg.Wpf.Themes;

namespace DigimonTcg.Wpf;

/// <summary>
/// 필드의 테이머 1장. 같은 카드 객체가 테이머 목록에 여러 번 들어 있을 수 있어 "몇 번째 장"으로 구분한다.
/// </summary>
internal sealed class TamerKey : IEquatable<TamerKey>
{
    public TamerKey(CardData card, int ordinal)
    {
        Card = card;
        Ordinal = ordinal;
    }

    public CardData Card { get; }
    public int Ordinal { get; }

    public bool Equals(TamerKey? other) => other != null && ReferenceEquals(Card, other.Card) && Ordinal == other.Ordinal;
    public override bool Equals(object? obj) => Equals(obj as TamerKey);
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Card), Ordinal);
}

/// <summary>필드 위치 키: 디지몬은 CardInstance 참조, 테이머는 TamerKey 값으로 비교</summary>
internal sealed class FieldKeyComparer : IEqualityComparer<object>
{
    public static readonly FieldKeyComparer Instance = new();

    public new bool Equals(object? x, object? y) =>
        x is TamerKey tx ? tx.Equals(y as TamerKey) : ReferenceEquals(x, y);

    public int GetHashCode(object obj) =>
        obj is TamerKey t ? t.GetHashCode() : RuntimeHelpers.GetHashCode(obj);
}

/// <summary>
/// 자유 배치 필드 — 배틀 에어리어의 디지몬 · 테이머를 원하는 자리에 끌어다 놓는다.
/// 위치는 화면에만 있는 정보다 (게임 상태 · 저장 파일에 들어가지 않는다). 카드가 필드를 떠나면 위치도 지운다.
/// 진화해도 같은 CardInstance라 자리를 지킨다.
/// </summary>
public partial class MainWindow
{
    private const double TamerArtWidth = 88;
    private const double TamerArtHeight = 120;
    private const double OpponentTamerArtWidth = 72;
    private const double OpponentTamerArtHeight = 98;

    private readonly Dictionary<object, Point> _myFieldPositions = new(FieldKeyComparer.Instance);
    private readonly Dictionary<object, Point> _oppFieldPositions = new(FieldKeyComparer.Instance);

    /// <summary>손패 · 육성 디지몬을 배틀 에어리어 빈 곳에 놓았을 때 그 자리 (캔버스 좌표, 새로 나온 카드의 가운데가 온다)</summary>
    private Point? _pendingFieldDropPoint;

    // 필드 카드 옮기기 드래그: 잡은 테이머 · 카드 안에서 잡은 자리 · 잡은 카드 크기 · 놓일 자리 미리보기
    private TamerKey? _potentialDragFieldTamer;
    private Point _dragGrabOffset;
    private Size _dragSourceSize;
    private Border? _fieldMovePreview;

    private record struct FieldItem(object Key, UIElement Element, bool IsTamer);

    /// <summary>테이머 목록을 장마다 키로 (같은 카드 객체는 0, 1, 2 … 번째 장)</summary>
    private static List<TamerKey> TamerKeysOf(PlayerZones zones)
    {
        var keys = new List<TamerKey>(zones.Tamers.Count);
        var seen = new Dictionary<CardData, int>(ReferenceEqualityComparer.Instance);
        foreach (var tamer in zones.Tamers)
        {
            seen.TryGetValue(tamer, out int n);
            keys.Add(new TamerKey(tamer, n));
            seen[tamer] = n + 1;
        }
        return keys;
    }

    /// <summary>레스트된 장 수만큼 앞쪽 장부터 레스트로 보여 준다 (엔진은 같은 카드 객체를 장 수로 센다)</summary>
    private static bool IsTamerRested(PlayerZones zones, TamerKey key) =>
        key.Ordinal < zones.SuspendedTamers.Count(t => ReferenceEquals(t, key.Card));

    /// <summary>
    /// 캔버스에 카드를 놓는다. 저장된 자리가 있는 카드는 그 자리(겹치면 가장 가까운 빈 자리로 밀림),
    /// 새 카드는 놓은 자리(dropCenter) 또는 자동 정렬 격자 자리 근처의 빈 자리에 놓는다.
    /// 디지몬은 한쪽 끝부터, 테이머는 반대쪽 끝부터 채운다 (mirrored = 상대 필드 거울 배치).
    /// </summary>
    /// <returns>dropCenter를 새 카드에 썼는지</returns>
    private bool LayoutFieldCanvas(
        Canvas canvas,
        ScrollViewer scroll,
        IReadOnlyList<FieldItem> items,
        Dictionary<object, Point> positions,
        bool mirrored,
        double guideHeight,
        bool contentDrivenHeight,
        Point? dropCenter)
    {
        canvas.Children.Clear();

        // 스크롤바가 생기고 없어지며 자리가 흔들리지 않도록 뷰포트가 아니라 스크롤 영역 전체 크기를 기준으로 한다
        double viewportWidth = Math.Max(0, scroll.ActualWidth - 2);
        bool persist = viewportWidth > 0;   // 아직 화면에 배치되기 전이면 임시 자리만 쓰고 저장하지 않는다
        double areaWidth = persist ? viewportWidth : 900;

        var sizes = new List<Size>(items.Count);
        foreach (var item in items)
        {
            canvas.Children.Add(item.Element);
            item.Element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            sizes.Add(item.Element.DesiredSize);
        }

        double tallest = sizes.Count == 0 ? 0 : sizes.Max(s => s.Height);
        double areaHeight = contentDrivenHeight
            ? Math.Max(guideHeight, tallest)
            : Math.Max(scroll.ActualHeight - 2, Math.Min(guideHeight, tallest));
        if (areaHeight <= 0)
            areaHeight = Math.Max(guideHeight, tallest);

        // 필드를 떠난 카드의 자리는 지운다 (다시 나오면 새 카드로 자동 배치)
        var liveKeys = new HashSet<object>(items.Select(i => i.Key), FieldKeyComparer.Instance);
        foreach (var stale in positions.Keys.Where(k => !liveKeys.Contains(k)).ToList())
            positions.Remove(stale);

        // 자동 정렬 격자 자리 (새 카드가 들어갈 기본 자리)
        var gridSlots = new FieldRect[items.Count];
        var digimonIdx = Enumerable.Range(0, items.Count).Where(i => !items[i].IsTamer).ToList();
        var tamerIdx = Enumerable.Range(0, items.Count).Where(i => items[i].IsTamer).ToList();
        var digimonFlow = FieldLayout.Flow(digimonIdx.Select(i => (sizes[i].Width, sizes[i].Height)).ToList(), areaWidth, fromRight: mirrored);
        var tamerFlow = FieldLayout.Flow(tamerIdx.Select(i => (sizes[i].Width, sizes[i].Height)).ToList(), areaWidth, fromRight: !mirrored);
        for (int k = 0; k < digimonIdx.Count; k++) gridSlots[digimonIdx[k]] = digimonFlow[k];
        for (int k = 0; k < tamerIdx.Count; k++) gridSlots[tamerIdx[k]] = tamerFlow[k];

        var placed = new FieldRect[items.Count];
        var settled = new List<FieldRect>(items.Count);

        // 1) 자리가 있는 카드: 제자리 (레스트 등으로 커져 겹치면 밀어냄)
        for (int i = 0; i < items.Count; i++)
        {
            if (!positions.TryGetValue(items[i].Key, out var pos))
                continue;
            var rect = new FieldRect(pos.X, pos.Y, sizes[i].Width, sizes[i].Height);
            placed[i] = FieldLayout.FindFreeSpot(rect, settled, areaWidth, Math.Max(areaHeight, rect.Bottom));
            settled.Add(placed[i]);
        }

        // 2) 새 카드: 놓은 자리 또는 격자 자리 근처의 빈 자리
        bool dropUsed = false;
        for (int i = 0; i < items.Count; i++)
        {
            if (positions.ContainsKey(items[i].Key))
                continue;
            var desired = gridSlots[i];
            if (dropCenter is { } center && !dropUsed)
            {
                desired = desired.MoveTo(center.X - sizes[i].Width / 2, center.Y - sizes[i].Height / 2);
                dropUsed = true;
            }
            placed[i] = FieldLayout.FindFreeSpot(desired, settled, areaWidth, areaHeight);
            settled.Add(placed[i]);
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (persist || positions.ContainsKey(items[i].Key))
                positions[items[i].Key] = new Point(placed[i].X, placed[i].Y);
            Canvas.SetLeft(items[i].Element, placed[i].X);
            Canvas.SetTop(items[i].Element, placed[i].Y);
        }

        // 자유 배치 필드라 고정 칸(점선 슬롯)은 그리지 않는다 — 자동 정렬 격자는 보이지 않는 기준으로만 쓴다

        double maxRight = settled.Count == 0 ? 0 : settled.Max(r => r.Right);
        double maxBottom = settled.Count == 0 ? 0 : settled.Max(r => r.Bottom);
        // 캔버스는 카드가 놓인 만큼만 (필드보다 넓거나 길게 놓였을 때만 스크롤)
        canvas.Width = maxRight;
        canvas.Height = contentDrivenHeight ? Math.Max(areaHeight, maxBottom) : maxBottom;
        return dropUsed;
    }

    /// <summary>[▦ 자동 정렬] — 옮긴 자리를 모두 지우고 격자로 다시 놓는다</summary>
    private void BtnAutoArrangeField_Click(object sender, RoutedEventArgs e)
    {
        _myFieldPositions.Clear();
        _oppFieldPositions.Clear();
        ShowToast(true, "▦ 필드를 자동 정렬했습니다. 디지몬은 왼쪽부터, 테이머는 오른쪽 끝부터 놓입니다. (카드를 끌어 내 배틀 에어리어 안에 놓으면 다시 옮길 수 있습니다)");
        RenderOpponentBattleArea();
        RenderBattleArea();
    }

    /// <summary>필드 크기가 바뀌면 (창 크기 · 옵션 줄 표시) 자리를 다시 맞춘다</summary>
    private void FieldScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isDragging || SimulatorView.Visibility != Visibility.Visible || _gameState == null)
            return;
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1 && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 1)
            return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isDragging)
                return;
            if (ReferenceEquals(sender, BattleAreaScroll))
                RenderBattleArea();
            else
                RenderOpponentBattleArea();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>창 좌표 → 내 배틀 에어리어 캔버스 좌표</summary>
    private Point ToMyFieldCanvas(Point windowPos) => TranslatePoint(windowPos, BattleAreaPanel);

    /// <summary>필드 카드 옮기기 드래그를 시작할 준비 (카드 안에서 잡은 자리 · 카드 크기 기억)</summary>
    private void BeginPotentialFieldMove(FrameworkElement source, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragGrabOffset = e.GetPosition(source);
        _dragSourceSize = new Size(source.ActualWidth, source.ActualHeight);
    }

    private object? DraggedFieldKey => (object?)_potentialDragFieldAttacker ?? _potentialDragFieldTamer;

    /// <summary>옮기는 카드를 지금 놓으면 들어갈 자리 (다른 카드와 겹치면 가장 가까운 빈 자리)</summary>
    private FieldRect? FieldMoveTarget(Point windowPos)
    {
        var key = DraggedFieldKey;
        if (key == null || _dragSourceSize.Width <= 0)
            return null;
        var p = ToMyFieldCanvas(windowPos);
        var desired = new FieldRect(p.X - _dragGrabOffset.X, p.Y - _dragGrabOffset.Y, _dragSourceSize.Width, _dragSourceSize.Height);
        var others = new List<FieldRect>();
        foreach (var child in BattleAreaPanel.Children.OfType<FrameworkElement>())
        {
            if (child.Tag == null || FieldKeyComparer.Instance.Equals(child.Tag, key))
                continue;
            others.Add(new FieldRect(Canvas.GetLeft(child), Canvas.GetTop(child), child.ActualWidth, child.ActualHeight));
        }
        double width = Math.Max(desired.Width, BattleAreaScroll.ActualWidth - 2);
        double height = Math.Max(BattleAreaScroll.ActualHeight - 2, desired.Height);
        return FieldLayout.FindFreeSpot(desired, others, width, height);
    }

    /// <summary>옮길 자리 미리보기 (점선 칸)</summary>
    private void ShowFieldMovePreview(FieldRect? target)
    {
        if (target is not { } rect)
        {
            HideFieldMovePreview();
            return;
        }
        if (_fieldMovePreview == null)
        {
            _fieldMovePreview = new Border
            {
                BorderBrush = Theme.GetBrush("Neo.Brush.Cyan"),
                BorderThickness = new Thickness(2),
                Background = BrushFromHex("#2238BDF8"),
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_fieldMovePreview, 50);
        }
        if (!BattleAreaPanel.Children.Contains(_fieldMovePreview))
            BattleAreaPanel.Children.Add(_fieldMovePreview);
        _fieldMovePreview.Width = rect.Width;
        _fieldMovePreview.Height = rect.Height;
        Canvas.SetLeft(_fieldMovePreview, rect.X);
        Canvas.SetTop(_fieldMovePreview, rect.Y);
    }

    private void HideFieldMovePreview()
    {
        if (_fieldMovePreview != null)
            BattleAreaPanel.Children.Remove(_fieldMovePreview);
    }

    /// <summary>필드 카드를 놓은 자리로 옮긴다 (게임 상태는 바뀌지 않는다)</summary>
    private bool TryCompleteFieldMove(object key, Point windowPos)
    {
        if (FieldMoveTarget(windowPos) is not { } target)
            return false;
        _myFieldPositions[key] = new Point(target.X, target.Y);
        return true;
    }

    /// <summary>손패 · 육성 디지몬을 내 배틀 에어리어의 빈 곳에 놓았으면 새 카드가 나올 자리를 기억</summary>
    private void RememberFieldDropPoint(Point windowPos, bool overBattleZone, bool onCard)
    {
        _pendingFieldDropPoint = overBattleZone && !onCard ? ToMyFieldCanvas(windowPos) : null;
    }

    // ==================== 🎖️ 필드 테이머 카드 (디지몬처럼 한 장씩, 레스트면 가로로 눕힘) ====================

    private FrameworkElement CreateFieldTamerWidget(TamerKey key, PlayerZones zones, bool isOpponent)
    {
        var tamer = key.Card;
        bool rested = IsTamerRested(zones, key);
        int sameIdCount = zones.Tamers.Count(t => t.CardId == tamer.CardId);
        bool canUseMain = !isOpponent && _gameState.CurrentPhase == Phase.Main
                          && _engine.GetMainAbilities(_gameState.Player).Any(a => a.Digimon == null && ReferenceEquals(a.Card, tamer));
        double artW = isOpponent ? OpponentTamerArtWidth : TamerArtWidth;
        double artH = isOpponent ? OpponentTamerArtHeight : TamerArtHeight;

        var stack = new StackPanel();
        // 상태 한 줄 (카드 오른쪽 위 배지에 이미 TAMER가 있다)
        stack.Children.Add(new TextBlock
        {
            Text = rested ? "↻ REST" : "⚡ ACTIVE",
            Foreground = BrushFromHex(rested ? "#FAB387" : "#A6E3A1"),
            FontWeight = FontWeights.ExtraBold,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 3)
        });

        var art = CreateCardVisualFrame(tamer, width: artW, height: artH, customDpOrBadge: "TAMER", isRested: rested);
        art.HorizontalAlignment = HorizontalAlignment.Center;
        if (rested)
        {
            // 디지몬처럼 레스트 = 가로로 눕힌다 (그래서 레스트 테이머는 칸이 더 넓다)
            art.LayoutTransform = new RotateTransform(90);
            art.Margin = new Thickness(0, 2, 0, 2);
        }
        stack.Children.Add(art);

        // 세워 둔 카드는 리본에 이름이 보이니, 눕혀서 리본이 옆으로 돈 레스트 카드에만 이름을 한 줄 더
        if (rested)
        stack.Children.Add(new TextBlock
        {
            Text = tamer.DisplayName,
            Foreground = BrushFromHex("#F8FAFC"),
            FontWeight = FontWeights.Bold,
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = (rested ? artH : artW) + 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        });

        if (canUseMain)
        {
            stack.Children.Add(new Border
            {
                Background = BrushFromHex("#F9E2AF"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "✦ 클릭: [메인] 효과",
                    Foreground = BrushFromHex("#11111B"),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 9.5
                }
            });
        }

        string auraDesc = GetHandCardQuickEffectSummary(tamer).Text;
        var scale = new ScaleTransform(1, 1);
        var border = new Border
        {
            Tag = key,
            Width = (rested ? artH : artW) + 16,
            Background = BrushFromHex(isOpponent ? "#141522" : "#C00B1020"),
            BorderBrush = GetCardColorBrush(tamer.Color),
            BorderThickness = new Thickness(canUseMain ? 2 : 1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 5, 7, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            ToolTip = isOpponent
                ? $"상대 테이머 [{tamer.DisplayName}]{(rested ? " — 레스트" : "")}\n{auraDesc}"
                : $"🎖️ [{tamer.DisplayName}]{(rested ? " — 레스트" : "")}\n{auraDesc}\n\n"
                  + (canUseMain ? "클릭: [메인] 효과 쓰기 · " : "클릭: 지속 효과 확인 · ")
                  + "끌어서 내 배틀 에어리어 안에 놓으면 자리 이동",
            Child = stack
        };

        border.MouseEnter += (_, _) =>
        {
            UpdateCardInspector(tamer);
            if (!isOpponent)
                UpdateTamerBreakdownOnSidebar(tamer, sameIdCount);
            Panel.SetZIndex(border, 10);
            var anim = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(120));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        };
        border.MouseLeave += (_, _) =>
        {
            Panel.SetZIndex(border, 0);
            var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        };

        if (isOpponent)
            return border;

        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_gameState.IsGameOver || _pendingHandCard != null)
                return;
            _dragStartPoint = e.GetPosition(this);
            _potentialDragHandCard = null;
            _potentialDragFieldAttacker = null;
            _potentialDragBreedingDigimon = null;
            _potentialDragFieldTamer = key;
            BeginPotentialFieldMove(border, e);
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            UpdateCardInspector(tamer);
            UpdateTamerBreakdownOnSidebar(tamer, sameIdCount);
            // [메인] 기동 효과가 있고 지금 쓸 수 있으면 사용, 아니면 지속 효과 안내
            if (_engine.GetMainAbilities(_gameState.Player).Any(a => a.Digimon == null && ReferenceEquals(a.Card, tamer)))
                TryActivateMainAbility(null, tamer);
            else
                ShowToast(true, $"🎖️ 테이머 [{tamer.DisplayName}]{(rested ? " (레스트)" : "")} x{sameIdCount}장 활성 중: {auraDesc}");
        };
        border.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            TryActivateMainAbility(null, tamer);
        };
        return border;
    }
}
