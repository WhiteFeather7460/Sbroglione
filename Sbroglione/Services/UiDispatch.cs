using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Sbroglione.Services;

/// <summary>
/// Seam per postare sul thread UI dai callback dei servizi (che girano su threadpool).
/// Nei test, impostare Override = action => action() per esecuzione sincrona
/// (stesso pattern di ConfirmDialogHelper.Override).
/// </summary>
public static class UiDispatch
{
    public static Action<Action>? Override;

    // Solo per il ramo di test: Dispatcher.UIThread.Post mette in coda su un unico thread,
    // quindi le mutazioni delle collezioni bindate sono già serializzate in produzione. Il
    // ramo Override esegue invece sul thread chiamante (per determinismo nei test): con più
    // file copiati in parallelo verso la stessa destinazione, più thread da threadpool
    // possono invocare Post in concorrenza, corrompendo una ObservableCollection senza questo
    // lock (che replica la serializzazione a thread singolo del dispatcher reale).
    private static readonly object TestOverrideLock = new();

    public static void Post(Action action)
    {
        if (Override is not null)
        {
            lock (TestOverrideLock)
                Override(action);
        }
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Come <see cref="Post"/>, ma awaitable: usato dalla scansione a strati per attendere che
    /// la UI abbia finito di leggere lo stato di uno strato prima di iniziare a scrivere quello
    /// successivo (altrimenti il thread UI potrebbe enumerare una <c>Children</c> list mentre il
    /// thread di scansione la sta ancora popolando).
    /// </summary>
    public static Task InvokeAsync(Action action)
    {
        if (Override is not null)
        {
            lock (TestOverrideLock)
                Override(action);
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
