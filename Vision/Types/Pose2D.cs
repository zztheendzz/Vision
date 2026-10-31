namespace Vision.Tools
{
    public  class Pose2D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Theta { get; set; }


        public Pose2D(double x, double y, double theta)
        {
            X = x;
            Y = y;
            Theta = theta;
        }
        public Pose2D() { }
    }
}
