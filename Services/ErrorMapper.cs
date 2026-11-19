using System.Net.Sockets;
using Azure;

namespace VoterVerification.Services;

public static class ErrorMapper
{
    public static AppException FromAzure(RequestFailedException exception)
    {
        var kind = exception.Status switch
        {
            401 or 403 => AppErrorKind.NotAuthorized,
            404 => AppErrorKind.NotFound,
            408 or 429 => AppErrorKind.AzureUnavailable,
            409 or 412 => AppErrorKind.Conflict,
            >= 500 => AppErrorKind.AzureUnavailable,
            _ => AppErrorKind.Unexpected
        };

        var message = kind switch
        {
            AppErrorKind.NotAuthorized =>
                "You are signed in, but this account cannot read or update the voter tables. An administrator needs to grant table data access on the storage account.",
            AppErrorKind.NotFound when exception.ErrorCode is "TableNotFound" =>
                "The Azure table was not found. Create the Voters and VoterVerifications tables, then try again.",
            AppErrorKind.NotFound =>
                "That record was not found.",
            AppErrorKind.AzureUnavailable when exception.Status == 429 =>
                "Azure Table Storage is busy. Wait a moment and try again.",
            AppErrorKind.AzureUnavailable =>
                "Azure Table Storage is unavailable. Try again shortly.",
            AppErrorKind.Conflict =>
                "This verification was changed by someone else. The latest values are shown.",
            _ =>
                "Azure Table Storage could not complete the request. Try again."
        };

        return new AppException(kind, message, exception);
    }

    public static string ToUserMessage(Exception exception)
    {
        if (exception is AppException appException)
        {
            return appException.Message;
        }

        if (exception is HttpRequestException or SocketException)
        {
            return "Can't reach Azure. Check the network connection and try again.";
        }

        if (exception is OperationCanceledException)
        {
            return "The request timed out. Check the network and try again.";
        }

        return "Something went wrong. Try again.";
    }
}
