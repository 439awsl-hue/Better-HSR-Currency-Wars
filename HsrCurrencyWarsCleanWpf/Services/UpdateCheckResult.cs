namespace HsrCurrencyWarsCleanWpf.Services;

public sealed record UpdateCheckResult(bool IsConfigured, bool HasUpdate, string Message, UpdateInfo? Update);
