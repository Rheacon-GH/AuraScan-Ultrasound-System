using FellowOakDicom;
using FellowOakDicom.IO.Buffer;
using FellowOakDicom.Imaging;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Dicom
{
    /// <summary>
    /// Builds DICOM datasets conforming to the Ultrasound (US) Image IOD.
    /// Populates all required DICOM tags per DICOM PS3.3 Table A.6-1.
    /// </summary>
    public sealed class DicomImageBuilder
    {
        /// <summary>
        /// Create a complete DICOM file from an ultrasound frame with patient and study information.
        /// Conforms to the US Image IOD (1.2.840.10008.5.1.4.1.1.6.1).
        /// </summary>
        public DicomFile BuildUltrasoundDicom(UltrasoundFrame frame, PatientInfo patient,
            ProbeConfiguration probe, ScanParameters scanParams)
        {
            var dataset = new DicomDataset();

            // --- Patient Module (M) ---
            dataset.AddOrUpdate(DicomTag.PatientName, patient.PatientName);
            dataset.AddOrUpdate(DicomTag.PatientID, patient.PatientId);
            if (patient.DateOfBirth.HasValue)
                dataset.AddOrUpdate(DicomTag.PatientBirthDate, patient.DateOfBirth.Value);
            dataset.AddOrUpdate(DicomTag.PatientSex, patient.Sex);
            if (patient.WeightKg.HasValue)
                dataset.AddOrUpdate(DicomTag.PatientWeight, (decimal)patient.WeightKg.Value);

            // --- General Study Module (M) ---
            string studyUid = string.IsNullOrEmpty(patient.StudyInstanceUid)
                ? DicomUIDGenerator.GenerateDerivedFromUUID().UID
                : patient.StudyInstanceUid;
            dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyUid);
            dataset.AddOrUpdate(DicomTag.StudyDate, patient.StudyDateTime);
            dataset.AddOrUpdate(DicomTag.StudyTime, patient.StudyDateTime);
            dataset.AddOrUpdate(DicomTag.AccessionNumber, patient.AccessionNumber);
            dataset.AddOrUpdate(DicomTag.ReferringPhysicianName, patient.ReferringPhysician);
            dataset.AddOrUpdate(DicomTag.StudyDescription, patient.StudyDescription);
            dataset.AddOrUpdate(DicomTag.StudyID, "1");

            // --- General Series Module (M) ---
            string seriesUid = string.IsNullOrEmpty(patient.SeriesInstanceUid)
                ? DicomUIDGenerator.GenerateDerivedFromUUID().UID
                : patient.SeriesInstanceUid;
            dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, seriesUid);
            dataset.AddOrUpdate(DicomTag.Modality, "US");
            dataset.AddOrUpdate(DicomTag.SeriesNumber, 1);
            dataset.AddOrUpdate(DicomTag.SeriesDescription,
                $"US {frame.Mode} - {probe.ModelName}");
            dataset.AddOrUpdate(DicomTag.PerformingPhysicianName, patient.PerformingPhysician);

            // --- General Equipment Module (M) ---
            dataset.AddOrUpdate(DicomTag.Manufacturer, probe.Manufacturer);
            dataset.AddOrUpdate(DicomTag.InstitutionName, patient.InstitutionName);
            dataset.AddOrUpdate(DicomTag.StationName, "AURASCAN-01");
            dataset.AddOrUpdate(DicomTag.ManufacturerModelName, "AuraScan Ultrasound System");
            dataset.AddOrUpdate(DicomTag.SoftwareVersions, "1.0.0");
            dataset.AddOrUpdate(DicomTag.DeviceSerialNumber, "AS-2024-001");

            // --- General Image Module (M) ---
            string sopInstanceUid = DicomUIDGenerator.GenerateDerivedFromUUID().UID;
            dataset.AddOrUpdate(DicomTag.SOPClassUID, DicomUID.UltrasoundImageStorage);
            dataset.AddOrUpdate(DicomTag.SOPInstanceUID, sopInstanceUid);
            dataset.AddOrUpdate(DicomTag.InstanceNumber, (int)frame.FrameId);
            dataset.AddOrUpdate(DicomTag.ContentDate, frame.Timestamp);
            dataset.AddOrUpdate(DicomTag.ContentTime, frame.Timestamp);
            dataset.AddOrUpdate(DicomTag.AcquisitionDateTime, frame.Timestamp);
            dataset.AddOrUpdate(DicomTag.ImageType, "ORIGINAL", "PRIMARY");

            // --- Image Pixel Module (M) ---
            dataset.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)1);
            dataset.AddOrUpdate(DicomTag.PhotometricInterpretation, "MONOCHROME2");
            dataset.AddOrUpdate(DicomTag.Rows, (ushort)frame.ImageHeight);
            dataset.AddOrUpdate(DicomTag.Columns, (ushort)frame.ImageWidth);
            dataset.AddOrUpdate(DicomTag.BitsAllocated, (ushort)8);
            dataset.AddOrUpdate(DicomTag.BitsStored, (ushort)8);
            dataset.AddOrUpdate(DicomTag.HighBit, (ushort)7);
            dataset.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)0);
            dataset.AddOrUpdate(DicomTag.PlanarConfiguration, (ushort)0);

            // Add pixel data
            if (frame.BmodeImageData != null)
            {
                var buffer = new MemoryByteBuffer(frame.BmodeImageData);
                dataset.AddOrUpdate(new DicomOtherByte(DicomTag.PixelData, buffer));
            }

            // --- US Image Module (M) ---
            dataset.AddOrUpdate(DicomTag.TransducerType, GetTransducerType(probe));
            dataset.AddOrUpdate(DicomTag.NumberOfFrames, "1");

            // US Region Calibration Sequence
            var regionSeq = new DicomSequence(DicomTag.SequenceOfUltrasoundRegions);
            var regionItem = new DicomDataset();
            regionItem.AddOrUpdate(DicomTag.RegionSpatialFormat, (ushort)1); // 2D
            regionItem.AddOrUpdate(DicomTag.RegionDataType, (ushort)1); // Tissue
            regionItem.AddOrUpdate(DicomTag.RegionLocationMinX0, (uint)0);
            regionItem.AddOrUpdate(DicomTag.RegionLocationMinY0, (uint)0);
            regionItem.AddOrUpdate(DicomTag.RegionLocationMaxX1, (uint)(frame.ImageWidth - 1));
            regionItem.AddOrUpdate(DicomTag.RegionLocationMaxY1, (uint)(frame.ImageHeight - 1));
            regionItem.AddOrUpdate(DicomTag.PhysicalDeltaX, scanParams.DepthM * 100.0 / frame.ImageWidth);
            regionItem.AddOrUpdate(DicomTag.PhysicalDeltaY, scanParams.DepthM * 100.0 / frame.ImageHeight);
            regionItem.AddOrUpdate(DicomTag.PhysicalUnitsXDirection, (ushort)3); // cm
            regionItem.AddOrUpdate(DicomTag.PhysicalUnitsYDirection, (ushort)3); // cm
            regionSeq.Items.Add(regionItem);
            dataset.AddOrUpdate(regionSeq);

            // --- Ultrasound-specific tags ---
            dataset.AddOrUpdate(DicomTag.MechanicalIndex, (decimal)frame.MechanicalIndex);
            // Thermal Index — use Soft Tissue variant (0018,0382)
            dataset.AddOrUpdate(DicomTag.SoftTissueThermalIndex, (decimal)frame.ThermalIndex);
            dataset.AddOrUpdate(DicomTag.DepthOfScanField, (int)(scanParams.DepthM * 1000));
            dataset.AddOrUpdate(DicomTag.TransducerData, probe.ModelName);

            // Transfer Syntax
            var file = new DicomFile(dataset);
            file.FileMetaInfo.TransferSyntax = DicomTransferSyntax.ExplicitVRLittleEndian;

            return file;
        }

        private static string GetTransducerType(ProbeConfiguration probe)
        {
            return probe.ProbeType switch
            {
                ProbeType.Convex => "CURVED LINEAR",
                ProbeType.Linear => "LINEAR",
                ProbeType.Phased => "SECTOR_PHASED",
                ProbeType.Endocavity => "CURVED LINEAR",
                _ => "UNKNOWN"
            };
        }
    }
}
