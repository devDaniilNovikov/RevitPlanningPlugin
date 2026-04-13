using System;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Двумерная точка (внутренняя модель, единицы — метры).
    /// </summary>
    public class Point2D : IEquatable<Point2D>
    {
        public double X { get; set; }
        public double Y { get; set; }

        public Point2D() { }
        public Point2D(double x, double y) { X = x; Y = y; }

        public double DistanceTo(Point2D other)
            => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));

        public bool Equals(Point2D? other)
        {
            if (other is null) return false;
            const double tol = 1e-6;
            return Math.Abs(X - other.X) < tol && Math.Abs(Y - other.Y) < tol;
        }

        public override bool Equals(object? obj) => Equals(obj as Point2D);
        public override int GetHashCode() => HashCode.Combine(
            Math.Round(X, 6), Math.Round(Y, 6));
        public override string ToString() => $"({X:F4}, {Y:F4})";
    }
}
