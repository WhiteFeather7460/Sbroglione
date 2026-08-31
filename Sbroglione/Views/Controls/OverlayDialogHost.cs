using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Sbroglione.Views.Controls;

/// <summary>
/// Presentazione di un dialogo in-app per le piattaforme single-view (Android), dove non esiste
/// una <see cref="Window"/> owner su cui chiamare <c>ShowDialog</c>.
/// Il contenuto viene montato nell'<see cref="OverlayLayer"/> del <c>TopLevel</c> corrente — lo
/// stesso layer che Avalonia usa per i propri popup/flyout, presente nel template di ogni
/// <c>TopLevel</c> e non solo di <c>Window</c> — sopra uno scrim a tutto schermo che fa da
/// modale intercettando i pointer event.
/// </summary>
public static class OverlayDialogHost
{
    /// <summary>Margine tra lo scrim e la scheda del dialogo.</summary>
    private const double CardMargin = 24;

    /// <summary>Larghezza massima della scheda del dialogo (schermi larghi/tablet).</summary>
    private const double CardMaxWidth = 560;

    /// <summary>
    /// Monta <paramref name="content"/> nell'overlay del <c>TopLevel</c> che contiene
    /// <paramref name="anchor"/> e completa il task quando il contenuto segnala il proprio esito.
    /// Se non esiste un overlay layer (view non ancora agganciata a un <c>TopLevel</c>) restituisce
    /// subito <c>default</c>, cioè lo stesso "annullato" del percorso desktop senza owner.
    /// </summary>
    public static Task<TResult> ShowAsync<TContent, TResult>(Visual anchor, TContent content)
        where TContent : Control, IDialogContent<TResult>
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);

        OverlayLayer? layer = OverlayLayer.GetOverlayLayer(anchor);
        if (layer is null)
            return Task.FromResult<TResult>(default!);

        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Center;

        var card = new Border
        {
            Child = content,
            MaxWidth = CardMaxWidth,
            Margin = new Thickness(CardMargin),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(8),
        };

        // OverlayLayer è un Canvas: i figli non vengono stirati automaticamente, quindi lo scrim
        // va dimensionato esplicitamente sui Bounds del layer e riallineato a ogni resize/rotazione.
        var scrim = new Panel
        {
            // Velo di oscuramento, non un colore di tema: è il grigio/nero semitrasparente
            // standard di una modale ed è volutamente identico in tema chiaro e scuro (i colori
            // veri del dialogo restano nel contenuto, via DynamicResource Brush.*).
            Background = new SolidColorBrush(Colors.Black, 0.5),
            Focusable = true,
        };
        scrim.Children.Add(card);

        void ApplyLayerSize()
        {
            scrim.Width = layer.Bounds.Width;
            scrim.Height = layer.Bounds.Height;
            card.MaxHeight = Math.Max(0, layer.Bounds.Height - (CardMargin * 2));
        }

        void OnLayerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.BoundsProperty)
                ApplyLayerSize();
        }

        // Lo scrim non deve lasciar passare i click al contenuto sottostante: è questo (e non
        // l'OverlayLayer, che non fornisce modalità) a rendere il dialogo modale.
        void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

        void Cancel() => Complete(default!);

        void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Esc su desktop, tasto Back su Android quando il backend lo instrada come key event:
            // annullano, esattamente come il pulsante "Annulla".
            if (e.Key is Key.Escape or Key.Back)
            {
                e.Handled = true;
                Cancel();
            }
        }

        void OnCompleted(TResult result) => Complete(result);

        void Complete(TResult result)
        {
            if (completion.Task.IsCompleted)
                return;

            content.Completed -= OnCompleted;
            scrim.KeyDown -= OnKeyDown;
            scrim.PointerPressed -= OnScrimPointerPressed;
            layer.PropertyChanged -= OnLayerPropertyChanged;
            layer.Children.Remove(scrim);

            completion.TrySetResult(result);
        }

        content.Completed += OnCompleted;
        scrim.KeyDown += OnKeyDown;
        scrim.PointerPressed += OnScrimPointerPressed;
        layer.PropertyChanged += OnLayerPropertyChanged;

        ApplyLayerSize();
        layer.Children.Add(scrim);
        scrim.Focus();

        return completion.Task;
    }
}
