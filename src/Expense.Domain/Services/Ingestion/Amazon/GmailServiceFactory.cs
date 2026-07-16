using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Builds an authorized GmailService from cached OAuth credentials. As long as the
/// refresh token in the token store remains valid, this completes non-interactively -
/// which is what lets the Dashboard's "Sync Now" button work without a browser. If the
/// token has been revoked/expired, this call will try to launch an interactive consent
/// flow, which has no browser to complete when triggered from a web request - that
/// specific run will fail, and the fix is to re-run the console importer once to
/// re-authorize interactively.
/// </summary>
public static class GmailServiceFactory
{
    public static async Task<GmailService?> TryCreateAsync(string credentialsPath, string tokenStorePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(credentialsPath))
        {
            return null;
        }

        UserCredential credential;
        await using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
        {
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                (await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken)).Secrets,
                [GmailService.Scope.GmailReadonly],
                "user",
                cancellationToken,
                new FileDataStore(tokenStorePath, true));
        }

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Expense Amazon Importer"
        });
    }
}
