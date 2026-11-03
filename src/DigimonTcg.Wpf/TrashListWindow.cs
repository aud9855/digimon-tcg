using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DigimonTcg.Core.Engine;
using DigimonTcg.Core.Models;
using DigimonTcg.Wpf.Themes;

namespace DigimonTcg.Wpf;

/// <summary>
/// v0.86.0 트래시 목록 창 — 트래시 칸을 왼쪽 클릭하면 연다 (내 트래시 · 상대 트래시 모두, 트래시는 공개 정보).
/// 맨 위(가장 나중에 트래시에 간 카드)부터 번호를 붙여 보여 주고, 카드를 누르면 오른쪽에 크게 · 효과 문구와 함께 보여 준다 (메인 창 카드 정보 칸도 같이 바꾼다).
/// 내 트래시의 [트래시]【메인】 효과는 "효과 사용" 버튼으로 쓰고, 지금 쓸 수 없으면 이유를 보여 준다.
/// </summary>
internal static class TrashListWindow
{
    /// <param name="useBlockedReason">효과를 쓸 수 없는 화면 상태 (상대 턴 재생 중 · 대상 고르는 중 등). null이면 엔진 판단만 본다</param>
    /// <param name="onUse">"효과 사용"을 누른 카드 (창을 닫은 뒤 부른다). null이면 버튼을 보이지 않는다 (상대 트래시)</param>
    /// <param name="onInspect">카드를 누르거나 마우스를 올렸을 때 (메인 창 카드 정보 칸)</param>
    public static void Show(
        Window owner, string title, IReadOnlyList<TrashCardInfo> cards, bool mine,
        Func<CardData, double, double, FrameworkElement> cardVisual,
        string? useBlockedReason, Action<CardData>? onUse, Action<CardData>? onInspect)
    {
        CardData? toUse = null;
        Window? window = null;

        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- 머리글: 장수 · 순서 안내 ----
        int usable = cards.Count(c => c.CanUse);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = $"{(mine ? "내" : "상대")} 트래시 · {cards.Count}장",
            Foreground = Brush("Neo.Brush.Gold", Brushes.Gold),
            FontSize = 18,
            FontWeight = FontWeights.ExtraBold,
        });
        header.Children.Add(new TextBlock
        {
            Text = cards.Count == 0 ? "트래시에 카드가 없습니다." : "① = 맨 위 (가장 최근에 트래시에 간 카드) → 번호가 클수록 먼저 간 카드입니다. 카드를 누르면 오른쪽에 자세히 보입니다.",
            Foreground = Brush("Neo.Brush.Text.Secondary", Brushes.LightGray),
            FontSize = 12.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        if (mine && cards.Any(c => c.HasTrashMain))
            header.Children.Add(new TextBlock
            {
                Text = usable > 0 ? $"⚡ 지금 쓸 수 있는 [트래시]【메인】 효과: {usable}장 — 카드를 고르고 \"효과 사용\"을 누르세요."
                                  : useBlockedReason ?? "[트래시]【메인】 효과를 가진 카드가 있지만 지금은 쓸 수 없습니다 (카드를 누르면 이유가 보입니다).",
                Foreground = usable > 0 && useBlockedReason == null ? Brush("Neo.Brush.Gold", Brushes.Gold) : Brush("Neo.Brush.Text.Secondary", Brushes.LightGray),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        // ---- 오른쪽: 고른 카드 자세히 ----
        var detail = new StackPanel();
        var detailBorder = new Border
        {
            Child = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x30, 0x40, 0x60)),
            BorderBrush = Brush("Neo.Brush.Gold", Brushes.Gold),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetRow(detailBorder, 1);
        Grid.SetColumn(detailBorder, 1);
        root.Children.Add(detailBorder);

        var tiles = new Dictionary<TrashCardInfo, Border>();
        TrashCardInfo? selected = null;

        void ShowDetail(TrashCardInfo info)
        {
            selected = info;
            foreach (var (k, b) in tiles)
            {
                bool on = ReferenceEquals(k, info);
                b.BorderBrush = on ? Brush("Neo.Brush.Gold", Brushes.Gold) : new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                b.BorderThickness = new Thickness(on ? 3 : 1);
            }
            detail.Children.Clear();
            var card = info.Card;
            var big = cardVisual(card, 220, 307);
            big.HorizontalAlignment = HorizontalAlignment.Center;
            detail.Children.Add(big);
            detail.Children.Add(Text($"{Circled(info.Position + 1)} {(info.Position == 0 ? "맨 위 · " : "")}[{card.CardId}] {card.DisplayName}", 15, FontWeights.ExtraBold, Brushes.White, top: 10));
            string stats = card.Type == CardType.Digimon || card.Type == CardType.Digitama
                ? $"Lv.{card.Level} {KoreanLabels.Colors(card)} {KoreanLabels.Type(card.Type)} · 등장 {card.PlayCost} · DP {card.DP:N0}"
                : $"{KoreanLabels.Colors(card)} {KoreanLabels.Type(card.Type)} · 코스트 {card.PlayCost}";
            detail.Children.Add(Text(stats, 12, FontWeights.Normal, Brush("Neo.Brush.Text.Secondary", Brushes.LightGray), top: 2));

            if (mine && info.HasTrashMain)
            {
                string? why = info.CanUse ? useBlockedReason : info.Reason;
                if (onUse != null)
                {
                    var use = MakeButton(info.CanUse && why == null ? "⚡ 효과 사용" : "효과 사용 (지금은 불가)", highlighted: info.CanUse && why == null);
                    use.IsEnabled = info.CanUse && why == null;
                    use.Opacity = use.IsEnabled ? 1.0 : 0.5;
                    use.HorizontalAlignment = HorizontalAlignment.Stretch;
                    use.Margin = new Thickness(0, 10, 0, 0);
                    use.Click += (_, _) => { toUse = card; window?.Close(); };
                    detail.Children.Add(use);
                }
                if (why != null)
                    detail.Children.Add(Text("쓸 수 없는 이유: " + why, 12, FontWeights.SemiBold, Brush("Neo.Brush.Rose", Brushes.Salmon), top: 6));
            }
            var trashLines = TrashLines(card.DisplayEffectText);
            if (trashLines.Length > 0)
            {
                detail.Children.Add(Text(info.HasTrashMain ? "[트래시] 효과 — 트래시에 있을 때만" : "[트래시] 효과 — 트래시에 있으면 조건이 맞을 때 자동으로 묻습니다", 12, FontWeights.Bold, Brush("Neo.Brush.Cyan", Brushes.SkyBlue), top: 10));
                detail.Children.Add(Text(trashLines, 12.5, FontWeights.Normal, Brushes.White, top: 2));
            }
            detail.Children.Add(Text("효과", 12, FontWeights.Bold, Brush("Neo.Brush.Gold", Brushes.Gold), top: 10));
            detail.Children.Add(Text(string.IsNullOrWhiteSpace(card.DisplayEffectText) ? "없음" : card.DisplayEffectText, 12.5, FontWeights.Normal, Brushes.White, top: 2));
            if (!string.IsNullOrWhiteSpace(card.DisplayInheritedEffectText))
            {
                detail.Children.Add(Text(card.IsDual ? "[옵션 쪽]" : "[진화원 효과]", 12, FontWeights.Bold, Brush("Neo.Brush.Gold", Brushes.Gold), top: 10));
                detail.Children.Add(Text(card.DisplayInheritedEffectText, 12.5, FontWeights.Normal, Brushes.White, top: 2));
            }
            onInspect?.Invoke(card);
        }

        // ---- 왼쪽: 카드 목록 (맨 위부터) ----
        FrameworkElement listContent;
        if (cards.Count == 0)
        {
            listContent = new TextBlock
            {
                Text = "트래시가 비어 있습니다.",
                Foreground = Brush("Neo.Brush.Text.Secondary", Brushes.LightGray),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 60, 0, 60),
            };
            detail.Children.Add(Text("카드가 없습니다.", 13, FontWeights.Normal, Brush("Neo.Brush.Text.Secondary", Brushes.LightGray), top: 0));
        }
        else
        {
            var wrap = new WrapPanel();
            foreach (var info in cards)
            {
                var content = new StackPanel { Width = 104 };
                // 순서 (카드 그림의 코스트 표시를 가리지 않게 그림 위 줄에)
                content.Children.Add(new TextBlock
                {
                    Text = info.Position == 0 ? "① 맨 위 (최근)" : Circled(info.Position + 1),
                    Foreground = info.Position == 0 ? Brush("Neo.Brush.Gold", Brushes.Gold) : Brush("Neo.Brush.Text.Secondary", Brushes.LightGray),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 11.5,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3),
                });
                var pic = cardVisual(info.Card, 96, 134);
                pic.HorizontalAlignment = HorizontalAlignment.Center;
                content.Children.Add(pic);
                content.Children.Add(new TextBlock
                {
                    Text = info.Card.DisplayName,
                    Foreground = Brushes.White,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                content.Children.Add(new TextBlock { Text = info.Card.CardId, Foreground = Brush("Neo.Brush.Text.Secondary", Brushes.LightGray), FontSize = 10, TextAlignment = TextAlignment.Center });
                if (info.HasTrashMain)
                {
                    bool can = mine && info.CanUse && useBlockedReason == null;
                    content.Children.Add(Badge(can ? "⚡ 【메인】 사용 가능" : "[트래시]【메인】", can ? Brush("Neo.Brush.Gold", Brushes.Gold) : Brush("Neo.Brush.Text.Secondary", Brushes.LightGray)));
                }
                else if (info.HasTrashTiming)
                    content.Children.Add(Badge("[트래시] 자동 효과", Brush("Neo.Brush.Cyan", Brushes.SkyBlue)));

                var tile = new Border
                {
                    Child = content,
                    Padding = new Thickness(5),
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(0x30, 0x30, 0x40, 0x60)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{Circled(info.Position + 1)} [{info.Card.CardId}] {info.Card.DisplayName}",
                };
                var captured = info;
                tile.MouseLeftButtonUp += (_, _) => ShowDetail(captured);
                tile.MouseEnter += (_, _) => onInspect?.Invoke(captured.Card);
                tiles[info] = tile;
                wrap.Children.Add(tile);
            }
            listContent = wrap;
        }
        var listScroll = new ScrollViewer { Content = listContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(listScroll, 1);
        root.Children.Add(listScroll);

        // ---- 아래: 닫기 ----
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var close = MakeButton("닫기", highlighted: false);
        close.Click += (_, _) => window?.Close();
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        // 처음에는 쓸 수 있는 카드 → [트래시]【메인】 카드 → 맨 위 카드를 보여 준다
        var first = cards.FirstOrDefault(c => mine && c.CanUse) ?? cards.FirstOrDefault(c => mine && c.HasTrashMain) ?? cards.FirstOrDefault();
        if (first != null)
            ShowDetail(first);

        double workH = SystemParameters.WorkArea.Height, workW = SystemParameters.WorkArea.Width;
        window = new Window
        {
            Owner = owner,
            Title = title,
            Content = root,
            Width = Math.Min(1120, workW - 60),
            Height = Math.Min(760, workH - 60),
            MinWidth = 720,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x22)),
        };
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        window.ShowDialog();
        if (toUse != null)
            onUse?.Invoke(toUse);
    }

    /// <summary>효과 문구 중 [트래시] 줄 (공식 한글 표기 · 영어 표기 모두)</summary>
    private static string TrashLines(string text) =>
        string.Join("\n", (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => Regex.IsMatch(l, @"^(?:\[Trash\]|\[트래시\]|【트래시】)", RegexOptions.IgnoreCase)));

    private static string Circled(int n) => n is >= 1 and <= 20 ? ((char)('①' + n - 1)).ToString() : $"#{n}";

    private static TextBlock Text(string text, double size, FontWeight weight, Brush color, double top) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, top, 0, 0),
    };

    private static Border Badge(string text, Brush color) => new()
    {
        BorderBrush = color,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(5, 1, 5, 1),
        Margin = new Thickness(0, 4, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = color, FontSize = 10, FontWeight = FontWeights.Bold },
    };

    private static Brush Brush(string key, Brush fallback)
    {
        try { return Theme.GetBrush(key); }
        catch { return fallback; }
    }

    private static Button MakeButton(string text, bool highlighted) => new()
    {
        Content = new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = highlighted ? FontWeights.Bold : FontWeights.Normal },
        Padding = new Thickness(14, 6, 14, 6),
        Margin = new Thickness(8, 0, 0, 0),
        MinWidth = 90,
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0x30, 0x40, 0x60)),
        BorderBrush = Brush(highlighted ? "Neo.Brush.Gold" : "Neo.Brush.Cyan", highlighted ? Brushes.Gold : Brushes.SteelBlue),
        BorderThickness = new Thickness(highlighted ? 2 : 1),
        Cursor = Cursors.Hand,
    };
}
