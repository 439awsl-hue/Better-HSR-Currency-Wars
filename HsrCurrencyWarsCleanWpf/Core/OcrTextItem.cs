using System.Windows;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record OcrTextItem(string Text, Rect Bounds, double Confidence);
