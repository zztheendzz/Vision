using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UI.ViewModels;

namespace UI.View
{
    /// <summary>
    /// Khung xem ảnh Halcon. Chỉ VẼ những gì ImageDisplay mô tả, không sở hữu và không Dispose gì.
    /// ViewModel đặt Display về Empty trước khi giải phóng ảnh, nên khung không bao giờ vẽ đối tượng đã chết.
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
        }

        private void FitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_window == null || Display?.Image == null) return;
            _window.SetFullImagePart(Display.Image);
            Render();
        }
    }
}
