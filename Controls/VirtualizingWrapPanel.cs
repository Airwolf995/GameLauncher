using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GameLauncher.Controls
{
    /// <summary>
    /// Vertikal scrollendes Wrap-Panel mit UI-Virtualisierung fuer gleich grosse Elemente.
    /// </summary>
    public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(
                nameof(ItemWidth),
                typeof(double),
                typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(320.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(
                nameof(ItemHeight),
                typeof(double),
                typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        private Size _extent = Size.Empty;
        private Size _viewport = Size.Empty;
        private Point _offset;

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        public bool CanVerticallyScroll { get; set; } = true;

        public bool CanHorizontallyScroll
        {
            get => false;
            set { }
        }

        public double ExtentWidth => _extent.Width;

        public double ExtentHeight => _extent.Height;

        public double ViewportWidth => _viewport.Width;

        public double ViewportHeight => _viewport.Height;

        public double HorizontalOffset => 0;

        public double VerticalOffset => _offset.Y;

        public ScrollViewer? ScrollOwner { get; set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            int itemCount = itemsControl?.Items.Count ?? 0;

            if (itemCount == 0 || ItemWidth <= 0 || ItemHeight <= 0)
            {
                ResetScrollState(availableSize);
                RemoveInternalChildRange(0, InternalChildren.Count);
                return availableSize;
            }

            double viewportWidth = NormalizeViewportWidth(availableSize.Width, itemsControl);
            double viewportHeight = double.IsInfinity(availableSize.Height) ? ItemHeight : availableSize.Height;
            int itemsPerRow = CalculateItemsPerRow(viewportWidth);
            int rowCount = (int)Math.Ceiling(itemCount / (double)itemsPerRow);

            _viewport = new Size(viewportWidth, viewportHeight);
            _extent = new Size(viewportWidth, rowCount * ItemHeight);
            CoerceVerticalOffset();
            ScrollOwner?.InvalidateScrollInfo();

            var indexRange = GetVisibleIndexRange(itemCount, itemsPerRow);
            RealizeItems(indexRange.firstIndex, indexRange.lastIndex);

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(ItemWidth, ItemHeight));
            }

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            int itemCount = itemsControl?.Items.Count ?? 0;
            if (itemCount == 0 || ItemWidth <= 0 || ItemHeight <= 0)
            {
                return finalSize;
            }

            int itemsPerRow = CalculateItemsPerRow(finalSize.Width);
            for (int childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
            {
                if (InternalChildren[childIndex] is not UIElement child)
                {
                    continue;
                }

                int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
                if (itemIndex < 0)
                {
                    continue;
                }

                int row = itemIndex / itemsPerRow;
                int column = itemIndex % itemsPerRow;
                Rect rect = new Rect(
                    column * ItemWidth,
                    row * ItemHeight - _offset.Y,
                    ItemWidth,
                    ItemHeight);
                child.Arrange(rect);
            }

            return finalSize;
        }

        protected override void OnClearChildren()
        {
            base.OnClearChildren();
            _offset = new Point(0, 0);
            ScrollOwner?.InvalidateScrollInfo();
        }

        protected override void BringIndexIntoView(int index)
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            int itemCount = itemsControl?.Items.Count ?? 0;
            if (itemCount == 0 || index < 0 || index >= itemCount)
            {
                return;
            }

            int itemsPerRow = CalculateItemsPerRow(_viewport.Width > 0 ? _viewport.Width : RenderSize.Width);
            int targetRow = index / itemsPerRow;
            double targetOffset = targetRow * ItemHeight;
            SetVerticalOffset(targetOffset);
            UpdateLayout();
        }

        public void LineUp() => SetVerticalOffset(VerticalOffset - 32);

        public void LineDown() => SetVerticalOffset(VerticalOffset + 32);

        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - SystemParameters.WheelScrollLines * 16);

        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + SystemParameters.WheelScrollLines * 16);

        public void LineLeft() { }

        public void LineRight() { }

        public void PageLeft() { }

        public void PageRight() { }

        public void MouseWheelLeft() { }

        public void MouseWheelRight() { }

        public void SetHorizontalOffset(double offset) { }

        public void SetVerticalOffset(double offset)
        {
            double maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
            double newOffset = Math.Max(0, Math.Min(offset, maxOffset));
            if (Math.Abs(newOffset - _offset.Y) < 0.1)
            {
                return;
            }

            _offset.Y = newOffset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (visual is not UIElement element)
            {
                return rectangle;
            }

            int index = InternalChildren.IndexOf(element);
            if (index >= 0)
            {
                int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0));
                if (itemIndex >= 0)
                {
                    BringIndexIntoView(itemIndex);
                }
            }

            return rectangle;
        }

        private void ResetScrollState(Size availableSize)
        {
            _extent = new Size(availableSize.Width, 0);
            _viewport = new Size(availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
            _offset = new Point(0, 0);
            ScrollOwner?.InvalidateScrollInfo();
        }

        private double NormalizeViewportWidth(double availableWidth, ItemsControl? itemsControl)
        {
            if (!double.IsInfinity(availableWidth) && availableWidth > 0)
            {
                return availableWidth;
            }

            if (itemsControl?.ActualWidth > 0)
            {
                return itemsControl.ActualWidth;
            }

            return ItemWidth;
        }

        private int CalculateItemsPerRow(double availableWidth)
        {
            if (availableWidth <= 0 || ItemWidth <= 0)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Floor(availableWidth / ItemWidth));
        }

        public (int firstIndex, int lastIndexExclusive) GetBufferedIndexRange(double verticalBuffer)
        {
            int itemCount = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
            if (itemCount == 0 || ItemWidth <= 0 || ItemHeight <= 0)
            {
                return (0, 0);
            }

            double width = _viewport.Width > 0 ? _viewport.Width : (RenderSize.Width > 0 ? RenderSize.Width : ItemWidth);
            double viewportHeight = _viewport.Height > 0 ? _viewport.Height : (RenderSize.Height > 0 ? RenderSize.Height : ItemHeight);
            int itemsPerRow = CalculateItemsPerRow(width);
            int rowCount = (int)Math.Ceiling(itemCount / (double)itemsPerRow);
            int firstRow = Math.Max(0, (int)Math.Floor((VerticalOffset - verticalBuffer) / ItemHeight));
            int lastRow = Math.Min(
                rowCount - 1,
                Math.Max(firstRow, (int)Math.Ceiling((VerticalOffset + viewportHeight + verticalBuffer) / ItemHeight)));

            int firstIndex = Math.Min(itemCount, firstRow * itemsPerRow);
            int lastIndexExclusive = Math.Min(itemCount, (lastRow + 1) * itemsPerRow);
            return (firstIndex, lastIndexExclusive);
        }

        public (int firstIndex, int lastIndexExclusive) GetRealizedIndexRange()
        {
            if (InternalChildren.Count == 0)
            {
                return (0, 0);
            }

            int firstIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(0, 0));
            int lastIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.Count - 1, 0));
            if (firstIndex < 0 || lastIndex < 0 || lastIndex < firstIndex)
            {
                return (0, 0);
            }

            return (firstIndex, lastIndex + 1);
        }

        private (int firstIndex, int lastIndex) GetVisibleIndexRange(int itemCount, int itemsPerRow)
        {
            int firstVisibleRow = Math.Max(0, (int)Math.Floor(VerticalOffset / ItemHeight));
            int visibleRowCount = Math.Max(1, (int)Math.Ceiling(ViewportHeight / ItemHeight) + 1);
            int cacheRows = 2;

            int firstRow = Math.Max(0, firstVisibleRow - cacheRows);
            int lastRow = Math.Min(
                (int)Math.Ceiling(itemCount / (double)itemsPerRow) - 1,
                firstVisibleRow + visibleRowCount + cacheRows);

            int firstIndex = firstRow * itemsPerRow;
            int lastIndex = Math.Min(itemCount - 1, ((lastRow + 1) * itemsPerRow) - 1);
            return (firstIndex, lastIndex);
        }

        private void RealizeItems(int firstIndex, int lastIndex)
        {
            CleanupItemsOutsideRange(firstIndex, lastIndex);

            IItemContainerGenerator generator = ItemContainerGenerator;
            GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(firstIndex);
            int childInsertIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

            using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
            {
                for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childInsertIndex++)
                {
                    UIElement? child = generator.GenerateNext(out bool isNewlyRealized) as UIElement;
                    if (child == null)
                    {
                        continue;
                    }

                    if (isNewlyRealized)
                    {
                        if (childInsertIndex >= InternalChildren.Count)
                        {
                            AddInternalChild(child);
                        }
                        else
                        {
                            InsertInternalChild(childInsertIndex, child);
                        }

                        generator.PrepareItemContainer(child);
                    }
                    else if (InternalChildren[childInsertIndex] != child)
                    {
                        RemoveInternalChildRange(childInsertIndex, 1);
                        InsertInternalChild(childInsertIndex, child);
                    }
                }
            }
        }

        private void CleanupItemsOutsideRange(int firstIndex, int lastIndex)
        {
            for (int childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
            {
                GeneratorPosition position = new GeneratorPosition(childIndex, 0);
                int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);
                if (itemIndex >= firstIndex && itemIndex <= lastIndex)
                {
                    continue;
                }

                ItemContainerGenerator.Remove(position, 1);
                RemoveInternalChildRange(childIndex, 1);
            }
        }

        private void CoerceVerticalOffset()
        {
            double maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
            if (_offset.Y > maxOffset)
            {
                _offset.Y = maxOffset;
            }
        }
    }
}
