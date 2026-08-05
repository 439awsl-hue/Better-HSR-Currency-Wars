using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Core;

/// <summary>
/// OCR 结果内存缓存。
///
/// 目的：自动流程中反复截取同一区域时，如果画面像素没有变化，
/// 直接复用上一次的 OCR 结果，跳过重复识别 —— 从而降低 OCR 频率与系统负荷，
/// 也避免反复把截图编码成 PNG 字节流产生的内存/CPU 开销。
///
/// 键 = 「截图像素区域 (left/top/width/height) + 图像指纹(全像素 FNV-1a)」。
/// 画面有任何变化，指纹就会不同，缓存自然失效，不影响识别正确性。
/// 容量有上限（默认 16 条），按最近使用顺序淘汰，避免长期挂机内存膨胀。
/// </summary>
public sealed class OcrResultCache
{
	public readonly record struct Key(int Left, int Top, int Width, int Height, ulong Fingerprint);

	private sealed class Entry
	{
		public OcrScanResult Result { get; set; } = OcrScanResult.Empty;

		public required LinkedListNode<Key> Node { get; init; }
	}

	private readonly int _capacity;

	private readonly Dictionary<Key, Entry> _map = new Dictionary<Key, Entry>();

	private readonly LinkedList<Key> _lru = new LinkedList<Key>();

	private readonly object _sync = new object();

	public int Count
	{
		get
		{
			lock (_sync)
			{
				return _map.Count;
			}
		}
	}

	public OcrResultCache(int capacity = 16)
	{
		_capacity = Math.Max(1, capacity);
	}

	public bool TryGet(Key key, out OcrScanResult? result)
	{
		lock (_sync)
		{
			if (_map.TryGetValue(key, out Entry? entry))
			{
				_lru.Remove(entry.Node);
				_lru.AddFirst(entry.Node);
				result = entry.Result;
				return true;
			}
			result = null;
			return false;
		}
	}

	public void Add(Key key, OcrScanResult result)
	{
		lock (_sync)
		{
			if (_map.TryGetValue(key, out Entry? existing))
			{
				existing.Result = result;
				_lru.Remove(existing.Node);
				_lru.AddFirst(existing.Node);
				return;
			}
			LinkedListNode<Key> node = _lru.AddFirst(key);
			_map.Add(key, new Entry { Result = result, Node = node });
			while (_map.Count > _capacity)
			{
				Key oldest = _lru.Last!.Value;
				_lru.RemoveLast();
				_map.Remove(oldest);
			}
		}
	}

	public void Clear()
	{
		lock (_sync)
		{
			_map.Clear();
			_lru.Clear();
		}
	}

	/// <summary>
	/// 计算 BitmapSource 的快速指纹：先统一转成 Bgra32，再对全部像素字节做 FNV-1a 64 位哈希。
	/// 完整像素哈希保证：只要画面有任何像素变化，指纹必然不同。
	/// </summary>
	public static ulong ComputeFingerprint(BitmapSource? image)
	{
		if (image == null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
		{
			return 0UL;
		}

		BitmapSource source = image;
		if (image.Format != PixelFormats.Bgra32)
		{
			try
			{
				source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0.0);
			}
			catch
			{
				// 个别格式转换失败时退回原图直接取像素
				source = image;
			}
		}

		int width = source.PixelWidth;
		int height = source.PixelHeight;
		int bytesPerPixel = (source.Format.BitsPerPixel + 7) / 8;
		int stride = width * bytesPerPixel;
		byte[] buffer = new byte[stride * height];
		try
		{
			source.CopyPixels(buffer, stride, 0);
		}
		catch
		{
			return 0UL;
		}

		// FNV-1a 64 位
		ulong hash = 14695981039346656037UL;
		foreach (byte value in buffer)
		{
			hash ^= value;
			hash *= 1099511628211UL;
		}
		return hash;
	}
}
