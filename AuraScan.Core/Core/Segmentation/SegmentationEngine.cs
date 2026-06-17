using OpenCvSharp;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Segmentation
{
    /// <summary>
    /// ITK-SNAP inspired segmentation engine for ultrasound images.
    /// Implements region growing, geodesic active contour (level set),
    /// and watershed segmentation using OpenCvSharp (OpenCV 4.13).
    /// </summary>
    public sealed class SegmentationEngine : IDisposable
    {
        /// <summary>
        /// Region growing segmentation from a seed point.
        /// Grows a connected region where pixel intensities are within the specified tolerance.
        /// Equivalent to ITK-SNAP's "Seed and Grow" mode.
        /// </summary>
        public SegmentationResult RegionGrowing(byte[] imageData, int width, int height,
            int seedX, int seedY, int lowerTolerance = 20, int upperTolerance = 20)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var srcMat = new Mat(height, width, MatType.CV_8UC1);
            CopyToMat(imageData, srcMat, width, height);

            using var mask = new Mat(height + 2, width + 2, MatType.CV_8UC1, Scalar.All(0));

            // OpenCV floodFill: region growing from seed point
            var seedPoint = new OpenCvSharp.Point(seedX, seedY);
            Cv2.FloodFill(
                srcMat,
                mask,
                seedPoint,
                new Scalar(255),
                out _,
                new Scalar(lowerTolerance),
                new Scalar(upperTolerance),
                FloodFillFlags.Link8 | (FloodFillFlags)(255 << 8));

            // Extract mask (floodFill uses a mask 2 pixels larger on each side)
            using var resultMask = mask[new OpenCvSharp.Rect(1, 1, width, height)];

            // Apply morphological closing to smooth boundary
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
            using var smoothedMask = new Mat();
            Cv2.MorphologyEx(resultMask, smoothedMask, MorphTypes.Close, kernel);

            // Extract contour
            var contour = ExtractContour(smoothedMask);

            // Calculate area
            double pixelArea = Cv2.CountNonZero(smoothedMask);

            var maskData = new byte[width * height];
            CopyFromMat(smoothedMask, maskData, width, height);

            sw.Stop();
            return new SegmentationResult
            {
                Algorithm = SegmentationAlgorithm.RegionGrowing,
                Mask = maskData,
                Contour = contour,
                Width = width,
                Height = height,
                SeedPoint = (seedX, seedY),
                AreaCm2 = 0, // Requires pixel-to-physical calibration
                PerimeterCm = 0,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
            };
        }

        /// <summary>
        /// Geodesic active contour (level set) segmentation.
        /// Equivalent to ITK-SNAP's "Active Contour" / "Snake" mode.
        /// Uses iterative contour evolution driven by edge gradients.
        /// </summary>
        public SegmentationResult LevelSetSegmentation(byte[] imageData, int width, int height,
            int seedX, int seedY, int initialRadius = 20,
            int iterations = 100, double propagationWeight = 1.0, double curvatureWeight = 0.5)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var srcMat = new Mat(height, width, MatType.CV_8UC1);
            CopyToMat(imageData, srcMat, width, height);

            // 1. Compute edge-based speed function
            using var blurred = new Mat();
            Cv2.GaussianBlur(srcMat, blurred, new OpenCvSharp.Size(5, 5), 1.5);

            using var gradX = new Mat();
            using var gradY = new Mat();
            Cv2.Sobel(blurred, gradX, MatType.CV_64FC1, 1, 0);
            Cv2.Sobel(blurred, gradY, MatType.CV_64FC1, 0, 1);

            using var gradMag = new Mat(height, width, MatType.CV_64FC1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double gx = gradX.At<double>(y, x);
                    double gy = gradY.At<double>(y, x);
                    double mag = Math.Sqrt(gx * gx + gy * gy);
                    // Edge stopping function: g(I) = 1 / (1 + |∇I|²)
                    gradMag.Set(y, x, 1.0 / (1.0 + mag * mag / 1000.0));
                }
            }

            // 2. Initialize level set as signed distance function
            var phi = new double[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double dx = x - seedX;
                    double dy = y - seedY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    phi[y, x] = dist - initialRadius; // Negative inside, positive outside
                }
            }

            // 3. Evolve level set using narrow-band method
            for (int iter = 0; iter < iterations; iter++)
            {
                var newPhi = new double[height, width];

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        // Only process near the zero level set (narrow band)
                        if (Math.Abs(phi[y, x]) > initialRadius * 2) 
                        {
                            newPhi[y, x] = phi[y, x];
                            continue;
                        }

                        // Compute curvature (mean curvature of level set)
                        double phiX = (phi[y, x + 1] - phi[y, x - 1]) / 2.0;
                        double phiY = (phi[y + 1, x] - phi[y - 1, x]) / 2.0;
                        double phiXX = phi[y, x + 1] - 2.0 * phi[y, x] + phi[y, x - 1];
                        double phiYY = phi[y + 1, x] - 2.0 * phi[y, x] + phi[y - 1, x];
                        double phiXY = (phi[y + 1, x + 1] - phi[y + 1, x - 1] -
                                       phi[y - 1, x + 1] + phi[y - 1, x - 1]) / 4.0;

                        double gradNorm = Math.Sqrt(phiX * phiX + phiY * phiY);
                        double curvature = 0;
                        if (gradNorm > 1e-10)
                        {
                            curvature = (phiXX * phiY * phiY - 2.0 * phiX * phiY * phiXY +
                                        phiYY * phiX * phiX) / (gradNorm * gradNorm * gradNorm);
                        }

                        // Speed function from edge map
                        double speed = gradMag.At<double>(y, x);

                        // Level set evolution equation:
                        // ∂φ/∂t = g(I) · (propagation + curvature·κ) · |∇φ|
                        double dt = 0.5;
                        double evolution = speed * (propagationWeight + curvatureWeight * curvature) * gradNorm;
                        newPhi[y, x] = phi[y, x] - dt * evolution;
                    }
                }

                phi = newPhi;
            }

            // 4. Extract zero level set as binary mask
            using var resultMask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (phi[y, x] <= 0)
                        resultMask.Set<byte>(y, x, 255);
                }
            }

            // Smooth with morphological operations
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
            using var smoothed = new Mat();
            Cv2.MorphologyEx(resultMask, smoothed, MorphTypes.Close, kernel);
            Cv2.MorphologyEx(smoothed, smoothed, MorphTypes.Open, kernel);

            var contour = ExtractContour(smoothed);
            var maskData = new byte[width * height];
            CopyFromMat(smoothed, maskData, width, height);

            sw.Stop();
            return new SegmentationResult
            {
                Algorithm = SegmentationAlgorithm.LevelSet,
                Mask = maskData,
                Contour = contour,
                Width = width,
                Height = height,
                SeedPoint = (seedX, seedY),
                AreaCm2 = 0,
                PerimeterCm = 0,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
            };
        }

        /// <summary>
        /// Watershed segmentation for automatic region delineation.
        /// Equivalent to ITK-SNAP's pre-segmentation/region splitting mode.
        /// </summary>
        public SegmentationResult WatershedSegmentation(byte[] imageData, int width, int height,
            int seedX, int seedY, double threshold = 0.3)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var srcMat = new Mat(height, width, MatType.CV_8UC1);
            CopyToMat(imageData, srcMat, width, height);

            // 1. Pre-processing: bilateral filter preserves edges
            using var filtered = new Mat();
            Cv2.BilateralFilter(srcMat, filtered, 9, 75, 75);

            // 2. Compute gradient magnitude for watershed markers
            using var gradX = new Mat();
            using var gradY = new Mat();
            Cv2.Sobel(filtered, gradX, MatType.CV_64FC1, 1, 0);
            Cv2.Sobel(filtered, gradY, MatType.CV_64FC1, 0, 1);

            using var gradMag = new Mat();
            Cv2.Magnitude(gradX, gradY, gradMag);
            using var gradMag8 = new Mat();
            gradMag.ConvertTo(gradMag8, MatType.CV_8UC1);

            // 3. Create markers for watershed
            using var thresholded = new Mat();
            Cv2.Threshold(filtered, thresholded, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            // Distance transform for foreground markers
            using var distTransform = new Mat();
            Cv2.DistanceTransform(thresholded, distTransform, DistanceTypes.L2, DistanceTransformMasks.Mask5);
            using var distNormalized = new Mat();
            Cv2.Normalize(distTransform, distNormalized, 0, 1.0, NormTypes.MinMax);

            using var foreground = new Mat();
            Cv2.Threshold(distNormalized, foreground, threshold, 255, ThresholdTypes.Binary);
            foreground.ConvertTo(foreground, MatType.CV_8UC1);

            // Background markers (sure background via dilation)
            using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(7, 7));
            using var background = new Mat();
            Cv2.Dilate(thresholded, background, dilateKernel, iterations: 3);

            // Unknown region
            using var unknown = new Mat();
            Cv2.Subtract(background, foreground, unknown);

            // Create markers
            var markers = new Mat();
            Cv2.ConnectedComponents(foreground, markers);
            Cv2.Add(markers, new Scalar(1), markers); // Shift labels so background=1
            // Set unknown region to 0 (unknown for watershed)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (unknown.At<byte>(y, x) == 255)
                        markers.Set(y, x, 0);

            // 4. Apply watershed
            using var colorSrc = new Mat();
            Cv2.CvtColor(srcMat, colorSrc, ColorConversionCodes.GRAY2BGR);
            Cv2.Watershed(colorSrc, markers);

            // 5. Extract the region containing the seed point
            int seedLabel = markers.At<int>(seedY, seedX);
            using var resultMask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (markers.At<int>(y, x) == seedLabel)
                        resultMask.Set<byte>(y, x, 255);

            var contour = ExtractContour(resultMask);
            var maskData = new byte[width * height];
            CopyFromMat(resultMask, maskData, width, height);
            markers.Dispose();

            sw.Stop();
            return new SegmentationResult
            {
                Algorithm = SegmentationAlgorithm.Watershed,
                Mask = maskData,
                Contour = contour,
                Width = width,
                Height = height,
                SeedPoint = (seedX, seedY),
                AreaCm2 = 0,
                PerimeterCm = 0,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
            };
        }

        /// <summary>
        /// Calibrate measurement results with pixel-to-physical-space mapping.
        /// </summary>
        public void CalibrateMeasurements(SegmentationResult result,
            double pixelsPerCmX, double pixelsPerCmY)
        {
            if (result.Mask == null) return;

            // Count pixels in mask
            int pixelCount = 0;
            for (int i = 0; i < result.Mask.Length; i++)
                if (result.Mask[i] > 0) pixelCount++;

            result.AreaCm2 = pixelCount / (pixelsPerCmX * pixelsPerCmY);

            // Perimeter from contour length
            double perimeter = 0;
            for (int i = 0; i < result.Contour.Count; i++)
            {
                var p1 = result.Contour[i];
                var p2 = result.Contour[(i + 1) % result.Contour.Count];
                double dx = (p2.X - p1.X) / pixelsPerCmX;
                double dy = (p2.Y - p1.Y) / pixelsPerCmY;
                perimeter += Math.Sqrt(dx * dx + dy * dy);
            }
            result.PerimeterCm = perimeter;
        }

        private static List<(double X, double Y)> ExtractContour(Mat mask)
        {
            Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxTC89L1);

            var result = new List<(double X, double Y)>();
            if (contours.Length > 0)
            {
                // Find the largest contour
                int largestIdx = 0;
                double largestArea = 0;
                for (int i = 0; i < contours.Length; i++)
                {
                    double area = Cv2.ContourArea(contours[i]);
                    if (area > largestArea)
                    {
                        largestArea = area;
                        largestIdx = i;
                    }
                }

                foreach (var pt in contours[largestIdx])
                    result.Add((pt.X, pt.Y));
            }

            return result;
        }

        private static void CopyToMat(byte[] data, Mat mat, int width, int height)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    mat.Set<byte>(y, x, data[y * width + x]);
        }

        private static void CopyFromMat(Mat mat, byte[] data, int width, int height)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    data[y * width + x] = mat.At<byte>(y, x);
        }

        public void Dispose() { }
    }
}
