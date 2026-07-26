namespace HsrCurrencyWarsCleanWpf.Services;

public sealed record CaptureRegion(string Name, double X, double Y, double Width, double Height)
{
	public static CaptureRegion FullWindow { get; } = new CaptureRegion("全窗口", 0.0, 0.0, 1.0, 1.0);

	public static CaptureRegion TopHalf { get; } = new CaptureRegion("上半屏", 0.0, 0.0, 1.0, 0.5);

	public static CaptureRegion BottomHalf { get; } = new CaptureRegion("下半屏", 0.0, 0.5, 1.0, 0.5);

	public static CaptureRegion LeftBottom { get; } = new CaptureRegion("左下角", 0.0, 0.5, 0.5, 0.5);

	public static CaptureRegion RightBottom { get; } = new CaptureRegion("右下角", 0.5, 0.5, 0.5, 0.5);
}
