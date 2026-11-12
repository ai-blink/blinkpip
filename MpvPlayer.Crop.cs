using System.Globalization;

namespace blinkpip;

public partial class MpvPlayer
{
    public bool HasCropVideo => GetDouble("video-params/w") is > 0;
    public CropValues GetCrop() => new CropValues(Math.Pow(2, GetDouble("video-zoom") ?? 0),
        GetDouble("video-pan-x") ?? 0, GetDouble("video-pan-y") ?? 0).Normalize();

    public bool ApplyCrop(CropValues values)
    {
        var v = values.Normalize();
        string Number(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
        double previousZoom = GetDouble("video-zoom") ?? 0;
        double previousX = GetDouble("video-pan-x") ?? 0;
        double previousY = GetDouble("video-pan-y") ?? 0;
        int zoom = SetProperty("video-zoom", Number(v.MpvZoom));
        int x = SetProperty("video-pan-x", Number(v.PanX));
        int y = SetProperty("video-pan-y", Number(v.PanY));
        if (zoom >= 0 && x >= 0 && y >= 0) return true;
        // Best effort rollback; callers re-read the actual properties even if rollback fails.
        SetProperty("video-zoom", Number(previousZoom));
        SetProperty("video-pan-x", Number(previousX));
        SetProperty("video-pan-y", Number(previousY));
        return false;
    }
}
