using OscarWatch.Core.Models;

namespace OscarWatch.Rotator;

public interface IRotatorDriver : IDisposable
{
    void Open();
    void SetPosition(double azimuthDeg, double elevationDeg, RotatorSettings settings);
    void Stop();
    (int? Azimuth, int? Elevation) GetPosition();
}
