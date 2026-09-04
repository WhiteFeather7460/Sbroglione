using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Sbroglione.Views.Controls;

namespace Sbroglione.Services;

/// <summary>
/// Punto unico di presentazione dei dialoghi modali, seam statico nello stesso stile di
/// <c>FileSystemService.Accessor</c> / <c>UiDispatch</c> (nessun container DI).
/// Sceglie l'host in base al lifetime dell'applicazione:
/// <list type="bullet">
///   <item><description>desktop (<see cref="IClassicDesktopStyleApplicationLifetime"/>): la
///   <see cref="Window"/> storica mostrata con <c>ShowDialog</c> — percorso invariato, con
///   modalità del sistema operativo, <c>CenterOwner</c>, focus ed <c>Esc</c>;</description></item>
///   <item><description>single-view (Android): lo stesso corpo del dialogo montato
///   nell'overlay del <c>TopLevel</c> corrente via <see cref="OverlayDialogHost"/>.</description></item>
/// </list>
/// Senza host disponibile restituisce <c>default</c>, cioè lo stesso "annullato" che i chiamanti
/// già trattavano come assenza di finestra.
/// </summary>
public static class DialogPresenter
{
    /// <summary>
    /// Solo per i test: sostituisce il lifetime da cui si sceglie l'host.
    /// <c>Application.ApplicationLifetime</c> non è modificabile dopo l'inizializzazione, quindi
    /// il ramo desktop/single-view non sarebbe altrimenti esercitabile in headless.
    /// Ripristinare a null a fine test.
    /// </summary>
    internal static Func<IApplicationLifetime?>? LifetimeOverride { get; set; }

    /// <summary>
    /// Mostra un dialogo scegliendo l'host adatto alla piattaforma.
    /// I due factory sono lazy di proposito: si costruisce solo il ramo effettivamente usato,
    /// mai una <see cref="Window"/> destinata a non essere mostrata.
    /// </summary>
    /// <param name="createWindow">Crea la finestra desktop (che ospita già il proprio contenuto).</param>
    /// <param name="createContent">Crea il solo corpo del dialogo, per l'host overlay.</param>
    /// <param name="dataContext">ViewModel del dialogo, assegnato all'host scelto.</param>
    public static async Task<TResult> ShowAsync<TContent, TResult>(
        Func<Window> createWindow,
        Func<TContent> createContent,
        object dataContext)
        where TContent : Control, IDialogContent<TResult>
    {
        ArgumentNullException.ThrowIfNull(createWindow);
        ArgumentNullException.ThrowIfNull(createContent);

        IApplicationLifetime? lifetime = LifetimeOverride is not null
            ? LifetimeOverride()
            : App.Current?.ApplicationLifetime;

        switch (lifetime)
        {
            case IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }:
            {
                Window window = createWindow();
                window.DataContext = dataContext;
                return await window.ShowDialog<TResult>(owner);
            }

            case ISingleViewApplicationLifetime { MainView: { } root }:
            {
                TContent content = createContent();
                content.DataContext = dataContext;
                return await OverlayDialogHost.ShowAsync<TContent, TResult>(root, content);
            }

            default:
                return default!;
        }
    }
}
