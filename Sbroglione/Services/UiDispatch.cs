using System;
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

    public static void Post(Action action)
    {
        if (Override is not null)
            Override(action);
        else
            Dispatcher.UIThread.Post(action);
    }
}
