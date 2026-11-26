using HalconDotNet;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UI.ViewModels.ROI;
using UI.ViewModels.RunAndDisplay;
using Vision.Tools.Locate;

namespace UI.Views
{
    /// <summary>
    /// Khung xem ảnh Halcon. Chỉ VẼ những gì ImageDisplay mô tả, không sở hữu và không Dispose gì.
    /// ViewModel đặt Display về Empty trước khi giải phóng ảnh, nên khung không bao giờ vẽ đối tượng đã chết.
    ///
    /// Nếu có Roi: vẽ thêm một hình kéo/đổi cỡ được (HDrawingObject) trên ảnh. Mỗi lần kéo, báo RoiChangedCommand
    /// với hình mới. Hình đang hiển thị được giữ riêng nên khi ViewModel đặt lại cùng giá trị thì không dựng lại
    /// (tránh làm đứt thao tác kéo).
    /// </summary>
    public partial class ImageViewerPanel : UserControl
    {
        // Màu lần lượt cho các đối tượng vẽ chồng
        private static readonly string[] OverlayColors = { "green", "red", "yellow", "cyan", "magenta", "orange" };

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(ImageViewerPanel),
                                        new PropertyMetadata(""));

        public static readonly DependencyProperty DisplayProperty =
            DependencyProperty.Register(nameof(Display), typeof(ImageDisplay), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null, (d, _) => ((ImageViewerPanel)d).Render()));

        public static readonly DependencyProperty CaptionTextProperty =
            DependencyProperty.Register(nameof(CaptionText), typeof(string), typeof(ImageViewerPanel),
                                        new PropertyMetadata(""));

        public static readonly DependencyProperty RoiProperty =
            DependencyProperty.Register(nameof(Roi), typeof(RoiGeometry), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null, (d, _) => ((ImageViewerPanel)d).SyncRoi(recreate: false)));

        public static readonly DependencyProperty RoiToggleLabelProperty =
            DependencyProperty.Register(nameof(RoiToggleLabel), typeof(string), typeof(ImageViewerPanel),
                                        new PropertyMetadata("", (d, e) =>
                                        {
                                            var p = (ImageViewerPanel)d;
                                            string text = e.NewValue as string ?? "";
                                            p.RoiToggleBox.Content = text;
                                            p.RoiToggleBox.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                                        }));

        public static readonly DependencyProperty RoiToggleCheckedProperty =
            DependencyProperty.Register(nameof(RoiToggleChecked), typeof(bool), typeof(ImageViewerPanel),
                                        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                                            (d, e) => ((ImageViewerPanel)d).RoiToggleBox.IsChecked = (bool)e.NewValue));

        /// <summary>Chữ của ô "đặt ROI ở khung này". Rỗng = không hiện ô.</summary>
        public string RoiToggleLabel { get => (string)GetValue(RoiToggleLabelProperty); set => SetValue(RoiToggleLabelProperty, value); }

        /// <summary>Đang chọn vẽ ROI ở khung này.</summary>
        public bool RoiToggleChecked { get => (bool)GetValue(RoiToggleCheckedProperty); set => SetValue(RoiToggleCheckedProperty, value); }

        private void RoiToggle_Changed(object sender, RoutedEventArgs e) => RoiToggleChecked = RoiToggleBox.IsChecked == true;

        public static readonly DependencyProperty RoiChangedCommandProperty =
            DependencyProperty.Register(nameof(RoiChangedCommand), typeof(ICommand), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null));

        public static readonly DependencyProperty EraseAreasProperty =
            DependencyProperty.Register(nameof(EraseAreas), typeof(string), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null, (d, _) => ((ImageViewerPanel)d).Render()));

        public static readonly DependencyProperty BrushModeProperty =
            DependencyProperty.Register(nameof(BrushMode), typeof(bool), typeof(ImageViewerPanel),
                                        new PropertyMetadata(false, (d, _) => ((ImageViewerPanel)d).UpdateEraseButtons()));

        public static readonly DependencyProperty UndoEraseCommandProperty =
            DependencyProperty.Register(nameof(UndoEraseCommand), typeof(ICommand), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null));

        /// <summary>true: vùng xóa dùng cọ tẩy theo con trỏ (khung Ảnh xử lý) thay cho hình chữ nhật / tròn.</summary>
        public bool BrushMode
        {
            get { return (bool)GetValue(BrushModeProperty); }
            set { SetValue(BrushModeProperty, value); }
        }

        /// <summary>Được gọi khi bấm "Hoàn tác" (bỏ nét tẩy vừa làm).</summary>
        public ICommand? UndoEraseCommand
        {
            get { return (ICommand?)GetValue(UndoEraseCommandProperty); }
            set { SetValue(UndoEraseCommandProperty, value); }
        }

        public static readonly DependencyProperty EraseCommandProperty =
            DependencyProperty.Register(nameof(EraseCommand), typeof(ICommand), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null));

        public static readonly DependencyProperty ClearEraseCommandProperty =
            DependencyProperty.Register(nameof(ClearEraseCommand), typeof(ICommand), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null));

        /// <summary>
        /// Các vùng xóa viền của node đang chọn (chữ, xem ShapeEraseAreas), vẽ màu đỏ lên ảnh.
        /// null: node không có tính năng này, các nút xóa viền ẩn; chuỗi rỗng: có tính năng nhưng chưa có vùng nào.
        /// </summary>
        public string? EraseAreas
        {
            get { return (string?)GetValue(EraseAreasProperty); }
            set { SetValue(EraseAreasProperty, value); }
        }

        /// <summary>Được gọi (tham số là chữ một vùng, vd "R 240 320 20 10 0") khi người dùng bấm Xác nhận xóa.</summary>
        public ICommand? EraseCommand
        {
            get { return (ICommand?)GetValue(EraseCommandProperty); }
            set { SetValue(EraseCommandProperty, value); }
        }

        /// <summary>Được gọi khi người dùng bấm "Bỏ hết vùng xóa".</summary>
        public ICommand? ClearEraseCommand
        {
            get { return (ICommand?)GetValue(ClearEraseCommandProperty); }
            set { SetValue(ClearEraseCommandProperty, value); }
        }

        /// <summary>ROI cần hiển thị để người dùng chỉnh. null: không hiển thị.</summary>
        public RoiGeometry? Roi
        {
            get { return (RoiGeometry?)GetValue(RoiProperty); }
            set { SetValue(RoiProperty, value); }
        }

        /// <summary>Được gọi (tham số là RoiGeometry) mỗi khi người dùng kéo hoặc đổi cỡ ROI.</summary>
        public ICommand? RoiChangedCommand
        {
            get { return (ICommand?)GetValue(RoiChangedCommandProperty); }
            set { SetValue(RoiChangedCommandProperty, value); }
        }

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public ImageDisplay? Display
        {
            get { return (ImageDisplay?)GetValue(DisplayProperty); }
            set { SetValue(DisplayProperty, value); }
        }

        /// <summary>Phần chữ phụ sau tiêu đề, ví dụ " - Threshold".</summary>
        public string CaptionText
        {
            get { return (string)GetValue(CaptionTextProperty); }
            private set { SetValue(CaptionTextProperty, value); }
        }

        private HSmartWindowControlWPF? _window;
        private bool _ready;                       // cửa sổ Halcon đã khởi tạo xong
        private HDrawingObject? _drawing;         // hình ROI đang đặt trên cửa sổ
        private RoiGeometry? _shownRoi;            // hình học của nó (cập nhật ngay khi người dùng kéo)
        private int _shownWidth, _shownHeight;     // cỡ ảnh đang hiển thị: cùng cỡ thì giữ nguyên mức zoom
        private HDrawingObject? _eraseDrawing;     // hình vùng xóa đang đặt (chưa xác nhận)
        private ShapeEraseAreas.Area? _eraseDraft; // hình học của nó, cập nhật khi kéo (dựng lại được sau khi vẽ lại ảnh)

        public ImageViewerPanel()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_window != null) return;

            _window = new HSmartWindowControlWPF();
            _window.HInitWindow += (_, _) =>
            {
                _ready = true;
                Render();                          // có thể đã có ảnh trước khi cửa sổ sẵn sàng
            };
            _window.HMouseDown += (_, e) => BrushDown(e);
            _window.HMouseMove += (_, e) => BrushMove(e);
            _window.HMouseUp += (_, e) => BrushUp(e);
            _window.HMouseWheel += (_, e) => BrushWheel(e);
            ImageHost.Children.Add(_window);
        }

        private void Render()
        {
            var display = Display;
            CaptionText = string.IsNullOrEmpty(display?.Caption) ? "" : "  -  " + display.Caption;

            if (!_ready || _window == null) return;

            // ClearWindow rồi vẽ lại: dựng lại hình ROI sau cùng. Giữ hình người dùng đã kéo (có thể mới hơn Roi
            // vì ViewModel cố ý không đặt lại Roi lúc kéo), nếu không ROI sẽ nhảy về vị trí cũ sau mỗi lần chạy.
            var latestRoi = _shownRoi;
            RemoveDrawing();
            RemoveEraseDrawing();

            try
            {
                var window = _window.HalconWindow;
                window.ClearWindow();

                if (display?.Image == null)
                {
                    SizeText.Text = "Kích thước: -";
                    _shownWidth = _shownHeight = 0;
                    window.DispText("Chưa có ảnh", "window", 12, 12, "gray",
                                    new HTuple("box"), new HTuple("false"));
                    return;
                }

                display.Image.GetImageSize(out int width, out int height);
                if (width != _shownWidth || height != _shownHeight)
                {
                    _window.SetFullImagePart(display.Image);     // ảnh mới khác cỡ: vừa khung
                    _shownWidth = width;
                    _shownHeight = height;
                }
                SizeText.Text = $"Kích thước: {width} x {height}";

                window.DispObj(display.Image);

                window.SetDraw("margin");                        // vùng chỉ vẽ viền để thấy ảnh bên dưới
                window.SetLineWidth(2);
                for (int i = 0; i < display.Overlays.Count; i++)
                {
                    window.SetColor(OverlayColors[i % OverlayColors.Length]);
                    window.DispObj(display.Overlays[i]);
                }

                DrawSavedEraseAreas(window);
            }
            catch (HalconException ex)
            {
                SizeText.Text = "Lỗi hiển thị: " + ex.Message;
            }
            finally
            {
                SyncRoi(recreate: true, preferred: latestRoi);
                PlaceEraseDrawing();
                UpdateEraseButtons();
            }
        }

        // ================= XÓA VIỀN KHI HỌC MẪU =================

        /// <summary>Vẽ các vùng xóa đã lưu (màu đỏ) để thấy chỗ nào không lấy viền.</summary>
        private void DrawSavedEraseAreas(HWindow window)
        {
            var areas = ShapeEraseAreas.Parse(EraseAreas, out _);
            if (areas == null || areas.Count == 0) return;

            window.SetColor("orange");                       // không dùng đỏ: đỏ là màu Region nhận vào của FindShape
            window.SetLineWidth(2);
            foreach (var a in areas)
            {
                using var region = ShapeEraseAreas.MakeRegion(a);
                window.DispObj(region);
            }
        }

        private void UpdateEraseButtons()
        {
            bool supported = EraseAreas != null && Display?.Image != null;
            bool editing = _eraseDraft != null;
            bool brush = BrushMode;

            // Khung có cọ tẩy: bộ nút cọ thay cho nút hình chữ nhật / tròn
            EraseRectButton.Visibility = supported && !editing && !brush ? Visibility.Visible : Visibility.Collapsed;
            EraseCircleButton.Visibility = supported && !editing && !brush ? Visibility.Visible : Visibility.Collapsed;
            BrushToggle.Visibility = supported && brush ? Visibility.Visible : Visibility.Collapsed;
            BrushModeBox.Visibility = supported && brush ? Visibility.Visible : Visibility.Collapsed;
            BrushSizeText.Visibility = supported && brush && BrushToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            EraseUndoButton.Visibility = supported && brush && !string.IsNullOrWhiteSpace(EraseAreas) ? Visibility.Visible : Visibility.Collapsed;
            EraseClearButton.Visibility = supported && !editing && !string.IsNullOrWhiteSpace(EraseAreas)
                ? Visibility.Visible : Visibility.Collapsed;
            EraseOkButton.Visibility = supported && editing ? Visibility.Visible : Visibility.Collapsed;
            EraseCancelButton.Visibility = supported && editing ? Visibility.Visible : Visibility.Collapsed;

            if (!(supported && brush) && BrushToggle.IsChecked == true) BrushToggle.IsChecked = false;   // hết quyền tẩy: tắt cọ, trả lại kéo / zoom
        }

        // ================= TẨY THEO CON TRỎ =================

        private double _brushRadius = 8;                         // pixel ảnh
        private List<(double Row, double Col)>? _stroke;          // nét đang vẽ (null: chưa giữ chuột)
        private (double Row, double Col)? _cursorShown;          // vòng tròn con trỏ đang vẽ (xor) để xóa đi vẽ lại

        private bool BrushOn => BrushMode && BrushToggle.IsChecked == true && _ready && _window != null && Display?.Image != null;

        private void BrushToggle_Changed(object sender, RoutedEventArgs e)
        {
            bool on = BrushToggle.IsChecked == true;
            if (_window != null)
            {
                // Đang tẩy thì kéo chuột không dời ảnh và lăn chuột không zoom (lăn chuột đổi cỡ cọ)
                _window.HMoveContent = !on;
                _window.HZoomContent = on ? HSmartWindowControlWPF.ZoomContent.Off : HSmartWindowControlWPF.ZoomContent.WheelForwardZoomsIn;
            }
            _stroke = null;
            _cursorShown = null;
            BrushSizeText.Text = $"Cỡ: {_brushRadius:0} px";
            UpdateEraseButtons();
            if (!on) Render();
        }

        private void BrushMode_Changed(object sender, SelectionChangedEventArgs e) { }

        private bool BrushWholeContour => BrushModeBox.SelectedIndex == 1;

        private void BrushWheel(HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!BrushOn) return;
            _brushRadius = Math.Clamp(_brushRadius + (e.Delta > 0 ? 2 : -2), 2, 200);
            BrushSizeText.Text = $"Cỡ: {_brushRadius:0} px";
            DrawCursor(e.Row, e.Column);
        }

        private void BrushDown(HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!BrushOn || e.Button != System.Windows.Input.MouseButton.Left) return;
            _stroke = new List<(double, double)> { (e.Row, e.Column) };
            DrawStamp(e.Row, e.Column);
            DrawCursor(e.Row, e.Column);
        }

        private void BrushMove(HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!BrushOn) return;
            if (_stroke != null)
            {
                var last = _stroke[^1];
                if (Math.Sqrt(Math.Pow(e.Row - last.Row, 2) + Math.Pow(e.Column - last.Col, 2)) >= Math.Max(1.0, _brushRadius / 2))
                {
                    _stroke.Add((e.Row, e.Column));
                    EraseCursor();
                    DrawStamp(e.Row, e.Column);
                }
            }
            DrawCursor(e.Row, e.Column);
        }

        private void BrushUp(HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            var stroke = _stroke;
            _stroke = null;
            _cursorShown = null;
            if (stroke == null || !BrushOn) return;

            var areas = BuildBrushAreas(stroke);
            var command = EraseCommand;
            foreach (var a in areas)
                if (command != null && command.CanExecute(ShapeEraseAreas.Format(new[] { a }))) command.Execute(ShapeEraseAreas.Format(new[] { a }));
            Render();                 // ảnh vẽ lại với các nét tẩy đã lưu (Xem mẫu lại để thấy viền mới)
        }

        /// <summary>Chế độ 1: một nét theo đường rê. Chế độ 2: mỗi đường viền bị nét chạm vào thành một nét phủ hết đường viền đó.</summary>
        private List<ShapeEraseAreas.Area> BuildBrushAreas(List<(double Row, double Col)> stroke)
        {
            var result = new List<ShapeEraseAreas.Area>();

            if (!BrushWholeContour)
            {
                var pts = new double[stroke.Count * 2];
                for (int i = 0; i < stroke.Count; i++) { pts[2 * i] = Math.Round(stroke[i].Row, 1); pts[2 * i + 1] = Math.Round(stroke[i].Col, 1); }
                result.Add(new ShapeEraseAreas.Area(false, stroke[0].Row, stroke[0].Col, _brushRadius, 0, 0, pts));
                return result;
            }

            // Viền hiển thị là đối tượng XLD đầu tiên (viền mẫu); mỗi đường viền liền mạch là một phần tử
            if (Display?.Overlays.Count > 0 && Display.Overlays[0] is HXLDCont xld)
            {
                try
                {
                    int count = xld.CountObj();
                    for (int k = 1; k <= count; k++)
                    {
                        using var one = xld.SelectObj(k);
                        one.GetContourXld(out HTuple rows, out HTuple cols);
                        double[] r = rows.ToDArr(), c = cols.ToDArr();
                        if (r.Length == 0 || !Touches(r, c, stroke)) continue;

                        var list = new List<double>();
                        for (int i = 0; i < r.Length; i += 3) { list.Add(Math.Round(r[i], 1)); list.Add(Math.Round(c[i], 1)); }
                        if (list.Count == 0 || (list[^2] != Math.Round(r[^1], 1) || list[^1] != Math.Round(c[^1], 1)))
                        { list.Add(Math.Round(r[^1], 1)); list.Add(Math.Round(c[^1], 1)); }
                        if (list.Count >= 2) result.Add(new ShapeEraseAreas.Area(false, list[0], list[1], 4, 0, 0, list.ToArray()));
                    }
                }
                catch (HalconException) { }
            }
            return result;
        }

        /// <summary>Có điểm nào của đường viền nằm trong bán kính cọ so với một điểm của nét không.</summary>
        private bool Touches(double[] r, double[] c, List<(double Row, double Col)> stroke)
        {
            double limit = _brushRadius * _brushRadius;
            foreach (var s in stroke)
                for (int i = 0; i < r.Length; i++)
                {
                    double dr = r[i] - s.Row, dc = c[i] - s.Col;
                    if (dr * dr + dc * dc <= limit) return true;
                }
            return false;
        }

        /// <summary>Vẽ ngay dấu cọ của nét đang rê (màu cam), chưa lưu; thả chuột mới lưu.</summary>
        private void DrawStamp(double row, double col)
        {
            if (_window == null) return;
            try
            {
                var window = _window.HalconWindow;
                window.SetDraw("fill");
                window.SetColor("orange");
                using var disc = new HRegion();
                disc.GenCircle(row, col, _brushRadius);
                window.DispObj(disc);
                window.SetDraw("margin");
            }
            catch (HalconException) { }
        }

        // Không vẽ vòng tròn con trỏ: phiên bản Halcon này không có HWindow.SetInsert (chế độ xor). Cỡ cọ hiện ở thanh tiêu đề,
        // dấu cọ cam hiện ngay khi rê.
        private void DrawCursor(double row, double col) { }

        private void EraseCursor() { _cursorShown = null; }

        private void EraseUndo_Click(object sender, RoutedEventArgs e)
        {
            var command = UndoEraseCommand;
            if (command != null && command.CanExecute(null)) command.Execute(null);
        }

        private void BeginErase(bool circle)
        {
            if (!_ready || _window == null || Display?.Image == null || _eraseDraft != null) return;

            Display.Image.GetImageSize(out int width, out int height);
            double size = Math.Max(10.0, Math.Min(width, height) / 10.0);
            _eraseDraft = circle
                ? new ShapeEraseAreas.Area(true, height / 2.0, width / 2.0, size, 0, 0)
                : new ShapeEraseAreas.Area(false, height / 2.0, width / 2.0, size, size / 2.0, 0);

            PlaceEraseDrawing();
            UpdateEraseButtons();
        }

        private void PlaceEraseDrawing()
        {
            if (_eraseDraft is not { } a || !_ready || _window == null || Display?.Image == null) return;

            RemoveEraseDrawing();
            try
            {
                var drawing = a.IsCircle
                    ? HDrawingObject.CreateDrawingObject(HDrawingObject.HDrawingObjectType.CIRCLE, a.Row, a.Column, a.A)
                    : HDrawingObject.CreateDrawingObject(HDrawingObject.HDrawingObjectType.RECTANGLE2,
                                                         a.Row, a.Column, a.PhiDeg * Math.PI / 180.0, a.A, a.B);
                drawing.SetDrawingObjectParams("color", "red");
                drawing.OnDrag(OnEraseChanged);
                drawing.OnResize(OnEraseChanged);
                _window.HalconWindow.AttachDrawingObjectToWindow(drawing);
                _eraseDrawing = drawing;
            }
            catch (HalconException ex)
            {
                SizeText.Text = "Lỗi vẽ vùng xóa: " + ex.Message;
            }
        }

        private void OnEraseChanged(HDrawingObject drawing, HWindow window, string type)
        {
            if (_eraseDraft is not { } a) return;
            try
            {
                _eraseDraft = a.IsCircle
                    ? a with { Row = Param(drawing, "row"), Column = Param(drawing, "column"), A = Param(drawing, "radius") }
                    : a with
                    {
                        Row = Param(drawing, "row"),
                        Column = Param(drawing, "column"),
                        PhiDeg = Param(drawing, "phi") * 180.0 / Math.PI,
                        A = Param(drawing, "length1"),
                        B = Param(drawing, "length2")
                    };
            }
            catch (HalconException)
            {
                // Giữ hình trước đó
            }
        }

        private void RemoveEraseDrawing()
        {
            var drawing = _eraseDrawing;
            if (drawing == null) return;
            _eraseDrawing = null;
            try
            {
                _window?.HalconWindow.DetachDrawingObjectFromWindow(drawing);
                drawing.Dispose();
            }
            catch (HalconException)
            {
                // Cửa sổ đã đóng
            }
        }

        private void EndErase()
        {
            RemoveEraseDrawing();
            _eraseDraft = null;
            Render();
        }

        private void EraseRect_Click(object sender, RoutedEventArgs e) => BeginErase(circle: false);
        private void EraseCircle_Click(object sender, RoutedEventArgs e) => BeginErase(circle: true);
        private void EraseCancel_Click(object sender, RoutedEventArgs e) => EndErase();

        private void EraseOk_Click(object sender, RoutedEventArgs e)
        {
            if (_eraseDraft is not { } a) return;
            string text = ShapeEraseAreas.Format(new[] { a });
            var command = EraseCommand;
            EndErase();
            if (command != null && command.CanExecute(text)) command.Execute(text);
        }

        private void EraseClear_Click(object sender, RoutedEventArgs e)
        {
            var command = ClearEraseCommand;
            if (command != null && command.CanExecute(null)) command.Execute(null);
        }

        // ================= ROI KÉO ĐƯỢC =================

        /// <summary>Đặt hình ROI lên ảnh cho khớp với Roi. recreate=false và hình không đổi thì giữ nguyên.</summary>
        private void SyncRoi(bool recreate, RoiGeometry? preferred = null)
        {
            if (!_ready || _window == null) return;

            var roi = Roi == null ? null : (preferred ?? Roi);
            if (roi == null || Display?.Image == null)
            {
                RemoveDrawing();
                return;
            }

            // Giá trị trùng hình đang hiển thị (thường là do chính thao tác kéo vừa gửi lên): không dựng lại
            if (!recreate && _drawing != null && roi.Equals(_shownRoi)) return;

            RemoveDrawing();
            try
            {
                var drawing = CreateDrawing(roi);
                drawing.OnDrag(OnDrawingChanged);
                drawing.OnResize(OnDrawingChanged);
                _window.HalconWindow.AttachDrawingObjectToWindow(drawing);
                _drawing = drawing;
                _shownRoi = roi;
            }
            catch (HalconException ex)
            {
                SizeText.Text = "Lỗi vẽ ROI: " + ex.Message;
            }
        }

        private static HDrawingObject CreateDrawing(RoiGeometry r)
        {
            switch (r.Shape)
            {
                case RoiShape.Circle:
                    return HDrawingObject.CreateDrawingObject(
                        HDrawingObject.HDrawingObjectType.CIRCLE, r.Row, r.Column, r.Radius);

                case RoiShape.RotatedRectangle:
                    return HDrawingObject.CreateDrawingObject(
                        HDrawingObject.HDrawingObjectType.RECTANGLE2,
                        r.Row, r.Column, r.Phi * Math.PI / 180.0, r.Length1, r.Length2);

                default:   // Rectangle: không xoay, dùng hình chữ nhật theo trục (không có tay cầm xoay)
                    return HDrawingObject.CreateDrawingObject(
                        HDrawingObject.HDrawingObjectType.RECTANGLE1,
                        r.Row - r.Length2, r.Column - r.Length1, r.Row + r.Length2, r.Column + r.Length1);
            }
        }

        /// <summary>Halcon gọi khi người dùng kéo hoặc đổi cỡ. Đọc hình mới và báo ViewModel.</summary>
        private void OnDrawingChanged(HDrawingObject drawing, HWindow window, string type)
        {
            var baseRoi = _shownRoi;
            if (baseRoi == null) return;

            RoiGeometry updated;
            try
            {
                switch (baseRoi.Shape)
                {
                    case RoiShape.Circle:
                        updated = baseRoi with
                        {
                            Row = Param(drawing, "row"),
                            Column = Param(drawing, "column"),
                            Radius = Param(drawing, "radius")
                        };
                        break;

                    case RoiShape.RotatedRectangle:
                        updated = baseRoi with
                        {
                            Row = Param(drawing, "row"),
                            Column = Param(drawing, "column"),
                            Phi = Param(drawing, "phi") * 180.0 / Math.PI,
                            Length1 = Param(drawing, "length1"),
                            Length2 = Param(drawing, "length2")
                        };
                        break;

                    default:
                        double r1 = Param(drawing, "row1"), c1 = Param(drawing, "column1");
                        double r2 = Param(drawing, "row2"), c2 = Param(drawing, "column2");
                        updated = baseRoi with
                        {
                            Row = (r1 + r2) / 2.0,
                            Column = (c1 + c2) / 2.0,
                            Length1 = Math.Abs(c2 - c1) / 2.0,
                            Length2 = Math.Abs(r2 - r1) / 2.0
                        };
                        break;
                }
            }
            catch (HalconException)
            {
                return;
            }

            updated = updated.Normalized();
            _shownRoi = updated;                           // ghi ngay để lần so sánh sau không dựng lại hình đang kéo

            // Chuyển về luồng giao diện cho chắc (callback có thể đến từ luồng của Halcon)
            Dispatcher.BeginInvoke(() =>
            {
                var command = RoiChangedCommand;
                if (command != null && command.CanExecute(updated)) command.Execute(updated);
            });
        }

        private static double Param(HDrawingObject drawing, string name)
            => drawing.GetDrawingObjectParams(name).D;

        private void RemoveDrawing()
        {
            var drawing = _drawing;
            if (drawing == null) return;
            _drawing = null;
            _shownRoi = null;

            try
            {
                _window?.HalconWindow.DetachDrawingObjectFromWindow(drawing);
                drawing.Dispose();
            }
            catch (HalconException)
            {
                // Cửa sổ đã đóng: không còn gì để gỡ
            }
        }

        private void FitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_window == null || Display?.Image == null) return;
            _window.SetFullImagePart(Display.Image);
            Render();
        }
    }
}
