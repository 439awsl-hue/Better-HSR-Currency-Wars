namespace HsrCurrencyWarsCleanWpf.Services;

public sealed record DragRequest(string Reason, int StartScreenX, int StartScreenY, int EndScreenX, int EndScreenY);
