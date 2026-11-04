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

        public static readonly DependencyProperty RoiChangedCommandProperty =
            DependencyProperty.Register(nameof(RoiChangedCommand), typeof(ICommand), typeof(ImageViewerPanel),
                                        new PropertyMetadata(null));

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
            }
            catch (HalconException ex)
            {
                SizeText.Text = "Lỗi hiển thị: " + ex.Message;
            }
            finally
            {
                SyncRoi(recreate: true, preferred: latestRoi);
            }
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
