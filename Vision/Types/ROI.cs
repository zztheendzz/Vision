namespace Vision.Tools
{
    public  class ROI
    {
        /// <summary>
        /// Lớp cơ sở cho vùng quan tâm (ROI), chỉ chứa dữ liệu số thuần túy, 
        /// không phụ thuộc hay giữ object của thư viện bên ngoài như Halcon[cite: 3].
        /// </summary>
        public abstract record Roi
        {
            public string ShapeType { get; init; } = string.Empty;
        }

        /// <summary>
        /// Vùng quan tâm hình chữ nhật (Rectangle/Rotated Rectangle).
        /// </summary>
        public record RectangleRoi(
            double Row,
            double Column,
            double Phi,
            double Length1,
            double Length2) : Roi
        {
            public RectangleRoi() : this(0, 0, 0, 100, 100)
            {
                ShapeType = "Rectangle";
            }
        }

        /// <summary>
        /// Vùng quan tâm hình tròn (Circle).
        /// </summary>
        public record CircleRoi(
            double Row,
            double Column,
            double Radius) : Roi
        {
            public CircleRoi() : this(0, 0, 50)
            {
                ShapeType = "Circle";
            }
        }

        /// <summary>
        /// Vùng quan tâm hình đa giác (Polygon) biểu diễn bằng danh sách các điểm tọa độ 2D.
        /// </summary>
        public record PolygonRoi(Point2D[] Points) : Roi
        {
            public PolygonRoi() : this(Array.Empty<Point2D>())
            {
                ShapeType = "Polygon";
            }
        }

    }
}
