namespace AuraScan_Ultrasound_System.Models
{
    public enum ImagingMode
    {
        BMode,
        MMode,
        ColorDoppler,
        PowerDoppler,
        SpectralDoppler,
        Volume3D
    }

    public enum ProbeType
    {
        Linear,
        Convex,
        Phased,
        Endocavity
    }

    public enum TissuePreset
    {
        Abdomen,
        ObGyn,
        Vascular,
        Cardiac,
        SmallParts,
        Renal
    }

    public enum MeasurementType
    {
        Distance,
        Area,
        EllipseArea,
        TraceArea,
        Volume,
        Velocity
    }

    public enum DopplerColorMap
    {
        RedBlue,
        BlueRed,
        Velocity,
        Variance,
        Power
    }

    public enum SegmentationAlgorithm
    {
        RegionGrowing,
        LevelSet,
        Watershed
    }

    public enum DicomOperationType
    {
        Store,
        Query,
        Retrieve,
        Worklist,
        Echo
    }

    public enum CineState
    {
        Stopped,
        Recording,
        Playing,
        Paused
    }

    public enum AcquisitionState
    {
        Idle,
        Scanning,
        Frozen
    }
}
