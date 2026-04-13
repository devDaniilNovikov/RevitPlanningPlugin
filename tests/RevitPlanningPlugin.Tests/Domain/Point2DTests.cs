using System;
using Xunit;
using RevitPlanningPlugin.Models.Domain;

namespace RevitPlanningPlugin.Tests.Domain
{
    public class Point2DTests
    {
        // ——— Конструкторы ———

        [Fact]
        public void DefaultConstructor_SetsZero()
        {
            var pt = new Point2D();
            Assert.Equal(0.0, pt.X);
            Assert.Equal(0.0, pt.Y);
        }

        [Fact]
        public void ParameterizedConstructor_SetsValues()
        {
            var pt = new Point2D(3.0, -4.5);
            Assert.Equal(3.0, pt.X);
            Assert.Equal(-4.5, pt.Y);
        }

        // ——— DistanceTo ———

        [Fact]
        public void DistanceTo_SamePoint_ReturnsZero()
        {
            var pt = new Point2D(1.0, 2.0);
            Assert.Equal(0.0, pt.DistanceTo(pt), precision: 10);
        }

        [Fact]
        public void DistanceTo_KnownDistance_Correct()
        {
            var a = new Point2D(0.0, 0.0);
            var b = new Point2D(3.0, 4.0);
            Assert.Equal(5.0, a.DistanceTo(b), precision: 10);
        }

        [Fact]
        public void DistanceTo_IsSymmetric()
        {
            var a = new Point2D(1.0, 2.0);
            var b = new Point2D(4.0, 6.0);
            Assert.Equal(a.DistanceTo(b), b.DistanceTo(a), precision: 10);
        }

        // ——— Equals ———

        [Fact]
        public void Equals_IdenticalPoints_ReturnsTrue()
        {
            var a = new Point2D(1.0, 2.0);
            var b = new Point2D(1.0, 2.0);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_WithinTolerance_ReturnsTrue()
        {
            // Допуск 1e-6
            var a = new Point2D(1.0, 2.0);
            var b = new Point2D(1.0 + 5e-7, 2.0 - 5e-7);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_BeyondTolerance_ReturnsFalse()
        {
            var a = new Point2D(1.0, 2.0);
            var b = new Point2D(1.0 + 1e-5, 2.0);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_Null_ReturnsFalse()
        {
            var a = new Point2D(1.0, 2.0);
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void Equals_BoxedObject_WorksViaOverride()
        {
            var a = new Point2D(1.0, 2.0);
            var b = new Point2D(1.0, 2.0);
            Assert.True(a.Equals((object)b));
        }

        // ——— GetHashCode ———

        [Fact]
        public void GetHashCode_EqualPoints_SameHash()
        {
            var a = new Point2D(1.0, 2.0);
            var b = new Point2D(1.0, 2.0);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        // ——— ToString ———

        [Fact]
        public void ToString_ContainsCoordinates()
        {
            var pt = new Point2D(1.2345, -6.7890);
            var str = pt.ToString();
            Assert.Contains("1.2345", str);
            Assert.Contains("-6.7890", str);
        }
    }
}
