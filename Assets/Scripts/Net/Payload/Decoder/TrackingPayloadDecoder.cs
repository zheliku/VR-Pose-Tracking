using System;
using System.Globalization;
using UnityEngine;
using Debug = UnityEngine.Debug;

public struct TrackingPayload
{
    public RawData ImageData { get; }
    public PoseData PoseData { get; }
    public TrackingPhase Phase { get; }

    public TrackingPayload(RawData imageData, PoseData poseData, TrackingPhase phase)
    {
        ImageData = imageData;
        PoseData = poseData;
        Phase = phase;
    }
}

public interface ITrackingPayloadDecoder
{
    bool TryDecode(byte[][] payloadParts, double timestampMs, out TrackingPayload payload);
}

public class TrackingPayloadDecoder : ITrackingPayloadDecoder
{
    public bool TryDecode(byte[][] payloadParts, double timestampMs, out TrackingPayload payload)
    {
        payload = default;

        if (payloadParts == null || payloadParts.Length != 3)
        {
            return false;
        }

        TrackingPhase phase = payloadParts[0].Length > 0 && payloadParts[0][0] == 1
            ? TrackingPhase.Tracking
            : TrackingPhase.Detecting;

        byte[] colorData = payloadParts[1];
        byte[] poseData = payloadParts[2];

        Matrix4x4? poseMatrix = null;
        if (poseData != null && poseData.Length > 0)
        {
            string poseJson = System.Text.Encoding.UTF8.GetString(poseData);
            if (!string.IsNullOrWhiteSpace(poseJson))
            {
                poseMatrix = ParsePoseMatrix(poseJson);
                if (poseMatrix == null)
                {
                    return false;
                }
            }
        }

        payload = new TrackingPayload(
            new RawData(colorData, timestampMs),
            new PoseData(poseMatrix),
            phase
        );
        return true;
    }

    private static Matrix4x4? ParsePoseMatrix(string json)
    {
        try
        {
            int matrixStart = json.IndexOf("[[", StringComparison.Ordinal);
            int matrixEnd = json.LastIndexOf("]]", StringComparison.Ordinal);
            if (matrixStart == -1 || matrixEnd == -1)
            {
                return null;
            }

            string matrixStr = json.Substring(matrixStart + 1, matrixEnd - matrixStart);
            string[] rows = matrixStr.Split(new[] { "], [", "],[" }, StringSplitOptions.None);
            if (rows.Length != 4)
            {
                return null;
            }

            Matrix4x4 matrix = new Matrix4x4();
            for (int row = 0; row < 4; row++)
            {
                string rowStr = rows[row].Trim('[', ']', ' ');
                string[] values = rowStr.Split(',');
                if (values.Length != 4)
                {
                    return null;
                }

                for (int col = 0; col < 4; col++)
                {
                    if (!float.TryParse(
                        values[col].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float val))
                    {
                        return null;
                    }

                    matrix[row, col] = val;
                }
            }

            return matrix;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TrackingPayloadDecoder] Failed to parse pose matrix: {e.Message}");
            return null;
        }
    }
}
