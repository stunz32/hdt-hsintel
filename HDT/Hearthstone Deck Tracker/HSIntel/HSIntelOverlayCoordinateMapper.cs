using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Hearthstone_Deck_Tracker.Utility;
using HSIntel.Overlay.Services;
using Hearthstone_Deck_Tracker.Windows;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace Hearthstone_Deck_Tracker.HSIntel
{
	internal sealed class HSIntelOverlayCoordinateMapper : OverlayCoordinateMapperBase
	{
		private readonly OverlayWindow _overlayWindow;

		public HSIntelOverlayCoordinateMapper(OverlayWindow overlayWindow)
		{
			_overlayWindow = overlayWindow ?? throw new ArgumentNullException(nameof(overlayWindow));
		}

		public override void Refresh()
		{
			var overlayWidth = _overlayWindow.ActualWidth > 0 ? _overlayWindow.ActualWidth : _overlayWindow.Width;
			var overlayHeight = _overlayWindow.ActualHeight > 0 ? _overlayWindow.ActualHeight : _overlayWindow.Height;

			if(overlayWidth <= 0 || overlayHeight <= 0)
				return;

			var hsRect = Helper.GetHearthstoneRect(true);
			if(hsRect.Width <= 0 || hsRect.Height <= 0)
				return;

			var overlayRect = new Rect(_overlayWindow.Left, _overlayWindow.Top, overlayWidth, overlayHeight);

			var regionDrawer = new Utility.RegionDrawer.RegionDrawer(overlayHeight, overlayWidth, _overlayWindow.ScreenRatio);

			var boardNormalized = CombineBoardRegions(regionDrawer);
			var handNormalized = CombineHandRegions(regionDrawer);

			var boardRect = ToScreenRect(boardNormalized, overlayRect);
			var handRect = ToScreenRect(handNormalized, overlayRect);

			var hearthstoneRect = new Rect(hsRect.X, hsRect.Y, hsRect.Width, hsRect.Height);

			SetBounds(hearthstoneRect, overlayRect, boardRect, handRect);

			Log.Debug($"[HSIntel][Overlay] Mapper refresh: overlay={overlayRect.Width}x{overlayRect.Height} board=({boardRect.X},{boardRect.Y},{boardRect.Width},{boardRect.Height}) hand=({handRect.X},{handRect.Y},{handRect.Width},{handRect.Height})");
		}

		private static Rect CombineBoardRegions(Utility.RegionDrawer.RegionDrawer drawer)
		{
			var rects = new List<Rect>();
			for(var pos = 1; pos <= 7; pos++)
			{
				var opponentRegions = drawer.DrawBoardCardRegions(7, pos, false, 0, 0);
				var playerRegions = drawer.DrawBoardCardRegions(7, pos, true, 0, 0);
				rects.Add(TakePrimary(opponentRegions));
				rects.Add(TakePrimary(playerRegions));
			}

			return UnionAll(rects);
		}

		private static Rect CombineHandRegions(Utility.RegionDrawer.RegionDrawer drawer)
		{
			var rects = new List<Rect>();
			for(var pos = 1; pos <= 10; pos++)
			{
				var regions = drawer.DrawHandCardRegions(10, pos, true, null, 0, 0);
				rects.Add(TakePrimary(regions));
			}

			return UnionAll(rects);
		}

		private static Rect TakePrimary(IReadOnlyList<Rect> rects) => rects.Count > 0 ? rects[0] : Rect.Empty;

		private static Rect UnionAll(IEnumerable<Rect> rects)
		{
			var result = Rect.Empty;
			foreach(var rect in rects.Where(r => !r.IsEmpty))
			{
				result = result.IsEmpty ? rect : Rect.Union(result, rect);
			}
			return result;
		}

		private static Rect ToScreenRect(Rect normalized, Rect overlayRect)
		{
			if(normalized.IsEmpty)
				return Rect.Empty;

			var left = overlayRect.X + normalized.X * overlayRect.Width;
			var top = overlayRect.Y + normalized.Y * overlayRect.Height;
			var width = normalized.Width * overlayRect.Width;
			var height = normalized.Height * overlayRect.Height;

			return new Rect(left, top, width, height);
		}
	}
}


