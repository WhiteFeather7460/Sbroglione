using System;
using System.IO;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>Mappa un'eccezione di I/O in un <see cref="ListingError"/> presentabile in UI.</summary>
internal static class FileSystemErrors
{
    internal static ListingError Create(Exception exception) => exception switch
    {
        DirectoryNotFoundException or FileNotFoundException =>
            new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound),
        UnauthorizedAccessException =>
            new ListingError(ListingErrorKind.AccessDenied, ListingErrorMessageKeys.AccessDenied),
        IOException =>
            new ListingError(ListingErrorKind.Unavailable, ListingErrorMessageKeys.Unavailable, exception.Message),
        _ => new ListingError(ListingErrorKind.Unavailable, ListingErrorMessageKeys.Generic, exception.Message)
    };
}
