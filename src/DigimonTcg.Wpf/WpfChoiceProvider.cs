using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;
using DigimonTcg.Wpf.Themes;

namespace DigimonTcg.Wpf;

/// <summary>
/// 카드 효과 처리 중 플레이어에게 묻는 창 (효과 확장 4단계):
/// "~할 수 있다" 확인, 효과 대상 고르기(카드 그림으로), 고르는 효과의 선택지. 언제나 추천(AI 평가) 답을 표시한다.
/// </summary>
public sealed class WpfChoiceProvider : IChoiceProvider
{
    private readonly Window _owner;
    private readonly Func<CardData, FrameworkElement>? _cardVisual;

    /// <param name="cardVisual">대상 고르기 창에 보여 줄 카드 그림 (없으면 이름 · DP만)</param>
    public WpfChoiceProvider(Window owner, Func<CardData, FrameworkElement>? cardVisual = null)
    {
        _owner = owner;
        _cardVisual = cardVisual;
    }

    public bool Confirm(GameState state, CardInstance? subject, string question, bool recommended)
    {
        bool? result = null;
        var (window, body) = CreateWindow("카드 효과", question);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(MakeButton(recommended ? "예 (추천)" : "예", recommended, () => { result = true; window.Close(); }));
        buttons.Children.Add(MakeButton(!recommended ? "아니요 (추천)" : "아니요", !recommended, () => { result = false; window.Close(); }));
        body.Children.Add(buttons);
        window.ShowDialog();
        return result ?? recommended;
    }

    public CardInstance? ChooseDigimon(GameState state, string question, IReadOnlyList<CardInstance> options, CardInstance? recommended, bool optional)
    {
        CardInstance? result = null;
        bool chosen = false;
        var (window, body) = CreateWindow("효과 대상 고르기", question);

        var cards = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 860 };
        foreach (var option in options)
        {
            bool mine = state.Player.BattleArea.Contains(option);
            var content = new StackPanel { Width = 120 };
            if (_cardVisual != null)
                content.Children.Add(_cardVisual(option.TopCard));
            content.Children.Add(new TextBlock
            {
                Text = $"{(mine ? "내" : "상대")} {option.TopCard.DisplayName}\n{DigimonStats(option)}"
                       + (ReferenceEquals(option, recommended) ? "\n★ 추천" : ""),
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
            });
            var pick = option;
            var button = MakeButton(content, ReferenceEquals(option, recommended), () => { result = pick; chosen = true; window.Close(); });
            button.Margin = new Thickness(6);
            button.Padding = new Thickness(6);
            cards.Children.Add(button);
        }
        body.Children.Add(cards);

        if (optional)
        {
            var skip = MakeButton("고르지 않음", false, () => { result = null; chosen = true; window.Close(); });
            skip.HorizontalAlignment = HorizontalAlignment.Right;
            skip.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(skip);
        }
        window.ShowDialog();
        return chosen ? result : optional ? null : recommended;
    }

    public int ChooseOption(GameState state, string question, IReadOnlyList<string> options, int recommended)
    {
        int result = recommended;
        var (window, body) = CreateWindow("효과 고르기", question);
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = MakeButton(new TextBlock
            {
                Text = $"{i + 1}. {options[i]}" + (i == recommended ? "   ★ 추천" : ""),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 620,
                Foreground = Brushes.White,
            }, i == recommended, () => { result = index; window.Close(); });
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(button);
        }
        window.ShowDialog();
        return result;
    }

    /// <summary>효과 대상 디지몬 여러 마리를 한 번에 고르는 창</summary>
    public IReadOnlyList<CardInstance> ChooseDigimons(GameState state, string question, IReadOnlyList<CardInstance> options,
        int min, int max, IReadOnlyList<CardInstance> recommended)
    {
        var items = options.Select(o => new MultiCardPicker.Item(o, o.TopCard,
            $"{(state.Player.BattleArea.Contains(o) ? "내" : "상대")} {o.TopCard.DisplayName}\n{DigimonStats(o)}")).ToList();
        var picked = MultiCardPicker.Show(_owner, "효과 대상 고르기", question, items, min, max, recommended.Cast<object>().ToList(), _cardVisual);
        return picked?.Cast<CardInstance>().ToList() ?? recommended;
    }

    /// <summary>패 · 트래시 · 진화원의 카드를 고르는 창 (같은 카드 여러 장은 번호로 구분)</summary>
    public IReadOnlyList<int> ChooseCards(GameState state, string question, CardChoiceZone zone, IReadOnlyList<CardData> options,
        int min, int max, IReadOnlyList<int> recommended)
    {
        string where = zone switch
        {
            CardChoiceZone.Hand => "패",
            CardChoiceZone.Trash => "트래시",
            CardChoiceZone.HandOrTrash => "패 · 트래시",
            CardChoiceZone.DigivolutionCards => "진화원",
            _ => "카드"
        };
        // 번호마다 새 객체를 키로 써서 같은 카드 여러 장도 따로 고를 수 있게 한다
        var keys = options.Select((_, i) => (object)new IndexKey(i)).ToList();
        // v0.35.0 뒷면 카드 (상대의 뒷면 진화원 · 테이머 아래): 내용을 가리고 실물 카드 뒷면으로 (종합 룰 4-7-10 — 주인만 볼 수 있다). 자신의 뒷면 카드는 "🂠뒷면" 표시
        var hidden = new HashSet<CardData>(options.Where(c => IsOpponentFaceDown(state, c)), ReferenceEqualityComparer.Instance);
        var items = options.Select((card, i) => new MultiCardPicker.Item(keys[i], card,
            hidden.Contains(card) ? "🂠 뒷면 카드"
            : $"{(IsOwnFaceDown(state, card) ? "🂠뒷면 · " : "")}{card.DisplayName}\n{(card.Level > 0 ? $"Lv.{card.Level} " : "")}{KoreanLabels.Type(card.Type)}{(card.Type == CardType.Digimon ? $"\nDP {card.DP:N0}" : "")}")).ToList();
        var recommendedKeys = recommended.Where(i => i >= 0 && i < keys.Count).Select(i => keys[i]).ToList();
        // 여러 곳(패 · 필드 등)에서 모은 후보(Other)는 장소를 붙이지 않는다
        string title = zone == CardChoiceZone.Other ? "카드 고르기" : $"{where}에서 카드 고르기";
        Func<CardData, FrameworkElement>? visual = _cardVisual == null ? null : card => hidden.Contains(card) ? CardBack() : _cardVisual(card);
        var picked = MultiCardPicker.Show(_owner, title, question, items, min, max, recommendedKeys, visual);
        return picked?.Cast<IndexKey>().Select(k => k.Index).ToList() ?? recommended;
    }

    private static bool IsOpponentFaceDown(GameState state, CardData card) =>
        state.DummyOpponent.BattleArea.Any(d => d.IsFaceDown(card) && d.EvolutionSources.Any(x => ReferenceEquals(x, card)))
        || (state.DummyOpponent.IsFaceDownUnderTamer(card) && state.DummyOpponent.UnderTamers.Any(x => ReferenceEquals(x, card)));

    private static bool IsOwnFaceDown(GameState state, CardData card) =>
        state.Player.BattleArea.Append(state.Player.BreedingArea).Any(d => d != null && d.IsFaceDown(card) && d.EvolutionSources.Any(x => ReferenceEquals(x, card)))
        || (state.Player.IsFaceDownUnderTamer(card) && state.Player.UnderTamers.Any(x => ReferenceEquals(x, card)));

    /// <summary>실물 TCG 카드 뒷면 (Duel.Brush.CardBackArt — data/images/_card_back.jpg가 있으면 그 그림)</summary>
    private static FrameworkElement CardBack()
    {
        Brush back;
        try { back = Theme.GetBrush("Duel.Brush.CardBackArt"); } catch { back = Brushes.DarkSlateBlue; }
        return new Border { Width = 108, Height = 151, CornerRadius = new CornerRadius(6), Background = back, BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1) };
    }

    private sealed record IndexKey(int Index);

    /// <summary>카드 아래 설명 (좁은 칸에서 어색하게 끊기지 않도록 줄을 나눈다)</summary>
    private static string DigimonStats(CardInstance d) =>
        $"Lv.{d.TopCard.Level} · DP {d.GetCurrentDP():N0}" + (d.IsSuspended ? "\n레스트 상태" : "");

    // =====================================================================
    // 창 · 버튼
    // =====================================================================

    private (Window Window, StackPanel Body) CreateWindow(string title, string question)
    {
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock
        {
            Text = question,
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 640,
        });
        var window = new Window
        {
            Owner = _owner,
            Title = title,
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 80),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x22)),
        };
        return (window, body);
    }

    private static Button MakeButton(object content, bool highlighted, Action onClick)
    {
        Brush accent;
        try { accent = Theme.GetBrush(highlighted ? "Neo.Brush.Gold" : "Neo.Brush.Cyan"); }
        catch { accent = highlighted ? Brushes.Gold : Brushes.SteelBlue; }

        var button = new Button
        {
            Content = content is string text ? new TextBlock { Text = text, Foreground = Brushes.White } : content,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 90,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x30, 0x40, 0x60)),
            BorderBrush = accent,
            BorderThickness = new Thickness(highlighted ? 2 : 1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}
