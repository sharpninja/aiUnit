using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SharpNinja.AiUnit.Desktop.Controls;

/// <summary>
/// Reusable image viewer with:
/// - Mouse wheel zoom (centered on cursor)
/// - Middle mouse drag panning
/// - Toolbar driven modes: Pan, BoxZoom, Highlighter, TextArea, Arrow
/// - Rubber band box zoom
/// - Persistent markups drawn on overlay (in image pixel coordinates)
/// All transforms are applied to a container so markups stay correctly positioned at any zoom/pan.
/// </summary>
public partial class MarkupImageViewer : UserControl
{
    private ScrollViewer? _scrollViewer;
    private Grid? _contentGrid;
    private Image? _imageElement;
    private Canvas? _markupOverlay;

    private double _zoom = 1.0;

    private Tool _currentTool = Tool.Pan;
    private bool _isDragging;
    private Point _dragStartScreen;
    private Point _dragStartImage;
    private Point _lastPointerPosition;

    // Temporary visuals for rubber band / drag preview
    private Avalonia.Controls.Shapes.Shape? _tempShape;

    // Persistent markups (in original image pixel coordinates)
    public ObservableCollection<ImageMarkup> Markups { get; } = new();

    // Remember the last base scale requested by the dropdown / ShowCurrentScenario so we can
    // re-apply it when the control finally receives a real positive size after dynamic reparenting.
    private Avalonia.Media.Stretch? _lastBaseStretch;

    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<MarkupImageViewer, Bitmap?>(nameof(Source));

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Raised when the user interacts with this viewer (pointer press or wheel).
    /// MainWindow uses this to track which image is "active" for toolbar zoom actions.
    /// </summary>
    public event Action<MarkupImageViewer>? Activated;

    public enum Tool
    {
        Pan,
        BoxZoom,
        Highlighter,
        TextArea,
        Arrow
    }

    public Tool CurrentTool
    {
        get => _currentTool;
        set
        {
            if (_currentTool == value) return;
            _currentTool = value;
            UpdateCursor();
            CancelTemporaryVisuals();
        }
    }

    public MarkupImageViewer()
    {
        InitializeComponent();

        // Find named parts after template is applied
        TemplateApplied += OnTemplateApplied;

        // Pointer events (we use AddHandler for wheel to get it even when not focused)
        PointerWheelChanged += OnPointerWheelChanged;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, __) => CancelTemporaryVisuals();

        // Redraw markups when collection changes
        Markups.CollectionChanged += (_, __) => RedrawMarkups();

        // When source changes, reset zoom/pan and layout the content to native pixel size
        SourceProperty.Changed.AddClassHandler<MarkupImageViewer>((s, e) => s.OnSourceChanged());
    }

    // Make this control a well-behaved "viewport" in the parent layout (the * rows in the
    // stacked or side-by-side panels). We always fill the space we are given and do not let
    // the large internal content (sized to image pixels for accurate markup coordinates) push
    // or distort the outer star-row allocation. The content is scaled/translated/clipped inside.
    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height))
            return new Size(400, 300);
        return availableSize;
    }

    // We rely on the base arrange for the ScrollViewer child. The MeasureOverride is the important one
    // to make the viewer behave as a fixed viewport in the parent * layout.

    private void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        _scrollViewer = e.NameScope.Find<ScrollViewer>("ScrollViewer");
        _contentGrid = e.NameScope.Find<Grid>("ContentGrid");
        _imageElement = e.NameScope.Find<Image>("ImageElement");
        _markupOverlay = e.NameScope.Find<Canvas>("MarkupOverlay");

        UpdateCursor();

        // Hook size changes so we can re-apply base scale (Fit etc.) once the ScrollViewer
        // reports a real viewport after being placed into the final ContentGrid/leftStack layout.
        if (_scrollViewer != null)
        {
            _scrollViewer.SizeChanged += OnViewportSizeChanged;
        }

        // Also listen to our own size changes so base scale (Fit etc.) can re-pin the content box
        // to the actual allocated panel size whenever the outer layout (leftStack rows, splitters, window)
        // gives us a new size.
        this.SizeChanged += OnViewerSizeChanged;

        // If Source was set before the template was realized, apply it now.
        if (Source != null)
        {
            OnSourceChanged();
        }
    }

    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_lastBaseStretch.HasValue && Source != null && e.NewSize.Width > 10 && e.NewSize.Height > 10)
        {
            // The viewport just became known (or changed, e.g. splitter drag). Re-fit the base scale.
            SetBaseScale(_lastBaseStretch.Value);
        }
    }

    private void OnViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_lastBaseStretch.HasValue && Source != null && e.NewSize.Width > 10 && e.NewSize.Height > 10)
        {
            SetBaseScale(_lastBaseStretch.Value);
        }
    }

    private void OnSourceChanged()
    {
        if (_imageElement == null) return;

        _imageElement.Source = Source;

        // Sizing for the base presentation (Fit/None/etc from the dropdown) is handled
        // by SetBaseScale / ResetView after the layout is settled (in ShowCurrentScenario
        // after UpdateLayout, or when the dropdown changes).
        // We only set the Source here so the bitmap is available to the Image as early as possible.
        // The Viewport-driven content sizing in the fit path will make the scaled image visible
        // in the panel without the Viewport "covering" empty space in a large mismatched canvas.

        Markups.Clear();

        // If a base stretch was already requested before this source arrived (or on reparent),
        // schedule a fit now that we have pixels.
        if (_lastBaseStretch.HasValue)
        {
            Dispatcher.UIThread.Post(() => SetBaseScale(_lastBaseStretch.Value), DispatcherPriority.Background);
        }
    }

    private void UpdateTransform()
    {
        // No longer used (ScrollViewer + explicit content size handles zoom/pan).
        // Kept for compatibility with any remaining calls.
    }

    private void UpdateCursor()
    {
        Cursor = _currentTool switch
        {
            Tool.Pan => new Cursor(StandardCursorType.Hand),
            Tool.BoxZoom => new Cursor(StandardCursorType.Cross),
            Tool.Highlighter => new Cursor(StandardCursorType.Cross),
            Tool.TextArea => new Cursor(StandardCursorType.Ibeam),
            Tool.Arrow => new Cursor(StandardCursorType.Cross),
            _ => Cursor.Default
        };
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_scrollViewer == null || _contentGrid == null || Source == null) return;

        Activated?.Invoke(this);

        // User is interactively zooming; stop auto-reapplying the dropdown base scale on future size changes.
        _lastBaseStretch = null;

        var mousePos = e.GetPosition(_scrollViewer);

        double oldZoom = _zoom;
        double factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
        _zoom = Math.Clamp(_zoom * factor, 0.05, 32.0);

        // Update the content size for the new zoom (this scales the bitmap visually)
        double naturalW = Source.PixelSize.Width;
        double naturalH = Source.PixelSize.Height;
        double newDisplayW = naturalW * _zoom;
        double newDisplayH = naturalH * _zoom;

        _contentGrid.Width = newDisplayW;
        _contentGrid.Height = newDisplayH;

        _markupOverlay!.Width = newDisplayW;
        _markupOverlay!.Height = newDisplayH;

        _imageElement!.Width = newDisplayW;
        _imageElement!.Height = newDisplayH;

        // Adjust scroll offsets so the point under the mouse stays under the mouse after the size change.
        // This gives "centered zoom" feel.
        double scaleChange = _zoom / oldZoom;

        double offsetX = _scrollViewer.Offset.X;
        double offsetY = _scrollViewer.Offset.Y;

        double newOffsetX = mousePos.X + (offsetX - mousePos.X) * scaleChange;
        double newOffsetY = mousePos.Y + (offsetY - mousePos.Y) * scaleChange;

        _scrollViewer.Offset = new Vector(newOffsetX, newOffsetY);

        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_scrollViewer == null) return;

        Activated?.Invoke(this);

        var currentPoint = e.GetPosition(_scrollViewer);
        _lastPointerPosition = currentPoint;

        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
            (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _currentTool == Tool.Pan))
        {
            _isDragging = true;
            // Use hand cursor while panning
        }
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartScreen = currentPoint;
            _dragStartImage = PointToImageCoords(currentPoint);

            // Start tool-specific preview
            if (_currentTool == Tool.BoxZoom || _currentTool == Tool.Highlighter || _currentTool == Tool.TextArea || _currentTool == Tool.Arrow)
            {
                CancelTemporaryVisuals();
                _tempShape = _currentTool switch
                {
                    Tool.BoxZoom => new Rectangle { Stroke = Brushes.Cyan, StrokeThickness = 1, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 200, 255)) },
                    Tool.Highlighter => new Rectangle { Stroke = Brushes.Yellow, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0)) },
                    Tool.TextArea => new Rectangle { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromArgb(30, 30, 144, 255)) },
                    Tool.Arrow => new Line { Stroke = Brushes.OrangeRed, StrokeThickness = 2 },
                    _ => null
                };

                if (_tempShape != null && _markupOverlay != null)
                {
                    Canvas.SetLeft(_tempShape, 0);
                    Canvas.SetTop(_tempShape, 0);
                    _markupOverlay.Children.Add(_tempShape);
                }
            }
        }
        else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Right click + drag also starts box zoom (per requirement)
            if (_currentTool != Tool.BoxZoom)
            {
                // Temporarily enter box zoom for this gesture
                _currentTool = Tool.BoxZoom;
                UpdateCursor();
            }

            _isDragging = true;
            _dragStartScreen = currentPoint;
            _dragStartImage = PointToImageCoords(currentPoint);

            CancelTemporaryVisuals();
            _tempShape = new Rectangle
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 1,
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 200, 255))
            };

            if (_markupOverlay != null && _tempShape != null)
            {
                Canvas.SetLeft(_tempShape, 0);
                Canvas.SetTop(_tempShape, 0);
                _markupOverlay.Children.Add(_tempShape);
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_scrollViewer == null || !_isDragging) return;

        var current = e.GetPosition(_scrollViewer);

        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
            (_currentTool == Tool.Pan && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed))
        {
            // Panning via ScrollViewer offsets (reliable, image always visible)
            var delta = current - _lastPointerPosition;
            var newOffset = new Vector(
                _scrollViewer!.Offset.X - delta.X,
                _scrollViewer!.Offset.Y - delta.Y
            );
            _scrollViewer.Offset = newOffset;
        }
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Tool specific drag preview
            var currentImage = PointToImageCoords(current);

            switch (_currentTool)
            {
                case Tool.BoxZoom:
                case Tool.Highlighter:
                case Tool.TextArea:
                    if (_tempShape is Rectangle rect)
                    {
                        double x = Math.Min(_dragStartScreen.X, current.X);
                        double y = Math.Min(_dragStartScreen.Y, current.Y);
                        double w = Math.Abs(current.X - _dragStartScreen.X);
                        double h = Math.Abs(current.Y - _dragStartScreen.Y);

                        Canvas.SetLeft(rect, x);
                        Canvas.SetTop(rect, y);
                        rect.Width = w;
                        rect.Height = h;
                    }
                    break;

                case Tool.Arrow:
                    if (_tempShape is Line line)
                    {
                        line.StartPoint = _dragStartScreen;
                        line.EndPoint = current;
                    }
                    break;
            }
        }

        _lastPointerPosition = current;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            CancelTemporaryVisuals();
            return;
        }

        var current = e.GetPosition(_scrollViewer);
        var endImage = PointToImageCoords(current);

        if (_currentTool == Tool.BoxZoom)
        {
            // Perform box zoom
            var startImage = _dragStartImage;
            var rect = new Rect(
                Math.Min(startImage.X, endImage.X),
                Math.Min(startImage.Y, endImage.Y),
                Math.Abs(endImage.X - startImage.X),
                Math.Abs(endImage.Y - startImage.Y));

            if (rect.Width > 5 && rect.Height > 5 && _scrollViewer != null)
            {
                _lastBaseStretch = null;
                ZoomToRect(rect);
            }
        }
        else if (_currentTool == Tool.Highlighter)
        {
            var startImage = _dragStartImage;
            var rect = new Rect(
                Math.Min(startImage.X, endImage.X),
                Math.Min(startImage.Y, endImage.Y),
                Math.Abs(endImage.X - startImage.X),
                Math.Abs(endImage.Y - startImage.Y));

            if (rect.Width > 3 && rect.Height > 3)
            {
                AddHighlighter(rect);
            }
        }
        else if (_currentTool == Tool.TextArea)
        {
            var startImage = _dragStartImage;
            var rect = new Rect(
                Math.Min(startImage.X, endImage.X),
                Math.Min(startImage.Y, endImage.Y),
                Math.Abs(endImage.X - startImage.X),
                Math.Abs(endImage.Y - startImage.Y));

            if (rect.Width > 10 && rect.Height > 10)
            {
                AddTextArea(rect, "Note"); // Default text; user can imagine editing later or we can enhance
            }
        }
        else if (_currentTool == Tool.Arrow)
        {
            var dx = endImage.X - _dragStartImage.X;
            var dy = endImage.Y - _dragStartImage.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > 8)
            {
                AddArrow(_dragStartImage, endImage);
            }
        }

        _isDragging = false;
        CancelTemporaryVisuals();

        // If we temporarily entered BoxZoom via right click, restore to previous? For simplicity we leave the tool as-is.
        // User can click the toolbar button to change.
    }

    private Point PointToImageCoords(Point screenPoint)
    {
        // Convert a point in the ScrollViewer's coordinate space back to original image pixel space.
        // The content is sized to natural * _zoom, so divide by zoom.
        double invZoom = 1.0 / Math.Max(_zoom, 0.0001);
        double imgX = (screenPoint.X + _scrollViewer!.Offset.X) * invZoom;
        double imgY = (screenPoint.Y + _scrollViewer!.Offset.Y) * invZoom;
        return new Point(imgX, imgY);
    }

    private void ZoomToRect(Rect imageRect)
    {
        if (_scrollViewer == null || Source == null || imageRect.Width < 1 || imageRect.Height < 1) return;

        double viewW = _scrollViewer.Viewport.Width;
        double viewH = _scrollViewer.Viewport.Height;

        if (viewW < 10 || viewH < 10) return;

        double naturalW = Source.PixelSize.Width;
        double naturalH = Source.PixelSize.Height;

        // The rect is in original pixel space. We want to set zoom so that the rect fills the view.
        double scaleX = viewW / imageRect.Width;
        double scaleY = viewH / imageRect.Height;
        _zoom = Math.Clamp(Math.Min(scaleX, scaleY), 0.05, 32.0);

        double displayW = naturalW * _zoom;
        double displayH = naturalH * _zoom;

        if (_contentGrid != null)
        {
            _contentGrid.Width = displayW;
            _contentGrid.Height = displayH;
        }
        if (_markupOverlay != null)
        {
            _markupOverlay.Width = displayW;
            _markupOverlay.Height = displayH;
        }
        if (_imageElement != null)
        {
            _imageElement.Width = displayW;
            _imageElement.Height = displayH;
        }

        // Position so the selected rect is at the top-left of the view (or centered; top-left is simple and useful for "zoom to area").
        double offsetX = imageRect.X * _zoom;
        double offsetY = imageRect.Y * _zoom;

        _scrollViewer.Offset = new Vector(offsetX, offsetY);
    }

    public void ResetView(bool fit = false)
    {
        if (_scrollViewer == null || Source == null)
        {
            _zoom = 1.0;
            if (_scrollViewer != null)
                _scrollViewer.Offset = new Vector(0, 0);
            return;
        }

        double naturalW = Source.PixelSize.Width;
        double naturalH = Source.PixelSize.Height;

        if (fit)
        {
            double viewW = Math.Max(_scrollViewer.Viewport.Width, 100);
            double viewH = Math.Max(_scrollViewer.Viewport.Height, 100);

            double scale = Math.Min(viewW / naturalW, viewH / naturalH);
            _zoom = Math.Clamp(scale, 0.05, 32.0);

            // Size the visible content area (the rect the ScrollViewer shows) to the viewport size.
            // This makes the "panel" the user sees have the scaled image, like the original
            // direct Image + Stretch inside ScrollViewer.
            if (_contentGrid != null)
            {
                _contentGrid.Width = viewW;
                _contentGrid.Height = viewH;
            }

            if (_imageElement != null)
            {
                _imageElement.Stretch = Stretch.Uniform;
                // Explicitly size the Image to the panel-sized content area.
                // With Uniform, the bitmap will be scaled to fill this box (the visible panel).
                // This guarantees the image is visible and fills the allocated space for the base Fit.
                _imageElement.Width = viewW;
                _imageElement.Height = viewH;
            }

            // Overlay at natural for pixel-accurate markup data.
            // RedrawMarkups scales the shapes by _zoom.
            if (_markupOverlay != null)
            {
                // For base Fit, size the overlay to the same panel-sized content area.
                // This ensures the ScrollViewer's visible area (Viewport) shows the scaled image
                // filling the panel, instead of a small portion of a huge canvas.
                // Markups will be drawn correctly at the scaled positions (using _zoom = fit scale).
                _markupOverlay.Width = viewW;
                _markupOverlay.Height = viewH;
            }

            _scrollViewer.Offset = new Vector(0, 0);
        }
        else
        {
            _zoom = 1.0;

            double displayW = naturalW * _zoom;
            double displayH = naturalH * _zoom;

            if (_contentGrid != null)
            {
                _contentGrid.Width = displayW;
                _contentGrid.Height = displayH;
            }
            if (_markupOverlay != null)
            {
                _markupOverlay.Width = displayW;
                _markupOverlay.Height = displayH;
            }
            if (_imageElement != null)
            {
                _imageElement.Width = displayW;
                _imageElement.Height = displayH;
                _imageElement.Stretch = Stretch.None;  // True 1:1
            }

            _scrollViewer.Offset = new Vector(0, 0);
        }
    }

    // === Markup creation ===

    private void AddHighlighter(Rect imageRect)
    {
        var markup = new ImageMarkup
        {
            Type = MarkupType.Highlighter,
            Bounds = imageRect
        };
        Markups.Add(markup);
        RedrawMarkups();
    }

    private void AddTextArea(Rect imageRect, string text)
    {
        var markup = new ImageMarkup
        {
            Type = MarkupType.TextArea,
            Bounds = imageRect,
            Text = text
        };
        Markups.Add(markup);
        RedrawMarkups();
    }

    private void AddArrow(Point start, Point end)
    {
        var markup = new ImageMarkup
        {
            Type = MarkupType.Arrow,
            StartPoint = start,
            EndPoint = end,
            Bounds = new Rect(start, end) // approximate
        };
        Markups.Add(markup);
        RedrawMarkups();
    }

    private void RedrawMarkups()
    {
        if (_markupOverlay == null || Source == null) return;

        _markupOverlay.Children.Clear();

        double s = _zoom; // current display scale for the canvas (natural * zoom)

        foreach (var m in Markups)
        {
            switch (m.Type)
            {
                case MarkupType.Highlighter:
                    if (m.Bounds.Width > 0 && m.Bounds.Height > 0)
                    {
                        var rect = new Rectangle
                        {
                            Width = m.Bounds.Width * s,
                            Height = m.Bounds.Height * s,
                            Fill = new SolidColorBrush(Color.FromArgb(90, 255, 255, 0)),
                            Stroke = Brushes.Gold,
                            StrokeThickness = 1.5
                        };
                        Canvas.SetLeft(rect, m.Bounds.X * s);
                        Canvas.SetTop(rect, m.Bounds.Y * s);
                        _markupOverlay.Children.Add(rect);
                    }
                    break;

                case MarkupType.TextArea:
                    if (m.Bounds.Width > 0 && m.Bounds.Height > 0)
                    {
                        var rect = new Rectangle
                        {
                            Width = m.Bounds.Width * s,
                            Height = m.Bounds.Height * s,
                            Fill = new SolidColorBrush(Color.FromArgb(40, 100, 149, 237)),
                            Stroke = Brushes.DeepSkyBlue,
                            StrokeThickness = 1.5
                        };
                        Canvas.SetLeft(rect, m.Bounds.X * s);
                        Canvas.SetTop(rect, m.Bounds.Y * s);
                        _markupOverlay.Children.Add(rect);

                        var tb = new TextBlock
                        {
                            Text = m.Text ?? "Text area",
                            Foreground = Brushes.White,
                            FontSize = 11,
                            FontWeight = FontWeight.SemiBold,
                            Margin = new Thickness(4, 2, 4, 2)
                        };
                        Canvas.SetLeft(tb, m.Bounds.X * s + 2);
                        Canvas.SetTop(tb, m.Bounds.Y * s + 2);
                        _markupOverlay.Children.Add(tb);
                    }
                    break;

                case MarkupType.Arrow:
                    if (m.StartPoint.HasValue && m.EndPoint.HasValue)
                    {
                        var start = m.StartPoint.Value;
                        var end = m.EndPoint.Value;

                        // Main line (scaled)
                        var line = new Line
                        {
                            StartPoint = new Point(start.X * s, start.Y * s),
                            EndPoint = new Point(end.X * s, end.Y * s),
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 2.5
                        };
                        _markupOverlay.Children.Add(line);

                        // Arrow head (scaled)
                        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
                        double headLength = 12 * s;
                        double headAngle = Math.PI / 6;

                        var p1 = new Point(
                            end.X * s - headLength * Math.Cos(angle - headAngle),
                            end.Y * s - headLength * Math.Sin(angle - headAngle));
                        var p2 = new Point(
                            end.X * s - headLength * Math.Cos(angle + headAngle),
                            end.Y * s - headLength * Math.Sin(angle + headAngle));

                        var head = new Polyline
                        {
                            Points = new[] { p1, new Point(end.X * s, end.Y * s), p2 },
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 2.5,
                            Fill = Brushes.OrangeRed
                        };
                        _markupOverlay.Children.Add(head);

                        // 10px circle around arrow head (as specified for capture), scaled
                        double r = 10 * s;
                        var circle = new Ellipse
                        {
                            Width = r * 2,
                            Height = r * 2,
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 1.5,
                            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 69, 0))
                        };
                        Canvas.SetLeft(circle, end.X * s - r);
                        Canvas.SetTop(circle, end.Y * s - r);
                        _markupOverlay.Children.Add(circle);
                    }
                    break;
            }
        }
    }

    private void CancelTemporaryVisuals()
    {
        if (_markupOverlay != null && _tempShape != null)
        {
            _markupOverlay.Children.Remove(_tempShape);
        }
        _tempShape = null;
    }

    // Public helpers for toolbar
    public void ZoomIn()
    {
        if (_scrollViewer == null || Source == null) return;

        _lastBaseStretch = null; // user-controlled zoom

        double oldZoom = _zoom;
        _zoom = Math.Min(_zoom * 1.25, 32.0);

        double naturalW = Source.PixelSize.Width;
        double naturalH = Source.PixelSize.Height;

        double newDisplayW = naturalW * _zoom;
        double newDisplayH = naturalH * _zoom;

        if (_contentGrid != null)
        {
            _contentGrid.Width = newDisplayW;
            _contentGrid.Height = newDisplayH;
        }
        if (_markupOverlay != null)
        {
            _markupOverlay.Width = newDisplayW;
            _markupOverlay.Height = newDisplayH;
        }
        if (_imageElement != null)
        {
            _imageElement.Width = newDisplayW;
            _imageElement.Height = newDisplayH;
        }

        // Rough center preservation (good enough for toolbar button)
        double cx = _scrollViewer.Offset.X + _scrollViewer.Viewport.Width / 2;
        double cy = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height / 2;

        double newCx = cx * (_zoom / oldZoom);
        double newCy = cy * (_zoom / oldZoom);

        _scrollViewer.Offset = new Vector(
            Math.Max(0, newCx - _scrollViewer.Viewport.Width / 2),
            Math.Max(0, newCy - _scrollViewer.Viewport.Height / 2)
        );
    }

    public void ZoomOut()
    {
        if (_scrollViewer == null || Source == null) return;

        _lastBaseStretch = null; // user-controlled zoom

        double oldZoom = _zoom;
        _zoom = Math.Max(_zoom / 1.25, 0.05);

        double naturalW = Source.PixelSize.Width;
        double naturalH = Source.PixelSize.Height;

        double newDisplayW = naturalW * _zoom;
        double newDisplayH = naturalH * _zoom;

        if (_contentGrid != null)
        {
            _contentGrid.Width = newDisplayW;
            _contentGrid.Height = newDisplayH;
        }
        if (_markupOverlay != null)
        {
            _markupOverlay.Width = newDisplayW;
            _markupOverlay.Height = newDisplayH;
        }
        if (_imageElement != null)
        {
            _imageElement.Width = newDisplayW;
            _imageElement.Height = newDisplayH;
        }

        double cx = _scrollViewer.Offset.X + _scrollViewer.Viewport.Width / 2;
        double cy = _scrollViewer.Offset.Y + _scrollViewer.Viewport.Height / 2;

        double newCx = cx * (_zoom / oldZoom);
        double newCy = cy * (_zoom / oldZoom);

        _scrollViewer.Offset = new Vector(
            Math.Max(0, newCx - _scrollViewer.Viewport.Width / 2),
            Math.Max(0, newCy - _scrollViewer.Viewport.Height / 2)
        );
    }

    public void FitToView()
    {
        ResetView(fit: true);
    }

    public void SetTool(Tool tool)
    {
        CurrentTool = tool;
    }

    public void ClearMarkups()
    {
        Markups.Clear();
    }

    /// <summary>
    /// Test hook (Byrd / mocks-first, see MarkupImageViewerBaseScaleSizingTests).
    /// Pure decision for base sizing targets. Implementation of the ACs for visible scaled
    /// content (panel rect box for non-None modes so Stretch visibly fills/affects the allocated
    /// viewer panel; natural for 1:1 None).
    /// </summary>
    internal static (double targetW, double targetH, Avalonia.Media.Stretch stretch) ComputeBaseSizingForTest(
        Avalonia.Media.Stretch mode, Avalonia.PixelSize natural, Avalonia.Size view)
    {
        double nw = natural.Width;
        double nh = natural.Height;
        if (mode == Avalonia.Media.Stretch.None)
            return (nw, nh, mode);

        double vw = view.Width > 0 ? view.Width : 100;
        double vh = view.Height > 0 ? view.Height : 100;
        return (vw, vh, mode);
    }

    /// <summary>
    /// Sets the base presentation scale for the image, emulating the original dropdown modes
    /// (None, Stretch, Fit, Fill). Pins the content box to the *actual allocated panel/viewport*
    /// rect for scaling modes and lets Image.Stretch perform the mapping. This guarantees the
    /// bitmap is visibly drawn inside the panel area (Fix for black/empty after dynamic reparent
    /// + layout). Includes viewport timing guard + Dispatcher retry + explicit invalidates.
    /// The interactive zoom/pan/markups layer on top.
    /// </summary>
    public void SetBaseScale(Avalonia.Media.Stretch stretchMode)
    {
        if (Source == null) return;

        // Defensive: elements may not be realized if Source set before template or during reparent
        // before the final arrange of ContentGrid/leftStack. OnTemplateApplied will re-apply when ready.
        if (_imageElement == null || _scrollViewer == null || _contentGrid == null || _markupOverlay == null)
        {
            Dispatcher.UIThread.Post(() => SetBaseScale(stretchMode), DispatcherPriority.Background);
            return;
        }

        double naturalW = Source.PixelSize.Width;
        double naturalH = Source.PixelSize.Height;

        double viewW = _scrollViewer.Viewport.Width;
        double viewH = _scrollViewer.Viewport.Height;

        // Timing guard: after ReconfigureMainLayout (detach/attach Borders into new leftStack or cols)
        // + UpdateLayout the inner ScrollViewer Viewport can still be 0 or stale on first pass.
        // Fall back to our arranged Bounds (or conservative) and schedule a retry at Background
        // so the star-sized panels have settled positive allocation.
        if (viewW < 20 || viewH < 20)
        {
            viewW = Math.Max(viewW, this.Bounds.Width);
            viewH = Math.Max(viewH, this.Bounds.Height);
            if (viewW < 20 || viewH < 20)
            {
                viewW = Math.Max(viewW, 400);
                viewH = Math.Max(viewH, 300);
                Dispatcher.UIThread.Post(() => SetBaseScale(stretchMode), DispatcherPriority.Background);
            }
        }

        double targetW;
        double targetH;

        if (stretchMode == Avalonia.Media.Stretch.None)
        {
            // 1:1 scrollable: explicit natural size on content so ScrollViewer can pan the full image.
            targetW = naturalW;
            targetH = naturalH;

            if (_contentGrid != null)
            {
                _contentGrid.Width = targetW;
                _contentGrid.Height = targetH;
            }
            if (_imageElement != null)
            {
                _imageElement.Width = targetW;
                _imageElement.Height = targetH;
                _imageElement.Stretch = Avalonia.Media.Stretch.None;
            }
            if (_markupOverlay != null)
            {
                _markupOverlay.Width = targetW;
                _markupOverlay.Height = targetH;
            }

            _zoom = 1.0;
        }
        else
        {
            // Base scale modes from the dropdown (Fit/Stretch/UniformToFill etc.).
            // We pin the inner ContentGrid + Image + Overlay to the actual size this viewer control
            // was allocated by the outer layout (the * rows in leftStack/ContentGrid, the Border's
            // inner Grid row 1, etc.). This is the "panel" size the user sees.
            //
            // Then we set Image.Stretch = the requested mode. The bitmap will be drawn (uniformly
            // scaled for Fit, distorted for Stretch, etc.) to exactly fill that box.
            //
            // This guarantees the image pixels are visible inside the dark panel instead of the
            // content box being NaN/collapsed or sized to a stale Viewport that doesn't match the
            // final arranged size after dynamic reparenting.
            //
            // We prefer this.Bounds (the size the parent gave *us*) over the inner ScrollViewer's
            // Viewport because the viewer is the thing placed in the star-sized slot.
            Size targetSize = this.Bounds.Size;
            if (targetSize.Width < 10 || targetSize.Height < 10)
            {
                if (_scrollViewer != null && _scrollViewer.Viewport.Width > 10 && _scrollViewer.Viewport.Height > 10)
                {
                    targetSize = _scrollViewer.Viewport;
                }
                else
                {
                    targetSize = new Size(400, 300);
                    // The size isn't known yet (initial layout or reparent). Schedule a retry.
                    // OnViewerSizeChanged / OnViewportSizeChanged will also call us again when
                    // a real positive size arrives.
                    Dispatcher.UIThread.Post(() => SetBaseScale(stretchMode), DispatcherPriority.Background);
                }
            }

            if (_contentGrid != null)
            {
                _contentGrid.Width = targetSize.Width;
                _contentGrid.Height = targetSize.Height;
            }
            if (_imageElement != null)
            {
                _imageElement.Width = targetSize.Width;
                _imageElement.Height = targetSize.Height;
                _imageElement.Stretch = stretchMode;
            }
            if (_markupOverlay != null)
            {
                _markupOverlay.Width = targetSize.Width;
                _markupOverlay.Height = targetSize.Height;
            }

            // Starting zoom for subsequent wheel/zoom gestures so they feel relative to the
            // current visual scale.
            double effectiveZoom = 1.0;
            if (naturalW > 0 && targetSize.Width > 0)
            {
                effectiveZoom = Math.Min(targetSize.Width / naturalW, targetSize.Height / naturalH);
                if (effectiveZoom <= 0) effectiveZoom = 1.0;
            }
            _zoom = effectiveZoom;
        }

        _scrollViewer!.Offset = new Vector(0, 0);

        RedrawMarkups();

        _lastBaseStretch = stretchMode;

        // Force updates so the bitmap paints inside the ScrollViewer even in freshly reparented
        // dynamic layout (ContentGrid / leftStack / * rows).
        _imageElement?.InvalidateMeasure();
        _imageElement?.InvalidateArrange();
        _imageElement?.InvalidateVisual();
        _contentGrid?.InvalidateMeasure();
        _contentGrid?.InvalidateArrange();
        _contentGrid?.InvalidateVisual();
        _markupOverlay?.InvalidateVisual();
        _scrollViewer?.InvalidateArrange();
        _scrollViewer?.InvalidateVisual();
        this.InvalidateMeasure();
        this.InvalidateArrange();
        this.InvalidateVisual();
    }
}

/// <summary>
/// Data model for a single markup. Coordinates are in original image pixel space.
/// </summary>
public class ImageMarkup
{
    public MarkupType Type { get; set; }
    public Rect Bounds { get; set; }

    // For arrows we store explicit points (head is EndPoint)
    public Point? StartPoint { get; set; }
    public Point? EndPoint { get; set; }

    public string? Text { get; set; }
}

public enum MarkupType
{
    Highlighter,
    TextArea,
    Arrow
}
