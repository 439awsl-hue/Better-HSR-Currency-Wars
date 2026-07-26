using System.Collections.Generic;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record MatchResult(IReadOnlyList<string> HitWords, IReadOnlyList<string> MissingWords);
